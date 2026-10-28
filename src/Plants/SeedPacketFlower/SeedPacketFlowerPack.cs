using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class SeedPacketFlowerPack : PlantPackBase
    {
        private const string AlmanacText =
            "卡片花像万金鱼一样定时产出，但掉的是卡片。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}产出：{STAT}一张随机植物的可用卡片\n" +
            "{KEYWORD}卡池：{STAT}全部原生植物（除模仿者）与本 mod 的可选植物\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}200\n" +
            "{KEYWORD}冷却：{STAT}很长\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}没人知道下一朵会开出谁的卡。";

        public override string PackId => "pgvz.seedpacket_flower";

        public override int Order => 11;

        public override void Register(string packId)
        {
            SeedPacketFlowerPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(SeedPacketFlowerPlant.Def, packId);
        }
    }
}
