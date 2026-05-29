using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System;
using System.Numerics;
namespace Saucy.TripleTriad.UI;

public class UIReaderTriadCardList : IUIReader
{
    public enum Status
    {
        NoErrors,
        AddonNotFound,
        AddonNotVisible,
        NodesNotReady
    }

    private nint cachedAddonAgentPtr;

    public UIStateTriadCardList cachedState = new();
    public Action<UIStateTriadCardList>? OnUIStateChanged;
    public Action<bool>? OnVisibilityChanged;

    public Status status = Status.AddonNotFound;
    public bool IsVisible => status is not Status.AddonNotFound and not Status.AddonNotVisible;
    public bool HasErrors => false;

    public string GetAddonName() => "GSInfoCardList";

    public void OnAddonLost()
    {
        cachedAddonAgentPtr = nint.Zero;
        SetStatus(Status.AddonNotFound);
    }

    public unsafe void OnAddonShown(nint addonPtr)
    {
        cachedAddonAgentPtr = (addonPtr != nint.Zero) ? Svc.GameGui.FindAgentInterface(addonPtr) : nint.Zero;

        if (cachedAddonAgentPtr == nint.Zero)
        {
            cachedAddonAgentPtr = LoadFailsafeAgent();
#if DEBUG
            Svc.Log.Info($"using agentPtr from failsafe: {(ulong)cachedAddonAgentPtr:X}");
#endif // DEBUG
        }

        if (addonPtr != nint.Zero)
        {
            var addon = (AddonGSInfoCardList*)addonPtr;
            if (addon->AtkUnitBase.RootNode != null)
            {
                (cachedState.screenPos, cachedState.screenSize) =
                    GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);
            }

            SetStatus(Status.NodesNotReady);
        }
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var addon = (AddonGSInfoCardList*)addonPtr;
        (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);

        var descNode = addon->SelectedCardDescription;
        if (descNode == null)
        {
            SetStatus(Status.NodesNotReady);
            return;
        }

        (cachedState.descriptionPos, cachedState.descriptionSize) =
            GUINodeUtils.GetNodePosAndSize(&descNode->AtkResNode);

        var newPageIndex = (byte)addon->SelectedPage;
        var newCardIndex = (byte)addon->SelectedCardIndex;
        var newFilterMode = (byte)CardListFilterMode.All;

        if (cachedAddonAgentPtr != nint.Zero)
        {
            var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;
            if (agent->EditDeckSelectedPage >= 0 && agent->EditDeckSelectedPage < GameCardDB.MaxGridPages)
            {
                newPageIndex = (byte)agent->EditDeckSelectedPage;
            }

            newFilterMode = (byte)agent->CardListFilterMode;
        }

        if (cachedState.pageIndex != newPageIndex ||
            cachedState.cardIndex != newCardIndex ||
            cachedState.filterMode != newFilterMode ||
            cachedState.numU != addon->NumSideU)
        {
            cachedState.numU = addon->NumSideU;
            cachedState.numL = addon->NumSideL;
            cachedState.numD = addon->NumSideD;
            cachedState.numR = addon->NumSideR;
            cachedState.rarity = addon->CardRarity;
            cachedState.type = (byte)addon->CardType;
            cachedState.iconId = addon->CardIconId;
            cachedState.pageIndex = newPageIndex;
            cachedState.cardIndex = newCardIndex;
            cachedState.filterMode = newFilterMode;

            OnUIStateChanged?.Invoke(cachedState);
        }

        SetStatus(Status.NoErrors);
    }

    private static nint ResolveAddonPtr()
    {
        for (var i = 0; i < 8; i++)
        {
            var handle = Svc.GameGui.GetAddonByName("GSInfoCardList", i);
            if (handle.Address != nint.Zero)
            {
                return handle.Address;
            }
        }

        return nint.Zero;
    }

    public static unsafe nint LoadFailsafeAgent()
    {
        var uiModule = (UIModule*)Svc.GameGui.GetUIModule().Address;
        if (uiModule != null)
        {
            var agentModule = uiModule->GetAgentModule();
            if (agentModule != null)
            {
                var agentPtr = agentModule->GetAgentByInternalId(AgentId.GoldSaucer);
                if (agentPtr != null)
                {
                    return new(agentPtr);
                }
            }
        }

        return nint.Zero;
    }

    public unsafe bool SetPageAndGridView(int pageIndex, int cellIndex)
    {
        if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
        {
            return false;
        }

        var addonPtr = ResolveAddonPtr();
        OnAddonShown(addonPtr);

        if (addonPtr != nint.Zero && cachedAddonAgentPtr != nint.Zero)
        {
            var addon = (AddonGSInfoCardList*)addonPtr;
            var agent = (AgentGoldSaucer*)cachedAddonAgentPtr;

            agent->EditDeckSelectedPage = pageIndex;
            addon->SelectedCardIndex = cellIndex;
            return true;
        }

        return false;
    }

    private void SetStatus(Status newStatus)
    {
        if (status != newStatus)
        {
            var wasVisible = IsVisible;
            status = newStatus;

            if (HasErrors)
            {
                Svc.Log.Error("CardList reader error: " + newStatus);
            }

            if (wasVisible != IsVisible)
            {
                OnVisibilityChanged?.Invoke(IsVisible);
            }
        }
    }
}

public class UIStateTriadCardList
{
    public byte cardIndex;
    public Vector2 descriptionPos;
    public Vector2 descriptionSize;
    public byte filterMode;
    public int iconId;
    public byte numD;
    public byte numL;
    public byte numR;

    public byte numU;

    public byte pageIndex;
    public byte rarity;
    public Vector2 screenPos;
    public Vector2 screenSize;
    public byte type;

    public TriadCard? ToTriadCard(GameUIParser ctx)
    {
        if (iconId >= 88000)
        {
            return ctx.cards.FindById(iconId - 88000);
        }

        if (iconId == 0 && numU == 0 && numL == 0 && numD == 0 && numR == 0)
        {
            return null;
        }

        var matchOb = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
        if (matchOb != null && matchOb.SameNumberId < 0)
        {
            return matchOb;
        }

        var gridMatch = ctx.ParseCardByGridLocation(pageIndex, cardIndex, filterMode, false);
        if (gridMatch != null)
        {
            return gridMatch;
        }

        return matchOb;
    }
}
