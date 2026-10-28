using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>伏僵塔黄：吞掉贴近前方的僵尸后休眠；大招拉拢并炼化 8 只随机僵尸、灼烧本行。</summary>
    public sealed class FjthPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 1,
            SpineName = "fjth",
            Cost = 225,
            RefreshTime = 750,
            LaunchRate = 60,
            Name = "伏僵塔黄",
            CardAsset = "fjth_card",
            AlmanacRow = 14,
            AlmanacColumn = 3,
            ChooserColumn = 2,
            ChooserRow = 13,
            StandAnim = "anim_stand",
            PreviewAnim = "anim_stand",
            PreviewScale = 0.40f,
            PreviewDx = 35f,
            PreviewDy = 82f,
            BodyScale = 1.056f,
            BodyDx = 2f,
            BodyDy = -3f,
            BodyUsesS = true,
            Health = 32000,
            Factory = () => new FjthPlant(),
        };

        public const int SleepTime = 1800;
        public const int AttackTotalFrames = 80;
        public const int AttackSwallowFrame = 26;
        public const int UltimateTargetCount = 8;
        public const int UltimateTotalFrames = 80;
        public const int UltimatePullFrame = 20;

        private sealed class PullTarget
        {
            public Zombie Zombie;
            public float StartX;
            public float StartY;
        }

        private static readonly HashSet<Zombie> PulledZombies = new HashSet<Zombie>();

        private string _mode = "stand";
        private int _sleepUntil;
        private int _attackUntil;
        private Zombie _attackTarget;
        private bool _attackSwallowed;
        private int _ultimateStart;
        private readonly List<PullTarget> _ultimateTargets = new List<PullTarget>();
        private bool _ultimateBurned;

        public static bool IsPulled(Zombie zombie)
        {
            return zombie != null && PulledZombies.Contains(zombie);
        }

        public override bool CanUseUltimate => _mode != "ultimate";

        public override bool UltimateActive => _mode == "ultimate";

        public override void OnPlanted()
        {
            _mode = "stand";
        }

        public override void OnRemoved()
        {
            foreach (PullTarget target in _ultimateTargets)
            {
                if (target.Zombie != null)
                {
                    PulledZombies.Remove(target.Zombie);
                }
            }
            _ultimateTargets.Clear();
            _attackTarget = null;
        }

        public override void OnUpdate()
        {
            if (B == null)
            {
                return;
            }
            int frame = Frame;
            if (_mode == "ultimate")
            {
                UpdateUltimate(frame);
                return;
            }
            if (_mode == "attack")
            {
                float animTime = AnimTime;
                float swallowTime = AttackSwallowFrame / (float)AttackTotalFrames;
                if (!_attackSwallowed && animTime >= swallowTime)
                {
                    Zombie swallowedTarget = _attackTarget;
                    if (swallowedTarget != null && !swallowedTarget.mDead)
                    {
                        swallowedTarget.DieNoLoot(false);
                    }
                    _attackSwallowed = true;
                    _sleepUntil = frame + SleepTime;
                }
                if (animTime < 0.999f)
                {
                    return;
                }
                _attackTarget = null;
                _attackSwallowed = false;
                Play("anim_sleep", ReanimLoopType.Loop, 30f);
                _mode = "sleep";
                return;
            }
            if (_sleepUntil > frame)
            {
                if (_attackUntil != 0 && frame >= _attackUntil)
                {
                    Play("anim_sleep", ReanimLoopType.Loop, 30f);
                    _attackUntil = 0;
                    _mode = "sleep";
                }
                return;
            }
            if (_mode != "stand")
            {
                Play("anim_stand", ReanimLoopType.Loop, 30f);
                _mode = "stand";
            }
            Zombie target = FindTarget(Plant);
            if (target == null)
            {
                return;
            }
            Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 60f);
            _attackTarget = target;
            _attackSwallowed = false;
            _mode = "attack";
        }

        public override void StartUltimate()
        {
            _sleepUntil = 0;
            _attackUntil = 0;
            _mode = "ultimate";
            _ultimateStart = Frame;
            _ultimateTargets.Clear();
            List<Zombie> candidates = new List<Zombie>();
            Board board = B;
            if (board != null)
            {
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (zombie.mDead || zombie.IsDeadOrDying() ||
                        PulledZombies.Contains(zombie) ||
                        zombie.mZombieType == ZombieType.Boss)
                    {
                        continue;
                    }
                    candidates.Add(zombie);
                }
            }
            while (candidates.Count > 0 && _ultimateTargets.Count < UltimateTargetCount)
            {
                int index = TodCommon.RandRangeInt(0, candidates.Count - 1);
                Zombie zombie = candidates[index];
                candidates.RemoveAt(index);
                _ultimateTargets.Add(new PullTarget
                {
                    Zombie = zombie,
                    StartX = zombie.mPosX,
                    StartY = zombie.mPosY,
                });
            }
            foreach (PullTarget target in _ultimateTargets)
            {
                PulledZombies.Add(target.Zombie);
                if (target.Zombie.mIsEating)
                {
                    target.Zombie.StopEating();
                }
            }
            _ultimateBurned = false;
            Play("anim_sa", ReanimLoopType.PlayOnceAndHold, 30f);
        }

        private void UpdateUltimate(int frame)
        {
            float animTime = Body == null ? 1f : Body.mAnimTime;
            float swallowTime = UltimatePullFrame / (float)UltimateTotalFrames;
            int gridX = B.GridToPixelX(Plant.mPlantCol, Plant.mRow);
            float targetX = gridX - 20f;
            float targetY = B.GetPosYBasedOnRow(targetX + 40f, Plant.mRow) - 30f;
            if (animTime < swallowTime)
            {
                float progress = Math.Max(0f, Math.Min(1f, animTime / swallowTime));
                foreach (PullTarget item in _ultimateTargets)
                {
                    Zombie zombie = item.Zombie;
                    if (zombie.mDead)
                    {
                        PulledZombies.Remove(zombie);
                        continue;
                    }
                    zombie.mPosX = item.StartX + (targetX - item.StartX) * progress;
                    zombie.mPosY = item.StartY + (targetY - item.StartY) * progress;
                    zombie.mX = (int)zombie.mPosX;
                    zombie.mY = (int)zombie.mPosY;
                }
                return;
            }
            if (!_ultimateBurned)
            {
                var swallowed = new HashSet<Zombie>();
                foreach (PullTarget item in _ultimateTargets)
                {
                    Zombie zombie = item.Zombie;
                    swallowed.Add(zombie);
                    PulledZombies.Remove(zombie);
                    if (!zombie.mDead)
                    {
                        zombie.DieNoLoot(false);
                    }
                }
                for (int i = 0; i < B.mZombies.Count; i++)
                {
                    Zombie zombie = B.mZombies[i];
                    if (zombie.mDead || zombie.mRow != Plant.mRow || swallowed.Contains(zombie))
                    {
                        continue;
                    }
                    zombie.ApplyBurn();
                }
                B.DoFwoosh(Plant.mRow);
                Plant.mApp.PlayFoley(FoleyType.JalapenoIgnite);
                Plant.mApp.PlayFoley(FoleyType.Juicy);
                _ultimateBurned = true;
            }
            if (Body != null && Body.mAnimTime < 0.999f)
            {
                return;
            }
            _ultimateTargets.Clear();
            _mode = "stand";
            Play("anim_stand", ReanimLoopType.Loop, 30f);
        }

        private static Zombie FindTarget(Plant plant)
        {
            Board board = plant.mBoard;
            if (board == null)
            {
                return null;
            }
            float rangeLeft = plant.mX + 35;
            float rangeRight = rangeLeft + 100;
            Zombie best = null;
            float bestX = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || PulledZombies.Contains(zombie) ||
                    zombie.mRow != plant.mRow || zombie.mZombieType == ZombieType.Boss)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float centerX = rect.mX + rect.mWidth * 0.5f;
                if (rangeLeft <= centerX && centerX <= rangeRight && centerX < bestX)
                {
                    best = zombie;
                    bestX = centerX;
                }
            }
            return best;
        }

        /// <summary>机器人泰坦踩踏时改为被伏僵塔黄吞掉。</summary>
        public static bool TrySwallowRobotTitan(Zombie zombie, int gridX, int gridY)
        {
            if (zombie == null || zombie.mBoard == null)
            {
                return false;
            }
            if (zombie.mZombieType != ZombieType.RobotTitan &&
                zombie.mZombieType != ZombieType.RedeyeRobotTitan)
            {
                return false;
            }
            Board board = zombie.mBoard;
            for (int i = 0; i < board.mPlants.Count; i++)
            {
                Plant plant = board.mPlants[i];
                if (plant.mDead || (int)plant.mSeedType != Def.SeedId ||
                    plant.mPlantCol != gridX || plant.mRow != gridY)
                {
                    continue;
                }
                FjthPlant behavior = CustomPlantRegistry.Get(plant) as FjthPlant;
                if (behavior != null && behavior._mode == "stand" &&
                    behavior._sleepUntil <= board.mMainCounter)
                {
                    behavior.Play("anim_attack", ReanimLoopType.PlayOnceAndHold, 60f);
                    behavior._attackTarget = zombie;
                    behavior._attackSwallowed = false;
                    behavior._mode = "attack";
                    return true;
                }
                return false;
            }
            return false;
        }
    }
}
