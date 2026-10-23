#!/usr/bin/env bash
# Phase 4 N3: every evaluation set under each variant, against the algorithm alone (docs/PHASE4-PLAN.md).
#   tools/beatnet/variants.sh <outDir>        (model beats already in .eval-cache/beatnet via infer.py)
# Variants:
#   alg          the algorithm alone (baseline)
#   model        model beats turned straight into red lines (ModelOnlyAlgorithm: no onsets, no Core pipeline)
#   model+onset  model beats refined onto the onset envelope (lag 28.7 = PeakToOsuMs)
#   model+octave model beats brought to the algorithm's whole-song beat level first, then refined
#   alg+modeloct the algorithm's red lines, each section's octave from the model's beat count
#   hybrid       the algorithm's steady sections (>= 8 s), the model's own red lines in the gaps (ramps)
set -euo pipefail
out="$1"; mkdir -p "$out"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
export DOTNET_CLI_HOME="$repo/.dotnet-home" NUGET_PACKAGES='D:\packages\nuget' TIMING_EVAL_BEATS=beatnet
run() { dotnet run --project "$repo/tools/TimingAnalyzer.Eval" -c Release --no-build -- run "$repo/tests/$1.tsv" "${@:3}" > "$out/$2-$1.txt" 2>&1; }
for set in multibpm-songs mania-songs ranked-picks; do
  run $set alg multi
  run $set model modelonly
  run $set model+onset multi ext=1 lag=28.7
  run $set model+octave multi ext=1 lag=28.7 extoct=1
  run $set alg+modeloct modeloct
  run $set hybrid hybrid
done
echo "done -> $out"
