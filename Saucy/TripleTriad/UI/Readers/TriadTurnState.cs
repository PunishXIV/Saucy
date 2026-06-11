using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
namespace Saucy.TripleTriad.UI;

internal static class TriadTurnState
{
    public const int PlayerTurnAtkValueIndex = 23;

    private static readonly string[] TurnBannerMarkers =
    [
        " TURN",
        " TURN'",
        " TOUR ",
        " TOUR DE ",
        " ZUG",
        " AM ZUG",
        " TURNO",
        " TURNO DE ",
        " VEZ DE ",
        "のターン",
        "回合",
        "턴",
        " ход",
        " хід"
    ];

    public static unsafe bool ReadIsPlayerTurn(AtkUnitBase* unit)
    {
        if (unit == null || unit->AtkValuesCount <= PlayerTurnAtkValueIndex)
        {
            return false;
        }

        ref var value = ref unit->AtkValues[PlayerTurnAtkValueIndex];
        return value.Type == AtkValueType.Int && value.Int == 1;
    }

    public static bool IsBoardPickPhase(byte turnState) => turnState == (byte)TurnState.NormalMove;

    public static bool IsForcedCardPickPhase(byte turnState) => turnState == (byte)TurnState.MaskedMove;

    public static bool CanBlueAct(byte turnState, bool isPlayerTurn) =>
        IsForcedCardPickPhase(turnState) ||
        (IsBoardPickPhase(turnState) && isPlayerTurn);

    public static unsafe bool IsTurnBannerVisible(AtkUnitBase* unit) =>
        unit != null && HasTurnBannerText(unit->RootNode);

    private static unsafe bool HasTurnBannerText(AtkResNode* node)
    {
        if (node == null || !node->IsVisible())
        {
            return false;
        }

        if ((int)node->Type == (int)NodeType.Text && IsTurnBannerText(GUINodeUtils.GetNodeText(node)))
        {
            return true;
        }

        foreach (var child in GUINodeUtils.GetImmediateChildNodes(node) ?? [])
        {
            if (HasTurnBannerText(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTurnBannerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 96)
        {
            return false;
        }

        foreach (var marker in TurnBannerMarkers)
        {
            if (text.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
