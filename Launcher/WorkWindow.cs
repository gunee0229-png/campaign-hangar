using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
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
        public long workId { get; set; }
    }

    // An undecorated panel must still support ordinary Windows edge resizing
    // and moving; the thin native inset keeps hit testing outside WebView2.
    internal sealed class WorkProgressFrame : Form
    {
        const int WM_NCHITTEST = 0x84, WM_NCLBUTTONDOWN = 0xA1;
        const int HTCLIENT = 1, HTCAPTION = 2;
        const int CornerRadiusAt96Dpi = 18;
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        internal WorkProgressFrame()
        {
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(6);
            ShowIcon = false;
            MaximizeBox = false;
            MinimizeBox = false;
        }

        // A CSS radius alone cannot remove the native square corners of this
        // independent window. Clip the actual HWND and refresh after resizing.
        void UpdateRoundedRegion()
        {
            if (IsDisposed || !IsHandleCreated || Width < 2 || Height < 2) return;
            int radius = Math.Min((int)Math.Round(CornerRadiusAt96Dpi * DeviceDpi / 96.0),
                Math.Min(Width, Height) / 2);
            using (var outline = GlassTheme.Round(new Rectangle(0, 0, Width, Height), radius))
            {
                var previous = Region;
                Region = new Region(outline);
                if (previous != null) previous.Dispose();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            UpdateRoundedRegion();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRoundedRegion();
        }

        internal void BeginMove()
        {
            if (!IsHandleCreated || IsDisposed) return;
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != WM_NCHITTEST || m.Result.ToInt64() != HTCLIENT ||
                WindowState != FormWindowState.Normal) return;
            long lparam = m.LParam.ToInt64();
            var p = PointToClient(new Point(unchecked((short)(lparam & 0xffff)),
                unchecked((short)((lparam >> 16) & 0xffff))));
            const int edge = 8;
            bool left = p.X < edge, right = p.X >= ClientSize.Width - edge;
            bool top = p.Y < edge, bottom = p.Y >= ClientSize.Height - edge;
            if (top && left) m.Result = new IntPtr(13);
            else if (top && right) m.Result = new IntPtr(14);
            else if (bottom && left) m.Result = new IntPtr(16);
            else if (bottom && right) m.Result = new IntPtr(17);
            else if (left) m.Result = new IntPtr(10);
            else if (right) m.Result = new IntPtr(11);
            else if (top) m.Result = new IntPtr(12);
            else if (bottom) m.Result = new IntPtr(15);
        }
    }

    public sealed partial class Hangar
    {
        WorkProgressFrame hubWorkWindow;
        WebView2 hubWorkWindowWeb;
        WorkWindowState hubWorkWindowState;
        long hubWorkWindowDismissedWorkId = -1;
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
        void DismissHubWorkWindow()
        {
            // The small window may be closed without cancelling the real work.
            // Suppress only this operation's subsequent progress updates.
            if (hubWorkWindowState != null)
                hubWorkWindowDismissedWorkId = hubWorkWindowState.workId;
            CloseHubWorkWindow();
            if (hubWeb != null && !hubWeb.IsDisposed && hubWeb.CoreWebView2 != null)
                hubWeb.ExecuteScriptAsync("window.UiLab&&UiLab.dismissWorkWindow()");
        }
        async void ShowHubWorkWindow(string json)
        {
            if((!hubProgressActive&&!hubWorkWindowQaEnabled)||!CanShowHubWorkScreen())return;
            var state=Data.Json.Deserialize<WorkWindowState>(json);
            if(state==null||state.rect==null||String.IsNullOrEmpty(state.html)||
                state.html.Length>16000||state.rect.viewWidth<100||state.rect.viewHeight<100||
                state.rect.width<1||state.rect.height<1)return;
            if (state.workId == hubWorkWindowDismissedWorkId) return;
            hubWorkWindowState=state;
            if(hubWorkWindow!=null){
                if(hubWorkWindowReady)hubWorkWindowWeb.CoreWebView2.PostWebMessageAsJson(json);
                return;
            }
            var window=new WorkProgressFrame {Text="커캠 격납고 · 작업 진행",AutoScaleMode=AutoScaleMode.None,
                StartPosition=FormStartPosition.Manual,MinimumSize=new Size(320,220),
                ShowInTaskbar=true,
                BackColor=GlassTheme.Background};
            hubWorkWindow=window;
            window.Icon=Icon;
            double scaleX=(double)hubWeb.ClientSize.Width/state.rect.viewWidth;
            double scaleY=(double)hubWeb.ClientSize.Height/state.rect.viewHeight;
            window.ClientSize=new Size(Math.Max(300,(int)Math.Round(state.rect.width*scaleX)),
                Math.Max(180,(int)Math.Round(state.rect.height*scaleY)));
            var origin=hubWeb.PointToScreen(new Point((int)Math.Round(state.rect.x*scaleX),
                (int)Math.Round(state.rect.y*scaleY)));
            WindowSize.Restore(window,new WindowPreference {
                left=origin.X,top=origin.Y,
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
                DismissHubWorkWindow();
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
                    }else if(action=="close"){
                        DismissHubWorkWindow();
                    }else if(action=="move"){
                        window.BeginMove();
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

        void VerifyWorkFrameCorners(string phase)
        {
            if (hubWorkWindow == null || hubWorkWindow.Region == null)
                throw new InvalidOperationException("Detached work native rounded region missing: " + phase);
            var region = hubWorkWindow.Region;
            int w = hubWorkWindow.Width, h = hubWorkWindow.Height;
            foreach (var p in new[] { new Point(1, 1), new Point(w - 2, 1),
                new Point(1, h - 2), new Point(w - 2, h - 2) })
                if (region.IsVisible(p))
                    throw new InvalidOperationException("Detached work square native corner: " + phase + " " + p);
            foreach (var p in new[] { new Point(w / 2, 1), new Point(1, h / 2),
                new Point(w - 2, h / 2), new Point(w / 2, h - 2), new Point(w / 2, h / 2) })
                if (!region.IsVisible(p))
                    throw new InvalidOperationException("Detached work native edge clipped: " + phase + " " + p);
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
                if(hubWorkWindow.FormBorderStyle!=FormBorderStyle.None)
                    throw new InvalidOperationException("Detached work window must not show an OS title bar.");
                VerifyWorkFrameCorners("initial");
                var controls=Data.Json.Deserialize<string>(await hubWorkWindowWeb.ExecuteScriptAsync(@"(function(){
                    var expand=document.getElementById('expandWork'),
                        close=document.getElementById('closeWork'),
                        drag=document.getElementById('moveWork');
                    if(!expand||!close||!drag)return 'controls-missing';
                    if(expand.textContent.trim()!=='크게 보기'||close.textContent.trim()!=='닫기')
                        return 'incorrect-labels';
                    var a=expand.getBoundingClientRect(),b=close.getBoundingClientRect();
                    if(!(a.right<b.left&&a.top<45&&b.top<45))return 'buttons-not-opposite';
                    var frame=getComputedStyle(document.querySelector('.boot.progress-window'));
                    if(frame.borderTopLeftRadius!=='12px'||frame.borderTopRightRadius!=='12px'||
                        frame.borderBottomLeftRadius!=='12px'||frame.borderBottomRightRadius!=='12px'||
                        frame.overflowX!=='hidden'||frame.overflowY!=='hidden')return 'missing-css-rounding';
                    return 'ok';
                })()"));
                if(controls!="ok")throw new InvalidOperationException("Detached frameless controls: "+controls);
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
                VerifyWorkFrameCorners("420x280");
                hubWorkWindow.Size=hubWorkWindow.MinimumSize;
                VerifyWorkFrameCorners("minimum");
                hubWorkWindow.Bounds=new Rectangle(area.Left+10,area.Top+10,420,280);
                VerifyWorkFrameCorners("restored-size");
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
                // WebView CapturePreview omits the native six-pixel frame.
                // Also capture the actual screen window for corner inspection.
                hubWorkWindow.BringToFront();
                await Task.Delay(150);
                using(var image=new Bitmap(hubWorkWindow.Width,hubWorkWindow.Height))
                {
                    using(var graphics=Graphics.FromImage(image))
                        graphics.CopyFromScreen(hubWorkWindow.Location,Point.Empty,image.Size);
                    image.Save(Path.Combine(output,"detached-work-window-native.png"),
                        System.Drawing.Imaging.ImageFormat.Png);
                }
                // Expanding is a different action from closing: only it
                // restores the owner's fullscreen loading screen.
                await hubWorkWindowWeb.ExecuteScriptAsync("document.getElementById('expandWork').click()");
                await Task.Delay(250);
                if(hubWorkWindow!=null)throw new InvalidOperationException("Expand did not close detached window.");
                var fullscreen=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                    return !document.getElementById('boot').classList.contains('is-hidden')&&
                        document.getElementById('bootBrowse').textContent==='둘러보기'?
                        'ok':'expanded-overlay-or-label';
                })()"));
                if(fullscreen!="ok")throw new InvalidOperationException("Fullscreen return: "+fullscreen);
                hubWorkWindowQaRendered=new TaskCompletionSource<bool>();
                await hubWeb.ExecuteScriptAsync(@"UiLab.setWorkProgress({sequence:90001,active:false});
                    UiLab.setWorkProgress({sequence:90002,workId:90001,active:true,fullscreen:true,browse:true,
                        title:'격리 검수 · 다시 열린 진행창',stage:'크기 위치 복원',completed:2,total:4});");
                if(await Task.WhenAny(hubWorkWindowQaRendered.Task,Task.Delay(15000))!=hubWorkWindowQaRendered.Task)
                    throw new InvalidOperationException("Detached work window did not reopen.");
                if(hubWorkWindow.Bounds!=remembered)throw new InvalidOperationException("Detached work geometry did not persist.");
                VerifyWorkFrameCorners("reopened");
                hubWorkWindowQaRendered=new TaskCompletionSource<bool>();
                await hubWeb.ExecuteScriptAsync(@"UiLab.setWorkProgress({sequence:90003,workId:90001,active:true,fullscreen:true,browse:true,
                    title:'격리 검수 · 다시 열린 진행창',stage:'진행률 갱신',completed:3,total:4});");
                if(await Task.WhenAny(hubWorkWindowQaRendered.Task,Task.Delay(15000))!=hubWorkWindowQaRendered.Task)
                    throw new InvalidOperationException("Detached work update did not render.");
                if(Data.Json.Deserialize<string>(await hubWorkWindowWeb.ExecuteScriptAsync("document.getElementById('bootPercent').textContent"))!="75%")
                    throw new InvalidOperationException("Detached work progress stopped updating.");
                await hubWorkWindowWeb.ExecuteScriptAsync("document.getElementById('closeWork').click()");
                await Task.Delay(250);
                if(hubWorkWindow!=null)throw new InvalidOperationException("Close left the detached window open.");
                var dismissed=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                    return document.getElementById('boot').classList.contains('is-hidden')&&
                        !document.body.classList.contains('is-booting')?'ok':'close-shows-fullscreen';
                })()"));
                if(dismissed!="ok")throw new InvalidOperationException("Close behavior: "+dismissed);
                await hubWeb.ExecuteScriptAsync(@"UiLab.setWorkProgress({sequence:90003.5,workId:90001,
                    active:true,fullscreen:true,browse:true,completed:4,total:4});");
                await Task.Delay(150);
                if(hubWorkWindow!=null)throw new InvalidOperationException("Dismissed window reopened during the same job.");
                Console.WriteLine("DETACHED WORK PASS: native rounded frame and CSS corners, frameless panel, left expand/right close, independent close, fullscreen browse, 25%/75%, movable/resizable saved bounds.");
            }finally{
                CloseHubWorkWindow();hubWorkWindowQaEnabled=false;hubWorkWindowQaRendered=null;
                cleanup=hubWeb.ExecuteScriptAsync("UiLab.setWorkProgress({sequence:90004,active:false})");
            }
            if(cleanup!=null)await cleanup;
        }
    }
}
