using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 定义自检（植物定义 + 注册子弹）：把只能靠实机才看得见的配置错误在注册阶段就写进日志。
    /// 只报"几乎必然是笔误"的组合，不替作者判断设计意图；一律 Warn，不抛异常。
    /// </summary>
    internal static class DefChecks
    {
        private static readonly HashSet<string> Logged = new HashSet<string>(StringComparer.Ordinal);

        private static bool _autoRan;

        private static readonly string[] KnownCardBackgrounds = { "green", "purple", "gray" };

        /// <summary>Plant.UpdateShooting 里带 Fire 分支的种子：借它们的外形身份会顺带多开一枪。
        /// 显式写了 ShootingCounter 的不算误用 —— 那个计数就是这条分支的节拍，说明四发是故意的。</summary>
        /// <summary>
        /// 借这些原生身份会让引擎在一次开火里多打出子弹/多带一记副武器：忧郁菇和机枪豌豆
        /// 那一轮 4 发是数着 mShootingCounter 打的，三线/分裂/香蒲/仙人掌/末日菇/玉米加农炮
        /// 那几支在一次 Fire 里自己多带弹。
        /// 注意投手族（卷心菜/西瓜/冰西瓜）不在列：它们那一支只是自己找好目标再调
        /// <c>Fire(target, row, Primary)</c> 一次（Plant.cs 里 Cabbagepult/Melonpult/Wintermelon
        /// 共用那一段），并不多打 —— 之前把它们算进来，借西瓜投手身份的模组植物每条都吃一句
        /// 假告警。玉米投手留着，因为它那一支还会按 KernelpultButter 状态换副武器补一记黄油。
        /// </summary>
        private static readonly SeedType[] DoubleFiringSeeds =
        {
            SeedType.Gloomshroom, SeedType.Gatlingpea, SeedType.Cattail, SeedType.HypnoCattail,
            SeedType.Threepeater, SeedType.Splitpea, SeedType.Endoflame, SeedType.Cactus,
            SeedType.Kernelpult, SeedType.Cobcannon,
        };

        /// <summary>
        /// 排版（种子号、动画槽、卡图分配）真正完成之后由注册表自动跑一次。
        /// 放在注册阶段跑不得：那时 SeedId/ReanimSlot 还没分配，每株都会吃一条假告警。
        /// </summary>
        public static void RunOnceAfterLayout()
        {
            if (_autoRan)
            {
                return;
            }
            _autoRan = true;
            ModLog.Info("def self-check: " + RunAll().Count + " warning(s)");
        }

        /// <summary>逐条检查全部定义，返回告警文本（同一句只写一次日志）。</summary>
        public static List<string> RunAll()
        {
            List<string> warnings = new List<string>();
            IReadOnlyList<CustomPlantDef> defs = CustomPlantRegistry.Defs;
            Dictionary<string, int> nameCount = new Dictionary<string, int>(StringComparer.Ordinal);
            Dictionary<int, string> reanimOwner = new Dictionary<int, string>();
            foreach (CustomPlantDef def in defs)
            {
                Check(def, warnings, nameCount, reanimOwner);
            }
            foreach (CustomBulletDef bullet in CustomBullets.All)
            {
                CheckBullet(bullet, warnings);
            }
            foreach (CustomCombat.PoisonSpec poison in CustomCombat.PoisonSpecs)
            {
                CheckPoison(poison, warnings);
            }
            foreach (CustomBurn.BurnSpec burn in CustomBurn.BurnSpecs)
            {
                CheckBurn(burn, warnings);
            }
            foreach (string warning in warnings)
            {
                if (Logged.Add(warning))
                {
                    ModLog.Warn(warning);
                }
            }
            return warnings;
        }

        /// <summary>
        /// 两套 uint 掩码各只到自己那几个位。越界的位没人读，几乎一定是"把
        /// <c>DamageRangeFlags</c> 枚举的位号当掩码写"或者手抖多打了位。
        /// </summary>
        private static void CheckBullet(CustomBulletDef bullet, List<string> warnings)
        {
            if (bullet == null)
            {
                return;
            }
            if ((bullet.DamageFlags & ~0x3Fu) != 0)
            {
                warnings.Add("子弹 " + bullet.Id + ": DamageFlags=" + bullet.DamageFlags +
                    " 有 DamageFlags 枚举之外的位（只有 1/2/4/8/16/32 有人读）");
            }
            if ((bullet.DamageRangeFlags & ~0xFFu) != 0)
            {
                warnings.Add("子弹 " + bullet.Id + ": DamageRangeFlags=" +
                    bullet.DamageRangeFlags + " 超过 8 类目标的掩码（最大 255）；" +
                    "注意 DamageRangeFlags 枚举给的是位号，这里要写 1<<位号");
            }
            if (!string.IsNullOrEmpty(bullet.Image) && !AssetStore.Has(bullet.Image))
            {
                warnings.Add("子弹 " + bullet.Id + ": Image=\"" + bullet.Image +
                    "\" 没有在 AssetStore 里注册，弹体会画不出来（当前素材 " +
                    AssetStore.Count + " 张）");
            }
            CheckTint("子弹 " + bullet.Id, "Tint", bullet.Tint, warnings);
            if (!string.IsNullOrEmpty(bullet.Tint) &&
                CustomProjectiles.CarrierImageField(bullet.Carrier) == null)
            {
                warnings.Add("子弹 " + bullet.Id + ": Tint=\"" + bullet.Tint + "\" 配 Carrier=" +
                    bullet.Carrier + " 画不出来 —— 那个弹种的弹体不走静态图字段，换不了图");
            }
            if (bullet.Pierce > 0 && bullet.Damage <= 0)
            {
                warnings.Add("子弹 " + bullet.Id + ": Pierce=" + bullet.Pierce +
                    " 但 Damage=0 —— 留 0 是交给原生 DoImpact 结算，它会把弹带走，穿透不会生效");
            }
            if (bullet.Pierce < 0 || bullet.Speed < 0f || bullet.SplashWidth < 0 ||
                bullet.MaxAge < 0)
            {
                warnings.Add("子弹 " + bullet.Id + ": Pierce=" + bullet.Pierce +
                    " Speed=" + bullet.Speed + " SplashWidth=" + bullet.SplashWidth +
                    " MaxAge=" + bullet.MaxAge + " 里有负数");
            }
            if (bullet.Impact != null && (bullet.Damage > 0 || bullet.SplashDamage > 0 ||
                    bullet.Pierce > 0 || bullet.Poison != null || bullet.Burn != null))
            {
                warnings.Add("子弹 " + bullet.Id + ": 填了 Impact 又写了 Damage/Splash/Pierce/Poison/Burn —— " +
                    "命中整段交给 Impact，那几个声明一条都不会参与");
            }
            if (bullet.SkipCollision && bullet.Motion == null && bullet.Impact == null)
            {
                warnings.Add("子弹 " + bullet.Id + ": SkipCollision=true 但没有 Motion/Impact —— " +
                    "没人算落点，这颗弹会一路飞出屏幕也不打中任何东西");
            }
        }

        /// <summary>
        /// 一条毒的"会不会真的落刀"全在数值配合里：写错的毒不报错，只是安静地一刀都不掉，
        /// 所以这里把三种必然失效的组合点名（时长/间隔/第一刀时刻/伤害来源）。
        /// </summary>
        private static void CheckPoison(CustomCombat.PoisonSpec poison, List<string> warnings)
        {
            if (poison == null)
            {
                return;
            }
            string who = "毒 " + (string.IsNullOrEmpty(poison.Name)
                ? "0x" + Convert.ToString(System.Runtime.CompilerServices
                    .RuntimeHelpers.GetHashCode(poison), 16)
                : poison.Name);
            int first = poison.FirstTick > 0 ? poison.FirstTick : poison.Interval;
            if (poison.Ticks < 0)
            {
                warnings.Add(who + ": Ticks=" + poison.Ticks + " 是负数");
            }
            if (poison.Ticks > 0 && poison.Duration > 0)
            {
                warnings.Add(who + ": 同时写了 Ticks=" + poison.Ticks + " 和 Duration=" +
                    poison.Duration + " —— Ticks 会覆盖 Duration，那个时长不参与");
            }
            if (poison.Ticks <= 0 && poison.Duration <= 0)
            {
                warnings.Add(who + ": 既没写 Ticks 也没写 Duration" +
                    "，挂上去当帧就收账，一刀都不会落");
            }
            if (poison.Interval <= 0)
            {
                warnings.Add(who + ": Interval=" + poison.Interval +
                    "，会变成每帧一刀（NextTick 归零后立刻又能归零）");
            }
            else if (poison.Ticks <= 0 && poison.Duration > 0 && first > poison.Duration)
            {
                warnings.Add(who + ": 第一刀要等 " + first + " 帧，但整段毒 " +
                    poison.Duration + " 帧就到期 —— 一刀都不会落");
            }
            if (poison.FixedDamage <= 0 && poison.DamagePercent <= 0)
            {
                warnings.Add(who + ": 没写伤害（FixedDamage 和 DamagePercent 都是 0），" +
                    "每刀只有 1 点，多半是漏填了其中一个");
            }
            if ((poison.TakeFlags & ~0x3Fu) != 0)
            {
                warnings.Add(who + ": TakeFlags=" + poison.TakeFlags +
                    " 有 DamageFlags 枚举之外的位（只有 1/2/4/8/16/32 有人读）");
            }
            if ((poison.RangeFlags & ~0xFFu) != 0)
            {
                warnings.Add(who + ": RangeFlags=" + poison.RangeFlags +
                    " 超过 8 类目标的掩码（最大 255）；" +
                    "注意 DamageRangeFlags 枚举给的是位号，这里要写 1<<位号");
            }
        }

        /// <summary>灼热那一档的自检，判据和毒伤同构（漏填、负数、越界掩码、染色串写错都点名）。</summary>
        private static void CheckBurn(CustomBurn.BurnSpec burn, List<string> warnings)
        {
            if (burn == null)
            {
                return;
            }
            string who = "灼热 " + (string.IsNullOrEmpty(burn.Name)
                ? "0x" + Convert.ToString(System.Runtime.CompilerServices
                    .RuntimeHelpers.GetHashCode(burn), 16)
                : burn.Name);
            int first = burn.FirstTick > 0 ? burn.FirstTick : burn.Interval;
            if (burn.Ticks < 0)
            {
                warnings.Add(who + ": Ticks=" + burn.Ticks + " 是负数");
            }
            if (burn.Ticks > 0 && burn.Duration > 0)
            {
                warnings.Add(who + ": 同时写了 Ticks=" + burn.Ticks + " 和 Duration=" +
                    burn.Duration + " —— Ticks 会覆盖 Duration，那个时长不参与");
            }
            if (burn.Ticks <= 0 && burn.Duration <= 0)
            {
                warnings.Add(who + ": 既没写 Ticks 也没写 Duration" +
                    "，挂上去当拍就收账，一刀都不会落");
            }
            if (burn.Interval <= 0)
            {
                warnings.Add(who + ": Interval=" + burn.Interval +
                    "，会变成每帧一刀（NextTick 归零后立刻又能归零）");
            }
            else if (burn.Ticks <= 0 && burn.Duration > 0 && first > burn.Duration)
            {
                warnings.Add(who + ": 第一刀要等 " + first + " 帧，但整段灼热 " +
                    burn.Duration + " 帧就到期 —— 一刀都不会落");
            }
            if (burn.DamagePercent <= 0)
            {
                warnings.Add(who + ": DamagePercent=" + burn.DamagePercent +
                    "，灼热只按百分比算，没写就一刀 0 点（RoundDown 时连这一刀都不落）");
            }
            if ((burn.TakeFlags & ~0x3Fu) != 0)
            {
                warnings.Add(who + ": TakeFlags=" + burn.TakeFlags +
                    " 有 DamageFlags 枚举之外的位（只有 1/2/4/8/16/32 有人读）");
            }
            if ((burn.RangeFlags & ~0xFFu) != 0)
            {
                warnings.Add(who + ": RangeFlags=" + burn.RangeFlags +
                    " 超过 8 类目标的掩码（最大 255）；" +
                    "注意 DamageRangeFlags 枚举给的是位号，这里要写 1<<位号");
            }
            if (burn.SplashWidth < 0 || burn.SplashRowSpan < 0)
            {
                warnings.Add(who + ": SplashWidth=" + burn.SplashWidth +
                    " SplashRowSpan=" + burn.SplashRowSpan + " 里有负数");
            }
            CheckTint(who, "TintColor", burn.TintColor, warnings);
            if (!string.IsNullOrEmpty(burn.TintColor) && !burn.Tint)
            {
                warnings.Add(who + ": 写了 TintColor=\"" + burn.TintColor +
                    "\" 但 Tint=false —— 这层灼热不显色，那个色永远用不上");
            }
        }

        /// <summary>
        /// 染色串写错了不会抛，只是安静地不染 —— 和"没填"长得一模一样，所以必须点名。
        /// </summary>
        private static void CheckTint(string who, string field, string value,
            List<string> warnings)
        {
            if (!string.IsNullOrEmpty(value) &&
                !CustomPlantDef.TryParseTint(value, out SexyColor _))
            {
                warnings.Add(who + ": " + field + "=\"" + value +
                    "\" 解析不出来，要 #rrggbb 或 #rrggbbaa 十六进制，染色不会生效");
            }
        }

        private static bool HasDefNamed(string name)
        {
            foreach (CustomPlantDef other in CustomPlantRegistry.Defs)
            {
                if (string.Equals(other.Name, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>有没有哪株植物声明"能被这张卡升级"（判式在 CustomPlantRegistry.UpgradesInto）。</summary>
        private static bool AnyDefUpgradesInto(CustomPlantDef card)
        {
            return CustomPlantRegistry.UpgradedFrom(card) != null;
        }

        private static void Check(CustomPlantDef def, List<string> warnings,
            Dictionary<string, int> nameCount, Dictionary<int, string> reanimOwner)
        {
            if (def == null)
            {
                return;
            }
            string who = def.Name + " [" + def.PackId + "]";
            bool laidOut = CustomPlantRegistry.DefinitionsInstalled;

            if (laidOut && def.SeedId <= 0)
            {
                warnings.Add(who + ": 排版跑完了却还没有种子号（注册被拒？）");
            }
            if (def.Cost <= 0)
            {
                warnings.Add(who + ": Cost=" + def.Cost + "，选卡上会显示 0 阳光");
            }
            if (def.RefreshTime <= 0)
            {
                warnings.Add(who + ": RefreshTime=" + def.RefreshTime + "，卡槽不会进入冷却");
            }
            if (def.SubClass == PlantSubClass.Shooter && def.LaunchRate <= 0)
            {
                warnings.Add(who + ": SubClass=Shooter 但 LaunchRate=0，永远不会开火");
            }
            if (def.ShootingCounter > 0 && def.SubClass != PlantSubClass.Shooter)
            {
                warnings.Add(who + ": 设了 ShootingCounter 但 SubClass 不是 Shooter，" +
                    "开火定格不会生效（那个计数只在 FindTargetAndFire 里写）");
            }
            if (def.ShootingAnimRate > 0f && def.ShootingCounter <= 0)
            {
                warnings.Add(who + ": 设了 ShootingAnimRate=" + def.ShootingAnimRate +
                    " 但没设 ShootingCounter —— 只改速率不改定格，动画会提前定住或一直动");
            }
            CheckTint(who, "BodyTint", def.BodyTint, warnings);
            CheckTint(who, "ParticleTint", def.ParticleTint, warnings);
            if (!string.IsNullOrEmpty(def.CardAsset) && !AssetStore.Has(def.CardAsset))
            {
                warnings.Add(who + ": CardAsset=\"" + def.CardAsset +
                    "\" 没有在 AssetStore 里注册，卡图会画不出来（当前素材 " +
                    AssetStore.Count + " 张）");
            }
            if (!string.IsNullOrEmpty(def.CardBackground) &&
                Array.IndexOf(KnownCardBackgrounds, def.CardBackground) < 0)
            {
                warnings.Add(who + ": CardBackground=\"" + def.CardBackground +
                    "\" 不认识，可选 green/purple/gray");
            }
            if (string.IsNullOrEmpty(def.CardAsset) && def.CardNativeSeed == SeedType.None &&
                def.HasPreview)
            {
                warnings.Add(who + ": 没有自定义卡图也没指定 CardNativeSeed，卡槽里是空壳");
            }
            if (def.NativeReanimType != ReanimationType.None &&
                (!string.IsNullOrEmpty(def.SpineName) || !string.IsNullOrEmpty(def.ReanimName)))
            {
                warnings.Add(who + ": 同时给了 NativeReanimType 和 Spine/Reanim 资源名，" +
                    "自定义动画会被忽略");
            }
            if (Array.IndexOf(DoubleFiringSeeds, def.DrawAsSeed) >= 0 && def.ShootingCounter <= 0)
            {
                warnings.Add(who + ": DrawAsSeed=" + def.DrawAsSeed +
                    " 在 UpdateShooting 里有自己的开火分支，借它的外形会顺带多打一枪");
            }
            if (!string.IsNullOrEmpty(def.UpgradesToName) && !HasDefNamed(def.UpgradesToName))
            {
                warnings.Add(who + ": UpgradesToName=\"" + def.UpgradesToName +
                    "\" 在已注册的植物里找不到，升级卡点上去游戏只会回 NeedsUpgrade");
            }
            if (def.IsUpgradeCard && !AnyDefUpgradesInto(def))
            {
                warnings.Add(who + ": 声明了 IsUpgradeCard，但没有任何植物说能被它升级 —— " +
                    "这张卡在场上点谁都是 NeedsUpgrade");
            }
            if (laidOut && def.UsesCustomAnimation && def.ReanimSlot < 0)
            {
                warnings.Add(who + ": 动画槽仍是 -1（自动分配没生效）");
            }
            if (!string.IsNullOrEmpty(def.Name))
            {
                nameCount.TryGetValue(def.Name, out int seen);
                nameCount[def.Name] = seen + 1;
                if (seen >= 1)
                {
                    warnings.Add(who + ": 名字\"" + def.Name + "\"被两株以上植物共用，" +
                        "图鉴与选卡里分不出谁是谁");
                }
            }
            if (def.UsesCustomAnimation && def.ReanimSlot >= 0)
            {
                int slot = (int)def.ReanimType;
                if (reanimOwner.TryGetValue(slot, out string owner))
                {
                    warnings.Add(who + ": 动画槽 " + slot + " 已被 " + owner + " 占用");
                }
                else
                {
                    reanimOwner[slot] = who;
                }
            }
        }
    }
}
