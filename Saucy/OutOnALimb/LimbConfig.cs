using ECommons.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Saucy.OutOnALimb;

[Serializable]
[Obfuscation(Exclude =true)]
public class Config : IEzConfig
{
		public bool EnableLimb = false;
		public int Tolerance = 2;
		public int Step = 10;
		public int StopAt = 18;
		public int HardStopAt = 12;
}
