using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CampaignHangar
{
    public static class ArchiveRuntime
    {
        public const string DownloadPage = "https://developer.microsoft.com/microsoft-edge/webview2/consumer/";
        public const string Bootstrapper = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        public static bool Installed()
        {
            return Installed(RegistryHive.LocalMachine, RegistryView.Registry32) ||
                Installed(RegistryHive.CurrentUser, RegistryView.Default);
        }
        static bool Installed(RegistryHive hive, RegistryView view)
        {
            using (var root = RegistryKey.OpenBaseKey(hive, view))
            using (var key = root.OpenSubKey(@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}"))
            {
                Version version;
                return key != null && Version.TryParse(key.GetValue("pv") as string, out version) && version > new Version(0, 0, 0, 0);
            }
        }
        public static async Task<bool> InstallAsync(IWin32Window owner)
        {
            if (Installed()) return true;
            if (MessageBox.Show(owner, "아카이브를 앱 안에서 열려면 Microsoft WebView2가 필요합니다.\nMicrosoft 공식 설치 파일을 다운로드하고 실행할까요?\n런타임은 다른 앱과 공유하며 자동 보안 업데이트를 받습니다.\n\n설치 조건과 개인정보 안내:\n" + DownloadPage,
                "WebView2 설치 동의", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return false;
            string folder = Path.Combine(Path.GetTempPath(), "campaign-webview-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string installer = Path.Combine(folder, "MicrosoftEdgeWebview2Setup.exe");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            using (var client = new WebClient()) await client.DownloadFileTaskAsync(new Uri(Bootstrapper), installer);
            VerifyMicrosoftSignature(installer);
            var process = Process.Start(new ProcessStartInfo(installer, "/install") { UseShellExecute = true });
            await Task.Run(() => process.WaitForExit());
            int code = process.ExitCode; process.Dispose();
            if (!Installed()) throw new InvalidOperationException("WebView2 설치를 확인하지 못했습니다. 설치 종료 코드: " + code + "\n공식 설치 안내: " + DownloadPage);
            return true;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TrustFile
        {
            public uint size;
            [MarshalAs(UnmanagedType.LPWStr)] public string path;
            public IntPtr file, subject;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct TrustData
        {
            public uint size;
            public IntPtr policy, sip;
            public uint ui, revocation, choice;
            public IntPtr file;
            public uint action;
            public IntPtr state;
            public IntPtr url;
            public uint flags, context;
        }
        [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr window, [In] ref Guid action, [In] ref TrustData data);
        internal static void VerifyMicrosoftSignature(string file)
        {
            var info = new TrustFile { size = (uint)Marshal.SizeOf(typeof(TrustFile)), path = file };
            IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TrustFile)));
            Marshal.StructureToPtr(info, memory, false);
            try
            {
                var data = new TrustData { size = (uint)Marshal.SizeOf(typeof(TrustData)), ui = 2, choice = 1, file = memory, flags = 0x80 };
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                if (result != 0) throw new InvalidOperationException("설치 파일의 Windows 신뢰 검증에 실패했습니다: " + result);
                using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(file)))
                    if (certificate.GetNameInfo(X509NameType.SimpleName, false) != "Microsoft Corporation")
                        throw new InvalidOperationException("설치 파일의 서명자가 Microsoft Corporation이 아닙니다.");
            }
            finally { Marshal.DestroyStructure(memory, typeof(TrustFile)); Marshal.FreeHGlobal(memory); }
        }
    }
}
