"""
US-E6-4 QA - delivery queue and shop-facing status view (CSP-264).

Writes evidence/CSP-264/api/delivery-queue-<target>.txt:
  US-E6-4-Q1  Agency queue: status=Pending shows only the operator's agency, sorted by scheduled date
  US-E6-4-Q2  Shop owner sees only their shop's deliveries; the cash-sale handover is hidden by default
  US-E6-4-Q3  Scope widening: shopId / salesRepId outside the caller's scope -> 403; other agency's detail -> 404
  US-E6-4-Q4  Paging and filters (past the end, pageSize 500, combined status + date, repeated status)
  US-E6-4-Q5  Tenant isolation: Company B's admin sees none of Company A's deliveries through any filter
  US-E6-4-AM  Area Manager sees only their provinces
  US-E6-4-FE  Response fields the web client reads (sellora-web feature/CSP-264 types.ts) are present
  (US-E6-4-Q6 UI states is in selenium_sprint4_ui.py --scenario deliveries)

Run:  python delivery_queue_tests.py --target staging
"""
import argparse
from datetime import timedelta

from sellora_qa import (Report, build_scenario, ci_get, colombo_today, delivery_base, items_of, load_env,
                        run_case, standard_args)

STORY = "CSP-264"


def listing(s, persona, query=""):
    return s.delivery.get(f"/api/deliveries{query}", s.token(persona))


def q1_agency_queue(s, r, ctx):
    # Two pending jobs created a moment apart, plus two assigned jobs with different dates
    ctx["pending"] = [s.scheduled_job(quantity=1), s.scheduled_job(quantity=1)]
    early, late = s.scheduled_job(quantity=1), s.scheduled_job(quantity=1)
    if not (early and late):
        from sellora_qa import SetupError
        raise SetupError("a delivery job did not appear within 60 s")
    s.assign(ci_get(late, "id"), scheduled=colombo_today() + timedelta(days=6))
    s.assign(ci_get(early, "id"), scheduled=colombo_today() + timedelta(days=2))
    ctx["assigned"] = [early, late]
    if not all(ctx["pending"] + ctx["assigned"]):
        from sellora_qa import SetupError
        raise SetupError("a delivery job did not appear within 60 s")

    response = listing(s, "op", "?status=Pending&pageSize=100")
    r.expect_status("Q1 operator lists status=Pending", response, 200)
    items = items_of(response.body)
    r.check("Q1 every row is Pending", all(ci_get(i, "status") == "Pending" for i in items),
            str({ci_get(i, "status") for i in items}))
    foreign = [ci_get(i, "deliveryReference") for i in items if not same_agency(s, i)]
    r.check("Q1 every row belongs to the operator's agency", not foreign, f"foreign rows {foreign[:5]}")
    refs = [ci_get(i, "deliveryReference") for i in items]
    mine = [ci_get(j, "deliveryReference") for j in ctx["pending"]]
    r.check("Q1 this run's pending jobs are listed", all(m in refs for m in mine), f"{mine} not in list")
    if all(m in refs for m in mine):
        r.check("Q1 unscheduled rows ordered by creation time", refs.index(mine[0]) < refs.index(mine[1]))

    response = listing(s, "op", "?status=Assigned&pageSize=100")
    dates = [ci_get(i, "scheduledDate") for i in items_of(response.body)]
    r.check("Q1 Assigned rows sorted by scheduled date ascending", dates == sorted(d for d in dates if d), str(dates[:10]))


def same_agency(s, item):
    if ci_get(item, "agencyId"):
        return ci_get(item, "agencyId") == s.ids["agencyId"]
    detail = s.detail(ci_get(item, "id"), persona="admin") or {}
    return ci_get(detail, "agencyId") == s.ids["agencyId"]


def q2_shop_view(s, r, ctx):
    cash, order, _ = s.cash_sale_job()
    ctx["cash"] = cash
    response = listing(s, "shop", "?pageSize=100")
    r.expect_status("Q2 shop owner lists deliveries", response, 200)
    items = items_of(response.body)
    wrong = []
    for item in items[:25]:
        detail = s.detail(ci_get(item, "id"), persona="admin") or {}
        if ci_get(detail, "shopId") != s.ids["shopId"]:
            wrong.append(ci_get(item, "deliveryReference"))
    r.check("Q2 only the owner's own shop (first 25 rows checked)", not wrong, str(wrong))
    r.check("Q2 status and scheduled date present on each row",
            all("status" in {k.lower() for k in i} and "scheduleddate" in {k.lower() for k in i} for i in items))
    refs = [ci_get(i, "deliveryReference") for i in items]
    if cash:
        r.check("Q2 cash-sale handover hidden by default", ci_get(cash, "deliveryReference") not in refs)
        with_handovers = items_of(listing(s, "shop", "?pageSize=100&includeHandovers=true").body)
        r.check("Q2 cash-sale handover shown with includeHandovers=true",
                ci_get(cash, "deliveryReference") in [ci_get(i, "deliveryReference") for i in with_handovers])
    else:
        r.blocked("Q2 handover hidden", "cash-sale job not created")


def q3_scope_widening(s, r, ctx):
    if s.ids.get("shopOtherId"):
        r.expect_status("Q3 shop owner passes another shop's shopId", listing(s, "shop", f"?shopId={s.ids['shopOtherId']}"), 403)
        response = listing(s, "shop", f"?shopId={s.ids['shopOtherId']}")
        r.check("Q3 ... and gets no data", not items_of(response.body), response.text[:200])
    else:
        r.blocked("Q3 shop widening", "SHOP_OTHER_ID / TOKEN_SHOP_OTHER not set")
    other_rep = s.env.get("REP_OTHER_AGENCY_ID") or s.ids.get("repOtherId")
    if other_rep:
        r.expect_status("Q3 operator passes another agency's salesRepId", listing(s, "op", f"?salesRepId={other_rep}"), 403)
        r.expect_status("Q3 rep passes another rep's salesRepId", listing(s, "rep", f"?salesRepId={other_rep}"), 403)
    else:
        r.blocked("Q3 salesRepId widening", "REP_OTHER_AGENCY_ID / REP_OTHER_ID not set")
    foreign = s.env.get("OTHER_AGENCY_DELIVERY_ID")
    if not foreign and s.source == "kafka":
        import uuid
        event = s.order_event("OrderConfirmed", "ScheduledDelivery", 1, "product1", agency_id=str(uuid.uuid4()))
        s.publish_order_event(event)
        job = s.wait_job(event["orderId"])
        foreign = ci_get(job, "id") if job else None
    if foreign:
        r.expect_status("Q3 operator opens another agency's delivery", s.delivery.get(f"/api/deliveries/{foreign}", s.token("op")), 404)
        r.expect_status("Q3 shop owner opens another shop's delivery", s.delivery.get(f"/api/deliveries/{foreign}", s.token("shop")), 404)
    else:
        r.blocked("Q3 other agency's detail", "set OTHER_AGENCY_DELIVERY_ID (a delivery of another agency)")
    if ctx.get("pending") and s.has("rep_other"):
        r.expect_status("Q3 rep opens a delivery not assigned to them",
                        s.delivery.get(f"/api/deliveries/{ci_get(ctx['pending'][0], 'id')}", s.token("rep_other")), 404)


def q4_paging_filters(s, r, ctx):
    first = listing(s, "admin", "?pageSize=5&includeHandovers=true")
    total = ci_get(first.body, "totalCount", 0)
    r.check("Q4 response shape {items, page, pageSize, totalCount}",
            all(ci_get(first.body, k) is not None for k in ("items", "page", "pageSize", "totalCount")), first.text[:200])
    beyond = listing(s, "admin", "?page=9999&pageSize=5&includeHandovers=true")
    r.check("Q4 page past the end -> empty items, same totalCount",
            beyond.status == 200 and not items_of(beyond.body) and ci_get(beyond.body, "totalCount") == total,
            beyond.text[:200])
    big = listing(s, "admin", "?pageSize=500")
    if big.status == 400:
        r.check("Q4 pageSize=500 -> 400", True)
    else:
        r.check("Q4 pageSize=500 -> clamped to 100", big.status == 200 and ci_get(big.body, "pageSize") == 100
                and len(items_of(big.body)) <= 100, big.text[:200])
        r.note("pageSize=500 is clamped to 100 (recorded for the story, backlog allows either)")
    zero = listing(s, "admin", "?pageSize=0&page=0")
    r.check("Q4 pageSize=0 / page=0 handled (no 500)", zero.status in (200, 400), str(zero.status))

    if ctx.get("assigned"):
        day = str(colombo_today() + timedelta(days=2))
        combined = listing(s, "op", f"?status=Assigned&scheduledDateFrom={day}&scheduledDateTo={day}&pageSize=100")
        items = items_of(combined.body)
        r.check("Q4 status + date range: only Assigned rows on that date",
                items and all(ci_get(i, "status") == "Assigned" and ci_get(i, "scheduledDate") == day for i in items),
                combined.text[:300])
        r.check("Q4 the early job is in the filtered set",
                ci_get(ctx["assigned"][0], "deliveryReference") in [ci_get(i, "deliveryReference") for i in items])
        r.check("Q4 the late job is not", ci_get(ctx["assigned"][1], "deliveryReference") not in
                [ci_get(i, "deliveryReference") for i in items])
    multi = listing(s, "op", "?status=Pending&status=Assigned&pageSize=100")
    r.check("Q4 repeated status parameter returns both statuses only",
            {ci_get(i, "status") for i in items_of(multi.body)} <= {"Pending", "Assigned"}, multi.text[:200])
    bogus = listing(s, "op", "?status=Bogus&pageSize=1")
    r.note(f"status=Bogus -> {bogus.status}, totalCount {ci_get(bogus.body, 'totalCount')} "
           "(unknown status values are ignored, so this returns everything)")
    if ctx.get("pending"):
        by_order = listing(s, "admin", f"?orderId={ci_get(ctx['pending'][0], 'orderId')}")
        r.check("Q4 orderId filter (used by the order detail panel) returns that one delivery",
                ci_get(by_order.body, "totalCount") == 1, by_order.text[:200])


def q5_tenant(s, r, ctx):
    if not s.has("admin_b"):
        r.blocked("Q5 tenant isolation", "TOKEN_ADMIN_B not set")
        return
    mine = {ci_get(j, "deliveryReference") for j in ctx.get("pending", []) + ctx.get("assigned", []) if j}
    for query in ("?pageSize=100&includeHandovers=true", f"?shopId={s.ids['shopId']}&includeHandovers=true",
                  "?status=Pending&status=Assigned&status=Delivered&pageSize=100",
                  f"?companyId={s.company}&pageSize=100"):
        response = listing(s, "admin_b", query)
        refs = {ci_get(i, "deliveryReference") for i in items_of(response.body)}
        r.check(f"Q5 Company B admin {query[:45]} -> none of Company A's jobs", response.status == 200 and not (refs & mine),
                f"{response.status} leaked {sorted(refs & mine)[:3]}")
    if ctx.get("pending"):
        job_id = ci_get(ctx["pending"][0], "id")
        r.expect_status("Q5 Company B admin opens a Company A delivery", s.delivery.get(f"/api/deliveries/{job_id}",
                                                                                        s.token("admin_b")), 404)
        r.expect_status("Q5 Company B admin cannot assign it", s.assign(job_id, persona="admin_b"), 403, 404)


def am_scope(s, r, ctx):
    if not s.has("am"):
        r.blocked("AM scope", "TOKEN_AREA_MANAGER_A not set")
        return
    provinces = set(ci_get(s.scope("am"), "provinceIds") or [])
    response = listing(s, "am", "?pageSize=50&includeHandovers=true")
    r.expect_status("AM lists deliveries", response, 200)
    outside = []
    for item in items_of(response.body)[:20]:
        detail = s.detail(ci_get(item, "id"), persona="admin") or {}
        if ci_get(detail, "provinceId") not in provinces:
            outside.append(ci_get(item, "deliveryReference"))
    r.check("AM every row is in the Area Manager's provinces (first 20 checked)", not outside, str(outside))


def fe_contract(s, r, ctx):
    if not ctx.get("pending"):
        r.blocked("FE contract", "no job created")
        return
    item = items_of(listing(s, "op", "?status=Pending&pageSize=1").body)
    item = item[0] if item else {}
    keys = {k.lower() for k in item}
    for name in ("id", "deliveryReference", "orderReference", "shopName", "status", "scheduledDate", "assignedRepName"):
        r.check(f"FE list item has '{name}'", name.lower() in keys, f"keys {sorted(keys)}")
    r.check("FE list item has 'orderId' (Order Ref link in the queue)", "orderid" in keys,
            "missing - the Order Ref link goes to /orders/undefined")
    r.check("FE list item has 'territory' (Territory column)", "territory" in keys,
            "BE sends territoryId (a GUID) - the column is blank")
    detail = s.detail(ci_get(ctx["pending"][0], "id")) or {}
    lines = ci_get(detail, "lines") or [{}]
    r.check("FE detail line has 'id' (React key in DeliveryDetailPage)", "id" in {k.lower() for k in lines[0]},
            f"line keys {sorted(lines[0])} - BE sends lineId")
    r.check("FE detail has 'version' (needed for PATCH status)", ci_get(detail, "version") is not None)


def main():
    parser = standard_args(argparse.ArgumentParser(description=__doc__,
                                                   formatter_class=argparse.RawDescriptionHelpFormatter))
    args = parser.parse_args()
    env = load_env(args.env)
    report = Report("delivery-queue")
    base = delivery_base(env, args.target)
    print(f"Target: {args.target} ({base})")
    s = build_scenario(args, env, report, base)
    ctx = {}

    report.story(STORY, "US-E6-4 Delivery queue and shop-facing status view")
    run_case(report, "US-E6-4-Q1", "Agency queue", q1_agency_queue, s, report, ctx)
    run_case(report, "US-E6-4-Q2", "Shop owner view", q2_shop_view, s, report, ctx)
    run_case(report, "US-E6-4-Q3", "Scope widening", q3_scope_widening, s, report, ctx)
    run_case(report, "US-E6-4-Q4", "Paging and filters", q4_paging_filters, s, report, ctx)
    run_case(report, "US-E6-4-Q5", "Tenant isolation", q5_tenant, s, report, ctx)
    run_case(report, "US-E6-4-AM", "Area Manager scope", am_scope, s, report, ctx)
    run_case(report, "US-E6-4-FE", "Fields the web client reads", fe_contract, s, report, ctx)
    report.finish(args.evidence, args.target, base)


if __name__ == "__main__":
    main()
