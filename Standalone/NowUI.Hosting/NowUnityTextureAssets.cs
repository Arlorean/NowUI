using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using StbImageSharp;
using UnityEngine;
using YamlDotNet.RepresentationModel;

[assembly: InternalsVisibleTo("NowUI.Native.Tests")]

namespace NowUI.Hosting
{
    // This loader reads source assets, not Unity's Library artifacts. Compression and custom importers are
    // intentionally outside this contract. The ordinary TextureImporter settings relevant to NowUI are read here.
    internal static class NowUnityTextureAssets
    {
        private sealed class SourceSize { public int width; public int height; }
        private static readonly ConditionalWeakTable<Texture2D, SourceSize> sourceSizes = new();
        private const long MaxPixels = 16 * 1024 * 1024;

        internal static Texture2D LoadTexture(string absolutePath, Action<UnityEngine.Object> own)
        {
            if (own == null) throw new ArgumentNullException(nameof(own));
            var importer = ReadImporter(absolutePath);
            ValidateImporter(absolutePath, importer);
            using var source = File.OpenRead(absolutePath);
            var info = ImageInfo.FromStream(source);
            if (!info.HasValue) throw new InvalidDataException($"Cannot decode image '{absolutePath}'. Supported source formats are PNG, JPEG, TGA and BMP.");
            ValidateDimensions(absolutePath, info.Value.Width, info.Value.Height);
            source.Position = 0;
            ImageResult image;
            try { image = ImageResult.FromStream(source, ColorComponents.RedGreenBlueAlpha); }
            catch (Exception error) { throw new InvalidDataException($"Cannot decode image '{absolutePath}': {error.Message}", error); }
            ValidateDimensions(absolutePath, image.Width, image.Height);
            var (width, height) = ImportedSize(importer, image.Width, image.Height);
            ValidateDimensions(absolutePath, width, height);
            bool resized = width != image.Width || height != image.Height;
            byte[] rgba = resized ? Resize(image.Data, image.Width, image.Height, width, height) : image.Data;

            int alphaUsage = Integer(importer, "alphaUsage", 1);
            bool grayAlpha = Integer(importer, "grayScaleToAlpha", 0) != 0 || alphaUsage == 2;
            for (int i = 0; i < rgba.Length; i += 4)
            {
                if (alphaUsage == 0) rgba[i + 3] = 255;
                else if (grayAlpha) rgba[i + 3] = (byte)Math.Round(rgba[i] * .299 + rgba[i + 1] * .587 + rgba[i + 2] * .114);
            }
            var pixels = new Color32[checked(width * height)];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int from = (y * width + x) * 4;
                pixels[(height - 1 - y) * width + x] = new Color32(rgba[from], rgba[from + 1], rgba[from + 2], rgba[from + 3]);
            }
            var mipmaps = Mapping(importer, "mipmaps");
            bool mipChain = Integer(mipmaps, "enableMipMap", 0) != 0;
            bool linear = Integer(mipmaps, "sRGBTexture", 1) == 0;
            var settings = Mapping(importer, "textureSettings");
            int legacyWrap = Math.Max(0, Integer(settings, "wrapMode", 0));
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain, linear)
                {
                    name = Path.GetFileNameWithoutExtension(absolutePath),
                    filterMode = (FilterMode)EnumInteger(settings, "filterMode", 1, 0, 2),
                    wrapModeU = (TextureWrapMode)EnumInteger(settings, "wrapU", legacyWrap, 0, 3),
                    wrapModeV = (TextureWrapMode)EnumInteger(settings, "wrapV", legacyWrap, 0, 3),
                    wrapModeW = (TextureWrapMode)EnumInteger(settings, "wrapW", legacyWrap, 0, 3),
                    anisoLevel = Math.Clamp(Integer(settings, "aniso", 1), 0, 16),
                };
                texture.SetPixels32(pixels);
                texture.Apply(mipChain, false);
                sourceSizes.Add(texture, new SourceSize { width = image.Width, height = image.Height });
                own(texture);
            }
            catch
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                throw;
            }
            var notes = new List<string>();
            if (resized) notes.Add($"resized {image.Width}x{image.Height} to {width}x{height} with bilinear sampling; Unity's resize filter may differ");
            if (info.Value.BitsPerChannel > 8) notes.Add("converted the source to 8-bit RGBA");
            if (Integer(importer, "alphaIsTransparency", 0) != 0 && HasTransparentPixels(rgba))
                notes.Add("preserved source RGB in transparent pixels; Unity's alpha color dilation is not reproduced");
            if (notes.Count > 0) Console.Error.WriteLine($"NowUI asset '{absolutePath}': {string.Join("; ", notes)}.");
            return texture;
        }

        internal static Sprite LoadSprite(string absolutePath, string spriteName, long? localFileId,
            Texture2D texture, Action<UnityEngine.Object> own)
        {
            if (own == null) throw new ArgumentNullException(nameof(own));
            if (texture == null) throw new ArgumentNullException(nameof(texture));
            var importer = ReadImporter(absolutePath);
            int mode = Integer(importer, "spriteMode", 0);
            if (mode == 0) throw new InvalidDataException($"'{absolutePath}' is not imported as a Sprite in its .meta file.");
            if (mode != 1 && mode != 2) throw new NotSupportedException($"Sprite mode {mode} in '{absolutePath}' is unsupported; single and multiple sprites are supported.");
            var sheet = Mapping(importer, "spriteSheet");
            SourceSize source = sourceSizes.TryGetValue(texture, out var foundSize)
                ? foundSize : new SourceSize { width = texture.width, height = texture.height };
            float sx = texture.width / (float)source.width, sy = texture.height / (float)source.height;
            Rect rect;
            Vector4 border;
            Vector2 pivot;
            string selectedName;
            if (mode == 1)
            {
                selectedName = Path.GetFileNameWithoutExtension(absolutePath);
                long id = Long(sheet, "internalID", 21300000);
                if (id == 0) id = 21300000;
                if ((!string.IsNullOrEmpty(spriteName) && spriteName != selectedName)
                    || (localFileId.HasValue && localFileId.Value != id && localFileId.Value != 21300000))
                    throw new InvalidDataException($"Sprite '{spriteName ?? localFileId.ToString()}' does not exist in '{absolutePath}'.");
                rect = new Rect(0, 0, texture.width, texture.height);
                border = Border(importer, "spriteBorder", sx, sy);
                pivot = Pivot(importer, "spritePivot");
            }
            else
            {
                var entries = Sequence(sheet, "sprites");
                var matches = new List<YamlMappingNode>();
                foreach (var node in entries.Children)
                {
                    if (node is not YamlMappingNode entry) throw new InvalidDataException($"Malformed sprite entry in '{absolutePath}.meta'.");
                    string name = Scalar(entry, "name", "");
                    long id = SpriteId(importer, sheet, entry, name);
                    if ((!string.IsNullOrEmpty(spriteName) && name != spriteName)
                        || (localFileId.HasValue && id != localFileId.Value)) continue;
                    matches.Add(entry);
                }
                if (matches.Count == 0) throw new InvalidDataException($"Sprite '{spriteName ?? localFileId?.ToString() ?? "(default)"}' does not exist in '{absolutePath}'.");
                if (matches.Count > 1) throw new InvalidDataException($"'{absolutePath}' contains multiple sprites; select one by #spriteName or its local fileID.");
                var selected = matches[0];
                selectedName = Scalar(selected, "name", "");
                var r = Mapping(selected, "rect");
                rect = new Rect(Number(r, "x", 0) * sx, Number(r, "y", 0) * sy,
                    Number(r, "width", 0) * sx, Number(r, "height", 0) * sy);
                border = Border(selected, "border", sx, sy);
                pivot = Pivot(selected, "pivot");
            }
            // Unity scales PPU along with the imported pixels, preserving the sprite's size in world units.
            // Pinned by a Unity 6000.4 import oracle: source64x32/max32/PPU32 becomes32x16/PPU16.
            float ppu = Number(importer, "spritePixelsToUnits", 100) * sx;
            if (ppu <= 0 || rect.width <= 0 || rect.height <= 0 || rect.x < 0 || rect.y < 0
                || rect.xMax > texture.width + .001f || rect.yMax > texture.height + .001f
                || border.x < 0 || border.y < 0 || border.z < 0 || border.w < 0
                || border.x + border.z > rect.width + .001f || border.y + border.w > rect.height + .001f)
                throw new InvalidDataException($"Sprite '{selectedName}' in '{absolutePath}' has invalid dimensions, borders or pixels per unit.");
            Sprite sprite = null;
            try
            {
                sprite = Sprite.Create(texture, rect, pivot, ppu, 0, SpriteMeshType.FullRect, border);
                sprite.name = selectedName;
                own(sprite);
                return sprite;
            }
            catch
            {
                if (sprite != null) UnityEngine.Object.DestroyImmediate(sprite);
                throw;
            }
        }

        private static YamlMappingNode ReadImporter(string path)
        {
            string meta = path + ".meta";
            if (!File.Exists(meta)) return new YamlMappingNode();
            if (new FileInfo(meta).Length > 4 * 1024 * 1024)
                throw new InvalidDataException($"Texture metadata '{meta}' exceeds the 4 MiB preview limit.");
            var yaml = new YamlStream();
            try
            {
                using var reader = File.OpenText(meta);
                yaml.Load(reader);
            }
            catch (Exception error) { throw new InvalidDataException($"Cannot parse Unity texture metadata '{meta}': {error.Message}", error); }
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root
                || Node(root, "TextureImporter") is not YamlMappingNode importer)
                throw new NotSupportedException($"'{meta}' is not a TextureImporter asset. Custom image importers require Unity.");
            return importer;
        }

        private static void ValidateImporter(string path, YamlMappingNode importer)
        {
            int type = Integer(importer, "textureType", 0);
            if (type != 0 && type != 8 && type != 2) // Default, Sprite, legacy GUI
                throw new NotSupportedException($"Texture type {type} in '{path}' is unsupported; ordinary 2D color textures and sprites are supported.");
            if (Integer(importer, "textureShape", 1) != 1 || Integer(Mapping(importer, "bumpmap"), "convertToNormalMap", 0) != 0)
                throw new NotSupportedException($"'{path}' uses a non-2D or normal-map import transformation that requires Unity.");
            if (Long(importer, "swizzle", 50462976) != 50462976)
                throw new NotSupportedException($"'{path}' uses texture channel swizzling, which is not supported by the source asset loader.");
        }

        private static void ValidateDimensions(string path, int width, int height)
        {
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384 || (long)width * height > MaxPixels)
                throw new InvalidDataException($"Image '{path}' is {width}x{height}; preview images must be at most 16384 pixels per side and 16 million pixels total.");
        }

        private static (int, int) ImportedSize(YamlMappingNode importer, int width, int height)
        {
            int npot = EnumInteger(importer, "nPOTScale", 0, 0, 3);
            // Unity disables NPOT rescaling for sprites.
            if (Integer(importer, "spriteMode", 0) == 0)
            {
                width = PowerOfTwo(width, npot);
                height = PowerOfTwo(height, npot);
            }
            int max = Integer(importer, "maxTextureSize", 0);
            YamlMappingNode platform = null;
            foreach (var node in Sequence(importer, "platformSettings").Children)
            {
                if (node is not YamlMappingNode entry) continue;
                string target = Scalar(entry, "buildTarget", "");
                if (target == "DefaultTexturePlatform" && platform == null) platform = entry;
                if (target == "Standalone" && Integer(entry, "overridden", 0) != 0) { platform = entry; break; }
            }
            if (platform != null) max = Integer(platform, "maxTextureSize", max);
            if (max > 0 && Math.Max(width, height) > max)
            {
                double scale = max / (double)Math.Max(width, height);
                width = Math.Max(1, (int)Math.Round(width * scale));
                height = Math.Max(1, (int)Math.Round(height * scale));
            }
            return (width, height);
        }

        private static int PowerOfTwo(int value, int mode)
        {
            if (mode == 0) return value;
            int upper = 1;
            while (upper < value) upper *= 2;
            if (upper == value || mode == 2) return upper;
            int lower = upper / 2;
            return mode == 3 || value - lower < upper - value ? lower : upper;
        }

        private static byte[] Resize(byte[] data, int oldWidth, int oldHeight, int width, int height)
        {
            var result = new byte[checked(width * height * 4)];
            for (int y = 0; y < height; y++)
            {
                double fy = Math.Clamp((y + .5) * oldHeight / height - .5, 0, oldHeight - 1);
                int y0 = (int)fy, y1 = Math.Min(y0 + 1, oldHeight - 1);
                double ty = fy - y0;
                for (int x = 0; x < width; x++)
                {
                    double fx = Math.Clamp((x + .5) * oldWidth / width - .5, 0, oldWidth - 1);
                    int x0 = (int)fx, x1 = Math.Min(x0 + 1, oldWidth - 1);
                    double tx = fx - x0;
                    for (int c = 0; c < 4; c++)
                    {
                        double a = data[(y0 * oldWidth + x0) * 4 + c] * (1 - tx) + data[(y0 * oldWidth + x1) * 4 + c] * tx;
                        double b = data[(y1 * oldWidth + x0) * 4 + c] * (1 - tx) + data[(y1 * oldWidth + x1) * 4 + c] * tx;
                        result[(y * width + x) * 4 + c] = (byte)Math.Round(a * (1 - ty) + b * ty);
                    }
                }
            }
            return result;
        }

        private static bool HasTransparentPixels(byte[] rgba)
        {
            for (int i = 3; i < rgba.Length; i += 4) if (rgba[i] == 0) return true;
            return false;
        }

        private static long SpriteId(YamlMappingNode importer, YamlMappingNode sheet, YamlMappingNode entry, string name)
        {
            long id = Long(entry, "internalID", 0);
            if (id != 0) return id;
            id = Long(Mapping(sheet, "nameFileIdTable"), name, 0);
            if (id != 0) return id;
            foreach (var item in Sequence(importer, "internalIDToNameTable").Children)
            {
                if (item is not YamlMappingNode pair || Scalar(pair, "second", "") != name) continue;
                id = Long(Mapping(pair, "first"), "213", 0);
                if (id != 0) return id;
            }
            // Older Unity metadata stores class/local IDs directly in this map.
            foreach (var item in Mapping(importer, "fileIDToRecycleName").Children)
                if (item.Value is YamlScalarNode value && value.Value == name
                    && item.Key is YamlScalarNode key && long.TryParse(key.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return id;
            return 0;
        }

        private static Vector4 Border(YamlMappingNode root, string key, float sx, float sy)
        {
            var map = Mapping(root, key);
            return new Vector4(Number(map, "x", 0) * sx, Number(map, "y", 0) * sy,
                Number(map, "z", 0) * sx, Number(map, "w", 0) * sy);
        }

        private static Vector2 Pivot(YamlMappingNode root, string key)
        {
            int alignment = Integer(root, "alignment", 0);
            return alignment switch
            {
                0 => new Vector2(.5f, .5f), 1 => new Vector2(0, 1), 2 => new Vector2(.5f, 1),
                3 => new Vector2(1, 1), 4 => new Vector2(0, .5f), 5 => new Vector2(1, .5f),
                6 => new Vector2(0, 0), 7 => new Vector2(.5f, 0), 8 => new Vector2(1, 0),
                9 => new Vector2(Number(Mapping(root, key), "x", .5f), Number(Mapping(root, key), "y", .5f)),
                _ => throw new InvalidDataException($"Unsupported sprite alignment {alignment}."),
            };
        }

        private static YamlNode Node(YamlMappingNode map, string key) => map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
        private static YamlMappingNode Mapping(YamlMappingNode map, string key) => Node(map, key) as YamlMappingNode ?? new YamlMappingNode();
        private static YamlSequenceNode Sequence(YamlMappingNode map, string key) => Node(map, key) as YamlSequenceNode ?? new YamlSequenceNode();
        private static string Scalar(YamlMappingNode map, string key, string fallback) => Node(map, key) is YamlScalarNode value ? value.Value ?? fallback : fallback;
        private static long Long(YamlMappingNode map, string key, long fallback)
        {
            string text = Scalar(map, key, null);
            if (text == null) return fallback;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) return value;
            throw new InvalidDataException($"Invalid integer '{text}' for TextureImporter.{key}.");
        }
        private static int Integer(YamlMappingNode map, string key, int fallback) => checked((int)Long(map, key, fallback));
        private static int EnumInteger(YamlMappingNode map, string key, int fallback, int min, int max)
        {
            int value = Integer(map, key, fallback);
            if (value == -1) return fallback; // Unity's unspecified sampler sentinel.
            if (value < min || value > max) throw new InvalidDataException($"Unsupported TextureImporter.{key} value {value}.");
            return value;
        }
        private static float Number(YamlMappingNode map, string key, float fallback)
        {
            string text = Scalar(map, key, null);
            if (text == null) return fallback;
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value)) return value;
            throw new InvalidDataException($"Invalid number '{text}' for TextureImporter.{key}.");
        }
    }
}
