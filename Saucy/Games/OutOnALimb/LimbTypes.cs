using System;
using System.Reflection;
namespace Saucy.OutOnALimb;

[Serializable]
[Obfuscation(Exclude = true)]
public class LimbConfig
{
    public LimbDifficulty LimbDifficulty = LimbDifficulty.Titan;
    public int MinSecondsForAnotherRound = 12;
    public int Step = 10;
}

public enum LimbDifficulty
{
    Titan, Morbol, Cactuar
}

[Obfuscation(Exclude = true)]
public enum HitPower
{
    Unobserved, Nothing, Weak, Strong, Maximum
}

public class HitResult(int cursor, HitPower power)
{
    public int Position = cursor;
    public HitPower Power = power;
}

public static class LimbStringExtensions
{
    public static string RemoveSpaces(this string s) => s.Replace(" ", "");
}
