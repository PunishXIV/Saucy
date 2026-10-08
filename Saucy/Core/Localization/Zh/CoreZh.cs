using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for core UI (sidebar, panels, stats, debug, shared widgets).</summary>
internal static class CoreZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        ["Enable"] = "启用",

        // Sidebar
        ["MACHINES"] = "街机",
        ["GATES"] = "机遇任务",
        ["OTHER GAMES"] = "其他游戏",
        ["Out on a Limb"] = "孤树无援",
        ["Cuff-a-Cur"] = "重击伽美什",
        ["Slice is Right"] = "快刀斩魔",
        ["Wind Blows"] = "喷风中的幸存者",
        ["Air Force One"] = "空军装甲驾驶员",
        ["Triple Triad"] = "九宫幻卡",
        ["Mini-Cactpot"] = "仙人微彩",
        ["Jumbo Cactpot"] = "仙人彩",
        ["Stats"] = "统计",
        ["About"] = "关于",
        ["Debug"] = "调试",
        ["Saucy theme"] = "Saucy 主题",
        ["Designed by Wah"] = "由 Wah 设计",
        ["Language"] = "语言",
        ["Automatic"] = "自动",
        ["♥ Ko-fi (to support my gacha addiction)"] = "♥ Ko-fi（支持一下我的抽卡瘾）",

        // Window title status
        ["Idle"] = "空闲",
        ["Enabled: {0}"] = "已启用：{0}",
        ["Any Way the Wind Blows"] = "喷风中的幸存者",

        // Triad panel tabs
        ["Main"] = "主设置",
        ["Cache"] = "缓存",

        // Debug panel
        ["Gold Saucer gate"] = "金碟机遇任务",
        ["No active gate director."] = "没有进行中的机遇任务。",
        ["Triple Triad NPC menu"] = "九宫幻卡 NPC 菜单",
        ["No select string menu open."] = "没有打开的选项菜单。",

        // /saucy command help (shown by /xlhelp)
        ["Opens the Saucy menu."] = "打开 Saucy 菜单。",
        ["/saucy stop → stop all navigation and automation"] = "/saucy stop → 停止所有导航与自动化",
        ["/saucy tt go → enable Triple Triad automation"] = "/saucy tt go → 启用九宫幻卡自动化",
        ["/saucy tt stop → stop Triple Triad automation"] = "/saucy tt stop → 停止九宫幻卡自动化",
        ["/saucy tt play <n> → fixed match count"] = "/saucy tt play <n> → 固定对局次数",
        ["/saucy tt cards any → stop after first card drop"] = "/saucy tt cards any → 首次掉落卡牌后停止",
        ["/saucy tt cards all → farm all NPC cards once"] = "/saucy tt cards all → 将 NPC 的全部卡牌各刷一张",
        ["/saucy d → toggle debug panels"] = "/saucy d → 切换调试面板"
    };
}
