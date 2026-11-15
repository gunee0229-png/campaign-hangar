using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    public sealed partial class Hangar : Form
    {
        readonly Library library;
        readonly ExternalCampaigns externalCampaigns;
        readonly ActivityStore activity;
        readonly DedicatedLibrary dedicatedLibrary;
        readonly System.Windows.Forms.Timer activityTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        readonly Dictionary<string, Image> externalCovers = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        bool activityPolling;
        string externalSession, externalProcess;
        double externalPendingUntil;
        readonly string root;
        readonly string preferenceFile, downloadPreferenceFile;
        readonly bool isolated, online;
        readonly TextBox game = new TextBox(), storagePath = new TextBox(), downloadPath = new TextBox();
        string downloadsRoot;
        readonly Label status = new Label();
        readonly UiLookStore uiLook;
        readonly HangarCommandDeck commandDeck;
        readonly Panel modeHeader = new Panel();
        UiCinematicBoot bootOverlay;
        readonly NotificationButton notifications = new NotificationButton();
        NotificationFeed notificationFeed;
        AppRelease appUpdateFixture;
        readonly FlowLayoutPanel navigation = Flow();
        readonly Panel screen = new Panel(), archivePage = new Panel(), installedPage = new Panel(), settingsPage = new Panel(), distributionPage = new Panel();
        readonly ArchiveView archiveView;
        readonly DistributionView distributionView;
        readonly InstalledView installedView;
        readonly ThumbnailStore thumbnails;
        readonly Func<Campaign, Task<Image>> loadThumbnail;
        readonly Func<Campaign, string, Task> openDistribution;
        readonly ZipDropTarget zipDrop;
        readonly TableLayoutPanel settings, pathSettings;
        readonly Button refresh;
        readonly List<string> jobs = new List<string>();
        readonly List<MetalButton> maintenanceButtons = new List<MetalButton>();
        readonly List<Control> conflictingControls = new List<Control>();
        readonly Dictionary<Control, bool> busyControlStates = new Dictionary<Control, bool>();
        readonly Label noticesText = new Label();
        readonly CheckBox applyConfirmation = new CheckBox();
        readonly CheckBox autoApplyAfterInstall = new CheckBox();
        readonly CheckBox autoRemovePreviousVersions = new CheckBox();
        readonly GlassChoiceField bankFolders = new GlassChoiceField();
        readonly Label bankStatus = ContentLabel("", 9.75f);
        MetalButton openBankFolder;
        readonly string applyPreferenceFile;
        List<Campaign> catalogue = new List<Campaign>();
        Campaign pendingSite;
        Task<bool> refreshing;
        bool busy, requesting, deferredCatalogue;
        bool settingsLayoutDirty = true;
        int settingsMeasuredWidth = -1;
        bool checkingAppUpdate, appUpdateInProgress, appUpdateExitApproved;
        AppRelease availableAppUpdate;
        string declinedAppUpdateVersion;
        readonly Panel updateOffer = new Panel();
        readonly Label updateOfferVersion = new Label(), updateOfferNotes = new Label();
        readonly MetalButton updateOfferInstall = new MetalButton(), updateOfferDecline = new MetalButton();
        readonly System.Windows.Forms.Timer appUpdateTimer = new System.Windows.Forms.Timer { Interval = 6 * 60 * 60 * 1000 };
        readonly System.Windows.Forms.Timer siteNewsTimer = new System.Windows.Forms.Timer { Interval = 15 * 60 * 1000 };
        readonly Stack<Panel> pageHistory = new Stack<Panel>();
        Panel currentPage;
        bool goingBack;
        public bool ReloadRequested { get; private set; }

        static FlowLayoutPanel Flow()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent };
        }
        static TableLayoutPanel Table(int columns, int rows)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, BackColor = Color.Transparent };
            if (columns == 1) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (rows == 1) table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            return table;
        }
        public Hangar(string developmentRoot = null, Func<Campaign, Task<Image>> imageLoader = null, bool online = true,
            Func<Campaign, string, Task> distributionOpener = null)
        {
            this.online = online; isolated = developmentRoot != null;
            AutoScaleMode = AutoScaleMode.None; Text = "커캠 격납고" + (AppChannel.IsDevelopment ? " [개발판]" : "") + " · SC2 캠페인 아카이브";
            MinimumSize = new Size(980, 760); Size = new Size(1320, 940); StartPosition = FormStartPosition.CenterScreen;
            BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text; Font = new Font("맑은 고딕", 12.5f); DoubleBuffered = true;
            root = developmentRoot ?? Storage.Resolve(Storage.PreferenceFile); library = new Library(root);
            // Load the visual identity *before* native pages are constructed,
            // so native actions keep their handlers and repaint in the skin.
            string profileRoot = isolated ? root + "-profile" :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppChannel.Profile);
            if (AppChannel.HasThemedUi)
            {
                uiLook = new UiLookStore(Path.Combine(profileRoot, "ui-theme.json"));
                LiveTheme.Select(LiveTheme.Resolve(uiLook.Value.theme));
                BackColor = GlassTheme.Background;
                ForeColor = GlassTheme.Text;
            }
            externalCampaigns = new ExternalCampaigns(root);
            activity = ActivityStore.OpenForStartup(root,
                question => MessageBox.Show(question, "플레이 기록 복구 · 선택 필요",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes,
                message => MessageBox.Show(message, "플레이 기록 복구 결과", MessageBoxButtons.OK, MessageBoxIcon.Information));
            dedicatedLibrary = new DedicatedLibrary(root);
            preferenceFile = isolated ? Path.Combine(root + "-profile", "storage.json") : Storage.PreferenceFile;
            downloadPreferenceFile = isolated ? Path.Combine(root + "-profile", "downloads.json") : Storage.DownloadPreferenceFile;
            downloadsRoot = isolated && !File.Exists(preferenceFile) ? Path.Combine(root, "downloads")
                : Storage.ResolveDownloads(downloadPreferenceFile, preferenceFile);
            // A previous version could have selected Downloads inside the
            // protected game installation.  Keep that configured path intact,
            // but do not prevent the entire launcher from opening when Windows
            // denies access. The user can change it explicitly in Settings.
            string downloadAccessProblem = null;
            try { Directory.CreateDirectory(downloadsRoot); }
            catch (UnauthorizedAccessException error) { downloadAccessProblem = error.Message; }
            catch (IOException error) { downloadAccessProblem = error.Message; }
            if (downloadAccessProblem != null)
                Load += delegate { status.Text = "ZIP 다운로드 폴더 접근 불가 · 경로 설정에서 기존 위치와 권한을 확인하세요. " + downloadAccessProblem; };
            thumbnails = new ThumbnailStore(Path.Combine(root, "thumbnails"));
            var normalThumbnail = imageLoader ?? thumbnails.Get;
            loadThumbnail = item => {
                if (item.kind == "external") return Task.FromResult(ExternalCover(item));
                // A verified ZIP may include its own original thumbnail. Prefer
                // the packaged file over a potentially stale website placeholder.
                if ((item.thumbnail ?? "").StartsWith(@"packages\", StringComparison.OrdinalIgnoreCase))
                {
                    string local = Data.Under(root, item.thumbnail);
                    if (File.Exists(local)) return Task.FromResult(ExternalCover(item));
                }
                if (item.kind == "dedicated" && !String.IsNullOrWhiteSpace(item.id))
                {
                    string cover = Data.Under(root, @"dedicated-artwork\" + item.id + ".png");
                    if (File.Exists(cover)) return Task.FromResult(ExternalCover(new Campaign {
                        thumbnail = @"dedicated-artwork\" + item.id + ".png" }));
                }
                return normalThumbnail(item);
            };
            applyPreferenceFile = Path.Combine(profileRoot, "apply.json");
            string windowPreference = Path.Combine(profileRoot, "window.json");
            Load += delegate { try { WindowSize.Restore(this, windowPreference); } catch (Exception error) { status.Text = "창 크기 복원 실패 · " + error.Message; } };
            archiveView = new ArchiveView(profileRoot, online, DownloadFromArchive, DedicatedClientState, ImportDropped);
            distributionView = new DistributionView(profileRoot, root, RegisterDownloaded,
                delegate { ShowPage(archivePage); }, ImportLocal, () => downloadsRoot,
                (title,cancel) => BeginHubFlow(title,cancel), progress => ReportHubProgress(progress),
                () => FinishHubFlow(), () => CanShowHubWorkScreen());
            openDistribution = distributionOpener ?? OpenRegisteredDistribution;
            installedView = new InstalledView(library, loadThumbnail, ApplyInstalled,
                delegate { ImportLocal(pendingSite); }, PickVersion, LinkInstalled, UpdateInstalled, RemoveInstalled, OpenSiteInformation,
                () => ExternalCampaigns.VisibleForChannel(externalCampaigns.All(), AppChannel.IsDevelopment),
                delegate { EditExternal(null); }, EditExternal, ShowPlayRecords, activity.Summary,
                async delegate { await RestoreBaseCampaign(); }, async item => { await LaunchExternal(item, true); },
                (item, episode) => item.kind == "dedicated" ? PlayDedicated(item, episode) : LaunchExternal(item, false, episode),
                dedicatedLibrary.IsInstalled, dedicatedLibrary.NeedsUpdate, dedicatedLibrary.Version,
                item => notificationFeed != null && notificationFeed.IsNewCampaign(item.id), MarkCampaignViewed,
                async item => { await InstallDedicated(item); }, dedicatedLibrary.VoiceLabel, OpenCreator, dedicatedLibrary.NeedsRepair,
                id => activity.Snapshot(id).seconds);
            if (AppChannel.HasThemedUi)
                installedView.SetOptimizedScrolling(LiveTheme.Current == LiveTheme.Classic);
            archivePage.Controls.Add(archiveView); distributionPage.Controls.Add(distributionView); installedPage.Controls.Add(installedView);
            if (AppChannel.HasThemedUi) InitializeLiveHub(profileRoot);
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets"), icon = Path.Combine(assets, "archive.ico");
            if (File.Exists(icon)) Icon = new Icon(icon);
            var layout = Table(1, AppChannel.HasThemedUi ? 3 : 2);
            layout.Padding = new Padding(30, 0, 30, 14);
            int navigationRow = AppChannel.HasThemedUi ? 1 : 0;
            int contentRow = AppChannel.HasThemedUi ? 2 : 1;
            liveShellLayout = layout;
            liveNavRow = navigationRow;
            if (AppChannel.HasThemedUi) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(layout);
            if (AppChannel.HasThemedUi)
            {
                commandDeck = new HangarCommandDeck(ChangeLiveTheme,
                    delegate { ShowPage(homePage); },
                    delegate { ShowPage(archivePage); },
                    delegate { ShowPage(installedPage); },
                    delegate { ShowPage(installedPage); installedView.SelectSlot("nco"); },
                    delegate { if (!busy && !distributionView.DownloadInProgress) ImportLocal(null);
                        else status.Text = "진행 중인 작업을 마친 뒤 ZIP 가져오기를 사용해 주세요."; },
                    delegate { StartLiveIntro(true); });
                modeHeader.Dock = DockStyle.Fill;
                commandDeck.Dock = DockStyle.Fill;
                modeHeader.Controls.Add(commandDeck);
                layout.Controls.Add(modeHeader, 0, 0);
                commandDeck.RefreshSkin();
            }
            navigation.Padding = new Padding(0, 14, 0, 0);
            navigation.Controls.Add(Button("아카이브", delegate { ShowPage(archivePage); }));
            navigation.Controls.Add(Button("내 캠페인", delegate { ShowPage(installedPage); }));
            navigation.Controls.Add(Button("설정", delegate { ShowPage(settingsPage); }));
            foreach (MetalButton button in navigation.Controls) button.Appearance = ButtonAppearance.Navigation;
            var header = Table(4, 1); header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 410)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64));
            bool fittingNavigation = false;
            Action fitNavigation = delegate
            {
                    if (fittingNavigation || (AppChannel.HasThemedUi && currentPage == homePage)) return;
                fittingNavigation = true;
                try
                {
                    int width = navigation.Padding.Horizontal, height = 0;
                    foreach (Control button in navigation.Controls)
                    {
                        button.Size = button.GetPreferredSize(Size.Empty);
                        width += button.Width + button.Margin.Horizontal;
                        height = Math.Max(height, button.Height + button.Margin.Vertical);
                    }
                    if (header.ColumnStyles[0].Width != width) header.ColumnStyles[0].Width = width;
                    float rowHeight = Math.Max(74, height + navigation.Padding.Vertical + 8);
                    if (layout.RowStyles[navigationRow].Height != rowHeight)
                        layout.RowStyles[navigationRow].Height = rowHeight;
                }
                finally { fittingNavigation = false; }
            };
            header.Layout += delegate { fitNavigation(); };
            foreach (Control button in navigation.Controls)
            {
                button.FontChanged += delegate { fitNavigation(); }; button.TextChanged += delegate { fitNavigation(); };
                button.HandleCreated += delegate { fitNavigation(); };
            }
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleRight; status.Font = new Font(Font.FontFamily, 10);
            status.ForeColor = GlassTheme.Muted; status.AutoEllipsis = true;
            header.Controls.Add(navigation, 0, 0); header.Controls.Add(status, 1, 0);
            layout.Controls.Add(header, 0, navigationRow);
            var youtube = new MetalButton { Text = "", AccessibleName = "DEAD SIGNAL CINEMA 유튜브 채널 열기",
                Size = new Size(52, 48), Anchor = AnchorStyles.Right, Margin = new Padding(6, 0, 0, 0), Cursor = Cursors.Hand };
            var youtubeImage = new Bitmap(30, 30);
            using (var graphics = Graphics.FromImage(youtubeImage))
            {
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var bounds = new Rectangle(0, 4, 30, 22);
                using (var shape = GlassTheme.Round(bounds, 6))
                using (var red = new SolidBrush(Color.FromArgb(220, 35, 35))) graphics.FillPath(red, shape);
                using (var white = new SolidBrush(Color.White))
                    graphics.FillPolygon(white, new[] { new Point(bounds.X + 12, bounds.Y + 6), new Point(bounds.X + 12, bounds.Y + 16), new Point(bounds.X + 21, bounds.Y + 11) });
            }
            youtube.LeadingIcon = youtubeImage;
            youtube.Disposed += delegate { youtubeImage.Dispose(); };
            youtube.Click += delegate { Process.Start("https://www.youtube.com/@deadsignalcinema"); };
            header.Controls.Add(youtube, 2, 0);
            notifications.Anchor = AnchorStyles.Right; notifications.Margin = new Padding(6, 0, 0, 0);
            notifications.Click += async delegate { await ShowNotifications(); };
            header.Controls.Add(notifications, 3, 0);
            screen.Dock = DockStyle.Fill; layout.Controls.Add(screen, 0, contentRow);
            foreach (Panel page in Pages()) { page.Dock = DockStyle.Fill; page.BackColor = GlassTheme.Background; screen.Controls.Add(page); }

            settings = Table(1, AppChannel.HasThemedUi ? 6 : 5);
            settings.Padding = new Padding(0, 16, 0, 12);
            settings.AutoSize = false;
            for (int i = 0; i < settings.RowCount; i++) settings.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var settingsTitle = SurfaceTable(1, 2);
            settingsTitle.Margin = new Padding(0, 0, 0, 20);
            settingsTitle.Controls.Add(ContentLabel("설정", 17.25f, true), 0, 0);
            settingsTitle.Controls.Add(ContentLabel("경로, 캠페인 적용 방식, 앱 관리 기능을 설정합니다.", 9.75f), 0, 1);
            settings.Controls.Add(settingsTitle, 0, 0);
            var pathsGroup = Group("경로 설정", "게임 위치와 파일을 보관하는 위치를 한곳에서 확인합니다.");
            pathSettings = SurfaceTable(1, 4);
            pathSettings.Margin = new Padding(0, 12, 0, 0);
            pathsGroup.Controls.Add(pathSettings, 0, 2); settings.Controls.Add(pathsGroup.Parent, 0, 1);
            var paths = PathSettingRow(pathSettings, 0, "게임 경로", "스타크래프트 II");
            game.ReadOnly = true; game.BackColor = GlassTheme.Field; game.ForeColor = ForeColor;
            game.Text = Storage.ResolveGame(preferenceFile, library.Current().gameRoot) ?? "게임 경로 미설정";
            var gameField = new GlassSearchField(game) { Dock = DockStyle.Fill, MinimumSize = new Size(0, 52), Margin = new Padding(0, 0, 12, 0) };
            var pickGame = SettingButton("경로 선택", async delegate { await PickGame(); });
            conflictingControls.Add(pickGame);
            paths.Controls.Add(PathFieldLine(gameField, pickGame), 0, 0);
            paths.Controls.Add(PathHint("StarCraft II.exe가 있는 폴더를 선택하세요."), 0, 1);
            var restoreGame = SettingButton("기본 캠페인 되돌리기", async delegate {
                if (!busy) await RestoreBaseCampaign();
            });
            conflictingControls.Add(restoreGame);
            var maintenance = Group("업데이트 · 기록 · 복구", "필요할 때 실행하는 관리 기능입니다.");
            var tools = SurfaceTable(2, 5); tools.Margin = new Padding(0, 10, 0, 0);
            tools.ColumnStyles.Clear(); tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            refresh = SettingButton("사이트 목록 새로고침", async delegate { await RefreshSite(); }); tools.Controls.Add(refresh, 0, 0);
            tools.Controls.Add(SettingButton("앱 업데이트 확인", async delegate { await CheckAppUpdate(true); }), 0, 1);
            tools.Controls.Add(SettingButton("설치·다운로드 기록", delegate { AppDialog.Info(this, jobs.Count == 0 ? "이번 실행의 기록이 없습니다." : String.Join("\n", jobs), "이번 실행의 기록"); }), 0, 2);
            tools.Controls.Add(SettingButton("실패한 전환 복구", async delegate { await Recover(); }), 0, 3);
            tools.Controls.Add(restoreGame, 0, 4);
            string[] descriptions = { "사이트의 신규 캠페인과 버전 정보를 다시 확인합니다.", "현재 앱 v" + AppUpdate.Current + " · 새 버전을 확인하고 동의 후 설치합니다.", "이번 실행에서 진행한 다운로드·설치 결과를 봅니다.", "중단된 파일 전환을 이전 적용 상태로 복구합니다.", "격납고가 적용한 파일만 적용 전 백업으로 되돌립니다. ZIP·뱅크·세이브는 유지합니다." };
            for (int i = 0; i < descriptions.Length; i++)
            {
                var description = ContentLabel(descriptions[i], 9.75f);
                description.Anchor = AnchorStyles.Left | AnchorStyles.Right; description.TextAlign = ContentAlignment.MiddleLeft;
                description.Margin = new Padding(0, i == 0 ? 0 : 12, 0, i == descriptions.Length - 1 ? 0 : 12);
                tools.Controls.Add(description, 1, i);
                var button = tools.GetControlFromPosition(0, i);
                button.AutoSize = false; button.MinimumSize = new Size(250, 50); button.Anchor = AnchorStyles.Left;
                maintenanceButtons.Add((MetalButton)button);
                ((Button)button).TextAlign = ContentAlignment.MiddleLeft;
                button.Margin = new Padding(0, i == 0 ? 0 : 12, 24, i == descriptions.Length - 1 ? 0 : 12);
            }
            bool sizingTools = false;
            Action sizeTools = delegate {
                if (sizingTools) return;
                sizingTools = true;
                try
                {
                    var sizes = maintenanceButtons.Select(button => button.GetPreferredSize(Size.Empty)).ToList();
                    var largest = new Size(Math.Max(250, sizes.Max(s => s.Width)), Math.Max(50, sizes.Max(s => s.Height)));
                    foreach (var button in maintenanceButtons) button.Size = largest;
                }
                finally { sizingTools = false; }
            };
            foreach (var button in maintenanceButtons)
            {
                button.FontChanged += delegate { sizeTools(); }; button.HandleCreated += delegate { sizeTools(); };
                button.TextChanged += delegate { sizeTools(); };
            }
            conflictingControls.AddRange(new Control[] { refresh, tools.GetControlFromPosition(0, 1), tools.GetControlFromPosition(0, 3) });
            sizeTools();
            tools.CellPaint += delegate(object sender, TableLayoutCellPaintEventArgs e)
            {
                if (e.Row > 0) using (var pen = new Pen(Color.FromArgb(23, 255, 255, 255)))
                    e.Graphics.DrawLine(pen, e.CellBounds.Left, e.CellBounds.Top, e.CellBounds.Right, e.CellBounds.Top);
            };
            maintenance.Controls.Add(tools, 0, 2); settings.Controls.Add(maintenance.Parent, 0, 3);
            var storage = PathSettingRow(pathSettings, 2, "캠페인 보관소", "파일 백업 위치");
            storagePath.Text = root; storagePath.ReadOnly = true; storagePath.AccessibleName = "현재 캠페인 저장소 경로"; storagePath.BackColor = GlassTheme.Field; storagePath.ForeColor = ForeColor;
            var storageField = new GlassSearchField(storagePath) { Dock = DockStyle.Fill, MinimumSize = new Size(0, 52), Margin = new Padding(0, 0, 12, 0) };
            var changeStorage = SettingButton("보관소 경로 변경", async delegate { await ChangeStorage(); });
            conflictingControls.Add(changeStorage);
            var openStorage = SettingButton("폴더 열기", delegate { Process.Start("explorer.exe", "\"" + root + "\""); });
            storage.Controls.Add(PathFieldLine(storageField, changeStorage, openStorage), 0, 0);
            storage.Controls.Add(PathHint("StarCraft II 게임 폴더 안이나 그 하위 폴더로 지정하지 마세요.", true), 0, 1);
            var downloadGroup = PathSettingRow(pathSettings, 1, "다운로드 경로", "받은 ZIP 파일");
            downloadPath.Text = downloadsRoot; downloadPath.ReadOnly = true; downloadPath.AccessibleName = "ZIP 다운로드 경로";
            downloadPath.BackColor = GlassTheme.Field; downloadPath.ForeColor = ForeColor;
            var downloadField = new GlassSearchField(downloadPath) { Dock = DockStyle.Fill, MinimumSize = new Size(0, 52), Margin = new Padding(0, 0, 12, 0) };
            var changeDownloads = SettingButton("다운로드 경로 변경", delegate { ChangeDownloads(); });
            conflictingControls.Add(changeDownloads);
            var openDownloads = SettingButton("폴더 열기", delegate { Directory.CreateDirectory(downloadsRoot); Process.Start("explorer.exe", "\"" + downloadsRoot + "\""); });
            downloadGroup.Controls.Add(PathFieldLine(downloadField, changeDownloads, openDownloads), 0, 0);
            downloadGroup.Controls.Add(PathHint("ZIP만 저장합니다. 경로를 바꿔도 캠페인 보관소와 백업은 이동하지 않습니다."), 0, 1);
            var applyOptions = Group("캠페인 적용", "설치 후 적용 여부와 확인창, 이전 버전 정리를 설정합니다.");
            applyConfirmation.Text = "캠페인 적용 전 확인창 표시";
            applyConfirmation.ForeColor = GlassTheme.Text; applyConfirmation.BackColor = Color.Transparent;
            applyConfirmation.AutoSize = true; applyConfirmation.Dock = DockStyle.Fill;
            applyConfirmation.Margin = new Padding(0, 16, 0, 0);
            var applyPreference = File.Exists(applyPreferenceFile) ? Data.Read<ApplyPreference>(applyPreferenceFile) : new ApplyPreference();
            applyConfirmation.Checked = applyPreference.showConfirmation;
            autoApplyAfterInstall.Text = "설치 후 바로 적용";
            autoApplyAfterInstall.ForeColor = GlassTheme.Text;
            autoApplyAfterInstall.BackColor = Color.Transparent;
            autoApplyAfterInstall.AutoSize = true; autoApplyAfterInstall.Dock = DockStyle.Fill;
            autoApplyAfterInstall.Margin = new Padding(0, 16, 0, 0);
            autoApplyAfterInstall.Checked = applyPreference.autoApplyAfterInstall;
            autoRemovePreviousVersions.Text = "새 버전 적용 후 이전 버전 자동 삭제";
            autoRemovePreviousVersions.ForeColor = GlassTheme.Text; autoRemovePreviousVersions.BackColor = Color.Transparent;
            autoRemovePreviousVersions.AutoSize = true; autoRemovePreviousVersions.Dock = DockStyle.Fill;
            autoRemovePreviousVersions.Margin = new Padding(0, 16, 0, 0);
            autoRemovePreviousVersions.Checked = applyPreference.autoRemovePreviousVersions;
            conflictingControls.AddRange(new Control[] { applyConfirmation, autoApplyAfterInstall, autoRemovePreviousVersions });
            bool savingApplyOption = false;
            EventHandler saveApplyOption = delegate(object sender, EventArgs e) {
                if (savingApplyOption) return;
                try { Data.Write(applyPreferenceFile, new ApplyPreference {
                    showConfirmation = applyConfirmation.Checked,
                    autoApplyAfterInstall = autoApplyAfterInstall.Checked,
                    autoRemovePreviousVersions = autoRemovePreviousVersions.Checked }); }
                catch (Exception error)
                {
                    var changed = (CheckBox)sender;
                    savingApplyOption = true; changed.Checked = !changed.Checked; savingApplyOption = false;
                    Error(error);
                }
            };
            applyConfirmation.CheckedChanged += saveApplyOption;
            autoApplyAfterInstall.CheckedChanged += saveApplyOption;
            autoRemovePreviousVersions.CheckedChanged += saveApplyOption;
            applyOptions.RowCount = 8;
            for (int i = 0; i < 5; i++) applyOptions.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            applyOptions.Controls.Add(autoApplyAfterInstall, 0, 2);
            var autoApplyDescription = ContentLabel("ZIP 가져오기·아카이브 다운로드 후 등록에 성공하면 캠페인 적용까지 진행합니다. 게임이 실행 중이면 안전을 위해 적용하지 않습니다.", 9.75f);
            autoApplyDescription.Margin = new Padding(28, 6, 0, 0);
            applyOptions.Controls.Add(autoApplyDescription, 0, 3);
            applyOptions.Controls.Add(applyConfirmation, 0, 4);
            var confirmationDescription = ContentLabel("체크하면 캠페인을 종료하고 게임 메뉴로 나왔는지 확인합니다. 해제하면 경고창 없이 바로 적용합니다.", 9.75f);
            confirmationDescription.Margin = new Padding(28, 6, 0, 0); applyOptions.Controls.Add(confirmationDescription, 0, 5);
            applyOptions.Controls.Add(autoRemovePreviousVersions, 0, 6);
            var cleanupDescription = ContentLabel("체크하면 적용 성공 후 같은 캠페인의 이전 보관 버전만 휴지통으로 보냅니다.\n최신·현재 적용 버전, 다운로드 ZIP·뱅크·세이브는 유지합니다.", 9.75f);
            cleanupDescription.Margin = new Padding(28, 8, 0, 0);
            applyOptions.Controls.Add(cleanupDescription, 0, 7);
            settings.Controls.Add(applyOptions.Parent, 0, 2);
            var banks = PathSettingRow(pathSettings, 3, "뱅크 폴더", "");
            bankFolders.AccessibleName = "열 뱅크 폴더";
            bankFolders.Dock = DockStyle.Fill; bankFolders.MinimumSize = new Size(0, 52);
            bankFolders.Margin = new Padding(0, 0, 12, 0);
            bankFolders.DropDownWidth = 850; bankFolders.Enabled = false;
            var findBankFolder = SettingButton("폴더 찾기", delegate {
                try { RefreshBankFolders(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)); }
                catch (Exception error) { bankStatus.Text = "뱅크 폴더를 확인하지 못했습니다. 접근 권한을 확인하세요."; Error(error); }
            });
            openBankFolder = SettingButton("뱅크 폴더 열기", delegate {
                try {
                    string path = bankFolders.SelectedItem as string;
                    if (path == null || !Directory.Exists(path))
                        throw new IOException("선택한 뱅크 폴더가 없습니다. 폴더 찾기를 다시 눌러 주세요.");
                    Process.Start("explorer.exe", "\"" + path + "\"");
                }
                catch (Exception error) { Error(error); }
            });
            openBankFolder.Enabled = false;
            banks.Controls.Add(PathFieldLine(bankFolders, findBankFolder, openBankFolder), 0, 0);
            bankStatus.Margin = new Padding(0, 8, 0, 0); banks.Controls.Add(bankStatus, 0, 3);
            bankStatus.Visible = false;
            bankStatus.TextChanged += delegate { bankStatus.Visible = !String.IsNullOrWhiteSpace(bankStatus.Text); };
            if (AppChannel.HasThemedUi && uiLook != null)
            {
                var looks = Group("게임 UI 디자인", "테마와 작업 진행 표시 방식을 설정합니다.");
                var lookButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill,
                    BackColor = Color.Transparent, WrapContents = true, Margin = new Padding(0, 10, 0, 0) };
                foreach (var palette in LiveTheme.Modes)
                {
                    var choice = palette;
                    var button = SettingButton(choice == LiveTheme.Classic ?
                        "최적화 모드" : choice.Name + " UI", delegate { ChangeLiveTheme(choice); });
                    button.MinimumSize = new Size(128, 49);
                    button.Margin = new Padding(0, 0, 10, 7);
                    button.AutoSize = true;
                    lookButtons.Controls.Add(button);
                }
                var workBrowseCheck = new CheckBox { AutoSize = true, Checked = uiLook.Value.browseWhileWorking,
                    Text = "작업 진행 박스만 표시 (해제: 전체 화면)", ForeColor = GlassTheme.Text,
                    BackColor = Color.Transparent, Margin = new Padding(15, 12, 0, 0),
                    Font = new Font("맑은 고딕", 10, FontStyle.Bold) };
                workBrowseCheck.CheckedChanged += async delegate {
                    uiLook.Value.browseWhileWorking = workBrowseCheck.Checked;
                    uiLook.Save();
                    await PushLiveHubPreferences();
                    UpdateHubWorkState();
                };
                lookButtons.Controls.Add(workBrowseCheck);
                var replay = SettingButton("시작 연출 다시 보기", delegate { StartLiveIntro(true); });
                replay.Margin = new Padding(0, 0, 10, 7);
                lookButtons.Controls.Add(replay);
                looks.Controls.Add(lookButtons, 0, 2);
                settings.Controls.Add(looks.Parent, 0, 4);
            }
            noticesText.Text = "커캠 격납고 v" + AppUpdate.Current + " · " + AppChannel.Label + "\nCCM © 2022 7thAce / GiantGrantGames\n오픈소스 고지: THIRD-PARTY-NOTICES.txt · WebView2-LICENSE.txt";
            noticesText.Font = new Font(Font.FontFamily, 9, FontStyle.Regular);
            noticesText.ForeColor = GlassTheme.Muted; noticesText.BackColor = Color.Transparent;
            noticesText.AutoSize = true; noticesText.Dock = DockStyle.Top; noticesText.TextAlign = ContentAlignment.MiddleRight;
            noticesText.Margin = new Padding(0, 8, 0, 0);
            settings.Controls.Add(noticesText, 0, AppChannel.HasThemedUi ? 5 : 4);
            settingsPage.Controls.Add(new DarkScrollPanel(settings, compositeChildren: true) { Dock = DockStyle.Fill });
            BuildUpdateOffer();
            foreach (Control control in Descendants(settings).Concat(new Control[] { settings }))
            {
                control.TextChanged += delegate { settingsLayoutDirty = true; };
                control.FontChanged += delegate { settingsLayoutDirty = true; };
            }
            string cached = Path.Combine(root, "catalogue.js");
            if (!File.Exists(cached)) cached = Path.Combine(assets, "catalogue.js");
            try { if (File.Exists(cached)) catalogue = Data.ParseCatalogue(File.ReadAllText(cached)); }
            catch (Exception error) { status.Text = "저장된 목록 확인 필요 · " + error.Message; }
            try {
                notificationFeed = new NotificationFeed(Path.Combine(profileRoot, "notifications.json"), catalogue);
                notifications.UnreadCount = notificationFeed.UnreadCount;
            }
            catch (Exception error) { notifications.Enabled = false; status.Text = "알림 기록 확인 필요 · " + error.Message; }
            AutoLinkExisting();
            ShowPage(AppChannel.HasThemedUi && LiveTheme.Current != LiveTheme.Classic ?
                homePage : archivePage);
            Shown += delegate
            {
                if (AppChannel.HasThemedUi && LiveTheme.Current != LiveTheme.Classic)
                    BeginInvoke(new Action(EnsureLiveHub));
                else BeginInvoke(new Action(EnterArchive));
            };
            if (online && !isolated)
            {
                Shown += async delegate { appUpdateTimer.Start(); siteNewsTimer.Start(); await CheckAppUpdate(false); };
                appUpdateTimer.Tick += async delegate { await CheckAppUpdate(false); };
                siteNewsTimer.Tick += async delegate { if (!busy && !appUpdateInProgress) await RefreshSite(); };
            }
            Disposed += delegate { appUpdateTimer.Dispose(); siteNewsTimer.Dispose(); };
            if (!isolated)
            {
                Shown += async delegate { activityTimer.Start(); await PollActivity(); };
                activityTimer.Tick += async delegate { await PollActivity(); };
            }
            Disposed += delegate { activityTimer.Dispose(); activity.Flush(); foreach (var image in externalCovers.Values) image.Dispose(); };
            zipDrop = new ZipDropTarget(installedView, delegate {
                return (currentPage == installedPage || currentPage == archivePage ||
                    currentPage == settingsPage || (AppChannel.HasThemedUi && currentPage == homePage)) &&
                    !busy && !distributionView.DownloadInProgress;
            }, ImportDropped);
            // Use the existing checked ZIP import, including the optional
            // post-install Apply flow, from every normal launcher page.
            zipDrop.AddSurface(archivePage);
            zipDrop.AddSurface(settingsPage);
            if (AppChannel.HasThemedUi) zipDrop.AddSurface(homePage);
            zipDrop.AddSurface(this, false); zipDrop.AddSurface(navigation); zipDrop.AddSurface(status);
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                // Keep the window alive while downloading/verifying an update,
                // but let it exit after the separate updater has acknowledged
                // readiness. Otherwise its 120s parent wait never completes.
                if (!appUpdateExitApproved && (busy || distributionView.DownloadInProgress))
                { e.Cancel = true; status.Text = "작업을 마치거나 배포처의 다운로드 취소를 눌러 주세요."; }
            };
            FormClosed += delegate { WindowSize.Save(this, windowPreference); thumbnails.Dispose(); };
            var backMouse = new BackMouseFilter(this, GoBack);
            Application.AddMessageFilter(backMouse);
            FormClosed += delegate { Application.RemoveMessageFilter(backMouse); };
            if (AppChannel.HasThemedUi)
            {
                LiveTheme.RecolorTree(this, LiveTheme.Classic, LiveTheme.Current);
                // The theme option now controls real work/loading animation;
                // do not play a timer-based fake boot when no work is underway.
            }
        }
        IEnumerable<Panel> Pages()
        {
            return AppChannel.HasThemedUi
                ? new[] { homePage, archivePage, installedPage, settingsPage, distributionPage }
                : new[] { archivePage, installedPage, settingsPage, distributionPage };
        }
        Label Label(string text, float size)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, ForeColor = GlassTheme.Muted, BackColor = Color.Transparent, AutoEllipsis = false,
                Font = new Font(Font.FontFamily, size, size > 12.5f ? FontStyle.Bold : FontStyle.Regular), TextAlign = ContentAlignment.MiddleLeft };
        }
        Button Button(string text, EventHandler handler)
        {
            var button = new MetalButton { Text = text, AutoSize = true, Height = 44, MinimumSize = new Size(100, 44), Margin = new Padding(3),
                Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold) };
            button.Click += handler; return button;
        }
        void RefreshBankFolders(string documents)
        {
            string previous = bankFolders.SelectedItem as string;
            bankFolders.Items.Clear(); bankFolders.Enabled = false; openBankFolder.Enabled = false;
            string[] paths = Storage.BankDirectories(documents);
            bankFolders.Items.AddRange(paths);
            if (paths.Length > 0)
            {
                bankFolders.SelectedIndex = Math.Max(0, Array.IndexOf(paths, previous));
                bankFolders.Enabled = true; openBankFolder.Enabled = true;
            }
            bankStatus.Text = paths.Length == 0
                ? "생성된 뱅크 폴더가 없습니다. 게임에서 뱅크가 저장된 뒤 다시 찾아 주세요."
                : paths.Length + "개 폴더를 찾았습니다. 계정·프로필 경로를 선택한 뒤 여세요.";
        }
        Image ExternalCover(Campaign item)
        {
            string key = item.thumbnail ?? "";
            Image image;
            if (externalCovers.TryGetValue(key, out image)) return image;
            if (key.Length > 0)
            {
                string path = Path.IsPathRooted(key) ? key : Data.Under(root, key);
                using (var original = Image.FromFile(path)) image = new Bitmap(original);
            }
            else
            {
                var bitmap = new Bitmap(960, 540);
                using (var graphics = Graphics.FromImage(bitmap))
                using (var icon = ExternalCampaigns.MapIcon())
                {
                    graphics.Clear(Color.FromArgb(15, 21, 33));
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(icon, new Rectangle(380, 150, 200, 200));
                    TextRenderer.DrawText(graphics, "전용 캠페인", Font, new Rectangle(100, 380, 760, 60),
                        GlassTheme.Text, TextFormatFlags.HorizontalCenter);
                }
                image = bitmap;
            }
            externalCovers.Add(key, image); return image;
        }
        void EditExternal(Campaign item) { EditExternal(item, null); }
        void EditExternal(Campaign item, string suggestedName)
        {
            if (busy) return;
            try
            {
                using (var dialog = new ExternalCampaignDialog(item, game.Text, suggestedName))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        var saved = externalCampaigns.Save(item == null ? null : item.id, dialog.CampaignName,
                            dialog.LauncherMap, dialog.Cover, game.Text, dialog.InstallationMode, dialog.LaunchMode, dialog.LauncherExecutable, dialog.EpisodeMaps);
                        installedView.Reveal(catalogue, saved);
                        status.Text = "실행 바로가기 연결 완료 · 다운로드·설치나 게임 파일 변경은 하지 않았습니다.";
                    }
            }
            catch (Exception error) { Error(error); }
        }
        void ShowPlayRecords(Campaign item)
        {
            if (busy) return;
            using (var dialog = new RecordsDialog(activity, item,
                isolated ? Path.Combine(root, "fixture-documents") : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                library.CatalogueId(item.id) == "revolite"))
                dialog.ShowDialog(this);
            installedView.UpdateRecordSummary();
        }
        async Task PollActivity()
        {
            if (busy || activityPolling || IsDisposed || String.IsNullOrWhiteSpace(game.Text)) return;
            activityPolling = true;
            string gameRoot = game.Text;
            try
            {
                string process = await Task.Run(() => ExternalCampaigns.GameProcess(gameRoot));
                if (IsDisposed || busy) return;
                string observedBank = externalSession ?? library.Current().active;
                if (externalSession != null)
                {
                    if (process != null && externalProcess == null) externalProcess = process;
                    else if (externalProcess != null && externalProcess != process) { externalSession = null; externalProcess = null; }
                    else if (externalProcess == null && (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency >= externalPendingUntil)
                    { externalSession = null; status.Text = "게임 실행을 2분 동안 확인하지 못해 기록 대기를 종료했습니다. 설치 자료와 시작 맵을 확인해 주세요."; }
                }
                string id = process == null ? null : externalSession ?? library.Current().active;
                double now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
                await Task.Run(() => {
                    activity.ObserveTime(id, process, now);
                    if (observedBank != null) activity.PollBank(observedBank);
                });
                if (!IsDisposed) {
                    installedView.UpdateRecordSummary();
                    if(hubReady&&hubWeb!=null&&hubWeb.CoreWebView2!=null)
                        await hubWeb.ExecuteScriptAsync("window.UiLab.setActivityTotals("+
                            Data.Json.Serialize(new {cleared=activity.ClearedCampaigns,seconds=activity.TotalSeconds})+")");
                }
            }
            catch (Exception error)
            {
                activity.ObserveTime(null, null, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                if (!IsDisposed) status.Text = "플레이 기록 확인 필요 · " + error.Message;
            }
            finally { activityPolling = false; }
        }
        async Task<bool> RestoreBaseCampaign()
        {
            if (busy || distributionView.DownloadInProgress) return false;
            try
            {
                ExternalCampaigns.RequireGameClosed(game.Text);
                if (library.Current().active == null)
                {
                    library.RestoreBase();
                    externalSession = null; externalProcess = null;
                    status.Text = "되돌릴 격납고 적용이 없습니다. 외부 런처가 바꾼 파일은 복원하지 않습니다."; return true;
                }
                if (!AppDialog.Confirm(this, "격납고가 적용한 파일을 적용 전 백업 상태로 되돌립니다.\n순정 파일을 새로 설치하는 기능은 아닙니다.\n보관 모드·ZIP·뱅크·세이브·플레이 기록은 유지합니다.\n외부 런처·CCM이 바꾼 미등록 파일은 복원 대상이 아닙니다.",
                    "기본 캠페인 되돌리기", "기본 캠페인 되돌리기")) return false;
                SetBusy(true);
                activity.ObserveTime(null, null, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                await RunWork("기본 캠페인 되돌리기", progress => { library.RestoreBase(progress); return true; });
                externalSession = null; externalProcess = null; installedView.Reload(catalogue);
                status.Text = "기본 캠페인 되돌리기 완료 · 적용 전 백업 상태로 복원했습니다. 외부 런처 변경은 제외입니다."; return true;
            }
            catch (Exception error) { Error(error); return false; }
            finally { SetBusy(false); }
        }
        Task<bool> LaunchExternal(Campaign item, bool openAuthorLauncher = false, int episode = 0)
        {
            if (busy || distributionView.DownloadInProgress) return Task.FromResult(false);
            try
            {
                bool author = openAuthorLauncher || ExternalCampaigns.LaunchMode(item) == "author-launcher";
                string directMission = null;
                if (!openAuthorLauncher && episode == 0 && OdysseyMissionDialog.Supports(item, game.Text))
                {
                    using (var chooser = new OdysseyMissionDialog(game.Text))
                    {
                        if (chooser.ShowDialog(this) != DialogResult.OK) return Task.FromResult(false);
                        directMission = chooser.SelectedMap;
                    }
                }
                // A direct mission bypasses the SC2 engine's potentially
                // failing GameSetNextMap handoff. It never patches the map.
                var start = directMission != null
                    ? ExternalCampaigns.LaunchInfo(new Campaign {
                        kind = "external", launchMode = "map", launcherMap = directMission }, game.Text)
                    : openAuthorLauncher ? ExternalCampaigns.AuthorLauncherInfo(item) :
                        ExternalCampaigns.LaunchInfo(item, game.Text, episode);
                ExternalCampaigns.RequireGameClosed(game.Text);
                activity.ObserveTime(null, null, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                using (var process = Process.Start(start))
                    if (process == null) throw new IOException("실행 요청을 시작하지 못했습니다.");
                if (author)
                {
                    externalSession = null; externalProcess = null;
                    status.Text = "제작자 런처 열기 요청 완료 · 설치·업데이트·게임 실행은 해당 런처에서 진행하세요. 런처 경유 시간은 자동 기록하지 않습니다.";
                    return Task.FromResult(true);
                }
                externalSession = item.id; externalProcess = null;
                externalPendingUntil = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency + 120;
                status.Text = "전용 캠페인 실행 요청 완료 · " + item.name +
                    (directMission == null ? episode == 0 ? "" : " · 에피소드 " + episode :
                        " · 임무 직접 실행: " + Path.GetFileNameWithoutExtension(directMission)) +
                    " · 게임 실행 확인 후 시간을 기록합니다.";
                return Task.FromResult(true);
            }
            catch (Exception error) { externalSession = null; externalProcess = null; Error(error); return Task.FromResult(false); }
        }
        internal Task<bool> VerifyExternalLaunch(Campaign item, string gameRoot, bool openAuthorLauncher)
        {
            if (!isolated) throw new InvalidOperationException("External launch checks require isolated storage.");
            game.Text = gameRoot;
            return LaunchExternal(item, openAuthorLauncher);
        }
        TableLayoutPanel Group(string title, string description)
        {
            var panel = new MetalPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(24), Margin = new Padding(0, 0, 0, 16) };
            var table = SurfaceTable(1, 3); panel.Controls.Add(table);
            table.SizeChanged += delegate {
                panel.MinimumSize = new Size(0, table.Height + panel.Padding.Vertical);
            };
            Color accent = title == "경로 설정" ? Color.FromArgb(86, 169, 248) :
                title == "캠페인 적용" ? Color.FromArgb(34, 221, 203) : Color.FromArgb(246, 187, 112);
            var bar = new Panel { Dock = DockStyle.Top, Height = 100, Margin = new Padding(0, 0, 0, 12),
                BackColor = Color.Transparent, AccessibleName = title + " 구역" };
            bar.Paint += delegate(object sender, PaintEventArgs e) {
                if (bar.Width < 4 || bar.Height < 4) return;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var rect = new Rectangle(1, 1, bar.Width - 3, bar.Height - 3);
                using (var path = GlassTheme.Round(rect, 12))
                using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(rect,
                    Color.FromArgb(48, 63, 89), Color.FromArgb(23, 32, 50), 0f))
                using (var edge = new Pen(Color.FromArgb(105, accent)))
                using (var stripe = new SolidBrush(accent))
                {
                    e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(edge, path);
                    var state = e.Graphics.Save(); e.Graphics.SetClip(path);
                    e.Graphics.FillRectangle(stripe, rect.Left, rect.Top, 5, rect.Height);
                    e.Graphics.Restore(state);
                }
            };
            var copy = SurfaceTable(1, 2); copy.Dock = DockStyle.Fill;
            copy.Padding = new Padding(24, 9, 19, 9);
            copy.RowStyles[0] = new RowStyle(SizeType.Absolute, 41);
            copy.RowStyles[1] = new RowStyle(SizeType.Absolute, 32);
            var heading = ContentLabel(title, 12f, true); heading.ForeColor = Color.White;
            heading.TextAlign = ContentAlignment.MiddleLeft;
            copy.Controls.Add(heading, 0, 0);
            var subheading = ContentLabel(description, 9.25f);
            subheading.ForeColor = Color.FromArgb(205, 217, 234);
            subheading.TextAlign = ContentAlignment.MiddleLeft;
            copy.Controls.Add(subheading, 0, 1);
            bar.Controls.Add(copy);
            table.Controls.Add(bar, 0, 0);
            return table;
        }
        static TableLayoutPanel SurfaceTable(int columns, int rows)
        {
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = columns, RowCount = rows, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent };
            for (int i = 0; i < columns; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            for (int i = 0; i < rows; i++) table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return table;
        }
        static TableLayoutPanel PathSettingRow(TableLayoutPanel parent, int index, string title, string subtitle)
        {
            var row = SurfaceTable(2, 1); row.Padding = new Padding(0, 18, 0, 18);
            row.ColumnStyles.Clear(); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var caption = SurfaceTable(1, String.IsNullOrEmpty(subtitle) ? 1 : 2); caption.Margin = new Padding(0, 8, 20, 0);
            caption.Controls.Add(ContentLabel(title, 11.25f, true), 0, 0);
            if (!String.IsNullOrEmpty(subtitle))
            {
                var detail = ContentLabel(subtitle, 9); detail.Margin = new Padding(0, 4, 0, 0); caption.Controls.Add(detail, 0, 1);
            }
            var body = SurfaceTable(1, 4);
            row.Controls.Add(caption, 0, 0); row.Controls.Add(body, 1, 0);
            row.Paint += delegate(object sender, PaintEventArgs e) {
                using (var pen = new Pen(Color.FromArgb(30, Color.White))) e.Graphics.DrawLine(pen, 0, 0, row.Width, 0);
            };
            parent.Controls.Add(row, 0, index);
            return body;
        }
        static TableLayoutPanel PathFieldLine(Control field, params Control[] actions)
        {
            var line = SurfaceTable(actions.Length + 1, 1);
            line.ColumnStyles.Clear(); line.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            line.RowStyles[0] = new RowStyle(SizeType.Absolute, 52);
            line.Controls.Add(field, 0, 0);
            for (int i = 0; i < actions.Length; i++)
            {
                line.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); line.Controls.Add(actions[i], i + 1, 0);
            }
            return line;
        }
        static Label PathHint(string text, bool warning = false)
        {
            var label = ContentLabel(text, 9.75f); label.Margin = new Padding(0, 8, 0, 0);
            if (warning) label.ForeColor = Color.FromArgb(255, 223, 173);
            return label;
        }
        static Label ContentLabel(string text, float size, bool bold = false)
        {
            return new Label { Text = text, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = bold ? GlassTheme.Text : GlassTheme.Muted, BackColor = Color.Transparent };
        }
        static MetalButton SettingButton(string text, EventHandler click)
        {
            var button = new MetalButton { Text = text, AutoSize = true, MinimumSize = new Size(110, 50),
                BlendWithSurface = true, Font = new Font("맑은 고딕", 11.25f, FontStyle.Bold), Margin = new Padding(0, 0, 10, 0) };
            button.Click += click; return button;
        }
        void ShowPage(Panel page)
        {
            // The native WinForms pages belong to the optimized mode. When a
            // themed archive owns the viewport, retain that UI and expose the
            // requested destination as its corresponding in-theme section.
            if (AppChannel.HasThemedUi && RedirectHubNativePage(page)) return;
            if (currentPage != null && currentPage != page && !goingBack) pageHistory.Push(currentPage);
            bool changingSettings = currentPage != page && (currentPage == settingsPage || page == settingsPage);
            currentPage = page;
            if (AppChannel.HasThemedUi && liveShellLayout != null)
            {
                bool home = page == homePage;
                liveShellLayout.Padding = home ? Padding.Empty : new Padding(30, 0, 30, 14);
                screen.Margin = home ? Padding.Empty : new Padding(3);
                homePage.Margin = Padding.Empty;
                // The HTML operation center uses the entire client area.
                // Existing native pages retain their full themed command bar
                // and every original campaign-control action.
                liveShellLayout.RowStyles[0].Height = home ? 0 :
                    (LiveTheme.Current == LiveTheme.Classic ? 0 : 132);
                liveShellLayout.RowStyles[liveNavRow].Height = home ? 0 : 74;
                commandDeck.Visible = !home && LiveTheme.Current != LiveTheme.Classic;
            }
            if (page == installedPage && !busy) installedView.Reload(catalogue);
            bool openingSettings = page == settingsPage && !settingsPage.Visible;
            var settingsViewport = (DarkScrollPanel)settings.Parent;
            var settingsControls = changingSettings
                ? new Control[] { settings }.Concat(Descendants(settings)).ToArray() : new Control[0];
            foreach (Control control in settingsControls) control.SuspendLayout();
            try
            {
                screen.SuspendLayout();
                foreach (Panel other in Pages()) other.Visible = other == page;
                page.BringToFront();
                int selected = page == homePage ? -1 : page == installedPage ? 1 : page == settingsPage ? 2 : 0;
                for (int i = 0; i < navigation.Controls.Count; i++) { ((MetalButton)navigation.Controls[i]).Primary = i == selected; navigation.Controls[i].Invalidate(); }
                screen.ResumeLayout(true);
            }
            finally
            {
                bool measureSettings = openingSettings && (settingsLayoutDirty ||
                    settingsMeasuredWidth != Math.Max(1, settingsViewport.ClientSize.Width -
                        settingsViewport.Controls.OfType<DarkScrollBar>().Single().Width - 4));
                foreach (Control control in settingsControls.Reverse()) control.ResumeLayout(measureSettings);
                if (openingSettings)
                {
                    if (measureSettings) settings.PerformLayout();
                    settingsViewport.ArrangeContent(); settingsLayoutDirty = false;
                    settingsMeasuredWidth = settings.Width;
                }
            }
            if (page == archivePage && IsHandleCreated) EnterArchive();
            if (AppChannel.HasThemedUi && page == homePage && IsHandleCreated) EnsureLiveHub();
        }
        async void GoBack()
        {
            // Windows delivers XBUTTON1 to the native owner, while WebView2
            // may consume the HTML mouse event before it reaches JavaScript.
            // Route the same physical Back button through the active themed
            // navigation first; native archive and other pages retain their
            // original history behavior below.
            if (AppChannel.HasThemedUi && currentPage == homePage &&
                hubReady && hubWeb != null && !hubWeb.IsDisposed && hubWeb.CoreWebView2 != null)
            {
                try
                {
                    var handled = await hubWeb.ExecuteScriptAsync(
                        "window.UiLab && window.UiLab.navigateBack()");
                    if (handled == "true") return;
                }
                catch (Exception error)
                { System.Diagnostics.Trace.WriteLine("Hub mouse back: " + error.Message); }
            }
            if (currentPage == archivePage && archiveView.TryGoBack()) return;
            if (currentPage == distributionPage && distributionView.TryGoBack()) return;
            if (pageHistory.Count == 0) return;
            goingBack = true;
            try { ShowPage(pageHistory.Pop()); }
            finally { goingBack = false; }
        }
        async void EnterArchive()
        {
            try { await archiveView.OpenAsync(); await RefreshSite(); }
            catch (Exception error) { Error(error); }
        }
        async Task<bool> RefreshSite()
        {
            if (busy) return false;
            if (!online) { status.Text = "격리 예시 · 저장된 목록"; return true; }
            if (refreshing != null) return await refreshing;
            refreshing = LoadCatalogue();
            try { return await refreshing; }
            finally { refreshing = null; }
        }
        async Task<bool> LoadCatalogue()
        {
            status.Text = "사이트 목록 확인 중…"; refresh.Enabled = false;
            try
            {
                string text = await Task.Run(() => Data.GetText(Data.Site + "/campaigns.js"));
                var next = Data.ParseCatalogue(text);
                File.WriteAllText(Path.Combine(root, "catalogue.js"), text, System.Text.Encoding.UTF8);
                catalogue = next;
                if (notificationFeed != null)
                {
                    notificationFeed.ObserveSite(next);
                    notifications.UnreadCount = notificationFeed.UnreadCount;
                }
                if (busy) deferredCatalogue = true;
                else AutoLinkExisting();
                if (!busy) status.Text = "사이트 연결됨 · " + DateTime.Now.ToString("HH:mm") + " 확인 · " + catalogue.Count + "개 항목";
                try
                {
                    await Task.Run(() => dedicatedLibrary.RefreshUpdateInfo(next, System.Threading.CancellationToken.None));
                    if (busy) deferredCatalogue = true;
                    else if (installedPage.Visible) installedView.Reload(catalogue);
                }
                catch (Exception error) {
                    if (!busy) {
                        if (installedPage.Visible) installedView.Reload(catalogue);
                        status.Text = "전용 캠페인 업데이트 확인 실패 · " + error.Message;
                    }
                }
                return true;
            }
            catch (Exception error) { status.Text = "목록 갱신 실패 · 저장된 목록 유지 · " + error.Message; return false; }
            finally { refresh.Enabled = !busy; }
        }
        async Task<bool> DownloadFromArchive(string url, string campaignId)
        {
            if (busy || requesting || distributionView.DownloadInProgress) { status.Text = "진행 중인 작업을 먼저 마쳐 주세요."; return true; }
            requesting = true;
            try
            {
                bool fresh = await RefreshSite();
                string action, dedicatedId; int episode;
                if (DedicatedAction(url, out action, out dedicatedId, out episode))
                {
                    var campaign = catalogue.SingleOrDefault(c => c.id == dedicatedId && c.kind == "dedicated");
                    if (campaign == null) throw new InvalidOperationException("아카이브에 등록된 전용 캠페인이 아닙니다.");
                    if (!fresh && action != "play") throw new InvalidOperationException("사이트 목록을 갱신한 뒤 설치해 주세요.");
                    if (action == "play") await PlayDedicated(campaign, episode);
                    else await InstallDedicated(campaign);
                    return true;
                }
                var item = DistributionPolicy.Resolve(catalogue, url, campaignId);
                if (item == null) return false;
                if (!fresh && !AppDialog.Confirm(this,
                    "사이트 목록을 갱신하지 못했습니다.\n저장된 목록의 배포 자료로 계속할까요?",
                    "저장된 목록 사용", "저장된 자료 사용", "취소")) return true;
                pendingSite = item;
                if (Data.DirectZip(url)) await DownloadDirect(item, url);
                else if (DistributionPolicy.DrivePage(url)) { ShowPage(distributionPage); await distributionView.OpenAsync(item, url); }
                else status.Text = item.name + " · 외부 배포처에서 받은 뒤 ‘내 캠페인 → ZIP 가져오기’로 등록하세요.";
                return Data.DirectZip(url) || DistributionPolicy.DrivePage(url);
            }
            catch (Exception error) { Error(error); return true; }
            finally { requesting = false; }
        }
        void SetBusy(bool value)
        {
            if (busy == value) return;
            busy = value; installedView.SetBusy(value);
            UpdateHubWorkState();
            if (value)
            {
                foreach (var control in conflictingControls)
                {
                    busyControlStates[control] = control.Enabled;
                    control.Enabled = false;
                }
            }
            else
            {
                foreach (var pair in busyControlStates) pair.Key.Enabled = pair.Value;
                busyControlStates.Clear();
                refresh.Enabled = refreshing == null;
                if (deferredCatalogue)
                {
                    deferredCatalogue = false; AutoLinkExisting(); installedView.Reload(catalogue);
                }
            }
        }
        async Task<T> RunWork<T>(string title, Func<Action<WorkProgress>, T> action,
            System.Threading.CancellationTokenSource cancellation = null)
        {
            hubWorkDepth++;
            hubWorkTitle=title;
            hubWorkCancellation=cancellation;
            hubMeasuredProgress = new WorkProgress(title);
            var stageEpoch = ++hubProgressStage;
            UpdateHubWorkState();
            using (var dialog = new WorkDialog(title, cancellation == null ? null : new Action(cancellation.Cancel)))
            {
                var reporter = new Progress<WorkProgress>(value => {
                    if (stageEpoch != hubProgressStage) return;
                    if (!dialog.IsDisposed) dialog.UpdateProgress(value);
                    ReportHubProgress(value);
                });
                bool fullScreen = CanShowHubWorkScreen();
                if (!fullScreen) dialog.Show(this);
                try
                {
                    // Permit the themed fullscreen layer to paint before the
                    // background work begins. Do not synthesize progress.
                    if (fullScreen) await Task.Delay(45);
                    T result = await Task.Run(() => action(value => ((IProgress<WorkProgress>)reporter).Report(value)));
                    // A committed install cannot be undone by a late click:
                    // the cancellable worker itself must throw before commit.
                    return result;
                }
                finally {
                    ++hubProgressStage;
                    dialog.Finish();
                    hubWorkDepth=Math.Max(0,hubWorkDepth-1);
                    if(hubWorkDepth==0)hubWorkTitle=null;
                    if(hubWorkDepth==0)hubWorkCancellation=null;
                    // An import/activation flow may continue through several
                    // measured phases. Never leave the previous phase's 100%
                    // on screen while preparing its next operation.
                    if (hubFlowDepth>0) hubMeasuredProgress=new WorkProgress("다음 작업 단계 준비 중");
                    UpdateHubWorkState();
                }
            }
        }
        void AddJob(string text) { jobs.Insert(0, DateTime.Now.ToString("HH:mm") + " · " + text); }
        async Task DownloadDirect(Campaign item, string url)
        {
            string applyNotice = autoApplyAfterInstall.Checked
                ? "등록에 성공하면 캠페인 적용도 이어서 진행합니다. 기존 적용 확인창 설정을 따릅니다."
                : "게임 적용은 설치 후 별도로 진행합니다.";
            if (!AppDialog.Confirm(this, item.name + "\n사이트 버전: " + item.version +
                "\n\n배포 ZIP을 받고 검증 후 보관소에 등록합니다.\n" + applyNotice +
                "\n원본 배포 파일의 공식 서명·해시 인증은 제공하지 않습니다.",
                "캠페인 받기", "다운로드", "취소")) return;
            BeginHubFlow(item.name + " · 다운로드 및 등록");
            try
            {
            string output = Path.Combine(downloadsRoot, Guid.NewGuid().ToString("N"), item.id + ".zip");
            SetBusy(true); AddJob(item.name + " 다운로드 시작"); status.Text = item.name + " 다운로드 중…";
            Campaign imported = null;
            try
            {
                using (var downloadCancellation = new System.Threading.CancellationTokenSource())
                    await RunWork("캠페인 다운로드", progress => {
                        Data.DownloadZip(url, output, progress, downloadCancellation.Token);
                        return true;
                    }, downloadCancellation);
                imported = await ImportPackage(item, output);
            }
            catch (OperationCanceledException)
            {
                AddJob(item.name + " 다운로드 취소 · 등록하지 않음");
                status.Text = item.name + " 다운로드를 중단했습니다. 완료되지 않은 ZIP은 설치하지 않았습니다.";
            }
            catch (Exception error) { AddJob(item.name + " 미완료 · " + error.Message); Error(error); }
            finally { SetBusy(false); }
            if (imported != null) await ApplyAfterImport(imported);
            if (imported != null) await RevealHubCampaign(imported);
            }
            finally { FinishHubFlow(); }
        }
        async Task<Campaign> ImportPackage(Campaign site, string zip)
        {
            Campaign automatic = null;
            var metadata = await RunWork("ZIP 정보 확인", progress => Library.InspectZip(zip));
            bool mismatchConfirmed = false;
            bool separateMetadata = false;
            if (site == null)
            {
                site = library.LinkedSite(metadata, catalogue);
                if (site == null)
                {
                    automatic = library.MatchSiteMetadata(metadata, catalogue);
                    site = automatic;
                    var candidates = SiteMatcher.Candidates(metadata, catalogue);
                    separateMetadata = site == null && candidates.Count > 0 &&
                        candidates.All(candidate => !library.AcceptsSiteMetadata(metadata, candidate));
                    bool existingMetadata = library.All().Any(c => SiteMatcher.SameMetadata(c, metadata));
                    if (site == null && !separateMetadata && !existingMetadata &&
                        !ChooseSite(null, true, out site, out mismatchConfirmed, metadata.slot, candidates)) return null;
                }
            }
            if (site != null && !library.AcceptsSiteMetadata(metadata, site))
            { site = null; automatic = null; separateMetadata = true; }
            if (site != null && !Catalogue.SupportsSlot(site, metadata.slot) && !mismatchConfirmed)
            {
                if (!SitePicker.ConfirmSlotMismatch(this, site, metadata.slot)) return null;
                mismatchConfirmed = true;
            }
            Campaign imported = await RunWork("캠페인 검증·등록", progress => {
                string hash = Data.Hash(zip, System.Threading.CancellationToken.None, (completed,total) => {
                    if (completed == 0 || completed == total || completed % (8L*1024*1024) == 0)
                        progress(new WorkProgress("ZIP 확인값 계산 중", completed, total, true));
                });
                var existing = library.All().SelectMany(c => library.Versions(c.id)).FirstOrDefault(c => c.versionKey == hash);
                if (existing != null)
                {
                    library.RememberZip(existing, zip);
                    if (site != null && library.AcceptsSiteMetadata(existing, site) &&
                        library.CatalogueId(existing.id) != site.id) library.LinkSite(existing.id, site.id);
                    return existing;
                }
                return site == null ? library.ImportLocalMetadata(zip, metadata, progress) : library.ImportForSite(zip, site, progress, mismatchConfirmed);
            });
            installedView.Reveal(catalogue, imported); ShowPage(installedPage); pendingSite = null;
            AddJob(imported.name + " 보관소 등록 완료");
            status.Text = separateMetadata ? "ZIP 제목·제작자가 달라 별도 모드로 등록했습니다. 기존 버전과 합치지 않았습니다." :
                automatic == null ? (autoApplyAfterInstall.Checked ? "보관소 등록 완료 · 캠페인 적용 확인 중…" :
                    "등록 완료 · 캠페인 적용은 별도입니다.") : "자동 사이트 연결: " + automatic.name + " · 다르면 관리에서 변경하세요.";
            return imported;
        }
        async Task ApplyAfterImport(Campaign imported)
        {
            if (!autoApplyAfterInstall.Checked || imported == null || imported.kind == "external" ||
                imported.kind == "dedicated") return;
            var current = library.Current();
            if (current.active == imported.id && current.versionKey == imported.versionKey)
            {
                status.Text = "이미 적용된 버전입니다 · " + imported.name;
                return;
            }
            // This is deliberately the same checked application path as the
            // manual button: it preserves backups, in-game safeguards and the
            // existing confirmation / don't-ask-again setting.
            AddJob(imported.name + " 설치 후 자동 적용 시작");
            if (!await ApplyInstalledVersion(imported, imported.versionKey))
            {
                AddJob(imported.name + " 보관소 등록 완료 · 자동 적용 미완료");
                if (!busy && library.Current().active != imported.id)
                    status.Text = imported.name + " 등록 완료 · 자동 적용은 취소되었거나 실패했습니다. 내 캠페인에서 다시 적용할 수 있습니다.";
            }
            else AddJob(imported.name + " 설치 후 자동 적용 완료");
        }
        internal async Task VerifyAutoApplyAfterImport(string fixtureGame, string zip)
        {
            if (!isolated || online)
                throw new InvalidOperationException("Auto-apply verification requires an offline isolated library.");
            if (autoApplyAfterInstall.Checked)
                throw new InvalidOperationException("New profiles must default to manual application.");
            var original = library.Import(zip, "auto-apply-fixture");
            game.Text = fixtureGame;
            applyConfirmation.Checked = false;
            autoApplyAfterInstall.Checked = false;
            await ApplyAfterImport(original);
            if (library.Current().active != null)
                throw new InvalidOperationException("Auto-apply off must not change the active campaign.");
            autoApplyAfterInstall.Checked = true;
            using (var reopened = new Hangar(root, null, false))
                if (!reopened.autoApplyAfterInstall.Checked || reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Auto-apply and confirmation options must persist independently.");
            await ApplyAfterImport(original);
            if (library.Current().active != original.id || library.Current().versionKey != original.versionKey)
                throw new InvalidOperationException("Auto-apply on must activate the imported version.");
            using (var reopened = new Hangar(root, null, false))
                if (!reopened.autoApplyAfterInstall.Checked)
                    throw new InvalidOperationException("Auto-apply must survive restarting the app.");
            // A game that is running must never be modified just because auto-apply is enabled.
            autoApplyAfterInstall.Checked = false;
            Console.WriteLine("AUTO APPLY PASS: opt-in defaults off, is saved, and calls the same guarded apply path after import.");
        }
        void AutoLinkExisting()
        {
            foreach (var package in library.All()) library.AutoLink(package, catalogue);
        }
        async Task RegisterDownloaded(Campaign site, string zip)
        {
            if (busy) throw new InvalidOperationException("다른 설치가 진행 중입니다. 받은 ZIP을 나중에 가져와 주세요.");
            BeginHubFlow((site == null ? Path.GetFileNameWithoutExtension(zip) : site.name) + " · ZIP 등록");
            try
            {
            SetBusy(true);
            Campaign imported = null;
            try { imported = await ImportPackage(site, zip); }
            finally { SetBusy(false); }
            if (imported != null) await ApplyAfterImport(imported);
            if (imported != null) await RevealHubCampaign(imported);
            }
            finally { FinishHubFlow(); }
        }
        async void ImportLocal(Campaign hint)
        {
            if (busy || distributionView.DownloadInProgress) return;
            try
            {
                using (var file = new OpenFileDialog { Filter = "캠페인 ZIP (*.zip)|*.zip", Title = "캠페인 ZIP 선택" })
                {
                    if (file.ShowDialog(this) != DialogResult.OK) return;
                    await RefreshSite();
                    await ImportZipFile(file.FileName, hint);
                }
            }
            catch (Exception error) { Error(error); }
        }
        async Task ImportZipFile(string path, Campaign hint)
        {
            if (!File.Exists(path) || !String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("실제 ZIP 파일을 선택해 주세요.");
            BeginHubFlow(Path.GetFileNameWithoutExtension(path) + " · ZIP 확인");
            try
            {
            // The first progress frame must be painted before ZIP inspection,
            // which can be synchronous for unindexed local archives.
            if (CanShowHubWorkScreen()) await Task.Delay(45);
            // ZIP probes may scan gigabytes of archives; never block the
            // WinForms/WebView2 input thread before the first loading frame.
            bool standalone = await RunWork("ZIP 패키지 형식 확인", progress => {
                progress(new WorkProgress("CCM 메타데이터 검사 중"));
                return Library.MissingCcmMetadata(path);
            });
            if (standalone)
            {
                // "Launcher" is often a .SC2Map (not an EXE). Inspect archive
                // structure first, without invoking CCM or running a creator EXE.
                var package = await RunWork("전용 ZIP 구조 분석", progress => {
                    progress(new WorkProgress("시작 맵·Launcher·모드 검사 중"));
                    return StandaloneZip.Inspect(path);
                });
                if (package.Choices.Count != 0)
                {
                    StandaloneZipChoice selected = package.Choices[0];
                    if (package.Choices.Count > 1)
                    {
                        using (var choices = new StandaloneChoiceDialog(package))
                        {
                            if (choices.ShowDialog(this) != DialogResult.OK) return;
                            selected = choices.Selected;
                        }
                    }
                    // A localized/modified installation may have different
                    // SC2Map and SC2Mod bytes. Detect *all* conflicts before
                    // copying anything; do not fall into the old IO exception
                    // after the user has already approved the import.
                    string inspectionGame = game.Text;
                    var conflicts = selected.Kind == "author-launcher" ? null :
                        await RunWork("설치 파일 충돌 확인", progress => {
                            progress(new WorkProgress("기존 맵·모드 비교 중"));
                            return StandaloneConflicts.Inspect(package, inspectionGame);
                        });
                    if (conflicts != null && conflicts.HasConflict)
                    {
                        if (!AppDialog.Confirm(this, conflicts.Summary(package.Name),
                            "다른 설치본 발견", "제거 후 설치", "닫기")) return;
                        if (busy) throw new InvalidOperationException("다른 설치가 진행 중입니다.");
                        SetBusy(true);
                        try
                        {
                            var replaced = await RunWork("기존 맵·모드 백업·새 ZIP 설치", progress =>
                                StandaloneConflicts.Replace(package, selected,
                                    root, game.Text, externalCampaigns, progress));
                            installedView.Reveal(catalogue, replaced.Campaign);
                            await RevealHubCampaign(replaced.Campaign);
                            status.Text = "전용 캠페인 맵·모드 교체 완료 · 이전 설치본 백업: " + replaced.PreviousBackup;
                            AddJob(replaced.Campaign.name + " 기존 맵·모드 백업 후 새 ZIP 설치");
                        }
                        finally { SetBusy(false); }
                        return;
                    }
                    if (selected == null || !AppDialog.Confirm(this, StandaloneZip.Summary(package, selected),
                        "전용 캠페인 자동 인식", "설치·연결")) return;
                    if (busy) throw new InvalidOperationException("다른 설치가 진행 중입니다.");
                    SetBusy(true);
                    try
                    {
                        var result = await RunWork("전용 캠페인 ZIP 설치·연결", progress =>
                            StandaloneZip.Install(package, selected, root, game.Text, externalCampaigns, progress));
                        installedView.Reveal(catalogue, result.Campaign);
                        await RevealHubCampaign(result.Campaign);
                        status.Text = result.AlreadyLinked ? "이미 연결된 전용 캠페인을 열었습니다. 중복 설치하지 않았습니다." :
                            "전용 ZIP 자동 인식·연결 완료 · " + selected.Title +
                            " · 제작자 EXE는 자동 실행하지 않았습니다.";
                        AddJob(result.Campaign.name + (result.AlreadyLinked ? " 기존 연결 확인" : " 전용 ZIP 자동 설치·연결"));
                    }
                    finally { SetBusy(false); }
                    return;
                }
                if (package.GameFiles.Count > 0 && package.GameFiles.All(f => !f.IsMap))
                {
                    // Some creator campaigns distribute Maps.zip and Mods.zip
                    // separately. Install a verified mods-only companion
                    // without fabricating a campaign launcher or card.
                    string inspectionGame = game.Text;
                    var conflicts = await RunWork("모드 설치 충돌 확인", progress => {
                        progress(new WorkProgress("기존 모드 비교 중"));
                        return StandaloneConflicts.Inspect(package, inspectionGame);
                    });
                    string title = conflicts.HasConflict ? "기존 모드 발견" : "전용 캠페인 모드 ZIP";
                    string summary = conflicts.HasConflict ?
                        conflicts.Summary(package.Name) :
                        package.Name + "에는 게임 모드 관련 파일 " + package.GameFiles.Count +
                        "개가 있습니다.\n\n게임 Mods 폴더에 설치합니다. 기존 파일은 백업하고, " +
                        "원본 ZIP·Bank·세이브는 변경하지 않습니다.\n시작 맵 ZIP은 별도로 연결해야 합니다.";
                    if (!AppDialog.Confirm(this, summary, title,
                        conflicts.HasConflict ? "제거 후 설치" : "모드 설치", "닫기")) return;
                    if (busy) throw new InvalidOperationException("다른 설치가 진행 중입니다.");
                    SetBusy(true);
                    try
                    {
                        var installedMods = await RunWork("전용 캠페인 모드 설치", progress =>
                            StandaloneConflicts.Replace(package, null, root, game.Text, externalCampaigns, progress));
                        status.Text = "모드 ZIP 설치 완료 · 이전 모드 백업: " + installedMods.PreviousBackup +
                            " · 시작 맵은 별도로 연결해 주세요.";
                        AddJob(package.Name + " 모드 설치 완료 · 기존 파일 백업 유지");
                    }
                    finally { SetBusy(false); }
                    return;
                }
                if (AppDialog.Confirm(this,
                    "이 ZIP에는 CCM용 metadata.txt가 없고 확실한 시작 맵·제작자 Launcher도 찾지 못했습니다.\n\n" +
                    "이미 설치한 .SC2Map 또는 제작자 .exe를 직접 연결하시겠습니까?\n" +
                    "원본 ZIP·게임 파일·뱅크·세이브는 변경하지 않습니다.",
                    "비CCM ZIP 연결", "연결 설정"))
                    EditExternal(null, Path.GetFileNameWithoutExtension(path));
                return;
            }
            await RegisterDownloaded(hint, path);
            }
            finally { FinishHubFlow(); }
        }
        async Task ImportDropped(string[] files)
        {
            foreach (string file in files)
            {
                try { await ImportZipFile(file, null); }
                catch (Exception error) { Error(error); }
            }
            // The themed campaign list is a separate WebView2. A successful
            // import or an interrupted/cancelled dialog must refresh its truth.
            if (AppChannel.HasThemedUi) await PushLiveHubData();
        }
        internal async Task VerifyNonCcmZip(string zip)
        {
            if (!isolated || online || !Library.MissingCcmMetadata(zip))
                throw new InvalidOperationException("Non-CCM import fixture must use an isolated metadata-free ZIP.");
            int before = externalCampaigns.All().Count, packages = library.All().Count;
            string activeBefore = library.Current().active, hashBefore = Data.Hash(zip);
            bool offered = false, editor = false;
            int ticks = 0;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    ticks++;
                    var message = Application.OpenForms.OfType<AppDialog>()
                        .FirstOrDefault(dialog => dialog.Text == "비CCM ZIP 연결");
                    if (message != null && !offered)
                    {
                        offered = message.MessageText.Contains("metadata.txt") &&
                            message.MessageText.Contains("시작 맵");
                        message.DialogResult = DialogResult.OK;
                    }
                    var link = Application.OpenForms.OfType<ExternalCampaignDialog>().FirstOrDefault();
                    if (link != null)
                    {
                        editor = link.CampaignName == Path.GetFileNameWithoutExtension(zip);
                        link.DialogResult = DialogResult.Cancel;
                    }
                    if (ticks > 120)
                    {
                        if (message != null) message.DialogResult = DialogResult.Cancel;
                        if (link != null) link.DialogResult = DialogResult.Cancel;
                    }
                };
                timer.Start();
                await ImportZipFile(zip, null);
                timer.Stop();
            }
            if (!offered || !editor || ticks > 120 || externalCampaigns.All().Count != before ||
                library.All().Count != packages || library.Current().active != activeBefore ||
                Data.Hash(zip) != hashBefore)
                throw new InvalidOperationException("Non-CCM ZIP must offer only safe manual connection; cancellation must preserve ZIP, library and game state.");
            Console.WriteLine("NON-CCM ZIP PASS: missing metadata opens named connection editor, cancel preserves ZIP and registrations; no auto-extraction or game changes.");
        }
        internal async Task VerifyStandaloneZipAuto(string zip, string fixtureGame)
        {
            if (!isolated || online || !Library.MissingCcmMetadata(zip))
                throw new InvalidOperationException("Auto-link ZIP fixture requires offline isolated storage.");
            string originalGame = game.Text, originalHash = Data.Hash(zip), active = library.Current().active;
            int before = externalCampaigns.All().Count, prompts = 0;
            var package = StandaloneZip.Inspect(zip);
            if (package.Choices.Count != 1 || package.Choices[0].Kind != "map")
                throw new InvalidOperationException("Auto-link fixture needs a unique map launcher.");
            try
            {
                game.Text = fixtureGame;
                using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    int ticks = 0;
                    timer.Tick += delegate {
                        ticks++;
                        var confirmation = Application.OpenForms.OfType<AppDialog>().FirstOrDefault(
                            dialog => dialog.Text == "전용 캠페인 자동 인식");
                        if (confirmation != null)
                        {
                            if (confirmation.MessageText.Contains("Launcher.SC2Map") &&
                                confirmation.MessageText.Contains("설치"))
                                prompts++;
                            confirmation.DialogResult = DialogResult.OK;
                        }
                        if (ticks > 100 && confirmation != null) confirmation.DialogResult = DialogResult.Cancel;
                    };
                    timer.Start();
                    await ImportZipFile(zip, null);
                    timer.Stop();
                    if (ticks > 100) throw new InvalidOperationException("Automatic ZIP import did not finish.");
                }
                if (prompts == 0 || externalCampaigns.All().Count != before + 1 ||
                    library.Current().active != active || Data.Hash(zip) != originalHash ||
                    !File.Exists(Path.Combine(fixtureGame, "Maps", "Recognized Standalone", "Campaign", "Launcher.SC2Map")))
                    throw new InvalidOperationException("Auto-link must detect, install and register without CCM activation or ZIP changes.");
                // Reimport must still show the recognized candidate, but must
                // resolve the existing connection rather than add a second one.
                using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
                {
                    timer.Tick += delegate {
                        var confirmation = Application.OpenForms.OfType<AppDialog>().FirstOrDefault(
                            dialog => dialog.Text == "전용 캠페인 자동 인식");
                        if (confirmation != null) confirmation.DialogResult = DialogResult.OK;
                    };
                    timer.Start();
                    await ImportZipFile(zip, null);
                    timer.Stop();
                }
                if (externalCampaigns.All().Count != before + 1 || Data.Hash(zip) != originalHash)
                    throw new InvalidOperationException("Importing the same ZIP twice duplicated a connection or changed the source.");
                Console.WriteLine("AUTO ZIP UI PASS: one ZIP launcher detected, confirmed, installed and linked; repeat import reused existing card; source and CCM activation preserved.");
            }
            finally { game.Text = originalGame; }
        }
        internal async Task VerifyStandaloneReplacementChoice(string zip, string fixtureGame)
        {
            if (!isolated || online || !Library.MissingCcmMetadata(zip))
                throw new InvalidOperationException("Replacement confirmation test requires an isolated ZIP and library.");
            var found = StandaloneZip.Inspect(zip);
            var current = StandaloneZip.ExistingMapVariant(found, found.Choices.Single(), fixtureGame);
            if (current == null || current.DifferentFileCount != 3)
                throw new InvalidOperationException("Expected a complete different three-map localization fixture.");
            string oldHash = Data.Hash(current.Launcher), zipHash = Data.Hash(zip);
            int before = externalCampaigns.All().Count;
            string savedGame = game.Text;
            try
            {
                game.Text = fixtureGame;
                bool shown = false, buttonsCorrect = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 55 })
                {
                    int ticks = 0;
                    timer.Tick += delegate {
                        ticks++;
                        var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault(d => d.Text == "다른 설치본 발견");
                        if (dialog != null)
                        {
                            if (!shown)
                            {
                                string previewImage = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                                    "dark-story-replace-dialog.png");
                                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                                {
                                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                                    bitmap.Save(previewImage);
                                }
                                Console.WriteLine("DARK STORY DIALOG RENDER: " + previewImage);
                            }
                            shown = true;
                            var labels = new List<string>();
                            Action<Control> walk = null;
                            walk = c => {
                                var button = c as MetalButton;
                                if (button != null&&!String.IsNullOrEmpty(button.Text)) labels.Add(button.Text);
                                foreach (Control child in c.Controls) walk(child);
                            };
                            walk(dialog);
                            buttonsCorrect = labels.Count == 2 &&
                                labels.Contains("제거 후 설치") && labels.Contains("닫기");
                            dialog.DialogResult = DialogResult.Cancel;
                        }
                        if (ticks > 90 && dialog != null) dialog.DialogResult = DialogResult.Cancel;
                    };
                    timer.Start();
                    await ImportZipFile(zip, null);
                    timer.Stop();
                    if (ticks > 90) throw new InvalidOperationException("Replacement dialog did not complete.");
                }
                if (!shown || !buttonsCorrect || Data.Hash(current.Launcher) != oldHash ||
                    Data.Hash(zip) != zipHash || externalCampaigns.All().Count != before ||
                    Directory.Exists(Data.Under(root, "standalone-backups")) &&
                    Directory.GetDirectories(Data.Under(root, "standalone-backups")).Length > 0)
                    throw new InvalidOperationException("Closing collision dialog must leave both files and registrations unchanged.");
                Console.WriteLine("DARK STORY UI PASS: collision offers exactly '제거 후 설치' and '닫기'; Close preserves translated files, source ZIP and registrations.");
                bool accepted = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 55 })
                {
                    timer.Tick += delegate {
                        var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault(d => d.Text == "다른 설치본 발견");
                        if (dialog != null) { accepted = true; dialog.DialogResult = DialogResult.OK; }
                    };
                    timer.Start();
                    await ImportZipFile(zip, null);
                    timer.Stop();
                }
                if (!accepted || externalCampaigns.All().Count != before + 1 ||
                    Data.Hash(current.Launcher) == oldHash || Data.Hash(zip) != zipHash ||
                    Directory.GetDirectories(Data.Under(root, "standalone-backups")).Length != 1)
                    throw new InvalidOperationException("Remove-and-install must back up, install ZIP and register exactly one campaign.");
                Console.WriteLine("DARK STORY UI PASS: Remove and install creates a complete backup, installs the ZIP, and links one campaign.");
            }
            finally { game.Text = savedGame; }
        }
        internal async Task VerifyStandaloneModConflict(string zip, string fixtureGame)
        {
            if (!isolated || online || !Library.MissingCcmMetadata(zip))
                throw new InvalidOperationException("Mod-only ZIP UI check requires offline isolated storage.");
            var package = StandaloneZip.Inspect(zip);
            if (package.Choices.Count != 0 || package.GameFiles.Any(f => f.IsMap))
                throw new InvalidOperationException("Fixture requires a mods-only ZIP.");
            string installedMod = Path.Combine(fixtureGame, "Mods", "TGH_Mod.SC2Mod");
            string oldHash = Data.Hash(installedMod), zipHash = Data.Hash(zip);
            int before = externalCampaigns.All().Count;
            string backups = Data.Under(root, "standalone-backups");
            int previous = Directory.Exists(backups) ? Directory.GetDirectories(backups).Length : 0;
            string priorGame = game.Text;
            try
            {
                game.Text = fixtureGame;
                bool shown = false, exactButtons = false, namesFile = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 55 })
                {
                    int ticks = 0;
                    timer.Tick += delegate {
                        ticks++;
                        var dialog = Application.OpenForms.OfType<AppDialog>()
                            .FirstOrDefault(d => d.Text == "기존 모드 발견");
                        if (dialog != null)
                        {
                            if (!shown)
                            {
                                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dark-story-mod-conflict-dialog.png");
                                using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                                {
                                    dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                                    bitmap.Save(path);
                                }
                                Console.WriteLine("TGH MOD DIALOG RENDER: " + path);
                            }
                            shown = true;
                            namesFile = dialog.MessageText.Contains("TGH_Mod.SC2Mod");
                            var labels = new List<string>();
                            Action<Control> inspect = null;
                            inspect = c => {
                                var button = c as MetalButton;
                                if (button != null&&!String.IsNullOrEmpty(button.Text)) labels.Add(button.Text);
                                foreach (Control child in c.Controls) inspect(child);
                            };
                            inspect(dialog);
                            exactButtons = labels.Count == 2 && labels.Contains("제거 후 설치") && labels.Contains("닫기");
                            dialog.DialogResult = DialogResult.Cancel;
                        }
                        if (ticks > 90 && dialog != null) dialog.DialogResult = DialogResult.Cancel;
                    };
                    timer.Start(); await ImportZipFile(zip, null); timer.Stop();
                    if (ticks > 90) throw new IOException("Mod collision prompt did not close.");
                }
                if (!shown || !namesFile || !exactButtons || Data.Hash(installedMod) != oldHash ||
                    Data.Hash(zip) != zipHash || externalCampaigns.All().Count != before ||
                    (Directory.Exists(backups) ? Directory.GetDirectories(backups).Length : 0) != previous)
                    throw new IOException("Close must leave the real conflict bytes, ZIP and registrations unchanged.");
                Console.WriteLine("TGH MOD UI PASS: original conflicting mod appears with exactly Remove and install / Close; Close changes nothing.");
                bool approved = false;
                using (var timer = new System.Windows.Forms.Timer { Interval = 55 })
                {
                    timer.Tick += delegate {
                        var dialog = Application.OpenForms.OfType<AppDialog>()
                            .FirstOrDefault(d => d.Text == "기존 모드 발견");
                        if (dialog != null) { approved = true; dialog.DialogResult = DialogResult.OK; }
                    };
                    timer.Start(); await ImportZipFile(zip, null); timer.Stop();
                }
                if (!approved || Data.Hash(installedMod) == oldHash ||
                    Data.Hash(zip) != zipHash || externalCampaigns.All().Count != before ||
                    !Directory.Exists(backups) || Directory.GetDirectories(backups).Length != previous + 1)
                    throw new IOException("Approved mod replacement must update only the mod and create one recoverable backup.");
                Console.WriteLine("TGH MOD UI PASS: approved replacement installs new mod and backs up old mod; no fake campaign card or game launch.");
            }
            finally { game.Text = priorGame; }
        }
        internal async Task VerifyZipDrop(string path, bool browse = false, bool expectPicker = true,
            string dropPage = "installed")
        {
            Control destination = installedView;
            if (dropPage == "archive") { ShowPage(archivePage); destination = archiveView; }
            else if (dropPage == "settings") { ShowPage(settingsPage); destination = settingsPage; }
            else if (dropPage == "installed") ShowPage(installedPage);
            else throw new ArgumentException("Unknown ZIP drop page.");
            PerformLayout();
            string before = library.Current().active;
            bool pickerOpened = false, fullPickerOpened = false, wrongInitialPicker = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 100 })
            {
                timer.Tick += delegate {
                    var picker = Application.OpenForms.OfType<SitePicker>().FirstOrDefault();
                    if (picker == null) return;
                    if (!pickerOpened)
                    {
                        pickerOpened = true; wrongInitialPicker = !picker.RecommendationMode;
                        if (browse) { picker.RequestBrowse(); return; }
                    }
                    if (!picker.RecommendationMode) fullPickerOpened = true;
                    picker.DialogResult = DialogResult.OK;
                };
                timer.Start();
                await zipDrop.VerifyEventsAndDrop(path, destination);
            }
            if (pickerOpened != expectPicker || wrongInitialPicker || fullPickerOpened != browse)
                throw new InvalidOperationException("New ZIP requires recommendations; registered ZIP must retain its connection without another picker.");
            var expected = library.All().Single(c => c.versionKey == Data.Hash(path));
            installedView.VerifyImported(expected);
            if (library.Current().active != before || !File.Exists(path)) throw new InvalidOperationException("ZIP drop changed activation or source ZIP.");
            Console.WriteLine("UI PASS: ZIP drag accepted from " + dropPage +
                "; metadata slot=" + expected.slot + "; hero preview selected; source/game preserved.");
        }
        internal async Task VerifySlotMismatchImport(string zip, Campaign site, bool accepted)
        {
            if (!isolated) throw new InvalidOperationException("Mismatch import checks require an isolated library.");
            int before = library.All().Count;
            string active = library.Current().active;
            await ImportPackage(site, zip);
            if (library.All().Count != before + (accepted ? 1 : 0) || library.Current().active != active)
                throw new InvalidOperationException("Mismatch confirmation changed registration or game activation unexpectedly.");
            if (accepted && library.Installed(site.id).slot != Library.InspectZip(zip).slot)
                throw new InvalidOperationException("A confirmed site mismatch must preserve the ZIP deployment target.");
            Console.WriteLine("UI PASS: import mismatch " + (accepted ? "Yes registers the ZIP target" : "No leaves the library unchanged") + "; no game activation.");
        }
        bool ChooseSite(Campaign hint, bool allowUnlinked, out Campaign selected, out bool mismatchConfirmed,
            string requiredSlot = null, IEnumerable<Campaign> recommendations = null)
        {
            selected = null; mismatchConfirmed = false;
            using (var dialog = new SitePicker(catalogue, loadThumbnail, hint, allowUnlinked, requiredSlot, recommendations))
            {
                var result = dialog.ShowDialog(this);
                if (result == DialogResult.Retry) return ChooseSite(hint, allowUnlinked, out selected, out mismatchConfirmed, requiredSlot);
                if (result != DialogResult.OK) return false;
                selected = dialog.Selected; mismatchConfirmed = dialog.SlotMismatchConfirmed; return allowUnlinked || selected != null;
            }
        }
        async void LinkInstalled(Campaign installed)
        {
            if (busy) return;
            if (installed.kind == "external") { EditExternal(installed); return; }
            try
            {
                await RefreshSite(); Campaign site; bool mismatchConfirmed;
                var hint = catalogue.FirstOrDefault(c => c.id == library.CatalogueId(installed.id));
                if (!ChooseSite(hint, false, out site, out mismatchConfirmed, installed.slot, SiteMatcher.Candidates(installed, catalogue))) return;
                if (!Catalogue.SupportsSlot(site, installed.slot) && !mismatchConfirmed)
                    throw new InvalidOperationException("설치 자료와 사이트 캠페인의 적용 대상이 다릅니다.");
                library.LinkSite(installed.id, site.id); installedView.Reload(catalogue); status.Text = "사이트 연결 완료 · 파일과 현재 적용 상태는 그대로 유지했습니다.";
            }
            catch (Exception error) { Error(error); }
        }
        async void UpdateInstalled(Campaign installed)
        {
            if (installed.kind == "dedicated") { await InstallDedicated(installed); return; }
            try { await OpenInstalledDistribution(installed); }
            catch (Exception error) { Error(error); }
        }
        async void OpenSiteInformation(Campaign installed)
        {
            if (installed.kind == "external")
            {
                AppDialog.Info(this, installed.name + "\n설치 방식: " + ExternalCampaigns.InstallationName(installed) +
                    "\n실행 방식: " + ExternalCampaigns.LaunchName(installed) +
                    "\n시작 맵: " + (installed.launcherMap ?? "사용 안 함") + "\n제작자 런처: " + (installed.launcherExecutable ?? "연결 안 됨") +
                    "\n격납고는 연결한 대상만 엽니다. 설치·업데이트는 제작자 안내를 따르세요." +
                    "\n연결을 열어도 현재 적용한 CCM은 유지합니다. 외부 런처가 바꾼 파일은 격납고에서 복원하지 않습니다.", "외부 연결 정보"); return;
            }
            try
            {
                var site = catalogue.FirstOrDefault(c => c.id == library.CatalogueId(installed.id));
                if (site == null) { status.Text = "사이트 캠페인을 먼저 연결해 주세요."; return; }
                ShowPage(archivePage); await archiveView.OpenCampaignAsync(site);
            }
            catch (Exception error) { Error(error); }
        }
        async Task OpenInstalledDistribution(Campaign installed)
        {
            if (busy || requesting || distributionView.DownloadInProgress) { status.Text = "진행 중인 작업을 먼저 마쳐 주세요."; return; }
            requesting = true;
            try
            {
                string linked=library.CatalogueId(installed.id);
                var site = catalogue.FirstOrDefault(c => c.id == linked);
                if (site==null && await RefreshSite())
                    site=catalogue.FirstOrDefault(c=>c.id==linked);
                if (site == null) { status.Text = "관리의 ‘사이트 캠페인 연결’을 먼저 선택해 주세요."; return; }
                await OpenUpdateFiles(site);
            }
            finally { requesting = false; }
        }
        internal async Task VerifyUpdateRoute(string zip, Campaign site)
        {
            if (!isolated || online) throw new InvalidOperationException("Update route fixture must be offline and isolated.");
            catalogue = new List<Campaign> { site };
            var installed = library.Installed(site.id) ?? library.Import(zip, site.id);
            string before = library.Current().active;
            ShowPage(installedPage);
            await OpenInstalledDistribution(installed);
            if (library.Current().active != before)
                throw new InvalidOperationException("Opening update files must not activate a campaign.");
        }
        async Task OpenUpdateFiles(Campaign site)
        {
            var links=DistributionPolicy.Links(site);
            if(links.Count==0){ShowPage(archivePage);if(!isolated)await archiveView.OpenCampaignAsync(site);return;}
            string url=links[0].url;
            if(links.Count>1){
                using(var picker=new DistributionLinkPicker(site,links)){
                    if(picker.ShowDialog(this)!=DialogResult.OK)return;
                    url=picker.SelectedUrl;
                }
            }
            await openDistribution(site,url);
            status.Text=site.name+" · 배포 파일 페이지";
        }
        internal async Task VerifyApplyCancellation(string fixtureGame, Campaign installed)
        {
            if (!isolated || online) throw new InvalidOperationException("Apply cancellation fixture must be isolated.");
            game.Text = fixtureGame;
            string before = library.Current().active;
            if (await ApplyInstalledVersion(installed, installed.versionKey) || library.Current().active != before)
                throw new InvalidOperationException("Cancelled apply changed active campaign.");
            Console.WriteLine("UI PASS: cancelling themed apply confirmation does not call campaign activation.");
        }
        internal async Task VerifyApplyOption(string fixtureGame, Campaign installed)
        {
            if (!isolated || online) throw new InvalidOperationException("Apply option test requires an offline isolated library.");
            if (!applyConfirmation.Checked) throw new InvalidOperationException("Confirmation must default on.");
            game.Text = fixtureGame;
            applyConfirmation.Checked = false;
            bool unexpectedDialog = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<ApplyConfirmation>().FirstOrDefault();
                    if (dialog == null) return;
                    unexpectedDialog = true; dialog.DialogResult = DialogResult.Cancel;
                };
                timer.Start();
                if (!await ApplyInstalledVersion(installed, installed.versionKey) || unexpectedDialog ||
                    library.Current().active != installed.id || Data.Read<ApplyPreference>(applyPreferenceFile).showConfirmation)
                    throw new InvalidOperationException("Disabled confirmation must persist and apply without displaying a dialog.");
                timer.Stop();
            }
            using (var reopened = new Hangar(root, null, false))
                if (reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Confirmation-off setting must survive reopening.");
            if(!await ApplyInstalledVersion(installed,installed.versionKey)||library.Current().active!=installed.id||
                !status.Text.StartsWith("이미 적용된 버전입니다",StringComparison.Ordinal))
                throw new InvalidOperationException("The already-applied version must be reported without another activation.");
            // Confirmation is for an effective change, not the now-disabled duplicate apply action.
            library.RestoreBase();
            applyConfirmation.Checked = true;
            bool shown = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<ApplyConfirmation>().FirstOrDefault();
                    if (dialog == null) return;
                    shown = true; dialog.DialogResult = DialogResult.Cancel;
                };
                timer.Start();
                if (await ApplyInstalledVersion(installed, installed.versionKey) || !shown ||
                    !Data.Read<ApplyPreference>(applyPreferenceFile).showConfirmation)
                    throw new InvalidOperationException("Re-enabled confirmation must display again and respect cancellation.");
                timer.Stop();
            }
            bool declinedSkip = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<ApplyConfirmation>().FirstOrDefault();
                    if (dialog == null) return;
                    declinedSkip = true; dialog.SetDoNotAskAgainForTest(true);
                    dialog.DialogResult = DialogResult.Cancel;
                };
                timer.Start();
                if (await ApplyInstalledVersion(installed, installed.versionKey) || !declinedSkip ||
                    !applyConfirmation.Checked || !Data.Read<ApplyPreference>(applyPreferenceFile).showConfirmation)
                    throw new InvalidOperationException("Cancelling the warning must not save do-not-ask.");
                timer.Stop();
            }
            bool acceptedSkip = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<ApplyConfirmation>().FirstOrDefault();
                    if (dialog == null) return;
                    acceptedSkip = true; dialog.SetDoNotAskAgainForTest(true);
                    dialog.DialogResult = DialogResult.OK;
                };
                timer.Start();
                if (!await ApplyInstalledVersion(installed, installed.versionKey) || !acceptedSkip ||
                    applyConfirmation.Checked || Data.Read<ApplyPreference>(applyPreferenceFile).showConfirmation)
                    throw new InvalidOperationException("Accepting do-not-ask must disable the confirmation option in Settings.");
                timer.Stop();
            }
            using (var reopened = new Hangar(root, null, false))
                if (reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Do-not-ask must persist after reopening Settings.");
            applyConfirmation.Checked = true;
            Console.WriteLine("UI PASS: confirmation on/off persists; do-not-ask saves only after Apply, not Cancel, and syncs Settings.");
        }
        async Task OpenRegisteredDistribution(Campaign site, string url)
        {
            if (DistributionPolicy.DrivePage(url)) { ShowPage(distributionPage); await distributionView.OpenAsync(site, url); }
            else if (Data.DirectZip(url)) await DownloadDirect(site, url);
            else if (DistributionPolicy.Https(url)) System.Diagnostics.Process.Start(url);
        }
        async void RemoveInstalled(Campaign installed)
        {
            if (busy) return;
            if (installed.kind == "dedicated")
            {
                if (!AppDialog.Confirm(this, "격납고가 설치한 전용 캠페인 파일만 제거합니다.\n다른 캠페인이 함께 쓰는 파일과 뱅크·세이브·원본 백업은 보존합니다.",
                    "전용 캠페인 설치 제거", "제거")) return;
                SetBusy(true);
                try
                {
                    await RunWork("전용 캠페인 설치 제거", progress => { dedicatedLibrary.Uninstall(installed.id, game.Text); return true; });
                    installedView.Reload(catalogue); status.Text = "전용 캠페인 설치 제거 완료";
                }
                catch (Exception error) { Error(error); }
                finally { SetBusy(false); }
                return;
            }
            if (installed.kind == "external")
            {
                if (!AppDialog.Confirm(this, "설치 목록에서 이 캠페인 연결을 제거합니다.\n격납고가 직접 설치한 파일이 아니므로 제작자 런처·시작 맵·뱅크·세이브·플레이 기록은 삭제하지 않습니다.",
                    "전용 캠페인 설치 제거", "목록에서 제거")) return;
                externalCampaigns.Forget(installed.id); installedView.Reload(catalogue); return;
            }
            using (var dialog = new RemovalDialog(installed, library.Versions(installed.id).Count))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                await RemoveInstalledSelected(installed,dialog.Kind);
            }
        }
        // Both optimized/native and themed manage screens share exactly the
        // same guarded file-removal implementation. A requested scope never
        // bypasses cancellation, ZIP preview or active-version confirmation.
        async Task RemoveInstalledSelected(Campaign installed, RemovalKind kind)
        {
            if(busy || installed==null || installed.kind=="dedicated" || installed.kind=="external")return;
            SetBusy(true);
            try
            {
                List<RegisteredZip> zips = null;
                if (kind == RemovalKind.AllInformation)
                {
                    using (var cancellation = new System.Threading.CancellationTokenSource())
                    {
                        try
                        {
                            zips = await RunWork("삭제할 ZIP 확인", progress => library.RemovalZips(installed.id,
                                new[] { downloadsRoot, Path.Combine(Storage.LegacyRoot, "downloads") }.Distinct(StringComparer.OrdinalIgnoreCase),
                                progress, cancellation.Token), cancellation);
                        }
                        catch (OperationCanceledException)
                        {
                            status.Text = "삭제할 ZIP 확인을 중단했습니다. 파일은 삭제하지 않았습니다.";
                            return;
                        }
                    }
                    using (var warning = new RemovalWarning(library.RemovalSummary(installed.id, zips)))
                        if (warning.ShowDialog(this) != DialogResult.OK) return;
                }
                else if (kind == RemovalKind.CampaignOnly && library.Current().active == installed.id &&
                    library.Current().versionKey == installed.versionKey)
                    if (!AppDialog.Confirm(this, "게임 메뉴로 나온 뒤 삭제하세요.\n선택한 버전의 적용을 해제하고 삭제합니다.\n이전 버전·ZIP·뱅크·세이브는 유지합니다.",
                        "적용 중인 버전 삭제", "해제 후 삭제")) return;
                await RunWork("캠페인 정리", progress => { library.Remove(installed.id, installed.versionKey, kind, kind == RemovalKind.AllInformation, zips); return true; });
                installedView.Reload(catalogue);
                status.Text = kind == RemovalKind.AllInformation ? "전체 보관 버전·확인된 ZIP·연결 정보 삭제 완료 · 뱅크·세이브 유지" :
                    kind == RemovalKind.PreviousVersions ? "이전 버전 삭제 완료 · 선택·최신·적용 버전과 ZIP 유지" :
                    "선택한 버전 삭제 완료 · 다른 버전·ZIP·뱅크·세이브 유지";
            }
            catch (Exception error) { Error(error); }
            finally { SetBusy(false); }
        }
        async void PickVersion(Campaign installed)
        {
            if (busy) return;
            var versions = library.Versions(installed.id);
            if (versions.Count == 0) return;
            using (var dialog = new VersionManagementDialog(installed, versions, selected =>
            {
                try { Process.Start("explorer.exe", "\"" + library.VersionFolder(selected) + "\""); }
                catch (Exception error) { Error(error); }
            }))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK) await ApplyInstalledVersion(installed, dialog.SelectedVersion.versionKey);
            }
        }
        internal void VerifySettingsLayout(string output)
        {
            if (!isolated || online) throw new InvalidOperationException("Settings layout checks require an offline isolated library.");
            bool confirmation = applyConfirmation.Checked, cleanup = autoRemovePreviousVersions.Checked;
            byte[] preference = File.Exists(applyPreferenceFile) ? File.ReadAllBytes(applyPreferenceFile) : null;
            var controls = Descendants(settings).Concat(new Control[] { settings }).ToArray();
            int layouts = 0;
            LayoutEventHandler countLayout = delegate { layouts++; };
            foreach (var control in controls) control.Layout += countLayout;
            try
            {
                foreach (var size in new[] { new Size(980, 760), new Size(1600, 1000) })
                {
                    Size = size; ShowPage(archivePage); PerformLayout(); Application.DoEvents();
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        layouts = 0;
                        var clock = Stopwatch.StartNew();
                        ShowPage(settingsPage); Application.DoEvents();
                        clock.Stop();
                        if (!settingsPage.Visible || Pages().Any(page => page != settingsPage && page.Visible) ||
                            !((MetalButton)navigation.Controls[2]).Primary)
                            throw new InvalidOperationException("Settings navigation must show only the selected page.");
                        if (Descendants(settings).OfType<MetalButton>().Count(button =>
                            button.Text == "기본 캠페인 되돌리기") != 1 ||
                            !Descendants(settings.GetControlFromPosition(0, 3)).OfType<MetalButton>()
                                .Any(button => button.Text == "기본 캠페인 되돌리기") ||
                            Descendants(settings.GetControlFromPosition(0, 2)).OfType<MetalButton>()
                                .Any(button => button.Text == "기본 캠페인 되돌리기"))
                            throw new InvalidOperationException("Base campaign restore belongs in Settings > Updates/Records/Recovery exactly once.");
                        VerifyChildren(settings); TextLayout.VerifyLabels(settings);
                        var bankField = bankFolders;
                        var bankLayout = (TableLayoutPanel)bankField.Parent;
                        var findBanks = bankLayout.GetControlFromPosition(1, 0);
                        if (findBanks.Text != "폴더 찾기" || settings.RowCount != (AppChannel.HasThemedUi ? 6 : 5) ||
                            Descendants(settings).OfType<Label>().Any(label =>
                                label.Text.Contains("미션 상태 자동 감지") ||
                                label.Text.Contains("기존 CCM 설정은 변경하지 않습니다.")) ||
                            bankField.Right > findBanks.Left || findBanks.Right > openBankFolder.Left ||
                            openBankFolder.Right > bankLayout.ClientSize.Width ||
                            bankField.Height != 52 || bankField.Height != downloadPath.Parent.Height)
                            throw new InvalidOperationException("Bank folder actions must fit to the right of the selector without overlap. " +
                                "window=" + size.Width + "x" + size.Height + ", field=" + bankField.Left + ":" + bankField.Right +
                                " h=" + bankField.Height + ", find=" + findBanks.Left + ":" + findBanks.Right +
                                ", open=" + openBankFolder.Left + ":" + openBankFolder.Right +
                                ", line=" + bankLayout.ClientSize.Width + ", downloadHeight=" + downloadPath.Parent.Height);
                        var downloadSection = pathSettings.GetControlFromPosition(0, 1);
                        var storageSection = pathSettings.GetControlFromPosition(0, 2);
                        if (Descendants(settings).Any(control => control.Text == "기본 경로로 되돌리기" ||
                            control.Text == "현재 기본 경로를 사용 중입니다." || control.Text == "계정·프로필별\nBanks") ||
                            !Descendants(storageSection).Any(control => control.Text == "파일 백업 위치"))
                            throw new InvalidOperationException("Removed storage actions and bank subtitle must stay absent; storage caption must fit one line.");
                        if (Descendants(pathSettings.GetControlFromPosition(0, 3)).Any(control =>
                            control.Text == "문서 폴더에서 확인합니다. 캠페인별로 판별하거나 파일을 변경하지 않습니다." ||
                            control.Text == "뱅크 폴더 찾기를 누르면 현재 사용자의 문서 폴더에서 확인합니다."))
                            throw new InvalidOperationException("Removed bank folder instructions must not remain.");
                        if (!Descendants(downloadSection).Contains(downloadPath) ||
                            !Descendants(storageSection).Contains(storagePath) ||
                            !Descendants(pathSettings.GetControlFromPosition(0, 0)).Contains(game) ||
                            !Descendants(pathSettings.GetControlFromPosition(0, 3)).Contains(bankFolders) ||
                            !Descendants(settings.GetControlFromPosition(0, 1)).Contains(pathSettings) ||
                            !Descendants(storageSection).Any(control =>
                                control.Text.Contains("StarCraft II 게임 폴더 안이나 그 하위 폴더로 지정하지 마세요.")))
                            throw new InvalidOperationException("Paths must group game, downloads, storage and banks in order, retaining the game-folder restriction.");
                        foreach (Control section in settings.Controls)
                             if ((section.Dock == DockStyle.Top || section.Dock == DockStyle.Fill) &&
                                 section.Width != settings.ClientSize.Width - settings.Padding.Horizontal - section.Margin.Horizontal)
                                 throw new InvalidOperationException("Settings sections must fill the available width after resizing: " +
                                     section.GetType().Name + " width=" + section.Width + ", content=" + settings.ClientSize.Width + ".");
                        if (applyConfirmation.Checked != confirmation || autoRemovePreviousVersions.Checked != cleanup)
                            throw new InvalidOperationException("Opening settings must not change either apply option.");
                        Console.WriteLine("SETTINGS LAYOUT PASS: " + size.Width + "x" + size.Height + " visit=" + (attempt + 1) +
                            " elapsed_ms=" + clock.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) +
                            " layout_events=" + layouts + "; all settings and labels fit; options unchanged.");
                        if (attempt == 1)
                        {
                            var viewport = (DarkScrollPanel)settings.Parent;
                            var scrollbar = viewport.Controls.OfType<DarkScrollBar>().Single();
                            scrollbar.Value = 0; SaveDesign(Path.Combine(output, "settings-" + size.Width + "-top.png"));
                            scrollbar.Value = scrollbar.Maximum;
                            if (settings.Top != -scrollbar.Value || scrollbar.Maximum <= 0)
                                throw new InvalidOperationException("Settings must remain scrollable through its final section.");
                            if (settings.Height > viewport.ClientSize.Height &&
                                noticesText.Bottom + settings.Padding.Bottom != settings.Height)
                                throw new InvalidOperationException("Settings must end at its credits without an unused scroll tail.");
                            SaveDesign(Path.Combine(output, "settings-" + size.Width + "-bottom.png"));
                            scrollbar.Value = 0;
                            if (settings.Top != 0) throw new InvalidOperationException("Settings scrolling must return to the top.");
                        }
                        ShowPage(archivePage); Application.DoEvents();
                    }
                }
                string originalStatus = bankStatus.Text;
                var originalFont = bankStatus.Font;
                using (var largerFont = new Font(originalFont.FontFamily, originalFont.Size + 2, originalFont.Style))
                {
                    bankStatus.Text = String.Join("\n", Enumerable.Repeat("뱅크 폴더 상태 변경 · 긴 한국어 안내가 줄바꿈되어도 설정 내용이 잘리지 않아야 합니다.", 4));
                    bankStatus.Font = largerFont;
                    ShowPage(settingsPage); Application.DoEvents();
                    VerifyChildren(settings); TextLayout.VerifyLabels(settings);
                    ShowPage(archivePage);
                    bankStatus.Text = originalStatus; bankStatus.Font = originalFont;
                    ShowPage(settingsPage); Application.DoEvents();
                    VerifyChildren(settings); TextLayout.VerifyLabels(settings);
                    ShowPage(archivePage);
                }
                byte[] current = File.Exists(applyPreferenceFile) ? File.ReadAllBytes(applyPreferenceFile) : null;
                if ((preference == null) != (current == null) || (preference != null && !preference.SequenceEqual(current)))
                    throw new InvalidOperationException("Settings navigation must not write application preferences.");
                // Theme-owned storage/update dialogs are rendered from isolated
                // data for each species; do not open or mutate any real folder.
                var previousSkin=LiveTheme.Current;
                try
                {
                    foreach(var skin in LiveTheme.Choices)
                    {
                        LiveTheme.Select(skin);
                        using(var folder=new HangarFolderDialog("캠페인 보관소",
                            "기존 보관소를 연결하거나 빈 폴더를 선택할 수 있습니다.",output))
                        {
                            folder.Show(this);Application.DoEvents();
                            folder.CapturePreview(Path.Combine(output,"folder-"+skin.Id+".png"));
                            if(folder.SelectedPath!=null)
                                throw new InvalidOperationException("Folder preview must never select a destination.");
                            folder.Close();
                        }
                        using(var update=new AppDialog("앱 업데이트",
                            "현재 버전과 신규 버전을 확인합니다. 테스트 화면에서는 변경하지 않습니다."))
                        {
                            update.Show(this);Application.DoEvents();
                            update.CapturePreview(Path.Combine(output,"update-dialog-"+skin.Id+".png"));
                            update.Close();
                        }
                    }
                }
                finally { LiveTheme.Select(previousSkin); }
                Console.WriteLine("THEMED NATIVE DIALOG PASS: four app-owned folder/update prompts rendered without filesystem changes.");
            }
            finally { foreach (var control in controls) control.Layout -= countLayout; }
            Console.WriteLine("SETTINGS CHECK PASS: repeated navigation; narrow/wide full-width layouts; full scrolling; changed text/font reflow; unchanged options and preference file.");
        }
        internal async Task VerifyRemovalZipWork(string fixtureZip)
        {
            if (!isolated || online) throw new InvalidOperationException("ZIP cancellation checks require an offline isolated library.");
            var item = library.Import(fixtureZip, "cancel-zip-check");
            string before = Data.Hash(fixtureZip);
            bool cancelled = false;
            using (var cancellation = new System.Threading.CancellationTokenSource())
            {
                try
                {
                    await RunWork("삭제할 ZIP 확인", progress => library.RemovalZips(item.id, null, value => {
                        progress(value);
                        if (value.bytes && value.completed > 0) cancellation.Cancel();
                    }, cancellation.Token), cancellation);
                }
                catch (OperationCanceledException) { cancelled = true; }
            }
            if (!cancelled || !Enabled || library.Versions(item.id).Count != 1 || Data.Hash(fixtureZip) != before)
                throw new InvalidOperationException("ZIP verification cancellation must propagate, unlock the owner and preserve files.");
            Console.WriteLine("UI PASS: cancelled ZIP read restores the hangar and preserves its campaign and ZIP.");
        }
        Task<bool> ApplyInstalled(Campaign item)
        {
            if (item.kind == "dedicated") return dedicatedLibrary.IsInstalled(item) ? PlayDedicated(item) : InstallDedicated(item);
            return item.kind == "external" ? LaunchExternal(item) : ApplyInstalledVersion(item, item.versionKey);
        }
        public static bool DedicatedAction(string url, out string action, out string id)
        {
            int episode;
            return DedicatedAction(url, out action, out id, out episode);
        }
        public static bool DedicatedAction(string url, out string action, out string id, out int episode)
        {
            action = id = null;
            episode = 0;
            if (!ArchiveView.InternalUrl(url)) return false;
            var match = System.Text.RegularExpressions.Regex.Match(url,
                "^" + System.Text.RegularExpressions.Regex.Escape(Data.Site) + "/hangar.html[?]action=(install|play)&campaign=([a-z0-9][a-z0-9-]{0,99})(?:&episode=([1-3]))?$");
            if (!match.Success) return false;
            action = match.Groups[1].Value; id = match.Groups[2].Value;
            if (match.Groups[3].Success)
            {
                if (action != "play") return false;
                episode = Int32.Parse(match.Groups[3].Value);
            }
            return true;
        }
        internal static AppDialog DedicatedConflictDialog(Campaign item, DedicatedConflict conflict, string action)
        {
            string message = "현재 사용 중인 캠페인 모드: " + conflict.campaignName +
                "\n" + action + "할 전용 캠페인: " + item.name +
                "\n\n두 캠페인이 같은 파일을 사용합니다.\n겹치는 파일: " + conflict.path +
                "\n\n현재 캠페인 모드의 적용을 해제하고 계속 진행할까요?" +
                (action == "실행" ? "\n해제 후 전용 캠페인 파일을 다시 받아 검증·설치한 뒤 실행합니다." : "") +
                "\n받아둔 모드 파일과 저장한 진행 상황은 삭제하지 않습니다.";
            return new AppDialog("현재 캠페인 모드와 파일이 겹칩니다", message, "예", "아니오")
                { ClientSize = new Size(680, 560), MinimumSize = new Size(560, 460) };
        }
        bool ConfirmDedicatedConflict(Campaign item, DedicatedConflict conflict, string action)
        {
            return (bool)Invoke(new Func<bool>(delegate {
                using (var dialog = DedicatedConflictDialog(item, conflict, action))
                    return dialog.ShowDialog(this) == DialogResult.OK;
            }));
        }
        internal static AppDialog DedicatedInstallRemovalDialog(Campaign item, IReadOnlyList<DedicatedConflict> conflicts)
        {
            string blockers = String.Join("\n", conflicts.Select(c => "• " + c.campaignName + "\n   충돌 파일: " + c.path));
            string message = "설치할 캠페인: " + item.name +
                "\n\n다음 전용 캠페인이 동일한 경로에 다른 모드 파일을 사용하여 설치가 막혔습니다.\n" +
                blockers +
                "\n\n위 캠페인을 설치 제거해 미설치 상태로 바꾸고 계속할까요?" +
                "\n원본 ZIP, Bank, Save, 기록 및 다른 캠페인이 공유하는 파일은 보존합니다." +
                "\n새 캠페인 파일은 먼저 검증합니다. 이후 설치가 실패하면 제거한 캠페인은 미설치 상태로 남을 수 있습니다.";
            return new AppDialog("전용 캠페인 설치 충돌", message, "기존 캠페인 설치 제거 후 계속", "취소")
                { ClientSize = new Size(750, 590), MinimumSize = new Size(640, 480) };
        }
        bool ConfirmDedicatedInstallRemoval(Campaign item, IReadOnlyList<DedicatedConflict> conflicts)
        {
            return (bool)Invoke(new Func<bool>(delegate {
                using (var dialog = DedicatedInstallRemovalDialog(item, conflicts))
                    return dialog.ShowDialog(this) == DialogResult.OK;
            }));
        }
        void OpenCreator(Campaign item)
        {
            if (DistributionPolicy.Https(item.creatorUrl)) Process.Start(item.creatorUrl);
        }
        async Task<bool> InstallDedicated(Campaign item)
        {
            if (busy || distributionView.DownloadInProgress) return false;
            try
            {
                if (!DedicatedLibrary.Ready(item)) throw new InvalidOperationException("검증된 한글 완성본이 아직 준비되지 않았습니다.");
                var selected = item;
                if (DedicatedLibrary.HasVoiceOptions(item))
                    using (var dialog = new DedicatedVoiceDialog(dedicatedLibrary.Voice(item) == "en", dedicatedLibrary.IsInstalled(item)))
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                        selected = DedicatedLibrary.VoiceVariant(item, dialog.English);
                    }
                SetBusy(true);
                using (var cancellation = new System.Threading.CancellationTokenSource())
                {
                    string selectedGame = game.Text;
                    await RunWork("전용 캠페인 설치", progress => {
                        dedicatedLibrary.Install(selected, selectedGame, progress, cancellation.Token,
                            conflict => ConfirmDedicatedConflict(item, conflict, "설치"),
                            conflicts => ConfirmDedicatedInstallRemoval(item, conflicts)); return true;
                    }, cancellation);
                }
                installedView.Reveal(catalogue, item);
                status.Text = "전용 캠페인 설치 완료 · " + item.name + " · 실행을 눌러 시작하세요.";
                return true;
            }
            catch (OperationCanceledException) { status.Text = "전용 캠페인 설치를 중단했습니다. 기존 설치는 보존했습니다."; return false; }
            catch (Exception error) { Error(error); return false; }
            finally { SetBusy(false); installedView.Reload(catalogue); }
        }
        async Task<bool> PlayDedicated(Campaign item, int episode = 0)
        {
            if (busy || distributionView.DownloadInProgress) return false;
            try
            {
                ExternalCampaigns.RequireGameClosed(game.Text);
                SetBusy(true);
                string selectedGame = game.Text;
                var start = await RunWork("전용 캠페인 실행 파일 확인", progress => dedicatedLibrary.LaunchInfo(item, selectedGame, episode,
                    conflict => ConfirmDedicatedConflict(item, conflict, "실행"), progress));
                using (var process = Process.Start(start))
                    if (process == null) throw new IOException("스타2 실행 요청을 시작하지 못했습니다.");
                externalSession = item.id; externalProcess = null;
                externalPendingUntil = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency + 120;
                status.Text = "전용 캠페인 실행 요청 완료 · " + item.name;
                return true;
            }
            catch (OperationCanceledException) { status.Text = "전용 캠페인 실행을 취소했습니다."; return false; }
            catch (Exception error) { Error(error); return false; }
            finally { SetBusy(false); installedView.Reload(catalogue); }
        }
        string DedicatedClientState()
        {
            return Data.Json.Serialize(dedicatedLibrary.Current().installed.Select(c => new {
                id = c.id, version = c.version, manifestSha256 = c.manifestSha256, voice = c.release.voice ?? "ko",
                needsUpdate = catalogue.Any(item => item.id == c.id && dedicatedLibrary.NeedsUpdate(item)),
                needsRepair = dedicatedLibrary.NeedsRepair(new Campaign { id = c.id }),
                launches = c.release.launches.Select(launch => launch.label).ToArray()
            }).ToArray());
        }
        async Task<bool> ApplyInstalledVersion(Campaign item, string versionKey, Action<int> applyFault = null)
        {
            if (busy) return false;
            if (!File.Exists(Path.Combine(game.Text, "StarCraft II.exe"))) { ShowPage(settingsPage); status.Text = "게임 경로를 먼저 선택해 주세요."; return false; }
            var selected = library.Versions(item.id).FirstOrDefault(c => c.versionKey == versionKey) ?? item;
            var applied = library.Current();
            if (applied.active==item.id && applied.versionKey==selected.versionKey &&
                String.Equals(applied.gameRoot,game.Text,StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "이미 적용된 버전입니다 · " + selected.name;
                return true;
            }
            if (applyConfirmation.Checked)
                using (var confirmation = new ApplyConfirmation(selected, game.Text))
                {
                    if (confirmation.ShowDialog(this) != DialogResult.OK) return false;
                    if (confirmation.DoNotAskAgain) applyConfirmation.Checked = false;
                }
            string target = game.Text;
            bool removePrevious = autoRemovePreviousVersions.Checked;
            SetBusy(true);
            try
            {
                activity.ObserveTime(null, null, (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
                await RunWork("캠페인 백업·적용", progress => { library.Activate(item.id, target, true, applyFault, versionKey, progress); return true; });
                string cleanupError = null;
                if (removePrevious)
                {
                    try
                    {
                        await RunWork("이전 보관 버전 자동 삭제", progress => {
                            library.Remove(item.id, versionKey, RemovalKind.PreviousVersions); return true;
                        });
                    }
                    catch (Exception error) { cleanupError = error.Message; }
                }
                installedView.Reload(catalogue);
                status.Text = "캠페인 적용 완료 · " + item.name +
                    (cleanupError == null ? "" : " · 이전 버전 자동 삭제 실패");
                if (cleanupError != null)
                {
                    AddJob("캠페인 적용 완료 · 이전 버전 자동 삭제 실패: " + cleanupError);
                    AppDialog.Info(this, "캠페인 적용은 완료했습니다.\n이전 보관 버전을 모두 정리하지 못했습니다.\n\n" + cleanupError,
                        "이전 버전 자동 삭제 실패");
                }
                return true;
            }
            catch (Exception error) { Error(error); return false; }
            finally { SetBusy(false); }
        }
        internal async Task VerifyMetadataImport(Campaign site, string rogueZip, string originalZip, string updateZip)
        {
            if (!isolated || online) throw new InvalidOperationException("Metadata import checks require an offline isolated library.");
            catalogue = new List<Campaign> { site };
            var rogue = library.ImportForSite(rogueZip, site);
            bool prompted = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
            {
                timer.Tick += delegate {
                    var picker = Application.OpenForms.OfType<SitePicker>().FirstOrDefault();
                    if (picker != null) { prompted = true; picker.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                await ImportZipFile(originalZip, null);
                var original = library.All().SingleOrDefault(c => c.sourceAuthor == "fishmaster26");
                if (prompted || original == null || original.id == rogue.id ||
                    library.CatalogueId(original.id) != original.id || library.Versions(rogue.id).Count != 1)
                    throw new InvalidOperationException("Different metadata must register separately without an extra selection prompt.");
                catalogue.Clear();
                await ImportZipFile(updateZip, null);
                if (prompted || library.Versions(original.id).Count != 2)
                    throw new InvalidOperationException("An existing unlinked mod update must not request a thumbnail even with no matching site.");
                catalogue.Add(site);
                await ImportZipFile(originalZip, site);
                if (prompted || library.Versions(original.id).Count != 2 || library.Versions(rogue.id).Count != 1 ||
                    library.CatalogueId(original.id) != original.id || library.Current().active != null ||
                    library.Installed(rogue.id).sourceAuthor != "Shirakami_Fubuki")
                    throw new InvalidOperationException("Updates and duplicate old ZIPs must never merge or rebind different mods.");
            }
            Console.WriteLine("METADATA UI PASS: original and rogue separated without prompts; original updates grouped; old ZIP recognized; no site rebind or game activation.");
        }
        internal async Task VerifyResponsiveApply(string fixtureGame, string[] zips, string output)
        {
            if (!isolated || online) throw new InvalidOperationException("Responsive apply checks require an offline isolated library.");
            var first = library.Import(zips[0], "responsive-apply");
            var second = library.Import(zips[1], first.id);
            library.Import(zips[2], "responsive-other");
            game.Text = fixtureGame; applyConfirmation.Checked = false; autoRemovePreviousVersions.Checked = false;
            byte[] preferences = File.ReadAllBytes(applyPreferenceFile);
            foreach (bool fail in new[] { false, true })
            {
                var selected = fail ? second : first;
                installedView.Reveal(catalogue, selected); ShowPage(installedPage);
                var before = library.Current();
                var gameHashes = Directory.GetFiles(fixtureGame, "*", SearchOption.AllDirectories)
                    .ToDictionary(path => path, Data.Hash);
                using (var release = new System.Threading.ManualResetEventSlim(false))
                using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    var entered = new TaskCompletionSource<bool>();
                    int paused = 0, heartbeats = 0;
                    timer.Tick += delegate {
                        heartbeats++;
                        if (fail)
                        {
                            var error = Application.OpenForms.OfType<AppDialog>().FirstOrDefault(dialog => dialog.Text == "작업 미완료");
                            if (error != null) error.Close();
                        }
                    };
                    timer.Start();
                    var applying = ApplyInstalledVersion(selected, selected.versionKey, step => {
                        if (System.Threading.Interlocked.CompareExchange(ref paused, 1, 0) == 0)
                        {
                            entered.TrySetResult(true); release.Wait();
                            if (fail) throw new IOException("responsive apply rollback fixture");
                        }
                    });
                    bool succeeded = false;
                    Exception browsingFailure = null;
                    try
                    {
                        if (await Task.WhenAny(entered.Task, applying, Task.Delay(15000)) != entered.Task)
                            throw new InvalidOperationException("Fixture apply did not reach the held file transition.");
                        if (!Enabled || !busy || !installedView.Enabled || conflictingControls.Any(control => control.Enabled) ||
                            Application.OpenForms.OfType<WorkDialog>().Count() != 1)
                            throw new InvalidOperationException("Apply must leave the owner usable and only conflicting controls disabled.");
                        for (int visit = 0; visit < 3; visit++)
                        {
                            ((MetalButton)navigation.Controls[2]).PerformClick();
                            if (!settingsPage.Visible) throw new InvalidOperationException("Settings tab is blocked during apply.");
                            var viewport = (DarkScrollPanel)settings.Parent;
                            var scrollbar = viewport.Controls.OfType<DarkScrollBar>().Single();
                            scrollbar.Value = scrollbar.Maximum;
                            if (settings.Top != -scrollbar.Value || scrollbar.Maximum <= 0)
                                throw new InvalidOperationException("Settings must scroll during apply.");
                            scrollbar.Value = 0;
                            ((MetalButton)navigation.Controls[0]).PerformClick();
                            if (!archivePage.Visible) throw new InvalidOperationException("Archive tab is blocked during apply.");
                            ((MetalButton)navigation.Controls[1]).PerformClick();
                            installedView.VerifyBusyBrowsing();
                        }
                        if (await ApplyInstalledVersion(selected, selected.versionKey))
                            throw new InvalidOperationException("A second apply must be rejected.");
                        RemoveInstalled(selected); PickVersion(selected); LinkInstalled(selected);
                        await PickGame(); await SelectGamePath(Path.Combine(output, "must-not-create"));
                        await ChangeStorage(); await Recover();
                        bool importRejected = false;
                        try { await RegisterDownloaded(selected, zips[0]); }
                        catch (InvalidOperationException) { importRejected = true; }
                        if (!importRejected || await RefreshSite() || Directory.Exists(Path.Combine(output, "must-not-create")))
                            throw new InvalidOperationException("Concurrent mutations must remain blocked.");
                        await Task.Delay(100);
                        if (heartbeats == 0 || !preferences.SequenceEqual(File.ReadAllBytes(applyPreferenceFile)))
                            throw new InvalidOperationException("UI messages must continue without changing apply preferences.");
                        ShowPage(settingsPage); SaveDesign(Path.Combine(output, fail ? "busy-failure-settings.png" : "busy-settings.png"));
                    }
                    catch (Exception error) { browsingFailure = error; }
                    finally { release.Set(); }
                    succeeded = await applying;
                    if (browsingFailure != null) throw browsingFailure;
                    if (!Enabled || busy || !installedView.Enabled || Application.OpenForms.OfType<WorkDialog>().Any() ||
                        !applyConfirmation.Enabled || !autoRemovePreviousVersions.Enabled)
                        throw new InvalidOperationException("Work completion must restore controls and remove its progress window.");
                    if (fail)
                    {
                        if (succeeded || library.Current().active != before.active || library.Current().versionKey != before.versionKey ||
                            !gameHashes.All(pair => File.Exists(pair.Key) && Data.Hash(pair.Key) == pair.Value) ||
                            Directory.GetFiles(fixtureGame, "*", SearchOption.AllDirectories).Length != gameHashes.Count)
                            throw new InvalidOperationException("Failed apply must restore the exact prior game and active version.");
                    }
                    else if (!succeeded || library.Current().active != first.id || library.Current().versionKey != first.versionKey)
                        throw new InvalidOperationException("Responsive apply must still commit the selected version.");
                    Console.WriteLine("RESPONSIVE APPLY PASS: " + (fail ? "failure rolled back" : "success committed") +
                        "; tabs/settings scrolling/preview browsing usable; duplicate apply/import/path/storage/recovery blocked; heartbeat=" + heartbeats + ".");
                }
            }
        }
        internal async Task VerifyAutoCleanup(string fixtureGame, string[] zips, string preview)
        {
            if (!isolated || online) throw new InvalidOperationException("Auto-cleanup checks require an offline isolated library.");
            if (autoRemovePreviousVersions.Checked) throw new InvalidOperationException("Automatic cleanup must default off.");
            Data.Write(applyPreferenceFile, new { showConfirmation = false });
            using (var legacy = new Hangar(root, null, false))
                if (legacy.applyConfirmation.Checked || legacy.autoRemovePreviousVersions.Checked)
                    throw new InvalidOperationException("Legacy preferences must preserve confirmation and default cleanup off.");
            var first = library.Import(zips[0], "auto-cleanup");
            var second = library.Import(zips[1], first.id);
            var third = library.Import(zips[2], first.id);
            var other = library.Import(zips[3], "cleanup-other");
            autoRemovePreviousVersions.Checked = true;
            applyConfirmation.Checked = false;
            using (var reopened = new Hangar(root, null, false))
                if (!reopened.autoRemovePreviousVersions.Checked || reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Both apply options must persist independently.");
            if (library.Versions(first.id).Count != 3)
                throw new InvalidOperationException("Enabling automatic cleanup must not delete anything immediately.");
            autoRemovePreviousVersions.Checked = false;
            using (var reopened = new Hangar(root, null, false))
                if (reopened.autoRemovePreviousVersions.Checked || reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Disabling cleanup must preserve the confirmation option.");
            game.Text = fixtureGame;
            if (!await ApplyInstalledVersion(first, first.versionKey) || !await ApplyInstalledVersion(second, second.versionKey) ||
                library.Versions(first.id).Count != 3)
                throw new InvalidOperationException("Cleanup-off applications must preserve all previous versions.");
            Console.WriteLine("UI PASS: cleanup defaults off; legacy settings and independent options persist; enabling does not delete; off preserves versions.");
            autoRemovePreviousVersions.Checked = true;
            applyConfirmation.Checked = true;
            bool cancellationShown = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
            {
                timer.Tick += delegate {
                    var dialog = OwnedForms.OfType<ApplyConfirmation>().FirstOrDefault();
                    if (dialog != null) { cancellationShown = true; dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                if (await ApplyInstalledVersion(third, third.versionKey) || !cancellationShown ||
                    library.Versions(first.id).Count != 3 || library.Current().versionKey != second.versionKey)
                    throw new InvalidOperationException("Cancelled application must preserve all previous versions and the active version.");
            }
            applyConfirmation.Checked = false;
            bool failureShown = false;
            Console.WriteLine("EXPECTED APPLY FAILURE: auto-cleanup fixture transition failure.");
            using (var timer = new System.Windows.Forms.Timer { Interval = 30 })
            {
                timer.Tick += delegate {
                    var dialog = OwnedForms.OfType<AppDialog>().FirstOrDefault(item => item.Text == "작업 미완료");
                    if (dialog != null) { failureShown = dialog.MessageText.Contains("auto-cleanup fixture transition failure"); dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                if (await ApplyInstalledVersion(third, third.versionKey, step => { throw new IOException("auto-cleanup fixture transition failure"); }) ||
                    !failureShown || library.Versions(first.id).Count != 3 || library.Current().versionKey != second.versionKey ||
                    File.ReadAllText(Path.Combine(fixtureGame, @"Maps\Campaign\mission.SC2Map")) != "cleanup-2")
                    throw new InvalidOperationException("A failed transition must roll back without deleting previous versions.");
            }
            if (!await ApplyInstalledVersion(third, third.versionKey) || library.Versions(first.id).Count != 1 ||
                library.Current().versionKey != third.versionKey || library.Installed(first.id).versionKey != third.versionKey ||
                !File.Exists(Path.Combine(library.VersionFolder(third), "manifest.json")) ||
                library.Installed(other.id).versionKey != other.versionKey || library.Versions(other.id).Count != 1 ||
                File.ReadAllText(Path.Combine(fixtureGame, @"Maps\Campaign\mission.SC2Map")) != "cleanup-3")
                throw new InvalidOperationException("Successful cleanup must preserve the applied/latest version, game files and other campaigns.");
            using (var reopened = new Hangar(root, null, false))
                if (!reopened.autoRemovePreviousVersions.Checked || reopened.applyConfirmation.Checked)
                    throw new InvalidOperationException("Automatic cleanup must remain enabled after a successful application.");
            ShowPage(settingsPage); PerformLayout(); Application.DoEvents();
            var surface = autoRemovePreviousVersions.Parent.Parent;
            TextLayout.VerifyLabels(surface);
            if (autoRemovePreviousVersions.Width < TextRenderer.MeasureText(autoRemovePreviousVersions.Text, autoRemovePreviousVersions.Font).Width + 24)
                throw new InvalidOperationException("Automatic cleanup checkbox label is clipped.");
            using (var image = new Bitmap(surface.Width, surface.Height))
            {
                surface.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(preview, System.Drawing.Imaging.ImageFormat.Png);
            }
            Console.WriteLine("UI PASS: cancellation and injected transition failure preserve versions; success recycles only previous versions; current/latest and other campaigns remain.");
        }
        async Task PickGame()
        {
            if (busy || distributionView.DownloadInProgress) return;
            try
            {
                using (var file = new OpenFileDialog { Filter = "StarCraft II.exe|StarCraft II.exe", Title = "게임 실행 파일 선택" })
                    if (file.ShowDialog(this) == DialogResult.OK) await SelectGamePath(Path.GetDirectoryName(file.FileName));
            }
            catch (Exception error) { ShowGamePathFailure(game.Text, error); }
        }
        internal Exception QaGamePathSelectionFailure;
        void ShowGamePathFailure(string selectedGame, Exception error)
        {
            bool denied = Storage.IsAccessFailure(error);
            string message = denied
                ? "게임 경로: " + selectedGame + "\n오류: " + error.Message + "\n\n" +
                    Storage.AccessDeniedGuidance(root, downloadsRoot)
                : Storage.GamePathFailureGuidance(selectedGame, root, downloadsRoot, error);
            status.Text = denied ? "게임 경로 지정 실패 · 폴더 접근 거부" : "게임 경로 지정 실패 · " + error.Message;
            Console.Error.WriteLine("Game path selection failed: " + error);
            if (!IsDisposed)
                AppDialog.Info(this, message, "게임 경로 지정 실패 · 관리자 권한 확인");
        }
        async Task SelectGamePath(string selectedGame)
        {
            if (busy || distributionView.DownloadInProgress) return;
            string destination = null, selectedDownloads = null;
            try
            {
                // Game selection used to move the entire library to a sibling
                // of StarCraft II and create Downloads inside its game folder.
                // Both are commonly protected under Program Files, and moving
                // a live library can fail due to file locks or antivirus scans.
                destination = root;
                selectedDownloads = downloadsRoot;
                if (!AppDialog.Confirm(this, "게임 폴더: " + selectedGame + "\n보관소(그대로 유지): " + destination +
                    "\n다운로드(그대로 유지): " + selectedDownloads +
                    "\n\n게임 경로만 등록합니다. 보관소를 이동하거나 게임 폴더에 다운로드 폴더를 생성하지 않습니다." +
                    "\n원한다면 설정의 '보관소 경로 변경' 또는 '다운로드 경로 변경'을 따로 사용하세요." +
                    "\n프로그램 파일 설치 경로에 맵을 적용할 때는 Windows 권한 승인이 필요할 수 있습니다.",
                    "게임 경로·보관소 설정", "설정 적용")) return;
                SetBusy(true);
                if (refreshing != null) await refreshing;
                await RunWork("게임 실행 경로 설정", progress => {
                    if (isolated && QaGamePathSelectionFailure != null)
                        throw QaGamePathSelectionFailure;
                    Storage.ConnectGameOnly(root, selectedGame, preferenceFile, downloadPreferenceFile, downloadsRoot);
                    return true;
                });
                game.Text = Storage.ResolveGame(preferenceFile, null);
                storagePath.Text = Storage.Resolve(preferenceFile);
                downloadPath.Text = Storage.ResolveDownloads(downloadPreferenceFile, preferenceFile);
                SetBusy(false);
                AppDialog.Info(this, "보관소: " + storagePath.Text + "\n다운로드: " + downloadPath.Text +
                    "\n보관소와 ZIP은 이동하거나 삭제하지 않았습니다." +
                    "\n설정을 반영하기 위해 앱 화면을 다시 엽니다.", "설정 완료");
                ReloadRequested = true; Close();
            }
            catch (Exception error) { ShowGamePathFailure(selectedGame, error); }
            finally { if (!IsDisposed) SetBusy(false); }
        }
        async Task ChangeStorage()
        {
            if (busy || distributionView.DownloadInProgress) return;
            if (!AppDialog.Confirm(this, Storage.RelocationWarning +
                "\n\n기존 보관소를 선택하면 파일 이동 없이 연결합니다.\n빈 폴더를 선택하면 현재 보관소를 이동합니다.\n다운로드 ZIP 위치는 변경하지 않습니다.",
                "보관소 변경 주의", "계속 · 폴더 선택")) return;
            if (isolated) { AppDialog.Info(this, "격리 미리보기에서는 실제 저장소를 변경하지 않습니다.", "보관소 변경"); return; }
            using (var picker = new HangarFolderDialog("캠페인 보관소",
                "기존 보관소는 연결만 합니다. 빈 폴더를 선택하면 안전 확인 후 이동합니다.",root))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                string target = picker.SelectedPath;
                if (Directory.Exists(Path.Combine(target, "packages")))
                {
                    if (!AppDialog.Confirm(this, target + "\n\n기존 캠페인 목록을 확인한 뒤 이 보관소에 연결합니다." +
                        "\n현재·선택한 보관소의 파일을 이동하거나 합치지 않습니다.\n다운로드 ZIP 위치는 유지합니다.",
                        "기존 보관소 연결", "연결")) return;
                    SetBusy(true);
                    try
                    {
                        if (refreshing != null) await refreshing;
                        int count = await Task.Run(() => Storage.ConnectExisting(root, target, preferenceFile));
                        SetBusy(false);
                        AppDialog.Info(this, count + "개 캠페인을 인식했습니다.\n파일 이동 없이 연결했습니다.\n" + target, "보관소 연결 완료");
                        ReloadRequested = true; Close();
                    }
                    catch (Exception error) { Error(error); }
                    finally { if (!IsDisposed) SetBusy(false); }
                    return;
                }
                if (!AppDialog.Confirm(this, target +
                    "\n\n" + Storage.RelocationDescription(root, target) + "\n다운로드 ZIP 위치는 유지합니다.",
                    "새 보관소 확인", "이동 후 변경")) return;
                SetBusy(true);
                try
                {
                    if (refreshing != null) await refreshing;
                    var result = await RunWork("보관소 이동", progress => Storage.Relocate(root, target, preferenceFile, progress));
                    SetBusy(false);
                    AppDialog.Info(this, "보관소 이동 완료.\n" + target + (result.warning == null ? "" : "\n" + result.warning), "보관소 변경 완료");
                    ReloadRequested = true; Close();
                }
                catch (Exception error) { Error(error); }
                finally { if (!IsDisposed) SetBusy(false); }
            }
        }
        void ChangeDownloads()
        {
            if (busy || distributionView.DownloadInProgress) { AppDialog.Info(this, "진행 중인 작업이 끝난 뒤 변경하세요.", "다운로드 경로"); return; }
            if (isolated) { AppDialog.Info(this, "격리 미리보기에서는 다운로드 경로를 변경하지 않습니다.", "다운로드 경로"); return; }
            using (var picker = new HangarFolderDialog("ZIP 다운로드 경로",
                "새 ZIP을 저장할 폴더를 선택하세요. 기존 ZIP과 백업은 이동하지 않습니다.",downloadsRoot))
            {
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    Storage.SelectDownloads(picker.SelectedPath, downloadPreferenceFile, preferenceFile);
                    downloadsRoot = Storage.ResolveDownloads(downloadPreferenceFile, preferenceFile); downloadPath.Text = downloadsRoot;
                    status.Text = "ZIP 다운로드 위치 변경 완료 · 기존 ZIP·교체용 보관소·백업은 유지됩니다.";
                }
                catch (Exception error) { Error(error); }
            }
        }
        async Task Recover()
        {
            if (busy || distributionView.DownloadInProgress) return;
            string path = Path.Combine(root, "transaction", "journal.json");
            string dedicatedPath = Data.Under(root, "dedicated/transaction/journal.json");
            if (!File.Exists(path) && !File.Exists(dedicatedPath)) { AppDialog.Info(this, "복구할 미완료 전환이 없습니다.", "전환 복구"); return; }
            if (!AppDialog.Confirm(this, "미션을 종료한 상태에서 미완료 CCM 전환과 전용 캠페인 설치를 이전 상태로 복구합니다.",
                "전환 복구", "이전 상태 복구")) return;
            SetBusy(true);
            try { await RunWork("이전 적용 상태 복구", progress => { library.Recover(); dedicatedLibrary.Recover(); return true; }); installedView.Reload(catalogue); status.Text = "이전 적용 상태 복구 완료"; }
            catch (Exception error) { Error(error); }
            finally { SetBusy(false); }
        }
        void MarkCampaignViewed(Campaign item)
        {
            if (notificationFeed == null) return;
            try { notificationFeed.MarkCampaignViewed(item.id); notifications.UnreadCount = notificationFeed.UnreadCount; }
            catch (Exception error) { status.Text = "읽음 상태 저장 실패 · " + error.Message; }
        }
        void BuildUpdateOffer()
        {
            // In-app update choice: never create a separate modal/OS window and
            // never automatically download a release found in the background.
            updateOffer.Size = new Size(342, 206);
            updateOffer.BackColor = GlassTheme.Surface;
            updateOffer.Padding = new Padding(18);
            updateOffer.Visible = false;
            updateOffer.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            updateOffer.AccessibleName = "격납고 업데이트 설치 선택";
            updateOffer.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = GlassTheme.Round(new Rectangle(1, 1, updateOffer.Width - 3, updateOffer.Height - 3), 18))
                using (var border = new Pen(GlassTheme.Accent, 1.5f))
                    e.Graphics.DrawPath(border, path);
            };
            updateOffer.SizeChanged += delegate {
                using (var shape = GlassTheme.Round(new Rectangle(0, 0, updateOffer.Width, updateOffer.Height), 18))
                {
                    var old = updateOffer.Region;
                    updateOffer.Region = new Region(shape);
                    if (old != null) old.Dispose();
                }
            };
            var heading = new Label { Text = "격납고 업데이트", Location = new Point(18, 12),
                Size = new Size(306, 30), ForeColor = GlassTheme.Text,
                Font = new Font("맑은 고딕", 12f, FontStyle.Bold),
                BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            updateOfferVersion.Location = new Point(18, 44); updateOfferVersion.Size = new Size(306, 24);
            updateOfferVersion.Font = new Font("맑은 고딕", 10.25f, FontStyle.Bold);
            updateOfferVersion.ForeColor = GlassTheme.Accent; updateOfferVersion.BackColor = Color.Transparent;
            updateOfferNotes.Location = new Point(18, 72); updateOfferNotes.Size = new Size(306, 54);
            updateOfferNotes.ForeColor = GlassTheme.Muted; updateOfferNotes.BackColor = Color.Transparent;
            updateOfferNotes.Font = new Font("맑은 고딕", 9.5f);
            updateOfferInstall.Text = "설치"; updateOfferInstall.Primary = true;
            updateOfferDecline.Text = "안 함";
            foreach (var button in new[] { updateOfferInstall, updateOfferDecline })
            {
                button.Size = new Size(146, 42);
                button.Font = new Font("맑은 고딕", 10.75f, FontStyle.Bold);
                button.CanvasColor = GlassTheme.Surface;
            }
            updateOfferInstall.Location = new Point(18, 148);
            updateOfferDecline.Location = new Point(178, 148);
            updateOfferInstall.Click += async delegate {
                if (availableAppUpdate == null || checkingAppUpdate || appUpdateInProgress || busy) return;
                var release = availableAppUpdate;
                updateOffer.Visible = false;
                await CheckAppUpdate(true, release);
            };
            updateOfferDecline.Click += delegate {
                declinedAppUpdateVersion = availableAppUpdate == null ? null : availableAppUpdate.version;
                if(hubProfileRoot!=null)Data.Write(Path.Combine(hubProfileRoot,"ignored-app-update.json"),declinedAppUpdateVersion);
                updateOffer.Visible = false;
                PushHubUpdateNotice();
            };
            updateOffer.Controls.Add(heading);
            updateOffer.Controls.Add(updateOfferVersion);
            updateOffer.Controls.Add(updateOfferNotes);
            updateOffer.Controls.Add(updateOfferInstall);
            updateOffer.Controls.Add(updateOfferDecline);
            settingsPage.Controls.Add(updateOffer);
            Action position = delegate {
                updateOffer.Left = Math.Max(12, settingsPage.ClientSize.Width - updateOffer.Width - 24);
                updateOffer.Top = 20;
            };
            settingsPage.SizeChanged += delegate { position(); };
            position();
            updateOffer.BringToFront();
        }
        void ShowUpdateOffer(AppRelease release, bool manual)
        {
            availableAppUpdate = release;
            if(hubProfileRoot!=null&&declinedAppUpdateVersion==null&&File.Exists(Path.Combine(hubProfileRoot,"ignored-app-update.json")))
                declinedAppUpdateVersion=Data.Read<string>(Path.Combine(hubProfileRoot,"ignored-app-update.json"));
            if (!manual && declinedAppUpdateVersion == release.version) return;
            updateOfferVersion.Text = "v" + AppUpdate.Current + "  →  v" + release.version;
            updateOfferNotes.Text = String.IsNullOrWhiteSpace(release.notes)
                ? "새 버전을 설치하시겠습니까?" :
                NotificationFeed.Short(release.notes, 140);
            updateOffer.Visible = true;
            updateOffer.BringToFront();
            if (manual) ShowPage(settingsPage);
            if(hubReady) {
                if(manual) PushHubUpdateNotice(true);
                else PushHubUpdateNotice();
            }
        }
        async Task ShowNotifications()
        {
            if (notificationFeed == null) return;
            try
            {
                using (var dialog = new NotificationDialog(notificationFeed.Items))
                {
                    dialog.Shown += delegate {
                        try { notificationFeed.MarkViewed(); notifications.UnreadCount = notificationFeed.UnreadCount; installedView.RefreshBadges(); }
                        catch (Exception error) { status.Text = "알림 읽음 상태 저장 실패 · " + error.Message; }
                    };
                    if (dialog.ShowDialog(this) != DialogResult.OK || dialog.SelectedNotice == null) return;
                    if (dialog.SelectedNotice.kind == "app") { await CheckAppUpdate(true); return; }
                    var site = catalogue.FirstOrDefault(item => String.Equals(item.id, dialog.SelectedNotice.campaignId, StringComparison.OrdinalIgnoreCase));
                    if (site == null) { AppDialog.Info(this, "현재 사이트 목록에서 이 캠페인을 찾을 수 없습니다.", "새 소식"); return; }
                    ShowPage(archivePage); await archiveView.OpenCampaignAsync(site);
                }
            }
            catch (Exception error) { Error(error); }
        }
        async Task CheckAppUpdate(bool manual, AppRelease approved = null)
        {
            bool updateOwnsControls = false;
            if (checkingAppUpdate || appUpdateInProgress) return;
            if ((isolated || !online) && appUpdateFixture == null)
            {
                if (manual) AppDialog.Info(this, "격리 검증에서는 앱 업데이트 통신·설치를 진행하지 않습니다.", "앱 업데이트");
                return;
            }
            if (busy) { if (manual) AppDialog.Info(this, "진행 중인 작업이 끝난 뒤 다시 확인하세요.", "앱 업데이트"); return; }
            checkingAppUpdate = true;
            status.Text = "앱 업데이트 확인 중 · 결과를 확인하고 있습니다.";
            status.Refresh();
            try
            {
                var release = approved ?? await Task.Run(() => appUpdateFixture ?? AppUpdate.Check());
                if (IsDisposed || busy) return;
                if (notificationFeed != null)
                {
                    notificationFeed.ObserveApp(release);
                    notifications.UnreadCount = notificationFeed.UnreadCount;
                }
                if (!AppUpdate.IsNewer(release))
                {
                    availableAppUpdate = null;
                    updateOffer.Visible = false;
                    PushHubUpdateNotice();
                    if (manual) AppDialog.Info(this, "현재 최신 앱 버전입니다. v" + AppUpdate.Current, "앱 업데이트");
                    return;
                }
                if (approved == null) { ShowUpdateOffer(release, manual); return; }
                if (isolated) throw new InvalidOperationException("격리 알림 시험에서는 업데이트 설치를 진행하지 않습니다.");
                // An open editor can contain uncommitted user changes. Do not
                // force-close it as part of the updater's process handoff.
                if (hubEditorWindow != null && !hubEditorWindow.IsDisposed)
                    throw new InvalidOperationException("열려 있는 커스텀 편집창의 변경사항을 저장하거나 취소하고 닫은 뒤 업데이트해 주세요.");
                appUpdateInProgress = true; updateOwnsControls = true; SetBusy(true); Enabled = false;
                string workspace = Path.Combine(Path.GetTempPath(), "hangar-app-update-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(workspace);
                string zip = Path.Combine(workspace, "release.zip");
                status.Text = "앱 업데이트 다운로드·무결성 확인 중";
                await Task.Run(() => AppUpdate.Download(release, zip));
                // Validate every entry and the executable version before asking the old app to exit.
                await Task.Run(() => AppUpdate.Extract(release, zip, Path.Combine(workspace, "preflight")));
                File.WriteAllText(Path.Combine(workspace, "release.json"), new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(release));
                string helper = Path.Combine(workspace, "CampaignHangarUpdater.exe");
                File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CampaignHangarUpdater.exe"), helper, false);
                using (var current = Process.GetCurrentProcess())
                using (var updater = Process.Start(new ProcessStartInfo(helper, current.Id + " \"" +
                    AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + "\" " + current.StartTime.ToUniversalTime().Ticks)
                    { UseShellExecute = false, WorkingDirectory = workspace }))
                {
                    bool ready = await Task.Run(() => {
                        for (int i = 0; i < 100; i++)
                        {
                            if (File.Exists(Path.Combine(workspace, "ready"))) return true;
                            if (updater.HasExited) return false;
                            System.Threading.Thread.Sleep(100);
                        }
                        return false;
                    });
                    if (!ready) throw new IOException("업데이트 프로그램의 시작을 확인하지 못했습니다. 기존 앱은 종료하지 않습니다.");
                }
                // The updater is now waiting for this process to exit. Do not
                // let the busy guard cancel Close(), and release optional
                // editor windows before the handoff starts.
                appUpdateExitApproved = true;
                Enabled = true;
                Close();
                if (!IsDisposed) throw new IOException("업데이트를 위해 기존 격납고를 종료하지 못했습니다.");
            }
            catch (Exception error)
            {
                var network = error as System.Net.WebException;
                var response = network == null ? null : network.Response as System.Net.HttpWebResponse;
                string message = response != null && response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? "아직 공개된 앱 업데이트가 없습니다. 현재 앱은 계속 사용할 수 있습니다."
                    : "앱 업데이트 확인·설치를 완료하지 못했습니다. 현재 앱은 계속 사용할 수 있습니다.\n" + error.Message;
                if (manual || appUpdateInProgress) AppDialog.Info(this, message, "앱 업데이트");
                else status.Text = "앱 업데이트 확인 보류 · 설정에서 다시 확인할 수 있습니다.";
                if (response != null) response.Dispose();
            }
            finally
            {
                checkingAppUpdate = false; appUpdateInProgress = false; appUpdateExitApproved = false;
                if (!IsDisposed && updateOwnsControls) { Enabled = true; SetBusy(false); }
            }
        }
        internal async Task VerifyUpdateNotification(AppRelease release)
        {
            if (!isolated || online || !AppUpdate.IsNewer(release))
                throw new InvalidOperationException("Update notification fixture must be isolated, offline and newer.");
            appUpdateFixture = release;
            await CheckAppUpdate(false);
            if (notifications.UnreadCount == 0 || Application.OpenForms.OfType<AppDialog>().Any() ||
                availableAppUpdate == null || currentPage != archivePage || appUpdateInProgress)
                throw new InvalidOperationException("Background check must remember the release without leaving Archive or installing anything.");
            ShowPage(settingsPage);
            if (!updateOffer.Visible)
                throw new InvalidOperationException("A detected release must appear automatically when Settings opens.");
            ShowPage(archivePage);
            using (var image = new Bitmap(Width, Height))
            { DrawToBitmap(image, new Rectangle(0, 0, Width, Height)); image.Save(Path.Combine(root, "unread-update.png")); }
            bool chosen = false;
            string failure = null;
            int ticks = 0;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    ticks++;
                    var notices = Application.OpenForms.OfType<NotificationDialog>().FirstOrDefault();
                    if (notices != null && !chosen)
                    {
                        chosen = true;
                        notices.ActivateFixtureNotice("app");
                    }
                    if (ticks > 200)
                    {
                        failure = "Notification fixture timed out.";
                        if (notices != null) notices.DialogResult = DialogResult.Cancel;
                    }
                };
                timer.Start();
                await ShowNotifications();
                timer.Stop();
            }
            if (failure != null || !chosen || notifications.UnreadCount != 0 || appUpdateInProgress ||
                currentPage != settingsPage || !updateOffer.Visible ||
                updateOfferInstall.Text != "설치" || updateOfferDecline.Text != "안 함" ||
                !updateOfferVersion.Text.Contains(release.version) || updateOffer.Parent != settingsPage ||
                updateOffer.Left < 0 || updateOffer.Right > settingsPage.ClientSize.Width ||
                settingsPage.Controls.GetChildIndex(updateOffer) != 0 ||
                Application.OpenForms.OfType<AppDialog>().Any())
                throw new InvalidOperationException(failure ?? "The update choice must appear inside Settings, not an OS dialog.");
            using (var image = new Bitmap(Width, Height))
            { DrawToBitmap(image, new Rectangle(0, 0, Width, Height)); image.Save(Path.Combine(root, "update-in-settings.png")); }
            using (var image = new Bitmap(updateOffer.Width, updateOffer.Height))
            { updateOffer.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(Path.Combine(root, "update-offer-panel.png")); }
            Console.WriteLine("IN-APP UPDATE BOUNDS: panel=" + updateOffer.Bounds + " settings=" +
                settingsPage.ClientRectangle + " z-index=" + settingsPage.Controls.GetChildIndex(updateOffer));
            updateOfferDecline.PerformClick();
            if (updateOffer.Visible || declinedAppUpdateVersion != release.version)
                throw new InvalidOperationException("Declining the update must hide the in-app choice.");
            await CheckAppUpdate(false);
            if (updateOffer.Visible || appUpdateInProgress)
                throw new InvalidOperationException("A declined release must not auto-prompt again or start an installer.");
            await CheckAppUpdate(true);
            if (!updateOffer.Visible) throw new InvalidOperationException("Manual update check must reopen the in-app offer.");
            updateOfferDecline.PerformClick();
            Console.WriteLine("UPDATE NOTIFICATION PASS: " + AppChannel.Label + " v" + AppUpdate.Current + " detected v" + release.version +
                "; red bell, Settings in-app offer, Install/Decline actions and dismissal verified; no download, install or game changes.");
            Console.WriteLine("Isolated screenshots: " + root);
            appUpdateFixture = null;
        }
        internal bool QaSuppressErrorDialogs;
        internal int QaErrorCount;
        void Error(Exception error)
        {
            // Previously only the localized error.Message appeared. A
            // sporadic "개체 참조" dialog contained no stack or failing
            // operation, making it impossible to distinguish UI races from
            // file/permission faults. Keep a bounded *local-only* diagnostic
            // with the original stack and current call site. Never upload it.
            string message = error == null ? "알 수 없는 오류" : error.Message;
            bool accessProblem = Storage.IsAccessFailure(error);
            if (accessProblem)
                message = Storage.AccessDeniedGuidance(root, downloadsRoot) + "\n\n상세: " + message;
            try
            {
                string folder = isolated ? Path.Combine(root, "qa-diagnostics") :
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CampaignHangar", "logs");
                Directory.CreateDirectory(folder);
                string log = Path.Combine(folder, "errors-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                if (File.Exists(log) && new FileInfo(log).Length > 1024 * 1024)
                    log = Path.Combine(folder, "errors-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".log");
                File.AppendAllText(log, DateTime.Now.ToString("o") + "\n" +
                    new System.Diagnostics.StackTrace(1, true) + "\n" +
                    (error == null ? "(no exception)" : error.ToString()) + "\n\n", new System.Text.UTF8Encoding(false));
                Console.Error.WriteLine("HANGAR ERROR LOG: " + log + "\n" + error);
            }
            catch (IOException) { Console.Error.WriteLine(error); }
            catch (UnauthorizedAccessException) { Console.Error.WriteLine(error); }
            if (!IsDisposed) status.Text = "작업 미완료 · " + message;
            if (isolated && QaSuppressErrorDialogs)
            {
                QaErrorCount++;
                return; // The QA harness fails with the captured stack, not a modal hang.
            }
            if (!IsDisposed) AppDialog.Info(this, message,
                accessProblem ? "폴더 접근 실패 · 관리자 권한 확인" : "작업 미완료");
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            // Only tear down the WebView/embedded archive after every handler
            // agrees to close. A canceled close used to dispose the owned
            // archive window, then a late archive-rect message raised JIT.
            if (!e.Cancel) BeginHubShutdown();
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); GlassTheme.ApplyTitleBar(Handle); }
        internal async Task VerifyGamePathSwitch()
        {
            if (!isolated || online) throw new InvalidOperationException("Game path switch fixture must be isolated and offline.");
            string fixtureGame = Path.Combine(root + "-selected-game", "StarCraft II");
            Directory.CreateDirectory(fixtureGame);
            File.WriteAllText(Path.Combine(fixtureGame, "StarCraft II.exe"), "fixture");
            Directory.CreateDirectory(Path.Combine(root, "packages"));
            File.WriteAllText(Path.Combine(root, "packages", "keep.txt"), "preserved campaign");
            Directory.CreateDirectory(downloadsRoot);
            File.WriteAllText(Path.Combine(downloadsRoot, "keep.zip"), "preserved download");
            string oldDownloads = downloadsRoot;
            // Reproduce both an ACL/antivirus access denial that was wrapped
            // into an IOException and an ordinary missing-game-path failure.
            // The real error dialog must be visible and must preserve all
            // existing settings and files in either case.
            string oldPreference = File.Exists(preferenceFile) ? File.ReadAllText(preferenceFile) : null;
            for (int failure = 0; failure < 2; failure++)
            {
                bool confirmed = false, warning = false;
                string selected = failure == 0 ? fixtureGame : Path.Combine(root, "no-such-sc2");
                QaGamePathSelectionFailure = failure == 0
                    ? (Exception)new IOException("임시 등록 폴더 접근 거부",
                        new UnauthorizedAccessException("보호된 폴더 접근 불가"))
                    : null;
                try
                {
                    using (var warningTimer = new System.Windows.Forms.Timer { Interval = 50 })
                    {
                        warningTimer.Tick += delegate
                        {
                            var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault();
                            if (dialog == null) return;
                            if (dialog.Text == "게임 경로·보관소 설정")
                            {
                                confirmed = true;
                                dialog.DialogResult = DialogResult.OK;
                            }
                            else if (dialog.Text == "게임 경로 지정 실패 · 관리자 권한 확인")
                            {
                                warning = dialog.MessageText.Contains("관리자 권한으로 실행") &&
                                    dialog.MessageText.Contains("백신") &&
                                    dialog.MessageText.Contains("즉시 다시 켜세요") &&
                                    dialog.MessageText.Contains("StarCraft II.exe") == (failure == 1);
                                dialog.VerifyFixture();
                                dialog.DialogResult = DialogResult.Cancel;
                            }
                            else throw new InvalidOperationException("Unexpected path-failure QA dialog: " + dialog.Text);
                        };
                        warningTimer.Start();
                        await SelectGamePath(selected);
                        warningTimer.Stop();
                    }
                }
                finally { QaGamePathSelectionFailure = null; }
                if (!confirmed || !warning ||
                    (File.Exists(preferenceFile) ? File.ReadAllText(preferenceFile) : null) != oldPreference ||
                    File.ReadAllText(Path.Combine(root, "packages", "keep.txt")) != "preserved campaign" ||
                    File.ReadAllText(Path.Combine(oldDownloads, "keep.zip")) != "preserved download" ||
                    ReloadRequested)
                    throw new InvalidOperationException("Failed game selection must show admin/AV advice while preserving all paths/data. mode=" + failure);
            }
            Console.WriteLine("UI PASS: access denied and missing SC2 path both show actual admin/AV guidance without changing storage or downloads.");
            bool accepted = false, complete = false, refreshed = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    if (dialog.Text == "게임 경로·보관소 설정")
                    { accepted = true; dialog.DialogResult = DialogResult.OK; }
                    else if (dialog.Text == "설정 완료")
                    {
                        complete = true;
                        refreshed = game.Text == fixtureGame && storagePath.Text == root &&
                            downloadPath.Text == oldDownloads;
                        dialog.DialogResult = DialogResult.OK;
                    }
                    else { Console.Error.WriteLine("Game switch dialog: " + dialog.Text + " · " + dialog.MessageText); dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start();
                await SelectGamePath(fixtureGame);
                timer.Stop();
            }
            string expectedRoot = root;
            string expectedDownloads = oldDownloads;
            if (!accepted || !complete || !refreshed || !ReloadRequested ||
                Storage.Resolve(preferenceFile) != expectedRoot ||
                Storage.ResolveDownloads(downloadPreferenceFile, preferenceFile) != expectedDownloads ||
                File.ReadAllText(Path.Combine(expectedRoot, "packages", "keep.txt")) != "preserved campaign" ||
                File.ReadAllText(Path.Combine(oldDownloads, "keep.zip")) != "preserved download")
                throw new InvalidOperationException("Game selection must keep storage and downloads paths while persisting the game and reloading. " +
                    "accepted=" + accepted + "; complete=" + complete + "; reload=" + ReloadRequested + "; " + status.Text);
            if (Directory.Exists(Path.Combine(fixtureGame, "Downloads")) ||
                Directory.Exists(Storage.DefaultForGame(fixtureGame)))
                throw new InvalidOperationException("Game path selection must not create folders inside or alongside a protected game installation.");
            Console.WriteLine("UI PASS: game-only setup preserves live library and downloads, original ZIP and game files; no relocation or admin rights required.");
        }
        internal void VerifyLayout()
        {
            string oldGame = game.Text;
            string fixtureGame = Path.Combine(root + "-game-path-fixture", "StarCraft II");
            Directory.CreateDirectory(fixtureGame);
            File.WriteAllText(Path.Combine(fixtureGame, "StarCraft II.exe"), "fixture");
            game.Text = fixtureGame;
            bool gameSetupSeen = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var dialog = Application.OpenForms.OfType<AppDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    timer.Stop();
                    gameSetupSeen = dialog.Text == "게임 경로·보관소 설정" &&
                        dialog.MessageText.Contains(root) &&
                        dialog.MessageText.Contains("그대로 유지");
                    try { dialog.VerifyFixture(); } finally { dialog.DialogResult = DialogResult.Cancel; }
                };
                timer.Start(); SelectGamePath(fixtureGame).GetAwaiter().GetResult(); timer.Stop();
            }
            if (!gameSetupSeen || storagePath.Text != root || downloadPath.Text != downloadsRoot)
                throw new InvalidOperationException("Game selection must preserve the existing storage and downloads paths on cancel.");
            Console.WriteLine("UI PASS: game path selection previews preservation of existing library/downloads, and cancel changes nothing.");
            if (storagePath.Text != root || downloadPath.Text != downloadsRoot ||
                !Descendants(settings).OfType<Button>().Any(b => b.Text == "다운로드 경로 변경") ||
                !Descendants(settings).OfType<Button>().Any(b => b.Text == "보관소 경로 변경") ||
                Descendants(settings).OfType<Button>().Any(b => b.Text == "기본 경로로 되돌리기"))
                throw new InvalidOperationException("Download and library settings must remain separate.");
            Console.WriteLine("UI PASS: separate ZIP download and campaign storage fields and actions.");
            var downloadField = pathSettings.GetControlFromPosition(0, 1);
            if (!Descendants(downloadField).Contains(downloadPath) ||
                !Descendants(downloadField).Any(c => c.Text == "다운로드 경로"))
                throw new InvalidOperationException("Download path must appear directly below game path.");
            using (var dialog = new AppDialog("이번 실행의 기록", "기록 예시\n여러 줄 기록"))
            { dialog.Show(); dialog.VerifyFixture(); dialog.Close(); }
            bool warningSeen = false;
            using (var timer = new System.Windows.Forms.Timer { Interval = 50 })
            {
                timer.Tick += delegate {
                    var warning = Application.OpenForms.OfType<AppDialog>().FirstOrDefault();
                    if (warning != null)
                    {
                        timer.Stop();
                        warningSeen = warning.Text == "보관소 변경 주의" && warning.MessageText.Contains(Storage.RelocationWarning);
                        try { warning.VerifyFixture(); } finally { warning.DialogResult = DialogResult.Cancel; }
                    }
                };
                timer.Start(); ChangeStorage().GetAwaiter().GetResult(); timer.Stop();
            }
            if (!warningSeen || storagePath.Text != root) throw new InvalidOperationException("Storage change must warn before any folder selection and cancel without changes.");
            Console.WriteLine("UI PASS: download settings below game; storage warning appears before picker and cancellation changes nothing.");
            game.Text = oldGame;
            archiveView.VerifyToolbar();
            ShowPage(installedPage); ShowPage(settingsPage);
            var backFilter = new BackMouseFilter(this, GoBack);
            var message = Message.Create(Handle, 0x020C, new IntPtr(1 << 16), IntPtr.Zero);
            if (!backFilter.PreFilterMessage(ref message) || currentPage != installedPage)
                throw new InvalidOperationException("Mouse button 4 must return to previous app page.");
            Console.WriteLine("UI PASS: mouse XButton1 returns to previous app page.");
            if (navigation.Controls.Count != 3 || String.Join(",", navigation.Controls.Cast<Control>().Select(c => c.Text)) != "아카이브,내 캠페인,설정")
                throw new InvalidOperationException("Final navigation still contains duplicate campaign catalogue.");
            string bankDocuments = Path.Combine(root, "bank-ui-fixture");
            RefreshBankFolders(bankDocuments);
            if (bankFolders.Enabled || openBankFolder.Enabled || Directory.Exists(bankDocuments))
                throw new InvalidOperationException("Missing banks must disable opening without creating folders.");
            string bankOne = Path.Combine(bankDocuments, @"StarCraft II\Accounts\one\profile\Banks");
            string bankTwo = Path.Combine(bankDocuments, @"StarCraft II\Accounts\two\profile\Banks");
            Directory.CreateDirectory(bankOne); Directory.CreateDirectory(bankTwo);
            RefreshBankFolders(bankDocuments);
            bankFolders.SelectedIndex = 1; RefreshBankFolders(bankDocuments);
            if (!bankFolders.Enabled || !openBankFolder.Enabled || bankFolders.Items.Count != 2 ||
                (string)bankFolders.SelectedItem != bankTwo)
                throw new InvalidOperationException("Bank folder selection must cover every profile and survive refresh.");
            Console.WriteLine("UI PASS: bank settings distinguish missing folders, select multiple profiles and preserve selection without opening Explorer.");
            foreach (var size in new[] { new Size(980, 760), new Size(1600, 1000) })
            {
                Size = size; ShowPage(settingsPage); PerformLayout();
                foreach (Control button in navigation.Controls)
                    if (TextRenderer.MeasureText(button.Text, button.Font).Width > button.Width - 18 || button.Right > navigation.Width) throw new InvalidOperationException("Navigation clipped.");
                VerifyChildren(settings);
                TextLayout.VerifyLabels(settings);
                if (maintenanceButtons.Count != 5 || maintenanceButtons.Select(button => button.Size).Distinct().Count() != 1 ||
                    noticesText.Parent != settings || settings.GetPositionFromControl(noticesText).Row != settings.RowCount - 1 ||
                    noticesText.TextAlign != ContentAlignment.MiddleRight || noticesText.Font.Size >= Font.Size ||
                    Descendants(this).OfType<Button>().Any(button => button.Text == "앱 정보·저작권 안내"))
                    throw new InvalidOperationException("Maintenance buttons must share largest size; small notices must be plain text at settings bottom-right only.");
                foreach (var panel in settings.Controls.OfType<MetalPanel>())
                {
                    if (panel.Padding != new Padding(24) || panel.Width < settings.ClientSize.Width - 4)
                        throw new InvalidOperationException("Settings cards must fill width with approved 24px padding.");
                    foreach (var button in Descendants(panel).OfType<MetalButton>())
                        if (!button.BlendWithSurface || button.Height < 48)
                            throw new InvalidOperationException("Settings button has a separate opaque background or small target.");
                }
                ShowPage(installedPage); PerformLayout();
                Console.WriteLine("UI PASS: three navigation tabs and complete settings at " + ClientSize);
            }
            if (noticesText.Visible) throw new InvalidOperationException("Copyright text must not remain visible outside settings.");
            Console.WriteLine("UI PASS: four maintenance buttons share largest size; small plain copyright text appears only at settings bottom-right, with no global button.");
            var originalFonts = navigation.Controls.Cast<Control>().Select(button => button.Font).ToArray();
            using (var enlarged = new Font(Font.FontFamily, 16, FontStyle.Bold))
            {
                foreach (Control button in navigation.Controls) button.Font = enlarged;
                Size = new Size(980, 760); PerformLayout();
                foreach (Control button in navigation.Controls)
                    if (TextLayout.Measure(button, button.Text, Int32.MaxValue).Width > button.Width - 18 || button.Right > navigation.ClientSize.Width)
                        throw new InvalidOperationException("Enlarged navigation text clipped.");
                for (int i = 0; i < originalFonts.Length; i++) navigation.Controls[i].Font = originalFonts[i];
            }
            Console.WriteLine("UI PASS: navigation button and containing column widths follow full labels at enlarged font size.");
        }
        static void VerifyChildren(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                if (child.Right > parent.ClientSize.Width || child.Bottom > parent.ClientSize.Height)
                    throw new InvalidOperationException("Settings clipped: " + child.GetType().Name + " " + child.Text + " " + child.Bounds + " in " + parent.GetType().Name + " " + parent.ClientSize);
                VerifyChildren(child);
            }
        }
        static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (Control descendant in Descendants(child)) yield return descendant;
            }
        }
        internal void CaptureDesign(string output)
        {
            Text = "격리 디자인 검증 · 실제 게임과 연결되지 않은 예시";
            archiveView.CaptureToolbar(Path.Combine(output, "archive-toolbar.png"));
            ClientSize = new Size(1120, 960); ShowPage(installedPage); PerformLayout(); SaveDesign(Path.Combine(output, "installed.png"));
            installedView.CaptureManagement(Path.Combine(output, "management.png"));
            ShowPage(settingsPage); PerformLayout(); SaveDesign(Path.Combine(output, "settings.png")); ShowPage(installedPage);
        }
        void SaveDesign(string path)
        {
            using (var image = new Bitmap(ClientSize.Width, ClientSize.Height))
            { Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, Controls[0].Size)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
            Console.WriteLine("Native design render: " + path);
        }
    }
}
