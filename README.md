# PGvZNewPlantMod

一个顶层宿主 DLL + 同名数据文件夹的自定义植物 mod，目标 PGvZ 1.3.2 / net6.0。
19 种 C# 植物是 19 个独立插件 DLL，IronPython 植物仍是一文件一株。
运行路径和 Spine 加载已按 Windows / Android 共用设计；发布包是一个 zip，直接解压进
对应平台的 `mods` 目录即可。

## 源码结构

```text
PGvZNewPlantMod/
├─ PGvZNewPlantMod.slnx
├─ README.md
├─ src/
│  ├─ PGvZPlantKit/                  可复用框架库
│  │  ├─ PGvZPlantKit.csproj
│  │  ├─ README.md
│  │  ├─ PlantKit.cs
│  │  ├─ IPlantPack.cs
│  │  ├─ Assets/                     通用卡槽背景 green/purple/gray
│  │  └─ Core/                       注册表、钩子、UI、存档、布局、加载顺序
│  ├─ PGvZNewPlantMod/               宿主工程
│  │  ├─ PGvZNewPlantMod.csproj
│  │  ├─ README.md
│  │  ├─ Assets/                     宿主自带的共享 PNG
│  │  ├─ PythonModule.cs
│  │  ├─ ModHost.cs
│  │  ├─ ManifestAutoDiscovery.cs
│  │  ├─ PlantPluginLoader.cs
│  │  ├─ FolderPlantLoader.cs
│  │  └─ Bootstrap.cs
│  └─ Plants/                        9 个在编译的植物插件工程
│     ├─ Directory.Build.props
│     ├─ FanTian/                    创造神（复制上一张卡）
│     ├─ SeedPacketImitater/         卡片模仿者
│     ├─ SeedPacketFlower/           卡片花
│     ├─ PlantBuster/                植物吞噬者
│     ├─ DoomCannon/                 死神大炮（自动装填的玉米炮）
│     ├─ HuaHuoLong/                 花火龙，经典逐帧动画
│     ├─ Ares/                       战神，前后双向齐射
│     ├─ LuoJi/                      火神，给穿过的子弹附魔
│     ├─ TeiTisi/                    水神，反弹子弹并上毒
│     └─ _archived/                  10 个已废弃、不再编译/部署/打包的工程
│                                    （SuperGatling、Fjth、Dmxj、Ssbps、Qgxlj、
│                                     Nbjqs、Czlb、Hjbl、Swg、ChocolateCannon）
├─ package/
│  └─ PGvZNewPlantMod/               运行时数据模板
│     ├─ plant_packs.json
│     ├─ plant_packs.template.json
│     ├─ plants/*.py                 （含 star_dust、py_shooter、py_pult、py_ash）
│     └─ assets/
├─ tests/
│  └─ VerifyHost/                    离线钩子/资源/加载验证
├─ tools/
│  ├─ 部署.bat / 部署.ps1           本机安装
│  ├─ 打包.py / 打包.cmd            候选包
│  └─ 发布.cmd                      正式包
├─ publish/                          生成的发布 zip
├─ 使用说明模板.txt                  玩家安装说明模板
├─ update.md                         下一版更新说明草稿
├─ 已发布.json                       正式发布记录
└─ docs/
   ├─ ironpython.md                  IronPython 植物说明
   └─ 安装与分发.md                  Windows/Android 安装说明
```

每个植物工程一般包含：

```text
src/Plants/Xxx/
├─ Xxx.csproj
├─ XxxPlant.cs       植物行为
├─ XxxPack.cs        IPlantPack 入口、包 ID、图鉴文字、注册
├─ XxxExtras.cs      专属钩子（部分植物没有）
└─ Assets/           该植物自己的卡图/弹体图
```

## 安装后的结构

```text
%AppData%\ZBC\PlantGirlsVsZombies\mods\
├── PGvZNewPlantMod.dll                      <- 唯一顶层 mod DLL
└── PGvZNewPlantMod\
    ├── PGvZPlantKit.dll                     <- 框架库
    ├── plant_packs.json                     <- 植物包顺序/启用
    ├── plants\
    │   ├── PGvZNewPlantMod.SuperGatling.dll <- 超级机枪射手
    │   ├── PGvZNewPlantMod.Fjth.dll         <- 伏僵塔黄
    │   ├── PGvZNewPlantMod.Dmxj.dll         <- 电鳗香蕉
    │   ├── PGvZNewPlantMod.Ssbps.dll        <- 桑葚爆破手
    │   ├── PGvZNewPlantMod.Qgxlj.dll        <- 球果训练家
    │   ├── PGvZNewPlantMod.Nbjqs.dll        <- 牛蒡击球手
    │   ├── PGvZNewPlantMod.Czlb.dll         <- 瓷砖萝卜
    │   ├── PGvZNewPlantMod.Hjbl.dll         <- 黄金蓓蕾
    │   ├── PGvZNewPlantMod.Swg.dll          <- 守卫菇 + 小守卫菇
    │   └── star_dust.py                     <- IronPython 示例
    ├── assets\                              <- 用户卡图
    ├── SaveData\                            <- 首次进关卡后生成
    └── plant_kit.log                        <- 运行日志
```

## 加载方式

1. 宿主定位同名文件夹，把 `plant_packs.json` 设为清单路径。
2. 扫描 `plants\*.dll` 和 `plants\*.py`。发现清单没有的植物包时自动追加
   `{"id":"包ID","enabled":true}`；已有条目、顺序和 `before/after` 不变。
3. 加载 `PGvZPlantKit.dll`，调用 `PlantKit.Install()`。
4. 扫描 `plants\*.dll`，按 `IPlantPack.Order` 排序后注册。每株各有独立包 ID
   （`pgvz.super_gatling`、`pgvz.fjth`、`pgvz.dmxj`、`pgvz.ssbps`、`pgvz.qgxlj`、
   `pgvz.nbjqs`、`pgvz.czlb`、`pgvz.hjbl`、`pgvz.swg`、`pgvz.fantian`、
   `pgvz.seedpacket_imitater`、`pgvz.seedpacket_flower`、`pgvz.plant_buster`、
   `pgvz.doom_cannon`、`pgvz.hua_huo_long`、`pgvz.ares`、`pgvz.chocolate_cannon`、
   `pgvz.loki`、`pgvz.teitisi`）；新插件没有显式 `PackId`
   时用 DLL 文件名。
5. 已废弃的工程（首批九株 + 巧克力大炮）整个挪进 `src/Plants/_archived/`，不再编译，
   也就不再注册定义；工程内的 `Pack` 仍写 `Deprecated = true`，把目录移回来即可复现【已废弃】标记。
   VerifyHost 断言两件事：加载到的定义里没有任何 `Deprecated`，插件目录里也没有归档工程的
   残留 DLL（名单按 `_archived/` 目录现算，归档新工程不用改门）。
5. 每个插件注册自己的资源、定义和专属钩子；宿主不引用任何植物类型。
6. 按清单执行 `plants\*.py`，自动注册 `assets\*.png`。
7. 首次打开图鉴/选卡或种植时提交自动 SeedId 和自动排版。

删除某个 `PGvZNewPlantMod.*.dll` 只会移除那一株植物；守卫菇 DLL 同时包含隐藏的小守卫菇。
宿主不再内嵌图片：通用卡槽背景（green/purple/gray）内嵌在 `PGvZPlantKit`，
每株自己的卡图与逐帧动画内嵌在各自的插件 DLL 里。

## 构建

```bat
cd /d "D:\植物大战僵尸\WP_PGVZ\my_mods\PGvZNewPlantMod"
dotnet build PGvZNewPlantMod.slnx -c Release
```

产物：

- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZPlantKit.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod\plants\PGvZNewPlantMod.*.dll`
- `src\PGvZNewPlantMod\bin\Release\net6.0\PGvZNewPlantMod\`（清单、示例植物、assets）

新建一株 `.py` 植物：把文件丢进 `package\PGvZNewPlantMod\plants\` 就行——工程按
`package\PGvZNewPlantMod\**` 通配收它并复制到输出，解决方案里**不需要**登记任何一条文件。
（`.slnx` 的解决方案文件夹到今天还不支持通配，[vs-solutionpersistence #61](https://github.com/microsoft/vs-solutionpersistence/issues/61) 还开着；
所以这些 `.py` 在解决方案资源管理器里挂在 **PGvZNewPlantMod 工程节点 → PGvZNewPlantMod\plants\** 下面，
那份挂在解决方案层的旧手写清单已经删掉了——它当初只列了 4 个文件，寒冰那两张一直不在里面。）

## 部署

双击 `tools\部署.bat`，或：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\部署.ps1 `
  -GameRoot "D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies"
```

脚本会安装宿主、框架、清单、插件 DLL 和 Python 植物，并保留已有的清单、Python
植物和用户卡图。Spine 已内嵌在插件 DLL 中。安装后完全关闭并重启游戏。

## 分发打包

与 PGvZOnlineMod / PvZWSTools_Inner / 植物娘AI对话 使用同一套约定：

```bat
tools\打包.cmd          :: 候选包
tools\发布.cmd          :: 正式包（先写 update.md，确认后输入 yes）
```

输出：

```text
publish\PGvZNewPlantMod_CSharp_发布_YYYYMMDD_HHMMSS.zip
```

压缩包内不套 `mods/`，直接解压进游戏的 `mods` 目录：

```text
PGvZNewPlantMod.dll
PGvZNewPlantMod/
├─ PGvZPlantKit.dll
├─ plant_packs.json
├─ plants/...
└─ assets/
使用说明.txt
update.md                 ← 仅正式包
```

- Windows：解压到 `%AppData%\ZBC\PlantGirlsVsZombies\mods`。
- Android：解压到
  `/storage/emulated/0/Android/data/net.pvz.pgvz.zbcteam/files/mods`。
- 不要再套一层 `PGvZNewPlantMod-版本号` 或 `mods\mods`。
- 九种植物的卡图、弹体图和 Spine 都嵌在各自插件 DLL 中；`TitleContainer` 打不开游戏
  原路径时会读取这些内嵌资源，所以不需要 `spanim` 目录，也不需要改 APK。
- 当前 DLL 用 PC 1.3.2 程序集编译，Android 1.3.1 的实机兼容性仍以设备复核结果为准。

详细步骤和版本注意事项见 [安装与分发](docs/安装与分发.md)。

## 新增植物

- IronPython：在 `mods\PGvZNewPlantMod\plants\` 放 `my_plant.py`，启动时自动加入清单。
  随包示例 `py_shooter.py`、`py_pult.py`、`py_ash.py` 分别演示
  `PlantSubClass.Shooter`、抛物线投掷和原生 `Plant.DoSpecial()`。
- C#：复制 `src\Plants\` 下的一个工程，把植物资源放进该工程的 `Assets\`，输出 DLL 放进
  `plants\`。宿主会自动发现并补清单；入口类不覆写 `PackId` 时用 DLL 文件名作为包 ID。

详细写法见 [IronPython 说明](docs/ironpython.md)、
[PGvZPlantKit README](src/PGvZPlantKit/README.md) 和
[数据目录说明](package/PGvZNewPlantMod/README.md)。

## 验证

```bat
dotnet run --project tests\VerifyHost -c Release
```

当前结果：9 个插件包、69 个核心钩子 + 13 个插件专属钩子、13 个植物定义、96 张资源，
自动 SeedId 为 94–106（重复 0、低于 94 的碰撞 0），经典逐帧动画加载通过，
存档编码往返（原生/自定义枚举、乱序历史 ID、缺失键回退占位、图片引用）通过，退出码 0。
首批九株已归档到 `src/Plants/_archived/`：不在编译/部署/打包范围内，
输出的 `plants/` 目录由构建目标自动剪掉旧副本。
实机已验证插件启动、图鉴、选卡、种植与战斗；存档用隔离沙盒跑完
存→读→再存 的逐字段比对，并退出进程后从游戏自己的"继续存档"入口重读同一份存档。

## 日志与存档

- 日志：`%AppData%\ZBC\PlantGirlsVsZombies\mods\PGvZNewPlantMod\plant_kit.log`
- 关卡存档：模组植物写进游戏自己的关卡存档（`docs/userdata/game<档案>_<模式>.dat`），
  植物身份用 `包ID:植物名` 而不是数字 SeedId，所以增删包不会让旧存档串位。
- 侧车存档：`%AppData%\ZBC\PlantGirlsVsZombies\mods\PGvZNewPlantMod\SaveData\`
  放游戏引擎装不下的额外记录（瓷砖分布、小守卫菇站位等），按存档文件配对。
