using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class SsbpsPack : PlantPackBase
    {
        private const string AlmanacText =
            "桑葚爆破手会投掷具有不同效果的爆弹。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}伤害：{STAT}大\n" +
            "{KEYWORD}特点：{STAT}特殊爆炸会波及3×3范围内的僵尸，并产生不同效果\n" +
            "{KEYWORD}大招：{STAT}向场上随机6只僵尸投掷爆弹\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}150\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}我准备了一些妙趣横生的手雷，这会产生一些妙趣横生的效果";

        public override string PackId => "pgvz.ssbps";

        public override int Order => 3;

        public override int HookCount => SsbpsExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => SsbpsExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(SsbpsPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(SsbpsPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            SsbpsPlant.Def.AlmanacText = AlmanacText;
            SsbpsPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(SsbpsPlant.Def, packId);
            SsbpsExtras.Install();
        }
    }
}
