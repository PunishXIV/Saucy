using FFXIVClientStructs.FFXIV.Component.GUI;
using System.Collections.Generic;
using static ECommons.GenericHelpers;

namespace Saucy.Framework.UI;

public class UIReaderScheduler(IGameGui gameGui)
{
    private const float SlowCheckInterval = 0.15f;
    private readonly List<AddonInfo> addons = [];
    private readonly IGameGui gameGui = gameGui;
    private float slowCheckRemaining;

    public void AddObservedAddon(IUIReader uiReader) => addons.Add(new()
    {
        name = uiReader.GetAddonName(), reader = uiReader
    });

    public void Update(float deltaSeconds)
    {
        if (gameGui == null)
        {
            return;
        }

        slowCheckRemaining -= deltaSeconds;
        if (slowCheckRemaining <= 0.0f)
        {
            slowCheckRemaining = SlowCheckInterval;

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

    private unsafe nint GetAddonPtrIfValid(string name)
    {
        if (gameGui == null)
        {
            return nint.Zero;
        }

        if (name == "GSInfoCardList")
        {
            for (var i = 0; i < 8; i++)
            {
                var handle = gameGui.GetAddonByName(name, i);
                if (handle.Address == nint.Zero)
                {
                    continue;
                }

                var baseNode = (AtkUnitBase*)handle.Address;
                if (IsAddonVisible(baseNode))
                {
                    return handle.Address;
                }
            }

            return nint.Zero;
        }

        return TryGetAddonByName<AtkUnitBase>(name, out var addon) ? (nint)addon : nint.Zero;
    }

    private static unsafe bool IsAddonVisible(AtkUnitBase* baseNode)
    {
        if (baseNode == null)
        {
            return false;
        }

        if (baseNode->IsVisible)
        {
            return true;
        }

        return baseNode->RootNode != null && baseNode->RootNode->IsVisible();
    }

    private class AddonInfo
    {
        public nint addonPtr;
        public bool isActive;
        public string? name;
        public IUIReader? reader;
    }
}
