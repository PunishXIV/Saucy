using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for the Stats tab.</summary>
internal static class StatsZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // Toolbar
        ["Hold Ctrl to reset stats."] = "按住 Ctrl 以重置统计。",
        ["Reset Lifetime"] = "重置总计",
        ["Reset Session"] = "重置本次",

        // Card headlines
        ["no games played"] = "暂无对局记录",
        ["{0} games"] = "{0} 局",
        ["{0} games · {1}% win"] = "{0} 局 · 胜率 {1}%",

        // Table columns
        ["Lifetime"] = "总计",
        ["Session"] = "本次",
        ["Per Hour"] = "每小时",
        ["Session rate since the first counted game of this minigame."] = "本次数据自该小游戏第一局计入统计时开始计算。",

        // Shared rows
        ["Games"] = "局数",
        ["MGP won"] = "获得 MGP",

        // Triple Triad rows
        ["Wins"] = "胜",
        ["Losses"] = "负",
        ["Draws"] = "平",
        ["Cards won"] = "获得卡牌",
        ["Card resale value"] = "卡牌出售价值",
        ["Most played NPC"] = "最常对战的 NPC",
        ["Most won card"] = "获得最多的卡牌",
        ["{0} games vs {1}"] = "与 {1} 对战 {0} 局",
        ["{0} won {1}×"] = "{0} 获得 {1} 次",

        // Cuff-a-Cur rows. The official CN client names for the three hit tiers could not be
        // verified, so these describe the tier (10 / 15 / 25 MGP) instead of guessing at the
        // client wording. Deliberately avoids 暴击 (= critical hit in combat) and 痛击 (used by
        // the panel subtitle "痛击仙人刺").
        ["Bruisings"] = "一般命中",
        ["Punishings"] = "良好命中",
        ["Brutals"] = "完美命中"
    };
}
