using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class DedicatedChecks
    {
        public static void RunWc3(string source, bool remote = false, string resume = null)
        {
            var ids = new[] { "synergy-azerothbattlegrounds", "synergy-azerothgods",
                "synergy-azerothlegends", "synergy-azerothmysteries", "synergy-warchasers" };
            var releases = ids.Select(id => Data.Read<DedicatedRelease>(Path.Combine(source, id + ".json"))).ToArray();
            var expectedCounts = new[] { 5, 10, 23, 6, 5 };
            var expectedStarts = new[] { "Maps/azerothbattlegrounds/Battlegrounds.SC2Map", "Maps/azerothgods/AGLauncher.SC2Map",
                "Maps/azerothlegends/ALLauncher.SC2Map", "Maps/azerothmysteries/AMLauncher.SC2Map", "Maps/warchasers/WarChasers.SC2Map" };
            Check(releases.Select((r, i) => r.files.Count == expectedCounts[i] && r.launches.Single().path == expectedStarts[i]).All(value => value),
                "file counts and launch maps match the five original pinned campaigns");
            var sources = Data.Read<Dictionary<string, string>>(Path.Combine(source, "local-sources.json"));
            var liveDownload = (Action<string, string, long, string, Action<WorkProgress>, CancellationToken>)Delegate.CreateDelegate(
                typeof(Action<string, string, long, string, Action<WorkProgress>, CancellationToken>),
                typeof(DedicatedLibrary).GetMethod("Download", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            var remoteUrls = new HashSet<string>();
            var verifiedRemote = resume == null ? new Dictionary<string, string>() :
                Data.Read<Dictionary<string, string>>(resume);
            string root = Path.Combine(Path.GetTempPath(), "wc3-install-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Console.WriteLine("WC3_CHECK_ROOT " + root);
            var common = releases[0].files.Where(f => f.path.StartsWith("Mods/")).ToArray();
            Check(common.Length == 4 && releases.All(r => r.files.Where(f => f.path.StartsWith("Mods/"))
                .All(f => common.Any(c => c.path == f.path && c.sha256 == f.sha256 && c.bytes == f.bytes))),
                "all five real manifests use identical four shared mods");
            foreach (var release in releases) DedicatedLibrary.ValidateRelease(release);
            for (int order = 0; order < 2; order++)
            {
                string directory = Path.Combine(root, order.ToString()), game = Path.Combine(directory, "game");
                Directory.CreateDirectory(Path.Combine(game, "Support64"));
                File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
                string switcher = Path.Combine(game, "Support64/SC2Switcher_x64.exe");
                File.WriteAllText(switcher, "fixture-not-executable");
                string bank = Path.Combine(directory, "preserved.SC2Bank"), save = Path.Combine(directory, "preserved.SC2Save");
                File.WriteAllText(bank, "preserved-bank"); File.WriteAllText(save, "preserved-save");
                var library = new DedicatedLibrary(Path.Combine(directory, "storage"));
                var requested = new List<string>(); object gate = new object();
                bool publicDownloads = remote && order == 0;
                Action<DedicatedRelease, bool, CancellationToken> install = (release, corrupt, cancellation) => {
                    string work = Path.Combine(directory, "work-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    library.InstallSnapshot(release, Data.Hash(Path.Combine(source, release.id + ".json")), work, game,
                        null, cancellation, (url, target, bytes, sha, progress, token) => {
                            lock (gate) requested.Add(url);
                            if (corrupt && url.EndsWith("-maps.zip")) File.WriteAllText(target, "corrupt-fixture");
                            else if (publicDownloads && !corrupt)
                            {
                                string verified;
                                lock (gate) verifiedRemote.TryGetValue(url, out verified);
                                if (verified != null)
                                {
                                    Check(new FileInfo(verified).Length == bytes && Data.Hash(verified) == sha,
                                        "previous complete remote download still matches: " + url);
                                    File.Copy(verified, target);
                                    lock (gate) remoteUrls.Add(url);
                                    Console.WriteLine("WC3 REMOTE REUSED " + url);
                                    return;
                                }
                                var watch = System.Diagnostics.Stopwatch.StartNew();
                                Console.WriteLine("WC3 REMOTE START " + url + " bytes=" + bytes);
                                long reported = 0;
                                liveDownload(url, target, bytes, sha, value => {
                                    if (value.completed - reported < 64L * 1024 * 1024 && value.completed != bytes) return;
                                    reported = value.completed;
                                    Console.WriteLine("WC3 REMOTE PROGRESS " + Path.GetFileName(target) + " bytes=" + reported);
                                }, token);
                                lock (gate)
                                {
                                    remoteUrls.Add(url);
                                    verifiedRemote[url] = target;
                                    Data.Write(Path.Combine(root, "verified-remote.json"), verifiedRemote);
                                }
                                Console.WriteLine("WC3 REMOTE VERIFIED " + url + " seconds=" + watch.Elapsed.TotalSeconds.ToString("F2"));
                            }
                            else File.Copy(sources[url], target);
                        });
                };
                var selected = order == 0 ? releases : releases.Reverse().ToArray();
                if (order == 0)
                {
                    Reject(() => install(selected[0], true, CancellationToken.None), "corrupt real package cannot mutate game");
                    Check(library.Current().installed.Count == 0 &&
                        !selected[0].files.Any(f => File.Exists(Data.Under(game, f.path))), "corrupt transfer leaves no installed files");
                    Reject(() => install(selected[0], false, new CancellationToken(true)), "cancelled real install preserves original state");
                }
                for (int i = 0; i < selected.Length; i++)
                {
                    var release = selected[i]; requested.Clear();
                    install(release, false, CancellationToken.None);
                    Check(release.files.All(f => File.Exists(Data.Under(game, f.path)) &&
                        new FileInfo(Data.Under(game, f.path)).Length == f.bytes && Data.Hash(Data.Under(game, f.path)) == f.sha256),
                        release.id + ": all real file paths, sizes and SHA256 values installed exactly");
                    Check(i == 0 ? requested.Count == 3 :
                        requested.Count == 1 && requested[0].EndsWith("-maps.zip"),
                        release.id + ": identical shared files reuse their original verified bytes");
                    var card = new Campaign { id = release.id, kind = "dedicated" };
                    var launch = library.LaunchInfo(card, game);
                    Check(Path.GetFullPath(launch.FileName) == Path.GetFullPath(switcher) &&
                        launch.Arguments == "\"" + Path.GetFullPath(Data.Under(game, release.launches[0].path)) + "\"" &&
                        !launch.UseShellExecute,
                        release.id + ": exact installed start map selected without executing game");
                    Check(!library.NeedsRepair(card), release.id + ": complete real install needs no repair");
                }
                publicDownloads = false;
                requested.Clear(); install(selected[0], false, CancellationToken.None);
                Check(requested.Count == 0 && library.Current().installed.Count == 5,
                    "same real package reinstalls without downloads or duplicate owners");
                string shared = Data.Under(game, "Mods/WC3_CustomAssets.SC2Mod");
                byte original;
                using (var stream = new FileStream(shared, FileMode.Open, FileAccess.ReadWrite))
                { original = (byte)stream.ReadByte(); stream.Position = 0; stream.WriteByte((byte)(original ^ 1)); }
                Reject(() => install(selected[0], false, CancellationToken.None), "externally changed shared mod blocks real reinstall");
                Reject(() => library.Uninstall(selected[0].id, game), "externally changed shared mod blocks removal");
                using (var stream = new FileStream(shared, FileMode.Open, FileAccess.Write)) stream.WriteByte(original);
                Check(Data.Hash(shared) == common.Single(f => f.path == "Mods/WC3_CustomAssets.SC2Mod").sha256,
                    "external-change fixture restored to exact original bytes");
                for (int i = 0; i < selected.Length; i++)
                {
                    library.Uninstall(selected[i].id, game);
                    Check(i == selected.Length - 1 ? common.All(f => !File.Exists(Data.Under(game, f.path))) :
                        common.All(f => File.Exists(Data.Under(game, f.path)) && Data.Hash(Data.Under(game, f.path)) == f.sha256),
                        "shared mods retained until last owner, order=" + order + " remaining=" + (selected.Length - i - 1));
                }
                Check(library.Current().installed.Count == 0 && File.ReadAllText(bank) == "preserved-bank" &&
                    File.ReadAllText(save) == "preserved-save", "bank/save fixtures preserved in both real package install orders");
            }
            if (remote) Check(remoteUrls.Count == 7, "all seven distinct public ZIPs fully downloaded and verified by the real native downloader");
            Console.WriteLine("WC3 COMPLETE: five real Korean packages; two install orders; shared reuse, corruption, cancellation, external changes, start maps and uninstall protected; no live game mutation.");
        }
        public static void RunSpeed(string source)
        {
            string root = Path.Combine(Path.GetTempPath(), "uedfl-reuse-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Console.WriteLine("UEDFL SPEED ISOLATED ROOT " + root);
            var release = Data.Read<DedicatedRelease>(Path.Combine(source, "synergy-uedfl.json"));
            DedicatedLibrary.ValidateRelease(release);
            Check(release.id == "synergy-uedfl" && release.files.Count == 39 &&
                release.files.Sum(f => f.bytes) == 4552294337L, "speed fixture uses the real 4.55GB/39-file UEDFL snapshot");
            var sources = Data.Read<Dictionary<string, string>>(Path.Combine(source, "local-sources.json"));
            string game = Path.Combine(root, "game"), storage = Path.Combine(root, "storage"), work = Path.Combine(root, "work");
            Directory.CreateDirectory(game); Directory.CreateDirectory(work);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            foreach (var bundle in release.packages)
            using (var archive = ZipFile.OpenRead(sources[bundle.url]))
                foreach (var entry in bundle.entries)
                {
                    string target = Data.Under(game, entry.Value); Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var input = archive.GetEntry(entry.Key).Open())
                    using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
                }
            Check(release.files.All(f => Data.Hash(Data.Under(game, f.path)) == f.sha256), "speed fixture game bytes match all independently pinned UEDFL hashes");
            string manifestHash = Data.Hash(Path.Combine(source, "synergy-uedfl.json"));
            Data.Write(Data.Under(storage, "dedicated/installed.json"), new DedicatedState {
                gameRoot = Path.GetFullPath(game),
                installed = new List<DedicatedInstalled> { new DedicatedInstalled {
                    id = release.id, version = release.version, manifestSha256 = manifestHash, release = release } },
                originals = release.files.Select(f => new DedicatedOriginal { path = f.path }).ToList()
            });
            var library = new DedicatedLibrary(storage);
            int downloads = 0; var timer = System.Diagnostics.Stopwatch.StartNew();
            library.InstallSnapshot(release, manifestHash, work, game, null, CancellationToken.None,
                (url, target, bytes, hash, report, cancellation) => { downloads++; throw new InvalidOperationException("Matching UEDFL files must not download any ZIP."); });
            timer.Stop();
            Check(downloads == 0 && !Directory.EnumerateFiles(work, "*.zip").Any() &&
                library.Installed(release.id).release.files.Count == 39, "real UEDFL reinstall reuses all 39 files, downloads zero ZIPs and preserves its installed record");
            Check(Directory.EnumerateDirectories(Path.Combine(storage, "dedicated"), "stage-*")
                .All(path => !Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Any()),
                "unchanged UEDFL reinstallation extracts and copies no game files");
            Console.WriteLine("UEDFL REUSE MEASURED seconds=" + timer.Elapsed.TotalSeconds.ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture) + "; files=39; existingBytes=4552294337; downloadedBytes=0; isolated warm-cache disk check, no network or real game mutation.");
        }
        public static void RunAdditional(string source, bool antioch = false, bool allies = false)
        {
            string root = Path.Combine(Path.GetTempPath(), "additional-campaign-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Console.WriteLine("ADDITIONAL ISOLATED ROOT " + root);
            var sources = Data.Read<Dictionary<string, string>>(Path.Combine(source, "local-sources.json"));
            foreach (string id in allies ? new[] { "mass-allies" } : antioch ? new[] { "antioch-chronicles" } : new[] { "sc-evo", "the-dark-story" })
            {
                var release = Data.Read<DedicatedRelease>(Path.Combine(source, id + ".json"));
                DedicatedLibrary.ValidateRelease(release);
                string game = Path.Combine(root, id, "game"), storage = Path.Combine(root, id, "storage");
                Directory.CreateDirectory(Path.Combine(game, "Support64"));
                File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
                string switcher = Path.GetFullPath(Path.Combine(game, "Support64/SC2Switcher_x64.exe"));
                File.WriteAllText(switcher, "fixture-not-executable");
                var ccm = new Library(storage); string before = Data.Json.Serialize(ccm.Current());
                var library = new DedicatedLibrary(storage);
                var paths = release.packages.Select(p => sources[p.url]).ToArray();
                library.InstallArchives(release, Data.Hash(Path.Combine(source, id + ".json")), paths, game, null, CancellationToken.None);
                Check(release.files.All(file => Data.Hash(Data.Under(game, file.path)) == file.sha256),
                    id + ": all original ZIP game files match the declared snapshot after installation");
                Check(library.Current().installed.Count == 1 && Data.Json.Serialize(ccm.Current()) == before,
                    id + ": direct installation does not change CCM state");
                var request = library.LaunchInfo(new Campaign { id = id, kind = "dedicated" }, game);
                Check(request.FileName == switcher && request.Arguments == "\"" + Data.Under(game, release.launches[0].path) + "\"" &&
                    !request.UseShellExecute, id + ": direct SC2 start-map request, no editor or author installer required by the app");
                Check(!Directory.EnumerateFiles(game, "*.exe", SearchOption.AllDirectories).Any(path =>
                    path != switcher && path != Path.Combine(game, "StarCraft II.exe")), id + ": no installer executable is installed");
                if (id == "sc-evo")
                    Check(release.files.Any(f => f.path == "Mods/SCMRassets.SC2Mod") &&
                        release.files.Any(f => f.path == "Mods/SCMRlocal.SC2Mod") &&
                        release.files.Any(f => f.path == "Mods/SCMRmod.SC2Mod"), "Mass Recall includes main mod plus Korean voice/localization dependencies");
                else if (id == "the-dark-story")
                    Check(File.Exists(Data.Under(game, "Mods/TDS_Achievements.SC2Mod/DocumentInfo")) &&
                        File.Exists(Data.Under(game, "Mods/TDS_Achievements.SC2Mod/Triggers")) &&
                        File.Exists(Data.Under(game, "Mods/TGH_Mod.SC2Mod")), "Dark Story includes its intact directory-form achievement mod and packed main mod");
                else if (id == "mass-allies")
                {
                    Check(release.files.Count == 86 && release.launches.Single().path ==
                        "Maps/Amith's Mass Recall with Allies/SCMR Campaign Launcher.SC2Map",
                        "With Allies contains all 86 original game archives and its installer-declared start map");
                    string conflictingZip;
                    var conflict = Package(root, "sc-evo", 8, 9, "fixture", out conflictingZip);
                    var shared = release.files.Single(f => f.path == "Mods/SCMRmod.SC2Mod");
                    conflict.files[1].path = shared.path;
                    using (var archive = ZipFile.Open(conflictingZip, ZipArchiveMode.Update))
                    {
                        archive.GetEntry("Mods/Shared.SC2Mod").Delete();
                        using (var stream = archive.CreateEntry(shared.path).Open())
                        { var bytes = Mpq(9); stream.Write(bytes, 0, bytes.Length); }
                    }
                    conflict.packages[0].bytes = new FileInfo(conflictingZip).Length;
                    conflict.packages[0].sha256 = Data.Hash(conflictingZip);
                    string record = Data.Hash(Data.Under(storage, "dedicated/installed.json"));
                    Reject(() => Install(library, conflict, conflictingZip, game),
                        "a second campaign with a different SCMRmod version is rejected before game writes");
                    Check(Data.Hash(Data.Under(game, shared.path)) == shared.sha256 &&
                        Data.Hash(Data.Under(storage, "dedicated/installed.json")) == record &&
                        !File.Exists(Data.Under(game, conflict.launches.Single().path)),
                        "shared-version rejection preserves the original With Allies bytes, installed record and other map");
                }
                else
                {
                    Check(release.files.Count == 38 && release.launches.Count == 3 &&
                        release.launches.Select(l => l.path).Distinct().Count() == 3, "Antioch contains all 38 original files and three distinct episode entry maps");
                    for (int episode = 1; episode <= 3; episode++)
                    {
                        var start = library.LaunchInfo(new Campaign { id = id, kind = "dedicated" }, game, episode);
                        Check(start.Arguments == "\"" + Data.Under(game, release.launches[episode - 1].path) + "\"",
                            "Antioch episode " + episode + " selects only its own installed entry map");
                    }
                    Reject(() => library.LaunchInfo(new Campaign { id = id, kind = "dedicated" }, game, 4), "Antioch rejects an episode outside 1–3");
                }
                library.Uninstall(id, game);
                Check(release.files.All(file => !File.Exists(Data.Under(game, file.path))) && library.Current().installed.Count == 0,
                    id + ": removing the installation touches only its recorded files");
            }
            Console.WriteLine("ADDITIONAL COMPLETE: actual publisher ZIPs installed, hashed, removed in isolated fake games; no real SC2 process was started.");
        }
        static void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("DEDICATED FAIL: " + name);
            Console.WriteLine("DEDICATED PASS: " + name);
        }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Check(rejected, name);
        }
        static readonly string Stamp = new string('a', 64);
        public static int RunArchive(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Archive checks require a new isolated directory.");
            Directory.CreateDirectory(root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var selected = Data.ParseCatalogue(Data.GetText(Data.Site + "/campaigns.js")).Single(c => c.id == "synergy-protosscovertops");
            Check(DedicatedLibrary.Ready(selected), "archive fixture uses a published dedicated campaign");
            bool installed = false, update = false;
            int result = 1;
            using (var form = new Form { Text = "전용 캠페인 연결 검사 · 실제 설치·게임 실행 없음", ClientSize = new Size(1220, 900) })
            using (var view = new ArchiveView(root, true, (url, campaignId) => {
                throw new InvalidOperationException("Read-only archive verification must not install or launch anything.");
            }, () => !installed ? "[]" : Data.Json.Serialize(new[] { new {
                id = selected.id, version = selected.version, manifestSha256 = update ? Stamp : selected.dedicatedManifestSha256
            } })))
            using (var timer = new System.Windows.Forms.Timer { Interval = 60000 })
            {
                form.Controls.Add(view);
                timer.Tick += delegate { Console.Error.WriteLine("Dedicated archive state check timed out."); form.Close(); };
                form.Shown += async delegate {
                    timer.Start();
                    try
                    {
                        await view.VerifyDedicatedPageAsync(selected);
                        Check(await view.VerifyDedicatedActionsAsync() == "설치", "native archive shows install for its isolated empty installation state");
                        installed = true;
                        Check(await view.VerifyDedicatedActionsAsync() == "실행", "native archive changes install to run when host reports installation");
                        update = true;
                        Check(await view.VerifyDedicatedActionsAsync() == "실행|업데이트", "native archive preserves run and offers pinned manifest update");
                        await view.CaptureDedicatedContent(root);
                        result = 0;
                    }
                    catch (Exception error) { Console.Error.WriteLine(error); }
                    finally { timer.Stop(); form.Close(); }
                };
                Application.Run(form);
                Check(result == 0, "native WebView document, host installation state and actions connect without a loading/error overlay");
                return result;
            }
        }
        public static void RunPublished(string root, bool additional = false, bool antioch = false)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Published checks require a new isolated directory.");
            Directory.CreateDirectory(root);
            var catalogue = Data.ParseCatalogue(Data.GetText(Data.Site + "/campaigns.js"));
            var ready = catalogue.Where(c => (antioch ? c.id == "antioch-chronicles" :
                additional ? c.id == "sc-evo" || c.id == "the-dark-story" :
                c.id.StartsWith("synergy-", StringComparison.Ordinal)) && DedicatedLibrary.Ready(c)).ToList();
            Check(ready.Count == (antioch ? 1 : additional ? 1 : 17), "public archive contains the expected pinned installation manifests");
            var library = new DedicatedLibrary(Path.Combine(root, "storage"));
            foreach (var item in ready)
            {
                if (!additional && !antioch) Check(item.translator == "좀비도죽는다" &&
                    item.sourceAuthor == (item.id == "synergy-uedfl" ? "NexusCore Games" : "Synergy"),
                    "public creator and translator credits: " + item.id);
                string directory = Path.Combine(root, "manifest-" + item.id); Directory.CreateDirectory(directory);
                var release = library.FetchRelease(item, directory, CancellationToken.None);
                Check(release.id == item.id && release.version == item.version, "public GitHub manifest matches pinned site entry: " + item.id);
            }
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "Support64"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            string switcher = Path.Combine(game, "Support64", "SC2Switcher_x64.exe");
            File.WriteAllText(switcher, "fixture-not-executable");
            foreach (var selected in ready.Where(c => antioch || additional || c.id == "synergy-protosscovertops"))
            {
            Console.WriteLine("PUBLIC INSTALL START " + selected.id);
            library.Install(selected, game, null, CancellationToken.None);
            var saved = library.Installed(selected.id);
            int expectedFiles = selected.id == "antioch-chronicles" ? 38 : selected.id == "sc-evo" ? 94 : selected.id == "the-dark-story" ? 76 : 11;
            Check(saved != null && saved.release.files.Count == expectedFiles && saved.version == selected.version,
                selected.id + ": original publisher ZIP downloads and installs the expected verified files into isolated game");
            var start = library.LaunchInfo(selected, game);
            Check(start.FileName == switcher && start.Arguments.StartsWith("\"" + Path.Combine(game, "Maps") + "\\", StringComparison.Ordinal) &&
                start.Arguments.EndsWith(".SC2Map\"", StringComparison.OrdinalIgnoreCase) && !start.UseShellExecute,
                "installed Korean campaign resolves directly to SC2Switcher and its start map without an author launcher");
            if (antioch)
                for (int episode = 1; episode <= 3; episode++)
                    Check(library.LaunchInfo(selected, game, episode).Arguments == "\"" +
                        Data.Under(game, saved.release.launches[episode - 1].path) + "\"", "public Antioch episode " + episode + " routes to its own verified map");
            library.Uninstall(selected.id, game);
            Check(library.Current().installed.Count == 0 && saved.release.files.All(f => !File.Exists(Data.Under(game, f.path))),
                "isolated public installation removes only its own files");
            }
            Console.WriteLine("DEDICATED ONLINE COMPLETE: pinned public manifest hashes and original ZIP install/uninstall; no actual game execution or production game mutation.");
        }
        static byte[] Mpq(byte value) { return new byte[] { 77, 80, 81, 26, value, 2, 3, 4 }; }
        static DedicatedRelease Package(string root, string id, byte map, byte shared, string version, out string zip)
        {
            zip = Path.Combine(root, id + "-" + Guid.NewGuid().ToString("N") + ".zip");
            string stage = Path.Combine(root, "fixture-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage);
            var files = new Dictionary<string, byte[]> {
                { "Maps/" + id + "/Start.SC2Map", Mpq(map) }, { "Mods/Shared.SC2Mod", Mpq(shared) }
            };
            var release = new DedicatedRelease { schema = 1, id = id, name = "한글 전용 캠페인 · 격리 예시", version = version,
                files = new List<DedicatedFile>(), packages = new List<DedicatedBundle>(),
                launches = new List<DedicatedLaunch> { new DedicatedLaunch { label = "플레이", path = "Maps/" + id + "/Start.SC2Map" } } };
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                foreach (var item in files)
                {
                    string path = Data.Under(stage, item.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, item.Value);
                    using (var stream = archive.CreateEntry(item.Key).Open()) stream.Write(item.Value, 0, item.Value.Length);
                    release.files.Add(new DedicatedFile { path = item.Key, sha256 = Data.Hash(path), bytes = item.Value.Length });
                }
                using (var writer = new StreamWriter(archive.CreateEntry("SOURCE.txt").Open())) writer.Write("fixture-only");
            }
            release.packages.Add(new DedicatedBundle { url = DedicatedLibrary.ReleasePrefix + "fixture/" + id + ".zip",
                sha256 = Data.Hash(zip), bytes = new FileInfo(zip).Length });
            return release;
        }
        static void Install(DedicatedLibrary library, DedicatedRelease release, string zip, string game, Action<int> fault = null,
            Func<DedicatedConflict, bool> confirmConflict = null)
        {
            library.InstallArchives(release, Stamp, new[] { zip }, game, null, CancellationToken.None, fault, confirmConflict);
        }
        public static void Run(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Dedicated checks require a new isolated directory.");
            Directory.CreateDirectory(root); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string storage = Path.Combine(root, "storage"), game = Path.Combine(root, "game");
            Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            Directory.CreateDirectory(Path.Combine(game, "Support64"));
            File.WriteAllText(Path.Combine(game, "Support64/SC2Switcher_x64.exe"), "fixture-not-executable");
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            string shared = Path.Combine(game, "Mods/Shared.SC2Mod"); File.WriteAllBytes(shared, Mpq(99));
            string original = Data.Hash(shared);
            string bank = Path.Combine(root, "fixture.SC2Bank"), save = Path.Combine(root, "fixture.SC2Save");
            File.WriteAllText(bank, "original-bank"); File.WriteAllText(save, "original-save");
            var ccm = new Library(storage); string ccmBefore = Data.Json.Serialize(ccm.Current());
            var library = new DedicatedLibrary(storage);
            string zipA, zipB, zipBad, zipUpdate;
            var a = Package(root, "campaign-a", 1, 10, "1.0-ko", out zipA);
            var b = Package(root, "campaign-b", 2, 10, "1.0-ko", out zipB);
            var conflict = Package(root, "campaign-conflict", 3, 11, "1.0-ko", out zipBad);
            var update = Package(root, "campaign-a", 4, 10, "2.0-ko", out zipUpdate);
            DedicatedLibrary.ValidateRelease(a);
            Check(DedicatedLibrary.ReleaseUrl(a.packages[0].url), "exact public release repository accepted");
            Check(!DedicatedLibrary.ReleaseUrl("https://github.com/other/repo/releases/download/a/a.zip") &&
                !DedicatedLibrary.ReleaseUrl(DedicatedLibrary.ReleasePrefix + "../a.zip") &&
                !DedicatedLibrary.ReleaseUrl(a.packages[0].url + "?redirect=1"), "foreign repository and ambiguous release paths rejected");
            Check(DedicatedLibrary.BundleUrl("https://github.com/sksh1260/asdfghjjk/releases/download/SCMR_SC_Evo/SCMR_SCEvo.zip") &&
                DedicatedLibrary.BundleUrl("https://mediafilez.forgecdn.net/files/9221/114/The%20Dark%20Story.zip") &&
                !DedicatedLibrary.BundleUrl("https://mediafilez.forgecdn.net.evil.invalid/files/1/2/x.zip") &&
                !DedicatedLibrary.BundleUrl("https://mediafilez.forgecdn.net/files/1/2/%2e%2e.zip") &&
                !DedicatedLibrary.BundleUrl("https://github.com/sksh1260/asdfghjjk/releases/download/SCMR_SC_Evo/evil.exe"),
                "original publisher ZIP origins are explicit; lookalike hosts, encoded traversal and executables are denied");
            string action, id;
            int selectedEpisode;
            Check(Hangar.DedicatedAction(Data.Site + "/hangar.html?action=play&campaign=antioch-chronicles&episode=3",
                out action, out id, out selectedEpisode) && selectedEpisode == 3 && id == "antioch-chronicles",
                "archive episode action carries the exact selected episode");
            Check(!Hangar.DedicatedAction(Data.Site + "/hangar.html?action=install&campaign=antioch-chronicles&episode=1",
                out action, out id, out selectedEpisode) && !Hangar.DedicatedAction(Data.Site +
                "/hangar.html?action=play&campaign=antioch-chronicles&episode=4", out action, out id, out selectedEpisode),
                "installation cannot carry an episode and episodes outside 1–3 are rejected");
            Check(Hangar.DedicatedAction(Data.Site + "/hangar.html?action=install&campaign=campaign-a", out action, out id) &&
                action == "install" && id == "campaign-a", "archive install route is exact and campaign-ID-only");
            Check(!Hangar.DedicatedAction("https://evil.invalid/hangar.html?action=install&campaign=campaign-a", out action, out id) &&
                !Hangar.DedicatedAction(Data.Site + "/hangar.html?action=install&campaign=../../x", out action, out id),
                "foreign pages and target traversal cannot request a dedicated install");
            var bad = Data.Json.Deserialize<DedicatedRelease>(Data.Json.Serialize(a));
            bad.files[0].path = "Maps/../escaped.SC2Map";
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "manifest path traversal rejected");
            bad.files[0].path = "Maps/Campaign/mission.SC2Map";
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "CCM campaign replacement directory rejected");
            bad.files[0].path = "Mods/evil.exe";
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "executable installation rejected");
            bad.files[0].path = "Mods/Achievements.SC2Mod/evil.exe";
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "directory-form mod cannot install an executable");
            bad = Data.Json.Deserialize<DedicatedRelease>(Data.Json.Serialize(a));
            bad.packages[0].entries = new Dictionary<string, string> { { "../outside.SC2Map", bad.files[0].path } };
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "publisher ZIP mapping cannot escape its archive path");
            bad.packages[0].entries = new Dictionary<string, string> { { "source.SC2Map", "Mods/Achievements.SC2Mod/evil.dll" } };
            Reject(() => DedicatedLibrary.ValidateRelease(bad), "publisher mapping cannot add an undeclared or executable target");
            Reject(() => library.InstallArchives(a, Stamp, new[] { zipA }, game, null, new CancellationToken(true)),
                "cancelled download/extraction cannot change game files");
            Check(Data.Hash(shared) == original && library.Current().installed.Count == 0, "cancel preserves initial game state");
            Install(library, a, zipA, game);
            string map = Path.GetFullPath(Path.Combine(game, "Maps/campaign-a/Start.SC2Map")), first = Data.Hash(map);
            Check(Data.Hash(shared) == a.files[1].sha256 && library.Current().installed.Count == 1, "full Korean package installs exact file hashes");
            var card = new Campaign { id = a.id, name = a.name, kind = "dedicated", categories = new[] { "dedicated" },
                dedicatedManifestUrl = DedicatedLibrary.ReleasePrefix + "fixture/campaign-a.json", dedicatedManifestSha256 = Stamp, version = a.version };
            var request = library.LaunchInfo(card, game);
            Check(request.Arguments == "\"" + map + "\"" && !request.UseShellExecute,
                "play directly selects installed start map without an author launcher");
            Check(!library.NeedsUpdate(card), "same manifest never offers a meaningless update");
            card.dedicatedManifestSha256 = new string('b', 64);
            Check(!library.NeedsUpdate(card), "an unchecked manifest hash alone cannot claim a game update");
            var metadataOnly = Data.Json.Deserialize<DedicatedRelease>(Data.Json.Serialize(a));
            metadataOnly.files.Reverse(); metadataOnly.launches[0].label = "다른 버튼 이름";
            metadataOnly.packages[0].entries = a.files.ToDictionary(f => f.path, f => f.path);
            library.RememberRelease(card, metadataOnly);
            Check(!library.NeedsUpdate(card), "ZIP mapping, label and file order changes do not offer a game update");
            library.RememberRelease(card, update);
            Check(library.NeedsUpdate(card), "changed game file content offers an update even without a changed catalogue version");
            var resized = Data.Json.Deserialize<DedicatedRelease>(Data.Json.Serialize(a));
            resized.files[0].bytes++;
            library.RememberRelease(card, resized);
            Check(library.NeedsUpdate(card), "changed file size is not metadata-only");
            library.RememberRelease(card, metadataOnly);
            Check(!library.NeedsRepair(card), "complete installation does not need repair");
            File.Delete(map);
            Check(library.NeedsRepair(card) && !library.NeedsUpdate(card), "missing installed map is repair, not update");
            Reject(() => Install(library, a, zipA, game), "repair status cannot bypass missing-file protection");
            File.WriteAllBytes(map, Mpq(1));
            Check(!library.NeedsRepair(card), "restored map clears repair status without changing install records");
            Install(library, b, zipB, game);
            Reject(() => Install(library, conflict, zipBad, game), "different shared mod version blocked before game writes");
            Check(Data.Hash(map) == first && library.Current().installed.Count == 2, "shared-version conflict preserves both installed campaigns");
            var sameCcm = new Campaign { files = new List<OwnedFile> { new OwnedFile { path = @"Mods\Shared.SC2Mod" } } };
            Reject(() => DedicatedLibrary.ProtectCcm(storage, sameCcm), "CCM activation cannot overwrite a dedicated shared mod");
            File.WriteAllBytes(map, Mpq(80));
            Reject(() => Install(library, update, zipUpdate, game), "external map edit blocks update");
            Reject(() => library.Uninstall(a.id, game), "external map edit blocks removal");
            Reject(() => library.LaunchInfo(card, game), "changed install cannot silently launch");
            File.WriteAllBytes(map, Mpq(1));
            Reject(() => Install(library, update, zipUpdate, game, count => { if (count == 1) throw new IOException("injected-install-failure"); }),
                "partial install fault is reported");
            Check(Data.Hash(map) == first && library.Installed(a.id).version == "1.0-ko", "partial install restores old files and install record");
            Install(library, update, zipUpdate, game);
            Check(Data.Hash(map) == update.files[0].sha256 && library.Installed(a.id).version == "2.0-ko",
                "update replaces only owned files after complete validation");
            library.Uninstall(a.id, game);
            Check(!File.Exists(map) && File.Exists(shared) && Data.Hash(shared) == b.files[1].sha256,
                "uninstall preserves identical shared file still owned by another campaign");
            library.Uninstall(b.id, game);
            Check(Data.Hash(shared) == original && library.Current().installed.Count == 0, "last owner removal restores initial external file backup");
            Check(Data.Json.Serialize(ccm.Current()) == ccmBefore && File.ReadAllText(bank) == "original-bank" &&
                File.ReadAllText(save) == "original-save", "CCM active state, bank and save remain untouched");
            var package = new Campaign { name = card.name, slot = "wol", sourceFileName = "campaign-a.zip" };
            Check(SiteMatcher.Candidates(package, new[] { card }).Count == 0 && !ccm.AcceptsSiteMetadata(package, card),
                "dedicated campaign never enters CCM ZIP matching or import");
            VerifyJournal(root, game, shared);
            VerifyCcmCoexistence(root);
            VerifyConflictDialog(root);
            VerifySelectiveInstall(root);
            VerifyOperationIsolation(root);
            VerifyUI(root, ccm, card);
            Console.WriteLine("DEDICATED COMPLETE: isolated local install/update/uninstall/failure protection and UI; no real game, bank or network mutation.");
        }
        static void VerifyOperationIsolation(string root)
        {
            string scope = Path.Combine(root, "operation-isolation"), storage = Path.Combine(scope, "storage"), game = Path.Combine(scope, "game");
            Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            string zipA, zipB, zipUpdate;
            var a = Package(scope, "unrelated-a", 1, 10, "1.0", out zipA);
            var b = Package(scope, "requested-b", 2, 10, "1.0", out zipB);
            var update = Package(scope, "requested-b", 3, 10, "2.0", out zipUpdate);
            var library = new DedicatedLibrary(storage);
            Install(library, a, zipA, game);
            string mapA = Data.Under(game, a.launches[0].path), mapB = Data.Under(game, b.launches[0].path);
            string shared = Data.Under(game, "Mods/Shared.SC2Mod"), state = Data.Under(storage, "dedicated/installed.json");
            string originalA = Data.Json.Serialize(library.Installed(a.id));
            File.Delete(mapA);
            Install(library, b, zipB, game);
            Check(!File.Exists(mapA) && Data.Hash(mapB) == b.files[0].sha256 &&
                Data.Json.Serialize(library.Installed(a.id)) == originalA,
                "missing unrelated campaign files do not block another install or get silently repaired");
            library.Uninstall(b.id, game);
            Check(!File.Exists(mapA) && !File.Exists(mapB) && library.Installed(a.id) != null,
                "uninstall touches only the selected campaign while preserving unrelated missing files and records");
            File.WriteAllBytes(mapA, Mpq(77)); string editedA = Data.Hash(mapA);
            Install(library, b, zipB, game);
            Check(Data.Hash(mapA) == editedA && Data.Json.Serialize(library.Installed(a.id)) == originalA,
                "an unrelated user edit is preserved when installing a different campaign");
            string before = Data.Hash(state);
            File.WriteAllBytes(mapB, Mpq(55)); string editedB = Data.Hash(mapB);
            Reject(() => Install(library, b, zipB, game), "externally changed files in the requested campaign still block installation");
            Reject(() => library.Uninstall(b.id, game), "externally changed files in the requested campaign still block removal");
            Check(Data.Hash(mapB) == editedB && Data.Hash(state) == before, "rejected operations preserve the requested external edit and installed state");
            File.WriteAllBytes(mapB, Mpq(2)); File.WriteAllBytes(shared, Mpq(66)); string editedShared = Data.Hash(shared);
            Reject(() => Install(library, b, zipB, game), "externally changed shared files still block an operation that uses them");
            Check(Data.Hash(shared) == editedShared && Data.Hash(state) == before, "shared-file protection is not bypassed by operation scoping");
            File.WriteAllBytes(shared, Mpq(10));
            Reject(() => Install(library, update, zipUpdate, game, step => { throw new IOException("isolated commit failure"); }),
                "scoped update rollback handles a commit failure even when another campaign was edited");
            Check(Data.Hash(mapB) == b.files[0].sha256 && Data.Hash(mapA) == editedA && Data.Hash(state) == before,
                "rollback restores only the failed operation and leaves unrelated user edits intact");
            library.Uninstall(b.id, game);
            Reject(() => library.Uninstall(a.id, game), "the previously unrelated edit stays protected when its own campaign is removed");
        }
        static void VerifySelectiveInstall(string root)
        {
            string fixture = Path.Combine(root, "selective"); Directory.CreateDirectory(fixture);
            var release = new DedicatedRelease { schema = 1, id = "selective", name = "선택 다운로드 검사", version = "1",
                files = new List<DedicatedFile>(), packages = new List<DedicatedBundle>(),
                launches = new List<DedicatedLaunch> { new DedicatedLaunch { label = "실행", path = "Maps/selective/Start.SC2Map" } } };
            var sources = new Dictionary<string, string>();
            for (int i = 0; i < 4; i++)
            {
                string path = i == 0 ? "Maps/selective/Start.SC2Map" : "Mods/Selective" + i + ".SC2Mod";
                byte[] data = Mpq((byte)(i + 1));
                string original = Data.Under(fixture, "source/" + path);
                Directory.CreateDirectory(Path.GetDirectoryName(original)); File.WriteAllBytes(original, data);
                string zip = Path.Combine(fixture, i + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var stream = archive.CreateEntry(path).Open()) stream.Write(data, 0, data.Length);
                var bundle = new DedicatedBundle { url = DedicatedLibrary.ReleasePrefix + "fixture/selective-" + i + ".zip",
                    sha256 = Data.Hash(zip), bytes = new FileInfo(zip).Length,
                    entries = new Dictionary<string, string> { { path, path } } };
                release.files.Add(new DedicatedFile { path = path, sha256 = Data.Hash(original), bytes = data.Length });
                release.packages.Add(bundle); sources.Add(bundle.url, zip);
            }
            int calls = 0, active = 0, peak = 0;
            bool concurrent = true, tamper = false, corrupt = false;
            string tamperTarget = null;
            object gate = new object();
            using (var entered = new CountdownEvent(3))
            {
                Action<string, string, long, string, Action<WorkProgress>, CancellationToken> download =
                    (url, output, bytes, hash, report, cancellation) => {
                        int number;
                        lock (gate) { number = ++calls; active++; peak = Math.Max(peak, active); }
                        try
                        {
                            if (concurrent && number <= 3)
                            {
                                entered.Signal();
                                if (!entered.Wait(10000)) throw new TimeoutException("Three independent downloads must run concurrently.");
                            }
                            cancellation.ThrowIfCancellationRequested();
                            File.Copy(sources[url], output);
                            if (corrupt)
                                using (var stream = File.Open(output, FileMode.Open, FileAccess.Write)) stream.WriteByte(0);
                            if (tamper) File.WriteAllBytes(tamperTarget, Mpq(88));
                            if (report != null) report(new WorkProgress("fixture", bytes, bytes, true));
                        }
                        finally { lock (gate) active--; }
                    };
                Func<string, string> gameFor = name => {
                    string game = Path.Combine(fixture, name, "game");
                    Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture");
                    return game;
                };
                Func<string, string> workFor = name => {
                    string work = Path.Combine(fixture, "work-" + name);
                    Directory.CreateDirectory(work); return work;
                };
                string fresh = gameFor("fresh");
                var library = new DedicatedLibrary(Path.Combine(fixture, "fresh/storage"));
                long previousProgress = 0;
                library.InstallSnapshot(release, Stamp, workFor("fresh"), fresh, p => {
                    if (p.stage != "필요한 파일 다운로드") return;
                    Check(p.total == release.packages.Sum(b => b.bytes) && p.completed >= previousProgress && p.completed <= p.total,
                        "parallel progress is aggregate bytes, monotonic and bounded");
                    previousProgress = p.completed;
                }, CancellationToken.None, download);
                Check(calls == 4 && peak == 3 && release.files.All(f => Data.Hash(Data.Under(fresh, f.path)) == f.sha256),
                    "fresh install uses at most three simultaneous ZIP transfers and validates every installed file");
                concurrent = false; calls = 0;
                var timer = System.Diagnostics.Stopwatch.StartNew();
                library.InstallSnapshot(release, Stamp, workFor("same"), fresh, null, CancellationToken.None, download);
                Check(calls == 0, "same-version reinstall downloads zero ZIPs and reuses matching game files");
                Console.WriteLine("SELECTIVE SAME-VERSION FIXTURE milliseconds=" + timer.ElapsedMilliseconds);
                string partial = gameFor("partial");
                foreach (var file in release.files.Take(3))
                    Data.Copy(Data.Under(fixture, "source/" + file.path), Data.Under(partial, file.path));
                calls = 0;
                var partialLibrary = new DedicatedLibrary(Path.Combine(fixture, "partial/storage"));
                partialLibrary.InstallSnapshot(release, Stamp, workFor("partial"), partial, null, CancellationToken.None, download);
                Check(calls == 1 && release.files.All(f => Data.Hash(Data.Under(partial, f.path)) == f.sha256),
                    "three matching files and one missing file download only the one required ZIP");
                string modified = gameFor("modified");
                foreach (var file in release.files)
                    Data.Copy(Data.Under(fixture, "source/" + file.path), Data.Under(modified, file.path));
                tamperTarget = Data.Under(modified, release.files[0].path); File.WriteAllBytes(tamperTarget, Mpq(90));
                calls = 0;
                var modifiedLibrary = new DedicatedLibrary(Path.Combine(fixture, "modified/storage"));
                modifiedLibrary.InstallSnapshot(release, Stamp, workFor("modified"), modified, null, CancellationToken.None, download);
                Check(calls == 1 && Data.Hash(tamperTarget) == release.files[0].sha256,
                    "one mismatched unowned file downloads its required ZIP and preserves a first-install backup");
                modifiedLibrary.Uninstall(release.id, modified);
                Check(File.ReadAllBytes(tamperTarget).SequenceEqual(Mpq(90)), "removing a selective installation restores the original differing file");
                string guarded = gameFor("guarded");
                foreach (var file in release.files.Take(3))
                    Data.Copy(Data.Under(fixture, "source/" + file.path), Data.Under(guarded, file.path));
                tamperTarget = Data.Under(guarded, release.files[0].path); tamper = true;
                var guardedLibrary = new DedicatedLibrary(Path.Combine(fixture, "guarded/storage"));
                Reject(() => guardedLibrary.InstallSnapshot(release, Stamp, workFor("guarded"), guarded, null, CancellationToken.None, download),
                    "reuse is revalidated: a matching file edited while a needed ZIP downloads blocks commit");
                Check(File.ReadAllBytes(tamperTarget).SequenceEqual(Mpq(88)) && guardedLibrary.Current().installed.Count == 0 &&
                    !File.Exists(Data.Under(guarded, release.files[3].path)), "changed reused file is preserved and no partial game install is committed");
                tamper = false; corrupt = true;
                string broken = gameFor("corrupt");
                var brokenLibrary = new DedicatedLibrary(Path.Combine(fixture, "corrupt/storage"));
                Reject(() => brokenLibrary.InstallSnapshot(release, Stamp, workFor("corrupt"), broken, null, CancellationToken.None, download),
                    "downloaded ZIP hash validation is never skipped by selective downloads");
                Check(brokenLibrary.Current().installed.Count == 0 && release.files.All(f => !File.Exists(Data.Under(broken, f.path))),
                    "corrupt transfer leaves every game target and install record untouched");
                corrupt = false; calls = 0;
                Reject(() => brokenLibrary.InstallSnapshot(release, Stamp, workFor("cancelled"), broken, null, new CancellationToken(true), download),
                    "cancelled selective install performs no downloads or game writes");
                Check(calls == 0 && brokenLibrary.Current().installed.Count == 0, "cancellation preserves pre-install state");
                File.WriteAllBytes(Data.Under(fresh, release.files[0].path), Mpq(89));
                calls = 0;
                Reject(() => library.InstallSnapshot(release, Stamp, workFor("owned-edit"), fresh, null, CancellationToken.None, download),
                    "selective downloads never authorize overwriting an externally modified owned file");
                Check(File.ReadAllBytes(Data.Under(fresh, release.files[0].path)).SequenceEqual(Mpq(89)),
                    "external-change guard still preserves user-edited owned files");
            }
        }
        static void VerifyUI(string root, Library ccm, Campaign card)
        {
            bool installed = false, update = false, newlyAdded = false, repair = false; int calls = 0;
            var episodeCalls = new List<int>();
            card.sourceAuthor = "Synergy"; card.translator = "좀비도죽는다";
            var unavailable = new Campaign { id = "not-ready", kind = "dedicated", name = "설치본 준비 중 · 격리 예시", categories = new[] { "dedicated" }, version = "준비 중" };
            using (var image = new Bitmap(320, 180))
            using (var form = new Form { ClientSize = new Size(1440, 1000) })
            using (var view = new InstalledView(ccm, c => Task.FromResult<Image>(image), c => { calls++; return Task.FromResult(false); },
                () => { }, external: () => new Campaign[0], recordSummary: s => "플레이 0.0시간",
                dedicatedInstalled: c => c.id == card.id && installed, dedicatedUpdate: c => c.id == card.id && update,
                dedicatedVersion: c => installed ? "installed-version-fixture" : c.version,
                launchEpisode: (c, episode) => { episodeCalls.Add(episode); return Task.FromResult(false); },
                isNewCampaign: c => newlyAdded && c.id == card.id, campaignViewed: c => { if (c.id == card.id) newlyAdded = false; },
                dedicatedRepair: c => repair && c.id == card.id))
            {
                form.Controls.Add(view); view.Reload(new[] { card, unavailable }); form.Show(); Application.DoEvents();
                foreach (int width in new[] { 1440, 980 })
                {
                    form.ClientSize = new Size(width, 1000);
                    installed = update = false; newlyAdded = true;
                    view.VerifyDedicatedState(card, "설치", true, false, false); view.VerifyNewCampaignBadge(card, false);
                    newlyAdded = true; view.Reveal(new[] { card, unavailable }, card); view.VerifyNewCampaignBadge(card, true);
                    Capture(form, Path.Combine(root, "install-" + width + ".png"));
                    view.Reveal(new[] { card, unavailable }, unavailable); view.VerifyNewCampaignBadge(card, true);
                    Capture(form, Path.Combine(root, "new-shelf-" + width + ".png"));
                    view.VerifyNewCampaignBadge(card, false);
                    installed = true;
                    view.VerifyDedicatedState(card, "실행", true, true, false); Capture(form, Path.Combine(root, "play-" + width + ".png"));
                    update = true;
                    view.VerifyDedicatedState(card, "실행", true, true, true);
                    repair = true;
                    view.VerifyDedicatedState(card, "복구 필요", false, true, false, true);
                    repair = false;
                    view.SetBusy(true); int before = calls;
                    view.VerifyDedicatedState(card, "실행", false, true, true);
                    Check(calls == before, "busy dedicated card sends no duplicate action");
                    view.SetBusy(false);
                    view.VerifyDedicatedState(unavailable, "설치본 준비 중", false, false, false);
                    installed = true; update = false; card.dedicatedEpisodeCount = 3; episodeCalls.Clear();
                    view.VerifyDedicatedEpisodes(card, false);
                    Check(episodeCalls.Count == 0, "opening the dedicated Run selector does not launch a mission");
                    view.VerifyEpisodeSelectionForTest();
                    Check(episodeCalls.SequenceEqual(new[] { 1, 2, 3 }), "dedicated episode choices dispatch exactly the selected episode");
                    Capture(form, Path.Combine(root, "dedicated-episodes-" + width + ".png"));
                    view.SetBusy(true); view.VerifyDedicatedEpisodes(card, true);
                    Check(episodeCalls.Count == 3, "busy dedicated episode selector dispatches nothing");
                    view.SetBusy(false); card.dedicatedEpisodeCount = 0;
                }
                form.Close();
            }
        }
        static void Capture(Form form, string path)
        {
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path); }
        }
        static void VerifyJournal(string root, string game, string shared)
        {
            string storage = Path.Combine(root, "recovery-storage");
            var library = new DedicatedLibrary(storage);
            string transaction = Data.Under(storage, "dedicated/transaction");
            string before = Data.Hash(shared);
            string backup = Data.Under(transaction, "before/Mods/Shared.SC2Mod");
            Data.Copy(shared, backup);
            File.WriteAllBytes(shared, Mpq(10));
            var journal = new DedicatedJournal { gameRoot = Path.GetFullPath(game), before = new DedicatedState(),
                changes = new List<DedicatedChange> { new DedicatedChange {
                    path = "Mods/Shared.SC2Mod", beforeHash = before, afterHash = Data.Hash(shared) } } };
            Data.Write(Path.Combine(transaction, "journal.json"), journal);
            Data.Write(Data.Under(storage, "dedicated/installed.json"), new DedicatedState { gameRoot = "interrupted-state-fixture" });
            library.Recover();
            Check(Data.Hash(shared) == before && library.Current().gameRoot == null && !Directory.Exists(transaction),
                "persisted interrupted journal restores original file and pre-install state");
            Data.Copy(shared, backup);
            Data.Write(Path.Combine(transaction, "journal.json"), journal);
            File.WriteAllBytes(shared, Mpq(80));
            string edited = Data.Hash(shared);
            Reject(() => library.Recover(), "interrupted journal refuses an unrelated external edit");
            Check(Data.Hash(shared) == edited && Directory.Exists(transaction),
                "blocked recovery preserves edited file and its recovery evidence");
        }
        static void VerifyCcmCoexistence(string root)
        {
            string fixture = Path.Combine(root, "coexistence"), storage = Path.Combine(fixture, "storage"),
                game = Path.Combine(fixture, "game");
            Directory.CreateDirectory(Path.Combine(game, "Support64"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            File.WriteAllText(Path.Combine(game, "Support64/SC2Switcher_x64.exe"), "fixture-not-executable");
            string mission = Data.Under(game, "Maps/Campaign/mission.SC2Map");
            Directory.CreateDirectory(Path.GetDirectoryName(mission)); File.WriteAllBytes(mission, Mpq(90));
            string existingMod = Data.Under(game, "Mods/Existing.SC2Mod");
            Directory.CreateDirectory(Path.GetDirectoryName(existingMod)); File.WriteAllBytes(existingMod, Mpq(91));
            var ccm = new Library(storage);
            Func<string, string, Campaign> import = (id, mod) => {
                string zip = Path.Combine(fixture, id + ".zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    using (var writer = new StreamWriter(archive.CreateEntry("metadata.txt").Open()))
                        writer.Write("title=" + id + "\ncampaign=wol\nversion=1.0\nauthor=fixture\n");
                    using (var stream = archive.CreateEntry("mission.SC2Map").Open())
                    { byte[] data = Mpq(50); stream.Write(data, 0, data.Length); }
                    using (var stream = archive.CreateEntry(mod).Open())
                    { byte[] data = Mpq(51); stream.Write(data, 0, data.Length); }
                }
                return ccm.Import(zip, id);
            };
            var current = import("existing-campaign", "Existing.SC2Mod");
            ccm.Activate(current.id, game, true, null);
            string active = Data.Json.Serialize(ccm.Current()), missionHash = Data.Hash(mission), modHash = Data.Hash(existingMod);
            var dedicated = new DedicatedLibrary(storage);
            string zipA, zipUpdate;
            var a = Package(fixture, "standalone", 1, 10, "1.0-ko", out zipA);
            var update = Package(fixture, "standalone", 2, 10, "2.0-ko", out zipUpdate);
            int confirmations = 0;
            Func<DedicatedConflict, bool> unexpected = conflict => {
                confirmations++; throw new InvalidOperationException("A non-overlapping campaign must not ask to restore CCM.");
            };
            Install(dedicated, a, zipA, game, null, unexpected);
            var card = new Campaign { id = a.id, kind = "dedicated" };
            Check(dedicated.LaunchInfo(card, game, 0, unexpected).Arguments.EndsWith("Start.SC2Map\"", StringComparison.Ordinal),
                "standalone start map resolves while a non-overlapping CCM campaign remains active");
            Install(dedicated, update, zipUpdate, game, null, unexpected);
            dedicated.Uninstall(a.id, game);
            Check(Data.Json.Serialize(ccm.Current()) == active && Data.Hash(mission) == missionHash && Data.Hash(existingMod) == modHash,
                "standalone install/update/play preparation/removal preserve unrelated active CCM state and bytes");
            Check(confirmations == 0, "non-overlapping install/update/run requests never display a CCM restore question");
            var overlapping = import("overlapping-campaign", "Shared.SC2Mod");
            ccm.Activate(overlapping.id, game, true, null);
            string protectedState = Data.Json.Serialize(ccm.Current()), shared = Data.Under(game, "Mods/Shared.SC2Mod"),
                protectedHash = Data.Hash(shared);
            bool blocked = false;
            try { Install(dedicated, a, zipA, game); }
            catch (InvalidOperationException error) { blocked = error.Message.Contains("Mods/Shared.SC2Mod"); }
            Check(blocked, "only a real CCM file overlap blocks standalone installation and identifies the exact path");
            Check(Data.Json.Serialize(ccm.Current()) == protectedState && Data.Hash(shared) == protectedHash &&
                dedicated.Current().installed.Count == 0, "file-overlap rejection preserves the active campaign and makes no standalone installation");
            bool declined = false;
            try { Install(dedicated, a, zipA, game, null, conflict => {
                confirmations++;
                Check(conflict.campaignId == overlapping.id && conflict.path == "Mods/Shared.SC2Mod",
                    "overlap confirmation identifies the actual active campaign and file");
                return false;
            }); }
            catch (OperationCanceledException) { declined = true; }
            Check(declined && confirmations == 1 && Data.Json.Serialize(ccm.Current()) == protectedState &&
                Data.Hash(shared) == protectedHash && dedicated.Current().installed.Count == 0,
                "No cancels the install and preserves the CCM campaign and its game bytes");
            File.WriteAllBytes(shared, Mpq(80));
            string externalEdit = Data.Hash(shared);
            Reject(() => Install(dedicated, a, zipA, game, null, conflict => true),
                "Yes never bypasses external-change protection when CCM restoration fails");
            Check(Data.Json.Serialize(ccm.Current()) == protectedState && Data.Hash(shared) == externalEdit &&
                dedicated.Current().installed.Count == 0, "failed CCM restoration prevents standalone installation");
            File.WriteAllBytes(shared, Mpq(51));
            confirmations = 0;
            Install(dedicated, a, zipA, game, null, conflict => { confirmations++; return true; });
            Check(confirmations == 1 && ccm.Current().active == null && dedicated.Installed(a.id) != null &&
                Data.Hash(shared) == a.files[1].sha256, "Yes restores the CCM backup and continues the already-verified installation without another prompt");
            // Emulate a legacy CCM instance that did not know about standalone ownership.
            string ownership = Data.Under(storage, "dedicated/installed.json");
            string parked = ownership + "." + Guid.NewGuid().ToString("N") + ".fixture";
            File.Move(ownership, parked);
            try { ccm.Activate(overlapping.id, game, true, null); }
            finally { File.Move(parked, ownership); }
            string legacyState = Data.Json.Serialize(ccm.Current()), legacyHash = Data.Hash(shared);
            declined = false; confirmations = 0;
            try { dedicated.LaunchInfo(card, game, 0, conflict => { confirmations++; return false; }); }
            catch (OperationCanceledException) { declined = true; }
            Check(declined && confirmations == 1 && Data.Json.Serialize(ccm.Current()) == legacyState && Data.Hash(shared) == legacyHash,
                "No cancels a conflicting run request without restoring or launching anything");
            confirmations = 0;
            int reapplied = 0;
            var launch = dedicated.LaunchInfo(card, game, 0, conflict => { confirmations++; return true; }, null,
                restored => {
                    dedicated.InstallArchives(a, Stamp, new[] { zipA }, game, null, CancellationToken.None, null, null, restored);
                    reapplied++;
                });
            Check(confirmations == 1 && ccm.Current().active == null && Data.Hash(shared) == a.files[1].sha256 &&
                reapplied == 1 && launch.Arguments.EndsWith("Start.SC2Map\"", StringComparison.Ordinal),
                "Yes restores the overlapping CCM campaign, revalidates standalone files and prepares direct SC2 execution");
        }
        static void VerifyConflictDialog(string root)
        {
            var item = new Campaign { name = "전용 캠페인 · 격리 예시" };
            var conflict = new DedicatedConflict("fixture-ccm", "기존 캠페인 · 격리 예시", "Mods/Shared.SC2Mod");
            using (var dialog = Hangar.DedicatedConflictDialog(item, conflict, "설치"))
            {
                dialog.Show(); Application.DoEvents(); dialog.VerifyFixture();
                var cancel = (Button)dialog.CancelButton;
                var accept = cancel.Parent.Controls.OfType<Button>().Single(b => b.DialogResult == DialogResult.OK);
                Check(accept.Text == "예" && cancel.Text == "아니오" && dialog.AcceptButton == cancel &&
                    dialog.MessageText.Contains(item.name) && dialog.MessageText.Contains(conflict.campaignName) &&
                    dialog.MessageText.Contains(conflict.path), "overlap dialog has explicit Yes/No, defaults to No and names both campaigns and the actual file");
                dialog.ClientSize = new Size(680, 560); dialog.PerformLayout();
                Capture(dialog, Path.Combine(root, "conflict-yes-no.png")); dialog.Close();
            }
            foreach (bool acceptChoice in new[] { false, true })
            using (var dialog = Hangar.DedicatedConflictDialog(item, conflict, "실행"))
            using (var timer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                timer.Tick += delegate {
                    timer.Stop();
                    ((Button)dialog.CancelButton).Parent.Controls.OfType<Button>()
                        .Single(b => b.DialogResult == (acceptChoice ? DialogResult.OK : DialogResult.Cancel)).PerformClick();
                };
                timer.Start();
                Check(dialog.ShowDialog() == (acceptChoice ? DialogResult.OK : DialogResult.Cancel),
                    "rendered overlap dialog returns the selected " + (acceptChoice ? "Yes" : "No") + " decision");
            }
        }
    }
}
