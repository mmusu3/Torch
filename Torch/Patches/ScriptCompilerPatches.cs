using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sandbox.Engine.Utils;
using Torch.Managers.PatchManager;
using VRage.FileSystem;
using VRage.Scripting;

namespace Torch.Patches;

[PatchShim]
public static class MyScriptCompilerPatch
{
    static bool additionalReferencesAdded;

    public static void Patch(PatchContext ctx)
    {
        var source = typeof(MyScriptCompiler).GetMethod("AddReferencedAssemblies", BindingFlags.Instance | BindingFlags.Public);
        var replacement = typeof(MyScriptCompilerPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Prefixes.Add(replacement);
    }

    static bool Prefix(MyScriptCompiler __instance)
    {
        if (additionalReferencesAdded)
            return true;

        additionalReferencesAdded = true;

        __instance.AddReferencedAssemblies("./CompilerRefAssemblies/netstandard.dll");

        return false;
    }
}

[PatchShim]
public static class MyScriptWhitelistPatch
{
    public static void Patch(PatchContext ctx)
    {
        var source = typeof(MyScriptWhitelist).GetConstructor(BindingFlags.Instance | BindingFlags.Public, null, [ typeof(MyScriptCompiler) ], null);
        var replacement = typeof(MyScriptWhitelistPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Prefixes.Add(replacement);
    }

    static bool Prefix(MyScriptWhitelist __instance, MyScriptCompiler scriptCompiler,
        ref MyScriptCompiler __field_m_scriptCompiler, ref Dictionary<string, MyWhitelistTarget> __field_m_whitelist, ref HashSet<string> __field_m_ingameBlacklist)
    {
        __field_m_scriptCompiler = scriptCompiler;
        __field_m_whitelist = new Dictionary<string, MyWhitelistTarget>();
        __field_m_ingameBlacklist = new HashSet<string>();

        using (var batch = __instance.OpenBatch())
        {
            // AllowNamespaceOfTypes also captures assembly name information so multiple types in the
            // same namespace must be used to register different asemblies, eg. List<T> and LinkedList<T>.

            batch.AllowNamespaceOfTypes(MyWhitelistTarget.Both,
                typeof(System.Collections.IEnumerator), typeof(List<>), /*typeof(System.Collections.Generic.LinkedList<>), */
                typeof(System.Text.StringBuilder), typeof(System.Text.RegularExpressions.Regex), typeof(System.Globalization.Calendar));

            batch.AllowNamespaceOfTypes(MyWhitelistTarget.ModApi,
                typeof(System.Linq.Enumerable), /*typeof(System.Collections.Concurrent.ConcurrentQueue<>), */typeof(System.Collections.Concurrent.ConcurrentBag<>));

            batch.AllowTypes(MyWhitelistTarget.Ingame,
                typeof(System.Linq.Enumerable), typeof(System.Linq.IGrouping<,>), typeof(System.Linq.ILookup<,>),
                typeof(System.Linq.IOrderedEnumerable<>), typeof(System.Linq.Lookup<,>));

            batch.AllowNamespaceOfTypes(MyWhitelistTarget.ModApi, typeof(System.Timers.Timer));

            batch.AllowTypes(MyWhitelistTarget.ModApi,
                typeof(System.Diagnostics.TraceEventType),
                typeof(AssemblyProductAttribute),
                typeof(AssemblyDescriptionAttribute),
                typeof(AssemblyConfigurationAttribute),
                typeof(AssemblyCompanyAttribute),
                typeof(AssemblyCultureAttribute),
                typeof(AssemblyVersionAttribute),
                typeof(AssemblyFileVersionAttribute),
                typeof(AssemblyCopyrightAttribute),
                typeof(AssemblyTrademarkAttribute),
                typeof(AssemblyTitleAttribute),
                typeof(System.Runtime.InteropServices.ComVisibleAttribute),
                typeof(System.ComponentModel.DefaultValueAttribute),
                typeof(SerializableAttribute),
                typeof(System.Runtime.InteropServices.GuidAttribute),
                typeof(System.Runtime.InteropServices.StructLayoutAttribute),
                typeof(System.Runtime.InteropServices.LayoutKind),
                typeof(Guid));

            batch.AllowTypes(MyWhitelistTarget.Both,
                typeof(object), typeof(IDisposable), typeof(string), typeof(StringComparison), typeof(Math), typeof(Enum),
                typeof(int), typeof(short), typeof(long), typeof(uint), typeof(ushort), typeof(ulong), typeof(double), typeof(float),
                typeof(bool), typeof(char), typeof(byte), typeof(sbyte), typeof(decimal),
                typeof(DateTime), typeof(TimeSpan), typeof(Array),

                typeof(System.Xml.Serialization.XmlElementAttribute), typeof(System.Xml.Serialization.XmlAttributeAttribute), typeof(System.Xml.Serialization.XmlArrayAttribute),
                typeof(System.Xml.Serialization.XmlArrayItemAttribute), typeof(System.Xml.Serialization.XmlAnyAttributeAttribute), typeof(System.Xml.Serialization.XmlAnyElementAttribute),
                typeof(System.Xml.Serialization.XmlAnyElementAttributes), typeof(System.Xml.Serialization.XmlArrayItemAttributes), typeof(System.Xml.Serialization.XmlAttributeEventArgs),
                typeof(System.Xml.Serialization.XmlAttributeOverrides), typeof(System.Xml.Serialization.XmlAttributes), typeof(System.Xml.Serialization.XmlChoiceIdentifierAttribute),
                typeof(System.Xml.Serialization.XmlElementAttributes), typeof(System.Xml.Serialization.XmlElementEventArgs), typeof(System.Xml.Serialization.XmlEnumAttribute),
                typeof(System.Xml.Serialization.XmlIgnoreAttribute), typeof(System.Xml.Serialization.XmlIncludeAttribute), typeof(System.Xml.Serialization.XmlRootAttribute),
                typeof(System.Xml.Serialization.XmlTextAttribute), typeof(System.Xml.Serialization.XmlTypeAttribute),

                typeof(RuntimeHelpers), typeof(BinaryReader), typeof(BinaryWriter),
                typeof(NullReferenceException), typeof(ArgumentException), typeof(ArgumentNullException), typeof(InvalidOperationException), typeof(FormatException),
                typeof(Exception), typeof(DivideByZeroException), typeof(InvalidCastException), typeof(FileNotFoundException), typeof(NotSupportedException),

                typeof(Nullable<>), typeof(StringComparer), typeof(IEquatable<>), typeof(IComparable), typeof(IComparable<>), typeof(BitConverter), typeof(FlagsAttribute),
                typeof(Path), typeof(Random), typeof(Convert), typeof(StringSplitOptions), typeof(DateTimeKind), typeof(MidpointRounding), typeof(EventArgs), typeof(Buffer),

                typeof(System.ComponentModel.INotifyPropertyChanging), typeof(System.ComponentModel.PropertyChangingEventHandler), typeof(System.ComponentModel.PropertyChangingEventArgs),
                typeof(System.ComponentModel.INotifyPropertyChanged), typeof(System.ComponentModel.PropertyChangedEventHandler), typeof(System.ComponentModel.PropertyChangedEventArgs));

            batch.AllowTypes(MyWhitelistTarget.ModApi, typeof(Stream), typeof(TextWriter), typeof(TextReader));
            batch.AllowMembers(MyWhitelistTarget.Both, typeof(MemberInfo).GetProperty("Name"));

            batch.AllowMembers(MyWhitelistTarget.Both,
                typeof(Type).GetProperty("FullName"), typeof(Type).GetMethod("GetTypeFromHandle"),
                typeof(Type).GetMethod("GetFields", [typeof(BindingFlags)]),
                typeof(Type).GetMethod("IsEquivalentTo"),
                typeof(Type).GetMethod("op_Equality"),
                typeof(Type).GetMethod("op_Inequality"),
                typeof(Type).GetMethod("ToString"));

            batch.AllowMembers(MyWhitelistTarget.Both, typeof(ValueType).GetMethod("Equals"), typeof(ValueType).GetMethod("GetHashCode"), typeof(ValueType).GetMethod("ToString"));
            batch.AllowMembers(MyWhitelistTarget.Both, typeof(Environment).GetProperty("CurrentManagedThreadId", BindingFlags.Static | BindingFlags.Public), typeof(Environment).GetProperty("NewLine", BindingFlags.Static | BindingFlags.Public), typeof(Environment).GetProperty("ProcessorCount", BindingFlags.Static | BindingFlags.Public));
            batch.AllowMembers(MyWhitelistTarget.Both, (from m in AllDeclaredMembers(typeof(Delegate)) where m.Name != "CreateDelegate" select m).ToArray());

            batch.AllowTypes(MyWhitelistTarget.Both,
                typeof(Action), typeof(Action<>), typeof(Action<,>), typeof(Action<,,>), typeof(Action<,,,>), typeof(Action<,,,,>), typeof(Action<,,,,,>),
                typeof(Action<,,,,,,>), typeof(Action<,,,,,,,>), typeof(Action<,,,,,,,,>), typeof(Action<,,,,,,,,,>), typeof(Action<,,,,,,,,,,>),
                typeof(Action<,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,,,>), typeof(Action<,,,,,,,,,,,,,,,>),
                typeof(Func<>), typeof(Func<,>), typeof(Func<,,>), typeof(Func<,,,>), typeof(Func<,,,,>), typeof(Func<,,,,,>), typeof(Func<,,,,,,>), typeof(Func<,,,,,,,>),
                typeof(Func<,,,,,,,,>), typeof(Func<,,,,,,,,,>), typeof(Func<,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,>),
                typeof(Func<,,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,,,>), typeof(Func<,,,,,,,,,,,,,,,,>));
        }

        return false;
    }

    static IEnumerable<MemberInfo> AllDeclaredMembers(Type type)
    {
        return from m in type.GetMembers(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public)
               where !IsPropertyMethod(m)
               select m;

        static bool IsPropertyMethod(MemberInfo memberInfo)
        {
            if (memberInfo is MethodInfo methodInfo && methodInfo.IsSpecialName)
                return methodInfo.Name.StartsWith("get_") || methodInfo.Name.StartsWith("set_");

            return false;
        }
    }
}

public static class MyScriptWhitelistBatchPatches
{
    static Type batchType = typeof(MyScriptWhitelist).GetNestedType("Batch", BindingFlags.NonPublic)!;
    static ConditionalWeakTable<object, Microsoft.CodeAnalysis.CSharp.CSharpCompilation> compilations = new();

    [PatchShim]
    public static class Patch1
    {
        public static void Patch(PatchContext ctx)
        {
            var source = batchType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [ typeof(MyScriptWhitelist) ], null);
            var replacement = typeof(Patch1).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

            ctx.GetPattern(source).Prefixes.Add(replacement);
        }

        static bool Prefix(object __instance, MyScriptWhitelist whitelist)
        {
            batchType.GetProperty("Whitelist", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(__instance, whitelist);

            var compilation = (Microsoft.CodeAnalysis.CSharp.CSharpCompilation)typeof(MyScriptWhitelist)
                .GetMethod("CreateCompilation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(whitelist, [])!;

#if NETCOREAPP2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
            compilations.AddOrUpdate(__instance, compilation);
#else
            _ = compilations.Remove(__instance);
            compilations.Add(__instance, compilation);
#endif

            return false;
        }
    }

    [PatchShim]
    public static class Patch2
    {
        public static void Patch(PatchContext ctx)
        {
            var source = batchType.GetMethod("ResolveTypeSymbol", BindingFlags.Instance | BindingFlags.NonPublic, null, [ typeof(Type) ], null);
            var replacement = typeof(Patch2).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

            ctx.GetPattern(source).Prefixes.Add(replacement);
        }

        static bool Prefix(object __instance, Type type, ref Microsoft.CodeAnalysis.INamedTypeSymbol __result)
        {
            if (compilations.TryGetValue(__instance, out var compilation))
            {
                Microsoft.CodeAnalysis.INamedTypeSymbol symbol = compilation.GetTypeByMetadataName(type.FullName);

                if (symbol == null)
                    throw new MyWhitelistException(string.Format("Cannot add {0} to the batch because its symbol variant could not be found.", type.FullName));

                __result = symbol;

                return false;
            }

            return true;
        }
    }
}

[PatchShim]
static class MySandboxGamePatch
{
    public static void Patch(PatchContext ctx)
    {
        var source = typeof(Sandbox.MySandboxGame).GetMethod("InitIlCompiler", BindingFlags.Instance | BindingFlags.NonPublic);
        var replacement = typeof(MySandboxGamePatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic);

        ctx.GetPattern(source).Prefixes.Add(replacement);
    }

    static bool Prefix()
    {
        InitIlCompiler();
        return false;
    }

    static void InitIlCompiler()
    {
        var assemblyReferences = new string[] {
            "./CompilerRefAssemblies/System.Memory.dll",
            "./CompilerRefAssemblies/System.Collections.Immutable.dll",

            Path.Combine(MyFileSystem.ExePath, "Sandbox.Game.dll"),
            Path.Combine(MyFileSystem.ExePath, "Sandbox.Common.dll"),
            Path.Combine(MyFileSystem.ExePath, "Sandbox.Graphics.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Library.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Math.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Game.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Render.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Input.dll"),
            Path.Combine(MyFileSystem.ExePath, "VRage.Scripting.dll"),
            Path.Combine(MyFileSystem.ExePath, "SpaceEngineers.ObjectBuilders.dll"),
            Path.Combine(MyFileSystem.ExePath, "SpaceEngineers.Game.dll"),
            Path.Combine(MyFileSystem.ExePath, "ProtoBuf.Net.Core.dll")
        };

        var referencedTypes = new Type[] {
            typeof(VRage.MyTuple),
            typeof(VRageMath.Vector2),
            typeof(VRage.Game.Game),
            typeof(Sandbox.ModAPI.Interfaces.ITerminalAction),
            typeof(Sandbox.ModAPI.Ingame.IMyGridTerminalSystem),
            typeof(Sandbox.Game.EntityComponents.MyModelComponent),
            typeof(VRage.Game.Components.IMyComponentAggregate),
            typeof(VRage.Collections.ListReader<>),
            typeof(VRage.Game.ObjectBuilders.Definitions.MyObjectBuilder_FactionDefinition),
            typeof(VRage.Game.ModAPI.Ingame.IMyCubeBlock),
            typeof(VRage.Game.ModAPI.Ingame.Utilities.MyIni),
            typeof(System.Collections.Immutable.ImmutableArray),
            typeof(SpaceEngineers.Game.ModAPI.Ingame.IMyAirVent),
            typeof(VRage.Game.GUI.TextPanel.MySprite)
        };

        var symbols = new string[] {
            GetPrefixedBranchName(),
            "STABLE",
            string.Empty,
            string.Empty,
            "VERSION_" + ((Version)VRage.Game.MyFinalBuildConstants.APP_VERSION).Minor,
            "BUILD_" + ((Version)VRage.Game.MyFinalBuildConstants.APP_VERSION).Build
        };

        var diagnosticsPath = MyFakes.ENABLE_ROSLYN_SCRIPT_DIAGNOSTICS ? Path.Combine(MyFileSystem.UserDataPath, "ScriptDiagnostics") : null;

        VRage.MyVRage.Platform.Scripting.Initialize(Sandbox.MySandboxGame.Static.UpdateThread,
            assemblyReferences, referencedTypes, symbols, diagnosticsPath, MyFakes.ENABLE_SCRIPTS_PDB);
    }

    static string GetPrefixedBranchName()
    {
        string branchName = Sandbox.Engine.Networking.MyGameService.BranchName;
        branchName = string.IsNullOrEmpty(branchName) ? "STABLE" : System.Text.RegularExpressions.Regex.Replace(branchName, "[^a-zA-Z0-9_]", "_").ToUpper();
        return "BRANCH_" + branchName;
    }
}
