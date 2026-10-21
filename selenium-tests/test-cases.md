# Delivery UI test cases

Target: Sellora web app (`BASE_URL`, default http://test.selloralk.tech).
All tests are read-only. Every test starts with a fresh browser and logs in with `TEST_USER` / `TEST_PASSWORD`.

| ID | Title | Steps | Expected result | Actual | Status |
|----|-------|-------|-----------------|--------|--------|
| TC-DEL-01 | Login shows Deliveries in the sidebar | 1. Open BASE_URL. 2. Sign in on the identity server login page. | The app sidebar ("Main navigation") is visible and has a "Deliveries" item. | As expected (passed in 13.720 s). | Pass |
| TC-DEL-02 | Deliveries page opens | 1. Log in. 2. Click "Deliveries" in the sidebar. | Heading "Deliveries" ("My deliveries" for a sales rep), shown in the breadcrumb; URL contains /deliveries. | As expected (passed in 17.926 s). | Pass |
| TC-DEL-03 | List or empty state with columns | 1. Log in. 2. Open Deliveries. 3. Wait for loading to finish. | Column headers match the user's role set (agency: Delivery Ref, Order Ref, Shop, Territory, Status, Scheduled Date, Sales Rep, Action). Rows are shown, or the empty state with a count of 0. | As expected (passed in 17.307 s). | Pass |
| TC-DEL-04 | Status filter applies and resets | 1. Open Deliveries. 2. Note the total. 3. Choose status "Pending". 4. Choose "All statuses". | With "Pending" every row has status Pending (or the empty state) and the total is not larger. After reset the filter shows "All statuses" and the total is at least the filtered total. Skipped for non-agency roles. | As expected (passed in 30.260 s). | Pass |
| TC-DEL-05 | Date range and Sales Rep ID filters apply and reset | 1. Open Deliveries. 2. Set From 2000-01-01, To 2099-12-31. 3. Clear both dates. 4. Enter an unknown Sales Rep ID. 5. Clear it. | Date range keeps the total at or below the unfiltered total. Unknown rep shows the empty state. After each reset the inputs are empty and the list loads without an error. Skipped for non-agency roles. | As expected (passed in 25.867 s). | Pass |
| TC-DEL-06 | Filter with no match shows the empty state (negative) | 1. Open Deliveries. 2. Set From 2099-01-01, To 2099-01-31. | No error banner; "No deliveries found." is shown; count reads 0; paging shows "Page 1 of 1". Skipped for non-agency roles. | As expected (passed in 17.381 s). | Pass |
| TC-DEL-07 | Paging controls | 1. Open Deliveries. 2. If more than one page, click Next, then Previous. | Starts on "Page 1 of N" with Previous disabled. Next goes to page 2, Previous back to page 1. With one page, Next is disabled. | As expected (passed in 14.328 s). | Pass |
| TC-DEL-08 | Open one delivery shows details | 1. Open Deliveries. 2. Click the link in the first row. | URL is /deliveries/<id>; heading starts with "Delivery"; breadcrumb has "Deliveries"; sections Delivery Lines, Delivery History, Status and Details are shown. Skipped if the list is empty. | As expected (passed in 16.293 s). | Pass |
| TC-DEL-09 | Back returns to the Deliveries list | 1. Open one delivery. 2. Click "Back to Deliveries". | Deliveries heading and list are shown again; URL ends with /deliveries. Skipped if the list is empty. | As expected (passed in 16.960 s). | Pass |
| TC-DEL-10 | Shop returns page opens | 1. Log in. 2. Click "Shop returns" in the sidebar. | Heading "Shop returns"; columns Return reference, Order reference, Shop, Status, Requested date, Requested by; status filter has All statuses, Requested, Approved, Rejected; no error banner. | As expected (passed in 14.568 s). | Pass |

Note: "Van returns" calls the Order service, not the Delivery service, so it is not covered here.
