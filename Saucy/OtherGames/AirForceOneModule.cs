using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Saucy.CuffACur;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Saucy.OtherGames
{
    internal class AirForceOneModule
    {

        [DllImport("user32.dll")]
        static extern void mouse_event(int dwFlags, int dx, int dy,
                      int dwData, int dwExtraInfo);

        [Flags]
        public enum MouseEventFlags
        {
            LEFTDOWN = 0x00000002,
            LEFTUP = 0x00000004,
            MIDDLEDOWN = 0x00000020,
            MIDDLEUP = 0x00000040,
            MOVE = 0x00000001,
            ABSOLUTE = 0x00008000,
            RIGHTDOWN = 0x00000008,
            RIGHTUP = 0x00000010
        }

        public static void LeftClick()
        {
            mouse_event((int)(MouseEventFlags.LEFTDOWN), 0, 0, 0, 0);
            Thread.Sleep(100);
            mouse_event((int)(MouseEventFlags.LEFTUP), 0, 0, 0, 0);
        }


        public static bool ModuleEnabled = false;
        private const double slowCheckInterval = 0.2;
        private static float slowCheckRemaining = 0.0f;
        private static unsafe TargetSystem* tg = TargetSystem.Instance();
        private static Inputs inputs = new Inputs();
        private static unsafe bool isOnScreen(GameObject obj) => tg->IsObjectOnScreen((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)obj.Address);
        private static unsafe FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject* GetObj(GameObject target) => (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address;

        public async static void RunModule()
        {
            try
            {
                Saucy.AirForceOneToken.Token.ThrowIfCancellationRequested();

                List<GameObject> targets = Svc.Objects.Where(x => x.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj && isOnScreen(x) && x.DataId is 2009676 or 2009677 or 2009678).ToList();

                var task = targets.RateLimitedForEachAsync(slowCheckInterval, async target =>
                {
                    if (Saucy.AirForceOneToken.Token.IsCancellationRequested)
                        Saucy.AirForceOneToken.Token.ThrowIfCancellationRequested();

                    if (Service.GameGui.WorldToScreen(target.Position, out var screenCoords))
                    {
                        Cursor.Position = new System.Drawing.Point((int)screenCoords.X, (int)screenCoords.Y);
                        LeftClick();
                    }
                });

                task.Wait();

                Saucy.AirForceOneTask.Clear();
            }
            catch (Exception e)
            {
                Dalamud.Logging.PluginLog.Debug($"{e.Message}");
                Saucy.AirForceOneTask.Clear();
            }
        }
    }

    public static class ListExtensions
    {
        public static async Task RateLimitedForEachAsync<T>(
            this List<T> list,
            double minumumDelay,
            Func<T, Task> async_task_func)
        {
            foreach (var item in list)
            {
                Stopwatch sw = Stopwatch.StartNew();

                await async_task_func(item);

                double left = minumumDelay - sw.Elapsed.TotalSeconds;

                if (left > 0)
                    await Task.Delay(TimeSpan.FromSeconds(left));

            }
        }

        public static void RateLimitedForEach<T>(
            this List<T> list,
            double minumumDelay,
            Action<T> action)
        {
            foreach (var item in list)
            {
                Stopwatch sw = Stopwatch.StartNew();

                action(item);

                double left = minumumDelay - sw.Elapsed.TotalSeconds;

                while (left > 0) { }
            }
        }
    }
}
