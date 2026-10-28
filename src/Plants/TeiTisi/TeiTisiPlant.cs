using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 水神忒提丝：把本行飞过她护盾范围内的子弹弹回去，被弹过的子弹命中僵尸会附加毒伤。
    /// 挥击动作由水神的钩子在真的弹开一颗子弹时触发。
    /// </summary>
    public sealed class TeiTisiPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "水神",
            Cost = 100,
            RefreshTime = 750,
            Health = 1000,
            MaxHealth = 1000,
            ReanimName = "teitisi",
            CardAsset = "teitisi_card",
            CardBackground = "green",
            StandAnim = "anim_idle",
            CardOffsetY = -13,
            Factory = () => new TeiTisiPlant(),
        };

        private const int IdleRate = 10;
        private const int ActionFrames = 5;
        private const int SwingTicks = 20;
        private const int SwingBlend = 20;

        // 动作段只有 5 帧但要铺满 20 tick，速率按这两个数现算，别抄常数。
        private const float SwingRate = ActionFrames / (float)SwingTicks * 100f;

        private int _poseTicks;

        public override void OnUpdate()
        {
            if (_poseTicks > 0)
            {
                _poseTicks--;
                if (_poseTicks == 0)
                {
                    Plant.PlayBodyReanim("anim_idle", ReanimLoopType.Loop,
                        (byte)SwingBlend, IdleRate);
                }
                return;
            }
            Reanimation body = Body;
            if (body != null)
            {
                body.mAnimRate = IdleRate;
            }
        }

        /// <summary>弹开一颗子弹时挥一下；正在挥就只把倒计时续上，不重播动画。</summary>
        public void StartSwing()
        {
            if (_poseTicks > 0)
            {
                _poseTicks = SwingTicks;
                return;
            }
            if (Body == null)
            {
                return;
            }
            Plant.PlayBodyReanim("anim_reflect", ReanimLoopType.PlayOnceAndHold,
                (byte)SwingBlend, SwingRate);
            _poseTicks = SwingTicks;
        }
    }
}
