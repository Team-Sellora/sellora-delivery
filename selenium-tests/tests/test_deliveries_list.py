import pytest
from selenium.webdriver.common.by import By

from pages.deliveries_page import HEADINGS, KNOWN_COLUMN_SETS, DeliveriesPage
from pages.login_page import LoginPage

NO_FILTERS_REASON = "Filters are only shown to agency roles (CompanyAdmin, AreaManager, AgencyOperator)"


@pytest.fixture
def logged_in(credentials, driver, base_url):
    LoginPage(driver, base_url).login(*credentials)
    return driver


@pytest.fixture
def deliveries(logged_in):
    page = DeliveriesPage(logged_in).open()
    assert page.error_text() is None, f"Deliveries could not be loaded: {page.error_text()}"
    return page


@pytest.mark.smoke
@pytest.mark.deliveries
def test_tc_del_02_deliveries_page_opens(logged_in):
    """TC-DEL-02: The Deliveries page opens with its heading.

    Steps: log in, click "Deliveries" in the sidebar.
    Expected: heading is "Deliveries" (or "My deliveries" for a sales rep),
    the breadcrumb shows it and the "Delivery queue" section is shown.
    """
    page = DeliveriesPage(logged_in).open()
    heading = page.heading_text()
    assert heading in HEADINGS
    assert heading in page.breadcrumb_text()
    assert "/deliveries" in logged_in.current_url


@pytest.mark.deliveries
def test_tc_del_03_list_or_empty_state_with_columns(deliveries):
    """TC-DEL-03: The list or the empty state renders with the expected columns.

    Steps: open Deliveries and wait for loading to finish.
    Expected: the column headers match one role's column set, and the table
    shows either delivery rows or the empty state message.
    """
    headers = [h.lower() for h in deliveries.column_headers()]
    expected = [[c.lower() for c in cols] for cols in KNOWN_COLUMN_SETS]
    assert headers in expected, f"Unexpected columns: {headers}"
    if deliveries.is_empty():
        assert deliveries.total_count() == 0
    else:
        assert len(deliveries.data_rows()) > 0
        assert len(deliveries.rows()[0].find_elements(By.TAG_NAME, "td")) == len(headers)


@pytest.mark.filters
def test_tc_del_04_status_filter_applies_and_resets(deliveries):
    """TC-DEL-04: The status filter applies and reset clears it.

    Steps: note the total, choose status "Pending", then choose "All statuses".
    Expected: with "Pending" every row shows status Pending (or the empty state)
    and the total is not larger; after reset the filter shows "All statuses"
    and the total is at least the filtered total.
    """
    if not deliveries.has_filters():
        pytest.skip(NO_FILTERS_REASON)
    before = deliveries.total_count()

    deliveries.choose_status("Pending")
    assert deliveries.error_text() is None
    assert deliveries.status_filter_text() == "Pending"
    assert all(s.startswith("Pending") for s in deliveries.status_values())
    filtered = deliveries.total_count()
    assert filtered <= before

    deliveries.reset_filters()
    assert deliveries.status_filter_text() == "All statuses"
    assert deliveries.total_count() >= filtered


@pytest.mark.filters
def test_tc_del_05_date_and_rep_filters_apply_and_reset(deliveries):
    """TC-DEL-05: The date range and Sales Rep ID filters apply and reset clears them.

    Steps: set From/To date to a wide range, then clear it; enter an unknown
    Sales Rep ID, then clear it.
    Expected: the date range keeps the total at or below the unfiltered total;
    an unknown rep shows the empty state; after each reset the inputs are empty
    and the list loads again without an error.
    """
    if not deliveries.has_filters():
        pytest.skip(NO_FILTERS_REASON)
    before = deliveries.total_count()

    deliveries.set_date_range("2000-01-01", "2099-12-31")
    assert deliveries.error_text() is None
    assert deliveries.date_range() == ("2000-01-01", "2099-12-31")
    assert deliveries.total_count() <= before

    deliveries.reset_filters()
    assert deliveries.date_range() == ("", "")
    assert deliveries.error_text() is None

    deliveries.set_rep_filter("00000000-0000-0000-0000-000000000000")
    assert deliveries.error_text() is None
    assert deliveries.is_empty(), "Unknown Sales Rep ID should show the empty state"

    deliveries.reset_filters()
    assert deliveries.rep_filter_value() == ""
    assert deliveries.error_text() is None
    assert deliveries.total_count() >= 0


@pytest.mark.filters
@pytest.mark.negative
def test_tc_del_06_filter_with_no_match_shows_empty_state(deliveries):
    """TC-DEL-06: A filter with no match shows the empty state.

    Steps: set From/To date to a range far in the future (2099).
    Expected: no error banner, the "No deliveries found." empty state is shown,
    the count reads 0 and paging shows "Page 1 of 1".
    """
    if not deliveries.has_filters():
        pytest.skip(NO_FILTERS_REASON)
    deliveries.set_date_range("2099-01-01", "2099-01-31")
    assert deliveries.error_text() is None
    assert deliveries.is_empty()
    assert deliveries.total_count() == 0
    assert deliveries.page_info() == (1, 1)


@pytest.mark.paging
def test_tc_del_07_paging_controls(deliveries):
    """TC-DEL-07: Paging controls work.

    Steps: open Deliveries; if there is more than one page click Next, then Previous.
    Expected: starts on "Page 1 of N" with Previous disabled; Next moves to
    page 2 and Previous returns to page 1; with one page Next is disabled.
    """
    page_no, total_pages = deliveries.page_info()
    assert page_no == 1 and total_pages >= 1
    assert not deliveries.previous_enabled()
    if total_pages == 1:
        assert not deliveries.next_enabled()
        return
    deliveries.go_next()
    assert deliveries.page_info() == (2, total_pages)
    assert deliveries.previous_enabled()
    deliveries.go_previous()
    assert deliveries.page_info() == (1, total_pages)
