// 离线验证宿主：不改存档、不启动游戏，只模拟加载流程并核验每一条 Hook 的签名。
using System.Reflection;
using PGvZPlantKit;
using Sexy;

string gameDir = @"D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies\LawnDLL";
var pending = new HashSet<string>();
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    string name = new AssemblyName(e.Name).Name;
    if (!pending.Add(name))
    {
        return null;
    }
    string file = Path.Combine(gameDir, name + ".dll");
    return File.Exists(file) ? Assembly.LoadFrom(file) : null;
};

Assembly asm = typeof(PGvZNewPlantMod.ModHost).Assembly;
Console.WriteLine("加载: " + asm.Location);

var attr = asm.GetCustomAttributesData()
    .FirstOrDefault(a => a.AttributeType.Name == "PythonModuleAttribute");
if (attr == null)
{
    Console.WriteLine("FAIL: 缺少 [PythonModule] 特性");
    return 1;
}
Console.WriteLine("PythonModule: " +
    string.Join(", ", attr.ConstructorArguments.Select(a => a.Value)));

// 拆成插件工程之后，宿主不再内嵌任何图片：通用卡槽背景在 PGvZPlantKit，
// 每株自己的卡图/逐帧动画在各自的插件 DLL 里。旧版"宿主内嵌 5 张共享图"的断言
// 是拆分前的遗留，每次跑都白报一条 WARN（喊多了就没人看警告了），改成只报数；
// 素材齐不齐由后面「注册资源总数 / 关键资源缺失」那两条真门负责。
string[] resources = asm.GetManifestResourceNames();
Console.WriteLine("宿主内嵌资源数: " + resources.Length + "（图片在 kit 与各插件里）");

PGvZNewPlantMod.ModHost.Load();
CustomPlantRegistry.EnsureDefinitions();
int coreHooks = CustomPlantHooks.HookCount;
int extraHooks = PGvZNewPlantMod.PlantPluginLoader.HookCount;
Console.WriteLine("插件包: " + PGvZNewPlantMod.PlantPluginLoader.PackCount +
    "，插件专属 Hook: " + extraHooks);
Console.WriteLine("Hook 成功: " + coreHooks + " core + " + extraHooks + " plugin extras");
int spineFallback = 0;
// 这条探针验的是"插件 DLL 内嵌的 Spine 能不能按游戏的虚拟路径读到"
// （TitleContainer 回退钩子）。名字从当前启用的定义里现取：
// 废弃工程移走后没有 Spine 植物时明确报"跳过"，不拿原生文件冒充通过。
string spineProbe = CustomPlantRegistry.Defs
    .Select(def => def.SpineName)
    .FirstOrDefault(name => !string.IsNullOrEmpty(name));
if (spineProbe == null)
{
    spineFallback = 1;
    Console.WriteLine("Spine 回退加载: 跳过（当前启用的包里没有 Spine 植物）");
}
else
{
    try
    {
        using Stream stream = Microsoft.Xna.Framework.TitleContainer.OpenStream(
            "Content/spanim/" + spineProbe + ".atlas");
        spineFallback = stream.Length > 0 ? 1 : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine("SPINE FALLBACK FAIL: " + ex.Message);
    }
    Console.WriteLine("Spine 回退加载: " + (spineFallback == 1) + " (探针 " + spineProbe + ")");
}
foreach (string failure in CustomPlantHooks.HookFailures)
{
    Console.WriteLine("HOOK FAIL(core): " + failure);
}
foreach (string failure in PGvZNewPlantMod.PlantPluginLoader.HookFailures)
{
    Console.WriteLine("HOOK FAIL(plugin): " + failure);
}
foreach (string failure in PGvZNewPlantMod.PlantPluginLoader.LoadFailures)
{
    Console.WriteLine("PLUGIN FAIL: " + failure);
}
Console.WriteLine("植物定义: " + CustomPlantRegistry.Defs.Count);
var seedIds = new HashSet<int>();
int duplicateSeeds = 0;
int lowSeeds = 0;
foreach (var def in CustomPlantRegistry.Defs)
{
    if (!seedIds.Add(def.SeedId))
    {
        duplicateSeeds++;
    }
    if (def.SeedId < 94)
    {
        lowSeeds++;
    }
    Console.WriteLine($"  seed {def.SeedId} reanim +{def.ReanimSlot} {def.Name} " +
        $"spine={def.SpineName} reanim={def.ReanimName}");
}
Console.WriteLine("自动 SeedId: 重复=" + duplicateSeeds + " 低于94=" + lowSeeds);

// 自动排位的两条规则：图鉴从模仿者的下一行起，选卡从最后一张原生卡的下一行起。
// 行号按游戏的常量现算，不写死"火红莲/第 18 行"。
int nativeSeeds = Lawn.GameConstants.NUM_ALMANAC_SEEDS;
int lastNativeSeed = nativeSeeds - 1;
if (lastNativeSeed >= 0 && (Lawn.SeedType)lastNativeSeed == Lawn.SeedType.Imitater)
{
    lastNativeSeed--;
}
int almanacStrideY = Sexy.Constants.SMALL_SEEDPACKET_HEIGHT + Sexy.Constants.Almanac_SeedSpace.Y;
int chooserStartRow = lastNativeSeed / 4 + 1;
int badAlmanac = 0;
int badChooser = 0;
if (almanacStrideY > 0)
{
    // 只有游戏里常量才初始化（离线宿主 Constants.S 仍是 0，AutoLayout 自己也会跳过）。
    int imitaterRow = (Sexy.Constants.Almanac_ImitatorPosition.Y -
        Sexy.Constants.Almanac_SeedOffset.X) / almanacStrideY + 1;
    int almanacStartRow = Math.Max(lastNativeSeed / 4 + 1, imitaterRow) + 1;
    badAlmanac = CustomPlantRegistry.Defs.Count(def =>
        def.Selectable && def.AutoAlmanac && def.AlmanacRow < almanacStartRow);
    badChooser = CustomPlantRegistry.Defs.Count(def =>
        def.Selectable && def.AutoChooser && def.ChooserRow < chooserStartRow);
    Console.WriteLine("自动排位: 模仿者行=" + imitaterRow +
        " 图鉴首行>=" + almanacStartRow + " 违规=" + badAlmanac +
        " 选卡首行>=" + chooserStartRow + " 违规=" + badChooser);
}
else
{
    Console.WriteLine("自动排位: 跳过（常量未初始化，实机日志里核）");
}

// 每个加载成功的插件包都必须至少注册一个植物定义，否则删掉注册代码也会全绿。
int emptyPacks = PGvZNewPlantMod.PlantPluginLoader.LoadedPackIds
    .Count(id => !CustomPlantRegistry.Defs.Any(def => def.PackId == id));
Console.WriteLine("空插件包: " + emptyPacks);

// plants 目录里的每个插件 DLL 都必须被发现出 pack 类（关掉某个包不会误报）。
string plantsDir = Path.Combine(AppContext.BaseDirectory, "PGvZNewPlantMod", "plants");
int pluginDlls = Directory.Exists(plantsDir)
    ? Directory.GetFiles(plantsDir, "PGvZNewPlantMod.*.dll").Length
    : -1;
Console.WriteLine("插件 DLL: " + pluginDlls + "，发现 pack: " +
    PGvZNewPlantMod.PlantPluginLoader.DiscoveredPackCount + "，注册成功: " +
    PGvZNewPlantMod.PlantPluginLoader.PackCount);

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
    {
        if (Directory.Exists(Path.Combine(dir.FullName, "src", "Plants", "_archived")))
        {
            return dir.FullName;
        }
    }
    return null;
}

// 废弃 = 工程已挪进 src\Plants\_archived，不再编译、不再部署。
// 所以两条都要成立：加载到的定义里不该有任何废弃标记，插件目录里也不该再躺着
// 归档工程的 DLL（上一轮就是被 bin 里的旧 DLL 骗过一回）。
int loadedDeprecated = CustomPlantRegistry.Defs.Count(def => def.Deprecated);
int archivedLeft = 0;
string repoRoot = FindRepoRoot();
if (repoRoot == null)
{
    Console.WriteLine("废弃标记: 已加载的废弃定义=" + loadedDeprecated + "（找不到仓库根，跳过归档残留检查）");
}
else
{
    foreach (string dir in Directory.GetDirectories(Path.Combine(repoRoot, "src", "Plants", "_archived")))
    {
        string dll = "PGvZNewPlantMod." + Path.GetFileName(dir) + ".dll";
        if (File.Exists(Path.Combine(plantsDir, dll)))
        {
            archivedLeft++;
            Console.WriteLine("  归档工程仍在插件目录: " + dll);
        }
    }
    Console.WriteLine("废弃标记: 已加载的废弃定义=" + loadedDeprecated +
        "，归档工程残留 DLL=" + archivedLeft +
        "（归档目录 " + Directory.GetDirectories(Path.Combine(repoRoot, "src", "Plants", "_archived")).Length + " 个工程）");
}

// 内嵌卡图总数由程序集资源现算，加图不用改这里。
int expectedAssetCount = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.GetName().Name?.StartsWith("PGvZNewPlantMod", StringComparison.Ordinal) == true ||
                a.GetName().Name == "PGvZPlantKit")
    .Sum(a => a.GetManifestResourceNames()
        .Count(n => (n.StartsWith("PGvZNewPlantMod.Assets.", StringComparison.Ordinal) ||
                     n.StartsWith("PGvZPlantKit.Assets.", StringComparison.Ordinal)) &&
                    n.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));

Type helpers = typeof(Sexy.TodLib.Reanimation).Assembly.GetType("Sexy.TodLib.ReanimatorXnaHelpers");
Console.WriteLine("ReanimatorXnaHelpers: " + (helpers != null));
if (helpers != null)
{
    foreach (string field in new[]
    {
        "gReanimatorDefArray", "gReanimationParamArray",
        "gReanimationParamArraySize", "gReanimatorDefCount",
    })
    {
        Console.WriteLine("  " + field + ": " + (helpers.GetField(field,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null));
    }
    Console.WriteLine("  ReanimatorEnsureDefinitionLoaded: " +
        (helpers.GetMethod("ReanimatorEnsureDefinitionLoaded",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) != null));
}
Console.WriteLine("ReanimBase: " + CustomPlantRegistry.ReanimBase +
    " (NumReanims=" + (int)Sexy.TodLib.ReanimationType.NumReanims + ")");
Console.WriteLine("Constants.S: " + Sexy.Constants.S);
Console.WriteLine("内嵌 Spine:");
int missingSpine = 0;
foreach (var def in CustomPlantRegistry.Defs)
{
    if (string.IsNullOrEmpty(def.SpineName))
    {
        continue;
    }
    bool ok = PlantKit.HasContent("Content/spanim/" + def.SpineName + ".atlas") &&
              PlantKit.HasContent("Content/spanim/" + def.SpineName + ".skel") &&
              PlantKit.HasContent("Content/spanim/" + def.SpineName + ".png");
    Console.WriteLine("  " + def.SpineName + ": " + ok);
    if (!ok)
    {
        missingSpine++;
    }
}

// 经典逐帧动画：离线只查结构与贴图登记，不建 Image（验证台没有 GraphicsDevice）。
Console.WriteLine("经典逐帧动画:");
int reanimProblems = 0;
foreach (var def in CustomPlantRegistry.Defs)
{
    if (string.IsNullOrEmpty(def.ReanimName))
    {
        continue;
    }
    CustomReanim.Stats stats = CustomReanim.Inspect(def.ReanimName);
    // 引擎 FindTrackIndex 用小写查询名比对存储的轨道名，轨道名不是全小写就查不到。
    int badNames = stats.TrackNames.Count(n => n != n.ToLowerInvariant());
    foreach (string trackName in stats.TrackNames)
    {
        if (trackName != trackName.ToLowerInvariant())
        {
            Console.WriteLine("    轨道名未规范化: " + trackName);
        }
    }
    Console.WriteLine($"  {def.ReanimName}: 轨道 {stats.Tracks} 帧 {stats.Frames} " +
        $"fps {stats.Fps} 缺图 {stats.MissingImages.Count} 轨道名 {string.Join("/", stats.TrackNames)}");
    foreach (string missingImage in stats.MissingImages.Take(5))
    {
        Console.WriteLine("    缺帧图: " + missingImage);
    }
    if (!stats.Ok || badNames > 0)
    {
        reanimProblems++;
    }
}

// 注册子弹：跨包靠 ID 字符串引用，包没启用或图没内嵌都要在这里暴露。
// 名单只列"已知存在跨包引用"的子弹，归属包没加载时自动退出断言——
// 工程挪进 _archived 不需要回来手改这份名单。
string[] expectedBulletsAll =
{
    "pgvz.ares:ares_shot",
    "pgvz.chocolate_cannon:shell",
    "pgvz.loki:ares_shot_enhanced",
};
string[] expectedBullets = expectedBulletsAll
    .Where(id => PGvZNewPlantMod.PlantPluginLoader.LoadedPackIds.Contains(
        id.Split(':')[0]))
    .ToArray();
int missingBullets = 0;
Console.WriteLine("注册子弹: " + CustomBullets.Count);
foreach (string bulletId in expectedBullets)
{
    CustomBulletDef bullet = CustomBullets.Find(bulletId);
    bool imageOk = bullet != null &&
        (string.IsNullOrEmpty(bullet.Image) || AssetStore.Has(bullet.Image));
    if (bullet != null && !imageOk)
    {
        Console.WriteLine("  " + bulletId + " 缺贴图: " + bullet.Image);
    }
    if (!imageOk)
    {
        missingBullets++;
    }
}

// 卡图按"当前启用的定义"现查：废弃工程移走后不会留下过期名单，
// 新增一株忘了内嵌卡图的植物照样会被点名。
int cardDefs = CustomPlantRegistry.Defs.Count(def => !string.IsNullOrEmpty(def.CardAsset));
int missing = CustomPlantRegistry.Defs.Count(def =>
    !string.IsNullOrEmpty(def.CardAsset) && !AssetStore.Has(def.CardAsset));
foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
{
    if (!string.IsNullOrEmpty(def.CardAsset) && !AssetStore.Has(def.CardAsset))
    {
        Console.WriteLine("  " + def.Name + " 缺卡图: " + def.CardAsset);
    }
}
Console.WriteLine("卡图齐备: " + (cardDefs - missing) + "/" + cardDefs);
// 裸立绘卡要靠框架自带的通用背景垫底：植物声明的色系必须有图，
// green/purple/gray 三种也一个都不能少（背景没内嵌进 DLL 时这里必须响）。
int backNeeded = CustomPlantRegistry.Defs.Count(def => !string.IsNullOrEmpty(def.CardBackground));
int backMissing = CustomPlantRegistry.Defs.Count(def => !string.IsNullOrEmpty(def.CardBackground) &&
    !AssetStore.Has("card_background_" + def.CardBackground));
int backVariantsMissing = new[] { "green", "purple", "gray" }
    .Count(color => !AssetStore.Has("card_background_" + color));
Console.WriteLine("通用卡槽背景: 需要 " + backNeeded + " 张垫底，植物色系缺图=" + backMissing +
    "，框架色系缺图=" + backVariantsMissing);
Console.WriteLine("注册资源总数: " + AssetStore.Count + " / 程序集内嵌 " + expectedAssetCount);
Console.WriteLine("关键资源缺失: " + missing);
bool saveEncodingPassed = SaveEncodingTests.Run();
// 定义自检的两向门（放在存档往返之后：坏定义会故意把注册表弄脏）：
// ① 当前真实定义必须一条都不报——否则这条门只是噪音，作者会当成假阳性忽略掉；
// ② 故意做坏的定义必须被逐条点名——否则"零告警"可能只是检查根本没响。
int cleanWarnings = PlantKit.ValidatePlants().Count;
Console.WriteLine("定义自检: 真定义告警=" + cleanWarnings);
foreach (string warning in PlantKit.ValidatePlants())
{
    Console.WriteLine("  真定义仍报: " + warning);
}

var broken = new CustomPlantDef
{
    Name = CustomPlantRegistry.Defs.FirstOrDefault()?.Name ?? "probe",
    Cost = 0,
    RefreshTime = 0,
    SubClass = Lawn.PlantSubClass.Shooter,
    LaunchRate = 0,
    CardAsset = "no_such_card_asset",
    CardBackground = "pink",
    NativeReanimType = Sexy.TodLib.ReanimationType.Fumeshroom,
    SpineName = "probe_spine",
    DrawAsSeed = Lawn.SeedType.Gatlingpea,
    UpgradesToName = "没有这株植物",
    IsUpgradeCard = true,
    ShootingAnimRate = 20f,
    BodyTint = "#12g456",
    ParticleTint = "zzzzzz",
    Factory = () => null,
};
PlantKit.RegisterPlant(broken);
// 子弹的两套 uint 掩码也要能被自检点名：上面那条"真定义 0 告警"只证明不报假阳性，
// 没证明这条检查会响。故意注册一发两套都越界的假弹，两句都必须出现。
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:shot",
    Damage = 40,
    DamageFlags = 0x40u,
    DamageRangeFlags = 0x100u,
});
// 毒也一样：写错的毒不报错，只是安静地一刀都不掉，所以坏数值必须被逐条点名。
CustomCombat.RegisterPoison(new CustomCombat.PoisonSpec
{
    Name = "verify.broken.poison",
    Duration = 0,
    Interval = 0,
    FixedDamage = 0,
    DamagePercent = 0,
    TakeFlags = 0x40u,
    RangeFlags = 0x100u,
});
// 子弹的其它几条声明也要能被自检点名：贴图没注册、穿透配 0 伤害、负数、
// 填了 Impact 还写声明、SkipCollision 却没人算落点 —— 五种各注册一条假弹。
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:image", Carrier = Lawn.ProjectileType.Pea,
    Damage = 40, Image = "no_such_bullet_image",
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:pierce", Damage = 0, Pierce = 1,
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:negative", Damage = 40, Pierce = -1,
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:impact", Damage = 40, SplashDamage = 20,
    Impact = new BrokenImpact(),
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:skip", Damage = 40, SkipCollision = true,
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:tint", Damage = 40, Tint = "#66cczz",
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:tintcarrier", Damage = 40, Tint = "#66ccff",
    Carrier = Lawn.ProjectileType.Fireball,
});
CustomBullets.Register(new CustomBulletDef
{
    Id = "verify.broken:burn", Damage = 40,
    Burn = new CustomBurn.BurnSpec
    {
        Name = "verify.broken.burn", Duration = 0, Interval = 0, DamagePercent = 0,
        SplashWidth = -5, SplashRowSpan = -1, Tint = false, TintColor = "#zz8833",
        TakeFlags = 64, RangeFlags = 256,
    },
});
List<string> probeWarnings = PlantKit.ValidatePlants().ToList();
string[] mustSee =
{
    "Cost=0", "RefreshTime=0", "LaunchRate=0",
    "没有在 AssetStore 里注册", "不认识，可选 green/purple/gray",
    "自定义动画会被忽略", "被两株以上植物共用",
    "在 UpdateShooting 里有自己的开火分支",
    "DamageFlags=64 有 DamageFlags 枚举之外的位",
    "DamageRangeFlags=256 超过 8 类目标的掩码",
    "毒 verify.broken.poison: 既没写 Ticks 也没写 Duration",
    "Interval=0，会变成每帧一刀",
    "没写伤害（FixedDamage 和 DamagePercent 都是 0）",
    "毒 verify.broken.poison: TakeFlags=64",
    "毒 verify.broken.poison: RangeFlags=256",
    "UpgradesToName=\"没有这株植物\" 在已注册的植物里找不到",
    "声明了 IsUpgradeCard，但没有任何植物说能被它升级",
    "设了 ShootingAnimRate=20 但没设 ShootingCounter",
    "BodyTint=\"#12g456\" 解析不出来",
    "ParticleTint=\"zzzzzz\" 解析不出来",
    "子弹 verify.broken:image: Image=\"no_such_bullet_image\" 没有在 AssetStore 里注册",
    "子弹 verify.broken:pierce: Pierce=1 但 Damage=0",
    "verify.broken:negative: Pierce=-1",
    "子弹 verify.broken:impact: 填了 Impact 又写了 Damage/Splash/Pierce/Poison",
    "子弹 verify.broken:skip: SkipCollision=true 但没有 Motion/Impact",
    "子弹 verify.broken:tint: Tint=\"#66cczz\" 解析不出来",
    "verify.broken:tintcarrier: Tint=\"#66ccff\" 配 Carrier=Fireball 画不出来",
    "灼热 verify.broken.burn: 既没写 Ticks 也没写 Duration",
    "灼热 verify.broken.burn: Interval=0",
    "灼热 verify.broken.burn: DamagePercent=0",
    "灼热 verify.broken.burn: SplashWidth=-5",
    "灼热 verify.broken.burn: TintColor=\"#zz8833\" 解析不出来",
    "灼热 verify.broken.burn: 写了 TintColor=\"#zz8833\" 但 Tint=false",
    "灼热 verify.broken.burn: TakeFlags=64",
    "灼热 verify.broken.burn: RangeFlags=256",
};
int probeMissing = mustSee.Count(needle => !probeWarnings.Any(w => w.Contains(needle)));
Console.WriteLine("定义自检: 坏定义告警=" + probeWarnings.Count +
    "，漏报=" + probeMissing + "（应至少 " + mustSee.Length + " 条）");
foreach (string needle in mustSee)
{
    if (!probeWarnings.Any(w => w.Contains(needle)))
    {
        Console.WriteLine("  坏定义漏报: " + needle);
    }
}

// 诊断快照也要真跑得动：漏段、某段抛异常、字段渲染错位，这里都得响。
string report = PlantKit.DumpReport(Path.Combine(AppContext.BaseDirectory, "diagnostics.offline.txt"));
int reportProblems = 0;
foreach (string needle in new[] { "== 环境", "== 包与加载顺序", "== 植物定义", "== 钩子",
                                  "== 素材与子弹", "== 自检告警", "== C# 植物插件", "reanim='']" })
{
    if (!report.Contains(needle))
    {
        reportProblems++;
        Console.WriteLine("  诊断快照缺: " + needle);
    }
}
if (report.Contains("! 本段采集失败"))
{
    reportProblems++;
    Console.WriteLine("  诊断快照有采集失败的段");
}
Console.WriteLine("诊断快照: " + report.Length + " 字符，问题=" + reportProblems);

// 染色解析的正向半边：只验"写错的能被抓出来"会漏掉"写对的解析错"（比如整串位移一位）。
// #66ccff 必须正好是 (102,204,255)，8 位那种要带上读的透明值。
int tintProblems = 0;
if (!CustomPlantDef.TryParseTint("#66ccff", out SexyColor tint) ||
    tint.Color.R != 102 || tint.Color.G != 204 || tint.Color.B != 255)
{
    tintProblems++;
    Console.WriteLine("  染色解析: #66ccff 没解析成 (102,204,255)");
}
if (!CustomPlantDef.TryParseTint("66ccff80", out SexyColor tintA) || tintA.Color.A != 128)
{
    tintProblems++;
    Console.WriteLine("  染色解析: 8 位带透明没解析对");
}
if (CustomPlantDef.TryParseTint("cc66f", out SexyColor _) ||
    CustomPlantDef.TryParseTint("", out SexyColor _))
{
    tintProblems++;
    Console.WriteLine("  染色解析: 短串/空串居然解析成功了");
}
Console.WriteLine("染色解析: 问题=" + tintProblems);

int burnProblems = 0;
CustomBulletDef summerBullet = CustomBullets.Find("SummerMelon:summer_melon");
CustomPlantDef summerPlant = CustomPlantRegistry.Defs.FirstOrDefault(d => d.PackId == "SummerMelon");
if (summerPlant == null || summerBullet?.Burn == null || summerBullet.Poison != null ||
    summerBullet.Carrier != Lawn.ProjectileType.Melon || summerBullet.Damage != 0 ||
    summerBullet.Burn.DamagePercent != 3 || !summerBullet.Burn.RoundDown ||
    summerBullet.Burn.Interval != 100 || summerPlant.NativeReanimType != Sexy.TodLib.ReanimationType.Melonpult)
{
    burnProblems++;
    Console.WriteLine("  SummerMelon 定义缺失，或未使用独立灼热/原生西瓜溅射");
}
else
{
    // 只验纯数值函数；不构造依赖游戏实例的 Zombie，也不声称这能代替命中实测。
    var dummy = (Lawn.Zombie)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(Lawn.Zombie));
    MethodInfo damageMethod = typeof(CustomBurn).GetMethod("BurnDamage", BindingFlags.Static | BindingFlags.NonPublic);
    int[][] maxima = { new[] { 271, 113, 137, 41 }, new[] { 1, 0, 0, 0 },
        new[] { 1000000000, 1000000000, 1000000000, 1000000000 } };
    foreach (int[] values in maxima)
    {
        dummy.mBodyMaxHealth = values[0];
        dummy.mHelmMaxHealth = values[1];
        dummy.mShieldMaxHealth = values[2];
        dummy.mFlyingMaxHealth = values[3];
        long total = values.Sum(value => (long)value);
        int expected = (int)(total * 3 / 100);
        int actual = (int)damageMethod.Invoke(null, new object[] { dummy, summerBullet.Burn });
        if (actual != expected) burnProblems++;
        Console.WriteLine($"  灼热算式: max={total} expected={expected} actual={actual}");
        if (total * 3 % 100 != 0)
        {
            try
            {
                summerBullet.Burn.RoundDown = false;
                int roundedUp = (int)damageMethod.Invoke(null, new object[] { dummy, summerBullet.Burn });
                if (roundedUp == expected) burnProblems++;
                Console.WriteLine($"  取整反向: floor={expected} ceil={roundedUp}");
            }
            finally
            {
                summerBullet.Burn.RoundDown = true;
            }
        }
    }
}
Console.WriteLine("夏瓜独立灼热: 问题=" + burnProblems);

// 弹体换图/染色靠"按弹种顶掉 AtlasResources 那张静态图"，字段名写错或哪天被引擎改名，
// 换图就会静默不生效（弹体照常画，谁都不报错）。这里逐个把表里的弹种问一遍。
int carrierProblems = 0;
List<Lawn.ProjectileType> carriers = CustomProjectiles.TintableCarriers.ToList();
foreach (Lawn.ProjectileType carrier in carriers)
{
    if (CustomProjectiles.CarrierImageField(carrier) == null)
    {
        carrierProblems++;
        Console.WriteLine("  换图表: " + carrier + " 的 AtlasResources 字段找不到");
    }
}
// 反向半边：火球那类走 reanim 的弹种必须仍然判"换不了图"，否则自检那条告警是假的。
if (CustomProjectiles.CarrierImageField(Lawn.ProjectileType.Fireball) != null)
{
    carrierProblems++;
    Console.WriteLine("  换图表: Fireball 不该有静态弹体图");
}
Console.WriteLine("弹体换图表: " + carriers.Count + " 个弹种，问题=" + carrierProblems);

// 借身份的钩子必须真的装上了。IsNocturnal 是纯静态方法，离线就能直接问它：
// 声明了 Nocturnal 的那株要回真，没声明的自定义种子仍要回假（否则等于钩子无脑放行）。
CustomPlantDef nocturnalDef = CustomPlantRegistry.Defs.FirstOrDefault(d => d.Nocturnal);
CustomPlantDef plainDef = CustomPlantRegistry.Defs.FirstOrDefault(
    d => d != nocturnalDef && d.SeedId > 0 && !d.Nocturnal);
bool nocturnalOk = nocturnalDef != null && plainDef != null &&
    Lawn.Plant.IsNocturnal(nocturnalDef.Seed) && !Lawn.Plant.IsNocturnal(plainDef.Seed);
Console.WriteLine("借身份钩子: " + (nocturnalDef?.Name ?? "没有声明 Nocturnal 的定义") +
    " IsNocturnal=" + nocturnalOk + "（对照 " + plainDef?.Name + "）");

return saveEncodingPassed && CustomPlantHooks.HookFailures.Count == 0 &&
       reportProblems == 0 && nocturnalOk &&
       PGvZNewPlantMod.PlantPluginLoader.LoadFailures.Count == 0 &&
       PGvZNewPlantMod.PlantPluginLoader.HookFailures.Count == 0 &&
       PGvZNewPlantMod.PlantPluginLoader.DiscoveredPackCount == pluginDlls &&
       emptyPacks == 0 &&
       loadedDeprecated == 0 && archivedLeft == 0 &&
       badAlmanac == 0 && badChooser == 0 &&
       backMissing == 0 && backVariantsMissing == 0 &&
       AssetStore.Count == expectedAssetCount &&
       spineFallback == 1 &&
       reanimProblems == 0 &&
       missingBullets == 0 &&
       missingSpine == 0 && duplicateSeeds == 0 && lowSeeds == 0 &&
       cleanWarnings == 0 && probeMissing == 0 &&
       tintProblems == 0 && carrierProblems == 0 && burnProblems == 0 &&
       probeWarnings.Count >= mustSee.Length &&
       missing == 0 ? 0 : 2;

// 自检里"填了 Impact 还写声明"那条要有一个真的 Impact 处理器才能触发。
sealed class BrokenImpact : ICustomProjectileImpact
{
    public bool OnImpact(Lawn.Projectile projectile, Lawn.Zombie zombie,
        Action<Lawn.Projectile, Lawn.Zombie> orig) => true;
}
