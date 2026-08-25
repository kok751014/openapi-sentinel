#!/usr/bin/env bash
# Builds versioned OpenAPI Sentinel release artifacts:
#   - self-contained binaries for 6 RIDs (zip/tar.gz + SHA256SUMS)
#   - NuGet global-tool package
#   - RELEASE_NOTES.md skeleton
#
# Usage: ./build/release.sh [version]
# Version defaults to 1.0.0 or $SENTINEL_VERSION.
set -euo pipefail

cd "$(dirname "$0")/.."
ROOT="$(pwd)"
VERSION="${1:-${SENTINEL_VERSION:-1.0.0}}"
OUT="$ROOT/dist/$VERSION"

export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
command -v dotnet >/dev/null || { echo "error: dotnet not on PATH" >&2; exit 5; }

echo "==> Building OpenAPI Sentinel $VERSION"
dotnet build -c Release /p:SentinelVersion="$VERSION"

# Creates a zip without requiring the 'zip' binary (falls back to python3).
make_zip() {
  local dir="$1" out="$2"
  if command -v zip >/dev/null 2>&1; then
    (cd "$dir" && zip -qr "$out" .)
  elif command -v python3 >/dev/null 2>&1; then
    python3 - "$dir" "$out" <<'ZIPFALLBACK'
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, "w", zipfile.ZIP_DEFLATED) as zf:
    for root, _, files in os.walk(src):
        for f in files:
            p = os.path.join(root, f)
            zf.write(p, os.path.relpath(p, src))
ZIPFALLBACK
  else
    echo "error: need 'zip' or 'python3' available to create Windows archives" >&2
    return 1
  fi
}

RIDS=(
  "win-x64 zip"
  "win-arm64 zip"
  "linux-x64 tar.gz"
  "linux-arm64 tar.gz"
  "osx-x64 tar.gz"
  "osx-arm64 tar.gz"
)

mkdir -p "$OUT/pkg" "$OUT/stage"
rm -rf "$OUT/stage"/*

for entry in "${RIDS[@]}"; do
  set -- $entry
  rid="$1"; archive="${2}"
  echo "==> Publishing self-contained $rid"
  # Publish performs its own RID-specific restore/build; --no-build would fail
  # because the earlier solution build has no RID-specific assets.
  dotnet publish src/OpenApiSentinel.Cli -c Release \
    -r "$rid" --self-contained true \
    /p:SentinelVersion="$VERSION" \
    -o "$OUT/stage/$rid"

  # Normalize the executable name to the product command for direct use.
  if [ "$archive" = "zip" ]; then
    mv "$OUT/stage/$rid/OpenApiSentinel.Cli.exe" "$OUT/stage/$rid/openapi-sentinel.exe" 2>/dev/null || true
  else
    mv "$OUT/stage/$rid/OpenApiSentinel.Cli" "$OUT/stage/$rid/openapi-sentinel" 2>/dev/null || true
  fi

  name="openapi-sentinel-$VERSION-$rid"
  pushd "$OUT/stage/$rid" >/dev/null
  if [ "$archive" = "zip" ]; then
    make_zip "$OUT/stage/$rid" "$OUT/$name.zip"
  else
    tar -czf "$OUT/$name.tar.gz" .
  fi
  popd >/dev/null
done

echo "==> Packing .NET global tool"
dotnet pack src/OpenApiSentinel.Cli -c Release \
  /p:SentinelVersion="$VERSION" \
  -o "$OUT/pkg"

echo "==> Checksums"
pushd "$OUT" >/dev/null
rm -f SHA256SUMS
find . -maxdepth 1 \( -name '*.zip' -o -name '*.tar.gz' \) -print0 |
  xargs -0 sha256sum | sed 's| \./| |' > SHA256SUMS
popd >/dev/null

cat > "$OUT/RELEASE_NOTES.md" <<EOF
# OpenAPI Sentinel $VERSION

OpenAPI Sentinel is an offline-capable OpenAPI contract compatibility and
breaking-change detection tool for engineering teams and CI/CD pipelines.

## Highlights

- Semantic OpenAPI contract comparison instead of plain text or JSON diffing.
- Direction-aware request and response compatibility analysis.
- Breaking-change severity and compatibility classification.
- Security contract change detection.
- Suppressions, suppression expiration, and baseline workflows.
- Semantic version recommendations and API changelog generation.
- Bruno collection sync with preservation-first incremental merging and
  auto-generated Traditional-Chinese test scenarios (see docs/bruno-sync.md).
- Stable CI-friendly exit codes.
- Human-readable and machine-readable reports.
- See docs/rules.md for the complete compatibility rule catalog.

## Supported Contracts

- Swagger / OpenAPI 2.0
- OpenAPI 3.0.x
- OpenAPI 3.1.x
- OpenAPI 3.2.x
- JSON and YAML

## Reports

Supported report formats include:

- Console
- JSON
- Markdown
- HTML
- SARIF
- JUnit XML

The HTML report is self-contained and can be viewed offline.

## Privacy and Security

Core compatibility analysis runs locally and does not require a hosted AI
service, external SaaS platform, database, or cloud execution.

Remote contract loading is disabled by default and is subject to explicit
network and security controls when enabled.

## Installation

### Self-contained binaries

Download the archive matching your operating system and architecture from
the Assets section of this release.

### .NET global tool package

The release also contains:

OpenApiSentinel.$VERSION.nupkg

When the package is published to NuGet.org, install it with:

    dotnet tool install -g OpenApiSentinel --version $VERSION

## Quick Start

Compare two contracts:

    openapi-sentinel compare old-api.yaml new-api.yaml

Fail CI when High-or-greater changes are detected:

    openapi-sentinel compare old-api.yaml new-api.yaml --fail-on high

Generate HTML and JSON reports:

    openapi-sentinel compare old-api.yaml new-api.yaml --html report.html --json report.json

## Artifacts

| Asset | Audience |
|-------|----------|
| openapi-sentinel-$VERSION-linux-x64.tar.gz | Linux x64 |
| openapi-sentinel-$VERSION-linux-arm64.tar.gz | Linux ARM64 |
| openapi-sentinel-$VERSION-win-x64.zip | Windows x64 |
| openapi-sentinel-$VERSION-win-arm64.zip | Windows ARM64 |
| openapi-sentinel-$VERSION-osx-x64.tar.gz | macOS x64 |
| openapi-sentinel-$VERSION-osx-arm64.tar.gz | macOS ARM64 |
| pkg/OpenApiSentinel.$VERSION.nupkg | .NET global tool package |
| SHA256SUMS | SHA-256 checksums |

## Verification

Verify downloaded archives with the hashes in SHA256SUMS.

## Documentation

See the repository documentation for:

- CLI reference
- compatibility model
- rule catalog
- configuration
- suppressions and baselines
- CI/CD integrations
- security
- privacy
- troubleshooting
EOF

echo "==> Done. Artifacts in:"
find "$OUT" -maxdepth 1 -type f -printf '  %p\n'
