using System;
using System.Globalization;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 一种自定义植物的静态元数据。C# 开发者可以直接 new 后赋值；
    /// IronPython 开发者也用同样的公开字段，不需要重新编译 DLL。
    /// </summary>
    public sealed class CustomPlantDef
    {
        public int SeedId;
        public string Name = "Custom Plant";
        public string PackId;
        public string AlmanacText = string.Empty;
        // 0 = 自动分配；显式填 >0 的图鉴行/列可覆盖。
        public int AlmanacRow;
        public int AlmanacColumn;
        // -1 = 自动分配；显式填 >=0 的选卡列/行可覆盖。
        public int ChooserColumn = -1;
        public int ChooserRow = -1;
        /// <summary>同包内自动排版的顺序，越小越靠前；相同则按注册顺序。</summary>
        public int ChooserOrder;

        public int Cost;
        public int RefreshTime = 750;
        public int PacketIndex = 54;
        /// <summary>Shooter 会走原生 UpdateShooter/FindTargetAndFire；投手也用 Shooter 并自行处理弹道。</summary>
        public PlantSubClass SubClass = PlantSubClass.Normal;
        public int LaunchRate;
        /// <summary>0 表示沿用原生默认血量。</summary>
        public int Health;
        public int MaxHealth;
        /// <summary>Hjbl 式：刚进关卡就能用，种下后才开始冷却。</summary>
        public bool ReadyImmediately;
        public bool Selectable = true;
        /// <summary>只标记不改行为：图鉴加注记，仍可种植、老存档仍可读。</summary>
        public bool Deprecated;

        /// <summary>由原生 PlantInitialize 创建身体（不替换成自定义 Spine）。</summary>
        public bool NativeReanim;
        /// <summary>复用原生动画；填 ReanimationType.None 表示使用 ReanimSlot 指向的自定义 Spine。</summary>
        public ReanimationType NativeReanimType = ReanimationType.None;
        /// <summary>自定义 Spine 槽位（从 ReanimBase 起算）；NativeReanimType 非 None 时忽略。</summary>
        public int ReanimSlot = -1;
        public string SpineName;
        /// <summary>经典逐帧动画的资源名，对应内嵌的 reanim/&lt;ReanimName&gt;.reanim 与同名帧图；与 SpineName 二选一。</summary>
        public string ReanimName;
        /// <summary>小守卫菇这类需要让原生初始化用另一个种子创建的实体。</summary>
        public SeedType NativeSeedOverride = SeedType.None;

        public string CardAsset;
        /// <summary>
        /// 卡图是裸立绘（没有卡槽框）时，先垫一张框架自带的通用背景，立绘/动画叠在上面。
        /// 普通卡用 "green"；"purple" 留给需要种在另一张卡上升级的卡（机枪射手那类），
        /// "gray" 是卡图集里那张灰色空白卡，一般不用声明 ——
        /// 模仿者选卡栏（ImitaterDialog）里所有卡都是过一遍 WashedOut 滤镜变灰的，
        /// 我们的卡也走同一滤镜，不需要换卡。留空表示卡图自带卡槽框。
        /// </summary>
        public string CardBackground;
        public float CardOffsetY = -23f;
        /// <summary>卡上立绘/动画的缩放，1 = 原生大小。</summary>
        public float CardArtScale = 1f;
        /// <summary>卡上立绘/动画的额外位移，叠加在 CardOffsetY 与原生基准偏移之上。</summary>
        public float CardArtDx;
        public float CardArtDy;
        /// <summary>没有自定义卡图时，用这张原生卡作为底图。</summary>
        public SeedType CardNativeSeed = SeedType.None;

        public bool HasPreview = true;
        public string StandAnim = "anim_stand";
        public string PreviewAnim = "anim_stand";
        public ReanimLoopType StandLoop = ReanimLoopType.Loop;
        public float PreviewScale;
        public float PreviewDx;
        public float PreviewDy;
        public float BodyScale = 1f;
        public float BodyDx;
        public float BodyDy;
        public bool HasCursorOverride;
        public float CursorScale;
        public float CursorDx;
        public float CursorDy;
        /// <summary>身体偏移是否在运行时乘 Constants.S（早期版本在 Load480x800 之前导入）。</summary>
        public bool BodyUsesS;
        public int BodyRenderOffset = 1;

        /// <summary>C# 用 () =&gt; new MyPlant()；IronPython 用 System.Func[CustomPlantBase](lambda: MyPlant())。</summary>
        public Func<CustomPlantBase> Factory;
        /// <summary>动态阳光（如瓷砖萝卜）。为空时用 Cost。</summary>
        public Func<Board, int> CostProvider;
        /// <summary>选卡时是否排除该植物（例如达到上限的瓷砖萝卜）。</summary>
        public Func<Board, bool> ExcludeFromConveyor;
        public Func<Board, int, int, bool> ExcludeFromImitaterPool;
        /// <summary>
        /// 种植位置过滤器：返回 False 的格子不可种（格子高亮与预览也会跟着消失）。
        /// IronPython 写法：
        ///   DEF.CanPlantAt = System.Func[Board, int, int, bool](lambda b, x, y: x &gt;= 3)
        /// </summary>
        public Func<Board, int, int, bool> CanPlantAt;

        // ------------------------------------------------------------ 借原生身份
        // 引擎里有一批"按 mSeedType 特判"的小灶（白天睡觉、开火定格几帧、挂什么粒子、
        // 影子多大、播哪条语音、能不能被升级卡覆盖、算不算真菌）。自定义种子不在那些
        // 硬编码名单里，一条都拿不到，所以这里逐项声明要借哪个原生种子的身份。
        // 全部默认"不借"，不填就和以前完全一样。

        /// <summary>像蘑菇一样：白天关卡睡觉、可被咖啡豆唤醒，图鉴按夜间植物标注。</summary>
        public bool Nocturnal;
        /// <summary>开火后射击动作定格多少帧。0 = 用引擎兜底值（借大喷菇动画时是 29），
        /// 原生大喷菇是 50、小喷菇 29、胆小菇 25。必须在 FindTargetAndFire 之后覆盖：
        /// 在引擎那个方法里换种子会让它回调到本株的 Fire，等于把伤害也换成了借来的那份。</summary>
        public int ShootingCounter;
        /// <summary>开火定格那段的播放速率（帧/秒），覆盖引擎兜底分支写死的 35。
        /// 原生忧郁菇是 14，四连吐打到第 126 帧，用 35 的话动画先定住、枪还在响。
        /// 只动身体动画（引擎的 PlayBodyReanim 就是打在 mBodyReanimID 上）。</summary>
        public float ShootingAnimRate;
        /// <summary>语音借用哪个原生种子（种植/被吃/睡觉/死亡）。引擎的语音表只有 65 格，
        /// 自定义种子会越界，所以不填就是现在这样：不出声。</summary>
        public SeedType VoiceSeed = SeedType.None;
        /// <summary>只在外形/特效里借哪个原生种子的身份：影子缩放与偏移、睡姿偏移、
        /// 以及开火特效（原生大喷菇那团 Fumecloud 是硬编码在 UpdateShooting 的种子分支里）。
        /// 这几处引擎只画不打分，所以可以整段临时换种子跑原方法，不用抄坐标。</summary>
        public SeedType DrawAsSeed = SeedType.None;
        /// <summary>可被这张升级卡覆盖（原生大喷菇对应忧郁菇）。</summary>
        public SeedType UpgradesTo = SeedType.None;
        /// <summary>
        /// 可被"哪张模组升级卡"覆盖 —— 按植物名写，不是按种子号：模组种子的 SeedId 要到
        /// 首次排版才分配，脚本互相引用时拿不到，而名字是稳定键（存档也用名字）。
        /// 引擎那边的配对是 <c>IsUpgradableTo(卡种子)</c>，钩子运行时按名字查表。
        /// </summary>
        public string UpgradesToName;
        /// <summary>
        /// 这张卡是升级卡：只能点到"能被它升级"的那株上。填了它，空地会由游戏自己
        /// 回 <c>PlantingReason.NeedsUpgrade</c>（和忧郁菇/机枪豌豆那 9 张一致），
        /// 挑战模式的"场上有没有可升级的株"筛选也一并跟着走。
        /// </summary>
        public bool IsUpgradeCard;
        /// <summary>算不算"真菌"：影响"只用蘑菇+咖啡"这类挑战判定和相关成就。</summary>
        public bool IsFungus;
        /// <summary>
        /// 身体动画常驻染色，写十六进制，如 <c>"#66ccff"</c>（也接受不带 # 和 8 位含透明）。
        /// 引擎里这份待遇是按种子写死的（爆炸坚果 (255,64,64)、冰墙果 (64,160,255)，
        /// 见 Plant.UpdateReanimColor），自定义种子不在名单里，所以在这里声明补回去。
        /// 留空 = 不染。手里举着能升级本株的卡时，引擎那下闪烁高亮仍然优先于它。
        /// </summary>
        public string BodyTint;
        /// <summary>
        /// 本株开火喷出来的粒子（大喷菇那团 Fumecloud、忧郁菇的 Gloomcloud 这类）也染这个色。
        /// 走的是引擎自己的 <c>TodParticleSystem.OverrideColor</c>，染的是"这株自己挂上来的
        /// 那团特效"（<c>Plant.mParticleID</c>），每帧重贴；脚本里另外用
        /// <c>mApp.AddTodParticle</c> 生成的粒子要自己调 OverrideColor。留空 = 不染。
        /// </summary>
        public string ParticleTint;

        // 以下标记由 Register 根据初始值自动设置，调用方不需要填。
        public bool AutoSeedId;
        public bool AutoAlmanac;
        public bool AutoChooser;
        public bool AutoReanimSlot;

        public SeedType Seed => (SeedType)SeedId;

        /// <summary>
        /// 这株原生 Plant 是不是用本定义注册出来的。脚本里写 <c>DEF.IsMine(self)</c>，
        /// 不要另写全局 is_mine()：所有植物脚本共享同一个全局命名空间，通用名字会互相覆盖。
        /// 自定义种子没有枚举名，SeedId 要到首次排版才自动分配；分配前（&lt;=0）一律不认领，
        /// 否则 SeedType(0) 恰好是豌豆射手，会把原生豌豆误当成自己。
        /// 借身份时钩子会临时把 <c>mSeedType</c> 换成借来的种子（忧郁菇的 UpdateShooting
        /// 就在 orig 里回调本株的 Fire），那一刻按种子比会认不出自己 —— 所以再按对象查一次注册表。
        /// </summary>
        public bool IsMine(Plant plant)
        {
            if (plant == null)
            {
                return false;
            }
            return (SeedId > 0 && plant.mSeedType == Seed) ||
                ReferenceEquals(CustomPlantRegistry.Get(plant)?.Definition, this);
        }

        /// <summary>
        /// 解析 <see cref="BodyTint"/> 那种十六进制写法：允许前后空白、可带 #，
        /// 6 位是 RGB、8 位是 RGBA。写错了返回 false 并给纯白，不抛异常 ——
        /// 这是作者填的配置，注册期自检会单独告一句，不能让一株植物把整局带崩。
        /// </summary>
        public static bool TryParseTint(string theValue, out SexyColor theColor)
        {
            theColor = SexyColor.White;
            string text = (theValue ?? string.Empty).Trim();
            if (text.StartsWith("#"))
            {
                text = text.Substring(1);
            }
            if (text.Length != 6 && text.Length != 8)
            {
                return false;
            }
            int[] part = new int[text.Length / 2];
            for (int i = 0; i < part.Length; i++)
            {
                if (!int.TryParse(text.Substring(i * 2, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out part[i]))
                {
                    return false;
                }
            }
            theColor = part.Length == 3
                ? new SexyColor(part[0], part[1], part[2])
                : new SexyColor(part[0], part[1], part[2], part[3]);
            return true;
        }

        /// <summary>需要占用一个自定义动画槽位（Spine 或经典 .reanim）。</summary>
        public bool UsesCustomAnimation => NativeReanimType == ReanimationType.None;
        public bool UsesCustomSpine => UsesCustomAnimation && !string.IsNullOrEmpty(SpineName);
        public bool UsesCustomReanim => UsesCustomAnimation && !string.IsNullOrEmpty(ReanimName);
        public bool HasBodyTransform => BodyScale != 1f || BodyDx != 0f || BodyDy != 0f;
        public ReanimationType ReanimType => NativeReanimType != ReanimationType.None
            ? NativeReanimType
            : (ReanimationType)(CustomPlantRegistry.ReanimBase + ReanimSlot);
    }
}
