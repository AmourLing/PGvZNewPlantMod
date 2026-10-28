using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class DoomCannonPack : PlantPackBase
    {
        private const string AlmanacText =
            "死神大炮自己装填、自己开火，不需要玩家指点落点。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}落点：{STAT}固定砸向草坪中段\n" +
            "{KEYWORD}伤害：{STAT}与玉米加农炮相同\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}1200\n" +
            "{KEYWORD}冷却：{STAT}极长\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}它不再等人下命令，只是把炮口对准了同一个方向。";

        public override string PackId => "pgvz.doom_cannon";

        public override int Order => 13;

        public override void Register(string packId)
        {
            DoomCannonPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(DoomCannonPlant.Def, packId);
        }
    }
}
