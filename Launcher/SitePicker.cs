using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CampaignHangar
{
    public sealed class SitePicker : ThemeForm
    {
        readonly List<Campaign> catalogue;
        readonly Func<Campaign, Task<Image>> thumbnail;
        readonly string requiredSlot;
        readonly FlowLayoutPanel categories = Flow(), bases = Flow(), cards = Flow();
        readonly DarkScrollPanel scroll;
        readonly Label selection = new Label();
        readonly Label recommendationEmpty = new Label { Text = "이 ZIP과 일치하는 썸네일 후보를 찾지 못했습니다.\n사이트에서 직접 찾거나 연결 없이 등록할 수 있습니다.",
            AutoSize = true, ForeColor = GlassTheme.Muted, TextAlign = ContentAlignment.MiddleLeft };
        readonly MetalButton confirm = new MetalButton();
        readonly MetalButton browse = new MetalButton(), skip = new MetalButton();
        readonly bool recommendationMode;
        readonly TextBox search = new TextBox { BackColor = GlassTheme.Field, ForeColor = GlassTheme.Text, AccessibleName = "캠페인 이름·별칭 검색" };
        readonly List<ChoiceCard> choices = new List<ChoiceCard>();
        string category = "gallery", slot = "wol";
        readonly List<Campaign> recommendations;
        public Campaign Selected { get; private set; }
        public bool SlotMismatchConfirmed { get; private set; }
        internal bool RecommendationMode { get { return recommendationMode; } }
        internal void RequestBrowse() { browse.PerformClick(); }
        static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty, BackColor = GlassTheme.Background }; }
        public SitePicker(IEnumerable<Campaign> catalogue, Func<Campaign, Task<Image>> thumbnail, Campaign hint, bool allowUnlinked, string requiredSlot, IEnumerable<Campaign> recommendations = null)
        {
            recommendationMode = recommendations != null;
            this.recommendations = (recommendations ?? Enumerable.Empty<Campaign>()).ToList();
            this.catalogue = catalogue.Where(c => c.kind != "resource" && c.kind != "dedicated").ToList(); this.thumbnail = thumbnail; this.requiredSlot = requiredSlot;
            Text = recommendationMode ? "추천 썸네일 선택" : "사이트 캠페인 연결"; AutoScaleMode = AutoScaleMode.None; Font = new Font("맑은 고딕", 11);
            BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text; MinimumSize = new Size(900, 700); ClientSize = new Size(1040, 760);
            StartPosition = FormStartPosition.CenterParent;
            if (hint != null) { Selected = hint; category = (hint.categories ?? new string[0]).FirstOrDefault(c => Catalogue.SiteKeys.Contains(c)) ?? "gallery"; }
            if (Catalogue.Keys.Contains(requiredSlot)) slot = requiredSlot;
            else if (hint != null) slot = Catalogue.Group(hint);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(20), Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 40, 44, recommendationMode ? 0 : 56, recommendationMode ? 0 : 52, recommendationMode ? 0 : 48 })
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            layout.RowStyles[0].SizeType = SizeType.AutoSize;
            layout.RowStyles[1].SizeType = SizeType.AutoSize;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            layout.Controls.Add(new Label { Text = Text, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 8),
                Font = new Font(Font.FontFamily, 19, FontStyle.Bold), ForeColor = ForeColor }, 0, 0);
            layout.Controls.Add(new Label { Text = recommendationMode ? "ZIP 이름으로 추측한 후보입니다. 맞는 썸네일을 고르거나 ‘사이트에서 찾아보기’를 누르세요." : "분류와 썸네일로 찾아 선택하세요. 연결을 바꿔도 설치 파일과 현재 게임 적용은 그대로 유지됩니다.",
                Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 10),
                ForeColor = GlassTheme.Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            if (!recommendationMode) { AddTabs(categories, Catalogue.SiteKeys, Catalogue.SiteTitles, true); AddTabs(bases, Catalogue.Keys, Catalogue.Titles, false); }
            categories.Visible = bases.Visible = !recommendationMode;
            categories.WrapContents = bases.WrapContents = true;
            layout.Controls.Add(categories, 0, 2); layout.Controls.Add(bases, 0, 3);
            cards.WrapContents = true; cards.Padding = new Padding(0, 4, 0, 4);
            recommendationEmpty.Font = Font;
            cards.ClientSizeChanged += delegate { FitRecommendationMessage(); };
            var searchRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180)); searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            searchRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            searchRow.Controls.Add(new Label { Text = "이름·별칭 검색", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = ForeColor }, 0, 0);
            search.Font = Font;
            searchRow.Controls.Add(new GlassSearchField(search) { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 6) }, 1, 0);
            layout.Controls.Add(searchRow, 0, 4);
            searchRow.Visible = !recommendationMode;
            search.TextChanged += delegate { Render(); };
            scroll = new DarkScrollPanel(cards) { Dock = DockStyle.Fill }; layout.Controls.Add(scroll, 0, 5);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            selection.Dock = DockStyle.Fill; selection.AutoSize = true; selection.Padding = new Padding(0, 8, 0, 8);
            selection.ForeColor = GlassTheme.Muted; selection.TextAlign = ContentAlignment.MiddleLeft; selection.AutoEllipsis = false; footer.Controls.Add(selection, 0, 0);
            var actions = Flow(); actions.AutoSize = true; actions.WrapContents = true; actions.Padding = new Padding(0, 8, 0, 0);
            confirm.Text = "선택한 캠페인 연결"; confirm.AutoSize = true; confirm.MinimumSize = new Size(0, 44); confirm.Height = 44; confirm.Font = new Font(Font.FontFamily, 11, FontStyle.Bold);
            confirm.Click += delegate { if (Selected != null) DialogResult = DialogResult.OK; }; actions.Controls.Add(confirm);
            confirm.Visible = !recommendationMode || this.recommendations.Count > 0;
            if (recommendationMode)
            {
                browse.Text = "사이트에서 찾아보기"; browse.AutoSize = true; browse.MinimumSize = new Size(0, 44); browse.Height = 44;
                browse.Font = Font; browse.Margin = new Padding(10, 0, 0, 0);
                browse.Click += delegate { DialogResult = DialogResult.Retry; }; actions.Controls.Add(browse);
            }
            if (allowUnlinked)
            {
                skip.Text = "연결 없이 진행"; skip.AutoSize = true; skip.MinimumSize = new Size(0, 44); skip.Height = 44; skip.Margin = new Padding(10, 0, 0, 0); skip.Font = Font;
                skip.Click += delegate { Selected = null; SlotMismatchConfirmed = false; DialogResult = DialogResult.OK; }; actions.Controls.Add(skip);
            }
            footer.Controls.Add(actions, 0, 1); layout.Controls.Add(footer, 0, 6); Controls.Add(layout);
            layout.RowStyles[6].SizeType = SizeType.AutoSize;
            Render();
        }
        void AddTabs(FlowLayoutPanel row, string[] keys, string[] titles, bool siteCategories)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                var button = new MetalButton { Text = titles[i], Tag = key,
                    Appearance = siteCategories ? ButtonAppearance.Action : ButtonAppearance.Tab, AutoSize = true,
                    MinimumSize = new Size(88, 44), Height = 44, Font = new Font(Font.FontFamily, 11, FontStyle.Bold), Margin = new Padding(0, 0, 14, 0) };
                button.Click += delegate { if (siteCategories) category = key; else slot = key; Render(); }; row.Controls.Add(button);
            }
        }
        void Render()
        {
            foreach (MetalButton tab in categories.Controls) { tab.Primary = (string)tab.Tag == category; tab.Invalidate(); }
            foreach (MetalButton tab in bases.Controls) { tab.Primary = (string)tab.Tag == slot; tab.Invalidate(); }
            cards.SuspendLayout();
            foreach (var card in choices) card.Dispose(); choices.Clear(); cards.Controls.Clear();
            var visible = recommendationMode ? recommendations : String.IsNullOrWhiteSpace(search.Text)
                ? Catalogue.Filter(catalogue, category, "", slot)
                : catalogue.Where(c => SiteMatcher.SearchMatches(c, search.Text));
            foreach (var item in visible)
            {
                var card = new ChoiceCard(item) { Chosen = Selected != null && Selected.id == item.id };
                card.Click += delegate {
                    bool mismatch = requiredSlot != null && !Catalogue.SupportsSlot(item, requiredSlot);
                    if (mismatch && !ConfirmSlotMismatch(this, item, requiredSlot)) return;
                    Selected = item; SlotMismatchConfirmed = mismatch; UpdateSelection();
                };
                choices.Add(card); cards.Controls.Add(card); LoadImage(card);
            }
            cards.ResumeLayout(true); scroll.ResetPosition(); scroll.ArrangeContent(); UpdateSelection();
            if (choices.Count == 0)
            {
                selection.Text = recommendationMode ? "추천 후보 없음 · 찾아보기 또는 연결 없이 진행" : "검색 결과가 없습니다. 다른 이름이나 별칭을 입력해 주세요.";
                if (recommendationMode)
                {
                    FitRecommendationMessage();
                    cards.Controls.Add(recommendationEmpty);
                }
            }
        }
        void FitRecommendationMessage()
        {
            recommendationEmpty.MaximumSize = new Size(Math.Max(1, cards.ClientSize.Width - cards.Padding.Horizontal - recommendationEmpty.Margin.Horizontal), 0);
        }
        void UpdateSelection()
        {
            confirm.Enabled = Selected != null && (requiredSlot == null || Catalogue.SupportsSlot(Selected, requiredSlot) || SlotMismatchConfirmed);
            selection.Text = Selected == null ? "연결할 캠페인을 선택해 주세요." : "연결할 캠페인: " + Selected.name;
            foreach (var card in choices) { card.Chosen = Selected != null && Selected.id == card.Item.id; card.Invalidate(); }
        }
        internal static bool ConfirmSlotMismatch(IWin32Window owner, Campaign item, string zipSlot)
        {
            string campaignTitle = Catalogue.Titles[Array.IndexOf(Catalogue.Keys, Catalogue.Group(item))];
            string zipTitle = Catalogue.Titles[Array.IndexOf(Catalogue.Keys, zipSlot)];
            return AppDialog.Confirm(owner,
                "해당 캠페인은 " + campaignTitle + " 캠페인입니다.\n적용하시겠습니까?\n\nZIP 적용 대상: " + zipTitle +
                "\n사이트 연결만 진행하며, 게임 적용은 별도입니다.",
                "캠페인 적용 대상 확인", "예", "아니오");
        }
        async void LoadImage(ChoiceCard card)
        {
            try { var image = await thumbnail(card.Item); if (!card.IsDisposed) { card.Thumbnail = image; card.Invalidate(); } }
            catch (Exception error) { if (!card.IsDisposed) card.AccessibleDescription = "이미지 확인 필요: " + error.Message; }
        }
        internal void VerifyFixture()
        {
            Application.DoEvents();
            if (recommendationMode) { VerifyRecommendations(); return; }
            search.Text = "결과없음-test-no-match";
            if (choices.Count != 0) throw new InvalidOperationException("Search must filter all nonmatching cards.");
            search.Text = catalogue[0].id;
            if (!choices.Any(c => c.Item.id == catalogue[0].id)) throw new InvalidOperationException("Search must find IDs across categories.");
            search.Clear();
            foreach (var size in new[] { new Size(900, 700), new Size(1200, 850) })
            {
                Size = size; PerformLayout(); scroll.ArrangeContent();
                TextLayout.VerifyLabels(this);
                foreach (Control control in search.Parent.Parent.Controls)
                    if (control.Bottom > control.Parent.ClientSize.Height || control.Right > control.Parent.ClientSize.Width)
                        throw new InvalidOperationException("Search label or field clipped.");
                foreach (Control tab in categories.Controls.Cast<Control>().Concat(bases.Controls.Cast<Control>()))
                    if (tab.Right > tab.Parent.ClientSize.Width || TextRenderer.MeasureText(tab.Text, tab.Font).Width > tab.Width - 18)
                        throw new InvalidOperationException("Site picker tab clipped.");
                if (choices.Any(c => c.Right > cards.ClientSize.Width || c.Bottom > cards.Height)) throw new InvalidOperationException("Site picker card clipped.");
                foreach (Control button in confirm.Parent.Controls)
                    if (button.Right > button.Parent.ClientSize.Width || button.Bottom > button.Parent.ClientSize.Height)
                        throw new InvalidOperationException("Site picker action clipped.");
            }
            if (choices.Count == 0) throw new InvalidOperationException("Site picker fixture needs visible cards.");
            choices.First(c => c.Enabled).PerformClick();
            if (Selected == null || !confirm.Enabled) throw new InvalidOperationException("Site card selection failed.");
            Console.WriteLine("UI PASS: categorized thumbnail site picker, complete tabs/cards at two sizes and manual selection.");
        }
        internal void VerifySlotSelection(Campaign matching, Campaign mismatching, string initialTitle, string mismatchTitle, string output)
        {
            Application.DoEvents();
            var activeTab = bases.Controls.OfType<MetalButton>().Single(button => button.Primary);
            if (activeTab.Text != initialTitle)
                throw new InvalidOperationException("ZIP target must select its campaign tab even when the existing site hint differs.");
            CapturePreview(System.IO.Path.Combine(output, matching.slot + "-initial.png"));
            search.Text = matching.id;
            choices.Single(card => card.Item.id == matching.id).PerformClick();
            if (Selected != matching || !confirm.Enabled || SlotMismatchConfirmed)
                throw new InvalidOperationException("A matching campaign must be selectable without mismatch approval.");
            search.Text = mismatching.id;
            var wrong = choices.Single(card => card.Item.id == mismatching.id);
            if (!wrong.Enabled) throw new InvalidOperationException("A mismatching campaign must remain clickable.");
            using (var answer = new Checks.MismatchAnswer(this, mismatchTitle, initialTitle, false, null))
            {
                wrong.PerformClick(); answer.Verify();
            }
            if (Selected != matching || SlotMismatchConfirmed || !confirm.Enabled)
                throw new InvalidOperationException("No must preserve the previous selection.");
            using (var answer = new Checks.MismatchAnswer(this, mismatchTitle, initialTitle, true,
                System.IO.Path.Combine(output, matching.slot + "-mismatch.png")))
            {
                wrong.PerformClick(); answer.Verify();
            }
            if (Selected != mismatching || !SlotMismatchConfirmed || !confirm.Enabled)
                throw new InvalidOperationException("Yes must allow only the explicitly confirmed mismatching selection.");
            search.Text = matching.id;
            choices.Single(card => card.Item.id == matching.id).PerformClick();
            if (Selected != matching || SlotMismatchConfirmed || !confirm.Enabled)
                throw new InvalidOperationException("A matching selection must clear the previous mismatch approval.");
            confirm.PerformClick();
            if (DialogResult != DialogResult.OK) throw new InvalidOperationException("The selected campaign must be connectable.");
            Console.WriteLine("UI PASS: " + initialTitle + " initial tab; matching selection; mismatch No/Yes; approval reset.");
        }
        void VerifyRecommendations()
        {
            if (categories.Controls.Count != 0 || bases.Controls.Count != 0 || search.Visible || choices.Count != recommendations.Count)
                throw new InvalidOperationException("Recommendation screen must not expose full catalogue search or unrelated cards.");
            foreach (var size in new[] { new Size(900, 700), new Size(1200, 850) })
            {
                Size = size; PerformLayout(); scroll.ArrangeContent();
                TextLayout.VerifyLabels(this);
                if (choices.Count == 0)
                {
                    int available = cards.ClientSize.Width - cards.Padding.Horizontal - recommendationEmpty.Margin.Horizontal;
                    int lineWidth = recommendationEmpty.Text.Split('\n').Max(line => TextRenderer.MeasureText(line, recommendationEmpty.Font).Width);
                    if (recommendationEmpty.Width < Math.Min(available, lineWidth) ||
                        recommendationEmpty.Right + recommendationEmpty.Margin.Right > cards.ClientSize.Width - cards.Padding.Right)
                        throw new InvalidOperationException("Empty recommendation message must use the available width without clipping after resize.");
                }
                foreach (Control button in browse.Parent.Controls)
                    if (button.Visible && (button.Right > button.Parent.ClientSize.Width || button.Bottom > button.Parent.ClientSize.Height))
                        throw new InvalidOperationException("Recommendation actions clipped.");
            }
            if (choices.Count > 0)
            {
                choices[0].PerformClick(); confirm.PerformClick();
                if (Selected != recommendations[0] || DialogResult != DialogResult.OK) throw new InvalidOperationException("Recommendation selection failed.");
            }
            DialogResult = DialogResult.None; RequestBrowse();
            if (DialogResult != DialogResult.Retry) throw new InvalidOperationException("Browse must request separate full picker.");
            DialogResult = DialogResult.None; skip.PerformClick();
            if (Selected != null || DialogResult != DialogResult.OK) throw new InvalidOperationException("Continue unlinked failed.");
            using (var enlarged = new Font(Font.FontFamily, 14))
            {
                Font = enlarged;
                foreach (Control button in browse.Parent.Controls) button.Font = enlarged;
                foreach (Control label in Controls[0].Controls) if (label is Label && label.Font.Size < 18) label.Font = enlarged;
                Size = new Size(900, 700); PerformLayout(); TextLayout.VerifyLabels(this);
                foreach (Control button in browse.Parent.Controls)
                    if (button.Visible && button.Bottom > button.Parent.Height) throw new InvalidOperationException("Larger text actions clipped.");
            }
            Console.WriteLine("UI PASS: recommendation-only thumbnails (" + choices.Count + "), separate browse and continue-unlinked at two sizes.");
        }
        internal new void CapturePreview(string output)
        {
            Text = (recommendationMode ? "추천 썸네일 선택" : "사이트 캠페인 연결") + " · 격리 예시"; ClientSize = new Size(1040, 760); PerformLayout(); scroll.ArrangeContent();
            using (var image = new Bitmap(ClientSize.Width, ClientSize.Height))
            { Controls[0].DrawToBitmap(image, new Rectangle(Point.Empty, Controls[0].Size)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            Console.WriteLine("Native site picker render: " + output);
        }
        sealed class ChoiceCard : Button
        {
            public readonly Campaign Item;
            public Image Thumbnail;
            public bool Chosen;
            int titleHeight = 42, versionHeight = 24;
            public ChoiceCard(Campaign item)
            {
                Item = item; Text = item.name; AccessibleName = item.name + " 사이트 연결"; Size = new Size(252, 224); Margin = new Padding(0, 0, 14, 14);
                BackColor = GlassTheme.Background; Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
                FontChanged += delegate { FitText(); }; SizeChanged += delegate { FitText(); }; FitText();
            }
            void FitText()
            {
                titleHeight = Math.Max(42, TextLayout.Measure(this, Item.name, Width - 24).Height + 4);
                versionHeight = Math.Max(24, TextLayout.Measure(this, Item.version ?? "버전 미표기", Width - 24).Height + 4);
                int height = Math.Max(224, 160 + titleHeight + 8 + versionHeight + 8);
                if (Height != height) Height = height;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(GlassTheme.Background);
                var bounds = new Rectangle(1, 1, Width - 3, Height - 3);
                using (var path = GlassTheme.Round(bounds, 12))
                using (var fill = new SolidBrush(GlassTheme.Surface))
                using (var pen = new Pen(Chosen ? GlassTheme.Accent : Color.FromArgb(56, Color.White)))
                { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path); }
                var image = new Rectangle(7, 7, Width - 14, 145);
                if (Thumbnail != null)
                {
                    float scale = Math.Min((float)image.Width / Thumbnail.Width, (float)image.Height / Thumbnail.Height);
                    e.Graphics.DrawImage(Thumbnail, image.X + (image.Width - Thumbnail.Width * scale) / 2, image.Y + (image.Height - Thumbnail.Height * scale) / 2, Thumbnail.Width * scale, Thumbnail.Height * scale);
                }
                else TextRenderer.DrawText(e.Graphics, "이미지 확인 중", Font, image, GlassTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(e.Graphics, Item.name, Font, new Rectangle(12, 160, Width - 24, titleHeight), Enabled ? GlassTheme.Text : GlassTheme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                using (var small = new Font(Font.FontFamily, 9)) TextRenderer.DrawText(e.Graphics, Item.version ?? "버전 미표기", small,
                    new Rectangle(12, 160 + titleHeight + 8, Width - 24, versionHeight), GlassTheme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }
    }
}
