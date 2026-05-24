using System;
using System.Runtime.InteropServices;
namespace TriadBuddyPlugin;

public class UnsafeReaderTriadDeck
{
    private readonly RefreshUIDelegate? RefreshUIFunc;
    private readonly SetSelectedCardDelegate? SetSelectedCardFunc;

    public UnsafeReaderTriadDeck()
    {
        var SetSelectedCardPtr = nint.Zero;
        var RefreshUIPtr = nint.Zero;

        if (Svc.SigScanner != null)
        {
            try
            {
                SetSelectedCardPtr = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? BE ?? ?? ?? ?? 40 84 FF");

                // Client::UI::Agent::AgentGoldSaucer.ReceiveEvent msg:6 -> FUN_140b973b0 msg:7
                //  writes to agent +0x100 and calls refresh

                RefreshUIPtr = Svc.SigScanner.ScanText("e8 ?? ?? ?? ?? 84 c0 0f 94 c0 88 43 58");
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "oh noes!");
            }
        }

        HasErrors = (SetSelectedCardPtr == nint.Zero) || (RefreshUIPtr == nint.Zero);
        if (!HasErrors)
        {
            SetSelectedCardFunc = Marshal.GetDelegateForFunctionPointer<SetSelectedCardDelegate>(SetSelectedCardPtr);
            RefreshUIFunc = Marshal.GetDelegateForFunctionPointer<RefreshUIDelegate>(RefreshUIPtr);
        }
        else
        {
            Svc.Log.Error("Failed to find triad deck functions, turning reader off");
        }
    }
    public bool HasErrors { get; }

    public void SetSelectedCard(nint addonPtr, int cellIdx)
    {
        if (SetSelectedCardFunc == null || cellIdx < 0 || cellIdx >= 30)
        {
            return;
        }

        SetSelectedCardFunc(addonPtr, cellIdx);
    }

    public void RefreshUI(nint agentPtr) => RefreshUIFunc?.Invoke(agentPtr);

    private delegate void SetSelectedCardDelegate(nint addonPtr, int cellIdx);

    private delegate void RefreshUIDelegate(nint agentPtr);
}
