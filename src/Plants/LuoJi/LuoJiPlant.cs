using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 火神洛基：穿过它火苗的那一截子弹会被附魔成更强的弹。
    /// 本体不攻击，只当"活的火炬树桩"，所以血量给得很厚。
    /// </summary>
    public sealed class LuoJiPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "火神",
            Cost = 125,
            RefreshTime = 750,
            Health = 4000,
            MaxHealth = 4000,
            ReanimName = "loki",
            CardAsset = "loki_card",
            CardBackground = "green",
            StandAnim = "anim_idle",
            CardOffsetY = -3,
            CardArtScale = 0.8f,
            Factory = () => new LuoJiPlant(),
        };

        private const int IdleRate = 10;

        public override void OnUpdate()
        {
            // 引擎建本体时给的是 10~15 的随机速率，钉回素材自己的 fps。
            Reanimation body = Body;
            if (body != null)
            {
                body.mAnimRate = IdleRate;
            }
        }
    }
}
