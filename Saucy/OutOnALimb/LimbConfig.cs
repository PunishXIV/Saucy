using ECommons.Configuration;
using System;
using System.Reflection;

namespace Saucy.OutOnALimb;

[Serializable]
[Obfuscation(Exclude = true)]
public class LimbConfig : IEzConfig
{
		public bool EnableLimb = false;
		public int Tolerance = 2;
		public int Step = 10;
		public int StopAt = 18;
		public int HardStopAt = 12;
		public LimbDifficulty LimbDifficulty = LimbDifficulty.Titan;
}
