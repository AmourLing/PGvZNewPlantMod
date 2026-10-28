using System;
using System.Collections.Generic;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 创造神：变身结束后，把上一张种下的植物在本格和相邻格各复制一株，共三株。
    /// 复制目标在种下时记录，随存档侧车写回，重启后仍能找到目标。
    /// </summary>
    public sealed class FanTianPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "创造神",
            Cost = 325,
            RefreshTime = 3000,
            NativeReanim = true,
            NativeReanimType = ReanimationType.Imitater,
            CardNativeSeed = SeedType.Imitater,
            // 只借模仿者的立绘，卡框用普通卡的绿底；模仿者自己的卡仍是原生灰底。
            CardBackground = "green",
            HasPreview = false,
            StandAnim = string.Empty,
            Factory = () => new FanTianPlant(),
        };

        private static readonly int[][] NeighborOffsets =
        {
            new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, 0 }, new[] { 1, 0 },
            new[] { -1, -1 }, new[] { -1, 1 }, new[] { 1, -1 }, new[] { 1, 1 },
        };

        private int _copied = -1;

        public override void OnPlanted()
        {
            _copied = FanTianExtras.LastPlantedSeed;
            Reanimation body = Body;
            if (body != null)
            {
                body.mAnimRate = 25f + 5f * TodCommon.RandRangeFloat(0f, 1f);
            }
            Plant.mStateCountdown = 100;
        }

        public override void OnUpdate()
        {
            if (B == null || !Plant.IsInPlay())
            {
                return;
            }
            if (Plant.mState != PlantState.ImitaterMorphing)
            {
                if (Plant.mStateCountdown <= 0)
                {
                    Plant.mState = PlantState.ImitaterMorphing;
                    Plant.mStateCountdown = 200;
                    Plant.PlayBodyReanim("anim_explode",
                        ReanimLoopType.PlayOnceAndHold, 0, 26f);
                }
                return;
            }
            Reanimation body = Body;
            if (body != null && body.mLoopCount > 0)
            {
                // 复制失败也必须结束自己：状态停在 ImitaterMorphing 的话每帧都会重试，
                // 一次越界异常就能把日志刷到几百万行。
                try
                {
                    SpawnCopies();
                }
                finally
                {
                    Plant.Die();
                }
            }
        }

        private void SpawnCopies()
        {
            if (_copied < 0)
            {
                return;
            }
            SeedType seed = (SeedType)_copied;
            int col = Plant.mPlantCol;
            int row = Plant.mRow;

            var placed = new List<int[]> { new[] { col, row } };
            B.AddPlant(col, row, seed, SeedType.None);
            for (int i = 0; i < NeighborOffsets.Length && placed.Count < 3; i++)
            {
                int nextCol = col + NeighborOffsets[i][0];
                int nextRow = row + NeighborOffsets[i][1];
                // 原生 CanPlantAt 不挡越界坐标；直接 AddPlant 会在 GridToPixelY 里
                // 用负数行号索引 mGridSquareType 抛 IndexOutOfRange。
                if (nextCol < 0 || nextCol >= Sexy.Constants.GRIDSIZEX ||
                    nextRow < 0 || nextRow >= Sexy.Constants.MAX_GRIDSIZEY)
                {
                    continue;
                }
                if (B.CanPlantAt(nextCol, nextRow, seed, false) != PlantingReason.Ok)
                {
                    continue;
                }
                B.AddPlant(nextCol, nextRow, seed, SeedType.None);
                placed.Add(new[] { nextCol, nextRow });
            }

            if (!Plant.IsUpgrade(seed))
            {
                return;
            }
            // 升级植物顶替旧植物：把被覆盖格上的前身清掉，否则一格会叠两层。
            foreach (Plant other in B.mPlants.ToArray())
            {
                if (other == Plant || other.mDead || !other.IsUpgradableTo(seed))
                {
                    continue;
                }
                foreach (int[] spot in placed)
                {
                    if (other.mPlantCol == spot[0] && other.mRow == spot[1])
                    {
                        other.Die();
                        break;
                    }
                }
            }
        }
    }
}
