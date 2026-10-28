using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class SeedPacketImitaterPack : PlantPackBase
    {
        private const string AlmanacText =
            "卡片模仿者会把本格植物的种子吐回给你。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}特点：{STAT}可以种在任何已有植物的格子上\n" +
            "{KEYWORD}产出：{STAT}本格植物的一张可用卡片\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}150\n" +
            "{KEYWORD}冷却：{STAT}短\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}它不会变成植物，只会变成那张卡。";

        public override string PackId => "pgvz.seedpacket_imitater";

        public override int Order => 10;

        public override int HookCount => SeedPacketImitaterExtras.HookCount;

        public override IReadOnlyList<string> HookFailures =>
            SeedPacketImitaterExtras.HookFailures;

        public override void Register(string packId)
        {
            SeedPacketImitaterPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(SeedPacketImitaterPlant.Def, packId);
            SeedPacketImitaterExtras.Install();
        }
    }
}
