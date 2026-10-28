using System;
using System.IO;
using System.Threading;
using Microsoft.Xna.Framework.Graphics;
using Sexy;

namespace PGvZPlantKit
{
    /// <summary>
    /// 免切窗口截图：任意线程登记请求，下一帧画完（LawnApp.DrawGame 返回后、Present 之前）
    /// 在游戏线程上从后备缓冲取像素写 PNG，不需要把游戏窗口切到前台。
    /// 触发方式：游戏里按 F12，或脚本里调 <see cref="PlantKit.RequestScreenshot"/>。
    /// </summary>
    public static class Screenshot
    {
        private static string _pending;
        private static int _sequence;

        public static string LastPath { get; private set; }

        public static string LastError { get; private set; }

        public static bool Pending => Volatile.Read(ref _pending) != null;

        public static string Directory => Path.Combine(ModPaths.StorageDirectory, "screenshots");

        /// <summary>
        /// 登记一次截图请求，返回即将写入的路径；thePath 留空则按时间自动命名。
        /// 只是登记，真正的抓图发生在下一帧末尾的游戏线程上。
        /// </summary>
        public static string Request(string thePath = null)
        {
            string target = string.IsNullOrEmpty(thePath) ? AutoPath() : thePath;
            Volatile.Write(ref _pending, target);
            return target;
        }

        private static string AutoPath()
        {
            int index = Interlocked.Increment(ref _sequence);
            return Path.Combine(Directory,
                "shot-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + index + ".png");
        }

        /// <summary>由 DrawGame 钩子在每帧末尾调用；没有请求时几乎零开销。</summary>
        internal static void OnFrameEnd()
        {
            string path = Interlocked.Exchange(ref _pending, null);
            if (path == null)
            {
                return;
            }
            try
            {
                Capture(path);
                LastPath = path;
                LastError = null;
            }
            catch (Exception ex)
            {
                LastPath = null;
                LastError = ex.GetType().Name + ": " + ex.Message;
                ModLog.Error("screenshot failed: " + path, ex);
            }
        }

        private static void Capture(string path)
        {
            GraphicsDevice device = GlobalStaticVars.g.GraphicsDevice;
            int width = device.PresentationParameters.BackBufferWidth;
            int height = device.PresentationParameters.BackBufferHeight;
            Microsoft.Xna.Framework.Color[] pixels =
                new Microsoft.Xna.Framework.Color[width * height];
            device.GetBackBufferData(pixels);
            using Texture2D texture = new Texture2D(device, width, height);
            texture.SetData(pixels);
            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                System.IO.Directory.CreateDirectory(folder);
            }
            using FileStream stream = File.Create(path);
            texture.SaveAsPng(stream, width, height);
        }
    }
}
