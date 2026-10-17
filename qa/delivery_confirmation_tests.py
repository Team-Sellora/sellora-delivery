"""
US-E6-5 QA - shop-owner delivery confirmation (CSP-265).

Writes evidence/CSP-265/api/delivery-confirmation-<target>.txt:
  US-E6-5-Q1  Destination shop owner confirms a Delivered job; record (user, time) stored; DeliveryConfirmed published
  US-E6-5-Q2  Dispute with a note; stored; DeliveryDisputed published with Notification's fields (emails: manual)
  US-E6-5-Q3  Validation: dispute without note 400, other shop's owner 403, job not Delivered 409, cash sale 409
  US-E6-5-Q4  Only once: second confirmation 409 (first unchanged); two parallel requests -> exactly one row
  Extra       Confirmation state on detail ('Awaiting' before) and confirmationState on list rows

Endpoint (backlog US-E6-5-BE1): POST /api/deliveries/{id}/confirmation  { outcome: Confirmed | Disputed, note? }
The code is on sellora-delivery dev (1d01173). Staging needs the BUG-01 fix (branch bugfix/staging-jwt-backchannel) before it can run.

Run:  python delivery_confirmation_tests.py --target staging
"""
import argparse
import threading

from sellora_qa import (NOTIFICATION_AGENCY_FIELDS, NOTIFICATION_SHOP_FIELDS, Report, SetupError, build_scenario,
                        ci_get, delivery_base, delivery_db, delivery_events, items_of, load_env, missing_fields,
                        now_utc, run_case, standard_args, sub_of)

STORY = "CSP-265"


def confirm(s, job_id, outcome, note=None, persona="shop"):
    body = {"outcome": outcome}
    if note is not None:
        body["note"] = note
    return s.delivery.post(f"/api/deliveries/{job_id}/confirmation", body, s.token(persona))


def deployed(s, ctx):
    """True when the endpoint exists: a POST on a real Delivered job must not be 404/405."""
    if "deployed" not in ctx:
        job = s.delivered_job(quantity=1)
        probe = s.delivery.post(f"/api/deliveries/{ci_get(job, 'id')}/confirmation", {}, s.token("shop"))
        ctx["deployed"] = probe.status not in (404, 405)
        ctx["probe_job"] = job
    if not ctx["deployed"]:
        raise SetupError("POST /api/deliveries/{id}/confirmation not deployed on this target (404/405)")


def q1_confirm(s, r, ctx, db):
    deployed(s, ctx)
    job = s.delivered_job(quantity=2)
    job_id, reference = ci_get(job, "id"), ci_get(job, "orderReference")
    state = ci_get(ci_get(job, "confirmation") or {}, "state")
    r.check("EXTRA detail shows confirmation 'Awaiting' before the shop acts", state == "Awaiting", str(ci_get(job, "confirmation")))
    rows = items_of(s.delivery.get("/api/deliveries?status=Delivered&pageSize=100", s.token("shop")).body)
    row = next((i for i in rows if ci_get(i, "id") == job_id), {})
    r.check("EXTRA shop's list row has confirmationState 'Awaiting' (drives the prompt badge)",
            ci_get(row, "confirmationState") == "Awaiting", str(row)[:200])

    since = now_utc()
    response = confirm(s, job_id, "Confirmed")
    r.expect_status("Q1 shop owner confirms receipt", response, 200, 201)
    after = ci_get(s.detail(job_id), "confirmation") or {}
    r.check("Q1 confirmation stored as Confirmed with a time", ci_get(after, "state") == "Confirmed"
            and (ci_get(after, "time") or ci_get(after, "confirmedAt")), str(after))
    if db and db.configured:
        stored = db.rows(f'select count(*) from "DeliveryConfirmations" where "DeliveryJobId" = \'{job_id}\'')
        r.check("Q1 one confirmation row in delivery_db", stored and stored[0][0] == "1", str(stored))
    if not s.tap.available:
        r.blocked("Q1 DeliveryConfirmed event", s.tap.reason)
        return
    events = delivery_events(s.tap, since, reference, "DeliveryConfirmed")
    r.check("Q1 DeliveryConfirmed published once", len(events) == 1, f"{len(events)} found")
    if events:
        r.attach("kafka/US-E6-5-Q1-DeliveryConfirmed.json", events[0].as_evidence())
        actor = ci_get(events[0].json, "actor") or {}
        r.check("Q1 event actor is the shop owner's user ID", ci_get(actor, "userId") == sub_of(s.token("shop")), str(actor))


def q2_dispute(s, r, ctx, db):
    deployed(s, ctx)
    job = s.delivered_job(quantity=2)
    job_id, reference = ci_get(job, "id"), ci_get(job, "orderReference")
    since = now_utc()
    response = confirm(s, job_id, "Disputed", note="2 cases missing")
    r.expect_status("Q2 shop owner disputes with '2 cases missing'", response, 200, 201)
    after = ci_get(s.detail(job_id), "confirmation") or {}
    r.check("Q2 stored as Disputed with the note", ci_get(after, "state") == "Disputed"
            and "2 cases missing" in str(ci_get(after, "note")), str(after))
    if s.tap.available:
        events = delivery_events(s.tap, since, reference, "DeliveryDisputed")
        r.check("Q2 DeliveryDisputed published once", len(events) == 1, f"{len(events)} found")
        if events:
            payload = events[0].json
            r.attach("kafka/US-E6-5-Q2-DeliveryDisputed.json", events[0].as_evidence())
            r.check("Q2 disputeReason = note", ci_get(payload, "disputeReason") == "2 cases missing", str(payload)[:300])
            r.check("Q2 raisedByRole ShopOwner", ci_get(payload, "raisedByRole") == "ShopOwner")
            r.check("Q2 shop and agency blocks present (Notification emails both)",
                    not missing_fields(ci_get(payload, "shop") or {}, NOTIFICATION_SHOP_FIELDS)
                    and not missing_fields(ci_get(payload, "agency") or {}, NOTIFICATION_AGENCY_FIELDS))
    else:
        r.blocked("Q2 DeliveryDisputed event", s.tap.reason)
    r.manual("Q2 dispute emails", [f"Shop and agency mailboxes each receive one dispute email for {reference}",
                                    "Screenshot into evidence/CSP-265/ss/"])


def q3_validation(s, r, ctx, db):
    deployed(s, ctx)
    job = s.delivered_job(quantity=1)
    job_id = ci_get(job, "id")
    r.expect_status("Q3 dispute without a note", confirm(s, job_id, "Disputed"), 400)
    r.expect_status("Q3 dispute with a blank note", confirm(s, job_id, "Disputed", note="  "), 400)
    r.expect_status("Q3 note over 1000 characters", confirm(s, job_id, "Disputed", note="x" * 1001), 400)
    r.expect_status("Q3 unknown outcome", confirm(s, job_id, "Maybe"), 400)
    if s.has("shop_other"):
        r.expect_status("Q3 another shop's owner", confirm(s, job_id, "Confirmed", persona="shop_other"), 403)
    else:
        r.blocked("Q3 another shop's owner", "TOKEN_SHOP_OTHER not set")
    r.expect_status("Q3 sales rep confirms", confirm(s, job_id, "Confirmed", persona="rep"), 403)
    r.expect_status("Q3 agency operator confirms", confirm(s, job_id, "Confirmed", persona="op"), 403)
    in_transit = s.assigned_job(quantity=1)
    s.set_status(ci_get(in_transit, "id"), "InTransit")
    r.expect_status("Q3 job InTransit (not Delivered)", confirm(s, ci_get(in_transit, "id"), "Confirmed"), 409)
    cash, _, _ = s.cash_sale_job()
    if cash:
        r.expect_status("Q3 cash-sale handover record", confirm(s, ci_get(cash, "id"), "Confirmed"), 409)
    r.check("Q3 rejected attempts stored nothing", ci_get(ci_get(s.detail(job_id), "confirmation") or {}, "state") == "Awaiting")


def q4_only_once(s, r, ctx, db):
    deployed(s, ctx)
    job = s.delivered_job(quantity=1)
    job_id = ci_get(job, "id")
    r.expect_status("Q4 first confirmation", confirm(s, job_id, "Confirmed"), 200, 201)
    first = ci_get(s.detail(job_id), "confirmation")
    repeat = confirm(s, job_id, "Confirmed")
    if repeat.status == 409:
        r.check("Q4 second confirmation (same outcome) -> 409", True)
    elif repeat.status == 200:
        r.warn("Q4 second confirmation (same outcome) answered 200 (idempotent) - backlog Q4 says 409",
               "dev code returns the existing confirmation for the same outcome (HandleExistingConfirmation); agree with the team which is intended")
    else:
        r.check("Q4 second confirmation (same outcome) -> 409", False, f"got {repeat.status} {repeat.text[:300]}")
    r.expect_status("Q4 second confirmation (dispute after confirm)", confirm(s, job_id, "Disputed", note="late"), 409)
    r.check("Q4 first record unchanged", ci_get(s.detail(job_id), "confirmation") == first, str(first))

    race = s.delivered_job(quantity=1)
    codes = []

    def attempt(outcome, note):
        codes.append(confirm(s, ci_get(race, "id"), outcome, note=note).status)
    threads = [threading.Thread(target=attempt, args=("Confirmed", None)),
               threading.Thread(target=attempt, args=("Disputed", "parallel"))]
    [t.start() for t in threads]
    [t.join() for t in threads]
    r.check("Q4 two parallel requests -> one success and one 409", sorted(codes)[1] == 409 and sorted(codes)[0] in (200, 201),
            f"got {sorted(codes)}")
    if db and db.configured:
        count = db.scalar(f'select count(*) from "DeliveryConfirmations" where "DeliveryJobId" = \'{ci_get(race, "id")}\'')
        r.check("Q4 exactly one confirmation row after the race", count == "1", str(count))


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-confirmation")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    db = delivery_db(env) if args.target == "local" else None
    ctx = {}

    report.story(STORY, "US-E6-5 Shop-owner delivery confirmation")
    run_case(report, "US-E6-5-Q1", "Confirm", q1_confirm, s, report, ctx, db)
    run_case(report, "US-E6-5-Q2", "Dispute", q2_dispute, s, report, ctx, db)
    run_case(report, "US-E6-5-Q3", "Validation", q3_validation, s, report, ctx, db)
    run_case(report, "US-E6-5-Q4", "Only once", q4_only_once, s, report, ctx, db)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
