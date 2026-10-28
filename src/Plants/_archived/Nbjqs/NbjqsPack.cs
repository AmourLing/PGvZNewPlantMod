using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class NbjqsPack : PlantPackBase
    {
        private const string AlmanacText =
            "牛蒡击球手会攻击附近的僵尸，也会强化经过身边的豌豆。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}伤害：{STAT}100\n" +
            "{KEYWORD}特点：{STAT}强化普通、寒冰、火焰和石块豌豆\n" +
            "{KEYWORD}大招：{STAT}进入强化形态，下一次攻击可秒杀普通僵尸\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}100\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}只要球棒挥得够准，豌豆也能打出全垒打。";

        public override string PackId => "pgvz.nbjqs";

        public override int Order => 5;

        public override int HookCount => NbjqsExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => NbjqsExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(NbjqsPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(NbjqsPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            NbjqsPlant.Def.AlmanacText = AlmanacText;
            NbjqsPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(NbjqsPlant.Def, packId);
            NbjqsExtras.Install();
        }
    }
}
