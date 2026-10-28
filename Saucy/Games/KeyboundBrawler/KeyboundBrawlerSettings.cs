using System;
namespace Saucy.KeyboundBrawler;

[Serializable]
public class KeyboundBrawlerSettings
{
    public bool AutoType { get; set; } = true;

    public int MinCharDelayMs { get; set; } = 95;

    public int MaxCharDelayMs { get; set; } = 167;

    public int EnterDelayMs { get; set; } = 140;

    // typing state + word list in the overlay
    public bool ShowDebug { get; set; }
}
