using System;
namespace Saucy.IPC;

[AttributeUsage(AttributeTargets.Class)]
internal sealed class IPCAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
