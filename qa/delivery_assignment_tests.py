"""
US-E6-2 QA - assign and schedule a delivery job (CSP-262).

Writes evidence/CSP-262/api/delivery-assignment-<target>.txt:
  US-E6-2-Q1  Successful assignment (Assigned, rep + date shown, history row, DeliveryStatusChanged Pending->Assigned)
  US-E6-2-Q2  Rep from another territory -> 400 naming the territory, job stays Pending
  US-E6-2-Q3  Date rules: yesterday 400, today (Asia/Colombo) accepted
  US-E6-2-Q4  Wrong caller: other agency's operator 404, Sales Rep / Shop Owner 403
  US-E6-2-Q5  Non-pending job (Assigned, InTransit) -> 409, original rep and date unchanged
              + two operators assigning at the same moment -> one 204 and one 409
  US-E6-2-Q6  Eligible reps follow the rep's status in Organization (deactivate / reactivate)
  US-E6-2-Q7  Organization down -> 503 naming Organization, not 500
  US-E6-2-D1  Gateway rejects a request without a token (401 from APIM, staging only)

Personas (.env): TOKEN_OPERATOR_A, TOKEN_OPERATOR_OTHER, TOKEN_REP_A, TOKEN_REP_OTHER, TOKEN_SHOP_A, TOKEN_ADMIN_A.
REP_OTHER_ID must be an Active rep of a different territory (it is read from TOKEN_REP_OTHER's scope if not set).

Run:  python delivery_assignment_tests.py --target staging [--interactive]
"""
import argparse
import threading
from datetime import datetime, timedelta, timezone

from sellora_qa import (COLOMBO, Report, build_scenario, ci_get, colombo_today, delivery_base, delivery_events,
                        load_env, now_utc, pause, run_case, standard_args, sub_of)

STORY = "CSP-262"


def new_pending(s):
    job = s.scheduled_job(quantity=2)
    if job is None:
        from sellora_qa import SetupError
        raise SetupError("delivery job did not appear within 60 s")
    return job


def q1_success(s, r):
    s.need("repId")
    job = new_pending(s)
    job_id, reference = ci_get(job, "id"), ci_get(job, "orderReference")
    reps = s.delivery.get(f"/api/deliveries/{job_id}/eligible-reps", s.token("op"))
    r.expect_status("Q1 eligible-reps as the operator", reps, 200)
    rep_ids = [ci_get(x, "salesRepId") for x in (reps.body if isinstance(reps.body, list) else [])]
    r.check("Q1 the territory's active rep is eligible", s.ids["repId"] in rep_ids, str(reps.body)[:300])
    r.check("Q1 every eligible rep is Active and in the job's territory",
            all(ci_get(x, "status") == "Active" and
                ci_get(ci_get(x, "currentTerritory") or {}, "territoryId") == ci_get(job, "territoryId")
                for x in (reps.body if isinstance(reps.body, list) else [])), str(reps.body)[:300])

    tomorrow = colombo_today() + timedelta(days=1)
    since = now_utc()
    response = s.assign(job_id, scheduled=tomorrow)
    r.expect_status(f"Q1 assign to the territory's rep for {tomorrow}", response, 204)
    after = s.detail(job_id)
    r.check("Q1 status Assigned", ci_get(after, "status") == "Assigned", ci_get(after, "status"))
    r.check("Q1 rep id and name shown", ci_get(after, "assignedRepId") == s.ids["repId"]
            and bool(ci_get(after, "assignedRepName")), f"{ci_get(after, 'assignedRepId')} {ci_get(after, 'assignedRepName')}")
    r.check("Q1 scheduled date shown", str(ci_get(after, "scheduledDate")) == str(tomorrow), str(ci_get(after, "scheduledDate")))
    history = ci_get(after, "history") or []
    r.check("Q1 history row Assigned by AgencyOperator",
            any(ci_get(h, "status") == "Assigned" and "AgencyOperator" in str(ci_get(h, "actorRole")) for h in history),
            str(history))
    if not s.tap.available:
        r.blocked("Q1 DeliveryStatusChanged on sellora.delivery.v1", s.tap.reason)
        return
    events = delivery_events(s.tap, since, reference, "DeliveryStatusChanged")
    r.check("Q1 one DeliveryStatusChanged published", len(events) == 1, f"{len(events)} found")
    if events:
        payload = events[0].json
        r.attach("kafka/US-E6-2-Q1-DeliveryStatusChanged.json", events[0].as_evidence())
        r.check("Q1 previousStatus Pending, status Assigned",
                ci_get(payload, "previousStatus") == "Pending" and ci_get(payload, "status") == "Assigned", str(payload)[:300])
        r.check("Q1 scheduledFor set to the chosen date", str(ci_get(payload, "scheduledFor")).startswith(str(tomorrow)),
                str(ci_get(payload, "scheduledFor")))
        actor = ci_get(payload, "actor") or {}
        r.check("Q1 actor is the operator (sub from the token)", ci_get(actor, "userId") == sub_of(s.token("op")),
                str(actor))
    r.note("Assigned is not a notifiable status - no email expected (checked in US-E6-3-Q6)")


def q2_other_territory(s, r):
    s.need("repOtherId")
    job = new_pending(s)
    job_id = ci_get(job, "id")
    response = s.assign(job_id, rep_id=s.ids["repOtherId"])
    r.expect_status("Q2 rep from another territory", response, 400)
    detail = str(response.detail()).lower()
    r.check("Q2 message names the territory mismatch", "territory" in detail, response.detail())
    r.check("Q2 job still Pending and unassigned",
            ci_get(s.detail(job_id), "status") == "Pending" and ci_get(s.detail(job_id), "assignedRepId") is None)


def q3_dates(s, r):
    job = new_pending(s)
    job_id = ci_get(job, "id")
    today = colombo_today()
    response = s.assign(job_id, scheduled=today - timedelta(days=1))
    r.expect_status("Q3 yesterday (Colombo)", response, 400)
    r.check("Q3 job unchanged after the rejected date", ci_get(s.detail(job_id), "status") == "Pending")
    response = s.assign(job_id, scheduled=today)
    r.expect_status(f"Q3 today in Colombo ({today})", response, 204)
    utc_date = datetime.now(timezone.utc).date()
    if utc_date != today:
        r.note(f"This run is inside the 00:00-05:30 Colombo window (UTC date {utc_date}, Colombo {today}) - "
               "the timezone case is proven by the line above")
    else:
        r.manual("Q3 timezone window", [
            f"Re-run this script between 00:00 and 05:30 Sri Lanka time (now {datetime.now(COLOMBO):%H:%M})",
            "The 'today in Colombo' line must still PASS - a UTC comparison would reject it"])


def q4_wrong_caller(s, r):
    job = new_pending(s)
    job_id = ci_get(job, "id")
    if s.has("op_other"):
        r.expect_status("Q4 other agency's operator: eligible-reps", s.delivery.get(
            f"/api/deliveries/{job_id}/eligible-reps", s.token("op_other")), 404)
        r.expect_status("Q4 other agency's operator: assignment", s.assign(job_id, persona="op_other"), 404)
    else:
        r.blocked("Q4 other agency's operator", "TOKEN_OPERATOR_OTHER not set")
    r.expect_status("Q4 Sales Rep: assignment", s.assign(job_id, persona="rep"), 403)
    r.expect_status("Q4 Shop Owner: assignment", s.assign(job_id, persona="shop"), 403)
    r.expect_status("Q4 Sales Rep: eligible-reps", s.delivery.get(f"/api/deliveries/{job_id}/eligible-reps",
                                                                  s.token("rep")), 403)
    r.check("Q4 job unchanged (Pending, unassigned)", ci_get(s.detail(job_id), "status") == "Pending")


def q5_not_pending(s, r):
    job = s.assigned_job(quantity=2)
    job_id = ci_get(job, "id")
    before = (ci_get(job, "assignedRepId"), str(ci_get(job, "scheduledDate")))
    response = s.assign(job_id, scheduled=colombo_today() + timedelta(days=5))
    r.expect_status("Q5 assign an Assigned job", response, 409)
    r.check("Q5 409 names the current status", "Assigned" in str(response.detail()), response.detail())
    after = s.detail(job_id)
    r.check("Q5 original rep and date unchanged",
            (ci_get(after, "assignedRepId"), str(ci_get(after, "scheduledDate"))) == before, str(after)[:200])

    moved = s.set_status(job_id, "InTransit")
    r.check("Q5 setup: rep starts the delivery", moved.status == 204, moved.text)
    response = s.assign(job_id, scheduled=colombo_today() + timedelta(days=5))
    r.expect_status("Q5 assign an InTransit job", response, 409)
    r.check("Q5 InTransit job keeps its rep and date",
            (ci_get(s.detail(job_id), "assignedRepId"), str(ci_get(s.detail(job_id), "scheduledDate"))) == before)

    race = new_pending(s)
    results = []

    def attempt():
        results.append(s.assign(ci_get(race, "id")).status)
    threads = [threading.Thread(target=attempt) for _ in range(2)]
    [t.start() for t in threads]
    [t.join() for t in threads]
    r.check("Q5 two simultaneous assignments -> one 204 and one 409 (xmin token)",
            sorted(results) == [204, 409], f"got {sorted(results)} (500 = concurrency exception not mapped)")
    history = [h for h in ci_get(s.detail(ci_get(race, "id")), "history") or [] if ci_get(h, "status") == "Assigned"]
    r.check("Q5 only one Assigned history row after the race", len(history) == 1, f"{len(history)} rows")


def q6_eligible_follow_status(s, r):
    job = new_pending(s)
    job_id = ci_get(job, "id")
    steps = ["In Organization, deactivate the territory's rep (Sales reps page, or the API)",
             f"Open delivery {ci_get(job, 'deliveryReference')} > Assign: list is empty and the dialog says "
             "'No eligible reps found'",
             "Reactivate the rep: the rep is listed again (no caching)"]
    if not pause("Q6: DEACTIVATE the territory's sales rep in Organization now.", s.interactive):
        r.manual("Q6 eligible reps follow the rep's status", steps)
        return
    response = s.delivery.get(f"/api/deliveries/{job_id}/eligible-reps", s.token("op"))
    r.check("Q6 deactivated rep not eligible", response.status == 200 and s.ids["repId"] not in
            [ci_get(x, "salesRepId") for x in response.body], response.text[:300])
    pause("Q6: REACTIVATE the rep now.", True)
    response = s.delivery.get(f"/api/deliveries/{job_id}/eligible-reps", s.token("op"))
    r.check("Q6 reactivated rep eligible again (live read, no cache)", response.status == 200 and s.ids["repId"] in
            [ci_get(x, "salesRepId") for x in response.body], response.text[:300])


def q7_org_down(s, r):
    job = new_pending(s)
    job_id = ci_get(job, "id")
    steps = ["Stop the sellora-organization staging slot (or the local organization container)",
             f"GET /api/deliveries/{job_id}/eligible-reps as the operator -> 503, body names Organization",
             f"PUT /api/deliveries/{job_id}/assignment -> 503, job still Pending", "Start Organization again"]
    if not pause("Q7: STOP the Organization service now.", s.interactive):
        r.manual("Q7 Organization down", steps)
        return
    response = s.delivery.get(f"/api/deliveries/{job_id}/eligible-reps", s.token("op"), soft=True)
    r.expect_status("Q7 eligible-reps with Organization down", response, 503)
    r.check("Q7 body names Organization", "organization" in response.text.lower(), response.text[:300])
    response = s.assign(job_id)
    r.expect_status("Q7 assignment with Organization down", response, 503)
    pause("Q7: START Organization again.", True)
    r.check("Q7 job still Pending", ci_get(s.detail(job_id), "status") == "Pending")


def d1_gateway(s, r):
    if s.target != "staging":
        r.note("D1 gateway check runs on staging only")
        return
    response = s.delivery.get("/api/deliveries")
    r.expect_status("D1 no token through APIM", response, 401)
    r.check("D1 401 comes from the gateway (WSO2 error body, not the service's problem+json)",
            "900902" in response.text or "Missing Credentials" in response.text or "Invalid Credentials" in response.text,
            response.text[:300])


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-assignment")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    s.interactive = args.interactive

    report.story(STORY, "US-E6-2 Assign and schedule a delivery job")
    run_case(report, "US-E6-2-Q1", "Successful assignment", q1_success, s, report)
    run_case(report, "US-E6-2-Q2", "Rep from another territory", q2_other_territory, s, report)
    run_case(report, "US-E6-2-Q3", "Date rules", q3_dates, s, report)
    run_case(report, "US-E6-2-Q4", "Wrong caller", q4_wrong_caller, s, report)
    run_case(report, "US-E6-2-Q5", "Non-pending job and concurrent assignment", q5_not_pending, s, report)
    run_case(report, "US-E6-2-Q6", "Eligible reps", q6_eligible_follow_status, s, report)
    run_case(report, "US-E6-2-Q7", "Organization down", q7_org_down, s, report)
    run_case(report, "US-E6-2-D1", "Gateway rejects missing token", d1_gateway, s, report)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
