using Dalamud;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using FFTriadBuddy;
using ImGuiNET;
using System;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginWindowDeckEval : Window, IDisposable
    {
        private readonly Vector4 colorWin = new Vector4(0.2f, 0.9f, 0.2f, 1);
        private readonly Vector4 colorDraw = new Vector4(0.9f, 0.9f, 0.2f, 1);
        private readonly Vector4 colorLose = new Vector4(0.9f, 0.2f, 0.2f, 1);
        private readonly Vector4 colorTxt = new Vector4(1, 1, 1, 1);
        private readonly Vector4 colorGray = new Vector4(0.6f, 0.6f, 0.6f, 1);

        private readonly UIReaderTriadPrep uiReaderPrep;
        private readonly Solver solver;
        private readonly PluginWindowDeckOptimize optimizerWindow;
        private readonly PluginWindowNpcStats statsWindow;

        private string locEvaluating;
        private string locWinChance;
        private string locCantFind;
        private string locNoProfileDecks;
        private string locOptimize;
        private string locNpcStats;
        private string locStatusPvPMatch;

        public PluginWindowDeckEval(Solver solver, UIReaderTriadPrep uiReaderPrep, PluginWindowDeckOptimize optimizerWindow, PluginWindowNpcStats statsWindow) : base("Deck Eval")
        {
            this.solver = solver;
            this.uiReaderPrep = uiReaderPrep;
            this.optimizerWindow = optimizerWindow;
            this.statsWindow = statsWindow;

            uiReaderPrep.OnMatchRequestChanged += OnMatchRequestChanged;
            OnMatchRequestChanged(uiReaderPrep.HasMatchRequestUI);

            // doesn't matter will be updated on next draw
            PositionCondition = ImGuiCond.None;
            SizeCondition = ImGuiCond.None;
            RespectCloseHotkey = false;
            ForceMainWindow = true;

            Flags = ImGuiWindowFlags.NoDecoration |
                ImGuiWindowFlags.NoResize |
                ImGuiWindowFlags.NoSavedSettings |
                ImGuiWindowFlags.NoMove |
                // ImGuiWindowFlags.NoMouseInputs |
                ImGuiWindowFlags.NoDocking |
                ImGuiWindowFlags.NoFocusOnAppearing |
                ImGuiWindowFlags.NoNav;

            Plugin.CurrentLocManager.LocalizationChanged += (_) => CacheLocalization();
            CacheLocalization();
        }

        public void Dispose()
        {
            // meh
        }

        private void CacheLocalization()
        {
            locEvaluating = Localization.Localize("DE_Evaluating", "Evaluating decks...");
            locWinChance = Localization.Localize("DE_WinChance", "win {0:P0}");
            locCantFind = Localization.Localize("DE_Failed", "Err.. Can't find best deck :<");
            locNoProfileDecks = Localization.Localize("DE_NoProfileDecks", "Err.. No decks to evaluate");
            locOptimize = Localization.Localize("DE_Optimize", "Optimize deck");
            locNpcStats = Localization.Localize("NS_Title", "NPC stats");
            locStatusPvPMatch = Localization.Localize("ST_StatusPvP", "PvP match");
        }

        private void OnMatchRequestChanged(bool active)
        {
            bool canAccessProfileDecks = (solver.profileGS != null) && !solver.profileGS.HasErrors;
            IsOpen = active && canAccessProfileDecks;

            if (active)
            {
                GameCardDB.Get().Refresh();
            }
        }

        public override void PreDraw()
        {
            var btnSize = ImGuiHelpers.GetButtonSize("-");
            var reqHeight = btnSize.Y + (ImGui.GetStyle().WindowPadding.Y * 2);
            if (optimizerWindow.CanRunOptimizer())
            {
                var framePaddingY = ImGui.GetStyle().FramePadding.Y;

                reqHeight += btnSize.Y;
                reqHeight += framePaddingY;
            }

            Position = uiReaderPrep.cachedState.screenPos + new Vector2(0, uiReaderPrep.cachedState.screenSize.Y);
            Size = new Vector2(uiReaderPrep.cachedState.screenSize.X, reqHeight) / ImGuiHelpers.GlobalScale;
        }

        public override void Draw()
        {
            Vector4 hintColor = colorTxt;
            string hintText = "";

            if (solver.preGameNpc == null)
            {
                hintColor = colorDraw;
                hintText = locStatusPvPMatch;
            }
            else if (solver.preGameDecks.Count == 0)
            {
                // no profile decks created vs profile reader failed
                hintColor = solver.HasAllProfileDecksEmpty ? colorTxt : colorDraw;
                hintText = locNoProfileDecks;
            }
            else if (solver.preGameProgress < 1.0f)
            {
                hintColor = colorTxt;
                hintText = string.Format("{0} {1:P0}", locEvaluating, solver.preGameProgress); // no more: .Replace("%", "%%");
            }
            else
            {
                if (solver.preGameDecks.TryGetValue(solver.preGameBestId, out var bestDeckData))
                {
                    hintColor = GetChanceColor(bestDeckData.chance);
                    hintText = $"{bestDeckData.name} -- ";
                    hintText += string.Format(locWinChance, bestDeckData.chance.winChance); // no more: .Replace("%", "%%");
                }
                else
                {
                    hintColor = colorLose;
                    hintText = locCantFind;
                }
            }

            var hasNpc = solver.preGameNpc != null;
            var btnSize = ImGuiHelpers.GetButtonSize("-");
            var textSize = ImGui.CalcTextSize(hintText);
            var windowMin = ImGui.GetWindowContentRegionMin();
            var windowMax = ImGui.GetWindowContentRegionMax();
            var windowSize = windowMax - windowMin;
            var hintPosY = windowMin.Y + ((windowSize.Y - textSize.Y) * 0.5f);
            var hintPosX = windowMin.X + ((windowSize.X - textSize.X) * 0.5f);

            var btnStartX = windowSize.X - btnSize.X - (10 * ImGuiHelpers.GlobalScale);
            var btnStartY = windowMin.Y;
            var optimizeSize = ImGui.CalcTextSize(locOptimize);
            var optimizeStartX = btnStartX - optimizeSize.X - (5 * ImGuiHelpers.GlobalScale);

            var statsSize = ImGui.CalcTextSize(locNpcStats);
            var statsStartX = btnStartX - statsSize.X - (5 * ImGuiHelpers.GlobalScale);

            if (optimizerWindow.CanRunOptimizer() && hasNpc)
            {
                hintPosX = Math.Max(windowMin.X + (10 * ImGuiHelpers.GlobalScale), Math.Min(hintPosX, optimizeStartX - (20 * ImGuiHelpers.GlobalScale) - textSize.X));
            }

            // use TextUnformatted here, hint text contains user created string (deck name) and can blow up imgui (e.g. deadly '%' chars)
            ImGui.SetCursorPos(new Vector2(hintPosX, hintPosY));
            ImGui.PushStyleColor(ImGuiCol.Text, hintColor);
            ImGui.TextUnformatted(hintText);
            ImGui.PopStyleColor();

            if (hasNpc)
            {
                var framePaddingY = ImGui.GetStyle().FramePadding.Y;
                if (optimizerWindow.CanRunOptimizer())
                {
                    ImGui.SetCursorPos(new Vector2(optimizeStartX, btnStartY + framePaddingY));
                    ImGui.TextColored(colorGray, locOptimize);

                    ImGui.SetCursorPos(new Vector2(btnStartX, btnStartY));
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Search))
                    {
                        optimizerWindow.SetupAndOpen(solver.preGameNpc, solver.preGameMods);
                    }

                    btnStartY += btnSize.Y;
                    btnStartY += framePaddingY;
                }

                ImGui.SetCursorPos(new Vector2(statsStartX, btnStartY + framePaddingY));
                ImGui.TextColored(colorGray, locNpcStats);

                ImGui.SetCursorPos(new Vector2(btnStartX, btnStartY));
                if (ImGuiComponents.IconButton(FontAwesomeIcon.ChartLine))
                {
                    statsWindow.SetupAndOpen(solver.preGameNpc);
                }
            }
        }

        public Vector4 GetChanceColor(SolverResult chance)
        {
            return (chance.expectedResult == ETriadGameState.BlueWins) ? colorWin :
                (chance.expectedResult == ETriadGameState.BlueDraw) ? colorDraw :
                colorLose;
        }
    }
}
