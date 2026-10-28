using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class LuoJiPack : PlantPackBase
    {
        private const string AlmanacText =
            "火神会给穿过它火苗的子弹附魔，相当于一个能站住的火炬树桩。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}豌豆／寒冰豌豆／星星火：{STAT}转成火球\n" +
            "{KEYWORD}战神的弹：{STAT}强化弹，40 伤害并溅射同排\n" +
            "{KEYWORD}窗口：{STAT}身子右侧那一截火苗\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}125\n" +
            "{KEYWORD}血量：{STAT}4000\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}经过它的手，连一颗豌豆都会烧起来。";

        public override string PackId => "pgvz.loki";

        public override int Order => 17;

        public override int HookCount => LuoJiExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => LuoJiExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(LuoJiPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(LuoJiPack).Assembly,
                "PGvZNewPlantMod.Reanim.", "Content/reanim");
            LuoJiPlant.Def.AlmanacText = AlmanacText;
            CustomBullets.Register(LuoJiExtras.EnhancedShot);
            PlantKit.RegisterPlant(LuoJiPlant.Def, packId);
            LuoJiExtras.Install();
        }
    }
}
