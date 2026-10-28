using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using Sexy;

namespace PGvZPlantKit
{
    /// <summary>
    /// 卡图/弹体图加载。C# mod 可以注册内嵌资源，IronPython 可以直接注册 PNG 文件或字节。
    /// 未注册的图片返回 null；调用方应回退到原生卡图或占位。
    /// </summary>
    public static class AssetStore
    {
        private static readonly Dictionary<string, Func<byte[]>> Providers =
            new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Image> Cache =
            new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterImage(string name, byte[] pngBytes)
        {
            if (string.IsNullOrEmpty(name) || pngBytes == null)
            {
                return;
            }
            byte[] copy = (byte[])pngBytes.Clone();
            Providers[name] = () => copy;
            Cache.Remove(name);
        }

        public static void RegisterImageFile(string name, string pngPath)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(pngPath))
            {
                return;
            }
            Providers[name] = () => File.ReadAllBytes(pngPath);
            Cache.Remove(name);
        }

        /// <summary>把程序集内嵌的 PNG 按文件名注册，例如 prefix="MyMod.Assets."。</summary>
        public static void RegisterAssembly(Assembly assembly, string prefix)
        {
            if (assembly == null)
            {
                return;
            }
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(prefix, StringComparison.Ordinal) ||
                    !resource.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string key = resource.Substring(prefix.Length,
                    resource.Length - prefix.Length - 4);
                Assembly captured = assembly;
                string capturedResource = resource;
                Providers[key] = () =>
                {
                    using Stream stream = captured.GetManifestResourceStream(capturedResource);
                    if (stream == null)
                    {
                        return null;
                    }
                    using MemoryStream memory = new MemoryStream();
                    stream.CopyTo(memory);
                    return memory.ToArray();
                };
                Cache.Remove(key);
            }
        }

        public static bool Has(string name)
        {
            return !string.IsNullOrEmpty(name) && Providers.ContainsKey(name);
        }

        public static int Count => Providers.Count;

        /// <summary>已登记的素材名（不触发解码），诊断报告与"卡图名拼错"自检用得上。</summary>
        public static IReadOnlyList<string> Names => Providers.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

        internal static bool TryGetName(Image image, out string name)
        {
            foreach (KeyValuePair<string, Image> entry in Cache)
            {
                if (ReferenceEquals(image, entry.Value))
                {
                    name = entry.Key;
                    return true;
                }
            }
            name = null;
            return false;
        }

        public static bool TryGet(string name, out Image image)
        {
            image = null;
            if (string.IsNullOrEmpty(name) || !Providers.TryGetValue(name, out Func<byte[]> provider))
            {
                return false;
            }
            if (Cache.TryGetValue(name, out image) && image != null)
            {
                return true;
            }
            byte[] bytes = provider();
            if (bytes == null || bytes.Length == 0)
            {
                return false;
            }
            using MemoryStream memory = new MemoryStream(bytes);
            Texture2D texture = Texture2D.FromStream(GlobalStaticVars.g.GraphicsDevice, memory);
            image = new Image(texture);
            Cache[name] = image;
            return true;
        }

        public static Image Get(string name)
        {
            return TryGet(name, out Image image) ? image : null;
        }
    }
}
