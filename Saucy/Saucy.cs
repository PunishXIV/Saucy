using ClickLib;
using ClickLib.Clicks;
using Dalamud.Game;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.IO;
using System.Linq;
using static ECommons.GenericHelpers;

namespace Saucy
{
    public sealed class Saucy : IDalamudPlugin
    {
        public string Name => "Saucy";

        private const string commandName = "/saucy";

        private DalamudPluginInterface PluginInterface { get; init; }
        private CommandManager CommandManager { get; init; }
        private Configuration Configuration { get; init; }
        private PluginUI PluginUi { get; init; }

        private Inputs Inputs { get; set; }


        public Saucy(
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
            Click.Initialize();
            Inputs = new Inputs();

        }


        private unsafe void RunBot(Framework framework)
        {
            if (!PluginUi.Enabled)
            {
                Inputs.ForceRelease(Dalamud.Game.ClientState.Keys.VirtualKey.NUMPAD0);
                return;
            }

            var prizeMenu = Svc.GameGui.GetAddonByName("GoldSaucerReward", 1);
            var addon = Svc.GameGui.GetAddonByName("PunchingMachine", 1);

            if (TryGetAddonByName<AddonSelectString>("SelectString", out var startMenu) && startMenu->AtkUnitBase.IsVisible)
            {
                try
                {
                    ClickSelectString.Using((IntPtr)startMenu).SelectItem1();
                }
                catch
                {

                }
            }

            if (addon != IntPtr.Zero)
            {
                var ui = (AtkUnitBase*)addon;

                if (ui->IsVisible)
                {
                    var slidingNode = ui->UldManager.NodeList[18];
                    var button = (IntPtr)ui->UldManager.NodeList[10];

                    if (slidingNode->Width >= 210 && slidingNode->Width <= 240)
                    {
                        Inputs.SimulatePress(Dalamud.Game.ClientState.Keys.VirtualKey.NUMPAD0);
                    }

                }
            }

            GameObject* cuf = (GameObject*)Svc.Objects.Where(x => x.DataId == 2005029).OrderBy(x => x.YalmDistanceX).FirstOrDefault()?.Address;
            if ((IntPtr)cuf == IntPtr.Zero)
                return;

            TargetSystem* tg = TargetSystem.Instance();
            tg->InteractWithObject(cuf);

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
            this.PluginUi.Visible = true;
        }

        private void DrawUI()
        {
            this.PluginUi.Draw();
        }

        private void DrawConfigUI()
        {
            this.PluginUi.Visible = true;
        }
    }


}
