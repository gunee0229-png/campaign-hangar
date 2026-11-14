using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CampaignHangar
{
    // The standalone installer cannot depend on the installed application's
    // theme libraries. Keep its Terran palette matched to LiveTheme.Terran.
    internal static class InstallerTerranSkin
    {
        internal static readonly Color Background = Color.FromArgb(8, 11, 18);
        internal static readonly Color Field = Color.FromArgb(16, 24, 35);
        internal static readonly Color Surface = Color.FromArgb(24, 35, 47);
        internal static readonly Color Accent = Color.FromArgb(255, 169, 80);
        internal static readonly Color Secondary = Color.FromArgb(255, 101, 114);
        internal static readonly Color Text = Color.FromArgb(247, 244, 239);
        internal static readonly Color Muted = Color.FromArgb(158, 170, 183);
        internal static readonly Color Edge = Color.FromArgb(91, 86, 83);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        internal static void DarkTitlebar(Form form)
        {
            try
            {
                int dark = 1;
                int caption = ColorTranslator.ToWin32(Color.FromArgb(18, 23, 30));
                int text = ColorTranslator.ToWin32(Text);
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                DwmSetWindowAttribute(form.Handle, 36, ref text, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    internal sealed class InstallerTerranSurface : Panel
    {
        readonly Image hangar;

        internal InstallerTerranSurface(Image background)
        {
            hangar = background;
            Dock = DockStyle.Fill;
            BackColor = InstallerTerranSkin.Background;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            using (var background = new LinearGradientBrush(r,
                InstallerTerranSkin.Field, InstallerTerranSkin.Background, LinearGradientMode.Vertical))
                g.FillRectangle(background, r);
            int header = Math.Min(154, Height);
            if (hangar != null && header > 0)
            {
                var oldClip = g.Clip;
                try
                {
                    g.SetClip(new Rectangle(0, 0, Width, header));
                    double aspect = (double)Width / header;
                    int sourceWidth = hangar.Width;
                    int sourceHeight = Math.Min(hangar.Height, (int)Math.Round(sourceWidth / aspect));
                    int y = Math.Max(0, (hangar.Height - sourceHeight) / 2);
                    g.DrawImage(hangar, new Rectangle(0, 0, Width, header),
                        new Rectangle(0, y, sourceWidth, sourceHeight), GraphicsUnit.Pixel);
                    using (var veil = new LinearGradientBrush(new Rectangle(0, 0, Width, header),
                        Color.FromArgb(244, 8, 11, 18), Color.FromArgb(125, 8, 11, 18),
                        LinearGradientMode.Horizontal))
                        g.FillRectangle(veil, 0, 0, Width, header);
                }
                finally { g.Clip = oldClip; oldClip.Dispose(); }
            }
            using (var line = new Pen(InstallerTerranSkin.Edge))
            {
                g.DrawLine(line, 0, header - 1, Width, header - 1);
                g.DrawRectangle(line, 24, 164, Width - 49, 148);
            }
            using (var highlight = new Pen(InstallerTerranSkin.Accent, 2))
            {
                g.DrawLine(highlight, 24, header - 1, 152, header - 1);
                g.DrawLine(highlight, 25, 165, 80, 165);
            }
            using (var panel = new LinearGradientBrush(new Rectangle(25, 165, Width - 50, 146),
                Color.FromArgb(28, 38, 50), Color.FromArgb(13, 21, 31),
                LinearGradientMode.Vertical))
                g.FillRectangle(panel, 25, 165, Width - 50, 146);
            using (var bar = new SolidBrush(InstallerTerranSkin.Secondary))
                g.FillRectangle(bar, 24, 164, 4, 18);
            using (var thin = new Pen(Color.FromArgb(44, 59, 72)))
            {
                for (int i = 0; i < 6; i++)
                    g.DrawLine(thin, Width - 220 + i * 28, 16, Width - 195 + i * 28, 1);
                g.DrawLine(thin, 24, 389, Width - 24, 389);
            }
        }
    }

    internal sealed class InstallerTerranButton : Button
    {
        readonly bool primary;
        readonly bool quiet;
        bool hovered;

        internal InstallerTerranButton(bool isPrimary, bool isQuiet)
        {
            primary = isPrimary;
            quiet = isQuiet;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = InstallerTerranSkin.Surface;
            UseVisualStyleBackColor = false;
            BackColor = InstallerTerranSkin.Field;
            ForeColor = InstallerTerranSkin.Text;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(InstallerTerranSkin.Background);
            int cut = Math.Min(11, Height / 4);
            using (var path = new GraphicsPath())
            {
                path.AddPolygon(new[] { new Point(1, 1), new Point(Width - cut - 2, 1),
                    new Point(Width - 2, cut + 1), new Point(Width - 2, Height - 2),
                    new Point(cut + 1, Height - 2), new Point(1, Height - cut - 2) });
                Color top = primary ? Color.FromArgb(116, 78, 37) :
                    quiet ? Color.FromArgb(24, 33, 44) : Color.FromArgb(33, 45, 57);
                Color bottom = primary ? Color.FromArgb(51, 34, 24) :
                    quiet ? Color.FromArgb(12, 19, 29) : Color.FromArgb(16, 27, 37);
                if (hovered) top = ControlPaint.Light(top, .2f);
                if (MouseButtons == MouseButtons.Left && ClientRectangle.Contains(PointToClient(Cursor.Position)))
                    bottom = ControlPaint.Light(bottom, .12f);
                using (var brush = new LinearGradientBrush(ClientRectangle, top, bottom, LinearGradientMode.Vertical))
                    g.FillPath(brush, path);
                using (var line = new Pen(primary ? InstallerTerranSkin.Accent :
                    hovered ? InstallerTerranSkin.Accent : InstallerTerranSkin.Edge))
                    g.DrawPath(line, path);
                using (var accent = new Pen(primary ? InstallerTerranSkin.Accent :
                    InstallerTerranSkin.Secondary, 2))
                    g.DrawLine(accent, 10, Height - 5, primary ? Width - 27 : 34, Height - 5);
            }
            Color text = primary ? Color.FromArgb(255, 241, 222) :
                quiet ? InstallerTerranSkin.Muted : InstallerTerranSkin.Text;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(9, 2, Width - 18, Height - 5),
                Enabled ? text : InstallerTerranSkin.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(5, 5, Width - 11, Height - 11),
                    InstallerTerranSkin.Text, InstallerTerranSkin.Field);
        }
    }

    static class Installer
    {
        static string DefaultTarget
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CampaignHangar"); }
        }

        static string StartMenuShortcut
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "커캠 격납고.lnk"); }
        }

        static bool HasEntries(string directory)
        {
            using (var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
                return entries.MoveNext();
        }

        static bool IsOurInstallation(string directory)
        {
            string executable = Path.Combine(directory, "CampaignHangar.exe");
            if (!File.Exists(executable)) return false;
            try
            {
                return String.Equals(FileVersionInfo.GetVersionInfo(executable).ProductName,
                    "CampaignHangar." + AppChannel.Id, StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        // A selected location is a *parent* directory. Always put the actual
        // installation inside CampaignHangar, even when the user selects E:\.
        // Reuse an existing CampaignHangar folder (or a verified old install)
        // instead of creating CampaignHangar\CampaignHangar.
        static string ResolveTarget(string input)
        {
            string requested = Environment.ExpandEnvironmentVariables((input ?? "").Trim().Trim('"'));
            if (requested.Length < 3 || !Char.IsLetter(requested[0]) || requested[1] != ':' ||
                (requested[2] != '\\' && requested[2] != '/'))
                throw new InvalidDataException("설치 위치는 C:\\ 또는 D:\\처럼 시작하는 로컬 드라이브의 전체 경로로 지정해 주세요.");
            string selected = Path.GetFullPath(requested);
            string normalized = selected.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            bool driveRoot = String.Equals(selected, Path.GetPathRoot(selected), StringComparison.OrdinalIgnoreCase);
            if (!driveRoot &&
                (String.Equals(Path.GetFileName(normalized), "CampaignHangar", StringComparison.OrdinalIgnoreCase) ||
                 IsOurInstallation(normalized)))
                return normalized;
            return Path.Combine(selected, "CampaignHangar");
        }

        // Do not infer ownership from a nonempty directory or from an EXE name.
        // A first install must use a new/empty app folder; a reinstall may
        // replace only an existing app of the same release channel.
        static string ValidateTarget(string input)
        {
            string target = ResolveTarget(input);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            if (String.Equals(target, windows, StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Windows 시스템 폴더에는 설치할 수 없습니다.");
            if (File.Exists(target))
                throw new InvalidDataException("선택한 설치 위치에 같은 이름의 파일이 있습니다.");
            for (string directory = target; !String.IsNullOrEmpty(directory); directory = Path.GetDirectoryName(directory))
                if (Directory.Exists(directory) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("연결 폴더(바로가기, 정션, 심볼릭 링크)에는 설치하지 않습니다.");
            if (Directory.Exists(target) && HasEntries(target) && !IsOurInstallation(target))
                throw new InvalidDataException("CampaignHangar 폴더에 다른 파일이 있습니다. 다른 위치를 선택해 주세요.");
            return target;
        }

        // The current user's AppData is normally writable without elevation.
        // Probe the selected destination before starting any actual install,
        // and request UAC only when Windows denies write access.
        static bool CanWriteTarget(string target)
        {
            string parent = target;
            while (!Directory.Exists(parent))
            {
                parent = Path.GetDirectoryName(parent);
                if (String.IsNullOrEmpty(parent))
                    throw new DirectoryNotFoundException("설치 위치의 드라이브를 찾지 못했습니다.");
            }

            string probe = Path.Combine(parent, ".hangar-write-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(probe);
                using (var file = new FileStream(Path.Combine(probe, "test.tmp"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
                    file.WriteByte(1);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                // A failed probe must not leave the user's real app altered.
                if (Directory.Exists(probe))
                {
                    // Newly written files may be briefly held by Defender or
                    // another scanner even when the target is writable.
                    for (int retry = 0; retry < 6; retry++)
                    {
                        try { Directory.Delete(probe, true); break; }
                        catch (IOException) { if (retry == 5) throw; }
                        catch (UnauthorizedAccessException) { if (retry == 5) throw; }
                        Thread.Sleep(120 * (retry + 1));
                    }
                }
            }
        }

        static bool InstallWithAdministratorApproval(string executable, string target)
        {
            try
            {
                using (var elevated = Process.Start(new ProcessStartInfo(executable)
                {
                    Arguments = "--elevated-install \"" + target + "\"",
                    Verb = "runas",
                    UseShellExecute = true
                }))
                {
                    if (elevated == null)
                        throw new IOException("관리자 권한 설치 프로그램을 실행하지 못했습니다.");
                    elevated.WaitForExit();
                    if (elevated.ExitCode != 0)
                        throw new IOException("관리자 권한으로 설치하지 못했습니다. 설치 위치와 디스크 권한을 확인해 주세요.");
                }
                return true;
            }
            catch (Win32Exception error)
            {
                if (error.NativeErrorCode == 1223) return false; // UAC cancelled
                throw;
            }
        }

        static string ExistingTarget()
        {
            if (!File.Exists(StartMenuShortcut)) return DefaultTarget;
            object shell = null, shortcut = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return DefaultTarget;
                shell = Activator.CreateInstance(shellType);
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { StartMenuShortcut });
                string executable = Convert.ToString(shortcut.GetType().InvokeMember("TargetPath",
                    BindingFlags.GetProperty, null, shortcut, null));
                if (String.Equals(Path.GetFileName(executable), "CampaignHangarSetup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    string directory = Path.GetDirectoryName(executable);
                    if (!String.IsNullOrEmpty(directory) && IsOurInstallation(directory))
                        return directory;
                }
            }
            catch (Exception) { /* A missing/stale shortcut must not prevent installation. */ }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.ReleaseComObject(shortcut);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
            return DefaultTarget;
        }

        static Label SkinLabel(string text, int left, int top, int width, int height,
            float size, Color color, bool strong)
        {
            return new Label { Text = text, Left = left, Top = top, Width = width, Height = height,
                BackColor = Color.Transparent, ForeColor = color, UseMnemonic = false,
                Font = new Font("Malgun Gothic", size, strong ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft };
        }

        static string ChooseTarget(string version, string backgroundPath)
        {
            Image artwork = null;
            if (File.Exists(backgroundPath)) artwork = Image.FromFile(backgroundPath);
            using (artwork)
            using (var form = new Form { Text = "커캠 격납고 설치", StartPosition = FormStartPosition.CenterScreen,
                ClientSize = new Size(760, 470), MinimumSize = new Size(764, 510),
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                ShowIcon = false, AutoScaleMode = AutoScaleMode.Dpi, Font = new Font("Malgun Gothic", 9.5f),
                BackColor = InstallerTerranSkin.Background, ForeColor = InstallerTerranSkin.Text })
            {
                var surface = new InstallerTerranSurface(artwork);
                form.Controls.Add(surface);
                surface.Controls.Add(SkinLabel("TERRAN / TACTICAL COMMAND", 30, 15, 480, 25,
                    9f, InstallerTerranSkin.Accent, true));
                surface.Controls.Add(SkinLabel("커캠 격납고 설치", 30, 44, 530, 49,
                    20f, InstallerTerranSkin.Text, true));
                surface.Controls.Add(SkinLabel("v" + version + "  ·  설치 위치 선택", 33, 98, 510, 30,
                    10f, InstallerTerranSkin.Muted, false));
                surface.Controls.Add(SkinLabel("SETUP  /  01", 604, 22, 137, 22,
                    9f, InstallerTerranSkin.Accent, true));
                surface.Controls.Add(SkinLabel("01   설치 폴더 / INSTALL DESTINATION", 37, 173, 555, 28,
                    9f, InstallerTerranSkin.Accent, true));

                var inputFrame = new Panel { Left = 36, Top = 208, Width = 535, Height = 43,
                    BackColor = InstallerTerranSkin.Edge };
                var inputBody = new Panel { Left = 1, Top = 1, Width = 533, Height = 41,
                    BackColor = InstallerTerranSkin.Field };
                var pathBox = new TextBox { Left = 10, Top = 9, Width = 510, Height = 26,
                    BorderStyle = BorderStyle.None, Text = ExistingTarget(),
                    BackColor = InstallerTerranSkin.Field, ForeColor = InstallerTerranSkin.Text,
                    Font = new Font("Malgun Gothic", 10.5f), TabIndex = 0 };
                inputBody.Controls.Add(pathBox);
                inputFrame.Controls.Add(inputBody);
                surface.Controls.Add(inputFrame);

                var browse = new InstallerTerranButton(false, false) { Left = 585, Top = 208,
                    Width = 139, Height = 43, Text = "찾아보기", TabIndex = 1 };
                var reset = new InstallerTerranButton(false, true) { Left = 36, Top = 263,
                    Width = 131, Height = 35, Text = "기본 경로", TabIndex = 2 };
                surface.Controls.Add(browse);
                surface.Controls.Add(reset);
                var resolvedPath = SkinLabel("", 181, 265, 505, 31,
                    8.8f, InstallerTerranSkin.Muted, false);
                resolvedPath.AutoEllipsis = true;
                surface.Controls.Add(resolvedPath);

                string reminder = "설치 전 격납고를 닫아 주세요. 기존 앱과 캠페인·설정은 보존됩니다.\n" +
                    "권한이 필요한 설치 위치에는 Windows 관리자 승인을 요청합니다.";
                var note = SkinLabel(reminder, 30, 327, 694, 58, 9.3f,
                    InstallerTerranSkin.Muted, false);
                surface.Controls.Add(note);
                var install = new InstallerTerranButton(true, false) { Left = 454, Top = 405,
                    Width = 131, Height = 43, Text = "설치", TabIndex = 3 };
                var cancel = new InstallerTerranButton(false, true) { Left = 598, Top = 405,
                    Width = 127, Height = 43, Text = "취소", TabIndex = 4,
                    DialogResult = DialogResult.Cancel };
                surface.Controls.Add(cancel);
                surface.Controls.Add(install);

                pathBox.TextChanged += delegate {
                    note.Text = reminder; note.ForeColor = InstallerTerranSkin.Muted;
                    try { resolvedPath.Text = "설치 위치: " + ResolveTarget(pathBox.Text); }
                    catch (Exception) { resolvedPath.Text = "선택한 폴더 안에 CampaignHangar 폴더를 만듭니다."; }
                };
                resolvedPath.Text = "설치 위치: " + ResolveTarget(pathBox.Text);
                reset.Click += delegate { pathBox.Text = DefaultTarget; pathBox.SelectionStart = pathBox.Text.Length; pathBox.Focus(); };
                browse.Click += delegate
                {
                    using (var dialog = new FolderBrowserDialog { Description = "폴더를 선택하면 그 안에 CampaignHangar 폴더를 자동으로 만듭니다.",
                        ShowNewFolderButton = true })
                    {
                        string candidate = pathBox.Text;
                        try
                        {
                            while (!String.IsNullOrEmpty(candidate) && !Directory.Exists(candidate))
                                candidate = Path.GetDirectoryName(candidate);
                            if (!String.IsNullOrEmpty(candidate)) dialog.SelectedPath = candidate;
                        }
                        catch (ArgumentException) { /* An invalid typed path can still be replaced. */ }
                        if (dialog.ShowDialog(form) != DialogResult.OK) return;
                        try { pathBox.Text = ResolveTarget(dialog.SelectedPath); }
                        catch (Exception) { pathBox.Text = dialog.SelectedPath; }
                    }
                };
                install.Click += delegate
                {
                    try
                    {
                        form.Tag = ValidateTarget(pathBox.Text);
                        form.DialogResult = DialogResult.OK;
                        form.Close();
                    }
                    catch (Exception error)
                    {
                        // Native white message boxes break the Terran installer
                        // identity. Keep errors visible in the themed HUD.
                        note.ForeColor = InstallerTerranSkin.Secondary;
                        note.Text = "설치 경로 확인: " + error.Message;
                        pathBox.Focus();
                    }
                };
                form.AcceptButton = install;
                form.CancelButton = cancel;
                form.Shown += delegate {
                    InstallerTerranSkin.DarkTitlebar(form);
                    browse.Focus();
                    pathBox.SelectionStart = 0; pathBox.SelectionLength = 0;
                };
                if (form.ShowDialog() != DialogResult.OK) return null;
                return (string)form.Tag;
            }
        }

        static AppRelease Unpack(string workspace)
        {
            var assembly=Assembly.GetExecutingAssembly();
            AppRelease release;
            using(var input=new StreamReader(assembly.GetManifestResourceStream("release.json")))
                release=AppUpdate.Parse(input.ReadToEnd().TrimStart('\uFEFF'));
            string zip=Path.Combine(workspace,"release.zip");
            using(var input=assembly.GetManifestResourceStream("release.zip"))
            using(var output=File.Create(zip))AppUpdate.CopyBounded(input,output,release.size);
            AppUpdate.Extract(release,zip,Path.Combine(workspace,"app"));
            return release;
        }
        static void Install(string source,string target,string backup)
        {
            // Recheck immediately before writing. An existing but empty app
            // directory is a valid first-install target, not an update.
            target = ValidateTarget(target);
            if(IsOurInstallation(target))
                AppUpdate.Replace(source,target,backup);
            else {
                if(Directory.Exists(target) && HasEntries(target))
                    throw new InvalidDataException("설치 폴더가 비어 있지 않습니다.");
                // Prepare and verify every app file before publishing a new
                // installation. A scanner-blocked copy must not leave a
                // half-installed CampaignHangar folder that then rejects
                // every later reinstall as an unknown application.
                bool exists = Directory.Exists(target);
                string stage = target + ".install-" + Guid.NewGuid().ToString("N");
                Directory.CreateDirectory(stage);
                try
                {
                    foreach (string name in AppUpdate.Files)
                    {
                        string output = Path.Combine(stage, name);
                        Directory.CreateDirectory(Path.GetDirectoryName(output));
                        File.Copy(Path.Combine(source, name), output, false);
                        if (!String.Equals(AppUpdate.Hash(Path.Combine(source, name)),
                            AppUpdate.Hash(output), StringComparison.OrdinalIgnoreCase))
                            throw new IOException("설치 파일 검증에 실패했습니다: " + name);
                    }
                    if (!exists)
                    {
                        for (int attempt = 0; ; attempt++)
                        {
                            if (Directory.Exists(target) || File.Exists(target))
                                throw new IOException("설치 진행 중 대상 경로에 다른 파일이 생겼습니다. 원본을 덮어쓰지 않았습니다.");
                            try { Directory.Move(stage, target); break; }
                            catch (Exception error)
                            {
                                if (!(error is UnauthorizedAccessException || error is IOException) ||
                                    attempt == 6 || (error.HResult & 0xffff) != 5 &&
                                    (error.HResult & 0xffff) != 32 && (error.HResult & 0xffff) != 33)
                                    throw;
                                Thread.Sleep(150 * (attempt + 1));
                            }
                        }
                    }
                    else
                    {
                        // The user explicitly selected an existing empty
                        // folder. Never delete that folder or unrelated files.
                        var created = new List<string>();
                        try
                        {
                            foreach (string name in AppUpdate.Files)
                            {
                                string destination = Path.Combine(target, name);
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                File.Copy(Path.Combine(stage, name), destination, false);
                                created.Add(destination);
                                if (AppUpdate.Hash(destination) != AppUpdate.Hash(Path.Combine(stage, name)))
                                    throw new IOException("설치 파일 검증에 실패했습니다: " + name);
                            }
                        }
                        catch
                        {
                            foreach (var file in created.AsEnumerable().Reverse())
                                if (File.Exists(file)) File.Delete(file);
                            throw;
                        }
                    }
                }
                finally
                {
                    if (Directory.Exists(stage))
                        try { Directory.Delete(stage, true); }
                        catch (IOException) { /* Keep only this unique staging folder for diagnosis. */ }
                        catch (UnauthorizedAccessException) { /* No user-owned target is deleted. */ }
                }
            }
        }
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--validate-target")
            {
                try { Console.WriteLine("INSTALL TARGET PASS: " + ValidateTarget(args[1])); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            }
            string workspace=Path.Combine(Path.GetTempPath(),"hangar-installer-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workspace);
            try {
                var release=Unpack(workspace);
                if(args.Length==1&&args[0]=="--verify-package"){
                    Console.WriteLine("INSTALLER PACKAGE PASS: "+release.version+" / "+AppUpdate.Files.Length+" files");
                    return 0;
                }
                if(args.Length==2&&args[0]=="--elevated-install"){
                    string privilegedTarget=ValidateTarget(args[1]);
                    Install(Path.Combine(workspace,"app"),privilegedTarget,Path.Combine(workspace,"previous-version"));
                    Console.WriteLine("INSTALLER PRIVILEGED STEP PASS: "+privilegedTarget);
                    return 0;
                }
                if(args.Length==2&&args[0]=="--install-fixture"){
                    string fixture=ValidateTarget(args[1]);
                    if(File.Exists(fixture) || IsOurInstallation(fixture) ||
                        (Directory.Exists(fixture) && HasEntries(fixture)))
                        throw new IOException("Fixture must be a new or empty app directory.");
                    Install(Path.Combine(workspace,"app"),fixture,Path.Combine(workspace,"backup"));
                    Console.WriteLine("INSTALLER FIXTURE PASS: "+fixture);return 0;
                }
                if(args.Length!=0)throw new ArgumentException("지원하지 않는 설치 옵션입니다.");
                Application.EnableVisualStyles();
                string target=ChooseTarget(release.version,Path.Combine(workspace,"app","assets","hangar.jpg"));
                if(target==null)return 0;
                if(CanWriteTarget(target))
                    Install(Path.Combine(workspace,"app"),target,Path.Combine(workspace,"previous-version"));
                else if(!InstallWithAdministratorApproval(Assembly.GetExecutingAssembly().Location,target))
                    return 0;
                Type shellType=Type.GetTypeFromProgID("WScript.Shell");
                object shell=Activator.CreateInstance(shellType);
                string menu=StartMenuShortcut;
                object shortcut=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{menu});
                Type shortcutType=shortcut.GetType();
                shortcutType.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{Path.Combine(target,"CampaignHangarSetup.exe")});
                shortcutType.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{target});
                shortcutType.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
                Process.Start(Path.Combine(target,"CampaignHangarSetup.exe"));
                return 0;
            }catch(Exception error){
                Console.Error.WriteLine(error);
                if(args.Length==0)MessageBox.Show("설치하지 못했습니다.\n"+error.Message+
                    "\n\n1. 실행 중인 격납고를 종료하고 설치 파일을 우클릭해 '관리자 권한으로 실행'해 보세요."+
                    "\n2. Windows 보안의 보호 기록을 확인하고 검증된 설치 파일만 예외로 허용해 보세요."+
                    "\n3. 그래도 안 된다면 백신 실시간 감시를 잠시 끄고 설치 여부만 시험한 후 즉시 다시 켜세요."+
                    "\n기존 설치 폴더나 사용자 데이터를 임의로 삭제하지 마세요."+
                    "\n\n복구 자료: "+workspace,"커캠 격납고 설치 · 관리자 권한 확인",MessageBoxButtons.OK,MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
