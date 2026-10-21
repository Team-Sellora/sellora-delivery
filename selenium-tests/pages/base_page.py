from selenium.common.exceptions import StaleElementReferenceException
from selenium.webdriver.common.by import By
from selenium.webdriver.support import expected_conditions as EC
from selenium.webdriver.support.ui import WebDriverWait

DEFAULT_TIMEOUT = 20

SIDEBAR = (By.CSS_SELECTOR, "nav[aria-label='Main navigation']")
PAGE_HEADING = (By.CSS_SELECTOR, ".page-heading h1")
BREADCRUMB = (By.CSS_SELECTOR, "nav[aria-label='Breadcrumb']")

# Sets a React-controlled input value and fires the events React listens to
SET_REACT_VALUE_JS = """
const el = arguments[0];
const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set;
setter.call(el, arguments[1]);
el.dispatchEvent(new Event('input', { bubbles: true }));
el.dispatchEvent(new Event('change', { bubbles: true }));
"""


class BasePage:
    def __init__(self, driver, timeout=DEFAULT_TIMEOUT):
        self.driver = driver
        self.wait = WebDriverWait(
            driver, timeout, ignored_exceptions=(StaleElementReferenceException,)
        )

    def find(self, locator):
        return self.wait.until(EC.presence_of_element_located(locator))

    def visible(self, locator):
        return self.wait.until(EC.visibility_of_element_located(locator))

    def click(self, locator):
        self.wait.until(EC.element_to_be_clickable(locator)).click()

    def is_present(self, locator):
        return len(self.driver.find_elements(*locator)) > 0

    def read(self, fn):
        # Runs fn again if React replaced an element while it was being read
        def attempt(_driver):
            try:
                return [fn()]
            except StaleElementReferenceException:
                return False

        return self.wait.until(attempt)[0]

    def text_of(self, locator):
        return self.read(lambda: self.visible(locator).text.strip())

    def set_react_value(self, element, value):
        self.driver.execute_script(SET_REACT_VALUE_JS, element, value)

    # Sidebar

    def sidebar(self):
        return self.visible(SIDEBAR)

    def sidebar_link(self, label):
        return (By.CSS_SELECTOR, f"nav[aria-label='Main navigation'] a[title='{label}']")

    def has_sidebar_item(self, label):
        self.sidebar()
        return self.is_present(self.sidebar_link(label))

    def open_from_sidebar(self, label):
        self.sidebar()
        self.click(self.sidebar_link(label))

    # Page header

    def heading_text(self):
        return self.text_of(PAGE_HEADING)

    def wait_for_heading(self, expected_options):
        def heading_matches(driver):
            try:
                return any(
                    h.text.strip() in expected_options for h in driver.find_elements(*PAGE_HEADING)
                )
            except StaleElementReferenceException:
                return False

        self.wait.until(heading_matches)
        return self.heading_text()

    def breadcrumb_text(self):
        return self.text_of(BREADCRUMB)
