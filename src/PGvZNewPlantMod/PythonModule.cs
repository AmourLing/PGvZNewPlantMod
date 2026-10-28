using System;
using System.Threading;
using IronPython.Runtime;
using PGvZPlantKit;

[assembly: PythonModule("PGvZNewPlantMod", typeof(PGvZNewPlantMod.PythonModule))]

namespace PGvZNewPlantMod
{
    /// <summary>宿主入口：安装框架、注册共享资源、加载 plants/ 下的 C# 插件和 Python 植物。</summary>
    public static class PythonModule
    {
        private static int _installCount;

        public static void PerformModuleReload(PythonContext context, PythonDictionary dictionary)
        {
            try
            {
                ModHost.Load();
                Interlocked.Increment(ref _installCount);
                PlantKit.Log("mod host loaded (install #" + _installCount + ")");
            }
            catch (Exception ex)
            {
                PlantKit.LogError("nine-plant mod install failed", ex);
            }
        }
    }

    public static class NinePlantModule
    {
        private static bool _loaded;

        public static void RegisterBuiltins(string modFolder)
        {
            if (_loaded)
            {
                return;
            }
            _loaded = true;

            PlantKit.Install();
            PlantKit.RegisterEmbeddedImages(typeof(NinePlantModule).Assembly,
                "PGvZNewPlantMod.Assets.");
            PlantPluginLoader.Load(modFolder);
        }
    }
}
