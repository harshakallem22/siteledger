# Turns k6 summary files (results/<schema>-<mode>-<trial>.json) into a Markdown table.
import glob
import json
import os
import statistics
import sys

results_dir = sys.argv[1] if len(sys.argv) > 1 else "results"
rows = {}
for path in sorted(glob.glob(os.path.join(results_dir, "*-*-*.json"))):
    schema, mode, trial = os.path.basename(path)[:-5].split("-")
    metrics = json.load(open(path))["metrics"]
    duration = metrics["http_req_duration"]["values"]
    rows.setdefault((schema, mode), []).append({
        "trial": trial,
        "p95": duration["p(95)"],
        "p50": duration["med"],
        "p99": duration["p(99)"],
        "requests": metrics["http_reqs"]["values"]["count"],
        "rps": metrics["http_reqs"]["values"]["rate"],
        "failed": metrics["http_req_failed"]["values"]["rate"] * 100,
    })

print("| Schema | Endpoint | Trial | P95 ms | P50 ms | P99 ms | Requests | Req/s | Failed % |")
print("|---|---|---|---|---|---|---|---|---|")
for (schema, mode), trials in sorted(rows.items()):
    for t in trials:
        print(f"| {schema} | {mode} | {t['trial']} | {t['p95']:.1f} | {t['p50']:.1f} | {t['p99']:.1f} | {t['requests']} | {t['rps']:.0f} | {t['failed']:.2f} |")

print()
print("| Schema | Endpoint | P95 per trial (ms) | Median P95 (ms) | Total requests | Max failed % |")
print("|---|---|---|---|---|---|")
for (schema, mode), trials in sorted(rows.items()):
    per_trial = " / ".join(f"{t['p95']:.1f}" for t in trials)
    print(f"| {schema} | {mode} | {per_trial} | {statistics.median(t['p95'] for t in trials):.1f} | "
          f"{sum(t['requests'] for t in trials)} | {max(t['failed'] for t in trials):.2f} |")
