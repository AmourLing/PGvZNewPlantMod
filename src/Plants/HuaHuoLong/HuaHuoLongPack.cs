using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class HuaHuoLongPack : PlantPackBase
    {
        private const string AlmanacText =
            "花火龙放一轮三响烟花，然后自己退场。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}阳光：{STAT}每响 8 朵，共 24 朵\n" +
            "{KEYWORD}起手：{STAT}种下后约 5 秒开始\n" +
            "{KEYWORD}特点：{STAT}放完消失，不占格子\n" +
            "{NORMAL}\n{KEYWORD}种植冷却：{STAT}短\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}它把整条身子都点着了，只为那三下。";

        public override string PackId => "pgvz.hua_huo_long";

        public override int Order => 14;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(HuaHuoLongPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(HuaHuoLongPack).Assembly,
                "PGvZNewPlantMod.Reanim.", "Content/reanim");
            HuaHuoLongPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(HuaHuoLongPlant.Def, packId);
        }
    }
}
