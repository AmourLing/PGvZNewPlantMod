using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class AresPack : PlantPackBase
    {
        private const string AlmanacText =
            "战神一轮齐射同时向身前和身后开火。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}前方：{STAT}6 发直射弹\n" +
            "{KEYWORD}后方：{STAT}8 发倒飞弹\n" +
            "{KEYWORD}伤害：{STAT}每发与豌豆同级\n" +
            "{NORMAL}\n{KEYWORD}出手间隔：{STAT}约 1 秒一轮\n" +
            "{KEYWORD}阳光：{STAT}200\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}两面受敌这件事，从他这里学来还太快了。";

        public override string PackId => "pgvz.ares";

        public override int Order => 15;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(AresPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(AresPack).Assembly,
                "PGvZNewPlantMod.Reanim.", "Content/reanim");
            AresPlant.Def.AlmanacText = AlmanacText;
            CustomBullets.Register(AresPlant.Bullet);
            PlantKit.RegisterPlant(AresPlant.Def, packId);
        }
    }
}
