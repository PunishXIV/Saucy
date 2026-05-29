using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.CuffACur;
using System;
using System.Linq;
namespace Saucy;

public class UIReaderGamesResults(IGameGui gameGui) : IUIReader
{
    private readonly IGameGui gameGui = gameGui;
    private UIStateCuffResults cuffResults = new();
    private UIStateLimbResults limbResults = new();

    private bool needsNotify;
    public Action<UIStateCuffResults>? OnCuffUpdated;
    public Action<UIStateLimbResults>? OnLimbUpdated;

    public bool HasResultsUI { get; private set; }

    public string GetAddonName() => "GoldSaucerReward";

    public void OnAddonLost() => SetIsResultsUI(false);

    public void OnAddonShown(nint addonPtr)
    {
        needsNotify = true;
        cuffResults = new();
        limbResults = new();
    }

    public unsafe void OnAddonUpdate(nint addonPtr)
    {
        var baseNode = (AtkUnitBase*)addonPtr;
        if (baseNode == null)
        {
            return;
        }

        if (needsNotify)
        {
            UpdateCachedState(baseNode);

            if (cuffResults.numMGP >= 0)
            {
                needsNotify = false;
                OnCuffUpdated?.Invoke(cuffResults);
            }

            if (limbResults.numMGP >= 0)
            {
                needsNotify = false;
                OnLimbUpdated?.Invoke(limbResults);
            }
        }
    }

    public void SetIsResultsUI(bool value) => HasResultsUI = value;

    private unsafe void UpdateCachedState(AtkUnitBase* baseNode)
    {
        var number = baseNode->UldManager.NodeList[4]->GetComponent()->UldManager.NodeList[2]->GetComponent()->UldManager.NodeList[1]->GetAsAtkTextNode();

        if (CufModule.ModuleEnabled)
        {
            if (!int.TryParse(number->NodeText.ToString(), out cuffResults.numMGP))
            {
                cuffResults.numMGP = -1;
            }

            switch (cuffResults.numMGP)
            {
                case 10:
                    cuffResults.isBruising = true; break;
                case 15:
                    cuffResults.isPunishing = true; break;
                case 25:
                    cuffResults.isBrutal = true; break;
            }
        }

        if (P.LimbManager.Cfg.EnableLimb)
        {
            if (!int.TryParse(number->NodeText.ToString().Where(char.IsDigit).ToArray(), out limbResults.numMGP))
            {
                limbResults.numMGP = -1;
            }

        }
    }
}

public class UIStateCuffResults
{
    public bool isBruising;
    public bool isBrutal;
    public bool isPunishing;
    public int numMGP;
}

public class UIStateLimbResults
{
    public int numMGP;
}
