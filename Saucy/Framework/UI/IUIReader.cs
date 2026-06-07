namespace Saucy.Framework.UI;

public interface IUIReader
{
    string GetAddonName();
    void OnAddonLost();
    void OnAddonShown(nint addonPtr);
    void OnAddonUpdate(nint addonPtr);
}
