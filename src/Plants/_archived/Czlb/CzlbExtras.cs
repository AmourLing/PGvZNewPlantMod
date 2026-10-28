using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    internal static class CzlbExtras
    {
        private sealed class PendingCast
        {
            public Plant Plant;
            public int Due;
        }

        private static readonly List<PendingCast> TilePending = new List<PendingCast>();
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

            Add(typeof(Board), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType), typeof(bool) },
                (Func<Func<Board, int, int, SeedType, bool, PlantingReason>, Board, int, int, SeedType, bool, PlantingReason>)
                BoardCanPlantAtHook);
            Add(typeof(Challenge), "CanPlantAt",
                new[] { typeof(int), typeof(int), typeof(SeedType) },
                (Func<Func<Challenge, int, int, SeedType, PlantingReason>, Challenge, int, int, SeedType, PlantingReason>)
                ChallengeCanPlantAtHook);
            Add(typeof(Board), "UpdateGame", Type.EmptyTypes,
                (Action<Action<Board>, Board>)BoardUpdateGameHook);
            Add(typeof(Board), "DrawBackdrop", new[] { typeof(Graphics) },
                (Action<Action<Board, Graphics>, Board, Graphics>)BoardDrawBackdropHook);

            PlantKit.RegisterSaveHandler(HasExtraData, SaveExtra, LoadExtra);
            PlantKit.UltimateStarted += OnUltimateStarted;
            PlantKit.Log("czlb extras installed: " + HookCount + " hooks");
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
                PlantKit.LogError("czlb extra hook failed: " + type.Name + "." + name, ex);
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
                    PlantKit.LogError("czlb hot path failed: " + where, ex);
                }
            }
        }

        private static bool IsCzlb(SeedType seed)
        {
            return (int)seed == CzlbPlant.Def.SeedId;
        }

        private static PlantingReason CzlbReason(Board board, int gridX, int gridY)
        {
            if (board == null || gridX < 0 || gridX >= 9 || gridY < 0 || gridY >= 6)
            {
                return PlantingReason.NotHere;
            }
            CzlbPlant.BoardState state = CzlbPlant.GetBoardState(board);
            if (state.Tiles.Contains((gridX, gridY)) ||
                state.Reserved.Contains((gridX, gridY)))
            {
                return PlantingReason.NotHere;
            }
            return PlantingReason.Ok;
        }

        private static PlantingReason BoardCanPlantAtHook(
            Func<Board, int, int, SeedType, bool, PlantingReason> orig,
            Board self, int gridX, int gridY, SeedType seed, bool isMove)
        {
            return IsCzlb(seed)
                ? CzlbReason(self, gridX, gridY)
                : orig(self, gridX, gridY, seed, isMove);
        }

        private static PlantingReason ChallengeCanPlantAtHook(
            Func<Challenge, int, int, SeedType, PlantingReason> orig,
            Challenge self, int gridX, int gridY, SeedType seed)
        {
            return IsCzlb(seed)
                ? CzlbReason(self.mBoard, gridX, gridY)
                : orig(self, gridX, gridY, seed);
        }

        private static void BoardUpdateGameHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("Board.UpdateGame.czlb", () => UpdatePending(self));
        }

        private static void BoardDrawBackdropHook(
            Action<Board, Graphics> orig, Board self, Graphics g)
        {
            orig(self, g);
            Hot("Board.DrawBackdrop.czlb", () => CzlbPlant.DrawTiles(self, g));
        }

        private static void OnUltimateStarted(Plant source, bool propagateTiles, string sourceKind)
        {
            if (source == null || source.mBoard == null || !propagateTiles)
            {
                return;
            }
            Board board = source.mBoard;
            CzlbPlant.BoardState tiles = CzlbPlant.GetBoardState(board);
            (int x, int y) origin = (source.mPlantCol, source.mRow);
            if (!tiles.Tiles.Contains(origin))
            {
                return;
            }
            var queued = new HashSet<Plant>();
            foreach (PendingCast item in TilePending)
            {
                if (item.Plant != null)
                {
                    queued.Add(item.Plant);
                }
            }
            foreach ((int x, int y) cell in tiles.Tiles)
            {
                if (cell == origin)
                {
                    continue;
                }
                Plant target = FindCustomUltimatePlantAt(board, cell.x, cell.y);
                if (target == null || queued.Contains(target))
                {
                    continue;
                }
                int distance = Math.Abs(cell.x - origin.x) +
                               Math.Abs(cell.y - origin.y);
                TilePending.Add(new PendingCast
                {
                    Plant = target,
                    Due = board.mMainCounter +
                          distance * CzlbPlant.TileChainDelayFrames,
                });
            }
        }

        private static Plant FindCustomUltimatePlantAt(Board board, int gridX, int gridY)
        {
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant candidate = board.mPlants[i];
                if (candidate.mDead || candidate.mSquished ||
                    candidate.mPlantCol != gridX || candidate.mRow != gridY ||
                    IsCzlb(candidate.mSeedType))
                {
                    continue;
                }
                CustomPlantBase behavior = CustomPlantRegistry.Get(candidate);
                if (behavior != null && behavior.CanUseUltimate)
                {
                    return candidate;
                }
            }
            return null;
        }

        private static void UpdatePending(Board board)
        {
            for (int i = TilePending.Count - 1; i >= 0; i--)
            {
                PendingCast item = TilePending[i];
                Plant plant = item.Plant;
                if (plant == null || plant.mDead || plant.mBoard != board)
                {
                    TilePending.RemoveAt(i);
                    continue;
                }
                if (board.mMainCounter < item.Due)
                {
                    continue;
                }
                CustomPlantBase behavior = CustomPlantRegistry.Get(plant);
                if (behavior != null && behavior.CanUseUltimate)
                {
                    CustomUltimate.StartSelected(plant, false, "tile");
                    TilePending.RemoveAt(i);
                }
            }
        }

        private static bool HasExtraData(Board board)
        {
            CzlbPlant.BoardState state = CzlbPlant.GetBoardState(board);
            return state.Tiles.Count > 0 || state.PlantedCount > 0;
        }

        private static void SaveExtra(Board board, string filePath)
        {
            CzlbPlant.Save(board, filePath);
        }

        private static void LoadExtra(Board board, string filePath)
        {
            CzlbPlant.Load(board, filePath);
        }
    }
}
