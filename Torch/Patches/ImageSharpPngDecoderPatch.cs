#if NET6_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using SixLabors.ImageSharp.PixelFormats;
using Torch.Managers.PatchManager;
using Torch.Managers.PatchManager.MSIL;

namespace Torch.Patches;

[PatchShim]
public static class ImageSharpPngDecoderPatch
{
    public static void Patch(PatchContext ctx)
    {
        var transpiler = typeof(ImageSharpPngDecoderPatch).GetMethod(nameof(Transpile_DecodePixelData), BindingFlags.Static | BindingFlags.NonPublic);
        var pngDecoderType = typeof(SixLabors.ImageSharp.Formats.Png.PngDecoder).Assembly.GetType("SixLabors.ImageSharp.Formats.Png.PngDecoderCore");
        var source = pngDecoderType.GetMethod("DecodePixelData", BindingFlags.Instance | BindingFlags.NonPublic);

        Transpile<Rgba32>();
        Transpile<Gray16>();
        Transpile<Gray8>();

        source = pngDecoderType.GetMethod("DecodeInterlacedPixelData", BindingFlags.Instance | BindingFlags.NonPublic);

        Transpile<Rgba32>();
        Transpile<Gray16>();
        Transpile<Gray8>();

        void Transpile<T>() => ctx.GetPattern(source.MakeGenericMethod(typeof(T))).Transpilers.Add(transpiler);
    }

    static IEnumerable<MsilInstruction> Transpile_DecodePixelData(IEnumerable<MsilInstruction> instructions)
    {
        var patchReadMethod = typeof(ImageSharpPngDecoderPatch).GetMethod(nameof(StreamRead), BindingFlags.Static | BindingFlags.NonPublic);

        foreach (var i in instructions)
        {
            if (i.OpCode == OpCodes.Callvirt && i.Operand is MsilOperandInline<MethodBase> call && call.Value.Name == "Read")
            {
                var replacedCall = new MsilInstruction(OpCodes.Call).InlineValue(patchReadMethod);

                foreach (var l in i.Labels)
                    replacedCall.Labels.Add(l);

                yield return replacedCall;
                continue;
            }

            yield return i;
        }
    }

    static int StreamRead(Stream stream, byte[] buffer, int offset, int count)
    {
        return stream.ReadAtLeast(buffer.AsSpan(offset, count), count, throwOnEndOfStream: false);
    }
}
#endif
