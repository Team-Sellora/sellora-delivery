@echo off
rem Sprint 4 QA - runs every automated staging test and saves the output into evidence\CSP-xxx\api\.
rem Double-click, or run from cmd inside this folder. Fill .env with fresh tokens first (they last about 1 hour).
setlocal
cd /d "%~dp0"
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8

if not exist .env (
  echo .env is missing. Copy .env.example to .env and paste the five access tokens.
  pause
  exit /b 1
)
where python >nul 2>nul || (echo Python is not on PATH. Install Python 3.10+ and tick "Add to PATH". & pause & exit /b 1)

python -c "from sellora_qa import load_env, check_tokens; check_tokens(load_env('.env')); print('Tokens OK')" || (echo. & echo Fix the tokens in .env, then run this again. Nothing was run. & pause & exit /b 1)

for %%S in (CSP-250 CSP-252 CSP-253 CSP-261 CSP-262 CSP-263 CSP-264 CSP-265 CSP-266 CSP-267) do (
  if not exist evidence\%%S\api mkdir evidence\%%S\api
  if not exist evidence\%%S\ss mkdir evidence\%%S\ss
)

call :run CSP-261 delivery_creation_tests.py
call :run CSP-264 delivery_queue_tests.py
call :run CSP-262 delivery_assignment_tests.py
call :run CSP-263 delivery_lifecycle_tests.py
call :run CSP-266 shop_returns_tests.py
call :run CSP-265 delivery_confirmation_tests.py
call :run CSP-267 delivery_tracking_tests.py
call :run CSP-250 audit_log_tests.py
call :run CSP-252 reporting_tests.py
call :run CSP-253 report_export_tests.py

echo.
echo ==== Gateway, role and tenant checks
python api_sprint4_isolation.py > evidence\CSP-262\api\console-isolation-staging.txt 2>&1
type evidence\CSP-262\api\console-isolation-staging.txt

echo.
echo ==== Summary (one RESULT line per story file)
for %%S in (CSP-250 CSP-252 CSP-253 CSP-261 CSP-262 CSP-263 CSP-264 CSP-265 CSP-266 CSP-267) do (
  for %%F in (evidence\%%S\api\*-staging.txt) do if /i not "%%~nF"=="console-staging" if /i not "%%~nF"=="console-isolation-staging" (
    findstr /b /c:"RESULT:" "%%F" >nul && (for /f "delims=" %%L in ('findstr /b /c:"RESULT:" "%%F"') do echo %%S  %%~nxF  %%L) || echo %%S  %%~nxF  no RESULT line - open the file, the run stopped early
  )
)
echo.
echo Done. Now take the screenshots listed in each evidence\CSP-xxx\README.txt and save them in that story's ss folder.
pause
exit /b 0

:run
echo.
echo ==== %1  %2
python %2 --target staging > evidence\%1\api\console-staging.txt 2>&1
type evidence\%1\api\console-staging.txt
exit /b 0
