#!/usr/bin/env bash
# Checks this repository against an original ServiceComponents.* release from nuget.org:
#   1. public API: ApiCompat reports anything of the original public API (types, members, parameter names) that is
#      missing or changed here; additions are fine;
#   2. interop: build/interop.sh with the samples built from that release (needs Docker; skip with --no-interop).
# Usage: build/upstream-check.sh [version] [--no-interop]   (default version: UpstreamVersion of versions.props)
#        build/upstream-check.sh --list-new                 prints original releases newer than UpstreamVersion
#                                                           that are not in UpstreamIgnoredVersions
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

if [ "${1:-}" = "--list-new" ]; then
  upstream="$(bash build/version.sh upstream)"
  ignored="$(bash build/version.sh ignored)"
  # nuget.org lists the versions in ascending NuGet order.
  curl -fsS https://api.nuget.org/v3-flatcontainer/servicecomponents.core/index.json \
    | tr -d ' \r\n' | sed 's/.*"versions":\[\(.*\)\].*/\1/' | tr ',' '\n' | tr -d '"' \
    | sed -n "/^$(printf '%s' "$upstream" | sed 's/\./\\./g')\$/,\$p" | sed 1d \
    | grep -vxF -f <(printf '%s\n' "$ignored") || true
  exit 0
fi

version=""
interop=1
for argument in "$@"; do
  case "$argument" in
    --no-interop) interop=0 ;;
    -*) echo "unknown option $argument" >&2; exit 2 ;;
    *) version="$argument" ;;
  esac
done
version="${version:-$(bash build/version.sh upstream)}"
echo "== Original release: ServiceComponents $version"

project="build/upstream/ApiSurface.csproj"
dotnet build "$project" -c Release -p:ScTarget=Baseline -p:ScBaselineVersion="$version" -v quiet -nologo
dotnet build "$project" -c Release -p:ScTarget=Current -v quiet -nologo
dotnet tool restore > /dev/null

left_list="build/upstream/obj/baseline/surface"
right_list="build/upstream/obj/surface"
lines() { tr -d '\r' < "$1" | tr '\\' '/'; }
# Reference directories: every compile reference, the ServiceComponents.* assemblies included (they reference each other).
directories() { cat "$1/assemblies.txt" "$1/references.txt" | tr -d '\r' | tr '\\' '/' | xargs -d '\n' -n1 dirname | sort -u | paste -sd,; }
left_references="$(directories "$left_list")"
right_references="$(directories "$right_list")"

echo "== Public API"
failed=0
while IFS= read -r left; do
  name="$(basename "$left")"
  right="$(lines "$right_list/assemblies.txt" | grep "/$name\$" || true)"
  if [ -z "$right" ]; then
    echo "$name: missing in this repository"
    failed=1
    continue
  fi
  if output="$(dotnet apicompat -l "$left" -r "$right" --lref "$left_references" --rref "$right_references" \
      --enable-rule-cannot-change-parameter-name 2>&1)"; then
    echo "$name: compatible"
  else
    echo "$name: NOT compatible"
    printf '%s\n' "$output" | sed 's/^/    /'
    failed=1
  fi
done < <(lines "$left_list/assemblies.txt")

if [ "$failed" != 0 ]; then
  echo "The public API of this repository does not cover ServiceComponents $version." >&2
  exit 1
fi

if [ "$interop" = 1 ]; then
  echo "== Interop"
  SC_BASELINE_VERSION="$version" bash build/interop.sh
fi

echo "== Compatible with ServiceComponents $version"
