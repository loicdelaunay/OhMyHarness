#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
if [[ "$(uname -s)" != "Darwin" ]]; then echo "Build the .app/.dmg on macOS (or use the macOS CI job)." >&2; exit 1; fi
arch="${1:-$(uname -m)}"
case "$arch" in arm64) rid=osx-arm64 ;; x64|x86_64) arch=x64; rid=osx-x64 ;; *) echo "Use arm64 or x64" >&2; exit 1 ;; esac
dotnet build OhMyHarness.Desktop.slnx -c Release
dotnet run --project tests/OhMyHarness.Tests -c Release --no-build
cd desktop
npm ci
npm test
node scripts/publish-service.cjs "$rid"
npx electron-builder --mac --"$arch"
echo "macOS packages: desktop/dist (signing/notarization require your Apple credentials)."
