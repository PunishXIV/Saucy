using FFXIVClientStructs.FFXIV.Component.GUI;
using Saucy.OutOnALimb.ECEmbedded;
using System.Runtime.InteropServices;
namespace Saucy.OutOnALimb;

[StructLayout(LayoutKind.Explicit)]
internal unsafe struct AddonMiniGameBotanist
{
    [FieldOffset(0)] public AtkUnitBase AtkUnitBase;
    [FieldOffset(0x2D1)] public byte HitPendingRaw;
    [FieldOffset(0x328)] public uint Health;

    public readonly bool HitPending => HitPendingRaw != 0;

    internal static AddonMiniGameBotanist* From(AtkUnitBase* addon) => (AddonMiniGameBotanist*)addon;
}

public unsafe class ReaderMiniGameBotanist(AtkUnitBase* UnitBase, int BeginOffset = 0) : AtkReader(UnitBase, BeginOffset)
{
    public uint State => ReadUInt(0) ?? 0;

    public uint SwingsLeft => ReadUInt(11) ?? 0;
}
