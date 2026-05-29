namespace Saucy.IPC;

internal static class IpcSubscriptions
{
    public static void Refresh()
    {
        QuestionableInterop.Refresh();
        VnavmeshInterop.Refresh();
        LifestreamInterop.Refresh();
    }

    public static void Dispose()
    {
        QuestionableInterop.Dispose();
        VnavmeshInterop.Dispose();
        LifestreamInterop.Dispose();
    }
}
