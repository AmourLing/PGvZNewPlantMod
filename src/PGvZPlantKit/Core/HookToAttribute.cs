using System;

namespace PGvZPlantKit
{
    /// <summary>
    /// 声明式钩子注册，对应 IronPython 那边的 <c>@M.HookTo(Target.Method)</c>：
    /// 标在处理器方法上，处理器第一个形参就是被钩方法的委托类型，后面的形参对应目标方法。
    /// <code>
    /// [HookTo(typeof(Zombie), nameof(Zombie.DieNoLoot), typeof(bool))]
    /// private static void ZombieDie(Action&lt;Zombie, bool&gt; orig, Zombie self, bool give) { ... }
    /// </code>
    /// 目标用类型 + 方法名 + 形参类型指定，和 <see cref="HookUtil.Method"/> 的口径一致，
    /// 重载必须把形参类型写全，不会像 Python 那样按名字撞上第一个同名方法。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class HookToAttribute : Attribute
    {
        public HookToAttribute(Type target, string methodName, params Type[] parameters)
        {
            Target = target;
            MethodName = methodName;
            Parameters = parameters ?? Type.EmptyTypes;
        }

        public Type Target { get; }

        public string MethodName { get; }

        public Type[] Parameters { get; }
    }
}
