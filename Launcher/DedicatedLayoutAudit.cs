using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CampaignHangar
{
    // Independently reviewed game paths, NOT a copy of ZIP directory names.
    // Every new published dedicated campaign should have an authored contract
    // before its release ZIP is admitted to the public catalogue.
    public sealed class DedicatedLayoutContract
    {
        public int schema { get; set; }
        public string id { get; set; }
        public string manifestSha256 { get; set; }
        public List<string> gameFiles { get; set; }
        public List<string> launchMaps { get; set; }
        public List<DedicatedSiblingRequirement> siblingRequirements { get; set; }
    }

    public sealed class DedicatedSiblingRequirement
    {
        public string launcher { get; set; }
        public List<string> files { get; set; }
    }

    // Reusable real-ZIP release gate. It runs the production native installer,
    // launch-path resolution, reinstall and uninstall against a disposable fake
    // game root. An author-provided contract is essential: the manifest alone
    // cannot know which runtime paths a map's Galaxy triggers expect.
    public static class DedicatedLayoutAudit
    {
        static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;

        static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidDataException("전용 캠페인 등록 검사 실패: " + detail);
            Console.WriteLine("LAYOUT PASS: " + detail);
        }

        static bool SameFiles(IEnumerable<string> actual, IEnumerable<string> expected)
        {
            var a = actual.ToArray();
            var e = expected.ToArray();
            return a.Length == e.Length && a.Distinct(Paths).Count() == a.Length &&
                e.Distinct(Paths).Count() == e.Length && new HashSet<string>(a, Paths).SetEquals(e);
        }

        public static DedicatedRelease Preflight(DedicatedLayoutContract contract, DedicatedRelease signed,
            string actualManifestHash)
        {
            Require(contract != null && contract.schema == 1 &&
                contract.id != null && contract.manifestSha256 != null &&
                contract.gameFiles != null && contract.gameFiles.Count > 0 &&
                contract.launchMaps != null && contract.launchMaps.Count > 0,
                "독립 설치 계약 형식과 필수 항목");
            DedicatedLibrary.ValidateRelease(signed);
            Require(signed.id == contract.id && contract.manifestSha256 == actualManifestHash,
                "실제 원본 매니페스트 SHA-256, 캠페인 ID 고정");
            var installed = DedicatedLibrary.NormalizeOdysseyLayout(signed);
            Require(SameFiles(installed.files.Select(f => f.path), contract.gameFiles),
                "압축 폴더가 아닌 계약에 선언된 모든 게임 설치 경로 일치");
            Require(installed.launches.Count == contract.launchMaps.Count &&
                installed.launches.Select((launch, i) => String.Equals(launch.path, contract.launchMaps[i], StringComparison.OrdinalIgnoreCase)).All(x => x),
                "에피소드별 시작 맵 경로·순서 일치");
            foreach (var sibling in contract.siblingRequirements ?? new List<DedicatedSiblingRequirement>())
            {
                Require(sibling != null && sibling.launcher != null && sibling.files != null &&
                    installed.launches.Any(l => String.Equals(l.path, sibling.launcher, StringComparison.OrdinalIgnoreCase)),
                    "실행 맵에 연결된 필수 동위 맵 계약");
                string folder = sibling.launcher.Substring(0, sibling.launcher.LastIndexOf('/') + 1);
                Require(sibling.files.All(path => path != null && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".SC2Map", StringComparison.OrdinalIgnoreCase) &&
                    installed.files.Any(f => String.Equals(path, f.path, StringComparison.OrdinalIgnoreCase))),
                    "시작 맵이 요구하는 임무들은 동일한 게임 폴더에 설치");
            }
            return installed;
        }

        public static void Run(string contractPath, string manifestPath, string zipDirectory, string root)
        {
            if (Directory.Exists(root))
                throw new InvalidOperationException("설치 검사에는 비어 있는 새 격리 폴더를 지정해야 합니다.");
            // Reject a mistakenly supplied test root under a real SC2 install
            // or an existing campaign store before any directory is created.
            for (var ancestor = new DirectoryInfo(Path.GetFullPath(root)).Parent;
                ancestor != null; ancestor = ancestor.Parent)
                if (File.Exists(Path.Combine(ancestor.FullName, "StarCraft II.exe")) ||
                    File.Exists(Path.Combine(ancestor.FullName, "dedicated", "installed.json")))
                    throw new InvalidOperationException("실제 게임 또는 기존 캠페인 보관소 안에는 격리 테스트를 만들 수 없습니다.");
            if (!File.Exists(contractPath) || !File.Exists(manifestPath) || !Directory.Exists(zipDirectory))
                throw new FileNotFoundException("설치 계약, 실제 설치 목록 또는 실제 ZIP 폴더가 없습니다.");
            var contract = Data.Read<DedicatedLayoutContract>(contractPath);
            var manifest = Data.Read<DedicatedRelease>(manifestPath);
            string manifestHash = Data.Hash(manifestPath);
            var expected = Preflight(contract, manifest, manifestHash);
            var archives = new List<string>();
            foreach (var package in manifest.packages)
            {
                string name = Uri.UnescapeDataString(new Uri(package.url).Segments.Last());
                string archive = Path.Combine(zipDirectory, name);
                Require(Path.GetFileName(archive) == name && File.Exists(archive), "원본 ZIP 확인: " + name);
                Require(new FileInfo(archive).Length == package.bytes && Data.Hash(archive) == package.sha256,
                    "원본 ZIP SHA-256·크기 일치: " + name);
                archives.Add(archive);
            }

            Directory.CreateDirectory(root);
            string game = Path.Combine(root, "game");
            string storage = Path.Combine(root, "storage");
            Directory.CreateDirectory(Path.Combine(game, "Support64"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-only-not-executable");
            File.WriteAllText(Path.Combine(game, "Support64", "SC2Switcher_x64.exe"), "fixture-only-not-executable");
            Directory.CreateDirectory(Path.Combine(game, "Maps"));
            Directory.CreateDirectory(Path.Combine(game, "Mods"));
            File.WriteAllText(Path.Combine(game, "Maps", "user-note.keep"), "preserve-foreign-maps");
            File.WriteAllText(Path.Combine(game, "Mods", "user-note.keep"), "preserve-foreign-mods");
            File.WriteAllText(Path.Combine(root, "preserved.SC2Bank"), "original-bank");
            File.WriteAllText(Path.Combine(root, "preserved.SC2Save"), "original-save");
            var library = new DedicatedLibrary(storage);
            var card = new Campaign { id = contract.id, kind = "dedicated" };
            Action verify = () => {
                var saved = library.Installed(contract.id);
                Require(saved != null && !library.NeedsRepair(card) &&
                    SameFiles(saved.release.files.Select(f => f.path), contract.gameFiles) &&
                    expected.files.All(file => {
                        string path = Data.Under(game, file.path);
                        return File.Exists(path) && new FileInfo(path).Length == file.bytes &&
                            Data.Hash(path) == file.sha256;
                    }), "실제 설치 결과 모든 파일의 경로·SHA-256·용량 일치");
                for (int i = 0; i < contract.launchMaps.Count; i++)
                {
                    var start = library.LaunchInfo(card, game, i + 1);
                    Require(start.FileName == Path.Combine(game, "Support64", "SC2Switcher_x64.exe") &&
                        start.Arguments == "\"" + Data.Under(game, contract.launchMaps[i]) + "\"" &&
                        File.Exists(Data.Under(game, contract.launchMaps[i])) && !start.UseShellExecute,
                        "실제 런처 선택 " + (i + 1) + "번은 선언된 시작 맵 실행 경로");
                }
            };
            library.InstallArchives(manifest, manifestHash, archives, game, null, CancellationToken.None);
            verify();
            var firstState = Data.Json.Serialize(library.Current());
            library.InstallArchives(manifest, manifestHash, archives, game, null, CancellationToken.None);
            Require(firstState == Data.Json.Serialize(library.Current()), "동일 배포 ZIP 재설치 후 기록 변화 없음");
            verify();
            library.Uninstall(contract.id, game);
            Require(library.Current().installed.Count == 0 &&
                expected.files.All(file => !File.Exists(Data.Under(game, file.path))) &&
                File.ReadAllText(Path.Combine(game, "Maps", "user-note.keep")) == "preserve-foreign-maps" &&
                File.ReadAllText(Path.Combine(game, "Mods", "user-note.keep")) == "preserve-foreign-mods" &&
                File.ReadAllText(Path.Combine(root, "preserved.SC2Bank")) == "original-bank" &&
                File.ReadAllText(Path.Combine(root, "preserved.SC2Save")) == "original-save",
                "설치 제거 후 다른 파일·Bank·Save 원형 보존");
            Data.Write(Path.Combine(root, "layout-audit-result.json"), new {
                id = contract.id, manifestSha256 = manifestHash, zipCount = archives.Count,
                gameFileCount = expected.files.Count, launchCount = expected.launches.Count,
                installed = true, reinstalled = true, uninstalled = true, hashesChecked = true,
                runtimeTriggersVerified = false,
                status = "PASS_STATIC_ISOLATED_NOT_INGAME"
            });
            Console.WriteLine("LAYOUT AUDIT PASS: " + contract.id + " / " + expected.files.Count +
                " installed files / " + expected.launches.Count +
                " launch choices; runtime trigger paths and in-game mission loading remain unverified.");
        }
    }
}
