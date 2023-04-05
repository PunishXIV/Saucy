using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using MgAl2O4.Utils;
using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UIReaderCuffResults : IUIReader
    {
        private readonly GameGui gameGui;
        private UIStateCuffResults cachedState = new();
        public Action<UIStateCuffResults> OnUpdated;
        public Action<bool> OnResultsUIChanged;

        private bool needsNotify = false;

        public bool HasResultsUI => hasResultsUI;
        private bool hasResultsUI;

        public UIReaderCuffResults(GameGui gameGui)
        {
            this.gameGui = gameGui;
        }

        public string GetAddonName()
        {
            return "GoldSaucerReward";
        }

        public void OnAddonLost()
        {
            SetIsResultsUI(false);
        }

        public void OnAddonShown(IntPtr addonPtr)
        {
            needsNotify = true;
            cachedState = new();
        }

        public unsafe void OnAddonUpdate(IntPtr addonPtr)
        {
            var baseNode = (AtkUnitBase*)addonPtr;
            if (baseNode == null)
            {
                return;
            }

            if (needsNotify)
            {
                
                UpdateCachedState(baseNode);

                if (cachedState.numMGP >= 0)
                {
                    needsNotify = false;
                    OnUpdated?.Invoke(cachedState);
                }
            }
        }

        public void SetIsResultsUI(bool value)
        {
            if (hasResultsUI != value)
            {
                hasResultsUI = value;
                OnResultsUIChanged?.Invoke(value);
            }
        }

        private unsafe void UpdateCachedState(AtkUnitBase* baseNode)
        {
            var number = baseNode->UldManager.NodeList[4]->GetComponent()->UldManager.NodeList[2]->GetComponent()->UldManager.NodeList[1]->GetAsAtkTextNode();
            if (!int.TryParse(number->NodeText.ToString(), out cachedState.numMGP))
            {
                cachedState.numMGP = -1;
            }

            switch (cachedState.numMGP)
            {
                case 10:
                    cachedState.isBruising = true; break;
                case 15:
                    cachedState.isPunishing = true; break;
                case 25:
                    cachedState.isBrutal = true; break;
            }

        }
    }

    public class UIStateCuffResults
    {
        public int numMGP;
        public bool isBruising;
        public bool isPunishing;
        public bool isBrutal;
    }
}
