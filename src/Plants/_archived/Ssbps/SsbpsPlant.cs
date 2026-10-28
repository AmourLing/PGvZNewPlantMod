using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>桑葚爆破手：投掷普通/毒/闪光/巨爆四种爆弹；大招向最多 6 只僵尸空投。</summary>
    public sealed class SsbpsPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 3,
            SpineName = "ssbps",
            Cost = 150,
            RefreshTime = 750,
            LaunchRate = 90,
            Name = "桑葚爆破手",
            CardOffsetY = -12f,
            CardAsset = "ssbps_card",
            AlmanacRow = 15,
            AlmanacColumn = 1,
            ChooserColumn = 0,
            ChooserRow = 14,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 0.72f,
            PreviewDx = 35f,
            PreviewDy = 88f,
            BodyScale = 1.7f,
            BodyDx = -32f,
            BodyDy = -57f,
            BodyUsesS = true,
            HasCursorOverride = true,
            CursorScale = 1.7f,
            CursorDx = -22f,
            CursorDy = -72f,
            Factory = () => new SsbpsPlant(),
        };

        public const int AttackFrames = 40;
        public const int SaFrames = 90;
        public const int SaFireStart = 40;
        public const int SaFireEnd = 90;
        public const int SaTargetCount = 6;
        public const float ProjectileSpeed = 3.33f;
        public const int UltimateFlightTime = 60;
        public const float UltimateArcHeight = 120f;

        public const int Normal = 0;
        public const int Poison = 1;
        public const int Flash = 2;
        public const int Big = 3;

        private sealed class Shot
        {
            public SsbpsPlant Owner;
            public Plant Plant;
            public int Kind;
            public float X;
            public float Y;
            public int Row;
            public Zombie Target;
            public bool Ultimate;
            public float Rotation;
            public float StartX;
            public float StartY;
            public float TargetX;
            public float TargetY;
            public int FlightAge;
            public int FlightTime;
            public bool Dead;
        }

        private static readonly List<Shot> Shots = new List<Shot>();

        private string _mode = "stand";
        private int _nextAttack;
        private int _saFired;
        private readonly List<Zombie> _saTargets = new List<Zombie>();

        public override bool CanUseUltimate => _mode != "ultimate";

        public override bool UltimateActive => _mode == "ultimate";

        public override void OnPlanted()
        {
            _mode = "stand";
            _nextAttack = 0;
        }

        public override void OnUpdate()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            float animTime = AnimTime;
            int frame = Frame;
            if (_mode == "attack")
            {
                if (animTime >= 0.999f)
                {
                    AddProjectile(Plant, ChooseBulletKind(), null);
                    _mode = "attack_recovery";
                    Play("anim_stand", ReanimLoopType.PlayOnceAndHold, 30f);
                }
                return;
            }
            if (_mode == "attack_recovery")
            {
                if (animTime >= 0.999f)
                {
                    _mode = "stand";
                    _nextAttack = frame;
                    Play("anim_stand", ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (_mode == "ultimate")
            {
                int animationFrame = (int)(animTime * SaFrames + 0.5f);
                int targetCount = _saTargets.Count;
                while (_saFired < targetCount)
                {
                    int fireFrame = SaFireStart +
                        (SaFireEnd - SaFireStart) * _saFired / Math.Max(1, targetCount);
                    if (animationFrame < fireFrame)
                    {
                        break;
                    }
                    AddUltimateProjectile(Plant, _saTargets[_saFired]);
                    _saFired++;
                }
                if (animTime >= 0.999f)
                {
                    _mode = "stand";
                    _nextAttack = frame + 90;
                    Play("anim_stand", ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (frame >= _nextAttack && HasTarget(Plant))
            {
                _mode = "attack";
                Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 30f);
            }
        }

        public override void StartUltimate()
        {
            _mode = "ultimate";
            _saFired = 0;
            _saTargets.Clear();
            var candidates = new List<Zombie>();
            Board board = B;
            if (board != null)
            {
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (IsValidTarget(zombie))
                    {
                        candidates.Add(zombie);
                    }
                }
            }
            for (int i = 0; i < SaTargetCount; i++)
            {
                _saTargets.Add(candidates.Count > 0
                    ? candidates[TodCommon.RandRangeInt(0, candidates.Count - 1)]
                    : null);
            }
            Play("anim_sa", ReanimLoopType.PlayOnceAndHold, 30f);
        }

        public override void OnRemoved()
        {
            foreach (Shot shot in Shots)
            {
                if (shot.Owner == this)
                {
                    shot.Dead = true;
                }
            }
        }

        private static bool IsValidTarget(Zombie zombie)
        {
            if (zombie == null || zombie.mDead || zombie.IsDeadOrDying())
            {
                return false;
            }
            if (zombie.mZombieType == ZombieType.Boss)
            {
                return CustomCombat.BossCanBeHit(zombie);
            }
            return true;
        }

        private static bool HasTarget(Plant plant)
        {
            Board board = plant.mBoard;
            if (board == null)
            {
                return false;
            }
            Zombie native = plant.FindTargetZombie(plant.mRow, PlantWeapon.Primary);
            if (native != null && IsValidTarget(native))
            {
                return true;
            }
            uint damageFlags = (uint)plant.GetDamageRangeFlags(PlantWeapon.Primary);
            TRect attackRect = plant.GetPlantAttackRect(PlantWeapon.Primary);
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (!IsValidTarget(zombie) || zombie.mMindControlled ||
                    !zombie.EffectedByDamage(damageFlags))
                {
                    continue;
                }
                if (zombie.mZombieType != ZombieType.Boss && zombie.mRow != plant.mRow)
                {
                    continue;
                }
                if (GameConstants.GetRectOverlap(attackRect, zombie.GetZombieRect()) > 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int ChooseBulletKind()
        {
            int roll = TodCommon.RandRangeInt(1, 100);
            if (roll <= 65)
            {
                return Normal;
            }
            if (roll <= 80)
            {
                return Poison;
            }
            if (roll <= 95)
            {
                return Flash;
            }
            return Big;
        }

        private static Shot AddProjectile(Plant plant, int kind, Zombie target)
        {
            float x = plant.mBoard.GridToPixelX(plant.mPlantCol, plant.mRow) + 58f;
            float y = plant.mBoard.GetPosYBasedOnRow(x, plant.mRow) + 18f;
            Shot shot = new Shot
            {
                Owner = CustomPlantRegistry.Get(plant) as SsbpsPlant,
                Plant = plant,
                Kind = kind,
                X = x,
                Y = y,
                Row = plant.mRow,
                Target = target,
            };
            Shots.Add(shot);
            return shot;
        }

        private static Shot AddUltimateProjectile(Plant plant, Zombie target)
        {
            Board board = plant.mBoard;
            float startX = board.GridToPixelX(plant.mPlantCol, plant.mRow) + 58f;
            float startY = board.GetPosYBasedOnRow(startX, plant.mRow) + 18f;
            float targetX;
            float targetY;
            if (target != null && IsValidTarget(target))
            {
                TRect rect = target.GetZombieRect();
                targetX = rect.mX + rect.mWidth * 0.5f;
                targetY = rect.mY + rect.mHeight * 0.5f;
            }
            else
            {
                int targetCol = TodCommon.RandRangeInt(1, 8);
                int targetRow = TodCommon.RandRangeInt(0, 4);
                targetX = board.GridToPixelX(targetCol, targetRow) + 40f;
                targetY = board.GetPosYBasedOnRow(targetX, targetRow) + 18f;
            }
            Shot shot = new Shot
            {
                Owner = CustomPlantRegistry.Get(plant) as SsbpsPlant,
                Plant = plant,
                Kind = Normal,
                X = startX,
                Y = startY,
                Row = plant.mRow,
                Target = target,
                Ultimate = true,
                StartX = startX,
                StartY = startY,
                TargetX = targetX,
                TargetY = targetY,
                FlightTime = UltimateFlightTime,
            };
            Shots.Add(shot);
            return shot;
        }

        private static List<Zombie> AreaTargets(Board board, float centerX, float centerY, int radius)
        {
            int gridX = board.PixelToGridXKeepOnBoard((int)centerX, (int)centerY);
            int gridY = board.PixelToGridYKeepOnBoard((int)centerX, (int)centerY);
            var result = new List<Zombie>();
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (!IsValidTarget(zombie))
                {
                    continue;
                }
                if (zombie.mZombieType == ZombieType.Boss)
                {
                    TRect rect = zombie.GetZombieRect();
                    float padding = radius * 80f;
                    if (rect.mX - padding <= centerX && centerX <= rect.mX + rect.mWidth + padding &&
                        rect.mY - padding <= centerY && centerY <= rect.mY + rect.mHeight + padding)
                    {
                        result.Add(zombie);
                    }
                    continue;
                }
                int zombieX = board.PixelToGridXKeepOnBoard((int)zombie.mPosX, (int)zombie.mPosY);
                if (Math.Abs(zombieX - gridX) <= radius && Math.Abs(zombie.mRow - gridY) <= radius)
                {
                    result.Add(zombie);
                }
            }
            return result;
        }

        private static void Explode(Shot shot, float impactX, float impactY, Zombie primaryTarget)
        {
            Board board = shot.Plant.mBoard;
            int kind = shot.Kind;
            int radius = kind == Normal ? 0 : 1;
            List<Zombie> targets;
            if (primaryTarget != null && IsValidTarget(primaryTarget))
            {
                int targetCol = board.PixelToGridXKeepOnBoard(
                    (int)primaryTarget.mPosX, (int)primaryTarget.mPosY);
                int targetRow = primaryTarget.mRow;
                targets = new List<Zombie>();
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (!IsValidTarget(zombie))
                    {
                        continue;
                    }
                    if (zombie.mZombieType == ZombieType.Boss)
                    {
                        if (zombie == primaryTarget)
                        {
                            targets.Add(zombie);
                        }
                        continue;
                    }
                    int zombieCol = board.PixelToGridXKeepOnBoard(
                        (int)zombie.mPosX, (int)zombie.mPosY);
                    if (Math.Abs(zombieCol - targetCol) <= radius &&
                        Math.Abs(zombie.mRow - targetRow) <= radius)
                    {
                        targets.Add(zombie);
                    }
                }
            }
            else
            {
                targets = AreaTargets(board, impactX, impactY, radius);
            }
            foreach (Zombie zombie in targets)
            {
                if (kind == Normal)
                {
                    zombie.TakeDamage(180, 0u);
                }
                else if (kind == Poison)
                {
                    int maximumHealth = CustomCombat.ZombieMaximumHealth(zombie);
                    zombie.TakeDamage(360, 0u);
                    if (CustomCombat.IsValidTarget(zombie))
                    {
                        CustomCombat.ApplySsbpsPoison(zombie, maximumHealth);
                    }
                }
                else if (kind == Flash)
                {
                    zombie.TakeDamage(360, 0u);
                    if (CustomCombat.IsValidTarget(zombie))
                    {
                        zombie.ApplyButter();
                        zombie.mButteredCounter = Math.Max(zombie.mButteredCounter, 240);
                    }
                }
                else if (zombie.mZombieType != ZombieType.Boss &&
                         !CustomCombat.IsGiantZombie(zombie))
                {
                    zombie.TakeDamage(CustomCombat.ZombieEffectiveHealth(zombie) + 1, 0u);
                }
                else
                {
                    zombie.TakeDamage(1600, 0u);
                }
            }
        }

        public static void UpdateProjectiles(Board board)
        {
            if (board == null || Shots.Count == 0)
            {
                return;
            }
            float rightEdge = board.mWidth + 40f;
            for (int index = Shots.Count - 1; index >= 0; index--)
            {
                Shot shot = Shots[index];
                Plant plant = shot.Plant;
                if (shot.Dead || plant == null || plant.mDead || plant.mBoard != board)
                {
                    Shots.RemoveAt(index);
                    continue;
                }
                if (shot.Ultimate)
                {
                    shot.FlightAge++;
                    float progress = Math.Min(1f,
                        (float)shot.FlightAge / Math.Max(1, shot.FlightTime));
                    shot.X = shot.StartX + (shot.TargetX - shot.StartX) * progress;
                    float groundY = shot.StartY + (shot.TargetY - shot.StartY) * progress;
                    shot.Y = groundY - UltimateArcHeight * 4f * progress * (1f - progress);
                    if (progress >= 1f)
                    {
                        Explode(shot, shot.TargetX, shot.TargetY, shot.Target);
                        Shots.RemoveAt(index);
                    }
                    continue;
                }
                Zombie target = shot.Target;
                if (target != null && IsValidTarget(target))
                {
                    TRect rect = target.GetZombieRect();
                    float targetX = rect.mX + rect.mWidth * 0.5f;
                    float targetY = rect.mY + rect.mHeight * 0.5f;
                    float dx = targetX - shot.X;
                    float dy = targetY - shot.Y;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance <= ProjectileSpeed * 2f)
                    {
                        Explode(shot, targetX, targetY, target);
                        Shots.RemoveAt(index);
                        continue;
                    }
                    shot.X += ProjectileSpeed * dx / distance;
                    shot.Y += ProjectileSpeed * dy / distance;
                }
                else
                {
                    shot.X += ProjectileSpeed;
                    shot.Y = board.GetPosYBasedOnRow(shot.X, shot.Row) + 18f;
                }
                shot.Rotation += 0.22f;
                if (shot.X > rightEdge)
                {
                    Shots.RemoveAt(index);
                    continue;
                }
                if (target == null)
                {
                    for (int i = 0; i < board.mZombies.Count; i++)
                    {
                        Zombie zombie = board.mZombies[i];
                        if (!IsValidTarget(zombie))
                        {
                            continue;
                        }
                        if (zombie.mZombieType != ZombieType.Boss && zombie.mRow != shot.Row)
                        {
                            continue;
                        }
                        TRect rect = zombie.GetZombieRect();
                        if (rect.mX - 20 <= shot.X && shot.X <= rect.mX + rect.mWidth + 20)
                        {
                            Explode(shot, shot.X, shot.Y, zombie);
                            Shots.RemoveAt(index);
                            break;
                        }
                    }
                }
            }
        }

        public static void DrawProjectiles(Board board, Graphics g)
        {
            if (board == null)
            {
                return;
            }
            foreach (Shot shot in Shots)
            {
                if (shot.Dead || shot.Plant == null || shot.Plant.mBoard != board)
                {
                    continue;
                }
                float rotation = shot.Ultimate ? 0f : shot.Rotation;
                Image image = RotatedImage(shot.Kind, rotation);
                g.DrawImageF(image, shot.X - image.mWidth * 0.5f,
                    shot.Y + 60f * Sexy.Constants.S - image.mHeight * 0.5f);
            }
        }

        private static Image RotatedImage(int kind, float rotation)
        {
            int frame = ((int)(rotation * (8.0 / Math.PI) + 0.5f)) & 15;
            string prefix = kind == Poison ? "ssbps_poison"
                : kind == Flash ? "ssbps_flash"
                : kind == Big ? "ssbps_big"
                : "ssbps_normal";
            return AssetStore.Get(prefix + "_" + frame.ToString("00"));
        }
    }
}
