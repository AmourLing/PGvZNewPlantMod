using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PGvZPlantKit;

namespace PGvZNewPlantMod
{
    /// <summary>
    /// 启动时把 plants/ 下新出现、但清单未列出的植物包追加到 plant_packs.json，
    /// 并显式启用。已有条目、顺序和 before/after 保持不变。
    /// </summary>
    internal static class ManifestAutoDiscovery
    {
        public static void Ensure(string modFolder)
        {
            string plantsDir = Path.Combine(modFolder, "plants");
            var ids = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string id in PlantPluginLoader.DiscoverPackIds(modFolder))
            {
                if (seen.Add(id))
                {
                    ids.Add(id);
                }
            }

            if (Directory.Exists(plantsDir))
            {
                foreach (string file in Directory.GetFiles(plantsDir, "*.py")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    string id = Path.GetFileNameWithoutExtension(file);
                    if (seen.Add(id))
                    {
                        ids.Add(id);
                    }
                }
            }

            if (ids.Count == 0)
            {
                PlantKit.Log("manifest auto discovery: no plant packs found");
                return;
            }

            IReadOnlyList<string> added = LoadOrder.EnsureManifestPacks(ids);
            if (added.Count > 0)
            {
                PlantKit.Log("manifest auto discovery: added " + added.Count + " pack(s)");
            }
            else
            {
                PlantKit.Log("manifest auto discovery: " + ids.Count +
                    " pack(s), no new entries");
            }
        }
    }
}
