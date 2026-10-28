using System;
using System.Collections.Generic;
using Lawn;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>卡片模仿者要能落在已有植物的格子上，所以直接放行落点检查。</summary>
    internal static class SeedPacketImitaterExtras
    {
        private static readonly List<string> Failures = new List<string>();
        private static bool _installed;

        public static int HookCount { get; private set; }

        public static IReadOnlyList<string> HookFailures => Failures;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;

            Add(typeof(Board), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType), typeof(bool) },
                (Func<Func<Board, int, int, SeedType, bool, PlantingReason>, Board, int,
                    int, SeedType, bool, PlantingReason>)BoardCanPlantAtHook);
            Add(typeof(Challenge), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType) },
                (Func<Func<Challenge, int, int, SeedType, PlantingReason>, Challenge, int,
                    int, SeedType, PlantingReason>)ChallengeCanPlantAtHook);

            PlantKit.Log("seedpacket imitater extras installed: " + HookCount + " hooks");
        }

        private static PlantingReason BoardCanPlantAtHook(
            Func<Board, int, int, SeedType, bool, PlantingReason> orig,
            Board self, int gridX, int gridY, SeedType seed, bool isMove)
        {
            return seed == SeedPacketImitaterPlant.Def.Seed
                ? PlantingReason.Ok
                : orig(self, gridX, gridY, seed, isMove);
        }

        private static PlantingReason ChallengeCanPlantAtHook(
            Func<Challenge, int, int, SeedType, PlantingReason> orig,
            Challenge self, int gridX, int gridY, SeedType seed)
        {
            return seed == SeedPacketImitaterPlant.Def.Seed
                ? PlantingReason.Ok
                : orig(self, gridX, gridY, seed);
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
                PlantKit.LogError("seedpacket imitater hook failed: " +
                    type.Name + "." + name, ex);
            }
        }
    }
}
