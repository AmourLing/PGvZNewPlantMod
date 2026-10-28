using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class HjblPack : PlantPackBase
    {
        private const string AlmanacText =
            "黄金蓓蕾会在绽放后产生大量阳光。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}特点：{STAT}种植后产生375阳光，然后消失\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}30秒\n" +
            "{KEYWORD}阳光：{STAT}0\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}金色的花蕾里，藏着一整个晴天。";

        public override string PackId => "pgvz.hjbl";

        public override int Order => 7;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(HjblPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(HjblPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            HjblPlant.Def.AlmanacText = AlmanacText;
            HjblPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(HjblPlant.Def, packId);
        }
    }
}
