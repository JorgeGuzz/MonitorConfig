@echo off
rem Pruebas de consola: modelo, servicios (con monitores simulados) y, salvo --no-ddc, una escritura inocua en el XG2705.
rem Uso: test.cmd [--no-ddc]
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
"%CSC%" /nologo /out:tools\Test.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll tools\Test.cs tools\Fakes.cs src\Strings.cs src\Lang.cs /recurse:src\Model\*.cs /recurse:src\Hardware\*.cs /recurse:src\Services\*.cs
if errorlevel 1 exit /b 1
"%CSC%" /nologo /out:tools\PackLang.exe tools\PackLang.cs
if errorlevel 1 exit /b 1
tools\PackLang.exe src\Lang src\lang.pack
if errorlevel 1 exit /b 1
tools\Test.exe %*
