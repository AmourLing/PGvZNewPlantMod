# PGvZNewPlantMod 数据目录

顶层只有 `mods\PGvZNewPlantMod.dll`；本文件夹由该 DLL 读取。`PGvZPlantKit.dll`
和 19 个植物插件 DLL 都必须留在本文件夹里，不要移到 mods 顶层。
Windows / Android 共用同一份目录；自带动画的植物把 Spine 或经典逐帧 `.reanim` 都嵌在各自插件 DLL 中，
本目录不再需要 `spanim` 文件夹，也不需要复制到游戏 `Content\spanim`。

```text
mods/
├── PGvZNewPlantMod.dll          <- 唯一的顶层 mod DLL
└── PGvZNewPlantMod/
    ├── PGvZPlantKit.dll         <- 框架库
    ├── plant_packs.json         <- 加载顺序/启用（保留你的修改）
    ├── plant_packs.template.json
    ├── plants/
    │   ├── PGvZNewPlantMod.SuperGatling.dll  <- 超级机枪射手
    │   ├── PGvZNewPlantMod.Fjth.dll          <- 伏僵塔黄
    │   ├── PGvZNewPlantMod.Dmxj.dll          <- 电鳗香蕉
    │   ├── PGvZNewPlantMod.Ssbps.dll         <- 桑葚爆破手
    │   ├── PGvZNewPlantMod.Qgxlj.dll         <- 球果训练家
    │   ├── PGvZNewPlantMod.Nbjqs.dll         <- 牛蒡击球手
    │   ├── PGvZNewPlantMod.Czlb.dll          <- 瓷砖萝卜
    │   ├── PGvZNewPlantMod.Hjbl.dll          <- 黄金蓓蕾
    │   ├── PGvZNewPlantMod.Swg.dll           <- 守卫菇 + 小守卫菇
    │   ├── ExamplePlantProducer.py           <- 周期产阳光示例
    │   ├── ExamplePlantShooter.py            <- Shooter 射手示例
    │   ├── ExamplePlantPult.py               <- 投手/抛物线示例
    │   └── ExamplePlantSpecialBomb.py        <- DoSpecial 灰烬示例
    ├── assets/
    │   ├── README.md
    │   └── my_card.png          <- 可选卡图，按文件名注册
    ├── SaveData/                <- 进关卡后生成
    └── plant_kit.log            <- 运行日志
```

宿主先扫描 `plants\*.dll`（C# 插件），再执行 `plants\*.py`（IronPython）。每个 C# 插件
各有独立包 ID：`pgvz.super_gatling`、`pgvz.fjth`、`pgvz.dmxj`、`pgvz.ssbps`、`pgvz.qgxlj`、
`pgvz.nbjqs`、`pgvz.czlb`、`pgvz.hjbl`、`pgvz.swg`、`pgvz.fantian`、
`pgvz.seedpacket_imitater`、`pgvz.seedpacket_flower`、`pgvz.plant_buster`、`pgvz.doom_cannon`、
`pgvz.hua_huo_long`、`pgvz.ares`、`pgvz.chocolate_cannon`、`pgvz.loki`、`pgvz.teitisi`。
守卫菇 DLL 同时注册隐藏的小守卫菇。
删除某个插件 DLL 只会移除那一株，宿主仍能启动。

最早移植的九株（超级机枪射手、伏僵塔黄、电鳗香蕉、桑葚爆破手、球果训练家、牛蒡击球手、
瓷砖萝卜、黄金蓓蕾、守卫菇）已**废弃并停止收录**：本版不再随包提供它们的插件 DLL，
选卡与图鉴里都不会再出现。含这些植物的旧存档仍能读取——被删掉的植物会以坚果占位
回到场上，并在 `plant_kit.log` 里逐条写 `save references a removed custom entry`，
不会把存档打成 `.crash`。

## 新增一株 IronPython 植物

1. 在 `plants/` 新建 `my_plant.py`。建议文件名只用小写字母、数字、`_`；
   文件名（不含 `.py`）就是包 ID，与 `plant_packs.json` 里的 `id` 对应。
2. 写定义和行为。宿主已经安装框架并预加载 Lawn / PGvZPlantKit，不需要 `clr` 或路径处理：

```python
import System

import Lawn
from Lawn import SeedType
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
DEF.Name = u"我的植物"
DEF.Cost = 100
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Peashooter
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Peashooter
DEF.StandAnim = ""

class MyPlant(CustomPlantBase):
    def OnUpdate(self):
        pass

DEF.Factory = System.Func[CustomPlantBase](lambda: MyPlant())
PlantKit.RegisterPlant(DEF)
```

3. 不需要手动改清单。宿主启动时会发现 `my_plant.py` 不在 `plant_packs.json` 里，
   自动把 `{"id":"my_plant","enabled":true}` 追加到末尾并启用。
   想固定顺序/禁用时再手动编辑清单；已有条目不会被重排。

脚本里可以直接使用宿主注入的变量：

- `__file__`：当前 `.py` 的绝对路径；
- `__name__`：包 ID；
- `PLANT_FOLDER`、`ASSET_FOLDER`、`MOD_FOLDER`：对应目录的绝对路径。

`PlantKit.RegisterPlant(DEF)` 会自动归到当前文件的包；需要显式指定时传第二个参数。
一个种子号只能注册一次；留 `SeedId=0` 由库自动分配。

## 新增 C# 插件

复制 `src\Plants\` 下的一个工程，写植物类和 `PlantPackBase` 入口，把该植物的资源放进工程内的
`Assets\`，编译后把 DLL 放进 `plants\` 即可；宿主会自动发现实现 `IPlantPack` 的类型，并把
未列出的包追加到清单。不覆写 `PackId` 时用 DLL 文件名作为包 ID；需要自定义时在入口类写
`public override string PackId => "my.pack";`。插件通过
`PlantKit.RegisterEmbeddedImages` 内嵌自己的卡图/特效图。接口示例见
[PGvZPlantKit/README.md](../../src/PGvZPlantKit/README.md)。

## 卡图 / Spine

- IronPython 卡图放进 `assets/`，例如 `assets/my_card.png`，脚本里写
  `DEF.CardAsset = "my_card"`；宿主会自动按文件名注册，不需要写路径。
- C# 插件的卡图/特效图/Spine 都内嵌在各自 DLL 中，不经过 `assets/`。
- IronPython 植物若要自带 Spine，可把资源放进插件 DLL 或改用原生动画；自带的示例植物
  已经不需要外部 `spanim`。
- 不提供卡图/Spine 时可用原生向日葵、豌豆等身体和卡面作为占位。

## 加载顺序

`plant_packs.json` 与精华版 `respack` 的规则一致：

- `packs` 数组顺序即加载顺序，前面优先；
- `enabled=false` 的包不执行对应 `.py`，也不注册其植物；例如关闭 `pgvz.swg` 只跳过
  守卫菇，其他八株不受影响；
- `before` / `after` 可用包 ID 或数组；`dependencies` 等价于本包 after 这些包；
- 未列出的 `.py` 文件或 C# 插件 DLL，宿主启动时会自动追加到清单末尾并设为
  `enabled=true`；已有条目、顺序和 `before/after` 不变；
- 大小写不敏感；修改后重启游戏。

`chooser_order` 写在**植物** `CustomPlantDef` 上（不是包里），值越小，自动图鉴/选卡位置越靠前；
相同时按包顺序、文件加载顺序。显式填了种子号/图鉴格/选卡格的植物不受自动排序影响。
自动 SeedId 会随包顺序变化，涉及存档的植物建议显式固定 `SeedId`。

## 内置内容

10 种精华版移植植物以 10 个 C# 插件 DLL 的形式位于 `plants/`，每个插件使用自己的包 ID
（`pgvz.fantian` 到 `pgvz.teitisi`）。`ExamplePlant*.py` 4 个是随包的 IronPython 示例；
两种方式可以共存。

## 日志与存档

- `PGvZNewPlantMod/plant_kit.log`：插件加载、注册、自动分配、钩子与脚本错误。
- 模组植物随游戏自己的关卡存档一起保存和读取：中途退出再"继续游戏"，场上的模组植物、
  它们的生长阶段和已上膛的子弹都会回来。
- `PGvZNewPlantMod/SaveData/`：引擎存档放不下的额外记录（瓷砖分布、小守卫菇站位等），
  按存档文件配对，删掉对应存档时一起清理即可。

插件 DLL 加载失败会写 `plant plugin failed` / `plant pack failed`；Python 脚本失败会写
`folder plant failed`。其他植物仍会继续加载。
