# IronPython 新建植物示例

示例已经并入宿主的数据文件夹，不再是一个独立的顶层脚本：

- 源码：`package\PGvZNewPlantMod\plants\ExamplePlantProducer.py`
- 部署后：`%AppData%\ZBC\PlantGirlsVsZombies\mods\PGvZNewPlantMod\plants\ExamplePlantProducer.py`

安装、清单和目录结构见
[数据文件夹说明](../package/PGvZNewPlantMod/README.md)。

随包示例：

- `ExamplePlantProducer.py`：周期产阳光。
- `ExamplePlantShooter.py`：`PlantSubClass.Shooter` + `OnNativeFire` 生成豌豆。
- `ExamplePlantPult.py`：同样走 Shooter 索敌，`OnNativeFire` 生成 `ProjectileMotion.Lobbed` 卷心菜。
- `ExamplePlantSpecialBomb.py`：延迟后临时切到 Cherrybomb 分支并调用原生 `Plant.DoSpecial()`。

所有示例的 `SeedId` 都留 0，由 PlantKit 自动分配。

## 使用

1. 按 [安装与分发](安装与分发.md) 把发布包所有内容解压到 Windows 或 Android 的 `mods` 目录，
   得到 `mods\PGvZNewPlantMod.dll` + `mods\PGvZNewPlantMod\`。
2. 在 `mods\PGvZNewPlantMod\plants\` 放一个 `my_plant.py`；文件名就是包 ID。
3. 重启游戏。九种 C# 插件植物之后会多出这株植物；种子号、图鉴格、选卡格自动分配。

脚本由宿主执行，`PlantKit.Install()`、Lawn 和 PGvZPlantKit 都已准备好，不需要 `clr`、
不需要自己拼 DLL 路径。最小写法：

```python
import System
from Lawn import SeedType
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
DEF.Name = u"我的植物"
DEF.Cost = 100
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Sunflower
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Sunflower
DEF.StandAnim = ""

class MyPlant(CustomPlantBase):
    def OnUpdate(self):
        pass

DEF.Factory = System.Func[CustomPlantBase](lambda: MyPlant())
PlantKit.RegisterPlant(DEF)
```

## 关键约束

- 一个文件一株植物；包 ID 来自文件名，必须与 `plant_packs.json` 里的 `id` 一致（大小写不敏感）。
- `SeedId = 0`、`AlmanacRow/AlmanacColumn = 0`、`ChooserRow/ChooserColumn = -1`、
  `ReanimSlot = -1` 表示自动分配；显式值优先并先占位。
- 自动 SeedId 在首次打开图鉴/选卡或种植时按包顺序提交；涉及存档的植物建议固定 `SeedId`。
- 卡图放 `assets\my_card.png`，脚本写 `DEF.CardAsset = "my_card"`，宿主按文件名自动注册。
- 自定义 Spine 可放进 mod 的 `spanim\`（或游戏 `Content\spanim`），设置 `SpineName`
  和 `ReanimSlot = -1`；九种 C# 植物已把 Spine 内嵌在各自 DLL，不需要外部目录。
- 行为实例每个 Plant 一份；不要把状态放在类变量上。
- 加载失败只影响当前文件，日志写 `folder plant failed: <包> <- <文件>` 和完整堆栈。

## 事件钩子（CustomPlantBase 可覆写）

| 方法 | 时机 |
|---|---|
| `OnPlanted()` / `OnRemoved()` | 种下 / 死亡离场 |
| `OnUpdate()` | 每帧（注册表会先走一次内部 Tick，再进 OnUpdate） |
| `OnNativeFire(orig, target, row, weapon)` | 原生射击节拍；不调 `orig(...)` 即完全接管弹型 |
| `OnMouseDown(x, y, clickCount)` | 被点击；返回 True 吃掉事件 |
| `OnSquish()` | 被碾压；返回 True 吃掉原生压扁 |
| `OnBitten(amount)` | 掉血（基类逐帧比对血量，啃咬/碾压都算） |
| `OnEatenStart(eater)` | 有僵尸开始啃这株植物（Zombie.StartEating） |
| `OnZombieDied(zombie)` | 同一行的僵尸死亡（DieNoLoot 全路径只计一次） |
| `OnWave(wave)` | 新一波僵尸出现（波号从 1 起） |
| `OnLevelStart()` | 关卡正式开始（首次进入 Playing） |
| `OnDrawTop(g)` | 每帧在植物本体之上自绘（光圈/计数器等） |
| `OnAlmanacPreview()` | 图鉴预览创建后 |

## 实例语法糖（CustomPlantBase 上的方法/属性）

- `self.Row` / `self.Col` / `self.CenterX` / `self.CenterY`：坐标速记。
- `self.FindFrontZombie(row=-1)`：某行（默认本行）最靠前的活僵尸。
- `self.ZombiesInRow(row=-1)`：某行活僵尸列表。
- `self.FireProjectile(弹型, dx=60, dy=-10, motion=Straight, velX/velY/velZ/accZ=0, weapon=Primary)`：
  从植物发弹；抛物弹传 `ProjectileMotion.Lobbed` 加 velX/velZ/accZ（参数可用关键字）。
- `self.DropSun(面额=25, dx=0, dy=0)`：掉阳光，面额自动取 5/15/25/50 档。
- `self.Heal(n)` / `self.AreaDamage(damage, x, y, w, h)`：回血 / 矩形范围伤害。
- `self.RowAreaDamage(damage, flags=2, rectSeed=SeedType.None)`：本行范围伤害。
  `flags` 是 `Zombie.TakeDamage` 的 **DamageFlags** 掩码（1 穿盾 / 2 盾体同扣 / 4 冰冻 /
  8 不闪白 / 16 不留尸 / 32 地刺扎死），不是"打得着谁"的范围掩码——后者由 `rectSeed`
  借来的原生种子走 `GetDamageRangeFlags`，两件事别写在一个数上（详见框架 README「两套 uint」）。
  自定义种子在原生 `PlantDefinition` 表里没有攻击矩形条目，所以借哪个原生种子的矩形就填 `rectSeed`
  （大喷菇那类填 `SeedType.Fumeshroom`）；helper 内部临时换 `mSeedType`、`try/finally` 还原。
  在钩子处理器里拿到的是 `Plant`，直接用同名静态方法 `CustomCombat.RowAreaDamage(plant, 40, 2, SeedType.Fumeshroom)`。
- 中毒不用自己建字典计时：声明一条 `CustomCombat.PoisonSpec`（`FixedDamage` 或 `DamagePercent`、
  `Ticks`/`Interval`/`FirstTick`（或 `Duration`）、`SlowPercent`、`Tint`、`TakeFlags` 与 `RangeFlags` 两套掩码、
  `TopUpHits` 决定再次命中是"补满欠刀"还是"整条重置"），命中时
  `CustomCombat.ApplyPoison(zombie, spec, 弹体)` 即可 —— 两套掩码留 0 就**默认跟着那一发子弹**
  （伤害掩码 = `弹.GetDamageFlags(僵尸)`，范围掩码 = `弹.mDamageRangeFlags`），写了值才不跟；
  范围型的毒没有来源子弹，传两参那一版重载就行。框架每帧在 `Board.UpdateGameObjects` 前后步进一次
  （前置记位置、后置算减速与扣血），并在僵尸死亡或进对象池时自动摘账；数值写错（时长 0、间隔 0、
  第一刀晚于到期、没填伤害）会由自检点名。详见框架 README「3d. 公用毒伤」。
- 计时和灼热同一套：`FirstTick=1` 是"命中当拍就落第一刀"，`Ticks=3` 是"共几刀"，
  结束帧由框架推（`FirstTick + Interval*(Ticks-1)`），不用为最后一刀去凑 `Duration`；
  不写 `Ticks` 才按 `Duration` 走（两者同时写会互相打架，自检会点名）。
- `self.Every(interval, key=None)`：节拍器，每 interval 帧返回一次 True；
  `OnPlanted` 里先调一次即可"上弦"，避免首个滴答立刻触发。
- `self.Schedule(delayTicks, 回调)`：延迟回调一次；植物死亡后未触发的自动作废。

## 独立灼热（CustomBurn）

灼热与中毒分别记账，可以同时存在；`HasBurn` 不会让 `HasPoison` 返回真。
夏西瓜示例通过 `CustomBulletDef.Burn` 在主目标命中后附加灼热，普通溅射不附加。

```python
from PGvZPlantKit import CustomBurn

SCORCH = CustomBurn.BurnSpec()
SCORCH.Name = u"灼热"
SCORCH.DamagePercent = 3
SCORCH.FirstTick = 1        # 命中当拍就烧第一刀（0 = 等满一个 Interval）
SCORCH.Ticks = 3            # 共几刀；结束帧由它推出，不用凑 305 那种时长
SCORCH.Interval = 100
SCORCH.SplashWidth = 60
SCORCH.SplashRowSpan = 1
BULLET.Burn = SCORCH
```

- 默认向下取整，基数为 `body + helm + shield + flying` 四项最大生命值之和，防具脱落不排除其最大值。
- 计时：`FirstTick` 是首刀时刻（`1` = 命中当拍就烧），`Ticks` 是共几刀；写了 `Ticks` 就由它推出
  结束帧（`FirstTick + Interval*(Ticks-1)`），所以别为"最后一刀"去凑 `Duration`，也不会到期空转。
- 每刀会在该僵尸身上放一支引擎火球命中用的 `JalapenoFire` 火焰；不做僵尸染色（绘制期染色对 reanim 无效）。
- 每刀向附近目标造成同一个伤害数值；邻居的生命上限不参与计算，也不会因此被挂上灼热。
- 同一档重复命中只刷新持续时间，不叠加、不推迟下一刀；死亡或对象回收时清理。
- `TakeFlags`、`RangeFlags` 留 0 时继承来源子弹，显式值覆盖继承。
- `CustomBurn.ApplyBurn(zombie, SCORCH, projectile)`、`HasBurn(zombie)`、`ClearBurn(zombie)` 可直接调用。

## 模组子弹（CustomBullets）

框架没有"新增 `ProjectileType`"的口子（那是引擎枚举、还会写进关卡存档），所以自定义子弹一律是
**原生载体弹种 + 一条注册记录**：换图、换伤害、加溅射、穿透、挂毒、改速度、甚至接管弹道与命中，
都挂在这条记录上；别的包按 ID 字符串引用它（那个包没启用时查不到记录，跳过即可，不报错）。

```python
from PGvZPlantKit import CustomBullets, CustomBulletDef, CustomCombat
from Lawn import ProjectileType, ProjectileMotion

BULLET = CustomBulletDef()
BULLET.Id = u"mymod:ice_shard"          # 跨包引用用这个字符串，前缀写自己的包 ID
BULLET.Carrier = ProjectileType.Pea     # 载体：决定碰撞盒、影子和默认表现
BULLET.Image = u"ice_shard"             # AssetStore 里注册过的贴图名，留空用载体自己的图
BULLET.Damage = 30                      # 0 = 用载体弹种的原生伤害
BULLET.DamageFlags = 4                  # "这一发怎么打"：4=附加冰冻；0=照引擎按弹种算
BULLET.DamageRangeFlags = 2             # "打得着谁"：2=空中；0=照发射植物算（两套别混）
BULLET.SplashDamage = 10                # 命中给同排邻居补一刀
BULLET.Pierce = 2                       # 还能咬几只新目标（同一只只吃一口）
BULLET.Poison = SPEC                    # 命中挂这层毒，两套掩码默认继承这颗弹
BULLET.Speed = 6.0                      # 直线弹每帧走多少像素
BULLET.MaxAge = 75                      # 飞满这么多帧就消失；0 = 交给引擎自己收
BULLET.Tint = u"#66ccff"                # 弹体染色（植物本体那条是 DEF.BodyTint，两套各管各的）
CustomBullets.Register(BULLET)          # 在脚本末尾、RegisterPlant 旁边

shot = CustomBullets.Fire(self.mBoard, self.Plant, u"mymod:ice_shard",
                          x, y, self.Plant.mRow)        # 抛物弹再传 ProjectileMotion.Lobbed
CustomBullets.Convert(现成的弹, u"mymod:ice_shard")      # 附魔：把飞行中的子弹换成这条记录
```

- `Pierce` 要配显式 `Damage`：留 0 是交给原生 `DoImpact` 结算，那一段自己会把弹带走。
- `Speed` 只对直线弹生效——引擎把直线弹位移写死成 3.33/帧（个别弹种 15），改 `mVelX` 没用，
  框架在原生那一帧之后把它换成声明值；抛物/落点弹道是时间曲线，动它会让弹落在别处，所以不动。
- 逐帧改弹道或换图：实现 `ICustomBulletMotion` 填到 `BULLET.Motion`（现成样子看巧克力大炮那一包）；
  命中完全自己算：实现 `ICustomProjectileImpact` 填到 `BULLET.Impact` —— 填了它，
  `Damage`/`Splash`/`Pierce`/`Poison` 这几条声明一条都不再参与（自检会点名这种配法）。
- `SkipCollision = True` 只跟 `Motion`/`Impact` 搭配用：没人算落点时它会一路飞出屏幕不打中任何东西。
- `Tint` 只管弹体那一张图；`Image` 也给了的话以 `Image` 为准。载体是火球那类走 reanim 的弹种
  染不了色（自检会点名）；染色图在发弹那一刻就备好，绘制那一趟不碰像素。
- `MaxAge` 管射程：引擎那句"小喷菇飞 75 帧就没了"是按 `mMotionType == Puff` 硬判的，
  斜着飞的弹（借 `ProjectileMotion.Star`）走不到那一支，所以这条声明在框架里补同一刀。
- 想照杨桃那样朝四个方向甩：传 `ProjectileMotion.Star` 给 `Fire`，再自己写 `shot.mVelX/mVelY`
  （`Star` 那一支按这两个速度逐帧推进，还会跟着更新 `mRow`，所以跨排打得着人）——
  寒冰忧郁菇的 `FireSpores` 就是这个写法。
- 弹体贴图要跟着方向转：引擎给杨桃星自转的那一支判的是"弹种 == Star"，借别的弹种拿不到，
  自己按速度写 `shot.mRotation = atan2(mVelY, mVelX)` 就行（画的时候绕格子中心转）。
- 数值配错（贴图没注册、穿透配 0 伤害、负数、填了 Impact 还写声明）由定义自检逐条点名，
  `PlantKit.DumpReport()` 的「素材与子弹」段能看见每条注册的子弹实际生效的形状。

## 定义与全局事件

- `DEF.CanPlantAt = System.Func[Board, int, int, bool](lambda b, x, y: ...)`：
  种植位置过滤器，返回 False 的格子不可种（格子高亮与预览同步消失）。
- `PlantKit.LevelStarted += handler(board)`、`PlantKit.WaveStarted += handler(board, wave)`、
  `PlantKit.ZombieDied += handler(zombie)`：全局事件，IronPython 直接 `+=`/`-=`，
  都在游戏线程触发，处理器抛异常只记日志不影响游戏。
- `PlantKit.PlantPlanted += handler(board, plant, def)` / `PlantKit.PlantRemoved += handler(board, plant, def)`：
  模组植物出现与离开棋盘。出现包括"种下、附件生成、存档恢复"三种来源，
  离开包括"铲掉、被吃、被压、清场"——做跨植物联动（例如"我场上一死一株就补一发"）用它，
  别为每一株单独挂 `Plant.Die`。判断是不是某种植物用 `def.IsMine(plant)` 或比对 `def.Seed`。
- `PlantKit.FindPlant(u"寒冰大喷菇")`：按显示名 / 包 ID / 种子号取定义。
  自定义种子没有枚举名，控制台里想拿它的 `SeedType` 就 `PlantKit.FindPlant(u"名字").Seed`。

## 调试与自检

- `PlantKit.DumpReport()`：一份"现在到底装了什么"的纯文本快照（环境路径、包顺序与启用状态、
  每株植物的实际字段与自动分配结果、Kit 钩子数与失败、受管钩子 key、素材与子弹清单、自检告警，
  宿主还会并进"C# 植物插件"一段）。返回全文，同时写到 `<mod 目录>\diagnostics.txt`；
  传路径可以改落点。任意线程可调，只做读取，各段独立 try。
- `PlantKit.ValidatePlants()`：核对所有已注册定义，把"几乎必然是笔误"的组合写成 WARN 行
  （卡图名没注册、`CardBackground` 不在 green/purple/gray、`SubClass=Shooter` 但 `LaunchRate=0`、
  名字重复、动画槽撞车、同时给 `NativeReanimType` 和 Spine/Reanim 名、Cost/RefreshTime=0、
  卡图既没自定义也没原生底图）。只报不改，排版（种子号 / 动画槽 / 卡图分配）完成时由框架自动跑一次
  ——早跑不得，那时每株都会吃一条"还没有种子号"的假告警；改完脚本想立刻看一眼结果就手动调，
  返回值是告警文本列表。
- `PlantKit.ManagedHookKeys`：当前生效的受管钩子 key（排序）。重跑脚本后长度不变才算没叠钩子。
- `PlantKit.AddReportSection(标题, lambda: 文本)`：给自己的包往诊断快照里加一段。

## 借原生身份（DEF 的十一个开关）

引擎里有一批**按 `mSeedType` 写死的特判**：蘑菇白天睡觉、开火后动作定格几帧、定格动画放多快、
开火挂什么特效、影子多大、身体和喷出的孢子染不染色、播哪条语音、能不能被升级卡覆盖、算不算真菌。
自定义种子不在那些名单里，一条都拿不到——所以"复用原生动画"只等于长得像，不等于行为像。
这十一项由 Kit 在对应的引擎方法里按声明补回去：

| 字段 | 补的是哪处特判 | 例子（寒冰大喷菇） |
|---|---|---|
| `Nocturnal` | `Plant.IsNocturnal`：白天关卡睡觉、可被咖啡豆唤醒、图鉴按夜间植物标注。（"睡↔醒切换动画"另说，见下） | `True` |
| `ShootingCounter` | `FindTargetAndFire` 里按种子写死的定格帧数；不填时自定义种子落到兜底 29 | `50` |
| `ShootingAnimRate` | 同一段定格动画的播放速率，引擎兜底分支写死 35；忧郁菇那条分支是 14（只动身体动画） | 寒冰忧郁菇 `14.0` |
| `DrawAsSeed` | `DrawShadow`（缩放/偏移）、`GetSleepingReanimOffset`、`UpdateShooting`（大喷菇那团 Fumecloud 烟雾就写在这个方法的种子分支里）——这三处只画不打分，所以整段临时换成该种子跑原方法，不抄坐标 | `SeedType.Fumeshroom` |
| `VoiceSeed` | `PlantVoice.Play`：引擎语音表只有 65 格，自定义种子会越界，所以以前一律不出声；填了就借那个种子的语音槽 | `SeedType.Fumeshroom` |
| `BodyTint` | `Plant.UpdateReanimColor` 里按种子写死的常驻染色（爆炸坚果红、冰墙果蓝），填 `"#rrggbb"`；手里举着能升级本株的卡时，引擎那下闪烁高亮仍然优先 | 寒冰两株 `u"#66ccff"` |
| `ParticleTint` | 本株开火喷出的粒子（Fumecloud / Gloomcloud 那几团）跟着染色，走引擎自己的 `TodParticleSystem.OverrideColor`。染的是"这株自己挂上来的那团特效"（`Plant.mParticleID`），每帧重贴；脚本里另外用 `mApp.AddTodParticle` 生成的要自己调 OverrideColor | 寒冰两株 `u"#66ccff"` |
| `UpgradesTo` | `Plant.IsUpgradableTo`：能不能被这张**原生**升级卡覆盖 | `SeedType.Gloomshroom` |
| `UpgradesToName` | 同上，但指向一张模组升级卡：只能按植物名，模组种子的 SeedId 要到首次排版才分配，脚本互相引用时拿不到对方的号（存档也按名字存） | `u"寒冰忧郁菇"` |
| `IsUpgradeCard` | `Plant.IsUpgrade`：声明"我是一张升级卡"。有了这条，点到不能被升级的东西上由游戏自己回 `NeedsUpgrade`（**不能直接种在空地上**）；卡槽里"场上没有可升级的株就不给拿、而且是灰的"这条也一起生效（`Board.PlantingRequirementsMet`，原生紫卡同一条规则，创造关卡勾了"简单升级"照样豁免）；挑战抽卡按"场上有没有可升级的株"筛；选卡界面只带了这张卡、没带基础卡时按开始，Kit 按原生同一道门弹警告拦下 | 寒冰忧郁菇 `True` |
| `IsFungus` | `Board.IsFungus`：真菌归类，影响"只用蘑菇+咖啡"挑战判定与相关成就 | `True` |

睡觉有两件事，别当成一件：**会不会睡**看 `Nocturnal`，**睡下/叫醒时播不播那套切换动画**看
`Plant.DoAdvancedSleeping` —— 引擎只给小喷菇、大喷菇、冰菇、海蘑菇开这条，忧郁菇不在里面。
Kit 按 `DrawAsSeed` 借的那个原生种子去问引擎自己，所以借大喷菇外形的有切换动画、
借忧郁菇外形的没有（和原生忧郁菇一致）；没写 `DrawAsSeed` 的才退回 `Nocturnal`。
把两者混成一个开关的后果实机见过：借忧郁菇外形的株会去播 `anim_sleep_to_idle`
（那轨在忧郁菇的 reanim 里只覆盖眼睛，身体整段看不见），而且切换完把待机重播成 30 帧/秒，
比原生忧郁菇的 10~15 快一倍。

升级这条链两边各写一半：**被升级的那株**写 `UpgradesToName`（或 `UpgradesTo`），**卡那一株**写
`IsUpgradeCard`。只写一半的话自检会点名（"UpgradesToName 找不到这株" / "IsUpgradeCard 但没人能被它升级"），
因为引擎的判定是 `拿着卡点上去 → 那株.IsUpgradableTo(卡种子)` 成不成立。
寒冰大喷菇 + 寒冰忧郁菇就是模组植物之间配对的一对样子。

两个刻意的取舍：`ShootingCounter` / `ShootingAnimRate` 用**数值覆盖**而不是换种子跑
`FindTargetAndFire`——那个方法内部会回调本株的 `Fire`，换着身份跑等于把伤害也换成借来的那份；
`DrawAsSeed` 只覆盖绘制与特效，不碰索敌和伤害。索敌矩形/伤害标记这类要借的，仍然得自己按
`IceFumeShroom.py` 的写法临时换 `mSeedType` 再调 `orig`。

`DrawAsSeed` 别指向忧郁菇、机枪射手、香蒲、三线射手、杨桃、火爆辣椒、仙人掌、各类投手、
玉米加农炮——`UpdateShooting` 对这些种子有"再开一枪"的分支，借它们的外形会顺带多打一枪；
定义自检会点名这种配法。

## 传统 MonoMod 钩子的受管接入

Kit 预定义事件没覆盖的场景（例如任意僵尸开饭、子弹命中细节），可以用社区习惯的
`LawnMod.MonoModUtils.HookTo` 写法直接挂引擎方法，再交给 Kit 托管。两种托管方式：

**实例钩子（随植物生死）**——`self.ManageHook(...)`，植物被移除时自动卸载：

```python
from Lawn import Zombie
from LawnMod import MonoModUtils as M

class MyPlant(CustomPlantBase):
    def OnPlanted(self):
        # OnPlanted 每株只跑一次，不需要手写重跑守卫。
        self.ManageHook(M.HookTo(Zombie.StartEating)(self._on_eating))

    def _on_eating(self, orig, zombie):
        orig(zombie)          # 传统写法：显式转发原实现
        ...                   # 自定义逻辑（自行过滤与自己的相关性）
```

**全局钩子（随脚本重跑替换）**——`PlantKit.ManageHook(key, ...)`，脚本顶层调用；
同名 key 再挂会先卸旧再挂新，反复重跑脚本零样板幂等：

```python
# CurrentPackId 是 [ThreadStatic]：宿主加载时为包 ID，WS 控制台线程上为 None，留同名兜底
PACK_ID = PlantKit.CurrentPackId or u"MyPlant"
PlantKit.ManageHook(PACK_ID + u".watch_die",
                    M.HookTo(Plant.Die)(_watch_die))
PlantKit.UnmanageHook(PACK_ID + u".watch_die")        # 不用了就卸掉
PlantKit.UnmanageHooks(PACK_ID + u".")                # 卸掉本包全部钩子
```

前缀直接用包 ID，不要另编一个名字：`.py` 植物的包 ID 就是它的文件名（`IceFumeShroom.py` →
`IceFumeShroom`，见 `FolderPlantLoader` 取 `Path.GetFileNameWithoutExtension`，并在执行脚本前
调 `PlantKit.BeginPack`），和 `plant_packs.json` 里的条目同名。这样按包清理钩子的前缀匹配、
日志和清单三者对得上；自己编的 `pgvz.xxx` 之类只会成为一个对不上号的第二套命名。

相对裸写钩子解决的问题：

1. **终结器崩溃**：`HookResult` 被回收时终结器会再调一次 `UnHook`（非幂等，直接崩进程）。
   受管后引用由注册表永久持有，终结器永不运行。
2. **重跑脚本重复挂钩**：全局钩子按 key 自动替换，实例钩子挂在 `OnPlanted` 里天然一次。
3. **植物死了钩子还活着**：实例钩子随植物移除自动卸载。

注意：`HookTo` 的既有限制仍然适用——不要用它挂 ref/out 参数的方法；
重载方法会按同名首个绑定，混入参数类型可降低歧义。


示例植物生产（ExamplePlantProducer）演示了复用原生向日葵身体/卡图、每 20 秒产 3 个阳光，以及不手填任何布局字段。
