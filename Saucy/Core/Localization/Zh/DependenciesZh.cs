using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for arcade run settings and plugin dependency prompts.</summary>
internal static class DependenciesZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // Arcade run settings
        ["Stops queuing new games while Duty Finder is ready."] = "任务搜索器就绪时暂停开始新对局。",

        // Fake break
        ["Take a break"] = "休息一下",
        ["After playing for a while, pause starting new games for a short break. Current games can still finish."] = "游玩一段时间后，暂停开始新对局以稍作休息。进行中的对局仍会打完。",
        ["Play time before break (minutes)"] = "休息前的游玩时长（分钟）",
        ["Break length (minutes)"] = "休息时长（分钟）",
        ["On break — {0}:{1} remaining"] = "休息中——剩余 {0}:{1}",

        // AutoRetainer integration
        ["Placed bell must be in range"] = "需在已放置的传唤铃范围内",
        ["Pause when retainers are ready (bell nearby)"] = "雇员就绪时暂停（附近有传唤铃）",
        ["Cuff-a-Cur and Out on a Limb only. When a placed summoning bell is in range and AutoRetainer reports retainers ready, Saucy finishes the current game, opens the bell, waits for AutoRetainer to finish, then resumes."] = "仅适用于重击伽美什与孤树无援。当已放置的传唤铃在范围内、且 AutoRetainer 报告雇员就绪时，Saucy 会先打完当前这一局，然后打开传唤铃，等待 AutoRetainer 完成后再继续。",
        ["Waiting for AutoRetainer…"] = "正在等待 AutoRetainer…",
        ["Retainers ready — finishing current game…"] = "雇员已就绪——正在打完当前这一局…",
        ["No placed summoning bell in range."] = "范围内没有已放置的传唤铃。",
        ["Optional plugin for retainer venture automation."] = "用于雇员探险自动化的可选插件。",
        ["Collects and reassigns retainer ventures. Saucy enables it at the bell automatically."] = "收取并重新派遣雇员探险。Saucy 会在传唤铃处自动启用它。",

        // Plugin dependency status and actions
        ["Installed"] = "已安装",
        ["Installed but not loaded"] = "已安装但未加载",
        ["Not installed"] = "未安装",
        ["Open installer"] = "打开插件安装器",
        ["Add repository"] = "添加插件库",
        ["Add {0} to Custom Plugin Repositories."] = "将 {0} 添加到自定义插件库。",
        ["Install plugin"] = "安装插件",
        ["Install {0} from its plugin repository."] = "从所属插件库安装 {0}。",

        // Plugin dependency chat output
        ["[Saucy] {0} repository is already added."] = "[Saucy] {0} 的插件库已添加。",
        ["[Saucy] Added {0} repository."] = "[Saucy] 已添加 {0} 的插件库。",
        ["[Saucy] Installed {0}."] = "[Saucy] 已安装 {0}。",
        ["[Saucy] Could not install {0}. Check the plugin installer for details."] = "[Saucy] 无法安装 {0}。请在插件安装器中查看详情。"
    };
}
