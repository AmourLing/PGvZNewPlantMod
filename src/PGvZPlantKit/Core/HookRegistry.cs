using System;
using System.Collections.Generic;
using System.Reflection;

namespace PGvZPlantKit
{
    /// <summary>
    /// 受管钩子注册表，解决传统 IronPython 钩子的三个坑：
    /// 1) HookResult 被 GC 后终结器会再卸一次钩子（UnHook 非幂等，直接崩进程）——
    ///    这里对见过的 HookResult 永久持有强引用，终结器永不运行；
    /// 2) 重跑脚本重复挂钩——同名 key 再挂会先卸旧再挂新，零样板幂等；
    /// 3) 植物死了钩子还在——CustomPlantBase.ManageHook 挂的实例钩子随植物移除自动卸载。
    /// </summary>
    public static class HookRegistry
    {
        private class Entry
        {
            public object Result;
            public bool Unhooked;
        }

        private static readonly object Lock = new object();
        // key -> 当前生效的条目；替换/卸载后条目移入 Graveyard 永久持有。
        private static readonly Dictionary<string, Entry> Active =
            new Dictionary<string, Entry>();
        // 见过的一切 HookResult 的最终归宿：只进不出，防终结器二次卸载。
        private static readonly List<object> Graveyard = new List<object>();

        /// <summary>当前生效的受管钩子数（不含已卸载的）。</summary>
        public static int Count
        {
            get
            {
                lock (Lock)
                {
                    return Active.Count;
                }
            }
        }

        /// <summary>当前生效的受管钩子 key（排序后），用来核对"到底挂了几份、挂的是谁"。</summary>
        public static IReadOnlyList<string> Keys
        {
            get
            {
                lock (Lock)
                {
                    List<string> keys = new List<string>(Active.Keys);
                    keys.Sort(StringComparer.Ordinal);
                    return keys;
                }
            }
        }

        /// <summary>
        /// 挂一个受管钩子。key 前缀用 PlantKit.CurrentPackId：.py 植物的包 ID 就是文件名本身
        /// （如 "IceFumeShroom.fire"），和 plant_packs.json 的条目同名，按包清理才匹配得上；
        /// 同名再挂自动先卸旧。
        /// 返回原 HookResult 方便 python 里一行写完。
        /// </summary>
        public static object Manage(string key, object result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            lock (Lock)
            {
                if (Active.TryGetValue(key, out Entry old))
                {
                    SafeUnhook(old);
                    Active.Remove(key);
                }
                Active[key] = new Entry { Result = result };
                return result;
            }
        }

        /// <summary>按 key 卸载；没挂过返回 False。</summary>
        public static bool Unmanage(string key)
        {
            lock (Lock)
            {
                if (!Active.TryGetValue(key, out Entry entry))
                {
                    return false;
                }
                SafeUnhook(entry);
                Active.Remove(key);
                return true;
            }
        }

        /// <summary>卸掉 key 以 prefix 开头的所有受管钩子（例如整个包的），返回卸载数。</summary>
        public static int UnmanageByPrefix(string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return 0;
            }
            lock (Lock)
            {
                List<string> doomed = null;
                foreach (KeyValuePair<string, Entry> pair in Active)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        (doomed ??= new List<string>()).Add(pair.Key);
                    }
                }
                if (doomed == null)
                {
                    return 0;
                }
                foreach (string key in doomed)
                {
                    SafeUnhook(Active[key]);
                    Active.Remove(key);
                }
                return doomed.Count;
            }
        }

        /// <summary>实例钩子随植物移除时的归宿：卸一次并永久持有引用。</summary>
        internal static void Bury(object result)
        {
            if (result == null)
            {
                return;
            }
            lock (Lock)
            {
                Graveyard.Add(result);
            }
        }

        private static void SafeUnhook(Entry entry)
        {
            if (entry.Unhooked)
            {
                return;
            }
            entry.Unhooked = true;
            try
            {
                // HookResult 在运行时才加载的 LawnMod 程序集里，静态引用不到，反射调 UnHook。
                MethodInfo unhook = entry.Result.GetType().GetMethod("UnHook", Type.EmptyTypes);
                unhook?.Invoke(entry.Result, null);
            }
            catch (Exception ex)
            {
                ModLog.Error("managed hook UnHook failed", ex);
            }
            finally
            {
                Graveyard.Add(entry.Result);
            }
        }
    }
}
