@echo off
rem Build DiskScan.exe using the C# compiler that ships with Windows (.NET Framework 4.x)
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found ^(.NET Framework 4.x required^)
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu ^
  /win32manifest:src\app.manifest ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  /out:DiskScan.exe src\*.cs
if errorlevel 1 (
  echo BUILD FAILED
  pause
  exit /b 1
)
echo OK: DiskScan.exe
