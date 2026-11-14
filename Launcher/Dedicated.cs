using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace CampaignHangar
{
    public sealed class DedicatedBundle
    {
        public string url { get; set; }
        public string sha256 { get; set; }
        public long bytes { get; set; }
        public Dictionary<string, string> entries { get; set; }
    }
    public sealed class DedicatedFile
    {
        public string path { get; set; }
        public string sha256 { get; set; }
        public long bytes { get; set; }
    }
    public sealed class DedicatedLaunch
    {
        public string label { get; set; }
        public string path { get; set; }
    }
    public sealed class DedicatedRelease
    {
        public int schema { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public string voice { get; set; }
        public List<DedicatedBundle> packages { get; set; }
        public List<DedicatedFile> files { get; set; }
        public List<DedicatedLaunch> launches { get; set; }
    }
    public sealed class DedicatedInstalled
    {
        public string id { get; set; }
        public string version { get; set; }
        public string manifestSha256 { get; set; }
        public DedicatedRelease release { get; set; }
    }
    public sealed class DedicatedOriginal
    {
        public string path { get; set; }
        public string sha256 { get; set; }
        public string backup { get; set; }
    }
    public sealed class DedicatedState
    {
        public string gameRoot { get; set; }
        // Installed campaign whose incompatible shared SCMR cinematics version
        // is currently deployed. Other campaigns remain registered.
        public string activeVariantId { get; set; }
        public List<DedicatedInstalled> installed { get; set; }
        public List<DedicatedOriginal> originals { get; set; }
        public DedicatedState()
        {
            installed = new List<DedicatedInstalled>();
            originals = new List<DedicatedOriginal>();
        }
    }
    public sealed class DedicatedChange
    {
        public string path { get; set; }
        public string beforeHash { get; set; }
        public string afterHash { get; set; }
    }
    public sealed class DedicatedJournal
    {
        public string gameRoot { get; set; }
        public DedicatedState before { get; set; }
        public List<DedicatedChange> changes { get; set; }
        public bool committed { get; set; }
    }

    public sealed class DedicatedConflict
    {
        public readonly string campaignId, campaignName, path;
        public DedicatedConflict(string campaignId, string campaignName, string path)
        { this.campaignId = campaignId; this.campaignName = campaignName; this.path = path; }
    }

    // Standalone files are managed independently; CCM restoration requires an explicit conflict decision.
    public sealed class DedicatedLibrary
    {
        public const string ReleasePrefix = "https://github.com/gunee0229-png/sc2-dedicated-campaigns-ko/releases/download/";
        public const int InstallerVersion = 4;
        readonly string root, statePath, transaction;
        readonly Dictionary<string, DedicatedRelease> checkedReleases = new Dictionary<string, DedicatedRelease>();
        const string SwitchableCinematics = "Mods/SCMRcinematics.SC2Mod";
        static bool VariantPath(string path) { return String.Equals(path, SwitchableCinematics, StringComparison.OrdinalIgnoreCase); }
        string VariantBackup(string hash)
        {
            if (!HashText(hash)) throw new InvalidDataException("공용 모드의 검증값이 올바르지 않습니다.");
            return Data.Under(root, "variants/" + hash + ".SC2Mod");
        }
        void RememberVariant(string source, string expectedHash)
        {
            string cached = VariantBackup(expectedHash);
            if (File.Exists(cached))
            {
                if (Data.Hash(cached) != expectedHash) throw new IOException("공용 모드 보관본의 검증값이 다릅니다: " + cached);
                return;
            }
            if (!File.Exists(source) || Data.Hash(source) != expectedHash)
                throw new IOException("공용 모드 원본 파일의 SHA256 검증에 실패했습니다: " + source);
            Directory.CreateDirectory(Path.GetDirectoryName(cached));
            string temporary = cached + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                Data.Copy(source, temporary);
                if (Data.Hash(temporary) != expectedHash)
                    throw new IOException("공용 모드 보관본의 SHA256 검증에 실패했습니다.");
                File.Move(temporary, cached);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public DedicatedLibrary(string storage)
        {
            root = Data.Under(storage, "dedicated");
            statePath = Path.Combine(root, "installed.json");
            transaction = Path.Combine(root, "transaction");
        }
        static bool HashText(string value) { return value != null && Regex.IsMatch(value, "^[a-f0-9]{64}$"); }
        static void Identity(string id)
        {
            if (id == null || !Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,99}$")) throw new InvalidDataException("전용 캠페인 ID가 올바르지 않습니다.");
        }
        public static bool ReleaseUrl(string value)
        {
            if (!DistributionPolicy.Https(value) || !value.StartsWith(ReleasePrefix, StringComparison.Ordinal)) return false;
            var uri = new Uri(value);
            return String.IsNullOrEmpty(uri.Query) && String.IsNullOrEmpty(uri.Fragment) &&
                !value.Contains("%") && !value.Substring(ReleasePrefix.Length).Split('/').Any(p => p == "." || p == "..") &&
                Regex.IsMatch(value.Substring(ReleasePrefix.Length), "^[a-zA-Z0-9._-]+/[a-zA-Z0-9._-]+\\z");
        }
        static string Relative(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || path.Length > 220 || path.Contains("\\") || path.Contains(":") ||
                path.Split('/').Any(p => p == "" || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) ||
                path.StartsWith("Maps/Campaign/", StringComparison.OrdinalIgnoreCase) ||
                !(path.StartsWith("Maps/", StringComparison.Ordinal) || path.StartsWith("Mods/", StringComparison.Ordinal)) ||
                !(path.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase) ||
                    DirectoryModMember(path)))
                throw new InvalidDataException("전용 캠페인의 설치 경로가 올바르지 않습니다: " + path);
            return path;
        }
        static bool DirectoryModMember(string path)
        {
            if (!Regex.IsMatch(path, "^Mods/[^/]+[.]SC2Mod/.+", RegexOptions.IgnoreCase)) return false;
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return new[] { ".dds", ".txt", ".xml", ".galaxy", ".sc2style", ".version", ".sc2layout", ".sc2components" }.Contains(extension) ||
                (extension == "" && new[] { "Changelog", "DocumentHeader", "DocumentInfo", "Triggers" }.Contains(Path.GetFileName(path)));
        }
        public static bool BundleUrl(string value)
        {
            if (ReleaseUrl(value)) return true;
            if (!DistributionPolicy.Https(value)) return false;
            var uri = new Uri(value);
            if (!String.IsNullOrEmpty(uri.Query) || !String.IsNullOrEmpty(uri.Fragment) || !String.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return false;
            const string mass = "https://github.com/sksh1260/asdfghjjk/releases/download/SCMR_SC_Evo/";
            if (value == mass + "SCMR_SCEvo.zip" || value == mass + "SCMR_SCEvo.KR.VO.Assets.Local.zip" ||
                value == mass + "SCMR_SCEvo.EN.VO.Assets.Local.zip") return true;
            return Regex.IsMatch(value, "^https://mediafilez[.]forgecdn[.]net/files/[0-9]+/[0-9]+/[A-Za-z0-9._% -]+[.]zip\\z") &&
                !Uri.UnescapeDataString(uri.AbsolutePath).Contains("..") &&
                !Regex.IsMatch(value.Replace("%20", ""), "%");
        }
        static string Target(string game, string path) { return Data.Under(game, Relative(path)); }
        static string CurrentHash(string path) { return File.Exists(path) ? Data.Hash(path) : null; }
        public DedicatedState Current()
        {
            return File.Exists(statePath) ? Data.Read<DedicatedState>(statePath) : new DedicatedState();
        }
        public DedicatedInstalled Installed(string id) { return Current().installed.SingleOrDefault(c => c.id == id); }
        public static bool Ready(Campaign item)
        {
            return item != null && item.kind == "dedicated" && ReleaseUrl(item.dedicatedManifestUrl) &&
                HashText(item.dedicatedManifestSha256) && item.dedicatedInstallerVersion <= InstallerVersion;
        }
        public bool IsInstalled(Campaign item) { return Installed(item.id) != null; }
        public static bool HasVoiceOptions(Campaign item)
        {
            return item != null && item.id == "sc-evo" && ReleaseUrl(item.dedicatedEnglishManifestUrl) &&
                HashText(item.dedicatedEnglishManifestSha256);
        }
        public static Campaign VoiceVariant(Campaign item, bool english)
        {
            if (!english) return item;
            if (!HasVoiceOptions(item)) throw new InvalidOperationException("검증된 영어 음성 설치 정보가 없습니다.");
            var selected = Data.Json.Deserialize<Campaign>(Data.Json.Serialize(item));
            selected.dedicatedManifestUrl = item.dedicatedEnglishManifestUrl;
            selected.dedicatedManifestSha256 = item.dedicatedEnglishManifestSha256;
            return selected;
        }
        public string Voice(Campaign item)
        {
            var saved = Installed(item.id);
            return saved != null && saved.release.voice == "en" ? "en" : "ko";
        }
        public string VoiceLabel(Campaign item) { return Voice(item) == "en" ? "영어" : "한국어"; }
        Campaign InstalledVariant(Campaign item) { return VoiceVariant(item, HasVoiceOptions(item) && Voice(item) == "en"); }
        public bool NeedsUpdate(Campaign item)
        {
            var saved = Installed(item.id);
            var selected = InstalledVariant(item);
            if (saved == null || !Ready(selected) || saved.manifestSha256 == selected.dedicatedManifestSha256 || NeedsRepair(item)) return false;
            DedicatedRelease latest;
            lock (checkedReleases)
                if (!checkedReleases.TryGetValue(selected.dedicatedManifestSha256, out latest)) return false;
            var files = latest.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            return saved.release.files.Count != files.Count || saved.release.files.Any(f => {
                DedicatedFile other;
                return !files.TryGetValue(f.path, out other) || f.bytes != other.bytes ||
                    !String.Equals(f.sha256, other.sha256, StringComparison.OrdinalIgnoreCase);
            }) || !saved.release.launches.Select(l => l.path).SequenceEqual(latest.launches.Select(l => l.path), StringComparer.OrdinalIgnoreCase);
        }
        public bool NeedsRepair(Campaign item)
        {
            var state = Current();
            var saved = state.installed.SingleOrDefault(c => c.id == item.id);
            return saved != null && saved.release.files.Any(f => !File.Exists(Target(state.gameRoot, f.path)));
        }
        internal void RememberRelease(Campaign item, DedicatedRelease release)
        {
            ValidateRelease(release);
            if (release.id != item.id) throw new InvalidDataException("다른 캠페인의 설치 목록입니다.");
            lock (checkedReleases) checkedReleases[item.dedicatedManifestSha256] =
                Data.Json.Deserialize<DedicatedRelease>(Data.Json.Serialize(release));
        }
        public void RefreshUpdateInfo(IEnumerable<Campaign> catalogue, CancellationToken cancellation)
        {
            foreach (var item in catalogue.Where(c => c.kind == "dedicated"))
            {
                var saved = Installed(item.id);
                if (saved == null) continue;
                var selected = InstalledVariant(item);
                if (!Ready(selected) || saved.manifestSha256 == selected.dedicatedManifestSha256) continue;
                lock (checkedReleases)
                    if (checkedReleases.ContainsKey(selected.dedicatedManifestSha256)) continue;
                cancellation.ThrowIfCancellationRequested();
                string work = Path.Combine(root, "check-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try { FetchRelease(selected, work, cancellation); }
                finally { Directory.Delete(work, true); }
            }
        }
        public string Version(Campaign item)
        {
            var saved = Installed(item.id); return saved == null ? item.version : saved.version;
        }
        public static void ValidateRelease(DedicatedRelease release)
        {
            if (release == null) throw new InvalidDataException("설치 목록이 없습니다.");
            if (release.voice != null && (release.id != "sc-evo" || release.voice != "en"))
                throw new InvalidDataException("지원하지 않는 음성 설치 정보입니다.");
            Identity(release.id);
            if (release.schema != 1 || String.IsNullOrWhiteSpace(release.version) || release.version.Length > 160 ||
                release.files == null || release.files.Count == 0 || release.files.Count > 1000 ||
                release.packages == null || release.packages.Count == 0 || release.packages.Count > 32 ||
                release.launches == null || release.launches.Count == 0 || release.launches.Count > 3)
                throw new InvalidDataException("전용 캠페인 설치 목록 형식이 올바르지 않습니다.");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in release.files)
            {
                if (file == null || !paths.Add(Relative(file.path)) || !HashText(file.sha256) || file.bytes < 4 || file.bytes > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("설치 파일이 중복되거나 검증 정보가 올바르지 않습니다.");
            }
            if (release.files.Sum(f => f.bytes) > 32L * 1024 * 1024 * 1024) throw new InvalidDataException("전용 캠페인 설치 용량 한도를 초과했습니다.");
            var mapped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in release.packages)
            {
                if (bundle == null || !BundleUrl(bundle.url) || !bundle.url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    !HashText(bundle.sha256) || bundle.bytes <= 0 || bundle.bytes > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("검증된 ZIP 배포 정보가 필요합니다.");
                if (bundle.entries == null) continue;
                if (bundle.entries.Count == 0 || bundle.entries.Count > release.files.Count) throw new InvalidDataException("ZIP 설치 경로 목록이 올바르지 않습니다.");
                foreach (var entry in bundle.entries)
                {
                    ArchiveEntry(entry.Key);
                    if (!paths.Contains(Relative(entry.Value)) || !mapped.Add(entry.Value))
                        throw new InvalidDataException("ZIP 설치 대상이 누락되거나 중복됐습니다.");
                }
            }
            foreach (var launch in release.launches)
                if (launch == null || String.IsNullOrWhiteSpace(launch.label) || launch.label.Length > 80 ||
                    !paths.Contains(launch.path) || !launch.path.StartsWith("Maps/", StringComparison.Ordinal) ||
                    !launch.path.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("실행 맵이 설치 파일 목록에 없습니다.");
        }
        static void Download(string url, string output, long bytes, string sha256, Action<WorkProgress> progress, CancellationToken cancellation)
        {
            if (!BundleUrl(url)) throw new InvalidDataException("허용된 배포 주소가 아닙니다.");
            string address = url;
            for (int redirect = 0; redirect < 6; redirect++)
            {
                var request = (HttpWebRequest)WebRequest.Create(address);
                request.AllowAutoRedirect = false; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
                request.ServicePoint.ConnectionLimit = Math.Max(request.ServicePoint.ConnectionLimit, 3);
                request.UserAgent = "CampaignHangar-Dedicated/1.0";
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                    {
                        var next = new Uri(new Uri(address), response.Headers["Location"]);
                        if (next.Scheme != "https" || !next.IsDefaultPort || !String.IsNullOrEmpty(next.UserInfo) ||
                            !(next.Host == "github.com" || next.Host == "release-assets.githubusercontent.com" || next.Host == "objects.githubusercontent.com" ||
                                next.Host == "mediafilez.forgecdn.net" || next.Host == "edge.forgecdn.net" || next.Host == "media.forgecdn.net"))
                            throw new InvalidDataException("허용된 배포 서버 밖으로 이동하는 다운로드입니다.");
                        address = next.AbsoluteUri; continue;
                    }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > bytes)
                        throw new InvalidDataException("다운로드 응답 크기가 설치 정보와 다릅니다.");
                    using (var source = response.GetResponseStream())
                    using (var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
                    using (var digest = SHA256.Create())
                    {
                        var buffer = new byte[1024 * 1024]; int read; long completed = 0;
                        while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            completed += read;
                            if (completed > bytes) throw new InvalidDataException("다운로드 크기 한도를 초과했습니다.");
                            target.Write(buffer, 0, read);
                            digest.TransformBlock(buffer, 0, read, buffer, 0);
                            if (progress != null) progress(new WorkProgress("캠페인 파일 다운로드", completed, bytes, true));
                        }
                        if (completed != bytes) throw new InvalidDataException("다운로드가 끝나기 전에 연결이 종료됐습니다.");
                        digest.TransformFinalBlock(new byte[0], 0, 0);
                        if (BitConverter.ToString(digest.Hash).Replace("-", "").ToLowerInvariant() != sha256)
                            throw new InvalidDataException("다운로드 파일의 SHA256이 다릅니다.");
                    }
                    return;
                }
            }
            throw new InvalidDataException("다운로드 이동 횟수가 너무 많습니다.");
        }
        public DedicatedRelease FetchRelease(Campaign item, string directory, CancellationToken cancellation)
        {
            if (!Ready(item)) throw new InvalidOperationException("이 캠페인의 검증된 설치본이 아직 등록되지 않았습니다.");
            Identity(item.id);
            // Manifests are small; a strict ceiling also bounds the JSON parser.
            string file = Path.Combine(directory, "release.json");
            var request = (HttpWebRequest)WebRequest.Create(item.dedicatedManifestUrl);
            request.AllowAutoRedirect = true; request.Timeout = 30000; request.ReadWriteTimeout = 30000;
            using (var response = request.GetResponse())
            using (var source = response.GetResponseStream())
            using (var target = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            {
                var uri = response.ResponseUri;
                if (uri.Scheme != "https" || !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo) ||
                    !(uri.Host == "github.com" || uri.Host == "release-assets.githubusercontent.com" || uri.Host == "objects.githubusercontent.com"))
                    throw new InvalidDataException("설치 목록이 GitHub 배포 서버 밖으로 이동했습니다.");
                var buffer = new byte[8192]; int count; long size = 0;
                while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellation.ThrowIfCancellationRequested(); size += count;
                    if (size > 1024 * 1024) throw new InvalidDataException("설치 목록이 1MiB를 초과했습니다.");
                    target.Write(buffer, 0, count);
                }
            }
            if (Data.Hash(file, cancellation) != item.dedicatedManifestSha256) throw new InvalidDataException("설치 목록의 SHA256이 사이트 등록값과 다릅니다.");
            var release = Data.Read<DedicatedRelease>(file); ValidateRelease(release);
            if (release.id != item.id) throw new InvalidDataException("다른 캠페인의 설치 목록입니다.");
            RememberRelease(item, release);
            return release;
        }
        public void Install(Campaign item, string gameRoot, Action<WorkProgress> progress, CancellationToken cancellation,
            Func<DedicatedConflict, bool> confirmConflict = null,
            Func<IReadOnlyList<DedicatedConflict>, bool> confirmRemoval = null)
        {
            InstallRestored(item, gameRoot, progress, cancellation, confirmConflict, null, confirmRemoval);
        }
        void InstallRestored(Campaign item, string gameRoot, Action<WorkProgress> progress, CancellationToken cancellation,
            Func<DedicatedConflict, bool> confirmConflict, Dictionary<string, string> restoredFiles,
            Func<IReadOnlyList<DedicatedConflict>, bool> confirmRemoval = null)
        {
            RequireGame(gameRoot);
            Directory.CreateDirectory(root);
            string work = Path.Combine(root, "download-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var release = FetchRelease(item, work, cancellation);
            InstallSnapshot(release, item.dedicatedManifestSha256, work, gameRoot, progress, cancellation,
                Download, confirmConflict, restoredFiles, confirmRemoval);
        }
        internal void InstallSnapshot(DedicatedRelease release, string manifestHash, string work, string gameRoot,
            Action<WorkProgress> progress, CancellationToken cancellation,
            Action<string, string, long, string, Action<WorkProgress>, CancellationToken> download,
            Func<DedicatedConflict, bool> confirmConflict = null, Dictionary<string, string> restoredFiles = null,
            Func<IReadOnlyList<DedicatedConflict>, bool> confirmRemoval = null)
        {
            RequireGame(gameRoot); ValidateRelease(release);
            var initialState = Current();
            var conflicts = InstallConflicts(release, initialState);
            string approvedState = null;
            if (conflicts.Count > 0)
            {
                if (confirmRemoval == null)
                    throw new InvalidOperationException(InstallConflictMessage(conflicts));
                string beforeConsent = Data.Json.Serialize(initialState);
                cancellation.ThrowIfCancellationRequested();
                if (!confirmRemoval(conflicts)) throw new OperationCanceledException("기존 전용 캠페인 설치 제거를 취소했습니다.");
                if (Data.Json.Serialize(Current()) != beforeConsent)
                    throw new IOException("선택 도중 전용 캠페인 설치 상태가 변경됐습니다. 설치를 다시 확인해 주세요.");
                approvedState = beforeConsent;
            }
            // Removing an overlapping owner later can remove even an identical
            // reused file. Stage those affected files from the verified ZIP.
            var atRisk = initialState.installed
                .Where(c => conflicts.Any(x => x.campaignId == c.id))
                .SelectMany(c => c.release.files).Select(f => f.path).ToArray();
            var reusable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var ccm = new Library(Path.GetDirectoryName(root)).Current();
            bool activeHere = ccm.active != null && String.Equals(ccm.gameRoot, Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase);
            long scanned = 0, total = release.files.Sum(f => f.bytes);
            foreach (var file in release.files)
            {
                cancellation.ThrowIfCancellationRequested();
                string path = Target(gameRoot, file.path);
                bool conflict = activeHere && Overlap(new[] { file.path }, ccm.files.Select(f => f.path)) != null;
                if (!conflict && !atRisk.Any(p => Overlap(new[] { file.path }, new[] { p }) != null) &&
                    File.Exists(path) && new FileInfo(path).Length == file.bytes &&
                    Data.Hash(path, cancellation, progress == null ? null : new Action<long, long>((done, size) =>
                        progress(new WorkProgress("기존 파일 확인", scanned + done, total, true)))) == file.sha256)
                    reusable.Add(file.path, file.sha256);
                scanned += file.bytes;
                if (progress != null) progress(new WorkProgress("기존 파일 확인", scanned, total, true));
            }
            bool allReused = reusable.Count == release.files.Count;
            var needed = Enumerable.Range(0, release.packages.Count).Where(i => !allReused &&
                (release.packages[i].entries == null || release.packages[i].entries.Values.Any(path => !reusable.ContainsKey(path)))).ToArray();
            if (progress != null) progress(new WorkProgress(allReused ? "기존 파일 일치 · 다운로드 생략" :
                "기존 파일 " + reusable.Count + "/" + release.files.Count + "개 재사용", 0, needed.Sum(i => release.packages[i].bytes), true));
            var paths = new string[release.packages.Count];
            var completed = new long[release.packages.Count];
            long downloadBytes = needed.Sum(i => release.packages[i].bytes);
            object gate = new object();
            try
            {
                Parallel.ForEach(needed, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellation }, i => {
                    var bundle = release.packages[i];
                    string path = Path.Combine(work, i + ".zip");
                    download(bundle.url, path, bundle.bytes, bundle.sha256, progress == null ? null : new Action<WorkProgress>(value => {
                        lock (gate)
                        {
                            completed[i] = value.completed;
                            progress(new WorkProgress("필요한 파일 다운로드", completed.Sum(), downloadBytes, true));
                        }
                    }), cancellation);
                    paths[i] = path;
                });
            }
            catch (AggregateException error)
            {
                var failures = error.Flatten().InnerExceptions;
                if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                throw;
            }
            cancellation.ThrowIfCancellationRequested();
            InstallPrepared(release, manifestHash, paths, gameRoot, progress, cancellation, null,
                confirmConflict, restoredFiles, reusable, confirmRemoval, approvedState);
        }
        void RequireGame(string game)
        {
            if (!Path.IsPathRooted(game) || !File.Exists(Path.Combine(game, "StarCraft II.exe")))
                throw new InvalidOperationException("스타2 게임 경로를 먼저 선택해 주세요.");
            ExternalCampaigns.RequireGameClosed(game);
            if (Directory.Exists(transaction)) throw new InvalidOperationException("전용 캠페인의 미완료 설치를 먼저 복구해 주세요.");
            var state = Current();
            if (state.installed.Count > 0 && !String.Equals(state.gameRoot, Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("전용 캠페인이 다른 게임 경로에 설치돼 있습니다.");
        }
        public void InstallArchives(DedicatedRelease release, string manifestHash, IList<string> archives, string game,
            Action<WorkProgress> progress, CancellationToken cancellation, Action<int> fault = null,
            Func<DedicatedConflict, bool> confirmConflict = null, Dictionary<string, string> restoredFiles = null,
            Func<IReadOnlyList<DedicatedConflict>, bool> confirmRemoval = null)
        {
            InstallPrepared(release, manifestHash, archives, game, progress, cancellation, fault,
                confirmConflict, restoredFiles, null, confirmRemoval, null);
        }
        void InstallPrepared(DedicatedRelease release, string manifestHash, IList<string> archives, string game,
            Action<WorkProgress> progress, CancellationToken cancellation, Action<int> fault,
            Func<DedicatedConflict, bool> confirmConflict, Dictionary<string, string> restoredFiles,
            Dictionary<string, string> reusable, Func<IReadOnlyList<DedicatedConflict>, bool> confirmRemoval,
            string approvedState)
        {
            RequireGame(game); ValidateRelease(release);
            if (!HashText(manifestHash) || archives.Count != release.packages.Count) throw new InvalidDataException("설치 자료가 누락됐습니다.");
            string work = Path.Combine(root, "stage-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
            var declared = release.files.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            var extracted = new HashSet<string>(reusable == null ? Enumerable.Empty<string>() : reusable.Keys, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < archives.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var bundle = release.packages[i];
                if (archives[i] == null)
                {
                    if (reusable == null || (bundle.entries == null ? reusable.Count != release.files.Count :
                        bundle.entries.Values.Any(path => !reusable.ContainsKey(path))))
                        throw new InvalidDataException("필요한 설치 ZIP이 누락됐습니다.");
                    continue;
                }
                if (new FileInfo(archives[i]).Length != bundle.bytes || Data.Hash(archives[i], cancellation) != bundle.sha256)
                    throw new InvalidDataException("설치 ZIP의 크기 또는 SHA256이 다릅니다.");
                using (var zip = ZipFile.OpenRead(archives[i]))
                {
                    foreach (var entry in zip.Entries)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        if (bundle.entries != null)
                        {
                            ArchiveEntry(entry.FullName.TrimEnd('/'));
                            if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                            if (!bundle.entries.ContainsKey(entry.FullName)) continue;
                        }
                        if (entry.FullName == "LICENSE-RPS.txt" || entry.FullName == "SOURCE.txt") continue;
                        DedicatedFile file;
                        string relative = Relative(bundle.entries == null ? entry.FullName : bundle.entries[entry.FullName]);
                        if (!declared.TryGetValue(relative, out file) || !seen.Add(relative) || entry.Length != file.bytes)
                            throw new InvalidDataException("ZIP 내용이 검증된 설치 파일 목록과 다릅니다: " + relative);
                        if (reusable != null && reusable.ContainsKey(relative)) continue;
                        extracted.Add(relative);
                        string target = Data.Under(work, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (var input = entry.Open())
                        using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write))
                        using (var digest = SHA256.Create())
                        {
                            var buffer = new byte[1024 * 1024]; int count; long total = 0;
                            while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellation.ThrowIfCancellationRequested(); total += count;
                                if (total > file.bytes) throw new InvalidDataException("압축 파일이 선언된 크기를 초과했습니다.");
                                output.Write(buffer, 0, count);
                                digest.TransformBlock(buffer, 0, count, buffer, 0);
                            }
                            if (total != file.bytes) throw new InvalidDataException("압축 파일이 잘렸습니다.");
                            digest.TransformFinalBlock(new byte[0], 0, 0);
                            if (BitConverter.ToString(digest.Hash).Replace("-", "").ToLowerInvariant() != file.sha256)
                                throw new InvalidDataException("설치 파일의 SHA256이 다릅니다: " + relative);
                        }
                        if (!DirectoryModMember(relative))
                            using (var stream = File.OpenRead(target))
                                if (stream.ReadByte() != 77 || stream.ReadByte() != 80 || stream.ReadByte() != 81 || stream.ReadByte() != 26)
                                    throw new InvalidDataException("SC2 MPQ 형식이 아닙니다: " + relative);
                        if (progress != null) progress(new WorkProgress("캠페인 파일 검증", extracted.Count, release.files.Count));
                    }
                }
            }
            if (extracted.Count != release.files.Count) throw new InvalidDataException("ZIP에 필요한 파일이 누락됐습니다.");
            // All ZIPs are now verified. Only now may a user-approved removal
            // change the installed state. A canceled or failed download never
            // removes an existing campaign.
            var prior = Current();
            var conflicts = InstallConflicts(release, prior);
            if (conflicts.Count > 0)
            {
                string beforeConsent = Data.Json.Serialize(prior);
                if (approvedState == null)
                {
                    if (confirmRemoval == null)
                        throw new InvalidOperationException(InstallConflictMessage(conflicts));
                    cancellation.ThrowIfCancellationRequested();
                    if (!confirmRemoval(conflicts)) throw new OperationCanceledException("기존 전용 캠페인 설치 제거를 취소했습니다.");
                    approvedState = beforeConsent;
                }
                if (approvedState != beforeConsent || Data.Json.Serialize(Current()) != approvedState)
                    throw new IOException("확인한 전용 캠페인 설치 상태가 바뀌었습니다. 자동 제거하지 않았습니다.");
                // Check all selected installed owners before touching the first
                // one: one edited/blocked owner must not strand a previous
                // owner in the uninstalled state.
                var ownedBefore = Owned(prior);
                var affectedOwners = prior.installed
                    .Where(c => conflicts.Any(x => x.campaignId == c.id))
                    .SelectMany(c => c.release.files).Select(f => f.path)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                foreach (var path in affectedOwners)
                {
                    string expected;
                    if (!ownedBefore.TryGetValue(path, out expected) ||
                        CurrentHash(Target(game, path)) != expected)
                        throw new IOException("기존 전용 캠페인 파일이 외부에서 변경되어 설치 제거를 진행하지 않았습니다: " + path);
                }
                foreach (var conflict in conflicts)
                {
                    cancellation.ThrowIfCancellationRequested();
                    Uninstall(conflict.campaignId, game);
                }
                if (InstallConflicts(release, Current()).Count != 0)
                    throw new IOException("다른 전용 캠페인과 파일 충돌이 남아 새 설치를 중단했습니다.");
            }
            else if (approvedState != null && Data.Json.Serialize(Current()) != approvedState)
                throw new IOException("설치 상태가 변경되어 승인된 설치를 계속하지 않았습니다.");
            var old = Current();
            var next = Data.Json.Deserialize<DedicatedState>(Data.Json.Serialize(old));
            next.gameRoot = Path.GetFullPath(game);
            next.installed.RemoveAll(c => c.id == release.id);
            next.installed.Add(new DedicatedInstalled { id = release.id, version = release.version, manifestSha256 = manifestHash, release = release });
            if (release.files.Any(f => VariantPath(f.path))) next.activeVariantId = release.id;
            var affected = release.files.Select(f => f.path).Concat(old.installed.Where(c => c.id == release.id)
                .SelectMany(c => c.release.files).Select(f => f.path));
            Change(old, next, game, work, cancellation, fault, affected, confirmConflict, progress, restoredFiles, reusable);
        }
        internal static List<DedicatedConflict> InstallConflicts(DedicatedRelease release, DedicatedState state)
        {
            ValidateRelease(release);
            var found = new List<DedicatedConflict>();
            foreach (var installed in state.installed.Where(c => c.id != release.id))
            {
                var conflict = (from existing in installed.release.files
                                from incoming in release.files
                                where Overlap(new[] { incoming.path }, new[] { existing.path }) != null &&
                                   (!String.Equals(existing.path, incoming.path, StringComparison.OrdinalIgnoreCase) ||
                                    !String.Equals(existing.sha256, incoming.sha256, StringComparison.OrdinalIgnoreCase))
                                select incoming.path).FirstOrDefault();
                if (conflict != null)
                    found.Add(new DedicatedConflict(installed.id,
                        String.IsNullOrWhiteSpace(installed.release.name) ? installed.id : installed.release.name, conflict));
            }
            return found;
        }
        static string InstallConflictMessage(IReadOnlyList<DedicatedConflict> conflicts)
        {
            return "설치된 전용 캠페인과 파일 경로·버전이 겹칩니다: " +
                String.Join("; ", conflicts.Select(c => c.campaignName + " (" + c.path + ")")) +
                ". 기존 전용 캠페인을 자동으로 제거하지 않았습니다.";
        }
        public void Uninstall(string id, string game)
        {
            RequireGame(game); Identity(id);
            var old = Current();
            if (!old.installed.Any(c => c.id == id)) return;
            var next = Data.Json.Deserialize<DedicatedState>(Data.Json.Serialize(old));
            next.installed.RemoveAll(c => c.id == id);
            if (next.activeVariantId == id)
            {
                var remainingVariant = next.installed.LastOrDefault(c => c.release.files.Any(f => VariantPath(f.path)));
                next.activeVariantId = remainingVariant == null ? null : remainingVariant.id;
            }
            Change(old, next, game, null, CancellationToken.None, null,
                old.installed.Single(c => c.id == id).release.files.Select(f => f.path));
        }
        static Dictionary<string, string> Owned(DedicatedState state)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!String.IsNullOrWhiteSpace(state.activeVariantId) &&
                !state.installed.Any(c => c.id == state.activeVariantId))
                throw new InvalidDataException("공용 모드 선택 기록이 설치 캠페인과 일치하지 않습니다.");
            foreach (var campaign in state.installed)
            {
                ValidateRelease(campaign.release);
                foreach (var file in campaign.release.files)
                {
                    string hash;
                    if (files.TryGetValue(file.path, out hash) && hash != file.sha256)
                    {
                        if (!VariantPath(file.path) || String.IsNullOrWhiteSpace(state.activeVariantId))
                            throw new InvalidOperationException("다른 전용 캠페인이 같은 공용 파일의 다른 버전을 사용합니다: " + file.path);
                        if (campaign.id == state.activeVariantId) files[file.path] = file.sha256;
                        continue;
                    }
                    files[file.path] = file.sha256;
                }
            }
            return files;
        }
        void Change(DedicatedState old, DedicatedState next, string game, string stage, CancellationToken cancellation, Action<int> fault,
            IEnumerable<string> affectedPaths,
            Func<DedicatedConflict, bool> confirmConflict = null, Action<WorkProgress> progress = null,
            Dictionary<string, string> restoredFiles = null, Dictionary<string, string> reusable = null)
        {
            var before = Owned(old); var after = Owned(next);
            var affected = new HashSet<string>(affectedPaths, StringComparer.OrdinalIgnoreCase);
            var restored = ProtectDedicated(affected, game, confirmConflict, progress, cancellation) ?? restoredFiles;
            var currentHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in before.Where(file => affected.Contains(file.Key)))
            {
                cancellation.ThrowIfCancellationRequested();
                string expected;
                if (restored == null || !restored.TryGetValue(file.Key, out expected)) expected = file.Value;
                string hash = CurrentHash(Target(game, file.Key));
                currentHashes[file.Key] = hash;
                if (hash != expected)
                    throw new InvalidOperationException("전용 캠페인 파일이 외부에서 변경돼 설치·제거를 중단했습니다: " + file.Key);
            }
            if (reusable != null)
                foreach (var file in reusable)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string hash;
                    if (!currentHashes.TryGetValue(file.Key, out hash))
                        currentHashes[file.Key] = hash = CurrentHash(Target(game, file.Key));
                    if (hash != file.Value) throw new IOException("재사용할 파일이 확인 후 변경돼 설치를 중단했습니다: " + file.Key);
                }
            // A handful of Mass Recall editions share a fixed filename but
            // require incompatible cinematics bytes. Preserve both SHA-pinned
            // variants outside the SC2 install before any replacement. Every
            // other shared-file version conflict remains a hard error.
            foreach (string path in before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!VariantPath(path) || !affected.Contains(path)) continue;
                string beforeHash, afterHash;
                before.TryGetValue(path, out beforeHash);
                after.TryGetValue(path, out afterHash);
                if (String.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase)) continue;
                if (beforeHash != null)
                {
                    string live = Target(game, path);
                    if (CurrentHash(live) != beforeHash)
                        throw new IOException("공용 모드가 외부에서 수정되어 버전 전환을 중단했습니다: " + path);
                    RememberVariant(live, beforeHash);
                }
                if (afterHash != null)
                {
                    string incoming = stage == null ? null : Data.Under(stage, path);
                    if (incoming != null && File.Exists(incoming)) RememberVariant(incoming, afterHash);
                    else if (!File.Exists(VariantBackup(afterHash)) ||
                        Data.Hash(VariantBackup(afterHash)) != afterHash)
                        throw new IOException("공용 모드의 검증된 보관본이 없어 버전 전환을 중단했습니다. 해당 캠페인을 다시 설치해 주세요: " + path);
                }
            }
            var changes = new List<DedicatedChange>();
            var originals = next.originals.ToDictionary(f => f.path, StringComparer.OrdinalIgnoreCase);
            string backupGroup = "originals/" + Guid.NewGuid().ToString("N");
            foreach (string path in before.Keys.Concat(after.Keys).Where(path => affected.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string destination = Target(game, path);
                if (Directory.Exists(destination)) throw new IOException("같은 위치에 폴더형 모드가 있어 자동으로 교체하지 않습니다: " + path);
                string hash;
                if (!currentHashes.TryGetValue(path, out hash)) hash = CurrentHash(destination);
                string wanted; bool remains = after.TryGetValue(path, out wanted);
                if (!originals.ContainsKey(path))
                {
                    var original = new DedicatedOriginal { path = path, sha256 = hash };
                    if (hash != null)
                    {
                        original.backup = backupGroup + "/" + path;
                        Data.Copy(destination, Data.Under(root, original.backup));
                        if (Data.Hash(Data.Under(root, original.backup)) != hash) throw new IOException("최초 파일 백업 검증에 실패했습니다.");
                    }
                    originals.Add(path, original);
                }
                if (!remains)
                {
                    var original = originals[path]; wanted = original.sha256;
                    if (wanted != null && Data.Hash(Data.Under(root, original.backup)) != wanted) throw new IOException("복원할 원본 백업이 변경됐습니다.");
                }
                if (hash != wanted) changes.Add(new DedicatedChange { path = path, beforeHash = hash, afterHash = wanted });
            }
            cancellation.ThrowIfCancellationRequested();
            string preparation = Path.Combine(root, "prepare-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(preparation);
            foreach (var change in changes)
            {
                if (change.beforeHash != null) Data.Copy(Target(game, change.path), Data.Under(preparation, "before/" + change.path));
                if (change.afterHash != null)
                {
                    string source;
                    if (!after.ContainsKey(change.path)) source = Data.Under(root, originals[change.path].backup);
                    else if (stage != null && File.Exists(Data.Under(stage, change.path))) source = Data.Under(stage, change.path);
                    else if (VariantPath(change.path)) source = VariantBackup(change.afterHash);
                    else throw new IOException("전용 캠페인 교체 소스 파일이 없습니다: " + change.path);
                    Data.Copy(source, Data.Under(preparation, "after/" + change.path));
                    if (Data.Hash(Data.Under(preparation, "after/" + change.path)) != change.afterHash) throw new IOException("설치 준비 파일의 검증에 실패했습니다.");
                }
            }
            next.originals = originals.Values.Where(f => after.ContainsKey(f.path)).ToList();
            var journal = new DedicatedJournal { gameRoot = Path.GetFullPath(game), before = old, changes = changes };
            Data.Write(Path.Combine(preparation, "journal.json"), journal);
            Data.MoveDirectoryWithRetry(preparation, transaction);
            try
            {
                int count = 0;
                foreach (var change in changes)
                {
                    cancellation.ThrowIfCancellationRequested();
                    string destination = Target(game, change.path);
                    if (CurrentHash(destination) != change.beforeHash) throw new IOException("설치 도중 게임 파일이 변경됐습니다: " + change.path);
                    if (change.afterHash == null) File.Delete(destination);
                    else ReplaceVerified(Data.Under(transaction, "after/" + change.path), destination, change.afterHash);
                    if (CurrentHash(destination) != change.afterHash) throw new IOException("설치 결과가 검증값과 다릅니다.");
                    if (fault != null) fault(++count);
                }
                Data.Write(statePath, next);
                journal.committed = true; Data.Write(Path.Combine(transaction, "journal.json"), journal);
                Data.MoveDirectoryWithRetry(transaction, Path.Combine(root, "completed-" + Guid.NewGuid().ToString("N")));
            }
            catch { Recover(); throw; }
        }
        public void Recover()
        {
            if (!Directory.Exists(transaction)) return;
            var journal = Data.Read<DedicatedJournal>(Path.Combine(transaction, "journal.json"));
            ExternalCampaigns.RequireGameClosed(journal.gameRoot);
            if (!journal.committed)
            {
                // Check everything before restoring anything; unknown edits remain protected.
                foreach (var change in journal.changes)
                {
                    string current = CurrentHash(Target(journal.gameRoot, change.path));
                    if (current != change.beforeHash && current != change.afterHash)
                        throw new IOException("복구 대상이 외부에서 변경됐습니다. 원본을 보존한 뒤 확인해 주세요: " + change.path);
                    if (change.beforeHash != null && Data.Hash(Data.Under(transaction, "before/" + change.path)) != change.beforeHash)
                        throw new IOException("전용 캠페인 복구 백업의 검증값이 다릅니다.");
                }
                foreach (var change in journal.changes)
                {
                    string target = Target(journal.gameRoot, change.path);
                    if (change.beforeHash == null) { if (File.Exists(target)) File.Delete(target); }
                    else ReplaceVerified(Data.Under(transaction, "before/" + change.path), target, change.beforeHash);
                }
                Data.Write(statePath, journal.before);
            }
            Data.MoveDirectoryWithRetry(transaction, Path.Combine(root, "recovered-" + Guid.NewGuid().ToString("N")));
        }
        static void ReplaceVerified(string source, string destination, string expected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            Data.Copy(source, temporary);
            if (Data.Hash(temporary) != expected) throw new IOException("교체 준비 파일의 검증값이 다릅니다.");
            if (File.Exists(destination)) File.Replace(temporary, destination, null);
            else File.Move(temporary, destination);
        }
        public ProcessStartInfo LaunchInfo(Campaign item, string game, int episode = 0,
            Func<DedicatedConflict, bool> confirmConflict = null, Action<WorkProgress> progress = null,
            Action<Dictionary<string, string>> reapply = null)
        {
            item = InstalledVariant(item);
            var saved = Installed(item.id);
            if (saved == null) throw new InvalidOperationException("전용 캠페인을 먼저 설치해 주세요.");
            var state = Current();
            if (!String.Equals(state.gameRoot, Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("설치된 게임 경로와 다릅니다.");
            if (Directory.Exists(transaction)) throw new InvalidOperationException("미완료 전용 설치를 먼저 복구해 주세요.");
            Owned(state);
            var restored = ProtectDedicated(saved.release.files.Select(f => f.path), game, confirmConflict, progress, CancellationToken.None);
            if (restored != null)
            {
                if (reapply == null) InstallRestored(item, game, progress, CancellationToken.None, null, restored);
                else reapply(restored);
                saved = Installed(item.id);
                if (saved == null) throw new IOException("전용 캠페인을 다시 설치하지 못해 실행을 중단했습니다.");
            }
            state = Current();
            if (saved.release.files.Any(f => VariantPath(f.path)) &&
                !String.Equals(state.activeVariantId, item.id, StringComparison.Ordinal))
            {
                ExternalCampaigns.RequireGameClosed(game);
                var switched = Data.Json.Deserialize<DedicatedState>(Data.Json.Serialize(state));
                switched.activeVariantId = item.id;
                Change(state, switched, game, null, CancellationToken.None, null,
                    saved.release.files.Select(f => f.path), confirmConflict, progress);
            }
            foreach (var file in saved.release.files)
                if (CurrentHash(Target(game, file.path)) != file.sha256) throw new IOException("설치 파일이 없거나 변경됐습니다. 재설치 전 확인해 주세요: " + file.path);
            int selected = episode == 0 ? 0 : episode - 1;
            if (selected < 0 || selected >= saved.release.launches.Count) throw new InvalidOperationException("실행할 항목이 없습니다.");
            var connected = new Campaign { kind = "external", launchMode = "map", launcherMap = Target(game, saved.release.launches[selected].path) };
            return ExternalCampaigns.LaunchInfo(connected, game);
        }
        public static void ProtectCcm(string storage, Campaign item)
        {
            var owned = Owned(new DedicatedLibrary(storage).Current());
            string conflict = Overlap(item.files.Select(f => f.path), owned.Keys);
            if (conflict != null)
                throw new InvalidOperationException("설치된 전용 캠페인과 같은 파일을 사용하여 모드 적용을 중단했습니다: " + conflict);
        }
        Dictionary<string, string> ProtectDedicated(IEnumerable<string> paths, string game, Func<DedicatedConflict, bool> confirmConflict,
            Action<WorkProgress> progress, CancellationToken cancellation)
        {
            var library = new Library(Path.GetDirectoryName(root));
            var active = library.Current();
            if (active.active == null || !String.Equals(active.gameRoot, Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase)) return null;
            var targets = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            string conflict = Overlap(targets, active.files.Select(f => f.path));
            if (conflict == null) return null;
            var campaign = library.Installed(active.active);
            string name = campaign == null ? active.active : campaign.name;
            if (confirmConflict == null)
                throw new InvalidOperationException("현재 사용 중인 '" + name + "' 모드와 같은 파일을 사용하여 작업을 중단했습니다.\n겹치는 파일: " +
                    conflict + "\n현재 모드를 유지하려면 이 작업을 취소하세요. 기존 모드는 자동으로 해제하지 않습니다.");
            cancellation.ThrowIfCancellationRequested();
            if (!confirmConflict(new DedicatedConflict(active.active, name, conflict))) throw new OperationCanceledException();
            cancellation.ThrowIfCancellationRequested();
            var current = library.Current();
            if (current.active != active.active || current.versionKey != active.versionKey ||
                !String.Equals(current.gameRoot, active.gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("확인 중 현재 캠페인 모드가 바뀌었습니다. 아무 모드도 해제하지 않았습니다. 다시 확인해 주세요.");
            library.RestoreBase(progress);
            return targets.Where(path => Overlap(new[] { path }, active.files.Select(f => f.path)) != null)
                .ToDictionary(path => path, path => CurrentHash(Target(game, path)), StringComparer.OrdinalIgnoreCase);
        }
        static string Overlap(IEnumerable<string> paths, IEnumerable<string> otherPaths)
        {
            var other = otherPaths.Select(p => p.Replace('\\', '/')).ToArray();
            return paths.Select(p => p.Replace('\\', '/')).FirstOrDefault(path => other.Any(p =>
                String.Equals(p, path, StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(p + "/", StringComparison.OrdinalIgnoreCase)));
        }
        static void ArchiveEntry(string name)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 400 || name.Contains("\\") || name.Contains(":") ||
                name.Split('/').Any(p => p == "" || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ") ||
                    p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidDataException("ZIP 내부 경로가 올바르지 않습니다: " + name);
        }
    }
}
