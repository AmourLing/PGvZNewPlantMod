using System.Collections.Generic;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 卡片花：按万金鱼的节奏周期生产一张随机植物卡片。
    /// 卡池为全部原生植物（排除模仿者）加上本 mod 注册的所有可选自定义植物。
    /// </summary>
    public sealed class SeedPacketFlowerPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "卡片花",
            Cost = 200,
            RefreshTime = 3000,
            LaunchRate = 2500,
            NativeReanim = true,
            NativeReanimType = ReanimationType.Marigold,
            CardNativeSeed = SeedType.Marigold,
            HasPreview = false,
            StandAnim = string.Empty,
            Factory = () => new SeedPacketFlowerPlant(),
        };

        public override void OnPlanted()
        {
            if (Plant.IsInPlay())
            {
                Plant.mLaunchCounter = 750;
            }
        }

        public override void OnUpdate()
        {
            if (B == null || !Plant.IsInPlay())
            {
                return;
            }
            Plant.mLaunchCounter--;
            if (Plant.mLaunchCounter <= 100)
            {
                Plant.mEatenFlashCountdown =
                    System.Math.Max(Plant.mEatenFlashCountdown, Plant.mLaunchCounter);
            }
            else if (Plant.mLaunchCounter > 0)
            {
                return;
            }
            Plant.mLaunchCounter = Plant.mLaunchRate;
            SeedType chosen = PickSeed();
            if (chosen != SeedType.None)
            {
                B.AddCoin(Plant.mX, Plant.mY, CoinType.UsableSeedPacket,
                    CoinMotion.Coin).mUsableSeedType = chosen;
            }
        }

        private static SeedType PickSeed()
        {
            var pool = new List<SeedType>();
            for (int id = (int)SeedType.Peashooter; id < (int)SeedType.SeedTypeCount; id++)
            {
                if ((SeedType)id == SeedType.Imitater)
                {
                    continue;
                }
                pool.Add((SeedType)id);
            }
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable)
                {
                    pool.Add(def.Seed);
                }
            }
            return pool.Count == 0
                ? SeedType.None
                : pool[TodCommon.RandRangeInt(0, pool.Count - 1)];
        }
    }
}
