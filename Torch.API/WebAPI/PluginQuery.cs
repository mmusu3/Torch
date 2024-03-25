using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NLog;

namespace Torch.API.WebAPI
{
    public class PluginQuery
    {
#if DEBUG
        private const string ALL_QUERY = "https://torchapi.com/api/plugins/?includeArchived=true";
#else
        private const string ALL_QUERY = "https://torchapi.com/api/plugins";
#endif
        private const string PLUGIN_QUERY = "https://torchapi.com/api/plugins/search/{0}";

        private readonly HttpClient _client;
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        private static PluginQuery _instance;
        public static PluginQuery Instance => _instance ??= new PluginQuery();

        private PluginQuery()
        {
#if NET8_0_OR_GREATER
            _client = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromSeconds(10) });
#else
            _client = new HttpClient();
#endif
        }

        public async Task<PluginResponse> QueryAll()
        {
            using var h = await _client.GetAsync(ALL_QUERY).ConfigureAwait(false);

            if (!h.IsSuccessStatusCode)
            {
                Log.Error($"Plugin query returned response {h.StatusCode}");
                return null;
            }

            var r = await h.Content.ReadAsStringAsync().ConfigureAwait(false);

            PluginResponse response;

            try
            {
                response = JsonConvert.DeserializeObject<PluginResponse>(r);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize plugin query response!");
                return null;
            }

            return response;
        }

        public Task<PluginFullItem> QueryOne(Guid guid)
        {
            return QueryOne(guid.ToString());
        }

        public async Task<PluginFullItem> QueryOne(string guid)
        {
            using var h = await _client.GetAsync(string.Format(PLUGIN_QUERY, guid)).ConfigureAwait(false);

            if (!h.IsSuccessStatusCode)
            {
                Log.Error($"Plugin query returned response {h.StatusCode}");
                return null;
            }

            var r = await h.Content.ReadAsStringAsync().ConfigureAwait(false);

            PluginFullItem response;

            try
            {
                response = JsonConvert.DeserializeObject<PluginFullItem>(r);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to deserialize plugin query response!");
                return null;
            }

            return response;
        }

        public Task<bool> DownloadPlugin(Guid guid, string path = null)
        {
            return DownloadPlugin(guid.ToString(), path);
        }

        public async Task<bool> DownloadPlugin(string guid, string path = null)
        {
            var item = await QueryOne(guid).ConfigureAwait(false);

            if (item == null)
                return false;

            return await DownloadPlugin(item, path).ConfigureAwait(false);
        }

        public async Task<bool> DownloadPlugin(PluginFullItem item, string path = null)
        {
            try
            {
                path ??= $"Plugins\\{item.Name}.zip";

                string relpath = Path.GetDirectoryName(path);

                Directory.CreateDirectory(relpath);

                using var h = await _client.GetAsync(string.Format(PLUGIN_QUERY, item.ID)).ConfigureAwait(false);

                string res = await h.Content.ReadAsStringAsync().ConfigureAwait(false);
                var response = JsonConvert.DeserializeObject<PluginFullItem>(res);

                if (response.Versions.Length == 0)
                {
                    Log.Error($"Selected plugin {item.Name} does not have any versions to download!");
                    return false;
                }

                var version = response.Versions.FirstOrDefault(v => v.Version == response.LatestVersion);

                if (version == null)
                {
                    Log.Error($"Could not find latest version for selected plugin {item.Name}");
                    return false;
                }

                using var s = await _client.GetStreamAsync(version.URL).ConfigureAwait(false);

                if (File.Exists(path))
                    File.Delete(path);

                using (var f = File.Create(path))
                {
                    await s.CopyToAsync(f).ConfigureAwait(false);
                    await f.FlushAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to download plugin!");
            }

            return true;
        }
    }

    public class PluginResponse
    {
        public PluginItem[] Plugins;
        public int Count;
    }

    public class PluginItem
    {
        public string ID;
        public string Name { get; set; }
        public string Author;
        public string Description;
        public string LatestVersion;
        public bool Installed { get; set; } = false;

        public override string ToString()
        {
            return Name;
        }
    }

    public class PluginFullItem : PluginItem
    {
        public VersionItem[] Versions;
    }

    public class VersionItem
    {
        public string Version;
        public string Note;
        public bool IsBeta;
        public string URL;
    }
}
