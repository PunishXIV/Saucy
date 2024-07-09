using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin
{
    public class UnsafeReaderTriadDeck
    {
        public bool HasErrors { get; private set; }

        private delegate void SetSelectedCardDelegate(IntPtr addonPtr, int cellIdx);
        private readonly SetSelectedCardDelegate? SetSelectedCardFunc;

        private delegate void RefreshUIDelegate(IntPtr agentPtr);
        private readonly RefreshUIDelegate? RefreshUIFunc;

        public UnsafeReaderTriadDeck()
        {
            IntPtr SetSelectedCardPtr = IntPtr.Zero;
            IntPtr RefreshUIPtr = IntPtr.Zero;

            if (Service.sigScanner != null)
            {
                try
                {
                    // SetDeckEditCell(void* addonPtr, int cellIdx)
                    //   +0xd88 = AddonTriadDeckEdit.CardIndex is part of signature

                    SetSelectedCardPtr = Service.sigScanner.ScanText("48 89 74 24 18 57 48 83 ec 20 48 63 f2 48 8b f9 89 b1 88 0d 00 00");

                    // Client::UI::Agent::AgentGoldSaucer.ReceiveEvent msg:6 -> FUN_140b973b0 msg:7
                    //  writes to agent +0x100 and calls refresh

                    RefreshUIPtr = Service.sigScanner.ScanText("e8 ?? ?? ?? ?? 84 c0 0f 94 c0 88 43 58");
                }
                catch (Exception ex)
                {
                    Service.logger.Error(ex, "oh noes!");
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
                Service.logger.Error("Failed to find triad deck functions, turning reader off");
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
            if (RefreshUIFunc != null)
            {
                RefreshUIFunc(agentPtr);
            }
        }
    }
}
