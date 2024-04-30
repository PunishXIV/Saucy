using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Saucy.OutOnALimb;
public class HitResult
{
		public int Position;
		public HitPower Power = HitPower.Unobserved;

		public HitResult(int cursor, HitPower power)
		{
				this.Position = cursor;
				this.Power = power;
		}
}
