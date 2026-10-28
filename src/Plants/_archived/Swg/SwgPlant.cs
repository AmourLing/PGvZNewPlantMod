using System;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>守卫菇：定时在周围召唤小守卫菇；大招吞掉前方 3x3 敌人并补满守卫。</summary>
    public sealed class SwgPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 8,
            SpineName = "swg",
            Cost = 100,
            RefreshTime = 750,
            LaunchRate = 0,
            Name = "守卫菇",
            CardOffsetY = -11f,
            CardAsset = "swg_card",
            AlmanacRow = 16,
            AlmanacColumn = 2,
            ChooserColumn = 1,
            ChooserRow = 15,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 0.46f,
            PreviewDx = 35f,
            PreviewDy = 82f,
            BodyScale = 1.35f,
            BodyDx = -25f,
            BodyDy = -20f,
            BodyRenderOffset = 2,
            Health = 3000,
            Factory = () => new SwgPlant(),
        };

        public const int SummonInterval = 600;
        public const int UltimateMaxTargets = 5;
        public const int UltimateHeavyDamage = 800;

        private string _mode = "stand";
        private int _nextSummon;

        public override void OnPlanted()
        {
            _mode = "stand";
            _nextSummon = Frame + SummonInterval;
        }

        public override void OnUpdate()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            if (_mode == "ultimate")
            {
                if (!AnimDone)
                {
                    return;
                }
                (var planted, var empty) = CandidateCells();
                foreach ((int x, int y) cell in planted)
                {
                    SummonSmall(Plant, cell.x, cell.y);
                }
                foreach ((int x, int y) cell in empty)
                {
                    SummonSmall(Plant, cell.x, cell.y);
                }
                _mode = "stand";
                _nextSummon = Frame + SummonInterval;
                Play("anim_stand", ReanimLoopType.Loop, 30f);
                return;
            }
            if (Frame < _nextSummon)
            {
                return;
            }
            (var plantedCells, var emptyCells) = CandidateCells();
            var candidates = plantedCells.Count > 0 ? plantedCells : emptyCells;
            if (candidates.Count > 0)
            {
                (int x, int y) chosen = candidates[TodCommon.RandRangeInt(0, candidates.Count - 1)];
                SummonSmall(Plant, chosen.x, chosen.y);
            }
            _nextSummon = Frame + SummonInterval;
        }

        public override bool CanUseUltimate => _mode != "ultimate";

        public override bool UltimateActive => _mode == "ultimate";

        public override void StartUltimate()
        {
            _mode = "ultimate";
            Zombie[] targets = FindUltimateTargets(Plant, UltimateMaxTargets);
            foreach (Zombie zombie in targets)
            {
                if (zombie.mZombieType == ZombieType.Boss || CustomCombat.IsGiantZombie(zombie))
                {
                    zombie.TakeDamage(UltimateHeavyDamage, 0u);
                }
                else
                {
                    zombie.TakeDamage(CustomCombat.ZombieEffectiveHealth(zombie) + 1, 0u);
                }
            }
            Play("anim_sa", ReanimLoopType.PlayOnceAndHold, 30f);
        }

        private (System.Collections.Generic.List<(int x, int y)> planted,
                 System.Collections.Generic.List<(int x, int y)> empty) CandidateCells()
        {
            var planted = new System.Collections.Generic.List<(int x, int y)>();
            var empty = new System.Collections.Generic.List<(int x, int y)>();
            Board board = B;
            if (board == null)
            {
                return (planted, empty);
            }
            (int dx, int dy)[] offsets =
            {
                (-1, -1), (0, -1), (1, -1),
                (-1, 0), (1, 0),
                (-1, 1), (0, 1), (1, 1),
            };
            foreach ((int dx, int dy) offset in offsets)
            {
                int gridX = Plant.mPlantCol + offset.dx;
                int gridY = Plant.mRow + offset.dy;
                if (gridX < 0 || gridX > 8 || gridY < 0 || gridY >= 6)
                {
                    continue;
                }
                if (FindSmallAt(board, gridX, gridY) != null)
                {
                    continue;
                }
                bool hasPlant = false;
                for (int i = 0; i < board.mPlants.Count; i++)
                {
                    Plant other = board.mPlants[i];
                    if (!other.mDead && !IsSmallPlant(other) &&
                        other.mPlantCol == gridX && other.mRow == gridY)
                    {
                        hasPlant = true;
                        break;
                    }
                }
                (hasPlant ? planted : empty).Add((gridX, gridY));
            }
            return (planted, empty);
        }

        public static bool IsSmallPlant(Plant plant)
        {
            if (plant == null)
            {
                return false;
            }
            if ((int)plant.mSeedType == SwgSmallPlant.Def.SeedId)
            {
                return true;
            }
            CustomPlantBase behavior = CustomPlantRegistry.Get(plant);
            return behavior != null && behavior.Definition != null &&
                   behavior.Definition.SeedId == SwgSmallPlant.Def.SeedId;
        }

        public static Plant FindSmallAt(Board board, int gridX, int gridY)
        {
            if (board == null)
            {
                return null;
            }
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant candidate = board.mPlants[i];
                if (!candidate.mDead && !candidate.mSquished &&
                    IsSmallPlant(candidate) &&
                    candidate.mPlantCol == gridX && candidate.mRow == gridY)
                {
                    return candidate;
                }
            }
            return null;
        }

        public static void SummonSmall(Plant guard, int gridX, int gridY)
        {
            Board board = guard?.mBoard;
            if (board == null || FindSmallAt(board, gridX, gridY) != null)
            {
                return;
            }
            Plant small = board.AddPlant(gridX, gridY, SeedType.Pumpkinshell, SeedType.None);
            if (small == null)
            {
                return;
            }
            SwgSmallPlant behavior = new SwgSmallPlant();
            CustomPlantRegistry.AttachBehavior(small, behavior, SwgSmallPlant.Def);
            behavior.SetupAttachedPlant();
        }

        private static Zombie[] FindUltimateTargets(Plant plant, int maxCount)
        {
            var targets = new System.Collections.Generic.List<Zombie>();
            Board board = plant.mBoard;
            if (board == null)
            {
                return targets.ToArray();
            }
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying())
                {
                    continue;
                }
                int zombieCol = board.PixelToGridXKeepOnBoard((int)zombie.mPosX, (int)zombie.mPosY);
                if (plant.mPlantCol + 1 <= zombieCol && zombieCol <= plant.mPlantCol + 3 &&
                    Math.Abs(zombie.mRow - plant.mRow) <= 1)
                {
                    targets.Add(zombie);
                    if (targets.Count >= maxCount)
                    {
                        break;
                    }
                }
            }
            return targets.ToArray();
        }

        /// <summary>撑杆/跳跳僵尸判定期间把守卫菇假扮成高坚果，避免被跳过。</summary>
        public static void RunAsTallnut(Board board, Action action)
        {
            var changed = new System.Collections.Generic.List<Plant>();
            if (board != null)
            {
                for (int i = 0; i < board.mPlants.Count; i++)
                {
                    Plant plant = board.mPlants[i];
                    if (!plant.mDead && (int)plant.mSeedType == Def.SeedId)
                    {
                        changed.Add(plant);
                        plant.mSeedType = SeedType.Tallnut;
                    }
                }
            }
            try
            {
                action();
            }
            finally
            {
                foreach (Plant plant in changed)
                {
                    plant.mSeedType = (SeedType)Def.SeedId;
                }
            }
        }
    }

    /// <summary>小守卫菇：本体是南瓜壳，但身体/行为全部替换；保护并强化同格植物。</summary>
    public sealed class SwgSmallPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 9,
            SpineName = "swg_small",
            Cost = 0,
            RefreshTime = 0,
            LaunchRate = 0,
            Name = "小守卫菇",
            CardAsset = "swg_card",
            AlmanacRow = 0,
            AlmanacColumn = 0,
            ChooserColumn = 0,
            ChooserRow = 0,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            HasPreview = false,
            Selectable = false,
            BodyScale = 1.44f,
            BodyDx = -26f,
            BodyDy = -18f,
            BodyRenderOffset = 20,
            Health = 600,
            NativeSeedOverride = SeedType.Pumpkinshell,
            Factory = () => new SwgSmallPlant(),
        };

        public const int HeavyDamage = 400;

        private string _mode = "stand";
        private Zombie _target;

        public override bool OnSquish()
        {
            Reanimation body = Body;
            body?.ReanimationDie();
            Plant.mPlantHealth = 0;
            Plant.Die();
            return true;
        }

        public override void OnUpdate()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            if (_mode == "attack")
            {
                if (!AnimDone)
                {
                    return;
                }
                if (_target != null && !_target.mDead && !_target.IsDeadOrDying())
                {
                    if (_target.mZombieType == ZombieType.Boss || CustomCombat.IsGiantZombie(_target))
                    {
                        _target.TakeDamage(HeavyDamage, 0u);
                    }
                    else
                    {
                        _target.TakeDamage(CustomCombat.ZombieEffectiveHealth(_target) + 1, 0u);
                    }
                }
                _mode = "stand";
                _target = null;
                Play("anim_stand", ReanimLoopType.Loop, 30f);
                return;
            }
            Zombie target = FindTarget(Plant);
            if (target != null)
            {
                _mode = "attack";
                _target = target;
                Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 30f);
            }
        }

        private static Zombie FindTarget(Plant plant)
        {
            Board board = plant.mBoard;
            if (board == null)
            {
                return null;
            }
            int targetX = plant.mPlantCol + 1;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mRow != plant.mRow)
                {
                    continue;
                }
                int zombieCol = board.PixelToGridXKeepOnBoard((int)zombie.mPosX, (int)zombie.mPosY);
                if (plant.mPlantCol <= zombieCol && zombieCol <= targetX)
                {
                    return zombie;
                }
            }
            return null;
        }
    }
}
