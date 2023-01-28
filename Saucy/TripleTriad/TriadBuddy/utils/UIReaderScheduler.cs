using Dalamud.Game.Gui;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;

namespace MgAl2O4.Utils
{
    public interface IUIReader
    {
        string GetAddonName();
        void OnAddonLost();
        void OnAddonShown(IntPtr addonPtr);
        void OnAddonUpdate(IntPtr addonPtr);
    }

    public class UIReaderScheduler
    {
        private class AddonInfo
        {
            public string name;
            public IUIReader reader;
            public bool isActive;

            public IntPtr addonPtr;
        }

        private readonly GameGui gameGui;
        private readonly List<AddonInfo> addons = new();

        private const float slowCheckInterval = 0.5f;
        private float slowCheckRemaining = 0.0f;
        private bool hasActiveAddons = false;

        public UIReaderScheduler(GameGui gameGui)
        {
            this.gameGui = gameGui;
        }

        public void AddObservedAddon(IUIReader uiReader)
        {
            addons.Add(new AddonInfo() { name = uiReader.GetAddonName(), reader = uiReader });
        }

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
                            var addonPtr = GetAddonPtrIfValid(addon.name);
                            if (addonPtr != IntPtr.Zero)
                            {
                                addon.addonPtr = addonPtr;
                                addon.isActive = true;
                                hasActiveAddons = true;

                                addon.reader.OnAddonShown(addonPtr);
                            }
                        }
                    }
                }

                // every tick: update & look for lost addons
                if (hasActiveAddons)
                {
                    hasActiveAddons = false;
                    foreach (var addon in addons)
                    {
                        if (addon.isActive)
                        {
                            var addonPtr = GetAddonPtrIfValid(addon.name);
                            if (addonPtr != addon.addonPtr)
                            {
                                addon.isActive = false;
                                addon.reader.OnAddonLost();

                                if (addonPtr != IntPtr.Zero)
                                {
                                    addon.isActive = true;
                                    addon.reader.OnAddonShown(addonPtr);
                                }
                            }

                            addon.addonPtr = addonPtr;
                            if (addonPtr != IntPtr.Zero)
                            {
                                addon.reader.OnAddonUpdate(addonPtr);
                                hasActiveAddons = true;
                            }
                        }
                    }
                }
            }
        }

        private unsafe IntPtr GetAddonPtrIfValid(string name)
        {
            IntPtr addonPtr = (gameGui == null) ? IntPtr.Zero : gameGui.GetAddonByName(name, 1);
            if (addonPtr != IntPtr.Zero)
            {
                var baseNode = (AtkUnitBase*)addonPtr;
                if (baseNode->RootNode != null && baseNode->RootNode->IsVisible)
                {
                    return addonPtr;
                }
            }

            return IntPtr.Zero;
        }
    }
}
