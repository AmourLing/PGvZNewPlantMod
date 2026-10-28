using System;
using System.Collections.Generic;

namespace PGvZPlantKit
{
    /// <summary>
    /// 一个 DLL 里的植物包入口。宿主从 plants/*.dll 反射发现实现并调用 Register。
    /// </summary>
    public interface IPlantPack
    {
        /// <summary>清单里的包 ID；留空时宿主用 DLL 文件名作为包 ID。</summary>
        string PackId => null;

        int Order { get; }

        int HookCount { get; }

        IReadOnlyList<string> HookFailures { get; }

        void Register(string packId);
    }

    public abstract class PlantPackBase : IPlantPack
    {
        public virtual string PackId => null;

        public virtual int Order => 0;

        public virtual int HookCount => 0;

        public virtual IReadOnlyList<string> HookFailures => Array.Empty<string>();

        public abstract void Register(string packId);
    }
}
