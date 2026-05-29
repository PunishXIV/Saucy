namespace Saucy.TripleTriad.Utils;

public class Logger
{
#if DEBUG
    public static IPluginLog? logger;
#endif // DEBUG

    public static void WriteLine(string fmt, params object[] args)
    {
#if DEBUG
        logger?.Info(string.Format(fmt, args));
#endif // DEBUG
    }
}
