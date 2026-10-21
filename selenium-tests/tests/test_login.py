import pytest

from pages.base_page import BasePage
from pages.login_page import LoginPage


@pytest.fixture
def logged_in(credentials, driver, base_url):
    LoginPage(driver, base_url).login(*credentials)
    return driver


@pytest.mark.smoke
def test_tc_del_01_login_shows_deliveries_in_sidebar(logged_in):
    """TC-DEL-01: Login succeeds and the Deliveries item is in the sidebar.

    Steps: open BASE_URL, sign in with TEST_USER / TEST_PASSWORD.
    Expected: the app shell sidebar is visible and has a "Deliveries" item.
    """
    page = BasePage(logged_in)
    assert page.sidebar().is_displayed()
    assert page.has_sidebar_item("Deliveries"), "Deliveries item missing from the sidebar"
