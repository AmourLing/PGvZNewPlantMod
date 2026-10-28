# -*- coding: utf-8 -*-
"""
PGvZNewPlantMod 打包脚本（参照 PGvZOnlineMod / PvZWSTools_Inner / 植物娘AI对话）

包内布局就是“解压进 mods\\ 即完成安装”：
  PGvZNewPlantMod.dll              mod 本体（mods 顶层）
  PGvZNewPlantMod\\                框架、清单、插件、assets、spanim
  使用说明.txt                      从 使用说明模板.txt 生成
  update.md                        只有 --publish 正式包才带

用法：
  python 打包.py                 候选包：编译 + VerifyHost + zip，不带 update.md
  python 打包.py --publish 1.0.0 正式包：要求 update.md 有正文，写 已发布.json，清空 update.md
  python 打包.py check           只检查 update.md 是否有可发布正文
  python 打包.py status          查看上次发布记录
"""
import json
import os
import subprocess
import sys
import time
import zipfile

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOLUTION = os.path.join(ROOT, "PGvZNewPlantMod.slnx")
BUILD = os.path.join(ROOT, "src", "PGvZNewPlantMod", "bin", "Release", "net6.0")
MOD_DLL = os.path.join(BUILD, "PGvZNewPlantMod.dll")
KIT_DLL = os.path.join(BUILD, "PGvZPlantKit.dll")
DATA = os.path.join(BUILD, "PGvZNewPlantMod")
PUBLISH = os.path.join(ROOT, "publish")
TEMPLATE = os.path.join(ROOT, "使用说明模板.txt")
NOTES = os.path.join(ROOT, "update.md")
MARKER = os.path.join(ROOT, "已发布.json")
VERIFY = os.path.join(ROOT, "tests", "VerifyHost")

SKELETON = (
    "# 更新说明（打包脚本只把不以 # 开头的行放进正式包）\n"
    "# 发布前在这里写本版改动；正式包会带上正文并把本文件清回这个骨架。\n\n"
)


def run(args, cwd=ROOT):
    print(">", " ".join(args))
    return subprocess.run(args, cwd=cwd).returncode


def notes_lines():
    if not os.path.exists(NOTES):
        return []
    with open(NOTES, "r", encoding="utf-8-sig") as f:
        return [line.rstrip() for line in f
                if line.strip() and not line.lstrip().startswith("#")]


def notes_text():
    return "\n".join(notes_lines()).strip()


def read_marker():
    if not os.path.exists(MARKER):
        return None
    try:
        with open(MARKER, "r", encoding="utf-8-sig") as f:
            return json.load(f)
    except Exception as ex:
        print("!! 已发布.json 读取失败：%s" % ex)
        return None


def write_marker(version, zip_name, notes):
    doc = read_marker() or {
        "说明": "上一次真正发出去的版本。打包不等于发布：只有 --publish 会改这个文件。",
        "发布": [],
    }
    doc.setdefault("发布", [])
    doc["发布"].insert(0, {
        "版本": version,
        "时间": time.strftime("%Y-%m-%d %H:%M:%S"),
        "包": zip_name,
        "说明": notes,
    })
    with open(MARKER, "w", encoding="utf-8") as f:
        json.dump(doc, f, ensure_ascii=False, indent=2)
        f.write("\n")


def build():
    if run(["dotnet", "build", SOLUTION, "-c", "Release", "--nologo", "-v", "minimal"]) != 0:
        print("## mod 编译失败，中止")
        return False
    for path in (MOD_DLL, KIT_DLL, DATA):
        if not os.path.exists(path):
            print("## 缺少构建产物：" + path)
            return False
    print("## mod 编译 OK")
    print("## VerifyHost 回归...")
    # tests/ 不含在上传的解决方案里（测试工具不上云），所以这里单独构建台架再跑。
    if run(["dotnet", "build", os.path.join(VERIFY, "VerifyHost.csproj"),
            "-c", "Release", "--nologo", "-v", "minimal"]) != 0:
        print("## VerifyHost 编译失败，中止")
        return False
    if run(["dotnet", "run", "--project", VERIFY, "-c", "Release", "--no-build"]) != 0:
        print("## VerifyHost 回归未通过，中止")
        return False
    print("## VerifyHost 回归 OK")
    return True


def stamp_dir(publish):
    stamp = time.strftime("%Y%m%d_%H%M%S")
    root = os.path.join(PUBLISH, stamp)
    layout = {
        "framework": os.path.join(root, "仅前置模组"),
        "plants": os.path.join(root, "模组植物"),
        "full": os.path.join(root, "全量整合"),
    }
    for path in layout.values():
        os.makedirs(path, exist_ok=True)
    return stamp, layout


def plant_files():
    """要单独分发的植物文件：plants 下的 .dll 与 .py（跳过缓存和隐藏文件）。"""
    folder = os.path.join(DATA, "plants")
    if not os.path.isdir(folder):
        return []
    return sorted(name for name in os.listdir(folder)
                  if not name.startswith((".", "_"))
                  and os.path.isfile(os.path.join(folder, name))
                  and (name.endswith(".dll") or name.endswith(".py")))


def plant_id(name):
    """PGvZNewPlantMod.FanTian.dll → FanTian；IceFumeShroom.py → IceFumeShroom。"""
    base = os.path.splitext(name)[0]
    return base.split(".", 1)[1] if base.startswith("PGvZNewPlantMod.") else base


PLANT_STEP = (
    "   单株植物：把包里的 \"{file}\" 直接放进\n"
    "     mods\\PGvZNewPlantMod\\plants\\ （已装前置模组的话这个目录已经存在），\n"
    "     不需要解压覆盖别的文件，也不用改 plant_packs.json —— 启动时会自动登记。\n"
)


def read_me(now, kind, file_name=None):
    if os.path.exists(TEMPLATE):
        with open(TEMPLATE, "r", encoding="utf-8-sig") as f:
            text = f.read()
    else:
        text = "PGvZNewPlantMod\n打包时间：{now}\n本包类型：{kind}\n"
    text = text.replace("{now}", now).replace("{kind}", kind)
    if file_name:
        return text.replace("{install_extra}", PLANT_STEP.replace("{file}", file_name))
    # 前置/全量没有"单株那一步"，整段连同它前面的空行一起去掉。
    return text.replace("\n" + PLANT_STEP, "").replace("{install_extra}", "")


def write_common(z, readme, notes=None):
    """三个包共用的说明文件；正式包再带上更新说明。"""
    count = 1
    z.writestr("使用说明.txt", readme.encode("utf-8"))
    if notes is not None:
        z.writestr("update.md", (notes + "\n").encode("utf-8"))
        count += 1
    return count


def make_framework(layout, now, stamp):
    """前置模组：宿主 + 框架 + 清单，plants 目录留空（不含任何模组植物）。"""
    readme = read_me(now, "前置模组（只有框架，不含任何模组植物）")
    path = os.path.join(layout["framework"], "PGvZNewPlantMod_前置模组_%s.zip" % stamp)
    count = 0
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(MOD_DLL, "PGvZNewPlantMod.dll")
        z.write(KIT_DLL, "PGvZNewPlantMod/PGvZPlantKit.dll")
        count += 2
        for name in ("README.md", "plant_packs.template.json"):
            src = os.path.join(DATA, name)
            if os.path.exists(src):
                z.write(src, "PGvZNewPlantMod/" + name)
                count += 1
        assets = os.path.join(DATA, "assets", "README.md")
        if os.path.exists(assets):
            z.write(assets, "PGvZNewPlantMod/assets/README.md")
            count += 1
        # 空清单：前置包不带植物，装好后由 kit 自己把丢进来的植物补进去。
        z.writestr("PGvZNewPlantMod/plant_packs.json",
                   json.dumps({"version": 1, "packs": []}, ensure_ascii=False, indent=2) + "\n")
        z.writestr("PGvZNewPlantMod/plants/", "")
        count += 2
        count += write_common(z, readme)
    return path, count


def make_plant_packs(layout, now):
    """每个模组植物一个包：文件就在压缩包根层，丢进 plants\\ 即生效；名字不带日期。"""
    made = []
    for name in plant_files():
        pid = plant_id(name)
        readme = read_me(now, "单株模组植物：%s" % pid, name)
        path = os.path.join(layout["plants"], "%s.zip" % pid)
        with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
            z.write(os.path.join(DATA, "plants", name), name)
            count = 1 + write_common(z, readme)
        made.append((pid, path, count))
    return made


def make_full(layout, now, stamp, publish, notes):
    """全量包：现在的默认布局（宿主 + 框架 + 全部植物 + 清单 + 素材）。"""
    readme = read_me(now, "全量（框架 + 全部模组植物）")
    path = os.path.join(layout["full"], "PGvZNewPlantMod_全量_%s.zip" % stamp)
    count = 0
    with zipfile.ZipFile(path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(MOD_DLL, "PGvZNewPlantMod.dll")
        z.write(KIT_DLL, "PGvZNewPlantMod/PGvZPlantKit.dll")
        count += 2
        for dirpath, dirnames, filenames in os.walk(DATA):
            dirnames[:] = sorted(d for d in dirnames
                                 if d not in ("SaveData", "spanim", "__pycache__"))
            for name in sorted(filenames):
                if name in ("plant_kit.log",) or name.endswith(".pyc"):
                    continue
                src = os.path.join(dirpath, name)
                rel = os.path.relpath(src, DATA).replace("\\", "/")
                arc = "PGvZNewPlantMod/" + rel
                if arc == "PGvZNewPlantMod/PGvZPlantKit.dll":
                    continue
                z.write(src, arc)
                count += 1
        count += write_common(z, readme, notes if publish else None)
    return path, count


def make_packages(publish, version):
    stamp, layout = stamp_dir(publish)
    notes = notes_text()
    now = time.strftime("%Y-%m-%d %H:%M")

    framework, framework_count = make_framework(layout, now, stamp)
    plants = make_plant_packs(layout, now)
    full, full_count = make_full(layout, now, stamp, publish, notes)

    print("## 打包完成：%s" % os.path.join(PUBLISH, stamp))
    print("   前置模组：%s（%d 个文件，%d bytes）" % (
        os.path.basename(framework), framework_count, os.path.getsize(framework)))
    print("   模组植物：%d 个包 %s" % (len(plants), "、".join(
        "%s（%d 文件）" % (pid, count) for pid, _, count in plants)))
    print("   全量：    %s（%d 个文件，%d bytes）" % (
        os.path.basename(full), full_count, os.path.getsize(full)))
    print("   解压到游戏的 mods\\ 即完成安装（DLL 在顶层，PGvZNewPlantMod 文件夹与它同级）")
    print("   只装前置模组 = 框架可用但没有任何模组植物；再把单个植物包解压进去即可增株。")

    if publish:
        write_marker(version, os.path.basename(full), notes)
        with open(NOTES, "w", encoding="utf-8") as f:
            f.write(SKELETON)
        print("## 已写入 已发布.json：%s（记的是全量包），update.md 已清回骨架" % version)
    else:
        print("   这是候选包：没有 update.md，也没有改 已发布.json。")
        print("   确认要发布时运行：python 打包.py --publish 1.0.0")
    return full


def main():
    argv = sys.argv[1:]
    if "status" in argv:
        doc = read_marker() or {}
        releases = doc.get("发布", [])
        if not releases:
            print("还没有正式发布记录。")
        else:
            print("最近发布：%s（%s）%s" % (
                releases[0].get("版本"), releases[0].get("时间"),
                releases[0].get("包")))
        return 0
    if "check" in argv:
        notes = notes_lines()
        if notes:
            print("## 可以发布：update.md 有 %d 行正文" % len(notes))
            return 0
        print("## 不能发布：update.md 没有正文，请先写这一版改了什么")
        return 1

    publish = "--publish" in argv
    version = None
    if publish:
        at = argv.index("--publish")
        if at + 1 < len(argv) and not argv[at + 1].startswith("-"):
            version = argv[at + 1]
        version = version or ("v" + time.strftime("%Y.%m.%d"))
        if not notes_text():
            print("## 要发布就得先在 update.md 写正文，中止")
            return 1

    if not build():
        return 1
    make_packages(publish, version)
    return 0


if __name__ == "__main__":
    sys.exit(main())
