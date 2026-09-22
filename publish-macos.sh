#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ "$(uname -s)" != "Darwin" ]]; then echo "Compilez et signez l’application macOS sur un Mac." >&2; exit 1; fi
case "${1:-$(uname -m)}" in arm64) rid=osx-arm64 ;; x64|x86_64) rid=osx-x64 ;; *) echo "Use arm64 or x64" >&2; exit 1 ;; esac
output="${2:-artifacts/release/$rid}"
dotnet run --project tests/OhMyHarness.Tests -c Release
dotnet publish src/OhMyHarness.App -f net10.0-desktop -p:OhMyHarnessDesktopOnly=true -c Release -r "$rid" --self-contained true -p:UseMonoRuntime=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:IncludeAllContentForSelfExtract=true -o "$output"
echo "Uno Platform : $output/OhMyHarness.App (signature Apple à appliquer pour la distribution)."
