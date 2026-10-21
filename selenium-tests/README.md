# Selenium UI tests (Delivery)

Read-only Selenium tests for the Delivery pages of the Sellora web app
(Deliveries list, delivery details, Shop returns). Python, Selenium 4, pytest,
pytest-html, Page Object Model. Selenium Manager downloads the Chrome driver.

Test cases are listed in [test-cases.md](test-cases.md).

## Layout

- `conftest.py` - Chrome driver fixture, screenshot after every test
- `pages/` - page objects (login, base, deliveries, delivery details, shop returns)
- `tests/` - one test file per page group
- `reports/` - created by a run (html report, junit xml, screenshots), not committed

## Environment variables

| Name | Required | Default |
|------|----------|---------|
| `BASE_URL` | no | `http://test.selloralk.tech` |
| `TEST_USER` | yes | |
| `TEST_PASSWORD` | yes | |
| `HEADLESS` | no | `1` (set `0` to see the browser) |

## Run locally (PowerShell)

```powershell
cd selenium-tests
py -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:TEST_USER = "<user>"
$env:TEST_PASSWORD = "<password>"
pytest --junitxml=reports/junit.xml
```

Headed mode: `$env:HEADLESS = "0"` before `pytest`.
The report is `reports/report.html`; screenshots are in `reports/screenshots/`.

## Pipeline

`.github/workflows/selenium.yml` runs on push to `test/selenium-delivery` and on
manual dispatch. It needs the repository secrets `SELLORA_TEST_USER` and
`SELLORA_TEST_PASSWORD`. The reports folder is uploaded as the
`selenium-delivery-report` artifact.
