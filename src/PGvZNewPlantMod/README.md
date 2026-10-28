# PGvZNewPlantMod（宿主 + 9 个植物插件）

基于 [PGvZPlantKit](../PGvZPlantKit/README.md) 的九种植物实现。部署后顶层只有一个
`PGvZNewPlantMod.dll`；九种植物分别是 `../Plants/` 下的独立工程，编译成 9 个插件 DLL，
由宿主从 `PGvZNewPlantMod\plants\` 反射加载。

| 项目 | 内容 |
| --- | --- |
| `../PGvZPlantKit` | 可复用框架 DLL：注册表、基类、`IPlantPack`、UI、存档、通用弹道 |
| `Bootstrap.cs` | 从同名文件夹加载 `PGvZPlantKit.dll` |
| `ModHost.cs` | 宿主入口：清单路径、框架、插件、Python 植物 |
| `ManifestAutoDiscovery.cs` | 新植物自动补清单并启用 |
| `PlantPluginLoader.cs` | 扫描 `plants\*.dll`，发现 `IPlantPack` 并注册 |
| `FolderPlantLoader.cs` | 逐个加载 `plants\*.py`（文件名即包 ID） |
| `../Plants/<Name>/` | 一株植物一个工程：植物类 + `*Pack.cs` + 专属钩子 + `Assets/` |
| `../../package/PGvZNewPlantMod/` | 运行时数据模板：清单、示例植物、assets |

## 9 个插件

| 工程 / DLL | 植物 | 包 ID | 种子号 | 专属钩子 |
| --- | --- | --- | --- | --- |
| `SuperGatling` | 超级机枪射手 | `pgvz.super_gatling` | 自动 | 无 |
| `Fjth` | 伏僵塔黄 | `pgvz.fjth` | 自动 | 拉拽/冰车/压扁/割草机 4 |
| `Dmxj` | 电鳗香蕉 | `pgvz.dmxj` | 自动 | 棋盘更新/绘制 2 |
| `Ssbps` | 桑葚爆破手 | `pgvz.ssbps` | 自动 | 棋盘更新/绘制 2 |
| `Qgxlj` | 球果训练家 | `pgvz.qgxlj` | 自动 | 僵尸绘制/棋盘更新/绘制 3 |
| `Nbjqs` | 牛蒡击球手 | `pgvz.nbjqs` | 自动 | 棋盘更新/绘制 2 |
| `Czlb` | 瓷砖萝卜 | `pgvz.czlb` | 自动 | 种植判定/棋盘更新/背景绘制 4 |
| `Hjbl` | 黄金蓓蕾 | `pgvz.hjbl` | 自动 | 无 |
| `Swg` | 守卫菇 + 小守卫菇（隐藏） | `pgvz.swg` | 自动 | 烟雾联动/僵尸目标/跳跃/棋盘更新 7 |

每株植物使用独立包 ID：

```text
pgvz.super_gatling  pgvz.fjth  pgvz.dmxj  pgvz.ssbps  pgvz.qgxlj
pgvz.nbjqs  pgvz.czlb  pgvz.hjbl  pgvz.swg
```

`plant_packs.json` 可以逐株启用/排序；删除某个插件 DLL 只会移除那一株，宿主仍能启动。

## 游戏内容

| 种子号 | 植物 | 阳光 | 冷却 |
| --- | --- | --- | --- |
| 自动 | 超级机枪射手 | 400 | 750 |
| 自动 | 伏僵塔黄 | 225 | 750 |
| 自动 | 电鳗香蕉 | 125 | 750 |
| 自动 | 桑葚爆破手 | 150 | 750 |
| 自动 | 球果训练家 | 300 | 750 |
| 自动 | 牛蒡击球手 | 100 | 750 |
| 自动 | 瓷砖萝卜 | 每次种植后翻倍 | 750 |
| 自动 | 黄金蓓蕾 | 0 | 1800 |
| 自动 | 守卫菇 | 100 | 750 |
| 自动 | 小守卫菇（隐藏实体） | — | — |

## IronPython 示例

`package\PGvZNewPlantMod\plants\` 随包提供 4 个可运行示例，全部使用自动 SeedId：

| 文件 / 包 ID | 植物 | 机制 |
| --- | --- | --- |
| `ExamplePlantProducer.py` | 示例植物生产 | 周期产阳光 |
| `ExamplePlantShooter.py` | 示例植物射手 | `PlantSubClass.Shooter`，`OnNativeFire` 生成豌豆 |
| `ExamplePlantPult.py` | 示例植物投手 | Shooter 索敌 + `ProjectileMotion.Lobbed` 抛物线 |
| `ExamplePlantSpecialBomb.py` | 示例植物特殊炸弹 | 延迟后调用原生 `Plant.DoSpecial()` 做范围爆炸 |

保留选卡、模仿者、上次选卡/预设卡组、图鉴、卡槽冷却、光标预览、
毒/减速/眩晕/击退、往返弹、空投、召唤物、关卡存档侧车。

未移植 Python 版的修改器/TAS/手控放置等与植物无关的入口。

## 构建

在仓库根目录执行：

```bat
dotnet build PGvZNewPlantMod.slnx -c Release
```

宿主工程通过 `ProjectReference`（`ReferenceOutputAssembly=false`）驱动 9 个插件一起构建，
再把插件 DLL 复制到数据目录：

- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZPlantKit.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod\plants\PGvZNewPlantMod.*.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod\`（清单、示例、assets）

## 部署

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\部署.ps1 `
  -GameRoot "D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies"
```

安装结果：

```text
mods\
├── PGvZNewPlantMod.dll
└── PGvZNewPlantMod\
    ├── PGvZPlantKit.dll
    ├── plant_packs.json
    ├── plants\
    │   ├── PGvZNewPlantMod.Czlb.dll
    │   ├── ...（9 个插件）
    │   └── ExamplePlantProducer.py
    └── assets\
```

九种植物的 Spine 已内嵌在插件 DLL 中，不再复制到游戏 `Content\spanim`。插件 DLL 始终
覆盖；清单、Python 植物和卡图保留用户修改。旧的顶层 kit/示例/清单会移到 `.bak`。

## 加载流程

1. 游戏加载顶层 `PGvZNewPlantMod.dll`，`PerformModuleReload` 调用 `ModHost.Load()`。
2. `LoadOrder.SetManifestPath` 指向 `PGvZNewPlantMod\plant_packs.json`。
3. `ManifestAutoDiscovery` 扫描 `plants\*.dll` 和 `*.py`，把清单未列出的包追加为
   `enabled=true`；已有条目和顺序不改。
4. `NinePlantModule.RegisterBuiltins` 调用 `PlantKit.Install()` 并注册宿主共享资源。
5. `PlantPluginLoader` 扫描 `plants\*.dll`，按 `IPlantPack.Order` 排序并调用
   `Register(packId)`；九植物各用自己的 `pgvz.*` 包 ID，新插件默认用 DLL 文件名。
6. `FolderPlantLoader` 按清单顺序执行 `plants\*.py`，按文件名注册 `assets\*.png`。
7. 首次打开图鉴/选卡或种植时统一提交自动 SeedId 和自动排版。

## 代码结构

- `../PGvZPlantKit/Core/CustomPlantBase.cs`：行为基类（身体、血量、对齐、回调）。
- `../PGvZPlantKit/Core/CustomPlantDef.cs` + `CustomPlantRegistry.cs`：定义与注册表。
- `../PGvZPlantKit/IPlantPack.cs`：插件接口和 `PlantPackBase`。
- `../PGvZPlantKit/Core/CustomPlantHooks.cs`：61 个通用钩子（含自定义种子定义读取和 Spine 跨平台回退）。
- `../PGvZPlantKit/Core/LoadOrder.cs`：`plant_packs.json` 排序、启停、自动补条目。
- `PlantPluginLoader.cs`：插件发现、排序、注册与失败统计。
- `../Plants/<Name>/*.cs`：植物类、`*Pack.cs` 入口、`*Extras.cs` 专属钩子。

新增 C# 植物：复制一个 `../Plants` 工程，写 `CustomPlantBase` 子类和 `PlantPackBase` 入口，
把工程加进宿主 csproj 的构建列表；输出 DLL 会被复制到 `plants\`。不放 csproj 也可以手工把
DLL 放进 `plants\`，宿主会自动发现并把未列出的包追加到清单。入口类不覆写 `PackId` 时，
用 DLL 文件名作为包 ID；需要自定义时写 `public override string PackId => "my.pack";`。

## 核验

- `tests\VerifyHost`：9 个插件包、61 个核心钩子 + 24 个插件专属钩子、14 个定义
  （九植物 + 小守卫菇 + 4 个 Python 示例）、注册资源 92、内嵌 Spine 加载通过、失败 0，
  退出码 0。
- 自动 SeedId 验证：14 个定义得到 94–107，重复 0，低于 94 的碰撞 0。
- 宿主不内嵌图片：通用卡槽背景在 `PGvZPlantKit`，每株的卡图与动画由各自插件内嵌。
- 移除测试：移走 `PGvZNewPlantMod.Swg.dll` 后得到 8 个包、9 个定义、退出码 2；
  宿主本身仍正常加载，说明没有编译期植物依赖。
- 清单自动登记实测：新增 `auto_test.py` 后清单自动追加并加载；新增无 `PackId` 的
  `AutoTestPlugin.dll` 后清单自动追加 `AutoTestPlugin` 并注册成功。
- 实机：9 个插件全部注册、24 个专属钩子安装成功，图鉴可打开，`plant_kit.log` 0 ERROR。

## 跨平台

- 宿主先按程序集位置找 mod 目录；位置为空时回退到
  `<工作目录>/mods/PGvZNewPlantMod`。Windows 工作目录是
  `%AppData%\ZBC\PlantGirlsVsZombies`，Android 是
  `Android/data/net.pvz.pgvz.zbcteam/files`。
- 游戏读取 `Content/spanim/...` 失败时，`TitleContainer.OpenStream` 钩子会读取插件 DLL
  内嵌的 Spine，所以发布包不需要 `spanim` 目录，也不需要改 APK。
- 日志和侧车存档写在 mod 目录，两个平台都可读写。
- 当前 DLL 按 PC 1.3.2 编译；Android APK 为 1.3.1，实机兼容性仍需在 Android 设备复核。

## 日志

`<mod目录>/plant_kit.log`

Windows：`%AppData%\ZBC\PlantGirlsVsZombies\mods\PGvZNewPlantMod\plant_kit.log`

Android：`Android/data/net.pvz.pgvz.zbcteam/files/mods/PGvZNewPlantMod/plant_kit.log`

侧车存档：`<mod目录>/SaveData/`
