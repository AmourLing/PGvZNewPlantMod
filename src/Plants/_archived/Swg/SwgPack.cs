using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class SwgPack : PlantPackBase
    {
        private const string AlmanacText =
            "守卫菇会召唤小守卫菇保护附近的植物。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}生命：{STAT}3000\n" +
            "{KEYWORD}特点：{STAT}高大植物，可阻挡跳跃僵尸\n" +
            "{KEYWORD}小守卫菇：{STAT}保护并强化同格植物\n" +
            "{KEYWORD}大招：{STAT}吞噬前方3×3范围的僵尸，并补满周围守卫\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}100\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}守护不只是挡在前面，也是让身边的伙伴变得更强。";

        public override string PackId => "pgvz.swg";

        public override int Order => 8;

        public override int HookCount => SwgExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => SwgExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(SwgPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(SwgPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            SwgPlant.Def.AlmanacText = AlmanacText;
            SwgPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(SwgPlant.Def, packId);
            SwgSmallPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(SwgSmallPlant.Def, packId);
            SwgExtras.Install();
        }
    }
}
