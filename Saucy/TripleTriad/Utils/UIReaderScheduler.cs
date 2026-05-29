using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
namespace Saucy.TripleTriad.Utils;

public interface IUIReader
{
    string GetAddonName();
    void OnAddonLost();
    void OnAddonShown(nint addonPtr);
    void OnAddonUpdate(nint addonPtr);
}

public class UIReaderScheduler(IGameGui gameGui)
{
    private const float slowCheckInterval = 0.15f;
    private readonly List<AddonInfo> addons = [];

    private readonly IGameGui gameGui = gameGui;
    private float slowCheckRemaining;

    public void AddObservedAddon(IUIReader uiReader) => addons.Add(new()
    {
        name = uiReader.GetAddonName(), reader = uiReader
    });

    public void Update(float deltaSeconds)
    {
        if (gameGui != null)
        {
            // slow check: look for newly created addons - would be nice to change to event driven
            slowCheckRemaining -= deltaSeconds;
            if (slowCheckRemaining <= 0.0f)
            {
                slowCheckRemaining = slowCheckInterval;

                foreach (var addon in addons)
                {
                    if (!addon.isActive)
                    {
                        if (addon.name == null || addon.reader == null)
                        {
                            continue;
                        }

                        var addonPtr = GetAddonPtrIfValid(addon.name);
                        if (addonPtr != nint.Zero)
                        {
                            addon.addonPtr = addonPtr;
                            addon.isActive = true;

                            addon.reader.OnAddonShown(addonPtr);
                        }
                    }
                }
            }

            // every tick: update & look for lost addons
            foreach (var addon in addons)
            {
                if (!addon.isActive)
                {
                    continue;
                }

                if (addon.name == null || addon.reader == null)
                {
                    continue;
                }

                var addonPtr = GetAddonPtrIfValid(addon.name);
                if (addonPtr != addon.addonPtr)
                {
                    addon.isActive = false;
                    addon.reader.OnAddonLost();

                    if (addonPtr != nint.Zero)
                    {
                        addon.isActive = true;
                        addon.reader.OnAddonShown(addonPtr);
                    }
                }

                addon.addonPtr = addonPtr;
                if (addonPtr != nint.Zero)
                {
                    addon.reader.OnAddonUpdate(addonPtr);
                }
            }
        }
    }

    private unsafe nint GetAddonPtrIfValid(string name)
    {
        if (gameGui == null)
        {
            return nint.Zero;
        }

        var maxIndex = name == "GSInfoCardList" ? 8 : 1;
        for (var i = 0; i < maxIndex; i++)
        {
            var handle = gameGui.GetAddonByName(name, i);
            if (handle.Address == nint.Zero)
            {
                continue;
            }

            var baseNode = (AtkUnitBase*)handle.Address;
            if (baseNode->RootNode != null && baseNode->RootNode->IsVisible())
            {
                return handle.Address;
            }
        }

        return nint.Zero;
    }

    private class AddonInfo
    {
        public nint addonPtr;
        public bool isActive;
        public string? name;
        public IUIReader? reader;
    }
}
