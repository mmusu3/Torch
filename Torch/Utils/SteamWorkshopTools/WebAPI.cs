#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using SteamKit2;

namespace Torch.Utils.SteamWorkshopTools;

public class WebAPI
{
    private static Logger Log = LogManager.GetLogger("SteamWorkshopService");

    public const uint AppID = 244850U;

    public string Username { get; private set; }
    private string password;
    public bool IsReady { get; private set; }
    public bool IsRunning { get; private set; }

    private TaskCompletionSource<bool> logonTaskCompletionSource;

    private SteamClient steamClient;
    private CallbackManager cbManager;
    private SteamUser steamUser;

    private static WebAPI? _instance;
    public static WebAPI Instance => _instance ??= new WebAPI();

    private WebAPI()
    {
        steamClient = new SteamClient();
        cbManager = new CallbackManager(steamClient);

        IsRunning = true;
    }

    public async Task<bool> Logon(string user = "anonymous", string pw = "")
    {
        if (string.IsNullOrEmpty(user)) throw new ArgumentNullException(nameof(user), "User can't be null!");
        if (!user.Equals("anonymous") && !pw.Equals("")) throw new ArgumentNullException(nameof(pw), "Password can't be null if user is not anonymous!");

        Username = user;
        password = pw;

        logonTaskCompletionSource = new TaskCompletionSource<bool>();

        steamUser = steamClient.GetHandler<SteamUser>();
        cbManager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
        cbManager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
        cbManager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        cbManager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);

        Log.Info("Connecting to Steam...");

        steamClient.Connect();

        bool result = await logonTaskCompletionSource.Task;

        return result;
    }

    public void CancelLogon()
    {
        logonTaskCompletionSource?.SetCanceled();
    }

    public async Task<Dictionary<ulong, PublishedItemDetails>?> GetPublishedFileDetails(IEnumerable<ulong> workshopIds)
    {
        if (logonTaskCompletionSource != null)
            await logonTaskCompletionSource.Task;

        var workshopIdsArray = workshopIds.ToArray();

        using (var remoteStorage = SteamKit2.WebAPI.GetAsyncInterface("ISteamRemoteStorage"))
        {
            KeyValue? allFilesDetails = null;
            remoteStorage.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                var ifaceArgs = new Dictionary<string, object?>();
                ifaceArgs["itemcount"] = workshopIdsArray.Length.ToString();

                for (int i = 0; i < workshopIdsArray.Length; i++)
                    ifaceArgs[$"publishedfileids[{i}]"] = workshopIdsArray[i].ToString();

                allFilesDetails = await remoteStorage.CallAsync(HttpMethod.Post, "GetPublishedFileDetails", args: ifaceArgs);
            }
            catch (HttpRequestException e)
            {
                Log.Error($"Fetching File Details failed: {e.Message}");
                return null;
            }

            if (allFilesDetails == null || !allFilesDetails.TryGetInt("result", out int requestResult))
                return null;

            if (requestResult > 1)
            {
                Log.Error($"Fetching File Details failed. Got result code: {(EResult)requestResult}");
                return null;
            }

            var detailsList = allFilesDetails.Find("publishedfiledetails")?.Children;

            if (detailsList == null)
            {
                Log.Error("Received invalid data: ");
#if DEBUG
                if (allFilesDetails != null)
                    PrintKeyValue(allFilesDetails);
#endif

                return null;
            }

            int resultCount = allFilesDetails.GetIntOrDefault("resultcount");

            if (detailsList.Count != workshopIdsArray.Length || resultCount != workshopIdsArray.Length)
            {
                Log.Error($"Received unexpected number of fileDetails. Expected: {workshopIdsArray.Length}, Received: {resultCount}");
                return null;
            }

            var itemDetails = new Dictionary<ulong, PublishedItemDetails>();

            for (int i = 0; i < resultCount; i++)
            {
                var fileDetails = detailsList[i];

                if (!fileDetails.TryGetULong("publishedfileid", out ulong publishedFileId)
                    || !fileDetails.TryGetInt("result", out int itemResult))
                {
                    continue;
                }

                if (itemResult > 1)
                {
                    Log.Error($"Fetching File Details failed for item ID: {publishedFileId}. Got result code: {(EResult)itemResult}");
                    continue;
                }

                var tags = Array.Empty<string>();
                var tagContainer = fileDetails.Children.Find(item => item.Name == "tags");

                if (tagContainer != null)
                {
                    var tagList = new List<string>();

                    foreach (var tagKv in tagContainer.Children)
                    {
                        var tag = tagKv.Children.Find(item => item.Name == "tag")?.Value;

                        if (tag != null)
                            tagList.Add(tag);
                    }

                    tags = tagList.ToArray();
                }

                itemDetails[publishedFileId] = new PublishedItemDetails() {
                    PublishedFileId = publishedFileId,
                    Views           = fileDetails.GetUIntOrDefault("views"),
                    Subscriptions   = fileDetails.GetUIntOrDefault("subscriptions"),
                    TimeUpdated     = DateTimeOffset.FromUnixTimeSeconds(fileDetails.GetLongOrDefault("time_updated")).DateTime,
                    TimeCreated     = DateTimeOffset.FromUnixTimeSeconds(fileDetails.GetLongOrDefault("time_created")).DateTime,
                    Description     = fileDetails.Find("description")?.Value,
                    Title           = fileDetails.Find("title")?.Value,
                    FileUrl         = fileDetails.Find("file_url")?.Value,
                    FileSize        = fileDetails.GetLongOrDefault("file_size"),
                    FileName        = fileDetails.Find("filename")?.Value,
                    ConsumerAppId   = fileDetails.GetULongOrDefault("consumer_app_id"),
                    CreatorAppId    = fileDetails.GetULongOrDefault("creator_app_id"),
                    Creator         = fileDetails.GetULongOrDefault("creator"),
                    Tags            = tags
                };
            }

            return itemDetails;
        }
    }

    [Obsolete("Space Engineers has transitioned to Steam's UGC api, therefore this method might not always work!")]
    public async Task DownloadPublishedFile(PublishedItemDetails fileDetails, string dir, string? name = null)
    {
        //var fullPath = Path.Combine(dir, name);

        name ??= fileDetails.FileName;

        //var expectedSize = (fileDetails.FileSize == 0) ? -1 : fileDetails.FileSize;

        using (var client = new WebClient())
        {
            try
            {
                var downloadTask = client.DownloadFileTaskAsync(fileDetails.FileUrl, Path.Combine(dir, name));

                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);

                    if (downloadTask.IsCompleted)
                        break;
                }

                if (!downloadTask.IsCompleted)
                {
                    client.CancelAsync();
                    throw new Exception("Timeout while attempting to downloading published workshop item!");
                }

                //var text = await client.DownloadStringTaskAsync(url);
                //File.WriteAllText(fullPath, text);
            }
            catch (Exception e)
            {
                Log.Error("Failed to download workshop item! /n" +
                    $"{e.Message} - url: {fileDetails.FileUrl}, path: {Path.Combine(dir, name)}");

                throw;
            }
        }

    }

    class Printable
    {
        public KeyValue Data;
        public int Offset;

        public Printable(KeyValue data, int offset)
        {
            Data = data;
            Offset = offset;
        }

        public void Print()
        {
            Log.Info($"{new string(' ', Offset)}{Data.Name}: {Data.Value}");
        }
    }

    private static void PrintKeyValue(KeyValue data)
    {
        var dataSet = new Stack<Printable>();
        dataSet.Push(new Printable(data, 0));

        while (dataSet.Count != 0)
        {
            var printable = dataSet.Pop();

            foreach (var child in printable.Data.Children)
                dataSet.Push(new Printable(child, printable.Offset + 2));

            printable.Print();
        }
    }

    #region CALLBACKS

    private void OnConnected(SteamClient.ConnectedCallback callback)
    {
        Log.Info("Connected to Steam! Logging in '{0}'...", Username);

        if (Username == "anonymous")
        {
            steamUser.LogOnAnonymous();
        }
        else
        {
            steamUser.LogOn(new SteamUser.LogOnDetails {
                Username = Username,
                Password = password
            });
        }
    }

    private void OnDisconnected(SteamClient.DisconnectedCallback callback)
    {
        Log.Info("Disconnected from Steam");

        IsReady = false;
        IsRunning = false;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        if (callback.Result != EResult.OK)
        {
            string msg;

            if (callback.Result == EResult.AccountLogonDenied)
            {
                msg = "Unable to logon to Steam: This account is Steamguard protected.";
                Log.Warn(msg);
                logonTaskCompletionSource.SetException(new Exception(msg));

                IsRunning = false;
                return;
            }

            msg = $"Unable to logon to Steam: {callback.Result} / {callback.ExtendedResult}";
            Log.Warn(msg);
            logonTaskCompletionSource.SetException(new Exception(msg));

            IsRunning = false;
            return;
        }

        IsReady = true;

        Log.Info("Successfully logged on!");
        logonTaskCompletionSource.SetResult(true);
    }

    private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
    {
        IsReady = false;
        Log.Info($"Logged off of Steam: {callback.Result}");
    }

    #endregion
}
