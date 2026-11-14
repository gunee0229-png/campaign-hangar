using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace CampaignHangar
{
    public sealed class ZipDropTarget
    {
        readonly Func<bool> enabled;
        readonly Func<string[], Task> import;
        readonly HashSet<Control> surfaces = new HashSet<Control>();
        bool processing;
        Task lastDrop = Task.FromResult(0);
        public ZipDropTarget(Control root, Func<bool> enabled, Func<string[], Task> import)
        {
            this.enabled = enabled; this.import = import; AddSurface(root);
        }
        public void AddSurface(Control control, bool children = true)
        {
            if (!surfaces.Add(control)) return;
            control.AllowDrop = true;
            control.DragEnter += Enter; control.DragOver += Enter; control.DragDrop += Drop;
            control.Disposed += delegate { surfaces.Remove(control); };
            if (!children) return;
            foreach (Control child in control.Controls) AddSurface(child);
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { AddSurface(e.Control); };
        }
        public static string[] ZipFiles(IDataObject data)
        {
            if (data == null || !data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files == null || files.Length == 0) return null;
            foreach (string path in files)
                if (String.IsNullOrWhiteSpace(path) || !String.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)) return null;
            return files;
        }
        // A Chromium child HWND consumes Explorer drops before WinForms parent
        // DragDrop handlers. WebView2 exposes the original OS File objects as
        // AdditionalObjects; never trust a file path supplied in a JS string.
        public static string[] WebZipFiles(CoreWebView2WebMessageReceivedEventArgs message)
        {
            if (message == null || message.AdditionalObjects == null ||
                message.AdditionalObjects.Count == 0 || message.AdditionalObjects.Count > 16)
                throw new InvalidDataException("ZIP 파일을 1~16개 드롭해 주세요.");
            var files = new List<string>();
            foreach (var value in message.AdditionalObjects)
            {
                var file = value as CoreWebView2File;
                if (file == null || String.IsNullOrWhiteSpace(file.Path) ||
                    !String.Equals(Path.GetExtension(file.Path), ".zip", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(file.Path))
                    throw new InvalidDataException("드롭한 항목 중 ZIP 파일이 아닌 항목이나 읽을 수 없는 파일이 있습니다.");
                files.Add(file.Path);
            }
            return files.ToArray();
        }
        void Enter(object sender, DragEventArgs e)
        {
            e.Effect = !processing && enabled() && ZipFiles(e.Data) != null && (e.AllowedEffect & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy : DragDropEffects.None;
        }
        void Drop(object sender, DragEventArgs e)
        {
            Enter(sender, e);
            if (e.Effect != DragDropEffects.Copy) return;
            lastDrop = ProcessAsync(ZipFiles(e.Data));
        }
        async Task ProcessAsync(string[] files)
        {
            processing = true;
            try { await import(files); }
            finally { processing = false; }
        }
        internal Task VerifyEventsAndDrop(string file, Control destination = null)
        {
            var data = new DataObject(); data.SetData(DataFormats.FileDrop, new[] { file });
            var method = typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic);
            Control target = null;
            foreach (Control surface in surfaces)
            {
                var enter = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
                method.Invoke(surface, new object[] { enter });
                if (!surface.AllowDrop || enter.Effect != DragDropEffects.Copy) throw new InvalidOperationException("ZIP drop is unavailable on " + surface.GetType().Name);
                if (surface is CampaignArtwork) target = surface;
            }
            if (destination != null)
            {
                if (!surfaces.Contains(destination)) throw new InvalidOperationException("ZIP drop surface missing for " + destination.GetType().Name);
                target = destination;
            }
            if (target == null) throw new InvalidOperationException("Hero artwork drop surface missing.");
            var drop = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None);
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, new object[] { drop });
            return lastDrop;
        }
    }
}
