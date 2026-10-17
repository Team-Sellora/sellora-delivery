"""
Prepares the Sprint 4 load test (Sprint4-load-test.jmx) from live staging data. Read-only except for one step:
if the test rep has no InTransit delivery, it creates one through the real flow (order -> approve -> assign -> InTransit)
so the live-tracking thread group has a job to post to.

Writes, in the QA folder:
  load/refs.csv          order references that exist in the audit log (CSP-250 searches)
  load/deliveries.csv    delivery job IDs visible to the Agency Operator (CSP-264 detail reads)
  load/load.properties   gateway paths, tokens, dates, user counts - CONTAINS TOKENS, never zip or commit it

Run from the QA folder:  python make_load_data.py [--duration 300] [--scale 1]
--scale multiplies every user count (0.5 = lighter, 2 = heavier). Staging runs on small Azure tiers: start at 1.
"""
import argparse
import calendar
import pathlib
import sys
from datetime import date, timedelta

from sellora_qa import (Api, Report, SetupError, audit_base, build_scenario, ci_get, colombo_today, delivery_base, items_of,
                        load_env, standard_args)
from urllib.parse import urlparse

STORY = "CSP-250"
USERS = {"audit_users": 20, "report_users": 10, "report_am_users": 5, "queue_users": 10, "shop_users": 5,
         "track_posters": 1, "track_viewers": 10, "export_users": 2}


def month_range(day):
    first = day.replace(day=1)
    last = day.replace(day=calendar.monthrange(day.year, day.month)[1])
    return first, last


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter))
    parser.add_argument("--duration", type=int, default=300, help="seconds each thread group runs (default 300)")
    parser.add_argument("--rampup", type=int, default=60)
    parser.add_argument("--scale", type=float, default=1.0)
    parser.add_argument("--sla-ms", type=int, default=1000)
    parser.add_argument("--delivery-only", action="store_true",
                        help="skip the audit, report and export groups (CSP-250/252/253) - use while the Audit API is unavailable")
    argv = sys.argv[1:]
    args = parser.parse_args(argv if "--target" in argv else argv + ["--target", "staging"])
    env = load_env(args.env)
    if not args.delivery_only:
        if not env.get("AUDIT_GW"):
            raise SystemExit("AUDIT_GW is empty in .env - set it to the Audit API gateway URL (e.g. https://13.61.228.129:8244/sellora-audit/1.0.0), "
                             "or run with --delivery-only")
        if urlparse(env["AUDIT_GW"]).hostname != urlparse(env["DELIVERY_GW"]).hostname:
            raise SystemExit("AUDIT_GW and DELIVERY_GW must be on the same API Manager host for the load test (AUDIT_GW is "
                             f"{env['AUDIT_GW']}). Publish the Audit API in APIM and use its gateway URL, or run with --delivery-only")
    report = Report("load-data")
    s = build_scenario(args, env, report, delivery_base(env, "staging"))
    out = pathlib.Path("load")
    out.mkdir(exist_ok=True)

    # 1. order references that the audit log really has
    refs = []
    if args.delivery_only:
        (out / "refs.csv").write_text("NONE\n", encoding="utf-8")
        print("--delivery-only: audit, report and export groups skipped (0 users)")
    audit = Api(audit_base(env, "staging"), "audit")
    for page in (range(1, 6) if not args.delivery_only else []):
        r = audit.get(f"/api/audit?pageSize=100&page={page}", s.token("admin"), soft=True)
        if r.status != 200:
            raise SystemExit(f"GET /api/audit answered {r.status} {r.text[:300]} - is the Audit API published in APIM?")
        refs += [ci_get(e, "orderReference") for e in items_of(r.body) if ci_get(e, "orderReference")]
        if len(items_of(r.body)) < 100:
            break
    refs = sorted(set(refs))
    if not args.delivery_only:
        if not refs:
            raise SystemExit("The audit log has no order references yet - place a few orders first (run delivery_creation_tests.py).")
        (out / "refs.csv").write_text("\n".join(refs) + "\n", encoding="utf-8")
        print(f"refs.csv: {len(refs)} order references")

    # 2. delivery job IDs the operator can open
    r = s.delivery.get("/api/deliveries?pageSize=100&includeHandovers=true", s.token("op"))
    ids = [ci_get(j, "id") or ci_get(j, "deliveryJobId") for j in items_of(r.body)] if r.status == 200 else []
    ids = [i for i in ids if i]
    if not ids:
        raise SystemExit(f"Operator delivery list is empty or failed ({r.status}) - run delivery_creation_tests.py first.")
    (out / "deliveries.csv").write_text("\n".join(ids) + "\n", encoding="utf-8")
    print(f"deliveries.csv: {len(ids)} delivery jobs")

    # 3. an InTransit job of the test rep for live tracking
    r = s.delivery.get("/api/deliveries?status=InTransit&pageSize=20", s.token("rep"))
    in_transit = next((ci_get(j, "id") for j in items_of(r.body) if ci_get(j, "status") == "InTransit"), None) if r.status == 200 else None
    if not in_transit:
        print("No InTransit job for the rep - creating one (order, approve, assign, InTransit) ...")
        try:
            job = s.assigned_job()
        except SetupError as error:
            raise SystemExit(f"Could not create a delivery job: {error}")
        moved = s.set_status(ci_get(job, "id"), "InTransit")
        if moved.status not in (200, 204):
            raise SystemExit(f"InTransit failed: {moved.status} {moved.text[:300]}")
        in_transit = ci_get(job, "id")
    print(f"InTransit job for tracking: {in_transit}")

    # 4. properties for JMeter
    today = colombo_today()
    month_from, month_to = month_range(today)
    prev_from, prev_to = month_range(month_from - timedelta(days=1))
    week_from = f"{today - timedelta(days=7)}T00:00:00+05:30"
    week_to = f"{today}T23:59:59+05:30"
    gw = urlparse(env["DELIVERY_GW"])
    props = {
        "host": gw.hostname, "port": gw.port or 443,
        "delivery_base": urlparse(env["DELIVERY_GW"]).path.rstrip("/"),
        "audit_base": urlparse(env.get("AUDIT_GW", "")).path.rstrip("/") or "/sellora-audit/1.0.0",
        "refsfile": str((out / "refs.csv").resolve()).replace("\\", "/"),
        "deliveriesfile": str((out / "deliveries.csv").resolve()).replace("\\", "/"),
        "intransit_id": in_transit,
        "month_from": month_from, "month_to": month_to, "prev_from": prev_from, "prev_to": prev_to,
        "week_from": week_from, "week_to": week_to,
        "duration": args.duration, "rampup": args.rampup, "sla_ms": args.sla_ms, "sla_download_ms": max(3000, args.sla_ms * 3),
        "token_admin": env["TOKEN_ADMIN_A"], "token_am": env.get("TOKEN_AREA_MANAGER_A") or env["TOKEN_ADMIN_A"],
        "token_op": env["TOKEN_OPERATOR_A"], "token_rep": env["TOKEN_REP_A"], "token_shop": env["TOKEN_SHOP_A"],
    }
    for key, users in USERS.items():
        props[key] = max(1, round(users * args.scale)) if key != "track_posters" else 1
        if args.delivery_only and key in ("audit_users", "report_users", "report_am_users", "export_users"):
            props[key] = 0
    lines = ["# Generated by make_load_data.py - CONTAINS ACCESS TOKENS. Never zip or commit this file."]
    lines += [f"{k}={v}" for k, v in props.items()]
    (out / "load.properties").write_text("\n".join(lines) + "\n", encoding="utf-8")
    total = sum(props[k] for k in USERS)
    print(f"load.properties written: {total} concurrent users for {args.duration} s (scale {args.scale}), SLA {args.sla_ms} ms")
    print("Tokens last about an hour - start JMeter now (RUN-LOAD.bat).")


if __name__ == "__main__":
    main()
