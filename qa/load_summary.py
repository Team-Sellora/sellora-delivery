"""
Summarises a JMeter results file per request and per story, against the SLA, and writes the evidence files.

Run from the QA folder (RUN-LOAD.bat does it for you):
  python load_summary.py load/results.jtl [--sla-ms 1000] [--max-error-pct 1]
Writes evidence/CSP-xxx/jmeter/load-summary.txt for every story in the run, and prints the same table.
Verdict per request: PASS when p95 <= SLA and errors <= max-error-pct. A 2xx answer that only broke the duration
assertion is counted as slow, not as an error.
"""
import argparse
import csv
import pathlib
import re
from collections import defaultdict


def pct(values, p):
    if not values:
        return 0
    values = sorted(values)
    k = max(0, min(len(values) - 1, int(round(p / 100 * len(values) + 0.5)) - 1))
    return values[k]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("jtl")
    ap.add_argument("--sla-ms", type=int, default=1000)
    ap.add_argument("--max-error-pct", type=float, default=1.0)
    ap.add_argument("--evidence", default="evidence")
    args = ap.parse_args()

    rows = defaultdict(list)
    errors = defaultdict(list)
    slow = defaultdict(int)
    first, last = None, None
    with open(args.jtl, newline="", encoding="utf-8") as f:
        for r in csv.DictReader(f):
            label = r["label"]
            if label == "Reset poll state":
                continue
            ts, elapsed = int(r["timeStamp"]), int(r["elapsed"])
            first = ts if first is None else min(first, ts)
            last = ts + elapsed if last is None else max(last, ts + elapsed)
            rows[label].append(elapsed)
            message = r.get("failureMessage") or ""
            if r["success"].lower() != "true" and "lasted too long" in message and r["responseCode"].startswith("2"):
                slow[label] += 1          # right answer, only over the SLA - counted in the percentiles, not as an error
            elif r["success"].lower() != "true":
                errors[label].append(f'{r["responseCode"]} {r.get("failureMessage") or r.get("responseMessage") or ""}'.strip())
    seconds = max(1, (last - first) / 1000) if first else 1

    by_story = defaultdict(list)
    for label in sorted(rows):
        values = rows[label]
        n, err = len(values), len(errors[label])
        err_pct = 100 * err / n
        p95 = pct(values, 95)
        sla = args.sla_ms * (3 if "download" in label else 1)
        verdict = "PASS" if p95 <= sla and err_pct <= args.max_error_pct else "FAIL"
        line = (f"{verdict}  {label}\n"
                f"      samples {n}, errors {err} ({err_pct:.1f}%), over {sla} ms {slow[label]} ({100 * slow[label] / n:.1f}%), "
                f"throughput {n / seconds:.2f}/s\n"
                f"      avg {sum(values) / n:.0f} ms, p50 {pct(values, 50)} ms, p90 {pct(values, 90)} ms, "
                f"p95 {p95} ms, p99 {pct(values, 99)} ms, max {max(values)} ms (SLA p95 <= {sla} ms)")
        if err:
            common = defaultdict(int)
            for e in errors[label]:
                common[e[:120]] += 1
            line += "\n      errors: " + "; ".join(f"{k} x{v}" for k, v in sorted(common.items(), key=lambda kv: -kv[1])[:3])
        story = (re.match(r"(CSP-\d+)", label) or [None, "other"])[1]
        by_story[story].append((verdict, line))

    header = (f"JMeter results: {args.jtl}\nTest window: {seconds:.0f} s, {sum(len(v) for v in rows.values())} requests\n"
              f"Rule: PASS when p95 <= SLA and errors <= {args.max_error_pct}%. Errors are wrong answers (non-2xx, failed checks); "
              f"slow 2xx answers count only towards the percentiles\n")
    print(header)
    for story, items in sorted(by_story.items()):
        failed = sum(1 for v, _ in items if v == "FAIL")
        body = f"{story}: {len(items) - failed} of {len(items)} requests within SLA\n\n" + "\n\n".join(l for _, l in items)
        print(body + "\n")
        if story.startswith("CSP-"):
            folder = pathlib.Path(args.evidence) / story / "jmeter"
            folder.mkdir(parents=True, exist_ok=True)
            (folder / "load-summary.txt").write_text(header + "\n" + body + "\n", encoding="utf-8")
    print("Evidence written to evidence/<CSP>/jmeter/load-summary.txt; HTML dashboard in load/report/index.html")


if __name__ == "__main__":
    main()