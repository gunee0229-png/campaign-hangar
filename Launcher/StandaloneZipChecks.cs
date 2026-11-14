using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace CampaignHangar
{
    static class StandaloneZipChecks
    {
        static void Require(bool test, string message) { if (!test) throw new Exception("STANDALONE ZIP FAIL: " + message); }
        static void Rejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Require(rejected, message);
        }
        static void Add(ZipArchive archive, string name, byte[] bytes)
        {
            using (var target = archive.CreateEntry(name, CompressionLevel.NoCompression).Open())
                target.Write(bytes, 0, bytes.Length);
        }
        static string Make(string root, string name, Action<ZipArchive> fill)
        {
            string path = Path.Combine(root, name + ".zip");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) fill(zip);
            return path;
        }
        public static void Run(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Standalone ZIP verification requires a new folder.");
            Directory.CreateDirectory(root);
            string game = Path.Combine(root, "game"), storage = Path.Combine(root, "storage");
            Directory.CreateDirectory(game); Directory.CreateDirectory(storage);
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "isolated fixture");
            var mpq = new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 };
            string normal = Make(root, "Map-Launcher", zip => {
                Add(zip, "Campaign/Launcher.SC2Map", mpq);
                Add(zip, "Campaign/Mission01.SC2Map", mpq);
                Add(zip, "Mods/Test.SC2Mod", mpq);
            });
            string originalHash = Data.Hash(normal);
            var package = StandaloneZip.Inspect(normal);
            Require(package.Choices.Count == 1 && package.Choices[0].Kind == "map" &&
                package.GameFiles.Count == 3, "recognize SC2Map launcher with dependencies");
            var external = new ExternalCampaigns(storage);
            var installed = StandaloneZip.Install(package, package.Choices[0], storage, game, external);
            Require(installed.Campaign != null && !installed.AlreadyLinked && installed.CreatedFiles == 3 &&
                File.Exists(Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Launcher.SC2Map")) &&
                File.Exists(Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Mission01.SC2Map")) &&
                File.Exists(Path.Combine(game, "Mods", "Test.SC2Mod")) &&
                installed.Campaign.developmentOnly == AppChannel.IsDevelopment, "install game MPQs and save dev-only connection");
            var repeat = StandaloneZip.Install(package, package.Choices[0], storage, game, external);
            Require(repeat.AlreadyLinked && external.All().Count == 1 && repeat.CreatedFiles == 0 &&
                Data.Hash(normal) == originalHash, "repeat import preserves original and doesn't duplicate");
            string missingMission = Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Mission01.SC2Map");
            File.Delete(missingMission); // isolated antivirus-quarantine simulation
            var repaired = StandaloneZip.Install(package, package.Choices[0], storage, game, external);
            Require(!repaired.AlreadyLinked && repaired.CreatedFiles == 1 && repaired.Campaign.id == installed.Campaign.id &&
                external.All().Count == 1 && Data.Hash(missingMission) == Data.Hash(Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Launcher.SC2Map")) &&
                Data.Hash(normal) == originalHash,
                "reimporting an already-linked ZIP restores its missing mission without replacing identity or original ZIP");
            File.WriteAllBytes(missingMission, new byte[] { 77, 80, 81, 26, 9, 9, 9, 9 });
            string editedMissionHash = Data.Hash(missingMission);
            Rejected(() => StandaloneZip.Install(package, package.Choices[0], storage, game, external),
                "reimport refuses to overwrite a user-modified installed mission");
            Require(Data.Hash(missingMission) == editedMissionHash && external.All().Count == 1,
                "failed repair preserves externally modified map and existing campaign ID");
            File.WriteAllBytes(missingMission, mpq);
            string differentSource = Make(root, "Other-Launcher", zip => {
                Add(zip, "Maps/Map-Launcher/Campaign/Launcher.SC2Map", mpq);
                Add(zip, "Maps/Map-Launcher/Campaign/Different.SC2Map", mpq);
            });
            var other = StandaloneZip.Inspect(differentSource);
            Rejected(() => StandaloneZip.Install(other, other.Choices[0], storage, game, external),
                "another ZIP targeting the same launcher cannot impersonate the existing connection");
            Require(external.All().Count == 1 && !File.Exists(Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Different.SC2Map")),
                "different ZIP conflict does not add files");
            string existing = Path.Combine(game, "Maps", "Map-Launcher", "Campaign", "Mission01.SC2Map");
            File.WriteAllBytes(existing, new byte[] { 4, 5, 6, 7 });
            string badConflict = Make(root, "Conflict", zip => {
                Add(zip, "maps/FreshLauncher.SC2Map", mpq);
                Add(zip, "maps/Map-Launcher/Campaign/Mission01.SC2Map", mpq);
            });
            var conflict = StandaloneZip.Inspect(badConflict);
            Rejected(() => StandaloneZip.Install(conflict, conflict.Choices[0], storage, game, external), "conflicting game file rejected");
            Require(!File.Exists(Path.Combine(game, "Maps", "FreshLauncher.SC2Map")) &&
                File.ReadAllBytes(existing).SequenceEqual(new byte[] { 4, 5, 6, 7 }) &&
                external.All().Count == 1, "failed attempt rolls back only created content");
            string episodes = Make(root, "Episodes", zip => {
                for (int i = 1; i <= 3; i++)
                    Add(zip, "Maps/Episode" + i + "Launcher.SC2Map", mpq);
            });
            var part = StandaloneZip.Inspect(episodes);
            Require(part.Choices.Count == 1 && part.Choices[0].Kind == "episodes", "three episode launchers auto-grouped");
            var trilogy = StandaloneZip.Install(part, part.Choices[0], storage, game, external);
            Require(trilogy.Campaign.episodeMaps.Length == 3 && trilogy.Campaign.episodeMaps.All(File.Exists),
                "three episode entries link to installed maps");
            string sourceExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CampaignHangar.exe");
            byte[] pe = File.ReadAllBytes(sourceExe);
            string executable = Make(root, "Creator", zip => {
                Add(zip, "Campaign/Launcher.exe", pe);
                Add(zip, "Campaign/info.txt", new byte[] { 65, 66, 67 });
                Add(zip, "Campaign/LaunchMap.SC2Map", mpq);
            });
            var exePackage = StandaloneZip.Inspect(executable);
            Require(exePackage.Choices.Count == 1 && exePackage.Choices[0].Kind == "author-launcher",
                "creator EXE detected without automatically executing");
            var author = StandaloneZip.Install(exePackage, exePackage.Choices[0], storage, game, external);
            Require(File.Exists(author.Campaign.launcherExecutable) &&
                File.Exists(Path.Combine(Path.GetDirectoryName(author.Campaign.launcherExecutable), "info.txt")) &&
                external.All().Count == 3, "creator executable managed outside the game without execution");
            string both = Make(root, "Both", zip => {
                Add(zip, "Launcher.exe", pe);
                Add(zip, "Launcher.SC2Map", mpq);
            });
            Require(StandaloneZip.Inspect(both).Choices.Count == 2, "dual launcher candidates require selection");
            string escape = Make(root, "Escape", zip => Add(zip, "../Launcher.SC2Map", mpq));
            Rejected(() => StandaloneZip.Inspect(escape), "ZIP Slip rejected");
            string duplicate = Make(root, "Duplicate", zip => {
                Add(zip, "Launcher.SC2Map", mpq);
                Add(zip, "launcher.sc2map", mpq);
            });
            Rejected(() => StandaloneZip.Inspect(duplicate), "case-insensitive duplicates rejected");
            string invalid = Make(root, "Invalid", zip => Add(zip, "InvalidLauncher.SC2Map", new byte[] { 1, 2, 3 }));
            var invalidPack = StandaloneZip.Inspect(invalid);
            Rejected(() => StandaloneZip.Install(invalidPack, invalidPack.Choices[0], storage, game, external), "invalid MPQ rejected");
            Require(!File.Exists(Path.Combine(game, "Maps", "Invalid", "InvalidLauncher.SC2Map")) &&
                external.All().Count == 3, "failure does not change registrations");
            string darkStory = Make(root, "The Dark Story", zip => {
                Add(zip, "TDS_Launcher.SC2Map", mpq);
                Add(zip, "tMission_1.SC2Map", mpq);
                Add(zip, "zMission_1.SC2Map", mpq);
            });
            var dark = StandaloneZip.Inspect(darkStory);
            Require(dark.Choices.Count == 1 && dark.GameFiles.Count == 3 &&
                dark.GameFiles.All(f => f.GamePath.StartsWith("Maps/The Dark Story/")),
                "root-level maps isolated in a named game folder");
            string darkFolder = Path.Combine(game, "Maps", "The Dark Story");
            Directory.CreateDirectory(darkFolder);
            byte[] translated = { 77, 80, 81, 26, 9, 8, 7, 6, 5 };
            foreach (var file in dark.GameFiles.Where(f => f.IsMap))
                File.WriteAllBytes(Data.Under(game, file.GamePath), translated);
            File.WriteAllText(Path.Combine(darkFolder, "local-notes.txt"), "preserve existing non-map data in backup");
            string originalZipHash = Data.Hash(darkStory);
            var variant = StandaloneZip.ExistingMapVariant(dark, dark.Choices[0], game);
            Require(variant != null && variant.MapCount == 3 && variant.DifferentFileCount == 3 &&
                String.Equals(variant.Folder, darkFolder, StringComparison.OrdinalIgnoreCase),
                "existing translated campaign detected without overwriting it");
            for (int faultStage = 1; faultStage <= 3; faultStage++)
            {
                int stage = faultStage;
                Rejected(() => StandaloneZip.ReplaceExistingMaps(dark, dark.Choices[0], variant, storage,
                    game, external, null, at => { if (at == stage) throw new IOException("Injected rollback " + at); }),
                    "replacement must roll back when failing at phase " + stage);
                Require(String.Equals(Data.Hash(Path.Combine(darkFolder, "TDS_Launcher.SC2Map")),
                    BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(translated)).Replace("-", ""),
                    StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(Path.Combine(darkFolder, "local-notes.txt")) &&
                    external.All().Count == 3 && Data.Hash(darkStory) == originalZipHash,
                    "injected failure restores the translated campaign and keeps external records and ZIP");
            }
            var replaced = StandaloneZip.ReplaceExistingMaps(dark, dark.Choices[0], variant, storage, game, external);
            Require(replaced.Campaign != null && replaced.CreatedFiles == 3 &&
                File.Exists(replaced.PreviousBackup) == false && Directory.Exists(replaced.PreviousBackup) &&
                File.Exists(Path.Combine(replaced.PreviousBackup, "local-notes.txt")) &&
                Data.Hash(Path.Combine(replaced.PreviousBackup, "TDS_Launcher.SC2Map")) !=
                    Data.Hash(Path.Combine(darkFolder, "TDS_Launcher.SC2Map")) &&
                new FileInfo(Path.Combine(darkFolder, "TDS_Launcher.SC2Map")).Length == mpq.Length &&
                !File.Exists(Path.Combine(darkFolder, "local-notes.txt")) &&
                external.All().Count == 4 && Data.Hash(darkStory) == originalZipHash,
                "explicit replace installs ZIP and leaves complete prior directory in recoverable backup");
            var repeatReplacement = StandaloneZip.Install(dark, dark.Choices[0], storage, game, external);
            Require(repeatReplacement.AlreadyLinked && external.All().Count == 4,
                "subsequent identical ZIP reuse does not create duplicate card");
            // The Dark Story is also distributed as a launcher/maps package
            // plus packed TGH_Mod.SC2Mod and a folder-form achievements mod.
            // A translated installed mod must trigger the SAME two-button
            // conflict flow as a translated map.
            string packedMod = Path.Combine(game, "Mods", "TGH_Mod.SC2Mod");
            byte[] oldMod = {77, 80, 81, 26, 9, 9, 9, 9, 9};
            byte[] newMod = {77, 80, 81, 26, 3, 4, 5, 6, 7};
            File.WriteAllBytes(packedMod, oldMod);
            string achievement = Path.Combine(game, "Mods", "TDS_Achievements.SC2Mod");
            Directory.CreateDirectory(achievement);
            File.WriteAllText(Path.Combine(achievement, "old-note.txt"), "keep this in full backup");
            string complete = Make(root, "The Dark Story Bundle", zip => {
                Add(zip, "Maps/The Dark Story/TDS_Launcher.SC2Map", mpq);
                Add(zip, "Maps/The Dark Story/tMission_1.SC2Map", mpq);
                Add(zip, "Maps/The Dark Story/zMission_1.SC2Map", mpq);
                Add(zip, "Mods/TGH_Mod.SC2Mod", newMod);
                Add(zip, "Mods/TDS_Achievements.SC2Mod/DocumentInfo", new byte[] {10,11,12});
            });
            var bundle = StandaloneZip.Inspect(complete);
            var report = StandaloneConflicts.Inspect(bundle, game);
            Require(report.HasConflict && report.DifferentMods >= 1 &&
                report.Different.Any(p => p.EndsWith("TGH_Mod.SC2Mod")) &&
                report.Summary("The Dark Story").Contains("TGH_Mod.SC2Mod"),
                "packed mod byte conflict identified before install and shown in confirmation");
            string beforeBundle = Data.Hash(complete), beforeMap = Data.Hash(Path.Combine(darkFolder,"TDS_Launcher.SC2Map"));
            string beforeMod = Data.Hash(packedMod);
            for(int inject=1;inject<=3;inject++)
            {
                int point=inject, unitCount=0;
                Rejected(() => StandaloneConflicts.Replace(bundle,bundle.Choices[0],storage,game,external,null,
                    at => {if(at==3 && ++unitCount<2) return; if(at==point) throw new IOException("forced mixed-map-mod failure");}),
                    "a mixed map/mod transaction must roll back at failure phase "+point);
                Require(Data.Hash(complete)==beforeBundle && Data.Hash(packedMod)==beforeMod &&
                    Data.Hash(Path.Combine(darkFolder,"TDS_Launcher.SC2Map"))==beforeMap &&
                    File.Exists(Path.Combine(achievement,"old-note.txt")) && external.All().Count==4,
                    "mixed package failure restores original map, main mod, directory mod, ZIP and links");
            }
            var replacedBundle=StandaloneConflicts.Replace(bundle,bundle.Choices[0],storage,game,external);
            Require(replacedBundle.Campaign!=null && replacedBundle.AlreadyLinked &&
                Directory.Exists(replacedBundle.PreviousBackup) &&
                Data.Hash(Path.Combine(replacedBundle.PreviousBackup,"Mods","TGH_Mod.SC2Mod"))==beforeMod &&
                File.Exists(Path.Combine(replacedBundle.PreviousBackup,"Mods","TDS_Achievements.SC2Mod","old-note.txt")) &&
                Data.Hash(packedMod)!=beforeMod &&
                File.ReadAllBytes(packedMod).SequenceEqual(newMod) &&
                !File.Exists(Path.Combine(achievement,"old-note.txt")) &&
                external.All().Count==4 && Data.Hash(complete)==beforeBundle,
                "confirmed mixed ZIP atomically replaces maps and mods and keeps both originals in storage backup");
            string modsOnly=Make(root,"ModsOnly",zip => {
                Add(zip,"TGH_Mod.SC2Mod",oldMod);
                Add(zip,"TDS_Achievements.SC2Mod/DocumentInfo",new byte[] {22,23});
            });
            var modsPackage=StandaloneZip.Inspect(modsOnly);
            Require(modsPackage.Choices.Count==0 && modsPackage.GameFiles.Count==2 &&
                StandaloneConflicts.Inspect(modsPackage,game).DifferentMods>=1,
                "mods-only ZIP can be installed as companion without a false launcher");
            var modules=StandaloneConflicts.Replace(modsPackage,null,storage,game,external);
            Require(modules.Campaign==null && File.ReadAllBytes(packedMod).SequenceEqual(oldMod) &&
                Directory.Exists(modules.PreviousBackup) && external.All().Count==4,
                "mods-only replace backs up previous mod but does not create a duplicate campaign card");
            string protectedRoot = Make(root, "Protected", zip => {
                Add(zip, "ProtectedLauncher.SC2Map", mpq);
                Add(zip, "Mission01.SC2Map", mpq);
                Add(zip, "Mission02.SC2Map", mpq);
            });
            var protectedPackage = StandaloneZip.Inspect(protectedRoot);
            string protectedFolder = Path.Combine(game, "Maps", "Protected");
            Directory.CreateDirectory(protectedFolder);
            foreach (var file in protectedPackage.GameFiles)
                File.WriteAllBytes(Data.Under(game, file.GamePath), translated);
            // The active CCM state protects folders under its management.
            // A standalone replacement must not replace such a folder.
            Rejected(() => StandaloneZip.ReplaceExistingMaps(protectedPackage,
                protectedPackage.Choices[0], StandaloneZip.ExistingMapVariant(
                    protectedPackage, protectedPackage.Choices[0], game),
                storage, game, external, null, at => {
                    if (at == 1) throw new IOException("fixture deliberately blocked");
                }), "isolated replacement fault does not mutate unrelated folder");
            Require(File.Exists(Path.Combine(protectedFolder, "ProtectedLauncher.SC2Map")) &&
                external.All().Count == 4, "failed replacement preserves other campaigns");
            string unknown = Make(root, "Unknown", zip => Add(zip, "docs/readme.txt", new byte[] { 2 }));
            Require(StandaloneZip.Inspect(unknown).Choices.Count == 0, "unknown archive stays on manual fallback");
            string ccm = Make(root, "CCM", zip => Add(zip, "metadata.txt", new byte[] { 1 }));
            Require(!Library.MissingCcmMetadata(ccm), "CCM archive stays in CCM pipeline");
            const string odysseyA = "Odyssey/Odyssey/Odyssey Campaign Act I + Launcher/";
            const string odysseyB = "Odyssey/Odyssey/Odyssey Campaign Act II 01-13 + Launcher/";
            string odyssey = Make(root, "Odyssey-KR", zip => {
                Add(zip, odysseyA + "Odyssey Campaign Launcher.SC2Map", mpq);
                Add(zip, odysseyA + "Act I Mission 01.SC2Map", mpq);
                Add(zip, odysseyB + "Odyssey Campaign Launcher.SC2Map", mpq);
                Add(zip, odysseyB + "Act II Mission 01.SC2Map", mpq);
            });
            var odysseyPackage = StandaloneZip.Inspect(odyssey);
            Require(odysseyPackage.Choices.Count == 1 && odysseyPackage.GameFiles.Count == 3 &&
                odysseyPackage.GameFiles.All(f => f.GamePath.StartsWith("Maps/Odyssey/", StringComparison.OrdinalIgnoreCase)) &&
                odysseyPackage.GameFiles.Any(f => f.GamePath == "Maps/Odyssey/Odyssey Campaign Launcher.SC2Map"),
                "Odyssey act wrappers map to one shared launcher and sibling mission maps");
            var odysseyInstalled = StandaloneZip.Install(odysseyPackage, odysseyPackage.Choices[0],
                storage, game, external);
            Require(odysseyInstalled.CreatedFiles == 3 &&
                File.Exists(Path.Combine(game, "Maps", "Odyssey", "Act I Mission 01.SC2Map")) &&
                File.Exists(Path.Combine(game, "Maps", "Odyssey", "Act II Mission 01.SC2Map")) &&
                !Directory.Exists(Path.Combine(game, "Maps", "Odyssey", "Odyssey")),
                "Odyssey installs missions next to the launcher without nested wrappers");
            string directI = Path.Combine(game, "Maps", "Odyssey", "Act I Mission 01 - Hell's Rejects.SC2Map");
            string directII = Path.Combine(game, "Maps", "Odyssey", "Act II Mission 01 - Invasion Day.SC2Map");
            File.WriteAllBytes(directI, mpq); File.WriteAllBytes(directII, mpq);
            string switcher = Path.Combine(game, "Support64", "SC2Switcher_x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(switcher));
            File.WriteAllText(switcher, "not executable; no process started in this fixture");
            var directly = OdysseyMissionDialog.InstalledMissions(game);
            Require(OdysseyMissionDialog.Supports(odysseyInstalled.Campaign, game) &&
                directly.SequenceEqual(new[] { directI, directII }, StringComparer.OrdinalIgnoreCase) &&
                ExternalCampaigns.LaunchInfo(new Campaign { kind = "external", launchMode = "map", launcherMap = directI }, game)
                    .Arguments == "\"" + directI + "\"",
                "Odyssey direct mission fallback offers only verified installed missions and uses the normal SC2Switcher map launch path");
            string legacyGame = Path.Combine(root, "odyssey-old-game");
            string legacyStorage = Path.Combine(root, "odyssey-old-storage");
            Directory.CreateDirectory(legacyGame);
            File.WriteAllText(Path.Combine(legacyGame, "StarCraft II.exe"), "fixture");
            string oldLauncher = Path.Combine(legacyGame, "Maps", "Odyssey", "Odyssey",
                "Odyssey Campaign Act I + Launcher", "Odyssey Campaign Launcher.SC2Map");
            Directory.CreateDirectory(Path.GetDirectoryName(oldLauncher));
            File.WriteAllBytes(oldLauncher, mpq);
            var legacyLinks = new ExternalCampaigns(legacyStorage);
            var legacy = legacyLinks.Save(null, "Odyssey 이전 설치", oldLauncher,
                null, legacyGame, "manual", "map", null, null, Data.Hash(odyssey));
            var migrated = StandaloneZip.Install(odysseyPackage, odysseyPackage.Choices[0],
                legacyStorage, legacyGame, legacyLinks);
            Require(!migrated.AlreadyLinked && migrated.CreatedFiles == 3 &&
                migrated.Campaign.id == legacy.id && legacyLinks.All().Count == 1 &&
                migrated.Campaign.name == legacy.name &&
                migrated.Campaign.launcherMap == Path.Combine(legacyGame, "Maps", "Odyssey", "Odyssey Campaign Launcher.SC2Map") &&
                File.Exists(oldLauncher) && Data.Hash(oldLauncher) == Data.Hash(migrated.Campaign.launcherMap),
                "old Odyssey nested-launcher path reconnects in place without losing the existing campaign identity or deleting its old file");
            string divergentOdyssey = Make(root, "Odyssey-Mismatch", zip => {
                Add(zip, odysseyA + "Odyssey Campaign Launcher.SC2Map", mpq);
                Add(zip, odysseyB + "Odyssey Campaign Launcher.SC2Map", translated);
            });
            Rejected(() => StandaloneZip.Inspect(divergentOdyssey),
                "Odyssey shared launcher duplicates with differing bytes are rejected");
            Require(File.Exists(Path.Combine(game, "Maps", "Odyssey", "Odyssey Campaign Launcher.SC2Map")) &&
                Data.Hash(Path.Combine(game, "Maps", "Odyssey", "Odyssey Campaign Launcher.SC2Map")) ==
                    Data.Hash(Path.Combine(game, "Maps", "Odyssey", "Act I Mission 01.SC2Map")),
                "Odyssey validation leaves installed files intact");
            Console.WriteLine("STANDALONE ZIP PASS: map/EXE launchers, grouped episodes, dependencies, duplicate guard, safe remove-then-install with preserved backups, injected rollback at 3 phases, invalid MPQ and unsafe ZIP rejection; no real game or EXE launched.");
        }
    }
}
