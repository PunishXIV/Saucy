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
