"""生成框架自带的通用卡槽背景：green / purple / gray 三种。

来源不是网上找的，是游戏自己的高清卡图集：
    Content\\images\\600x1000\\seedpackets_hd_2x.png
    （2x 表，格 162x212、4 列；用 D:\\PvZWSTools\\UI核对\\unpack_content.py 解出来）

表里机枪射手(40)、香蒲(43)、46 号格是同一张"紫色无立绘"空白卡，48 号格是
同一张卡的"灰色无立绘"版本 —— 两张直接裁出来当 purple / gray。

绿色没有空白格，按"给灰色卡染色"来得到：灰色卡是无彩底的骨架（云、远山、
草地都在），把它逐行贴到原生白天卡的配色上就是绿色版——
    out[r] = 目标行均值 + (灰卡像素 - 灰卡该行均值) * 增益
先用同一套算法拿紫色卡自己的逐行配色去染灰卡，能几乎还原出紫色卡，
这一步的误差就是脚本打印的 self-check；还原不了说明两张空白卡不是同一张图，
那绿色就不该这么生成。框（圆角白边）始终用原像素，不参与染色。

跑这个脚本需要本机装了游戏；输出是随框架 DLL 内嵌的三张 81x50 PNG。
"""

import os
import sys

import numpy as np
from PIL import Image

SHEET_DIR = r"D:\植物大战僵尸\WP_PGVZ\PlantGirlsVsZombies\Content\images\600x1000"
SHEET = "seedpackets_hd_2x.png"
UNPACKER = r"D:\PvZWSTools\UI核对\unpack_content.py"

CEL_W, CEL_H, COLS = 162, 212, 4
CARD_X, CARD_Y, CARD_W, CARD_H = 0, 110, 162, 100   # 格内卡片本体：81x50
PURPLE_CEL, GRAY_CEL = 40, 48
DAY_CELS = (0, 1, 4, 6, 7, 16, 17, 22, 23, 24, 26, 27, 30, 34, 35, 36, 38)
# 卡框内侧区域（外边是白色圆角框，不参与染色）
INSET_TOP, INSET_BOTTOM, INSET_SIDE = 14, 12, 16
STRIP = 14
GAIN_MIN, GAIN_MAX = 0.7, 1.3
# 灰卡染回紫卡的自检：两张空白卡是同一张骨架，只是云的位置略有差别，
# 所以判中位数与 90 分位，不判最大值（最大值会被云边那几像素顶到天上去）。
SELF_CHECK_MEDIAN = 5.0
SELF_CHECK_P90 = 15.0


def load_sheet():
    folder = sys.argv[1] if len(sys.argv) > 1 else SHEET_DIR
    png = os.path.join(folder, SHEET)
    if not os.path.exists(png):
        sys.exit("先解包 %s\\%s.xnb：python %s <Content> --only images"
                 % (folder, SHEET[:-4], UNPACKER))
    return np.array(Image.open(png).convert("RGBA")).astype(np.float64)


def card(sheet, index):
    row, col = divmod(index, COLS)
    cell = sheet[row * CEL_H:row * CEL_H + CEL_H, col * CEL_W:col * CEL_W + CEL_W]
    return cell[CARD_Y:CARD_Y + CARD_H, CARD_X:CARD_X + CARD_W].copy()


def row_stats(pixels):
    mean = pixels.mean(axis=1)
    std = pixels.std(axis=1) + 1e-6
    return mean, std


def day_reference(sheet):
    """白天卡左右两条边条的逐行中位数：立绘压不住这两条，取中位数最稳。"""
    top, bottom = INSET_TOP, CARD_H - INSET_BOTTOM
    strips = []
    for index in DAY_CELS:
        box = card(sheet, index)
        strips.append(box[top:bottom, INSET_SIDE:INSET_SIDE + STRIP])
        strips.append(box[top:bottom, -INSET_SIDE - STRIP:-INSET_SIDE])
    stacked = np.stack(strips)                                  # (n, row, col, rgba)
    colors = np.where(stacked[:, :, :, 3:4] > 200, stacked[:, :, :, :3], np.nan)
    mean = np.nanmedian(colors, axis=(0, 2))
    std = np.nanstd(colors, axis=(0, 2)) + 1e-6
    # 天空到草地是平滑的竖向渐变，个别行被立绘污染就用竖向滑动中位数抹平
    mean = np.stack([np.nanmedian(mean[max(0, r - 3):r + 4], axis=0)
                     for r in range(mean.shape[0])])
    return mean, std


def tint(base, target):
    """把无彩骨架 base 的内芯按行贴到 target 配色上；框保持原像素。"""
    target_mean, target_std = target
    out = base.copy()
    top, bottom = INSET_TOP, CARD_H - INSET_BOTTOM
    region = out[top:bottom, INSET_SIDE:CARD_W - INSET_SIDE]
    pixels = region[:, :, :3]
    src_mean, src_std = row_stats(pixels)
    gain = np.clip(target_std[:, None, :] / src_std[:, None, :], GAIN_MIN, GAIN_MAX)
    region[:, :, :3] = np.clip((pixels - src_mean[:, None, :]) * gain
                               + target_mean[:, None, :], 0, 255)
    out[top:bottom, INSET_SIDE:CARD_W - INSET_SIDE] = region
    return out


def inner(card_image):
    return card_image[INSET_TOP:CARD_H - INSET_BOTTOM,
                      INSET_SIDE:CARD_W - INSET_SIDE, :3]


def save(name, rgba):
    image = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8), "RGBA")
    image = image.resize((CARD_W // 2, CARD_H // 2), Image.LANCZOS)
    out_dir = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                            "..", "src", "PGvZPlantKit", "Assets"))
    path = os.path.join(out_dir, 'card_background_%s.png' % name)
    image.save(path)
    print("%-7s %sx%s -> %s" % (name, image.width, image.height, path))


def main():
    sheet = load_sheet()
    purple, gray = card(sheet, PURPLE_CEL), card(sheet, GRAY_CEL)

    frame_gap = np.abs(purple[:, :, :3] - gray[:, :, :3]).max()
    print('两张空白卡的色差（含框）：max %.0f' % frame_gap)

    restored = tint(gray, row_stats(inner(purple)))
    error = np.abs(inner(restored) - inner(purple)).max(axis=2)
    median, p90, p99, top = (float(np.percentile(error, q)) for q in (50, 90, 99, 100))
    print('self-check 灰卡染回紫卡：中位 %.1f / p90 %.1f / p99 %.1f / 最大 %.1f'
          % (median, p90, p99, top))
    if median > SELF_CHECK_MEDIAN or p90 > SELF_CHECK_P90:
        sys.exit('灰卡和紫卡不是同一张图的骨架，染色法不成立，先别生成绿色')
    print('  （偏差集中在顶部云带：两张空白卡的云位置略有不同，约占 %.1f%% 像素）'
          % (100.0 * float((error > SELF_CHECK_P90).mean())))

    save('purple', purple)
    save('gray', gray)
    save('green', tint(gray, day_reference(sheet)))


if __name__ == "__main__":
    main()
