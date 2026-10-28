# -*- coding: utf-8 -*-
"""
IronPython 示例：直射射手（PlantSubClass.Shooter）。

SeedId 留 0，由 PlantKit 自动分配；身体和卡图复用原生豌豆射手。
原生 UpdateShooter 负责索敌和射击动画，OnNativeFire 负责生成自定义豌豆。

新语法示范：
- self.FindFrontZombie(row)：本行最前僵尸（替代手写 mZombies 循环）。
- self.FireProjectile(...)：一行发弹（弹型/偏移/轨迹/速度都收口到基类）。
- self.OnZombieDied(zombie)：本行击杀回馈——每 3 杀自动追发一颗星星弹。
- self.OnWave(wave)：新波到来时进入 300 帧的狂热（额外双发）。
- self.OnDrawTop(g)：头顶画击杀计数的小星星。
- self.ManageHook(...)：传统 MonoMod 钩子写法的受管接入——Kit 事件没覆盖的
  场景直接挂引擎方法，植物死亡自动卸载、重跑脚本不需要手写幂等守卫。
"""
import System

import Lawn
from Lawn import Zombie
from LawnMod import MonoModUtils as M
from Sexy import AtlasResources
from Lawn import (SeedType, PlantSubClass, PlantWeapon,
                  ProjectileType, ProjectileMotion)
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
DEF.Name = u"示例植物射手"
DEF.Cost = 100
DEF.RefreshTime = 750
DEF.Health = 300
DEF.SubClass = PlantSubClass.Shooter
DEF.LaunchRate = 90
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Peashooter
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Peashooter
DEF.StandAnim = ""
DEF.AlmanacText = (
    u"示例植物射手会持续向本行最前方的僵尸射击。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}20\n"
    u"{KEYWORD}战绩：{STAT}每击杀 3 只补射一颗星星\n"
    u"{FLAVOR}它负责提醒大家：草坪的另一头又来客人了。"
)

FEVER_FRAMES = 300      # 新一波到来后的狂热时长
KILLS_PER_STAR = 3      # 每击杀几只补射一颗星星


class ExamplePlantShooter(CustomPlantBase):
    def __init__(self):
        self._kills = 0
        self._fever_until = 0

    def OnLevelStart(self):
        # 开局广播示范：只在日志里报个到。
        PlantKit.Log(u"示例植物射手就位（第 %d 行）" % self.Row)

    def OnPlanted(self):
        # 传统钩子接入示范：盯住任意僵尸开饭（事件只派给被啃的植物，钩子是全量流）。
        # self.ManageHook 托管后：这株植物被移除时自动卸载；HookResult 由注册表
        # 永久持有，GC 终结器不会把它再卸一遍导致崩溃。
        self.ManageHook(M.HookTo(Zombie.StartEating)(self._on_any_eating))

    def _on_any_eating(self, orig, zombie):
        orig(zombie)
        if self.IsAlive and zombie.mRow == self.Row and self.Every(300, 'eatlog'):
            PlantKit.Log(u"示例植物射手：第 %d 行有僵尸开饭了" % self.Row)

    def OnWave(self, wave):
        self._fever_until = self.Frame + FEVER_FRAMES

    def OnNativeFire(self, orig, target, row, weapon):
        if self.Plant is None or self.B is None:
            return
        if target is None:
            # 原生偶尔传空目标（自定义种子常见），交给基类的找怪助手。
            target = self.FindFrontZombie(row)
        if target is None:
            return
        self.FireProjectile(ProjectileType.Pea, 60, -10)
        if self.Frame < self._fever_until:
            # 狂热期双发。
            self.Schedule(12, lambda: self.FireProjectile(ProjectileType.Pea, 60, -10))

    def OnZombieDied(self, zombie):
        self._kills += 1
        if self._kills % KILLS_PER_STAR == 0 and self.IsAlive:
            self.FireProjectile(ProjectileType.Star, 60, -30)

    def OnDrawTop(self, g):
        # 头顶按击杀数画几颗小星星示意（原生星星贴图，演示 OnDrawTop 自绘层）。
        stars = self._kills // KILLS_PER_STAR
        if stars <= 0 or not self.IsAlive:
            return
        image = AtlasResources.IMAGE_STAR40
        for i in range(min(stars, 5)):
            g.DrawImage(image, -6 + i * 12, -92, 24, 24)


DEF.Factory = System.Func[CustomPlantBase](lambda: ExamplePlantShooter())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"example shooter registered in pack %s" % PlantKit.CurrentPackId)
