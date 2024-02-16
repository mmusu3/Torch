#nullable enable

using SteamKit2;

namespace Torch.Utils.SteamWorkshopTools;

public static class KeyValueExtensions
{
    public static KeyValue? Find(this KeyValue keyValue, string key)
    {
        return keyValue.Children?.Find((KeyValue item) => item.Name == key);
    }

    public static bool TryGetInt(this KeyValue keyValue, string key, out int value)
    {
        value = 0;

        var kv = Find(keyValue, key);

        if (kv == null)
            return false;

        if (int.TryParse(kv.Value, out value))
            return true;

        return false;
    }

    public static int GetIntOrDefault(this KeyValue keyValue, string key)
    {
        return TryGetInt(keyValue, key, out int value) ? value : default;
    }

    public static uint GetUIntOrDefault(this KeyValue keyValue, string key)
    {
        var kv = Find(keyValue, key);

        if (kv == null)
            return default;

        _ = uint.TryParse(kv.Value, out uint value);
        return value;
    }

    public static bool TryGetLong(this KeyValue keyValue, string key, out long value)
    {
        value = 0;

        var kv = Find(keyValue, key);

        if (kv == null)
            return false;

        if (long.TryParse(kv.Value, out value))
            return true;

        return false;
    }

    public static long GetLongOrDefault(this KeyValue keyValue, string key)
    {
        return TryGetLong(keyValue, key, out long value) ? value : default;
    }

    public static bool TryGetULong(this KeyValue keyValue, string key, out ulong value)
    {
        value = 0;

        var kv = Find(keyValue, key);

        if (kv == null)
            return false;

        if (ulong.TryParse(kv.Value, out value))
            return true;

        return false;
    }

    public static ulong GetULongOrDefault(this KeyValue keyValue, string key)
    {
        return TryGetULong(keyValue, key, out ulong value) ? value : default;
    }
}