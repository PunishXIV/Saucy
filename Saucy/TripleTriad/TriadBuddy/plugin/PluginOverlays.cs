using Dalamud.Interface;
using FFTriadBuddy;
using ImGuiNET;
using System.Numerics;

namespace TriadBuddyPlugin
{
    public class PluginOverlays
    {
        public const uint colorWin = 0xFF00FF00;
        public const uint colorDraw = 0xFF00D7FF;
        public const uint colorLose = 0xFF0000FF;
        public const uint colorBoard = 0xFFFF7C00;

        public readonly UIReaderTriadGame uiReaderGame;
        public readonly UIReaderTriadPrep uiReaderPrep;
        public readonly Solver solver;
        public readonly Configuration config;

        // overlay: game board
        private bool hasGameOverlay;
        private uint gameCardColor;
        private int gameCardIdx;
        private int gameBoardIdx;

        // overlay: deck selection
        private bool hasDeckSelection;

        public PluginOverlays(Solver solver, UIReaderTriadGame uiReaderGame, UIReaderTriadPrep uiReaderPrep, Configuration config)
        {
            this.solver = solver;
            this.uiReaderGame = uiReaderGame;
            this.uiReaderPrep = uiReaderPrep;
            this.config = config;

            solver.OnMoveChanged += OnSolverMove;
            uiReaderPrep.OnDeckSelectionChanged += (active) => hasDeckSelection = active;
        }

        public void OnSolverMove(bool foundMove)
        {
            hasGameOverlay = foundMove;
            if (foundMove)
            {
                gameBoardIdx = solver.moveBoardIdx;
                gameCardIdx = solver.moveCardIdx;
                gameCardColor = GetChanceColor(solver.moveWinChance);
            }
            else
            {
                gameBoardIdx = -1;
                gameCardIdx = -1;
            }
        }

        public void OnDraw()
        {
            if (hasGameOverlay)
            {
                DrawGameOverlay();
            }

            if (hasDeckSelection)
            {
                DrawDeckSelectionOverlay();
            }
        }

        private void DrawGameOverlay()
        {
            if (uiReaderGame == null || uiReaderGame.status != UIReaderTriadGame.Status.NoErrors)
            {
                hasGameOverlay = false;
                return;
            }

            if (config.ShowSolverHintsInGame)
            {
                var (deckCardPos, deckCardSize) = uiReaderGame.GetBlueCardPosAndSize(gameCardIdx);
                var (boardCardPos, boardCardSize) = uiReaderGame.GetBoardCardPosAndSize(gameBoardIdx);

                var drawCardPos = deckCardPos + ImGuiHelpers.MainViewport.Pos;
                var drawBoardPos = boardCardPos + ImGuiHelpers.MainViewport.Pos;

                var drawList = ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport);
                drawList.AddRect(drawCardPos, drawCardPos + deckCardSize, gameCardColor, 5.0f, ImDrawFlags.RoundCornersAll, 5.0f * ImGuiHelpers.GlobalScale);
                drawList.AddRect(drawBoardPos, drawBoardPos + boardCardSize, colorBoard, 5.0f, ImDrawFlags.RoundCornersAll, 5.0f * ImGuiHelpers.GlobalScale);
            }
        }

        private void DrawDeckSelectionOverlay()
        {
            if (uiReaderPrep == null || uiReaderPrep.cachedState == null)
            {
                hasDeckSelection = false;
                return;
            }

            var drawList = ImGui.GetForegroundDrawList(ImGuiHelpers.MainViewport);
            const int padding = 5;
            var hintTextOffset = new Vector2(padding, padding);

            for (int idx = 0; idx < uiReaderPrep.cachedState.decks.Count; idx++)
            {
                var deckState = uiReaderPrep.cachedState.decks[idx];
                if (solver.preGameDecks.TryGetValue(deckState.id, out var deckData))
                {
                    bool isSolverReady = deckData.chance.score > 0;
                    var hintText = !isSolverReady ? "..." : deckData.chance.winChance.ToString("P0");
                    uint hintColor = !isSolverReady ? 0xFFFFFFFF : GetChanceColor(deckData.chance);

                    var hintTextSize = ImGui.CalcTextSize(hintText);
                    var hintRectSize = hintTextSize;
                    hintRectSize.X += padding * 2;
                    hintRectSize.Y += padding * 2;

                    var hintPos = deckState.screenPos + ImGuiHelpers.MainViewport.Pos;
                    hintPos.X += padding;
                    hintPos.Y += (deckState.screenSize.Y - hintTextSize.Y) / 2;

                    drawList.AddRectFilled(hintPos, hintPos + hintRectSize, 0x80000000, 5.0f, ImDrawFlags.RoundCornersAll);
                    drawList.AddText(hintPos + hintTextOffset, hintColor, hintText);
                }
            }
        }

        public uint GetChanceColor(SolverResult chance)
        {
            return (chance.expectedResult == ETriadGameState.BlueWins) ? colorWin :
                (chance.expectedResult == ETriadGameState.BlueDraw) ? colorDraw :
                colorLose;
        }
    }
}
