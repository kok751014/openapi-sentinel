#!/usr/bin/env python3
"""Regenerates docs/rules.md from the rule registry.

Usage:
    python3 build/gen-rules-doc.py [--cli <path-to-OpenApiSentinel.Cli.dll>]

Runs `openapi-sentinel rules --format json` (building nothing; expects a built CLI)
and renders docs/rules.md. Run after adding or changing any rule.
"""
import json
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CLI = ROOT / "src/OpenApiSentinel.Cli/bin/Release/net10.0/OpenApiSentinel.Cli.dll"

CATEGORY_ORDER = ["Endpoint", "Parameter", "RequestBody", "Response", "Schema", "Security", "Metadata", "Engine"]


def fetch_rules(cli: str) -> list[dict]:
    out = subprocess.run(
        ["dotnet", cli, "rules", "--format", "json"],
        capture_output=True, text=True, check=True,
    )
    return json.loads(out.stdout)


def render(rules: list[dict]) -> str:
    cats: dict[str, list[dict]] = {}
    for rule in rules:
        cats.setdefault(rule["category"], []).append(rule)

    core = [r for r in rules if not r["id"].startswith("CG9")]
    meta = [r for r in rules if r["id"].startswith("CG9")]

    lines = [
        "# Rule catalog",
        "",
        f"OpenAPI Sentinel ships **{len(core)} built-in rules** with stable IDs (`CGxxxx`)",
        f"plus **{len(meta)} engine meta-checks** ({', '.join('`' + m['id'] + '`' for m in sorted(meta, key=lambda x: x['id']))})",
        "that document engine-emitted process findings.",
        "Rule IDs never change meaning; new rules get fresh IDs. This file is generated from the",
        "rule registry (`openapi-sentinel rules --format json`), so it cannot drift from the binary.",
        "",
        "Regenerate with:",
        "",
        "```bash",
        "python3 build/gen-rules-doc.py",
        "```",
        "",
        "> `openapi-sentinel explain CG2003` shows this exact information for one rule, including",
        "> breaking/non-breaking examples and limitations.",
        "",
    ]

    order = [c for c in CATEGORY_ORDER if c in cats] + sorted(c for c in cats if c not in CATEGORY_ORDER)
    for cat in order:
        rs = cats[cat]
        lines += [f"## {cat}", "", "| Rule | Title | Default severity | Classification |",
                  "|------|-------|------------------|----------------|"]
        for r in sorted(rs, key=lambda x: x["id"]):
            lines.append(f"| `{r['id']}` | {r['title']} | {r['defaultSeverity']} | {r['compatibility']} |")
        lines.append("")
        for r in sorted(rs, key=lambda x: x["id"]):
            kinds = ", ".join(f"`{k}`" for k in r["changeKinds"]) or "(engine-emitted)"
            lines += [
                f"### `{r['id']}` — {r['title']}",
                "",
                r["description"],
                "",
                f"- **Default severity:** {r['defaultSeverity']}  ",
                f"- **Classification:** {r['compatibility']}  ",
                f"- **Change kinds:** {kinds}",
                "",
            ]
    return "\n".join(lines)


def main() -> int:
    cli = DEFAULT_CLI
    args = sys.argv[1:]
    if "--cli" in args:
        cli = args[args.index("--cli") + 1]
    elif not cli.exists():
        print(f"error: built CLI not found at {cli}; pass --cli <path> or build first", file=sys.stderr)
        return 1

    (ROOT / "docs/rules.md").write_text(render(fetch_rules(str(cli))))
    print("docs/rules.md regenerated")
    return 0


if __name__ == "__main__":
    sys.exit(main())
