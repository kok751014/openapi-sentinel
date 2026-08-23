# Generic PowerShell usage of OpenAPI Sentinel (Azure DevOps / PowerShell CI steps).
$ErrorActionPreference = "Stop"

$old = "git:origin/main:contracts/openapi.yaml"
$new = "contracts/openapi.yaml"
$threshold = if ($env:SENTINEL_FAIL_ON) { $env:SENTINEL_FAIL_ON } else { "high" }

New-Item -ItemType Directory -Force -Path artifacts | Out-Null

$args = @(
  "compare", $old, $new,
  "--fail-on", $threshold,
  "--json",     "artifacts/api-diff.json",
  "--markdown", "artifacts/api-diff.md",
  "--html",     "artifacts/api-diff.html",
  "--no-color"
)

& openapi-sentinel @args
$code = $LASTEXITCODE

if (Test-Path "artifacts/api-diff.json") {
  $report = Get-Content "artifacts/api-diff.json" -Raw | ConvertFrom-Json
  Write-Host ("OpenAPI Sentinel: {0} breaking change(s), exit code {1}." -f `
    $report.summary.breaking, $code)
}

exit $code
