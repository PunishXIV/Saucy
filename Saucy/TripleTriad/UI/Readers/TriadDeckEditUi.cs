namespace Saucy.TripleTriad.UI;

internal static class TriadDeckEditUi
{
    public static bool IsDeckEditScreenOpen()
    {
        for (var idx = 0; idx < 8; idx++)
        {
            if (Svc.GameGui.GetAddonByName("GSInfoCardDeck", idx) != nint.Zero)
            {
                return true;
            }
        }

        return false;
    }
}
