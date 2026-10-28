using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 巧克力炮弹：垂直上升 → 空中吸附落点 → 垂直下落 → 落点溅射 → 13 帧爆炸动画。
    /// 原生只有"灰烬直接抹掉"和"固定 1800 伤害"两种收场，都调不动数值，
    /// 所以这一发的溅射自己扫：够打死的交给 ApplyBurn 保住灰烬表现，打不死的扣 900。
    /// </summary>
    public sealed class ChocolateShell : ICustomBulletMotion
    {
        public const string BulletId = "pgvz.chocolate_cannon:shell";

        public const float RiseVelZ = 12f;
        private const float FallVelZ = 12f;
        private const float RiseSnapZ = -1000f;
        private const float DetonateZ = 0f;
        private const float BoomFps = 20f;
        private const int BoomFirst = 1;
        private const int BoomLast = 13;
        private const int SplashRadius = 95;
        private const int SplashRowRange = 1;
        private const int BlastDamage = 900;
        private const uint BlastDamageFlags = 18;

        public static readonly CustomBulletDef Def = new CustomBulletDef
        {
            Id = BulletId,
            Carrier = ProjectileType.Pea,
            Image = "chocalate_cannon_proj",
            SkipCollision = true,
            Motion = new ChocolateShell(),
        };

        public void Update(Projectile shot, Action<Projectile> orig)
        {
            ProjectileTag tag = CustomProjectiles.TagFor(shot);
            if (tag.BoomFrame > 0f)
            {
                AdvanceBoom(shot, tag);
                return;
            }
            if (shot.mVelZ < 0f)
            {
                if (shot.mPosZ <= RiseSnapZ)
                {
                    shot.mPosX = shot.mCobTargetX;
                    shot.mPosY = shot.mBoard.GetPosYBasedOnRow(shot.mCobTargetX, shot.mRow);
                    shot.mShadowY = shot.mPosY + 67f;
                    shot.mVelX = 0f;
                    shot.mVelZ = FallVelZ;
                }
                orig(shot);
                return;
            }
            if (shot.mPosZ >= DetonateZ)
            {
                Detonate(shot);
                return;
            }
            orig(shot);
        }

        public Image GetImage(Projectile shot)
        {
            if (!CustomProjectiles.TryGetTag(shot, out ProjectileTag tag) || tag.BoomFrame <= 0f)
            {
                return null;
            }
            int index = Math.Min(BoomLast, Math.Max(BoomFirst, (int)(tag.BoomFrame - 1f)));
            return AssetStore.Get("qklc_boom_" + index.ToString("D3"));
        }

        private static void AdvanceBoom(Projectile shot, ProjectileTag tag)
        {
            float frame = tag.BoomFrame - 1f + BoomFps / 100f;
            if ((int)frame > BoomLast)
            {
                tag.BoomFrame = 0f;
                shot.Die();
                return;
            }
            tag.BoomFrame = frame + 1f;
        }

        private static void Detonate(Projectile shot)
        {
            CustomProjectiles.TagFor(shot).BoomFrame = 1f;
            Blast(shot);
            if (shot.mApp != null)
            {
                shot.mApp.PlayFoley(FoleyType.Melonimpact);
            }
        }

        private static void Blast(Projectile shot)
        {
            Board board = shot.mBoard;
            if (board == null)
            {
                return;
            }
            // 引擎那句是 (int)mPosX + 80 / (int)mPosY + 40，取整再比，别拿浮点去蹭 1 像素边界。
            int centerX = (int)shot.mPosX + 40;
            int centerY = (int)shot.mPosY + 40;
            uint rangeFlags = (uint)shot.mDamageRangeFlags;
            foreach (Zombie zombie in board.mZombies.ToArray())
            {
                if (zombie.mDead || !zombie.EffectedByDamage(rangeFlags))
                {
                    continue;
                }
                int rowDelta = zombie.mZombieType == ZombieType.Boss
                    ? 0
                    : zombie.mRow - shot.mRow;
                if (rowDelta > SplashRowRange || rowDelta < -SplashRowRange)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                if (!CircleOverlapsRect(centerX, centerY, SplashRadius, rect))
                {
                    continue;
                }
                if (zombie.mBodyHealth <= BlastDamage)
                {
                    zombie.ApplyBurn();
                    continue;
                }
                zombie.TakeDamage(BlastDamage, BlastDamageFlags);
            }
        }

        /// <summary>圆-矩形重叠判据，逐分支照 GameConstants.GetRectOverlap 那套。</summary>
        private static bool CircleOverlapsRect(int cx, int cy, int radius, TRect rect)
        {
            int dx = 0;
            int dy = 0;
            bool outX = false;
            bool outY = false;
            if (cx < rect.mX)
            {
                outX = true;
                dx = rect.mX - cx;
            }
            else if (cx > rect.mX + rect.mWidth)
            {
                outX = true;
                dx = cx - rect.mX - rect.mWidth;
            }
            if (cy < rect.mY)
            {
                outY = true;
                dy = rect.mY - cy;
            }
            else if (cy > rect.mY + rect.mHeight)
            {
                outY = true;
                dy = cy - rect.mY - rect.mHeight;
            }
            if (!outX && !outY)
            {
                return true;
            }
            if (outX && outY)
            {
                return dx * dx + dy * dy <= radius * radius;
            }
            return outX ? dx <= radius : dy <= radius;
        }
    }
}
