using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class ActivityChecks
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("ACTIVITY FAIL: " + message);
            Console.WriteLine("ACTIVITY PASS: " + message);
        }
        static void Reject(Action action, string message)
        {
            bool failed = false;
            try { action(); } catch (Exception) { failed = true; }
            Check(failed, message);
        }
        static string Bank(int instance, string mission, bool success, string completed = "", bool simulation = false)
        {
            return "<Bank version=\"1\"><Section name=\"WCampaign|CampaignLastInfo\">" +
                "<Key name=\"LastMission\"><Value string=\"" + mission + "\"/></Key>" +
                "<Key name=\"LastMissionSuccess\"><Value flag=\"" + (success ? "1" : "0") + "\"/></Key>" +
                "<Key name=\"LastMissionSimulation\"><Value flag=\"" + (simulation ? "1" : "0") + "\"/></Key></Section>" +
                "<Section name=\"WCampaign|CampaignSetting\"><Key name=\"MapInstanceID\"><Value int=\"" + instance +
                "\"/></Key></Section><Section name=\"WCampaign|MissionCompletedAge\">" +
                String.Join("", completed.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(m =>
                    "<Key name=\"" + m + "\"><Value int=\"1\"/></Key>")) + "</Section>" +
                "<Section name=\"WCampaign|MissionBestTime\"><Key name=\"NotCompleted-4\"><Value int=\"10\"/></Key></Section></Bank>";
        }
        static void VerifyRecordRecovery(string root)
        {
            string goodRoot = Path.Combine(root, "record-recovery-valid");
            Directory.CreateDirectory(goodRoot);
            var original = new ActivityStore(goodRoot);
            original.ObserveTime("review-campaign", "review-process", 0);
            original.ObserveTime("review-campaign", "review-process", 70);
            original.Flush();
            string file = Path.Combine(goodRoot, "play-records.json");
            string lastGood = file + ".last-good";
            Check(File.Exists(lastGood) && Data.Hash(lastGood) == Data.Hash(file),
                "successful playtime save retains separate matching last-good snapshot");
            File.WriteAllText(file, "{truncated-json");
            string brokenHash = Data.Hash(file);
            bool asked = false;
            string notice = null;
            var repaired = ActivityStore.OpenForStartup(goodRoot,
                question => { asked = true; return false; }, message => notice = message);
            string[] damaged = Directory.GetFiles(goodRoot, "play-records.damaged-*.json");
            Check(!asked && notice != null && repaired.Snapshot("review-campaign").seconds == 70 &&
                damaged.Length == 1 && Data.Hash(damaged[0]) == brokenHash &&
                Data.Hash(lastGood) == Data.Hash(file),
                "damaged primary is recovered from validated last-good; damaged bytes preserved, no exclusion prompt");

            string declinedRoot = Path.Combine(root, "record-recovery-declined");
            Directory.CreateDirectory(declinedRoot);
            string declinedFile = Path.Combine(declinedRoot, "play-records.json");
            File.WriteAllText(declinedFile, "{truncated-json");
            string declinedHash = Data.Hash(declinedFile);
            bool prompted = false, canceled = false;
            try
            {
                ActivityStore.OpenForStartup(declinedRoot,
                    question => { prompted = question.Contains("기록 없이 시작할까요"); return false; }, message => { });
            }
            catch (ActivityStore.StartupCanceledException) { canceled = true; }
            Check(prompted && canceled && Data.Hash(declinedFile) == declinedHash &&
                Directory.GetFiles(declinedRoot, "play-records.damaged-*.json").Length == 0,
                "declining exclusion exits without changing damaged play records");
            string excludedNotice = null;
            var excluded = ActivityStore.OpenForStartup(declinedRoot, question => true, message => excludedNotice = message);
            damaged = Directory.GetFiles(declinedRoot, "play-records.damaged-*.json");
            Check(!File.Exists(declinedFile) && excludedNotice != null &&
                damaged.Length == 1 && Data.Hash(damaged[0]) == declinedHash &&
                excluded.TotalSeconds == 0,
                "confirmed exclusion preserves damaged original and starts with empty in-memory records");
            excluded.ObserveTime("after-exclusion", "process", 0);
            excluded.ObserveTime("after-exclusion", "process", 90);
            Check(new ActivityStore(declinedRoot).Snapshot("after-exclusion").seconds == 90 &&
                Data.Hash(damaged[0]) == declinedHash, "new playtime saves independently; preserved damaged file unchanged");

            string invalidRoot = Path.Combine(root, "record-recovery-invalid-backup");
            Directory.CreateDirectory(invalidRoot);
            string invalidFile = Path.Combine(invalidRoot, "play-records.json");
            File.WriteAllText(invalidFile, "{truncated-primary");
            File.WriteAllText(invalidFile + ".last-good", "{truncated-backup");
            var originalInvalidHash = Data.Hash(invalidFile);
            bool invalidPrompt = false;
            var started = ActivityStore.OpenForStartup(invalidRoot,
                question => { invalidPrompt = true; return true; }, message => { });
            Check(invalidPrompt && started.TotalSeconds == 0 &&
                Directory.GetFiles(invalidRoot, "play-records.damaged-*.json").Length == 1 &&
                Data.Hash(invalidFile + ".last-good") != originalInvalidHash,
                "unreadable last-good never silently restores; asks before excluding primary");

            string oldRoot = Path.Combine(root, "record-recovery-legacy");
            Directory.CreateDirectory(oldRoot);
            string oldFile = Path.Combine(oldRoot, "play-records.json");
            File.WriteAllText(oldFile, "[{\"id\":\"old-record\",\"seconds\":12,\"missions\":1,\"clears\":0}]");
            var legacy = new ActivityStore(oldRoot).Snapshot("old-record");
            Check(legacy.seconds == 12 && legacy.seenMissions.Count == 0 && legacy.completed.Count == 0,
                "valid older records with absent optional collections are normalized, not treated as corruption");
            Console.WriteLine("ACTIVITY RECOVERY COMPLETE: isolated backup restore, explicit exclude/decline, legacy records.");
        }
        public static void Run(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Activity checks require a new isolated directory.");
            Directory.CreateDirectory(root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Console.WriteLine("ACTIVITY isolated root: " + root);
            VerifyRecordRecovery(root);
            string bank = Path.Combine(root, "RLO8D26WCampaign.SC2Bank");
            File.WriteAllText(bank, Bank(4, "TRaynor01", true, "TRaynor01"));
            string originalBank = Data.Hash(bank);
            var store = new ActivityStore(root);
            store.ObserveTime("mode-a", "process-a", 0);
            store.ObserveTime("mode-a", "process-a", 10);
            store.ObserveTime("mode-b", "process-a", 20);
            store.ObserveTime("mode-b", "process-a", 30);
            store.ObserveTime(null, null, 40);
            store.ObserveTime("mode-a", "process-b", 100);
            store.ObserveTime("mode-a", "process-b", 110);
            store.Flush();
            Check(store.Snapshot("mode-a").seconds == 20 && store.Snapshot("mode-b").seconds == 10,
                "time isolated by campaign and process; stopped and unknown intervals excluded");
            Check(new ActivityStore(root).Snapshot("mode-a").seconds == 20, "play time survives store reload");
            Check(store.Summary("unsupported").Contains("완료 기록 미연결"), "unlinked completion is not shown as a measured zero");
            // Package IDs need not equal the catalogue ID.
            string id = "mod-fixture-revolite";
            store.LinkBank(id, bank);
            Check(store.Snapshot(id).missions == 0 && store.Snapshot(id).clears == 0 && Data.Hash(bank) == originalBank,
                "link establishes read-only baseline without retroactive completion");
            var parsed = BankProgress.Read(bank);
            Check(parsed.completed.SequenceEqual(new[] { "TRaynor01" }), "completed-age format used; best times are not completion counters");
            File.WriteAllText(bank, Bank(5, "TRaynor02", false, "TRaynor01")); store.PollBank(id);
            Check(store.Snapshot(id).missions == 0, "failed mission is not counted");
            File.WriteAllText(bank, Bank(6, "TRaynor02", true, "TRaynor01,TRaynor02")); store.PollBank(id);
            store.PollBank(id);
            Check(store.Snapshot(id).missions == 1, "new success counted exactly once across repeated polls");
            store = new ActivityStore(root); store.PollBank(id);
            Check(store.Snapshot(id).missions == 1, "restart does not duplicate success");
            File.WriteAllText(bank, Bank(7, "TRaynor03", true, "TRaynor01,TRaynor02", true)); store.PollBank(id);
            Check(store.Snapshot(id).missions == 1, "simulation excluded");
            File.WriteAllText(bank, Bank(8, "TValerian03", true, "TRaynor01,TRaynor02,TValerian03")); store.PollBank(id);
            File.WriteAllText(bank, Bank(9, "TValerian03", true, "TRaynor01,TRaynor02,TValerian03")); store.PollBank(id);
            Check(store.Snapshot(id).missions == 2 && store.Snapshot(id).clears == 1, "final mission success counts one clear, not points or serial values");
            Check(store.ClearedCampaigns==1&&store.TotalSeconds==30,
                "lifetime totals count one cleared campaign and sum every tracked campaign");
            File.WriteAllText(bank, Bank(2, "", false)); store.PollBank(id);
            File.WriteAllText(bank, Bank(3, "TRaynor01", true, "TRaynor01")); store.PollBank(id);
            Check(store.Snapshot(id).needsReview && store.Snapshot(id).missions == 2, "reset or old save pauses ambiguous counting");
            store.ConfirmNewRun(id);
            File.WriteAllText(bank, Bank(4, "TRaynor02", true, "TRaynor01,TRaynor02")); store.PollBank(id);
            Check(store.Snapshot(id).missions == 3 && store.Snapshot(id).clears == 1, "confirmed new run preserves totals and resets baseline only");
            string lifetimeRoot=Path.Combine(root,"lifetime");
            Directory.CreateDirectory(lifetimeRoot);
            File.Copy(Path.Combine(root,"play-records.json"),Path.Combine(lifetimeRoot,"play-records.json"));
            var lifetime=new ActivityStore(lifetimeRoot);
            File.WriteAllText(bank,Bank(5,"TValerian03",true,"TRaynor01,TRaynor02,TValerian03"));lifetime.PollBank(id);
            Check(lifetime.Snapshot(id).clears==2&&lifetime.ClearedCampaigns==1,
                "repeat campaign clear never increments unique cleared-campaign total");
            var reloadedLifetime=new ActivityStore(lifetimeRoot);
            Check(reloadedLifetime.TotalSeconds==30&&reloadedLifetime.ClearedCampaigns==1&&
                reloadedLifetime.Snapshot("mode-a").seconds==20,
                "lifetime totals and per-campaign playtime survive absent packages and record reload");
            File.WriteAllText(bank, "<Bank/>"); store.PollBank(id);
            Check(store.Snapshot(id).bankError != null && store.Snapshot(id).missions == 3, "incomplete bank reports error without clearing progress");
            File.WriteAllText(bank, "<!DOCTYPE Bank [<!ENTITY x SYSTEM 'file:///not-readable'>]><Bank>&x;</Bank>");
            Reject(() => BankProgress.Read(bank), "XML external entities prohibited");
            File.WriteAllText(bank, Bank(4, "TRaynor02", true, "TRaynor01,TRaynor02")); store.PollBank(id);
            Check(store.Snapshot(id).bankError == null, "valid bank read recovers after transient malformed write");
            string bankHash = Data.Hash(bank);

            string game = Path.Combine(root, "game"), maps = Path.Combine(game, "Maps");
            Directory.CreateDirectory(maps);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            string map = Path.Combine(maps, "전용 캠페인", "시작 맵.SC2Map");
            Directory.CreateDirectory(Path.GetDirectoryName(map));
            File.WriteAllBytes(map, new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 });
            string mapHash = Data.Hash(map);
            string support = Path.Combine(game, "Support");
            Directory.CreateDirectory(support); File.WriteAllText(Path.Combine(support, "SC2Switcher.exe"), "fixture-not-executable");
            var external = new ExternalCampaigns(root);
            var campaign = external.Save(null, "매스 리콜 · 격리 예시", map, null, game);
            Check(external.All().Single().launcherMap == map && campaign.slot == "external", "dedicated campaign registration stores map connection only");
            var start = ExternalCampaigns.LaunchInfo(campaign, game);
            Check(start.Arguments == "\"" + map + "\"" && !start.UseShellExecute && start.WorkingDirectory == game &&
                start.FileName == Path.Combine(support, "SC2Switcher.exe"), "drag-launch equivalent quotes map path and uses verified fallback without shell");
            support = Path.Combine(game, "Support64");
            Directory.CreateDirectory(support); File.WriteAllText(Path.Combine(support, "SC2Switcher_x64.exe"), "fixture-not-executable");
            Check(ExternalCampaigns.LaunchInfo(campaign, game).FileName == Path.Combine(support, "SC2Switcher_x64.exe"),
                "64-bit switcher preferred when installed; no process started");
            Reject(() => external.Save(null, "duplicate", map, null, game), "duplicate map registration rejected");
            Reject(() => ExternalCampaigns.ValidateMap(Path.Combine(root, "outside.SC2Map"), game), "map outside game Maps rejected");
            Reject(() => ExternalCampaigns.ValidateMap(Path.Combine(maps, "..", "outside.SC2Map"), game), "map traversal rejected");
            string invalid = Path.Combine(maps, "invalid.SC2Map"); File.WriteAllText(invalid, "not-mpq");
            Reject(() => ExternalCampaigns.ValidateMap(invalid, game), "invalid map format rejected");
            string cover = Path.Combine(root, "cover.png");
            using (var image = new Bitmap(320, 180)) image.Save(cover);
            campaign = external.Save(campaign.id, campaign.name, map, cover, game);
            Check(!Path.IsPathRooted(campaign.thumbnail) && File.Exists(Data.Under(root, campaign.thumbnail)),
                "cover copied into portable storage without changing input");
            campaign = external.Save(campaign.id, "변경한 캠페인 · 격리 예시", map, campaign.thumbnail, game);
            Check(external.All().Count == 1 && campaign.name.StartsWith("변경한"), "edit retains dedicated campaign identity");
            var library = new Library(Path.Combine(root, "library"));
            string zip = Path.Combine(root, "fixture.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("metadata.txt").Open()))
                    writer.Write("title=fixture\ncampaign=wol\nversion=1.0\nauthor=fixture\n");
                using (var writer = new StreamWriter(archive.CreateEntry("mission.SC2Map").Open())) writer.Write("fixture-map");
                using (var writer = new StreamWriter(archive.CreateEntry("Shared.SC2Mod").Open())) writer.Write("fixture-mod");
            }
            string mission = Data.Under(game, @"Maps\Campaign\mission.SC2Map");
            Directory.CreateDirectory(Path.GetDirectoryName(mission)); File.WriteAllText(mission, "original-map");
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            File.WriteAllText(Path.Combine(game, "Mods", "Shared.SC2Mod"), "original-mod");
            string save = Path.Combine(root, "fixture.SC2Save"); File.WriteAllText(save, "preserved-save");
            string zipHash = Data.Hash(zip);
            var package = library.Import(zip, "fixture");
            library.Activate(package.id, game, true, null);
            VerifyRunningProtection(root, game, library, package);
            File.WriteAllText(mission, "external-edit");
            Reject(() => library.RestoreBase(), "base restore does not bypass external-change protection");
            Check(File.ReadAllText(mission) == "external-edit" && library.Current().active == package.id,
                "rejected restore leaves external edit and active state intact");
            File.WriteAllText(mission, "fixture-map");
            int returning = 0;
            Reject(() => library.RestoreBase(p => {
                if (p.stage == "이전 캠페인 보관소로 반환 중" && ++returning == 2) throw new IOException("fixture-restore-failure");
            }), "injected partial restore failure reported");
            Check(library.Current().active == package.id && File.ReadAllText(mission) == "fixture-map" &&
                File.ReadAllText(Path.Combine(game, "Mods", "Shared.SC2Mod")) == "fixture-mod",
                "partial restore failure rolls back both game files and active state");
            library.RestoreBase();
            Check(library.Current().active == null && File.ReadAllText(mission) == "original-map" &&
                File.ReadAllText(Path.Combine(game, "Mods", "Shared.SC2Mod")) == "original-mod", "base restore recovers original map and shared mod");
            Check(library.Installed(package.id) != null && library.Versions(package.id).Count == 2 &&
                Data.Hash(zip) == zipHash && Data.Hash(bank) == bankHash && Data.Hash(map) == mapHash &&
                File.ReadAllText(save) == "preserved-save", "restore preserves stored version, ZIP, banks, save and dedicated map");
            library.RestoreBase();
            Check(library.Current().active == null, "already restored operation is safe");
            VerifyUI(root, library, external, store, campaign, game);
            external.Forget(campaign.id);
            Check(external.All().Count == 0 && Data.Hash(map) == mapHash && Data.Hash(bank) == bankHash &&
                store.Snapshot(id).missions == 3, "forget removes connection only; map, bank and play records preserved");
            store.ObserveTime(campaign.id,"external-record",0);store.ObserveTime(campaign.id,"external-record",12);store.Flush();
            var reconnected=external.Save(null,campaign.name,map,null,game);
            Check(reconnected.id==campaign.id&&new ActivityStore(root).Snapshot(reconnected.id).seconds==12,
                "removed external campaign reconnects to its retained identity and playtime");
            Console.WriteLine("ACTIVITY COMPLETE: isolated files and UI only; no game or real bank writes or game execution.");
        }
        static void VerifyUI(string root, Library library, ExternalCampaigns external, ActivityStore store, Campaign campaign, string game)
        {
            using (var image = new Bitmap(320, 180))
            using (var form = new Form { ClientSize = new Size(1440, 940) })
            using (var view = new InstalledView(library, c => Task.FromResult<Image>(image), c => Task.FromResult(true), () => { },
                external: external.All, addExternal: () => { }, editExternal: c => { }, showRecords: c => { },
                recordSummary: store.Summary, restore: () => { }))
            {
                form.Controls.Add(view); view.Reveal(new Campaign[0], campaign); form.Show(); Application.DoEvents();
                view.VerifyActivityFeatures(campaign, false);
                Capture(form, Path.Combine(root, "dedicated-wide.png"));
                form.ClientSize = new Size(980, 760); Application.DoEvents();
                view.VerifyActivityFeatures(campaign, false);
                Capture(form, Path.Combine(root, "dedicated-narrow.png"));
                view.SetBusy(true); view.VerifyActivityFeatures(campaign, true); view.SetBusy(false);
                form.Close();
            }
            using (var dialog = new ExternalCampaignDialog(campaign, game))
            { dialog.Show(); Application.DoEvents(); Capture(dialog, Path.Combine(root, "dedicated-edit.png")); dialog.Close(); }
            using (var dialog = new RecordsDialog(store, new Campaign { id = "mod-fixture-revolite", name = "레볼라이트 · 격리 예시" }, root, true))
            { dialog.Show(); Application.DoEvents(); Capture(dialog, Path.Combine(root, "records.png")); dialog.Close(); }
            using (var dialog = new RecordsDialog(store, campaign, root))
            { dialog.Show(); Application.DoEvents(); Capture(dialog, Path.Combine(root, "records-unsupported.png")); dialog.Close(); }
        }
        static void Capture(Form form, string path)
        {
            using (var full = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(full, new Rectangle(Point.Empty, full.Size));
                full.Save(path);
            }
        }
        static void VerifyRunningProtection(string root, string game, Library library, Campaign package)
        {
            string updateZip = Path.Combine(root, "running-update.zip");
            using (var archive = ZipFile.Open(updateZip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("metadata.txt").Open()))
                    writer.Write("title=fixture\ncampaign=wol\nversion=2.0\nauthor=fixture\n");
                using (var writer = new StreamWriter(archive.CreateEntry("mission.SC2Map").Open())) writer.Write("fixture-map-updated");
                using (var writer = new StreamWriter(archive.CreateEntry("Shared.SC2Mod").Open())) writer.Write("fixture-mod-updated");
            }
            var update = library.Import(updateZip, package.id);
            string name = "HangarFixture" + Guid.NewGuid().ToString("N");
            string executable = Path.Combine(game, "SC2_x64.exe");
            using (var compiler = System.CodeDom.Compiler.CodeDomProvider.CreateProvider("CSharp"))
            {
                var options = new System.CodeDom.Compiler.CompilerParameters {
                    GenerateExecutable = true, OutputAssembly = executable,
                    TempFiles = new System.CodeDom.Compiler.TempFileCollection(root, false) };
                options.ReferencedAssemblies.Add("System.dll");
                var result = compiler.CompileAssemblyFromSource(options,
                    "class Fixture { static void Main(string[] args) { System.Threading.EventWaitHandle.OpenExisting(args[0]).Set(); System.Console.ReadLine(); } }");
                Check(!result.Errors.HasErrors, "compile isolated process stand-in, not a game executable");
            }
            using (var ready = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset, name))
            using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(executable, name) {
                UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true }))
            {
                try
                {
                    Check(ready.WaitOne(10000), "isolated process stand-in ready");
                    Check(ExternalCampaigns.GameProcess(game) != null, "running process matched by exact installed root");
                    Check(ExternalCampaigns.GameProcess(Path.Combine(root, "other-game")) == null, "other install process not attributed to this game");
                    Reject(() => ExternalCampaigns.RequireGameClosed(game), "dedicated launch gate blocks running game");
                    Reject(() => library.RestoreBase(), "base restore blocked while game process runs");
                    library.Activate(package.id, game, true, null, update.versionKey);
                    Check(!process.HasExited && library.Current().versionKey == update.versionKey &&
                        File.ReadAllText(Data.Under(game, @"Maps\Campaign\mission.SC2Map")) == "fixture-map-updated",
                        "CCM update applies while the isolated game process remains running");
                    library.Activate(package.id, game, true, null, package.versionKey);
                    Check(!process.HasExited && library.Current().versionKey == package.versionKey,
                        "CCM version switching also works without terminating the running process");
                    string stateHash = Data.Hash(Path.Combine(library.Root, "active.json"));
                    using (var locked = new FileStream(Data.Under(game, @"Mods\Shared.SC2Mod"), FileMode.Open, FileAccess.Read, FileShare.None))
                        Reject(() => library.Activate(package.id, game, true, null, update.versionKey),
                            "a locked game file still blocks CCM updates while the game process runs");
                    Check(Data.Hash(Path.Combine(library.Root, "active.json")) == stateHash &&
                        File.ReadAllText(Data.Under(game, @"Maps\Campaign\mission.SC2Map")) == "fixture-map",
                        "lock failure preserves the active CCM version and game bytes");
                    string mission = Data.Under(game, @"Maps\Campaign\mission.SC2Map");
                    File.WriteAllText(mission, "external-running-edit");
                    Reject(() => library.Activate(package.id, game, true, null, update.versionKey),
                        "external edits remain protected during running-game CCM updates");
                    Check(File.ReadAllText(mission) == "external-running-edit" &&
                        Data.Hash(Path.Combine(library.Root, "active.json")) == stateHash,
                        "rejected running-game update preserves the user edit and active record");
                    File.WriteAllText(mission, "fixture-map");
                }
                finally { process.StandardInput.Close(); }
                Check(process.WaitForExit(10000), "isolated process stand-in exits after input closes");
            }
        }
    }
}
