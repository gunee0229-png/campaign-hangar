using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Xml;

namespace CampaignHangar
{
    public sealed class PlayRecord
    {
        public string id { get; set; }
        public double seconds { get; set; }
        public int missions { get; set; }
        public int clears { get; set; }
        public string bankPath { get; set; }
        public string lastMission { get; set; }
        public int lastInstance { get; set; }
        public bool initialized { get; set; }
        public bool needsReview { get; set; }
        public string bankError { get; set; }
        public List<string> seenMissions { get; set; }
        public List<string> completed { get; set; }
        public PlayRecord() { seenMissions = new List<string>(); completed = new List<string>(); }
    }

    public sealed class BankProgress
    {
        public string mission;
        public bool success;
        public bool simulation;
        public int instance;
        public List<string> completed = new List<string>();
        public static BankProgress Read(string path)
        {
            if (!String.Equals(Path.GetFileName(path), "RLO8D26WCampaign.SC2Bank", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("현재 자동 판독은 레볼라이트의 RLO8D26WCampaign.SC2Bank만 지원합니다.");
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = 4 * 1024 * 1024 };
            var document = new XmlDocument { XmlResolver = null };
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = XmlReader.Create(stream, settings)) document.Load(reader);
            if (document.DocumentElement == null || document.DocumentElement.Name != "Bank")
                throw new InvalidDataException("스타2 뱅크 형식이 아닙니다.");
            var result = new BankProgress();
            bool lastInfo = false, instanceFound = false;
            foreach (XmlNode section in document.SelectNodes("/Bank/Section"))
            {
                string name = Attribute(section, "name");
                if (name == "WCampaign|CampaignLastInfo") lastInfo = true;
                foreach (XmlNode key in section.SelectNodes("Key"))
                {
                    string keyName = Attribute(key, "name");
                    var value = key.SelectSingleNode("Value");
                    if (value == null) continue;
                    if (name == "WCampaign|CampaignLastInfo")
                    {
                        if (keyName == "LastMission") result.mission = Attribute(value, "string");
                        if (keyName == "LastMissionSuccess") result.success = Attribute(value, "flag") == "1";
                        if (keyName == "LastMissionSimulation") result.simulation = Attribute(value, "flag") == "1";
                    }
                    if (name == "WCampaign|CampaignSetting" && keyName == "MapInstanceID")
                    {
                        instanceFound = Int32.TryParse(Attribute(value, "int"), out result.instance) && result.instance >= 0;
                    }
                    if (name == "WCampaign|MissionCompletedAge" && Number(value) > 0 && !String.IsNullOrEmpty(keyName))
                        result.completed.Add(keyName);
                }
            }
            if (!lastInfo || !instanceFound) throw new InvalidDataException("캠페인 진행 정보가 없거나 불완전한 뱅크입니다.");
            result.completed = result.completed.Distinct(StringComparer.Ordinal).OrderBy(s => s).ToList();
            return result;
        }
        static string Attribute(XmlNode node, string name) { return node.Attributes[name] == null ? null : node.Attributes[name].Value; }
        static int Number(XmlNode node) { int value; return Int32.TryParse(Attribute(node, "int"), out value) ? value : 0; }
    }

    public sealed class ActivityStore
    {
        readonly string file;
        readonly object sync = new object();
        readonly Dictionary<string, PlayRecord> records;
        string previousId, previousProcess;
        double previousClock, pending;
        bool hasClock;
        public sealed class StartupCanceledException : OperationCanceledException
        {
            public StartupCanceledException() : base("플레이 기록 복구·제외를 취소했습니다. 원본 기록은 그대로 보존했습니다.") { }
        }
        static Dictionary<string, PlayRecord> LoadRecords(string path)
        {
            var entries = Data.Read<List<PlayRecord>>(path);
            if (entries == null) throw new InvalidDataException("플레이 기록 목록이 비어 있습니다.");
            var loaded = new Dictionary<string, PlayRecord>(StringComparer.Ordinal);
            foreach (var record in entries)
            {
                if (record == null || String.IsNullOrWhiteSpace(record.id) ||
                    Double.IsNaN(record.seconds) || Double.IsInfinity(record.seconds) || record.seconds < 0 ||
                    record.missions < 0 || record.clears < 0 || record.lastInstance < 0 ||
                    loaded.ContainsKey(record.id))
                    throw new InvalidDataException("플레이 기록에 비어 있거나 중복된 캠페인, 잘못된 누적 수치가 있습니다.");
                // Older records may not contain either collection. Keep their
                // other fields instead of discarding a valid legacy record.
                if (record.seenMissions == null) record.seenMissions = new List<string>();
                if (record.completed == null) record.completed = new List<string>();
                loaded.Add(record.id, record);
            }
            return loaded;
        }
        static bool CorruptRecordData(Exception error)
        {
            // Access denied, sharing errors, missing storage and disk IO are
            // NOT corruption: never offer to discard records for those.
            return error is InvalidDataException || error is ArgumentException ||
                error is FormatException || error is InvalidOperationException;
        }
        static string DamagedRecordPath(string file)
        {
            return Path.Combine(Path.GetDirectoryName(file), "play-records.damaged-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".json");
        }
        public static ActivityStore OpenForStartup(string root, Func<string, bool> confirmExclude, Action<string> notify)
        {
            string primary = Path.Combine(root, "play-records.json");
            try { return new ActivityStore(root); }
            catch (Exception error)
            {
                if (!CorruptRecordData(error) || !File.Exists(primary)) throw;
                string backup = primary + ".last-good";
                if (File.Exists(backup))
                {
                    try
                    {
                        var good = LoadRecords(backup);
                        string preserved = DamagedRecordPath(primary);
                        File.Copy(primary, preserved, false);
                        Data.Write(primary, good.Values.OrderBy(r => r.id).ToList());
                        var recovered = new ActivityStore(root);
                        if (notify != null) notify("플레이 기록 파일이 손상되어 마지막 정상 사본에서 복구했습니다.\n\n" +
                            "원본 손상 파일은 삭제하지 않고 다음 위치에 보관했습니다:\n" + preserved);
                        return recovered;
                    }
                    catch (Exception restoreError)
                    {
                        System.Diagnostics.Trace.WriteLine("Play records last-good recovery unavailable: " + restoreError.Message);
                        // Ask before excluding, rather than silently starting
                        // with no completion/playtime history.
                    }
                }
                string question = "플레이 기록 파일을 읽지 못했고 정상 사본으로도 복구하지 못했습니다.\n\n" +
                    "손상된 기록을 안전한 다른 이름으로 보존하고, 이번에는 기록 없이 시작할까요?\n" +
                    "기존 게임·뱅크·세이브·설치 목록은 변경하지 않습니다.\n\n" +
                    "예: 손상 기록을 제외하고 시작\n아니오: 원본을 유지하고 종료";
                if (confirmExclude == null || !confirmExclude(question)) throw new StartupCanceledException();
                string excluded = DamagedRecordPath(primary);
                File.Move(primary, excluded);
                var empty = new ActivityStore(root);
                if (notify != null) notify("손상된 플레이 기록을 제외하고 시작합니다.\n\n" +
                    "기록 원본은 삭제하지 않고 보존했습니다:\n" + excluded);
                return empty;
            }
        }
        public ActivityStore(string root)
        {
            file = Path.Combine(root, "play-records.json");
            records = File.Exists(file) ? LoadRecords(file) : new Dictionary<string, PlayRecord>(StringComparer.Ordinal);
        }
        PlayRecord Item(string id)
        {
            PlayRecord value;
            if (!records.TryGetValue(id, out value)) { value = new PlayRecord { id = id }; records.Add(id, value); }
            return value;
        }
        void Save()
        {
            var snapshot = records.Values.OrderBy(r => r.id).ToList();
            Data.Write(file, snapshot);
            pending = 0;
            // A separate atomic last-good copy makes recovery possible even
            // when the primary JSON is later truncated or externally damaged.
            try { Data.Write(file + ".last-good", snapshot); }
            catch (IOException error) { System.Diagnostics.Trace.WriteLine("Play records backup: " + error.Message); }
            catch (UnauthorizedAccessException error) { System.Diagnostics.Trace.WriteLine("Play records backup: " + error.Message); }
        }
        public PlayRecord Snapshot(string id)
        {
            lock (sync)
            {
                var value = Item(id);
                return new PlayRecord { id = value.id, seconds = value.seconds, missions = value.missions, clears = value.clears,
                    bankPath = value.bankPath, lastMission = value.lastMission, lastInstance = value.lastInstance,
                    initialized = value.initialized, needsReview = value.needsReview, bankError = value.bankError,
                    seenMissions = value.seenMissions.ToList(), completed = value.completed.ToList() };
            }
        }
        public string Summary(string id)
        {
            var r = Snapshot(id);
            string time = "플레이 " + TimeSpan.FromSeconds(r.seconds).TotalHours.ToString("0.0") + "시간";
            return r.bankPath == null ? time + " · 완료 기록 미연결" : time + " · 임무 " +
                r.missions + " · 회차 " + r.clears + " (연결 이후 관측)";
        }
        public void ObserveTime(string id, string process, double clock)
        {
            lock (sync)
            {
                if (hasClock && id != null && process != null && id == previousId && process == previousProcess && clock >= previousClock)
                {
                    double elapsed = clock - previousClock;
                    Item(id).seconds += elapsed; pending += elapsed;
                }
                bool ended = hasClock && previousId != null && (id != previousId || process != previousProcess);
                previousId = id; previousProcess = process; previousClock = clock; hasClock = true;
                if (pending >= 60 || (ended && pending > 0)) Save();
            }
        }
        public void Flush() { lock (sync) { if (pending > 0) Save(); } }
        public double TotalSeconds { get { lock(sync) { return records.Values.Sum(r=>r.seconds); } } }
        public int ClearedCampaigns { get { lock(sync) { return records.Values.Count(r=>r.clears>0); } } }
        static void Baseline(PlayRecord record, BankProgress progress)
        {
            record.lastMission = progress.mission; record.lastInstance = progress.instance;
            record.completed = progress.completed.ToList();
            record.seenMissions = progress.completed.ToList();
            if (progress.success && !progress.simulation && !String.IsNullOrEmpty(progress.mission) && !record.seenMissions.Contains(progress.mission))
                record.seenMissions.Add(progress.mission);
            record.initialized = true; record.needsReview = false; record.bankError = null;
        }
        public void LinkBank(string id, string path)
        {
            path = Path.GetFullPath(path);
            if (!String.Equals(Path.GetExtension(path), ".SC2Bank", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SC2Bank 파일을 선택해 주세요.");
            if (!File.Exists(path)) throw new FileNotFoundException("뱅크 파일을 찾을 수 없습니다.", path);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("연결 파일은 지원하지 않습니다.");
            var progress = BankProgress.Read(path);
            lock (sync)
            {
                var record = Item(id); record.bankPath = path; Baseline(record, progress); Save();
            }
        }
        public void ConfirmNewRun(string id)
        {
            lock (sync)
            {
                var record = Item(id);
                if (String.IsNullOrEmpty(record.bankPath)) throw new InvalidOperationException("뱅크를 먼저 연결해 주세요.");
                Baseline(record, BankProgress.Read(record.bankPath)); Save();
            }
        }
        public void ObserveBank(string id, BankProgress progress)
        {
            lock (sync)
            {
                var record = Item(id);
                if (!record.initialized) { Baseline(record, progress); Save(); return; }
                if (record.needsReview) return;
                if (progress.instance < record.lastInstance || record.completed.Except(progress.completed).Any())
                {
                    record.needsReview = true; record.bankError = "새 회차 또는 이전 세이브가 감지됐습니다. 새 회차라면 기록 창에서 확인해 주세요.";
                    Save(); return;
                }
                bool changed = record.bankError != null;
                record.bankError = null;
                bool transition = progress.instance > record.lastInstance || progress.mission != record.lastMission;
                if (transition && progress.instance > 0 && progress.success && !progress.simulation && !String.IsNullOrEmpty(progress.mission) &&
                    !record.seenMissions.Contains(progress.mission))
                {
                    record.seenMissions.Add(progress.mission); record.missions++; changed = true;
                    if (progress.mission == "TValerian03") record.clears++;
                }
                if (record.lastInstance != progress.instance || record.lastMission != progress.mission ||
                    !record.completed.SequenceEqual(progress.completed)) changed = true;
                record.lastInstance = progress.instance; record.lastMission = progress.mission; record.completed = progress.completed.ToList();
                if (changed) Save();
            }
        }
        public void PollBank(string id)
        {
            string path;
            lock (sync) { path = Item(id).bankPath; }
            if (String.IsNullOrEmpty(path)) return;
            try { ObserveBank(id, BankProgress.Read(path)); }
            catch (Exception error)
            {
                if (!(error is IOException) && !(error is XmlException) && !(error is UnauthorizedAccessException) && !(error is InvalidDataException)) throw;
                lock (sync)
                {
                    var record = Item(id);
                    if (record.bankError != error.Message) { record.bankError = error.Message; Save(); }
                }
            }
        }
    }

    public sealed class ExternalCampaigns
    {
        readonly string root, file;
        string IdentityFile {get {return Path.Combine(root,"external-identities.json");}}
        static string Identity(string name,string launchMode,string destination,string[] episodes)
        {
            string paths=launchMode=="episodes"?String.Join("|",(episodes??new string[0]).Select(p=>
                String.IsNullOrWhiteSpace(p)?"":Path.GetFullPath(p).ToUpperInvariant())):destination.ToUpperInvariant();
            return name.Trim()+"|"+launchMode+"|"+paths;
        }
        public ExternalCampaigns(string root) { this.root = root; file = Path.Combine(root, "external-campaigns.json"); }
        public List<Campaign> All() { return File.Exists(file) ? Data.Read<List<Campaign>>(file) : new List<Campaign>(); }
        public static IEnumerable<Campaign> VisibleForChannel(IEnumerable<Campaign> records, bool isDevelopment)
        {
            return records.Where(record => isDevelopment || !record.developmentOnly);
        }
        public static string InstallationMode(Campaign item) { return item.installationMode ?? "manual"; }
        public static string LaunchMode(Campaign item) { return item.launchMode ?? "map"; }
        public static bool UsesAuthorLauncher(Campaign item)
        {
            return item.kind == "external" && (InstallationMode(item) == "author-launcher" || LaunchMode(item) == "author-launcher");
        }
        public static string InstallationName(Campaign item)
        { return InstallationMode(item) == "author-launcher" ? "제작자 런처" : "수동 설치"; }
        public static string LaunchName(Campaign item)
        { return LaunchMode(item) == "episodes" ? "에피소드별 시작 맵" : LaunchMode(item) == "author-launcher" ? "제작자 런처" : "시작 맵"; }
        public static void ValidateConnection(string installationMode, string launchMode, string map, string executable, string gameRoot,
            string[] episodeMaps = null)
        {
            if (installationMode != "manual" && installationMode != "author-launcher")
                throw new InvalidDataException("지원하지 않는 설치 방식입니다.");
            if (launchMode != "map" && launchMode != "author-launcher" && launchMode != "episodes")
                throw new InvalidDataException("지원하지 않는 실행 방식입니다.");
            if (launchMode == "map") ValidateMap(map, gameRoot);
            if (launchMode == "episodes")
            {
                if (episodeMaps == null || episodeMaps.Length != 3) throw new InvalidDataException("에피소드 1·2·3의 연결 정보를 확인해 주세요.");
                foreach (string path in episodeMaps.Where(p => !String.IsNullOrWhiteSpace(p))) ValidateMap(path, gameRoot);
                var connected = episodeMaps.Where(p => !String.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToList();
                if (connected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != connected.Count)
                    throw new InvalidDataException("에피소드마다 다른 시작 맵을 연결해 주세요. 공통 선택 맵을 세 버튼에 중복 연결할 수 없습니다.");
            }
            if (installationMode == "author-launcher" || launchMode == "author-launcher") ValidateLauncher(executable);
        }
        public Campaign Save(string id, string name, string map, string image, string gameRoot,
            string installationMode = "manual", string launchMode = "map", string executable = null, string[] episodeMaps = null,
            string standaloneZipHash = null)
        {
            ValidateConnection(installationMode, launchMode, map, executable, gameRoot, episodeMaps);
            if (String.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidDataException("캠페인 이름은 1~120자로 입력해 주세요.");
            var items = All();
            string destination = launchMode == "episodes" ? null : Path.GetFullPath(launchMode == "map" ? map : executable);
            if (launchMode != "episodes" && items.Any(c => c.id != id && LaunchMode(c) == launchMode &&
                String.Equals(launchMode == "map" ? c.launcherMap : c.launcherExecutable, destination, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("이미 연결한 실행 대상입니다. 관리에서 변경해 주세요.");
            Campaign item;
            if (id == null)
            {
                var identities=File.Exists(IdentityFile)?Data.Read<Dictionary<string,string>>(IdentityFile):new Dictionary<string,string>();
                string oldId;
                identities.TryGetValue(Identity(name,launchMode,destination,episodeMaps),out oldId);
                if(oldId!=null&&!System.Text.RegularExpressions.Regex.IsMatch(oldId,"^external-[0-9a-f]{32}$"))
                    throw new InvalidDataException("저장된 외부 캠페인 식별자가 올바르지 않습니다.");
                if(oldId!=null&&items.Any(c=>c.id==oldId))
                    throw new InvalidOperationException("기존 기록에 연결된 캠페인이 있습니다. 해당 항목의 연결 설정을 변경해 주세요.");
                item = new Campaign { id = oldId??"external-" + Guid.NewGuid().ToString("N"), kind = "external", slot = "external",
                    version = "전용 실행", sourceAuthor = null, developmentOnly = AppChannel.IsDevelopment,
                    files = new List<OwnedFile>() };
                item.versionKey = item.id; items.Add(item);
            }
            else item = items.Single(c => c.id == id);
            string cover = item.thumbnail;
            if (!String.IsNullOrWhiteSpace(image) && !String.Equals(image, cover, StringComparison.OrdinalIgnoreCase))
            {
                using (var bitmap = Image.FromFile(image))
                {
                    string path = Data.Under(root, "external-covers\\" + item.id + "-" + Guid.NewGuid().ToString("N") + ".png");
                    Directory.CreateDirectory(Path.GetDirectoryName(path)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                    cover = path.Substring(root.TrimEnd('\\').Length + 1);
                }
            }
            item.name = name.Trim(); item.launcherMap = launchMode == "map" ? destination : null; item.thumbnail = cover;
            item.installationMode = installationMode; item.launchMode = launchMode;
            item.launcherExecutable = UsesAuthorLauncher(item) ? Path.GetFullPath(executable) : null;
            item.episodeMaps = launchMode == "episodes" ? episodeMaps.Select(p => String.IsNullOrWhiteSpace(p) ? null : Path.GetFullPath(p)).ToArray() : null;
            item.standaloneZipHash = standaloneZipHash;
            Data.Write(file, items); return item;
        }
        public void Forget(string id) {
            var items=All();var item=items.SingleOrDefault(c=>c.id==id);
            if(item!=null) {
                var identities=File.Exists(IdentityFile)?Data.Read<Dictionary<string,string>>(IdentityFile):new Dictionary<string,string>();
                string mode=LaunchMode(item),destination=mode=="map"?item.launcherMap:item.launcherExecutable;
                identities[Identity(item.name,mode,destination,item.episodeMaps)]=item.id;
                Data.Write(IdentityFile,identities);
            }
            Data.Write(file,items.Where(c=>c.id!=id).ToList());
        }
        public static void ValidateMap(string map, string gameRoot)
        {
            if (String.IsNullOrWhiteSpace(gameRoot) || !File.Exists(Path.Combine(gameRoot, "StarCraft II.exe")))
                throw new InvalidOperationException("설정에서 스타2 게임 경로를 먼저 선택해 주세요.");
            string maps = Path.Combine(Path.GetFullPath(gameRoot), "Maps");
            string full = Path.GetFullPath(map);
            if (!full.StartsWith(maps.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetExtension(full), ".SC2Map", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("게임 Maps 폴더에 설치된 캠페인의 시작 SC2Map 파일을 선택해 주세요.");
            string relative = full.Substring(maps.TrimEnd('\\').Length + 1);
            full = Data.Under(maps, relative);
            using (var stream = File.OpenRead(full))
            {
                if (stream.ReadByte() != 'M' || stream.ReadByte() != 'P' || stream.ReadByte() != 'Q' || stream.ReadByte() != 26)
                    throw new InvalidDataException("MPQ 형식의 SC2Map 파일이 아닙니다.");
            }
        }
        public static ProcessStartInfo LaunchInfo(Campaign item, string gameRoot, int episode = 0)
        {
            if (item.kind != "external") throw new InvalidOperationException("전용 캠페인이 아닙니다.");
            string map = item.launcherMap;
            if (LaunchMode(item) == "episodes")
            {
                if (episode < 1 || episode > 3 || item.episodeMaps == null || item.episodeMaps.Length != 3)
                    throw new InvalidOperationException("실행할 에피소드 1·2·3을 선택해 주세요.");
                map = item.episodeMaps[episode - 1];
                if (String.IsNullOrWhiteSpace(map)) throw new InvalidOperationException("이 에피소드의 시작 맵이 연결되지 않았습니다. 관리에서 설치된 맵을 연결해 주세요.");
            }
            else if (episode != 0) throw new InvalidOperationException("에피소드별 실행 캠페인이 아닙니다.");
            if (LaunchMode(item) == "author-launcher") return AuthorLauncherInfo(item);
            if (LaunchMode(item) != "map" && LaunchMode(item) != "episodes") throw new InvalidOperationException("지원하지 않는 실행 방식입니다.");
            ValidateMap(map, gameRoot);
            string executable = Path.Combine(gameRoot, "Support64", "SC2Switcher_x64.exe");
            if (!File.Exists(executable)) executable = Path.Combine(gameRoot, "Support", "SC2Switcher.exe");
            if (!File.Exists(executable)) throw new FileNotFoundException("스타2 SC2Switcher 실행기를 찾을 수 없습니다.");
            return new ProcessStartInfo(executable, "\"" + Path.GetFullPath(map) + "\"")
                { UseShellExecute = false, WorkingDirectory = Path.GetFullPath(gameRoot) };
        }
        public static void ValidateLauncher(string executable)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) ||
                !Path.GetPathRoot(executable).EndsWith("\\", StringComparison.Ordinal) ||
                executable.StartsWith("\\\\", StringComparison.Ordinal) ||
                !String.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("이 PC에 저장된 제작자 런처의 EXE 파일을 선택해 주세요. 바로가기·스크립트·네트워크 경로는 지원하지 않습니다.");
            string full = Path.GetFullPath(executable);
            full = Data.Under(Path.GetDirectoryName(full), Path.GetFileName(full));
            using (var stream = File.OpenRead(full))
            using (var reader = new BinaryReader(stream))
            {
                if (stream.Length < 64 || reader.ReadUInt16() != 0x5a4d)
                    throw new InvalidDataException("Windows 실행 파일 형식이 아닙니다.");
                stream.Position = 0x3c;
                int header = reader.ReadInt32();
                if (header < 64 || header > stream.Length - 4)
                    throw new InvalidDataException("Windows 실행 파일 헤더가 올바르지 않습니다.");
                stream.Position = header;
                if (reader.ReadUInt32() != 0x00004550) throw new InvalidDataException("Windows 실행 파일 형식이 아닙니다.");
            }
        }
        public static ProcessStartInfo AuthorLauncherInfo(Campaign item)
        {
            if (!UsesAuthorLauncher(item)) throw new InvalidOperationException("연결한 제작자 런처가 없습니다.");
            ValidateLauncher(item.launcherExecutable);
            string executable = Path.GetFullPath(item.launcherExecutable);
            return new ProcessStartInfo(executable) { Arguments = "", UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
        }
        public static Image MapIcon()
        {
            var image = new Bitmap(28, 28);
            using (var graphics = Graphics.FromImage(image))
            using (var pen = new Pen(GlassTheme.Accent, 2))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.DrawPolygon(pen, new[] { new Point(3, 7), new Point(10, 3), new Point(18, 7), new Point(25, 3),
                    new Point(25, 22), new Point(18, 26), new Point(10, 22), new Point(3, 26) });
                graphics.DrawLine(pen, 10, 3, 10, 22); graphics.DrawLine(pen, 18, 7, 18, 26);
            }
            return image;
        }
        public static string GameProcess(string gameRoot)
        {
            foreach (string name in new[] { "SC2_x64", "SC2" })
                foreach (var process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        string path = Path.GetFullPath(process.MainModule.FileName);
                        if (path.StartsWith(Path.GetFullPath(gameRoot).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                            return process.Id + ":" + process.StartTime.ToUniversalTime().Ticks;
                    }
            return null;
        }
        public static void RequireGameClosed(string gameRoot)
        {
            if (GameProcess(gameRoot) != null) throw new InvalidOperationException("실행 중인 스타2를 종료한 뒤 다시 시도해 주세요.");
        }
    }

    public sealed class ExternalCampaignDialog : ThemeForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);
        readonly TextBox title = new TextBox(), map = new TextBox(), cover = new TextBox(), executable = new TextBox();
        readonly ComboBox installation = new ComboBox(), launch = new ComboBox();
        readonly TextBox[] episodeFields = { new TextBox(), new TextBox(), new TextBox() };
        readonly MetalButton mapMode = new MetalButton(), launcherMode = new MetalButton(), episodeToggle = new MetalButton();
        readonly MetalButton browseMap = new MetalButton(), browseExecutable = new MetalButton();
        readonly Panel mapCard, launcherCard;
        readonly Panel fileSlot;
        readonly TableLayoutPanel episodePanel = new TableLayoutPanel();
        readonly FlowLayoutPanel episodeLine = new FlowLayoutPanel();
        DarkScrollPanel scrollHost;
        TableLayoutPanel content;
        public string CampaignName { get { return title.Text; } }
        public string LauncherMap { get { return map.Tag as string; } }
        public string Cover { get { return cover.Text; } }
        public string InstallationMode { get { return installation.SelectedIndex == 1 ? "author-launcher" : "manual"; } }
        public string LaunchMode { get { return launch.SelectedIndex == 2 ? "episodes" : launch.SelectedIndex == 1 ? "author-launcher" : "map"; } }
        public string LauncherExecutable { get { return executable.Tag as string; } }
        public string[] EpisodeMaps { get { return episodeFields.Select(f => f.Tag as string ?? "").ToArray(); } }
        public ExternalCampaignDialog(Campaign item, string gameRoot, string suggestedName = null)
        {
            Text = item == null ? "설치된 캠페인 연결" : "전용 캠페인 연결 변경";
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("맑은 고딕", 10.5f); BackColor = Color.FromArgb(75, 100, 128); ForeColor = GlassTheme.Text;
            FormBorderStyle = FormBorderStyle.None; ClientSize = new Size(820, 620);
            MinimumSize = new Size(640, 540); MaximumSize = new Size(1050, 860); Padding = new Padding(1);
            ShowInTaskbar = false; ShowIcon = false; KeyPreview = true;
            GlassTheme.RoundWindow(this, 20);
            installation.Items.AddRange(new object[] { "manual", "author-launcher" });
            launch.Items.AddRange(new object[] { "map", "author-launcher", "episodes" });

            var frame = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(28, 20, 28, 18), Margin = Padding.Empty, BackColor = GlassTheme.Background };
            frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            frame.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                Margin = Padding.Empty, BackColor = Color.Transparent };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            var titleLabel = new Label { Text = Text, Dock = DockStyle.Fill,
                ForeColor = GlassTheme.Text, Font = new Font(Font.FontFamily, 16.25f, FontStyle.Bold),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };
            header.Controls.Add(titleLabel, 0, 0);
            var subtitle = new Label { Text = "실행 방식과 연결 파일을 지정합니다. 게임 원본은 변경하지 않습니다.",
                Dock = DockStyle.Fill, ForeColor = GlassTheme.Muted, BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 9.5f), TextAlign = ContentAlignment.MiddleLeft,
                Margin = Padding.Empty };
            header.Controls.Add(subtitle, 0, 1);
            var close = new Button { Dock = DockStyle.Fill, Text = "", FlatStyle = FlatStyle.Flat,
                ForeColor = GlassTheme.Muted, BackColor = GlassTheme.Background, Cursor = Cursors.Hand,
                AccessibleName = "연결 취소하고 닫기", Margin = new Padding(0, 1, 0, 24) };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = GlassTheme.Surface;
            close.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float x = close.Width / 2f, y = close.Height / 2f;
                using (var pen = new Pen(GlassTheme.Muted, 1.8f)) {
                    e.Graphics.DrawLine(pen, x - 5, y - 5, x + 5, y + 5);
                    e.Graphics.DrawLine(pen, x + 5, y - 5, x - 5, y + 5);
                }
            };
            close.Click += delegate { DialogResult = DialogResult.Cancel; };
            header.Controls.Add(close, 1, 0); header.SetRowSpan(close, 2);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            };
            header.MouseDown += drag; titleLabel.MouseDown += drag; subtitle.MouseDown += drag;
            frame.Controls.Add(header, 0, 0);

            content = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 7, Margin = Padding.Empty, BackColor = GlassTheme.Background };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < content.RowCount; i++)
                content.RowStyles.Add(new RowStyle(i == 4 || i == 5 ? SizeType.Absolute : SizeType.AutoSize, 0));
            scrollHost = new DarkScrollPanel(content) { Dock = DockStyle.Fill, HideUnusedScrollBar = true,
                Margin = new Padding(0, 3, 0, 0) };
            frame.Controls.Add(scrollHost, 0, 1);

            var namePanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 2, Height = 84,
                BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, 12) };
            namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            namePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
            namePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
            namePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            namePanel.Controls.Add(new Label { Text = "캠페인 이름", Dock = DockStyle.Fill,
                ForeColor = GlassTheme.Muted, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
            FormatLinkField(title, "캠페인 이름"); title.ReadOnly = false;
            var titleField = new GlassSearchField(title) { Dock = DockStyle.Fill, MinimumSize = new Size(0, 46),
                Margin = new Padding(0, 0, 12, 0) };
            namePanel.Controls.Add(titleField, 0, 1);
            var pickCover = new MetalButton { Text = "표지 변경", Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold), Margin = Padding.Empty };
            pickCover.Click += delegate {
                using (var picker = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.bmp" })
                    if (picker.ShowDialog(this) == DialogResult.OK) cover.Text = picker.FileName;
            };
            namePanel.Controls.Add(pickCover, 1, 1);
            content.Controls.Add(namePanel, 0, 0);

            var modeHeading = new Label { Text = "실행 방식 선택", Dock = DockStyle.Top, Height = 30,
                ForeColor = GlassTheme.Text, Font = new Font(Font.FontFamily, 10.75f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, BackColor = Color.Transparent, Margin = Padding.Empty };
            content.Controls.Add(modeHeading, 0, 1);
            var modeChoices = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 1,
                Height = 58, Margin = new Padding(0, 0, 0, 12), BackColor = Color.Transparent };
            modeChoices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            modeChoices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            modeChoices.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            mapMode.Text = "시작 맵 연결"; launcherMode.Text = "제작자 런처 연결";
            foreach (var choice in new[] { mapMode, launcherMode })
            {
                choice.Dock = DockStyle.Fill; choice.AutoSize = false;
                choice.Font = new Font(Font.FontFamily, 10.75f, FontStyle.Bold);
                choice.Cursor = Cursors.Hand;
            }
            mapMode.Margin = new Padding(0, 0, 7, 0);
            launcherMode.Margin = new Padding(7, 0, 0, 0);
            modeChoices.Controls.Add(mapMode, 0, 0); modeChoices.Controls.Add(launcherMode, 1, 0);
            content.Controls.Add(modeChoices, 0, 2);

            browseMap.Text = "맵 선택"; browseMap.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            mapCard = LinkCard("시작 맵 파일 (.SC2Map)", map, "시작 맵 파일 (.SC2Map)", browseMap);
            browseMap.Click += delegate {
                using (var picker = new OpenFileDialog { Filter = "스타2 시작 맵|*.SC2Map",
                    InitialDirectory = Path.Combine(gameRoot, "Maps") })
                    if (picker.ShowDialog(this) == DialogResult.OK) {
                        DisplayFile(map, picker.FileName); launch.SelectedIndex = 0;
                        if (String.IsNullOrWhiteSpace(title.Text)) title.Text = Path.GetFileNameWithoutExtension(picker.FileName);
                    }
            };
            map.Click += delegate { browseMap.PerformClick(); };
            mapMode.Click += delegate { launch.SelectedIndex = 0; };

            browseExecutable.Text = "런처 선택"; browseExecutable.Font = new Font(Font.FontFamily, 10, FontStyle.Bold);
            launcherCard = LinkCard("제작자 런처 파일 (.exe)", executable, "제작자 런처 파일 (.exe)", browseExecutable);
            browseExecutable.Click += delegate {
                using (var picker = new OpenFileDialog { Filter = "제작자 런처|*.exe" })
                    if (picker.ShowDialog(this) == DialogResult.OK) {
                        DisplayFile(executable, picker.FileName); launch.SelectedIndex = 1;
                        if (item == null || ExternalCampaigns.InstallationMode(item) == "author-launcher") installation.SelectedIndex = 1;
                        if (String.IsNullOrWhiteSpace(title.Text)) title.Text = Path.GetFileNameWithoutExtension(picker.FileName);
                    }
            };
            executable.Click += delegate { browseExecutable.PerformClick(); };
            launcherMode.Click += delegate { launch.SelectedIndex = 1; };
            fileSlot = new Panel { Dock = DockStyle.Top, Height = 113,
                Margin = new Padding(0, 0, 0, 9), BackColor = GlassTheme.Background };
            // Two Dock=Fill children leave stale dock bounds after changing the
            // selected mode; explicitly give both the same full-size viewport.
            mapCard.Dock = DockStyle.None; launcherCard.Dock = DockStyle.None;
            fileSlot.Controls.Add(mapCard); fileSlot.Controls.Add(launcherCard);
            fileSlot.SizeChanged += delegate {
                var bounds = fileSlot.ClientRectangle;
                if (mapCard.Bounds != bounds) mapCard.Bounds = bounds;
                if (launcherCard.Bounds != bounds) launcherCard.Bounds = bounds;
            };
            mapCard.Bounds = launcherCard.Bounds = fileSlot.ClientRectangle;
            content.Controls.Add(fileSlot, 0, 3);

            episodeToggle.Text = "에피소드별 시작 맵 연결";
            episodeToggle.Font = new Font(Font.FontFamily, 9.75f, FontStyle.Bold);
            episodeToggle.AutoSize = false; episodeToggle.Width = 262; episodeToggle.Height = 39;
            episodeToggle.Margin = new Padding(0, 0, 0, 7);
            episodeToggle.Click += delegate { launch.SelectedIndex = LaunchMode == "episodes" ? 0 : 2; };
            episodeLine.Dock = DockStyle.Top; episodeLine.FlowDirection = FlowDirection.LeftToRight;
            episodeLine.WrapContents = false; episodeLine.Height = 44;
            episodeLine.BackColor = Color.Transparent; episodeLine.Margin = Padding.Empty;
            episodeLine.Controls.Add(episodeToggle); content.Controls.Add(episodeLine, 0, 4);

            episodePanel.Dock = DockStyle.Top; episodePanel.ColumnCount = 3; episodePanel.RowCount = 4;
            episodePanel.Height = 198; episodePanel.BackColor = GlassTheme.Surface;
            episodePanel.Padding = new Padding(15, 10, 15, 10);
            episodePanel.Margin = new Padding(0, 0, 0, 9);
            episodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 106));
            episodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            episodePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            episodePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            for (int i = 0; i < 3; i++) episodePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            var episodeHeading = new Label { Text = "에피소드별 실행 맵", Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 10, FontStyle.Bold), ForeColor = GlassTheme.Text,
                BackColor = Color.Transparent, Margin = Padding.Empty };
            episodePanel.Controls.Add(episodeHeading, 0, 0); episodePanel.SetColumnSpan(episodeHeading, 3);
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                episodePanel.Controls.Add(new Label { Text = "에피소드 " + (i + 1), Dock = DockStyle.Fill,
                    ForeColor = GlassTheme.Muted, BackColor = Color.Transparent,
                    TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, i + 1);
                FormatLinkField(episodeFields[i], "에피소드 " + (i + 1) + " 시작 맵");
                var episodeField = new GlassSearchField(episodeFields[i]) {
                    Dock = DockStyle.Fill, MinimumSize = new Size(0, 42), Margin = new Padding(0, 3, 9, 3) };
                episodePanel.Controls.Add(episodeField, 1, i + 1);
                var pickEpisode = new MetalButton { Text = "맵 선택", Dock = DockStyle.Fill,
                    Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold), Margin = new Padding(0, 3, 0, 3) };
                pickEpisode.Click += delegate {
                    using (var picker = new OpenFileDialog { Filter = "에피소드 시작 맵|*.SC2Map",
                        InitialDirectory = Path.Combine(gameRoot, "Maps") })
                        if (picker.ShowDialog(this) == DialogResult.OK) DisplayFile(episodeFields[index], picker.FileName);
                };
                episodeFields[i].Click += delegate { pickEpisode.PerformClick(); };
                episodePanel.Controls.Add(pickEpisode, 2, i + 1);
                if (item != null && item.episodeMaps != null && item.episodeMaps.Length == 3)
                    DisplayFile(episodeFields[i], item.episodeMaps[i]);
            }
            content.Controls.Add(episodePanel, 0, 5);

            content.Controls.Add(new Label { Text = "연결 정보만 저장됩니다. 원본 맵·런처·뱅크·세이브는 유지됩니다.",
                Dock = DockStyle.Top, ForeColor = GlassTheme.Muted, BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 9.25f), Height = 28, Margin = Padding.Empty }, 0, 6);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, BackColor = Color.Transparent, Margin = new Padding(0, 9, 0, 0) };
            var save = new MetalButton { Text = "연결 저장", AutoSize = true, Primary = true,
                MinimumSize = new Size(156, 48), Font = new Font(Font.FontFamily, 11, FontStyle.Bold) };
            save.Click += delegate {
                try {
                    ExternalCampaigns.ValidateConnection(InstallationMode, LaunchMode, LauncherMap, LauncherExecutable, gameRoot, EpisodeMaps);
                    if (String.IsNullOrWhiteSpace(title.Text) || title.Text.Length > 120)
                        throw new InvalidDataException("캠페인 이름은 1~120자로 입력해 주세요.");
                    DialogResult = DialogResult.OK;
                }
                catch (Exception error) { AppDialog.Info(this, error.Message, "연결 확인"); }
            };
            actions.Controls.Add(save);
            var cancelButton = new MetalButton { Text = "취소", DialogResult = DialogResult.Cancel,
                AutoSize = true, MinimumSize = new Size(114, 48), Margin = new Padding(0, 0, 10, 0),
                Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold) };
            actions.Controls.Add(cancelButton);
            frame.Controls.Add(actions, 0, 2); Controls.Add(frame);
            AcceptButton = cancelButton; CancelButton = cancelButton;
            Shown += delegate {
                title.SelectionStart = title.TextLength; title.SelectionLength = 0;
                cancelButton.Focus();
            };

            installation.SelectedIndex = item != null && ExternalCampaigns.InstallationMode(item) == "author-launcher" ? 1 : 0;
            launch.SelectedIndexChanged += delegate { UpdateModeDisplay(); };
            launch.SelectedIndex = item != null && ExternalCampaigns.LaunchMode(item) == "episodes" ? 2 :
                item != null && ExternalCampaigns.LaunchMode(item) == "author-launcher" ? 1 : 0;
            if (item != null) {
                title.Text = item.name; DisplayFile(map, item.launcherMap); cover.Text = item.thumbnail ?? "";
                DisplayFile(executable, item.launcherExecutable);
            }
            else {
                DisplayFile(map, null); DisplayFile(executable, null);
                if (!String.IsNullOrWhiteSpace(suggestedName))
                    title.Text = suggestedName.Length > 120 ? suggestedName.Substring(0, 120) : suggestedName;
            }
            UpdateModeDisplay();
        }
        static void DisplayFile(TextBox field, string path)
        {
            field.Tag = String.IsNullOrWhiteSpace(path) ? null : path;
            field.Text = field.Tag == null ? "선택된 파일 없음" : Path.GetFileName(path);
            field.ForeColor = field.Tag == null ? GlassTheme.Muted : GlassTheme.Text;
            field.AccessibleDescription = field.Tag as string ?? "파일을 선택해 주세요.";
            field.SelectionStart = 0; field.SelectionLength = 0;
        }
        static void FormatLinkField(TextBox field, string name)
        {
            field.AccessibleName = name; field.BackColor = GlassTheme.Field; field.ForeColor = GlassTheme.Text;
            field.Font = new Font("맑은 고딕", 10);
            field.BorderStyle = BorderStyle.FixedSingle; field.ReadOnly = true;
        }
        Panel LinkCard(string label, TextBox path, string accessibleName, MetalButton browse)
        {
            var card = new Panel { Dock = DockStyle.Fill, BackColor = GlassTheme.Background,
                Padding = new Padding(16, 10, 16, 13), Margin = Padding.Empty };
            card.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var border = new Pen(Color.FromArgb(64, 98, 129)))
                using (var fill = new SolidBrush(Color.FromArgb(21, 32, 48)))
                using (var outline = GlassTheme.Round(new Rectangle(1, 1, card.Width - 3, card.Height - 3), 12))
                { e.Graphics.FillPath(fill, outline); e.Graphics.DrawPath(border, outline); }
            };
            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
                BackColor = Color.Transparent, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill,
                ForeColor = Color.FromArgb(205, 217, 232), BackColor = Color.Transparent,
                Font = new Font(Font.FontFamily, 10f, FontStyle.Bold),
                Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            grid.SetColumnSpan(grid.GetControlFromPosition(0, 0), 2);
            FormatLinkField(path, accessibleName);
            var field = new GlassSearchField(path) { Dock = DockStyle.Fill, MinimumSize = new Size(0, 44),
                Margin = new Padding(0, 0, 10, 0) };
            grid.Controls.Add(field, 0, 1);
            browse.Dock = DockStyle.Fill; browse.AutoSize = false; browse.Margin = Padding.Empty;
            grid.Controls.Add(browse, 1, 1);
            card.Controls.Add(grid);
            return card;
        }
        void UpdateModeDisplay()
        {
            bool mapSelected = LaunchMode == "map" || LaunchMode == "episodes";
            mapMode.Primary = mapSelected; launcherMode.Primary = !mapSelected;
            mapMode.AccessibleDescription = mapSelected ? "현재 선택된 실행 방식" : "시작 맵 방식 선택";
            launcherMode.AccessibleDescription = !mapSelected ? "현재 선택된 실행 방식" : "제작자 런처 방식 선택";
            mapMode.Invalidate(); launcherMode.Invalidate();
            mapCard.Bounds = launcherCard.Bounds = fileSlot.ClientRectangle;
            mapCard.Visible = mapSelected;
            launcherCard.Visible = !mapSelected;
            episodeLine.Visible = mapSelected;
            bool episodes = LaunchMode == "episodes";
            episodePanel.Visible = episodes;
            if (content != null)
            {
                content.RowStyles[4].Height = mapSelected ? 44 : 0;
                content.RowStyles[5].Height = episodes ? 203 : 0;
            }
            episodeToggle.Text = episodes ? "일반 시작 맵으로 전환" : "에피소드별 시작 맵 연결";
            if (content != null) content.PerformLayout();
            if (scrollHost != null) scrollHost.ArrangeContent();
        }
        static void AddChoice(TableLayoutPanel layout, string text, ComboBox choice, int row)
        {
            choice.DropDownStyle = ComboBoxStyle.DropDownList; choice.Dock = DockStyle.Fill; choice.AccessibleName = text;
            choice.BackColor = GlassTheme.Field; choice.ForeColor = GlassTheme.Text;
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 10, 0, 4) }, 0, row);
            layout.Controls.Add(choice, 0, row + 1); layout.SetColumnSpan(choice, 2);
        }
        internal void VerifyModes(string installationMode, string launchMode, string selectedMap, string selectedExecutable, string gameRoot)
        {
            var labels = Controls.Cast<Control>().SelectMany(DescendantLabels).Select(label => label.Text).ToArray();
            if (mapMode.Text != "시작 맵 연결" || launcherMode.Text != "제작자 런처 연결" ||
                labels.Any(text => text == "설치 방식") ||
                Region == null || Region.IsVisible(new Point(0, 0)) ||
                !Region.IsVisible(new Point(Width / 2, Height / 2)))
                throw new InvalidOperationException("Connection editor must show only the two primary file-link choices.");
            installation.SelectedIndex = installationMode == "author-launcher" ? 1 : 0;
            launch.SelectedIndex = launchMode == "author-launcher" ? 1 : 0;
            DisplayFile(map, selectedMap); DisplayFile(executable, selectedExecutable);
            Application.DoEvents();
            var selectedCard = launchMode == "map" ? mapCard : launcherCard;
            var selectedField = launchMode == "map" ? map.Parent : executable.Parent;
            int fieldBottom = selectedField.PointToScreen(new Point(0, selectedField.Height)).Y;
            int cardBottom = selectedCard.PointToScreen(new Point(0, selectedCard.Height)).Y;
            if (selectedField.Height < 38 || fieldBottom > cardBottom - 6)
                throw new InvalidOperationException("Connection file picker must fit entirely within the selected card.");
            if (InstallationMode != installationMode || LaunchMode != launchMode ||
                !map.Enabled || !executable.Enabled || mapMode.Primary != (launchMode == "map") ||
                launcherMode.Primary != (launchMode == "author-launcher") ||
                mapCard.Visible != (launchMode == "map") || launcherCard.Visible != (launchMode == "author-launcher") ||
                map.Text != Path.GetFileName(selectedMap) || executable.Text != Path.GetFileName(selectedExecutable))
                throw new InvalidOperationException("Simple link sections must retain both editable paths and both launch choices.");
            ExternalCampaigns.ValidateConnection(InstallationMode, LaunchMode, LauncherMap, LauncherExecutable, gameRoot);
            Console.WriteLine("AUTHOR UI PASS: two clear link choices preserve " + InstallationMode + "/" + LaunchMode);
        }
        static IEnumerable<Label> DescendantLabels(Control control)
        {
            if (control is Label) yield return (Label)control;
            foreach (Control child in control.Controls)
                foreach (var label in DescendantLabels(child)) yield return label;
        }
        internal void VerifyEpisodeConnections(string[] paths, string gameRoot)
        {
            launch.SelectedIndex = 2; installation.SelectedIndex = 0;
            for (int i = 0; i < 3; i++) DisplayFile(episodeFields[i], paths[i]);
            if (LaunchMode != "episodes" || !EpisodeMaps.SequenceEqual(paths.Select(p => p ?? "")) ||
                episodeFields.Any(f => !f.Visible) || !episodePanel.Visible || !map.Enabled)
                throw new InvalidOperationException("Expanded episode editor must retain three independent map selections.");
            ExternalCampaigns.ValidateConnection(InstallationMode, LaunchMode, LauncherMap, LauncherExecutable, gameRoot, EpisodeMaps);
            Console.WriteLine("EPISODE UI PASS: three labelled map fields; unlinked paths may remain empty.");
        }
        static void AddField(TableLayoutPanel layout, string text, TextBox field, int row)
        {
            layout.Controls.Add(new Label { Text = text, AutoSize = true, Padding = new Padding(0, 10, 0, 4) }, 0, row);
            field.Dock = DockStyle.Fill; field.AccessibleName = text; field.BackColor = GlassTheme.Field; field.ForeColor = GlassTheme.Text;
            layout.Controls.Add(field, 0, row + 1); if (row == 1) layout.SetColumnSpan(field, 2);
        }
    }

    public sealed class RecordsDialog : ThemeForm
    {
        readonly ActivityStore store;
        readonly Campaign campaign;
        readonly Label details = new Label();
        readonly bool supportsBank;
        public RecordsDialog(ActivityStore store, Campaign campaign, string documents, bool supportsBank = false)
        {
            this.store = store; this.campaign = campaign; this.supportsBank = supportsBank;
            Text = "플레이 기록·뱅크 연결"; StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(720, 520);
            MinimumSize = new Size(640, 500); Font = new Font("맑은 고딕", 11); BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(22) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            details.Dock = DockStyle.Top; details.AutoSize = true;
            scroll.ClientSizeChanged += delegate { details.MaximumSize = new Size(Math.Max(1, scroll.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 6), 0); };
            scroll.Controls.Add(details); layout.Controls.Add(scroll);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            var link = new MetalButton { Text = "레볼라이트 뱅크 연결", AutoSize = true, MinimumSize = new Size(160, 44), Enabled = supportsBank };
            link.Click += delegate {
                using (var picker = new OpenFileDialog { Filter = "레볼라이트 캠페인 뱅크|RLO8D26WCampaign.SC2Bank", InitialDirectory = Path.Combine(documents, "StarCraft II", "Accounts") })
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        try { store.LinkBank(campaign.id, picker.FileName); RefreshRecord(); }
                        catch (Exception error) { AppDialog.Info(this, error.Message, "뱅크 연결 실패"); }
            };
            var run = new MetalButton { Text = "새 회차 시작 확인", AutoSize = true, MinimumSize = new Size(160, 44), Enabled = supportsBank && store.Snapshot(campaign.id).needsReview };
            run.Click += delegate {
                if (!AppDialog.Confirm(this, "게임에서 새 회차를 시작한 상태인가요?\n현재 뱅크를 새 기준으로 삼습니다. 기존 시간·완료 누적 기록은 유지합니다.\n이전 세이브를 불러온 것이라면 취소하세요.", "새 회차 확인", "새 회차 확인")) return;
                try { store.ConfirmNewRun(campaign.id); RefreshRecord(); } catch (Exception error) { AppDialog.Info(this, error.Message, "확인 실패"); }
            };
            actions.Controls.Add(link); actions.Controls.Add(run);
            actions.Controls.Add(new MetalButton { Text = "닫기", AutoSize = true, MinimumSize = new Size(90, 44), DialogResult = DialogResult.Cancel });
            layout.Controls.Add(actions, 0, 1); Controls.Add(layout); RefreshRecord();
        }
        void RefreshRecord()
        {
            var r = store.Snapshot(campaign.id);
            details.Text = campaign.name + "\n\n" + store.Summary(campaign.id) +
                "\n\n시간: 격납고가 열린 동안 확인한 게임 실행 시간입니다.\n전용 캠페인은 격납고에서 시작 맵으로 실행한 경우에 기록합니다.\n제작자 런처 경유 시간은 자동 기록하지 않습니다.\n메뉴·자리 비움이 포함될 수 있고, 이전 시간은 소급하지 않습니다." +
                "\n\n완료 기록: 연결 후 관측한 성공만 집계하며 회차 내 같은 임무는 중복 제외합니다." +
                "\n새 회차·이전 세이브가 구분되지 않으면 집계를 보류합니다." +
                "\n\n연결 뱅크: " + (r.bankPath ?? "연결 안 됨") +
                "\n최근 임무: " + (r.lastMission ?? "확인 안 됨") +
                "\n상태: " + (r.bankError ?? (r.bankPath == null ? "임무·회차 자동 기록 미연결" : "관측 중 · 과거 클리어 횟수는 추정하지 않음")) +
                (supportsBank ? "" : "\n이 모드는 시간 기록만 지원합니다. 뱅크 완료 자동 판독은 아직 미지원입니다.");
        }
    }
}
