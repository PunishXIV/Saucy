using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for chat and toast messages.</summary>
internal static class ChatZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // Saucy.cs — commands
        ["[Saucy] Triple Triad automation enabled."] = "[Saucy] 九宫幻卡自动化已启用。",
        ["[Saucy] Fixed match count enabled: {0} matches."] = "[Saucy] 已启用固定对局次数：{0} 场。",
        ["[Saucy] Usage: /saucy tt play <number of matches>"] = "[Saucy] 用法：/saucy tt play <对局次数>",
        ["[Saucy] Stopping after the first card drop."] = "[Saucy] 将在首次掉落卡牌后停止。",
        ["[Saucy] Farming all NPC cards once."] = "[Saucy] 将收集所有 NPC 卡牌各一张。",
        ["[Saucy] Unknown command. Available: /saucy, /saucy stop, /saucy d, /saucy tt go | stop | play <n> | cards any | cards all"] =
            "[Saucy] 未知命令。可用命令：/saucy、/saucy stop、/saucy d、/saucy tt go | stop | play <n> | cards any | cards all",

        // AutoRetainerPause.cs
        ["[Saucy] Pausing arcade automation — retainers ready at nearby bell."] = "[Saucy] 暂停街机自动化——附近传唤铃有雇员待处理。",
        ["[Saucy] AutoRetainer pause timed out; resuming automation."] = "[Saucy] AutoRetainer 暂停已超时，恢复自动化。",
        ["[Saucy] AutoRetainer finished; resuming automation."] = "[Saucy] AutoRetainer 处理完成，恢复自动化。",

        // Deck select automation
        ["[Saucy] Could not find deck {0} in the selection list."] = "[Saucy] 在选择列表中找不到卡组 {0}。",
        ["[Saucy] Match started without a deck. Confirm deck selection manually."] = "[Saucy] 对局已开始但未选择卡组，请手动确认卡组选择。",
        ["[Saucy] Could not use game recommended deck. Pick a deck manually or try another option."] =
            "[Saucy] 无法使用游戏推荐卡组，请手动选择卡组或尝试其他选项。",
        ["[Saucy] Using game recommended deck..."] = "[Saucy] 正在使用游戏推荐卡组…",
        ["[Saucy] Selecting first deck..."] = "[Saucy] 正在选择第一个卡组…",

        // Run session
        ["[Saucy] You already have every card from {0}. Farming MGP instead."] = "[Saucy] 你已拥有 {0} 的全部卡牌，改为刷取 MGP。",
        ["[Saucy] Stopped navigation, travel, and triad automation."] = "[Saucy] 已停止导航、传送与幻卡自动化。",

        // Battlehall
        ["[Saucy] The Battlehall is a Duty Finder instance.\nSaucy cannot path there — enter via Duty Finder."] =
            "[Saucy] 幻卡对战室是任务搜索器副本。\nSaucy 无法自动前往——请通过任务搜索器进入。",

        // Map navigation
        ["[Saucy] Navigation timed out."] = "[Saucy] 导航超时。",
        ["[Saucy] Could not reach the Triple Triad NPC."] = "[Saucy] 无法抵达九宫幻卡 NPC。",
        ["[Saucy] Arrived at {0}. Starting Triple Triad..."] = "[Saucy] 已抵达 {0}，正在开始九宫幻卡…",
        ["[Saucy] Could not open Triple Triad with this NPC."] = "[Saucy] 无法与该 NPC 开始九宫幻卡。",

        // Travel
        ["[Saucy] Install vnavmesh to path to NPCs from Saucy."] = "[Saucy] 请安装 vnavmesh 以便 Saucy 自动寻路至 NPC。",
        ["[Saucy] Could not resolve NPC map coordinates."] = "[Saucy] 无法解析 NPC 的地图坐标。",
        ["[Saucy] {0} is in another zone. Install Lifestream to teleport there."] = "[Saucy] {0} 位于其他区域，请安装 Lifestream 以传送前往。",
        ["[Saucy] Lifestream is busy. Try again in a moment."] = "[Saucy] Lifestream 正忙，请稍后再试。",
        ["[Saucy] Lifestream could not start teleport."] = "[Saucy] Lifestream 无法开始传送。",
        ["[Saucy] Teleporting to {0}."] = "[Saucy] 正在传送至 {0}。",
        ["[Saucy] No unlocked aetheryte found for {0}. Opening map."] = "[Saucy] 未找到 {0} 附近已解锁的以太之光，改为打开地图。",
        ["[Saucy] Walking: {0}."] = "[Saucy] 改为步行：{0}。",
        ["[Saucy] Lifestream aethernet to {0}."] = "[Saucy] Lifestream 都市传送至 {0}。",
        ["[Saucy] Teleporting to {0}, then Lifestream aethernet to {1}."] = "[Saucy] 正在传送至 {0}，随后由 Lifestream 都市传送至 {1}。",
        ["[Saucy] Arrived in {0}."] = "[Saucy] 已抵达 {0}。",
        ["[Saucy] Arrived in {0}. Waiting for vnavmesh..."] = "[Saucy] 已抵达 {0}，正在等待 vnavmesh…",
        ["[Saucy] Walking to aethernet hub, then Lifestream to {0}."] = "[Saucy] 正在步行前往都市传送网枢纽，随后由 Lifestream 传送至 {0}。",
        ["[Saucy] Aethernet hub is in another zone. Walking to NPC instead."] = "[Saucy] 都市传送网枢纽位于其他区域，改为步行前往 NPC。",
        ["[Saucy] Could not resolve aethernet hub position. Walking to NPC instead."] = "[Saucy] 无法解析都市传送网枢纽的位置，改为步行前往 NPC。",
        ["[Saucy] Could not reach the aethernet hub. Walking to NPC instead."] = "[Saucy] 无法抵达都市传送网枢纽，改为步行前往 NPC。",
        ["[Saucy] Lifestream could not start aethernet to {0}. Walking instead."] = "[Saucy] Lifestream 无法开始都市传送至 {0}，改为步行。",
        ["[Saucy] Lifestream: aethernet to {0}."] = "[Saucy] Lifestream：都市传送至 {0}。",
        ["[Saucy] Waiting for Lifestream aethernet to {0}..."] = "[Saucy] 正在等待 Lifestream 都市传送至 {0}…",
        ["[Saucy] Lifestream could not take aethernet to {0}. Walking from here instead."] = "[Saucy] Lifestream 无法都市传送至 {0}，改为从当前位置步行。",
        ["[Saucy] Aethernet travel did not start. Walking instead."] = "[Saucy] 都市传送未能开始，改为步行。",

        // Vnav
        ["[Saucy] vnavmesh is not ready for this zone yet. Waiting..."] = "[Saucy] vnavmesh 尚未准备好本区域的导航网格，正在等待…",
        ["[Saucy] vnavmesh is not ready for this zone yet."] = "[Saucy] vnavmesh 尚未准备好本区域的导航网格。",
        ["[Saucy] vnavmesh could not start movement."] = "[Saucy] vnavmesh 无法开始移动。",
        ["[Saucy] navmesh building..."] = "[Saucy] 正在构建导航网格…",
        ["[Saucy] Moving to {0}..."] = "[Saucy] 正在前往 {0}…",
        ["[Saucy] Moving to {0}."] = "[Saucy] 正在前往 {0}。",

        // Multi-area routes
        ["[Saucy] Entering {0}, then moving to the NPC."] = "[Saucy] 正在进入 {0}，随后前往 NPC。",
        ["[Saucy] Lifestream could not teleport for {0}."] = "[Saucy] Lifestream 无法为 {0} 路线进行传送。",
        ["[Saucy] Teleporting for {0}, then moving to the NPC."] = "[Saucy] 正在为 {0} 路线传送，随后前往 NPC。",
        ["[Saucy] Could not resolve aethernet shard \"{0}\"."] = "[Saucy] 无法解析传送网水晶“{0}”。",
        ["[Saucy] Lifestream could not aethernet for {0}."] = "[Saucy] Lifestream 无法为 {0} 路线进行都市传送。",
        ["[Saucy] vnavmesh could not start movement for this route step."] = "[Saucy] vnavmesh 无法为此路线步骤开始移动。",
        ["[Saucy] Did not arrive in {0} after zone transition."] = "[Saucy] 区域切换后未能抵达 {0}。",

        // NPC unlock
        ["[Saucy] {0}'s Triple Triad isn't unlocked yet — complete {1} first."] = "[Saucy] {0} 的九宫幻卡尚未解锁——请先完成 {1}。",
        ["[Saucy] {0}'s Triple Triad isn't unlocked yet."] = "[Saucy] {0} 的九宫幻卡尚未解锁。",
        ["[Saucy] {0}'s Triple Triad isn't unlocked yet — complete one of: {1}."] = "[Saucy] {0} 的九宫幻卡尚未解锁——请先完成以下任务之一：{1}。",
        ["[Saucy] Could not verify Triple Triad unlock for {0}."] = "[Saucy] 无法确认 {0} 的九宫幻卡解锁状态。",
        ["[Saucy] Triple Triad is not available with {0} (unlocked yet?). Aborting."] = "[Saucy] 无法与 {0} 进行九宫幻卡（可能尚未解锁），已中止。",
        ["this NPC"] = "该 NPC",
        ["quest #{0}"] = "任务 #{0}",

        // Punctuation keys used to build the "complete one of: …" quest list.
        [", "] = "、",
        ["\"{0}\""] = "“{0}”",

        // Arcade module conflicts
        ["Disabled {0} to enable {1}."] = "已关闭「{0}」以启用「{1}」。",
        ["Disabled {0} and {1} to enable {2}."] = "已关闭「{0}」和「{1}」以启用「{2}」。"
    };
}
