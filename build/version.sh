#!/bin/sh
set -eu

if [ "${1:-}" = stable ]; then
    tag=${GITHUB_REF_NAME:?Expected a release tag}
    if ! printf '%s\n' "$tag" | grep -Eq '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'; then
        printf 'Invalid stable release tag: %s\n' "$tag" >&2
        exit 1
    fi
    printf '%s\n' "${tag#v}"
    exit
fi

if [ "${1:-}" = prerelease ]; then
    version=${2:?Expected a prerelease version}
    case "$version" in
        *[!a-zA-Z0-9.-]*) exit 1 ;;
    esac
    if ! printf '%s\n' "$version" | grep -Eq '^((0|[1-9][0-9]*)\.){2}(0|[1-9][0-9]*)-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*$' ||
        printf '%s\n' "${version#*-}" | grep -Eq '(^|\.)0[0-9]+(\.|$)'; then
        printf 'Invalid prerelease version: %s\n' "$version" >&2
        exit 1
    fi
    printf '%s\n' "$version"
    exit
fi

last=$(git tag --list 'v*' --sort=-v:refname | grep -m1 -E '^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' || true)
last=${last#v}
last=${last:-0.0.0}
major=${last%%.*}
rest=${last#*.}
minor=${rest%%.*}
patch=${rest#*.}
# nuget.org rejects a single prerelease label that starts with a digit.
printf '%s.%s.%s-n%s-%s\n' "$major" "$minor" "$((patch + 1))" "$(date -u +%Y%m%d%H%M)" "$(git rev-parse --short=7 HEAD)"
