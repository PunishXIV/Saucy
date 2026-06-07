#nullable disable
using System.Collections.Generic;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierRoulette : TriadGameModifier
{
    protected TriadGameModifier RuleInst;

    public TriadGameModifierRoulette()
    {
        RuleName = "Roulette";
        RuleIndex = 1;
        SpecialMod = ETriadGameSpecialMod.RandomizeRule;
    }

    public override string GetCodeName() => base.GetCodeName() + (RuleInst != null ? (" (" + RuleInst.GetCodeName() + ")") : "");
    public override string GetLocalizedName() => base.GetLocalizedName() + (RuleInst != null ? (" (" + RuleInst.GetLocalizedName() + ")") : "");
    public override bool AllowsCombo() => (RuleInst != null) ? RuleInst.AllowsCombo() : base.AllowsCombo();
    public override bool IsDeckOrderImportant() => (RuleInst != null) ? RuleInst.IsDeckOrderImportant() : base.IsDeckOrderImportant();
    public override ETriadGameSpecialMod GetSpecialRules() => base.GetSpecialRules() | ((RuleInst != null) ? RuleInst.GetSpecialRules() : ETriadGameSpecialMod.None);
    public override EFeature GetFeatures() => (RuleInst != null) ? RuleInst.GetFeatures() : EFeature.None;
    public override bool HasLastRedReminder() => (RuleInst != null) ? RuleInst.HasLastRedReminder() : base.HasLastRedReminder();

    public override void OnCardPlaced(TriadGameSimulationState gameData, int boardPos) => RuleInst?.OnCardPlaced(gameData, boardPos);

    public override void OnCheckCaptureNeis(TriadGameSimulationState gameData, int boardPos, int[] neiPos, List<int> captureList) => RuleInst?.OnCheckCaptureNeis(gameData, boardPos, neiPos, captureList);

    public override void OnCheckCaptureCardWeights(TriadGameSimulationState gameData, int boardPos, int neiPos, bool isReverseActive, ref int cardNum, ref int neiNum) => RuleInst?.OnCheckCaptureCardWeights(gameData, boardPos, neiPos, isReverseActive, ref cardNum, ref neiNum);

    public override void OnCheckCaptureCardMath(TriadGameSimulationState gameData, int boardPos, int neiPos, int cardNum, int neiNum, ref bool isCaptured) => RuleInst?.OnCheckCaptureCardMath(gameData, boardPos, neiPos, cardNum, neiNum, ref isCaptured);

    public override void OnPostCaptures(TriadGameSimulationState gameData, int boardPos) => RuleInst?.OnPostCaptures(gameData, boardPos);

    public override void OnAllCardsPlaced(TriadGameSimulationState gameData) => RuleInst?.OnAllCardsPlaced(gameData);

    public override void OnFilterNextCards(TriadGameSimulationState gameData, ref int allowedCardsMask) => RuleInst?.OnFilterNextCards(gameData, ref allowedCardsMask);

    public override void OnMatchInit() => SetRuleInstance(null);

    public void SetRuleInstance(TriadGameModifier RuleInstance) => RuleInst = RuleInstance;

    public TriadGameModifier GetResolvedRule() => RuleInst;
}
