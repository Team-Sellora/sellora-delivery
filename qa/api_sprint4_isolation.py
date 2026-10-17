"""
Sprint 4 gateway, role, scope and tenant checks through API Manager (Python, no bash or jq needed).

Covers: US-E6-2-D1 / DoD 6 (invalid JWT rejected at the gateway), US-E6-2-Q4, US-E6-3-Q4, US-E6-4-Q3, US-E6-4-Q5,
        US-E7-1-Q3a, US-E7-1-Q6, US-E7-2-Q7. Read-only except for requests that must be refused.

Writes evidence/<CSP>/api/api-isolation-staging.txt for CSP-262, CSP-263, CSP-264, CSP-250 and CSP-252.

Run from the QA folder:  python api_sprint4_isolation.py
Needs in .env: DELIVERY_GW, TOKEN_ADMIN_A, TOKEN_OPERATOR_A, TOKEN_REP_A, TOKEN_SHOP_A.
Optional: AUDIT_GW, TOKEN_ADMIN_B (Company B checks), TOKEN_AREA_MANAGER_A, SHOP_OTHER_ID, REP_OTHER_ID,
          REP_OTHER_AGENCY_ID, COMPANY_A_ID. Checks whose value is missing show BLOCK with the reason.
"""
import argparse

from sellora_qa import tampered, Api, Report, check_tokens, ci_get, items_of, load_env

FAKE_REP = "00000000-0000-0000-0000-000000000001"


def refs(response):
    return {ci_get(i, "deliveryReference") for i in items_of(response.body)} if response.status == 200 else set()


def expect(r, label, response, *codes):
    r.expect_status(label, response, *codes)


def gateway(r, d, a, env):
    r.story("CSP-262", "Gateway rejects bad tokens (US-E6-2-D1, DoD 6) and role checks on assignment")
    admin = env["TOKEN_ADMIN_A"]
    expect(r, "GW-01 delivery, no token", d.get("/api/deliveries", None, soft=True), 401)
    expect(r, "GW-02 delivery, tampered signature", d.get("/api/deliveries", tampered(admin), soft=True), 401)
    expect(r, "GW-03 delivery, garbage token", d.get("/api/deliveries", "not.a.jwt", soft=True), 401)
    valid = d.get("/api/deliveries?pageSize=1", admin, soft=True)
    if valid.status == 401 and "900901" in valid.text:
        raise SystemExit("The gateway rejected the Company Admin token (900901): it is revoked or expired. "
                         "Copy fresh tokens (do not log out of the web app afterwards) and run again. Nothing was written.")
    expect(r, "GW-00 delivery, valid Company Admin access token is accepted", valid, 200)
    return valid


def roles_and_tenants(r, d, env):
    admin, admin_b = env["TOKEN_ADMIN_A"], env.get("TOKEN_ADMIN_B")
    a_list = d.get("/api/deliveries?pageSize=100&includeHandovers=true", admin, soft=True)
    a_refs = refs(a_list)
    a_id = next((ci_get(i, "id") or ci_get(i, "deliveryJobId") for i in items_of(a_list.body)), None) \
        if a_list.status == 200 else None

    if a_id:
        expect(r, "ROLE-01 sales rep calls assignment",
               d.put(f"/api/deliveries/{a_id}/assignment", {"salesRepId": FAKE_REP, "scheduledDate": "2030-01-01"},
                     env["TOKEN_REP_A"], soft=True), 403)
    else:
        r.blocked("ROLE-01", f"Company A delivery list answered {a_list.status}, no job to call")

    r.story("CSP-263", "Role checks on the status endpoint (US-E6-3-Q4)")
    if a_id:
        expect(r, "ROLE-02 shop owner calls status",
               d.patch(f"/api/deliveries/{a_id}/status", {"status": "InTransit", "version": 0},
                       env["TOKEN_SHOP_A"], soft=True), 403)
        expect(r, "ROLE-03 agency operator calls status",
               d.patch(f"/api/deliveries/{a_id}/status", {"status": "InTransit", "version": 0},
                       env["TOKEN_OPERATOR_A"], soft=True), 403)
    else:
        r.blocked("ROLE-02/03", f"Company A delivery list answered {a_list.status}, no job to call")

    r.story("CSP-264", "Scope widening and tenant isolation (US-E6-4-Q3, Q5)")
    r.note(f"Company A list as admin: HTTP {a_list.status}, {len(a_refs)} deliveries on page 1")
    for key, token_key, param, label in (
            ("SHOP_OTHER_ID", "TOKEN_SHOP_A", "shopId", "SCOPE-01 shop owner passes another shopId"),
            ("REP_OTHER_AGENCY_ID", "TOKEN_OPERATOR_A", "salesRepId", "SCOPE-02 operator passes another agency's salesRepId"),
            ("REP_OTHER_ID", "TOKEN_REP_A", "salesRepId", "SCOPE-03 rep passes another rep's salesRepId")):
        if env.get(key):
            expect(r, label, d.get(f"/api/deliveries?{param}={env[key]}", env[token_key], soft=True), 403)
        else:
            r.blocked(label.split()[0], f"{key} not set in .env (needs a second shop / rep / agency)")

    if not admin_b:
        r.blocked("TEN-01..04", "TOKEN_ADMIN_B not set (needs a Company B admin user)")
        return
    b_list = d.get("/api/deliveries?pageSize=100&includeHandovers=true", admin_b, soft=True)
    shared = a_refs & refs(b_list)
    r.check("TEN-01 Company B list has none of Company A's deliveries", b_list.status == 200 and not shared,
            f"HTTP {b_list.status}, {len(shared)} shared references")
    if a_id:
        expect(r, "TEN-02 Company B opens a Company A delivery", d.get(f"/api/deliveries/{a_id}", admin_b, soft=True), 404)
        expect(r, "TEN-03 Company B assigns a Company A delivery",
               d.put(f"/api/deliveries/{a_id}/assignment", {"salesRepId": FAKE_REP, "scheduledDate": "2030-01-01"},
                     admin_b, soft=True), 403, 404)
    if env.get("COMPANY_A_ID"):
        injected = d.get(f"/api/deliveries?pageSize=100&companyId={env['COMPANY_A_ID']}", admin_b, soft=True)
        leak = a_refs & refs(injected)
        r.check("TEN-04 ?companyId=<A> as Company B is ignored", not leak, f"{len(leak)} Company A references returned")


def audit_and_reports(r, a, env):
    r.story("CSP-250", "Audit API through the gateway (US-E7-1-Q3a, Q6, D5)")
    if not a.configured:
        r.blocked("GW-04/05, AUD-01..05", "AUDIT_GW not set - the Audit API is not published in APIM yet")
        r.story("CSP-252", "Reports role guard (US-E7-2-Q7)")
        r.blocked("REP-01", "AUDIT_GW not set - the reports API is not published in APIM yet")
        return
    admin, admin_b = env["TOKEN_ADMIN_A"], env.get("TOKEN_ADMIN_B")
    expect(r, "GW-04 audit, no token", a.get("/api/audit", None, soft=True), 401)
    expect(r, "GW-05 audit, tampered signature", a.get("/api/audit", tampered(admin), soft=True), 401)
    first = a.get("/api/audit?pageSize=1", admin, soft=True)
    if first.status != 200:
        reason = (f"the Company Admin's own GET /api/audit answered {first.status} - BUG-04 (Audit rejects valid tokens)"
                  if first.status == 401 else f"the Company Admin's own GET /api/audit answered {first.status}")
        r.blocked("AUD-01..05", reason + "; a 401 for the other roles would prove nothing")
        r.story("CSP-252", "Reports role guard (US-E7-2-Q7)")
        r.blocked("REP-01", reason)
        return
    entry = next((ci_get(i, "auditEntryId") or ci_get(i, "id") for i in items_of(first.body)), None) \
        if first.status == 200 else None
    if entry:
        for method in ("PUT", "PATCH", "DELETE"):
            body = None if method == "DELETE" else {"payload": "tampered"}
            expect(r, f"AUD-01 {method} /api/audit/{{id}}", a.call(method, f"/api/audit/{entry}", body, admin, soft=True), 404, 405)
        if admin_b:
            expect(r, "AUD-02 Company B opens a Company A entry", a.get(f"/api/audit/{entry}", admin_b, soft=True), 404)
    else:
        r.blocked("AUD-01/02", f"no audit entry to use (GET /api/audit answered {first.status})")
    for persona in ("TOKEN_OPERATOR_A", "TOKEN_REP_A", "TOKEN_SHOP_A"):
        if env.get(persona):
            expect(r, f"AUD-04 {persona} searches audit", a.get("/api/audit?pageSize=1", env[persona], soft=True), 403)
    if env.get("TOKEN_AREA_MANAGER_A"):
        expect(r, "AUD-05 Area Manager calls verify", a.get("/api/audit/verify", env["TOKEN_AREA_MANAGER_A"], soft=True), 403)

    r.story("CSP-252", "Reports role guard (US-E7-2-Q7)")
    for persona in ("TOKEN_OPERATOR_A", "TOKEN_REP_A", "TOKEN_SHOP_A"):
        if env.get(persona):
            expect(r, f"REP-01 {persona} sales-summary",
                   a.get("/api/reports/sales-summary?from=2026-10-01&to=2026-10-31", env[persona], soft=True), 403)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--env", default=".env")
    parser.add_argument("--evidence", default="evidence")
    args = parser.parse_args()
    env = load_env(args.env)
    missing = [k for k in ("DELIVERY_GW", "TOKEN_ADMIN_A", "TOKEN_OPERATOR_A", "TOKEN_REP_A", "TOKEN_SHOP_A") if not env.get(k)]
    if missing:
        raise SystemExit("Missing in .env: " + ", ".join(missing))
    check_tokens(env)
    delivery, audit = Api(env["DELIVERY_GW"], "delivery"), Api(env.get("AUDIT_GW", ""), "audit")
    report = Report("api-isolation")
    print(f"Delivery gateway: {delivery.base}\nAudit gateway:    {audit.base or 'not set'}")
    gateway(report, delivery, audit, env)
    roles_and_tenants(report, delivery, env)
    audit_and_reports(report, audit, env)
    report.finish(args.evidence, "staging", delivery.base)


if __name__ == "__main__":
    main()
