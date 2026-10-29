using System;

namespace Saucy.KeyboundBrawler;

internal sealed class TypingDelayCalculator(double speedScale, double smoothingFactor, int variationMs)
{
    private const int GlobalMeanDelayMs = 172;
    private const int MinimumBaseDelayMs = 80;
    private const int MinimumFinalDelayMs = 70;
    private const int SlowestMeasuredDelayMs = 714;
    private const int SlowestPunctuationDelayMs = 433;

    private const string Chars = "serht', almk-pciGwnfgbou.)yYd(Hv!TzNq:EU;O\"BL?WMFC_S";
    private const string ExcludedPunctuation = "., -!?\"'„“”";
    private static ReadOnlySpan<ushort> Delays =>
    [
        131, 100, 104, 99, 165, 319, 183, 121,
        133, 158, 181, 107, 190, 143, 154, 117,
        203, 147, 113, 140, 97, 240, 111, 127,
        206, 254, 180, 225, 122, 658, 506, 131,
        347, 442, 277, 249, 219, 313, 437, 651,
        203, 714, 433, 336, 506, 230, 239, 377,
        342, 337, 537, 369
    ];

    private readonly double speed = Math.Clamp(double.IsFinite(speedScale) ? speedScale : 0.8, MinimumSpeedScale(smoothingFactor), 1.0);
    private readonly double smoothing = Math.Clamp(double.IsFinite(smoothingFactor) ? smoothingFactor : 0.35, 0.0, 1.0);
    private readonly int variation = Math.Clamp(variationMs, 0, 100);

    internal static double MinimumSpeedScale(double smoothingFactor)
    {
        var smoothing = Math.Clamp(double.IsFinite(smoothingFactor) ? smoothingFactor : 0.35, 0.0, 1.0);
        var slowestDelay = Math.Max(SlowestMeasuredDelayMs + (GlobalMeanDelayMs - SlowestMeasuredDelayMs) * smoothing,
                                    SlowestPunctuationDelayMs);
        return MinimumBaseDelayMs / slowestDelay;
    }

    public int GetDelay(char character)
    {
        var index = Chars.IndexOf(character);
        double delay;
        if (index >= 0) delay = Delays[index];
        else
        {
            index = Chars.IndexOf(char.ToLowerInvariant(character));
            delay = index >= 0 ? Delays[index] * 1.15 : GlobalMeanDelayMs;
        }

        if (!ExcludedPunctuation.Contains(character))
            delay += (GlobalMeanDelayMs - delay) * smoothing;

        delay = Math.Max(MinimumBaseDelayMs, delay * speed) + Random.Shared.Next(-variation, variation + 1);
        return Math.Max(MinimumFinalDelayMs, (int)Math.Round(delay));
    }
}
