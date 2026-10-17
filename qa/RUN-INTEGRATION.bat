@echo off
rem Sprint 4 QA - runs the xUnit integration tests in the sellora-delivery repo and saves evidence\CSP-26x\integration\test-output.txt.
rem Needs: .NET 8 SDK, Docker Desktop running (Testcontainers starts PostgreSQL), a clone of sellora-delivery on the dev branch.
rem Usage:  RUN-INTEGRATION.bat C:\path\to\sellora-delivery
setlocal
cd /d "%~dp0"
set REPO=%~1
if "%REPO%"=="" (
  echo Usage: RUN-INTEGRATION.bat C:\path\to\sellora-delivery
  pause
  exit /b 1
)
set TESTS=%REPO%\tests\Sellora.DeliveryService.Tests
if not exist "%TESTS%" (
  echo Test project not found at %TESTS%
  pause
  exit /b 1
)
docker info >nul 2>nul || (echo Docker is not running. Start Docker Desktop and try again. & pause & exit /b 1)
if not exist "%TESTS%\Integration" mkdir "%TESTS%\Integration"
copy /y DeliverySprint4QaTests.cs "%TESTS%\Integration\" >nul

for %%N in (261 262 263 264) do (
  if not exist evidence\CSP-%%N\integration mkdir evidence\CSP-%%N\integration
  echo.
  echo ==== CSP-%%N
  dotnet test "%TESTS%" --filter "FullyQualifiedName~Sprint4Qa_CSP%%N" --logger "console;verbosity=normal" > evidence\CSP-%%N\integration\test-output.txt 2>&1
  findstr /c:"Passed " /c:"Failed " /c:"Passed!" /c:"Failed!" /c:"Total tests" /c:"No test matches" /c:"error CS" evidence\CSP-%%N\integration\test-output.txt
)
echo.
echo Tests with _Spec_ in the name are expected to fail on today's dev (KI-02, KI-03, KI-04, KI-06) - those failures are the bug evidence.
echo Remove Integration\DeliverySprint4QaTests.cs from the repo afterwards unless the team wants to keep it.
pause
