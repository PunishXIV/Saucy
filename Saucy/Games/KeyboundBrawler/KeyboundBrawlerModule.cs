using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using static ECommons.GenericHelpers;
namespace Saucy.KeyboundBrawler;

public unsafe class KeyboundBrawlerModule : Framework.Module
{
    public override string InternalName => ModuleNames.KeyboundBrawler;
    public override string Name => ModuleDisplayNames.KeyboundBrawler;

    // tankbuster isn't here on purpose: the boss casts it, the word to type is the green one
    private static readonly string[] SpecialCasts = ["Self-destruct", "Burst", "Needles", "Big Bomber", "Germinate"];

    // typing while these resolve is punished. cast bars have no countdown, so the end is
    // first-seen + CastMs (measured from the cast start/end log lines)
    private static readonly (string Name, int CastMs, int AfterMs)[] PauseCasts =
    [
        ("Sensory Jammer", 4700, 4500),
        ("Cursed Gaze", 4700, 4000),
    ];

    // KTGEWord slot: word in node 8. KTGEnemyCastBar slot: name in node 5, stack counter (not a countdown) in node 4
    private const int WordNodeIndex = 8;
    private const int CastNameNodeIndex = 5;
    private const int CastStacksNodeIndex = 4;

    // KTGInput: text input component at node 4, typed text in its node 4
    private const int InputComponentIndex = 4;
    private const int InputTextNodeIndex = 4;

    private const int MgpItemId = 29;
    private const int MgpIcon = 65025;
    private const int MgcItemId = 52322;
    private const int MgcIcon = 65140;
    private const int CofferItemId = 52450;
    private const int CofferIcon = 26531;

    private const int SubmitPendingMs = 3000;
    private const int PauseBeforeEndMs = 1000;

    // ponytail: fixed px cutoff, ignores resolution/ui scale
    private const float MaxCastPairPx = 200;

    private static readonly string[] TierNames = ["BOSS-STACKS", "GREEN", "STACKS", "SPECIAL", "mob", "boss"];

    private record Cast(int Index, string Name, int Stacks, Vector2 Pos);

    private record Slot(int Index, string Word, string Cast, int Stacks, float CastPx, IBattleNpc? Enemy, float Distance, int Rank);

    private readonly Random rng = new();
    private readonly Dictionary<string, long> castSeenAt = [];
    private readonly Dictionary<string, long> submitted = [];
    private List<Slot> slots = [];
    private string currentWord = "";
    private string textToType = "";
    private int currentSlot;
    private int typedChars;
    private long nextActionAt;
    private string pausedBy = "";
    private long pauseUntil;
    private long nextEscAt;
    private bool resultRecorded;

    private static KeyboundBrawlerSettings Settings => C.KeyboundBrawler;

    public override void Enable()
    {
        Svc.Framework.Update += OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw += DrawOverlay;
    }

    public override void Disable()
    {
        Svc.Framework.Update -= OnUpdate;
        Svc.PluginInterface.UiBuilder.Draw -= DrawOverlay;
        Reset();
    }

    private void Reset()
    {
        slots.Clear();
        currentWord = "";
    }

    private void OnUpdate(IFramework _)
    {
        try
        {
            TrackResultWindow();

            var input = GetVisibleAddon("KTGInput");
            if (input == null)
            {
                Reset();
                return;
            }

            var now = Environment.TickCount64;
            var casts = ReadCasts();
            slots = ReadSlots(casts);
            UpdatePause(casts, now);

            if (!Settings.AutoType || pausedBy != "" || now < nextActionAt)
            {
                return;
            }

            // the word only changes a few seconds after a submit, skip it meanwhile and retry if it's still there after
            foreach (var (key, until) in submitted)
            {
                if (until < now)
                {
                    submitted.Remove(key);
                }
            }

            // only drop the current word if it's gone, a higher priority target waits for the next pick
            if (currentWord != "" && slots.All(s => s.Word != currentWord))
            {
                currentWord = "";
            }

            if (currentWord == "")
            {
                var best = slots.FirstOrDefault(s => !submitted.ContainsKey(SlotKey(s.Index, s.Word)));
                if (best == null)
                {
                    return;
                }

                currentWord = best.Word;
                currentSlot = best.Index;
                Log($"pick {currentSlot}:\"{currentWord}\" from " + string.Join(", ", slots.Select(s =>
                    $"{s.Index}:\"{s.Word}\" {TierNames[s.Rank]} cast=\"{s.Cast}\"@{s.CastPx:F0}px {s.Distance:F0}y{(submitted.ContainsKey(SlotKey(s.Index, s.Word)) ? " pending" : "")}")));
                // the game ignores case and ?!-'
                textToType = new string([.. currentWord.ToLowerInvariant().Where(c => !"?!-'".Contains(c))]);
                typedChars = 0;
            }

            // the game clears the box a bit after enter and eats early keys, check the box holds what we sent before each key
            var inBox = ReadText(GetSlotNode(input, InputComponentIndex, InputTextNodeIndex));
            if (!inBox.Equals(textToType[..typedChars].Trim(), StringComparison.OrdinalIgnoreCase))
            {
                // esc on an empty box quits the duty, only press it with text in the box, throttled
                if (inBox != "" && now >= nextEscAt)
                {
                    WindowsKeypress.SendKeypress((int)VirtualKey.ESCAPE);
                    nextEscAt = now + 300;
                }

                typedChars = 0;
                nextActionAt = now + Settings.MinCharDelayMs;
                return;
            }

            if (typedChars < textToType.Length)
            {
                SendChar(textToType[typedChars]);
                typedChars++;
                nextActionAt = now + (typedChars == textToType.Length ? Settings.EnterDelayMs : NextCharDelay());
                return;
            }

            WindowsKeypress.SendKeypress((int)VirtualKey.RETURN);
            submitted[SlotKey(currentSlot, currentWord)] = now + SubmitPendingMs;
            currentWord = "";
            nextActionAt = now + NextCharDelay();
        }
        catch (Exception ex)
        {
            LogError($"update failed: {ex}");
            currentWord = "";
        }
    }

    private int NextCharDelay() =>
        rng.Next(Settings.MinCharDelayMs, Math.Max(Settings.MinCharDelayMs, Settings.MaxCharDelayMs) + 1);

    private static string SlotKey(int slot, string word) => $"{slot}:{word}";

    private static List<Cast> ReadCasts()
    {
        var casts = new List<Cast>();
        var castAddon = GetVisibleAddon("KTGEnemyCastBar");
        for (var i = 0; castAddon != null && i < castAddon->UldManager.NodeListCount; i++)
        {
            var nameNode = GetSlotNode(castAddon, i, CastNameNodeIndex);
            var name = ReadText(nameNode);
            if (name != "")
            {
                int.TryParse(ReadText(GetSlotNode(castAddon, i, CastStacksNodeIndex)), out var stacks);
                casts.Add(new(i, name, stacks, NodeCenter(nameNode)));
            }
        }

        return casts;
    }

    private static List<Slot> ReadSlots(List<Cast> casts)
    {
        var result = new List<Slot>();
        var wordAddon = GetVisibleAddon("KTGEWord");
        if (wordAddon == null)
        {
            return result;
        }

        var player = Svc.Objects.LocalPlayer;
        var enemies = Svc.Objects.OfType<IBattleNpc>()
            .Where(o => o.IsTargetable && !o.IsDead && (byte)o.BattleNpcKind == 5)
            .Select(o => (Npc: o, Ok: Svc.GameGui.WorldToScreen(o.Position, out var sp), Screen: sp))
            .Where(e => e.Ok)
            .ToList();

        for (var i = 0; i < wordAddon->UldManager.NodeListCount; i++)
        {
            var wordNode = GetSlotNode(wordAddon, i, WordNodeIndex);
            var word = ReadText(wordNode);
            if (word == "")
            {
                continue;
            }

            // word slot N and cast bar slot N aren't the same enemy, pair by screen position
            var pos = NodeCenter(wordNode);
            var cast = casts.Select(c => (c, Px: Vector2.Distance(c.Pos, pos)))
                .Where(c => c.Px <= MaxCastPairPx)
                .OrderBy(c => c.Px)
                .FirstOrDefault();

            // ponytail: nearest projected enemy, mostly by x; swap for real slot->entity data if the KTG agent gets mapped
            var npc = enemies.OrderBy(e => Math.Abs(e.Screen.X - pos.X) + (0.25f * Math.Abs(e.Screen.Y - pos.Y))).FirstOrDefault().Npc;
            var dist = npc == null || player == null ? float.MaxValue : Vector3.Distance(npc.Position, player.Position);
            var castName = cast.c?.Name ?? "";
            var stacks = cast.c?.Stacks ?? 0;
            var boss = npc != null && npc.Name.TextValue != "";
            // tankbuster/barrier words are 160,248,116, normal ones white or 255,248,176
            var color = wordNode->GetAsAtkTextNode()->TextColor;
            var green = color.G > color.R + 60 && color.G > color.B + 60;
            var rank =
                boss && stacks > 0 ? 0 :
                green ? 1 :
                stacks > 0 ? 2 :
                SpecialCasts.Any(s => CastMatches(castName, s)) ? 3 :
                boss ? 5 : 4;
            result.Add(new(i, word, castName, stacks, cast.c == null ? -1 : cast.Px, npc, dist, rank));
        }

        return [.. result
            .OrderBy(s => s.Rank)
            .ThenBy(s => s.Rank == 2 ? s.Stacks : 0)
            .ThenBy(s => s.Distance)];
    }

    // substring match so "Sensory Jammer" also catches "Dynamic Sensory Jammer"
    private static bool CastMatches(string cast, string name) => cast.Contains(name, StringComparison.OrdinalIgnoreCase);

    private void UpdatePause(List<Cast> casts, long now)
    {
        foreach (var c in casts)
        {
            if (castSeenAt.TryAdd($"{c.Index}:{c.Name}", now))
            {
                Log($"cast start {c.Index}:{c.Name}");
            }
        }

        // the cast lengths in PauseCasts come from these lines
        foreach (var (key, since) in castSeenAt)
        {
            if (!casts.Any(c => $"{c.Index}:{c.Name}" == key))
            {
                Log($"cast end {key} after {now - since}ms");
                castSeenAt.Remove(key);
            }
        }

        // keep pushing the deadline while the cast is nearly done, then let it run out
        foreach (var c in casts)
        {
            var entry = PauseCasts.FirstOrDefault(e => CastMatches(c.Name, e.Name));
            if (entry.Name != null && entry.CastMs - (now - castSeenAt[$"{c.Index}:{c.Name}"]) <= PauseBeforeEndMs)
            {
                pausedBy = c.Name;
                pauseUntil = now + entry.AfterMs;
                break;
            }
        }

        if (now >= pauseUntil)
        {
            pausedBy = "";
        }
    }

    private void TrackResultWindow()
    {
        var result = GetVisibleAddon("KTGResult");
        if (result == null)
        {
            resultRecorded = false;
            return;
        }

        if (resultRecorded)
        {
            return;
        }

        resultRecorded = true;
        var mgp = RewardAmount(result, MgpItemId, MgpIcon);
        var mgc = RewardAmount(result, MgcItemId, MgcIcon);
        var coffers = RewardAmount(result, CofferItemId, CofferIcon);
        Log($"result: {mgp} MGP, {mgc} MGC, {coffers} coffers");
        StatsSessionClock.MarkKeyboundActive();
        C.UpdateStats(stats =>
        {
            stats.KeyboundGamesPlayed++;
            stats.KeyboundMGP += StatsBonusHelper.ApplyMgpBonus(mgp);
            stats.KeyboundMGC += mgc;
            stats.KeyboundCoffers += coffers;
        });
        C.Save();
    }

    // rewards sit in the atkvalues as (item id, amount, icon) triples after the player blocks, whose
    // count depends on the party, so look them up by id + icon rather than by index
    private static int RewardAmount(AtkUnitBase* result, int itemId, int icon)
    {
        for (var i = 0; i + 2 < result->AtkValuesCount; i++)
        {
            if (result->AtkValues[i].Int == itemId && result->AtkValues[i + 2].Int == icon)
            {
                return result->AtkValues[i + 1].Int;
            }
        }

        return 0;
    }

    private static Vector2 NodeCenter(AtkResNode* node) =>
        new(node->ScreenX + (node->Width * node->ScaleX / 2), node->ScreenY + (node->Height * node->ScaleY / 2));

    private static AtkUnitBase* GetVisibleAddon(string name) =>
        TryGetAddonByName<AtkUnitBase>(name, out var addon) && addon->IsVisible ? addon : null;

    private static AtkResNode* GetSlotNode(AtkUnitBase* addon, int slot, int child)
    {
        if (slot >= addon->UldManager.NodeListCount)
        {
            return null;
        }

        var node = addon->UldManager.NodeList[slot];
        if (node == null || (ushort)node->Type < 1000 || !node->IsVisible())
        {
            return null;
        }

        var comp = node->GetAsAtkComponentNode()->Component;
        return comp == null || child >= comp->UldManager.NodeListCount ? null : comp->UldManager.NodeList[child];
    }

    private static string ReadText(AtkResNode* node) =>
        node == null || node->Type != NodeType.Text || !node->IsVisible()
            ? ""
            : node->GetAsAtkTextNode()->NodeText.ToString().Trim();

    // SetText submits but the duty still kicks for inactivity (AntiAfkKick doesn't help), it counts real
    // keystrokes. SendMessage skips TranslateMessage, so send WM_CHAR by hand
    // TerraFX (what ECommons' WindowsKeypress uses) isn't referenced by Saucy, hence the P/Invoke
    private static void SendChar(char c)
    {
        if (gameWindow == 0)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            gameWindow = process.MainWindowHandle;
        }

        var vk = (nint)(VkKeyScanW(c) & 0xFF);
        SendMessageW(gameWindow, WmKeyDown, vk, 0);
        SendMessageW(gameWindow, WmChar, c, 0);
        SendMessageW(gameWindow, WmKeyUp, vk, 0);
    }

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmChar = 0x0102;
    private static nint gameWindow;

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    private void DrawOverlay()
    {
        try
        {
            if (GetVisibleAddon("KTGInput") == null)
            {
                return;
            }

            ImGuiHelpers.ForceNextWindowMainViewport();
            ImGui.SetNextWindowPos(new(35, 90), ImGuiCond.FirstUseEver);
            if (ImGui.Begin($"{Loc.T(ModuleDisplayNames.KeyboundBrawler)}###SaucyKeyboundBrawler",
                    ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav))
            {
                var auto = Settings.AutoType;
                if (ImGui.Checkbox(Loc.T("Auto type"), ref auto))
                {
                    Settings.AutoType = auto;
                    C.Save();
                }

                if (pausedBy != "")
                {
                    ImGui.TextColored(new Vector4(1, 0.3f, 0.3f, 1), Loc.T("PAUSED: {0} ({1:F1}s)", pausedBy, Math.Max(0, pauseUntil - Environment.TickCount64) / 1000f));
                }

                if (Settings.ShowDebug)
                {
                    ImGui.TextUnformatted($"typing: {textToType} ({typedChars}/{textToType.Length})");
                    foreach (var s in slots)
                    {
                        var enemy = s.Enemy == null ? "no match" : $"{s.Enemy.BaseId} {s.Enemy.Name.TextValue} {s.Distance:F1}y";
                        ImGui.TextUnformatted($"[{s.Index}] {TierNames[s.Rank]} \"{s.Word}\" cast=\"{s.Cast}\"@{s.CastPx:F0}px stacks={s.Stacks} -> {enemy}");
                    }
                }
            }

            ImGui.End();
        }
        catch (Exception ex)
        {
            LogError($"overlay failed: {ex}");
        }
    }

    public static void DrawSettings()
    {
        var enabled = C.IsModuleEnabled(ModuleNames.KeyboundBrawler);
        if (ImGui.Checkbox($"{Loc.T("Enable")}###SaucyKeyboundEnable", ref enabled))
        {
            C.SetModuleEnabled(ModuleNames.KeyboundBrawler, enabled);
            C.Save();
        }

        ImGui.TextWrapped(Loc.T("Types the words shown over enemies for you, with human-like delays. Priority: boss interrupts, barrier words, special casts, then the closest enemy. Stops typing during casts that punish it."));

        var changed = false;
        var min = Settings.MinCharDelayMs;
        var max = Settings.MaxCharDelayMs;
        var enter = Settings.EnterDelayMs;
        var debug = Settings.ShowDebug;
        if (ImGui.SliderInt($"{Loc.T("Min delay per character (ms)")}###SaucyKeyboundMin", ref min, 0, 500))
        {
            Settings.MinCharDelayMs = min;
            changed = true;
        }

        if (ImGui.SliderInt($"{Loc.T("Max delay per character (ms)")}###SaucyKeyboundMax", ref max, 0, 500))
        {
            Settings.MaxCharDelayMs = max;
            changed = true;
        }

        if (ImGui.SliderInt($"{Loc.T("Delay before Enter (ms)")}###SaucyKeyboundEnter", ref enter, 0, 1000))
        {
            Settings.EnterDelayMs = enter;
            changed = true;
        }

        if (ImGui.Checkbox($"{Loc.T("Show debug info")}###SaucyKeyboundDebug", ref debug))
        {
            Settings.ShowDebug = debug;
            changed = true;
        }

        if (changed)
        {
            C.Save();
        }
    }
}
