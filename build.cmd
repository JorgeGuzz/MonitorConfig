@echo off
rem Builds MonitorConfig.exe (the app) and tools\MonitorConfig.dev.exe (with the --shots/--bench developer tools)
rem using the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set "COMMON=/nologo /target:winexe /optimize+ /win32icon:src\app.ico /win32manifest:src\app.manifest /resource:src\lang.pack,lang.pack /r:System.Windows.Forms.dll /r:System.Drawing.dll"

if not exist "src\app.ico" (
  "%CSC%" /nologo /out:tools\MakeIcon.exe /r:System.Drawing.dll tools\MakeIcon.cs
  if errorlevel 1 exit /b 1
  tools\MakeIcon.exe src\app.ico
  if errorlevel 1 exit /b 1
)

rem Texts: checks that every language is complete and packs them compressed.
"%CSC%" /nologo /out:tools\PackLang.exe tools\PackLang.cs
if errorlevel 1 goto failed
tools\PackLang.exe src\Lang src\lang.pack
if errorlevel 1 (
  echo Missing or extra texts in src\Lang ^(see above^).
  exit /b 1
)

rem Closes the running copy (if any) so the .exe can be overwritten.
if exist MonitorConfig.exe .\MonitorConfig.exe --exit

"%CSC%" %COMMON% /out:MonitorConfig.exe /recurse:src\*.cs
if errorlevel 1 goto failed
"%CSC%" %COMMON% /define:DEVTOOLS /out:tools\MonitorConfig.dev.exe /recurse:src\*.cs
if errorlevel 1 goto failed
echo Done: %~dp0MonitorConfig.exe
exit /b 0

:failed
echo.
echo Build failed. If MonitorConfig.exe is running, exit it from the tray and try again.
exit /b 1
