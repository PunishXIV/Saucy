using FFTriadBuddy;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UIReaderTriadCardList : IUIReader
    {
        [StructLayout(LayoutKind.Explicit, Size = 0x520)]               // it's around 0x550?
        private unsafe struct AddonTriadCardList
        {
            [FieldOffset(0x0)] public AtkUnitBase AtkUnitBase;
            [FieldOffset(0xe0)] public AtkCollisionNode* SelectedCardColisionNode;

            [FieldOffset(0x298)] public byte CardRarity;                // 1..5
            [FieldOffset(0x299)] public byte CardType;                  // 0: no type, 1: primal, 2: scion, 3: beastman, 4: garland
            [FieldOffset(0x29b)] public byte NumSideU;
            [FieldOffset(0x29c)] public byte NumSideD;
            [FieldOffset(0x29d)] public byte NumSideR;
            [FieldOffset(0x29e)] public byte NumSideL;
            [FieldOffset(0x2a0)] public int CardIconId;                 // texture id for button (82100+) or 0 when missing

            [FieldOffset(0x32c)] public byte FilterMode;                // 0xD = all, 0x3 = only owned, 0xC = only missing
            [FieldOffset(0x51c)] public byte PageIndex;                 // ignores writes
            [FieldOffset(0x524)] public byte CardIndex;                 // can be written to, yay!
        }

        [StructLayout(LayoutKind.Explicit, Size = 0x110)]               // it's around 0x200?
        public unsafe struct AgentTriadCardList
        {
            [FieldOffset(0x100)] public int PageIndex;                  // ignores writes
            [FieldOffset(0x108)] public int CardIndex;                  // ignores writes
            [FieldOffset(0x10c)] public byte FilterMode;                // 0 = all, 1 = only owned, 2 = only missing

            [FieldOffset(0x120)] public ushort FilterDeckTypeRarity;
            [FieldOffset(0x122)] public ushort FilterDeckSides;
            [FieldOffset(0x124)] public byte FilterDeckSorting;

            // 0x28 card data iterator start?
            // 0x30 card data iterator end
        }

        public enum Status
        {
            NoErrors,
            AddonNotFound,
            AddonNotVisible,
            NodesNotReady,
        }

        public UIStateTriadCardList cachedState = new();
        public Action<UIStateTriadCardList>? OnUIStateChanged;
        public Action<bool>? OnVisibilityChanged;

        public Status status = Status.AddonNotFound;
        public bool IsVisible => (status != Status.AddonNotFound) && (status != Status.AddonNotVisible);
        public bool HasErrors => false;

        private IntPtr cachedAddonAgentPtr;

        public string GetAddonName()
        {
            return "GSInfoCardList";
        }

        public void OnAddonLost()
        {
            // reset cached pointers when addon address changes
            cachedState.descNodeAddr = 0;
            cachedAddonAgentPtr = IntPtr.Zero;

            SetStatus(Status.AddonNotFound);
        }

        public void OnAddonShown(IntPtr addonPtr)
        {
            cachedAddonAgentPtr = (addonPtr != IntPtr.Zero) ? Service.gameGui.FindAgentInterface(addonPtr) : IntPtr.Zero;

            if (cachedAddonAgentPtr == IntPtr.Zero)
            {
                // failsafe, likely to break with patch
                cachedAddonAgentPtr = LoadFailsafeAgent();
#if DEBUG
                Service.logger.Info($"using agentPtr from failsafe: {(ulong)cachedAddonAgentPtr:X}");
#endif // DEBUG
            }
        }

        public unsafe void OnAddonUpdate(IntPtr addonPtr)
        {
            var addon = (AddonTriadCardList*)addonPtr;
            if (cachedState.descNodeAddr == 0)
            {
                if (!FindTextNodeAddresses(addon))
                {
                    SetStatus(Status.NodesNotReady);
                    return;
                }
            }

            var descNode = (AtkResNode*)cachedState.descNodeAddr;
            (cachedState.screenPos, cachedState.screenSize) = GUINodeUtils.GetNodePosAndSize(addon->AtkUnitBase.RootNode);
            (cachedState.descriptionPos, cachedState.descriptionSize) = GUINodeUtils.GetNodePosAndSize(descNode);

            byte newFilterMode =
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

        public static unsafe IntPtr LoadFailsafeAgent()
        {
            var uiModule = (UIModule*)Service.gameGui.GetUIModule();
            if (uiModule != null)
            {
                var agentModule = uiModule->GetAgentModule();
                if (agentModule != null)
                {
                    var agentPtr = agentModule->GetAgentByInternalId(FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentId.GoldSaucer);
                    if (agentPtr != null)
                    {
                        return new IntPtr(agentPtr);
                    }
                }
            }

            return IntPtr.Zero;
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
            IntPtr addonPtr = Service.gameGui.GetAddonByName(GetAddonName(), 1);
            OnAddonShown(addonPtr);

            if (addonPtr != IntPtr.Zero && cachedAddonAgentPtr != IntPtr.Zero)
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
                bool wasVisible = IsVisible;
                status = newStatus;

                if (HasErrors)
                {
                    Service.logger.Error("CardList reader error: " + newStatus);
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

            return (cachedState.descNodeAddr == 0);
        }
    }

    public class UIStateTriadCardList
    {
        public Vector2 screenPos;
        public Vector2 screenSize;
        public Vector2 descriptionPos;
        public Vector2 descriptionSize;
        public ulong descNodeAddr;

        public byte numU;
        public byte numL;
        public byte numD;
        public byte numR;
        public byte rarity;
        public byte type;
        public int iconId;

        public byte pageIndex;
        public byte cardIndex;
        public byte filterMode;

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
}
