# PowerShell release script for OpenAPI Sentinel.
# Produces self-contained binaries, checksums and the NuGet global-tool package.
#
# Usage: ./build/release.ps1 [[-Version] "1.0.0"]
[CmdletBinding()]
param(
    [string]$Version = $(if ($env:SENTINEL_VERSION) { $env:SENTINEL_VERSION } else { "1.0.0" })
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet is not on PATH"
}

Write-Host "==> Building OpenAPI Sentinel $Version"
& dotnet build -c Release "/p:SentinelVersion=$Version"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$rids = @(
    @{ Rid = "win-x64";      Archive = "zip" },
    @{ Rid = "win-arm64";    Archive = "zip" },
    @{ Rid = "linux-x64";    Archive = "tar.gz" },
    @{ Rid = "linux-arm64";  Archive = "tar.gz" },
    @{ Rid = "osx-x64";      Archive = "tar.gz" },
    @{ Rid = "osx-arm64";    Archive = "tar.gz" }
)

$out = Join-Path $root "dist/$Version"
New-Item -ItemType Directory -Force -Path "$out/pkg", "$out/stage" | Out-Null

foreach ($entry in $rids) {
    $rid = $entry.Rid
    Write-Host "==> Publishing self-contained $rid"
    & dotnet publish src/OpenApiSentinel.Cli -c Release `
        --no-build --no-restore `
        -r $rid --self-contained true `
        "/p:SentinelVersion=$Version" `
        -o "$out/stage/$rid"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    # Normalize the executable name to the product command.
    $exeName = if ($entry.Archive -eq "zip") { "openapi-sentinel.exe" } else { "openapi-sentinel" }
    Move-Item (Join-Path "$out/stage/$rid" "OpenApiSentinel.Cli$(if ($entry.Archive -eq 'zip') { '.exe' })") `
              (Join-Path "$out/stage/$rid" $exeName) -Force

    $name = "openapi-sentinel-$Version-$rid"
    Push-Location "$out/stage/$rid"
    try {
        if ($entry.Archive -eq "zip") {
            Compress-Archive -Path * -DestinationPath "$out/$name.zip" -Force
        }
        else {
            & tar -czf "$out/$name.tar.gz" .
        }
    }
    finally { Pop-Location }
}

Write-Host "==> Packing .NET global tool"
& dotnet pack src/OpenApiSentinel.Cli -c Release --no-build "/p:SentinelVersion=$Version" -o "$out/pkg"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "==> Checksums (SHA256)"
Get-ChildItem $out -File -Include *.zip, *.tar.gz -Recurse -Depth 0 |
    ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "{0}  {1}" -f $hash, $_.Name
    } | Set-Content "$out/SHA256SUMS.txt"

Write-Host "==> Done. Artifacts in $out"
Get-ChildItem $out -File | Select-Object Name, Length
