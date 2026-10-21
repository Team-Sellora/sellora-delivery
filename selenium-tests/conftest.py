import base64
import os
from pathlib import Path

import pytest
from pytest_html import extras
from selenium import webdriver

BASE_URL = os.environ.get("BASE_URL", "http://test.selloralk.tech").rstrip("/")
SCREENSHOT_DIR = Path(__file__).parent / "reports" / "screenshots"


@pytest.fixture(scope="session")
def base_url():
    return BASE_URL


@pytest.fixture(scope="session")
def credentials():
    user = os.environ.get("TEST_USER")
    password = os.environ.get("TEST_PASSWORD")
    if not user or not password:
        pytest.exit("TEST_USER and TEST_PASSWORD environment variables must be set.", returncode=2)
    return user, password


@pytest.fixture
def driver(request):
    options = webdriver.ChromeOptions()
    if os.environ.get("HEADLESS", "1") != "0":
        options.add_argument("--headless=new")
    options.add_argument("--no-sandbox")
    options.add_argument("--disable-dev-shm-usage")
    options.add_argument("--window-size=1920,1080")
    options.add_argument("--ignore-certificate-errors")
    options.accept_insecure_certs = True
    drv = webdriver.Chrome(options=options)
    yield drv
    _save_screenshot(drv, request)
    drv.quit()


@pytest.hookimpl(hookwrapper=True)
def pytest_runtest_makereport(item, call):
    outcome = yield
    report = outcome.get_result()
    setattr(item, "rep_" + report.when, report)
    # Attach the screenshot saved by the driver fixture to the html report
    if report.when == "teardown":
        shot = getattr(item, "screenshot_path", None)
        if shot and shot.exists():
            report.extras = getattr(report, "extras", []) + [_html_image(shot)]


def _save_screenshot(drv, request):
    call_rep = getattr(request.node, "rep_call", None)
    failed = call_rep is None or call_rep.failed
    status = "failed" if failed else "passed"
    SCREENSHOT_DIR.mkdir(parents=True, exist_ok=True)
    path = SCREENSHOT_DIR / f"{request.node.name}-{status}.png"
    try:
        drv.save_screenshot(str(path))
        request.node.screenshot_path = path
    except Exception:
        pass


def _html_image(path):
    return extras.png(base64.b64encode(path.read_bytes()).decode("ascii"))
