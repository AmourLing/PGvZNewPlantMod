using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 植物吞噬者：像墓碑吞噬者一样落地、啃食，把本格的植物吃掉并吐出它的卡片。
    /// </summary>
    public sealed class PlantBusterPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "植物吞噬者",
            Cost = 100,
            RefreshTime = 750,
            NativeReanim = true,
            NativeReanimType = ReanimationType.GraveBuster,
            CardNativeSeed = SeedType.Gravebuster,
            HasPreview = false,
            StandAnim = string.Empty,
            Factory = () => new PlantBusterPlant(),
        };

        public override void OnPlanted()
        {
            if (!Plant.IsInPlay())
            {
                return;
            }
            Plant.mY += 8;
            Reanimation body = Body;
            if (body != null)
            {
                body.SetFramesForLayer("anim_land");
                body.mLoopType = ReanimLoopType.PlayOnceAndHold;
            }
            Plant.mState = PlantState.GravebusterLanding;
        }

        public override void OnUpdate()
        {
            if (B == null || !Plant.IsInPlay())
            {
                return;
            }
            Reanimation body = Body;
            if (Plant.mState == PlantState.GravebusterLanding)
            {
                if (body != null && body.mLoopCount > 0)
                {
                    Plant.PlayBodyReanim("anim_idle", ReanimLoopType.Loop, 10, 12f);
                    Plant.mStateCountdown = 400;
                    Plant.mState = PlantState.GravebusterEating;
                }
                return;
            }
            if (Plant.mState != PlantState.GravebusterEating || Plant.mStateCountdown > 0)
            {
                return;
            }
            foreach (Plant other in B.mPlants.ToArray())
            {
                if (other == Plant || other.mDead ||
                    other.mPlantCol != Plant.mPlantCol || other.mRow != Plant.mRow)
                {
                    continue;
                }
                B.AddCoin(Plant.mX, Plant.mY, CoinType.UsableSeedPacket,
                    CoinMotion.Coin).mUsableSeedType = other.mSeedType;
                other.Die();
            }
            Plant.Die();
        }
    }
}
