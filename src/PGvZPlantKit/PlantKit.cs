using System;
using System.Collections.Generic;
using System.Reflection;
using Lawn;

namespace PGvZPlantKit
{
    /// <summary>
    /// 库的公开入口。任何 C# mod 或 IronPython 脚本只需：
    ///   PlantKit.Install();
    ///   PlantKit.RegisterPlant(def);
    /// </summary>
    public static class PlantKit
    {
        public static bool Installed => CustomPlantHooks.Installed;

        /// <summary>当前 mod 数据目录；Windows/Android 都由宿主在安装前设置。</summary>
        public static string ModDirectory => ModPaths.ModDirectory;

        public static void SetModDirectory(string path)
        {
            ModPaths.SetModDirectory(path);
        }

        public static int HookCount => CustomPlantHooks.HookCount;

        public static IReadOnlyList<string> HookFailures => CustomPlantHooks.HookFailures;

        public static void Install()
        {
            LoadOrder.EnsureLoaded();
            AssetStore.RegisterAssembly(typeof(PlantKit).Assembly, "PGvZPlantKit.Assets.");
            CustomPlantHooks.Install();
        }

        [ThreadStatic] private static string _currentPackId;

        /// <summary>当前正在执行的植物文件所属包；由宿主在 ExecuteFile 前后设置。</summary>
        public static string CurrentPackId => _currentPackId;

        public static void BeginPack(string packId)
        {
            _currentPackId = LoadOrder.NormalizePackId(packId);
        }

        public static void EndPack()
        {
            _currentPackId = null;
        }

        public static void RegisterPlant(CustomPlantDef def)
        {
            CustomPlantRegistry.Register(def, null);
        }

        /// <summary>packId 对应 plant_packs.json 中的包 ID；加载顺序和启用状态由清单控制。</summary>
        public static void RegisterPlant(CustomPlantDef def, string packId)
        {
            CustomPlantRegistry.Register(def, packId);
        }

        public static int AllocateSeedId(int start = 94)
        {
            return CustomPlantRegistry.AllocateSeedId(start);
        }

        public static void RegisterImage(string name, byte[] pngBytes)
        {
            AssetStore.RegisterImage(name, pngBytes);
        }

        public static void RegisterImageFile(string name, string pngPath)
        {
            AssetStore.RegisterImageFile(name, pngPath);
        }

        public static void RegisterEmbeddedImages(Assembly assembly, string prefix)
        {
            AssetStore.RegisterAssembly(assembly, prefix);
        }

        /// <summary>
        /// 注册插件内嵌的 Spine 文件，按 virtualDirectory/文件名 映射到
        /// TitleContainer.OpenStream 的虚拟路径。
        /// </summary>
        public static void RegisterEmbeddedContent(Assembly assembly, string prefix,
            string virtualDirectory = "Content/spanim")
        {
            EmbeddedContentStore.RegisterAssembly(assembly, prefix, virtualDirectory);
        }

        public static bool HasContent(string relativePath)
        {
            return ModPaths.HasContent(relativePath);
        }

        public static void RegisterSaveHandler(Func<Board, bool> hasExtraData,
            Action<Board, string> saveExtra, Action<Board, string> loadExtra)
        {
            CustomSave.RegisterExtraHandler(hasExtraData, saveExtra, loadExtra);
        }

        /// <summary>关卡正式开始（首次进入 Playing）。游戏线程触发；IronPython 可直接 +=。</summary>
        public static event Action<Board> LevelStarted
        {
            add => CustomEvents.LevelStarted += value;
            remove => CustomEvents.LevelStarted -= value;
        }

        /// <summary>新一波僵尸出现（波号从 1 起）。游戏线程触发。</summary>
        public static event Action<Board, int> WaveStarted
        {
            add => CustomEvents.WaveStarted += value;
            remove => CustomEvents.WaveStarted -= value;
        }

        /// <summary>任意僵尸死亡（DieWithLoot 亦会触发且只触发一次）。游戏线程触发。</summary>
        public static event Action<Zombie> ZombieDied
        {
            add => CustomEvents.ZombieDied += value;
            remove => CustomEvents.ZombieDied -= value;
        }

        /// <summary>
        /// 场上出现一株模组植物：种下、附件生成、存档恢复都算。游戏线程触发。
        /// IronPython 写法：PlantKit.PlantPlanted += lambda board, plant, def: ...
        /// </summary>
        public static event Action<Board, Plant, CustomPlantDef> PlantPlanted
        {
            add => CustomEvents.PlantPlanted += value;
            remove => CustomEvents.PlantPlanted -= value;
        }

        /// <summary>模组植物离开棋盘（铲掉、被吃、被压、清场）。游戏线程触发。</summary>
        public static event Action<Board, Plant, CustomPlantDef> PlantRemoved
        {
            add => CustomEvents.PlantRemoved += value;
            remove => CustomEvents.PlantRemoved -= value;
        }

        /// <summary>
        /// 核对所有已注册定义的配置，把可疑组合写成 WARN 日志并返回文本。
        /// 宿主在全部包加载完之后自动跑一次；改完脚本想立刻看一眼结果就手动调。
        /// </summary>
        public static IReadOnlyList<string> ValidatePlants() => DefChecks.RunAll();

        /// <summary>
        /// 一份"现在到底装了什么"的快照：环境、包顺序、每株植物的实际字段、
        /// 钩子数与失败、素材与子弹、自检告警。返回全文，同时写盘
        /// （path 为空写 &lt;mod 目录&gt;\diagnostics.txt）。控制台里一行一个用。
        /// </summary>
        public static string DumpReport(string path = null) => Diagnostics.Write(path);

        /// <summary>给诊断快照追加自己那一段（宿主或第三方包用）。</summary>
        public static void AddReportSection(string title, Func<string> provider) =>
            Diagnostics.AddSection(title, provider);

        /// <summary>
        /// 按显示名、包 ID 或种子号找植物（自定义种子没有枚举名，控制台里靠这个拿 SeedType）。
        /// 三种匹配在同一次遍历里先到先得；找不到返回 null。
        /// </summary>
        public static CustomPlantDef FindPlant(string nameOrSeedId)
        {
            if (string.IsNullOrEmpty(nameOrSeedId))
            {
                return null;
            }
            CustomPlantDef contains = null;
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (string.Equals(def.Name, nameOrSeedId, StringComparison.Ordinal) ||
                    string.Equals(def.PackId, nameOrSeedId, StringComparison.OrdinalIgnoreCase))
                {
                    return def;
                }
                if (int.TryParse(nameOrSeedId, out int seedId) && def.SeedId == seedId)
                {
                    return def;
                }
                if (contains == null && def.Name != null &&
                    def.Name.IndexOf(nameOrSeedId, StringComparison.Ordinal) >= 0)
                {
                    contains = def;
                }
            }
            return contains;
        }

        /// <summary>当前生效的受管钩子 key（排序），用来核对"到底挂了几份"。</summary>
        public static IReadOnlyList<string> ManagedHookKeys => HookRegistry.Keys;

        public static void Log(string message)
        {
            ModLog.Info(message);
        }

        public static void LogError(string message, Exception ex = null)
        {
            ModLog.Error(message, ex);
        }

        /// <summary>第三方 mod 可用来安装自己的额外钩子。</summary>
        public static void AddHook(Type type, string name, Type[] args, Delegate hook)
        {
            HookUtil.Add(HookUtil.Method(type, name, args), hook);
        }

        // ------------------------------------------------------------ 受管 MonoMod 钩子（传统写法接入）

        /// <summary>
        /// 挂一个受管钩子（配合 LawnMod.MonoModUtils.HookTo 的结果使用）。
        /// 同名 key 再挂自动替换（重跑脚本零样板幂等）；HookResult 由 Kit 持有，
        /// 不会被 GC 终结器二次卸载。key 前缀用包 ID（.py 植物即文件名），这样
        /// UnmanageHooks(包ID + ".") 能整包清理。IronPython 写法：
        ///   PlantKit.ManageHook(PlantKit.CurrentPackId + u".watch_die",
        ///       M.HookTo(Plant.Die)(lambda orig, plant: ...))
        /// </summary>
        public static object ManageHook(string key, object hookResult)
        {
            return HookRegistry.Manage(key, hookResult);
        }

        /// <summary>按 key 卸载一个受管钩子。</summary>
        public static bool UnmanageHook(string key)
        {
            return HookRegistry.Unmanage(key);
        }

        /// <summary>卸掉 key 前缀匹配的全部受管钩子（例如一个包的），返回卸载数。</summary>
        public static int UnmanageHooks(string keyPrefix)
        {
            return HookRegistry.UnmanageByPrefix(keyPrefix);
        }

        /// <summary>当前生效的受管钩子数。</summary>
        public static int ManagedHookCount => HookRegistry.Count;

        /// <summary>
        /// 请求一次截图（任意线程可调，游戏里按 F12 同一个入口），返回即将写入的 PNG 路径。
        /// 只是登记，下一帧画完在游戏线程上落盘；用 <see cref="Screenshot.LastPath"/> 判断是否写完。
        /// </summary>
        public static string RequestScreenshot(string path = null)
        {
            return Screenshot.Request(path);
        }
    }
}
