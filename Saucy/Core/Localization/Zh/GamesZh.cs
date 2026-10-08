using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for mini-game modules (machines, GATEs, cactpot).</summary>
internal static class GamesZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // Shared machine settings

        // Cuff-a-Cur
        ["No Cuff-a-Cur machine nearby. Move closer to the machine."] = "附近没有重击伽美什街机，请靠近街机。",
        ["No Cuff-a-Cur machine nearby. Move closer to a punching machine."] = "附近没有重击伽美什街机，请走到街机前再试。",
        ["Use \"Fixed match count\" below to stop after a set number of games."] = "使用下方的“固定对局次数”可在玩满指定局数后自动停止。",
        ["Runs automatically when enabled. Start the minigame at the Gold Saucer punching machine."] = "启用后自动运行。请在金碟游乐场的重击伽美什街机处开始小游戏。",

        // Any Way the Wind Blows
        ["move left"] = "向左移动",
        ["move right"] = "向右移动",
        ["move down"] = "向下移动",
        ["move up"] = "向上移动",

        // Out on a Limb
        ["No Out on a Limb machine nearby. Move closer to the machine."] = "附近没有孤树无援街机，请靠近街机。",
        ["Walk up to the Out on a Limb machine, set how many games to play, and Saucy runs them."] = "走到孤树无援街机旁，设置要玩的局数，Saucy 会自动完成。",
        ["Runs automatically when enabled at the Out on a Limb machine."] = "在孤树无援街机旁启用后自动运行。",
        ["Options"] = "选项",
        ["Stop at next double-down"] = "在下次双倍下注时停止",
        ["Cashes out at the next double-down and disables automation after the reward."] = "将在下次双倍下注时兑现奖励，并在领取奖励后停用自动化。",
        ["Duty Finder ready also cashes out but leaves automation enabled."] = "任务搜索器就绪时同样会兑现奖励，但保持自动化开启。",
        ["Tuning"] = "调校",
        ["Difficulty"] = "难度",
        ["Titan"] = "泰坦",
        ["Morbol"] = "魔界花",
        ["Cactuar"] = "仙人刺",
        ["Step"] = "步长",
        ["Default"] = "默认",
        ["Spacing between probed cursor positions when searching for the sweet spot. Smaller = more precise but slower; the default works for most setups."] = "搜索最佳落点时探测光标位置的间距。数值越小越精确但更慢；默认值适用于大多数情况。",
        ["Min seconds for another round"] = "再来一轮所需的最少秒数",
        ["Always double down while the minigame timer is above this. Cash out when there is not enough time left for another round."] = "小游戏剩余时间高于此值时总是双倍下注；剩余时间不足再来一轮时兑现奖励。",

        // Panel subtitles
        ["punch the cactuar"] = "痛击仙人刺",
        ["swing the hatchet"] = "挥斧砍树",
        ["dodge the falling slices"] = "躲避落下的刀刃",
        ["statistical safe spot"] = "统计学安全点",
        ["ride shooting minigame"] = "飞行射击小游戏",
        ["daily 3×3 scratcher"] = "每日 3×3 刮刮乐",
        ["weekly 4-digit raffle"] = "每周 4 位数字抽奖",

        // Slice is Right
        ["Draws slice and AoE markers during the GATE."] = "在机遇任务期间绘制刀刃与 AoE 范围标记。",
        ["Automatic movement (Boss Mod VBM AI)"] = "自动移动（Boss Mod VBM AI）",
        ["Activates the VBM AI preset so Boss Mod's Slice is Right module can path you out of hazards."] = "启用 VBM AI 预设，让 Boss Mod 的快刀斩魔模块带你走出危险区域。",
        ["Optional plugin for automatic dodging during the GATE. Overlays still work without it."] = "用于在机遇任务期间自动躲避的可选插件。不安装也可正常显示覆盖层。",
        ["Provides the Slice is Right boss module (hazard zones) and the VBM AI preset Saucy activates during the GATE. Keep the Gold Saucer Slice is Right module enabled in Boss Mod settings."] = "提供快刀斩魔的 Boss 模块（危险区域），以及 Saucy 在机遇任务期间启用的 VBM AI 预设。请在 Boss Mod 设置中保持金碟游乐场的快刀斩魔模块启用。",

        // Any Way the Wind Blows
        ["Shows the statistical safe spot during the GATE."] = "在机遇任务期间显示统计学安全点。",
        ["Automatic movement (vnavmesh)"] = "自动移动（vnavmesh）",
        ["Pathfinds you onto the safe spot while you are off it."] = "当你不在安全点上时自动寻路前往。",
        ["Optional plugin for automatic movement to the safe spot. Overlays still work without it."] = "用于自动移动到安全点的可选插件。不安装也可正常显示覆盖层。",
        ["Pathfinds you onto the statistical safe spot during the GATE."] = "在机遇任务期间自动寻路前往统计学安全点。",

        // Air Force One
        ["Runs automatically when enabled. Plays the Air Force One ride-shooting minigame for you."] = "启用后自动运行，代你完成空军装甲驾驶员飞行射击小游戏。",

        // Mini-Cactpot
        ["Plays Mini Cactpot automatically when you open the daily scratcher at the Gold Saucer."] = "在金碟游乐场打开每日刮刮乐时自动完成仙人微彩。",

        // Jumbo Cactpot
        ["Collect prizes at the Cactpot cashier yourself. Saucy then paths you to the Jumbo broker and handles ticket purchase dialogue and confirms."] = "请自行到仙人彩兑换员处领取奖金。之后 Saucy 会带你前往仙人彩售票员，并自动处理购票对话与确认。",
        ["Number selection"] = "号码选择",
        ["Random"] = "随机",
        ["Specific numbers"] = "指定号码",
        ["Ticket 1 (100 MGP)"] = "第 1 张（100 MGP）",
        ["Ticket 2 (150 MGP)"] = "第 2 张（150 MGP）",
        ["Ticket 3 (200 MGP)"] = "第 3 张（200 MGP）",
        ["Leave a ticket blank to randomize that purchase."] = "留空则该张彩票随机选号。",
        ["Jumbo Cactpot input"] = "仙人彩输入",
        ["Open the Jumbo ticket purchase window to inspect addon nodes."] = "打开仙人彩购票窗口以检查 addon 节点。"
    };
}
