using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Saucy.Framework;

public class ModuleManager : IDisposable
{
    private readonly List<Module> _modules = [];

    public IReadOnlyList<Module> Modules => _modules.AsReadOnly();

    public ModuleManager()
    {
        var moduleTypes = Assembly.GetExecutingAssembly().GetTypes().Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract).ToList();
        foreach (var moduleType in moduleTypes)
        {
            try
            {
                if (Activator.CreateInstance(moduleType) is Module module)
                    _modules.Add(module);
            }
            catch (Exception ex)
            {
                Svc.Log.Error(ex, $"[{nameof(ModuleManager)}] Failed to create instance of module type: {moduleType.Name}");
            }
        }
    }

    public T? GetModule<T>() where T : class, IModule => _modules.OfType<T>().FirstOrDefault();

    public void Dispose()
    {
        _modules.ForEach(m => m.Disable());
        _modules.Clear();
    }
}
