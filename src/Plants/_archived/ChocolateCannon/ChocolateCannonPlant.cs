using System;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 巧克力大炮：像玉米加农炮那样自动蓄力，但不需要玩家指点落点，固定砸向草坪中段。
    /// 炮弹是一颗注册子弹（载体用豌豆），弹道、落点和爆炸帧都由 ChocolateShell 接管。
    /// </summary>
    public sealed class ChocolateCannonPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "巧克力大炮",
            Cost = 500,
            RefreshTime = 3000,
            LaunchRate = 600,
            ReanimName = "qiaokelidapao",
            CardAsset = "qklc_card",
            CardBackground = "green",
            StandAnim = "anim_unarmed_idle",
            Factory = () => new ChocolateCannonPlant(),
        };

        private const int ArmCountdown = 50;
        private const int RearmCountdown = 150;
        private const int AutoAimX = 650;
        private const int ChargeAnimRate = 24;
        private const int LaunchDx = -30;
        private const int LaunchDy = -160;

        public override void OnPlanted()
        {
            if (!Plant.IsInPlay())
            {
                return;
            }
            Plant.mState = PlantState.CobcannonArming;
            Plant.mStateCountdown = ArmCountdown;
        }

        public override void OnUpdate()
        {
            if (B == null || Plant.mDead || !Plant.IsInPlay())
            {
                return;
            }
            // 原生只对 Cobcannon 种子驱动这套状态机，自定义种子要自己调。
            Plant.UpdateCobCannon();
            if (Plant.mState == PlantState.CobcannonLoading)
            {
                Reanimation body = Body;
                if (body != null)
                {
                    body.mAnimRate = ChargeAnimRate;
                }
                return;
            }
            if (Plant.mState == PlantState.CobcannonReady)
            {
                Plant.CobCannonFire(AutoAimX, Plant.mRow);
                return;
            }
            if (Plant.mState == PlantState.CobcannonFiring && Plant.mShootingCounter == 0)
            {
                Plant.mState = PlantState.CobcannonArming;
                Plant.mStateCountdown = RearmCountdown;
                Plant.PlayBodyReanim("anim_unarmed_idle", ReanimLoopType.Loop, 20, 12f);
            }
        }

        public override void OnNativeFire(
            Action<Plant, Zombie, int, PlantWeapon> orig,
            Zombie target, int row, PlantWeapon weapon)
        {
            int x = Plant.mX + LaunchDx;
            int y = Plant.mY + LaunchDy;
            if (B.GetFlowerPotAt(Plant.mPlantCol, Plant.mRow) != null)
            {
                y -= 5;
            }
            // 溅射旗子借玉米炮的（水陆空全吃）：自定义种子走 default 只拿得到 1，打不到潜水和空中僵尸。
            uint flags = CobDamageRangeFlags(weapon);
            Projectile shell = CustomBullets.Fire(B, Plant, ChocolateShell.BulletId,
                x, y, row, ProjectileMotion.Lobbed, flags);
            shell.mCobTargetX = Plant.mTargetX - 40;
            shell.mVelX = 0.001f;
            shell.mVelY = 0f;
            shell.mVelZ = -ChocolateShell.RiseVelZ;
            shell.mAccZ = 0f;
        }

        private uint CobDamageRangeFlags(PlantWeapon weapon)
        {
            SeedType saved = Plant.mSeedType;
            Plant.mSeedType = SeedType.Cobcannon;
            try
            {
                return (uint)Plant.GetDamageRangeFlags(weapon);
            }
            finally
            {
                Plant.mSeedType = saved;
            }
        }
    }
}
