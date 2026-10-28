using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PGvZPlantKit;

namespace PGvZNewPlantMod
{
    /// <summary>
    /// 从 PGvZNewPlantMod/plants/*.dll 加载 C# 植物插件。
    /// 每个插件实现 IPlantPack 并自行注册定义、资源和专属钩子。
    /// </summary>
    public static class PlantPluginLoader
    {
        private sealed class DiscoveredPack
        {
            public IPlantPack Pack;
            public string FallbackId;
            public string Label => Pack.GetType().FullName;
        }

        private static bool _loaded;
        private static string _discoveredDir;
        private static List<DiscoveredPack> _discovered;

        public static int PackCount { get; private set; }
        public static int HookCount { get; private set; }

        /// <summary>plants 目录里发现到的 pack 类数量，与清单启用状态无关。</summary>
        public static int DiscoveredPackCount => _discovered?.Count ?? 0;

        public static readonly List<string> LoadedPackIds = new List<string>();
        /// <summary>包 ID → 插件作者写的加载序号；清单顺序可被用户改，这个不会。</summary>
        public static readonly Dictionary<string, int> PackOrder = new Dictionary<string, int>();
        public static readonly List<string> HookFailures = new List<string>();
        public static readonly List<string> LoadFailures = new List<string>();

        /// <summary>只发现包 ID，不注册植物；供启动时自动补清单。</summary>
        public static IReadOnlyList<string> DiscoverPackIds(string modFolder)
        {
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DiscoveredPack item in Discover(modFolder))
            {
                string id = ResolvePackId(item);
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }
            return ids;
        }

        public static void Load(string modFolder)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;

            List<DiscoveredPack> packs = Discover(modFolder);
            if (packs.Count == 0)
            {
                PlantKit.Log("no C# plant plugins in " + Path.Combine(modFolder, "plants"));
                return;
            }

            int skipped = 0;
            foreach (DiscoveredPack item in packs
                .OrderBy(pack => pack.Pack.Order)
                .ThenBy(pack => pack.Label, StringComparer.Ordinal))
            {
                string packId = ResolvePackId(item);
                PackOrder[packId] = item.Pack.Order;
                if (!LoadOrder.IsEnabled(packId))
                {
                    skipped++;
                    PlantKit.Log("plant pack disabled, skip: " + item.Label +
                        " [" + packId + "]");
                    continue;
                }
                PlantKit.BeginPack(packId);
                try
                {
                    item.Pack.Register(packId);
                    PackCount++;
                    LoadedPackIds.Add(packId);
                    HookCount += item.Pack.HookCount;
                    foreach (string failure in item.Pack.HookFailures)
                    {
                        HookFailures.Add(item.Label + ": " + failure);
                    }
                    PlantKit.Log("plant pack registered: " + item.Label +
                        " [" + packId + "] (+" + item.Pack.HookCount + " hooks)");
                }
                catch (Exception ex)
                {
                    LoadFailures.Add(item.Label + ": " + ex.Message);
                    PlantKit.LogError("plant pack failed: " + item.Label, ex);
                }
                finally
                {
                    PlantKit.EndPack();
                }
            }

            PlantKit.Log("plant plugins loaded: " + PackCount + " pack(s), " +
                HookCount + " extras hooks" + (skipped > 0 ? ", skipped " + skipped : ""));
        }

        private static List<DiscoveredPack> Discover(string modFolder)
        {
            string dir = Path.Combine(modFolder, "plants");
            if (_discovered != null &&
                string.Equals(_discoveredDir, dir, StringComparison.OrdinalIgnoreCase))
            {
                return _discovered;
            }
            _discoveredDir = dir;
            _discovered = new List<DiscoveredPack>();

            if (!Directory.Exists(dir))
            {
                PlantKit.Log("plant plugin folder missing: " + dir);
                return _discovered;
            }

            foreach (string file in Directory.GetFiles(dir, "*.dll")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (string.Equals(name, "PGvZPlantKit", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "PGvZNewPlantMod", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    Assembly assembly = Assembly.LoadFrom(file);
                    int found = 0;
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface ||
                            !typeof(IPlantPack).IsAssignableFrom(type))
                        {
                            continue;
                        }
                        try
                        {
                            IPlantPack pack = Activator.CreateInstance(type) as IPlantPack;
                            if (pack == null)
                            {
                                continue;
                            }
                            _discovered.Add(new DiscoveredPack
                            {
                                Pack = pack,
                                FallbackId = name,
                            });
                            found++;
                        }
                        catch (Exception ex)
                        {
                            LoadFailures.Add(type.FullName + ": " + ex.Message);
                            PlantKit.LogError("plant pack type failed: " + type.FullName, ex);
                        }
                    }
                    PlantKit.Log("plant plugin loaded: " + assembly.GetName().Name +
                        " (" + found + " pack(s))");
                }
                catch (ReflectionTypeLoadException ex)
                {
                    string details = string.Join("; ", ex.LoaderExceptions
                        .Where(e => e != null).Select(e => e.Message));
                    LoadFailures.Add(name + ": " + details);
                    PlantKit.LogError("plant plugin types failed: " + name + " :: " + details);
                }
                catch (Exception ex)
                {
                    LoadFailures.Add(name + ": " + ex.Message);
                    PlantKit.LogError("plant plugin failed: " + name, ex);
                }
            }
            return _discovered;
        }

        private static string ResolvePackId(DiscoveredPack item)
        {
            string id = null;
            try
            {
                id = item.Pack.PackId;
            }
            catch (Exception ex)
            {
                LoadFailures.Add(item.Label + ".PackId: " + ex.Message);
                PlantKit.LogError("plant pack id failed: " + item.Label, ex);
            }
            if (string.IsNullOrWhiteSpace(id))
            {
                id = item.FallbackId;
            }
            return id.Trim();
        }
    }
}
