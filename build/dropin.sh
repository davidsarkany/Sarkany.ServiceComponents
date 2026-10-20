#!/usr/bin/env bash
# Drop-in check: packs this repository as Sarkany.ServiceComponents.* packages into artifacts/packages, then builds and
# tests the samples against those packages (-p:ScTarget=Package). The samples only change the package ids, never code.
#   DROPIN_VERSION  package version (default: 0.0.0-local)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
version="${DROPIN_VERSION:-0.0.0-local}"
feed="artifacts/packages"

rm -rf "$feed"
dotnet pack src/ServiceComponents.sln -c Release -p:Version="$version" --output "$feed"
# A previously restored package with the same version would shadow the new one.
for package in "$feed"/*.nupkg; do
  id="$(basename "$package" ".$version.nupkg" | tr '[:upper:]' '[:lower:]')"
  rm -rf "${NUGET_PACKAGES:-$HOME/.nuget/packages}/$id/$version"
done

dotnet test samples/Samples.sln -c Release -p:ScTarget=Package -p:ScPackageVersion="$version"
