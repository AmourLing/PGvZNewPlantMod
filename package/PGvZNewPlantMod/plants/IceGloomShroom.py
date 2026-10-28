# -*- coding: utf-8 -*-
"""
寒冰忧郁菇 —— 寒冰大喷菇的升级卡（.py 模组植物，和 IceFumeShroom 同一套写法）。

外形、卡图、语音、影子、睡姿全部借原生忧郁菇；索敌和伤害矩形也借它的 3×3，
开火一次打周围 3 行 × 3 格，伤害 40 并附带减速（同寒冰大喷菇那一族）。
另外每次开火朝四个 45° 斜方向各吐一颗孢子（40 伤害、同样带减速，贴图与小喷菇的弹一样并
跟着染成冰蓝，射程限制也照小喷菇的 75 帧），发射方式照杨桃：Star 运动自己给斜向速度，能跨行打。

三个和"升级卡"有关的关键点（都照引擎自己的模型走，不自造机制）：
1. DEF.IsUpgradeCard = True → 引擎的 Plant.IsUpgrade(种子) 认它。
   有了这条，拿着它点到"不能被它升级"的东西上，游戏自己回 NeedsUpgrade，
   也就是**不能直接种在空地上**（和原生忧郁菇/机枪豌豆那 9 张一致）；
   挑战模式抽卡时"场上没有可升级的株就不给这张"也自动跟着走；
   选卡界面里只带了这张紫卡、没带寒冰大喷菇就按开始，Kit 按原生同一道门
   （SeedChooserScreen.CheckSeedUpgrade 那条）弹警告拦下。
2. 配对写在**被升级那一侧**：IceFumeShroom.py 里 DEF.UpgradesToName = u"寒冰忧郁菇"。
   只能按名字，不能按种子号 —— 模组种子的 SeedId 要到首次排版才分配，
   两个脚本互相引用时对方那个号还不存在（存档也是按名字存的）。
3. 忧郁菇的开火是引擎在 UpdateShooting 里按种子硬编码的一轮 4 发
   （mShootingCounter 从 200 往下数，在 126/98/70/42 各回调一次 Fire，
   在 136/108/80/52 各挂一朵 Gloomcloud）。DEF.DrawAsSeed 让 Kit 在那几处
   临时把 mSeedType 换成忧郁菇，所以本株的 Fire 钩子被回调时，
   self.mSeedType 是**借来的忧郁菇**而不是自己 —— 判断"是不是我这株"必须用
   DEF.IsMine(self)（它同时按对象查注册表，换种子期间仍然认得自己），
   别自己写 plant.mSeedType == DEF.Seed 那种比较，否则会漏掉这 4 次回调，
   结果就是"只放烟雾不出伤害"。
"""
import System

from Lawn import (Plant, SeedType, PlantSubClass, ProjectileMotion, ProjectileType)
from LawnMod import MonoModUtils as M
from Sexy.TodLib import ReanimationType, TodCommon
from PGvZPlantKit import (PlantKit, CustomPlantDef, CustomPlantBase, CustomCombat,
                          CustomBullets, CustomBulletDef)

DEF = CustomPlantDef()
DEF.Name = u"寒冰忧郁菇"
DEF.Cost = 150
DEF.RefreshTime = 5000
DEF.Health = 300
DEF.SubClass = PlantSubClass.Shooter
DEF.LaunchRate = 200
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Gloomshroom
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Gloomshroom
DEF.CardBackground = "purple"          # 升级卡的卡面颜色（紫=升级、灰=模仿者、绿=普通）
DEF.StandAnim = ""
# 借原生身份：睡觉与咖啡、开火定格 200 帧（一轮 4 发）、影子缩放与睡姿偏移、语音、算真菌。
DEF.Nocturnal = True
DEF.ShootingCounter = 200
# 忧郁菇那条定格动画的速率引擎写死在它自己的种子分支里（14），自定义种子落到兜底的 35：
# 动画两秒就定住，后面三发是"静止画面在开枪"。四发打到第 42 帧，速率必须一起借。
DEF.ShootingAnimRate = 14.0
DEF.VoiceSeed = SeedType.Gloomshroom
DEF.DrawAsSeed = SeedType.Gloomshroom
DEF.IsFungus = True
DEF.IsUpgradeCard = True
# 寒冰一族的染色（引擎里这份待遇原本只有爆炸坚果/冰墙果有，按 #rrggbb 声明补回去）；
# ParticleTint 管那一轮连喷吐出的 Gloomcloud 孢子团。
DEF.BodyTint = u"#66ccff"
DEF.ParticleTint = u"#66ccff"
DEF.AlmanacText = (
    u"寒冰忧郁菇是寒冰大喷菇的升级：一次向周围 3×3 喷出寒冰孢子，"
    u"同时打中九宫格里的僵尸，并把它们冻得慢下来。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}40（3×3 范围）\n"
    u"{FLAVOR}它把整片都降温了。"
)


class IceGloomShroom(CustomPlantBase):
    pass


PACK_ID = PlantKit.CurrentPackId or u"IceGloomShroom"

# 每次开火再朝四个 45° 斜方向各吐一颗孢子（发射方式照杨桃：Star 运动 = 自己给 mVelX/mVelY，
# 每帧 mPosX += mVelX、mPosY += mVelY，行号跟着走，所以能跨行打到相邻行的僵尸）。
# 载体弹种用 Puff：贴图、落地的 PuffSplat、飞行拖尾都是小喷菇那颗现成的。
# "和小喷菇一样的限制"就是 75 帧寿命 —— 引擎那条判在 mMotionType == Puff 上，
# 斜着飞走的是 Star 运动拿不到，所以按注册子弹的 MaxAge 补同样的 75 帧。
SPORE_ID = PACK_ID + u":spore"
SPORE = CustomBulletDef()
SPORE.Id = SPORE_ID
SPORE.Carrier = ProjectileType.Puff
SPORE.Damage = 40
SPORE.DamageFlags = 2 + 4             # 和植物那一族同一套"怎么打"：盾与本体同扣 + 附加冰冻（减速）
SPORE.DamageRangeFlags = 9          # 小喷菇/大喷菇/忧郁菇那一族"打得着谁"的掩码
SPORE.MaxAge = 75
SPORE.Tint = u"#66ccff"             # 弹体跟着本体一起染冰蓝（留空就是小喷菇原本的紫色）
CustomBullets.Register(SPORE)

# 和杨桃同一档速度（3.33 像素/帧）拆成 45° 的两个分量
SPORE_STEP = 3.33 * System.Math.Cos(TodCommon.DegToRad(45.0))


def FireSpores(plant):
    for dx in (1, -1):
        for dy in (1, -1):
            shot = CustomBullets.Fire(plant.mBoard, plant, SPORE_ID,
                                      plant.mX + 25, plant.mY + 25, plant.mRow,
                                      ProjectileMotion.Star)
            if shot is None:
                continue
            shot.mVelX = SPORE_STEP * dx
            shot.mVelY = SPORE_STEP * dy
            # 弹体贴图是引擎按 mRotation 转着画的，而它给杨桃星旋转的那一支判的是
            # "弹种 == Star"；我们借的是 Puff 弹种（只要它那张图），所以那份旋转轮不到，
            # 贴图就一直正着飞。这里自己按速度方向转过去，四颗才各朝自己的 45°。
            shot.mRotation = System.Math.Atan2(SPORE_STEP * dy, SPORE_STEP * dx)


def Plant_FindTargetZombie__IceGloomShroom(orig, self, theRow, thePlantWeapon):
    if not DEF.IsMine(self):
        return orig(self, theRow, thePlantWeapon)
    # 索敌借忧郁菇的 3×3 矩形；临时换种子必须用完就还原（异常也要还原）。
    original = self.mSeedType
    self.mSeedType = SeedType.Gloomshroom
    try:
        return orig(self, theRow, thePlantWeapon)
    finally:
        self.mSeedType = original


def Plant_Fire__IceGloomShroom(orig, self, theTargetZombie, theRow, thePlantWeapon):
    if not DEF.IsMine(self):
        return orig(self, theTargetZombie, theRow, thePlantWeapon)
    # 2 = 盾与本体同扣，4 = 附加冰冻（减速）；矩形借忧郁菇的 3×3，
    # 而且 DoRowAreaDamage 里"忧郁菇才算 ±1 行"那条特判也靠这次借身份生效。
    CustomCombat.RowAreaDamage(self, 40, 2+4, SeedType.Gloomshroom)
    FireSpores(self)


PlantKit.ManageHook(
    PACK_ID + u".find_target",
    M.HookTo(Plant.FindTargetZombie)(Plant_FindTargetZombie__IceGloomShroom))
PlantKit.ManageHook(
    PACK_ID + u".fire",
    M.HookTo(Plant.Fire)(Plant_Fire__IceGloomShroom))

DEF.Factory = System.Func[CustomPlantBase](lambda: IceGloomShroom())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"ice gloomshroom registered in pack %s" % PlantKit.CurrentPackId)
