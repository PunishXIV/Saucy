namespace Saucy.TripleTriad.UI;

public class UnsafeReaderTriadCards
{
    public UnsafeReaderTriadCards() => HasErrors = false;
    public bool HasErrors { get; }

    public bool IsCardOwned(int cardId)
    {
        if (cardId <= 0 || cardId > 65535)
        {
            return false;
        }

        if (!TriadMemoryReads.IsAvailable)
        {
            return false;
        }

        return TriadMemoryReads.TryIsCardOwned(cardId);
    }

    // Beaten-state reads disabled: native triad NPC queries crash on some clients/patches.
    public bool IsNpcBeaten(int triadResidentId) => false;
}
