#nullable disable
using System;
using System.Collections.Generic;
using System.Reflection;
namespace Saucy.TripleTriad.GameLogic;

public class TriadGameModifierDB
{
    private static readonly TriadGameModifierDB instance = new();
    public List<TriadGameModifier> mods;

    public TriadGameModifierDB()
    {
        mods = [];
        foreach (var type in Assembly.GetAssembly(typeof(TriadGameModifier)).GetTypes())
        {
            if (type.IsSubclassOf(typeof(TriadGameModifier)))
            {
                var modOb = (TriadGameModifier)Activator.CreateInstance(type);
                mods.Add(modOb);
            }
        }

        mods.Sort((a, b) => (a.GetLocalizationId().CompareTo(b.GetLocalizationId())));

        for (var idx = 0; idx < mods.Count; idx++)
        {
            if (mods[idx].GetLocalizationId() != idx)
            {
                Logger.WriteLine("FAILED to initialize modifiers!");
                break;
            }
        }
    }
    public static TriadGameModifierDB Get() => instance;
}
