using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    public static class InstalledSelection
    {
        public static RectangleF FitDestination(Size image, Size frame)
        {
            float scale = Math.Min((float)frame.Width / image.Width, (float)frame.Height / image.Height);
            float width = image.Width * scale, height = image.Height * scale;
            return new RectangleF((frame.Width - width) / 2, (frame.Height - height) / 2, width, height);
        }
        public static readonly string[] Slots = { "wol", "hots", "lotv", "nco" };
        public static readonly string[] Titles = { "자날", "군심", "공유", "노바" };
        public static readonly string[] Categories = { "gallery", "translated", "rogue", "untranslated", "uncategorized" };
        public static readonly string[] CategoryTitles = { "갤산", "한글 번역", "로그라이크", "미번역", "미분류" };
        public const string ApplyHelp = "캠페인을 종료하고 나와서 적용 눌러주세요";
        public static string Category(Campaign item)
        {
            var values = item == null ? null : item.categories;
            if (values != null)
                foreach (var key in Categories.Take(4))
                    if (values.Any(value => String.Equals(value, key, StringComparison.OrdinalIgnoreCase))) return key;
            // Unlinked ZIPs cannot be assumed to be untranslated. Keep them
            // visible in a separate category rather than inventing a language.
            return "uncategorized";
        }
        public static List<string> AvailableCategories(IEnumerable<Campaign> items, string slot)
        {
            var present = new HashSet<string>(items.Where(item => item.slot == slot).Select(Category));
            return Categories.Where(present.Contains).ToList();
        }
        public static string DisplayName(Campaign item)
        {
            if (item == null) return "";
            string label = item.name ?? "";
            return label.Contains("매스리콜") && (label.IndexOf("SCEvo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                label.IndexOf("SC Evo", StringComparison.OrdinalIgnoreCase) >= 0)
                ? "매스리콜 SC Evo" : label;
        }
        public static bool SameDedicatedCampaign(Campaign dedicated, Campaign linked)
        {
            if (dedicated == null || linked == null || dedicated.kind != "dedicated" || linked.kind != "external") return false;
            return (dedicated.id == "antioch-chronicles" && (linked.name ?? "").Contains("안티오크 연대기")) ||
                (dedicated.id == "sc-evo" && DisplayName(linked) == "매스리콜 SC Evo") ||
                (dedicated.id == "the-dark-story" &&
                 (linked.name ?? "").IndexOf("The Dark Story", StringComparison.OrdinalIgnoreCase) >= 0);
        }
        public static IEnumerable<Campaign> UniqueDedicated(IEnumerable<Campaign> dedicated, IEnumerable<Campaign> links)
        {
            var personal = links.ToList();
            return dedicated.Where(item => !personal.Any(link => SameDedicatedCampaign(item, link)));
        }
        public static List<Campaign> Shelf(IEnumerable<Campaign> items, string slot, string preview, State state,
            Func<string, double> playSeconds = null)
        {
            // Keep the current/previous applied campaign easy to find. Sort the
            // remaining campaigns by recorded (unrounded) time, not display text.
            string first = preview == state.active ? state.previousActive : state.active;
            return items.Where(c => c.slot == slot && c.id != preview)
                .OrderBy(c => c.id == first ? 0 : 1)
                .ThenByDescending(c => SafePlaySeconds(playSeconds, c.id))
                .ThenBy(c => c.name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        static double SafePlaySeconds(Func<string, double> source, string id)
        {
            if (source == null) return 0;
            double seconds = source(id);
            return Double.IsNaN(seconds) || Double.IsInfinity(seconds) || seconds < 0 ? 0 : seconds;
        }
        public static Campaign ActivePackage(Library library, State state)
        {
            return state.active == null ? null : library.Versions(state.active).FirstOrDefault(c => c.versionKey == state.versionKey);
        }
    }

    public sealed class InstalledView : UserControl
    {
        const int CreditCaptionWidth = 120;
        readonly Library library;
        readonly Func<Campaign, Task<Image>> thumbnail;
        readonly Func<Campaign, Task<bool>> apply;
        readonly Func<IEnumerable<Campaign>> external;
        readonly Action addExternal;
        readonly Action<Campaign> editExternal;
        readonly Action<Campaign> showRecords;
        readonly Func<string, string> recordSummary;
        readonly Func<string, double> playSeconds;
        readonly Func<Campaign, bool> dedicatedInstalled, dedicatedUpdate, dedicatedRepair;
        readonly Func<Campaign, string> dedicatedVersion;
        readonly Func<Campaign, bool> isNewCampaign;
        readonly Action<Campaign> campaignViewed;
        readonly Action<Campaign> changeVoice, openCreator;
        readonly Func<Campaign, string> voiceLabel;
        readonly Label evoDeveloper = Label("", 11.25f);
        readonly Label recordText = Label("", 10.25f);
        readonly FlowLayoutPanel tabs = new FlowLayoutPanel(), shelf = new FlowLayoutPanel();
        readonly RowStyle mainHeaderHeight = new RowStyle(SizeType.Absolute, 100);
        readonly CampaignArtwork artwork = new CampaignArtwork();
        readonly Label mode = Label("", 9.75f, true), name = Label("", 20.25f, true), author = Label("", 11.25f),
            translator = Label("", 11.25f),
            version = Label("", 11.25f), latestVersion = Label("", 11.25f), help = Label(InstalledSelection.ApplyHelp, 9),
            empty = Label("설치된 캠페인이 없습니다.\n아카이브에서 받거나 ZIP을 가져와 주세요.", 15);
        readonly MetalButton applyButton = new MetalButton();
        readonly MetalButton removeButton = new MetalButton();
        readonly MetalButton updateButton = new MetalButton();
        readonly MetalButton voiceButton = new MetalButton(), creatorButton = new MetalButton(), translatorSiteButton = new MetalButton();
        readonly MetalButton manageButton = new MetalButton();
        readonly MetalButton importButton = new MetalButton();
        readonly ContextMenuStrip episodePicker = new ContextMenuStrip();
        readonly Func<Campaign, int, Task<bool>> launchEpisode;
        readonly ContextMenuStrip management = new ContextMenuStrip();
        readonly ToolStripItem authorLauncherAction;
        readonly TableLayoutPanel hero = Table(2, 1);
        readonly TableLayoutPanel campaignLayout;
        readonly TableLayoutPanel credits = Table(1, 3);
        readonly Control translationFact;
        readonly Control originalFact, evoFact;
        readonly DarkScrollPanel shelfScroll, pageScroll;
        readonly List<InstalledTile> cards = new List<InstalledTile>();
        readonly List<Panel> shelfSections = new List<Panel>();
        List<Campaign> installed = new List<Campaign>(), catalogue = new List<Campaign>();
        State state = new State();
        string slot = "wol";
        string renderedSlot;
        Campaign preview;
        bool busy, rendering, compositingTransition;
        int imageGeneration;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Compose only a transition; permanent composition slows nested scrolling.
                if (compositingTransition) parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return parameters;
            }
        }
        internal void CaptureManagement(string output)
        {
            management.Show(manageButton, new Point(0, manageButton.Height));
            using (var image = new Bitmap(management.Width, management.Height))
            { management.DrawToBitmap(image, new Rectangle(Point.Empty, management.Size)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            management.Close();
            Console.WriteLine("Native management menu render: " + output);
        }
        internal void VerifyArtworkClick()
        {
            artwork.Image = null;
            typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(artwork, new object[] { EventArgs.Empty });
        }
        internal void VerifyRemovalClick()
        {
            if (!removeButton.Visible || !removeButton.Enabled || removeButton.Parent.Parent.Parent != hero)
                throw new InvalidOperationException("Removal must be visible directly inside the large campaign detail panel.");
            removeButton.PerformClick();
        }
        static Label Label(string text, float size, bool bold = false)
        {
            return new Label { Text = text, Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = GlassTheme.Text, BackColor = Color.Transparent, Dock = DockStyle.Fill,
                AutoEllipsis = false, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft };
        }
        static TableLayoutPanel Table(int columns, int rows)
        {
            return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows,
                BackColor = GlassTheme.Background, Margin = Padding.Empty, Padding = Padding.Empty };
        }
        public InstalledView(Library library, Func<Campaign, Task<Image>> thumbnail, Func<Campaign, Task<bool>> apply, Action import,
            Action<Campaign> versions = null, Action<Campaign> link = null, Action<Campaign> update = null, Action<Campaign> remove = null, Action<Campaign> information = null,
            Func<IEnumerable<Campaign>> external = null, Action addExternal = null, Action<Campaign> editExternal = null,
            Action<Campaign> showRecords = null, Func<string, string> recordSummary = null, Action restore = null,
            Action<Campaign> openAuthorLauncher = null, Func<Campaign, int, Task<bool>> launchEpisode = null,
            Func<Campaign, bool> dedicatedInstalled = null, Func<Campaign, bool> dedicatedUpdate = null,
            Func<Campaign, string> dedicatedVersion = null, Func<Campaign, bool> isNewCampaign = null,
            Action<Campaign> campaignViewed = null, Action<Campaign> changeVoice = null,
            Func<Campaign, string> voiceLabel = null, Action<Campaign> openCreator = null,
            Func<Campaign, bool> dedicatedRepair = null, Func<string, double> playSeconds = null)
        {
            this.library = library; this.thumbnail = thumbnail; this.apply = apply;
            this.external = external; this.addExternal = addExternal; this.editExternal = editExternal;
            this.showRecords = showRecords; this.recordSummary = recordSummary; this.playSeconds = playSeconds;
            this.dedicatedInstalled = dedicatedInstalled; this.dedicatedUpdate = dedicatedUpdate; this.dedicatedVersion = dedicatedVersion;
            this.dedicatedRepair = dedicatedRepair;
            this.launchEpisode = launchEpisode;
            this.isNewCampaign = isNewCampaign; this.campaignViewed = campaignViewed;
            this.changeVoice = changeVoice; this.voiceLabel = voiceLabel; this.openCreator = openCreator;
            AutoScaleMode = AutoScaleMode.None; BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text;
            Font = new Font("맑은 고딕", 12.5f); Dock = DockStyle.Fill;
            var layout = Table(1, 4); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            campaignLayout = layout;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 500));
            // Category/installed section bars already explain the card shelf;
            // there is no need for a second, 70px "other campaigns" caption.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Height = 810;
            // Keep drag access to the page's scrollbar. Render a single bitmap
            // during its gesture instead of moving every native child control
            // on each pointer/wheel step (the old path flickers under load).
            pageScroll = new DarkScrollPanel(layout) {
                Dock = DockStyle.Fill, HideUnusedScrollBar = true, SnapshotGestureScroll = true };
            var pageLayout = Table(1, 2);
            pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pageLayout.RowStyles.Add(mainHeaderHeight);
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pageLayout.Controls.Add(pageScroll, 0, 1); Controls.Add(pageLayout);
            SizeChanged += delegate { SyncHeaderHeight(); layout.Height = Math.Max(740, ClientSize.Height - 100); pageScroll.ArrangeContent(); };
            var toolbar = Table(2, 1); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tabs.Dock = DockStyle.Fill; tabs.WrapContents = external != null; tabs.BackColor = BackColor;
            for (int i = 0; i < InstalledSelection.Slots.Length; i++)
            {
                string key = InstalledSelection.Slots[i];
                var icon = Image.FromFile(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "campaign-" + key + ".png"));
                var tab = new MetalButton { Appearance = ButtonAppearance.Tab, Text = InstalledSelection.Titles[i], Tag = key, LeadingIcon = icon,
                    AutoSize = true, MinimumSize = new Size(112, 64), Margin = new Padding(0, 10, 10, 0), Font = new Font("맑은 고딕", 12.5f, FontStyle.Bold) };
                tab.Disposed += delegate { icon.Dispose(); };
                tab.Click += delegate { slot = key; preview = null; pageScroll.ResetPosition(); Render(); };
                tabs.Controls.Add(tab);
            }
            if (external != null)
            {
                var mapIcon = ExternalCampaigns.MapIcon();
                var tab = new MetalButton { Appearance = ButtonAppearance.Tab, Text = "전용 캠페인", Tag = "external", LeadingIcon = mapIcon,
                    AutoSize = true, MinimumSize = new Size(140, 64), Margin = new Padding(0, 10, 10, 0), Font = new Font("맑은 고딕", 12.5f, FontStyle.Bold) };
                tab.Disposed += delegate { mapIcon.Dispose(); };
                tab.Click += delegate { slot = "external"; preview = null; pageScroll.ResetPosition(); Render(); };
                tabs.Controls.Add(tab);
            }
            toolbar.Controls.Add(tabs, 0, 0);
            var zip = importButton;
            zip.Text = "ZIP 가져오기"; zip.Dock = DockStyle.Top; zip.Height = 56;
            zip.Font = new Font(Font, FontStyle.Bold); zip.Margin = new Padding(0, 3, 0, 0);
            zip.Click += delegate { if (!busy) { if (slot == "external" && this.addExternal != null) this.addExternal(); else import(); } };
            var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            zip.Dock = DockStyle.None; zip.Width = 200;
            manageButton.Text = "관리"; manageButton.Size = new Size(110, 56); manageButton.Font = new Font(Font, FontStyle.Bold); manageButton.Margin = new Padding(10, 3, 0, 0);
            management.Renderer = new GlassMenuRenderer(); management.BackColor = GlassTheme.Surface;
            management.ForeColor = GlassTheme.Text; management.Font = new Font(Font.FontFamily, 11.25f, FontStyle.Bold); management.ShowImageMargin = false; management.Padding = new Padding(10);
            management.Items.Add("버전관리·폴더관리", null, delegate {
                if (preview != null && preview.kind == "external" && this.editExternal != null) this.editExternal(preview);
                else if (preview != null && versions != null) versions(preview);
            });
            management.Items.Add("사이트 캠페인 연결", null, delegate { if (preview != null && link != null) link(preview); });
            management.Items.Add("캠페인·사이트 정보", null, delegate { if (preview != null && information != null) information(preview); });
            string[] menuIcons = { "history", "link", "info" };
            for (int i = 0; i < management.Items.Count; i++)
            { var item = management.Items[i]; item.Padding = Padding.Empty; item.ForeColor = GlassTheme.Text; item.Tag = menuIcons[i]; }
            if (showRecords != null)
            {
                var records = management.Items.Add("플레이 기록·뱅크 연결", null, delegate { if (preview != null && !busy) showRecords(preview); });
                records.Tag = "bank"; records.ForeColor = GlassTheme.Text;
            }
            if (openAuthorLauncher != null)
            {
                authorLauncherAction = management.Items.Add("제작자 런처 열기", null,
                    delegate { if (!busy && preview != null) openAuthorLauncher(preview); });
                authorLauncherAction.Tag = "link"; authorLauncherAction.ForeColor = GlassTheme.Text;
            }
            episodePicker.Renderer = management.Renderer;
            episodePicker.BackColor = GlassTheme.Surface;
            episodePicker.ForeColor = GlassTheme.Text;
            episodePicker.Font = management.Font;
            management.SizeChanged += delegate
            {
                using (var path = GlassTheme.Round(new Rectangle(0, 0, management.Width, management.Height), 16))
                { var previous = management.Region; management.Region = new Region(path); if (previous != null) previous.Dispose(); }
            };
            management.Opened += delegate
            {
                using (var graphics = management.CreateGraphics())
                {
                    var visibleItems = management.Items.Cast<ToolStripItem>().Where(item => item.Available).ToArray();
                    var sizes = visibleItems.Select(item => TextRenderer.MeasureText(graphics, item.Text, item.Font,
                        new Size(Int32.MaxValue, Int32.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding)).ToList();
                    int width = sizes.Max(size => size.Width) + 88;
                    for (int i = 0; i < visibleItems.Length; i++)
                    { visibleItems[i].AutoSize = false; visibleItems[i].Size = new Size(width, Math.Max(58, sizes[i].Height + 26)); }
                    management.AutoSize = false;
                    management.Size = new Size(width + management.Padding.Horizontal,
                        visibleItems.Sum(item => item.Height + item.Margin.Vertical) + management.Padding.Vertical);
                    management.PerformLayout();
                    foreach (ToolStripItem item in visibleItems)
                        if (TextRenderer.MeasureText(graphics, item.Text, item.Font).Width > item.Width - 72)
                            throw new InvalidOperationException("Management menu text clipped.");
                }
            };
            manageButton.Click += delegate { management.Show(manageButton, new Point(0, manageButton.Height)); };
            Disposed += delegate { management.Dispose(); episodePicker.Dispose(); };
            tools.Controls.Add(zip); tools.Controls.Add(manageButton); toolbar.Controls.Add(tools, 1, 0);
            pageLayout.Controls.Add(toolbar, 0, 0);
            hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 660)); hero.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
            hero.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); hero.Padding = new Padding(2);
            hero.Paint += delegate(object sender, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = GlassTheme.Round(new Rectangle(1, 1, hero.Width - 3, hero.Height - 3), 20))
                using (var border = new Pen(Color.FromArgb(43, 255, 255, 255))) e.Graphics.DrawPath(border, path);
            };
            hero.SizeChanged += delegate
            {
                if (hero.Width < 4 || hero.Height < 4) return;
                int artWidth = Math.Min(660, Math.Max(420, hero.ClientSize.Width - 430));
                hero.ColumnStyles[0].Width = artWidth;
                hero.ColumnStyles[1].Width = Math.Max(1, hero.ClientSize.Width - hero.Padding.Horizontal - artWidth);
                using (var path = GlassTheme.Round(new Rectangle(0, 0, hero.Width, hero.Height), 20))
                { var old = hero.Region; hero.Region = new Region(path); if (old != null) old.Dispose(); }
            };
            var art = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 21, 33), Margin = Padding.Empty };
            artwork.Dock = DockStyle.Fill; artwork.BackColor = art.BackColor;
            artwork.Cursor = Cursors.Hand;
            artwork.AccessibleName = "사이트 캠페인 연결";
            artwork.Click += delegate {
                if (busy || preview == null) return;
                if (preview.kind == "external" && editExternal != null) editExternal(preview);
                else if (link != null) link(preview);
            };
            artwork.Paint += delegate(object sender, PaintEventArgs e)
            {
                if (preview == null) return;
                if (artwork.Image == null && artwork.Width > 0 && artwork.Height > 0)
                {
                    using (var fill = new System.Drawing.Drawing2D.LinearGradientBrush(artwork.ClientRectangle,
                        Color.FromArgb(25, 41, 62), GlassTheme.Background, 35f)) e.Graphics.FillRectangle(fill, artwork.ClientRectangle);
                    var titleBounds = new Rectangle(28, Math.Max(60, artwork.Height / 2 - 45), Math.Max(1, artwork.Width - 56), 80);
                    TextRenderer.DrawText(e.Graphics, preview.name, name.Font, titleBounds, GlassTheme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(e.Graphics, preview.kind == "external" ? "지도 아이콘 · 여기를 눌러 표지를 선택하세요." : "이미지 없음 · 여기를 눌러 사이트 캠페인을 연결하세요.", help.Font,
                        new Rectangle(28, titleBounds.Bottom + 10, Math.Max(1, artwork.Width - 56), 50), GlassTheme.Muted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
                }
                var font = mode.Font;
                var measured = TextRenderer.MeasureText(mode.Text, font);
                var bounds = new Rectangle(16, 16, Math.Min(Math.Max(1, artwork.Width - 32), measured.Width + 20), measured.Height + 10);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = GlassTheme.Round(bounds, 7))
                using (var fill = new SolidBrush(Color.FromArgb(232, GlassTheme.Background)))
                using (var border = new Pen(Color.FromArgb(isActivePreview() ? 102 : 128, isActivePreview() ? GlassTheme.Accent : Color.FromArgb(201, 148, 255))))
                { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
                TextRenderer.DrawText(e.Graphics, mode.Text, font, Rectangle.Inflate(bounds, -8, -3), mode.ForeColor,
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            };
            art.Controls.Add(artwork);
            mode.Dock = DockStyle.Top; mode.Height = 34; mode.Padding = new Padding(10, 0, 10, 0); mode.BackColor = art.BackColor;
            mode.Dock = DockStyle.None; mode.Location = new Point(16, 16); mode.AutoSize = true; mode.Padding = new Padding(10, 6, 10, 6);
            mode.Visible = false; hero.Controls.Add(art, 0, 0);
            var metadata = Table(1, 8); metadata.BackColor = Color.FromArgb(17, 20, 29);
            metadata.Padding = new Padding(28, 26, 28, 26); metadata.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 26, 64, 52, 52 }) metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            metadata.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            metadata.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var activeLabel = Label("캠페인", 10, true); activeLabel.ForeColor = Color.FromArgb(173, 190, 211);
            metadata.Controls.Add(activeLabel, 0, 0); metadata.Controls.Add(name, 0, 1);
            credits.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            credits.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            credits.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            credits.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            originalFact = Fact("제작자", author); credits.Controls.Add(originalFact, 0, 0);
            evoFact = Fact("EVO 개발", evoDeveloper); evoFact.Visible = false; credits.Controls.Add(evoFact, 0, 1);
            translationFact = Fact("한글번역", translator);
            translationFact.Visible = false;
            credits.Controls.Add(translationFact, 0, 2);
            metadata.Controls.Add(credits, 0, 2);
            var versionsRow = Table(2, 1);
            versionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); versionsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            versionsRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            versionsRow.Controls.Add(VersionFact("현재 버전", version), 0, 0);
            versionsRow.Controls.Add(VersionFact("사이트 최신 버전", latestVersion), 1, 0);
            metadata.Controls.Add(versionsRow, 0, 3);
            if (recordSummary != null)
            {
                metadata.RowStyles[4].Height = 64;
                recordText.ForeColor = GlassTheme.Muted; metadata.Controls.Add(recordText, 0, 4);
            }
            latestVersion.ForeColor = GlassTheme.Accent;
            applyButton.Text = "캠페인 적용"; applyButton.Dock = DockStyle.None; applyButton.Primary = true; applyButton.Margin = Padding.Empty;
            applyButton.Font = new Font("맑은 고딕", 12, FontStyle.Bold);
            applyButton.CanvasColor = metadata.BackColor;
            applyButton.Click += async delegate
            {
                if (busy || preview == null) return;
                if (HasEpisodeActions()) { ShowEpisodePicker(); return; }
                var target = preview;
                if (target.kind == "dedicated" && campaignViewed != null) { campaignViewed(target); Render(); }
                if (await apply(target)) Reload(catalogue);
            };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Margin = Padding.Empty, BackColor = metadata.BackColor };
            removeButton.Text = "캠페인 제거"; removeButton.Dock = DockStyle.None; removeButton.Margin = new Padding(10, 0, 0, 0);
            removeButton.Font = applyButton.Font; removeButton.CanvasColor = metadata.BackColor;
            updateButton.Text = "업데이트 하기"; updateButton.Font = applyButton.Font; updateButton.CanvasColor = metadata.BackColor;
            updateButton.Margin = new Padding(10, 0, 0, 0); updateButton.Visible = false;
            updateButton.Click += delegate { if (!busy && preview != null && update != null) update(preview); };
            Action sizeActions = delegate {
                int width = Math.Max(170, new[] { applyButton, removeButton, updateButton }.Max(button => button.GetPreferredSize(Size.Empty).Width));
                int height = Math.Max(64, Math.Max(applyButton.GetPreferredSize(Size.Empty).Height, removeButton.GetPreferredSize(Size.Empty).Height));
                applyButton.Size = removeButton.Size = updateButton.Size = new Size(width, height);
            };
            sizeActions(); applyButton.FontChanged += delegate { sizeActions(); }; removeButton.FontChanged += delegate { sizeActions(); };
            actions.HandleCreated += delegate { sizeActions(); };
            removeButton.Click += delegate { if (!busy && preview != null && remove != null) remove(preview); };
            actions.Controls.Add(applyButton);
            actions.Controls.Add(removeButton); actions.Controls.Add(updateButton);
            foreach (var button in new[] { voiceButton, creatorButton, translatorSiteButton })
            {
                button.Font = applyButton.Font; button.CanvasColor = metadata.BackColor;
                button.Margin = new Padding(10, 0, 0, 0); button.AutoSize = true;
                button.MinimumSize = new Size(170, 64); button.Visible = false; actions.Controls.Add(button);
            }
            voiceButton.Click += delegate { if (!busy && preview != null && changeVoice != null) changeVoice(preview); };
            creatorButton.Click += delegate { if (preview != null && openCreator != null) openCreator(preview); };
            translatorSiteButton.Click += delegate {
                if (preview != null && openCreator != null && DistributionPolicy.Https(preview.translatorUrl))
                    openCreator(new Campaign { creatorUrl = preview.translatorUrl });
            };
            metadata.Controls.Add(actions, 0, 5);
            help.ForeColor = GlassTheme.Muted; help.TextAlign = ContentAlignment.TopLeft; help.Padding = new Padding(0, 8, 0, 0);
            metadata.Controls.Add(help, 0, 6); hero.Controls.Add(metadata, 1, 0); layout.Controls.Add(hero, 0, 1);
            metadata.Layout += delegate
            {
                if (rendering) return;
                int captionHeight = Math.Max(26, TextLayout.Measure(activeLabel, activeLabel.Text, Math.Max(1, activeLabel.Width)).Height + 6);
                if (metadata.RowStyles[0].Height != captionHeight) metadata.RowStyles[0].Height = captionHeight;
                int titleHeight = Math.Max(64, installed.Select(c => TextLayout.Measure(name, c.name, Math.Max(1, metadata.ClientSize.Width - metadata.Padding.Horizontal)).Height + 10).DefaultIfEmpty(64).Max());
                int authorHeight = Math.Max(52, installed.Select(c => TextLayout.Measure(author, c.sourceAuthor ?? "제작자 미표기", Math.Max(1, metadata.ClientSize.Width - metadata.Padding.Horizontal - CreditCaptionWidth)).Height + 10).DefaultIfEmpty(52).Max());
                int translationHeight = Math.Max(52, installed.Select(c => TextLayout.Measure(translator, c.translator ?? "미표기", Math.Max(1, metadata.ClientSize.Width - metadata.Padding.Horizontal - CreditCaptionWidth)).Height + 10).DefaultIfEmpty(52).Max());
                bool dedicated = preview != null && preview.kind == "dedicated";
                bool showTranslation = preview != null && (dedicated || (preview.kind == "external" &&
                    !String.IsNullOrWhiteSpace(preview.translator)) || (preview.categories ?? new string[0]).Contains("translated"));
                bool showEvo = preview != null && (dedicated || preview.kind == "external") &&
                    !String.IsNullOrWhiteSpace(preview.evoDeveloper);
                int evoHeight = Math.Max(52, TextLayout.Measure(evoDeveloper, evoDeveloper.Text,
                    Math.Max(1, metadata.ClientSize.Width - metadata.Padding.Horizontal - CreditCaptionWidth)).Height + 10);
                credits.RowStyles[0].Height = authorHeight;
                credits.RowStyles[1].Height = showEvo ? evoHeight : 0;
                credits.RowStyles[2].Height = showTranslation ? translationHeight : 0;
                evoFact.Visible = showEvo;
                translationFact.Visible = showTranslation;
                int versionWidth = Math.Max(1, (metadata.ClientSize.Width - metadata.Padding.Horizontal) / 2 - 12);
                int versionHeight = Math.Max(70, installed.Select(c => Math.Max(TextLayout.Measure(version, c.version ?? "미표기", versionWidth).Height,
                    TextLayout.Measure(latestVersion, SiteVersion(c), versionWidth).Height) + 38).DefaultIfEmpty(70).Max());
                if (metadata.RowStyles[1].Height != titleHeight) metadata.RowStyles[1].Height = titleHeight;
                int creditsHeight = authorHeight + (showEvo ? evoHeight : 0) + (showTranslation ? translationHeight : 0);
                if (metadata.RowStyles[2].Height != creditsHeight) metadata.RowStyles[2].Height = creditsHeight;
                versionsRow.Visible = !dedicated;
                int visibleVersionHeight = dedicated ? 0 : versionHeight;
                if (metadata.RowStyles[3].Height != visibleVersionHeight) metadata.RowStyles[3].Height = visibleVersionHeight;
                int actionHeight = Math.Max(64, TextLayout.FlowHeight(actions));
                bool anyUpdate = installed.Any(c => {
                    var linked = SiteCampaign(c);
                    return linked != null && !String.IsNullOrWhiteSpace(linked.version) && !Catalogue.SameVersion(linked.version, c.version);
                });
                int contentWidth = Math.Max(1, metadata.ClientSize.Width - metadata.Padding.Horizontal);
                int columns = Math.Max(1, (contentWidth + 10) / (applyButton.Width + 10));
                int actionCount = anyUpdate ? 3 : 2;
                actionHeight = Math.Max(actionHeight, (actionCount + columns - 1) / columns * (applyButton.Height + (actionCount == 4 ? 6 : 0)));
                if (metadata.RowStyles[5].Height != actionHeight) metadata.RowStyles[5].Height = actionHeight;
                help.Visible = !dedicated && !String.IsNullOrWhiteSpace(help.Text);
                metadata.RowStyles[6].Height = help.Visible ?
                    Math.Max(44, TextLayout.Measure(help, help.Text, contentWidth).Height + help.Padding.Vertical + 8) : 0;
                int required = (int)metadata.RowStyles.Cast<RowStyle>().Where(row => row.SizeType == SizeType.Absolute).Sum(row => row.Height) + metadata.Padding.Vertical + hero.Padding.Vertical;
                hero.MinimumSize = new Size(0, Math.Max(500, required));
                if (layout.RowStyles[1].Height != hero.MinimumSize.Height) layout.RowStyles[1].Height = hero.MinimumSize.Height;
                int pageHeight = (int)layout.RowStyles.Cast<RowStyle>().Where(row => row.SizeType == SizeType.Absolute).Sum(row => row.Height) + 220;
                layout.Height = Math.Max(Math.Max(910, ClientSize.Height), pageHeight); pageScroll.ArrangeContent();
            };
            actions.Layout += delegate { metadata.PerformLayout(); };
            shelf.WrapContents = true; shelf.Margin = Padding.Empty; shelf.Padding = new Padding(0, 2, 0, 0); shelf.BackColor = BackColor;
            shelfScroll = new DarkScrollPanel(shelf) {
                Dock = DockStyle.Fill, HideScrollBar = true, SnapshotGestureScroll = true };
            layout.Controls.Add(shelfScroll, 0, 3);
            shelf.SizeChanged += delegate {
                int available = Math.Max(220, shelf.ClientSize.Width - shelf.Padding.Horizontal - 10);
                foreach (var section in shelfSections)
                    if (section.Width != available) section.Width = available;
            };
            empty.TextAlign = ContentAlignment.MiddleCenter; empty.ForeColor = GlassTheme.Muted; empty.BackColor = Color.FromArgb(16, 22, 34);
            empty.Visible = false; layout.Controls.Add(empty, 0, 1);
            TextLayout.Bind(toolbar);
        }
        void SyncHeaderHeight()
        {
            // The five tabs can wrap on a very narrow window. Determine the
            // height from viewport width, never from the currently open tab,
            // so the large preview stays at the same Y on every campaign tab.
            int buttons = tabs.Controls.Cast<Control>().Sum(c =>
                Math.Max(c.MinimumSize.Width, c.GetPreferredSize(Size.Empty).Width) + c.Margin.Horizontal);
            int toolsWidth = Math.Max(330, Math.Max(200,
                TextRenderer.MeasureText("설치된 캠페인 연결", importButton.Font).Width + 24) +
                manageButton.Width + manageButton.Margin.Horizontal + 10);
            int width = Math.Max(1, ClientSize.Width);
            int target = width < buttons + toolsWidth + 16 ? 164 : 100;
            if (mainHeaderHeight.Height != target) mainHeaderHeight.Height = target;
        }
        static Control Fact(string title, Label value)
        {
            var row = Table(2, 1); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.BackColor = Color.FromArgb(17, 20, 29);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, CreditCaptionWidth)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var caption = Label(title, 10, true); caption.ForeColor = Color.FromArgb(167, 182, 204);
            row.Controls.Add(caption, 0, 0); row.Controls.Add(value, 1, 0); return row;
        }
        static Control VersionFact(string title, Label value)
        {
            var row = Table(1, 2); row.Padding = new Padding(0, 0, 10, 0); row.BackColor = Color.FromArgb(17, 20, 29);
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var caption = Label(title, 10, true); caption.ForeColor = Color.FromArgb(167, 182, 204);
            row.Controls.Add(caption, 0, 0); row.Controls.Add(value, 0, 1); return row;
        }
        Campaign SiteCampaign(Campaign item)
        {
            string id = library.CatalogueId(item.id);
            return catalogue.FirstOrDefault(c => c.id == id);
        }
        string SiteVersion(Campaign item)
        {
            var site = SiteCampaign(item);
            return site == null ? "연결 안 됨" : site.version ?? "미표기";
        }
        Campaign Display(Campaign stored)
        {
            if (stored == null) return null;
            if (stored.kind == "external" || stored.kind == "dedicated") return stored;
            var site = SiteCampaign(stored);
            var categories = site == null ? stored.categories : site.categories;
            if ((categories == null || categories.Length == 0) && site == null)
            {
                // An exact, unique site title is enough to use its display
                // category without changing the saved site link or ZIP.
                var exact = catalogue.Where(c => c.kind != "dedicated" && c.kind != "resource" &&
                    String.Equals((c.name ?? "").Trim(), (stored.name ?? "").Trim(),
                        StringComparison.CurrentCultureIgnoreCase)).Take(2).ToArray();
                if (exact.Length == 1) categories = exact[0].categories;
            }
            bool translated = (categories ?? new string[0]).Contains("translated");
            bool gallery = (categories ?? new string[0]).Contains("gallery");
            string translationAuthor = translated
                ? site == null ? stored.translator : site.creditLabel == "자료 모음 작성자" ? null : site.sourceAuthor : null;
            string creator = gallery && site != null
                ? site.sourceAuthor : stored.sourceAuthor;
            if (translated && String.Equals((creator ?? "").Trim(), (translationAuthor ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                creator = null;
            if (!translated && !gallery && String.IsNullOrWhiteSpace(creator) && site != null) creator = site.sourceAuthor;
            return new Campaign { id = stored.id, slot = stored.slot, name = stored.name, version = stored.version, versionKey = stored.versionKey,
                sourceAuthor = String.IsNullOrWhiteSpace(creator) ? null : creator, translator = translationAuthor, categories = categories,
                thumbnail = site == null || (stored.thumbnail ?? "").StartsWith(@"packages\", StringComparison.OrdinalIgnoreCase)
                    ? stored.thumbnail : site.thumbnail, images = site == null ? stored.images : site.images };
        }
        string Information()
        {
            if (preview == null) return "";
            var site = SiteCampaign(preview);
            return "캠페인: " + preview.name + "\n보관 버전: " + (preview.version ?? "미표기") +
                "\n제작자: " + (preview.sourceAuthor ?? "미표기") +
                ((preview.categories ?? new string[0]).Contains("translated") ? "\n한글번역: " + (String.IsNullOrWhiteSpace(preview.translator) ? "미표기" : preview.translator) : "") +
                (site == null ? "\n사이트 연결: 없음" : "\n사이트 연결: " + site.name + "\n사이트 버전: " + (site.version ?? "미표기") +
                (Catalogue.SameVersion(site.version, preview.version) ? "\n보관 버전과 일치" : "\n보관 버전과 다름"));
        }
        internal void VerifyInformationClick()
        {
            if (management.Items.Cast<ToolStripItem>().Any(item => item.Text == "업데이트 확인·받기"))
                throw new InvalidOperationException("Obsolete update menu remains.");
            management.Items.Cast<ToolStripItem>().Single(item => item.Text == "캠페인·사이트 정보").PerformClick();
        }
        public void Reload(IEnumerable<Campaign> source)
        {
            catalogue = source.ToList();
            var personal = (external == null ? Enumerable.Empty<Campaign>() : external()).ToList();
            // A verified public release and a personal test launcher are two
            // representations of one title, not two distinct campaigns.
            // Keep the original connection and executable untouched.
            var dedicated = InstalledSelection.UniqueDedicated(catalogue.Where(c => c.kind == "dedicated"), personal)
                .OrderByDescending(c => dedicatedInstalled != null && dedicatedInstalled(c))
                .ThenByDescending(c => DedicatedLibrary.Ready(c)).ThenBy(c => c.name).ToList();
            foreach (var item in dedicated) item.slot = "external";
            installed = library.All().Select(Display).Concat(dedicated).Concat(personal).ToList(); state = library.Current();
            Render();
        }
        // The command-deck Nova shortcut navigates into the existing NCO
        // shelf, retaining every real launch/apply/update/remove action.
        public void SelectSlot(string desired)
        {
            if (!InstalledSelection.Slots.Contains(desired) && desired != "external")
                throw new ArgumentException("Unknown installed campaign slot.", "desired");
            slot = desired;
            preview = null;
            pageScroll.ResetPosition();
            Render();
        }
        public void SetOptimizedScrolling(bool optimized)
        {
            // The original lightweight WinForms appearance does not take
            // a large synchronous bitmap snapshot for every wheel gesture.
            // Rich modes keep the gesture path for compatible shorter lists.
            pageScroll.SnapshotGestureScroll = !optimized;
            shelfScroll.SnapshotGestureScroll = !optimized;
        }
        // Forward a user-initiated command from the live UI hub through the
        // existing apply/launch button, preserving its confirmation, checks
        // and episode selection rather than implementing a second play path.
        public bool ActivateSelected()
        {
            if (busy || preview == null || !applyButton.Visible || !applyButton.Enabled)
                return false;
            applyButton.PerformClick();
            return true;
        }
        public Campaign Preview { get { return preview; } }
        public void Reveal(IEnumerable<Campaign> source, Campaign item)
        {
            slot = item.slot; preview = item; Reload(source);
        }
        internal void VerifyImported(Campaign item)
        {
            Reveal(catalogue, item);
            if (preview == null || preview.id != item.id || slot != item.slot || !hero.Visible || empty.Visible)
                throw new InvalidOperationException("Newly imported campaign is not displayed in its installed tab.");
            Console.WriteLine("UI PASS: newly imported campaign appears immediately in its own installed tab without activation.");
        }
        internal void VerifyCategorySections()
        {
            var previousInstalled = installed;
            var previousCatalogue = catalogue;
            var previousState = state;
            var previousPreview = preview;
            var previousSlot = slot;
            try
            {
                catalogue = new List<Campaign>();
                state = new State();
                installed = new List<Campaign>();
                string[] kinds = { "gallery", "translated", "rogue", "untranslated" };
                for (int i = 0; i < kinds.Length; i++)
                    for (int j = 0; j < 2; j++)
                        installed.Add(new Campaign { id = kinds[i] + j, name = kinds[i] + j, slot = "wol",
                            version = "1.0", categories = new[] { kinds[i] } });
                installed.Add(new Campaign { id = "unlinked", name = "연결되지 않은 ZIP", slot = "hots", version = "1.0" });
                installed.Add(new Campaign { id = "unlinked2", name = "또 다른 개인 ZIP", slot = "hots", version = "1.0" });
                slot = "wol"; preview = null; Render();
                if (shelfSections.Count != 4 || cards.Count != 7 ||
                    !shelfSections.Select(section => (string)section.Tag).SequenceEqual(
                        InstalledSelection.CategoryTitles.Take(4)))
                    throw new InvalidOperationException("CCM groups must be in the card shelf, not above the hero.");
                if (tabs.Controls.Cast<MetalButton>().Any(button =>
                    InstalledSelection.CategoryTitles.Contains(button.Text)))
                    throw new InvalidOperationException("CCM categories must not appear as top navigation tabs.");
                foreach (string key in kinds)
                {
                    int categoryIndex = Array.IndexOf(InstalledSelection.Categories, key);
                    int start = shelf.Controls.IndexOf(shelfSections[categoryIndex]);
                    int end = categoryIndex + 1 == 4 ? shelf.Controls.Count :
                        shelf.Controls.IndexOf(shelfSections[categoryIndex + 1]);
                    var group = cards.Where(card => InstalledSelection.Category(card.Item) == key).ToArray();
                    if (group.Length == 0 || group.Any(card =>
                        shelf.Controls.IndexOf(card) <= start || shelf.Controls.IndexOf(card) >= end))
                        throw new InvalidOperationException("CCM shelf cards must sit below their own category heading.");
                }
                cards.Single(card => card.Item.id == "translated0").PerformClick();
                if (preview == null || preview.id != "translated0" || shelfSections.Count != 4)
                    throw new InvalidOperationException("Category card preview must preserve every other category section.");
                slot = "hots"; preview = null; Render();
                if (shelfSections.Count != 1 || (string)shelfSections[0].Tag != "미분류" ||
                    preview.id != "unlinked" || cards.Count != 1)
                    throw new InvalidOperationException("Unlinked ZIPs must remain in a shelf group without a guessed language.");
                slot = "nco"; preview = null; Render();
                if (shelfSections.Count != 0 || cards.Count != 0 || hero.Visible || !empty.Visible)
                    throw new InvalidOperationException("Empty campaign slots must not render category sections.");
                Console.WriteLine("CCM CATEGORY SECTION PASS: card-shelf headings match dedicated layout, preserve all categories and hide empty sections.");
            }
            finally
            {
                installed = previousInstalled; catalogue = previousCatalogue; state = previousState;
                slot = previousSlot; preview = previousPreview;
                Render();
            }
        }
        internal void VerifyLinkedVersion(Campaign item, string siteId)
        {
            var site = new Campaign { id = siteId, version = "9.0", sourceAuthor = "연결된 제작자" };
            Reveal(new[] { site }, item);
            if (!Information().Contains("사이트 버전: 9.0") || !Information().Contains("보관 버전과 다름") || help.Text.Contains("\n") || latestVersion.Text != "9.0" ||
                version.Text != item.version || author.Text != site.sourceAuthor)
                throw new InvalidOperationException("Linked package lost site update information or replaced its installed version.");
            Console.WriteLine("UI PASS: linked site details remain in management information, installed version unchanged and main help stays short.");
        }
        internal void VerifyCredits(Campaign item, string siteId)
        {
            var stored = library.Installed(item.id);
            string originalAuthor = stored.sourceAuthor;
            stored.sourceAuthor = "ZIP 내부 작성자";
            var site = new Campaign { id = siteId, sourceAuthor = "ESKR", categories = new[] { "gallery" } };
            catalogue = new List<Campaign> { site };
            var displayed = Display(stored);
            if (displayed.sourceAuthor != "ESKR" || displayed.translator != null)
                throw new InvalidOperationException("Gallery creator must use the linked post author, not ZIP author.");
            site.categories = new[] { "translated" }; site.sourceAuthor = "번역 담당";
            displayed = Display(stored);
            if (displayed.sourceAuthor != "ZIP 내부 작성자" || displayed.translator != "번역 담당")
                throw new InvalidOperationException("Translated campaign must keep ZIP creator separate from Korean translator.");
            stored.sourceAuthor = "번역 담당";
            if (Display(stored).sourceAuthor != null)
                throw new InvalidOperationException("Translator must not be repeated as an unverified original creator.");
            stored.sourceAuthor = null;
            if (Display(stored).sourceAuthor != null || Display(stored).translator != "번역 담당")
                throw new InvalidOperationException("Missing original creator must not fall back to translator.");
            site.creditLabel = "자료 모음 작성자";
            if (Display(stored).translator != null)
                throw new InvalidOperationException("A collection post author is not a verified translator.");
            site.creditLabel = null;
            site.sourceAuthor = new string('한', 65);
            Reveal(new[] { site }, item);
            foreach (var size in new[] { new Size(890, 540), new Size(1480, 800) })
            {
                Size = size; PerformLayout(); name.Parent.PerformLayout();
                if (!translationFact.Visible || translator.Text != site.sourceAuthor ||
                    author.Text != "제작자 미표기" || !Information().Contains("\n한글번역: " + site.sourceAuthor))
                    throw new InvalidOperationException("Creator and Korean translator must appear separately in preview and information: " +
                        "visible=" + translationFact.Visible + " translated=" + translator.Text +
                        " author=" + author.Text + " preview=" + (preview == null ? "null" : preview.id) +
                        " siteAuthorLength=" + site.sourceAuthor.Length +
                        " hasInfo=" + Information().Contains("\n한글번역: " + site.sourceAuthor));
                TextLayout.VerifyLabels(credits);
            }
            site.categories = new[] { "gallery" }; site.sourceAuthor = "ESKR";
            Reveal(new[] { site }, item);
            if (translationFact.Visible || author.Text != "ESKR" || Information().Contains("\n한글번역:"))
                throw new InvalidOperationException("Gallery campaign must show the post author without translation row.");
            if (library.Installed(item.id).sourceAuthor != originalAuthor)
                throw new InvalidOperationException("Credit display must not rewrite stored ZIP metadata.");
            Console.WriteLine("UI PASS: gallery post author takes precedence; Korean translator is separate; unknown original creator stays unknown; long credits fit two sizes and ZIP metadata stays unchanged.");
        }
        internal void VerifyUpdateButton(Campaign item, string siteId)
        {
            Reveal(new[] { new Campaign { id = siteId, version = "9.0", sourceAuthor = "제작자" } }, item);
            if (!updateButton.Visible || !updateButton.Enabled || updateButton.Text != "업데이트 하기")
                throw new InvalidOperationException("Different site version must show update action.");
            updateButton.PerformClick();
            Reveal(new[] { new Campaign { id = siteId, version = item.version } }, item);
            if (updateButton.Visible) throw new InvalidOperationException("Matching site version must hide update action.");
            Reveal(new Campaign[0], item);
            if (updateButton.Visible) throw new InvalidOperationException("Unlinked package must not offer a guessed update.");
            Console.WriteLine("UI PASS: update button appears only for known differing site version and invokes the existing update flow.");
        }
        internal void VerifyUpdateBorders(Campaign shown, Campaign shelfItem)
        {
            foreach (var test in new[] { new[] { "9.0", "yes" }, new[] { shelfItem.version, "no" },
                new[] { "0.0", "no" }, new[] { "전체 번역 · 추가 패치", "no" }, new[] { "", "no" } })
            {
                Reveal(new[] { new Campaign { id = library.CatalogueId(shelfItem.id), version = test[0] } }, shown);
                var card = cards.Single(c => c.Item.id == shelfItem.id);
                if (card.UpdateAvailable != (test[1] == "yes")) throw new InvalidOperationException("Small thumbnail version border incorrect.");
                using (var bitmap = new Bitmap(card.Width, card.Height))
                {
                    card.DrawToBitmap(bitmap, new Rectangle(Point.Empty, card.Size));
                    bool yellow = bitmap.GetPixel(1, 60).ToArgb() == Color.FromArgb(255, 210, 70).ToArgb();
                    if (yellow != card.UpdateAvailable) throw new InvalidOperationException("Small thumbnail yellow border not rendered.");
                }
            }
            Reveal(new Campaign[0], shown);
            if (cards.Any(c => c.UpdateAvailable)) throw new InvalidOperationException("Unlinked thumbnail has update border.");
            Console.WriteLine("UI PASS: yellow small-thumbnail border only for newer site version; matching/older/unknown/missing/unlinked stay normal.");
        }
        internal void VerifyCommonFrame(Campaign first, Campaign second)
        {
            Reveal(catalogue, first); PerformLayout();
            var frame = hero.Size; var art = artwork.Size; var actions = applyButton.Location;
            Reveal(catalogue, second); PerformLayout();
            if (hero.Size != frame || artwork.Size != art || applyButton.Location != actions || help.Text.Contains("\n"))
                throw new InvalidOperationException("Campaign slots must share the same frame and action geometry.");
            Console.WriteLine("UI PASS: Wings and Swarm share identical detail/image frames, compact actions and short help.");
        }
        internal void VerifyPreviewHeaderAlignment(Form host)
        {
            if (tabs.Controls.Count != 5) throw new InvalidOperationException("Expected four CCM tabs and one dedicated tab.");
            Size previous = host.ClientSize;
            try
            {
                foreach (int width in new[] { 1280, 1040, 980, 890 })
                {
                    host.ClientSize = new Size(width, 840); host.PerformLayout(); PerformLayout();
                    ((MetalButton)tabs.Controls[0]).PerformClick(); Application.DoEvents();
                    int normalY = hero.PointToScreen(Point.Empty).Y, normalHeader = (int)mainHeaderHeight.Height;
                    ((MetalButton)tabs.Controls[4]).PerformClick(); Application.DoEvents();
                    int dedicatedY = hero.PointToScreen(Point.Empty).Y, dedicatedHeader = (int)mainHeaderHeight.Height;
                    var lastTab = tabs.Controls[4];
                    if (normalY != dedicatedY || normalHeader != dedicatedHeader ||
                        (width == 1280 && normalHeader != 100) ||
                        lastTab.Bottom > tabs.ClientSize.Height || lastTab.Right > tabs.ClientSize.Width ||
                        campaignLayout.RowStyles[2].Height != 0 ||
                        RenderControls(this).OfType<Label>().Any(l =>
                            l.Text == "다른 캠페인" || l.Text == "클릭해서 미리보기"))
                        throw new InvalidOperationException("Campaign preview top must align and duplicate shelf caption stay removed at width=" + width +
                            "; y=" + normalY + "/" + dedicatedY + "; header=" + normalHeader + "/" + dedicatedHeader +
                            "; last tab=" + lastTab.Bounds + "; tabs=" + tabs.ClientRectangle);
                    Console.WriteLine("PREVIEW ALIGN PASS: width=" + width + " y=" + normalY + " header=" + normalHeader +
                        " shelf heading removed, five tabs reachable.");
                }
            }
            finally
            {
                host.ClientSize = previous;
                ((MetalButton)tabs.Controls[0]).PerformClick();
            }
        }
        public void SetBusy(bool value)
        {
            busy = value; importButton.Enabled = !value;
            removeButton.Enabled = preview != null && !value;
            updateButton.Enabled = !value;
            if (management.Items.Count > 3) management.Items[3].Enabled = !value;
            if (authorLauncherAction != null) authorLauncherAction.Enabled = !value;
            management.Items[0].Enabled = !value && (preview == null || preview.kind != "dedicated");
            management.Items[1].Enabled = !value && (preview == null || (preview.kind != "dedicated" && preview.kind != "external"));
            manageButton.Enabled = preview != null;
            UpdateApply();
        }
        internal void VerifyBusyBrowsing()
        {
            if (!busy || !Enabled || !tabs.Enabled || importButton.Enabled || applyButton.Enabled ||
                removeButton.Enabled || updateButton.Enabled || management.Items[0].Enabled ||
                management.Items[1].Enabled || !management.Items[2].Enabled)
                throw new InvalidOperationException("Busy state must protect mutations without disabling browsing.");
            ((MetalButton)tabs.Controls[1]).PerformClick();
            if (slot != "hots") throw new InvalidOperationException("Campaign tabs must remain usable while applying.");
            ((MetalButton)tabs.Controls[0]).PerformClick();
            if (cards.Count > 0) cards[0].PerformClick();
            if (preview == null || !manageButton.Enabled || applyButton.Enabled || removeButton.Enabled)
                throw new InvalidOperationException("Campaign previews and information must remain usable while applying.");
            Console.WriteLine("RESPONSIVE APPLY PASS: campaign tabs, previews and information enabled; mutations disabled.");
        }
        void UpdateApply()
        {
            bool repair = preview != null && preview.kind == "dedicated" && dedicatedRepair != null && dedicatedRepair(preview);
            voiceButton.Enabled = !busy && !repair;
            bool active = preview != null && preview.id == state.active && preview.versionKey == state.versionKey;
            applyButton.Enabled = !busy && preview != null && (preview.kind == "external" || !active);
            if (preview != null && preview.kind == "dedicated")
            {
                applyButton.Enabled = !busy && !repair && ((dedicatedInstalled != null && dedicatedInstalled(preview)) || DedicatedLibrary.Ready(preview));
                removeButton.Enabled = !busy && dedicatedInstalled != null && dedicatedInstalled(preview);
            }
            applyButton.Visible = true;
        }
        bool HasEpisodeActions()
        {
            return preview != null && ((preview.kind == "external" && ExternalCampaigns.LaunchMode(preview) == "episodes") ||
                (preview.kind == "dedicated" && preview.dedicatedEpisodeCount == 3 && dedicatedInstalled != null && dedicatedInstalled(preview)));
        }
        bool EpisodeReady(Campaign campaign, int episode)
        {
            return !busy && EpisodeLinked(campaign, episode);
        }
        bool EpisodeLinked(Campaign campaign, int episode)
        {
            if (campaign == null || launchEpisode == null) return false;
            if (campaign.kind == "dedicated")
                return dedicatedInstalled != null && dedicatedInstalled(campaign);
            return campaign.episodeMaps != null && campaign.episodeMaps.Length == 3 &&
                !String.IsNullOrWhiteSpace(campaign.episodeMaps[episode - 1]) &&
                System.IO.File.Exists(campaign.episodeMaps[episode - 1]);
        }
        void BuildEpisodePicker()
        {
            episodePicker.Items.Clear();
            if (!HasEpisodeActions()) return;
            var target = preview;
            for (int i = 1; i <= 3; i++)
            {
                int episode = i;
                bool linked = EpisodeLinked(target, episode);
                var entry = new ToolStripMenuItem("에피소드 " + episode + (linked ? "" : " · 미연결")) {
                    Tag = "external", Enabled = linked && !busy, Name = "episode-" + episode
                };
                entry.Click += async delegate {
                    if (busy || !EpisodeReady(target, episode)) return;
                    if (await launchEpisode(target, episode)) Reload(catalogue);
                };
                episodePicker.Items.Add(entry);
            }
        }
        void ShowEpisodePicker()
        {
            if (busy || !HasEpisodeActions()) return;
            BuildEpisodePicker();
            episodePicker.Show(applyButton, new Point(0, applyButton.Height));
        }
        bool isActivePreview()
        {
            return preview != null && preview.id == state.active && preview.versionKey == state.versionKey;
        }
        void Render()
        {
            var controls = RenderControls(this).Where(control => control == this ||
                control is TableLayoutPanel || control is FlowLayoutPanel || control is DarkScrollPanel).ToArray();
            compositingTransition = true;
            if (IsHandleCreated) UpdateStyles();
            try
            {
                foreach (var control in controls) control.SuspendLayout();
                rendering = true;
                try
                {
                    if (renderedSlot != slot) pageScroll.ResetPosition();
                    RenderContent();
                    renderedSlot = slot;
                }
                finally
                {
                    rendering = false;
                    foreach (var control in controls.Reverse())
                        if (!control.IsDisposed) control.ResumeLayout(true);
                    Invalidate(true);
                    Update();
                }
            }
            finally
            {
                compositingTransition = false;
                if (IsHandleCreated) { UpdateStyles(); Update(); }
            }
        }
        static IEnumerable<Control> RenderControls(Control control)
        {
            yield return control;
            foreach (Control child in control.Controls)
                foreach (Control nested in RenderControls(child)) yield return nested;
        }
        void RenderContent()
        {
            foreach (MetalButton tab in tabs.Controls) { tab.Primary = (string)tab.Tag == slot; tab.Invalidate(); }
            SyncHeaderHeight();
            var items = installed.Where(c => c.slot == slot).ToList();
            if (preview != null) preview = items.FirstOrDefault(c => c.id == preview.id);
            if (preview == null)
            {
                var active = InstalledSelection.ActivePackage(library, state);
                if (active != null && items.Any(c => c.id == active.id))
                {
                    // The latest registered version may differ from the version
                    // actually applied. Preserve the applied-version hero preview.
                    preview = Display(active);
                }
            }
            if (preview != null && preview.slot != slot) preview = null;
            if (preview == null) preview = items.FirstOrDefault();
            bool hasPreview = preview != null;
            hero.Visible = hasPreview; empty.Visible = !hasPreview;
            if (hasPreview) hero.BringToFront(); else empty.BringToFront();
            bool isActive = hasPreview && preview.id == state.active && preview.versionKey == state.versionKey;
            bool isExternal = hasPreview && preview.kind == "external";
            bool isDedicated = hasPreview && preview.kind == "dedicated";
            bool installedDedicated = isDedicated && dedicatedInstalled != null && dedicatedInstalled(preview);
            bool repairDedicated = installedDedicated && dedicatedRepair != null && dedicatedRepair(preview);
            bool updateDedicated = isDedicated && !repairDedicated && dedicatedUpdate != null && dedicatedUpdate(preview);
            bool authorLaunch = isExternal && ExternalCampaigns.LaunchMode(preview) == "author-launcher";
            mode.Text = isExternal ? "전용 캠페인 · 직접 연결" : isActive ? "● 현재 적용 중" : "● 미리보기 · 아직 적용 전";
            if (isDedicated) mode.Text = installedDedicated ? "전용 캠페인 · 설치됨" : DedicatedLibrary.Ready(preview) ? "전용 캠페인 · 설치 가능" : "전용 캠페인 · 설치본 준비 중";
            if (repairDedicated) mode.Text = "전용 캠페인 · 복구 필요";
            if (isDedicated && isNewCampaign != null && isNewCampaign(preview)) mode.Text += " · NEW";
            mode.ForeColor = isActive ? Color.FromArgb(168, 250, 255) : Color.FromArgb(234, 215, 255);
            help.Text = isExternal ? "등록된 실행 연결을 사용합니다. 연결을 제거해도 게임 파일은 유지됩니다." :
                isActive ? "" : InstalledSelection.ApplyHelp;
            if (isExternal && ExternalCampaigns.LaunchMode(preview) == "episodes")
                help.Text = "실행 버튼에서 에피소드 1·2·3을 선택할 수 있습니다.";
            applyButton.Text = HasEpisodeActions() ? "실행" :
                authorLaunch ? "제작자 런처 열기" : isExternal ? "캠페인 실행" : "캠페인 적용";
            if (isDedicated)
            {
                applyButton.Text = repairDedicated ? "복구 필요" : installedDedicated ? "실행" : DedicatedLibrary.Ready(preview) ? "설치" : "설치본 준비 중";
                help.Text = "";
            }
            if (isExternal || isDedicated) applyButton.Width = Math.Max(applyButton.Width, applyButton.GetPreferredSize(Size.Empty).Width);
            removeButton.Text = isExternal ? "설치 제거" : "캠페인 제거";
            if (isDedicated) removeButton.Text = "설치 제거";
            importButton.Text = slot == "external" ? "설치된 캠페인 연결" : "ZIP 가져오기";
            importButton.Width = Math.Max(200, importButton.GetPreferredSize(Size.Empty).Width);
            ((TableLayoutPanel)importButton.Parent.Parent).ColumnStyles[1].Width = importButton.Width + manageButton.Width + 10;
            empty.Text = slot == "external" ? "표시할 전용 캠페인이 없습니다.\n아카이브에서 목록을 확인하거나 설치된 캠페인을 연결해 주세요." :
                "설치된 캠페인이 없습니다.\n아카이브에서 받거나 ZIP을 가져와 주세요.";
            management.Items[0].Text = isExternal ? "실행 연결·표지 변경" : "버전관리·폴더관리";
            management.Items[0].Enabled = !busy && !isDedicated;
            if (authorLauncherAction != null)
            { authorLauncherAction.Visible = isExternal && ExternalCampaigns.UsesAuthorLauncher(preview); authorLauncherAction.Enabled = !busy; }
            management.Items[1].Enabled = !busy && !isExternal && !isDedicated;
            manageButton.Enabled = hasPreview;
            removeButton.Enabled = hasPreview && !busy;
            if (isDedicated) removeButton.Enabled = installedDedicated && !busy;
            artwork.Invalidate();
            name.Text = hasPreview ? InstalledSelection.DisplayName(preview) : "";
            author.Text = hasPreview ? preview.sourceAuthor ?? "제작자 미표기" : "";
            ((TableLayoutPanel)originalFact).GetControlFromPosition(0, 0).Text =
                isDedicated && !String.IsNullOrWhiteSpace(preview.evoDeveloper) ? "원작" : "제작자";
            evoDeveloper.Text = hasPreview ? preview.evoDeveloper ?? "" : "";
            voiceButton.Visible = isDedicated && installedDedicated && DedicatedLibrary.HasVoiceOptions(preview) && changeVoice != null;
            voiceButton.Enabled = !busy;
            voiceButton.Text = "음성 변경" + (voiceLabel == null || !voiceButton.Visible ? "" : " (" + voiceLabel(preview) + ")");
            creatorButton.Visible = (isDedicated || isExternal) && DistributionPolicy.Https(preview.creatorUrl) && openCreator != null;
            creatorButton.Text = hasPreview ? preview.creatorLabel == "제작자 디스코드"
                ? "공식 배포처" : preview.creatorLabel ?? "제작자 사이트" : "";
            translatorSiteButton.Visible = (isDedicated || isExternal) &&
                DistributionPolicy.Https(preview.translatorUrl) && openCreator != null &&
                !String.Equals(preview.translatorUrl, preview.creatorUrl, StringComparison.OrdinalIgnoreCase);
            translatorSiteButton.Text = hasPreview ? preview.translatorLabel ?? "한글번역 사이트" : "";
            translator.Text = hasPreview ? String.IsNullOrWhiteSpace(preview.translator) ? "미표기" : preview.translator : "";
            version.Text = hasPreview ? preview.version ?? "버전 미표기" : "";
            if (isDedicated && dedicatedVersion != null) version.Text = dedicatedVersion(preview);
            latestVersion.Text = hasPreview ? SiteVersion(preview) : "";
            UpdateRecordSummary();
            var site = hasPreview ? SiteCampaign(preview) : null;
            updateButton.Visible = site != null && !String.IsNullOrWhiteSpace(site.version) &&
                !Catalogue.SameVersion(site.version, preview.version);
            if (isDedicated) updateButton.Visible = updateDedicated;
            updateButton.Enabled = !busy;
            name.Parent.PerformLayout();
            UpdateApply(); LoadArtwork(preview);
            shelf.SuspendLayout();
            try
            {
                foreach (var card in cards) card.Dispose();
                foreach (var section in shelfSections) section.Dispose();
                shelfSections.Clear(); cards.Clear(); shelf.Controls.Clear(); shelfScroll.ResetPosition();
                // Dedicated campaigns and user-linked external campaigns share this tab.
                // Hiding external cards whenever a dedicated title exists leaves saved
                // launch links inaccessible despite being registered successfully.
                Action<Campaign> addCard = delegate(Campaign item)
                {
                    // Dedicated tab already groups installed/linked and uninstalled campaigns.
                    // Do not repeat that status (or '전용 실행') on every small card.
                    string badge = slot == "external" ? "" :
                        item.kind == "dedicated" ? dedicatedInstalled != null && dedicatedInstalled(item) ? "설치됨" : "미설치" :
                        item.kind == "external" ? "전용 실행" : item.id == state.active ? "현재 적용" : item.id == state.previousActive ? "직전 적용" : "";
                    var card = new InstalledTile(item, badge);
                    card.PlayTime = recordSummary == null ? "" :
                        (recordSummary(item.id) ?? "").Split('·')[0].Trim();
                    card.NewAvailable = item.kind == "dedicated" && isNewCampaign != null && isNewCampaign(item);
                    if (card.NewAvailable)
                    { card.AccessibleName = item.name + " 새 캠페인 미리보기"; card.AccessibleDescription = "NEW · 새 캠페인"; }
                    var linked = SiteCampaign(item);
                    card.UpdateAvailable = linked != null && Catalogue.NewerVersion(linked.version, item.version);
                    if (item.kind == "dedicated") card.UpdateAvailable = dedicatedUpdate != null && dedicatedUpdate(item);
                    if (card.UpdateAvailable) card.AccessibleDescription = (card.NewAvailable ? "NEW · " : "") + "새 버전 있음 · 사이트 " + linked.version;
                    card.Click += delegate {
                        if (item.kind == "dedicated" && campaignViewed != null) campaignViewed(item);
                        preview = item; Render();
                    };
                    cards.Add(card); shelf.Controls.Add(card); LoadCard(card);
                };
                var other = InstalledSelection.Shelf(items, slot, preview == null ? null : preview.id, state, playSeconds);
                if (slot == "external")
                {
                    Func<Campaign, bool> installedHere = item => item.kind == "external" ||
                        (item.kind == "dedicated" && dedicatedInstalled != null && dedicatedInstalled(item));
                    var ready = other.Where(installedHere).ToList();
                    var missing = other.Where(item => !installedHere(item)).ToList();
                    AddShelfSection("설치 · 연결됨", ready.Count, Color.FromArgb(35, 212, 193));
                    foreach (var item in ready) addCard(item);
                    AddShelfSection("미설치", missing.Count, Color.FromArgb(126, 159, 204));
                    foreach (var item in missing) addCard(item);
                }
                else
                {
                    // Same grouping style as dedicated installed/uninstalled:
                    // category headers belong in the card shelf, not in the
                    // primary navigation toolbar. Never show empty sections.
                    Color[] accents = {
                        Color.FromArgb(45, 190, 218), Color.FromArgb(85, 205, 171),
                        Color.FromArgb(228, 165, 92), Color.FromArgb(138, 158, 201),
                        Color.FromArgb(120, 137, 168)
                    };
                    foreach (string category in InstalledSelection.AvailableCategories(other, slot))
                    {
                        var group = other.Where(item => InstalledSelection.Category(item) == category).ToList();
                        if (group.Count == 0) continue;
                        int index = Array.IndexOf(InstalledSelection.Categories, category);
                        AddShelfSection(InstalledSelection.CategoryTitles[index], group.Count, accents[index]);
                        foreach (var item in group) addCard(item);
                    }
                }
            }
            finally { shelf.ResumeLayout(true); shelfScroll.ArrangeContent(); }
        }
        void AddShelfSection(string name, int count, Color accent)
        {
            if (shelf.Controls.Count > 0) shelf.SetFlowBreak(shelf.Controls[shelf.Controls.Count - 1], true);
            var section = new Panel { Tag = name, Height = 48,
                Width = Math.Max(220, shelf.ClientSize.Width - shelf.Padding.Horizontal - 10),
                Margin = new Padding(0, 12, 0, 12), BackColor = GlassTheme.Background,
                AccessibleName = name + " " + count + "개" };
            section.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var path = GlassTheme.Round(new Rectangle(1, 1, section.Width - 3, section.Height - 3), 9))
                using (var fill = new SolidBrush(Color.FromArgb(22, 31, 47)))
                using (var border = new Pen(Color.FromArgb(68, accent)))
                using (var stripe = new SolidBrush(accent))
                {
                    e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
                    var saved = e.Graphics.Save(); e.Graphics.SetClip(path);
                    e.Graphics.FillRectangle(stripe, 1, 1, 4, section.Height - 2);
                    e.Graphics.Restore(saved);
                }
            };
            var heading = new Label { Text = name + "  ·  " + count + "개", Dock = DockStyle.Fill,
                Padding = new Padding(18, 0, 0, 0), Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(238, 242, 250),
                BackColor = Color.Transparent };
            section.Controls.Add(heading); shelfSections.Add(section);
            shelf.Controls.Add(section); shelf.SetFlowBreak(section, true);
        }
        public void RefreshBadges() { Render(); }
        internal void VerifyCreatorVoiceActions(Campaign item, bool installed, bool working)
        {
            Reveal(catalogue, item); SetBusy(working);
            TextLayout.VerifyLabels(credits);
            if (!creatorButton.Visible || creatorButton.Text != item.creatorLabel ||
                voiceButton.Visible != installed || (installed && voiceButton.Enabled == working) ||
                (item.evoDeveloper != null && (!evoFact.Visible || evoDeveloper.Text != item.evoDeveloper ||
                    evoDeveloper.PointToScreen(Point.Empty).Y <= author.PointToScreen(Point.Empty).Y)))
                throw new InvalidOperationException("Creator link, original/EVO credit order or voice action state is incorrect.");
            if (!working) { creatorButton.PerformClick(); if (installed) voiceButton.PerformClick(); }
            SetBusy(false);
            Console.WriteLine("DEDICATED UI PASS: creator action, original above EVO developer, selected voice action and busy guard.");
        }
        internal void VerifyNewCampaignBadge(Campaign item, bool expected)
        {
            if (!expected)
            {
                if (preview == null || preview.id != item.id) cards.Single(c => c.Item.id == item.id).PerformClick();
                if (preview == null || preview.id != item.id || mode.Text.Contains("NEW"))
                    throw new InvalidOperationException("Opening the new campaign must clear only its NEW badge.");
            }
            else if (preview != null && preview.id == item.id)
            {
                if (!mode.Text.Contains("NEW")) throw new InvalidOperationException("The new campaign hero must display NEW.");
            }
            else if (!cards.Single(c => c.Item.id == item.id).NewAvailable)
                throw new InvalidOperationException("The new campaign tile must display NEW.");
            Console.WriteLine("DEDICATED NEW PASS: hero and tile badge state=" + expected + "; explicit card selection acknowledges the campaign.");
        }
        public void UpdateRecordSummary()
        {
            recordText.Text = preview == null || recordSummary == null ? "" : recordSummary(preview.id);
            if (preview != null && preview.kind == "external" && ExternalCampaigns.LaunchMode(preview) == "author-launcher")
                recordText.Text += " · 런처 경유 시간 미지원";
        }
        internal void VerifyActivityFeatures(Campaign expected, bool working)
        {
            var dedicated = tabs.Controls.Cast<MetalButton>().Single(t => (string)t.Tag == "external");
            if (preview == null || preview.id != expected.id || preview.launcherMap != expected.launcherMap ||
                applyButton.Text != (ExternalCampaigns.LaunchMode(expected) == "episodes" ? "실행" :
                    ExternalCampaigns.LaunchMode(expected) == "author-launcher" ? "제작자 런처 열기" : "캠페인 실행") ||
                removeButton.Text != "설치 제거" ||
                importButton.Text != "설치된 캠페인 연결" || !mode.Text.Contains("전용 캠페인") ||
                dedicated.LeadingIcon == null || dedicated.Bottom > tabs.ClientSize.Height ||
                removeButton.Bottom > removeButton.Parent.ClientSize.Height ||
                TextLayout.Measure(importButton, importButton.Text, Int32.MaxValue).Width > importButton.Width - 18 ||
                TextLayout.Measure(help, help.Text, help.Width).Height + help.Padding.Vertical > help.Height ||
                applyButton.Enabled == working || management.Items[3].Enabled == working ||
                (string)management.Items[3].Tag != "bank" ||
                management.Items[1].Enabled || !tabs.Enabled || !Enabled)
                throw new InvalidOperationException("Dedicated campaign UI routing, map tab or mutation protection failed.");
            Console.WriteLine("ACTIVITY UI PASS: dedicated map tab, execute, records and restore; busy=" + working);
        }
        internal void VerifyExternalSeparation(IEnumerable<Campaign> direct, Campaign legacy)
        {
            Reload(direct);
            var tab = tabs.Controls.Cast<MetalButton>().Single(t => (string)t.Tag == "external");
            tab.PerformClick();
            if (shelfSections.Count != 2 || (string)shelfSections[0].Tag != "설치 · 연결됨" ||
                (string)shelfSections[1].Tag != "미설치")
                throw new InvalidOperationException("Dedicated shelf must have installed/linked and uninstalled sections.");
            int missingHeader = shelf.Controls.IndexOf(shelfSections[1]);
            foreach (var card in cards)
            {
                bool ready = card.Item.kind == "external" ||
                    (card.Item.kind == "dedicated" && dedicatedInstalled != null && dedicatedInstalled(card.Item));
                if ((shelf.Controls.IndexOf(card) < missingHeader) != ready)
                    throw new InvalidOperationException("Installed/linked campaigns must appear above uninstalled campaigns.");
                if (!String.IsNullOrEmpty(card.StatusBadge))
                    throw new InvalidOperationException("Dedicated cards must not duplicate the installed/uninstalled/linked section status.");
            }
            if (preview == null || preview.kind != "dedicated" || applyButton.Text != "설치" || !applyButton.Enabled ||
                !cards.Any(c => c.Item.id == legacy.id) ||
                management.Items.Cast<ToolStripItem>().Any(i => i.Text == "기존 외부 연결" || i.Text == "기본 캠페인 되돌리기"))
                throw new InvalidOperationException("Personal connections must be normal cards, without a legacy submenu.");
            cards.Single(c => c.Item.id == legacy.id).PerformClick();
            if (preview == null || preview.id != legacy.id || !mode.Text.Contains("전용 캠페인") ||
                help.Text.Contains("먼저 해제") || !help.Text.Contains("게임 파일은 유지"))
                throw new InvalidOperationException("Saved direct connection must remain accessible without modifying game files.");
            VerifyActivityFeatures(legacy, false);
            tab.PerformClick();
            if (preview == null || preview.kind != "dedicated")
                throw new InvalidOperationException("Returning to the dedicated tab must select a direct installation card.");
            Console.WriteLine("AUTHOR UI PASS: dedicated and personal links remain accessible as cards, without legacy menu duplication.");
        }
        internal void VerifyAuthorLauncherAction(Campaign expected, bool working)
        {
            bool available = ExternalCampaigns.UsesAuthorLauncher(expected);
            if (authorLauncherAction == null || authorLauncherAction.Available != available ||
                authorLauncherAction.Enabled == working ||
                management.Items.Cast<ToolStripItem>().Any(i => i.Text == "기본 캠페인 되돌리기") ||
                (ExternalCampaigns.LaunchMode(expected) == "author-launcher" && !recordText.Text.Contains("런처 경유 시간 미지원")))
                throw new InvalidOperationException("Author launcher action, time support or restore scope UI is incorrect.");
            if (available && !working) authorLauncherAction.PerformClick();
            if (!working) applyButton.PerformClick();
            Console.WriteLine("AUTHOR UI PASS: selected connection routed; mutations protected; busy=" + working);
        }
        internal void VerifyEpisodeActions(Campaign expected, bool working)
        {
            if (preview == null || preview.id != expected.id || !applyButton.Visible ||
                applyButton.Text != "실행" || applyButton.Enabled == working)
                throw new InvalidOperationException("Episode campaign must use one Run action.");
            BuildEpisodePicker();
            if (episodePicker.Items.Count != 3) throw new InvalidOperationException("Run must offer three episodes.");
            for (int i = 0; i < episodePicker.Items.Count; i++)
            {
                var option = (ToolStripMenuItem)episodePicker.Items[i];
                bool connected = expected.episodeMaps != null && expected.episodeMaps.Length == 3 &&
                    !String.IsNullOrWhiteSpace(expected.episodeMaps[i]) && System.IO.File.Exists(expected.episodeMaps[i]);
                if (option.Enabled != (!working && connected) ||
                    option.Text != "에피소드 " + (i + 1) + (connected ? "" : " · 미연결"))
                    throw new InvalidOperationException("Episode choice has the wrong name or unsafe enabled state: " + (i + 1));
            }
            Console.WriteLine("EPISODE UI PASS: single Run action offers three safe episode choices; busy=" + working);
        }
        internal void VerifyEpisodeSelectionForTest()
        {
            BuildEpisodePicker();
            foreach (ToolStripMenuItem entry in episodePicker.Items)
                if (entry.Enabled) entry.PerformClick();
        }
        internal void VerifyExternalShelfAlongsideDedicated(Campaign linked)
        {
            var dedicated = new Campaign { id = "dedicated-shelf-fixture", kind = "dedicated",
                slot = "external", name = "전용 캠페인 테스트 항목", version = "1" };
            Reveal(new[] { dedicated }, dedicated);
            if (slot != "external" || !cards.Any(card => card.Item.id == linked.id))
                throw new InvalidOperationException("Saved external campaign is hidden when a dedicated campaign is present.");
            Console.WriteLine("EXTERNAL SHELF PASS: registered external campaign remains visible alongside dedicated titles.");
            Reveal(new Campaign[0], linked);
        }
        internal void VerifyDedicatedState(Campaign campaign, string expectedAction, bool enabled, bool installed, bool update, bool repair = false)
        {
            Reveal(catalogue, campaign);
            if (preview == null || preview.id != campaign.id || preview.kind != "dedicated" ||
                applyButton.Text != expectedAction || applyButton.Enabled != enabled ||
                version.Visible || latestVersion.Visible || help.Visible || help.Text != "" ||
                !translationFact.Visible || translator.Text != (String.IsNullOrWhiteSpace(campaign.translator) ? "미표기" : campaign.translator) ||
                translator.PointToScreen(Point.Empty).Y <= author.PointToScreen(Point.Empty).Y ||
                ((TableLayoutPanel)help.Parent).RowStyles[3].Height != 0 || ((TableLayoutPanel)help.Parent).RowStyles[6].Height != 0 ||
                removeButton.Text != "설치 제거" || removeButton.Enabled != (!busy && installed) ||
                updateButton.Visible != update || mode.Text.Contains("복구 필요") != repair || management.Items[0].Enabled || management.Items[1].Enabled)
                throw new InvalidOperationException("Dedicated install/play/update UI must stay separate from CCM.");
            if (enabled) applyButton.PerformClick();
            Console.WriteLine("DEDICATED UI PASS: " + expectedAction + "; installed=" + installed + "; update=" + update + "; busy=" + busy);
        }
        internal void VerifyDedicatedEpisodes(Campaign campaign, bool working)
        {
            Reveal(catalogue, campaign);
            if (preview == null || preview.id != campaign.id || preview.kind != "dedicated" || !applyButton.Visible ||
                applyButton.Text != "실행" || applyButton.Enabled == working ||
                version.Visible || latestVersion.Visible || help.Visible)
                throw new InvalidOperationException("Installed dedicated episodes must show one Run action without versions or explanation.");
            BuildEpisodePicker();
            if (episodePicker.Items.Count != 3) throw new InvalidOperationException("Three episode choices missing.");
            for (int i = 0; i < episodePicker.Items.Count; i++)
            {
                var option = (ToolStripMenuItem)episodePicker.Items[i];
                if (option.Enabled != !working || option.Text != "에피소드 " + (i + 1))
                    throw new InvalidOperationException("Dedicated episode option has the wrong state.");
            }
            Console.WriteLine("DEDICATED EPISODE UI PASS: a single Run action reveals three installed episodes; busy=" + working);
        }
        async void LoadArtwork(Campaign item)
        {
            int generation = ++imageGeneration; artwork.Image = null;
            if (item == null) return;
            try
            {
                var image = await thumbnail(item);
                if (!IsDisposed && generation == imageGeneration) { artwork.Image = image; artwork.AccessibleName = item.name + " 썸네일"; }
            }
            catch (Exception error)
            {
                if (!IsDisposed && generation == imageGeneration) { artwork.AccessibleDescription = "썸네일 확인 필요: " + error.Message; artwork.Invalidate(); }
            }
        }
        async void LoadCard(InstalledTile card)
        {
            try { var image = await thumbnail(card.Item); if (!card.IsDisposed) { card.Thumbnail = image; card.Invalidate(); } }
            catch (Exception error) { if (!card.IsDisposed) { card.AccessibleDescription = "썸네일 확인 필요: " + error.Message; card.Invalidate(); } }
        }
        internal void VerifyFixture(Campaign expected, string expectedFirst)
        {
            slot = expected.slot; preview = null; Render(); PerformLayout();
            if (preview == null || preview.id != expected.id || version.Text != expected.version || applyButton.Enabled ||
                help.Text != "" || help.Visible || cards.Count == 0 || cards[0].Item.id != expectedFirst)
                throw new InvalidOperationException("Installed library active preview/order/version failed: preview=" +
                    (preview == null ? "null" : preview.id) + " expected=" + expected.id +
                    " version=" + version.Text + " expectedVersion=" + expected.version +
                    " applyEnabled=" + applyButton.Enabled + " help=" + help.Text +
                    " visible=" + help.Visible + " cardCount=" + cards.Count +
                    " firstCard=" + (cards.Count == 0 ? "none" : cards[0].Item.id) +
                    " expectedFirst=" + expectedFirst);
            var other = cards[0].Item; cards[0].PerformClick(); PerformLayout();
            if (preview.id != other.id || !applyButton.Enabled || library.Current().active != expected.id)
                throw new InvalidOperationException("Preview changed real activation or did not enable apply.");
            if (cards.Count == 0 || cards[0].Item.id != expected.id)
                throw new InvalidOperationException("Current active campaign must lead the small shelf during preview.");
            applyButton.PerformClick(); PerformLayout();
            if (library.Current().active != other.id || library.Current().previousActive != expected.id ||
                preview.id != other.id || applyButton.Enabled || cards.Count == 0 || cards[0].Item.id != expected.id)
                throw new InvalidOperationException("Apply must promote preview and move the former active campaign first.");
            Size fixedActions = applyButton.Size;
            foreach (var size in new[] { new Size(890, 540), new Size(1480, 800) })
            {
                Size = size; PerformLayout(); shelfScroll.ArrangeContent();
                var topToolbar = ((TableLayoutPanel)Controls[0]).GetControlFromPosition(0, 0);
                if (topToolbar.Top < 0 || topToolbar.Height < 80 ||
                    manageButton.PointToScreen(Point.Empty).Y < PointToScreen(Point.Empty).Y)
                    throw new InvalidOperationException("Campaign toolbar must remain outside scrolling content during resize.");
                if (applyButton.Height < 44 || name.Height < name.Font.Height ||
                    (help.Visible && help.Height < help.Font.Height) ||
                    help.Bottom > help.Parent.ClientSize.Height || applyButton.Bottom > applyButton.Parent.ClientSize.Height ||
                    applyButton.Parent.Right > hero.ClientSize.Width ||
                    cards.Any(c => c.Right > shelf.Width || c.Bottom > shelf.Height))
                    throw new InvalidOperationException("Installed library layout clips content.");
                if (hero.Height < 500 || artwork.Height != hero.Height - hero.Padding.Vertical || removeButton.Height != 64 || applyButton.Size != fixedActions ||
                    removeButton.Right > removeButton.Parent.ClientSize.Width ||
                    TextRenderer.MeasureText(removeButton.Text, removeButton.Font).Width > removeButton.Width ||
                    TextRenderer.MeasureText(applyButton.Text, applyButton.Font).Width > applyButton.Width)
                    throw new InvalidOperationException("Enlarged campaign panel or direct removal button clips.");
                if (applyButton.Parent.Top - version.Parent.Parent.Bottom < 26 || applyButton.Parent.Top - version.Parent.Parent.Bottom > 34)
                    throw new InvalidOperationException("Apply actions must leave a modest gap below version.");
                TextLayout.VerifyLabels(hero);
            }
            if (cards.Any(c => c.Width != 220 || c.Height < 166) || name.Font.Size != 20.25f ||
                mode.Font.Size != 9.75f || applyButton.Height != 64 || applyButton.Width != removeButton.Width)
                throw new InvalidOperationException("Installed view no longer matches approved mockup dimensions.");
            slot = "nco"; preview = null; Render();
            if (!empty.Visible || applyButton.Enabled) throw new InvalidOperationException("Empty slot must not allow activation.");
            Console.WriteLine("UI PASS: installed tabs,active-version metadata,preview-only clicks,apply button switches fixture and orders former active first,empty slot and two library sizes.");
        }
    }

    // Keep original images in their owner; cache only this control's fitted presentation.
    internal sealed class CampaignArtwork : Control
    {
        Image image;
        Bitmap presentation;
        public Image Image
        {
            get { return image; }
            set
            {
                if (Object.ReferenceEquals(image, value)) return;
                image = value; ClearPresentation(); Invalidate();
            }
        }
        public CampaignArtwork()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        void ClearPresentation()
        {
            if (presentation != null) { presentation.Dispose(); presentation = null; }
        }
        protected override void OnSizeChanged(EventArgs e) { ClearPresentation(); base.OnSizeChanged(e); }
        protected override void OnBackColorChanged(EventArgs e) { ClearPresentation(); base.OnBackColorChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 1 || Height < 1) return;
            if (presentation == null)
            {
                presentation = new Bitmap(Width, Height);
                using (var graphics = Graphics.FromImage(presentation))
                {
                    graphics.Clear(BackColor);
                    if (image != null)
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(image, InstalledSelection.FitDestination(image.Size, ClientSize),
                            new RectangleF(0, 0, image.Width, image.Height), GraphicsUnit.Pixel);
                    }
                }
            }
            e.Graphics.DrawImageUnscaled(presentation, Point.Empty);
            base.OnPaint(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearPresentation();
            base.Dispose(disposing);
        }
    }
    public sealed class InstalledTile : Button
    {
        public readonly Campaign Item;
        public Image Thumbnail;
        public bool UpdateAvailable { get; set; }
        public bool NewAvailable { get; set; }
        string playTime;
        public string PlayTime
        {
            get { return playTime; }
            set {
                var duration = (value ?? "").Trim();
                playTime = duration.StartsWith("플레이 ", StringComparison.Ordinal) ? duration.Substring(3) : duration;
                Invalidate();
            }
        }
        readonly string badge;
        internal string StatusBadge { get { return badge; } }
        bool hover;
        public InstalledTile(Campaign item, string badge)
        {
            Item = item; this.badge = badge; Text = InstalledSelection.DisplayName(item);
            AccessibleName = item.name + " 미리보기";
            Size = new Size(220, 166); Margin = new Padding(0, 0, 20, 20); BackColor = GlassTheme.Background;
            Font = new Font("맑은 고딕", 9.75f, FontStyle.Bold); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            string shownBadge = NewAvailable ? (String.IsNullOrEmpty(badge) ? "NEW" : "NEW · " + badge) : badge;
            e.Graphics.Clear(GlassTheme.Background);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using (var path = GlassTheme.Round(bounds, 10))
            using (var fill = new SolidBrush(GlassTheme.Field))
            using (var border = new Pen(hover ? GlassTheme.Accent : Color.FromArgb(56, 255, 255, 255)))
            {
                e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
                var imageBounds = new Rectangle(1, 1, Width - 2, 128);
                if (Thumbnail != null)
                {
                    float scale = Math.Min((float)imageBounds.Width / Thumbnail.Width, (float)imageBounds.Height / Thumbnail.Height);
                    var target = new RectangleF(imageBounds.X + (imageBounds.Width - Thumbnail.Width * scale) / 2,
                        imageBounds.Y + (imageBounds.Height - Thumbnail.Height * scale) / 2, Thumbnail.Width * scale, Thumbnail.Height * scale);
                    e.Graphics.DrawImage(Thumbnail, target, new RectangleF(0, 0, Thumbnail.Width, Thumbnail.Height), GraphicsUnit.Pixel);
                    using (var tint = new SolidBrush(Color.FromArgb(hover ? 31 : 115, GlassTheme.Background))) e.Graphics.FillRectangle(tint, imageBounds);
                }
                if (UpdateAvailable)
                    using (var updateBorder = new Pen(Color.FromArgb(255, 210, 70), 3))
                        e.Graphics.DrawPath(updateBorder, path);
                if (!String.IsNullOrEmpty(shownBadge))
                {
                    using (var badgeFont = new Font("맑은 고딕", 7.5f))
                    {
                        var measured = TextRenderer.MeasureText(shownBadge, badgeFont);
                        var badgeBounds = new Rectangle(7, 7, measured.Width + 12, measured.Height + 6);
                        using (var badgePath = GlassTheme.Round(badgeBounds, 4))
                        using (var badgeFill = new SolidBrush(Color.FromArgb(237, 16, 23, 37)))
                        using (var badgeBorder = new Pen(NewAvailable || badge == "현재 적용" ? Color.FromArgb(102, GlassTheme.Accent) : Color.FromArgb(85, 145, 164, 195)))
                        { e.Graphics.FillPath(badgeFill, badgePath); e.Graphics.DrawPath(badgeBorder, badgePath); }
                        TextRenderer.DrawText(e.Graphics, shownBadge, badgeFont, badgeBounds, NewAvailable || badge == "현재 적용" ? Color.FromArgb(180, 252, 255) : Color.FromArgb(226, 234, 250), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }
                if (!String.IsNullOrWhiteSpace(PlayTime))
                {
                    var playBounds = new Rectangle(13, 51, Width - 26, 34);
                    var flags = TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                    using (var playFont = new Font("맑은 고딕", 10.25f, FontStyle.Bold))
                    {
                        TextRenderer.DrawText(e.Graphics, PlayTime, playFont,
                            new Rectangle(playBounds.X + 1, playBounds.Y + 2, playBounds.Width, playBounds.Height),
                            Color.FromArgb(235, 6, 12, 23), flags);
                        TextRenderer.DrawText(e.Graphics, PlayTime, playFont, playBounds,
                            Color.FromArgb(245, 249, 255), flags);
                    }
                }
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(10, 130, Width - 20, Height - 134), Color.FromArgb(210, 220, 235),
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
            }
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -3, -3));
        }
    }
}
