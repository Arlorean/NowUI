using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using UnityEngine;
using YamlDotNet.RepresentationModel;

namespace NowUI.Hosting
{
    /// <summary>Reads Unity's source serialization, without importing assets or executing project scripts.</summary>
    internal static class NowUnitySerializedAssets
    {
        // Script identities belong to the NowUI package. Serialized class names never select arbitrary CLR types.
        static readonly Dictionary<string, Type> KnownScripts = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["10bbcf9941bbd194b90f55d4099e3d9c"] = typeof(NowFont),
            ["7fc6d0b96f8f49d4a8da53c8d2c48f91"] = typeof(NowFontFamily),
            ["04c5a0a2a8e8407792a9d8e3844255a0"] = typeof(NowThemeAsset),
            ["8b3e6c69a9d14d44929dbdf5b7962f14"] = typeof(NowControlRenderer),
            ["9d4c7a2f8e5b4d7ab1c9f062a63e1d58"] = typeof(NowMaterialControlRenderer),
            ["e87ea36cc0a14cb0af91fa5c5e6d4fd4"] = typeof(NowUnityEditorControlRenderer),
            ["3055cc0dbfa91e549afdc1ff056b42d5"] = typeof(NowLottieAsset),
        };

        static readonly Regex DocumentHeader = new Regex(@"^--- !u!(\d+) &(-?\d+)(?: stripped)?\s*$", RegexOptions.Multiline);
        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        // Build-time browser provisioning uses the exact same whitelist as runtime loading.
        internal static bool IsKnownAsset(string path)
        {
            Node source = ReadSource(path, null);
            return source.Child("$classId").Text == "28" || KnownScripts.ContainsKey(source.Child("m_Script").Child("guid").Text);
        }

        internal static IReadOnlyCollection<string> ReferencedGuids(string path)
        {
            var guids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Visit(ReadSource(path, null));
            return guids;
            void Visit(Node node)
            {
                string guid = node.Child("guid").Text;
                string fileId = node.Child("fileID").Text;
                if (guid.Length > 0 && guid != "00000000000000000000000000000000" && fileId != "0" && fileId.Length > 0) guids.Add(guid);
                foreach (var pair in node.Map)
                    if (pair.Key != "m_Script") Visit(pair.Value); // Script GUIDs select the whitelist; project code is never staged.
                foreach (var item in node.Items) Visit(item);
            }
        }

        static Node ReadSource(string path, long? localFileId)
        {
            using var reader = new StreamReader(path);
            int first = reader.Peek();
            return first == '%' || first == '-' || first == 'M'
                ? ReadYaml(reader.ReadToEnd(), localFileId, path)
                : ReadBinary(path, localFileId);
        }

        // own is called for the main object before references are followed, so the provider can cache the shell
        // and preserve counterpart/fallback cycles. Empty reference GUID means a subasset in the current file.
        internal static UnityEngine.Object Load(string absolutePath, Type requestedType, long? localFileId,
            Func<string, long, Type, UnityEngine.Object> resolveGuid, Action<UnityEngine.Object> own)
        {
            string extension = Path.GetExtension(absolutePath);
            if (extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase) || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase))
            {
                if (requestedType != null && !requestedType.IsAssignableFrom(typeof(NowFont))) return null;
                if (localFileId.HasValue && localFileId != 12800000 && localFileId != 11400000)
                    throw new InvalidDataException($"Font '{absolutePath}' has no subasset with fileID {localFileId}.");
                if (!NowFontCompiler.TryCompile(File.ReadAllBytes(absolutePath), out NowFont font, out string error))
                    throw new InvalidDataException($"Cannot read font '{absolutePath}': {error}");
                own(font);
                font.name = Path.GetFileNameWithoutExtension(absolutePath);
                return font;
            }

            Node source = ReadSource(absolutePath, localFileId);

            if (source.Child("$classId").Text == "28")
            {
                if (requestedType != null && !requestedType.IsAssignableFrom(typeof(Texture2D))) return null;
                return LoadTexture(source, absolutePath, own);
            }
            string scriptGuid = source.Child("m_Script").Child("guid").Text;
            if (!KnownScripts.TryGetValue(scriptGuid, out Type type))
                throw new NotSupportedException($"Asset '{absolutePath}' uses unsupported script GUID '{scriptGuid}'. Only NowUI fonts, font families, themes, Lottie assets, and built-in control renderers can be read by the standalone host.");
            if (requestedType != null && !requestedType.IsAssignableFrom(type)) return null;
            var result = ScriptableObject.CreateInstance(type);
            own(result);
            result.name = source.Child("m_Name").Text;
            Populate(result, source, resolveGuid);
            if (result is NowLottieAsset lottie && lottie.hasJson)
                lottie.SetSource(source.Child("_json").Text);
            if (result is ISerializationCallbackReceiver callback)
                callback.OnAfterDeserialize();
            if (result is NowFont loadedFont && !loadedFont.HasEmbeddedSource && loadedFont.atlas == null && loadedFont.bakedPageCount == 0)
                throw new InvalidDataException($"Font asset '{absolutePath}' has neither embedded font bytes nor a baked atlas.");
            return result;
        }

        static void Populate(object target, Node node, Func<string, long, Type, UnityEngine.Object> resolve)
        {
            // Reflection is limited to the serialized data members of a known NowUI object and its declared
            // value types. Private runtime caches, properties, constructors named by data, and scripts are ignored.
            for (Type type = target.GetType(); type != null && type != typeof(ScriptableObject) && type != typeof(UnityEngine.Object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(Fields))
                {
                    if (field.IsStatic || field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), false) ||
                        (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false)))
                        continue;
                    if (!node.Map.TryGetValue(field.Name, out Node value))
                        continue; // Unity preserves initializer defaults for fields absent in older assets.
                    field.SetValue(target, ConvertValue(value, field.FieldType, field.GetValue(target), resolve));
                }
            }
        }

        static Texture2D LoadTexture(Node source, string path, Action<UnityEngine.Object> own)
        {
            int width = Integer(source, "m_Width"), height = Integer(source, "m_Height");
            if (width < 1 || height < 1 || width > 16384 || height > 16384 || (long)width * height > 16 * 1024 * 1024)
                throw new InvalidDataException($"Texture in '{path}' has unsupported dimensions {width}x{height}.");
            if (Integer(source, "m_TextureDimension", 2) != 2 || Integer(source, "m_ImageCount", 1) != 1)
                throw new NotSupportedException($"Only ordinary 2D texture subassets can be read from '{path}'.");
            int format = Integer(source, "m_TextureFormat");
            int channels = format switch { 1 or 63 => 1, 3 => 3, 4 or 5 or 14 => 4, _ => 0 };
            if (channels == 0)
                throw new NotSupportedException($"Texture in '{path}' uses unsupported Unity texture format {format}; embedded atlases must use Alpha8, R8, RGB24, RGBA32, ARGB32, or BGRA32.");
            if (Integer(source, "m_MipsStripped") != 0)
                throw new NotSupportedException($"Texture in '{path}' has stripped mip levels.");
            int mipCount = Integer(source, "m_MipCount", 1);
            int maximumMips = 1 + (int)Math.Floor(Math.Log2(Math.Max(width, height)));
            if (mipCount < 1 || mipCount > maximumMips)
                throw new InvalidDataException($"Texture in '{path}' declares invalid mip count {mipCount}.");
            Node encoded = source.Map.TryGetValue("_typelessdata", out Node hex) ? hex : source.Child("image data");
            if (source.Child("m_StreamData").Child("path").Text.Length != 0)
                throw new NotSupportedException($"Texture in '{path}' stores its pixel data in an external stream; only embedded atlas pixels are supported.");
            byte[] raw = encoded.Scalar is byte[] bytes ? bytes : System.Convert.FromHexString(encoded.Text);
            int pixelCount = 0;
            for (int mip = 0; mip < mipCount; mip++) pixelCount += Math.Max(1, width >> mip) * Math.Max(1, height >> mip);
            if (raw.Length != checked(pixelCount * channels))
                throw new InvalidDataException($"Texture in '{path}' has {raw.Length} embedded pixel bytes, expected {pixelCount * channels}; streamed or truncated texture data cannot be read directly.");
            byte[] rgba = new byte[checked(pixelCount * 4)];
            for (int i = 0, p = 0; i < raw.Length; i += channels, p += 4)
            {
                switch (format)
                {
                    case 1: rgba[p] = rgba[p + 1] = rgba[p + 2] = 255; rgba[p + 3] = raw[i]; break;
                    case 63: rgba[p] = raw[i]; rgba[p + 3] = 255; break;
                    case 3: rgba[p] = raw[i]; rgba[p + 1] = raw[i + 1]; rgba[p + 2] = raw[i + 2]; rgba[p + 3] = 255; break;
                    case 4: Buffer.BlockCopy(raw, i, rgba, p, 4); break;
                    case 5: rgba[p] = raw[i + 1]; rgba[p + 1] = raw[i + 2]; rgba[p + 2] = raw[i + 3]; rgba[p + 3] = raw[i]; break;
                    case 14: rgba[p] = raw[i + 2]; rgba[p + 1] = raw[i + 1]; rgba[p + 2] = raw[i]; rgba[p + 3] = raw[i + 3]; break;
                }
            }
            Node settings = source.Child("m_TextureSettings");
            // Unity's serialized TextureColorSpace is Linear=0, sRGB=1 (not the project's ColorSpace enum).
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipCount, Integer(source, "m_ColorSpace") == 0);
            own(texture);
            texture.name = source.Child("m_Name").Text;
            texture.filterMode = (FilterMode)Integer(settings, "m_FilterMode", 1);
            texture.wrapModeU = (TextureWrapMode)Integer(settings, "m_WrapU", Integer(settings, "m_WrapMode"));
            texture.wrapModeV = (TextureWrapMode)Integer(settings, "m_WrapV", Integer(settings, "m_WrapMode"));
            texture.wrapModeW = (TextureWrapMode)Integer(settings, "m_WrapW", Integer(settings, "m_WrapMode"));
            texture.anisoLevel = Integer(settings, "m_Aniso", 1);
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, false);
            return texture;
        }

        static int Integer(Node node, string key, int fallback = 0)
            => node.Map.TryGetValue(key, out Node value) ? System.Convert.ToInt32(value.Scalar, CultureInfo.InvariantCulture) : fallback;

        static object ConvertValue(Node node, Type type, object existing, Func<string, long, Type, UnityEngine.Object> resolve)
        {
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                long fileId = long.Parse(node.Child("fileID").Text, CultureInfo.InvariantCulture);
                if (fileId == 0) return null;
                UnityEngine.Object result = resolve(node.Child("guid").Text, fileId, type);
                if (result == null)
                    throw new FileNotFoundException($"Unable to resolve {type.Name} reference guid '{node.Child("guid").Text}', fileID {fileId}.");
                if (!type.IsInstanceOfType(result))
                    throw new InvalidCastException($"Reference fileID {fileId} resolved to {result.GetType().Name}, expected {type.Name}.");
                return result;
            }
            if (type == typeof(string)) return node.Text;
            if (type == typeof(byte[]) && node.Scalar is byte[] bytes) return bytes;
            if (type == typeof(byte[]) && node.Scalar is string hex)
                return string.IsNullOrEmpty(hex) ? Array.Empty<byte>() : System.Convert.FromHexString(hex);
            if (type == typeof(bool)) return node.Text == "1" || node.Text.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (type.IsEnum) return Enum.ToObject(type, long.Parse(node.Text, CultureInfo.InvariantCulture));
            if (type.IsPrimitive || type == typeof(decimal))
                return System.Convert.ChangeType(node.Scalar, type, CultureInfo.InvariantCulture);
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                var values = Array.CreateInstance(elementType, node.Items.Count);
                for (int i = 0; i < node.Items.Count; i++)
                    values.SetValue(ConvertValue(node.Items[i], elementType, null, resolve), i);
                return values;
            }
            if (!type.IsValueType || (type.Assembly != typeof(NowThemeAsset).Assembly && type.Assembly != typeof(Color).Assembly))
                throw new NotSupportedException($"Serialized member type {type.FullName} is unsupported.");
            object value = existing ?? Activator.CreateInstance(type);
            Populate(value, node, resolve);
            return value;
        }

        static Node ReadYaml(string text, long? localId, string path)
        {
            var headers = DocumentHeader.Matches(text);
            if (headers.Count == 0)
                throw new InvalidDataException($"Asset '{path}' is missing its Unity document header.");
            if (!localId.HasValue)
            {
                // Unity may serialize texture subassets before their owning font. Document order is not identity.
                Match main = headers.Cast<Match>().FirstOrDefault(value => value.Groups[1].Value == "114" && value.Groups[2].Value == "11400000")
                    ?? headers.Cast<Match>().FirstOrDefault(value => value.Groups[1].Value == "114")
                    ?? headers.Cast<Match>().FirstOrDefault(value => value.Groups[1].Value == "28");
                if (main != null) localId = long.Parse(main.Groups[2].Value, CultureInfo.InvariantCulture);
            }
            for (int i = 0; i < headers.Count; i++)
            {
                long id = long.Parse(headers[i].Groups[2].Value, CultureInfo.InvariantCulture);
                if (localId.HasValue && id != localId.Value) continue;
                int classId = int.Parse(headers[i].Groups[1].Value, CultureInfo.InvariantCulture);
                if (!localId.HasValue && classId != 114 && classId != 28) continue;
                if (classId != 114 && classId != 28)
                    throw new NotSupportedException($"Asset '{path}' fileID {id} is not a supported NowUI ScriptableObject.");
                int start = headers[i].Index + headers[i].Length;
                int end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
                var yaml = new YamlStream();
                yaml.Load(new StringReader(text.Substring(start, end - start)));
                Node root = FromYaml(yaml.Documents[0].RootNode);
                Node result = root.Child(classId == 114 ? "MonoBehaviour" : "Texture2D");
                result.Map["$classId"] = new Node(classId);
                return result;
            }
            throw new InvalidDataException($"Asset '{path}' has no supported document with fileID {localId?.ToString() ?? "(main)"}.");
        }

        static Node FromYaml(YamlNode node)
        {
            if (node is YamlScalarNode scalar) return new Node(scalar.Value ?? "");
            var result = new Node();
            if (node is YamlMappingNode map)
                foreach (var item in map.Children) result.Map[((YamlScalarNode)item.Key).Value] = FromYaml(item.Value);
            else if (node is YamlSequenceNode list)
                foreach (YamlNode item in list.Children) result.Items.Add(FromYaml(item));
            else throw new InvalidDataException("Unsupported YAML node in Unity asset.");
            return result;
        }

        static Node ReadBinary(string path, long? localId)
        {
            var manager = new AssetsManager();
            try
            {
                // Dependencies are resolved from source .meta GUIDs by the project provider, never Library imports.
                AssetsFileInstance instance = manager.LoadAssetsFile(path, false);
                if (!instance.file.Metadata.TypeTreeEnabled)
                    throw new NotSupportedException($"Binary asset '{path}' has no embedded type tree; its fields cannot be read directly.");
                AssetFileInfo info = localId.HasValue
                    ? instance.file.GetAssetInfo(localId.Value)
                    : instance.file.AssetInfos.FirstOrDefault(value => value.PathId == 11400000 && value.TypeId == 114)
                        ?? instance.file.AssetInfos.FirstOrDefault(value => value.TypeId == 114)
                        ?? instance.file.AssetInfos.FirstOrDefault(value => value.TypeId == 28);
                if (info == null)
                    throw new InvalidDataException($"Binary asset '{path}' has no supported object with fileID {localId?.ToString() ?? "(main)"}.");
                if (info.TypeId != 114 && info.TypeId != 28)
                    throw new NotSupportedException($"Binary asset '{path}' fileID {info.PathId} has unsupported Unity class ID {info.TypeId}.");
                Node result = FromBinary(manager.GetBaseField(instance, info), instance);
                result.Map["$classId"] = new Node(info.TypeId);
                return result;
            }
            finally { manager.UnloadAll(); }
        }

        static Node FromBinary(AssetTypeValueField field, AssetsFileInstance instance)
        {
            if (field.TypeName.StartsWith("PPtr<", StringComparison.Ordinal))
            {
                int index = field["m_FileID"].AsInt;
                string guid = "";
                if (index != 0)
                {
                    if (index < 0 || index > instance.file.Metadata.Externals.Count)
                        throw new InvalidDataException("Invalid external reference index in binary Unity asset.");
                    guid = instance.file.Metadata.Externals[index - 1].Guid.ToString();
                }
                var pointer = new Node();
                pointer.Map["guid"] = new Node(guid);
                pointer.Map["fileID"] = new Node(field["m_PathID"].AsLong);
                return pointer;
            }
            if (field.TemplateField.ValueType == AssetValueType.String) return new Node(field.AsString);
            if (field.TemplateField.ValueType == AssetValueType.ByteArray) return new Node(field.AsByteArray);
            if (field.TemplateField.IsArray)
            {
                var list = new Node();
                foreach (var item in field.Children) list.Items.Add(FromBinary(item, instance));
                return list;
            }
            if (field.Children.Count == 1 && field.Children[0].TemplateField.IsArray)
                return FromBinary(field.Children[0], instance);
            if (field.Value != null) return new Node(field.AsObject);
            var result = new Node();
            foreach (var child in field.Children) result.Map[child.FieldName] = FromBinary(child, instance);
            return result;
        }

        sealed class Node
        {
            internal readonly object Scalar;
            internal readonly Dictionary<string, Node> Map = new Dictionary<string, Node>(StringComparer.Ordinal);
            internal readonly List<Node> Items = new List<Node>();
            internal Node(object scalar = null) { Scalar = scalar; }
            internal string Text => System.Convert.ToString(Scalar, CultureInfo.InvariantCulture) ?? "";
            internal Node Child(string name) => Map.TryGetValue(name, out Node child) ? child : new Node("");
        }
    }
}
