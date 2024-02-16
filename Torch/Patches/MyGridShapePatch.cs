#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Torch.Managers.PatchManager;

namespace Torch.Patches;

[PatchShim]
static class MyGridShapePatch
{
    public static void Patch(PatchContext ctx)
    {
        var source = typeof(Sandbox.Game.Entities.Cube.MyGridShape).GetMethod("AddShapesFromCollector", BindingFlags.Instance | BindingFlags.NonPublic);
        var replacement = typeof(MyGridShapePatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Prefixes.Add(replacement);
    }

    public struct ShapeInfo
    {
        public int Count;
        public VRageMath.Vector3I Min;
        public VRageMath.Vector3I Max;
    }

    static FieldInfo shapeInfosField = Type.GetType("Sandbox.Game.Entities.Cube.MyCubeBlockCollector, Sandbox.Game")!
        .GetField("ShapeInfos", BindingFlags.Instance | BindingFlags.Public)!;

    static FieldInfo shapesField = Type.GetType("Sandbox.Game.Entities.Cube.MyCubeBlockCollector, Sandbox.Game")!
        .GetField("Shapes", BindingFlags.Instance | BindingFlags.Public)!;

    static bool Prefix(Havok.HkGridShape __field_m_root, object __field_m_blockCollector)
    {
        var shapeInfos = Unsafe.As<List<ShapeInfo>>(shapeInfosField.GetValue(__field_m_blockCollector)!);
        var shapes = (List<Havok.HkShape>)shapesField.GetValue(__field_m_blockCollector)!;

        AddShapesFromCollector(__field_m_root, shapeInfos, shapes);

        return false;
    }

#if NET5_0_OR_GREATER
    static void AddShapesFromCollector(Havok.HkGridShape gridShape, List<ShapeInfo> shapeInfos, List<Havok.HkShape> shapes)
    {
        int shapeCount = 0;
        bool showLimitNotif = false;

        for (int i = 0; i < shapeInfos.Count; i++)
        {
            var shapeInfo = shapeInfos[i];
            int newShapeCount = gridShape.ShapeCount + shapeInfo.Count;

            if (newShapeCount > Sandbox.Game.Entities.Cube.MyGridShape.MAX_SHAPE_COUNT)
            {
                showLimitNotif = true;

                if (newShapeCount >= 65536)
                    break;
            }

            var shapeSpan = CollectionsMarshal.AsSpan(shapes).Slice(shapeCount, shapeInfo.Count);

            shapeCount += shapeInfo.Count;

            gridShape.AddShapes(shapeSpan, new VRageMath.Vector3S(shapeInfo.Min), new VRageMath.Vector3S(shapeInfo.Max));
        }

        if (showLimitNotif)
            Sandbox.Game.Gui.MyHud.Notifications.Add(Sandbox.Game.Gui.MyNotificationSingletons.GridReachedPhysicalLimit);
    }
#else
    static void AddShapesFromCollector(Havok.HkGridShape gridShape, List<ShapeInfo> shapeInfos, List<Havok.HkShape> shapes)
    {
        Span<Havok.HkShape> shapeStackBuffer = stackalloc Havok.HkShape[16];
        Havok.HkShape[]? shapeHeapBuffer = null;

        int shapeCount = 0;
        bool showLimitNotif = false;

        for (int i = 0; i < shapeInfos.Count; i++)
        {
            var shapeInfo = shapeInfos[i];

            scoped Span<Havok.HkShape> shapeSpan;

            if (shapeInfo.Count <= shapeStackBuffer.Length)
            {
                shapeSpan = shapeStackBuffer.Slice(0, shapeInfo.Count);
            }
            else
            {
                if (shapeHeapBuffer == null || shapeHeapBuffer.Length < shapeInfo.Count)
                    shapeHeapBuffer = new Havok.HkShape[shapeInfo.Count];

                shapeSpan = shapeHeapBuffer.AsSpan(0, shapeInfo.Count);
            }

            for (int j = 0; j < shapeInfo.Count; j++)
                shapeSpan[j] = shapes[shapeCount + j];

            shapeCount += shapeInfo.Count;

            int newShapeCount = gridShape.ShapeCount + shapeInfo.Count;

            if (newShapeCount > Sandbox.Game.Entities.Cube.MyGridShape.MAX_SHAPE_COUNT)
            {
                showLimitNotif = true;

                if (newShapeCount >= 65536)
                    break;
            }

            gridShape.AddShapes(shapeSpan, new VRageMath.Vector3S(shapeInfo.Min), new VRageMath.Vector3S(shapeInfo.Max));
        }

        if (showLimitNotif)
            Sandbox.Game.Gui.MyHud.Notifications.Add(Sandbox.Game.Gui.MyNotificationSingletons.GridReachedPhysicalLimit);
    }
#endif
}
