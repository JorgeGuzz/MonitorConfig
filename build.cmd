@echo off
rem Compila MonitorConfig.exe (uso diario) y tools\MonitorConfig.dev.exe (con las herramientas de prueba --shots/--bench)
rem con el compilador de C# que trae Windows (.NET Framework 4.x). No necesita instalar nada.
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

rem Textos: comprueba que todos los idiomas esten completos y los empaqueta comprimidos.
"%CSC%" /nologo /out:tools\PackLang.exe tools\PackLang.cs
if errorlevel 1 goto failed
tools\PackLang.exe src\Lang src\lang.pack
if errorlevel 1 (
  echo Faltan o sobran textos en src\Lang ^(ver arriba^).
  exit /b 1
)

rem Cierra la copia abierta (si la hay) para poder sobrescribir el .exe.
if exist MonitorConfig.exe .\MonitorConfig.exe --exit

"%CSC%" %COMMON% /out:MonitorConfig.exe /recurse:src\*.cs
if errorlevel 1 goto failed
"%CSC%" %COMMON% /define:DEVTOOLS /out:tools\MonitorConfig.dev.exe /recurse:src\*.cs
if errorlevel 1 goto failed
echo Listo: %~dp0MonitorConfig.exe
exit /b 0

:failed
echo.
echo Error al compilar. Si MonitorConfig.exe esta abierto, cierralo desde la bandeja y vuelve a intentarlo.
exit /b 1
