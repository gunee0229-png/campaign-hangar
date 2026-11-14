using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampaignHangar
{
    internal sealed class WorkWindowState
    {
        public string html { get; set; }
        public string theme { get; set; }
        public Dictionary<string,string> colors { get; set; }
        public HubEmbeddedRect rect { get; set; }
        public bool indeterminate { get; set; }
    }

    public sealed partial class Hangar
    {
        Form hubWorkWindow;
        WebView2 hubWorkWindowWeb;
        WorkWindowState hubWorkWindowState;
        bool hubWorkWindowReady,hubWorkWindowClosing,hubWorkWindowQaEnabled;
        TaskCompletionSource<bool> hubWorkWindowQaRendered;
        string WorkWindowPreference {get{return Path.Combine(hubProfileRoot,"work-progress-window.json");}}

        void SaveWorkWindowGeometry()
        {
            if(hubWorkWindow==null||!hubWorkWindow.Visible||hubWorkWindow.WindowState!=FormWindowState.Normal)return;
            try{WindowSize.Save(hubWorkWindow,WorkWindowPreference);}
            catch(Exception error){status.Text="진행창 위치 저장 실패 · "+error.Message;}
        }
        void CloseHubWorkWindow()
        {
            SaveWorkWindowGeometry();
            hubWorkWindowClosing=true;
            try{
                if(hubWorkWindow!=null){hubWorkWindow.Close();hubWorkWindow.Dispose();}
                hubWorkWindow=null;hubWorkWindowWeb=null;hubWorkWindowReady=false;
            }finally{hubWorkWindowClosing=false;}
            if(hubWeb!=null&&!hubWeb.IsDisposed&&hubWeb.CoreWebView2!=null)
                hubWeb.ExecuteScriptAsync("window.UiLab&&UiLab.workWindowAttached(false)");
        }
        async void ShowHubWorkWindow(string json)
        {
            if((!hubProgressActive&&!hubWorkWindowQaEnabled)||!CanShowHubWorkScreen())return;
            var state=Data.Json.Deserialize<WorkWindowState>(json);
            if(state==null||state.rect==null||String.IsNullOrEmpty(state.html)||
                state.html.Length>16000||state.rect.viewWidth<100||state.rect.viewHeight<100||
                state.rect.width<1||state.rect.height<1)return;
            hubWorkWindowState=state;
            if(hubWorkWindow!=null){
                if(hubWorkWindowReady)hubWorkWindowWeb.CoreWebView2.PostWebMessageAsJson(json);
                return;
            }
            var window=new Form {Text="커캠 격납고 · 작업 진행",AutoScaleMode=AutoScaleMode.None,
                StartPosition=FormStartPosition.Manual,MinimumSize=new Size(320,220),
                FormBorderStyle=FormBorderStyle.Sizable,MaximizeBox=false,ShowInTaskbar=true,
                BackColor=GlassTheme.Background};
            hubWorkWindow=window;
            window.Icon=Icon;
            double scaleX=(double)hubWeb.ClientSize.Width/state.rect.viewWidth;
            double scaleY=(double)hubWeb.ClientSize.Height/state.rect.viewHeight;
            window.ClientSize=new Size(Math.Max(300,(int)Math.Round(state.rect.width*scaleX)),
                Math.Max(180,(int)Math.Round(state.rect.height*scaleY)));
            var origin=hubWeb.PointToScreen(new Point((int)Math.Round(state.rect.x*scaleX),
                (int)Math.Round(state.rect.y*scaleY)));
            int border=(window.Width-window.ClientSize.Width)/2;
            WindowSize.Restore(window,new WindowPreference {
                left=origin.X-border,top=origin.Y-(window.Height-window.ClientSize.Height-border),
                width=window.Width,height=window.Height});
            bool restoreWorkWindow=File.Exists(WorkWindowPreference);
            try{WindowSize.Restore(window,WorkWindowPreference);}
            catch(Exception error){status.Text="진행창 위치 복원 실패 · "+error.Message;}
            var web=new WebView2 {Dock=DockStyle.Fill,DefaultBackgroundColor=GlassTheme.Background};
            hubWorkWindowWeb=web;window.Controls.Add(web);
            window.ResizeEnd+=delegate{SaveWorkWindowGeometry();};
            window.FormClosing+=delegate(object sender,FormClosingEventArgs e){
                if(hubWorkWindowClosing||IsDisposed)return;
                e.Cancel=true;
                hubWeb.ExecuteScriptAsync("window.UiLab&&UiLab.showWorkFullScreen()");
            };
            try{
                await web.EnsureCoreWebView2Async(hubWeb.CoreWebView2.Environment);
                if(window.IsDisposed||hubWorkWindow!=window)return;
                var core=web.CoreWebView2;
                core.Settings.AreHostObjectsAllowed=false;
                core.Settings.AreDevToolsEnabled=false;
                core.Settings.AreDefaultContextMenusEnabled=false;
                core.Settings.IsStatusBarEnabled=false;
                core.SetVirtualHostNameToFolderMapping("hangar-live.test",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","ui-hub"),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.AddWebResourceRequestedFilter("https://hangar-live.test/progress.html",CoreWebView2WebResourceContext.Document);
                core.WebResourceRequested+=delegate(object sender,CoreWebView2WebResourceRequestedEventArgs e){
                    if(e.Request.Uri=="https://hangar-live.test/progress.html")
                        e.Response=core.Environment.CreateWebResourceResponse(
                            typeof(Hangar).Assembly.GetManifestResourceStream("CampaignHangar.Progress"),200,"OK",
                            "Content-Type: text/html; charset=utf-8");
                };
                core.NavigationStarting+=delegate(object sender,CoreWebView2NavigationStartingEventArgs e){
                    if(e.Uri!="https://hangar-live.test/progress.html")e.Cancel=true;
                };
                core.NewWindowRequested+=delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;};
                core.PermissionRequested+=delegate(object sender,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
                core.WebMessageReceived+=delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e){
                    if(e.Source!="https://hangar-live.test/progress.html"||hubWorkWindow!=window)return;
                    string action=e.TryGetWebMessageAsString();
                    if(action=="ready"){
                        hubWorkWindowReady=true;
                        core.PostWebMessageAsJson(Data.Json.Serialize(hubWorkWindowState));
                    }else if(action=="rendered"){
                        if(!window.Visible){
                            window.Show(this);
                            if(!restoreWorkWindow){
                                // Showing on another monitor finalizes the DPI-dependent window borders.
                                var clientOrigin=window.PointToScreen(Point.Empty);
                                WindowSize.Restore(window,new WindowPreference {
                                    left=window.Left+origin.X-clientOrigin.X,top=window.Top+origin.Y-clientOrigin.Y,
                                    width=window.Width,height=window.Height});
                            }
                        }
                        hubWeb.ExecuteScriptAsync("window.UiLab&&UiLab.workWindowAttached(true)");
                        if(hubWorkWindowQaRendered!=null)hubWorkWindowQaRendered.TrySetResult(true);
                    }else if(action=="expand"){
                        hubWeb.ExecuteScriptAsync("window.UiLab&&UiLab.showWorkFullScreen()");
                    }else if(action=="cancel"&&hubProgressActive){
                        if(hubWorkCancellation!=null&&!hubWorkCancellation.IsCancellationRequested)hubWorkCancellation.Cancel();
                        else if(hubExternalCancel!=null)hubExternalCancel();
                    }
                };
                core.Navigate("https://hangar-live.test/progress.html");
            }catch(Exception error){
                if(window.IsDisposed||hubWorkWindow!=window)return;
                CloseHubWorkWindow();
                status.Text="별도 진행창을 열지 못했습니다 · "+error.Message;
            }
        }

        async Task VerifyDetachedWorkWindowAsync(string output)
        {
            if(!isolated)throw new InvalidOperationException("Detached work QA must use an isolated profile.");
            hubWorkWindowQaEnabled=true;
            Task cleanup=null;
            try{
                hubWorkWindowQaRendered=new TaskCompletionSource<bool>();
                await hubWeb.ExecuteScriptAsync(@"UiLab.setView('all');UiLab.finishBoot();
                    UiLab.setWorkProgress({sequence:90000,workId:90000,active:true,fullscreen:true,browse:false,
                        title:'격리 검수 · 외부 진행창',stage:'실측 파일 확인',completed:1,total:4,cancelable:true});
                    document.getElementById('bootBrowse').click();");
                if(await Task.WhenAny(hubWorkWindowQaRendered.Task,Task.Delay(15000))!=hubWorkWindowQaRendered.Task)
                    throw new InvalidOperationException("Detached work window did not render.");
                if(hubWorkWindow==null||!hubWorkWindow.TopLevel||hubWorkWindow.Owner!=this)
                    throw new InvalidOperationException("Work progress must be an independent owned window.");
                var initial=hubWorkWindowState.rect;
                var expected=hubWeb.PointToScreen(new Point((int)Math.Round(initial.x*(double)hubWeb.ClientSize.Width/initial.viewWidth),
                    (int)Math.Round(initial.y*(double)hubWeb.ClientSize.Height/initial.viewHeight)));
                var actual=hubWorkWindow.PointToScreen(Point.Empty);
                var initialArea=Screen.FromPoint(expected).WorkingArea;
                if(initialArea.Contains(new Rectangle(expected,hubWorkWindow.ClientSize))&&
                    (Math.Abs(expected.X-actual.X)>1||Math.Abs(expected.Y-actual.Y)>1))
                    throw new InvalidOperationException("First detached window did not use the fullscreen panel position.");
                var area=Screen.FromControl(this).WorkingArea;
                hubWorkWindow.Bounds=new Rectangle(area.Left+10,area.Top+10,420,280);
                var remembered=hubWorkWindow.Bounds;
                if(Bounds.Contains(remembered))throw new InvalidOperationException("Work window could not move outside its owner.");
                SaveWorkWindowGeometry();
                var progress=Data.Json.Deserialize<string>(await hubWorkWindowWeb.ExecuteScriptAsync(@"(function(){
                    var center=document.querySelector('.boot-center').getBoundingClientRect();
                    var percent=document.getElementById('bootPercent').getBoundingClientRect();
                    return document.getElementById('bootPercent').textContent==='25%'&&Math.abs(center.top)<1&&
                        center.right<=innerWidth+1&&percent.bottom<=innerHeight?'ok':'progress-position-or-value';
                })()"));
                if(progress!="ok")throw new InvalidOperationException("Detached window layout: "+progress);
                using(var image=File.Create(Path.Combine(output,"detached-work-window.png")))
                    await hubWorkWindowWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
                CloseHubWorkWindow();
                hubWorkWindowQaRendered=new TaskCompletionSource<bool>();
                await hubWeb.ExecuteScriptAsync(@"UiLab.setWorkProgress({sequence:90001,active:false});
                    UiLab.setWorkProgress({sequence:90002,workId:90001,active:true,fullscreen:true,browse:true,
                        title:'격리 검수 · 다시 열린 진행창',stage:'크기 위치 복원',completed:2,total:4});");
                if(await Task.WhenAny(hubWorkWindowQaRendered.Task,Task.Delay(15000))!=hubWorkWindowQaRendered.Task)
                    throw new InvalidOperationException("Detached work window did not reopen.");
                if(hubWorkWindow.Bounds!=remembered)throw new InvalidOperationException("Detached work geometry did not persist.");
                hubWorkWindowQaRendered=new TaskCompletionSource<bool>();
                await hubWeb.ExecuteScriptAsync(@"UiLab.setWorkProgress({sequence:90003,workId:90001,active:true,fullscreen:true,browse:true,
                    title:'격리 검수 · 다시 열린 진행창',stage:'진행률 갱신',completed:3,total:4});");
                if(await Task.WhenAny(hubWorkWindowQaRendered.Task,Task.Delay(15000))!=hubWorkWindowQaRendered.Task)
                    throw new InvalidOperationException("Detached work update did not render.");
                if(Data.Json.Deserialize<string>(await hubWorkWindowWeb.ExecuteScriptAsync("document.getElementById('bootPercent').textContent"))!="75%")
                    throw new InvalidOperationException("Detached work progress stopped updating.");
                Console.WriteLine("DETACHED WORK PASS: independent movable/resizable window, actual 25%/75%, saved bounds restored across close/reopen.");
            }finally{
                CloseHubWorkWindow();hubWorkWindowQaEnabled=false;hubWorkWindowQaRendered=null;
                cleanup=hubWeb.ExecuteScriptAsync("UiLab.setWorkProgress({sequence:90004,active:false})");
            }
            if(cleanup!=null)await cleanup;
        }
    }
}
