import pytest

from pages.deliveries_page import HEADINGS, DeliveriesPage
from pages.delivery_detail_page import SECTION_TITLES, DeliveryDetailPage
from pages.login_page import LoginPage

EMPTY_LIST_REASON = "The Deliveries list is empty for this user, so there is no delivery to open"


@pytest.fixture
def logged_in(credentials, driver, base_url):
    LoginPage(driver, base_url).login(*credentials)
    return driver


@pytest.fixture
def opened_delivery(logged_in):
    deliveries = DeliveriesPage(logged_in).open()
    assert deliveries.error_text() is None, f"Deliveries could not be loaded: {deliveries.error_text()}"
    if deliveries.is_empty():
        pytest.skip(EMPTY_LIST_REASON)
    reference = deliveries.open_first_delivery()
    return reference, DeliveryDetailPage(logged_in).wait_until_loaded()


@pytest.mark.details
def test_tc_del_08_open_delivery_shows_details(opened_delivery, logged_in):
    """TC-DEL-08: Opening one delivery shows its details.

    Steps: open Deliveries, click the link in the first row.
    Expected: the URL is /deliveries/<id>, the heading starts with "Delivery",
    the breadcrumb has "Deliveries", and the detail sections are shown.
    """
    _, detail = opened_delivery
    assert "/deliveries/" in logged_in.current_url
    assert detail.heading_text().startswith("Delivery ")
    assert "Deliveries" in detail.breadcrumb_text()
    titles = detail.section_titles()
    for title in SECTION_TITLES:
        assert title in titles, f"Missing section {title!r}"


@pytest.mark.details
def test_tc_del_09_back_returns_to_list(opened_delivery, logged_in):
    """TC-DEL-09: Going back returns to the Deliveries list.

    Steps: open one delivery, click "Back to Deliveries".
    Expected: the Deliveries heading and the delivery list are shown again.
    """
    _, detail = opened_delivery
    detail.back_to_list()
    deliveries = DeliveriesPage(logged_in)
    assert deliveries.wait_for_heading(HEADINGS) in HEADINGS
    deliveries.wait_until_loaded()
    assert logged_in.current_url.rstrip("/").endswith("/deliveries")
    assert len(deliveries.data_rows()) > 0
