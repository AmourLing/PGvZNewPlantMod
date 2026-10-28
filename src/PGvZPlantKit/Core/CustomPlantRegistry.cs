using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 自定义种子/动画注册表 + Plant 到行为实例的路由。
    /// 具体植物由 mod 或 IronPython 脚本调用 Register() 加进来。
    /// </summary>
    public static class CustomPlantRegistry
    {
        public static readonly int ReanimBase = (int)ReanimationType.NumReanims;

        private static readonly List<CustomPlantDef> DefList = new List<CustomPlantDef>();
        private static readonly Dictionary<int, CustomPlantDef> BySeed =
            new Dictionary<int, CustomPlantDef>();
        private static readonly ConditionalWeakTable<Plant, CustomPlantBase> Behaviors =
            new ConditionalWeakTable<Plant, CustomPlantBase>();

        private static bool _defsReady;
        private static int _initDepth;
        private static int _registrationCounter;
        private static readonly Dictionary<CustomPlantDef, int> RegistrationIndex =
            new Dictionary<CustomPlantDef, int>();

        public static IReadOnlyList<CustomPlantDef> Defs => DefList;

        /// <summary>种子号/图鉴格/选卡格/动画槽是否已经真正分配过（EnsureDefinitions 是否跑完过）。
        /// 自检里依赖排版的规则只在 True 时才判定，否则标题界面手动 DumpReport 会满屏假告警。</summary>
        public static bool DefinitionsInstalled => _defsReady;

        public static int InitDepth => _initDepth;

        /// <summary>
        /// 注册一种植物。可在 mod 启动或 IronPython 顶层调用。
        /// packId 对应 plant_packs.json 里的包 ID；为空时使用 def.PackId 或 "default"。
        /// 自动 SeedId 在首次 EnsureDefinitions 时按包顺序统一分配。
        /// </summary>
        public static void Register(CustomPlantDef def, string packId = null)
        {
            if (def == null)
            {
                throw new ArgumentNullException(nameof(def));
            }
            LoadOrder.EnsureLoaded();
            packId = !string.IsNullOrEmpty(packId)
                ? packId
                : (!string.IsNullOrEmpty(def.PackId)
                    ? def.PackId
                    : PlantKit.CurrentPackId);
            def.PackId = LoadOrder.NormalizePackId(packId);
            if (!LoadOrder.IsEnabled(def.PackId))
            {
                ModLog.Info("pack disabled, skip plant " + def.Name +
                    " (pack " + def.PackId + ")");
                return;
            }
            def.AutoSeedId = def.SeedId <= 0;
            def.AutoAlmanac = def.AlmanacRow <= 0 || def.AlmanacColumn <= 0;
            def.AutoChooser = def.ChooserColumn < 0 || def.ChooserRow < 0;
            def.AutoReanimSlot = def.UsesCustomAnimation && def.ReanimSlot < 0;
            if (!def.AutoSeedId && def.SeedId < 65)
            {
                throw new ArgumentOutOfRangeException(nameof(def.SeedId),
                    "custom seed ids must be >= 65");
            }
            if (def.Factory == null)
            {
                throw new ArgumentException("CustomPlantDef.Factory is required", nameof(def));
            }
            if (def.NativeReanimType != ReanimationType.None)
            {
                def.NativeReanim = true;
            }
            if (def.UsesCustomAnimation && string.IsNullOrEmpty(def.SpineName) &&
                string.IsNullOrEmpty(def.ReanimName))
            {
                throw new ArgumentException(
                    "SpineName or ReanimName is required unless NativeReanimType is set",
                    nameof(def));
            }
            if (def.UsesCustomAnimation && !def.AutoReanimSlot && def.ReanimSlot < 0)
            {
                throw new ArgumentException(
                    "ReanimSlot must be >= 0 or left at -1 for auto allocation",
                    nameof(def));
            }
            if (!def.AutoSeedId)
            {
                if (BySeed.TryGetValue(def.SeedId, out CustomPlantDef old) && old != def)
                {
                    if (old.AutoSeedId)
                    {
                        throw new ArgumentException(
                            "seed " + def.SeedId + " is already used by auto allocation; " +
                            "leave SeedId=0 or choose another id", nameof(def));
                    }
                    DefList.Remove(old);
                    RegistrationIndex.Remove(old);
                    ModLog.Error("duplicate custom seed id " + def.SeedId +
                        "; replacing " + old.Name + " with " + def.Name);
                }
                BySeed[def.SeedId] = def;
            }
            if (!DefList.Contains(def))
            {
                DefList.Add(def);
                RegistrationIndex[def] = _registrationCounter++;
            }
            _defsReady = false;
        }

        internal static int RegistrationOrderOf(CustomPlantDef def)
        {
            return def != null && RegistrationIndex.TryGetValue(def, out int index)
                ? index
                : int.MaxValue;
        }

        /// <summary>同一包内按注册顺序；跨包按 plant_packs.json 的顺序。</summary>
        internal static int ComparePackRegistration(CustomPlantDef a, CustomPlantDef b)
        {
            int byPack = LoadOrder.ComparePacks(a.PackId, b.PackId);
            return byPack != 0
                ? byPack
                : RegistrationOrderOf(a).CompareTo(RegistrationOrderOf(b));
        }

        /// <summary>自动排版：chooser_order 全局优先，其次包顺序、注册顺序。</summary>
        internal static int CompareForLayout(CustomPlantDef a, CustomPlantDef b)
        {
            int byChooser = a.ChooserOrder.CompareTo(b.ChooserOrder);
            return byChooser != 0 ? byChooser : ComparePackRegistration(a, b);
        }

        /// <summary>首次使用时按包顺序分配所有待定的自动 SeedId。</summary>
        private static void CommitAutoSeeds()
        {
            var pending = new List<CustomPlantDef>();
            foreach (CustomPlantDef def in DefList)
            {
                if (def.AutoSeedId && def.SeedId <= 0)
                {
                    pending.Add(def);
                }
            }
            if (pending.Count == 0)
            {
                return;
            }
            pending.Sort(ComparePackRegistration);
            foreach (CustomPlantDef def in pending)
            {
                int id = AllocateSeedId(AutoLayout.AutoSeedStart);
                def.SeedId = id;
                BySeed[id] = def;
                ModLog.Info("auto seed " + id + " for " + def.Name +
                    " (pack " + def.PackId + ")");
            }
        }

        public static int AllocateSeedId(int start = 94)
        {
            int id = Math.Max(65, start);
            // 其他 mod 可能已经扩展了植物定义表；从表尾之后开始，避免撞号。
            PlantDefinition[] defs = GameConstants.gPlantDefs;
            if (defs != null)
            {
                id = Math.Max(id, defs.Length);
            }
            while (BySeed.ContainsKey(id))
            {
                id++;
            }
            return id;
        }

        public static CustomPlantDef GetDef(SeedType seed)
        {
            BySeed.TryGetValue((int)seed, out CustomPlantDef def);
            return def;
        }

        /// <summary>
        /// 升级配对的那一条判式（只此一份）：基础株写 <c>UpgradesTo</c>（原生卡）或
        /// <c>UpgradesToName</c>（模组卡，种子号要到首次排版才有），卡那一侧写 <c>IsUpgradeCard</c>。
        /// 钩子、选卡闸门、自检都问这里，别各自再抄一遍。
        /// </summary>
        public static bool UpgradesInto(CustomPlantDef baseDef, SeedType cardSeed,
            string cardName)
        {
            if (baseDef == null)
            {
                return false;
            }
            if (baseDef.UpgradesTo != SeedType.None && (int)cardSeed > 0 &&
                baseDef.UpgradesTo == cardSeed)
            {
                // (int)cardSeed > 0：模组卡的 SeedId 要首次排版才分配，没排版时它是 0，
                // 而 0 恰好是豌豆射手 —— 和 IsMine 同一个坑。
                return true;
            }
            return !string.IsNullOrEmpty(baseDef.UpgradesToName) &&
                string.Equals(baseDef.UpgradesToName, cardName, StringComparison.Ordinal);
        }

        /// <summary>这张卡是被谁声明为升级目标的（找不到 = 没有基础卡，或它本身不是升级卡）。</summary>
        public static CustomPlantDef UpgradedFrom(CustomPlantDef card)
        {
            if (card == null)
            {
                return null;
            }
            foreach (CustomPlantDef other in Defs)
            {
                if (other != card && UpgradesInto(other, card.Seed, card.Name))
                {
                    return other;
                }
            }
            return null;
        }

        public static bool IsCustomSeed(SeedType seed)
        {
            return BySeed.ContainsKey((int)seed);
        }

        public static bool IsSelectableCustomSeed(SeedType seed)
        {
            CustomPlantDef def = GetDef(seed);
            return def != null && def.Selectable;
        }

        public static SeedType EffectiveCustomSeed(SeedType seed, SeedType imitater)
        {
            if (IsCustomSeed(seed))
            {
                return seed;
            }
            if (seed == SeedType.Imitater && IsCustomSeed(imitater))
            {
                return imitater;
            }
            return SeedType.None;
        }

        public static bool TryGet(Plant plant, out CustomPlantBase behavior)
        {
            behavior = null;
            return plant != null && Behaviors.TryGetValue(plant, out behavior);
        }

        public static CustomPlantBase Get(Plant plant)
        {
            return plant != null && Behaviors.TryGetValue(plant, out CustomPlantBase behavior)
                ? behavior
                : null;
        }

        internal static void ClearLoadedBehaviors() => Behaviors.Clear();

        public static CustomPlantBase CreateForPlant(Plant plant, CustomPlantDef def)
        {
            if (def == null || def.Factory == null)
            {
                return null;
            }
            CustomPlantBase behavior = def.Factory();
            if (behavior == null)
            {
                return null;
            }
            behavior.Definition = def;
            behavior.Attach(plant);
            Behaviors.Remove(plant);
            Behaviors.Add(plant, behavior);
            CustomEvents.OnPlantPlanted(plant, def);
            return behavior;
        }

        public static void AttachBehavior(Plant plant, CustomPlantBase behavior,
            CustomPlantDef def)
        {
            behavior.Definition = def;
            behavior.Attach(plant);
            Behaviors.Remove(plant);
            Behaviors.Add(plant, behavior);
            CustomEvents.OnPlantPlanted(plant, def);
        }

        public static void Remove(Plant plant)
        {
            if (plant == null)
            {
                return;
            }
            if (Behaviors.TryGetValue(plant, out CustomPlantBase behavior))
            {
                try
                {
                    behavior.OnRemoved();
                }
                catch (Exception ex)
                {
                    ModLog.Error("OnRemoved failed", ex);
                }
                try
                {
                    behavior.ReleaseInstanceHooks();
                }
                catch (Exception ex)
                {
                    ModLog.Error("ReleaseInstanceHooks failed", ex);
                }
                Behaviors.Remove(plant);
                CustomEvents.OnPlantRemoved(plant, behavior.Definition);
            }
        }

        public static bool PlantIsCustom(Plant plant)
        {
            return plant != null &&
                   (IsCustomSeed(plant.mSeedType) || Behaviors.TryGetValue(plant, out _));
        }

        // ------------------------------------------------------------ 棋盘级事件广播

        /// <summary>把回调派给棋盘上每个自定义植物；回调里可能增删植物，按下标循环容忍。</summary>
        private static void ForEachBehavior(Board board, Action<CustomPlantBase> dispatch)
        {
            if (board == null || board.mPlants == null)
            {
                return;
            }
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = i < board.mPlants.Count ? board.mPlants[i] : null;
                if (plant == null)
                {
                    continue;
                }
                CustomPlantBase behavior = Get(plant);
                if (behavior == null || behavior.Plant == null)
                {
                    continue;
                }
                try
                {
                    dispatch(behavior);
                }
                catch (Exception ex)
                {
                    ModLog.Error("board event dispatch failed for " +
                                 (behavior.Definition != null ? behavior.Definition.Name : "?"), ex);
                }
            }
        }

        internal static void DispatchLevelStart(Board board)
        {
            ForEachBehavior(board, behavior => behavior.OnLevelStart());
        }

        internal static void DispatchWave(Board board, int wave)
        {
            ForEachBehavior(board, behavior => behavior.OnWave(wave));
        }

        internal static void DispatchZombieDied(Board board, Zombie zombie)
        {
            if (zombie == null)
            {
                return;
            }
            // 只广播给同一行的植物，避免全场自定义植物每杀一只都跳一遍。
            ForEachBehavior(board, behavior =>
            {
                if (behavior.Plant.mRow == zombie.mRow)
                {
                    behavior.OnZombieDied(zombie);
                }
            });
        }

        internal static void DispatchEatenStart(Plant plant, Zombie eater)
        {
            CustomPlantBase behavior = Get(plant);
            if (behavior == null || eater == null)
            {
                return;
            }
            try
            {
                behavior.OnEatenStart(eater);
            }
            catch (Exception ex)
            {
                ModLog.Error("OnEatenStart failed for " +
                             (behavior.Definition != null ? behavior.Definition.Name : "?"), ex);
            }
        }

        internal static void BeginInitialize()
        {
            _initDepth++;
        }

        internal static void EndInitialize()
        {
            if (_initDepth > 0)
            {
                _initDepth--;
            }
        }

        // ---------------------------------------------------------------- 注册到游戏表

        public static void EnsureDefinitions(bool validate = false)
        {
            if (_defsReady && (!validate || DefinitionsStillInstalled()))
            {
                return;
            }
            CommitAutoSeeds();
            foreach (CustomPlantDef def in Defs)
            {
                CustomStrings.Ensure(def);
            }
            if (!AutoLayout.Ensure())
            {
                return;
            }
            PlantDefinition[] plantDefs = GameConstants.gPlantDefs;
            if (plantDefs == null)
            {
                return;
            }

            int maxSeed = 64;
            int maxReanim = ReanimBase - 1;
            bool anyCustomAnimation = false;
            foreach (CustomPlantDef def in DefList)
            {
                maxSeed = Math.Max(maxSeed, def.SeedId);
                if (def.UsesCustomAnimation)
                {
                    anyCustomAnimation = true;
                    maxReanim = Math.Max(maxReanim, (int)def.ReanimType);
                }
            }

            if (plantDefs.Length <= maxSeed)
            {
                plantDefs = ExtendArray(plantDefs, maxSeed + 1);
                GameConstants.gPlantDefs = plantDefs;
            }
            ReanimationType fallback = plantDefs.Length > 0 && plantDefs[0] != null
                ? plantDefs[0].mReanimationType
                : ReanimationType.Peashooter;
            foreach (CustomPlantDef def in DefList)
            {
                plantDefs[def.SeedId] = new PlantDefinition(
                    def.Seed, null, def.ReanimType, def.PacketIndex, def.Cost,
                    def.RefreshTime, def.SubClass, def.LaunchRate, def.Name);
            }
            for (int i = 0; i < plantDefs.Length; i++)
            {
                if (plantDefs[i] == null)
                {
                    plantDefs[i] = new PlantDefinition(
                        SeedType.None, null, fallback, 0, 0, 0,
                        PlantSubClass.Normal, 0, "UNUSED_CUSTOM_SEED_SLOT");
                }
            }

            if (!anyCustomAnimation)
            {
                _defsReady = true;
                ModLog.Info("custom plant definitions registered: " + DefList.Count +
                    " native-reanim plant(s)");
                DefChecks.RunOnceAfterLayout();
                return;
            }

            ReanimationParams[] reanimParams = GameConstants.gLawnReanimationArray;
            if (reanimParams == null)
            {
                return;
            }
            int needed = maxReanim + 1;
            if (reanimParams.Length < needed)
            {
                reanimParams = ExtendArray(reanimParams, needed);
                GameConstants.gLawnReanimationArray = reanimParams;
            }
            foreach (CustomPlantDef def in DefList)
            {
                if (def.UsesCustomSpine)
                {
                    reanimParams[(int)def.ReanimType] = new ReanimationParams(
                        def.ReanimType, "spanim/" + def.SpineName, 0, true, null, 14.3f);
                }
                else if (def.UsesCustomReanim)
                {
                    // 参数表占位保持密集：整表重载时逐项读 mReanimationType，空洞会直接崩。
                    // 真正的定义由 ReanimatorEnsureDefinitionLoaded 钩子从内嵌资源构造。
                    reanimParams[(int)def.ReanimType] = new ReanimationParams(
                        def.ReanimType, "reanim/" + def.ReanimName, 0);
                    CustomReanim.Register(def.ReanimType, def.ReanimName);
                }
            }

            if (!InstallReanimatorCache(reanimParams, needed, maxReanim))
            {
                return;
            }
            _defsReady = true;
            ModLog.Info("custom plant definitions registered: " + DefList.Count +
                " plant(s), reanims " + ReanimBase + "-" + maxReanim);
            DefChecks.RunOnceAfterLayout();
        }

        private static bool DefinitionsStillInstalled()
        {
            int maxReanim = ReanimBase - 1;
            bool anyCustomAnimation = false;
            foreach (CustomPlantDef def in DefList)
            {
                if (def.UsesCustomAnimation)
                {
                    anyCustomAnimation = true;
                    maxReanim = Math.Max(maxReanim, (int)def.ReanimType);
                }
            }
            if (!anyCustomAnimation)
            {
                return true;
            }
            try
            {
                Type helpers = typeof(Reanimation).Assembly.GetType("Sexy.TodLib.ReanimatorXnaHelpers");
                const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic;
                FieldInfo defField = helpers?.GetField("gReanimatorDefArray", Flags);
                FieldInfo paramField = helpers?.GetField("gReanimationParamArray", Flags);
                FieldInfo sizeField = helpers?.GetField("gReanimationParamArraySize", Flags);
                if (defField == null || paramField == null || sizeField == null)
                {
                    return false;
                }
                int needed = maxReanim + 1;
                Array definitions = defField.GetValue(null) as Array;
                Array parameters = paramField.GetValue(null) as Array;
                int size = (int)sizeField.GetValue(null);
                return definitions != null && parameters != null &&
                       definitions.Length >= needed && parameters.Length >= needed &&
                       size >= needed && definitions.GetValue(maxReanim) != null;
            }
            catch
            {
                return false;
            }
        }

        private static bool InstallReanimatorCache(ReanimationParams[] reanimParams,
            int needed, int maxReanim)
        {
            try
            {
                Type helpers = typeof(Reanimation).Assembly.GetType("Sexy.TodLib.ReanimatorXnaHelpers");
                if (helpers == null)
                {
                    ModLog.Error("ReanimatorXnaHelpers type not found");
                    return false;
                }
                const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public |
                                           BindingFlags.NonPublic;
                FieldInfo defField = helpers.GetField("gReanimatorDefArray", Flags);
                FieldInfo paramField = helpers.GetField("gReanimationParamArray", Flags);
                FieldInfo sizeField = helpers.GetField("gReanimationParamArraySize", Flags);
                FieldInfo countField = helpers.GetField("gReanimatorDefCount", Flags);
                MethodInfo ensure = helpers.GetMethod("ReanimatorEnsureDefinitionLoaded", Flags);
                if (defField == null || paramField == null || sizeField == null ||
                    countField == null || ensure == null)
                {
                    ModLog.Error("ReanimatorXnaHelpers cache fields not found");
                    return false;
                }

                Array definitions = defField.GetValue(null) as Array;
                if (definitions == null)
                {
                    definitions = Array.CreateInstance(typeof(ReanimatorDefinition), needed);
                }
                int oldSize = (int)sizeField.GetValue(null);
                int oldCount = (int)countField.GetValue(null);
                int finalSize = Math.Max(needed, Math.Max(oldSize, oldCount));
                if (definitions.Length < finalSize)
                {
                    Array grown = Array.CreateInstance(
                        definitions.GetType().GetElementType(), finalSize);
                    Array.Copy(definitions, grown, definitions.Length);
                    definitions = grown;
                }
                if (reanimParams.Length < finalSize)
                {
                    reanimParams = ExtendArray(reanimParams, finalSize);
                    GameConstants.gLawnReanimationArray = reanimParams;
                }
                defField.SetValue(null, definitions);
                paramField.SetValue(null, reanimParams);
                sizeField.SetValue(null, finalSize);
                countField.SetValue(null, finalSize);

                foreach (CustomPlantDef def in DefList)
                {
                    if (!def.UsesCustomAnimation)
                    {
                        continue;
                    }
                    string asset = def.UsesCustomReanim
                        ? "Content/reanim/" + def.ReanimName + ".reanim"
                        : "Content/spanim/" + def.SpineName + ".png";
                    if (!ModPaths.HasContent(asset))
                    {
                        ModLog.Error("missing animation asset: " + asset);
                    }
                    try
                    {
                        ensure.Invoke(null, new object[] { def.ReanimType, false });
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error("load animation failed: " +
                            (def.UsesCustomReanim ? def.ReanimName : def.SpineName), ex);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLog.Error("InstallReanimatorCache failed", ex);
                return false;
            }
        }

        private static T[] ExtendArray<T>(T[] old, int length)
        {
            T[] result = new T[length];
            Array.Copy(old, result, Math.Min(old.Length, length));
            return result;
        }

        public static void StartPacketCooldown(Plant plant, CustomPlantDef def)
        {
            SeedBank bank = plant?.mBoard?.mSeedBank;
            if (bank == null || def == null)
            {
                return;
            }
            // 和原生 SeedPacket.WasPlanted 同一条件：坚不可摧开场前种植不进冷却。
            if (plant.mApp != null &&
                plant.mApp.mGameMode == GameMode.ChallengeLastStand &&
                plant.mBoard.mChallenge.mChallengeState != ChallengeState.LastStandOnslaught)
            {
                return;
            }
            int refresh = def.RefreshTime > 0 ? def.RefreshTime : 750;
            for (int i = 0; i < bank.mNumPackets; i++)
            {
                SeedPacket packet = bank.mSeedPackets[i];
                if ((int)packet.mPacketType != def.SeedId)
                {
                    continue;
                }
                packet.mRefreshing = true;
                packet.mRefreshTime = refresh;
                packet.mRefreshCounter = 0;
                packet.mActive = false;
                return;
            }
        }
    }
}
