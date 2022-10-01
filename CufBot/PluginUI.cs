using ImGuiNET;
using System;
using System.Numerics;

namespace Saucy
{
    // It is good to have this be disposable in general, in case you ever need it
    // to do any cleanup
    class PluginUI : IDisposable
    {
        private Configuration configuration;

        private ImGuiScene.TextureWrap demoImage;

        // this extra bool exists for ImGui, since you can't ref a property
        private bool visible = false;
        public bool Visible
        {
            get { return this.visible; }
            set { this.visible = value; }
        }

        private bool settingsVisible = false;
        public bool SettingsVisible
        {
            get { return this.settingsVisible; }
            set { this.settingsVisible = value; }
        }

        // passing in the image here just for simplicity
        public PluginUI(Configuration configuration, ImGuiScene.TextureWrap demoImage)
        {
            this.configuration = configuration;
            this.demoImage = demoImage;
        }

        public void Dispose()
        {
            this.demoImage.Dispose();
        }

        public bool Enabled { get; set; } = false;

        public void Draw()
        {
            // This is our only draw handler attached to UIBuilder, so it needs to be
            // able to draw any windows we might have open.
            // Each method checks its own visibility/state to ensure it only draws when
            // it actually makes sense.
            // There are other ways to do this, but it is generally best to keep the number of
            // draw delegates as low as possible.

            DrawMainWindow();
        }

        public void DrawMainWindow()
        {
            if (!Visible)
            {
                return;
            }

            ImGui.SetNextWindowSize(new Vector2(375, 330), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(375, 330), new Vector2(float.MaxValue, float.MaxValue));
            if (ImGui.Begin("Saucy Config", ref this.visible, ImGuiWindowFlags.AlwaysAutoResize))
            {
                bool enabled = Enabled;

                ImGui.TextWrapped(@"How to use: Click ""Enable Saucy"" then walk up to a Cuff-a-cur machine.");
                ImGui.Separator();

                if (ImGui.Checkbox("Enable Saucy", ref enabled))
                {
                    Enabled = enabled;
                }
            }
            ImGui.End();
        }
    }
}
