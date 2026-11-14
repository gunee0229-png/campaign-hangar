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
    internal sealed class ArchiveRecommendationResult
    {
        public bool ok { get; set; }
        public bool duplicate { get; set; }
        public int? count { get; set; }
        public string error { get; set; }
    }
    public sealed class ArchiveView : UserControl
    {
        readonly WebView2 web = new WebView2();
        readonly Label message = new Label();
        readonly Button setup = new MetalButton();
        readonly ToolTip tips = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000 };
        readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, Padding = new Padding(0, 6, 0, 0) };
        TableLayoutPanel archiveLayout;
        readonly string profile;
        readonly bool online;
        readonly Func<string, string, Task<bool>> requestDownload;
        readonly Func<string[], Task> importDropped;
        readonly Func<string> dedicatedClientState;
        bool routing;
        bool embeddedInHub;
        ulong latestNavigation;
        Task initialization;
        readonly Stopwatch navigationWatch=new Stopwatch();
        internal long LastNavigationMilliseconds { get; private set; }
        internal bool IsBrowserReadyForQa { get { return web.CoreWebView2 != null; } }
        public ArchiveView(string root, bool online, Func<string, string, Task<bool>> requestDownload = null, Func<string> dedicatedClientState = null, Func<string[], Task> importDropped = null)
        {
            this.online = online; this.requestDownload = requestDownload; profile = Path.Combine(root, "archive-browser");
            this.dedicatedClientState = dedicatedClientState;
            this.importDropped = importDropped;
            Dock = DockStyle.Fill; BackColor = GlassTheme.Background;
            var layout = archiveLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            toolbar.Controls.Add(ActionButton("뒤로", "back", delegate { if (web.CoreWebView2 != null && web.CanGoBack) web.GoBack(); }));
            toolbar.Controls.Add(ActionButton("홈", "home", delegate { if (web.CoreWebView2 != null) web.CoreWebView2.Navigate(Data.Site); }));
            toolbar.Controls.Add(ActionButton("새로고침", "refresh", delegate { if (web.CoreWebView2 != null) web.Reload(); }));
            toolbar.Controls.Add(ActionButton("브라우저로 열기", "external", delegate { OpenExternal(web.Source == null ? Data.Site : web.Source.AbsoluteUri); }));
            Disposed += delegate { tips.Dispose(); };
            setup.Text = "WebView2 설치"; setup.Size = new Size(180, 42); setup.Visible = false;
            setup.Click += async delegate
            {
                setup.Enabled = false;
                try
                {
                    if (await ArchiveRuntime.InstallAsync(this)) { initialization = null; await OpenAsync(); }
                }
                catch (Exception error) { message.Text = "설치 실패: " + error.Message; }
                finally { setup.Enabled = true; }
            };
            toolbar.Controls.Add(setup); layout.Controls.Add(toolbar, 0, 0);
            var content = new Panel { Dock = DockStyle.Fill, BackColor = GlassTheme.Background };
            // The host owns local ZIP imports. A scoped DOM drop handler passes
            // OS File objects to the same importer, rather than navigating to ZIPs.
            web.AllowExternalDrop = importDropped != null;
            web.Dock = DockStyle.Fill; web.DefaultBackgroundColor = GlassTheme.Background; web.Visible = false;
            message.Dock = DockStyle.Fill; message.TextAlign = ContentAlignment.MiddleCenter; message.ForeColor = GlassTheme.Muted;
            message.Font = new Font("맑은 고딕", 12); message.Text = "아카이브를 불러오는 중입니다.";
            content.Controls.Add(web); content.Controls.Add(message); layout.Controls.Add(content, 0, 1);
            Controls.Add(layout);
        }
        Button ActionButton(string text, string icon, EventHandler action)
        {
            var button = new MetalButton { Text = text, AccessibleName = text, AccessibleRole = AccessibleRole.PushButton,
                IconName = icon, Size = new Size(48, 48), Margin = new Padding(0, 0, 10, 0) };
            tips.SetToolTip(button, text);
            button.Click += action; return button;
        }
        internal void VerifyToolbar()
        {
            string[] names = { "뒤로", "홈", "새로고침", "브라우저로 열기" };
            string[] icons = { "back", "home", "refresh", "external" };
            for (int i = 0; i < 4; i++)
            {
                var button = (MetalButton)toolbar.Controls[i];
                if (button.AccessibleName != names[i] || tips.GetToolTip(button) != names[i] || button.IconName != icons[i] || button.Size != new Size(48, 48))
                    throw new InvalidOperationException("Archive icon buttons must retain accessible labels, tooltips and 48px targets.");
            }
            Console.WriteLine("UI PASS: four archive icon buttons preserve names, tooltips and fixed 48px hit targets.");
        }
        internal void CaptureToolbar(string output)
        {
            toolbar.Width = 300; toolbar.PerformLayout();
            using (var image = new Bitmap(300, 58))
            { toolbar.DrawToBitmap(image, new Rectangle(0, 0, 300, 58)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
        }
        public static bool InternalUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" &&
                uri.Host == new Uri(Data.Site).Host && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo);
        }
        static void OpenExternal(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo)) return;
            Process.Start(uri.AbsoluteUri);
        }
        void ExternalLink(string value, bool userInitiated)
        {
            Uri uri;
            if (!userInitiated || !Uri.TryCreate(value, UriKind.Absolute, out uri) || uri.Scheme != "https" || !String.IsNullOrEmpty(uri.UserInfo)) return;
            if (AppDialog.Confirm(this, uri.AbsoluteUri +
                "\n\n이 외부 링크를 기본 브라우저로 열까요?",
                "외부 사이트", "브라우저로 열기", "취소")) OpenExternal(value);
        }
        async void RouteDistribution(string value, bool userInitiated)
        {
            if (!userInitiated || routing || web.Source == null || !InternalUrl(web.Source.AbsoluteUri)) return;
            routing = true;
            try
            {
                string json = await web.ExecuteScriptAsync("document.querySelector('#detail-dialog[open] [data-save-campaign]')?.getAttribute('data-save-campaign') || null");
                string campaignId = Data.Json.Deserialize<string>(json);
                if (requestDownload == null || !await requestDownload(value, campaignId)) ExternalLink(value, true);
                await RefreshDedicatedState();
            }
            catch (Exception error) { AppDialog.Info(this, "배포 자료를 연결하지 못했습니다.\n" + error.Message, "배포 연결 실패"); }
            finally { routing = false; }
        }
        async Task RefreshDedicatedState()
        {
            if (dedicatedClientState == null || web.CoreWebView2 == null || web.Source == null || !InternalUrl(web.Source.AbsoluteUri)) return;
            string state = dedicatedClientState().Replace("<", "\\u003c");
            await web.ExecuteScriptAsync("window.SC2DedicatedClient={installerVersion:" + DedicatedLibrary.InstallerVersion +
                ",installed:" + state + "};window.SC2RefreshDedicatedActions?.();");
        }
        public Task OpenAsync()
        {
            if (initialization == null) initialization = InitializeAsync(Data.Site);
            return initialization;
        }
        public bool TryGoBack()
        {
            if (web.CoreWebView2 == null || !web.CanGoBack) return false;
            web.GoBack(); return true;
        }
        // A single existing archive browser is reused inside the rich UI.
        // Native optimization mode can restore the original toolbar unchanged.
        internal void SetEmbeddedInHub(bool embedded)
        {
            if (archiveLayout == null) return;
            embeddedInHub = embedded;
            archiveLayout.RowStyles[0].Height = embedded ? 0 : 58;
            toolbar.Visible = !embedded;
            archiveLayout.PerformLayout();
            SyncEmbeddedSkipNavigation();
        }
        async void SyncEmbeddedSkipNavigation()
        {
            if (web.CoreWebView2 == null) return;
            try
            {
                // The archive site has a focus-only <a class="skip" href="#main">
                // link. Suppress it only in the embedded game-themed surface:
                // a persistent CSS rule also covers late DOM updates and
                // navigation. Remove the rule again in optimized/native mode
                // so the original website retains keyboard accessibility.
                await web.ExecuteScriptAsync(@"(function(embedded){
                    var id='campaign-hangar-embedded-skip-style';
                    var style=document.getElementById(id);
                    if(!embedded){if(style)style.remove();return;}
                    if(!style){
                      style=document.createElement('style');style.id=id;
                      style.textContent=`a.skip[href='#main'],a.skip[href^='#main-']{display:none!important;visibility:hidden!important;}`;
                      (document.head||document.documentElement).appendChild(style);
                    }
                })("+(embeddedInHub?"true":"false")+")");
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine("Embedded archive skip-link state: " + error.Message); }
        }
        internal async Task<bool> VerifyEmbeddedSkipNavigationAsync()
        {
            if (online) throw new InvalidOperationException("Skip-link fixture must use an offline ArchiveView.");
            if (web.CoreWebView2 == null)
            {
                // The offline ArchiveView intentionally does not open the real
                // website. Initialize only its isolated test browser, and use
                // synthetic markup; never touch the live user's profile.
                var testEnvironment=await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(profile,"skip-link-fixture"));
                await web.EnsureCoreWebView2Async(testEnvironment);
                web.CoreWebView2.NavigateToString("<html><body><main id='main'></main></body></html>");
                await Task.Delay(230);
            }
            // Isolated UI QA adds one disposable site-like anchor. Verify that
            // game themes hide it while original optimization mode restores it.
            await web.ExecuteScriptAsync(@"(function(){
                var a=document.createElement('a');a.id='hangar-skip-qa';
                a.className='skip';a.href='#main';a.textContent='본문으로 건너뛰기';
                (document.body||document.documentElement).appendChild(a);
            })()");
            Func<Task<bool>> hidden = async delegate {
                var value=await web.ExecuteScriptAsync(@"(function(){
                    var a=document.getElementById('hangar-skip-qa');
                    return !!a && getComputedStyle(a).display==='none';
                })()");
                return String.Equals(value,"true",StringComparison.OrdinalIgnoreCase);
            };
            SetEmbeddedInHub(true);await Task.Delay(160);
            bool themeHidden=await hidden();
            SetEmbeddedInHub(false);await Task.Delay(160);
            bool originalVisible=!await hidden();
            SetEmbeddedInHub(true);await Task.Delay(160);
            bool themeRestored=await hidden();
            await web.ExecuteScriptAsync("document.getElementById('hangar-skip-qa')?.remove()");
            return themeHidden && originalVisible && themeRestored;
        }
        internal void HomeFromHub()
        {
            if (web.CoreWebView2 != null) web.CoreWebView2.Navigate(Data.Site);
        }
        internal async Task SupportFromHub()
        {
            await OpenAsync();
            if (web.CoreWebView2 != null) web.CoreWebView2.Navigate(Data.Site + "/support.html");
        }
        internal void RefreshFromHub()
        {
            if (web.CoreWebView2 != null) web.Reload();
        }
        internal void ExternalFromHub()
        {
            OpenExternal(web.Source == null ? Data.Site : web.Source.AbsoluteUri);
        }
        async Task InitializeAsync(string firstUrl)
        {
            if (!online) { message.Text = "격리 UI 검사에서는 웹사이트를 열지 않습니다."; return; }
            if (!ArchiveRuntime.Installed())
            {
                setup.Visible = true; message.Text = "WebView2가 설치되지 않았습니다.\n위의 설치 버튼을 누르면 동의 후 Microsoft 공식 설치를 진행합니다.\n다른 캠페인 기능은 그대로 사용할 수 있습니다."; return;
            }
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                if (IsDisposed) return;
                await web.EnsureCoreWebView2Async(environment);
                web.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                web.CoreWebView2.Settings.IsWebMessageEnabled = importDropped != null;
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                // Hide the Chromium hover URL at the bottom left without
                // intercepting anchor navigation, downloads, or back history.
                web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                if (importDropped != null)
                {
                    web.CoreWebView2.WebMessageReceived += async delegate(object sender, CoreWebView2WebMessageReceivedEventArgs e)
                    {
                        if (!InternalUrl(e.Source)) return;
                        try
                        {
                            if (e.TryGetWebMessageAsString() != "import-drop") return;
                            await importDropped(ZipDropTarget.WebZipFiles(e));
                        }
                        catch (Exception error)
                        {
                            AppDialog.Info(this, "ZIP 가져오기 실패: " + error.Message, "파일 드롭 확인");
                        }
                    };
                    await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                        (function(){
                          if(window.__hangarZipDrop)return;window.__hangarZipDrop=true;
                          var cue=null;
                          function isFiles(e){return !!(e.dataTransfer&&Array.prototype.indexOf.call(e.dataTransfer.types||[],'Files')>=0);}
                          function show(){
                            if(!cue){cue=document.createElement('div');cue.id='hangar-zip-drop-cue';
                              cue.style.cssText='position:fixed;inset:12px;z-index:2147483647;pointer-events:none;display:grid;place-items:center;background:rgba(5,16,28,.86);border:3px dashed #5cc7e8;border-radius:12px;color:white;font:bold 22px system-ui,sans-serif;text-align:center';
                              cue.textContent='캠페인 ZIP 설치 · 파일을 놓아주세요';document.documentElement.appendChild(cue);}
                            cue.style.display='grid';
                          }
                          function hide(){if(cue)cue.style.display='none';}
                          document.addEventListener('dragenter',function(e){if(isFiles(e)){e.preventDefault();show();}},true);
                          document.addEventListener('dragover',function(e){if(isFiles(e)){e.preventDefault();e.dataTransfer.dropEffect='copy';show();}},true);
                          document.addEventListener('dragleave',function(e){if(isFiles(e)&&!e.relatedTarget)hide();},true);
                          document.addEventListener('drop',function(e){
                            if(!isFiles(e))return;e.preventDefault();e.stopImmediatePropagation();hide();
                            var dropped=Array.prototype.slice.call(e.dataTransfer.files||[]);
                            if(window.chrome&&chrome.webview&&chrome.webview.postMessageWithAdditionalObjects)
                              chrome.webview.postMessageWithAdditionalObjects('import-drop',dropped);
                          },true);
                        })();");
                }
                web.CoreWebView2.PermissionRequested += delegate(object sender, CoreWebView2PermissionRequestedEventArgs e) { e.State = CoreWebView2PermissionState.Deny; };
                web.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    string action, id;
                    if (Hangar.DedicatedAction(e.Uri, out action, out id) || !InternalUrl(e.Uri) || Data.DirectZip(e.Uri))
                    { e.Cancel = true; RouteDistribution(e.Uri, e.IsUserInitiated); }
                    else {
                        latestNavigation = e.NavigationId;navigationWatch.Restart();
                        if(!web.Visible){message.Text="아카이브 서버 응답을 기다리는 중입니다. 화면 이동은 바로 사용할 수 있습니다.";message.Visible=true;}
                    }
                };
                web.CoreWebView2.NewWindowRequested += delegate(object sender, CoreWebView2NewWindowRequestedEventArgs e)
                {
                    e.Handled = true;
                    string action, id;
                    if (Hangar.DedicatedAction(e.Uri, out action, out id)) RouteDistribution(e.Uri, e.IsUserInitiated);
                    else if (InternalUrl(e.Uri) && !Data.DirectZip(e.Uri)) web.CoreWebView2.Navigate(e.Uri);
                    else RouteDistribution(e.Uri, e.IsUserInitiated);
                };
                web.CoreWebView2.DownloadStarting += delegate(object sender, CoreWebView2DownloadStartingEventArgs e)
                {
                    e.Cancel = true; AppDialog.Info(this,
                        "이 다운로드는 앱의 자동 설치 지원 대상이 아닙니다.\n‘브라우저로 열기’에서 ZIP을 받은 뒤 ‘내 캠페인 → ZIP 가져오기’를 사용해 주세요.",
                        "지원되지 않는 다운로드");
                };
                web.CoreWebView2.DOMContentLoaded += async delegate(object sender, CoreWebView2DOMContentLoadedEventArgs e)
                {
                    if (e.NavigationId != latestNavigation) return;
                    LastNavigationMilliseconds=navigationWatch.ElapsedMilliseconds;
                    Trace.WriteLine("Archive DOM ready: "+LastNavigationMilliseconds+"ms");
                    ShowContent();
                    SyncEmbeddedSkipNavigation();
                    await RefreshDedicatedState();
                };
                web.CoreWebView2.NavigationCompleted += delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (e.NavigationId != latestNavigation) return;
                    if (e.IsSuccess) { ShowContent(); SyncEmbeddedSkipNavigation(); }
                    else { message.Visible = true; message.Text = "아카이브를 열지 못했습니다: " + e.WebErrorStatus + "\n인터넷 연결을 확인하고 새로고침해 주세요."; message.BringToFront(); }
                };
                web.CoreWebView2.Navigate(firstUrl);
            }
            catch (Exception error)
            {
                message.Text = "아카이브 초기화 실패: " + error.Message + "\n위의 ‘브라우저로 열기’로 사이트를 볼 수 있습니다.";
                throw;
            }
        }
        internal async Task<string> VerifySiteAsync()
        {
            await OpenAsync();
            if (web.CoreWebView2 == null) throw new InvalidOperationException(message.Text);
            var loaded = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            { if (e.IsSuccess) loaded.TrySetResult(true); else loaded.TrySetException(new InvalidOperationException(e.WebErrorStatus.ToString())); };
            web.CoreWebView2.NavigationCompleted += handler;
            try
            {
                if (await Task.WhenAny(loaded.Task, Task.Delay(30000)) != loaded.Task) throw new TimeoutException("Archive navigation timed out.");
                await loaded.Task;
                string result = await web.ExecuteScriptAsync("JSON.stringify({url:location.href,title:document.title,heading:document.querySelector('h1')?.textContent})");
                if (!InternalUrl(web.Source.AbsoluteUri)) throw new InvalidOperationException("Unexpected archive origin.");
                return result;
            }
            finally { web.CoreWebView2.NavigationCompleted -= handler; }
        }
        internal static string CampaignReviewUrl(Campaign item)
        {
            string url=Catalogue.DetailUrl(item);
            int hash=url.IndexOf('#');
            return url.Insert(hash<0?url.Length:hash,"&view=reviews");
        }
        public async Task OpenCampaignAsync(Campaign item, bool reviews = false)
        {
            // campaigns.html supports this deep link itself: it opens the
            // matching campaign, loads reviews and scrolls to the writing form.
            string url = reviews?CampaignReviewUrl(item):Catalogue.DetailUrl(item);
            bool first = initialization == null;
            if (first) initialization = InitializeAsync(url);
            await initialization;
            if (!first && web.CoreWebView2 != null) web.CoreWebView2.Navigate(url);
        }
        internal async Task<string> ProbeRecommendationEndpointAsync()
        {
            await OpenAsync();
            for(int i=0;i<120;i++)
            {
                if(web.CoreWebView2!=null&&web.Source!=null&&InternalUrl(web.Source.AbsoluteUri))
                {
                    try
                    {
                        var origin=Data.Json.Deserialize<string>(await web.ExecuteScriptAsync("location.origin"));
                        if(String.Equals(origin,Data.Site,StringComparison.OrdinalIgnoreCase))
                            break;
                    }
                    catch(Exception error){Trace.WriteLine("Recommendation probe readiness: "+error.Message);}
                }
                await Task.Delay(100);
            }
            if(web.CoreWebView2==null)throw new InvalidOperationException("WebView2 not ready.");
            // QA only: invalid JSON does not contain a campaign ID; the server
            // cannot insert a vote. Observe actual browser request handling.
            const string script=@"(async function(){
                var result={origin:location.origin};
                try{
                    var get=await fetch('/api/recommendations',{credentials:'same-origin'});
                    result.getStatus=get.status;result.getType=get.headers.get('content-type');
                    var post=await fetch('/api/recommendations',{
                        method:'POST',headers:{'Content-Type':'application/json'},
                        body:'{invalid_json:',credentials:'same-origin'
                    });
                    result.postStatus=post.status;result.postType=post.headers.get('content-type');
                    var body=await post.text();
                    result.postBody=body.slice(0,400);
                }catch(error){result.error=String(error&&error.message||error);}
                return result;
            })()";
            return await AwaitBrowserAsyncResult(script);
        }
        internal async Task<string> VerifyRecommendationFlowWithMockAsync()
        {
            if(web.CoreWebView2==null||web.Source==null||!InternalUrl(web.Source.AbsoluteUri))
                throw new InvalidOperationException("추천 모의 테스트의 아카이브 브라우저가 준비되지 않았습니다.");
            // Dedicated, disposable QA browser profile only. Intercept both
            // recommendation GET and POST in memory so NO real vote is made.
            var injected=await web.ExecuteScriptAsync(@"(function(){
                if(window.__hangarQaOriginalFetch)return false;
                window.__hangarQaOriginalFetch=window.fetch;
                window.__hangarQaVotes={get:0,post:0,voted:false};
                window.fetch=function(url,options){
                    if(url!=='/api/recommendations')
                        return window.__hangarQaOriginalFetch.apply(this,arguments);
                    var q=window.__hangarQaVotes;
                    var method=(options&&options.method||'GET').toUpperCase();
                    var day=new Date(Date.now()+9*3600000).toISOString().slice(0,10);
                    if(method==='GET'){
                        q.get++;
                        return Promise.resolve(new Response(JSON.stringify({
                            day:day,counts:{},voted:q.voted?['case20-qa-fake-id']:[]
                        }),{status:200,headers:{'Content-Type':'application/json'}}));
                    }
                    q.post++;
                    var id=JSON.parse(options.body).campaignId;
                    if(id==='case20-qa-fake-id'){
                        q.voted=true;
                        return Promise.resolve(new Response(JSON.stringify({
                            day:day,count:44,voted:[id]
                        }),{status:200,headers:{'Content-Type':'application/json'}}));
                    }
                    return Promise.resolve(new Response(JSON.stringify({
                        error:'격리 모의 서버 점검 중'
                    }),{status:503,headers:{'Content-Type':'application/json'}}));
                };
                return true;
            })()");
            if(injected!="true")throw new InvalidOperationException("모의 fetch를 설치하지 못했습니다.");
            string checkedCounters=null;
            Exception qaError=null;
            try
            {
                var success=await RecommendCampaignAsync("case20-qa-fake-id",false);
                if(!success.ok||success.count!=44||success.duplicate)
                    throw new InvalidOperationException("성공한 서버 응답을 실패로 처리했습니다.");
                var second=await RecommendCampaignAsync("case20-qa-fake-id",false);
                if(second.ok||!second.duplicate)
                    throw new InvalidOperationException("서버에 저장된 오늘 추천을 감지하지 못했습니다.");
                var failed=await RecommendCampaignAsync("case20-qa-unavailable-id",false);
                if(failed.ok||failed.duplicate||failed.error!="격리 모의 서버 점검 중")
                    throw new InvalidOperationException("모의 서버 오류 처리가 부정확합니다.");
                string counters=await web.ExecuteScriptAsync(
                    "JSON.stringify(window.__hangarQaVotes)");
                string actual=Data.Json.Deserialize<string>(counters);
                if(!actual.Contains("\"get\":3")||!actual.Contains("\"post\":2")||
                    !actual.Contains("\"voted\":true"))
                    throw new InvalidOperationException("추천 재요청 방지 검증 실패: "+actual);
                checkedCounters=actual;
            }
            catch(Exception error){qaError=error;}
            if(web.CoreWebView2!=null)
            {
                await web.ExecuteScriptAsync(@"(function(){
                    if(window.__hangarQaOriginalFetch){
                        window.fetch=window.__hangarQaOriginalFetch;
                        delete window.__hangarQaOriginalFetch;
                        delete window.__hangarQaVotes;
                    }
                })()");
            }
            if(qaError!=null)throw new InvalidOperationException("추천 요청 모의 테스트 실패: "+qaError.Message,qaError);
            return checkedCounters;
        }
        async Task<string> AwaitBrowserAsyncResult(string promiseExpression)
        {
            if(web.CoreWebView2==null)throw new InvalidOperationException("아카이브 브라우저가 준비되지 않았습니다.");
            // WebView2.ExecuteScriptAsync does NOT await a JavaScript Promise:
            // an async IIFE returned "{}" instantly, falsely reporting an
            // unsuccessful vote even if the server had saved it. Attach an
            // in-page completion callback and poll only its settled result.
            // The expression (including any POST) is invoked EXACTLY ONCE.
            string token=Guid.NewGuid().ToString("N");
            string start=@"(function(){
                var key=RESULT_KEY;
                var bag=window.__hangarAsyncResponses||(window.__hangarAsyncResponses=Object.create(null));
                if(bag[key])return false;
                bag[key]={done:false};
                Promise.resolve(PROMISE_EXPRESSION).then(function(value){
                    bag[key]={done:true,value:value};
                },function(error){
                    bag[key]={done:true,value:{ok:false,error:String(error&&error.message||error)}};
                });
                return true;
            })()".Replace("RESULT_KEY",Data.Json.Serialize(token))
                .Replace("PROMISE_EXPRESSION",promiseExpression);
            var started=await web.ExecuteScriptAsync(start);
            if(!String.Equals(started,"true",StringComparison.Ordinal))
                throw new InvalidOperationException("추천 요청을 시작하지 못했습니다.");
            for(int attempt=0;attempt<250;attempt++)
            {
                string read=@"(function(){
                    var bag=window.__hangarAsyncResponses;
                    var item=bag&&bag[RESULT_KEY];
                    if(!item||!item.done)return null;
                    delete bag[RESULT_KEY];
                    return item.value;
                })()".Replace("RESULT_KEY",Data.Json.Serialize(token));
                string value=await web.ExecuteScriptAsync(read);
                if(!String.IsNullOrWhiteSpace(value)&&value!="null")
                    return value;
                await Task.Delay(100);
            }
            throw new TimeoutException("추천 응답을 확인하지 못했습니다. 다시 누르기 전에 아카이브의 오늘 추천 여부를 확인해 주세요.");
        }
        internal async Task<ArchiveRecommendationResult> RecommendCampaignAsync(string siteId, bool refreshOnSuccess = true)
        {
            if(!online) return new ArchiveRecommendationResult {ok=false,error="격리 검증에서는 실제 추천을 등록하지 않습니다."};
            if(String.IsNullOrWhiteSpace(siteId)||siteId.Length>128)
                throw new ArgumentException("추천할 사이트 캠페인을 확인할 수 없습니다.", "siteId");
            await OpenAsync();
            // Do not use a separate HTTP client: recommendation rate limits
            // depend on the actual archive browser's origin/session.
            bool ready=false;
            for(int i=0;i<40;i++) {
                if(web.CoreWebView2!=null && web.Source!=null && InternalUrl(web.Source.AbsoluteUri)) {
                    try {
                        var origin=Data.Json.Deserialize<string>(await web.ExecuteScriptAsync("location.origin"));
                        ready=String.Equals(origin,Data.Site,StringComparison.OrdinalIgnoreCase);
                    }catch(Exception e){Trace.WriteLine("Archive recommendation readiness: "+e.Message);}
                }
                if(ready)break;
                await Task.Delay(150);
            }
            if(!ready)throw new InvalidOperationException("아카이브 연결을 확인하지 못했습니다. 연결 후 다시 시도해 주세요.");
            var script=@"(async function(){
                try {
                    // The archive applies a per-campaign KST daily limit. Query
                    // its same-session record first so a second click can show
                    // the precise one-per-day message without sending a vote.
                    try {
                        var status=await fetch('/api/recommendations',{
                            credentials:'same-origin',signal:AbortSignal.timeout(8000)
                        });
                        if(status.ok){
                            var current=await status.json();
                            var day=new Date(Date.now()+9*3600000).toISOString().slice(0,10);
                            if(current.day===day&&Array.isArray(current.voted)&&
                               current.voted.includes(SITE_CAMPAIGN_ID))
                                return {ok:false,duplicate:true,
                                    error:'각 캠페인당 하루 한 번만 추천 가능합니다.'};
                        }
                    }catch(readError){
                        // POST still has the authoritative duplicate guard.
                    }
                    var response=await fetch('/api/recommendations',{
                        method:'POST',credentials:'same-origin',
                        headers:{'Content-Type':'application/json'},
                        body:JSON.stringify({campaignId:SITE_CAMPAIGN_ID}),
                        signal:AbortSignal.timeout(10000)
                    });
                    var payload=await response.json();
                    if(!response.ok){
                        var message=String(payload.error||'추천을 저장하지 못했습니다.');
                        return {ok:false,duplicate:/하루|이미.+추천|중복|한 번/.test(message),error:message};
                    }
                    return {ok:true,count:typeof payload.count==='number'?payload.count:null,error:null};
                } catch(error) {
                    return {ok:false,error:String(error&&error.message||'추천 연결에 실패했습니다.')};
                }
            })()".Replace("SITE_CAMPAIGN_ID",Data.Json.Serialize(siteId));
            string value=await AwaitBrowserAsyncResult(script);
            var result=Data.Json.Deserialize<ArchiveRecommendationResult>(value);
            if(result==null)throw new IOException("아카이브 추천 응답을 읽을 수 없습니다.");
            if(result.ok && refreshOnSuccess) {
                // The archive's own counters/rate-limit UI read their state
                // during page load; refresh those after a confirmed server vote.
                web.Reload();
            }
            return result;
        }
        void ShowContent()
        {
            message.Visible = false; web.Visible = true; web.BringToFront();
            SyncEmbeddedSkipNavigation();
        }
        internal async Task VerifyDedicatedPageAsync(Campaign item)
        {
            var loaded = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (web.Source == null || web.Source.AbsoluteUri != Catalogue.DetailUrl(item)) return;
                if (e.IsSuccess) loaded.TrySetResult(true);
                else loaded.TrySetException(new InvalidOperationException(e.WebErrorStatus.ToString()));
            };
            web.NavigationCompleted += handler;
            try
            {
                await OpenCampaignAsync(item);
                if (await Task.WhenAny(loaded.Task, Task.Delay(45000)) != loaded.Task) throw new TimeoutException("Dedicated archive navigation timed out.");
                await loaded.Task;
                if (!web.Visible || message.Visible) throw new InvalidOperationException("Loaded archive must not remain covered by its loading/error message.");
            }
            finally { web.NavigationCompleted -= handler; }
        }
        internal async Task<string> VerifyDedicatedActionsAsync()
        {
            await RefreshDedicatedState();
            return Data.Json.Deserialize<string>(await web.ExecuteScriptAsync(
                "[...document.querySelectorAll('#detail-dialog[open] [data-dedicated-actions] a')].map(a=>a.textContent).join('|')"));
        }
        internal async Task CaptureDedicatedContent(string directory)
        {
            var screenshot = Data.Json.Deserialize<System.Collections.Generic.Dictionary<string, object>>(
                await web.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", "{\"format\":\"png\"}"));
            File.WriteAllBytes(Path.Combine(directory, "native-content.png"), Convert.FromBase64String((string)screenshot["data"]));
        }
    }
}
