@echo off
rem Sprint 4 load test: prepares data from staging, runs JMeter for 5 minutes, writes the HTML dashboard and the per-story summary.
rem Needs: Java 17+, Apache JMeter 5.6 (jmeter.bat on PATH or JMETER_HOME set), fresh tokens in .env, AUDIT_GW set in .env.
rem Usage:  RUN-LOAD.bat                 (default: 63 users, 300 s)
rem         RUN-LOAD.bat --scale 0.5     (half the users)   --duration 600   --sla-ms 1500
rem         RUN-LOAD.bat --delivery-only (skip CSP-250/252/253 while the Audit API is unavailable: 26 users)
setlocal
cd /d "%~dp0"
set PYTHONUTF8=1
set PYTHONIOENCODING=utf-8

set JM=jmeter
if defined JMETER_HOME set JM="%JMETER_HOME%\bin\jmeter.bat"
where %JM% >nul 2>nul || if not defined JMETER_HOME (echo JMeter not found. Install Apache JMeter 5.6 and add its bin folder to PATH, or set JMETER_HOME. & pause & exit /b 1)

python -c "from sellora_qa import load_env, check_tokens; check_tokens(load_env('.env')); print('Tokens OK')" || (echo Fix the tokens in .env first. & pause & exit /b 1)

echo.
echo ==== 1. Preparing load data from staging
python make_load_data.py %* || (pause & exit /b 1)

if exist load\results.jtl del /q load\results.jtl
if exist load\report rmdir /s /q load\report

echo.
echo ==== 2. Running JMeter (non-GUI). This takes the configured duration plus ramp-up.
call %JM% -n -t Sprint4-load-test.jmx -q load\load.properties -l load\results.jtl -j load\jmeter.log -e -o load\report -Jjmeter.save.saveservice.output_format=csv

echo.
echo ==== 3. Summary against the SLA
python load_summary.py load\results.jtl

if not exist evidence\CSP-250\jmeter mkdir evidence\CSP-250\jmeter
if exist evidence\CSP-250\jmeter\load-report rmdir /s /q evidence\CSP-250\jmeter\load-report
xcopy /e /i /q load\report evidence\CSP-250\jmeter\load-report >nul
copy /y load\results.jtl evidence\CSP-250\jmeter\results.jtl >nul
echo.
echo HTML dashboard: evidence\CSP-250\jmeter\load-report\index.html (covers every story in the run)
echo Never zip load\load.properties - it holds the access tokens.
pause
