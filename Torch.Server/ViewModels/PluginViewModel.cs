using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using NLog;
using Torch.API.Plugins;
using Torch.Server.Views;

namespace Torch.Server.ViewModels
{
    public class PluginViewModel
    {
        public UserControl Control { get; }
        public string Name { get; }
        public ITorchPlugin Plugin { get; }

        private static Logger _log = LogManager.GetCurrentClassLogger();

        public PluginViewModel(ITorchPlugin plugin)
        {
            Plugin = plugin;

            if (Plugin is IWpfPlugin p)
            {
                try
                {
                    Control = p.GetControl();
                }
                catch (InvalidOperationException)
                {
                    //ignore as its likely a hot reload, we can figure out a better solution in the future.
                    Control = null;
                }
                catch (Exception ex)
                {
                    _log.Error(ex, $"Exception loading interface for plugin {Plugin.Name}! Plugin interface will not be available!");
                    Control = null;
                }
            }

            Name = $"{plugin.Name} ({plugin.Version})";

            ThemeControl.UpdateDynamicControls += UpdateResourceDict;
            UpdateResourceDict(ThemeControl.currentTheme);
        }

        public void UpdateResourceDict(ResourceDictionary dictionary)
        {
            if (Control == null)
                return;

            Control.Resources.MergedDictionaries.Clear();
            Control.Resources.MergedDictionaries.Add(dictionary);
        }

        public Brush Color
        {
            get
            {
                switch (Plugin.State)
                {
                case PluginState.NotInitialized:
                case PluginState.MissingDependency:
                case PluginState.DisabledError:
                    return Brushes.Red;
                case PluginState.UpdateRequired:
                    return Brushes.DodgerBlue;
                case PluginState.UninstallRequested:
                    return Brushes.Gold;
                case PluginState.NotInstalled:
                case PluginState.DisabledUser:
                    return Brushes.Gray;
                case PluginState.Enabled:
                    return Brushes.Transparent;
                default:
                    throw new ArgumentOutOfRangeException();
                }
            }
        }

        public string ToolTip
        {
            get
            {
                return Plugin.State switch {
                    PluginState.NotInitialized     => "Error during load.",
                    PluginState.DisabledError      => "Disabled due to error on load.",
                    PluginState.DisabledUser       => "Disabled.",
                    PluginState.UpdateRequired     => "Update required.",
                    PluginState.UninstallRequested => "Marked for uninstall.",
                    PluginState.NotInstalled       => "Not installed. Click 'Enable'",
                    PluginState.Enabled            => string.Empty,
                    PluginState.MissingDependency  => "Dependency missing. Check the log.",
                    _ => throw new ArgumentOutOfRangeException(),
                };
            }
        }
    }
}
