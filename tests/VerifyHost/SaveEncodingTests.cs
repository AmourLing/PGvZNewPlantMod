using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Lawn;
using PGvZPlantKit;
using Sexy;
using Sexy.TodLib;

public static class SaveEncodingTests
{
    private const BindingFlags Instance = BindingFlags.Instance |
        BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Type ContextType = typeof(SeedType).Assembly.GetType(
        "Lawn.GlobalMembersSaveGame+SaveGameContext", true);
    private static readonly Type EncodingType = typeof(CustomPlantRegistry).Assembly.GetType(
        "PGvZPlantKit.CustomSaveEncoding", true);

    public static bool Run()
    {
        // 下面会故意造"存档引用已删除条目"的用例，那条 Error 日志这时候压掉，
        // 否则打包输出里会看着像真的失败。
        bool previous = CustomSave.QuietMissingEntries;
        CustomSave.QuietMissingEntries = true;
        try
        {
            return RunCore();
        }
        finally
        {
            CustomSave.QuietMissingEntries = previous;
        }
    }

    private static bool RunCore()
    {
        CustomPlantDef[] defs = CustomPlantRegistry.Defs.ToArray();
        CustomPlantDef[] animations = defs.Where(d => d.UsesCustomAnimation).ToArray();
        Check(defs.Length > 0 && animations.Length > 0, "custom definitions must be loaded");
        Check(defs.All(d => d.SeedId > 0) && animations.All(d => d.ReanimSlot >= 0),
            "call EnsureDefinitions before SaveEncodingTests.Run");

        var seedName = Mapper<SeedType, string>("SeedName");
        var parseSeed = Mapper<string, SeedType>("ParseSeed");
        var animationName = Mapper<ReanimationType, string>("AnimationName");
        var parseAnimation = Mapper<string, ReanimationType>("ParseAnimation");
        foreach (CustomPlantDef def in defs)
        {
            string key = "pgvz-plant:" + def.PackId + ":" + def.Name;
            Check(seedName(def.Seed) == key && parseSeed(key) == def.Seed,
                "stable plant identity: " + key);
        }
        foreach (CustomPlantDef def in animations)
        {
            string asset = !string.IsNullOrEmpty(def.SpineName) ? def.SpineName : def.ReanimName;
            string key = "pgvz-animation:" + def.PackId + ":" + asset;
            Check(animationName(def.ReanimType) == key && parseAnimation(key) == def.ReanimType,
                "stable animation identity: " + key);
        }

        ZombieType zombie = Enum.GetValues<ZombieType>().First(z => (int)z >= 0);
        SeedType[] nativeSeeds =
        {
            SeedType.Peashooter, SeedType.None,
            (SeedType)((int)SeedType.ZombieCardFlag | (int)zombie),
            (SeedType)((int)SeedType.MindControlledCardFlag | (int)zombie),
        };
        foreach (SeedType seed in nativeSeeds)
        {
            Check(parseSeed(seedName(seed)) == seed, "native metadata seed: " + seed);
        }
        Check(seedName(SeedType.None) == "None" && parseSeed("-1") == SeedType.None,
            "None metadata must not become Peashooter or a zombie card");
        Check(parseSeed("unknown-native-seed") == SeedType.None,
            "unknown native metadata seed fallback");
        Check(parseAnimation("unknown-native-animation") == ReanimationType.None,
            "unknown native metadata animation fallback");
        Check(parseAnimation(animationName(ReanimationType.Peashooter)) == ReanimationType.Peashooter &&
            parseAnimation(animationName(ReanimationType.None)) == ReanimationType.None,
            "native animation metadata");

        SeedType[] seeds = defs.Select(d => d.Seed).Distinct().ToArray();
        ReanimationType[] reanims = animations.Select(d => d.ReanimType).Distinct().ToArray();
        Check(animations.Any(d => d.ReanimType == ReanimationType.NumReanims),
            "fixture must include the NumReanims alias");
        Check(animationName(ReanimationType.NumReanims).StartsWith("pgvz-animation:",
            StringComparison.Ordinal), "custom animation must win over NumReanims");

        RoundTrip("aSeedTypeSaver", SeedType.None, seeds, nativeSeeds, seedName);
        RoundTrip("aReanimationTypeSaver", ReanimationType.None, reanims,
            new[] { ReanimationType.Peashooter, ReanimationType.None }, animationName);
        ReorderedTable("aSeedTypeSaver", SeedType.None, SeedType.Peashooter,
            seeds, seedName, parseSeed);
        ReorderedTable("aReanimationTypeSaver", ReanimationType.None, ReanimationType.Peashooter,
            reanims, animationName, parseAnimation);

        string missing = "__save_encoding_missing_" + Guid.NewGuid().ToString("N");
        foreach (string key in new[]
        {
            "pgvz-plant:" + missing + ":plant",
            "pgvz-plant:" + defs[0].PackId + ":" + missing,
        })
        {
            InvalidData(() => parseSeed(key), "missing plant metadata");
            MissingTableKeyFallsBack("aSeedTypeSaver", seeds[0], SeedType.Wallnut,
                seedName, key);
        }
        foreach (string key in new[]
        {
            "pgvz-animation:" + missing + ":animation",
            "pgvz-animation:" + animations[0].PackId + ":" + missing,
        })
        {
            InvalidData(() => parseAnimation(key), "missing animation metadata");
            MissingTableKeyFallsBack("aReanimationTypeSaver", reanims[0],
                ReanimationType.Peashooter, animationName, key);
        }
        InvalidData(() => parseSeed(animationName(reanims[0])), "wrong custom key kind");
        InvalidData(() => parseAnimation(seedName(seeds[0])), "wrong custom key kind");
        ImageWire(missing);
        Console.WriteLine("SaveEncoding PASS: seeds=" + seeds.Length + ", animations=" +
            reanims.Length + "; native/None, repeat buffer, reordered IDs, missing keys, image wire");
        return true;
    }

    private static void RoundTrip<T>(string field, T fallback, T[] custom, T[] native,
        Func<T, string> name) where T : struct, Enum
    {
        using var stream = new MemoryStream();
        var buffer = new BufferNew(stream);
        object writer = Context(buffer, false);
        object saver = ContextType.GetField(field, Instance).GetValue(writer);
        Type saverType = SaverType<T>();
        MethodInfo toIndex = Method(saverType, "Convert", typeof(T));
        MethodInfo toName = Method(saverType, "ConvertName", typeof(T));
        MethodInfo sync = Method(saverType, "Sync", ContextType);
        MethodInfo syncEnum = ContextType.GetMethods(Instance)
            .Single(m => m.Name == "SyncEnum" && m.IsGenericMethodDefinition)
            .MakeGenericMethod(typeof(T));
        Call(Method(saverType, "RegistAllByDefault"), saver);
        int unregistered = 0;
        foreach (T value in custom)
        {
            int index = (int)Call(toIndex, saver, value);
            if (!Enum.IsDefined(typeof(T), value))
            {
                Check(index == -1, "unextended EnumSaver.Convert must return -1: " + value);
                unregistered++;
            }
        }
        Check(unregistered > 0 || typeof(T) == typeof(ReanimationType),
            "fixture must prove the unregistered-custom -1 case");
        string[] nativeNames = native.Select(v => (string)Call(toName, saver, v)).ToArray();
        Call(sync, saver, writer);
        long tableEnd = stream.Position;
        T[] values = native.Concat(custom).ToArray();
        foreach (T value in values)
        {
            Check((int)Call(toIndex, saver, value) >= 0, "registered wire index: " + value);
            Call(syncEnum, writer, value, saver, SaveFileVersion.Initial, fallback);
        }
        byte[] original = stream.ToArray();
        stream.Position = 0;
        int count = buffer.ReadInt32LE();
        string[] names = new string[count];
        for (int i = 0; i < count; i++)
        {
            names[i] = buffer.ReadString();
        }
        Check(stream.Position == tableEnd, "enum table boundary");
        foreach (T value in custom)
        {
            Check(names[(int)Call(toIndex, saver, value)] == name(value),
                "custom stable wire name: " + value);
        }
        for (int i = 0; i < native.Length; i++)
        {
            Check(names[(int)Call(toIndex, saver, native[i])] == nativeNames[i],
                "native virtual ConvertName must be preserved: " + native[i]);
        }

        object reader = Context(buffer, true);
        object readSaver = ContextType.GetField(field, Instance).GetValue(reader);
        for (int pass = 0; pass < 2; pass++)
        {
            stream.Position = 0;
            Call(sync, readSaver, reader);
            foreach (T value in values)
            {
                object[] args = { fallback, readSaver, SaveFileVersion.Initial, fallback };
                Call(syncEnum, reader, args);
                Check(EqualityComparer<T>.Default.Equals((T)args[0], value),
                    "SyncEnum round trip, pass " + pass + ": " + value);
            }
            Check(stream.Position == stream.Length && original.SequenceEqual(stream.ToArray()),
                "re-reading the original BufferNew must not change its bytes");
            int readCount = ((List<T>)saverType.GetField("_intToEnumDic", Instance)
                .GetValue(readSaver)).Count;
            Check(readCount == count, "repeated Sync must not append another enum table");
        }
    }

    private static void ReorderedTable<T>(string field, T fallback, T native, T[] custom,
        Func<T, string> name, Func<string, T> parse) where T : struct, Enum
    {
        // Historical IDs and table ordinals intentionally differ from today's registry IDs.
        var oldRecords = custom.Select(value => new
        {
            OldId = (long)Convert.ToInt32(value) + 1000000,
            Key = name(value),
            Current = value,
        }).Reverse().ToArray();
        foreach (var record in oldRecords)
        {
            T restored = parse(record.Key);
            Check(EqualityComparer<T>.Default.Equals(restored, record.Current) &&
                Convert.ToInt32(restored) != record.OldId, "resolve identity, not historical numeric ID");
        }
        string[] names = new[] { "unknown-native-first" }
            .Concat(oldRecords.Select(r => r.Key))
            .Concat(new[] { fallback.ToString(), "unknown-native-second", native.ToString(), oldRecords[0].Key })
            .ToArray();
        T[] expected = new[] { fallback }.Concat(oldRecords.Select(r => r.Current))
            .Concat(new[] { fallback, fallback, native, oldRecords[0].Current }).ToArray();
        using var stream = new MemoryStream();
        var buffer = new BufferNew(stream);
        buffer.WriteInt32LE(names.Length);
        foreach (string key in names)
        {
            buffer.WriteString(key);
        }
        stream.Position = 0;
        object reader = Context(buffer, true);
        object saver = ContextType.GetField(field, Instance).GetValue(reader);
        Type type = SaverType<T>();
        Call(Method(type, "Sync", ContextType), saver, reader);
        MethodInfo fromIndex = Method(type, "Convert", typeof(int));
        for (int i = 0; i < expected.Length; i++)
        {
            Check(EqualityComparer<T>.Default.Equals((T)Call(fromIndex, saver, i), expected[i]),
                "file-order Regist(force:true), ordinal " + i);
        }
        Check(stream.Position == stream.Length, "reordered table consumed exactly");
    }

    /// <summary>
    /// 存档里引用了已被删除的模组条目：读表不能抛异常（否则引擎把整份存档改名成 .crash，
    /// 一局进度全丢），该索引回退成默认值，且不能把后面的索引挤走。
    /// </summary>
    private static void MissingTableKeyFallsBack<T>(string field, T known, T substitute,
        Func<T, string> name, string missingKey) where T : struct, Enum
    {
        string[] keys = { name(known), missingKey, name(known) };
        using var stream = new MemoryStream();
        var buffer = new BufferNew(stream);
        buffer.WriteInt32LE(keys.Length);
        foreach (string key in keys)
        {
            buffer.WriteString(key);
        }
        stream.Position = 0;
        object reader = Context(buffer, true);
        FieldInfo saverField = ContextType.GetField(field, Instance);
        object saver = saverField.GetValue(reader);
        Call(Method(saverField.FieldType, "Sync", ContextType), saver, reader);
        MethodInfo fromIndex = Method(SaverType<T>(), "Convert", typeof(int));
        Check(EqualityComparer<T>.Default.Equals((T)Call(fromIndex, saver, 0), known),
            "known key keeps its index");
        Check(EqualityComparer<T>.Default.Equals((T)Call(fromIndex, saver, 1), substitute),
            "missing key must land on a defined placeholder, not on None");
        Check(EqualityComparer<T>.Default.Equals((T)Call(fromIndex, saver, 2), known),
            "missing key must not shift later indices");
        Check(stream.Position == stream.Length, "missing key table consumed exactly");
    }

    private static void ImageWire(string missing)
    {
        MethodInfo sync = Method(ContextType, "SyncImage", typeof(Image).MakeByRefType(),
            typeof(SaveFileVersion), typeof(Image));
        using var stream = new MemoryStream();
        var buffer = new BufferNew(stream);
        buffer.WriteUInt8(2);
        buffer.WriteString(null);
        stream.Position = 0;
        object reader = Context(buffer, true);
        object[] args = { null, SaveFileVersion.Initial, null };
        Call(sync, reader, args);
        Check(args[0] == null && stream.Position == stream.Length,
            "native image tag must rewind before orig");

        stream.SetLength(0);
        stream.Position = 0;
        buffer.WriteUInt8(3);
        buffer.WriteString(missing);
        stream.Position = 0;
        Call(sync, reader, null, SaveFileVersion.CoinAutoCollect, null);
        Check(stream.Position == 0, "old-version image read must not consume a tag");
        object writer = Context(buffer, false);
        byte[] bytes = stream.ToArray();
        Call(sync, writer, null, SaveFileVersion.CoinAutoCollect, null);
        Check(stream.Position == 0 && bytes.SequenceEqual(stream.ToArray()),
            "old-version image write must not emit a tag");
        InvalidData(() => Call(sync, reader, null, SaveFileVersion.Initial, null),
            "missing tag-3 image must fail");
    }

    private static object Context(BufferNew buffer, bool reading)
    {
        object context = Activator.CreateInstance(ContextType, true);
        ContextType.GetField("mBuffer", Instance).SetValue(context, buffer);
        ContextType.GetField("mReading", Instance).SetValue(context, reading);
        ContextType.GetField("mVersion", Instance).SetValue(context, SaveFileVersion.Initial);
        return context;
    }

    private static Type SaverType<T>() where T : struct, Enum
    {
        return typeof(SeedType).Assembly.GetType("Lawn.GlobalMembersSaveGame+EnumSaver`1", true)
            .MakeGenericType(typeof(T));
    }

    private static Func<T, TResult> Mapper<T, TResult>(string name)
    {
        MethodInfo method = EncodingType.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(T) }, null);
        return (Func<T, TResult>)method.CreateDelegate(typeof(Func<T, TResult>));
    }

    private static MethodInfo Method(Type type, string name, params Type[] args)
    {
        return type.GetMethod(name, Instance, null, args, null) ??
            throw new MissingMethodException(type.FullName, name);
    }

    private static object Call(MethodInfo method, object target, params object[] args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static void InvalidData(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new InvalidOperationException("SaveEncoding FAIL: " + message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException("SaveEncoding FAIL: " + message);
        }
    }
}
