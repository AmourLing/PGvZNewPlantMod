using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class CzlbPack : PlantPackBase
    {
        private const string AlmanacText =
            "瓷砖萝卜会把所在格转化为能够传递大招的瓷砖。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}特点：{STAT}可种在草坪、水池、屋顶或已有植物所在格\n" +
            "{KEYWORD}瓷砖：{STAT}让其他瓷砖上的植物依距离依次释放大招\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}每次种植后翻倍\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}排好瓷砖，力量就会沿着花纹一路传过去。";

        public override string PackId => "pgvz.czlb";

        public override int Order => 6;

        public override int HookCount => CzlbExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => CzlbExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(CzlbPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(CzlbPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            CzlbPlant.Def.AlmanacText = AlmanacText;
            CzlbPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(CzlbPlant.Def, packId);
            CzlbExtras.Install();
        }
    }
}
