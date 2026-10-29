#!/usr/bin/env bash
# Build portable tools from the exact sources shipped alongside the release.
set -eo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
work="$root/.build/native"
prefix="$work/prefix"
sources="$root/artifacts/native-sources"
mkdir -p "$work" "$prefix" "$sources" "$root/tools"
export PATH="$prefix/bin:$PATH"
export PKG_CONFIG_PATH="$prefix/lib/pkgconfig"
jobs="${BUILD_JOBS:-4}"
fetch() {
  curl --fail --location --retry 3 "$1" --output "$sources/$2"
}
fetch https://ffmpeg.org/releases/ffmpeg-8.0.1.tar.xz ffmpeg-8.0.1.tar.xz
fetch https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz lame-3.100.tar.gz
fetch https://downloads.xiph.org/releases/opus/opus-1.5.2.tar.gz opus-1.5.2.tar.gz
fetch https://github.com/webmproject/libvpx/archive/refs/tags/v1.15.2.tar.gz libvpx-1.15.2.tar.gz
# Resolve the maintained stable branch once and retain the exact commit and tarball.
x264_sha="$(git ls-remote https://github.com/mirror/x264.git refs/heads/stable | cut -f1)"
test -n "$x264_sha"
printf '%s\n' "$x264_sha" > "$sources/x264-commit.txt"
fetch "https://github.com/mirror/x264/archive/$x264_sha.tar.gz" x264.tar.gz
for archive in "$sources"/*.tar.*; do tar -xf "$archive" -C "$work"; done
host_flags=()
ffmpeg_flags=()
static_flags=''
case "$(uname -s)" in
  MINGW*|MSYS*)
    export CC=gcc CXX=g++
    host_flags=(--host=x86_64-w64-mingw32)
    ffmpeg_flags=(--target-os=mingw32 --arch=x86_64 --cc=gcc --cxx=g++ --enable-schannel)
    static_flags='-static'
    ;;
  Darwin)
    export MACOSX_DEPLOYMENT_TARGET=14.0
    export CC=clang CXX=clang++
    ffmpeg_flags=(--enable-securetransport)
    ;;
  *) echo 'Only macOS and MinGW are supported.' >&2; exit 1 ;;
esac
cd "$work/lame-3.100"
./configure --prefix="$prefix" "${host_flags[@]}" --disable-shared --enable-static --disable-frontend
make -j"$jobs"
make install
cd "$work/opus-1.5.2"
./configure --prefix="$prefix" "${host_flags[@]}" --disable-shared --enable-static --disable-extra-programs --disable-doc
make -j"$jobs"
make install
cd "$work/x264-$x264_sha"
./configure --prefix="$prefix" "${host_flags[@]}" --enable-static --disable-cli --disable-opencl --disable-asm
make -j"$jobs"
make install
cd "$work/libvpx-1.15.2"
./configure --prefix="$prefix" --target=generic-gnu --disable-shared --enable-static --disable-examples --disable-tools --disable-docs --disable-unit-tests
make -j"$jobs"
make install
cd "$work/ffmpeg-8.0.1"
./configure --prefix="$prefix" --extra-cflags="-I$prefix/include" --extra-ldflags="-L$prefix/lib $static_flags" \
  --pkg-config-flags=--static --disable-autodetect --disable-shared --enable-static \
  --disable-debug --disable-doc --disable-ffplay --enable-gpl --enable-libx264 --enable-libvpx \
  --enable-libmp3lame --enable-libopus "${ffmpeg_flags[@]}"
make -j"$jobs"
make install
suffix=''
if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then suffix='.exe'; fi
cp "$prefix/bin/ffmpeg$suffix" "$prefix/bin/ffprobe$suffix" "$root/tools/"
cp "$root/scripts/build-ffmpeg.sh" "$sources/"
cp "$work/ffmpeg-8.0.1/COPYING.GPLv2" "$sources/FFmpeg-COPYING.GPLv2"
cp "$work/lame-3.100/COPYING" "$sources/LAME-COPYING"
cp "$work/opus-1.5.2/COPYING" "$sources/Opus-COPYING"
cp "$work/libvpx-1.15.2/LICENSE" "$sources/libvpx-LICENSE"
cp "$work/x264-$x264_sha/COPYING" "$sources/x264-COPYING"
"$root/tools/ffmpeg$suffix" -version > "$sources/ffmpeg-build.txt"
