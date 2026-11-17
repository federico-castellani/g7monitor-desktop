@echo off
rem Builds bin\G7MonitorDesktop.exe with the C# compiler included in Windows (.NET Framework 4.x),
rem then the installer dist\G7MonitorDesktop-Setup-<version>.exe if Inno Setup 6 is installed.
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%ROOT%bin" mkdir "%ROOT%bin"

rem The running copy locks the file: close it first.
taskkill /F /IM G7MonitorDesktop.exe >nul 2>&1

"%CSC%" /nologo /target:winexe /optimize+ ^
  /out:"%ROOT%bin\G7MonitorDesktop.exe" ^
  /win32icon:"%ROOT%assets\G7MonitorDesktop.ico" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /reference:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Core.dll,System.Security.dll,System.Web.Extensions.dll ^
  /recurse:"%ROOT%src\*.cs"
if errorlevel 1 (
    echo [ERROR] Compilation failed.
    exit /b 1
)
echo [OK] bin\G7MonitorDesktop.exe

set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" (
    echo [SKIP] Inno Setup 6 not found: installer not built.
    exit /b 0
)

"%ISCC%" /Q "%ROOT%installer\G7MonitorDesktop.iss"
if errorlevel 1 (
    echo [ERROR] Installer build failed.
    exit /b 1
)
echo [OK] installer in dist\
