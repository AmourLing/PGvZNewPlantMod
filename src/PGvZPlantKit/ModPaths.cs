using System;
using System.Collections.Generic;
using System.IO;

namespace PGvZPlantKit
{
    /// <summary>
    /// 跨平台路径解析。游戏在 Windows/Android 都会把工作目录切到可写的存储根，
    /// mod 本体位于 &lt;工作目录&gt;/mods/PGvZNewPlantMod。
    /// </summary>
    internal static class ModPaths
    {
        private static string _modDirectory;

        public static string ModDirectory => _modDirectory;

        public static string StorageDirectory
        {
            get
            {
                if (!string.IsNullOrEmpty(_modDirectory) && Directory.Exists(_modDirectory))
                {
                    return _modDirectory;
                }
                string assemblyDir = Path.GetDirectoryName(typeof(ModPaths).Assembly.Location);
                return string.IsNullOrEmpty(assemblyDir)
                    ? Environment.CurrentDirectory
                    : assemblyDir;
            }
        }

        public static void SetModDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }
            try
            {
                _modDirectory = Path.GetFullPath(path);
            }
            catch
            {
                _modDirectory = path;
            }
        }

        public static string FindContentFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                return null;
            }
            string relative = relativePath.Replace('\\', '/').TrimStart('/');
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string root in Roots())
            {
                foreach (string candidate in Candidates(root, relative))
                {
                    if (seen.Add(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }

        public static bool HasContent(string relativePath)
        {
            return EmbeddedContentStore.Has(relativePath) ||
                   FindContentFile(relativePath) != null;
        }

        public static Stream OpenContent(string relativePath)
        {
            string path = FindContentFile(relativePath);
            if (path != null)
            {
                return File.OpenRead(path);
            }
            return EmbeddedContentStore.TryOpen(relativePath, out Stream stream)
                ? stream
                : null;
        }

        private static IEnumerable<string> Roots()
        {
            if (!string.IsNullOrEmpty(_modDirectory))
            {
                yield return _modDirectory;
            }
            yield return Environment.CurrentDirectory;
            yield return AppContext.BaseDirectory;
            string assemblyDir = Path.GetDirectoryName(typeof(ModPaths).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                yield return assemblyDir;
            }
        }

        private static IEnumerable<string> Candidates(string root, string relative)
        {
            yield return Path.Combine(root, relative);
            if (relative.StartsWith("Content/", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(root, relative.Substring("Content/".Length));
            }
            else if (relative.StartsWith("spanim/", StringComparison.OrdinalIgnoreCase) ||
                     relative.StartsWith("reanim/", StringComparison.OrdinalIgnoreCase))
            {
                yield return Path.Combine(root, "Content", relative);
            }
        }
    }
}
