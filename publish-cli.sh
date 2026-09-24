#!/usr/bin/env bash
set -euo pipefail
runtime=osx-arm64
if (( $# > 0 )); then runtime="$1"; fi
case "$runtime" in
  osx-arm64|osx-x64|win-x64|win-arm64) ;;
  *) echo "Supported runtimes: osx-arm64, osx-x64, win-x64, win-arm64" >&2; exit 1 ;;
esac
repo_dir="$(cd "$(dirname "$0")" && pwd)"
output="$repo_dir/artifacts/release/cli/$runtime"
if (( $# > 1 )); then output="$2"; fi
dotnet publish "$repo_dir/src/OhMyHarness.Cli/OhMyHarness.Cli.csproj" \
  -c Release -r "$runtime" --self-contained true \
  -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false \
  -o "$output"
echo "OhMyHarness CLI available in $output"
