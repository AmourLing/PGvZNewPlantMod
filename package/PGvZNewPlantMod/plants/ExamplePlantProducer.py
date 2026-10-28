# -*- coding: utf-8 -*-
"""
IronPython 示例：生产植物（种子/图鉴格/选卡格自动分配）

由 PGvZNewPlantMod.dll 从 PGvZNewPlantMod/plants/ 加载。
文件名 ExamplePlantProducer.py 就是 pack id；plant_packs.json 控制加载顺序/启用。
宿主已预加载 Lawn / PGvZPlantKit，脚本只写定义和行为。

新语法示范：
- self.Every(1200, 'sun')：节拍器替代手写 _next_sun 记账；OnPlanted 里先"上弦"
  （消耗掉首次立刻触发的滴答），OnUpdate 只管掉阳光。
- self.DropSun(面额, dx, dy)：自动挑 5/15/25/50 档位的原生阳光。
- self.OnWave(wave)：每波开始送一朵大阳光。
- PlantKit.LevelStarted += 处理器：全局事件（含 IronPython 直接 += 的写法）。
"""
import System

import Lawn
from Lawn import SeedType
from Sexy.TodLib import ReanimationType
from PGvZPlantKit import PlantKit, CustomPlantDef, CustomPlantBase

DEF = CustomPlantDef()
# SeedId / AlmanacRow / AlmanacColumn / ChooserRow / ChooserColumn 全部留空，
# 由 PlantKit 自动避开原生卡和图鉴占位后分配；需要固定位置时再手动填。
DEF.Name = u"示例植物生产"
DEF.Cost = 50
DEF.RefreshTime = 750
DEF.Health = 300
DEF.LaunchRate = 0
# 不新建 Spine，直接借用原生向日葵身体和卡图。
DEF.NativeReanim = True
DEF.NativeReanimType = ReanimationType.Sunflower
DEF.HasPreview = False
DEF.CardNativeSeed = SeedType.Sunflower
DEF.StandAnim = ""              # 让原生 PlantInitialize 播它自己的 idle
DEF.AlmanacText = u"示例植物生产会周期性地洒下三枚阳光。\n{SHORTLINE}\n" \
                  u"{KEYWORD}阳光：{STAT}每 20 秒 75\n" \
                  u"{KEYWORD}星愿：{STAT}每一波开始时额外掉落一朵大阳光\n" \
                  u"{FLAVOR}她把夜空里的星尘收集起来，分给草坪上的每一个伙伴。"

SUN_PERIOD = 1200        # 三枚小阳光的节拍（帧）


class ExamplePlantProducer(CustomPlantBase):
    def OnPlanted(self):
        # 上弦：消耗掉 Every 的首个滴答，第一次掉阳光仍在完整周期之后。
        self.Every(SUN_PERIOD, 'sun')

    def OnUpdate(self):
        if not self.Every(SUN_PERIOD, 'sun'):
            return
        self.DropSun(25, 10, 10)
        self.DropSun(25, 22, 10)
        self.DropSun(25, 34, 10)

    def OnWave(self, wave):
        # 每一波开始，为全队补一朵大阳光。
        self.DropSun(50, 10, 0)

    def OnEatenStart(self, eater):
        # 被啃时自救一口：立刻掉一枚小阳光当作"报警"。
        self.DropSun(15, 0, -10)


def _on_level_started(board):
    PlantKit.Log(u"示例植物生产：第 %s 关开始，星尘已就位" % board.mLevel)


# 全局事件钩子：IronPython 直接 += / -=，不需要写 MonoMod。
PlantKit.LevelStarted += _on_level_started


DEF.Factory = System.Func[CustomPlantBase](lambda: ExamplePlantProducer())
PlantKit.RegisterPlant(DEF)
PlantKit.Log(u"example producer registered in pack %s" % PlantKit.CurrentPackId)
