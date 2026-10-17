"""
US-E6-6 QA - shop return request and stock restoration (CSP-266).

Writes evidence/CSP-266/api/shop-returns-<target>.txt:
  US-E6-6-Q1  On a delivery of 20 units, request 5 -> Requested, visible in the agency's pending list
  US-E6-6-Q2  Over-return: 18 pending + 5 -> 400 "only 2 remain"; 18 approved + 5 -> same; returnableQuantity = 2
  US-E6-6-Q3  Wrong state or product: InTransit job 409; product not on the order 400; qty 0 / duplicate line 400
  US-E6-6-Q4  Approval restores stock: ReturnAccepted with Inventory's exact fields; agency on-hand rises by exactly 5
  US-E6-6-Q5  Rejection: no reason 400; with reason -> Rejected, no ReturnAccepted, stock unchanged
  US-E6-6-Q6  Who can decide: rep, shop owner, other agency's operator 403; deciding twice 409
  US-E6-6-Q7  Replay: the same ReturnAccepted delivered again does not add stock a second time
  US-E6-6-Q8  Inventory down: approve -> 503, return still Requested, no event

Endpoints from the backlog (US-E6-6-BE2..BE4):
  POST /api/returns                  { deliveryJobId, lines[{ productId, quantity, reason }] }
  PUT  /api/returns/{id}/decision    { decision: Approved | Rejected, reason? }
  GET  /api/returns, GET /api/returns/{id}, and lines[].returnableQuantity on GET /api/deliveries/{id}
Stock is read from Inventory: GET /api/stock?productId=&inventoryOwnerId= (INVENTORY_GW), agency owner from
GET /api/inventory-owners (OwnerType Agency, ExternalOwnerId = agency).

Run:  python shop_returns_tests.py --target staging [--allow-produce] [--interactive]
"""
import argparse
import json
import uuid

from sellora_qa import (DELIVERY_TOPIC, INVENTORY_RETURN_FIELDS, RETURN_REF_PATTERN, Report, SetupError,
                        build_scenario, ci_get, delivery_base, delivery_events, items_of, load_env, missing_fields,
                        now_utc, pause, poll, run_case, standard_args, wait_seconds)

STORY = "CSP-266"


class Returns:
    def __init__(self, s):
        self.s = s

    def request(self, job, quantity, product=None, persona="shop", extra_lines=None):
        line = {"productId": product or ci_get(ci_get(job, "lines")[0], "productId"), "quantity": quantity,
                "reason": "QA - damaged cases"}
        body = {"deliveryJobId": ci_get(job, "id"), "lines": [line] + (extra_lines or [])}
        return self.s.delivery.post("/api/returns", body, self.s.token(persona))

    def decide(self, return_id, decision, reason=None, persona="op"):
        body = {"decision": decision}
        if reason is not None:
            body["reason"] = reason
        return self.s.delivery.put(f"/api/returns/{return_id}/decision", body, self.s.token(persona), soft=True)

    def get(self, return_id, persona="op"):
        response = self.s.delivery.get(f"/api/returns/{return_id}", self.s.token(persona))
        return response.body if response.status == 200 else {}


def return_id_of(response):
    return ci_get(response.body, "returnRequestId") or ci_get(response.body, "id")


def agency_owner(s):
    if s.env.get("AGENCY_A_INVENTORY_OWNER_ID"):
        return s.env["AGENCY_A_INVENTORY_OWNER_ID"]
    if not s.inventory.configured:
        return None
    owners = s.inventory.get("/api/inventory-owners", s.token("op"), soft=True)
    for owner in items_of(owners.body):
        if ci_get(owner, "ownerType") == "Agency" and ci_get(owner, "externalOwnerId") == s.ids["agencyId"]:
            return ci_get(owner, "inventoryOwnerId")
    return None


def on_hand(s, owner, product):
    response = s.inventory.get(f"/api/stock?productId={product}&inventoryOwnerId={owner}", s.token("admin"), soft=True)
    rows = response.body if isinstance(response.body, list) else items_of(response.body)
    return sum(ci_get(x, "quantityOnHand", 0) for x in rows) if response.status == 200 else None


def deployed(s, ctx):
    if "deployed" not in ctx:
        probe = s.delivery.get("/api/returns?pageSize=1", s.token("op"))
        ctx["deployed"] = probe.status not in (404, 405)
    if not ctx["deployed"]:
        raise SetupError("/api/returns not deployed on this target (404)")


def twenty_unit_delivery(s, ctx):
    if "job20" not in ctx:
        ctx["job20"] = s.delivered_job(quantity=20)
    return ctx["job20"]


def need_ctx(ctx, key):
    if not ctx.get(key):
        raise SetupError(f"depends on an earlier case that did not run ({key})")
    return ctx[key]


def q1_valid(s, r, ctx, rt):
    deployed(s, ctx)
    job = twenty_unit_delivery(s, ctx)
    response = rt.request(job, 5)
    r.expect_status("Q1 shop owner requests 5 of 20", response, 201, 200)
    ctx["r5"] = return_id_of(response)
    created = rt.get(ctx["r5"])
    r.check("Q1 status Requested", ci_get(created, "status") == "Requested", str(created)[:200])
    r.check("Q1 return reference RT-yyMMdd-XXXXXX", bool(RETURN_REF_PATTERN.match(ci_get(created, "returnReference") or "")),
            ci_get(created, "returnReference"))
    pending = items_of(s.delivery.get("/api/returns?status=Requested&pageSize=100", s.token("op")).body)
    r.check("Q1 visible in the agency's pending list", ctx["r5"] in [ci_get(i, "returnRequestId") or ci_get(i, "id")
                                                                     for i in pending])
    by_rep = rt.request(job, 1, persona="rep")
    r.check("Q1 the order's / assigned rep may also raise a return", by_rep.status in (200, 201), by_rep.text[:200])
    ctx["r1_rep"] = return_id_of(by_rep)


def q2_over_return(s, r, ctx, rt):
    deployed(s, ctx)
    job = twenty_unit_delivery(s, ctx)
    already = 5 + (1 if ctx.get("r1_rep") else 0)
    response = rt.request(job, 18 - already)
    r.expect_status(f"Q2 request {18 - already} more (18 of 20 now approved or pending)", response, 201, 200)
    ctx["r13"] = return_id_of(response)
    over = rt.request(job, 5)
    r.expect_status("Q2 18 approved-or-pending + request 5", over, 400)
    r.check("Q2 message says only 2 remain", "only 2" in over.detail().lower(), over.detail())
    lines = ci_get(s.detail(ci_get(job, "id")), "lines") or [{}]
    r.check("Q2 detail shows returnableQuantity 2", ci_get(lines[0], "returnableQuantity") == 2, str(lines[0]))

    owner = agency_owner(s)
    if not owner:
        r.blocked("Q2 approved case", "agency inventory owner unknown (INVENTORY_GW / AGENCY_A_INVENTORY_OWNER_ID)")
        return
    for return_id in [x for x in (ctx.get("r1_rep"), ctx["r13"]) if x]:
        decided = rt.decide(return_id, "Approved")
        r.check(f"Q2 setup: approve {return_id}", decided.status in (200, 204), decided.text[:200])
    ctx["approved_extra"] = True
    over = rt.request(job, 5)
    r.expect_status("Q2 all 18 approved + request 5", over, 400)
    r.check("Q2 message still says only 2 remain", "only 2" in over.detail().lower(), over.detail())


def q3_wrong_state_or_product(s, r, ctx, rt):
    deployed(s, ctx)
    moving = s.assigned_job(quantity=3)
    s.set_status(ci_get(moving, "id"), "InTransit")
    r.expect_status("Q3 return against an InTransit job", rt.request(moving, 1), 409)
    job = twenty_unit_delivery(s, ctx)
    r.expect_status("Q3 product not on the order", rt.request(job, 1, product=str(uuid.uuid4())), 400)
    r.expect_status("Q3 quantity 0", rt.request(job, 0), 400)
    r.expect_status("Q3 negative quantity", rt.request(job, -1), 400)
    product = ci_get(ci_get(job, "lines")[0], "productId")
    duplicate = rt.request(job, 1, extra_lines=[{"productId": product, "quantity": 1, "reason": "dup"}])
    r.expect_status("Q3 the same product twice in one request", duplicate, 400)
    if s.has("shop_other"):
        r.expect_status("Q3 another shop's owner raises a return", rt.request(job, 1, persona="shop_other"), 403, 404)
    r.expect_status("Q3 agency operator raises a return (not allowed)", rt.request(job, 1, persona="op"), 403)


def q4_approval_restores_stock(s, r, ctx, rt):
    deployed(s, ctx)
    owner = agency_owner(s)
    if not owner:
        raise SetupError("agency inventory owner unknown (INVENTORY_GW / AGENCY_A_INVENTORY_OWNER_ID)")
    job = twenty_unit_delivery(s, ctx)
    product = ci_get(ci_get(job, "lines")[0], "productId")
    before = on_hand(s, owner, product)
    r.note(f"agency on-hand for the product before approval: {before}")
    since = now_utc()
    decided = rt.decide(need_ctx(ctx, "r5"), "Approved")
    r.expect_status("Q4 operator approves the 5-unit return", decided, 200, 204)
    stored = rt.get(ctx["r5"])
    r.check("Q4 return Approved with decider and InventoryOwnerId",
            ci_get(stored, "status") == "Approved" and ci_get(stored, "inventoryOwnerId") == owner, str(stored)[:300])
    if s.tap.available:
        events = s.tap.wait_for(DELIVERY_TOPIC, since, lambda m, p: ci_get(p, "eventType") == "ReturnAccepted"
                                and ci_get(p, "entityId") == ctx["r5"], timeout=60)
        r.check("Q4 one ReturnAccepted on sellora.delivery.v1", len(events) == 1, f"{len(events)} found")
        if events:
            payload = events[0].json
            ctx["return_accepted"] = events[0]
            r.attach("kafka/US-E6-6-Q4-ReturnAccepted.json", events[0].as_evidence())
            r.check("Q4 every field of Inventory's ReturnAcceptedEvent present",
                    not missing_fields(payload, INVENTORY_RETURN_FIELDS), f"missing {missing_fields(payload, INVENTORY_RETURN_FIELDS)}")
            line = (ci_get(payload, "lines") or [{}])[0]
            r.check("Q4 line {productId, batchId: null, quantity 5}", ci_get(line, "productId") == product
                    and ci_get(line, "batchId") is None and ci_get(line, "quantity") == 5, json.dumps(line))
            r.check("Q4 inventoryOwnerId = agency's stock owner", ci_get(payload, "inventoryOwnerId") == owner)
            r.check("Q4 schemaVersion 1.0", ci_get(payload, "schemaVersion") == "1.0")
    else:
        r.blocked("Q4 ReturnAccepted event", s.tap.reason)
    after = poll(lambda: (lambda v: v if v is not None and before is not None and v != before else None)(
        on_hand(s, owner, product)), timeout=60) or on_hand(s, owner, product)
    r.check("Q4 agency on-hand rose by exactly 5", before is not None and after == before + 5, f"{before} -> {after}")
    ctx["stock_after_q4"] = after
    r.manual("Q4 processed_inventory_event row", [
        f"inventory_db: select * from processed_inventory_event where event_id = '<eventId of the ReturnAccepted above>'",
        "one row - screenshot into evidence/CSP-266/ss/"])


def q5_rejection(s, r, ctx, rt):
    deployed(s, ctx)
    job = s.delivered_job(quantity=4)
    response = rt.request(job, 2)
    return_id = return_id_of(response)
    if not return_id:
        raise SetupError(f"return not created: {response.status} {response.text[:200]}")
    owner = agency_owner(s)
    product = ci_get(ci_get(job, "lines")[0], "productId")
    before = on_hand(s, owner, product) if owner else None
    since = now_utc()
    r.expect_status("Q5 reject without a reason", rt.decide(return_id, "Rejected"), 400)
    r.expect_status("Q5 reject with a reason", rt.decide(return_id, "Rejected", reason="QA - goods not damaged"), 200, 204)
    stored = rt.get(return_id)
    r.check("Q5 status Rejected with the reason", ci_get(stored, "status") == "Rejected"
            and "not damaged" in str(ci_get(stored, "decisionReason")), str(stored)[:300])
    if s.tap.available:
        accepted = s.tap.wait_for(DELIVERY_TOPIC, since, lambda m, p: ci_get(p, "eventType") == "ReturnAccepted"
                                  and ci_get(p, "entityId") == return_id, timeout=20)
        r.check("Q5 no ReturnAccepted published", not accepted, f"{len(accepted)} found")
    wait_seconds(10, "make sure no stock movement follows")
    if owner:
        r.check("Q5 stock unchanged", on_hand(s, owner, product) == before, f"{before} -> {on_hand(s, owner, product)}")


def q6_who_decides(s, r, ctx, rt):
    deployed(s, ctx)
    job = s.delivered_job(quantity=3)
    return_id = return_id_of(rt.request(job, 1))
    if not return_id:
        raise SetupError("return not created")
    r.expect_status("Q6 sales rep decides", rt.decide(return_id, "Approved", persona="rep"), 403)
    r.expect_status("Q6 shop owner decides", rt.decide(return_id, "Approved", persona="shop"), 403)
    if s.has("op_other"):
        r.expect_status("Q6 other agency's operator decides", rt.decide(return_id, "Approved", persona="op_other"), 403, 404)
    r.check("Q6 still Requested", ci_get(rt.get(return_id), "status") == "Requested")
    rt.decide(return_id, "Rejected", reason="QA")
    r.expect_status("Q6 deciding an already-decided return", rt.decide(return_id, "Approved"), 409)
    r.expect_status("Q6 no PUT/DELETE on return lines (immutable)",
                    s.delivery.delete(f"/api/returns/{return_id}", s.token("op")), 404, 405)


def q7_replay(s, r, ctx, rt):
    message = ctx.get("return_accepted")
    owner = agency_owner(s)
    if not (message and owner):
        raise SetupError("needs Q4's ReturnAccepted message and the agency owner")
    if not s.tap.can_produce:
        r.manual("Q7 replay", ["Reset Inventory's consumer group on sellora.delivery.v1 back by a few messages",
                               "Stock for the product must not rise a second time"])
        return
    product = ci_get((ci_get(message.json, "lines") or [{}])[0], "productId")
    before = on_hand(s, owner, product)
    s.tap.produce(DELIVERY_TOPIC, message.key, message.value, {k: v for k, v in message.headers.items() if v})
    wait_seconds(20, "Inventory consumes the replayed ReturnAccepted")
    r.check("Q7 replayed ReturnAccepted (same eventId) does not add stock again", on_hand(s, owner, product) == before,
            f"{before} -> {on_hand(s, owner, product)}")


def q8_inventory_down(s, r, ctx, rt):
    deployed(s, ctx)
    job = s.delivered_job(quantity=2)
    return_id = return_id_of(rt.request(job, 1))
    if not pause("Q8: STOP the Inventory service (staging slot or local container) now.", s.interactive):
        r.manual("Q8 Inventory down", ["Stop sellora-inventory", f"Approve return {return_id} -> 503",
                                       "Return still Requested, no ReturnAccepted", "Start Inventory again"])
        return
    since = now_utc()
    r.expect_status("Q8 approve with Inventory down", rt.decide(return_id, "Approved"), 503)
    r.check("Q8 return still Requested", ci_get(rt.get(return_id), "status") == "Requested")
    if s.tap.available:
        events = s.tap.wait_for(DELIVERY_TOPIC, since, lambda m, p: ci_get(p, "entityId") == return_id, timeout=15)
        r.check("Q8 no event published", not events)
    pause("Q8: START Inventory again.", True)


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("shop-returns")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    s.interactive = args.interactive
    rt, ctx = Returns(s), {}

    report.story(STORY, "US-E6-6 Return request and stock restoration")
    for qa_id, title, fn in [("Q1", "Valid return", q1_valid), ("Q4", "Approval restores stock", q4_approval_restores_stock),
                             ("Q2", "Over-return", q2_over_return), ("Q3", "Wrong state or product", q3_wrong_state_or_product),
                             ("Q5", "Rejection", q5_rejection), ("Q6", "Who can decide", q6_who_decides),
                             ("Q7", "Replay", q7_replay), ("Q8", "Inventory down", q8_inventory_down)]:
        run_case(report, f"US-E6-6-{qa_id}", title, fn, s, report, ctx, rt)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
