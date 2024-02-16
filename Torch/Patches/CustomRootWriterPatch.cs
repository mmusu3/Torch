using System.Reflection;
using Torch.Managers.PatchManager;
using VRage;

namespace Torch.Patches;

//[PatchShim] // Manually registered
public static class CustomRootWriterPatch
{
    public static void Patch(PatchContext ctx)
    {
        var source = typeof(CustomRootWriter).GetMethod("Init", BindingFlags.Instance | BindingFlags.NonPublic);
        var replacement = typeof(CustomRootWriterPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Prefixes.Add(replacement);
    }

    static bool Prefix(string customRootType, System.Xml.XmlWriter target,
        ref System.Xml.XmlWriter __field_m_target, ref string __field_m_customRootType, ref int __field_m_currentDepth)
    {
        __field_m_target = target;
        __field_m_customRootType = customRootType;

        target.WriteAttributeString("type", System.Xml.Schema.XmlSchema.InstanceNamespace, customRootType);

        __field_m_currentDepth = 0;

        return false;
    }
}
