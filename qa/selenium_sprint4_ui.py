"""
Sprint 4 UI checks through the real web app (Selenium + Chrome network capture).

One login per run; pick what to check with --scenario (repeatable). Evidence goes to
evidence/<CSP>/selenium/<timestamp>-<host>-<user>/ (screenshots, network-summary.json, results.txt).

Scenarios
  deliveries     CSP-264 Q6 + FE1/FE2: /deliveries renders for the role with the right columns, rows link to a
                 real order (no /orders/undefined), territory filled, every call goes to the Delivery gateway with a
                 bearer token; the detail page shows lines + history; Assign / status buttons only for the right role
  error-state    CSP-264 Q6: run while the Delivery slot is STOPPED - an error message, not a blank screen
  empty-state    CSP-264 Q6: run as a user of a company with no deliveries - the empty message
  status-actions CSP-263 FE1: as the assigned rep, 'Start delivery' on an Assigned job (--delivery-id) succeeds
                 (catches the missing 'version' field: PATCH must not answer 400)
  assign-dialog  CSP-262 FE2: as the operator, the Assign dialog opens on a Pending job, lists eligible reps
  route-guard    CSP-264/266/250/252: which of /deliveries /returns /audit /reports the role may open
  returns-menu   CSP-266 FE2: menu shows 'Shop returns' and 'Van returns' as two entries
  map            CSP-267 Q3: maps.googleapis.com blocked -> 'Map unavailable', rest of the page works (--delivery-id)
  audit          CSP-250 FE2/FE3: /audit?orderReference=... lists entries; 'Verify integrity' shows a result
  reports        CSP-252 FE1: /reports shows the summary cards and charts; a far-future range shows the empty state

Install:  pip install selenium
Run:      python selenium_sprint4_ui.py --base-url https://test.selloralk.tech --user qa.a.operator --password '***'
              --scenario deliveries --scenario assign-dialog --delivery-gateway-host <APIM host>
"""
import argparse
import base64
import json
import pathlib
import re
import sys
import time
from datetime import datetime
from urllib.parse import urlparse

from selenium import webdriver
from selenium.common.exceptions import TimeoutException
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait

SIGN_IN_BUTTON = (By.XPATH, "//button[normalize-space()='Sign in']")
USERNAME_LOCATORS = [(By.ID, "usernameUserInput"), (By.NAME, "usernameUserInput"), (By.ID, "username")]
PASSWORD_LOCATOR = (By.ID, "password")
SUBMIT_LOCATOR = (By.CSS_SELECTOR, "button[type='submit']")
CONSENT_LOCATOR = (By.XPATH, "//*[@id='approve' or @value='Allow' or normalize-space()='Allow']")

COLUMNS = {
    "agency": ["DELIVERY REF", "ORDER REF", "SHOP", "TERRITORY", "STATUS", "SCHEDULED DATE", "SALES REP", "ACTION"],
    "SalesRep": ["SHOP", "STATUS", "SCHEDULED DATE"],
    "ShopOwner": ["ORDER REF", "STATUS", "SCHEDULED DATE"],
}
ROUTE_ACCESS = {  # backlog 0.10 / FE subtasks
    "/deliveries": {"CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep", "ShopOwner"},
    "/returns": {"CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep", "ShopOwner"},
    "/audit": {"CompanyAdmin", "AreaManager"},
    "/reports": {"CompanyAdmin", "AreaManager"},
}
EMPTY_MESSAGES = ["No deliveries found.", "You have no upcoming deliveries.", "No deliveries assigned to you."]


class Results:
    def __init__(self):
        self.rows = []

    def check(self, name, passed, detail=""):
        self.rows.append(("PASS" if passed else "FAIL", name, detail))
        print(f"  {'PASS' if passed else 'FAIL'}  {name}" + (f"  -> {detail}" if detail and not passed else ""))
        return passed

    def note(self, text):
        self.rows.append(("INFO", text, ""))
        print(f"  INFO  {text}")

    @property
    def failed(self):
        return [r for r in self.rows if r[0] == "FAIL"]


def build_driver(headless):
    options = webdriver.ChromeOptions()
    if headless:
        options.add_argument("--headless=new")
    options.add_argument("--window-size=1440,900")
    options.add_argument("--ignore-certificate-errors")  # WSO2 self-signed certs; check certs by hand separately
    options.set_capability("acceptInsecureCerts", True)
    options.set_capability("goog:loggingPrefs", {"performance": "ALL"})
    return webdriver.Chrome(options=options)


def first_present(driver, timeout, locators):
    end = time.time() + timeout
    while time.time() < end:
        for locator in locators:
            found = [e for e in driver.find_elements(*locator) if e.is_displayed() and e.is_enabled()]
            if found:
                return found[0]
        time.sleep(0.3)
    raise TimeoutException(f"None of {locators} appeared")


def login(driver, base_url, user, password, timeout):
    wait = WebDriverWait(driver, timeout)
    driver.get(base_url)
    wait.until(EC.element_to_be_clickable(SIGN_IN_BUTTON)).click()
    time.sleep(1.5)  # WSO2 login page swaps its inputs in after load; typing too early hits a hidden field
    first_present(driver, timeout, USERNAME_LOCATORS).send_keys(user)
    first_present(driver, timeout, [PASSWORD_LOCATOR]).send_keys(password)
    driver.find_element(*SUBMIT_LOCATOR).click()
    try:
        WebDriverWait(driver, 5).until(EC.element_to_be_clickable(CONSENT_LOCATOR)).click()
    except TimeoutException:
        pass
    app_host = urlparse(base_url).netloc
    wait.until(lambda d: urlparse(d.current_url).netloc == app_host and not d.find_elements(*SIGN_IN_BUTTON)
               and "Loading…" not in d.find_element(By.TAG_NAME, "body").text)


def spa_navigate(driver, path):
    """Navigate inside the SPA without a reload (the token lives in memory only)."""
    driver.execute_script("window.history.pushState({}, '', arguments[0]);"
                          "window.dispatchEvent(new PopStateEvent('popstate'));", path)
    time.sleep(1.0)


def body_text(driver):
    return driver.find_element(By.TAG_NAME, "body").text


def wait_text(driver, timeout, *texts):
    try:
        WebDriverWait(driver, timeout).until(lambda d: any(t in body_text(d) for t in texts))
        return True
    except TimeoutException:
        return False


def wait_queue_settled(driver, timeout):
    """Loading row gone: real rows, the empty message, or an error banner."""
    try:
        WebDriverWait(driver, timeout).until(
            lambda d: "Loading deliveries" not in body_text(d) and d.find_elements(By.CSS_SELECTOR, "table tbody tr"))
        return True
    except TimeoutException:
        return False


class Network:
    def __init__(self, driver):
        self.driver, self.requests, self.responses, self.extra = driver, {}, {}, {}

    def drain(self):
        for entry in self.driver.get_log("performance"):
            message = json.loads(entry["message"])["message"]
            params = message.get("params", {})
            method = message.get("method")
            if method == "Network.requestWillBeSent":
                request = params.get("request", {})
                # skip CORS preflights: they never carry a token and answer 200/204 whatever the real call does
                if "/api/" in request.get("url", "") and request.get("method") != "OPTIONS":
                    self.requests[params["requestId"]] = request
            elif method == "Network.requestWillBeSentExtraInfo":
                self.extra[params.get("requestId")] = params.get("headers", {})
            elif method == "Network.responseReceived" and params.get("requestId") in self.requests:
                self.responses[params["requestId"]] = params.get("response", {}).get("status")

    @staticmethod
    def _auth(*header_sets):
        for headers in header_sets:
            for key, value in (headers or {}).items():
                if key.lower() == "authorization" and value:
                    return value
        return ""

    def calls(self, needle=""):
        self.drain()
        return [{"method": r.get("method"), "url": r.get("url"), "status": self.responses.get(rid),
                 "auth": self._auth(r.get("headers"), self.extra.get(rid))}
                for rid, r in self.requests.items() if needle in r.get("url", "")]

    def role(self):
        for call in self.calls():
            token = call["auth"].replace("Bearer ", "")
            if token.count(".") == 2:
                part = token.split(".")[1] + "=" * (-len(token.split(".")[1]) % 4)
                claims = json.loads(base64.urlsafe_b64decode(part))
                roles = claims.get("roles") or []
                roles = roles if isinstance(roles, list) else [roles]
                for role in roles:
                    name = role.split("/")[-1]
                    if name in {"CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep", "ShopOwner"}:
                        return name
        return None


# ---------------------------------------------------------------- scenarios
def scenario_deliveries(driver, args, results, net, shot):
    spa_navigate(driver, "/deliveries")
    settled = wait_queue_settled(driver, args.timeout)
    results.check("DEL-01 queue page settles (rows, empty or error)", settled)
    shot("DEL-01-queue")
    role = args.role or net.role()
    results.note(f"role from token: {role}")
    headers = [h.text.strip().upper() for h in driver.find_elements(By.CSS_SELECTOR, "table thead th")]
    expected = COLUMNS["agency"] if role in ("CompanyAdmin", "AreaManager", "AgencyOperator") else COLUMNS.get(role, [])
    results.check(f"DEL-02 columns for {role}: {expected}", headers == expected, f"got {headers}")
    calls = net.calls("/api/deliveries")
    results.check("DEL-03 the page called GET /api/deliveries", bool(calls))
    if args.delivery_gateway_host:
        wrong = [c["url"] for c in calls if urlparse(c["url"]).netloc != args.delivery_gateway_host]
        results.check("DEL-04 every delivery call goes to the APIM gateway", not wrong, str(wrong[:3]))
    results.check("DEL-05 every delivery call carries a bearer token", all(c["auth"].startswith("Bearer ") for c in calls))
    statuses = [c["status"] for c in calls if c["method"] == "GET"]
    results.check("DEL-06 list call answered 200", bool(statuses) and all(s == 200 for s in statuses), str(statuses))
    order_links = [a.get_attribute("href") for a in driver.find_elements(By.CSS_SELECTOR, "table tbody a[href*='/orders/']")]
    results.check("DEL-07 Order Ref links point at a real order (no /orders/undefined)",
                  not any(link.rstrip("/").endswith("undefined") for link in order_links), str(order_links[:3]))
    if "TERRITORY" in headers:
        index = headers.index("TERRITORY") + 1
        cells = [c.text.strip() for c in driver.find_elements(By.CSS_SELECTOR, f"table tbody tr td:nth-child({index})")]
        if cells and not any(m in body_text(driver) for m in EMPTY_MESSAGES):
            results.check("DEL-08 Territory column filled", all(cells), f"blank cells: {cells.count('')} of {len(cells)}")
    links = driver.find_elements(By.CSS_SELECTOR, "table tbody a[href*='/deliveries/']")
    if not links:
        results.note("no delivery rows - detail checks skipped")
        return
    links[0].click()
    opened = wait_text(driver, args.timeout, "Delivery History", "Failed to load delivery details.")
    results.check("DEL-09 detail page opens with lines and history", opened and "Delivery History" in body_text(driver)
                  and "Delivery Lines" in body_text(driver))
    shot("DEL-09-detail")
    text = body_text(driver)
    status_buttons = [b for b in ("Start delivery", "Mark delivered", "Mark failed") if b in text]
    if role != "SalesRep":
        results.check(f"DEL-10 no rep status buttons for {role}", not status_buttons, str(status_buttons))
    if role != "AgencyOperator":
        results.check(f"DEL-11 no 'Assign Delivery' button for {role}", "Assign Delivery" not in text)


def scenario_error_state(driver, args, results, net, shot):
    spa_navigate(driver, "/deliveries")
    wait_queue_settled(driver, args.timeout)
    time.sleep(3)
    text = body_text(driver)
    shot("ERR-01-delivery-down")
    results.check("ERR-01 page header still shown (not a blank screen)", "Deliveries" in text or "My deliveries" in text)
    results.check("ERR-02 an error message is shown", "could not be loaded" in text or "delivery service answered" in text
                  or "Failed" in text or "unavailable" in text.lower(), text[:300])
    results.check("ERR-03 the empty-state text is NOT shown (an outage is not 'no deliveries')",
                  not any(m in text for m in EMPTY_MESSAGES), "an outage looks like an empty list")


def scenario_empty_state(driver, args, results, net, shot):
    spa_navigate(driver, "/deliveries")
    wait_queue_settled(driver, args.timeout)
    shot("EMP-01-empty")
    results.check("EMP-01 empty-state message shown", any(m in body_text(driver) for m in EMPTY_MESSAGES))
    results.check("EMP-02 count reads 0 deliveries", "0 deliveries" in body_text(driver))


def open_delivery(driver, args):
    if not args.delivery_id:
        raise SystemExit("--delivery-id is required for this scenario")
    spa_navigate(driver, f"/deliveries/{args.delivery_id}")
    return wait_text(driver, args.timeout, "Delivery History", "Failed to load delivery details.")


def scenario_status_actions(driver, args, results, net, shot):
    open_delivery(driver, args)
    buttons = driver.find_elements(By.XPATH, "//button[normalize-space()='Start delivery']")
    if not results.check("ACT-01 assigned rep sees 'Start delivery' on an Assigned job", bool(buttons)):
        shot("ACT-01-no-button")
        return
    buttons[0].click()
    time.sleep(4)
    shot("ACT-02-after-start")
    patch = [c for c in net.calls("/status") if c["method"] == "PATCH"]
    results.check("ACT-02 PATCH /status answered 204", patch and patch[-1]["status"] == 204,
                  f"{[c['status'] for c in patch]} - 400 means the client did not send 'version'")
    results.check("ACT-03 no 'Update Failed' alert", "Update Failed" not in body_text(driver))
    results.check("ACT-04 badge and buttons move to In Transit", "Mark delivered" in body_text(driver))


def scenario_assign_dialog(driver, args, results, net, shot):
    open_delivery(driver, args)
    buttons = driver.find_elements(By.XPATH, "//button[normalize-space()='Assign Delivery']")
    if not results.check("ASG-01 'Assign Delivery' shown to the operator on a Pending job", bool(buttons)):
        return
    buttons[0].click()
    opened = wait_text(driver, args.timeout, "Assign Delivery Job")
    results.check("ASG-02 dialog opens", opened)
    time.sleep(2)
    shot("ASG-02-dialog")
    calls = [c for c in net.calls("/eligible-reps")]
    results.check("ASG-03 eligible reps requested (live, from the dialog)", bool(calls) and calls[-1]["status"] == 200,
                  str([c["status"] for c in calls]))
    text = body_text(driver)
    results.check("ASG-04 a rep is pre-selected, or the dialog says none are eligible",
                  "Select a sales rep" not in text or "No eligible reps found" in text, text[-300:])
    results.check("ASG-05 date picker offers 'Pick a date'", "Pick a date" in text or "Scheduled Date" in text)


ROLES = ("CompanyAdmin", "AreaManager", "AgencyOperator", "SalesRep", "ShopOwner")


def role_from_page(driver):
    """Fallback: the header shows the signed-in role under the user name."""
    text = body_text(driver)
    return next((r for r in ROLES if r in text), None)


def scenario_route_guard(driver, args, results, net, shot):
    role = args.role or net.role() or role_from_page(driver)
    for path, allowed in ROUTE_ACCESS.items():
        spa_navigate(driver, path)
        time.sleep(2)
        text = body_text(driver).lower()
        landed = urlparse(driver.current_url).path.rstrip("/") or "/"
        blocked = landed != path or any(m in text for m in (
            "not authorised", "not authorized", "access denied", "forbidden", "don't have permission",
            "do not have permission", "not allowed", "no access", "unauthorised", "unauthorized"))
        not_built = any(m in text for m in ("page not found", "404", "coming soon"))
        shot(f"GUARD{path.replace('/', '-')}")
        should_open = role in allowed
        label = f"GUARD {path} for {role}: {'opens' if should_open else 'blocked'}"
        if not_built and not blocked:
            results.note(f"{label} - page shows 'not found / coming soon' (screen not built yet), url {driver.current_url}")
            continue
        results.check(label, blocked != should_open,
                      f"url {driver.current_url}; page starts: {body_text(driver)[:160]!r}")


def scenario_returns_menu(driver, args, results, net, shot):
    text = body_text(driver)
    shot("MENU-01-navigation")
    results.check("MENU-01 'Shop returns' in the menu", "Shop returns" in text)
    role = args.role or net.role()
    if role == "ShopOwner":
        results.note("MENU-02 'Van returns' is a field-team entry - not expected for a shop owner"
                     + ("" if "Van returns" not in text else " (but it IS shown)"))
    else:
        results.check(f"MENU-02 'Van returns' still a separate entry for {role}", "Van returns" in text)
    links = [a.get_attribute("href") for a in driver.find_elements(By.XPATH, "//a[normalize-space()='Shop returns']")]
    results.check("MENU-03 'Shop returns' goes to /returns", any(l and l.rstrip("/").endswith("/returns") for l in links),
                  str(links))


def scenario_map(driver, args, results, net, shot):
    driver.execute_cdp_cmd("Network.enable", {})
    driver.execute_cdp_cmd("Network.setBlockedURLs", {"urls": ["*maps.googleapis.com*", "*maps.gstatic.com*"]})
    open_delivery(driver, args)
    time.sleep(5)
    shot("MAP-01-maps-blocked")
    text = body_text(driver)
    results.check("MAP-01 'Map unavailable' shown when Google Maps cannot load", "Map unavailable" in text)
    results.check("MAP-02 last update time or 'No position yet' shown", "No position yet" in text or "Last update" in text
                  or "last update" in text.lower())
    results.check("MAP-03 rest of the page still works (history shown)", "Delivery History" in text)
    driver.execute_cdp_cmd("Network.setBlockedURLs", {"urls": []})


def scenario_audit(driver, args, results, net, shot):
    reference = args.order_reference or ""
    spa_navigate(driver, f"/audit?orderReference={reference}")
    loaded = wait_text(driver, args.timeout, reference or "Audit", "No entries")
    shot("AUD-01-search")
    results.check("AUD-01 audit page loads", loaded)
    if reference:
        results.check(f"AUD-02 entries for {reference} listed", reference in body_text(driver))
        squashed = re.sub(r"[\s_-]", "", body_text(driver)).lower()
        for event_type in ("OrderPlaced", "OrderConfirmed", "PaymentRecorded"):
            # the page may show the type as a label ("Order placed") rather than the raw name
            results.check(f"AUD-03 {event_type} row shown", event_type.lower() in squashed)
    calls = net.calls("/api/audit")
    results.check("AUD-04 search calls the audit gateway with a bearer token",
                  bool(calls) and all(c["auth"].startswith("Bearer ") for c in calls))
    verify = driver.find_elements(By.XPATH, "//button[contains(normalize-space(), 'Verify integrity')]")
    role = args.role or net.role()
    if role == "CompanyAdmin":
        if results.check("AUD-05 'Verify integrity' shown to the Company Admin", bool(verify)):
            verify[0].click()
            time.sleep(5)
            shot("AUD-05-verify")
            results.check("AUD-06 a verify result is shown", "verified" in body_text(driver).lower()
                          or "broken" in body_text(driver).lower() or "intact" in body_text(driver).lower())
    else:
        results.check(f"AUD-05 no 'Verify integrity' for {role}", not verify)


def scenario_reports(driver, args, results, net, shot):
    spa_navigate(driver, "/reports")
    loaded = wait_text(driver, args.timeout, "Sales", "Orders")
    time.sleep(3)
    shot("REP-01-reports")
    text = body_text(driver)
    results.check("REP-01 reports page loads", loaded)
    for card in ("Sales", "Orders", "Collected", "Expected"):
        results.check(f"REP-02 '{card}' card shown", card.lower() in text.lower())
    results.check("REP-03 charts rendered (recharts svg)", bool(driver.find_elements(By.CSS_SELECTOR, ".recharts-wrapper svg")))
    results.check("REP-04 LKR formatting used", "LKR" in text or "Rs" in text)
    calls = net.calls("/api/reports")
    results.check("REP-05 report calls answered 200", bool(calls) and all(c["status"] == 200 for c in calls if c["status"]),
                  str([c["status"] for c in calls]))


SCENARIOS = {"deliveries": ("CSP-264", scenario_deliveries), "error-state": ("CSP-264", scenario_error_state),
             "empty-state": ("CSP-264", scenario_empty_state), "status-actions": ("CSP-263", scenario_status_actions),
             "assign-dialog": ("CSP-262", scenario_assign_dialog), "route-guard": ("CSP-264", scenario_route_guard),
             "returns-menu": ("CSP-266", scenario_returns_menu), "map": ("CSP-267", scenario_map),
             "audit": ("CSP-250", scenario_audit), "reports": ("CSP-252", scenario_reports)}


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--base-url", required=True)
    p.add_argument("--user", required=True)
    p.add_argument("--password", required=True)
    p.add_argument("--scenario", action="append", choices=sorted(SCENARIOS), required=True)
    p.add_argument("--role", help="override the role read from the token")
    p.add_argument("--delivery-id", help="delivery to open (status-actions, assign-dialog, map)")
    p.add_argument("--order-reference", help="order reference to search (audit)")
    p.add_argument("--delivery-gateway-host", help="expected host of delivery API calls (APIM)")
    p.add_argument("--evidence", default="evidence")
    p.add_argument("--timeout", type=int, default=30)
    p.add_argument("--headless", action="store_true")
    args = p.parse_args()

    stamp = f"{datetime.now():%Y%m%d-%H%M%S}-{urlparse(args.base_url).netloc.replace(':', '_')}-{args.user}"
    driver = build_driver(args.headless)
    results = Results()
    folders = {}
    try:
        login(driver, args.base_url, args.user, args.password, args.timeout)
        net = Network(driver)
        for name in args.scenario:
            jira, fn = SCENARIOS[name]
            folder = pathlib.Path(args.evidence) / jira / "selenium" / stamp
            folder.mkdir(parents=True, exist_ok=True)
            folders[jira] = folder
            print(f"\n== {jira}  {name}")

            def shot(label, _folder=folder):
                driver.save_screenshot(str(_folder / f"{label}.png"))
            try:
                fn(driver, args, results, net, shot)
            except (TimeoutException, SystemExit) as error:
                results.check(f"{name} finished without timeout", False, str(error)[:200])
                shot(f"{name}-timeout")
        summary = net.calls()
        for folder in folders.values():
            (folder / "network-summary.json").write_text(json.dumps(
                [{k: (v if k != "auth" else ("Bearer <redacted>" if v else "")) for k, v in c.items()} for c in summary],
                indent=2), encoding="utf-8")
            (folder / "results.txt").write_text("\n".join(f"{s}  {n}" + (f"  -> {d}" if d else "") for s, n, d in results.rows),
                                                encoding="utf-8")
    finally:
        driver.quit()
    print(f"\nRESULT: {len(results.rows) - len(results.failed)} passed/info, {len(results.failed)} failed")
    print("Evidence: " + ", ".join(str(f) for f in folders.values()))
    sys.exit(1 if results.failed else 0)


if __name__ == "__main__":
    main()