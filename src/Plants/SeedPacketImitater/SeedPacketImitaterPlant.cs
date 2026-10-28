using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 卡片模仿者：种在任意格上，变身后吐出本格植物的卡片。
    /// 优先级为普通植物 &gt; 南瓜壳 &gt; 底座（花盆/睡莲） &gt; 飞行植物。
    /// </summary>
    public sealed class SeedPacketImitaterPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "卡片模仿者",
            Cost = 150,
            RefreshTime = 750,
            NativeReanim = true,
            NativeReanimType = ReanimationType.Imitater,
            CardNativeSeed = SeedType.Imitater,
            // 只借模仿者的立绘，卡框用普通卡的绿底。
            CardBackground = "green",
            HasPreview = false,
            StandAnim = string.Empty,
            Factory = () => new SeedPacketImitaterPlant(),
        };

        public override void OnPlanted()
        {
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
                SeedType copied = FindCellMate();
                if (copied != SeedType.None)
                {
                    B.AddCoin(Plant.mX, Plant.mY, CoinType.UsableSeedPacket,
                        CoinMotion.Coin).mUsableSeedType = copied;
                }
                Plant.Die();
            }
        }

        private SeedType FindCellMate()
        {
            SeedType normal = SeedType.None;
            SeedType flying = SeedType.None;
            SeedType under = SeedType.None;
            SeedType pumpkin = SeedType.None;
            foreach (Plant other in B.mPlants.ToArray())
            {
                if (other == Plant || other.mDead ||
                    other.mPlantCol != Plant.mPlantCol || other.mRow != Plant.mRow)
                {
                    continue;
                }
                SeedType seed = other.mSeedType;
                if (Plant.IsFlying(seed))
                {
                    flying = seed;
                }
                else if (seed == SeedType.Flowerpot || seed == SeedType.Lilypad)
                {
                    under = seed;
                }
                else if (seed == SeedType.Pumpkinshell)
                {
                    pumpkin = seed;
                }
                else
                {
                    normal = seed;
                }
            }
            if (normal != SeedType.None)
            {
                return normal;
            }
            if (pumpkin != SeedType.None)
            {
                return pumpkin;
            }
            return under != SeedType.None ? under : flying;
        }
    }
}
