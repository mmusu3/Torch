using System;
using System.IO;
using System.Threading;
using Sandbox.Engine.Multiplayer;
using Sandbox.Game.Entities;
using Sandbox.Game.World;

namespace Torch.Utils;

public static class MiscExtensions
{
    private static readonly ThreadLocal<WeakReference<byte[]>> _streamBuffer = new ThreadLocal<WeakReference<byte[]>>(() => new WeakReference<byte[]>(null));

    private static bool TryGetLength(this Stream stream, out long length)
    {
        length = 0;

        if (!stream.CanSeek)
            return false;

        try
        {
            length = stream.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static byte[] ReadToEnd(this Stream stream, int optionalDataLength = -1)
    {
        const int minBufferSize = 1024;

        long streamLength;

        if (!stream.TryGetLength(out streamLength))
            streamLength = minBufferSize;

        long initialBufferSize = optionalDataLength > 0 ? optionalDataLength : streamLength;

        if (initialBufferSize < minBufferSize)
            initialBufferSize = minBufferSize;

        byte[] buffer;

        if (!_streamBuffer.Value.TryGetTarget(out buffer) || buffer.Length < initialBufferSize)
            buffer = new byte[initialBufferSize];

        int streamPosition = 0;

        do
        {
            int toRead = buffer.Length - streamPosition;
            int bytesRead = stream.Read(buffer, streamPosition, toRead);

            if (bytesRead == 0)
                break;

            streamPosition += bytesRead;

            if (streamPosition == buffer.Length)
            {
                if (CheckEndOfStreamOrResizeBuffer(stream, streamPosition, ref buffer, streamLength))
                    break;
            }
        }
        while (true);

        byte[] result;

        if (buffer.Length == streamPosition)
        {
            result = buffer;
            _streamBuffer.Value.SetTarget(null);
        }
        else
        {
            result = new byte[streamPosition];
            Array.Copy(buffer, 0, result, 0, result.Length);
            _streamBuffer.Value.SetTarget(buffer);
        }

        return result;
    }

    private static bool CheckEndOfStreamOrResizeBuffer(Stream stream, int streamPosition, ref byte[] buffer, long maxBufferSize)
    {
#if NETFRAMEWORK
        var tempSmallBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1);
        int bytesRead = stream.Read(tempSmallBuffer, 0, 1);
#else
        Span<byte> tempSmallBuffer = stackalloc byte[1];
        int bytesRead = stream.Read(tempSmallBuffer);
#endif

        if (bytesRead == 0)
        {
#if NETFRAMEWORK
            System.Buffers.ArrayPool<byte>.Shared.Return(tempSmallBuffer);
#endif
            return true;
        }

        int newSize = Math.Max((int)maxBufferSize, buffer.Length * 2);
        Array.Resize(ref buffer, newSize);

        buffer[streamPosition] = tempSmallBuffer[0];

#if NETFRAMEWORK
        System.Buffers.ArrayPool<byte>.Shared.Return(tempSmallBuffer);
#endif
        return false;
    }

    public static string GetGridOwnerName(this MyCubeGrid grid)
    {
        if (grid.BigOwners.Count == 0 || grid.BigOwners[0] == 0)
            return "nobody";

        var identityId = grid.BigOwners[0];
        var session = MySession.Static;

        if (session.Players.IdentityIsNpc(identityId))
        {
            var identity = session.Players.TryGetIdentity(identityId);
            return identity.DisplayName;
        }
        else
        {
            return MyMultiplayer.Static.GetMemberName(session.Players.TryGetSteamId(identityId));
        }
    }
}