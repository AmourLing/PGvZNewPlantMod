using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IronPython.Hosting;
using Microsoft.Scripting.Hosting;
using PGvZPlantKit;

namespace PGvZNewPlantMod
{
    /// <summary>
    /// 从 PGvZNewPlantMod/plants/*.py 逐个加载植物。
    /// 文件名（不含 .py）就是 pack id；plant_packs.json 控制顺序/启用，
    /// 未列出的文件按文件名追加。每个文件在独立 scope 执行，
    /// PlantKit.RegisterPlant(def) 会自动归到当前文件的 pack。
    /// </summary>
    internal static class FolderPlantLoader
    {
        private static ScriptEngine _engine;
        private static readonly List<ScriptScope> Scopes = new List<ScriptScope>();

        public static void Load(string modFolder)
        {
            string plantsDir = Path.Combine(modFolder, "plants");
            string assetsDir = Path.Combine(modFolder, "assets");
            Directory.CreateDirectory(plantsDir);
            Directory.CreateDirectory(assetsDir);

            int assetCount = RegisterAssets(assetsDir);
            List<(string pack, string file)> plan = BuildPlan(plantsDir);
            if (plan.Count == 0)
            {
                PlantKit.Log("no folder plants in " + plantsDir + " (assets " + assetCount + ")");
                return;
            }

            try
            {
                EnsureEngine(modFolder, plantsDir);
            }
            catch (Exception ex)
            {
                PlantKit.LogError("folder plant engine failed; C# plugins remain active", ex);
                return;
            }
            foreach ((string pack, string file) in plan)
            {
                Execute(pack, file, plantsDir, assetsDir);
            }
            PlantKit.Log("folder plants loaded: " + plan.Count + " file(s), assets " + assetCount);
        }

        private static int RegisterAssets(string assetsDir)
        {
            int count = 0;
            foreach (string file in Directory.GetFiles(assetsDir, "*.png")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                PlantKit.RegisterImageFile(name, file);
                count++;
            }
            return count;
        }

        private static List<(string pack, string file)> BuildPlan(string plantsDir)
        {
            var plan = new List<(string pack, string file)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pack in LoadOrder.OrderedPackIds)
            {
                if (!visited.Add(pack))
                {
                    continue;
                }
                if (!LoadOrder.IsEnabled(pack))
                {
                    PlantKit.Log("pack disabled, skip folder plant " + pack);
                    continue;
                }
                string file = Path.Combine(plantsDir, pack + ".py");
                if (File.Exists(file))
                {
                    plan.Add((pack, file));
                }
            }

            foreach (string file in Directory.GetFiles(plantsDir, "*.py")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string pack = Path.GetFileNameWithoutExtension(file);
                if (!visited.Add(pack))
                {
                    continue;
                }
                if (!LoadOrder.IsEnabled(pack))
                {
                    PlantKit.Log("pack disabled, skip folder plant " + pack);
                    continue;
                }
                plan.Add((pack, file));
            }
            return plan;
        }

        private static void EnsureEngine(string modFolder, string plantsDir)
        {
            if (_engine != null)
            {
                return;
            }
            _engine = Python.CreateEngine();
            _engine.Runtime.LoadAssembly(typeof(PlantKit).Assembly);
            _engine.Runtime.LoadAssembly(typeof(Lawn.LawnApp).Assembly);
            _engine.Runtime.LoadAssembly(typeof(Microsoft.Xna.Framework.Game).Assembly);
            _engine.SetSearchPaths(new[]
            {
                modFolder,
                plantsDir,
                Environment.CurrentDirectory,
                AppDomain.CurrentDomain.BaseDirectory,
            });
        }

        private static void Execute(string pack, string file, string plantsDir, string assetsDir)
        {
            PlantKit.BeginPack(pack);
            try
            {
                ScriptScope scope = _engine.CreateScope();
                Scopes.Add(scope);
                scope.SetVariable("__file__", file);
                scope.SetVariable("__name__", pack);
                scope.SetVariable("PLANT_FOLDER", plantsDir);
                scope.SetVariable("ASSET_FOLDER", assetsDir);
                scope.SetVariable("MOD_FOLDER", Path.GetDirectoryName(plantsDir));
                _engine.ExecuteFile(file, scope);
                PlantKit.Log("folder plant loaded: " + pack + " <- " + file);
            }
            catch (Exception ex)
            {
                string details = ex.ToString();
                try
                {
                    details = _engine.GetService<ExceptionOperations>().FormatException(ex);
                }
                catch
                {
                }
                PlantKit.LogError("folder plant failed: " + pack + " <- " + file + "\n" + details);
            }
            finally
            {
                PlantKit.EndPack();
            }
        }
    }
}
