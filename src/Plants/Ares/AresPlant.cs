using System;
using Lawn;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>
    /// 战神阿瑞斯：一轮齐射向前后两侧连发。前方 6 发直射，后方 8 发用原生的 Backwards 弹道。
    /// 索敌和起手交给原生射手逻辑，我们只负责把一次 Fire 展开成一串，
    /// 并把攻击段动画的速率铺满"起势 + 整串"的时间，避免人和弹脱节。
    /// </summary>
    public sealed class AresPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            Name = "战神",
            Cost = 200,
            RefreshTime = 750,
            LaunchRate = 100,
            SubClass = PlantSubClass.Shooter,
            ReanimName = "ares",
            CardAsset = "ares_card",
            CardBackground = "green",
            CardOffsetY = -15,
            StandAnim = "anim_idle",
            Factory = () => new AresPlant(),
        };

        /// <summary>别的包（火神、水神）按这个 ID 引用战神的弹。</summary>
        public const string BulletId = "pgvz.ares:ares_shot";

        public static readonly CustomBulletDef Bullet = new CustomBulletDef
        {
            Id = BulletId,
            Carrier = ProjectileType.Pea,
            Image = "ares_shot",
        };

        private const int IdleRate = 10;
        private const int ShootFrames = 10;
        private const int ShotGap = 6;
        private const int FrontShots = 6;
        private const int BackShots = 8;
        private const int Salvos = BackShots > FrontShots ? BackShots : FrontShots;
        private const int BurstTicks = Salvos * ShotGap;
        private const int SwingTail = (Salvos - 1) * ShotGap;

        // 出膛点：相对植物原点（格子左上角）的像素。
        private const int FrontMuzzleX = 80;
        private const int FrontMuzzleY = 30;
        private const int BackMuzzleX = -2;
        private const int BackMuzzleY = 35;

        private int _salvo = -1;
        private float _animRate = IdleRate;

        public override void OnUpdate()
        {
            Reanimation body = Body;
            if (B == null || body == null)
            {
                return;
            }
            if (_salvo < 0)
            {
                if (Plant.mShootingCounter <= 0)
                {
                    // 引擎每次回待机都按素材 fps 重设速率，这里逐帧钉回素材自己的 10 帧/秒。
                    body.mAnimRate = IdleRate;
                    return;
                }
                _salvo = 0;
                _animRate = ShootFrames /
                    (float)(Plant.mShootingCounter + SwingTail) * 100f;
            }
            body.mAnimRate = _animRate;
            if (_salvo <= 0)
            {
                return;
            }
            int counter = Plant.mShootingCounter;
            if (counter <= 0 || counter >= BurstTicks || counter % ShotGap != 0)
            {
                return;
            }
            FireSalvo(_salvo);
            if (_salvo >= Salvos)
            {
                _salvo = -1;
                // 留 1 而不是 0：计数器 1→0 那帧引擎才会恢复待机动画。
                Plant.mShootingCounter = 1;
                return;
            }
            _salvo++;
        }

        public override void OnNativeFire(
            Action<Plant, Zombie, int, PlantWeapon> orig,
            Zombie target, int row, PlantWeapon weapon)
        {
            FireSalvo(1);
            _salvo = 2;
            _animRate = ShootFrames / (float)SwingTail * 100f;
            Plant.mShootingCounter = BurstTicks;
        }

        private void FireSalvo(int round)
        {
            if (round <= FrontShots)
            {
                FireOne(FrontMuzzleX, FrontMuzzleY, ProjectileMotion.Straight);
            }
            if (round <= BackShots)
            {
                FireOne(BackMuzzleX, BackMuzzleY, ProjectileMotion.Backwards);
            }
        }

        private void FireOne(int offsetX, int offsetY, ProjectileMotion motion)
        {
            int x = Plant.mX + offsetX;
            int y = Plant.mY + offsetY;
            if (B.GetFlowerPotAt(Plant.mPlantCol, Plant.mRow) != null)
            {
                y -= 5;
            }
            CustomBullets.Fire(B, Plant, BulletId, x, y, Plant.mRow, motion);
        }
    }
}
