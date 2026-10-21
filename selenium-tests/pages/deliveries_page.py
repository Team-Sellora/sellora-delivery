import re

from selenium.common.exceptions import StaleElementReferenceException, TimeoutException
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait

from pages.base_page import BasePage

SIDEBAR_LABEL = "Deliveries"
HEADINGS = ("Deliveries", "My deliveries")

# Column sets per role, from roleView.tsx
AGENCY_COLUMNS = [
    "Delivery Ref", "Order Ref", "Shop", "Territory", "Status", "Scheduled Date", "Sales Rep", "Action",
]
SALES_REP_COLUMNS = ["Delivery Ref", "Shop", "Status", "Scheduled Date"]
SHOP_OWNER_COLUMNS = ["Order Ref", "Status", "Scheduled Date"]
KNOWN_COLUMN_SETS = (AGENCY_COLUMNS, SALES_REP_COLUMNS, SHOP_OWNER_COLUMNS)

EMPTY_MESSAGES = ("No deliveries found.", "No deliveries assigned to you.", "You have no upcoming deliveries.")
LOADING_TEXT = "Loading deliveries…"

QUEUE = "//section[.//span[normalize-space()='Delivery queue']]"
QUEUE_SECTION = (By.XPATH, QUEUE)
HEADER_CELLS = (By.XPATH, QUEUE + "//table/thead//th")
BODY_ROWS = (By.XPATH, QUEUE + "//table/tbody/tr")
ERROR_BANNER = (By.XPATH, QUEUE + "/div[contains(@class,'text-destructive')]")
TOOLBAR_SPANS = (By.XPATH, QUEUE + "/div[1]/span")
STATUS_FILTER = (By.XPATH, QUEUE + "//button[@role='combobox']")
FROM_DATE = (By.CSS_SELECTOR, "input[aria-label='From Date']")
TO_DATE = (By.CSS_SELECTOR, "input[aria-label='To Date']")
REP_FILTER = (By.CSS_SELECTOR, "input[placeholder='Sales Rep ID']")
PAGINATION = (By.CSS_SELECTOR, "nav[aria-label='Pagination']")
PAGE_LABEL = (By.XPATH, "//nav[@aria-label='Pagination']/span")
PREVIOUS_BUTTON = (By.XPATH, "//nav[@aria-label='Pagination']//button[normalize-space()='Previous']")
NEXT_BUTTON = (By.XPATH, "//nav[@aria-label='Pagination']//button[normalize-space()='Next']")
ROW_DETAIL_LINK = (By.CSS_SELECTOR, "a[href*='/deliveries/']")

COUNT_PATTERN = re.compile(r"^(\d+) deliver(y|ies)$")
PAGE_PATTERN = re.compile(r"^Page (\d+) of (\d+)$")


def option_locator(label):
    return (By.XPATH, f"//*[@role='option'][normalize-space()='{label}']")


class DeliveriesPage(BasePage):
    def open(self):
        self.open_from_sidebar(SIDEBAR_LABEL)
        self.wait_for_heading(HEADINGS)
        self.wait_until_loaded()
        return self

    # List state

    def _loaded(self, driver):
        try:
            if driver.find_elements(*ERROR_BANNER):
                return True
            rows = driver.find_elements(*BODY_ROWS)
            return len(rows) > 0 and rows[0].text.strip() != LOADING_TEXT
        except StaleElementReferenceException:
            return False

    def wait_until_loaded(self):
        self.find(QUEUE_SECTION)
        self.wait.until(self._loaded)

    def refresh_after(self, action):
        # Waits for the old rows to be replaced, then for the new list to load
        rows = self.driver.find_elements(*BODY_ROWS)
        action()
        if rows:
            try:
                WebDriverWait(self.driver, 5).until(EC.staleness_of(rows[0]))
            except TimeoutException:
                pass
        self.wait_until_loaded()

    def error_text(self):
        def banner_text():
            banners = self.driver.find_elements(*ERROR_BANNER)
            return banners[0].text.strip() if banners else None

        return self.read(banner_text)

    def column_headers(self):
        # Header cells are upper-cased by CSS, so compare without case
        return self.read(lambda: [th.text.strip() for th in self.driver.find_elements(*HEADER_CELLS)])

    def rows(self):
        return self.driver.find_elements(*BODY_ROWS)

    def _is_empty_now(self):
        rows = self.rows()
        return len(rows) == 1 and rows[0].text.strip() in EMPTY_MESSAGES

    def is_empty(self):
        return self.read(self._is_empty_now)

    def data_rows(self):
        return self.read(lambda: [] if self._is_empty_now() else self.rows())

    def total_count(self):
        def count():
            for span in self.driver.find_elements(*TOOLBAR_SPANS):
                match = COUNT_PATTERN.match(span.text.strip())
                if match:
                    return int(match.group(1))
            raise AssertionError("Delivery count label not found")

        return self.read(count)

    def status_values(self):
        def values():
            headers = [h.lower() for h in self.column_headers()]
            index = headers.index("status")
            rows = [] if self._is_empty_now() else self.rows()
            return [row.find_elements(By.TAG_NAME, "td")[index].text.strip() for row in rows]

        return self.read(values)

    # Filters (only shown to agency roles)

    def has_filters(self):
        return self.is_present(STATUS_FILTER)

    def status_filter_text(self):
        return self.text_of(STATUS_FILTER)

    def choose_status(self, label):
        def action():
            self.click(STATUS_FILTER)
            self.click(option_locator(label))

        self.refresh_after(action)

    def set_date_range(self, date_from, date_to):
        def action():
            self.set_react_value(self.find(FROM_DATE), date_from)
            self.set_react_value(self.find(TO_DATE), date_to)

        self.refresh_after(action)

    def date_range(self):
        return self.read(
            lambda: (
                self.find(FROM_DATE).get_attribute("value"),
                self.find(TO_DATE).get_attribute("value"),
            )
        )

    def set_rep_filter(self, value):
        self.refresh_after(lambda: self.set_react_value(self.find(REP_FILTER), value))

    def rep_filter_value(self):
        return self.read(lambda: self.find(REP_FILTER).get_attribute("value"))

    def reset_filters(self):
        if self.status_filter_text() != "All statuses":
            self.choose_status("All statuses")
        if any(self.date_range()):
            self.set_date_range("", "")
        if self.rep_filter_value():
            self.set_rep_filter("")

    # Paging

    def page_info(self):
        text = self.text_of(PAGE_LABEL)
        match = PAGE_PATTERN.match(text)
        assert match, f"Unexpected paging label: {text!r}"
        return int(match.group(1)), int(match.group(2))

    def previous_enabled(self):
        return self.read(lambda: self.find(PREVIOUS_BUTTON).is_enabled())

    def next_enabled(self):
        return self.read(lambda: self.find(NEXT_BUTTON).is_enabled())

    def go_next(self):
        self.refresh_after(lambda: self.click(NEXT_BUTTON))

    def go_previous(self):
        self.refresh_after(lambda: self.click(PREVIOUS_BUTTON))

    # Details

    def open_first_delivery(self):
        def open_link():
            link = self.rows()[0].find_element(*ROW_DETAIL_LINK)
            reference = link.text.strip()
            link.click()
            return reference

        return self.read(open_link)
