using System;
using System.IO;
using UnityEngine;
using YamlDotNet.RepresentationModel;

namespace NowUI.Hosting
{
    internal static class NowLottieAssets
    {
        // NowLottieImporter.AddObjectToAsset("animation", asset). Pinned by the repository's actual scene references.
        internal const long ImportedFileId = 7868935432812623880;
        const string ImporterGuid = "39fc20ac96163174f8a350451c3145a0";

        internal static NowLottieAsset Load(string path, Type requested, long? localFileId, Action<UnityEngine.Object> own)
        {
            if (requested != null && !requested.IsAssignableFrom(typeof(NowLottieAsset))) return null;
            if (localFileId.HasValue && localFileId.Value != ImportedFileId && localFileId.Value != 11400000)
                throw new InvalidDataException($"Lottie '{path}' has no animation subasset with fileID {localFileId}.");
            if (Path.GetExtension(path).Equals(".lottie", StringComparison.OrdinalIgnoreCase)) ValidateImporter(path);
            using var stream = File.OpenRead(path);
            bool archive = stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
            stream.Position = 0;
            long limit = Math.Max(1, archive ? NowLottieAsset.maxArchiveBytes : NowLottieAsset.maxJsonBytes);
            if (stream.Length > limit || stream.Length > int.MaxValue)
                throw new InvalidDataException($"Lottie '{path}' exceeds the configured source byte limit ({limit}).");
            byte[] bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            var asset = ScriptableObject.CreateInstance<NowLottieAsset>();
            own(asset);
            asset.name = Path.GetFileNameWithoutExtension(path);
            asset.SetSource(bytes); // Shared JSON/archive limits and the exact Unity import parser.
            return asset;
        }

        static void ValidateImporter(string path)
        {
            string meta = path + ".meta";
            if (!File.Exists(meta)) return;
            if (new FileInfo(meta).Length > 4 * 1024 * 1024) throw new InvalidDataException("Lottie metadata exceeds 4 MiB: " + meta);
            using var reader = File.OpenText(meta);
            var yaml = new YamlStream(); yaml.Load(reader);
            if (yaml.Documents.Count != 1 || yaml.Documents[0].RootNode is not YamlMappingNode root
                || !root.Children.TryGetValue(new YamlScalarNode("ScriptedImporter"), out var importerNode)
                || importerNode is not YamlMappingNode importer
                || !importer.Children.TryGetValue(new YamlScalarNode("script"), out var scriptNode)
                || scriptNode is not YamlMappingNode script
                || !script.Children.TryGetValue(new YamlScalarNode("guid"), out var guid)
                || !string.Equals((guid as YamlScalarNode)?.Value, ImporterGuid, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException($"Lottie '{path}' uses an unknown importer; only NowLottieImporter source semantics are supported.");
        }
    }
}
