using System.Collections.Generic;

namespace Saucy.Localization;

/// <summary>Simplified Chinese entries for Triple Triad UI (settings, collection, windows).</summary>
internal static class TriadZh
{
    public static readonly Dictionary<string, string> Entries = new()
    {
        // --- TriadSettingsUi: main toggles ---
        ["No Triple Triad NPC nearby. Move closer to the NPC you want to play."] = "附近没有九宫幻卡 NPC。请靠近想要对战的 NPC。",
        ["No Triple Triad NPC nearby ({0}). Move closer to the NPC you want to play."] = "附近没有九宫幻卡 NPC（{0}）。请靠近想要对战的 NPC。",
        ["Accepts match invites, selects a deck, and plays through rematches. Turn on before or during match prep."] = "自动接受对局邀请、选择卡组并连续再战。请在对局准备之前或期间开启。",
        ["Open window when challenging an NPC"] = "挑战 NPC 时自动打开窗口",
        ["Gold Saucer card search panels"] = "金碟游乐场卡牌搜索面板",
        ["Shows a searchable card list beside the Gold Saucer card UI, including Edit Deck (TriadBuddy-style [No.1] ordering). Also shows NPC search on the main card collection screen."] = "在金碟游乐场卡牌界面旁显示可搜索的卡牌列表，包括编辑卡组（TriadBuddy 风格的 [No.1] 排序）。同时在卡牌收藏主界面显示 NPC 搜索。",

        // --- TriadSettingsUi: card sections ---
        ["Deck"] = "卡组",
        ["Run mode"] = "运行模式",
        ["Travel"] = "前往",
        ["Map navigation"] = "地图导航",
        ["Notifications"] = "通知",
        ["Dependencies"] = "依赖插件",
        ["Optional integrations"] = "可选集成",

        // --- TriadSettingsUi: deck optimizer settings ---
        ["Show deck automation messages in chat"] = "在聊天栏显示卡组自动化消息",
        ["Shows [Saucy] deck optimizer, deck selection, and profile-write messages in chat. Does not hide the game's own lines (e.g. \"in play for the next match\")."] = "在聊天栏显示 [Saucy] 的卡组优化、卡组选择和配置写入消息。不会隐藏游戏自身的提示（例如“将在下一场对局中使用”）。",
        ["Skip beaten or completed NPCs"] = "跳过已击败或已收集完成的 NPC",
        ["Don't start a background deck build when you target an NPC you've already beaten or whose cards you already own. Still builds at match prep if you challenge them."] = "选中已击败或卡牌已集齐的 NPC 时，不启动后台卡组构建。发起挑战时仍会在对局准备阶段构建。",
        ["Pause while Questionable is running"] = "Questionable 运行时暂停",
        ["Pauses background deck builds while Questionable (/qst) is questing. Match prep still builds if you challenge an NPC."] = "当 Questionable（/qst）执行任务时暂停后台卡组构建。挑战 NPC 时对局准备阶段仍会构建。",
        ["Optimizer threads (0 = all)"] = "优化器线程数（0 = 全部）",
        ["All"] = "全部",
        ["Uses {0} of {1} logical cores for parallel deck tests."] = "使用 {1} 个逻辑核心中的 {0} 个并行测试卡组。",
        ["Parallel threads while building an optimized deck (0 = all cores)."] = "构建优化卡组时的并行线程数（0 = 全部核心）。",
        ["Linux / Wine (XLCore, Steam Deck): deck builds are capped to half your logical cores no matter what you pick here. Using every core for parallel deck builds can hard-crash the game under Wine."] = "Linux / Wine（XLCore、Steam Deck）：无论此处如何设置，卡组构建都会被限制为逻辑核心数的一半。在 Wine 下使用全部核心并行构建卡组可能导致游戏崩溃。",
        ["Optimizer timeout (min)"] = "优化器超时（分钟）",
        ["min"] = "分钟",
        ["Cancels a background deck build after this long. If the build is not finished by deck select, Saucy falls back to your best profile deck. Map navigation waits until a deck is ready."] = "后台卡组构建超过该时长后自动取消。如果到选择卡组时仍未完成，Saucy 会回退到你最优的配置卡组。地图导航会等到卡组就绪后再出发。",

        // --- TriadSettingsUi: deck body ---
        ["Challenge an NPC once to load your profile decks here."] = "先挑战一次 NPC，即可在此加载你的配置卡组。",
        ["Auto-pick best deck"] = "自动选择最佳卡组",
        ["Picks a deck at deck select. Default: highest opening win % among your profile decks. If none of those decks have 5 cards, Saucy uses the game's Recommended button."] = "在选择卡组时自动挑选。默认选择配置卡组中起手胜率最高的一套。如果这些卡组都不足 5 张卡牌，Saucy 会使用游戏自带的推荐按钮。",
        ["Use cached deck if available"] = "优先使用缓存卡组",
        ["At match prep, loads a matching cached deck into profile slot 5 when one exists for this NPC and rules. Auto-pick still sims your profile decks and picks the highest opening win %. Cannot be combined with Build optimized deck."] = "对局准备时，如果存在匹配此 NPC 和规则的缓存卡组，则将其载入配置槽位 5。自动选择仍会模拟你的配置卡组并挑选起手胜率最高的一套。不能与“构建优化卡组”同时启用。",
        ["Build optimized deck"] = "构建优化卡组",
        ["At match prep, builds a deck from your owned cards when no valid cache or existing \"NPC (Saucy)\" profile deck fits this NPC and rules. Rebuilds if you have gained {0} or more new cards since the last build for that NPC. Saves to profile slot 5 and selects it. Cannot be combined with Use cached deck."] = "对局准备时，如果没有适用于此 NPC 和规则的有效缓存或“NPC (Saucy)”配置卡组，则用你拥有的卡牌构建一套。若自上次为该 NPC 构建以来新获得 {0} 张或更多卡牌，则会重新构建。保存到配置槽位 5 并选中。不能与“优先使用缓存卡组”同时启用。",
        ["Target NPC: {0} (calculating win %…)"] = "目标 NPC：{0}（正在计算胜率…）",
        ["Target NPC: {0}"] = "目标 NPC：{0}",
        ["(none)"] = "（无）",
        ["Game recommended"] = "游戏推荐",
        ["Select deck"] = "选择卡组",
        ["Game recommended uses FFXIV's built-in deck suggestion for the current match (not Saucy sims)."] = "游戏推荐使用 FFXIV 内置的当前对局卡组建议（而非 Saucy 模拟）。",
        ["Deck {0}"] = "卡组 {0}",

        // --- TriadSettingsUi: run mode ---
        ["Choose when Saucy stops playing. On plugin load no option is selected and Saucy rematches until automation is disabled."] = "选择 Saucy 何时停止对战。插件加载时默认不选任何选项，Saucy 会持续再战直到自动化被关闭。",
        ["Fixed match count"] = "固定对局次数",
        ["How many times:"] = "次数：",
        ["Matches left this session: {0}"] = "本次运行剩余对局：{0}",
        ["Stop after first card drop"] = "首次掉落卡牌后停止",
        ["Farm all NPC cards once"] = "将 NPC 全部卡牌各刷一张",
        ["No stop condition — runs until automation is disabled."] = "无停止条件——持续运行直到自动化被关闭。",
        ["Stops rematching while Duty Finder is ready."] = "任务搜索器准备就绪时暂停再战。",
        ["NPC: {0}"] = "NPC：{0}",
        ["(match registration open)"] = "（对局报名界面已打开）",
        ["NPC: reading match registration…"] = "NPC：正在读取对局报名信息…",
        ["NPC: open match registration to list missing cards."] = "NPC：打开对局报名界面以列出缺少的卡牌。",
        ["Missing cards only"] = "仅缺少的卡牌",
        ["Card #{0}"] = "卡牌 #{0}",
        ["You already have every card from this NPC. Uncheck \"Missing cards only\" or choose a different NPC."] = "你已拥有该 NPC 的全部卡牌。请取消勾选“仅缺少的卡牌”或选择其他 NPC。",
        ["Start a match with an NPC to see which cards are still missing."] = "与 NPC 开始一场对局以查看仍缺少哪些卡牌。",

        // --- TriadSettingsUi: notifications ---
        ["Log out when run completes"] = "运行完成后登出",
        ["Logs out of the game when a run finishes: fixed match count reaches zero, card drop mode triggers, or card farm completes."] = "运行结束时登出游戏：固定对局次数归零、卡牌掉落模式触发或收卡完成。",
        ["Play sound when run completes"] = "运行完成后播放提示音",
        ["Open sound folder — drop MP3s here to add your own."] = "打开音效文件夹——将 MP3 放入此处即可添加自定义音效。",

        // --- TriadSettingsUi: dependencies & travel ---
        ["Optional plugins for pathing to NPCs on the map, teleporting when needed, and starting unlock quests from Saucy."] = "可选插件：用于在地图上寻路至 NPC、按需传送，以及从 Saucy 启动解锁任务。",
        ["Walk to Triple Triad NPCs from Saucy map links after you arrive in the zone."] = "抵达区域后，通过 Saucy 的地图链接步行前往九宫幻卡 NPC。",
        ["Teleport to the nearest aetheryte before pathing when the NPC is far away or in another zone."] = "当 NPC 较远或位于其他区域时，先传送到最近的以太之光再寻路。",
        ["Start Triple Triad unlock quests from Saucy card and NPC search."] = "从 Saucy 的卡牌和 NPC 搜索中启动九宫幻卡解锁任务。",
        ["Mount used before vnavmesh pathing to Triple Triad NPCs."] = "使用 vnavmesh 寻路前往九宫幻卡 NPC 之前召唤的坐骑。",
        ["Mount roulette"] = "随机坐骑",
        ["Mount #{0} (unavailable)"] = "坐骑 #{0}（不可用）",
        ["{0} (unavailable)"] = "{0}（不可用）",
        ["Default uses the game's Mount Roulette general action. Pick a mount to always summon that one before map navigation."] = "默认使用游戏的随机坐骑通用技能。选择一个坐骑后，地图导航前将始终召唤它。",

        // --- TriadSettingsUi: optimizer status ---
        ["Building deck for {0}…"] = "正在为 {0} 构建卡组…",
        ["Opening win chance: {0}"] = "起手胜率：{0}",
        ["(updating…)"] = "（更新中…）",
        ["Opening win chance: calculating…"] = "起手胜率：计算中…",
        ["Cards owned: {0}"] = "拥有卡牌：{0}",
        ["Possible decks: {0}"] = "可能的卡组数：{0}",
        ["Tested: {0}"] = "已测试：{0}",
        ["Progress: {0}%"] = "进度：{0}%",
        ["Time left: {0}"] = "剩余时间：{0}",
        ["Cancel build"] = "取消构建",
        ["Stops the current background deck build."] = "停止当前的后台卡组构建。",

        // --- TriadSettingsUi: auto-pick summary line (TriadSession.GetAutoPickDeckSummary) ---
        ["In match…"] = "对局进行中…",
        ["Calculating…"] = "计算中…",
        ["No usable decks"] = "没有可用的卡组",
        ["Profile decks aren't simmable"] = "配置卡组无法模拟",
        ["No complete profile decks"] = "没有完整的配置卡组",
        ["{0} — using game recommended"] = "{0}——改用游戏推荐",
        // Composition shapes. Own keys because the translator picks the wording around the
        // fragment; the " · " separator is kept as-is, it reads the same in both languages.
        ["{0} · cached deck exists"] = "{0} · 已有缓存卡组",
        ["{0} · rebuilding after new cards"] = "{0} · 获得新卡后正在重新构建",
        ["{0} · generating new (cached deck exists)"] = "{0} · 正在生成新卡组（已有缓存卡组）",
        ["Paused — Questionable is running"] = "已暂停——Questionable 正在运行",
        ["Skipped — NPC beaten or all cards owned"] = "已跳过——NPC 已击败或卡牌已集齐",
        ["Waiting for vnavmesh…"] = "正在等待 vnavmesh…",
        ["Optimizer cooling down · still generating new"] = "优化器冷却中 · 仍在生成新卡组",
        ["Cached deck outdated · generating new…"] = "缓存卡组已过期 · 正在生成新卡组…",
        ["Cached deck exists · still generating new…"] = "已有缓存卡组 · 仍在生成新卡组…",
        ["Profile deck in slot {0} · still generating new…"] = "配置槽位 {0} 中已有卡组 · 仍在生成新卡组…",
        ["Waiting for optimized deck…"] = "正在等待优化卡组…",
        ["Last build timed out · still generating new…"] = "上次构建已超时 · 仍在生成新卡组…",

        // --- TriadCacheSettingsUi ---
        ["No cached decks yet."] = "暂无缓存卡组。",
        ["Log in to view cached decks."] = "登录后查看缓存卡组。",
        ["no cached decks"] = "无缓存卡组",
        ["1 cached deck"] = "1 套缓存卡组",
        ["{0} cached decks"] = "{0} 套缓存卡组",
        ["No optimized decks saved for this character yet."] = "该角色尚未保存任何优化卡组。",
        ["unknown time"] = "未知时间",
        ["NPC {0}"] = "NPC {0}",
        [" · {0}% opening"] = " · 起手胜率 {0}%",
        // Deck-cache row shapes. Full-width parens, and the separator stays " · " because
        // the surrounding fragments are already spaced Latin-style timestamps.
        ["{0}{1} · {2}"] = "{0}{1} · {2}",
        ["{0} ({1}){2} · {3}"] = "{0}（{1}）{2} · {3}",
        ["Clear deck cache for this character"] = "清除此角色的卡组缓存",
        ["Deletes OptimizedDeckCache.json for the logged-in character."] = "删除当前登录角色的 OptimizedDeckCache.json。",
        ["Hold Ctrl while clicking to clear the cache for this character."] = "按住 Ctrl 点击以清除此角色的缓存。",

        // --- TriadNpcMapUi ---
        ["The Battlehall is a Duty Finder instance.\nSaucy cannot path there."] = "幻卡对战室是任务搜索器副本区域。\nSaucy 无法寻路前往。",
        ["Install vnavmesh to walk to this NPC."] = "安装 vnavmesh 以步行前往此 NPC。",
        ["Left-click: path there and farm missing cards."] = "左键：寻路前往并收集缺少的卡牌。",
        ["Right-click: path there and farm MGP."] = "右键：寻路前往并刷 MGP。",
        ["Enables Triple Triad automation on arrival."] = "抵达后自动启用九宫幻卡自动化。",
        ["Left-click uses MGP farm if you already have every card from this NPC."] = "若已拥有该 NPC 的全部卡牌，左键将改为刷 MGP。",
        ["Left-click with missing cards builds an optimized deck even if that option is off."] = "缺少卡牌时左键会构建优化卡组，即使该选项已关闭。",
        ["Click to path with vnavmesh."] = "点击以使用 vnavmesh 寻路。",
        ["Uses Lifestream for travel (aetheryte or aethernet shard)."] = "使用 Lifestream 移动（以太之光或传送网水晶）。",

        // --- TriadNpcQuestUi ---
        ["Quest #{0}"] = "任务 #{0}",
        ["[Saucy] Install Questionable (/qst) to start quests from Saucy."] = "[Saucy] 安装 Questionable（/qst）后即可从 Saucy 启动任务。",
        ["[Saucy] Sent \"{0}\" to Questionable."] = "[Saucy] 已将“{0}”发送给 Questionable。",
        ["[Saucy] Questionable could not start \"{0}\"."] = "[Saucy] Questionable 无法启动“{0}”。",
        ["Install Questionable (/qst) to start this quest."] = "安装 Questionable（/qst）后即可启动此任务。",
        ["Not supported in Questionable yet."] = "Questionable 尚不支持此任务。",
        ["Start \"{0}\" with Questionable"] = "使用 Questionable 启动“{0}”",
        ["Quest already accepted."] = "任务已接受。",
        ["Quest unavailable in Questionable."] = "该任务在 Questionable 中不可用。",
        ["Prerequisites not met yet (check Questionable /qst)."] = "前置条件尚未满足（请查看 Questionable /qst）。",

        // --- TriadCardInfoWindow ---
        ["Card Info"] = "卡牌信息",
        ["Reward from:"] = "奖励来源：",
        ["Show on map"] = "在地图上显示",
        ["Not available"] = "暂无",
        ["Show in NPC tab"] = "在 NPC 标签页中显示",

        // --- TriadCardSearchWindow ---
        ["Card Search"] = "卡牌搜索",
        ["Deck Cards"] = "卡组卡牌",
        ["Loading card data…"] = "正在加载卡牌数据…",
        ["Cards"] = "卡牌",
        ["NPC reward cards only"] = "仅 NPC 奖励卡牌",
        ["Unowned only"] = "仅未拥有",
        ["(Collection filtering is active)"] = "（收藏筛选生效中）",
        ["Loading NPC data…"] = "正在加载 NPC 数据…",
        ["No NPC data loaded."] = "未加载 NPC 数据。",
        ["No NPCs available."] = "没有可用的 NPC。",
        ["No NPCs match the current filters."] = "没有符合当前筛选条件的 NPC。",
        ["Hide beaten NPCs"] = "隐藏已击败的 NPC",
        ["Hide completed NPCs"] = "隐藏已收集完成的 NPC",
        ["Unowned rewards: {0}"] = "未拥有的奖励：{0}",
        ["Optimized deck"] = "优化卡组",
        ["Builds a deck from your owned cards and saves it to profile slot 5. Run this before travel so it is ready at match prep."] = "用你拥有的卡牌构建一套卡组并保存到配置槽位 5。建议在出发前运行，以便对局准备时可直接使用。",
        ["Build deck"] = "构建卡组",
        ["Rebuild deck"] = "重新构建卡组",
        ["Runs a fresh build and overwrites the deck in profile slot 5."] = "重新构建并覆盖配置槽位 5 中的卡组。",

        // --- TriadNpcStatsWindow (shared with search window) ---
        ["NPC"] = "NPC",
        ["NPC stats"] = "NPC 统计",
        ["MGP per match:"] = "每局 MGP：",
        ["Matches tracked: {0}"] = "已记录对局：{0}",
        ["Game stats:"] = "对局统计：",
        ["{0} wins, {1} draws, {2} losses"] = "{0} 胜，{1} 平，{2} 负",
        ["Win rate: {0}"] = "胜率 {0}",
        ["Reward stats:"] = "奖励统计：",
        ["MGP: {0}"] = "MGP：{0}",
        ["{0} card: {1}"] = "{0} 卡牌：{1}",
        ["Includes MGP from selling cards"] = "包含出售卡牌获得的 MGP",
        ["Copy"] = "复制",
        ["Reset"] = "重置",
        ["{0} stats:"] = "{0} 的统计：",
        ["{0} matches (W:{1}/D:{2}/L:{3})"] = "{0} 场对局（胜：{1}/平：{2}/负：{3}）",
        ["no card drops"] = "无卡牌掉落",
        // Own key rather than Loc.T("{0},", …): a punctuation-only format is not something a
        // translator can act on, and CJK wants the full-width comma.
        ["NPC stats,"] = "NPC 统计，",

        // Multi-area route tooltips. Proper nouns with no official CN name stay English.
        ["Uses the Doman Enclave aetheryte when unlocked, otherwise Yanxia Namai and the enclave entrance."] =
            "解锁后使用多玛飞地的以太之光，否则经由延夏的纳玛伊村与飞地入口前往。",
        ["Foundation aetheryte, aethernet to The Last Vigil, then enter the manor."] =
            "先到基础层以太之光，经都市传送网前往 The Last Vigil，然后进入宅邸。",
        ["Lower Jeuno routes via Mamook and the Yak T'el portal."] =
            "Lower Jeuno 路线经由 Mamook 与 Yak T'el 的传送门。"
    };
}
