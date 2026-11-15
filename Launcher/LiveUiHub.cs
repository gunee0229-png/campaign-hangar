using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CampaignHangar
{
    internal sealed class LiveHubEntry
    {
        public string id { get; set; }
        public string versionKey { get; set; }
        public string name { get; set; }
        public string en { get; set; }
        public string kind { get; set; }
        public string category { get; set; }
        public bool installed { get; set; }
        public bool dedicated { get; set; }
        public string author { get; set; }
        public string img { get; set; }
        public string desc { get; set; }
        public string updateState { get; set; }
        public string siteVersion { get; set; }
        public double playSeconds { get; set; }
        public int missions { get; set; }
        public int clears { get; set; }
        public bool bankLinked { get; set; }
        public bool needsReview { get; set; }
        public string lastMission { get; set; }
        public string bankError { get; set; }
        public string[] completedMissions { get; set; }
    }
    internal sealed class HubEmbeddedRect
    {
        public int x { get; set; }
        public int y { get; set; }
        public int width { get; set; }
        public int height { get; set; }
        public int viewWidth { get; set; }
        public int viewHeight { get; set; }
    }
    internal sealed class HubManageFinalRequest
    {
        public string id { get; set; }
        public string action { get; set; }
        public string versionKey { get; set; }
        public string removalKind { get; set; }
        public string siteId { get; set; }
        public bool mismatchAcknowledged { get; set; }
    }
    internal sealed class HubFreePanelRect
    {
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
        public bool locked { get; set; }
    }
    internal sealed class HubUserLayout
    {
        public string menu { get; set; }
        public string detail { get; set; }
        public int menuSize { get; set; }
        public int heroHeight { get; set; }
        public int detailWidth { get; set; }
        public int listHeight { get; set; }
        public int cols { get; set; }
        public int spacing { get; set; }
        public int cardHeight { get; set; }
        public string[] parts { get; set; }
        public string[] nav { get; set; }
        public string[] hiddenCampaigns { get; set; }
        public string[] actions { get; set; }
        public int? navGap { get; set; }
        public string menuShape { get; set; }
        public string presentation { get; set; }
        public string[] contactOrder { get; set; }
        public Dictionary<string,string> colors { get; set; }
        public Dictionary<string,string> texts { get; set; }
        public string[] hiddenElements { get; set; }
        public bool free { get; set; }
        public Dictionary<string,HubFreePanelRect> panels { get; set; }
        public Dictionary<string,HubQuickSize> quickSizes {get;set;}
    }
    internal sealed class HubQuickSize {public int w {get;set;}public int h {get;set;}}
    internal sealed class HubUserLayoutProfile
    {
        public string preset { get; set; }
        public HubUserLayout layout { get; set; }
        public Dictionary<string,HubUserLayout> named { get; set; }
    }
    internal sealed class HubCustomPreset
    {
        public string id { get; set; }
        public string name { get; set; }
        public string theme { get; set; }
        public HubUserLayout layout { get; set; }
        public bool shared { get; set; }
        public string created { get; set; }
    }
    internal sealed class HubCustomPresetStore
    {
        public int schema { get; set; }
        public List<HubCustomPreset> items { get; set; }
    }
    internal sealed class HubCustomPresetExchange
    {
        public string format { get; set; }
        public int version { get; set; }
        public string name { get; set; }
        public string theme { get; set; }
        public HubUserLayout layout { get; set; }
    }
    internal sealed class HubDialogPalette
    {
        public string theme {get;set;}
        public Dictionary<string,string> colors {get;set;}
    }

    public sealed partial class Hangar
    {
        internal string WindowGeometryDirectory {get{return isolated?root+"-profile":
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),AppChannel.Profile);}}
        readonly Panel homePage = new Panel();
        TableLayoutPanel liveShellLayout;
        int liveNavRow;
        WebView2 hubWeb;
        Label hubFallback;
        bool hubInitializing, hubReady;
        string hubProfileRoot, hubFavoritesFile, hubLayoutFile, hubHiddenCampaignsFile, hubPresetLibraryFile, hubPendingArchiveId;
        bool hubPendingArchiveReviews, hubRecommendationInFlight;
        Panel hubEmbeddedArchive;
        Form hubArchiveWindow;
        HubEmbeddedRect hubLastArchiveRect;
        bool hubArchiveOpenRequested, hubDistributionActive, hubArchiveRequestedVisible,
            hubArchiveSuspendedByIntro;
        bool hubShuttingDown;
        int hubWorkDepth;
        string hubWorkTitle;
        int hubFlowDepth;
        string hubFlowTitle;
        long hubProgressSequence;
        long hubWorkEpoch;
        long hubProgressStage;
        bool hubProgressActive;
        WorkProgress hubMeasuredProgress;
        System.Threading.CancellationTokenSource hubWorkCancellation;
        Action hubExternalCancel;
        ThemeForm hubEditorWindow;
        WebView2 hubEditorWeb;
        string hubEditorState;
        bool hubEditorClosing, hubEditorReady, hubEditorQaEnabled;
        void PushHubUpdateNotice(bool force=false)
        {
            if(!hubReady||hubWeb==null||hubWeb.CoreWebView2==null)return;
            hubWeb.ExecuteScriptAsync("window.UiLab.setUpdateNotice("+
                Data.Json.Serialize(new {visible=availableAppUpdate!=null&&
                    (force||declinedAppUpdateVersion!=availableAppUpdate.version),
                    version=availableAppUpdate==null?"":availableAppUpdate.version})+")");
        }

        async void OpenHubEditor()
        {
            if(isolated&&!hubEditorQaEnabled)return;
            if (hubEditorWindow != null) { hubEditorWindow.Activate(); return; }
            if (!hubReady || hubWeb.CoreWebView2 == null) return;
            ThemeForm window = null;
            try
            {
                hubEditorWindow = new ThemeForm { Text = "커스텀 편집 · " + LiveTheme.Current.Name,
                    ClientSize = new Size(780,780), MinimumSize = new Size(480,560),
                    FormBorderStyle = FormBorderStyle.Sizable, MaximizeBox = true,
                    SizeGripStyle = SizeGripStyle.Show, ShowInTaskbar = true };
                window=hubEditorWindow;
                hubEditorWeb = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = GlassTheme.Background };
                var web=hubEditorWeb;
                hubEditorWindow.Controls.Add(hubEditorWeb);
                hubEditorWindow.FormClosing += delegate(object sender,FormClosingEventArgs e) {
                    if (hubEditorClosing || IsDisposed) return;
                    e.Cancel = true;
                    hubWeb.ExecuteScriptAsync("window.HangarFeatures.editorCommand({action:'window-close'})");
                };
                hubEditorWindow.Show(this);
                await web.EnsureCoreWebView2Async(hubWeb.CoreWebView2.Environment);
                if(window.IsDisposed||hubEditorWindow!=window)return;
                var core = web.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreHostObjectsAllowed = false;
                core.Settings.IsStatusBarEnabled = false;
                core.SetVirtualHostNameToFolderMapping("hangar-live.test",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","ui-hub"),
                    CoreWebView2HostResourceAccessKind.DenyCors);
                core.NavigationStarting += delegate(object sender,CoreWebView2NavigationStartingEventArgs e) {
                    if (e.Uri != "https://hangar-live.test/editor.html") e.Cancel = true;
                };
                core.NewWindowRequested += delegate(object sender,CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                core.WebMessageReceived += delegate(object sender,CoreWebView2WebMessageReceivedEventArgs e) {
                    if (e.Source != "https://hangar-live.test/editor.html") return;
                    try
                    {
                        string raw = e.TryGetWebMessageAsString();
                        if (raw.Length > 4096) return;
                        var request = Data.Json.Deserialize<Dictionary<string,object>>(raw);
                        object action;
                        if (request == null || !request.TryGetValue("action",out action)) return;
                        if (Convert.ToString(action) == "ready") {
                            hubEditorReady = true;
                            if (hubEditorState != null) core.PostWebMessageAsJson(hubEditorState);
                            hubWeb.ExecuteScriptAsync("window.HangarFeatures.syncExternalEditor()");
                        }
                        else if (new[]{"close","choice","button","field","range","color","text","all","preset-name"}.Contains(Convert.ToString(action)))
                            hubWeb.ExecuteScriptAsync("window.HangarFeatures.editorCommand(" + Data.Json.Serialize(request) + ")");
                    }
                    catch (Exception error) { System.Diagnostics.Trace.WriteLine("Editor request: "+error.Message); }
                };
                core.Navigate("https://hangar-live.test/editor.html");
            }
            catch (Exception error) {
                if(window==null||window.IsDisposed||hubEditorWindow!=window)return;
                CloseHubEditor();Error(error);
            }
        }
        void CloseHubEditor()
        {
            hubEditorClosing = true;
            try {
                if (hubEditorWindow != null) { hubEditorWindow.Close(); hubEditorWindow.Dispose(); }
                hubEditorWindow = null; hubEditorWeb = null; hubEditorReady = false;
            }
            finally { hubEditorClosing = false; }
        }

        internal async Task VerifyReleaseUpdateDemo(string directory)
        {
            if(!isolated||online||AppChannel.IsDevelopment)
                throw new InvalidOperationException("Update demo requires an isolated offline release app.");
            var release=AppUpdate.Parse(File.ReadAllText(Path.Combine(directory,"latest.json"),Encoding.UTF8).TrimStart('\uFEFF'));
            if(!AppUpdate.IsNewer(release))throw new InvalidOperationException("Demo candidate must be newer.");
            for(int i=0;i<100&&!hubReady;i++)await Task.Delay(100);
            if(!hubReady)throw new InvalidOperationException("Release UI did not become ready.");
            await hubWeb.ExecuteScriptAsync("UiLab.finishBoot();UiLab.setView('settings')");
            availableAppUpdate=release;PushHubUpdateNotice(true);
            await Task.Delay(180);
            using(var image=File.Create(Path.Combine(directory,"01-update-offer.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            string zip=Path.Combine(directory,"candidate.zip"),staged=Path.Combine(directory,"staged");
            string target=Path.Combine(directory,"installed-copy");
            if(!File.Exists(Path.Combine(target,"CampaignHangar.exe")))throw new IOException("Missing isolated old app.");
            string before=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(target,"CampaignHangar.exe")).FileVersion;
            string sentinel=Path.Combine(target,"user-settings-sentinel.txt");
            File.WriteAllText(sentinel,"preserve-user-settings");
            AppUpdate.Extract(release,zip,staged);
            AppUpdate.Replace(staged,target,Path.Combine(directory,"previous-version"));
            string after=System.Diagnostics.FileVersionInfo.GetVersionInfo(Path.Combine(target,"CampaignHangar.exe")).FileVersion;
            if(after!=release.version||File.ReadAllText(sentinel)!="preserve-user-settings")
                throw new InvalidOperationException("Update demo failed to preserve data or replace the version.");
            Console.WriteLine("UPDATE DEMO PASS: "+before+" -> "+after+"; exact package/hash/channel verified; prior files backed up; user data preserved.");
            await hubWeb.ExecuteScriptAsync("UiLab.setUpdateNotice(null);document.querySelector('.build-flag').textContent="+
                Data.Json.Serialize("v"+after+" · 교체 완료 (격리 시연)")+";");
            using(var image=File.Create(Path.Combine(directory,"02-replaced-version.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
        }

        bool CanShowHubWorkScreen()
        {
            return AppChannel.HasThemedUi && LiveTheme.Current != LiveTheme.Classic &&
                hubReady && hubWeb != null && !hubWeb.IsDisposed &&
                hubWeb.CoreWebView2 != null && hubWeb.Visible;
        }

        void BeginHubFlow(string title, Action cancel = null)
        {
            hubFlowDepth++;
            if (hubFlowDepth == 1)
            {
                hubFlowTitle = title;
                hubExternalCancel = cancel;
                hubMeasuredProgress = new WorkProgress("작업 준비 중");
                // One logical operation may contain many ZIP, install and
                // UI-commit stages. Never carry its previous measurements
                // into a new operation.
                hubWorkEpoch++;
                hubProgressActive = true;
            }
            UpdateHubWorkState();
        }

        void FinishHubFlow()
        {
            hubFlowDepth = Math.Max(0, hubFlowDepth - 1);
            if (hubFlowDepth == 0) { hubFlowTitle = null; hubExternalCancel = null; }
            UpdateHubWorkState();
        }

        void ReportHubProgress(WorkProgress value)
        {
            if (value == null) return;
            if (!hubProgressActive && !busy && hubWorkDepth == 0 && hubFlowDepth == 0) return;
            hubMeasuredProgress = value;
            UpdateHubWorkState();
        }

        void UpdateHubWorkState()
        {
            bool working = busy || hubWorkDepth > 0 || hubFlowDepth > 0;
            if (working && !hubProgressActive)
            {
                hubProgressActive = true;
                hubMeasuredProgress = new WorkProgress("작업 준비 중");
                hubWorkEpoch++;
            }
            if (!working)
            {
                hubProgressActive = false;
                hubMeasuredProgress = null;
                CloseHubWorkWindow();
            }
            // Keep state even while WebView2 is loading, so its first ready
            // frame can be synchronized without replaying stale percentages.
            if (!hubReady || hubWeb == null || hubWeb.IsDisposed || hubWeb.CoreWebView2 == null) return;
            try
            {
                var progress = hubMeasuredProgress;
                var payload = new {
                    sequence = ++hubProgressSequence,
                    workId = hubWorkEpoch,
                    active = working,
                    title = hubFlowTitle ?? hubWorkTitle ?? "캠페인 작업 중",
                    stage = progress == null ? "작업 준비 중" : progress.stage,
                    completed = progress == null ? 0L : progress.completed,
                    total = progress == null ? 0L : progress.total,
                    bytes = progress != null && progress.bytes,
                    fullscreen = CanShowHubWorkScreen(),
                    browse = uiLook != null && uiLook.Value.browseWhileWorking,
                    cancelable = (hubWorkCancellation != null && !hubWorkCancellation.IsCancellationRequested) ||
                        hubExternalCancel != null
                };
                hubWeb.ExecuteScriptAsync("window.UiLab&&window.UiLab.setWorkProgress("+
                    Data.Json.Serialize(payload)+")");
            }
            catch (Exception error) { System.Diagnostics.Trace.WriteLine("Hub work UI: " + error.Message); }
        }

        // Local-only CASE 20 checks for hidden cards, sharing, preset storage
        // and the real detached editor surface. No network vote or game write.
        internal async Task VerifyHiddenAndSharedUiAsync(string output)
        {
            if(!isolated||online||!AppChannel.HasThemedUi)
                throw new InvalidOperationException("Hidden/share QA requires an isolated offline themed app.");
            Directory.CreateDirectory(output);
            for(int i=0;i<100&&!hubReady;i++)await Task.Delay(100);
            if(!hubReady||hubWeb==null||hubWeb.CoreWebView2==null)
                throw new InvalidOperationException("Themed WebView2 did not initialize.");
            // At high DPI a compact archive viewport used to re-show the
            // header marked [hidden] because the narrow CSS forced display:flex.
            // Keep real archive navigation visible without repeating the
            // campaign preview title or stats above the embedded website.
            Size originalClientSize=ClientSize;
            try
            {
                ClientSize=new Size(1050,Math.Min(900,Math.Max(720,originalClientSize.Height)));
                await Task.Delay(340);
                var archiveHeader=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                    if(innerWidth>1120)return 'not-compact:'+innerWidth;
                    var w=document.getElementById('workspace');
                    var h=w.querySelector('.workspace-header');
                    var embed=document.getElementById('archiveEmbed');
                    var frame=document.getElementById('archiveMount');
                    var actions=[...document.querySelectorAll('.archive-embed-actions [data-archive-action]')];
                    if(w.dataset.view!=='archive'||!h.hidden)return 'archive-state';
                    if(getComputedStyle(h).display!=='none'||h.getBoundingClientRect().height>0)return 'duplicate-archive-header';
                    if(embed.hidden||getComputedStyle(embed).display==='none'||frame.getBoundingClientRect().height<120)
                        return 'archive-content-hidden';
                    if(actions.length!==4||actions.some(function(a){return a.getBoundingClientRect().height<25;}))
                        return 'archive-navigation-hidden';
                    if(document.documentElement.scrollWidth>document.documentElement.clientWidth+3)
                        return 'archive-horizontal-overflow';
                    return 'ok';
                })()"));
                if(archiveHeader!="ok")
                    throw new InvalidOperationException("Compact archive header regression: "+archiveHeader);
                using(var screenshot=File.Create(Path.Combine(output,"archive-compact-header-hidden.png")))
                    await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            }
            finally { ClientSize=originalClientSize; }
            Console.WriteLine("WEBVIEW COMPACT ARCHIVE PASS: hidden preview/stats remain hidden; embedded site, four navigation actions and overflow verified.");
            var legacyActions=CleanHubActions(new[]{"installButton","launchButton",
                "screenshotButton","detailButton","importZipButton"});
            if(!legacyActions.SequenceEqual(new[]{"installButton","launchButton",
                "screenshotButton","detailButton","reviewButton","recommendButton",
                "importZipButton"}))
                throw new InvalidOperationException("Old custom button order was not preserved while inserting new social actions before ZIP.");
            var rows=new[]{
                new LiveHubEntry {id="hide-qa-a",name="가림 검증",en="HIDE QA",category="자날",
                    installed=true,kind="ccm",author="QA",img="assets/hangar.jpg"},
                new LiveHubEntry {id="hide-qa-b",name="남는 캠페인",en="VISIBLE QA",category="군심",
                    installed=true,kind="ccm",author="QA",img="assets/hangar.jpg"}
            };
            await hubWeb.ExecuteScriptAsync("UiLab.setRealCampaigns("+Data.Json.Serialize(rows)+",2);UiLab.setView('all');");
            string spaceOptions=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var ui=window.UiLab,logo=document.querySelector('.sidebar-logo'),
                    emblem=logo.querySelector('.brand-symbol');
                function leftInset(){
                    return emblem.getBoundingClientRect().left-logo.getBoundingClientRect().left;
                }
                ui.setThemeFromHost('terran');
                var terranInset=leftInset();
                ui.setThemeFromHost('zerg');
                var zergInset=leftInset();
                if(Math.abs(terranInset-zergInset)>5 || zergInset>12)
                    return 'zerg-logo-inset:'+terranInset+'/'+zergInset;
                if(getComputedStyle(logo.querySelector('.logo-copy')).alignItems!=='flex-start')
                    return 'zerg-logo-copy-alignment';
                ui.setView('all');
                document.getElementById('workspace').scrollTop=0;
                var hero=document.getElementById('hero');
                var originalTop=hero.getBoundingClientRect().top;
                ui.setView('settings');
                var themes=document.getElementById('themeVisibilityToggle'),
                    top=document.getElementById('topSummaryToggle'),
                    buttons=document.querySelector('.sidebar>.theme-switcher'),
                    caption=document.querySelector('.sidebar>.themes-caption'),
                    header=document.querySelector('#workspace>.workspace-header'),
                    activation=document.getElementById('activationReadout'),
                    settingsNav=document.querySelector('.settings-nav [data-view=settings]');
                if(!themes||!top||!settingsNav || getComputedStyle(settingsNav).display==='none')
                    return 'visibility-controls-unavailable';
                themes.click();
                if(document.body.dataset.hideThemes!=='true' ||
                   getComputedStyle(buttons).display!=='none'||
                   getComputedStyle(caption).display!=='none'||
                   themes.getAttribute('aria-pressed')!=='true' ||
                   themes.querySelector('strong').textContent!=='테마 버튼 다시 표시')
                    return 'themes-not-hidden';
                top.click();
                if(document.body.dataset.hideTop!=='true' ||
                   getComputedStyle(header).display!=='none' ||
                   getComputedStyle(activation).display!=='none' ||
                   top.getAttribute('aria-pressed')!=='true')
                    return 'header-not-hidden';
                ui.setView('all');
                document.getElementById('workspace').scrollTop=0;
                if(originalTop-hero.getBoundingClientRect().top<35)
                    return 'no-thumbnail-space-gained:'+originalTop+'/'+hero.getBoundingClientRect().top;
                if(document.querySelector('.settings-nav [data-view=settings]').getBoundingClientRect().height<24)
                    return 'cannot-reopen-settings';
                return 'ok';
            })()"));
            if(spaceOptions!="ok")
                throw new InvalidOperationException("Independent display optimization regression: "+spaceOptions);
            using(var screenshot=File.Create(Path.Combine(output,"zerg-logo-top-hidden.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            await hubWeb.ExecuteScriptAsync("UiLab.setView('settings')");
            using(var screenshot=File.Create(Path.Combine(output,"display-optimization-buttons.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            await Task.Delay(220);
            var preferencePath=Path.Combine(root+"-profile","ui-theme.json");
            var persisted=new UiLookStore(preferencePath).Value;
            if(uiLook==null||!uiLook.Value.hideThemeButtons||!uiLook.Value.hideTopSummary||
               !persisted.hideThemeButtons||!persisted.hideTopSummary)
                throw new InvalidOperationException("Two independent visibility preferences were not stored natively.");
            string restoredOptions=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var ui=window.UiLab,
                    themes=document.getElementById('themeVisibilityToggle'),
                    top=document.getElementById('topSummaryToggle'),
                    chooser=document.querySelector('.sidebar>.theme-switcher'),
                    header=document.querySelector('#workspace>.workspace-header');
                ui.setView('settings');
                themes.click();
                if(document.body.dataset.hideThemes!=='false' ||
                   getComputedStyle(chooser).display==='none'||
                   getComputedStyle(header).display!=='none')return 'themes-not-restored-independently';
                themes.click();
                top.click();
                if(document.body.dataset.hideTop!=='false' ||
                   getComputedStyle(header).display==='none'||
                   getComputedStyle(chooser).display!=='none')return 'header-not-restored-independently';
                themes.click();
                ui.setThemeFromHost('terran');
                ui.setView('all');
                if(themes.getAttribute('aria-pressed')!=='false' ||
                   top.getAttribute('aria-pressed')!=='false')return 'button-state-not-restored';
                return 'ok';
            })()"));
            if(restoredOptions!="ok")
                throw new InvalidOperationException("Display optimization restore regression: "+restoredOptions);
            await Task.Delay(220);
            persisted=new UiLookStore(preferencePath).Value;
            if(uiLook.Value.hideThemeButtons||uiLook.Value.hideTopSummary||
               persisted.hideThemeButtons||persisted.hideTopSummary)
                throw new InvalidOperationException("Restored visibility choices were not saved.");
            Console.WriteLine("WEBVIEW DISPLAY SPACE PASS: Zerg logo left aligned, independent theme/top hide and restore, thumbnail space increased, native persisted readback.");
            // The action rail itself may be resized by a custom layout even
            // if the desktop window remains wide. Verify labels and icons at
            // the actual allocated rail width rather than viewport alone.
            await hubWeb.ExecuteScriptAsync("document.querySelector('.action-strip').style.width='670px'");
            await Task.Delay(180);
            var compactActions=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                var rail=document.querySelector('.action-strip'),items=[...rail.querySelectorAll('.action-button')];
                var expected=['적용','관리','정보','스샷','리뷰','추천','ZIP'];
                return rail.dataset.compactActions==='true' && items.length===7 &&
                  items.every(function(item,i){
                    var short=item.querySelector('.action-label-compact');
                    var full=item.querySelector('.action-label-full');
                    var icon=item.querySelector('.action-icon svg');
                    var box=item.getBoundingClientRect();
                    return short&&short.textContent===expected[i] &&
                      getComputedStyle(short).display!=='none' &&
                      getComputedStyle(full).display==='none' &&
                      icon&&getComputedStyle(item.querySelector('.action-icon')).display!=='none' &&
                      box.width>=60 && box.right<=rail.getBoundingClientRect().right+2 &&
                      item.getAttribute('aria-label')&&item.title;
                  });
            })()"));
            if(!(compactActions is bool)||!(bool)compactActions)
                throw new InvalidOperationException("Narrow action rail did not retain icons and exact abbreviated labels.");
            await hubWeb.ExecuteScriptAsync("document.querySelector('.action-strip').style.width='1180px'");
            bool fullActions=false;
            for(int i=0;i<25&&!fullActions;i++)
            {
                await Task.Delay(100);
                fullActions=Convert.ToBoolean(Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                var rail=document.querySelector('.action-strip'),items=[...rail.querySelectorAll('.action-button')];
                return rail.dataset.compactActions==='false'&&items.length===7 &&
                  items.every(function(item){
                    return getComputedStyle(item.querySelector('.action-label-compact')).display==='none' &&
                      getComputedStyle(item.querySelector('.action-label-full')).display!=='none';
                  });
                })()")));
            }
            if(!fullActions)
            {
                var reason=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"JSON.stringify((function(){
                    var r=document.querySelector('.action-strip');
                    return {width:r.getBoundingClientRect().width,compact:r.dataset.compactActions,
                        windowWidth:innerWidth,items:[...r.querySelectorAll('.action-button')].map(function(b){
                            return {id:b.id,full:getComputedStyle(b.querySelector('.action-label-full')).display,
                                short:getComputedStyle(b.querySelector('.action-label-compact')).display};
                        })};
                })())"));
                throw new InvalidOperationException("Wide action rail did not restore complete button labels: "+reason);
            }
            await hubWeb.ExecuteScriptAsync("document.querySelector('.action-strip').style.removeProperty('width')");
            Console.WriteLine("WEBVIEW RESPONSIVE ACTIONS PASS: allocated-width abbreviations, 7 icons, full tooltips and full labels restored.");
            string quickMenu=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var u=UiLab,f=HangarFeatures,menu=document.getElementById('actionContextMenu'),
                    rail=document.querySelector('.action-strip'),
                    blank=document.querySelector('.campaign-list-region');
                u.setThemeFromHost('terran');u.setView('all');
                if(f.getHiddenActionIds().length)return 'fixture-not-clean';
                function right(node,x,y){
                    node.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,
                        clientX:x||310,clientY:y||230,button:2}));
                }
                var a=document.getElementById('screenshotButton'),b=document.getElementById('installButton');
                right(a);
                var hide=menu.querySelector('[data-action-choice=hide][data-action-id=screenshotButton]');
                if(menu.hidden||!hide)return 'button-right-click-menu';
                hide.click();
                if(!a.hidden||f.getHiddenActionIds().indexOf(a.id)<0)return 'screenshot-not-hidden';
                right(b);
                hide=menu.querySelector('[data-action-choice=hide][data-action-id=installButton]');
                if(!hide)return 'second-button-menu';
                hide.click();
                if(!b.hidden||f.getHiddenActionIds().length!==2)return 'second-button-hide';
                right(blank);
                var opts=[...menu.querySelectorAll('[data-action-choice=restore]')];
                if(menu.hidden||opts.length!==2||
                   opts[0].dataset.actionId!=='installButton'||
                   opts[1].dataset.actionId!=='screenshotButton')return 'saved-action-order';
                u.setThemeFromHost('zerg');
                if(f.getHiddenActionIds().length||a.hidden||b.hidden)return 'theme-scoping';
                u.setThemeFromHost('terran');
                if(!a.hidden||!b.hidden||f.getHiddenActionIds().length!==2)return 'theme-return';
                right(blank);
                if(menu.querySelectorAll('[data-action-choice=restore]').length!==2)return 'blank-restoration-menu';
                return 'ok';
            })()"));
            if(quickMenu!="ok")throw new InvalidOperationException("Quick-action context hide: "+quickMenu);
            using(var png=File.Create(Path.Combine(output,"quick-action-hidden-menu.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            bool hiddenSaved=false;
            for(int attempt=0;attempt<30&&!hiddenSaved;attempt++)
            {
                await Task.Delay(100);
                if(!File.Exists(hubLayoutFile))continue;
                var saved=CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8));
                var terran=saved["terran"].layout.hiddenElements??new string[0];
                var zerg=saved["zerg"].layout.hiddenElements??new string[0];
                hiddenSaved=terran.Contains("installButton")&&terran.Contains("screenshotButton")&&
                    !zerg.Contains("installButton")&&!zerg.Contains("screenshotButton");
            }
            if(!hiddenSaved)throw new InvalidOperationException("Hidden quick actions were not saved to isolated native theme profile.");
            await PushHubLayouts();
            var reread=Data.Json.Deserialize<bool>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var f=HangarFeatures,ids=f.getHiddenActionIds();
                return ids.length===2&&ids.includes('installButton')&&
                    ids.includes('screenshotButton')&&
                    document.getElementById('installButton').hidden&&
                    document.getElementById('screenshotButton').hidden;
            })()"));
            if(!reread)throw new InvalidOperationException("Quick-action hidden choices did not survive host layout reload.");
            string restoredMenu=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var u=UiLab,f=HangarFeatures,blank=document.querySelector('.campaign-list-region'),
                    menu=document.getElementById('actionContextMenu');
                var ids=['launchButton','installButton','detailButton','screenshotButton',
                    'reviewButton','recommendButton','importZipButton'];
                ids.forEach(function(id){f.setActionHidden(id,true);});
                if(f.getHiddenActionIds().length!==7||
                   ids.some(function(id){return !document.getElementById(id).hidden;}))
                    return 'all-hidden-state';
                function right(node){
                    node.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,
                        clientX:340,clientY:255,button:2}));
                }
                right(blank);
                if(menu.hidden||menu.querySelectorAll('[data-action-choice=restore]').length!==7)
                    return 'all-hidden-recovery-menu';
                ids.forEach(function(id){
                    right(blank);
                    var button=menu.querySelector('[data-action-choice=restore][data-action-id='+id+']');
                    if(button)button.click();
                });
                if(f.getHiddenActionIds().length||ids.some(function(id){
                    return document.getElementById(id).hidden;}))return 'restore-all';
                var card=document.querySelector('[data-card=hide-qa-b]');
                if(!card)return 'card-missing';
                right(card);
                var cardMenu=document.getElementById('cardContextMenu'),
                    actions=[...cardMenu.querySelectorAll('[data-card-action]')].map(function(n){return n.dataset.cardAction;});
                if(cardMenu.hidden||actions.slice(0,3).join(',')!=='apply,manage,detail')
                    return 'card-menu-order';
                cardMenu.querySelector('[data-card-action=manage]').click();
                if(document.getElementById('manageOverlay').hidden||
                   document.getElementById('manageOverlay').dataset.campaignId!=='hide-qa-b')
                    return 'card-manage-route';
                f.closeManage();
                var detail=document.getElementById('detailButton'),original=detail.click,called=0;
                detail.click=function(){called++;};
                try{
                    right(card);cardMenu.querySelector('[data-card-action=detail]').click();
                }finally{detail.click=original;}
                if(called!==1||u.getSelected().id!=='hide-qa-b')return 'card-detail-route';
                var savedProfiles=f.getProfiles(),temporary=JSON.parse(JSON.stringify(savedProfiles));
                temporary.terran.layout.presentation='contact';
                f.applyHostProfiles(temporary);u.setView('all');
                var contactCard=document.querySelector('#contactGrid [data-contact-id=hide-qa-b]');
                if(!contactCard)return 'contact-card-missing';
                right(contactCard);
                if(cardMenu.hidden||cardMenu.querySelector('[data-card-action=manage]')===null||
                   cardMenu.querySelector('[data-card-action=detail]')===null)
                    return 'contact-card-context';
                f.applyHostProfiles(savedProfiles);u.setView('all');
                var rail=document.querySelector('.action-strip');
                rail.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,cancelable:true,
                    key:'F10',shiftKey:true}));
                if(menu.hidden||menu.querySelector('.quick-action-menu-empty')===null)
                    return 'keyboard-empty-restore-menu';
                document.dispatchEvent(new KeyboardEvent('keydown',{bubbles:true,cancelable:true,key:'Escape'}));
                if(!menu.hidden)return 'escape-close';
                return 'ok';
            })()"));
            if(restoredMenu!="ok")throw new InvalidOperationException("Quick-action full recovery and card menu: "+restoredMenu);
            bool restoredSaved=false;
            for(int attempt=0;attempt<30&&!restoredSaved;attempt++)
            {
                await Task.Delay(100);
                if(!File.Exists(hubLayoutFile))continue;
                var saved=CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8));
                restoredSaved=!(saved["terran"].layout.hiddenElements??new string[0]).Any(id=>
                    new[] {"launchButton","installButton","detailButton","screenshotButton",
                        "reviewButton","recommendButton","importZipButton"}.Contains(id));
            }
            if(!restoredSaved)throw new InvalidOperationException("Restored quick-action visibility was not persisted.");
            await hubWeb.ExecuteScriptAsync(@"UiLab.setView('all');
                document.querySelector('[data-card=hide-qa-b]').dispatchEvent(
                    new MouseEvent('contextmenu',{bubbles:true,cancelable:true,
                        clientX:480,clientY:280,button:2}));");
            using(var png=File.Create(Path.Combine(output,"campaign-card-context-actions.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            await hubWeb.ExecuteScriptAsync("document.getElementById('cardContextMenu').hidden=true");
            Console.WriteLine("WEBVIEW QUICK ACTION MENU PASS: seven buttons hide/restore, blank-area and keyboard recovery, order and theme isolation, native readback, card manage/detail routing.");
            string behavior=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var ui=UiLab,root=document,hidden=root.getElementById('showHiddenCampaigns');
                var buttons=[...root.querySelectorAll('.action-strip>.action-button')].map(b=>b.id);
                if(buttons.indexOf('recommendButton')!==buttons.indexOf('importZipButton')-1 ||
                   buttons.indexOf('reviewButton')!==buttons.indexOf('recommendButton')-1)
                    return 'quick-action-order';
                var visualButtons=[...root.querySelectorAll('.action-strip>.action-button')]
                    .sort((a,b)=>Number(getComputedStyle(a).order)-Number(getComputedStyle(b).order))
                    .map(b=>b.id);
                if(visualButtons.indexOf('recommendButton')!==visualButtons.indexOf('importZipButton')-1 ||
                   visualButtons.indexOf('reviewButton')!==visualButtons.indexOf('recommendButton')-1)
                    return 'saved-layout-visual-order';
                if(!hidden||hidden.getAttribute('aria-pressed')!=='false'||
                   !root.querySelector('.search-with-hidden').contains(hidden))return 'eye-location';
                var card=root.querySelector('[data-card=hide-qa-a]');
                card.dispatchEvent(new MouseEvent('contextmenu',{bubbles:true,cancelable:true,
                    clientX:100,clientY:100}));
                var menu=root.getElementById('cardContextMenu');
                if(menu.hidden||menu.querySelector('[data-card-action=visibility]').textContent!=='캠페인 숨기기')
                    return 'card-context-visibility';
                menu.querySelector('[data-card-action=visibility]').click();
                if(root.querySelector('[data-card=hide-qa-a]')||
                   root.querySelectorAll('[data-card]').length!==1)return 'card-not-hidden';
                hidden.click();
                if(hidden.getAttribute('aria-pressed')!=='true'||
                   !root.querySelector('[data-card=hide-qa-a].is-hidden-campaign')||
                   !root.querySelector('[data-card=hide-qa-a] .card-hidden-label'))
                    return 'hidden-view-not-restored';
                if(!root.querySelector('#hiddenCampaignCount') ||
                   root.querySelector('#hiddenCampaignCount').textContent!=='1')
                    return 'hidden-counter';
                hidden.click();
                if(root.querySelector('[data-card=hide-qa-a]'))return 'hidden-view-not-closed';
                if(!root.getElementById('recommendButton')||!root.getElementById('reviewButton'))
                    return 'social-buttons-missing';
                return 'ok';
            })()"));
            if(behavior!="ok")throw new InvalidOperationException("Hidden card/quick action UI: "+behavior);
            for(int i=0;i<30&&!File.Exists(hubHiddenCampaignsFile);i++)await Task.Delay(100);
            if(!File.Exists(hubHiddenCampaignsFile) ||
                !(Data.Read<List<string>>(hubHiddenCampaignsFile)??new List<string>()).Contains("hide-qa-a"))
                throw new InvalidOperationException("Hidden card choice did not persist into isolated profile.");
            await hubWeb.ExecuteScriptAsync("UiLab.setHiddenCampaignIds(['hide-qa-a'])");
            var persistent=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "UiLab.filtered().length===1&&UiLab.filtered()[0].id==='hide-qa-b'"));
            if(!(persistent is bool)||!(bool)persistent)
                throw new InvalidOperationException("Hidden campaign list was not restored.");
            Console.WriteLine("WEBVIEW HIDDEN PASS: right-click hide, eye toggle, visible count, isolated disk readback.");
            if((await archiveView.RecommendCampaignAsync("hide-qa-a")).ok)
                throw new InvalidOperationException("Isolated UI must never submit a public recommendation.");
            var reviewUrl=ArchiveView.CampaignReviewUrl(new Campaign {
                id="qa-site",kind="ccm",categories=new[]{"gallery"}
            });
            if(!reviewUrl.Contains("campaign=qa-site&view=reviews#gallery"))
                throw new InvalidOperationException("Review action did not build the site's review-writing deep link.");
            Console.WriteLine("WEBVIEW SOCIAL PASS: review deep-link and offline recommendation guard; no production votes.");
            // Simulate the archive's successful reply only inside this isolated
            // fixture. A successful click bursts into stars instead of opening
            // a message modal. A duplicate retains the 1.5s fading notice.
            var firstVote=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                UiLab.selectCampaign('hide-qa-b');
                UiLab.recommendationResult('hide-qa-b',true,9,'',false);
                var notice=document.getElementById('recommendationNotice');
                var burst=document.getElementById('recommendationBurst');
                return notice.hidden && !burst.hidden &&
                    !!burst.querySelector('.recommendation-burst-core') &&
                    burst.querySelectorAll('.recommendation-burst-star').length===18 &&
                    getComputedStyle(burst).pointerEvents==='none';
            })()"));
            if(!(firstVote is bool)||!(bool)firstVote)
                throw new InvalidOperationException("A successful recommendation did not produce the starburst without a modal.");
            await Task.Delay(250);
            using(var signalScreenshot=File.Create(Path.Combine(output,"recommend-starburst-terran.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,signalScreenshot);
            await Task.Delay(1390);
            var gone=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "document.getElementById('recommendationBurst').hidden"));
            if(!(gone is bool)||!(bool)gone)
                throw new InvalidOperationException("The 1.5-second recommendation stars did not disappear.");
            var repeated=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                document.getElementById('recommendButton').click();
                var notice=document.getElementById('recommendationNotice');
                return !notice.hidden&&notice.dataset.phase==='duplicate' &&
                    document.getElementById('recommendationDescription').textContent===
                      '각 캠페인당 하루 한 번만 추천 가능합니다.';
            })()"));
            if(!(repeated is bool)||!(bool)repeated)
                throw new InvalidOperationException("Second recommendation click did not show the once-per-day warning.");
            await Task.Delay(1215);
            var fading=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "document.getElementById('recommendationNotice').classList.contains('is-fading')"));
            if(!(fading is bool)||!(bool)fading)
                throw new InvalidOperationException("Daily limit notice did not start its soft fade.");
            await Task.Delay(420);
            var repeatDismissed=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "document.getElementById('recommendationNotice').hidden"));
            if(!(repeatDismissed is bool)||!(bool)repeatDismissed)
                throw new InvalidOperationException("Second-click warning did not auto-dismiss.");
            Console.WriteLine("WEBVIEW RECOMMEND SIGNAL PASS: 18-star success burst/no modal, 1.5-second duplicate panel and fade, no public votes.");
            hubEditorQaEnabled=true;
            await hubWeb.ExecuteScriptAsync("UiLab.setHiddenCampaignIds([]);HangarFeatures.beginEdit(null)");
            for(int i=0;i<50&&(hubEditorWeb==null||!hubEditorReady);i++)await Task.Delay(100);
            if(hubEditorWindow==null||hubEditorWeb==null||!hubEditorReady)
                throw new InvalidOperationException("Separate custom editor did not open.");
            if(hubEditorWindow.FormBorderStyle!=FormBorderStyle.Sizable ||
               !hubEditorWindow.MaximizeBox||hubEditorWindow.ClientSize.Width<700)
                throw new InvalidOperationException("Custom editor does not permit horizontal resizing.");
            hubEditorWindow.ClientSize=new Size(560,760);
            bool stacked=false;
            for(int i=0;i<22&&!stacked;i++){
                await Task.Delay(90);
                stacked=Convert.ToBoolean(Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(@"(function(){
                  var a=document.querySelector('.layout-editor-section--geometry').getBoundingClientRect();
                  var b=document.querySelector('.layout-editor-section--colors').getBoundingClientRect();
                  return Math.abs(a.left-b.left)<5 && b.top>a.top+40 &&
                    document.documentElement.scrollWidth<=document.documentElement.clientWidth+3;
                })()")));
            }
            if(!stacked)throw new InvalidOperationException("Narrow custom editor did not use a one-column layout without horizontal overflow.");
            double initialCssWidth=Convert.ToDouble(Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(
                "document.getElementById('editorContent').getBoundingClientRect().width")));
            hubEditorWindow.ClientSize=new Size(1040,800);
            bool medium=false;
            for(int i=0;i<30&&!medium;i++){
                await Task.Delay(100);
                medium=Convert.ToBoolean(Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(
                    "document.getElementById('editorContent').getBoundingClientRect().width>"+
                    (initialCssWidth+85).ToString(System.Globalization.CultureInfo.InvariantCulture)+"&&"+
                    "!!document.querySelector('[data-edit-action=library-save]')&&"+
                    "!!document.querySelector('[data-edit-action=library-import]')&&"+
                    "!!document.querySelector('[data-edit-action=library-export]')&&"+
                    "(function(){var g=document.querySelector('.layout-editor-section--geometry').getBoundingClientRect();"+
                    "var c=document.querySelector('.layout-editor-section--colors').getBoundingClientRect();"+
                    "var v=[...document.querySelectorAll('.layout-editor-choices>label')].map(x=>x.getBoundingClientRect());"+
                    "return v.length===4 && v.every(x=>Math.abs(x.top-v[0].top)<6)&&"+
                    "c.top>g.top+50 && document.documentElement.scrollWidth<=document.documentElement.clientWidth+3;})()")));
            }
            if(!medium)
            {
                var mediumDiag=Data.Json.Deserialize<string>(await hubEditorWeb.ExecuteScriptAsync(@"JSON.stringify((function(){
                    var choices=[...document.querySelectorAll('.layout-editor-choices>label')];
                    return {windowWidth:innerWidth,dashboard:document.querySelector('.layout-editor-dashboard').getBoundingClientRect().width,
                        choiceCount:choices.length,choicePositions:choices.map(x=>Math.round(x.getBoundingClientRect().top)),
                        grid:getComputedStyle(document.querySelector('.layout-editor-choices')).gridTemplateColumns,
                        horizontalOverflow:document.documentElement.scrollWidth-document.documentElement.clientWidth};
                })())"));
                throw new InvalidOperationException("Medium editor failed to align four base selectors in one row: "+mediumDiag);
            }
            hubEditorWindow.ClientSize=new Size(1580,900);
            bool wide=false;
            for(int i=0;i<35&&!wide;i++){
                await Task.Delay(100);
                wide=Convert.ToBoolean(Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(@"(function(){
                    var choices=[...document.querySelectorAll('.layout-editor-choices>label')].map(x=>x.getBoundingClientRect());
                    var metrics=[...document.querySelectorAll('.layout-editor-metrics>label')].map(x=>x.getBoundingClientRect());
                    var panel=document.querySelector('.layout-color-group--panel').getBoundingClientRect();
                    var surfaces=document.querySelector('.layout-color-group--surfaces').getBoundingClientRect();
                    if(choices.length!==4||metrics.length!==8)return false;
                    var choicesSingleRow=choices.every(x=>Math.abs(x.top-choices[0].top)<6);
                    var metrics4x2=metrics.slice(0,4).every(x=>Math.abs(x.top-metrics[0].top)<6)&&
                      metrics.slice(4).every(x=>Math.abs(x.top-metrics[4].top)<6)&&
                      metrics[4].top>metrics[0].top+35 && metrics[1].left>metrics[0].left+20;
                    var colorsSideBySide=surfaces.left>=panel.right-6&&Math.abs(surfaces.top-panel.top)<8;
                    return choicesSingleRow&&metrics4x2&&colorsSideBySide&&
                      document.documentElement.scrollWidth<=document.documentElement.clientWidth+3;
                })()")));
            }
            var editorDiag=Data.Json.Deserialize<string>(await hubEditorWeb.ExecuteScriptAsync(
                "JSON.stringify({width:document.getElementById('editorContent').getBoundingClientRect().width,"+
                "windowWidth:innerWidth,choices:document.querySelectorAll('.layout-editor-choices>label').length,"+
                "metrics:document.querySelectorAll('.layout-editor-metrics>label').length,"+
                "containerWidth:document.querySelector('.layout-editor-dashboard').getBoundingClientRect().width,"+
                "scrollWidth:document.documentElement.scrollWidth})"));
            if(!wide)throw new InvalidOperationException("Widened editor did not render 4 top choices, 4x2 sliders, side-by-side colors: "+editorDiag);
            Console.WriteLine("WEBVIEW GROUP GRID PASS: 4 selectors in one row, 8 sliders in 4x2, panel colors left of menu/list/detail, no horizontal overflow.");
            using(var screenshot=File.Create(Path.Combine(output,"custom-ui-library-wide.png")))
                await hubEditorWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,screenshot);
            await hubEditorWeb.ExecuteScriptAsync(@"(function(){
                var name=document.querySelector('[data-preset-name]');
                name.value='내 테스트 배치';name.dispatchEvent(new Event('input',{bubbles:true}));
                document.querySelector('[data-edit-action=library-save]').click();
                return true;
            })()");
            for(int i=0;i<40&&!File.Exists(hubPresetLibraryFile);i++)await Task.Delay(100);
            if(!File.Exists(hubPresetLibraryFile))
                throw new InvalidOperationException("Named preset did not reach native storage.");
            var store=ReadHubPresetStore();
            if(store.items.Count!=1||store.items[0].name!="내 테스트 배치"||store.items[0].shared)
                throw new InvalidOperationException("First named preset was not retained correctly.");
            var exchange=new HubCustomPresetExchange {
                format="campaign-hangar-ui",version=1,name="공유한 전술 배치",theme="terran",
                layout=CleanHubLayout(new HubUserLayout{menu="right",
                    colors=new Dictionary<string,string>{{"accent","#a1b2c3"}}})
            };
            string file=Path.Combine(output,"share-fixture.chui.json");
            WriteHubPresetShare(file,exchange);
            var parsed=ReadHubPresetExchange(File.ReadAllText(file,Encoding.UTF8));
            if(parsed.name!=exchange.name||parsed.theme!=exchange.theme||
                parsed.layout.menu!="right"||parsed.layout.colors["accent"]!="#a1b2c3")
                throw new InvalidOperationException("Exported UI exchange did not round-trip exact layout and colors.");
            var imported=await ImportHubCustomPresetFile(file);
            store=ReadHubPresetStore();
            if(!imported.shared||store.items.Count!=2||
                !store.items.Any(x=>x.id==imported.id&&x.shared))
                throw new InvalidOperationException("Imported design was not saved alongside the user's own design.");
            var applied=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){var p=HangarFeatures.getProfiles().terran.layout;return "+
                "p.menu==='right'&&p.colors.accent==='#a1b2c3'&&"+
                "HangarFeatures.getPresetLibrary().length===2&&"+
                "document.getElementById('layoutEditor').querySelectorAll('.layout-library-item').length===2;})()"));
            if(!(applied is bool)||!(bool)applied)
                throw new InvalidOperationException("Imported design did not immediately apply and appear in the library.");
            if(!File.Exists(hubLayoutFile) ||
                CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8))["terran"].layout.menu!="right")
                throw new InvalidOperationException("Immediate import did not persist the active custom layout.");
            string invalid=Path.Combine(output,"invalid-share.chui.json");
            File.WriteAllText(invalid,"{\"format\":\"wrong\",\"version\":1,\"name\":\"bad\"}",Encoding.UTF8);
            bool rejected=false;
            try{await ImportHubCustomPresetFile(invalid);}
            catch(InvalidDataException){rejected=true;}
            if(!rejected||ReadHubPresetStore().items.Count!=2)
                throw new InvalidOperationException("Invalid shared UI file modified the preset storage.");
            // Populate only the in-memory fixture with 13 example designs.
            // The real profile store remains the two verified saved presets.
            await hubWeb.ExecuteScriptAsync(@"(function(){
                var original=HangarFeatures.getPresetLibrary();
                var examples=Array.from({length:13},function(_,i){
                    return {id:(i+1).toString(16).padStart(32,'0'),name:'격리 디자인 '+(i+1),
                        theme:'terran',layout:original[0].layout,shared:i%2===0,
                        created:'2026-11-15T00:00:00Z'};
                });
                return HangarFeatures.setPresetLibrary(examples);
            })()");
            bool multiple=false;
            for(int i=0;i<30&&!multiple;i++)
            {
                await Task.Delay(100);
                multiple=Convert.ToBoolean(Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(@"(function(){
                    var cards=[...document.querySelectorAll('.layout-library-item')].map(x=>x.getBoundingClientRect());
                    var pager=document.querySelector('.layout-library-pages');
                    var available=document.querySelector('.layout-editor-dashboard').getBoundingClientRect().width;
                    var expected=available>=1260?4:available>=850?3:0;
                    var columns=getComputedStyle(document.querySelector('.layout-library-list'))
                        .gridTemplateColumns.split(/\s+/).filter(Boolean).length;
                    return expected>=3 && columns===expected && cards.length===12 &&
                        pager && pager.textContent.includes('1 / 2') &&
                        cards.slice(0,columns).every(function(c){return Math.abs(c.top-cards[0].top)<7;}) &&
                        cards.slice(0,columns-1).every(function(c,i){return cards[i+1].left>c.right-5;}) &&
                        cards[columns].top>cards[0].top+35 &&
                        document.documentElement.scrollWidth<=document.documentElement.clientWidth+3;
                })()")));
            }
            if(!multiple)
            {
                var layoutDiag=Data.Json.Deserialize<string>(await hubEditorWeb.ExecuteScriptAsync(@"JSON.stringify((function(){
                    var box=document.querySelector('.layout-library-list');
                    var cards=[...document.querySelectorAll('.layout-library-item')];
                    return {width:box.getBoundingClientRect().width,
                        columns:getComputedStyle(box).gridTemplateColumns,
                        count:cards.length,
                        pager:document.querySelector('.layout-library-pages')&&document.querySelector('.layout-library-pages').textContent,
                        cards:cards.slice(0,5).map(x=>({x:Math.round(x.getBoundingClientRect().left),
                            y:Math.round(x.getBoundingClientRect().top),
                            w:Math.round(x.getBoundingClientRect().width)})),
                        overflow:document.documentElement.scrollWidth-document.documentElement.clientWidth};
                })())"));
                throw new InvalidOperationException("Custom design library did not match its responsive 3/4 columns and pagination: "+layoutDiag);
            }
            await hubWeb.ExecuteScriptAsync("HangarFeatures.editorCommand({action:'button',value:'library-next'})");
            await Task.Delay(150);
            var nextPage=Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(
                "document.querySelectorAll('.layout-library-item').length===1 &&"+
                "document.querySelector('.layout-library-pages').textContent.includes('2 / 2')"));
            if(!(nextPage is bool)||!(bool)nextPage)
                throw new InvalidOperationException("Preset library pagination did not show the thirteenth item.");
            await PushHubPresetLibrary();
            Console.WriteLine("WEBVIEW DESIGN LIBRARY PASS: responsive 3/4 columns, 12 visible items, next page, original saved data restored.");
            await hubWeb.ExecuteScriptAsync("HangarFeatures.editorCommand({action:'button',value:'apply'})");
            for(int i=0;i<40&&hubEditorWindow!=null;i++)await Task.Delay(100);
            if(hubEditorWindow!=null)throw new InvalidOperationException("External editor did not close after saving.");
            hubEditorQaEnabled=false;
            Console.WriteLine("WEBVIEW UI PRESET PASS: native save, JSON export/import, immediate apply, two stored designs, rejection, wide editor.");
        }

        // This runs only from --ui-hub-scroll-check with a temporary,
        // offline storage root. It executes the same WebView2 UI as the
        // deployed archive; no real campaign/game data is opened or written.
        internal async Task VerifyLiveHubScrollAsync(string output)
        {
            if (!isolated || online) throw new InvalidOperationException("Hub visual QA requires isolated offline mode.");
            Directory.CreateDirectory(output);
            for (int i = 0; i < 100 && !hubReady; i++)
                await Task.Delay(100);
            if (!hubReady || hubWeb == null || hubWeb.CoreWebView2 == null)
                throw new InvalidOperationException("WebView2 live archive did not become ready.");
            var siteFirst = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){return window.UiLab.getState().view==='archive' &&" +
                "!document.getElementById('archiveEmbed').hidden &&" +
                "!!window.HangarFeatures && !!document.getElementById('archiveMount');})()"));
            if (!(siteFirst is bool) || !(bool)siteFirst)
                throw new InvalidOperationException("Four-theme startup must open the embedded real archive.");
            bool mounted = false;
            for (int i = 0; i < 20; i++)
            {
                mounted = hubEmbeddedArchive != null && archiveView.Parent == hubEmbeddedArchive &&
                    hubEmbeddedArchive.Visible && hubEmbeddedArchive.Width > 120 &&
                    hubEmbeddedArchive.Height > 100;
                if (mounted) break;
                await Task.Delay(100);
            }
            if (!mounted)
                throw new InvalidOperationException("ArchiveView was not mounted inside the actual themed WebView.");
            Console.WriteLine("WEBVIEW EMBEDDED ARCHIVE PASS: real existing ArchiveView is hosted within the themed UI.");
            if (!await archiveView.VerifyEmbeddedSkipNavigationAsync())
                throw new InvalidOperationException("Embedded skip-to-content link is not hidden and restored by mode.");
            Console.WriteLine("WEBVIEW EMBEDDED SKIP PASS: '본문으로 건너뛰기' hidden in themed view, restored in optimized mode.");
            // Dense scrolling is measured on the custom campaign directory,
            // not on the separate actual website's own scrolling surface. The
            // fixture is installed-only, matching the real user's new catalogue.
            var scrollRows = Enumerable.Range(1,36).Select(i => new LiveHubEntry {
                id="qa-install-"+i,name="설치 격리 캠페인 "+i,en="QA "+i,
                kind="캠페인",category=i%4==0?"전용":i%4==1?"자날":i%4==2?"군심":"공유",
                installed=true,dedicated=i%4==0,author="격리 테스트",img="assets/hangar.jpg",
                desc="격리된 캠페인 항목입니다.",playSeconds=i*3600,
                missions=i%5,bankLinked=i%3==0
            }).ToList();
            await hubWeb.ExecuteScriptAsync("window.UiLab.setRealCampaigns("+
                Data.Json.Serialize(scrollRows)+","+120+")");
            await hubWeb.ExecuteScriptAsync("window.UiLab.setView('all')");
            for (int i = 0; i < 50; i++)
            {
                double count = Convert.ToDouble(Data.Json.DeserializeObject(
                    await hubWeb.ExecuteScriptAsync("document.querySelectorAll('[data-card]').length")));
                if (count >= 24) break;
                if (i == 49) throw new InvalidOperationException("No dense campaign fixture available.");
                await Task.Delay(100);
            }
            string region = "document.querySelector('.campaign-list-region')";
            Func<Task<double>> scrollTop = async delegate {
                return Convert.ToDouble(Data.Json.DeserializeObject(
                    await hubWeb.ExecuteScriptAsync(region + ".scrollTop")));
            };
            Func<string, Task<double>> getNumber = async delegate(string script) {
                return Convert.ToDouble(Data.Json.DeserializeObject(
                    await hubWeb.ExecuteScriptAsync(script)));
            };
            await hubWeb.ExecuteScriptAsync(region + ".scrollTop=0");
            double baseline = await getNumber("window.UiLab.getWheelDistanceForTest(100,0)");
            if (Math.Abs(baseline - 96) > 1)
                throw new InvalidOperationException("Wheel notch must be 2x original 3x16 CSS lines: " + baseline);
            string eventScript = "(function(){var e=new WheelEvent('wheel',{deltaY:100,deltaMode:0,bubbles:true,cancelable:true});" +
                region + ".dispatchEvent(e);return e.defaultPrevented;})()";
            var consumed = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(eventScript));
            if (!(consumed is bool) || !(bool)consumed)
                throw new InvalidOperationException("The dense list did not accept the wheel event.");
            await Task.Delay(330);
            double afterOne = await scrollTop();
            if (Math.Abs(afterOne - baseline) > 3)
                throw new InvalidOperationException("One-notch wheel movement failed: " + afterOne);
            // Interrupt the previous animation and issue four rapid notches.
            await hubWeb.ExecuteScriptAsync("(function(){var r=" + region +
                ";r.dispatchEvent(new PointerEvent('pointerdown',{button:0,bubbles:true}));r.scrollTop=0;" +
                "for(var i=0;i<4;i++)r.dispatchEvent(new WheelEvent('wheel'," +
                "{deltaY:100,deltaMode:0,bubbles:true,cancelable:true}));})()");
            await Task.Delay(350);
            double afterFour = await scrollTop();
            if (Math.Abs(afterFour - baseline * 4) > 5)
                throw new InvalidOperationException("Rapid wheel events queued or jumped: " + afterFour);
            await hubWeb.ExecuteScriptAsync("(function(){var r=" + region +
                ";r.dispatchEvent(new PointerEvent('pointerdown',{button:0,bubbles:true}));" +
                "r.scrollTop=600;})()");
            await Task.Delay(160);
            double dragPosition = await scrollTop();
            if (Math.Abs(dragPosition - 600) > 2)
                throw new InvalidOperationException("Scrollbar drag target was overwritten by a delayed wheel tween: " + dragPosition);
            Console.WriteLine("WEBVIEW SCROLL PASS: cards>=24; one notch="+afterOne+
                " CSSpx, four rapid notches="+afterFour+" CSSpx, direct thumb="+dragPosition+" CSSpx.");
            // Regression: switching archive tabs, searching or filtering must
            // never leave a stale hero/detail/launch target from another tab.
            string installedMatch = "(function(){var ui=window.UiLab;ui.setView('installed');" +
                "var list=ui.filtered(),id=ui.getState().selected;" +
                "var item=list.find(function(c){return c.id===id});" +
                "return list.every(function(c){return c.installed})&&" +
                "(list.length ? id===list[0].id : id===null)&&" +
                "(item ? (document.getElementById('heroTitle').textContent===item.name&&" +
                "document.getElementById('detailTitle').textContent===item.name&&" +
                "!document.getElementById('launchButton').disabled) :" +
                "(list.length===0&&id===null&&document.getElementById('launchButton').disabled));})()";
            string dedicatedMatch = "(function(){var ui=window.UiLab;ui.setView('dedicated');" +
                "var list=ui.filtered(),id=ui.getState().selected;" +
                "var item=list.find(function(c){return c.id===id});" +
                "return list.every(function(c){return c.dedicated})&&" +
                "(list.length ? id===list[0].id : id===null)&&" +
                "(item ? (document.getElementById('heroTitle').textContent===item.name&&" +
                "document.getElementById('detailTitle').textContent===item.name&&" +
                "!document.getElementById('launchButton').disabled) :" +
                "(list.length===0&&id===null&&document.getElementById('launchButton').disabled));})()";
            string emptyMatch = "(function(){var ui=window.UiLab;ui.getState().favorites.clear();" +
                "ui.setView('favorites');return ui.filtered().length===0&&" +
                "ui.getState().selected===null&&" +
                "document.getElementById('launchButton').disabled&&" +
                "document.getElementById('installButton').disabled&&" +
                "document.getElementById('heroTitle').textContent==='조건에 맞는 캠페인이 없습니다'&&" +
                "document.getElementById('statusMessage').textContent.indexOf('조건에 맞는')>=0;})()";
            string searchMatch = "(function(){var ui=window.UiLab;ui.setView('all');" +
                "var input=document.getElementById('campaignSearch');input.value='no-such-campaign-zzzzzz';" +
                "input.dispatchEvent(new Event('input',{bubbles:true}));" +
                "var empty=ui.filtered().length===0&&ui.getState().selected===null&&" +
                "!document.getElementById('favoriteButton').disabled&&" +
                "document.getElementById('statusMessage').textContent.indexOf('조건에 맞는')>=0;" +
                "input.value='';input.dispatchEvent(new Event('input',{bubbles:true}));" +
                "return empty&&ui.filtered().length>=24&&!!ui.getState().selected;})()";
            foreach (var regression in new[] {
                new {Name="installed list",Code=installedMatch},
                new {Name="dedicated list",Code=dedicatedMatch},
                new {Name="empty favorites",Code=emptyMatch},
                new {Name="empty search",Code=searchMatch}
            })
            {
                var result = Data.Json.DeserializeObject(
                    await hubWeb.ExecuteScriptAsync(regression.Code));
                if (!(result is bool) || !(bool)result)
                    throw new InvalidOperationException("WebView selection regression: " + regression.Name);
            }
            Console.WriteLine("WEBVIEW SELECTION PASS: installed/dedicated/empty favorites/empty search never leave stale hero or active launch targets.");
            foreach (var palette in LiveTheme.Choices)
            {
                ChangeLiveTheme(palette, false);
                await hubWeb.ExecuteScriptAsync("window.UiLab.setThemeFromHost(" +
                    Data.Json.Serialize(palette.Id) + ")");
                await Task.Delay(110);
                var themeId = Data.Json.Deserialize<string>(
                    await hubWeb.ExecuteScriptAsync("document.body.dataset.theme"));
                if (themeId != palette.Id)
                    throw new InvalidOperationException("WebView skin could not switch to " + palette.Id);
                using (var image = File.Create(Path.Combine(output, palette.Id + ".png")))
                    await hubWeb.CoreWebView2.CapturePreviewAsync(
                        CoreWebView2CapturePreviewImageFormat.Png, image);
            }
            Console.WriteLine("WEBVIEW THEME PASS: four actual WebView2 screenshots captured.");

            // Exercise the real embedded UI handlers with synthetic entries
            // after screenshots. This temporary, isolated WebView never
            // reaches user campaign registrations or a StarCraft installation.
            string viewContract = @"(function(){
                var rows=[
                    {id:'qa-dedicated',name:'전용 격리 캠페인',kind:'전용',category:'전용',
                     installed:false,dedicated:true,author:'QA',img:'assets/hangar.jpg',desc:'test'},
                    {id:'qa-installed',name:'설치 격리 캠페인',kind:'자날',category:'자날',
                     installed:true,dedicated:false,author:'QA',img:'assets/hangar.jpg',desc:'test'},
                    {id:'qa-other',name:'미설치 격리 캠페인',kind:'군심',category:'군심',
                     installed:false,dedicated:false,author:'QA',img:'assets/hangar.jpg',desc:'test'}
                ];
                var ui=window.UiLab;
                if(document.title!=='커캠 격납고 · 캠페인 아카이브' ||
                   document.querySelector('.boot-bottom > span').textContent.indexOf('DEMO DATA')>=0)return false;
                if(!ui.setRealCampaigns(rows))return false;
                ui.setView('dedicated');
                if(ui.getState().selected!=='qa-dedicated' ||
                   document.getElementById('heroTitle').textContent!=='전용 격리 캠페인' ||
                   document.getElementById('primaryActionText').textContent!=='캠페인 실행' ||
                   ui.filtered().length!==1)return false;
                ui.setView('installed');
                if(ui.getState().selected!=='qa-installed' ||
                   document.getElementById('detailTitle').textContent!=='설치 격리 캠페인' ||
                   document.getElementById('primaryActionText').textContent!=='캠페인 적용' ||
                   ui.filtered().length!==1)return false;
                if(document.querySelector('[data-card=qa-installed] .card-status') ||
                   !document.getElementById('detailInstalled').parentElement.hidden)return false;
                if(!ui.setActivationState({activeName:'테스트 현재 적용',previousName:'테스트 직전 적용'}) ||
                   document.getElementById('activeCampaignName').textContent!=='테스트 현재 적용' ||
                   document.getElementById('previousCampaignName').textContent!=='테스트 직전 적용')return false;
                ui.setFavoriteIds(['qa-dedicated']);
                ui.setView('favorites');
                if(ui.getState().selected!=='qa-dedicated' ||
                   ui.filtered().length!==1)return false;
                // A user removing their last favorite must leave a safe empty
                // list and still receive a toast for the item just removed.
                document.querySelector('[data-card=qa-dedicated] .card-favorite').click();
                if(ui.getState().selected!==null || ui.filtered().length!==0 ||
                   document.getElementById('favoriteButton').disabled ||
                   document.getElementById('statusMessage').textContent.indexOf('조건에 맞는')<0 ||
                   document.getElementById('toastMessage').textContent.indexOf('전용 격리 캠페인')<0 ||
                   document.getElementById('toastMessage').textContent.indexOf('해제했습니다')<0)return false;
                ui.setFavoriteIds([]);
                if(ui.getState().selected!==null ||
                   !document.getElementById('launchButton').disabled ||
                   !document.getElementById('installButton').disabled ||
                   document.getElementById('emptyState').hidden)return false;
                ui.setView('all');
                if(ui.filtered().length!==3 || ui.getState().selected===null)return false;
                var search=document.getElementById('campaignSearch');
                search.value='찾을수없는문구';
                search.dispatchEvent(new Event('input',{bubbles:true}));
                if(ui.filtered().length!==0 || ui.getState().selected!==null ||
                   !document.getElementById('launchButton').disabled ||
                   document.getElementById('statusMessage').textContent.indexOf('조건에 맞는')<0)return false;
                search.value='';
                search.dispatchEvent(new Event('input',{bubbles:true}));
                if(ui.filtered().length!==3 || ui.getState().selected===null)return false;
                ui.setView('settings');
                if(document.getElementById('settingsPanel').hidden ||
                   !document.getElementById('hero').hidden ||
                   !document.querySelector('.action-strip').hidden)return false;
                ui.setView('all');
                if(document.getElementById('catalogPanel').hidden ||
                   document.getElementById('hero').hidden ||
                   document.querySelector('.action-strip').hidden)return false;
                search.value='전용';
                search.dispatchEvent(new Event('input',{bubbles:true}));
                ui.setView('all',true);
                return ui.getState().search==='' && ui.getState().category==='all' &&
                       search.value==='' && ui.getState().selected===ui.filtered()[0].id;
            })()";
            var viewResult = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(viewContract));
            if (!(viewResult is bool) || !(bool)viewResult)
                throw new InvalidOperationException("Embedded archive filter/selection/settings contract failed: " +
                    (viewResult == null ? "<null>" : viewResult.ToString()));
            Console.WriteLine("WEBVIEW LIVE VIEW PASS: installed/dedicated/favorites/empty-search restore selection and disable unavailable actions; settings in themed UI.");
            string workProgressContract=@"(function(){
                var u=window.UiLab;
                if(!u.setWorkProgress({sequence:10001,active:true,title:'Test ZIP',stage:'Bytes copied',
                    completed:1048576,total:4194304,bytes:true,fullscreen:true,cancelable:true}))return 'start';
                if(document.getElementById('boot').classList.contains('is-hidden')||
                   document.getElementById('bootPercent').textContent!=='25%'||
                   document.getElementById('bootWorkAmount').textContent.indexOf('1.0 / 4.0 MB')<0||
                   !document.getElementById('skipBoot').hidden ||
                   document.getElementById('bootWorkCancel').hidden||
                   document.getElementById('bootBrowse').textContent!=='둘러보기')return 'measured-bytes';
                document.getElementById('bootBrowse').click();
                if(!document.getElementById('boot').classList.contains('is-compact-work')||
                   document.body.classList.contains('is-booting')||
                   document.getElementById('bootPercent').textContent!=='25%'||
                   document.getElementById('bootBrowse').textContent!=='크게 보기')return 'browse-retains-progress';
                u.setWorkProgress({sequence:10001.5,active:true,fullscreen:true,completed:2,total:4});
                if(!document.getElementById('boot').classList.contains('is-compact-work')||
                   document.getElementById('bootPercent').textContent!=='50%')return 'browse-survives-progress';
                document.getElementById('bootBrowse').click();
                if(document.getElementById('boot').classList.contains('is-compact-work')||
                   !document.body.classList.contains('is-booting')||
                   document.getElementById('bootBrowse').textContent!=='둘러보기')return 'browse-expand';
                u.setWorkProgress({sequence:10002,active:true,title:'ZIP unknown total',stage:'Reading',
                    completed:0,total:0,bytes:true,fullscreen:true});
                if(!document.getElementById('boot').classList.contains('is-indeterminate')||
                   document.getElementById('bootPercent').textContent!=='측정 중')return 'indeterminate';
                u.setWorkProgress({sequence:10003,active:false});
                if(!document.getElementById('boot').classList.contains('is-hidden'))return 'finish';
                var displayToggle=document.getElementById('workBrowseToggle');
                if(!displayToggle || !displayToggle.textContent.includes('전체 화면'))return 'work-setting-missing';
                displayToggle.click();
                if(!displayToggle.textContent.includes('진행 박스만')||
                    displayToggle.getAttribute('aria-pressed')!=='true')return 'work-setting-label';
                u.setWorkProgress({sequence:10004,active:true,fullscreen:false});
                if(!document.getElementById('boot').classList.contains('is-hidden'))return 'compact';
                u.setWorkProgress({sequence:10005,active:false});
                u.setWorkProgress({sequence:10005.1,active:true,fullscreen:true});
                if(!document.getElementById('boot').classList.contains('is-compact-work')||
                   document.body.classList.contains('is-booting'))return 'saved-window-mode';
                u.setWorkProgress({sequence:10005.2,active:false});
                displayToggle.click();
                if(!displayToggle.textContent.includes('전체 화면')||
                    displayToggle.getAttribute('aria-pressed')!=='false')return 'fullscreen-default-label';
                u.setWorkProgress({sequence:10005.3,active:true,fullscreen:true});
                if(document.getElementById('boot').classList.contains('is-compact-work')||
                   !document.body.classList.contains('is-booting'))return 'fullscreen-default';
                u.setWorkProgress({sequence:10005.4,active:false});
                u.setActivationState({activeId:'qa-other',previousId:'qa-installed',
                    activeName:'First',previousName:'Second'});
                u.setView('all');
                if(u.filtered()[0].id!=='qa-other'||u.filtered()[1].id!=='qa-installed')return 'history-order';
                if(!u.revealCampaign('qa-other','군심')||u.getState().view!=='hots'||
                   u.getState().selected!=='qa-other'||
                   document.getElementById('heroTitle').textContent!==u.getSelected().name||
                   document.getElementById('detailCategory').textContent.indexOf('현재 적용')<0)
                   return 'reveal-import';
                u.setView('all');return 'ok';
            })()";
            var workResult=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(workProgressContract));
            if (!(workResult is string) || (string)workResult!="ok")
                throw new InvalidOperationException("Unified real-work overlay/reveal contract failed: "+workResult);
            Console.WriteLine("WEBVIEW WORK & REVEAL PASS: measured/unknown progress, restored work display setting, separate-window persistence, fullscreen default, independent race loading, active history and category selection.");
            // Confirm that the *native* WorkProgress reporter, rather than
            // a browser-only fabricated progress value, reaches the UI.
            hubProgressSequence = 10006;
            var realProgress = RunWork("QA ZIP 진행 이벤트", reporter => {
                reporter(new WorkProgress("실측 바이트 복사 중",1024*1024,4*1024*1024,true));
                System.Threading.Thread.Sleep(1900);
                return true;
            });
            bool nativeReached=false;
            for(int attempt=0;attempt<14;attempt++)
            {
                await Task.Delay(100);
                var nativeProgress=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                    "(function(){return !document.getElementById('boot').classList.contains('is-hidden')&&"+
                    "document.getElementById('bootPercent').textContent==='25%'&&"+
                    "document.getElementById('bootStep').textContent.indexOf('실측 바이트')>=0;})()"));
                nativeReached=nativeProgress is bool&&(bool)nativeProgress;
                if(nativeReached)break;
            }
            if(!nativeReached)
            {
                var uiState=await hubWeb.ExecuteScriptAsync(
                    "(function(){return JSON.stringify({className:document.getElementById('boot').className,"+
                    "percent:document.getElementById('bootPercent').textContent,"+
                    "stage:document.getElementById('bootStep').textContent});})()");
                await realProgress;
                throw new InvalidOperationException("Native WorkProgress did not reach themed fullscreen loader. Actual: "+uiState);
            }
            using(var image=File.Create(Path.Combine(output,"work-native-progress.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await realProgress;
            await Task.Delay(110);
            string browseSurface=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                UiLab.setView('archive');
                UiLab.setWorkProgress({sequence:10020,active:true,fullscreen:true,browse:false,
                    title:'격리 검수 · 다운로드 및 등록',stage:'파일 확인 중',completed:1048576,total:4194304,bytes:true,cancelable:true});
                var original=document.querySelector('.boot-center').getBoundingClientRect();
                document.getElementById('bootBrowse').click();
                var box=document.getElementById('boot').getBoundingClientRect();
                var center=document.querySelector('.boot-center').getBoundingClientRect();
                var style=getComputedStyle(document.getElementById('boot'));
                return box.width<=670&&center.right<=box.right+1&&center.bottom<=box.bottom+1&&
                    Math.abs(box.left-original.left)<1&&Math.abs(box.top-original.top)<1&&style.backgroundImage==='none'&&
                    style.borderTopWidth==='0px'&&getComputedStyle(document.getElementById('boot'),'::before').display==='none'?
                    'ok':'compact-work-overlap-or-artwork';
            })()"));
            if(browseSurface!="ok")throw new InvalidOperationException(browseSurface);
            await Task.Delay(100);
            using(var image=File.Create(Path.Combine(output,"work-browse-compact.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("UiLab.setWorkProgress({sequence:10021,active:false});UiLab.setView('all')");
            hubProgressSequence=10022;
            Console.WriteLine("WEBVIEW NATIVE WORK PASS: actual WorkProgress event rendered as 25%/1 of 4 MB then closed.");
            var selectionSpeed=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                var u=window.UiLab;u.setView('all');
                var ids=u.getCampaigns().map(function(c){return c.id;});
                if(ids.length<3)return -1;
                var start=performance.now();
                for(var i=0;i<30;i++)u.selectCampaign(ids[i%ids.length]);
                window.__selectionLatency=performance.now()-start;
                return window.__selectionLatency;
            })()"));
            if(selectionSpeed==null || Convert.ToDouble(selectionSpeed,System.Globalization.CultureInfo.InvariantCulture)>1500 ||
                Convert.ToDouble(selectionSpeed,System.Globalization.CultureInfo.InvariantCulture)<0)
                throw new InvalidOperationException("Selection handlers must remain responsive: "+selectionSpeed);
            Console.WriteLine("WEBVIEW SELECTION LATENCY PASS: 30 selections in "+selectionSpeed+" ms, without rebuilding all cards.");
            string newMenuContract = @"(function(){
                var u=window.UiLab;
                if(document.querySelectorAll('.sidebar .nav-link[data-view]').length!==9||
                   document.querySelectorAll('.side-nav [data-view]').length!==6)return 'tab-count';
                if(document.querySelector('.sidebar [data-view=installed]')||
                   document.querySelector('.sidebar [data-view=favorites]'))return 'old-tabs';
                if(!['all','wol','hots','lotv','nova','dedicated'].every(function(k){
                  return !!document.querySelector('.side-nav [data-view='+k+']');}))return 'species-tabs';
                var headings=Array.from(document.querySelectorAll('.sidebar>.side-group-title'));
                if(headings.length!==4||[['01','아카이브'],['02','작전 메뉴'],
                   ['03','화면 테마'],['04','환경설정']].some(function(pair,i){
                   var labels=Array.from(headings[i].querySelectorAll('span')).slice(0,2);
                   return labels.length!==2||labels[0].textContent.trim()!==pair[0]||
                     labels[1].textContent.trim()!==pair[1];}))return 'section-headings';
                if(!document.querySelector('.archive-nav [data-view=archive]')||
                   !document.querySelector('.settings-nav [data-view=settings]')||
                   !document.querySelector('.sidebar>.layout-nav-link[data-view=custom]'))return 'section-membership';
                if(document.querySelector('.side-quick-actions').compareDocumentPosition(
                    document.querySelector('.archive-caption'))&Node.DOCUMENT_POSITION_PRECEDING)
                   return 'quick-actions-position';
                var ordered=Array.from(document.querySelectorAll('.side-nav [data-view]'));
                ordered.sort(function(a,b){return Number(a.style.order)-Number(b.style.order);});
                if(ordered.indexOf(document.querySelector('.side-nav [data-view=all]')) >
                   ordered.indexOf(document.querySelector('.side-nav [data-view=wol]')))return 'all-before-wol';
                if(ordered.indexOf(document.querySelector('.side-nav [data-view=nova]'))!==
                   ordered.indexOf(document.querySelector('.side-nav [data-view=lotv]'))+1)return 'nova-after-lotv';
                var marks=Array.from(document.querySelectorAll('.theme-option .theme-emblem img'));
                if(marks.length!==4||marks.some(function(mark){return !mark.complete||mark.naturalWidth===0;}))
                   return 'theme-artwork-load';
                var choices=Array.from(document.querySelectorAll('.theme-option')).map(function(e){return e.getBoundingClientRect();});
                if(Math.abs(choices[0].top-choices[1].top)>1||choices[2].top<choices[0].bottom-1||
                   Math.abs(choices[2].top-choices[3].top)>1)return 'theme-two-by-two';
                u.setTheme('terran',false,true);
                var brand=document.querySelector('#brandGlyph img');
                if(!brand||brand.getAttribute('src')!==marks[0].getAttribute('src')||
                   getComputedStyle(document.getElementById('brandGlyph')).visibility==='hidden')return 'brand-theme-artwork';
                if(!getComputedStyle(document.querySelector('.action-button'),'::after').backgroundImage.includes('linear-gradient'))
                   return 'action-gradient-missing';
                var fixtures=[
                  {id:'qa-wol',name:'설치 자날',category:'자날',installed:true,kind:'캠페인',playSeconds:5400,missions:2,bankLinked:true},
                  {id:'qa-hots',name:'설치 군심',category:'군심',installed:true,kind:'캠페인'},
                  {id:'qa-lotv',name:'설치 공유',category:'공유',installed:true,kind:'캠페인'},
                  {id:'qa-nova',name:'설치 노바',category:'노바',installed:true,kind:'캠페인'},
                  {id:'qa-ded',name:'설치 전용',category:'전용',installed:true,dedicated:true,kind:'전용'},
                  {id:'qa-uninstalled',name:'미설치 외부',category:'자날',installed:false,kind:'캠페인'}
                ];
                u.setRealCampaigns(fixtures,127);
                if(u.getCampaigns().length!==6)return 'fixture';
                u.setView('wol');
                if(u.filtered().length!==1||u.filtered()[0].id!=='qa-wol')return 'wol-filter';
                if(document.getElementById('recordPlayTime').textContent!=='1.5시간'||
                   document.getElementById('recordMissions').textContent!=='2개')return 'record-summary';
                var action=document.getElementById('favoriteButton');
                if(action.disabled)return 'favorites-disabled';
                u.getState().favorites.add('qa-wol');action.click();
                if(!u.getState().favoritesOnly||u.filtered().length!==1)return 'favorites-filter-on';
                action.click();
                if(u.getState().favoritesOnly)return 'favorites-filter-off';
                ['hots','lotv','nova','dedicated'].forEach(function(view){
                  u.setView(view);
                  if(u.filtered().length!==1)throw Error('slot:'+view);
                });
                u.setView('all');
                u.setActivityTotals({cleared:2,seconds:7200});
                if(u.filtered().length!==5||document.getElementById('statTotal').textContent!=='2개'||
                   document.getElementById('statPlayTime').textContent!=='2.0시간')
                  return 'installed-only';
                u.setView('archive');return 'ok';
            })()";
            var menuResult = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(newMenuContract));
            if (!String.Equals(menuResult as string,"ok",StringComparison.Ordinal))
                throw new InvalidOperationException("Installed-only four-category/favorites/records UI contract: "+menuResult);
            Console.WriteLine("WEBVIEW INSTALLED NAV PASS: Nova and other installed categories, ordered navigation, favorites and tracked records.");
            // Capture each real preset with the isolated catalogue fixture.
            // These screenshots exercise the selected WebView layout, not a
            // poster or static mockup, and never touch the live user profile.
            foreach (var presetId in new[] {"base","focus","list","contact"})
            {
                await hubWeb.ExecuteScriptAsync("(function(){window.UiLab.setView('all');"+
                    "window.UiLab.setTheme('terran',false,true);"+
                    "document.querySelector('[data-preset=" + presetId + "]').click();})()");
                await Task.Delay(160);
                await hubWeb.ExecuteScriptAsync("document.getElementById('toast').hidden=true");
                using (var image = File.Create(Path.Combine(output,"preset-"+presetId+".png")))
                    await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            }
            Console.WriteLine("WEBVIEW PRESET SCREENSHOTS PASS: base/focus/list/contact captured from actual live WebView2 DOM.");
            await hubWeb.ExecuteScriptAsync("(function(){window.UiLab.setTheme('terran',false,true);"+
                "window.UiLab.setView('all');var s=document.querySelector('.sidebar');"+
                "s.scrollTop=s.scrollHeight-s.clientHeight;})()");
            await Task.Delay(120);
            using (var image = File.Create(Path.Combine(output,"menu-sections-bottom.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("document.querySelector('.sidebar').scrollTop=0");
            Console.WriteLine("WEBVIEW SECTION SCROLL PASS: edit and settings are available below the operations list.");
            // User-facing layout editor is tested with real WebView2 DOM and
            // an isolated user profile; no installed campaigns are mutated.
            string layoutContract = @"(function(){
                var ui=window.UiLab,f=window.HangarFeatures;
                if(!f||!document.getElementById('layoutEditor')||
                   !document.getElementById('archiveEmbed'))return false;
                ui.setView('all');ui.setTheme('terran',false,true);
                var protossBefore=JSON.stringify(f.getLayout('protoss'));
                function choose(k){document.getElementById('layoutSettings').querySelector('[data-preset='+k+']').click();}
                window.__layoutQaStage='preset-base';choose('base');
                window.__layoutQaDiag=[document.body.dataset.layoutPreset,document.body.dataset.layoutMenu,
                  getComputedStyle(document.getElementById('hero')).display,
                  getComputedStyle(document.getElementById('detailPanel')).display,
                  f.getProfiles().terran.preset,document.getElementById('hero').hidden,
                  document.getElementById('catalogPanel').hidden].join('|');
                if(document.body.dataset.layoutPreset!=='base'||
                   document.body.dataset.layoutMenu!=='left'||
                   getComputedStyle(document.getElementById('hero')).display==='none'||
                   getComputedStyle(document.getElementById('detailPanel')).display==='none')return false;
                window.__layoutQaStage='preset-focus';choose('focus');
                if(document.body.dataset.layoutPreset!=='focus'||
                   getComputedStyle(document.getElementById('focusDetails')).display==='none'||
                   getComputedStyle(document.getElementById('detailPanel')).display!=='none'||
                   document.getElementById('focusAuthor').textContent==='—')return false;
                window.__layoutQaStage='preset-list';choose('list');
                if(document.body.dataset.layoutPreset!=='list'||
                   document.body.dataset.layoutMenu!=='top'||
                   getComputedStyle(document.getElementById('hero')).display!=='none'||
                   getComputedStyle(document.getElementById('detailPanel')).display==='none')return false;
                window.__layoutQaStage='preset-list-theme-switcher';
                var themeSwitch=document.querySelector('.sidebar>.theme-switcher');
                var themeButtons=Array.prototype.slice.call(themeSwitch.querySelectorAll('.theme-option'));
                if(getComputedStyle(themeSwitch).display!=='grid'||themeButtons.length!==4||
                   themeButtons.some(function(btn){var b=btn.getBoundingClientRect();
                     return b.width<24||b.left<0||b.right>window.innerWidth-2;}))return false;
                window.__layoutQaStage='preset-contact';choose('contact');
                var grid=document.getElementById('contactGrid');
                if(document.body.dataset.layoutPreset!=='contact'||grid.hidden||
                   grid.querySelectorAll('.contact-cell').length!==6||
                   getComputedStyle(grid).gridTemplateColumns.split(' ').length!==(innerWidth<=900?1:2)||
                   !grid.querySelector('[data-contact-items=nova] [data-contact-id=qa-nova]')||
                   !grid.querySelector('[data-contact-items=wol] [data-contact-id=qa-wol]'))return false;
                window.__layoutQaStage='preset-base-return';choose('base');
                if(f.isEditing()||document.body.dataset.layoutDetail!=='right' ||
                   f.getProfiles().terran.preset!=='base')return false;
                window.__layoutQaStage='begin-edit';f.beginEdit(null);
                if(!f.isEditing()||!document.getElementById('layoutEditor').hidden)return false;
                if(!document.querySelector('.layout-editor-header [data-edit-action=apply]') ||
                   !document.getElementById('layoutAllThemes')||
                   document.querySelectorAll('[data-layout-color]').length!==20)return false;
                window.__layoutQaStage='edit-menu';var menu=document.querySelector('[data-layout-field=menu]');
                menu.value='right';menu.dispatchEvent(new Event('change',{bubbles:true}));
                if(document.body.dataset.layoutMenu!=='right')return false;
                document.dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true}));
                if(document.body.dataset.layoutMenu!=='left')return false;
                document.dispatchEvent(new KeyboardEvent('keydown',{key:'y',ctrlKey:true,bubbles:true}));
                if(document.body.dataset.layoutMenu!=='right')return false;
                window.__layoutQaStage='confirm-save';f.editorCommand({action:'close'});
                if(document.getElementById('layoutExitConfirm').hidden)return false;
                document.querySelector('[data-close-choice=yes]').click();
                if(f.isEditing()||f.getLayout('terran').menu!=='right')return false;
                window.__layoutQaStage='protoss-independence';ui.setTheme('protoss',false,true);
                if(JSON.stringify(f.getLayout('protoss'))!==protossBefore ||
                   document.body.dataset.layoutMenu!=='left')return false;
                window.__layoutQaStage='terran-restored';ui.setTheme('terran',false,true);
                if(document.body.dataset.layoutMenu!=='right')return false;
                window.__layoutQaStage='cancel-focus';f.beginEdit('focus');
                if(document.body.dataset.layoutDetail!=='hidden')return false;
                f.editorCommand({action:'close'});
                if(document.getElementById('layoutExitConfirm').hidden)return false;
                document.querySelector('[data-close-choice=no]').click();
                if(document.body.dataset.layoutDetail!=='right' ||
                   f.getLayout('terran').menu!=='right')return false;
                if(f.isEditing())return false;
                window.__layoutQaStage='manage';var selected=ui.getSelected();
                if(!selected||ui.getState().view!=='all')return false;
                f.openManage(selected);
                if(document.getElementById('manageOverlay').hidden ||
                   document.getElementById('manageTitle').textContent!==selected.name ||
                   ui.getState().view!=='all')return false;
                f.closeManage();
                ui.setView('archive');
                return !document.getElementById('archiveEmbed').hidden &&
                    document.getElementById('catalogPanel').hidden &&
                    document.getElementById('manageOverlay').hidden;
            })()";
            var layoutResult = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(layoutContract));
            if (!(layoutResult is bool) || !(bool)layoutResult)
                throw new InvalidOperationException("Themed user-layout, edit history, management panel or archive contract failed at " +
                    Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync("window.__layoutQaStage")) + " · " +
                    Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync("window.__layoutQaDiag")) + ".");
            await Task.Delay(250);
            if (!File.Exists(hubLayoutFile))
                throw new InvalidOperationException("Layout bridge did not write to the isolated user profile.");
            string savedLayouts = File.ReadAllText(hubLayoutFile,Encoding.UTF8);
            var validated = CleanHubProfiles(savedLayouts);
            if (!validated.ContainsKey("terran") || validated["terran"].layout.menu != "right")
                throw new InvalidOperationException("Theme layout did not persist.");
            await PushHubLayouts();
            var persistedResult = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){var p=window.HangarFeatures.getProfiles();return p.terran.layout.menu==='right'&&"+
                "p.protoss.layout.menu==='left';})()"));
            if (!(persistedResult is bool) || !(bool)persistedResult)
                throw new InvalidOperationException("Saved layout did not restore independently for each species.");
            // Custom colors: live preview, per-species isolation, native storage,
            // reset preview/cancel and reloading the persisted profile.
            var colorsResult = Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var ui=window.UiLab,f=window.HangarFeatures;
                ui.setView('all');ui.setTheme('terran',false,true);
                var protoss=JSON.stringify(f.getLayout('protoss'));
                f.beginEdit(null);
                var picker=document.querySelector('[data-layout-color=accent]');
                if(!picker)return 'missing-color-input';
                picker.value='#fc9338';
                picker.dispatchEvent(new Event('input',{bubbles:true}));
                picker.dispatchEvent(new Event('change',{bubbles:true}));
                if(getComputedStyle(document.body).getPropertyValue('--accent').trim()!=='#fc9338')
                  return 'preview-color';
                document.querySelector('[data-edit-action=apply]').click();
                if(f.isEditing()||f.getLayout('terran').colors.accent!=='#fc9338')
                  return 'color-save';
                ui.setTheme('protoss',false,true);
                if(JSON.stringify(f.getLayout('protoss'))!==protoss ||
                   getComputedStyle(document.body).getPropertyValue('--accent').trim()==='#fc9338')
                  return 'cross-theme-color';
                ui.setTheme('terran',false,true);
                if(getComputedStyle(document.body).getPropertyValue('--accent').trim()!=='#fc9338')
                  return 'restored-color';
                f.beginEdit(null);
                document.querySelector('[data-edit-action=reset-colors]').click();
                if(getComputedStyle(document.body).getPropertyValue('--accent').trim()==='#fc9338')
                  return 'color-reset-preview';
                f.editorCommand({action:'close'});
                document.querySelector('[data-close-choice=no]').click();
                if(f.isEditing()||f.getLayout('terran').colors.accent!=='#fc9338')
                  return 'color-reset-cancel';
                f.beginEdit('contact');
                var left=document.querySelector('.contact-cell[data-contact-cell=detail]');
                var target=document.querySelector('.contact-cell[data-contact-cell=hots]');
                if(!left.draggable||!target)return 'contact-drag-disabled';
                var data=new DataTransfer();
                left.dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:data}));
                target.dispatchEvent(new DragEvent('drop',{bubbles:true,cancelable:true,dataTransfer:data}));
                var order=f.getEditingLayout().contactOrder;
                if(order.indexOf('detail')!==order.indexOf('hots')+1)return 'contact-drag-order';
                f.editorCommand({action:'close'});
                document.querySelector('[data-close-choice=no]').click();
                return 'ok';
            })()"));
            if (colorsResult != "ok")
                throw new InvalidOperationException("Themed custom colors/contact drag contract failed: " + colorsResult);
            await Task.Delay(180);
            var storedColors = CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8));
            string selectedColor;
            if (!storedColors["terran"].layout.colors.TryGetValue("accent",out selectedColor) ||
                selectedColor != "#fc9338" || storedColors["protoss"].layout.colors.ContainsKey("accent"))
                throw new InvalidOperationException("Custom color profile failed native persistence/isolation.");
            await PushHubLayouts();
            var colorRestored = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "window.HangarFeatures.getLayout('terran').colors.accent==='#fc9338'"));
            if (!(colorRestored is bool) || !(bool)colorRestored)
                throw new InvalidOperationException("Custom color profile failed native reload.");
            Console.WriteLine("WEBVIEW COLOR & CONTACT PASS: color picker preview/save/restore/cancel, species isolation and contact drag sorting.");
            // The standard QA window is intentionally compact. Expand only
            // this isolated fixture to exercise true free XY rendering, not
            // just its narrow-window safety fallback.
            var compactQaBounds = Bounds;
            Size = new Size(Math.Max(Width,1770),Math.Max(Height,1050));
            await Task.Delay(250);
            // The formerly grid-only editor must now support independent XY
            // geometry, safe clipping and a persisted per-theme free layout.
            // All edits below run in the isolated QA profile, never the user's.
            string freeContract = @"(function(){
                var ui=window.UiLab,f=window.HangarFeatures;
                ui.setView('all');ui.setTheme('terran',false,true);
                document.getElementById('layoutSettings').querySelector('[data-preset=base]').click();
                var protoss=JSON.stringify(f.getLayout('protoss'));
                ui.setView('custom');
                if(!f.isEditing())return 'custom-entry';
                f.editorCommand({action:'close'});
                if(f.isEditing()||!document.getElementById('layoutEditor').hidden)
                  return 'unchanged-custom-should-close-directly';
                f.beginEdit(null);
                var toggle=document.querySelector('#layoutEditor [data-edit-action=free]');
                if(!toggle)return 'no-free-action';
                var sampled=['.workspace-header','#hero','#launchButton','.catalog-toolbar',
                  '.campaign-list-region','#detailPanel'],startRects={};
                sampled.forEach(function(selector){
                  var node=document.querySelector(selector),r=node.getBoundingClientRect();
                  if(r.width>15&&r.height>15)startRects[selector]=[r.x,r.y];
                });
                toggle.click();
                var jumps=[];
                sampled.forEach(function(selector){
                  if(!startRects[selector])return;
                  var r=document.querySelector(selector).getBoundingClientRect();
                  var dx=Math.abs(r.x-startRects[selector][0]),dy=Math.abs(r.y-startRects[selector][1]);
                  if(dx>70||dy>70)jumps.push(selector+':'+Math.round(dx)+'/'+Math.round(dy));
                });
                window.__freeEntryDiag=jumps.join(',');
                if(jumps.length)return 'free-entry-geometry-jumped:'+window.__freeEntryDiag;
                var viewport=document.getElementById('workspace');
                if(viewport.clientWidth<760 || viewport.clientHeight<560)return 'expanded-viewport-not-large-enough';
                if(document.body.dataset.layoutFree!=='true')return 'missing-absolute-layout';
                if(!document.querySelector('[data-layout-color]')||
                   !document.querySelector('[data-layout-field=menu]')||
                   !document.querySelector('[data-edit-action=undo]')||
                   !document.querySelector('[data-layout-text=catalogEyebrow]')||
                   !document.querySelector('[data-free-action=launchButton] .layout-free-move'))
                   return 'free-full-editor-with-button-handles';
                var nonCampaign=document.querySelector('.archive-nav [data-view=archive]');
                if(nonCampaign.querySelector('.nav-visibility-toggle')||
                   document.querySelector('.settings-nav [data-view=settings] .nav-visibility-toggle'))
                   return 'protected-nav-cannot-be-closed';
                var hide=document.querySelector('.side-nav [data-view=wol] .nav-visibility-toggle');
                if(!hide)return 'campaign-visibility-control-missing';
                hide.click();
                if(f.getEditingLayout().hiddenCampaigns.indexOf('wol')<0)return 'campaign-hide-edit';
                hide.click();
                if(f.getEditingLayout().hiddenCampaigns.indexOf('wol')>=0)return 'campaign-restore-edit';
                var grip=document.querySelector('.layout-menu-resize');
                if(!grip)return 'menu-border-resize-missing';
                var menuBefore=f.getEditingLayout().menuSize;
                grip.dispatchEvent(new PointerEvent('pointerdown',
                  {bubbles:true,button:0,pointerId:19,clientX:400,clientY:400}));
                grip.dispatchEvent(new PointerEvent('pointermove',
                  {bubbles:true,button:0,pointerId:19,clientX:418,clientY:400}));
                grip.dispatchEvent(new PointerEvent('pointerup',
                  {bubbles:true,button:0,pointerId:19,clientX:418,clientY:400}));
                if(f.getEditingLayout().menuSize<=menuBefore)return 'menu-border-drag-not-resized';
                var hero=document.getElementById('hero'),start=f.getEditingLayout().panels.hero.x;
                hero.dispatchEvent(new PointerEvent('pointerdown',
                  {bubbles:true,button:0,pointerId:21,clientX:500,clientY:310}));
                hero.dispatchEvent(new PointerEvent('pointermove',
                  {bubbles:true,button:0,pointerId:21,clientX:500+viewport.clientWidth*.01,clientY:310}));
                hero.dispatchEvent(new PointerEvent('pointerup',
                  {bubbles:true,button:0,pointerId:21,clientX:500+viewport.clientWidth*.01,clientY:310}));
                if(f.getEditingLayout().panels.hero.x<=start)return 'panel-surface-move-not-updated';
                var width=f.getEditingLayout().panels.hero.w;
                var edge=hero.querySelector('.layout-free-edge-se');
                if(!edge)return 'panel-edge-resize-missing';
                edge.dispatchEvent(new PointerEvent('pointerdown',
                  {bubbles:true,button:0,pointerId:22,clientX:700,clientY:420}));
                edge.dispatchEvent(new PointerEvent('pointermove',
                  {bubbles:true,button:0,pointerId:22,clientX:700-viewport.clientWidth*.02,clientY:410}));
                edge.dispatchEvent(new PointerEvent('pointerup',
                  {bubbles:true,button:0,pointerId:22,clientX:700-viewport.clientWidth*.02,clientY:410}));
                if(f.getEditingLayout().panels.hero.w>=width)return 'panel-edge-not-resized';
                document.dispatchEvent(new KeyboardEvent('keydown',{key:'z',ctrlKey:true,bubbles:true}));
                if(f.getEditingLayout().panels.hero.w!==width)return 'panel-edge-undo';
                document.dispatchEvent(new KeyboardEvent('keydown',{key:'y',ctrlKey:true,bubbles:true}));
                if(f.getEditingLayout().panels.hero.w>=width)return 'panel-edge-redo';
                var rect=document.getElementById('hero').getBoundingClientRect(),area=viewport.getBoundingClientRect();
                if(rect.left < area.left-2 || rect.right > area.right+2 ||
                   rect.top < area.top-2 || rect.bottom > area.bottom+2)return 'free-panel-outside-workspace';
                window.__freeSavedX=f.getEditingLayout().panels.hero.x;
                document.querySelector('#layoutEditor [data-edit-action=apply]').click();
                if(f.isEditing() || !f.getLayout('terran').free ||
                   f.getLayout('terran').panels.hero.x!==window.__freeSavedX)return 'free-commit';
                ui.setTheme('protoss',false,true);
                if(JSON.stringify(f.getLayout('protoss'))!==protoss ||
                   f.getLayout('protoss').free)return 'free-cross-theme-leak';
                ui.setTheme('terran',false,true);
                f.beginEdit(null);
                if(!document.querySelector('[data-layout-field=menu]'))return 'free-editor-settings';
                f.editorCommand({action:'close'});
                if(f.isEditing()||!document.getElementById('layoutEditor').hidden)
                  return 'free-cancel-should-be-direct';
                if(!f.getLayout('terran').free)return 'free-cancel';
                f.beginEdit(null);
                var lockButton=document.querySelector('#hero .layout-free-lock');
                if(!lockButton)return 'free-lock-missing';
                lockButton.click();
                if(!f.getEditingLayout().panels.hero.locked ||
                   document.querySelector('#hero .layout-free-edge'))return 'free-lock-enforcement';
                document.querySelector('#layoutEditor [data-edit-action=apply]').click();
                if(!f.getLayout('terran').panels.hero.locked)return 'free-lock-save';
                f.beginEdit(null);
                document.querySelector('#hero .layout-free-lock').click();
                if(f.getEditingLayout().panels.hero.locked)return 'free-lock-unlock';
                f.editorCommand({action:'button',value:'cancel'});
                document.querySelector('[data-close-choice=no]').click();
                if(!f.getLayout('terran').panels.hero.locked)return 'free-collision-cancel';
                ui.setView('archive');return 'ok';
            })()";
            var freeResult = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(freeContract));
            if (!String.Equals(freeResult as string,"ok",StringComparison.Ordinal))
                throw new InvalidOperationException("Free XY layout contract failed: " + freeResult);
            await Task.Delay(250);
            var freeStored = CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8));
            if (!freeStored["terran"].layout.free || freeStored["terran"].layout.panels == null ||
                !freeStored["terran"].layout.panels.ContainsKey("hero") ||
                freeStored["terran"].layout.panels["hero"].x < 0 ||
                freeStored["terran"].layout.panels["hero"].x > 1000)
                throw new InvalidOperationException("Free panel coordinates did not persist in the native host.");
            await PushHubLayouts();
            var freeRestored = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){var f=window.HangarFeatures,p=f.getLayout('terran');return p.free&&"+
                "p.panels.hero.x===window.__freeSavedX&&!f.getLayout('protoss').free;})()"));
            if (!(freeRestored is bool) || !(bool)freeRestored)
                throw new InvalidOperationException("Native XY panel profile did not restore per theme.");
            await hubWeb.ExecuteScriptAsync("(function(){window.UiLab.setView('all');"+
                "window.UiLab.setTheme('terran',false,true);window.HangarFeatures.beginEdit(null);})()");
            await Task.Delay(130);
            using(var image=File.Create(Path.Combine(output,"free-direct-edit.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("(function(){HangarFeatures.editorCommand({action:'close'});"+
                "window.UiLab.setView('archive');})()");
            Console.WriteLine("WEBVIEW FREE EDITOR PASS: drag-ready panels/buttons, edge/menu resize and full editing controls.");
            string advancedLayoutContract = @"(function(){
                var ui=window.UiLab,f=window.HangarFeatures;
                ui.setView('all');ui.setTheme('terran',false,true);
                document.getElementById('layoutSettings').querySelector('[data-preset=base]').click();
                ui.setView('custom');
                if(!f.isEditing()||!document.getElementById('layoutEditor').hidden)return 'custom-menu';
                var palette=document.querySelector('[data-layout-color=accent]');
                if(!palette)return 'color-input';
                palette.value='#a638d2';
                palette.dispatchEvent(new Event('input',{bubbles:true}));
                palette.dispatchEvent(new Event('change',{bubbles:true}));
                if(f.getEditingLayout().colors.accent!=='#a638d2'||
                   getComputedStyle(document.body).getPropertyValue('--accent').trim()!=='#a638d2')
                   return 'color-preview';
                var kind=document.querySelector('[data-layout-field=presentation]');
                kind.value='contact';kind.dispatchEvent(new Event('change',{bubbles:true}));
                var a=document.querySelector('[data-contact-cell=wol]');
                var b=document.querySelector('[data-contact-cell=hots]');
                if(document.getElementById('contactGrid').hidden||!a.draggable)return 'contact-edit';
                var transfer=new DataTransfer();
                a.dispatchEvent(new DragEvent('dragstart',{bubbles:true,dataTransfer:transfer}));
                b.dispatchEvent(new DragEvent('dragover',{bubbles:true,dataTransfer:transfer,cancelable:true}));
                b.dispatchEvent(new DragEvent('drop',{bubbles:true,dataTransfer:transfer,cancelable:true}));
                var order=f.getEditingLayout().contactOrder;
                if(order.indexOf('wol')!==2||order.indexOf('hots')!==1)return 'contact-order';
                document.querySelector('[data-edit-action=apply]').click();
                if(f.isEditing()||f.getLayout('terran').colors.accent!=='#a638d2'||
                   f.getLayout('terran').contactOrder[1]!=='hots')return 'custom-save';
                ui.setTheme('protoss',false,true);
                if(f.getLayout('protoss').colors.accent)return 'custom-cross-theme';
                ui.setTheme('terran',false,true);
                ui.setView('settings');ui.navigateBack();
                if(ui.getState().view!=='all')return 'back-navigation';
                return 'ok';
            })()";
            var advancedLayout = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(advancedLayoutContract));
            if (!String.Equals(advancedLayout as string,"ok",StringComparison.Ordinal))
                throw new InvalidOperationException("Custom palette/contact drag and in-app back contract failed: " + advancedLayout);
            await Task.Delay(270);
            var customStored = CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8));
            string customAccent;
            if (!customStored["terran"].layout.colors.TryGetValue("accent",out customAccent) ||
                customAccent != "#a638d2" || customStored["terran"].layout.contactOrder[1] != "hots")
                throw new InvalidOperationException("Custom theme colors or contact order did not persist to the isolated host profile.");
            await PushHubLayouts();
            await hubWeb.ExecuteScriptAsync("window.UiLab.setView('settings')");
            GoBack();
            await Task.Delay(270);
            var nativeBack = Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(
                "window.UiLab.getState().view"));
            if (nativeBack != "all")
                throw new InvalidOperationException("Native mouse back routing did not return the themed page.");
            Console.WriteLine("WEBVIEW CUSTOM PASS: color preview/save/readback, contact panel drag order, theme isolation and native back routing.");
            var immediateBoot = Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var u=window.UiLab;
                u.setThemeFromHost('terran');
                u.applyLivePreferences({browseWhileWorking:true,reducedMotion:false});
                document.getElementById('boot').classList.add('is-hidden');
                document.querySelector('[data-set-theme=protoss]').click();
                if(window.matchMedia('(prefers-reduced-motion: reduce)').matches)return 'reduced';
                return document.body.dataset.theme==='terran' &&
                    !document.getElementById('boot').classList.contains('is-hidden') ?
                    'ok':'changed-before-loading';
            })()"));
            if (immediateBoot != "ok" && immediateBoot != "reduced")
                throw new InvalidOperationException("Switching theme showed the underlying new screen before the boot overlay: " + immediateBoot);
            await Task.Delay(280);
            if (immediateBoot == "ok")
            {
                var nextTheme = Data.Json.Deserialize<string>(
                    await hubWeb.ExecuteScriptAsync("document.body.dataset.theme"));
                if (nextTheme != "protoss")
                    throw new InvalidOperationException("Theme did not switch after the loading overlay appeared.");
            }
            await hubWeb.ExecuteScriptAsync("window.UiLab.finishBoot();window.UiLab.setThemeFromHost('terran')");
            Console.WriteLine("WEBVIEW THEME LOADING PASS: legacy work-box preference cannot hide the loading frame before a theme change.");
            var bootSequence=30000;
            foreach(var palette in LiveTheme.Choices)
            {
                int seq=++bootSequence;
                await hubWeb.ExecuteScriptAsync("window.UiLab.setThemeFromHost("+
                    Data.Json.Serialize(palette.Id)+");"+
                    "window.UiLab.setWorkProgress("+Data.Json.Serialize(new {
                        sequence=seq,active=true,fullscreen=true,stage="진행 측정 QA",
                        completed=3,total=12,bytes=false,title="ZIP 단계 진행"})+")");
                await Task.Delay(160);
                var artwork = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                    "(function(){return getComputedStyle(document.body).getPropertyValue('--boot-image')"+
                    ".indexOf('loading-"+palette.Id+".png')>=0&&"+
                    "document.getElementById('bootPercent').textContent==='25%';})()"));
                if (!(artwork is bool) || !(bool)artwork)
                    throw new InvalidOperationException("Missing dynamic progress or species loading PNG: "+palette.Id);
                using(var png=File.Create(Path.Combine(output,"loading-"+palette.Id+"-progress.png")))
                    await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            }
            await hubWeb.ExecuteScriptAsync("window.UiLab.setWorkProgress({sequence:30005,active:false})");
            hubProgressSequence=30005;
            Console.WriteLine("WEBVIEW FOUR RACE LOADING PASS: four backgrounds render with runtime 25% data and no baked percentage.");
            await hubWeb.ExecuteScriptAsync("window.UiLab.setView('all');"+
                "window.UiLab.setThemeFromHost('terran')");
            await Task.Delay(150);
            using (var image = File.Create(Path.Combine(output,"free-terran-clean.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("window.HangarFeatures.beginEdit(null)");
            await Task.Delay(100);
            using (var image = File.Create(Path.Combine(output,"free-terran.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("HangarFeatures.editorCommand({action:'close'});"+
                "window.UiLab.setView('archive')");
            Bounds = compactQaBounds;
            Console.WriteLine("WEBVIEW LAYOUT PASS: four real presets, editing history, cancel/save, host-profile readback and theme isolation.");
            Console.WriteLine("WEBVIEW FREE XY PASS: direct panel drag, eight edges, menu border resize, no-change close, undo/redo, scroll canvas, cancel and native per-theme persistence.");
            Console.WriteLine("WEBVIEW ARCHIVE UI PASS: themed startup and live archive mount remain in one app surface.");
            await hubWeb.ExecuteScriptAsync("window.UiLab.setThemeFromHost('nova');"+
                "window.UiLab.setView('settings');");
            await Task.Delay(170);
            using (var capture = File.Create(Path.Combine(output,"settings-nova.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,capture);
            await hubWeb.ExecuteScriptAsync("document.getElementById('layoutSettings').scrollIntoView({block:'center'})");
            await Task.Delay(140);
            using (var capture = File.Create(Path.Combine(output,"settings-layout-nova.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,capture);
            await hubWeb.ExecuteScriptAsync("document.getElementById('hostSettings').scrollIntoView({block:'center'})");
            await Task.Delay(140);
            using (var capture = File.Create(Path.Combine(output,"settings-host-nova.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,capture);
            await hubWeb.ExecuteScriptAsync("document.getElementById('workspace').scrollTop=0");
            var hostSettingsVisible = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){return !document.getElementById('hostSettings').hidden&&"+
                "!!document.getElementById('layoutSettings').querySelector('.preset-map')&&"+
                "document.querySelector('.setting-grid>.setting-tile:last-child').id==='layoutSettings'&&"+
                "!document.getElementById('replayBoot').hidden===false;})()"));
            // The header replay button is deliberately hidden in production,
            // but this fixture tests the always-visible host settings independently.
            if (!(hostSettingsVisible is bool) || !(bool)hostSettingsVisible)
                throw new InvalidOperationException("Always-open host settings or visual layout preset missing.");
            await hubWeb.ExecuteScriptAsync("window.UiLab.setView('wol');"+
                "(function(){var c=window.UiLab.getSelected();"+
                "if(c){window.HangarFeatures.openManage(c);"+
                "document.querySelector('[data-manage-action=versions]').click();}})()");
            await Task.Delay(120);
            var themedManage = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                "(function(){return !document.getElementById('manageOverlay').hidden&&"+
                "!document.getElementById('manageActionPanel').hidden;})()"));
            if (!(themedManage is bool) || !(bool)themedManage)
                throw new InvalidOperationException("Manage actions must remain themed before native file dialogs.");
            using (var capture = File.Create(Path.Combine(output,"manage-nova.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,capture);
            var themedActionsResult=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(@"(function(){
                var f=window.HangarFeatures,c=window.UiLab.getSelected();
                f.setManageChoices('versions',{kind:'ccm',versions:[
                  {versionKey:'qa-version-a',label:'테스트 버전 A'},
                  {versionKey:'qa-version-b',label:'테스트 버전 B'}]});
                if(!document.getElementById('manageVersionChoice') ||
                   document.getElementById('manageVersionChoice').options.length!==2 ||
                   !document.getElementById('manageActionBack') ||
                   !document.getElementById('manageActionConfirm').hidden)return 'version-picker';
                f.closeManage();f.openManage(c);
                document.querySelector('[data-manage-action=remove]').click();
                f.setManageChoices('remove',{kind:'ccm',versions:[]});
                var input=document.querySelector('[name=manage-removal][value=CampaignOnly]');
                if(!input)return 'removal-options';
                input.checked=true;input.dispatchEvent(new Event('change',{bubbles:true}));
                if(document.getElementById('manageActionConfirm').hidden)return 'removal-not-selectable';
                f.closeManage();f.openManage(c);
                document.querySelector('[data-manage-action=link]').click();
                f.setManageChoices('link',{kind:'ccm',selectedId:'qa-site',sites:[
                  {id:'qa-site',name:'설치 대상 동일',matchSlot:true,recommended:true},
                  {id:'qa-other-site',name:'설치 대상 다름',matchSlot:false,recommended:false}]});
                if(!document.getElementById('manageSiteChoice') ||
                   document.getElementById('manageActionConfirm').disabled)return 'site-picker';
                if(!document.querySelector('#manageSiteRecommendations [data-site-id=qa-site]') ||
                   !document.getElementById('manageSiteSearch'))return 'site-suggestion-cards';
                var search=document.getElementById('manageSiteSearch');
                search.value='대상 다름';search.dispatchEvent(new Event('input',{bubbles:true}));
                var result=document.querySelector('#manageSiteResults [data-site-id=qa-other-site]');
                if(!result)return 'site-result-card';
                result.click();
                if(!document.getElementById('manageActionConfirm').disabled)return 'slot-warning-not-required';
                document.getElementById('manageSiteMismatch').click();
                if(document.getElementById('manageActionConfirm').disabled)return 'slot-confirmation-ignored';
                var installAction=document.querySelector('[data-manage-action=install]');
                if(installAction&&!installAction.hidden)return 'duplicate-install-tab';
                f.closeManage();return 'ok';
            })()"));
            if(!String.Equals(themedActionsResult as string,"ok",StringComparison.Ordinal))
                throw new InvalidOperationException("Themed versions/removal/site link choice UI failed: "+themedActionsResult);
            await hubWeb.ExecuteScriptAsync("window.HangarFeatures.closeManage();"+
                "window.HangarFeatures.openRecord(window.UiLab.getSelected());");
            await Task.Delay(100);
            using (var capture = File.Create(Path.Combine(output,"record-nova.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png,capture);
            await hubWeb.ExecuteScriptAsync("window.HangarFeatures.closeRecord();"+
                "window.UiLab.setView('archive');");
            Console.WriteLine("WEBVIEW MANAGEMENT UX PASS: native settings open, visual presets, themed management and play-record panels.");
            Console.WriteLine("WEBVIEW MANAGE CHOICES PASS: themed version picker, guarded removal scopes and archive site linking selection.");
            await VerifyRefinedEditorAsync(output);
            await VerifyExpandedFreeEditorAsync(output);
            await VerifyQuickResizeAsync(output);
            await VerifyUtilityDialogSkinsAsync(output);
            await VerifyThemeArtworkAsync(output);
            await VerifyDetachedWorkWindowAsync(output);
        }
        async Task VerifyQuickResizeAsync(string output)
        {
            string original=await hubWeb.ExecuteScriptAsync("HangarFeatures.getProfiles()");
            string result=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var u=UiLab,f=HangarFeatures;u.setTheme('terran',false,true);u.setView('all');
                document.querySelector('[data-preset=base]').click();
                if(getComputedStyle(document.getElementById('detailArt')).backgroundSize!=='contain')return 'base-art-cropped';
                function artGrows(){
                    var panel=document.getElementById('detailPanel'),art=document.getElementById('detailArt');
                    var saved=panel.style.cssText;
                    panel.style.setProperty('height','900px','important');
                    panel.style.setProperty('min-height','900px','important');
                    panel.style.setProperty('max-height','none','important');
                    var small=art.getBoundingClientRect().height;
                    panel.style.setProperty('height','1100px','important');
                    panel.style.setProperty('min-height','1100px','important');
                    var large=art.getBoundingClientRect().height;
                    panel.style.cssText=saved;
                    return large>=small+180;
                }
                if(!artGrows())return 'base-art-does-not-grow';
                function drag(selector,dx,dy){
                    var handle=document.querySelector(selector),r=handle.getBoundingClientRect();
                    handle.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:95,clientX:r.x,clientY:r.y}));
                    handle.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,buttons:1,pointerId:95,clientX:r.x+dx,clientY:r.y+dy}));
                    handle.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,button:0,pointerId:95,clientX:r.x+dx,clientY:r.y+dy}));
                }
                var width=f.getLayout('terran').menuSize;
                drag('.sidebar>.quick-resize-e',25,0);
                if(f.isEditing()||f.getLayout('terran').menuSize<=width)return 'ordinary-menu-resize';
                var height=document.getElementById('hero').clientHeight;
                drag('#hero>.quick-resize-s',0,30);
                if(document.getElementById('hero').clientHeight<height+20)return 'ordinary-hero-resize';
                drag('#detailArt>.quick-resize-s',0,40);
                if(!f.getLayout('terran').quickSizes.art)return 'ordinary-art-resize';
                if(!artGrows())return 'saved-art-size-blocks-growth';
                document.querySelector('[data-preset=list]').click();
                drag('#detailPanel>.quick-resize-s',0,30);
                if(!f.getLayout('terran').quickSizes.detail)return 'list-detail-resize';
                document.querySelector('[data-preset=contact]').click();
                var panel=document.querySelector('[data-contact-cell=wol]'),originalHeight=panel.getBoundingClientRect().height;
                drag('[data-contact-cell=wol]>.quick-resize-se',20,50);
                if(!f.getLayout('terran').quickSizes['contact-wol'])return 'contact-list-resize';
                if(panel.getBoundingClientRect().height<originalHeight+35)return 'contact-panel-height-not-growing';
                var previousHeight=panel.getBoundingClientRect().height;
                drag('[data-contact-cell=wol]>.quick-resize-s',0,-90);
                if(panel.getBoundingClientRect().height>previousHeight-70)return 'contact-panel-height-not-shrinking';
                if(!document.querySelector('[data-contact-cell=detail]>.quick-resize-s'))return 'contact-detail-edge-missing';
                drag('#contactArt>.quick-resize-s',0,30);
                if(!f.getLayout('terran').quickSizes.contactArt)return 'contact-image-resize';
                if(getComputedStyle(document.querySelector('.sidebar')).scrollbarWidth!=='none')return 'menu-scrollbar-visible';
                return 'ok';
            })()"));
            if(result!="ok")throw new InvalidOperationException("Quick resize contract: "+result);
            await Task.Delay(250);
            var stored=CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8))["terran"].layout;
            if(stored.quickSizes==null||!stored.quickSizes.ContainsKey("contactArt"))
                throw new InvalidOperationException("Ordinary edge dimensions did not persist.");
            await PushHubLayouts();
            string menu=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                UiLab.setTheme('protoss',false,true);document.querySelector('[data-preset=contact]').click();
                HangarFeatures.beginEdit(null);HangarFeatures.editorCommand({action:'field',key:'menu',value:'top'});
                HangarFeatures.editorCommand({action:'button',value:'apply'});
                var root=document.querySelector('.sidebar'),active=root.querySelector('.nav-link.is-active');
                var nav=root.querySelector('.side-nav').getBoundingClientRect();
                var archive=root.querySelector('.archive-nav').getBoundingClientRect();
                if(nav.width<76||Math.abs(nav.width-archive.width)>15)return 'contact-menu-space:'+nav.width+'/'+archive.width;
                var archiveButton=root.querySelector('.archive-nav>.nav-link').getBoundingClientRect();
                if(Math.abs(archiveButton.right-archive.right)>2)return 'archive-button-not-filling-cell';
                if(getComputedStyle(active,'::before').clipPath==='none'||
                    getComputedStyle(active,'::after').clipPath==='none')return 'protoss-silhouette-outline';
                UiLab.finishBoot();return 'ok';
            })()"));
            if(menu!="ok")throw new InvalidOperationException("Expanded contact menu: "+menu);
            await Task.Delay(200);
            string outline=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var style=getComputedStyle(document.querySelector('.sidebar .nav-link.is-active'));
                return style.boxShadow==='none'&&style.borderLeftColor==='rgba(0, 0, 0, 0)'?'ok':'protoss-extra-rectangle-or-stripe';
            })()"));
            if(outline!="ok")throw new InvalidOperationException("Expanded contact menu: "+outline);
            using(var image=File.Create(Path.Combine(output,"contact-protoss-top-menu.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubWeb.ExecuteScriptAsync("HangarFeatures.applyHostProfiles("+original+");");
            Console.WriteLine("QUICK RESIZE PASS: menu, hero, detail, detail artwork and contact panels resize without opening editor; host save/readback, hidden scrollbar.");
        }

        async Task VerifyUtilityDialogSkinsAsync(string output)
        {
            var previous=LiveTheme.Current;
            foreach(var palette in LiveTheme.Choices)
            {
                ChangeLiveTheme(palette,false);
                using(var dialog=new AppDialog("보관소 변경 주의",Storage.RelocationWarning+
                    "\n\n기존 보관소는 연결만 합니다. 빈 폴더를 선택하면 현재 보관소를 이동합니다.",
                    "계속 · 폴더 선택"))
                {
                    dialog.Show(this);dialog.PerformLayout();TextLayout.VerifyLabels(dialog);
                    if(!dialog.HasFactionBackdrop||dialog.BackColor!=palette.Background)
                        throw new InvalidOperationException("Missing faction skin: "+palette.Id);
                    dialog.CapturePreview(Path.Combine(output,"utility-"+palette.Id+"-storage.png"));dialog.Close();
                }
                using(var dialog=new HangarFolderDialog("ZIP 다운로드 경로",
                    "새 ZIP을 저장할 폴더를 선택하세요. 기존 ZIP과 백업은 이동하지 않습니다.",root))
                {
                    dialog.Show(this);dialog.PerformLayout();TextLayout.VerifyLabels(dialog);
                    if(!dialog.HasFactionBackdrop)throw new InvalidOperationException("Missing folder skin.");
                    dialog.CapturePreview(Path.Combine(output,"utility-"+palette.Id+"-downloads.png"));dialog.Close();
                }
                using(var dialog=new AppDialog("앱 업데이트","현재 최신 앱 버전입니다. v"+AppUpdate.Current))
                {
                    dialog.Show(this);TextLayout.VerifyLabels(dialog);
                    dialog.CapturePreview(Path.Combine(output,"utility-"+palette.Id+"-update.png"));dialog.Close();
                }
                using(var dialog=new AppDialog("설치·다운로드 기록","검사 자료 · 캠페인 ZIP 확인 완료\n검사 자료 · 캠페인 등록 완료"))
                {
                    dialog.Show(this);TextLayout.VerifyLabels(dialog);
                    dialog.CapturePreview(Path.Combine(output,"utility-"+palette.Id+"-history.png"));dialog.Close();
                }
                LiveTheme.ApplyColors(new Dictionary<string,string>{{"bg","#10151a"},{"panel","#20303b"},
                    {"accent","#85cbe2"},{"borderColor","#c4a3fa"},{"buttonText","#f1e8ff"}});
                using(var dialog=new AppDialog("사용자 색상 확인","현재 종족의 사용자 배경·강조·테두리·버튼 색을 사용합니다."))
                {
                    dialog.Show(this);TextLayout.VerifyLabels(dialog);
                    if(dialog.BackColor!=ColorTranslator.FromHtml("#10151a")||
                        GlassTheme.CustomBorder!=ColorTranslator.FromHtml("#c4a3fa"))
                        throw new InvalidOperationException("Custom utility palette was not applied.");
                    dialog.CapturePreview(Path.Combine(output,"utility-"+palette.Id+"-custom.png"));dialog.Close();
                }
            }
            ChangeLiveTheme(previous,false);
            await PushLiveHubPreferences();
            Console.WriteLine("UTILITY SKIN PASS: four races x storage warning/download folder/update/history/custom palette rendered; no folder or game changes.");
        }

        async Task VerifyExpandedFreeEditorAsync(string output)
        {
            string original=await hubWeb.ExecuteScriptAsync("HangarFeatures.getProfiles()");
            string result=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var f=HangarFeatures,u=UiLab;u.setTheme('terran',false,true);u.setView('all');
                document.querySelector('[data-preset=base]').click();f.beginEdit(null);
                f.editorCommand({action:'button',value:'free'});
                if(!f.getEditingLayout().free||!document.querySelector('[data-layout-color]'))return 'full-editor';
                f.editorCommand({action:'text',key:'catalogEyebrow',value:'RAIDERS / MISSION INDEX'});
                if(document.getElementById('catalogEyebrow').textContent!=='RAIDERS / MISSION INDEX')return 'text-preview';
                document.querySelector('[data-free-action=detailButton] .layout-free-close').click();
                if(getComputedStyle(document.querySelector('[data-free-action=detailButton]')).display!=='none'||
                   !document.querySelector('[data-edit-action=restore-detailButton]'))return 'hidden-button-restore-list';
                f.editorCommand({action:'button',value:'undo'});
                if(f.getEditingLayout().hiddenElements.indexOf('detailButton')>=0)return 'hide-undo';
                f.editorCommand({action:'button',value:'redo'});
                if(f.getEditingLayout().hiddenElements.indexOf('detailButton')<0)return 'hide-redo';
                f.editorCommand({action:'button',value:'restore-detailButton'});
                var wrap=document.querySelector('[data-free-action=detailButton]'),handle=wrap.querySelector('.layout-free-move');
                var before=JSON.stringify(f.getEditingLayout().panels.detailButton),r=handle.getBoundingClientRect();
                handle.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:77,clientX:r.x,clientY:r.y}));
                wrap.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,buttons:1,pointerId:77,clientX:r.x+70,clientY:r.y+70}));
                wrap.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,button:0,pointerId:77,clientX:r.x+70,clientY:r.y+70}));
                if(JSON.stringify(f.getEditingLayout().panels.detailButton)===before)return 'button-drag';
                var edge=wrap.querySelector('.layout-free-edge-e'),er=edge.getBoundingClientRect();
                edge.dispatchEvent(new PointerEvent('pointerdown',{bubbles:true,button:0,pointerId:78,clientX:er.x,clientY:er.y}));
                wrap.dispatchEvent(new PointerEvent('pointermove',{bubbles:true,buttons:1,pointerId:78,clientX:er.x+37,clientY:er.y}));
                wrap.dispatchEvent(new PointerEvent('pointerup',{bubbles:true,button:0,pointerId:78,clientX:er.x+37,clientY:er.y}));
                var area=document.getElementById('workspace'),ar=area.getBoundingClientRect(),br=wrap.getBoundingClientRect();
                var mesh=getComputedStyle(area).backgroundSize.split(' ').map(parseFloat);
                function aligned(value,unit){return Number.isFinite(unit)&&unit>0&&Math.abs(value/unit-Math.round(value/unit))<.05;}
                if(!aligned(br.left-ar.left,mesh[0])||!aligned(br.top-ar.top+area.scrollTop,mesh[1])||
                   !aligned(br.width,mesh[0])||!aligned(br.height,mesh[1]))return 'free-move-resize-off-visible-grid';
                document.querySelector('#hero .layout-free-close').click();
                if(!document.querySelector('[data-edit-action=restore-hero]'))return 'hidden-panel-list';
                f.editorCommand({action:'button',value:'restore-hero'});
                f.editorCommand({action:'button',value:'free'});
                if(f.getEditingLayout().free||document.getElementById('detailButton').parentNode!==document.querySelector('.action-strip'))return 'free-disable';
                f.editorCommand({action:'button',value:'free'});
                if(!f.getEditingLayout().free)return 'free-reenable';
                f.editorCommand({action:'button',value:'hide-screenshotButton'});
                f.editorCommand({action:'button',value:'apply'});return 'ok';
            })()"));
            if(result!="ok")throw new InvalidOperationException("Expanded free editor: "+result);
            await Task.Delay(250);
            var saved=CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8))["terran"].layout;
            if(saved.texts["catalogEyebrow"]!="RAIDERS / MISSION INDEX"||
                !saved.hiddenElements.Contains("screenshotButton")||!saved.panels.ContainsKey("detailButton"))
                throw new InvalidOperationException("Free editor text/visibility/button geometry did not persist.");
            await PushHubLayouts();
            var readback=Data.Json.Deserialize<bool>(await hubWeb.ExecuteScriptAsync(
                "HangarFeatures.getLayout('terran').hiddenElements.includes('screenshotButton')&&document.getElementById('catalogEyebrow').textContent==='RAIDERS / MISSION INDEX'"));
            if(!readback)throw new InvalidOperationException("Free editor readback failed.");
            using(var png=File.Create(Path.Combine(output,"expanded-free-layout.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            string contact=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                var f=HangarFeatures;document.querySelector('[data-preset=contact]').click();f.beginEdit(null);
                f.editorCommand({action:'button',value:'free'});
                var node=document.querySelector('[data-free-action=installButton]');
                if(!node||getComputedStyle(node).display==='none'||!node.querySelector('.layout-free-move'))return 'contact-action-handles';
                node.querySelector('.layout-free-close').click();
                if(!document.querySelector('[data-edit-action=restore-installButton]'))return 'contact-action-restore';
                f.editorCommand({action:'button',value:'restore-installButton'});
                f.editorCommand({action:'button',value:'cancel'});document.querySelector('[data-close-choice=no]').click();
                return 'ok';
            })()"));
            if(contact!="ok")throw new InvalidOperationException("Contact free actions: "+contact);
            await hubWeb.ExecuteScriptAsync("HangarFeatures.applyHostProfiles("+original+");");
            await hubWeb.ExecuteScriptAsync("UiLab.applyLivePreferences({browseWhileWorking:false,reducedMotion:false});UiLab.replayBoot(1000);");
            await Task.Delay(100);
            using(var png=File.Create(Path.Combine(output,"enhanced-loading.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,png);
            await hubWeb.ExecuteScriptAsync("UiLab.finishBoot();");
            Console.WriteLine("WEBVIEW EXPANDED FREE PASS: full editor, text preview/save/readback, button drag, panel/button hide, restore, undo/redo and free toggle.");
        }

        async Task VerifyRefinedEditorAsync(string output)
        {
            if(!isolated||online)throw new InvalidOperationException("Editor QA must be isolated and offline.");
            var visible=LiveEntries();
            if(catalogue.Where(c=>c.kind=="dedicated").Any(c=>!visible.Any(e=>e.id==c.id&&e.dedicated)))
                throw new InvalidOperationException("A site dedicated campaign was omitted from the live catalogue.");
            string original=await hubWeb.ExecuteScriptAsync("window.HangarFeatures.getProfiles()");
            string script=@"(function(){
                var u=UiLab,f=HangarFeatures;
                u.setTheme('terran',false,true);u.setView('all');
                var fixtures=[
                  {id:'refine-a',versionKey:'v1',name:'격리 캠페인',category:'자날',kind:'한글 번역',author:'검사 제작자',installed:true,playSeconds:7200},
                  {id:'refine-d',name:'미설치 전용',category:'전용',kind:'전용 캠페인',dedicated:true,installed:false}];
                u.setRealCampaigns(fixtures,2);u.setView('dedicated');
                if(u.filtered().length!==1)return 'missing-uninstalled-dedicated';
                f.openManage(u.getSelected());
                var install=document.querySelector('[data-manage-action=install]');
                if(!install||install.hidden||install.disabled)return 'dedicated-install-action';
                f.closeManage();
                u.setView('all');u.selectCampaign('refine-a');
                u.setActivationState({activeId:'refine-a',versionKey:'v1'});
                if(!document.getElementById('launchButton').disabled||document.getElementById('primaryActionText').textContent!=='적용됨')return 'applied-version';
                u.setActivationState({activeId:'refine-a',versionKey:'v0'});
                if(document.getElementById('launchButton').disabled)return 'new-version-blocked';
                if(!document.querySelector('.toolbar-inputs #favoriteButton')||
                   !document.querySelector('.action-strip #screenshotButton'))return 'action-relocation';
                if(!Array.from(document.querySelectorAll('[data-archive-action]')).filter(function(e){return e.dataset.archiveAction!=='installed';}).every(function(e){return !!e.querySelector('svg');}))return 'archive-icons';
                var custom=document.querySelector('.sidebar>[data-view=custom]');
                if(!custom||!document.querySelector('.sidebar>.theme-switcher'))return 'editor-section';
                document.querySelector('[data-preset=contact]').click();u.setView('all');
                var actions=document.querySelector('.action-strip'),firstCell=document.querySelector('.contact-cell');
                if(getComputedStyle(actions).display==='none'||actions.getBoundingClientRect().bottom>firstCell.getBoundingClientRect().top)
                    return 'contact-common-actions-not-above';
                if(['launchButton','installButton','detailButton','screenshotButton','importZipButton'].some(function(id){
                    var button=document.getElementById(id);return button.parentNode!==actions||button.getBoundingClientRect().width<1;
                }))return 'contact-common-actions-missing';
                if(Array.from(document.querySelectorAll('.side-nav [data-view]')).filter(function(e){return !e.hidden;}).length!==1)return 'contact-menu';
                if(!document.querySelector('[data-contact-id=refine-a]').textContent.includes('2.0시간')||
                   !document.querySelector('[data-contact-id=refine-a]').textContent.includes('한글 번역'))return 'contact-records';
                var contactCard=document.querySelector('[data-contact-id=refine-a]');
                if(contactCard.getBoundingClientRect().height>100)return 'contact-card-size';
                f.beginEdit(null);
                if(document.querySelector('[data-layout-field=detail]')||
                   document.querySelector('[data-layout-range=heroHeight]')||
                   document.querySelector('[data-layout-range=listHeight]'))return 'unsupported-contact-controls';
                if(document.querySelector('[data-layout-range=cols]').max!=='3')return 'contact-columns-limit';
                for(var columns=1;columns<=3;columns++){
                    f.editorCommand({action:'range',key:'cols',value:String(columns)});
                    var slots=Array.from(document.querySelectorAll('.contact-items'));
                    if(slots.some(function(slot){return getComputedStyle(slot).gridTemplateColumns.split(' ').length!==columns;}))
                        return 'contact-columns-not-applied:'+columns;
                }
                if(document.getElementById('layoutFreeToggle')||!document.querySelector('[data-edit-action=free]'))return 'free-button';
                f.editorCommand({action:'button',value:'cancel'});
                document.querySelector('[data-preset=base]').click();
                f.beginEdit(null);
                f.editorCommand({action:'field',key:'menuShape',value:'zerg'});
                if(document.body.dataset.menuShape!=='zerg')return 'menu-shape';
                f.editorCommand({action:'color',key:'buttonText',value:'#bada55'});
                if(getComputedStyle(document.getElementById('launchButton')).color!=='rgb(186, 218, 85)')return 'button-text-color';
                f.editorCommand({action:'color',key:'iconColor',value:'#44aaff'});
                if(getComputedStyle(document.querySelector('#launchButton .action-icon')).color!=='rgb(68, 170, 255)')return 'button-icon-color';
                f.editorCommand({action:'button',value:'color-reset-buttonText'});
                if(f.getEditingLayout().colors.buttonText||f.getEditingLayout().colors.iconColor!=='#44aaff')return 'individual-color-reset';
                f.editorCommand({action:'button',value:'palettes'});
                if(document.querySelectorAll('[data-edit-action^=palette-]').length!==12)return 'recommended-palettes';
                for(var pi=0;pi<12;pi++){
                  f.editorCommand({action:'button',value:'palette-'+pi});
                  if(!f.getEditingLayout().colors.bg||!f.getEditingLayout().colors.buttonText)return 'palette-apply';
                }
                f.editorCommand({action:'range',key:'cols',value:'4'});
                if(f.getEditingLayout().cols!==4)return 'four-columns-control';
                f.editorCommand({action:'color',key:'menu',value:'#123456'});
                f.editorCommand({action:'color',key:'menuEnd',value:'#654321'});
                var bg=getComputedStyle(document.querySelector('.sidebar')).backgroundImage;
                if(!bg.includes('18, 52, 86')||!bg.includes('101, 67, 33'))return 'menu-gradient';
                f.editorCommand({action:'button',value:'apply'});
                return 'ok';
            })()";
            var result=Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(script));
            if(result!="ok")throw new InvalidOperationException("Refined editor contract: "+result);
            var correctedCompletion=new TaskCompletionSource<string>();
            EventHandler<CoreWebView2WebMessageReceivedEventArgs> correctedHandler=(sender,e)=>{
                if(e.Source!="https://hangar-live.test/index.html"&&
                   !e.Source.StartsWith("https://hangar-live.test/index.html?",StringComparison.Ordinal))return;
                try {
                    string message=e.TryGetWebMessageAsString();
                    if(message.StartsWith("qa-contact-boundaries:",StringComparison.Ordinal))
                        correctedCompletion.TrySetResult(message.Substring("qa-contact-boundaries:".Length));
                }catch(ArgumentException){}
            };
            hubWeb.CoreWebView2.WebMessageReceived+=correctedHandler;
            string corrected=null;
            try {
            await hubWeb.ExecuteScriptAsync(@"(async function(){
                var u=UiLab,f=HangarFeatures,grid=document.getElementById('contactGrid');
                u.setView('all');document.querySelector('[data-preset=contact]').click();
                var fixtures=Array.from({length:90},function(_,i){return {id:'wheel-'+i,name:'휠 검사 '+i,
                    category:['자날','군심','노바'][i%3],kind:'캠페인',author:'검사',installed:true};});
                u.setRealCampaigns(fixtures,90);
                await new Promise(function(r){requestAnimationFrame(function(){requestAnimationFrame(r);});});
                var list=grid.querySelector('[data-contact-items=wol]');
                if(list.scrollHeight<=list.clientHeight)return 'contact-scroll-range';
                list.scrollTop=0;list.dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true}));
                await new Promise(function(r){setTimeout(r,350);});
                if(list.scrollTop<20)return 'contact-wheel';
                var workspace=document.getElementById('workspace');
                workspace.scrollTop=0;list.scrollTop=list.scrollHeight-list.clientHeight;
                list.dispatchEvent(new WheelEvent('wheel',{deltaY:120,bubbles:true,cancelable:true}));
                await new Promise(function(r){setTimeout(r,350);});
                if(workspace.scrollTop<20)return 'contact-wheel-boundary-chain';
                for(var slot of ['wol','hots','nova']){
                    var inner=grid.querySelector('[data-contact-items='+slot+']');
                    function reset(node,top){
                        node.dispatchEvent(new PointerEvent('pointerdown',{button:0,bubbles:true}));
                        node.scrollTop=top;
                    }
                    async function wheel(delta){
                        inner.dispatchEvent(new WheelEvent('wheel',{deltaY:delta,bubbles:true,cancelable:true}));
                        await new Promise(function(r){setTimeout(r,350);});
                    }
                    reset(workspace,0);reset(inner,inner.scrollHeight-inner.clientHeight);
                    await wheel(120);
                    if(workspace.scrollTop<20)return 'contact-end-down:'+slot;
                    reset(workspace,120);reset(inner,0);
                    await wheel(-120);
                    if(workspace.scrollTop>100)return 'contact-end-up:'+slot;
                    reset(workspace,0);reset(inner,inner.scrollHeight-inner.clientHeight-30);
                    await wheel(300);
                    if(workspace.scrollTop<100)return 'contact-excess-down:'+slot;
                    reset(workspace,300);reset(inner,30);
                    await wheel(-300);
                    if(workspace.scrollTop>200)return 'contact-excess-up:'+slot;
                }
                u.setActivityTotals({cleared:1,seconds:36000000});
                var value=document.getElementById('statPlayTime'),unit=value.querySelector('span');
                if(Math.abs(value.getBoundingClientRect().bottom-unit.getBoundingClientRect().bottom)>15)return 'playtime-wrap';
                f.beginEdit(null);f.editorCommand({action:'field',key:'menu',value:'top'});
                if(document.querySelector('.side-quick-actions').getBoundingClientRect().width<80)return 'quick-actions-hidden';
                f.editorCommand({action:'button',value:'apply'});
                document.querySelector('[data-preset=focus]').click();
                var missions=document.getElementById('focusMissions').getBoundingClientRect();
                var time=document.getElementById('heroPlayTime').getBoundingClientRect();
                if(missions.x>=time.x||Math.abs(missions.bottom-time.bottom)>15)return 'focus-record-alignment';
                var hero=document.getElementById('hero').getBoundingClientRect();
                var body=document.getElementById('heroDescription'),before=body.textContent;
                if(body.parentNode!==document.querySelector('.hero-copy')||missions.left<hero.left+hero.width/2)return 'focus-description-left-information-right';
                body.textContent='긴 상세 설명에서도 정보와 기록 영역은 겹치지 않습니다. '.repeat(30);
                var description=body.getBoundingClientRect(),information=document.getElementById('focusDetails').getBoundingClientRect();
                if(description.right>information.left||description.height>141)return 'focus-long-description-overlap';
                body.textContent=before;
                u.setView('all');
                var listBefore=document.getElementById('campaignList').firstChild,start=performance.now();
                u.setView('archive');window.__archiveSwitchMilliseconds=performance.now()-start;
                if(document.getElementById('campaignList').firstChild!==listBefore)return 'archive-rebuilt-hidden-cards';
                u.setView('all');
                return 'ok';
            })().then(function(result){chrome.webview.postMessage('qa-contact-boundaries:'+result);},
                function(error){chrome.webview.postMessage('qa-contact-boundaries:'+String(error));});");
            if(await Task.WhenAny(correctedCompletion.Task,Task.Delay(15000))!=correctedCompletion.Task)
                throw new InvalidOperationException("Contact boundary fixture did not report completion.");
            corrected=await correctedCompletion.Task;
            }finally{hubWeb.CoreWebView2.WebMessageReceived-=correctedHandler;}
            if(corrected!="ok")throw new InvalidOperationException("Corrected layouts contract: "+corrected);
            Console.WriteLine("ARCHIVE LOCAL SWITCH milliseconds="+await hubWeb.ExecuteScriptAsync("window.__archiveSwitchMilliseconds"));
            await hubWeb.ExecuteScriptAsync("document.querySelector('[data-preset=base]').click();");
            // Reopen so native message transport, not a cloned mock state, is exercised.
            ChangeLiveTheme(LiveTheme.Terran,false);
            hubEditorQaEnabled=true;
            await hubWeb.ExecuteScriptAsync("HangarFeatures.beginEdit(null)");
            for(int i=0;i<40&&(hubEditorWeb==null||!hubEditorReady);i++)await Task.Delay(100);
            if(hubEditorWeb==null||!hubEditorReady)throw new InvalidOperationException("External editor did not reopen.");
            await Task.Delay(100);
            var ready=Data.Json.DeserializeObject(await hubEditorWeb.ExecuteScriptAsync(
                "document.querySelectorAll('[data-layout-color]').length===20&&!!document.querySelector('[data-layout-field=menuShape]')"));
            if(!(ready is bool)||!(bool)ready)throw new InvalidOperationException("External editor content missing.");
            string editorSurface=Data.Json.Deserialize<string>(await hubEditorWeb.ExecuteScriptAsync(@"(function(){
                var header=getComputedStyle(document.querySelector('.layout-editor-header'));
                var content=getComputedStyle(document.querySelector('.layout-editor'));
                return header.backgroundColor===content.backgroundColor&&content.backgroundImage==='none'?'ok':'editor-header-background-mismatch';
            })()"));
            if(editorSurface!="ok")throw new InvalidOperationException(editorSurface);
            await hubEditorWeb.ExecuteScriptAsync("(function(){var e=document.querySelector('[data-layout-field=menuShape]');e.value='protoss';e.dispatchEvent(new Event('change',{bubbles:true}));})()");
            bool changed=false;
            for(int i=0;i<30&&!changed;i++){
                await Task.Delay(100);
                changed=Convert.ToBoolean(Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync("document.body.dataset.menuShape==='protoss'")));
            }
            if(!changed)throw new InvalidOperationException("External editor input did not reach current live layout.");
            await hubEditorWeb.ExecuteScriptAsync("(function(){var e=document.querySelector('[data-layout-color=accent]');e.value='#b18afe';e.dispatchEvent(new Event('change',{bubbles:true}));})()");
            bool nativeColored=false;
            for(int i=0;i<30&&!nativeColored;i++){await Task.Delay(100);nativeColored=GlassTheme.Accent.ToArgb()==ColorTranslator.FromHtml("#b18afe").ToArgb();}
            if(!nativeColored)throw new InvalidOperationException("Custom theme color did not reach native auxiliary dialogs.");
            using(var image=File.Create(Path.Combine(output,"external-editor.png")))
                await hubEditorWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            await hubEditorWeb.ExecuteScriptAsync("document.querySelector('[data-edit-action=apply]').click()");
            for(int i=0;i<30&&hubEditorWindow!=null;i++)await Task.Delay(100);
            if(hubEditorWindow!=null)throw new InvalidOperationException("Saved external editor did not close.");
            string savedProfiles=await hubWeb.ExecuteScriptAsync("HangarFeatures.getProfiles()");
            await hubWeb.ExecuteScriptAsync("HangarFeatures.beginEdit(null)");
            for(int i=0;i<40&&(hubEditorWeb==null||!hubEditorReady);i++)await Task.Delay(100);
            if(hubEditorWindow==null||!hubEditorReady)throw new InvalidOperationException("Editor close fixture did not reopen.");
            await hubWeb.ExecuteScriptAsync("HangarFeatures.editorCommand({action:'color',key:'accent',value:'#7fa2bf'})");
            hubEditorWindow.Close();
            await Task.Delay(150);
            if(hubEditorWindow==null)throw new InvalidOperationException("First editor X discarded unsaved edits without confirmation.");
            hubEditorWindow.Close();
            for(int i=0;i<30&&hubEditorWindow!=null;i++)await Task.Delay(100);
            if(hubEditorWindow!=null||savedProfiles!=await hubWeb.ExecuteScriptAsync("HangarFeatures.getProfiles()"))
                throw new InvalidOperationException("Second editor X did not close and preserve saved profile.");
            hubEditorQaEnabled=false;
            availableAppUpdate=new AppRelease {version="99.0.0",channel=AppChannel.Id,notes="격리 알림 검사"};
            PushHubUpdateNotice();
            await Task.Delay(100);
            await hubWeb.ExecuteScriptAsync("document.getElementById('updateBannerIgnore').click()");
            for(int i=0;i<30&&declinedAppUpdateVersion!="99.0.0";i++)await Task.Delay(100);
            if(Data.Read<string>(Path.Combine(hubProfileRoot,"ignored-app-update.json"))!="99.0.0")
                throw new InvalidOperationException("Ignored update version was not persisted.");
            PushHubUpdateNotice();
            await Task.Delay(80);
            if(!Convert.ToBoolean(Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync("document.getElementById('updateBanner').hidden"))))
                throw new InvalidOperationException("Ignored update reappeared.");
            availableAppUpdate.version="99.0.1";PushHubUpdateNotice();
            await Task.Delay(80);
            if(Convert.ToBoolean(Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync("document.getElementById('updateBanner').hidden"))))
                throw new InvalidOperationException("A newer update was suppressed by an older ignored version.");
            using(var image=File.Create(Path.Combine(output,"update-banner.png")))
                await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
            availableAppUpdate=null;PushHubUpdateNotice();
            await hubWeb.ExecuteScriptAsync("HangarFeatures.applyHostProfiles("+original+");UiLab.setView('all');");
            Console.WriteLine("WEBVIEW REFINED EDITOR PASS: independent colors/gradients/shapes, applied version, uninstalled dedicated, contact records/menu, scoped controls, external editor input/save/close.");
            Console.WriteLine("WEBVIEW UPDATE NOTICE PASS: ignored version persisted, same release suppressed, newer release shown without installation.");
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClientRect")]
        static extern bool GetArtworkClientRect(IntPtr window, out ArtworkClientRect rectangle);
        struct ArtworkClientRect { public int left, top, right, bottom; }

        async Task VerifyThemeArtworkAsync(string output)
        {
            var originalSize = ClientSize;
            var originalMaximum = MaximumSize;
            MaximumSize = new Size(8000, 5000);
            string profiles = await hubWeb.ExecuteScriptAsync("window.HangarFeatures.getProfiles()");
            string originalTheme = await hubWeb.ExecuteScriptAsync("window.UiLab.getState().theme");
            double originalZoom = hubWeb.ZoomFactor;
            Task restore = null;
            try
            {
                await hubWeb.ExecuteScriptAsync("(function(){var profiles=window.HangarFeatures.getProfiles();" +
                    "Object.keys(profiles).forEach(function(id){profiles[id].layout.colors={};});" +
                    "window.HangarFeatures.applyHostProfiles(profiles);window.UiLab.setView('all');})()");
                foreach (string theme in new[] { "terran", "protoss", "zerg", "nova" })
                {
                    await hubWeb.ExecuteScriptAsync("window.UiLab.setThemeFromHost(" + Data.Json.Serialize(theme) + ")");
                    foreach (string preset in new[] { "base", "focus", "list", "contact" })
                    {
                        ClientSize = new Size(1920, 1080);
                        await hubWeb.ExecuteScriptAsync("document.querySelector('[data-preset=" + preset + "]').click()");
                        await hubWeb.ExecuteScriptAsync(@"window.__themeArtworkResult=null;(async function(){
                            await Promise.all(Array.from(document.querySelectorAll('.theme-option img,#brandGlyph img')).map(function(img){return img.decode();}));
                            await new Promise(requestAnimationFrame);
                            var cards=Array.from(document.querySelectorAll('.theme-option')).map(function(e){return e.getBoundingClientRect();});
                            if(Math.abs(cards[0].top-cards[1].top)>1||cards[2].top<cards[0].bottom-1||
                               Math.abs(cards[2].top-cards[3].top)>1)return 'two-by-two';
                            var selected=document.querySelector('.theme-option[aria-pressed=true] img');
                            var brand=document.querySelector('#brandGlyph img');
                            if(!selected||!brand||selected.getAttribute('src')!==brand.getAttribute('src')||
                               getComputedStyle(brand.parentElement).visibility!=='visible')return 'brand';
                            if(document.querySelectorAll('.theme-option[aria-pressed=true]').length!==1)return 'selection';
                            var themeGroup=document.querySelector('.sidebar>.theme-switcher').getBoundingClientRect();
                            if(cards.some(function(r){return r.width<24||r.height<24||r.left<themeGroup.left-1||r.right>themeGroup.right+1;}))return 'card-clipping';
                            UiLab.setView('all');await new Promise(requestAnimationFrame);
                            var bg=getComputedStyle(document.body).getPropertyValue('--boot-image').trim();
                            var image=new Image();image.src=bg.slice(4,-1).replace(/^['""]|['""]$/g,'');await image.decode();
                            if(['base','focus'].includes(document.body.dataset.layoutPreset)){
                                var app=document.getElementById('app'),savedHeight=app.style.height;
                                var list=document.querySelector('.campaign-list-region');
                                app.style.height='1300px';await new Promise(requestAnimationFrame);
                                var initial=list.clientHeight;
                                app.style.height='1700px';await new Promise(requestAnimationFrame);
                                var growth=list.clientHeight-initial;app.style.height=savedHeight;
                                if(growth<250)return 'list-height-not-growing:'+growth;
                            }
                            return 'ok';
                        })().then(function(result){window.__themeArtworkResult=result;},
                            function(error){window.__themeArtworkResult='image-error:'+error.message;});");
                        string result = null;
                        for (int attempt = 0; attempt < 30 && result == null; attempt++)
                        {
                            await Task.Delay(100);
                            result = Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync("window.__themeArtworkResult"));
                        }
                        if (result != "ok") throw new InvalidOperationException("Theme artwork " + theme + "/" + preset + ": " + result);
                        using (var image = File.Create(Path.Combine(output, "theme-" + theme + "-" + preset + ".png")))
                            await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                    }
                }
                foreach (var size in new[] { new Size(980,760), new Size(2560,1440), new Size(3840,2160) })
                {
                    ClientSize = size;
                    await hubWeb.ExecuteScriptAsync("document.querySelector('[data-preset=list]').click()");
                    await Task.Delay(110);
                    ArtworkClientRect client;
                    if (!GetArtworkClientRect(Handle, out client))
                        throw new InvalidOperationException("Could not read the actual native client rectangle.");
                    if (hubWeb.Width != client.right || hubWeb.Height != client.bottom)
                        throw new InvalidOperationException("Themed viewport leaves native outer margins: client=" + client.right + "x" + client.bottom +
                            ", web=" + hubWeb.Bounds + ", page=" + homePage.Bounds + ", screen=" + screen.Bounds +
                            ", layout=" + liveShellLayout.Bounds + ", padding=" + liveShellLayout.Padding +
                            ", screen margin=" + screen.Margin + ", page=" + currentPage.Name);
                    Console.WriteLine("WEBVIEW EDGE PASS: requested " + size.Width + "x" + size.Height +
                        ", actual native client and WebView " + client.right + "x" + client.bottom + ".");
                    string result = Data.Json.Deserialize<string>(await hubWeb.ExecuteScriptAsync(@"(function(){
                        var app=document.getElementById('app').getBoundingClientRect();
                        if(Math.abs(app.left)>1||Math.abs(app.top)>1||Math.abs(app.right-innerWidth)>1||
                           Math.abs(app.bottom-innerHeight)>1)return 'outer-gap';
                        if(document.documentElement.scrollWidth>innerWidth+1)return 'horizontal-overflow';
                        var cards=Array.from(document.querySelectorAll('.theme-option')).map(function(e){return e.getBoundingClientRect();});
                        var side=document.querySelector('.sidebar').getBoundingClientRect();
                        if(cards.some(function(r){return r.top<side.top-1||r.bottom>side.bottom+1;}))return 'horizontal-menu-clipping';
                        return 'ok';
                    })()"));
                    if (result != "ok") throw new InvalidOperationException("Viewport " + size + ": " + result);
                    using (var image = File.Create(Path.Combine(output, "viewport-" + size.Width + ".png")))
                        await hubWeb.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, image);
                }
                ClientSize = new Size(1920,1080);
                foreach (double zoom in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    hubWeb.ZoomFactor = zoom;
                    await Task.Delay(110);
                    bool fits = Convert.ToBoolean(Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                        "document.documentElement.scrollWidth<=innerWidth+1")));
                    if (!fits) throw new InvalidOperationException("Horizontal overflow at simulated scale " + zoom);
                }
                Console.WriteLine("WEBVIEW ARTWORK PASS: 4 themes x 4 layouts, PNG brand/card identity, decoded backgrounds, 980/FHD/QHD/4K edge coverage and simulated 100/125/150/200% scale.");
            }
            finally
            {
                hubWeb.ZoomFactor = originalZoom;
                ClientSize = originalSize;
                MaximumSize = originalMaximum;
                restore = hubWeb.ExecuteScriptAsync("window.HangarFeatures.applyHostProfiles(" + profiles +
                    ");window.UiLab.setThemeFromHost(" + originalTheme + ");delete window.__themeArtworkResult;");
            }
            await restore;
        }

        void InitializeLiveHub(string profileRoot)
        {
            if (!AppChannel.HasThemedUi) return;
            hubProfileRoot = profileRoot;
            hubFavoritesFile = Path.Combine(profileRoot, "ui-hub-favorites.json");
            hubLayoutFile = Path.Combine(profileRoot, "ui-hub-layouts.json");
            hubHiddenCampaignsFile = Path.Combine(profileRoot, "ui-hub-hidden-campaigns.json");
            hubPresetLibraryFile = Path.Combine(profileRoot, "ui-hub-custom-presets.json");
            homePage.BackColor = GlassTheme.Background;
            homePage.Dock = DockStyle.Fill;
            homePage.Margin = Padding.Empty;
            FormClosing += delegate(object sender,FormClosingEventArgs e) {
                if (!isolated && hubEditorWindow != null && !hubEditorClosing) {
                    e.Cancel=true;
                    hubWeb.ExecuteScriptAsync("window.HangarFeatures.editorCommand({action:'close'})");
                }
            };
            FormClosed += delegate {
                CloseHubEditor(); CloseHubWorkWindow();
                if (hubArchiveWindow != null && !hubArchiveWindow.IsDisposed)
                { hubArchiveWindow.Hide(); hubArchiveWindow.Close(); }
            };
            hubWeb = new WebView2 { Dock = DockStyle.Fill, Visible = false,
                AllowExternalDrop = true,
                DefaultBackgroundColor = GlassTheme.Background };
            homePage.Controls.Add(hubWeb);
            hubFallback = new Label { Dock = DockStyle.Fill, AutoSize = false,
                ForeColor = GlassTheme.Muted, BackColor = GlassTheme.Background,
                Font = new Font("맑은 고딕", 15, FontStyle.Bold),
                Text = "커캠 격납고 작전실을 연결하고 있습니다...",
                TextAlign = ContentAlignment.MiddleCenter };
            homePage.Controls.Add(hubFallback);
            hubEmbeddedArchive = new Panel { Visible = false,
                BackColor = GlassTheme.Background, TabStop = false };
            // WebView2's native composition surface cannot reliably be placed
            // above another WebView2 HWND inside the same WinForms parent.
            // Use a borderless owned window precisely clipped to the themed
            // archive mount: the user still sees one integrated archive UI.
            hubArchiveWindow = new Form {
                FormBorderStyle = FormBorderStyle.None,
                ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                BackColor = GlassTheme.Background, AutoScaleMode = AutoScaleMode.None,
                TopMost = false
            };
            hubEmbeddedArchive.Dock = DockStyle.Fill;
            hubArchiveWindow.Controls.Add(hubEmbeddedArchive);
            LocationChanged += delegate { PositionHubArchiveWindow(); };
            SizeChanged += delegate { PositionHubArchiveWindow(); };
            // Never dispose this owned window in FormClosing: another handler
            // may still cancel a close (notably while an update is busy).
        }
        void BeginHubShutdown()
        {
            if (hubShuttingDown) return;
            hubShuttingDown = true;
            hubArchiveRequestedVisible = false;
            hubLastArchiveRect = null;
            hubPendingArchiveId = null;
            hubPendingArchiveReviews = false;
            if (hubWeb != null && !hubWeb.IsDisposed)
            {
                try
                {
                    if (hubWeb.CoreWebView2 != null)
                        hubWeb.CoreWebView2.WebMessageReceived -= LiveHubMessage;
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            }
        }
        bool HubArchiveAlive(Control hosted)
        {
            return !hubShuttingDown && !IsDisposed && !Disposing &&
                archiveView != null && !archiveView.IsDisposed && !archiveView.Disposing &&
                distributionView != null && !distributionView.IsDisposed && !distributionView.Disposing &&
                hubWeb != null && !hubWeb.IsDisposed && !hubWeb.Disposing &&
                hubEmbeddedArchive != null && !hubEmbeddedArchive.IsDisposed && !hubEmbeddedArchive.Disposing &&
                hubArchiveWindow != null && !hubArchiveWindow.IsDisposed && !hubArchiveWindow.Disposing &&
                (hosted == null || (!hosted.IsDisposed && !hosted.Disposing));
        }
        internal void VerifyUpdateCloseLifecycle()
        {
            if (!isolated || online || !AppChannel.HasThemedUi || hubArchiveWindow == null)
                throw new InvalidOperationException("Update-close lifecycle test requires an offline isolated themed app.");
            if (LiveTheme.Current == LiveTheme.Classic)
                LiveTheme.Select(LiveTheme.Resolve("terran"));
            ShowPage(homePage);
            SetBusy(true); // The updater has not acknowledged readiness yet.
            appUpdateInProgress = true;
            try
            {
                Close();
                if (IsDisposed || hubArchiveWindow.IsDisposed || hubShuttingDown)
                    throw new InvalidOperationException("A canceled busy close must keep the archive host and app alive.");
                Console.WriteLine("UPDATE LIFECYCLE PASS: busy close was canceled without disposing archive host.");

                var rect = new HubEmbeddedRect { x = 12, y = 20, width = 500, height = 380,
                    viewWidth = 1280, viewHeight = 720 };
                // Simulate the exact stale ArchiveView observed in the 7393
                // WinForms stack trace, without downloading anything or
                // touching the user's real browser profile.
                archiveView.Dispose();
                ShowHubEmbeddedArchive(rect);
                if (hubArchiveRequestedVisible)
                    throw new InvalidOperationException("A disposed ArchiveView was re-shown by a stale rectangle.");
                Console.WriteLine("UPDATE LIFECYCLE PASS: disposed ArchiveView ignores late archive-rect messages.");

                appUpdateExitApproved = true; // Updater ready-file received.
                Close();
                if (!IsDisposed || !hubShuttingDown)
                    throw new InvalidOperationException("Updater-ready close must terminate the old busy app.");
                ShowHubEmbeddedArchive(rect);
                Console.WriteLine("UPDATE LIFECYCLE PASS: updater-ready close exited; post-close archive messages ignored.");
            }
            finally
            {
                if (!IsDisposed)
                {
                    appUpdateExitApproved = false;
                    appUpdateInProgress = false;
                    SetBusy(false);
                }
            }
        }

        void PositionHubArchiveWindow()
        {
            if (!HubArchiveAlive(null) ||
                hubLastArchiveRect == null || hubWeb == null ||
                hubWeb.IsDisposed || homePage.IsDisposed || !homePage.IsHandleCreated) return;
            if (hubArchiveSuspendedByIntro || WindowState == FormWindowState.Minimized ||
                LiveTheme.Current == LiveTheme.Classic || currentPage != homePage)
            { hubArchiveWindow.Hide(); return; }
            var rect = hubLastArchiveRect;
            if (rect.viewWidth < 100 || rect.viewHeight < 100) return;
            int x = hubWeb.Left + (int)Math.Round((double)rect.x * hubWeb.ClientSize.Width / rect.viewWidth);
            int y = hubWeb.Top + (int)Math.Round((double)rect.y * hubWeb.ClientSize.Height / rect.viewHeight);
            int w = (int)Math.Round((double)rect.width * hubWeb.ClientSize.Width / rect.viewWidth);
            int h = (int)Math.Round((double)rect.height * hubWeb.ClientSize.Height / rect.viewHeight);
            Rectangle bounds = Rectangle.Intersect(new Rectangle(x,y,w,h),homePage.ClientRectangle);
            if (bounds.Width < 120 || bounds.Height < 100) return;
            hubArchiveWindow.Bounds = new Rectangle(homePage.PointToScreen(bounds.Location),bounds.Size);
            if (hubArchiveRequestedVisible && !hubArchiveWindow.Visible)
                hubArchiveWindow.Show(this);
        }

        // The original ArchiveView WebView2 is reparented into a framed
        // region INSIDE the four species' UI, never opened as a native page.
        // Ratios account for Windows DPI independently of WebView2 CSS pixels.
        async void ShowHubEmbeddedArchive(HubEmbeddedRect rect)
        {
            if (!HubArchiveAlive(null) ||
                LiveTheme.Current == LiveTheme.Classic || currentPage != homePage ||
                rect == null || rect.viewWidth < 100 || rect.viewHeight < 100 ||
                rect.width < 120 || rect.height < 120 || rect.width > 30000 ||
                rect.height > 30000) return;
            hubLastArchiveRect = rect;
            // Messages from an earlier animation frame may arrive after the
            // intro has begun. Never re-show the owned website in that period.
            if (hubArchiveSuspendedByIntro) return;
            Control hosted = hubDistributionActive ? (Control)distributionView : archiveView;
            if (!HubArchiveAlive(hosted)) return;
            try
            {
            if (hosted.Parent != hubEmbeddedArchive)
            {
                if (hosted.Parent != null) hosted.Parent.Controls.Remove(hosted);
                hubEmbeddedArchive.Controls.Add(hosted);
                hosted.Dock = DockStyle.Fill;
                if (hosted == archiveView) archiveView.SetEmbeddedInHub(true);
            }
            if (hubDistributionActive && archiveView.Parent == hubEmbeddedArchive)
                archiveView.Hide();
            if (!hubDistributionActive && distributionView.Parent == hubEmbeddedArchive)
                distributionView.Hide();
            hosted.Show();hosted.BringToFront();
            hubArchiveRequestedVisible = true;
            hubEmbeddedArchive.Show();hubEmbeddedArchive.BringToFront();
            PositionHubArchiveWindow();
            if (!hubArchiveOpenRequested && !hubDistributionActive)
            {
                hubArchiveOpenRequested = true;
                try { await archiveView.OpenAsync(); }
                catch (Exception e) { if (HubArchiveAlive(null)) status.Text = "아카이브 연결 실패 · " + e.Message; }
            }
            if (!HubArchiveAlive(null)) return;
            if (!String.IsNullOrWhiteSpace(hubPendingArchiveId))
            {
                string id = hubPendingArchiveId; hubPendingArchiveId = null;
                bool openReviews = hubPendingArchiveReviews;hubPendingArchiveReviews=false;
                var item = FindLiveEntry(id);
                if (item != null)
                {
                    try
                    {
                        var site=ResolveHubSiteCampaign(item);
                        if (site!=null) await archiveView.OpenCampaignAsync(site,openReviews);
                        else
                        {
                            archiveView.HomeFromHub();
                            status.Text=item.name+" · 연결된 아카이브 상세 페이지가 없습니다. 설치·관리에서 사이트 캠페인을 연결해 주세요.";
                        }
                    }
                    catch (Exception e) { if (HubArchiveAlive(null)) status.Text = "캠페인 상세 페이지 연결 실패 · " + e.Message; }
                }
            }
            }
            catch (ObjectDisposedException)
            {
                // A late WebView2 rectangle is not an instruction to recreate
                // an ArchiveView already disposed during the update handoff.
                if (HubArchiveAlive(null)) status.Text = "아카이브 화면이 종료되어 다시 표시하지 않았습니다.";
            }
        }
        void HideHubEmbeddedArchive()
        {
            hubArchiveRequestedVisible = false;
            if (hubArchiveWindow != null && !hubArchiveWindow.IsDisposed)
                hubArchiveWindow.Hide();
            if (hubEmbeddedArchive != null) hubEmbeddedArchive.Hide();
        }
        bool RedirectHubNativePage(Panel page)
        {
            // Existing offline WinForms regression fixtures intentionally
            // navigate the original pages; they must remain testable even
            // though real rich-mode users never enter these pages.
            if ((isolated && !online) || IsDisposed || LiveTheme.Current == LiveTheme.Classic ||
                currentPage != homePage || hubWeb == null || hubWeb.IsDisposed ||
                hubWeb.CoreWebView2 == null || page == homePage) return false;
            string section = page == archivePage || page == distributionPage ? "archive" :
                page == installedPage ? "installed" :
                page == settingsPage ? "settings" : null;
            if (section == null) return false;
            try
            {
                hubDistributionActive = page == distributionPage;
                // Existing import/link/repair handlers still run, but they no
                // longer eject the user from the game-styled hub on completion.
                hubWeb.ExecuteScriptAsync("window.UiLab.setView(" +
                    Data.Json.Serialize(section) + ",true)");
                if (section == "archive" && hubLastArchiveRect != null)
                    ShowHubEmbeddedArchive(hubLastArchiveRect);
                if (page == installedPage) PushLiveHubData();
            }
            catch (Exception e)
            { status.Text = "게임풍 화면 이동 실패 · " + e.Message; }
            return true;
        }
        void RestoreHubArchiveToNative()
        {
            HideHubEmbeddedArchive();
            hubArchiveSuspendedByIntro = false;
            hubDistributionActive = false;
            if (archiveView.Parent != archivePage)
            {
                if (archiveView.Parent != null) archiveView.Parent.Controls.Remove(archiveView);
                archivePage.Controls.Add(archiveView);
                archiveView.Dock = DockStyle.Fill;
            }
            archiveView.SetEmbeddedInHub(false);archiveView.Show();
            if (distributionView.Parent != distributionPage)
            {
                if (distributionView.Parent != null) distributionView.Parent.Controls.Remove(distributionView);
                distributionPage.Controls.Add(distributionView);
                distributionView.Dock = DockStyle.Fill;
            }
            distributionView.Show();
        }

        static int HubBound(int value, int min, int max)
        { return Math.Max(min,Math.Min(max,value)); }
        static bool HubAllowed(string value, params string[] choices)
        { return choices.Contains(value); }
        static string[] HubOrder(string[] source, string[] allowed)
        {
            var unique = new List<string>();
            foreach (string value in source ?? new string[0])
                if (allowed.Contains(value) && !unique.Contains(value)) unique.Add(value);
            foreach (string value in allowed)
                if (!unique.Contains(value)) unique.Add(value);
            return unique.ToArray();
        }
        static Dictionary<string,HubFreePanelRect> CleanHubFreePanels(Dictionary<string,HubFreePanelRect> source)
        {
            var minimum = new Dictionary<string,int[]> {
                {"header",new[]{300,80}}, {"hero",new[]{260,155}},
                {"actions",new[]{245,65}}, {"toolbar",new[]{290,95}},
                {"list",new[]{250,210}}, {"detail",new[]{190,190}}
            };
            foreach (var id in new[]{"detail","wol","hots","lotv","nova","dedicated"})
                minimum["contact-"+id] = new[]{1,1};
            foreach(var id in new[]{"launchButton","installButton","detailButton","screenshotButton",
                "reviewButton","recommendButton","importZipButton"})
                minimum[id]=new[]{90,40};
            var clean = new Dictionary<string,HubFreePanelRect>();
            foreach (var item in minimum)
            {
                HubFreePanelRect value;
                if (source == null || !source.TryGetValue(item.Key,out value) || value == null) continue;
                int width = HubBound(value.w,1,1000);
                int height = HubBound(value.h,1,1000);
                clean[item.Key] = new HubFreePanelRect {
                    x = HubBound(value.x,0,1000-width),y = HubBound(value.y,0,10000-height),
                    w=width,h=height,locked=value.locked
                };
            }
            return clean;
        }
        static string[] CleanHubActions(string[] source)
        {
            var supplied=(source??new string[0])
                .Select(id=>id=="favoriteButton"?"screenshotButton":id).ToArray();
            var order=HubOrder(supplied,new[] {"launchButton","installButton",
                "detailButton","screenshotButton","reviewButton","recommendButton",
                "importZipButton"}).ToList();
            // When pre-existing custom layouts contain only five buttons, the
            // newly added review/recommend actions were appended after ZIP.
            // Place just those additions immediately before ZIP; preserve the
            // owner's ordering for every older action.
            bool missing=supplied.All(x=>x!="reviewButton")||
                supplied.All(x=>x!="recommendButton");
            bool oldTail=order.Count>=3&&order[order.Count-3]=="importZipButton"&&
                order[order.Count-2]=="reviewButton"&&order[order.Count-1]=="recommendButton";
            if(missing||oldTail)
            {
                order.Remove("reviewButton");order.Remove("recommendButton");
                int insert=order.IndexOf("importZipButton");
                order.Insert(insert,"reviewButton");
                order.Insert(insert+1,"recommendButton");
            }
            return order.ToArray();
        }
        static HubUserLayout CleanHubLayout(HubUserLayout source)
        {
            source = source ?? new HubUserLayout();
            string menu = HubAllowed(source.menu,"top","bottom","left","right") ? source.menu : "left";
            var nav = HubOrder(source.nav,new[] {"archive","all","wol","hots","lotv","nova","dedicated","settings","custom"}).ToList();
            // Existing saved profiles may have appended 'all' at the end.
            if (nav.IndexOf("all") > nav.IndexOf("wol")) {
                nav.Remove("all"); nav.Insert(nav.IndexOf("wol"),"all");
            }
            // Legacy profiles must also keep Nova directly below Legacy of the Void.
            if (nav.IndexOf("nova") != nav.IndexOf("lotv")+1) {
                nav.Remove("nova"); nav.Insert(nav.IndexOf("lotv")+1,"nova");
            }
            var colors = new Dictionary<string,string>();
            foreach (var key in new[] {"accent","secondary","bg","panel","panelEnd","menu","menuEnd","list","listEnd",
                "detail","detailEnd","text","titleText","mutedText","buttonText","menuIcon","iconColor",
                "borderColor","selectedColor","progressColor"}) {
                string hex;
                if (source.colors != null && source.colors.TryGetValue(key,out hex) &&
                    hex != null && System.Text.RegularExpressions.Regex.IsMatch(hex,"^#[0-9a-fA-F]{6}$"))
                    colors[key] = hex.ToLowerInvariant();
            }
            var texts=new Dictionary<string,string>();
            foreach(var key in new[]{"sectionEyebrow","heroKicker","catalogEyebrow","actionCaption","heroSideCaption"}){
                string text;
                if(source.texts!=null&&source.texts.TryGetValue(key,out text)&&text!=null)
                    texts[key]=text.Substring(0,Math.Min(120,text.Length));
            }
            var quickSizes=new Dictionary<string,HubQuickSize>();
            foreach(var key in new[]{"hero","detail","art","contactArt","contact-detail","contact-wol","contact-hots","contact-lotv","contact-nova","contact-dedicated"}){
                HubQuickSize size;
                if(source.quickSizes!=null&&source.quickSizes.TryGetValue(key,out size)&&size!=null)
                    quickSizes[key]=new HubQuickSize {w=HubBound(size.w,120,1200),h=HubBound(size.h,80,1200)};
            }
            var elements=new[]{"header","hero","actions","toolbar","list","detail","contact-detail","contact-wol",
                "contact-hots","contact-lotv","contact-nova","contact-dedicated","launchButton","installButton",
                "detailButton","screenshotButton","reviewButton","recommendButton","importZipButton"};
            return new HubUserLayout {
                menu = menu,
                detail = HubAllowed(source.detail,"right","left","hidden") ? source.detail : "right",
                menuSize = HubBound(source.menuSize,(menu=="top"||menu=="bottom")?70:170,340),
                heroHeight = HubBound(source.heroHeight,145,440),
                detailWidth = HubBound(source.detailWidth,200,520),
                listHeight = HubBound(source.listHeight,260,750),
                cols = HubBound(source.cols,1,4),
                spacing = HubBound(source.spacing,5,30),
                cardHeight = HubBound(source.cardHeight,72,150),
                parts = HubOrder(source.parts,new[] {"hero","actions","catalog"}),
                nav = nav.ToArray(),
                hiddenCampaigns = (source.hiddenCampaigns ?? new string[0]).Where(k =>
                    new[] {"all","wol","hots","lotv","nova","dedicated"}.Contains(k))
                    .Distinct(StringComparer.Ordinal).ToArray(),
                actions = CleanHubActions(source.actions),
                navGap = HubBound(source.navGap ?? 5,0,18),
                menuShape = HubAllowed(source.menuShape,"auto","terran","protoss","zerg","nova") ? source.menuShape : "auto",
                presentation = HubAllowed(source.presentation,"base","focus","list","contact") ? source.presentation : "base",
                contactOrder = HubOrder(source.contactOrder,new[] {"detail","wol","hots","lotv","nova","dedicated"}),
                colors = colors,
                texts = texts,
                quickSizes=quickSizes,
                hiddenElements=(source.hiddenElements??new string[0]).Where(elements.Contains).Distinct().ToArray(),
                free = source.free,
                panels = CleanHubFreePanels(source.panels)
            };
        }
        static Dictionary<string,HubUserLayoutProfile> CleanHubProfiles(string raw)
        {
            var source = Data.Json.Deserialize<Dictionary<string,HubUserLayoutProfile>>(raw);
            if (source == null) throw new InvalidDataException("레이아웃 저장값이 비어 있습니다.");
            var clean = new Dictionary<string,HubUserLayoutProfile>();
            foreach (var id in new[] {"terran","protoss","zerg","nova"})
            {
                HubUserLayoutProfile p;
                if (!source.TryGetValue(id,out p) || p == null) continue;
                string preset = HubAllowed(p.preset,"base","wide","focus","compact","list","contact","custom")?p.preset:"custom";
                var named = new Dictionary<string,HubUserLayout>();
                if (p.named != null)
                    foreach (var item in p.named.Take(8))
                        if (!String.IsNullOrWhiteSpace(item.Key) && item.Key.Length <= 30 &&
                            item.Key != "__proto__" && item.Key != "constructor")
                            named[item.Key] = CleanHubLayout(item.Value);
                clean[id] = new HubUserLayoutProfile {
                    preset = preset, layout=CleanHubLayout(p.layout), named=named
                };
            }
            return clean;
        }
        async void SaveHubLayouts(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw) || raw.Length > 32000 || hubLayoutFile == null) return;
            bool ok = false;
            string result = "레이아웃을 저장하지 못했습니다.";
            try
            {
                var clean = CleanHubProfiles(raw);
                Directory.CreateDirectory(Path.GetDirectoryName(hubLayoutFile));
                string temp = hubLayoutFile + ".new";
                File.WriteAllText(temp,Data.Json.Serialize(clean),new UTF8Encoding(false));
                if (File.Exists(hubLayoutFile))
                    File.Replace(temp,hubLayoutFile,hubLayoutFile+".previous",true);
                else File.Move(temp,hubLayoutFile);
                status.Text = "종족별 사용자 레이아웃 저장 완료";
                result = "이 테마의 사용자 레이아웃을 저장했습니다.";
                ok = true;
            }
            catch (Exception error) {
                status.Text = "레이아웃을 저장하지 못했습니다 · " + error.Message;
                result = status.Text;
            }
            if (hubWeb != null && hubWeb.CoreWebView2 != null)
                try {
                    await hubWeb.ExecuteScriptAsync(
                        "window.HangarFeatures&&window.HangarFeatures.layoutSaved(" +
                        (ok ? "true" : "false") + "," + Data.Json.Serialize(result) + ")");
                } catch { }
        }
        async Task PushHubLayouts()
        {
            if (hubWeb == null || hubWeb.CoreWebView2 == null) return;
            try
            {
                string json = "{}";
                if (File.Exists(hubLayoutFile))
                    json = Data.Json.Serialize(CleanHubProfiles(File.ReadAllText(hubLayoutFile,Encoding.UTF8)));
                await hubWeb.ExecuteScriptAsync(
                    "window.HangarFeatures&&window.HangarFeatures.applyHostProfiles(" + json + ")");
            }
            catch (Exception e) { System.Diagnostics.Trace.WriteLine("Hub layouts load: " + e.Message); }
        }
        static string HubPresetName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length < 1 || name.Length > 48 || name.Any(char.IsControl))
                throw new InvalidDataException("디자인 이름은 1~48자로 입력해 주세요.");
            return name;
        }
        static string HubPresetTheme(string id)
        {
            if (!HubAllowed(id,"terran","protoss","zerg","nova"))
                throw new InvalidDataException("테란·프로토스·저그·노바 테마만 가져올 수 있습니다.");
            return id;
        }
        static HubCustomPresetExchange ReadHubPresetExchange(string raw)
        {
            if (String.IsNullOrWhiteSpace(raw) || Encoding.UTF8.GetByteCount(raw) > 262144)
                throw new InvalidDataException("커스텀 UI 파일이 비어 있거나 256KB를 초과했습니다.");
            var input=Data.Json.Deserialize<HubCustomPresetExchange>(raw);
            if (input==null || input.format!="campaign-hangar-ui" || input.version!=1 || input.layout==null)
                throw new InvalidDataException("커캠 격납고 UI 공유 파일 형식(v1)이 아닙니다.");
            return new HubCustomPresetExchange {
                format="campaign-hangar-ui",version=1,
                name=HubPresetName(input.name),theme=HubPresetTheme(input.theme),
                layout=CleanHubLayout(input.layout)
            };
        }
        HubCustomPresetStore ReadHubPresetStore()
        {
            var store=File.Exists(hubPresetLibraryFile)
                ? Data.Json.Deserialize<HubCustomPresetStore>(File.ReadAllText(hubPresetLibraryFile,Encoding.UTF8))
                : new HubCustomPresetStore { schema=1,items=new List<HubCustomPreset>() };
            if (store==null || store.schema!=1 || store.items==null || store.items.Count>64)
                throw new InvalidDataException("커스텀 UI 보관함 구조가 올바르지 않습니다.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var item in store.items)
            {
                Guid id;
                if(item==null || !Guid.TryParse(item.id,out id) || !seen.Add(item.id) ||
                    item.layout==null || item.created==null || item.created.Length>80)
                    throw new InvalidDataException("커스텀 UI 보관함 항목이 손상됐습니다.");
                item.name=HubPresetName(item.name);
                item.theme=HubPresetTheme(item.theme);
                item.layout=CleanHubLayout(item.layout);
            }
            return store;
        }
        void SaveHubPresetStore(HubCustomPresetStore store)
        {
            if(store.items.Count>64)throw new InvalidOperationException("디자인은 최대 64개까지 보관할 수 있습니다.");
            Directory.CreateDirectory(Path.GetDirectoryName(hubPresetLibraryFile));
            string temp=hubPresetLibraryFile+".new-"+Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp,Data.Json.Serialize(store),new UTF8Encoding(false));
                if(File.Exists(hubPresetLibraryFile))
                    File.Replace(temp,hubPresetLibraryFile,
                        hubPresetLibraryFile+".backup-"+Guid.NewGuid().ToString("N"),true);
                else File.Move(temp,hubPresetLibraryFile);
            }
            finally {if(File.Exists(temp))File.Delete(temp);}
        }
        async Task PushHubPresetLibrary()
        {
            if(hubWeb==null || hubWeb.IsDisposed || hubWeb.CoreWebView2==null)return;
            try {
                var items=ReadHubPresetStore().items;
                await hubWeb.ExecuteScriptAsync("window.HangarFeatures&&window.HangarFeatures.setPresetLibrary("+
                    Data.Json.Serialize(items)+")");
            }
            catch(Exception error){status.Text="커스텀 UI 보관함 읽기 실패 · "+error.Message;}
        }
        async Task HubPresetFeedback(string message)
        {
            status.Text=message;
            if(hubWeb==null || hubWeb.IsDisposed || hubWeb.CoreWebView2==null)return;
            try{await hubWeb.ExecuteScriptAsync("window.HangarFeatures&&window.HangarFeatures.presetFeedback("+
                Data.Json.Serialize(message)+")");}
            catch(Exception error){System.Diagnostics.Trace.WriteLine("Preset feedback: "+error.Message);}
        }
        IWin32Window HubPresetDialogOwner()
        {
            return hubEditorWindow!=null&&!hubEditorWindow.IsDisposed?(IWin32Window)hubEditorWindow:this;
        }
        async Task SaveHubCustomPreset(string raw)
        {
            try
            {
                var candidate=ReadHubPresetExchange(raw);
                var store=ReadHubPresetStore();
                if(store.items.Count>=64)throw new InvalidOperationException("보관함이 가득 찼습니다. 사용하지 않는 디자인을 삭제해 주세요.");
                var item=new HubCustomPreset { id=Guid.NewGuid().ToString("N"),
                    name=candidate.name,theme=candidate.theme,layout=candidate.layout,
                    created=DateTimeOffset.UtcNow.ToString("o"),shared=false };
                store.items.Insert(0,item);
                SaveHubPresetStore(store);
                await PushHubPresetLibrary();
                await HubPresetFeedback("디자인 ‘"+item.name+"’을 보관함에 저장했습니다.");
            }
            catch(Exception error){HubPresetFeedback("디자인 저장 실패 · "+error.Message);}
        }
        static string HubPresetFileName(string name)
        {
            var invalid=Path.GetInvalidFileNameChars();
            string cleaned=new string(name.Where(c=>!invalid.Contains(c)&&!char.IsControl(c)).ToArray()).Trim();
            if(cleaned.Length>48)cleaned=cleaned.Substring(0,48);
            return String.IsNullOrWhiteSpace(cleaned)?"CampaignHangar-UI":cleaned;
        }
        static void WriteHubPresetShare(string path, HubCustomPresetExchange candidate)
        {
            if(String.IsNullOrWhiteSpace(path)||candidate==null)
                throw new InvalidDataException("내보낼 파일과 디자인을 확인할 수 없습니다.");
            var validated=ReadHubPresetExchange(Data.Json.Serialize(candidate));
            File.WriteAllText(path,Data.Json.Serialize(validated),new UTF8Encoding(false));
        }
        async Task ExportHubCustomPreset(string raw)
        {
            try
            {
                var candidate=ReadHubPresetExchange(raw);
                using(var dialog=new SaveFileDialog {
                    Title="커스텀 UI 내보내기",Filter="커캠 격납고 UI (*.chui.json)|*.chui.json|JSON 파일 (*.json)|*.json",
                    FileName=HubPresetFileName(candidate.name)+".chui.json",AddExtension=true,OverwritePrompt=true
                })
                {
                    if(dialog.ShowDialog(HubPresetDialogOwner())!=DialogResult.OK)return;
                    WriteHubPresetShare(dialog.FileName,candidate);
                }
                await HubPresetFeedback("커스텀 UI 공유 파일을 내보냈습니다.");
            }
            catch(Exception error){HubPresetFeedback("UI 내보내기 실패 · "+error.Message);}
        }
        async Task ImportHubCustomPreset()
        {
            try
            {
                string path;
                using(var dialog=new OpenFileDialog {
                    Title="공유받은 커스텀 UI 불러오기",
                    Filter="커캠 격납고 UI (*.chui.json)|*.chui.json|JSON 파일 (*.json)|*.json",
                    CheckFileExists=true,Multiselect=false
                })
                {
                    if(dialog.ShowDialog(HubPresetDialogOwner())!=DialogResult.OK)return;
                    path=dialog.FileName;
                }
                await ImportHubCustomPresetFile(path);
            }
            catch(Exception error){HubPresetFeedback("UI 불러오기 실패 · "+error.Message);}
        }
        async Task<HubCustomPreset> ImportHubCustomPresetFile(string path)
        {
            var file=new FileInfo(path);
            if(file.Length>262144 || file.Length==0)
                throw new InvalidDataException("커스텀 UI 파일은 256KB 이하여야 합니다.");
            var candidate=ReadHubPresetExchange(File.ReadAllText(file.FullName,Encoding.UTF8));
            var store=ReadHubPresetStore();
            if(store.items.Count>=64)throw new InvalidOperationException("보관함이 가득 찼습니다.");
            var item=new HubCustomPreset {id=Guid.NewGuid().ToString("N"),
                name=candidate.name,theme=candidate.theme,layout=candidate.layout,
                created=DateTimeOffset.UtcNow.ToString("o"),shared=true};
            store.items.Insert(0,item);
            SaveHubPresetStore(store);
            // A shared file is data only: the sanitized layout has no OS path,
            // executable, campaign ID, account, or personal play-record fields.
            await PushHubPresetLibrary();
            if(hubWeb!=null&&!hubWeb.IsDisposed&&hubWeb.CoreWebView2!=null)
            {
                var applied=Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                    "window.HangarFeatures&&window.HangarFeatures.applyImportedPreset("+
                    Data.Json.Serialize(item)+")"));
                if(!(applied is bool) || !(bool)applied)
                    throw new InvalidOperationException("공유 디자인을 보관했지만 화면에 적용하지 못했습니다.");
            }
            await HubPresetFeedback("공유 디자인 ‘"+item.name+"’을 보관하고 현재 화면에 적용했습니다.");
            return item;
        }
        async Task ExportStoredHubPreset(string id)
        {
            try
            {
                var entry=ReadHubPresetStore().items.SingleOrDefault(x=>x.id==id);
                if(entry==null)throw new InvalidDataException("내보낼 디자인을 찾지 못했습니다.");
                await ExportHubCustomPreset(Data.Json.Serialize(new HubCustomPresetExchange {
                    format="campaign-hangar-ui",version=1,name=entry.name,theme=entry.theme,layout=entry.layout
                }));
            }
            catch(Exception error){HubPresetFeedback("보관 디자인 내보내기 실패 · "+error.Message);}
        }
        async Task DeleteStoredHubPreset(string id)
        {
            try
            {
                var store=ReadHubPresetStore();
                var item=store.items.SingleOrDefault(x=>x.id==id);
                if(item==null)throw new InvalidDataException("보관 디자인을 찾지 못했습니다.");
                if(!AppDialog.Confirm(HubPresetDialogOwner(),
                    "‘"+item.name+"’을 보관함에서 삭제하시겠습니까?\n현재 화면에 적용한 디자인은 유지됩니다.",
                    "커스텀 UI 보관함","보관함에서 삭제","취소"))return;
                store.items.Remove(item);
                SaveHubPresetStore(store);
                await PushHubPresetLibrary();
                await HubPresetFeedback("보관함에서 ‘"+item.name+"’을 삭제했습니다.");
            }
            catch(Exception error){HubPresetFeedback("보관 디자인 삭제 실패 · "+error.Message);}
        }
        async Task PushHubHostSettings()
        {
            if (hubWeb == null || hubWeb.CoreWebView2 == null) return;
            try
            {
                var data = new {
                    game = game.Text,storage=storagePath.Text,downloads=downloadPath.Text,
                    bank=File.Exists(Path.Combine(hubProfileRoot,"bank-folder-selection.json"))
                        ? Data.Read<string>(Path.Combine(hubProfileRoot,"bank-folder-selection.json")) : Convert.ToString(bankFolders.SelectedItem),
                    confirm=applyConfirmation.Checked,autoApply=autoApplyAfterInstall.Checked,
                    autoRemove=autoRemovePreviousVersions.Checked,
                    unreadNotifications=notificationFeed == null ? 0 : notificationFeed.UnreadCount
                };
                await hubWeb.ExecuteScriptAsync("window.HangarFeatures&&window.HangarFeatures.showHostSettings(" +
                    Data.Json.Serialize(data) + ");window.UiLab&&window.UiLab.showNotificationCount(" +
                    data.unreadNotifications + ");");
            }
            catch (Exception e) { System.Diagnostics.Trace.WriteLine("Hub native settings read: " + e.Message); }
        }
        async void ActivateHubSetting(string action)
        {
            // Dispatch the real controller method, never search for a native
            // button by localized text or depend on an invisible native page.
            if (busy || distributionView.DownloadInProgress)
            {
                status.Text = "작업이 진행 중입니다. 작업 완료 후 설정을 변경해 주세요.";
                return;
            }
            try
            {
                await SyncHubDialogPalette();
                switch (action)
                {
                    case "game": await PickGame(); break;
                    case "storage": await ChangeStorage(); break;
                    case "downloads": ChangeDownloads(); break;
                    case "site-refresh": await RefreshSite(); break;
                    case "update-check": await CheckAppUpdate(true); break;
                    case "history": AppDialog.Info(this,
                        jobs.Count == 0 ? "이번 실행의 기록이 없습니다." : String.Join("\n", jobs),
                        "설치·다운로드 기록"); break;
                    case "bank":
                        ShowHubBankFolders();
                        break;
                    default: return;
                }
                if (!IsDisposed) await PushHubHostSettings();
            }
            catch (Exception error) { Error(error); }
        }
        void ApplyHubDialogPalette(HubDialogPalette palette)
        {
            if(palette==null||palette.theme!=LiveTheme.Current.Id)return;
            var colors=CleanHubLayout(new HubUserLayout {colors=palette.colors}).colors;
            var previous=LiveTheme.Snapshot();
            LiveTheme.ApplyColors(colors);
            LiveTheme.RecolorTree(this,previous,LiveTheme.Snapshot());
            if(IsHandleCreated)GlassTheme.ApplyTitleBar(Handle);
            Invalidate(true);
        }
        async Task SyncHubDialogPalette()
        {
            if(!hubReady||hubWeb==null||hubWeb.CoreWebView2==null)return;
            string json=await hubWeb.ExecuteScriptAsync(@"(function(){
                var f=HangarFeatures,id=UiLab.getState().theme;
                return {theme:id,colors:(f.isEditing()?f.getEditingLayout():f.getLayout(id)).colors};
            })()");
            ApplyHubDialogPalette(Data.Json.Deserialize<HubDialogPalette>(json));
        }

        void ShowHubBankFolders()
        {
            RefreshBankFolders(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            using(var dialog=new ThemeForm {Text="뱅크 폴더 관리",ClientSize=new Size(780,310),MinimumSize=new Size(620,350)})
            {
                var grid=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=4,BackColor=Color.Transparent};
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute,36));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,50));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent,100));grid.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
                var title=new Label {Text="계정·프로필 뱅크 경로",Dock=DockStyle.Fill,ForeColor=GlassTheme.Accent,BackColor=Color.Transparent};
                var paths=new GlassChoiceField {Dock=DockStyle.Fill,
                    BackColor=GlassTheme.Field,ForeColor=GlassTheme.Text,AccessibleName="뱅크 폴더 경로",DropDownWidth=1100};
                var hint=new Label {Text="경로 선택은 격납고가 열 폴더만 바꿉니다. 게임의 뱅크 파일을 이동하거나 변경하지 않습니다.",
                    Dock=DockStyle.Fill,ForeColor=GlassTheme.Muted,BackColor=Color.Transparent};
                string saved=Path.Combine(hubProfileRoot,"bank-folder-selection.json");
                Action<string> choose=delegate(string path) {
                    if(String.IsNullOrWhiteSpace(path))return;
                    if(!paths.Items.Contains(path))paths.Items.Add(path);
                    paths.SelectedIndex=paths.Items.IndexOf(path);
                };
                foreach(var item in bankFolders.Items)paths.Items.Add(item);
                if(File.Exists(saved))choose(Data.Read<string>(saved));
                if(paths.SelectedIndex<0&&paths.Items.Count>0)paths.SelectedIndex=0;
                if(paths.Items.Count==0)hint.Text="찾은 뱅크 폴더가 없습니다. 경로 선택으로 기존 Banks 폴더를 지정할 수 있습니다.";
                dialog.FormClosed+=delegate {
                    if(paths.SelectedItem!=null)Data.Write(saved,Convert.ToString(paths.SelectedItem));
                };
                var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,FlowDirection=FlowDirection.LeftToRight,BackColor=Color.Transparent};
                var find=new MetalButton {Text="다시 찾기",AutoSize=true};
                var pick=new MetalButton {Text="경로 선택",AutoSize=true};
                var open=new MetalButton {Text="폴더 열기",AutoSize=true};
                var close=new MetalButton {Text="닫기",AutoSize=true,DialogResult=DialogResult.Cancel};
                find.Click+=delegate {
                    RefreshBankFolders(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                    foreach(var item in bankFolders.Items)if(!paths.Items.Contains(item))paths.Items.Add(item);
                    if(paths.SelectedIndex<0&&paths.Items.Count>0)paths.SelectedIndex=0;
                    hint.Text=bankStatus.Text;
                };
                pick.Click+=delegate {
                    using(var browser=new FolderBrowserDialog {Description="격납고에서 열 뱅크 폴더 선택",ShowNewFolderButton=false,
                        SelectedPath=Convert.ToString(paths.SelectedItem)}) {
                        if(browser.ShowDialog(dialog)!=DialogResult.OK)return;
                        choose(browser.SelectedPath);Data.Write(saved,browser.SelectedPath);
                    }
                };
                open.Click+=delegate {
                    string path=Convert.ToString(paths.SelectedItem);
                    if(!Directory.Exists(path)){AppDialog.Info(dialog,"선택한 폴더가 없습니다. 다시 찾거나 경로를 선택해 주세요.","뱅크 폴더");return;}
                    System.Diagnostics.Process.Start("explorer.exe","\""+path+"\"");
                };
                actions.Controls.AddRange(new Control[]{find,pick,open,close});
                grid.Controls.Add(title,0,0);grid.Controls.Add(paths,0,1);grid.Controls.Add(hint,0,2);grid.Controls.Add(actions,0,3);
                dialog.Controls.Add(grid);dialog.CancelButton=close;dialog.ShowDialog(this);
            }
        }

        async void OpenHubNotifications()
        {
            try
            {
                await ShowNotifications();
                if (!IsDisposed) await PushHubHostSettings();
            }
            catch (Exception error) { Error(error); }
        }

        async void EnsureLiveHub()
        {
            if (!AppChannel.HasThemedUi || IsDisposed || hubWeb == null ||
                hubWeb.IsDisposed) return;
            if (hubReady) { await PushLiveHubData(); return; }
            if (hubInitializing || (hubWeb.CoreWebView2 != null && hubWeb.Source != null))
                return;
            hubInitializing = true;
            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "ui-hub");
                if (!File.Exists(Path.Combine(folder, "index.html")))
                    throw new FileNotFoundException("작전실의 UI 파일이 누락되었습니다.", folder);
                Directory.CreateDirectory(Path.Combine(hubProfileRoot, "ui-hub-webview"));
                var env = await CoreWebView2Environment.CreateAsync(null,
                    Path.Combine(hubProfileRoot, "ui-hub-webview"));
                await hubWeb.EnsureCoreWebView2Async(env);
                // WebView2 already accounts for Windows per-monitor DPI.
                // Applying 96/DpiX again made the real development window
                // microscopically small at 150% scaling (0.67x).
                // Use a consistent 100% browser zoom, like the UI previews.
                hubWeb.ZoomFactor = 1.0;
                hubWeb.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                hubWeb.CoreWebView2.Settings.AreDevToolsEnabled = false;
                hubWeb.CoreWebView2.Settings.AreHostObjectsAllowed = false;
                hubWeb.CoreWebView2.Settings.IsStatusBarEnabled = false;
                hubWeb.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hangar-live.test", folder, CoreWebView2HostResourceAccessKind.Allow);
                var thumbnails = Path.Combine(root, "thumbnails");
                Directory.CreateDirectory(thumbnails);
                hubWeb.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    "hangar-thumbs.test", thumbnails, CoreWebView2HostResourceAccessKind.Allow);
                hubWeb.CoreWebView2.NavigationStarting += delegate(object sender, CoreWebView2NavigationStartingEventArgs e)
                {
                    if (!e.Uri.StartsWith("https://hangar-live.test/", StringComparison.OrdinalIgnoreCase) &&
                        !e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                        e.Cancel = true;
                };
                hubWeb.CoreWebView2.WebMessageReceived += LiveHubMessage;
                hubWeb.CoreWebView2.NavigationCompleted += async delegate(object sender, CoreWebView2NavigationCompletedEventArgs e)
                {
                    if (!e.IsSuccess)
                    {
                        hubFallback.Text = "작전실 UI를 표시하지 못했습니다. 상단의 아카이브·내 캠페인 버튼으로 기존 기능을 사용할 수 있습니다.";
                        status.Text = "작전실 WebView2 연결 실패 · 기존 캠페인 기능은 유지됩니다.";
                        return;
                    }
                    hubReady = true;
                    hubWeb.Visible = true;
                    hubWeb.BringToFront();
                    UpdateHubWorkState();
                    if (!IsDisposed) await PushLiveHubData();
                };
                hubWeb.Visible = true;
                hubWeb.BringToFront();
                // Startup/theme animations are unrelated to background-work UI.
                bool intro = !isolated;
                hubWeb.Source = new Uri("https://hangar-live.test/index.html?live=1&embed=1&theme=" +
                    LiveTheme.Current.Id + "&intro=" + (intro ? "1" : "0") + "&channel=" + AppChannel.Id);
            }
            catch (Exception error)
            {
                status.Text = "작전실 시작 실패 · " + error.Message;
                if (hubFallback != null && !hubFallback.IsDisposed)
                {
                    hubFallback.Text = "작전실 시각 화면을 표시할 수 없습니다.\n" +
                        error.Message + "\n\n기존 아카이브·내 캠페인 기능은 상단에서 계속 사용할 수 있습니다.";
                    hubFallback.Show(); hubFallback.BringToFront();
                }
            }
            finally { hubInitializing = false; }
        }

        static string CategoryLabel(string slot, string kind)
        {
            if (kind == "dedicated" || kind == "external" || slot == "external") return "전용";
            if (slot == "hots") return "군심";
            if (slot == "lotv") return "공유";
            if (slot == "nco") return "노바";
            return "자날";
        }
        static bool HasKorean(string value)
        {
            return !String.IsNullOrWhiteSpace(value) && value.Any(c => c >= '\uac00' && c <= '\ud7a3');
        }
        string KoreanHubSummary(Campaign item)
        {
            if (HasKorean(item.summary)) return item.summary;
            // Prefer the verified Korean catalogue description when the user's
            // already-installed local metadata still contains English prose.
            var published = catalogue.FirstOrDefault(c =>
                c != null && String.Equals(c.id,item.id,StringComparison.OrdinalIgnoreCase) &&
                HasKorean(c.summary));
            if (published != null) return published.summary;
            // Read-only Korean summaries for the existing locally installed
            // English-only manifests. Preserve creator metadata and ZIP files;
            // these descriptions are display translations, not stored edits.
            var translated = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
                {"borl","로그라이트 요소를 추가한 캠페인입니다. 임무마다 여러 차례 영구 재능과 저주를 선택할 수 있으며 선택한 효과는 다음 임무에도 이어집니다."},
                {"eskr","레이너 로그 기반 병력 구성과 명령 카드를 적용하고, 부활 절차를 통일했습니다. 점막 없이 사용하는 주점과 기술실을 지원하며, 선택 조각을 없애고 특전 세 가지를 도입했습니다. 엔진 플레이 검증은 완료되지 않았습니다."},
                {"hots-legend","로그라이크 요소를 더한 군단의 심장 캠페인입니다. 임무 중 여러 차례 영구 재능과 저주를 선택할 수 있으며 선택한 효과는 이후 임무까지 이어집니다."},
                {"local-99c3caf455cae47f","자유의 날개 악몽 난이도 변형 캠페인입니다. 유닛은 히페리온에서만 생산하며, 소수의 강력한 부대를 세밀하게 조작하는 전투에 집중합니다."},
                {"mod-469567afcff63a168258cc61","자유의 날개 모든 유닛의 크기를 스타크래프트 설정집에 묘사된 크기에 맞춰 조정한 모드입니다."},
                {"mod-88480aa80edebe84fe75c512","캠페인 시작 시 조종할 유닛이 무작위로 결정되며, 선택된 유닛들을 자유의 날개 진행 중 언제든 활용할 수 있습니다."},
                {"mod-d4e53485fd80e4bd089b9661","군단의 심장 유닛 크기를 세계관 설정에 맞춰 조정했습니다. 대형 전함은 설정상 규모의 약 3분의 1 크기로 표현됩니다."},
                {"project-ued-wol-korean","UED 세력을 이용해 자유의 날개 캠페인을 진행하는 커스텀 캠페인입니다."},
                {"realscale","공허의 유산 유닛 크기와 능력치를 세계관 설정에 가깝게 조정한 변형 캠페인입니다."}
            };
            string translation;
            if (translated.TryGetValue(item.id,out translation)) return translation;
            var category = CategoryLabel(item.slot,item.kind);
            string type = item.kind == "dedicated" || item.kind == "external" ?
                "독립 실행·연결형 캠페인" : "커스텀 캠페인";
            return item.name + " · " + category + " 분류의 설치된 " + type +
                "입니다. 임무 구성과 최신 배포 설명은 아카이브 상세에서 확인할 수 있습니다.";
        }
        string LocalCover(Campaign item)
        {
            try
            {
                var url = Catalogue.ThumbnailUrl(item);
                if (!String.IsNullOrEmpty(url) && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    string cached;
                    using (var sha = SHA256.Create())
                        cached = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "");
                    string image = cached + "-original.png";
                    if (File.Exists(Path.Combine(root, "thumbnails", image)))
                        return "https://hangar-thumbs.test/" + image;
                    // The URL is validated to belong to the configured archive.
                    // No title-based image guessing: the artwork must belong to
                    // this exact catalogue entry.
                    return url;
                }
            }
            catch (Exception e) { System.Diagnostics.Trace.WriteLine("Hub thumbnail fallback: " + e.Message); }
            return "assets/hangar.jpg";
        }
        List<LiveHubEntry> LiveEntries()
        {
            List<Campaign> local;
            try { local = library.All(); }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Live hub library read: " + error.Message);
                local = new List<Campaign>();
            }
            try
            {
                local.AddRange(ExternalCampaigns.VisibleForChannel(
                    externalCampaigns.All(), AppChannel.IsDevelopment));
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Live hub external campaigns: " + error.Message);
            }
            var entries = new List<LiveHubEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<Campaign,bool> add = delegate(Campaign item, bool isInstalled)
            {
                if (item == null || String.IsNullOrWhiteSpace(item.id) ||
                    String.IsNullOrWhiteSpace(item.name) || !seen.Add(item.id)) return;
                PlayRecord record = null;
                if (isInstalled && activity != null)
                {
                    try { record = activity.Snapshot(item.id); }
                    catch (Exception error) { System.Diagnostics.Trace.WriteLine("Hub record read: " + error.Message); }
                }
                Campaign linkedSite = null;
                if (isInstalled)
                {
                    try
                    {
                        var siteId = library.CatalogueId(item.id);
                        if (!String.IsNullOrWhiteSpace(siteId))
                            linkedSite = catalogue.FirstOrDefault(c => c.id == siteId);
                    }
                    catch (Exception error) { System.Diagnostics.Trace.WriteLine("Hub version link: " + error.Message); }
                }
                var updateState = "unknown";
                if (linkedSite != null && !String.IsNullOrWhiteSpace(linkedSite.version) &&
                    !String.IsNullOrWhiteSpace(item.version))
                {
                    if (Catalogue.SameVersion(linkedSite.version,item.version)) updateState = "current";
                    else if (Catalogue.NewerVersion(linkedSite.version,item.version)) updateState = "available";
                }
                var metadata = linkedSite ?? item;
                var labels = Catalogue.SiteKeys.Select((key,index)=>new {key,index})
                    .Where(value=>(metadata.categories??new string[0]).Contains(value.key))
                    .Select(value=>Catalogue.SiteTitles[value.index]).ToArray();
                entries.Add(new LiveHubEntry {
                    id=item.id, versionKey=item.versionKey, name=item.name, en=item.name, kind=item.kind == "external" ? "전용 캠페인" :
                        item.kind == "dedicated" ? "전용 캠페인" :
                        labels.Length>0 ? String.Join(" · ",labels) : "분류 미등록",
                    category=CategoryLabel(item.slot,item.kind), installed=isInstalled,
                    dedicated=item.kind == "dedicated" || item.kind == "external" || item.slot == "external",
                    author=String.IsNullOrWhiteSpace(metadata.sourceAuthor) ? "제작자 미표기" : metadata.sourceAuthor,
                    img=LocalCover(metadata), desc=KoreanHubSummary(metadata),
                    updateState=updateState,siteVersion=linkedSite==null?null:linkedSite.version,
                    playSeconds=record == null ? 0 : record.seconds,
                    missions=record == null ? 0 : record.missions,
                    clears=record == null ? 0 : record.clears,
                    bankLinked=record != null && !String.IsNullOrWhiteSpace(record.bankPath),
                    needsReview=record != null && record.needsReview,
                    lastMission=record == null ? null : record.lastMission,
                    bankError=record == null ? null : record.bankError,
                    completedMissions=record == null ? new string[0] : record.completed.ToArray()
                });
            };
            foreach (var c in local) add(c,true);
            foreach (var c in catalogue.Where(c => c != null))
            {
                bool installedDedicated = c.kind == "dedicated" &&
                    dedicatedLibrary != null && dedicatedLibrary.IsInstalled(c);
                add(c,installedDedicated);
            }
            return entries;
        }
        async Task<bool> PushLiveHubData()
        {
            if (!hubReady || hubWeb == null || hubWeb.IsDisposed || hubWeb.CoreWebView2 == null) return false;
            try
            {
                var entries = LiveEntries();
                var installedEntries = entries.Where(c => c.installed || c.dedicated).ToList();
                var json = Data.Json.Serialize(installedEntries);
                var accepted = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                    "window.UiLab&&window.UiLab.setRealCampaigns(" + json + "," + entries.Count + ")"));
                if (!(accepted is bool) || !(bool)accepted)
                    throw new InvalidOperationException("작전실 캠페인 목록 반영 응답이 없습니다.");
                // Read the existing activation journal; never infer 'active'
                // from the selected card or any game process.
                var applied = library.Current();
                Func<string,string> label = id => {
                    if (String.IsNullOrWhiteSpace(id)) return "기록 없음";
                    var match = installedEntries.FirstOrDefault(c =>
                        String.Equals(c.id,id,StringComparison.OrdinalIgnoreCase));
                    return match == null ? "등록 정보 없음 (" + id + ")" : match.name;
                };
                await hubWeb.ExecuteScriptAsync("window.UiLab.setActivationState(" +
                    Data.Json.Serialize(new {activeId=applied.active,versionKey=applied.versionKey,previousId=applied.previousActive,
                        activeName=applied.active==null?"적용 내역 없음":label(applied.active),
                        previousName=label(applied.previousActive)}) + ")");
                List<string> favorite = new List<string>();
                try
                {
                    if (File.Exists(hubFavoritesFile)) favorite = Data.Read<List<string>>(hubFavoritesFile);
                }
                catch (Exception e) { System.Diagnostics.Trace.WriteLine("Hub favorites read: " + e.Message); }
                await hubWeb.ExecuteScriptAsync("window.UiLab.setFavoriteIds(" +
                    Data.Json.Serialize(favorite ?? new List<string>()) + ")");
                List<string> hidden = new List<string>();
                try {
                    if(File.Exists(hubHiddenCampaignsFile))
                        hidden = Data.Read<List<string>>(hubHiddenCampaignsFile) ?? new List<string>();
                }
                catch(Exception error){System.Diagnostics.Trace.WriteLine("Hub hidden list read: "+error.Message);}
                await hubWeb.ExecuteScriptAsync("window.UiLab.setHiddenCampaignIds("+
                    Data.Json.Serialize(hidden)+")");
                await PushLiveHubPreferences();
                await PushHubLayouts();
                await PushHubHostSettings();
                await hubWeb.ExecuteScriptAsync("window.UiLab.setActivityTotals("+
                    Data.Json.Serialize(new {cleared=activity.ClearedCampaigns,seconds=activity.TotalSeconds})+")");
                PushHubUpdateNotice();
                return true;
            }
            catch (Exception error)
            {
                status.Text = "작전실 실제 목록 갱신 실패 · " + error.Message;
                System.Diagnostics.Trace.WriteLine("Hub data refresh: " + error);
                return false;
            }
        }
        async Task<bool> RevealHubCampaign(Campaign item)
        {
            if (item == null || !AppChannel.HasThemedUi) return false;
            if (!hubReady || hubWeb == null || hubWeb.IsDisposed || hubWeb.CoreWebView2 == null)
            {
                status.Text = item.name + " 등록 완료 · 작전실을 열어 목록에서 확인해 주세요.";
                return false;
            }
            try
            {
                ReportHubProgress(new WorkProgress("등록 목록 및 대표 이미지 갱신 중"));
                if (!await PushLiveHubData())
                    throw new InvalidOperationException("등록 목록을 다시 읽지 못했습니다.");
                string category = CategoryLabel(item.slot,item.kind);
                var selected = Data.Json.DeserializeObject(await hubWeb.ExecuteScriptAsync(
                    "window.UiLab&&window.UiLab.revealCampaign(" +
                    Data.Json.Serialize(item.id)+","+Data.Json.Serialize(category)+")"));
                if (!(selected is bool) || !(bool)selected)
                    throw new InvalidOperationException("대상 캠페인 ID·카드·상세 화면을 확인하지 못했습니다.");
                return true;
            }
            catch (Exception error)
            {
                status.Text = item.name + " 등록 완료 · 자동 선택 미완료: " +
                    error.Message + " · 내 캠페인에서 확인해 주세요.";
                System.Diagnostics.Trace.WriteLine("Imported campaign selection: "+error);
                return false;
            }
        }
        async Task PushLiveHubPreferences()
        {
            if (!hubReady || hubWeb == null || hubWeb.IsDisposed ||
                hubWeb.CoreWebView2 == null || uiLook == null) return;
            try
            {
                string json = Data.Json.Serialize(new {
                    theme = LiveTheme.Current.Id,
                    browseWhileWorking = uiLook.Value.browseWhileWorking,
                    reducedMotion = uiLook.Value.reducedMotion,
                    highContrast = uiLook.Value.highContrast,
                    hideThemeButtons = uiLook.Value.hideThemeButtons,
                    hideTopSummary = uiLook.Value.hideTopSummary
                });
                await hubWeb.ExecuteScriptAsync("window.UiLab.applyLivePreferences(" + json + ")");
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Hub preferences update: " + error.Message);
            }
        }
        async void SyncLiveHubTheme(LivePalette palette, bool enteringArchive = false)
        {
            if (palette == LiveTheme.Classic || !hubReady ||
                hubWeb == null || hubWeb.IsDisposed || hubWeb.CoreWebView2 == null) return;
            try
            {
                // Entering the game-styled archive from a native management
                // page must open the archive, not restore a hidden settings tab.
                // An in-WebView theme change keeps the current section.
                await hubWeb.ExecuteScriptAsync("window.UiLab.setThemeFromHost(" +
                    Data.Json.Serialize(palette.Id) + ");" +
                    (enteringArchive ? "window.UiLab.setView('archive',true);" : ""));
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Hub theme sync: " + error.Message);
            }
        }
        Campaign FindLiveEntry(string id)
        {
            if (String.IsNullOrWhiteSpace(id) || id.Length > 128) return null;
            try
            {
                var local = library.All().FirstOrDefault(c =>
                    String.Equals(c.id,id,StringComparison.OrdinalIgnoreCase));
                if (local != null) return local;
                var ext = ExternalCampaigns.VisibleForChannel(externalCampaigns.All(),AppChannel.IsDevelopment)
                    .FirstOrDefault(c => String.Equals(c.id,id,StringComparison.OrdinalIgnoreCase));
                if (ext != null) return ext;
                return catalogue.FirstOrDefault(c =>
                    String.Equals(c.id,id,StringComparison.OrdinalIgnoreCase));
            }
            catch { return null; }
        }
        async void OpenLiveEntry(string id, bool play)
        {
            var entry = FindLiveEntry(id);
            if (entry == null) { status.Text = "선택한 캠페인을 찾을 수 없습니다. 목록을 새로고침해 주세요."; return; }
            if (!play) return; // Details and management live in the new HTML panel.
            Campaign installed = null;
            try { installed = library.Installed(entry.id); } catch { }
            bool available = installed != null || entry.kind == "external" ||
                (entry.kind == "dedicated" && dedicatedLibrary.IsInstalled(entry));
            if (!available)
            {
                status.Text = entry.name + " · 설치되지 않았습니다. 설치·관리에서 연결해 주세요.";
                return;
            }
            // Only a user's explicit Apply/Play click calls the existing,
            // confirmation-protected native activation logic.
            try {
                var target=installed??entry;
                int count=target.kind=="external"&&ExternalCampaigns.LaunchMode(target)=="episodes"
                    ? (target.episodeMaps??new string[0]).Length : target.dedicatedEpisodeCount;
                if (count>1) {
                    using(var dialog=new EpisodeDialog(target.name,count,
                        target.kind=="external" ? target.episodeMaps : null)) {
                        if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                        if(target.kind=="dedicated")await PlayDedicated(target,dialog.Episode);
                        else await LaunchExternal(target,false,dialog.Episode);
                    }
                }
                else await ApplyInstalled(target);
                await PushLiveHubData();
            }
            catch (Exception error) { status.Text = "캠페인을 실행하지 못했습니다 · " + error.Message; }
        }
        Campaign ResolveHubSiteCampaign(Campaign item)
        {
            if(item==null)return null;
            // Local installation IDs may differ from site article IDs.
            // A recommendation/review must always target a verified site ID.
            string siteId=null;
            try { siteId=library.CatalogueId(item.id); }
            catch(Exception error){System.Diagnostics.Trace.WriteLine("Hub site mapping: "+error.Message);}
            // An explicitly linked but missing site article is not permission
            // to vote for a different article with the same local install ID.
            string targetId=String.IsNullOrWhiteSpace(siteId)?item.id:siteId;
            return catalogue.FirstOrDefault(c=>c!=null &&
                String.Equals(c.id,targetId,StringComparison.OrdinalIgnoreCase));
        }
        async void OpenHubArchiveDetail(string id, bool reviews=false)
        {
            if (FindLiveEntry(id) == null) return;
            hubPendingArchiveId = id;
            hubPendingArchiveReviews = reviews;
            if (hubWeb != null && hubWeb.CoreWebView2 != null)
            {
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('archive')");
                if (hubLastArchiveRect != null) ShowHubEmbeddedArchive(hubLastArchiveRect);
            }
        }
        async Task RecommendHubCampaign(string id)
        {
            if(hubRecommendationInFlight)return;
            hubRecommendationInFlight=true;
            bool ok=false,duplicate=false;int? count=null;
            string message="추천을 완료하지 못했습니다.";
            try
            {
                var item=FindLiveEntry(id);
                var site=ResolveHubSiteCampaign(item);
                if(site==null)
                    message="아카이브에 연결된 캠페인이 없습니다. 설치·관리에서 사이트 캠페인을 먼저 연결해 주세요.";
                else if(site.kind=="resource")
                    message="자료 항목은 추천할 수 없습니다.";
                else
                {
                    var result=await archiveView.RecommendCampaignAsync(site.id);
                    ok=result.ok;duplicate=result.duplicate;count=result.count;
                    message=ok ? "‘"+site.name+"’ 추천이 등록됐습니다." :
                        duplicate ? "각 캠페인당 하루 한 번만 추천 가능합니다." :
                        (String.IsNullOrWhiteSpace(result.error)?"추천을 저장하지 못했습니다.":result.error);
                }
            }
            catch(Exception error){message="추천 요청 실패 · "+error.Message;}
            finally
            {
                hubRecommendationInFlight=false;
            }
            status.Text=message;
            if(hubWeb!=null&&!hubWeb.IsDisposed&&hubWeb.CoreWebView2!=null)
                try{await hubWeb.ExecuteScriptAsync("window.UiLab&&window.UiLab.recommendationResult("+
                    Data.Json.Serialize(id)+","+(ok?"true":"false")+","+
                    (count.HasValue?count.Value.ToString(System.Globalization.CultureInfo.InvariantCulture):"null")+","+
                    Data.Json.Serialize(message)+","+(duplicate?"true":"false")+")");}
                catch(Exception error){System.Diagnostics.Trace.WriteLine("Recommend UI result: "+error.Message);}
        }
        async void PerformHubManageAction(string action, string id)
        {
            if (busy || String.IsNullOrWhiteSpace(id) || id.Length > 128) return;
            var entry = FindLiveEntry(id);
            if (entry == null) { status.Text = "선택한 캠페인을 찾을 수 없습니다."; return; }
            Campaign installed = null;
            try { installed = library.Installed(entry.id); } catch { }
            bool present = installed != null || entry.kind == "external" ||
                (entry.kind == "dedicated" && dedicatedLibrary.IsInstalled(entry));
            var target = installed ?? entry;
            try
            {
                if (action == "apply") { if (present) await ApplyInstalled(target); }
                else if (action == "install")
                {
                    if (present && target.kind == "external") EditExternal(target);
                    else if (present) LinkInstalled(target);
                    else if (entry.kind == "dedicated") await InstallDedicated(entry);
                    else OpenHubArchiveDetail(entry.id);
                }
                else if (action == "remove" && present) RemoveInstalled(target);
                else if (action == "update" && present) await OpenHubUpdate(target);
                else if (action == "versions" && present) PickVersion(target);
                else if (action == "records" && present) ShowPlayRecords(target);
                else if (action == "link" && present) LinkInstalled(target);
                else if (action == "link" && !present) OpenHubArchiveDetail(entry.id);
                await PushLiveHubData();
            }
            catch (Exception error) { status.Text = "캠페인 관리 기능을 열지 못했습니다 · " + error.Message; }
        }
        async Task OpenHubUpdate(Campaign installed)
        {
            if (installed == null || busy || requesting || distributionView.DownloadInProgress) return;
            status.Text=installed.name+" · 내부 아카이브에서 업데이트 확인 중";
            if (installed.kind == "external")
            {
                status.Text = "외부 캠페인 업데이트는 제작자 안내에서 확인해 주세요.";
                return;
            }
            string linkedId = null;
            try { linkedId = library.CatalogueId(installed.id); }
            catch (Exception error) { status.Text = "사이트 연결 정보 확인 실패 · " + error.Message; return; }
            if (String.IsNullOrWhiteSpace(linkedId) && installed.kind == "dedicated") linkedId = installed.id;
            var site = catalogue.FirstOrDefault(c => c != null && c.id == linkedId);
            if(site==null && !String.IsNullOrWhiteSpace(linkedId) && await RefreshSite())
                site=catalogue.FirstOrDefault(c=>c!=null && c.id==linkedId);
            if (site == null)
            {
                status.Text = "연결된 아카이브 배포 정보를 찾지 못했습니다. 사이트 연결을 확인해 주세요.";
                return;
            }
            await OpenUpdateFiles(site);
        }
        async void PushHubManageChoices(string action,string id)
        {
            if (busy || (action!="versions" && action!="remove" && action!="link") ||
                String.IsNullOrWhiteSpace(id) || id.Length>128) return;
            var item=FindLiveEntry(id);
            if(item==null)return;
            try
            {
                var installed=library.Installed(item.id);
                var target=installed??item;
                bool stored=target.kind!="dedicated" && target.kind!="external";
                var versions=stored?library.Versions(target.id).Select(v=>new {
                    versionKey=v.versionKey,
                    label=(String.IsNullOrWhiteSpace(v.version)?"버전 미표기":v.version)+
                        " · "+(String.IsNullOrWhiteSpace(v.versionKey)?"키 없음":v.versionKey.Substring(0,Math.Min(10,v.versionKey.Length)))
                }).ToArray():new object[0];
                string linkedId=null;
                try { if(installed!=null)linkedId=library.CatalogueId(target.id); }
                catch(Exception error){System.Diagnostics.Trace.WriteLine("Hub linked site: "+error.Message);}
                var recommended=action=="link"&&target.kind!="external" ?
                    new HashSet<string>(SiteMatcher.Candidates(target,catalogue).Select(c=>c.id),
                        StringComparer.OrdinalIgnoreCase) : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var sites=action=="link"&&target.kind!="external" ? catalogue
                    .Where(c=>c!=null && c.kind!="resource" && c.kind!="dedicated")
                    .Where(c=>!String.IsNullOrWhiteSpace(c.id) && !String.IsNullOrWhiteSpace(c.name))
                    .Select(c=>new {id=c.id,name=c.name,aliases=c.aliases??new string[0],matchSlot=Catalogue.SupportsSlot(c,target.slot),
                        recommended=recommended.Contains(c.id),thumbnail=Catalogue.ThumbnailUrl(c)})
                    .OrderByDescending(c=>c.id==linkedId).ThenByDescending(c=>c.recommended)
                    .ThenByDescending(c=>c.matchSlot).ThenBy(c=>c.name).ToArray():new object[0];
                var data=new {kind=target.kind,versions=versions,sites=sites,selectedId=linkedId};
                if(hubWeb!=null && hubWeb.CoreWebView2!=null)
                    await hubWeb.ExecuteScriptAsync("window.HangarFeatures.setManageChoices("+
                        Data.Json.Serialize(action)+","+Data.Json.Serialize(data)+")");
            }
            catch(Exception error){status.Text="관리 옵션을 읽지 못했습니다 · "+error.Message;}
        }
        async void PerformHubManageFinal(string raw)
        {
            if(busy || String.IsNullOrEmpty(raw)||raw.Length>12000)return;
            try
            {
                var req=Data.Json.Deserialize<HubManageFinalRequest>(raw);
                if(req==null || String.IsNullOrWhiteSpace(req.id)||req.id.Length>128)return;
                var entry=FindLiveEntry(req.id);
                if(entry==null)return;
                var installed=library.Installed(entry.id);
                var target=installed??entry;
                if(req.action=="version-folder"||req.action=="version-apply")
                {
                    if(installed==null||String.IsNullOrWhiteSpace(req.versionKey)||
                        req.versionKey.Length>160)return;
                    var selected=library.Versions(installed.id).FirstOrDefault(v=>
                        String.Equals(v.versionKey,req.versionKey,StringComparison.Ordinal));
                    if(selected==null)return;
                    if(req.action=="version-folder")
                        System.Diagnostics.Process.Start("explorer.exe","\""+library.VersionFolder(selected)+"\"");
                    else await ApplyInstalledVersion(installed,selected.versionKey);
                }
                else if(req.action=="remove-scope")
                {
                    if(target.kind=="dedicated"||target.kind=="external")
                    {
                        if(req.removalKind=="Dedicated")RemoveInstalled(target);
                    }
                    else if(installed!=null)
                    {
                        RemovalKind kind;
                        if(req.removalKind=="PreviousVersions")kind=RemovalKind.PreviousVersions;
                        else if(req.removalKind=="CampaignOnly")kind=RemovalKind.CampaignOnly;
                        else if(req.removalKind=="AllInformation")kind=RemovalKind.AllInformation;
                        else return;
                        await RemoveInstalledSelected(installed,kind);
                    }
                }
                else if(req.action=="link-site")
                {
                    if(target.kind=="external" || installed==null ||
                        String.IsNullOrWhiteSpace(req.siteId)||req.siteId.Length>128)return;
                    if(!await RefreshSite())return;
                    var site=catalogue.FirstOrDefault(c=>c!=null &&
                        c.id==req.siteId && c.kind!="resource" && c.kind!="dedicated");
                    if(site==null)return;
                    bool mismatch=!Catalogue.SupportsSlot(site,installed.slot);
                    if(mismatch&&!req.mismatchAcknowledged)return;
                    // The site picker is now in the species-themed UI. Keep
                    // the validated linker and its exact target-slot guard.
                    library.LinkSite(installed.id,site.id);
                    installedView.Reload(catalogue);
                    status.Text=installed.name+" · 사이트 연결 완료";
                }
                else return;
                await PushLiveHubData();
            }
            catch(Exception error){status.Text="관리 요청을 처리하지 못했습니다 · "+error.Message;}
        }
        async void PerformHubRecordAction(string action, string id)
        {
            if (busy || FindLiveEntry(id)==null) return;
            try
            {
                if (action == "bank")
                {
                    string documents = isolated ? Path.Combine(root,"fixture-documents") :
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    using (var picker = new OpenFileDialog {
                        Filter="레볼라이트 완료 기록 뱅크|RLO8D26WCampaign.SC2Bank",
                        Title="완료 기록 뱅크 연결",
                        InitialDirectory=Path.Combine(documents,"StarCraft II","Accounts")
                    })
                    {
                        if (picker.ShowDialog(this)!=DialogResult.OK) return;
                        // Existing validated reader supports this bank format only.
                        activity.LinkBank(id,picker.FileName);
                    }
                }
                else if (action == "new-run")
                {
                    var record = activity.Snapshot(id);
                    if (!record.needsReview || String.IsNullOrWhiteSpace(record.bankPath)) return;
                    // Explicit in-theme confirmation is required before this message is sent.
                    activity.ConfirmNewRun(id);
                }
                else return;
                installedView.UpdateRecordSummary();
                await PushLiveHubData();
                if (hubWeb != null && hubWeb.CoreWebView2 != null)
                    await hubWeb.ExecuteScriptAsync("window.HangarFeatures.openRecord("+
                        "window.UiLab.getCampaigns().find(function(x){return x.id==="+
                        Data.Json.Serialize(id)+";}) )");
            }
            catch (Exception error) { status.Text="플레이 기록 처리 실패 · "+error.Message; }
        }
        async void LiveHubMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (hubShuttingDown || IsDisposed || Disposing || hubWeb == null || hubWeb.IsDisposed) return;
            // Only accept OS File objects from the local, host-owned game UI.
            // Arbitrary JS path strings must never become import targets.
            Uri source;
            if (!Uri.TryCreate(e.Source,UriKind.Absolute,out source) ||
                source.Scheme!="https" || source.Host!="hangar-live.test" || !source.IsDefaultPort ||
                source.AbsolutePath!="/index.html" || !String.IsNullOrEmpty(source.UserInfo)) return;
            string command;
            try { command = e.TryGetWebMessageAsString(); }
            catch { return; }
            if (String.IsNullOrEmpty(command) || command.Length > 32768) return;
            if(hubProgressActive&&(command.StartsWith("play:",StringComparison.Ordinal)||
                command.StartsWith("theme:",StringComparison.Ordinal)||
                command.StartsWith("manage-action:",StringComparison.Ordinal)||
                command.StartsWith("manage-final:",StringComparison.Ordinal)||
                command.StartsWith("host-setting-toggle:",StringComparison.Ordinal)||
                command=="import-zip"||command=="import-drop"))
            {
                status.Text="진행 중인 작업을 마친 뒤 적용·설치·변경을 사용할 수 있습니다.";
                return;
            }
            try {
            if (command.StartsWith("theme:",StringComparison.Ordinal))
            {
                var theme = command.Substring(6);
                if (LiveTheme.Modes.Any(c => c.Id == theme))
                    ChangeLiveTheme(LiveTheme.Resolve(theme));
            }
            else if(command.StartsWith("native-colors:",StringComparison.Ordinal))
            {
                ApplyHubDialogPalette(Data.Json.Deserialize<HubDialogPalette>(command.Substring(14)));
            }
            else if (command.StartsWith("archive-rect:",StringComparison.Ordinal))
            {
                try { ShowHubEmbeddedArchive(Data.Json.Deserialize<HubEmbeddedRect>(command.Substring(13))); }
                catch (Exception error) { status.Text = "아카이브 화면 배치 실패 · " + error.Message; }
            }
            else if (command == "archive-hide") HideHubEmbeddedArchive();
            else if (command == "archive-suspend")
            {
                hubArchiveSuspendedByIntro = true;
                HideHubEmbeddedArchive();
            }
            else if (command == "archive-resume")
                hubArchiveSuspendedByIntro = false;
            else if (command == "archive-back") archiveView.TryGoBack();
            else if (command == "archive-home") archiveView.HomeFromHub();
            else if (command == "archive-support")
            {
                hubDistributionActive = false;
                await archiveView.SupportFromHub();
                if(hubLastArchiveRect!=null)ShowHubEmbeddedArchive(hubLastArchiveRect);
            }
            else if (command == "work-cancel")
            {
                if (!hubProgressActive) return;
                if (hubWorkCancellation != null && !hubWorkCancellation.IsCancellationRequested)
                    hubWorkCancellation.Cancel();
                else if (hubExternalCancel != null) hubExternalCancel();
            }
            else if(command.StartsWith("work-window-state:",StringComparison.Ordinal))
                ShowHubWorkWindow(command.Substring("work-window-state:".Length));
            else if(command=="work-window-hide")CloseHubWorkWindow();
            else if (command == "archive-reload") archiveView.RefreshFromHub();
            else if (command == "archive-external") archiveView.ExternalFromHub();
            else if (command.StartsWith("archive-detail:",StringComparison.Ordinal))
                OpenHubArchiveDetail(command.Substring(15));
            else if (command.StartsWith("review-campaign:",StringComparison.Ordinal))
                OpenHubArchiveDetail(command.Substring("review-campaign:".Length),true);
            else if (command.StartsWith("recommend-campaign:",StringComparison.Ordinal))
                await RecommendHubCampaign(command.Substring("recommend-campaign:".Length));
            else if (command.StartsWith("layout-save:",StringComparison.Ordinal))
                SaveHubLayouts(command.Substring(12));
            else if (command=="preset-request") await PushHubPresetLibrary();
            else if (command.StartsWith("preset-save:",StringComparison.Ordinal))
                await SaveHubCustomPreset(command.Substring("preset-save:".Length));
            else if (command.StartsWith("preset-export:",StringComparison.Ordinal))
                await ExportHubCustomPreset(command.Substring("preset-export:".Length));
            else if (command=="preset-import") await ImportHubCustomPreset();
            else if (command.StartsWith("preset-export-stored:",StringComparison.Ordinal))
                await ExportStoredHubPreset(command.Substring("preset-export-stored:".Length));
            else if (command.StartsWith("preset-delete:",StringComparison.Ordinal))
                await DeleteStoredHubPreset(command.Substring("preset-delete:".Length));
            else if (command == "editor-open") OpenHubEditor();
            else if (command == "editor-close") CloseHubEditor();
            else if (command.StartsWith("editor-state:",StringComparison.Ordinal))
            {
                try {
                    hubEditorState = Data.Json.Serialize(Data.Json.DeserializeObject(command.Substring(13)));
                    if (hubEditorReady && hubEditorWeb != null && hubEditorWeb.CoreWebView2 != null)
                        hubEditorWeb.CoreWebView2.PostWebMessageAsJson(hubEditorState);
                }
                catch (Exception error) { System.Diagnostics.Trace.WriteLine("Editor state: "+error.Message); }
            }
            else if (command == "settings-request") { await PushHubHostSettings(); }
            else if (command.StartsWith("host-setting-action:",StringComparison.Ordinal))
                ActivateHubSetting(command.Substring(20));
            else if (command.StartsWith("host-setting-toggle:",StringComparison.Ordinal))
            {
                var parts = command.Substring(20).Split(':');
                if (parts.Length == 2 && (parts[1]=="0" || parts[1]=="1"))
                {
                    bool value = parts[1]=="1";
                    if (parts[0]=="confirm") applyConfirmation.Checked=value;
                    else if (parts[0]=="auto-apply") autoApplyAfterInstall.Checked=value;
                    else if (parts[0]=="auto-remove") autoRemovePreviousVersions.Checked=value;
                }
            }
            else if (command.StartsWith("manage-action:",StringComparison.Ordinal))
            {
                var parts = command.Substring(14).Split(new[] {':'},2);
                if (parts.Length == 2) PerformHubManageAction(parts[0],parts[1]);
            }
            else if(command.StartsWith("manage-prepare:",StringComparison.Ordinal))
            {
                var parts=command.Substring(15).Split(new[]{':'},2);
                if(parts.Length==2)PushHubManageChoices(parts[0],parts[1]);
            }
            else if(command.StartsWith("manage-final:",StringComparison.Ordinal))
                PerformHubManageFinal(command.Substring(13));
            else if (command.StartsWith("record-action:",StringComparison.Ordinal))
            {
                var parts = command.Substring(14).Split(new[] {':'},2);
                if (parts.Length == 2) PerformHubRecordAction(parts[0],parts[1]);
            }
            else if (command == "open-archive")
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('archive')");
            else if (command == "open-youtube")
            {
                try { System.Diagnostics.Process.Start("https://www.youtube.com/@deadsignalcinema"); }
                catch (Exception error) { status.Text = "유튜브 채널 열기 실패 · " + error.Message; }
            }
            else if (command == "open-screenshots")
            {
                try {
                    string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"StarCraft II","Screenshots");
                    if (!Directory.Exists(folder)) AppDialog.Info(this,"스타2에서 스크린샷을 저장하면 이 폴더가 생성됩니다.","스크린샷 폴더");
                    else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute=true });
                }
                catch(Exception error) { Error(error); }
            }
            else if(command=="app-update-ignore"&&availableAppUpdate!=null) {
                declinedAppUpdateVersion=availableAppUpdate.version;
                Data.Write(Path.Combine(hubProfileRoot,"ignored-app-update.json"),declinedAppUpdateVersion);
                updateOffer.Visible=false;PushHubUpdateNotice();
            }
            else if(command=="app-update-install"&&availableAppUpdate!=null&&!busy) {
                await CheckAppUpdate(true,availableAppUpdate);
            }
            else if (command == "open-notifications") OpenHubNotifications();
            else if (command == "open-installed")
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('wol')");
            else if (command == "open-dedicated")
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('dedicated')");
            else if (command == "open-nova")
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('nova')");
            else if (command == "open-settings")
                await hubWeb.ExecuteScriptAsync("window.UiLab.setView('settings')");
            else if (command.StartsWith("settings:",StringComparison.Ordinal))
            {
                if (uiLook == null) return;
                var setting = command.Substring("settings:".Length);
                if (setting == "work-browse:1") uiLook.Value.browseWhileWorking = true;
                else if (setting == "work-browse:0") uiLook.Value.browseWhileWorking = false;
                else if (setting == "motion:1") uiLook.Value.reducedMotion = true;
                else if (setting == "motion:0") uiLook.Value.reducedMotion = false;
                else if (setting == "contrast:1") uiLook.Value.highContrast = true;
                else if (setting == "contrast:0") uiLook.Value.highContrast = false;
                else if (setting == "hide-themes:1") uiLook.Value.hideThemeButtons = true;
                else if (setting == "hide-themes:0") uiLook.Value.hideThemeButtons = false;
                else if (setting == "hide-top:1") uiLook.Value.hideTopSummary = true;
                else if (setting == "hide-top:0") uiLook.Value.hideTopSummary = false;
                else return;
                uiLook.Save();
                UpdateHubWorkState();
            }
            else if (command == "import-zip") { if (!busy) ImportLocal(null); }
            else if (command == "import-drop") ReceiveLiveHubZipDrop(e);
            else if (command.StartsWith("play:",StringComparison.Ordinal)) OpenLiveEntry(command.Substring(5),true);
            else if (command.StartsWith("manage:",StringComparison.Ordinal)) OpenLiveEntry(command.Substring(7),false);
            else if (command.StartsWith("favorites:",StringComparison.Ordinal))
            {
                try
                {
                    var ids = Data.Json.Deserialize<List<string>>(command.Substring(10));
                    if (ids == null || ids.Count > 512) return;
                    var allowed = new HashSet<string>(LiveEntries().Select(c => c.id),StringComparer.OrdinalIgnoreCase);
                    ids = ids.Where(x => !String.IsNullOrWhiteSpace(x) && x.Length <= 128 && allowed.Contains(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    Directory.CreateDirectory(Path.GetDirectoryName(hubFavoritesFile));
                    Data.Write(hubFavoritesFile,ids);
                }
                catch (Exception error) { status.Text = "작전실 즐겨찾기를 저장하지 못했습니다 · " + error.Message; }
            }
            else if (command.StartsWith("hidden-campaigns:",StringComparison.Ordinal))
            {
                try {
                    var ids=Data.Json.Deserialize<List<string>>(command.Substring("hidden-campaigns:".Length));
                    if(ids==null || ids.Count>512)throw new InvalidDataException("숨김 캠페인 목록이 너무 큽니다.");
                    ids=ids.Where(id=>!String.IsNullOrWhiteSpace(id)&&id.Length<=128&&
                        !id.Any(char.IsControl)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    Directory.CreateDirectory(Path.GetDirectoryName(hubHiddenCampaignsFile));
                    Data.Write(hubHiddenCampaignsFile,ids);
                }
                catch(Exception error){ status.Text="숨김 캠페인 목록을 저장하지 못했습니다 · "+error.Message; }
            }
            } catch(Exception error) { if(!hubShuttingDown && !IsDisposed && !Disposing)Error(error); }
        }
        async void ReceiveLiveHubZipDrop(CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                if (busy || distributionView.DownloadInProgress)
                    throw new InvalidOperationException("다른 작업이 진행 중입니다. 완료 후 ZIP을 다시 드롭해 주세요.");
                var files = ZipDropTarget.WebZipFiles(e);
                status.Text = "드롭한 ZIP " + files.Length + "개를 검사하고 있습니다.";
                await ImportDropped(files);
                await PushLiveHubData();
            }
            catch (Exception error) { Error(error); }
        }
    }
}
