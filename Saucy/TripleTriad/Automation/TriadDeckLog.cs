namespace Saucy.TripleTriad;

internal static class TriadDeckLog
{
    public static void Print(string message, bool force = false)
    {
        if (!force && !C.ShowOptimizerChatSpam)
        {
            return;
        }

        Svc.Chat.Print(message);
    }
}
