#!/usr/bin/env bash
# Versions of versions.props, for scripts and workflows:
#   build/version.sh upstream         prints UpstreamVersion
#   build/version.sh ignored          prints UpstreamIgnoredVersions, one per line
#   build/version.sh release <tag>    prints the package version of a release tag (a leading 'v' is dropped) and fails
#                                     unless it is UpstreamVersion or UpstreamVersion.N (N >= 1)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
props="$root/versions.props"

property() {
  sed -n "s:.*<$1>\(.*\)</$1>.*:\1:p" "$props" | head -1
}

case "${1:-}" in
  upstream)
    property UpstreamVersion
    ;;
  ignored)
    property UpstreamIgnoredVersions | tr ';' '\n' | sed '/^$/d'
    ;;
  release)
    tag="${2:?usage: build/version.sh release <tag>}"
    version="${tag#v}"
    upstream="$(property UpstreamVersion)"
    if [ "$version" = "$upstream" ]; then
      echo "$version"
    elif [[ "$version" == "$upstream".* ]] && [[ "${version#"$upstream".}" =~ ^[1-9][0-9]*$ ]]; then
      echo "$version"
    else
      echo "Tag $tag does not match the compatibility version: expected $upstream or $upstream.N (see versions.props)" >&2
      exit 1
    fi
    ;;
  *)
    echo "usage: build/version.sh upstream | ignored | release <tag>" >&2
    exit 2
    ;;
esac
