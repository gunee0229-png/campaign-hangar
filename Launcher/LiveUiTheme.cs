using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CampaignHangar
{
    // The four UI identities deliberately live *above* the existing,
    // functional native campaign manager. No installation or play path is
    // replaced by a web mockup.
    internal sealed class LivePalette
    {
        public readonly string Id, Name, Line, Subline, BootText, Glyph;
        public readonly Color Background, Field, Surface, Accent, Text, Muted, Secondary;
        public readonly string Icon;
        public LivePalette(string id, string name, string line, string subline, string bootText,
            string glyph, string icon, Color background, Color field, Color surface,
            Color accent, Color secondary, Color text, Color muted)
        {
            Id = id; Name = name; Line = line; Subline = subline; BootText = bootText;
            Glyph = glyph; Icon = icon; Background = background; Field = field;
            Surface = surface; Accent = accent; Secondary = secondary; Text = text; Muted = muted;
        }
    }

    internal static class LiveTheme
    {
        static Color RGB(int r, int g, int b) { return Color.FromArgb(r, g, b); }
        internal static readonly LivePalette Classic = new LivePalette(
            "classic", "클래식", "CLASSIC CAMPAIGN HANGAR", "원래 격납고 · 간결한 조작",
            "기존 격납고 화면을 준비합니다", "◆", "campaign-wol.png",
            RGB(11,14,23), RGB(22,28,42), RGB(22,29,43), RGB(0,240,255),
            RGB(234,165,87), Color.White, RGB(188,192,204));
        internal static readonly LivePalette Terran = new LivePalette(
            "terran", "테란", "TERRAN / TACTICAL COMMAND", "강철 격납고 · 주황 전술 HUD",
            "테란 작전 통제 시스템을 연결합니다", "⌬", "campaign-wol.png",
            RGB(8,11,18), RGB(16,24,35), RGB(24,35,47), RGB(255,169,80),
            RGB(255,101,114), RGB(247,244,239), RGB(158,170,183));
        internal static readonly LivePalette Protoss = new LivePalette(
            "protoss", "프로토스", "PROTOSS / WARP ARCHIVE", "푸른 성소 · 황금 장식",
            "수정 에너지의 흐름을 동기화합니다", "✦", "campaign-lotv.png",
            RGB(5,15,33), RGB(10,30,56), RGB(16,43,74), RGB(224,182,90),
            RGB(88,189,255), RGB(238,245,255), RGB(166,185,210));
        internal static readonly LivePalette Zerg = new LivePalette(
            "zerg", "저그", "ZERG / HIVE CONSCIOUSNESS", "보라색 군락 · 생체 신경망",
            "군체의 신경망이 깨어나고 있습니다", "✺", "campaign-hots.png",
            RGB(18,9,28), RGB(34,17,47), RGB(41,22,54), RGB(210,102,248),
            RGB(184,241,108), RGB(255,240,255), RGB(200,163,211));
        internal static readonly LivePalette Nova = new LivePalette(
            "nova", "노바", "NOVA / COVERT OPERATIONS", "고스트 특수작전 · 스텔스 HUD",
            "고스트 전술 네트워크를 암호화합니다", "✧", "campaign-nco.png",
            RGB(5,12,17), RGB(11,26,34), RGB(17,38,46), RGB(57,216,208),
            RGB(142,219,214), RGB(231,246,245), RGB(151,181,184));

        // The original native UI is a separate low-overhead mode. The four
        // game-styled themes use the richer archive UI.
        internal static readonly LivePalette[] Choices = { Terran, Protoss, Zerg, Nova };
        internal static readonly LivePalette[] Modes = { Classic, Terran, Protoss, Zerg, Nova };
        internal static LivePalette Current = Classic;
        internal static LivePalette Resolve(string id)
        {
            if (String.Equals(id, Classic.Id, StringComparison.OrdinalIgnoreCase))
                return Classic;
            return Choices.FirstOrDefault(t => String.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Terran;
        }
        internal static void Select(LivePalette palette)
        {
            Current = palette;
            GlassTheme.CustomButtonText = GlassTheme.CustomIcon = GlassTheme.CustomBorder = null;
            GlassTheme.Background = palette.Background;
            GlassTheme.Field = palette.Field;
            GlassTheme.Surface = palette.Surface;
            GlassTheme.Accent = palette.Accent;
            GlassTheme.Text = palette.Text;
            GlassTheme.Muted = palette.Muted;
        }
        internal static LivePalette Snapshot()
        {
            var p=Current;
            return new LivePalette(p.Id,p.Name,p.Line,p.Subline,p.BootText,p.Glyph,p.Icon,
                GlassTheme.Background,GlassTheme.Field,GlassTheme.Surface,GlassTheme.Accent,
                p.Secondary,GlassTheme.Text,GlassTheme.Muted);
        }
        internal static void ApplyColors(System.Collections.Generic.Dictionary<string,string> colors)
        {
            Select(Current);
            foreach(var pair in colors)
            {
                var color=ColorTranslator.FromHtml(pair.Value);
                switch(pair.Key) {
                    case "bg": GlassTheme.Background=color;break;
                    case "panel": GlassTheme.Field=color;break;
                    case "panelEnd": GlassTheme.Surface=color;break;
                    case "accent": GlassTheme.Accent=color;break;
                    case "text": GlassTheme.Text=color;break;
                    case "mutedText": GlassTheme.Muted=color;break;
                    case "buttonText": GlassTheme.CustomButtonText=color;break;
                    case "iconColor": GlassTheme.CustomIcon=color;break;
                    case "borderColor": GlassTheme.CustomBorder=color;break;
                }
            }
        }
        static bool ReplaceColor(ref Color value, LivePalette old, LivePalette next)
        {
            if (value.ToArgb() == old.Background.ToArgb()) { value = next.Background; return true; }
            if (value.ToArgb() == old.Field.ToArgb()) { value = next.Field; return true; }
            if (value.ToArgb() == old.Surface.ToArgb()) { value = next.Surface; return true; }
            if (value.ToArgb() == old.Text.ToArgb()) { value = next.Text; return true; }
            if (value.ToArgb() == old.Muted.ToArgb()) { value = next.Muted; return true; }
            if (value.ToArgb() == old.Accent.ToArgb()) { value = next.Accent; return true; }
            return false;
        }
        internal static void RecolorTree(Control control, LivePalette from, LivePalette to)
        {
            if (control == null || control.IsDisposed) return;
            Color background = control.BackColor, foreground = control.ForeColor;
            if (ReplaceColor(ref background, from, to)) control.BackColor = background;
            if (ReplaceColor(ref foreground, from, to)) control.ForeColor = foreground;
            var metal = control as MetalButton;
            if (metal != null)
            {
                Color canvas = metal.CanvasColor;
                if (ReplaceColor(ref canvas, from, to)) metal.CanvasColor = canvas;
            }
            foreach (Control child in control.Controls) RecolorTree(child, from, to);
            control.Invalidate();
        }
    }

    internal sealed class UiLookPreference
    {
        public string theme { get; set; }
        public bool browseWhileWorking { get; set; }
        public bool reducedMotion { get; set; }
        public bool highContrast { get; set; }
        public bool hideThemeButtons { get; set; }
        public bool hideTopSummary { get; set; }
        public UiLookPreference() { theme = "terran"; }
    }
    internal sealed class UiLookStore
    {
        readonly string file;
        internal UiLookPreference Value { get; private set; }
        internal UiLookStore(string file)
        {
            this.file = file; Value = new UiLookPreference();
            try
            {
                if (File.Exists(file))
                {
                    var loaded = Data.Read<UiLookPreference>(file);
                    if (loaded != null) Value = loaded;
                }
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine("UI appearance preference could not be read: " + e.Message);
            }
            Value.theme = LiveTheme.Resolve(Value.theme).Id;
            // Work progress presentation is persisted separately from the
            // startup and race-switch loading animation.
        }
        internal void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                Data.Write(file, Value);
            }
            catch (Exception e)
            {
                System.Diagnostics.Trace.WriteLine("UI appearance preference could not be saved: " + e.Message);
            }
        }
    }

    // Stable, keyboard accessible owner-drawn theme picker. It paints only
    // itself, with no per-frame layout of child HWNDs (scroll performance).
    internal sealed class ThemePickButton : Control
    {
        readonly LivePalette candidate;
        bool hot, selected;
        internal ThemePickButton(LivePalette candidate, Action<LivePalette> change)
        {
            this.candidate = candidate;
            Size = new Size(75, 47); MinimumSize = Size;
            TabStop = true; AccessibleName = candidate.Name + " 테마";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            // WinForms owner-drawn Control does not reliably synthesize Click
            // on the themed surface. Handle the completed left-button gesture
            // directly; keyboard Enter/Space remains supported.
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                    change(candidate);
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; change(candidate); }
            };
        }
        internal string ThemeId { get { return candidate.Id; } }
        internal bool Selected
        {
            get { return selected; }
            set { if (selected == value) return; selected = value; Invalidate(); }
        }
        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(LiveTheme.Current.Background);
            var p = LiveTheme.Current;
            var box = new Rectangle(2, 2, Width - 5, Height - 5);
            using (var fill = new SolidBrush(Color.FromArgb(selected ? 71 : hot ? 33 : 15, candidate.Accent)))
            using (var border = new Pen(selected ? candidate.Accent : hot ? p.Accent : p.Field, selected ? 2f : 1f))
            {
                g.FillRectangle(fill, box);
                g.DrawRectangle(border, box);
            }
            if (selected)
                using (var pen = new Pen(candidate.Accent, 3))
                    g.DrawLine(pen, box.Left + 4, box.Bottom - 3, box.Right - 4, box.Bottom - 3);
            using (var font = new Font("맑은 고딕", 10, FontStyle.Bold))
                TextRenderer.DrawText(g, candidate.Name, font, new Rectangle(0, 7, Width, 23),
                    selected ? candidate.Text : p.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine);
            using (var marker = new SolidBrush(candidate.Secondary))
                g.FillRectangle(marker, new Rectangle(Width / 2 - 7, Height - 12, 14, 2));
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, new Rectangle(5,5,Width-10,Height-10));
        }
    }

    internal sealed class DeckCommand : Control
    {
        readonly Action handler;
        bool hover;
        internal DeckCommand(string text, Action handler)
        {
            Text = text; this.handler = handler; Cursor = Cursors.Hand;
            Size = new Size(99, 30); TabStop = true;
            AccessibleName = text;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))
                    handler();
            };
            KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { e.Handled = true; handler(); } };
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p = LiveTheme.Current;
            e.Graphics.Clear(p.Background);
            using (var fill = new SolidBrush(Color.FromArgb(hover ? 74 : 33, p.Accent)))
            using (var edge = new Pen(Color.FromArgb(hover ? 210 : 95, p.Accent)))
            {
                var rect = new Rectangle(1,1,Width-3,Height-3);
                e.Graphics.FillRectangle(fill,rect);
                e.Graphics.DrawRectangle(edge,rect);
            }
            using (var font = new Font("맑은 고딕", 9, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, Text, font, ClientRectangle, p.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
        }
    }

    internal sealed class HangarCommandDeck : Control
    {
        readonly ThemePickButton[] themes;
        readonly DeckCommand[] commands;
        Image background, emblem;
        internal HangarCommandDeck(Action<LivePalette> choose, Action openHome, Action openArchive, Action openInstalled,
            Action openNova, Action importZip, Action replayBoot)
        {
            Height = 130; Dock = DockStyle.Fill;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            string assets = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            try
            {
                var file = Path.Combine(assets, "hangar.jpg");
                if (File.Exists(file)) { using (var source = Image.FromFile(file)) background = new Bitmap(source); }
            }
            catch (Exception e) { System.Diagnostics.Trace.WriteLine("Command deck art unavailable: " + e.Message); }
            themes = LiveTheme.Modes.Select(p => new ThemePickButton(p, choose)).ToArray();
            foreach(var button in themes)Controls.Add(button);
            commands = new[] {
                new DeckCommand("작전실",openHome),
                new DeckCommand("아카이브",openArchive),
                new DeckCommand("내 캠페인",openInstalled),
                new DeckCommand("노바",openNova),
                new DeckCommand("ZIP 등록",importZip),
                new DeckCommand("연출",replayBoot)
            };
            foreach (var button in commands) Controls.Add(button);
            SizeChanged += delegate { Arrange(); };
            Arrange();
        }
        internal bool HasOperations { get { return commands.Length == 6 && themes.Length == 5; } }
        internal void RefreshSkin()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets");
            if (emblem != null) { emblem.Dispose(); emblem = null; }
            try
            {
                string file = Path.Combine(folder, LiveTheme.Current.Icon);
                if (File.Exists(file)) { using (var source = Image.FromFile(file)) emblem = new Bitmap(source); }
            }
            catch (Exception e) { System.Diagnostics.Trace.WriteLine("Deck emblem unavailable: " + e.Message); }
            foreach(var button in themes){button.Selected=button.ThemeId==LiveTheme.Current.Id;button.Invalidate();}
            foreach (var command in commands) command.Invalidate();
            Invalidate();
        }
        void Arrange()
        {
            int right = Width - 18;
            int width=75,gap=6;
            int themeGroup=themes.Length*width+(themes.Length-1)*gap;
            int x=Math.Max(430,right-themeGroup);
            for(int i=0;i<themes.Length;i++)themes[i].Bounds=new Rectangle(x+i*(width+gap),15,width,47);
            int commandWidth = Math.Max(68, Math.Min(100, (Width - 412 - 20) / commands.Length - 5));
            int allWidth = commands.Length * commandWidth + (commands.Length-1) * 6;
            int start = Math.Max(403, right - allWidth);
            for (int i = 0; i < commands.Length; i++) commands[i].Bounds =
                new Rectangle(start + i * (commandWidth + 6), Height-40, commandWidth, 30);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p = LiveTheme.Current;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var full = ClientRectangle;
            if (full.Width < 5 || full.Height < 5) return;
            using (var fill = new LinearGradientBrush(full,p.Surface,p.Background,LinearGradientMode.Horizontal))
                g.FillRectangle(fill,full);
            if (background != null)
            {
                using (var imageAttributes = new ImageAttributes())
                {
                    var matrix = new ColorMatrix(); matrix.Matrix33 = .19f;
                    imageAttributes.SetColorMatrix(matrix);
                    g.DrawImage(background, new Rectangle(Math.Max(0,Width/3),0,Width,Height),
                        0,0,background.Width,background.Height,GraphicsUnit.Pixel,imageAttributes);
                }
            }
            using (var glass = new SolidBrush(Color.FromArgb(185,p.Background)))
            {
                var points = new[] { new Point(0,0),new Point(Math.Min(650,Width/2+120),0),
                    new Point(Math.Min(505,Width/2),Height),new Point(0,Height) };
                g.FillPolygon(glass,points);
            }
            using (var line = new Pen(Color.FromArgb(23,p.Accent)))
                for (int i = -200; i < Width+80; i += 44)
                    g.DrawLine(line,i,0,i+100,Height);
            using (var border = new Pen(Color.FromArgb(146,p.Accent),1.2f))
            {
                g.DrawLine(border,0,0,Width-1,0);
                g.DrawLine(border,0,Height-1,Width-1,Height-1);
                g.DrawLine(border,0,0,0,Height);
                g.DrawLine(border,Width-1,0,Width-1,Height);
                g.DrawLine(border,2,2,38,2);
                g.DrawLine(border,2,2,2,30);
            }
            if (emblem != null) g.DrawImage(emblem,new Rectangle(15,26,83,78));
            string over = "C20 /  " + p.Line;
            using (var font = new Font("Consolas",9,FontStyle.Bold))
            using (var brush = new SolidBrush(p.Secondary)) g.DrawString(over,font,brush,112,21);
            using (var font = new Font("맑은 고딕",22,FontStyle.Bold))
                TextRenderer.DrawText(g,"커캠 격납고",font,new Rectangle(105,42,340,43),p.Text,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var font = new Font("맑은 고딕",9,FontStyle.Regular))
                TextRenderer.DrawText(g,p.Subline,font,new Rectangle(111,89,335,20),p.Muted,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            using (var pen = new Pen(Color.FromArgb(152,p.Secondary),3))
                g.DrawLine(pen,110,117,Math.Min(380, Width-25),117);
            if (Width>780)
                using(var pen = new Pen(Color.FromArgb(53,p.Secondary),1))
                    g.DrawArc(pen,Width-365,-105,350,350,25,265);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if(background!=null)background.Dispose(); if(emblem!=null)emblem.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class UiCinematicBoot : Control
    {
        readonly Timer timer = new Timer { Interval = 33 };
        readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        readonly Action done;
        Image background, badge;
        bool finished;
        public UiCinematicBoot(Action done)
        {
            this.done = done;
            Dock = DockStyle.Fill; TabStop = true; Cursor = Cursors.Hand;
            AccessibleName = "커캠 격납고 시작 연출. 클릭 또는 Escape 키로 건너뛸 수 있습니다.";
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,true);
            try
            {
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","hangar.jpg");
                if(File.Exists(path)) { using(var image = Image.FromFile(path)) background = new Bitmap(image); }
                var icon = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets",LiveTheme.Current.Icon);
                if(File.Exists(icon)) { using(var image = Image.FromFile(icon)) badge = new Bitmap(image); }
            }
            catch(Exception e) { System.Diagnostics.Trace.WriteLine("Boot image unavailable: " + e.Message); }
            timer.Tick += delegate { if(clock.ElapsedMilliseconds >= 2450) Finish(); else Invalidate(); };
            Click += delegate { Finish(); };
            KeyDown += delegate(object sender,KeyEventArgs e) { if(e.KeyCode == Keys.Escape || e.KeyCode == Keys.Enter) { e.Handled=true; Finish(); } };
            Disposed += delegate { timer.Dispose(); if(background!=null)background.Dispose();if(badge!=null)badge.Dispose(); };
        }
        public void Begin() { clock.Restart(); timer.Start(); Focus(); }
        public void Finish()
        {
            if(finished)return;
            finished = true;timer.Stop();
            if(done!=null)done();
            Dispose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var p=LiveTheme.Current;
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            var rect=ClientRectangle; if(rect.Width<20 || rect.Height<20)return;
            using(var fill = new LinearGradientBrush(rect,p.Background,Color.FromArgb(1,4,11),LinearGradientMode.Vertical))
                g.FillRectangle(fill,rect);
            if(background!=null)
                using(var attributes=new ImageAttributes())
                {
                    var mat=new ColorMatrix();mat.Matrix33=.15f;attributes.SetColorMatrix(mat);
                    g.DrawImage(background,rect,0,0,background.Width,background.Height,GraphicsUnit.Pixel,attributes);
                }
            int cx=Width/2, cy=Height/2-76, radius=Math.Min(190,Math.Min(Width,Height)/4);
            float angle=(float)(clock.ElapsedMilliseconds*.1f);
            using(var pen=new Pen(Color.FromArgb(95,p.Accent),2))
            {
                g.DrawEllipse(pen,cx-radius,cy-radius,radius*2,radius*2);
                g.DrawArc(pen,cx-radius-22,cy-radius-22,(radius+22)*2,(radius+22)*2,angle,100);
            }
            using(var pen=new Pen(p.Secondary,4))
                g.DrawArc(pen,cx-radius+23,cy-radius+23,(radius-23)*2,(radius-23)*2,-angle*.8f,78);
            using(var pen=new Pen(Color.FromArgb(58,p.Accent)))
            {
                g.DrawLine(pen,0,cy,cx-radius-30,cy);
                g.DrawLine(pen,cx+radius+30,cy,Width,cy);
                g.DrawLine(pen,cx,0,cx,cy-radius-30);
            }
            if(badge!=null)
            {
                var box=new Rectangle(cx-75,cy-75,150,150);
                g.DrawImage(badge,box);
            }
            int row=cy+radius+18;
            using(var font=new Font("Consolas",10,FontStyle.Bold))
            using(var brush=new SolidBrush(p.Secondary))
            {
                var value="CASE 20    /    "+p.Line;
                var size=g.MeasureString(value,font);
                g.DrawString(value,font,brush,cx-size.Width/2,row);
            }
            using(var font=new Font("맑은 고딕",29,FontStyle.Bold))
            using(var brush=new SolidBrush(p.Text))
            {
                var name="커캠 격납고";
                var size=g.MeasureString(name,font);
                g.DrawString(name,font,brush,cx-size.Width/2,row+25);
            }
            using(var font=new Font("맑은 고딕",11))
            using(var brush=new SolidBrush(p.Muted))
            {
                var name=p.BootText;var size=g.MeasureString(name,font);
                g.DrawString(name,font,brush,cx-size.Width/2,row+80);
            }
            int progress=Math.Max(0,Math.Min(100,(int)(clock.ElapsedMilliseconds*100/2250)));
            int width=Math.Min(510,Width-100),left=cx-width/2,top=row+124;
            using(var brush=new SolidBrush(Color.FromArgb(34,p.Accent)))g.FillRectangle(brush,left,top,width,5);
            using(var brush=new SolidBrush(p.Accent))g.FillRectangle(brush,left,top,width*progress/100,5);
            using(var font=new Font("Consolas",9,FontStyle.Bold))
            using(var brush=new SolidBrush(p.Muted))
            {
                g.DrawString("LINKING CORE MODULES...",font,brush,left,top+12);
                g.DrawString(progress+"%",font,brush,left+width-36,top+12);
                g.DrawString("SKIP [ESC]   /   BUILD: DEVELOPMENT",font,brush,Width-275,Height-38);
            }
        }
    }

    public sealed partial class Hangar
    {
        void ChangeLiveTheme(LivePalette palette)
        {
            ChangeLiveTheme(palette, true);
        }
        void ChangeLiveTheme(LivePalette palette, bool persist)
        {
            if (!AppChannel.HasThemedUi || palette == null) return;
            var previous = LiveTheme.Snapshot();
            SuspendLayout();
            try
            {
                LiveTheme.Select(palette);
                LiveTheme.RecolorTree(this, previous, palette);
                if (commandDeck != null) commandDeck.RefreshSkin();
                if (IsHandleCreated) GlassTheme.ApplyTitleBar(Handle);
                if (installedView != null)
                    installedView.SetOptimizedScrolling(palette == LiveTheme.Classic);
            }
            finally { ResumeLayout(false); }
            if (persist && uiLook != null)
            {
                uiLook.Value.theme = palette.Id;
                uiLook.Save();
            }
            if (persist && IsHandleCreated)
            {
                if (palette == LiveTheme.Classic)
                {
                    // Restore the original native archive only in optimized mode.
                    RestoreHubArchiveToNative();
                    if (currentPage != archivePage) ShowPage(archivePage);
                    else ShowPage(currentPage);
                }
                else
                {
                    bool enteringArchive = currentPage != homePage;
                    if (enteringArchive) ShowPage(homePage);
                    SyncLiveHubTheme(palette, enteringArchive);
                    // The optimized/native archive may have temporarily
                    // reparented the shared WebView2; remount it when returning
                    // to the user's chosen game-styled archive.
                    if (enteringArchive && hubLastArchiveRect != null)
                        ShowHubEmbeddedArchive(hubLastArchiveRect);
                }
            }
            status.Text = palette == LiveTheme.Classic ?
                "최적화 모드 · 기존 격납고 UI 및 기능 유지" :
                palette.Name + " UI · 아카이브와 실제 캠페인 관리 기능 연결";
        }
        void StartLiveIntro(bool forced)
        {
            if (!AppChannel.HasThemedUi || IsDisposed || !IsHandleCreated) return;
            if (!forced && (isolated || uiLook == null)) return;
            if (bootOverlay != null && !bootOverlay.IsDisposed) bootOverlay.Finish();
            UiCinematicBoot current = null;
            current = new UiCinematicBoot(delegate
            {
                if (!IsDisposed && Controls.Contains(current)) Controls.Remove(current);
                if (bootOverlay == current) bootOverlay = null;
            });
            bootOverlay = current;
            Controls.Add(current);
            current.BringToFront();
            current.Begin();
        }
        internal void VerifyLiveUiThemes()
        {
            if (!AppChannel.HasThemedUi) return;
            if (commandDeck == null || !commandDeck.HasOperations ||
                navigation.Controls.Count != 3 || installedView == null)
                throw new InvalidOperationException("Optimized mode and four skins must preserve native actions.");
            int clicked = 0;
            using (var button = new DeckCommand("click fixture", delegate { clicked++; }))
            {
                var mouseUp = typeof(Control).GetMethod("OnMouseUp",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                mouseUp.Invoke(button, new object[] {
                    new MouseEventArgs(MouseButtons.Left, 1, 15, 15, 0) });
                if (clicked != 1)
                    throw new InvalidOperationException("Native theme controls must respond to left mouse clicks.");
            }
            var original = LiveTheme.Current;
            try
            {
                foreach (var palette in LiveTheme.Modes)
                {
                    ChangeLiveTheme(palette, false);
                    if (LiveTheme.Current.Id != palette.Id ||
                        GlassTheme.Accent.ToArgb() != palette.Accent.ToArgb() ||
                        GlassTheme.Background.ToArgb() != palette.Background.ToArgb())
                        throw new InvalidOperationException("Actual native skin did not match " + palette.Id);
                    foreach (Control button in navigation.Controls)
                        if (!(button is MetalButton) || !button.Enabled)
                            throw new InvalidOperationException("Original native navigation was lost while switching skins.");
                }
                // This shortcut is the real Nova/NCO shelf. No test data or
                // fake play/install UI is substituted.
                installedView.SelectSlot("nco");
                installedView.SelectSlot("wol");
            }
            finally { ChangeLiveTheme(original, false); }
            Console.WriteLine("LIVE UI PASS: optimized native mode and Terran / Protoss / Zerg / Nova; Nova shelf and archive/installed/settings remain available.");
        }
    }
}
