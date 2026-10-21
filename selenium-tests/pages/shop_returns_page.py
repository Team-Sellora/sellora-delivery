from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import Select

from pages.base_page import BasePage

SIDEBAR_LABEL = "Shop returns"
HEADING = "Shop returns"
COLUMNS = ["Return reference", "Order reference", "Shop", "Status", "Requested date", "Requested by"]
STATUS_OPTIONS = ["All statuses", "Requested", "Approved", "Rejected"]
EMPTY_MESSAGE = "No shop returns found."

STATUS_FILTER = (By.CSS_SELECTOR, "select[aria-label='Filter by status']")
HEADER_CELLS = (By.CSS_SELECTOR, "table thead th")
BODY_ROWS = (By.CSS_SELECTOR, "table tbody tr")
SKELETON = (By.CSS_SELECTOR, "table tbody .animate-pulse")
ERROR_BANNER = (By.CSS_SELECTOR, "div.text-destructive")


class ShopReturnsPage(BasePage):
    def open(self):
        self.open_from_sidebar(SIDEBAR_LABEL)
        self.wait_for_heading((HEADING,))
        self.wait_until_loaded()
        return self

    def wait_until_loaded(self):
        self.wait.until(
            lambda d: d.find_elements(*ERROR_BANNER)
            or (d.find_elements(*BODY_ROWS) and not d.find_elements(*SKELETON))
        )

    def error_text(self):
        def banner_text():
            banners = self.driver.find_elements(*ERROR_BANNER)
            return banners[0].text.strip() if banners else None

        return self.read(banner_text)

    def column_headers(self):
        return self.read(lambda: [th.text.strip() for th in self.driver.find_elements(*HEADER_CELLS)])

    def status_options(self):
        return self.read(lambda: [o.text.strip() for o in Select(self.find(STATUS_FILTER)).options])

    def is_empty(self):
        def empty():
            rows = self.driver.find_elements(*BODY_ROWS)
            return len(rows) == 1 and rows[0].text.strip() == EMPTY_MESSAGE

        return self.read(empty)

    def first_row_cell_count(self):
        return self.read(lambda: len(self.find(BODY_ROWS).find_elements(By.TAG_NAME, "td")))
