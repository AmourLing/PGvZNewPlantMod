using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class ChocolateCannonPack : PlantPackBase
    {
        private const string AlmanacText =
            "巧克力大炮自己蓄力、自己开火，炮弹砸向草坪中段。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}落点：{STAT}固定，不需要玩家指点\n" +
            "{KEYWORD}伤害：{STAT}单发 900，够打死的那部分直接化灰\n" +
            "{KEYWORD}溅射：{STAT}落点半径 95、上下各一行\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}500\n" +
            "{KEYWORD}冷却：{STAT}很长\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}甜的东西炸起来，比谁都安静。";

        public override string PackId => "pgvz.chocolate_cannon";

        public override int Order => 16;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(ChocolateCannonPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(ChocolateCannonPack).Assembly,
                "PGvZNewPlantMod.Reanim.", "Content/reanim");
            ChocolateCannonPlant.Def.AlmanacText = AlmanacText;
            ChocolateCannonPlant.Def.Deprecated = true;
            CustomBullets.Register(ChocolateShell.Def);
            PlantKit.RegisterPlant(ChocolateCannonPlant.Def, packId);
        }
    }
}
