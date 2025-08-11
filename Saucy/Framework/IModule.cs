namespace Saucy.Framework;

public interface IModule
{
    string InternalName { get; }
    string Name { get; }
    bool IsEnabled { get; }
    void Enable();
    void Disable();
}
