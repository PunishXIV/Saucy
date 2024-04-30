using ECommons.UIHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Saucy.OutOnALimb;
public unsafe class ReaderMiniGameBotanist(AtkUnitBase* UnitBase, int BeginOffset = 0) : AtkReader(UnitBase, BeginOffset)
{
		public uint State => ReadUInt(0) ?? 0;
		public uint Unk1 => ReadUInt(1) ?? 0;
		public uint Unk2 => ReadUInt(2) ?? 0;
		public int Unk3 => ReadInt(3) ?? 0;
		public int Unk4 => ReadInt(4) ?? 0;
		public int Unk5 => ReadInt(5) ?? 0;
		public int Unk6 => ReadInt(6) ?? 0;
		public int Unk7 => ReadInt(7) ?? 0;
		public bool Unk10 => ReadBool(10) ?? false;
		public uint SwingsLeft => ReadUInt(11) ?? 0;
		public uint Health => ReadUInt(12) ?? 0;
		public uint MaxHealth => ReadUInt(13) ?? 0;
		public uint Unk14 => ReadUInt(14) ?? 0;
		public string TimeRemaining => ReadString(15);
		public int SecondsRemaining => int.Parse(TimeRemaining.Split(":")[1]);
}
