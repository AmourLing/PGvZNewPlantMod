using System.Collections.Generic;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    public sealed class DmxjPack : PlantPackBase
    {
        private const string AlmanacText =
            "电鳗香蕉会发动往返攻击。\n" +
            "{SHORTLINE}\n" +
            "{KEYWORD}伤害：{STAT}中等\n" +
            "{KEYWORD}特点：{STAT}穿透敌人，子弹到尽头会折返\n" +
            "{KEYWORD}大招：{STAT}秒杀前方中小型僵尸，对大型僵尸造成高额伤害。击败僵尸有几率刷新大招\n" +
            "{NORMAL}\n{KEYWORD}冷却时间：{STAT}短\n" +
            "{KEYWORD}阳光：{STAT}125\n" +
            "{SHORTLINE}\n" +
            "{FLAVOR}这条围巾戴着有点麻麻的，是保暖效果太好了吗。";

        public override string PackId => "pgvz.dmxj";

        public override int Order => 2;

        public override int HookCount => DmxjExtras.HookCount;

        public override IReadOnlyList<string> HookFailures => DmxjExtras.HookFailures;

        public override void Register(string packId)
        {
            PlantKit.RegisterEmbeddedImages(typeof(DmxjPack).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantKit.RegisterEmbeddedContent(typeof(DmxjPack).Assembly,
                "PGvZNewPlantMod.Spine.");
            DmxjPlant.Def.AlmanacText = AlmanacText;
            DmxjPlant.Def.Deprecated = true;
            PlantKit.RegisterPlant(DmxjPlant.Def, packId);
            DmxjExtras.Install();
        }
    }
}
