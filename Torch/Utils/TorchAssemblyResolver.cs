#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
#if NETCOREAPP
using System.Runtime.InteropServices;
using System.Runtime.Loader;
#endif
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

#if NETFRAMEWORK
        AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainOnAssemblyResolve;
#else
        AssemblyLoadContext.Default.Resolving += ResolveAssembly;
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += ResolveUnmanagedDll;
#endif
    }

#if NETCOREAPP
    Assembly? ResolveAssembly(AssemblyLoadContext alc, AssemblyName name)
    {
        return CurrentDomainOnAssemblyResolve(null, name, null);
    }

    IntPtr ResolveUnmanagedDll(Assembly assembly, string name)
    {
        foreach (var binDir in _binDirectories)
        {
            var path = Path.Combine(binDir, name) + ".dll";

            if (NativeLibrary.TryLoad(path, out IntPtr handle))
                return handle;
        }

        return 0;
    }
#endif

    string SimplifyPath(string path)
    {
        return path.StartsWith(_removablePathPrefix) ? path.Substring(_removablePathPrefix.Length) : path;
    }

    Assembly? CurrentDomainOnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        var name = new AssemblyName(args.Name);

        lock (_assemblies)
            return CurrentDomainOnAssemblyResolve(sender, name, args.RequestingAssembly);
    }

    Assembly? CurrentDomainOnAssemblyResolve(object? sender, AssemblyName name, Assembly? requestingAssembly)
    {
        string assemblyName = name.Name!;

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

#if NET8_0_OR_GREATER
                    if (assemblyName == "VRage.Dedicated")
                    {
                        var data = File.ReadAllBytes(assemblyPath);
                        RemoveVRageDedicatedUnsupportedTypes(ref data);
                        asm = Assembly.Load(data);
                    }
                    else
#endif
                    if (assemblyName == "Steamworks.NET")
                    {
                        asm = FixSteamworksNETTypes(assemblyPath);
                    }
                    else
                    {
                        asm = Assembly.LoadFrom(assemblyPath);
                    }

                    _assemblies.Add(assemblyName, asm);

                    // Recursively load SE dependencies since they don't trigger AssemblyResolve.
                    // This trades some performance on load for actually working code.
                    foreach (AssemblyName dependency in asm.GetReferencedAssemblies())
                    {
                        if (!_assemblies.ContainsKey(dependency.Name!))
                            CurrentDomainOnAssemblyResolve(sender, dependency, asm);
                    }

                    return asm;
                }
            }
            catch
            {
                // Ignored
            }
        }

        _assemblies.Add(assemblyName, null);

        return null;
    }

#if NET8_0_OR_GREATER
    static void RemoveVRageDedicatedUnsupportedTypes(ref byte[] data)
    {
        using var assemResolver = new Mono.Cecil.DefaultAssemblyResolver();
        assemResolver.AddSearchDirectory("DedicatedServer64");

        var readerParams = new Mono.Cecil.ReaderParameters() {
            InMemory = true,
            AssemblyResolver = assemResolver
        };

        using var assemblyDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(data), readerParams);

        // These reference System.Configuration.Install
        RemoveType("VRage.Dedicated.WindowsServiceInstallerBase");
        RemoveType("VRage.Dedicated.Configurator.SelectInstanceForm");
        // These depend on the above and are not used by Torch
        RemoveType("VRage.Dedicated.MyConfigurator");
        RemoveType("VRage.Dedicated.DedicatedServer");
        RemoveType("VRage.Dedicated.ConfigForm");
        RemoveType("VRage.Dedicated.WindowsService");

        assemblyDef.MainModule.AssemblyReferences.Remove(assemblyDef.MainModule.AssemblyReferences.First(r => r.Name == "System.Configuration.Install"));

        using (var stream = new MemoryStream(data.Length))
        {
            assemblyDef.Write(stream);
            data = stream.ToArray();
        }

        void RemoveType(string fullName)
        {
            var type = assemblyDef.MainModule.GetType(fullName);
            assemblyDef.MainModule.Types.Remove(type);
        }
    }
#endif

    static Assembly FixSteamworksNETTypes(string assemblyPath)
    {
        var data = File.ReadAllBytes(assemblyPath);

        using var assemResolver = new Mono.Cecil.DefaultAssemblyResolver();
        assemResolver.RemoveSearchDirectory(".");
        assemResolver.RemoveSearchDirectory("bin");

        var readerParams = new Mono.Cecil.ReaderParameters() {
            InMemory = true,
            AssemblyResolver = assemResolver
        };

        using var moduleDef = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(data), readerParams);

        // The type has an explicit field layout and contains a reference type overlapped with a value type.
        // The fields are not accessible so just remove them and set the struct size.
        var type = moduleDef.GetType("Steamworks.SteamDatagramRelayAuthTicket/ExtraField/OptionValue");

        type.IsExplicitLayout = false;
        type.IsSequentialLayout = true;
        type.ClassSize = 16;
        type.PackingSize = 8;
        type.Fields.Clear();

        Directory.CreateDirectory("PatchedAssemblies");

        var newFilePath = Path.Combine("PatchedAssemblies", Path.GetFileName(assemblyPath));

        //using (var stream = new MemoryStream(data.Length))
        using (var stream = File.OpenWrite(newFilePath))
        {
            moduleDef.Write(stream);
            //data = stream.ToArray();
        }

        //return Assembly.Load(data);
        return Assembly.LoadFrom(newFilePath);
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
