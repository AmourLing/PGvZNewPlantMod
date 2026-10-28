using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 灼热（燃烧）这一档持续伤害，账本结构与 <see cref="CustomCombat"/> 里那套毒伤同构
    /// —— 按僵尸对象记账、两套掩码在挂上那一刻定下来、对象池复用前清账、计数进诊断快照 ——
    /// 但它是**独立的一份**：灼热不是毒，不共用毒的类型、台账、显色和计数，
    /// 所以"烧着的僵尸"不会被任何按毒判断的逻辑（比如水神那套）误认。
    /// 逐拍还会照引擎西瓜那条溅射规则往周围补同一刀，这是毒没有的行为。
    /// </summary>
    public static class CustomBurn
    {
        /// <summary>一档灼热的声明。字段含义与毒伤那套对齐，另加逐拍溅射两项。</summary>
        public sealed class BurnSpec
        {
            /// <summary>给日志和自检用的名字（"灼热"这种）。</summary>
            public string Name;
            /// <summary>每刀按总最大生命（本体+防具+盾+飞行）的百分之几。</summary>
            public int DamagePercent;
            /// <summary>百分比向下取整（3% × 270 = 8.1 → 8）。false = 向上取整、至少 1 点。</summary>
            public bool RoundDown = true;
            /// <summary>整段燃烧的时长（游戏帧），走完收账。</summary>
            public int Duration;
            /// <summary>
            /// 想烧几刀。&gt;0 时 <see cref="Duration"/> 由它推出来
            /// （<c>FirstTick + Interval*(Ticks-1)</c>）：末刀落在最后一帧，不会出现
            /// "多补几帧才够最后一刀"的 305 那种凑数写法，也不会到期后空转一整秒。
            /// 0 = 按 <see cref="Duration"/> 自己算刀数。
            /// </summary>
            public int Ticks;
            /// <summary>两刀之间的间隔；"每秒一刀"就是 100。</summary>
            public int Interval;
            /// <summary>第一刀提前到第几帧；0 = 等满一个 Interval。</summary>
            public int FirstTick;
            /// <summary>
            /// &gt;0 时每一刀除了烧本体，还把同一次伤害溅给周围：以被烧的那只为中点取一个
            /// 这么宽的矩形，行差不超过 <see cref="SplashRowSpan"/>、矩形搭上就算。
            /// 判法照引擎西瓜的溅射（<c>IsZombieHitBySplash</c>：±1 行 + 矩形重叠 + EffectedByDamage）。
            /// </summary>
            public int SplashWidth;
            /// <summary>溅射跨几行（1 = 本行加上下各一行）。配 <see cref="SplashWidth"/> 用。</summary>
            public int SplashRowSpan = 1;
            /// <summary>烧着的僵尸要不要显色（默认要，烧起来看得见才叫烧）。</summary>
            public bool Tint = true;
            /// <summary>显色的颜色，<c>#rrggbb</c>；留空用默认的火橙。</summary>
            public string TintColor;
            /// <summary>
            /// 传给 TakeDamage 的 DamageFlags 掩码。留 0 = 继承挂上它的那发子弹
            /// （<c>Projectile.GetDamageFlags(zombie)</c>），写了值才不继承。
            /// </summary>
            public uint TakeFlags;
            /// <summary>每刀前问一次 EffectedByDamage。留 0 = 继承那发子弹的范围掩码。</summary>
            public uint RangeFlags;
        }

        private sealed class BurnRecord
        {
            public BurnSpec Spec;
            public int Remaining;
            public int NextTick;
            public uint TakeFlags;
            public uint RangeFlags;
        }

        private sealed class BurnedZombie
        {
            public readonly List<BurnRecord> Records = new List<BurnRecord>();
        }

        private static readonly Dictionary<Zombie, BurnedZombie> Burned =
            new Dictionary<Zombie, BurnedZombie>();
        private static readonly List<Zombie> Stale = new List<Zombie>();
        private static readonly List<BurnSpec> Specs = new List<BurnSpec>();

        /// <summary>注册过的灼热档（自检和诊断快照看这份）。</summary>
        public static IReadOnlyList<BurnSpec> BurnSpecs => Specs;

        /// <summary>真正烧下去的刀数累计。</summary>
        public static int BurnHitsApplied { get; private set; }

        /// <summary>逐拍溅射额外烧到的刀数累计（不含本体那一刀）。</summary>
        public static int BurnSplashApplied { get; private set; }

        /// <summary>被 RangeFlags 挡掉、只走倒计时不扣血的刀数累计。</summary>
        public static int BurnGateBlocked { get; private set; }

        /// <summary>当前账本里挂着灼热的僵尸数。</summary>
        public static int BurnedZombieCount => Burned.Count;

        public static void RegisterBurn(BurnSpec spec)
        {
            if (spec != null && !Specs.Contains(spec))
            {
                Specs.Add(spec);
            }
        }

        /// <summary>同档刷新时长但保留下一刀时刻；掩码取本次命中的来源子弹。</summary>
        public static void ApplyBurn(Zombie zombie, BurnSpec spec, Projectile source)
        {
            if (zombie == null || spec == null || zombie.mDead || zombie.IsDeadOrDying())
            {
                return;
            }
            if (!Burned.TryGetValue(zombie, out BurnedZombie state))
            {
                state = new BurnedZombie();
                Burned[zombie] = state;
            }
            BurnRecord record = state.Records.Find(item => item.Spec == spec);
            if (record == null)
            {
                record = new BurnRecord
                {
                    Spec = spec,
                    NextTick = spec.FirstTick > 0 ? spec.FirstTick : spec.Interval,
                };
                state.Records.Add(record);
            }
            CustomBulletDef bullet = CustomBullets.Of(source);
            record.Remaining = TickSpan(spec);
            record.TakeFlags = spec.TakeFlags != 0 ? spec.TakeFlags
                : bullet != null && bullet.DamageFlags != 0 ? bullet.DamageFlags
                : source != null ? source.GetDamageFlags(zombie) : 0u;
            record.RangeFlags = spec.RangeFlags != 0 ? spec.RangeFlags
                : source != null ? (uint)source.mDamageRangeFlags : 0u;
        }

        public static void ApplyBurn(Zombie zombie, BurnSpec spec) => ApplyBurn(zombie, spec, null);

        /// <summary>这档灼热一共烧多少帧：写了 Ticks 就按刀数推，否则用 Duration。</summary>
        public static int TickSpan(BurnSpec spec)
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

        /// <summary>
        /// 火焰标识：照引擎火球命中那一手放同一支 <c>JalapenoFire</c>
        /// （<c>Projectile.DoImpact</c> 里 Fireball 分支就是这么放的：0.25 起、速率 24、缩 0.7x0.4）。
        /// 不自己画叠加光 —— 那层 <c>mExtraAdditiveColor</c> 在 <c>Zombie.Draw</c> 里每帧被重写，外部改不进去。
        /// </summary>
        private static void PlayBurnFx(Zombie zombie)
        {
            LawnApp app = zombie.mApp;
            if (app == null)
            {
                return;
            }
            try
            {
                TRect rect = zombie.GetZombieRect();
                Reanimation fx = app.AddReanimation(
                    rect.mX + rect.mWidth / 2f, rect.mY - 20f,
                    zombie.mRenderOrder + 1, ReanimationType.JalapenoFire);
                if (fx == null)
                {
                    return;
                }
                fx.mAnimTime = 0.25f;
                fx.mAnimRate = 24f;
                fx.OverrideScale(0.7f, 0.4f);
            }
            catch (Exception e)
            {
                if (BurnFxErrors.Add(zombie.mZombieType))
                {
                    ModLog.Warn("灼热火焰标识放不出来（" + zombie.mZombieType + "）：" + e.Message);
                }
            }
        }

        private static readonly HashSet<ZombieType> BurnFxErrors = new HashSet<ZombieType>();

        /// <summary>身上挂着任意一层灼热。</summary>
        public static bool HasBurn(Zombie zombie)
        {
            return zombie != null && Burned.ContainsKey(zombie);
        }

        /// <summary>身上挂着至少一层要求显色的灼热（画僵尸那两处钩子按这个决定染不染）。</summary>
        public static bool HasVisibleBurn(Zombie zombie)
        {
            if (zombie == null || !Burned.TryGetValue(zombie, out BurnedZombie state))
            {
                return false;
            }
            foreach (BurnRecord record in state.Records)
            {
                if (record.Spec != null && record.Spec.Tint)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>显色用哪个颜色：第一条要求显色且写了 <c>TintColor</c> 的档，否则默认火橙。</summary>
        public static SexyColor BurnTintOf(Zombie zombie)
        {
            SexyColor fallback = new SexyColor(255, 120, 40, 255);
            if (zombie == null || !Burned.TryGetValue(zombie, out BurnedZombie state))
            {
                return fallback;
            }
            foreach (BurnRecord record in state.Records)
            {
                BurnSpec spec = record.Spec;
                if (spec != null && spec.Tint &&
                    CustomPlantDef.TryParseTint(spec.TintColor, out SexyColor color))
                {
                    return color;
                }
            }
            return fallback;
        }

        /// <summary>
        /// 摘掉一只僵尸的全部灼热。僵尸按对象引用记账，而引擎会把死掉的僵尸
        /// <c>PrepareForReuse</c> 塞回对象池复用 —— 不清账的话下一只僵尸直接继承这层火。
        /// </summary>
        public static void ClearBurn(Zombie zombie)
        {
            if (zombie != null)
            {
                Burned.Remove(zombie);
            }
        }

        /// <summary>每拍由 Board 的驱动钩子调用一次。</summary>
        public static void TickBurnBoard(Board board)
        {
            if (board == null || Burned.Count == 0)
            {
                return;
            }
            Stale.Clear();
            foreach (KeyValuePair<Zombie, BurnedZombie> entry in Burned)
            {
                Zombie zombie = entry.Key;
                BurnedZombie state = entry.Value;
                if (zombie == null || zombie.mDead || zombie.IsDeadOrDying() ||
                    zombie.mBoard != board)
                {
                    Stale.Add(zombie);
                    continue;
                }
                for (int i = state.Records.Count - 1; i >= 0; i--)
                {
                    if (!TickOne(board, zombie, state.Records[i]))
                    {
                        state.Records.RemoveAt(i);
                    }
                }
                if (state.Records.Count == 0)
                {
                    Stale.Add(zombie);
                }
            }
            foreach (Zombie zombie in Stale)
            {
                Burned.Remove(zombie);
            }
        }

        /// <summary>步进一层灼热；返回 false 表示这层该收走了。</summary>
        private static bool TickOne(Board board, Zombie zombie, BurnRecord record)
        {
            BurnSpec spec = record.Spec;
            if (spec == null || zombie.mDead || zombie.IsDeadOrDying())
            {
                return false;
            }
            record.Remaining--;
            record.NextTick--;
            if (record.NextTick <= 0)
            {
                int damage = BurnDamage(zombie, spec);
                if (damage > 0)
                {
                    if (record.RangeFlags != 0 && !zombie.EffectedByDamage(record.RangeFlags))
                    {
                        BurnGateBlocked++;
                    }
                    else
                    {
                        TRect center = zombie.GetZombieRect();
                        BurnHitsApplied++;
                        zombie.TakeDamage(damage, record.TakeFlags);
                        // 每一刀都给个"被火球打到"的火焰标识，不靠染色。
                        PlayBurnFx(zombie);
                        SplashBurnDamage(board, zombie, center, record, damage);
                    }
                }
                record.NextTick = spec.Interval;
            }
            return record.Remaining > 0;
        }

        private static int BurnDamage(Zombie zombie, BurnSpec spec)
        {
            long maximum = (long)Math.Max(0, zombie.mBodyMaxHealth) +
                Math.Max(0, zombie.mHelmMaxHealth) + Math.Max(0, zombie.mShieldMaxHealth) +
                Math.Max(0, zombie.mFlyingMaxHealth);
            long scaled = maximum * spec.DamagePercent;
            return (int)(spec.RoundDown ? Math.Max(0L, scaled / 100)
                : Math.Max(1L, (scaled + 99) / 100));
        }

        // 中心在主目标受击前取得，避免致死伤害改变碰撞框；博士的跨行判定沿用原生。
        private static void SplashBurnDamage(Board board, Zombie source, TRect center,
            BurnRecord record, int damage)
        {
            BurnSpec spec = record.Spec;
            if (board == null || spec.SplashWidth <= 0 || damage <= 0)
            {
                return;
            }
            TRect area = new TRect(
                center.mX + center.mWidth / 2 - spec.SplashWidth / 2,
                center.mY, spec.SplashWidth, center.mHeight);
            foreach (Zombie other in board.mZombies)
            {
                if (other == source || other.mDead || other.IsDeadOrDying())
                {
                    continue;
                }
                int rowSpan = other.mZombieType == ZombieType.Boss ? 0 : Math.Abs(other.mRow - source.mRow);
                if (rowSpan > spec.SplashRowSpan ||
                    (record.RangeFlags != 0 && !other.EffectedByDamage(record.RangeFlags)) ||
                    GameConstants.GetRectOverlap(area, other.GetZombieRect()) < 0)
                {
                    continue;
                }
                BurnSplashApplied++;
                other.TakeDamage(damage, record.TakeFlags);
            }
        }
    }
}
