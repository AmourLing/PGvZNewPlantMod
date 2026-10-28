using System;
using System.Collections.Generic;
using System.Reflection;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>卡片、种子预览、光标预览、费用与名字等通用绘制。</summary>
    public static class CustomUi
    {

        /// <summary>框架自带的通用卡槽背景（Assets/card_background_*.png）。</summary>
        public const string CardBackgroundAssetPrefix = "card_background_";

        /// <summary>
        /// 模仿者选卡栏（ImitaterDialog）里原生卡不是换了一张灰卡，而是把整张卡图过一遍
        /// WashedOut 滤镜（亮度 ×1.8、饱和度 ×0.2）。自绘的通用背景和卡图也走同一个滤镜，
        /// 才会和同窗口的原生卡一个颜色。
        /// 注意：这里每帧都要真的调 FilterEffectGetImage，它会把生成的纹理登记进
        /// gFilterCacheThisFrame；缓存返回值会让 FilterEffectProcessDeleteQueue 回收掉那张纹理。
        /// </summary>
        private static Image WashedOut(Image image)
        {
            return image == null ? null :
                FilterEffect.FilterEffectGetImage(image, FilterEffectType.WashedOut);
        }

        private static void DrawCardBackground(Graphics g, CustomPlantDef def, float x, float y,
            bool asImitater)
        {
            if (string.IsNullOrEmpty(def.CardBackground) ||
                !AssetStore.TryGet(CardBackgroundAssetPrefix + def.CardBackground,
                    out Image back) || back == null)
            {
                return;
            }
            // 不动着色状态：外层已经按 grayness 设好颜色，背景才能和卡图一起变暗。
            g.DrawImage(asImitater ? WashedOut(back) : back, x, y);
        }

        private static readonly SeedType[] TachieSeeds =
        {
            SeedType.Imitater, SeedType.Cattail, SeedType.Spikerock, SeedType.Gatlingpea,
        };

        private static readonly Dictionary<CustomPlantDef, SpineWidget> CardTachies =
            new Dictionary<CustomPlantDef, SpineWidget>();
        private static readonly HashSet<CustomPlantDef> CardTachieFailed =
            new HashSet<CustomPlantDef>();

        /// <summary>
        /// 原生 DrawDynamicTachieSmallSeedPacket 里的基准参数（SeedPacket.cs:531）：
        /// 资源名、待机轨道、是否 Skel 格式、缩放、骨架基准偏移。
        /// 这里单独抄一份是为了能在它之上叠 CardArtScale / CardArtDx / CardArtDy。
        /// </summary>
        private static bool TachieLayout(SeedType seed, out string asset, out string track,
            out bool skel, out float scale, out float baseX, out float baseY)
        {
            asset = null;
            track = null;
            skel = false;
            scale = 1f;
            baseX = 0f;
            baseY = 0f;
            switch (seed)
            {
                case SeedType.Imitater:
                    asset = "imitater_tachie";
                    track = "idle";
                    scale = 1f / 12f;
                    baseX = 25f;
                    baseY = 202f;
                    return true;
                case SeedType.Cattail:
                    asset = "190306";
                    track = "wait1";
                    scale = 1f / 28f;
                    baseX = 25f;
                    baseY = 252f;
                    return true;
                case SeedType.Spikerock:
                    asset = "spikerock_tachie";
                    track = "idle2";
                    skel = true;
                    scale = 0.05f;
                    baseX = 30f;
                    baseY = 122f;
                    return true;
                case SeedType.Gatlingpea:
                    asset = "gatlingpea_tachie";
                    track = "idle2";
                    skel = true;
                    scale = 0.05f;
                    baseX = 12f;
                    baseY = 222f;
                    return true;
                default:
                    return false;
            }
        }

        private static SpineWidget CardTachie(CustomPlantDef def, out float baseX, out float baseY)
        {
            baseX = 0f;
            baseY = 0f;
            if (!TachieLayout(def.CardNativeSeed, out string asset, out string track,
                    out bool skel, out float scale, out baseX, out baseY))
            {
                return null;
            }
            if (CardTachies.TryGetValue(def, out SpineWidget cached))
            {
                return cached;
            }
            if (CardTachieFailed.Contains(def))
            {
                return null;
            }
            try
            {
                SpineWidget widget = skel
                    ? new SpineWidget(asset, asset, scale * def.CardArtScale,
                        SpineWidget.SkeletonFormat.Skel)
                    : new SpineWidget(asset, asset, scale * def.CardArtScale);
                widget.SetAnimationTrack(track, ReanimLoopType.Loop);
                CardTachies[def] = widget;
                return widget;
            }
            catch (Exception ex)
            {
                CardTachieFailed.Add(def);
                ModLog.Info("card tachie unavailable (" + asset + "): " + ex.Message +
                            " -> falls back to the native card");
                return null;
            }
        }

        /// <summary>把借用来的立绘画在卡上，位置/大小按 def.CardArtDx/CardArtDy/CardArtScale 调。</summary>
        private static bool DrawTachieCardArt(Graphics g, CustomPlantDef def, float x, float y)
        {
            SpineWidget widget = CardTachie(def, out float baseX, out float baseY);
            if (widget == null)
            {
                return false;
            }
            int lift = -Sexy.Constants.SMALL_SEEDPACKET_HEIGHT;
            widget.Resize((int)x + g.mTransX + 5, (int)y + g.mTransY + lift,
                Sexy.Constants.SMALL_SEEDPACKET_WIDTH - 12,
                Sexy.Constants.SMALL_SEEDPACKET_HEIGHT - 8 - lift);
            widget.SetSkeletonPos(baseX + def.CardArtDx, baseY + def.CardArtDy);
            widget.Update_FromDraw(GlobalStaticVars.gLawnApp.mAppCounter);
            widget.Draw(g);
            return true;
        }

        /// <summary>
        /// 原生有几张卡是"空白卡框 + 动态立绘"两层拼出来的。借这类卡当底图的模组植物
        /// 只要声明了通用背景，就只借它的立绘，卡框换成自己声明的色系：
        /// 创造神/卡片模仿者因此是绿底立绘，而模仿者自己的卡照旧用原生那张灰底。
        /// 返回 false 表示不是那种拼法（或立绘资源取不到），交回给原生整张画。
        /// </summary>
        private static bool TryDrawOwnFrameWithTachie(Graphics g, CustomPlantDef def,
            float x, float y, bool asImitater)
        {
            if (string.IsNullOrEmpty(def.CardBackground) ||
                Array.IndexOf(TachieSeeds, def.CardNativeSeed) < 0)
            {
                return false;
            }
            if (CardTachie(def, out _, out _) == null)
            {
                return false;
            }
            DrawCardBackground(g, def, x, y, asImitater);
            return DrawTachieCardArt(g, def, x, y);
        }

        private static readonly Dictionary<int, Reanimation> SeedPreviews =
            new Dictionary<int, Reanimation>();

        /// <summary>由钩子保存的原生绘制入口；用于没有自定义卡图/预览的植物回退。</summary>
        public static Action<Graphics, float, float, SeedType, SeedType, float, int,
            bool, bool, bool, bool, bool> NativeDrawSmallSeedPacket;
        public static Action<Graphics, SeedType, SeedType, DrawVariation, float, float>
            NativeDrawSeedType;

        public static bool IsCustomPacket(SeedType seed, SeedType imitater)
        {
            return CustomPlantRegistry.EffectiveCustomSeed(seed, imitater) != SeedType.None;
        }

        public static CustomPlantDef DefFor(SeedType seed, SeedType imitater)
        {
            SeedType custom = CustomPlantRegistry.EffectiveCustomSeed(seed, imitater);
            return custom == SeedType.None ? null : CustomPlantRegistry.GetDef(custom);
        }

        public static int CustomCost(SeedType seed, SeedType imitater, Board board)
        {
            CustomPlantDef def = DefFor(seed, imitater);
            if (def == null)
            {
                return -1;
            }
            if (def.CostProvider != null)
            {
                try
                {
                    return def.CostProvider(board);
                }
                catch (Exception ex)
                {
                    ModLog.Error("CostProvider failed for " + def.Name, ex);
                    return def.Cost;
                }
            }
            return def.Cost;
        }

        public static int CustomRefresh(SeedType customSeed)
        {
            CustomPlantDef def = CustomPlantRegistry.GetDef(customSeed);
            return def == null || def.RefreshTime <= 0 ? 750 : def.RefreshTime;
        }

        public static string CustomName(SeedType seed)
        {
            return CustomPlantRegistry.GetDef(seed)?.Name;
        }

        // ---------------------------------------------------------------- 种子卡预览

        public static void DrawSeedType(Graphics g, SeedType seedType, SeedType imitater,
            float posX, float posY)
        {
            CustomPlantDef def = DefFor(seedType, imitater);
            if (def == null)
            {
                return;
            }
            // 借原生身体的：交给原生按它的种子、尺度和高度偏移去画，
            // 位置才会和原版光标预览一致（自己算 40/64 会偏）。
            if (def.CardNativeSeed != SeedType.None && NativeDrawSeedType != null)
            {
                NativeDrawSeedType(g, def.CardNativeSeed, SeedType.None,
                    DrawVariation.Normal, posX, posY);
                return;
            }
            if (!def.HasPreview)
            {
                return;
            }
            CustomPlantRegistry.EnsureDefinitions();
            int key = def.SeedId;
            if (!SeedPreviews.TryGetValue(key, out Reanimation reanim) || reanim == null)
            {
                reanim = Reanimation.GetNewReanimation();
                reanim.ReanimationInitializeType(0f, 0f, def.ReanimType);
                reanim.PlayReanim(def.PreviewAnim, ReanimLoopType.Loop, 0, 30f);
                reanim.Update();
                SeedPreviews[key] = reanim;
            }
            // PreviewScale 默认 0（表示没配），按 1 画，否则预览完全看不见。
            float previewScale = def.PreviewScale > 0f ? def.PreviewScale : 1f;
            reanim.OverrideScale(previewScale * g.mScaleX, previewScale * g.mScaleY);
            // 原生给预览的锚点是 (40, 高度偏移+64)（实机量得 num=0 → (40,64)），
            // 而种到地上的身体画在 (0,0)；不抵消的话影子会整体往右下掉。
            float previewDx = def.PreviewDx != 0f ? def.PreviewDx : 40f;
            float previewDy = def.PreviewDy != 0f ? def.PreviewDy : 64f;
            reanim.SetPosition(posX - previewDx * g.mScaleX,
                posY - previewDy * g.mScaleY);
            // 预览只画植物本身：卡槽背景是"卡片"的东西，画进光标预览里
            // 会出现一个和植物错位的白框。
            reanim.Draw(g);
        }

        // ---------------------------------------------------------------- 种子卡

        public static void DrawSmallSeedPacket(Graphics g, float x, float y,
            SeedType seedType, SeedType imitaterType, float percentDark, int grayness,
            bool drawCost, bool useCurrentCost, bool drawBackground,
            bool drawCostBackground, bool canShowName = true)
        {
            CustomPlantDef def = DefFor(seedType, imitaterType);
            if (def == null)
            {
                return;
            }
            bool isImitater = seedType == SeedType.Imitater;
            Board board = GlobalStaticVars.gLawnApp == null ? null : GlobalStaticVars.gLawnApp.mBoard;
            string costText = CostText(def, useCurrentCost, board);

            Image card = null;
            bool hasCard = !string.IsNullOrEmpty(def.CardAsset) &&
                           AssetStore.TryGet(def.CardAsset, out card) && card != null;
            bool nativeCard = !hasCard && def.CardNativeSeed != SeedType.None &&
                              NativeDrawSmallSeedPacket != null;

            // 和原生 DrawSmallSeedPacket 同序：先复位颜色，着色一直开到方法结尾，
            // 通用背景、卡图和费用底板才一起变暗。
            g.SetColor(new SexyColor(255, 255, 255, 255).Color);
            if (grayness != 255)
            {
                g.SetColor(new SexyColor(grayness, grayness, grayness, 255).Color);
                g.SetColorizeImages(true);
            }
            else if (percentDark > 0f)
            {
                g.SetColor(new SexyColor(128, 128, 128, 255).Color);
                g.SetColorizeImages(true);
            }
            if (drawBackground)
            {
                if (hasCard)
                {
                    DrawCardBackground(g, def, x, y, isImitater);
                    Image art = isImitater ? WashedOut(card) : card;
                    float artX = x + def.CardArtDx;
                    float artY = y + def.CardOffsetY + def.CardArtDy;
                    if (def.CardArtScale == 1f)
                    {
                        g.DrawImage(art, artX, artY);
                    }
                    else
                    {
                        // SetScale 的 origin 是"图片宽高的比例"（0~1），不是像素：
                        // 传绝对坐标会把卡图整体甩出画面。0 = 左上角不动、只缩小，
                        // 位置仍完全由 CardOffsetY/CardArtDx/Dy 决定。
                        g.SetScale(def.CardArtScale, def.CardArtScale, 0f, 0f);
                        g.DrawImage(art, artX, artY);
                        g.SetScale(1f, 1f, 0f, 0f);
                    }
                }
                else if (nativeCard && TryDrawOwnFrameWithTachie(g, def, x, y, isImitater))
                {
                }
                else if (nativeCard)
                {
                    // 模仿者栏要把上下文原样交给原生：传 (Imitater, 本体) 才会走
                    // FilterEffect.WashedOut，画出来才是同窗口里那种灰底卡。
                    NativeDrawSmallSeedPacket(g, x, y,
                        isImitater ? SeedType.Imitater : def.CardNativeSeed,
                        isImitater ? def.CardNativeSeed : SeedType.None,
                        0f, grayness, false, false, true, false, false);
                }
                else
                {
                    g.SetColor(new SexyColor(grayness, grayness, grayness, 220).Color);
                    g.FillRect((int)x, (int)y, Sexy.Constants.SMALL_SEEDPACKET_WIDTH,
                        Sexy.Constants.SMALL_SEEDPACKET_HEIGHT);
                }
            }
            if (drawCostBackground)
            {
                // 借用原生卡时，原生绘制结尾会关掉着色；不重新打开的话，
                // 这张费用底板（和后面任何贴图）都是满亮的，选卡里会看到
                // "卡暗了、价格底板还白着"。
                if (grayness != 255 || percentDark > 0f)
                {
                    int shade = grayness != 255 ? grayness : 128;
                    g.SetColor(new SexyColor(shade, shade, shade, 255).Color);
                    g.SetColorizeImages(true);
                }
                g.DrawImageF(CostTabImage(def, seedType),
                    x + Sexy.Constants.SeedPacket_Cost.X,
                    y + Sexy.Constants.SeedPacket_Cost.Y);
            }
            if (percentDark > 0f)
            {
                int overlayHeight = (int)(Sexy.Constants.SMALL_SEEDPACKET_HEIGHT * percentDark) + 2;
                g.SetColor(new SexyColor(0, 0, 0, 150).Color);
                g.FillRect((int)x, (int)y, Sexy.Constants.SMALL_SEEDPACKET_WIDTH, overlayHeight);
            }
            if (drawBackground && canShowName && GlobalStaticVars.gLawnApp != null &&
                GlobalStaticVars.gLawnApp.mPlayerInfo != null &&
                GlobalStaticVars.gLawnApp.mPlayerInfo.mShowName)
            {
                // 走游戏自己的取名：模仿者卡会拿 [IMITATED_PLANT] 那条格式，和原生一致。
                DrawSeedPacketName(g, Plant.GetNameString(seedType, imitaterType),
                    new TRect(2 + (int)x, -19 + (int)y, 55, 80), grayness);
            }
            if (drawCost)
            {
                TodCommon.TodDrawString(g, costText,
                    (int)x + Sexy.Constants.SeedPacket_CostText_Pos.X,
                    (int)y + Sexy.Constants.SeedPacket_CostText_Pos.Y,
                    Resources.FONT_PICO129, SexyColor.Black,
                    DrawStringJustification.Right);
            }
            g.SetColorizeImages(false);
        }

        /// <summary>费用底板按原生同一套：模仿者灰，其余跟随卡片声明的通用背景色系。</summary>
        private static Image CostTabImage(CustomPlantDef def, SeedType seedType)
        {
            if (seedType == SeedType.Imitater)
            {
                return AtlasResources.IMAGE_SEEDPACKETS_GRAY_TAB;
            }
            switch (def.CardBackground)
            {
                case "purple":
                    return AtlasResources.IMAGE_SEEDPACKETS_PURPLE_TAB;
                case "gray":
                    return AtlasResources.IMAGE_SEEDPACKETS_GRAY_TAB;
                default:
                    return AtlasResources.IMAGE_SEEDPACKETS_GREEN_TAB;
            }
        }

        private static string CostText(CustomPlantDef def, bool useCurrentCost, Board board)
        {
            if (useCurrentCost && def.CostProvider != null)
            {
                try
                {
                    return def.CostProvider(board).ToString();
                }
                catch
                {
                    // fall through to static cost
                }
            }
            return def.Cost.ToString();
        }

        private static MethodInfo _wrappedMethod;
        private static bool _wrappedMethodChecked;

        private static void DrawSeedPacketName(Graphics g, string text, TRect rect, int grayness)
        {
            Font font = Resources.FONT_BRIANNETOD12;
            (float sx, float sy) = FontScaleHelper.Get(font);
            FontScaleHelper.Set(font, 0.5f, 0.5f);
            g.HardwareClip();
            try
            {
                (int dx, int dy)[] offsets =
                {
                    (0, 0), (1, 0), (0, 1), (-1, 0), (0, -1),
                };
                MethodInfo wrapped = GetWrappedMethod();
                for (int i = 4; i >= 0; i--)
                {
                    SexyColor color = i == 0
                        ? SexyColor.Black
                        : new SexyColor(grayness, grayness, grayness);
                    TRect drawRect = new TRect(rect.mX + offsets[i].dx,
                        rect.mY + offsets[i].dy, rect.mWidth, rect.mHeight);
                    if (wrapped != null)
                    {
                        wrapped.Invoke(null, new object[]
                        {
                            g, text, drawRect, font, color,
                            DrawStringJustification.CenterVerticalBottom, false,
                        });
                    }
                    else
                    {
                        Font oldFont = g.GetFont();
                        Microsoft.Xna.Framework.Color oldColor = g.mColor;
                        try
                        {
                            g.SetFont(font);
                            g.SetColor(color.Color);
                            g.WriteWordWrapped(drawRect, text, 0,
                                (int)DrawStringJustification.CenterVerticalBottom, true);
                        }
                        finally
                        {
                            g.SetFont(oldFont);
                            g.SetColor(oldColor);
                        }
                    }
                }
            }
            finally
            {
                g.EndHardwareClip();
                FontScaleHelper.Set(font, sx, sy);
            }
        }

        private static MethodInfo GetWrappedMethod()
        {
            if (_wrappedMethodChecked)
            {
                return _wrappedMethod;
            }
            _wrappedMethodChecked = true;
            Type type = typeof(Reanimation).Assembly.GetType("Sexy.TodLib.TodStringFile");
            if (type == null)
            {
                return null;
            }
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "TodDrawStringWrapped" && parameters.Length == 7 &&
                    parameters[6].ParameterType == typeof(bool))
                {
                    _wrappedMethod = method;
                    break;
                }
            }
            return _wrappedMethod;
        }

        /// <summary>
        /// 卡槽的灰与暗直接问引擎自己（<c>SeedPacket.GetGraynessAndDarkness</c>），
        /// 不再另写一套判定。以前那套漏了 <c>mApp.mEasyPlantingCheat</c>（ZBCheatDialog 的
        /// "无冷却种植"）那条分支：原生卡在作弊下永远满亮，模组卡种下却压着 128，
        /// 非得点一下（走下面"手持"豁免）才亮回来；开场动画变暗也同样漏。
        /// 只有"手上正举着这张"要挡一下：原生 DrawBackground/DrawOverlay 对手中那张
        /// 根本不算灰度，满亮画完再叠 IMAGE_SELECTED_PACKET，而选卡瞬间 MouseDown 已经
        /// Deactivate 把 mActive 置成了 false，照抄引擎的数就会一选中就变灰。
        /// </summary>
        private static void PacketShade(SeedPacket packet, out int grayness, out float percentDark)
        {
            grayness = 255;
            percentDark = 0f;
            if (packet == null || packet.mBoard == null || packet.mApp == null)
            {
                return;
            }
            CursorObject cursor = packet.mBoard.mCursorObject;
            if (cursor != null && cursor.mCursorType == CursorType.PlantFromBank &&
                cursor.mSeedBankIndex == packet.mIndex)
            {
                return;
            }
            packet.GetGraynessAndDarkness(ref grayness, ref percentDark);
        }

        /// <summary>给台架用的一对读数：模组卡此刻的灰度与暗度（都取自引擎自己的判定）。</summary>
        public static int PacketGrayness(SeedPacket packet)
        {
            PacketShade(packet, out int grayness, out float _);
            return grayness;
        }

        public static float PacketDarkness(SeedPacket packet)
        {
            PacketShade(packet, out int _, out float percentDark);
            return percentDark;
        }

        public static void DrawPacketBackground(SeedPacket packet, Graphics g)
        {
            PacketShade(packet, out int grayness, out float _);
            DrawSmallSeedPacket(g, 0f, packet.mOffsetY, packet.mPacketType,
                packet.mImitaterType, 0f, grayness,
                false, true, true, true);
        }

        public static void DrawPacketOverlay(SeedPacket packet, Graphics g)
        {
            PacketShade(packet, out int _, out float percentDark);
            if (percentDark > 0f)
            {
                // 冷却黑条的高度和"要不要画"都由引擎那个 percentDark 说了算，
                // 自己再按 mRefreshCounter 算一遍就会在作弊/开场动画里多画一截。
                int height = (int)(Sexy.Constants.SMALL_SEEDPACKET_HEIGHT * percentDark);
                if (height > 0)
                {
                    g.SetColor(new SexyColor(20, 20, 20, 170).Color);
                    g.FillRect(0, packet.mOffsetY, Sexy.Constants.SMALL_SEEDPACKET_WIDTH, height);
                }
            }
            // 原生 DrawOverlay 的选中高亮：手持中叠白色 IMAGE_SELECTED_PACKET；
            // 点不起（冷却/阳光不足）buzz 时叠同一张图的渐隐红框。原生在 Draw 里
            // 递减 mBuzzedCounter，这里接管了 DrawOverlay 就要一并接过来，否则红框不退。
            CursorObject cursor = packet.mBoard?.mCursorObject;
            bool held = cursor != null && cursor.mCursorType == CursorType.PlantFromBank &&
                        cursor.mSeedBankIndex == packet.mIndex;
            if (held)
            {
                DrawSelectedPacketOverlay(g, packet.mOffsetY,
                    new SexyColor(255, 255, 255, 180, premultiply: true));
            }
            if (packet.mBuzzedCounter > 0)
            {
                packet.mBuzzedCounter--;
                DrawSelectedPacketOverlay(g, packet.mOffsetY,
                    new SexyColor(255, 100, 100, 180 * packet.mBuzzedCounter / 25,
                        premultiply: true));
            }
        }

        /// <summary>原生 SeedPacket.DrawOverlay 里 IMAGE_SELECTED_PACKET 的画法，原样照搬。</summary>
        private static void DrawSelectedPacketOverlay(Graphics g, int offsetY, SexyColor color)
        {
            g.SetColorizeImages(true);
            g.SetColor(color.Color);
            g.DrawImage(AtlasResources.IMAGE_SELECTED_PACKET,
                Sexy.Constants.SeedPacket_Selector_Pos.X,
                offsetY - Sexy.Constants.SeedPacket_Selector_Pos.Y);
            g.SetColorizeImages(false);
        }

        /// <summary>
        /// 开局卡槽状态照原生 SeedPacket.SetPacketType：普通卡一进场就是满亮可用
        /// （mRefreshing=false / mActive=true / mRefreshTime=0，种下时 WasPlanted 自己按
        /// Plant.GetRefreshTime 填冷却），只有原生那两类例外才开局进冷却：
        /// 升级卡（原生是 IsUpgrade 那 9 个种子，我们按声明的紫色卡面 CardBackground="purple"）
        /// 和 5000 冷却那类（毁灭菇）。ReadyImmediately 再覆盖一层"连例外也开局可用"。
        /// </summary>
        public static void OnSetPacketType(SeedPacket packet, SeedType seed, SeedType imitater)
        {
            CustomPlantDef def = DefFor(seed, imitater);
            if (def == null || packet.mBoard == null ||
                packet.mBoard.HasConveyorBeltSeedBank() || packet.mApp.IsSlotMachineLevel())
            {
                return;
            }
            int refresh = def.RefreshTime > 0 ? def.RefreshTime : 750;
            bool startsCooling = !def.ReadyImmediately &&
                (def.CardBackground == "purple" || refresh == 5000);
            if (!startsCooling)
            {
                packet.mRefreshing = false;
                packet.mRefreshTime = 0;
                packet.mActive = true;
                return;
            }
            packet.mRefreshTime = packet.mApp.IsSurvivalMode() ? 8000 : 3500;
            packet.mRefreshing = true;
            packet.mActive = false;
        }

        // ---------------------------------------------------------------- 光标

        /// <summary>
        /// 光标层：原生这里画的是不透明的缓存植物图。锚点和原生一致，都是格子左上角
        /// （-格宽/2*S, -格高/2*S）；多减 40/64 会把贴图整体甩到鼠标左上角去。
        /// 借原生身体的直接取引擎缓存图；自绘动画的没有缓存，就画同一株的 preview reanim
        /// （不碰离屏渲染，不透明、静态）。
        /// </summary>
        public static void DrawCursorTopLayer(CursorObject cursor, Graphics g)
        {
            CustomPlantDef def = DefFor(cursor.mType, cursor.mImitaterType);
            if (def == null)
            {
                return;
            }
            // 原生 PlantFromBank/PlantFromUsableCoin 分支的整段门禁，原样照搬：
            // mX 是 Board 控件内坐标（控件本身横移了 Board_Offset_AspectRatio_Correction），
            // 加回偏移就是窗口 x；光标还在窗口左缘卡槽带（窗口 x < 卡宽）时不画跟随预览。
            // 不照搬的话模组卡一点开就顶着一张全尺寸缓存图糊在卡槽 UI 上，
            // 原生植物此刻是没有这个预览的。
            Board board = cursor.mBoard;
            if (board == null || board.mIgnoreMouseUp)
            {
                return;
            }
            bool conveyor = board.HasConveyorBeltSeedBank();
            if (!conveyor && cursor.mX + Sexy.Constants.Board_Offset_AspectRatio_Correction <
                Sexy.Constants.SMALL_SEEDPACKET_WIDTH)
            {
                return;
            }
            if (conveyor)
            {
                // 传送带关卡原生会把跟随图裁到卡槽右侧，避免盖住传送带。
                int camX = (int)((board.mCamera?.BoardToScreen(
                    Sexy.Constants.Board_Offset_AspectRatio_Correction, 0).mX ?? 0) /
                    Sexy.Constants.BoardCameraScaleMultiplier);
                g.SetClipRect(new TRect(-cursor.mX - camX + Sexy.Constants.New.SeedBank_Width,
                    -cursor.mY, 32767, 32767));
                g.HardwareClip();
            }
            try
            {
                float scale = Sexy.Constants.S;
                float posX = -(float)Sexy.Constants.New.Board_GridCellSizeX / 2f * scale;
                float posY = -(float)Sexy.Constants.New.Board_GridCellSizeY_6Rows / 2f * scale;
                if (def.CardNativeSeed != SeedType.None)
                {
                    ReanimatorCache cache = GlobalStaticVars.gLawnApp == null
                        ? null
                        : GlobalStaticVars.gLawnApp.mReanimatorCache;
                    if (cache != null)
                    {
                        cache.DrawCachedPlant(g, posX, posY, def.CardNativeSeed,
                            DrawVariation.Normal);
                    }
                }
                else if (def.HasPreview)
                {
                    CustomPlantRegistry.EnsureDefinitions();
                    if (!SeedPreviews.TryGetValue(def.SeedId, out Reanimation reanim) || reanim == null)
                    {
                        reanim = Reanimation.GetNewReanimation();
                        reanim.ReanimationInitializeType(0f, 0f, def.ReanimType);
                        reanim.PlayReanim(def.PreviewAnim, ReanimLoopType.Loop, 0, 30f);
                        reanim.Update();
                        SeedPreviews[def.SeedId] = reanim;
                    }
                    // 不每帧 Update：和原生缓存图一样是一张静态定格。
                    float previewScale = def.PreviewScale > 0f ? def.PreviewScale : 1f;
                    reanim.OverrideScale(previewScale, previewScale);
                    reanim.SetPosition(posX, posY);
                    reanim.Draw(g);
                }
                // 原生：悬在垃圾桶上时叠加叉号，表示这张卡会被丢掉。
                if (board.TrashcanHitTest(cursor.mX, cursor.mY))
                {
                    g.DrawImage(AtlasResources.IMAGE_SEEDPACKET_CROSS,
                        -AtlasResources.IMAGE_SEEDPACKET_CROSS.mWidth / 2,
                        -AtlasResources.IMAGE_SEEDPACKET_CROSS.mHeight / 2);
                }
            }
            finally
            {
                if (conveyor)
                {
                    g.EndHardwareClip();
                }
            }
        }

        // ---------------------------------------------------------------- 种子号/名字

        public static SeedType LegacySeedFromInt(int value)
        {
            return CustomPlantRegistry.IsCustomSeed((SeedType)value)
                ? (SeedType)value
                : SeedType.None;
        }

        private static class FontScaleHelper
        {
            private static readonly Dictionary<Type, PropertyInfo[]> ByType = new();

            private static PropertyInfo[] Pair(Font font)
            {
                Type type = font.GetType();
                if (!ByType.TryGetValue(type, out PropertyInfo[] pair))
                {
                    // mScaleX/Y 是实现类（StashFont/XNAFont）上的公开属性，
                    // 按 NonPublic 找基类会拿到 null，然后整个缩放就静默失效。
                    const BindingFlags flags = BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic;
                    pair = new[]
                    {
                        type.GetProperty("mScaleX", flags),
                        type.GetProperty("mScaleY", flags),
                    };
                    ByType[type] = pair;
                }
                return pair;
            }

            public static (float, float) Get(Font font)
            {
                PropertyInfo[] pair = Pair(font);
                if (pair[0] == null || pair[1] == null)
                {
                    return (1f, 1f);
                }
                return ((float)pair[0].GetValue(font), (float)pair[1].GetValue(font));
            }

            public static void Set(Font font, float x, float y)
            {
                PropertyInfo[] pair = Pair(font);
                pair[0]?.SetValue(font, x);
                pair[1]?.SetValue(font, y);
            }
        }
    }
}
