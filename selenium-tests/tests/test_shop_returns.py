import pytest

from pages.login_page import LoginPage
from pages.shop_returns_page import COLUMNS, HEADING, STATUS_OPTIONS, ShopReturnsPage


@pytest.fixture
def logged_in(credentials, driver, base_url):
    LoginPage(driver, base_url).login(*credentials)
    return driver


@pytest.mark.returns
def test_tc_del_10_shop_returns_page_opens(logged_in):
    """TC-DEL-10: The Shop returns page (served by the Delivery service) opens.

    Steps: log in, click "Shop returns" in the sidebar.
    Expected: heading "Shop returns", the expected columns, the status filter
    options, and either rows or the "No shop returns found." empty state.
    """
    page = ShopReturnsPage(logged_in)
    if not page.has_sidebar_item("Shop returns"):
        pytest.skip("Shop returns is not in the sidebar for this user's role")
    page.open()
    assert page.heading_text() == HEADING
    assert page.error_text() is None, f"Shop returns could not be loaded: {page.error_text()}"
    assert [h.lower() for h in page.column_headers()] == [c.lower() for c in COLUMNS]
    assert page.status_options() == STATUS_OPTIONS
    if not page.is_empty():
        assert page.first_row_cell_count() == len(COLUMNS)
