using System;
using System.Collections.Generic;
using System.Reflection;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 把模组植物的名字与简介登记进游戏自己的字符串表。
    /// 原生取名/取简介走 [mPlantName] 与 [mPlantName_TOOLTIP] 两个键，
    /// 不登记就会显示 &lt;Missing 某某_TOOLTIP&gt;。
    /// </summary>
    internal static class CustomStrings
    {
        private static readonly Dictionary<string, string> Properties =
            (Dictionary<string, string>)typeof(Reanimation).Assembly
                .GetType("Sexy.TodLib.TodStringFile", true)
                .GetField("gStringProperties", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);

        internal static void Ensure(CustomPlantDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Name))
            {
                return;
            }
            if (!Properties.ContainsKey(def.Name))
            {
                Properties[def.Name] = def.Name;
            }
            string tooltip = FirstLine(def.AlmanacText);
            string key = def.Name + "_TOOLTIP";
            if (!string.IsNullOrEmpty(tooltip) && !Properties.ContainsKey(key))
            {
                Properties[key] = tooltip;
            }
        }

        internal static bool Has(string key)
        {
            return !string.IsNullOrEmpty(key) && Properties.ContainsKey(key);
        }

        private static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            int cut = text.IndexOf('\n');
            string line = (cut < 0 ? text : text.Substring(0, cut)).Trim();
            return line.Length == 0 ? null : line;
        }
    }
}
