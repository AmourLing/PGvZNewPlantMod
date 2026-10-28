using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PGvZNewPlantMod
{
    /// <summary>
    /// 顶层只保留 PGvZNewPlantMod.dll：框架库和数据目录从 mods/PGvZNewPlantMod 解析。
    /// Windows 与 Android 的工作目录都由游戏切到可写存储根，因此同时搜索程序集目录和
    /// 当前目录下的 mods/。
    /// </summary>
    internal static class Bootstrap
    {
        private const string KitName = "PGvZPlantKit";

        [ModuleInitializer]
        internal static void Initialize()
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolvePlantKit;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveGameAssembly;
        }

        private static Assembly ResolvePlantKit(object sender, ResolveEventArgs args)
        {
            string requested = new AssemblyName(args.Name).Name;
            if (!string.Equals(requested, KitName, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            foreach (string path in KitCandidates())
            {
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(path);
                }
            }
            return null;
        }

        private static IEnumerable<string> KitCandidates()
        {
            string baseDir = Path.GetDirectoryName(typeof(Bootstrap).Assembly.Location);
            if (!string.IsNullOrEmpty(baseDir))
            {
                yield return Path.Combine(baseDir, "PGvZNewPlantMod", KitName + ".dll");
                yield return Path.Combine(baseDir, KitName + ".dll");
                yield return Path.Combine(baseDir, "mods", "PGvZNewPlantMod", KitName + ".dll");
            }
            yield return Path.Combine(Environment.CurrentDirectory, "mods", "PGvZNewPlantMod",
                KitName + ".dll");
            yield return Path.Combine(Environment.CurrentDirectory, "mods", KitName + ".dll");
            yield return Path.Combine(Environment.CurrentDirectory, "PGvZNewPlantMod",
                KitName + ".dll");
            yield return Path.Combine(AppContext.BaseDirectory, "mods", "PGvZNewPlantMod",
                KitName + ".dll");
        }

        /// <summary>同一套 DLL 在 Windows/Android 上引用不同版本的游戏程序集时，优先复用已加载版本。</summary>
        private static Assembly ResolveGameAssembly(object sender, ResolveEventArgs args)
        {
            string requested = new AssemblyName(args.Name).Name;
            switch (requested)
            {
                case "Lawn":
                case "MonoGame.Framework":
                case "IronPython":
                case "Microsoft.Scripting":
                case "MonoMod.RuntimeDetour":
                case "MonoMod.Utils":
                    break;
                default:
                    return null;
            }
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(assembly.GetName().Name, requested,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return assembly;
                }
            }
            return null;
        }
    }
}
