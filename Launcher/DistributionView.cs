using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampaignHangar
{
    public sealed class DistributionView : UserControl
    {
        readonly WebView2 web = new WebView2();
        readonly Label status = new Label();
        readonly ProgressBar downloadProgress = new ProgressBar();
        readonly string profile, downloads;
        readonly Func<string> downloadDirectory;
        readonly Func<Campaign, string, Task> import;
        readonly Action returnArchive;
        readonly Action<Campaign> importLocal;
        readonly Action<string,Action> beginWork;
        readonly Action<WorkProgress> reportWork;
        readonly Action finishWork;
        readonly Func<bool> useFullscreen;
        Campaign campaign;
        string distributionUrl;
        CoreWebView2DownloadOperation activeDownload;
        Task initialization;
        public bool DownloadInProgress { get { return activeDownload != null; } }
        public bool TryGoBack()
        {
            if (web.CoreWebView2 == null || !web.CanGoBack) return false;
            web.GoBack(); return true;
        }
        public DistributionView(string profileRoot, string storageRoot, Func<Campaign, string, Task> import,
            Action returnArchive, Action<Campaign> importLocal, Func<string> downloadDirectory = null,
            Action<string,Action> beginWork = null, Action<WorkProgress> reportWork = null,
            Action finishWork = null, Func<bool> useFullscreen = null)
        {
            this.downloadDirectory = downloadDirectory;
            this.import = import; this.returnArchive = returnArchive; this.importLocal = importLocal;
            this.beginWork = beginWork; this.reportWork = reportWork;
            this.finishWork = finishWork; this.useFullscreen = useFullscreen;
            profile = Path.Combine(profileRoot, "distribution-browser"); downloads = Path.Combine(storageRoot, "downloads");
            Dock = DockStyle.Fill; BackColor = GlassTheme.Background;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 14)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            toolbar.Controls.Add(Button("아카이브로", delegate { this.returnArchive(); }));
            toolbar.Controls.Add(Button("받은 ZIP 가져오기", delegate { if (campaign != null) this.importLocal(campaign); }));
            toolbar.Controls.Add(Button("브라우저로 열기", delegate { if (DistributionPolicy.Https(distributionUrl)) Process.Start(distributionUrl); }));
            toolbar.Controls.Add(Button("다운로드 취소", delegate { if (activeDownload != null) activeDownload.Cancel(); }));
            status.Dock = DockStyle.Fill; status.ForeColor = GlassTheme.Muted; status.Font = new Font("맑은 고딕", 10);
            web.Dock = DockStyle.Fill; web.DefaultBackgroundColor = GlassTheme.Background;
            downloadProgress.Dock = DockStyle.Fill; downloadProgress.Visible = false;
            layout.Controls.Add(toolbar, 0, 0); layout.Controls.Add(status, 0, 1);
            layout.Controls.Add(downloadProgress, 0, 2); layout.Controls.Add(web, 0, 3); Controls.Add(layout);
        }
        static Button Button(string text, EventHandler handler)
        {
            var button = new MetalButton { Text = text, AutoSize = true, Height = 44, Font = new Font("맑은 고딕", 10, FontStyle.Bold), Margin = new Padding(0, 4, 10, 0) };
            button.Click += handler; return button;
        }
        public async Task OpenAsync(Campaign target, string url)
        {
            if (!DistributionPolicy.DrivePage(url)) throw new InvalidOperationException("앱 안에서 지원하는 드라이브 배포처가 아닙니다.");
            if (DownloadInProgress) throw new InvalidOperationException("진행 중인 다운로드를 마치거나 취소해 주세요.");
            if (initialization == null) initialization = InitializeAsync();
            await initialization;
            campaign = target; distributionUrl = url;
            status.Text = target.name + " · 배포처에서 ZIP을 선택해 다운로드하세요.\n로그인·접근 제한이 있으면 브라우저로 받은 뒤 ‘받은 ZIP 가져오기’를 사용하세요.";
            web.CoreWebView2.Navigate(url);
        }
        async Task InitializeAsync()
        {
            var environment = await CoreWebView2Environment.CreateAsync(null, profile);
            await web.EnsureCoreWebView2Async(environment);
            web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
            web.CoreWebView2.Settings.IsWebMessageEnabled = false;
            web.CoreWebView2.Settings.AreDevToolsEnabled = false;
            web.CoreWebView2.Settings.IsStatusBarEnabled = false;
            web.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
            web.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!DistributionPolicy.DrivePage(e.Uri))
                { e.Cancel = true; status.Text = "이 배포처 이동은 앱 안에서 지원하지 않습니다. 브라우저로 열기 또는 ZIP 가져오기를 사용하세요."; }
            };
            web.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
            {
                e.Handled = true;
                if (e.IsUserInitiated && DistributionPolicy.DrivePage(e.Uri)) web.CoreWebView2.Navigate(e.Uri);
                else status.Text = "새 창이 필요하면 ‘브라우저로 열기’를 사용하세요.";
            };
            web.CoreWebView2.DownloadStarting += StartDownload;
            web.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (!e.IsSuccess) status.Text = "배포처를 열지 못했습니다: " + e.WebErrorStatus + "\n접근 제한을 우회하지 않습니다. 브라우저로 받거나 ZIP을 가져와 주세요.";
            };
        }
        internal async Task<string> VerifyPageAsync(Campaign target)
        {
            await OpenAsync(target, target.download);
            var loaded = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            { if (e.IsSuccess) loaded.TrySetResult(true); else loaded.TrySetException(new InvalidOperationException(e.WebErrorStatus.ToString())); };
            web.CoreWebView2.NavigationCompleted += handler;
            try
            {
                if (await Task.WhenAny(loaded.Task, Task.Delay(30000)) != loaded.Task) throw new TimeoutException("Drive navigation timed out.");
                await loaded.Task;
                if (!DistributionPolicy.DrivePage(web.Source.AbsoluteUri)) throw new InvalidOperationException("Unexpected distribution origin.");
                return await web.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,text:document.body.innerText.slice(0,800)})");
            }
            finally { web.CoreWebView2.NavigationCompleted -= handler; }
        }
        void StartDownload(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            e.Cancel = true;
            var operation = e.DownloadOperation;
            string filename = Path.GetFileName(e.ResultFilePath);
            if (campaign == null || DownloadInProgress || web.Source == null ||
                !DistributionPolicy.DriveDownload(web.Source.AbsoluteUri, operation.Uri, filename) ||
                operation.TotalBytesToReceive > DistributionPolicy.MaximumBytes)
            { status.Text = "등록된 드라이브 배포처의 ZIP만 받을 수 있습니다. 파일 크기 상한은 4GiB입니다."; return; }
            var context = campaign;
            if (!AppDialog.Confirm(this, context.name + "\n" + filename +
                "\n\n이 ZIP을 받고 검증 후 캠페인 보관소에 등록할까요?\n게임에는 자동 적용하지 않습니다.",
                "배포 ZIP 받기", "ZIP 다운로드", "취소")) return;
            string folder = Path.Combine(downloadDirectory == null ? downloads : downloadDirectory(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
            string partial = Path.Combine(folder, "release.zip.partial"), complete = Path.Combine(folder, "release.zip");
            e.ResultFilePath = partial; e.Handled = true; e.Cancel = false; activeDownload = operation;
            downloadProgress.Visible = true; downloadProgress.Style = ProgressBarStyle.Marquee;
            if (beginWork != null) beginWork(context.name + " · 배포 ZIP 다운로드",operation.Cancel);
            WorkDialog progressWindow = null;
            if (useFullscreen == null || !useFullscreen())
            {
                progressWindow = new WorkDialog(context.name + " · ZIP 다운로드", new Action(operation.Cancel));
                progressWindow.Show(FindForm());
            }
            EventHandler<object> progress = null, changed = null;
            progress = delegate
            {
                if (operation.BytesReceived > DistributionPolicy.MaximumBytes) { status.Text = "4GiB 상한을 초과해 다운로드를 취소했습니다."; operation.Cancel(); }
                else
                {
                    ulong total = operation.TotalBytesToReceive.GetValueOrDefault();
                    downloadProgress.Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                    if (total > 0) downloadProgress.Value = (int)Math.Min(100, operation.BytesReceived * 100.0 / total);
                    status.Text = context.name + " · " + (operation.BytesReceived / 1048576) + " MB 받음" +
                        (total > 0 ? " / " + (total / 1048576) + " MB" : " · 전체 크기 확인 중");
                    var measured = new WorkProgress("배포 ZIP 다운로드 중",
                        (long)operation.BytesReceived,(long)total,true);
                    if (reportWork != null) reportWork(measured);
                    if (progressWindow != null && !progressWindow.IsDisposed)
                        progressWindow.UpdateProgress(measured);
                }
            };
            changed = async delegate
            {
                if (operation.State == CoreWebView2DownloadState.InProgress) return;
                operation.BytesReceivedChanged -= progress; operation.StateChanged -= changed; activeDownload = null;
                downloadProgress.Visible = false;
                if (progressWindow != null && !progressWindow.IsDisposed) progressWindow.Finish();
                try
                {
                if (operation.State != CoreWebView2DownloadState.Completed)
                { status.Text = "다운로드 미완료 · " + operation.InterruptReason + "\n부분 파일은 보존했으며 설치하지 않았습니다."; return; }
                try
                {
                    File.Move(partial, complete);
                    status.Text = context.name + " · ZIP 검증·등록 중…";
                    await import(context, complete);
                    status.Text = context.name + " · 등록 완료 · 게임 적용은 별도입니다.";
                }
                catch (Exception error)
                {
                    status.Text = "등록 실패 · " + error.Message + "\n다운로드 원본은 보존했습니다.";
                    AppDialog.Info(this, status.Text, "다운로드 완료 · 보관소 등록 실패");
                }
                }
                finally { if (finishWork != null) finishWork(); }
            };
            operation.BytesReceivedChanged += progress; operation.StateChanged += changed;
        }
    }
}
