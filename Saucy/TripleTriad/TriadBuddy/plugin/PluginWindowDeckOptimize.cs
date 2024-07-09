using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Numerics;
using TriadBuddy;

namespace TriadBuddyPlugin
{
    public class PluginWindowDeckOptimize : Window, IDisposable
    {
        private readonly Vector4 colorSetupData = new Vector4(0.9f, 0.9f, 0.0f, 1);
        private readonly Vector4 colorResultData = new Vector4(0.0f, 0.9f, 0.9f, 1);

        private SolverDeckOptimize? solver;
        private UIReaderTriadDeckEdit uiReaderDeckEdit;

        public Action? OnConfigRequested;

        private TriadDeckOptimizer deckOptimizer;
        private List<TriadGameModifier> regionMods = new();
        private TriadNpc? npc;
        private string? regionModsDesc;

        private float optimizerProgress;
        private float optimizerElapsedTime;
        private float optimizerWinChance;
        private float pendingWinChance;
        private string? optimizerTimeRemainingDesc;
        private bool isOptimizerRunning;
        private int[]? pendingCardIds;
        private int[]? shownCardIds;
        private string[] shownCardTooltipsDeckEdit = new string[5];
        private string[] shownCardTooltipsCollection = new string[5];
        private float pendingCardsUpdateTimeRemaining;
        private float optimizerStatsTimeRemaining;

        private bool hasDeckSolverResult;
        private SolverResult deckWinChance;
        private TriadDeck? cachedSolverDeck;

        private bool canUseBestDeck;
        private TriadDeck? bestDeck;
        private SolverResult bestWinChance;

        private Vector2 cardImageSize = new(104, 128);
        private Vector2[] cardImagePos = new Vector2[5];
        private Vector2 cardImageBox = new(0.0f, 0.0f);

        private string? locNpc;
        private string? locRegionRules;
        private string? locWinChance;
        private string? locDeckScore;
        private string? locTimeRemaining;
        private string? locCollectionPage;
        private string? locDeckEditPage;
        private string? locOptimizeStart;
        private string? locOptimizeAbort;
        private string? locOptimizeGuess;
        private bool hasCachedLocStrings;

        public PluginWindowDeckOptimize(UIReaderTriadDeckEdit uiReaderDeckEdit) : base("Deck Optimizer")
        {
            this.solver = SolverUtils.solverDeckOptimize;
            this.uiReaderDeckEdit = uiReaderDeckEdit;

            deckOptimizer = (solver != null) ? solver.deckOptimizer : new TriadDeckOptimizer();
            deckOptimizer.OnFoundDeck += DeckOptimizer_OnFoundDeck;

            cardImagePos[0] = new Vector2(0, 0);
            cardImagePos[1] = new Vector2(cardImageSize.X + 5, 0);
            cardImagePos[2] = new Vector2((cardImageSize.X + 5) * 2, 0);
            cardImagePos[3] = new Vector2((cardImageSize.X + 5) / 2, cardImageSize.Y + 5);
            cardImagePos[4] = new Vector2((cardImageSize.X + 5) / 2 + cardImageSize.X + 5, cardImageSize.Y + 5);
            cardImageBox.X = cardImagePos[2].X + cardImageSize.X;
            cardImageBox.Y = cardImagePos[4].Y + cardImageSize.Y;

            SizeCondition = ImGuiCond.None;
            SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(cardImageSize.X * 4, (cardImageSize.Y * 2) + ImGui.GetTextLineHeight() * 8),
            };

            if (Plugin.CurrentLocManager != null)
            {
                Plugin.CurrentLocManager.LocalizationChanged += (langCode) => { hasCachedLocStrings = false; };
            }
        }

        private void DeckOptimizer_OnFoundDeck(TriadDeck deck, float estWinChance)
        {
            if (deck != null && deck.knownCards != null && deck.knownCards.Count == 5)
            {
                // buffer card changes to catch multiple fast swaps and reduce number of loaded images
                pendingWinChance = estWinChance;
                cachedSolverDeck = deck;

                pendingCardIds = new int[5];
                for (int idx = 0; idx < pendingCardIds.Length; idx++)
                {
                    pendingCardIds[idx] = deck.knownCards[idx].Id;
                }
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

            WindowName = Localization.Localize("DO_Title", "Deck Optimizer");
            locNpc = Localization.Localize("DO_Npc", "Npc:");
            locRegionRules = Localization.Localize("DO_RegionRules", "Region rules:");
            locWinChance = Localization.Localize("DO_WinChance", "Win chance:");
            locDeckScore = Localization.Localize("DO_DeckScore", "Deck score:");
            locTimeRemaining = Localization.Localize("DO_TimeRemaining", "Time remaining:");
            locCollectionPage = Localization.Localize("DO_CollectionPage", "Collection page: {0}");
            locDeckEditPage = Localization.Localize("DO_DeckBuilderPage", "Deck builder page: {0}");
            locOptimizeStart = Localization.Localize("DO_Start", "Optimize deck");
            locOptimizeAbort = Localization.Localize("DO_Abort", "Abort");
            locOptimizeGuess = Localization.Localize("DO_Guess", "Guess");
        }

        public bool CanRunOptimizer()
        {
            // needs access to list of currently owned cards, provided by UnsafeReaderTriadCard class
            // any errors there (outdated signatures) will disable deck optimizer
            return PlayerSettingsDB.Get().ownedCards.Count > 0;
        }

        public void SetupAndOpen(TriadNpc? npc, List<TriadGameModifier> gameRules)
        {
            string prevModDesc = regionModsDesc ?? "";
            var prevNpc = this.npc;

            regionMods.Clear();
            regionModsDesc = null;

            canUseBestDeck = false;
            bestDeck = null;
            bestWinChance = SolverResult.Zero;

            this.npc = npc;
            if (npc != null)
            {
                bool[] removedNpcMod = { false, false };
                foreach (var mod in gameRules)
                {
                    if (mod != null)
                    {
                        int npcModIdx = npc.Rules.FindIndex(x => x.GetLocalizationId() == mod.GetLocalizationId());
                        if (npcModIdx != -1 && !removedNpcMod[npcModIdx])
                        {
                            removedNpcMod[npcModIdx] = true;
                            continue;
                        }

                        TriadGameModifier? modOb = Activator.CreateInstance(mod.GetType()) as TriadGameModifier;
                        if (modOb != null)
                        {
                            regionMods.Add(modOb);

                            if (regionModsDesc != null) { regionModsDesc += ", "; }
                            regionModsDesc += mod.GetLocalizedName();
                        }
                    }
                }
            }

            bool setupChanged = (npc != prevNpc) || (regionModsDesc != prevModDesc);
            if (setupChanged)
            {
                optimizerWinChance = -1;
                hasDeckSolverResult = false;
                shownCardIds = null;
            }

            // opening on already running optimizer? sounds bad, make sure it's aborted
            if (isOptimizerRunning)
            {
                AbortOptimizer();
            }

            IsOpen = true;
            uiReaderDeckEdit?.OnDeckOptimizerVisible(IsOpen);
            uiReaderDeckEdit?.SetHighlightedCards(shownCardIds);
        }

        private IDalamudTextureWrap? GetCardTexture(int cardId)
        {
            uint iconId = TriadCardDB.GetCardTextureId(cardId);
            GameIconLookup iconLookup = new(iconId);

            var resource = Service.textureProvider.GetFromGameIcon(iconLookup);
            if (resource != null)
            {
                var hasWrap = resource.TryGetWrap(out IDalamudTextureWrap? resultOb, out Exception? ex);
                if (hasWrap)
                {
                    return resultOb;
                }
            }

            return null;
        }

        private IDalamudTextureWrap? GetCardBackgroundImage()
        {
            var resource = Service.textureProvider.GetFromGame("ui/uld/CardTripleTriad.tex");
            if (resource != null)
            {
                var hasWrap = resource.TryGetWrap(out IDalamudTextureWrap? resultOb, out Exception? ex);
                if (hasWrap)
                {
                    return resultOb;
                }
            }

            return null;
        }

        public override void Draw()
        {
            var cardBackgroundImage = GetCardBackgroundImage();
            if (cardBackgroundImage == null)
            {
                return;
            }

            UpdateLocalizationCache();
            UpdateTick();
            var orgPos = ImGui.GetCursorPos();

            // header
            ImGui.Text(locNpc);
            ImGui.SameLine();
            ImGui.TextColored(colorSetupData, npc != null ? npc.Name.GetLocalized() : "??");

            ImGui.Text(locRegionRules);
            ImGui.SameLine();
            ImGui.TextColored(colorSetupData, !string.IsNullOrEmpty(regionModsDesc) ? regionModsDesc : "--");

            // deck cards and their layouts
            var textSizeWinChance = ImGui.CalcTextSize(locWinChance);
            var textSizeTimeRemaining = ImGui.CalcTextSize(locTimeRemaining);
            var textWidthMax = Math.Max(textSizeTimeRemaining.X, textSizeWinChance.X) + 10;
            var imageBoxIndentX = cardImagePos[3].X;
            var imageBoxOffsetX = Math.Max(0, textWidthMax - imageBoxIndentX);

            var currentPos = ImGui.GetCursorPos();
            var availRegionWidth = ImGui.GetWindowContentRegionMax().X - ImGui.GetWindowContentRegionMin().X;

            ImGui.SetCursorPos(new Vector2(availRegionWidth - (20 * ImGuiHelpers.GlobalScale), orgPos.Y));
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog))
            {
                OnConfigRequested?.Invoke();
            }

            // card images are not scaled and neither is dummy filler!
            ImGui.Dummy(cardImageBox);
            ImGui.SameLine();
            ImGui.Dummy(new Vector2(75 * 2, 1));

            var centerOffset = new Vector2((availRegionWidth - cardImageBox.X) / 2, 10 * ImGuiHelpers.GlobalScale);
            var footerPosY = currentPos.Y + cardImageBox.Y + (20 * ImGuiHelpers.GlobalScale);

            for (int idx = 0; idx < cardImagePos.Length; idx++)
            {
                var drawPos = currentPos + cardImagePos[idx] + centerOffset;
                drawPos.X += Math.Max(0, imageBoxOffsetX - centerOffset.X);

                int cardId = (shownCardIds != null) ? shownCardIds[idx] : -1;
                DrawCard(drawPos, cardId, idx, cardBackgroundImage);
            }

            // stat block
            ImGui.SetCursorPos(new Vector2(currentPos.X, currentPos.Y + cardImagePos[3].Y + ImGui.GetTextLineHeight()));
            if (isOptimizerRunning || hasDeckSolverResult || optimizerWinChance >= 0.0f)
            {
                if (hasDeckSolverResult)
                {
                    ImGui.Text(locWinChance);
                    ImGui.TextColored(colorResultData, deckWinChance.winChance.ToString("P0").Replace("%", "%%"));

                    if (canUseBestDeck && !isOptimizerRunning)
                    {
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.RedoAlt))
                        {
                            if (bestDeck != null)
                            {
                                DeckOptimizer_OnFoundDeck(bestDeck, bestWinChance.winChance);
                                pendingCardsUpdateTimeRemaining = 0.0f;

                                deckWinChance = bestWinChance;
                                canUseBestDeck = false;
                            }
                        }

                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip(bestWinChance.winChance.ToString("P0").Replace("%", "%%"));
                        }
                    }
                }
                else
                {
                    // "win chance" - it's a score assigned by deck optimizer, that can be approximated as win chance %
                    ImGui.Text(locDeckScore);
                    ImGui.TextColored(colorResultData, optimizerWinChance < 0 ? "--" : (optimizerWinChance * 1000).ToString("0"));
                }
            }

            if (isOptimizerRunning)
            {
                ImGui.NewLine();
                ImGui.Text(locTimeRemaining);
                ImGui.TextColored(colorResultData, optimizerTimeRemainingDesc);

                var statBlockPosY = ImGui.GetCursorPosY();
                footerPosY = Math.Max(footerPosY, statBlockPosY);
            }

            // footer
            ImGui.SetCursorPos(new Vector2(currentPos.X, footerPosY));
            if (!isOptimizerRunning)
            {
                var textSize = ImGui.CalcTextSize(locOptimizeGuess);
                textSize.X += 75.0f * ImGuiHelpers.GlobalScale;
                if (ImGui.Button(locOptimizeGuess, new Vector2(textSize.X, 0)))
                {
                    OptimizerGuess();
                }

                ImGui.SameLine();

                if (ImGui.Button(locOptimizeStart, new Vector2(-1, 0)))
                {
                    StartOptimizer();
                }
            }
            else
            {
                ImGui.SetNextItemWidth(availRegionWidth * 0.75f);
                ImGui.ProgressBar(optimizerProgress, Vector2.Zero);
                ImGui.SameLine();

                if (ImGui.Button(locOptimizeAbort, new Vector2(-1, 0)))
                {
                    AbortOptimizer();
                }
            }
        }

        private void DrawCard(Vector2 drawPos, int cardId, int tooltipIdx, IDalamudTextureWrap cardBackgroundImage)
        {
            Vector2 cardBackgroundUV1 = new(1.0f, cardImageSize.Y / cardBackgroundImage.Height);

            ImGui.SetCursorPos(drawPos);
            ImGui.Image(cardBackgroundImage.ImGuiHandle, cardImageSize, Vector2.Zero, cardBackgroundUV1);

            var cardImage = (cardId >= 0) ? GetCardTexture(cardId) : null;
            if (cardImage != null)
            {
                ImGui.SetCursorPos(drawPos);
                var drawOffset = ImGui.GetCursorScreenPos();

                ImGui.Image(cardImage.ImGuiHandle, cardImageSize);

                if (ImGui.IsItemHovered())
                {
                    bool isDeckEditorOpen = uiReaderDeckEdit?.IsVisible ?? false;
                    ImGui.SetTooltip(isDeckEditorOpen ? shownCardTooltipsDeckEdit[tooltipIdx] : shownCardTooltipsCollection[tooltipIdx]);
                }

                var cardOb = TriadCardDB.Get().FindById(cardId);
                if (cardOb != null)
                {
                    var textU = cardOb.Sides[(int)ETriadGameSide.Up].ToString("X");
                    var textD = cardOb.Sides[(int)ETriadGameSide.Down].ToString("X");
                    var textL = cardOb.Sides[(int)ETriadGameSide.Left].ToString("X");
                    var textR = cardOb.Sides[(int)ETriadGameSide.Right].ToString("X");

                    var textSizeU = ImGui.CalcTextSize(textU);
                    var textSizeD = ImGui.CalcTextSize(textD);
                    var textSizeL = ImGui.CalcTextSize(textL);
                    var textSizeR = ImGui.CalcTextSize(textR);
                    var textSizePadding = ImGui.CalcTextSize("#");

                    var padding = 0 * ImGuiHelpers.GlobalScale;
                    var relativeDrawY = cardImageSize.Y * 3 / 4;
                    if (relativeDrawY + padding + textSizeD.Y > cardImageSize.Y)
                    {
                        relativeDrawY = cardImageSize.Y - textSizeD.Y - padding;
                    }

                    var useCenterX = drawPos.X + (cardImageSize.X * 0.5f);
                    var useCenterY = drawPos.Y + relativeDrawY;

                    drawOffset -= drawPos;
                    ImGui.GetWindowDrawList().AddRectFilled(
                        new Vector2(drawOffset.X + useCenterX - padding - textSizePadding.X * 2, drawOffset.Y + useCenterY - padding - textSizePadding.Y),
                        new Vector2(drawOffset.X + useCenterX + padding + textSizePadding.X * 2, drawOffset.Y + useCenterY + padding + textSizePadding.Y),
                        0x80000000);

                    ImGui.SetCursorPos(new Vector2(useCenterX - (textSizeU.X * 0.5f), useCenterY - padding - textSizeU.Y));
                    ImGui.Text(textU);

                    ImGui.SetCursorPos(new Vector2(useCenterX - (textSizeD.X * 0.5f), useCenterY + padding));
                    ImGui.Text(textD);

                    ImGui.SetCursorPos(new Vector2(useCenterX + padding + textSizePadding.X, useCenterY - (textSizeL.Y * 0.5f)));
                    ImGui.Text(textL);

                    ImGui.SetCursorPos(new Vector2(useCenterX - padding - textSizePadding.X - textSizeR.X, useCenterY - (textSizeR.Y * 0.5f)));
                    ImGui.Text(textR);
                }
            }
        }

        private void UpdateTick()
        {
            // found deck buffering, 0.5s cooldown for changes
            if (pendingCardIds != null)
            {
                if (pendingCardsUpdateTimeRemaining <= 0.0f)
                {
                    pendingCardsUpdateTimeRemaining = 0.5f;

                    shownCardIds = pendingCardIds;
                    pendingCardIds = null;

                    optimizerWinChance = pendingWinChance;
                    pendingWinChance = -1;

                    uiReaderDeckEdit?.SetHighlightedCards(shownCardIds);

                    for (int idx = 0; idx < shownCardIds.Length; idx++)
                    {
                        var cardOb = TriadCardDB.Get().FindById(shownCardIds[idx]);
                        if (cardOb != null)
                        {
                            var tooltip = $"[{CardUtils.GetOrderDesc(cardOb)}] {CardUtils.GetRarityDesc(cardOb)} {CardUtils.GetUIDesc(cardOb)}";
                            shownCardTooltipsCollection[idx] = tooltip;
                            shownCardTooltipsDeckEdit[idx] = tooltip;

                            var cardInfo = GameCardDB.Get().FindById(shownCardIds[idx]);
                            if (cardInfo != null)
                            {
                                int deckEditPageIdx = cardInfo.Collection[(int)GameCardCollectionFilter.DeckEditDefault].PageIndex;
                                shownCardTooltipsDeckEdit[idx] += "\n\n";
                                shownCardTooltipsDeckEdit[idx] += string.Format(locDeckEditPage ?? "", deckEditPageIdx + 1);

                                int collectionPageIdx = cardInfo.Collection[(int)GameCardCollectionFilter.All].PageIndex;
                                shownCardTooltipsCollection[idx] += "\n\n";
                                shownCardTooltipsCollection[idx] += string.Format(locCollectionPage ?? "", collectionPageIdx + 1);
                            }
                        }
                    }
                }
                else
                {
                    pendingCardsUpdateTimeRemaining -= ImGui.GetIO().DeltaTime;
                }
            }

            // stat update tick, refresh every 0.25s
            if (isOptimizerRunning)
            {
                optimizerElapsedTime += ImGui.GetIO().DeltaTime;
                if (optimizerStatsTimeRemaining <= 0.0f)
                {
                    optimizerStatsTimeRemaining = 0.25f;
                    optimizerProgress = deckOptimizer.GetProgress() / 100.0f;

                    int secondsRemaining = deckOptimizer.GetSecondsRemaining((int)(optimizerElapsedTime * 1000));
                    optimizerElapsedTime = 0;

                    var tspan = TimeSpan.FromSeconds(secondsRemaining);
                    if (tspan.Hours > 0 || tspan.Minutes > 55)
                    {
                        optimizerTimeRemainingDesc = string.Format("{0:D2}h:{1:D2}m:{2:D2}s", tspan.Hours, tspan.Minutes, tspan.Seconds);
                    }
                    else if (tspan.Minutes > 0 || tspan.Seconds > 55)
                    {
                        optimizerTimeRemainingDesc = string.Format("{0:D2}m:{1:D2}s", tspan.Minutes, tspan.Seconds);
                    }
                    else
                    {
                        optimizerTimeRemainingDesc = string.Format("{0:D2}s", tspan.Seconds);
                    }
                }
                else
                {
                    optimizerStatsTimeRemaining -= ImGui.GetIO().DeltaTime;
                }
            }
        }

        private async void StartOptimizer()
        {
            if (!CanRunOptimizer())
            {
                Service.logger.Error("Failed to start deck optimizer");
                return;
            }

            if (npc == null)
            {
                return;
            }

            // TODO: do i want to add UI selectors for locked cards? probably not.
            var lockedCards = new List<TriadCard?>();
            for (int idx = 0; idx < 5; idx++)
            {
                lockedCards.Add(null);
            }

            deckOptimizer.Initialize(npc, regionMods.ToArray(), lockedCards);
            deckOptimizer.parallelLoadPct = (Service.pluginConfig.DeckOptimizerCPU >= 1.0f) ? -1 : Service.pluginConfig.DeckOptimizerCPU;

            optimizerStatsTimeRemaining = 0;
            pendingCardsUpdateTimeRemaining = 0;
            optimizerProgress = 0;
            optimizerTimeRemainingDesc = "--";
            isOptimizerRunning = true;
            hasDeckSolverResult = false;

            await deckOptimizer.Process(npc, regionMods.ToArray(), lockedCards);

            isOptimizerRunning = false;
            optimizerTimeRemainingDesc = "--";

            RunDeckSolver();
        }

        private void AbortOptimizer()
        {
            deckOptimizer.SetPaused(false);
            deckOptimizer.AbortProcess();
        }

        private void RunDeckSolver()
        {
            hasDeckSolverResult = false;

            if (solver != null && cachedSolverDeck != null)
            {
                solver.SolveOptimizedDeck(cachedSolverDeck, npc, regionMods, (chance) =>
                {
                    // this is invoked from worker thread!
                    if (bestDeck == null || chance.IsBetterThan(bestWinChance))
                    {
                        bestDeck = new TriadDeck(cachedSolverDeck.knownCards);
                        bestWinChance = deckWinChance;
                    }

                    canUseBestDeck = bestWinChance.IsBetterThan(chance) && !cachedSolverDeck.Equals(bestDeck);
                    deckWinChance = chance;
                    hasDeckSolverResult = true;
                });
            }
        }

        private void OptimizerGuess()
        {
            if (!CanRunOptimizer() || npc == null)
            {
                Service.logger.Error("Failed to start deck optimizer");
                return;
            }

            // TODO: do i want to add UI selectors for locked cards? probably not.
            var lockedCards = new List<TriadCard>();
            for (int idx = 0; idx < 5; idx++)
            {
                lockedCards.Add(null);
            }

            deckOptimizer.Initialize(npc, regionMods.ToArray(), lockedCards);
            deckOptimizer.GuessDeck(lockedCards);

            DeckOptimizer_OnFoundDeck(deckOptimizer.optimizedDeck, 0.0f);
            RunDeckSolver();
        }

        public override void OnClose()
        {
            if (isOptimizerRunning)
            {
                deckOptimizer.AbortProcess();
            }

            uiReaderDeckEdit?.OnDeckOptimizerVisible(false);
        }
    }
}
