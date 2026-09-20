using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for deck optimizer status, deck selection, and their chat output.</summary>
internal static class OptimizerZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // Premade optimizer status and block reasons
        ["No NPC selected."] = "未选择 NPC。",
        ["Card data is still loading."] = "卡牌数据仍在加载中。",
        ["Turn on Auto-pick best deck in Triad settings."] = "请在九宫幻卡设置中开启“自动选择最佳卡组”。",
        ["Enable Auto-pick best deck in Triad settings."] = "请在九宫幻卡设置中启用“自动选择最佳卡组”。",
        ["No owned cards found in the collection cache."] = "收藏缓存中没有找到已拥有的卡牌。",
        ["Another deck optimization is already running."] = "已有另一个卡组优化正在运行。",
        ["Building deck… {0}%"] = "正在构建卡组… {0}%",
        ["Building deck… {0}% ({1})"] = "正在构建卡组… {0}%（{1}）",
        ["Ready in profile slot {0}"] = "已就绪，位于配置槽位 {0}",
        ["Last build timed out; try again."] = "上次构建超时，请重试。",

        // Optimizer chat output
        ["[Saucy] {0}"] = "[Saucy] {0}",
        ["[Saucy] Optimizing deck for {0}..."] = "[Saucy] 正在为 {0} 优化卡组…",
        ["[Saucy] Still optimizing deck for {0}..."] = "[Saucy] 仍在为 {0} 优化卡组…",
        ["[Saucy] Rebuilding deck for {0} ({1} new cards since last build)."] =
            "[Saucy] 正在为 {0} 重新构建卡组（自上次构建以来新增 {1} 张卡牌）。",
        ["[Saucy] Deck optimization cancelled for {0}."] = "[Saucy] 已取消为 {0} 优化卡组。",
        ["[Saucy] Deck optimization interrupted for {0}; retry {1}/{2}…"] =
            "[Saucy] 为 {0} 优化卡组被中断，正在重试 {1}/{2}…",
        ["[Saucy] Deck optimizer deck has invalid card at slot {0}."] = "[Saucy] 优化卡组的第 {0} 张卡牌无效。",
        ["[Saucy] Deck optimizer skipped: no owned cards in collection cache."] =
            "[Saucy] 已跳过卡组优化：收藏缓存中没有已拥有的卡牌。",
        ["[Saucy] Waiting for vnavmesh before building deck…"] = "[Saucy] 正在等待 vnavmesh 后再构建卡组…",
        ["[Saucy] Waiting for zone route before building deck…"] = "[Saucy] 正在等待跨区路线后再构建卡组…",
        ["[Saucy] Deck optimizer aborted; using best deck found so far."] =
            "[Saucy] 卡组优化已中止，将使用目前找到的最佳卡组。",
        ["[Saucy] Failed to write optimized deck to profile."] = "[Saucy] 无法将优化卡组写入配置。",
        ["[Saucy] Deck optimizer timed out after {0} min; using best deck found so far."] =
            "[Saucy] 卡组优化在 {0} 分钟后超时，将使用目前找到的最佳卡组。",
        ["[Saucy] Optimized deck written to slot {0} for {1}."] = "[Saucy] 已将 {1} 的优化卡组写入槽位 {0}。",
        ["[Saucy] Loaded cached deck into profile slot {0} for {1}."] =
            "[Saucy] 已将 {1} 的缓存卡组载入配置槽位 {0}。",
        ["[Saucy] Using cached deck for {0} in profile slot {1}."] =
            "[Saucy] 正在使用配置槽位 {1} 中 {0} 的缓存卡组。",
        ["[Saucy] Using existing optimized deck for {0} in profile slot {1}."] =
            "[Saucy] 正在使用配置槽位 {1} 中 {0} 的现有优化卡组。",
        ["[Saucy] Profile reader unavailable; optimizing deck for {0} (cannot save to profile)."] =
            "[Saucy] 无法读取卡组配置，仍会为 {0} 优化卡组（无法保存到配置）。",

        // Deck selection chat output
        ["[Saucy] Selecting \"{0}\"..."] = "[Saucy] 正在选择“{0}”…",
        ["[Saucy] Selecting \"{0}\" (slot {1})..."] = "[Saucy] 正在选择“{0}”（槽位 {1}）…",
        ["[Saucy] Selecting deck {0}..."] = "[Saucy] 正在选择卡组 {0}…",
        ["[Saucy] Selecting optimized deck {0}..."] = "[Saucy] 正在选择优化卡组 {0}…",
        ["[Saucy] Retrying with deck {0}..."] = "[Saucy] 正在改用卡组 {0} 重试…"
    };
}
