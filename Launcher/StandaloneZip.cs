using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CampaignHangar
{
    // Non-CCM archives are never sent through the CCM parser.  This reader only
    // inspects ZIP directory entries; no installer, executable or script runs.
    public sealed class StandaloneZipFile
    {
        public string Entry { get; set; }
        public string GamePath { get; set; }
        public long Bytes { get; set; }
        public bool IsMap { get; set; }
        public bool IsModArchive { get; set; }
    }
    public sealed class StandaloneZipChoice
    {
        public string Kind { get; set; } // map, episodes, author-launcher
        public string Entry { get; set; }
        public string[] Episodes { get; set; }
        public string Title { get; set; }
        public override string ToString() { return Title; }
    }
    public sealed class StandaloneZipPackage
    {
        public string Zip { get; set; }
        public string Name { get; set; }
        public List<StandaloneZipFile> GameFiles { get; set; }
        public List<StandaloneZipChoice> Choices { get; set; }
        public long TotalBytes { get; set; }
    }
    public sealed class StandaloneZipResult
    {
        public Campaign Campaign { get; set; }
        public bool AlreadyLinked { get; set; }
        public int CreatedFiles { get; set; }
        public string PreviousBackup { get; set; }
    }
    public sealed class StandaloneExistingMaps
    {
        public string Launcher { get; set; }
        public string Folder { get; set; }
        public int MapCount { get; set; }
        public int DifferentFileCount { get; set; }
    }
    public static class StandaloneZip
    {
        const long MaxEntry = 5L * 1024 * 1024 * 1024;
        const long MaxTotal = 16L * 1024 * 1024 * 1024;
        const int MaxEntries = 20000;
        static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;

        static string[] Parts(string entry)
        {
            if (String.IsNullOrWhiteSpace(entry) || entry.Length > 240 ||
                entry.StartsWith("/", StringComparison.Ordinal) || entry.Contains("\\") || entry.Contains(":"))
                throw new InvalidDataException("전용 ZIP에 안전하지 않은 파일 경로가 있습니다: " + entry);
            string[] parts = entry.TrimEnd('/').Split('/');
            if (parts.Length > 24 || parts.Any(part => String.IsNullOrWhiteSpace(part) || part == "." || part == ".." ||
                part.EndsWith(" ", StringComparison.Ordinal) || part.EndsWith(".", StringComparison.Ordinal) ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase)))
                throw new InvalidDataException("전용 ZIP에 경로 탈출 또는 지원되지 않는 이름이 있습니다: " + entry);
            return parts;
        }
        static string GamePath(string[] parts, string packageFolder)
        {
            // A directory-backed .SC2Mod contains all its original relative
            // members; a packed .SC2Mod is one file. Never flatten its contents.
            int mod = Array.FindIndex(parts, p => p.EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase));
            if (mod >= 0)
            {
                int explicitMods = Array.FindIndex(parts, p => String.Equals(p, "Mods", StringComparison.OrdinalIgnoreCase));
                return "Mods/" + String.Join("/", parts.Skip(explicitMods >= 0 && explicitMods < mod ? explicitMods + 1 : mod));
            }
            if (!parts.Last().EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase)) return null;
            int maps = Array.FindIndex(parts, p => String.Equals(p, "Maps", StringComparison.OrdinalIgnoreCase));
            string suffix;
            if (maps >= 0) suffix = String.Join("/", parts.Skip(maps + 1));
            else if (parts.Length == 1) suffix = packageFolder + "/" + parts[0];
            else if (String.Equals(parts[0], "Campaign", StringComparison.OrdinalIgnoreCase))
                suffix = packageFolder + "/" + String.Join("/", parts);
            else suffix = String.Join("/", parts);
            // CCM owns Maps/Campaign. A non-CCM ZIP cannot write into it.
            if (suffix.StartsWith("Campaign/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("전용 ZIP이 CCM 공용 Maps/Campaign 폴더를 덮을 수 없습니다.");
            return "Maps/" + suffix;
        }
        static bool IsLauncher(string entry)
        {
            return Path.GetFileNameWithoutExtension(entry).IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static StandaloneZipPackage Inspect(string path)
        {
            var package = new StandaloneZipPackage { Zip = Path.GetFullPath(path),
                Name = Path.GetFileNameWithoutExtension(path), GameFiles = new List<StandaloneZipFile>(),
                Choices = new List<StandaloneZipChoice>() };
            // Root-level maps belong in an isolated named campaign folder.
            // Dropping The Dark Story.zip must never scatter tMission_*.SC2Map
            // into the shared Maps root or overwrite an existing translation.
            Parts(package.Name + "/Launcher.SC2Map");
            using (var zip = ZipFile.OpenRead(path))
            {
                if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("전용 ZIP 파일 수가 안전 한도를 초과했습니다.");
                var names = new HashSet<string>(IgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    string[] parts = Parts(entry.FullName);
                    bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                    int unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (unixMode == 0xA000) throw new InvalidDataException("ZIP 내부의 심볼릭 링크를 설치할 수 없습니다.");
                    if (directory) continue;
                    if (!names.Add(entry.FullName))
                        throw new InvalidDataException("ZIP에 Windows에서 충돌하는 파일명이 중복돼 있습니다.");
                    if (entry.Length < 0 || entry.Length > MaxEntry || package.TotalBytes > MaxTotal - entry.Length)
                        throw new InvalidDataException("전용 ZIP의 압축 해제 크기가 안전 한도를 초과했습니다.");
                    package.TotalBytes += entry.Length;
                    if (entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                    string target = GamePath(parts, package.Name);
                    if (target != null)
                        package.GameFiles.Add(new StandaloneZipFile { Entry = entry.FullName, GamePath = target,
                            Bytes = entry.Length, IsMap = parts.Last().EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase),
                            IsModArchive = parts.Last().EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase) });
                    bool map = parts.Last().EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase);
                    bool exe = parts.Last().EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                    if ((map || exe) && IsLauncher(entry.FullName))
                        package.Choices.Add(new StandaloneZipChoice { Entry = entry.FullName,
                            Kind = map ? "map" : "author-launcher",
                            Title = (map ? "시작 맵" : "제작자 EXE") + " · " + entry.FullName });
                }
            }
            if (package.GameFiles.GroupBy(f => f.GamePath, IgnoreCase).Any(g => g.Count() > 1))
                throw new InvalidDataException("서로 다른 ZIP 파일이 동일한 게임 설치 경로를 사용합니다.");
            var maps = package.GameFiles.Where(f => f.IsMap).ToList();
            if (package.Choices.Count == 0 && maps.Count == 1)
            {
                package.Choices.Add(new StandaloneZipChoice { Kind = "map",
                    Entry = maps[0].Entry, Title = "단일 시작 맵 · " + maps[0].Entry });
            }
            // When the creator supplies exactly three episode launchers, the
            // episode selection menu can link all three without manual browsing.
            var mapLaunchers = package.Choices.Where(c => c.Kind == "map").ToList();
            if (mapLaunchers.Count == 3)
            {
                var episodePaths = new string[3];
                foreach (var choice in mapLaunchers)
                {
                    var match = Regex.Match(Path.GetFileName(choice.Entry),
                        "(?:episode|ep)[ _.-]?([123])", RegexOptions.IgnoreCase);
                    if (match.Success) episodePaths[Int32.Parse(match.Groups[1].Value) - 1] = choice.Entry;
                }
                if (episodePaths.All(p => p != null))
                {
                    package.Choices.RemoveAll(c => c.Kind == "map");
                    package.Choices.Insert(0, new StandaloneZipChoice { Kind = "episodes", Episodes = episodePaths,
                        Entry = episodePaths[0], Title = "에피소드 1·2·3 시작 맵 자동 연결" });
                }
            }
            return package;
        }
        public static StandaloneExistingMaps ExistingMapVariant(StandaloneZipPackage package,
            StandaloneZipChoice choice, string gameRoot)
        {
            if (choice == null || choice.Kind != "map" || !File.Exists(Path.Combine(gameRoot ?? "", "StarCraft II.exe")))
                return null;
            var maps = package.GameFiles.Where(f => f.IsMap).ToList();
            // Only offer reuse of a complete multi-map folder; a single
            // filename collision does not establish campaign identity.
            if (maps.Count < 3) return null;
            var launcher = maps.SingleOrDefault(f => f.Entry == choice.Entry);
            if (launcher == null) return null;
            string launcherPath = Data.Under(gameRoot, launcher.GamePath);
            if (!File.Exists(launcherPath)) return null;
            string dir = Path.GetDirectoryName(launcherPath);
            if (maps.Any(f => !String.Equals(Path.GetDirectoryName(Data.Under(gameRoot, f.GamePath)),
                dir, StringComparison.OrdinalIgnoreCase))) return null;
            if (maps.Any(f => !File.Exists(Data.Under(gameRoot, f.GamePath)))) return null;
            // Verify every existing file has the expected game-container
            // signature. A random directory with matching names is insufficient.
            foreach (var map in maps) ExternalCampaigns.ValidateMap(Data.Under(gameRoot, map.GamePath), gameRoot);
            int different = 0;
            using (var archive = ZipFile.OpenRead(package.Zip))
            using (var sha = SHA256.Create())
                foreach (var map in maps)
                {
                    string target = Data.Under(gameRoot, map.GamePath);
                    var entry = archive.GetEntry(map.Entry);
                    if (entry == null) throw new InvalidDataException("ZIP 맵 목록을 다시 읽지 못했습니다.");
                    if (new FileInfo(target).Length != map.Bytes) { different++; continue; }
                    using (var stream = entry.Open())
                    {
                        string zipped = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                        if (!String.Equals(zipped, Data.Hash(target), StringComparison.OrdinalIgnoreCase)) different++;
                    }
                }
            return new StandaloneExistingMaps {
                Launcher = launcherPath, Folder = dir, MapCount = maps.Count,
                DifferentFileCount = different
            };
        }
        static void CopyBackup(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            var root = Path.GetFullPath(source).TrimEnd('\\') + "\\";
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("기존 폴더에 링크가 있어 안전한 백업을 만들 수 없습니다.");
                Directory.CreateDirectory(Data.Under(destination, dir.Substring(root.Length)));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("기존 파일에 링크가 있어 안전한 백업을 만들 수 없습니다.");
                string copied = Data.Under(destination, file.Substring(root.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(copied));
                File.Copy(file, copied, false);
                if (new FileInfo(copied).Length != new FileInfo(file).Length ||
                    !String.Equals(Data.Hash(file), Data.Hash(copied), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("기존 캠페인 백업의 무결성 검증에 실패했습니다: " + file);
            }
        }
        static void RequireUnmanagedFolder(string storageRoot, string gameRoot, string folder)
        {
            string prefix = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            string game = Path.GetFullPath(gameRoot).TrimEnd('\\') + "\\";
            if (!prefix.StartsWith(game, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Path.GetFileName(Path.GetDirectoryName(folder)), "Maps", StringComparison.OrdinalIgnoreCase) == false ||
                String.Equals(Path.GetFileName(folder), "Campaign", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("공용 게임 맵이나 CCM 관리 폴더는 이 기능으로 교체하지 않습니다.");
            var state = new Library(storageRoot).Current();
            if ((state.managed ?? new List<OwnedFile>()).Any(f =>
                Data.Under(gameRoot, f.path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                (state.files ?? new List<OwnedFile>()).Any(f =>
                    Data.Under(gameRoot, f.path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("현재 적용된 CCM 캠페인이 관리하는 파일이므로 교체하지 않았습니다.");
            var dedicated = new DedicatedLibrary(storageRoot).Current();
            if (dedicated.installed.Any(c => c.release != null && c.release.files != null &&
                c.release.files.Any(f => Data.Under(gameRoot, f.path)
                    .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
                throw new IOException("격납고가 관리하는 정식 전용 캠페인 폴더이므로 임의 교체하지 않았습니다.");
        }
        // Called only after the user explicitly selects "제거 후 설치".
        // The previous complete directory is preserved as a recoverable
        // backup. ZIP bytes are verified in a sibling staging directory
        // before the existing folder is moved out of the way.
        public static StandaloneZipResult ReplaceExistingMaps(StandaloneZipPackage package,
            StandaloneZipChoice choice, StandaloneExistingMaps existing, string storageRoot,
            string gameRoot, ExternalCampaigns external, Action<WorkProgress> progress = null,
            Action<int> fault = null)
        {
            if (choice == null || !package.Choices.Contains(choice) || choice.Kind != "map")
                throw new InvalidOperationException("교체할 시작 맵을 찾을 수 없습니다.");
            var current = ExistingMapVariant(package, choice, gameRoot);
            if (current == null || existing == null ||
                !String.Equals(current.Folder, existing.Folder, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(current.Launcher, existing.Launcher, StringComparison.OrdinalIgnoreCase))
                throw new IOException("설치된 캠페인이 변경됐습니다. ZIP을 다시 검사해 주세요.");
            var files = package.GameFiles;
            // Whole-directory replacement is safe only for a self-contained
            // map-only ZIP. Never silently discard external mod dependencies.
            if (files.Count != current.MapCount || files.Any(f => !f.IsMap ||
                !String.Equals(Path.GetDirectoryName(Data.Under(gameRoot, f.GamePath)),
                    current.Folder, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("추가 모드·여러 폴더가 섞인 ZIP은 자동 교체하지 않습니다.");
            ExternalCampaigns.RequireGameClosed(gameRoot);
            RequireUnmanagedFolder(storageRoot, gameRoot, current.Folder);
            var prior = external.All().FirstOrDefault(c => c.kind == "external" &&
                ExternalCampaigns.LaunchMode(c) == "map" &&
                String.Equals(c.launcherMap, current.Launcher, StringComparison.OrdinalIgnoreCase));
            string suffix = Guid.NewGuid().ToString("N");
            string parent = Path.GetDirectoryName(current.Folder);
            string stage = Path.Combine(parent, ".hangar-stage-" + suffix);
            string holding = Path.Combine(parent, ".hangar-previous-" + suffix);
            string backup = Data.Under(storageRoot, "standalone-backups/" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + suffix);
            bool movedOld = false, installedNew = false, backupReady = false;
            bool sameVolume = String.Equals(Path.GetPathRoot(backup), Path.GetPathRoot(current.Folder),
                StringComparison.OrdinalIgnoreCase);
            int created = 0;
            try
            {
                Directory.CreateDirectory(stage);
                using (var archive = ZipFile.OpenRead(package.Zip))
                {
                    long bytes = 0, total = files.Sum(f => f.Bytes);
                    foreach (var file in files)
                    {
                        string target = Data.Under(stage, Path.GetFileName(file.GamePath));
                        var entry = archive.GetEntry(file.Entry);
                        if (entry == null || entry.Length != file.Bytes)
                            throw new InvalidDataException("ZIP 파일 목록이 설치 준비 중 변경됐습니다.");
                        CopyEntry(entry, target, count => {
                            bytes += count;
                            if (progress != null && (bytes == total || bytes % (4L * 1024 * 1024) < count))
                                progress(new WorkProgress("새 캠페인 검증·설치 준비", bytes, total, true));
                        });
                        RequireMpq(target);
                        created++;
                    }
                }
                if (fault != null) fault(1);
                ExternalCampaigns.RequireGameClosed(gameRoot);
                RequireUnmanagedFolder(storageRoot, gameRoot, current.Folder);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                if (sameVolume)
                {
                    Directory.Move(current.Folder, backup);
                    movedOld = backupReady = true;
                }
                else
                {
                    // Cross-volume Directory.Move is not atomic. First make
                    // and verify a full copy; then move the live directory to
                    // its sibling so rollback remains possible on game volume.
                    CopyBackup(current.Folder, backup);
                    backupReady = true;
                    Directory.Move(current.Folder, holding);
                    movedOld = true;
                }
                if (fault != null) fault(2);
                Directory.Move(stage, current.Folder);
                installedNew = true;
                if (fault != null) fault(3);
                string hash = Data.Hash(package.Zip);
                Campaign result = prior != null ? external.Save(prior.id, prior.name,
                    current.Launcher, null, gameRoot, prior.installationMode ?? "manual",
                    "map", null, null, hash) : external.Save(null,
                    package.Name.Length > 120 ? package.Name.Substring(0, 120) : package.Name,
                    current.Launcher, null, gameRoot, "manual", "map", null, null, hash);
                // No source EXE or StarCraft II process is ever launched here.
                if (Directory.Exists(holding))
                {
                    // Do not turn a successful installation into a rollback
                    // solely because post-commit scratch cleanup was denied.
                    try { Directory.Delete(holding, true); } catch (IOException) { }
                }
                return new StandaloneZipResult { Campaign = result, AlreadyLinked = prior != null,
                    CreatedFiles = created, PreviousBackup = backup };
            }
            catch
            {
                // Restore the previous version rather than leave a half-installed
                // game. A successful backup is never deleted by this handler.
                if (movedOld)
                {
                    if (installedNew && Directory.Exists(current.Folder))
                        Directory.Move(current.Folder, stage);
                    string restore = sameVolume ? backup : holding;
                    if (!Directory.Exists(current.Folder) && Directory.Exists(restore))
                        Directory.Move(restore, current.Folder);
                }
                throw;
            }
            finally
            {
                if (Directory.Exists(stage) && (!movedOld || Directory.Exists(current.Folder)))
                    Directory.Delete(stage, true);
                // Keep verified cross-volume backup; it is the user's
                // recovery copy even if the rollback had to run.
                if (!backupReady && Directory.Exists(backup)) Directory.Delete(backup, true);
            }
        }
        public static string Summary(StandaloneZipPackage package, StandaloneZipChoice choice)
        {
            if (choice.Kind == "author-launcher")
                return package.Name + "\n\n찾은 제작자 런처:\n" + choice.Entry +
                    "\n\nZIP 구성 파일을 격납고 보관소의 별도 폴더로 압축 해제하고 실행 경로를 자동 연결합니다." +
                    "\n제작자 EXE·설치 스크립트는 자동 실행하지 않습니다. 원본 ZIP과 게임 파일도 변경하지 않습니다.";
            int maps = package.GameFiles.Count(f => f.IsMap), mods = package.GameFiles.Count(f => f.IsModArchive ||
                f.GamePath.IndexOf(".SC2Mod/", StringComparison.OrdinalIgnoreCase) >= 0);
            return package.Name + "\n\n찾은 시작 맵:\n" + (choice.Kind == "episodes" ?
                String.Join("\n", choice.Episodes) : choice.Entry) +
                "\n\n맵 " + maps + "개, 모드 관련 파일 " + mods + "개를 게임 Maps/Mods에 설치하고 연결합니다." +
                "\n기존 파일은 덮어쓰지 않으며 동일한 파일만 재사용합니다. 오류 시 이번에 만든 파일을 되돌립니다." +
                "\n원본 ZIP·Bank·세이브는 변경하지 않습니다. 스타2가 종료된 상태에서만 설치합니다.";
        }
        static void CopyEntry(ZipArchiveEntry entry, string output, Action<long> report)
        {
            using (var source = entry.Open())
            using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[128 * 1024];
                long size = 0;
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    size += read;
                    if (size > entry.Length) throw new InvalidDataException("ZIP 파일 크기 정보가 일치하지 않습니다.");
                    target.Write(buffer, 0, read);
                    if (report != null) report(read);
                }
                if (size != entry.Length) throw new InvalidDataException("ZIP 데이터가 끝나기 전에 읽기가 종료됐습니다.");
            }
        }
        static void RequireMpq(string file)
        {
            using (var stream = File.OpenRead(file))
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'P' ||
                    stream.ReadByte() != 'Q' || stream.ReadByte() != 26)
                    throw new InvalidDataException("ZIP에 유효한 MPQ 캠페인 맵/모드가 아닙니다: " + Path.GetFileName(file));
        }
        static bool SameConnection(Campaign item, StandaloneZipChoice choice, string start, string[] episodes)
        {
            if (item.kind != "external" || ExternalCampaigns.LaunchMode(item) != choice.Kind) return false;
            if (choice.Kind == "episodes") return item.episodeMaps != null && item.episodeMaps.Length == 3 &&
                item.episodeMaps.SequenceEqual(episodes, IgnoreCase);
            return String.Equals(choice.Kind == "map" ? item.launcherMap : item.launcherExecutable,
                start, StringComparison.OrdinalIgnoreCase);
        }
        public static StandaloneZipResult Install(StandaloneZipPackage package, StandaloneZipChoice choice,
            string storageRoot, string gameRoot, ExternalCampaigns external, Action<WorkProgress> progress = null)
        {
            if (choice == null || !package.Choices.Contains(choice)) throw new InvalidOperationException("ZIP에서 찾은 시작점을 선택해 주세요.");
            bool author = choice.Kind == "author-launcher";
            if (!author && !File.Exists(Path.Combine(gameRoot ?? "", "StarCraft II.exe")))
                throw new InvalidOperationException("설정에서 스타2 게임 폴더를 먼저 선택해 주세요.");
            string baseFolder = null, executable = null;
            string archiveHash = Data.Hash(package.Zip);
            string start = null, installMode = author ? "author-launcher" : "manual";
            string[] episodes = null;
            if (author)
            {
                // A content-addressed managed copy is independent of the
                // creator's original ZIP. It is never executed by the importer.
                baseFolder = Data.Under(storageRoot, "standalone-zips/" + archiveHash);
                executable = Data.Under(baseFolder, choice.Entry);
                start = executable;
            }
            else
            {
                var selected = package.GameFiles.Single(f => f.Entry == choice.Entry);
                start = Data.Under(gameRoot, selected.GamePath);
                if (choice.Kind == "episodes")
                    episodes = choice.Episodes.Select(entry => Data.Under(gameRoot,
                        package.GameFiles.Single(f => f.Entry == entry).GamePath)).ToArray();
            }
            var prior = external.All().FirstOrDefault(c => SameConnection(c, choice, start, episodes));
            if (prior != null)
            {
                if (prior.standaloneZipHash != archiveHash)
                    throw new IOException("이 시작점은 다른 ZIP 또는 기존 수동 연결에서 사용 중입니다. 기존 연결을 확인해 주세요.");
                return new StandaloneZipResult { Campaign = prior, AlreadyLinked = true };
            }
            if (!author) ExternalCampaigns.RequireGameClosed(gameRoot);

            string stage = null;
            var created = new List<string>();
            long completed = 0, total = author ? package.TotalBytes : package.GameFiles.Sum(f => f.Bytes);
            Action<long> advance = count => {
                completed += count;
                if (progress != null && (completed == total || completed % (4L * 1024 * 1024) < count))
                    progress(new WorkProgress("전용 캠페인 파일 확인·설치", completed, total, true));
            };
            try
            {
                using (var archive = ZipFile.OpenRead(package.Zip))
                {
                    if (author)
                    {
                        if (!Directory.Exists(baseFolder))
                        {
                            stage = Data.Under(storageRoot, "standalone-zips/stage-" + Guid.NewGuid().ToString("N"));
                            Directory.CreateDirectory(stage);
                            foreach (var entry in archive.Entries.Where(e => !e.FullName.EndsWith("/", StringComparison.Ordinal)))
                            {
                                Parts(entry.FullName); // Validate again before creating each file.
                                if (entry.FullName.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase)) continue;
                                string destination = Data.Under(stage, entry.FullName);
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                CopyEntry(entry, destination, advance);
                            }
                            ExternalCampaigns.ValidateLauncher(Data.Under(stage, choice.Entry));
                            Directory.Move(stage, baseFolder);
                            stage = null; created.Add(baseFolder);
                        }
                        ExternalCampaigns.ValidateLauncher(executable);
                    }
                    else
                    {
                        foreach (var file in package.GameFiles)
                        {
                            string destination = Data.Under(gameRoot, file.GamePath);
                            var entry = archive.GetEntry(file.Entry);
                            if (entry == null || entry.Length != file.Bytes)
                                throw new InvalidDataException("설치 중 ZIP 파일 구성이 변경됐습니다.");
                            string scratch = Data.Under(gameRoot, file.GamePath + ".hangar-" + Guid.NewGuid().ToString("N") + ".tmp");
                            Directory.CreateDirectory(Path.GetDirectoryName(scratch));
                            try
                            {
                                CopyEntry(entry, scratch, advance);
                                if (file.IsMap || file.IsModArchive) RequireMpq(scratch);
                                // Same-byte dependencies may already belong to another
                                // campaign. A different file is NEVER overwritten.
                                if (File.Exists(destination))
                                {
                                    if (new FileInfo(destination).Length != file.Bytes ||
                                        Data.Hash(destination) != Data.Hash(scratch))
                                        throw new IOException("다른 내용의 게임 파일이 이미 있습니다. 덮어쓰지 않았습니다: " + destination);
                                }
                                else
                                {
                                    File.Move(scratch, destination);
                                    created.Add(destination);
                                }
                            }
                            finally { if (File.Exists(scratch)) File.Delete(scratch); }
                        }
                    }
                }
                var name = package.Name.Length > 120 ? package.Name.Substring(0, 120) : package.Name;
                Campaign saved = external.Save(null, name,
                    choice.Kind == "map" ? start : null, null, gameRoot,
                    installMode, choice.Kind, author ? executable : null, episodes, archiveHash);
                return new StandaloneZipResult { Campaign = saved, CreatedFiles = created.Count };
            }
            catch
            {
                // Roll back only paths created during THIS attempt.  Never
                // delete a pre-existing game file, a ZIP, a Bank or a save.
                foreach (var file in Enumerable.Reverse(created))
                {
                    if (Directory.Exists(file)) Directory.Delete(file, true);
                    else if (File.Exists(file)) File.Delete(file);
                }
                throw;
            }
            finally
            {
                if (stage != null && Directory.Exists(stage)) Directory.Delete(stage, true);
            }
        }
    }

    public sealed class StandaloneChoiceDialog : ThemeForm
    {
        readonly ListBox options;
        public StandaloneZipChoice Selected { get { return options.SelectedItem as StandaloneZipChoice; } }
        public StandaloneChoiceDialog(StandaloneZipPackage package)
        {
            Text = "ZIP 시작점 자동 인식"; ClientSize = new Size(780, 440);
            MinimumSize = new Size(620, 360); ShowInTaskbar = false;
            Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
            Padding = new Padding(22); BackColor = GlassTheme.Background;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            var message = new Label { Dock = DockStyle.Fill, ForeColor = GlassTheme.Text,
                Text = "ZIP 안에서 실행 시작점을 여러 개 찾았습니다.\n원하는 시작점을 선택하세요. 파일을 다시 찾아 연결할 필요는 없습니다.",
                TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(message, 0, 0);
            options = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false,
                BackColor = GlassTheme.Field, ForeColor = GlassTheme.Text,
                BorderStyle = BorderStyle.FixedSingle, Font = new Font("맑은 고딕", 10.5f),
                HorizontalScrollbar = true };
            foreach (var candidate in package.Choices) options.Items.Add(candidate);
            if (options.Items.Count > 0) options.SelectedIndex = 0;
            layout.Controls.Add(options, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 12, 0, 0) };
            var accept = new MetalButton { Text = "이 시작점 사용", DialogResult = DialogResult.OK, Primary = true,
                MinimumSize = new Size(174, 48), AutoSize = true };
            var cancel = new MetalButton { Text = "취소", DialogResult = DialogResult.Cancel,
                MinimumSize = new Size(120, 48), AutoSize = true };
            buttons.Controls.Add(cancel); buttons.Controls.Add(accept); layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout); AcceptButton = accept; CancelButton = cancel;
        }
    }
}
