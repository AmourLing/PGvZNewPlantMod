using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PGvZPlantKit
{
    /// <summary>
    /// 植物包加载顺序。参考 PvZ Ultimate 的 resourcepacks.json / pack.json：
    /// - 有序的 packs 列表，前面优先；
    /// - 可选 before / after / dependencies 调整顺序；
    /// - enabled=false 的包不注册植物；
    /// - 未列出的包按 ID 字母序追加，默认启用。
    ///
    /// 配置文件为 kit DLL 同目录下的 plant_packs.json，修改后需重启游戏。
    /// </summary>
    public static class LoadOrder
    {
        public const string ManifestName = "plant_packs.json";
        private const string DefaultPackId = "default";

        private sealed class PackEntry
        {
            public string Id;
            public bool Enabled = true;
            public readonly List<string> Before = new List<string>();
            public readonly List<string> After = new List<string>();
            public readonly List<string> Dependencies = new List<string>();
            public bool ReferencedOnly;
        }

        private static bool _loaded;
        private static string _manifestPath;
        private static readonly Dictionary<string, PackEntry> Entries =
            new Dictionary<string, PackEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> Ordered = new List<string>();
        private static readonly Dictionary<string, int> OrderIndex =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>宿主 DLL 可在 PlantKit.Install 之前把清单指到自己的文件夹。</summary>
        public static void SetManifestPath(string path)
        {
            if (string.Equals(_manifestPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            _manifestPath = path;
            _loaded = false;
            Entries.Clear();
            Ordered.Clear();
            OrderIndex.Clear();
        }

        /// <summary>按清单顺序排列的包 ID；未列出的包不在这里。</summary>
        public static IReadOnlyList<string> OrderedPackIds
        {
            get
            {
                EnsureLoaded();
                return Ordered;
            }
        }

        public static string ManifestPathUsed => ResolveManifestPath();

        /// <summary>清单里出现过的全部包（含禁用的），按清单顺序；诊断报告用。</summary>
        public static IReadOnlyList<string> KnownPackIds
        {
            get
            {
                EnsureLoaded();
                List<string> ids = new List<string>(Entries.Keys);
                ids.Sort(ComparePacks);
                return ids;
            }
        }

        public static void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;
            try
            {
                LoadManifest();
            }
            catch (Exception ex)
            {
                ModLog.Error("plant_packs.json load failed; all packs enabled, alphabetical order", ex);
                Entries.Clear();
                Ordered.Clear();
                OrderIndex.Clear();
            }
        }

        /// <summary>
        /// 把新发现、但清单里还没有的包追加到末尾并启用。
        /// 保留已有条目、顺序和 before/after/dependencies；返回实际写入的包 ID。
        /// </summary>
        public static IReadOnlyList<string> EnsureManifestPacks(IEnumerable<string> packIds)
        {
            var added = new List<string>();
            if (packIds == null)
            {
                return added;
            }

            var wanted = new List<string>();
            var wantedSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in packIds)
            {
                string id = (raw ?? string.Empty).Trim();
                if (id.Length > 0 && wantedSeen.Add(id))
                {
                    wanted.Add(id);
                }
            }
            if (wanted.Count == 0)
            {
                return added;
            }

            string path = ResolveManifestPath();
            JsonObject root;
            JsonArray packs;
            if (File.Exists(path))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8)) as JsonObject;
                    if (root == null)
                    {
                        ModLog.Error("cannot auto-add packs: " + ManifestName + " root is not an object");
                        return added;
                    }
                    if (!root.TryGetPropertyValue("version", out JsonNode versionNode) ||
                        versionNode is not JsonValue versionValue ||
                        !versionValue.TryGetValue<int>(out int version) || version != 1)
                    {
                        ModLog.Error("cannot auto-add packs: unsupported " + ManifestName +
                            " version (expected 1)");
                        return added;
                    }
                    if (!root.TryGetPropertyValue("packs", out JsonNode packsNode) ||
                        packsNode is not JsonArray packsArray)
                    {
                        ModLog.Error("cannot auto-add packs: " + ManifestName + " has no packs array");
                        return added;
                    }
                    packs = packsArray;
                }
                catch (Exception ex)
                {
                    ModLog.Error("cannot auto-add packs: " + ManifestName + " parse failed", ex);
                    return added;
                }
            }
            else
            {
                root = new JsonObject
                {
                    ["version"] = 1,
                    ["packs"] = new JsonArray(),
                };
                packs = (JsonArray)root["packs"];
            }

            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonNode node in packs)
            {
                if (node is not JsonObject entry ||
                    !entry.TryGetPropertyValue("id", out JsonNode idNode) ||
                    idNode is not JsonValue idValue ||
                    !idValue.TryGetValue<string>(out string id) ||
                    string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }
                existing.Add(id.Trim());
            }

            foreach (string id in wanted)
            {
                if (!existing.Add(id))
                {
                    continue;
                }
                packs.Add(new JsonObject
                {
                    ["id"] = id,
                    ["enabled"] = true,
                });
                added.Add(id);
            }
            if (added.Count == 0)
            {
                return added;
            }

            string temp = path + ".tmp";
            try
            {
                string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                File.Move(temp, path, true);
            }
            catch (Exception ex)
            {
                ModLog.Error("failed to write " + ManifestName, ex);
                try
                {
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }
                }
                catch
                {
                }
                return new List<string>();
            }

            _loaded = false;
            Entries.Clear();
            Ordered.Clear();
            OrderIndex.Clear();
            ModLog.Info("auto-added plant packs to " + ManifestName + ": " +
                string.Join(", ", added));
            return added;
        }

        public static string NormalizePackId(string packId)
        {
            return string.IsNullOrEmpty(packId) ? DefaultPackId : packId;
        }

        public static bool IsEnabled(string packId)
        {
            EnsureLoaded();
            PackEntry entry;
            return !Entries.TryGetValue(NormalizePackId(packId), out entry) || entry.Enabled;
        }

        /// <summary>小于 0 表示 a 先于 b。</summary>
        public static int ComparePacks(string packA, string packB)
        {
            EnsureLoaded();
            string a = NormalizePackId(packA);
            string b = NormalizePackId(packB);
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
            bool hasA = OrderIndex.TryGetValue(a, out int indexA);
            bool hasB = OrderIndex.TryGetValue(b, out int indexB);
            if (hasA && hasB)
            {
                return indexA.CompareTo(indexB);
            }
            if (hasA)
            {
                return -1;
            }
            if (hasB)
            {
                return 1;
            }
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveManifestPath()
        {
            if (!string.IsNullOrEmpty(_manifestPath))
            {
                return _manifestPath;
            }
            string dir = Path.GetDirectoryName(typeof(LoadOrder).Assembly.Location);
            return Path.Combine(string.IsNullOrEmpty(dir) ? "." : dir, ManifestName);
        }

        private static void LoadManifest()
        {
            string path = ResolveManifestPath();
            if (!File.Exists(path))
            {
                ModLog.Info("no " + ManifestName + " at " + path +
                    "; unlisted packs are enabled and ordered by id");
                return;
            }
            ModLog.Info("load order manifest: " + path);

            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllText(path, Encoding.UTF8));
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("version", out JsonElement versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                versionElement.GetInt32() != 1)
            {
                ModLog.Error("unsupported " + ManifestName + " version (expected 1)");
            }

            var manifestOrder = new List<string>();
            if (root.TryGetProperty("packs", out JsonElement packs) &&
                packs.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in packs.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }
                    string id = GetString(item, "id");
                    if (string.IsNullOrEmpty(id))
                    {
                        ModLog.Error("ignoring pack entry without id in " + ManifestName);
                        continue;
                    }
                    if (Entries.ContainsKey(id))
                    {
                        ModLog.Error("duplicate pack id in " + ManifestName + ": " + id);
                        continue;
                    }
                    var entry = new PackEntry { Id = id };
                    if (item.TryGetProperty("enabled", out JsonElement enabled) &&
                        enabled.ValueKind == JsonValueKind.False)
                    {
                        entry.Enabled = false;
                    }
                    ReadIds(item, "before", entry.Before);
                    ReadIds(item, "after", entry.After);
                    ReadIds(item, "dependencies", entry.Dependencies);
                    Entries[id] = entry;
                    manifestOrder.Add(id);
                }
            }

            // 被 before/after/dependencies 引用但未列出的包也纳入拓扑图。
            var referenced = new List<string>();
            foreach (PackEntry entry in Entries.Values)
            {
                AddReferenced(entry.Before, referenced);
                AddReferenced(entry.After, referenced);
                AddReferenced(entry.Dependencies, referenced);
            }
            referenced.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string id in referenced)
            {
                if (Entries.ContainsKey(id))
                {
                    continue;
                }
                Entries[id] = new PackEntry { Id = id, ReferencedOnly = true };
                manifestOrder.Add(id);
            }

            var baseOrder = new List<string>(manifestOrder);
            var nodes = new HashSet<string>(baseOrder, StringComparer.OrdinalIgnoreCase);
            var edges = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var indegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (string id in baseOrder)
            {
                edges[id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                indegree[id] = 0;
            }
            foreach (PackEntry entry in Entries.Values)
            {
                foreach (string target in entry.Before)
                {
                    AddEdge(entry.Id, target, nodes, edges, indegree);
                }
                foreach (string target in entry.After)
                {
                    AddEdge(target, entry.Id, nodes, edges, indegree);
                }
                foreach (string dependency in entry.Dependencies)
                {
                    AddEdge(dependency, entry.Id, nodes, edges, indegree);
                }
            }

            var remaining = new List<string>(baseOrder);
            while (remaining.Count > 0)
            {
                string next = null;
                foreach (string candidate in remaining)
                {
                    if (indegree[candidate] == 0)
                    {
                        next = candidate;
                        break;
                    }
                }
                if (next == null)
                {
                    ModLog.Error("cycle in " + ManifestName + " order; " +
                        "keeping registration order for: " + string.Join(", ", remaining));
                    Ordered.AddRange(remaining);
                    break;
                }
                remaining.Remove(next);
                Ordered.Add(next);
                foreach (string target in edges[next])
                {
                    indegree[target] = Math.Max(0, indegree[target] - 1);
                }
            }

            for (int i = 0; i < Ordered.Count; i++)
            {
                OrderIndex[Ordered[i]] = i;
            }
            var disabled = new List<string>();
            foreach (PackEntry entry in Entries.Values)
            {
                if (!entry.Enabled)
                {
                    disabled.Add(entry.Id);
                }
            }
            ModLog.Info("plant pack order: " + string.Join(" -> ", Ordered) +
                (disabled.Count > 0 ? "; disabled: " + string.Join(", ", disabled) : ""));
        }

        private static void AddReferenced(List<string> ids, List<string> referenced)
        {
            foreach (string id in ids)
            {
                if (!string.IsNullOrEmpty(id) &&
                    !referenced.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    referenced.Add(id);
                }
            }
        }

        private static void AddEdge(string from, string to, HashSet<string> nodes,
            Dictionary<string, HashSet<string>> edges,
            Dictionary<string, int> indegree)
        {
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) ||
                string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (!nodes.Contains(from) || !nodes.Contains(to))
            {
                ModLog.Error("load order references unlisted packs: " + from + " -> " + to);
                return;
            }
            if (edges[from].Add(to))
            {
                indegree[to]++;
            }
        }

        private static string GetString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static void ReadIds(JsonElement element, string name, List<string> result)
        {
            if (!element.TryGetProperty(name, out JsonElement value))
            {
                return;
            }
            if (value.ValueKind == JsonValueKind.String)
            {
                string id = value.GetString();
                if (!string.IsNullOrEmpty(id))
                {
                    result.Add(id);
                }
                return;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string id = item.GetString();
                        if (!string.IsNullOrEmpty(id))
                        {
                            result.Add(id);
                        }
                    }
                }
                return;
            }
            if (value.ValueKind == JsonValueKind.Object)
            {
                // dependencies 兼容 {"pack.id": {"min": "1.0.0"}} 形式，只取包 ID。
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!string.IsNullOrEmpty(property.Name))
                    {
                        result.Add(property.Name);
                    }
                }
                return;
            }
            ModLog.Error("ignoring invalid " + name + " in " + ManifestName);
        }
    }
}
