#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Sandbox;
using Sandbox.Engine.Utils;
using Torch.API;
using Torch.Managers;
using Torch.Mod;
using Torch.Server.ViewModels;
using Torch.Utils;
using VRage.FileSystem;
using VRage.Game;
using VRage.ObjectBuilders.Private;

namespace Torch.Server.Managers
{
    public class InstanceManager : Manager
    {
        private const string CONFIG_NAME = "SpaceEngineers-Dedicated.cfg";

        public event Action<ConfigDedicatedViewModel>? InstanceLoaded;

        public ConfigDedicatedViewModel DedicatedConfig { get; set; } = null!;

        private static readonly Logger Log = LogManager.GetLogger(nameof(InstanceManager));

        public InstanceManager(ITorchBase torchInstance)
            : base(torchInstance) { }

        public void LoadInstance(string path, bool validate = true)
        {
            Log.Info($"Loading instance {path}");

            if (validate)
                ValidateInstance(path);

            MyFileSystem.Reset();
            MyFileSystem.Init("Content", path);
            //Initializes saves path. Why this isn't in Init() we may never know.
            MyFileSystem.InitUserSpecific(null);

            // why?....
            // var configPath = Path.Combine(path, CONFIG_NAME);
            // if (!File.Exists(configPath))
            // {
            //     Log.Error($"Failed to load dedicated config at {path}");
            //     return;
            // }

            // var config = new MyConfigDedicated<MyObjectBuilder_SessionSettings>(configPath);
            // config.Load(configPath);

            DedicatedConfig = new ConfigDedicatedViewModel(this, (MyConfigDedicated<MyObjectBuilder_SessionSettings>)MySandboxGame.ConfigDedicated);

            var worldFolders = Directory.EnumerateDirectories(Path.Combine(Torch.Config.InstancePath, "Saves"));

            foreach (var f in worldFolders)
            {
                try
                {
                    if (!string.IsNullOrEmpty(f) && File.Exists(Path.Combine(f, "Sandbox.sbc")))
                        DedicatedConfig.Worlds.Add(new WorldViewModel(f));
                }
                catch (Exception)
                {
                    Log.Error("Failed to load world at path: " + f);
                    continue;
                }
            }

            if (DedicatedConfig.Worlds.Count == 0)
            {
                Log.Warn($"No worlds found in the current instance {path}.");
                return;
            }

            if (DedicatedConfig.LoadWorld != null)
                SelectWorld(DedicatedConfig.LoadWorld);
            else
                SelectWorld(DedicatedConfig.Worlds.First());

            InstanceLoaded?.Invoke(DedicatedConfig);
        }

        public void SelectWorld(string worldPath)
        {
            var config = DedicatedConfig;
            config.LoadWorld = worldPath;

            var world = config.Worlds.FirstOrDefault(x => x.WorldPath == worldPath);

            if (world?.Checkpoint == null)
            {
                try
                {
                    world = new WorldViewModel(worldPath);
                    config.Worlds.Add(world);
                }
                catch (Exception)
                {
                    Log.Error("Failed to load world at path: " + worldPath);
                    config.LoadWorld = null;
                    return;
                }
            }

            UpdateSelectedWorld(world, updateView: true);
        }

        public void SelectWorld(WorldViewModel world, bool updateView = true)
        {
            var config = DedicatedConfig;
            config.LoadWorld = world.WorldPath;

            UpdateSelectedWorld(world, updateView);
        }

        void UpdateSelectedWorld(WorldViewModel world, bool updateView)
        {
            var config = DedicatedConfig;

            if (updateView)
                config.UpdateSelectedWorld(world);

            if (world.Checkpoint == null)
                return;

            config.SessionSettings = world.WorldConfiguration.Settings;
            config.Mods.Clear();

            // remove the Torch mod to avoid running multiple copies of it
            world.WorldConfiguration.Mods.RemoveAll(m => m.PublishedFileId == TorchModCore.MOD_ID);

            foreach (var m in world.WorldConfiguration.Mods)
                config.Mods.Add(new ModItemInfo(m));

            Task.Run(() => config.UpdateAllModInfosAsync());
        }

        public void SaveConfig()
        {
            if (((TorchServer)Torch).HasRun)
            {
                Log.Warn("Checkpoint cache is stale, not saving dedicated config.");
                return;
            }

            DedicatedConfig.Save(Path.Combine(Torch.Config.InstancePath, CONFIG_NAME));
            Log.Info("Saved dedicated config.");

            try
            {
                var world = DedicatedConfig.Worlds.FirstOrDefault(x => x.WorldPath == DedicatedConfig.LoadWorld) ?? new WorldViewModel(DedicatedConfig.LoadWorld);

                world.Checkpoint.SessionName = DedicatedConfig.WorldName;
                world.WorldConfiguration.Settings = DedicatedConfig.SessionSettings;
                world.WorldConfiguration.Mods.Clear();

                foreach (var mod in DedicatedConfig.Mods)
                {
                    var savedMod = ModItemUtils.Create(mod.PublishedFileId);
                    savedMod.IsDependency = mod.IsDependency;
                    savedMod.Name = mod.Name;
                    savedMod.FriendlyName = mod.FriendlyName;

                    world.WorldConfiguration.Mods.Add(savedMod);
                }

                Task.Run(() => DedicatedConfig.UpdateAllModInfosAsync());

                world.SaveSandbox();

                Log.Info("Saved world config.");
            }
            catch (Exception e)
            {
                Log.Error("Failed to write sandbox config, changes will not appear on server");
                Log.Error(e);
            }
        }

        /// <summary>
        /// Ensures that the given path is a valid server instance.
        /// </summary>
        private static void ValidateInstance(string path)
        {
            Directory.CreateDirectory(Path.Combine(path, "Saves"));
            Directory.CreateDirectory(Path.Combine(path, "Mods"));

            var configPath = Path.Combine(path, CONFIG_NAME);

            if (File.Exists(configPath))
                return;

            var config = new MyConfigDedicated<MyObjectBuilder_SessionSettings>(configPath);
            config.Save(configPath);
        }
    }

    public class WorldViewModel : ViewModel
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        public string FolderName { get; set; }
        public string WorldPath { get; }
        public long WorldSizeKB { get; }
        private string _checkpointPath;
        private string _worldConfigPath;

        public CheckpointViewModel Checkpoint { get; private set; }
        public WorldConfigurationViewModel WorldConfiguration { get; private set; }

        public WorldViewModel(string worldPath)
        {
            try
            {
                WorldPath = worldPath;
                WorldSizeKB = new DirectoryInfo(worldPath).GetFiles().Sum(x => x.Length) / 1024;
                _checkpointPath = Path.Combine(WorldPath, "Sandbox.sbc");
                _worldConfigPath = Path.Combine(WorldPath, "Sandbox_config.sbc");
                FolderName = Path.GetFileName(worldPath);

                LoadSandbox();
            }
            catch (ArgumentException)
            {
                Log.Error($"World view model failed to load the path: {worldPath} Please ensure this is a valid path.");
                throw; //rethrow to be handled further up the stack
            }
        }

        public void SaveSandbox()
        {
            MyObjectBuilderSerializerKeen.SerializeXML(_checkpointPath, false, Checkpoint);
            MyObjectBuilderSerializerKeen.SerializeXML(_worldConfigPath, false, WorldConfiguration);
        }

        [MemberNotNull(nameof(Checkpoint), nameof(WorldConfiguration))]
        private void LoadSandbox()
        {
            MyObjectBuilderSerializerKeen.DeserializeXML(_checkpointPath, out MyObjectBuilder_Checkpoint checkpoint);
            Checkpoint = new CheckpointViewModel(checkpoint);

            // migrate old saves
            if (File.Exists(_worldConfigPath))
            {
                MyObjectBuilderSerializerKeen.DeserializeXML(_worldConfigPath, out MyObjectBuilder_WorldConfiguration worldConfig);
                WorldConfiguration = new WorldConfigurationViewModel(worldConfig);
            }
            else
            {
                WorldConfiguration = new WorldConfigurationViewModel(new MyObjectBuilder_WorldConfiguration {
                    Mods = checkpoint.Mods,
                    Settings = checkpoint.Settings
                });

                checkpoint.Mods = null;
                checkpoint.Settings = null;
            }
        }
    }
}
