# -*- coding: utf-8 -*-
"""
寒冰大喷菇 —— 传统 MonoMod 钩子写法的示例（对照 ExamplePlantShooter 的 Kit 事件写法）。

外形/卡图复用原生大喷菇；索敌借用 Fumeshroom 的攻击矩形（前方 3 格），
开火不生成直射弹，改成本行范围伤害 + 喷雾音效（同原生 Fumeshroom.Fire）。

传统钩子的几个关键点（本文件逐条示范）：
1. 处理器定义成模块级自由函数，签名是 (orig, self, ...原方法参数)。
   判断"是不是自己这株"用 DEF.IsMine(plant)（kit 提供的通用方法）。
   自定义种子没有 "SeedType.寒冰大喷菇" 这种枚举名：它的 id 由 kit 在首次排版时
   自动分配，注册那一刻 DEF.SeedId 还是 0，所以 IsMine 在分配前一律返回 False。
   不要把这类判断写成全局 is_mine()：所有植物脚本共享同一个全局命名空间，
   别的植物脚本再定义同名函数会把它覆盖掉；写成 DEF 上的方法就不会冲突。
2. M.HookTo 挂到的是同名方法的第一个重载（两次签名丢失）：
   Plant.FindTargetAndFire 有 2 参/3 参两个重载，按名字挂会落到先声明的 2 参版，
   而 UpdateShooter 实际调的是 3 参版——所以这里改挂没有重载的 FindTargetZombie。
3. 钩子挂在 Plant 类上对全场生效，处理器必须先判断"是不是自己这株"，
   不是自己的调用原样转交 orig 并 return 其结果，否则会改掉所有原生植物的行为。
4. 临时改 mSeedType 一定要"用完就还原"：DoRowAreaDamage、GetPlantAttackRect、
   GetDamageRangeFlags 都按 mSeedType 取矩形/标记，自定义种子在原生表里没条目。
   伤害这种"直接调原生方法"的场景用 kit 助手 CustomCombat.RowAreaDamage，
   它内部换种子、try/finally 还原，异常也不会把这株植物永久变成 Fumeshroom。
5. 索敌这里不能套同样的助手：钩子处理器要调的是 orig（未被钩过的原实现），
   而助手拿到的是 Plant 对象、只能调 plant.FindTargetZombie —— 那会再次进钩子，
   自己调自己无限递归。所以凡是"借矩形跑 orig"的，就得像下面这样手写换/还原。
6. 用 PlantKit.ManageHook 托管钩子：同一个 key 再挂会自动替换，脚本重跑不会叠钩子。
   key 前缀直接用包 ID（PlantKit.CurrentPackId）——.py 植物的包 ID 就是文件名本身
   （IceFumeShroom），和 plant_packs.json 里的条目同名；别自己另编一个名字，否则
   按包清理钩子的 PlantKit.UnmanageHooks(包ID + ".") 就匹配不上，日志也对不上号。
7. "像原生那株"不只是复用动画：引擎有一批按 mSeedType 写死的特判（白天睡觉、开火定格几帧、
   开火烟雾、影子缩放、语音槽、能不能被升级卡覆盖、算不算真菌），自定义种子一条都拿不到。
   这些由 DEF.Nocturnal / ShootingCounter / ShootingAnimRate / VoiceSeed / DrawAsSeed /
   UpgradesTo / UpgradesToName / IsFungus / BodyTint / ParticleTint 声明，
   Kit 负责在对应的引擎方法里补回去；不填就是本文件改动前的样子。
"""
import System

import Lawn
from Lawn import Plant, SeedType, PlantSubClass, PlantWeapon
from LawnMod import MonoModUtils as M
from Sexy.TodLib import ReanimationType, FoleyType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase, CustomCombat

DEF = CustomPlantDef()
DEF.Name = u"寒冰大喷菇"
DEF.Cost = 200
DEF.RefreshTime = 750
DEF.Health = 300
DEF.SubClass = PlantSubClass.Shooter
DEF.LaunchRate = 150
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Fumeshroom
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Fumeshroom
DEF.StandAnim = ""
# 借原生身份：引擎按 mSeedType 特判的那些小灶，自定义种子本来一条都拿不到。
# 这株既然复用大喷菇的外形和索敌，行为也照它来（睡觉、开火定格 50 帧、Fumecloud 烟雾、
# 影子缩放、语音、可被忧郁菇升级卡覆盖、算真菌）。
DEF.Nocturnal = True
DEF.ShootingCounter = 50
DEF.VoiceSeed = SeedType.Fumeshroom
DEF.DrawAsSeed = SeedType.Fumeshroom
DEF.UpgradesTo = SeedType.Gloomshroom
# 也接受模组升级卡「寒冰忧郁菇」：按名字配对，因为两张卡的 SeedId 都要到首次排版才分配。
DEF.UpgradesToName = u"寒冰忧郁菇"
DEF.IsFungus = True
# 寒冰一族的染色：引擎里这份待遇按种子写死（爆炸坚果红、冰墙果蓝），自定义种子不在名单里，
# 按 #rrggbb 声明由 Kit 在 Plant.UpdateReanimColor 里补回去。举着升级卡时的闪烁高亮仍然优先。
# ParticleTint 管喷出去的那团孢子（Fumecloud），走引擎自己的 TodParticleSystem.OverrideColor。
DEF.BodyTint = u"#66ccff"
DEF.ParticleTint = u"#66ccff"
DEF.AlmanacText = (
    u"寒冰大喷菇会向前方 3 格喷出寒冰孢子，伤害本行一路的僵尸。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}40（行内范围）\n"
    u"{FLAVOR}凉快吗？"
)


class IceFumeShroom(CustomPlantBase):
    pass


# CurrentPackId 是 [ThreadStatic]：宿主 FolderPlantLoader 在游戏线程上 BeginPack，值就是文件名；
# 在 WS 控制台里重跑本文件时那个线程没 BeginPack，读到 None，所以留一个同名兜底。
PACK_ID = PlantKit.CurrentPackId or u"IceFumeShroom"

def Plant_FindTargetZombie__IceFumeShroom(orig, self, theRow, thePlantWeapon):
    if not DEF.IsMine(self):
        return orig(self, theRow, thePlantWeapon)
    original = self.mSeedType
    self.mSeedType = SeedType.Fumeshroom
    try:
        return orig(self, theRow, thePlantWeapon)
    finally:
        self.mSeedType = original


def Plant_Fire__IceFumeShroom(orig, self, theTargetZombie, theRow, thePlantWeapon):
    if not DEF.IsMine(self):
        return orig(self, theTargetZombie, theRow, thePlantWeapon)
    # 伤害矩形借大喷菇的（前方 3 格），RowAreaDamage 内部换种子、用完还原。
    CustomCombat.RowAreaDamage(self, 40, 2+4, SeedType.Fumeshroom)
    self.mApp.PlayFoley(FoleyType.Fume)


PlantKit.ManageHook(
    PACK_ID + u".find_target",
    M.HookTo(Plant.FindTargetZombie)(Plant_FindTargetZombie__IceFumeShroom))
PlantKit.ManageHook(
    PACK_ID + u".fire",
    M.HookTo(Plant.Fire)(Plant_Fire__IceFumeShroom))

DEF.Factory = System.Func[CustomPlantBase](lambda: IceFumeShroom())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"ice fumeshroom registered in pack %s" % PlantKit.CurrentPackId)
