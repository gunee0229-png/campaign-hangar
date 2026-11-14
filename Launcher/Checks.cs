using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CampaignHangar
{
    static class Checks
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct FileIdentity { public ulong volume, low, high; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetFileInformationByHandleEx(Microsoft.Win32.SafeHandles.SafeFileHandle file, int informationClass,
            out FileIdentity identity, uint size);
        static FileIdentity Identity(string path)
        {
            using (var file = File.OpenRead(path))
            {
                FileIdentity value;
                if (!GetFileInformationByHandleEx(file.SafeFileHandle, 18, out value, 24))
                    throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
                return value;
            }
        }
        static int passed;
        static void Assert(bool value, string name)
        {
            if (!value) throw new Exception("FAIL: " + name);
            passed++; Console.WriteLine("PASS: " + name);
        }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Assert(rejected, name);
        }
        public static void VerifyExternalLinkUI(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("External link UI check requires a new isolated directory.");
            Directory.CreateDirectory(root);
            string game = Path.Combine(root, "fixture-game");
            Directory.CreateDirectory(Path.Combine(game, "Maps", "Campaign"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "not executable");
            string map = Path.Combine(game, "Maps", "Campaign", "TestStart.SC2Map");
            File.WriteAllBytes(map, new byte[] { (byte)'M', (byte)'P', (byte)'Q', 26, 0, 1, 2, 3 });
            string launcher = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CampaignHangar.exe");
            if (!File.Exists(launcher)) throw new FileNotFoundException("The isolated check needs its own known PE executable.", launcher);
            using (var dialog = new ExternalCampaignDialog(null, game))
            {
                dialog.Show(); System.Windows.Forms.Application.DoEvents();
                dialog.VerifyModes("manual", "map", map, launcher, game);
                dialog.VerifyModes("author-launcher", "map", map, launcher, game);
                dialog.VerifyModes("manual", "author-launcher", map, launcher, game);
                dialog.VerifyModes("author-launcher", "author-launcher", map, launcher, game);
                dialog.VerifyEpisodeConnections(new[] { map, null, null }, game);
                dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel; dialog.Close();
            }
            if (Directory.GetFiles(root, "external-campaigns.json", SearchOption.AllDirectories).Length > 0)
                throw new InvalidOperationException("UI inspection unexpectedly persisted a connection.");
            Console.WriteLine("EXTERNAL LINK UI PASS: only start map and launcher are primary; filename display retains real paths; episodes remain expandable; cancellation saves nothing.");
        }
        public static void VerifyZipArtwork(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("ZIP artwork fixture must use a fresh directory.");
            Directory.CreateDirectory(root);
            string zip = Path.Combine(root, "artwork-example.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Package/Maps/metadata.txt").Open()))
                    writer.Write("title=Artwork fixture\ncampaign=wol\nversion=1.0\n");
                using (var writer = new StreamWriter(archive.CreateEntry("Package/Maps/Campaign/fixture.SC2Map").Open()))
                    writer.Write("fixture-map");
                using (var art = new System.Drawing.Bitmap(480, 270))
                using (var stream = archive.CreateEntry("Package/hangar/thumbnail.png").Open())
                    art.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                using (var writer = new StreamWriter(archive.CreateEntry("Package/hangar/credits.json").Open()))
                    writer.Write("{\"sourceAuthor\":\"Original author\",\"translator\":\"Korean translator\"," +
                        "\"creatorUrl\":\"https://example.com/original\"," +
                        "\"translatorUrl\":\"https://example.com/translation\"," +
                        "\"creatorLabel\":\"원작자 사이트\",\"translatorLabel\":\"한글패치 안내\"}");
            }
            var library = new Library(Path.Combine(root, "library"));
            var item = library.Import(zip, "artwork-example");
            string image = Data.Under(library.Root, item.thumbnail);
            Assert(item.sourceAuthor == "Original author" && item.translator == "Korean translator" &&
                item.creatorUrl == "https://example.com/original" &&
                item.translatorUrl == "https://example.com/translation" &&
                File.Exists(image) && item.thumbnail.Contains("thumbnail.png"),
                "translation ZIP automatically registers packaged artwork and verified credits");
            using (var loaded = System.Drawing.Image.FromFile(image))
                Assert(loaded.Width == 480 && loaded.Height == 270,
                    "ZIP image remains decodable and has the original dimensions");
            Assert(item.files.All(file => !file.path.Contains("hangar") && !file.path.Contains("thumbnail.png") &&
                !file.path.Contains("credits.json")), "artwork and credits are never deployed to SC2 game folders");
            Assert(File.Exists(zip), "original translation ZIP remains unchanged");
            Console.WriteLine("ZIP ARTWORK COMPLETE: source, creator and translator metadata auto-linked in isolated storage; game untouched.");
        }
        static string Zip(string root, string name, string contents, bool escape, string version = "1.0", string slot = "wol",
            string title = null, string author = null)
        {
            string path = Path.Combine(root, name + ".zip");
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("wrapper/metadata.txt").Open()))
                    writer.Write("title=" + (title ?? name) + "\ncampaign=" + slot + "\nversion=" + version +
                        "\nauthor=" + (author ?? "") + "\ndesc=contains=equals\n");
                using (var writer = new StreamWriter(archive.CreateEntry(escape ? "../escaped.txt" : "wrapper/mission.SC2Map").Open()))
                    writer.Write(contents);
                if (!escape)
                    using (var writer = new StreamWriter(archive.CreateEntry("wrapper/Shared.SC2Mod").Open())) writer.Write(contents + "-dependency");
            }
            return path;
        }
        public static async System.Threading.Tasks.Task VerifyInstallAutoApply(Hangar form, string root)
        {
            var local = Path.Combine(root, "fixture-game");
            Directory.CreateDirectory(local);
            File.WriteAllText(Path.Combine(local, "StarCraft II.exe"), "fixture only");
            string zip = Zip(root, "auto-apply", "mock mission", false);
            await form.VerifyAutoApplyAfterImport(local, zip);
            var records = new[] {
                new Campaign { id = "personal-dev-test", kind = "external", developmentOnly = true },
                new Campaign { id = "published-connection", kind = "external", developmentOnly = false }
            };
            var visibleDevelopment = ExternalCampaigns.VisibleForChannel(records, true).ToList();
            var visibleRelease = ExternalCampaigns.VisibleForChannel(records, false).ToList();
            if (visibleDevelopment.Count != 2 || visibleRelease.Count != 1 ||
                visibleRelease[0].id != "published-connection")
                throw new InvalidOperationException("Release must hide development-only external connections.");
            Console.WriteLine("CHANNEL PASS: test-only nonCCM links remain visible in development and absent in release.");
        }
        public static void VerifyCcmPayload(string root,string sourceZip=null)
        {
            if(Directory.Exists(root))throw new InvalidOperationException("Choose a new isolated payload check directory.");
            Directory.CreateDirectory(root);
            string marker=Path.Combine(root,"must-not-execute.txt"),zip=Path.Combine(root,"fixture.zip");
            var payload=new Dictionary<string,string>();
            foreach(var extension in new[]{"exe","dll","bat","cmd","ps1","lnk","com","scr","msi","futureformat"})
                payload["helper."+extension]="fixture bytes; never execute";
            payload["helper.ps1"]="Set-Content -LiteralPath '"+marker.Replace("'","''")+"' -Value 'executed'";
            using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))
            {
                using(var writer=new StreamWriter(archive.CreateEntry("wrapper/metadata.txt").Open()))
                    writer.Write("title=CCM payload fixture\ncampaign=WoL\nversion=1.0\n");
                using(var writer=new StreamWriter(archive.CreateEntry("wrapper/mission.SC2Map").Open()))
                    writer.Write("isolated map bytes");
                foreach(var pair in payload)
                    using(var writer=new StreamWriter(archive.CreateEntry("wrapper/"+pair.Key).Open()))
                        writer.Write(pair.Value);
            }
            var library=new Library(Path.Combine(root,"library"));
            var item=library.Import(zip,"payload-check");
            string game=Path.Combine(root,"fake-game");
            Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,"StarCraft II.exe"),"fixture only");
            library.Activate(item.id,game,true,null);
            foreach(var pair in payload)
            {
                string target=Data.Under(game,@"Maps\Campaign\"+pair.Key);
                Assert(File.ReadAllText(target)==pair.Value,"opaque CCM payload copied unchanged: "+pair.Key);
            }
            Assert(!File.Exists(marker),"CCM import and activation never execute packaged scripts");
            library.RestoreBase();
            Assert(payload.Keys.All(name=>!File.Exists(Data.Under(game,@"Maps\Campaign\"+name))),
                "base restore removes only installed payload files");
            string traversal=Zip(root,"traversal","bad",true);
            Reject(()=>library.Import(traversal,"payload-traversal"),"path traversal remains rejected");
            Assert(!File.Exists(Path.Combine(root,"escaped.txt")),"rejected archive cannot escape its stage");
            if(sourceZip!=null)
            {
                string originalHash=Data.Hash(sourceZip);
                var actual=library.Import(sourceZip,"actual-ccm-package");
                var linked=new Campaign {id="auto-post-6798",name="자유의 사령관",slot="wol",kind="campaign",@base="자유의 날개"};
                Assert(SiteMatcher.Candidates(actual,new[]{linked}).Any(c=>c.id==linked.id),
                    "Co-op Like metadata recommends the verified Freedom Commanders campaign");
                using(var archive=ZipFile.OpenRead(sourceZip))
                {
                    string metadata=archive.Entries.Single(e=>Path.GetFileName(e.FullName)=="metadata.txt").FullName;
                    string parent=metadata.Substring(0,metadata.LastIndexOf('/')+1);
                    foreach(var entry in archive.Entries.Where(e=>e.FullName.StartsWith(parent,StringComparison.Ordinal)&&
                        new[]{".cmd",".ps1"}.Contains(Path.GetExtension(e.FullName),StringComparer.OrdinalIgnoreCase)))
                    {
                        string stored=Data.Under(library.VersionFolder(actual),"payload/"+entry.FullName);
                        using(var stream=entry.Open())
                        using(var hash=System.Security.Cryptography.SHA256.Create())
                            Assert(Data.Hash(stored)==BitConverter.ToString(hash.ComputeHash(stream)).Replace("-","").ToLowerInvariant(),
                                "original helper preserved in isolated store: "+entry.FullName);
                    }
                }
                Assert(Data.Hash(sourceZip)==originalHash,"source ZIP unchanged after isolated import");
                Console.WriteLine("REAL CCM IMPORT PASS: "+actual.name+" | "+actual.files.Count+" deployment files | isolated storage only");
            }
            Console.WriteLine("CCM PAYLOAD PASS: arbitrary file types stored/applied as data; no real game or bank access, no packaged code execution.");
        }
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "campaign-hangar-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Console.WriteLine("Isolated test root: " + root);
            try
            {
                VerifyCcmPayload(Path.Combine(root,"ccm-payload"));
                string lookPath=Path.Combine(root,"work-browse-preference.json");
                var look=new UiLookStore(lookPath);
                Assert(!look.Value.browseWhileWorking,
                    "fresh install defaults to fullscreen work and theme loading");
                // Keep the user's selected work-only presentation independent
                // of startup and race-switch loading screens.
                look.Value.theme="zerg";
                look.Value.highContrast=true;
                look.Value.reducedMotion=true;
                look.Value.browseWhileWorking=true;look.Save();
                var restoredLook=new UiLookStore(lookPath);
                Assert(restoredLook.Value.browseWhileWorking &&
                    restoredLook.Value.theme=="zerg" && restoredLook.Value.highContrast &&
                    restoredLook.Value.reducedMotion,
                    "saved work presentation and other UI preferences survive restart");
                var publicTitles = new[] {
                    new Campaign { id = "antioch-chronicles", name = "안티오크 연대기", kind = "dedicated" },
                    new Campaign { id = "sc-evo", name = "매스리콜 · SC Evo 적용판", kind = "dedicated" },
                    new Campaign { id = "another", name = "별도 작품", kind = "dedicated" }
                };
                var personalLinks = new[] {
                    new Campaign { id = "linked-antioch", name = "안티오크 연대기", kind = "external" },
                    new Campaign { id = "linked-evo", name = "매스리콜 SCEvo", kind = "external" }
                };
                Assert(InstalledSelection.UniqueDedicated(publicTitles, personalLinks).Select(c => c.id)
                    .SequenceEqual(new[] { "another" }) &&
                    InstalledSelection.UniqueDedicated(publicTitles, new Campaign[0]).Count() == 3 &&
                    InstalledSelection.DisplayName(publicTitles[1]) == "매스리콜 SC Evo",
                    "public and personal links are displayed once without deleting either saved record");
                string documents = Path.Combine(root, "fixture-documents");
                Assert(Storage.BankDirectories(documents).Length == 0 && !Directory.Exists(documents),
                    "bank lookup does not create missing document or bank folders");
                string bankA = Path.Combine(documents, @"StarCraft II\Accounts\account-a\profile-a\Banks");
                string bankB = Path.Combine(documents, @"StarCraft II\Accounts\account-b\profile-b\Banks");
                Directory.CreateDirectory(bankA); Directory.CreateDirectory(bankB);
                Directory.CreateDirectory(Path.Combine(documents, @"StarCraft II\Accounts\account-a\profile-without-banks"));
                string bankFile = Path.Combine(bankA, "fixture.SC2Bank");
                File.WriteAllText(bankFile, "untouched-bank");
                Assert(Storage.BankDirectories(documents).SequenceEqual(new[] { bankA, bankB }),
                    "bank lookup finds all account profiles including empty Banks folders in stable order");
                Assert(File.ReadAllText(bankFile) == "untouched-bank" && Directory.GetFiles(bankB).Length == 0,
                    "bank lookup preserves bank contents and empty folders");
                Reject(() => Storage.BankDirectories("relative-documents"), "bank lookup rejects relative document paths");
                VerifyNotifications(root);
                Assert(Storage.PreferenceFile == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CampaignHangar", "storage.json"),
                    "development and release share existing storage preferences without moving campaigns");
                var lib = new Library(Path.Combine(root, "library"));
                string game = Path.Combine(root, "fixture-game");
                Directory.CreateDirectory(game);
                File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-only-not-executable");
                string mission = Data.Under(game, @"Maps\Campaign\mission.SC2Map");
                string shared = Data.Under(game, @"Mods\Shared.SC2Mod");
                Directory.CreateDirectory(Path.GetDirectoryName(mission));
                Directory.CreateDirectory(Path.GetDirectoryName(shared));
                File.WriteAllText(mission, "original");
                File.WriteAllText(shared, "original-dependency");
                string unrelated = Data.Under(game, @"Maps\Campaign\user.SC2Map");
                File.WriteAllText(unrelated, "user-edit");
                var a = lib.Import(Zip(root, "A", "A", false), "campaign-a");
                var b = lib.Import(Zip(root, "B", "B", false), "campaign-b");
                Assert(a.summary == "contains=equals", "metadata preserves equals in value");
                Assert(a.slot == "wol" && b.files.Count == 3, "CCM layout and dependency mapping");
                Assert(File.ReadAllText(mission) == "original", "import never touches game");
                Assert(File.Exists(Path.Combine(root, "A.zip")), "source ZIP preserved");
                string reconnectPreference = Path.Combine(root, "reinstall-profile", "storage.json");
                string reconnectSource = Path.Combine(root, "reinstall-empty-storage");
                var emptyLibrary = new Library(reconnectSource);
                var snapshot = Directory.GetFiles(lib.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, Data.Hash);
                Assert(Storage.ConnectExisting(reconnectSource, lib.Root, reconnectPreference) == 2 &&
                    Storage.Resolve(reconnectPreference) == lib.Root && new Library(Storage.Resolve(reconnectPreference)).All().Count == 2,
                    "reinstalled app reconnects to an existing storage and recognizes campaigns");
                Assert(snapshot.All(pair => File.Exists(pair.Key) && Data.Hash(pair.Key) == pair.Value) &&
                    emptyLibrary.All().Count == 0, "existing storage connection never moves, merges or modifies campaign files");
                string beforeConnection = File.ReadAllText(reconnectPreference);
                Reject(() => Storage.ConnectExisting(reconnectSource, Path.Combine(root, "not-a-library"), reconnectPreference),
                    "invalid existing storage is rejected");
                Assert(File.ReadAllText(reconnectPreference) == beforeConnection, "failed reconnect preserves the selected storage");
                foreach (bool isUpdate in new[] { false, true })
                {
                    var rollbackLibrary = new Library(Path.Combine(root, "import-rollback-" + isUpdate));
                    var previous = rollbackLibrary.Import(Zip(root, "Rollback-base-" + isUpdate, "base", false), "existing");
                    string candidateId = isUpdate ? "existing" : "new";
                    string candidateZip = Zip(root, "Rollback-candidate-" + isUpdate, "candidate", false, "2.0");
                    string candidateHash = Data.Hash(candidateZip);
                    string index = Path.Combine(rollbackLibrary.Root, "source-zips.json");
                    string indexBefore = File.ReadAllText(index);
                    string manifestFile = Path.Combine(rollbackLibrary.Root, "packages", "existing", "manifest.json");
                    byte[] manifestBefore = File.ReadAllBytes(manifestFile);
                    using (File.Open(index, FileMode.Open, FileAccess.Read, FileShare.Read))
                        Reject(() => rollbackLibrary.Import(candidateZip, candidateId),
                            "late ZIP history write failure is reported for " + (isUpdate ? "update" : "new import"));
                    Assert((isUpdate ? rollbackLibrary.Installed(candidateId).versionKey == previous.versionKey :
                        rollbackLibrary.Installed(candidateId) == null) &&
                        File.ReadAllBytes(manifestFile).SequenceEqual(manifestBefore) &&
                        File.ReadAllText(index) == indexBefore,
                        "late import failure restores exact previous manifest and ZIP history");
                    Assert(!Directory.Exists(Path.Combine(rollbackLibrary.Root, "packages", candidateId, "versions", candidateHash)) &&
                        !Directory.GetDirectories(rollbackLibrary.Root, "import-*").Any() &&
                        Data.Hash(candidateZip) == candidateHash,
                        "late import failure removes only new payload and preserves original ZIP");
                    var retried = rollbackLibrary.Import(candidateZip, candidateId);
                    Assert(retried.versionKey == candidateHash &&
                        rollbackLibrary.Installed(candidateId).versionKey == candidateHash &&
                        rollbackLibrary.Versions(candidateId).Count == (isUpdate ? 2 : 1),
                        "same ZIP retries normally after late failure without losing older versions");
                }
                string structuredZip = Path.Combine(root, "sibling-mods.zip");
                using (var archive = ZipFile.Open(structuredZip, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Maps/metadata.txt").Open())) writer.Write("title=Sibling mods\ncampaign=WoL\nversion=1.0");
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Maps/mission.SC2Map").Open())) writer.Write("mission");
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Mods/Shared.SC2Mod").Open())) writer.Write("dependency");
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Mods/Loose.SC2Mod/Base.SC2Data/GameData.xml").Open())) writer.Write("data");
                }
                var structured = lib.Import(structuredZip, "sibling-mods");
                Assert(structured.files.Count == 4 && structured.files.Any(f => f.path == @"Mods\Shared.SC2Mod") &&
                    structured.files.Any(f => f.path == @"Mods\Loose.SC2Mod\Base.SC2Data\GameData.xml") &&
                    structured.files.Any(f => f.path == @"Maps\Campaign\mission.SC2Map"),
                    "Maps metadata with sibling Mods supports packed and unpacked dependencies in correct game paths");
                string noisyZip = Path.Combine(root, "eskr-style-notes.zip");
                using (var archive = ZipFile.Open(noisyZip, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("metadata.txt").Open()))
                        writer.Write("title=ESKR-style notes\ncampaign=HotS\nversion=1.0");
                    using (var writer = new StreamWriter(archive.CreateEntry("mission.SC2Map").Open()))
                        writer.Write("safe mission");
                    using (var writer = new StreamWriter(archive.CreateEntry("Shared.SC2Mod").Open()))
                        writer.Write("safe dependency");
                    using (var writer = new StreamWriter(archive.CreateEntry("_notes/history/developer.txt").Open()))
                        writer.Write("development notes");
                    using (var writer = new StreamWriter(archive.CreateEntry("CHANGELOG.md").Open()))
                        writer.Write("documentation");
                    using (var writer = new StreamWriter(archive.CreateEntry("AUDIT.tsv").Open()))
                        writer.Write("audit");
                    using (var writer = new StreamWriter(archive.CreateEntry("tools/check_eskr.js").Open()))
                        writer.Write("development tool");
                    using (var writer = new StreamWriter(archive.CreateEntry("Runtime.SC2Mod/custom.md").Open()))
                        writer.Write("runtime container asset");
                }
                var noisyLibrary = new Library(Path.Combine(root, "eskr-style-library"));
                var noisy = noisyLibrary.Import(noisyZip, "eskr-style");
                Assert(noisy.files.Count == 8 && Library.DeploymentFiles(noisy.files).Count == 4,
                    "development-only sidecars are preserved in registered ZIP but excluded from deployment");
                string noisyGame = Path.Combine(root, "eskr-style-game");
                Directory.CreateDirectory(noisyGame);
                File.WriteAllText(Path.Combine(noisyGame, "StarCraft II.exe"), "fixture only");
                noisyLibrary.Activate(noisy.id, noisyGame, true, null);
                Assert(File.Exists(Path.Combine(noisyGame, @"Maps\Campaign\swarm\mission.SC2Map")) &&
                    File.Exists(Path.Combine(noisyGame, @"Mods\Runtime.SC2Mod\custom.md")) &&
                    File.Exists(Path.Combine(noisyGame, @"Mods\Shared.SC2Mod")) &&
                    !Directory.Exists(Path.Combine(noisyGame, @"Maps\Campaign\swarm\_notes")) &&
                    !File.Exists(Path.Combine(noisyGame, @"Maps\Campaign\swarm\CHANGELOG.md")) &&
                    noisyLibrary.Current().files.Count == 4, "ESKR-style activation excludes development notes and preserves runtime containers");
                noisyLibrary.RestoreBase();
                Assert(!File.Exists(Path.Combine(noisyGame, @"Maps\Campaign\swarm\mission.SC2Map")) &&
                    !Directory.Exists(Path.Combine(noisyGame, @"Mods\Runtime.SC2Mod")) &&
                    File.Exists(noisyZip), "ESKR-style restore preserves source ZIP and removes test deployments");
                using (var archive = ZipFile.Open(structuredZip, ZipArchiveMode.Update))
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Other/unknown.SC2Map").Open())) writer.Write("unmapped");
                Reject(() => lib.Import(structuredZip, "unmapped"), "unmapped sibling files still reject rather than silently disappear");
                Assert(lib.Installed("unmapped") == null, "failed sibling layout leaves no installed package");
                using (var archive = ZipFile.Open(structuredZip, ZipArchiveMode.Update))
                {
                    archive.GetEntry("Package/Other/unknown.SC2Map").Delete();
                    using (var writer = new StreamWriter(archive.CreateEntry("Package/Mods/unknown.txt").Open())) writer.Write("not a mod");
                }
                Reject(() => lib.Import(structuredZip, "not-a-mod"), "sibling Mods rejects non-SC2Mod content");
                foreach (var layout in new[] {
                    new[] { "WoL", "Maps/Campaign" }, new[] { "HotS", "Maps/Campaign/swarm" },
                    new[] { "LotV", "Maps/Campaign/void" }, new[] { "NCO", "Maps/Campaign/nova" } })
                {
                    string gameLayoutZip = Path.Combine(root, "game-layout-" + layout[0] + ".zip");
                    using (var archive = ZipFile.Open(gameLayoutZip, ZipArchiveMode.Create))
                    {
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/" + layout[1] + "/metadata.txt").Open()))
                            writer.Write("title=Game path layout\ncampaign=" + layout[0] + "\nversion=2.59");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/" + layout[1] + "/mission.SC2Map").Open())) writer.Write("mission");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/Mods/UED.SC2Mod").Open())) writer.Write("packed");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/Mods/UEDData.SC2Mod/Base.SC2Data/GameData/Data.xml").Open())) writer.Write("unpacked");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/README.txt").Open())) writer.Write("readme kept in payload");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/特别鸣谢.txt").Open())) writer.Write("credits kept in payload");
                        foreach (string companion in new[] { "VALIDATION.json", "수정안내.md", "PREVIEW.png" })
                            using (var writer = new StreamWriter(archive.CreateEntry("Package/" + companion).Open())) writer.Write("companion-only");
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/docs/검증/결과.json").Open())) writer.Write("nested companion");
                    }
                    string packageId = "game-layout-" + layout[0].ToLowerInvariant();
                    var imported = lib.Import(gameLayoutZip, packageId);
                    Assert(imported.files.Count == 4 && imported.files.Any(f => f.path == layout[1].Replace('/', '\\') + @"\mission.SC2Map") &&
                        imported.files.Any(f => f.path == @"Mods\UED.SC2Mod") &&
                        imported.files.Any(f => f.path == @"Mods\UEDData.SC2Mod\Base.SC2Data\GameData\Data.xml"),
                        "full CCM game tree maps exactly once for " + layout[0]);
                    string stored = Path.Combine(lib.Root, "packages", packageId, "versions", imported.versionKey, "payload", "Package", "README.txt");
                    Assert(File.ReadAllText(stored) == "readme kept in payload" &&
                        !imported.files.Any(f => f.path.EndsWith("README.txt") || f.path.EndsWith("特别鸣谢.txt")),
                        "package root text documents are preserved but not installed for " + layout[0]);
                    Assert(new[] { "VALIDATION.json", "수정안내.md", "PREVIEW.png" }.All(name =>
                        File.Exists(Path.Combine(Path.GetDirectoryName(stored), name)) &&
                        !imported.files.Any(f => f.path.EndsWith(name, StringComparison.OrdinalIgnoreCase))),
                        "generic validation JSON, Markdown and PNG previews are retained without game deployment for " + layout[0]);
                    Assert(File.Exists(Path.Combine(Path.GetDirectoryName(stored), "docs", "검증", "결과.json")) &&
                        !imported.files.Any(f => f.path.Contains("docs")),
                        "nested companion folders remain stored but never deployed for " + layout[0]);
                    using (var archive = ZipFile.Open(gameLayoutZip, ZipArchiveMode.Update))
                        using (var writer = new StreamWriter(archive.CreateEntry("Package/unmapped.SC2Map").Open())) writer.Write("not a document");
                    Reject(() => lib.Import(gameLayoutZip, packageId + "-invalid"), "unmapped root game file rejected for " + layout[0]);
                }
                Reject(() => lib.Import(Path.Combine(root, "A.zip"), "campaign-a"), "no silent package overwrite");
                Reject(() => lib.Import(Zip(root, "evil", "X", true), "evil"), "ZIP traversal rejected");
                Assert(!File.Exists(Path.Combine(root, "escaped.txt")), "no escaped file written");
                Assert(!Directory.GetDirectories(lib.Root, "import-*").Any(), "failed imports leave no temporary extracted files");
                Reject(() => lib.Activate("campaign-a", game, false, null), "mission/menu confirmation required");
                lib.Activate("campaign-a", game, true, null);
                Assert(File.ReadAllText(mission) == "A" && File.ReadAllText(shared) == "A-dependency", "activate A with shared dependency");
                lib.Activate("campaign-b", game, true, null);
                Assert(File.ReadAllText(mission) == "B" && File.ReadAllText(shared) == "B-dependency", "switch A to B");
                Assert(lib.Current().previousActive == a.id &&
                    InstalledSelection.Shelf(lib.All(), "wol", b.id, lib.Current()).First().id == a.id,
                    "successful apply puts previous active campaign first in installed shelf");
                lib.Activate("campaign-a", game, true, null);
                Assert(File.ReadAllText(mission) == "A", "switch B back to A");
                Reject(() => lib.Activate("campaign-b", game, true, n => { throw new IOException("injected-copy-failure"); }), "copy failure reported");
                Assert(File.ReadAllText(mission) == "A" && lib.Current().active == "campaign-a", "failure restores files and active state");
                Assert(lib.Current().previousActive == b.id, "failed activation preserves previous-active ordering");
                Assert(InstalledSelection.Shelf(new[] { a, b, new Campaign { id = "hots", name = "군심", slot = "hots" } }, "wol", a.id, lib.Current()).All(c => c.slot == "wol") &&
                    InstalledSelection.Slots.SequenceEqual(new[] { "wol", "hots", "lotv", "nco" }), "installed shelf uses only matching CCM slot and four sub-tabs");
                var classification = new[] {
                    new Campaign { id = "g", slot = "wol", categories = new[] { "gallery" } },
                    new Campaign { id = "t", slot = "wol", categories = new[] { "translated" } },
                    new Campaign { id = "r", slot = "wol", categories = new[] { "rogue" } },
                    new Campaign { id = "u", slot = "hots", categories = new[] { "untranslated" } },
                    new Campaign { id = "unknown", slot = "wol" }
                };
                Assert(InstalledSelection.AvailableCategories(classification, "wol")
                        .SequenceEqual(new[] { "gallery", "translated", "rogue", "uncategorized" }) &&
                    InstalledSelection.AvailableCategories(classification, "hots").SequenceEqual(new[] { "untranslated" }) &&
                    InstalledSelection.AvailableCategories(classification, "nco").Count == 0 &&
                    InstalledSelection.Category(classification[4]) == "uncategorized",
                    "each CCM expansion shows only installed categories; an unlinked ZIP is not mislabeled untranslated");
                var playFixtures = new[] {
                    new Campaign { id = "recent", name = "직전 적용", slot = "wol" },
                    new Campaign { id = "short", name = "가", slot = "wol" },
                    new Campaign { id = "long", name = "나", slot = "wol" },
                    new Campaign { id = "unplayed", name = "다", slot = "wol" }
                };
                var sampleSeconds = new Dictionary<string, double> {
                    { "recent", 5 }, { "short", 3600 }, { "long", 7201 }, { "unplayed", 0 }
                };
                var playedOrder = InstalledSelection.Shelf(playFixtures, "wol", "current",
                    new State { active = "current", previousActive = "recent" },
                    id => sampleSeconds[id]).Select(c => c.id).ToArray();
                Assert(playedOrder.SequenceEqual(new[] { "recent", "long", "short", "unplayed" }),
                    "previous applied campaign stays first; all other cards sort by actual play seconds descending");
                Assert(File.ReadAllText(unrelated) == "user-edit", "unowned user file preserved");
                var a2 = lib.Import(Zip(root, "A-update", "A2", false, "2.0"), "campaign-a");
                Assert(lib.Versions("campaign-a").Count == 2, "update retains both immutable ZIP versions");
                Assert(lib.VersionFolder(a) != lib.VersionFolder(a2) &&
                    File.Exists(Path.Combine(lib.VersionFolder(a), "manifest.json")) &&
                    File.Exists(Path.Combine(lib.VersionFolder(a2), "manifest.json")),
                    "folder management resolves exact selected old and new version directories");
                Reject(() => lib.VersionFolder(new Campaign { id = a.id, versionKey = "../outside" }),
                    "folder management rejects versions not registered in library");
                Assert(File.ReadAllText(mission) == "A", "installing update does not switch active version");
                Reject(() => lib.Activate("campaign-a", game, true, n => { throw new IOException("update-failure"); }),
                    "failed version update reported");
                Assert(File.ReadAllText(mission) == "A" && lib.Current().versionKey == a.versionKey,
                    "failed update restores previous version identity and files");
                lib.Activate("campaign-a", game, true, null);
                Assert(File.ReadAllText(mission) == "A2" && lib.Current().versionKey == a2.versionKey, "activate newly installed version");
                lib.Activate("campaign-a", game, true, null, a.versionKey);
                Assert(File.ReadAllText(mission) == "A" && lib.Current().versionKey == a.versionKey, "explicit rollback to older stored version");
                using (File.Open(shared, FileMode.Open, FileAccess.Read, FileShare.None))
                    Reject(() => lib.Activate("campaign-b", game, true, null), "locked dependency blocks before mutation");
                Assert(File.ReadAllText(mission) == "A", "lock rejection leaves active files unchanged");
                File.WriteAllText(mission, "edited-by-user");
                Reject(() => lib.Activate("campaign-b", game, true, null), "external user edits protected");
                Assert(File.ReadAllText(mission) == "edited-by-user", "external edit remains intact");
                Reject(() => Data.Under(root, "../outside"), "path containment rejects sibling");
                var catalogue = Data.ParseCatalogue("window.CAMPAIGNS=[{\"id\":\"a\",\"name\":\"한글\"}];");
                Assert(catalogue[0].name == "한글", "site JSON parsed as data");
                var extended = Data.ParseCatalogue("window.CAMPAIGNS=[{\"id\":\"a\",\"name\":\"한글 [테스트]\",\"summary\":\"인용 \\\" ] \\\"\",\"features\":[\"nested\"]}];\nwindow.SITE_COPY={\"title\":\"별도 데이터\"};");
                Assert(extended[0].name == "한글 [테스트]", "live response suffix and quoted brackets parsed safely");
                Reject(() => Data.ParseCatalogue("window.CAMPAIGNS=[{\"id\":\"a\",\"name\":\"A\"}"), "truncated JSON rejected");
                Reject(() => Data.ParseCatalogue("alert(1);"), "arbitrary JavaScript not evaluated");
                Assert(Data.DirectZip("https://example.com/releases/a.zip"), "direct HTTPS ZIP recognized");
                Assert(!Data.DirectZip("https://example.com/folder") && !Data.DirectZip("http://example.com/a.zip"), "folder and HTTP download not treated as direct ZIP");
                string refused = Path.Combine(root, "not-downloaded.zip");
                Reject(() => Data.DownloadZip("http://example.com/a.zip", refused), "unsafe download rejected before request");
                Assert(!File.Exists(refused), "rejected download creates no file");
                Assert(Catalogue.Keys.SequenceEqual(new[] { "wol", "hots", "lotv", "nco", "other" }) &&
                    Catalogue.Titles.Length == Catalogue.Keys.Length, "campaign navigation contains five base-campaign tabs");
                Assert(Catalogue.SiteKeys.SequenceEqual(new[] { "gallery", "rogue", "translated", "untranslated" }),
                    "four site categories remain above base-campaign subtabs");
                var dropData = new System.Windows.Forms.DataObject();
                dropData.SetData(System.Windows.Forms.DataFormats.FileDrop, new[] { @"C:\fixture.ZIP" });
                Assert(ZipDropTarget.ZipFiles(dropData).Length == 1, "ZIP drop accepts Windows file data and uppercase extension");
                dropData.SetData(System.Windows.Forms.DataFormats.FileDrop, new[] { @"C:\fixture.zip", @"C:\not-a-zip.exe" });
                Assert(ZipDropTarget.ZipFiles(dropData) == null && ZipDropTarget.ZipFiles(new System.Windows.Forms.DataObject("https://example.com/file.zip")) == null,
                    "ZIP drop rejects mixed executable files and URL/text drops");
                Assert(ArchiveView.InternalUrl(Data.Site + "/campaigns.html") &&
                    !ArchiveView.InternalUrl("https://example.com/") && !ArchiveView.InternalUrl("file:///C:/Windows") &&
                    !ArchiveView.InternalUrl(Data.Site.Replace("https:", "http:")) &&
                    !ArchiveView.InternalUrl(Data.Site.Replace("https://", "https://user@")) &&
                    !ArchiveView.InternalUrl(Data.Site + ":8443/"), "embedded archive permits only HTTPS site origin, no credentials or local files");
                Reject(() => ArchiveRuntime.VerifyMicrosoftSignature(Path.Combine(root, "A.zip")), "unsigned runtime installer cannot execute");
                var archiveItem = new Campaign { id = "archive-direct", name = "fixture", download = "https://example.com/main.zip",
                    downloads = new System.Collections.Generic.List<CampaignDownload> { new CampaignDownload { url = "https://example.com/alternate.zip", label = "다른 배포판" } } };
                Assert(Catalogue.ArchiveDownload(new[] { archiveItem }, archiveItem.download) == archiveItem &&
                    Catalogue.ArchiveDownload(new[] { archiveItem }, archiveItem.download + "#fragment") == archiveItem &&
                    Catalogue.ArchiveDownload(new[] { archiveItem }, archiveItem.downloads[0].url) == archiveItem,
                    "archive main and alternate distribution links resolve to registered campaign");
                Assert(Catalogue.ArchiveDownload(new[] { archiveItem }, "https://evil.example/main.zip") == null &&
                    Catalogue.ArchiveDownload(new[] { archiveItem }, "https://example.com/MAIN.zip") == null &&
                    Catalogue.ArchiveDownload(new[] { archiveItem }, "https://drive.google.com/drive/folders/test") == null &&
                    Catalogue.ArchiveDownload(new[] { archiveItem }, "file:///C:/main.zip") == null,
                    "unregistered, case-distinct, folder and local downloads never auto-install");
                Assert(Catalogue.ArchiveDownload(new[] { archiveItem, new Campaign { id = "ambiguous", download = archiveItem.download } }, archiveItem.download) == null &&
                    Catalogue.ArchiveDownload(new[] { new Campaign { id = "tool", kind = "resource", download = archiveItem.download } }, archiveItem.download) == null,
                    "ambiguous campaign links and developer resources never auto-install");
                var driveSite = new Campaign { id = "site-stable", name = "사이트 캠페인", download = "https://drive.google.com/drive/folders/fixture" };
                var linksFixture = new Campaign { id = "links", download = driveSite.download, downloads = new System.Collections.Generic.List<CampaignDownload> {
                    new CampaignDownload { url = driveSite.download }, new CampaignDownload { url = "file:///private/file.zip" },
                    new CampaignDownload { url = "https://example.com/release.zip", label = "별도 배포 파일" } } };
                Assert(DistributionPolicy.Links(linksFixture).Count == 2 &&
                    DistributionPolicy.Resolve(new[] { linksFixture }, "https://example.com/release.zip", linksFixture.id) == linksFixture,
                    "distribution choices use registered HTTPS links, deduplicate URLs and exclude local file targets");
                Assert(DistributionPolicy.Resolve(new[] { driveSite }, driveSite.download, driveSite.id) == driveSite &&
                    DistributionPolicy.Resolve(new[] { driveSite }, driveSite.download, "wrong-id") == null, "Drive distribution retains exact site campaign identity");
                Assert(DistributionPolicy.DriveDownload(driveSite.download, "https://drive.usercontent.google.com/download?id=fixture", "campaign.ZIP") &&
                    !DistributionPolicy.DriveDownload(driveSite.download, "https://evil.example/campaign.zip", "campaign.zip") &&
                    !DistributionPolicy.DriveDownload("https://evil.example", "https://drive.google.com/file", "campaign.zip") &&
                    !DistributionPolicy.DriveDownload(driveSite.download, "https://drive.google.com/file", "campaign.exe"), "Drive capture rejects untrusted origins and non-ZIP files");
                var linkedLibrary = new Library(Path.Combine(root, "linked-library"));
                var local = linkedLibrary.Import(Zip(root, "Local-linked", "local-linked", false), "local-fixture");
                linkedLibrary.LinkSite(local.id, driveSite.id);
                Assert(new Library(linkedLibrary.Root).CatalogueId(local.id) == driveSite.id &&
                    linkedLibrary.PackageId(driveSite.id) == local.id, "manual package site binding persists across restart");
                var update = linkedLibrary.ImportForSite(Zip(root, "Linked-update", "linked-update", false, "2.0", title: "Local-linked"), driveSite);
                Assert(update.id == local.id && linkedLibrary.Versions(local.id).Count == 2 && linkedLibrary.Current().active == null,
                    "site update joins manual package version history without activation");
                var newSite = new Campaign { id = "new-release", name = "새 출시" };
                var newRelease = linkedLibrary.ImportForSite(Zip(root, "New-release", "new-release", false), newSite);
                Assert(newRelease.id == newSite.id && linkedLibrary.All().Count == 2, "new site campaign installs without per-campaign app code");
                Reject(() => linkedLibrary.LinkSite(newRelease.id, driveSite.id), "site binding never silently merges distinct installed campaigns");
                Reject(() => linkedLibrary.ImportForSite(Zip(root, "Wrong-slot", "wrong-slot", false), new Campaign { id = "hots-only", @base = "군단의 심장" }),
                    "wrong campaign slot is rejected before committing linked import");
                Assert(linkedLibrary.Installed("hots-only") == null, "rejected site binding leaves no installed manifest");
                var matchSites = new[] {
                    new Campaign { id = "blackout", name = "블랙 아웃", @base = "자유의 날개", categories = new[] { "gallery" } },
                    new Campaign { id = "roguelite", name = "자날 로그라이트", @base = "자유의 날개", categories = new[] { "gallery", "rogue" } },
                    new Campaign { id = "alternate-site", name = "다른 캠페인", @base = "자유의 날개", categories = new[] { "translated" } } };
                var matchMetadata = new Campaign { name = "RGRogue(like) Black Out", sourceFileName = "RaynorRogue - Black Out_v1.0.zip", slot = "wol" };
                Assert(SiteMatcher.Unique(matchMetadata, matchSites) == matchSites[0], "ZIP metadata and filename identify Black Out despite site Korean name");
                Assert(SiteMatcher.Unique(new Campaign { name = "Unknown", sourceFileName = "RaynorRogueLite_v2.zip", slot = "wol" }, matchSites) == matchSites[1],
                    "camel-case ZIP names match whole site identifiers");
                Assert(SiteMatcher.Unique(new Campaign { name = "Black Out Curse", slot = "wol" },
                    new[] { matchSites[0], new Campaign { id = "blackout-curse", name = "블랙 아웃 커스", @base = "자유의 날개" } }) == null &&
                    SiteMatcher.Unique(new Campaign { name = "Black Out", slot = "hots" }, matchSites) == null &&
                    SiteMatcher.Unique(new Campaign { name = "Messengers", slot = "wol" }, new[] { new Campaign { id = "es", @base = "자유의 날개" } }) == null,
                    "ambiguous variants, wrong slots and inside-word short IDs do not auto-link");
                var autoLibrary = new Library(Path.Combine(root, "auto-library"));
                var koreanSites = new[] {
                    new Campaign { id = "hehestan", name = "로그 HEHESTAN", @base = "커스텀 캠페인" },
                    new Campaign { id = "star-rogue", name = "스타크래프트 로그", @base = "커스텀 캠페인" } };
                var koreanPackage = new Campaign { name = "Unknown", sourceFileName = "스타크래프트로그헬레스탄.zip", slot = "wol" };
                Assert(SiteMatcher.Candidates(koreanPackage, koreanSites).Count == 2 &&
                    SiteMatcher.Unique(koreanPackage, koreanSites) == null,
                    "joined Korean ZIP includes HEHESTAN alias and base campaign as choices, not an arbitrary automatic link");
                Assert(SiteMatcher.Unique(koreanPackage, new[] { koreanSites[0] }) == koreanSites[0],
                    "Korean HEHESTAN alias resolves a unique compatible site");
                Assert(SiteMatcher.SearchMatches(koreanSites[0], "로그헬레스탄") &&
                    SiteMatcher.SearchMatches(koreanSites[0], "HEHESTAN") &&
                    !SiteMatcher.SearchMatches(koreanSites[0], "없는 캠페인"),
                    "site search supports joined Korean aliases, case-insensitive IDs and no-result queries");
                var lingSite = new Campaign { id = "ling", name = "링 오브 날개", @base = "자유의 날개" };
                var alliesSite = new Campaign { id = "reoverhaul-redux-allies", name = "ReOverhaul Redux · 동맹과 함께하는 자날", @base = "자유의 날개" };
                Assert(SiteMatcher.Unique(new Campaign { name = "자유의 날개 리오버홀: 동맹과 함께", sourceFileName = "WoL_ReOverhaul_ReAllies_1.9.13_koKR.zip", slot = "wol" },
                    new[] { alliesSite }) == alliesSite, "real ReAllies metadata title and release filename match site identity");
                Assert(SiteMatcher.Unique(new Campaign { name = "Lings of Wiberty", sourceFileName = "lingofwiberty.zip", slot = "wol" }, new[] { lingSite }) == lingSite,
                    "English original and joined filename identify Korean Lings of Wiberty listing");
                var typo = new Campaign { name = "Lings of Wibety", slot = "wol" };
                Assert(SiteMatcher.Candidates(typo, new[] { lingSite }).Single() == lingSite && SiteMatcher.Unique(typo, new[] { lingSite }) == null,
                    "near spelling appears as recommendation but never silently auto-links");
                Assert(SiteMatcher.Candidates(new Campaign { name = "Ligns of Wiberty", slot = "wol" }, new[] { lingSite }).Count == 1 &&
                    SiteMatcher.Candidates(new Campaign { name = "Messengers", slot = "wol" }, new[] { new Campaign { id = "es", name = "ES", @base = "자유의 날개" } }).Count == 0,
                    "transposed letters are recommended without matching unrelated short IDs");
                var fit = InstalledSelection.FitDestination(new System.Drawing.Size(1920, 1080), new System.Drawing.Size(660, 500));
                var uedSites = new[] {
                    new Campaign { id = "project-ued-modified", name = "프로젝트 UED 개조판", @base = "자유의 날개" },
                    new Campaign { id = "project-ued-wol-korean", name = "프로젝트 UED", @base = "자유의 날개" },
                    new Campaign { id = "dev-ued-files", name = "UED 모드 파일", @base = "자유의 날개", kind = "resource" },
                    new Campaign { id = "other-kr", name = "다른 한글번역", @base = "자유의 날개" },
                    new Campaign { id = "ued-swarm", name = "UED 군심", @base = "군단의 심장" } };
                var uedPackage = new Campaign { name = "Unknown", sourceFileName = "UED_KR_v1.2.zip", slot = "wol" };
                var uedRecommendations = SiteMatcher.Candidates(uedPackage, uedSites);
                Assert(uedRecommendations.Select(c => c.id).SequenceEqual(uedSites.Take(2).Select(c => c.id)),
                    "UED ZIP keyword recommends original and modified UED without resources, wrong slots or KR-only matches");
                Assert(SiteMatcher.Unique(uedPackage, uedSites) == null &&
                    SiteMatcher.Unique(uedPackage, new[] { uedSites[1] }) == null,
                    "partial UED keyword never auto-links even when only one related campaign exists");
                var bundledSites = Data.ParseCatalogue(File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "catalogue.js")));
                var bundledUed = SiteMatcher.Candidates(uedPackage, bundledSites);
                Assert(bundledUed.Any(c => c.id == "project-ued-wol-korean") &&
                    bundledUed.Any(c => c.id == "project-ued-modified") && bundledUed.All(c => c.kind != "resource"),
                    "UED ZIP finds both real bundled campaign entries without adding the UED resource collection");
                uedPackage.sourceFileName = "프로젝트_UED_KR.zip";
                Assert(SiteMatcher.Candidates(uedPackage, uedSites).First() == uedSites[1],
                    "whole campaign name match precedes a partial UED match regardless of catalogue order");
                var genericZip = new Campaign { name = "Unknown", sourceFileName = "KR_koKR_Korean_v204_1.9.13.zip", slot = "wol" };
                Assert(SiteMatcher.Candidates(genericZip, uedSites).Count == 0,
                    "language tags and version numbers alone do not recommend unrelated campaigns");
                var overlappingNames = new[] {
                    new Campaign { id = "guide", name = "Guide to maps", @base = "자유의 날개" },
                    new Campaign { id = "moon-ued", name = "Moon UED", aliases = new[] { "UED Moon Story" }, @base = "자유의 날개" },
                    new Campaign { id = "ued-lunar", name = "UED Lunar", @base = "자유의 날개" } };
                uedPackage.sourceFileName = "UED_Moon_KR.zip";
                Assert(SiteMatcher.Candidates(uedPackage, overlappingNames).Select(c => c.id)
                    .SequenceEqual(new[] { "moon-ued", "ued-lunar" }),
                    "more shared keywords rank first, aliases do not duplicate cards and UED is not an inside-word substring");
                Assert(Math.Abs(fit.Width / fit.Height - 1920f / 1080) < 0.001f && fit.X >= 0 && fit.Y >= 0 &&
                    fit.Right <= 660.001f && fit.Bottom <= 500.001f, "full-resolution thumbnail fits whole image without cropping or aspect distortion");
                string autoZip = Zip(root, "RGRogue Black Out", "auto", false);
                var inspected = Library.InspectZip(autoZip);
                Assert(inspected.name == "RGRogue Black Out" && inspected.slot == "wol" && inspected.sourceFileName == Path.GetFileName(autoZip), "ZIP identity inspected before import using shared metadata parser");
                var autoPackage = autoLibrary.Import(autoZip, "local-auto");
                Assert(autoLibrary.AutoLink(autoPackage, matchSites) == matchSites[0] && autoLibrary.CatalogueId(autoPackage.id) == "blackout",
                    "existing unlinked ZIP gains automatic site artwork binding");
                autoLibrary.LinkSite(autoPackage.id, "alternate-site");
                Assert(autoLibrary.AutoLink(autoPackage, matchSites) == null && autoLibrary.CatalogueId(autoPackage.id) == "alternate-site",
                    "manual correction is preserved on subsequent auto-link scans");
                Assert(autoLibrary.LinkedSite(inspected, matchSites) == matchSites[2], "next ZIP update preserves manually corrected identity");
                var autoUpdate = autoLibrary.ImportForSite(Zip(root, "RGRogue Black Out update", "auto2", false, "2.0", title: "RGRogue Black Out"), matchSites[2]);
                Assert(autoUpdate.id == autoPackage.id && autoLibrary.Versions(autoPackage.id).Count == 2 && autoLibrary.Current().active == null,
                    "matched ZIP updates retain one version history and never activate the game");
                VerifyMetadataIdentity(root);
                var longLibrary = new Library(Path.Combine(root, "long-library-" + new string('p', 110)));
                var longItem = longLibrary.Import(Zip(root, "Long-path", "long-path", false), "long-path");
                var longFiles = Directory.GetFiles(Path.Combine(longLibrary.Root, "packages"), "*", SearchOption.AllDirectories);
                Assert(longFiles.Any(file => file.Length > 260) && longLibrary.Installed(longItem.id) != null,
                    ".NET long-path handling imports immutable versions beyond MAX_PATH");
                var categorized = new[] { new Campaign { id = "a & b", name = "군심", @base = "군단의 심장", categories = new[] { "gallery", "rogue" }, thumbnail = "assets/test.webp" },
                    new Campaign { id = "other", name = "도구", categories = new[] { "developer" }, kind = "resource" } };
                Assert(Catalogue.ToggleSelection(null, categorized[0]) == categorized[0] &&
                    Catalogue.ToggleSelection(categorized[1], categorized[0]) == categorized[0], "clicking a campaign selects it or replaces the previous selection");
                Assert(Catalogue.ToggleSelection(categorized[0], new Campaign { id = categorized[0].id }) == null, "clicking the selected campaign again clears selection");
                Assert(Catalogue.Filter(categorized, "rogue", "군", "hots").Count() == 1 &&
                    !Catalogue.Filter(categorized, "translated", "", "hots").Any() &&
                    !Catalogue.Filter(categorized, "rogue", "", "wol").Any(), "site category, base subtab and search filters combine correctly");
                Assert(new[] { "자유의 날개", "군단의 심장", "공허의 유산", "노바 비밀 작전", "독자 캠페인" }
                    .Select(value => Catalogue.Group(new Campaign { @base = value })).SequenceEqual(Catalogue.Keys),
                    "site base metadata maps exactly to four campaigns and other");
                Assert(Catalogue.DetailUrl(categorized[0]) == Data.Site + "/campaigns.html?campaign=a%20%26%20b#gallery", "site detail link uses encoded campaign ID and real category");
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == Data.Site + "/assets/test.webp", "thumbnail resolves against site origin");
                categorized[0].thumbnail = "https://media.forgecdn.net/avatars/193/524/636868521699918824.jpeg";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == categorized[0].thumbnail,
                    "site-listed ForgeCDN creator thumbnails are retained by hangar");
                categorized[0].thumbnail = "https://media.forgecdn.net/attachments/160/287/Logo.jpg";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == categorized[0].thumbnail,
                    "ForgeCDN attachment thumbnails are retained without manual artwork");
                categorized[0].thumbnail = "http://media.forgecdn.net/avatars/193/524/636868521699918824.jpeg";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == null, "unsecured ForgeCDN thumbnail rejected");
                categorized[0].thumbnail = "https://media.forgecdn.net.evil.example/a.jpg";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == null, "lookalike ForgeCDN host rejected");
                categorized[0].thumbnail = "https://media.forgecdn.net:444/a.jpg";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == null, "non-default ForgeCDN port rejected");
                categorized[0].thumbnail = "https://media.forgecdn.net/a.exe";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == null, "ForgeCDN non-image path rejected");
                categorized[0].thumbnail = "https://other.example/test.webp";
                Assert(Catalogue.ThumbnailUrl(categorized[0]) == null, "external thumbnail origin rejected");
                Assert(Catalogue.SameVersion("v2.1 · 정식 출시", "2.1.0") &&
                    !Catalogue.SameVersion("2.2", "2.1") && !Catalogue.SameVersion("", ""), "version display labels normalize without false update conflicts");
                Assert(Catalogue.SameVersion("1.9.13 · 한글 번역", "ReAllies 1.9.13") &&
                    !Catalogue.SameVersion("1.9.14", "ReAllies 1.9.13"), "ReAllies release prefix does not create a false update button");
                Assert(Catalogue.NewerVersion("v204", "v199") && Catalogue.NewerVersion("1.10", "1.9") &&
                    Catalogue.NewerVersion("1.9.14 · 한글 번역", "ReAllies 1.9.13") &&
                    !Catalogue.NewerVersion("2.1", "2.1.0") && !Catalogue.NewerVersion("1.2", "1.3") &&
                    !Catalogue.NewerVersion("최신 번역", "1.0") && !Catalogue.NewerVersion("1.1 beta", "1.0") &&
                    !Catalogue.NewerVersion("1.0", "") && !Catalogue.NewerVersion("9999999999999999999999999", "1"),
                    "newer version comparison handles numeric releases, normalization and unknown labels without guessing");
                VerifyContainerTransitions(root);
                VerifyDeploymentMoves(root);
                VerifyRemoval(root);
                string storageSource = Path.Combine(root, "storage-source");
                string storageTarget = Path.Combine(root, "storage-target");
                string preference = Path.Combine(root, "settings", "storage.json");
                Directory.CreateDirectory(Path.Combine(storageSource, "packages"));
                File.WriteAllText(Path.Combine(storageSource, "packages", "fixture.txt"), "preserve");
                Directory.CreateDirectory(Path.Combine(storageSource, "downloads"));
                File.WriteAllText(Path.Combine(storageSource, "downloads", "existing.zip"), "preserved download");
                string downloadPreference = Path.Combine(root, "settings", "downloads.json");
                string downloadTarget = Path.Combine(root, "zip-downloads");
                Assert(Storage.ResolveDownloads(downloadPreference) == Path.Combine(Storage.LegacyRoot, "downloads"),
                    "default ZIP directory does not follow custom campaign storage");
                Storage.SelectDownloads(downloadTarget, downloadPreference);
                Assert(Storage.ResolveDownloads(downloadPreference) == downloadTarget && !File.Exists(preference) &&
                    File.ReadAllText(Path.Combine(storageSource, "packages", "fixture.txt")) == "preserve",
                    "changing downloads changes only ZIP preference, not library or files");
                Storage.CopyAndSelect(storageSource, storageTarget, preference);
                Assert(Storage.Resolve(preference) == storageTarget, "custom storage location persists");
                Assert(File.ReadAllText(Path.Combine(storageTarget, "packages", "fixture.txt")) == "preserve" &&
                    File.ReadAllText(Path.Combine(storageSource, "packages", "fixture.txt")) == "preserve", "storage copies content and preserves original");
                Assert(Storage.ResolveDownloads(downloadPreference) == downloadTarget &&
                    !Directory.Exists(Path.Combine(storageTarget, "downloads")) &&
                    File.ReadAllText(Path.Combine(storageSource, "downloads", "existing.zip")) == "preserved download",
                    "relocating library preserves ZIP path and original downloads without copying downloads");
                string invalidDownload = Path.Combine(root, "not-a-folder");
                string defaultFixture = Path.Combine(root, "default-storage");
                Directory.CreateDirectory(Path.Combine(defaultFixture, "packages"));
                Directory.CreateDirectory(Path.Combine(defaultFixture, "downloads"));
                File.WriteAllText(Path.Combine(defaultFixture, "packages", "fixture.txt"), "old default");
                File.WriteAllText(Path.Combine(defaultFixture, "downloads", "keep.zip"), "keep download");
                Directory.CreateDirectory(Path.Combine(storageTarget, "originals-v2"));
                File.WriteAllText(Path.Combine(storageTarget, "originals-v2", "backup.txt"), "game original");
                string savedDefault = Storage.RestoreDefault(storageTarget, preference, defaultFixture);
                Assert(Storage.Resolve(preference) == defaultFixture &&
                    File.ReadAllText(Path.Combine(defaultFixture, "packages", "fixture.txt")) == "preserve" &&
                    File.ReadAllText(Path.Combine(defaultFixture, "originals-v2", "backup.txt")) == "game original",
                    "default restore copies verified campaign packages and original game backups before selecting");
                Assert(File.ReadAllText(Path.Combine(savedDefault, "packages", "fixture.txt")) == "old default" &&
                    File.ReadAllText(Path.Combine(storageTarget, "packages", "fixture.txt")) == "preserve",
                    "default restore preserves both existing default contents and source without merging");
                Assert(File.ReadAllText(Path.Combine(defaultFixture, "downloads", "keep.zip")) == "keep download" &&
                    Storage.ResolveDownloads(downloadPreference) == downloadTarget,
                    "default restore keeps default downloads at original path and separate ZIP preference unchanged");
                Assert(Storage.RestoreDefault(defaultFixture, preference, defaultFixture) == null,
                    "already-default restore is a no-op");
                string failedPreference = Path.Combine(root, "preference-is-directory");
                Directory.CreateDirectory(failedPreference);
                Reject(() => Storage.RestoreDefault(storageTarget, failedPreference, defaultFixture),
                    "preference write failure rejects default restore");
                Assert(File.ReadAllText(Path.Combine(defaultFixture, "downloads", "keep.zip")) == "keep download" &&
                    File.ReadAllText(Path.Combine(defaultFixture, "packages", "fixture.txt")) == "preserve" &&
                    Storage.Resolve(preference) == defaultFixture,
                    "failed default publication rolls destination and downloads back without changing selection");
                string emptyDefault = Path.Combine(root, "new-default");
                Storage.RestoreDefault(storageTarget, preference, emptyDefault);
                Assert(Storage.Resolve(preference) == emptyDefault &&
                    File.ReadAllText(Path.Combine(emptyDefault, "packages", "fixture.txt")) == "preserve",
                    "restore also supports a default directory that does not yet exist");
                Reject(() => Storage.RestoreDefault(storageTarget, preference, Path.Combine(storageTarget, "nested")),
                    "default restore refuses overlapping directories");
                Directory.CreateDirectory(Path.Combine(defaultFixture, "transaction"));
                File.WriteAllText(Path.Combine(defaultFixture, "transaction", "journal.json"), "{}");
                Reject(() => Storage.RestoreDefault(storageTarget, preference, defaultFixture),
                    "default restore refuses unrecovered destination transaction");
                Data.Write(preference, new StoragePreference { path = storageTarget });
                string fixtureGame = Path.Combine(root, "game-storage-fixture");
                Directory.CreateDirectory(fixtureGame);
                File.WriteAllText(Path.Combine(fixtureGame, "StarCraft II.exe"), "fixture");
                string gameExeHash = Data.Hash(Path.Combine(fixtureGame, "StarCraft II.exe"));
                string gameStorage = Path.Combine(root, "CampaignStorage");
                string channelDownloads = Path.Combine(fixtureGame, "Downloads");
                string blockedParent = Path.Combine(root, "permission-denied-parent-fixture");
                File.WriteAllText(blockedParent, "must not be replaced by a directory");
                string protectedLibrary = Path.Combine(blockedParent, "CampaignStorage");
                string protectedDownloads = Path.Combine(blockedParent, "Downloads");
                string userFallback = Path.Combine(root, "safe-user-storage");
                Assert(!Storage.CanWriteAt(protectedLibrary) && !Storage.CanWriteAt(protectedDownloads) &&
                    Storage.WritableDestination(protectedLibrary, userFallback) == userFallback &&
                    Storage.WritableDestination(protectedDownloads, userFallback) == userFallback &&
                    File.ReadAllText(blockedParent) == "must not be replaced by a directory",
                    "protected game-adjacent storage and Downloads select a user-writable fallback without touching protected paths");
                Assert(Storage.CanWriteAt(gameStorage) &&
                    Storage.WritableDestination(gameStorage, userFallback) == gameStorage &&
                    Storage.DefaultDownloadsForGame(fixtureGame) == channelDownloads,
                    "writable game-adjacent storage and game Downloads retain existing default behavior");
                string configuredNoDownloads = Path.Combine(root, "game-with-legacy-download-setting.json");
                Data.Write(configuredNoDownloads, new StoragePreference {
                    path = storageTarget, gameRoot = fixtureGame, downloadsPath = null });
                Assert(Storage.ResolveDownloads(Path.Combine(root, "missing-downloads-preference.json"),
                    configuredNoDownloads) == Storage.DefaultDownloadsForGame(fixtureGame),
                    "existing game selection without an explicit ZIP directory also probes writable fallback");
                Assert(Storage.DefaultForGame(fixtureGame) == gameStorage &&
                    !gameStorage.StartsWith(fixtureGame + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                    "default library is beside game folder, never inside Battle.net update tree");
                string oldGamePreference = Path.Combine(root, "old-game-preference.json");
                Data.Write(oldGamePreference, new StoragePreference { path = gameStorage, gameRoot = fixtureGame });
                Assert(Storage.ResolveDownloads(Path.Combine(root, "no-download-preference.json"), oldGamePreference) == channelDownloads,
                    "existing game selection without a download preference now defaults to game Downloads");
                Directory.CreateDirectory(Path.Combine(storageTarget, "empty-folder"));
                var storageProgress = new System.Collections.Generic.List<WorkProgress>();
                Storage.SelectGame(storageTarget, fixtureGame, preference, storageProgress.Add);
                string unsafeLibrary = Path.Combine(fixtureGame, "CampaignHangar");
                Reject(() => Storage.Relocate(gameStorage, unsafeLibrary, preference),
                    "manual move into game update tree is blocked before creating files");
                Reject(() => Storage.CopyAndSelect(gameStorage, unsafeLibrary, preference),
                    "copy-based relocation cannot recreate a library inside game folder");
                Reject(() => Storage.RestoreDefault(gameStorage, preference, unsafeLibrary),
                    "default restoration also rejects game folder as storage");
                Assert(!Directory.Exists(unsafeLibrary) && Storage.Resolve(preference) == gameStorage,
                    "blocked game-folder destinations preserve current library and game folder");
                Assert(Storage.Resolve(preference) == gameStorage && Storage.ResolveGame(preference, null) == fixtureGame &&
                    File.ReadAllText(Path.Combine(gameStorage, "packages", "fixture.txt")) == "preserve" &&
                    File.ReadAllText(Path.Combine(gameStorage, "originals-v2", "backup.txt")) == "game original",
                    "selecting game creates its own CampaignHangar library and atomically persists game and library paths");
                Assert(Storage.ResolveDownloads(downloadPreference, preference) == channelDownloads &&
                    Directory.Exists(channelDownloads),
                    "selecting game automatically creates Downloads and stores its path with game and library in one preference");
                Assert(storageProgress.Any(p => p.stage == "보관소 폴더 이동 중") &&
                    !storageProgress.Any(p => p.stage.StartsWith("복사 중")) &&
                    storageProgress.Last().stage == "경로 설정 저장 중",
                    "same-volume game selection reports a folder move without copying or hashing every file");
                Assert(Directory.Exists(Path.Combine(gameStorage, "empty-folder")),
                    "library relocation also preserves empty directories");
                Assert(Storage.ResolveDownloads(downloadPreference) == downloadTarget &&
                    Data.Hash(Path.Combine(fixtureGame, "StarCraft II.exe")) == gameExeHash &&
                    !Directory.Exists(Path.Combine(fixtureGame, "Maps")) && !Directory.Exists(Path.Combine(fixtureGame, "Mods")) &&
                    !Directory.Exists(storageTarget),
                    "same-volume game selection removes old library location while preserving downloads and game files");
                Storage.CopyAndSelect(gameStorage, storageTarget, preference);
                Storage.SelectGame(gameStorage, fixtureGame, preference);
                Assert(Storage.Resolve(preference) == gameStorage && Storage.ResolveGame(preference, null) == fixtureGame,
                    "selecting the same game and default library is idempotent");
                string customAgain = Path.Combine(root, "custom-again");
                Storage.CopyAndSelect(gameStorage, customAgain, preference);
                Assert(Storage.ResolveGame(preference, null) == fixtureGame,
                    "manual library relocation preserves selected game path");
                Assert(Storage.ResolveDownloads(downloadPreference, preference) == channelDownloads,
                    "manual library relocation keeps game download directory");
                Storage.SelectDownloads(downloadTarget, downloadPreference, preference);
                Assert(Storage.ResolveDownloads(downloadPreference, preference) == downloadTarget &&
                    Storage.Resolve(preference) == customAgain && Storage.ResolveGame(preference, null) == fixtureGame,
                    "manual download override preserves game and library preferences");
                Storage.SelectGame(customAgain, fixtureGame, preference, null, false);
                Assert(Storage.ResolveDownloads(downloadPreference, preference) == downloadTarget,
                    "return-to-default library action does not reset manually selected downloads");
                Storage.SelectGame(gameStorage, fixtureGame, preference);
                Assert(Storage.ResolveDownloads(downloadPreference, preference) == channelDownloads,
                    "reselecting game resets download override to game Downloads");
                Reject(() => Storage.SelectGame(storageTarget, fixtureGame, failedPreference),
                    "failed combined path preference rejects game switch");
                Assert(Storage.Resolve(preference) == gameStorage &&
                    Storage.ResolveDownloads(downloadPreference, preference) == channelDownloads &&
                    File.ReadAllText(Path.Combine(gameStorage, "packages", "fixture.txt")) == "preserve",
                    "failed game switch preserves selected library/download settings and destination contents");
                Data.Write(preference, new StoragePreference { path = customAgain, gameRoot = fixtureGame,
                    downloadsPath = channelDownloads });
                Reject(() => Storage.SelectGame(customAgain, root, preference), "game without StarCraft II.exe is rejected");
                Assert(Storage.Resolve(preference) == customAgain && Storage.ResolveGame(preference, null) == fixtureGame,
                    "failed game selection preserves both paths");
                var activeFixture = new State { active = "fixture", gameRoot = Path.Combine(root, "another-game") };
                Data.Write(Path.Combine(customAgain, "active.json"), activeFixture);
                Reject(() => Storage.SelectGame(customAgain, fixtureGame, preference),
                    "a campaign active in another game cannot silently carry its game state to the new game");
                Data.Write(preference, new StoragePreference { path = storageTarget });
                File.WriteAllText(invalidDownload, "file");
                Reject(() => Storage.SelectDownloads(invalidDownload, downloadPreference), "invalid ZIP directory rejected");
                Assert(Storage.ResolveDownloads(downloadPreference) == downloadTarget, "failed ZIP change preserves preference");
                string deletableZip = Zip(root, "remove-download", "no-zip-needed", false);
                var detachedLibrary = new Library(Path.Combine(root, "detached-library"));
                detachedLibrary.Import(deletableZip, "detached");
                File.Delete(deletableZip);
                string detachedGame = Path.Combine(root, "detached-game"); Directory.CreateDirectory(detachedGame);
                File.WriteAllText(Path.Combine(detachedGame, "StarCraft II.exe"), "fixture");
                detachedLibrary.Activate("detached", detachedGame, true, null);
                Assert(File.ReadAllText(Path.Combine(detachedGame, "Maps", "Campaign", "mission.SC2Map")) == "no-zip-needed",
                    "registered campaign applies after downloaded ZIP is deleted");
                Reject(() => Storage.CopyAndSelect(storageSource, storageTarget, preference), "nonempty storage target rejected");
                Reject(() => Storage.CopyAndSelect(storageSource, Path.Combine(storageSource, "nested"), preference), "nested storage target rejected");
                Directory.CreateDirectory(Path.Combine(storageSource, "transaction"));
                File.WriteAllText(Path.Combine(storageSource, "transaction", "journal.json"), "{}");
                Reject(() => Storage.CopyAndSelect(storageSource, Path.Combine(root, "blocked-target"), preference), "unrecovered transition blocks storage change");
                Reject(() => Storage.RestoreDefault(storageSource, preference, emptyDefault), "unrecovered source blocks default restore");
                Assert(Storage.Resolve(preference) == storageTarget, "failed storage switch preserves preference");
                VerifyStorageMoves(root);
                Console.WriteLine("PASSED " + passed + " checks. Real SC2, banks and saves untouched.");
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        }
        static string ShapeZip(string root, string name, bool loose)
        {
            string zip = Path.Combine(root, name + ".zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(archive.CreateEntry("Maps/metadata.txt").Open())) writer.Write("title=" + name + "\ncampaign=wol\nversion=1.0");
                foreach (string relative in new[] { loose ? "Maps/thanson01.SC2Map/MapInfo" : "Maps/thanson01.SC2Map",
                    loose ? "Mods/Shared.SC2Mod/Base.SC2Data/Data.xml" : "Mods/Shared.SC2Mod" })
                    using (var writer = new StreamWriter(archive.CreateEntry(relative).Open())) writer.Write(name + ":" + relative);
            }
            return zip;
        }
        static void VerifyStorageMoves(string root)
        {
            string source = Path.Combine(root, "move-source"), destination = Path.Combine(root, "move-destination");
            string preference = Path.Combine(root, "move-settings", "storage.json");
            Directory.CreateDirectory(Path.Combine(source, "packages"));
            string original = Path.Combine(source, "packages", "original.txt");
            File.WriteAllText(original, "original");
            Assert(Storage.SameVolume(source, destination), "volume identity recognizes isolated source and destination on same volume");
            var identity = Identity(original);
            using (File.Open(original, FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => Storage.Relocate(source, destination, preference), "locked same-volume source reports a move failure");
            Assert(File.Exists(original) && !Directory.Exists(destination), "blocked directory move preserves original source");
            Storage.Relocate(source, destination, preference);
            var relocated = Identity(Path.Combine(destination, "packages", "original.txt"));
            Assert(!Directory.Exists(source) && identity.Equals(relocated) && Storage.Resolve(preference) == destination,
                "same-volume relocation keeps the original physical file identity and removes old path, rather than making a copy");
            string failedPreference = Path.Combine(root, "move-preference-directory");
            Directory.CreateDirectory(failedPreference);
            Reject(() => Storage.Relocate(destination, source, failedPreference), "failed move preference is reported");
            Assert(Directory.Exists(destination) && !Directory.Exists(source) &&
                File.ReadAllText(Path.Combine(destination, "packages", "original.txt")) == "original",
                "failed same-volume path save moves the whole library back");
            Directory.CreateDirectory(Path.Combine(source, "packages"));
            File.WriteAllText(Path.Combine(source, "packages", "old.txt"), "existing destination");
            Directory.CreateDirectory(Path.Combine(source, "downloads"));
            File.WriteAllText(Path.Combine(source, "downloads", "target.zip"), "target download");
            Directory.CreateDirectory(Path.Combine(destination, "downloads"));
            File.WriteAllText(Path.Combine(destination, "downloads", "source.zip"), "source download");
            var moved = Storage.Relocate(destination, source, preference, null, true);
            Assert(File.ReadAllText(Path.Combine(moved.backup, "packages", "old.txt")) == "existing destination" &&
                !Directory.Exists(Path.Combine(destination, "packages")) &&
                File.ReadAllText(Path.Combine(destination, "downloads", "source.zip")) == "source download" &&
                File.ReadAllText(Path.Combine(source, "downloads", "target.zip")) == "target download",
                "same-volume move preserves old destination separately and keeps both ZIP download directories at their original paths");
            string crossRoot = Environment.GetEnvironmentVariable("HANGAR_CROSS_VOLUME_TEST_ROOT");
            if (String.IsNullOrEmpty(crossRoot))
            { Console.WriteLine("CROSS-VOLUME CHECK NOT RUN: set HANGAR_CROSS_VOLUME_TEST_ROOT to an isolated directory on another volume."); return; }
            string crossSource = Path.Combine(root, "cross-source");
            string crossDestination = Path.Combine(crossRoot, "cross-destination");
            Directory.CreateDirectory(Path.Combine(crossSource, "packages"));
            File.WriteAllText(Path.Combine(crossSource, "packages", "item.txt"), "cross-volume");
            Directory.CreateDirectory(Path.Combine(crossSource, "downloads"));
            File.WriteAllText(Path.Combine(crossSource, "downloads", "keep.zip"), "keep");
            Assert(!Storage.SameVolume(crossSource, crossDestination), "integration test uses two different actual volumes");
            var progress = new System.Collections.Generic.List<WorkProgress>();
            var cross = Storage.Relocate(crossSource, crossDestination, preference, progress.Add);
            Assert(cross.warning == null && !Directory.Exists(Path.Combine(crossSource, "packages")) &&
                File.ReadAllText(Path.Combine(crossDestination, "packages", "item.txt")) == "cross-volume" &&
                File.ReadAllText(Path.Combine(crossSource, "downloads", "keep.zip")) == "keep" &&
                progress.Any(p => p.stage.StartsWith("검증 중")) && progress.Any(p => p.stage == "이전 보관소 정리 중"),
                "cross-volume relocation verifies copied data then removes old library while preserving independent downloads");
            string crossFailure = Path.Combine(root, "cross-failure");
            Reject(() => Storage.Relocate(crossDestination, crossFailure, failedPreference),
                "failed cross-volume preference rejects relocation");
            Assert(File.ReadAllText(Path.Combine(crossDestination, "packages", "item.txt")) == "cross-volume" &&
                !Directory.Exists(crossFailure), "failed cross-volume publication leaves original intact and no selected partial destination");
            string lockedSource = Path.Combine(root, "cross-locked");
            string lockedDestination = Path.Combine(crossRoot, "cross-locked-destination");
            Directory.CreateDirectory(lockedSource);
            string locked = Path.Combine(lockedSource, "keep.txt"); File.WriteAllText(locked, "locked source");
            using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var partial = Storage.Relocate(lockedSource, lockedDestination, preference);
                Assert(partial.warning != null && Storage.Resolve(preference) == lockedDestination &&
                    File.ReadAllText(Path.Combine(lockedDestination, "keep.txt")) == "locked source" && File.Exists(locked),
                    "source cleanup lock reports retained files while keeping the verified new library selected");
            }
        }
        static void VerifyContainerTransitions(string root)
        {
            var lib = new Library(Path.Combine(root, "shape-library"));
            string game = Path.Combine(root, "shape-game"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture");
            string map = Data.Under(game, @"Maps\Campaign\thanson01.SC2Map");
            string mod = Data.Under(game, @"Mods\Shared.SC2Mod");
            Directory.CreateDirectory(Path.GetDirectoryName(map)); Directory.CreateDirectory(Path.GetDirectoryName(mod));
            File.WriteAllText(map, "original-packed-map"); File.WriteAllText(mod, "original-packed-mod");
            string user = Data.Under(game, @"Maps\Campaign\user.SC2Map"); File.WriteAllText(user, "user");
            var loose = lib.Import(ShapeZip(root, "shape-loose", true), "shape-loose");
            var packed = lib.Import(ShapeZip(root, "shape-packed", false), "shape-packed");
            Reject(() => lib.Activate(loose.id, game, true, n => { throw new IOException("injected loose copy failure"); }),
                "file-to-folder failed apply is reported");
            Assert(File.ReadAllText(map) == "original-packed-map" && File.ReadAllText(mod) == "original-packed-mod" && lib.Current().active == null,
                "failed file-to-folder apply restores original packed maps and mods");
            lib.Activate(loose.id, game, true, null);
            Assert(Directory.Exists(map) && Directory.Exists(mod) && File.Exists(Path.Combine(map, "MapInfo")),
                "packed map and mod can be replaced by verified loose container folders");
            string member = Path.Combine(map, "MapInfo"), originalMember = File.ReadAllText(member);
            File.WriteAllText(member, "same-length-tamper");
            Reject(() => lib.Activate(packed.id, game, true, null), "shared verification rejects edited loose container members");
            Assert(lib.Current().active == loose.id && File.ReadAllText(member) == "same-length-tamper",
                "shared verification preserves the active identity and externally edited member");
            File.WriteAllText(member, originalMember);
            using (File.Open(Path.Combine(map, "MapInfo"), FileMode.Open, FileAccess.Read, FileShare.None))
                Reject(() => lib.Activate(packed.id, game, true, null), "locked loose container member blocks before mutation");
            File.WriteAllText(Path.Combine(map, "user-added.txt"), "user added");
            Reject(() => lib.Activate(packed.id, game, true, null), "external extra file inside loose container is protected");
            Assert(File.ReadAllText(Path.Combine(map, "user-added.txt")) == "user added", "rejected switch preserves user-added container files");
            File.Delete(Path.Combine(map, "user-added.txt"));
            Reject(() => lib.Activate(packed.id, game, true, n => { throw new IOException("injected packed copy failure"); }),
                "folder-to-file failed apply is reported");
            Assert(Directory.Exists(map) && File.Exists(Path.Combine(map, "MapInfo")) && lib.Current().active == loose.id,
                "failed folder-to-file apply restores loose container tree and prior active identity");
            lib.Activate(packed.id, game, true, null);
            Assert(File.Exists(map) && File.Exists(mod) && lib.Current().active == packed.id && File.ReadAllText(user) == "user",
                "loose-to-packed map and mod transition preserves unrelated user files");
            lib.Activate(loose.id, game, true, null);
            lib.Remove(loose.id, loose.versionKey, RemovalKind.AllInformation, true);
            Assert(File.ReadAllText(map) == "original-packed-map" && File.ReadAllText(mod) == "original-packed-mod" && lib.Current().active == null,
                "full removal of loose campaign restores packed originals and clears active state");
            var second = new Library(Path.Combine(root, "shape-original-directory"));
            string otherGame = Path.Combine(root, "shape-game-directory"); Directory.CreateDirectory(otherGame);
            File.WriteAllText(Path.Combine(otherGame, "StarCraft II.exe"), "fixture");
            string oldDirectory = Data.Under(otherGame, @"Maps\Campaign\thanson01.SC2Map");
            Directory.CreateDirectory(oldDirectory); File.WriteAllText(Path.Combine(oldDirectory, "user-original.txt"), "original directory content");
            string empty = Path.Combine(oldDirectory, "empty"); Directory.CreateDirectory(empty);
            var item = second.Import(ShapeZip(root, "directory-original-packed", false), "directory-original");
            second.Activate(item.id, otherGame, true, null);
            Assert(File.Exists(oldDirectory), "existing original directory is backed up before packed replacement");
            second.Remove(item.id, item.versionKey, RemovalKind.AllInformation, true);
            Assert(File.ReadAllText(Path.Combine(oldDirectory, "user-original.txt")) == "original directory content" && Directory.Exists(empty),
                "unapply restores pre-existing directory files and empty subfolders exactly");
        }
        static void VerifyDeploymentMoves(string root)
        {
            string location = Path.Combine(root, "deployment-moves");
            var lib = new Library(Path.Combine(location, "library"));
            string game = Path.Combine(location, "game"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture");
            var a = lib.Import(Zip(location, "Move-A", "A", false), "a");
            var b = lib.Import(Zip(location, "Move-B", "B", false), "b");
            Func<Campaign, string> stored = c => Path.Combine(lib.Root, "packages", c.id, "versions", c.versionKey, "deploy", @"Maps\Campaign\mission.SC2Map");
            string target = Path.Combine(game, @"Maps\Campaign\mission.SC2Map");
            var identity = Identity(stored(a));
            lib.Activate(a.id, game, true, null);
            var timing = Data.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(lib.Root, "last-activation-timing.json")));
            var stages = ((System.Collections.IEnumerable)timing["stages"]).Cast<Dictionary<string,object>>().ToList();
            Assert(Convert.ToBoolean(timing["completed"]) && stages.Count > 0 &&
                stages.All(s => Convert.ToInt64(s["milliseconds"]) >= 0) &&
                stages.Sum(s => Convert.ToInt64(s["milliseconds"])) <= Convert.ToInt64(timing["totalMilliseconds"]),
                "completed activation records nonnegative stage times bounded by total duration");
            Assert(!File.Exists(stored(a)) && lib.Current().movedDeployment && identity.Equals(Identity(target)),
                "first activation moves original physical deploy file into game instead of copying it");
            Assert(Directory.GetFiles(lib.Root, "*", SearchOption.AllDirectories).Any(path => path.Contains("\\payload\\")),
                "source payload remains archival data, not the deploy files used for repeated switching");
            lib.Activate(b.id, game, true, null);
            Assert(File.ReadAllText(stored(a)) == "A" && !File.Exists(stored(b)) &&
                identity.Equals(Identity(stored(a))) && File.ReadAllText(target) == "B",
                "switch returns A to its exact version deploy and moves B out without recopying A");
            lib.Activate(a.id, game, true, null);
            Assert(identity.Equals(Identity(target)) && File.ReadAllText(stored(b)) == "B",
                "repeat activation preserves physical file identity across round trips");
            Reject(() => lib.Activate(b.id, game, true, n => { throw new IOException("injected move failure"); }),
                "injected partial deployment move reports failure");
            Assert(lib.Current().active == a.id && File.ReadAllText(target) == "A" &&
                !File.Exists(stored(a)) && File.ReadAllText(stored(b)) == "B",
                "move rollback restores game and both version locations without duplicate active deploy files");
            var state = lib.Current();
            string aStore = stored(a);
            Directory.CreateDirectory(Path.GetDirectoryName(aStore));
            File.Move(target, aStore);
            string transaction = Path.Combine(lib.Root, "transaction"); Directory.CreateDirectory(transaction);
            Data.Write(Path.Combine(transaction, "journal.json"), new Journal {
                before = state, gameRoot = game, moves = new System.Collections.Generic.List<DeploymentMove> {
                    new DeploymentMove { source = target, destination = aStore,
                        node = new OwnedFile { hash = a.files.Single(f => f.path.EndsWith("mission.SC2Map")).hash } } } });
            lib.Recover();
            Assert(File.ReadAllText(target) == "A" && !File.Exists(aStore) && lib.Current().active == a.id,
                "persisted interrupted move journal restores active game file on recovery");
            lib.Recover();
            Assert(File.ReadAllText(target) == "A", "recovery is safe when repeated after completed rollback");
            // Simulate a library created by the earlier copy-based release.
            Data.Copy(target, stored(a));
            state = lib.Current(); state.movedDeployment = false; Data.Write(Path.Combine(lib.Root, "active.json"), state);
            lib.Activate(b.id, game, true, null);
            Assert(lib.Current().movedDeployment && File.ReadAllText(stored(a)) == "A" && !File.Exists(stored(b)),
                "legacy copy-based active state upgrades to move-based deployment on next switch");
            string crossRoot = Environment.GetEnvironmentVariable("HANGAR_CROSS_VOLUME_TEST_ROOT");
            if (!String.IsNullOrEmpty(crossRoot))
            {
                var cross = new Library(Path.Combine(root, "cross-deploy-library"));
                string crossGame = Path.Combine(crossRoot, "deployment-game"); Directory.CreateDirectory(crossGame);
                File.WriteAllText(Path.Combine(crossGame, "StarCraft II.exe"), "fixture");
                var c = cross.Import(Zip(location, "Move-Cross-A", "cross-a", false), "cross-a");
                var d = cross.Import(Zip(location, "Move-Cross-B", "cross-b", false), "cross-b");
                cross.Activate(c.id, crossGame, true, null);
                string cPath = Path.Combine(cross.Root, "packages", c.id, "versions", c.versionKey, "deploy", @"Maps\Campaign\mission.SC2Map");
                Assert(!File.Exists(cPath) && File.ReadAllText(Path.Combine(crossGame, @"Maps\Campaign\mission.SC2Map")) == "cross-a",
                    "cross-volume activation verifies copy and removes deploy source");
                Reject(() => cross.Activate(d.id, crossGame, true, n => { throw new IOException("cross move fault"); }),
                    "cross-volume injected activation failure is reported");
                Assert(!File.Exists(cPath) && cross.Current().active == c.id &&
                    File.ReadAllText(Path.Combine(crossGame, @"Maps\Campaign\mission.SC2Map")) == "cross-a",
                    "cross-volume rollback restores previous active version and its file location");
            }
        }
        static void VerifyRemoval(string root)
        {
            var lib = new Library(Path.Combine(root, "removal-library"));
            string game = Path.Combine(root, "removal-game");
            Directory.CreateDirectory(Path.Combine(game, @"Maps\Campaign"));
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture");
            string mission = Path.Combine(game, @"Maps\Campaign\mission.SC2Map");
            string shared = Path.Combine(game, @"Mods\Shared.SC2Mod");
            string user = Path.Combine(game, @"Maps\Campaign\user.SC2Map");
            File.WriteAllText(mission, "original"); File.WriteAllText(shared, "original-shared"); File.WriteAllText(user, "user");
            string bank = Path.Combine(root, "removal-bank.SC2Bank"); File.WriteAllText(bank, "save");
            string firstZip = Zip(root, "Remove1", "R1", false);
            var first = lib.Import(firstZip, "remove");
            string secondZip = Zip(root, "Remove2", "R2", false, "2.0");
            using (var archive = ZipFile.Open(secondZip, ZipArchiveMode.Update))
                using (var writer = new StreamWriter(archive.CreateEntry("wrapper/Exclusive/nested.SC2Map").Open())) writer.Write("nested");
            var second = lib.Import(secondZip, "remove");
            var latest = lib.Import(Zip(root, "Remove3", "R3", false, "3.0"), "remove");
            lib.LinkSite("remove", "remove-site");
            lib.Activate("remove", game, true, null, second.versionKey);
            lib.Remove("remove", latest.versionKey, RemovalKind.PreviousVersions);
            Assert(lib.Versions("remove").Count == 2 && lib.Current().versionKey == second.versionKey &&
                lib.Installed("remove").versionKey == latest.versionKey, "previous-version cleanup preserves latest and active versions");
            lib.Remove("remove", latest.versionKey, RemovalKind.CampaignOnly);
            Assert(lib.Current().versionKey == second.versionKey && File.ReadAllText(mission) == "R2" &&
                lib.Versions("remove").Single().versionKey == second.versionKey &&
                File.Exists(Path.Combine(root, "Remove3.zip")),
                "deleting inactive selected version keeps a different active version and its ZIP");
            Reject(() => lib.Remove("remove", second.versionKey, RemovalKind.AllInformation), "all-information cleanup requires explicit file-list confirmation");
            File.WriteAllText(mission, "user-modified");
            Reject(() => lib.Remove("remove", second.versionKey, RemovalKind.AllInformation, true), "cleanup refuses externally changed owned files");
            Assert(lib.Installed("remove") != null && File.ReadAllText(mission) == "user-modified", "rejected cleanup preserves library and user modifications");
            File.WriteAllText(mission, "R2");
            Assert(Directory.Exists(Path.Combine(game, @"Maps\Campaign\Exclusive")), "activation records newly created campaign subfolder");
            string downloads = Path.Combine(root, "removal-downloads");
            Directory.CreateDirectory(downloads);
            string copiedZip = Path.Combine(downloads, "release.zip");
            string unrelatedZip = Path.Combine(downloads, "unrelated.zip");
            File.Copy(secondZip, copiedZip);
            File.WriteAllText(unrelatedZip, "unrelated ZIP");
            var zipTargets = lib.RemovalZips("remove", new[] { downloads });
            Assert(zipTargets.Any(z => z.path == copiedZip) && zipTargets.Any(z => z.path == firstZip) &&
                !zipTargets.Any(z => z.path == unrelatedZip),
                "full removal identifies recorded ZIPs including deleted versions and finds legacy copies by hash, never by filename alone");
            byte[] beforeZip = File.ReadAllBytes(copiedZip);
            File.AppendAllText(copiedZip, "external edit");
            Reject(() => lib.Remove("remove", second.versionKey, RemovalKind.AllInformation, true, zipTargets),
                "ZIP changed after confirmation blocks full removal before game or library mutation");
            Assert(lib.Current().active == "remove" && lib.Installed("remove") != null,
                "changed ZIP rejection preserves active campaign and library");
            File.WriteAllBytes(copiedZip, beforeZip);
            lib.Remove("remove", second.versionKey, RemovalKind.AllInformation, true, zipTargets);
            Assert(lib.Installed("remove") == null && lib.Current().active == null && lib.CatalogueId("remove") == "remove",
                "all-information cleanup clears package, binding and active campaign");
            Assert(File.ReadAllText(mission) == "original" && File.ReadAllText(shared) == "original-shared" &&
                File.ReadAllText(user) == "user" && File.ReadAllText(bank) == "save", "full cleanup restores originals and protects shared, unrelated files and saves");
            Assert(!Directory.Exists(Path.Combine(game, @"Maps\Campaign\Exclusive")) &&
                Directory.Exists(Path.Combine(game, @"Maps\Campaign")) && Directory.Exists(Path.Combine(game, "Mods")),
                "only recorded empty subfolders are removed, never shared root folders");
            Assert(!File.Exists(firstZip) && !File.Exists(secondZip) && !File.Exists(Path.Combine(root, "Remove3.zip")) &&
                !File.Exists(copiedZip) && File.Exists(unrelatedZip) &&
                Directory.GetDirectories(lib.Root, "completed-removal-*").Length == 1,
                "full removal recycles only confirmed source ZIPs and keeps unrelated ZIPs and shared recovery backup");
            var old = lib.Import(Zip(root, "Only1", "O1", false), "only");
            var newer = lib.Import(Zip(root, "Only2", "O2", false, "2.0"), "only");
            lib.Remove("only", newer.versionKey, RemovalKind.CampaignOnly);
            Assert(lib.Versions("only").Count == 1 && lib.Installed("only").versionKey == old.versionKey,
                "campaign-only removal deletes selected version and retains remaining version");
            lib.Activate("only", game, true, null);
            var activeSelected = lib.Import(Zip(root, "Only3", "O3", false, "3.0"), "only");
            lib.Activate("only", game, true, null, activeSelected.versionKey);
            lib.Remove("only", activeSelected.versionKey, RemovalKind.CampaignOnly);
            Assert(lib.Current().active == null && lib.Versions("only").Single().versionKey == old.versionKey &&
                File.ReadAllText(mission) == "original" && File.Exists(Path.Combine(root, "Only1.zip")) &&
                File.Exists(Path.Combine(root, "Only3.zip")) && File.ReadAllText(bank) == "save",
                "campaign-only deletion automatically unapplies selected active version while preserving older version, ZIPs and bank");
            lib.Activate("only", game, true, null);
            var unrelated = lib.Import(Zip(root, "Unrelated", "U", false), "unrelated");
            lib.Remove("unrelated", unrelated.versionKey, RemovalKind.AllInformation, true);
            Assert(lib.Current().active == "only" && File.ReadAllText(mission) == "O1", "removing inactive campaign preserves another active campaign");
            var retained = lib.Import(Zip(root, "Retain-link", "R", false), "retain-link");
            lib.LinkSite(retained.id, "retained-site");
            lib.Remove(retained.id, retained.versionKey, RemovalKind.CampaignOnly);
            Assert(lib.Installed(retained.id) == null && lib.CatalogueId(retained.id) == "retained-site",
                "campaign-only removal keeps site binding unlike all-information cleanup");
            string sharedZip = Zip(root, "Shared-removal-zip", "shared zip", false);
            var sharedA = lib.Import(sharedZip, "shared-a");
            var sharedB = lib.Import(sharedZip, "shared-b");
            Assert(lib.RemovalZips(sharedA.id).Count == 0, "ZIP also registered to another campaign is excluded from removal");
            lib.Remove(sharedA.id, sharedA.versionKey, RemovalKind.AllInformation, true);
            Assert(File.Exists(sharedZip) && lib.Installed(sharedB.id) != null, "full removal preserves another campaign and its shared ZIP");
            Reject(() => lib.Remove("../outside", old.versionKey, RemovalKind.AllInformation, true), "removal rejects escaped campaign paths");
        }
        static void VerifyNotifications(string root)
        {
            string file = Path.Combine(root, "notification-fixture", "notifications.json");
            var original = new Campaign { id = "first", name = "첫 캠페인", version = "1.0", summary = "소개" };
            var added = new Campaign { id = "second", name = "새 캠페인", version = "1.0" };
            var feed = new NotificationFeed(file, new[] { original });
            feed.ObserveSite(new[] { original });
            Assert(feed.Items.Count == 0, "first unchanged catalogue does not flood notifications");
            feed.ObserveSite(new[] { original, added });
            Assert(feed.UnreadCount == 1 && feed.Items[0].campaignId == added.id && feed.Items[0].title.Contains("새 캠페인"),
                "new campaign creates one unread detail notification");
            feed = new NotificationFeed(file, new Campaign[0]);
            Assert(feed.UnreadCount == 1 && feed.IsNewCampaign(added.id) && !feed.IsNewCampaign(original.id),
                "only newly added campaigns keep NEW across app restart");
            added.summary = "소개 보완"; feed.ObserveSite(new[] { original, added });
            Assert(feed.IsNewCampaign(added.id), "an unseen new campaign keeps NEW when its details change");
            feed.MarkCampaignViewed(added.id);
            Assert(!feed.IsNewCampaign(added.id) && feed.UnreadCount == 0, "opening the campaign clears its persisted NEW badge");
            feed.MarkViewed(); feed.ObserveSite(new[] { original, added });
            Assert(feed.UnreadCount == 0 && feed.Items.Count == 1, "viewed unchanged notices do not light the badge again");
            original.version = "2.0"; original.updateNote = "새 미션 추가";
            feed.ObserveSite(new[] { original, added });
            Assert(feed.UnreadCount == 1 && feed.Items[0].campaignId == original.id &&
                feed.Items[0].summary.Contains("새 미션 추가"), "version updates include the site's update note");
            Assert(!feed.IsNewCampaign(original.id), "a version update is not mislabeled as a newly added campaign");
            string verbosePatch = "1 자동 확인 (2026-11-12 03:46 KST): 제작자 Wimbledon의 #7235 배포 자료에서 II&II V1.zip을 확인했습니다. " +
                "새 버전의 배포 파일을 반영했습니다. 변경 사항: 균열 안에서도 캠페인의 유닛·건물·기술·해금 상태가 그대로 적용됩니다. " +
                "· 난이도 패널티가 균열 공세에도 적용됩니다.";
            string onlyChanges = NotificationFeed.PatchNotesOnly(verbosePatch);
            Assert(onlyChanges.StartsWith("균열 안에서도") && onlyChanges.Contains("난이도 패널티") &&
                !onlyChanges.Contains("자동 확인") && !onlyChanges.Contains("배포 파일"),
                "release notes show real patch changes without automated upload logs");
            Assert(NotificationFeed.DisplaySummary(new HangarNotice { versionUpdate = true, summary = verbosePatch }) == onlyChanges,
                "previously saved update notices also hide the automated deployment preamble");
            original.version = "3.0"; original.updateNote = verbosePatch;
            feed.ObserveSite(new[] { original, added });
            Assert(feed.Items[0].summary.StartsWith("균열 안에서도") && !feed.Items[0].summary.Contains("자동 확인"),
                "new update notices store only actual patch content even after the 220-character limit");
            feed.MarkViewed(); original.summary = "설명 보완"; feed.ObserveSite(new[] { original, added });
            Assert(feed.UnreadCount == 0, "metadata-only edits do not create version update notices");
            feed.MarkViewed(); original.categories = new string[0]; feed.ObserveSite(new[] { original, added });
            Assert(feed.UnreadCount == 0, "equivalent null and empty category lists do not create false updates");
            original.categories = new[] { "zerg", "terran" }; feed.ObserveSite(new[] { original, added }); feed.MarkViewed();
            original.categories = new[] { "terran", "zerg" }; feed.ObserveSite(new[] { added, original });
            Assert(feed.UnreadCount == 0, "category and catalogue ordering do not create false updates");
            var release = new AppRelease { version = "99.0.0.0", notes = "알림 테스트용 · 설치하지 않음" };
            feed.ObserveApp(release);
            Assert(feed.UnreadCount == 1 && feed.Items[0].kind == "app" && feed.Items[0].summary.Contains("설치하지 않음"),
                "new app release creates a separate update-action notice");
            feed.MarkViewed(); feed.ObserveApp(release);
            Assert(feed.UnreadCount == 0, "same app release stays read after repeated checks");
            feed.ObserveApp(new AppRelease { version = AppUpdate.Current.ToString() });
            Assert(feed.Items.All(item => item.kind != "app"), "current app release removes obsolete update notices");
            feed.ObserveSite(new[] { original });
            Assert(feed.Items.All(item => item.campaignId != added.id), "removed site entries cannot leave dead detail actions");
            string emptyFile = Path.Combine(root, "notifications-no-cache.json");
            var empty = new NotificationFeed(emptyFile, new Campaign[0]);
            empty.ObserveSite(new[] { added });
            Assert(empty.Items.Count == 0, "first online catalogue establishes a baseline when no cache exists");
            string saved = File.ReadAllText(file);
            using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => feed.ObserveSite(new[] { original, added }), "notification save failures are reported");
            Assert(feed.Items.All(item => item.campaignId != added.id) && File.ReadAllText(file) == saved,
                "failed notification writes preserve both previous memory and disk state");
        }
        public static void VerifyNewsScroll(string root)
        {
            Assert(!Directory.Exists(root), "news and scroll checks require a new isolated directory");
            Directory.CreateDirectory(root);
            VerifyNotifications(root);
            var campaign = new Campaign { id = "legacy", name = "기존 모드", version = "1.0", summary = "긴 작품 소개는 업데이트 설명이 아님" };
            string legacyFile = Path.Combine(root, "legacy-notices.json");
            Data.Write(legacyFile, new NoticeState {
                initialized = true,
                snapshots = new System.Collections.Generic.Dictionary<string, string> { { campaign.id, "opaque-old-metadata-hash" } },
                items = new System.Collections.Generic.List<HangarNotice> {
                    new HangarNotice { key = "site:legacy", kind = "site", campaignId = campaign.id,
                        title = "사이트 정보 갱신 · 기존 모드", summary = "예전 소개 변경", version = "1.0", unread = true }
                }
            });
            var legacy = new NotificationFeed(legacyFile, new[] { campaign });
            legacy.ObserveSite(new[] { campaign });
            Assert(legacy.UnreadCount == 0 && legacy.Items.Count == 1 &&
                Data.Read<NoticeState>(legacyFile).items[0].unread,
                "legacy metadata history is preserved without pretending it is a version update");
            campaign.version = "v1.0.0 · 표시만 수정"; campaign.translator = "번역자명 변경";
            legacy.ObserveSite(new[] { campaign });
            Assert(legacy.UnreadCount == 0, "equivalent version labels and translator edits do not produce notices");
            campaign.version = "2.0"; campaign.updateNote = "새 임무 추가와 진행 오류 수정";
            legacy.ObserveSite(new[] { campaign });
            Assert(legacy.UnreadCount == 1 && legacy.Items[0].versionUpdate &&
                !legacy.Items[0].newCampaign && legacy.Items[0].summary == campaign.updateNote,
                "version updates show changes rather than the campaign introduction");
            var news = new[] {
                new HangarNotice { key = "new", kind = "site", newCampaign = true, unread = true,
                    title = "새로운 모드 · 창 크기와 화면 배율에 맞춰 표시되는 긴 한국어 캠페인 이름",
                    version = "2026.11.07-ko-r2", summary = "새로 등록된 캠페인입니다." },
                new HangarNotice { key = "update", kind = "site", versionUpdate = true, unread = true,
                    title = "모드 업데이트 · 기존 캠페인의 새로운 버전", version = "2.0",
                    summary = String.Join(" ", Enumerable.Repeat("임무 진행 오류와 한국어 설명 및 저장 호환 문제를 수정했습니다.", 4)) },
                new HangarNotice { key = "app", kind = "app", unread = true, title = "격납고 업데이트",
                    version = "99.0.0.0", summary = "새소식 분류와 스크롤 성능을 개선했습니다." }
            };
            using (var dialog = new NotificationDialog(news))
            {
                dialog.Show(); System.Windows.Forms.Application.DoEvents();
                dialog.CapturePreview(Path.Combine(root, "news-three-tabs.png"));
                dialog.VerifyFixture(news[0]); dialog.Close();
            }
            using (var dialog = new NotificationDialog(new HangarNotice[0]))
            {
                dialog.Show(); System.Windows.Forms.Application.DoEvents();
                TextLayout.VerifyLabels(dialog); dialog.Close();
            }
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(600, 300) })
            using (var content = new System.Windows.Forms.FlowLayoutPanel {
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown, WrapContents = false })
            using (var viewport = new DarkScrollPanel(content) { Dock = System.Windows.Forms.DockStyle.Fill })
            {
                for (int i = 0; i < 40; i++)
                    content.Controls.Add(new MetalButton { Text = "격리 스크롤 " + i, Size = new System.Drawing.Size(260, 60) });
                form.Controls.Add(viewport); form.Show(); System.Windows.Forms.Application.DoEvents();
                viewport.ArrangeContent();
                var bar = viewport.Controls.OfType<DarkScrollBar>().Single();
                Assert(bar.Maximum > 1000, "wheel fixture has sufficient overflow");
                bar.Value = 200; System.Windows.Forms.Application.DoEvents();
                int layouts = 0;
                viewport.Layout += delegate { layouts++; };
                for (int i = 0; i < 12; i++) { bar.Value += 15; viewport.Update(); System.Windows.Forms.Application.DoEvents(); }
                Assert(layouts == 0, "scrolling does not relayout the content at every wheel position");
                bar.Value = 200;
                for (int i = 0; i < 3; i++) viewport.ApplyWheel(-30, 3);
                Assert(bar.Value == 200, "partial wheel input is accumulated without premature movement");
                viewport.ApplyWheel(-30, 3);
                Assert(bar.Value == 200 + viewport.Font.Height * 3 * 2,
                    "four partial wheel deltas equal one full notch at the approved 2x distance");
                bar.Value = 100;
                viewport.ApplyWheel(-120, -1);
                Assert(bar.Value == 100 + viewport.ClientSize.Height, "page-wheel setting scrolls down by one viewport");
                int unchanged = bar.Value;
                viewport.ApplyWheel(-120, 0);
                Assert(bar.Value == unchanged, "disabled wheel scrolling is respected");
                bar.Value = bar.Maximum;
                Assert(!viewport.ApplyWheel(-120, 3), "nested scrolling yields to its parent at the bottom");
                bar.Value = 0;
                Assert(!viewport.ApplyWheel(120, 3), "nested scrolling yields to its parent at the top");
                viewport.ApplyWheel(-30, 3); viewport.ResetPosition(); viewport.ApplyWheel(-30, 3);
                Assert(bar.Value == 0, "changing a page clears fractional wheel remainder");
                int invalidations = 0;
                bar.Invalidated += delegate { invalidations++; };
                bar.SetRange(content.Height, viewport.ClientSize.Height);
                Assert(invalidations == 0, "unchanged scroll ranges do not request another repaint");
                viewport.HideScrollBar = true;
                viewport.ArrangeContent();
                Assert(!bar.Visible && bar.Maximum > 0 && content.Width > 560,
                    "hidden campaign scroll track retains overflow and uses available width");
                viewport.ResetPosition();
                Assert(viewport.ApplyWheel(-120, 3) && bar.Value > 0 && content.Top < 0,
                    "mouse wheel still scrolls when the scroll track is hidden");
                viewport.ResetPosition();
                Assert(content.Top == 0, "hidden scrollbar position resets without moving any files");
                // Screenshot-animated gesture: the WinForms child controls must
                // remain stationary while an isolated bitmap follows the input.
                viewport.HideScrollBar = false;
                viewport.SnapshotGestureScroll = true;
                viewport.ArrangeContent();
                int beforeGesture = content.Top;
                Assert(viewport.ApplySnapshotWheel(-120, 3) && viewport.SnapshotIsActive,
                    "one wheel gesture starts a single bitmap animation");
                int gestureTarget = viewport.SnapshotTarget;
                Assert(content.Top == beforeGesture && bar.Value == 0 && gestureTarget > 0,
                    "native WinForms children do not move or repaint at gesture start");
                Assert(gestureTarget == Math.Min(bar.Maximum, viewport.Font.Height * 3 * 2),
                    "one wheel notch advances twice the original campaign-list distance");
                viewport.AdvanceGestureFrameForTest(0.35);
                Assert(viewport.SnapshotOffset > 0 && viewport.SnapshotOffset < gestureTarget &&
                    content.Top == beforeGesture,
                    "animation shows intermediate positions without moving child HWNDs");
                viewport.ApplySnapshotWheel(-120, 3);
                Assert(viewport.SnapshotTarget > gestureTarget,
                    "consecutive wheel input extends the same animated scroll");
                viewport.AdvanceGestureFrameForTest(1.0);
                Assert(!viewport.SnapshotIsActive && content.Top == -bar.Value &&
                    bar.Value > gestureTarget,
                    "the final child position is committed only when the gesture finishes");
                viewport.ResetPosition();
                // Mouse drag of the real thumb must share the same safe path.
                var mouseDown = typeof(System.Windows.Forms.Control).GetMethod("OnMouseDown",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var mouseMove = typeof(System.Windows.Forms.Control).GetMethod("OnMouseMove",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var mouseUp = typeof(System.Windows.Forms.Control).GetMethod("OnMouseUp",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                mouseDown.Invoke(bar, new object[] { new System.Windows.Forms.MouseEventArgs(
                    System.Windows.Forms.MouseButtons.Left, 1, 8, 8, 0) });
                mouseMove.Invoke(bar, new object[] { new System.Windows.Forms.MouseEventArgs(
                    System.Windows.Forms.MouseButtons.Left, 0, 8, 90, 0) });
                Assert(viewport.SnapshotIsActive && bar.Value > 0 && content.Top == 0,
                    "dragging the scrollbar does not immediately relocate nested campaign windows" +
                    "; captured=" + bar.Capture + " visible=" + bar.Visible +
                    " value=" + bar.Value + " top=" + content.Top +
                    " snapshot=" + viewport.SnapshotIsActive);
                Assert(Math.Abs(viewport.SnapshotOffset - bar.Value) <= 1,
                    "the bitmap follows the scrollbar thumb directly, without a delayed tween");
                mouseUp.Invoke(bar, new object[] { new System.Windows.Forms.MouseEventArgs(
                    System.Windows.Forms.MouseButtons.Left, 1, 8, 90, 0) });
                Assert(!viewport.SnapshotIsActive && content.Top == -bar.Value,
                    "releasing the scrollbar thumb immediately commits the destination");
                viewport.ResetPosition();
                Console.WriteLine("DRAG SCROLL PASS: wheel and thumb drag animate one bitmap, with one final HWND position.");
                form.Close();
            }
            VerifyResizePainting();
            Console.WriteLine("NEWS/SCROLL PASS: version-only news, three tabs, complete measured text, wheel routing and resize redraw.");
        }
        static void VerifyResizePainting()
        {
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(600, 400) })
            using (var surface = new MetalPanel { Bounds = new System.Drawing.Rectangle(10, 10, 440, 240) })
            using (var button = new MetalButton { Text = "창 크기 변경 검사", Bounds = new System.Drawing.Rectangle(10, 270, 300, 70) })
            using (var scroll = new DarkScrollBar { Bounds = new System.Drawing.Rectangle(500, 10, 18, 320) })
            {
                form.Controls.Add(surface); form.Controls.Add(button); form.Controls.Add(scroll);
                scroll.SetRange(1000, 200);
                form.Show(); System.Windows.Forms.Application.DoEvents();
                foreach (System.Windows.Forms.Control control in new System.Windows.Forms.Control[] { surface, button, scroll })
                {
                    bool invalidated = false;
                    System.Windows.Forms.InvalidateEventHandler handler = delegate(object sender, System.Windows.Forms.InvalidateEventArgs e) {
                        invalidated |= e.InvalidRect.Contains(control.ClientRectangle);
                    };
                    control.Invalidated += handler;
                    control.Size = new System.Drawing.Size(control.Width, control.Height - 30);
                    System.Windows.Forms.Application.DoEvents();
                    control.Invalidated -= handler;
                    if (!invalidated) throw new InvalidOperationException(control.GetType().Name + " did not request a full redraw after shrinking.");
                }
                form.Close();
            }
            Console.WriteLine("UI PASS: shaded panel, button and scrollbar request a full redraw when shrinking.");
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(440, 280) })
            using (var content = new System.Windows.Forms.FlowLayoutPanel { AutoSize = false })
            using (var viewport = new DarkScrollPanel(content) { Dock = System.Windows.Forms.DockStyle.Fill })
            {
                for (int i = 0; i < 20; i++)
                    content.Controls.Add(new MetalButton { Text = "스크롤 검사 " + i, Size = new System.Drawing.Size(200, 60) });
                form.Controls.Add(viewport);
                form.Show(); System.Windows.Forms.Application.DoEvents(); viewport.ArrangeContent();
                var scrollbar = viewport.Controls.OfType<DarkScrollBar>().Single();
                if (scrollbar.Maximum <= 0) throw new InvalidOperationException("Scroll fixture must overflow.");
                bool painted = false;
                viewport.Paint += delegate { painted = true; };
                foreach (int value in new[] { scrollbar.Maximum, 0, scrollbar.Maximum / 2, 0 })
                {
                    painted = false; scrollbar.Value = value;
                    viewport.Update();
                    if (content.Top != -value || !painted)
                        throw new InvalidOperationException("Scrolling did not redraw the viewport when pending paint was processed.");
                }
                form.Close();
            }
            Console.WriteLine("UI PASS: rapid forward/back scrolling redraws the viewport at every position after pending paint.");
        }
        public static void VerifyGestureScrollVisual(string output)
        {
            if (Directory.Exists(output)) throw new InvalidOperationException("Use a new isolated gesture QA folder.");
            Directory.CreateDirectory(output);
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(980, 680),
                Text = "격납고 드래그 스크롤 검증용 - 실제 캠페인 데이터 사용 안 함" })
            using (var content = new System.Windows.Forms.FlowLayoutPanel {
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown, WrapContents = false,
                BackColor = GlassTheme.Background })
            using (var viewport = new DarkScrollPanel(content) { Dock = System.Windows.Forms.DockStyle.Fill,
                HideUnusedScrollBar = true, SnapshotGestureScroll = true })
            {
                for (int i = 0; i < 24; i++)
                {
                    var panel = new System.Windows.Forms.Panel { Width = 920, Height = 104,
                        BackColor = i % 2 == 0 ? System.Drawing.Color.FromArgb(25, 45, 68) :
                            System.Drawing.Color.FromArgb(38, 35, 59), Margin = new System.Windows.Forms.Padding(6) };
                    panel.Controls.Add(new System.Windows.Forms.Label {
                        Dock = System.Windows.Forms.DockStyle.Fill, Text = "캠페인 카드 · 화면 밀림 검사  #" + (i + 1),
                        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                        ForeColor = System.Drawing.Color.White, BackColor = System.Drawing.Color.Transparent,
                        Font = new System.Drawing.Font("맑은 고딕", 16, System.Drawing.FontStyle.Bold) });
                    content.Controls.Add(panel);
                }
                form.Controls.Add(viewport);
                form.Show(); System.Windows.Forms.Application.DoEvents();
                viewport.ArrangeContent();
                Action<string> capture = name => {
                    form.Refresh(); System.Windows.Forms.Application.DoEvents();
                    using (var bitmap = new System.Drawing.Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(output, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                };
                capture("01-original");
                if (!viewport.ApplySnapshotWheel(-720, 3) || !viewport.SnapshotIsActive)
                    throw new InvalidOperationException("Gesture snapshot did not begin in visual fixture.");
                viewport.AdvanceGestureFrameForTest(0.50);
                if (!viewport.SnapshotIsActive || viewport.SnapshotOffset <= 0 ||
                    content.Top != 0) throw new InvalidOperationException("Child controls moved during visual gesture.");
                capture("02-halfway-overlay");
                var overlay = viewport.Controls.Cast<System.Windows.Forms.Control>()
                    .Single(control => control.GetType().Name == "ScrollSnapshotOverlay");
                if (!overlay.Visible || overlay.Width < 700 || overlay.Height < 400)
                    throw new InvalidOperationException("Gesture bitmap overlay is missing or undersized.");
                using (var overlayImage = new System.Drawing.Bitmap(overlay.Width, overlay.Height))
                {
                    overlay.DrawToBitmap(overlayImage,
                        new System.Drawing.Rectangle(System.Drawing.Point.Empty, overlay.Size));
                    overlayImage.Save(Path.Combine(output, "02-overlay-alone.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
                viewport.AdvanceGestureFrameForTest(1.0);
                if (viewport.SnapshotIsActive || content.Top >= 0)
                    throw new InvalidOperationException("Gesture did not commit final child position.");
                capture("03-committed");
                form.Close();
                Console.WriteLine("GESTURE VISUAL PASS: original, bitmap-only intermediate, native final screenshots generated in " + output);
            }
        }
        public static void PreviewGestureScroll()
        {
            using (var form = new System.Windows.Forms.Form {
                ClientSize = new System.Drawing.Size(1000, 760),
                Text = "격납고 스크롤 렌더링 검증 · 테스트 전용 (게임/설정 변경 없음)" })
            using (var content = new System.Windows.Forms.FlowLayoutPanel {
                FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                WrapContents = false, BackColor = GlassTheme.Background })
            using (var viewport = new DarkScrollPanel(content) {
                Dock = System.Windows.Forms.DockStyle.Fill,
                HideUnusedScrollBar = true, SnapshotGestureScroll = true })
            {
                for (int i = 0; i < 30; ++i)
                {
                    var card = new System.Windows.Forms.Panel {
                        Width = 920, Height = 100, Margin = new System.Windows.Forms.Padding(6),
                        BackColor = i % 2 == 0 ? System.Drawing.Color.FromArgb(28, 47, 70) :
                            System.Drawing.Color.FromArgb(38, 33, 57) };
                    card.Controls.Add(new System.Windows.Forms.Label {
                        Dock = System.Windows.Forms.DockStyle.Fill,
                        ForeColor = System.Drawing.Color.White, BackColor = System.Drawing.Color.Transparent,
                        Text = "격납고 스크롤 테스트 카드 " + (i + 1),
                        TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                        Font = new System.Drawing.Font("맑은 고딕", 16, System.Drawing.FontStyle.Bold) });
                    content.Controls.Add(card);
                }
                form.Controls.Add(viewport);
                form.Show();
                viewport.ArrangeContent();
                viewport.ApplySnapshotWheel(-720, 3);
                viewport.AdvanceGestureFrameForTest(0.5);
                // Inspect this real desktop frame while child HWNDs remain
                // stationary; user wheel or drag then resumes a new motion.
                viewport.HoldGestureForVisualCheck();
                System.Windows.Forms.Application.Run(form);
            }
        }
        public static void VerifyChoiceFieldSurface()
        {
            foreach (int width in new[] { 220, 480, 900 })
            foreach (int height in new[] { 52, 70 })
            foreach (float size in new[] { 9.75f, 12.5f, 16f })
            foreach (bool enabled in new[] { false, true })
            using (var field = new GlassChoiceField { Size = new System.Drawing.Size(width, height), Enabled = enabled })
            using (var font = new System.Drawing.Font("맑은 고딕", size))
            using (var image = new System.Drawing.Bitmap(width, height))
            {
                field.Font = font;
                field.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, width, height));
                var arrow = new System.Drawing.Rectangle(width - 31, height / 2 - 5, 15, 11);
                for (int y = 8; y < height - 8; y++)
                    for (int x = 12; x < width - 12; x++)
                        if (!arrow.Contains(x, y) && image.GetPixel(x, y).ToArgb() != GlassTheme.Field.ToArgb())
                            throw new InvalidOperationException("Bank choice field contains a seam or gap at " + x + "," + y +
                                "; width=" + width + "; height=" + height + "; font=" + size + "; enabled=" + enabled);
            }
            using (var parent = new MetalPanel { Size = new System.Drawing.Size(1000, 150), Padding = System.Windows.Forms.Padding.Empty })
            using (var background = new System.Drawing.Bitmap(1000, 150))
            using (var actual = new System.Drawing.Bitmap(1000, 150))
            {
                parent.DrawToBitmap(background, parent.ClientRectangle);
                foreach (int width in new[] { 220, 480, 900 })
                foreach (int height in new[] { 52, 70 })
                foreach (float size in new[] { 9.75f, 12.5f, 16f })
                foreach (bool enabled in new[] { false, true })
                using (var field = new GlassChoiceField { Location = new System.Drawing.Point(24, 30),
                    Size = new System.Drawing.Size(width, height), Enabled = enabled })
                using (var font = new System.Drawing.Font("맑은 고딕", size))
                {
                    field.Font = font; parent.Controls.Add(field);
                    parent.DrawToBitmap(actual, parent.ClientRectangle);
                    foreach (var corner in new[] { new System.Drawing.Point(0, 0), new System.Drawing.Point(width - 1, 0),
                        new System.Drawing.Point(0, height - 1), new System.Drawing.Point(width - 1, height - 1) })
                    {
                        int x = field.Left + corner.X, y = field.Top + corner.Y;
                        if (actual.GetPixel(x, y) != background.GetPixel(x, y))
                            throw new InvalidOperationException("Bank selector paints a dark corner instead of its parent surface: " + corner);
                    }
                    parent.Controls.Remove(field);
                }
            }
            Console.WriteLine("UI PASS: bank selector interior and parent-background corners match across 36 size/font/enabled states.");
        }
        public static void VerifyInstalledUI()
        {
            VerifyChoiceFieldSurface();
            VerifyResizePainting();
            var notice = new HangarNotice { key = "site:fixture", kind = "site", newCampaign = true, title = "새로운 모드 · 격리 예시",
                summary = "1.0 · 새 미션이 추가되었습니다. · 눌러서 상세 보기", unread = true };
            using (var dialog = new NotificationDialog(new[] { notice }))
            { dialog.Show(); dialog.VerifyFixture(notice); dialog.Close(); }
            using (var dialog = new NotificationDialog(new HangarNotice[0]))
            { dialog.Show(); dialog.Close(); }
            using (var button = new NotificationButton())
            using (var image = new System.Drawing.Bitmap(52, 48))
            {
                button.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, 52, 48));
                var quiet = image.GetPixel(39, 11);
                button.UnreadCount = 1;
                button.DrawToBitmap(image, new System.Drawing.Rectangle(0, 0, 52, 48));
                var unread = image.GetPixel(39, 11);
                if (quiet == unread || unread.R < 180 || unread.G > 100 || unread.B > 120)
                    throw new InvalidOperationException("Unread notification badge must turn visibly red.");
                Console.WriteLine("UI PASS: unread news adds a red indicator to the notification bell.");
            }
            using (var dialog = new AppDialog("긴 기록 · 크기 변경 검사",
                String.Join("\n", Enumerable.Range(1, 50).Select(i => i + ". 캠페인 등록·다운로드 작업 기록 · 긴 한글 설명과 경로를 창 크기에 맞춰 표시합니다."))))
            { dialog.Show(); dialog.VerifyFixture(); dialog.Close(); }
            string root = Path.Combine(Path.GetTempPath(), "campaign-installed-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using (var saved = new System.Windows.Forms.Form())
            using (var restored = new System.Windows.Forms.Form())
            {
                saved.Size = new System.Drawing.Size(1000, 780);
                var area=System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                saved.StartPosition=System.Windows.Forms.FormStartPosition.Manual;
                saved.Location=new System.Drawing.Point(area.Left+30,area.Top+40);
                string preference = Path.Combine(root, "window.json");
                WindowSize.Save(saved, preference); WindowSize.Restore(restored, preference);
                if (restored.Bounds != saved.Bounds) throw new InvalidOperationException("Last window bounds were not restored.");
                Data.Write(preference,new WindowPreference {left=100000,top=100000,width=1000,height=780});
                WindowSize.Restore(restored,preference);
                if(!System.Windows.Forms.Screen.AllScreens.Any(s=>s.WorkingArea.Contains(restored.Bounds)))
                    throw new InvalidOperationException("Offscreen saved window must be moved inside a connected screen.");
                Console.WriteLine("UI PASS: main window position/size persist; disconnected-screen positions remain reachable.");
            }
            System.Drawing.Rectangle remembered;
            using(var dialog=new ThemeForm {Text="geometry-fixture",GeometryDirectoryForCheck=root}){
                dialog.Show();dialog.Bounds=new System.Drawing.Rectangle(System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Left+70,
                    System.Windows.Forms.Screen.PrimaryScreen.WorkingArea.Top+80,620,420);
                remembered=dialog.Bounds;dialog.Close();
            }
            using(var dialog=new ThemeForm {Text="geometry-fixture",GeometryDirectoryForCheck=root}){
                dialog.Show();
                if(dialog.Bounds!=remembered)throw new InvalidOperationException("Auxiliary window bounds did not survive close/reopen.");
                dialog.Close();
            }
            Console.WriteLine("UI PASS: auxiliary window position and size survive actual close/reopen in isolated profile.");
            using (var sourceImage = new System.Drawing.Bitmap(1920, 1080))
            using (var encoded = new MemoryStream())
            {
                sourceImage.SetPixel(1919, 1079, System.Drawing.Color.Cyan);
                sourceImage.Save(encoded, System.Drawing.Imaging.ImageFormat.Png); encoded.Position = 0;
                string file = Path.Combine(root, "thumbnail-original.png");
                ThumbnailStore.SaveOriginal(encoded, file);
                using (var decoded = new System.Drawing.Bitmap(file))
                    Assert(decoded.Width == 1920 && decoded.Height == 1080 && decoded.GetPixel(1919, 1079).ToArgb() == System.Drawing.Color.Cyan.ToArgb(),
                        "thumbnail cache preserves original 1920x1080 resolution and edge pixels");
            }
            var lib = new Library(Path.Combine(root, "library"));
            string game = Path.Combine(root, "fixture-game"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            var a = lib.Import(Zip(root, "A", "A", false), "a");
            var b = lib.Import(Zip(root, "B", "B", false), "b");
            lib.Activate(a.id, game, true, null); lib.Activate(b.id, game, true, null);
            lib.Import(Zip(root, "B-update", "B2", false, "2.0"), "b");
            Campaign openedVersion = null;
            using (var versionDialog = new VersionManagementDialog(lib.Installed("b"), lib.Versions("b"), c => openedVersion = c))
            {
                versionDialog.Show();
                versionDialog.VerifyFixture(b);
                Assert(openedVersion != null && openedVersion.versionKey == b.versionKey,
                    "folder button routes the selected previous version without changing active campaign");
                versionDialog.Close();
            }
            Campaign clickedPackage = null, removalPackage = null, updatePackage = null, informationPackage = null;
            using (var image = new System.Drawing.Bitmap(384, 240))
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(1100, 720) })
            using (var view = new InstalledView(lib, c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(image),
                c => { lib.Activate(c.id, game, true, null, c.versionKey); return System.Threading.Tasks.Task.FromResult(true); }, () => { },
                link: c => clickedPackage = c, remove: c => removalPackage = c, update: c => updatePackage = c, information: c => informationPackage = c))
            {
                form.Controls.Add(view);
                view.Reload(new Campaign[0]);
                form.Show(); view.VerifyFixture(b, a.id);
                view.VerifyCategorySections();
                view.VerifyUpdateBorders(b, a);
                var fresh = lib.Import(Zip(root, "Fresh", "fresh", false), "fresh");
                view.VerifyImported(fresh);
                view.VerifyInformationClick();
                Assert(informationPackage != null && informationPackage.id == fresh.id, "site information menu routes selected campaign and removes duplicate update menu");
                view.VerifyRemovalClick();
                Assert(removalPackage != null && removalPackage.id == fresh.id && lib.Installed(fresh.id) != null,
                    "direct removal button in large detail panel selects preview and does not delete before confirmation");
                view.VerifyArtworkClick();
                Assert(clickedPackage != null && clickedPackage.id == fresh.id && lib.Current().active == a.id,
                    "missing artwork click opens link action for preview without activating");
                Assert(lib.Current().active == a.id, "displaying new import does not activate it");
                lib.LinkSite(fresh.id, "fresh-site");
                view.VerifyLinkedVersion(fresh, "fresh-site");
                view.VerifyCredits(fresh, "fresh-site");
                view.VerifyUpdateButton(fresh, "fresh-site");
                Assert(updatePackage != null && updatePackage.id == fresh.id && lib.Current().active == a.id,
                    "update click selects preview without automatic game activation");
                var swarm = lib.Import(Zip(root, "Swarm frame", "swarm", false, "1.0", "hots"), "swarm-frame");
                view.Reload(new Campaign[0]);
                view.VerifyCommonFrame(fresh, swarm);
                form.Close();
            }
            using (var image = new System.Drawing.Bitmap(384, 240))
            using (var form = new System.Windows.Forms.Form { ClientSize = new System.Drawing.Size(1280, 840) })
            using (var view = new InstalledView(lib,
                c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(image),
                c => System.Threading.Tasks.Task.FromResult(true), () => { },
                external: () => new[] { new Campaign { id = "preview-external-qa", slot = "external", kind = "external",
                    name = "전용 미리보기 정렬 확인", version = "1", launcherMap = "fixture.SC2Map" } }))
            {
                form.Controls.Add(view);
                view.Reload(new Campaign[0]); form.Show();
                view.VerifyPreviewHeaderAlignment(form);
                form.Close();
            }
            Console.WriteLine("UI fixture root: " + root + "; no real game or library access.");
            using (var work = new WorkDialog("작업 진행 검사"))
            { work.Show(); work.VerifyFixture(); work.Finish(); }
            using (var image = new System.Drawing.Bitmap(384, 240))
            using (var picker = new SitePicker(new[] {
                new Campaign { id = "picker-a", name = "선택 캠페인 A", categories = new[] { "gallery", "rogue" }, @base = "자유의 날개" },
                new Campaign { id = "picker-b", name = "선택 캠페인 B", categories = new[] { "gallery" }, @base = "자유의 날개" } },
                c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(image), null, true, "wol"))
            { picker.Show(); picker.VerifyFixture(); picker.Close(); }
            var recommendation = new Campaign { id = "hehestan", name = "로그 HEHESTAN", categories = new[] { "rogue" }, @base = "커스텀 캠페인" };
            using (var confirmation = new ApplyConfirmation(b, game))
            {
                confirmation.Show();
                confirmation.CapturePreview(Path.Combine(root, "apply-warning.png"));
                confirmation.VerifyFixture(); confirmation.Close();
            }
            foreach (var slotPath in new[] { new[] { "wol", @"Maps\Campaign" }, new[] { "hots", @"Maps\Campaign\swarm" },
                new[] { "lotv", @"Maps\Campaign\void" }, new[] { "nco", @"Maps\Campaign\nova" } })
            {
                var mapsOnly = new Campaign { name = "맵 적용 위치 검사", slot = slotPath[0],
                    files = new System.Collections.Generic.List<OwnedFile> {
                        new OwnedFile { path = slotPath[1] + @"\mission.SC2Map" },
                        new OwnedFile { path = slotPath[1] + @"\unpacked.SC2Map\MapInfo" } } };
                using (var confirmation = new ApplyConfirmation(mapsOnly, game))
                {
                    confirmation.Show();
                    confirmation.VerifyFixture(); confirmation.Close();
                }
            }
            var modsOnly = new Campaign { name = "모드 적용 위치 검사",
                files = new System.Collections.Generic.List<OwnedFile> {
                    new OwnedFile { path = @"Mods\One.SC2Mod" }, new OwnedFile { path = @"Mods\Two.SC2Mod\Base.SC2Data\GameData.xml" } } };
            using (var confirmation = new ApplyConfirmation(modsOnly, game))
            {
                confirmation.Show();
                confirmation.VerifyFixture(); confirmation.Close();
            }
            var huge = new Campaign { name = "ESKR-style test", slot = "hots",
                files = Enumerable.Range(0, 5000).Select(i => new OwnedFile {
                    path = @"Maps\Campaign\swarm\_notes\history\test" + i + ".txt" }).ToList() };
            using (var confirmation = new ApplyConfirmation(huge, game))
            {
                confirmation.Show(); confirmation.VerifyFixture(); confirmation.Close();
            }
            Console.WriteLine("UI PASS: apply confirmation shows only the mission-exit warning, including 5000-file packages.");
            using (var picker = new DistributionLinkPicker(recommendation, new[] {
                new CampaignDownload { url = "https://drive.google.com/file/d/fixture/view", label = "한국어 배포 파일" },
                new CampaignDownload { url = "https://example.com/release.zip", label = "직접 ZIP" } }))
            { picker.Show(); picker.VerifyFixture(); picker.Close(); }
            using (var dialog = new RemovalDialog(recommendation, 3)) { dialog.Show(); dialog.VerifyFixture(); dialog.Close(); }
            using (var warning = new RemovalWarning("격리 예시 · 실제 게임 변경 없음")) { warning.Show(); warning.VerifyFixture(); warning.Close(); }
            using (var picker = new SitePicker(new[] { recommendation }, c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(null),
                null, true, "wol", new[] { recommendation }))
            { picker.Show(); picker.VerifyFixture(); picker.Close(); }
            using (var picker = new SitePicker(new[] { recommendation }, c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(null),
                null, true, "wol", new Campaign[0]))
            { picker.Show(); picker.VerifyFixture(); picker.Close(); }
            var secondRecommendation = new Campaign { id = "star-rogue", name = "스타크래프트 로그", @base = "커스텀 캠페인" };
            using (var picker = new SitePicker(new[] { recommendation, secondRecommendation }, c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(null),
                null, true, "wol", new[] { recommendation, secondRecommendation }))
            { picker.Show(); picker.VerifyFixture(); picker.Close(); }
        }
        internal sealed class MismatchAnswer : IDisposable
        {
            readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 30 };
            readonly DateTime started = DateTime.UtcNow;
            bool answered;
            Exception failure;
            public MismatchAnswer(System.Windows.Forms.Form owner, string campaignTitle, string zipTitle, bool yes, string output)
            {
                timer.Tick += delegate
                {
                    var dialog = owner.OwnedForms.OfType<AppDialog>().FirstOrDefault();
                    if (dialog == null)
                    {
                        if ((DateTime.UtcNow - started).TotalSeconds > 8) { failure = new TimeoutException("Mismatch confirmation did not open."); timer.Stop(); }
                        return;
                    }
                    timer.Stop();
                    try
                    {
                        var no = (System.Windows.Forms.Button)dialog.CancelButton;
                        var accept = no.Parent.Controls.OfType<System.Windows.Forms.Button>().Single(button => button.Text == "예");
                        Assert(no.Text == "아니오" && dialog.AcceptButton == dialog.CancelButton,
                            "mismatch confirmation has Yes/No and defaults to No");
                        Assert(dialog.MessageText.StartsWith("해당 캠페인은 " + campaignTitle + " 캠페인입니다.\n적용하시겠습니까?", StringComparison.Ordinal) &&
                            dialog.MessageText.Contains("ZIP 적용 대상: " + zipTitle),
                            "mismatch confirmation names the selected campaign and the unchanged ZIP target");
                        if (output != null)
                            using (var image = new System.Drawing.Bitmap(dialog.Width, dialog.Height))
                            {
                                dialog.DrawToBitmap(image, new System.Drawing.Rectangle(System.Drawing.Point.Empty, image.Size));
                                image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
                            }
                        answered = true;
                        (yes ? accept : no).PerformClick();
                    }
                    catch (Exception error) { failure = error; dialog.DialogResult = System.Windows.Forms.DialogResult.Cancel; }
                };
                timer.Start();
            }
            public void Verify()
            {
                if (failure != null) throw failure;
                Assert(answered, "mismatch confirmation was displayed and answered");
            }
            public void Dispose() { timer.Dispose(); }
        }
        public static async System.Threading.Tasks.Task VerifyRemovalZipCheck(Hangar form, string root)
        {
            using (var ordinary = new WorkDialog("일반 진행창 검사"))
            { ordinary.Show(form); ordinary.VerifyFixture(); ordinary.Finish(); }
            string downloads = Path.Combine(root, "downloads");
            Directory.CreateDirectory(Path.Combine(downloads, "nested"));
            var library = new Library(Path.Combine(root, "library"));
            string zip = Zip(downloads, "Owned", "owned", false);
            var item = library.Import(zip, "zip-check");
            string duplicate = Path.Combine(downloads, @"nested\renamed.zip");
            File.Copy(zip, duplicate);
            string sameSize = Path.Combine(downloads, "different-same-size.zip");
            var changed = File.ReadAllBytes(zip); changed[0] ^= 1; File.WriteAllBytes(sameSize, changed);
            string large = Path.Combine(downloads, "unrelated-large.zip");
            File.WriteAllBytes(large, new byte[3 * 1024 * 1024]);
            // The isolated host owns an active WebView2 user-data folder beneath
            // root. Those transient Chromium journals can be locked or change
            // while the test runs; they are not campaign ZIPs or library records.
            Func<string[]> owned = () => Directory.GetFiles(downloads, "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(library.Root, "*", SearchOption.AllDirectories)).ToArray();
            var before = owned().ToDictionary(path => path, Data.Hash);
            using (var locked = File.Open(large, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var matches = library.RemovalZips(item.id, new[] { downloads });
                Assert(matches.Count == 2 && matches.Any(match => match.path == zip) && matches.Any(match => match.path == duplicate),
                    "ZIP checks find exact renamed copies, reject same-size different bytes and skip unrelated sizes without opening them");
            }
            using (var cancellation = new System.Threading.CancellationTokenSource())
            {
                int visited = 0;
                bool cancelled = false;
                try
                {
                    library.RemovalZips(item.id, new[] { downloads }, value => {
                        if (value.stage.StartsWith("ZIP 후보 찾는 중", StringComparison.Ordinal) && ++visited == 2) cancellation.Cancel();
                    }, cancellation.Token);
                }
                catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && visited == 2, "ZIP directory discovery stops cooperatively before hashing or deletion");
            }
            string missing = Path.Combine(root, "registered-source-moved.zip");
            File.Move(zip, missing);
            var movedMatches = library.RemovalZips(item.id, new[] { downloads });
            Assert(movedMatches.Count == 1 && movedMatches[0].path == duplicate,
                "missing registered ZIPs do not suppress discovery of matching unregistered copies");
            File.Move(missing, zip);
            Assert(before.Count == owned().Length &&
                before.All(pair => File.Exists(pair.Key) && Data.Hash(pair.Key) == pair.Value),
                "ZIP discovery and cancellation leave all input files and library records unchanged");
            library.Import(zip, "shared-zip");
            Assert(library.RemovalZips(item.id, new[] { downloads }).Count == 0,
                "ZIPs shared with another campaign remain excluded from deletion");
            using (var cancellation = new System.Threading.CancellationTokenSource())
            {
                long read = 0, total = 0;
                bool cancelled = false;
                try
                {
                    Data.Hash(large, cancellation.Token, (completed, length) => {
                        read = completed; total = length;
                        if (completed > 0) cancellation.Cancel();
                    });
                }
                catch (OperationCanceledException) { cancelled = true; }
                Assert(cancelled && read > 0 && read < total, "Stop interrupts an individual large-file hash before all bytes are read");
            }
            using (var stream = File.OpenRead(large))
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Assert(Data.Hash(large, System.Threading.CancellationToken.None, (completed, total) => { }) ==
                    BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(),
                    "progress-aware hashing matches the independent SHA256 computation");
            using (var cancellation = new System.Threading.CancellationTokenSource())
            using (var dialog = new WorkDialog("삭제할 ZIP 확인", cancellation.Cancel))
            {
                dialog.Show(form); form.Enabled = false;
                try { dialog.VerifyCancellation(Path.Combine(root, "zip-check-stop.png")); }
                finally { form.Enabled = true; }
                Assert(cancellation.IsCancellationRequested, "Stop button signals the cancellation source");
            }
            await form.VerifyRemovalZipWork(zip);
            Console.WriteLine("REMOVAL ZIP CHECK PASS: one-pass traversal; verified size filtering; same-size hash checks; missing sources; mid-file cancellation; usable Stop; no deletion.");
        }
        static void VerifyMetadataIdentity(string root)
        {
            var site = new Campaign { id = "perpetual-randomizer", name = "자날 로그라이크 · 퍼페추얼 랜더마이저", @base = "자유의 날개" };
            var library = new Library(Path.Combine(root, "metadata-identity"));
            string rogue1 = Zip(root, "identity-rogue-1", "rogue-one", false, "0.62.0",
                title: "WoLRoguePerpetual_Randomizer", author: "Shirakami_Fubuki");
            string rogue2 = Zip(root, "identity-rogue-2", "rogue-two", false, "0.62.1",
                title: "WoLRoguePerpetual_Randomizer", author: "Shirakami_Fubuki");
            string original1 = Zip(root, "identity-original-1", "original-one", false, "1.5.0",
                title: "Perpetual Randomizer KR", author: "fishmaster26");
            string original2 = Zip(root, "identity-original-2", "original-two", false, "1.5.1",
                title: "Perpetual_Randomizer_koKR", author: "fishmaster26");
            var originalMetadata = Library.InspectZip(original1);
            var rogue = library.ImportForSite(rogue1, site);
            library.ImportForSite(rogue2, site);
            Assert(library.MatchSiteMetadata(originalMetadata, new[] { site }) == null &&
                !library.AcceptsSiteMetadata(originalMetadata, site),
                "real original title/author never auto-links to known rogue metadata despite shared site ID");
            var original = library.ImportForSite(original1, site);
            Assert(original.id != rogue.id && library.CatalogueId(original.id) == original.id &&
                library.Versions(rogue.id).Count == 2 && library.Versions(original.id).Count == 1,
                "explicit same-site download still stores conflicting metadata as a separate unlinked mod");
            var updated = library.ImportLocalMetadata(original2, Library.InspectZip(original2));
            Assert(updated.id == original.id && library.Versions(original.id).Count == 2 &&
                SiteMatcher.SameMetadata(original, updated) && library.Installed(rogue.id).version == "0.62.1",
                "language suffix/separator changes and new version retain the original mod history only");
            Assert(library.AutoLink(updated, new[] { site }) == null &&
                library.LinkedSite(updated, new[] { site }) == null,
                "restart auto-link and learned bindings cannot reconnect the separate original to rogue");
            Assert(!SiteMatcher.SameMetadata(original, new Campaign { name = original.name, slot = "wol", sourceAuthor = "another-author" }) &&
                !SiteMatcher.SameMetadata(original, new Campaign { name = original.name, slot = "hots", sourceAuthor = original.sourceAuthor }),
                "same title with different named author or target campaign is not the same mod");
            Assert(SiteMatcher.SameMetadata(original, new Campaign { name = "Perpetual Randomizer", slot = "wol" }),
                "missing optional author alone does not split an otherwise identical title and target");
            Assert(SiteMatcher.MetadataId(original) == SiteMatcher.MetadataId(updated),
                "local metadata identity does not depend on archive filename, archive hash or version");
            var mixed = new Library(Path.Combine(root, "mixed-identity"));
            mixed.Import(rogue1, site.id); mixed.Import(rogue2, site.id);
            var misplaced = mixed.Import(original1, site.id);
            var activeBefore = mixed.Current();
            var before = Directory.GetFiles(mixed.VersionFolder(misplaced), "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != "manifest.json")
                .ToDictionary(path => path.Substring(mixed.VersionFolder(misplaced).Length + 1), Data.Hash);
            string newId = mixed.SeparateStoredVersion(site.id, misplaced.versionKey);
            var separated = mixed.Installed(newId);
            Assert(mixed.Installed(site.id).name == "WoLRoguePerpetual_Randomizer" &&
                mixed.Installed(site.id).version == "0.62.1" && separated.name == "Perpetual Randomizer KR" &&
                separated.sourceAuthor == "fishmaster26" && mixed.Versions(site.id).Count == 2 &&
                mixed.Versions(newId).Single().id == newId,
                "mixed history repair restores rogue latest metadata and moves original into its own identity");
            string separatedFolder = mixed.VersionFolder(separated);
            Assert(before.All(pair => Data.Hash(Path.Combine(separatedFolder, pair.Key)) == pair.Value) &&
                Directory.GetFiles(separatedFolder, "*", SearchOption.AllDirectories).Length == before.Count + 1,
                "history split preserves every payload/deploy byte without deleting or reapplying the mod");
            var registered = Data.Read<System.Collections.Generic.List<RegisteredZip>>(Path.Combine(mixed.Root, "source-zips.json"));
            Assert(registered.Single(zip => zip.hash == misplaced.versionKey).campaignId == newId &&
                registered.Where(zip => zip.hash != misplaced.versionKey).All(zip => zip.campaignId == site.id) &&
                mixed.Current().active == activeBefore.active && !File.Exists(Path.Combine(mixed.Root, "active.json")) &&
                Directory.GetDirectories(mixed.Root, "identity-backup-*").Length == 1,
                "history split updates only matching ZIP ownership, keeps active state and saves original metadata backup");
            Assert(mixed.MatchSiteMetadata(Library.InspectZip(rogue2), new[] { site }) == site &&
                mixed.MatchSiteMetadata(originalMetadata, new[] { site }) == null,
                "repaired histories still auto-recognize rogue updates while keeping original independent");
            foreach (int faultAt in new[] { 1, 2, 3 })
            {
                var interrupted = new Library(Path.Combine(root, "identity-rollback-" + faultAt));
                interrupted.Import(rogue1, site.id);
                var wrong = interrupted.Import(original1, site.id);
                var snapshot = Directory.GetFiles(interrupted.Root, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => path, Data.Hash);
                Reject(() => interrupted.SeparateStoredVersion(site.id, wrong.versionKey, step => {
                    if (step == faultAt) throw new IOException("identity repair fixture failure");
                }), "identity repair reports injected failure at stage " + faultAt);
                Assert(snapshot.All(pair => File.Exists(pair.Key) && Data.Hash(pair.Key) == pair.Value) &&
                    interrupted.All().Count == 1 && interrupted.Versions(site.id).Count == 2 &&
                    !Directory.Exists(Path.Combine(interrupted.Root, "packages", SiteMatcher.MetadataId(wrong))),
                    "identity repair rolls back every original file and ZIP index at stage " + faultAt);
            }
            Reject(() => mixed.SeparateStoredVersion(site.id, rogue.versionKey),
                "same-mod historical versions cannot be split as different identities");
            string activePath = Path.Combine(mixed.Root, "active.json");
            Data.Write(activePath, new State { active = site.id, versionKey = rogue.versionKey });
            Reject(() => mixed.SeparateStoredVersion(site.id, rogue.versionKey),
                "identity repair refuses a currently applied package before changing it");
        }
        public static async System.Threading.Tasks.Task VerifyMetadataImport(Hangar form, string root)
        {
            var site = new Campaign { id = "perpetual-randomizer", name = "자날 로그라이크 · 퍼페추얼 랜더마이저", @base = "자유의 날개",
                categories = new[] { "gallery" } };
            string rogue = Zip(root, "ui-identity-rogue", "rogue", false, "0.62.1",
                title: "WoLRoguePerpetual_Randomizer", author: "Shirakami_Fubuki");
            string original = Zip(root, "ui-identity-original", "original", false, "1.5.0",
                title: "Perpetual Randomizer KR", author: "fishmaster26");
            string update = Zip(root, "ui-identity-original-update", "original-update", false, "1.5.0",
                title: "Perpetual_Randomizer_koKR", author: "fishmaster26");
            await form.VerifyMetadataImport(site, rogue, original, update);
        }
        public static async System.Threading.Tasks.Task VerifyResponsiveApply(Hangar form, string root)
        {
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, @"Maps\Campaign"));
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            Directory.CreateDirectory(Path.Combine(game, "Banks"));
            Directory.CreateDirectory(Path.Combine(game, "Saves"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            File.WriteAllText(Path.Combine(game, @"Maps\Campaign\mission.SC2Map"), "fixture-original-map");
            File.WriteAllText(Path.Combine(game, @"Mods\Shared.SC2Mod"), "fixture-original-mod");
            string bank = Path.Combine(game, @"Banks\keep.SC2Bank"), save = Path.Combine(game, @"Saves\keep.SC2Save");
            File.WriteAllText(bank, "preserved-bank"); File.WriteAllText(save, "preserved-save");
            var zips = new[] { Zip(root, "Responsive1", "responsive-1", false, "1.0"),
                Zip(root, "Responsive2", "responsive-2", false, "2.0"), Zip(root, "ResponsiveOther", "other", false) };
            var hashes = zips.Select(Data.Hash).ToArray();
            await form.VerifyResponsiveApply(game, zips, root);
            Assert(zips.Select(Data.Hash).SequenceEqual(hashes), "responsive apply preserves every input ZIP");
            Assert(File.ReadAllText(bank) == "preserved-bank" && File.ReadAllText(save) == "preserved-save",
                "responsive apply preserves banks and saves");
        }
        public static async System.Threading.Tasks.Task VerifyAutoCleanup(Hangar form, string root)
        {
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, @"Maps\Campaign"));
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            Directory.CreateDirectory(Path.Combine(game, "Banks"));
            Directory.CreateDirectory(Path.Combine(game, "Saves"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture");
            File.WriteAllText(Path.Combine(game, @"Maps\Campaign\mission.SC2Map"), "original");
            File.WriteAllText(Path.Combine(game, @"Mods\Shared.SC2Mod"), "original-shared");
            string bank = Path.Combine(game, @"Banks\keep.SC2Bank"), save = Path.Combine(game, @"Saves\keep.SC2Save");
            File.WriteAllText(bank, "preserved-bank"); File.WriteAllText(save, "preserved-save");
            var zips = new[] { Zip(root, "Cleanup1", "cleanup-1", false, "1.0"), Zip(root, "Cleanup2", "cleanup-2", false, "2.0"),
                Zip(root, "Cleanup3", "cleanup-3", false, "3.0"), Zip(root, "CleanupOther", "other-campaign", false) };
            var hashes = zips.Select(Data.Hash).ToArray();
            await form.VerifyAutoCleanup(game, zips, Path.Combine(root, "auto-cleanup-setting.png"));
            Assert(zips.Select(Data.Hash).SequenceEqual(hashes), "automatic cleanup preserves every downloaded ZIP");
            Assert(File.ReadAllText(bank) == "preserved-bank" && File.ReadAllText(save) == "preserved-save",
                "automatic cleanup preserves banks and saves");
            Console.WriteLine("AUTO CLEANUP PASS: default off; independent persistence; off/cancel/failure preserve versions; success-only cleanup; ZIP/game/bank/save integrity.");
        }
        public static async System.Threading.Tasks.Task VerifySitePicker(Hangar form, string root)
        {
            string[] slots = { "wol", "hots", "lotv", "nco" };
            string[] bases = { "자유의 날개", "군단의 심장", "공허의 유산", "노바 비밀 작전" };
            string[] titles = { "자날", "군심", "공유", "노바" };
            var sites = Enumerable.Range(0, slots.Length).Select(i => new Campaign {
                id = "picker-" + slots[i], name = titles[i] + " 선택 검사", @base = bases[i], slot = slots[i],
                categories = new[] { "gallery" }, version = "격리 예시" }).ToArray();
            using (var image = new System.Drawing.Bitmap(384, 240))
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    int other = (i + 1) % slots.Length;
                    using (var picker = new SitePicker(sites, c => System.Threading.Tasks.Task.FromResult<System.Drawing.Image>(image),
                        sites[other], true, slots[i]))
                    {
                        picker.Show(form); picker.VerifyFixture();
                        picker.VerifySlotSelection(sites[i], sites[other], titles[i], titles[other], root);
                        picker.Close();
                    }
                }
            }
            var library = new Library(Path.Combine(root, "library"));
            var original = library.ImportForSite(Zip(root, "Original-target", "original", false, "1.0", "wol"), sites[0]);
            string zip = Zip(root, "LotV-target", "lotv", false, "2.0", "lotv");
            Reject(() => library.ImportForSite(zip, sites[0]), "unconfirmed slot mismatch remains rejected");
            Assert(library.Versions(sites[0].id).Count == 1, "rejected mismatch preserves the original version");
            var approved = library.ImportForSite(zip, sites[0], null, true);
            Assert(approved.slot == "lotv" && approved.files.Any(file => file.path == @"Maps\Campaign\void\mission.SC2Map") &&
                approved.id != original.id && library.Versions(sites[0].id).Count == 1 &&
                library.Versions(approved.id).Count == 1 && File.Exists(Path.Combine(library.VersionFolder(original), "manifest.json")),
                "approved site mismatch preserves exact ZIP paths but different metadata stays in a separate history");
            var multiple = new Campaign { id = "multiple-targets", @base = bases[0], campaignGroups = new[] { bases[0], bases[2] } };
            Assert(library.ImportForSite(zip, multiple).slot == "lotv", "matching declared campaign groups need no override");
            using (var answer = new MismatchAnswer(form, "자날", "공유", false, null))
            {
                await form.VerifySlotMismatchImport(zip, sites[0], false); answer.Verify();
            }
            using (var answer = new MismatchAnswer(form, "자날", "공유", true, Path.Combine(root, "import-mismatch.png")))
            {
                await form.VerifySlotMismatchImport(zip, sites[0], true); answer.Verify();
            }
            Console.WriteLine("SITE PICKER PASS: four initial tabs; normal selection; mismatch No/Yes; exact ZIP targets; isolated import; original versions preserved.");
        }
        public static async System.Threading.Tasks.Task VerifyZipDrop(Hangar form)
        {
            string root = Path.Combine(Path.GetTempPath(), "campaign-drop-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            foreach (string slot in InstalledSelection.Slots)
            {
                string zip = Zip(root, "Drop-" + slot, "drop-" + slot, false, "1.0", slot);
                await form.VerifyZipDrop(zip, true);
                await form.VerifyZipDrop(zip, false, false);
                if (slot == "wol")
                {
                    await form.VerifyZipDrop(zip, false, false, "archive");
                    await form.VerifyZipDrop(zip, false, false, "settings");
                }
            }
            string nonCcm = Path.Combine(root, "Standalone No CCM.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(nonCcm, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("Campaign/README.txt").Open()))
                writer.Write("No identifiable launcher or map; only manual fallback applies.");
            if (!Library.MissingCcmMetadata(nonCcm))
                throw new InvalidOperationException("Non-CCM ZIP must be detected before CCM metadata parsing.");
            await form.VerifyNonCcmZip(nonCcm);
            string autoZip = Path.Combine(root, "Recognized Standalone.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(autoZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var map = archive.CreateEntry("Campaign/Launcher.SC2Map").Open())
                    map.Write(new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 }, 0, 8);
                using (var map = archive.CreateEntry("Campaign/Mission01.SC2Map").Open())
                    map.Write(new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 }, 0, 8);
            }
            string standaloneGame = Path.Combine(root, "standalone-game");
            Directory.CreateDirectory(standaloneGame);
            File.WriteAllText(Path.Combine(standaloneGame, "StarCraft II.exe"), "isolated fixture");
            await form.VerifyStandaloneZipAuto(autoZip, standaloneGame);
            string darkZip = Path.Combine(root, "The Dark Story.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(darkZip, System.IO.Compression.ZipArchiveMode.Create))
                foreach (string name in new[] { "TDS_Launcher.SC2Map", "tMission_1.SC2Map", "zMission_1.SC2Map" })
                    using (var map = archive.CreateEntry(name).Open())
                        map.Write(new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 }, 0, 8);
            string darkFolder = Path.Combine(standaloneGame, "Maps", "The Dark Story");
            Directory.CreateDirectory(darkFolder);
            foreach (string name in new[] { "TDS_Launcher.SC2Map", "tMission_1.SC2Map", "zMission_1.SC2Map" })
                File.WriteAllBytes(Path.Combine(darkFolder, name), new byte[] { 77, 80, 81, 26, 9, 8, 7, 6, 5 });
            await form.VerifyStandaloneReplacementChoice(darkZip, standaloneGame);
            string modZip = Path.Combine(root, "Dark Story Mods.zip");
            using (var archive = System.IO.Compression.ZipFile.Open(modZip, System.IO.Compression.ZipArchiveMode.Create))
            {
                using (var mod = archive.CreateEntry("TGH_Mod.SC2Mod").Open())
                    mod.Write(new byte[] {77, 80, 81, 26, 2, 3, 4, 5, 6}, 0, 9);
            }
            string modRoot = Path.Combine(standaloneGame, "Mods");
            Directory.CreateDirectory(modRoot);
            File.WriteAllBytes(Path.Combine(modRoot, "TGH_Mod.SC2Mod"),
                new byte[] {77, 80, 81, 26, 9, 8, 7, 6, 5});
            await form.VerifyStandaloneModConflict(modZip, standaloneGame);
            var opened = new System.Collections.Generic.List<string>();
            using (var updateForm = new Hangar(Path.Combine(root, "update-route"), null, false,
                (campaign, url) => { opened.Add(campaign.id + "|" + url); return System.Threading.Tasks.Task.FromResult(0); }))
            {
                updateForm.Show();
                string zip = Zip(root, "Update route", "fixture", false);
                var site = new Campaign { id = "update-route", name = "업데이트 경로", download = "https://drive.google.com/file/d/fixture/view", @base = "자유의 날개" };
                await updateForm.VerifyUpdateRoute(zip, site);
                Assert(opened.Count == 1&&opened[0]==site.id+"|"+site.download,
                    "update request opens exact registered distribution without activation");
                site.downloads = new System.Collections.Generic.List<CampaignDownload> { new CampaignDownload { url = "https://example.com/selected.zip", label = "선택 배포 파일" } };
                using(var timer=new System.Windows.Forms.Timer {Interval=50}){
                    timer.Tick+=delegate {
                        var picker=System.Windows.Forms.Application.OpenForms.OfType<DistributionLinkPicker>().FirstOrDefault();
                        if(picker==null)return;
                        picker.ChooseFixture(1);
                    };
                    timer.Start();await updateForm.VerifyUpdateRoute(zip,site);timer.Stop();
                }
                Assert(opened.Count==2&&opened[1]==site.id+"|https://example.com/selected.zip",
                    "multiple update distributions open only the user's selected file");
                string fixtureGame = Path.Combine(root, "fixture-game"); Directory.CreateDirectory(fixtureGame);
                File.WriteAllText(Path.Combine(fixtureGame, "StarCraft II.exe"), "fixture-not-executable");
                var fixtureLibrary = new Library(Path.Combine(root, "update-route"));
                using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
                {
                    timer.Tick += delegate {
                        var confirmation = System.Windows.Forms.Application.OpenForms.OfType<ApplyConfirmation>().FirstOrDefault();
                        if (confirmation != null) confirmation.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                    };
                    timer.Start(); await updateForm.VerifyApplyCancellation(fixtureGame, fixtureLibrary.Installed(site.id));
                }
                await updateForm.VerifyApplyOption(fixtureGame, fixtureLibrary.Installed(site.id));
                updateForm.Close();
            }
        }
        public static void ExportDesign(string output, bool preview = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "campaign-design-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Directory.CreateDirectory(output);
            var lib = new Library(root);
            string game = Path.Combine(root, "fixture-game"); Directory.CreateDirectory(game);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            var a = lib.Import(Zip(root, "블랙 아웃", "A", false, "1.0.2"), "blackout");
            var b = lib.Import(Zip(root, "자날 로그라이트", "B", false, "1.4.3"), "roguelite");
            lib.Import(Zip(root, "자날 로그라이트 커스", "C", false, "1.0.0"), "roguelite-curse");
            lib.Activate(b.id, game, true, null); lib.Activate(a.id, game, true, null);
            string imagesRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\design\mockups\campaign-library"));
            var images = new System.Collections.Generic.Dictionary<string, System.Drawing.Image>();
            try
            {
                foreach (string id in new[] { "blackout", "roguelite", "roguelite-curse" })
                    images[id] = System.Drawing.Image.FromFile(Path.Combine(imagesRoot, id + ".png"));
                using (var form = new Hangar(root, c => System.Threading.Tasks.Task.FromResult(images.ContainsKey(c.id) ? images[c.id] : null), false))
                {
                    if (preview)
                    {
                        form.Shown += delegate { form.CaptureDesign(output); };
                        System.Windows.Forms.Application.Run(form);
                    }
                    else { form.Show(); form.CaptureDesign(output); form.Close(); }
                }
                if (!preview)
                {
                    var items = lib.All().Select(c => new Campaign { id = c.id, name = c.name, version = c.version, @base = "자유의 날개", categories = new[] { "gallery", "rogue" } }).ToList();
                    using (var picker = new SitePicker(items, c => System.Threading.Tasks.Task.FromResult(images[c.id]), items[0], true, "wol"))
                    { picker.Show(); picker.CapturePreview(Path.Combine(output, "site-link.png")); picker.Close(); }
                    using (var picker = new SitePicker(items, c => System.Threading.Tasks.Task.FromResult(images[c.id]), null, true, "wol", items.Take(2)))
                    { picker.Show(); picker.CapturePreview(Path.Combine(output, "recommendations.png")); picker.Close(); }
                    using (var picker = new SitePicker(items, c => System.Threading.Tasks.Task.FromResult(images[c.id]), null, true, "wol", new Campaign[0]))
                    { picker.Show(); picker.CapturePreview(Path.Combine(output, "recommendations-empty.png")); picker.Close(); }
                    using (var dialog = new RemovalDialog(items[0], 3))
                    { dialog.Show(); dialog.CapturePreview(Path.Combine(output, "removal.png")); dialog.Close(); }
                    using (var warning = new RemovalWarning("격리 예시 · 실제 게임 변경 없음\n보관소: 예시 캠페인 / 전체 3개 버전\n게임: 예시 게임 / 기록된 모드 파일·생성 하위 폴더\n기존 CCM에서 별도로 설치한 미등록 폴더는 추측해서 삭제하지 않습니다."))
                    { warning.Show(); warning.CapturePreview(Path.Combine(output, "removal-warning.png")); warning.Close(); }
                    using (var confirmation = new ApplyConfirmation(items[0], game))
                    {
                        confirmation.Show();
                        confirmation.CapturePreview(Path.Combine(output, "apply-confirmation.png"));
                        confirmation.SetDoNotAskAgainForTest(true);
                        confirmation.CapturePreview(Path.Combine(output, "apply-confirmation-checked.png"));
                        confirmation.Close();
                    }
                    using (var connection = new ExternalCampaignDialog(new Campaign {
                        id = "fixture-external-map", kind = "external", name = "설치된 캠페인 · 테스트",
                        installationMode = "manual", launchMode = "map",
                        launcherMap = Path.Combine(game, "Maps", "Campaign", "Start.SC2Map") }, game))
                    { connection.Show(); connection.CapturePreview(Path.Combine(output, "external-link-map.png")); connection.Close(); }
                    using (var connection = new ExternalCampaignDialog(new Campaign {
                        id = "fixture-external-launcher", kind = "external", name = "제작자 런처 캠페인 · 테스트",
                        installationMode = "author-launcher", launchMode = "author-launcher",
                        launcherExecutable = Path.Combine(root, "CreatorLauncher.exe") }, game))
                    { connection.Show(); connection.CapturePreview(Path.Combine(output, "external-link-launcher.png")); connection.Close(); }
                    using (var dialog = new AppDialog("보관소 변경 주의", Storage.RelocationWarning +
                        "\n\n기본 위치 사용을 권장합니다. 계속하면 새 보관소 폴더를 선택합니다.\n다운로드 ZIP 위치는 변경하지 않습니다.", "계속 · 폴더 선택"))
                    { dialog.Show(); dialog.CapturePreview(Path.Combine(output, "storage-warning.png")); dialog.Close(); }
                    using (var dialog = new AppDialog("이번 실행의 기록", "19:30 · UED 자날 한글번역 보관소 등록 완료\n19:25 · 캠페인 ZIP 다운로드 완료\n\n격리 화면 예시 · 실제 게임 변경 없음"))
                    { dialog.Show(); dialog.CapturePreview(Path.Combine(output, "activity-log.png")); dialog.Close(); }
                    using (var dialog = new AppDialog("보관소 변경 주의", Storage.RelocationWarning +
                        "\n\n기본 위치 사용을 권장합니다. 계속하면 새 보관소 폴더를 선택합니다.\n다운로드 ZIP 위치는 변경하지 않습니다.", "계속 · 폴더 선택"))
                    { dialog.Show(); dialog.ClientSize = new System.Drawing.Size(1000, 620); dialog.PerformLayout(); dialog.CapturePreview(Path.Combine(output, "storage-warning-resized.png")); dialog.Close(); }
                    using (var tile = new InstalledTile(items[0], "") { UpdateAvailable = true, Thumbnail = images[items[0].id],
                        PlayTime = "플레이 2.5시간" })
                    using (var bitmap = new System.Drawing.Bitmap(tile.Width, tile.Height))
                    { tile.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, tile.Size)); bitmap.Save(Path.Combine(output, "update-thumbnail.png")); }
                    using (var tile = new InstalledTile(new Campaign { kind = "dedicated", name = "매우 긴 전용 캠페인 이름과 추가 패치 이름까지 들어가는 테스트" }, "") {
                        Thumbnail = images[items[0].id], PlayTime = "플레이 12.6시간" })
                    using (var bitmap = new System.Drawing.Bitmap(tile.Width, tile.Height))
                    { tile.DrawToBitmap(bitmap, new System.Drawing.Rectangle(System.Drawing.Point.Empty, tile.Size)); bitmap.Save(Path.Combine(output, "playtime-thumbnail.png")); }
                    using (var picker = new DistributionLinkPicker(items[0], new[] {
                        new CampaignDownload { url = "https://drive.google.com/file/d/fixture/view", label = "한국어 배포 파일 · 격리 예시" },
                        new CampaignDownload { url = "https://example.com/release.zip", label = "별도 배포 파일 · 격리 예시" } }))
                    { picker.Show(); picker.CapturePreview(Path.Combine(output, "distribution-choice.png")); picker.Close(); }
                }
            }
            finally { foreach (var image in images.Values) image.Dispose(); }
            Console.WriteLine("Design fixture: " + root + "; example install state only; real game untouched.");
        }
    }
}
