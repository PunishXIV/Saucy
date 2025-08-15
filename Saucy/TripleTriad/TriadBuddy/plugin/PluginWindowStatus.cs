using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace TriadBuddyPlugin;

public class PluginWindowStatus : Window, IDisposable
{
    private readonly UIReaderTriadGame uiReaderGame;
    private readonly UIReaderTriadPrep uiReaderPrep;

    public bool showConfigs = false;
    private bool showDebugDetails;
    private float orgDrawPosX;
    private const float debugCellSize = 30.0f;
    private const float debugCellPading = 4.0f;

    private Vector4 colorErr = new(0.9f, 0.2f, 0.2f, 1);
    private Vector4 colorOk = new(0.2f, 0.9f, 0.2f, 1);
    private Vector4 colorYellow = new(0.9f, 0.9f, 0.2f, 1);
    private Vector4 colorInactive = new(0.5f, 0.5f, 0.5f, 1);

    private string? locStatus;
    private string? locStatusNotActive;
    private string? locStatusPvPMatch;
    private string? locStatusActive;
    private string? locGameData;
    private string? locGameDataError;
    private string? locGameDataLog;
    private string? locPrepNpc;
    private string? locPrepRule;
    private string? locGameNpc;
    private string? locGameMove;
    private string? locGameMoveDisabled;
    private string? locBoardX0;
    private string? locBoardX1;
    private string? locBoardX2;
    private string? locBoardY0;
    private string? locBoardY1;
    private string? locBoardY2;
    private string? locBoardCenter;
    private string? locDebugMode;
    private string? locConfigSolverHints;
    private string? locConfigDeckEditHighlights;
    private string? locConfigOptimizerCPU;
    private string? locConfigOptimizerCPUHint;
    private bool hasCachedLocStrings;

    public PluginWindowStatus(UIReaderTriadGame uiReaderGame, UIReaderTriadPrep uiReaderPrep) : base("Triad Buddy")
    {
        this.uiReaderGame = uiReaderGame;
        this.uiReaderPrep = uiReaderPrep;

        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints() { MinimumSize = new Vector2(350, 0), MaximumSize = new Vector2(700, 3000) };

        Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar;

        if (Plugin.CurrentLocManager != null)
        {
            Plugin.CurrentLocManager.LocalizationChanged += (_) => { hasCachedLocStrings = false; };
        }
    }

    public void Dispose()
    {
        // ...
    }

    private void UpdateLocalizationCache()
    {
        if (hasCachedLocStrings) { return; }
        hasCachedLocStrings = true;

        locStatus = Localization.Localize("ST_Status", "Status:");
        locStatusNotActive = Localization.Localize("ST_StatusNotActive", "Minigame not active");
        locStatusPvPMatch = Localization.Localize("ST_StatusPvP", "PvP match");
        locStatusActive = Localization.Localize("ST_StatusActive", "Active");
        locGameData = Localization.Localize("ST_GameData", "Game data:");
        locGameDataError = Localization.Localize("ST_GameDataError", "missing! solver disabled");
        locGameDataLog = Localization.Localize("ST_GameDataLog", "cards: {0}, npcs: {1}");
        locPrepNpc = Localization.Localize("ST_PrepNpc", "Prep.NPC:");
        locPrepRule = Localization.Localize("ST_PrepRules", "Prep.Rules:");
        locGameNpc = Localization.Localize("ST_GameNpc", "Solver.NPC:");
        locGameMove = Localization.Localize("ST_GameMove", "Solver.Move:");
        locGameMoveDisabled = Localization.Localize("ST_GameMoveDisabled", "disabled");
        locBoardX0 = Localization.Localize("ST_BoardXLeft", "left");
        locBoardX1 = Localization.Localize("ST_BoardXCenter", "center");
        locBoardX2 = Localization.Localize("ST_BoardXRight", "right");
        locBoardY0 = Localization.Localize("ST_BoardYTop", "top");
        locBoardY1 = Localization.Localize("ST_BoardYCenter", "center");
        locBoardY2 = Localization.Localize("ST_BoardYBottom", "bottom");
        locBoardCenter = Localization.Localize("ST_BoardXYCenter", "center");
        locDebugMode = Localization.Localize("ST_DebugMode", "Show debug details");
        locConfigSolverHints = Localization.Localize("CFG_GameToggleHints", "Show solver hints in game");
        locConfigDeckEditHighlights = Localization.Localize("CFG_DeckEditHighlights", "Show highlights in deck edit");
        locConfigOptimizerCPU = Localization.Localize("CFG_OptimizerParallelLoad", "CPU usage for Deck Optimizer");
        locConfigOptimizerCPUHint = Localization.Localize("CFG_OptimizerParallelLoadHint", "Controls number of logical processors used for calculations. Does not reduce load of individual CPUs!");
    }

    public override void OnOpen()
    {
        showDebugDetails = false;
    }

    public override void OnClose()
    {
    }

    public override void Draw()
    {
        UpdateLocalizationCache();

        if (showConfigs)
        {
            DrawConfiguration();
        }
        else
        {
            DrawStatus();
        }
    }

    private void DrawConfiguration()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(locStatus);
        ImGui.SameLine();

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Backward))
        {
            showConfigs = false;
        }

        ImGui.Separator();
        var hasChanges = false;

        var showSolverHintsInGameCopy = Service.pluginConfig.ShowSolverHintsInGame;
        var showDeckEditHighlightsCopy = Service.pluginConfig.ShowDeckEditHighlights;
        var deckOptimizerCPUCopy = (int)(100 * Service.pluginConfig.DeckOptimizerCPU);

        hasChanges = ImGui.Checkbox(locConfigSolverHints, ref showSolverHintsInGameCopy) || hasChanges;
        hasChanges = ImGui.Checkbox(locConfigDeckEditHighlights, ref showDeckEditHighlightsCopy) || hasChanges;

        ImGui.Spacing();
        ImGui.Text(locConfigOptimizerCPU);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(locConfigOptimizerCPUHint ?? "");
        ImGui.SameLine();
        var maxProcessors = Math.Max(1, Environment.ProcessorCount);
        var numProcessors = Math.Max(1, (int)(maxProcessors * deckOptimizerCPUCopy * 0.01f));
        ImGui.TextColored(colorInactive, $"({numProcessors} / {maxProcessors})");

        hasChanges = ImGui.SliderInt("##optimizerCPU", ref deckOptimizerCPUCopy, 1, 100, "%d%%") || hasChanges;
        ImGui.SameLine();

        if (hasChanges)
        {
            Service.pluginConfig.ShowSolverHintsInGame = showSolverHintsInGameCopy;
            Service.pluginConfig.ShowDeckEditHighlights = showDeckEditHighlightsCopy;
            Service.pluginConfig.DeckOptimizerCPU = deckOptimizerCPUCopy * 0.01f;
            Service.pluginConfig.Save();
        }
    }

    private void DrawStatus()
    {
        if (SolverUtils.solverGame == null)
        {
            return;
        }

        ImGui.AlignTextToFramePadding();
        ImGui.Text(locStatus);
        ImGui.SameLine();

        var isPvPMatch = (uiReaderGame.status == UIReaderTriadGame.Status.PvPMatch) || (SolverUtils.solverGame.status == SolverGame.Status.FailedToParseNpc);
        var statusDesc =
            isPvPMatch ? locStatusPvPMatch :
            uiReaderGame.HasErrors ? uiReaderGame.status.ToString() :
            SolverUtils.solverGame.HasErrors ? SolverUtils.solverGame.status.ToString() :
            !uiReaderGame.IsVisible ? locStatusNotActive :
            locStatusActive;

        var statusColor =
            isPvPMatch ? colorYellow :
            uiReaderGame.HasErrors || SolverUtils.solverGame.HasErrors ? colorErr :
            colorOk;

        var availRegionWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

        ImGui.TextColored(statusColor, statusDesc);
        ImGui.SameLine(availRegionWidth - (50 * ImGuiHelpers.GlobalScale));

        if (ImGuiComponents.IconButton(FontAwesomeIcon.Bug))
        {
            showDebugDetails = !showDebugDetails;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(locDebugMode);
        }

        ImGui.SameLine(availRegionWidth - (20 * ImGuiHelpers.GlobalScale));
        if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
        {
            showConfigs = true;
            showDebugDetails = false;
        }

        ImGui.Text(locGameData);
        ImGui.SameLine();
        var numCards = TriadCardDB.Get().cards.Count;
        var numNpcs = TriadNpcDB.Get().npcs.Count;
        var isGameDataMissing = numCards == 0 || numNpcs == 0;
        if (isGameDataMissing)
        {
            ImGui.TextColored(colorErr, locGameDataError);
        }
        else
        {
            ImGui.Text(string.Format(locGameDataLog ?? "", numCards, numNpcs));
        }

        ImGui.Separator();

        // context sensitive part
        if (uiReaderPrep.HasDeckSelectionUI || uiReaderPrep.HasMatchRequestUI)
        {
            if (SolverUtils.solverPreGameDecks != null)
            {
                var npcDesc = (SolverUtils.solverPreGameDecks.preGameNpc != null) ? SolverUtils.solverPreGameDecks.preGameNpc.Name.GetLocalized() : uiReaderPrep.cachedState.npc;
                string rulesDesc;
                if (SolverUtils.solverPreGameDecks.preGameMods.Count > 0)
                {
                    rulesDesc = "";
                    foreach (var ruleOb in SolverUtils.solverPreGameDecks.preGameMods)
                    {
                        if (rulesDesc.Length > 0) { rulesDesc += ", "; }
                        rulesDesc += ruleOb.GetLocalizedName();
                    }
                }
                else
                {
                    rulesDesc = string.Join(", ", uiReaderPrep.cachedState.rules);
                }

                ImGui.Text(locPrepNpc);
                ImGui.SameLine();
                ImGui.TextColored(colorYellow, npcDesc);

                ImGui.Text(locPrepRule);
                ImGui.SameLine();
                ImGui.TextColored(colorYellow, rulesDesc);
            }
        }
        else
        {
            ImGui.Text(locGameNpc);
            ImGui.SameLine();
            ImGui.TextColored(colorYellow, (SolverUtils.solverGame.currentNpc != null) ? SolverUtils.solverGame.currentNpc.Name.GetLocalized() : "--");

            ImGui.Text(locGameMove);
            ImGui.SameLine();

            if (isPvPMatch || isGameDataMissing || !Service.pluginConfig.ShowSolverHintsInGame)
            {
                ImGui.TextColored(colorYellow, locGameMoveDisabled);
            }
            else if (SolverUtils.solverGame.hasMove)
            {
                var useColor =
                    (SolverUtils.solverGame.moveWinChance.expectedResult == ETriadGameState.BlueWins) ? colorOk :
                    (SolverUtils.solverGame.moveWinChance.expectedResult == ETriadGameState.BlueDraw) ? colorYellow :
                    colorErr;

                var humanCard = (SolverUtils.solverGame.MoveCard != null) ? SolverUtils.solverGame.MoveCard.Name.GetLocalized() : "??";
                var boardX = SolverUtils.solverGame.moveBoardIdx % 3;
                var boardY = SolverUtils.solverGame.moveBoardIdx / 3;
                var humanBoardX = boardX == 0 ? locBoardX0 : (boardX == 1) ? locBoardX1 : locBoardX2;
                var humanBoardY = boardY == 0 ? locBoardY0 : (boardY == 1) ? locBoardY1 : locBoardY2;
                var humanBoard = (SolverUtils.solverGame.moveBoardIdx == 4) ? locBoardCenter : $"{humanBoardY}, {humanBoardX}";

                // 1 based indexing for humans, disgusting
                ImGui.TextColored(useColor, $"[{SolverUtils.solverGame.moveCardIdx + 1}] {humanCard} => {humanBoard}");
            }
            else
            {
                ImGui.TextColored(colorYellow, "--");
            }
        }

        if (showDebugDetails)
        {
            ImGui.Separator();
            ImGui.Spacing();

            DrawDebugDetails();
        }
    }

    private void DrawDebugDetails()
    {
        // mostly for debugging purposes, try avoiding localized texts
        // visualize current uistate 
        if (SolverUtils.solverGame == null || SolverUtils.solverGame.DebugScreenMemory == null)
        {
            return;
        }

        // rules
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text($"{FontAwesomeIcon.Book.ToIconString()}");
        ImGui.PopFont();
        ImGui.SameLine();

        var modDesc = "";
        foreach (var mod in SolverUtils.solverGame.DebugScreenMemory.gameSolver.simulation.modifiers)
        {
            if (modDesc.Length > 0) { modDesc += ", "; }
            modDesc += mod.GetLocalizedName();
        }
        ImGui.TextColored(colorYellow, modDesc);

        // swap card
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text($"{FontAwesomeIcon.ArrowsAltH.ToIconString()}");
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text($"{SolverUtils.solverGame.DebugScreenMemory.swappedBlueCardIdx}");

        // solver chances
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text($"{FontAwesomeIcon.Question.ToIconString()}");
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextUnformatted(SolverUtils.solverGame.moveWinChance.ToString());

        // cards
        const uint colorBlue = 0x80ff9400;
        const uint colorRed = 0x800000ff;
        const uint colorNope = 0x80ffffff;
        const uint colorHidden = 0x8000ffff;
        const uint colorForced = 0x80ffffff;

        var pos = ImGui.GetCursorScreenPos();
        pos.Y += 10;
        orgDrawPosX = pos.X;

        var linePos = new Vector2[3];
        var redDeckOffsetX = 50;

        // - board
        for (var idxY = 0; idxY < 3; idxY++)
        {
            for (var idxX = 0; idxX < 3; idxX++)
            {
                var cellId = idxX + (idxY * 3);
                var boardCellOb = SolverUtils.solverGame.DebugScreenMemory.gameState.board[cellId];
                var useColor = (boardCellOb == null) ? colorNope :
                    (boardCellOb.owner == ETriadCardOwner.Blue) ? colorBlue :
                    (boardCellOb.owner == ETriadCardOwner.Red) ? colorRed :
                    colorNope;

                DrawPaddedCardHelper(ref pos, boardCellOb?.card, useColor);
            }

            linePos[idxY] = pos;
            DrawPaddedNewline(ref pos);
        }

        // - red: unknown cards
        _ = SolverUtils.solverGame.DebugScreenMemory.deckRed;
        var (redKnownCards, redUnknownCards) = SolverUtils.solverGame.GetScreenRedDeckDebug();

        pos = linePos[0] + new Vector2(redDeckOffsetX, 0);
        var posDeckStartX = pos.X;
        DrawPaddedIcon(ref pos, FontAwesomeIcon.Question, colorRed);
        for (var idx = 0; idx < 5; idx++)
        {
            if (idx < redUnknownCards.Count)
            {
                var cardOb = redUnknownCards[idx];
                DrawPaddedCardHelper(ref pos, cardOb, cardOb != null && !cardOb.IsValid() ? colorHidden : colorRed);
            }
            else
            {
                DrawPaddedCardHelper(ref pos, null, colorRed);
            }
        }

        // - red: known cards
        pos = linePos[1] + new Vector2(redDeckOffsetX, 0);
        DrawPaddedIcon(ref pos, FontAwesomeIcon.Check, colorRed);
        for (var idx = 0; idx < 5; idx++)
        {
            if (idx < redKnownCards.Count)
            {
                var cardOb = redKnownCards[idx];
                DrawPaddedCardHelper(ref pos, cardOb, cardOb != null && !cardOb.IsValid() ? colorHidden : colorRed);
            }
            else
            {
                DrawPaddedCardHelper(ref pos, null, colorRed);
            }
        }

        // - red: ui state
        pos = linePos[2] + new Vector2(redDeckOffsetX, 0);
        DrawPaddedIcon(ref pos, FontAwesomeIcon.Eye, colorRed);
        if (SolverUtils.solverGame.DebugScreenState != null)
        {
            for (var idx = 0; idx < 5; idx++)
            {
                var cardOb = SolverUtils.solverGame.DebugScreenState.redDeck[idx];
                DrawPaddedCardHelper(ref pos, cardOb, cardOb != null && !cardOb.IsValid() ? colorHidden : colorRed);
            }
        }

        // - blue: known cards
        DrawPaddedNewline(ref pos);
        pos = new Vector2(posDeckStartX, pos.Y + 10);
        DrawPaddedIcon(ref pos, FontAwesomeIcon.Check, colorBlue);
        if (SolverUtils.solverGame.DebugScreenMemory.deckBlue != null)
        {
            var forcedCardIdx = SolverUtils.solverGame.DebugScreenMemory.gameState?.forcedCardIdx ?? -1;
            for (var idx = 0; idx < 5; idx++)
            {
                var cardOb = SolverUtils.solverGame.DebugScreenMemory.deckBlue.GetCard(idx);
                DrawPaddedCardHelper(ref pos, cardOb, idx == forcedCardIdx ? colorForced : colorBlue);
            }
        }

        ImGui.Dummy(new Vector2(400, 180));
    }

    private void DrawPaddedCardHelper(ref Vector2 pos, TriadCard? cardOb, uint cellColor)
    {
        const float halfThickness = debugCellPading / 2;
        var totalSize = debugCellSize + debugCellPading * 2;

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(pos + new Vector2(halfThickness, halfThickness),
            pos + new Vector2(totalSize - halfThickness, totalSize - halfThickness),
            cellColor, 0.0f, ImDrawFlags.None, halfThickness * 2);

        if (cardOb != null && cardOb.IsValid())
        {
            var texture = GetCardTexture(cardOb.Id);
            if (texture != null)
            {
                drawList.AddImage(texture.Handle,
                    pos + new Vector2(debugCellPading, debugCellPading),
                    pos + new Vector2(debugCellPading + debugCellSize, debugCellPading + debugCellSize));
            }
        }
        else
        {
            var textStr = cardOb != null ? "??" : "-";
            var textSize = ImGui.CalcTextSize(textStr);
            drawList.AddText(pos + new Vector2((totalSize - textSize.X) * 0.5f, (totalSize - textSize.Y) * 0.5f), 0xffffffff, textStr);
        }

        pos.X += totalSize;
    }

    private void DrawPaddedIcon(ref Vector2 pos, FontAwesomeIcon icon, uint color)
    {
        var totalSize = debugCellSize + debugCellPading * 2;

        ImGui.PushFont(UiBuilder.IconFont);
        var textStr = icon.ToIconString();
        var textSize = ImGui.CalcTextSize(textStr);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddText(pos + new Vector2((totalSize - textSize.X) * 0.5f, (totalSize - textSize.Y) * 0.5f), color, textStr);
        ImGui.PopFont();

        pos.X += totalSize;
    }

    private void DrawPaddedNewline(ref Vector2 pos)
    {
        pos.X = orgDrawPosX;
        pos.Y += debugCellSize + debugCellPading * 2;
    }

    private IDalamudTextureWrap? GetCardTexture(int cardId)
    {
        var iconId = TriadCardDB.GetCardTextureId(cardId);
        GameIconLookup iconLookup = new(iconId);

        var resource = Svc.Texture.GetFromGameIcon(iconLookup);
        if (resource != null)
        {
            var hasWrap = resource.TryGetWrap(out var resultOb, out _);
            if (hasWrap)
            {
                return resultOb;
            }
        }

        return null;
    }
}
