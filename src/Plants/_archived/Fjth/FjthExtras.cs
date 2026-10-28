using System;
using System.Collections.Generic;
using Lawn;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    internal static class FjthExtras
    {
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

            Add(typeof(Zombie), "FindPlantTarget", new[] { typeof(ZombieAttackType) },
                (Func<Func<Zombie, ZombieAttackType, Plant>, Zombie, ZombieAttackType, Plant>)
                ZombieFindPlantTargetHook);
            Add(typeof(Zombie), "UpdateZamboni", Type.EmptyTypes,
                (Action<Action<Zombie>, Zombie>)ZamboniHook);
            Add(typeof(Zombie), "SquishAllInSquare",
                new[] { typeof(int), typeof(int), typeof(ZombieAttackType) },
                (Action<Action<Zombie, int, int, ZombieAttackType>, Zombie, int, int, ZombieAttackType>)
                SquishAllHook);
            Add(typeof(LawnMower), "MowZombie", new[] { typeof(Zombie) },
                (Action<Action<LawnMower, Zombie>, LawnMower, Zombie>)MowZombieHook);

            PlantKit.Log("fjth extras installed: " + HookCount + " hooks");
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
                PlantKit.LogError("fjth extra hook failed: " + type.Name + "." + name, ex);
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
                    PlantKit.LogError("fjth hot path failed: " + where, ex);
                }
            }
        }

        private static Plant ZombieFindPlantTargetHook(
            Func<Zombie, ZombieAttackType, Plant> orig, Zombie self, ZombieAttackType attackType)
        {
            return FjthPlant.IsPulled(self) ? null : orig(self, attackType);
        }

        private static void ZamboniHook(Action<Zombie> orig, Zombie self)
        {
            if (!FjthPlant.IsPulled(self))
            {
                orig(self);
                return;
            }
            Board board = self.mBoard;
            int row = self.mRow;
            int iceMin = board.mIceMinX[row];
            int iceTimer = board.mIceTimer[row];
            orig(self);
            board.mIceMinX[row] = iceMin;
            board.mIceTimer[row] = iceTimer;
        }

        private static void SquishAllHook(
            Action<Zombie, int, int, ZombieAttackType> orig,
            Zombie self, int gridX, int gridY, ZombieAttackType attackType)
        {
            if (FjthPlant.IsPulled(self))
            {
                return;
            }
            if (FjthPlant.TrySwallowRobotTitan(self, gridX, gridY))
            {
                return;
            }
            orig(self, gridX, gridY, attackType);
        }

        private static void MowZombieHook(Action<LawnMower, Zombie> orig,
            LawnMower self, Zombie zombie)
        {
            if (FjthPlant.IsPulled(zombie))
            {
                return;
            }
            orig(self, zombie);
        }
    }
}
