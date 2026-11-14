@echo off
setlocal
set "CHANNEL=%~2"
if "%CHANNEL%"=="" set "CHANNEL=development"
if /i "%CHANNEL%"=="development" (set "BUILD_SYMBOLS=DEVELOPMENT") else if /i "%CHANNEL%"=="release" (set "BUILD_SYMBOLS=RELEASE") else (echo Unknown channel: %CHANNEL% & exit /b 2)
if /i "%~3"=="update-fixture" set "BUILD_SYMBOLS=%BUILD_SYMBOLS%,UPDATE_FIXTURE"
set "OUT=%~dp0bin-%CHANNEL%"
if not "%~1"=="" set "OUT=%~f1"
set "WV=%~dp0vendor\WebView2-1.0.4258.31"
set "CSC_EXTRA=/define:%BUILD_SYMBOLS% /reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll /reference:C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll"
set EXTRA="%~dp0LiveUiTheme.cs" "%~dp0LiveUiHub.cs" "%~dp0DistributionPolicy.cs" "%~dp0AppUpdate.cs" "%~dp0Notifications.cs" "%~dp0Activity.cs" "%~dp0ActivityChecks.cs" "%~dp0AuthorLauncherChecks.cs" "%~dp0EpisodeChecks.cs" "%~dp0StandaloneZip.cs" "%~dp0StandaloneConflicts.cs" "%~dp0StandaloneZipChecks.cs" "%~dp0Dedicated.cs" "%~dp0DedicatedChecks.cs" "%~dp0DedicatedVoiceDialog.cs" "%~dp0DedicatedVoiceChecks.cs"
if not exist "%OUT%" mkdir "%OUT%"
xcopy /e /i /y "%~dp0bin\assets" "%OUT%\assets" >nul
if errorlevel 1 exit /b %errorlevel%
xcopy /e /i /y "%~dp0ui-hub" "%OUT%\assets\ui-hub" >nul
if errorlevel 1 exit /b %errorlevel%
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0generate-package-files.ps1" -OutputFile "%OUT%\package-files.g.cs"
if errorlevel 1 exit /b %errorlevel%
set EXTRA=%EXTRA% "%OUT%\package-files.g.cs" "%~dp0WorkWindow.cs" /resource:"%~dp0ui-hub\progress.html",CampaignHangar.Progress
copy /y "%WV%\Microsoft.Web.WebView2.Core.dll" "%OUT%\Microsoft.Web.WebView2.Core.dll" >nul
if errorlevel 1 exit /b %errorlevel%
copy /y "%WV%\Microsoft.Web.WebView2.WinForms.dll" "%OUT%\Microsoft.Web.WebView2.WinForms.dll" >nul
if errorlevel 1 exit /b %errorlevel%
copy /y "%WV%\WebView2Loader.dll" "%OUT%\WebView2Loader.dll" >nul
if errorlevel 1 exit /b %errorlevel%
copy /y "%WV%\LICENSE.txt" "%OUT%\WebView2-LICENSE.txt" >nul
if errorlevel 1 exit /b %errorlevel%
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" %CSC_EXTRA% /nologo /target:winexe /platform:x64 /utf8output /win32manifest:"%~dp0app.manifest" /win32icon:"%OUT%\assets\archive.ico" /out:"%OUT%\CampaignHangar.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:"%OUT%\Microsoft.Web.WebView2.Core.dll" /reference:"%OUT%\Microsoft.Web.WebView2.WinForms.dll" /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /reference:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" "%~dp0Core.cs" "%~dp0Storage.cs" "%~dp0Catalogue.cs" "%~dp0Program.cs" "%~dp0Shell.cs" "%~dp0InstalledView.cs" "%~dp0ThumbnailStore.cs" "%~dp0Visuals.cs" "%~dp0Checks.cs" "%~dp0ArchiveRuntime.cs" "%~dp0ArchiveView.cs" "%~dp0DistributionView.cs" "%~dp0ZipDropTarget.cs" "%~dp0SiteMatcher.cs" "%~dp0SitePicker.cs" %EXTRA%
if errorlevel 1 exit /b %errorlevel%
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /define:%BUILD_SYMBOLS% /nologo /target:winexe /platform:x64 /utf8output /out:"%OUT%\CampaignHangarUpdater.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "%~dp0AppUpdate.cs" "%~dp0AppUpdater.cs" "%~dp0AppUpdateChecks.cs" "%OUT%\package-files.g.cs"
if errorlevel 1 exit /b %errorlevel%
"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /utf8output /out:"%OUT%\CampaignHangarSetup.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll "%~dp0ArchiveRuntime.cs" "%~dp0Setup.cs"
if errorlevel 1 exit /b %errorlevel%
copy /y "%~dp0THIRD-PARTY-NOTICES.txt" "%OUT%\THIRD-PARTY-NOTICES.txt" >nul
copy /y "%~dp0App.config" "%OUT%\CampaignHangar.exe.config" >nul
echo Built: %OUT%\CampaignHangar.exe
