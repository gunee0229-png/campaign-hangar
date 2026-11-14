using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CampaignHangar
{
    static class Installer
    {
        static AppRelease Unpack(string workspace)
        {
            var assembly=Assembly.GetExecutingAssembly();
            AppRelease release;
            using(var input=new StreamReader(assembly.GetManifestResourceStream("release.json")))
                release=AppUpdate.Parse(input.ReadToEnd().TrimStart('\uFEFF'));
            string zip=Path.Combine(workspace,"release.zip");
            using(var input=assembly.GetManifestResourceStream("release.zip"))
            using(var output=File.Create(zip))AppUpdate.CopyBounded(input,output,release.size);
            AppUpdate.Extract(release,zip,Path.Combine(workspace,"app"));
            return release;
        }
        static void Install(string source,string target,string backup)
        {
            if(Directory.Exists(target))
                AppUpdate.Replace(source,target,backup);
            else {
                Directory.CreateDirectory(target);
                foreach(string name in AppUpdate.Files){
                    string destination=Path.Combine(target,name);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(Path.Combine(source,name),destination,false);
                }
            }
        }
        [STAThread]
        static int Main(string[] args)
        {
            string workspace=Path.Combine(Path.GetTempPath(),"hangar-installer-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try {
                var release=Unpack(workspace);
                if(args.Length==1&&args[0]=="--verify-package"){
                    Console.WriteLine("INSTALLER PACKAGE PASS: "+release.version+" / "+AppUpdate.Files.Length+" files");
                    return 0;
                }
                if(args.Length==2&&args[0]=="--install-fixture"){
                    string fixture=Path.GetFullPath(args[1]);
                    if(Directory.Exists(fixture)||File.Exists(fixture))throw new IOException("Fixture must be a new directory.");
                    Install(Path.Combine(workspace,"app"),fixture,Path.Combine(workspace,"backup"));
                    Console.WriteLine("INSTALLER FIXTURE PASS: "+fixture);return 0;
                }
                if(args.Length!=0)throw new ArgumentException("지원하지 않는 설치 옵션입니다.");
                Application.EnableVisualStyles();
                string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","CampaignHangar");
                if(MessageBox.Show("커캠 격납고 v"+release.version+"를 설치합니다.\n\n"+target+
                    "\n\n기존 캠페인과 개인 설정은 변경하지 않습니다.\n실행 중인 격납고를 닫은 뒤 진행해 주세요.",
                    "커캠 격납고 설치",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return 0;
                Install(Path.Combine(workspace,"app"),target,Path.Combine(workspace,"previous-version"));
                Type shellType=Type.GetTypeFromProgID("WScript.Shell");
                object shell=Activator.CreateInstance(shellType);
                string menu=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"커캠 격납고.lnk");
                object shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{menu});
                Type shortcutType=shortcut.GetType();
                shortcutType.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(target,"CampaignHangarSetup.exe")});
                shortcutType.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{target});
                shortcutType.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
                Process.Start(Path.Combine(target,"CampaignHangarSetup.exe"));
                return 0;
            }catch(Exception error){
                Console.Error.WriteLine(error);
                if(args.Length==0)MessageBox.Show("설치하지 못했습니다.\n"+error.Message+
                    "\n\n복구 자료: "+workspace,"커캠 격납고 설치",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
