using System;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 死神大炮：不需要玩家点击瞄准，装填完成后自动向草坪中段（x=650）砸一发玉米加农炮。
    /// 弹道参数沿用原 Lua 版：几乎不前进的高抛弹，落点由 mCobTarget 决定。
    /// </summary>
    public sealed class DoomCannonPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "死神大炮",
            Cost = 1200,
            RefreshTime = 5000,
            LaunchRate = 600,
            NativeReanim = true,
            NativeReanimType = ReanimationType.Cobcannon,
            CardNativeSeed = SeedType.Cobcannon,
            HasPreview = false,
            StandAnim = string.Empty,
            Factory = () => new DoomCannonPlant(),
        };

        private const int AutoAimX = 650;

        public override void OnPlanted()
        {
            if (!Plant.IsInPlay())
            {
                return;
            }
            Plant.mState = PlantState.CobcannonArming;
            Plant.mStateCountdown = 10;
        }

        public override void OnUpdate()
        {
            if (B == null || !Plant.IsInPlay())
            {
                return;
            }
            Plant.UpdateCobCannon();
            if (Plant.mState == PlantState.CobcannonReady)
            {
                Plant.CobCannonFire(AutoAimX, Plant.mRow);
            }
            if (Plant.mState == PlantState.CobcannonFiring)
            {
                Plant.mState = PlantState.CobcannonArming;
                Plant.mStateCountdown = 300;
            }
        }

        public override void OnNativeFire(
            Action<Plant, Zombie, int, PlantWeapon> orig,
            Zombie target, int row, PlantWeapon weapon)
        {
            int x = Plant.mX - 30;
            int y = Plant.mY - 160;
            if (B.GetFlowerPotAt(Plant.mPlantCol, Plant.mRow) != null)
            {
                y -= 5;
            }
            Projectile projectile = B.AddProjectile(
                x, y, Plant.mRenderOrder - 1, row, ProjectileType.Cobbig);
            projectile.mDamageRangeFlags = CobDamageRangeFlags(weapon);
            projectile.mFromPlant = Plant.mSeedType;
            projectile.mMotionType = ProjectileMotion.Lobbed;
            projectile.mVelX = 0.001f;
            projectile.mVelY = 0f;
            projectile.mVelZ = -8f;
            projectile.mAccZ = 0f;
            projectile.mCobTargetX = Plant.mTargetX - 40;
            projectile.mCobTargetRow = Plant.mRow;
        }

        /// <summary>伤害范围表按 mSeedType 取，暂时借用加农炮的种子算。</summary>
        private int CobDamageRangeFlags(PlantWeapon weapon)
        {
            SeedType saved = Plant.mSeedType;
            Plant.mSeedType = SeedType.Cobcannon;
            try
            {
                return Plant.GetDamageRangeFlags(weapon);
            }
            finally
            {
                Plant.mSeedType = saved;
            }
        }
    }
}
