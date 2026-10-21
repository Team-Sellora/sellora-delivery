import os

from selenium.common.exceptions import TimeoutException
from selenium.webdriver.common.by import By

from pages.base_page import SIDEBAR, BasePage

# Login locators: check these against the real identity server login page
APP_SIGN_IN_BUTTON = (By.XPATH, "//button[normalize-space()='Sign in']")
USERNAME_INPUT = (
    By.CSS_SELECTOR,
    "#usernameUserInput, input#username:not([type='hidden']), input[name='username']:not([type='hidden'])",
)
PASSWORD_INPUT = (By.CSS_SELECTOR, "#password, input[name='password']")
SUBMIT_BUTTON = (By.CSS_SELECTOR, "button[type='submit'], input[type='submit']")
# Guessed: consent screen the identity server may show on first login
CONSENT_BUTTON = (
    By.XPATH,
    "//button[normalize-space()='Allow' or normalize-space()='Approve' or normalize-space()='Continue']"
    " | //input[@type='submit' and (@value='Allow' or @value='Approve' or @value='Continue')]",
)

# Only the login steps use this; the identity server can be slow on the first login of a run
LOGIN_TIMEOUT = int(os.environ.get("LOGIN_TIMEOUT", "60"))
LOGIN_ATTEMPTS = 2


class LoginTimeoutError(TimeoutException):
    pass


class LoginPage(BasePage):
    def __init__(self, driver, base_url):
        super().__init__(driver, LOGIN_TIMEOUT)
        self.base_url = base_url

    def login(self, username, password):
        # Retry the whole login once: a cold identity server can time out the first attempt
        for attempt in range(1, LOGIN_ATTEMPTS + 1):
            try:
                self._login_once(username, password)
                return
            except LoginTimeoutError as e:
                if attempt == LOGIN_ATTEMPTS:
                    raise LoginTimeoutError(
                        f"Login failed after {LOGIN_ATTEMPTS} attempts "
                        f"(timeout {LOGIN_TIMEOUT}s per step). Last attempt: {e.msg}"
                    ) from e

    def _login_once(self, username, password):
        self._step("open the app", lambda: self.driver.get(self.base_url))
        # The app either shows its own Sign in button or redirects straight to the login form
        self._step(
            "wait for the login form, Sign in button or app sidebar",
            lambda: self.wait.until(
                lambda d: d.find_elements(*USERNAME_INPUT)
                or d.find_elements(*APP_SIGN_IN_BUTTON)
                or d.find_elements(*SIDEBAR)
            ),
        )
        if self.is_present(SIDEBAR):
            return
        if self.is_present(APP_SIGN_IN_BUTTON) and not self.is_present(USERNAME_INPUT):
            self._step("click the app Sign in button", lambda: self.click(APP_SIGN_IN_BUTTON))

        user_box = self._step("wait for the username field", lambda: self.visible(USERNAME_INPUT))
        user_box.clear()
        user_box.send_keys(username)
        pass_box = self._step("wait for the password field", lambda: self.visible(PASSWORD_INPUT))
        pass_box.clear()
        pass_box.send_keys(password)
        self._step("click the login submit button", lambda: self.click(SUBMIT_BUTTON))

        self._step(
            "wait for the app sidebar or consent page after submitting",
            lambda: self.wait.until(
                lambda d: d.find_elements(*SIDEBAR) or d.find_elements(*CONSENT_BUTTON)
            ),
        )
        if self.is_present(CONSENT_BUTTON) and not self.is_present(SIDEBAR):
            self._step("click Allow / Continue on the consent page", lambda: self.click(CONSENT_BUTTON))
            self._step("wait for the app sidebar after consent", lambda: self.visible(SIDEBAR))
        else:
            self._step("wait for the app sidebar", lambda: self.visible(SIDEBAR))

    def _step(self, name, action):
        try:
            return action()
        except TimeoutException as e:
            raise LoginTimeoutError(
                f"Login step timed out after {LOGIN_TIMEOUT}s: {name} (url: {self.driver.current_url})"
            ) from e
