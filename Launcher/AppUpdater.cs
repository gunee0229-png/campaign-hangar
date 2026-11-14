using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class AppUpdater
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--self-test") return AppUpdateChecks.Run();
            Application.EnableVisualStyles();
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            try
            {
                if (args.Length != 3) throw new ArgumentException("앱에서 업데이트를 시작하세요.");
                string workspace = AppDomain.CurrentDomain.BaseDirectory;
                string target = Path.GetFullPath(args[1]), zip = Path.Combine(workspace, "release.zip");
                var release = AppUpdate.Parse(File.ReadAllText(Path.Combine(workspace, "release.json")));
                if (!AppUpdate.IsNewer(release)) throw new InvalidDataException("새 버전이 아닙니다.");
                using (var parent = Process.GetProcessById(Int32.Parse(args[0])))
                {
                    if (!String.Equals(Path.GetFullPath(parent.MainModule.FileName), Path.Combine(target, "CampaignHangar.exe"), StringComparison.OrdinalIgnoreCase) ||
                        parent.StartTime.ToUniversalTime().Ticks != Int64.Parse(args[2]))
                        throw new InvalidDataException("업데이트할 실행 앱을 확인할 수 없습니다.");
                    // Parent stays alive until this acknowledgement exists.
                    File.WriteAllText(Path.Combine(workspace, "ready"), "ready");
                    if (!parent.WaitForExit(120000)) throw new IOException("앱이 종료되지 않았습니다. 업데이트를 취소합니다.");
                }
                string staged = Path.Combine(workspace, "staged");
                AppUpdate.Extract(release, zip, staged);
                string backup = Path.Combine(workspace, "previous-version");
                AppUpdate.Replace(staged, target, backup);
                Process.Start(new ProcessStartInfo(Path.Combine(target, "CampaignHangar.exe")) { WorkingDirectory = target });
                return 0;
            }
            catch (Exception error)
            {
                MessageBox.Show("앱 업데이트를 완료하지 못했습니다.\n" + error.Message +
                    "\n\n기존 파일 백업과 다운로드 자료는 다음 폴더에 보관됩니다.\n" + AppDomain.CurrentDomain.BaseDirectory,
                    "앱 업데이트 실패", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
    }
}
