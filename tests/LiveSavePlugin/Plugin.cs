using IronPython.Runtime;

[assembly: PythonModule("PGvZLiveSaveTest", typeof(PGvZLiveSaveTest.Plugin))]

namespace PGvZLiveSaveTest
{
    public static class Plugin
    {
        public static void PerformModuleReload(PythonContext context, PythonDictionary dictionary)
        {
        }

        public static string Result => LiveSaveTests.Result;

        public static void Queue(string operation)
        {
            LiveSaveTests.Queue(operation);
        }
    }
}
