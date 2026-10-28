using System;
using System.Collections.Generic;
using Lawn;
using Sexy;
using Sexy.TodLib;
using PGvZPlantKit;

namespace PGvZNewPlantMod.Plants
{
    /// <summary>球果训练家：三种形态分别召唤恐龙、水母、马；点击本体切换形态。</summary>
    public sealed class QgxljPlant : CustomPlantBase
    {
        public static readonly CustomPlantDef Def = new CustomPlantDef
        {
            SeedId = 0,
            ReanimSlot = 4,
            SpineName = "qgxlj",
            Cost = 300,
            RefreshTime = 750,
            LaunchRate = 90,
            Name = "球果训练家",
            CardOffsetY = -10f,
            CardAsset = "qgxlj_card",
            AlmanacRow = 15,
            AlmanacColumn = 2,
            ChooserColumn = 1,
            ChooserRow = 14,
            StandAnim = "anim_stand1",
            PreviewAnim = "anim_stand1",
            PreviewScale = 0.72f,
            PreviewDx = 35f,
            PreviewDy = 88f,
            BodyScale = 1.7f,
            BodyDx = QgxljBodyXOffset,
            BodyDy = QgxljBodyYOffset,
            HasCursorOverride = true,
            CursorScale = 1.7f,
            CursorDx = -70f,
            CursorDy = -133f,
            Factory = () => new QgxljPlant(),
        };

        public const int AttackFrames = 80;
        public const int SummonFrame = 60;
        public const int DinoWait = 180;
        public const int DinoSwallowRest = 1200;
        public const float DinoStepDistance = 40f;
        public const int DinoStepTime = 15;
        public const int GiantDamage = 400;
        public const int UltimateDamage = 800;
        public const float UltimateSpeed = 4f;
        public const float BodyScale = 1.7f;
        public const float QgxljBodyXOffset = -35f;
        public const float QgxljBodyYOffset = -45f;
        public const float NormalDinoScale = 0.16f;
        public const float UltimateDinoScale = 0.18f;
        public const float NormalDinoYOffset = 80f;
        public const float UltimateDinoYOffset = 64f;
        public const int JellyfishLifetime = 1200;
        public const int JellyfishFireInterval = 120;
        public const int SpikeDamage = 80;
        public const int SpikeCharmChance = 20;
        public const int LaserLifetime = 720;
        public const int LaserTickInterval = 12;
        public const int LaserDamage = 200;
        public const int HorseDamage = 300;
        public const int HorseExplosionDamage = 700;
        public const float HorseSpeed = 1.25f;
        public const float HorseYOffset = 30f;
        public const int HorseEndWait = 120;
        public const int FormationLifetime = 900;
        public const int FormationStun = 240;

        private sealed class Dino
        {
            public QgxljPlant Owner;
            public Plant Plant;
            public Board Board;
            public int Row;
            public float X;
            public float GroundY;
            public bool Ultimate;
            public int WaitUntil;
            public float StepStart;
            public float StepEnd;
            public int StepAge = DinoStepTime;
            public readonly HashSet<Zombie> Hit = new HashSet<Zombie>();
            public int NextGiantTick;
        }

        private sealed class Jellyfish
        {
            public QgxljPlant Owner;
            public Plant Plant;
            public Board Board;
            public float X;
            public float Y;
            public int Born;
            public int Expires;
            public int NextFire;
        }

        private sealed class Laser
        {
            public Board Board;
            public Zombie Target;
            public int Expires;
            public int NextTick;
            public float X;
            public float BottomY;
        }

        private sealed class Horse
        {
            public QgxljPlant Owner;
            public Plant Plant;
            public Board Board;
            public int Row;
            public float X;
            public float GroundY;
            public readonly HashSet<Zombie> Hit = new HashSet<Zombie>();
            public int EndWait = -1;
        }

        private sealed class Formation
        {
            public Board Board;
            public float TargetX;
            public float CenterX;
            public float CenterY;
            public int Born;
            public int Expires;
            public readonly HashSet<Zombie> KnownZombies = new HashSet<Zombie>();
        }

        private sealed class SpikeHandlerImpl : ICustomProjectileImpact
        {
            public bool OnImpact(Projectile projectile, Zombie zombie,
                Action<Projectile, Zombie> orig)
            {
                if (zombie == null)
                {
                    orig(projectile, zombie);
                    return true;
                }
                orig(projectile, zombie);
                if (CustomCombat.IsValidTarget(zombie))
                {
                    zombie.TakeDamage(SpikeDamage - 20,
                        projectile.GetDamageFlags(zombie));
                    if (CustomCombat.IsValidTarget(zombie) &&
                        zombie.mZombieType != ZombieType.Boss &&
                        TodCommon.RandRangeInt(1, 100) <= SpikeCharmChance)
                    {
                        zombie.ApplyMindControl();
                    }
                }
                return true;
            }
        }

        private static readonly SpikeHandlerImpl SpikeHandler = new SpikeHandlerImpl();

        private static readonly List<Dino> Dinos = new List<Dino>();
        private static readonly List<Jellyfish> Jellyfishes = new List<Jellyfish>();
        private static readonly List<Laser> Lasers = new List<Laser>();
        private static readonly List<Horse> Horses = new List<Horse>();
        private static readonly List<Formation> Formations = new List<Formation>();

        private int _form = 1;
        private string _mode = "stand";
        private int _attackForm = 1;
        private bool _summoned;
        private Dino _dino;
        private Jellyfish _jellyfish;
        private Horse _horse;
        private int _ultimateUntil;

        public override bool CanUseUltimate => true;

        public override bool UltimateActive => Frame < _ultimateUntil;

        public override void OnPlanted()
        {
            _form = 1;
            _mode = "stand";
        }

        public override bool OnMouseDown(int x, int y, int clickCount)
        {
            _form = _form % 3 + 1;
            if (_mode != "attack")
            {
                Play("anim_stand" + _form, ReanimLoopType.Loop, 30f);
            }
            return true;
        }

        public override void OnUpdate()
        {
            if (B == null)
            {
                return;
            }
            float animTime = AnimTime;
            if (_mode == "attack")
            {
                int animationFrame = (int)(animTime * AttackFrames + 0.5f);
                if (!_summoned && animationFrame >= SummonFrame)
                {
                    if (_attackForm == 3)
                    {
                        _horse = AddHorse(Plant);
                    }
                    else if (_attackForm == 2)
                    {
                        _jellyfish = AddJellyfish(Plant);
                    }
                    else
                    {
                        _dino = AddDino(Plant, false, Plant.mRow);
                    }
                    _summoned = true;
                }
                if (animTime >= 0.999f)
                {
                    _mode = _attackForm == 3 ? "waiting_horse"
                        : _attackForm == 2 ? "waiting_jellyfish" : "waiting_dino";
                    Play("anim_stand" + _form, ReanimLoopType.Loop, 30f);
                }
                return;
            }
            if (_mode == "waiting_dino" || _mode == "waiting_jellyfish" ||
                _mode == "waiting_horse")
            {
                return;
            }
            if (HasTarget(Plant, _form))
            {
                _mode = "attack";
                _attackForm = _form;
                _summoned = false;
                Play("anim_attack" + _attackForm, ReanimLoopType.PlayOnceAndHold, 30f);
            }
        }

        public override void StartUltimate()
        {
            _ultimateUntil = Frame + 180;
            if (_form == 3)
            {
                AddFormation(Plant);
                return;
            }
            if (_form == 2)
            {
                AddLaser(Plant);
                return;
            }
            int rows = Plant.mBoard.StageHas6Rows() ? 6 : 5;
            for (int row = 0; row < rows; row++)
            {
                AddDino(Plant, true, row);
            }
        }

        public override void OnRemoved()
        {
            Dinos.RemoveAll(d => d.Owner == this && !d.Ultimate);
            Jellyfishes.RemoveAll(j => j.Owner == this);
            Horses.RemoveAll(h => h.Owner == this);
            _dino = null;
            _jellyfish = null;
            _horse = null;
        }

        private static bool HasTarget(Plant plant, int form)
        {
            if (plant.mBoard == null)
            {
                return false;
            }
            if (form == 2)
            {
                return CattailTarget(plant.mBoard, plant.mX + 40, plant.mY + 40) != null;
            }
            return plant.FindTargetZombie(plant.mRow, PlantWeapon.Primary) != null;
        }

        private static Dino AddDino(Plant plant, bool ultimate, int row)
        {
            Board board = plant.mBoard;
            int startCol = ultimate ? 0 : plant.mPlantCol;
            float startOffset = ultimate ? 40f : 60f;
            float startX = board.GridToPixelX(startCol, row) + startOffset;
            float groundY = board.GetPosYBasedOnRow(startX, row) + 20f;
            Dino dino = new Dino
            {
                Owner = CustomPlantRegistry.Get(plant) as QgxljPlant,
                Plant = plant,
                Board = board,
                Row = row,
                X = startX,
                GroundY = groundY,
                Ultimate = ultimate,
                WaitUntil = ultimate ? 0 : board.mMainCounter + DinoWait,
                StepStart = startX,
                StepEnd = startX,
                StepAge = DinoStepTime,
                NextGiantTick = board.mMainCounter,
            };
            Dinos.Add(dino);
            return dino;
        }

        private static Zombie CattailTarget(Board board, float sourceX, float sourceY)
        {
            Zombie target = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mMindControlled ||
                    !zombie.mHasHead)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float centerX = rect.mX + rect.mWidth * 0.5f;
                float centerY = rect.mY + rect.mHeight * 0.5f;
                float score = (centerX - sourceX) * (centerX - sourceX) +
                              (centerY - sourceY) * (centerY - sourceY);
                if (zombie.IsFlying())
                {
                    score -= 1000000f;
                }
                if (target == null || score < bestScore)
                {
                    target = zombie;
                    bestScore = score;
                }
            }
            return target;
        }

        private static void FireSpike(Jellyfish jellyfish)
        {
            Board board = jellyfish.Board;
            Plant plant = jellyfish.Plant;
            Zombie target = CattailTarget(board, jellyfish.X, jellyfish.Y);
            if (target == null)
            {
                return;
            }
            Projectile projectile = board.AddProjectile(
                (int)jellyfish.X, (int)jellyfish.Y, plant.mRenderOrder + 2,
                target.mRow, ProjectileType.CattailSpike);
            projectile.mDamageRangeFlags = plant.GetDamageRangeFlags(PlantWeapon.Primary);
            projectile.mFromPlant = (SeedType)Def.SeedId;
            projectile.mMotionType = ProjectileMotion.Homing;
            projectile.mVelX = 2f;
            projectile.mTargetZombieID = board.ZombieGetID(target);
            CustomProjectiles.SetImpactHandler(projectile, SpikeHandler);
        }

        private static Jellyfish AddJellyfish(Plant plant)
        {
            Board board = plant.mBoard;
            float x = board.GridToPixelX(plant.mPlantCol, plant.mRow) + 95f;
            float y = board.GetPosYBasedOnRow(x, plant.mRow) - 15f;
            Jellyfish jellyfish = new Jellyfish
            {
                Owner = CustomPlantRegistry.Get(plant) as QgxljPlant,
                Plant = plant,
                Board = board,
                X = x,
                Y = y,
                Born = board.mMainCounter,
                Expires = board.mMainCounter + JellyfishLifetime,
                NextFire = board.mMainCounter,
            };
            Jellyfishes.Add(jellyfish);
            return jellyfish;
        }

        private static Zombie LeftmostZombie(Board board)
        {
            Zombie target = null;
            float leftX = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mMindControlled)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                if (rect.mX < leftX)
                {
                    target = zombie;
                    leftX = rect.mX;
                }
            }
            return target;
        }

        private static Laser AddLaser(Plant plant)
        {
            Board board = plant.mBoard;
            Laser laser = new Laser
            {
                Board = board,
                Target = LeftmostZombie(board),
                Expires = board.mMainCounter + LaserLifetime,
                NextTick = board.mMainCounter,
                X = plant.mX + 80,
                BottomY = plant.mY + 80,
            };
            Lasers.Add(laser);
            return laser;
        }

        private static Horse AddHorse(Plant plant)
        {
            Board board = plant.mBoard;
            float startX = board.GridToPixelX(plant.mPlantCol, plant.mRow) + 75f;
            float groundY = board.GetPosYBasedOnRow(startX, plant.mRow) + 55f;
            Horse horse = new Horse
            {
                Owner = CustomPlantRegistry.Get(plant) as QgxljPlant,
                Plant = plant,
                Board = board,
                Row = plant.mRow,
                X = startX,
                GroundY = groundY,
            };
            Horses.Add(horse);
            return horse;
        }

        private static void AddFormation(Plant plant)
        {
            Board board = plant.mBoard;
            int row = 2;
            float targetX = board.GridToPixelX(8, row);
            float centerX = targetX + 40f;
            float centerY = board.GetPosYBasedOnRow(centerX, row) + 35f;
            Formation formation = new Formation
            {
                Board = board,
                TargetX = targetX,
                CenterX = centerX,
                CenterY = centerY,
                Born = board.mMainCounter,
                Expires = board.mMainCounter + FormationLifetime,
            };
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                formation.KnownZombies.Add(board.mZombies[i]);
            }
            Formations.Add(formation);
        }

        public static void UpdateEffects(Board board)
        {
            if (board == null)
            {
                return;
            }
            UpdateDinos(board);
            UpdateJellyfishes(board);
            UpdateLasers(board);
            UpdateHorses(board);
            UpdateFormations(board);
        }

        private static void UpdateDinos(Board board)
        {
            float rightEdge = board.RightEdge(0) + 120f;
            bool shake = false;
            for (int index = Dinos.Count - 1; index >= 0; index--)
            {
                Dino dino = Dinos[index];
                if (dino.Board != board)
                {
                    Dinos.RemoveAt(index);
                    continue;
                }
                Plant plant = dino.Plant;
                if (!dino.Ultimate && (plant == null || plant.mDead || plant.mSquished))
                {
                    Dinos.RemoveAt(index);
                    continue;
                }
                if (dino.Ultimate)
                {
                    dino.X += UltimateSpeed;
                    dino.GroundY = board.GetPosYBasedOnRow(dino.X, dino.Row) + 20f;
                    for (int i = 0; i < board.mZombies.Count; i++)
                    {
                        Zombie zombie = board.mZombies[i];
                        if (dino.Hit.Contains(zombie) || zombie.mDead ||
                            zombie.IsDeadOrDying() || zombie.mRow != dino.Row)
                        {
                            continue;
                        }
                        TRect rect = zombie.GetZombieRect();
                        if (rect.mX - 30 <= dino.X && dino.X <= rect.mX + rect.mWidth + 30)
                        {
                            zombie.TakeDamage(UltimateDamage, 0u);
                            dino.Hit.Add(zombie);
                        }
                    }
                    shake = true;
                }
                else if (board.mMainCounter >= dino.WaitUntil)
                {
                    if (dino.StepAge >= DinoStepTime)
                    {
                        Zombie target = FindDinoTarget(dino);
                        if (target != null)
                        {
                            if (CustomCombat.IsGiantZombie(target))
                            {
                                if (board.mMainCounter >= dino.NextGiantTick)
                                {
                                    target.TakeDamage(GiantDamage, 0u);
                                    dino.NextGiantTick = board.mMainCounter + 60;
                                }
                                continue;
                            }
                            target.DieNoLoot(false);
                            dino.WaitUntil = board.mMainCounter + DinoSwallowRest;
                            continue;
                        }
                        dino.StepStart = dino.X;
                        dino.StepEnd = dino.X + DinoStepDistance;
                        dino.StepAge = 0;
                    }
                    dino.StepAge++;
                    float progress = Math.Min(1f, dino.StepAge / (float)DinoStepTime);
                    dino.X = dino.StepStart + (dino.StepEnd - dino.StepStart) * progress;
                    dino.GroundY = board.GetPosYBasedOnRow(dino.X, dino.Row) + 20f;
                }
                if (dino.X > rightEdge)
                {
                    Dinos.RemoveAt(index);
                    if (!dino.Ultimate)
                    {
                        QgxljPlant owner = CustomPlantRegistry.Get(dino.Plant) as QgxljPlant;
                        if (owner != null && owner._dino == dino)
                        {
                            owner._dino = null;
                            owner._mode = "stand";
                        }
                    }
                }
            }
            if (shake)
            {
                board.ShakeBoard(1, 4);
            }
        }

        private static Zombie FindDinoTarget(Dino dino)
        {
            Board board = dino.Board;
            float front = dino.X + 80f;
            Zombie best = null;
            float bestX = float.MaxValue;
            for (int i = 0; i < board.mZombies.Count; i++)
            {
                Zombie zombie = board.mZombies[i];
                if (zombie.mDead || zombie.IsDeadOrDying() ||
                    zombie.mZombieType == ZombieType.Boss || zombie.mRow != dino.Row)
                {
                    continue;
                }
                TRect rect = zombie.GetZombieRect();
                float center = rect.mX + rect.mWidth * 0.5f;
                if (dino.X <= center && center <= front && center < bestX)
                {
                    best = zombie;
                    bestX = center;
                }
            }
            return best;
        }

        private static void UpdateJellyfishes(Board board)
        {
            for (int index = Jellyfishes.Count - 1; index >= 0; index--)
            {
                Jellyfish jellyfish = Jellyfishes[index];
                if (jellyfish.Board != board)
                {
                    Jellyfishes.RemoveAt(index);
                    continue;
                }
                Plant plant = jellyfish.Plant;
                if (plant == null || plant.mDead || plant.mSquished)
                {
                    Jellyfishes.RemoveAt(index);
                    continue;
                }
                int frame = board.mMainCounter;
                if (frame >= jellyfish.Expires)
                {
                    Jellyfishes.RemoveAt(index);
                    QgxljPlant owner = CustomPlantRegistry.Get(plant) as QgxljPlant;
                    if (owner != null && owner._jellyfish == jellyfish)
                    {
                        owner._jellyfish = null;
                        owner._mode = "stand";
                    }
                    continue;
                }
                if (frame >= jellyfish.NextFire)
                {
                    FireSpike(jellyfish);
                    FireSpike(jellyfish);
                    jellyfish.NextFire = frame + JellyfishFireInterval;
                }
            }
        }

        private static void UpdateLasers(Board board)
        {
            for (int index = Lasers.Count - 1; index >= 0; index--)
            {
                Laser laser = Lasers[index];
                if (laser.Board != board)
                {
                    Lasers.RemoveAt(index);
                    continue;
                }
                int frame = board.mMainCounter;
                if (frame >= laser.Expires)
                {
                    Lasers.RemoveAt(index);
                    continue;
                }
                Zombie target = laser.Target;
                if (target == null || target.mDead || target.IsDeadOrDying() ||
                    target.mMindControlled)
                {
                    target = LeftmostZombie(board);
                    laser.Target = target;
                }
                if (target == null)
                {
                    continue;
                }
                TRect rect = target.GetZombieRect();
                laser.X = rect.mX + rect.mWidth * 0.5f;
                laser.BottomY = rect.mY + rect.mHeight * 0.72f;
                if (frame < laser.NextTick)
                {
                    continue;
                }
                laser.NextTick = frame + LaserTickInterval;
                const float beamHalfWidth = 42f;
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    if (zombie.mDead || zombie.IsDeadOrDying() || zombie.mMindControlled)
                    {
                        continue;
                    }
                    TRect zombieRect = zombie.GetZombieRect();
                    if (zombieRect.mX <= laser.X + beamHalfWidth &&
                        zombieRect.mX + zombieRect.mWidth >= laser.X - beamHalfWidth)
                    {
                        zombie.TakeDamage(LaserDamage, 0u);
                    }
                }
            }
        }

        private static void UpdateHorses(Board board)
        {
            float rightX = board.GridToPixelX(8, 0) + 40f;
            for (int index = Horses.Count - 1; index >= 0; index--)
            {
                Horse horse = Horses[index];
                if (horse.Board != board)
                {
                    Horses.RemoveAt(index);
                    continue;
                }
                Plant plant = horse.Plant;
                if (plant == null || plant.mDead || plant.mSquished)
                {
                    Horses.RemoveAt(index);
                    continue;
                }
                if (horse.EndWait < 0)
                {
                    horse.X = Math.Min(rightX, horse.X + HorseSpeed);
                    horse.GroundY = board.GetPosYBasedOnRow(horse.X, horse.Row) + 55f;
                    for (int i = 0; i < board.mZombies.Count; i++)
                    {
                        Zombie zombie = board.mZombies[i];
                        if (horse.Hit.Contains(zombie) || zombie.mDead ||
                            zombie.IsDeadOrDying() || zombie.mRow != horse.Row)
                        {
                            continue;
                        }
                        TRect rect = zombie.GetZombieRect();
                        if (rect.mX - 35 <= horse.X && horse.X <= rect.mX + rect.mWidth + 35)
                        {
                            zombie.TakeDamage(HorseDamage, 0u);
                            horse.Hit.Add(zombie);
                        }
                    }
                    if (horse.X >= rightX)
                    {
                        horse.EndWait = board.mMainCounter + HorseEndWait;
                    }
                }
                else if (board.mMainCounter >= horse.EndWait)
                {
                    for (int i = 0; i < board.mZombies.Count; i++)
                    {
                        Zombie zombie = board.mZombies[i];
                        if (zombie.mDead || zombie.IsDeadOrDying() ||
                            Math.Abs(zombie.mRow - horse.Row) > 1)
                        {
                            continue;
                        }
                        TRect rect = zombie.GetZombieRect();
                        float centerX = rect.mX + rect.mWidth * 0.5f;
                        if (Math.Abs(centerX - horse.X) <= 120f)
                        {
                            zombie.TakeDamage(HorseExplosionDamage, 0u);
                        }
                    }
                    Horses.RemoveAt(index);
                    QgxljPlant owner = CustomPlantRegistry.Get(plant) as QgxljPlant;
                    if (owner != null && owner._horse == horse)
                    {
                        owner._horse = null;
                        owner._mode = "stand";
                    }
                }
            }
        }

        private static void UpdateFormations(Board board)
        {
            for (int index = Formations.Count - 1; index >= 0; index--)
            {
                Formation formation = Formations[index];
                if (formation.Board != board)
                {
                    Formations.RemoveAt(index);
                    continue;
                }
                if (board.mMainCounter >= formation.Expires)
                {
                    Formations.RemoveAt(index);
                    continue;
                }
                for (int i = 0; i < board.mZombies.Count; i++)
                {
                    Zombie zombie = board.mZombies[i];
                    int originalRow = Math.Max(0, Math.Min(4, zombie.mRow));
                    float lastCellRight = board.GridToPixelX(8, originalRow) + 80f;
                    bool outsideLastCell = zombie.mPosX > lastCellRight;
                    if (formation.KnownZombies.Contains(zombie) && !outsideLastCell)
                    {
                        continue;
                    }
                    formation.KnownZombies.Add(zombie);
                    if (zombie.mDead || zombie.IsDeadOrDying() ||
                        zombie.mZombieType == ZombieType.Boss ||
                        CustomCombat.IsGiantZombie(zombie))
                    {
                        continue;
                    }
                    if (zombie.mRow != 2)
                    {
                        board.ZombieSwitchRow(zombie, 2);
                        zombie.SetRow(2);
                    }
                    zombie.mPosX = formation.TargetX;
                    zombie.mPosY = board.GetPosYBasedOnRow(zombie.mPosX + 40f, 2) - 30f;
                    zombie.mX = (int)zombie.mPosX;
                    zombie.mY = (int)zombie.mPosY;
                    zombie.mOnHighGround = zombie.IsOnHighGround();
                    zombie.mRenderOrder = Board.MakeRenderOrder(RenderLayer.Zombie, 2, 4);
                    zombie.ApplyButter();
                    zombie.mButteredCounter = FormationStun;
                }
            }
        }

        private static readonly Dictionary<Board, int> FormationDrawFrame =
            new Dictionary<Board, int>();

        public static bool HasFormations(Board board)
        {
            if (board == null)
            {
                return false;
            }
            foreach (Formation formation in Formations)
            {
                if (formation.Board == board)
                {
                    return true;
                }
            }
            return false;
        }

        public static bool FormationNeedsDraw(Board board)
        {
            if (!HasFormations(board))
            {
                return false;
            }
            return !FormationDrawFrame.TryGetValue(board, out int frame) ||
                   frame != board.mMainCounter;
        }

        public static void MarkFormationDrawn(Board board)
        {
            if (board != null)
            {
                FormationDrawFrame[board] = board.mMainCounter;
            }
        }

        public static void DrawFormationLayer(Board board, Graphics g)
        {
            DrawFormations(board, g);
        }

        public static void DrawEffects(Board board, Graphics g)
        {
            if (board == null)
            {
                return;
            }
            DrawDinos(board, g);
            DrawJellyfishes(board, g);
            DrawLasers(board, g);
            DrawHorses(board, g);
        }

        private static void DrawDinos(Board board, Graphics g)
        {
            foreach (Dino dino in Dinos)
            {
                if (dino.Board != board)
                {
                    continue;
                }
                Image image = AssetStore.Get(dino.Ultimate ? "qgxlj_ultimate_dino" : "qgxlj_dino");
                float scale = dino.Ultimate ? UltimateDinoScale : NormalDinoScale;
                float yOffset = dino.Ultimate ? UltimateDinoYOffset : NormalDinoYOffset;
                float period = dino.Ultimate ? 12f : 18f;
                float bounce = Math.Abs((float)Math.Sin(
                    board.mMainCounter * Math.PI / period)) * 16f;
                float drawX = dino.X - image.mWidth * scale * 0.5f;
                float drawY = dino.GroundY + yOffset - bounce - image.mHeight * scale;
                TodCommon.TodDrawImageScaledF(g, image, drawX, drawY, scale, scale);
            }
        }

        private static void DrawJellyfishes(Board board, Graphics g)
        {
            Image image = AssetStore.Get("qgxlj_jellyfish");
            const float scale = 0.18f;
            foreach (Jellyfish jellyfish in Jellyfishes)
            {
                if (jellyfish.Board != board)
                {
                    continue;
                }
                int age = board.mMainCounter - jellyfish.Born;
                float bob = (float)Math.Sin(age * Math.PI / 45.0) * 10f;
                float drawX = jellyfish.X - image.mWidth * scale * 0.5f;
                float drawY = jellyfish.Y + bob - image.mHeight * scale * 0.5f;
                TodCommon.TodDrawImageScaledF(g, image, drawX, drawY, scale, scale);
            }
        }

        private static void DrawLasers(Board board, Graphics g)
        {
            Image image = AssetStore.Get("qgxlj_laser");
            foreach (Laser laser in Lasers)
            {
                if (laser.Board != board || laser.Target == null)
                {
                    continue;
                }
                const float top = -140f;
                float height = Math.Max(1f, laser.BottomY - top);
                const float scaleX = 0.42f;
                float scaleY = height / image.mHeight;
                float drawX = laser.X - image.mWidth * scaleX * 0.5f;
                TodCommon.TodDrawImageScaledF(g, image, drawX, top, scaleX, scaleY);
            }
        }

        private static void DrawHorses(Board board, Graphics g)
        {
            Image image = AssetStore.Get("qgxlj_horse");
            const float scale = 0.18f;
            foreach (Horse horse in Horses)
            {
                if (horse.Board != board)
                {
                    continue;
                }
                float bob = (float)Math.Sin(board.mMainCounter * Math.PI / 90.0) * 8f;
                float drawX = horse.X - image.mWidth * scale * 0.5f;
                float drawY = horse.GroundY + HorseYOffset - bob - image.mHeight * scale;
                TodCommon.TodDrawImageScaledF(g, image, drawX, drawY, scale, scale);
            }
        }

        private static void DrawFormations(Board board, Graphics g)
        {
            Image image = AssetStore.Get("qgxlj_formation");
            const float diameter = 160f;
            float scaleX = diameter / image.mWidth;
            float scaleY = diameter / image.mHeight;
            foreach (Formation formation in Formations)
            {
                if (formation.Board != board)
                {
                    continue;
                }
                float drawX = formation.CenterX - diameter * 0.5f;
                float drawY = formation.CenterY - diameter * 0.5f;
                bool oldColorize = g.mColorizeImages;
                Microsoft.Xna.Framework.Color oldColor = g.mColor;
                g.SetColorizeImages(true);
                g.SetColor(new SexyColor(255, 255, 255, 150).Color);
                try
                {
                    TodCommon.TodDrawImageScaledF(g, image, drawX, drawY, scaleX, scaleY);
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
