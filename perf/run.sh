#!/usr/bin/env bash
# Runs the performance suite against the Baseline (pre-rewrite packages) and the Current (repository) build on this
# machine and writes a comparison to perf/results/<timestamp>/:
#   benchmarks/   BenchmarkDotNet reports; every benchmark runs for both builds, Baseline is the ratio base
#   load-*.json   raw load test results per build
#   comparison.md load test comparison with the regression thresholds
# Options (environment):
#   PERF_QUICK=1          short runs (smoke test of the suite, numbers are noisy)
#   PERF_FILTER='*'       BenchmarkDotNet filter
#   PERF_SCENARIOS=http,rabbit   load test scenarios (rabbit needs Docker)
#   PERF_CONCURRENCY=32   load test workers
#   PERF_REPEAT=3         load test rounds; Baseline and Current alternate, the comparison uses the medians
# Exit code is non-zero when a load test exceeds the thresholds (throughput -5%, p99 +10%).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
stamp="$(date +%Y%m%d-%H%M%S)"
out="$root/perf/results/$stamp"
mkdir -p "$out"
# Native (Windows) form of the path for arguments Git Bash cannot convert, e.g. comma separated lists.
command -v cygpath >/dev/null 2>&1 && out="$(cygpath -m "$out")"
quick_flag=""
[ "${PERF_QUICK:-0}" = "1" ] && quick_flag="--quick"


echo "== Building"
for target in Baseline Current; do
  dotnet build perf/ServiceComponents.Benchmarks -c Release -p:ScTarget=$target
  dotnet build perf/ServiceComponents.LoadTests -c Release -p:ScTarget=$target
done

echo "== Micro benchmarks"
(cd perf/ServiceComponents.Benchmarks && \
  dotnet run -c Release --no-build -- --filter "${PERF_FILTER:-*}" --artifacts "$out/benchmarks")

echo "== Load tests"
repeat="${PERF_REPEAT:-3}"
[ "${PERF_QUICK:-0}" = "1" ] && repeat="${PERF_REPEAT:-1}"
baseline_runs=""; current_runs=""
for round in $(seq 1 "$repeat"); do
  # Alternate the order so drift (thermal, background load) does not favour one build.
  if [ $((round % 2)) = 1 ]; then order="Baseline Current"; else order="Current Baseline"; fi
  for target in $order; do
    file="$out/load-$(echo $target | tr '[:upper:]' '[:lower:]')-$round.json"
    dotnet run --project perf/ServiceComponents.LoadTests -c Release -p:ScTarget=$target --no-build -- \
      run --target $target --scenarios "${PERF_SCENARIOS:-http,rabbit}" --concurrency "${PERF_CONCURRENCY:-32}" \
      $quick_flag --out "$file"
    if [ $target = Baseline ]; then baseline_runs="$baseline_runs,$file"; else current_runs="$current_runs,$file"; fi
  done
done

echo "== Comparison"
status=0
dotnet run --project perf/ServiceComponents.LoadTests -c Release --no-build -- \
  compare --baseline "${baseline_runs#,}" --current "${current_runs#,}" --out "$out/comparison.md" || status=$?

{
  echo "# Performance comparison $stamp"
  echo
  echo "Baseline: original ServiceComponents packages from nuget.org (${SC_BASELINE_VERSION:-0.2.0-beta.12}), Current: $(git rev-parse --short HEAD 2>/dev/null || echo working tree)."
  echo
  echo "## Load tests"
  echo
  cat "$out/comparison.md" 2>/dev/null || echo "(no load test comparison)"
  echo
  echo "## Micro benchmarks"
  echo
  cat "$out"/benchmarks/results/*-report-github.md 2>/dev/null || echo "(no benchmark reports)"
} > "$out/README.md"
echo "Report: $out/README.md"
exit $status
