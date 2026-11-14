using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace CampaignHangar
{
    public sealed class StandaloneCollisionReport
    {
        public readonly List<string> Different = new List<string>();
        public int DifferentMaps { get; internal set; }
        public int DifferentMods { get; internal set; }
        public bool HasConflict { get { return Different.Count > 0; } }
        public string Summary(string packageName)
        {
            string names = String.Join(", ", Different.Take(3).Select(Path.GetFileName));
            if (Different.Count > 3) names += " 외 " + (Different.Count - 3) + "개";
            return packageName + " · 맵 " + DifferentMaps + "개 / 모드 " + DifferentMods + "개 충돌\n" +
                names + "\n\n" +
                "제거 후 설치: 백업 후 교체, 실패 시 복원.\n" +
                "닫기: 변경 없음. ZIP·Bank·세이브 보존.";
        }
    }
    // A ZIP may contain both maps and shared-named mods, or a separate mods-
    // only companion ZIP.  Replace every affected installation unit together,
    // not just the map folder.  Nothing here executes the creator's EXE.
    public static class StandaloneConflicts
    {
        sealed class Unit
        {
            public string Relative, Destination, Staged, Backup, Holding;
            public bool OldMoved, Installed, CopiedBackup, SameVolume;
        }
        static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }
        static bool IsFolder(string path) { return Directory.Exists(path); }
        static string UnitPath(string relative)
        {
            string[] parts = relative.Replace('\\','/').Split('/');
            if (parts.Length < 2) throw new InvalidDataException("ZIP 설치 상대 경로가 올바르지 않습니다: " + relative);
            if (String.Equals(parts[0], "Maps", StringComparison.OrdinalIgnoreCase))
            {
                if (String.Equals(parts[1], "Campaign", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("CCM 공용 캠페인 파일은 교체할 수 없습니다.");
                return "Maps/" + parts[1];
            }
            if (String.Equals(parts[0], "Mods", StringComparison.OrdinalIgnoreCase) &&
                parts[1].EndsWith(".SC2Mod", StringComparison.OrdinalIgnoreCase))
                return "Mods/" + parts[1];
            throw new InvalidDataException("ZIP에 지원되지 않는 게임 설치 경로가 있습니다: " + relative);
        }
        static List<Unit> Units(StandaloneZipPackage package, string gameRoot)
        {
            var list = package.GameFiles.Select(f => UnitPath(f.GamePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(relative => new Unit {
                    Relative = relative, Destination = Data.Under(gameRoot, relative)
                }).ToList();
            if (list.Count == 0 || list.Count > 2000)
                throw new InvalidDataException("게임에 배치할 맵·모드가 없거나 설치 항목이 너무 많습니다.");
            return list;
        }
        static bool SameEntry(ZipArchiveEntry entry, string file)
        {
            if (!File.Exists(file)) return false;
            if (new FileInfo(file).Length != entry.Length) return false;
            using (var sha = SHA256.Create())
            using (var source = entry.Open())
                return String.Equals(BitConverter.ToString(sha.ComputeHash(source)).Replace("-", ""),
                    Data.Hash(file), StringComparison.OrdinalIgnoreCase);
        }
        public static StandaloneCollisionReport Inspect(StandaloneZipPackage package, string gameRoot)
        {
            var report = new StandaloneCollisionReport();
            if (!File.Exists(Path.Combine(gameRoot ?? "", "StarCraft II.exe")))
                throw new InvalidOperationException("설정에서 스타2 게임 폴더를 먼저 선택해 주세요.");
            Units(package, gameRoot); // validate all install units before inspection
            using (var archive = ZipFile.OpenRead(package.Zip))
                foreach (var file in package.GameFiles)
                {
                    string destination = Data.Under(gameRoot, file.GamePath);
                    if (Directory.Exists(destination))
                        throw new IOException("ZIP 설치 파일과 같은 이름의 폴더가 있습니다: " + destination);
                    if (!File.Exists(destination)) continue;
                    var entry = archive.GetEntry(file.Entry);
                    if (entry == null || entry.Length != file.Bytes)
                        throw new InvalidDataException("ZIP 파일 목록이 변경됐습니다.");
                    if (SameEntry(entry, destination)) continue;
                    report.Different.Add(file.GamePath);
                    if (file.IsMap) report.DifferentMaps++;
                    else report.DifferentMods++;
                }
            return report;
        }
        static bool Overlaps(string target, string owned)
        {
            string a = Path.GetFullPath(target).TrimEnd('\\');
            string b = Path.GetFullPath(owned).TrimEnd('\\');
            return String.Equals(a,b,StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith(b+"\\",StringComparison.OrdinalIgnoreCase) ||
                b.StartsWith(a+"\\",StringComparison.OrdinalIgnoreCase);
        }
        static void CheckOwnership(string storageRoot, string gameRoot, List<Unit> units,
            StandaloneZipPackage package, ExternalCampaigns external, StandaloneZipChoice choice)
        {
            string maps = Data.Under(gameRoot, "Maps"), mods = Data.Under(gameRoot, "Mods");
            foreach(var unit in units)
            {
                if (String.Equals(unit.Destination, maps, StringComparison.OrdinalIgnoreCase) ||
                    String.Equals(unit.Destination, mods, StringComparison.OrdinalIgnoreCase) ||
                    (File.Exists(unit.Destination) && (File.GetAttributes(unit.Destination) & FileAttributes.ReparsePoint) != 0) ||
                    (Directory.Exists(unit.Destination) && (File.GetAttributes(unit.Destination) & FileAttributes.ReparsePoint) != 0))
                    throw new IOException("보호된 게임 경로나 링크는 교체할 수 없습니다: " + unit.Relative);
            }
            var state = new Library(storageRoot).Current();
            var owned = (state.managed ?? new List<OwnedFile>()).Concat(state.files ?? new List<OwnedFile>())
                .Where(f => f != null && !String.IsNullOrEmpty(f.path))
                .Select(f => Data.Under(gameRoot, f.path));
            if (units.Any(u => owned.Any(o => Overlaps(u.Destination, o))))
                throw new IOException("현재 적용된 CCM이 관리하는 파일과 충돌해 교체를 중단했습니다.");
            var dedicated = new DedicatedLibrary(storageRoot).Current();
            if (units.Any(u => (dedicated.installed ?? new List<DedicatedInstalled>()).Any(c =>
                c.release != null && c.release.files != null &&
                c.release.files.Any(f => Overlaps(u.Destination, Data.Under(gameRoot, f.path))))))
                throw new IOException("정식 전용 캠페인이 소유하는 파일이므로 교체를 중단했습니다.");
            // Other manually connected start maps also count as ownership.
            var chosen = new List<string>();
            if (choice != null)
            {
                var entries=choice.Kind=="episodes" ? choice.Episodes : new [] { choice.Entry };
                foreach(string entry in entries)
                    chosen.Add(Data.Under(gameRoot,package.GameFiles.Single(f=>f.Entry==entry).GamePath));
            }
            foreach(var linked in external.All())
            {
                var paths = new List<string>();
                if (!String.IsNullOrWhiteSpace(linked.launcherMap)) paths.Add(linked.launcherMap);
                if (linked.episodeMaps != null) paths.AddRange(linked.episodeMaps.Where(p => !String.IsNullOrWhiteSpace(p)));
                foreach(string path in paths)
                    if (units.Any(u => Overlaps(u.Destination, path)) &&
                        !chosen.Any(p => String.Equals(p,path,StringComparison.OrdinalIgnoreCase)))
                        throw new IOException("다른 개인 캠페인의 연결 맵이 같은 위치에 있어 교체할 수 없습니다: " + path);
            }
        }
        static void VerifyTree(string source)
        {
            if (!Directory.Exists(source)) return;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("링크 형태의 설치 폴더는 자동 교체하지 않습니다.");
            foreach(var dir in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("설치 폴더 안에 링크가 있어 자동 교체하지 않습니다.");
            foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("설치 폴더 안에 링크 파일이 있어 자동 교체하지 않습니다.");
        }
        static void CopyWithVerify(string source, string destination)
        {
            if (IsFolder(source))
            {
                VerifyTree(source);
                Directory.CreateDirectory(destination);
                string prefix=Path.GetFullPath(source).TrimEnd('\\')+"\\";
                foreach(var dir in Directory.GetDirectories(source,"*",SearchOption.AllDirectories))
                    Directory.CreateDirectory(Data.Under(destination,dir.Substring(prefix.Length)));
                foreach(var file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
                {
                    string target=Data.Under(destination,file.Substring(prefix.Length));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file,target,false);
                    if (Data.Hash(file)!=Data.Hash(target))
                        throw new IOException("기존 설치본 백업 무결성 검사 실패: "+file);
                }
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source,destination,false);
                if (Data.Hash(source)!=Data.Hash(destination))
                    throw new IOException("모드 백업 무결성 검사 실패: "+source);
            }
        }
        static void Move(string from, string to)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            if (Directory.Exists(from)) Directory.Move(from,to);
            else File.Move(from,to);
        }
        static void Remove(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path,true);
            else if (File.Exists(path)) File.Delete(path);
        }
        public static StandaloneZipResult Replace(StandaloneZipPackage package, StandaloneZipChoice choice,
            string storageRoot, string gameRoot, ExternalCampaigns external,
            Action<WorkProgress> progress = null, Action<int> fault = null)
        {
            if (choice != null && (!package.Choices.Contains(choice) || choice.Kind == "author-launcher"))
                throw new InvalidOperationException("ZIP 시작점을 다시 선택해 주세요.");
            if (choice == null && package.GameFiles.Any(f => f.IsMap))
                throw new InvalidOperationException("시작 맵이 있는 ZIP은 시작점을 선택해야 합니다.");
            ExternalCampaigns.RequireGameClosed(gameRoot);
            var units = Units(package,gameRoot);
            CheckOwnership(storageRoot,gameRoot,units,package,external,choice);
            string sourceHash=Data.Hash(package.Zip);
            string token=Guid.NewGuid().ToString("N");
            string stage=Data.Under(gameRoot,".hangar-standalone-stage-"+token);
            string backupRoot=Data.Under(storageRoot,"standalone-backups/"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+token);
            bool sameVolume=String.Equals(Path.GetPathRoot(storageRoot),Path.GetPathRoot(gameRoot),StringComparison.OrdinalIgnoreCase);
            var completed=new List<Unit>();
            bool committed=false, restored=true;
            try
            {
                Directory.CreateDirectory(stage);
                using (var archive=ZipFile.OpenRead(package.Zip))
                {
                    long bytes=0,total=package.GameFiles.Sum(f=>f.Bytes);
                    foreach(var file in package.GameFiles)
                    {
                        string target=Data.Under(stage,file.GamePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        var entry=archive.GetEntry(file.Entry);
                        if (entry==null || entry.Length!=file.Bytes)
                            throw new InvalidDataException("ZIP 파일 구성이 설치 전 변경됐습니다.");
                        using (var input=entry.Open())
                        using (var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))
                        {
                            byte[] buffer=new byte[128*1024]; int read; long actual=0;
                            while((read=input.Read(buffer,0,buffer.Length))>0)
                            {
                                actual+=read;
                                if(actual>entry.Length) throw new InvalidDataException("ZIP 파일 크기가 올바르지 않습니다.");
                                output.Write(buffer,0,read);
                                bytes+=read;
                                if(progress!=null && (bytes==total || bytes%(4L*1024*1024)<read))
                                    progress(new WorkProgress("새 캠페인 맵·모드 검증",bytes,total,true));
                            }
                            if(actual!=entry.Length) throw new InvalidDataException("ZIP 항목 데이터가 손상됐습니다.");
                        }
                        if(file.IsMap || file.IsModArchive)
                            using(var input=File.OpenRead(target))
                                if(input.ReadByte()!='M'||input.ReadByte()!='P'||input.ReadByte()!='Q'||input.ReadByte()!=26)
                                    throw new InvalidDataException("게임 MPQ 파일 형식이 아닙니다: "+file.Entry);
                    }
                }
                if(Data.Hash(package.Zip)!=sourceHash)
                    throw new IOException("검증 중 원본 ZIP이 변경되어 설치하지 않았습니다.");
                if(fault!=null)fault(1);
                ExternalCampaigns.RequireGameClosed(gameRoot);
                CheckOwnership(storageRoot,gameRoot,units,package,external,choice);
                foreach(var unit in units)
                {
                    unit.Staged=Data.Under(stage,unit.Relative);
                    unit.Backup=Data.Under(backupRoot,unit.Relative);
                    unit.Holding=unit.Destination+".hangar-previous-"+token;
                    unit.SameVolume=sameVolume;
                    if(!Exists(unit.Staged)) throw new IOException("준비한 ZIP 설치 파일이 없습니다: "+unit.Relative);
                    completed.Add(unit);
                    if(Exists(unit.Destination))
                    {
                        VerifyTree(unit.Destination);
                        if(sameVolume)
                        {
                            Move(unit.Destination,unit.Backup);
                            unit.OldMoved=true;
                        }
                        else
                        {
                            CopyWithVerify(unit.Destination,unit.Backup);
                            unit.CopiedBackup=true;
                            Move(unit.Destination,unit.Holding);
                            unit.OldMoved=true;
                        }
                    }
                    if(fault!=null)fault(2);
                    Move(unit.Staged,unit.Destination);
                    unit.Installed=true;
                    if(fault!=null)fault(3);
                }
                Campaign saved=null;
                bool linkedBefore=false;
                if(choice!=null)
                {
                    string launcher=Data.Under(gameRoot,package.GameFiles.Single(f=>f.Entry==choice.Entry).GamePath);
                    string[] episodes=choice.Kind=="episodes" ? choice.Episodes.Select(e=>Data.Under(gameRoot,
                        package.GameFiles.Single(f=>f.Entry==e).GamePath)).ToArray() : null;
                    var prior=external.All().FirstOrDefault(c=>c.kind=="external" &&
                        ExternalCampaigns.LaunchMode(c)==choice.Kind &&
                        (choice.Kind=="map" ? String.Equals(c.launcherMap,launcher,StringComparison.OrdinalIgnoreCase) :
                         c.episodeMaps!=null && c.episodeMaps.SequenceEqual(episodes,StringComparer.OrdinalIgnoreCase)));
                    linkedBefore=prior!=null;
                    saved=external.Save(prior==null?null:prior.id,prior==null?package.Name:prior.name,
                        choice.Kind=="map"?launcher:null,null,gameRoot,"manual",choice.Kind,null,episodes,sourceHash);
                }
                committed=true;
                foreach(var unit in units)
                    if(unit.OldMoved && !unit.SameVolume && Exists(unit.Holding))
                        try { Remove(unit.Holding); } catch(IOException) { }
                return new StandaloneZipResult {Campaign=saved,AlreadyLinked=linkedBefore,
                    CreatedFiles=package.GameFiles.Count,PreviousBackup=backupRoot};
            }
            catch(Exception error)
            {
                var recovery=new List<string>();
                foreach(var unit in Enumerable.Reverse(completed))
                {
                    try
                    {
                        if(unit.Installed && Exists(unit.Destination)) Remove(unit.Destination);
                        if(unit.OldMoved)
                        {
                            string old=unit.SameVolume ? unit.Backup : unit.Holding;
                            if(Exists(old)) Move(old,unit.Destination);
                            else throw new IOException("원본 복구 파일 누락: "+old);
                        }
                    }
                    catch(Exception restoreError) {recovery.Add(restoreError.Message);}
                }
                restored=recovery.Count==0;
                if(!restored) throw new IOException("설치를 되돌리던 중 복구가 완료되지 않았습니다. 백업을 보호했습니다: "+
                    backupRoot+" / "+String.Join("; ",recovery),error);
                throw;
            }
            finally
            {
                if(restored && Directory.Exists(stage))
                    try { Directory.Delete(stage,true); } catch(IOException) { }
                // Never erase the recovery copy after successful replacement.
                // On rollback, the same-volume copy may have been moved back.
                if(!committed && restored && Directory.Exists(backupRoot))
                    try {Directory.Delete(backupRoot,true);} catch(IOException) { }
            }
        }
    }
}
