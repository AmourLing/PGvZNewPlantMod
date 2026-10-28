using System;
using System.Collections.Generic;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 水神的护盾与毒伤：登记每行最靠右的那株水神，把迎面飞来的子弹弹回去，
    /// 并给"被她弹过的子弹命中"的僵尸挂一层毒——计时与扣血走框架的公用毒伤
    /// （<see cref="CustomCombat.PoisonSpec"/>），这里只声明她那一档的数值。
    /// </summary>
    internal static class TeiTisiExtras
    {
        private const int ReachRight = 80;
        private const int ReachLeft = 20;

        // 只反弹迎面飞来的弹（见 IsIncoming），友方向右飞的弹不会被掉头。
        // ZombiePea 是 PeaHead 的弹，向左飞，是最常见的一种"迎面"。
        private static readonly ProjectileType[] ReflectTypes =
        {
            ProjectileType.Pea,
            ProjectileType.ZombiePea,
            ProjectileType.Star,
            ProjectileType.Spike,
        };

        // 水神的毒：3 刀 ×100，命中当拍就落第一刀、之后每秒一刀；结束帧由 Ticks 推出来
        // （1 + 100*(3-1) = 201），末刀即收账，不用再为"最后一刀"去凑 305 那种时长。
        // 不减速、不染色；再被反弹弹打一次只把欠的刀补满 3 刀，不提前下一刀。
        // 计时与扣血由框架的公用毒伤负责。
        // 两套掩码这里是"显式写了"= 不继承那发反弹弹（弹是按豌豆算的：门会吃掉毒伤）；
        // 她要的是毒直接咬本体、并且不给受击闪白，所以自己写 9/9。想跟着子弹走就把这两行删掉。
        private static readonly CustomCombat.PoisonSpec Poison = new CustomCombat.PoisonSpec
        {
            Name = "水神的毒",
            FixedDamage = 100,
            Ticks = 3,
            Interval = 100,
            FirstTick = 1,
            TopUpHits = 3,
            TakeFlags = 9,   // DamageFlags：1 穿盾 | 8 不闪白
            RangeFlags = 9,  // DamageRangeFlags：1 地面 | 8 僵尸狗
            Tint = false,
            SlowPercent = 0,
        };

        private static readonly Dictionary<int, Plant> RowShield = new Dictionary<int, Plant>();
        private static readonly HashSet<Projectile> Reflected = new HashSet<Projectile>();
        private static readonly HashSet<Projectile> PoisonShots = new HashSet<Projectile>();

        private static readonly List<string> Failures = new List<string>();
        private static bool _installed;
        private static bool _hotReported;

        public static int HookCount { get; private set; }

        public static IReadOnlyList<string> HookFailures => Failures;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            CustomCombat.RegisterPoison(Poison);

            Add(typeof(Board), "Update", Type.EmptyTypes,
                (Action<Action<Board>, Board>)BoardUpdateHook);
            Add(typeof(Projectile), "Update", Type.EmptyTypes,
                (Action<Action<Projectile>, Projectile>)ProjectileUpdateHook);
            Add(typeof(Projectile), "DoImpact", new[] { typeof(Zombie) },
                (Action<Action<Projectile, Zombie>, Projectile, Zombie>)ProjectileImpactHook);
            // 弹体是对象池复用的，新弹出生一律清白，免得上一颗的毒记号落到新弹身上。
            Add(typeof(Projectile), "ProjectileInitialize",
                new[] { typeof(int), typeof(int), typeof(int), typeof(int), typeof(ProjectileType) },
                (Action<Action<Projectile, int, int, int, int, ProjectileType>, Projectile,
                    int, int, int, int, ProjectileType>)ProjectileInitializeHook);

            PlantKit.Log("teitisi extras installed: " + HookCount + " hooks");
        }

        private static void BoardUpdateHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("shield", () => RebuildShields(self));
        }

        private static void ProjectileUpdateHook(Action<Projectile> orig, Projectile self)
        {
            orig(self);
            Hot("reflect", () => CheckShot(self));
        }

        private static void ProjectileImpactHook(Action<Projectile, Zombie> orig,
            Projectile self, Zombie zombie)
        {
            orig(self, zombie);
            if (zombie == null || !PoisonShots.Contains(self))
            {
                return;
            }
            Hot("poison mark", () => CustomCombat.ApplyPoison(zombie, Poison));
        }

        private static void ProjectileInitializeHook(
            Action<Projectile, int, int, int, int, ProjectileType> orig, Projectile self,
            int x, int y, int renderOrder, int row, ProjectileType type)
        {
            Reflected.Remove(self);
            PoisonShots.Remove(self);
            orig(self, x, y, renderOrder, row, type);
        }

        private static void RebuildShields(Board board)
        {
            RowShield.Clear();
            SeedType seed = TeiTisiPlant.Def.Seed;
            foreach (Plant plant in board.mPlants)
            {
                if (plant.mDead || plant.mSeedType != seed)
                {
                    continue;
                }
                if (RowShield.TryGetValue(plant.mRow, out Plant current) &&
                    current.mPlantCol >= plant.mPlantCol)
                {
                    continue;
                }
                RowShield[plant.mRow] = plant;
            }
        }

        /// <summary>反弹挂在子弹这一侧：子弹每帧过一次判定，比让植物扫子弹便宜得多。</summary>
        private static void CheckShot(Projectile shot)
        {
            if (shot.mDead || !RowShield.TryGetValue(shot.mRow, out Plant shield))
            {
                return;
            }
            // 出框就把"已弹过"的记号清掉：掉头后她还在框里，不记这一笔会在原地来回抖。
            if (shot.mPosX < shield.mX - ReachLeft || shot.mPosX > shield.mX + ReachRight)
            {
                Reflected.Remove(shot);
                return;
            }
            if (Reflected.Contains(shot) ||
                Array.IndexOf(ReflectTypes, shot.mProjectileType) < 0 ||
                !IsIncoming(shot))
            {
                return;
            }
            Flip(shot);
            Reflected.Add(shot);
            PoisonShots.Add(shot);
            if (CustomPlantRegistry.Get(shield) is TeiTisiPlant teitisi)
            {
                teitisi.StartSwing();
            }
        }

        /// <summary>迎面 = 向左飞。Straight 恒定向右，星形弹看水平速度符号。</summary>
        private static bool IsIncoming(Projectile shot) =>
            shot.mMotionType == ProjectileMotion.Backwards ||
            (shot.mMotionType == ProjectileMotion.Star && shot.mVelX < 0f);

        /// <summary>掉头方式按运动类型分派；速度与贴图不动，只按需换弹种。</summary>
        private static void Flip(Projectile shot)
        {
            // 引擎按弹种决定撞谁：ZombiePea 只找植物，不改弹种的话掉头也打不到僵尸。
            // 顺手把"打得着谁"改回地面的 1（那是 DamageRangeFlags 掩码，不是 TakeDamage 的旗子）。
            if (shot.mProjectileType == ProjectileType.ZombiePea)
            {
                shot.mProjectileType = ProjectileType.Pea;
                shot.mFromPlant = SeedType.None;
                shot.mDamageRangeFlags = 1;
            }
            if (shot.mMotionType == ProjectileMotion.Backwards)
            {
                shot.mMotionType = ProjectileMotion.Straight;
                return;
            }
            // Star：位置每 tick 按速度积分，两个分量都取负就是原路退回。
            shot.mVelX = -shot.mVelX;
            shot.mVelY = -shot.mVelY;
        }

        private static void Hot(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (!_hotReported)
                {
                    _hotReported = true;
                    PlantKit.LogError("teitisi hot path failed: " + where, ex);
                }
            }
        }

        private static void Add(Type type, string name, Type[] args, Delegate hook)
        {
            try
            {
                PlantKit.AddHook(type, name, args, hook);
                HookCount++;
            }
            catch (Exception ex)
            {
                Failures.Add(type.Name + "." + name + ": " + ex.Message);
                PlantKit.LogError("teitisi hook failed: " + type.Name + "." + name, ex);
            }
        }
    }
}
