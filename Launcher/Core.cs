using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace CampaignHangar
{
    public sealed class Campaign
    {
        public string id { get; set; }
        public string name { get; set; }
        public string version { get; set; }
        public string summary { get; set; }
        public string detail { get; set; }
        public string download { get; set; }
        public List<CampaignDownload> downloads { get; set; }
        public string source { get; set; }
        public string thumbnail { get; set; }
        public string[] categories { get; set; }
        public string[] images { get; set; }
        public string[] aliases { get; set; }
        public string kind { get; set; }
        public string updateNote { get; set; }
        public string sourceAuthor { get; set; }
        // Personal external connections created in the development channel
        // must not leak into the published client before a site release exists.
        public bool developmentOnly { get; set; }
        public string translator { get; set; }
        public string translatorUrl { get; set; }
        public string translatorLabel { get; set; }
        public string creditLabel { get; set; }
        public string evoDeveloper { get; set; }
        public string creatorUrl { get; set; }
        public string creatorLabel { get; set; }
        public string sourceFileName { get; set; }
        public string galleryGroup { get; set; }
        public string[] campaignGroups { get; set; }
        public string slot { get; set; }
        public string @base { get; set; }
        public string versionKey { get; set; }
        public string launcherMap { get; set; }
        public string installationMode { get; set; }
        public string launchMode { get; set; }
        public string launcherExecutable { get; set; }
        public string standaloneZipHash { get; set; }
        public string[] episodeMaps { get; set; }
        public string dedicatedManifestUrl { get; set; }
        public string dedicatedManifestSha256 { get; set; }
        public string dedicatedEnglishManifestUrl { get; set; }
        public string dedicatedEnglishManifestSha256 { get; set; }
        public int dedicatedInstallerVersion { get; set; }
        public int dedicatedEpisodeCount { get; set; }
        public List<OwnedFile> files { get; set; }
        public override string ToString() { return name; }
    }
    public sealed class OwnedFile
    {
        public string path { get; set; }
        public string hash { get; set; }
        public bool directory { get; set; }
        public string backup { get; set; }
    }
    public sealed class CampaignDownload
    {
        public string url { get; set; }
        public string label { get; set; }
    }
    public sealed class State
    {
        public string active { get; set; }
        public string previousActive { get; set; }
        public string versionKey { get; set; }
        public string gameRoot { get; set; }
        public List<OwnedFile> files { get; set; }
        public List<OwnedFile> originals { get; set; }
        public List<string> createdDirectories { get; set; }
        public List<OwnedFile> managed { get; set; }
        public bool movedDeployment { get; set; }
        public State()
        {
            files = new List<OwnedFile>();
            originals = new List<OwnedFile>();
            createdDirectories = new List<string>();
            managed = new List<OwnedFile>();
        }
    }
    public sealed class Journal
    {
        public string gameRoot { get; set; }
        public State before { get; set; }
        public List<string> paths { get; set; }
        public List<string> existed { get; set; }
        public List<OwnedFile> snapshots { get; set; }
        public List<DeploymentMove> moves { get; set; }
        public bool committed { get; set; }
    }
    public sealed class DeploymentMove
    {
        public string source { get; set; }
        public string destination { get; set; }
        public OwnedFile node { get; set; }
    }
    public static class Data
    {
        public const string Site = "https://sc2-campaign-archive.ddaheuk.workers.dev";
        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16000000 };
        public static string Hash(string file)
        { return Hash(file, System.Threading.CancellationToken.None); }
        public static string Hash(string file, System.Threading.CancellationToken cancellation, Action<long, long> progress = null)
        {
            cancellation.ThrowIfCancellationRequested();
            using (var stream = File.OpenRead(file))
            using (var sha = SHA256.Create())
            {
                byte[] digest;
                if (!cancellation.CanBeCanceled && progress == null) digest = sha.ComputeHash(stream);
                else
                {
                    var buffer = new byte[1024 * 1024];
                    long completed = 0, total = stream.Length;
                    if (progress != null) progress(completed, total);
                    while (true)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        int count = stream.Read(buffer, 0, buffer.Length);
                        if (count == 0) break;
                        sha.TransformBlock(buffer, 0, count, buffer, 0);
                        completed += count;
                        if (progress != null) progress(completed, total);
                    }
                    cancellation.ThrowIfCancellationRequested();
                    sha.TransformFinalBlock(new byte[0], 0, 0);
                    digest = sha.Hash;
                }
                return BitConverter.ToString(digest).Replace("-", "").ToLowerInvariant();
            }
        }
        public static T Read<T>(string file) { return Json.Deserialize<T>(File.ReadAllText(file, Encoding.UTF8)); }
        public static void Write(string file, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, Json.Serialize(value), new UTF8Encoding(false));
            if (File.Exists(file)) File.Replace(temp, file, null);
            else File.Move(temp, file);
        }
        public static string Under(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new InvalidDataException("절대 경로 또는 비어 있는 경로는 사용할 수 없습니다.");
            string prefix = Path.GetFullPath(root).TrimEnd('\\') + "\\";
            string full = Path.GetFullPath(Path.Combine(prefix, relative.Replace('/', '\\')));
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("대상 폴더 밖으로 나가는 경로입니다.");
            for (string p = full; p != null && p.Length >= prefix.Length - 1; p = Path.GetDirectoryName(p))
                if ((Directory.Exists(p) || File.Exists(p)) &&
                    (File.GetAttributes(p) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("연결된 폴더·파일은 안전하게 처리할 수 없습니다.");
            return full;
        }
        public static void Copy(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }
        public static List<Campaign> ParseCatalogue(string text)
        {
            const string prefix = "window.CAMPAIGNS=";
            text = text.Trim();
            if (!text.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidDataException("사이트 목록 형식이 다릅니다.");
            string json = text.Substring(prefix.Length).Trim();
            // The site's response also contains SITE_COPY and other assignments.
            // Extract exactly the leading JSON array; never execute the script.
            if (!json.StartsWith("[")) throw new InvalidDataException("사이트 캠페인 배열이 없습니다.");
            int depth = 0, end = -1;
            bool quoted = false, escaped = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (quoted)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                }
                else if (c == '"') quoted = true;
                else if (c == '[') depth++;
                else if (c == ']' && --depth == 0) { end = i + 1; break; }
            }
            if (end < 0) throw new InvalidDataException("사이트 캠페인 배열이 잘렸습니다.");
            json = json.Substring(0, end);
            var result = Json.Deserialize<List<Campaign>>(json);
            if (result == null || result.Any(c => c == null || String.IsNullOrWhiteSpace(c.id) || String.IsNullOrWhiteSpace(c.name)) ||
                result.Select(c => c.id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
                throw new InvalidDataException("캠페인 ID·이름 또는 중복 항목을 확인해 주세요.");
            return result;
        }
        public static string GetText(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Timeout = 20000;
            request.ReadWriteTimeout = 20000;
            request.UserAgent = "CampaignHangar-Preview/0.1";
            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var text = new StringBuilder();
                var buffer = new char[4096];
                int count;
                while ((count = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (text.Length + count > 16000000) throw new InvalidDataException("사이트 목록이 허용 크기를 초과합니다.");
                    text.Append(buffer, 0, count);
                }
                return text.ToString();
            }
        }
        public static bool WebLink(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https";
        }
        public static bool DirectZip(string url)
        {
            Uri uri;
            return Uri.TryCreate(url, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.AbsolutePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        }
        public static void DownloadZip(string url, string destination, Action<WorkProgress> progress = null,
            System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!DirectZip(url)) throw new InvalidDataException("직접 HTTPS ZIP 주소만 다운로드할 수 있습니다.");
            if (File.Exists(destination)) throw new IOException("같은 다운로드 파일이 이미 있습니다.");
            string current = url;
            // Every redirect must retain HTTPS; never execute a downloaded payload.
            for (int redirect = 0; redirect < 6; redirect++)
            {
                cancellation.ThrowIfCancellationRequested();
                var request = (HttpWebRequest)WebRequest.Create(current);
                request.AllowAutoRedirect = false;
                request.Timeout = 30000;
                request.ReadWriteTimeout = 30000;
                request.UserAgent = "CampaignHangar-Preview/0.1";
                // Interrupt even a blocking network read instead of waiting for
                // the HTTP timeout after the user presses Cancel.
                using (cancellation.Register(delegate { try { request.Abort(); } catch { } }))
                try
                {
                  using (var response = (HttpWebResponse)request.GetResponse())
                  {
                    int code = (int)response.StatusCode;
                    if (code >= 300 && code < 400)
                    {
                        Uri next = new Uri(new Uri(current), response.Headers["Location"]);
                        if (next.Scheme != "https") throw new InvalidDataException("HTTPS가 아닌 다운로드 전환은 중단합니다.");
                        current = next.AbsoluteUri;
                        continue;
                    }
                    const long limit = 4L * 1024 * 1024 * 1024;
                    if (response.ContentLength > limit) throw new InvalidDataException("다운로드가 4GiB 제한을 초과합니다.");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    try
                    {
                        using (var source = response.GetResponseStream())
                        using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            long total = 0;
                            byte[] buffer = new byte[65536];
                            int count;
                            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                total += count;
                                if (total > limit) throw new InvalidDataException("다운로드가 4GiB 제한을 초과합니다.");
                                output.Write(buffer, 0, count);
                                if (progress != null) progress(new WorkProgress("다운로드 중", total, response.ContentLength, true));
                            }
                            if (response.ContentLength >= 0 && total != response.ContentLength)
                                throw new IOException("다운로드 길이가 다릅니다.");
                        }
                        cancellation.ThrowIfCancellationRequested();
                        using (var archive = ZipFile.OpenRead(destination))
                            if (archive.Entries.Count == 0) throw new InvalidDataException("비어 있는 ZIP입니다.");
                        return;
                    }
                    catch
                    {
                        if (File.Exists(destination)) File.Delete(destination);
                        throw;
                    }
                }
                }
                catch (Exception)
                {
                    // Abort may surface as WebException/IOException. Preserve
                    // cancellation's meaning; the partial ZIP was cleaned by
                    // the download's existing catch/finally path.
                    cancellation.ThrowIfCancellationRequested();
                    throw;
                }
            }
            throw new IOException("다운로드 리디렉션이 너무 많습니다.");
        }
    }
    public enum RemovalKind { PreviousVersions, AllInformation, CampaignOnly }
    public sealed class RegisteredZip
    {
        public string campaignId { get; set; }
        public string path { get; set; }
        public string hash { get; set; }
    }
    public sealed class Library
    {
        public readonly string Root;
        private readonly string packages;
        private readonly string statePath;
        private readonly string transaction;
        public Library(string root)
        {
            Root = Path.GetFullPath(root);
            packages = Path.Combine(Root, "packages");
            statePath = Path.Combine(Root, "active.json");
            transaction = Path.Combine(Root, "transaction");
            Directory.CreateDirectory(packages);
        }
        private static string Id(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 100 ||
                id.Any(c => !(Char.IsLetterOrDigit(c) || c == '-' || c == '_')))
                throw new InvalidDataException("캠페인 ID는 글자·숫자·하이픈·밑줄만 사용할 수 있습니다.");
            return id;
        }
        private string ManifestPath(string id) { return Data.Under(packages, Id(id) + "\\manifest.json"); }
        public Campaign Installed(string id)
        {
            string path = ManifestPath(id);
            return File.Exists(path) ? Data.Read<Campaign>(path) : null;
        }
        public List<Campaign> Versions(string id)
        {
            string directory = Data.Under(packages, Id(id) + "\\versions");
            return Directory.Exists(directory)
                ? Directory.GetDirectories(directory).Select(p => Path.Combine(p, "manifest.json"))
                    .Where(File.Exists).Select(p => Data.Read<Campaign>(p)).OrderBy(c => c.version).ToList()
                : new List<Campaign>();
        }
        private string StoredPath(Campaign campaign, string relative)
        {
            return Data.Under(packages, Id(campaign.id) + "\\versions\\" + Id(campaign.versionKey) + "\\" + relative);
        }
        public string VersionFolder(Campaign campaign)
        {
            if (campaign == null || !Versions(campaign.id).Any(item => item.versionKey == campaign.versionKey))
                throw new InvalidDataException("보관된 캠페인 버전을 찾을 수 없습니다.");
            return Data.Under(packages, Id(campaign.id) + "\\versions\\" + Id(campaign.versionKey));
        }
        public List<Campaign> All()
        {
            return Directory.GetDirectories(packages).Select(p => Path.Combine(p, "manifest.json"))
                .Where(File.Exists).Select(p => Data.Read<Campaign>(p)).ToList();
        }
        public State Current() { return File.Exists(statePath) ? Data.Read<State>(statePath) : new State(); }
        List<RegisteredZip> SourceZips()
        {
            string path = Path.Combine(Root, "source-zips.json");
            return File.Exists(path) ? Data.Read<List<RegisteredZip>>(path) : new List<RegisteredZip>();
        }
        internal void RememberZip(Campaign item, string zip)
        {
            zip = Path.GetFullPath(zip);
            if (!String.Equals(Path.GetExtension(zip), ".zip", StringComparison.OrdinalIgnoreCase) || Data.Hash(zip) != item.versionKey)
                throw new InvalidDataException("등록 ZIP의 내용이 보관 버전과 다릅니다.");
            var sources = SourceZips();
            if (!sources.Any(s => s.campaignId == item.id && s.hash == item.versionKey &&
                String.Equals(s.path, zip, StringComparison.OrdinalIgnoreCase)))
            {
                sources.Add(new RegisteredZip { campaignId = item.id, hash = item.versionKey, path = zip });
                Data.Write(Path.Combine(Root, "source-zips.json"), sources);
            }
        }
        static void CheckZipPath(string path)
        {
            path = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ZIP 파일만 정리할 수 있습니다.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("연결된 ZIP 파일은 삭제하지 않습니다.");
            for (var parent = new FileInfo(path).Directory; parent != null; parent = parent.Parent)
                if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("연결 폴더 안의 ZIP은 삭제하지 않습니다.");
        }
        static IEnumerable<string> RemovalZipFiles(string root, System.Threading.CancellationToken cancellation, Action<WorkProgress> progress)
        {
            var pending = new Stack<string>();
            pending.Push(Path.GetFullPath(root));
            while (pending.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                string directory = pending.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("연결된 다운로드 폴더는 확인하지 않습니다.");
                if (progress != null) progress(new WorkProgress("ZIP 후보 찾는 중 · " + directory));
                foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    cancellation.ThrowIfCancellationRequested();
                    string safe = Data.Under(directory, Path.GetFileName(entry));
                    if (Directory.Exists(safe)) pending.Push(safe);
                    else if (String.Equals(Path.GetExtension(safe), ".zip", StringComparison.OrdinalIgnoreCase)) yield return safe;
                }
            }
        }
        public List<RegisteredZip> RemovalZips(string id, IEnumerable<string> downloadRoots = null,
            Action<WorkProgress> progress = null, System.Threading.CancellationToken cancellation = default(System.Threading.CancellationToken))
        {
            Id(id);
            cancellation.ThrowIfCancellationRequested();
            var versions = Versions(id);
            var sources = SourceZips();
            var hashes = new HashSet<string>(versions.Select(c => c.versionKey)
                .Concat(sources.Where(s => s.campaignId == id).Select(s => s.hash)));
            var shared = new HashSet<string>(All().Where(c => c.id != id).SelectMany(c => Versions(c.id)).Select(c => c.versionKey)
                .Concat(sources.Where(s => s.campaignId != id).Select(s => s.hash)));
            hashes.ExceptWith(shared);
            if (hashes.Count == 0) return new List<RegisteredZip>();
            var paths = sources.Where(s => s.campaignId == id).Select(s => s.path).ToList();
            foreach (string root in downloadRoots ?? Enumerable.Empty<string>())
            {
                cancellation.ThrowIfCancellationRequested();
                if (!Directory.Exists(root)) continue;
                paths.AddRange(RemovalZipFiles(root, cancellation, progress));
            }
            paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var result = new List<RegisteredZip>();
            var verifiedSizes = new Dictionary<string, long>();
            for (int index = 0; index < paths.Count; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                string path = paths[index];
                string stage = "ZIP 확인 중 (" + (index + 1) + "/" + paths.Count + ") · " + Path.GetFileName(path);
                if (progress != null) progress(new WorkProgress(stage, index, paths.Count));
                if (!File.Exists(path)) continue;
                CheckZipPath(path);
                if (hashes.All(knownHash => verifiedSizes.ContainsKey(knownHash)) &&
                    !verifiedSizes.Values.Contains(new FileInfo(path).Length)) continue;
                long verifiedLength = 0;
                string hash = Data.Hash(path, cancellation, (completed, total) => {
                    verifiedLength = total;
                    if (progress != null) progress(new WorkProgress(stage, completed, total, true));
                });
                if (hashes.Contains(hash))
                {
                    verifiedSizes[hash] = verifiedLength;
                    result.Add(new RegisteredZip { campaignId = id, path = Path.GetFullPath(path), hash = hash });
                }
            }
            cancellation.ThrowIfCancellationRequested();
            if (progress != null) progress(new WorkProgress("삭제할 ZIP 확인 완료", paths.Count, paths.Count));
            return result;
        }
        public void Remove(string id, string versionKey, RemovalKind kind, bool cleanupConfirmed = false,
            IEnumerable<RegisteredZip> zipTargets = null)
        {
            Id(id); Id(versionKey);
            if (Directory.Exists(transaction)) throw new InvalidOperationException("복구 대기 중에는 캠페인을 제거할 수 없습니다.");
            var versions = Versions(id);
            var selected = versions.SingleOrDefault(c => c.versionKey == versionKey);
            if (selected == null || Installed(id) == null) throw new InvalidOperationException("제거할 보관 버전을 찾을 수 없습니다.");
            var state = Current();
            if (kind == RemovalKind.PreviousVersions)
            {
                var previous = versions.Where(c => c.versionKey != versionKey &&
                    c.versionKey != Installed(id).versionKey && !(state.active == id && c.versionKey == state.versionKey)).ToList();
                foreach (var item in previous) CheckRemovalTree(Data.Under(packages, id + "\\versions\\" + Id(item.versionKey)));
                foreach (var item in previous) RecycleDirectory(Data.Under(packages, id + "\\versions\\" + Id(item.versionKey)));
                return;
            }
            if (kind != RemovalKind.AllInformation && kind != RemovalKind.CampaignOnly) throw new ArgumentException("제거 범위를 선택해 주세요.");
            if (kind == RemovalKind.AllInformation && !cleanupConfirmed)
                throw new InvalidOperationException("삭제할 파일 목록을 먼저 확인해 주세요.");
            var zips = kind == RemovalKind.AllInformation ? (zipTargets ?? RemovalZips(id)).ToList() : new List<RegisteredZip>();
            var knownHashes = new HashSet<string>(versions.Select(c => c.versionKey)
                .Concat(SourceZips().Where(s => s.campaignId == id).Select(s => s.hash)));
            var otherHashes = new HashSet<string>(All().Where(c => c.id != id).SelectMany(c => Versions(c.id)).Select(c => c.versionKey)
                .Concat(SourceZips().Where(s => s.campaignId != id).Select(s => s.hash)));
            foreach (var zip in zips)
            {
                if (zip.campaignId != id || !knownHashes.Contains(zip.hash) || otherHashes.Contains(zip.hash))
                    throw new InvalidDataException("다른 캠페인의 ZIP은 삭제하지 않습니다.");
                CheckZipPath(zip.path);
                using (File.Open(zip.path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                if (Data.Hash(zip.path) != zip.hash) throw new IOException("확인 후 ZIP이 변경되어 삭제를 중단했습니다.");
            }
            var remaining = versions.Where(c => c.versionKey != versionKey).ToList();
            bool removePackage = kind == RemovalKind.AllInformation || remaining.Count == 0;
            string target = Data.Under(packages, Id(id) + (removePackage ? "" : "\\versions\\" + Id(versionKey)));
            CheckRemovalTree(target);
            if (kind == RemovalKind.AllInformation || (state.active == id && state.versionKey == versionKey))
            {
                Unapply(id);
                CleanCreatedDirectories(kind == RemovalKind.AllInformation ? versions : new List<Campaign> { selected });
            }
            foreach (var zip in zips)
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(zip.path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                    Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
            RecycleDirectory(target);
            if (kind == RemovalKind.AllInformation)
                Data.Write(Path.Combine(Root, "source-zips.json"), SourceZips().Where(s => s.campaignId != id).ToList());
            if (removePackage)
            {
                var links = SiteLinks();
                if (kind == RemovalKind.AllInformation && links.Remove(id)) Data.Write(Path.Combine(Root, "site-links.json"), links);
            }
            else if (Installed(id).versionKey == versionKey) Data.Write(ManifestPath(id), remaining.Last());
        }
        static void RecycleDirectory(string target)
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(target,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
        public string RemovalSummary(string id, IEnumerable<RegisteredZip> zips = null)
        {
            Id(id);
            var state = Current();
            var sources = (zips ?? Enumerable.Empty<RegisteredZip>()).ToList();
            var shown = sources.Take(12).Select(zip => zip.path).ToList();
            return "보관소: " + Data.Under(packages, id) + "\n" +
                (state.active == id ? "게임: " + state.gameRoot + "\n적용 파일: " + state.files.Count + "개" :
                "현재 이 캠페인이 게임에 적용되어 있지 않습니다.") +
                "\n확인된 ZIP: " + sources.Count + "개\n" + String.Join("\n", shown) +
                (sources.Count > shown.Count ? "\n외 " + (sources.Count - shown.Count) + "개" : "") +
                "\n확인된 ZIP만 휴지통으로 보냅니다. 위치를 찾지 못한 ZIP과 다른 캠페인이 공유하는 ZIP은 유지합니다." +
                "\n뱅크·세이브·공용 복구 백업은 유지합니다.";
        }
        void Unapply(string id)
        {
            var old = Current();
            if (old.active != id) return;
            RestoreBase();
        }
        public void RestoreBase(Action<WorkProgress> progress = null)
        {
            var old = Current();
            if (Directory.Exists(transaction)) throw new InvalidOperationException("미완료 전환을 먼저 복구해 주세요.");
            if (old.active == null) return;
            ExternalCampaigns.RequireGameClosed(old.gameRoot);
            Transition(null, old.gameRoot, null, progress);
        }
        void CleanCreatedDirectories(List<Campaign> versions)
        {
            var state = Current();
            if (String.IsNullOrEmpty(state.gameRoot)) return;
            var files = versions.SelectMany(c => c.files).Select(f => f.path).ToList();
            var removed = new List<string>();
            foreach (string relative in state.createdDirectories.OrderByDescending(p => p.Length))
            {
                if (!files.Any(f => f.StartsWith(relative + "\\", StringComparison.OrdinalIgnoreCase))) continue;
                string path = Target(state.gameRoot, relative);
                if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) { Directory.Delete(path); removed.Add(relative); }
            }
            state.createdDirectories = state.createdDirectories.Except(removed).ToList();
            if (versions.Any(c => c.id == state.previousActive)) state.previousActive = null;
            Data.Write(statePath, state);
        }
        static void CheckRemovalTree(string directory)
        {
            foreach (string child in Directory.GetFileSystemEntries(directory))
            {
                string safe = Data.Under(directory, Path.GetFileName(child));
                if (Directory.Exists(safe)) CheckRemovalTree(safe);
            }
        }
        Dictionary<string, string> SiteLinks()
        {
            string path = Path.Combine(Root, "site-links.json");
            return File.Exists(path) ? Data.Read<Dictionary<string, string>>(path) : new Dictionary<string, string>();
        }
        public string CatalogueId(string packageId)
        {
            string siteId;
            return SiteLinks().TryGetValue(Id(packageId), out siteId) ? siteId : packageId;
        }
        public string PackageId(string siteId)
        {
            Id(siteId);
            var matches = All().Where(c => CatalogueId(c.id) == siteId).ToList();
            if (matches.Count > 1) throw new InvalidOperationException("같은 사이트 캠페인에 설치 자료가 여러 개 연결돼 있습니다.");
            return matches.Count == 1 ? matches[0].id : siteId;
        }
        public void LinkSite(string packageId, string siteId)
        {
            Id(packageId); Id(siteId);
            if (Installed(packageId) == null) throw new InvalidOperationException("설치된 캠페인을 먼저 선택해 주세요.");
            if (All().Any(c => c.id != packageId && CatalogueId(c.id) == siteId))
                throw new InvalidOperationException("이미 다른 설치 자료가 이 사이트 캠페인과 연결돼 있습니다.");
            var links = SiteLinks(); links[packageId] = siteId;
            Data.Write(Path.Combine(Root, "site-links.json"), links);
        }
        public Campaign ImportForSite(string zip, Campaign site, Action<WorkProgress> progress = null, bool allowSlotMismatch = false)
        {
            if (site == null || site.kind == "resource" || site.kind == "dedicated") throw new InvalidDataException("CCM으로 연결할 사이트 캠페인을 선택해 주세요.");
            var metadata = InspectZip(zip);
            if (!allowSlotMismatch && !Catalogue.SupportsSlot(site, metadata.slot))
                throw new InvalidDataException("ZIP의 적용 대상과 선택한 사이트 캠페인의 적용 대상이 다릅니다. 연결할 캠페인을 다시 확인해 주세요.");
            if (!AcceptsSiteMetadata(metadata, site))
                return ImportLocalMetadata(zip, metadata, progress);
            return Import(zip, PackageId(site.id), metadata.slot, progress);
        }
        public Campaign ImportLocalMetadata(string zip, Campaign metadata, Action<WorkProgress> progress = null)
        {
            var matches = All().Where(c => SiteMatcher.SameMetadata(c, metadata)).ToList();
            string id = matches.Count == 1 ? matches[0].id : SiteMatcher.MetadataId(metadata);
            return Import(zip, id, metadata.slot, progress);
        }
        public bool AcceptsSiteMetadata(Campaign metadata, Campaign site)
        {
            if (site == null || site.kind == "dedicated" || site.kind == "resource") return false;
            var known = All().Where(c => CatalogueId(c.id) == site.id)
                .SelectMany(c => Versions(c.id)).ToList();
            return known.Count == 0 || known.Any(c => SiteMatcher.SameMetadata(c, metadata));
        }
        public Campaign MatchSiteMetadata(Campaign metadata, IEnumerable<Campaign> source)
        {
            var catalogue = source.ToList();
            var linked = LinkedSite(metadata, catalogue);
            if (linked != null) return linked;
            var site = SiteMatcher.Unique(metadata, catalogue);
            return site != null && AcceptsSiteMetadata(metadata, site) ? site : null;
        }
        public Campaign AutoLink(Campaign package, IEnumerable<Campaign> source)
        {
            var catalogue = source.ToList();
            if (!package.id.StartsWith("local-", StringComparison.Ordinal) || CatalogueId(package.id) != package.id || catalogue.Any(c => c.id == package.id)) return null;
            var site = MatchSiteMetadata(package, catalogue);
            if (site == null || All().Any(c => c.id != package.id && CatalogueId(c.id) == site.id)) return null;
            LinkSite(package.id, site.id); return site;
        }
        public Campaign LinkedSite(Campaign metadata, IEnumerable<Campaign> catalogue)
        {
            var ids = All().Where(c => SiteMatcher.SameMetadata(c, metadata))
                .Select(c => CatalogueId(c.id)).Distinct().ToList();
            var sites = catalogue.Where(c => ids.Contains(c.id)).ToList();
            return sites.Count == 1 ? sites[0] : null;
        }
        public string SeparateStoredVersion(string packageId, string versionKey, Action<int> fault = null)
        {
            Id(packageId); Id(versionKey);
            var state = Current();
            if (state.active == packageId || state.previousActive == packageId || Directory.Exists(transaction))
                throw new InvalidOperationException("현재·직전 적용 자료 또는 전환 중인 자료는 자동 분리하지 않습니다.");
            var versions = Versions(packageId);
            var selected = versions.Single(c => c.versionKey == versionKey);
            if (versions.Count < 2) throw new InvalidOperationException("서로 다른 자료가 섞인 항목만 분리할 수 있습니다.");
            var retained = versions.Where(c => c.versionKey != versionKey).ToList();
            if (retained.Any(c => SiteMatcher.SameMetadata(c, selected)))
                throw new InvalidOperationException("같은 모드의 버전은 분리하지 않습니다.");
            string newId = SiteMatcher.MetadataId(selected);
            string destination = Data.Under(packages, newId);
            if (Directory.Exists(destination)) throw new IOException("분리할 보관 항목이 이미 있습니다.");
            string source = VersionFolder(selected);
            CheckRemovalTree(source);
            var originals = new Dictionary<string, byte[]> {
                { ManifestPath(packageId), File.ReadAllBytes(ManifestPath(packageId)) },
                { Path.Combine(source, "manifest.json"), File.ReadAllBytes(Path.Combine(source, "manifest.json")) }
            };
            string zipIndex = Path.Combine(Root, "source-zips.json");
            if (File.Exists(zipIndex)) originals.Add(zipIndex, File.ReadAllBytes(zipIndex));
            string backup = Path.Combine(Root, "identity-backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(backup);
            foreach (var pair in originals)
            {
                string saved = Data.Under(backup, pair.Key.Substring(Root.TrimEnd('\\').Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(saved));
                File.WriteAllBytes(saved, pair.Value);
            }
            bool moved = false;
            string movedVersion = Path.Combine(destination, "versions", versionKey);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(movedVersion));
                Directory.Move(source, movedVersion); moved = true;
                if (fault != null) fault(1);
                selected.id = newId;
                Data.Write(Path.Combine(movedVersion, "manifest.json"), selected);
                Data.Write(ManifestPath(newId), selected);
                var latest = retained.OrderByDescending(c => c.version, StringComparer.OrdinalIgnoreCase).First();
                Data.Write(ManifestPath(packageId), latest);
                if (fault != null) fault(2);
                if (File.Exists(zipIndex))
                {
                    var zips = SourceZips();
                    foreach (var zip in zips.Where(z => z.campaignId == packageId && z.hash == versionKey))
                        zip.campaignId = newId;
                    Data.Write(zipIndex, zips);
                }
                if (fault != null) fault(3);
                return newId;
            }
            catch
            {
                if (moved) Directory.Move(movedVersion, source);
                foreach (var pair in originals) File.WriteAllBytes(pair.Key, pair.Value);
                if (Directory.Exists(destination)) Directory.Delete(destination, true);
                throw;
            }
        }
        public static bool MissingCcmMetadata(string zip)
        {
            // Read only the archive directory. An arbitrary external ZIP must
            // never be unpacked into StarCraft II by the CCM import pipeline.
            using (var archive = ZipFile.OpenRead(zip))
                return !archive.Entries.Any(entry =>
                    String.Equals(entry.Name, "metadata.txt", StringComparison.OrdinalIgnoreCase));
        }
        public static Campaign InspectZip(string zip)
        {
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entries = archive.Entries.Where(e => String.Equals(e.Name, "metadata.txt", StringComparison.OrdinalIgnoreCase)).ToList();
                if (entries.Count != 1 || entries[0].Length > 1024 * 1024) throw new InvalidDataException("1MiB 이하 metadata.txt가 정확히 한 개 있는 CCM ZIP이 필요합니다.");
                using (var reader = new StreamReader(entries[0].Open(), Encoding.UTF8))
                {
                    var item = ReadMetadata(reader); item.sourceFileName = Path.GetFileName(zip); return item;
                }
            }
        }
        static Campaign ReadMetadata(TextReader reader)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                int equals = line.IndexOf('=');
                if (equals > 0) values[line.Substring(0, equals).Trim()] = line.Substring(equals + 1).Trim();
            }
            if (!values.ContainsKey("title") || !values.ContainsKey("campaign")) throw new InvalidDataException("metadata.txt에 title·campaign이 필요합니다.");
            return new Campaign { name = values["title"], slot = Slot(values["campaign"]), version = values.ContainsKey("version") ? values["version"] : "",
                summary = values.ContainsKey("desc") ? values["desc"] : "", sourceAuthor = values.ContainsKey("author") ? values["author"] : "", files = new List<OwnedFile>() };
        }

        // Campaign alias rules adapted from SC2CCM Mod.SetCampaign (7thAce/GGG, 2022).
        // A recognized campaign is required; the upstream unknown-campaign fallback is not installed.
        public static string Slot(string campaign)
        {
            string s = campaign.ToLowerInvariant();
            if (s.Contains("wings") || s.Contains("liberty") || s.Contains("wol")) return "wol";
            if (s.Contains("heart") || s.Contains("swarm") || s.Contains("hots")) return "hots";
            if (s.Contains("legacy") || s.Contains("void") || s.Contains("lotv")) return "lotv";
            if (s.Contains("nova") || s.Contains("covert") || s.Contains("ops") || s.Contains("nco")) return "nco";
            throw new InvalidDataException("알 수 없는 campaign 값: " + campaign);
        }
        private static string CampaignPath(string slot)
        {
            switch (slot)
            {
                case "wol": return @"Maps\Campaign";
                case "hots": return @"Maps\Campaign\swarm";
                case "lotv": return @"Maps\Campaign\void";
                case "nco": return @"Maps\Campaign\nova";
                default: throw new InvalidDataException("지원하지 않는 캠페인입니다.");
            }
        }
        public Campaign Import(string zipPath, string id, string expectedSlot = null, Action<WorkProgress> progress = null)
        {
            Id(id);
            if (progress != null) progress(new WorkProgress("ZIP 확인값 계산 중"));
            string versionKey = Data.Hash(zipPath);
            string final = Data.Under(packages, id + "\\versions\\" + versionKey);
            if (Directory.Exists(final))
                throw new InvalidOperationException("같은 ZIP이 이미 보관돼 있습니다. 기존 버전은 변경하지 않습니다.");
            string manifest = ManifestPath(id);
            byte[] previousManifest = File.Exists(manifest) ? File.ReadAllBytes(manifest) : null;
            bool published = false, manifestChanged = false;
            string stage = Path.Combine(Root, "import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
            string payload = Path.Combine(stage, "payload");
            Directory.CreateDirectory(payload);
            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count > 100000) throw new InvalidDataException("ZIP 항목이 너무 많습니다.");
                long expanded = 0;
                int extracted = 0;
                int extractCount = archive.Entries.Count(entry => !String.IsNullOrEmpty(entry.Name));
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in archive.Entries)
                {
                    string destination = Data.Under(payload, entry.FullName);
                    if (!seen.Add(destination)) throw new InvalidDataException("ZIP에 중복 경로가 있습니다.");
                    expanded = checked(expanded + entry.Length);
                    if (expanded > 16L * 1024 * 1024 * 1024) throw new InvalidDataException("ZIP 해제 크기가 16GiB를 초과합니다.");
                    if (String.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, false);
                    if (progress != null) progress(new WorkProgress("ZIP 압축 해제 중", ++extracted, extractCount));
                }
            }
            string[] metadata = Directory.GetFiles(payload, "metadata.txt", SearchOption.AllDirectories);
            if (metadata.Length != 1) throw new InvalidDataException("metadata.txt가 정확히 한 개 있는 CCM ZIP만 지원합니다. 원본 ZIP은 보존됩니다.");
            Campaign item;
            using (var reader = new StreamReader(metadata[0], Encoding.UTF8)) item = ReadMetadata(reader);
            item.id = id; item.versionKey = versionKey; item.sourceFileName = Path.GetFileName(zipPath);
            if (expectedSlot != null && item.slot != expectedSlot)
                throw new InvalidDataException("ZIP의 적용 대상과 선택한 사이트 캠페인의 적용 대상이 다릅니다. 연결할 캠페인을 다시 확인해 주세요.");
            string parent = Path.GetDirectoryName(metadata[0]);
            // CCM packages may contain either Maps/metadata.txt or the actual
            // Maps/Campaign[/swarm|void|nova]/metadata.txt game directory tree.
            string packageRoot = null;
            if (String.Equals(Path.GetFileName(parent), "Maps", StringComparison.OrdinalIgnoreCase))
                packageRoot = Path.GetDirectoryName(parent);
            else
            {
                var directory = new DirectoryInfo(parent);
                bool matchesGamePath = true;
                foreach (string part in CampaignPath(item.slot).Split('\\').Reverse())
                {
                    if (directory == null || !String.Equals(directory.Name, part, StringComparison.OrdinalIgnoreCase))
                    { matchesGamePath = false; break; }
                    directory = directory.Parent;
                }
                if (matchesGamePath && directory != null) packageRoot = directory.FullName;
            }
            string siblingMods = packageRoot == null ? null : Path.Combine(packageRoot, "Mods") + "\\";
            // A translated CCM ZIP can carry original artwork and credits next to
            // its Maps/Mods folders. Keep those resources in the version archive,
            // never copy them into the StarCraft II installation.
            if (packageRoot != null)
            {
                string artworkRoot = Path.Combine(packageRoot, "hangar");
                var artwork = new[] { "thumbnail.png", "thumbnail.jpg", "thumbnail.jpeg" }
                    .Select(name => Path.Combine(artworkRoot, name)).Where(File.Exists).ToArray();
                if (artwork.Length > 1) throw new InvalidDataException("ZIP에는 격납고 썸네일을 한 장만 넣어 주세요.");
                if (artwork.Length == 1)
                {
                    if (new FileInfo(artwork[0]).Length > 8 * 1024 * 1024)
                        throw new InvalidDataException("썸네일은 8MB 이하여야 합니다.");
                    using (var image = Image.FromFile(artwork[0]))
                        if (image.Width < 200 || image.Height < 100 || image.Width > 8192 || image.Height > 8192 ||
                            (long)image.Width * image.Height > 30000000)
                            throw new InvalidDataException("썸네일 이미지 해상도가 적절하지 않습니다.");
                    // Preserve wrapper folders (e.g. Package/Maps + Package/hangar).
                    // A bare payload/hangar path silently breaks the thumbnail.
                    string wrapper = packageRoot.Substring(payload.TrimEnd('\\').Length)
                        .TrimStart('\\');
                    item.thumbnail = @"packages\" + id + @"\versions\" + versionKey +
                        @"\payload\" + (wrapper.Length == 0 ? "" : wrapper + @"\") +
                        @"hangar\" + Path.GetFileName(artwork[0]);
                }
                string creditsFile = Path.Combine(artworkRoot, "credits.json");
                if (File.Exists(creditsFile))
                {
                    if (new FileInfo(creditsFile).Length > 8192)
                        throw new InvalidDataException("격납고 제작자 정보는 8KB 이하여야 합니다.");
                    var credits = Data.Read<Campaign>(creditsFile);
                    if (credits == null) throw new InvalidDataException("격납고 제작자 정보가 비어 있습니다.");
                    if (!String.IsNullOrWhiteSpace(credits.sourceAuthor)) item.sourceAuthor = credits.sourceAuthor.Trim();
                    if (!String.IsNullOrWhiteSpace(credits.translator)) item.translator = credits.translator.Trim();
                    if (DistributionPolicy.Https(credits.creatorUrl)) item.creatorUrl = credits.creatorUrl;
                    if (DistributionPolicy.Https(credits.translatorUrl)) item.translatorUrl = credits.translatorUrl;
                    if (!String.IsNullOrWhiteSpace(credits.creatorLabel)) item.creatorLabel = credits.creatorLabel.Trim();
                    if (!String.IsNullOrWhiteSpace(credits.translatorLabel)) item.translatorLabel = credits.translatorLabel.Trim();
                }
            }
            var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
            {
                bool inCampaign = file.StartsWith(parent + "\\", StringComparison.OrdinalIgnoreCase);
                bool inSiblingMods = siblingMods != null && file.StartsWith(siblingMods, StringComparison.OrdinalIgnoreCase);
                if (!inCampaign && !inSiblingMods)
                {
                    // Companion folders outside Maps/Mods remain in storage, never the game.
                    if (packageRoot != null && file.StartsWith(packageRoot + "\\", StringComparison.OrdinalIgnoreCase) &&
                        new[] { ".txt", ".md", ".json", ".png", ".jpg", ".jpeg" }.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
                    throw new InvalidDataException("게임에 배치할 수 없는 ZIP 경로입니다: " + file.Substring(payload.Length + 1) +
                        "\n캠페인 폴더·같은 패키지의 Mods 폴더·TXT/MD/JSON/PNG 부속 자료만 지원합니다. 원본 ZIP은 보존됩니다.");
                }
                string relative = file.Substring(inCampaign ? parent.Length + 1 : siblingMods.Length);
                string[] parts = relative.Split('\\');
                if (parts.Any(p => p.Equals("lotvprologue", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("프롤로그 별도 배치 패키지는 첫 버전에서 검증 전 보류합니다.");
                int modIndex = Array.FindIndex(parts, p => p.EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase));
                if (inSiblingMods && modIndex < 0)
                    throw new InvalidDataException("Mods 폴더에는 SC2Mod 의존 파일·폴더만 사용할 수 있습니다.");
                string target = modIndex >= 0 ? @"Mods\" + String.Join("\\", parts.Skip(modIndex))
                    : CampaignPath(item.slot) + "\\" + relative;
                Data.Under(Root, "test\\" + target);
                if (!destinations.Add(target)) throw new InvalidDataException("같은 게임 경로로 배치되는 파일이 여러 개 있습니다.");
                item.files.Add(new OwnedFile { path = target, hash = Data.Hash(file) });
                Data.Copy(file, Data.Under(stage, "deploy\\" + target));
                if (progress != null) progress(new WorkProgress("캠페인 파일 검증·등록 중 · " + Path.GetFileName(file)));
            }
            Data.Write(Path.Combine(stage, "manifest.json"), item);
            Directory.CreateDirectory(Path.GetDirectoryName(final));
            Directory.Move(stage, final);
            published = true;
            Data.Write(manifest, item);
            manifestChanged = true;
            RememberZip(item, zipPath);
            return item;
            }
            catch
            {
                if (manifestChanged)
                {
                    if (previousManifest == null) File.Delete(manifest);
                    else
                    {
                        string restore = manifest + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        File.WriteAllBytes(restore, previousManifest);
                        File.Replace(restore, manifest, null);
                    }
                }
                if (published) Directory.Delete(final, true);
                // This unique directory belongs only to this import attempt.
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                throw;
            }
        }
        internal static string Target(string gameRoot, string relative)
        {
            if (!(relative.StartsWith(@"Maps\Campaign\", StringComparison.OrdinalIgnoreCase) ||
                  relative.StartsWith(@"Mods\", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("캠페인·모드 폴더 외의 경로는 변경하지 않습니다.");
            string target = Data.Under(gameRoot, relative);
            string normalized = target.Substring(Path.GetFullPath(gameRoot).TrimEnd('\\').Length + 1);
            if (!(normalized.StartsWith(@"Maps\Campaign\", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(@"Mods\", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("캠페인·모드 폴더 외의 경로는 변경하지 않습니다.");
            return target;
        }
        internal static List<string> ApplicationDirectories(Campaign campaign, string gameRoot, bool mods)
        {
            string prefix = mods ? @"Mods\" : @"Maps\Campaign\";
            return (campaign.files ?? new List<OwnedFile>())
                .Where(file => file.path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetDirectoryName(Target(gameRoot, Container(file.path))))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }
        public void Recover()
        {
            string path = Path.Combine(transaction, "journal.json");
            if (!File.Exists(path)) return;
            var j = Data.Read<Journal>(path);
            if (j.moves != null)
            {
                if (!j.committed)
                {
                    foreach (var move in j.moves.AsEnumerable().Reverse())
                    {
                        ValidateMovePath(move.source, j.gameRoot); ValidateMovePath(move.destination, j.gameRoot);
                        if (!File.Exists(move.destination) && !Directory.Exists(move.destination)) continue;
                        VerifyNode(move.destination, move.node);
                        if (File.Exists(move.source) || Directory.Exists(move.source))
                        { VerifyNode(move.source, move.node); RemoveNode(move.destination); }
                        else MoveDeploymentNode(move.destination, move.source, move.node);
                    }
                    Data.Write(statePath, j.before);
                }
                Directory.Move(transaction, Path.Combine(Root, "recovered-" + Guid.NewGuid().ToString("N")));
                return;
            }
            if (j.snapshots != null)
            {
                foreach (var snapshot in j.snapshots)
                    VerifyNode(Data.Under(transaction, "before\\" + snapshot.path), snapshot);
                foreach (var snapshot in j.snapshots)
                    RestoreNode(Data.Under(transaction, "before\\" + snapshot.path), Target(j.gameRoot, snapshot.path), snapshot);
            }
            else
            {
                foreach (string relative in j.paths)
                {
                    string target = Target(j.gameRoot, relative);
                    if (j.existed.Contains(relative)) Data.Copy(Data.Under(transaction, "before\\" + relative), target);
                    else if (File.Exists(target)) File.Delete(target);
                }
            }
            Data.Write(statePath, j.before);
            Directory.Move(transaction, Path.Combine(Root, "recovered-" + Guid.NewGuid().ToString("N")));
        }
        public void Activate(string id, string gameRoot, bool menuConfirmed, Action<int> fault, string versionKey = null, Action<WorkProgress> progress = null)
        {
            if (!menuConfirmed) throw new InvalidOperationException("미션을 종료하고 게임 메뉴인지 확인해 주세요.");
            gameRoot = Path.GetFullPath(gameRoot);
            if (!File.Exists(Path.Combine(gameRoot, "StarCraft II.exe")))
                throw new InvalidOperationException("StarCraft II.exe가 있는 폴더를 선택해 주세요.");
            if (Directory.Exists(transaction)) throw new InvalidOperationException("미완료 전환이 있습니다. 복구 후 다시 시도해 주세요.");
            Campaign item = versionKey == null ? Installed(id) : Versions(id).SingleOrDefault(c => c.versionKey == versionKey);
            if (item == null) throw new InvalidOperationException("먼저 ZIP을 설치해 주세요.");
            DedicatedLibrary.ProtectCcm(Root, item);
            Transition(item, gameRoot, fault, progress);
        }
        static string Container(string relative)
        {
            string[] parts = relative.Split('\\');
            int end = Array.FindIndex(parts, p => p.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase));
            return end < 0 ? relative : String.Join("\\", parts.Take(end + 1));
        }
        // Development-only notes and scripts bundled with some campaign releases
        // remain in the stored ZIP/deploy archive but must not be placed in the game.
        // Files inside a .SC2Map/.SC2Mod container are always runtime content.
        internal static List<OwnedFile> DeploymentFiles(IEnumerable<OwnedFile> contents)
        {
            return contents.Where(file => {
                string[] parts = file.path.Split('\\');
                if (parts.Any(part => part.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase) ||
                                      part.EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase))) return true;
                if (parts.Any(part => part.Equals("_notes", StringComparison.OrdinalIgnoreCase))) return false;
                string extension = Path.GetExtension(file.path);
                return !new[] { ".md", ".tsv", ".diff", ".patch", ".py", ".sh", ".js" }
                    .Contains(extension, StringComparer.OrdinalIgnoreCase);
            }).ToList();
        }
        static OwnedFile Node(string path, string relative, Dictionary<string,string> observedHashes = null)
        {
            bool directory = Directory.Exists(path);
            string hash = null;
            if (directory)
            {
                CheckRemovalTree(path);
                var entries = Directory.GetFileSystemEntries(path).OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
                    .Select(p => {
                        var child = Node(p, "", observedHashes);
                        return (child.directory ? "D:" : "F:") + Path.GetFileName(p).ToLowerInvariant() + ":" + child.hash;
                    });
                using (var sha = SHA256.Create())
                    hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(String.Join("\n", entries)))).Replace("-", "").ToLowerInvariant();
            }
            else if (File.Exists(path))
            {
                hash = Data.Hash(path);
                if (observedHashes != null) observedHashes[path] = hash;
            }
            return new OwnedFile { path = relative, hash = hash, directory = directory };
        }
        static void CopyNode(string source, string destination)
        {
            if (File.Exists(source)) { Data.Copy(source, destination); return; }
            if (!Directory.Exists(source)) return;
            CheckRemovalTree(source); Directory.CreateDirectory(destination);
            foreach (string child in Directory.GetFileSystemEntries(source))
                CopyNode(child, Data.Under(destination, Path.GetFileName(child)));
        }
        static void RemoveNode(string path)
        {
            if (Directory.Exists(path)) { CheckRemovalTree(path); Directory.Delete(path, true); }
            else if (File.Exists(path)) File.Delete(path);
        }
        static void VerifyNode(string path, OwnedFile expected, Dictionary<string,string> observedHashes = null)
        {
            var actual = Node(path, expected.path, observedHashes);
            if (actual.hash != expected.hash || actual.directory != expected.directory)
                throw new InvalidOperationException("파일·폴더가 외부에서 변경됐습니다. 보호를 위해 작업을 중단합니다: " + expected.path);
        }
        static void RestoreNode(string source, string destination, OwnedFile snapshot)
        {
            RemoveNode(destination);
            if (snapshot.hash != null) { CopyNode(source, destination); VerifyNode(destination, snapshot); }
        }
        static void CheckLocks(string path)
        {
            if (Directory.Exists(path))
            {
                CheckRemovalTree(path);
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            }
            else if (File.Exists(path)) using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        }
        string OriginalPath(OwnedFile file) { return Data.Under(Root, file.backup ?? "originals\\" + file.path); }
        void ValidateMovePath(string path, string gameRoot)
        {
            string full = Path.GetFullPath(path);
            string prefix = Root.TrimEnd('\\') + "\\";
            if (full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            { Data.Under(Root, full.Substring(prefix.Length)); return; }
            prefix = Path.GetFullPath(gameRoot).TrimEnd('\\') + "\\";
            if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("복구 이동 경로가 보관소·게임 폴더 밖입니다.");
            Target(gameRoot, full.Substring(prefix.Length));
        }
        static void MoveDeploymentNode(string source, string destination, OwnedFile expected)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (Storage.SameVolume(source, destination))
            {
                if (expected.directory) Directory.Move(source, destination);
                else File.Move(source, destination);
            }
            else
            {
                // A failed partial copy must never be mistaken for a completed move.
                string partial = destination + ".moving-" + Guid.NewGuid().ToString("N");
                try { CopyNode(source, partial); VerifyNode(partial, expected);
                    if (expected.directory) Directory.Move(partial, destination); else File.Move(partial, destination);
                }
                catch { RemoveNode(partial); throw; }
                RemoveNode(source);
            }
        }
        void MoveRecorded(Journal journal, string source, string destination)
        {
            var node = Node(source, "");
            if (node.hash == null) return;
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new IOException("이동 대상에 파일이 이미 있습니다: " + destination);
            ValidateMovePath(source, journal.gameRoot); ValidateMovePath(destination, journal.gameRoot);
            journal.moves.Add(new DeploymentMove { source = source, destination = destination, node = node });
            Data.Write(Path.Combine(transaction, "journal.json"), journal);
            MoveDeploymentNode(source, destination, node);
        }
        void Transition(Campaign item, string gameRoot, Action<int> fault, Action<WorkProgress> progress = null)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var stages = new List<object>();
            string currentStage = "prepare";
            long stageStart = 0;
            Action<string> phase = next => {
                long now = watch.ElapsedMilliseconds;
                stages.Add(new { stage = currentStage, milliseconds = now - stageStart });
                currentStage = next; stageStart = now;
            };
            bool completed = false;
            try { TransitionCore(item, gameRoot, fault, progress, phase); completed = true; }
            finally
            {
                phase("finished");
                try { Data.Write(Path.Combine(Root, "last-activation-timing.json"), new {
                    completed = completed, totalMilliseconds = watch.ElapsedMilliseconds, stages = stages }); }
                catch (Exception error) { Console.Error.WriteLine("Activation timing could not be saved: " + error.Message); }
            }
        }
        void TransitionCore(Campaign item, string gameRoot, Action<int> fault, Action<WorkProgress> progress, Action<string> phase)
        {
            if (Directory.Exists(transaction)) throw new InvalidOperationException("미완료 전환이 있습니다. 복구 후 다시 시도해 주세요.");
            var old = Current();
            if (old.originals.Any(f => f.backup == null))
            {
                // Existing per-file journals/backups are upgraded by the proven legacy path once.
                LegacyTransition(item, gameRoot, fault, progress);
                return;
            }
            if (old.active != null && !String.Equals(old.gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("활성 캠페인의 게임 경로와 다릅니다.");
            phase("verify-active-files");
            var observedHashes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in old.managed) VerifyNode(Target(gameRoot, node.path), node, observedHashes);
            foreach (var file in old.files)
            {
                string path = Target(gameRoot, file.path), hash;
                if (!observedHashes.TryGetValue(path, out hash) && File.Exists(path)) hash = Data.Hash(path);
                if (!File.Exists(path) || hash != file.hash)
                    throw new InvalidOperationException("이전 활성 파일이 외부에서 변경됐습니다. 보호를 위해 전환하지 않습니다: " + file.path);
            }
            if (item != null && old.active == item.id && old.versionKey == item.versionKey && old.movedDeployment) return;
            phase("verify-stored-files");
            var files = item == null ? new List<OwnedFile>() : DeploymentFiles(item.files);
            foreach (var file in files)
            {
                if (progress != null) progress(new WorkProgress("보관 파일 확인 중 · " + Path.GetFileName(file.path)));
                if (Data.Hash(StoredPath(item, "deploy\\" + file.path)) != file.hash)
                    throw new InvalidDataException("보관 파일의 무결성이 다릅니다.");
            }
            phase("verify-locks-and-originals");
            var oldPaths = old.files.Select(f => Container(f.path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var newPaths = files.Select(f => Container(f.path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var affected = oldPaths.Concat(newPaths).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string relative in affected) CheckLocks(Target(gameRoot, relative));
            foreach (string relative in newPaths) CheckLocks(StoredPath(item, "deploy\\" + relative));
            var originals = old.originals.ToDictionary(f => f.path, f => f, StringComparer.OrdinalIgnoreCase);
            var previous = old.active == null ? null : Versions(old.active).Single(c => c.versionKey == old.versionKey);
            if (old.movedDeployment)
                foreach (string relative in oldPaths)
                    if (File.Exists(StoredPath(previous, "deploy\\" + relative)) || Directory.Exists(StoredPath(previous, "deploy\\" + relative)))
                        throw new IOException("반환할 보관소 위치에 파일이 이미 있습니다: " + relative);
            string backupGroup = "originals-v2\\" + Guid.NewGuid().ToString("N") + "\\";
            foreach (string relative in affected)
            {
                OwnedFile original;
                if (originals.TryGetValue(relative, out original)) { VerifyNode(OriginalPath(original), original); continue; }
                if (progress != null) progress(new WorkProgress("최초 게임 원본 백업 중 · " + relative));
                string backup = Data.Under(Root, backupGroup + relative);
                CopyNode(Target(gameRoot, relative), backup);
                original = Node(backup, relative); original.backup = backupGroup + relative;
                VerifyNode(Target(gameRoot, relative), original);
                originals.Add(relative, original);
            }
            var journal = new Journal { gameRoot = gameRoot, before = old, paths = affected,
                moves = new List<DeploymentMove>() };
            Directory.CreateDirectory(transaction);
            Data.Write(Path.Combine(transaction, "journal.json"), journal);
            try
            {
                phase("return-previous-deployment");
                foreach (string relative in affected)
                {
                    bool returnToLibrary = old.movedDeployment && oldPaths.Contains(relative, StringComparer.OrdinalIgnoreCase);
                    if (progress != null) progress(new WorkProgress(returnToLibrary ? "이전 캠페인 보관소로 반환 중" : "기존 적용 파일 정리 중"));
                    MoveRecorded(journal, Target(gameRoot, relative), returnToLibrary
                        ? StoredPath(previous, "deploy\\" + relative) : Data.Under(transaction, "before\\" + relative));
                }
                phase("move-new-deployment");
                var created = new List<string>(old.createdDirectories);
                foreach (var file in files)
                    for (string parent = Path.GetDirectoryName(file.path);
                        parent != null && parent != @"Maps\Campaign" && parent != "Mods"; parent = Path.GetDirectoryName(parent))
                        if (!Directory.Exists(Target(gameRoot, parent)) && !created.Contains(parent)) created.Add(parent);
                int count = 0;
                foreach (string relative in newPaths)
                {
                    if (progress != null) progress(new WorkProgress("새 캠페인 이동 중", count, files.Count));
                    MoveRecorded(journal, StoredPath(item, "deploy\\" + relative), Target(gameRoot, relative));
                    foreach (var file in files.Where(f => Container(f.path).Equals(relative, StringComparison.OrdinalIgnoreCase)))
                    { if (fault != null) fault(++count); else count++; }
                }
                phase("restore-originals");
                foreach (string relative in oldPaths.Except(newPaths, StringComparer.OrdinalIgnoreCase))
                {
                    var original = originals[relative];
                    if (original.hash == null) continue;
                    string staged = Data.Under(transaction, "restore\\" + relative);
                    CopyNode(OriginalPath(original), staged); VerifyNode(staged, original);
                    MoveRecorded(journal, staged, Target(gameRoot, relative));
                }
                phase("verify-final-deployment");
                var managed = newPaths.Select(relative => Node(Target(gameRoot, relative), relative)).ToList();
                phase("commit-state");
                Data.Write(statePath, new State { active = item == null ? null : item.id, movedDeployment = item != null,
                    previousActive = item == null ? null : old.active == item.id ? old.previousActive : old.active,
                    versionKey = item == null ? null : item.versionKey, gameRoot = gameRoot,
                    files = files, originals = originals.Values.ToList(), createdDirectories = created, managed = managed });
                journal.committed = true;
                Data.Write(Path.Combine(transaction, "journal.json"), journal);
                Directory.Move(transaction, Path.Combine(Root, (item == null ? "completed-removal-" : "completed-") + Guid.NewGuid().ToString("N")));
            }
            catch { phase("recover"); if (progress != null) progress(new WorkProgress("이동 전 상태로 복구 중")); Recover(); throw; }
        }
        void LegacyTransition(Campaign item, string gameRoot, Action<int> fault, Action<WorkProgress> progress = null)
        {
            if (Directory.Exists(transaction)) throw new InvalidOperationException("미완료 전환이 있습니다. 복구 후 다시 시도해 주세요.");
            State old = Current();
            if (old.active != null && !String.Equals(old.gameRoot, gameRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("활성 캠페인의 게임 경로와 다릅니다.");
            foreach (var owned in old.files)
            {
                string p = Target(gameRoot, owned.path);
                if (!File.Exists(p) || Data.Hash(p) != owned.hash)
                    throw new InvalidOperationException("이전 활성 파일이 외부에서 변경됐습니다. 보호를 위해 전환하지 않습니다: " + owned.path);
            }
            foreach (var node in old.managed ?? new List<OwnedFile>()) VerifyNode(Target(gameRoot, node.path), node);
            var files = item == null ? new List<OwnedFile>() : DeploymentFiles(item.files);
            foreach (var file in files)
            {
                if (progress != null) progress(new WorkProgress("보관 파일 검증 중 · " + Path.GetFileName(file.path)));
                if (Data.Hash(StoredPath(item, "deploy\\" + file.path)) != file.hash)
                    throw new InvalidDataException("보관 파일의 무결성이 다릅니다.");
            }
            var affected = old.files.Concat(files).Select(f => Container(f.path))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (string relative in affected) CheckLocks(Target(gameRoot, relative));
            var originals = old.originals.ToDictionary(f => f.path, f => f, StringComparer.OrdinalIgnoreCase);
            string backupGroup = "originals-v2\\" + Guid.NewGuid().ToString("N") + "\\";
            foreach (string relative in affected)
            {
                if (progress != null) progress(new WorkProgress("원본 백업 중 · " + relative));
                OwnedFile original;
                if (originals.TryGetValue(relative, out original))
                {
                    VerifyNode(OriginalPath(original), original);
                    continue;
                }
                // Upgrade legacy per-file backups to one exact map/mod container snapshot.
                string backup = Data.Under(Root, backupGroup + relative);
                CopyNode(Target(gameRoot, relative), backup);
                foreach (var legacy in originals.Values.Where(f => f.path.StartsWith(relative + "\\", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    VerifyNode(OriginalPath(legacy), legacy);
                    RestoreNode(OriginalPath(legacy), Data.Under(backup, legacy.path.Substring(relative.Length + 1)), legacy);
                    originals.Remove(legacy.path);
                }
                original = Node(backup, relative); original.backup = backupGroup + relative;
                originals[relative] = original;
            }
            string staging = Path.Combine(Root, "transition-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            var journal = new Journal { gameRoot = gameRoot, before = old, paths = affected, existed = new List<string>(), snapshots = new List<OwnedFile>() };
            try
            {
                foreach (string relative in affected)
                {
                    if (progress != null) progress(new WorkProgress("교체 전 상태 백업 중 · " + relative));
                    var snapshot = Node(Target(gameRoot, relative), relative);
                    journal.snapshots.Add(snapshot);
                    CopyNode(Target(gameRoot, relative), Data.Under(staging, "before\\" + relative));
                    VerifyNode(Data.Under(staging, "before\\" + relative), snapshot);
                }
                Data.Write(Path.Combine(staging, "journal.json"), journal);
                Directory.Move(staging, transaction);
            }
            catch { if (Directory.Exists(staging)) Directory.Delete(staging, true); throw; }
            try
            {
                foreach (string relative in old.files.Select(f => Container(f.path)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var original = originals[relative];
                    RestoreNode(OriginalPath(original), Target(gameRoot, relative), original);
                }
                foreach (string relative in files.Select(f => Container(f.path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    RemoveNode(Target(gameRoot, relative));
                int count = 0;
                var created = new List<string>(old.createdDirectories);
                foreach (var f in files)
                {
                    for (string parent = Path.GetDirectoryName(f.path);
                        parent != null && !parent.Equals(@"Maps\Campaign", StringComparison.OrdinalIgnoreCase) && !parent.Equals("Mods", StringComparison.OrdinalIgnoreCase);
                        parent = Path.GetDirectoryName(parent))
                        if (!Directory.Exists(Target(gameRoot, parent)) && !created.Contains(parent))
                            created.Add(parent);
                    Data.Copy(StoredPath(item, "deploy\\" + f.path), Target(gameRoot, f.path));
                    if (Data.Hash(Target(gameRoot, f.path)) != f.hash) throw new IOException("배치 후 해시가 다릅니다.");
                    if (progress != null) progress(new WorkProgress("캠페인 적용·검증 중", count + 1, files.Count));
                    if (fault != null) fault(++count);
                }
                var managed = files.Select(f => Container(f.path)).Distinct(StringComparer.OrdinalIgnoreCase).Select(relative => Node(Target(gameRoot, relative), relative)).ToList();
                Data.Write(statePath, new State { active = item == null ? null : item.id,
                    previousActive = item == null ? null : old.active == item.id ? old.previousActive : old.active,
                    versionKey = item == null ? null : item.versionKey, gameRoot = gameRoot, files = files, originals = originals.Values.ToList(),
                    createdDirectories = created, managed = managed });
                Directory.Move(transaction, Path.Combine(Root, (item == null ? "completed-removal-" : "completed-") + Guid.NewGuid().ToString("N")));
            }
            catch { if (progress != null) progress(new WorkProgress("실패한 전환 복구 중")); Recover(); throw; }
        }
    }
}
