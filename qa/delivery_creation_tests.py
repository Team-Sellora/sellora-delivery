"""
US-E6-1 QA - auto-create a delivery job on order confirmation (CSP-261).

Covers the QA sub-tasks of the story and writes evidence/CSP-261/api/delivery-creation-<target>.txt:
  US-E6-1-Q1  Scheduled delivery creates exactly one Pending job that matches the order
  US-E6-1-Q2  Unapproved and rejected orders create nothing
  US-E6-1-Q3  Cash sale creates a Delivered record (DeliveredAt = check-in time, hidden from the open queue,
              DeliveryJobCreated published, no DeliveryStatusChanged)
  US-E6-1-Q4  Redelivered event creates no second job; DeliveryEventDuplicate is logged
  US-E6-1-Q5  Malformed message goes to sellora.delivery.dead-letter.v1 and the next valid event still works
  US-E6-1-Q6  Consumer downtime: two orders confirmed while Delivery is stopped each get one job

Targets
  local   : Delivery on http://localhost:5297 (DELIVERY_LOCAL), local Kafka container (sellora-kafka) and the
            local delivery_db container. Jobs are created by publishing OrderConfirmed events (--source kafka),
            so every check runs, including DB counts and the dead-letter topic.
  staging : Delivery through API Manager (DELIVERY_GW). Jobs come from real orders placed and approved through
            the Order API (--source order). Kafka checks run when KAFKA_* is set in .env; Q4/Q5 publish to the
            staging order topic only with --allow-produce.

Run from the QA folder:
  python delivery_creation_tests.py --target local
  python delivery_creation_tests.py --target staging [--allow-produce] [--interactive]
"""
import argparse
import json
import uuid

from sellora_qa import (DELIVERY_DLQ, DELIVERY_REF_PATTERN, ORDER_TOPIC, Report, build_scenario, ci_get,
                        container_log_contains, delivery_base, delivery_db, delivery_events, items_of, load_env,
                        not_camel_case, now_utc, pause, run_case, same_instant, standard_args, wait_seconds)

STORY = "CSP-261"


def q1_scheduled_creates_pending(s, r, db):
    since = now_utc()
    job = s.scheduled_job(quantity=7)
    if not r.check("Q1 delivery job appears within 60 s of approval", job is not None):
        return
    order = job["_order"]
    order_id, reference = ci_get(order, "orderId"), ci_get(order, "orderReference")
    r.note(f"order {reference} -> delivery {ci_get(job, 'deliveryReference')} ({ci_get(job, 'id')})")

    r.check("Q1 exactly one job for the order", s.jobs_for_order(order_id) == 1, str(s.jobs_for_order(order_id)))
    if db and db.configured:
        count = db.scalar(f"select count(*) from delivery_jobs where order_id = '{order_id}'")
        r.check("Q1 delivery_db has exactly one delivery_jobs row", count == "1", str(count))
    r.check("Q1 status Pending", ci_get(job, "status") == "Pending", ci_get(job, "status"))
    r.check("Q1 fulfilmentType ScheduledDelivery", ci_get(job, "fulfilmentType") == "ScheduledDelivery",
            ci_get(job, "fulfilmentType"))
    r.check("Q1 delivery reference format DL-yyMMdd-XXXXXX",
            bool(DELIVERY_REF_PATTERN.match(ci_get(job, "deliveryReference") or "")), ci_get(job, "deliveryReference"))
    r.check("Q1 not assigned and not scheduled yet",
            ci_get(job, "assignedRepId") is None and ci_get(job, "scheduledDate") is None, str(job))

    # Compare with the order's own detail (the order detail page) - or the published event on the kafka path
    if s.source == "order":
        source = s.order.get(f"/api/orders/{order_id}", s.token("admin")).body
        source_lines = [(ci_get(l, "productId"), ci_get(l, "quantity"), float(ci_get(l, "lineTotal")))
                        for l in ci_get(source, "lines") or []]
        shop_id, agency_id = ci_get(source, "shopId"), ci_get(source, "agencyId")
    else:
        source = order
        source_lines = [(l["productId"], l["quantity"], float(l["lineTotal"])) for l in order["lines"]]
        shop_id, agency_id = order["shop"]["shopId"], order["agency"]["agencyId"]
    pairs = [("orderId", order_id, ci_get(job, "orderId")),
             ("orderReference", reference, ci_get(job, "orderReference")),
             ("shopId", shop_id, ci_get(job, "shopId")),
             ("agencyId", agency_id, ci_get(job, "agencyId")),
             ("territoryId", ci_get(source, "territoryId"), ci_get(job, "territoryId")),
             ("provinceId", ci_get(source, "provinceId"), ci_get(job, "provinceId"))]
    for name, expected, actual in pairs:
        r.check(f"Q1 {name} matches the order", str(expected).lower() == str(actual).lower(),
                f"order {expected} / job {actual}")
    r.check("Q1 total matches the order", float(ci_get(source, "total")) == float(ci_get(job, "total")),
            f"order {ci_get(source, 'total')} / job {ci_get(job, 'total')}")
    job_lines = [(ci_get(l, "productId"), ci_get(l, "quantity"), float(ci_get(l, "lineTotal")))
                 for l in ci_get(job, "lines") or []]
    r.check("Q1 lines match the order (product, quantity, line total)",
            sorted(map(str, job_lines)) == sorted(map(str, source_lines)), f"order {source_lines} / job {job_lines}")
    snapshot = {k: ci_get(job, k) for k in ("shopName", "shopOwnerName", "shopOwnerEmail", "agencyName", "agencyEmail")}
    r.check("Q1 shop and agency name snapshots filled (needed by Notification)",
            all(snapshot[k] for k in ("shopName", "shopOwnerEmail", "agencyName")), snapshot)
    # The agency email is copied from Organization (via the order). Compare with the source, not just "non-empty".
    tree = s.org.get("/api/hierarchy", s.token("admin"), soft=True)
    source_email = None
    if tree.status == 200:
        for province in ci_get(tree.body, "provinces") or []:
            for agency in ci_get(province, "agencies") or []:
                if str(ci_get(agency, "agencyId")).lower() == str(ci_get(job, "agencyId")).lower():
                    source_email = ci_get(agency, "email") or ""
    if source_email is None:
        r.check("Q1 agency email snapshot filled", bool(snapshot["agencyEmail"]), snapshot)
    elif not source_email:
        r.check("Q1 agency email matches Organization (both empty)", not snapshot["agencyEmail"], snapshot)
        r.warn("Q1 test data: the agency has no email in Organization",
               f"{snapshot['agencyName']} has an empty email, so agency notifications cannot be sent - set it in Organization")
    else:
        r.check("Q1 agency email matches Organization", (snapshot["agencyEmail"] or "").lower() == source_email.lower(),
                f"Organization {source_email} / job {snapshot['agencyEmail']}")

    if not s.tap.available:
        r.blocked("Q1 DeliveryJobCreated on sellora.delivery.v1", s.tap.reason)
        return
    events = delivery_events(s.tap, since, reference, "DeliveryJobCreated")
    r.check("Q1 one DeliveryJobCreated published for the order", len(events) == 1, f"{len(events)} found")
    if events:
        message, payload = events[0], events[0].json
        r.attach("kafka/US-E6-1-Q1-DeliveryJobCreated.json", message.as_evidence())
        r.check("Q1 message key = order reference", message.key == reference, message.key)
        r.check("Q1 event status Pending and deliveryJobId matches",
                ci_get(payload, "status") == "Pending" and ci_get(payload, "deliveryJobId") == ci_get(job, "id"),
                json.dumps(payload)[:300])
        if not_camel_case(payload):
            r.warn("Q1 payload is PascalCase, contract doc says camelCase",
                   f"{not_camel_case(payload)[:5]} - consumers are case-insensitive today, Audit must be too")
        if ci_get(payload, "schemaVersion") is None:
            r.warn("Q1 DeliveryJobCreated has no schemaVersion", "every other Sellora event carries schemaVersion 1.0")
        r.check("Q1 'event-id' header present (UTF-8 GUID, same as Order - backlog 0.7)",
                message.header("event-id") is not None, f"headers: {list(message.headers)}")
        r.check("Q1 a correlation-id header present", message.header("correlation-id", "x-correlation-id") is not None,
                f"headers: {list(message.headers)}")


def q2_unapproved_and_rejected(s, r, db):
    if s.source == "order":
        pending = s.scheduled_job(quantity=1, approve=False)["order"]
        rejected = s.scheduled_job(quantity=1, approve=False)["order"]
        response = s.order.put(f"/api/orders/{ci_get(rejected, 'orderId')}/approval",
                               {"decision": "Reject", "reason": "QA - rejected on purpose"}, s.token("op"))
        r.check("Q2 setup: agency rejects the second order", response.status in (200, 204), response.text)
        wait_seconds(25, "giving the consumer time to (wrongly) create a job")
        r.check(f"Q2 PendingApproval order {ci_get(pending, 'orderReference')} has no job",
                s.jobs_for_order(ci_get(pending, "orderId")) == 0)
        r.check(f"Q2 rejected order {ci_get(rejected, 'orderReference')} has no job",
                s.jobs_for_order(ci_get(rejected, "orderId")) == 0)
        return
    placed = s.order_event("OrderPlaced", "ScheduledDelivery", 1, "product1")
    placed["status"] = "PendingApproval"
    placed.pop("confirmedAt")
    s.publish_order_event(placed)
    cancelled = s.order_event("OrderCancelled", "ScheduledDelivery", 1, "product1")
    cancelled.update({"status": "Cancelled", "source": "AgencyRejection", "reason": "QA - rejected on purpose",
                      "cancelledAt": cancelled["occurredAt"], "cancelledBy": {"userId": "qa", "role": "AgencyOperator"}})
    s.publish_order_event(cancelled)
    wait_seconds(20, "giving the consumer time to (wrongly) create a job")
    r.check(f"Q2 OrderPlaced alone ({placed['orderReference']}) creates no job", s.jobs_for_order(placed["orderId"]) == 0)
    r.check(f"Q2 OrderCancelled for an order with no job ({cancelled['orderReference']}) creates no job",
            s.jobs_for_order(cancelled["orderId"]) == 0)
    if db and db.configured:
        receipt = db.scalar(f"select count(*) from processed_delivery_events where event_id = '{cancelled['eventId']}'")
        r.note(f"OrderCancelled for a missing job recorded as processed: {receipt} row(s)")


def q3_cash_sale(s, r, db):
    since = now_utc()
    job, order, checked_in_at = s.cash_sale_job()
    if not r.check("Q3 cash-sale record appears within 60 s of payment", job is not None):
        return
    reference = ci_get(order, "orderReference")
    r.note(f"cash sale {reference} -> {ci_get(job, 'deliveryReference')}")
    r.check("Q3 status Delivered", ci_get(job, "status") == "Delivered", ci_get(job, "status"))
    r.check("Q3 fulfilmentType ImmediateCashSale", ci_get(job, "fulfilmentType") == "ImmediateCashSale")
    r.check("Q3 DeliveredAt equals the GPS check-in time", same_instant(ci_get(job, "deliveredAt"), checked_in_at),
            f"deliveredAt {ci_get(job, 'deliveredAt')} / check-in {checked_in_at}")
    history = ci_get(job, "history") or []
    r.check("Q3 one history row, actor System", len(history) == 1 and ci_get(history[0], "actorRole") == "System",
            str(history))
    default_list = s.delivery.get(f"/api/deliveries?orderId={ci_get(order, 'orderId')}", s.token("admin"))
    r.check("Q3 not in the open queue (default list hides handovers)",
            default_list.status == 200 and ci_get(default_list.body, "totalCount") == 0, default_list.text[:300])
    pending_list = s.delivery.get("/api/deliveries?status=Pending&status=Delivered&pageSize=100", s.token("op"))
    refs = [ci_get(i, "orderReference") for i in items_of(pending_list.body)]
    r.check("Q3 not in the operator's queue either", reference not in refs)

    if not s.tap.available:
        r.blocked("Q3 event checks", s.tap.reason)
    else:
        created = delivery_events(s.tap, since, reference, "DeliveryJobCreated")
        r.check("Q3 DeliveryJobCreated published with status Delivered",
                len(created) == 1 and ci_get(created[0].json, "status") == "Delivered", f"{len(created)} found")
        changed = delivery_events(s.tap, since, reference, "DeliveryStatusChanged", timeout=15)
        r.check("Q3 no DeliveryStatusChanged for the cash sale (would trigger a 2nd 'delivered' email)",
                len(changed) == 0, f"{len(changed)} found")
        if created:
            r.attach("kafka/US-E6-1-Q3-DeliveryJobCreated.json", created[0].as_evidence())
    r.manual("Q3 shop receives only the Sprint 3 payment receipt, no delivery email",
             [f"Open the test mailbox ({s.env.get('SHOP_A_OWNER_EMAIL', 'shop owner mailbox')})",
              f"Find mail for {reference}: exactly one payment receipt, no 'delivered' email",
              "Screenshot into evidence/CSP-261/ss/"])


def q4_duplicate(s, r, db):
    if not s.tap.can_produce:
        r.manual("Q4 duplicate event (needs Kafka produce rights or an offset reset)", [
            "Stop the Delivery staging app (a consumer group can only be reset while it has no active member)",
            "Move the group back a few messages, in Confluent Cloud > Consumers > sellora.delivery.order.v1 or with the CLI:",
            "kafka-consumer-groups --bootstrap-server <server> --command-config <client.properties> "
            "--group sellora.delivery.order.v1 --topic sellora.order.v1 --reset-offsets --shift-by -5 --execute",
            "Start the Delivery app again and wait 1 minute",
            "Check: job count per order unchanged; Delivery log shows DeliveryEventDuplicate with the order reference"])
        return
    since = now_utc()
    event = s.order_event("OrderConfirmed", "ScheduledDelivery", 3, "product1")
    s.publish_order_event(event)
    first = s.wait_job(event["orderId"])
    r.check("Q4 setup: first delivery creates the job", first is not None)
    s.publish_order_event(event)  # identical message, same eventId
    wait_seconds(15, "letting the consumer process the redelivered event")
    r.check("Q4 same eventId redelivered -> still exactly one job", s.jobs_for_order(event["orderId"]) == 1,
            str(s.jobs_for_order(event["orderId"])))
    logged = container_log_contains(s.env.get("LOCAL_DELIVERY_CONTAINER"), since, "DeliveryEventDuplicate",
                                    event["orderReference"])
    if logged is None:
        r.manual("Q4 DeliveryEventDuplicate logged", [
            f"Search the Delivery console / App Service log stream for 'DeliveryEventDuplicate {event['orderReference']}'"])
    else:
        r.check("Q4 log shows DeliveryEventDuplicate with the order reference", logged)

    second = dict(event, eventId=str(uuid.uuid4()))  # same order, new event id: the unique index is the 2nd guard
    s.publish_order_event(second)
    wait_seconds(15, "letting the consumer process a second OrderConfirmed for the same order")
    r.check("Q4 new eventId for the same order -> still exactly one job (unique OrderId guard)",
            s.jobs_for_order(event["orderId"]) == 1, str(s.jobs_for_order(event["orderId"])))
    dlq = s.tap.wait_for(DELIVERY_DLQ, since, lambda m, p: event["orderReference"] in (m.value or ""), timeout=10)
    if dlq:
        r.warn("Q4 the second OrderConfirmed went to the dead-letter topic",
               "backlog BE3 says log DeliveryEventDuplicate and commit, not dead-letter")


def q5_malformed(s, r, db):
    if not s.tap.can_produce:
        r.blocked("Q5 malformed message", s.tap.reason or "staging: re-run with --allow-produce")
        return
    since = now_utc()
    marker = f"QA-MALFORMED-{s.run}"
    s.tap.produce(ORDER_TOPIC, marker, f"this is not json {{ {marker}")
    found = s.tap.wait_for(DELIVERY_DLQ, since, lambda m, p: marker in (m.value or ""), timeout=45)
    r.check("Q5 non-JSON message lands on sellora.delivery.dead-letter.v1", len(found) >= 1)
    if found:
        payload = found[0].json or {}
        r.attach("kafka/US-E6-1-Q5-dead-letter.json", found[0].as_evidence())
        r.check("Q5 dead letter names source topic, partition and offset",
                ci_get(payload, "sourceTopic") == ORDER_TOPIC and ci_get(payload, "offset") is not None
                and ci_get(payload, "partition") is not None, json.dumps(payload)[:300])
        r.check("Q5 dead letter keeps the original value", marker in str(ci_get(payload, "originalValue")))

    incomplete = s.order_event("OrderConfirmed", "ScheduledDelivery", 1, "product1")
    incomplete["lines"] = []
    s.publish_order_event(incomplete)
    found = s.tap.wait_for(DELIVERY_DLQ, since, lambda m, p: incomplete["orderReference"] in (m.value or ""), timeout=30)
    r.check("Q5 OrderConfirmed without lines (contract violation) is dead-lettered, no job",
            len(found) >= 1 and s.jobs_for_order(incomplete["orderId"]) == 0)

    valid = s.order_event("OrderConfirmed", "ScheduledDelivery", 2, "product1")
    s.publish_order_event(valid)
    r.check("Q5 a valid order confirmed straight after still creates its job", s.wait_job(valid["orderId"]) is not None)


def q6_downtime(s, r, db):
    steps = ["Stop the Delivery app (local: Ctrl+C; staging: stop the sellora-delivery staging slot)",
             "Confirm two scheduled-delivery orders (approve them as the operator)",
             "Start Delivery again and wait 1 minute",
             "Check each order has exactly one delivery job (GET /api/deliveries?orderId=...&includeHandovers=true)"]
    if not s.has("op", "rep", "admin") or not pause("Q6: STOP the Delivery service now.", getattr(s, "interactive", False)):
        r.manual("Q6 consumer downtime", steps)
        return
    if s.source == "order":
        orders = [s.scheduled_job(quantity=1, approve=False)["order"] for _ in range(2)]
        for order in orders:
            s.order.put(f"/api/orders/{ci_get(order, 'orderId')}/approval", {"decision": "Approve"}, s.token("op"))
        order_ids = [ci_get(o, "orderId") for o in orders]
    else:
        events = [s.order_event("OrderConfirmed", "ScheduledDelivery", 1, "product1") for _ in range(2)]
        for event in events:
            s.publish_order_event(event)
        order_ids = [e["orderId"] for e in events]
    pause("Q6: START the Delivery service again.", True)
    for order_id in order_ids:
        job = s.wait_job(order_id, timeout=120)
        r.check(f"Q6 order {order_id} has its job after restart", job is not None)
        r.check(f"Q6 order {order_id} has exactly one job", s.jobs_for_order(order_id) == 1)


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-creation")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    scenario = build_scenario(args, env, report, base)
    scenario.interactive = args.interactive
    db = delivery_db(env) if args.target == "local" else None

    report.story(STORY, "US-E6-1 Auto-create delivery job on order confirmation")
    run_case(report, "US-E6-1-Q1", "Scheduled delivery creates one Pending job", q1_scheduled_creates_pending,
             scenario, report, db)
    run_case(report, "US-E6-1-Q2", "Unapproved and rejected orders create nothing", q2_unapproved_and_rejected,
             scenario, report, db)
    run_case(report, "US-E6-1-Q3", "Cash sale creates a Delivered record", q3_cash_sale, scenario, report, db)
    run_case(report, "US-E6-1-Q4", "Duplicate event", q4_duplicate, scenario, report, db)
    run_case(report, "US-E6-1-Q5", "Malformed message", q5_malformed, scenario, report, db)
    run_case(report, "US-E6-1-Q6", "Consumer downtime", q6_downtime, scenario, report, db)
    report.note("orders created in this run: " + ", ".join(scenario.mailbox_checklist()))
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
