using System;
using System.Collections.Generic;
using System.IO;
using Lawn;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    internal static class SwgExtras
    {
        private static readonly List<Plant> GuardPending = new List<Plant>();
        private static readonly string SwgDir =
            Path.Combine(CustomSave.RootDir, "swg_small");
        private static bool _installed;
        private static readonly HashSet<string> HotErrors = new HashSet<string>();

        public static int HookCount { get; private set; }
        public static readonly List<string> HookFailures = new List<string>();

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;

            Add(typeof(Plant), "Fire", new[] { typeof(Zombie), typeof(int), typeof(PlantWeapon) },
                (Action<Action<Plant, Zombie, int, PlantWeapon>, Plant, Zombie, int, PlantWeapon>)
                PlantFireHook);
            Add(typeof(Plant), "FindTargetZombie", new[] { typeof(int), typeof(PlantWeapon) },
                (Func<Func<Plant, int, PlantWeapon, Zombie>, Plant, int, PlantWeapon, Zombie>)
                PlantFindTargetZombieHook);
            Add(typeof(Plant), "DoRowAreaDamage", new[] { typeof(int), typeof(uint) },
                (Action<Action<Plant, int, uint>, Plant, int, uint>)PlantDoRowAreaDamageHook);
            Add(typeof(Zombie), "FindPlantTarget", new[] { typeof(ZombieAttackType) },
                (Func<Func<Zombie, ZombieAttackType, Plant>, Zombie, ZombieAttackType, Plant>)
                ZombieFindPlantTargetHook);
            Add(typeof(Zombie), "UpdateZombiePolevaulter", Type.EmptyTypes,
                (Action<Action<Zombie>, Zombie>)PolevaulterHook);
            Add(typeof(Zombie), "UpdateZombiePogo", Type.EmptyTypes,
                (Action<Action<Zombie>, Zombie>)PogoHook);
            Add(typeof(Board), "UpdateGame", Type.EmptyTypes,
                (Action<Action<Board>, Board>)BoardUpdateGameHook);

            PlantKit.RegisterSaveHandler(HasExtraData, SaveExtra, LoadExtra);
            PlantKit.UltimateStarted += OnUltimateStarted;
            PlantKit.Log("swg extras installed: " + HookCount + " hooks");
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
                HookFailures.Add(type.Name + "." + name + ": " + ex.Message);
                PlantKit.LogError("swg extra hook failed: " + type.Name + "." + name, ex);
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
                    PlantKit.LogError("swg hot path failed: " + where, ex);
                }
            }
        }

        private static bool IsFumeFamily(Plant plant)
        {
            return plant != null && (plant.mSeedType == SeedType.Puffshroom ||
                plant.mSeedType == SeedType.Fumeshroom ||
                plant.mSeedType == SeedType.Scaredyshroom);
        }

        private static void PlantFireHook(
            Action<Plant, Zombie, int, PlantWeapon> orig,
            Plant self, Zombie target, int row, PlantWeapon weapon)
        {
            Board board = self.mBoard;
            if (board != null && IsFumeFamily(self) &&
                SwgPlant.FindSmallAt(board, self.mPlantCol, self.mRow) != null)
            {
                var before = new HashSet<Projectile>(board.mProjectiles);
                orig(self, target, row, weapon);
                for (int i = 0; i < board.mProjectiles.Count; i++)
                {
                    Projectile projectile = board.mProjectiles[i];
                    if (!before.Contains(projectile))
                    {
                        CustomProjectiles.MarkSwgRepeat(projectile);
                    }
                }
                return;
            }
            orig(self, target, row, weapon);
        }

        private static Zombie PlantFindTargetZombieHook(
            Func<Plant, int, PlantWeapon, Zombie> orig, Plant self, int row, PlantWeapon weapon)
        {
            Board board = self.mBoard;
            if (board != null && IsFumeFamily(self) &&
                SwgPlant.FindSmallAt(board, self.mPlantCol, self.mRow) != null)
            {
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (!zombie.mDead && !zombie.IsDeadOrDying() &&
                        zombie.mRow == self.mRow && zombie.mPosX >= self.mX)
                    {
                        return zombie;
                    }
                }
            }
            return orig(self, row, weapon);
        }

        private static void PlantDoRowAreaDamageHook(
            Action<Plant, int, uint> orig, Plant self, int damage, uint flags)
        {
            Board board = self.mBoard;
            if (self.mSeedType == SeedType.Fumeshroom && board != null &&
                SwgPlant.FindSmallAt(board, self.mPlantCol, self.mRow) != null)
            {
                uint damageFlags = (uint)self.GetDamageRangeFlags(PlantWeapon.Primary);
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (!zombie.mDead && !zombie.IsDeadOrDying() &&
                        zombie.mRow == self.mRow && zombie.mPosX >= self.mX &&
                        zombie.EffectedByDamage(damageFlags))
                    {
                        zombie.TakeDamage(damage * 2, flags);
                    }
                }
                return;
            }
            orig(self, damage, flags);
        }

        private static Plant ZombieFindPlantTargetHook(
            Func<Zombie, ZombieAttackType, Plant> orig, Zombie self, ZombieAttackType attackType)
        {
            Plant target = orig(self, attackType);
            if (target == null || target.mBoard == null)
            {
                return target;
            }
            Plant protector = SwgPlant.FindSmallAt(
                target.mBoard, target.mPlantCol, target.mRow);
            return protector != null && protector != target ? protector : target;
        }

        private static void PolevaulterHook(Action<Zombie> orig, Zombie self)
        {
            SwgPlant.RunAsTallnut(self.mBoard, () => orig(self));
        }

        private static void PogoHook(Action<Zombie> orig, Zombie self)
        {
            SwgPlant.RunAsTallnut(self.mBoard, () => orig(self));
        }

        private static void BoardUpdateGameHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("Board.UpdateGame.swg", () => UpdatePending(self));
        }

        private static void OnUltimateStarted(Plant source, bool propagateTiles, string sourceKind)
        {
            if (source == null || source.mBoard == null)
            {
                return;
            }
            Board board = source.mBoard;
            if ((sourceKind == "manual" || sourceKind == "tile") &&
                SwgPlant.FindSmallAt(board, source.mPlantCol, source.mRow) != null &&
                !GuardPending.Contains(source))
            {
                GuardPending.Add(source);
            }
        }

        private static void UpdatePending(Board board)
        {
            for (int i = GuardPending.Count - 1; i >= 0; i--)
            {
                Plant plant = GuardPending[i];
                if (plant == null || plant.mDead || plant.mBoard != board)
                {
                    GuardPending.RemoveAt(i);
                    continue;
                }
                if (SwgPlant.FindSmallAt(board, plant.mPlantCol, plant.mRow) == null)
                {
                    GuardPending.RemoveAt(i);
                    continue;
                }
                CustomPlantBase behavior = CustomPlantRegistry.Get(plant);
                if (behavior != null && behavior.CanUseUltimate)
                {
                    CustomUltimate.StartSelected(plant, false, "guard_repeat");
                    GuardPending.RemoveAt(i);
                }
            }
        }

        private static bool HasExtraData(Board board)
        {
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                if (!board.mPlants[i].mDead && SwgPlant.IsSmallPlant(board.mPlants[i]))
                {
                    return true;
                }
            }
            return false;
        }

        private static void SaveExtra(Board board, string filePath)
        {
            var cells = new List<string>();
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (!plant.mDead && SwgPlant.IsSmallPlant(plant))
                {
                    cells.Add(plant.mPlantCol + "," + plant.mRow);
                }
            }
            CustomSave.WriteSidecar(SwgDir, filePath, ".small", string.Join(";", cells));
        }

        private static void LoadExtra(Board board, string filePath)
        {
            LoadSwgSmallState(board, filePath);
        }

        private static void LoadSwgSmallState(Board board, string filePath)
        {
            string text = CustomSave.ReadSidecar(SwgDir, filePath, ".small");
            var cells = new HashSet<(int x, int y)>();
            if (text != null)
            {
                foreach (string cell in text.Split(';'))
                {
                    string[] pair = cell.Split(',');
                    if (pair.Length == 2 && int.TryParse(pair[0], out int x) &&
                        int.TryParse(pair[1], out int y))
                    {
                        cells.Add((x, y));
                    }
                }
            }
            if (cells.Count == 0)
            {
                var guards = new List<(int x, int y)>();
                for (int i = 0; i < board.mPlants.Count; i++)
                {
                    Plant plant = board.mPlants[i];
                    if (!plant.mDead && (int)plant.mSeedType == SwgPlant.Def.SeedId)
                    {
                        guards.Add((plant.mPlantCol, plant.mRow));
                    }
                }
                for (int i = 0; i < board.mPlants.Count; i++)
                {
                    Plant plant = board.mPlants[i];
                    if (plant.mDead || plant.mSeedType != SeedType.Pumpkinshell)
                    {
                        continue;
                    }
                    foreach ((int gx, int gy) guard in guards)
                    {
                        if (Math.Abs(plant.mPlantCol - guard.gx) <= 1 &&
                            Math.Abs(plant.mRow - guard.gy) <= 1)
                        {
                            cells.Add((plant.mPlantCol, plant.mRow));
                            break;
                        }
                    }
                }
            }
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (!plant.mDead && plant.mSeedType == SeedType.Pumpkinshell &&
                    cells.Contains((plant.mPlantCol, plant.mRow)) &&
                    !SwgPlant.IsSmallPlant(plant))
                {
                    SwgSmallPlant behavior = new SwgSmallPlant();
                    CustomPlantRegistry.AttachBehavior(plant, behavior, SwgSmallPlant.Def);
                    behavior.SetupAttachedPlant();
                }
            }
        }
    }
}
