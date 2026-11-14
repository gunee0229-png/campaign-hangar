using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CampaignHangar
{
    public sealed class HangarNotice
    {
        public string key, kind, campaignId, title, summary, version;
        public bool unread, newCampaign, versionUpdate;
        public override string ToString() { return title; }
    }
    public sealed class NoticeState
    {
        public bool initialized;
        public Dictionary<string, string> snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public List<HangarNotice> items = new List<HangarNotice>();
    }
    public sealed class NotificationFeed
    {
        readonly string path;
        NoticeState state;
        public IList<HangarNotice> Items { get { return state.items.AsReadOnly(); } }
        public int UnreadCount { get { return state.items.Count(item => item.unread && VisibleNotice(item)); } }
        public NotificationFeed(string path, IEnumerable<Campaign> baseline)
        {
            this.path = path;
            if (File.Exists(path))
            {
                state = Data.Read<NoticeState>(path);
                if (state == null || state.snapshots == null || state.versions == null || state.items == null ||
                    state.items.Any(item => item == null || String.IsNullOrEmpty(item.key) ||
                        (item.kind != "site" && item.kind != "app")) ||
                    state.items.Select(item => item.key).Distinct().Count() != state.items.Count)
                    throw new InvalidDataException("알림 기록 형식을 확인해 주세요.");
                state.snapshots = new Dictionary<string, string>(state.snapshots, StringComparer.OrdinalIgnoreCase);
                state.versions = new Dictionary<string, string>(state.versions, StringComparer.OrdinalIgnoreCase);
                // Old snapshots were opaque metadata hashes. Use the cached catalogue
                // as a version baseline without turning old metadata edits into updates.
                foreach (var item in baseline)
                    if (state.snapshots.ContainsKey(item.id) && !state.versions.ContainsKey(item.id))
                        state.versions[item.id] = item.version ?? "";
            }
            else
            {
                state = new NoticeState();
                var source = baseline.ToList();
                state.initialized = source.Count > 0;
                foreach (var item in source)
                {
                    state.snapshots[item.id] = item.version ?? "";
                    state.versions[item.id] = item.version ?? "";
                }
            }
        }
        public static bool VisibleNotice(HangarNotice item)
        { return item.kind == "app" || item.newCampaign || item.versionUpdate; }
        NoticeState Copy()
        {
            var copy = Data.Json.Deserialize<NoticeState>(Data.Json.Serialize(state));
            copy.snapshots = new Dictionary<string, string>(copy.snapshots, StringComparer.OrdinalIgnoreCase);
            copy.versions = new Dictionary<string, string>(copy.versions, StringComparer.OrdinalIgnoreCase);
            return copy;
        }
        void Save(NoticeState next)
        {
            next.items = next.items.Take(100).ToList();
            Data.Write(path, next);
            state = next;
        }
        static void Put(NoticeState next, HangarNotice item)
        {
            next.items.RemoveAll(old => old.key == item.key);
            next.items.Insert(0, item);
        }
        public static string Short(string text, int limit)
        {
            string value = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return value.Length <= limit ? value : value.Substring(0, limit - 1) + "…";
        }
        public static string DisplaySummary(HangarNotice item)
        {
            string text = item.summary ?? "";
            string prefix = (item.version ?? "") + " · ";
            if (!String.IsNullOrWhiteSpace(item.version) && text.StartsWith(prefix, StringComparison.Ordinal))
                text = text.Substring(prefix.Length);
            foreach (string suffix in new[] { " · 눌러서 상세 보기", " · 눌러서 업데이트 확인" })
                if (text.EndsWith(suffix, StringComparison.Ordinal)) text = text.Substring(0, text.Length - suffix.Length);
            return item.versionUpdate ? PatchNotesOnly(text) : text.Trim();
        }
        public static string PatchNotesOnly(string source)
        {
            string text = (source ?? "").Trim();
            if (text.Length == 0) return "변경 내역이 제공되지 않았습니다.";
            // Catalogue update notes may include an automated deployment log
            // before the actual changelog. Keep the changelog, never the log.
            var heading = Regex.Match(text,
                @"(?:변경\s*사항|변경\s*내용|패치\s*내역|패치\s*노트|업데이트\s*내용)\s*[:：]\s*",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (heading.Success) text = text.Substring(heading.Index + heading.Length);
            else if (Regex.IsMatch(text, @"^\s*(?:\d+\s*)?자동\s*확인\s*\(", RegexOptions.CultureInvariant) ||
                     text.Contains("배포 자료에서") || text.Contains("새 버전의 배포 파일을 반영했습니다"))
                return "변경 내역이 제공되지 않았습니다.";
            text = Regex.Replace(text, @"\s+", " ").Trim().TrimStart('·', '-', ' ', ':', '：').Trim();
            return text.Length == 0 ? "변경 내역이 제공되지 않았습니다." : text;
        }
        public void ObserveSite(IEnumerable<Campaign> source)
        {
            var next = Copy();
            var snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in source)
            {
                string previous;
                bool existed = state.snapshots.ContainsKey(item.id);
                bool changed = state.versions.TryGetValue(item.id, out previous) &&
                    !String.IsNullOrWhiteSpace(previous) && !String.IsNullOrWhiteSpace(item.version) &&
                    !Catalogue.SameVersion(previous, item.version);
                snapshots.Add(item.id, item.version ?? "");
                versions.Add(item.id, item.version ?? "");
                if (state.initialized && (!existed || changed))
                {
                    string reason = !existed ? "새로운 모드" : "모드 업데이트";
                    string description = !existed ? item.summary : PatchNotesOnly(item.updateNote);
                    Put(next, new HangarNotice {
                        key = "site:" + item.id.ToLowerInvariant(), kind = "site", campaignId = item.id,
                        newCampaign = !existed, versionUpdate = existed,
                        version = item.version, unread = true, title = reason + " · " + item.name,
                        summary = String.IsNullOrWhiteSpace(description) ?
                            (!existed ? "새로운 모드가 등록됐습니다." : "변경 내역이 제공되지 않았습니다.") : Short(description, 220)
                    });
                }
            }
            next.snapshots = snapshots; next.versions = versions; next.initialized = true;
            next.items.RemoveAll(item => item.kind == "site" && !snapshots.ContainsKey(item.campaignId));
            Save(next);
        }
        public void ObserveApp(AppRelease release)
        {
            var next = Copy();
            bool newer = release != null && AppUpdate.IsNewer(release);
            string key = newer ? "app:" + release.version : null;
            next.items.RemoveAll(item => item.kind == "app" && item.key != key);
            if (newer && !next.items.Any(item => item.key == key))
                Put(next, new HangarNotice {
                    key = key, kind = "app", version = release.version, unread = true,
                    title = "격납고 업데이트",
                    summary = String.IsNullOrWhiteSpace(release.notes) ? "변경 내역이 제공되지 않았습니다." : Short(release.notes, 220)
                });
            Save(next);
        }
        public bool IsNewCampaign(string id)
        {
            return state.items.Any(item => item.kind == "site" && item.newCampaign && item.unread &&
                String.Equals(item.campaignId, id, StringComparison.OrdinalIgnoreCase));
        }
        public void MarkCampaignViewed(string id)
        {
            if (!state.items.Any(item => item.kind == "site" && item.unread &&
                String.Equals(item.campaignId, id, StringComparison.OrdinalIgnoreCase))) return;
            var next = Copy();
            foreach (var item in next.items.Where(item => item.kind == "site" &&
                String.Equals(item.campaignId, id, StringComparison.OrdinalIgnoreCase))) item.unread = false;
            Save(next);
        }
        public void MarkViewed()
        {
            if (UnreadCount == 0) return;
            var next = Copy();
            foreach (var item in next.items) item.unread = false;
            Save(next);
        }
    }
    public sealed class NotificationButton : MetalButton
    {
        int unread;
        public int UnreadCount
        {
            get { return unread; }
            set { unread = value; AccessibleName = "알림 · 읽지 않은 소식 " + value + "개"; Invalidate(); }
        }
        public NotificationButton()
        {
            Text = ""; Size = new Size(52, 48); AccessibleName = "알림 · 읽지 않은 소식 0개";
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            int x = (Width - 22) / 2, y = (Height - 26) / 2;
            using (var pen = new Pen(Enabled ? GlassTheme.Text : GlassTheme.Muted, 2))
            using (var shape = new GraphicsPath())
            {
                shape.AddArc(x + 3, y + 4, 16, 16, 180, 180);
                shape.AddLine(x + 19, y + 12, x + 19, y + 19);
                shape.AddLine(x + 19, y + 19, x + 22, y + 22);
                shape.AddLine(x + 22, y + 22, x, y + 22);
                shape.AddLine(x, y + 22, x + 3, y + 19);
                shape.CloseFigure();
                e.Graphics.DrawPath(pen, shape);
                e.Graphics.DrawArc(pen, x + 8, y + 21, 6, 5, 0, 180);
                e.Graphics.DrawLine(pen, x + 11, y + 1, x + 11, y + 4);
            }
            if (unread > 0)
                using (var red = new SolidBrush(Color.FromArgb(240, 65, 70)))
                    e.Graphics.FillEllipse(red, Width - 18, 6, 10, 10);
        }
    }
    public sealed class NotificationDialog : ThemeForm
    {
        readonly ListBox list = new ListBox();
        readonly Label empty = new Label();
        readonly List<HangarNotice> items;
        readonly MetalButton[] tabs = new MetalButton[3];
        readonly Font small;
        readonly string[] titles = { "새로운 모드", "모드 업데이트", "격납고 업데이트" };
        int category;
        bool filling;
        const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        public HangarNotice SelectedNotice { get; private set; }
        public NotificationDialog(IEnumerable<HangarNotice> source)
        {
            Text = "새 소식"; ClientSize = new Size(720, 510); MinimumSize = new Size(540, 350);
            small = new Font(Font.FontFamily, 10f);
            Disposed += delegate { small.Dispose(); };
            items = source.Where(NotificationFeed.VisibleNotice).ToList();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(20) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            var categories = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            for (int i = 0; i < tabs.Length; i++)
            {
                int selected = i;
                categories.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
                tabs[i] = new MetalButton { Text = titles[i], Dock = DockStyle.Fill, Margin = new Padding(2, 0, 2, 6), TabIndex = i,
                    Appearance = ButtonAppearance.Tab, Font = new Font("맑은 고딕", 11.25f, FontStyle.Bold),
                    CanvasColor = GlassTheme.Background, AccessibleName = titles[i] };
                var tab = tabs[i];
                tab.Disposed += delegate { tab.Font.Dispose(); };
                tabs[i].Click += delegate { category = selected; Fill(); };
                categories.Controls.Add(tabs[i], i, 0);
            }
            layout.Controls.Add(categories, 0, 0);
            list.Dock = DockStyle.Fill; list.BackColor = GlassTheme.Field; list.ForeColor = GlassTheme.Text;
            list.Font = Font; list.BorderStyle = BorderStyle.None; list.DrawMode = DrawMode.OwnerDrawVariable;
            list.IntegralHeight = false;
            list.MeasureItem += delegate(object sender, MeasureItemEventArgs e) {
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                int title, version, summary;
                e.ItemHeight = ItemHeight(e.Graphics, (HangarNotice)list.Items[e.Index], TextWidth(), out title, out version, out summary);
            };
            list.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= list.Items.Count) return;
                var item = (HangarNotice)list.Items[e.Index];
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var fill = new SolidBrush(selected ? GlassTheme.Background : GlassTheme.Field)) e.Graphics.FillRectangle(fill, e.Bounds);
                int title, version, summary, width = TextWidth();
                ItemHeight(e.Graphics, item, width, out title, out version, out summary);
                int y = e.Bounds.Y + 10;
                TextRenderer.DrawText(e.Graphics, item.title ?? "", Font, new Rectangle(e.Bounds.X + 12, y, width, title),
                    item.unread ? GlassTheme.Accent : GlassTheme.Text, Flags);
                y += title + 6;
                if (version > 0)
                {
                    TextRenderer.DrawText(e.Graphics, "버전: " + item.version, small, new Rectangle(e.Bounds.X + 12, y, width, version), GlassTheme.Text, Flags);
                    y += version + 6;
                }
                if (summary > 0)
                    TextRenderer.DrawText(e.Graphics, NotificationFeed.DisplaySummary(item), small,
                        new Rectangle(e.Bounds.X + 12, y, width, summary), GlassTheme.Muted, Flags);
                e.DrawFocusRectangle();
            };
            list.SizeChanged += delegate { Fill(); };
            list.MouseClick += delegate(object sender, MouseEventArgs e) {
                int index = list.IndexFromPoint(e.Location);
                if (index >= 0) Choose(index);
            };
            list.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0) { e.Handled = true; Choose(list.SelectedIndex); }
            };
            var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            empty.Dock = DockStyle.Fill; empty.ForeColor = GlassTheme.Muted; empty.TextAlign = ContentAlignment.MiddleCenter;
            body.Controls.Add(list); body.Controls.Add(empty); layout.Controls.Add(body, 0, 1);
            var close = new MetalButton { Text = "닫기", Dock = DockStyle.Right, Width = 110, DialogResult = DialogResult.Cancel };
            layout.Controls.Add(close, 0, 2); CancelButton = close;
            Controls.Add(layout);
            int tabWidth = tabs.Max(tab => TextRenderer.MeasureText(tab.Text, tab.Font).Width + tab.Padding.Horizontal + 24);
            MinimumSize = new Size(Math.Max(MinimumSize.Width, tabWidth * 3 + 48), MinimumSize.Height);
            category = items.Count == 0 ? 0 : Category(items[0]);
            Fill();
        }
        static int Category(HangarNotice item) { return item.kind == "app" ? 2 : item.newCampaign ? 0 : 1; }
        int TextWidth() { return Math.Max(1, list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 24); }
        int ItemHeight(Graphics graphics, HangarNotice item, int width, out int title, out int version, out int summary)
        {
            title = TextRenderer.MeasureText(graphics, item.title ?? "", Font, new Size(width, Int32.MaxValue), Flags).Height + 2;
            version = String.IsNullOrWhiteSpace(item.version) ? 0 :
                TextRenderer.MeasureText(graphics, "버전: " + item.version, small, new Size(width, Int32.MaxValue), Flags).Height + 2;
            string text = NotificationFeed.DisplaySummary(item);
            summary = text.Length == 0 ? 0 : TextRenderer.MeasureText(graphics, text, small, new Size(width, Int32.MaxValue), Flags).Height + 2;
            return 10 + title + 6 + (version > 0 ? version + 6 : 0) + summary + 12;
        }
        void Fill()
        {
            if (filling) return;
            filling = true;
            try
            {
                string selected = list.SelectedItem == null ? null : ((HangarNotice)list.SelectedItem).key;
                list.BeginUpdate();
                try
                {
                    list.Items.Clear();
                    list.Items.AddRange(items.Where(item => Category(item) == category).ToArray());
                    for (int i = 0; i < list.Items.Count; i++)
                        if (((HangarNotice)list.Items[i]).key == selected) list.SelectedIndex = i;
                    if (list.SelectedIndex < 0 && list.Items.Count > 0) list.SelectedIndex = 0;
                }
                finally { list.EndUpdate(); }
                list.AccessibleName = titles[category] + " 소식";
                list.Visible = list.Items.Count > 0;
                empty.Text = titles[category] + " 소식이 없습니다."; empty.Visible = list.Items.Count == 0;
                foreach (var tab in tabs) { tab.Primary = tab == tabs[category]; tab.Invalidate(); }
            }
            finally { filling = false; }
        }
        void Choose(int index)
        {
            SelectedNotice = (HangarNotice)list.Items[index]; DialogResult = DialogResult.OK;
        }
        internal void ActivateFixtureNotice(string kind)
        {
            var expected = items.FirstOrDefault(item => item.kind == kind);
            if (expected == null) throw new InvalidOperationException("Fixture notification not found.");
            category = Category(expected); Fill();
            for (int i = 0; i < list.Items.Count; i++)
                if (((HangarNotice)list.Items[i]).kind == kind) { Choose(i); return; }
            throw new InvalidOperationException("Fixture notification not found.");
        }
        internal void VerifyFixture(HangarNotice expected)
        {
            foreach (var size in new[] { new Size(560, 370), new Size(760, 540) })
            {
                Size = size; PerformLayout();
                if (list.Width < 400 || list.Height < 130 || list.Bottom > list.Parent.ClientSize.Height)
                    throw new InvalidOperationException("Notification list clipped: " + list.Bounds + " in " + list.Parent.ClientSize);
                foreach (var tab in tabs)
                {
                    using (var graphics = tab.CreateGraphics())
                        if (TextRenderer.MeasureText(graphics, tab.Text, tab.Font).Width + tab.Padding.Horizontal > tab.ClientSize.Width)
                            throw new InvalidOperationException("Notification tab caption clipped: " + tab.Text);
                    tab.PerformClick();
                    if (list.Items.Count != items.Count(item => Category(item) == category))
                        throw new InvalidOperationException("Notification category contains another tab's items.");
                    for (int i = 0; i < list.Items.Count; i++)
                        using (var graphics = list.CreateGraphics())
                        {
                            int title, version, summary;
                            int needed = ItemHeight(graphics, (HangarNotice)list.Items[i], TextWidth(), out title, out version, out summary);
                            if (list.GetItemHeight(i) < needed) throw new InvalidOperationException("Notification text clipped in " + titles[category]);
                        }
                }
            }
            category = Category(expected); Fill();
            Choose(list.Items.IndexOf(expected));
            if (SelectedNotice != expected || DialogResult != DialogResult.OK)
                throw new InvalidOperationException("Notification selection must return the requested action.");
            Console.WriteLine("UI PASS: notification list fits small/large windows and selects the requested notice.");
        }
    }
}
