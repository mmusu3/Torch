using System;
using System.Reflection;
using Torch.Managers.PatchManager;

namespace Torch.Patches;

[PatchShim]
static class MyTypeTablePatch
{
    public static void Patch(PatchContext ctx)
    {
        var source = typeof(VRage.Network.MyTypeTable).GetMethod("IsSerializableClass", BindingFlags.Static | BindingFlags.NonPublic);
        var replacement = typeof(MyTypeTablePatch).GetMethod(nameof(Postfix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Suffixes.Add(replacement);
    }

    static void Postfix(Type type, ref bool __result)
    {
        if (type == typeof(Delegate) || type == typeof(MulticastDelegate))
            __result = true;
    }
}
