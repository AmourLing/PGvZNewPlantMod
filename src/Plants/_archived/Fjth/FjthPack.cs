using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class FjthPack : PlantPackBase
    {
        private const string AlmanacText =
            "伏僵塔黄会吞噬靠近前方的僵尸，随后进入休眠。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}生命：{STAT}非常高\n" +
            "{KEYWORD}特点：{STAT}吞噬前方一格的僵尸，然后进入休眠\n" +
            "{KEYWORD}大招：{STAT}吸引全场随机8只僵尸并立刻炼化，同时灼烧本行\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}225\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}镇守高塔的少女，会将闯入的敌人尽数收入法器。";

        public override string PackId => "pgvz.fjth";

        public override int Order => 1;

        public override int HookCount => FjthExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => FjthExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(FjthPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(FjthPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            FjthPlant.Def.AlmanacText = AlmanacText;
            FjthPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(FjthPlant.Def, packId);
            FjthExtras.Install();
        }
    }
}
