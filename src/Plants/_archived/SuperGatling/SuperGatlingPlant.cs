using System;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>超级机枪射手：随机属性豌豆连射 + 低频 180 发扇形大招。</summary>
    public sealed class SuperGatlingPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 0,
            SpineName = "super_gatlingpea",
            Cost = 400,
            RefreshTime = 750,
            LaunchRate = 90,
            Name = "超级机枪射手",
            CardAsset = "super_gatling_card",
            AlmanacRow = 14,
            AlmanacColumn = 2,
            ChooserColumn = 1,
            ChooserRow = 13,
            NativeReanim = true,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 1.375f,
            PreviewDx = 48f,
            PreviewDy = 109f,
            BodyScale = 1.375f,
            BodyDx = -28f,
            BodyDy = -83f,
            BodyUsesS = true,
            Factory = () => new SuperGatlingPlant(),
        };

        public const int BaseShots = 4;
        public const int MaxShots = 7;
        public const int BurstChance = 5;
        public const int BurstCount = 180;
        public const int VolleyGap = 90;
        public const int ShotInterval = 6;
        public const int BurstPerFrame = 1;
        public const int AttackInterval = 90;
        public const int AttackAnimFrames = 65;
        public const int AttackFireFrame = 25;
        public const int BurstAnimFrames = 240;
        public const int BurstFireStart = 40;
        public const int BurstFireEnd = 220;

        private int _shots = BaseShots;
        private int _lastFireFrame = -1000;
        private CustomPeaKind _volleyKind = CustomPeaKind.Normal;
        private int _volleyFires;
        private int _pendingShots;
        private int _nextShotFrame;
        private int _burstRemaining;
        private int _burstFireStart;
        private int _burstFireEnd;
        private int _attackAnimUntil;
        private int _nextAttackFrame;

        public override bool CanUseUltimate => _burstRemaining == 0;

        public override bool UltimateActive => _burstRemaining > 0;

        public override void StartUltimate()
        {
            StartBurst();
        }

        public override void OnUpdate()
        {
            if (B == null)
            {
                return;
            }
            UpdateSuperGatlingAttack();
        }

        public override void OnNativeFire(
            Action<Plant, Zombie, int, PlantWeapon> orig,
            Zombie target, int row, PlantWeapon weapon)
        {
            TryStartVolley();
        }

        private Projectile AddProjectile(CustomPeaKind kind)
        {
            ProjectileType type = kind == CustomPeaKind.Snow
                ? ProjectileType.Snowpea
                : ProjectileType.Pea;
            Projectile projectile = Plant.mBoard.AddProjectile(
                Plant.mX + 45, (int)(Plant.mY + 10), Plant.mRenderOrder - 1,
                Plant.mRow, type);
            projectile.mDamageRangeFlags = Plant.GetDamageRangeFlags(PlantWeapon.Primary);
            projectile.mFromPlant = Plant.mSeedType;
            projectile.mVelY = 0f;
            CustomProjectiles.SetKind(projectile, kind);
            CustomProjectiles.MarkEdgeKill(projectile);
            return projectile;
        }

        private void FireBurstShot()
        {
            const float halfAngle = 22.5f;
            CustomPeaKind kind = (CustomPeaKind)TodCommon.RandRangeInt(0, 5);
            float angle = TodCommon.RandRangeFloat(-halfAngle, halfAngle);
            float velocityY = 3.33f * (float)Math.Tan(angle * Math.PI / 180.0);
            Projectile projectile = AddProjectile(kind);
            CustomProjectiles.SetFan(projectile, velocityY);
        }

        private void TryStartVolley()
        {
            int frame = Frame;
            if (frame - _lastFireFrame >= VolleyGap)
            {
                _volleyFires = 0;
                bool burst = TodCommon.RandRangeInt(1, 100) <= BurstChance;
                _volleyKind = (CustomPeaKind)TodCommon.RandRangeInt(0, 5);
                if (burst)
                {
                    StartBurst();
                }
                else
                {
                    _pendingShots = _shots;
                    _nextShotFrame = frame + AttackFireFrame;
                    Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 30f);
                    _attackAnimUntil = frame + AttackAnimFrames;
                }
            }
            _lastFireFrame = frame;
            _volleyFires++;
        }

        private void StartBurst()
        {
            _pendingShots = 0;
            _burstRemaining = BurstCount;
            _burstFireStart = Frame + BurstFireStart;
            _burstFireEnd = Frame + BurstFireEnd;
            _shots = Math.Min(MaxShots, _shots + 1);
            _lastFireFrame = Frame;
            Play("anim_sa", ReanimLoopType.PlayOnceAndHold, 30f);
            _attackAnimUntil = Frame + BurstAnimFrames;
            _nextAttackFrame = Frame;
        }

        private void UpdateSuperGatlingAttack()
        {
            int frame = Frame;
            UpdateFire(frame);
            if (_burstRemaining == 0 && _pendingShots == 0 &&
                frame >= _nextAttackFrame &&
                (_attackAnimUntil == 0 || frame >= _attackAnimUntil) &&
                HasTarget())
            {
                TryStartVolley();
                _nextAttackFrame = frame + AttackInterval;
            }
            if (_attackAnimUntil > 0 && frame >= _attackAnimUntil)
            {
                Play("anim_stand", ReanimLoopType.Loop, 30f);
                _attackAnimUntil = 0;
            }
        }

        private void UpdateFire(int frame)
        {
            if (_pendingShots > 0 && frame >= _nextShotFrame)
            {
                AddProjectile(_volleyKind);
                _pendingShots--;
                _nextShotFrame = frame + ShotInterval;
            }
            if (_burstRemaining > 0 && frame >= _burstFireStart && frame < _burstFireEnd)
            {
                int count = Math.Min(BurstPerFrame, _burstRemaining);
                for (int i = 0; i < count; i++)
                {
                    FireBurstShot();
                    _burstRemaining--;
                }
                if (_burstRemaining == 0)
                {
                    _nextAttackFrame = frame + AttackInterval;
                }
            }
        }

        private bool HasTarget()
        {
            return Plant.FindTargetZombie(Plant.mRow, PlantWeapon.Primary) != null;
        }
    }
}
