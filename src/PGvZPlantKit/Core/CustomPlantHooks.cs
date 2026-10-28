using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 框架钩子：生命周期、费用/名字、选卡/图鉴、存档、通用弹道、大招按钮。
    /// 具体植物的额外机制（瓷砖、守卫联动、专属特效等）由 mod 自己安装钩子。
    /// </summary>
    public static class CustomPlantHooks
    {
        private static bool _installed;
        private static readonly HashSet<Plant> ErrorReported = new HashSet<Plant>();
        private static readonly HashSet<string> HotErrors = new HashSet<string>();

        public static readonly List<string> HookFailures = new List<string>();
        public static int HookCount { get; private set; }
        public static bool Installed => _installed;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            foreach (MethodInfo method in typeof(CustomPlantHooks).GetMethods(
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                HookToAttribute hookTo = method.GetCustomAttribute<HookToAttribute>();
                if (hookTo != null)
                {
                    InstallDeclared(method, hookTo);
                }
            }


            // 引擎关选卡时会用自定义种子去索引原生 65 格的 mChosenSeeds，必须先摘掉再还原。
            Type saveHelpers = typeof(Board).Assembly.GetType("Lawn.GlobalMembersSaveGame", true);
            Add(saveHelpers, "LawnSaveGame", new[] { typeof(Board), typeof(string) },
                (Func<Func<Board, string, bool>, Board, string, bool>)LawnSaveGameHook);
            Add(saveHelpers, "LawnLoadGame", new[] { typeof(Board), typeof(string) },
                (Func<Func<Board, string, bool>, Board, string, bool>)LawnLoadGameHook);
            CustomSave.Install((method, hook) => Add(method.DeclaringType, method.Name,
                Array.ConvertAll(method.GetParameters(), parameter => parameter.ParameterType), hook));
            // 原生实现断言 SeedType < 65；图鉴预览必须能读取自动种子的扩展表项。
            // Android 的 TitleContainer 只认 APK 内资源；自定义 Spine 需要回退到 mod 目录。
            // 经典逐帧 .reanim 不在游戏 Content 表里：命中的槽位直接换成我们构造的定义。
            Type reanimHelpers = typeof(Reanimation).Assembly
                .GetType("Sexy.TodLib.ReanimatorXnaHelpers");
            if (reanimHelpers != null)
            {
                Add(reanimHelpers, "ReanimatorEnsureDefinitionLoaded",
                    new[] { typeof(ReanimationType), typeof(bool) },
                    (Action<Action<ReanimationType, bool>, ReanimationType, bool>)
                    ReanimEnsureDefinitionLoadedHook);
            }

            ModLog.Info("core hooks installed: " + HookCount);
        }

        [HookTo(typeof(Plant), "SaveToFile", typeof(Sexy.Buffer))]
        private static bool PlantSaveToFileHook(Func<Plant, Sexy.Buffer, bool> orig,
            Plant self, Sexy.Buffer buffer)
        {
            try
            {
                return orig(self, buffer);
            }
            catch (Exception ex)
            {
                ModLog.Error("Plant.SaveToFile failed, seed=" + (int)self.mSeedType, ex);
                throw;
            }
        }

        [HookTo(typeof(Plant), "LoadFromFile", typeof(Sexy.Buffer))]
        private static bool PlantLoadFromFileHook(Func<Plant, Sexy.Buffer, bool> orig,
            Plant self, Sexy.Buffer buffer)
        {
            try
            {
                return orig(self, buffer);
            }
            catch (Exception ex)
            {
                // 引擎读档吞异常，这里把“哪一株、什么种子”记下来才能定位。
                ModLog.Error("Plant.LoadFromFile failed, seed=" + (int)self.mSeedType, ex);
                throw;
            }
        }

        [HookTo(typeof(Board), "LoadGame", typeof(string))]
        private static bool BoardLoadGameHook(Func<Board, string, bool> orig, Board self, string theFilePath)
        {
            bool result = orig(self, theFilePath);
            if (!result)
            {
                return result;
            }
            // 旧存档可能带着崩溃帧写出的半初始化弹体；类型越界的弹体一律标记死亡，
            // 否则 Projectile.Draw/Update 里 gProjectileDefinition[type] 越界会一路
            // 炸进宿主 Main.Update 的 catch，把整个主循环堵死在报错框上。
            foreach (Projectile projectile in self.mProjectiles)
            {
                int type = (int)projectile.mProjectileType;
                if (type < 0 || GameConstants.gProjectileDefinition == null
                    || type >= GameConstants.gProjectileDefinition.Length)
                {
                    projectile.mDead = true;
                    ModLog.Error("save projectile type out of range: " + type + " (removed on load)");
                }
            }
            return result;
        }

        private static void ReanimEnsureDefinitionLoadedHook(
            Action<ReanimationType, bool> orig, ReanimationType type, bool isPreloading)
        {
            if (CustomReanim.TrySupply(type))
            {
                return;
            }
            orig(type, isPreloading);
        }

        [HookTo(typeof(Plant), "GetPlantDefinition", typeof(SeedType))]
        private static PlantDefinition PlantGetPlantDefinitionHook(
            Func<SeedType, PlantDefinition> orig, SeedType seed)
        {
            PlantDefinition[] definitions = GameConstants.gPlantDefs;
            int index = (int)seed;
            if (CustomPlantRegistry.IsCustomSeed(seed))
            {
                if (definitions != null && index >= 0 && index < definitions.Length &&
                    definitions[index] != null)
                {
                    return definitions[index];
                }
            }
            if (definitions != null && (index < 0 || index >= definitions.Length))
            {
                // 空卡槽是 SeedType.None(-1)：捡阳光时引擎会拿它算每张卡的费用，
                // 原实现直接索引数组就越界。给一个安全定义，别让整个捡阳光流程中断。
                for (int i = 0; i < definitions.Length; i++)
                {
                    if (definitions[i] != null)
                    {
                        return definitions[i];
                    }
                }
            }
            return orig(seed);
        }

        [HookTo(typeof(Microsoft.Xna.Framework.TitleContainer), "OpenStream", typeof(string))]
        private static Stream TitleContainerOpenStreamHook(
            Func<string, Stream> orig, string path)
        {
            Exception error = null;
            try
            {
                Stream stream = orig(path);
                if (stream != null)
                {
                    return stream;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            try
            {
                Stream fallback = ModPaths.OpenContent(path);
                if (fallback != null)
                {
                    return fallback;
                }
            }
            catch (Exception ex)
            {
                if (error == null)
                {
                    error = ex;
                }
            }
            if (error != null)
            {
                throw error;
            }
            throw new FileNotFoundException("Content not found: " + path);
        }

        /// <summary>每帧画完、Present 之前抓后备缓冲，见 <see cref="Screenshot"/>。</summary>
        [HookTo(typeof(LawnApp), "DrawGame", typeof(Microsoft.Xna.Framework.GameTime))]
        private static void LawnAppDrawGameHook(
            Action<LawnApp, Microsoft.Xna.Framework.GameTime> orig,
            LawnApp self, Microsoft.Xna.Framework.GameTime gameTime)
        {
            orig(self, gameTime);
            Screenshot.OnFrameEnd();
        }

        /// <summary>F12 截图，F9 转储诊断快照；不拦原逻辑，按键照常传给游戏。</summary>
        [HookTo(typeof(WidgetManager), "KeyDown", typeof(KeyCode))]
        private static bool WidgetManagerKeyDownHook(
            Func<WidgetManager, KeyCode, bool> orig, WidgetManager self, KeyCode key)
        {
            if (key == KeyCode.F12)
            {
                Screenshot.Request();
            }
            else if (key == KeyCode.F9)
            {
                Diagnostics.DumpToLog();
            }
            return orig(self, key);
        }

        /// <summary>
        /// 按处理器自己的签名推出委托类型装钩子：第一个形参是被钩方法的委托，
        /// 返回类型跟处理器一致；目标解析方式和原来的 Add(...) 表一样。
        /// </summary>
        private static void InstallDeclared(MethodInfo method, HookToAttribute hookTo)
        {
            try
            {
                MethodInfo target = HookUtil.Method(hookTo.Target, hookTo.MethodName,
                    hookTo.Parameters);
                ParameterInfo[] parameters = method.GetParameters();
                Type[] signature = new Type[parameters.Length + 1];
                for (int i = 0; i < parameters.Length; i++)
                {
                    signature[i] = parameters[i].ParameterType;
                }
                signature[parameters.Length] = method.ReturnType;
                HookUtil.Add(target, method.CreateDelegate(Expression.GetDelegateType(signature)));
                HookCount++;
            }
            catch (Exception ex)
            {
                HookFailures.Add(hookTo.Target.Name + "." + hookTo.MethodName + ": " + ex.Message);
                ModLog.Error("hook install failed: " + hookTo.Target.Name + "." +
                             hookTo.MethodName + " (" + method.Name + ")", ex);
            }
        }

        private static void Add(Type type, string name, Type[] args, Delegate hook)
        {
            try
            {
                HookUtil.Add(HookUtil.Method(type, name, args), hook);
                HookCount++;
            }
            catch (Exception ex)
            {
                HookFailures.Add(type.Name + "." + name + ": " + ex.Message);
                ModLog.Error("hook install failed: " + type.Name + "." + name, ex);
            }
        }

        private static void Hot(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (HotErrors.Add(where))
                {
                    ModLog.Error("hot path failed: " + where, ex);
                }
            }
        }

        private static void Guard(Plant plant, string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (plant == null || ErrorReported.Add(plant))
                {
                    ModLog.Error(where, ex);
                }
            }
        }

        // ------------------------------------------------------------ 生命周期

        [HookTo(typeof(Plant), "PlantInitialize", typeof(int), typeof(int), typeof(SeedType), typeof(SeedType))]
        private static void PlantInitializeHook(
            Action<Plant, int, int, SeedType, SeedType> orig, Plant self,
            int gridX, int gridY, SeedType seedType, SeedType imitaterType)
        {
            CustomPlantRegistry.Remove(self);
            bool custom = CustomPlantRegistry.IsCustomSeed(seedType);
            if (!custom && CustomPlantRegistry.IsCustomSeed(imitaterType))
            {
                CustomPlantRegistry.EnsureDefinitions(validate: true);
            }
            if (!custom)
            {
                orig(self, gridX, gridY, seedType, imitaterType);
                return;
            }

            CustomPlantRegistry.EnsureDefinitions(validate: true);
            CustomPlantDef def = CustomPlantRegistry.GetDef(seedType);
            if (def == null)
            {
                orig(self, gridX, gridY, seedType, imitaterType);
                return;
            }

            PlantDefinition swappedDef = null;
            ReanimationType savedReanim = ReanimationType.Peashooter;
            try
            {
                if (!def.NativeReanim && def.UsesCustomAnimation)
                {
                    swappedDef = GameConstants.gPlantDefs[def.SeedId];
                    if (swappedDef != null)
                    {
                        savedReanim = swappedDef.mReanimationType;
                        swappedDef.mReanimationType = ReanimationType.Peashooter;
                    }
                }
                CustomPlantRegistry.BeginInitialize();
                SeedType nativeSeed = def.NativeSeedOverride;
                if (nativeSeed == SeedType.None && def.NativeReanim)
                {
                    // 豌豆类的身体骨架带 --noshow，真正显示的是原生按豌豆种子挂上来的"头"重动画。
                    // 自定义种子走原生初始化拿不到这个头，场上就只剩影子。
                    nativeSeed = HeadSeedFor(def.NativeReanimType);
                }
                if (nativeSeed == SeedType.None)
                {
                    nativeSeed = seedType;
                }
                orig(self, gridX, gridY, nativeSeed, imitaterType);
                if (nativeSeed != seedType)
                {
                    // 头/附件已经按原生种子建好，立刻把身份与依赖种子的字段还原成模组值。
                    self.mSeedType = seedType;
                    self.mSubclass = (int)def.SubClass;
                    self.mLaunchRate = def.LaunchRate;
                }
            }
            finally
            {
                if (swappedDef != null)
                {
                    swappedDef.mReanimationType = savedReanim;
                }
                CustomPlantRegistry.EndInitialize();
            }

            CustomPlantBase behavior = CustomPlantRegistry.CreateForPlant(self, def);
            if (behavior == null)
            {
                return;
            }
            Guard(self, "AfterNativeInitialize " + def.SeedId, behavior.AfterNativeInitialize);
        }

        /// <summary>
        /// 这些原生身体自己带 --noshow，可见立绘是 PlantInitialize 按种子创建的"头"；
        /// 用自定义种子初始化会没有头。返回空表示这个身体不依赖头。
        /// </summary>
        private static SeedType HeadSeedFor(ReanimationType reanimType) =>
            reanimType == ReanimationType.Peashooter ? SeedType.Peashooter : SeedType.None;

        [HookTo(typeof(Plant), "Update")]
        private static void PlantUpdateHook(Action<Plant> orig, Plant self)
        {
            orig(self);
            if (!CustomPlantRegistry.PlantIsCustom(self))
            {
                return;
            }
            if (self.mDead || self.mSquished)
            {
                CustomPlantRegistry.Remove(self);
                return;
            }
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (behavior == null)
            {
                return;
            }
            Guard(self, "OnUpdate " + behavior.Definition.SeedId, () =>
            {
                // Tick 先走：派发 OnBitten 与 Schedule 定时器，再进用户逻辑。
                behavior.Tick();
                behavior.OnUpdate();
                behavior.AlignBody();
            });
            TintOwnParticle(self, behavior.Definition);
        }

        [HookTo(typeof(Plant), "UpdateReanim")]
        private static void PlantUpdateReanimHook(Action<Plant> orig, Plant self)
        {
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (CustomPlantRegistry.InitDepth > 0 &&
                (CustomPlantRegistry.IsCustomSeed(self.mSeedType) || behavior != null))
            {
                return;
            }
            if (behavior != null && !behavior.Definition.NativeReanim)
            {
                Reanimation body = self.mApp.ReanimationTryToGet(self.mBodyReanimID);
                body?.Update();
                return;
            }
            orig(self);
        }

        [HookTo(typeof(Plant), "Die")]
        private static void PlantDieHook(Action<Plant> orig, Plant self)
        {
            CustomPlantRegistry.Remove(self);
            orig(self);
        }

        [HookTo(typeof(Plant), "Squish")]
        private static void PlantSquishHook(Action<Plant> orig, Plant self)
        {
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (behavior != null && behavior.OnSquish())
            {
                return;
            }
            CustomPlantRegistry.Remove(self);
            orig(self);
        }

        [HookTo(typeof(Plant), "Draw", typeof(Graphics))]
        private static void PlantDrawHook(Action<Plant, Graphics> orig, Plant self, Graphics g)
        {
            orig(self, g);
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (behavior != null)
            {
                Hot("Plant.Draw.top", () => behavior.OnDrawTop(g));
            }
        }

        [HookTo(typeof(Plant), "MouseDown", typeof(int), typeof(int), typeof(int))]
        private static void PlantMouseDownHook(Action<Plant, int, int, int> orig,
            Plant self, int x, int y, int clickCount)
        {
            Board board = self.mBoard;
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (behavior != null && clickCount >= 0 && board != null &&
                self.mApp.mGameScene == GameScenes.Playing &&
                board.mCursorObject != null &&
                board.mCursorObject.mCursorType == CursorType.Normal)
            {
                if (behavior.OnMouseDown(x, y, clickCount))
                {
                    return;
                }
            }
            orig(self, x, y, clickCount);
        }

        [HookTo(typeof(Plant), "Fire", typeof(Zombie), typeof(int), typeof(PlantWeapon))]
        private static void PlantFireHook(Action<Plant, Zombie, int, PlantWeapon> orig,
            Plant self, Zombie target, int row, PlantWeapon weapon)
        {
            CustomPlantBase behavior = CustomPlantRegistry.Get(self);
            if (behavior != null)
            {
                behavior.OnNativeFire(orig, target, row, weapon);
                return;
            }
            orig(self, target, row, weapon);
        }

        [HookTo(typeof(Plant), "DrawSeedType", typeof(Graphics), typeof(SeedType), typeof(SeedType), typeof(DrawVariation), typeof(float), typeof(float))]
        private static void PlantDrawSeedTypeHook(
            Action<Graphics, SeedType, SeedType, DrawVariation, float, float> orig,
            Graphics g, SeedType seed, SeedType imitater, DrawVariation variation,
            float x, float y)
        {
            CustomUi.NativeDrawSeedType ??= orig;
            CustomPlantDef def = CustomUi.DefFor(seed, imitater);
            if (def == null)
            {
                orig(g, seed, imitater, variation, x, y);
                return;
            }
            if (def.HasPreview)
            {
                Hot("Plant.DrawSeedType", () => CustomUi.DrawSeedType(g, seed, imitater, x, y));
                return;
            }
            if (def.CardNativeSeed != SeedType.None)
            {
                orig(g, def.CardNativeSeed, SeedType.None, variation, x, y);
            }
        }

        // ------------------------------------------------------------ 费用与名字

        [HookTo(typeof(Plant), "GetCost", typeof(SeedType), typeof(SeedType))]
        private static int PlantGetCostHook(Func<SeedType, SeedType, int> orig,
            SeedType seed, SeedType imitater)
        {
            int cost = CustomUi.CustomCost(seed, imitater, null);
            if (cost >= 0)
            {
                return cost;
            }
            if ((int)seed >= 65 || (int)imitater >= 65)
            {
                return 0;
            }
            return orig(seed, imitater);
        }

        [HookTo(typeof(Plant), "GetRefreshTime", typeof(SeedType), typeof(SeedType))]
        private static int PlantGetRefreshTimeHook(Func<SeedType, SeedType, int> orig,
            SeedType seed, SeedType imitater)
        {
            SeedType custom = CustomPlantRegistry.EffectiveCustomSeed(seed, imitater);
            if (custom != SeedType.None)
            {
                return CustomUi.CustomRefresh(custom);
            }
            return orig(seed, imitater);
        }

        [HookTo(typeof(Plant), "IsUpgrade", typeof(SeedType))]
        private static bool PlantIsUpgradeHook(Func<SeedType, bool> orig, SeedType seed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
            if (def != null)
            {
                // 模组升级卡由定义自己声明；没声明的模组种子仍然不是升级卡（和以前一样）。
                return def.IsUpgradeCard;
            }
            return orig(seed);
        }

        [HookTo(typeof(Plant), "GetNameString", typeof(SeedType), typeof(SeedType))]
        private static string PlantGetNameStringHook(
            Func<SeedType, SeedType, string> orig, SeedType seed, SeedType imitater)
        {
            SeedType custom = CustomPlantRegistry.EffectiveCustomSeed(seed, imitater);
            if (custom != SeedType.None)
            {
                return CustomUi.CustomName(custom) ?? orig(seed, imitater);
            }
            return orig(seed, imitater);
        }

        [HookTo(typeof(Plant), "checkForPlantAchievements")]
        private static void PlantCheckAchievementsHook(Action<Plant> orig, Plant self)
        {
            if (CustomPlantRegistry.IsCustomSeed(self.mSeedType))
            {
                return;
            }
            orig(self);
        }

        // ------------------------------------------------------------ 借原生身份
        // 引擎里有一批"按 mSeedType 特判"的小灶（白天睡觉、开火定格几帧、开火特效、
        // 影子大小、播哪条语音、能不能被升级卡覆盖、算不算真菌）。自定义种子不在那些
        // 硬编码名单里，一条都拿不到，所以按 CustomPlantDef 的声明逐项补回去。
        // 行为类（睡觉/定格/升级/真菌）按声明直接回话；外形类（影子/睡姿/开火特效）
        // 只在绘制或特效那几行里临时换种子再调原方法，不抄引擎的坐标常量。

        private static CustomPlantDef Borrow(Plant plant)
        {
            return plant == null ? null : CustomPlantRegistry.GetDef(plant.mSeedType);
        }

        [HookTo(typeof(Plant), "IsNocturnal", typeof(SeedType))]
        private static bool PlantIsNocturnalHook(Func<SeedType, bool> orig, SeedType seed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
            return def != null ? def.Nocturnal : orig(seed);
        }

        /// <summary>
        /// "有睡觉这套动画切换"和"是夜间植物"是两件事：引擎只给小喷菇/大喷菇/冰菇/海蘑菇
        /// 开这条（<c>Plant.DoAdvancedSleeping</c>），忧郁菇不在里面。跟着 <c>Nocturnal</c> 一律放行，
        /// 借忧郁菇外形的模组植物就会去播 <c>anim_sleep_to_idle</c>（那轨在忧郁菇的 reanim 里
        /// 只有关键字覆盖到的眼睛，身体整段没了），而且 <c>AnimateSleeping</c> 每次切换完
        /// 都把待机重播成 30 帧/秒 —— 原生忧郁菇的待机不是这个速率。
        /// 所以按 <c>DrawAsSeed</c> 借的那个原生种子问引擎自己；没借身份的才退回 <c>Nocturnal</c>。
        /// </summary>
        [HookTo(typeof(Plant), "DoAdvancedSleeping", typeof(SeedType))]
        private static bool PlantDoAdvancedSleepingHook(Func<SeedType, bool> orig, SeedType seed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
            if (def == null)
            {
                return orig(seed);
            }
            return def.DrawAsSeed != SeedType.None ? orig(def.DrawAsSeed) : def.Nocturnal;
        }

        [HookTo(typeof(Plant), "GetSleepingReanimOffset")]
        private static float[] PlantGetSleepingReanimOffsetHook(
            Func<Plant, float[]> orig, Plant self)
        {
            CustomPlantDef def = Borrow(self);
            if (def == null || def.DrawAsSeed == SeedType.None)
            {
                return orig(self);
            }
            SeedType original = self.mSeedType;
            self.mSeedType = def.DrawAsSeed;
            try
            {
                return orig(self);
            }
            finally
            {
                self.mSeedType = original;
            }
        }

        [HookTo(typeof(Plant), "DrawShadow", typeof(Graphics), typeof(float), typeof(float))]
        private static void PlantDrawShadowHook(Action<Plant, Graphics, float, float> orig,
            Plant self, Graphics g, float offsetX, float offsetY)
        {
            CustomPlantDef def = Borrow(self);
            if (def == null || def.DrawAsSeed == SeedType.None)
            {
                orig(self, g, offsetX, offsetY);
                return;
            }
            // 影子的缩放/偏移在引擎里是按种子写死的分支（大喷菇 0.9 倍、行 -4）。
            SeedType original = self.mSeedType;
            self.mSeedType = def.DrawAsSeed;
            try
            {
                orig(self, g, offsetX, offsetY);
            }
            finally
            {
                self.mSeedType = original;
            }
        }

        [HookTo(typeof(Plant), "UpdateShooting")]
        private static void PlantUpdateShootingHook(Action<Plant> orig, Plant self)
        {
            CustomPlantDef def = Borrow(self);
            if (def == null || def.DrawAsSeed == SeedType.None)
            {
                orig(self);
                return;
            }
            // 原生大喷菇那团 Fumecloud 就写在这个方法的种子分支里（计数减到 15 那一帧挂上），
            // 借身份跑一遍原方法比抄它的坐标和渲染层级干净。
            SeedType original = self.mSeedType;
            self.mSeedType = def.DrawAsSeed;
            try
            {
                orig(self);
            }
            finally
            {
                self.mSeedType = original;
            }
        }

        [HookTo(typeof(Plant), "FindTargetAndFire", typeof(int), typeof(PlantWeapon),
            typeof(LaunchTime))]
        private static bool PlantFindTargetAndFireHook(
            Func<Plant, int, PlantWeapon, LaunchTime, bool> orig, Plant self, int row,
            PlantWeapon weapon, LaunchTime launchTime)
        {
            bool fired = orig(self, row, weapon, launchTime);
            if (!fired)
            {
                return false;
            }
            CustomPlantDef def = Borrow(self);
            if (def == null)
            {
                return true;
            }
            if (def.ShootingCounter > 0)
            {
                // 定格帧数是按种子写死的分支，自定义种子只会落到兜底值（借大喷菇动画时 29）。
                self.mShootingCounter = def.ShootingCounter;
            }
            if (def.ShootingAnimRate > 0f)
            {
                // 同一条兜底分支还把速率写死成 35，原生忧郁菇是 14：四连吐打到第 126 帧，
                // 35 的话射击动画先定住、后面三发是"定格画面在开枪"。
                Reanimation body = self.mApp.ReanimationTryToGet(self.mBodyReanimID);
                if (body != null)
                {
                    body.mAnimRate = def.ShootingAnimRate;
                }
            }
            return true;
        }

        [HookTo(typeof(Plant), "UpdateReanimColor")]
        private static void PlantUpdateReanimColorHook(Action<Plant> orig, Plant self)
        {
            orig(self);
            CustomPlantDef def = CustomPlantRegistry.Get(self)?.Definition;
            if (!TryTint(def?.BodyTint, out SexyColor tint))
            {
                return;
            }
            // 引擎那段的顺序是"升级卡悬停闪烁"排在染色之前，补的时候也得让它赢，
            // 否则手里举着升级卡时场上那株不再一闪一闪。
            Board board = self.mBoard;
            SeedType cursor = board == null ? SeedType.None : board.GetSeedTypeInCursor();
            if (cursor != SeedType.None && self.IsPartOfUpgradableTo(cursor) &&
                board.CanPlantAt(self.mPlantCol, self.mRow, cursor) == PlantingReason.Ok &&
                !self.mGloveGrabbed)
            {
                return;
            }
            Reanimation body = self.mApp.ReanimationTryToGet(self.mBodyReanimID);
            if (body != null && body.mColorOverride != tint)
            {
                body.mColorOverride = tint;
            }
        }

        /// <summary>
        /// 本株开火喷出的粒子（大喷菇的 Fumecloud、忧郁菇的 Gloomcloud）跟着染色，
        /// 用的是引擎自己的 <c>TodParticleSystem.OverrideColor</c>（催眠菇那套同一入口）。
        /// 每帧重贴而不是拦 <c>AddAttachedParticle</c>：那个方法太小，被 JIT 内联进了
        /// UpdateShooting，钩子只拦得住外部调用（实机量过：直接调会染色，植物自己喷就不染）。
        /// </summary>
        private static void TintOwnParticle(Plant self, CustomPlantDef def)
        {
            TodParticleSystem system = self.mParticleID;
            if (system == null || system.mDead || system.mEmitterList.Count == 0 ||
                !TryTint(def?.ParticleTint, out SexyColor tint))
            {
                return;
            }
            if (system.mEmitterList[0].mColorOverride != tint)
            {
                // OverrideColor 自己遍历全部发射器；先比一个，免得每帧重复写
                system.OverrideColor(null, tint);
            }
        }

        /// <summary>解析一个染色声明；没填或写错都回 false（写错的由定义自检点名）。</summary>
        private static bool TryTint(string hex, out SexyColor color)
        {
            color = SexyColor.White;
            return !string.IsNullOrEmpty(hex) && CustomPlantDef.TryParseTint(hex, out color);
        }

        [HookTo(typeof(Plant), "IsUpgradableTo", typeof(SeedType))]
        private static bool PlantIsUpgradableToHook(Func<Plant, SeedType, bool> orig,
            Plant self, SeedType updatedType)
        {
            if (orig(self, updatedType))
            {
                return true;
            }
            CustomPlantDef def = Borrow(self);
            if (def == null)
            {
                return false;
            }
            // 升级卡是模组植物时只能按名字配对：两边的 SeedId 都在首次排版时才分配，
            // 脚本执行期互相拿不到对方的种子号。
            CustomPlantDef card = CustomPlantRegistry.GetDef(updatedType);
            return CustomPlantRegistry.UpgradesInto(def, updatedType, card?.Name);
        }

        /// <summary>
        /// 卡槽里"这张升级卡现在点不点得起来、灰不灰"。原生紫卡那层"场上没有对应低级株就不给拿"
        /// 是 <c>Board.PlantingRequirementsMet</c> 里按种子写死的（忧郁菇数大喷菇、机枪豌豆数双发…），
        /// 模组种子不在那张表里 —— 所以模组的紫卡以前什么时候都点得起来；反过来场上只有一株
        /// 模组大喷菇时，原生忧郁菇那张表也数不到它，卡会一直灰着。两个方向一起补。
        /// 注意别拿 <c>orig</c> 的答复当"引擎放行"：那张表对不认识的种子一律回 true，
        /// 所以模组紫卡必须自己判（唯一的豁免是创造关卡勾了"简单升级"）。
        /// </summary>
        [HookTo(typeof(Board), "PlantingRequirementsMet", typeof(SeedType))]
        private static bool BoardPlantingRequirementsMetHook(
            Func<Board, SeedType, bool> orig, Board self, SeedType seed)
        {
            CustomPlantDef card = CustomPlantRegistry.GetDef(seed);
            if (card == null)
            {
                return orig(self, seed) || HasUpgradeTarget(self, seed);
            }
            if (!card.IsUpgradeCard)
            {
                return true;
            }
            return EasyUpgrade(self.mApp) || HasUpgradeTarget(self, seed);
        }

        /// <summary>引擎那句"创造关卡勾了简单升级，就不卡前置株"的豁免（Board.cs 11417）。</summary>
        private static bool EasyUpgrade(LawnApp app)
        {
            if (app == null || !app.IsLevelUseJson() || app.mCreativeLevel == null)
            {
                return false;
            }
            Lawn.Creative.CSLevelProperty property =
                app.mCreativeLevel.GetComponent<Lawn.Creative.CSLevelProperty>();
            return property != null && property.mEasyUpgrade;
        }

        /// <summary>场上有没有一株"能被这张卡升级"的（已经死了/正在被铲的不算）。</summary>
        private static bool HasUpgradeTarget(Board board, SeedType card)
        {
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (plant != null && !plant.mDead && plant.IsUpgradableTo(card))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 升级时"醒着还是睡着"的搬运。引擎只在 <c>seedTypeInCursor == SeedType.Gloomshroom</c>
        /// 那一条分支里做（Board.cs 6262 判定、6397 落给新株）：大喷菇醒着 → 忧郁菇直接醒着；
        /// 大喷菇睡着但已被咖啡豆叫到过 → 把 mWakeUpCounter 的进度带过去。模组升级卡进不了
        /// 那个 ==，所以按同一语义补在这里 —— 老株此刻已被 Die() 但还挂在 mPlants 上，查得到。
        /// </summary>
        [HookTo(typeof(Board), "AddPlant", typeof(int), typeof(int), typeof(SeedType), typeof(SeedType))]
        private static Plant BoardAddPlantHook(
            Func<Board, int, int, SeedType, SeedType, Plant> orig,
            Board self, int gridX, int gridY, SeedType seed, SeedType imitater)
        {
            CustomPlantDef card = CustomPlantRegistry.GetDef(
                CustomPlantRegistry.EffectiveCustomSeed(seed, imitater));
            if (card == null || !card.IsUpgradeCard)
            {
                return orig(self, gridX, gridY, seed, imitater);
            }
            Plant old = FindUpgradeSource(self, gridX, gridY, card.Seed);
            Plant plant = orig(self, gridX, gridY, seed, imitater);
            if (old != null && plant != null)
            {
                if (!old.mIsAsleep)
                {
                    plant.SetSleeping(theIsAsleep: false);
                }
                else
                {
                    plant.mWakeUpCounter = old.mWakeUpCounter;
                }
            }
            return plant;
        }

        /// <summary>这一格里声明"能被这张升级卡覆盖"的那株（含刚被 Die()、还没进删除队列的）。</summary>
        private static Plant FindUpgradeSource(Board board, int gridX, int gridY, SeedType card)
        {
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (plant != null && plant.mPlantCol == gridX && plant.mRow == gridY &&
                    plant.IsUpgradableTo(card))
                {
                    return plant;
                }
            }
            return null;
        }

        [HookTo(typeof(Board), "IsFungus", typeof(SeedType))]
        private static bool BoardIsFungusHook(Func<Board, SeedType, bool> orig, Board self,
            SeedType seed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
            return def != null ? def.IsFungus : orig(self, seed);
        }

        [HookTo(typeof(PlantVoice), "Play", typeof(SeedType), typeof(SeedType), typeof(PlantVoice.VoiceType))]
        private static void PlantVoicePlayHook(
            Action<SeedType, SeedType, PlantVoice.VoiceType> orig,
            SeedType seed, SeedType imitater, PlantVoice.VoiceType purpose)
        {
            // 引擎的语音表只有 65 格（PlantVoice.cachedSoundEffects = uint?[65,12]），
            // 自定义种子拿去做下标会直接越界，所以以前一律丢弃。
            // 现在按声明借某个原生种子的语音槽；没声明的还是不出声。
            SeedType custom = CustomPlantRegistry.EffectiveCustomSeed(seed, imitater);
            if (custom != SeedType.None)
            {
                CustomPlantDef def = CustomPlantRegistry.GetDef(custom);
                if (def == null || def.VoiceSeed == SeedType.None)
                {
                    return;
                }
                orig(def.VoiceSeed, SeedType.None, purpose);
                return;
            }
            orig(seed, imitater, purpose);
        }

        // ------------------------------------------------------------ 种植

        [HookTo(typeof(Board), "NewPlant", typeof(int), typeof(int), typeof(SeedType), typeof(SeedType))]
        private static Plant BoardNewPlantHook(
            Func<Board, int, int, SeedType, SeedType, Plant> orig,
            Board self, int gridX, int gridY, SeedType seedType, SeedType imitater)
        {
            if (CustomPlantRegistry.IsCustomSeed(seedType) ||
                CustomPlantRegistry.IsCustomSeed(imitater))
            {
                CustomPlantRegistry.EnsureDefinitions();
            }
            return orig(self, gridX, gridY, seedType, imitater);
        }

        [HookTo(typeof(Board), "GetCurrentPlantCost", typeof(SeedType), typeof(SeedType))]
        private static int BoardGetCurrentPlantCostHook(
            Func<Board, SeedType, SeedType, int> orig, Board self,
            SeedType seed, SeedType imitater)
        {
            int cost = CustomUi.CustomCost(seed, imitater, self);
            if (cost >= 0)
            {
                return cost;
            }
            if ((int)seed >= 65 || (int)imitater >= 65)
            {
                return 0;
            }
            return orig(self, seed, imitater);
        }

        [HookTo(typeof(Board), "CanPlantAt", typeof(int), typeof(int), typeof(SeedType), typeof(bool))]
        private static PlantingReason BoardCanPlantAtHook(
            Func<Board, int, int, SeedType, bool, PlantingReason> orig,
            Board self, int gridX, int gridY, SeedType seed, bool isMove)
        {
            if (CustomPlantRegistry.IsCustomSeed(seed))
            {
                if (!PassesPlantingFilter(self, gridX, gridY, seed))
                {
                    return PlantingReason.NotHere;
                }
                CustomPlantDef card = CustomPlantRegistry.GetDef(seed);
                if (card != null && card.IsUpgradeCard && !isMove)
                {
                    // 下面会把自定义种子换成"豌豆能不能种这格"去代问，而豌豆当然不能被
                    // 模组升级卡升级 —— 那样升级卡永远点不动。所以这里先按引擎自己的判据
                    // （那一格的普通植物 IsUpgradableTo + 不在被雨伞抓走的状态）放行。
                    Plant target = self.GetTopPlantAt(gridX, gridY, TopPlant.OnlyNormalPosition);
                    if (target != null && !target.mDead && target.IsUpgradableTo(seed) &&
                        target.mOnBungeeState != PlantOnBungeeState.GettingGrabbedByBungee)
                    {
                        return PlantingReason.Ok;
                    }
                    return PlantingReason.NeedsUpgrade;
                }
                return orig(self, gridX, gridY, SeedType.Peashooter, isMove);
            }
            return orig(self, gridX, gridY, seed, isMove);
        }

        [HookTo(typeof(Challenge), "CanPlantAt", typeof(int), typeof(int), typeof(SeedType))]
        private static PlantingReason ChallengeCanPlantAtHook(
            Func<Challenge, int, int, SeedType, PlantingReason> orig,
            Challenge self, int gridX, int gridY, SeedType seed)
        {
            if (CustomPlantRegistry.IsCustomSeed(seed))
            {
                if (!PassesPlantingFilter(self.mBoard, gridX, gridY, seed))
                {
                    return PlantingReason.NotHere;
                }
                return orig(self, gridX, gridY, SeedType.Peashooter);
            }
            return orig(self, gridX, gridY, seed);
        }

        /// <summary>DEF.CanPlantAt 过滤器：抛异常按通过处理（再交给豌豆替身判定兜底）。</summary>
        private static bool PassesPlantingFilter(Board board, int gridX, int gridY, SeedType seed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
            if (def == null || def.CanPlantAt == null)
            {
                return true;
            }
            try
            {
                return def.CanPlantAt(board, gridX, gridY);
            }
            catch (Exception ex)
            {
                ModLog.Error("CanPlantAt filter failed for " + def.Name, ex);
                return true;
            }
        }

        // ------------------------------------------------------------ 选卡/图鉴

        [HookTo(typeof(LawnApp), "HasSeedType", typeof(SeedType))]
        private static bool LawnAppHasSeedTypeHook(Func<LawnApp, SeedType, bool> orig,
            LawnApp self, SeedType seed)
        {
            return CustomPlantRegistry.IsSelectableCustomSeed(seed) || orig(self, seed);
        }

        [HookTo(typeof(LawnApp), "ShowSeedChooserScreen")]
        private static void LawnAppShowChooserHook(Action<LawnApp> orig, LawnApp self)
        {
            CustomPlantRegistry.EnsureDefinitions(validate: true);
            List<CustomChooserUi.HiddenPacket> hidden =
                CustomChooserUi.HideCustomSurvivalPackets(self.mBoard);
            try
            {
                orig(self);
            }
            finally
            {
                CustomChooserUi.RestoreCustomSurvivalPackets(hidden);
            }
            CustomChooserUi.RestoreSurvivalChooserSelection(self.mSeedChooserScreen, hidden);
        }

        [HookTo(typeof(SeedChooserScreen), "SeedNotAllowedToPick", typeof(SeedType))]
        private static bool ChooserSeedNotAllowedHook(
            Func<SeedChooserScreen, SeedType, bool> orig, SeedChooserScreen self, SeedType seed)
        {
            return !CustomPlantRegistry.IsSelectableCustomSeed(seed) && orig(self, seed);
        }

        [HookTo(typeof(SeedChooserScreen), "OnStartButton")]
        private static void ChooserStartButtonHook(Action<SeedChooserScreen> orig,
            SeedChooserScreen self)
        {
            if (CustomChooserUi.BlockStartForCustomUpgrades(self))
            {
                return;
            }
            orig(self);
        }

        [HookTo(typeof(SeedChooserScreen), "SeedSelected", typeof(SeedType))]
        private static void ChooserSeedSelectedHook(
            Action<SeedChooserScreen, SeedType> orig, SeedChooserScreen self, SeedType seed)
        {
            if (CustomPlantRegistry.IsCustomSeed(seed))
            {
                CustomChooserUi.OnSeedSelected(self, seed);
                return;
            }
            orig(self, seed);
        }

        [HookTo(typeof(SeedChooserScreen), "SaveToSeedGroup", typeof(CardGroup))]
        private static bool ChooserSaveGroupHook(
            Func<SeedChooserScreen, CardGroup, bool> orig, SeedChooserScreen self, CardGroup group)
        {
            return orig(self, group);
        }

        [HookTo(typeof(SeedChooserScreen), "LoadSeedGroup", typeof(CardGroup), typeof(int))]
        private static bool ChooserLoadGroupHook(
            Func<SeedChooserScreen, CardGroup, int, bool> orig,
            SeedChooserScreen self, CardGroup group, int index)
        {
            return orig(self, group, index);
        }

        [HookTo(typeof(SeedChooserScreen), "FindSeedInBank", typeof(int))]
        private static SeedType ChooserFindSeedHook(
            Func<SeedChooserScreen, int, SeedType> orig, SeedChooserScreen self, int index)
        {
            SeedType result = orig(self, index);
            if (result != SeedType.None)
            {
                return result;
            }
            return CustomChooserUi.FindSeedInBank(self, index);
        }

        /// <summary>
        /// 飞行中的卡必须挂在 DrawOverlay 上：SeedChooserScreen.Draw 里调的是 DeferOverlay，
        /// 卡片列表由控件管理器在 Draw 之后才画，挂在 Draw 上会把飞出的卡垫到列表下面。
        /// </summary>
        [HookTo(typeof(SeedChooserScreen), "DrawOverlay", typeof(Graphics))]
        private static void ChooserOverlayHook(
            Action<SeedChooserScreen, Graphics> orig, SeedChooserScreen self, Graphics g)
        {
            orig(self, g);
            Hot("SeedChooserScreen.DrawOverlay", () => CustomChooserUi.DrawChooser(self, g));
        }

        [HookTo(typeof(SeedChooserScreen), "MouseDown", typeof(int), typeof(int), typeof(int))]
        private static void ChooserMouseDownHook(
            Action<SeedChooserScreen, int, int, int> orig,
            SeedChooserScreen self, int x, int y, int clickCount)
        {
            if (CustomChooserUi.HandleChooserMouseDown(self, x, y))
            {
                return;
            }
            orig(self, x, y, clickCount);
        }

        [HookTo(typeof(SeedChooserScreen), "Update")]
        private static void ChooserUpdateHook(Action<SeedChooserScreen> orig, SeedChooserScreen self)
        {
            CustomPlantRegistry.EnsureDefinitions();
            CustomChooserUi.EnsureEntries(self);
            orig(self);
            CustomChooserUi.UpdateChooser(self);
        }

        [HookTo(typeof(SeedChooserScreen), "CloseSeedChooser")]
        private static void ChooserCloseHook(Action<SeedChooserScreen> orig, SeedChooserScreen self)
        {
            try
            {
                CustomChooserUi.CloseChooser(self, orig);
            }
            catch (Exception ex)
            {
                ModLog.Error("CloseSeedChooser failed", ex);
            }
        }

        [HookTo(typeof(SeedPacketsWidget), "Draw", typeof(Graphics))]
        private static void SeedPacketsWidgetDrawHook(
            Action<SeedPacketsWidget, Graphics> orig, SeedPacketsWidget self, Graphics g)
        {
            CustomChooserUi.EnsureImitaterWidget(self);
            CustomChooserUi.EnsureEntries(self.mApp.mSeedChooserScreen);
            orig(self, g);
            Hot("SeedPacketsWidget.Draw", () => CustomChooserUi.DrawWidget(self, g));
        }

        [HookTo(typeof(SeedPacketsWidget), "MouseUp", typeof(int), typeof(int), typeof(int))]
        private static void SeedPacketsWidgetMouseUpHook(
            Action<SeedPacketsWidget, int, int, int> orig,
            SeedPacketsWidget self, int x, int y, int clickCount)
        {
            if (CustomChooserUi.HandleWidgetMouseUp(self, x, y))
            {
                return;
            }
            orig(self, x, y, clickCount);
        }

        [HookTo(typeof(SeedPacket), "DrawSmallSeedPacket", typeof(Graphics), typeof(float), typeof(float), typeof(SeedType), typeof(SeedType), typeof(float), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool), typeof(bool))]
        private static void SeedPacketDrawHook(
            Action<Graphics, float, float, SeedType, SeedType, float, int,
                bool, bool, bool, bool, bool> orig,
            Graphics g, float x, float y, SeedType seed, SeedType imitater,
            float percentDark, int grayness, bool drawCost, bool useCurrentCost,
            bool drawBackground, bool drawCostBackground, bool canShowName)
        {
            CustomUi.NativeDrawSmallSeedPacket ??= orig;
            if (CustomUi.IsCustomPacket(seed, imitater))
            {
                Hot("SeedPacket.DrawSmallSeedPacket", () => CustomUi.DrawSmallSeedPacket(
                    g, x, y, seed, imitater, percentDark, grayness, drawCost,
                    useCurrentCost, drawBackground, drawCostBackground, canShowName));
                return;
            }
            orig(g, x, y, seed, imitater, percentDark, grayness, drawCost,
                useCurrentCost, drawBackground, drawCostBackground, canShowName);
        }

        [HookTo(typeof(SeedPacket), "SetPacketType", typeof(SeedType), typeof(SeedType))]
        private static void SeedPacketSetTypeHook(
            Action<SeedPacket, SeedType, SeedType> orig,
            SeedPacket self, SeedType seed, SeedType imitater)
        {
            orig(self, seed, imitater);
            CustomUi.OnSetPacketType(self, seed, imitater);
        }

        [HookTo(typeof(SeedPacket), "DrawBackground", typeof(Graphics))]
        private static void SeedPacketDrawBackgroundHook(
            Action<SeedPacket, Graphics> orig, SeedPacket self, Graphics g)
        {
            if (CustomUi.IsCustomPacket(self.mPacketType, self.mImitaterType))
            {
                Hot("SeedPacket.DrawBackground", () => CustomUi.DrawPacketBackground(self, g));
                return;
            }
            orig(self, g);
        }

        [HookTo(typeof(SeedPacket), "DrawOverlay", typeof(Graphics))]
        private static void SeedPacketDrawOverlayHook(
            Action<SeedPacket, Graphics> orig, SeedPacket self, Graphics g)
        {
            if (CustomUi.IsCustomPacket(self.mPacketType, self.mImitaterType))
            {
                Hot("SeedPacket.DrawOverlay", () => CustomUi.DrawPacketOverlay(self, g));
                return;
            }
            orig(self, g);
        }

        [HookTo(typeof(PlantGalleryWidget), "Draw", typeof(Graphics))]
        private static void GalleryDrawHook(
            Action<PlantGalleryWidget, Graphics> orig, PlantGalleryWidget self, Graphics g)
        {
            CustomPlantRegistry.EnsureDefinitions();
            Hot("PlantGalleryWidget.Prepare", () => CustomChooserUi.PrepareGallery(self));
            orig(self, g);
            Hot("PlantGalleryWidget.Draw", () => CustomChooserUi.DrawGalleryEntries(self, g));
        }

        [HookTo(typeof(PlantGalleryWidget), "MouseUp", typeof(int), typeof(int), typeof(int))]
        private static void GalleryMouseUpHook(
            Action<PlantGalleryWidget, int, int, int> orig,
            PlantGalleryWidget self, int x, int y, int clickCount)
        {
            CustomPlantRegistry.EnsureDefinitions();
            if (CustomChooserUi.HandleGalleryMouseUp(self, x, y))
            {
                return;
            }
            orig(self, x, y, clickCount);
        }

        [HookTo(typeof(SeedTypeLegacy), "FromInt", typeof(int))]
        private static SeedType SeedTypeLegacyFromIntHook(Func<int, SeedType> orig, int value)
        {
            SeedType custom = CustomUi.LegacySeedFromInt(value);
            return custom == SeedType.None ? orig(value) : custom;
        }


        // ------------------------------------------------------------ 存档

        private static bool LawnSaveGameHook(Func<Board, string, bool> orig,
            Board self, string filePath)
        {
            return CustomSave.Save(orig, self, filePath);
        }

        private static bool LawnLoadGameHook(Func<Board, string, bool> orig,
            Board self, string filePath)
        {
            return CustomSave.Load(orig, self, filePath);
        }

        [HookTo(typeof(SeedBank), "SaveToFile", typeof(Sexy.Buffer))]
        private static bool SeedBankSaveHook(Func<SeedBank, Sexy.Buffer, bool> orig,
            SeedBank self, Sexy.Buffer buffer)
        {
            SeedPacket[] original = self.mSeedPackets;
            if (original == null || original.Length <= 9)
            {
                return orig(self, buffer);
            }
            SeedPacket[] legacy = new SeedPacket[9];
            Array.Copy(original, legacy, 9);
            self.mSeedPackets = legacy;
            try
            {
                return orig(self, buffer);
            }
            catch (Exception ex)
            {
                ModLog.Error("SeedBank.SaveToFile failed", ex);
                throw;
            }
            finally
            {
                self.mSeedPackets = original;
            }
        }

        [HookTo(typeof(SeedBank), "LoadFromFile", typeof(Sexy.Buffer))]
        private static bool SeedBankLoadHook(Func<SeedBank, Sexy.Buffer, bool> orig,
            SeedBank self, Sexy.Buffer buffer)
        {
            bool result;
            try
            {
                result = orig(self, buffer);
            }
            catch (Exception ex)
            {
                ModLog.Error("SeedBank.LoadFromFile failed", ex);
                throw;
            }
            if (!result)
            {
                return result;
            }
            try
            {
                int position = (int)buffer.mData.Position;
                byte[] data = buffer.mData.ToArray();
                if (position + 81 <= data.Length && data[position] == 1 &&
                    (data[position + 9] == 0 || data[position + 9] == 1) &&
                    (data[position + 26] == 0 || data[position + 26] == 1) &&
                    BitConverter.ToInt32(data, position + 5) ==
                    Sexy.Constants.SMALL_SEEDPACKET_HEIGHT &&
                    BitConverter.ToInt32(data, position + 27) ==
                    Sexy.Constants.SMALL_SEEDPACKET_WIDTH &&
                    BitConverter.ToInt32(data, position + 44) == 9)
                {
                    buffer.mData.Position = position + 81;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("SeedBank LoadFromFile compatibility probe failed", ex);
            }
            return result;
        }

        // ------------------------------------------------------------ 弹道/僵尸

        [HookTo(typeof(Projectile), "PeaAboutToHitTorchwood")]
        private static bool PeaTorchwoodHook(Func<Projectile, bool> orig, Projectile self)
        {
            try
            {
                return CustomProjectiles.PeaAboutToHitTorchwood(self, () => orig(self));
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.PeaAboutToHitTorchwood"))
                {
                    ModLog.Error("hot path failed: Projectile.PeaAboutToHitTorchwood", ex);
                }
                return orig(self);
            }
        }

        [HookTo(typeof(Projectile), "DoImpact", typeof(Zombie))]
        private static void ProjectileImpactHook(
            Action<Projectile, Zombie> orig, Projectile self, Zombie zombie)
        {
            try
            {
                CustomProjectiles.OnProjectileImpact(orig, self, zombie);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.DoImpact"))
                {
                    ModLog.Error("hot path failed: Projectile.DoImpact", ex);
                }
            }
        }

        [HookTo(typeof(Projectile), "CheckForCollision")]
        private static void ProjectileCollisionHook(Action<Projectile> orig, Projectile self)
        {
            try
            {
                CustomProjectiles.OnProjectileCheckForCollision(orig, self);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.CheckForCollision"))
                {
                    ModLog.Error("hot path failed: Projectile.CheckForCollision", ex);
                }
                orig(self);
            }
        }

        [HookTo(typeof(Projectile), "CheckForHighGround")]
        private static void ProjectileHighGroundHook(Action<Projectile> orig, Projectile self)
        {
            try
            {
                CustomProjectiles.CheckForHighGround(self, orig);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.CheckForHighGround"))
                {
                    ModLog.Error("hot path failed: Projectile.CheckForHighGround", ex);
                }
                orig(self);
            }
        }

        /// <summary>
        /// 弹体回池时把框架标签清掉：引擎复用同一个 Projectile 对象，
        /// 上一颗注册弹的定义（伤害/穿透/染色/MaxAge）不能跟到下一发原生弹身上。
        /// </summary>
        [HookTo(typeof(Projectile), "Reset")]
        private static void ProjectileResetHook(Action<Projectile> orig, Projectile self)
        {
            CustomProjectiles.Discard(self);
            orig(self);
        }

        [HookTo(typeof(Projectile), "Update")]
        private static void ProjectileUpdateHook(Action<Projectile> orig, Projectile self)
        {
            try
            {
                CustomProjectiles.OnProjectileUpdate(orig, self);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.Update"))
                {
                    ModLog.Error("hot path failed: Projectile.Update", ex);
                }
                orig(self);
            }
        }

        [HookTo(typeof(Projectile), "Draw", typeof(Graphics))]
        private static void ProjectileDrawHook(
            Action<Projectile, Graphics> orig, Projectile self, Graphics g)
        {
            try
            {
                CustomProjectiles.OnProjectileDraw(orig, self, g);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Projectile.Draw"))
                {
                    ModLog.Error("hot path failed: Projectile.Draw", ex);
                }
                orig(self, g);
            }
        }

        // 毒的节拍挂在棋盘这一侧，不是每只僵尸的 Update 上。两个原因：
        // ① 只有账本非空才有开销，比每帧给场上每只僵尸套一层钩子+查表便宜得多；
        // ② 死掉/被移走的僵尸不再被引擎 Update，挂在它身上的钩子收不到最后一账，
        //    整表扫才收得到（实测挂在 Zombie.Update 上的那版一次都没步进过，毒一刀都没落）。
        [HookTo(typeof(Board), "UpdateGameObjects")]
        private static void BoardUpdateGameObjectsHook(Action<Board> orig, Board self)
        {
            CustomCombat.PreTickPoison(self);
            orig(self);
            try
            {
                CustomCombat.TickPoisonBoard(self);
            }
            catch (Exception ex)
            {
                // 引擎那一帧已经跑完了，这里炸不能把更新流打断；但也不能静默——
                // 静默的结果就是"毒挂着却一刀不落"，跟没有毒一样。
                if (HotErrors.Add("Board.UpdateGameObjects poison"))
                {
                    ModLog.Error("poison driver failed", ex);
                }
            }
            try
            {
                CustomBurn.TickBurnBoard(self);
            }
            catch (Exception ex)
            {
                if (HotErrors.Add("Board.UpdateGameObjects burn"))
                {
                    ModLog.Error("burn driver failed", ex);
                }
            }
        }

        // 引擎在清理死亡僵尸时把它 PrepareForReuse 塞回静态栈，之后 GetNewZombie 原样取出来复用。
        // 毒是按对象引用记在框架这边的账，不在进池这一刻摘掉，下一只僵尸会带着上一层的毒出场
        // （而且它不挨打也会掉血、变慢、发紫）。灼热那份账同理。
        [HookTo(typeof(Zombie), "PrepareForReuse")]
        private static void ZombiePrepareForReuseHook(Action<Zombie> orig, Zombie self)
        {
            CustomCombat.ClearPoison(self);
            CustomBurn.ClearBurn(self);
            orig(self);
        }

        [HookTo(typeof(Zombie), "Draw", typeof(Graphics))]
        private static void ZombieDrawHook(Action<Zombie, Graphics> orig, Zombie self, Graphics g)
        {
            bool burned = CustomBurn.HasVisibleBurn(self);
            if (!burned && !CustomCombat.HasVisiblePoison(self))
            {
                orig(self, g);
                return;
            }
            bool oldColorize = g.mColorizeImages;
            Microsoft.Xna.Framework.Color oldColor = g.mColor;
            g.SetColorizeImages(true);
            // 烧着的用灼热那档颜色；只有毒的仍走原来那层紫。
            g.SetColor(burned
                ? CustomBurn.BurnTintOf(self).Color
                : new SexyColor(170, 70, 210, 255).Color);
            try
            {
                orig(self, g);
            }
            finally
            {
                g.SetColor(oldColor);
                g.SetColorizeImages(oldColorize);
            }
        }

        [HookTo(typeof(Zombie), "DrawBossPart", typeof(Graphics), typeof(BossPart))]
        private static void ZombieDrawBossPartHook(
            Action<Zombie, Graphics, BossPart> orig, Zombie self, Graphics g, BossPart part)
        {
            bool burned = CustomBurn.HasVisibleBurn(self);
            if (!burned && !CustomCombat.HasVisiblePoison(self))
            {
                orig(self, g, part);
                return;
            }
            bool oldColorize = g.mColorizeImages;
            Microsoft.Xna.Framework.Color oldColor = g.mColor;
            g.SetColorizeImages(true);
            g.SetColor(burned
                ? CustomBurn.BurnTintOf(self).Color
                : new SexyColor(170, 70, 210, 255).Color);
            try
            {
                orig(self, g, part);
            }
            finally
            {
                g.SetColor(oldColor);
                g.SetColorizeImages(oldColorize);
            }
        }

        // ------------------------------------------------------------ 棋盘

        [HookTo(typeof(Board), "UpdateGame")]
        private static void BoardUpdateGameHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("Board.UpdateGame", () =>
            {
                CustomProjectiles.Prune(self);
                CustomEvents.OnBoardUpdate(self);
            });
        }

        /// <summary>僵尸开始啃植物：按目标派发 OnEatenStart（自定义植物才收）。</summary>
        [HookTo(typeof(Zombie), "StartEating")]
        private static void ZombieStartEatingHook(Action<Zombie> orig, Zombie self)
        {
            orig(self);
            if (self != null && self.mTargetPlantID != null)
            {
                CustomPlantRegistry.DispatchEatenStart(self.mTargetPlantID, self);
            }
        }

        /// <summary>
        /// 僵尸死亡（DieWithLoot 内部也走 DieNoLoot，只挂这一个不会重复计）。
        /// 先广播同行的 OnZombieDied，再触发 PlantKit.ZombieDied 全局事件。
        /// </summary>
        [HookTo(typeof(Zombie), "DieNoLoot", typeof(bool))]
        private static void ZombieDieNoLootHook(Action<Zombie, bool> orig, Zombie self,
            bool giveAchievements)
        {
            orig(self, giveAchievements);
            Hot("Zombie.DieNoLoot.events", () => CustomEvents.OnZombieDied(self));
        }

        [HookTo(typeof(CursorObject), "DrawTopLayer", typeof(Graphics))]
        private static void CursorTopLayerHook(
            Action<CursorObject, Graphics> orig, CursorObject self, Graphics g)
        {
            // 原生这一层会用"光标上的种子号"去查 ReanimatorCache 的植物图，
            // 94+ 的模组种子下标越界会闪退，所以自定义种子改由我们直接画植物本体。
            // 只有从卡槽/可用卡拿起的才画预览；手套拖着的是场上真植物，原生这里也不画。
            CursorType cursorType = self.mCursorType;
            bool fromCard = cursorType == CursorType.PlantFromBank ||
                            cursorType == CursorType.PlantFromUsableCoin;
            if (fromCard && CustomUi.IsCustomPacket(self.mType, self.mImitaterType))
            {
                Hot("CursorObject.DrawTopLayer", () => CustomUi.DrawCursorTopLayer(self, g));
                return;
            }
            orig(self, g);
        }

        [HookTo(typeof(CursorPreview), "Draw", typeof(Graphics))]
        private static void CursorPreviewHook(
            Action<CursorPreview, Graphics> orig, CursorPreview self, Graphics g)
        {
            // 原生会用 Plant.DrawSeedType 画预览，而那个入口我们已经按植物分流
            // （借原生身体的走原生图，自定义动画的走自己的 reanim），这里直接交给原生。
            orig(self, g);
        }
    }
}
