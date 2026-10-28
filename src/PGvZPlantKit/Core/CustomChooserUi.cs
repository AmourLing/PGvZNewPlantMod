using System;
using System.Collections.Generic;
using System.Reflection;
using Lawn;
using Sexy;
using Sexy.TodLib;


namespace PGvZPlantKit
{
    /// <summary>选卡界面、卡组、模仿者与图鉴。对应 Python 版 5598-6600 行的 UI 钩子。</summary>
    public static class CustomChooserUi
    {
        private static readonly HashSet<SeedChooserScreen> ScrollReady =
            new HashSet<SeedChooserScreen>();

        private static IEnumerable<CustomPlantDef> SelectableDefs()
        {
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                if (def.Selectable)
                {
                    yield return def;
                }
            }
        }

        /// <summary>
        /// 按"行从上到下、行内从左到右"的顺序画：卡上的立绘会向上探进上一行，
        /// 只有后画的（更下面的行）压在上面才对；按包顺序画会出现
        /// "这张被上一行截掉、旁边那张却压着它"的乱序。
        /// </summary>
        private static IEnumerable<CustomPlantDef> InDrawOrder(
            Func<CustomPlantDef, int> row, Func<CustomPlantDef, int> column)
        {
            var ordered = new List<CustomPlantDef>(SelectableDefs());
            ordered.Sort((a, b) =>
            {
                int byRow = row(a).CompareTo(row(b));
                return byRow != 0 ? byRow : column(a).CompareTo(column(b));
            });
            return ordered;
        }

        private static int RequiredWidgetRows()
        {
            int rows = 1;
            foreach (CustomPlantDef def in SelectableDefs())
            {
                rows = Math.Max(rows, def.ChooserRow + 1);
            }
            return rows;
        }

        private static int ColX(int column)
        {
            return column * (Sexy.Constants.SMALL_SEEDPACKET_WIDTH +
                             Sexy.Constants.SEED_PACKET_HORIZ_GAP);
        }

        private static int RowY(int row)
        {
            return row * (Sexy.Constants.SMALL_SEEDPACKET_HEIGHT +
                          Sexy.Constants.SEED_PACKET_VERT_GAP) +
                   SeedPacketsWidget.SEEDPACKETS_MARGIN_UP;
        }

        /// <summary>
        /// 选卡格在 SeedChooserScreen 自己那层坐标里的位置。飞行中的卡画在这一层，
        /// 拿 widget 局部坐标当起点会横向瞬移 SCROLL_AREA_OFFSET_X；两个 GetAbsPos 相减
        /// 既带上滚动条位移，也不用猜 SCROLL_AREA_OFFSET 这套常量。
        /// </summary>
        private static (int x, int y) ChooserCellPos(SeedChooserScreen chooser, int column, int row)
        {
            SeedPacketsWidget widget = chooser.mSeedPacketsWidget;
            if (widget == null)
            {
                return (ColX(column), RowY(row));
            }
            CGPoint widgetAbs = widget.GetAbsPos();
            CGPoint chooserAbs = chooser.GetAbsPos();
            return ((int)(widgetAbs.x - chooserAbs.x) + ColX(column),
                (int)(widgetAbs.y - chooserAbs.y) + RowY(row));
        }

        // ---------------------------------------------------------------- 选卡网格

        public static void EnsureImitaterWidget(SeedPacketsWidget widget)
        {
            if (widget == null || !widget.mImitaters)
            {
                return;
            }
            ResizeWidgetRows(widget, Math.Max(16, RequiredWidgetRows()));
        }

        private static void ResizeWidgetRows(SeedPacketsWidget widget, int rows)
        {
            if (widget.mRows >= rows)
            {
                return;
            }
            widget.mRows = rows;
            widget.mHeight = Sexy.Constants.SMALL_SEEDPACKET_HEIGHT * rows +
                (rows - 1) * Sexy.Constants.SEED_PACKET_VERT_GAP +
                SeedPacketsWidget.SEEDPACKETS_MARGIN_UP;
            if (widget.mParent is ScrollWidget scroll)
            {
                scroll.ClientSizeChanged();
            }
        }

        public static void EnsureEntries(SeedChooserScreen chooser)
        {
            if (chooser == null)
            {
                return;
            }
            CustomPlantRegistry.EnsureDefinitions();
            int neededRows = Math.Max(14, RequiredWidgetRows());
            foreach (CustomPlantDef def in SelectableDefs())
            {
                int seedId = def.SeedId;
                if (chooser.mChosenSeeds.Length <= seedId)
                {
                    ChosenSeed[] grown = new ChosenSeed[seedId + 1];
                    Array.Copy(chooser.mChosenSeeds, grown, chooser.mChosenSeeds.Length);
                    chooser.mChosenSeeds = grown;
                }
                neededRows = Math.Max(neededRows, def.ChooserRow + 1);
                if (chooser.mChosenSeeds[seedId] != null)
                {
                    continue;
                }
                ChosenSeed chosen = new ChosenSeed
                {
                    mSeedType = def.Seed,
                    mImitaterType = SeedType.None,
                    mSeedState = ChosenSeedState.SEED_IN_CHOOSER,
                    mSeedIndexInBank = 0,
                    mRefreshCounter = 0,
                    mRefreshing = false,
                    mCrazyDavePicked = false,
                };
                chosen.mX = chosen.mStartX = chosen.mEndX = ColX(def.ChooserColumn);
                chosen.mY = chosen.mStartY = chosen.mEndY = RowY(def.ChooserRow);
                chosen.mTimeStartMotion = chosen.mTimeEndMotion = 0;
                chooser.mChosenSeeds[seedId] = chosen;
            }
            if (chooser.mSeedPacketsWidget != null)
            {
                int before = chooser.mSeedPacketsWidget.mRows;
                ResizeWidgetRows(chooser.mSeedPacketsWidget, neededRows);
                if (chooser.mSeedPacketsWidget.mRows != before)
                {
                    CacheScrollValues(chooser.mScrollWidget);
                }
            }
        }

        private static void CacheScrollValues(ScrollWidget scroll)
        {
            if (scroll == null)
            {
                return;
            }
            MethodInfo cache = scroll.GetType().GetMethod("CacheDerivedValues",
                BindingFlags.Instance | BindingFlags.NonPublic);
            cache?.Invoke(scroll, null);
        }

        // ------------------------------------------------------------ 升级卡（紫卡）限制

        /// <summary>
        /// 原生紫卡的限制在 <c>SeedChooserScreen.CheckSeedUpgrade(id, to, from)</c>（1480），
        /// 由 OnStartButton 末尾那一长串条件调用（1072）：只带了升级卡、没带基础卡就按开始，
        /// 弹一次"要不要回去重选"。这里给模组升级卡补同一道门。
        /// 不直接调那个方法是因为它靠 <c>PickedPlantType</c> 判定，而它只扫 0..53
        /// （<c>SeedsInChooserCount = ExplodeONut</c>），模组种子的下标在 65 往上，根本不在射程里。
        /// 告警槽位用 19：<c>mPickWarningsWaved</c> 是 bool[20]，原生占了 0..18。
        /// 返回 true = 弹窗已经挂上，这一下不给他开始。
        /// </summary>
        public static bool BlockStartForCustomUpgrades(SeedChooserScreen chooser)
        {
            if (chooser == null || chooser.mApp.IsSurvivalMode())
            {
                return false;
            }
            foreach (CustomPlantDef card in CustomPlantRegistry.Defs)
            {
                if (!card.IsUpgradeCard || !IsPickedInBank(chooser, card))
                {
                    continue;
                }
                CustomPlantDef baseDef = CustomPlantRegistry.UpgradedFrom(card);
                if (baseDef == null || IsPickedInBank(chooser, baseDef))
                {
                    continue;
                }
                // 引擎那条文案是 TodStringFile.TodStringTranslate("[SEED_CHOOSER_UPGRADE_WARNING]")
                // 再替换两个占位；TodStringFile 是 internal 类，模组够不着，而公开的
                // TodCommon.TodReplaceString 内部第一件事就是翻译传入串，所以直接拿它当翻译用，
                // 出来的字和原生紫卡一模一样。
                string text = TodCommon.TodReplaceString(
                    TodCommon.TodReplaceString("[SEED_CHOOSER_UPGRADE_WARNING]",
                        "{UPGRADE_TO}", Plant.GetNameString(card.Seed, SeedType.None)),
                    "{UPGRADE_FROM}", Plant.GetNameString(baseDef.Seed, SeedType.None));
                // 已经点过"不管了"的原生同一槽位会直接放行，不再弹。
                if (chooser.DisplayRepickWarningDialog(19, text))
                {
                    continue;
                }
                return true;
            }
            return false;
        }

        /// <summary>这张模组卡此刻在不在卡槽里（模仿者带着它也算，和 PickedPlantType 一致）。</summary>
        private static bool IsPickedInBank(SeedChooserScreen chooser, CustomPlantDef def)
        {
            ChosenSeed[] chosen = chooser.mChosenSeeds;
            for (int i = 0; i < chosen.Length; i++)
            {
                ChosenSeed seed = chosen[i];
                if (seed == null || seed.mSeedState != ChosenSeedState.SEED_IN_BANK)
                {
                    continue;
                }
                if (seed.mSeedType == def.Seed || seed.mImitaterType == def.Seed)
                {
                    return true;
                }
            }
            return false;
        }

        public static void DrawWidget(SeedPacketsWidget widget, Graphics g)
        {
            EnsureImitaterWidget(widget);
            SeedChooserScreen chooser = widget.mApp.mSeedChooserScreen;
            if (chooser == null)
            {
                return;
            }
            EnsureEntries(chooser);
            foreach (CustomPlantDef def in InDrawOrder(d => d.ChooserRow, d => d.ChooserColumn))
            {
                ChosenSeed chosen = chooser.mChosenSeeds[def.SeedId];
                if (chosen == null)
                {
                    continue;
                }
                int x = ColX(def.ChooserColumn);
                int y = RowY(def.ChooserRow);
                bool available = chosen.mSeedState == ChosenSeedState.SEED_IN_CHOOSER;
                SeedType packetType = widget.mImitaters ? SeedType.Imitater : def.Seed;
                SeedType imitaterType = widget.mImitaters ? def.Seed : SeedType.None;
                CustomUi.DrawSmallSeedPacket(g, x, y, packetType, imitaterType,
                    0f, widget.mImitaters || available ? 255 : 55,
                    true, false, true, true);
            }
        }

        public static bool HandleWidgetMouseUp(SeedPacketsWidget widget, int x, int y)
        {
            SeedChooserScreen chooser = widget.mApp.mSeedChooserScreen;
            if (chooser == null)
            {
                return false;
            }
            EnsureImitaterWidget(widget);
            EnsureEntries(chooser);
            int width = Sexy.Constants.SMALL_SEEDPACKET_WIDTH;
            int height = Sexy.Constants.SMALL_SEEDPACKET_HEIGHT;
            foreach (CustomPlantDef def in SelectableDefs())
            {
                ChosenSeed chosen = chooser.mChosenSeeds[def.SeedId];
                if (chosen == null)
                {
                    continue;
                }
                int chooserX = ColX(def.ChooserColumn);
                int chooserY = RowY(def.ChooserRow);
                bool selectable = widget.mImitaters ||
                                  chosen.mSeedState == ChosenSeedState.SEED_IN_CHOOSER;
                if (selectable && chooserX <= x && x < chooserX + width &&
                    chooserY <= y && y < chooserY + height)
                {
                    if (widget.mImitaters)
                    {
                        widget.mListener?.SeedSelected(def.Seed);
                    }
                    else
                    {
                        chooser.SeedSelected(def.Seed);
                    }
                    return true;
                }
            }
            return false;
        }

        public static void OnSeedSelected(SeedChooserScreen chooser, SeedType seed)
        {
            if (!CustomPlantRegistry.IsCustomSeed(seed) || chooser == null)
            {
                return;
            }
            int seedId = (int)seed;
            if (seedId >= chooser.mChosenSeeds.Length)
            {
                return;
            }
            ChosenSeed chosen = chooser.mChosenSeeds[seedId];
            if (chosen == null || chosen.mSeedState != ChosenSeedState.SEED_IN_CHOOSER ||
                chooser.mSeedsInBank == chooser.mBoard.mSeedBank.mNumPackets)
            {
                return;
            }
            CustomPlantDef selected = CustomPlantRegistry.GetDef(seed);
            (int cellX, int cellY) = ChooserCellPos(chooser, selected.ChooserColumn,
                selected.ChooserRow);
            chosen.mTimeStartMotion = chooser.mSeedChooserAge;
            chosen.mTimeEndMotion = chooser.mSeedChooserAge + 25;
            chosen.mStartX = cellX;
            chosen.mStartY = cellY;
            chosen.mEndY = chooser.mBoard.mSeedBank.mY +
                chooser.mBoard.GetSeedPacketPositionY(chooser.mSeedsInBank) - chooser.mY;
            chosen.mEndX = chooser.mBoard.mSeedBank.mX;
            chosen.mSeedState = ChosenSeedState.SEED_FLYING_TO_BANK;
            chosen.mSeedIndexInBank = chooser.mSeedsInBank;
            chooser.mSeedsInFlight++;
            chooser.mSeedsInBank++;
            chooser.mApp.PlaySample(Resources.SOUND_TAP);
            if (chooser.mSeedsInBank == chooser.mBoard.mSeedBank.mNumPackets)
            {
                chooser.EnableStartButton(true);
            }
        }

        /// <summary>
        /// 飞行中的卡按原生那样在 DrawOverlay 这一层画（原生 Draw 里只 DeferOverlay）：
        /// 坐标是 chooser 本地坐标，画进 SeedPacketsWidget 会被列表裁掉半张，
        /// 画在 Draw 里又会垫在未选卡片的下面。
        /// </summary>
        public static void DrawChooser(SeedChooserScreen chooser, Graphics g)
        {
            if (chooser == null || chooser.mChosenSeeds.Length <= 65)
            {
                return;
            }
            bool clippedToScroll = false;
            foreach (CustomPlantDef def in SelectableDefs())
            {
                if (chooser.mChosenSeeds.Length <= def.SeedId)
                {
                    continue;
                }
                ChosenSeed chosen = chooser.mChosenSeeds[def.SeedId];
                if (chosen == null)
                {
                    continue;
                }
                if (chosen.mSeedState == ChosenSeedState.SEED_FLYING_TO_BANK)
                {
                    g.SetClipRect(0, 0, chooser.mWidth, chooser.mHeight);
                }
                else if (chosen.mSeedState == ChosenSeedState.SEED_FLYING_TO_CHOOSER)
                {
                    g.SetClipRect(0, 0, chooser.mWidth,
                        chooser.mScrollWidget.mY + chooser.mScrollWidget.mHeight);
                    g.HardwareClip();
                    clippedToScroll = true;
                }
                else
                {
                    continue;
                }
                // 与原生 DrawOverlay 那一句同参：canShowName 默认为真，飞行途中名字照样跟着飞。
                CustomUi.DrawSmallSeedPacket(g, chosen.mX, chosen.mY,
                    def.Seed, SeedType.None, 0f, 255, true, false, true, true);
            }
            if (clippedToScroll)
            {
                g.EndHardwareClip();
            }
            g.SetColorizeImages(false);
        }

        public static void UpdateChooser(SeedChooserScreen chooser)
        {
            CustomPlantRegistry.EnsureDefinitions();
            EnsureEntries(chooser);
            SeedChooserScreen chooserRef = chooser;
            if (ScrollReady.Add(chooserRef) && chooser.mChosenSeeds.Length > 65 &&
                chooser.mChosenSeeds[65] != null)
            {
                ChosenSeed first = chooser.mChosenSeeds[65];
                chooser.mScrollWidget.ScrollRectIntoView(
                    new TRect(first.mX, first.mY,
                        Sexy.Constants.SMALL_SEEDPACKET_WIDTH,
                        Sexy.Constants.SMALL_SEEDPACKET_HEIGHT), false);
            }
            foreach (CustomPlantDef def in SelectableDefs())
            {
                if (chooser.mChosenSeeds.Length <= def.SeedId)
                {
                    continue;
                }
                ChosenSeed chosen = chooser.mChosenSeeds[def.SeedId];
                if (chosen == null || (chosen.mSeedState != ChosenSeedState.SEED_FLYING_TO_BANK &&
                    chosen.mSeedState != ChosenSeedState.SEED_FLYING_TO_CHOOSER))
                {
                    continue;
                }
                chosen.mX = TodCommon.TodAnimateCurve(
                    chosen.mTimeStartMotion, chosen.mTimeEndMotion, chooser.mSeedChooserAge,
                    chosen.mStartX, chosen.mEndX, TodCurves.EaseInOut);
                chosen.mY = TodCommon.TodAnimateCurve(
                    chosen.mTimeStartMotion, chosen.mTimeEndMotion, chooser.mSeedChooserAge,
                    chosen.mStartY, chosen.mEndY, TodCurves.EaseInOut);
                if (chooser.mSeedChooserAge < chosen.mTimeEndMotion)
                {
                    continue;
                }
                bool returning = chosen.mSeedState == ChosenSeedState.SEED_FLYING_TO_CHOOSER;
                ChosenSeed local = chosen;
                chooser.LandFlyingSeed(ref local);
                if (returning)
                {
                    chosen.mX = ColX(def.ChooserColumn);
                    chosen.mY = RowY(def.ChooserRow);
                }
            }
        }

        public static bool HandleChooserMouseDown(SeedChooserScreen chooser, int x, int y)
        {
            EnsureEntries(chooser);
            ChosenSeed chosen = null;
            int chosenId = -1;
            foreach (CustomPlantDef def in SelectableDefs())
            {
                if (chooser.mChosenSeeds.Length <= def.SeedId)
                {
                    continue;
                }
                ChosenSeed candidate = chooser.mChosenSeeds[def.SeedId];
                if (candidate == null || candidate.mSeedState != ChosenSeedState.SEED_IN_BANK)
                {
                    continue;
                }
                if (candidate.mX <= x && x < candidate.mX + Sexy.Constants.SMALL_SEEDPACKET_WIDTH &&
                    candidate.mY <= y && y < candidate.mY + Sexy.Constants.SMALL_SEEDPACKET_HEIGHT)
                {
                    chosen = candidate;
                    chosenId = def.SeedId;
                    break;
                }
            }
            if (chosen == null)
            {
                return false;
            }
            int removedIndex = chosen.mSeedIndexInBank;
            for (int bankIndex = removedIndex + 1;
                 bankIndex < chooser.mBoard.mSeedBank.mNumPackets; bankIndex++)
            {
                SeedType seedType = chooser.FindSeedInBank(bankIndex);
                if (seedType == SeedType.None || (int)seedType >= chooser.mChosenSeeds.Length)
                {
                    continue;
                }
                ChosenSeed shifted = chooser.mChosenSeeds[(int)seedType];
                if (shifted == null)
                {
                    continue;
                }
                shifted.mTimeStartMotion = chooser.mSeedChooserAge;
                shifted.mTimeEndMotion = chooser.mSeedChooserAge + 15;
                shifted.mStartX = shifted.mX;
                shifted.mStartY = shifted.mY;
                shifted.mEndX = chooser.mBoard.mSeedBank.mX;
                shifted.mEndY = chooser.mBoard.mSeedBank.mY +
                    chooser.mBoard.GetSeedPacketPositionY(bankIndex - 1) - chooser.mY;
                shifted.mSeedState = ChosenSeedState.SEED_FLYING_TO_BANK;
                shifted.mSeedIndexInBank = bankIndex - 1;
                chooser.mSeedsInFlight++;
            }
            CustomPlantDef defChosen = CustomPlantRegistry.GetDef((SeedType)chosenId);
            (int cellX, int cellY) = ChooserCellPos(chooser, defChosen.ChooserColumn,
                defChosen.ChooserRow);
            chosen.mTimeStartMotion = chooser.mSeedChooserAge;
            chosen.mTimeEndMotion = chooser.mSeedChooserAge + 25;
            chosen.mStartX = chosen.mX;
            chosen.mStartY = chosen.mY;
            chosen.mEndX = cellX;
            chosen.mEndY = cellY;
            chosen.mSeedState = ChosenSeedState.SEED_FLYING_TO_CHOOSER;
            chosen.mSeedIndexInBank = 0;
            chooser.mSeedsInFlight++;
            chooser.mSeedsInBank--;
            chooser.EnableStartButton(false);
            chooser.mApp.PlaySample(Resources.SOUND_TAP);
            return true;
        }

        public static SeedType FindSeedInBank(SeedChooserScreen chooser, int bankIndex)
        {
            foreach (CustomPlantDef def in SelectableDefs())
            {
                if (chooser.mChosenSeeds.Length <= def.SeedId)
                {
                    continue;
                }
                ChosenSeed chosen = chooser.mChosenSeeds[def.SeedId];
                if (chosen != null && chosen.mSeedState == ChosenSeedState.SEED_IN_BANK &&
                    chosen.mSeedIndexInBank == bankIndex)
                {
                    return def.Seed;
                }
            }
            return SeedType.None;
        }

        /// <summary>
        /// 关选卡。原生那段对每个卡槽都用 FindSeedInBank 的结果去索引 mChosenSeeds：
        /// 空卡槽返回 None（这个构建里是 -1）会直接越界，自定义种子也不在原生 65 格里。
        /// 所以这里按同样的顺序自己填卡槽，再走原版收尾的那两步。
        /// </summary>
        public static void CloseChooser(SeedChooserScreen chooser,
            Action<SeedChooserScreen> orig)
        {
            SeedBank bank = chooser?.mBoard?.mSeedBank;
            if (bank == null || bank.mSeedPackets == null)
            {
                orig(chooser);
                return;
            }
            int count = Math.Min(bank.mNumPackets, bank.mSeedPackets.Length);
            for (int i = 0; i < count; i++)
            {
                SeedPacket packet = bank.mSeedPackets[i];
                if (packet == null)
                {
                    continue;
                }
                SeedType custom = FindSeedInBank(chooser, i);
                if (custom != SeedType.None)
                {
                    packet.SetPacketType(custom, SeedType.None);
                    packet.mActive = true;
                    packet.mRefreshing = false;
                    packet.mRefreshCounter = 0;
                    continue;
                }
                SeedType native = FindNativeSeedInBank(chooser, i);
                int nativeId = (int)native;
                // 这个构建里 SeedType.None 是 -1；0 是豌豆射手，不能当空卡处理。
                if (nativeId < 0 || nativeId >= chooser.mChosenSeeds.Length)
                {
                    // 这一格没选卡：直接写字段，别调 SetPacketType —— 它会去查
                    // GetPlantDefinition(None=-1) 然后越界。
                    packet.mPacketType = SeedType.None;
                    packet.mImitaterType = SeedType.None;
                    packet.mActive = true;
                    packet.mRefreshing = false;
                    packet.mRefreshCounter = 0;
                    continue;
                }
                ChosenSeed chosen = chooser.mChosenSeeds[nativeId];
                SeedType imitater = chosen == null ? SeedType.None : chosen.mImitaterType;
                packet.SetPacketType(native, imitater);
                if (chosen != null && chosen.mRefreshing)
                {
                    packet.mRefreshCounter = chosen.mRefreshCounter;
                    packet.mRefreshTime = Plant.GetRefreshTime(
                        packet.mPacketType, packet.mImitaterType);
                    packet.mRefreshing = true;
                    packet.mActive = false;
                }
            }
            chooser.SaveToSeedGroup(chooser.mApp.mPlayerInfo.mCardGroup[0]);
            chooser.mBoard.mDoPlaceRose = chooser.mRoseButton.mChecked;
            chooser.mBoard.mCutScene.EndSeedChooser();
        }

        /// <summary>只在原生 65 格里找卡槽对应的种子，跳过自定义（它们不在这个数组里）。</summary>
        private static SeedType FindNativeSeedInBank(SeedChooserScreen chooser, int bankIndex)
        {
            int limit = Math.Min(chooser.mChosenSeeds.Length, 65);
            for (int i = 0; i < limit; i++)
            {
                ChosenSeed chosen = chooser.mChosenSeeds[i];
                if (chosen != null && chosen.mSeedState == ChosenSeedState.SEED_IN_BANK &&
                    chosen.mSeedIndexInBank == bankIndex)
                {
                    return (SeedType)i;
                }
            }
            return SeedType.None;
        }

        // ---------------------------------------------------------------- 往返飞行与卡组

        private static int CardGroupColumnX(CardGroupWidget widget, int groupIndex)
        {
            int stride = Sexy.Constants.SEED_PACKET_HORIZ_GAP +
                         Sexy.Constants.SMALL_SEEDPACKET_WIDTH;
            if (widget.mCols == 1 && groupIndex == 1)
            {
                return stride * 3 / 2;
            }
            if (widget.mCols == 2)
            {
                return stride * (groupIndex == 1 ? 3 : 9) / 4;
            }
            if (widget.mCols == 3 && groupIndex >= 1 && groupIndex <= 3)
            {
                return stride * (groupIndex * 2 - 1) / 2;
            }
            if (widget.mCols == 4 && groupIndex >= 1 && groupIndex <= 4)
            {
                return stride * (groupIndex - 1);
            }
            return 0;
        }

        private static (SeedType seed, SeedType imitater) CardInfoSeedTypes(CardInfo card)
        {
            return card.mImitater
                ? (SeedType.Imitater, card.mSeedType)
                : (card.mSeedType, SeedType.None);
        }

        // ---------------------------------------------------------------- 生存模式重开

        internal sealed class HiddenPacket
        {
            public int BankIndex;
            public SeedPacket Packet;
            public SeedType Seed;
            public SeedType Imitater;
            public bool Refreshing;
            public int Counter;
        }

        internal static List<HiddenPacket> HideCustomSurvivalPackets(Board board)
        {
            var hidden = new List<HiddenPacket>();
            if (board?.mSeedBank == null)
            {
                return hidden;
            }
            for (int i = 0; i < board.mSeedBank.mNumPackets; i++)
            {
                SeedPacket packet = board.mSeedBank.mSeedPackets[i];
                if (CustomPlantRegistry.EffectiveCustomSeed(
                    packet.mPacketType, packet.mImitaterType) == SeedType.None)
                {
                    continue;
                }
                hidden.Add(new HiddenPacket
                {
                    BankIndex = i,
                    Packet = packet,
                    Seed = packet.mPacketType,
                    Imitater = packet.mImitaterType,
                    Refreshing = packet.mRefreshing,
                    Counter = packet.mRefreshCounter,
                });
                packet.mPacketType = SeedType.None;
            }
            return hidden;
        }

        internal static void RestoreCustomSurvivalPackets(List<HiddenPacket> hidden)
        {
            foreach (HiddenPacket item in hidden)
            {
                item.Packet.mPacketType = item.Seed;
                item.Packet.mImitaterType = item.Imitater;
            }
        }

        internal static void RestoreSurvivalChooserSelection(SeedChooserScreen chooser,
            List<HiddenPacket> hidden)
        {
            if (chooser == null || hidden.Count == 0)
            {
                return;
            }
            EnsureEntries(chooser);
            var refreshByIndex = new Dictionary<int, (bool refreshing, int counter)>();
            foreach (HiddenPacket item in hidden)
            {
                refreshByIndex[item.BankIndex] = (item.Refreshing, item.Counter);
            }
            int seedsInBank = 0;
            for (int bankIndex = 0; bankIndex < chooser.mBoard.mSeedBank.mNumPackets; bankIndex++)
            {
                SeedPacket packet = chooser.mBoard.mSeedBank.mSeedPackets[bankIndex];
                SeedType seedType = packet.mPacketType;
                if (seedType == SeedType.None)
                {
                    continue;
                }
                int seedId = seedType == SeedType.Imitater &&
                             CustomPlantRegistry.IsCustomSeed(packet.mImitaterType)
                    ? (int)SeedType.Imitater
                    : (int)seedType;
                if (seedId < 0 || seedId >= chooser.mChosenSeeds.Length)
                {
                    continue;
                }
                ChosenSeed chosen = chooser.mChosenSeeds[seedId];
                if (chosen == null)
                {
                    continue;
                }
                chosen.mX = 0;
                chosen.mY = chooser.mBoard.GetSeedPacketPositionY(bankIndex);
                chosen.mStartX = chosen.mEndX = chosen.mX;
                chosen.mStartY = chosen.mEndY = chosen.mY;
                chosen.mSeedState = ChosenSeedState.SEED_IN_BANK;
                chosen.mSeedIndexInBank = bankIndex;
                chosen.mImitaterType = packet.mImitaterType;
                if (refreshByIndex.TryGetValue(bankIndex, out (bool refreshing, int counter) refresh))
                {
                    chosen.mRefreshing = refresh.refreshing;
                    chosen.mRefreshCounter = refresh.counter;
                }
                seedsInBank++;
            }
            chooser.mSeedsInBank = seedsInBank;
            chooser.mSeedsInFlight = 0;
            chooser.EnableStartButton(seedsInBank == chooser.mBoard.mSeedBank.mNumPackets);
        }

        // ---------------------------------------------------------------- 图鉴

        public static TRect AlmanacGridRect(int row, int column)
        {
            int x = Sexy.Constants.Almanac_SeedOffset.X +
                (column - 1) * (Sexy.Constants.SMALL_SEEDPACKET_WIDTH +
                                Sexy.Constants.Almanac_SeedSpace.X);
            int y = Sexy.Constants.Almanac_SeedOffset.X +
                (row - 1) * (Sexy.Constants.SMALL_SEEDPACKET_HEIGHT +
                             Sexy.Constants.Almanac_SeedSpace.Y);
            return new TRect(x, y, Sexy.Constants.SMALL_SEEDPACKET_WIDTH,
                Sexy.Constants.SMALL_SEEDPACKET_HEIGHT);
        }

        public static void PrepareGallery(PlantGalleryWidget gallery)
        {
            CustomPlantRegistry.EnsureDefinitions();
            int requiredHeight = Sexy.Constants.Almanac_ImitatorPosition.Y +
                Sexy.Constants.SMALL_SEEDPACKET_HEIGHT;
            foreach (CustomPlantDef def in SelectableDefs())
            {
                requiredHeight = Math.Max(requiredHeight,
                    AlmanacGridRect(def.AlmanacRow, def.AlmanacColumn).mY +
                    Sexy.Constants.SMALL_SEEDPACKET_HEIGHT);
            }
            if (gallery.mHeight < requiredHeight && gallery.mDialog != null)
            {
                gallery.mHeight = requiredHeight;
                gallery.mDialog.mPlantsScrollWidget?.ClientSizeChanged();
            }
        }

        public static void DrawGalleryEntries(PlantGalleryWidget gallery, Graphics g)
        {
            CustomPlantRegistry.EnsureDefinitions();
            foreach (CustomPlantDef def in InDrawOrder(d => d.AlmanacRow, d => d.AlmanacColumn))
            {
                TRect rect = AlmanacGridRect(def.AlmanacRow, def.AlmanacColumn);
                SeedPacket.DrawSmallSeedPacket(g, rect.mX, rect.mY, def.Seed,
                    SeedType.None, 0f, 255, true, false, true, true);
            }
        }

        public static bool HandleGalleryMouseUp(PlantGalleryWidget gallery, int x, int y)
        {
            CustomPlantRegistry.EnsureDefinitions();
            foreach (CustomPlantDef def in SelectableDefs())
            {
                if (AlmanacGridRect(def.AlmanacRow, def.AlmanacColumn).Contains(x, y))
                {
                    SetupAlmanacPage(gallery.mDialog, def);
                    return true;
                }
            }
            return false;
        }

        private static void SetupAlmanacPage(AlmanacDialog dialog, CustomPlantDef def)
        {
            CustomPlantRegistry.EnsureDefinitions();
            dialog.mSelectedSeed = def.Seed;
            dialog.SetupPlant();
            // 借原生身体的植物（如示例植物射手）用自定义种子建出来的预览会画不出来，
            // 用原生种子重建一次实体；标题/描述仍按模组种子走，所以还是模组名字。
            Plant preview = dialog.mPlant;
            if (preview != null && def.CardNativeSeed != SeedType.None)
            {
                preview.PlantInitialize(0, 0, def.CardNativeSeed, SeedType.None);
                preview.mX = (int)Sexy.Constants.Almanac_PlantPosition.X;
                preview.mY = (int)Sexy.Constants.Almanac_PlantPosition.Y;
            }
            CustomPlantBase behavior = preview == null
                ? null
                : CustomPlantRegistry.Get(preview);
            behavior?.OnAlmanacPreview();
            string text = def.AlmanacText ?? string.Empty;
            if (def.Deprecated)
            {
                text += "\n【已废弃】";
            }
            dialog.mDescriptionWidget.SetText(ref text);
            dialog.mDescriptionScrollWidget.ClientSizeChanged();
            dialog.mDescriptionScrollWidget.ScrollToMin(false);
        }
    }
}
