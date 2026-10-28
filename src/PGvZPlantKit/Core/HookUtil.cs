using System;
using System.Reflection;
using MonoMod.RuntimeDetour.HookGen;

namespace PGvZPlantKit
{
    /// <summary>供 kit 自身和第三方 mod 安装额外钩子。</summary>
    public static class HookUtil
    {
        public static MethodInfo Method(Type type, string name, params Type[] args)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                null, args, null);
            if (method == null)
            {
                throw new MissingMethodException(type.FullName, name);
            }
            return method;
        }

        public static void Add(MethodInfo target, Delegate hook)
        {
            HookEndpointManager.Add(target, hook);
        }
    }
}
