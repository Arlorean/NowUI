using System.Text.RegularExpressions;
using System.Text.Json;
using System.Reflection;

namespace NowUI.Cli;

/// <summary>Connects the selected scene without imposing additional C# construction requirements.</summary>
internal static class BrowserEntrySource
{
    internal const string SceneFactoryMarker = "/*NOWUI_SCENE_FACTORY*/";
    internal const string SceneAssemblyAlias = "nowui_scene";

    internal static string Create(string template, string sceneName, string setup = "")
        => Create(template, new SceneFactoryOptions(sceneName, "", SceneFactoryMode.Direct), setup);

    internal static string Create(string template, SceneFactoryOptions scene, string setup = "")
    {
        if (template.IndexOf(SceneFactoryMarker, StringComparison.Ordinal) < 0 ||
            template.IndexOf(SceneFactoryMarker, StringComparison.Ordinal) != template.LastIndexOf(SceneFactoryMarker, StringComparison.Ordinal))
            throw new InvalidDataException("The browser kit must contain exactly one scene factory marker. Rebuild the kit and CLI together.");
        string prefix = "", suffix = "", factory;
        if (scene.Mode == SceneFactoryMode.Reflection)
        {
            // Some exported CLR names and obsolete-error types cannot be referenced by C# source.
            factory = "static () => (global::NowUI.Hosting.INowScene)global::System.Activator.CreateInstance(" +
                "global::System.Reflection.Assembly.Load(new global::System.Reflection.AssemblyName(" +
                JsonSerializer.Serialize(scene.AssemblyName) + ")).GetType(" + JsonSerializer.Serialize(scene.SceneName) + ", throwOnError: true)!)!";
        }
        else
        {
            // The alias also handles applications whose scene's full name exists in another dependency.
            prefix = "extern alias " + SceneAssemblyAlias + ";\n";
            string name = TypeName(scene.SceneName).Replace("global::", SceneAssemblyAlias + "::", StringComparison.Ordinal);
            if (scene.Mode == SceneFactoryMode.ConstructorAccessor)
            {
                factory = "static () => NowUISceneFactory.Create()";
                suffix = "\nfile static class NowUISceneFactory\n{\n" +
                    "    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]\n" +
                    "    internal static extern " + name + " Create();\n}\n";
            }
            else factory = "static () => new " + name + "()";
        }
        return prefix + setup + template.Replace(SceneFactoryMarker, factory, StringComparison.Ordinal) + suffix;
    }

    internal static string ReflectionRegistration(IEnumerable<string> assemblyNames) =>
        string.Concat(assemblyNames.Order(StringComparer.Ordinal).Select(name =>
            "global::NowUI.Engine.NowRuntime.RegisterAssembly(global::System.Reflection.Assembly.Load(" +
            "new global::System.Reflection.AssemblyName(" + JsonSerializer.Serialize(name) + ")));\n"));

    internal static string TypeName(string fullName)
    {
        // SceneAssembly already requires an exported, non-generic class with a public default constructor.
        // Escape every C# identifier, including keywords, and reject metadata names C# cannot spell.
        string[] names = fullName.Split(['.', '+']);
        if (names.Any(name => !Regex.IsMatch(name, @"\A[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Nd}\p{Pc}\p{Mn}\p{Mc}\p{Cf}]*\z")))
            throw new InvalidDataException("The selected scene type cannot be represented in generated C#: " + fullName);
        return "global::" + string.Join(".", names.Select(name => "@" + name));
    }
}

internal enum SceneFactoryMode { Direct, ConstructorAccessor, Reflection }

internal sealed record SceneFactoryOptions(string SceneName, string AssemblyName, SceneFactoryMode Mode)
{
    internal static SceneFactoryOptions FromType(Type scene)
    {
        var mode = SceneFactoryMode.Direct;
        for (Type? declaring = scene; declaring != null; declaring = declaring.DeclaringType)
        {
            if (ObsoleteError(declaring) || !CSharpIdentifier(declaring.Name)) mode = SceneFactoryMode.Reflection;
        }
        if (scene.Namespace?.Split('.').Any(name => !CSharpIdentifier(name)) == true) mode = SceneFactoryMode.Reflection;
        if (mode != SceneFactoryMode.Reflection)
        {
            var constructor = scene.GetConstructor(Type.EmptyTypes)
                ?? throw new InvalidDataException("The selected scene must have a public parameterless constructor.");
            bool required = false;
            for (Type? type = scene; type != null; type = type.BaseType)
                required |= HasAttribute(type, "System.Runtime.CompilerServices.RequiredMemberAttribute");
            if (ObsoleteError(constructor) || required && !HasAttribute(constructor, "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute"))
                mode = SceneFactoryMode.ConstructorAccessor;
        }
        return new(scene.FullName!, scene.Assembly.GetName().Name!, mode);
    }

    // Formatting characters are discarded by the C# compiler and cannot identify an exact CLR name.
    static bool CSharpIdentifier(string name) => Regex.IsMatch(name, @"\A[_\p{L}\p{Nl}][_\p{L}\p{Nl}\p{Nd}\p{Pc}\p{Mn}\p{Mc}]*\z");
    static bool HasAttribute(MemberInfo member, string name) => member.CustomAttributes.Any(attribute => attribute.AttributeType.FullName == name);
    static bool ObsoleteError(MemberInfo member) => member.CustomAttributes.Any(attribute =>
        attribute.AttributeType.FullName == "System.ObsoleteAttribute" && attribute.ConstructorArguments.Count == 2 &&
        attribute.ConstructorArguments[1].Value is true);
}
