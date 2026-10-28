using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 瓷砖萝卜：把所在格变成可串联大招的瓷砖，种下后本体消失。
    /// 棋盘状态（已有瓷砖/预留格/累计种植数）挂在 Board 上，供 UI 与存档共用。
    /// </summary>
    public sealed class CzlbPlant : CustomPlantBase
    {
        public sealed class BoardState
        {
            public readonly HashSet<(int x, int y)> Tiles = new HashSet<(int x, int y)>();
            public readonly HashSet<(int x, int y)> Reserved = new HashSet<(int x, int y)>();
            public int PlantedCount;
        }

        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 6,
            SpineName = "czlb",
            Cost = 0,
            RefreshTime = 750,
            LaunchRate = 60,
            Name = "瓷砖萝卜",
            CardOffsetY = -14f,
            CardAsset = "czlb_card",
            AlmanacRow = 15,
            AlmanacColumn = 4,
            ChooserColumn = 3,
            ChooserRow = 14,
            StandAnim = "anim_atand",
            StandLoop = ReanimLoopType.PlayOnceAndHold,
            PreviewAnim = "anim_atand",
            PreviewScale = 0.48f,
            PreviewDx = 25f,
            PreviewDy = 74f,
            BodyScale = 1.8f,
            BodyDx = -100f,
            BodyDy = -248f,
            BodyRenderOffset = 2,
            CostProvider = board => NextCost(board),
            ExcludeFromConveyor = board =>
            {
                BoardState state = GetBoardState(board);
                int limit = 9 * (board.StageHasPool() ? 6 : 5);
                return state.Tiles.Count + state.Reserved.Count +
                    board.mSeedBank.CountOfTypeOnConveyorBelt(Def.Seed) >= limit;
            },
            ExcludeFromImitaterPool = (board, x, y) =>
            {
                BoardState state = GetBoardState(board);
                return state.Tiles.Contains((x, y)) || state.Reserved.Contains((x, y));
            },
            Factory = () => new CzlbPlant(),
        };

        private static readonly ConditionalWeakTable<Board, BoardState> Boards =
            new ConditionalWeakTable<Board, BoardState>();

        private static readonly string SaveDir =
            System.IO.Path.Combine(CustomSave.RootDir, "czlb_tiles");

        public const int TileChainDelayFrames = 30;

        public static BoardState GetBoardState(Board board)
        {
            if (board == null)
            {
                return new BoardState();
            }
            return Boards.GetValue(board, _ => new BoardState());
        }

        public static int NextCost(Board board)
        {
            if (board == null)
            {
                return 0;
            }
            int count = GetBoardState(board).PlantedCount;
            return count <= 0 ? 0 : 25 * (1 << (count - 1));
        }

        private string _mode = "stand";

        public override void OnPlanted()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            BoardState state = GetBoardState(board);
            state.PlantedCount++;
            state.Reserved.Add((Plant.mPlantCol, Plant.mRow));
        }

        public override void OnUpdate()
        {
            if (_mode == "stand")
            {
                if (AnimDone)
                {
                    _mode = "attack";
                    Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 30f);
                }
                return;
            }
            if (_mode == "attack" && AnimDone)
            {
                _mode = "complete";
                CompleteTile();
            }
        }

        private void CompleteTile()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            BoardState state = GetBoardState(board);
            (int x, int y) cell = (Plant.mPlantCol, Plant.mRow);
            state.Reserved.Remove(cell);
            state.Tiles.Add(cell);
            Plant.Die();
        }

        public override void OnAlmanacPreview()
        {
            Play("anim_atand", ReanimLoopType.Loop, 30f);
        }

        public override void OnRemoved()
        {
            Board board = B;
            if (board != null)
            {
                GetBoardState(board).Reserved.Remove((Plant.mPlantCol, Plant.mRow));
            }
        }

        public static void DrawTiles(Board board, Graphics g)
        {
            if (board == null)
            {
                return;
            }
            BoardState state = GetBoardState(board);
            if (state.Tiles.Count == 0)
            {
                return;
            }
            Image image = AssetStore.Get("czlb_tile");
            foreach ((int x, int y) cell in state.Tiles)
            {
                int drawX = board.GridToPixelX(cell.x, cell.y);
                int drawY = board.GridToPixelY(cell.x, cell.y);
                g.DrawImage(image, drawX, drawY, 80, 100);
            }
        }

        public static void Save(Board board, string filePath)
        {
            BoardState state = GetBoardState(board);
            CustomSave.EnsureSidecarDir(SaveDir);
            List<string> cells = new List<string>();
            foreach ((int x, int y) cell in state.Tiles)
            {
                cells.Add(cell.x + "," + cell.y);
            }
            cells.Sort(StringComparer.Ordinal);
            CustomSave.WriteSidecar(SaveDir, filePath, ".tiles",
                state.PlantedCount + "|" + string.Join(";", cells));
        }

        public static void Load(Board board, string filePath)
        {
            string text = CustomSave.ReadSidecar(SaveDir, filePath, ".tiles");
            if (text == null)
            {
                return;
            }
            string[] values = text.Split('|');
            if (values.Length < 2)
            {
                ModLog.Error("invalid czlb tile sidecar: " + filePath);
                return;
            }
            BoardState state = GetBoardState(board);
            state.Tiles.Clear();
            if (!string.IsNullOrEmpty(values[1]))
            {
                foreach (string cell in values[1].Split(';'))
                {
                    string[] pair = cell.Split(',');
                    if (pair.Length == 2 && int.TryParse(pair[0], out int x) &&
                        int.TryParse(pair[1], out int y))
                    {
                        state.Tiles.Add((x, y));
                    }
                }
            }
            state.PlantedCount = int.TryParse(values[0], out int count) ? count : 0;
            state.Reserved.Clear();
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (!plant.mDead && (int)plant.mSeedType == Def.SeedId)
                {
                    state.Reserved.Add((plant.mPlantCol, plant.mRow));
                }
            }
        }
    }
}
