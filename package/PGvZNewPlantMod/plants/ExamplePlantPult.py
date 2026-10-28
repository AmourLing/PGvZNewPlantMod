# -*- coding: utf-8 -*-
"""
IronPython 示例：投手（Lobbed 弹道）。

没有单独的 PlantSubClass.Pult；这里同样设为 Shooter，让原生 UpdateShooter 索敌，
OnNativeFire 生成 Cabbage 抛物弹并设置 Lobbed 轨迹。

新语法示范：
- self.FireProjectile(..., motion=ProjectileMotion.Lobbed, velX=..., velZ=..., accZ=...)：
  抛物弹参数用关键字传，替代手写 AddProjectile + 六个字段赋值。
- self.Schedule(延迟, 回调)：新波到来时排三轮延迟齐射（回调直接收 python 方法）。
- self.ZombiesInRow()：本行僵尸列表（这里用来判断要不要浪费卷心菜）。
"""
import System

import Lawn
from Lawn import (SeedType, PlantSubClass, PlantWeapon,
                  ProjectileType, ProjectileMotion)
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
DEF.Name = u"示例植物投手"
DEF.Cost = 100
DEF.RefreshTime = 750
DEF.Health = 300
DEF.SubClass = PlantSubClass.Shooter
DEF.LaunchRate = 300
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Cabbagepult
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Cabbagepult
DEF.StandAnim = ""
DEF.AlmanacText = (
    u"示例植物投手会把卷心菜抛向本行最前方的僵尸。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}40\n"
    u"{KEYWORD}特点：{STAT}抛物线攻击，可越过前排植物\n"
    u"{KEYWORD}齐射：{STAT}每一波开始时额外投掷 3 颗\n"
    u"{FLAVOR}厨房里最不缺的就是投掷物。"
)

VOLLEY_GAP = 18       # 齐射间隔（帧）


class ExamplePlantPult(CustomPlantBase):
    def OnWave(self, wave):
        # 每波开始：三轮延迟齐射。Schedule 的回调在植物死亡后自动作废。
        for i in range(3):
            self.Schedule(VOLLEY_GAP * i, self._lobOne)

    def OnNativeFire(self, orig, target, row, weapon):
        if self.Plant is None or self.B is None:
            return
        if target is None:
            target = self.FindFrontZombie(row)
        if target is None:
            return
        self._lobAt(target)

    def _lobOne(self):
        target = self.FindFrontZombie()
        if target is not None:
            self._lobAt(target)

    def _lobAt(self, target):
        plant = self.Plant
        if plant is None or plant.mDead:
            return
        x = plant.mX + 5
        y = plant.mY - 12
        zombie_rect = target.GetZombieRect()
        dx = target.ZombieTargetLeadX(50.0) - x - 30.0
        if dx < 40.0:
            dx = 40.0
        dz = zombie_rect.mY - y
        self.FireProjectile(
            ProjectileType.Cabbage, 5, -12,
            ProjectileMotion.Lobbed,
            velX=dx / 120.0,
            velZ=-7.0 + dz / 120.0,
            accZ=0.115)

    def OnEatenStart(self, eater):
        # 被啃的第一口就把面前的僵尸砸一记，投手的近身自卫。
        if self.IsAlive:
            self.FireProjectile(ProjectileType.Cabbage, 5, -12,
                                ProjectileMotion.Lobbed, velX=0.8, velZ=-6.5, accZ=0.115)


DEF.Factory = System.Func[CustomPlantBase](lambda: ExamplePlantPult())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"example pult registered in pack %s" % PlantKit.CurrentPackId)
