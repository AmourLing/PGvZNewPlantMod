using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>电鳗香蕉：发射往返穿透电弹；大招秒杀中小型僵尸、重创大型。</summary>
    public sealed class DmxjPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 2,
            SpineName = "dmxj",
            Cost = 125,
            RefreshTime = 750,
            LaunchRate = 90,
            Name = "电鳗香蕉",
            CardAsset = "dmxj_card",
            AlmanacRow = 14,
            AlmanacColumn = 4,
            ChooserColumn = 3,
            ChooserRow = 13,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 0.66f,
            PreviewDx = 36f,
            PreviewDy = 84f,
            BodyScale = 1.716f,
            BodyDx = -28f,
            BodyDy = -60f,
            BodyUsesS = true,
            Factory = () => new DmxjPlant(),
        };

        public const int AttackFrames = 40;
        public const int SaFrames = 80;
        public const float NormalSpeed = 5.0f;
        public const float UltimateSpeed = 2.67f;
        public const int NormalDamage = 80;
        public const int UltimateDamage = 1800;
        public const int NormalRangeGrids = 6;

        private sealed class Shot
        {
            public DmxjPlant Owner;
            public Plant Plant;
            public float X;
            public float Y;
            public int Row;
            public int Direction = 1;
            public bool Ultimate;
            public float Speed;
            public float TurnX;
            public readonly HashSet<Zombie> HitOut = new HashSet<Zombie>();
            public readonly HashSet<Zombie> HitBack = new HashSet<Zombie>();
            public bool Dead;
        }

        private static readonly List<Shot> Shots = new List<Shot>();

        private string _mode = "stand";
        private Shot _shot;

        public override bool CanUseUltimate => _mode != "ultimate_anim";

        public override bool UltimateActive => _mode == "ultimate_anim";

        public override void OnPlanted()
        {
            _mode = "stand";
            _shot = null;
        }

        public override void OnUpdate()
        {
            Board board = B;
            if (board == null)
            {
                return;
            }
            bool animDone = AnimDone;
            if (_mode == "attack_anim")
            {
                if (animDone)
                {
                    _shot = AddProjectile(Plant, false);
                    _mode = "rest";
                    Play("anim_stand2", ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (_mode == "rest")
            {
                return;
            }
            if (_mode == "reverse")
            {
                UpdateReverseAnimation();
                return;
            }
            if (_mode == "ultimate_anim")
            {
                if (animDone)
                {
                    AddProjectile(Plant, true);
                    _mode = "stand";
                    Play("anim_stand", ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (_mode == "stand" && HasTarget(Plant))
            {
                _mode = "attack_anim";
                Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 30f);
            }
        }

        public override void StartUltimate()
        {
            if (_shot != null)
            {
                _shot.Dead = true;
                _shot = null;
            }
            _mode = "ultimate_anim";
            Play("anim_sa", ReanimLoopType.PlayOnceAndHold, 30f);
        }

        public override void OnRemoved()
        {
            _shot = null;
            foreach (Shot shot in Shots)
            {
                if (shot.Owner == this)
                {
                    shot.Dead = true;
                }
            }
        }

        private void UpdateReverseAnimation()
        {
            if (Body == null)
            {
                _mode = "stand";
                return;
            }
            SetAnimTime(Math.Max(0f, AnimTime - 1f / AttackFrames));
            if (AnimTime <= 0.001f)
            {
                _mode = "stand";
                Play("anim_stand", ReanimLoopType.Loop, 30f);
            }
        }

        private static bool HasTarget(Plant plant)
        {
            Board board = plant.mBoard;
            if (board == null)
            {
                return false;
            }
            float startX = board.GridToPixelX(plant.mPlantCol, plant.mRow) + 45;
            float maxX = startX + NormalRangeGrids * 80f;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying())
                {
                    continue;
                }
                bool isBoss = zombie.mZombieType == ZombieType.Boss;
                if (isBoss)
                {
                    if (!CustomCombat.BossCanBeHit(zombie))
                    {
                        continue;
                    }
                }
                else if (zombie.mRow != plant.mRow)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float centerX = rect.mX + rect.mWidth * 0.5f;
                if (centerX >= startX && centerX <= maxX)
                {
                    return true;
                }
            }
            return false;
        }

        private static Shot AddProjectile(Plant plant, bool ultimate)
        {
            Board board = plant.mBoard;
            int gridX = board.GridToPixelX(plant.mPlantCol, plant.mRow);
            float x = gridX + 55f;
            int yOffset = ultimate ? 10 : 50;
            float y = board.GetPosYBasedOnRow(x, plant.mRow) + yOffset;
            float rightEdge = board.RightEdge(0) + 80f;
            float turnX = ultimate ? rightEdge : Math.Min(rightEdge, x + NormalRangeGrids * 80f);
            Shot shot = new Shot
            {
                Owner = plant == null ? null : CustomPlantRegistry.Get(plant) as DmxjPlant,
                Plant = plant,
                X = x,
                Y = y,
                Row = plant.mRow,
                Direction = 1,
                Ultimate = ultimate,
                Speed = ultimate ? UltimateSpeed : NormalSpeed,
                TurnX = turnX,
            };
            Shots.Add(shot);
            if (!ultimate && shot.Owner != null)
            {
                shot.Owner._shot = shot;
            }
            return shot;
        }

        private static void DamageZombie(Shot shot, Zombie zombie)
        {
            bool wasAlive = CustomCombat.IsValidTarget(zombie);
            if (shot.Ultimate)
            {
                if (zombie.mZombieType == ZombieType.Boss || CustomCombat.IsGiantZombie(zombie))
                {
                    CustomCombat.DoElectricDamage(zombie, UltimateDamage, 0u, true);
                }
                else
                {
                    if (zombie.mHasShield || (int)zombie.mShieldType != 0)
                    {
                        zombie.DropShield(16u);
                        zombie.mShieldHealth = 0;
                    }
                    if (zombie.mHasHelm || (int)zombie.mHelmType != 0)
                    {
                        zombie.DropHelm(16u);
                        zombie.mHelmHealth = 0;
                    }
                    zombie.mBodyHealth = Math.Min(zombie.mBodyHealth, 1799);
                    zombie.ApplyBurn();
                }
                bool killed = wasAlive && (zombie.mDead || zombie.IsDeadOrDying());
                if (killed && TodCommon.RandRangeInt(1, 100) <= 18)
                {
                    CustomUltimate.AddStock(zombie.mBoard, 1);
                }
                return;
            }
            CustomCombat.DoElectricDamage(zombie, NormalDamage, 0u, true);
        }

        public static void UpdateProjectiles(Board board)
        {
            if (board == null || Shots.Count == 0)
            {
                return;
            }
            float rightEdge = board.RightEdge(0) + 80f;
            for (int index = Shots.Count - 1; index >= 0; index--)
            {
                Shot shot = Shots[index];
                Plant plant = shot.Plant;
                if (shot.Dead || plant == null || plant.mDead || plant.mBoard != board)
                {
                    shot.Dead = true;
                    Shots.RemoveAt(index);
                    continue;
                }
                shot.X += shot.Speed * shot.Direction;
                int yOffset = shot.Ultimate ? 10 : 50;
                shot.Y = board.GetPosYBasedOnRow(shot.X, shot.Row) + yOffset;
                float turnX = shot.Ultimate ? rightEdge : shot.TurnX;
                if (shot.Direction > 0 && shot.X >= turnX)
                {
                    if (shot.Ultimate)
                    {
                        Shots.RemoveAt(index);
                        continue;
                    }
                    shot.X = turnX;
                    shot.Direction = -1;
                }
                HashSet<Zombie> hitSet = shot.Direction > 0 ? shot.HitOut : shot.HitBack;
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (zombie.mDead || zombie.IsDeadOrDying() || hitSet.Contains(zombie))
                    {
                        continue;
                    }
                    bool isBoss = zombie.mZombieType == ZombieType.Boss;
                    if (isBoss)
                    {
                        if (!CustomCombat.BossCanBeHit(zombie))
                        {
                            continue;
                        }
                    }
                    else if (zombie.mRow != shot.Row)
                    {
                        continue;
                    }
                    TRect rect = zombie.GetZombieRect();
                    bool ignoresHeight = shot.Ultimate &&
                        (zombie.mZombieType == ZombieType.Balloon ||
                         (zombie.mZombieType == ZombieType.Digger &&
                          zombie.mZombiePhase == ZombiePhase.DiggerTunneling));
                    bool horizontalHit = rect.mX - 35 <= shot.X &&
                                         shot.X <= rect.mX + rect.mWidth + 35;
                    if (horizontalHit && (isBoss || ignoresHeight ||
                        (rect.mY - 25 <= shot.Y && shot.Y <= rect.mY + rect.mHeight + 25)))
                    {
                        hitSet.Add(zombie);
                        DamageZombie(shot, zombie);
                    }
                }
                if (!shot.Ultimate && shot.Direction < 0)
                {
                    float homeX = board.GridToPixelX(plant.mPlantCol, plant.mRow) + 45f;
                    if (shot.X <= homeX)
                    {
                        Shots.RemoveAt(index);
                        DmxjPlant owner = CustomPlantRegistry.Get(plant) as DmxjPlant;
                        if (owner != null)
                        {
                            owner._shot = null;
                            owner._mode = "reverse";
                            owner.Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 0f);
                            owner.SetAnimTime(1f);
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
                Image image = AssetStore.Get(shot.Ultimate ? "dmxj_sa" : "dmxj_shot");
                float scale = shot.Ultimate ? 0.336f : 0.108f;
                float scaleX = shot.Direction > 0 ? scale : -scale;
                float drawX = shot.X - image.mWidth * scale * 0.5f;
                if (scaleX < 0)
                {
                    drawX = shot.X + image.mWidth * scale * 0.5f;
                }
                float drawY = shot.Y - image.mHeight * scale * 0.5f;
                bool oldColorize = g.mColorizeImages;
                Microsoft.Xna.Framework.Color oldColor = g.mColor;
                if (shot.Ultimate)
                {
                    g.SetColorizeImages(true);
                    g.SetColor(new SexyColor(255, 255, 255, 180).Color);
                }
                try
                {
                    TodCommon.TodDrawImageScaledF(g, image, drawX, drawY, scaleX, scale);
                }
                finally
                {
                    g.SetColor(oldColor);
                    g.SetColorizeImages(oldColorize);
                }
            }
        }
    }
}
