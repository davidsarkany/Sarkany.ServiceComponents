#!/usr/bin/env bash
# Runs all tests with coverage, writes an HTML + Cobertura report to artifacts/coverage and
# fails when line or branch coverage of the published packages drops below the thresholds.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"
# Relative paths keep reportgenerator's glob working under Git Bash on Windows too.
results="artifacts/coverage/raw"
report="artifacts/coverage/report"
min_line="${MIN_LINE_COVERAGE:-90}"
min_branch="${MIN_BRANCH_COVERAGE:-80}"

rm -rf "$results" "$report"
dotnet test src/ServiceComponents.sln --configuration Release \
  --settings src/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory "$results"
# The samples consume the packages like an application (and are the only tests of ServiceComponents.Testing's TestHost).
dotnet test samples/Samples.sln --configuration Release   --settings src/coverage.runsettings --collect:"XPlat Code Coverage" --results-directory "$results"

dotnet tool restore
dotnet reportgenerator -reports:"$results/**/coverage.cobertura.xml" -targetdir:"$report" \
  -reporttypes:"Html;Cobertura;TextSummary;MarkdownSummaryGithub"

cat "$report/Summary.txt"

line=$(grep -m1 -E '^\s*Line coverage:' "$report/Summary.txt" | grep -oE '[0-9]+(\.[0-9]+)?' | head -1)
branch=$(grep -m1 -E '^\s*Branch coverage:' "$report/Summary.txt" | grep -oE '[0-9]+(\.[0-9]+)?' | head -1)

below() { awk -v value="$1" -v min="$2" 'BEGIN { exit !(value < min) }'; }
if below "$line" "$min_line" || below "$branch" "$min_branch"; then
  echo "Coverage below threshold: line ${line}% (min ${min_line}%), branch ${branch}% (min ${min_branch}%)" >&2
  exit 1
fi
echo "Coverage OK: line ${line}% (min ${min_line}%), branch ${branch}% (min ${min_branch}%)"
