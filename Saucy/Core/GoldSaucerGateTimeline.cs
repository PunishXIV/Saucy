using Saucy.Framework;
using System;
using System.Diagnostics;

namespace Saucy;

internal enum TimelineState
{
    Entry,
    InProgress,
    Coming
}

internal readonly record struct TimelineGate(long Slot, int Kind, LocText Name, LocText Variant, int EntrySeconds, int InProgressSeconds)
{
    public Module.GateType Type => GoldSaucerGateTimeline.GateTypes[Kind];
}

internal readonly record struct TimelinePinned(TimelineGate Gate, TimelineState State, long EndsAt);

// Based on https://github.com/rooroodev/ffxivgateschedule

internal static class GoldSaucerGateTimeline
{
    public const int SlotSeconds = 1200;
    public const int AllGateMask = 31;

    private const int NotifyWindowSeconds = 90;

    public static readonly LocText[] Names =
    [
            LocText.Of(ModuleDisplayNames.AirForceOne),
            LocText.Of("Cliffhanger"),
            LocText.Of("Leap of Faith"),
            LocText.Of(ModuleDisplayNames.AnyWayTheWindBlows),
            LocText.Of(ModuleDisplayNames.SliceIsRight)
    ];

    public static readonly Module.GateType[] GateTypes =
    [
            Module.GateType.AirForceOne,
            Module.GateType.Cliffhanger,
            Module.GateType.LeapOfFaith,
            Module.GateType.AnyWayTheWindBlows,
            Module.GateType.SliceIsRight
    ];

    private static readonly LocText[] CieldalaesRoutes =
    [
            LocText.Of("Cieldalaes: Sea Route"),
            LocText.Of("Cieldalaes: Cave Route"),
            LocText.Of("Cieldalaes: River Route")
    ];

    private static readonly LocText[] SaucerRoutes =
    [
            LocText.Of("Event Square Route"),
            LocText.Of("Wonder Square Route"),
            LocText.Of("Round Square Route")
    ];

    private static readonly LocText[] LeapStages =
    [
            LocText.Of("The Fall of Belah'dia"),
            LocText.Of("The Falling City of Nym"),
            LocText.Of("Sylphstep")
    ];

    private static long lastNotifiedSlot  = -1;
    private static long lastCheckedSecond = -1;

    static GoldSaucerGateTimeline()
    {
        Debug.Assert(GateTypes.Length == Names.Length);
        Debug.Assert(Predict(0) is { Kind    : 0, Variant.Format: "Cieldalaes: Sea Route" });
        Debug.Assert(Predict(14400) is { Kind: 1, Variant.Format: "Round Square" });
        Debug.Assert(Predict(18000) is { Kind: 2, Variant.Format: "The Falling City of Nym" });
        Debug.Assert(Predict(1200)
                            .Kind ==
                     3 &&
                     Predict(2400)
                            .Kind ==
                     4);
    }

    public static TimelineGate Predict(long slot)
    {
        var  x           = unchecked((uint)slot);
        uint y           = 0, z = 0, w = 0;
        var  gateDraw    = Next(ref x, ref y, ref z, ref w) % 100;
        var  variantDraw = Next(ref x, ref y, ref z, ref w);
        var  minute      = slot / SlotSeconds % 3;

        var kind = minute switch
                   {
                           0 => gateDraw < 32 ? 0 : gateDraw < 65 ? 1 : 2,
                           1 => gateDraw < 45 ? 3 : gateDraw < 78 ? 4 : 0,
                           _ => gateDraw < 12 ? 4 : gateDraw < 25 ? 2 : 0
                   };

        var variant = kind switch
                      {
                              0 when minute == 1 => SaucerRoutes[variantDraw     % 3],
                              0                  => CieldalaesRoutes[variantDraw % 3],
                              1 => variantDraw % 14 < 7
                                           ? LocText.Of("Round Square")
                                           : LocText.Of("Wonder Square East"),
                              2 => LeapStages[variantDraw % 27 / 9],
                              _ => LocText.Of("Event Square")
                      };

        var entry = kind switch
                    {
                            2      => 480,
                            3 or 4 => 120,
                            _      => 600
                    };

        var progress = kind switch
                       {
                               3 => 75,
                               4 => 270,
                               _ => 0
                       };

        return new TimelineGate(slot, kind, Names[kind], variant, entry, progress);
    }

    public static TimelinePinned Current(long now)
    {
        var slot    = now / SlotSeconds * SlotSeconds;
        var gate    = Predict(slot);
        var elapsed = now - slot;
        if (elapsed < gate.EntrySeconds)
        {
            return new TimelinePinned(gate, TimelineState.Entry, slot + gate.EntrySeconds);
        }

        if (elapsed < gate.EntrySeconds + gate.InProgressSeconds)
        {
            return new TimelinePinned(gate, TimelineState.InProgress, slot + gate.EntrySeconds + gate.InProgressSeconds);
        }

        var next = slot + SlotSeconds;
        return new TimelinePinned(Predict(next), TimelineState.Coming, next);
    }

    public static bool IsShown(int filter, TimelineGate gate) => (filter & (1 << gate.Kind)) != 0;

    public static void Tick()
    {
        if (!C.GoldSaucerGates.TimelineNotifyOnEntry)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (now == lastCheckedSecond)
        {
            return;
        }

        lastCheckedSecond = now;
        var pinned = Current(now);
        if (pinned.State           != TimelineState.Entry ||
            pinned.Gate.Slot       == lastNotifiedSlot    ||
            now - pinned.Gate.Slot > NotifyWindowSeconds  ||
            !IsShown(C.GoldSaucerGates.TimelineGateFilter, pinned.Gate))
        {
            return;
        }

        lastNotifiedSlot = pinned.Gate.Slot;
        Svc.Chat.Print(Loc.T("[Saucy] {0} entry is open for {1} minutes. {2}.",
                             pinned.Gate.Name.Localized, pinned.Gate.EntrySeconds / 60, pinned.Gate.Variant.Localized));
    }

    private static uint Next(ref uint x, ref uint y, ref uint z, ref uint w)
    {
        unchecked
        {
            var t = x ^ (x << 11);
            x = y;
            y = z;
            z = w;
            w = w ^ (w >> 19) ^ t ^ (t >> 8);
            return w;
        }
    }
}
