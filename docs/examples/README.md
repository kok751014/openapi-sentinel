# Example outputs

Generated from the [Order Management sample contracts](../../samples/basic/) —
`v1.yaml` compared against the intentionally breaking `v2-breaking.yaml`:

| File | What it shows |
|------|---------------|
| [api-diff.md](api-diff.md) | Full Markdown report: summary tables, every finding with explanation, suppressed section |
| [pr-summary.md](pr-summary.md) | The compact block posted to pull requests / `$GITHUB_STEP_SUMMARY` |
| [CHANGELOG-sample.md](CHANGELOG-sample.md) | Generated API changelog with Breaking vs Non-breaking sections |
| [api-diff.html](api-diff.html) | The flagship single-file interactive report: filters, search, collapsible findings — works offline, no CDN |
| [callbacks-api-diff.md](callbacks-api-diff.md) | Callback-aware Markdown report, generated from the [Notification Hooks sample](../../samples/callbacks/): callback added/target changed (CG1011/CG1013), a required property inside a callback payload (CG5015), and a response-header type change (CG5001) |
| [callbacks-api-diff.html](callbacks-api-diff.html) | The same callbacks scenario in the interactive single-file HTML report — filters, search, collapsible findings, works offline |
| [callbacks-pr-summary.md](callbacks-pr-summary.md) | The compact PR block for the callbacks pair |

For **Bruno collection sync** examples (OpenCollection YAML output, sync reports,
business scenarios), see [`../../samples/bruno-sync/`](../../samples/bruno-sync/) — its
`expected/` folder contains a committed collection + reports reproduced byte-for-byte by
the documented steps.

To produce an interactive HTML version locally:

```bash
openapi-sentinel compare ../../samples/basic/v1.yaml ../../samples/basic/v2-breaking.yaml --html report.html
```

These files are committed so reviewers can inspect real output without installing anything.
