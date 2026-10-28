using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lawn;
using Sexy;

namespace PGvZPlantKit
{
    public static class CustomSave
    {
        /// <summary>
        /// 存档编码回归会故意造"存档引用已删除条目"的用例；那时压掉那条错误日志，
        /// 免得打包/回归输出里看着像真失败。正常游戏流程保持 false。
        /// </summary>
        public static bool QuietMissingEntries;

        private const int StateMarker = 0x504B5331;
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticMethods = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly Type SaveHelpers = typeof(Board).Assembly.GetType("Lawn.GlobalMembersSaveGame", true);
        private static readonly Type ContextType = SaveHelpers.GetNestedType("SaveGameContext", BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo ReadingField = ContextType.GetField("mReading");
        private static readonly FieldInfo BufferField = ContextType.GetField("mBuffer");
        private static readonly ConditionalWeakTable<Board, SavedState> Pending = new();
        private static readonly List<Func<Board, bool>> ExtraPredicates = new();
        private static event Action<Board, string> SaveExtra;
        private static event Action<Board, string> LoadExtra;

        public static string RootDir => Path.Combine(ModPaths.StorageDirectory, "SaveData");
        public static string SeedBankDir => Path.Combine(RootDir, "seedbanks");

        public sealed class SavedPlant
        {
            public int Index { get; set; }
            public string Seed { get; set; }
            public Dictionary<string, string> Fields { get; set; } = new();
        }

        public sealed class SavedBullet
        {
            public int Index { get; set; }
            public string Id { get; set; }
            public Dictionary<string, string> Fields { get; set; } = new();
        }

        public sealed class SavedState
        {
            public int Version { get; set; } = 1;
            public List<SavedPlant> Plants { get; set; } = new();
            public List<SavedBullet> Bullets { get; set; } = new();
        }

        internal static void Install(Action<MethodInfo, Delegate> add)
        {
            CustomSaveEncoding.Install(add);
            MethodInfo sync = SaveHelpers.GetMethod("SyncGame", StaticMethods);
            Type origType = Expression.GetActionType(ContextType, typeof(Board));
            MethodInfo hook = typeof(CustomSave).GetMethod(nameof(SyncGameHook), StaticMethods);
            add(sync, HookBuilder.Bind(hook, origType, ContextType));
        }

        private static void SyncGameHook<TOrig, TContext>(TOrig orig, TContext context, Board board)
        {
            SyncGame((Delegate)(object)orig, context, board);
        }

        private static void SyncGame(Delegate original, object context, Board board)
        {
            bool reading = (bool)ReadingField.GetValue(context);
            var buffer = (BufferNew)BufferField.GetValue(context);
            try
            {
                original.DynamicInvoke(context, board);
                if (!reading)
                {
                    SavedState state = Capture(board);
                    buffer.WriteInt32LE(StateMarker);
                    buffer.WriteString(JsonSerializer.Serialize(state));
                    return;
                }
                Pending.Remove(board);
                if (buffer.InnerStream.Position == buffer.InnerStream.Length)
                {
                    return;
                }
                if (buffer.ReadInt32LE() != StateMarker)
                {
                    throw new InvalidDataException("Unrecognized custom plant state block");
                }
                SavedState loaded = JsonSerializer.Deserialize<SavedState>(buffer.ReadString());
                if (loaded == null || loaded.Version != 1)
                {
                    throw new InvalidDataException("Unsupported custom plant state version");
                }
                Pending.Add(board, loaded);
            }
            catch (Exception error)
            {
                Exception cause = error is TargetInvocationException invocation ? invocation.InnerException : error;
                ModLog.Error(reading ? "native save state read failed" : "native save state write failed", cause);
                throw;
            }
        }

        private static SavedState Capture(Board board)
        {
            var state = new SavedState();
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                CustomPlantBase behavior = CustomPlantRegistry.Get(plant);
                if (behavior == null)
                {
                    continue;
                }
                state.Plants.Add(new SavedPlant
                {
                    Index = i,
                    Seed = CustomSaveEncoding.SeedName(behavior.Definition.Seed),
                    Fields = CaptureFields(behavior, typeof(CustomPlantBase), board),
                });
            }
            for (int i = 0; i < board.mProjectiles.Count; i++)
            {
                Projectile projectile = board.mProjectiles[i];
                if (!CustomProjectiles.TryGetTag(projectile, out ProjectileTag tag))
                {
                    continue;
                }
                state.Bullets.Add(new SavedBullet
                {
                    Index = i,
                    Id = tag.Bullet?.Id,
                    Fields = CaptureFields(tag, typeof(object), board),
                });
            }
            return state;
        }

        private static IEnumerable<FieldInfo> StateFields(Type type, Type stop)
        {
            for (; type != null && type != stop; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(InstanceFields | BindingFlags.DeclaredOnly))
                {
                    if (!field.IsInitOnly)
                    {
                        yield return field;
                    }
                }
            }
        }

        private static string FieldKey(FieldInfo field) => field.DeclaringType.FullName + ":" + field.Name;

        private static Dictionary<string, string> CaptureFields(object instance, Type stop, Board board)
        {
            var result = new Dictionary<string, string>();
            foreach (FieldInfo field in StateFields(instance.GetType(), stop))
            {
                object value = field.GetValue(instance);
                Type type = field.FieldType;
                string text;
                if (type == typeof(SeedType))
                    text = CustomSaveEncoding.SeedName((SeedType)value);
                else if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                    text = Convert.ToString(value, CultureInfo.InvariantCulture);
                else if (type == typeof(Plant))
                    text = board.mPlants.IndexOf((Plant)value).ToString(CultureInfo.InvariantCulture);
                else if (type == typeof(Zombie))
                    text = board.mZombies.IndexOf((Zombie)value).ToString(CultureInfo.InvariantCulture);
                else if (type == typeof(Projectile))
                    text = board.mProjectiles.IndexOf((Projectile)value).ToString(CultureInfo.InvariantCulture);
                else
                    continue;
                result.Add(FieldKey(field), text);
            }
            if (instance is IronPython.Runtime.Types.IPythonObject python && python.Dict != null)
            {
                foreach (KeyValuePair<object, object> item in python.Dict)
                {
                    if (item.Key is string key && (item.Value == null || item.Value is string || item.Value.GetType().IsPrimitive))
                        result["python:" + key] = JsonSerializer.Serialize(item.Value);
                }
            }
            return result;
        }

        private static void RestoreFields(object instance, Type stop, Board board, Dictionary<string, string> values)
        {
            foreach (FieldInfo field in StateFields(instance.GetType(), stop))
            {
                if (!values.TryGetValue(FieldKey(field), out string text))
                    continue;
                Type type = field.FieldType;
                object value;
                if (type == typeof(SeedType))
                    value = CustomSaveEncoding.ParseSeed(text);
                else if (type.IsEnum)
                    value = Enum.Parse(type, text);
                else if (type == typeof(string))
                    value = text;
                else if (type.IsPrimitive)
                    value = Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
                else if (type == typeof(Plant))
                    value = EntityAt(board.mPlants, int.Parse(text, CultureInfo.InvariantCulture));
                else if (type == typeof(Zombie))
                    value = EntityAt(board.mZombies, int.Parse(text, CultureInfo.InvariantCulture));
                else if (type == typeof(Projectile))
                    value = EntityAt(board.mProjectiles, int.Parse(text, CultureInfo.InvariantCulture));
                else
                    continue;
                field.SetValue(instance, value);
            }
            if (instance is IronPython.Runtime.Types.IPythonObject python && python.Dict != null)
            {
                foreach (KeyValuePair<string, string> entry in values.Where(p => p.Key.StartsWith("python:", StringComparison.Ordinal)))
                {
                    using JsonDocument document = JsonDocument.Parse(entry.Value);
                    JsonElement value = document.RootElement;
                    python.Dict[entry.Key.Substring(7)] = value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => value.TryGetInt64(out long integer) ? (object)integer : value.GetDouble(),
                        _ => null,
                    };
                }
            }
        }

        private static T EntityAt<T>(List<T> list, int index) where T : class
        {
            if (index == -1) return null;
            if (index < 0 || index >= list.Count) throw new InvalidDataException("Invalid saved entity reference");
            return list[index];
        }

        internal static bool Save(Func<Board, string, bool> original, Board board, string path)
        {
            CustomPlantRegistry.EnsureDefinitions(validate: true);
            string temporary = path + ".plantkit-" + Guid.NewGuid().ToString("N") + ".tmp";
            bool success = original(board, temporary);
            if (!success)
            {
                ModLog.Error("native save failed; previous save preserved: " + path);
                return false;
            }
            File.Move(temporary, path, true);
            SaveSidecars(board, path);
            ModLog.Info("saved custom state: " + path + ", plants=" + board.mPlants.Count);
            return true;
        }

        internal static bool Load(Func<Board, string, bool> original, Board board, string path)
        {
            CustomPlantRegistry.EnsureDefinitions(validate: true);
            Pending.Remove(board);
            if (!original(board, path))
            {
                ModLog.Error("native load failed: " + path);
                return false;
            }
            Pending.TryGetValue(board, out SavedState state);
            Pending.Remove(board);
            CustomPlantRegistry.ClearLoadedBehaviors();
            CustomProjectiles.ClearLoadedTags();
            foreach (Plant plant in board.mPlants)
            {
                CustomPlantDef def = CustomPlantRegistry.GetDef(plant.mSeedType);
                if (def != null && !plant.mDead)
                    CustomPlantRegistry.CreateForPlant(plant, def);
            }
            if (state != null)
            {
                foreach (SavedPlant record in state.Plants)
                {
                    Plant plant = EntityAt(board.mPlants, record.Index);
                    CustomPlantDef def = record.Seed == null ? null
                        : CustomPlantRegistry.GetDef(CustomSaveEncoding.ParseSeedOrNone(record.Seed));
                    if (plant == null || def == null)
                    {
                        continue;
                    }
                    CustomPlantBase behavior = CustomPlantRegistry.Get(plant) ?? CustomPlantRegistry.CreateForPlant(plant, def);
                    RestoreFields(behavior, typeof(CustomPlantBase), board, record.Fields);
                }
                foreach (SavedBullet record in state.Bullets)
                {
                    Projectile projectile = EntityAt(board.mProjectiles, record.Index);
                    if (projectile == null)
                    {
                        continue;
                    }
                    ProjectileTag tag = CustomProjectiles.TagFor(projectile);
                    if (!string.IsNullOrEmpty(record.Id))
                    {
                        if (!CustomSaveEncoding.TryFindBullet(record.Id, out CustomBulletDef def))
                        {
                            continue;
                        }
                        CustomBullets.Attach(projectile, def);
                    }
                    RestoreFields(tag, typeof(object), board, record.Fields);
                }
            }
            LoadSidecars(board, path);
            ModLog.Info("loaded custom state: " + path + ", behaviors=" + (state?.Plants.Count ?? 0));
            return true;
        }

        public static void RegisterExtraHandler(Func<Board, bool> hasExtraData, Action<Board, string> saveExtra, Action<Board, string> loadExtra)
        {
            if (hasExtraData != null) ExtraPredicates.Add(hasExtraData);
            if (saveExtra != null) SaveExtra += saveExtra;
            if (loadExtra != null) LoadExtra += loadExtra;
        }

        public static void EnsureSidecarDir(string dir) => Directory.CreateDirectory(dir);

        public static void WriteSidecar(string dir, string filePath, string extension, string text)
        {
            EnsureSidecarDir(dir);
            string target = Path.Combine(dir, Path.GetFileName(filePath) + extension);
            string temporary = target + ".tmp";
            File.WriteAllText(temporary, text, Encoding.UTF8);
            File.Move(temporary, target, true);
        }

        public static string ReadSidecar(string dir, string filePath, string extension)
        {
            string path = Path.Combine(dir, Path.GetFileName(filePath) + extension);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }

        public static bool BoardHasCustomData(Board board) => board.mPlants.Any(p => CustomPlantRegistry.IsCustomSeed(p.mSeedType)) ||
            board.mSeedBank.mSeedPackets.Any(p => p != null && CustomPlantRegistry.IsCustomSeed(p.mPacketType)) || ExtraPredicates.Any(p => p(board));

        public static void SaveSidecars(Board board, string filePath) => SaveExtra?.Invoke(board, filePath);
        public static void LoadSidecars(Board board, string filePath) => LoadExtra?.Invoke(board, filePath);
    }
}
