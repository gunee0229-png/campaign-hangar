@echo off
setlocal
if "%~3"=="" (echo Usage: build-installer.cmd app-directory package-directory output.exe & exit /b 2)
set "APP=%~f1"
set "PACKAGE=%~f2"
set "INSTALLER=%~f3"
for %%F in ("%PACKAGE%\CampaignHangar-*.zip") do set "ZIP=%%~fF"
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /utf8output /win32manifest:"%~dp0app.manifest" /win32icon:"%APP%\assets\archive.ico" /out:"%INSTALLER%" /resource:"%ZIP%",release.zip /resource:"%PACKAGE%\latest.json",release.json /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "%~dp0Installer.cs" "%~dp0AppUpdate.cs" "%APP%\package-files.g.cs"
exit /b %errorlevel%
