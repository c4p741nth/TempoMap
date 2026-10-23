#!/usr/bin/env bash
# Beat model data for a songs.tsv, in chunks so raw audio (20 MB per song) never piles up on disk:
# dump 50 songs' PCM + red lines (timing-eval dump-set), turn them into mel .npz and delete the PCM (data.py), repeat.
#   tools/beatnet/prepare.sh <songs.tsv> <cacheDir> <python>
# Resumable: chunks left in <cacheDir>/chunks from an interrupted run are done first, songs already in the cache are
# skipped by dump-set, and a new split is made only when no chunk is left.
set -euo pipefail
songs="$1"; cache="$2"; py="$3"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
export DOTNET_CLI_HOME="$repo/.dotnet-home" NUGET_PACKAGES='D:\packages\nuget'
tmp="$cache/chunks"; mkdir -p "$tmp"
if [ -z "$(ls -A "$tmp")" ]; then grep -v '^#' "$songs" | split -l 50 - "$tmp/chunk-"; fi
for chunk in "$tmp"/chunk-*; do
  dotnet run --project "$repo/tools/TimingAnalyzer.Eval" -c Release --no-build -- dump-set "$(cygpath -w "$chunk")" "$(cygpath -w "$cache")" 2>/dev/null | tail -1
  "$py" -I "$repo/tools/beatnet/data.py" "$cache" | tail -1
  rm -f "$chunk"
done
echo "prepared: $(ls "$cache"/*.npz | wc -l) mel files, $(du -sh "$cache" | cut -f1)"
