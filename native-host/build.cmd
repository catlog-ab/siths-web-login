@echo off
rem Bygger SithsWebLogin.exe med kompilatorn i .NET Framework 4.8 (finns i alla Windows 10/11).
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist bin mkdir bin
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /out:bin\SithsWebLogin.exe ^
  /r:System.dll /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll ^
  Program.cs HostConfig.cs CardMonitor.cs CardSigner.cs
if errorlevel 1 exit /b 1
endlocal
