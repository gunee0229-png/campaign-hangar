using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CampaignHangar
{
    static class Setup
    {
        [STAThread]
        static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            {
                if (key == null || Convert.ToInt32(key.GetValue("Release", 0)) < 528040)
                {
                    if (args.Length == 1 && args[0] == "--check") { Console.WriteLine(".NET Framework 4.8 required"); return 48; }
                    MessageBox.Show("이 앱은 .NET Framework 4.8이 필요합니다.\nWindows 업데이트로 설치한 뒤 다시 실행해 주세요.", "필수 구성요소");
                    return 48;
                }
            }
            if (args.Length == 1 && args[0] == "--check")
            { Console.WriteLine("WebView2 installed: " + ArchiveRuntime.Installed()); return ArchiveRuntime.Installed() ? 0 : 20; }
            Application.EnableVisualStyles();
            using (var form = new Form { Text = "캠페인 격납고 시작 준비", Width = 520, Height = 180, StartPosition = FormStartPosition.CenterScreen })
            {
                var label = new Label { Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleCenter, Text = "필수 구성요소를 확인하고 있습니다." };
                form.Controls.Add(label);
                form.Shown += async delegate
                {
                    try
                    {
                        if (!ArchiveRuntime.Installed() && !await ArchiveRuntime.InstallAsync(form))
                        {
                            label.Text = "WebView2 설치를 진행하지 않았습니다.\n앱의 캠페인 기능은 사용할 수 있으며 아카이브에서 나중에 설치할 수 있습니다.";
                        }
                        Process.Start(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CampaignHangar.exe"));
                        form.Close();
                    }
                    catch (Exception error) { label.Text = "준비 실패: " + error.Message; }
                };
                Application.Run(form);
            }
            return 0;
        }
    }
}
