using System;
using System.Collections.Generic;
using System.IO;
using Lawn;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 创造神的包级钩子：记录场上最后种下的植物，并让落点检查按被复制植物的规则走。
    /// 记录值写进 SaveData 侧车，重开游戏后仍能复制上一局的最后一种。
    /// </summary>
    internal static class FanTianExtras
    {
        private const string RecordFile = "fantian_last_seed.txt";

        private static readonly List<string> Failures = new List<string>();
        private static bool _installed;
        private static int _lastPlantedSeed = -1;

        public static int HookCount { get; private set; }

        public static IReadOnlyList<string> HookFailures => Failures;

        public static int LastPlantedSeed => _lastPlantedSeed;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;
            _lastPlantedSeed = ReadRecord();

            Add(typeof(Plant), "PlantInitialize",
                new[] { typeof(int), typeof(int), typeof(SeedType), typeof(SeedType) },
                (Action<Action<Plant, int, int, SeedType, SeedType>, Plant, int, int,
                    SeedType, SeedType>)PlantInitializeHook);
            Add(typeof(Board), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType), typeof(bool) },
                (Func<Func<Board, int, int, SeedType, bool, PlantingReason>, Board, int,
                    int, SeedType, bool, PlantingReason>)BoardCanPlantAtHook);
            Add(typeof(Challenge), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType) },
                (Func<Func<Challenge, int, int, SeedType, PlantingReason>, Challenge, int,
                    int, SeedType, PlantingReason>)ChallengeCanPlantAtHook);

            PlantKit.Log("fantian extras installed: " + HookCount + " hooks");
        }

        /// <summary>创造神自己没有落点规则，检查时换成它准备复制的那张卡。</summary>
        private static SeedType Substitute(SeedType seed)
        {
            return seed == FanTianPlant.Def.Seed && _lastPlantedSeed >= 0
                ? (SeedType)_lastPlantedSeed
                : seed;
        }

        private static PlantingReason BoardCanPlantAtHook(
            Func<Board, int, int, SeedType, bool, PlantingReason> orig,
            Board self, int gridX, int gridY, SeedType seed, bool isMove)
        {
            return orig(self, gridX, gridY, Substitute(seed), isMove);
        }

        private static PlantingReason ChallengeCanPlantAtHook(
            Func<Challenge, int, int, SeedType, PlantingReason> orig,
            Challenge self, int gridX, int gridY, SeedType seed)
        {
            return orig(self, gridX, gridY, Substitute(seed));
        }

        private static void PlantInitializeHook(
            Action<Plant, int, int, SeedType, SeedType> orig, Plant self,
            int gridX, int gridY, SeedType seedType, SeedType imitaterType)
        {
            orig(self, gridX, gridY, seedType, imitaterType);
            if (seedType != FanTianPlant.Def.Seed)
            {
                Record((int)seedType);
            }
        }

        private static void Record(int seedId)
        {
            if (seedId == _lastPlantedSeed)
            {
                return;
            }
            _lastPlantedSeed = seedId;
            try
            {
                string dir = CustomSave.RootDir;
                CustomSave.EnsureSidecarDir(dir);
                File.WriteAllText(Path.Combine(dir, RecordFile),
                    seedId.ToString(), System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                PlantKit.LogError("fantian record write failed", ex);
            }
        }

        private static int ReadRecord()
        {
            try
            {
                string path = Path.Combine(CustomSave.RootDir, RecordFile);
                if (File.Exists(path))
                {
                    string text = File.ReadAllText(path).Trim();
                    if (int.TryParse(text, out int seedId))
                    {
                        return seedId;
                    }
                }
            }
            catch (Exception ex)
            {
                PlantKit.LogError("fantian record read failed", ex);
            }
            return -1;
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
                PlantKit.LogError("fantian extra hook failed: " + type.Name + "." + name, ex);
            }
        }
    }
}
