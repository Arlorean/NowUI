using System.Globalization;
using System.Reflection;
using System.Text;

namespace NowUI.ApiDump;

/// <summary>
/// Writes an assembly's public API surface to a sorted, diffable text file.
///
/// The engine-free core split has to prove two things, and both are diffs of this output:
///   1. Unity's NowUI.Runtime public contract is byte-for-byte unchanged by the split (exit criterion D8(d)).
///   2. The standalone build's divergence from it is exactly the expected set, recorded in StandaloneApiDelta.md.
///
/// Loading is metadata-only. NowUI.Runtime references UnityEngine assemblies that cannot be loaded or executed in a
/// plain console process, and executing the assembly under inspection would be wrong regardless.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? assemblyPath = null;
        string? outputPath = null;
        var includeInternals = false;
        var referencePaths = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--assembly" when i + 1 < args.Length: assemblyPath = args[++i]; break;
                case "--output" when i + 1 < args.Length: outputPath = args[++i]; break;
                case "--reference-path" when i + 1 < args.Length: referencePaths.Add(args[++i]); break;
                case "--include-internals": includeInternals = true; break;
                default:
                    Console.Error.WriteLine($"Unrecognised argument '{args[i]}'.");
                    return 2;
            }
        }

        if (string.IsNullOrWhiteSpace(assemblyPath))
        {
            Console.Error.WriteLine(
                "Usage: NowUI.ApiDump --assembly <path.dll> [--output <path.txt>] " +
                "[--reference-path <dir>]... [--include-internals]");
            return 2;
        }

        assemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Assembly not found at '{assemblyPath}'.");
            return 2;
        }

        outputPath = string.IsNullOrWhiteSpace(outputPath)
            ? Path.ChangeExtension(assemblyPath, ".api.txt")
            : Path.GetFullPath(outputPath);

        // Resolve references from the assembly's own folder plus this process's framework, and tolerate the rest:
        // an unresolvable reference must degrade to a printed type name, never to a crash.
        var searchPaths = new List<string>();
        var assemblyDirectory = Path.GetDirectoryName(assemblyPath);
        if (!string.IsNullOrEmpty(assemblyDirectory))
        {
            searchPaths.AddRange(Directory.GetFiles(assemblyDirectory, "*.dll"));
        }

        var frameworkDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(frameworkDirectory))
        {
            searchPaths.AddRange(Directory.GetFiles(frameworkDirectory, "*.dll"));
        }

        // Callers add Unity's managed folders here so UnityEngine types resolve to their real names rather than to "?".
        foreach (var referencePath in referencePaths)
        {
            if (Directory.Exists(referencePath))
            {
                searchPaths.AddRange(Directory.GetFiles(referencePath, "*.dll", SearchOption.AllDirectories));
            }
            else
            {
                Console.Error.WriteLine($"Warning: reference path '{referencePath}' does not exist and is ignored.");
            }
        }

        var resolver = new PathAssemblyResolver(searchPaths.Distinct(StringComparer.OrdinalIgnoreCase));
        using var context = new MetadataLoadContext(resolver);

        var assembly = context.LoadFromAssemblyPath(assemblyPath);

        Type?[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            types = exception.Types;
            var unloadable = exception.LoaderExceptions.Length;
            Console.Error.WriteLine($"Warning: {unloadable} type(s) could not be loaded and are omitted.");
        }

        var lines = new List<string>();
        var unresolved = 0;

        foreach (var type in types)
        {
            if (type is null || !IsVisibleType(type, includeInternals))
            {
                continue;
            }

            lines.Add($"{DescribeKind(type)} {type.FullName}");

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.DeclaredOnly;

            foreach (var member in type.GetMembers(flags))
            {
                if (!IsVisibleMember(member, includeInternals))
                {
                    continue;
                }

                string? line;
                try
                {
                    line = Describe(member);
                }
                catch (FileNotFoundException)
                {
                    // A signature naming a type from an assembly we cannot resolve. Emitting the member with an
                    // unresolved marker keeps the dump complete and the diff honest; dropping it would silently hide
                    // a member from the gate, which is the one thing this tool must never do.
                    unresolved++;
                    line = $"  <unresolved-signature> {member.MemberType} {member.Name}";
                }

                if (line is not null)
                {
                    lines.Add(line);
                }
            }
        }

        if (unresolved > 0)
        {
            Console.Error.WriteLine(
                $"Warning: {unresolved} member signature(s) could not be fully resolved and are marked " +
                "<unresolved-signature>. Pass --reference-path pointing at the missing assemblies for a complete dump.");
        }

        // Ordinal sort so the file is identical on every machine and the diff is stable.
        lines.Sort(StringComparer.Ordinal);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllLines(outputPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"{lines.Count.ToString(CultureInfo.InvariantCulture)} line(s) written to {outputPath}");
        return 0;
    }

    private static string DescribeKind(Type type)
    {
        if (type.IsInterface) return "interface";
        if (type.IsEnum) return "enum";
        if (type.IsValueType) return "struct";
        return "class";
    }

    private static bool IsVisibleType(Type type, bool includeInternals)
    {
        if (type.IsPublic || type.IsNestedPublic)
        {
            return true;
        }

        // A protected nested type is part of the contract for anyone deriving from the outer type.
        if (type.IsNestedFamily || type.IsNestedFamORAssem)
        {
            return true;
        }

        return includeInternals;
    }

    private static bool IsVisibleMember(MemberInfo member, bool includeInternals)
    {
        switch (member)
        {
            case MethodBase method:
                return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly || includeInternals;
            case FieldInfo field:
                return field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly || includeInternals;
            default:
                // Properties and events carry no accessibility of their own; their accessors were filtered above.
                return true;
        }
    }

    private static string? Describe(MemberInfo member)
    {
        switch (member)
        {
            case MethodInfo method:
                // Property and event accessors are already covered by their property/event line.
                if (method.IsSpecialName && IsAccessorName(method.Name))
                {
                    return null;
                }

                return $"  method {Describe(method.ReturnType)} {method.Name}({DescribeParameters(method)})";

            case ConstructorInfo constructor:
                return $"  ctor .ctor({DescribeParameters(constructor)})";

            case PropertyInfo property:
                var accessors = new List<string>(2);
                if (property.GetMethod is not null) accessors.Add("get");
                if (property.SetMethod is not null) accessors.Add("set");
                return $"  property {Describe(property.PropertyType)} {property.Name} {{{string.Join("; ", accessors)}}}";

            case FieldInfo field:
                return $"  field {Describe(field.FieldType)} {field.Name}";

            case EventInfo eventInfo:
                return $"  event {Describe(eventInfo.EventHandlerType)} {eventInfo.Name}";

            default:
                return null;
        }
    }

    private static bool IsAccessorName(string name) =>
        name.StartsWith("get_", StringComparison.Ordinal) ||
        name.StartsWith("set_", StringComparison.Ordinal) ||
        name.StartsWith("add_", StringComparison.Ordinal) ||
        name.StartsWith("remove_", StringComparison.Ordinal);

    private static string DescribeParameters(MethodBase method)
    {
        var parameters = method.GetParameters();
        if (parameters.Length == 0)
        {
            return string.Empty;
        }

        var parts = new string[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var prefix = string.Empty;

            if (parameter.ParameterType.IsByRef)
            {
                // in / ref / out are separate contracts; a dump that conflated them would hide a breaking change.
                if (parameter.IsOut) prefix = "out ";
                else if (parameter.IsIn) prefix = "in ";
                else prefix = "ref ";
            }

            parts[i] = prefix + Describe(parameter.ParameterType);
        }

        return string.Join(", ", parts);
    }

    private static string Describe(Type? type)
    {
        if (type is null)
        {
            return "?";
        }

        // ToString() on a MetadataLoadContext type is stable and includes generic arguments; strip the trailing '&'
        // that byref parameters carry, since the direction is already spelled out by the in/ref/out prefix.
        var text = type.ToString();
        return type.IsByRef && text.EndsWith("&", StringComparison.Ordinal)
            ? text[..^1]
            : text;
    }
}
