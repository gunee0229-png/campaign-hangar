using System;
using System.IO;
using System.Linq;
using System.IO.Compression;
using System.Web.Script.Serialization;

namespace CampaignHangar
{
    static class AppUpdateChecks
    {
        static void Assert(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
        static void Reject(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Assert(rejected, name);
        }
        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "hangar-app-update-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var release = new AppRelease { version = "0.3.0.0", channel = AppChannel.Id, url = AppUpdate.PackageUrl("0.3.0.0"), sha256 = new string('a',64), size = 100 };
                var json = new JavaScriptSerializer();
                Assert(AppUpdate.Manifest == AppUpdate.Origin + (AppChannel.IsDevelopment
                    ? AppChannel.ReleasePath+"latest.json" : "/gunee0229-png/campaign-hangar/releases/latest/download/latest.json"), "update manifest is isolated by channel");
                Assert(AppUpdate.IsNewer(AppUpdate.Parse(json.Serialize(release))), "newer app release");
                release.channel = AppChannel.IsDevelopment ? "release" : "development";
                Reject(() => AppUpdate.Parse(json.Serialize(release)), "other update channel rejected");
                release.channel = AppChannel.Id;
                release.url = AppUpdate.Origin + (AppChannel.IsDevelopment ? "/gunee0229-png/campaign-hangar/releases/download/v0.3.0.0/" : "/gunee0229-png/campaign-hangar/releases/download/development/") + "wrong-channel.zip";
                Reject(() => AppUpdate.Parse(json.Serialize(release)), "other channel URL rejected even with a matching channel label");
                release.url = AppUpdate.PackageUrl("0.3.0.0");
                release.version = AppUpdate.Current.ToString();
                release.url = AppUpdate.PackageUrl(release.version);
                Assert(!AppUpdate.IsNewer(AppUpdate.Parse(json.Serialize(release))), "same version does not notify");
                release.version = "0.0.1.0";
                release.url = AppUpdate.PackageUrl(release.version);
                Assert(!AppUpdate.IsNewer(AppUpdate.Parse(json.Serialize(release))), "older release does not downgrade");
                foreach (string url in new[] { "http://dc-html-workshop.ddaheuk.workers.dev/app/releases/a.zip", "https://evil.example/app/releases/a.zip", AppUpdate.Origin + "/wrong.zip", AppUpdate.Origin + "/app/releases/a.zip?q=1" })
                { release.url = url; Reject(() => AppUpdate.Parse(json.Serialize(release)), "untrusted release URL rejected"); }
                release.url = AppUpdate.PackageUrl(release.version);
                release.sha256 = "bad"; Reject(() => AppUpdate.Parse(json.Serialize(release)), "invalid hash rejected");
                using (var input = new MemoryStream(new byte[20]))
                using (var output = new MemoryStream()) Reject(() => AppUpdate.CopyBounded(input, output, 10), "oversized download rejected");
                string zip = Path.Combine(root, "invalid.zip");
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var writer = new StreamWriter(archive.CreateEntry("../game-file").Open())) writer.Write("no");
                release.size = new FileInfo(zip).Length; release.sha256 = AppUpdate.Hash(zip);
                Reject(() => AppUpdate.Extract(release, zip, Path.Combine(root,"staged")), "path traversal rejected before extraction");
                release.sha256 = new string('b',64);
                Reject(() => AppUpdate.Verify(release,zip), "tampered package rejected");
                string completeZip = Path.Combine(root, "complete.zip");
                string updater = System.Reflection.Assembly.GetExecutingAssembly().Location;
                using (var archive = ZipFile.Open(completeZip, ZipArchiveMode.Create))
                {
                    foreach (string name in AppUpdate.Files)
                        if (name == "CampaignHangar.exe") archive.CreateEntryFromFile(updater, name);
                        else using (var writer = new StreamWriter(archive.CreateEntry(name).Open())) writer.Write("fixture:"+name);
                }
                release.version = AppUpdate.Current.ToString(); release.size = new FileInfo(completeZip).Length; release.sha256 = AppUpdate.Hash(completeZip);
                string complete = Path.Combine(root, "complete");
                AppUpdate.Extract(release, completeZip, complete);
                Assert(AppUpdate.Files.All(n=>File.Exists(Path.Combine(complete,n))), "valid package extracts every required file");
                release.version = "99.0.0.0";
                Reject(() => AppUpdate.Extract(release,completeZip,Path.Combine(root,"mismatch")), "manifest and executable version mismatch rejected");
                string source = Path.Combine(root,"new"), target = Path.Combine(root,"app");
                foreach (string name in AppUpdate.Files)
                {
                    foreach (string directory in new[] { source,target }) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(directory,name)));
                    File.WriteAllText(Path.Combine(source,name),"new:"+name); File.WriteAllText(Path.Combine(target,name),"old:"+name);
                }
                string sentinel = Path.Combine(target,"campaign-user-data"); File.WriteAllText(sentinel,"preserve");
                Reject(() => AppUpdate.Replace(source,target,Path.Combine(root,"rollback"), i=>{if(i==3)throw new IOException("injected failure");}), "copy failure reported");
                Assert(AppUpdate.Files.All(n=>File.ReadAllText(Path.Combine(target,n))=="old:"+n),"all changed app files restored");
                AppUpdate.Replace(source,target,Path.Combine(root,"backup"));
                Assert(AppUpdate.Files.All(n=>File.ReadAllText(Path.Combine(target,n))=="new:"+n),"complete app replacement");
                Assert(AppUpdate.Files.All(n=>File.ReadAllText(Path.Combine(root,"backup",n))=="old:"+n),"previous app remains recoverable");
                Assert(File.ReadAllText(sentinel)=="preserve","unrelated user data untouched");
                Console.WriteLine("Isolated fixtures: "+root); return 0;
            }
            catch(Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
