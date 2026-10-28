# -*- coding: utf-8 -*-
"""
IronPython 示例：特殊/灰烬植物。

复用一个原生灰烬动画，倒计时结束后临时切到 Cherrybomb 的种子类型，
调用原生 Plant.DoSpecial() 执行原版樱桃炸弹的范围伤害和死亡流程。

新语法示范：
- self.OnEatenStart(eater)：被僵尸下嘴的瞬间提前引爆（灰烬植物不该被吃掉）。
- self.OnBitten(amount)：基类逐帧比对血量得出的掉血通知，半血时同样引爆。
- self.Every(间隔, key)：节拍器语法（本文件用普通倒计时，见 ExamplePlantProducer.py 的用法）。
"""
import System

import Lawn
from Lawn import SeedType
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
DEF.Name = u"示例植物特殊炸弹"
DEF.Cost = 150
DEF.RefreshTime = 5000
DEF.Health = 300
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Cherrybomb
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Cherrybomb
DEF.StandAnim = ""
DEF.AlmanacText = (
    u"示例植物特殊炸弹种下后会短暂延迟，然后引爆并消灭周围僵尸。\n"
    u"{SHORTLINE}\n"
    u"{KEYWORD}伤害：{STAT}范围爆炸\n"
    u"{KEYWORD}引信：{STAT}被啃咬或半血时立刻起爆\n"
    u"{FLAVOR}它把整片草坪的热情都压缩进了一颗豆子里。"
)


class ExamplePlantSpecialBomb(CustomPlantBase):
    def __init__(self):
        self._due = 0
        self._done = False

    def OnPlanted(self):
        self._due = self.Frame + 90

    def OnUpdate(self):
        if self.Frame < self._due:
            return
        self._explode()

    def OnEatenStart(self, eater):
        # 僵尸刚下嘴：灰烬植物不给啃，直接炸。
        self._explode()

    def OnBitten(self, amount):
        # 任意掉血来源（碾压/啃咬）打掉一半血就起爆。
        plant = self.Plant
        if plant is not None and plant.mPlantHealth < plant.mPlantMaxHealth / 2:
            self._explode()

    def _explode(self):
        if self._done or not self.IsAlive:
            return
        plant = self.Plant
        if plant is None or plant.mDead:
            return
        self._done = True
        # DoSpecial 内部按 mSeedType 分派；临时借用樱桃炸弹的分支。
        plant.mSeedType = SeedType.Cherrybomb
        plant.DoSpecial()


DEF.Factory = System.Func[CustomPlantBase](lambda: ExamplePlantSpecialBomb())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"example special bomb registered in pack %s" % PlantKit.CurrentPackId)
