using System;
namespace Saucy.IPC;

internal static class IPCNames
{
    public const string Lifestream = "Lifestream";
    public const string BossMod = "BossMod";
    public const string Vnavmesh = "vnavmesh";
    public const string Questionable = "Questionable";
}

[AttributeUsage(AttributeTargets.Class)]
internal sealed class IPCAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
