#!/bin/sh
# Builds Sundex.Engine's native audio library (miniaudio and the sundex_audio shim) for one RID.
#   build/audio-native.sh [rid] [compiler]
# The RID defaults to this machine's; the compiler defaults to $CC, then cc. The library lands in
# Sundex.Engine/Audio/Native/runtimes/<rid>/native/, which the engine package ships and the
# engine's resolver finds next to the app.
set -eu

root=$(cd "$(dirname "$0")/.." && pwd)
src=$root/Sundex.Engine/Audio/Native

if [ -n "${1:-}" ]; then
    rid=$1
else
    case $(uname -s) in
        Linux) os=linux ;;
        Darwin) os=osx ;;
        MINGW* | MSYS* | CYGWIN*) os=win ;;
        *) echo "Unknown OS $(uname -s); pass a RID" >&2; exit 1 ;;
    esac
    case $(uname -m) in
        x86_64 | amd64) arch=x64 ;;
        aarch64 | arm64) arch=arm64 ;;
        *) echo "Unknown architecture $(uname -m); pass a RID" >&2; exit 1 ;;
    esac
    rid=$os-$arch
fi
cc=${2:-${CC:-cc}}

out=$src/runtimes/$rid/native
mkdir -p "$out"
case $rid in
    win-*)
        "$cc" -O2 -shared -o "$out/sundex_audio.dll" "$src/sundex_audio.c" -lole32
        ;;
    osx-*)
        case $rid in osx-arm64) march=arm64 ;; *) march=x86_64 ;; esac
        "$cc" -O2 -shared -fPIC -arch "$march" -o "$out/libsundex_audio.dylib" "$src/sundex_audio.c" \
            -framework CoreFoundation -framework CoreAudio -framework AudioToolbox
        ;;
    *)
        "$cc" -O2 -shared -fPIC -fvisibility=hidden -o "$out/libsundex_audio.so" "$src/sundex_audio.c" -ldl -lpthread -lm
        ;;
esac
echo "$out"
