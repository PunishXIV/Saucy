using System;
using System.Reflection;
namespace Saucy;

internal enum GoldSaucerArcadeMachine
{
    Cuff,
    Limb
}

[Serializable]
[Obfuscation(Exclude = true)]
public class GoldSaucerArcadeRunSettings
{
    public bool PlayXTimes { get; set; }

    public int MatchCount { get; set; } = 1;

    public bool EnableFakeBreak { get; set; }

    public int FakeBreakPlayMinutes { get; set; } = 60;

    public int FakeBreakMinutes { get; set; } = 5;
}
