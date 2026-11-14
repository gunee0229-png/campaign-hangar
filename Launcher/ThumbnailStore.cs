using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace CampaignHangar
{
    public sealed class ThumbnailStore : IDisposable
    {
        readonly string root;
        readonly SemaphoreSlim slots = new SemaphoreSlim(3);
        readonly Dictionary<string, Task<Image>> pending = new Dictionary<string, Task<Image>>();
        readonly List<Image> images = new List<Image>();
        bool disposed;
        public ThumbnailStore(string directory) { root = directory; Directory.CreateDirectory(root); }
        public Task<Image> Get(Campaign item)
        {
            string url = Catalogue.ThumbnailUrl(item);
            if (url == null) throw new InvalidDataException("사이트 썸네일 주소가 없습니다.");
            lock (pending)
            {
                Task<Image> task;
                if (!pending.TryGetValue(url, out task))
                {
                    task = Load(url); pending[url] = task;
                }
                return task;
            }
        }
        async Task<Image> Load(string url)
        {
            await slots.WaitAsync();
            try
            {
                return await Task.Run(() =>
                {
                    string key;
                    using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(url))).Replace("-", "");
                    string file = Path.Combine(root, key + "-original.png");
                    if (!File.Exists(file))
                    {
                        var request = (HttpWebRequest)WebRequest.Create(url);
                        request.Timeout = 15000; request.ReadWriteTimeout = 15000; request.AllowAutoRedirect = false;
                        using (var response = request.GetResponse())
                        using (var stream = response.GetResponseStream())
                        using (var raw = new MemoryStream())
                        {
                            if (response.ContentLength > 4 * 1024 * 1024) throw new InvalidDataException("썸네일이 4MiB를 초과합니다.");
                            byte[] buffer = new byte[16384]; int count;
                            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                if (raw.Length + count > 4 * 1024 * 1024) throw new InvalidDataException("썸네일이 너무 큽니다.");
                                raw.Write(buffer, 0, count);
                            }
                            raw.Position = 0;
                            SaveOriginal(raw, file);
                        }
                    }
                    using (var bitmap = Image.FromFile(file))
                    {
                        var result = new Bitmap(bitmap);
                        lock (images)
                        {
                            if (disposed) { result.Dispose(); throw new ObjectDisposedException("ThumbnailStore"); }
                            images.Add(result);
                        }
                        return (Image)result;
                    }
                });
            }
            finally { slots.Release(); }
        }
        internal static void SaveOriginal(MemoryStream source, string file)
        {
            var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            if (frame.PixelWidth > 8192 || frame.PixelHeight > 8192) throw new InvalidDataException("썸네일 해상도가 너무 큽니다.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(frame);
            using (var png = new MemoryStream())
            {
                encoder.Save(png); png.Position = 0;
                using (var original = Image.FromStream(png)) original.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        public void Dispose()
        {
            lock (images) { disposed = true; foreach (var image in images) image.Dispose(); images.Clear(); }
        }
    }
}
