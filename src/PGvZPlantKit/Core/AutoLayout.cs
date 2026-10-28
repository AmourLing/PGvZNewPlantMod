using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// SeedId / 图鉴格 / 选卡格 / 自定义动画槽的自动分配。
    /// 原生占位（含未解锁的空白卡）、已注册的显式位置都会被避开；
    /// 显式字段仍然优先，留空才自动。
    /// </summary>
    internal static class AutoLayout
    {
        public const int AutoSeedStart = 94;
        private const int MaxRows = 64;

        private static readonly Dictionary<CustomPlantDef, string> LastSignatures =
            new Dictionary<CustomPlantDef, string>();

        /// <summary>返回 false 表示 UI 常量尚未初始化，稍后重试。</summary>
        public static bool Ensure()
        {
            AssignReanimSlots();

            bool needsUi = false;
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable && (def.AutoAlmanac || def.AutoChooser))
                {
                    needsUi = true;
                    break;
                }
            }
            if (!needsUi)
            {
                return true;
            }
            if (Sexy.Constants.SMALL_SEEDPACKET_WIDTH <= 0 ||
                Sexy.Constants.SMALL_SEEDPACKET_HEIGHT <= 0 ||
                GameConstants.NUM_ALMANAC_SEEDS <= 0)
            {
                return false;
            }

            AssignAlmanac();
            AssignChooser();
            LogAssignments();
            return true;
        }

        // ---------------------------------------------------------------- 图鉴

        private static void AssignAlmanac()
        {
            var used = new HashSet<(int row, int col)>();
            int width = Sexy.Constants.SMALL_SEEDPACKET_WIDTH;
            int height = Sexy.Constants.SMALL_SEEDPACKET_HEIGHT;
            int offset = Sexy.Constants.Almanac_SeedOffset.X;
            int strideX = width + Sexy.Constants.Almanac_SeedSpace.X;
            int strideY = height + Sexy.Constants.Almanac_SeedSpace.Y;

            int nativeCount = Math.Max(0, GameConstants.NUM_ALMANAC_SEEDS);
            for (int i = 0; i < nativeCount; i++)
            {
                if ((SeedType)i == SeedType.Imitater)
                {
                    continue;
                }
                used.Add((i / 4 + 1, i % 4 + 1));
            }

            // 模仿者不在 4 列网格里，按它的实际位置避开重叠格、并推出模组卡的起始行。
            int imitaterRow = (Sexy.Constants.Almanac_ImitatorPosition.Y - offset) / strideY + 1;
            TRect imitaterRect = new TRect(
                Sexy.Constants.Almanac_ImitatorPosition.X,
                AlmanacRect(imitaterRow, 1, offset, strideX, strideY, width, height).mY,
                width, height);
            for (int row = 1; row <= MaxRows; row++)
            {
                for (int col = 1; col <= 4; col++)
                {
                    TRect cell = AlmanacRect(row, col, offset, strideX, strideY, width, height);
                    // GameConstants.GetRectOverlap 只算横向重叠，这里必须用真正的二维相交。
                    if (Intersects(cell, imitaterRect))
                    {
                        used.Add((row, col));
                    }
                }
            }
            // 图鉴从模仿者的下一行开始排，原生那行剩下的空格留给原生卡自己。
            int startRow = Math.Max(LastNativeSeed(nativeCount) / 4 + 1, imitaterRow) + 1;

            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable && !def.AutoAlmanac &&
                    def.AlmanacRow > 0 && def.AlmanacColumn > 0)
                {
                    used.Add((def.AlmanacRow, def.AlmanacColumn));
                }
            }

            var autoAlmanac = new List<CustomPlantDef>();
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable && def.AutoAlmanac)
                {
                    autoAlmanac.Add(def);
                }
            }
            autoAlmanac.Sort(CustomPlantRegistry.CompareForLayout);
            foreach (CustomPlantDef def in autoAlmanac)
            {
                (int row, int col) cell = FindFree(used, startRow, 1, 4);
                def.AlmanacRow = cell.row;
                def.AlmanacColumn = cell.col;
                used.Add(cell);
            }
        }

        private static TRect AlmanacRect(int row, int col, int offset, int strideX,
            int strideY, int width, int height)
        {
            return new TRect(offset + (col - 1) * strideX,
                offset + (row - 1) * strideY, width, height);
        }

        private static bool Intersects(TRect a, TRect b)
        {
            return a.mX < b.mX + b.mWidth && b.mX < a.mX + a.mWidth &&
                   a.mY < b.mY + b.mHeight && b.mY < a.mY + a.mHeight;
        }

        // ---------------------------------------------------------------- 选卡

        private static void AssignChooser()
        {
            var used = new HashSet<(int row, int col)>();
            int nativeCount = Math.Max(0, GameConstants.NUM_ALMANAC_SEEDS);
            for (int i = 0; i < nativeCount; i++)
            {
                if ((SeedType)i == SeedType.Imitater)
                {
                    continue;
                }
                used.Add((i / 4, i % 4));
            }

            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable && !def.AutoChooser &&
                    def.ChooserRow >= 0 && def.ChooserColumn >= 0)
                {
                    used.Add((def.ChooserRow, def.ChooserColumn));
                }
            }

            // 选卡同理：从最后一张原生卡（当前是火红莲）的下一行起排。
            int startChooserRow = LastNativeSeed(nativeCount) / 4 + 1;
            var autoChooser = new List<CustomPlantDef>();
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable && def.AutoChooser)
                {
                    autoChooser.Add(def);
                }
            }
            autoChooser.Sort(CustomPlantRegistry.CompareForLayout);
            foreach (CustomPlantDef def in autoChooser)
            {
                (int row, int col) cell = FindFree(used, startChooserRow, 0, 4);
                def.ChooserRow = cell.row;
                def.ChooserColumn = cell.col;
                used.Add(cell);
            }
        }

        /// <summary>最后一张原生卡的索引：表尾是模仿者时不算它。</summary>
        private static int LastNativeSeed(int nativeCount)
        {
            int last = nativeCount - 1;
            return last >= 0 && (SeedType)last == SeedType.Imitater ? last - 1 : last;
        }

        private static (int row, int col) FindFree(
            HashSet<(int row, int col)> used, int minRow, int minCol, int columns)
        {
            for (int row = minRow; row <= MaxRows; row++)
            {
                for (int col = minCol; col < minCol + columns; col++)
                {
                    if (!used.Contains((row, col)))
                    {
                        return (row, col);
                    }
                }
            }
            return (MaxRows, minCol);
        }

        // ---------------------------------------------------------------- 动画槽

        private static void AssignReanimSlots()
        {
            var used = new HashSet<int>();
            var ourPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (!def.UsesCustomAnimation)
                {
                    continue;
                }
                ourPaths.Add(def.UsesCustomReanim
                    ? "reanim/" + def.ReanimName
                    : "spanim/" + def.SpineName);
                if (!def.AutoReanimSlot && def.ReanimSlot >= 0)
                {
                    used.Add(def.ReanimSlot);
                }
            }

            // 避开其他 mod 已经写进动画参数表的槽位（通过路径识别自己的条目）。
            ReanimationParams[] parameters = GameConstants.gLawnReanimationArray;
            if (parameters != null)
            {
                for (int i = CustomPlantRegistry.ReanimBase; i < parameters.Length; i++)
                {
                    ReanimationParams param = parameters[i];
                    if (param != null && !ourPaths.Contains(param.mReanimFileName ?? string.Empty))
                    {
                        used.Add(i - CustomPlantRegistry.ReanimBase);
                    }
                }
            }

            var autoReanim = new List<CustomPlantDef>();
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.UsesCustomAnimation && def.AutoReanimSlot)
                {
                    autoReanim.Add(def);
                }
            }
            autoReanim.Sort(CustomPlantRegistry.ComparePackRegistration);
            foreach (CustomPlantDef def in autoReanim)
            {
                int slot = 0;
                while (used.Contains(slot))
                {
                    slot++;
                }
                def.ReanimSlot = slot;
                used.Add(slot);
            }
        }

        private static void LogAssignments()
        {
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (!def.AutoSeedId && !def.AutoAlmanac && !def.AutoChooser &&
                    !def.AutoReanimSlot)
                {
                    continue;
                }
                if (!def.Selectable && !def.AutoSeedId && !def.AutoReanimSlot)
                {
                    continue;
                }
                string signature = def.SeedId + "/" + def.AlmanacRow + "/" +
                    def.AlmanacColumn + "/" + def.ChooserRow + "/" +
                    def.ChooserColumn + "/" + def.ReanimSlot;
                if (LastSignatures.TryGetValue(def, out string old) && old == signature)
                {
                    continue;
                }
                LastSignatures[def] = signature;
                ModLog.Info("auto layout " + def.Name + " [pack " + def.PackId +
                    "]: seed " + def.SeedId +
                    ", almanac (" + def.AlmanacRow + "," + def.AlmanacColumn +
                    "), chooser (" + def.ChooserRow + "," + def.ChooserColumn +
                    "), reanim slot " + def.ReanimSlot);
            }
        }
    }
}
