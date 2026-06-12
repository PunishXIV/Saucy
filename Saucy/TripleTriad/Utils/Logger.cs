namespace Saucy.TripleTriad.Utils;

public class Logger
{
    public static void WriteLine(string fmt, params object[] args)
    {
#if DEBUG
        Svc.Log?.Info(string.Format(fmt, args));
#endif
    }
}
