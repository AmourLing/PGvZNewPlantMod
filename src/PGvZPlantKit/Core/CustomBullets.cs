using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 逐帧接管一颗子弹：Update 自己决定要不要调 orig（原生弹道），
    /// GetImage 决定这一帧画哪张图；返回 null 就按注册定义里的图走。
    /// </summary>
    public interface ICustomBulletMotion
    {
        void Update(Projectile shot, Action<Projectile> orig);
        Image GetImage(Projectile shot);
    }

    /// <summary>一颗注册子弹的定义。见 CustomBullets。</summary>
    public sealed class CustomBulletDef
    {
        /// <summary>全局 ID，跨包引用用它，例："pgvz.ares:ares_shot"。</summary>
        public string Id;
        /// <summary>载体弹种：决定原生碰撞盒、影子和默认伤害。</summary>
        public ProjectileType Carrier = ProjectileType.Pea;
        /// <summary>AssetStore 里的贴图名；留空则用载体弹种自己的图。</summary>
        public string Image;
        /// <summary>
        /// 弹体染色，写 <c>#rrggbb</c>（可带 <c>##rrggbbaa</c> 的 alpha），留空不染。
        /// 引擎画弹体走 <c>TodCommon.TodBltMatrix(..., SexyColor.White, ...)</c>，
        /// 那条路径不读 <c>g.mColor</c>/<c>SetColorizeImages</c>（只有普通 <c>DrawImage</c> 读），
        /// 所以染色只能换成"乘过色的那张图"：框架按 (原图, 颜色) 缓存一份染好的副本，
        /// 只在这颗弹的这一次绘制里把载体那张静态图顶掉。
        /// </summary>
        public string Tint;
        /// <summary>大于 0 时由框架结算伤害；留 0 用载体弹种的原生伤害。</summary>
        public int Damage;
        /// <summary>
        /// 命中时传给 <c>Zombie.TakeDamage(damage, flags)</c> 的 <c>DamageFlags</c> 掩码，
        /// 管"这一发怎么打"：1 穿盾、2 盾与本体同扣、4 附加冰冻、8 不播受击闪白、
        /// 16 不留尸体、32 地刺扎死。0 = 用引擎按弹种自己算的那套
        /// （<c>Projectile.GetDamageFlags</c>：溅射给 2、抛物/倒飞给 1、雪豌豆/冰瓜加 4）。
        /// </summary>
        public uint DamageFlags;
        /// <summary>
        /// 管"这一发打得着谁"，写进 <c>Projectile.mDamageRangeFlags</c> 并喂给
        /// <c>Zombie.EffectedByDamage</c>：1 地面、2 空中、4 潜水、8 狗、16 离地阶段、
        /// 32 濒死、64 地下/隐身、128 只打被洗脑的。0 = 用发射植物
        /// <c>GetDamageRangeFlags</c> 算出的值（豌豆那族是 1，大喷菇 9，香蒲 11，樱桃那类 127）。
        /// </summary>
        public uint DamageRangeFlags;
        /// <summary>大于 0 时命中给同排邻居补这一刀。</summary>
        public int SplashDamage;
        /// <summary>溅射矩形宽度，默认照火球。</summary>
        public int SplashWidth = 100;
        /// <summary>
        /// 还能再穿几个目标：&gt;0 时咬到新目标就扣一口、弹不消失，同一只僵尸只吃一口
        /// （重复撞上已经咬过的它既不扣血也不掉额度）。额度用完后，下一只新目标这一口
        /// 之后弹才落地。"打过谁"按对象记、不跨存档：读档后同一只最多再挨一口；
        /// 额度本身跟着子弹存。需要显式 <see cref="Damage"/>：留 0 走原生
        /// <c>DoImpact</c>，那一段自己会把弹带走（定义自检会点名这种配法）。
        /// </summary>
        public int Pierce;
        /// <summary>
        /// 命中就挂上这层毒（<see cref="CustomCombat.PoisonSpec"/>），两套掩码默认继承这颗弹。
        /// 注册这颗子弹时这条 spec 一并进毒的台账，诊断快照和自检都看得到它。
        /// </summary>
        public CustomCombat.PoisonSpec Poison;
        /// <summary>
        /// 命中就给主目标挂一层灼热（<see cref="CustomBurn.BurnSpec"/>）：独立于毒的另一套账本，
        /// 每拍按总最大生命的百分比烧，并按西瓜那档范围往周围溅同一刀。
        /// </summary>
        public CustomBurn.BurnSpec Burn;
        /// <summary>
        /// 直线弹每帧走多少像素。引擎把直线弹的位移写死成 3.33/帧（个别弹种 15），
        /// 改 <c>mVelX</c> 没用；填了它，框架在原生那一帧之后把位移换成这个数。
        /// 只对直线弹生效——抛物/落点弹道是时间曲线，改横向速度会让它落在别处。
        /// </summary>
        public float Speed;
        /// <summary>
        /// 飞多少帧就消失（0 = 不管）。小喷菇那颗的"射程短"就是引擎在
        /// <c>Projectile.CheckForCollision</c> 里按 <c>mMotionType == Puff</c> 判的 75 帧；
        /// 斜着飞的弹走的是 <c>Star</c> 运动，拿不到那条，所以用这个声明补同样的限制。
        /// </summary>
        public int MaxAge;
        /// <summary>true 时不走原生碰撞，落点由 Motion/Impact 自己结算。</summary>
        public bool SkipCollision;
        public ICustomBulletMotion Motion;
        /// <summary>填了就完全接管命中结算，Damage/Splash 那套不再参与。</summary>
        public ICustomProjectileImpact Impact;
    }

    /// <summary>
    /// 子弹注册表。这个框架里没有"新增 ProjectileType"的口子（类型是引擎枚举、
    /// 还写进关卡存档），所以自定义子弹一律 = 原生载体弹种 + 一张注册表记录：
    /// 换图、换伤害、换弹道、换命中都挂在记录上，别的包按 ID 字符串引用它。
    /// 查不到 ID 就是那个包没启用，调用方跳过即可，不会报错。
    /// </summary>
    public static class CustomBullets
    {
        private static readonly Dictionary<string, CustomBulletDef> Defs =
            new Dictionary<string, CustomBulletDef>(StringComparer.OrdinalIgnoreCase);

        public static int Count => Defs.Count;

        public static IEnumerable<CustomBulletDef> All => Defs.Values;

        public static void Register(CustomBulletDef def)
        {
            if (def == null || string.IsNullOrEmpty(def.Id))
            {
                return;
            }
            Defs[def.Id] = def;
            // 子弹自带的毒跟着进台账：不然它只在命中那一拍存在，自检和快照都看不见。
            CustomCombat.RegisterPoison(def.Poison);
            CustomBurn.RegisterBurn(def.Burn);
        }

        public static CustomBulletDef Find(string id)
        {
            return !string.IsNullOrEmpty(id) && Defs.TryGetValue(id, out CustomBulletDef def)
                ? def
                : null;
        }

        /// <summary>造一颗注册过的子弹，坐标和弹道仍由调用方按原生方式填。</summary>
        public static Projectile Fire(Board board, Plant plant, string id,
            int x, int y, int row, ProjectileMotion? motion = null,
            uint? rangeFlags = null)
        {
            CustomBulletDef def = Find(id);
            Projectile shot = board.AddProjectile(x, y, plant.mRenderOrder - 1, row,
                def?.Carrier ?? ProjectileType.Pea);
            if (motion.HasValue)
            {
                shot.mMotionType = motion.Value;
            }
            shot.mDamageRangeFlags = (int)ResolveRangeFlags(plant, def, rangeFlags);
            shot.mFromPlant = plant.mSeedType;
            if (def != null)
            {
                Attach(shot, def);
            }
            return shot;
        }

        /// <summary>
        /// 范围掩码的取值顺序：显式入参 &gt; 注册定义 &gt; 发射植物的原生值。
        /// 三处都拿 0 表示"没写"，跟 <see cref="CustomBulletDef.DamageRangeFlags"/> 的约定一致。
        /// </summary>
        private static uint ResolveRangeFlags(Plant plant, CustomBulletDef def, uint? explicitFlags)
        {
            if (explicitFlags.GetValueOrDefault() != 0)
            {
                return explicitFlags.Value;
            }
            if (def != null && def.DamageRangeFlags != 0)
            {
                return def.DamageRangeFlags;
            }
            return (uint)plant.GetDamageRangeFlags(PlantWeapon.Primary);
        }

        /// <summary>"打得着谁"：注册定义写死就用它，否则用这颗弹当前的范围掩码。</summary>
        private static uint RangeFlagsOf(Projectile shot, CustomBulletDef def)
        {
            return def.DamageRangeFlags != 0
                ? def.DamageRangeFlags
                : (uint)shot.mDamageRangeFlags;
        }

        /// <summary>"怎么打"：注册定义写死就用它，否则照引擎那套按这颗弹和目标现算。</summary>
        private static uint TakeFlagsOf(Projectile shot, Zombie target, CustomBulletDef def)
        {
            return def.DamageFlags != 0
                ? def.DamageFlags
                : shot.GetDamageFlags(target);
        }

        public static void Attach(Projectile shot, CustomBulletDef def)
        {
            ProjectileTag tag = CustomProjectiles.TagFor(shot);
            tag.Bullet = def;
            // 穿透额度跟着"这颗弹现在是哪条定义"走：附魔换成会穿透的弹，就重新发一份额度。
            tag.PierceLeft = def.Pierce;
            tag.PierceHits = null;
            // 染色图在这里就备好（读像素/建纹理不能挪到 Draw 里）。
            tag.TintedImage = CustomProjectiles.PrepareTint(def, shot.mProjectileType);
            if (!string.IsNullOrEmpty(def.Image))
            {
                tag.Image = def.Image;
            }
        }

        public static CustomBulletDef Of(Projectile shot)
        {
            return shot != null && CustomProjectiles.TryGetTag(shot, out ProjectileTag tag)
                ? tag.Bullet
                : null;
        }

        /// <summary>附魔：把一颗飞行中的子弹换成另一条注册定义。</summary>
        public static bool Convert(Projectile shot, string toId)
        {
            CustomBulletDef def = Find(toId);
            if (def == null)
            {
                return false;
            }
            // 先换弹种再 Attach：染色图是按载体弹种去取那张静态图的，顺序反了会备成旧弹种的图。
            shot.mProjectileType = def.Carrier;
            Attach(shot, def);
            return true;
        }

        internal static bool HasMotion(Projectile shot) => Of(shot)?.Motion != null;

        /// <summary>由 CustomProjectiles 的 Draw 路由调用：行为可以逐帧换图。</summary>
        internal static Image MotionImage(Projectile shot, ProjectileTag tag)
        {
            CustomBulletDef def = tag?.Bullet;
            if (def == null)
            {
                return null;
            }
            if (def.Motion != null)
            {
                Image dynamic = def.Motion.GetImage(shot);
                if (dynamic != null)
                {
                    return dynamic;
                }
            }
            return string.IsNullOrEmpty(def.Image) ? null : AssetStore.Get(def.Image);
        }

        /// <summary>
        /// 命中结算。返回 true 表示这颗注册子弹已经处理完，调用方不要再走原生逻辑。
        /// </summary>
        internal static bool TryImpact(Projectile shot, Zombie zombie,
            Action<Projectile, Zombie> orig)
        {
            CustomBulletDef def = Of(shot);
            if (def == null)
            {
                return false;
            }
            if (def.Impact != null)
            {
                return def.Impact.OnImpact(shot, zombie, orig);
            }
            ProjectileTag tag = CustomProjectiles.TagFor(shot);
            // 穿透额度在"撞上之前"判：咬完最后一只那一口它还得往前飞，
            // 到下一只新目标才落地消失。
            bool pierces = def.Pierce > 0 && tag.PierceLeft > 0;
            bool alreadyHit = zombie != null && tag.PierceHits != null &&
                tag.PierceHits.Contains(zombie);
            bool landed = zombie != null && !zombie.mDead && !alreadyHit &&
                zombie.EffectedByDamage(RangeFlagsOf(shot, def));
            if (def.Damage <= 0)
            {
                // 交给载体弹种自己结算：它的 DoImpact 里带着自己的命中判定和"弹消失"，
                // 所以这一档不支持穿透（要穿透就显式写 Damage）。
                orig(shot, zombie);
                Splash(shot, zombie, def);
                if (landed)
                {
                    ApplyPoison(shot, zombie, def);
                    ApplyBurn(shot, zombie, def);
                }
                return true;
            }
            if (landed)
            {
                zombie.TakeDamage(def.Damage, TakeFlagsOf(shot, zombie, def));
                ApplyPoison(shot, zombie, def);
                ApplyBurn(shot, zombie, def);
                if (pierces)
                {
                    tag.PierceHits ??= new HashSet<Zombie>();
                    tag.PierceHits.Add(zombie);
                    tag.PierceLeft--;
                }
            }
            Splash(shot, zombie, def);
            if (pierces || alreadyHit)
            {
                return true;
            }
            ImpactFx(shot);
            shot.Die();
            return true;
        }

        /// <summary>命中挂毒：两套掩码由 <c>TouchPoison</c> 按这颗弹继承，除非 spec 里显式写了。</summary>
        private static void ApplyPoison(Projectile shot, Zombie zombie, CustomBulletDef def)
        {
            if (def.Poison != null)
            {
                CustomCombat.ApplyPoison(zombie, def.Poison, shot);
            }
        }

        /// <summary>命中挂灼热：掩码同样按这颗弹继承，除非 spec 里显式写了。</summary>
        private static void ApplyBurn(Projectile shot, Zombie zombie, CustomBulletDef def)
        {
            if (def.Burn != null)
            {
                CustomBurn.ApplyBurn(zombie, def.Burn, shot);
            }
        }

        /// <summary>原生豌豆那套溅落表现（音效 + 落地粒子）；消失由调用方决定。</summary>
        private static void ImpactFx(Projectile shot)
        {
            if (shot.mApp == null)
            {
                return;
            }
            shot.mApp.PlayFoley(FoleyType.Splat);
            shot.mApp.AddTodParticle(shot.mPosX - 3f, shot.mPosY + 17f,
                shot.mRenderOrder + 1, ParticleEffect.PeaSplat);
        }

        /// <summary>同排邻居按加宽矩形补一刀，规则照引擎火球溅射（挨着就算）。</summary>
        private static void Splash(Projectile shot, Zombie target, CustomBulletDef def)
        {
            if (def.SplashDamage <= 0 || shot.mBoard == null)
            {
                return;
            }
            uint range = RangeFlagsOf(shot, def);
            float left = shot.mPosX - 15f;
            foreach (Zombie other in shot.mBoard.mZombies)
            {
                if (other == target || other.mDead || other.mRow != shot.mRow ||
                    !other.EffectedByDamage(range))
                {
                    continue;
                }
                TRect rect = other.GetZombieRect();
                if (Math.Min(left + def.SplashWidth, rect.mX + rect.mWidth) -
                    Math.Max(left, rect.mX) < 0)
                {
                    continue;
                }
                other.TakeDamage(def.SplashDamage, TakeFlagsOf(shot, other, def));
            }
        }
    }
}
