using Dalamud;
using Dalamud.Data;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using ImGuiScene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginWindowStatus : Window, IDisposable
    {
        private readonly UIReaderTriadGame uiReaderGame;
        private readonly UIReaderTriadPrep uiReaderPrep;
        private readonly Solver solver;
        private readonly DataManager dataManager;
        private readonly Configuration config;

        public bool showConfigs = false;
        private bool showDebugDetails;
        private float orgDrawPosX;
        private Dictionary<int, TextureWrap> mapCardImages = new();
        private const float debugCellSize = 30.0f;
        private const float debugCellPading = 4.0f;

        private Vector4 colorErr = new Vector4(0.9f, 0.2f, 0.2f, 1);
        private Vector4 colorOk = new Vector4(0.2f, 0.9f, 0.2f, 1);
        private Vector4 colorYellow = new Vector4(0.9f, 0.9f, 0.2f, 1);
        private Vector4 colorInactive = new Vector4(0.5f, 0.5f, 0.5f, 1);

        private string locStatus;
        private string locStatusNotActive;
        private string locStatusPvPMatch;
        private string locStatusActive;
        private string locGameData;
        private string locGameDataError;
        private string locGameDataLog;
        private string locPrepNpc;
        private string locPrepRule;
        private string locGameNpc;
        private string locGameMove;
        private string locGameMoveDisabled;
        private string locBoardX0;
        private string locBoardX1;
        private string locBoardX2;
        private string locBoardY0;
        private string locBoardY1;
        private string locBoardY2;
        private string locBoardCenter;
        private string locDebugMode;
        private string locConfigSolverHints;
        private string locConfigOptimizerCPU;
        private string locConfigOptimizerCPUHint;

        public PluginWindowStatus(DataManager dataManager, Solver solver, UIReaderTriadGame uiReaderGame, UIReaderTriadPrep uiReaderPrep, Configuration config) : base("Triad Buddy")
        {
            this.dataManager = dataManager;
            this.solver = solver;
            this.uiReaderGame = uiReaderGame;
            this.uiReaderPrep = uiReaderPrep;
            this.config = config;

            IsOpen = false;

            SizeConstraints = new WindowSizeConstraints() { MinimumSize = new Vector2(350, 0), MaximumSize = new Vector2(700, 3000) };

            Flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar;

            Plugin.CurrentLocManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        public void Dispose()
        {
            foreach (var kvp in mapCardImages)
            {
                kvp.Value.Dispose();
            }
        }

        private void CacheLocalization()
        {
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
            locConfigOptimizerCPU = Localization.Localize("CFG_OptimizerParallelLoad", "CPU usage for Deck Optimizer");
            locConfigOptimizerCPUHint = Localization.Localize("CFG_OptimizerParallelLoadHint", "Controls number of logical processors used for calculations. Does not reduce load of individual CPUs!");
        }

        public override void OnOpen()
        {
            showDebugDetails = false;
        }

        public override void OnClose()
        {
            // release all images collected so far
            foreach (var kvp in mapCardImages)
            {
                kvp.Value.Dispose();
            }
            mapCardImages.Clear();
        }

        public override void Draw()
        {
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
            bool hasChanges = false;

            var showSolverHintsInGameCopy = config.ShowSolverHintsInGame;
            var deckOptimizerCPUCopy = (int)(100 * config.DeckOptimizerCPU);

            hasChanges = ImGui.Checkbox(locConfigSolverHints, ref showSolverHintsInGameCopy) || hasChanges;

            ImGui.Spacing();
            ImGui.Text(locConfigOptimizerCPU);
            ImGui.SameLine();
            ImGuiComponents.HelpMarker(locConfigOptimizerCPUHint);
            ImGui.SameLine();
            int maxProcessors = Math.Max(1, Environment.ProcessorCount);
            int numProcessors = Math.Max(1, (int)(maxProcessors * deckOptimizerCPUCopy * 0.01f));
            ImGui.TextColored(colorInactive, $"({numProcessors} / {maxProcessors})");

            hasChanges = ImGui.SliderInt("##optimizerCPU", ref deckOptimizerCPUCopy, 1, 100, "%d%%") || hasChanges;
            ImGui.SameLine();

            if (hasChanges)
            {
                config.ShowSolverHintsInGame = showSolverHintsInGameCopy;
                config.DeckOptimizerCPU = deckOptimizerCPUCopy * 0.01f;
                config.Save();
            }
        }

        private void DrawStatus()
        {
            ImGui.AlignTextToFramePadding();
            ImGui.Text(locStatus);
            ImGui.SameLine();

            bool isPvPMatch = (uiReaderGame.status == UIReaderTriadGame.Status.PvPMatch) || (solver.status == Solver.Status.FailedToParseNpc);
            var statusDesc =
                isPvPMatch ? locStatusPvPMatch :
                uiReaderGame.HasErrors ? uiReaderGame.status.ToString() :
                solver.HasErrors ? solver.status.ToString() :
                !uiReaderGame.IsVisible ? locStatusNotActive :
                locStatusActive;

            var statusColor =
                isPvPMatch ? colorYellow :
                uiReaderGame.HasErrors || solver.HasErrors ? colorErr :
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
            int numCards = TriadCardDB.Get().cards.Count;
            int numNpcs = TriadNpcDB.Get().npcs.Count;
            bool isGameDataMissing = numCards == 0 || numNpcs == 0;
            if (isGameDataMissing)
            {
                ImGui.TextColored(colorErr, locGameDataError);
            }
            else
            {
                ImGui.Text(string.Format(locGameDataLog, numCards, numNpcs));
            }

            ImGui.Separator();

            // context sensitive part
            if (uiReaderPrep.HasDeckSelectionUI || uiReaderPrep.HasMatchRequestUI)
            {
                var rulesDesc = "--";

                var npcDesc = (solver.preGameNpc != null) ? solver.preGameNpc.Name.GetLocalized() : uiReaderPrep.cachedState.npc;
                if (solver.preGameMods.Count > 0)
                {
                    rulesDesc = "";
                    foreach (var ruleOb in solver.preGameMods)
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
            else
            {
                ImGui.Text(locGameNpc);
                ImGui.SameLine();
                ImGui.TextColored(colorYellow, (solver.currentNpc != null) ? solver.currentNpc.Name.GetLocalized() : "--");

                ImGui.Text(locGameMove);
                ImGui.SameLine();

                if (isPvPMatch || isGameDataMissing || !config.ShowSolverHintsInGame)
                {
                    ImGui.TextColored(colorYellow, locGameMoveDisabled);
                }
                else if (solver.hasMove)
                {
                    var useColor =
                        (solver.moveWinChance.expectedResult == ETriadGameState.BlueWins) ? colorOk :
                        (solver.moveWinChance.expectedResult == ETriadGameState.BlueDraw) ? colorYellow :
                        colorErr;

                    string humanCard = (solver.moveCard != null) ? solver.moveCard.Name.GetLocalized() : "??";
                    int boardX = solver.moveBoardIdx % 3;
                    int boardY = solver.moveBoardIdx / 3;
                    string humanBoardX = boardX == 0 ? locBoardX0 : (boardX == 1) ? locBoardX1 : locBoardX2;
                    string humanBoardY = boardY == 0 ? locBoardY0 : (boardY == 1) ? locBoardY1 : locBoardY2;
                    string humanBoard = (solver.moveBoardIdx == 4) ? locBoardCenter : $"{humanBoardY}, {humanBoardX}";

                    // 1 based indexing for humans, disgusting
                    ImGui.TextColored(useColor, $"[{solver.moveCardIdx + 1}] {humanCard} => {humanBoard}");
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
            if (solver == null || solver.DebugScreenMemory == null)
            {
                return;
            }

            // rules
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text($"{FontAwesomeIcon.Book.ToIconString()}");
            ImGui.PopFont();
            ImGui.SameLine();

            string modDesc = "";
            foreach (var mod in solver.DebugScreenMemory.gameSolver.simulation.modifiers)
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
            ImGui.Text($"{solver.DebugScreenMemory.swappedBlueCardIdx}");

            // solver chances
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text($"{FontAwesomeIcon.Question.ToIconString()}");
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextUnformatted(solver.moveWinChance.ToString());

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
            for (int idxY = 0; idxY < 3; idxY++)
            {
                for (int idxX = 0; idxX < 3; idxX++)
                {
                    int cellId = idxX + (idxY * 3);
                    var boardCellOb = solver.DebugScreenMemory.gameState.board[cellId];
                    uint useColor = (boardCellOb == null) ? colorNope :
                        (boardCellOb.owner == ETriadCardOwner.Blue) ? colorBlue :
                        (boardCellOb.owner == ETriadCardOwner.Red) ? colorRed :
                        colorNope;

                    DrawPaddedCardHelper(ref pos, boardCellOb == null ? null : boardCellOb.card, useColor);
                }

                linePos[idxY] = pos;
                DrawPaddedNewline(ref pos);
            }

            // - red: unknown cards
            var redDeckInst = solver.DebugScreenMemory.deckRed;
            var (redKnownCards, redUnknownCards) = solver.GetScreenRedDeckDebug();

            pos = linePos[0] + new Vector2(redDeckOffsetX, 0);
            var posDeckStartX = pos.X;
            DrawPaddedIcon(ref pos, FontAwesomeIcon.Question, colorRed);
            for (int idx = 0; idx < 5; idx++)
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
            for (int idx = 0; idx < 5; idx++)
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
            if (solver.DebugScreenState != null)
            {
                for (int idx = 0; idx < 5; idx++)
                {
                    var cardOb = solver.DebugScreenState.redDeck[idx];
                    DrawPaddedCardHelper(ref pos, cardOb, cardOb != null && !cardOb.IsValid() ? colorHidden : colorRed);
                }
            }

            // - blue: known cards
            DrawPaddedNewline(ref pos);
            pos = new Vector2(posDeckStartX, pos.Y + 10);
            DrawPaddedIcon(ref pos, FontAwesomeIcon.Check, colorBlue);
            if (solver.DebugScreenMemory.deckBlue != null)
            {
                int forcedCardIdx = solver.DebugScreenMemory.gameState?.forcedCardIdx ?? -1;
                for (int idx = 0; idx < 5; idx++)
                {
                    var cardOb = solver.DebugScreenMemory.deckBlue.GetCard(idx);
                    DrawPaddedCardHelper(ref pos, cardOb, idx == forcedCardIdx ? colorForced : colorBlue);
                }
            }

            ImGui.Dummy(new Vector2(400, 180));
        }

        private void DrawPaddedCardHelper(ref Vector2 pos, TriadCard cardOb, uint cellColor)
        {
            const float halfThickness = debugCellPading / 2;
            float totalSize = debugCellSize + debugCellPading * 2;

            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRect(pos + new Vector2(halfThickness, halfThickness),
                pos + new Vector2(totalSize - halfThickness, totalSize - halfThickness),
                cellColor, 0.0f, ImDrawFlags.None, halfThickness * 2);

            if (cardOb != null && cardOb.IsValid())
            {
                var texture = GetCardTexture(cardOb.Id);
                drawList.AddImage(texture.ImGuiHandle,
                    pos + new Vector2(debugCellPading, debugCellPading),
                    pos + new Vector2(debugCellPading + debugCellSize, debugCellPading + debugCellSize));
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
            float totalSize = debugCellSize + debugCellPading * 2;

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

        private TextureWrap GetCardTexture(int cardId)
        {
            if (mapCardImages.TryGetValue(cardId, out var texWrap))
            {
                return texWrap;
            }

            uint iconId = TriadCardDB.GetCardIconTextureId(cardId);
            var newTexWrap = dataManager.GetImGuiTextureIcon(iconId);
            mapCardImages.Add(cardId, newTexWrap);

            return newTexWrap;
        }
    }
}
