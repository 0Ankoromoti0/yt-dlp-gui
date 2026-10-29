#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
rid="${1:-osx-arm64}"
case "$rid" in osx-arm64|osx-x64) ;; *) echo 'Expected osx-arm64 or osx-x64' >&2; exit 1 ;; esac
app="$root/artifacts/$rid/YtDlpGui.app"
# Only the generated bundle for the explicitly validated RID may be replaced.
case "$app" in
  "$root/artifacts/osx-arm64/YtDlpGui.app"|"$root/artifacts/osx-x64/YtDlpGui.app") ;;
  *) echo 'Unexpected bundle output path' >&2; exit 1 ;;
esac
if [[ -d "$app" ]]; then rm -rf -- "$app"; fi
output="$app/Contents/MacOS"
mkdir -p "$output" "$app/Contents/Resources"
# Bundle managed DLLs so macOS only sees native code in Contents/MacOS.
dotnet publish src/YtDlpGui.Mac/YtDlpGui.Mac.csproj -c Release -r "$rid" --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false -p:DebugType=embedded -o "$output"
mkdir -p "$output/tools"
for tool in yt-dlp ffmpeg ffprobe deno; do
  test -f "$root/tools/$tool"
  cp "$root/tools/$tool" "$output/tools/$tool"
  chmod +x "$output/tools/$tool"
done
for file in README.md THIRD-PARTY-NOTICES.md LICENSE VERIFICATION.md; do cp "$file" "$app/Contents/Resources/"; done
cp -R licenses "$app/Contents/Resources/"
if [[ -f artifacts/native-sources/FFmpeg-COPYING.GPLv2 ]]; then
  cp artifacts/native-sources/FFmpeg-COPYING.GPLv2 "$app/Contents/Resources/licenses/ffmpeg-LICENSE.txt"
  for license in artifacts/native-sources/*-COPYING artifacts/native-sources/*-LICENSE; do
    cp "$license" "$app/Contents/Resources/licenses/"
  done
fi
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
    # LC_ID_DYLIB is the library's own install ID, not a loaded dependency.
    # Inspect actual load commands and search paths in every architecture slice.
    dependencies="$(otool -l "$file" | awk '
      $1 == "cmd" {
        library = ($2 ~ /^LC_(LOAD|LOAD_WEAK|LAZY_LOAD|REEXPORT|LOAD_UPWARD)_DYLIB$/)
        searchpath = ($2 == "LC_RPATH")
      }
      (library && $1 == "name") || (searchpath && $1 == "path") { print $2 }
    ')"
    if printf '%s\n' "$dependencies" | grep -E '^(/opt/homebrew/|/usr/local/|/Users/|/mingw)'; then
      echo "Non-portable dependency: $file" >&2; exit 1
    fi
  fi
done < <(find "$output" -type f -print0)
ditto -c -k --sequesterRsrc --keepParent "$app" "$root/artifacts/YtDlpGui-$rid.zip"
echo "$app"
