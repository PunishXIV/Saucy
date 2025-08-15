using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Runtime.InteropServices;

namespace TriadBuddyPlugin;

public class UnsafeReaderTriadCards
{
    public bool HasErrors { get; private set; }

    private delegate byte IsNpcBeatenDelegate(IntPtr uiState, int triadNpcId);

    private readonly IsNpcBeatenDelegate? IsNpcBeatenFunc;
    private readonly IntPtr UIStatePtr;

    public UnsafeReaderTriadCards()
    {
        var IsNpcBeatenPtr = IntPtr.Zero;

        if (Svc.SigScanner != null)
        {
            try
            {
                // IsTriadNpcCompleted(void* uiState, int triadNpcId)
                //   identified by pretty unique rowId from TripleTriad sheet: 0x230002
                //   looking for negative of that number (0xFFDCFFFE) gives pretty much only npc access functions (set + get)

                IsNpcBeatenPtr = Svc.SigScanner.ScanText("E8 ?? ?? ?? ?? 84 C0 0F 94 C0 88 43 58 45 33 FF");

                // UIState addr, use LEA opcode before calling IsTriadCardOwned, same function as described above
                UIStatePtr = Svc.SigScanner.GetStaticAddressFromSig("E8 ?? ?? ?? ?? EB 35 8B FD");
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, "oh noes!");
            }
        }

        HasErrors = (IsNpcBeatenPtr == IntPtr.Zero) || (UIStatePtr == IntPtr.Zero);
        if (!HasErrors)
        {
            IsNpcBeatenFunc = Marshal.GetDelegateForFunctionPointer<IsNpcBeatenDelegate>(IsNpcBeatenPtr);
        }
        else
        {
            Svc.Log.Error("Failed to find triad card functions, turning reader off");
        }
    }

    public unsafe bool IsCardOwned(int cardId)
    {
        if (HasErrors || cardId <= 0 || cardId > 65535)
        {
            return false;
        }

        return UIState.Instance()->IsTripleTriadCardUnlocked((ushort)cardId);
    }

    public bool IsNpcBeaten(int npcId)
    {
        if (HasErrors || npcId < 0x230002)
        {
            return false;
        }

        return (IsNpcBeatenFunc != null) && IsNpcBeatenFunc(UIStatePtr, npcId) != 0;
    }

    /*public void TestBeatenNpcs()
    {
        // fixed addr from 5.58
        IntPtr memAddr = UIStatePtr + 0x15d18;

        byte[] flags = Dalamud.Memory.MemoryHelper.ReadRaw(memAddr, 0x70 / 8);
        flags[10 / 8] |= 1 << (10 % 8);
        flags[11 / 8] |= 1 << (11 % 8);
        flags[12 / 8] |= 1 << (12 % 8);

        Dalamud.Memory.MemoryHelper.WriteRaw(memAddr, flags);
    }*/

    /*public void TestOwnedCardBits()
    {
        // fixed addr from 5.58
        IntPtr memAddr = UIStatePtr + 0x15ce5;

        byte[] flags = Dalamud.Memory.MemoryHelper.ReadRaw(memAddr, 0x29);
        flags[70 / 8] |= 1 << (70 % 8);
        flags[71 / 8] |= 1 << (71 % 8);
        flags[72 / 8] |= 1 << (72 % 8);

        Dalamud.Memory.MemoryHelper.WriteRaw(memAddr, flags);
    }*/
}
