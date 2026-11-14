using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace CampaignHangar
{
    public sealed class StoragePreference
    {
        public string path { get; set; }
        public string gameRoot { get; set; }
        public string downloadsPath { get; set; }
    }
    public sealed class WorkProgress
    {
        public string stage;
        public long completed, total;
        public bool bytes;
        public WorkProgress(string stage, long completed = 0, long total = 0, bool bytes = false)
        { this.stage = stage; this.completed = completed; this.total = total; this.bytes = bytes; }
    }
    public sealed class StorageMoveResult
    {
        public string backup, warning;
    }
    public sealed class WindowPreference
    {
        public int? left {get;set;}
        public int? top {get;set;}
        public int width { get; set; }
        public int height { get; set; }
        public bool maximized { get; set; }
    }
    public sealed class ApplyPreference
    {
        public bool showConfirmation { get; set; }
        public bool autoApplyAfterInstall { get; set; }
        public bool autoRemovePreviousVersions { get; set; }
        public ApplyPreference() { showConfirmation = true; }
    }
    public static class WindowSize
    {
        public static void Save(Form form, string path)
        {
            var bounds=form.WindowState==FormWindowState.Normal?form.Bounds:form.RestoreBounds;
            Data.Write(path, new WindowPreference {left=bounds.Left,top=bounds.Top,
                width=bounds.Width,height=bounds.Height,maximized=form.WindowState==FormWindowState.Maximized});
        }
        public static void Restore(Form form, string path)
        {
            if (!File.Exists(path)) return;
            Restore(form,Data.Read<WindowPreference>(path));
        }
        public static void Restore(Form form, WindowPreference value)
        {
            if (value.width <= 0 || value.height <= 0) throw new InvalidDataException("저장된 창 크기가 올바르지 않습니다.");
            var saved=new Rectangle(value.left??form.Left,value.top??form.Top,value.width,value.height);
            var screen=(value.left.HasValue&&value.top.HasValue?Screen.FromRectangle(saved):Screen.FromControl(form)).WorkingArea;
            int maxWidth=form.MaximumSize.Width>0?Math.Min(screen.Width,form.MaximumSize.Width):screen.Width;
            int maxHeight=form.MaximumSize.Height>0?Math.Min(screen.Height,form.MaximumSize.Height):screen.Height;
            form.Size = new Size(Math.Max(form.MinimumSize.Width, Math.Min(maxWidth, value.width)),
                Math.Max(form.MinimumSize.Height, Math.Min(maxHeight, value.height)));
            if(value.left.HasValue&&value.top.HasValue){
                form.StartPosition=FormStartPosition.Manual;
                form.Location=new Point(Math.Max(screen.Left,Math.Min(screen.Right-form.Width,value.left.Value)),
                    Math.Max(screen.Top,Math.Min(screen.Bottom-form.Height,value.top.Value)));
            }
            if (value.maximized&&form.MaximizeBox) form.WindowState = FormWindowState.Maximized;
        }
    }
    public static class Storage
    {
        public static readonly string LegacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CampaignHangar-Preview");
        public static readonly string PreferenceFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CampaignHangar", "storage.json");
        public static readonly string DownloadPreferenceFile = Path.Combine(Path.GetDirectoryName(PreferenceFile), "downloads.json");
        public const string RelocationWarning = "보관소는 StarCraft II 게임 폴더 안이나 그 하위 폴더로 지정하지 마세요.\n게임과 다른 드라이브로 보관소를 변경하면 캠페인 교체 시간이 오래 걸릴 수 있습니다.";
        public static string[] BankDirectories(string documents)
        {
            if (String.IsNullOrWhiteSpace(documents) || !Path.IsPathRooted(documents))
                throw new ArgumentException("문서 폴더의 전체 경로가 필요합니다.");
            string accounts = Path.Combine(documents, "StarCraft II", "Accounts");
            if (!Directory.Exists(accounts)) return new string[0];
            var result = new List<string>();
            foreach (string account in Directory.GetDirectories(accounts))
                foreach (string profile in Directory.GetDirectories(account))
                {
                    string banks = Path.Combine(profile, "Banks");
                    if (Directory.Exists(banks)) result.Add(banks);
                }
            return result.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        public static string DefaultForGame(string gameRoot)
        {
            gameRoot = Path.GetFullPath(gameRoot);
            if (!File.Exists(Path.Combine(gameRoot, "StarCraft II.exe")))
                throw new IOException("StarCraft II.exe가 있는 게임 폴더를 먼저 선택해 주세요.");
            string besideGame = Path.Combine(Directory.GetParent(gameRoot.TrimEnd('\\')).FullName, AppChannel.StorageFolder);
            string userStorage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppChannel.Profile, AppChannel.StorageFolder);
            return WritableDestination(besideGame, userStorage);
        }

        public static string DefaultDownloadsForGame(string gameRoot)
        {
            gameRoot = Path.GetFullPath(gameRoot);
            string inGame = Path.Combine(gameRoot, AppChannel.DownloadsFolder);
            string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppChannel.Profile, AppChannel.DownloadsFolder);
            return WritableDestination(inGame, userDownloads);
        }

        // When SC2 lives in Program Files or another protected location,
        // ordinary accounts cannot create a sibling CampaignStorage or a
        // Downloads directory inside the game. Probe only a temporary file in
        // the nearest existing directory; never modify game files or ACLs.
        internal static string WritableDestination(string preferred, string fallback)
        {
            return CanWriteAt(preferred) ? preferred : fallback;
        }

        internal static bool CanWriteAt(string desired)
        {
            string existing = Path.GetFullPath(desired);
            while (!Directory.Exists(existing))
            {
                if (File.Exists(existing)) return false;
                // Directory.Exists can return false for an inaccessible folder,
                // not just a missing one. Do not mistake its writable parent
                // for permission to write inside that protected directory.
                try { File.GetAttributes(existing); return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                existing = Path.GetDirectoryName(existing);
                if (String.IsNullOrEmpty(existing)) return false;
            }
            string probe = Path.Combine(existing, ".hangar-access-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 1, FileOptions.DeleteOnClose)) file.WriteByte(0);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }
        static string BackupPath(string destination)
        {
            string parent = Path.GetDirectoryName(destination);
            for (int number = 1; number < 10000; number++)
            {
                string path = Path.Combine(parent, "CSBackup" + number.ToString("D3"));
                if (!Directory.Exists(path) && !File.Exists(path)) return path;
            }
            throw new IOException("백업 폴더 이름을 만들 수 없습니다.");
        }
        public static string ResolveGame(string preferenceFile, string fallback)
        {
            string configured = File.Exists(preferenceFile) ? Data.Read<StoragePreference>(preferenceFile).gameRoot : null;
            return String.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }
        public static StorageMoveResult SelectGame(string source, string gameRoot, string preferenceFile, Action<WorkProgress> progress = null, bool configureDownloads = true)
        {
            string destination = DefaultForGame(gameRoot);
            string stateFile = Path.Combine(source, "active.json");
            var active = File.Exists(stateFile) ? Data.Read<State>(stateFile) : new State();
            if (active.active != null && !String.Equals(Path.GetFullPath(active.gameRoot).TrimEnd('\\'), Path.GetFullPath(gameRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new IOException("다른 게임 폴더에 적용 중인 캠페인을 먼저 해제한 뒤 게임 경로를 변경해 주세요.");
            string downloads = configureDownloads ? DefaultDownloadsForGame(gameRoot) : null;
            if (downloads != null) CheckWritable(downloads);
            return Relocate(source, destination, preferenceFile, progress, true, Path.GetFullPath(gameRoot), downloads);
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumePathName(string path, StringBuilder volumePath, int length);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, int length);
        static string Volume(string path)
        {
            var directory = new DirectoryInfo(Path.GetFullPath(path));
            while (!directory.Exists) directory = directory.Parent;
            var mount = new StringBuilder(1024); var volume = new StringBuilder(1024);
            if (!GetVolumePathName(directory.FullName, mount, mount.Capacity) ||
                !GetVolumeNameForVolumeMountPoint(mount.ToString(), volume, volume.Capacity))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return volume.ToString();
        }
        internal static bool SameVolume(string source, string destination)
        {
            if (Path.GetFullPath(source).StartsWith(@"\\") || Path.GetFullPath(destination).StartsWith(@"\\")) return false;
            return String.Equals(Volume(source), Volume(destination), StringComparison.OrdinalIgnoreCase);
        }
        internal static string RelocationDescription(string source, string destination)
        {
            return SameVolume(source, destination)
                ? "같은 드라이브에서는 보관소 폴더를 이동합니다. 이전 위치에 복사본은 남기지 않습니다."
                : "다른 드라이브에서는 복사·검증 후 이전 보관소를 정리합니다. 파일이 많으면 오래 걸릴 수 있습니다.";
        }
        static void ValidateGameBoundary(string source, string destination, string preferenceFile, string gameRoot = null)
        {
            gameRoot = gameRoot ?? ResolveGame(preferenceFile, null);
            string active = Path.Combine(source, "active.json");
            if (String.IsNullOrWhiteSpace(gameRoot) && File.Exists(active)) gameRoot = Data.Read<State>(active).gameRoot;
            if (String.IsNullOrWhiteSpace(gameRoot)) return;
            string game = Path.GetFullPath(gameRoot).TrimEnd('\\');
            string target = Path.GetFullPath(destination).TrimEnd('\\');
            if (String.Equals(game, target, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(game + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("스타2 업데이트를 방해하지 않도록 보관소는 게임 폴더 밖에 지정해 주세요.");
        }
        public static StorageMoveResult Relocate(string source, string destination, string preferenceFile,
            Action<WorkProgress> progress = null, bool allowExisting = false, string gameRoot = null, string downloadsPath = null)
        {
            source = Path.GetFullPath(source).TrimEnd('\\'); destination = Path.GetFullPath(destination).TrimEnd('\\');
            var old = File.Exists(preferenceFile) ? Data.Read<StoragePreference>(preferenceFile) : new StoragePreference();
            ValidateGameBoundary(source, destination, preferenceFile, gameRoot);
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                Select(destination, preferenceFile, gameRoot, downloadsPath);
                return new StorageMoveResult();
            }
            ValidateSelection(source, destination);
            if (String.Equals(source, Path.GetPathRoot(source).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(preferenceFile).StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
                throw new IOException("드라이브 전체나 앱 설정을 포함한 폴더는 이동할 수 없습니다.");
            if (!allowExisting && Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length != 0)
                throw new IOException("파일이 없는 빈 폴더를 선택해 주세요.");
            ValidateTree(source);
            if (Directory.Exists(destination))
            {
                ValidateTree(destination);
                if (File.Exists(Path.Combine(destination, "transaction", "journal.json")))
                    throw new IOException("대상 보관소의 미완료 전환을 먼저 복구해 주세요.");
            }
            string configuredDownloads = ResolveDownloads(Path.Combine(Path.GetDirectoryName(preferenceFile), "downloads.json"), preferenceFile);
            if (String.Equals(configuredDownloads.TrimEnd('\\'), source, StringComparison.OrdinalIgnoreCase))
                throw new IOException("다운로드 폴더를 보관소 안의 별도 폴더로 지정한 뒤 이동해 주세요.");
            var keep = new List<string> { "downloads" };
            if (configuredDownloads.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase))
                keep.Add(configuredDownloads.Substring(source.Length + 1));
            keep = keep.Where(relative => Directory.Exists(Path.Combine(source, relative)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(relative => !keep.Any(other => !String.Equals(relative, other, StringComparison.OrdinalIgnoreCase) &&
                    relative.StartsWith(other + "\\", StringComparison.OrdinalIgnoreCase))).ToList();
            var result = new StorageMoveResult();
            if (!SameVolume(source, destination))
            {
                var snapshot = StorageFiles(source).Where(file => !Kept(file.Substring(source.Length + 1), keep))
                    .ToDictionary(file => file.Substring(source.Length + 1), Data.Hash, StringComparer.OrdinalIgnoreCase);
                result.backup = RestoreDefault(source, preferenceFile, destination, gameRoot, progress, downloadsPath);
                try
                {
                    RemapZipPaths(source, destination, keep);
                    if (progress != null) progress(new WorkProgress("이전 보관소 정리 중"));
                    foreach (var file in snapshot)
                    {
                        string path = Path.Combine(source, file.Key);
                        if (Data.Hash(path) != file.Value) throw new IOException("이동 중 원본 파일이 변경됐습니다: " + file.Key);
                        File.Delete(path);
                    }
                    RemoveEmptyDirectories(source);
                    if (Directory.Exists(source) && StorageFiles(source).Any(file => !Kept(file.Substring(source.Length + 1), keep)))
                        result.warning = "새 보관소로 전환했지만 이동 중 추가된 파일은 이전 위치에 남겼습니다: " + source;
                }
                catch (Exception error)
                { result.warning = "새 보관소는 사용 가능하지만 이전 위치 일부를 정리하지 못했습니다: " + source + "\n" + error.Message; }
                return result;
            }
            string hold = source + ".downloads-" + Guid.NewGuid().ToString("N");
            string backup = Directory.Exists(destination) ? BackupPath(destination) : null;
            bool saved = false, moved = false, targetDownloads = false;
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            try
            {
                MoveKept(source, hold, keep);
                if (backup != null) { Directory.Move(destination, backup); saved = true; }
                if (progress != null) progress(new WorkProgress("보관소 폴더 이동 중"));
                Directory.Move(source, destination); moved = true;
                MoveKept(hold, source, keep);
                if (saved && Directory.Exists(Path.Combine(backup, "downloads")))
                {
                    Directory.Move(Path.Combine(backup, "downloads"), Path.Combine(destination, "downloads"));
                    targetDownloads = true;
                }
                if (progress != null) progress(new WorkProgress("경로 설정 저장 중"));
                Select(destination, preferenceFile, gameRoot ?? old.gameRoot, downloadsPath ?? old.downloadsPath);
                result.backup = backup;
                try { RemapZipPaths(source, destination, keep); }
                catch (Exception error) { result.warning = "보관소는 이동했지만 ZIP 경로 기록을 갱신하지 못했습니다.\n" + error.Message; }
                return result;
            }
            catch
            {
                if (targetDownloads) Directory.Move(Path.Combine(destination, "downloads"), Path.Combine(backup, "downloads"));
                if (moved)
                {
                    MoveKept(source, hold, keep);
                    RemoveEmptyDirectories(source);
                    Directory.Move(destination, source);
                }
                if (saved) Directory.Move(backup, destination);
                MoveKept(hold, source, keep);
                throw;
            }
            finally { RemoveEmptyDirectories(hold); }
        }
        static bool Kept(string relative, IEnumerable<string> keep)
        {
            return keep.Any(path => String.Equals(path, relative, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase));
        }
        static void RemapZipPaths(string source, string destination, IEnumerable<string> keep)
        {
            string path = Path.Combine(destination, "source-zips.json");
            if (!File.Exists(path)) return;
            var zips = Data.Read<List<RegisteredZip>>(path);
            bool changed = false;
            foreach (var zip in zips)
                if (zip.path.StartsWith(source + "\\", StringComparison.OrdinalIgnoreCase) &&
                    !Kept(zip.path.Substring(source.Length + 1), keep))
                { zip.path = Path.Combine(destination, zip.path.Substring(source.Length + 1)); changed = true; }
            if (changed) Data.Write(path, zips);
        }
        static void MoveKept(string source, string destination, IEnumerable<string> keep)
        {
            foreach (string relative in keep)
            {
                string path = Path.Combine(source, relative);
                if (!Directory.Exists(path)) continue;
                string target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                Directory.Move(path, target);
            }
        }
        static void RemoveEmptyDirectories(string root)
        {
            if (!Directory.Exists(root)) return;
            if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결 폴더는 정리하지 않습니다: " + root);
            foreach (string directory in Directory.GetDirectories(root)) RemoveEmptyDirectories(directory);
            if (!Directory.EnumerateFileSystemEntries(root).Any()) Directory.Delete(root);
        }
        public static string ResolveDownloads(string preferenceFile, string storagePreferenceFile = null)
        {
            string gameRoot = null;
            if (storagePreferenceFile != null && File.Exists(storagePreferenceFile))
            {
                var value = Data.Read<StoragePreference>(storagePreferenceFile);
                var configured = value.downloadsPath;
                if (!String.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
                gameRoot = value.gameRoot;
            }
            return File.Exists(preferenceFile) ? Path.GetFullPath(Data.Read<StoragePreference>(preferenceFile).path)
                : String.IsNullOrWhiteSpace(gameRoot) ? Path.Combine(LegacyRoot, "downloads") : DefaultDownloadsForGame(gameRoot);
        }
        public static void SelectDownloads(string destination, string preferenceFile, string storagePreferenceFile = null)
        {
            destination = Path.GetFullPath(destination);
            CheckWritable(destination);
            if (storagePreferenceFile != null)
            {
                var value = File.Exists(storagePreferenceFile) ? Data.Read<StoragePreference>(storagePreferenceFile)
                    : new StoragePreference { path = LegacyRoot };
                value.downloadsPath = destination;
                Data.Write(storagePreferenceFile, value);
            }
            else Data.Write(preferenceFile, new StoragePreference { path = destination });
        }
        static void CheckWritable(string destination)
        {
            Directory.CreateDirectory(destination);
            string probe = Path.Combine(destination, ".hangar-write-" + Guid.NewGuid().ToString("N"));
            using (var file = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) file.WriteByte(0);
        }
        public static string Resolve(string preferenceFile)
        {
            return File.Exists(preferenceFile) ? Path.GetFullPath(Data.Read<StoragePreference>(preferenceFile).path) : LegacyRoot;
        }
        public static int ConnectExisting(string source, string destination, string preferenceFile)
        {
            source = Path.GetFullPath(source).TrimEnd('\\');
            destination = Path.GetFullPath(destination).TrimEnd('\\');
            ValidateSelection(source, destination);
            if (!Directory.Exists(Path.Combine(destination, "packages")))
                throw new IOException("격납고 보관소의 packages 폴더가 없습니다.");
            if (File.Exists(Path.Combine(destination, "transaction", "journal.json")))
                throw new IOException("대상 보관소의 미완료 전환을 먼저 복구해 주세요.");
            var library = new Library(destination);
            var campaigns = library.All();
            if (campaigns.Count == 0 || campaigns.Any(item => item == null || String.IsNullOrWhiteSpace(item.id) ||
                String.IsNullOrWhiteSpace(item.name) || String.IsNullOrWhiteSpace(item.versionKey) || item.files == null))
                throw new InvalidDataException("인식할 캠페인 목록이 없거나 보관 정보가 올바르지 않습니다.");
            if (campaigns.Select(item => item.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != campaigns.Count)
                throw new InvalidDataException("보관소의 캠페인 ID가 중복되었습니다.");
            foreach (var item in campaigns)
            {
                if (!String.Equals(library.Installed(item.id).versionKey, item.versionKey, StringComparison.Ordinal) ||
                    !library.Versions(item.id).Any(version => version.versionKey == item.versionKey))
                    throw new InvalidDataException("캠페인의 보관 버전 정보가 일치하지 않습니다.");
                library.VersionFolder(item);
                // Applied versions may have their deployment files moved into the game.
                // Reconnection reads the library metadata; activation validates actual files.
            }
            var active = library.Current();
            string game = ResolveGame(preferenceFile, null);
            if (!String.IsNullOrWhiteSpace(active.gameRoot))
            {
                if (!String.IsNullOrWhiteSpace(game) && active.active != null &&
                    !String.Equals(Path.GetFullPath(game).TrimEnd('\\'), Path.GetFullPath(active.gameRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("적용 기록의 게임 경로가 현재 설정과 다릅니다. 같은 게임 경로를 먼저 선택해 주세요.");
                if (String.IsNullOrWhiteSpace(game)) game = active.gameRoot;
            }
            ValidateGameBoundary(source, destination, preferenceFile, game);
            Select(destination, preferenceFile, game);
            return campaigns.Count;
        }
        public static void CopyAndSelect(string source, string destination, string preferenceFile, Action<WorkProgress> progress = null)
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            destination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar);
            ValidateGameBoundary(source, destination, preferenceFile);
            ValidateSelection(source, destination);
            if (Directory.Exists(destination) && Directory.GetFileSystemEntries(destination).Length != 0)
                throw new IOException("파일이 없는 빈 폴더를 선택해 주세요.");
            ValidateTree(source);
            Directory.CreateDirectory(destination);
            CopyVerified(source, destination, progress);
            if (progress != null) progress(new WorkProgress("경로 설정 저장 중"));
            Select(destination, preferenceFile);
        }
        public static string RestoreDefault(string source, string preferenceFile, string defaultRoot, string gameRoot = null,
            Action<WorkProgress> progress = null, string downloadsPath = null)
        {
            source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
            string destination = Path.GetFullPath(defaultRoot).TrimEnd(Path.DirectorySeparatorChar);
            ValidateGameBoundary(source, destination, preferenceFile, gameRoot);
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                if (gameRoot != null) Select(destination, preferenceFile, gameRoot, downloadsPath);
                return null;
            }
            ValidateSelection(source, destination);
            if (progress != null) progress(new WorkProgress("보관소 파일 확인 중"));
            ValidateTree(source);
            if (Directory.Exists(destination))
            {
                if (File.Exists(Path.Combine(destination, "transaction", "journal.json")))
                    throw new IOException("기본 보관소에 미완료 전환이 있습니다. 해당 보관소를 먼저 복구해 주세요.");
                ValidateTree(destination);
            }
            string stage = destination + ".restore-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(stage);
            CopyVerified(source, stage, progress);
            string backup = Directory.Exists(destination) ? BackupPath(destination) : null;
            bool saved = false, downloadsMoved = false, published = false;
            try
            {
                if (backup != null)
                {
                    Directory.Move(destination, backup); saved = true;
                    if (Directory.Exists(Path.Combine(backup, "downloads")))
                    {
                        Directory.Move(Path.Combine(backup, "downloads"), Path.Combine(stage, "downloads"));
                        downloadsMoved = true;
                    }
                }
                Directory.Move(stage, destination); published = true;
                if (progress != null) progress(new WorkProgress("경로 설정 저장 중"));
                Select(destination, preferenceFile, gameRoot, downloadsPath);
                return backup;
            }
            catch
            {
                if (published) Directory.Move(destination, stage);
                if (downloadsMoved) Directory.Move(Path.Combine(stage, "downloads"), Path.Combine(backup, "downloads"));
                if (saved) Directory.Move(backup, destination);
                throw;
            }
        }
        static void ValidateSelection(string source, string destination)
        {
            if (String.Equals(source, destination, StringComparison.OrdinalIgnoreCase) ||
                destination.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                source.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                String.Equals(destination, Path.GetPathRoot(destination).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new IOException("현재 저장소와 겹치거나 드라이브 최상위인 경로는 선택할 수 없습니다.");
            if (File.Exists(Path.Combine(source, "transaction", "journal.json")))
                throw new IOException("진행 중인 전환을 먼저 복구한 뒤 저장소를 변경해 주세요.");
            for (var parent = new DirectoryInfo(destination); parent != null; parent = parent.Parent)
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("연결 폴더는 저장소로 선택할 수 없습니다.");
        }
        static void Select(string destination, string preferenceFile, string gameRoot = null, string downloadsPath = null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(preferenceFile));
            string temporary = preferenceFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var old = File.Exists(preferenceFile) ? Data.Read<StoragePreference>(preferenceFile) : new StoragePreference();
            Data.Write(temporary, new StoragePreference { path = destination, gameRoot = gameRoot ?? old.gameRoot,
                downloadsPath = downloadsPath ?? old.downloadsPath });
            if (File.Exists(preferenceFile)) File.Replace(temporary, preferenceFile, null);
            else File.Move(temporary, preferenceFile);
        }
        static void ValidateTree(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("저장소의 연결 폴더·파일은 복사할 수 없습니다.");
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("저장소의 연결 폴더·파일은 복사할 수 없습니다.");
                if (Directory.Exists(entry)) ValidateTree(entry);
            }
        }
        static void CopyVerified(string source, string destination, Action<WorkProgress> progress)
        {
            string excluded = Path.Combine(source, "downloads");
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                if (!String.Equals(directory, excluded, StringComparison.OrdinalIgnoreCase) &&
                    !directory.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    Directory.CreateDirectory(Path.Combine(destination, directory.Substring(source.Length).TrimStart('\\')));
            var files = StorageFiles(source).ToList();
            int copied = 0;
            foreach (string file in files)
            {
                string output = Path.Combine(destination, file.Substring(source.Length).TrimStart('\\'));
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                if (progress != null) progress(new WorkProgress("복사 중 · " + Path.GetFileName(file), copied, files.Count));
                File.Copy(file, output, false);
                if (progress != null) progress(new WorkProgress("검증 중 · " + Path.GetFileName(file), copied, files.Count));
                if (Data.Hash(file) != Data.Hash(output)) throw new IOException("복사한 파일 검증에 실패했습니다. 기존 저장소를 유지합니다.");
                copied++;
                if (progress != null) progress(new WorkProgress("파일 복사·검증", copied, files.Count));
            }
        }
        static IEnumerable<string> StorageFiles(string source, bool storageRoot = true)
        {
            foreach (string file in Directory.GetFiles(source)) yield return file;
            foreach (string directory in Directory.GetDirectories(source))
            {
                if (storageRoot && String.Equals(Path.GetFileName(directory), "downloads", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string file in StorageFiles(directory, false)) yield return file;
            }
        }
    }
}
