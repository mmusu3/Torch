﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.API.WebAPI;
using Torch.Collections;
using Torch.Commands;
using Torch.Utils;

namespace Torch.Managers
{
    /// <inheritdoc />
    public class PluginManager : Manager, IPluginManager
    {
        //event for when the plugins are reloaded
        public event Action PluginsReloaded;

        private class PluginItem
        {
            public string Filename { get; set; }
            public string Path { get; set; }
            public PluginManifest Manifest { get; set; }
            public bool IsZip { get; set; }
            public List<PluginItem> ResolvedDependencies { get; set; }
        }

        private static Logger _log = LogManager.GetCurrentClassLogger();

        private const string MANIFEST_NAME = "manifest.xml";

        public readonly string PluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        private readonly MtObservableSortedDictionary<Guid, ITorchPlugin> _plugins = new MtObservableSortedDictionary<Guid, ITorchPlugin>();
        private readonly List<PluginItem> _pluginItems = new List<PluginItem>();
        private readonly List<Guid> _reloadList = new List<Guid>();
        private CommandManager _mgr;

#pragma warning disable 649
        [Dependency]
        private ITorchSessionManager _sessionManager;
#pragma warning restore 649

        /// <inheritdoc />
        public IReadOnlyDictionary<Guid, ITorchPlugin> Plugins => _plugins.AsReadOnlyObservable();

        public event Action<IReadOnlyCollection<ITorchPlugin>> PluginsLoaded;

        public PluginManager(ITorchBase torchInstance) : base(torchInstance)
        {
            if (!Directory.Exists(PluginDir))
                Directory.CreateDirectory(PluginDir);
        }

        /// <summary>
        /// Updates loaded plugins in parallel.
        /// </summary>
        public void UpdatePlugins()
        {
            foreach (var plugin in _plugins.Values)
            {
                try
                {
                    plugin.Update();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, $"Plugin {plugin.Name} threw an exception during update!");
                }
            }
        }

        /// <inheritdoc/>
        public override void Attach()
        {
            base.Attach();
            _sessionManager.SessionStateChanged += SessionManagerOnSessionStateChanged;
        }

        private void SessionManagerOnSessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            _mgr = session.Managers.GetManager<CommandManager>();
            if (_mgr == null)
                return;
            switch (newState)
            {
            case TorchSessionState.Loaded:
                foreach (ITorchPlugin plugin in _plugins.Values)
                    _mgr.RegisterPluginCommands(plugin);
                return;
            case TorchSessionState.Unloading:
                foreach (ITorchPlugin plugin in _plugins.Values)
                    _mgr.UnregisterPluginCommands(plugin);
                return;
            case TorchSessionState.Loading:
            case TorchSessionState.Unloaded:
            default:
                return;
            }
        }

        /// <summary>
        /// Unloads all plugins.
        /// </summary>
        public override void Detach()
        {
            _sessionManager.SessionStateChanged -= SessionManagerOnSessionStateChanged;

            foreach (var plugin in _plugins.Values)
                plugin.Dispose();

            _plugins.Clear();
        }

        public void LoadPlugins()
        {
            _log.Info("Loading plugins...");

            if (!string.IsNullOrEmpty(Torch.Config.TestPlugin))
            {
                _log.Info($"Loading plugin for debug at {Torch.Config.TestPlugin}");

                foreach (var item in GetLocalPlugins(Torch.Config.TestPlugin, true))
                {
                    _log.Info(item.Path);
                    LoadPlugin(item);
                }

                foreach (var plugin in _plugins.Values)
                {
                    plugin.Init(Torch);
                }

                _log.Info($"Loaded {_plugins.Count} plugins.");
                PluginsLoaded?.Invoke(_plugins.Values.AsReadOnly());
                return;
            }

            var pluginItems = GetLocalPlugins(PluginDir);
            var pluginsToLoad = new List<PluginItem>();

            foreach (var item in pluginItems)
            {
                var pluginItem = item;

                if (!TryValidatePluginDependencies(pluginItems, ref pluginItem, out var missingPlugins))
                {
                    // We have some missing dependencies.
                    // Future fix would be to download them, but instead for now let's
                    // just warn the user it's missing
                    foreach (var missingPlugin in missingPlugins)
                        _log.Warn($"{item.Manifest.Name} is missing dependency {missingPlugin}. Skipping plugin.");

                    continue;
                }

                pluginsToLoad.Add(pluginItem);
            }

            if (Torch.Config.ShouldUpdatePlugins && DownloadPluginUpdates(pluginsToLoad))
            {
                // Resort the plugins just in case updates changed load hints.
                pluginItems = GetLocalPlugins(PluginDir);
                pluginsToLoad.Clear();

                foreach (var item in pluginItems)
                {
                    var pluginItem = item;

                    if (!TryValidatePluginDependencies(pluginItems, ref pluginItem, out var missingPlugins))
                    {
                        foreach (var missingPlugin in missingPlugins)
                            _log.Warn($"{item.Manifest.Name} is missing dependency {missingPlugin}. Skipping plugin.");

                        continue;
                    }

                    pluginsToLoad.Add(pluginItem);
                }
            }

            // Sort based on dependencies.
            try
            {
                pluginsToLoad = pluginsToLoad.TSort(item => item.ResolvedDependencies)
                    .ToList();
            }
            catch (Exception e)
            {
                // This will happen on cylic dependencies.
                _log.Error(e);
            }

            if (_reloadList.Count > 0)
            {
                foreach (var item in _pluginItems)
                {
                    LoadPlugin(item);
                }

                foreach (var plugin in _plugins.Values)
                {
                    plugin.Init(Torch);
                }
            }
            else
            {
                foreach (var plugin in pluginsToLoad)
                {
                    _pluginItems.Add(plugin);
                    LoadPlugin(plugin);
                }

                foreach (var plugin in _plugins.Values)
                {
                    plugin.Init(Torch);
                }
            }

            _reloadList.Clear();


            _log.Info($"Loaded {_plugins.Count} plugins.");
            PluginsLoaded?.Invoke(_plugins.Values.AsReadOnly());
        }

        //debug flag is set when the user asks us to run with a specific plugin for plugin development debug
        //please do not change references to this arg unless you are very sure you know what you're doing
        private List<PluginItem> GetLocalPlugins(string pluginDir, bool debug = false)
        {
            var firstLoad = Torch.Config.Plugins.Count == 0;

            var pluginItems = Directory.EnumerateFiles(pluginDir, "*.zip")
                .Union(Directory.EnumerateDirectories(pluginDir));

            if (debug)
                pluginItems = pluginItems.Union(new List<string> { pluginDir });

            var results = new List<PluginItem>();

            foreach (var item in pluginItems)
            {
                var path = item;

                if (!Path.IsPathRooted(path))
                    path = Path.Combine(pluginDir, item);

                var isZip = item.EndsWith(".zip", StringComparison.CurrentCultureIgnoreCase);
                var manifest = isZip ? GetManifestFromZip(path) : GetManifestFromDirectory(path);

                if (manifest == null)
                {
                    if (!debug)
                    {
                        _log.Warn($"Item '{item}' is missing a manifest, skipping.");
                        continue;
                    }

                    manifest = new PluginManifest {
                        Guid = new Guid(),
                        Version = "0",
                        Name = "TEST"
                    };
                }

                var duplicatePlugin = results.FirstOrDefault(r => r.Manifest.Guid == manifest.Guid);

                if (duplicatePlugin != null)
                {
                    _log.Warn(
                        $"The GUID provided by {manifest.Name} ({item}) is already in use by {duplicatePlugin.Manifest.Name}.");
                    continue;
                }

                if (!Torch.Config.LocalPlugins && !debug)
                {
                    if (isZip && !Torch.Config.Plugins.Contains(manifest.Guid))
                    {
                        if (!firstLoad)
                        {
                            _log.Warn($"Plugin {manifest.Name} ({item}) exists in the plugin directory, but is not listed in torch.cfg. Skipping load!");
                            continue;
                        }
                        _log.Info($"First-time load: Plugin {manifest.Name} added to torch.cfg.");
                        Torch.Config.Plugins.Add(manifest.Guid);
                    }
                }

                results.Add(new PluginItem {
                    Filename = item,
                    IsZip = isZip,
                    Manifest = manifest,
                    Path = path
                });
            }

            if (!Torch.Config.LocalPlugins && firstLoad)
                Torch.Config.Save();

            return results;
        }

        private bool DownloadPluginUpdates(List<PluginItem> plugins)
        {
            _log.Info("Checking for plugin updates...");

            int count = 0;

            Task.WaitAll(plugins.Select(DownloadPluginUpdateAsync).ToArray());

            _log.Info($"Updated {count} plugins.");

            return count > 0;

            async Task DownloadPluginUpdateAsync(PluginItem item)
            {
                try
                {
                    if (!item.IsZip)
                    {
                        _log.Warn($"Unzipped plugins cannot be auto-updated. Skipping plugin {item.Manifest.Name}");
                        return;
                    }

                    item.Manifest.Version.TryExtractVersion(out Version currentVersion);

                    var latest = await PluginQuery.Instance.QueryOne(item.Manifest.Guid).ConfigureAwait(false);

                    if (latest?.LatestVersion == null)
                    {
                        _log.Warn($"Plugin {item.Manifest.Name} does not have any releases on torchapi.com. Cannot update.");
                        return;
                    }

                    latest.LatestVersion.TryExtractVersion(out Version newVersion);

                    if (currentVersion == null || newVersion == null)
                    {
                        _log.Error($"Error parsing version from manifest or website for plugin '{item.Manifest.Name}.'");
                        return;
                    }

                    if (newVersion <= currentVersion)
                    {
                        _log.Debug($"{item.Manifest.Name} {item.Manifest.Version} is up to date.");
                        return;
                    }

                    _log.Info($"Updating plugin '{item.Manifest.Name}' from {currentVersion} to {newVersion}.");

                    await PluginQuery.Instance.DownloadPlugin(latest, item.Path).ConfigureAwait(false);

                    Interlocked.Increment(ref count);
                }
                catch (Exception e)
                {
                    _log.Warn($"An error occurred updating the plugin {item.Manifest.Name}.");
                    _log.Warn(e);
                }
            }
        }

#nullable enable

        private void LoadPlugin(PluginItem item)
        {
            var assemblies = new List<Assembly>();
            //var loaded = AppDomain.CurrentDomain.GetAssemblies();

            var assemblyFiles = new Dictionary<string, (string? FilePath, byte[] AsmData, byte[]? SymbolData)>();

            if (item.IsZip)
            {
                using (var zipFile = ZipFile.OpenRead(item.Path))
                {
                    foreach (var entry in zipFile.Entries)
                    {
                        if (!entry.Name.EndsWith(".dll", StringComparison.CurrentCultureIgnoreCase))
                            continue;

                        //if (loaded.Any(a => entry.Name.Contains(a.GetName().Name)))
                        //    continue;

                        using (var stream = entry.Open())
                        {
                            var data = stream.ReadToEnd((int)entry.Length);
                            byte[]? symbol = null;

                            var symbolEntryName = entry.FullName.Substring(0, entry.FullName.Length - "dll".Length) + "pdb";
                            var symbolEntry = zipFile.GetEntry(symbolEntryName);

                            if (symbolEntry != null)
                            {
                                try
                                {
                                    using (var symbolStream = symbolEntry.Open())
                                        symbol = symbolStream.ReadToEnd((int)symbolEntry.Length);
                                }
                                catch (Exception e)
                                {
                                    _log.Warn(e, $"Failed to read debugging symbols from {item.Filename}:{symbolEntryName}");
                                }
                            }

                            assemblyFiles.Add(Path.GetFileNameWithoutExtension(entry.Name), (null, data, symbol));
                        }
                    }
                }
            }
            else
            {
                var files = Directory
                    .EnumerateFiles(item.Path, "*.*", SearchOption.AllDirectories)
                    .ToList();

                foreach (var file in files)
                {
                    if (!file.EndsWith(".dll", StringComparison.CurrentCultureIgnoreCase))
                        continue;

                    //if (loaded.Any(a => file.Contains(a.GetName().Name)))
                    //    continue;

                    using (var stream = File.OpenRead(file))
                    {
                        var data = stream.ReadToEnd();
                        byte[]? symbol = null;

                        var symbolPath = Path.Combine(Path.GetDirectoryName(file) ?? ".",
                            Path.GetFileNameWithoutExtension(file) + ".pdb");

                        if (File.Exists(symbolPath))
                        {
                            try
                            {
                                using (var symbolStream = File.OpenRead(symbolPath))
                                    symbol = symbolStream.ReadToEnd();
                            }
                            catch (Exception e)
                            {
                                _log.Warn(e, $"Failed to read debugging symbols from {symbolPath}");
                            }
                        }

                        assemblyFiles.Add(Path.GetFileNameWithoutExtension(file), (file, data, symbol));
                    }
                }
            }

            foreach (var ad in assemblyFiles)
            {
                var data = ad.Value.AsmData;
                var symbol = ad.Value.SymbolData;

                var assembly = LoadAssembly(data, ref symbol, ad.Key, ad.Value.FilePath, assemblyFiles);

                if (assembly != null)
                    assemblies.Add(assembly);
            }

            RegisterAllAssemblies(assemblies);
            InstantiatePlugin(item.Manifest, assemblies);
        }

#if NET8_0_OR_GREATER
        class PluginCecilAssemblyResolver : Mono.Cecil.BaseAssemblyResolver
        {
            readonly Dictionary<string, Mono.Cecil.AssemblyDefinition> cache;
            readonly Dictionary<string, (string?, byte[], byte[]?)> assemblyFiles;
            readonly Mono.Cecil.ReaderParameters readerParameters;

            public PluginCecilAssemblyResolver(Dictionary<string, (string?, byte[], byte[]?)> assemblyFiles)
            {
                cache = new Dictionary<string, Mono.Cecil.AssemblyDefinition>(StringComparer.Ordinal);
                this.assemblyFiles = assemblyFiles;
                readerParameters = new Mono.Cecil.ReaderParameters { InMemory = true, AssemblyResolver = this };
            }

            public override Mono.Cecil.AssemblyDefinition Resolve(Mono.Cecil.AssemblyNameReference name)
            {
                Mono.Cecil.AssemblyDefinition? assembly;

                if (cache.TryGetValue(name.FullName, out assembly))
                    return assembly;

                if (assemblyFiles.TryGetValue(name.Name, out var assemData))
                    assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(assemData.Item2), readerParameters);
                else
                    assembly = base.Resolve(name);

                cache[name.FullName] = assembly;

                return assembly;
            }

            protected override void Dispose(bool disposing)
            {
                foreach (var assembly in cache.Values)
                    assembly.Dispose();

                cache.Clear();

                base.Dispose(disposing);
            }
        }
#endif

        static Assembly? LoadAssembly(byte[] data, ref byte[]? symbolData, string assemblyName, string? originalFilePath,
            Dictionary<string, (string? FilePath, byte[] AsmData, byte[]? SymbolData)> assemblyFiles)
        {
            Assembly? assembly;

            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                assembly = null;
            }

            if (assembly != null)
                return assembly;

#if NET8_0_OR_GREATER
            string? newFilePath = null;

            // TODO: PDB?
            try
            {
                PatchPluginAssemblyIfNeeded(ref data, assemblyName, assemblyFiles, out newFilePath);
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Failed to pre-patch plugin assembly '{assemblyName}'.");
            }

            if (newFilePath != null)
            {
                assembly = Assembly.LoadFrom(newFilePath);
            }
            else
#endif

            if (originalFilePath != null)
            {
                assembly = Assembly.LoadFrom(originalFilePath);
            }
            else
            {
                assembly = symbolData != null
                    ? Assembly.Load(data, symbolData)
                    : Assembly.Load(data);
            }

            return assembly;
        }

#if NET8_0_OR_GREATER
        static bool PatchPluginAssemblyIfNeeded(ref byte[] data, string assemblyName,
            Dictionary<string, (string? FilePath, byte[] AsmData, byte[]? SymbolData)> assemblyFiles,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? newFilePath)
        {
            var assemResolver = new PluginCecilAssemblyResolver(assemblyFiles);
            assemResolver.AddSearchDirectory("DedicatedServer64");

            var readerParams = new Mono.Cecil.ReaderParameters() {
                InMemory = true,
                AssemblyResolver = assemResolver
            };

            using var assemblyDef = Mono.Cecil.AssemblyDefinition.ReadAssembly(new MemoryStream(data), readerParams);
            bool assemblyModified = false;

            foreach (var typeDef in assemblyDef.MainModule.Types)
            {
                foreach (var fieldDef in typeDef.Fields)
                {
                    if (!fieldDef.IsStatic || !fieldDef.IsInitOnly)
                        continue;

                    bool hasReflectedMemberAttrib = false;

                    foreach (var attrib in fieldDef.CustomAttributes)
                    {
                        switch (attrib.AttributeType.FullName)
                        {
                        case "Torch.Utils.ReflectedMethodAttribute":
                        case "Torch.Utils.ReflectedStaticMethodAttribute":
                        case "Torch.Utils.ReflectedGetterAttribute":
                        case "Torch.Utils.ReflectedSetterAttribute":
                        case "Torch.Utils.ReflectedFieldInfoAttribute":
                        case "Torch.Utils.ReflectedPropertyInfoAttribute":
                        case "Torch.Utils.ReflectedMethodInfoAttribute":
                        case "Torch.Utils.ReflectedEventReplaceAttribute":
                            hasReflectedMemberAttrib = true;
                            break;
                        }
                    }

                    if (hasReflectedMemberAttrib)
                    {
                        // Cannot set readonly static fields via reflection in newer .net runtimes.
                        // Change to non-readonly.
                        fieldDef.IsInitOnly = false;
                        assemblyModified = true;
                    }
                }
            }

            if (assemblyModified)
            {
                Directory.CreateDirectory("PatchedAssemblies");

                newFilePath = Path.Combine("PatchedAssemblies", assemblyName + ".dll");

                //using (var stream = new MemoryStream(data.Length))
                using (var stream = File.OpenWrite(newFilePath))
                {
                    assemblyDef.Write(stream);
                    //data = stream.ToArray();
                }
            }
            else
            {
                newFilePath = null;
            }

            return assemblyModified;
        }
#endif

#nullable restore

        private void RegisterAllAssemblies(IReadOnlyCollection<Assembly> assemblies)
        {
            Assembly ResolveDependentAssembly(object sender, ResolveEventArgs args)
            {
                var requiredAssemblyName = new AssemblyName(args.Name);
                foreach (Assembly asm in assemblies)
                {
                    if (IsAssemblyCompatible(requiredAssemblyName, asm.GetName()))
                        return asm;
                }
                if (requiredAssemblyName.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                    return null;
                foreach (var asm in assemblies)
                {
                    if (asm == args.RequestingAssembly)
                    {
                        _log.Warn($"Couldn't find dependency! {args.RequestingAssembly} depends on {requiredAssemblyName}.");
                        break;
                    }
                }
                return null;
            }

            AppDomain.CurrentDomain.AssemblyResolve += ResolveDependentAssembly;

            foreach (Assembly asm in assemblies)
            {
                TorchBase.RegisterAuxAssembly(asm);
            }
        }

        private static bool IsAssemblyCompatible(AssemblyName a, AssemblyName b)
        {
            int aMajor = a.Version?.Major ?? 0;
            int aMinor = a.Version?.Minor ?? 0;

            return a.Name == b.Name && aMajor == b.Version.Major && aMinor == b.Version.Minor;
        }

        public void ReloadPlugins()
        {
            _log.Info("Reloading plugins.");

            var plugins = _plugins.ToList();

            if (!Torch.Config.BypassIsReloadableFlag)
                plugins = plugins.Where(p => p.Value.IsReloadable).ToList();

            foreach (var plugin in plugins)
            {
                _reloadList.Add(plugin.Key);
                plugin.Value?.Dispose();
                _plugins.Remove(plugin.Key);
            }

            LoadPlugins();
            PluginsReloaded?.Invoke();
        }

        public void ReloadPlugin(Guid guid)
        {
            var plugin = _plugins[guid];

            plugin.Dispose();
            _plugins.Remove(guid);
            _log.Info($"{plugin.Name} {plugin.Version} has been unloaded.");

            LoadPlugin(_pluginItems.First(p => p.Manifest.Guid == guid));
            _log.Info($"{plugin.Name} {plugin.Version} has been reloaded.");
        }

        private void InstantiatePlugin(PluginManifest manifest, IEnumerable<Assembly> assemblies)
        {
            Type pluginType = null;
            bool mult = false;
            foreach (var asm in assemblies)
            {
                foreach (var type in asm.GetExportedTypes())
                {
                    if (!type.GetInterfaces().Contains(typeof(ITorchPlugin)))
                        continue;

                    if (type.IsAbstract)
                        continue;

                    _log.Info($"Loading plugin at {type.FullName}");

                    if (pluginType != null)
                    {
                        //_log.Error($"The plugin '{manifest.Name}' has multiple implementations of {nameof(ITorchPlugin)}, not loading.");
                        //return;
                        mult = true;
                        continue;
                    }

                    pluginType = type;
                }
            }

            if (mult)
            {
                _log.Error($"The plugin '{manifest.Name}' has multiple implementations of {nameof(ITorchPlugin)}, not loading.");
                return;
            }

            if (pluginType == null)
            {
                _log.Error($"The plugin '{manifest.Name}' does not have an implementation of {nameof(ITorchPlugin)}, not loading.");
                return;
            }

#pragma warning disable CS0618 // Type or member is obsolete

            // Backwards compatibility for PluginAttribute.
            var pluginAttr = pluginType.GetCustomAttribute<PluginAttribute>();

            if (pluginAttr != null)
            {
                _log.Warn($"Plugin '{manifest.Name}' is using the obsolete {nameof(PluginAttribute)}, using info from attribute if necessary.");

#pragma warning restore CS0618

                manifest.Version ??= pluginAttr.Version.ToString();
                manifest.Name ??= pluginAttr.Name;

                if (manifest.Guid == default)
                    manifest.Guid = pluginAttr.Guid;
            }

            _log.Info($"Loading plugin '{manifest.Name}' ({manifest.Version})");

            TorchPluginBase plugin;
            try
            {
                plugin = (TorchPluginBase)Activator.CreateInstance(pluginType);
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Plugin {manifest.Name} threw an exception during instantiation! Not loading!");
                return;
            }
            plugin.Manifest = manifest;
            plugin.StoragePath = Torch.Config.InstancePath;
            plugin.Torch = Torch;
            _plugins.Add(manifest.Guid, plugin);
        }

        private PluginManifest GetManifestFromZip(string path)
        {
            try
            {
                using (var zipFile = ZipFile.OpenRead(path))
                {
                    foreach (var entry in zipFile.Entries)
                    {
                        if (!entry.Name.Equals(MANIFEST_NAME, StringComparison.CurrentCultureIgnoreCase))
                            continue;

                        using (var stream = new StreamReader(entry.Open()))
                        {
                            return PluginManifest.Load(stream);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, $"Error opening zip! File is likely corrupt. File at {path} will be deleted and re-acquired on the next restart!");
                File.Delete(path);
            }

            return null;
        }

        private bool TryValidatePluginDependencies(List<PluginItem> items, ref PluginItem item, out List<Guid> missingDependencies)
        {
            var dependencies = new List<PluginItem>();
            missingDependencies = new List<Guid>();

            foreach (var pluginDependency in item.Manifest.Dependencies)
            {
                var dependency = items
                    .FirstOrDefault(pi => pi?.Manifest.Guid == pluginDependency.Plugin);
                if (dependency == null)
                {
                    missingDependencies.Add(pluginDependency.Plugin);
                    continue;
                }

                if (!string.IsNullOrEmpty(pluginDependency.MinVersion)
                    && dependency.Manifest.Version.TryExtractVersion(out var dependencyVersion)
                    && pluginDependency.MinVersion.TryExtractVersion(out var minVersion))
                {
                    // really only care about version if it is defined.
                    if (dependencyVersion < minVersion)
                    {
                        // If dependency version is too low, we can try to update. Otherwise
                        // it's a missing dependency.

                        // For now let's just warn the user. bitMuse is lazy.
                        _log.Warn($"{dependency.Manifest.Name} is below the requested version for {item.Manifest.Name}."
                        + Environment.NewLine
                        + $" Desired version: {pluginDependency.MinVersion}, Available version: {dependency.Manifest.Version}");
                        missingDependencies.Add(pluginDependency.Plugin);
                        continue;
                    }
                }

                dependencies.Add(dependency);
            }

            item.ResolvedDependencies = dependencies;

            return missingDependencies.Count <= 0;
        }

        private PluginManifest GetManifestFromDirectory(string directory)
        {
            var path = Path.Combine(directory, MANIFEST_NAME);
            return !File.Exists(path) ? null : PluginManifest.Load(path);
        }

        /// <inheritdoc cref="IEnumerable.GetEnumerator"/>
        public IEnumerator<ITorchPlugin> GetEnumerator() => _plugins.Values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
