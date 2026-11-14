using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace CampaignHangar
{
    // Native rendering equivalents of the user's glassmorphism token set.
    // Flat low-alpha surfaces; no image backdrop or continuous real-time blur.
    public static class GlassTheme
    {
        // Live development UI skins update these tokens without replacing
        // working campaign controls or altering installed files.
        public static Color Background = Color.FromArgb(11, 14, 23);
        public static Color Field = Color.FromArgb(22, 28, 42);
        public static Color Surface = Color.FromArgb(22, 29, 43);
        public static Color Text = Color.White;
        public static Color Muted = Color.FromArgb(188, 192, 204);
        public static Color Accent = Color.FromArgb(0, 240, 255);
        public static Color? CustomButtonText, CustomIcon, CustomBorder;
        public static GraphicsPath Round(Rectangle rectangle, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
            var path = new GraphicsPath();
            if (d <= 0) return path;
            path.AddArc(rectangle.Left, rectangle.Top, d, d, 180, 90);
            path.AddArc(rectangle.Right - d, rectangle.Top, d, d, 270, 90);
            path.AddArc(rectangle.Right - d, rectangle.Bottom - d, d, d, 0, 90);
            path.AddArc(rectangle.Left, rectangle.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        public static GraphicsPath ButtonOutline(Rectangle r)
        {
            string theme=LiveTheme.Current.Id;
            if(theme=="classic")return Round(r,9);
            if(theme=="zerg")return Round(r,Math.Min(17,r.Height/3));
            int cut=Math.Min(8,Math.Min(r.Width/8,r.Height/3));
            Point[] points=theme=="protoss"?new[]{
                new Point(r.Left+cut,r.Top),new Point(r.Right-cut,r.Top),new Point(r.Right,r.Top+r.Height/2),
                new Point(r.Right-cut,r.Bottom),new Point(r.Left+cut,r.Bottom),new Point(r.Left,r.Top+r.Height/2)
            }:new[]{
                new Point(r.Left,r.Top),new Point(r.Right-cut,r.Top),new Point(r.Right,r.Top+cut),
                new Point(r.Right,r.Bottom),new Point(r.Left+cut,r.Bottom),new Point(r.Left,r.Bottom-cut)
            };
            var path=new GraphicsPath();path.AddPolygon(points);return path;
        }
        public static GraphicsPath DialogOutline(Rectangle r)
        {
            if(LiveTheme.Current==LiveTheme.Protoss){
                int c=12;var path=new GraphicsPath();path.AddPolygon(new[]{
                    new Point(r.Left+c,r.Top),new Point(r.Right-c,r.Top),new Point(r.Right,r.Top+c),
                    new Point(r.Right,r.Bottom-c),new Point(r.Right-c,r.Bottom),new Point(r.Left+c,r.Bottom),
                    new Point(r.Left,r.Bottom-c),new Point(r.Left,r.Top+c)});return path;
            }
            return Round(r,LiveTheme.Current==LiveTheme.Zerg?25:LiveTheme.Current==LiveTheme.Nova?12:17);
        }
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        public static void ApplyTitleBar(IntPtr window)
        {
            int dark = 1, background = ColorTranslator.ToWin32(Background), text = ColorTranslator.ToWin32(Text);
            if (LiveTheme.Current == LiveTheme.Terran || LiveTheme.Current == LiveTheme.Nova)
                background = ColorTranslator.ToWin32(Color.FromArgb(
                    (Background.R*85+Accent.R*15)/100,(Background.G*85+Accent.G*15)/100,(Background.B*85+Accent.B*15)/100));
            // Unsupported caption-color attributes are ignored by older Windows.
            int darkResult = DwmSetWindowAttribute(window, 20, ref dark, 4);
            int colorResult = DwmSetWindowAttribute(window, 35, ref background, 4);
            int textResult = DwmSetWindowAttribute(window, 36, ref text, 4);
            if (darkResult != 0 || colorResult != 0 || textResult != 0)
                System.Diagnostics.Trace.WriteLine("Windows title-bar theme result: " + darkResult + "/" + colorResult + "/" + textResult);
        }
        // Borderless utility dialogs do not receive Windows' caption rounding.
        // Clip only the window surface; do not alter its input, focus, or cancel behavior.
        public static void RoundWindow(Form window, int radius)
        {
            Action update = delegate {
                if (window.IsDisposed || window.Width < radius * 2 || window.Height < radius * 2) return;
                using (var outline = LiveTheme.Current==LiveTheme.Classic?Round(new Rectangle(0,0,window.Width-1,window.Height-1),radius):
                    DialogOutline(new Rectangle(0,0,window.Width-1,window.Height-1)))
                {
                    var prior = window.Region;
                    window.Region = new Region(outline);
                    if (prior != null) prior.Dispose();
                }
            };
            window.HandleCreated += delegate { update(); };
            window.SizeChanged += delegate { update(); };
            update();
        }
    }
    public sealed class BackMouseFilter : IMessageFilter
    {
        readonly Form owner;
        readonly Action back;
        public BackMouseFilter(Form owner, Action back) { this.owner = owner; this.back = back; }
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != 0x020C || (((long)message.WParam >> 16) & 0xffff) != 1) return false;
            var control = Control.FromChildHandle(message.HWnd);
            if (control == null || control.FindForm() != owner || !owner.Enabled) return false;
            back(); return true;
        }
    }
    public static class TextLayout
    {
        public static Size Measure(Control control, string text, int width)
        {
            var bounds = new Size(Math.Max(1, width), Int32.MaxValue);
            if (control.IsHandleCreated)
                using (var graphics = control.CreateGraphics())
                    return TextRenderer.MeasureText(graphics, text ?? "", control.Font, bounds, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            return TextRenderer.MeasureText(text ?? "", control.Font, bounds, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
        public static int FlowHeight(FlowLayoutPanel flow)
        {
            int x = 0, height = 0, line = 0, available = Math.Max(1, flow.ClientSize.Width - flow.Padding.Horizontal);
            foreach (Control item in flow.Controls)
            {
                if (!item.Visible) continue;
                var size = item.AutoSize ? item.GetPreferredSize(Size.Empty) : item.Size;
                int width = size.Width + item.Margin.Horizontal, h = size.Height + item.Margin.Vertical;
                if (flow.WrapContents && x > 0 && x + width > available) { height += line; x = 0; line = 0; }
                x += width; line = Math.Max(line, h);
            }
            return height + line + flow.Padding.Vertical;
        }
        public static void Bind(Control root)
        {
            foreach (Control child in root.Controls) Bind(child);
            var table = root as TableLayoutPanel;
            if (table == null || table.RowStyles.Count == 0) return;
            var minimum = table.RowStyles.Cast<RowStyle>().Select(row => row.Height).ToArray();
            bool fitting = false;
            Action fit = delegate
            {
                if (fitting || !table.Visible) return;
                fitting = true;
                try
                {
                    for (int row = 0; row < table.RowStyles.Count; row++)
                    {
                        if (table.RowStyles[row].SizeType != SizeType.Absolute) continue;
                        float needed = minimum[row];
                        foreach (Control control in table.Controls)
                        {
                            if (!control.Visible || table.GetRow(control) != row || table.GetRowSpan(control) != 1) continue;
                            int height = 0;
                            if (control is Label || control is CheckBox)
                                height = Measure(control, control.Text, control.ClientSize.Width - control.Padding.Horizontal).Height + control.Padding.Vertical + 10;
                            else if (control is MetalButton) height = control.GetPreferredSize(Size.Empty).Height;
                            else if (control is FlowLayoutPanel) height = FlowHeight((FlowLayoutPanel)control);
                            else if (control is TableLayoutPanel && ((TableLayoutPanel)control).RowStyles.Cast<RowStyle>().All(style => style.SizeType == SizeType.Absolute))
                                height = (int)((TableLayoutPanel)control).RowStyles.Cast<RowStyle>().Sum(style => style.Height) + control.Padding.Vertical;
                            height = Math.Max(height, control.MinimumSize.Height);
                            if (height > 0) needed = Math.Max(needed, height + control.Margin.Vertical);
                        }
                        if (Math.Abs(table.RowStyles[row].Height - needed) > 0.5f) table.RowStyles[row].Height = needed;
                    }
                }
                finally { fitting = false; }
            };
            table.Layout += delegate { fit(); }; table.VisibleChanged += delegate { fit(); };
            foreach (Control child in table.Controls)
            {
                child.TextChanged += delegate { fit(); }; child.FontChanged += delegate { fit(); };
                child.SizeChanged += delegate { fit(); };
                if (child is FlowLayoutPanel) child.Layout += delegate { fit(); };
            }
            fit();
        }
        public static void VerifyLabels(Control root)
        {
            foreach (Control child in root.Controls)
            {
                if (!child.Visible) continue;
                var label = child as Label;
                if (label != null && !String.IsNullOrEmpty(label.Text) &&
                    Measure(label, label.Text, label.ClientSize.Width - label.Padding.Horizontal).Height + label.Padding.Vertical > label.ClientSize.Height)
                    throw new InvalidOperationException("Text clipped: " + label.Text + "; size=" + label.ClientSize +
                        "; measured=" + Measure(label, label.Text, label.ClientSize.Width - label.Padding.Horizontal) + "; font=" + label.Font);
                var button = child as MetalButton;
                if (button != null && button.IconName == null && (Measure(button, button.Text, Int32.MaxValue).Width > button.Width - 18 || button.Font.Height > button.Height - 6))
                    throw new InvalidOperationException("Button text clipped: " + button.Text);
                if(button!=null&&button.IconName!=null&&(button.Width<24||button.Height<24))
                    throw new InvalidOperationException("Button icon clipped: "+button.IconName);
                VerifyLabels(child);
            }
        }
    }
    public class ThemeForm : Form
    {
        Image factionBackdrop;
        string geometryFile;
        internal string GeometryDirectoryForCheck;
        internal bool HasFactionBackdrop {get{return factionBackdrop!=null;}}
        public ThemeForm()
        {
            AutoScaleMode = AutoScaleMode.None; BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text;
            Font = new Font("맑은 고딕", 11); StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable; ShowIcon = true; ShowInTaskbar = true;
            string icon = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "archive.ico");
            Icon = new Icon(icon);
            if(AppChannel.HasThemedUi&&LiveTheme.Current!=LiveTheme.Classic){
                string scene=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"assets","ui-hub","assets","case20-theme-finish","loading-"+LiveTheme.Current.Id+".png");
                using(var image=Image.FromFile(scene))factionBackdrop=new Bitmap(image,new Size(960,540));
            }
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); GlassTheme.ApplyTitleBar(Handle); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (BackgroundImage != null) {
                base.OnPaintBackground(e);
                using (var shade=new SolidBrush(Color.FromArgb(160,GlassTheme.Background)))
                    e.Graphics.FillRectangle(shade,ClientRectangle);
            }
            else {
                using (var fill=new LinearGradientBrush(ClientRectangle,GlassTheme.Field,GlassTheme.Background,100f))
                    e.Graphics.FillRectangle(fill,ClientRectangle);
                if(factionBackdrop!=null){
                    e.Graphics.DrawImage(factionBackdrop,ClientRectangle);
                    using(var shade=new SolidBrush(Color.FromArgb(218,GlassTheme.Background)))
                        e.Graphics.FillRectangle(shade,ClientRectangle);
                }
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (LiveTheme.Current == LiveTheme.Classic) return;
            Color outline=GlassTheme.CustomBorder??GlassTheme.Accent;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var path=GlassTheme.DialogOutline(new Rectangle(3,3,Math.Max(1,ClientSize.Width-7),Math.Max(1,ClientSize.Height-7))))
            using(var shadow=new Pen(Color.FromArgb(220,GlassTheme.Background),6f))
            using(var edge=new Pen(Color.FromArgb(145,outline),1.4f)){
                e.Graphics.DrawPath(shadow,path);e.Graphics.DrawPath(edge,path);
            }
            using(var path=GlassTheme.DialogOutline(new Rectangle(6,6,Math.Max(1,ClientSize.Width-13),Math.Max(1,ClientSize.Height-13))))
            using(var light=new Pen(Color.FromArgb(27,GlassTheme.Text),1f)){
                e.Graphics.DrawPath(light,path);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing&&factionBackdrop!=null){factionBackdrop.Dispose();factionBackdrop=null;}
            base.Dispose(disposing);
        }
        protected override void OnLoad(EventArgs e)
        {
            var owner=Owner as Hangar;
            string directory=GeometryDirectoryForCheck??(owner==null?null:owner.WindowGeometryDirectory);
            if(directory!=null){
                string key=GetType().FullName+"|"+Text;
                using(var hash=System.Security.Cryptography.SHA256.Create()){
                    string name=BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key))).Replace("-","").ToLowerInvariant();
                    geometryFile=Path.Combine(directory,"windows",name+".json");
                }
                try{WindowSize.Restore(this,geometryFile);}
                catch(Exception error){System.Diagnostics.Trace.WriteLine("Window geometry restore: "+error.Message);}
            }
            TextLayout.Bind(this);base.OnLoad(e);
        }
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if(geometryFile!=null)
                try{WindowSize.Save(this,geometryFile);}
                catch(Exception error){System.Diagnostics.Trace.WriteLine("Window geometry save: "+error.Message);}
            base.OnFormClosed(e);
        }
        internal void CapturePreview(string output)
        {
            PerformLayout();
            using (var image = new Bitmap(Width, Height))
            { DrawToBitmap(image, new Rectangle(Point.Empty, Size)); image.Save(output, System.Drawing.Imaging.ImageFormat.Png); }
            Console.WriteLine("Native dialog render: " + output);
        }
    }
    public sealed class ThemeProgressBar : Control
    {
        readonly Timer animation=new Timer { Interval=40 };
        ProgressBarStyle style;
        int value,phase;
        public ProgressBarStyle Style { get {return style;} set {style=value;animation.Enabled=Visible&&style==ProgressBarStyle.Marquee;Invalidate();} }
        public int Value { get {return value;} set {this.value=Math.Max(0,Math.Min(100,value));Invalidate();} }
        public int MarqueeAnimationSpeed { get {return animation.Interval;} set {animation.Interval=Math.Max(15,value);} }
        public ThemeProgressBar() {
            DoubleBuffered=true;
            animation.Tick+=delegate {phase=(phase+3)%130;Invalidate();};
        }
        protected override void OnVisibleChanged(EventArgs e) {base.OnVisibleChanged(e);animation.Enabled=Visible&&style==ProgressBarStyle.Marquee;}
        protected override void OnPaint(PaintEventArgs e) {
            var track=new Rectangle(1,Math.Max(1,(Height-12)/2),Math.Max(1,Width-3),12);
            using(var bg=new SolidBrush(GlassTheme.Field))e.Graphics.FillRectangle(bg,track);
            e.Graphics.SetClip(track);
            int width=style==ProgressBarStyle.Marquee?Math.Max(12,track.Width/4):(int)(track.Width*value/100.0);
            int x=style==ProgressBarStyle.Marquee?track.Left+(int)(track.Width*(phase-30)/100.0):track.Left;
            using(var fill=new SolidBrush(GlassTheme.Accent))e.Graphics.FillRectangle(fill,x,track.Top,width,track.Height);
            e.Graphics.ResetClip();
            using(var edge=new Pen(GlassTheme.Accent))e.Graphics.DrawRectangle(edge,track);
        }
        protected override void Dispose(bool disposing) {if(disposing)animation.Dispose();base.Dispose(disposing);}
    }
    public sealed class EpisodeDialog : ThemeForm
    {
        public int Episode {get;private set;}
        public EpisodeDialog(string campaign,int count,string[] maps=null)
        {
            if(count<1||count>24)throw new InvalidDataException("에피소드 수가 지원 범위를 벗어났습니다.");
            Text="에피소드 선택 · "+campaign;ShowInTaskbar=false;
            ClientSize=new Size(600,230);MinimumSize=new Size(500,250);Padding=new Padding(22);
            var heading=new Label {Text=campaign+"\n실행할 에피소드를 선택하세요.",Dock=DockStyle.Top,
                Height=72,ForeColor=GlassTheme.Text,BackColor=Color.Transparent,Font=new Font(Font,FontStyle.Bold)};
            var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(0,12,0,0)};
            for(int i=1;i<=count;i++) {
                int episode=i;
                var button=new MetalButton {Text="에피소드 "+i,Size=new Size(150,52),Margin=new Padding(0,0,12,12),
                    Font=new Font(Font,FontStyle.Bold),Enabled=maps==null||(i<=maps.Length&&!String.IsNullOrWhiteSpace(maps[i-1]))};
                button.Click+=delegate {Episode=episode;DialogResult=DialogResult.OK;};
                buttons.Controls.Add(button);
            }
            Controls.Add(buttons);Controls.Add(heading);
            var cancel=new MetalButton {Text="취소",Dock=DockStyle.Bottom,Height=42,DialogResult=DialogResult.Cancel};
            Controls.Add(cancel);CancelButton=cancel;
        }
    }
    public sealed class WorkDialog : ThemeForm
    {
        readonly Label stage = new Label(), amount = new Label();
        readonly ThemeProgressBar bar = new ThemeProgressBar();
        readonly MetalButton stop = new MetalButton();
        readonly System.Diagnostics.Stopwatch duration = new System.Diagnostics.Stopwatch();
        readonly Timer durationTick = new Timer { Interval = 250 };
        string amountDetail = "";
        bool finished;
        public bool CancellationRequested { get; private set; }
        public WorkDialog(string title, Action cancel = null)
        {
            Text = LiveTheme.Current == LiveTheme.Classic ? title : LiveTheme.Current.Name + " · " + title;
            ClientSize = new Size(620, 220); MinimumSize = new Size(500, 240);
            ShowInTaskbar = false; ControlBox = false; Padding = new Padding(24);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            stage.Dock = amount.Dock = DockStyle.Fill; stage.AutoSize = amount.AutoSize = true;
            stage.ForeColor = GlassTheme.Text; amount.ForeColor = GlassTheme.Muted;
            stage.Font = new Font(Font, FontStyle.Bold); bar.Dock = DockStyle.Fill;
            bar.Style = ProgressBarStyle.Marquee; bar.MarqueeAnimationSpeed = 25;
            layout.Controls.Add(stage, 0, 0); layout.Controls.Add(bar, 0, 1); layout.Controls.Add(amount, 0, 2);
            if (cancel != null)
            {
                ClientSize = new Size(620, 284); MinimumSize = new Size(500, 310);
                layout.RowCount = 4; layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                stop.Text = "중단"; stop.AutoSize = true; stop.MinimumSize = new Size(130, 44);
                stop.Anchor = AnchorStyles.Right | AnchorStyles.Bottom; stop.Margin = new Padding(0, 10, 0, 0);
                stop.Click += delegate {
                    if (CancellationRequested) return;
                    CancellationRequested = true; stop.Enabled = false; stop.Text = "중단 중";
                    stage.Text = "ZIP 확인 중단 중 · 파일은 삭제하지 않습니다.";
                    cancel();
                };
                layout.Controls.Add(stop, 0, 3); CancelButton = stop;
            }
            Controls.Add(layout);
            durationTick.Tick += delegate { if (!IsDisposed) amount.Text=amountDetail+
                " · 경과 "+Math.Floor(duration.Elapsed.TotalSeconds).ToString("0")+"초"; };
            Shown += delegate { duration.Restart(); durationTick.Start(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!finished) e.Cancel = true; };
            UpdateProgress(new WorkProgress("작업 준비 중"));
        }
        public void UpdateProgress(WorkProgress value)
        {
            if (IsDisposed || CancellationRequested) return;
            stage.Text = value.stage;
            bar.Style = value.total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
            if (value.total > 0) bar.Value = (int)Math.Min(100, value.completed * 100.0 / value.total);
            amountDetail = value.bytes ? String.Format("{0:N1} MB", value.completed / 1048576.0) +
                (value.total > 0 ? String.Format(" / {0:N1} MB", value.total / 1048576.0) : " · 전체 크기 확인 중")
                : value.total > 0 ? value.completed + " / " + value.total + "개 파일" : "작업 중입니다. 완료될 때까지 기다려 주세요.";
            amount.Text=amountDetail+(duration.IsRunning?" · 경과 "+Math.Floor(duration.Elapsed.TotalSeconds).ToString("0")+"초":"");
        }
        public void Finish() { finished = true; durationTick.Stop(); duration.Stop(); Close(); }
        internal void VerifyCancellation(string output)
        {
            UpdateProgress(new WorkProgress("ZIP 확인 중 (1/3) · 확인할 캠페인.zip", 1048576, 8388608, true));
            PerformLayout();
            if (CancelButton != stop || !stop.CanSelect || stop.Text != "중단" || bar.Value != 12)
                throw new InvalidOperationException("ZIP verification needs an available Stop button and byte progress.");
            TextLayout.VerifyLabels(this);
            using (var image = new Bitmap(Width, Height))
            {
                DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            }
            stop.PerformClick();
            if (!CancellationRequested || stop.Enabled || stop.Text != "중단 중")
                throw new InvalidOperationException("Stop must request cancellation once and wait for the read worker.");
            string cancelledStage = stage.Text;
            UpdateProgress(new WorkProgress("늦게 도착한 진행 상태"));
            if (stage.Text != cancelledStage)
                throw new InvalidOperationException("Queued progress must not overwrite the cancellation state.");
            Finish();
            Console.WriteLine("UI PASS: ZIP Stop is enabled while the owner is locked; cancellation waits for the worker; late progress is ignored.");
        }
        internal void VerifyFixture()
        {
            UpdateProgress(new WorkProgress("복사 중", 3, 10));
            if (bar.Value != 30 || !amount.Text.Contains("3 / 10")) throw new InvalidOperationException("File progress differs from actual counts.");
            UpdateProgress(new WorkProgress("다운로드 중", 1048576, 4194304, true));
            if (bar.Value != 25 || !amount.Text.Contains("1.0 MB")) throw new InvalidOperationException("Download progress differs from received bytes.");
            UpdateProgress(new WorkProgress("다운로드 중", 1048576, 0, true));
            if (bar.Style != ProgressBarStyle.Marquee) throw new InvalidOperationException("Unknown totals must use indeterminate progress.");
            foreach (var size in new[] { new Size(520, 260), new Size(780, 330) })
            { Size = size; PerformLayout(); TextLayout.VerifyLabels(this); }
            Close();
            if (IsDisposed) throw new InvalidOperationException("Running work must not be dismissible.");
            Console.WriteLine("UI PASS: progress uses actual files/bytes, unknown totals animate, close is blocked during work and text fits two sizes.");
        }
    }
    public sealed class AppDialog : ThemeForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        readonly MetalButton accept, cancel;
        readonly Label body;
        readonly DarkScrollPanel scroll;
        internal string MessageText { get; private set; }
        public AppDialog(string title, string message, string action = null, string cancelAction = null)
        {
            Text = title; MessageText = message; ClientSize = new Size(680, 400); MinimumSize = new Size(560, 340);
            Padding = new Padding(22); ShowInTaskbar = false; Font = new Font("맑은 고딕", 11.5f, FontStyle.Bold);
            // The dialog is app-owned, not the OS file picker. Give storage,
            // update and confirmation prompts the actual selected race material.
            FormBorderStyle = FormBorderStyle.None; ShowIcon = false;
            BackColor = GlassTheme.Background;
            GlassTheme.RoundWindow(this, 16);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68)); Controls.Add(layout);
            var header = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=2, RowCount=1,
                Height=51, BackColor=Color.Transparent, Margin=Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,42));
            var heading = new Label { Text = (LiveTheme.Current == LiveTheme.Classic ? "◆" : LiveTheme.Current.Glyph) + "  " + title,
                AutoSize=false, Dock=DockStyle.Fill, ForeColor=GlassTheme.Accent, BackColor=Color.Transparent,
                Font = new Font("맑은 고딕", 15, FontStyle.Bold), TextAlign=ContentAlignment.MiddleLeft,
                Margin = Padding.Empty };
            var dismiss = new MetalButton { Text="", IconName="close", Dock=DockStyle.Fill, DialogResult=DialogResult.Cancel,
                FlatStyle=FlatStyle.Flat, BackColor=GlassTheme.Field, ForeColor=GlassTheme.Text,
                Font=new Font("맑은 고딕",16,FontStyle.Bold), Cursor=Cursors.Hand,
                Margin=new Padding(6,4,0,9),AccessibleName="대화상자 취소" };
            dismiss.FlatAppearance.BorderSize=1;dismiss.FlatAppearance.BorderColor=GlassTheme.Accent;
            dismiss.FlatAppearance.MouseOverBackColor=GlassTheme.Surface;
            header.Controls.Add(heading,0,0);header.Controls.Add(dismiss,1,0);
            Action fitHeading=delegate {
                if(heading.ClientSize.Width<1)return;
                int height=Math.Max(51,TextLayout.Measure(heading,heading.Text,heading.ClientSize.Width).Height+8);
                if(header.MinimumSize.Height!=height)header.MinimumSize=new Size(0,height);
                if(header.Height!=height)header.Height=height;
            };
            header.SizeChanged+=delegate {fitHeading();};
            heading.FontChanged+=delegate {fitHeading();};
            MouseEventHandler move = delegate(object sender, MouseEventArgs e) {
                if(e.Button!=MouseButtons.Left)return;
                ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
            };
            heading.MouseDown+=move;header.MouseDown+=move;
            layout.Controls.Add(header,0,0);
            var surface = new MetalPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = Padding.Empty };
            var content = new Panel { BackColor = Color.Transparent };
            body = new Label { Text = message, ForeColor = GlassTheme.Text, Font = Font,
                BackColor = Color.Transparent, AutoSize = false, AccessibleName = title + " 내용" };
            content.Controls.Add(body);
            content.SizeChanged += delegate {
                body.Width = Math.Max(1, content.ClientSize.Width);
                body.Height = TextLayout.Measure(body, message, body.Width).Height + 12;
            };
            scroll = new DarkScrollPanel(content) { Dock = DockStyle.Fill, BackColor = Color.Transparent, HideUnusedScrollBar = true };
            surface.Controls.Add(scroll);
            layout.Controls.Add(surface, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 16, 0, 0), Margin = Padding.Empty };
            cancel = new MetalButton { Text = cancelAction ?? (action == null ? "확인" : "취소"), DialogResult = DialogResult.Cancel,
                AutoSize = true, MinimumSize = new Size(130, 48), Margin = new Padding(10, 0, 0, 0), Font = Font };
            buttons.Controls.Add(cancel); CancelButton = cancel;
            if (action != null)
            {
                accept = new MetalButton { Text = action, DialogResult = DialogResult.OK, Primary = true,
                    AutoSize = true, MinimumSize = new Size(170, 48), Margin = Padding.Empty, Font = Font };
                buttons.Controls.Add(accept);
            }
            AcceptButton = cancel; layout.Controls.Add(buttons, 0, 2);
        }
        public static void Info(IWin32Window owner, string message, string title)
        { using (var dialog = new AppDialog(title, message)) dialog.ShowDialog(owner); }
        public static bool Confirm(IWin32Window owner, string message, string title, string action, string cancelAction = null)
        { using (var dialog = new AppDialog(title, message, action, cancelAction)) return dialog.ShowDialog(owner) == DialogResult.OK; }
        internal void VerifyFixture()
        {
            if (AcceptButton != cancel || CancelButton != cancel || cancel.DialogResult != DialogResult.Cancel)
                throw new InvalidOperationException("Themed dialog must default to cancel.");
            foreach (var size in new[] { new Size(680, 400), new Size(1120, 760), new Size(560, 340),
                new Size(950, 350), new Size(590, 720), new Size(680, 400) })
            {
                ClientSize = size; PerformLayout(); scroll.ArrangeContent(); TextLayout.VerifyLabels(this);
                if (cancel.Height < 44 || (accept != null && (accept.Right > accept.Parent.ClientSize.Width || accept.Height < 44)))
                    throw new InvalidOperationException("Themed dialog actions clipped.");
                if (!body.Font.Bold || body.BackColor != Color.Transparent || body.Right > body.Parent.ClientSize.Width ||
                    body.Height < TextLayout.Measure(body, MessageText, body.Width).Height)
                    throw new InvalidOperationException("Dialog body must reflow with bold text and no separate background.");
            }
            Console.WriteLine("UI PASS: themed dialog reflows through six resize states; bold body, integrated background, default cancel.");
        }
    }
    public sealed class GlassMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return GlassTheme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return GlassTheme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return GlassTheme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return GlassTheme.Surface; } }
        public override Color MenuItemSelected { get { return GlassTheme.Field; } }
        public override Color MenuItemBorder { get { return GlassTheme.Accent; } }
        public override Color MenuBorder { get { return Color.FromArgb(56, 255, 255, 255); } }
    }
    public static class UiIcon
    {
        public static void Draw(Graphics graphics, string name, Rectangle bounds, Color color)
        {
            var state = graphics.Save();
            graphics.TranslateTransform(bounds.X, bounds.Y); graphics.ScaleTransform(bounds.Width / 24f, bounds.Height / 24f);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (name)
                {
                    case "close":
                        graphics.DrawLine(pen, 6, 6, 18, 18);
                        graphics.DrawLine(pen, 18, 6, 6, 18); break;
                    case "back":
                        graphics.DrawLines(pen, new[] { new PointF(10, 5), new PointF(3, 12), new PointF(10, 19) });
                        graphics.DrawLine(pen, 3, 12, 21, 12); break;
                    case "home":
                        graphics.DrawLines(pen, new[] { new PointF(3, 11), new PointF(12, 3), new PointF(21, 11) });
                        graphics.DrawLines(pen, new[] { new PointF(5, 10), new PointF(5, 21), new PointF(10, 21), new PointF(10, 15), new PointF(14, 15), new PointF(14, 21), new PointF(19, 21), new PointF(19, 10) }); break;
                    case "refresh":
                        graphics.DrawArc(pen, 4, 4, 16, 16, 30, 300);
                        graphics.DrawLines(pen, new[] { new PointF(20, 3), new PointF(20, 9), new PointF(14, 9) }); break;
                    case "external":
                        graphics.DrawLines(pen, new[] { new PointF(10, 4), new PointF(4, 4), new PointF(4, 20), new PointF(20, 20), new PointF(20, 14) });
                        graphics.DrawLine(pen, 11, 13, 21, 3);
                        graphics.DrawLines(pen, new[] { new PointF(14, 3), new PointF(21, 3), new PointF(21, 10) }); break;
                    case "history":
                        graphics.DrawArc(pen, 4, 4, 16, 16, 210, 300);
                        graphics.DrawLines(pen, new[] { new PointF(3, 3), new PointF(3, 9), new PointF(9, 9) });
                        graphics.DrawLines(pen, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(16, 14) }); break;
                    case "bank":
                        graphics.DrawEllipse(pen, 4, 3, 16, 6);
                        graphics.DrawArc(pen, 4, 6, 16, 7, 0, 180);
                        graphics.DrawArc(pen, 4, 11, 16, 7, 0, 180);
                        graphics.DrawArc(pen, 4, 16, 16, 7, 0, 180);
                        graphics.DrawLine(pen, 4, 6, 4, 19);
                        graphics.DrawLine(pen, 20, 6, 20, 19); break;
                    case "link":
                        graphics.DrawArc(pen, 2, 9, 12, 12, 45, 270);
                        graphics.DrawArc(pen, 10, 1, 12, 12, 225, 270);
                        graphics.DrawLine(pen, 8, 16, 16, 8); break;
                    case "info":
                        graphics.DrawEllipse(pen, 3, 3, 18, 18);
                        graphics.DrawLine(pen, 12, 11, 12, 17);
                        graphics.DrawEllipse(pen, 11.5f, 6.5f, 1, 1); break;
                }
            }
            graphics.Restore(state);
        }
    }
    /// <summary>
    /// Owned folder chooser for the race-themed settings window. Browses the
    /// filesystem without changing it; only returns a verified existing path.
    /// Native OS file pickers remain available for selecting game executables.
    /// </summary>
    public sealed class HangarFolderDialog : ThemeForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        readonly TextBox path = new TextBox();
        readonly TreeView folders = new TreeView();
        readonly Label feedback = new Label();
        readonly MetalButton choose = new MetalButton();
        public string SelectedPath { get; private set; }
        public HangarFolderDialog(string title, string guide, string initial)
        {
            Text = title; FormBorderStyle = FormBorderStyle.None;
            MinimizeBox=false; MaximizeBox=false; ShowInTaskbar=false;
            ClientSize = new Size(720,555); MinimumSize = new Size(620,475);
            Padding = new Padding(20);
            BackColor=GlassTheme.Background;ForeColor=GlassTheme.Text;
            ShowIcon=false;GlassTheme.RoundWindow(this,16);
            var layout = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,
                BackColor=Color.Transparent,Margin=Padding.Empty};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,61));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,60));
            Controls.Add(layout);
            var header = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,
                Margin=Padding.Empty,BackColor=Color.Transparent};
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,43));
            var heading = new Label {Text=LiveTheme.Current.Glyph+"  "+title,
                Dock=DockStyle.Fill,Font=new Font("맑은 고딕",16,FontStyle.Bold),
                ForeColor=GlassTheme.Accent,BackColor=Color.Transparent,
                TextAlign=ContentAlignment.MiddleLeft,Margin=Padding.Empty};
            var dismiss = new MetalButton {Text="",IconName="close",Dock=DockStyle.Fill,
                DialogResult=DialogResult.Cancel,FlatStyle=FlatStyle.Flat,
                BackColor=GlassTheme.Field,ForeColor=GlassTheme.Text,
                Font=new Font("맑은 고딕",16,FontStyle.Bold),Cursor=Cursors.Hand,
                Margin=new Padding(5,2,0,6),AccessibleName="폴더 선택 취소"};
            dismiss.FlatAppearance.BorderColor=GlassTheme.Accent;
            dismiss.FlatAppearance.MouseOverBackColor=GlassTheme.Surface;
            header.Controls.Add(heading,0,0);header.Controls.Add(dismiss,1,0);
            MouseEventHandler dragTitle=delegate(object sender,MouseEventArgs e){
                if(e.Button!=MouseButtons.Left)return;
                ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);
            };
            heading.MouseDown+=dragTitle;header.MouseDown+=dragTitle;
            layout.Controls.Add(header,0,0);
            var details = new Label {Text=guide,Font=new Font("맑은 고딕",10),
                Dock=DockStyle.Fill,ForeColor=GlassTheme.Muted,
                BackColor=Color.Transparent,TextAlign=ContentAlignment.MiddleLeft};
            layout.Controls.Add(details,0,1);
            path.Dock=DockStyle.Fill;path.Text=initial??"";
            path.BorderStyle=BorderStyle.FixedSingle;path.BackColor=GlassTheme.Field;
            path.ForeColor=GlassTheme.Text;path.Font=new Font("맑은 고딕",11);
            layout.Controls.Add(path,0,2);
            folders.Dock=DockStyle.Fill;folders.BorderStyle=BorderStyle.FixedSingle;
            folders.BackColor=GlassTheme.Field;folders.ForeColor=GlassTheme.Text;
            folders.LineColor=GlassTheme.Accent;folders.Font=new Font("맑은 고딕",10);
            folders.HideSelection=false;folders.ShowRootLines=true;
            layout.Controls.Add(folders,0,3);
            var buttons = new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=3,RowCount=1,
                BackColor=Color.Transparent,Margin=Padding.Empty};
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,120));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,180));
            feedback.Text="폴더를 선택하거나 경로를 붙여넣으세요.";
            feedback.ForeColor=GlassTheme.Muted;feedback.Dock=DockStyle.Fill;
            feedback.TextAlign=ContentAlignment.MiddleLeft;
            buttons.Controls.Add(feedback,0,0);
            var cancel = new MetalButton {Text="취소",Dock=DockStyle.Fill,
                DialogResult=DialogResult.Cancel,Margin=new Padding(7)};
            choose.Text="이 폴더 선택";choose.Dock=DockStyle.Fill;choose.Primary=true;
            choose.Margin=new Padding(7);
            buttons.Controls.Add(cancel,1,0);buttons.Controls.Add(choose,2,0);
            layout.Controls.Add(buttons,0,4);
            CancelButton=cancel;AcceptButton=cancel;
            choose.Click+=delegate {
                try
                {
                    string target=Path.GetFullPath(path.Text.Trim());
                    if(!Directory.Exists(target))
                    { feedback.Text="폴더가 존재하지 않습니다.";feedback.ForeColor=GlassTheme.Accent;return; }
                    SelectedPath=target;DialogResult=DialogResult.OK;
                }
                catch(Exception error)
                { feedback.Text=error is UnauthorizedAccessException?"선택한 폴더에 접근할 수 없습니다.":"폴더 경로를 확인해 주세요.";
                  feedback.ForeColor=GlassTheme.Accent; }
            };
            folders.AfterSelect+=delegate(object sender,TreeViewEventArgs e){
                string location=e.Node.Tag as string;
                if(!String.IsNullOrEmpty(location))path.Text=location;
            };
            folders.BeforeExpand+=async delegate(object sender,TreeViewCancelEventArgs e){
                string parent=e.Node.Tag as string;
                if(String.IsNullOrWhiteSpace(parent) || e.Node.Nodes.Count!=1 ||
                   e.Node.Nodes[0].Tag!=null)return;
                e.Node.Nodes[0].Text="폴더 확인 중...";
                string[] children=await Task.Run(() => {
                    try{return Directory.EnumerateDirectories(parent).Take(160).ToArray();}
                    catch{return new string[0];}
                });
                if(IsDisposed||folders.IsDisposed)return;
                e.Node.Nodes.Clear();
                foreach(var child in children)
                    AppendFolderNode(e.Node.Nodes,child);
            };
            try
            {
                foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady))
                    AppendFolderNode(folders.Nodes,drive.RootDirectory.FullName);
            }
            catch(Exception error){System.Diagnostics.Trace.WriteLine("Folder chooser drives: "+error.Message);}
            Shown+=delegate{path.Focus();path.SelectAll();};
        }
        static void AppendFolderNode(TreeNodeCollection nodes,string location)
        {
            var trimmed=location.TrimEnd(Path.DirectorySeparatorChar);
            var node=new TreeNode(String.IsNullOrEmpty(trimmed)?location:
                (Path.GetFileName(trimmed).Length>0?Path.GetFileName(trimmed):location));
            node.Tag=location;node.Nodes.Add(new TreeNode("폴더 펼치기"));
            nodes.Add(node);
        }
    }

    public sealed class VersionManagementDialog : ThemeForm
    {
        readonly Campaign[] versions;
        readonly ComboBox choice;
        readonly MetalButton apply, folder;
        public Campaign SelectedVersion { get { return versions[choice.SelectedIndex]; } }
        public VersionManagementDialog(Campaign installed, System.Collections.Generic.IEnumerable<Campaign> source, Action<Campaign> openFolder)
        {
            versions = source.ToArray();
            Text = installed.name + " · 버전관리·폴더관리";
            ClientSize = new Size(620, 170);
            choice = new ComboBox { Left = 20, Top = 24, Width = 580, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "관리할 보관 버전", BackColor = GlassTheme.Field, ForeColor = GlassTheme.Text, FlatStyle = FlatStyle.Flat };
            foreach (var item in versions) choice.Items.Add(item.version + " · " + item.versionKey.Substring(0, 10));
            choice.SelectedIndex = Math.Max(0, Array.FindIndex(versions, item => item.versionKey == installed.versionKey));
            apply = new MetalButton { Text = "버전 적용", Location = new Point(20, 88), Size = new Size(180, 48) };
            folder = new MetalButton { Text = "폴더 열기", Location = new Point(212, 88), Size = new Size(180, 48) };
            apply.Click += delegate { DialogResult = DialogResult.OK; };
            folder.Click += delegate { openFolder(SelectedVersion); };
            Controls.Add(choice); Controls.Add(apply); Controls.Add(folder);
        }
        internal void VerifyFixture(Campaign expected)
        {
            choice.SelectedIndex = Array.FindIndex(versions, item => item.versionKey == expected.versionKey);
            folder.PerformClick();
            if (DialogResult != DialogResult.None || apply.Text != "버전 적용" || folder.Text != "폴더 열기")
                throw new InvalidOperationException("Folder action must not apply or close version management.");
            apply.PerformClick();
            if (DialogResult != DialogResult.OK || SelectedVersion.versionKey != expected.versionKey)
                throw new InvalidOperationException("Apply must use the selected stored version.");
            Console.WriteLine("UI PASS: version/folder management opens selected version folder without applying; version apply selects the chosen version.");
        }
    }
    public sealed class GlassMenuRenderer : ToolStripProfessionalRenderer
    {
        public GlassMenuRenderer() : base(new GlassMenuColors()) { RoundedEdges = true; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(bounds, 16))
            using (var fill = new LinearGradientBrush(bounds, Color.FromArgb(33, 43, 67), Color.FromArgb(18, 25, 39), 30f))
                e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 16))
            using (var pen = new Pen(Color.FromArgb(56, Color.White))) e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(new Rectangle(2, 3, e.Item.Width - 4, e.Item.Height - 6), 10))
            using (var fill = new SolidBrush(Color.FromArgb(22, GlassTheme.Accent)))
            using (var border = new Pen(Color.FromArgb(76, GlassTheme.Accent)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            UiIcon.Draw(e.Graphics, (string)e.Item.Tag, new Rectangle(17, (e.Item.Height - 22) / 2, 22, 22), GlassTheme.Accent);
            TextRenderer.DrawText(e.Graphics, e.Text, e.Item.Font, new Rectangle(54, 0, e.Item.Width - 72, e.Item.Height),
                e.Item.Enabled ? GlassTheme.Text : GlassTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
    public sealed class RemovalDialog : ThemeForm
    {
        public RemovalKind Kind { get; private set; }
        readonly MetalButton previous = new MetalButton(), all = new MetalButton(), single = new MetalButton(), confirm = new MetalButton(), cancel = new MetalButton();
        readonly Label detail = new Label();
        public RemovalDialog(Campaign campaign, int count)
        {
            Text = "캠페인 제거"; ClientSize = new Size(760, 520); MinimumSize = new Size(776, 559);
            var panel = new MetalPanel { Dock = DockStyle.Fill, Padding = new Padding(24) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = GlassTheme.Background };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in new[] { 76, 62, 62, 62 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.Controls.Add(new Label { Text = campaign.name + " · " + campaign.version + "\n삭제할 범위를 선택하세요.",
                Dock = DockStyle.Fill, ForeColor = ForeColor, Font = Font }, 0, 0);
            previous.Text = "이전버전 삭제"; single.Text = "해당 캠페인만 삭제"; all.Text = "뱅크 제외 모든 정보 삭제";
            foreach (var button in new[] { previous, all, single }) { button.Dock = DockStyle.Fill; button.Font = new Font(Font, FontStyle.Bold); button.Margin = new Padding(0, 4, 0, 4); }
            previous.Click += delegate { SelectKind(RemovalKind.PreviousVersions);
                detail.Text = "이 캠페인의 이전 보관 버전을 정리합니다.\n최신 보관 버전·선택한 버전·현재 적용 중인 버전은 유지합니다."; };
            all.Click += delegate { SelectKind(RemovalKind.AllInformation);
                detail.Text = "이 캠페인의 전체 " + count + "개 보관 버전·적용 파일·확인된 ZIP·사이트 연결을 삭제합니다.\n뱅크·세이브·다른 캠페인과 공용 복구 백업은 유지합니다."; };
            single.Click += delegate { SelectKind(RemovalKind.CampaignOnly);
                detail.Text = "선택한 버전(" + campaign.version + ")만 삭제합니다. 이 버전이 적용 중이면 먼저 적용을 해제합니다.\n이전 버전·다른 버전·ZIP·뱅크·세이브는 유지합니다."; all.Invalidate(); single.Invalidate(); };
            layout.Controls.Add(previous, 0, 1); layout.Controls.Add(single, 0, 2); layout.Controls.Add(all, 0, 3);
            detail.Text = "보관소에서 제거한 자료는 휴지통으로 옮깁니다."; detail.Dock = DockStyle.Fill; detail.TextAlign = ContentAlignment.MiddleLeft; detail.ForeColor = GlassTheme.Muted;
            layout.Controls.Add(detail, 0, 4);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            confirm.Text = "선택한 범위 제거"; confirm.Enabled = false; confirm.MinimumSize = new Size(210, 44); confirm.AutoSize = true; confirm.Click += delegate { DialogResult = DialogResult.OK; };
            cancel.Text = "취소"; cancel.MinimumSize = new Size(100, 44); cancel.AutoSize = true; cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(confirm); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 5);
            panel.Controls.Add(layout); Controls.Add(panel); CancelButton = cancel;
        }
        internal void VerifyFixture()
        {
            if (confirm.Enabled) throw new InvalidOperationException("Removal must require explicit scope.");
            single.PerformClick();
            if (Kind != RemovalKind.CampaignOnly || !confirm.Enabled || !detail.Text.Contains("이전 버전") || !detail.Text.Contains("먼저 적용을 해제"))
                throw new InvalidOperationException("Single version choice must preserve old versions and explain automatic unapply.");
            all.PerformClick();
            if (Kind != RemovalKind.AllInformation || !confirm.Enabled || all.Text != "뱅크 제외 모든 정보 삭제" ||
                !detail.Text.Contains("ZIP") || !detail.Text.Contains("세이브"))
                throw new InvalidOperationException("Full cleanup must explicitly name ZIP removal and save preservation.");
            previous.PerformClick();
            if (Kind != RemovalKind.PreviousVersions || !confirm.Enabled || Icon == null) throw new InvalidOperationException("Previous versions choice or icon failed.");
            if (confirm.Right > confirm.Parent.Width || all.Width < TextRenderer.MeasureText(all.Text, all.Font).Width)
                throw new InvalidOperationException("Removal controls clipped.");
            Console.WriteLine("UI PASS: three themed removal choices, explicit confirmation, complete labels and archive window icon.");
        }
        void SelectKind(RemovalKind kind)
        {
            Kind = kind; confirm.Enabled = true;
            previous.Primary = kind == RemovalKind.PreviousVersions; all.Primary = kind == RemovalKind.AllInformation; single.Primary = kind == RemovalKind.CampaignOnly;
            previous.Invalidate(); all.Invalidate(); single.Invalidate();
        }
    }
    // A real CheckBox keeps keyboard / UI Automation behavior, but paints the
    // control in the launcher's dark palette instead of a white system square.
    public sealed class GlassCheckBox : CheckBox
    {
        public GlassCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = GlassTheme.Background;
            ForeColor = GlassTheme.Text;
            Cursor = Cursors.Hand;
            TabStop = true;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var square = new Rectangle(1, (Height - 18) / 2, 18, 18);
            using (var path = GlassTheme.Round(square, 4))
            using (var fill = new SolidBrush(Checked ? GlassTheme.Accent : Color.FromArgb(30, 39, 56)))
            using (var border = new Pen(Checked ? GlassTheme.Accent : Color.FromArgb(91, 107, 132)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            if (Checked)
                using (var pen = new Pen(GlassTheme.Background, 2.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    e.Graphics.DrawLine(pen, 5, square.Y + 9, 8, square.Y + 12);
                    e.Graphics.DrawLine(pen, 8, square.Y + 12, 15, square.Y + 5);
                }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(29, 0, Width - 29, Height),
                Enabled ? ForeColor : GlassTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(27, 1, Width - 28, Height - 2));
        }
    }
    public sealed class ApplyConfirmation : ThemeForm
    {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int message, IntPtr wParam, IntPtr lParam);

        readonly MetalButton confirm = new MetalButton(), cancel = new MetalButton();
        readonly GlassCheckBox doNotAsk = new GlassCheckBox();
        readonly Label question = new Label(), explanation = new Label();
        public bool DoNotAskAgain { get { return doNotAsk.Checked; } }
        public ApplyConfirmation(Campaign campaign, string gameRoot)
        {
            Text = "캠페인 적용 확인";
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(590, 300);
            MinimumSize = MaximumSize = Size;
            Padding = new Padding(8);
            BackColor = GlassTheme.Background;
            ShowInTaskbar = false; ShowIcon = false; KeyPreview = true;
            GlassTheme.RoundWindow(this, 18);

            var surface = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            Controls.Add(surface);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Padding = new Padding(18, 10, 18, 10), Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            surface.Controls.Add(layout);

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                BackColor = Color.Transparent, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
            var headerTitle = new Label { Text = "캠페인 적용 확인", Dock = DockStyle.Fill,
                ForeColor = GlassTheme.Text, Font = new Font(Font.FontFamily, 14.5f, FontStyle.Bold),
                Margin = Padding.Empty, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            var close = new Button { Text = "", Dock = DockStyle.Fill,
                BackColor = GlassTheme.Background, ForeColor = GlassTheme.Muted,
                FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand,
                Margin = new Padding(0, 2, 0, 10), TabStop = true, AccessibleName = "적용 취소하고 닫기" };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = GlassTheme.Surface;
            close.FlatAppearance.MouseDownBackColor = GlassTheme.Field;
            close.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float x = close.ClientSize.Width / 2f, y = close.ClientSize.Height / 2f;
                using (var pen = new Pen(GlassTheme.Muted, 1.8f))
                { e.Graphics.DrawLine(pen, x - 5, y - 5, x + 5, y + 5);
                  e.Graphics.DrawLine(pen, x + 5, y - 5, x - 5, y + 5); }
            };
            close.Click += delegate { DialogResult = DialogResult.Cancel; };
            header.Controls.Add(headerTitle, 0, 0); header.Controls.Add(close, 1, 0);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                ReleaseCapture(); SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            };
            header.MouseDown += drag; headerTitle.MouseDown += drag;
            layout.Controls.Add(header, 0, 0);

            var note = new Panel { Dock = DockStyle.Fill, BackColor = GlassTheme.Background, Margin = new Padding(0, 3, 0, 5) };
            note.Paint += delegate(object sender, PaintEventArgs e) {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var path = GlassTheme.Round(new Rectangle(1, 1, note.Width - 3, note.Height - 3), 11))
                using (var fill = new SolidBrush(GlassTheme.Field))
                using (var outline = new Pen(Color.FromArgb(120, GlassTheme.Accent)))
                using (var accent = new SolidBrush(GlassTheme.Accent))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(outline, path);
                    e.Graphics.FillRectangle(accent, 1, 22, 3, Math.Max(1, note.Height - 44));
                }
            };
            question.Text = "게임 메뉴로 나왔나요?";
            question.Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold);
            question.ForeColor = GlassTheme.Text; question.BackColor = Color.Transparent;
            question.Location = new Point(21, 10); question.Size = new Size(485, 40);
            explanation.Text = "진행 중인 미션을 종료한 뒤 적용해 주세요.\n미션 중 파일을 변경하면 오류가 발생할 수 있습니다.";
            explanation.Font = new Font(Font.FontFamily, 9.75f, FontStyle.Regular);
            explanation.ForeColor = GlassTheme.Muted; explanation.BackColor = Color.Transparent;
            explanation.Location = new Point(21, 50); explanation.Size = new Size(490, 59);
            note.Controls.Add(question); note.Controls.Add(explanation);
            layout.Controls.Add(note, 0, 1);

            var choice = new Panel { Dock = DockStyle.Fill, BackColor = GlassTheme.Background, Margin = Padding.Empty };
            doNotAsk.Text = "다시 묻지 않기"; doNotAsk.Font = new Font(Font.FontFamily, 10.5f);
            doNotAsk.Location = new Point(0, 4); doNotAsk.Size = new Size(230, 28);
            doNotAsk.AccessibleDescription = "선택하고 캠페인 적용을 누르면 설정의 적용 확인창 표시가 해제됩니다.";
            choice.Controls.Add(doNotAsk);
            layout.Controls.Add(choice, 0, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Right, ColumnCount = 2, RowCount = 1,
                Width = 276, Height = 48, BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            cancel.Text = "취소"; cancel.Dock = DockStyle.Fill; cancel.AutoSize = false;
            cancel.Margin = new Padding(0, 0, 10, 0);
            cancel.MinimumSize = new Size(105, 46); cancel.DialogResult = DialogResult.Cancel;
            cancel.Font = new Font(Font.FontFamily, 11.25f, FontStyle.Bold);
            confirm.Text = "캠페인 적용"; confirm.Dock = DockStyle.Fill; confirm.AutoSize = false;
            confirm.MinimumSize = new Size(151, 46); confirm.Margin = Padding.Empty;
            confirm.Font = new Font(Font.FontFamily, 11.25f, FontStyle.Bold); confirm.Primary = true;
            confirm.Click += delegate { DialogResult = DialogResult.OK; };
            actions.Controls.Add(cancel, 0, 0); actions.Controls.Add(confirm, 1, 0);
            layout.Controls.Add(actions, 0, 3);
            AcceptButton = cancel; CancelButton = cancel;
            Shown += delegate { cancel.Focus(); };
        }
        internal void SetDoNotAskAgainForTest(bool value) { doNotAsk.Checked = value; }
        internal void VerifyFixture()
        {
            Application.DoEvents();
            if (!confirm.Enabled || AcceptButton != cancel || CancelButton != cancel ||
                !confirm.Font.Bold || !cancel.Font.Bold || doNotAsk.Checked ||
                !question.Text.Contains("게임 메뉴") || DoNotAskAgain ||
                ClientSize != new Size(590, 300) || FormBorderStyle != FormBorderStyle.None ||
                explanation.Text.Contains("적용 위치") || Region == null ||
                Region.IsVisible(new Point(0, 0)) || !Region.IsVisible(new Point(Width / 2, Height / 2)))
                throw new InvalidOperationException("Apply warning must be compact, readable, and default to cancel.");
            PerformLayout(); TextLayout.VerifyLabels(this);
            doNotAsk.Checked = true;
            confirm.PerformClick();
            if (DialogResult != DialogResult.OK || !DoNotAskAgain)
                throw new InvalidOperationException("Explicit apply and do-not-ask selection must both work.");
            Console.WriteLine("UI PASS: compact dark apply dialog, bold actions, custom checkbox, default cancel.");
        }
    }
    public sealed class DistributionLinkPicker : ThemeForm
    {
        readonly FlowLayoutPanel choices = new FlowLayoutPanel();
        public string SelectedUrl { get; private set; }
        public DistributionLinkPicker(Campaign campaign, System.Collections.Generic.IEnumerable<CampaignDownload> links)
        {
            Text = "배포 파일 선택"; ClientSize = new Size(760, 500); MinimumSize = new Size(700, 420);
            var panel = new MetalPanel { Dock = DockStyle.Fill, Padding = new Padding(24) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label { Text = campaign.name + "\n열 배포 파일을 선택하세요. 다운로드·게임 적용은 자동으로 시작하지 않습니다.",
                AutoSize = true, Dock = DockStyle.Fill, ForeColor = GlassTheme.Text, Font = Font, Padding = new Padding(0, 0, 0, 16) }, 0, 0);
            choices.FlowDirection = FlowDirection.TopDown; choices.WrapContents = false; choices.Dock = DockStyle.Fill;
            foreach (var link in links)
            {
                var button = new MetalButton { Text = (String.IsNullOrWhiteSpace(link.label) ? "배포 파일" : link.label) + "\n" + new Uri(link.url).Host,
                    Font = Font, Height = 76, Width = 630, Margin = new Padding(0, 0, 0, 12), Tag = link.url };
                button.Click += delegate { SelectedUrl = link.url; DialogResult = DialogResult.OK; };
                choices.Controls.Add(button);
            }
            var scroll = new DarkScrollPanel(choices) { Dock = DockStyle.Fill };
            choices.SizeChanged += delegate {
                foreach (Control button in choices.Controls)
                { button.Width = Math.Max(1, choices.ClientSize.Width - 8); button.Height = Math.Max(76, TextLayout.Measure(button, button.Text, button.Width - 36).Height + 22); }
            };
            layout.Controls.Add(scroll, 0, 1);
            var cancel = new MetalButton { Text = "취소", AutoSize = true, MinimumSize = new Size(110, 48), DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right };
            layout.Controls.Add(cancel, 0, 2); panel.Controls.Add(layout); Controls.Add(panel); AcceptButton = cancel; CancelButton = cancel;
        }
        internal void ChooseFixture(int index) { ((Button)choices.Controls[index]).PerformClick(); }
        internal void VerifyFixture()
        {
            Application.DoEvents();
            if (SelectedUrl != null || DialogResult == DialogResult.OK) throw new InvalidOperationException("Distribution selection must not open automatically.");
            for (int i = 0; i < choices.Controls.Count; i++)
            {
                ChooseFixture(i);
                if (SelectedUrl != (string)choices.Controls[i].Tag || DialogResult != DialogResult.OK) throw new InvalidOperationException("Distribution link selection mismatch.");
                DialogResult = DialogResult.None;
            }
            Console.WriteLine("UI PASS: multiple distribution links require explicit selection and preserve exact URL.");
        }
    }
    public sealed class RemovalWarning : ThemeForm
    {
        readonly CheckBox menu = new CheckBox();
        readonly MetalButton confirm = new MetalButton();
        public RemovalWarning(string summary)
        {
            Text = "뱅크 제외 모든 정보 삭제"; ClientSize = new Size(780, 570); MinimumSize = new Size(796, 609);
            var panel = new MetalPanel { Dock = DockStyle.Fill };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = GlassTheme.Background };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            layout.Controls.Add(new Label { Text = "이 캠페인의 모든 버전·적용 파일·아래 ZIP과 연결 정보를 삭제합니다.\n적용 전 원본을 복원하고, 삭제 자료는 휴지통으로 보냅니다.\n뱅크·세이브·다른 캠페인과 공용 복구 백업은 유지합니다.",
                Dock = DockStyle.Fill, ForeColor = Color.FromArgb(255, 190, 165) }, 0, 0);
            var details = new Label { Text = summary, Width = 690, ForeColor = GlassTheme.Text, Font = Font };
            details.Height = TextRenderer.MeasureText(summary, Font, new Size(690, 100000), TextFormatFlags.WordBreak).Height + 20;
            layout.Controls.Add(new DarkScrollPanel(details) { Dock = DockStyle.Fill }, 0, 1);
            menu.Text = "캠페인을 종료하고 게임 메뉴로 나왔습니다."; menu.Dock = DockStyle.Fill; menu.ForeColor = ForeColor;
            menu.CheckedChanged += delegate { confirm.Enabled = menu.Checked; }; layout.Controls.Add(menu, 0, 2);
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            confirm.Text = "뱅크 제외 모두 삭제"; confirm.MinimumSize = new Size(240, 44); confirm.AutoSize = true; confirm.Enabled = false;
            confirm.Click += delegate { if (menu.Checked) DialogResult = DialogResult.OK; };
            var cancel = new MetalButton { Text = "취소", MinimumSize = new Size(100, 44), AutoSize = true, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(confirm); actions.Controls.Add(cancel); layout.Controls.Add(actions, 0, 3);
            panel.Controls.Add(layout); Controls.Add(panel); CancelButton = cancel;
        }
        internal void VerifyFixture()
        {
            if (confirm.Enabled) throw new InvalidOperationException("Cleanup warning defaults to no consent.");
            menu.Checked = true;
            if (!confirm.Enabled || Icon == null) throw new InvalidOperationException("Cleanup warning consent or icon failed.");
            Console.WriteLine("UI PASS: additional themed cleanup warning requires explicit menu-state confirmation.");
        }
    }
    public sealed class MetalPanel : Panel
    {
        public MetalPanel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = GlassTheme.Background;
            Padding = new Padding(22);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            PaintSurface(e.Graphics, Point.Empty);
        }
        internal void PaintSurface(Graphics graphics, Point offset)
        {
            if (Width < 2 || Height < 2) return;
            graphics.Clear(GlassTheme.Background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new Rectangle(1 - offset.X, 1 - offset.Y, Width - 3, Height - 4);
            using (var path = GlassTheme.Round(bounds, 20))
            using (var surface = new LinearGradientBrush(bounds, GlassTheme.Field, GlassTheme.Surface, 25f))
            using (var border = new Pen(GlassTheme.CustomBorder ?? Color.FromArgb(56, GlassTheme.Accent)))
            {
                graphics.FillPath(surface, path);
                graphics.DrawPath(border, path);
            }
        }
    }
    public sealed class GlassTextBlock : Label
    {
        public GlassTextBlock()
        {
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 9))
            using (var fill = new SolidBrush(GlassTheme.Field))
            using (var border = new Pen(Color.FromArgb(41, 255, 255, 255)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, Padding.Top,
                Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical)), ForeColor,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
    public sealed class GlassChoiceField : Control
    {
        readonly ListBox choices = new ListBox();
        readonly ToolStripDropDown popup = new ToolStripDropDown();
        readonly ToolStripControlHost host;
        int selectionBeforeOpen;
        public ListBox.ObjectCollection Items { get { return choices.Items; } }
        public int SelectedIndex { get { return choices.SelectedIndex; } set { choices.SelectedIndex = value; } }
        public object SelectedItem { get { return choices.SelectedItem; } }
        public int DropDownWidth { get; set; }
        public bool DroppedDown
        {
            get { return popup.Visible; }
            set { if (value) OpenChoices(); else popup.Close(); }
        }
        public GlassChoiceField()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Color.Transparent; ForeColor = GlassTheme.Text;
            TabStop = true; AccessibleRole = AccessibleRole.ComboBox;
            choices.BorderStyle = BorderStyle.None; choices.IntegralHeight = false;
            choices.BackColor = GlassTheme.Field; choices.ForeColor = GlassTheme.Text;
            choices.DrawMode = DrawMode.OwnerDrawFixed;
            choices.DrawItem += delegate(object sender, DrawItemEventArgs e) {
                if (e.Index < 0 || e.Index >= Items.Count) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using (var fill = new SolidBrush(selected ? Color.FromArgb(29, 61, 74) : GlassTheme.Field))
                    e.Graphics.FillRectangle(fill, e.Bounds);
                TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
                    Rectangle.Inflate(e.Bounds, -10, 0), GlassTheme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                e.DrawFocusRectangle();
            };
            choices.SelectedIndexChanged += delegate { Invalidate(); AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); };
            choices.MouseClick += delegate(object sender, MouseEventArgs e) {
                if (choices.IndexFromPoint(e.Location) >= 0) { popup.Close(); Focus(); }
            };
            choices.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.KeyCode == Keys.Escape) { SelectedIndex = selectionBeforeOpen; popup.Close(); Focus(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { popup.Close(); Focus(); e.Handled = true; }
            };
            host = new ToolStripControlHost(choices) { AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty };
            popup.AutoSize = false; popup.Padding = new Padding(1); popup.BackColor = GlassTheme.Field;
            popup.Items.Add(host); popup.Closed += delegate { Invalidate(); };
        }
        void OpenChoices()
        {
            if (!Enabled || Items.Count == 0 || popup.Visible) return;
            Focus(); selectionBeforeOpen = SelectedIndex;
            choices.Font = Font; choices.ItemHeight = Font.Height + 12;
            host.Size = new Size(Math.Max(Width, DropDownWidth), Math.Min(8, Items.Count) * choices.ItemHeight);
            choices.Size = host.Size; popup.Size = new Size(host.Width + 2, host.Height + 2);
            popup.Show(this, new Point(0, Height)); choices.Focus();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) DroppedDown = !DroppedDown;
        }
        protected override bool IsInputKey(Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End ||
                key == Keys.F4 || key == Keys.Enter || key == Keys.Space || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled) return;
            if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || (e.Alt && e.KeyCode == Keys.Down))
            { DroppedDown = !DroppedDown; e.Handled = true; }
            else if (Items.Count > 0)
            {
                if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1);
                else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, SelectedIndex - 1);
                else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
                else if (e.KeyCode == Keys.End) SelectedIndex = Items.Count - 1;
                else return;
                e.Handled = true;
            }
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e); if (!Enabled) popup.Close(); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 8))
            using (var fill = new SolidBrush(GlassTheme.Field))
            using (var border = new Pen(Focused ? GlassTheme.Accent : Color.FromArgb(56, 255, 255, 255)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            TextRenderer.DrawText(e.Graphics, SelectedItem == null ? "" : SelectedItem.ToString(), Font,
                new Rectangle(12, 3, Math.Max(1, Width - 52), Height - 6),
                Enabled ? GlassTheme.Text : GlassTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            int x = Width - 24, y = Height / 2;
            using (var arrow = new SolidBrush(Enabled ? GlassTheme.Text : GlassTheme.Muted))
                e.Graphics.FillPolygon(arrow, new[] { new Point(x - 5, y - 2), new Point(x + 5, y - 2), new Point(x, y + 3) });
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new ChoiceAccessibleObject(this); }
        sealed class ChoiceAccessibleObject : ControlAccessibleObject
        {
            readonly GlassChoiceField field;
            public ChoiceAccessibleObject(GlassChoiceField owner) : base(owner) { field = owner; }
            public override string Value { get { return field.SelectedItem == null ? "" : field.SelectedItem.ToString(); } }
            public override string DefaultAction { get { return field.DroppedDown ? "닫기" : "열기"; } }
            public override AccessibleStates State { get { return base.State | (field.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed); } }
            public override void DoDefaultAction() { if (field.Enabled) field.DroppedDown = !field.DroppedDown; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) popup.Dispose();
            base.Dispose(disposing);
        }
    }
    public sealed class GlassSearchField : Panel
    {
        readonly TextBox editor;
        public GlassSearchField(TextBox text)
        {
            editor = text; Padding = new Padding(12, 0, 12, 0);
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Transparent;
            editor.BorderStyle = BorderStyle.None;
            Controls.Add(editor);
            editor.GotFocus += delegate { Invalidate(); };
            editor.LostFocus += delegate { Invalidate(); };
        }
        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Math.Max(1, proposedSize.Width), Math.Max(MinimumSize.Height, editor.PreferredHeight + 24));
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (editor != null) editor.SetBounds(Padding.Left, Math.Max(0, (Height - editor.PreferredHeight) / 2),
                Math.Max(1, Width - Padding.Horizontal), editor.PreferredHeight);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 3 || Height < 3) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = GlassTheme.Round(new Rectangle(1, 1, Width - 3, Height - 3), 8))
            using (var fill = new SolidBrush(GlassTheme.Field))
            using (var border = new Pen(editor.Focused ? GlassTheme.Accent : Color.FromArgb(56, 255, 255, 255)))
            { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
        }
    }
    public sealed class DarkScrollBar : Control
    {
        int content, viewport, value, dragOffset = -1;
        internal bool PointerChanging { get; private set; }
        public event EventHandler ValueChanged;
        internal event EventHandler PointerReleased;
        public int Maximum { get { return Math.Max(0, content - viewport); } }
        public int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(0, Math.Min(Maximum, value));
                if (this.value == next) return;
                this.value = next; Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            }
        }
        public DarkScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
            Width = 18; TabStop = true; BackColor = GlassTheme.Background;
            AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "캠페인 목록 스크롤";
        }
        public void SetRange(int total, int visible)
        {
            if (content == total && viewport == visible) return;
            content = total; viewport = visible; Value = value; Invalidate();
        }
        Rectangle Thumb
        {
            get
            {
                int track = Math.Max(1, Height - 4);
                int height = Math.Min(track, Math.Max(24, content > 0 ? track * viewport / content : track));
                int y = 2 + (Maximum == 0 ? 0 : (int)((long)value * (track - height) / Maximum));
                return new Rectangle(3, y, Math.Max(1, Width - 6), height);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Maximum == 0 || Width < 7 || Height < 5) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = GlassTheme.Round(new Rectangle(3, 2, Width - 6, Height - 4), 6))
            using (var fill = new SolidBrush(GlassTheme.Field)) e.Graphics.FillPath(fill, track);
            using (var path = GlassTheme.Round(Thumb, 6))
            using (var fill = new SolidBrush(Capture || Focused ? GlassTheme.Accent : Color.FromArgb(93, 114, 140)))
                e.Graphics.FillPath(fill, path);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Maximum == 0) return;
            Focus();
            if (Thumb.Contains(e.Location)) { dragOffset = e.Y - Thumb.Top; Capture = true; }
            else
            {
                PointerChanging = true;
                try { Value += e.Y < Thumb.Top ? -viewport : viewport; }
                finally { PointerChanging = false; }
            }
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture || dragOffset < 0) return;
            int travel = Height - 4 - Thumb.Height;
            if (travel > 0)
            {
                PointerChanging = true;
                try { Value = (int)((long)(e.Y - 2 - dragOffset) * Maximum / travel); }
                finally { PointerChanging = false; }
            }
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool wasDragging = dragOffset >= 0;
            base.OnMouseUp(e);
            Capture = false; dragOffset = -1; Invalidate();
            if (wasDragging && PointerReleased != null) PointerReleased(this, EventArgs.Empty);
        }
        protected override bool IsInputKey(Keys keyData) { return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            switch (e.KeyCode)
            {
                case Keys.Up: Value -= Font.Height * 3; break;
                case Keys.Down: Value += Font.Height * 3; break;
                case Keys.PageUp: Value -= viewport; break;
                case Keys.PageDown: Value += viewport; break;
                case Keys.Home: Value = 0; break;
                case Keys.End: Value = Maximum; break;
                default: return;
            }
            e.Handled = true;
        }
    }
    public sealed class DarkScrollPanel : Panel
    {
        public bool HideUnusedScrollBar { get; set; }
        // Used by campaign pages that scroll by wheel/keyboard without a visible track.
        // The underlying bar still owns scroll range and position.
        public bool HideScrollBar { get; set; }
        // Opt-in for dense WinForms campaign shelves. A single cached image is
        // moved during a gesture; native child HWNDs are repositioned just once
        // when the animation finishes. This avoids repeated native child paints.
        public bool SnapshotGestureScroll { get; set; }
        readonly Control content;
        readonly DarkScrollBar bar = new DarkScrollBar();
        readonly ScrollSnapshotOverlay scrollPreview = new ScrollSnapshotOverlay();
        readonly System.Windows.Forms.Timer gestureTimer = new System.Windows.Forms.Timer { Interval = 16 };
        readonly System.Diagnostics.Stopwatch gestureClock = System.Diagnostics.Stopwatch.StartNew();
        readonly bool compositeChildren;
        bool arranging;
        bool snapshotActive;
        bool committingGesture;
        double gestureStart, gestureTarget, gesturePosition;
        long gestureStartedAt;
        long gestureLastAt;
        double gestureDurationMs;
        int wheelRemainder;
        public DarkScrollPanel(Control panel) : this(panel, false) { }
        public DarkScrollPanel(Control panel, bool compositeChildren)
        {
            this.compositeChildren = compositeChildren;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            content = panel; BackColor = GlassTheme.Background;
            content.Dock = DockStyle.None;
            var scrolling = content as ScrollableControl;
            if (scrolling != null) scrolling.AutoScroll = false;
            Controls.Add(content); bar.Dock = DockStyle.Right; Controls.Add(bar); bar.BringToFront();
            scrollPreview.Visible = false;
            scrollPreview.BackColor = GlassTheme.Background;
            scrollPreview.MouseWheel += Wheel;
            Controls.Add(scrollPreview);
            gestureTimer.Tick += delegate { AdvanceGestureFrame(); };
            bar.ValueChanged += delegate {
                if (SnapshotGestureScroll && bar.PointerChanging && !committingGesture &&
                    RequestSnapshotScroll(bar.Value, true)) return;
                if (snapshotActive && !committingGesture) EndSnapshotScroll();
                // Preserve layout, not pixels painted for the previous parent origin.
                SuspendLayout();
                try { content.Top = -bar.Value; }
                finally { ResumeLayout(false); }
                InvalidateParentSurfaces(content, new Rectangle(0, bar.Value, content.Width, ClientSize.Height));
                Invalidate();
            };
            bar.PointerReleased += delegate { if (snapshotActive) EndSnapshotScroll(); };
            content.Layout += delegate { ArrangeContent(); };
            content.MouseWheel += Wheel;
            bar.MouseWheel += Wheel;
            MouseWheel += Wheel;
            content.ControlAdded += delegate(object sender, ControlEventArgs e)
            {
                e.Control.MouseWheel += Wheel;
                e.Control.GotFocus += delegate
                {
                    if (e.Control.Top < bar.Value) bar.Value = e.Control.Top;
                    else if (e.Control.Bottom > bar.Value + ClientSize.Height) bar.Value = e.Control.Bottom - ClientSize.Height;
                };
            };
        }
        sealed class ScrollSnapshotOverlay : Control
        {
            internal Bitmap Snapshot;
            internal double Offset;
            public ScrollSnapshotOverlay()
            {
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                TabStop = false;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                if (Snapshot == null) return;
                int y = Math.Max(0, Math.Min(Snapshot.Height - 1, (int)Math.Round(Offset)));
                int width = Math.Min(ClientSize.Width, Snapshot.Width);
                int height = Math.Min(ClientSize.Height, Snapshot.Height - y);
                if (width <= 0 || height <= 0) return;
                e.Graphics.DrawImage(Snapshot, new Rectangle(0, 0, width, height),
                    new Rectangle(0, y, width, height), GraphicsUnit.Pixel);
            }
        }
        internal bool SnapshotIsActive { get { return snapshotActive; } }
        internal int SnapshotOffset { get { return (int)Math.Round(gesturePosition); } }
        internal int SnapshotTarget { get { return (int)Math.Round(gestureTarget); } }
        bool StartSnapshot()
        {
            if (snapshotActive) return true;
            if (!IsHandleCreated || !Visible || !content.Visible || bar.Maximum <= 0 ||
                content.Width <= 0 || content.Height <= 0 || content.Width > 2200 ||
                content.Height > 4400 || (long)content.Width * content.Height > 9000000) return false;
            Bitmap snapshot = null;
            try
            {
                snapshot = new Bitmap(content.Width, content.Height);
                content.DrawToBitmap(snapshot, new Rectangle(0, 0, snapshot.Width, snapshot.Height));
                scrollPreview.Snapshot = snapshot;
                gesturePosition = Math.Max(0, -content.Top);
                gestureStart = gesturePosition;
                gestureTarget = gesturePosition;
                scrollPreview.Offset = gesturePosition;
                // Layout may run while setting the overlay bounds. Do not
                // let it apply the new scrollbar target to native children.
                snapshotActive = true;
                scrollPreview.Bounds = new Rectangle(0, 0,
                    Math.Max(1, ClientSize.Width - (HideScrollBar ? 0 : bar.Width)), ClientSize.Height);
                scrollPreview.Show();
                scrollPreview.BringToFront();
                bar.BringToFront();
                return true;
            }
            catch (Exception error)
            {
                snapshotActive = false;
                if (snapshot != null) snapshot.Dispose();
                scrollPreview.Snapshot = null;
                System.Diagnostics.Trace.WriteLine("Gesture snapshot skipped: " + error.Message);
                return false;
            }
        }
        bool RequestSnapshotScroll(int target, bool pointer = false)
        {
            target = Math.Max(0, Math.Min(bar.Maximum, target));
            if (!StartSnapshot()) return false;
            if (pointer)
            {
                // A human dragging a thumb controls the position directly.
                // Never let a late easing animation chase the pointer.
                gestureTimer.Stop();
                gestureStart = gesturePosition = gestureTarget = target;
                scrollPreview.Offset = target;
                scrollPreview.Invalidate();
                return true;
            }
            if (Math.Abs(target - gestureTarget) < 0.5) return true;
            gestureStart = gesturePosition;
            gestureTarget = target;
            gestureStartedAt = gestureClock.ElapsedMilliseconds;
            if (!gestureTimer.Enabled) gestureLastAt = gestureStartedAt;
            gestureDurationMs = Math.Max(90, Math.Min(220, Math.Abs(target - gestureStart) * 0.28));
            gestureTimer.Start();
            return true;
        }
        internal bool ApplySnapshotWheel(int delta, int lines)
        {
            if (!SnapshotGestureScroll) return ApplyWheel(delta, lines);
            if (bar.Maximum <= 0 || delta == 0) return false;
            if (lines == 0) return true;
            double from = snapshotActive ? gestureTarget : bar.Value;
            // Keep the existing gesture animation; only increase the distance
            // travelled by each wheel notch in the campaign scroll surfaces.
            // Page-scroll wheel settings (-1) still move by one viewport.
            int distance = lines < 0 ? ClientSize.Height : Font.Height * lines * 2;
            int target = (int)Math.Round(Math.Max(0, Math.Min(bar.Maximum, from - (double)delta * distance / 120)));
            if (Math.Abs(target - from) < 0.5) return false;
            if (RequestSnapshotScroll(target)) return true;
            bar.Value = target; // Safe fallback if screenshot/canvas is unavailable.
            return true;
        }
        void AdvanceGestureFrame()
        {
            if (!snapshotActive) { gestureTimer.Stop(); return; }
            // Fixed-time easing with a continuously retargeted destination.
            // Repeated wheel notches do not restart a fixed-duration tween.
            long now = gestureClock.ElapsedMilliseconds;
            double dt = Math.Min(400, Math.Max(1, now - gestureLastAt));
            gestureLastAt = now;
            double gap = gestureTarget - gesturePosition;
            if (Math.Abs(gap) < .65)
            {
                gesturePosition = gestureTarget;
                EndSnapshotScroll();
                return;
            }
            gesturePosition += gap * (1 - Math.Exp(-dt / 67d));
            scrollPreview.Offset = gesturePosition;
            scrollPreview.Invalidate();
        }
        internal bool ApplySnapshotThumbForTest(int target)
        {
            if (!SnapshotGestureScroll) return false;
            return RequestSnapshotScroll(target, true);
        }
        internal void ReleaseSnapshotThumbForTest() { EndSnapshotScroll(); }
        internal void AdvanceGestureFrameForTest(double progress)
        {
            if (!snapshotActive) return;
            double t = Math.Max(0, Math.Min(1, progress));
            double eased = t * t * (3 - 2 * t);
            gesturePosition = gestureStart + (gestureTarget - gestureStart) * eased;
            scrollPreview.Offset = gesturePosition;
            scrollPreview.Invalidate();
            if (t >= 1) EndSnapshotScroll();
        }
        internal void HoldGestureForVisualCheck()
        {
            if (!snapshotActive) throw new InvalidOperationException("No gesture to hold.");
            gestureTimer.Stop();
        }
        void EndSnapshotScroll()
        {
            if (!snapshotActive) return;
            gestureTimer.Stop();
            // Flush native scrolling while the bitmap still covers child HWNDs.
            committingGesture = true;
            try
            {
                bar.Value = Math.Max(0, Math.Min(bar.Maximum, (int)Math.Round(gestureTarget)));
                content.Top = -bar.Value;
                InvalidateParentSurfaces(content, new Rectangle(0, bar.Value, content.Width, ClientSize.Height));
                Invalidate();
            }
            finally { committingGesture = false; }
            scrollPreview.Hide();
            snapshotActive = false;
            var snapshot = scrollPreview.Snapshot;
            scrollPreview.Snapshot = null;
            if (snapshot != null) snapshot.Dispose();
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // Buffer native child windows together, not only the host's background.
                if (compositeChildren) parameters.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return parameters;
            }
        }
        static void InvalidateParentSurfaces(Control control, Rectangle visible)
        {
            visible.Intersect(control.ClientRectangle);
            if (visible.IsEmpty || !control.Visible) return;
            var button = control as MetalButton;
            if (control.BackColor.A < 255 || (button != null && button.BlendWithSurface))
                control.Invalidate(visible);
            foreach (Control child in control.Controls)
            {
                var intersection = Rectangle.Intersect(visible, child.Bounds);
                if (intersection.IsEmpty) continue;
                intersection.Offset(-child.Left, -child.Top);
                InvalidateParentSurfaces(child, intersection);
            }
        }
        public void ResetPosition() { EndSnapshotScroll(); wheelRemainder = 0; bar.Value = 0; }
        void Wheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null && handled.Handled) return;
            bool moved = SnapshotGestureScroll ? ApplySnapshotWheel(e.Delta, SystemInformation.MouseWheelScrollLines) :
                ApplyWheel(e.Delta, SystemInformation.MouseWheelScrollLines);
            if (handled != null) handled.Handled = moved;
        }
        internal bool ApplyWheel(int delta, int lines)
        {
            if (bar.Maximum == 0 || delta == 0 || (delta > 0 && bar.Value == 0) || (delta < 0 && bar.Value == bar.Maximum))
                return false;
            if (lines == 0) return true;
            wheelRemainder += delta;
            int notches = wheelRemainder / 120;
            wheelRemainder %= 120;
            if (notches == 0) return true;
            int before = bar.Value;
            // Same user-approved two-fold wheel distance in the low-overhead
            // original UI and in the rich snapshot modes.
            int distance = lines < 0 ? ClientSize.Height : Font.Height * lines * 2;
            bar.Value -= notches * distance;
            return before != bar.Value;
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); ArrangeContent(); }
        protected override void OnVisibleChanged(EventArgs e)
        {
            if (!Visible) EndSnapshotScroll();
            base.OnVisibleChanged(e); ArrangeContent();
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            EndSnapshotScroll(); base.OnSizeChanged(e); ArrangeContent();
        }
        public void ArrangeContent()
        {
            if (arranging || content == null || !Visible) return;
            arranging = true;
            try
            {
                int width = Math.Max(1, ClientSize.Width - (HideScrollBar ? 0 : bar.Width) - 4);
                if (content.Width != width) content.Width = width;
                int bottom = content.Padding.Vertical;
                foreach (Control child in content.Controls) bottom = Math.Max(bottom, child.Bottom + child.Margin.Bottom + content.Padding.Bottom);
                content.Height = Math.Max(ClientSize.Height, bottom);
                bar.SetRange(content.Height, ClientSize.Height);
                bar.Visible = !HideScrollBar && (!HideUnusedScrollBar || content.Height > ClientSize.Height);
                if (!snapshotActive) content.Location = new Point(0, -bar.Value);
            }
            finally { arranging = false; }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                EndSnapshotScroll();
                gestureTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
    public enum ButtonAppearance { Action, Navigation, Tab }
    public class MetalButton : Button
    {
        public bool Primary { get; set; }
        public ButtonAppearance Appearance { get; set; }
        public Color CanvasColor { get; set; }
        public Image LeadingIcon { get; set; }
        public bool BlendWithSurface { get; set; }
        public string IconName { get; set; }
        bool hover;
        public MetalButton()
        {
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            CanvasColor = GlassTheme.Background; BackColor = GlassTheme.Background; ForeColor = GlassTheme.Text;
        }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        public override Size GetPreferredSize(Size proposedSize)
        {
            var textSize = TextLayout.Measure(this, Text, Int32.MaxValue);
            return new Size(Math.Max(MinimumSize.Width, textSize.Width + 36 + (LeadingIcon == null ? 0 : 36)), Math.Max(MinimumSize.Height, textSize.Height + 18));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (BlendWithSurface)
            {
                Control ancestor = this;
                Point offset = Point.Empty;
                while (ancestor != null && !(ancestor is MetalPanel))
                { offset.Offset(ancestor.Left, ancestor.Top); ancestor = ancestor.Parent; }
                if (ancestor is MetalPanel) ((MetalPanel)ancestor).PaintSurface(e.Graphics, offset);
                else e.Graphics.Clear(CanvasColor);
            }
            else e.Graphics.Clear(CanvasColor);
            var bounds = new Rectangle(1, 1, Width - 3, Height - 4);
            bool activePrimary = Primary && Enabled;
            Color text = !Enabled ? GlassTheme.Muted : Appearance == ButtonAppearance.Navigation && activePrimary ? GlassTheme.Accent :
                Appearance == ButtonAppearance.Action && activePrimary ? GlassTheme.Background : GlassTheme.Text;
            if(Enabled&&GlassTheme.CustomButtonText.HasValue)text=GlassTheme.CustomButtonText.Value;
            if (Appearance != ButtonAppearance.Tab)
            {
                Color fillColor = Appearance == ButtonAppearance.Navigation ? Color.FromArgb(activePrimary ? 12 : hover ? 10 : 0, GlassTheme.Accent) :
                    activePrimary ? GlassTheme.Accent : Color.FromArgb(hover && Enabled ? 27 : 14, 255, 255, 255);
                using (var path = GlassTheme.ButtonOutline(bounds))
                using (var fill = new SolidBrush(fillColor))
                using (var border = new Pen(GlassTheme.CustomBorder ?? (Appearance == ButtonAppearance.Navigation ? Color.FromArgb(activePrimary ? 54 : 0, GlassTheme.Accent) :
                    activePrimary ? Color.FromArgb(140, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255))))
                { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path); }
            }
            else if (activePrimary)
                using (var pen = new Pen(GlassTheme.Accent, 3)) e.Graphics.DrawLine(pen, 10, Height - 3, Width - 10, Height - 3);
            if (IconName != null)
            {
                UiIcon.Draw(e.Graphics, IconName, new Rectangle((Width - 22) / 2, (Height - 22) / 2, 22, 22), GlassTheme.CustomIcon ?? (hover ? GlassTheme.Accent : text));
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5));
                return;
            }
            int inset = LeadingIcon == null ? 9 : 45;
            if (TextAlign == ContentAlignment.MiddleLeft) inset += 10;
            if (LeadingIcon != null)
                e.Graphics.DrawImage(LeadingIcon, new Rectangle(9, (Height - 30) / 2, 30, 30));
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(inset, 3, Width - inset - 9, Height - 6),
                text,
                (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter) |
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues)
                using (var pen = new Pen(GlassTheme.Accent))
                using (var outline = GlassTheme.Round(Rectangle.Inflate(bounds, -3, -3), 6))
                    e.Graphics.DrawPath(pen, outline);
        }
    }
}
