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
