#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="$(sed -n 's/.*<ApplicationDisplayVersion>\([^<]*\)<\/ApplicationDisplayVersion>.*/\1/p' src/OhMyHarness.App/OhMyHarness.App.csproj)"
cli_version="$(sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' src/OhMyHarness.Cli/OhMyHarness.Cli.csproj)"
[[ -n "$version" && "$version" == "$cli_version" ]] || { echo 'GUI/CLI versions differ' >&2; exit 1; }
root="artifacts/TEMP/release-packages"
for channel in GUI CLI; do
  if [[ "$channel" == GUI ]]; then exe=OhMyHarness.App; prefix=OhMyHarness; else exe=omh; prefix=OhMyHarness-CLI; fi
  source="artifacts/$channel/$exe"
  [[ -f "$source" ]] || { echo "Missing $source" >&2; exit 1; }
  stage="$root/stage-$channel-linux-x64"
  output="$root/$channel"
  [[ ! -e "$stage" ]] || { echo "Staging folder already exists: $stage" >&2; exit 1; }
  mkdir -p "$stage" "$output"
  trap 'rm -rf -- "$stage"' EXIT
  cp -- "$source" "$stage/$exe"
  cp -- LICENSE "$stage/LICENSE"
  cat > "$stage/README.txt" <<EOF
OhMyHarness $channel $version (Linux x64; tested on Fedora 44)

Extract into a writable folder with tar -xzf. Run ./$exe.
The GUI requires an X11 desktop. Browser use needs GTK3 and WebKitGTK.
The CLI can connect a provider with /connect.
Bundled .NET and Python need no separate installation.

The portable database and .linux-key file are created beside the executable.
Keep the entire folder private and back it up together. To update, close the
application and replace only $exe.

Desktop mouse, keyboard, application listing and screen capture are not
implemented on Linux. Git, Docker/Podman, OpenCode and MCP are optional.
https://github.com/loicdelaunay/OhMyHarness
EOF
  chmod +x "$stage/$exe"
  archive="$prefix-v$version-linux-x64.tar.gz"
  [[ ! -e "$output/$archive" ]] || { echo "Archive already exists: $output/$archive" >&2; exit 1; }
  tar -czf "$output/$archive" -C "$stage" "$exe" LICENSE README.txt
  ( cd "$output" && sha256sum "$archive" > SHA256SUMS-linux-x64.txt )
  rm -rf -- "$stage"
  trap - EXIT
  echo "Packaged $output/$archive"
done
