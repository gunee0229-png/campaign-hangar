using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class DedicatedVoiceChecks
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("VOICE FAIL: " + message);
            Console.WriteLine("VOICE PASS: " + message);
        }
        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (Exception) { Check(true, message); return; }
            throw new InvalidOperationException("VOICE FAIL: " + message);
        }
        public static void Run(string source)
        {
            string root = Path.Combine(Path.GetTempPath(), "scevo-voice-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Console.WriteLine("VOICE_ROOT " + root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string korean = Path.Combine(source, "sc-evo-ko.json"), english = Path.Combine(source, "sc-evo-en.json");
            var ko = Data.Read<DedicatedRelease>(korean); var en = Data.Read<DedicatedRelease>(english);
            DedicatedLibrary.ValidateRelease(ko); DedicatedLibrary.ValidateRelease(en);
            Check(ko.files.Count == 94 && en.files.Count == 94 && en.voice == "en" &&
                ko.packages[0].sha256 == en.packages[0].sha256 &&
                ko.files.Where(f => en.files.Single(e => e.path == f.path).sha256 != f.sha256).Select(f => f.path).OrderBy(p => p)
                    .SequenceEqual(new[] { "Mods/SCMRassets.SC2Mod", "Mods/SCMRlocal.SC2Mod" }),
                "the pinned English variant changes exactly the two voice mods, not the main campaign");
            var sources = Data.Read<Dictionary<string, string>>(Path.Combine(source, "local-sources.json"));
            string storage = Path.Combine(root, "storage"), game = Path.Combine(root, "game");
            Directory.CreateDirectory(Path.Combine(game, "Support64")); Directory.CreateDirectory(Path.Combine(game, "Mods"));
            File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            File.WriteAllText(Path.Combine(game, "Support64/SC2Switcher_x64.exe"), "fixture-not-executable");
            string original = Data.Under(game, "Mods/SCMRlocal.SC2Mod");
            File.WriteAllBytes(original, new byte[] { 77, 80, 81, 26, 4, 3, 2, 1 }); string originalHash = Data.Hash(original);
            string bank = Path.Combine(root, "preserved.SC2Bank"); File.WriteAllText(bank, "preserved-bank");
            var library = new DedicatedLibrary(storage);
            var requested = new List<string>(); object gate = new object();
            Action<DedicatedRelease, string, CancellationToken> install = (release, manifest, cancellation) => {
                string work = Path.Combine(root, "request-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(work);
                library.InstallSnapshot(release, Data.Hash(manifest), work, game, null, cancellation,
                    (url, target, bytes, hash, progress, token) => {
                        lock (gate) requested.Add(url);
                        Data.Copy(sources[url], target);
                    });
            };
            var campaign = CampaignFixture(korean, english);
            Check(DedicatedLibrary.VoiceVariant(campaign, true).dedicatedManifestSha256 == Data.Hash(english) &&
                DedicatedLibrary.VoiceVariant(campaign, false).dedicatedManifestSha256 == Data.Hash(korean),
                "explicit voice choice selects only the independently pinned manifest");
            install(ko, korean, CancellationToken.None);
            Check(requested.Count == 2 && ko.files.All(f => Data.Hash(Data.Under(game, f.path)) == f.sha256) &&
                library.Voice(campaign) == "ko", "legacy Korean snapshot remains the default and installs all 94 verified files");
            requested.Clear(); install(en, english, CancellationToken.None);
            Check(requested.SequenceEqual(new[] { en.packages[1].url }) &&
                en.files.All(f => Data.Hash(Data.Under(game, f.path)) == f.sha256),
                "Korean to English requests only the English voice ZIP and preserves every main file");
            Check(new DedicatedLibrary(storage).Voice(campaign) == "en" && !library.NeedsUpdate(campaign),
                "selected English voice survives storage reload without a false Korean update notification");
            requested.Clear(); install(ko, korean, CancellationToken.None);
            Check(requested.SequenceEqual(new[] { ko.packages[1].url }) &&
                ko.files.All(f => Data.Hash(Data.Under(game, f.path)) == f.sha256) && !library.NeedsUpdate(campaign),
                "English to Korean requests only the Korean voice ZIP and restores the pinned Korean pair");
            string state = Data.Under(storage, "dedicated/installed.json"), stateHash = Data.Hash(state);
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); requested.Clear();
                Reject(() => install(en, english, cancellation.Token), "cancelled voice change is rejected before transfer");
                Check(requested.Count == 0 && Data.Hash(state) == stateHash && library.Voice(campaign) == "ko",
                    "cancellation preserves the existing voice and installed record");
            }
            File.WriteAllBytes(original, new byte[] { 77, 80, 81, 26, 9, 9, 9, 9 }); string edited = Data.Hash(original);
            Reject(() => install(en, english, CancellationToken.None), "voice selection does not bypass external-change protection");
            Check(Data.Hash(original) == edited && Data.Hash(state) == stateHash,
                "rejected voice change preserves the user edit and active voice record");
            using (var archive = ZipFile.OpenRead(sources[ko.packages[1].url]))
            using (var input = archive.GetEntry(ko.packages[1].entries.Single(e => e.Value == "Mods/SCMRlocal.SC2Mod").Key).Open())
            using (var output = File.Create(original)) input.CopyTo(output);
            VerifyUI(root, campaign);
            library.Uninstall(campaign.id, game);
            Check(Data.Hash(original) == originalHash && File.ReadAllText(bank) == "preserved-bank" &&
                library.Current().installed.Count == 0 &&
                ko.files.Where(f => f.path != "Mods/SCMRlocal.SC2Mod").All(f => !File.Exists(Data.Under(game, f.path))),
                "uninstall after voice switching restores the pre-install original and preserves the bank");
            Console.WriteLine("VOICE COMPLETE: original publisher ZIPs only, isolated game, no network or real game mutation.");
        }
        static Campaign CampaignFixture(string korean, string english)
        {
            return new Campaign { id = "sc-evo", kind = "dedicated", name = "매스리콜 · SC Evo",
                sourceAuthor = "Mass Recall 제작팀", evoDeveloper = "PotenKing", creditLabel = "원작",
                creatorUrl = "https://potenking.blogspot.com/2024/04/httpsdrive.html", creatorLabel = "제작자 사이트",
                dedicatedManifestUrl = DedicatedLibrary.ReleasePrefix + "fixture/sc-evo-ko.json",
                dedicatedManifestSha256 = Data.Hash(korean), dedicatedInstallerVersion = 4,
                dedicatedEnglishManifestUrl = DedicatedLibrary.ReleasePrefix + "fixture/sc-evo-en.json",
                dedicatedEnglishManifestSha256 = Data.Hash(english) };
        }
        public static void RunUI(string source)
        {
            string root = Path.Combine(Path.GetTempPath(), "scevo-voice-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root); Console.WriteLine("VOICE_UI_ROOT " + root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            VerifyUI(root, CampaignFixture(Path.Combine(source, "sc-evo-ko.json"), Path.Combine(source, "sc-evo-en.json")));
        }
        static void VerifyUI(string root, Campaign campaign)
        {
            bool installed = false; int links = 0, changes = 0;
            using (var image = new Bitmap(320, 180))
            using (var form = new Form { ClientSize = new Size(1440, 1080) })
            using (var view = new InstalledView(new Library(Path.Combine(root, "ui")), c => Task.FromResult<Image>(image),
                c => Task.FromResult(false), () => { }, external: () => new Campaign[0],
                dedicatedInstalled: c => installed, changeVoice: c => changes++, voiceLabel: c => "영어", openCreator: c => links++))
            {
                form.Controls.Add(view); form.Show();
                foreach (int width in new[] { 1440, 980 })
                {
                    form.ClientSize = new Size(width, 1080); view.Reload(new[] { campaign });
                    installed = false; view.VerifyCreatorVoiceActions(campaign, false, false);
                    installed = true; view.VerifyCreatorVoiceActions(campaign, true, false);
                    view.VerifyCreatorVoiceActions(campaign, true, true);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.Combine(root, "voice-credits-" + width + ".png")); }
                }
                Check(links == 4 && changes == 2, "creator links and voice actions route only explicit user clicks");
            }
            foreach (bool english in new[] { false, true })
            foreach (bool accepted in new[] { false, true })
                using (var dialog = new DedicatedVoiceDialog(english, true))
                {
                    dialog.Show(); Application.DoEvents(); TextLayout.VerifyLabels(dialog);
                    Check(dialog.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<Label>()
                        .Select(label => label.Text).SequenceEqual(new[] { "설치할 음성을 선택하세요." }),
                        "voice dialog only shows the choice prompt without implementation or reassurance paragraphs");
                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                    { dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size)); bitmap.Save(Path.Combine(root, "voice-dialog-" + english + "-" + accepted + ".png")); }
                    dialog.VerifyChoice(english, accepted);
                    Check(dialog.DialogResult == (accepted ? DialogResult.OK : DialogResult.Cancel),
                        "Korean/English selection and cancellation return distinct explicit results");
                }
        }
    }
}
