using System;
using System.Collections.Generic;
using Lawn;
using System.Reflection;
using Sexy;
using Sexy.TodLib;

namespace PGvZPlantKit
{
    /// <summary>
    /// 自定义植物行为基类。一个 Plant 实例对应一个行为实例；注册表在 PlantInitialize 后
    /// 创建行为、替换身体、设血量，并每帧回调 OnUpdate。
    ///
    /// C# 与 IronPython 都可以继承：
    ///   C#:            class MyPlant : CustomPlantBase { public override void OnUpdate() {...} }
    ///   IronPython:    class MyPlant(CustomPlantBase):
    ///                      def OnUpdate(self): ...
    /// 定义由 PlantKit.RegisterPlant(def) 绑定时自动写入，子类不需要实现 Definition。
    /// </summary>
    public abstract class CustomPlantBase
    {
        public CustomPlantDef Definition { get; internal set; }

        public Plant Plant { get; private set; }

        public Board B => Plant == null ? null : Plant.mBoard;

        public int Frame => B == null ? 0 : B.mMainCounter;

        public Reanimation Body => Plant == null || Plant.mApp == null
            ? null
            : Plant.mApp.ReanimationTryToGet(Plant.mBodyReanimID);

        public float AnimTime
        {
            get
            {
                Reanimation body = Body;
                return body == null ? 1f : body.mAnimTime;
            }
        }

        public bool AnimDone => AnimTime >= 0.999f;

        public bool IsAlive => Plant != null && !Plant.mDead && !Plant.mSquished;

        public bool HasCustomSpine => Definition != null && Definition.UsesCustomSpine;

        // ------------------------------------------------------------ 便捷坐标

        /// <summary>所在行（0 起）；没有实体时 -1。</summary>
        public int Row => Plant == null ? -1 : Plant.mRow;

        /// <summary>所在列（0 起）；没有实体时 -1。</summary>
        public int Col => Plant == null ? -1 : Plant.mPlantCol;

        /// <summary>格子中心附近的像素坐标（近似，供特效/掉阳光用）。</summary>
        public float CenterX => Plant == null ? 0f : Plant.mX + 40f;

        public float CenterY => Plant == null ? 0f : Plant.mY + 40f;

        // ------------------------------------------------------------ 查询助手

        /// <summary>某行（默认本行）最靠左的活僵尸，也就是最先走到植物跟前的那个。</summary>
        public Zombie FindFrontZombie(int row = -1)
        {
            Board board = B;
            if (board == null)
            {
                return null;
            }
            int targetRow = row < 0 ? Row : row;
            Zombie front = null;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie == null || zombie.mDead || zombie.mRow != targetRow)
                {
                    continue;
                }
                if (front == null || zombie.mX < front.mX)
                {
                    front = zombie;
                }
            }
            return front;
        }

        /// <summary>某行（默认本行）所有活僵尸；直接改这张表不影响棋盘。</summary>
        public List<Zombie> ZombiesInRow(int row = -1)
        {
            List<Zombie> result = new List<Zombie>();
            Board board = B;
            if (board == null)
            {
                return result;
            }
            int targetRow = row < 0 ? Row : row;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie != null && !zombie.mDead && zombie.mRow == targetRow)
                {
                    result.Add(zombie);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ 动作助手

        /// <summary>
        /// 从植物身上发射一颗弹。直射弹只给 type/dx/dy；抛物弹传 Lobbed 并补
        /// velX/velZ/accZ（与原生卷心菜投手同单位的合速度）。
        /// </summary>
        public Projectile FireProjectile(ProjectileType type, float dx = 60f, float dy = -10f,
            ProjectileMotion motion = ProjectileMotion.Straight,
            float velX = 0f, float velY = 0f, float velZ = 0f, float accZ = 0f,
            PlantWeapon weapon = PlantWeapon.Primary)
        {
            Plant plant = Plant;
            Board board = B;
            if (plant == null || board == null)
            {
                return null;
            }
            Projectile projectile = board.AddProjectile(
                (int)(plant.mX + dx), (int)(plant.mY + dy),
                plant.mRenderOrder - 1, plant.mRow, type);
            if (projectile == null)
            {
                return null;
            }
            projectile.mMotionType = motion;
            if (motion != ProjectileMotion.Straight)
            {
                projectile.mVelX = velX;
                projectile.mVelY = velY;
                projectile.mVelZ = velZ;
                projectile.mAccZ = accZ;
            }
            projectile.mDamageRangeFlags = plant.GetDamageRangeFlags(weapon);
            projectile.mFromPlant = plant.mSeedType;
            return projectile;
        }

        /// <summary>
        /// 在植物附近掉落一坨阳光（生产植物式 FromPlant 轨迹）。数值自动就近取
        /// 5/15/25/50 一档的原生阳光（Tinysun/Smallsun/Sun/Largesun）。
        /// </summary>
        public void DropSun(int value = 25, float dx = 0f, float dy = 0f)
        {
            Plant plant = Plant;
            Board board = B;
            if (plant == null || board == null)
            {
                return;
            }
            CoinType type = value >= 50 ? CoinType.Largesun
                : value >= 25 ? CoinType.Sun
                : value >= 15 ? CoinType.Smallsun
                : CoinType.Tinysun;
            board.AddCoin((int)(plant.mX + dx), (int)(plant.mY + dy), type, CoinMotion.FromPlant);
        }

        public void Heal(int amount)
        {
            Plant plant = Plant;
            if (plant == null || amount == 0)
            {
                return;
            }
            int max = plant.mPlantMaxHealth > 0 ? plant.mPlantMaxHealth : plant.mPlantHealth;
            plant.mPlantHealth = Math.Min(Math.Max(1, max), plant.mPlantHealth + amount);
        }

        /// <summary>对矩形区域（棋盘像素坐标）内的所有僵尸造成一次性伤害。</summary>
        public void AreaDamage(int damage, int x, int y, int w, int h)
        {
            Board board = B;
            if (board == null || damage <= 0)
            {
                return;
            }
            TRect area = new TRect(x, y, w, h);
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie == null || zombie.mDead)
                {
                    continue;
                }
                if (zombie.GetZombieRect().Intersects(area))
                {
                    zombie.TakeDamage(damage, 0);
                }
            }
        }

        /// <summary>
        /// 借用某个原生种子的攻击矩形打一行范围伤害（见 CustomCombat.RowAreaDamage）。
        /// 用法（IronPython）：self.RowAreaDamage(40, 2, SeedType.Fumeshroom)
        /// </summary>
        public void RowAreaDamage(int damage, uint flags = 2u, SeedType rectSeed = SeedType.None) =>
            CustomCombat.RowAreaDamage(Plant, damage, flags, rectSeed);

        // ------------------------------------------------------------ 节拍/定时语法

        private Dictionary<string, int> _everyLast;
        private readonly List<ScheduledCall> _scheduled = new List<ScheduledCall>();
        private List<object> _instanceHooks;

        /// <summary>
        /// 节拍器：每 interval 帧返回一次 True。可用 key 区分同一株植物上的多个节拍，
        /// OnPlanted 里先调一次用于"上弦"，OnUpdate 里的滴答就不会立刻触发。
        /// </summary>
        public bool Every(int interval, string key = null)
        {
            if (interval <= 0 || Plant == null)
            {
                return true;
            }
            key = key ?? string.Empty;
            if (_everyLast == null)
            {
                _everyLast = new Dictionary<string, int>();
            }
            if (!_everyLast.TryGetValue(key, out int last) || Frame - last >= interval)
            {
                _everyLast[key] = Frame;
                return true;
            }
            return false;
        }

        /// <summary>delayTicks 帧后回调一次；植物死亡时未触发的回调直接作废。</summary>
        public void Schedule(int delayTicks, Action callback)
        {
            if (callback == null)
            {
                return;
            }
            _scheduled.Add(new ScheduledCall { Due = Frame + Math.Max(0, delayTicks), Call = callback });
        }

        // ------------------------------------------------------------ 受管引擎钩子

        /// <summary>
        /// 挂一个只在这株植物存活期间生效的引擎钩子（传统 MonoModUtils.HookTo 写法的受管版）。
        /// 植物被移除时自动卸载一次；HookResult 由注册表永久持有，GC 终结器不会二次卸载。
        /// 在 OnPlanted 里调用即可，不用写重跑守卫。IronPython：
        ///   self.ManageHook(M.HookTo(Zombie.StartEating)(self._on_any_eating))
        /// </summary>
        public object ManageHook(object hookResult)
        {
            if (hookResult == null)
            {
                throw new ArgumentNullException(nameof(hookResult));
            }
            (_instanceHooks ??= new List<object>()).Add(hookResult);
            return hookResult;
        }

        /// <summary>植物移除时由注册表调用：卸载全部实例钩子（每个只卸一次）。</summary>
        internal void ReleaseInstanceHooks()
        {
            if (_instanceHooks == null)
            {
                return;
            }
            foreach (object result in _instanceHooks)
            {
                try
                {
                    MethodInfo unhook = result.GetType().GetMethod("UnHook", Type.EmptyTypes);
                    unhook?.Invoke(result, null);
                }
                catch (Exception ex)
                {
                    ModLog.Error("instance hook UnHook failed for " +
                                 (Definition != null ? Definition.Name : "?"), ex);
                }
            }
            // 引用移交注册表永久持有，防止终结器把已卸载的钩子再卸一遍。
            foreach (object result in _instanceHooks)
            {
                HookRegistry.Bury(result);
            }
            _instanceHooks = null;
        }

        internal struct ScheduledCall
        {
            public int Due;
            public Action Call;
        }

        private int _lastHealth = -1;

        /// <summary>每帧由注册表在 OnUpdate 之前调用：血量变化派发 OnBitten，并走定时器。</summary>
        internal void Tick()
        {
            Plant plant = Plant;
            if (plant != null && !plant.mDead)
            {
                int health = plant.mPlantHealth;
                if (_lastHealth > 0 && health < _lastHealth)
                {
                    try
                    {
                        OnBitten(_lastHealth - health);
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error("OnBitten failed for " + (Definition != null ? Definition.Name : "?"), ex);
                    }
                }
                _lastHealth = health;
            }
            if (_scheduled.Count > 0)
            {
                int now = Frame;
                for (int i = _scheduled.Count - 1; i >= 0; i--)
                {
                    if (now < _scheduled[i].Due)
                    {
                        continue;
                    }
                    Action call = _scheduled[i].Call;
                    _scheduled.RemoveAt(i);
                    try
                    {
                        call();
                    }
                    catch (Exception ex)
                    {
                        ModLog.Error("scheduled call failed for " +
                                     (Definition != null ? Definition.Name : "?"), ex);
                    }
                }
            }
        }

        internal void Attach(Plant plant)
        {
            Plant = plant;
        }

        internal void AfterNativeInitialize()
        {
            CustomPlantDef def = Definition;
            if (def.Health > 0)
            {
                Plant.mPlantHealth = def.Health;
                Plant.mPlantMaxHealth = def.MaxHealth > 0 ? def.MaxHealth : def.Health;
            }
            if (!def.NativeReanim)
            {
                CreateCustomBody();
            }
            else if (!string.IsNullOrEmpty(def.StandAnim))
            {
                Play(def.StandAnim, def.StandLoop, 30f);
            }
            OnPlanted();
            CustomPlantRegistry.StartPacketCooldown(Plant, def);
            AlignBody();
        }

        /// <summary>给已经存在的 Plant（例如南瓜壳转小守卫菇）挂上行为并换身体。</summary>
        public void SetupAttachedPlant()
        {
            CustomPlantDef def = Definition;
            if (def.Health > 0)
            {
                Plant.mPlantHealth = def.Health;
                Plant.mPlantMaxHealth = def.MaxHealth > 0 ? def.MaxHealth : def.Health;
            }
            CreateCustomBody();
            OnPlanted();
            AlignBody();
        }

        /// <summary>把原生临时身体换成该植物自己的 Spine 身体。</summary>
        public void CreateCustomBody()
        {
            CustomPlantDef def = Definition;
            if (def.NativeReanim || !def.UsesCustomAnimation)
            {
                return;
            }
            Reanimation temporary = Plant.mApp.ReanimationTryToGet(Plant.mBodyReanimID);
            if (temporary != null)
            {
                temporary.ReanimationDie();
            }
            Plant.mBodyReanimID = null;
            Reanimation body = Plant.mApp.AddReanimation(
                0f, 0f, Plant.mRenderOrder + def.BodyRenderOffset, def.ReanimType);
            body.mIsAttachment = true;
            Plant.mBodyReanimID = Plant.mApp.ReanimationGetID(body);
            if (!string.IsNullOrEmpty(def.StandAnim))
            {
                Play(def.StandAnim, def.StandLoop, 30f);
            }
            AlignBody();
        }

        public void Play(string animName, ReanimLoopType loop, float rate = 30f)
        {
            Reanimation body = Body;
            if (body == null || string.IsNullOrEmpty(animName))
            {
                return;
            }
            string track = animName.StartsWith("anim_") ? animName : "anim_" + animName;
            if (body.mDefinition == null)
            {
                return;
            }
            // Spine 的缺失动画会抛异常；非 Spine 的轨道查找本身是安全的。
            if (body.mDefinition.mIsSpine && !body.TrackExists(track))
            {
                return;
            }
            body.PlayReanim(track, loop, 0, rate);
        }

        public void AlignBody()
        {
            Reanimation body = Body;
            if (body == null)
            {
                return;
            }
            CustomPlantDef def = Definition;
            if (def.NativeReanim && !def.HasBodyTransform)
            {
                return;
            }
            body.OverrideScale(def.BodyScale, def.BodyScale);
            float scale = def.BodyUsesS ? Sexy.Constants.S : 1f;
            body.SetPosition(def.BodyDx * scale, def.BodyDy * scale);
        }

        public void SetAnimTime(float time)
        {
            Reanimation body = Body;
            if (body != null)
            {
                body.mAnimTime = time;
            }
        }

        public virtual void OnPlanted()
        {
        }

        public virtual void OnUpdate()
        {
        }

        public virtual void OnRemoved()
        {
        }

        /// <summary>返回 true 表示已处理点击，不再调用原生 Plant.MouseDown。</summary>
        public virtual bool OnMouseDown(int x, int y, int clickCount)
        {
            return false;
        }

        /// <summary>返回 true 表示已处理压扁（例如小守卫菇的立即消失），不再调用原生 Squish。</summary>
        public virtual bool OnSquish()
        {
            return false;
        }

        /// <summary>原生 Plant.Fire 被调用时；默认原样转发。可覆写为自定义射击控制器。</summary>
        public virtual void OnNativeFire(
            System.Action<Plant, Zombie, int, PlantWeapon> orig,
            Zombie target, int row, PlantWeapon weapon)
        {
            orig(Plant, target, row, weapon);
        }

        /// <summary>图鉴预览创建后调用，用于播放特殊待机动画或阻止预览产生资源。</summary>
        public virtual void OnAlmanacPreview()
        {
        }

        /// <summary>血量下降时（含被啃咬、碾压等一切掉血来源；由基类逐帧比对得出）。</summary>
        public virtual void OnBitten(int amount)
        {
        }

        /// <summary>有僵尸开始啃这株植物时（Zombie.StartEating 后按目标派发）。</summary>
        public virtual void OnEatenStart(Zombie eater)
        {
        }

        /// <summary>同一行有僵尸死亡时（DieNoLoot 全路径都会触发一次，含掉宝死亡）。</summary>
        public virtual void OnZombieDied(Zombie zombie)
        {
        }

        /// <summary>新一波僵尸出现时（波号从 1 起，对棋盘上所有自定义植物广播）。</summary>
        public virtual void OnWave(int wave)
        {
        }

        /// <summary>关卡正式开始（首次进入 Playing）时对棋盘上所有自定义植物广播一次。</summary>
        public virtual void OnLevelStart()
        {
        }

        /// <summary>每帧在植物本体之上自绘（光圈、计数器等）；抛异常只记日志不中断。</summary>
        public virtual void OnDrawTop(Graphics g)
        {
        }

    }
}
