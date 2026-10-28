using System;
using System.Collections.Generic;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 火神的附魔：子弹穿过火苗那一截就换弹。
    /// 附魔＝换类型/换注册记录，所以"一颗只附一次"天然成立，不需要给子弹记号。
    /// </summary>
    internal static class LuoJiExtras
    {
        /// <summary>强化弹：战神那颗 20 伤的单发换成 40 伤并带同排溅射。</summary>
        public static readonly CustomBulletDef EnhancedShot = new CustomBulletDef
        {
            Id = EnhancedShotId,
            Carrier = ProjectileType.Pea,
            Image = "ares_shot_enhanced",
            Damage = 40,
            SplashDamage = 20,
            SplashWidth = 100,
        };

        public const string EnhancedShotId = "pgvz.loki:ares_shot_enhanced";

        // 源注册弹 → 目标注册弹。别的包的弹按 ID 字符串引用（不建工程引用），
        // 那个包没启用时这条自然匹配不上，不会报错。
        private static readonly (string From, string To)[] BulletMap =
        {
            ("pgvz.ares:ares_shot", EnhancedShotId),
        };

        // 原生弹种直接交给引擎自己的转化：豌豆/寒冰豌豆/星星火 → 火球（40 伤害 + 自带燃烧表现）。
        private static readonly ProjectileType[] FireTypes =
        {
            ProjectileType.Pea,
            ProjectileType.Snowpea,
            ProjectileType.Star,
        };

        // 附魔窗口照火炬树桩自己的攻击矩形：TRect(mX + 50, mY, 30, mHeight)；
        // 弹的矩形是 (mX - 15, 宽 mWidth + 15)，右边正好到 mPosX + mWidth。
        private const int RectDx = 50;
        private const int RectWidth = 30;
        private const int BulletPad = 15;

        private static readonly Dictionary<int, List<Plant>> RowLuoJi =
            new Dictionary<int, List<Plant>>();

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

            Add(typeof(Board), "Update", Type.EmptyTypes,
                (Action<Action<Board>, Board>)BoardUpdateHook);
            Add(typeof(Projectile), "Update", Type.EmptyTypes,
                (Action<Action<Projectile>, Projectile>)ProjectileUpdateHook);

            PlantKit.Log("luoji extras installed: " + HookCount + " hooks");
        }

        private static void BoardUpdateHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("row registry", () => RebuildRows(self));
        }

        private static void ProjectileUpdateHook(Action<Projectile> orig, Projectile self)
        {
            orig(self);
            Hot("enchant", () => TryEnchant(self));
        }

        private static void RebuildRows(Board board)
        {
            foreach (List<Plant> list in RowLuoJi.Values)
            {
                list.Clear();
            }
            SeedType seed = LuoJiPlant.Def.Seed;
            foreach (Plant plant in board.mPlants)
            {
                if (plant.mDead || plant.mSeedType != seed)
                {
                    continue;
                }
                if (!RowLuoJi.TryGetValue(plant.mRow, out List<Plant> list))
                {
                    list = new List<Plant>();
                    RowLuoJi[plant.mRow] = list;
                }
                list.Add(plant);
            }
        }

        private static void TryEnchant(Projectile shot)
        {
            if (shot.mDead || !RowLuoJi.TryGetValue(shot.mRow, out List<Plant> list) ||
                list.Count == 0)
            {
                return;
            }
            CustomBulletDef bullet = CustomBullets.Of(shot);
            if (bullet == null && Array.IndexOf(FireTypes, shot.mProjectileType) < 0)
            {
                return;
            }
            foreach (Plant luoji in list)
            {
                if (!InFlameRect(shot, luoji))
                {
                    continue;
                }
                if (bullet != null)
                {
                    foreach ((string from, string to) in BulletMap)
                    {
                        if (string.Equals(bullet.Id, from, StringComparison.OrdinalIgnoreCase))
                        {
                            CustomBullets.Convert(shot, to);
                        }
                    }
                    return;
                }
                shot.ConvertToFireball();
                return;
            }
        }

        private static bool InFlameRect(Projectile shot, Plant luoji)
        {
            float left = luoji.mX + RectDx;
            float shotLeft = shot.mPosX - BulletPad;
            float shotRight = shot.mPosX + shot.mWidth;
            return Math.Min(shotRight, left + RectWidth) - Math.Max(shotLeft, left) >= 1f;
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
                    PlantKit.LogError("luoji hot path failed: " + where, ex);
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
                PlantKit.LogError("luoji hook failed: " + type.Name + "." + name, ex);
            }
        }
    }
}
