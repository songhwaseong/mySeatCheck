@echo off
rem Make dist\SeatWatch-yyyyMMdd.zip for sharing (no personal data). See package.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0package.ps1"
set "packageResult=%ERRORLEVEL%"
pause
exit /b %packageResult%
