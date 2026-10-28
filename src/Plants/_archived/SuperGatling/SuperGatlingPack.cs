using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class SuperGatlingPack : PlantPackBase
    {
        private const string AlmanacText =
            "超级机枪射手会连续发射多颗随机属性豌豆。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}伤害：{STAT}因子弹类型而异\n" +
            "{KEYWORD}发射数量：{STAT}4-7颗\n" +
            "{KEYWORD}特点：{STAT}随机发射六种豌豆\n" +
            "{KEYWORD}大招：{STAT}向前方扇形区域发射180颗随机豌豆\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}400\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}更大的枪，更密集的火力，以及足以覆盖整片草坪的豌豆风暴。";

        public override string PackId => "pgvz.super_gatling";

        public override int Order => 0;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(SuperGatlingPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(SuperGatlingPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            SuperGatlingPlant.Def.AlmanacText = AlmanacText;
            SuperGatlingPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(SuperGatlingPlant.Def, packId);
        }
    }
}
