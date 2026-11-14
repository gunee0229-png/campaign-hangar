using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Contains("--self-test")) return Checks.Run();
            if ((args.Length == 2 || args.Length == 3) && args[0] == "--ccm-payload-check")
            {
                try { Checks.VerifyCcmPayload(Path.GetFullPath(args[1]),args.Length==3?Path.GetFullPath(args[2]):null);return 0; }
                catch(Exception error){Console.Error.WriteLine(error);return 1;}
            }
            if (args.Length == 2 && args[0] == "--standalone-zip-check")
            {
                try { StandaloneZipChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--standalone-zip-inspect")
            {
                try
                {
                    var package = StandaloneZip.Inspect(Path.GetFullPath(args[1]));
                    Console.WriteLine("ZIP: " + package.Name + " | map/mod assets: " +
                        package.GameFiles.Count + " | candidate launchers: " + package.Choices.Count);
                    foreach (var choice in package.Choices) Console.WriteLine(choice.Kind + " | " + choice.Title);
                    Console.WriteLine("READ ONLY PASS: no files extracted, installed, registered or launched.");
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 3 && args[0] == "--standalone-conflict-inspect")
            {
                try
                {
                    var package = StandaloneZip.Inspect(Path.GetFullPath(args[1]));
                    var conflicts = StandaloneConflicts.Inspect(package, Path.GetFullPath(args[2]));
                    Console.WriteLine("ZIP: " + package.Name + " | entries: " + package.GameFiles.Count +
                        " | map conflicts: " + conflicts.DifferentMaps + " | mod conflicts: " + conflicts.DifferentMods);
                    foreach (var path in conflicts.Different.Take(20)) Console.WriteLine("DIFFERENT: " + path);
                    Console.WriteLine("READ ONLY: no install, registration, backup or game execution.");
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 3 && args[0] == "--standalone-existing-inspect")
            {
                try
                {
                    var package = StandaloneZip.Inspect(Path.GetFullPath(args[1]));
                    foreach (var choice in package.Choices.Where(c => c.Kind == "map"))
                    {
                        var existing = StandaloneZip.ExistingMapVariant(package, choice, Path.GetFullPath(args[2]));
                        if (existing != null)
                            Console.WriteLine("EXISTING MAPS: " + existing.MapCount + " | different contents: " +
                                existing.DifferentFileCount + " | folder: " + existing.Folder);
                    }
                    Console.WriteLine("READ ONLY PASS: no game file, ZIP or registration changes.");
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--news-scroll-check")
            {
                try
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Checks.VerifyNewsScroll(Path.GetFullPath(args[1])); return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--gesture-scroll-visual-check")
            {
                try
                {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Checks.VerifyGestureScrollVisual(Path.GetFullPath(args[1])); return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 1 && args[0] == "--gesture-scroll-live-preview")
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Checks.PreviewGestureScroll(); return 0;
            }
            if (args.Length == 2 && args[0] == "--activity-check")
            {
                try { ActivityChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--external-link-ui-check")
            {
                try {
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    Checks.VerifyExternalLinkUI(Path.GetFullPath(args[1])); return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--zip-artwork-check")
            {
                try { Checks.VerifyZipArtwork(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--author-launcher-check")
            {
                try { AuthorLauncherChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--episode-check")
            {
                try { EpisodeChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-check")
            {
                try { DedicatedChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-wc3-check")
            {
                try { DedicatedChecks.RunWc3(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-wc3-remote-check")
            {
                try { DedicatedChecks.RunWc3(Path.GetFullPath(args[1]), true); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 3 && args[0] == "--dedicated-wc3-remote-check")
            {
                try { DedicatedChecks.RunWc3(Path.GetFullPath(args[1]), true, Path.GetFullPath(args[2])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-voice-ui-check")
            {
                try { DedicatedVoiceChecks.RunUI(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-voice-check")
            {
                try { DedicatedVoiceChecks.Run(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-speed-check")
            {
                try { DedicatedChecks.RunSpeed(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-online-check")
            {
                try { DedicatedChecks.RunPublished(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-additional-check")
            {
                try { DedicatedChecks.RunAdditional(Path.GetFullPath(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-online-additional-check")
            {
                try { DedicatedChecks.RunPublished(Path.GetFullPath(args[1]), true); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-allies-check")
            {
                try { DedicatedChecks.RunAdditional(Path.GetFullPath(args[1]), false, true); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-antioch-check")
            {
                try { DedicatedChecks.RunAdditional(Path.GetFullPath(args[1]), true); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-online-antioch-check")
            {
                try { DedicatedChecks.RunPublished(Path.GetFullPath(args[1]), false, true); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--dedicated-archive-check")
            {
                try { return DedicatedChecks.RunArchive(Path.GetFullPath(args[1])); }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && (args[0] == "--site-picker-check" || args[0] == "--auto-cleanup-check" ||
                args[0] == "--install-auto-apply-check" ||
                args[0] == "--removal-zip-check" || args[0] == "--settings-layout-check" || args[0] == "--responsive-apply-check" ||
                args[0] == "--metadata-identity-check"))
            {
                string root = Path.GetFullPath(args[1]);
                if (Directory.Exists(root)) throw new InvalidOperationException("Checks require a new isolated directory.");
                Directory.CreateDirectory(root);
                int result = 0;
                using (var form = new Hangar(Path.Combine(root, "hangar"), null, false))
                {
                    form.Shown += delegate
                    {
                        form.BeginInvoke(new Action(async delegate
                        {
                            try
                            {
                                if (args[0] == "--settings-layout-check") form.VerifySettingsLayout(root);
                                else if (args[0] == "--metadata-identity-check") await Checks.VerifyMetadataImport(form, root);
                                else if (args[0] == "--responsive-apply-check") await Checks.VerifyResponsiveApply(form, root);
                                else if (args[0] == "--removal-zip-check") await Checks.VerifyRemovalZipCheck(form, root);
                                else if (args[0] == "--auto-cleanup-check") await Checks.VerifyAutoCleanup(form, root);
                                else if (args[0] == "--install-auto-apply-check") await Checks.VerifyInstallAutoApply(form, root);
                                else await Checks.VerifySitePicker(form, root);
                            }
                            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                            finally { form.Close(); }
                        }));
                    };
                    Application.Run(form);
                }
                return result;
            }
            if (args.Length == 2 && args[0] == "--package-check")
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "campaign-hangar-package-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var library = new Library(testRoot);
                    var item = library.Import(args[1], "compatibility-check");
                    Console.WriteLine("PACKAGE PASS: " + item.name + " / " + item.version + " / " + item.slot + " / " + item.files.Count + " files.");
                    Console.WriteLine("Isolated library: " + testRoot + "; no activation or game access.");
                    return 0;
                }
                catch (Exception e) { Console.Error.WriteLine("PACKAGE FAIL: " + e.Message + "\nIsolated root: " + testRoot); return 1; }
            }
            if (args.Length == 2 && args[0] == "--download-check")
            {
                string testRoot = Path.Combine(Path.GetTempPath(), "campaign-hangar-download-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var item = Data.ParseCatalogue(Data.GetText(Data.Site + "/campaigns.js")).Single(c => c.id == args[1]);
                    string output = Path.Combine(testRoot, "release.zip");
                    Data.DownloadZip(item.download, output);
                    Console.WriteLine("DOWNLOAD PASS: " + item.id + " / " + new FileInfo(output).Length + " bytes.");
                    Console.WriteLine("SHA256: " + Data.Hash(output));
                    Console.WriteLine("Downloaded ZIP: " + output + "; not installed.");
                    return 0;
                }
                catch (Exception e) { Console.Error.WriteLine("DOWNLOAD FAIL: " + e.Message + "\nIsolated root: " + testRoot); return 1; }
            }
            if (args.Contains("--catalogue-check"))
            {
                try
                {
                    var campaigns = Data.ParseCatalogue(Data.GetText(Data.Site + "/campaigns.js"));
                    Console.WriteLine("Live catalogue OK: " + campaigns.Count + " entries; read-only GET; no campaign downloads.");
                    return 0;
                }
                catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("--distribution-check") || args.Contains("--distribution-install-check"))
            {
                bool installCheck = args.Contains("--distribution-install-check");
                int result = 1;
                string root = Path.Combine(Path.GetTempPath(), "campaign-drive-check-" + Guid.NewGuid().ToString("N"));
                var library = new Library(Path.Combine(root, "isolated-library"));
                var item = Data.ParseCatalogue(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "catalogue.js"))).Single(c => c.id == "blackout");
                using (var form = new Form { Text = "공개 배포처 격리 검증", Width = 1100, Height = 800 })
                using (var view = new DistributionView(root, root, async (c, file) =>
                {
                    if (!installCheck) throw new InvalidOperationException("Navigation check must not install files.");
                    try
                    {
                        var imported = await System.Threading.Tasks.Task.Run(() => library.ImportForSite(file, c));
                        if (imported.id != c.id || library.Current().active != null) throw new InvalidOperationException("Drive import identity or activation isolation failed.");
                        Console.WriteLine("DRIVE IMPORT PASS: " + imported.id + " / " + imported.name + " / " + imported.version +
                            " / " + new FileInfo(file).Length + " bytes; library=" + library.Root + "; no game activation.");
                        result = 0; form.BeginInvoke(new Action(form.Close));
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); form.BeginInvoke(new Action(form.Close)); throw; }
                }, () => { }, c => { }))
                using (var timer = new System.Windows.Forms.Timer { Interval = installCheck ? 600000 : 45000 })
                {
                    form.Controls.Add(view);
                    timer.Tick += delegate { Console.Error.WriteLine("Drive check timed out."); form.Close(); };
                    form.Shown += async delegate
                    {
                        timer.Start();
                        try { Console.WriteLine("DRIVE NAVIGATION: " + await view.VerifyPageAsync(item)); if (!installCheck) result = 0; }
                        catch (Exception error) { Console.Error.WriteLine(error); form.Close(); }
                        finally { if (!installCheck) { timer.Stop(); form.Close(); } }
                    };
                    Application.Run(form);
                }
                return result;
            }
            if (args.Contains("--archive-check"))
            {
                int result = 1;
                string root = Path.Combine(Path.GetTempPath(), "campaign-archive-check-" + Guid.NewGuid().ToString("N"));
                using (var form = new Form { Text = "아카이브 격리 검증", Width = 1100, Height = 800 })
                using (var view = new ArchiveView(root, true))
                using (var timer = new System.Windows.Forms.Timer { Interval = 45000 })
                {
                    form.Controls.Add(view);
                    timer.Tick += delegate { Console.Error.WriteLine("Archive check timed out."); form.Close(); };
                    form.Shown += async delegate
                    {
                        timer.Start();
                        try { Console.WriteLine("ARCHIVE PASS: " + await view.VerifySiteAsync()); result = 0; }
                        catch (Exception error) { Console.Error.WriteLine(error); }
                        finally { timer.Stop(); form.Close(); }
                    };
                    Application.Run(form);
                }
                return result;
            }
            if (args.Length == 2 && (args[0] == "--design-render" || args[0] == "--design-preview"))
            {
                try { Checks.ExportDesign(args[1], args[0] == "--design-preview"); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Contains("--ui-layout-check"))
            {
                int result = 0;
                using (var form = new Hangar(Path.Combine(Path.GetTempPath(), "campaign-layout-" + Guid.NewGuid().ToString("N")), null, false))
                {
                    form.Shown += delegate
                    {
                        form.BeginInvoke(new Action(async delegate
                        {
                            try
                            {
                                form.VerifyLayout(); form.VerifyLiveUiThemes();
                                Checks.VerifyInstalledUI(); await Checks.VerifyZipDrop(form);
                                await form.VerifyGamePathSwitch();
                            }
                            catch (Exception error) { Console.Error.WriteLine(error); result = 1; }
                            finally { form.Close(); }
                        }));
                    };
                    Application.Run(form);
                }
                return result;
            }
            if (args.Length == 2 && args[0] == "--theme-preview" && AppChannel.IsDevelopment)
            {
                try
                {
                    // Isolated preview: no real campaigns, installed mods,
                    // game processes or saved user preferences are used.
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var form = new Hangar(Path.GetFullPath(args[1]), null, false))
                    {
                        form.Text = "커캠 격납고 [4종 UI 디자인 격리 검증]";
                        Application.Run(form);
                    }
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }
            if (args.Length == 2 && args[0] == "--ui-hub-scroll-check" && AppChannel.HasThemedUi)
            {
                int code = 0;
                string root = Path.GetFullPath(args[1]);
                if (Directory.Exists(root)) throw new InvalidOperationException("Choose an unused isolated UI test path.");
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var form = new Hangar(Path.Combine(root, "storage"), null, false))
                {
                    form.Shown += delegate
                    {
                        form.BeginInvoke(new Action(async delegate
                        {
                            try { await form.VerifyLiveHubScrollAsync(Path.Combine(root, "screenshots")); }
                            catch (Exception error) { Console.Error.WriteLine(error); code = 1; }
                            finally { form.Close(); }
                        }));
                    };
                    Application.Run(form);
                }
                return code;
            }
            bool created;
            if(args.Length==2&&args[0]=="--release-update-demo"){
                string directory=Path.GetFullPath(args[1]);
                int result=1;
                using(var form=new Hangar(Path.Combine(directory,"isolated-profile"),null,false)){
                    form.Shown+=async delegate {
                        try{await form.VerifyReleaseUpdateDemo(directory);result=0;}
                        catch(Exception error){Console.Error.WriteLine(error);}
                        finally{form.Close();}
                    };
                    Application.Run(form);
                }
                return result;
            }
            if (args.Length == 2 && args[0] == "--update-notification-check")
            {
                int result = 1;
                try
                {
                    var candidate = System.Diagnostics.FileVersionInfo.GetVersionInfo(args[1]);
                    if (candidate.ProductName != "CampaignHangar." + AppChannel.Id)
                        throw new InvalidDataException("알림 시험 대상의 개발판·배포판 구분이 다릅니다.");
                    var release = AppUpdate.Parse(Data.Json.Serialize(new AppRelease {
                        version = candidate.FileVersion, channel = AppChannel.Id,
                        url = AppUpdate.PackageUrl(candidate.FileVersion),
                        sha256 = new string('a', 64), size = 1,
                        notes = "격리 알림 시험용 버전입니다. 공개 배포·다운로드·설치를 하지 않습니다."
                    }));
                    string fixture = Path.Combine(Path.GetTempPath(), "hangar-update-notification-" + Guid.NewGuid().ToString("N"));
                    using (var form = new Hangar(fixture, null, false))
                    {
                        form.Shown += async delegate {
                            try { await form.VerifyUpdateNotification(release); result = 0; }
                            catch (Exception error) { Console.Error.WriteLine(error); }
                            finally { form.Close(); }
                        };
                        Application.Run(form);
                    }
                }
                catch (Exception error) { Console.Error.WriteLine(error); }
                return result;
            }
            using (var mutex = new Mutex(true, "Local\\CampaignHangarPreview", out created))
            {
                if (!created) { MessageBox.Show("커캠 격납고가 이미 실행 중입니다.\n게임 파일 보호를 위해 개발판과 배포판도 동시에 실행하지 않습니다."); return 1; }
                bool reload;
                do
                {
                    try
                    {
                        using (var form = new Hangar())
                        {
                            Application.Run(form);
                            reload = form.ReloadRequested;
                        }
                    }
                    catch (UnauthorizedAccessException error)
                    {
                        // Never replace or reset an existing inaccessible library:
                        // the user's installed campaigns may still be inside it.
                        string storagePath = "(저장된 경로를 읽을 수 없음)";
                        try { storagePath = Storage.Resolve(Storage.PreferenceFile); }
                        catch (Exception) { }
                        MessageBox.Show("격납고 저장 폴더에 접근할 수 없습니다.\n\n" +
                            "캠페인 보관소: " + storagePath + "\n\n" +
                            "해당 폴더에 현재 Windows 계정의 읽기·쓰기 권한이 있는지 확인해 주세요. " +
                            "게임이 Program Files에 설치된 경우 보관소와 다운로드는 별도 사용자 폴더에 두는 것이 안전합니다.\n\n" +
                            "기존 보관소와 경로 설정은 변경하지 않았습니다.\n상세: " + error.Message,
                            "보관소·다운로드 폴더 접근 거부", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return 1;
                    }
                } while (reload);
            }
            return 0;
        }
    }
}
