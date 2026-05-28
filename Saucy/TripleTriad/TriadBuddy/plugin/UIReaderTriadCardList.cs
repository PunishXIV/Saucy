using FFTriadBuddy;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
namespace TriadBuddyPlugin;

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
        // reset cached pointers when addon address changes
        cachedState.descNodeAddr = 0;
        cachedAddonAgentPtr = nint.Zero;

        SetStatus(Status.AddonNotFound);
    }

    public unsafe void OnAddonShown(nint addonPtr)
    {
        cachedAddonAgentPtr = (addonPtr != nint.Zero) ? Svc.GameGui.FindAgentInterface(addonPtr) : nint.Zero;

        if (cachedAddonAgentPtr == nint.Zero)
        {
            // failsafe, likely to break with patch
            cachedAddonAgentPtr = LoadFailsafeAgent();
#if DEBUG
            Svc.Log.Info($"using agentPtr from failsafe: {(ulong)cachedAddonAgentPtr:X}");
#endif // DEBUG
        }

        if (addonPtr != nint.Zero)
        {
            var addon = (AddonTriadCardList*)addonPtr;
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
        var addon = (AddonTriadCardList*)addonPtr;
        (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);

        if (cachedState.descNodeAddr == 0)
        {
            if (!FindTextNodeAddresses(addon))
            {
                SetStatus(Status.NodesNotReady);
                return;
            }
        }

        var descNode = (AtkResNode*)cachedState.descNodeAddr;
        (cachedState.descriptionPos, cachedState.descriptionSize) = GUINodeUtils.GetNodePosAndSize(descNode);

        var newFilterMode =
            (addon->FilterMode == 0x7) ? (byte)1 :
            (addon->FilterMode == 0xA) ? (byte)2 :
            (byte)0;

        if (cachedState.pageIndex != addon->PageIndex ||
            cachedState.cardIndex != addon->CardIndex ||
            cachedState.filterMode != newFilterMode ||
            cachedState.numU != addon->NumSideU)
        {
            cachedState.numU = addon->NumSideU;
            cachedState.numL = addon->NumSideL;
            cachedState.numD = addon->NumSideD;
            cachedState.numR = addon->NumSideR;
            cachedState.rarity = addon->CardRarity;
            cachedState.type = addon->CardType;
            cachedState.iconId = addon->CardIconId;
            cachedState.pageIndex = addon->PageIndex;
            cachedState.cardIndex = addon->CardIndex;
            cachedState.filterMode = newFilterMode;

            OnUIStateChanged?.Invoke(cachedState);
        }

        SetStatus(Status.NoErrors);
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
        // doesn't really belong to a ui "reader", but won't be making a class just for calling one function
        // (there is no UnsafeReaderTriadCards, it's a um.. just a trick of light)

        // basic sanity checks on values before writing them to memory
        // this will NOT be enough when filters are active!
        if (pageIndex < 0 || pageIndex >= GameCardDB.MaxGridPages || cellIndex < 0 || cellIndex >= GameCardDB.MaxGridCells)
        {
            return false;
        }

        // refresh cached pointers before using them
        nint addonPtr = Svc.GameGui.GetAddonByName(GetAddonName());
        OnAddonShown(addonPtr);

        if (addonPtr != nint.Zero && cachedAddonAgentPtr != nint.Zero)
        {
            var addon = (AddonTriadCardList*)addonPtr;
            var addonAgent = (AgentTriadCardList*)cachedAddonAgentPtr;

            addonAgent->PageIndex = pageIndex;
            addon->CardIndex = (byte)cellIndex;
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

    private unsafe bool FindTextNodeAddresses(AddonTriadCardList* addon)
    {
        // 9 child nodes (sibling scan)
        //     [1] aqcuire section, simple node, 5 children
        //         [2] text
        //     [2] description section, simple node, 4 children
        //         [0] text

        var nodeArrL0 = GUINodeUtils.GetImmediateChildNodes(addon->AtkUnitBase.RootNode);
        var nodeDescripion = GUINodeUtils.PickNode(nodeArrL0, 2, 9);
        cachedState.descNodeAddr = (ulong)GUINodeUtils.GetChildNode(nodeDescripion);

        return cachedState.descNodeAddr != 0;
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x520)] // it's around 0x550?
    private unsafe struct AddonTriadCardList
    {
        [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;
        [FieldOffset(0xe0)] public AtkCollisionNode* SelectedCardColisionNode;

        [FieldOffset(0x2a0)] public byte CardRarity; // 1..5
        [FieldOffset(0x2a1)] public byte CardType; // 0: no type, 1: primal, 2: scion, 3: beastman, 4: garland
        [FieldOffset(0x2a3)] public byte NumSideU;
        [FieldOffset(0x2a4)] public byte NumSideD;
        [FieldOffset(0x2a5)] public byte NumSideR;
        [FieldOffset(0x2a6)] public byte NumSideL;
        [FieldOffset(0x2a8)] public int CardIconId; // texture id for button (82100+) or 0 when missing

        [FieldOffset(0x334)] public byte FilterMode; // 0xD = all, 0x3 = only owned, 0xC = only missing
        [FieldOffset(0x52c)] public byte PageIndex; // ignores writes
        [FieldOffset(0x534)] public byte CardIndex; // can be written to, yay!
    }

    [StructLayout(LayoutKind.Explicit, Size = 0x110)] // it's around 0x200?
    public struct AgentTriadCardList
    {
        [FieldOffset(0x100)] public int PageIndex; // can be written to
        [FieldOffset(0x108)] public int CardIndex; // ignores writes
        [FieldOffset(0x10c)] public byte FilterMode; // 0 = all, 1 = only owned, 2 = only missing

        [FieldOffset(0x120)] public ushort FilterDeckTypeRarity;
        [FieldOffset(0x122)] public ushort FilterDeckSides;
        [FieldOffset(0x124)] public byte FilterDeckSorting;

        // 0x28 card data iterator start?
        // 0x30 card data iterator end
    }
}

public class UIStateTriadCardList
{
    public byte cardIndex;
    public ulong descNodeAddr;
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
        var matchOb = ctx.ParseCard(numU, numL, numD, numR, (ETriadCardType)type, (ETriadCardRarity)rarity, false);
        if (matchOb == null || matchOb.SameNumberId >= 0)
        {
            // number match is increasing unreliable, use grid location instead
            return ctx.ParseCardByGridLocation(pageIndex, cardIndex, filterMode);
        }

        return matchOb;
    }
}
