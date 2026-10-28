using System;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>黄金蓓蕾：绽放后一次性产出 15 个阳光并消失。</summary>
    public sealed class HjblPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 7,
            SpineName = "hjbl",
            Cost = 0,
            RefreshTime = 1800,
            ReadyImmediately = true,
            LaunchRate = 0,
            Name = "黄金蓓蕾",
            CardOffsetY = -16f,
            CardAsset = "hjbl_card",
            AlmanacText = string.Empty,
            AlmanacRow = 16,
            AlmanacColumn = 1,
            ChooserColumn = 0,
            ChooserRow = 15,
            StandAnim = "anim_attack",
            StandLoop = ReanimLoopType.PlayOnceAndHold,
            PreviewAnim = "anim_stand",
            PreviewScale = 0.46f,
            PreviewDx = 28f,
            PreviewDy = 80f,
            BodyScale = 1.65f,
            BodyDx = -98f,
            BodyDy = -210f,
            BodyRenderOffset = 2,
            Factory = () => new HjblPlant(),
        };

        private bool _sunCreated;

        public override void OnAlmanacPreview()
        {
            _sunCreated = true;
            Play("anim_stand", ReanimLoopType.Loop, 30f);
        }

        public override void OnPlanted()
        {
            _sunCreated = false;
        }

        public override void OnUpdate()
        {
            if (_sunCreated)
            {
                return;
            }
            if (!AnimDone)
            {
                return;
            }
            _sunCreated = true;
            Board board = B;
            if (board != null)
            {
                for (int i = 0; i < 15; i++)
                {
                    board.AddCoin(
                        Plant.mX + 10 + (i % 5) * 12,
                        Plant.mY + 10 + (i / 5) * 8,
                        CoinType.Sun, CoinMotion.FromPlant);
                }
            }
            Plant.Die();
        }
    }
}
