using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 花火龙：种下 5 秒后开始三响烟花，每一响掉 8 朵阳光，放完自己退场。
    /// 动画是内嵌的经典逐帧 .reanim（hhl），待机速率钉成素材自己的 10 帧/秒。
    /// </summary>
    public sealed class HuaHuoLongPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "花火龙",
            Cost = 150,
            RefreshTime = 750,
            LaunchRate = 500,
            ReanimName = "hhl",
            CardAsset = "hhl_card",
            CardBackground = "green",
            StandAnim = "anim_idle",
            CardOffsetY = -12,
            CardArtScale = 0.91f,
            Factory = () => new HuaHuoLongPlant(),
        };

        private const int IdleRate = 10;
        private const int BurstRate = 10;
        private const int ActionFrames = 22;
        private const int BurstBlend = 20;
        private const int SunsPerBurst = 8;

        // 三响各在动作段的第几帧炸开（素材帧 18 / 21 / 26 折算到动作段起点）。
        private static readonly int[] BurstAtFrames = { 7, 10, 15 };

        // 一 tick 走 rate/100 帧，所以"动作段第 f 帧"落在 f * 100 / rate 这一 tick。
        private const int TicksPerFrame = 100 / BurstRate;
        private const int ShowTicks = ActionFrames * TicksPerFrame;

        private int _tick = -1;
        private int _burst;

        public override void OnPlanted()
        {
            if (Plant.IsInPlay())
            {
                Plant.mLaunchCounter = Plant.mLaunchRate;
            }
        }

        public override void OnUpdate()
        {
            if (B == null || !Plant.IsInPlay())
            {
                return;
            }
            if (_tick < 0)
            {
                UpdateWaiting();
                return;
            }
            UpdateShow();
        }

        private void UpdateWaiting()
        {
            Plant.mLaunchCounter--;
            if (Plant.mLaunchCounter > 0)
            {
                // 引擎建本体时给的是随机速率，逐帧钉死才不会一圈快一圈慢。
                Reanimation body = Body;
                if (body != null)
                {
                    body.mAnimRate = IdleRate;
                }
                return;
            }
            _tick = 0;
            _burst = 0;
            Plant.PlayBodyReanim("anim_burst", ReanimLoopType.PlayOnceAndHold,
                (byte)BurstBlend, BurstRate);
        }

        private void UpdateShow()
        {
            _tick++;
            // 用 >= 而不是 ==：偶发跳帧也不会把某一响整发漏掉。
            while (_burst < BurstAtFrames.Length &&
                   _tick >= BurstAtFrames[_burst] * TicksPerFrame)
            {
                _burst++;
                SpawnSuns();
            }
            if (_tick >= ShowTicks)
            {
                Plant.Die();
            }
        }

        private void SpawnSuns()
        {
            for (int i = 0; i < SunsPerBurst; i++)
            {
                B.AddCoin(
                    Plant.mX + TodCommon.RandRangeInt(0, 80),
                    Plant.mY + TodCommon.RandRangeInt(0, 40),
                    CoinType.Sun, CoinMotion.Coin);
            }
        }
    }
}
