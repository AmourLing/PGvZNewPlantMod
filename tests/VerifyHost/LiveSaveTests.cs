using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Lawn;
using MonoMod.RuntimeDetour;
using MonoMod.RuntimeDetour.HookGen;
using PGvZPlantKit;
using Sexy;
using Sexy.TodLib;

public static class LiveSaveTests
{
    private static Hook updateHook;
    private static string pending;
    private static string result = "not started";
    private static readonly string SavePath = Path.Combine(Environment.CurrentDirectory, "roundtrip.dat");
    private static readonly string SnapshotPath = Path.Combine(Environment.CurrentDirectory, "roundtrip.expected.json");
    private static readonly string StatePath = Path.Combine(Environment.CurrentDirectory, "state.baseline.json");
    private static readonly string ReloadedPath = Path.Combine(Environment.CurrentDirectory, "state.reloaded.json");
    private static readonly string SlotLoadedPath = Path.Combine(Environment.CurrentDirectory, "state.slot-reloaded.json");

    // ---------------------------------------------------------------- 行为套件
    // 一株不落地种、一只靶子僵尸、跑固定帧数，统计每株各自开了几发、还在不在。
    private sealed class BehaveRun
    {
        public int Frames;
        public int FramesLeft;
        public int SunStart;
        public int ZombieHpStart;
        public int ZombiesStart;
        public int MaxProjectiles;
        public readonly List<CustomPlantDef> Defs = new List<CustomPlantDef>();
        public readonly List<Plant> Plants = new List<Plant>();
    }

    private static BehaveRun behave;
    // 某些 op 自己写最终结果（packetcmp / behave / plantreal），别让 PASS 盖掉它
    private static bool reportWritten;
    // 真实种卡路径的单株观察：MouseUpWithPlant 种下去后盯 N 帧
    private static Plant watchPlant;
    private static int watchFramesLeft;
    private static string watchName;
    private static int watchCol;
    private static int watchRow;
    private static Hook fireHook;
    private static Hook cobHook;
    private static readonly Dictionary<Plant, int> FireShots = new Dictionary<Plant, int>();
    private static readonly Dictionary<Plant, int> CobShots = new Dictionary<Plant, int>();

    public static string Result => Volatile.Read(ref result);

    public static void Queue(string operation)
    {
        string current = Path.GetFullPath(Environment.CurrentDirectory).Replace('\\', '/');
        if (!current.Contains("/tests/save-sandbox/", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live tests are restricted to the isolated sandbox");
        if (behave != null)
            throw new InvalidOperationException("A behaviour suite is still running");
        if (watchPlant != null)
            throw new InvalidOperationException("A plant watch is still running");
        if (poisonRun != null)
            throw new InvalidOperationException("A poison run is still running");
        if (summerRun != null)
            throw new InvalidOperationException("A summerlive run is still running");
        if (SourceCases.Count > 0)
            throw new InvalidOperationException("A poison source run is still running");
        if (IceCases.Count > 0)
            throw new InvalidOperationException("An ice check is still running");
        updateHook ??= new Hook(typeof(LawnApp).GetMethod("UpdateFrames"),
            (Action<Action<LawnApp>, LawnApp>)Update);
        if (Interlocked.CompareExchange(ref pending, operation, null) != null)
            throw new InvalidOperationException("A game-thread operation is still pending");
        Volatile.Write(ref result, "pending " + operation);
    }

    private static void Update(Action<LawnApp> original, LawnApp app)
    {
        SummerRun sample = summerRun;
        if (sample != null)
        {
            try
            {
                // 沙盒窗口在后台，游戏会自己把棋盘暂停（丢焦点就弹暂停框那种）。
                // 采样要的是"真实帧推进"，所以每帧先把这个暂停按掉再走 orig；
                // 这是台架对后台窗口的让步，不是产品行为。
                if (sample.Board != null && sample.Board.mPaused)
                {
                    sample.Board.mPaused = false;
                }
                CheckSummerLive(app, sample);
                // 必须在 orig 前钉住；只改坐标，不手调 Board/Projectile，也不催开火。
                foreach (Zombie zombie in sample.Zombies)
                {
                    PinZombie(zombie, sample.Board.GridToPixelX(6, zombie.mRow));
                    zombie.cachedZombieRectUpToDate = false;
                }
                original(app);
                TickSummerLive(app, sample);
            }
            catch (Exception error)
            {
                FinishSummerLive(app, sample, error.ToString());
            }
            return;
        }
        original(app);
        string operation = Interlocked.Exchange(ref pending, null);
        if (operation != null)
        {
            reportWritten = false;
            try
            {
                Execute(app, operation);
                // 有界采样自己落最终结果，不能先写 PASS。
                if (behave == null && watchPlant == null && summerRun == null && !reportWritten)
                    Volatile.Write(ref result, "PASS " + operation + " thread=" + Environment.CurrentManagedThreadId);
            }
            catch (Exception error)
            {
                Volatile.Write(ref result, "FAIL " + operation + " " + error);
            }
        }
        TickBehave(app);
        TickWatch(app);
        TickPoisonRun(app);
        TickPoisonSource(app);
        TickIceCheck(app);
    }

    private static void TickWatch(LawnApp app)
    {
        Plant plant = watchPlant;
        Board board = app == null ? null : app.mBoard;
        if (plant == null || board == null)
        {
            return;
        }
        if (--watchFramesLeft > 0)
        {
            return;
        }
        watchPlant = null;
        bool listed = board.mPlants.Contains(plant);
        Volatile.Write(ref result, string.Format(
            "REALPLANT {0} at=({1},{2}) listed={3} dead={4} squished={5} hp={6} " +
            "fire={7} anim={8:0.00}->{9:0.00} zombieHpDrop={10}",
            watchName, watchCol, watchRow, listed, plant.mDead, plant.mSquished,
            plant.mPlantHealth,
            FireShots.TryGetValue(plant, out int fire) ? fire : 0,
            watchAnimStart, BodyAnimTime(plant),
            watchZombie == null ? -1
                : watchZombieHpStart - (watchZombie.mBodyHealth + watchZombie.mHelmHealth +
                                        watchZombie.mShieldHealth)));
    }

    private static void TickBehave(LawnApp app)
    {
        BehaveRun run = behave;
        Board board = app == null ? null : app.mBoard;
        if (run == null || board == null)
        {
            return;
        }
        if (board.mProjectiles.Count > run.MaxProjectiles)
        {
            run.MaxProjectiles = board.mProjectiles.Count;
        }
        if (--run.FramesLeft > 0)
        {
            return;
        }
        behave = null;
        Volatile.Write(ref result, BuildBehaveReport(board, run));
    }

    private static void Execute(LawnApp app, string operation)
    {
        if (operation == "summerlive")
        {
            // 本入口连前置校验失败也要冻结棋盘并输出完整采样读数。
            RunSummerLive(app);
            return;
        }
        if (NeedsLiveBoard(operation))
        {
            RequireLiveBoard(app, operation);
        }
        if (operation.StartsWith("plantreal", StringComparison.Ordinal) ||
            operation.StartsWith("plantadd", StringComparison.Ordinal))
        {
            RunPlantReal(app, operation);
            return;
        }
        if (operation.StartsWith("hitcheck", StringComparison.Ordinal))
        {
            RunHitCheck(app, operation);
            return;
        }
        if (operation.StartsWith("flagcheck", StringComparison.Ordinal))
        {
            RunFlagCheck(app, operation);
            return;
        }
        if (operation.StartsWith("poison ", StringComparison.Ordinal))
        {
            RunPoison(app, operation);
            return;
        }
        if (operation.StartsWith("scorch ", StringComparison.Ordinal))
        {
            RunScorch(app, operation);
            return;
        }
        if (operation.StartsWith("poisrc ", StringComparison.Ordinal))
        {
            RunPoisonSource(app, operation);
            return;
        }
        if (operation.StartsWith("icecheck ", StringComparison.Ordinal))
        {
            RunIceCheck(app, operation);
            return;
        }
        if (operation.StartsWith("zombie ", StringComparison.Ordinal))
        {
            RunSpawnZombie(app, operation);
            return;
        }
        if (operation.StartsWith("cheatgray ", StringComparison.Ordinal))
        {
            RunCheatGray(app, operation);
            return;
        }
        if (operation.StartsWith("upgradegate ", StringComparison.Ordinal))
        {
            RunUpgradeGate(app, operation);
            return;
        }
        if (operation == "packetcmp")
        {
            SeedBank bank = app.mBoard.mSeedBank;
            CursorObject cursor = app.mBoard.mCursorObject;
            var lines = new List<string>();
            for (int i = 0; i < bank.mNumPackets; i++)
            {
                SeedPacket pk = bank.mSeedPackets[i];
                int nativeGrayness = 255;
                float nativeDark = 0f;
                pk.GetGraynessAndDarkness(ref nativeGrayness, ref nativeDark);
                bool held = cursor.mCursorType == CursorType.PlantFromBank &&
                            cursor.mSeedBankIndex == pk.mIndex;
                if (held)
                {
                    // 原生 DrawBackground/DrawOverlay 对被拿起的那张直接跳过变暗计算
                    nativeGrayness = 255;
                    nativeDark = 0f;
                }
                lines.Add($"{pk.mPacketType}#{pk.mIndex} act={pk.mActive} ref={pk.mRefreshing}" +
                    $" ct={pk.mRefreshCounter}/{pk.mRefreshTime} held={(held ? 1 : 0)}" +
                    $" native={nativeGrayness}/{nativeDark:0.00} ours={CustomUi.PacketGrayness(pk)}");
            }
            Volatile.Write(ref result, "PACKETCMP " + string.Join(" | ", lines));
            reportWritten = true;
            return;
        }
        if (operation.StartsWith("cursor ", StringComparison.Ordinal))
        {
            RunCursorPreview(app, operation);
            return;
        }
        if (operation.StartsWith("shot ", StringComparison.Ordinal))
        {
            PlantKit.RequestScreenshot(operation.Substring("shot ".Length).Trim());
            return;
        }
        if (operation == "play")
        {
            // 沙盒里有时卡在 LevelIntro，直接把选卡关掉并推进到 Playing，
            // 只为让光标预览这类"只在游玩中显示"的东西能画出来。
            SeedChooserScreen chooser = app.mSeedChooserScreen;
            if (chooser != null)
            {
                chooser.OnStartButton();
                // OnStartButton 只把选卡"结束"掉，界面本身还挂在 app 上继续画，
                // 不藏掉的话截图正中间就是一块选卡面板， lawn 全被盖住。
                chooser.mVisible = false;
            }
            Board board = app.mBoard;
            if (board != null && board.mCutScene != null)
            {
                board.mCutScene.EndSeedChooser();
            }
            if (app.mGameScene != GameScenes.Playing)
            {
                app.mGameScene = GameScenes.Playing;
            }
            return;
        }
        if (operation.StartsWith("behave", StringComparison.Ordinal))
        {
            RunBehave(app, operation);
            return;
        }
        if (operation == "prepare")
        {
            DismissTitle(app);
            app.PreNewGame(GameMode.ChallengeLastStand, false);
            return;
        }
        if (operation == "choose")
        {
            SeedChooserScreen chooser = app.mSeedChooserScreen;
            Require(chooser != null, "missing chooser");
            // 坚不可摧禁向日葵；睡莲同时消掉水池警告。测试不弹警告框。
            for (int i = 0; i < chooser.mPickWarningsWaved.Length; i++)
            {
                chooser.mPickWarningsWaved[i] = true;
            }
            chooser.SeedSelected(SeedType.Lilypad);
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs.Where(d => d.Selectable).Take(9))
                chooser.SeedSelected(def.Seed);
            return;
        }
        if (operation == "start")
        {
            SeedChooserScreen chooser = app.mSeedChooserScreen;
            Require(chooser != null && chooser.mSeedsInBank == chooser.mBoard.mSeedBank.mNumPackets, "fill bank before starting");
            chooser.OnStartButton();
            return;
        }
        if (operation == "seed-save")
        {
            Board board = app.mBoard;
            Require(app.mGameScene == GameScenes.Playing, "not playing yet");
            board.Pause(true);
            CustomPlantDef[] defs = CustomPlantRegistry.Defs.Where(d => d.Selectable).ToArray();
            for (int i = 0; i < defs.Length; i++)
            {
                Plant plant = board.AddPlant(i / 5, i % 5, defs[i].Seed, SeedType.None);
                plant.mPlantHealth = Math.Min(plant.mPlantHealth, 173 + i);
                plant.mStateCountdown = 431 + i;
                if (defs[i].PackId == "pgvz.hua_huo_long")
                {
                    SetField(CustomPlantRegistry.Get(plant), "_tick", 121);
                    SetField(CustomPlantRegistry.Get(plant), "_burst", 2);
                    plant.PlayBodyReanim("anim_burst", ReanimLoopType.PlayOnceAndHold, 0, 10);
                }
                if (defs[i].PackId == "pgvz.ares")
                {
                    SetField(CustomPlantRegistry.Get(plant), "_salvo", 4);
                    SetField(CustomPlantRegistry.Get(plant), "_animRate", 17.5f);
                }
            }
            Plant native = board.AddPlant(8, 4, SeedType.Wallnut, SeedType.None);
            native.mPlantHealth = 1337;
            Zombie zombie = board.AddZombieInRow(ZombieType.Normal, 0, 0);
            zombie.mTargetPlantID = board.mPlants[0];
            board.mPlants[0].mTargetZombieID = zombie;
            SeedBank bank = board.mSeedBank;
            bank.mSeedPackets[3].mRefreshing = true;
            bank.mSeedPackets[3].mActive = false;
            bank.mSeedPackets[3].mRefreshCounter = 123;
            bank.mSeedPackets[3].mRefreshTime = 750;
            CustomPlantBase ares = board.mPlants.Select(CustomPlantRegistry.Get).First(b => b?.Definition.PackId == "pgvz.ares");
            CustomBullets.Fire(board, ares.Plant, "pgvz.ares:ares_shot", 300, 200, 2, ProjectileMotion.Backwards);
            Snapshot before = Capture(board);
            object[] plants = board.mPlants.Cast<object>().ToArray();
            object[] bodies = board.mPlants.Select(p => (object)p.mBodyReanimID).ToArray();
            board.SaveGame(SavePath);
            Require(File.Exists(SavePath), "save was not created");
            Require(JsonSerializer.Serialize(before) == JsonSerializer.Serialize(Capture(board)), "save changed live state");
            Require(plants.SequenceEqual(board.mPlants.Cast<object>()), "save replaced plant objects");
            Require(bodies.SequenceEqual(board.mPlants.Select(p => (object)p.mBodyReanimID)), "save replaced body objects");
            File.WriteAllText(SnapshotPath, JsonSerializer.Serialize(before));
            return;
        }
        if (operation == "load")
        {
            DismissTitle(app);
            if (app.mBoard != null) app.KillBoard();
            app.SetGameMode(GameMode.ChallengeLastStand);
            app.MakeNewBoard();
            Require(app.mBoard.LoadGame(SavePath), "LoadGame returned false");
            app.mBoard.Pause(true);
            string expected = File.ReadAllText(SnapshotPath);
            string actual = JsonSerializer.Serialize(Capture(app.mBoard));
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "roundtrip.actual.json"), actual);
            Require(expected == actual, "loaded snapshot differs from expected");
            Plant plant = app.mBoard.mPlants[0];
            Require(ReferenceEquals(plant.mTargetZombieID?.mTargetPlantID, plant), "plant/zombie reference was not restored");
            Require(app.mBoard.mPlants.Count > 10, "empty test data");
            return;
        }
        if (operation == "resave")
        {
            Board board = app.mBoard;
            board.SaveGame(SavePath);
            string actual = JsonSerializer.Serialize(Capture(board));
            Require(File.ReadAllText(SnapshotPath) == actual, "resave changed loaded state");
            return;
        }
        if (operation == "save-slot")
        {
            app.mBoard.SaveGame(SlotPath(app));
            return;
        }
        if (operation == "load-slot")
        {
            DismissTitle(app);
            app.SetGameMode(GameMode.ChallengeLastStand);
            string slot = SlotPath(app);
            Require(File.Exists(slot), "slot save missing: " + slot);
            // 走游戏自己的"继续存档"入口，不自己拼 MakeNewBoard + LoadGame。
            Require(app.TryLoadGame(), "TryLoadGame returned false");
            app.KillDialog(37);
            app.mBoard.Pause(true);
            string expected = File.ReadAllText(SnapshotPath);
            string actual = JsonSerializer.Serialize(Capture(app.mBoard));
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "roundtrip.slot.json"), actual);
            Require(expected == actual, "slot-loaded snapshot differs from expected");
            return;
        }
        if (operation == "boom-fire")
        {
            // 弹体停在引爆高度：下一帧进 Detonate，之后约 1 秒都在爆炸动画里，
            // 这段时间存读档正好能测到"二次引爆"。
            Board board = app.mBoard;
            Plant cannon = board.mPlants.FirstOrDefault(p =>
                CustomPlantRegistry.Get(p)?.Definition.PackId == "pgvz.chocolate_cannon");
            Require(cannon != null, "no chocolate cannon on board");
            board.AddZombieInRow(ZombieType.Normal, cannon.mRow, 3);
            Projectile shell = CustomBullets.Fire(board, cannon,
                "pgvz.chocolate_cannon:shell",
                board.GridToPixelX(cannon.mPlantCol, cannon.mRow),
                board.GridToPixelY(cannon.mPlantCol, cannon.mRow), cannon.mRow);
            shell.mVelZ = 0f;
            shell.mPosZ = 1f;
            return;
        }
        if (operation == "boom-report")
        {
            Board board = app.mBoard;
            var report = board.mProjectiles
                .Where(p => CustomBullets.Of(p)?.Id == "pgvz.chocolate_cannon:shell")
                .Select(p => CustomProjectiles.TagFor(p).BoomFrame.ToString("0.00"))
                .ToList();
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "boom.report.json"),
                JsonSerializer.Serialize(new
                {
                    shells = report,
                    zombies = board.mZombies.Select(z => z.mZombieType + "|" + z.mBodyHealth).ToList(),
                }));
            return;
        }
        if (operation == "grow")
        {
            app.mBoard.Pause(false);
            return;
        }
        if (operation == "settle")
        {
            // 让植物先真跑一段（瓷砖扩散、小守卫菇、恐龙、在飞的子弹），再取整份状态存盘。
            Board board = app.mBoard;
            board.Pause(true);
            string state = StateJson(board);
            File.WriteAllText(StatePath, state);
            board.SaveGame(SavePath);
            Require(StateJson(board) == state, "save changed custom state");
            Require(File.Exists(SavePath), "save was not created");
            return;
        }
        if (operation == "reload")
        {
            DismissTitle(app);
            if (app.mBoard != null) app.KillBoard();
            app.SetGameMode(GameMode.ChallengeLastStand);
            app.MakeNewBoard();
            Require(app.mBoard.LoadGame(SavePath), "LoadGame returned false");
            app.mBoard.Pause(true);
            File.WriteAllText(ReloadedPath, StateJson(app.mBoard));
            return;
        }
        if (operation == "slot-load")
        {
            DismissTitle(app);
            app.SetGameMode(GameMode.ChallengeLastStand);
            Require(app.TryLoadGame(), "TryLoadGame returned false");
            app.KillDialog(37);
            app.mBoard.Pause(true);
            File.WriteAllText(SlotLoadedPath, StateJson(app.mBoard));
            return;
        }
        if (operation == "exit")
        {
            app.AppExit();
            return;
        }
        if (operation == "quit")
        {
            Environment.Exit(0);
            return;
        }
        throw new ArgumentException("Unknown live test operation");
    }

    public sealed class Snapshot
    {
        public int Mode { get; set; }
        public int Counter { get; set; }
        public int Sun { get; set; }
        public List<string> Packets { get; set; } = new();
        public List<string> Plants { get; set; } = new();
        public List<string> Bullets { get; set; } = new();
        public List<string> Zombies { get; set; } = new();
    }

    /// <summary>
    /// 整份模组状态摘要：kit 存档实际捕获的字段（含 Python 侧字典）+ 各插件静态集合的条目数。
    /// 静态集合那部分是"存了但没恢复"的检测点，所以单独列出来。
    /// </summary>
    private static string StateJson(Board board)
    {
        MethodInfo capture = typeof(CustomSave).GetMethod("Capture",
            BindingFlags.NonPublic | BindingFlags.Static);
        object state = capture.Invoke(null, new object[] { board });
        return JsonSerializer.Serialize(new
        {
            captured = state,
            statics = StaticCensus(),
        });
    }

    private static SortedDictionary<string, int> StaticCensus()
    {
        var counts = new SortedDictionary<string, int>();
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string name = assembly.GetName().Name;
            if (name == null || !name.StartsWith("PGvZNewPlantMod.", StringComparison.Ordinal))
            {
                continue;
            }
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException error)
            {
                types = error.Types.Where(t => t != null).ToArray();
            }
            foreach (Type type in types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsLiteral || !typeof(ICollection).IsAssignableFrom(field.FieldType))
                    {
                        continue;
                    }
                    if (field.GetValue(null) is ICollection collection)
                    {
                        counts[type.Name + "." + field.Name] = collection.Count;
                    }
                }
            }
        }
        return counts;
    }

    private static Snapshot Capture(Board board)
    {
        var snapshot = new Snapshot { Mode = (int)board.mApp.mGameMode, Counter = board.mMainCounter, Sun = board.mSunMoney };
        for (int i = 0; i < board.mSeedBank.mNumPackets; i++)
        {
            SeedPacket p = board.mSeedBank.mSeedPackets[i];
            snapshot.Packets.Add($"{i}|{Key(p.mPacketType)}|{Key(p.mImitaterType)}|{p.mActive}|{p.mRefreshing}|{p.mRefreshCounter}|{p.mRefreshTime}");
        }
        foreach (Plant p in board.mPlants)
        {
            CustomPlantBase behavior = CustomPlantRegistry.Get(p);
            Reanimation body = board.mApp.ReanimationTryToGet(p.mBodyReanimID);
            string extra = "";
            if (behavior?.Definition.PackId == "pgvz.hua_huo_long")
                extra = GetField(behavior, "_tick") + ":" + GetField(behavior, "_burst");
            if (behavior?.Definition.PackId == "pgvz.ares")
                extra = GetField(behavior, "_salvo") + ":" + GetField(behavior, "_animRate");
            snapshot.Plants.Add($"{Key(p.mSeedType)}|{p.mPlantCol}|{p.mRow}|{p.mPlantHealth}|{p.mPlantMaxHealth}|{p.mState}|{p.mStateCountdown}|{p.mLaunchCounter}|{body?.mAnimTime}|{body?.mFrameStart}|{body?.mFrameCount}|{body?.mDefinition.mIsSpine}|{behavior?.Definition.PackId}|{extra}");
        }
        foreach (Projectile p in board.mProjectiles)
            snapshot.Bullets.Add($"{CustomBullets.Of(p)?.Id}|{p.mMotionType}|{p.mPosX}|{p.mPosY}|{Key(p.mFromPlant)}");
        foreach (Zombie z in board.mZombies)
            snapshot.Zombies.Add($"{z.mZombieType}|{z.mRow}|{z.mBodyHealth}|{z.mBodyMaxHealth}|{z.mHelmHealth}");
        return snapshot;
    }

    /// <summary>
    /// 摘掉"点击开始"标题页和冒险地图。autopilot.py 的教训：
    /// 不先走这一步就硬塞棋盘，局会在标题页底下偷偷跑完，屏幕上什么都看不见。
    /// </summary>
    private static void DismissTitle(LawnApp app)
    {
        if (app.mTitleScreen != null)
        {
            Require(app.mTitleScreen.mLoadingThreadComplete, "title screen still loading");
            app.LoadingCompleted();
        }
        app.KillGameSelector();
    }

    private static string SlotPath(LawnApp app)
    {
        return Path.Combine(app.applicationStoragePath, "docs", "userdata",
            "game" + app.mPlayerInfo.mId + "_" + (int)app.mGameMode + ".dat");
    }

    private static string Key(SeedType seed) => CustomPlantRegistry.GetDef(seed)?.PackId + ":" + (CustomPlantRegistry.GetDef(seed)?.Name ?? seed.ToString());
    // ---------------------------------------------------------------- 行为套件实现

    private static void RunBehave(LawnApp app, string operation)
    {
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Require(board.mPlants.Count == 0, "board is not empty: " + board.mPlants.Count + " plants");
        Require(board.mZombies.Count == 0, "board already has " + board.mZombies.Count + " zombies");

        int frames = 600;
        string arg = operation.Substring("behave".Length).Trim();
        if (arg.Length > 0)
        {
            frames = int.Parse(arg);
        }
        InstallCombatCounters();
        FireShots.Clear();
        CobShots.Clear();

        BehaveRun run = new BehaveRun
        {
            Frames = frames,
            FramesLeft = frames,
            SunStart = board.mSunMoney,
        };
        var rows = new List<int>();
        for (int r = 0; r < Sexy.Constants.MAX_GRIDSIZEY; r++)
        {
            if (board.CanPlantAt(0, r, SeedType.Peashooter) == PlantingReason.Ok)
            {
                rows.Add(r);
            }
        }
        Require(rows.Count > 0, "没有可种的陆地行（全被水/屋顶挡了）");
        foreach (CustomPlantDef def in CustomPlantRegistry.Defs.Where(d => d.Selectable))
        {
            // AddPlant 是 (列, 行)；行只取当前关卡的可种陆地行，别把植物丢进水池。
            int index = run.Plants.Count;
            int col = index / rows.Count;
            int row = rows[index % rows.Count];
            Plant plant = board.AddPlant(col, row, def.Seed, SeedType.None);
            Require(plant != null, "AddPlant failed for " + def.Name);
            Require(plant.mPlantCol == col && plant.mRow == row,
                def.Name + " 落点不对：期望 (" + col + "," + row + ")，实际 (" +
                plant.mPlantCol + "," + plant.mRow + ")");
            run.Defs.Add(def);
            run.Plants.Add(plant);
        }
        foreach (int row in rows)
        {
            board.AddZombieInRow(ZombieType.Gargantuar, row, 0);
        }
        run.ZombieHpStart = TotalZombieHp(board);
        run.ZombiesStart = board.mZombies.Count;
        behave = run;
    }

    /// <summary>
    /// 真实种卡路径：卡进卡槽 → 光标拿起 → board.MouseUpWithPlant 落到指定格，
    /// 再盯 frames 帧看这株还在不在。用法：plantreal &lt;seedId&gt; &lt;col&gt; &lt;row&gt; &lt;frames&gt;
    /// </summary>
    private static void RunPlantReal(LawnApp app, string operation)
    {
        bool real = operation.StartsWith("plantreal", StringComparison.Ordinal);
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 5, "usage: plant<real|add> <seedId> <col> <row> <frames> [zombie]");
        SeedType seed = (SeedType)int.Parse(parts[1]);
        int col = int.Parse(parts[2]);
        int row = int.Parse(parts[3]);
        int frames = int.Parse(parts[4]);
        bool spawnZombie = parts.Length > 5 && parts[5] == "zombie";

        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Require(board.CanPlantAt(col, row, seed) == PlantingReason.Ok,
            "cell (" + col + "," + row + ") 不可种：" + board.CanPlantAt(col, row, seed));

        if (!real)
        {
            Plant added = board.AddPlant(col, row, seed, SeedType.None);
            Require(added != null, "AddPlant failed");
            if (spawnZombie)
            {
                SpawnWatchZombie(board, row);
            }
            ArmWatch(added, seed, frames);
            return;
        }

        Plant planted = PlantThroughCursor(board, seed, col, row);
        if (spawnZombie)
        {
            SpawnWatchZombie(board, row);
        }
        ArmWatch(planted, seed, frames);
    }

    /// <summary>
    /// 玩家落种的正路：卡进卡槽 → 光标拿起 → Board.MouseUpWithPlant 放到指定格。
    /// 用"种前后对象差集"认新植物，不受落点换算和模仿者那一帧位置的影响。
    /// </summary>
    private static Plant PlantThroughCursor(Board board, SeedType seed, int col, int row)
    {
        SeedBank bank = board.mSeedBank;
        int index = -1;
        for (int i = 0; i < bank.mNumPackets; i++)
        {
            if (bank.mSeedPackets[i].mPacketType == seed)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            index = 0;
            bank.mSeedPackets[index].SetPacketType(seed, SeedType.None);
            bank.mSeedPackets[index].mActive = true;
            bank.mSeedPackets[index].mRefreshing = false;
            bank.mSeedPackets[index].mRefreshCounter = 0;
        }

        CursorObject cursor = board.mCursorObject;
        cursor.mType = seed;
        cursor.mImitaterType = SeedType.None;
        cursor.mCursorType = CursorType.PlantFromBank;
        cursor.mSeedBankIndex = index;

        int x = board.GridToPixelX(col, row) + 20;
        int y = board.GridToPixelY(col, row) + 20;
        var before = new HashSet<Plant>(board.mPlants);
        int sunBefore = board.mSunMoney;
        board.MouseUpWithPlant(x, y, 1);

        Plant planted = null;
        foreach (Plant candidate in board.mPlants)
        {
            if (!before.Contains(candidate))
            {
                planted = candidate;
            }
        }
        if (planted == null)
        {
            var cells = string.Join(" ", board.mPlants.Select(p =>
                p.mSeedType + "@" + p.mPlantCol + "," + p.mRow));
            throw new InvalidOperationException(
                "MouseUpWithPlant 没种出植物（落点 " + x + "," + y + "）canPlant=" +
                board.CanPlantAt(col, row, seed) + " sunBefore=" + sunBefore +
                " sunNow=" + board.mSunMoney + " cursor=" + board.mCursorObject.mCursorType +
                " plants=[" + cells + "]");
        }
        Require(planted.mSeedType == seed,
            "种下的不是目标种子：" + planted.mSeedType + " != " + seed);
        return planted;
    }

    /// <summary>
    /// 单发结算探针：种下 → 摆一只就在射程里的僵尸 → 走钩子调一次 Fire 和一次
    /// FindTargetAndFire → 报"这一发掉多少血"和"开火后定格计数是多少"。
    /// 全在同一次游戏线程执行里做完，不靠外部采样，避免把竞态读成产品行为。
    /// 用法：hitcheck &lt;seedId&gt; &lt;col&gt; &lt;row&gt;
    /// </summary>
    private static void RunHitCheck(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 4, "usage: hitcheck <seedId> <col> <row>");
        SeedType seed = (SeedType)int.Parse(parts[1]);
        int col = int.Parse(parts[2]);
        int row = int.Parse(parts[3]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        // 先问落点：像"卡片模仿者"这种必须种在别的植物上的卡，硬塞进空格会让游戏自己的
        // 后续更新出状况（实测能把台架的排队卡死在这里），所以非法格子直接报失败跳过。
        PlantingReason reason = board.CanPlantAt(col, row, seed);
        Require(reason == PlantingReason.Ok,
            "cell (" + col + "," + row + ") 不可种：" + reason);
        board.mSunMoney = 9000;

        Plant plant = PlantThroughCursor(board, seed, col, row);
        Zombie zombie = board.AddZombieInRow(ZombieType.Normal, row, 0);
        Require(zombie != null, "AddZombieInRow failed");
        zombie.mX = plant.mX + 100;

        TRect rect = plant.GetPlantAttackRect(PlantWeapon.Primary);
        int overlap = GameConstants.GetRectOverlap(rect, zombie.GetZombieRect());
        // 只对 Shooter 类做单发结算：对非射手（创造神、卡片模仿者这类）硬调 Plant.Fire
        // 会撞上引擎的 Debug.ASSERT，弹出模态断言框把游戏线程整个挂住（实测台架就是这么卡死的）。
        if (plant.mSubclass != (int)PlantSubClass.Shooter)
        {
            Volatile.Write(ref result,
                $"HITCHECK {seed}#{plant.mPlantCol},{plant.mRow} skip=subclass {plant.mSubclass} " +
                $"rect=({rect.mX},{rect.mWidth}) overlap={overlap}");
            reportWritten = true;
            return;
        }
        int hpBefore = ZombieTotalHealth(zombie);
        int projBefore = board.mProjectiles.Count;
        int plantHpBefore = plant.mPlantHealth;
        plant.Fire(zombie, plant.mRow, PlantWeapon.Primary);
        int fireDamage = hpBefore - ZombieTotalHealth(zombie);

        int counterBefore = plant.mShootingCounter;
        bool fired = plant.FindTargetAndFire(plant.mRow, PlantWeapon.Primary, LaunchTime.Normal);
        int counterAfter = plant.mShootingCounter;
        int secondDamage = hpBefore - ZombieTotalHealth(zombie) - fireDamage;
        // 打弹类（豌豆/投手那一族）当场不掉血，伤害在弹体命中时结算——用新增弹体数
        // 把"当场结算"和"稍后结算"分开，否则读数会像"这株不打人"。
        int newProjectiles = board.mProjectiles.Count - projBefore;

        Volatile.Write(ref result,
            $"HITCHECK {seed}#{plant.mPlantCol},{plant.mRow} zx={(int)zombie.mX} " +
            $"rect=({rect.mX},{rect.mWidth}) overlap={overlap} effected9={zombie.EffectedByDamage(9u)} " +
            $"Fire->-{fireDamage} FTAF={fired} cnt {counterBefore}->{counterAfter} FTAF->-{secondDamage} " +
            $"proj=+{newProjectiles} plantHp {plantHpBefore}->{plant.mPlantHealth} dead={plant.mDead} " +
            $"asleep={plant.mIsAsleep}");
        reportWritten = true;
    }

    private static int ZombieTotalHealth(Zombie zombie) =>
        zombie.mBodyHealth + zombie.mHelmHealth + zombie.mShieldHealth +
        (zombie.mFlyingHealth > 0 ? zombie.mFlyingHealth : 0);

    // ---------------------------------------------------------------- 公用毒伤
    private sealed class PoisonRun
    {
        public CustomCombat.PoisonSpec Spec;
        public Zombie Target;
        public Zombie Blocked;
        public int PinX;
        public int BlockedX;

        public int Frame;
        public int PrevBody;
        public int PrevDoor;
        public int BodyStart;
        public int DoorStart;
        public int BlockedBodyStart;
        public int BlockedDoorStart;
        public int BoardFrames;
        public int PausedFrames;
        public int LastCounter;
        public int LedgerAt60;
        public bool LedgerAtEnd;
        public int ZombieUpdatesAtArm;
        public int ZombieUpdatesAtEnd;
        public int ZombieEndpointAtArm;
        public int ZombieEndpointAtEnd;
        public int DriverAtArm;
        public int DriverAtEnd;
        public int At60Keys;
        public int At60Records;
        public int At60Hits;
        public int At60Blocked;
        public int At60Driver;
        public bool TintAtStart;
        public bool PoisonedAtStart;
        public bool Killed;
        public bool LedgerAfterKill;
        public int AmbientDamage;
        public readonly List<string> Hits = new List<string>();
        public int ShieldHits;
    }

    private static PoisonRun poisonRun;

    // 独立于框架那套毒钩子的计数器：用来分清"Zombie.Update 根本没被调到"
    // 和"调到了但框架的钩子没包住它"——这两种情况的修法完全不同。
    // 两条安装路径并排计数：台架用 MonoMod 的 Hook 对象直装，框架用 HookEndpointManager.Add，
    // 谁没数到就是谁那条路在这台机器上不管用。
    private static Hook zombieProbeHook;
    private static Delegate zombieEndpointProbe;
    private static int zombieUpdateCalls;
    private static int zombieEndpointCalls;

    private static void EnsureZombieProbe()
    {
        if (zombieProbeHook != null)
        {
            return;
        }
        MethodInfo update = typeof(Zombie).GetMethod("Update", Type.EmptyTypes);
        zombieProbeHook = new Hook(update, (Action<Action<Zombie>, Zombie>)((orig, self) =>
        {
            zombieUpdateCalls++;
            orig(self);
        }));
        zombieEndpointProbe = (Action<Action<Zombie>, Zombie>)((orig, self) =>
        {
            zombieEndpointCalls++;
            orig(self);
        });
        HookEndpointManager.Add(update, zombieEndpointProbe);
    }

    /// <summary>
    /// 跑帧之前必须确认这一局真的在走：窗口丢焦点时游戏自己会弹"暂停"模态框
    /// （<c>LawnApp.LostFocus → DoPauseDialog</c> = 对话框 19 + <c>Board.Pause(true)</c>），
    /// 这时僵尸不再 Update，而我数的是钩子帧 —— 读出来就是"毒一刀都没落"的假结论
    /// （2026-10-27 第一版探针就是这么翻车的）。暂停框按游戏自己的恢复路径摘掉；
    /// 已经 GameOver（对话框 17）的局救不回来，直接失败让调用方重开一局。
    /// </summary>
    private static void EnsureBoardRunning(LawnApp app)
    {
        Board board = app.mBoard;
        Require(board != null, "no board");
        if (app.GetDialog(17) != null)
        {
            // 结算框本身按游戏自己的路径摘掉（GameOverDialog 的按钮就是这么收的），
            // 摘完还跑不动才判失败让调用方重开一局。
            app.KillDialog(17);
        }
        if (app.GetDialog(19) != null)
        {
            app.KillDialog(19);
        }
        if (board.mPaused)
        {
            board.Pause(false);
        }
        Require(!board.mPaused, "board still paused（重开一局再跑）");
    }

    /// <summary>
    /// 从水神包里反射读她那条毒的数值（台架不重抄一遍，否则测的是我的副本不是她的那条定义）。
    /// </summary>
    private static CustomCombat.PoisonSpec TeiTisiPoisonSpec()
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type extras = asm.GetType("PGvZNewPlantMod.Plants.TeiTisiExtras");
            if (extras == null)
            {
                continue;
            }
            FieldInfo field = extras.GetField("Poison",
                BindingFlags.NonPublic | BindingFlags.Static);
            return field?.GetValue(null) as CustomCombat.PoisonSpec;
        }
        return null;
    }

    /// <summary>
    /// scorch &lt;row&gt;：只步进实际夏瓜的 Burn，不混入原生 80/26；自然弹道另由 summerlive 验。
    /// 基准逐帧核验到 301；55/150 帧重复命中后核验到 451，抓提前、推迟、叠层与未刷新。
    /// 以原生 Melon 矩形对齐烧伤源中心，比较行跨度及两侧接触/差 1px 的边界。
    /// </summary>
    private static void RunScorch(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Require(parts.Length == 2, "usage: scorch <row>");
        int row = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        Board board = app.mBoard;
        Require(row >= 0 && row < Constants.MAX_GRIDSIZEY &&
            board.CanPlantAt(6, row, SeedType.Peashooter) == PlantingReason.Ok, "scorch 要合法陆地行");
        Require(!board.mZombies.Any(z => !z.mDead) && CustomBurn.BurnedZombieCount == 0,
            "scorch 要无其他活僵尸/灼热的棋盘，避免污染全局计数与邻伤");
        int adjacentRow = Enumerable.Range(0, Constants.MAX_GRIDSIZEY).FirstOrDefault(r =>
            Math.Abs(r - row) == 1 && board.CanPlantAt(6, r, SeedType.Peashooter) == PlantingReason.Ok, -1);
        Require(adjacentRow >= 0, "找不到相邻陆地行");
        CustomBulletDef bullet = SummerBullet();
        CustomBurn.BurnSpec spec = bullet.Burn;
        int savedSpan = spec.SplashRowSpan;
        var zombies = new List<Zombie>();
        Projectile source = null;
        var notes = new List<string>();
        try
        {
            Zombie Spawn(int r, int x)
            {
                Zombie z = board.AddZombieInRow(ZombieType.Normal, r, 0);
                Require(z != null, "scorch AddZombieInRow failed");
                zombies.Add(z);
                PinZombie(z, x);
                z.cachedZombieRectUpToDate = false;
                // 只抬当前本体防死；不让当前血量、防具存在标志替代四项 max。
                z.mBodyHealth = 90000;
                z.mHelmHealth = z.mShieldHealth = z.mFlyingHealth = 0;
                z.mHasHelm = z.mHasShield = false;
                z.mBodyMaxHealth = 907;
                z.mHelmMaxHealth = 211;
                z.mShieldMaxHealth = 59;
                z.mFlyingMaxHealth = 83;
                return z;
            }
            int pin = board.GridToPixelX(5, row);
            Zombie burning = Spawn(row, pin);
            Zombie adjacent = Spawn(adjacentRow, pin);
            Zombie distant = Spawn(row, pin - 300);
            burning.mBodyMaxHealth = 271;
            burning.mHelmMaxHealth = 113;
            burning.mShieldMaxHealth = 137;
            burning.mFlyingMaxHealth = 41;
            const int maximum = 271 + 113 + 137 + 41;
            const int expected = maximum * 3 / 100; // 16.86 -> 16；不借产品 helper 算预期。
            Require(maximum % 100 != 0 && expected != (907 + 211 + 59 + 83) * 3 / 100,
                "两只靶子的百分比必须可区分");

            TRect center = burning.GetZombieRect();
            int left = center.mX + center.mWidth / 2 - 30;
            // 原生 Melon.GetProjectileRect = (mX+20, mY, 60, height)。与烧伤源中心严格对齐。
            source = board.AddProjectile(left - 20, center.mY, 0, row, ProjectileType.Melon);
            Require(source != null, "native Melon geometry source missing");
            source.mX = left - 20;
            source.mY = center.mY;
            source.mMotionType = ProjectileMotion.Lobbed;
            source.mDamageRangeFlags = 1;
            CustomBullets.Attach(source, bullet);
            Require(source.GetProjectileRect().mX == left && source.GetProjectileRect().mWidth == 60,
                "native Melon source 未对齐");

            CustomBurn.ApplyBurn(burning, spec, source);
            Require(CustomBurn.HasBurn(burning) && !CustomCombat.HasPoison(burning), "Burn 被当成 Poison");
            CustomCombat.ApplyPoison(burning, CustomCombat.PoisonPea);
            Require(CustomBurn.HasBurn(burning) && CustomCombat.HasPoison(burning), "Burn/Poison 不能共存");
            CustomBurn.ClearBurn(burning);
            Require(!CustomBurn.HasBurn(burning) && CustomCombat.HasPoison(burning), "ClearBurn 清掉了 Poison");
            CustomBurn.ApplyBurn(burning, spec, source);
            CustomCombat.ClearPoison(burning);
            Require(CustomBurn.HasBurn(burning) && !CustomCombat.HasPoison(burning), "ClearPoison 清掉了 Burn");
            CustomBurn.ClearBurn(burning);

            // 刀数与时序全从产品定义推：首刀 FirstTick、之后每 Interval 一刀、
            // 写了 Ticks 就共 N 刀（末刀落在最后一帧，账随即收走）。
            int firstTick = spec.FirstTick > 0 ? spec.FirstTick : spec.Interval;
            int totalTicks = spec.Ticks > 0
                ? spec.Ticks : (CustomBurn.TickSpan(spec) - firstTick) / spec.Interval + 1;
            int lastTickFrame = firstTick + spec.Interval * (totalTicks - 1);

            void Timeline(bool repeat)
            {
                int mainStart = ZombieTotalHealth(burning), adjStart = ZombieTotalHealth(adjacent);
                int farStart = ZombieTotalHealth(distant);
                int hits = CustomBurn.BurnHitsApplied, splashes = CustomBurn.BurnSplashApplied;
                CustomBurn.ApplyBurn(burning, spec, source);
                // 刷新只延长账、不动下一刀：所以到期时刻 = 最后一次刷新 + 原本的末刀时刻。
                int expire = repeat ? 150 + lastTickFrame : lastTickFrame;
                for (int frame = 1; frame <= expire + 1; frame++)
                {
                    CustomBurn.TickBurnBoard(board);
                    if (repeat && (frame == 55 || frame == 150))
                    {
                        for (int n = 0; n < 3; n++) CustomBurn.ApplyBurn(burning, spec, source);
                    }
                    int cuts = frame < firstTick ? 0 : 1 + (frame - firstTick) / spec.Interval;
                    int mainDrop = mainStart - ZombieTotalHealth(burning);
                    int adjDrop = adjStart - ZombieTotalHealth(adjacent);
                    bool active = CustomBurn.HasBurn(burning);
                    string state = $"{(repeat ? "refresh" : "base")} f={frame} main={mainDrop} neighbour={adjDrop} burn={active}";
                    Require(mainDrop == cuts * expected && adjDrop == cuts * expected, state + " 错刀/叠层/按邻居上限算伤");
                    Require(CustomBurn.BurnHitsApplied - hits == cuts &&
                        CustomBurn.BurnSplashApplied - splashes == cuts, state + " 刀数不符");
                    Require(active == (frame < expire), state + " 到期/刷新错误");
                    Require(!CustomCombat.HasPoison(burning) && !CustomBurn.HasBurn(adjacent) &&
                        !CustomCombat.HasPoison(adjacent) && !CustomBurn.HasBurn(distant) &&
                        farStart == ZombieTotalHealth(distant), state + " 状态串账/远处掉血");
                    if (frame <= 2 || frame == firstTick - 1 || frame == firstTick ||
                        frame % spec.Interval == 0 || frame % spec.Interval == 1 ||
                        frame == expire || frame == expire + 1)
                        notes.Add(state);
                }
            }
            Timeline(false);
            Timeline(true);

            void Boundary(string label, int targetRow, int x, bool expectedHit)
            {
                CustomBurn.ClearBurn(burning);
                adjacent.mRow = targetRow;
                PinZombie(adjacent, x);
                adjacent.cachedZombieRectUpToDate = false;
                bool nativeHit = source.IsZombieHitBySplash(adjacent);
                Require(nativeHit == expectedHit, label + " 原生边界前提不成立");
                int hp = ZombieTotalHealth(adjacent), mainHp = ZombieTotalHealth(burning);
                int farHp = ZombieTotalHealth(distant), splashes = CustomBurn.BurnSplashApplied;
                CustomBurn.ApplyBurn(burning, spec, source);
                for (int f = 0; f < 100; f++) CustomBurn.TickBurnBoard(board);
                int drop = hp - ZombieTotalHealth(adjacent);
                Require(mainHp - ZombieTotalHealth(burning) == expected &&
                    drop == (expectedHit ? expected : 0) &&
                    CustomBurn.BurnSplashApplied - splashes == (expectedHit ? 1 : 0) &&
                    farHp == ZombieTotalHealth(distant) && !CustomBurn.HasBurn(adjacent),
                    label + $" native={nativeHit} burnDamage={drop}");
                notes.Add(label + $"={nativeHit}/{drop}");
            }
            foreach (int delta in new[] { -2, -1, 0, 1, 2 })
                if (row + delta >= 0 && row + delta < Constants.MAX_GRIDSIZEY)
                    Boundary("row" + delta, row + delta, pin, Math.Abs(delta) <= 1);
            // 两边各验恰好接触（overlap=0）和差 1px；横向判定不依赖邻行的 Y。
            TRect neighbourRect = adjacent.GetZombieRect();
            int offset = neighbourRect.mX - adjacent.mX;
            int rightTouch = left + 60 - offset;
            int leftTouch = left - neighbourRect.mWidth - offset;
            Boundary("right-touch", adjacentRow, rightTouch, true);
            Boundary("right-out1", adjacentRow, rightTouch + 1, false);
            Boundary("left-touch", adjacentRow, leftTouch, true);
            Boundary("left-out1", adjacentRow, leftTouch - 1, false);

            CustomBurn.ClearBurn(burning);
            adjacent.mRow = adjacentRow;
            PinZombie(adjacent, pin);
            adjacent.cachedZombieRectUpToDate = false;
            spec.SplashRowSpan = 0; // 唯一变异真实定义的地方，外层 finally 必须还原。
            int adjBefore = ZombieTotalHealth(adjacent), mainBefore = ZombieTotalHealth(burning);
            int splashBefore = CustomBurn.BurnSplashApplied;
            CustomBurn.ApplyBurn(burning, spec, source);
            for (int f = 0; f < 100; f++) CustomBurn.TickBurnBoard(board);
            Require(mainBefore - ZombieTotalHealth(burning) == expected &&
                adjBefore == ZombieTotalHealth(adjacent) && CustomBurn.BurnSplashApplied == splashBefore,
                "行跨度0时邻行仍掉血，或正例本体没挨刀");
            notes.Add("span0=0 independent=ok max=562 cut=16 neighbourMax=1260");
        }
        finally
        {
            spec.SplashRowSpan = savedSpan;
            try
            {
                if (source != null && !source.mDead) source.Die();
            }
            finally
            {
                var cleanupErrors = new List<Exception>();
                foreach (Zombie zombie in zombies)
                {
                    try { RetireTestZombie(zombie); }
                    catch (Exception error) { cleanupErrors.Add(error); }
                }
                if (cleanupErrors.Count > 0) throw new AggregateException("scorch cleanup failed", cleanupErrors);
            }
        }
        Volatile.Write(ref result, "PASS scorch " + string.Join(" | ", notes));
        reportWritten = true;
    }

    private const string SummerBulletId = "SummerMelon:summer_melon";

    private static CustomBulletDef SummerBullet()
    {
        CustomBulletDef bullet = CustomBullets.Find(SummerBulletId);
        Require(bullet != null && bullet.Burn != null, "missing " + SummerBulletId + ".Burn");
        CustomBurn.BurnSpec spec = bullet.Burn;
        Require(bullet.Carrier == ProjectileType.Melon && bullet.Damage == 0 &&
            bullet.SplashDamage == 0 && bullet.Impact == null && bullet.Poison == null,
            "夏瓜必须保留原生 Melon 直伤/溅射，且不带 Poison");
        Require(spec.DamagePercent == 3 && spec.RoundDown && spec.Ticks == 3 &&
            spec.Interval == 100 && spec.FirstTick == 1 &&
            spec.SplashWidth == 60 && spec.SplashRowSpan == 1,
            "夏瓜实际 Burn 定义不符合 3%/floor/3刀/首刀1/100/60px/±1 行");
        return bullet;
    }

    private static void RetireTestZombie(Zombie zombie)
    {
        try { CustomBurn.ClearBurn(zombie); }
        finally
        {
            try { CustomCombat.ClearPoison(zombie); }
            finally { if (zombie != null && !zombie.mDead) zombie.DieNoLoot(false); }
        }
    }

    // ---------------------------------------------------------------- 夏瓜真实发射/弹道/命中（700 个 UpdateFrames，自有临时 hook）
    private sealed class SummerRun
    {
        public Board Board;
        public CustomPlantDef Def;
        public Plant Plant;
        public Zombie Target;
        public Zombie Neighbour;
        public int Row, NeighbourRow, Frame, LastCounter;
        public int MainStart = -1, NeighbourStart = -1;
        public int BurnStart, SplashStart, FireCount, ImpactCount, NonNullImpacts, NativeShots;
        public int DirectDamage, NativeSplashDamage, BurnDamage;
        public bool BurnSeen;
        public string Error;
        public Hook FireHook, ImpactHook;
        public readonly List<Zombie> Zombies = new List<Zombie>();
        public readonly HashSet<Projectile> Shots = new HashSet<Projectile>();
        public readonly List<string> Events = new List<string>();
    }

    private static volatile SummerRun summerRun;
    private const int SummerFrames = 700;

    private static void RunSummerLive(LawnApp app)
    {
        var run = new SummerRun
        {
            Board = app?.mBoard,
            BurnStart = CustomBurn.BurnHitsApplied,
            SplashStart = CustomBurn.BurnSplashApplied,
        };
        summerRun = run;
        reportWritten = true;
        try
        {
            // 动态 Seed：109 在沙盒可能是 ExamplePlantPult，绝不能当作夏瓜。
            run.Def = CustomPlantRegistry.Defs.SingleOrDefault(d => d.PackId == "SummerMelon");
            Require(run.Def != null, "没有 PackId=SummerMelon 的植物定义");
            RequireLiveBoard(app, "summerlive");
            Board board = run.Board;
            // 坚不可摧开局就有屏外僵尸潮，不能要求整张棋盘空；只要求"战场这一侧"干净，
            // 否则它们会替我们挨打/打我们，伤害就没法归因到夏瓜。
            Require(board.mPlants.Count == 0 &&
                !board.mZombies.Any(z => !z.mDead && z.mPosX < 900f) &&
                !board.mProjectiles.Any(p => !p.mDead) &&
                CustomBurn.BurnedZombieCount == 0 && CustomCombat.PoisonedZombieCount == 0,
                "summerlive 必须在战场干净（无植物/场内僵尸/弹体/效果账本）的沙盒活局开始");
            Require(!GlobalStaticVars.gFastMo && !GlobalStaticVars.gSlowMo, "summerlive 要正常游戏速度");
            SummerBullet();
            int col = -1, row = -1, neighbourRow = -1;
            for (int c = 1; c <= 3 && col < 0; c++)
            {
                for (int r = 0; r < Constants.MAX_GRIDSIZEY && col < 0; r++)
                {
                    if (board.CanPlantAt(c, r, run.Def.Seed) != PlantingReason.Ok ||
                        board.CanPlantAt(6, r, SeedType.Peashooter) != PlantingReason.Ok) continue;
                    int adjacent = Enumerable.Range(0, Constants.MAX_GRIDSIZEY).FirstOrDefault(n =>
                        Math.Abs(n - r) == 1 && board.CanPlantAt(6, n, SeedType.Peashooter) == PlantingReason.Ok, -1);
                    if (adjacent < 0) continue;
                    col = c;
                    row = r;
                    neighbourRow = adjacent;
                }
            }
            Require(col >= 0, "找不到合法空地和两条相邻陆地行");
            run.Row = row;
            run.NeighbourRow = neighbourRow;
            run.Plant = board.AddPlant(col, row, run.Def.Seed, SeedType.None);
            Require(run.Plant != null && run.Plant.mPlantCol == col && run.Plant.mRow == row &&
                ReferenceEquals(CustomPlantRegistry.GetDef(run.Plant.mSeedType), run.Def) &&
                ReferenceEquals(CustomPlantRegistry.Get(run.Plant)?.Definition, run.Def) && run.Def.IsMine(run.Plant),
                "合法落株后 GetDef/Get/IsMine 或落点不符");
            Zombie Spawn(int r)
            {
                Zombie zombie = board.AddZombieInRow(ZombieType.Normal, r, 0);
                Require(zombie != null, "summerlive AddZombieInRow failed");
                run.Zombies.Add(zombie);
                zombie.mBodyHealth = 90000; // 只防死，保留原生四项上限和自然动作。
                PinZombie(zombie, board.GridToPixelX(6, r));
                zombie.cachedZombieRectUpToDate = false;
                return zombie;
            }
            run.Target = Spawn(row);
            run.Neighbour = Spawn(neighbourRow);
            run.MainStart = ZombieTotalHealth(run.Target);
            run.NeighbourStart = ZombieTotalHealth(run.Neighbour);
            run.BurnDamage = (int)(((long)run.Target.mBodyMaxHealth + run.Target.mHelmMaxHealth +
                run.Target.mShieldMaxHealth + run.Target.mFlyingMaxHealth) * 3 / 100);
            Require(run.BurnDamage > 0, "原生正常僵尸的每刀灼热必须大于0");
            run.FireHook = new Hook(
                typeof(Plant).GetMethod("Fire", new[] { typeof(Zombie), typeof(int), typeof(PlantWeapon) }),
                (Action<Action<Plant, Zombie, int, PlantWeapon>, Plant, Zombie, int, PlantWeapon>)ObserveSummerFire);
            run.ImpactHook = new Hook(typeof(Projectile).GetMethod("DoImpact", new[] { typeof(Zombie) }),
                (Action<Action<Projectile, Zombie>, Projectile, Zombie>)ObserveSummerImpact);
            run.LastCounter = board.mMainCounter;
            board.Pause(false);
            CheckSummerLive(app, run);
            Volatile.Write(ref result, "SUMMERLIVE armed " + SummerReport(run));
        }
        catch (Exception error)
        {
            FinishSummerLive(app, run, error.ToString());
        }
    }

    private static void ObserveSummerFire(Action<Plant, Zombie, int, PlantWeapon> orig,
        Plant self, Zombie target, int row, PlantWeapon weapon)
    {
        SummerRun run = summerRun;
        if (run == null || !ReferenceEquals(self, run.Plant))
        {
            orig(self, target, row, weapon);
            return;
        }
        var before = new HashSet<Projectile>(run.Board.mProjectiles);
        int seed = (int)self.mSeedType;
        string pack = CustomPlantRegistry.GetDef(self.mSeedType)?.PackId;
        bool mine = run.Def.IsMine(self);
        run.FireCount++;
        orig(self, target, row, weapon); // 只观察真正的自然 Fire；绝不自己调用 Fire/FindTargetAndFire。
        try
        {
            Projectile[] created = run.Board.mProjectiles.Where(p => !before.Contains(p)).ToArray();
            foreach (Projectile shot in created) run.Shots.Add(shot); // 校验失败也保有所有新弹的清理权。
            run.Events.Add($"f{run.Frame + 1}:Fire seed={seed} pack={pack} IsMine={mine} target={(target == null ? "null" : target.mZombieType.ToString())}");
            // 真开火这一拍 mSeedType 是**借来的**西瓜投手（39）：kit 在 UpdateShooting 里
            // 临时换种子跑原生那段，所以这里不能用 seed 判身份，只能问 IsMine / 行为对象。
            Require(mine && ReferenceEquals(CustomPlantRegistry.Get(self)?.Definition, run.Def),
                "真实 Fire 不是动态注册的夏瓜行为对象");
            foreach (Projectile shot in created)
            {
                string id = CustomBullets.Of(shot)?.Id;
                run.Events.Add($"f{run.Frame + 1}:shot {shot.mProjectileType}/{shot.mMotionType} id={id ?? "null"}");
                Require(!shot.mDead && shot.mProjectileType == ProjectileType.Melon &&
                    shot.mMotionType == ProjectileMotion.Lobbed && id == SummerBulletId,
                    "真实 Fire 差集未生成正确挂签的 Melon/Lobbed 夏瓜");
                run.NativeShots++;
            }
            Require(created.Length >= 1 && created.Length <= 3,
                "一次夏瓜开火应投出 1~3 颗（上/本/下三行各最多一颗），实际=" + created.Length);
        }
        catch (Exception error) { run.Error ??= error.ToString(); }
    }

    private static void ObserveSummerImpact(Action<Projectile, Zombie> orig, Projectile shot, Zombie target)
    {
        SummerRun run = summerRun;
        if (run == null || !run.Shots.Contains(shot))
        {
            orig(shot, target);
            return;
        }
        // orig 会 Die 弹体；身份、主命中、原生溅射门和血量必须在调用前取。
        string id = CustomBullets.Of(shot)?.Id;
        ProjectileType carrier = shot.mProjectileType;
        ProjectileMotion motion = shot.mMotionType;
        bool victimIsTarget = ReferenceEquals(target, run.Target);
        bool victimIsNeighbour = ReferenceEquals(target, run.Neighbour);
        int mainHp = ZombieTotalHealth(run.Target), neighbourHp = ZombieTotalHealth(run.Neighbour);
        int hits = CustomBurn.BurnHitsApplied, splashes = CustomBurn.BurnSplashApplied;
        run.ImpactCount++;
        if (target != null) run.NonNullImpacts++;
        orig(shot, target);
        try
        {
            int mainDrop = mainHp - ZombieTotalHealth(run.Target);
            int neighbourDrop = neighbourHp - ZombieTotalHealth(run.Neighbour);
            // 三行索敌：这一发可能是冲着邻行那只去的。被砸中的吃 80 直伤，
            // 另一只最多吃 26 的原生溅射（不在 60px 矩形里就是 0）。
            int victimDrop = victimIsTarget ? mainDrop : neighbourDrop;
            int otherDrop = victimIsTarget ? neighbourDrop : mainDrop;
            run.DirectDamage += victimDrop;
            run.NativeSplashDamage += otherDrop;
            bool burning = CustomBurn.HasBurn(target), poison = CustomCombat.HasPoison(target);
            run.Events.Add($"f{run.Frame + 1}:DoImpact {carrier}/{motion} id={id ?? "null"} " +
                $"victim={(victimIsTarget ? "main" : victimIsNeighbour ? "neighbour" : "other")} " +
                $"hp={mainHp}->{ZombieTotalHealth(run.Target)}/{neighbourHp}->{ZombieTotalHealth(run.Neighbour)} " +
                $"burn={burning} poison={poison}");
            Require(carrier == ProjectileType.Melon && motion == ProjectileMotion.Lobbed && id == SummerBulletId,
                "真实 DoImpact 丢了 Melon/Lobbed 或夏瓜标签");
            Require(target != null && (victimIsTarget || victimIsNeighbour),
                "夏瓜真实落点不是本行/邻行那两只靶子之一");
            Require(victimDrop == 80 && (otherDrop == 0 || otherDrop == 80 / 3),
                $"被砸中那只应吃 80、另一只 0 或 26，实际={victimDrop}/{otherDrop}");
            Require(CustomBurn.BurnHitsApplied == hits && CustomBurn.BurnSplashApplied == splashes,
                "灼热在 DoImpact 中提前落刀");
            Require(burning && !poison, "被砸中那只没挂上独立灼热（或被当成毒）");
            run.BurnSeen = true;
        }
        catch (Exception error) { run.Error ??= error.ToString(); }
        finally { run.Shots.Remove(shot); }
    }

    private static void CheckSummerLive(LawnApp app, SummerRun run)
    {
        RequireLiveBoard(app, "summerlive");
        Require(ReferenceEquals(app.mBoard, run.Board) && !run.Board.mPaused,
            "summerlive 棋盘被替换或已暂停");
        Require(!GlobalStaticVars.gFastMo && !GlobalStaticVars.gSlowMo, "采样中游戏速度改变");
        Require(run.Error == null, run.Error ?? "summerlive hook error");
        Require(run.Plant != null && run.Board.mPlants.Contains(run.Plant) && !run.Plant.mDead &&
            !run.Plant.mSquished && run.Plant.mRow == run.Row && run.Def.IsMine(run.Plant) &&
            ReferenceEquals(CustomPlantRegistry.GetDef(run.Plant.mSeedType), run.Def) &&
            ReferenceEquals(CustomPlantRegistry.Get(run.Plant)?.Definition, run.Def), "夏瓜对象或身份丢失");
        Require(run.Board.mPlants.Count == 1 &&
            run.Board.mZombies.Count(z => !z.mDead && z.mPosX < 900f) == 2,
            "战场混入其他实体，不能归因夏瓜伤害");
        foreach (Zombie zombie in run.Zombies)
        {
            Require(ReferenceEquals(zombie.mBoard, run.Board) && run.Board.mZombies.Contains(zombie) &&
                !zombie.mDead && !zombie.IsDeadOrDying() && zombie.mZombieType == ZombieType.Normal,
                "测试僵尸消失/死亡/被对象池复用");
            Require(!CustomCombat.HasPoison(zombie), "夏瓜靶子不该中毒");
        }
        Require(run.Target.mRow == run.Row && run.Neighbour.mRow == run.NeighbourRow,
            "靶子换行");
    }

    private static void TickSummerLive(LawnApp app, SummerRun run)
    {
        run.Frame++;
        CheckSummerLive(app, run); // orig 前后都验，弹窗/判负/替换不算零伤害。
        Require(run.Board.mMainCounter == run.LastCounter + 1,
            $"UpdateFrames 没有恰好推进一个游戏帧：{run.LastCounter}->{run.Board.mMainCounter}");
        run.LastCounter = run.Board.mMainCounter;
        foreach (Projectile shot in run.Shots)
            Require(!shot.mDead && run.Board.mProjectiles.Contains(shot) &&
                ReferenceEquals(shot.mBoard, run.Board) && CustomBullets.Of(shot)?.Id == SummerBulletId,
                "夏瓜弹体未经过真实 DoImpact 就消失/丢签");
        if (run.Frame < SummerFrames)
        {
            if (run.Frame % 100 == 0) Volatile.Write(ref result, "SUMMERLIVE sampling " + SummerReport(run));
            return;
        }
        int burns = CustomBurn.BurnHitsApplied - run.BurnStart;
        int splashes = CustomBurn.BurnSplashApplied - run.SplashStart;
        // 三行索敌：一次开火最多三颗（每行一颗），所以颗数落在 [Fire, Fire*3]；
        // 精确的每刀数值与刀数由 scorch 那一组负责，这里只验"真开火→真命中→真灼热"这条链。
        Require(run.FireCount > 0 && run.NativeShots >= run.FireCount &&
            run.NativeShots <= run.FireCount * 3 && run.ImpactCount > 0 &&
            run.NonNullImpacts == run.ImpactCount && run.BurnSeen && CustomBurn.HasBurn(run.Target),
            "700帧内没有完整真实夏瓜 Fire/Melon/Lobbed/非null命中/Burn 证据");
        Require(burns > 0 && splashes > 0, "没有逐拍灼热/邻伤增量");
        FinishSummerLive(app, run, null);
    }

    private static string SummerReport(SummerRun run) =>
        $"seed={(run.Def == null ? -1 : (int)run.Def.Seed)} pack={run.Def?.PackId ?? "missing"} " +
        $"plantSeed={(run.Plant == null ? -1 : (int)run.Plant.mSeedType)} f={run.Frame}/{SummerFrames} " +
        $"Fire={run.FireCount} Impact={run.ImpactCount} nonNull={run.NonNullImpacts} Melon/Lobbed={run.NativeShots} " +
        $"BurnHits=+{CustomBurn.BurnHitsApplied - run.BurnStart} Splash=+{CustomBurn.BurnSplashApplied - run.SplashStart} " +
        $"hp={run.MainStart}->{(run.Target == null ? -1 : ZombieTotalHealth(run.Target))} " +
        $"neighbourHp={run.NeighbourStart}->{(run.Neighbour == null ? -1 : ZombieTotalHealth(run.Neighbour))} " +
        $"nativeDamage={run.DirectDamage}/{run.NativeSplashDamage} burnCut={run.BurnDamage} " +
        $"HasBurn={CustomBurn.HasBurn(run.Target)} HasPoison={CustomCombat.HasPoison(run.Target)} " +
        $"neighbourBurn={CustomBurn.HasBurn(run.Neighbour)}";

    private static void FinishSummerLive(LawnApp app, SummerRun run, string failure)
    {
        string snapshot = SummerReport(run); // 失败杀靶前读血，不能把清理算伤害。
        var errors = new List<string>();
        bool fireDisposed = false, impactDisposed = false;
        if (failure != null) errors.Add(failure);
        void Attempt(Action action)
        {
            try { action(); }
            catch (Exception error) { errors.Add(error.ToString()); }
        }
        void Freeze(Board board)
        {
            if (board == null) return;
            try { board.Pause(true); }
            finally { board.mPaused = true; } // 即使暂停音效失败也不再放行无人控制的局。
        }
        try
        {
            Attempt(() => Freeze(run.Board));
            if (!ReferenceEquals(app?.mBoard, run.Board)) Attempt(() => Freeze(app?.mBoard));
        }
        finally
        {
            // 只卸自己这两只临时 hook；不动 kit、其他探针或 UpdateFrames 的公共入口。
            Attempt(() => { run.FireHook?.Dispose(); fireDisposed = true; });
            Attempt(() => { run.ImpactHook?.Dispose(); impactDisposed = true; });
            run.FireHook = run.ImpactHook = null;
            if (errors.Count > 0)
            {
                foreach (Zombie zombie in run.Zombies) Attempt(() => RetireTestZombie(zombie));
                foreach (Projectile shot in run.Shots)
                    Attempt(() => { if (!shot.mDead && run.Board.mProjectiles.Contains(shot)) shot.Die(); });
            }
            // 成功保留暂停中的植物/靶子/灼热画面，供主会话截图；失败清账后同样冻结。
            summerRun = null;
        }
        reportWritten = true;
        Volatile.Write(ref result, (errors.Count == 0 ? "PASS summerlive " : "FAIL summerlive ") +
            snapshot + $" paused={run.Board?.mPaused} hooksDisposed={fireDisposed}/{impactDisposed} events=[" +
            string.Join(" | ", run.Events) + "]" +
            (errors.Count == 0 ? "" : " error=" + string.Join(" | ", errors)));
    }

    private static void RunPoison(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 2, "usage: poison <row>");
        int row = int.Parse(parts[1]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Require(poisonRun == null && behave == null && watchPlant == null, "另一轮还在跑");
        EnsureBoardRunning(app);
        CustomCombat.PoisonSpec spec = TeiTisiPoisonSpec();
        Require(spec != null, "反射读不到水神的毒定义（TeiTisiExtras.Poison）");

        Zombie target = board.AddZombieInRow(ZombieType.Door, row, 0);
        Zombie blocked = board.AddZombieInRow(ZombieType.Door, row, 0);
        Require(target != null && blocked != null, "AddZombieInRow failed");
        Require(target.mShieldHealth > 0 && blocked.mShieldHealth > 0, "刷出来的僵尸不带门，穿盾分支没被测到");
        // 钉在第 5 格（远离房子）：这局没人防守，放开的僵尸走进房子会把局判输，
        // 之后整局冻住，量到的"零伤害"全是假的。每一拍都按同一个位置复位。
        target.mX = board.GridToPixelX(5, row);
        target.mPosX = target.mX;
        // 血量抬到 4 刀以上：原生 270 血挨到第 3 刀就死，第 4 刀（叠毒补出来的那刀）
        // 就没法在同一只僵尸身上量到了。
        target.mBodyHealth = 9000;
        blocked.mX = Constants.WIDE_BOARD_WIDTH + 600;
        blocked.mPosX = blocked.mX;

        CustomCombat.ApplyPoison(target, spec);
        CustomCombat.ApplyPoison(blocked, spec);
        EnsureZombieProbe();
        poisonRun = new PoisonRun
        {
            Spec = spec,
            Target = target,
            Blocked = blocked,
            PinX = target.mX,
            BlockedX = blocked.mX,
            PrevBody = target.mBodyHealth,
            PrevDoor = target.mShieldHealth,
            BodyStart = target.mBodyHealth,
            DoorStart = target.mShieldHealth,
            BlockedBodyStart = blocked.mBodyHealth,
            BlockedDoorStart = blocked.mShieldHealth,
            TintAtStart = CustomCombat.HasVisiblePoison(target),
            PoisonedAtStart = CustomCombat.HasPoison(target),
            ZombieUpdatesAtArm = zombieUpdateCalls,
            ZombieEndpointAtArm = zombieEndpointCalls,
            DriverAtArm = CustomCombat.PoisonDriverCalls,
        };
        Volatile.Write(ref result,
            $"POISON+ armed 固定{spec.FixedDamage}x{spec.TopUpHits} 间隔{spec.Interval} " +
            $"首刀{spec.FirstTick} 时长{spec.Duration} take={spec.TakeFlags} range={spec.RangeFlags} " +
            $"减速{spec.SlowPercent} 挂上={poisonRun.PoisonedAtStart} 显色={poisonRun.TintAtStart} " +
            $"门={target.mShieldHealth} 本体={target.mBodyHealth} zx={(int)target.mX} " +
            $"场外zx={(int)blocked.mX}");
        reportWritten = true;
    }

    /// <summary>
    /// 进程里名为 PGvZPlantKit 的程序集有几份、分别从哪加载。
    /// 毒账本是静态字典：如果宿主和台架各自持有不同副本，两边读写的就不是同一本账，
    /// "挂着毒却一步不动"就是这么来的，所以先把它数清楚再怀疑逻辑。
    /// </summary>
    private static string KitCopies()
    {
        var parts = new List<string>();
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(asm.GetName().Name, "PGvZPlantKit", StringComparison.Ordinal))
            {
                string location;
                try
                {
                    location = asm.IsDynamic ? "<dynamic>" : asm.Location;
                }
                catch
                {
                    location = "<unreadable>";
                }
                parts.Add(System.IO.Path.GetFileName(location) + "@" +
                    asm.GetHashCode().ToString("X"));
            }
        }
        return parts.Count + "份[" + string.Join(",", parts) + "]";
    }

    /// <summary>
    /// 无冷却种植（ZBCheatDialog 那个 <c>mEasyPlantingCheat</c>）下的模组卡读数：
    /// 把这张卡压进冷却，分别在"作弊关/开"两态读 kit 的灰度和引擎自己的灰度。
    /// 老 kit 自己另写了一套判定、漏了作弊那条分支，开着作弊时仍回 128 —— 这条会红。
    /// 用法：cheatgray &lt;seedId&gt;
    /// </summary>
    private static void RunCheatGray(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 2, "usage: cheatgray <seedId>");
        SeedType seed = (SeedType)int.Parse(parts[1]);
        Board board = app.mBoard;
        Require(board != null, "no board");
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        EnsureBoardRunning(app);
        SeedBank bank = board.mSeedBank;
        SeedPacket packet = null;
        for (int i = 0; i < bank.mNumPackets; i++)
        {
            if (bank.mSeedPackets[i].mPacketType == seed)
            {
                packet = bank.mSeedPackets[i];
                break;
            }
        }
        if (packet == null)
        {
            packet = bank.mSeedPackets[0];
            packet.SetPacketType(seed, SeedType.None);
        }
        SeedType savedType = packet.mPacketType;
        SeedType savedImitater = packet.mImitaterType;
        bool savedActive = packet.mActive;
        bool savedRefreshing = packet.mRefreshing;
        int savedCounter = packet.mRefreshCounter;
        int savedTime = packet.mRefreshTime;
        bool savedCheat = app.mEasyPlantingCheat;
        try
        {
            packet.mActive = false;
            packet.mRefreshing = true;
            packet.mRefreshTime = 750;
            packet.mRefreshCounter = 100;
            app.mEasyPlantingCheat = false;
            int offOurs = CustomUi.PacketGrayness(packet);
            float offDark = CustomUi.PacketDarkness(packet);
            app.mEasyPlantingCheat = true;
            int onOurs = CustomUi.PacketGrayness(packet);
            float onDark = CustomUi.PacketDarkness(packet);
            Volatile.Write(ref result, "CHEATGRAY " + seed +
                " 关作弊=kit " + offOurs + "/" + offDark.ToString("0.##") +
                " 开作弊=kit " + onOurs + "/" + onDark.ToString("0.##"));
            reportWritten = true;
            Require(offOurs == 128 && offDark > 0f,
                "关着作弊、卡正在冷却时该压暗：读到 " + offOurs + "/" + offDark);
            Require(onOurs == 255 && onDark == 0f,
                "开了无冷却种植模组卡仍灰：读到 " + onOurs + "/" + onDark);
        }
        finally
        {
            app.mEasyPlantingCheat = savedCheat;
            packet.SetPacketType(savedType, savedImitater);
            packet.mActive = savedActive;
            packet.mRefreshing = savedRefreshing;
            packet.mRefreshCounter = savedCounter;
            packet.mRefreshTime = savedTime;
        }
    }

    /// <summary>
    /// 紫卡限制的台架：只把升级卡放进卡槽、基础卡不放，按"开始"必须被拦在选卡界面，
    /// 并且占的是 19 号告警槽（原生占了 0..18）；两张都在时这道门必须放行。
    /// 用法：upgradegate &lt;基础卡seedId&gt; &lt;升级卡seedId&gt;（prepare 之后、choose/start 之前跑）
    /// 结束后把 mSeedsInBank、每张 ChosenSeed 的状态和告警跳过表原样还原。
    /// </summary>
    private static void RunUpgradeGate(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 3, "usage: upgradegate <baseSeedId> <cardSeedId>");
        SeedType baseSeed = (SeedType)int.Parse(parts[1]);
        SeedType card = (SeedType)int.Parse(parts[2]);
        SeedChooserScreen chooser = app.mSeedChooserScreen;
        Require(chooser != null, "没有选卡界面（本 op 要在 prepare 之后、start 之前跑）");
        ChosenSeed[] all = chooser.mChosenSeeds;
        int[] savedState = new int[all.Length];
        for (int i = 0; i < all.Length; i++)
        {
            savedState[i] = all[i] == null ? -1 : (int)all[i].mSeedState;
        }
        int savedBank = chooser.mSeedsInBank;
        bool[] savedWaved = (bool[])chooser.mPickWarningsWaved.Clone();
        int savedPending = chooser.mPendingWarningId;
        var notes = new List<string>();
        try
        {
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null)
                {
                    all[i].mSeedState = ChosenSeedState.SEED_IN_CHOOSER;
                }
            }
            chooser.mSeedsInBank = 0;
            for (int i = 0; i < chooser.mPickWarningsWaved.Length; i++)
            {
                chooser.mPickWarningsWaved[i] = false;
            }
            chooser.PreChooseSeed(card, true);
            Require(InBank(chooser, card), "升级卡没能进卡槽，前提不成立");
            Require(!InBank(chooser, baseSeed), "基础卡不知怎么在槽里");
            Require(app.mBoard?.mCutScene != null, "没有棋盘/开场动画");
            Require(app.mBoard.mCutScene.mSeedChoosing, "现在不在选卡状态，验不了这道门");
            chooser.mPendingWarningId = -1;
            chooser.OnStartButton();
            notes.Add("只带紫卡: pending=" + chooser.mPendingWarningId + " 选卡中=" +
                app.mBoard.mCutScene.mSeedChoosing);
            Require(app.mBoard.mCutScene.mSeedChoosing, "只带升级卡也直接开局了");
            Require(chooser.mPendingWarningId == 19,
                "拦是拦了，但占的不是 19 号槽（读到 " + chooser.mPendingWarningId +
                "）：要么弹的是别的警告，要么这道门没接在 OnStartButton 上");
            app.KillDialog(28);
            chooser.PreChooseSeed(baseSeed, true);
            Require(InBank(chooser, baseSeed), "基础卡没能进卡槽");
            chooser.mPendingWarningId = -1;
            bool blocked = CustomChooserUi.BlockStartForCustomUpgrades(chooser);
            notes.Add("两张都带: 拦=" + blocked + " pending=" + chooser.mPendingWarningId);
            reportWritten = true;
            Volatile.Write(ref result, "UPGRADEGATE " + string.Join(" | ", notes));
            Require(!blocked, "两张都带了还在拦");
            Require(chooser.mPendingWarningId != 19, "放行时还占了 19 号告警槽");
        }
        finally
        {
            for (int i = 0; i < all.Length && i < savedState.Length; i++)
            {
                if (all[i] != null && savedState[i] >= 0)
                {
                    all[i].mSeedState = (ChosenSeedState)savedState[i];
                }
            }
            chooser.mSeedsInBank = savedBank;
            Array.Copy(savedWaved, chooser.mPickWarningsWaved, savedWaved.Length);
            chooser.mPendingWarningId = savedPending;
        }
    }

    /// <summary>这张卡此刻在不在卡槽里（模仿者带着它也算，和 PickedPlantType 一个口径）。</summary>
    private static bool InBank(SeedChooserScreen chooser, SeedType seed)
    {
        ChosenSeed[] all = chooser.mChosenSeeds;
        for (int i = 0; i < all.Length; i++)
        {
            ChosenSeed chosen = all[i];
            if (chosen != null && chosen.mSeedState == ChosenSeedState.SEED_IN_BANK &&
                (chosen.mSeedType == seed || chosen.mImitaterType == seed))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 冰系喷菇实机探针：种下 → 摆三只屏幕门僵尸（本行、上一行、远处的对照）→ 跑 700 帧，
    /// 报每只掉多少血、减速计数器峰值、以及升级卡的四条判定。
    /// 蘑菇在这关（白天背景）会睡觉，而"种在同一格叫醒它"会让射击循环整个停摆
    /// （2026-10-27 踩过：mLaunchCounter 冻住、看着像钩子坏了），所以这里临时把定义的
    /// Nocturnal 关掉来量伤害，睡觉本身另有专项验过。
    /// 用法：icecheck &lt;seedId&gt; &lt;col&gt; &lt;row&gt; &lt;卡seedId或-1&gt;
    /// </summary>
    private static void RunIceCheck(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 4, "usage: icecheck <seedId> <col> <row> <cardSeedId>");
        SeedType seed = (SeedType)int.Parse(parts[1]);
        int col = int.Parse(parts[2]);
        int row = int.Parse(parts[3]);
        int cardId = parts.Length >= 5 ? int.Parse(parts[4]) : -1;
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Require(poisonRun == null && behave == null && watchPlant == null &&
            SourceCases.Count == 0, "另一轮还在跑");
        EnsureBoardRunning(app);

        CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
        Require(def != null, "种子 " + seed + " 不是模组植物");
        board.mSunMoney = 9000;
        Plant plant = PlantThroughCursor(board, seed, col, row);
        iceDef = def;
        iceNocturnal = def.Nocturnal;
        def.Nocturnal = false;
        plant.mIsAsleep = false;

        var notes = new List<string>();
        if (cardId > 0)
        {
            SeedType card = (SeedType)cardId;
            int freeCol = -1;
            int freeRow = -1;
            for (int r = 0; r < 6 && freeRow < 0; r++)
            {
                for (int c = 0; c < 9; c++)
                {
                    if (board.CanPlantAt(c, r, SeedType.Peashooter) == PlantingReason.Ok)
                    {
                        freeCol = c;
                        freeRow = r;
                        break;
                    }
                }
            }
            Require(freeRow >= 0, "找不到一个空格来试升级卡");
            notes.Add("空地=" + board.CanPlantAt(freeCol, freeRow, card));
            notes.Add("本株上=" + board.CanPlantAt(col, row, card));
            notes.Add("IsUpgradable=" + plant.IsUpgradableTo(card));
            notes.Add("IsUpgrade=" + Plant.IsUpgrade(card));
            notes.Add("卡名=" + (CustomPlantRegistry.GetDef(card)?.Name ?? "?"));

            // 真的按一次：走引擎自己的 MouseUpWithPlant（升级逻辑就在那个方法里），
            // 然后把那一格现在的种子读回来 —— 只判"许可"不判"落地的确是那株"不够。
            SeedBank bank = board.mSeedBank;
            int slot = -1;
            for (int i = 0; i < bank.mNumPackets; i++)
            {
                if (bank.mSeedPackets[i].mPacketType == card)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = 0;
                bank.mSeedPackets[slot].SetPacketType(card, SeedType.None);
            }
            bank.mSeedPackets[slot].mActive = true;
            bank.mSeedPackets[slot].mRefreshing = false;
            bank.mSeedPackets[slot].mRefreshCounter = 0;
            // 场上有基础株时这张紫卡该点得起来；升级完成后场上没有低级株了，就该点不起来
            // （原生紫卡同一规则，在 Board.PlantingRequirementsMet 里按种子写死）。
            bool pickWithBase = board.PlantingRequirementsMet(card) &&
                bank.mSeedPackets[slot].CanPickUp();
            notes.Add("有基础株可拿=" + pickWithBase);
            Require(pickWithBase, "场上有基础株时这张紫卡点不起来");
            CursorObject cursor = board.mCursorObject;
            cursor.mType = card;
            cursor.mImitaterType = SeedType.None;
            cursor.mCursorType = CursorType.PlantFromBank;
            cursor.mSeedBankIndex = slot;
            // 真的按一次：走引擎自己的按下+抬起（升级判定在 MouseUpWithPlant 里，
            // 但只调 MouseUpWithPlant 会被前面的入口校验挡住，实测原生那对也不认）。
            int clickX = board.GridToPixelX(col, row) + 20;
            int clickY = board.GridToPixelY(col, row) + 20;
            board.MouseDownInternal(clickX, clickY, 1, false);
            board.MouseUpInternal(clickX, clickY, 1, false);
            Plant after = board.GetTopPlantAt(col, row, TopPlant.OnlyNormalPosition);
            notes.Add("点完=" + (after == null ? "空" : after.mSeedType + "/" +
                (CustomPlantRegistry.GetDef(after.mSeedType)?.Name ?? "原生")) +
                " 老株没了=" + plant.mDead +
                // 光标还在不在 = 那一下到底被引擎吃掉了（走到升级分支）还是提前退了
                " 光标=" + cursor.mCursorType +
                " 阳光=" + board.mSunMoney +
                " 卡=" + bank.mSeedPackets[slot].mPacketType + "/" + cursor.mType);
            if (after != null && after.mSeedType == card)
            {
                // 基础株这轮是"醒着"种下的（上面 mIsAsleep = false），升级后的新株必须还醒着：
                // 引擎那份清醒状态搬运只认 seedTypeInCursor == SeedType.Gloomshroom。
                Require(!after.mIsAsleep, "升级后新株在睡觉：没继承基础株的清醒状态");
                bool pickWithoutBase = board.PlantingRequirementsMet(card) &&
                    bank.mSeedPackets[slot].CanPickUp();
                notes.Add("新株醒着=1 唤醒进度=" + after.mWakeUpCounter +
                    " 升级后没低级株可拿=" + pickWithoutBase);
                Require(!pickWithoutBase,
                    "场上已经没有能被它升级的株了，这张紫卡还点得起来");
                iceCardDef = CustomPlantRegistry.GetDef(card);
                plant = after;
            }
            else
            {
                iceCardDef = null;
            }
        }

        IceCases.Clear();
        icePlant = plant;
        foreach ((string label, int targetRow, int offset) in new[]
        {
            ("本行", row, 60),
            ("邻行", row == 0 ? 1 : row - 1, 60),
            ("远排", row == 5 ? 4 : 5, 60),
        })
        {
            Zombie zombie = board.AddZombieInRow(ZombieType.Door, targetRow, 0);
            Require(zombie != null && zombie.mShieldHealth > 0, label + ": 没有带门的靶子");
            zombie.mX = plant.mX + offset;
            zombie.mPosX = zombie.mX;
            zombie.mBodyHealth = 9000;
            zombie.mHelmHealth = 9000;
            zombie.mShieldHealth = 9000;
            IceCases.Add(new IceCase
            {
                Label = label,
                Target = zombie,
                HpStart = ZombieTotalHealth(zombie),
            });
        }
        iceNotes = string.Join(" ", notes);
        iceFrame = 0;
        iceAsleepSeen = false;
        iceHoldMax = 0;
        iceRateAtHold = 0f;
        icePinX = plant.mX;
        Volatile.Write(ref result, "ICECHECK+ armed " + def.Name + " 靶=" + IceCases.Count +
            " " + iceNotes);
        reportWritten = true;
    }

    private static void TickIceCheck(LawnApp app)
    {
        if (IceCases.Count == 0 || app?.mBoard == null)
        {
            return;
        }
        Board board = app.mBoard;
        Plant plant = icePlant;
        iceFrame++;
        if (plant != null && !plant.mDead)
        {
            // 临时关掉 Nocturnal 后，引擎下一帧才把 mIsAsleep 撤掉；记下有没有真被叫醒
            if (plant.mIsAsleep)
            {
                iceAsleepSeen = true;
                plant.mIsAsleep = false;
            }
            plant.mX = icePinX;
            // 开火那一拍读数：定格帧数峰值 + 那一刻身体动画的速率（引擎兜底分支写死 35）
            if (plant.mShootingCounter > iceHoldMax)
            {
                iceHoldMax = plant.mShootingCounter;
                Reanimation body = app.ReanimationTryToGet(plant.mBodyReanimID);
                iceRateAtHold = body == null ? 0f : body.mAnimRate;
            }
        }
        foreach (IceCase item in IceCases)
        {
            Zombie zombie = item.Target;
            if (zombie.mDead)
            {
                continue;
            }
            int hp = ZombieTotalHealth(zombie);
            if (hp < item.HpLast)
            {
                item.Hits++;
                item.Damage += item.HpLast - hp;
            }
            item.HpLast = hp;
            item.ChilledMax = Math.Max(item.ChilledMax, zombie.mChilledCounter);
        }
        if (iceFrame < 700)
        {
            if (iceFrame % 100 == 0)
            {
                Volatile.Write(ref result, "ICECHECK+ f=" + iceFrame +
                    " 远排=" + IceCases[IceCases.Count - 1].Damage);
            }
            return;
        }
        var line = new StringBuilder("ICECHECK f=" + iceFrame + " 株=" +
            (icePlant == null || icePlant.mDead ? "没了" : "在") +
            " 睡着过=" + iceAsleepSeen + " " +
            (iceDef == null ? "" : "伤害矩形="));
        if (icePlant != null && !icePlant.mDead)
        {
            TRect rect = icePlant.GetPlantAttackRect(PlantWeapon.Primary);
            line.Append("(" + rect.mX + "," + rect.mY + "," + rect.mWidth + "x" + rect.mHeight + ")");
        }
        foreach (IceCase item in IceCases)
        {
            line.Append(" " + item.Label + ":刀=" + item.Hits + " 伤=" + item.Damage +
                " 减速峰值=" + item.ChilledMax);
            item.Target.mPosX = Constants.WIDE_BOARD_WIDTH + 900;
            item.Target.mX = Constants.WIDE_BOARD_WIDTH + 900;
        }
        line.Append(" 升级判定 " + iceNotes);
        line.Append(" 定格=" + iceHoldMax + "@" + iceRateAtHold.ToString("0.##"));
        // Tick 里不抛异常（那会打在游戏的 Update 钩子上），核验结果写成一行读数，
        // 由外面的门按"声明核验=合格"判。
        if (iceCardDef != null)
        {
            bool holdOk = iceHoldMax >= iceCardDef.ShootingCounter;
            bool rateOk = iceCardDef.ShootingAnimRate <= 0f ||
                Math.Abs(iceRateAtHold - iceCardDef.ShootingAnimRate) < 0.01f;
            line.Append(holdOk && rateOk
                ? " 声明核验=合格"
                : " 声明核验=不合格(观测 " + iceHoldMax + "@" +
                    iceRateAtHold.ToString("0.##") + " 声明 " + iceCardDef.ShootingCounter +
                    "@" + iceCardDef.ShootingAnimRate.ToString("0.##") + ")");
        }
        if (iceDef != null)
        {
            iceDef.Nocturnal = iceNocturnal;
        }
        IceCases.Clear();
        icePlant = null;
        iceDef = null;
        iceCardDef = null;
        icePinX = 0;
        Volatile.Write(ref result, line.ToString());
    }

    private static void TickPoisonRun(LawnApp app)
    {
        PoisonRun run = poisonRun;
        Board board = app == null ? null : app.mBoard;
        if (run == null || board == null)
        {
            return;
        }
        Zombie target = run.Target;
        run.Frame++;
        // 引擎自己按 mMainCounter 推进度：这一拍它没走（暂停/结算），就别把"没掉血"算在毒头上。
        int counter = board.mMainCounter;
        if (counter != run.LastCounter)
        {
            run.BoardFrames++;
        }
        else
        {
            run.PausedFrames++;
        }
        run.LastCounter = counter;
        if (!run.Killed)
        {
            // 引擎每帧按 mPosX 重算 mX 和矩形，只写 mX 会被顶回去：
            // 实测探针僵尸一直停在草坪右侧外，那里 EffectedByDamage 恒为假，
            // 毒的倒计时照走却一刀不落（计数里表现成"挡"而不是"刀"）。
            target.mPosX = run.PinX;
            target.mX = run.PinX;
            run.Blocked.mPosX = run.BlockedX;
            run.Blocked.mX = run.BlockedX;
            int body = target.mBodyHealth;
            int door = target.mShieldHealth;
            if (body < run.PrevBody)
            {
                int drop = run.PrevBody - body;
                if (drop >= run.Spec.FixedDamage)
                {
                    run.Hits.Add("f" + run.Frame + "体-" + drop);
                }
                else
                {
                    // 这一局自带的环境伤害（关卡自己会掉 1 点那种），不能算成毒刀
                    run.AmbientDamage += drop;
                }
            }
            if (door < run.PrevDoor)
            {
                run.ShieldHits++;
            }
            run.PrevBody = body;
            run.PrevDoor = door;
        }
        if (run.Frame == 55)
        {
            CustomCombat.ApplyPoison(target, run.Spec);
        }
        if (run.Frame == 60)
        {
            // 第 60 帧的账本快照：把"当帧就被收走"和"挂着但没人步进"分开
            run.LedgerAt60 = CustomCombat.HasPoison(target) ? 1 : 0;
            run.At60Keys = CustomCombat.PoisonedZombieCount;
            run.At60Records = CustomCombat.PoisonRecordCount;
            run.At60Hits = CustomCombat.PoisonHitsApplied;
            run.At60Blocked = CustomCombat.PoisonGateBlocked;
            run.At60Driver = CustomCombat.PoisonDriverCalls;
        }
        if (run.Frame == 320)
        {
            target.TakeDamage(CustomCombat.ZombieEffectiveHealth(target) + 1, 16u);
            run.Killed = true;
        }
        if (run.Frame == 330)
        {
            run.LedgerAfterKill = CustomCombat.HasPoison(target);
        }
        if (run.Frame == 340)
        {
            poisonRun = null;
            Zombie blocked = run.Blocked;
            run.LedgerAtEnd = CustomCombat.HasPoison(target);
            run.ZombieUpdatesAtEnd = zombieUpdateCalls;
            run.ZombieEndpointAtEnd = zombieEndpointCalls;
            run.DriverAtEnd = CustomCombat.PoisonDriverCalls;
            // 收工前把这批测试僵尸挪回草坪右侧外：留在场内它们会继续往房子走，
            // 没人防守的局就会被自己的探针判输（实测把整局冻成 GameOver）。
            target.mPosX = Constants.WIDE_BOARD_WIDTH + 600;
            target.mX = Constants.WIDE_BOARD_WIDTH + 600;
            blocked.mPosX = Constants.WIDE_BOARD_WIDTH + 600;
            blocked.mX = Constants.WIDE_BOARD_WIDTH + 600;
            Volatile.Write(ref result,
                "POISON f=" + run.Frame + " 刀数=" + run.Hits.Count +
                " [" + string.Join(",", run.Hits) + "]" +
                " 本体-" + (run.BodyStart - run.PrevBody) +
                " 门被扣=" + run.ShieldHits +
                " 场外门" + (run.BlockedDoorStart - blocked.mShieldHealth) +
                "/" + (run.BlockedBodyStart - blocked.mBodyHealth) +
                " 显色=" + run.TintAtStart +
                " 挂上=" + run.PoisonedAtStart +
                " 60帧还在=" + run.LedgerAt60 + " 收工还在=" + run.LedgerAtEnd +
                " 僵尸Update=" + (run.ZombieUpdatesAtEnd - run.ZombieUpdatesAtArm) + " 次" +
                " HookEndpoint=" + (run.ZombieEndpointAtEnd - run.ZombieEndpointAtArm) + " 次" +
                " 毒驱动=" + (run.DriverAtEnd - run.DriverAtArm) + " 拍" +
                " 60帧时[键=" + run.At60Keys + " 条=" + run.At60Records + " 刀=" + run.At60Hits +
                " 挡=" + run.At60Blocked + " 驱动=" + run.At60Driver + "]" +
                " 全程刀=" + CustomCombat.PoisonHitsApplied + " 挡=" + CustomCombat.PoisonGateBlocked +
                " 杂伤-" + run.AmbientDamage +
                " 引擎走过=" + run.BoardFrames + "/冻住" + run.PausedFrames + " 拍" +
                " kit=" + KitCopies() +
                " 杀后进池账本=" + (run.LedgerAfterKill ? "残留" : "已清"));
        }
        else if (run.Frame % 40 == 0)
        {
            Volatile.Write(ref result, "POISON+ f=" + run.Frame +
                " 刀数=" + run.Hits.Count + " 本体=" + target.mBodyHealth);
        }
    }

    // ---------------------------------------------------------------- 毒的掩码继承
    private sealed class SourceCase
    {
        public string Name;
        public Zombie Target;
        public int BodyStart;
        public int DoorStart;
    }

    private static readonly List<SourceCase> SourceCases = new List<SourceCase>();
    private static int sourceFrame;

    // ---------------------------------------------------------------- 冰系喷菇探针
    private sealed class IceCase
    {
        public string Label;
        public Zombie Target;
        public int HpStart;
        public int HpLast;
        public int Damage;
        public int Hits;
        public int ChilledMax;
    }

    private static readonly List<IceCase> IceCases = new List<IceCase>();
    private static Plant icePlant;
    private static CustomPlantDef iceDef;
    private static bool iceNocturnal;
    private static bool iceAsleepSeen;
    private static int icePinX;
    private static int iceFrame;
    private static string iceNotes = "";
    // 这轮真正升级上去的那张卡 + 开火定格的观测值（帧数峰值、那一刻身体动画的速率）
    private static CustomPlantDef iceCardDef;
    private static int iceHoldMax;
    private static float iceRateAtHold;

    /// <summary>
    /// "毒的两套掩码默认跟着挂上它的那发子弹"的分向探针。三只屏幕门僵尸、两发弹、一条空白 spec：
    ///   followGround  —— spec 不写掩码 + 豌豆弹（范围=地面）：应挨刀，且门吃掉（继承 TakeFlags=0）
    ///   followFlying  —— 同一条空白 spec + 范围写成"只打空中"的弹：地面僵尸一刀都不该掉
    ///   teitisi       —— 水神那条显式写了 9/9 的 spec + 同一发"只打空中"的弹：仍该掉血且穿盾
    /// 第二、三只差的就是"spec 写没写"，所以这一组同时证明继承生效和显写豁免。
    /// 用法：poisrc &lt;col&gt; &lt;row&gt;
    /// </summary>
    private static void RunPoisonSource(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 3, "usage: poisrc <col> <row>");
        int col = int.Parse(parts[1]);
        int row = int.Parse(parts[2]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Require(poisonRun == null && behave == null && watchPlant == null, "另一轮还在跑");
        EnsureBoardRunning(app);

        // 载体植物种到别的泳道：它只是给 Fire 提供 renderOrder 和范围掩码，
        // 种在同一行会真的朝靶子开枪，把门上的读数掺进毒的伤害。
        int plantRow = -1;
        for (int candidate = 0; candidate < 6; candidate++)
        {
            if (candidate != row &&
                board.CanPlantAt(col, candidate, SeedType.Peashooter) == PlantingReason.Ok)
            {
                plantRow = candidate;
                break;
            }
        }
        Require(plantRow >= 0, "找不到一行放载体豌豆射手");
        Plant plant = PlantThroughCursor(board, SeedType.Peashooter, col, plantRow);
        CustomCombat.PoisonSpec inherit = new CustomCombat.PoisonSpec
        {
            Name = "verify.inherit",
            FixedDamage = 100,
            Duration = 300,
            Interval = 100,
            FirstTick = 5,
            Tint = false,
        };
        CustomCombat.PoisonSpec explicitSpec = TeiTisiPoisonSpec();
        Require(explicitSpec != null, "反射读不到水神的毒定义");

        Projectile groundShot = CustomBullets.Fire(board, plant, "verify.unregistered",
            (int)plant.mX - 400, (int)plant.mY, row);
        Projectile flyingShot = CustomBullets.Fire(board, plant, "verify.unregistered",
            (int)plant.mX - 400, (int)plant.mY, row, null, 2u);
        Require(flyingShot.mDamageRangeFlags == 2 && groundShot.mDamageRangeFlags == 1,
            "两发弹的范围掩码没分开（地面=" + groundShot.mDamageRangeFlags +
            " 空中=" + flyingShot.mDamageRangeFlags + "）");

        SourceCases.Clear();
        sourceFrame = 0;
        foreach ((string name, CustomCombat.PoisonSpec spec, Projectile source) in
            new[]
            {
                ("followGround", inherit, groundShot),
                ("followFlying", inherit, flyingShot),
                ("teitisi", explicitSpec, flyingShot),
            })
        {
            Zombie zombie = board.AddZombieInRow(ZombieType.Door, row, 0);
            Require(zombie != null && zombie.mShieldHealth > 0, name + ": 没有带门的靶子");
            zombie.mX = board.GridToPixelX(5, row);
            zombie.mPosX = zombie.mX;
            zombie.mBodyHealth = 9000;
            CustomCombat.ApplyPoison(zombie, spec, source);
            SourceCases.Add(new SourceCase
            {
                Name = name,
                Target = zombie,
                BodyStart = zombie.mBodyHealth,
                DoorStart = zombie.mShieldHealth,
            });
        }
        // 弹体立刻收掉：它们只是"来源"，留着会被原生碰撞再打一手，把读数搅浑
        groundShot.Die();
        flyingShot.Die();
        Volatile.Write(ref result, "POISRC+ armed 三只靶子已挂毒 刀=" +
            CustomCombat.PoisonHitsApplied + " 挡=" + CustomCombat.PoisonGateBlocked);
        reportWritten = true;
    }

    private static void TickPoisonSource(LawnApp app)
    {
        if (SourceCases.Count == 0)
        {
            return;
        }
        Board board = app == null ? null : app.mBoard;
        if (board == null)
        {
            SourceCases.Clear();
            return;
        }
        sourceFrame++;
        foreach (SourceCase item in SourceCases)
        {
            // 引擎每帧按 mPosX 重算 mX，钉位置要两个都写（否则靶子会走到右侧外被门槛吞掉）
            int pin = board.GridToPixelX(5, item.Target.mRow);
            item.Target.mPosX = pin;
            item.Target.mX = pin;
        }
        if (sourceFrame < 220)
        {
            return;
        }
        var line = new StringBuilder("POISRC f=" + sourceFrame +
            " 刀=" + CustomCombat.PoisonHitsApplied + " 挡=" + CustomCombat.PoisonGateBlocked);
        foreach (SourceCase item in SourceCases)
        {
            line.Append(" " + item.Name + ":门-" + (item.DoorStart - item.Target.mShieldHealth) +
                " 体-" + (item.BodyStart - item.Target.mBodyHealth));
            // 收工前挪回草坪右侧外：这三只没钉住，留在场上会走进房子把这局判输，
            // 下一把探针就量到一局冻住的棋盘（实测第二个 op 只跑到 45 帧就停）。
            item.Target.mPosX = Constants.WIDE_BOARD_WIDTH + 600;
            item.Target.mX = Constants.WIDE_BOARD_WIDTH + 600;
        }
        SourceCases.Clear();
        Volatile.Write(ref result, line.ToString());
    }

    /// <summary>
    /// 子弹两套 uint 掩码的分向探针。一发注册弹打同一只屏幕门僵尸，四种写法各来一发：
    ///   take0  —— 都不写：应照原生豌豆（门吃掉 40，本体不动）
    ///   take1  —— DamageFlags=1 穿盾：本体 -40，门不动
    ///   take2  —— DamageFlags=2 盾体同扣：门 -40 且本体 -40
    ///   range2 —— 只写 DamageRangeFlags=2（只打得着空中）：地面门僵尸应完全不掉血
    /// 最后一发同时证明"范围掩码只用来筛人，不会漏给 TakeDamage 当伤害标记"：
    /// 合并成一套的老写法在这里会掉 40 血（而且走的是穿盾）。
    /// 第五发是火神那颗强化弹的形状（40 直伤 + 同排溅射 20，两套掩码都不写），
    /// 溅射的那只邻居也必须"门吃掉、本体不动"。
    /// 用法：flagcheck &lt;col&gt; &lt;row&gt;
    /// </summary>
    private static void RunFlagCheck(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 3, "usage: flagcheck <col> <row>");
        int col = int.Parse(parts[1]);
        int row = int.Parse(parts[2]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        board.mSunMoney = 9000;
        Plant plant = PlantThroughCursor(board, SeedType.Peashooter, col, row);

        (string Label, uint Take, uint Range, int Splash)[] cases =
        {
            ("take0", 0u, 0u, 0),
            ("take1", 1u, 0u, 0),
            ("take2", 2u, 0u, 0),
            ("range2", 0u, 2u, 0),
            // 火神那颗强化弹的真实形状：直伤 40 + 同排溅射 20，两套掩码都不写。
            ("splash", 0u, 0u, 20),
        };
        var line = new StringBuilder("FLAGCHECK pea#" + plant.mPlantCol + "," + plant.mRow);
        foreach ((string label, uint take, uint range, int splash) in cases)
        {
            Zombie zombie = board.AddZombieInRow(ZombieType.Door, row, 0);
            Require(zombie != null, "AddZombieInRow failed");
            Require(zombie.mShieldHealth > 0, label + ": 刷出来的不是带门的僵尸，盾分支没被测到");
            PinZombie(zombie, plant.mX + 100);
            Zombie neighbour = null;
            if (splash > 0)
            {
                neighbour = board.AddZombieInRow(ZombieType.Door, row, 0);
                Require(neighbour != null && neighbour.mShieldHealth > 0,
                    label + ": 溅射靶子没带门");
                PinZombie(neighbour, zombie.mX);
            }
            string id = "verify.flag:" + label;
            CustomBullets.Register(new CustomBulletDef
            {
                Id = id,
                Carrier = ProjectileType.Pea,
                Damage = 40,
                DamageFlags = take,
                DamageRangeFlags = range,
                SplashDamage = splash,
                SplashWidth = 100,
            });
            Projectile shot = CustomBullets.Fire(board, plant, id,
                (int)zombie.mX - 20, (int)plant.mY, row);
            Require(CustomBullets.Of(shot) != null, label + ": 弹体没挂上注册定义");
            int shieldBefore = zombie.mShieldHealth;
            int bodyBefore = zombie.mBodyHealth;
            int neighbourDoor = neighbour == null ? 0 : neighbour.mShieldHealth;
            int neighbourBody = neighbour == null ? 0 : neighbour.mBodyHealth;
            CustomProjectiles.OnProjectileImpact((s, z) => { }, shot, zombie);
            line.Append($" {label}: door-{shieldBefore - zombie.mShieldHealth}" +
                $" body-{bodyBefore - zombie.mBodyHealth}");
            if (neighbour != null)
            {
                line.Append($" 邻居 door-{neighbourDoor - neighbour.mShieldHealth}" +
                    $" body-{neighbourBody - neighbour.mBodyHealth}");
            }
        }
        // ---- 穿透 / 命中挂毒 / 直线速度：三个声明字段各要一条能反着读的证
        CustomBullets.Register(new CustomBulletDef
        {
            Id = "verify.flag:pierce", Carrier = ProjectileType.Pea, Damage = 40, Pierce = 2,
        });
        Zombie first = board.AddZombieInRow(ZombieType.Door, row, 0);
        Zombie second = board.AddZombieInRow(ZombieType.Door, row, 0);
        Zombie third = board.AddZombieInRow(ZombieType.Door, row, 0);
        Require(first != null && second != null && third != null, "穿透的三个靶子没刷出来");
        // 刷出来都在草坪右侧外 —— 那里 EffectedByDamage 恒为假，一口都不落（踩过两次）。
        PinZombie(first, plant.mX + 100);
        PinZombie(second, plant.mX + 150);
        PinZombie(third, plant.mX + 200);
        Require(first.EffectedByDamage(1), "靶子还在场外，穿透这组量不到东西");
        Projectile through = CustomBullets.Fire(board, plant, "verify.flag:pierce",
            (int)first.mX - 20, (int)plant.mY, row);
        int firstStart = first.mShieldHealth;
        CustomProjectiles.OnProjectileImpact((s, z) => { }, through, first);
        int firstHit = firstStart - first.mShieldHealth;
        CustomProjectiles.OnProjectileImpact((s, z) => { }, through, first);
        int firstAgain = firstStart - first.mShieldHealth - firstHit;
        int secondStart = second.mShieldHealth;
        CustomProjectiles.OnProjectileImpact((s, z) => { }, through, second);
        int secondHit = secondStart - second.mShieldHealth;
        bool flyingAfterTwo = !through.mDead;
        int thirdStart = third.mShieldHealth;
        CustomProjectiles.OnProjectileImpact((s, z) => { }, through, third);
        int thirdHit = thirdStart - third.mShieldHealth;
        line.Append($" 穿透: 第一只-{firstHit} 重复打同一只-{firstAgain} 第二只-{secondHit}" +
            $" 穿两只后还在飞={flyingAfterTwo} 额度用完={through.mDead}");
        // 先把读数落进 result 再断言：FAIL 会盖掉整行，没数字的失败只能靠猜。
        Volatile.Write(ref result, line.ToString());
        reportWritten = true;
        Require(first != null && !first.mDead && second != null && third != null,
            "穿透靶子没站住");
        Require(firstHit == 40 && secondHit == 40 && thirdHit == 40,
            "穿透弹的某一口没结算: A-" + firstHit + " B-" + secondHit + " C-" + thirdHit +
            " 弹=" + (through == null ? "null" : through.mProjectileType.ToString()));
        Require(firstAgain == 0, "同一只僵尸吃了第二口穿透弹（多扣 " + firstAgain + "）");
        Require(flyingAfterTwo, "还有穿透额度时弹提前没了");
        Require(through.mDead, "穿透额度用完、又咬了第三只，弹还没走");

        var bulletPoison = new CustomCombat.PoisonSpec
        {
            Name = "verify.flag.poison", FixedDamage = 100, Duration = 600,
            Interval = 100, FirstTick = 50,
        };
        CustomBullets.Register(new CustomBulletDef
        {
            Id = "verify.flag:poisoned", Carrier = ProjectileType.Pea, Damage = 40,
            Poison = bulletPoison,
        });
        Zombie poisoned = board.AddZombieInRow(ZombieType.Door, row, 0);
        Require(poisoned != null, "挂毒靶子没刷出来");
        PinZombie(poisoned, plant.mX + 120);
        int recordsBefore = CustomCombat.PoisonRecordCount;
        Projectile poisonedShot = CustomBullets.Fire(board, plant, "verify.flag:poisoned",
            (int)poisoned.mX - 20, (int)plant.mY, row);
        CustomProjectiles.OnProjectileImpact((s, z) => { }, poisonedShot, poisoned);
        int recordsAfter = CustomCombat.PoisonRecordCount;
        line.Append($" 挂毒: 台账记录 {recordsBefore}->{recordsAfter}");
        Require(recordsAfter == recordsBefore + 1,
            "子弹带的毒没挂上（记录数 " + recordsBefore + "->" + recordsAfter + "）");

        CustomBullets.Register(new CustomBulletDef
        {
            Id = "verify.flag:fast", Carrier = ProjectileType.Pea, Damage = 40, Speed = 10f,
        });
        Projectile fast = CustomBullets.Fire(board, plant, "verify.flag:fast",
            plant.mX + 60, (int)plant.mY, row);
        Projectile plain = CustomBullets.Fire(board, plant, "verify.flag:take0",
            plant.mX + 60, (int)plant.mY, row);
        float fastStart = fast.mPosX;
        float plainStart = plain.mPosX;
        fast.Update();
        plain.Update();
        float fastStep = fast.mPosX - fastStart;
        float plainStep = plain.mPosX - plainStart;
        line.Append($" 速度: 声明10 实测={fastStep.ToString("0.##")}" +
            $" 对照(没声明)={plainStep.ToString("0.##")}");
        Require(Math.Abs(fastStep - 10f) < 0.01f,
            "Speed=10 的直线弹这一帧走了 " + fastStep.ToString("0.###") + " 像素");
        Require(Math.Abs(plainStep - 3.33f) < 0.01f,
            "对照弹（没声明速度）本该走引擎写死的 3.33，实测 " + plainStep.ToString("0.###"));
        Volatile.Write(ref result, line.ToString());
        reportWritten = true;
    }

    /// <summary>测试用：在指定行刷一只指定类型的僵尸。用法：zombie &lt;ZombieType&gt; &lt;row&gt;</summary>
    private static void RunSpawnZombie(LawnApp app, string operation)
    {
        string[] parts = operation.Split(' ');
        Require(parts.Length >= 3, "usage: zombie <ZombieType> <row>");
        ZombieType type = (ZombieType)Enum.Parse(typeof(ZombieType), parts[1]);
        int row = int.Parse(parts[2]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        Zombie zombie = board.AddZombieInRow(type, row, 0);
        Require(zombie != null, "AddZombieInRow failed");
    }

    /// <summary>测试用：把种子放进光标并停到指定格中心。用法：cursor &lt;seedId&gt; &lt;col&gt; &lt;row&gt;</summary>
    private static void RunCursorPreview(LawnApp app, string operation)
    {
        string[] parts = operation.Split(new[] { ' ' }, 4);
        Require(parts.Length >= 4, "usage: cursor <seedId> <col> <row>");
        SeedType seed = (SeedType)int.Parse(parts[1]);
        int col = int.Parse(parts[2]);
        int row = int.Parse(parts[3]);
        Board board = app.mBoard;
        Require(app.mGameScene == GameScenes.Playing, "not playing yet");
        SeedBank bank = board.mSeedBank;
        int index = -1;
        for (int i = 0; i < bank.mNumPackets; i++)
        {
            if (bank.mSeedPackets[i].mPacketType == seed)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
        {
            index = 0;
            bank.mSeedPackets[index].SetPacketType(seed, SeedType.None);
            bank.mSeedPackets[index].mActive = true;
            bank.mSeedPackets[index].mRefreshing = false;
            bank.mSeedPackets[index].mRefreshCounter = 0;
        }
        CursorObject cursor = board.mCursorObject;
        cursor.mType = seed;
        cursor.mImitaterType = SeedType.None;
        cursor.mCursorType = CursorType.PlantFromBank;
        cursor.mSeedBankIndex = index;
        app.mWidgetManager.mMouseIn = true;
        board.mIgnoreMouseUp = false;
        board.mLastToolX = board.GridToPixelX(col, row) + 40;
        board.mLastToolY = board.GridToPixelY(col, row) + 42;
    }

    private static void ArmWatch(Plant planted, SeedType seed, int frames)
    {
        CustomPlantDef def = CustomPlantRegistry.GetDef(seed);
        InstallCombatCounters();
        FireShots.Clear();
        CobShots.Clear();
        watchPlant = planted;
        watchName = def == null ? seed.ToString() : def.Name;
        watchCol = planted.mPlantCol;
        watchRow = planted.mRow;
        watchFramesLeft = frames;
        watchAnimStart = BodyAnimTime(planted);
        watchZombieHpStart = TotalZombieHp(planted.mBoard);
    }

    private static float BodyAnimTime(Plant plant)
    {
        LawnApp app = GlobalStaticVars.gLawnApp;
        Reanimation body = app == null || plant == null
            ? null
            : app.ReanimationTryToGet(plant.mBodyReanimID);
        return body == null ? -1f : body.mAnimTime;
    }

    private static int watchZombieHpStart;
    private static float watchAnimStart;

    private static void SpawnWatchZombie(Board board, int row)
    {
        watchZombie = board.AddZombieInRow(ZombieType.Gargantuar, row, 0);
        watchZombieHpStart = watchZombie == null ? -1
            : watchZombie.mBodyHealth + watchZombie.mHelmHealth + watchZombie.mShieldHealth;
    }

    private static Zombie watchZombie;

    private static void InstallCombatCounters()
    {
        fireHook ??= new Hook(
            typeof(Plant).GetMethod("Fire", new[] { typeof(Zombie), typeof(int), typeof(PlantWeapon) }),
            (Action<Action<Plant, Zombie, int, PlantWeapon>, Plant, Zombie, int, PlantWeapon>)CountFire);
        cobHook ??= new Hook(
            typeof(Plant).GetMethod("CobCannonFire", new[] { typeof(int), typeof(int) }),
            (Action<Action<Plant, int, int>, Plant, int, int>)CountCob);
    }

    private static void CountFire(Action<Plant, Zombie, int, PlantWeapon> orig,
        Plant self, Zombie target, int row, PlantWeapon weapon)
    {
        orig(self, target, row, weapon);
        FireShots[self] = (FireShots.TryGetValue(self, out int count) ? count : 0) + 1;
    }

    private static void CountCob(Action<Plant, int, int> orig, Plant self, int x, int y)
    {
        orig(self, x, y);
        CobShots[self] = (CobShots.TryGetValue(self, out int count) ? count : 0) + 1;
    }

    private static int TotalZombieHp(Board board)
    {
        int total = 0;
        foreach (Zombie zombie in board.mZombies)
        {
            total += zombie.mBodyHealth + zombie.mHelmHealth + zombie.mShieldHealth;
        }
        return total;
    }

    private static string BuildBehaveReport(Board board, BehaveRun run)
    {
        var lines = new List<string>
        {
            "BEHAVE frames=" + run.Frames +
            " zombies=" + run.ZombiesStart + "->" + board.mZombies.Count +
            " hpDrop=" + (run.ZombieHpStart - TotalZombieHp(board)) +
            " maxProj=" + run.MaxProjectiles +
            " sun=" + (board.mSunMoney - run.SunStart),
        };
        for (int i = 0; i < run.Plants.Count; i++)
        {
            Plant plant = run.Plants[i];
            bool listed = plant != null && board.mPlants.Contains(plant);
            lines.Add(string.Format(
                "  {0} at=({1},{2}) listed={3} dead={4} squished={5} hp={6} fire={7} cob={8} stateCd={9}",
                run.Defs[i].Name,
                plant == null ? -1 : plant.mPlantCol, plant == null ? -1 : plant.mRow,
                listed,
                plant != null && plant.mDead, plant != null && plant.mSquished,
                plant == null ? -1 : plant.mPlantHealth,
                FireShots.TryGetValue(plant, out int fire) ? fire : 0,
                CobShots.TryGetValue(plant, out int cob) ? cob : 0,
                plant == null ? -1 : plant.mStateCountdown));
        }
        return string.Join("\n", lines);
    }

    private static object GetField(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(obj);
    private static void SetField(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(obj, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    /// <summary>
    /// 会读伤害/放僵尸/看卡面状态的那批 op 必须在"活局"上跑。
    /// 判负后引擎只是把 <c>mGameScene</c> 留在 Playing 并弹一个结算框，
    /// 而 <c>Board.Update</c> 有框就直接早退 —— 此时读到的数全是死局上的假象。
    /// </summary>
    private static bool NeedsLiveBoard(string operation)
    {
        string[] live =
        {
            "plantreal", "plantadd", "hitcheck", "flagcheck", "poison ", "poisrc ", "scorch ",
            "icecheck ", "zombie ", "behave", "cheatgray ", "upgradegate ", "cursor ",
            "packetcmp", "summerlive",
        };
        return live.Any(prefix => operation.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static void RequireLiveBoard(LawnApp app, string operation)
    {
        Board board = app.mBoard;
        int dialogs = app.GetDialogCount();
        int fade = board == null ? -99 : board.mBoardFadeOutCounter;
        Require(app.mGameScene == GameScenes.Playing && board != null && dialogs == 0 && fade <= 0,
            $"{operation} 要活局：scene={app.mGameScene} board={(board == null ? "null" : "ok")} " +
            $"弹窗={dialogs} fade={fade} —— 先 prepare/choose/play 重开一局再来");
    }

    /// <summary>
    /// 把僵尸摆到指定横坐标。引擎每帧按 <c>mPosX</c> 重算 <c>mX</c> 和矩形，
    /// 只写一个会被顶回去；而停在草坪右侧外的僵尸 <c>EffectedByDamage</c> 恒为假，
    /// 伤害一口都不落，量出来像"结算坏了"。（这两个坑各骗过我一轮。）
    /// </summary>
    private static void PinZombie(Zombie zombie, int x)
    {
        zombie.mPosX = x;
        zombie.mX = x;
    }
}
