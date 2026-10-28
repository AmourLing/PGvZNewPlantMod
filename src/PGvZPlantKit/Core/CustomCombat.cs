using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>跨植物共用的僵尸/伤害规则（对应 Python 版同名 helper）。</summary>
    public static class CustomCombat
    {
        public const int ElectricPeaContactDamage = 80;
        public const int ElectricPeaShockDamage = 120;
        public const int ElectricPeaShockInterval = 9;
        public const int PoisonPeaImpactDamage = 40;
        public const int PoisonDuration = 300;
        public const int PoisonTickInterval = 60;
        public const int PoisonDamagePercent = 2;
        public const int PoisonSlowPercent = 25;
        public const int StonePeaImpactDamage = 80;
        public const int StonePeaStunChance = 25;
        public const int StonePeaKnockbackChance = 25;
        public const int StonePeaExecuteChance = 2;
        public const int StonePeaStunDuration = 60;
        public const int StonePeaKnockbackDistance = 80;

        public const int SsbpsPoisonDuration = 600;
        public const int SsbpsPoisonTickInterval = 60;
        public const int SsbpsPoisonDamagePercent = 20;
        public const int SsbpsPoisonSlowPercent = 30;

        /// <summary>
        /// 一帧一帧自己数时间的中毒定义。引擎没有中毒状态（Zombie 上只有冰冻/燃烧/黄油），
        /// 所以计时、扣血、减速、显色全在框架这边。一个包写一条静态 spec 就够，
        /// 数值不同的毒（每刀 100 的、按血量百分比的）都是同一条机制的不同参数。
        /// </summary>
        public sealed class PoisonSpec
        {
            /// <summary>给日志和自检用的名字（"水神的毒"这种），漏填就用第一个已填的字段兜底。</summary>
            public string Name;
            /// <summary>每刀固定伤害；&gt;0 时按它扣，忽略 <see cref="DamagePercent"/>。</summary>
            public int FixedDamage;
            /// <summary>每刀按血量上限的百分之几（整数百分比），FixedDamage=0 时生效。</summary>
            public int DamagePercent;
            /// <summary>整段毒的时长（游戏帧/cs），倒计时走完就收账。</summary>
            public int Duration;
            /// <summary>
            /// 想落几刀。&gt;0 时 <see cref="Duration"/> 由它推出来
            /// （<c>FirstTick + Interval*(Ticks-1)</c>）：末刀落在最后一帧，不用为"最后一刀"
            /// 去凑 305 那种时长，也不会到期后空转一整秒。0 = 按 <see cref="Duration"/> 自己算刀数。
            /// </summary>
            public int Ticks;
            /// <summary>两刀之间的间隔。</summary>
            public int Interval;
            /// <summary>第一刀提前到第几帧；0 = 等满一个 Interval 才出第一刀。</summary>
            public int FirstTick;
            /// <summary>百分比伤害用哪份上限：true = 每刀现算当前最大值，false = 施加时钉死。</summary>
            public bool UseCurrentMaximum = true;
            /// <summary>减速百分比（0 = 不减速），靠回缩本帧位移实现（在 Board.UpdateGameObjects 前后取坐标）。</summary>
            public int SlowPercent;
            /// <summary>要不要把僵尸染紫（框架唯一那层中毒表现）。</summary>
            public bool Tint = true;
            /// <summary>
            /// 传给 TakeDamage 的 DamageFlags 掩码，管"怎么扣"：1 穿盾 / 2 盾体同扣 / 8 不闪白 …
            /// 留 0 = 继承把这层毒挂上的那一发子弹（<c>Projectile.GetDamageFlags(zombie)</c>），
            /// 也就是"这一口毒怎么结算，跟那发子弹一致"；写了值才不继承。
            /// </summary>
            public uint TakeFlags;
            /// <summary>
            /// 每刀前问一次 EffectedByDamage，管"打得着谁"。
            /// 留 0 = 继承那一发子弹的 <c>mDamageRangeFlags</c>（豌豆 1、大喷菇 9、香蒲 11…）；
            /// 没有来源子弹可读时也留 0，那表示不设门槛。写了值才不继承。
            /// </summary>
            public uint RangeFlags;
            /// <summary>&gt;0 时重复施加不再重置计时，只把"还欠几刀"补到这个上限（下一刀时刻不动）。</summary>
            public int TopUpHits;
        }

        /// <summary>一条毒定义在某个僵尸身上的进度。两套掩码在挂上的那一刻就定下来了。</summary>
        private sealed class PoisonRecord
        {
            public PoisonSpec Spec;
            public int Remaining;
            public int NextTick;
            public int MaximumHealth;
            public uint TakeFlags;
            public uint RangeFlags;
        }

        /// <summary>一只僵尸的中毒状态：本帧起点（算减速位移用）+ 它身上挂着的每条毒。</summary>
        private sealed class PoisonedZombie
        {
            public float LastX;
            public float LastY;
            public readonly List<PoisonRecord> Records = new List<PoisonRecord>();
        }

        private static readonly Dictionary<Zombie, PoisonedZombie> Poisoned =
            new Dictionary<Zombie, PoisonedZombie>();

        private static readonly List<Zombie> PoisonStale = new List<Zombie>();

        /// <summary>毒这一拍被驱动了几次（诊断用：为 0 就说明驱动点没跑到，刀自然一刀都不会落）。</summary>
        public static int PoisonDriverCalls { get; private set; }

        /// <summary>真正扣下去的毒刀数累计。</summary>
        public static int PoisonHitsApplied { get; private set; }

        /// <summary>被 RangeFlags（EffectedByDamage）挡掉、只走倒计时不扣血的刀数累计。</summary>
        public static int PoisonGateBlocked { get; private set; }

        /// <summary>当前账本里挂着的效果数（键=中毒的僵尸，值=它身上的条数）。</summary>
        public static int PoisonedZombieCount => Poisoned.Count;

        public static int PoisonRecordCount
        {
            get
            {
                int total = 0;
                foreach (PoisonedZombie state in Poisoned.Values)
                {
                    total += state.Records.Count;
                }
                return total;
            }
        }

        private static readonly List<PoisonSpec> Specs = new List<PoisonSpec>();

        /// <summary>已登记的毒定义，自检和诊断快照都读这份。</summary>
        public static IReadOnlyList<PoisonSpec> PoisonSpecs => Specs;

        /// <summary>
        /// 登记一条毒定义（同一条重复登记只留一份）。登记只为了自检与报告：
        /// 数值写错的毒不会报错，只会安静地一刀都不落，所以让它至少能在日志里被点名。
        /// </summary>
        public static void RegisterPoison(PoisonSpec spec)
        {
            if (spec != null && !Specs.Contains(spec))
            {
                Specs.Add(spec);
            }
        }

        /// <summary>毒豌豆那一档：5 秒、每 0.6 秒一刀、按当前血量上限 2%、减速 25%。</summary>
        public static readonly PoisonSpec PoisonPea = new PoisonSpec
        {
            Name = "毒豌豆",
            Duration = PoisonDuration,
            Interval = PoisonTickInterval,
            DamagePercent = PoisonDamagePercent,
            SlowPercent = PoisonSlowPercent,
        };

        /// <summary>桑葚爆破手那一档：10 秒、每 0.6 秒一刀、钉死上限的 20%、减速 30%。</summary>
        public static readonly PoisonSpec SsbpsPoison = new PoisonSpec
        {
            Name = "桑葚毒",
            Duration = SsbpsPoisonDuration,
            Interval = SsbpsPoisonTickInterval,
            DamagePercent = SsbpsPoisonDamagePercent,
            SlowPercent = SsbpsPoisonSlowPercent,
            UseCurrentMaximum = false,
        };

        static CustomCombat()
        {
            RegisterPoison(PoisonPea);
            RegisterPoison(SsbpsPoison);
        }

        public static bool IsGiantZombie(Zombie zombie)
        {
            return zombie != null && (zombie.mZombieType == ZombieType.Gargantuar ||
                zombie.mZombieType == ZombieType.RedeyeGargantuar ||
                zombie.mZombieType == ZombieType.RobotTitan ||
                zombie.mZombieType == ZombieType.RedeyeRobotTitan);
        }

        public static int ZombieMaximumHealth(Zombie zombie)
        {
            int health = Math.Max(0, zombie.mBodyMaxHealth);
            if (zombie.mHasHelm)
            {
                health += Math.Max(0, zombie.mHelmMaxHealth);
            }
            if (zombie.mHasShield)
            {
                health += Math.Max(0, zombie.mShieldMaxHealth);
            }
            if (zombie.mFlyingMaxHealth > 0)
            {
                health += zombie.mFlyingMaxHealth;
            }
            return health;
        }

        public static int ZombieEffectiveHealth(Zombie zombie)
        {
            int health = Math.Max(0, zombie.mBodyHealth);
            if (zombie.mHasHelm)
            {
                health += Math.Max(0, zombie.mHelmHealth);
            }
            if (zombie.mHasShield)
            {
                health += Math.Max(0, zombie.mShieldHealth);
            }
            if (zombie.mFlyingHealth > 0)
            {
                health += zombie.mFlyingHealth;
            }
            return health;
        }

        public static void DoElectricDamage(Zombie zombie, int damage, uint flags, bool ashOnKill)
        {
            if (ashOnKill && ZombieEffectiveHealth(zombie) <= damage)
            {
                zombie.ApplyBurn();
            }
            else
            {
                zombie.TakeDamage(damage, flags);
            }
        }

        /// <summary>
        /// 借用某个原生种子的攻击矩形打一行范围伤害。DoRowAreaDamage 里的
        /// GetPlantAttackRect / GetDamageRangeFlags 都按 mSeedType 取矩形，而自定义种子在
        /// 原生表里没有条目，所以先临时换成 rectSeed 再调，完事无条件还原（异常也还原）。
        /// rectSeed 传 SeedType.None 表示就按这株当前种子的矩形算。钩子处理器里直接：
        ///   CustomCombat.RowAreaDamage(plant, 40, 2u, SeedType.Fumeshroom)
        /// </summary>
        public static void RowAreaDamage(Plant plant, int damage, uint flags,
            SeedType rectSeed = SeedType.None)
        {
            if (plant == null || damage <= 0)
            {
                return;
            }
            SeedType original = plant.mSeedType;
            if (rectSeed != SeedType.None)
            {
                plant.mSeedType = rectSeed;
            }
            try
            {
                plant.DoRowAreaDamage(damage, flags);
            }
            finally
            {
                plant.mSeedType = original;
            }
        }

        public static bool BossCanBeHit(Zombie zombie)
        {
            return zombie != null && (zombie.mZombiePhase == ZombiePhase.BossHeadIdleBeforeSpit ||
                zombie.mZombiePhase == ZombiePhase.BossHeadIdleAfterSpit ||
                zombie.mZombiePhase == ZombiePhase.BossHeadSpit);
        }

        public static bool IsValidTarget(Zombie zombie)
        {
            return zombie != null && !zombie.mDead && !zombie.IsDeadOrDying();
        }

        public static bool IsValidElectricTarget(Projectile projectile, Zombie zombie)
        {
            return IsValidTarget(zombie) && zombie.EffectedByDamage((uint)projectile.mDamageRangeFlags);
        }

        public static bool IsValidElectricShockTarget(Projectile projectile, Zombie zombie)
        {
            if (!IsValidTarget(zombie))
            {
                return false;
            }
            if (zombie.mZombieType == ZombieType.Balloon)
            {
                return true;
            }
            if (zombie.mZombieType == ZombieType.Digger &&
                zombie.mZombiePhase == ZombiePhase.DiggerTunneling)
            {
                return true;
            }
            return zombie.EffectedByDamage((uint)projectile.mDamageRangeFlags);
        }

        public static bool IsWearingMetalArmor(Zombie zombie)
        {
            bool metalHelm = zombie.mHasHelm && zombie.mHelmHealth > 0 &&
                (zombie.mHelmType == HelmType.Pail || zombie.mHelmType == HelmType.Football ||
                 zombie.mHelmType == HelmType.Digger || zombie.mHelmType == HelmType.Bobsled ||
                 zombie.mHelmType == HelmType.FootballPremium ||
                 zombie.mHelmType == HelmType.RobotTitanHead ||
                 zombie.mHelmType == HelmType.RedeyeRobotTitanHead);
            bool metalShield = zombie.mHasShield && zombie.mShieldHealth > 0 &&
                (zombie.mShieldType == ShieldType.Door || zombie.mShieldType == ShieldType.Ladder);
            return metalHelm || metalShield;
        }

        /// <summary>挂毒豌豆那一档的毒（= ApplyPoison(zombie, PoisonPea)）。</summary>
        public static void ApplyPoison(Zombie zombie) => ApplyPoison(zombie, PoisonPea);

        public static void ApplySsbpsPoison(Zombie zombie, int maximumHealth)
        {
            PoisonRecord record = TouchPoison(zombie, SsbpsPoison, null);
            if (record != null && maximumHealth > 0)
            {
                record.MaximumHealth = maximumHealth;
            }
        }

        /// <summary>
        /// 给僵尸挂一条毒。同一条 spec 重复施加按 TopUpHits 决定"补满欠刀"还是"整条重置"，
        /// 不同 spec 各占一格互不覆盖（减速叠乘，任意一条要显色就显色）。
        /// <paramref name="source"/> 是挂上这层毒的那发子弹：spec 没写两套掩码时按它继承
        /// （伤害掩码 = <c>source.GetDamageFlags(zombie)</c>，范围掩码 = <c>source.mDamageRangeFlags</c>）。
        /// </summary>
        public static void ApplyPoison(Zombie zombie, PoisonSpec spec, Projectile source)
        {
            TouchPoison(zombie, spec, source);
        }

        /// <summary>没有来源子弹（范围型、地面型毒）时套这一条：spec 没写的两套掩码就按 0 处理。</summary>
        public static void ApplyPoison(Zombie zombie, PoisonSpec spec)
        {
            TouchPoison(zombie, spec, null);
        }

        private static PoisonRecord TouchPoison(Zombie zombie, PoisonSpec spec, Projectile source)
        {
            if (zombie == null || spec == null)
            {
                return null;
            }
            if (!Poisoned.TryGetValue(zombie, out PoisonedZombie state))
            {
                state = new PoisonedZombie
                {
                    LastX = zombie.mPosX,
                    LastY = zombie.mPosY,
                };
                Poisoned[zombie] = state;
            }
            List<PoisonRecord> records = state.Records;
            PoisonRecord existing = FindRecord(records, spec);
            if (existing != null && spec.TopUpHits > 0)
            {
                // 只补欠的刀：计时和两套掩码都按挂上那一口的那发子弹，别让后一发改写规则
                TopUpPendingHits(existing, spec.TopUpHits);
                return existing;
            }
            PoisonRecord record = existing ?? new PoisonRecord();
            record.Spec = spec;
            record.Remaining = TickSpan(spec);
            record.NextTick = spec.FirstTick > 0 ? spec.FirstTick : spec.Interval;
            record.MaximumHealth = ZombieMaximumHealth(zombie);
            record.TakeFlags = spec.TakeFlags != 0 ? spec.TakeFlags
                : (source != null ? source.GetDamageFlags(zombie) : 0u);
            record.RangeFlags = spec.RangeFlags != 0 ? spec.RangeFlags
                : (source != null ? (uint)source.mDamageRangeFlags : 0u);
            if (existing == null)
            {
                records.Add(record);
            }
            return record;
        }

        /// <summary>这档毒一共走多少帧：写了 Ticks 就按刀数推，否则用 Duration。</summary>
        public static int TickSpan(PoisonSpec spec)
        {
            if (spec == null)
            {
                return 0;
            }
            if (spec.Ticks > 0 && spec.Interval > 0)
            {
                int first = spec.FirstTick > 0 ? spec.FirstTick : 1;
                return first + spec.Interval * (spec.Ticks - 1);
            }
            return spec.Duration;
        }

        private static PoisonRecord FindRecord(List<PoisonRecord> records, PoisonSpec spec)        {
            foreach (PoisonRecord record in records)
            {
                if (ReferenceEquals(record.Spec, spec))
                {
                    return record;
                }
            }
            return null;
        }

        /// <summary>还欠几刀 = 从现在起 NextTick 在到期前还能归零几次。</summary>
        private static int PendingHits(PoisonRecord record)
        {
            PoisonSpec spec = record.Spec;
            if (spec == null || spec.Interval <= 0 || record.Remaining < record.NextTick)
            {
                return 0;
            }
            return 1 + (record.Remaining - record.NextTick) / spec.Interval;
        }

        /// <summary>
        /// 叠毒只补整周期：已经打出去的刀不追回，下一刀的时刻也不提前，
        /// 只是把尾巴拉长到"还能出满 maxHits 刀"。直接把剩余时长拍回满值会让下一刀早到。
        /// </summary>
        private static void TopUpPendingHits(PoisonRecord record, int maxHits)
        {
            int extra = maxHits - PendingHits(record);
            if (extra > 0)
            {
                record.Remaining += extra * record.Spec.Interval;
            }
        }

        /// <summary>身上挂着任意一条毒。</summary>
        public static bool HasPoison(Zombie zombie)
        {
            return zombie != null && Poisoned.ContainsKey(zombie);
        }

        /// <summary>身上挂着至少一条要求显色的毒（画僵尸那两处钩子按这个决定要不要染紫）。</summary>
        public static bool HasVisiblePoison(Zombie zombie)
        {
            if (zombie == null ||
                !Poisoned.TryGetValue(zombie, out PoisonedZombie state))
            {
                return false;
            }
            foreach (PoisonRecord record in state.Records)
            {
                if (record.Spec != null && record.Spec.Tint)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 摘掉一只僵尸的全部毒。僵尸是按对象引用记账的，而引擎会把死掉的僵尸
        /// <c>PrepareForReuse</c> 塞回对象池复用 —— 不清账的话下一只僵尸直接继承上一层的毒。
        /// </summary>
        public static void ClearPoison(Zombie zombie)
        {
            if (zombie != null)
            {
                Poisoned.Remove(zombie);
            }
        }

        /// <summary>
        /// Board.UpdateGameObjects 之前：把每只中毒僵尸的当前位置记下来，
        /// 一帧结束后按它算位移缩放（减速就是这么实现的）。账本空着时什么都不做。
        /// </summary>
        public static void PreTickPoison(Board board)
        {
            if (board == null || Poisoned.Count == 0)
            {
                return;
            }
            PoisonDriverCalls++;
            foreach (KeyValuePair<Zombie, PoisonedZombie> entry in Poisoned)
            {
                Zombie zombie = entry.Key;
                if (zombie == null || zombie.mDead || zombie.IsDeadOrDying())
                {
                    continue;
                }
                entry.Value.LastX = zombie.mPosX;
                entry.Value.LastY = zombie.mPosY;
            }
        }

        /// <summary>
        /// Board.UpdateGameObjects 之后：回缩本帧位移（减速）、逐条步进扣血，
        /// 再把死掉 / 换局 / 到期的账收走。
        /// </summary>
        public static void TickPoisonBoard(Board board)
        {
            if (board == null || Poisoned.Count == 0)
            {
                return;
            }
            PoisonStale.Clear();
            foreach (KeyValuePair<Zombie, PoisonedZombie> entry in Poisoned)
            {
                Zombie zombie = entry.Key;
                PoisonedZombie state = entry.Value;
                if (zombie == null || zombie.mDead || zombie.IsDeadOrDying() ||
                    zombie.mBoard != board)
                {
                    PoisonStale.Add(zombie);
                    continue;
                }
                float scale = 1f;
                foreach (PoisonRecord record in state.Records)
                {
                    if (record.Spec != null)
                    {
                        scale *= (100f - record.Spec.SlowPercent) / 100f;
                    }
                }
                zombie.mPosX = state.LastX + (zombie.mPosX - state.LastX) * scale;
                zombie.mPosY = state.LastY + (zombie.mPosY - state.LastY) * scale;

                for (int i = state.Records.Count - 1; i >= 0; i--)
                {
                    if (!TickOne(zombie, state.Records[i]))
                    {
                        state.Records.RemoveAt(i);
                    }
                }
                if (state.Records.Count == 0)
                {
                    PoisonStale.Add(zombie);
                }
            }
            foreach (Zombie zombie in PoisonStale)
            {
                Poisoned.Remove(zombie);
            }
        }

        /// <summary>步进一条毒；返回 false 表示这条该收走了。</summary>
        private static bool TickOne(Zombie zombie, PoisonRecord record)
        {
            PoisonSpec spec = record.Spec;
            if (spec == null || zombie.mDead || zombie.IsDeadOrDying())
            {
                return false;
            }
            record.Remaining--;
            record.NextTick--;
            if (record.NextTick <= 0)
            {
                int damage = PoisonDamage(zombie, record);
                if (damage > 0)
                {
                    if (record.RangeFlags != 0 && !zombie.EffectedByDamage(record.RangeFlags))
                    {
                        PoisonGateBlocked++;
                    }
                    else
                    {
                        PoisonHitsApplied++;
                        zombie.TakeDamage(damage, record.TakeFlags);
                    }
                }
                record.NextTick = spec.Interval;
            }
            return record.Remaining > 0;
        }

        private static int PoisonDamage(Zombie zombie, PoisonRecord record)
        {
            PoisonSpec spec = record.Spec;
            if (spec.FixedDamage > 0)
            {
                return spec.FixedDamage;
            }
            int maximum = spec.UseCurrentMaximum ? ZombieMaximumHealth(zombie) : record.MaximumHealth;
            return Math.Max(1, (maximum * spec.DamagePercent + 99) / 100);
        }

        public static void ElectricShockRandom(Projectile projectile)
        {
            Board board = projectile.mBoard;
            List<Zombie> targets = ZombiesInElectricArea(
                projectile, projectile.mPosX, projectile.mPosY, new HashSet<Zombie>());
            if (targets.Count == 0)
            {
                return;
            }
            Zombie target = targets[TodCommon.RandRangeInt(0, targets.Count - 1)];
            HashSet<Zombie> shocked = new HashSet<Zombie>();
            while (target != null)
            {
                shocked.Add(target);
                bool conducts = IsWearingMetalArmor(target);
                DoElectricDamage(target, ElectricPeaShockDamage,
                    projectile.GetDamageFlags(target), true);
                if (!conducts)
                {
                    break;
                }
                List<Zombie> nearby = ZombiesInElectricArea(
                    projectile, target.mPosX, target.mPosY, shocked);
                if (nearby.Count == 0)
                {
                    break;
                }
                target = nearby[TodCommon.RandRangeInt(0, nearby.Count - 1)];
            }
        }

        private static List<Zombie> ZombiesInElectricArea(Projectile projectile,
            float centerX, float centerY, HashSet<Zombie> excluded)
        {
            Board board = projectile.mBoard;
            int gridX = board.PixelToGridXKeepOnBoard((int)centerX, (int)centerY);
            int gridY = board.PixelToGridYKeepOnBoard((int)centerX, (int)centerY);
            List<Zombie> result = new List<Zombie>();
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (excluded.Contains(zombie) || !IsValidElectricShockTarget(projectile, zombie))
                {
                    continue;
                }
                int zombieGridX = board.PixelToGridXKeepOnBoard((int)zombie.mPosX, (int)zombie.mPosY);
                if (Math.Abs(zombieGridX - gridX) <= 1 && Math.Abs(zombie.mRow - gridY) <= 1)
                {
                    result.Add(zombie);
                }
            }
            return result;
        }
    }
}
