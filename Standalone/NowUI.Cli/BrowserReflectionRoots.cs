using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace NowUI.Cli;

/// <summary>Preserves the host's reflection contracts without rooting entire NowUI assemblies.</summary>
internal static class BrowserReflectionRoots
{
    // These are the only CLR types selected by NowUnitySerializedAssets.KnownScripts.
    static readonly string[] AssetTypes =
    [
        "NowUI.NowFont", "NowUI.NowFontFamily", "NowUI.NowThemeAsset", "NowUI.NowControlRenderer",
        "NowUI.NowMaterialControlRenderer", "NowUI.NowUnityEditorControlRenderer", "NowUI.NowLottieAsset"
    ];

    internal static string Write(IReadOnlyDictionary<string, string> references, string path)
    {
        path = Path.GetFullPath(path);
        Create(references).Save(path);
        return path;
    }

    internal static XDocument Create(IReadOnlyDictionary<string, string> references)
    {
        using var graph = new MetadataGraph(references);
        var roots = new SortedDictionary<string, SortedDictionary<string, TypeRoots>>(StringComparer.Ordinal);
        TypeRoots Root(TypeKey key)
        {
            if (!roots.TryGetValue(key.Assembly, out var types)) roots.Add(key.Assembly, types = new(StringComparer.Ordinal));
            if (!types.TryGetValue(key.Name, out var type)) types.Add(key.Name, type = new());
            return type;
        }

        foreach (var assembly in graph.Assemblies.Values)
            foreach (var handle in assembly.Reader.TypeDefinitions)
            {
                var type = assembly.Reader.GetTypeDefinition(handle);
                foreach (var methodHandle in type.GetMethods())
                {
                    var method = assembly.Reader.GetMethodDefinition(methodHandle);
                    if (assembly.HasAttribute(method.GetCustomAttributes(), "UnityEngine.RuntimeInitializeOnLoadMethodAttribute"))
                        Root(assembly.Key(handle)).Methods.Add(assembly.Reader.GetString(method.Name));
                }
            }

        var visited = new HashSet<TypeKey>();
        void Schema(TypeKey key)
        {
            if (key.Name is "UnityEngine.ScriptableObject" or "UnityEngine.Object" || !visited.Add(key)) return;
            var (assembly, handle) = graph.Require(key);
            var type = assembly.Reader.GetTypeDefinition(handle);
            var root = Root(key);
            foreach (var methodHandle in type.GetMethods())
            {
                var method = assembly.Reader.GetMethodDefinition(methodHandle);
                if ((method.Attributes & MethodAttributes.Static) != 0) continue;
                string name = assembly.Reader.GetString(method.Name);
                if (name is not (".ctor" or "Awake" or "OnEnable" or "OnDisable" or "OnDestroy")) continue;
                var signature = method.DecodeSignature(assembly.Signatures, (object?)null);
                if (signature.ParameterTypes.Length == 0 && signature.GenericParameterCount == 0)
                {
                    if (name == ".ctor") root.DefaultConstructor = true;
                    else root.Methods.Add(name);
                }
            }
            foreach (var fieldHandle in type.GetFields())
            {
                var field = assembly.Reader.GetFieldDefinition(fieldHandle);
                if ((field.Attributes & (FieldAttributes.Static | FieldAttributes.InitOnly)) != 0) continue;
                // ECMA-335 NotSerialized flag; the runtime exposes it as the pseudo-attribute too.
                if (((int)field.Attributes & 0x0080) != 0) continue;
                if ((field.Attributes & FieldAttributes.FieldAccessMask) != FieldAttributes.Public &&
                    !assembly.HasAttribute(field.GetCustomAttributes(), "UnityEngine.SerializeField")) continue;
                root.Fields.Add(assembly.Reader.GetString(field.Name));
                TypeKey? memberType = field.DecodeSignature(assembly.Signatures, (object?)null);
                if (memberType is { } valueType && graph.IsValueType(valueType)) Schema(valueType);
                // UnityEngine.Object fields are GUID references, resolved through
                // the asset whitelist rather than recursively populated as data.
            }
            TypeKey? baseType = assembly.Key(type.BaseType);
            if (baseType is { } parent && graph.Assemblies.ContainsKey(parent.Assembly)) Schema(parent);
        }
        foreach (string name in AssetTypes)
        {
            var key = new TypeKey("NowUI.Runtime", name);
            graph.Require(key); // A mismatched runtime must fail before an incomplete descriptor is emitted.
            Schema(key);
        }

        // Runtime enum values are deserialized numerically. Their names also need
        // to survive for controls/inspectors that display the serialized value.
        foreach (var assembly in graph.Assemblies.Values)
            foreach (var handle in assembly.Reader.TypeDefinitions)
            {
                var key = assembly.Key(handle);
                if (!visited.Contains(key)) continue;
                var type = assembly.Reader.GetTypeDefinition(handle);
                if (assembly.Key(type.BaseType)?.Name != "System.Enum") continue;
                foreach (var field in type.GetFields()) Root(key).Fields.Add(assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(field).Name));
            }

        var linker = new XElement("linker");
        foreach (var assembly in roots)
        {
            var assemblyElement = new XElement("assembly", new XAttribute("fullname", assembly.Key));
            linker.Add(assemblyElement);
            foreach (var type in assembly.Value)
                assemblyElement.Add(new XElement("type", new XAttribute("fullname", type.Key), new XAttribute("preserve", "nothing"),
                    type.Value.DefaultConstructor ? new XElement("method", new XAttribute("signature", "System.Void .ctor()")) : null,
                    type.Value.Methods.Select(name => new XElement("method", new XAttribute("name", name))),
                    type.Value.Fields.Select(name => new XElement("field", new XAttribute("name", name)))));
        }
        return new XDocument(linker);
    }

    sealed class TypeRoots
    {
        internal bool DefaultConstructor;
        internal readonly SortedSet<string> Methods = new(StringComparer.Ordinal);
        internal readonly SortedSet<string> Fields = new(StringComparer.Ordinal);
    }

    readonly record struct TypeKey(string Assembly, string Name);

    sealed class MetadataGraph : IDisposable
    {
        internal readonly Dictionary<string, MetadataAssembly> Assemblies = new(StringComparer.Ordinal);
        internal MetadataGraph(IReadOnlyDictionary<string, string> references)
        {
            try
            {
                foreach (string name in new[] { "NowUI.Engine", "NowUI.Runtime" })
                {
                    if (!references.TryGetValue(name, out string? path)) throw new InvalidDataException("Missing browser reflection dependency: " + name);
                    Assemblies.Add(name, new MetadataAssembly(name, path));
                }
            }
            catch { Dispose(); throw; }
        }
        internal (MetadataAssembly Assembly, TypeDefinitionHandle Type) Require(TypeKey key)
        {
            if (!Assemblies.TryGetValue(key.Assembly, out var assembly) || !assembly.Types.TryGetValue(key.Name, out var type))
                throw new InvalidDataException("Missing browser reflection type: " + key.Assembly + ":" + key.Name);
            return (assembly, type);
        }
        internal bool IsValueType(TypeKey key)
        {
            if (!Assemblies.TryGetValue(key.Assembly, out var assembly)) return false;
            var (_, handle) = Require(key);
            return assembly.Key(assembly.Reader.GetTypeDefinition(handle).BaseType)?.Name is "System.ValueType" or "System.Enum";
        }
        public void Dispose() { foreach (var assembly in Assemblies.Values) assembly.Dispose(); }
    }

    sealed class MetadataAssembly : IDisposable
    {
        readonly FileStream stream;
        readonly PEReader pe;
        internal readonly MetadataReader Reader;
        internal readonly string Name;
        internal readonly Dictionary<string, TypeDefinitionHandle> Types = new(StringComparer.Ordinal);
        internal readonly SignatureTypes Signatures;
        internal MetadataAssembly(string name, string path)
        {
            stream = File.OpenRead(path);
            try
            {
                pe = new PEReader(stream);
                Reader = pe.GetMetadataReader();
                Name = Reader.GetString(Reader.GetAssemblyDefinition().Name);
                if (Name != name) throw new InvalidDataException($"Expected {name} metadata in {path}, found {Name}.");
                Signatures = new(this);
                foreach (var handle in Reader.TypeDefinitions) Types.Add(Key(handle).Name, handle);
            }
            catch { pe?.Dispose(); stream.Dispose(); throw; }
        }
        internal TypeKey Key(TypeDefinitionHandle handle)
        {
            var type = Reader.GetTypeDefinition(handle);
            var declaring = type.GetDeclaringType();
            string name = Reader.GetString(type.Name);
            if (!declaring.IsNil) name = Key(declaring).Name + "/" + name;
            else { string ns = Reader.GetString(type.Namespace); if (ns.Length > 0) name = ns + "." + name; }
            return new(Name, name);
        }
        internal TypeKey? Key(EntityHandle handle)
        {
            if (handle.IsNil) return null;
            if (handle.Kind == HandleKind.TypeDefinition) return Key((TypeDefinitionHandle)handle);
            if (handle.Kind == HandleKind.TypeSpecification)
                return Reader.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(Signatures, (object?)null);
            if (handle.Kind != HandleKind.TypeReference) return null;
            var type = Reader.GetTypeReference((TypeReferenceHandle)handle);
            string name = Reader.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
            {
                var parent = Key(type.ResolutionScope)!.Value;
                return new(parent.Assembly, parent.Name + "/" + name);
            }
            string ns = Reader.GetString(type.Namespace);
            string assembly = type.ResolutionScope.Kind == HandleKind.AssemblyReference
                ? Reader.GetString(Reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name) : Name;
            return new(assembly, ns.Length > 0 ? ns + "." + name : name);
        }
        internal bool HasAttribute(CustomAttributeHandleCollection attributes, string name)
        {
            foreach (var handle in attributes)
            {
                var attribute = Reader.GetCustomAttribute(handle);
                EntityHandle owner = attribute.Constructor.Kind == HandleKind.MemberReference
                    ? Reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent
                    : Reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
                if (Key(owner)?.Name == name) return true;
            }
            return false;
        }
        public void Dispose() { pe.Dispose(); stream.Dispose(); }
    }

    sealed class SignatureTypes(MetadataAssembly assembly) : ISignatureTypeProvider<TypeKey?, object?>
    {
        public TypeKey? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public TypeKey? GetArrayType(TypeKey? elementType, ArrayShape shape) => elementType;
        public TypeKey? GetByReferenceType(TypeKey? elementType) => elementType;
        public TypeKey? GetFunctionPointerType(MethodSignature<TypeKey?> signature) => null;
        public TypeKey? GetGenericInstantiation(TypeKey? genericType, ImmutableArray<TypeKey?> typeArguments) => genericType;
        public TypeKey? GetGenericMethodParameter(object? context, int index) => null;
        public TypeKey? GetGenericTypeParameter(object? context, int index) => null;
        public TypeKey? GetModifiedType(TypeKey? modifier, TypeKey? type, bool required) => type;
        public TypeKey? GetPinnedType(TypeKey? elementType) => elementType;
        public TypeKey? GetPointerType(TypeKey? elementType) => null;
        public TypeKey? GetSZArrayType(TypeKey? elementType) => elementType;
        public TypeKey? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => assembly.Key(handle);
        public TypeKey? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => assembly.Key(handle);
        public TypeKey? GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
    }
}
