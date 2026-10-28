using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>牛蒡击球手：近战挥棒，并把经过身边的豌豆强化；大招让下一次挥棒变成处决。</summary>
    public sealed class NbjqsPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 5,
            SpineName = "nbjqs",
            Cost = 100,
            RefreshTime = 750,
            LaunchRate = 60,
            Name = "牛蒡击球手",
            CardOffsetY = -17f,
            CardAsset = "nbjqs_card",
            AlmanacRow = 15,
            AlmanacColumn = 3,
            ChooserColumn = 2,
            ChooserRow = 14,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 1.728f,
            PreviewDx = 134.2f,
            PreviewDy = 188.9f,
            BodyScale = 1.728f,
            BodyDx = -133.2f,
            BodyDy = -182.9f,
            Factory = () => new NbjqsPlant(),
        };

        public const int AttackFrames = 40;
        public const int HitFrame = 20;
        public const int BoostStart = 0;
        public const int BoostEnd = 10;
        public const int MeleeDamage = 100;
        public const int UltimateGiantDamage = 600;
        public const int SaBulletDamage = 1800;

        private sealed class Meteor
        {
            public Board Board;
            public int Row;
            public float X;
            public float Y;
            public float TargetX;
            public float TargetY;
            public int Age;
            public int Duration = 36;
            public int Damage;
        }

        private sealed class SaBullet
        {
            public Board Board;
            public int Row;
            public float X;
            public float Y;
            public float Vx;
            public float Vy;
            public readonly HashSet<Zombie> Hit = new HashSet<Zombie>();
        }

        private static readonly List<Meteor> Meteors = new List<Meteor>();
        private static readonly List<SaBullet> SaBullets = new List<SaBullet>();

        private string _mode = "stand";
        private bool _ultimate;
        private Zombie _target;
        private bool _hitDone;
        private readonly HashSet<Projectile> _boosted = new HashSet<Projectile>();
        private readonly List<Projectile> _pending = new List<Projectile>();
        private readonly Dictionary<Projectile, float> _pendingVelocities =
            new Dictionary<Projectile, float>();

        public override bool CanUseUltimate => !_ultimate;

        public override bool UltimateActive => _ultimate;

        public override void OnPlanted()
        {
            _mode = "stand";
        }

        public override void StartUltimate()
        {
            ReleaseAllQueued();
            _ultimate = true;
            _mode = "stand";
            _target = null;
            _hitDone = false;
            _boosted.Clear();
            _pending.Clear();
            _pendingVelocities.Clear();
            Play("anim_sa-stand", ReanimLoopType.Loop, 30f);
        }

        public override void OnRemoved()
        {
            ReleaseAllQueued();
        }

        public override void OnUpdate()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            float animTime = AnimTime;
            int animationFrame = (int)(animTime * AttackFrames + 0.5f);
            if (_mode == "attack_melee")
            {
                QueueProjectiles();
                if (!_hitDone && animationFrame >= HitFrame)
                {
                    Zombie zombie = _target;
                    if (zombie != null && !zombie.mDead && !zombie.IsDeadOrDying())
                    {
                        if (_ultimate)
                        {
                            if (zombie.mZombieType == ZombieType.Boss ||
                                CustomCombat.IsGiantZombie(zombie))
                            {
                                zombie.TakeDamage(UltimateGiantDamage, 0u);
                            }
                            else
                            {
                                zombie.TakeDamage(
                                    CustomCombat.ZombieEffectiveHealth(zombie) + 1, 0u);
                            }
                            _ultimate = false;
                        }
                        else
                        {
                            zombie.TakeDamage(MeleeDamage, 0u);
                        }
                    }
                    _hitDone = true;
                }
                if (animTime >= 0.999f)
                {
                    if (_pending.Count > 0)
                    {
                        _mode = "attack_boost";
                        _target = null;
                        _boosted.Clear();
                        Play(_ultimate ? "anim_sa-attack" : "anim_attack",
                            ReanimLoopType.PlayOnceAndHold, 30f);
                        return;
                    }
                    _mode = "stand";
                    _target = null;
                    Play(_ultimate ? "anim_sa-stand" : "anim_stand",
                        ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (_mode == "attack_boost")
            {
                if (animationFrame <= BoostEnd)
                {
                    QueueProjectiles();
                }
                if (animationFrame >= BoostStart && animationFrame <= BoostEnd)
                {
                    for (int i = _pending.Count - 1; i >= 0; i--)
                    {
                        Projectile projectile = _pending[i];
                        if (projectile.mDead || _boosted.Contains(projectile))
                        {
                            _pending.RemoveAt(i);
                            continue;
                        }
                        _boosted.Add(projectile);
                        ReleaseQueued(projectile);
                        BoostProjectile(projectile, _ultimate);
                        _pending.RemoveAt(i);
                    }
                }
                if (animTime >= 0.999f)
                {
                    if (_ultimate)
                    {
                        _ultimate = false;
                    }
                    ReleaseAllQueued();
                    _mode = "stand";
                    _boosted.Clear();
                    Play("anim_stand", ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (EligibleProjectiles().Count > 0)
            {
                _mode = "attack_boost";
                _boosted.Clear();
                QueueProjectiles();
                Play(_ultimate ? "anim_sa-attack" : "anim_attack",
                    ReanimLoopType.PlayOnceAndHold, 30f);
                return;
            }
            Zombie target = FindMeleeTarget();
            if (target != null)
            {
                _mode = "attack_melee";
                _target = target;
                _hitDone = false;
                Play(_ultimate ? "anim_sa-attack" : "anim_attack",
                    ReanimLoopType.PlayOnceAndHold, 30f);
            }
        }

        private Zombie FindMeleeTarget()
        {
            Board board = B;
            if (board == null)
            {
                return null;
            }
            float left = Plant.mX - 40;
            float right = Plant.mX + 120;
            Zombie best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mMindControlled ||
                    zombie.mRow != Plant.mRow)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float centerX = rect.mX + rect.mWidth * 0.5f;
                if (left <= centerX && centerX <= right)
                {
                    float distance = Math.Abs(centerX - (Plant.mX + 40));
                    if (distance < bestDistance)
                    {
                        best = zombie;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        public static CustomPeaKind? PeaKindOf(Projectile projectile)
        {
            CustomPeaKind? customKind = CustomProjectiles.GetKind(projectile);
            if (customKind == CustomPeaKind.Electric || customKind == CustomPeaKind.Poison)
            {
                return null;
            }
            if (customKind == CustomPeaKind.Fire || customKind == CustomPeaKind.Stone)
            {
                return customKind;
            }
            if (projectile.mProjectileType == ProjectileType.Pea)
            {
                return CustomPeaKind.Normal;
            }
            if (projectile.mProjectileType == ProjectileType.Snowpea)
            {
                return CustomPeaKind.Snow;
            }
            if (projectile.mProjectileType == ProjectileType.Fireball)
            {
                return CustomPeaKind.Fire;
            }
            return null;
        }

        private static int OriginalPeaDamage(CustomPeaKind kind)
        {
            if (kind == CustomPeaKind.Fire)
            {
                return 40;
            }
            if (kind == CustomPeaKind.Stone)
            {
                return CustomCombat.StonePeaImpactDamage;
            }
            return 20;
        }

        private bool ProjectileInCell(Projectile projectile)
        {
            if (projectile.mDead || projectile.mRow != Plant.mRow)
            {
                return false;
            }
            TRect rect = projectile.GetProjectileRect();
            return rect.mX + rect.mWidth >= Plant.mX &&
                   rect.mX <= Plant.mX + 80 &&
                   rect.mY + rect.mHeight >= Plant.mY &&
                   rect.mY <= Plant.mY + 100;
        }

        private List<Projectile> EligibleProjectiles()
        {
            var result = new List<Projectile>();
            Board board = B;
            if (board == null)
            {
                return result;
            }
            for (int i = 0; i < board.mProjectiles.Count; i++)
            {
                Projectile projectile = board.mProjectiles[i];
                if (PeaKindOf(projectile) != null && ProjectileInCell(projectile))
                {
                    result.Add(projectile);
                }
            }
            return result;
        }

        private void QueueProjectiles()
        {
            _pending.RemoveAll(p => p == null || p.mDead);
            var queued = new HashSet<Projectile>(_pending);
            queued.UnionWith(_boosted);
            foreach (Projectile projectile in EligibleProjectiles())
            {
                if (queued.Contains(projectile))
                {
                    continue;
                }
                _pending.Add(projectile);
                _pendingVelocities[projectile] = projectile.mVelX;
                projectile.mVelX = 0f;
                if (CustomProjectiles.TryGetTag(projectile, out ProjectileTag tag))
                {
                    tag.Frozen = true;
                }
                else
                {
                    CustomProjectiles.SetFrozen(projectile);
                }
                queued.Add(projectile);
            }
        }

        private void ReleaseQueued(Projectile projectile)
        {
            CustomProjectiles.SetFrozen(projectile, false);
            if (!projectile.mDead)
            {
                projectile.mVelX = _pendingVelocities.TryGetValue(
                    projectile, out float velocity) ? velocity : 3.33f;
            }
            _pendingVelocities.Remove(projectile);
        }

        private void ReleaseAllQueued()
        {
            foreach (Projectile projectile in _pending)
            {
                ReleaseQueued(projectile);
            }
            _pending.Clear();
            _pendingVelocities.Clear();
        }

        private void BoostProjectile(Projectile projectile, bool ultimate)
        {
            CustomPeaKind? kind = PeaKindOf(projectile);
            if (kind == null)
            {
                return;
            }
            int damage = OriginalPeaDamage(kind.Value);
            if (ultimate)
            {
                projectile.Die();
                AddSaProjectile(0f);
                AddSaProjectile(-15f);
                AddSaProjectile(15f);
                return;
            }
            int roll = TodCommon.RandRangeInt(1, 100);
            int nativeDamage = projectile.mProjectileType == ProjectileType.Fireball ? 40 : 20;
            if (roll <= 50)
            {
                CustomProjectiles.SetBoost(projectile, new NbjqsBoost
                {
                    Damage = damage,
                    Effect = "knockback",
                    Kind = kind.Value,
                    NativeDamage = nativeDamage,
                });
                return;
            }
            if (roll <= 95)
            {
                CustomProjectiles.SetBoost(projectile, new NbjqsBoost
                {
                    Damage = damage,
                    Effect = "stun",
                    Kind = kind.Value,
                    NativeDamage = nativeDamage,
                });
                Zombie target = NearestZombieInRow(
                    projectile.mBoard, projectile.mRow, projectile.mPosX);
                float targetX;
                float verticalOffset;
                if (target == null)
                {
                    targetX = projectile.mBoard.mWidth;
                    verticalOffset = 0f;
                }
                else
                {
                    targetX = target.ZombieTargetLeadX(50f) - 30f;
                    verticalOffset = target.GetZombieRect().mY - projectile.mPosY;
                }
                float distance = Math.Max(40f, targetX - projectile.mPosX);
                const float flightTime = 120f;
                projectile.mMotionType = ProjectileMotion.Lobbed;
                projectile.mVelX = distance / flightTime;
                projectile.mVelY = 0f;
                projectile.mVelZ = -7f + verticalOffset / flightTime;
                projectile.mAccZ = 0.115f;
                return;
            }
            AddMeteor(projectile, damage * 5);
            projectile.Die();
        }

        private void AddSaProjectile(float angleDegrees)
        {
            float speed = 4f;
            double angle = angleDegrees * Math.PI / 180.0;
            SaBullets.Add(new SaBullet
            {
                Board = B,
                Row = Plant.mRow,
                X = Plant.mX + 65,
                Y = Plant.mY + 35,
                Vx = (float)Math.Cos(angle) * speed,
                Vy = (float)Math.Sin(angle) * speed,
            });
        }

        private static Zombie NearestZombieInRow(Board board, int row, float startX)
        {
            Zombie target = null;
            float bestX = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mMindControlled ||
                    zombie.mRow != row)
                {
                    continue;
                }
                if (zombie.mZombieType == ZombieType.Boss && !CustomCombat.BossCanBeHit(zombie))
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float centerX = rect.mX + rect.mWidth * 0.5f;
                if (centerX >= startX && centerX < bestX)
                {
                    target = zombie;
                    bestX = centerX;
                }
            }
            return target;
        }

        private static void AddMeteor(Projectile projectile, int damage)
        {
            Board board = projectile.mBoard;
            Zombie target = NearestZombieInRow(board, projectile.mRow, projectile.mPosX);
            if (target == null)
            {
                return;
            }
            TRect rect = target.GetZombieRect();
            float targetX = rect.mX + rect.mWidth * 0.5f;
            float targetY = rect.mY + rect.mHeight * 0.5f;
            Meteors.Add(new Meteor
            {
                Board = board,
                Row = projectile.mRow,
                X = targetX - 150f,
                Y = targetY - 260f,
                TargetX = targetX,
                TargetY = targetY,
                Damage = damage,
            });
        }

        public static void UpdateEffects(Board board)
        {
            if (board == null)
            {
                return;
            }
            for (int index = Meteors.Count - 1; index >= 0; index--)
            {
                Meteor meteor = Meteors[index];
                if (meteor.Board != board)
                {
                    Meteors.RemoveAt(index);
                    continue;
                }
                meteor.Age++;
                float progress = Math.Min(1f, meteor.Age / (float)meteor.Duration);
                meteor.X += (meteor.TargetX - meteor.X) * 0.13f;
                meteor.Y += (meteor.TargetY - meteor.Y) * 0.13f;
                if (progress < 1f)
                {
                    continue;
                }
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mRow != meteor.Row)
                    {
                        continue;
                    }
                    if (zombie.mZombieType == ZombieType.Boss &&
                        !CustomCombat.BossCanBeHit(zombie))
                    {
                        continue;
                    }
                    TRect rect = zombie.GetZombieRect();
                    float centerX = rect.mX + rect.mWidth * 0.5f;
                    if (Math.Abs(centerX - meteor.TargetX) <= 40f)
                    {
                        zombie.TakeDamage(meteor.Damage, 0u);
                        if (CustomCombat.IsValidTarget(zombie) &&
                            zombie.mZombieType != ZombieType.Boss)
                        {
                            zombie.ApplyButter();
                            zombie.mButteredCounter = 240;
                        }
                    }
                }
                Meteors.RemoveAt(index);
            }

            for (int index = SaBullets.Count - 1; index >= 0; index--)
            {
                SaBullet shot = SaBullets[index];
                if (shot.Board != board)
                {
                    SaBullets.RemoveAt(index);
                    continue;
                }
                shot.X += shot.Vx;
                shot.Y += shot.Vy;
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (shot.Hit.Contains(zombie) || zombie.mDead || zombie.IsDeadOrDying())
                    {
                        continue;
                    }
                    if (zombie.mZombieType == ZombieType.Boss &&
                        !CustomCombat.BossCanBeHit(zombie))
                    {
                        continue;
                    }
                    TRect rect = zombie.GetZombieRect();
                    if (rect.mX - 20 <= shot.X && shot.X <= rect.mX + rect.mWidth + 20 &&
                        rect.mY - 20 <= shot.Y && shot.Y <= rect.mY + rect.mHeight + 20)
                    {
                        shot.Hit.Add(zombie);
                        if (zombie.mZombieType == ZombieType.Boss ||
                            CustomCombat.IsGiantZombie(zombie))
                        {
                            zombie.TakeDamage(SaBulletDamage, 0u);
                        }
                        else
                        {
                            zombie.TakeDamage(
                                CustomCombat.ZombieEffectiveHealth(zombie) + 1, 0u);
                        }
                    }
                }
                if (shot.X > board.mWidth + 80 || shot.Y < -100 ||
                    shot.Y > board.mHeight + 100)
                {
                    SaBullets.RemoveAt(index);
                }
            }
        }

        public static void DrawEffects(Board board, Graphics g)
        {
            if (board == null)
            {
                return;
            }
            Image meteorImage = AssetStore.Get("nbjqs_meteor");
            foreach (Meteor meteor in Meteors)
            {
                if (meteor.Board != board)
                {
                    continue;
                }
                g.DrawImage(meteorImage,
                    meteor.X - meteorImage.mWidth * 0.5f,
                    meteor.Y - meteorImage.mHeight * 0.5f);
            }
            Image bulletImage = AssetStore.Get("nbjqs_sa_bullet");
            float scale = 60f / Math.Max(1f, bulletImage.mHeight);
            foreach (SaBullet shot in SaBullets)
            {
                if (shot.Board != board)
                {
                    continue;
                }
                TodCommon.TodDrawImageScaledF(g, bulletImage,
                    shot.X - bulletImage.mWidth * scale * 0.5f,
                    shot.Y - bulletImage.mHeight * scale * 0.5f, scale, scale);
            }
        }
    }
}
