using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PGvZPlantKit
{
    /// <summary>
    /// 把插件 DLL 内嵌的 Spine 文件映射成 TitleContainer 的虚拟路径，
    /// 例如 Content/spanim/czlb.atlas。
    /// </summary>
    internal static class EmbeddedContentStore
    {
        private static readonly Dictionary<string, Func<Stream>> Providers =
            new Dictionary<string, Func<Stream>>(StringComparer.OrdinalIgnoreCase);

        public static void RegisterAssembly(Assembly assembly, string prefix,
            string virtualDirectory)
        {
            if (assembly == null || string.IsNullOrEmpty(prefix))
            {
                return;
            }
            string dir = Normalize(virtualDirectory ?? string.Empty).Trim('/');
            foreach (string resource in assembly.GetManifestResourceNames())
            {
                if (!resource.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }
                string name = resource.Substring(prefix.Length);
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                string key = dir.Length == 0 ? name : dir + "/" + name;
                Assembly capturedAssembly = assembly;
                string capturedResource = resource;
                Providers[Normalize(key)] = () =>
                    capturedAssembly.GetManifestResourceStream(capturedResource);
            }
        }

        public static bool Has(string path)
        {
            foreach (string key in Keys(path))
            {
                if (Providers.ContainsKey(key))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool TryOpen(string path, out Stream stream)
        {
            foreach (string key in Keys(path))
            {
                if (!Providers.TryGetValue(key, out Func<Stream> provider))
                {
                    continue;
                }
                stream = provider();
                if (stream != null)
                {
                    return true;
                }
            }
            stream = null;
            return false;
        }

        private static IEnumerable<string> Keys(string path)
        {
            string normalized = Normalize(path).Trim('/');
            if (normalized.Length == 0)
            {
                yield break;
            }
            yield return normalized;
            if (normalized.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                yield return normalized.Substring("Content/".Length);
            }
            else
            {
                yield return "Content/" + normalized;
            }
        }

        private static string Normalize(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/');
        }
    }
}
