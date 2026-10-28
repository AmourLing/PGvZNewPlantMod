using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using PGvZPlantKit;

namespace PGvZNewPlantMod
{
    /// <summary>
    /// 单 DLL + 同名文件夹结构的宿主：
    ///   mods/PGvZNewPlantMod.dll
    ///   mods/PGvZNewPlantMod/
    ///     PGvZPlantKit.dll
    ///     plant_packs.json
    ///     plants/*.py
    ///     assets/*.png
    ///     spanim/*
    /// </summary>
    public static class ModHost
    {
        public const string FolderName = "PGvZNewPlantMod";
        private static bool _loaded;

        public static void Load()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            string modFolder = ResolveModFolder();
            PlantKit.SetModDirectory(modFolder);
            try
            {
                string platform = OperatingSystem.IsAndroid() ? "Android" :
                    OperatingSystem.IsWindows() ? "Windows" : "Unknown";
                PlantKit.Log("runtime: " + platform + ", Lawn " +
                    typeof(Lawn.LawnApp).Assembly.GetName().Version);
            }
            catch
            {
            }
            Directory.CreateDirectory(modFolder);
            Directory.CreateDirectory(Path.Combine(modFolder, "plants"));
            Directory.CreateDirectory(Path.Combine(modFolder, "assets"));

            string manifest = Path.Combine(modFolder, "plant_packs.json");
            LoadOrder.SetManifestPath(manifest);

            // 新植物先自动补进清单并启用，再安装框架/注册植物
            ManifestAutoDiscovery.Ensure(modFolder);

            // 框架 + 共享资源 + plants/*.dll 中的 C# 植物插件
            NinePlantModule.RegisterBuiltins(modFolder);

            // 文件夹中的 IronPython 植物
            FolderPlantLoader.Load(modFolder);

            // 宿主自己的加载结果并进诊断快照（Kit 看不到插件程序集的发现与失败明细）。
            PlantKit.AddReportSection("C# 植物插件", () =>
                "packs     : " + PlantPluginLoader.PackCount + " 启用 / " +
                PlantPluginLoader.DiscoveredPackCount + " 发现\r\n" +
                "hooks     : " + PlantPluginLoader.HookCount + "\r\n" +
                "loaded    : " + string.Join(", ", PlantPluginLoader.LoadedPackIds) + "\r\n" +
                "hook fails: " + (PlantPluginLoader.HookFailures.Count == 0 ? "0" :
                    string.Join("\r\n            ", PlantPluginLoader.HookFailures)) + "\r\n" +
                "load fails: " + (PlantPluginLoader.LoadFailures.Count == 0 ? "0" :
                    string.Join("\r\n            ", PlantPluginLoader.LoadFailures)));

            // 定义自检不在这里跑：此刻种子号与动画槽还没分配（EnsureDefinitions 是懒执行的），
            // 提前检查会让每株植物都吃一条"还没有种子号"的假告警。自检由排版完成时自动触发。
            PlantKit.Log("host loaded: " + modFolder);
        }

        private static string ResolveModFolder()
        {
            var candidates = new List<string>();
            string assemblyDir = Path.GetDirectoryName(typeof(ModHost).Assembly.Location);
            if (!string.IsNullOrEmpty(assemblyDir))
            {
                candidates.Add(Path.Combine(assemblyDir, FolderName));
                candidates.Add(Path.Combine(assemblyDir, "mods", FolderName));
            }
            candidates.Add(Path.Combine(Environment.CurrentDirectory, "mods", FolderName));
            candidates.Add(Path.Combine(AppContext.BaseDirectory, "mods", FolderName));

            foreach (string candidate in candidates)
            {
                if (Directory.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
            string fallback = Path.Combine(Environment.CurrentDirectory, "mods", FolderName);
            return Path.GetFullPath(fallback);
        }
    }
}
