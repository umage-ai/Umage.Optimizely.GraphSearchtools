#!/usr/bin/env bash
# Packs both package flavours and checks what ended up inside them, so a wrong CMS dependency
# group or a misnamed .targets file fails CI instead of surfacing after a release.
#   UmageAI.Optimizely.GraphSearchTools        net8.0 -> CMS 12, net10.0 -> CMS 13
#   UmageAI.Optimizely.GraphSearchTools.Cms12  net8.0 -> CMS 12, net10.0 -> CMS 12
set -euo pipefail

project=src/GraphSearchtools/GraphSearchtools.csproj
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT

# The two flavours share obj/ and bin/, so pack them one after the other.
dotnet pack "$project" -c Release -p:Version=0.0.0-ci -o "$out/nupkg"
dotnet pack "$project" -c Release -p:Version=0.0.0-ci -p:GraphSearchToolsCmsVersion=12 -o "$out/nupkg"

fail=0

# Prints the major version of EPiServer.CMS in a package's dependency group for one TFM.
cms_major() {
  local dir=$1 tfm=$2
  awk -v tfm="$tfm" '
    /<group targetFramework="/ { in_group = ($0 ~ "targetFramework=\"" tfm "\"") }
    in_group && /<dependency id="EPiServer.CMS"/ {
      match($0, /version="[0-9]+/); print substr($0, RSTART + 9, RLENGTH - 9); exit
    }' "$dir"/*.nuspec
}

check() {
  local id=$1 tfm=$2 want=$3 dir=$4
  local got
  got="$(cms_major "$dir" "$tfm")"
  if [ "$got" != "$want" ]; then
    echo "FAIL: $id $tfm depends on EPiServer.CMS ${got:-none}, expected $want"
    fail=1
  else
    echo "ok:   $id $tfm depends on EPiServer.CMS $want"
  fi
  # NuGet only auto-imports build/<tfm>/<PackageId>.targets.
  if [ ! -f "$dir/build/$tfm/$id.targets" ]; then
    echo "FAIL: $id is missing build/$tfm/$id.targets"
    fail=1
  fi
}

unpack() {
  local id=$1 dir="$out/$1"
  mkdir -p "$dir"
  unzip -q "$out/nupkg/$id.0.0.0-ci.nupkg" -d "$dir"
  echo "$dir"
}

main_id=UmageAI.Optimizely.GraphSearchTools
cms12_id=UmageAI.Optimizely.GraphSearchTools.Cms12

main_dir="$(unpack "$main_id")"
cms12_dir="$(unpack "$cms12_id")"

check "$main_id" net8.0 12 "$main_dir"
check "$main_id" net10.0 13 "$main_dir"
check "$cms12_id" net8.0 12 "$cms12_dir"
check "$cms12_id" net10.0 12 "$cms12_dir"

exit "$fail"
