"""
US-E6-7 QA - Google Maps live delivery tracking (CSP-267). Endpoints on sellora-delivery dev (CSP-267, 94b43b1).

Writes evidence/CSP-267/api/delivery-tracking-<target>.txt:
  US-E6-7-Q1  Posting rules: assigned rep on InTransit accepted; Assigned job 409 and nothing stored; another rep 403;
              latitude 95 -> 400; capturedAt 10 minutes in the future -> 400; older than 30 minutes -> 400
  US-E6-7-Q2  Read and scope: latest position returned with shop coordinates; another shop's owner 404
  (US-E6-7-Q3 map UI and the blocked-maps fallback: selenium_sprint4_ui.py --scenario map)

Endpoints (backlog US-E6-7-BE2/BE3):
  POST /api/deliveries/{id}/location  { latitude, longitude, accuracyMeters?, capturedAt }
  GET  /api/deliveries/{id}/location

Run:  python delivery_tracking_tests.py --target staging
"""
import argparse
from datetime import timedelta

from sellora_qa import (Report, SetupError, build_scenario, ci_get, delivery_base, iso, load_env, now_utc, run_case,
                        standard_args)


def money6(value):
    return round(float(value), 4) if value is not None else None

STORY = "CSP-267"


def post_location(s, job_id, lat=6.9147, lng=79.8730, captured=None, persona="rep"):
    body = {"latitude": lat, "longitude": lng, "accuracyMeters": 8, "capturedAt": iso(captured or now_utc())}
    return s.delivery.post(f"/api/deliveries/{job_id}/location", body, s.token(persona))


def get_location(s, job_id, persona="shop"):
    return s.delivery.get(f"/api/deliveries/{job_id}/location", s.token(persona))


def q1_posting(s, r, ctx):
    assigned = s.assigned_job(quantity=1)
    probe = post_location(s, ci_get(assigned, "id"))
    if probe.status in (404, 405):
        raise SetupError("POST /api/deliveries/{id}/location not deployed (404/405)")
    r.expect_status("Q1 position on an Assigned job", probe, 409)
    stored = get_location(s, ci_get(assigned, "id"), persona="admin")
    r.check("Q1 nothing stored for the Assigned job", stored.status in (200, 404) and
            not ci_get(stored.body, "lastLatitude"), stored.text[:200])

    moving = s.assigned_job(quantity=1)
    job_id = ci_get(moving, "id")
    s.set_status(job_id, "InTransit")
    ctx["job"] = moving
    r.expect_status("Q1 assigned rep posts on InTransit", post_location(s, job_id, 6.9150, 79.8735), 200, 201, 204)
    if s.has("rep_other"):
        r.expect_status("Q1 another rep posts", post_location(s, job_id, persona="rep_other"), 403)
    r.expect_status("Q1 latitude 95", post_location(s, job_id, lat=95), 400)
    r.expect_status("Q1 longitude 181", post_location(s, job_id, lng=181), 400)
    r.expect_status("Q1 capturedAt 10 minutes in the future", post_location(s, job_id, captured=now_utc() + timedelta(minutes=10)), 400)
    r.expect_status("Q1 capturedAt 31 minutes old", post_location(s, job_id, captured=now_utc() - timedelta(minutes=31)), 400)
    r.expect_status("Q1 operator posts (rep only)", post_location(s, job_id, persona="op"), 403)


def q2_read(s, r, ctx):
    job = ctx.get("job")
    if not job:
        raise SetupError("needs Q1's InTransit job")
    job_id = ci_get(job, "id")
    post_location(s, job_id, 6.9160, 79.8740)
    response = get_location(s, job_id)
    r.expect_status("Q2 destination shop owner reads the position", response, 200)
    body = response.body
    position = ci_get(body, "position") or {}
    shop = ci_get(body, "shop") or {}
    r.check("Q2 latest position returned", money6(ci_get(position, "latitude")) == 6.9160
            and money6(ci_get(position, "longitude")) == 79.8740, str(body)[:300])
    r.check("Q2 last update time returned (position.capturedAt)", bool(ci_get(position, "capturedAt")), str(position))
    r.check("Q2 shop coordinates returned (snapshotted at assignment)",
            ci_get(shop, "latitude") is not None and ci_get(shop, "longitude") is not None, str(shop))
    r.check("Q2 status returned so the map can stop polling", ci_get(body, "status") == "InTransit", str(ci_get(body, "status")))
    for persona in ("rep", "op", "admin"):
        r.expect_status(f"Q2 {persona} reads the position", get_location(s, job_id, persona), 200)
    if s.has("shop_other"):
        r.expect_status("Q2 another shop's owner", get_location(s, job_id, "shop_other"), 404)
    if s.has("op_other"):
        r.expect_status("Q2 another agency's operator", get_location(s, job_id, "op_other"), 404)


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-tracking")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    ctx = {}
    report.story(STORY, "US-E6-7 Google Maps live delivery tracking")
    run_case(report, "US-E6-7-Q1", "Posting rules", q1_posting, s, report, ctx)
    run_case(report, "US-E6-7-Q2", "Read and scope", q2_read, s, report, ctx)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
