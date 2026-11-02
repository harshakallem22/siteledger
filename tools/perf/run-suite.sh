#!/usr/bin/env bash
# Warm-up, then 3 measured trials per report endpoint; usage: run-suite.sh <label>  (needs API_TOKEN, PROJECT_ID)
set -euo pipefail
label="$1"
script="$(dirname "$0")/report-test.js"
mkdir -p results

for mode in portfolio project; do
  k6 run --quiet -e MODE="$mode" -e VUS=10 -e DURATION=20s -e SUMMARY_FILE=/dev/null "$script" > /dev/null || true
  for trial in 1 2 3; do
    # Thresholds may fail (exit 99); the numbers are still recorded.
    k6 run --quiet -e MODE="$mode" -e SUMMARY_FILE="results/${label}-${mode}-${trial}.json" "$script" || true
  done
done
