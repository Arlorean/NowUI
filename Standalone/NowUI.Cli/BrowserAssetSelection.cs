using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace NowUI.Cli;

/// <summary>Uses compiler-emitted strings without executing a scene or trying to parse C# syntax.</summary>
internal static class BrowserAssetSelection
{
    internal static readonly HashSet<string> HostAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "NowUI.Engine", "NowUI.Runtime", "NowUI.Hosting", "NowUI.Browser", "NowUI.Desktop", "nowui",
        "NowUI.Extensions.CodeEditor", "NowUI.Extensions.Docking", "NowUI.Extensions.Markdown", "NowUI.Extensions.Markdown.Markup",
        "NowUI.Extensions.Markup", "NowUI.Extensions.NodeGraph", "NowUI.Extensions.Sdf", "YamlDotNet", "AssetsTools.NET", "StbImageSharp"
    };
    static readonly Dictionary<short, OpCode> Opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode)).Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(opcode => opcode.Value);

    internal static bool IsHostAssembly(string name) => HostAssemblies.Contains(name);

    internal static IReadOnlyCollection<string> FromAssemblies(IEnumerable<string> assemblyPaths)
    {
        var roots = new HashSet<string>(StringComparer.Ordinal);
        foreach (string path in assemblyPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (IsHostAssembly(name) || name == "mscorlib" || name == "netstandard" || name == "System" || name.StartsWith("System.", StringComparison.Ordinal)) continue;
            roots.UnionWith(FromAssembly(path));
        }
        return roots.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }

    internal static IReadOnlyCollection<string> FromAssembly(string sceneAssemblyPath)
    {
        using var stream = File.OpenRead(sceneAssemblyPath);
        using var pe = new PEReader(stream);
        if (!pe.HasMetadata) throw new InvalidDataException("The browser scene must be a managed assembly: " + sceneAssemblyPath);
        var metadata = pe.GetMetadataReader();
        var strings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var methodHandle in metadata.MethodDefinitions)
        {
            var method = metadata.GetMethodDefinition(methodHandle);
            if (method.RelativeVirtualAddress == 0) continue;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader();
            while (body.RemainingBytes > 0)
            {
                byte first = body.ReadByte();
                short code = first == 0xfe ? (short)(0xfe00 | body.ReadByte()) : first;
                if (!Opcodes.TryGetValue(code, out var opcode)) throw new InvalidDataException("Invalid scene IL opcode.");
                if (opcode.OperandType == OperandType.InlineString)
                {
                    int token = body.ReadInt32();
                    strings.Add(metadata.GetUserString(MetadataTokens.UserStringHandle(token & 0x00ffffff)));
                    continue;
                }
                int count = opcode.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => checked(body.ReadInt32() * 4),
                    _ => 4
                };
                if (count < 0 || count > body.RemainingBytes) throw new InvalidDataException("Invalid scene IL operand.");
                body.Offset += count;
            }
        }
        return strings.OrderBy(value => value, StringComparer.Ordinal).ToArray();
    }
}
