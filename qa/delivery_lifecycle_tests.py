"""
US-E6-3 QA - delivery status lifecycle and events (CSP-263).

Writes evidence/CSP-263/api/delivery-lifecycle-<target>.txt:
  US-E6-3-Q1  Assigned -> InTransit -> Delivered by the assigned rep; history rows; two DeliveryStatusChanged events
              whose field names match Notification's contract (plus the additive Audit fields and Order's headers)
  US-E6-3-Q2  Invalid transitions rejected (409, or 400 for targets the API never accepts); no event, status unchanged
  US-E6-3-Q3  Failed needs a reason (blank / too long -> 400); with a reason -> Failed, reason in history and event
  US-E6-3-Q4  Ownership: another rep and the operator -> 403
  US-E6-3-Q5  Upstream cancellation: shop cancels an Assigned job's order -> Cancelled with the reason; a Delivered job
              stays Delivered (flag 6.4: record what Inventory does)
  US-E6-3-Q6  Delivery emails (manual mailbox check; the script prints the order references to look for)
  US-E6-3-Q7  Kafka outage: the status change succeeds and is published exactly once after Kafka is back (local)
  Extra       Stale version (another device already moved the job) -> 409 the UI can show

Run:  python delivery_lifecycle_tests.py --target staging        (Q7 needs --target local --interactive)
"""
import argparse
import json

from sellora_qa import (NOTIFICATION_AGENCY_FIELDS, NOTIFICATION_DELIVERY_FIELDS, NOTIFICATION_SHOP_FIELDS,
                        DELIVERY_EXTRA_FIELDS, Report, build_scenario, ci_get, delivery_base, delivery_db,
                        delivery_events, load_env, missing_fields, not_camel_case, now_utc, pause, run_case,
                        standard_args, sub_of, wait_seconds)

STORY = "CSP-263"


def check_contract(r, label, message, expected_previous, expected_status):
    payload = message.json or {}
    r.check(f"{label} previousStatus {expected_previous} -> status {expected_status}",
            ci_get(payload, "previousStatus") == expected_previous and ci_get(payload, "status") == expected_status,
            f"{ci_get(payload, 'previousStatus')} -> {ci_get(payload, 'status')}")
    r.check(f"{label} every Notification contract field present", not missing_fields(payload, NOTIFICATION_DELIVERY_FIELDS),
            f"missing {missing_fields(payload, NOTIFICATION_DELIVERY_FIELDS)}")
    r.check(f"{label} shop{{shopId,name,ownerName,ownerEmail}} present",
            not missing_fields(ci_get(payload, "shop") or {}, NOTIFICATION_SHOP_FIELDS), str(ci_get(payload, "shop")))
    r.check(f"{label} agency{{agencyId,name,email}} present",
            not missing_fields(ci_get(payload, "agency") or {}, NOTIFICATION_AGENCY_FIELDS), str(ci_get(payload, "agency")))
    r.check(f"{label} additive fields deliveryReference, territoryId, provinceId, actor present",
            not missing_fields(payload, DELIVERY_EXTRA_FIELDS), f"missing {missing_fields(payload, DELIVERY_EXTRA_FIELDS)}")
    r.check(f"{label} schemaVersion 1.0", ci_get(payload, "schemaVersion") == "1.0", str(ci_get(payload, "schemaVersion")))
    r.check(f"{label} key = order reference", message.key == ci_get(payload, "orderReference"), message.key)
    r.check(f"{label} correlationId filled", bool(ci_get(payload, "correlationId")),
            "null - Audit search by correlation ID (US-E7-1-Q1) cannot link it to the request")
    event_id_header = message.header("event-id")
    r.check(f"{label} 'event-id' header = payload eventId (Order's header set, backlog BE4)",
            event_id_header is not None and event_id_header == str(ci_get(payload, "eventId")),
            f"headers {sorted(message.headers)}")
    if not_camel_case(payload):
        r.warn(f"{label} JSON is PascalCase; Notification's contract says camelCase",
               "works today only because Notification and Inventory read case-insensitively")


def q1_progression(s, r, db):
    job = s.assigned_job(quantity=4)
    job_id, reference = ci_get(job, "id"), ci_get(job, "orderReference")
    since = now_utc()
    r.expect_status("Q1 rep: Assigned -> InTransit", s.set_status(job_id, "InTransit"), 204)
    r.expect_status("Q1 rep: InTransit -> Delivered", s.set_status(job_id, "Delivered"), 204)
    after = s.detail(job_id)
    r.check("Q1 status Delivered and DeliveredAt set", ci_get(after, "status") == "Delivered"
            and ci_get(after, "deliveredAt") is not None, str(after)[:200])
    history = [h for h in ci_get(after, "history") or [] if ci_get(h, "status") in ("InTransit", "Delivered")]
    r.check("Q1 history has InTransit and Delivered rows with actor role and time",
            [ci_get(h, "status") for h in history] == ["InTransit", "Delivered"]
            and all(ci_get(h, "actorRole") and ci_get(h, "occurredAt") for h in history), str(history))
    rep_sub = sub_of(s.token("rep"))
    if any(ci_get(h, "actorUserId") for h in history):
        r.check("Q1 history rows carry the rep's user ID", all(ci_get(h, "actorUserId") == rep_sub for h in history))
    else:
        r.warn("Q1 detail response has no actorUserId on history rows",
               "AC says 'history shows the rep's user ID'; only actorRole is returned")
        if db and db.configured:
            actors = db.rows(f"select actor_user_id from delivery_status_history where delivery_job_id = '{job_id}' "
                             "and to_status in ('InTransit','Delivered') order by occurred_at")
            r.check("Q1 delivery_status_history stores the rep's sub", [a[0] for a in actors] == [rep_sub] * 2,
                    str(actors))
    if not s.tap.available:
        r.blocked("Q1 events on sellora.delivery.v1", s.tap.reason)
        return
    events = delivery_events(s.tap, since, reference, "DeliveryStatusChanged", min_count=2)
    r.check("Q1 exactly two DeliveryStatusChanged events", len(events) == 2, f"{len(events)} found")
    for message in events:
        r.attach(f"kafka/US-E6-3-Q1-{ci_get(message.json, 'status')}.json", message.as_evidence())
    ordered = sorted(events, key=lambda m: m.offset)
    if len(ordered) >= 1:
        check_contract(r, "Q1 event 1", ordered[0], "Assigned", "InTransit")
    if len(ordered) >= 2:
        check_contract(r, "Q1 event 2", ordered[1], "InTransit", "Delivered")
        actor = ci_get(ordered[1].json, "actor") or {}
        r.check("Q1 actor{userId, role} is the rep", ci_get(actor, "userId") == rep_sub
                and "SalesRep" in str(ci_get(actor, "role")), str(actor))


def assert_rejected(s, r, label, job_id, status, expected_codes, reason=None, persona="rep", since=None):
    before = s.detail(job_id)
    response = s.set_status(job_id, status, reason=reason, persona=persona)
    r.expect_status(label, response, *expected_codes)
    if 409 in expected_codes and response.status == 409:
        r.check(f"{label}: message names both states",
                str(ci_get(before, "status")) in response.detail() and status in response.detail(), response.detail())
    r.check(f"{label}: status unchanged", ci_get(s.detail(job_id), "status") == ci_get(before, "status"))
    return response


def q2_invalid(s, r, db):
    since = now_utc()
    pending = s.scheduled_job(quantity=1)
    response = s.set_status(ci_get(pending, "id"), "Delivered")
    r.check("Q2 Pending -> Delivered rejected (409; 403 also acceptable because the job has no rep yet)",
            response.status in (409, 403), f"{response.status} {response.text[:200]}")
    if response.status == 403:
        r.note("Pending -> Delivered answered 403: ownership is checked before the transition table")

    delivered = s.delivered_job(quantity=1)
    assert_rejected(s, r, "Q2 Delivered -> InTransit", ci_get(delivered, "id"), "InTransit", (409,))

    cancelled = s.assigned_job(quantity=1)
    s.cancel_order(cancelled["_order"] if "_order" in cancelled else {"orderId": ci_get(cancelled, "orderId")},
                   reason="QA cancel for Q2")
    became_cancelled = s.detail(ci_get(cancelled, "id"))
    for _ in range(15):
        if ci_get(became_cancelled, "status") == "Cancelled":
            break
        wait_seconds(2, "waiting for OrderCancelled to reach Delivery")
        became_cancelled = s.detail(ci_get(cancelled, "id"))
    if ci_get(became_cancelled, "status") == "Cancelled":
        assert_rejected(s, r, "Q2 Cancelled -> InTransit", ci_get(cancelled, "id"), "InTransit", (409,))
    else:
        r.blocked("Q2 Cancelled -> InTransit", f"could not get a Cancelled job (status {ci_get(became_cancelled, 'status')})")

    assigned = s.assigned_job(quantity=1)
    assert_rejected(s, r, "Q2 rep sends Cancelled (API never accepts it)", ci_get(assigned, "id"), "Cancelled", (400,))
    assert_rejected(s, r, "Q2 rep sends Assigned (API never accepts it)", ci_get(assigned, "id"), "Assigned", (400,))
    assert_rejected(s, r, "Q2 rep sends Pending", ci_get(assigned, "id"), "Pending", (400,))
    assert_rejected(s, r, "Q2 Assigned -> Delivered (skips InTransit)", ci_get(assigned, "id"), "Delivered", (409,))
    if s.tap.available:
        events = delivery_events(s.tap, since, ci_get(assigned, "orderReference"), "DeliveryStatusChanged", timeout=15)
        r.check("Q2 rejected requests published nothing (only the assignment event exists)",
                len(events) == 1, f"{len(events)} DeliveryStatusChanged for {ci_get(assigned, 'orderReference')}")


def q3_failed_reason(s, r, db):
    job = s.assigned_job(quantity=1)
    job_id = ci_get(job, "id")
    s.set_status(job_id, "InTransit")
    assert_rejected(s, r, "Q3 Failed with no reason", job_id, "Failed", (400,))
    assert_rejected(s, r, "Q3 Failed with a blank reason", job_id, "Failed", (400,), reason="   ")
    assert_rejected(s, r, "Q3 Failed with a 501-character reason", job_id, "Failed", (400,), reason="x" * 501)
    since = now_utc()
    reason = "Shop closed - owner not reachable"
    r.expect_status("Q3 Failed with a reason", s.set_status(job_id, "Failed", reason=reason), 204)
    after = s.detail(job_id)
    r.check("Q3 status Failed", ci_get(after, "status") == "Failed")
    r.check("Q3 reason in history", any(ci_get(h, "reason") == reason for h in ci_get(after, "history") or []))
    if s.tap.available:
        events = delivery_events(s.tap, since, ci_get(job, "orderReference"), "DeliveryStatusChanged")
        failed = [m for m in events if ci_get(m.json, "status") == "Failed"]
        r.check("Q3 Failed event carries the reason", failed and ci_get(failed[0].json, "reason") == reason,
                str([m.json for m in failed])[:300])


def q4_ownership(s, r, db):
    job = s.assigned_job(quantity=1)
    job_id = ci_get(job, "id")
    if s.has("rep_other"):
        r.expect_status("Q4 another rep moves rep A's job", s.set_status(job_id, "InTransit", persona="rep_other"), 403)
    else:
        r.blocked("Q4 another rep", "TOKEN_REP_OTHER not set")
    r.expect_status("Q4 Agency Operator sends InTransit", s.set_status(job_id, "InTransit", persona="op"), 403)
    r.expect_status("Q4 Shop Owner sends InTransit", s.set_status(job_id, "InTransit", persona="shop"), 403)
    r.check("Q4 job still Assigned", ci_get(s.detail(job_id), "status") == "Assigned")


def wait_status(s, job_id, wanted, seconds=40):
    detail = s.detail(job_id)
    for _ in range(seconds // 2):
        if ci_get(detail, "status") == wanted:
            break
        wait_seconds(2, f"waiting for {wanted}")
        detail = s.detail(job_id)
    return detail


def q5_upstream_cancel(s, r, db):
    job = s.assigned_job(quantity=1)
    job_id = ci_get(job, "id")
    reason = "QA - shop no longer needs the goods"
    response = s.cancel_order(job.get("_order") or {"orderId": ci_get(job, "orderId")}, reason=reason)
    r.check("Q5 setup: shop owner cancels inside the window", response.status in (200, 204), response.text[:300])
    after = wait_status(s, job_id, "Cancelled")
    r.check("Q5 Assigned job -> Cancelled", ci_get(after, "status") == "Cancelled", ci_get(after, "status"))
    last = (ci_get(after, "history") or [{}])[-1]
    r.check("Q5 history reason carries the order's reason", reason in str(ci_get(last, "reason")), str(last))
    r.check("Q5 reason format '{source}: {reason}' (backlog BE5)",
            str(ci_get(last, "reason")).startswith("ShopCancellation:"), str(ci_get(last, "reason")))
    r.check("Q5 actor System", ci_get(last, "actorRole") == "System", str(last))
    rep_list = s.delivery.get("/api/deliveries?pageSize=100", s.token("rep"))
    rep_refs = [ci_get(i, "deliveryReference") for i in (ci_get(rep_list.body, "items") or [])]
    r.check("Q5 cancelled job gone from the rep's default list", ci_get(job, "deliveryReference") not in rep_refs,
            "still listed - the rep list has no default status filter")

    delivered = s.delivered_job(quantity=1)
    stock_before = stock_of(s)
    response = s.cancel_order(delivered.get("_order") or {"orderId": ci_get(delivered, "orderId")},
                              reason="QA - cancel after delivery (flag 6.4)")
    r.note(f"cancel after delivery -> Order answered {response.status} {response.text[:150]}")
    wait_seconds(15, "letting OrderCancelled reach Delivery and Inventory")
    r.check("Q5 Delivered job stays Delivered", ci_get(s.detail(ci_get(delivered, "id")), "status") == "Delivered")
    stock_after = stock_of(s)
    r.note(f"flag 6.4 known limitation - agency stock of product 1 before {stock_before}, after {stock_after} "
           "(a rise means Inventory returned stock that is physically at the shop)")


def stock_of(s):
    owner = s.env.get("AGENCY_A_INVENTORY_OWNER_ID")
    if not (s.inventory.configured and owner and s.ids.get("product1")):
        return "n/a (set INVENTORY_GW and AGENCY_A_INVENTORY_OWNER_ID)"
    response = s.inventory.get(f"/api/stock?productId={s.ids['product1']}&inventoryOwnerId={owner}", s.token("admin"))
    rows = response.body if isinstance(response.body, list) else []
    return sum(ci_get(x, "quantityOnHand", 0) for x in rows)


def q6_emails(s, r, db):
    r.manual("Q6 delivery emails (closes US-E5-4's delivery half)", [
        f"Shop mailbox {s.env.get('SHOP_A_OWNER_EMAIL', '(SHOP_A_OWNER_EMAIL)')} and agency mailbox "
        f"{s.env.get('AGENCY_A_EMAIL', '(AGENCY_A_EMAIL)')}",
        "Each receives ONE email per order for InTransit, Delivered, Failed and Cancelled",
        "NO email for Assigned and none for a cash-sale DeliveryJobCreated",
        "Orders from this run: " + ", ".join(s.mailbox_checklist()),
        "Cross-check in Notification: Failed notifications page shows none for these references",
        "Screenshot each email into evidence/CSP-263/ss/"])


def q7_kafka_outage(s, r, db):
    steps = ["Local only. Have an Assigned job ready",
             "docker stop sellora-kafka", "Move the job to InTransit as the rep -> 204 (outbox stores it)",
             "docker start sellora-kafka and wait 30 s",
             "Exactly one DeliveryStatusChanged InTransit for the order on sellora.delivery.v1"]
    if s.target != "local" or not s.interactive:
        r.manual("Q7 Kafka outage", steps)
        return
    job = s.assigned_job(quantity=1)
    since = now_utc()
    pause(f"Q7: run  docker stop {s.tap.container}", True)
    r.expect_status("Q7 InTransit accepted while Kafka is down", s.set_status(ci_get(job, "id"), "InTransit"), 204)
    if db and db.configured:
        pending = db.scalar(f"select count(*) from outbox_messages where message_key = '{ci_get(job, 'orderReference')}' "
                            "and processed_at is null")
        r.check("Q7 outbox row waiting while Kafka is down", pending not in (None, "0"), str(pending))
    pause(f"Q7: run  docker start {s.tap.container}  (wait for it to be healthy)", True)
    wait_seconds(30, "outbox relay retry")
    events = delivery_events(s.tap, since, ci_get(job, "orderReference"), "DeliveryStatusChanged", timeout=60)
    in_transit = [m for m in events if ci_get(m.json, "status") == "InTransit"]
    r.check("Q7 InTransit published exactly once after Kafka recovered", len(in_transit) == 1, f"{len(in_transit)} found")


def stale_version(s, r, db):
    job = s.assigned_job(quantity=1)
    job_id = ci_get(job, "id")
    stale = ci_get(job, "version")
    s.set_status(job_id, "InTransit", version=stale)
    response = s.set_status(job_id, "Delivered", version=stale)
    r.check("EXTRA stale version (other device moved the job) -> 409 with a message the UI shows",
            response.status == 409, f"got {response.status} {response.text[:200]} (FE-1 shows 409 text inline)")
    missing = s.delivery.patch(f"/api/deliveries/{job_id}/status", {"status": "Delivered"}, s.token("rep"))
    r.check("EXTRA request without 'version' is accepted (sellora-web never sends it)",
            missing.status == 204, f"got {missing.status} {missing.text[:200]} - every UI status button fails")


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-lifecycle")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    s.interactive = args.interactive
    db = delivery_db(env) if args.target == "local" else None

    report.story(STORY, "US-E6-3 Delivery status lifecycle and events")
    run_case(report, "US-E6-3-Q1", "Valid progression", q1_progression, s, report, db)
    run_case(report, "US-E6-3-Q2", "Invalid transitions", q2_invalid, s, report, db)
    run_case(report, "US-E6-3-Q3", "Failed needs a reason", q3_failed_reason, s, report, db)
    run_case(report, "US-E6-3-Q4", "Ownership", q4_ownership, s, report, db)
    run_case(report, "US-E6-3-Q5", "Upstream cancellation", q5_upstream_cancel, s, report, db)
    run_case(report, "US-E6-3-Q7", "Kafka outage", q7_kafka_outage, s, report, db)
    run_case(report, "US-E6-3-EXTRA", "Version token and the web client", stale_version, s, report, db)
    run_case(report, "US-E6-3-Q6", "Delivery emails", q6_emails, s, report, db)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
