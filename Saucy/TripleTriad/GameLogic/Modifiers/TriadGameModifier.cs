#nullable disable
using System;
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

[Flags]
public enum ETriadGameSpecialMod
{
    None = 0,
    SelectVisible3 = 0x1,
    SelectVisible5 = 0x2,
    RandomizeRule = 0x4,
    RandomizeBlueDeck = 0x8,
    SwapCards = 0x10,
    BlueCardSelection = 0x20,
    IgnoreOwnedCheck = 0x40
}

public class TriadGameModifier : IComparable
{
    [Flags]
    public enum EFeature
    {
        None = 0,
        CardPlaced = 1,
        CaptureNei = 2,
        CaptureWeights = 4,
        CaptureMath = 8,
        PostCapture = 16,
        AllPlaced = 32,
        FilterNext = 64
    }

    protected bool bAllowCombo;
    protected bool bHasLastRedReminder;
    protected bool bIsDeckOrderImportant;
    public string DisplayName = string.Empty;
    protected EFeature Features = EFeature.None;
    protected int RuleIndex;
    protected string RuleName;
    protected ETriadGameSpecialMod SpecialMod = ETriadGameSpecialMod.None;

    public int CompareTo(object obj) => CompareTo((TriadGameModifier)obj);

    public virtual string GetCodeName() => RuleName;
    public virtual string GetLocalizedName() => string.IsNullOrEmpty(DisplayName) ? RuleName : DisplayName;
    public int GetLocalizationId() => RuleIndex;
    public virtual bool AllowsCombo() => bAllowCombo;
    public virtual bool IsDeckOrderImportant() => bIsDeckOrderImportant;
    public virtual ETriadGameSpecialMod GetSpecialRules() => SpecialMod;
    public virtual EFeature GetFeatures() => Features;
    public virtual bool HasLastRedReminder() => bHasLastRedReminder;
    public override string ToString() => GetCodeName();

    public virtual void OnCardPlaced(TriadGameSimulationState gameData, int boardPos) { }
    public virtual void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList) { }
    public virtual void OnCheckCaptureCardWeights(TriadGameSimulationState gameData, int boardPos, int neiPos, bool isReverseActive, ref int cardNum, ref int neiNum) { }
    public virtual void OnCheckCaptureCardMath(TriadGameSimulationState gameData, int boardPos, int neiPos, int cardNum, int neiNum, ref bool isCaptured) { }
    public virtual void OnPostCaptures(TriadGameSimulationState gameData, int boardPos) { }
    public virtual void OnScreenUpdate(TriadGameSimulationState gameData) { }
    public virtual void OnAllCardsPlaced(TriadGameSimulationState gameData) { }
    public virtual void OnFilterNextCards(TriadGameSimulationState gameData, ref int allowedCardsMask) { }
    public virtual void OnMatchInit() { }
    public virtual void OnScoreCard(TriadCard card, ref float score) { }

    public int CompareTo(TriadGameModifier otherMod)
    {
        if (otherMod != null)
        {
            var locStrA = GetLocalizedName();
            var locStrB = otherMod.GetLocalizedName();
            return locStrA.CompareTo(locStrB);
        }
        return 0;
    }

    public virtual TriadGameModifier Clone() => (TriadGameModifier)MemberwiseClone();

    public override bool Equals(object obj) => (obj is TriadGameModifier otherMod) && (GetLocalizationId() == otherMod.GetLocalizationId());

    public override int GetHashCode() => GetLocalizationId();
}
