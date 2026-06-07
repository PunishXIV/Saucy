namespace Saucy.OtherGames;

internal static class GoldSaucerGateDependenciesUi
{
    public static void DrawSliceIsRight() =>
        PluginDependenciesUi.Draw(
            "Optional plugin for automatic dodging during the GATE. Overlays still work without it.",
            [
                PluginDependenciesUi.BossModPlugin(
                    "Provides the Slice is Right boss module (hazard zones) and the VBM AI preset Saucy activates during the GATE. " +
                    "Keep the Gold Saucer Slice is Right module enabled in Boss Mod settings.")
            ]);

    public static void DrawWindBlows() =>
        PluginDependenciesUi.Draw(
            "Optional plugin for automatic movement to the safe spot. Overlays still work without it.",
            [
                PluginDependenciesUi.Vnavmesh(
                    "Pathfinds you onto the statistical safe spot during the GATE.")
            ]);
}
