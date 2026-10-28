using System;
using System.Linq.Expressions;
using System.Reflection;

namespace PGvZPlantKit
{
    /// <summary>
    /// 目标方法常来自 internal 类型，编译期写不出钩子签名。
    /// 用泛型钩子方法按运行时类型闭合，再绑成 MonoMod 需要的委托形状：
    /// (orig, self?, 原参数...)。
    /// </summary>
    internal static class HookBuilder
    {
        internal static Delegate Bind(MethodInfo hook, params Type[] typeArgs)
        {
            MethodInfo closed = hook.MakeGenericMethod(typeArgs);
            ParameterInfo[] parameters = closed.GetParameters();
            var signature = new Type[parameters.Length + 1];
            for (int i = 0; i < parameters.Length; i++)
            {
                signature[i] = parameters[i].ParameterType;
            }
            signature[parameters.Length] = closed.ReturnType;
            Type delegateType = Expression.GetDelegateType(signature);
            if (delegateType == null)
            {
                throw new InvalidOperationException("no delegate type for " + hook.Name);
            }
            return closed.CreateDelegate(delegateType);
        }
    }
}
