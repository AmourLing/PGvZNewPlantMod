using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class TeiTisiPack : PlantPackBase
    {
        private const string AlmanacText =
            "水神把穿过她护盾的子弹弹回去，弹回去的子弹带着毒。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}范围：{STAT}本行，身前后 20 到身位 +80\n" +
            "{KEYWORD}可弹：{STAT}豌豆、星星果、仙人掌刺（只弹直线/倒飞/星星轨迹）\n" +
            "{KEYWORD}毒伤：{STAT}命中后 3 刀 × 100，每秒一刀\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}100\n" +
            "{KEYWORD}血量：{STAT}1000\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}她说：你扔过来的东西，我原样还你，还多送一点。";

        public override string PackId => "pgvz.teitisi";

        public override int Order => 18;

        public override int HookCount => TeiTisiExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => TeiTisiExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(TeiTisiPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(TeiTisiPack).Assembly,
                "PGvZNewPlantMod.Reanim.", "Content/reanim");
            TeiTisiPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(TeiTisiPlant.Def, packId);
            TeiTisiExtras.Install();
        }
    }
}
