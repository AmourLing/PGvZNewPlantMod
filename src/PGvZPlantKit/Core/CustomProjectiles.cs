using System;
using System.Collections.Generic;
using System.Reflection;
using Lawn;
using Microsoft.Xna.Framework.Graphics;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    public interface ICustomProjectileImpact
    {
        /// <summary>返回 true 表示已完全处理这次命中，不再走内置火焰/石块/毒逻辑。</summary>
        bool OnImpact(Projectile projectile, Zombie zombie,
            Action<Projectile, Zombie> orig);
    }

    public enum CustomPeaKind
    {
        Normal = 0,
        Snow = 1,
        Fire = 2,
        Electric = 3,
        Poison = 4,
        Stone = 5,
    }

    public sealed class NbjqsBoost
    {
        public int Damage;
        public string Effect;
        public CustomPeaKind Kind;
        public int NativeDamage;
    }

    public sealed class ProjectileTag
    {
        public CustomPeaKind Kind = CustomPeaKind.Normal;
        /// <summary>注册子弹定义；非空时贴图/伤害/弹道按这条记录走。</summary>
        public CustomBulletDef Bullet;
        /// <summary>非空时用这张 AssetStore 图替换弹体绘制（弹道、伤害仍走原生）。</summary>
        public string Image;
        public bool EdgeKill;
        public bool Fan;
        public float FanVelY;
        public float FanScreenY;
        public bool SwgRepeat;
        public bool Frozen;
        /// <summary>
        /// 爆炸动画进度（存"帧号 + 1"，0 = 没在爆炸）。跟着子弹一起进存档，
        /// 否则读档后弹体还会再引爆一次（重复 900 溅射 + 重复音效）。
        /// </summary>
        public float BoomFrame;
        public NbjqsBoost Boost;
        public HashSet<Zombie> ElectricHits;
        /// <summary>这颗注册子弹还能穿几个目标（跟着子弹进存档；换定义时重发）。</summary>
        public int PierceLeft;
        /// <summary>已经吃过这发穿透弹的目标。不跨存档：读档后同一只最多再挨一口。</summary>
        public HashSet<Zombie> PierceHits;
        /// <summary>这颗弹这次该画的那张染色图；null = 不染（没声明、或建不出来）。</summary>
        public Image TintedImage;
        public ICustomProjectileImpact ImpactHandler;
    }

    /// <summary>
    /// 复用原版 Projectile 载体，但按标签改写伤害/碰撞/绘制。
    /// 对应 Python 版的 _custom_projectiles/_fan_projectiles/_super_gatling_projectiles 等字典。
    /// </summary>
    public static class CustomProjectiles
    {
        private static readonly Dictionary<Projectile, ProjectileTag> Tags =
            new Dictionary<Projectile, ProjectileTag>();

        internal static void ClearLoadedTags() => Tags.Clear();

        public static ProjectileTag TagFor(Projectile projectile)
        {
            if (!Tags.TryGetValue(projectile, out ProjectileTag tag))
            {
                tag = new ProjectileTag();
                Tags[projectile] = tag;
            }
            return tag;
        }

        /// <summary>
        /// 引擎的 Projectile 是对象池复用的（回收走 <c>Projectile.Reset</c>，那里连
        /// <c>mProjectileType</c> 都会被抹回 Pea）。标签是按对象存的，不清就会跟着下一发跑：
        /// 实测过手写一颗原生小喷菇的弹，结果它带着上一颗孢子的定义（染色、伤害、MaxAge 全套）。
        /// </summary>
        public static void Discard(Projectile projectile)
        {
            if (projectile != null)
            {
                Tags.Remove(projectile);
            }
        }

        public static void Prune(Board currentBoard)
        {
            if (Tags.Count == 0)
            {
                return;
            }
            List<Projectile> stale = null;
            foreach (Projectile projectile in Tags.Keys)
            {
                if (projectile == null || projectile.mDead || projectile.mBoard != currentBoard)
                {
                    stale ??= new List<Projectile>();
                    stale.Add(projectile);
                }
            }
            if (stale != null)
            {
                foreach (Projectile projectile in stale)
                {
                    Tags.Remove(projectile);
                }
            }
        }

        public static bool TryGetTag(Projectile projectile, out ProjectileTag tag)
        {
            tag = null;
            return projectile != null && Tags.TryGetValue(projectile, out tag);
        }

        public static CustomPeaKind? GetKind(Projectile projectile)
        {
            return projectile != null && Tags.TryGetValue(projectile, out ProjectileTag tag)
                ? tag.Kind
                : (CustomPeaKind?)null;
        }

        public static void SetKind(Projectile projectile, CustomPeaKind kind)
        {
            TagFor(projectile).Kind = kind;
        }

        /// <summary>用 AssetStore 里的这张图绘制弹体，其余行为不变。</summary>
        public static void SetImage(Projectile projectile, string assetName)
        {
            TagFor(projectile).Image = assetName;
        }

        public static void SetFan(Projectile projectile, float fanVelY,
            bool edgeKill = true)
        {
            ProjectileTag tag = TagFor(projectile);
            tag.Fan = true;
            tag.FanVelY = fanVelY;
            tag.FanScreenY = projectile.mPosY;
            tag.EdgeKill = edgeKill;
        }

        public static void MarkEdgeKill(Projectile projectile)
        {
            TagFor(projectile).EdgeKill = true;
        }

        public static void MarkSwgRepeat(Projectile projectile)
        {
            TagFor(projectile).SwgRepeat = true;
        }

        public static void SetImpactHandler(Projectile projectile,
            ICustomProjectileImpact handler)
        {
            TagFor(projectile).ImpactHandler = handler;
        }

        public static void SetFrozen(Projectile projectile, bool frozen = true)
        {
            TagFor(projectile).Frozen = frozen;
        }

        public static void SetBoost(Projectile projectile, NbjqsBoost boost)
        {
            TagFor(projectile).Boost = boost;
        }

        public static NbjqsBoost TakeBoost(Projectile projectile)
        {
            if (projectile != null && Tags.TryGetValue(projectile, out ProjectileTag tag) &&
                tag.Boost != null)
            {
                NbjqsBoost boost = tag.Boost;
                tag.Boost = null;
                return boost;
            }
            return null;
        }

        public static void RestoreBoost(Projectile projectile, NbjqsBoost boost)
        {
            TagFor(projectile).Boost = boost;
        }

        public static void OnProjectileUpdate(Action<Projectile> orig, Projectile projectile)
        {
            Tags.TryGetValue(projectile, out ProjectileTag tag);
            // 注册子弹自带弹道（巧克力大炮那一类）：整帧交给它，原生积分不再参与。
            if (tag?.Bullet?.Motion != null)
            {
                tag.Bullet.Motion.Update(projectile, orig);
                if (projectile.mDead)
                {
                    Tags.Remove(projectile);
                }
                return;
            }
            // 牛蒡击球手强化窗口：1.3.2 的直线弹位移是硬编码 3.33，单改 mVelX 冻不住。
            if (tag != null && tag.Frozen && !projectile.mDead)
            {
                return;
            }
            bool fan = tag != null && tag.Fan && !projectile.mDead;
            if (fan)
            {
                int safeY = projectile.mBoard.GridToPixelY(0, projectile.mRow) + 10;
                projectile.mPosY = safeY;
                projectile.mY = safeY;
                projectile.mVelY = 0f;
            }

            float xBefore = projectile.mPosX;
            orig(projectile);

            ApplySpeed(projectile, tag, xBefore);

            // 小喷菇那颗的"射程短"是引擎按 mMotionType == Puff 判的 75 帧；
            // 斜着飞（Star 运动）的注册弹拿不到那条，所以按声明在这里收掉同一刀。
            if (tag?.Bullet != null && tag.Bullet.MaxAge > 0 && !projectile.mDead &&
                projectile.mProjectileAge >= tag.Bullet.MaxAge)
            {
                projectile.Die();
            }

            if (fan && !projectile.mDead)
            {
                tag.FanScreenY += tag.FanVelY;
                projectile.mPosY = tag.FanScreenY;
                projectile.mY = (int)projectile.mPosY;
                Reanimation fireReanim = projectile.mApp.ReanimationTryToGet(projectile.mProjectileReanimID);
                if (fireReanim != null)
                {
                    fireReanim.SetPosition(projectile.mPosX, projectile.mPosY);
                }
            }

            if (tag != null && tag.EdgeKill && !projectile.mDead)
            {
                int rightEdge = tag.Fan
                    ? projectile.mBoard.mWidth + 40
                    : projectile.mBoard.RightEdge(projectile.mRow);
                if (projectile.mPosX > rightEdge)
                {
                    projectile.Die();
                }
            }

            if (projectile.mDead)
            {
                Tags.Remove(projectile);
                return;
            }
            if (tag != null && tag.Kind == CustomPeaKind.Electric &&
                projectile.mProjectileAge > 0 &&
                projectile.mProjectileAge % CustomCombat.ElectricPeaShockInterval == 0)
            {
                CustomCombat.ElectricShockRandom(projectile);
            }
        }

        /// <summary>
        /// 直线弹速度：引擎把这一帧的位移写死成 3.33（18~20 号弹种是 15），改 <c>mVelX</c> 没用，
        /// 只能在它走完之后再把它挪的那一段换成声明值。别的弹道（抛物/落点/浮空）是时间曲线，
        /// 换横向速度会让它落在别处，所以只有当这一帧的位移正好等于那两个常数时才修正。
        /// </summary>
        private static void ApplySpeed(Projectile projectile, ProjectileTag tag, float xBefore)
        {
            CustomBulletDef def = tag?.Bullet;
            if (def == null || def.Speed <= 0f || projectile.mDead)
            {
                return;
            }
            float step = projectile.mPosX - xBefore;
            if (Math.Abs(Math.Abs(step) - 3.33f) > 0.001f &&
                Math.Abs(Math.Abs(step) - 15f) > 0.001f)
            {
                return;
            }
            projectile.mPosX = xBefore + (step < 0f ? -def.Speed : def.Speed);
            projectile.mX = (int)projectile.mPosX;
            Reanimation reanim = projectile.mApp.ReanimationTryToGet(
                projectile.mProjectileReanimID);
            reanim?.SetPosition(projectile.mPosX, projectile.mPosY);
        }

        public static void OnProjectileCheckForCollision(Action<Projectile> orig, Projectile projectile)
        {
            Tags.TryGetValue(projectile, out ProjectileTag tag);
            // 自己算落点的注册子弹（巧克力炮弹）不能被原生碰撞提前引爆。
            if (tag?.Bullet != null && tag.Bullet.SkipCollision)
            {
                return;
            }
            if (tag != null && tag.Fan)
            {
                TRect projectileRect = projectile.GetProjectileRect();
                for (int i = 0; i < projectile.mBoard.mZombies.Count; i++)
                {
                    Zombie zombie = projectile.mBoard.mZombies[i];
                    if (!CustomCombat.IsValidElectricTarget(projectile, zombie))
                    {
                        continue;
                    }
                    TRect zombieRect = zombie.GetZombieRect();
                    bool overlaps = !(projectileRect.mX + projectileRect.mWidth < zombieRect.mX ||
                        zombieRect.mX + zombieRect.mWidth < projectileRect.mX ||
                        projectileRect.mY + projectileRect.mHeight < zombieRect.mY ||
                        zombieRect.mY + zombieRect.mHeight < projectileRect.mY);
                    if (overlaps)
                    {
                        projectile.DoImpact(zombie);
                        return;
                    }
                }
                return;
            }
            if (tag == null || tag.Kind != CustomPeaKind.Electric)
            {
                orig(projectile);
                return;
            }
            tag.ElectricHits ??= new HashSet<Zombie>();
            TRect rect = projectile.GetProjectileRect();
            for (int i = 0; i < projectile.mBoard.mZombies.Count; i++)
            {
                Zombie zombie = projectile.mBoard.mZombies[i];
                if (tag.ElectricHits.Contains(zombie) ||
                    !CustomCombat.IsValidElectricTarget(projectile, zombie))
                {
                    continue;
                }
                TRect zombieRect = zombie.GetZombieRect();
                bool overlaps = !(rect.mX + rect.mWidth < zombieRect.mX ||
                    zombieRect.mX + zombieRect.mWidth < rect.mX ||
                    rect.mY + rect.mHeight < zombieRect.mY ||
                    zombieRect.mY + zombieRect.mHeight < rect.mY);
                if (overlaps)
                {
                    tag.ElectricHits.Add(zombie);
                    CustomCombat.DoElectricDamage(zombie,
                        CustomCombat.ElectricPeaContactDamage,
                        projectile.GetDamageFlags(zombie), false);
                }
            }
        }

        public static void OnProjectileDraw(Action<Projectile, Graphics> orig,
            Projectile projectile, Graphics g)
        {
            if (!Tags.TryGetValue(projectile, out ProjectileTag tag))
            {
                orig(projectile, g);
                return;
            }
            Image replacement = PeaImageOverride(tag) ?? CustomBullets.MotionImage(projectile, tag);
            if (replacement == null && tag.TintedImage != null)
            {
                // 染色图在发弹那一刻就建好了（见 PrepareTint）：绘制这一趟只做一次查表，
                // 绝不在这里读像素/建纹理 —— 实测过在 Draw 里做这些会把游戏线程卡死。
                replacement = tag.TintedImage;
                TintDraws++;
            }
            if (replacement == null)
            {
                orig(projectile, g);
                return;
            }
            // 引擎画弹体是从 AtlasResources 的静态字段里按弹种取图，所以"换图/染色"
            // 就是在这一次绘制里把那张字段顶掉；画完必须还原，否则别人的同款弹也跟着变。
            ProjectileType drawn = tag.Kind == CustomPeaKind.Fire
                ? ProjectileType.Pea
                : projectile.mProjectileType;
            FieldInfo field = CarrierImageField(drawn);
            if (field == null)
            {
                WarnUnmappedCarrier(drawn);
                orig(projectile, g);
                return;
            }
            Image nativeImage = field.GetValue(null) as Image;
            ProjectileType nativeType = projectile.mProjectileType;
            float nativeX = projectile.mPosX;
            float nativeY = projectile.mPosY;
            field.SetValue(null, replacement);
            if (tag.Kind == CustomPeaKind.Fire)
            {
                projectile.mProjectileType = ProjectileType.Pea;
            }
            // 引擎把弹画在 (mPos + 半格) 上，格子尺寸一变视觉中心就跳；补偿回来，画完还原。
            ShiftAnchor(projectile, nativeImage, replacement);
            try
            {
                orig(projectile, g);
            }
            finally
            {
                projectile.mProjectileType = nativeType;
                field.SetValue(null, nativeImage);
                projectile.mPosX = nativeX;
                projectile.mPosY = nativeY;
            }
        }

        /// <summary>
        /// 引擎 <c>Projectile.Draw</c> 每个弹种从哪个静态字段取弹体图。
        /// 没在表里的弹种（火球/玉米加农炮那类走 reanim 的）换不了图，会点名一次。
        /// </summary>
        private static readonly Dictionary<ProjectileType, string> CarrierImageFields =
            new Dictionary<ProjectileType, string>
            {
                [ProjectileType.Pea] = "IMAGE_PROJECTILEPEA",
                [ProjectileType.ZombiePea] = "IMAGE_PROJECTILEPEA",
                [ProjectileType.ZombiePeaMindControl] = "IMAGE_PROJECTILEPEA",
                [ProjectileType.Snowpea] = "IMAGE_PROJECTILESNOWPEA",
                [ProjectileType.Spike] = "IMAGE_PROJECTILECACTUS",
                [ProjectileType.CattailSpike] = "IMAGE_REANIM_PROJECTILECATTAIL",
                [ProjectileType.HypnoCattailSpike] = "IMAGE_REANIM_PROJECTILECATTAIL_HYPNO",
                [ProjectileType.Star] = "IMAGE_PROJECTILE_STAR",
                [ProjectileType.Puff] = "IMAGE_PUFFSHROOM_PUFF1",
                [ProjectileType.PuffGreen] = "IMAGE_SEASHROOM_PUFF_GREEN1",
                [ProjectileType.Basketball] = "IMAGE_REANIM_ZOMBIE_CATAPULT_BASKETBALL",
                [ProjectileType.Cabbage] = "IMAGE_REANIM_CABBAGEPULT_CABBAGE",
                [ProjectileType.Kernel] = "IMAGE_REANIM_CORNPULT_KERNAL",
                [ProjectileType.Butter] = "IMAGE_REANIM_CORNPULT_BUTTER",
                [ProjectileType.Melon] = "IMAGE_REANIM_MELONPULT_MELON",
                [ProjectileType.Wintermelon] = "IMAGE_REANIM_WINTERMELON_PROJECTILE",
            };

        private static readonly Dictionary<string, Image> TintedImages =
            new Dictionary<string, Image>();

        /// <summary>按 (图集页, 颜色) 缓存的染色副本页 —— 同页多个弹种共用一张。</summary>
        private static readonly Dictionary<string, Texture2D> TintedPages =
            new Dictionary<string, Texture2D>();

        /// <summary>登记过的弹种（换图/染色的支持面）。台架用它逐个验字段还在不在。</summary>
        public static IEnumerable<ProjectileType> TintableCarriers => CarrierImageFields.Keys;

        private static readonly HashSet<ProjectileType> unmappedWarned = new();

        /// <summary>画了几次染色弹。台架用它证明这条路径真的走到了，而不是声明了个空字段。</summary>
        public static int TintDraws;

        /// <summary>这个弹种的弹体走不走静态图字段（不走就换不了图、也染不了色）。</summary>
        public static FieldInfo CarrierImageField(ProjectileType type)
        {
            return CarrierImageFields.TryGetValue(type, out string name)
                ? typeof(AtlasResources).GetField(name,
                    BindingFlags.Static | BindingFlags.Public)
                : null;
        }

        private static void WarnUnmappedCarrier(ProjectileType type)
        {
            if (unmappedWarned.Add(type))
            {
                ModLog.Warn("弹体染色/换图还不支持 " + type + "（引擎那一段不读静态图字段）");
            }
        }

        /// <summary>
        /// 发弹（含读档恢复）那一刻把染色图备好：读像素、建纹理这些都在游戏逻辑里做，
        /// 绘制那一趟只查表。整段兜住异常 —— 染不出来宁可回到不染色，
        /// 也不能把一次贴图失败变成整局卡住。
        /// </summary>
        public static Image PrepareTint(CustomBulletDef def, ProjectileType type)
        {
            if (def == null || string.IsNullOrEmpty(def.Tint))
            {
                return null;
            }
            try
            {
                if (!CustomPlantDef.TryParseTint(def.Tint, out SexyColor tint))
                {
                    return null;
                }
                FieldInfo field = CarrierImageField(type);
                if (field?.GetValue(null) is not Image source || source.Texture == null)
                {
                    return null;
                }
                string key = source.GetHashCode() + "#" + tint.mRed + "," + tint.mGreen + "," +
                    tint.mBlue + "#" + source.mWidth + "x" + source.mHeight;
                if (TintedImages.TryGetValue(key, out Image cached))
                {
                    return cached;
                }
                Image made = MultiplyTint(source, tint);
                if (made != null)
                {
                    TintedImages[key] = made;
                }
                return made;
            }
            catch (Exception e)
            {
                ModLog.Warn("弹体染色准备失败（" + def.Id + " " + def.Tint + "），这发不染色：" +
                    e.Message);
                return null;
            }
        }

        /// <summary>
        /// 造一张整页大小的染色副本：把源图那一格乘过色写回它原来的页内位置，再用引擎自己的
        /// <c>Image(texture, s, t, w, h)</c> 造副本图，偏移/格子/<c>mInvScale</c> 全照抄源图 ——
        /// UV 怎么算由引擎负责，框架不复刻它的取帧公式。
        /// （之前在这里自己抠一张独立小图，就得自己算 <c>mInvScale</c> 那一步，
        /// 少算一点就把图集邻格的碎片吃了进来、弹体还画小一号。）
        /// alpha 原样保留 —— 弹体图带空腔，动 alpha 会把边缘啃出洞。
        /// </summary>
        private static Image MultiplyTint(Image source, SexyColor tint)
        {
            Texture2D page = source.Texture;
            if (source.mWidth <= 0 || source.mHeight <= 0 ||
                source.mS < 0 || source.mT < 0 ||
                source.mS + source.mWidth > page.Width ||
                source.mT + source.mHeight > page.Height)
            {
                ModLog.Warn("弹体染色跳过：图格 " + source.mWidth + "x" + source.mHeight + "@" +
                    source.mS + "," + source.mT + " 放不下 " + page.Width + "x" + page.Height + " 这页");
                return null;
            }
            Texture2D tintedPage = TintedPage(page, source, tint);
            if (tintedPage == null)
            {
                return null;
            }
            Image result = new Image(tintedPage, source.mS, source.mT,
                source.mWidth, source.mHeight);
            result.mInvScale = source.mInvScale;
            result.mNumCols = source.mNumCols;
            result.mNumRows = source.mNumRows;
            if (result.GetCelWidth() != source.GetCelWidth() ||
                result.GetCelHeight() != source.GetCelHeight())
            {
                ModLog.Warn("弹体染色跳过：副本格子 " + result.GetCelWidth() + "x" +
                    result.GetCelHeight() + " 和原图 " + source.GetCelWidth() + "x" +
                    source.GetCelHeight() + " 不一致");
                return null;
            }
            return result;
        }

        /// <summary>
        /// 同一页 + 同一颜色只造一张副本，一页上多个弹种共用。
        /// 只读写这一格的像素：整页 4M 像素在发弹那一拍走一遍会明显卡一下。
        /// 副本其余部分是空的 —— 引擎按源图那套偏移只会采到这一格，采不到别处。
        /// </summary>
        private static Texture2D TintedPage(Texture2D page, Image source, SexyColor tint)
        {
            string key = page.GetHashCode() + "#" + tint.mRed + "," + tint.mGreen + "," + tint.mBlue;
            if (TintedPages.TryGetValue(key, out Texture2D cached))
            {
                return cached;
            }
            Microsoft.Xna.Framework.Color[] pixels;
            try
            {
                pixels = new Microsoft.Xna.Framework.Color[source.mWidth * source.mHeight];
                page.GetData(0,
                    new Microsoft.Xna.Framework.Rectangle(source.mS, source.mT,
                        source.mWidth, source.mHeight),
                    pixels, 0, pixels.Length);
            }
            catch (Exception e)
            {
                ModLog.Warn("弹体图读不出来，染色跳过：" + e.Message);
                return null;
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Microsoft.Xna.Framework.Color(
                    (byte)(pixels[i].R * tint.mRed / 255),
                    (byte)(pixels[i].G * tint.mGreen / 255),
                    (byte)(pixels[i].B * tint.mBlue / 255),
                    pixels[i].A);
            }
            Texture2D copy = new Texture2D(page.GraphicsDevice, page.Width, page.Height);
            copy.SetData(0,
                new Microsoft.Xna.Framework.Rectangle(source.mS, source.mT,
                    source.mWidth, source.mHeight),
                pixels, 0, pixels.Length);
            TintedPages[key] = copy;
            return copy;
        }


        private static void ShiftAnchor(Projectile projectile, Image from, Image to)
        {
            if (from == null || to == null)
            {
                return;
            }
            int dx = from.GetCelWidth() - to.GetCelWidth();
            int dy = from.GetCelHeight() - to.GetCelHeight();
            if (dx != 0)
            {
                projectile.mPosX += dx / 2f;
            }
            if (dy != 0)
            {
                projectile.mPosY += dy / 2f;
            }
        }

        /// <summary>需要换图的豌豆类弹返回替换图；不需要换的返回 null，交给原生绘制。</summary>
        private static Image PeaImageOverride(ProjectileTag tag)
        {
            if (tag.Bullet != null)
            {
                // 注册子弹的图统一由 MotionImage 给（行为可以逐帧换图）。
                return null;
            }
            if (!string.IsNullOrEmpty(tag.Image))
            {
                return AssetStore.Get(tag.Image);
            }
            switch (tag.Kind)
            {
                case CustomPeaKind.Fire: return AssetStore.Get("fire_pea");
                case CustomPeaKind.Electric: return AssetStore.Get("electric_pea");
                case CustomPeaKind.Poison: return AssetStore.Get("venom_pea");
                case CustomPeaKind.Stone: return AssetStore.Get("stone_pea");
                default: return null;
            }
        }

        public static bool PeaAboutToHitTorchwood(Projectile projectile, Func<bool> orig)
        {
            if (Tags.TryGetValue(projectile, out ProjectileTag tag) &&
                (tag.Bullet != null || !string.IsNullOrEmpty(tag.Image) ||
                 tag.Kind == CustomPeaKind.Fire || tag.Kind == CustomPeaKind.Electric ||
                 tag.Kind == CustomPeaKind.Poison || tag.Kind == CustomPeaKind.Stone))
            {
                return false;
            }
            return orig();
        }

        public static bool CheckForHighGround(Projectile projectile, Action<Projectile> orig)
        {
            if (Tags.TryGetValue(projectile, out ProjectileTag tag) && tag.Fan)
            {
                return false;
            }
            orig(projectile);
            return true;
        }

        public static void OnProjectileImpact(Action<Projectile, Zombie> orig,
            Projectile projectile, Zombie zombie)
        {
            if (Tags.TryGetValue(projectile, out ProjectileTag tag) && tag.SwgRepeat)
            {
                tag.SwgRepeat = false;
                orig(projectile, zombie);
                if (CustomCombat.IsValidTarget(zombie))
                {
                    zombie.TakeDamage(20, projectile.GetDamageFlags(zombie));
                }
                return;
            }

            // 注册子弹：伤害/溅射/自定义命中都按那条记录结算，不再走载体弹种的默认伤害。
            if (tag != null && tag.Bullet != null &&
                CustomBullets.TryImpact(projectile, zombie, orig))
            {
                return;
            }

            NbjqsBoost boost = TakeBoost(projectile);
            if (boost != null)
            {
                HandleBoostedImpact(orig, projectile, zombie, boost);
                return;
            }

            if (tag != null && tag.ImpactHandler != null)
            {
                if (tag.ImpactHandler.OnImpact(projectile, zombie, orig))
                {
                    return;
                }
            }

            CustomPeaKind kind = tag?.Kind ?? CustomPeaKind.Normal;
            if (kind == CustomPeaKind.Fire)
            {
                orig(projectile, zombie);
                if (CustomCombat.IsValidTarget(zombie))
                {
                    zombie.TakeDamage(20, projectile.GetDamageFlags(zombie));
                }
                return;
            }
            if (kind == CustomPeaKind.Stone)
            {
                HandleStoneImpact(orig, projectile, zombie);
                return;
            }
            if (kind != CustomPeaKind.Poison)
            {
                orig(projectile, zombie);
                return;
            }

            bool poisonBoss = zombie != null && zombie.mZombieType == ZombieType.Boss &&
                              CustomCombat.IsValidTarget(zombie);
            if (poisonBoss)
            {
                CustomCombat.ApplyPoison(zombie, CustomCombat.PoisonPea, projectile);
            }
            orig(projectile, zombie);
            if (CustomCombat.IsValidTarget(zombie))
            {
                zombie.TakeDamage(
                    CustomCombat.PoisonPeaImpactDamage - 20,
                    projectile.GetDamageFlags(zombie));
                if (!poisonBoss && CustomCombat.IsValidTarget(zombie))
                {
                    CustomCombat.ApplyPoison(zombie, CustomCombat.PoisonPea, projectile);
                }
            }
        }

        private static void HandleStoneImpact(Action<Projectile, Zombie> orig,
            Projectile projectile, Zombie zombie)
        {
            if (zombie == null)
            {
                orig(projectile, zombie);
                return;
            }
            bool isBoss = zombie.mZombieType == ZombieType.Boss;
            bool execute = !isBoss && !CustomCombat.IsGiantZombie(zombie) &&
                           TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaExecuteChance;
            bool stun = !isBoss &&
                        TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaStunChance;
            bool knockback = !isBoss &&
                             TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaKnockbackChance;
            orig(projectile, zombie);
            if (!CustomCombat.IsValidTarget(zombie))
            {
                return;
            }
            if (execute)
            {
                zombie.TakeDamage(CustomCombat.ZombieEffectiveHealth(zombie) + 1,
                    projectile.GetDamageFlags(zombie));
                return;
            }
            zombie.TakeDamage(
                CustomCombat.StonePeaImpactDamage - 20, projectile.GetDamageFlags(zombie));
            if (!CustomCombat.IsValidTarget(zombie))
            {
                return;
            }
            if (stun)
            {
                zombie.ApplyButter();
                zombie.mButteredCounter = CustomCombat.StonePeaStunDuration;
            }
            if (knockback)
            {
                float lawnRight = projectile.mBoard.RightEdge(zombie.mRow) + 80f;
                if (zombie.mPosX < lawnRight)
                {
                    zombie.mPosX = Math.Min(
                        lawnRight, zombie.mPosX + CustomCombat.StonePeaKnockbackDistance);
                    zombie.mX = (int)zombie.mPosX;
                }
            }
        }

        private static void HandleBoostedImpact(Action<Projectile, Zombie> orig,
            Projectile projectile, Zombie zombie, NbjqsBoost boost)
        {
            if (zombie == null)
            {
                orig(projectile, zombie);
                return;
            }
            if (zombie.mZombieType == ZombieType.Boss && !CustomCombat.BossCanBeHit(zombie))
            {
                RestoreBoost(projectile, boost);
                return;
            }
            int baseDamage = boost.Damage * 2;
            bool stoneExecute = false;
            bool stoneStun = false;
            bool stoneKnockback = false;
            if (boost.Kind == CustomPeaKind.Stone)
            {
                bool isBoss = zombie.mZombieType == ZombieType.Boss;
                stoneExecute = !isBoss && !CustomCombat.IsGiantZombie(zombie) &&
                    TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaExecuteChance;
                stoneStun = !isBoss &&
                    TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaStunChance;
                stoneKnockback = !isBoss &&
                    TodCommon.RandRangeInt(1, 100) <= CustomCombat.StonePeaKnockbackChance;
            }
            orig(projectile, zombie);
            if (!CustomCombat.IsValidTarget(zombie))
            {
                return;
            }
            if (stoneExecute)
            {
                zombie.TakeDamage(CustomCombat.ZombieEffectiveHealth(zombie) + 1,
                    projectile.GetDamageFlags(zombie));
                return;
            }
            zombie.TakeDamage(Math.Max(0, baseDamage - boost.NativeDamage),
                projectile.GetDamageFlags(zombie));
            if (!CustomCombat.IsValidTarget(zombie))
            {
                return;
            }
            if (stoneStun)
            {
                zombie.ApplyButter();
                zombie.mButteredCounter = Math.Max(
                    zombie.mButteredCounter, CustomCombat.StonePeaStunDuration);
            }
            if (stoneKnockback)
            {
                float lawnRight = projectile.mBoard.GridToPixelX(8, zombie.mRow) + 80f;
                if (zombie.mPosX < lawnRight)
                {
                    zombie.mPosX = Math.Min(
                        lawnRight, zombie.mPosX + CustomCombat.StonePeaKnockbackDistance);
                    zombie.mX = (int)zombie.mPosX;
                }
            }
            if (boost.Effect == "knockback")
            {
                float lawnRight = projectile.mBoard.GridToPixelX(8, zombie.mRow) + 80f;
                if (zombie.mZombieType != ZombieType.Boss && zombie.mPosX < lawnRight)
                {
                    zombie.mPosX = Math.Min(lawnRight, zombie.mPosX + 80f);
                    zombie.mX = (int)zombie.mPosX;
                }
            }
            else if (zombie.mZombieType != ZombieType.Boss)
            {
                zombie.ApplyButter();
                zombie.mButteredCounter = Math.Max(zombie.mButteredCounter, 120);
            }
        }
    }
}
