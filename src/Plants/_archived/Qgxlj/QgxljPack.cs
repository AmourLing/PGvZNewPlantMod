using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class QgxljPack : PlantPackBase
    {
        private const string AlmanacText =
            "球果训练家会召唤不同的伙伴协助作战。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}特点：{STAT}种植后点击植物可以切换不同形态\n" +
            "{KEYWORD}大招：{STAT}依据不同形态产生不同效果\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}300\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}在山里，我常常能听到动物伙伴的声音，芝士恐龙，芝士水母，芝士马";

        public override string PackId => "pgvz.qgxlj";

        public override int Order => 4;

        public override int HookCount => QgxljExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => QgxljExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(QgxljPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(QgxljPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            QgxljPlant.Def.AlmanacText = AlmanacText;
            QgxljPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(QgxljPlant.Def, packId);
            QgxljExtras.Install();
        }
    }
}
