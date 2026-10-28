using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class PlantBusterPack : PlantPackBase
    {
        private const string AlmanacText =
            "植物吞噬者会啃掉本格的一株植物，并把它的卡片吐出来。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}特点：{STAT}可以种在已有植物的格子上\n" +
            "{KEYWORD}目标：{STAT}同格的所有植物，一次全部回收\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}100\n" +
            "{KEYWORD}冷却：{STAT}短\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}比铲子礼貌一点：至少把卡还给你。";

        public override string PackId => "pgvz.plant_buster";

        public override int Order => 12;

        public override int HookCount => PlantBusterExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => PlantBusterExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantBusterPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(PlantBusterPlant.Def, packId);
            PlantBusterExtras.Install();
        }
    }
}
