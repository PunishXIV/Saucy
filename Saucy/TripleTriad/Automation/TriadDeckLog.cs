namespace Saucy.TripleTriad;

internal static class TriadDeckLog
{
    /// <summary>
    /// Prints an optimizer chat line in the current UI language. Pass a <see cref="LocText"/>
    /// (English format plus arguments) rather than an interpolated string so the text keeps a
    /// translation key.
    /// </summary>
    public static void Print(LocText message, bool force = false)
    {
        if (!force && !C.ShowOptimizerChatSpam)
        {
            return;
        }

        Svc.Chat.Print(message.Localized);
    }
}
