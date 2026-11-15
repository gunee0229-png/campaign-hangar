using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

#if UPDATE_FIXTURE
[assembly: System.Reflection.AssemblyVersion("0.2.6.0")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.6.0")]
#else
[assembly: System.Reflection.AssemblyVersion("0.2.5.5")]
[assembly: System.Reflection.AssemblyFileVersion("0.2.5.5")]
#endif
#if DEVELOPMENT
[assembly: System.Reflection.AssemblyProduct("CampaignHangar.development")]
#else
[assembly: System.Reflection.AssemblyProduct("CampaignHangar.release")]
#endif

namespace CampaignHangar
{
    public static class AppChannel
    {
        public const bool HasThemedUi = true;
#if DEVELOPMENT
        public const bool IsDevelopment = true;
        public const string Id = "development", Label = "개발판", Profile = "CampaignHangar-Development";
        public const string StorageFolder = "CampaignStorage", DownloadsFolder = "Downloads";
        public const string ReleasePath = "/gunee0229-png/campaign-hangar/releases/download/development/";
#else
        public const bool IsDevelopment = false;
        public const string Id = "release", Label = "일반", Profile = "CampaignHangar";
        public const string StorageFolder = "CampaignStorage", DownloadsFolder = "Downloads";
        public const string ReleasePath = "/gunee0229-png/campaign-hangar/releases/download/";
#endif
    }
    public sealed class AppRelease
    {
        public string version, url, sha256, notes, channel;
        public long size;
    }
    public static partial class AppUpdate
    {
        public const string Origin = "https://github.com";
        public static readonly string Manifest = AppChannel.IsDevelopment
            ? Origin + AppChannel.ReleasePath + "latest.json"
            : Origin + "/gunee0229-png/campaign-hangar/releases/latest/download/latest.json";
        public static readonly Version Current = typeof(AppUpdate).Assembly.GetName().Version;
        public const long MaximumPackage = 100 * 1024 * 1024;
        public static readonly string[] Files = new[] {
            "CampaignHangar.exe", "CampaignHangar.exe.config", "CampaignHangarSetup.exe", "CampaignHangarUpdater.exe",
            "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll", "WebView2Loader.dll",
            "THIRD-PARTY-NOTICES.txt", "WebView2-LICENSE.txt", "assets/archive.ico", "assets/archive.png",
            "assets/hangar.jpg", "assets/catalogue.js", "assets/campaign-wol.png", "assets/campaign-hots.png",
            "assets/campaign-lotv.png", "assets/campaign-nco.png"
        }.Concat(UiFiles()).ToArray();
        public static string PackageUrl(string version)
        {
            return Origin + AppChannel.ReleasePath + (AppChannel.IsDevelopment ? "" : "v" + version + "/") +
                "CampaignHangar-" + version + ".zip";
        }
        public static AppRelease Parse(string text)
        {
            var release = new JavaScriptSerializer { MaxJsonLength = 32768 }.Deserialize<AppRelease>(text);
            Version version;
            Uri url;
            if (release == null || !Version.TryParse(release.version, out version) ||
                (release.channel ?? "release") != AppChannel.Id ||
                !Uri.TryCreate(release.url, UriKind.Absolute, out url) || url.GetLeftPart(UriPartial.Authority) != Origin ||
                !url.AbsolutePath.StartsWith(AppChannel.ReleasePath, StringComparison.Ordinal) ||
                (!AppChannel.IsDevelopment && release.url != PackageUrl(release.version)) ||
                !url.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal) ||
                !String.IsNullOrEmpty(url.Query) || !String.IsNullOrEmpty(url.Fragment) || !String.IsNullOrEmpty(url.UserInfo) ||
                release.size <= 0 || release.size > MaximumPackage ||
                release.sha256 == null || !System.Text.RegularExpressions.Regex.IsMatch(release.sha256, "^[a-fA-F0-9]{64}$") ||
                (release.notes ?? "").Length > 6000)
                throw new InvalidDataException("앱 업데이트 배포 정보를 확인할 수 없습니다.");
            return release;
        }
        public static bool IsNewer(AppRelease release) { return new Version(release.version) > Current; }
        static HttpWebResponse Response(string url)
        {
            for(int hop=0;hop<5;hop++){
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                request.UserAgent = "CampaignHangar/" + Current;
                var response = (HttpWebResponse)request.GetResponse();
                if(response.StatusCode==HttpStatusCode.OK)return response;
                int status=(int)response.StatusCode;
                string location=response.Headers["Location"];
                response.Dispose();
                Uri next;
                if((status!=301&&status!=302&&status!=303&&status!=307&&status!=308)||
                    !Uri.TryCreate(new Uri(url),location,out next)||next.Scheme!="https"||
                    !next.IsDefaultPort||!String.IsNullOrEmpty(next.UserInfo)||
                    !(next.Host=="github.com"&&next.AbsolutePath.StartsWith("/gunee0229-png/campaign-hangar/releases/",StringComparison.Ordinal)||
                      next.Host=="release-assets.githubusercontent.com"||next.Host=="objects.githubusercontent.com"))
                    throw new IOException("허용되지 않은 업데이트 이동 주소입니다.");
                url=next.AbsoluteUri;
            }
            throw new IOException("업데이트 이동 횟수 제한을 초과했습니다.");
        }
        public static AppRelease Check()
        {
            using (var response = Response(Manifest))
            using (var stream = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                CopyBounded(stream, output, 32768);
                return Parse(System.Text.Encoding.UTF8.GetString(output.ToArray()).TrimStart('\uFEFF'));
            }
        }
        public static void CopyBounded(Stream source, Stream destination, long limit)
        {
            byte[] buffer = new byte[65536]; long total = 0; int count;
            while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
            { total += count; if (total > limit) throw new InvalidDataException("업데이트 파일 크기 제한을 초과했습니다."); destination.Write(buffer, 0, count); }
        }
        public static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        public static void Download(AppRelease release, string zip)
        {
            Parse(new JavaScriptSerializer().Serialize(release));
            using (var response = Response(release.url))
            using (var input = response.GetResponseStream())
            using (var output = new FileStream(zip, FileMode.CreateNew)) CopyBounded(input, output, release.size);
            Verify(release, zip);
        }
        public static void Verify(AppRelease release, string zip)
        {
            if (new FileInfo(zip).Length != release.size || !String.Equals(Hash(zip), release.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("앱 업데이트 파일의 무결성 검증에 실패했습니다. 설치하지 않습니다.");
        }
        public static void Extract(AppRelease release, string zip, string directory)
        {
            Verify(release, zip);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
            using (var archive = ZipFile.OpenRead(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    string name = entry.FullName;
                    if (!Files.Contains(name, StringComparer.Ordinal) || !names.Add(name) || entry.Length <= 0 ||
                        (total += entry.Length) > 250 * 1024 * 1024)
                        throw new InvalidDataException("앱 업데이트 ZIP에 허용되지 않은 파일이 있습니다: " + name);
                }
                if (names.Count != Files.Length) throw new InvalidDataException("앱 업데이트 ZIP의 필수 파일이 부족합니다.");
                foreach (var entry in archive.Entries)
                {
                    string path = Path.Combine(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = new FileStream(path, FileMode.CreateNew)) CopyBounded(input, output, entry.Length);
                }
            }
            var actual = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(directory, "CampaignHangar.exe")).FileVersion;
            if (new Version(actual) != new Version(release.version))
                throw new InvalidDataException("다운로드한 앱 버전과 배포 정보가 다릅니다.");
            string product = System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(directory, "CampaignHangar.exe")).ProductName;
            if (product != "CampaignHangar." + AppChannel.Id)
                throw new InvalidDataException("다운로드한 앱의 개발판·배포판 구분이 다릅니다.");
        }
        public static void Replace(string source, string target, string backup, Action<int> afterCopy = null)
        {
            target = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
            if (target == Path.GetPathRoot(target).TrimEnd(Path.DirectorySeparatorChar) ||
                !File.Exists(Path.Combine(target, "CampaignHangar.exe")) || Directory.Exists(backup))
                throw new InvalidDataException("앱 설치 위치 또는 백업 위치를 확인하세요.");
            foreach (var name in Files)
            {
                if (!File.Exists(Path.Combine(source, name))) throw new FileNotFoundException("업데이트 파일이 없습니다.", name);
                string path = Path.Combine(target, name);
                for (string parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent))
                    if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("연결 폴더에는 앱 업데이트를 설치하지 않습니다.");
                if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("연결 파일에는 앱 업데이트를 설치하지 않습니다.");
            }
            Directory.CreateDirectory(backup);
            var changed = new List<string>();
            // Complete the backup before changing any installed file.
            foreach (var name in Files)
            {
                string old = Path.Combine(target, name), saved = Path.Combine(backup, name);
                if (!File.Exists(old)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(saved)); File.Copy(old, saved, false);
            }
            try
            {
                foreach (var name in Files)
                {
                    string path = Path.Combine(target, name); Directory.CreateDirectory(Path.GetDirectoryName(path));
                    changed.Add(name); File.Copy(Path.Combine(source, name), path, true);
                    if (afterCopy != null) afterCopy(changed.Count);
                }
            }
            catch
            {
                foreach (var name in changed.AsEnumerable().Reverse())
                {
                    string saved = Path.Combine(backup, name), path = Path.Combine(target, name);
                    if (File.Exists(saved)) File.Copy(saved, path, true); else File.Delete(path);
                }
                throw;
            }
        }
    }
}
