#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NLog;

namespace Torch.Utils;

/// <summary>
/// Adds and removes an additional library path
/// </summary>
public class TorchAssemblyResolver : IDisposable
{
    static readonly Logger _log = LogManager.GetCurrentClassLogger();

    static readonly string[] _fileExtensionsToCheck = { ".dll", ".exe" };

    readonly Dictionary<string, Assembly?> _assemblies = new Dictionary<string, Assembly?>();
    readonly string[] _binDirectories;
    readonly string _removablePathPrefix;

    /// <summary>
    /// Initializes an assembly resolver that looks at the given paths for assemblies
    /// </summary>
    /// <param name="paths"></param>
    public TorchAssemblyResolver(params string[] paths)
    {
        string location = Assembly.GetEntryAssembly()?.Location ?? GetType().Assembly.Location;

        if (location != null)
            location = Path.GetDirectoryName(location) + Path.DirectorySeparatorChar;

        _removablePathPrefix = location ?? "";
        _binDirectories = paths;

        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
    }

    string SimplifyPath(string path)
    {
        return path.StartsWith(_removablePathPrefix) ? path.Substring(_removablePathPrefix.Length) : path;
    }

    Assembly? CurrentDomainOnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string assemblyName = new AssemblyName(args.Name).Name!;

        lock (_assemblies)
        {
            Assembly? asm;

            if (_assemblies.TryGetValue(assemblyName, out asm))
                return asm;

            foreach (var item in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (item.GetName().Name!.Equals(assemblyName))
                {
                    _assemblies.Add(assemblyName, item);
                    return item;
                }
            }

            foreach (string path in _binDirectories)
            {
                try
                {
                    foreach (var tryExt in _fileExtensionsToCheck)
                    {
                        string assemblyPath = Path.Combine(path, assemblyName + tryExt);

                        if (!File.Exists(assemblyPath))
                            continue;

                        _log.Trace($"Loading {assemblyName} from {SimplifyPath(assemblyPath)}");

                        LogManager.Flush();

                        asm = Assembly.LoadFrom(assemblyPath);
                        _assemblies.Add(assemblyName, asm);

                        // Recursively load SE dependencies since they don't trigger AssemblyResolve.
                        // This trades some performance on load for actually working code.
                        //foreach (AssemblyName dependency in asm.GetReferencedAssemblies())
                        //    CurrentDomainOnAssemblyResolve(sender, new ResolveEventArgs(dependency.Name, asm));

                        return asm;
                    }
                }
                catch
                {
                    // Ignored
                }
            }
        }

        _assemblies.Add(assemblyName, null);

        return null;
    }

    /// <summary>
    /// Unregisters the assembly resolver
    /// </summary>
    public void Dispose()
    {
        AppDomain.CurrentDomain.AssemblyResolve -= CurrentDomainOnAssemblyResolve;
        _assemblies.Clear();
    }
}
