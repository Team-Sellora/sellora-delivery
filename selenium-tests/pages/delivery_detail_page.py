from selenium.webdriver.common.by import By

from pages.base_page import PAGE_HEADING, BasePage

SECTION_TITLES = ("Delivery Lines", "Delivery History", "Status", "Details")
LOAD_ERROR = "Failed to load delivery details."

BACK_LINK = (By.XPATH, "//a[normalize-space()='Back to Deliveries']")
SECTION_HEADINGS = (By.CSS_SELECTOR, "section h3")
LOAD_ERROR_TEXT = (By.XPATH, f"//div[normalize-space()='{LOAD_ERROR}']")


class DeliveryDetailPage(BasePage):
    def wait_until_loaded(self):
        self.wait.until(
            lambda d: d.find_elements(*LOAD_ERROR_TEXT)
            or any(h.text.startswith("Delivery ") for h in d.find_elements(*PAGE_HEADING))
        )
        assert not self.is_present(LOAD_ERROR_TEXT), LOAD_ERROR
        return self

    def section_titles(self):
        return [h.text.strip() for h in self.driver.find_elements(*SECTION_HEADINGS)]

    def back_to_list(self):
        self.click(BACK_LINK)
