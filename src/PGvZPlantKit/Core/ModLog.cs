using System;
using System.IO;
using System.Reflection;
using Sexy;

namespace PGvZPlantKit
{
    public static class ModLog
    {
        private static string LogPath =>
            Path.Combine(ModPaths.StorageDirectory, "plant_kit.log");

        public static void Info(string message)
        {
            SafeWrite("INFO  " + message);
            try { Debug.Log(DebugType.Info, "[PGvZPlantKit] " + message); } catch { }
        }

        public static void Warn(string message)
        {
            SafeWrite("WARN  " + message);
            try { Debug.Log(DebugType.Warn, "[PGvZPlantKit] " + message); } catch { }
        }

        public static void Error(string message, Exception ex = null)
        {
            SafeWrite("ERROR " + message + (ex == null ? "" : Environment.NewLine + ex));
            try { Debug.Log(DebugType.Error, "[PGvZPlantKit] " + message + (ex == null ? "" : " :: " + ex)); } catch { }
        }

        private static void SafeWrite(string text)
        {
            try
            {
                File.AppendAllText(LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + text + Environment.NewLine);
            }
            catch { }
        }
    }
}
