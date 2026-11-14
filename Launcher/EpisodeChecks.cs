using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class EpisodeChecks
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("EPISODE FAIL: " + message);
            Console.WriteLine("EPISODE PASS: " + message);
        }
        static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch { rejected = true; }
            Check(rejected, message);
        }
        public static void Run(string root)
        {
            if (Directory.Exists(root)) throw new InvalidOperationException("Episode checks require a new isolated directory.");
            Directory.CreateDirectory(root);
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string game = Path.Combine(root, "game");
            Directory.CreateDirectory(game); File.WriteAllText(Path.Combine(game, "StarCraft II.exe"), "fixture-not-executable");
            var paths = Enumerable.Range(1, 3).Select(i => Path.Combine(game, "Maps", "격리 에피소드", "Episode " + i + ".SC2Map")).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(paths[0]));
            foreach (string map in paths) File.WriteAllBytes(map, new byte[] { 77, 80, 81, 26, 1, 2, 3, 4 });
            var hashes = paths.Select(Data.Hash).ToArray();
            using(var dialog=new EpisodeDialog("에피소드 선택 검사",3,new[]{paths[0],null,paths[2]}))
            {
                dialog.Show();Application.DoEvents();
                var buttons=dialog.Controls.OfType<FlowLayoutPanel>().Single().Controls.OfType<MetalButton>().ToArray();
                Check(buttons.Length==3&&buttons[0].Enabled&&!buttons[1].Enabled&&buttons[2].Enabled,
                    "themed episode dialog offers connected episodes and disables missing maps");
                buttons[2].PerformClick();
                Check(dialog.Episode==3&&dialog.DialogResult==DialogResult.OK,
                    "themed episode choice returns exactly the selected episode without starting a process");
                dialog.Close();
            }
            string switcher = Path.Combine(game, "Support64", "SC2Switcher_x64.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(switcher));
            using (var compiler = CodeDomProvider.CreateProvider("CSharp"))
            {
                var options = new CompilerParameters { GenerateExecutable = true, OutputAssembly = switcher,
                    TempFiles = new TempFileCollection(root, false) };
                options.ReferencedAssemblies.Add("System.dll");
                var result = compiler.CompileAssemblyFromSource(options,
                    "class Fixture { static void Main(string[] args) { System.IO.File.WriteAllLines(\"episode-request.txt\",args); } }");
                Check(!result.Errors.HasErrors, "compile harmless switcher stand-in; no real game executable used");
            }
            var store = new ExternalCampaigns(root);
            var pending = store.Save(null, "안티오크 연대기 · 격리 예시", null, null, game,
                "manual", "episodes", null, new string[3]);
            Check(pending.episodeMaps.Length == 3 && pending.episodeMaps.All(p => p == null) &&
                new ExternalCampaigns(root).All().Single().launchMode == "episodes",
                "uninstalled campaign retains three unlinked episode slots without invented paths");
            Reject(() => ExternalCampaigns.LaunchInfo(pending, game, 1), "unlinked episode cannot produce a launch request");
            string manifest = Path.Combine(root, "external-campaigns.json"), before = Data.Hash(manifest);
            Reject(() => store.Save(pending.id, pending.name, null, null, game, "manual", "episodes", null,
                new[] { paths[0], paths[0], paths[2] }), "same shared selector map cannot masquerade as separate episode targets");
            Reject(() => store.Save(pending.id, pending.name, null, null, game, "manual", "episodes", null,
                new[] { paths[0], paths[1] }), "episode schema requires exactly three slots");
            Reject(() => store.Save(pending.id, pending.name, null, null, game, "manual", "episodes", null,
                new[] { paths[0], Path.Combine(root, "outside.SC2Map"), paths[2] }), "outside game Maps target rejected");
            Check(Data.Hash(manifest) == before, "invalid connections leave existing episode settings unchanged");
            VerifyUI(root, store, pending, game, "pending");
            var connected = store.Save(pending.id, pending.name, null, null, game, "manual", "episodes", null, paths);
            Check(connected.id == pending.id && connected.episodeMaps.SequenceEqual(paths), "connecting maps preserves campaign identity and order");
            Reject(() => ExternalCampaigns.LaunchInfo(connected, game), "generic play cannot silently choose an episode");
            Reject(() => ExternalCampaigns.LaunchInfo(connected, game, 4), "out-of-range episode rejected");
            Reject(() => ExternalCampaigns.LaunchInfo(new Campaign { kind = "external", launchMode = "map", launcherMap = paths[0] }, game, 1),
                "ordinary campaign cannot be launched through an episode selector");
            for (int i = 1; i <= 3; i++)
            {
                var request = ExternalCampaigns.LaunchInfo(connected, game, i);
                Check(request.FileName == switcher && request.Arguments == "\"" + paths[i - 1] + "\"" && !request.UseShellExecute,
                    "episode " + i + " selects only its quoted map");
                using (var process = Process.Start(request))
                    Check(process != null && process.WaitForExit(10000) && process.ExitCode == 0, "stand-in accepts episode " + i + " request");
                Check(File.ReadAllLines(Path.Combine(game, "episode-request.txt")).SequenceEqual(new[] { paths[i - 1] }),
                    "one click opens exactly one selected episode, not all three");
            }
            VerifyUI(root, store, connected, game, "connected");
            File.Move(paths[1], paths[1] + ".held");
            try
            {
                Reject(() => ExternalCampaigns.LaunchInfo(connected, game, 2), "removed map blocks that episode at execution time");
                Check(ExternalCampaigns.LaunchInfo(connected, game, 1).Arguments.Contains(paths[0]), "another episode remains independently available");
                VerifyUI(root, store, connected, game, "missing-second");
            }
            finally { File.Move(paths[1] + ".held", paths[1]); }
            Check(paths.Select(Data.Hash).SequenceEqual(hashes), "all source maps preserved");
            store.Forget(connected.id);
            Check(store.All().Count == 0 && paths.All(File.Exists), "forget removes connection only, not installed episodes");
            Console.WriteLine("EPISODE COMPLETE: isolated metadata, three buttons and harmless stand-in only; no Antioch installer downloaded or game run.");
        }
        static void VerifyUI(string root, ExternalCampaigns store, Campaign campaign, string game, string state)
        {
            var calls = new List<int>(); var library = new Library(Path.Combine(root, "ui-library"));
            using (var image = new Bitmap(320, 180))
            using (var form = new Form { ClientSize = new Size(1440, 1000) })
            using (var view = new InstalledView(library, c => Task.FromResult<Image>(image), c => {
                    throw new InvalidOperationException("Generic apply must not run for an episode campaign.");
                }, () => { }, external: store.All, editExternal: c => { },
                launchEpisode: (c, episode) => {
                    Check(c.id == campaign.id, "episode action retains selected campaign");
                    calls.Add(episode); return Task.FromResult(false);
                }))
            {
                form.Controls.Add(view); view.Reveal(new Campaign[0], campaign); form.Show(); Application.DoEvents();
                view.VerifyExternalShelfAlongsideDedicated(campaign);
                foreach (int width in new[] { 1440, 980 })
                {
                    form.ClientSize = new Size(width, 1000); Application.DoEvents();
                    int count = calls.Count;
                    var expected = Enumerable.Range(1, 3).Where(i => !String.IsNullOrWhiteSpace(campaign.episodeMaps[i - 1]) &&
                        File.Exists(campaign.episodeMaps[i - 1])).ToArray();
                    view.VerifyEpisodeActions(campaign, false);
                    Check(calls.Count == count, "opening the episode selector does not launch a mission");
                    view.VerifyEpisodeSelectionForTest();
                    Check(calls.Skip(count).SequenceEqual(expected), "selected episode choices route exactly their connected maps");
                    Capture(form, Path.Combine(root, state + "-" + width + ".png"));
                    count = calls.Count; view.SetBusy(true); view.VerifyEpisodeActions(campaign, true);
                    Check(calls.Count == count, "busy state dispatches no episode");
                    view.SetBusy(false);
                }
                form.Close();
            }
            if (state != "missing-second")
                using (var dialog = new ExternalCampaignDialog(campaign, game))
                {
                    string before = Data.Hash(Path.Combine(root, "external-campaigns.json"));
                    dialog.Show(); Application.DoEvents(); dialog.VerifyEpisodeConnections(campaign.episodeMaps, game);
                    Capture(dialog, Path.Combine(root, state + "-editor.png"));
                    dialog.DialogResult = DialogResult.Cancel; dialog.Close();
                    Check(Data.Hash(Path.Combine(root, "external-campaigns.json")) == before, "editor cancellation preserves episode map connections");
                }
        }
        static void Capture(Form form, string path)
        {
            using (var image = new Bitmap(form.Width, form.Height))
            { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(path); }
        }
    }
}
