using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    internal static class CustomSaveEncoding
    {
        private const BindingFlags Instance = BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const string SeedPrefix = "pgvz-plant:";
        private const string AnimationPrefix = "pgvz-animation:";
        private static readonly Type ContextType = typeof(SeedType).Assembly.GetType(
            "Lawn.GlobalMembersSaveGame+SaveGameContext", true);
        private static readonly HashSet<string> MissingLogged = new HashSet<string>();
        private static readonly FieldInfo ReadingField =
            ContextType.GetField("mReading", Instance);
        private static readonly FieldInfo VersionField =
            ContextType.GetField("mVersion", Instance);
        private static readonly FieldInfo BufferField =
            ContextType.GetField("mBuffer", Instance);

        internal static void Install(Action<MethodInfo, Delegate> add)
        {
            if (add == null)
            {
                throw new ArgumentNullException(nameof(add));
            }
            InstallEnum<SeedType>(add);
            InstallEnum<ReanimationType>(add);

            Type imageRef = typeof(Image).MakeByRefType();
            MethodInfo target = HookUtil.Method(ContextType, "SyncImage",
                imageRef, typeof(SaveFileVersion), typeof(Image));
            Type origType = Expression.GetDelegateType(ContextType, imageRef,
                typeof(SaveFileVersion), typeof(Image), typeof(void));
            MethodInfo hook = typeof(CustomSaveEncoding).GetMethod(nameof(SyncImageHook), Static);
            add(target, HookBuilder.Bind(hook,
                origType, ContextType, typeof(Image), typeof(SaveFileVersion)));
        }

        private static void SyncHook<T, TOrig, TSelf, TContext>(
            TOrig orig, TSelf self, TContext context) where T : struct, Enum
        {
            SyncTable<T>(self, (bool)ReadingField.GetValue(context),
                (BufferNew)BufferField.GetValue(context));
        }

        private static void SyncImageHook<TOrig, TSelf, TImage, TVersion>(
            TOrig orig, TSelf self, ref TImage image, TVersion version, TImage defaultImage)
        {
            Image current = (Image)(object)image;
            bool handled = TrySyncImage((bool)ReadingField.GetValue(self),
                (SaveFileVersion)VersionField.GetValue(self),
                (BufferNew)BufferField.GetValue(self), ref current,
                (SaveFileVersion)(object)version);
            if (handled)
            {
                image = (TImage)(object)current;
                return;
            }
            object[] args = { self, image, version, defaultImage };
            ((Delegate)(object)orig).DynamicInvoke(args);
            image = (TImage)args[1];
        }

        private static Type SaverType<T>() where T : struct, Enum
        {
            return typeof(SeedType).Assembly.GetType(
                "Lawn.GlobalMembersSaveGame+EnumSaver`1", true).MakeGenericType(typeof(T));
        }

        private static void InstallEnum<T>(Action<MethodInfo, Delegate> add)
            where T : struct, Enum
        {
            Type saverType = SaverType<T>();
            MethodInfo target = HookUtil.Method(saverType, "Sync", ContextType);
            Type origType = Expression.GetDelegateType(saverType, ContextType, typeof(void));
            MethodInfo hook = typeof(CustomSaveEncoding).GetMethod(nameof(SyncHook), Static);
            add(target, HookBuilder.Bind(hook,
                typeof(T), origType, saverType, ContextType));
        }

        private static void SyncTable<T>(object saver, bool reading, BufferNew buffer)
            where T : struct, Enum
        {
            Type type = SaverType<T>();
            var entries = (List<T>)type.GetField("_intToEnumDic", Instance).GetValue(saver);
            MethodInfo regist = HookUtil.Method(type, "Regist", typeof(T), typeof(bool));
            if (reading)
            {
                int count = buffer.ReadInt32LE();
                if (count < 0 || count > (buffer.InnerStream.Length - buffer.InnerStream.Position) / 4)
                {
                    throw new InvalidDataException("Invalid saved enum table length: " + count);
                }
                entries.Clear();
                ((Dictionary<T, int>)type.GetField("_enumToIntDic", Instance)
                    .GetValue(saver)).Clear();
                T fallback = (T)type.GetField("_defaultValueEnum", Instance).GetValue(saver);
                MethodInfo parse = HookUtil.Method(type, "ConvertName", typeof(string));
                for (int i = 0; i < count; i++)
                {
                    string name = buffer.ReadString();
                    T value;
                    if (IsCustomName(name))
                    {
                        // 存档里的模组植物可能已经被删掉：换成回退值继续读，
                        // 不能整档失败（引擎会把文件改名成 .crash，一局进度全丢）。
                        try
                        {
                            value = typeof(T) == typeof(SeedType)
                                ? (T)(object)ParseSeed(name)
                                : (T)(object)ParseAnimation(name);
                        }
                        catch (InvalidDataException)
                        {
                            LogMissingOnce(name);
                            value = Placeholder<T>();
                        }
                    }
                    else
                    {
                        try
                        {
                            value = (T)parse.Invoke(saver, new object[] { name });
                        }
                        catch (TargetInvocationException)
                        {
                            value = fallback;
                        }
                    }
                    regist.Invoke(saver, new object[] { value, true });
                }
                return;
            }

            // InitializeSaver has already registered the native values on the write path.
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                T value = typeof(T) == typeof(SeedType)
                    ? (T)(object)def.Seed
                    : (T)(object)def.ReanimType;
                regist.Invoke(saver, new object[] { value, false });
            }
            MethodInfo convertName = HookUtil.Method(type, "ConvertName", typeof(T));
            buffer.WriteInt32LE(entries.Count);
            foreach (T value in entries)
            {
                string name = typeof(T) == typeof(SeedType)
                    ? CustomSeedName((SeedType)(object)value)
                    : CustomAnimationName((ReanimationType)(object)value);
                // Invoke the virtual method on the actual saver, including zombie-card names.
                buffer.WriteString(name ?? (string)convertName.Invoke(saver, new object[] { value }));
            }
        }

        internal static string SeedName(SeedType seed)
        {
            return CustomSeedName(seed) ?? seed.ToString();
        }

        internal static SeedType ParseSeed(string name)
        {
            if (IsCustomName(name))
            {
                foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
                {
                    if (string.Equals(name, SeedPrefix + def.PackId + ":" + def.Name,
                        StringComparison.Ordinal))
                    {
                        return def.Seed;
                    }
                }
                throw new InvalidDataException("Missing custom plant: " + name);
            }
            return Enum.TryParse(name, out SeedType value) ? value : SeedType.None;
        }

        /// <summary>状态块里遇到已被删除的模组植物：回退成 None 让调用方跳过这一株。</summary>
        internal static SeedType ParseSeedOrNone(string name)
        {
            try
            {
                return ParseSeed(name);
            }
            catch (InvalidDataException)
            {
                LogMissingOnce(name);
                return SeedType.None;
            }
        }

        internal static bool TryFindBullet(string id, out CustomBulletDef def)
        {
            def = CustomBullets.Find(id);
            if (def == null)
            {
                LogMissingOnce(id);
            }
            return def != null;
        }

        /// <summary>
        /// 已删除条目在存档里的占位值：必须是引擎自己有定义的原生值。
        /// 用 None(-1) 会让重动画/植物同步取到空定义并抛异常，整档被打成 .crash。
        /// </summary>
        private static T Placeholder<T>() where T : struct, Enum
        {
            if (typeof(T) == typeof(SeedType))
            {
                return (T)(object)SeedType.Wallnut;
            }
            if (typeof(T) == typeof(ReanimationType))
            {
                return (T)(object)ReanimationType.Peashooter;
            }
            return default;
        }

        private static void LogMissingOnce(string key)
        {
            if (MissingLogged.Add(key) && !CustomSave.QuietMissingEntries)
            {
                ModLog.Error("save references a removed custom entry, skipped: " + key);
            }
        }

        internal static string AnimationName(ReanimationType animation)
        {
            return CustomAnimationName(animation) ?? animation.ToString();
        }

        internal static ReanimationType ParseAnimation(string name)
        {
            if (IsCustomName(name))
            {
                foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
                {
                    if (def.UsesCustomAnimation && string.Equals(name,
                        AnimationPrefix + def.PackId + ":" + AnimationAsset(def),
                        StringComparison.Ordinal))
                    {
                        return def.ReanimType;
                    }
                }
                throw new InvalidDataException("Missing custom animation: " + name);
            }
            return Enum.TryParse(name, out ReanimationType value) ? value : ReanimationType.None;
        }

        private static bool IsCustomName(string name)
        {
            return name != null && name.StartsWith("pgvz-", StringComparison.Ordinal);
        }

        private static string CustomSeedName(SeedType seed)
        {
            if (seed != SeedType.None)
            {
                foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
                {
                    if (def.Seed == seed)
                    {
                        return SeedPrefix + def.PackId + ":" + def.Name;
                    }
                }
            }
            return null;
        }

        private static string CustomAnimationName(ReanimationType animation)
        {
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.UsesCustomAnimation && def.ReanimType == animation)
                {
                    return AnimationPrefix + def.PackId + ":" + AnimationAsset(def);
                }
            }
            return null;
        }

        private static string AnimationAsset(CustomPlantDef def)
        {
            return !string.IsNullOrEmpty(def.SpineName) ? def.SpineName : def.ReanimName;
        }

        private static bool TrySyncImage(bool reading, SaveFileVersion currentVersion,
            BufferNew buffer, ref Image image, SaveFileVersion version)
        {
            if (currentVersion < version)
            {
                return false;
            }
            if (reading)
            {
                long position = buffer.InnerStream.Position;
                if (buffer.ReadUInt8() != 3)
                {
                    buffer.InnerStream.Position = position;
                    return false;
                }
                string name = buffer.ReadString();
                image = AssetStore.Get(name) ??
                    throw new InvalidDataException("Missing custom image: " + name);
                return true;
            }
            if (image != null && AssetStore.TryGetName(image, out string assetName))
            {
                buffer.WriteUInt8(3);
                buffer.WriteString(assetName);
                return true;
            }
            return false;
        }
    }
}
