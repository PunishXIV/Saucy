using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin;

public class UnsafeReaderTriadDeck
{
    public bool HasErrors { get; private set; }

    private delegate void SetSelectedCardDelegate(IntPtr addonPtr, int cellIdx);
    private readonly SetSelectedCardDelegate? SetSelectedCardFunc;

    private delegate void RefreshUIDelegate(IntPtr agentPtr);
    private readonly RefreshUIDelegate? RefreshUIFunc;

    public UnsafeReaderTriadDeck()
    {
        var SetSelectedCardPtr = IntPtr.Zero;
        var RefreshUIPtr = IntPtr.Zero;

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

        HasErrors = (SetSelectedCardPtr == IntPtr.Zero) || (RefreshUIPtr == IntPtr.Zero);
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

    public void SetSelectedCard(IntPtr addonPtr, int cellIdx)
    {
        if (SetSelectedCardFunc == null || cellIdx < 0 || cellIdx >= 30)
        {
            return;
        }

        SetSelectedCardFunc(addonPtr, cellIdx);
    }

    public void RefreshUI(IntPtr agentPtr)
    {
        RefreshUIFunc?.Invoke(agentPtr);
    }
}
