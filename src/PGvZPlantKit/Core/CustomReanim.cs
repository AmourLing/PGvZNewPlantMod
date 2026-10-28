using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 经典逐帧动画（原版 .reanim XML）导入器。
    ///
    /// 槽位登记在 CustomPlantRegistry：填 CustomPlantDef.ReanimName 即可，
    /// 定义文本走内嵌资源（Content/reanim/&lt;name&gt;.reanim），帧图按 IMAGE_XXX 的 ID
    /// 从 AssetStore 取插件内嵌 PNG。因此 Windows 和 Android 都不必往游戏 Content 里放文件。
    ///
    /// 解析规则与 Sexy.TodLib.ReanimXmlReader + ReanimatorXnaHelpers.ReanimationLoadDefinition
    /// 一致：缺省字段先写 -99999 占位，再按轨道把占位值续成上一条已填的值；
    /// 坐标按 doScale（默认 ScaleFromPC）乘 Constants.S，所以移动端缩放自动跟着引擎。
    /// </summary>
    public static class CustomReanim
    {
        private const float Placeholder = -99999f;

        private enum ScaleType
        {
            NoScale = 0,
            InvertAndScale = 1,
            ScaleFromPC = 255,
        }

        private static readonly Dictionary<int, string> Names =
            new Dictionary<int, string>();

        private static readonly Dictionary<int, ReanimatorDefinition> Built =
            new Dictionary<int, ReanimatorDefinition>();

        /// <summary>离线结构检查结果：轨道名、帧数和找不到贴图的帧图 ID。</summary>
        public sealed class Stats
        {
            public int Tracks;
            public int Frames;
            public float Fps;
            public readonly List<string> TrackNames = new List<string>();
            public readonly List<string> MissingImages = new List<string>();
            public bool Ok => Tracks > 0 && Frames > 0 && MissingImages.Count == 0;
        }

        /// <summary>登记槽位用哪个内嵌 .reanim；重新登记会丢掉已构造的定义。</summary>
        public static void Register(ReanimationType type, string name)
        {
            int key = (int)type;
            Names[key] = name;
            Built.Remove(key);
        }

        public static bool IsRegistered(ReanimationType type) =>
            Names.ContainsKey((int)type);

        /// <summary>
        /// 由 ReanimatorEnsureDefinitionLoaded 钩子调用：命中我们登记的槽位就写进游戏缓存，
        /// 让引擎跳过按文件名加载 .xnb 的那一步。构造失败时退回一个已有定义，
        /// 免得整表预加载直接抛异常。
        /// </summary>
        public static bool TrySupply(ReanimationType type)
        {
            if (!IsRegistered(type))
            {
                return false;
            }
            if (!TryGetDefinition(type, out ReanimatorDefinition definition))
            {
                definition = CachedDefinitionOf(ReanimationType.Peashooter);
                if (definition == null)
                {
                    return false;
                }
                ModLog.Error("reanim fallback to Peashooter: " + type);
            }
            return WriteCached(type, definition);
        }

        private static ReanimatorDefinition CachedDefinitionOf(ReanimationType type)
        {
            Array array = DefinitionArray();
            int index = (int)type;
            return array != null && index >= 0 && index < array.Length
                ? array.GetValue(index) as ReanimatorDefinition
                : null;
        }

        private static bool WriteCached(ReanimationType type, ReanimatorDefinition definition)
        {
            Array array = DefinitionArray();
            int index = (int)type;
            if (array == null || index < 0 || index >= array.Length)
            {
                return false;
            }
            array.SetValue(definition, index);
            return true;
        }

        private static Array DefinitionArray()
        {
            try
            {
                Type helpers = typeof(Reanimation).Assembly
                    .GetType("Sexy.TodLib.ReanimatorXnaHelpers");
                FieldInfo field = helpers?.GetField("gReanimatorDefArray",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                return field?.GetValue(null) as Array;
            }
            catch (Exception ex)
            {
                ModLog.Error("reanim cache field not found", ex);
                return null;
            }
        }

        public static bool TryGetDefinition(ReanimationType type,
            out ReanimatorDefinition definition)
        {
            int key = (int)type;
            definition = null;
            if (!Names.TryGetValue(key, out string name))
            {
                return false;
            }
            if (Built.TryGetValue(key, out definition))
            {
                return definition != null;
            }
            definition = Build(name);
            Built[key] = definition;
            return definition != null;
        }

        /// <summary>只做结构解析、不创建贴图，供没有 GraphicsDevice 的离线验证台用。</summary>
        public static Stats Inspect(string name)
        {
            Stats stats = new Stats();
            Parse(name, false, stats);
            return stats;
        }

        private static ReanimatorDefinition Build(string name)
        {
            Stats stats = new Stats();
            ReanimatorDefinition definition = Parse(name, true, stats);
            if (definition != null && stats.MissingImages.Count > 0)
            {
                ModLog.Error("reanim frames missing image: " + name + " -> " +
                    string.Join(", ", stats.MissingImages));
            }
            return definition;
        }

        private static ReanimatorDefinition Parse(string name, bool resolveImages, Stats stats)
        {
            string text = ReadText("Content/reanim/" + name + ".reanim");
            if (string.IsNullOrWhiteSpace(text))
            {
                ModLog.Error("missing reanim definition: reanim/" + name + ".reanim");
                return null;
            }
            XDocument document;
            try
            {
                document = XDocument.Parse("<reanim>" + text + "</reanim>");
            }
            catch (Exception ex)
            {
                ModLog.Error("reanim xml parse failed: " + name, ex);
                return null;
            }
            XElement root = document.Root;
            ScaleType scale = ParseScale(root.Element("doScale")?.Value);
            float fps = ParseFloat(root.Element("fps")?.Value, 0f);

            var tracks = new List<ReanimatorTrack>();
            foreach ( XElement trackElement in root.Elements("track"))
            {
                ReanimatorTrack track = BuildTrack(trackElement, scale, resolveImages, stats);
                if (track != null)
                {
                    tracks.Add(track);
                }
            }
            if (tracks.Count == 0)
            {
                ModLog.Error("reanim has no track: " + name);
                return null;
            }

            ReanimatorDefinition definition = new ReanimatorDefinition
            {
                mFPS = fps > 0f ? fps : 12f,
                mTrackCount = (short)tracks.Count,
                mTracks = tracks.ToArray(),
            };
            stats.Tracks = definition.mTrackCount;
            stats.Fps = definition.mFPS;
            if (resolveImages)
            {
                ModLog.Info("reanim built: " + name + ", tracks " + definition.mTrackCount +
                    ", frames " + stats.Frames + ", fps " + definition.mFPS);
            }
            return definition;
        }

        private static ReanimatorTrack BuildTrack(XElement element, ScaleType scale,
            bool resolveImages, Stats stats)
        {
            var transforms = new List<ReanimatorTransform>();
            foreach ( XElement frameElement in element.Elements("t"))
            {
                transforms.Add(BuildTransform(frameElement, scale));
            }
            if (transforms.Count == 0)
            {
                return null;
            }
            // 引擎 FindTrackIndex 是拿 ToLower(查询名) 去比存储的轨道名，
            // 混合大小写的轨道（如 CobCannon_cob）会静默查不到并回落到轨道 0。
            string trackName = (element.Element("name")?.Value ?? string.Empty).ToLowerInvariant();
            ReanimatorTrack track = new ReanimatorTrack(trackName, transforms.Count);
            transforms.CopyTo(track.mTransforms);
            FillInTrack(track, resolveImages, stats);
            stats.Frames += track.mTransformCount;
            stats.TrackNames.Add(track.mName);
            return track;
        }

        private static ReanimatorTransform BuildTransform(XElement element, ScaleType scale)
        {
            ReanimatorTransform transform = FreshTransform();
            transform.mFontName = element.Element("font")?.Value ?? string.Empty;
            transform.mImageName = element.Element("i")?.Value ?? string.Empty;
            transform.mText = element.Element("text")?.Value ?? string.Empty;
            transform.mAlpha = ParseFloat(element.Element("a")?.Value, Placeholder);
            transform.mFrame = ParseFloat(element.Element("f")?.Value, Placeholder);
            transform.mScaleX = ParseFloat(element.Element("sx")?.Value, Placeholder);
            transform.mScaleY = ParseFloat(element.Element("sy")?.Value, Placeholder);
            transform.mSkewX = ParseFloat(element.Element("kx")?.Value, Placeholder);
            transform.mSkewY = ParseFloat(element.Element("ky")?.Value, Placeholder);

            bool hasX = TryParseFloat(element.Element("x")?.Value, out float x);
            bool hasY = TryParseFloat(element.Element("y")?.Value, out float y);
            transform.mTransX = hasX ? ApplyScale(x, scale) : Placeholder;
            transform.mTransY = hasY ? ApplyScale(y, scale) : Placeholder;
            return transform;
        }

        /// <summary>
        /// 与 ReanimationLoadDefinition 的后处理一致：占位值续上一条，补全倾斜三角函数，
        /// 并把帧图名解析成 Image（解析不到就沿用上一条，等价于该帧不换图）。
        /// </summary>
        private static void FillInTrack(ReanimatorTrack track, bool resolveImages, Stats stats)
        {
            float previousX = 0f;
            float previousY = 0f;
            float previousSkewX = 0f;
            float previousSkewY = 0f;
            float previousScaleX = 1f;
            float previousScaleY = 1f;
            float previousFrame = 0f;
            float previousAlpha = 1f;
            Image previousImage = null;
            string previousImageName = string.Empty;
            Font previousFont = null;
            string previousText = string.Empty;

            foreach (ReanimatorTransform transform in track.mTransforms)
            {
                FillIn(ref previousX, ref transform.mTransX);
                FillIn(ref previousY, ref transform.mTransY);
                FillIn(ref previousSkewX, ref transform.mSkewX);
                FillIn(ref previousSkewY, ref transform.mSkewY);
                FillIn(ref previousScaleX, ref transform.mScaleX);
                FillIn(ref previousScaleY, ref transform.mScaleY);
                FillIn(ref previousFrame, ref transform.mFrame);
                FillIn(ref previousAlpha, ref transform.mAlpha);
                transform.mSkewXCos = (float)Math.Cos((double)transform.mSkewX *
                    (0.0 - (double)TodCommon.DEG_TO_RAD));
                transform.mSkewXSin = (float)Math.Sin((double)transform.mSkewX *
                    (0.0 - (double)TodCommon.DEG_TO_RAD));
                transform.mSkewYCos = (float)Math.Cos((double)transform.mSkewY *
                    (0.0 - (double)TodCommon.DEG_TO_RAD));
                transform.mSkewYSin = (float)Math.Sin((double)transform.mSkewY *
                    (0.0 - (double)TodCommon.DEG_TO_RAD));

                if (!string.IsNullOrEmpty(transform.mImageName))
                {
                    string key = ImageKey(transform.mImageName);
                    transform.mImage = resolveImages ? AssetStore.Get(key) : null;
                    if (!AssetStore.Has(key) || (resolveImages && transform.mImage == null))
                    {
                        stats.MissingImages.Add(key);
                    }
                }
                if (transform.mImage == null)
                {
                    transform.mImage = previousImage;
                    transform.mImageName = previousImageName;
                }
                else
                {
                    previousImage = transform.mImage;
                    previousImageName = transform.mImageName;
                }

                if (transform.mFont == null)
                {
                    transform.mFont = previousFont;
                }
                else
                {
                    previousFont = transform.mFont;
                }
                if (string.IsNullOrEmpty(transform.mText))
                {
                    transform.mText = previousText;
                }
                else
                {
                    previousText = transform.mText;
                }
            }
        }

        private static void FillIn(ref float previous, ref float value)
        {
            if (value == Placeholder)
            {
                value = previous;
            }
            else
            {
                previous = value;
            }
        }

        private static ReanimatorTransform FreshTransform()
        {
            ReanimatorTransform transform =
                ReanimatorTransform.GetReanimatorTransformForLoadingThread();
            transform.mFontName = string.Empty;
            transform.mImageName = string.Empty;
            transform.mText = string.Empty;
            return transform;
        }

        private static ScaleType ParseScale(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out int number))
            {
                return number == (int)ScaleType.NoScale ||
                       number == (int)ScaleType.InvertAndScale
                    ? (ScaleType)number
                    : ScaleType.ScaleFromPC;
            }
            return ScaleType.ScaleFromPC;
        }

        private static float ApplyScale(float value, ScaleType scale)
        {
            if (scale == ScaleType.InvertAndScale)
            {
                return Constants.InvertAndScale(value);
            }
            return scale == ScaleType.ScaleFromPC ? Constants.S * value : value;
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture,
                out value);
        }

        private static float ParseFloat(string text, float fallback)
        {
            return TryParseFloat(text, out float value) ? value : fallback;
        }

        /// <summary>
        /// IMAGE_REANIM_ARES_IDLE_000 → ares_idle_000，
        /// reanim/ares_idle_000.png 这类路径写法也归一到同一个 AssetStore 键。
        /// </summary>
        private static string ImageKey(string imageName)
        {
            if (string.IsNullOrEmpty(imageName))
            {
                return string.Empty;
            }
            string name = imageName;
            int slash = name.LastIndexOfAny(new char[] { '/', '\\' });
            if (slash >= 0)
            {
                name = name.Substring(slash + 1);
                if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }
                return name.ToLowerInvariant();
            }
            if (name.StartsWith("IMAGE_", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring("IMAGE_".Length);
                if (name.StartsWith("REANIM_", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring("REANIM_".Length);
                }
            }
            return name.ToLowerInvariant();
        }

        private static string ReadText(string relativePath)
        {
            try
            {
                using Stream stream = ModPaths.OpenContent(relativePath);
                if (stream == null)
                {
                    return null;
                }
                using StreamReader reader = new StreamReader(stream, Encoding.UTF8, true);
                return reader.ReadToEnd();
            }
            catch (Exception ex)
            {
                ModLog.Error("reanim read failed: " + relativePath, ex);
                return null;
            }
        }
    }
}
