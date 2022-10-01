using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using System.Reflection;
using ECommons;
using PunishLib;
using ECommons.DalamudServices;
using Dalamud.Game;
using System;
using FFXIVClientStructs.FFXIV.Client.UI;
using static ECommons.GenericHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CufBot
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "CufBot";

        private const string commandName = "/cufabot";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }

        public Plugin(
            [RequiredVersion("1.0")] DalamudPluginInterface pluginInterface,
            [RequiredVersion("1.0")] CommandManager commandManager)
        {
            this.PluginInterface = pluginInterface;
            this.CommandManager = commandManager;

            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            // you might normally want to embed resources and load them from the manifest stream
            var imagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "punish.png");
            var demoImage = this.PluginInterface.UiBuilder.LoadImage(imagePath);
            this.PluginUi = new PluginUI(this.Configuration, demoImage);

            this.CommandManager.AddHandler(commandName, new CommandInfo(OnCommand)
            {
                HelpMessage = "A useful message to display in /xlhelp"
            });

            this.PluginInterface.UiBuilder.Draw += DrawUI;
            this.PluginInterface.UiBuilder.OpenConfigUi += DrawConfigUI;
            ECommons.ECommons.Init(pluginInterface, this);
            Svc.Framework.Update += RunBot;
        }

        private unsafe void RunBot(Framework framework)
        {
            var addon = Svc.GameGui.GetAddonByName("PunchingMachine", 1);
            if (addon == IntPtr.Zero) return;

            var ui = (AtkUnitBase*)addon;

            if (ui->IsVisible)
            {
                var slidingNode = ui->UldManager.NodeList[18];

                if (slidingNode->Width >= 180 && slidingNode->Width <= 220)
                {
                    
                }
            }
        }

        public void Dispose()
        {
            this.PluginUi.Dispose();
            this.CommandManager.RemoveHandler(commandName);
            ECommons.ECommons.Dispose();

            Svc.Framework.Update -= RunBot;

        }

        private void OnCommand(string command, string args)
        {
            // in response to the slash command, just display our main ui
            this.PluginUi.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.SettingsVisible = true;
        }
    }
}
