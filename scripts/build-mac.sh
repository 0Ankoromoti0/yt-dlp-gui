#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
rid="${1:-osx-arm64}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 1 ;; esac
app="$root/artifacts/$rid/YtDlpGui.app"
output="$app/Contents/MacOS"
mkdir -p "$output" "$app/Contents/Resources"
dotnet publish src/YtDlpGui.Mac/YtDlpGui.Mac.csproj -c Release -r "$rid" --self-contained true -p:PublishSingleFile=false -o "$output"
mkdir -p "$output/tools"
for tool in yt-dlp ffmpeg ffprobe deno; do
  test -f "$root/tools/$tool"
  cp "$root/tools/$tool" "$output/tools/$tool"
  chmod +x "$output/tools/$tool"
done
for file in README.md THIRD-PARTY-NOTICES.md LICENSE VERIFICATION.md; do cp "$file" "$app/Contents/Resources/"; done
cp -R licenses "$app/Contents/Resources/"
cp packaging/macos/Info.plist "$app/Contents/Info.plist"
# Ad-hoc signing supplies valid Mach-O signatures, including the JIT entitlement.
# This does not claim a Developer ID identity or Apple notarization.
while IFS= read -r -d '' file; do
  if file "$file" | grep -q 'Mach-O'; then
    codesign --force --sign - --entitlements packaging/macos/entitlements.plist "$file"
  fi
done < <(find "$output" -type f -print0)
codesign --force --sign - --entitlements packaging/macos/entitlements.plist "$app"
codesign --verify --deep --strict "$app"
# No Homebrew or machine-local library may leak into the portable distribution.
while IFS= read -r -d '' file; do
  if file "$file" | grep -q 'Mach-O'; then
    if otool -L "$file" | grep -E '/opt/homebrew/|/usr/local/|/Users/|/mingw'; then
      echo "Non-portable dependency: $file" >&2; exit 1
    fi
  fi
done < <(find "$output" -type f -print0)
ditto -c -k --sequesterRsrc --keepParent "$app" "$root/artifacts/YtDlpGui-$rid.zip"
echo "$app"
