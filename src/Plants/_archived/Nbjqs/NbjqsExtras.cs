using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    internal static class NbjqsExtras
    {
        private static bool _installed;
        private static readonly HashSet<string> HotErrors = new HashSet<string>();

        public static int HookCount { get; private set; }
        public static readonly List<string> HookFailures = new List<string>();

        public static void Install()
        {
            if (_installed)
            {
                return;
            }
            _installed = true;

            Add(typeof(Board), "UpdateGame", Type.EmptyTypes,
                (Action<Action<Board>, Board>)BoardUpdateGameHook);
            Add(typeof(Board), "Draw", new[] { typeof(Graphics) },
                (Action<Action<Board, Graphics>, Board, Graphics>)BoardDrawHook);

            PlantKit.Log("nbjqs extras installed: " + HookCount + " hooks");
        }

        private static void Add(Type type, string name, Type[] args, Delegate hook)
        {
            try
            {
                PlantKit.AddHook(type, name, args, hook);
                HookCount++;
            }
            catch (Exception ex)
            {
                HookFailures.Add(type.Name + "." + name + ": " + ex.Message);
                PlantKit.LogError("nbjqs extra hook failed: " + type.Name + "." + name, ex);
            }
        }

        private static void Hot(string where, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                if (HotErrors.Add(where))
                {
                    PlantKit.LogError("nbjqs hot path failed: " + where, ex);
                }
            }
        }

        private static void BoardUpdateGameHook(Action<Board> orig, Board self)
        {
            orig(self);
            Hot("Board.UpdateGame.nbjqs", () => NbjqsPlant.UpdateEffects(self));
        }

        private static void BoardDrawHook(Action<Board, Graphics> orig, Board self, Graphics g)
        {
            orig(self, g);
            Board.Camera camera = self.mCamera;
            if (camera != null)
            {
                camera.ApplyTransform(g);
            }
            try
            {
                Hot("Board.Draw.nbjqs", () => NbjqsPlant.DrawEffects(self, g));
            }
            finally
            {
                if (camera != null)
                {
                    camera.ResetTransform(g);
                }
            }
            Hot("Board.Draw.ultimate", () => CustomUltimate.Draw(self, g));
        }
    }
}
