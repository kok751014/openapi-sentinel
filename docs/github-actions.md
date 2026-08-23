# GitHub Actions

Two integration levels are provided:

## 1. Composite action (recommended)

`action/action.yml` in this repository wraps install + compare + outputs.

```yaml
- uses: openapi-sentinel/openapi-sentinel-action@v1
  with:
    base: git:origin/main:openapi.yaml
    head: openapi.yaml
    fail-on: high          # critical|high|medium|low|verify|info|all
    report-dir: sentinel-reports
    # config: openapi-sentinel.yml        # optional
    # allow-remote: true                  # optional
```

**Outputs:** `breaking-count`, `high-count`, `medium-count`, `recommended-version`,
`report-path`.

Behaviour:

- Installs the `OpenApiSentinel` .NET global tool (falls back to building from source).
- Runs the comparison and writes `api-diff.json|md|html` into `report-dir`.
- Emits GitHub `::error::` annotations for breaking/potentially-breaking findings (capped at 20).
- Appends the compact PR summary to `$GITHUB_STEP_SUMMARY` automatically (visible on the job page).
- Exits non-zero when the policy threshold is violated — the job fails like any test step.
- A complete example workflow lives at [samples/github-actions/workflow.yml](../samples/github-actions/workflow.yml).

## 2. Plain step

```yaml
- uses: actions/setup-dotnet@v4
  with: { dotnet-version: 10.0.x }
- run: dotnet tool install --global OpenApiSentinel || true
- run: |
    set +e
    "$HOME/.dotnet/tools/openapi-sentinel" compare \
      git:origin/main:openapi.yaml openapi.yaml \
      --fail-on high --html sentinel/api-diff.html --no-color
    echo "exit=$?" >> "$GITHUB_OUTPUT"
```

## PR summaries

The markdown report plus `MarkdownReporter.RenderPrSummary` output are designed for
`$GITHUB_STEP_SUMMARY` or a PR comment; see the sample workflow's "Post PR summary" step.

## Repo CI

This repository's own [.github/workflows/ci.yml](../.github/workflows/ci.yml) builds on three
OSes, runs all tests, executes both sample scenarios (safe passes, breaking fails with exit 1),
and packs the NuGet tool as an artifact.
