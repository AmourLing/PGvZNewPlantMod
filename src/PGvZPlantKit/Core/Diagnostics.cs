using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Lawn;

namespace PGvZPlantKit
{
    /// <summary>
    /// 一次性诊断快照：把散在注册表、钩子、素材、清单里的状态汇成一份文本。
    /// 控制台里 <c>PlantKit.DumpReport()</c> 一行就能看清"现在到底装了什么、哪段没装上"。
    /// 只读不改状态，任意线程可调；每一段各自 try，一段崩了不影响其余段落。
    /// </summary>
    internal static class Diagnostics
    {
        private static readonly List<KeyValuePair<string, Func<string>>> Sections =
            new List<KeyValuePair<string, Func<string>>>();

        /// <summary>宿主/第三方 mod 追加自己那一段（标题 + 返回文本）。</summary>
        public static void AddSection(string title, Func<string> provider)
        {
            if (string.IsNullOrEmpty(title) || provider == null)
            {
                return;
            }
            Sections.Add(new KeyValuePair<string, Func<string>>(title, provider));
        }

        public static string Build()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("PGvZPlantKit diagnostics ")
              .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).AppendLine();
            Part(sb, "环境", RuntimeEnvironment);
            Part(sb, "包与加载顺序", Packs);
            Part(sb, "植物定义", Plants);
            Part(sb, "钩子", Hooks);
            Part(sb, "素材与子弹", Assets);
            Part(sb, "自检告警", Checks);
            for (int i = 0; i < Sections.Count; i++)
            {
                KeyValuePair<string, Func<string>> section = Sections[i];
                Part(sb, section.Key, () => section.Value());
            }
            return sb.ToString();
        }

        /// <summary>写盘并返回"路径 + 错误"；path 为空时落到 mod 目录 diagnostics.txt。</summary>
        public static string Write(string path)
        {
            string text = Build();
            return Save(text, path) + Environment.NewLine + text;
        }

        /// <summary>游戏里按 F9 的那条路：只写盘并往日志里记一行落点，不打控制台输出。</summary>
        public static void DumpToLog()
        {
            ModLog.Info("diagnostics written: " + Save(Build(), null));
        }

        private static string Save(string text, string path)
        {
            string target = string.IsNullOrEmpty(path)
                ? Path.Combine(ModPaths.StorageDirectory, "diagnostics.txt")
                : path;
            try
            {
                File.WriteAllText(target, text);
                return target;
            }
            catch (Exception ex)
            {
                ModLog.Error("diagnostics write failed", ex);
                return "! 写文件失败: " + ex.Message;
            }
        }

        private static void Part(StringBuilder sb, string title, Func<string> body)
        {
            sb.AppendLine().AppendLine("== " + title);
            string text;
            try
            {
                text = body();
            }
            catch (Exception ex)
            {
                text = "! 本段采集失败: " + ex.Message;
            }
            sb.AppendLine(string.IsNullOrEmpty(text) ? "(空)" : text.TrimEnd());
        }

        private static string RuntimeEnvironment()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("runtime   : " +
                (OperatingSystem.IsAndroid() ? "Android" :
                 OperatingSystem.IsWindows() ? "Windows" : "Other") +
                ", .NET " + System.Environment.Version);
            sb.AppendLine("kit       : " + typeof(Diagnostics).Assembly.GetName().Version +
                " (" + typeof(Diagnostics).Assembly.Location + ")");
            try
            {
                sb.AppendLine("lawn      : " + typeof(LawnApp).Assembly.GetName().Version +
                    " (" + typeof(LawnApp).Assembly.Location + ")");
            }
            catch
            {
            }
            sb.AppendLine("mod dir   : " + ModPaths.ModDirectory);
            sb.AppendLine("storage   : " + ModPaths.StorageDirectory);
            sb.AppendLine("log       : " + Path.Combine(ModPaths.StorageDirectory, "plant_kit.log"));
            sb.AppendLine("screenshot: " + Screenshot.Directory);
            return sb.ToString();
        }

        private static string Packs()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("manifest  : " + LoadOrder.ManifestPathUsed);
            foreach (string packId in LoadOrder.KnownPackIds)
            {
                int count = 0;
                foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
                {
                    if (string.Equals(def.PackId, packId, StringComparison.OrdinalIgnoreCase))
                    {
                        count++;
                    }
                }
                sb.AppendLine((LoadOrder.IsEnabled(packId) ? "on  " : "off ") + packId +
                    "  (" + count + " 株)");
            }
            return sb.ToString();
        }

        private static string Plants()
        {
            StringBuilder sb = new StringBuilder();
            foreach (CustomPlantDef def in CustomPlantRegistry.Defs)
            {
                sb.Append('[').Append(def.SeedId).Append("] ").Append(def.Name)
                  .Append("  pack=").Append(def.PackId)
                  .Append(" cost=").Append(def.Cost)
                  .Append(" refresh=").Append(def.RefreshTime)
                  .Append(" hp=").Append(def.Health).Append('/').Append(def.MaxHealth)
                  .Append(" sub=").Append(def.SubClass)
                  .Append(" launch=").Append(def.LaunchRate).AppendLine();
                sb.Append("      anim: native=").Append(def.NativeReanimType).Append(
                        def.NativeReanimType != Sexy.TodLib.ReanimationType.None ? " (复用原生)" : "")
                  .Append(" slot=").Append(def.ReanimSlot)
                  .Append(" type=").Append((int)def.ReanimType)
                  .Append(" spine='").Append(def.SpineName ?? "")
                  .Append("' reanim='").Append(def.ReanimName ?? "").Append("']").AppendLine();
                sb.Append("      card: asset='").Append(def.CardAsset ?? "")
                  .Append("' bg='").Append(def.CardBackground ?? "")
                  .Append("' native=").Append(def.CardNativeSeed)
                  .Append(" scale=").Append(def.CardArtScale.ToString("0.###"))
                  .Append(" dy=").Append(def.CardOffsetY.ToString("0.###")).AppendLine();
                sb.Append("      layout: chooser=(").Append(def.ChooserRow).Append(',').Append(def.ChooserColumn)
                  .Append(def.AutoChooser ? ") auto" : ") 手填")
                  .Append(" almanac=(").Append(def.AlmanacRow).Append(',').Append(def.AlmanacColumn)
                  .Append(def.AutoAlmanac ? ") auto" : ") 手填")
                  .Append(" order=").Append(def.ChooserOrder).AppendLine();
                sb.Append("      flags:").Append(def.Selectable ? " selectable" : " 不可选")
                  .Append(def.Deprecated ? " deprecated" : "")
                  .Append(def.ReadyImmediately ? " readyImmediately" : "")
                  .Append(def.HasPreview ? " preview" : " 无预览")
                  .Append(def.Factory == null ? " 无Factory!" : "")
                  .AppendLine();
                string borrow = BorrowText(def);
                if (borrow.Length > 0)
                {
                    sb.Append("      borrow: ").AppendLine(borrow);
                }
                sb.Append("      hooks: canPlantAt=").Append(def.CanPlantAt != null)
                  .Append(" costProvider=").Append(def.CostProvider != null)
                  .Append(" excludeConveyor=").Append(def.ExcludeFromConveyor != null)
                  .Append(" excludeImitater=").Append(def.ExcludeFromImitaterPool != null)
                  .AppendLine();
            }
            return sb.Length == 0 ? "(没有注册任何植物)" : sb.ToString();
        }

        /// <summary>借原生身份的声明汇总；一个都没借时返回空串（报告里就不占一行）。</summary>
        private static string BorrowText(CustomPlantDef def)
        {
            List<string> parts = new List<string>();
            if (def.Nocturnal) parts.Add("nocturnal");
            if (def.IsFungus) parts.Add("fungus");
            if (def.ShootingCounter > 0) parts.Add("shootHold=" + def.ShootingCounter);
            if (def.ShootingAnimRate > 0f) parts.Add("shootRate=" + def.ShootingAnimRate);
            if (!string.IsNullOrEmpty(def.BodyTint)) parts.Add("tint=" + def.BodyTint);
            if (!string.IsNullOrEmpty(def.ParticleTint)) parts.Add("ptint=" + def.ParticleTint);
            if (def.VoiceSeed != SeedType.None) parts.Add("voice=" + def.VoiceSeed);
            if (def.DrawAsSeed != SeedType.None) parts.Add("drawAs=" + def.DrawAsSeed);
            if (def.UpgradesTo != SeedType.None) parts.Add("upgradesTo=" + def.UpgradesTo);
            if (!string.IsNullOrEmpty(def.UpgradesToName))
            {
                parts.Add("upgradesTo=" + def.UpgradesToName);
            }
            if (def.IsUpgradeCard) parts.Add("isUpgradeCard");
            return string.Join(" ", parts);
        }

        private static string Hooks()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("kit hooks : " + CustomPlantHooks.HookCount);
            sb.AppendLine("failures  : " + (CustomPlantHooks.HookFailures.Count == 0
                ? "0" : CustomPlantHooks.HookFailures.Count + " 条"));
            foreach (string failure in CustomPlantHooks.HookFailures)
            {
                sb.AppendLine("  ! " + failure);
            }
            sb.AppendLine("managed   : " + HookRegistry.Count);
            foreach (string key in HookRegistry.Keys)
            {
                sb.AppendLine("  - " + key);
            }
            return sb.ToString();
        }

        private static string Assets()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("images    : " + AssetStore.Count);
            foreach (string name in AssetStore.Names)
            {
                sb.AppendLine("  - " + name);
            }
            sb.AppendLine("bullets   : " + CustomBullets.Count);
            foreach (CustomBulletDef bullet in CustomBullets.All)
            {
                sb.AppendLine("  - " + bullet.Id + " carrier=" + bullet.Carrier +
                    " img='" + bullet.Image + "' tint='" + bullet.Tint + "' dmg=" + bullet.Damage +
                    " damageFlags=" + bullet.DamageFlags +
                    " rangeFlags=" + bullet.DamageRangeFlags +
                    " splash=" + bullet.SplashDamage +
                    (bullet.Pierce > 0 ? " pierce=" + bullet.Pierce : "") +
                    (bullet.MaxAge > 0 ? " maxAge=" + bullet.MaxAge : "") +
                    (bullet.Speed > 0f ? " speed=" + bullet.Speed.ToString("0.##") : "") +
                    (bullet.Poison != null
                        ? " poison=" + (string.IsNullOrEmpty(bullet.Poison.Name)
                            ? "(未命名)" : bullet.Poison.Name)
                        : "") +
                    (bullet.Burn != null ? " burn=" + bullet.Burn.Name : "") +
                    (bullet.SkipCollision ? " skipCollision" : "") +
                    (bullet.Motion != null ? " motion" : "") +
                    (bullet.Impact != null ? " impact" : ""));
            }
            sb.AppendLine("poisons   : " + CustomCombat.PoisonSpecs.Count +
                " (驱动 " + CustomCombat.PoisonDriverCalls + " 拍)");
            foreach (CustomCombat.PoisonSpec poison in CustomCombat.PoisonSpecs)
            {
                sb.AppendLine("  - " + (string.IsNullOrEmpty(poison.Name) ? "(未命名)" : poison.Name) +
                    " 每刀=" + (poison.FixedDamage > 0
                        ? poison.FixedDamage + "固定"
                        : poison.DamagePercent + "%上限") +
                    " 时长=" + CustomCombat.TickSpan(poison) + " 间隔=" + poison.Interval +
                    (poison.Ticks > 0 ? " 刀数=" + poison.Ticks : "") +
                    " 首刀=" + (poison.FirstTick > 0 ? poison.FirstTick : poison.Interval) +
                    " 减速=" + poison.SlowPercent + " 显色=" + poison.Tint +
                    " take=" + (poison.TakeFlags != 0 ? poison.TakeFlags.ToString() : "继承子弹") +
                    " range=" + (poison.RangeFlags != 0 ? poison.RangeFlags.ToString() : "继承子弹") +
                    " 补刀上限=" + poison.TopUpHits);
            }
            sb.AppendLine("burns     : " + CustomBurn.BurnSpecs.Count +
                " (烧中 " + CustomBurn.BurnHitsApplied + " 刀，溅射 " +
                CustomBurn.BurnSplashApplied + " 刀，被门槛挡 " + CustomBurn.BurnGateBlocked + " 刀)");
            foreach (CustomBurn.BurnSpec burn in CustomBurn.BurnSpecs)
            {
                sb.AppendLine("  - " + (string.IsNullOrEmpty(burn.Name) ? "(未命名)" : burn.Name) +
                    " 每刀=" + burn.DamagePercent + "%总上限" +
                    (burn.RoundDown ? "(向下取整)" : "(向上取整)") +
                    " 时长=" + CustomBurn.TickSpan(burn) + " 间隔=" + burn.Interval +
                    (burn.Ticks > 0 ? " 刀数=" + burn.Ticks : "") +
                    " 首刀=" + (burn.FirstTick > 0 ? burn.FirstTick : burn.Interval) +
                    (burn.SplashWidth > 0
                        ? " 溅射=" + burn.SplashWidth + "px/±" + burn.SplashRowSpan + "行"
                        : "") +
                    " 显色=" + burn.Tint +
                    (string.IsNullOrEmpty(burn.TintColor) ? "" : "(" + burn.TintColor + ")") +
                    " take=" + (burn.TakeFlags != 0 ? burn.TakeFlags.ToString() : "继承子弹") +
                    " range=" + (burn.RangeFlags != 0 ? burn.RangeFlags.ToString() : "继承子弹"));
            }
            return sb.ToString();
        }

        private static string Checks()
        {
            List<string> warnings = DefChecks.RunAll();
            if (warnings.Count == 0)
            {
                return "没有告警（全部定义看起来正常）";
            }
            StringBuilder sb = new StringBuilder();
            foreach (string warning in warnings)
            {
                sb.AppendLine("  ! " + warning);
            }
            return sb.ToString();
        }
    }
}
