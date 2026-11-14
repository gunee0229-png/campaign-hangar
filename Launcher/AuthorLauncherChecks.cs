using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class AuthorLauncherChecks
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("AUTHOR FAIL: " + message);
            Console.WriteLine("AUTHOR PASS: " + message);
        }
        static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Check(rejected, message);
        }
        public static void Run(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Author launcher checks require a new isolated directory.");
            Directory.CreateDirectory(root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Console.WriteLine("AUTHOR isolated root: " + root);
            string game = Path.Combine(root, "game"), maps = Path.Combine(game, "Maps");
            Directory.CreateDirectory(maps);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            string map = Path.Combine(maps, "시작 맵.SC2Map");
            File.WriteAllBytes(map, new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 });
            Directory.CreateDirectory(Path.Combine(game, "Support64"));
            File.WriteAllText(Path.Combine(game, "Support64", "SC2Switcher_x64.exe"), "fixture-not-executable");
            string launcherFolder = Path.Combine(root, "제작자 런처 폴더");
            Directory.CreateDirectory(launcherFolder);
            string executable = Path.Combine(launcherFolder, "Author Launcher.exe");
            using (var compiler = CodeDomProvider.CreateProvider("CSharp"))
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = executable,
                    TempFiles = new TempFileCollection(root, false) };
                options.ReferencedAssemblies.Add("System.dll");
                var result = compiler.CompileAssemblyFromSource(options,
                    "class Fixture { static void Main(string[] args) { System.IO.File.WriteAllText(\"author-launcher-opened.txt\", args.Length.ToString()); } }");
                Check(!result.Errors.HasErrors, "compile harmless launcher stand-in in test-owned directory");
            }
            string marker = Path.Combine(launcherFolder, "author-launcher-opened.txt");
            string mapHash = Data.Hash(map), executableHash = Data.Hash(executable);
            var external = new ExternalCampaigns(root);
            string database = Path.Combine(root, "external-campaigns.json");
            Data.Write(database, new[] { new { id = "external-legacy", versionKey = "external-legacy", kind = "external",
                slot = "external", name = "기존 시작 맵 · 격리 예시", launcherMap = map, version = "전용 실행" } });
            string oldDatabaseHash = Data.Hash(database);
            var legacy = external.All().Single();
            Check(ExternalCampaigns.InstallationMode(legacy) == "manual" && ExternalCampaigns.LaunchMode(legacy) == "map",
                "old map-only connection defaults to manual installation and map execution");
            Check(ExternalCampaigns.LaunchInfo(legacy, game).Arguments == "\"" + map + "\"" &&
                Data.Hash(database) == oldDatabaseHash, "old connection opens without migration writes or changed launch request");
            var author = external.Save(null, "매스 리콜 SCEvo · 격리 예시", null, null, "",
                "author-launcher", "author-launcher", executable);
            Check(author.launcherMap == null && author.launcherExecutable == executable &&
                author.installationMode == "author-launcher" && author.launchMode == "author-launcher",
                "author installation and execution stored independently of game Maps and SC2Switcher");
            Check(!File.Exists(marker), "registering a launcher never starts it or installs files");
            var start = ExternalCampaigns.LaunchInfo(author, "");
            Check(start.FileName == executable && start.Arguments == "" && start.WorkingDirectory == launcherFolder &&
                !start.UseShellExecute && String.IsNullOrEmpty(start.Verb), "author launch uses exact EXE, its own directory, no shell, arguments or elevation request");
            Reject(() => external.Save(null, "duplicate", null, null, game, "manual", "author-launcher", executable),
                "duplicate author execution target rejected even with another installation mode");
            string beforeReject = Data.Hash(database);
            Reject(() => external.Save(author.id, author.name, null, null, game, "unsupported", "author-launcher", executable),
                "unknown installation mode rejected");
            Reject(() => external.Save(author.id, author.name, null, null, game, "manual", "unsupported", executable),
                "unknown launch mode rejected");
            Reject(() => external.Save(author.id, author.name, null, null, game, "author-launcher", "author-launcher", Path.Combine(root, "missing.exe")),
                "missing executable rejected");
            Reject(() => ExternalCampaigns.ValidateLauncher("https://example.com/launcher.exe"), "URL not accepted as a local executable");
            Reject(() => ExternalCampaigns.ValidateLauncher(@"\\server\share\launcher.exe"), "network executable rejected");
            Reject(() => ExternalCampaigns.ValidateLauncher("Author Launcher.exe"), "relative executable rejected");
            Reject(() => ExternalCampaigns.ValidateLauncher(Path.Combine(root, "launcher.cmd")), "script rejected");
            Reject(() => ExternalCampaigns.ValidateLauncher(Path.Combine(root, "launcher.lnk")), "shortcut rejected");
            string fake = Path.Combine(root, "fake.exe"); File.WriteAllText(fake, "not a PE executable");
            Reject(() => ExternalCampaigns.ValidateLauncher(fake), "renamed non-executable rejected");
            var malformed = new byte[80]; malformed[0] = 77; malformed[1] = 90; malformed[60] = 64;
            File.WriteAllBytes(fake, malformed);
            Reject(() => ExternalCampaigns.ValidateLauncher(fake), "invalid PE signature rejected");
            Check(Data.Hash(database) == beforeReject && !File.Exists(marker), "failed saves do not alter connections or execute a file");

            string combinedMap = Path.Combine(maps, "혼합 시작 맵.SC2Map"); File.Copy(map, combinedMap);
            var combined = external.Save(null, "런처 설치·맵 실행 · 격리 예시", combinedMap, null, game,
                "author-launcher", "map", executable);
            Check(ExternalCampaigns.LaunchInfo(combined, game).FileName.EndsWith("SC2Switcher_x64.exe") &&
                ExternalCampaigns.AuthorLauncherInfo(combined).FileName == executable,
                "launcher installation can coexist with direct map execution and separate installer action");
            File.Move(executable, executable + ".held");
            try
            {
                Check(ExternalCampaigns.LaunchInfo(combined, game).Arguments == "\"" + combinedMap + "\"",
                    "map execution does not require the installer to remain present after installation");
                Reject(() => ExternalCampaigns.AuthorLauncherInfo(combined), "missing installer blocks only the installer action");
            }
            finally { File.Move(executable + ".held", executable); }
            author = external.Save(author.id, author.name, null, null, "", "manual", "author-launcher", executable);
            var reloaded = external.All().Single(c => c.id == author.id);
            Check(reloaded.installationMode == "manual" && reloaded.launchMode == "author-launcher" &&
                reloaded.id == author.id && reloaded.versionKey == author.versionKey, "mode edit persists without changing campaign or play-record identity");
            using (var process = Process.Start(ExternalCampaigns.AuthorLauncherInfo(author)))
                Check(process != null && process.WaitForExit(10000) && process.ExitCode == 0, "harmless local stand-in starts and exits successfully");
            Check(File.ReadAllText(marker) == "0" && Data.Hash(executable) == executableHash && Data.Hash(map) == mapHash,
                "stand-in runs in launcher directory without injected arguments or source changes");
            VerifyCcmCoexistence(root, game, author, legacy);

            var store = new ActivityStore(root);
            store.ObserveTime(author.id, "fixture-process", 0); store.ObserveTime(author.id, "fixture-process", 10); store.Flush();
            VerifyUI(root, external, store, new[] { legacy, combined, author }, game, executable, map);
            string recordsHash = Data.Hash(Path.Combine(root, "play-records.json"));
            string save = Path.Combine(root, "fixture.SC2Save"); File.WriteAllText(save, "preserved-save");
            string bank = Path.Combine(root, "fixture.SC2Bank"); File.WriteAllText(bank, "preserved-bank");
            var library = new Library(Path.Combine(root, "library")); library.RestoreBase();
            Check(Data.Hash(executable) == executableHash && Data.Hash(map) == mapHash &&
                File.ReadAllText(marker) == "0", "base restore does not claim ownership of author launcher or its outputs");
            external.Forget(author.id);
            Check(external.All().Count == 2 && Data.Hash(executable) == executableHash &&
                Data.Hash(Path.Combine(root, "play-records.json")) == recordsHash &&
                File.ReadAllText(save) == "preserved-save" && File.ReadAllText(bank) == "preserved-bank",
                "forget preserves executable, play records, bank and save");
            Console.WriteLine("AUTHOR COMPLETE: isolated stand-in only; no third-party EXE download, install, launch or real game/bank changes.");
        }
        static void VerifyCcmCoexistence(string root, string game, Campaign author, Campaign map)
        {
            string storage = Path.Combine(root, "coexistence-library"), zip = Path.Combine(root, "coexistence.zip");
            string target = Data.Under(game, "Maps/Campaign/mission.SC2Map");
            Directory.CreateDirectory(Path.GetDirectoryName(target)); File.WriteAllText(target, "original-map");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("metadata.txt").Open()))
                    writer.Write("title=fixture\ncampaign=wol\nversion=1.0\nauthor=fixture\n");
                using (var writer = new StreamWriter(archive.CreateEntry("mission.SC2Map").Open())) writer.Write("fixture-map");
            }
            var library = new Library(storage);
            var package = library.Import(zip, "fixture"); library.Activate(package.id, game, true, null);
            File.Copy(author.launcherExecutable, Path.Combine(game, "Support64", "SC2Switcher_x64.exe"), true);
            string stateHash = Data.Hash(Path.Combine(storage, "active.json")), gameHash = Data.Hash(target);
            using (var shell = new Hangar(storage, c => Task.FromResult<Image>(null), false))
                foreach (var connection in new[] { author, map })
                foreach (bool installerAction in connection == author ? new[] { false, true } : new[] { false })
                {
                    Check(shell.VerifyExternalLaunch(connection, game, installerAction).GetAwaiter().GetResult(),
                        "shell opens harmless external launcher or map stand-in with active CCM without a restore prompt");
                    Check(library.Current().active == package.id && Data.Hash(Path.Combine(storage, "active.json")) == stateHash &&
                        Data.Hash(target) == gameHash,
                        "external launcher primary and management actions preserve active CCM bytes and record");
                }
        }
        static void VerifyUI(string root, ExternalCampaigns external, ActivityStore store, Campaign[] campaigns,
            string game, string executable, string map)
        {
            var library = new Library(Path.Combine(root, "ui-library"));
            var primary = new List<string>(); var authorActions = new List<string>();
            using (var image = new Bitmap(320, 180))
            using (var form = new Form { ClientSize = new Size(1440, 940) })
            using (var view = new InstalledView(library, c => Task.FromResult<Image>(image),
                c => { primary.Add(c.id); return Task.FromResult(true); }, () => { },
                external: external.All, addExternal: () => { }, editExternal: c => { }, showRecords: c => { },
                recordSummary: store.Summary, restore: () => { }, openAuthorLauncher: c => authorActions.Add(c.id)))
            {
                form.Controls.Add(view); form.Show();
                foreach (var campaign in campaigns)
                {
                    foreach (int width in new[] { 1440, 980 })
                    {
                        form.ClientSize = new Size(width, width == 980 ? 760 : 940);
                        view.Reveal(new Campaign[0], campaign); Application.DoEvents();
                        view.VerifyActivityFeatures(campaign, false);
                        int primaryBefore = primary.Count, authorBefore = authorActions.Count;
                        view.VerifyAuthorLauncherAction(campaign, false);
                        Check(primary.Count == primaryBefore + 1 && primary.Last() == campaign.id, "primary action selects the displayed campaign");
                        bool usesAuthor = campaign.id != "external-legacy";
                        Check(authorActions.Count == authorBefore + (usesAuthor ? 1 : 0) &&
                            (!usesAuthor || authorActions.Last() == campaign.id), "installer menu selects the displayed connection only");
                        Capture(form, Path.Combine(root, campaign.id + "-" + width + ".png"));
                        view.SetBusy(true); view.VerifyActivityFeatures(campaign, true); view.VerifyAuthorLauncherAction(campaign, true);
                        view.SetBusy(false);
                    }
                }
                string connectionsHash = Data.Hash(Path.Combine(root, "external-campaigns.json"));
                var direct = new[] {
                    new Campaign { id = "sc-evo", name = "매스리콜 SC Evo", kind = "dedicated", version = "26.07.24-ko-voice",
                        dedicatedManifestUrl = "https://github.com/gunee0229-png/sc2-dedicated-campaigns-ko/releases/download/standalone-2026.11.06/sc-evo.json",
                        dedicatedManifestSha256 = "915cbb4f920faaf81413e2e1cb041df178aebd72dc13ba48186f8a017f0b1833", dedicatedInstallerVersion = 2 },
                    new Campaign { id = "antioch-chronicles", name = "안티오크 연대기", kind = "dedicated", version = "5.1-ko",
                        dedicatedManifestUrl = "https://github.com/gunee0229-png/sc2-dedicated-campaigns-ko/releases/download/selective-download-2026.11.06/antioch-chronicles.json",
                        dedicatedManifestSha256 = "3c34b6933dc5b5d748336605f23d1b6f93da0355bf28322145e6462d48af4c04",
                        dedicatedInstallerVersion = 3, dedicatedEpisodeCount = 3 } };
                foreach (int width in new[] { 1440, 980 })
                {
                    form.ClientSize = new Size(width, width == 980 ? 760 : 940);
                    view.VerifyExternalSeparation(direct, campaigns.Last()); Application.DoEvents();
                    Capture(form, Path.Combine(root, "direct-default-" + width + ".png"));
                }
                Check(Data.Hash(Path.Combine(root, "external-campaigns.json")) == connectionsHash,
                    "separating old connections never rewrites their IDs, modes, paths or records");
                form.Close();
            }
            foreach (var campaign in campaigns)
                using (var dialog = new ExternalCampaignDialog(campaign, game))
                {
                    string before = Data.Hash(Path.Combine(root, "external-campaigns.json"));
                    dialog.Show(); Application.DoEvents();
                    dialog.VerifyModes("manual", "map", map, executable, game);
                    dialog.VerifyModes("author-launcher", "map", map, executable, game);
                    dialog.VerifyModes("manual", "author-launcher", map, executable, game);
                    dialog.VerifyModes("author-launcher", "author-launcher", map, executable, game);
                    Capture(dialog, Path.Combine(root, campaign.id + "-dialog-wide.png"));
                    dialog.ClientSize = new Size(640, 530); Application.DoEvents();
                    Capture(dialog, Path.Combine(root, campaign.id + "-dialog-narrow.png"));
                    dialog.DialogResult = DialogResult.Cancel; dialog.Close();
                    Check(Data.Hash(Path.Combine(root, "external-campaigns.json")) == before, "cancelled editor never changes stored connection");
                }
        }
        static void Capture(Form form, string path)
        {
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path); }
        }
    }
}
