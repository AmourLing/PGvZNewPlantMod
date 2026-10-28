# PGvZPlantKit

PGvZ 自定义植物框架库（net6.0，无具体植物）。其他 C# mod 或 IronPython mod 通过它注册
种子、Spine 动画、行为类、卡片和图鉴，不需要复制九植物 mod 的代码。

## 1. 安装

随 `PGvZNewPlantMod` 发布时，本 DLL 位于 `mods\PGvZNewPlantMod\PGvZPlantKit.dll`，
由顶层的 `PGvZNewPlantMod.dll` 在启动时解析并加载，再调用 `PlantKit.Install()`。
九种植物是 `mods\PGvZNewPlantMod\plants\` 下的 9 个插件 DLL，通过 `IPlantPack` 自注册。
不要把 kit 或插件 DLL 单独放到 `mods` 顶层：游戏加载器会把顶层每个 DLL 当成 mod 入口。

单独使用（自己的 C# mod 或自己的 Python 宿主）时：

- C# mod 在 `csproj` 里引用 `PGvZPlantKit.csproj` 或 DLL，并保证运行时能解析到它；
  在入口调用 `PlantKit.Install()`。
- IronPython 宿主需要先把 kit 程序集载入引擎。`PGvZNewPlantMod` 宿主已经预加载，
  放在 `plants\` 里的脚本可以直接 `from PGvZPlantKit import ...`；
  其他引擎里用 `clr.AddReference('PGvZPlantKit')`。

```csharp
PlantKit.Install();
PlantKit.RegisterPlant(def);
```

```python
import clr
clr.AddReference('PGvZPlantKit')
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase, CustomProjectiles
PlantKit.Install()
```

### 1.1 跨平台路径

Windows 和 Android 的游戏都会把当前工作目录切到可写存储根；mod 位于
`<工作目录>/mods/PGvZNewPlantMod`。宿主在 `PlantKit.Install()` 之前调用：

```csharp
PlantKit.SetModDirectory(modFolder);
```

库随后用它做三件事：

- `PlantKit.ModDirectory` 可查询当前 mod 目录；日志和侧车存档也写在这里。
- 自定义 Spine 找 `Content/spanim/...` 时，如果游戏自己的 `TitleContainer` 打不开，
  会先查 `RegisterEmbeddedContent` 注册的内嵌资源，再回退到 mod 目录的 `spanim/`、
  当前工作目录和程序集目录。因此同一份 mod 目录在 Windows
  （`%AppData%\ZBC\PlantGirlsVsZombies\mods`）和 Android
  （`Android/data/net.pvz.pgvz.zbcteam/files/mods`）都能加载 Spine，不需要改 APK。
- 九种植物已经用 `RegisterEmbeddedContent` 把 Spine 嵌进各自 DLL；外部文件仍可作为覆盖。
- `AssetStore` 仍然只负责内嵌/注册的图片，不依赖平台路径。

## 2. 最小 C# 植物

```csharp
public sealed class MyPlant : CustomPlantBase
{
    public static readonly CustomPlantDef Def = new CustomPlantDef
    {
        SeedId = 0, // 0 = 自动分配（默认从 94 起）
        Name = "我的植物",
        Cost = 100,
        RefreshTime = 750,
        Health = 300,
        NativeReanim = true,
        NativeReanimType = ReanimationType.Peashooter, // 复用原生动画
        HasPreview = false,
        CardNativeSeed = SeedType.Peashooter,          // 复用原生卡图
        StandAnim = "",                                // 复用原生动画时留空
        // ChooserRow/ChooserColumn 默认 -1、AlmanacRow/AlmanacColumn 默认 0 = 自动排版
        Factory = () => new MyPlant(),
    };

    public override void OnPlanted() { }
    public override void OnUpdate() { }
}
```

启动时：

```csharp
PlantKit.Install();
PlantKit.RegisterPlant(MyPlant.Def, "my.pack");
```

### 2.1 作为 plants/*.dll 插件

`PGvZNewPlantMod` 宿主会扫描 `mods\PGvZNewPlantMod\plants\*.dll`，反射发现
`IPlantPack` 实现，按 `Order` 排序后调用 `Register(packId)`。插件只引用本库和游戏程序集，
不需要引用宿主：

```csharp
using PGvZPlantKit;

public sealed class MyPack : PlantPackBase
{
    public override string PackId => "my.pack"; // 可省略；省略时用 DLL 文件名
    public override int Order => 10;

    public override void Register(string packId)
    {
        PlantKit.RegisterEmbeddedImages(typeof(MyPack).Assembly, "MyMod.Assets.");
        PlantKit.RegisterPlant(MyPlant.Def, packId);
    }
}
```

- `packId` 由宿主传入；九植物各自使用 `pgvz.super_gatling`、`pgvz.fjth` 等独立包 ID。
- 不覆写 `PackId` 时，宿主用 DLL 文件名（不含扩展名）作为包 ID。
- 宿主启动时会把未列出的插件包追加到 `plant_packs.json` 并设为启用，见第 7 节。
- 插件自己的卡图/特效图用 `RegisterEmbeddedImages` 内嵌，逻辑名以 `MyMod.Assets.` 开头。
- 有专属钩子时，把钩子安装放在 `Register` 里，并覆写 `HookCount`/`HookFailures`
  供验证宿主统计（可直接转发给一个 `Extras` 类）。
- 删除某个插件 DLL 只会少一株植物，宿主仍能启动；不要把它放到 `mods` 顶层。

## 3. 自定义 Spine

- `NativeReanimType` 留 `ReanimationType.None`
- `ReanimSlot` 留 `-1` 自动分配（也可显式填 0,1,...，从 `CustomPlantRegistry.ReanimBase` 起算）
- `SpineName = "my_plant"`，把 `my_plant.atlas/.skel/.png` 放到游戏根目录 `Content/spanim`，
  或用 `PlantKit.RegisterEmbeddedContent(assembly, "MyMod.Assets.Spine.")` 内嵌到 DLL
- `StandAnim = "anim_stand"` 等按导出动画名填写；复用原生动画时留空
- `BodyScale/BodyDx/BodyDy` 控制身体缩放和锚点；`HasPreview/Preview*` 控制图鉴缩略图
- 卡图：`PlantKit.RegisterEmbeddedImages(assembly, "MyMod.Assets.")` 后把 `CardAsset`
  设为资源名（不含扩展名）；或用 `PlantKit.RegisterImageFile("my_card", path)`。
- 卡上立绘的位置与大小：`CardOffsetY`（默认 -23）管上下，`CardArtDx/CardArtDy` 再叠一层微调，
  `CardArtScale`（默认 1）整体缩放；自绘卡图和借来的原生动态立绘都吃这三个值。

## 3b. 经典逐帧动画（原版 .reanim）

不想把素材转成 Spine 时，填 `ReanimName` 而不是 `SpineName`：

- `ReanimName = "my_anim"` → 运行时会读内嵌的 `Content/reanim/my_anim.reanim`
- 帧图按动画里的 `<i>IMAGE_REANIM_MY_ANIM_IDLE_000</i>` 解析成资源名
  `my_anim_idle_000`，所以帧 PNG 用 `Assets\Reanim\my_anim_idle_000.png` 放进工程即可，
  逻辑名前缀仍是 `PGvZNewPlantMod.Assets.`（`src\Plants\Directory.Build.props` 已配好）
- `PlantKit.RegisterEmbeddedContent(assembly, "MyMod.Reanim.", "Content/reanim")`
- 解析规则与引擎自己的 `ReanimXmlReader` 一致：缺省字段续上一帧的值，
  坐标按 `doScale`（默认 `ScaleFromPC`）乘 `Constants.S`，所以 Android 上会一起缩小
- 动画名照 `.reanim` 里的 `<name>` 填 `StandAnim`；帧率取文件里的 `<fps>`，
  需要固定播放速度时每帧写一次 `Body.mAnimRate`

## 3c. 注册子弹（CustomBullets）

这个框架不能新增 `ProjectileType`（类型是引擎枚举，还会写进关卡存档），所以自定义子弹一律是
**原生载体弹种 + 一条注册记录**：换贴图、换伤害、加溅射、甚至自己接管弹道和命中，都挂在这条记录上。

```csharp
public static readonly CustomBulletDef Bullet = new CustomBulletDef
{
    Id = "mymod:my_shot",          // 跨包引用用这个字符串
    Carrier = ProjectileType.Pea,   // 载体：决定碰撞盒、影子和默认表现
    Image = "my_shot",              // AssetStore 里的贴图名
    Damage = 40,                    // 0 = 用载体弹种自己的伤害
    DamageFlags = 0,                // 这一发"怎么打"，见下表；0 = 照引擎按弹种算
    DamageRangeFlags = 0,           // 这一发"打得着谁"，见下表；0 = 照发射植物算
    SplashDamage = 20,              // 命中给同排邻居补一刀
    SplashWidth = 100,              // 溅射矩形宽度，默认照火球
    Pierce = 2,                     // 还能穿几个目标（要显式 Damage，留 0 时原生会把弹带走）
    Poison = CustomCombat.PoisonPea, // 命中挂这层毒，两套掩码默认继承这颗弹
    Speed = 6f,                     // 直线弹每帧走多少像素（引擎写死 3.33，改 mVelX 没用）
    MaxAge = 75,                    // 飞满这么多帧就消失（0 = 交给引擎自己收）
    Tint = "#66ccff",               // 弹体染色（#rrggbb / #rrggbbaa），留空用载体原色
    Motion = new MyShell(),         // 可选：逐帧接管运动/贴图
    SkipCollision = true,           // 落点自己算时别再被原生碰撞提前引爆
};

CustomBullets.Register(Bullet);                       // 在 Pack.Register 里
Projectile shot = CustomBullets.Fire(B, Plant, "mymod:my_shot", x, y, row,
    ProjectileMotion.Lobbed, 127u);                   // 最后一位是范围掩码，覆盖 DEF 的取值
CustomBullets.Convert(otherShot, "mymod:my_shot");    // 附魔：把飞行中的子弹换成这条记录
```

- 换图后引擎会按新图的格子重算落点，框架自动补回半格差值，所以贴图尺寸变化不会"跳一下位"。
- 别的包按 ID 字符串引用你的弹；那个包没启用时查不到记录，调用方跳过即可，不报错。
- **穿透**：`Pierce=N` 表示这颗弹最多咬 N 只新目标，同一只只吃一口（重复撞上已咬过的
  既不扣血也不掉额度），额度用完后的那一只落地消失。"打过谁"不跨存档，穿透额度本身跟着子弹存。
- **命中挂毒**：`Poison` 填 `CustomCombat.PoisonSpec`；注册这颗子弹时这条 spec 一并进毒的台账，
  自检和诊断快照都看得到它。两套掩码默认继承这颗弹（显式写了才不继承）。
- **速度**：`Speed` 只对直线弹生效——引擎把直线弹的位移写死成 3.33/帧（个别弹种 15），
  框架在原生那一帧之后把它换成声明值；抛物/落点弹道是时间曲线，改横向速度会让它落在别处，所以不动。
- **射程**：`MaxAge=N` 让这颗弹飞到第 N 帧就消失。引擎那条"小喷菇只能飞 75 帧"是按
  `mMotionType == Puff` 硬判的，斜着飞（借 `Star` 运动）的注册弹走不到那一支，所以按声明补同一刀。
- **弹体染色**：`Tint` 把弹体本身乘上这个颜色（植物本体用 `DEF.BodyTint`，两套各管各的）。
  引擎画弹体走 `TodBltMatrix(..., SexyColor.White, ...)`，那条路不读 `g.mColor`/`SetColorizeImages`，
  所以框架改成"发弹那一刻备好一张染色图、绘制时只查表"。染色图是**整页副本 + 把乘好色的那一格写回
  它原来的页内位置**，再用引擎自己的 `Image(texture, s, t, w, h)` 造出来 —— 偏移、格子、`mInvScale`
  全照抄源图，UV 怎么算归引擎，框架不复刻它的取帧公式（自己抠小图就得自己算 `mInvScale` 那一步，
  少算一点会把图集邻格的碎片吃进来）。代价：每个「图集页 + 颜色」一张页大小副本、同页共用，
  且只读写那一格的像素（整页 4M 像素遍历一遍会让第一发弹卡一下）。
  读像素/建纹理**不能放在 Draw 里**——实测那样会把游戏线程卡死（窗口冻住、CPU 归零）。
  `Image` 和 `Tint` 同时给了时 `Image` 优先；载体是火球那类走 reanim 的弹种染不了色（自检会点名）。
- 想要完全自定义命中，实现 `ICustomProjectileImpact` 填到 `Impact`；想自定义弹道或逐帧换图
  （例如落点爆炸帧），实现 `ICustomBulletMotion` 填到 `Motion`。填了 `Impact` 之后，
  `Damage`/`Splash`/`Pierce`/`Poison` 这几条声明一条都不再参与（自检会点名这种配法）。

### 两套 uint，别混

引擎里有两个都叫"flags"的无符号整数，语义完全不同，注册子弹时两套都要写：

`DamageFlags`（引擎枚举 `Lawn.DamageFlags`，`TakeDamage` 的第二个参数，**子弹身上没有字段存它**，
每次命中由 `Projectile.GetDamageFlags` 现算）管"这一发怎么打"：

| 值 | 位 | 效果 |
|---|---|---|
| 1 | 0 | 穿盾（门/梯子不吃这口，直接打本体） |
| 2 | 1 | 盾与本体同扣（溅射弹走这条） |
| 4 | 2 | 附加冰冻（`ApplyChill`，雪豌豆/冰瓜） |
| 8 | 3 | 不播受击闪白、不播盾牌受击音效 |
| 16 | 4 | 不留尸体：不掉头不掉手、无碎尸粒子（樱桃/辣椒那类秒杀，`DieNoLoot`） |
| 32 | 5 | 地刺扎死（配合 `Zamboni`/`Catapult` 会把伤害改写成 1800） |

原生样本：豌豆 `0`、大喷菇 `20|2 = 22`、地刺 `1|32 = 33`、樱桃 `18 = 2|16`、冰陷阱 `1`。
留 `0` 时框架照引擎那样按这颗弹现算（`Projectile.GetDamageFlags`：溅射给 2、
抛物/倒飞给 1、雪豌豆/冰瓜再叠 4），也就是**和同弹种原生子弹完全一致**。

`DamageRangeFlags`（引擎枚举 `Lawn.DamageRangeFlags`，存进 `Projectile.mDamageRangeFlags`，
喂给 **`Zombie.EffectedByDamage(mask)`**）管"打得着谁"。枚举值是**位号**，字段里存的是**掩码**
（`1 << 位号`，多类叠加按位或）：

| 位号 | 掩码 | 谁 |
|---|---|---|
| 0 | 1 | 地面 |
| 1 | 2 | 空中 |
| 2 | 4 | 水下 |
| 3 | 8 | 僵尸狗 |
| 4 | 16 | 离地阶段（跳、跳杆、蹦蹦棒） |
| 5 | 32 | 濒死 |
| 6 | 64 | 地下/隐身（挖地钻） |
| 7 | 128 | 只打被洗脑的 |

原生植物的取值：豌豆那族 `1`、仙人掌 `3`、缠绕水草 `5`、喷菇/海蘑菇/大喷菇/忧郁菇/大嘴花 `9`、
香蒲 `11`、投手与南瓜 `13`、巨墙瓜 `17`、地雷 `77`、樱桃/辣椒/加农炮/末日菇 `127`。
自定义种子在原生 `GetDamageRangeFlags` 的分支里没有条目，一律落回 `1`（只能打地面），
所以要打空中或水下就在 DEF 里写 `DamageRangeFlags`。

> 这两套以前是一个字段：命中时把范围掩码当伤害掩码传给了 `TakeDamage`。
> 后果是"地面(1)"读成了"穿盾(1)"——模组子弹无声地无视铁桶和门板。
> 现在两套各走各的：范围掩码只做筛选，伤害掩码只喂 `TakeDamage`。

## 3d. 公用毒伤（CustomCombat.PoisonSpec）

引擎里没有"中毒"这个状态（`Zombie` 上只有冰冻/燃烧/黄油），所以毒完全是框架自己记账、
每帧在 `Board.UpdateGameObjects` 前后步进一次（前置记录位置 → 后置按位移算减速、逐条扣血）。
各包不要再造一套计时字典，声明一条 `PoisonSpec` 就行：

```csharp
// 固定值多段毒（水神那一档）：3 秒内 3 刀 ×100，穿盾、不闪白、不减速、不染色
private static readonly CustomCombat.PoisonSpec Poison = new CustomCombat.PoisonSpec
{
    Name = "水神的毒",
    FixedDamage = 100,          // >0 按固定值扣；=0 才走 DamagePercent
    Duration = 300,             // 整段时长（帧）
    Interval = 100,             // 两刀之间
    FirstTick = 5,              // 第一刀提前到第 5 帧（0 = 等满一个 Interval）
    TopUpHits = 3,              // 重复施加只把"还欠几刀"补满 3，不重置计时
    TakeFlags = 9,              // 写了 = 不继承：DamageFlags 1 穿盾 | 8 不播受击闪白
    RangeFlags = 9,             // 写了 = 不继承：DamageRangeFlags 1 地面 | 8 僵尸狗
    Tint = false,               // 不染紫
    SlowPercent = 0,            // 不减速
};

CustomCombat.RegisterPoison(Poison);              // 在 Pack.Install 里，只登记数值给自检/报告
CustomCombat.ApplyPoison(zombie, Poison, 弹体);   // 由子弹挂上：把那一发传进来
CustomCombat.ApplyPoison(zombie, spec);           // 没有来源子弹的范围型毒
CustomCombat.ApplyPoison(zombie);                 // 框架预设：毒豌豆那一档（2% 上限、减速 25%）
```

- **两套掩码默认继承附着它的那一发子弹**：`TakeFlags`/`RangeFlags` 留 0 时，挂毒那一刻按
  `source.GetDamageFlags(zombie)` 取伤害掩码、按 `source.mDamageRangeFlags` 取范围掩码 ——
  "这一口毒怎么结算，跟那发子弹一致"。写了值才不继承；传不出子弹（范围型/地面型毒）
  时留 0 就是原语义：伤害走防具、不设范围门槛。叠毒走 `TopUpHits` 只补时长，
  **不会**用后一发的弹改写已经定下来的规则。
- **同一条 spec 叠在一起**：`TopUpHits > 0` 就只延长尾巴（下一刀的时刻不动），
  否则整条重置。不同 spec 各占一格互不覆盖；几条同时挂着时减速叠乘，任意一条 `Tint` 就显色。
- `RangeFlags != 0` 时每一刀前先问一次 `Zombie.EffectedByDamage`，不匹配就**跳过这一刀但倒计时照走**
  （草坪右侧外的僵尸本来就该被挡在这里，别把它读成"毒没生效"）。
- 僵尸死后引擎会把它 `PrepareForReuse()` 塞回对象池再复用，所以框架在同一个钩子上
  `ClearPoison` 摘账 —— 别在自己的包里另存一份 `Dictionary<Zombie, …>`，那只僵尸会被下一只继承。
- 数值写错的毒**不会报错，只会安静地一刀都不掉**，所以 `RegisterPoison` 之后
  `PlantKit.ValidatePlants()` / F9 清单会点名这几类：`Duration<=0`、`Interval<=0`、
  第一刀晚于整段到期、没写伤害、两套掩码越界。

## 3e. 独立灼热（CustomBurn）

`CustomBulletDef.Burn` 接受 `CustomBurn.BurnSpec`，只在主目标命中后附加，不影响原生溅射。
`Carrier = Melon`、`Damage = 0` 保留原生西瓜的结算；`SummerMelon.py` 还复用原生发射过程。

灼热有独立的状态、查询、计时和清理，与中毒互不覆盖。默认每刀向下取整，总最大生命值包含
`mBodyMaxHealth + mHelmMaxHealth + mShieldMaxHealth + mFlyingMaxHealth`，不按当前防具是否存在删项。
`SplashWidth` 与 `SplashRowSpan` 控制以灼热目标为中心的扩散，夏西瓜设为 60 和 1，邻居吃同一数值，不传染灼热。
**计时**：`FirstTick` 是首刀时刻，`Ticks` 是总共几刀 —— 写了 `Ticks` 就由它推出结束帧
（`FirstTick + Interval*(Ticks-1)`，末刀落在最后一帧），不用为了"最后一刀"去凑 305 那种时长，
也不会到期后空转一整秒；`FirstTick=1` 就是"命中当拍就烧第一刀"。不写 `Ticks` 时才按 `Duration` 走。
每刀都会在该僵尸身上放一支引擎火球命中用的 `JalapenoFire` 火焰（同 `Projectile.DoImpact` 的 Fireball 分支）。
同一档重复施加刷新 `Duration`，不重置下一刀、不叠层。`TakeFlags`/`RangeFlags` 为 0 时继承来源子弹；
`HasBurn`/`HasVisibleBurn`/`ClearBurn` 分别查询、检查显色、清除此效果，不操作毒账本。

> 显色（`Tint`/`TintColor`）走的是 `Zombie.Draw` 里的绘制期染色，**对僵尸的 reanim 不生效**：
> 实测同一帧 `HasBurn=True`、已挨 3 刀，画面上仍是原色。夏西瓜因此把它关掉了（`SCORCH.Tint=False`）。
> 要让"烧着"看得见，得改成按僵尸 reanim 上色（和 `DEF.BodyTint` 同一条路子），目前还没做。

## 4. IronPython 植物

在 `PGvZNewPlantMod` 宿主里，脚本放进 `mods\PGvZNewPlantMod\plants\`，文件名（不含 `.py`）
就是包 ID；宿主已经调用 `PlantKit.Install()` 并预加载本库，不需要 `clr` 或路径处理。
每个文件在独立 scope 执行。

```python
import System
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase
from Lawn import SeedType, CoinType, CoinMotion
from Sexy.TodLib import ReanimationType

D = CustomPlantDef()
D.Name = u"示例植物生产"   # SeedId=0、图鉴/选卡格留空即自动
D.Cost = 50
D.NativeReanim = True
D.NativeReanimType = ReanimationType.Sunflower
D.HasPreview = False
D.CardNativeSeed = SeedType.Sunflower

# 卡片外观（都可省略）：裸立绘先垫一张框架自带的通用背景，再微调立绘在卡上的位置/大小
# D.CardBackground = "green"   # "green" 普通卡；"purple" 升级卡（要种在另一张卡上）
# D.CardArtScale = 0.9         # 立绘缩放，1 = 原生大小
# D.CardArtDx = 0              # 立绘额外位移，叠加在 CardOffsetY 之上
# D.CardArtDy = -4

class MyPlant(CustomPlantBase):
    def OnUpdate(self):
        pass

D.Factory = System.Func[CustomPlantBase](lambda: MyPlant())
PlantKit.RegisterPlant(D)
```

`RegisterPlant(D)` 不传包 ID 时使用宿主当前文件的包。完整可运行示例见
[`../../package/PGvZNewPlantMod/plants/ExamplePlantProducer.py`](../../package/PGvZNewPlantMod/plants/ExamplePlantProducer.py)。

**拿原生 `Lawn.Plant` 的字段/方法**：行为类不是 `Plant` 的子类，所以 `self.mPlantHealth`
不成立；底层实体在只读属性 `self.Plant` 上，`Plant`/`GameObject` 的公开字段和方法直接可用：

```python
plant = self.Plant
plant.mPlantHealth = 900          # 公开字段可直接读写
board = self.B                    # == self.Plant.mBoard，kit 的便捷属性
board.AddCoin(plant.mX + 10, plant.mY, CoinType.Sun, CoinMotion.Coin)
```

`self.Frame / self.Body / self.AnimTime / self.AnimDone / self.IsAlive / self.Definition`
是 kit 提供的便捷属性。只有 **public** 成员可见（internal/protected 在 Python 里会报错）；
`Plant.Update/Die/Squish/Draw/Fire` 这些已经由 kit 转发成 `OnUpdate/OnRemoved/OnSquish/...`
回调，别再在里面直接调一遍，否则会重复执行。

**Python 侧的钩子写法没有变**，仍然是游戏自带的 MonoMod 装饰器（引擎 API，
和框架的 `[HookTo]` 属性各管一边 —— .NET 特性没法贴到 Python 函数上）：

```python
from LawnMod import MonoModUtils as M
from Lawn import Zombie

@M.HookTo(Zombie.DieNoLoot)
def Zombie_DieNoLoot__MyPlant(orig, self, giveAchievements):
    orig(self, giveAchievements)
```

`@M.HookTo` 拿到的是 CLR 方法对象，重载按 Python 里那个属性实际绑定的方法算；
C# 侧新加的 `[HookTo(...)]` 要显式写形参类型。两边装的是同一套 MonoMod 钩子，
区别只在声明形式和重载的确定方式。目标类型只能运行时确定的场合，Python 也可以调
`PlantKit.AddHook(type, name, args, delegate)`。

## 5. 生命周期与扩展点

`CustomPlantBase` 自动完成：

- 原生临时身体替换、SPINE 身体创建、血量、初始动画、每帧对齐；
- `Plant.Update/Draw/Die/Squish/MouseDown/Fire` 的分发；
- 卡槽冷却、选卡/图鉴/模仿者/光标预览（卡的灰与暗直接问引擎 `GetGraynessAndDarkness`，
  所以"无冷却种植"作弊、开场动画变暗这些原生行为一并跟着走）；
- 含自定义种子的 legacy 存档（第 10 卡槽侧车）。

可覆写：`OnPlanted`、`OnUpdate`、`OnRemoved`、`OnMouseDown`、`OnSquish`、
`OnNativeFire`、`OnAlmanacPreview`、`OnBitten`、`OnEatenStart`、`OnZombieDied`、
`OnWave`、`OnLevelStart`、`OnDrawTop`。

第三方 mod 额外机制：

- `[HookTo(typeof(Target), "Method", typeof(Arg1), ...)]`：声明式钩子，标在静态处理器方法上，
  处理器第一个形参就是被钩方法的委托，目标写类型+方法名+形参类型。写法对齐 IronPython 的
  `@M.HookTo(Target.Method)`，框架自己的 69 个核心钩子都是这么挂的：
  ```csharp
  [HookTo(typeof(Zombie), "Draw", typeof(Graphics))]
  private static void ZombieDrawHook(Action<Zombie, Graphics> orig, Zombie self, Graphics g) { }
  ```
- `PlantKit.RequestScreenshot(path = None)`：请求一次截图，返回即将写入的 PNG 路径（留空按时间命名），
  真正落盘在下一帧画完的游戏线程上（读后备缓冲，不需要窗口在前台）。游戏里按 **F12** 是同一个入口，
  默认存到 `<mod 目录>/screenshots/`；结果看 `Screenshot.LastPath` / `Screenshot.LastError`。
- `PlantKit.AddHook(type, name, args, delegate)`：老式写法仍然可用，适合目标类型只能运行时确定的场合。
- `PlantKit.RegisterSaveHandler(predicate, saveExtra, loadExtra)`：额外侧车存档。
- `CustomProjectiles.SetKind/SetFan/SetBoost/SetFrozen/SetImpactHandler`：复用原生弹体。
- `CustomCombat`：毒、电击、巨型/血量判定等共享规则。

## 6. 自动分配规则

以下字段可以全部留空，第一次 `EnsureDefinitions`（打开图鉴/选卡或首次种植）时确定：

| 字段 | 留空值 | 分配方式 |
| --- | --- | --- |
| `SeedId` | `0` | 从 `max(94, gPlantDefs.Length)` 起找空闲号 |
| `AlmanacRow/AlmanacColumn` | `0` | 4 列网格，1 基；避开原生 54 格、未解锁空白卡、模仿者特殊位置、已注册显式格 |
| `ChooserRow/ChooserColumn` | `-1` | 4 列网格，0 基；避开原生 54 格和已注册显式格 |
| `ReanimSlot` | `-1` | 仅自定义 Spine；避开显式槽和其他 mod 已写入参数表的槽 |

- 显式值优先；显式位置会先占位，自动植物再排后面。
- 自动分配结果写入 `plant_kit.log`，例如：
  `auto layout 示例植物生产: seed 94, almanac (16,1), chooser (14,0), reanim slot -1`。
- 本项目当前 14 个自动定义（九植物 + 小守卫菇 + 4 个 Python 示例）分配为
  `seed 94–107`，重复 0、低于 94 的碰撞 0。原生 SeedType 为 0–64，旧版自定义号
  65–93 也不会被自动号占用。
- 不要混用“自动 94 起”和“显式 94+”；显式占用会让自动号后移或在重复注册时被替换。

## 7. 植物包加载顺序

参考精华版 `resourcepacks.json` / `pack.json`。库默认读取 kit DLL 同目录的
`plant_packs.json`；`PGvZNewPlantMod` 宿主会显式指向
`mods\PGvZNewPlantMod\plant_packs.json`。修改后重启游戏：

```json
{
  "version": 1,
  "packs": [
    { "id": "my.pack", "enabled": true, "before": ["other.pack"] },
    { "id": "other.pack", "enabled": true },
    { "id": "old.pack", "enabled": false }
  ]
}
```

- `packs` 数组顺序就是加载顺序，前面优先。
- `enabled=false`：该包调用 `RegisterPlant` 会被跳过并写日志；在 `PGvZNewPlantMod` 宿主里，
  对应的 `plants\<id>.py` 也不会执行。
- `before` / `after`：包 ID 或数组，用于依赖排序；`dependencies` 等价于本包 after 这些包。
- 未列出的包在内存中按包 ID 字母序追加、默认启用。`PGvZNewPlantMod` 宿主启动时还会
  扫描 `plants\*.py` 和插件 DLL，把未列出的包写入清单并显式设为 `enabled: true`；
  已有条目、顺序和 `before/after` 保持不变。
- 自动补清单用 `LoadOrder.EnsureManifestPacks(ids)`；文件不存在时会新建
  `version: 1` 清单，写入失败只记日志，不影响本轮加载。
- 循环约束会写警告，并按列表顺序稳定打破。
- 注册时传入包 ID：`PlantKit.RegisterPlant(def, "my.pack")`；在宿主里省略时使用当前文件包。
- 自动 SeedId 在第一次 `EnsureDefinitions` 时按包顺序统一分配；显式 SeedId 先占位。
- 自动图鉴/选卡位置按 `chooser_order` 全局排序，值小在前；相同时按包顺序、注册顺序。
- 显式填写了 `SeedId`/图鉴格/选卡格的植物保持原位，不受包排序影响。
- 脚本执行：`PGvZNewPlantMod` 宿主会按清单枚举并执行 `plants\*.py`；其他 Python 加载器
  自行决定执行顺序，本清单只控制注册提交、自动 SeedId 和自动排版。禁用包在那些加载器里
  只跳过 `RegisterPlant`，脚本自己安装的钩子需要用 `LoadOrder.IsEnabled("pack.id")` 自检。
- 自动 SeedId 会随包顺序变化。含自动植物的存档不要随意增删/重排自动包；正式发布建议
  给存档相关植物显式固定 `SeedId`。
- 实机顺序实测：清单基础顺序 `early -> late`，`late.before(early)` 后日志为
  `nine -> star -> late -> early`；自动种子 `star=94, late=95, early=96`；
  `enabled=false` 的包未注册；`chooser_order=-10` 让 early 排到图鉴最前，0 ERROR。

## 8. 约束

- 一个种子只对应一个 `CustomPlantDef`。
- 行为实例每个 Plant 一份；状态不要放静态字段。
- 需要 `PlantKit.Install()`；kit DLL 本身不会自动挂钩子，`PGvZNewPlantMod` 宿主会代为调用。
- 自定义 Spine 必须有可读取的来源，否则日志会出现 `missing Spine asset`：
  可以内嵌到插件 DLL（`RegisterEmbeddedContent`），也可以放在数据文件夹的 `spanim\`
  或游戏 `Content/spanim`。本项目的九种植物使用内嵌方式。
- 随宿主发布时保持 `PGvZPlantKit.dll` 在 `mods\PGvZNewPlantMod\` 内，不要移到顶层。
- C# 植物插件放在 `mods\PGvZNewPlantMod\plants\`，实现 `IPlantPack`；宿主不引用插件类型。
