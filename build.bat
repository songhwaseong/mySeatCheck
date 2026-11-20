@echo off
setlocal
cd /d "%~dp0"

set "CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ^>^> C# compiler not found: %CSC%
  exit /b 1
)

set "OUTDIR=%~1"
if not defined OUTDIR set "OUTDIR=%~dp0app"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

"%CSC%" /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ ^
  /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Security.dll ^
  /r:lib\Microsoft.Web.WebView2.Core.dll /r:lib\Microsoft.Web.WebView2.WinForms.dll ^
  /win32icon:src\SeatWatch.ico /resource:src\SeatWatch.ico,SeatWatch.ico ^
  /resource:src\parse.js,parse.js ^
  "/out:%OUTDIR%\SeatWatch.exe" src\SeatWatch.cs src\TicketDashboard.cs src\TicketCard.cs src\FlightSearch.cs src\CheckHistory.cs src\SettingsStore.cs
if errorlevel 1 exit /b 1

copy /Y lib\Microsoft.Web.WebView2.Core.dll "%OUTDIR%\" >nul || exit /b 1
copy /Y lib\Microsoft.Web.WebView2.WinForms.dll "%OUTDIR%\" >nul || exit /b 1
copy /Y lib\WebView2Loader.dll "%OUTDIR%\" >nul || exit /b 1
echo ^>^> "%OUTDIR%\SeatWatch.exe"
