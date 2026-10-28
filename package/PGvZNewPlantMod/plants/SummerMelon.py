# -*- coding: utf-8 -*-
import System

from Lawn import SeedType, Plant, PlantSubClass, ProjectileType, ProjectileMotion
from LawnMod import MonoModUtils as M
from Sexy import Constants
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import (PlantKit, CustomPlantDef, CustomPlantBase, CustomBullets,
                         CustomBulletDef, CustomBurn, ModLog, ICustomBulletMotion)

DEF = CustomPlantDef()
DEF.Name = u"夏西瓜"
DEF.Cost = 500
DEF.RefreshTime = 5000
DEF.Health = 300
DEF.SubClass = PlantSubClass.Shooter
DEF.LaunchRate = 300
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Melonpult
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Melonpult
DEF.CardBackground = "green"
DEF.StandAnim = ""
DEF.VoiceSeed = SeedType.Melonpult
DEF.DrawAsSeed = SeedType.Melonpult
DEF.BodyTint = u"#ffaa33"
DEF.AlmanacText = (
    u"夏西瓜抛出滚烫的夏瓜，保留西瓜的溅射攻击，正中目标还会受到灼热。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}80，溅射与西瓜相同\n"
    u"{KEYWORD}灼热：{STAT}持续3秒，每秒造成自身总最大生命值的3%伤害，向下取整；"
    u"同一数值也伤害附近僵尸，范围与西瓜溅射相同\n"
    u"{KEYWORD}再次命中：{STAT}刷新灼热时长，不叠加\n"
    u"{FLAVOR}西瓜可以消暑，它偏不。"
)

PACK_ID = PlantKit.CurrentPackId or u"SummerMelon"
SCORCH = CustomBurn.BurnSpec()
SCORCH.Name = u"灼热"
SCORCH.DamagePercent = 3
SCORCH.RoundDown = True
# 命中当拍就烧第一刀（FirstTick=1），之后每秒一刀；只写"几刀"，结束时刻由框架推出来，
# 不用凑 305 那种数、也不会到期后空转。
SCORCH.FirstTick = 1
SCORCH.Ticks = 3
SCORCH.Interval = 100
SCORCH.SplashWidth = 60
SCORCH.SplashRowSpan = 1
# 不做僵尸显色：框架那层染色走绘制期 SetColorizeImages，实测对僵尸的 reanim 不生效
# （同一帧 HasBurn=True、已挨 3 刀，画面上仍是原色），留着就是个"声明了没效果"的开关。
SCORCH.Tint = False

SHELL_ID = PACK_ID + u":summer_melon"
SHELL = CustomBulletDef()
SHELL.Id = SHELL_ID
SHELL.Carrier = ProjectileType.Melon
SHELL.Damage = 0
SHELL.Burn = SCORCH
SHELL.Tint = u"#ffaa33"

# 跨行那一颗的弹道修正：引擎的瓜按"相对发射点的固定 -35"落地，站本行看没问题，
# 但三行齐投时三颗都收在本行高度上。这里不改发射点（三颗同一起点），
# 只在飞行过程中把绘制高度按进度从 0 渐变到目标行差 —— 起点是植物嘴里，
# 落点才落在目标行。原工程序照常跑（积分、碰撞、消失都由它算）。
_LOB_STATE = {}


class RowLobMotion(ICustomBulletMotion):
    def Update(self, shot, orig):
        orig(shot)
        state = _LOB_STATE.get(shot)
        if state is None:
            return
        if shot.mDead:
            _LOB_STATE.pop(shot, None)
            return
        x0, launchY, shadowY, delta, targetX = state
        if delta == 0 or targetX <= x0:
            return
        progress = (shot.mPosX - x0) / (targetX - x0)
        if progress < 0.0:
            progress = 0.0
        elif progress > 1.0:
            progress = 1.0
        # 始终从记录的原始高度重算，别在上一帧的结果上叠加（会越推越远）。
        shot.mPosY = launchY + delta * progress
        shot.mShadowY = shadowY + delta * progress
        shot.mX = int(shot.mPosX)
        shot.mY = int(shot.mPosY + shot.mPosZ)
        reanim = shot.mApp.ReanimationTryToGet(shot.mProjectileReanimID)
        if reanim is not None:
            reanim.SetPosition(shot.mPosX, shot.mPosY)

    def GetImage(self, shot):
        return None


SHELL.Motion = RowLobMotion()
CustomBullets.Register(SHELL)


def FrontZombieInRow(plant, row):
    """
    本行**在植物右前方**最靠前的一只活僵尸。
    必须排除身后的：投手只能往前扔，瞄一个已经在植物左边的僵尸，
    引擎算出来的水平位移会被夹到最小值，瓜就落回原地了。
    mDead 要到尸体阶段才置位，正在倒下的那只也得排掉。
    """
    board = plant.mBoard
    if board is None:
        return None
    front = None
    for zombie in board.mZombies:
        if (zombie is None or zombie.mDead or zombie.IsDeadOrDying() or
                zombie.mRow != row or zombie.mPosX < plant.mX):
            continue
        if front is None or zombie.mPosX < front.mPosX:
            front = zombie
    return front


def SummerMelon_FindTargetZombie(orig, self, theRow, thePlantWeapon):
    """
    引擎的 FindTargetAndFire 在本行找不到目标时直接 return false，连 Fire 都不会调 ——
    那样"本行没僵尸、邻行有"就永远不投。这里把索敌扩到上/本/下三行：
    本行照旧走原生，找不到就取相邻行里最靠前的一只。开火后由 OnNativeFire 按行各投一颗。
    """
    if not DEF.IsMine(self):
        return orig(self, theRow, thePlantWeapon)
    found = orig(self, theRow, thePlantWeapon)
    if found is not None:
        return found
    # 只在"问的就是本株这一行"时扩索敌；别的行另有用途（比如范围判定）时别乱答。
    if theRow != self.mRow:
        return None
    for offset in (1, -1):
        row = self.mRow + offset
        if row < 0 or row >= Constants.MAX_GRIDSIZEY:
            continue
        front = FrontZombieInRow(self, row)
        if front is not None:
            return front
    return None


class SummerMelon(CustomPlantBase):
    _fire_failed = False

    def OnNativeFire(self, orig, target, row, weapon):
        """
        上、本、下三行各锁一只：引擎的 FindTargetZombie(row) 本来就按传入行找，
        所以对每个有目标的行调一次原生 Fire（它负责发射点、预判和特殊僵尸的弹道修正），
        再给这一发新生成的 Melon/Lobbed 挂上夏瓜定义。没有目标的行不空放。
        """
        try:
            plant = self.Plant
            board = self.B
            if plant is None or board is None:
                return
            rows = []
            for candidate in (row - 1, row, row + 1):
                if candidate >= 0 and candidate < Constants.MAX_GRIDSIZEY:
                    rows.append(candidate)
            for aimRow in rows:
                # 上层传下来的 target 只在本行为空时可能是邻行那只（扩索敌的结果），
                # 所以本行必须确认它真在这一行且还活着，否则证明本行空着、不投。
                if (aimRow == row and target is not None and target.mRow == row and
                        not target.mDead and not target.IsDeadOrDying()):
                    aim = target
                else:
                    aim = FrontZombieInRow(plant, aimRow)
                if aim is None:
                    continue
                before = list(board.mProjectiles)
                original = plant.mSeedType
                try:
                    # 原生西瓜负责发射点、目标预判、花盆和特殊僵尸的弹道修正。
                    plant.mSeedType = SeedType.Melonpult
                    orig(plant, aim, aimRow, weapon)
                finally:
                    plant.mSeedType = original
                for shot in board.mProjectiles:
                    if (shot not in before and not shot.mDead and
                            shot.mProjectileType == ProjectileType.Melon and
                            shot.mMotionType == ProjectileMotion.Lobbed):
                        CustomBullets.Attach(shot, SHELL)
                        delta = (board.GridToPixelY(0, aimRow) -
                                 board.GridToPixelY(0, plant.mRow))
                        # 引擎给投手的落点公式是 velX = (leadX - 发射x - 30) / 120，
                        # 也就是真正落地在 leadX - 30；进度必须按那个点算满，否则球到落点
                        # 还没降到位，会越过目标砸到它后面那只。
                        _LOB_STATE[shot] = (float(shot.mPosX), float(shot.mPosY),
                                            float(shot.mShadowY), float(delta),
                                            float(aim.ZombieTargetLeadX(50.0)) - 30.0)
        except Exception as error:
            if not self._fire_failed:
                self._fire_failed = True
                ModLog.Warn(u"SummerMelon fire failed: %s" % error)


DEF.Factory = System.Func[CustomPlantBase](lambda: SummerMelon())
PlantKit.RegisterPlant(DEF)
PlantKit.ManageHook(
    PACK_ID + u".find_target",
    M.HookTo(Plant.FindTargetZombie)(SummerMelon_FindTargetZombie))
PlantKit.Log(u"summer melon registered in pack %s" % PlantKit.CurrentPackId)
