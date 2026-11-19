#!/bin/sh
set -eu

cd "$(dirname "$0")/.."
version=${1:?Pass the version of the packages in artifacts/}
configuration=${2:-Release}
project=samples/Sundex.Samples.CoreOnly/Sundex.Samples.CoreOnly.csproj

dotnet restore "$project" --configfile build/core-only.nuget.config -p:SundexVersion="$version"
packages=$(dotnet list "$project" package --include-transitive --no-restore)
printf '%s\n' "$packages"
if printf '%s\n' "$packages" | grep -Eq 'Sundex\.(Style\.DSL|Components|Markup|Testing)'; then
    printf '%s\n' 'The core sample brought in extension packages.' >&2
    exit 1
fi

dotnet build "$project" --no-restore -c "$configuration" -p:SundexVersion="$version"
LIBGL_ALWAYS_SOFTWARE=1 XDG_SESSION_TYPE=x11 xvfb-run -a \
    dotnet run --project "$project" --no-build -c "$configuration" -p:SundexVersion="$version"
