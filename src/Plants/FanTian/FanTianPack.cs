using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class FanTianPack : PlantPackBase
    {
        private const string AlmanacText =
            "创造神会复制本局最后种下的那一种植物。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}数量：{STAT}本格加相邻格共三株\n" +
            "{KEYWORD}特点：{STAT}落点按被复制植物的规则判断\n" +
            "{NORMAL}\n{KEYWORD}阳光：{STAT}325\n" +
            "{KEYWORD}冷却：{STAT}很长\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}它偷走了最后一张卡的模样，而且一次偷了三份。";

        public override string PackId => "pgvz.fantian";

        public override int Order => 9;

        public override int HookCount => FanTianExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => FanTianExtras.HookFailures;

        public override void Register(string packId)
        {
            FanTianPlant.Def.AlmanacText = AlmanacText;
            PlantKit.RegisterPlant(FanTianPlant.Def, packId);
            FanTianExtras.Install();
        }
    }
}
