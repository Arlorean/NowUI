using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace NowUI.Editor
{
    /// <summary>
    /// Bakes a <see cref="NowFont"/>'s authored characters into sealed dynamic atlas
    /// pages stored on the asset. The bake drives the same compiler session the runtime
    /// uses, so a baked page is exactly what a warmed session would have produced; the
    /// first draw of those characters finds it resident instead of rasterizing them.
    /// Glyphs outside the authored set keep baking on demand.
    /// </summary>
    public static class NowFontBaker
    {
        public static readonly string ASCII = BuildRange(0x20, 0x7E);
        public static readonly string LATIN_1 = BuildRange(0xA0, 0xFF);
        public static readonly string LATIN_EXTENDED_A = BuildRange(0x100, 0x17F);

        const int SESSION_ADD_CHUNK = 64;
        const int MIN_PAGE_SIDE = 64;

        [MenuItem("Assets/NowUI/Bake Font Glyphs")]
        static void BakeSelection()
        {
            var fonts = CollectSelectedFonts();

            if (fonts.Count == 0)
            {
                Debug.Log("NowUI: select NowFont or NowFontFamily assets to bake their authored characters.");
                return;
            }

            var allGlyphFonts = new List<NowFont>();

            for (int i = 0; i < fonts.Count; ++i)
            {
                if (BakesAllGlyphs(fonts[i]))
                    allGlyphFonts.Add(fonts[i]);
            }

            if (allGlyphFonts.Count > 0 && !ConfirmBakeAll(allGlyphFonts))
                return;

            try
            {
                for (int i = 0; i < fonts.Count; ++i)
                {
                    var font = fonts[i];
                    EditorUtility.DisplayProgressBar("Bake Font Glyphs", font.name, i / (float)fonts.Count);

                    if (TryBake(font, out string error))
                        Debug.Log($"NowUI: baked {font.bakedGlyphCount} glyph records into {font.bakedPageCount} page(s) for {font.name}.");
                    else
                        Debug.LogError($"NowUI: failed to bake {font.name}\n{error}");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>A font bakes all of its glyphs unless a character subset was authored
        /// and its last bake used that subset.</summary>
        public static bool BakesAllGlyphs(NowFont font)
        {
            return font != null && (font.bakedAllGlyphs || string.IsNullOrEmpty(font.bakedCharacters));
        }

        /// <summary>Number of codepoints the font maps; what "all glyphs" bakes.</summary>
        public static int CountAllGlyphs(NowFont font)
        {
            return CollectAllCodepoints(font).Count;
        }

        /// <summary>
        /// Page count and asset bytes a bake of <paramref name="glyphCount"/> glyphs is
        /// expected to take at the font's current settings. Glyph cells are estimated at
        /// their full padded size; real bakes land close to this, a page over at most
        /// when a font has many glyphs wider than its em box.
        /// </summary>
        public static void EstimateBake(NowFont font, int glyphCount, out int pageCount, out int pageSide, out long bytes)
        {
            pageCount = 0;
            pageSide = 0;
            bytes = 0;

            if (font == null || glyphCount <= 0)
                return;

            int cell = font.GetBaseDynamicGlyphSize() + font.GetBaseDynamicPixelRange() + NowFont.DYNAMIC_GLYPH_PADDING * 2;
            int maxSide = font.GetDynamicPageSize(cell);
            int side = MIN_PAGE_SIDE;

            while (side < cell)
                side *= 2;

            while (side < maxSide && (long)(side / cell) * (side / cell) < glyphCount)
                side *= 2;

            long cellsPerPage = Math.Max(1L, (long)(side / cell) * (side / cell));
            pageSide = side;
            pageCount = (int)Math.Min(int.MaxValue, (glyphCount + cellsPerPage - 1) / cellsPerPage);
            bytes = pageCount * (long)side * side * 4;
        }

        public static string DescribeEstimate(NowFont font, int glyphCount)
        {
            EstimateBake(font, glyphCount, out int pageCount, out int pageSide, out long bytes);
            return $"{glyphCount:N0} glyphs, about {pageCount} page(s) of {pageSide} px, ~{bytes / (1024f * 1024f):0.0} MB stored in the asset.";
        }

        /// <summary>
        /// Asks before baking every glyph of the given fonts, showing what it will cost:
        /// a Latin face is a few megabytes, a CJK face can be hundreds. The choice stays
        /// with the user either way.
        /// </summary>
        public static bool ConfirmBakeAll(IList<NowFont> fonts)
        {
            const int MAX_LISTED_FONTS = 8;

            if (fonts == null || fonts.Count == 0)
                return false;

            var message = new StringBuilder();
            long totalBytes = 0;

            for (int i = 0; i < fonts.Count; ++i)
            {
                int glyphCount = CountAllGlyphs(fonts[i]);
                EstimateBake(fonts[i], glyphCount, out _, out _, out long bytes);
                totalBytes += bytes;

                if (i < MAX_LISTED_FONTS)
                    message.AppendLine($"{fonts[i].name}: {DescribeEstimate(fonts[i], glyphCount)}");
                else if (i == MAX_LISTED_FONTS)
                    message.AppendLine($"... and {fonts.Count - MAX_LISTED_FONTS} more.");
            }

            if (fonts.Count > 1)
                message.AppendLine($"Total: ~{totalBytes / (1024f * 1024f):0.0} MB.");

            message.Append("Glyphs outside a bake still render on demand; bake a character subset instead when the size is more than the font is worth.");

            return EditorUtility.DisplayDialog(
                fonts.Count == 1 ? $"Bake all glyphs of {fonts[0].name}?" : $"Bake all glyphs of {fonts.Count} fonts?",
                message.ToString(),
                "Bake",
                "Cancel");
        }

        [MenuItem("Assets/NowUI/Bake Font Glyphs", true)]
        static bool ValidateBakeSelection()
        {
            return CollectSelectedFonts().Count > 0;
        }

        static List<NowFont> CollectSelectedFonts()
        {
            var fonts = new List<NowFont>();
            var selection = Selection.objects;

            for (int i = 0; i < selection.Length; ++i)
            {
                switch (selection[i])
                {
                    case NowFont font:
                        AddFont(fonts, font);
                        break;
                    case NowFontFamily family:
                        AddFont(fonts, family.regular);
                        AddFont(fonts, family.bold);
                        AddFont(fonts, family.italic);
                        AddFont(fonts, family.boldItalic);
                        break;
                }
            }

            return fonts;
        }

        static void AddFont(List<NowFont> fonts, NowFont font)
        {
            if (font != null && !fonts.Contains(font))
                fonts.Add(font);
        }

        public static string BuildRange(int firstCodepoint, int lastCodepoint)
        {
            var builder = new StringBuilder(Mathf.Max(0, lastCodepoint - firstCodepoint + 1));

            for (int codepoint = firstCodepoint; codepoint <= lastCodepoint; ++codepoint)
                builder.Append(char.ConvertFromUtf32(codepoint));

            return builder.ToString();
        }

        /// <summary>Appends the codepoints of <paramref name="addition"/> that
        /// <paramref name="existing"/> lacks, keeping the authored order readable.</summary>
        public static string Merge(string existing, string addition)
        {
            if (string.IsNullOrEmpty(addition))
                return existing ?? string.Empty;

            var seen = new HashSet<int>();
            var builder = new StringBuilder((existing?.Length ?? 0) + addition.Length);

            if (!string.IsNullOrEmpty(existing))
            {
                builder.Append(existing);

                for (int i = 0; i < existing.Length; ++i)
                    seen.Add(NowFont.ReadCodepoint(existing, ref i));
            }

            for (int i = 0; i < addition.Length; ++i)
            {
                int codepoint = NowFont.ReadCodepoint(addition, ref i);

                if (codepoint > 0 && seen.Add(codepoint))
                    builder.Append(char.ConvertFromUtf32(codepoint));
            }

            return builder.ToString();
        }

        /// <summary>Repeats the font's last bake: every glyph, or the authored characters.</summary>
        public static bool TryBake(NowFont font, out string error)
        {
            return BakesAllGlyphs(font)
                ? TryBakeAll(font, out error)
                : TryBake(font, font.bakedCharacters, out error);
        }

        /// <summary>Bakes every codepoint the font maps into pages stored on its asset.</summary>
        public static bool TryBakeAll(NowFont font, out string error)
        {
            if (!TryGetAssetPath(font, out string path, out error))
                return false;

            if (!TryBakeAllPages(font, out var pages, out error))
                return false;

            StorePages(font, path, font.bakedCharacters, true, pages);
            return true;
        }

        /// <summary>
        /// Bakes <paramref name="characters"/> into pages stored as sub-assets of the font,
        /// replacing any previous bake, and records the characters as the font's authored set.
        /// </summary>
        public static bool TryBake(NowFont font, string characters, out string error)
        {
            if (!TryGetAssetPath(font, out string path, out error))
                return false;

            if (!TryBakePages(font, characters, out var pages, out error))
                return false;

            StorePages(font, path, characters, false, pages);
            return true;
        }

        static bool TryGetAssetPath(NowFont font, out string path, out string error)
        {
            path = font != null ? AssetDatabase.GetAssetPath(font) : null;

            if (string.IsNullOrEmpty(path))
            {
                error = "Baked pages are stored as sub-assets, so the font must be a saved asset.";
                return false;
            }

            error = null;
            return true;
        }

        static void StorePages(NowFont font, string path, string characters, bool allGlyphs, List<NowFont.BakedPage> pages)
        {
            font.ClearDynamicCache();
            RemoveBakedTextures(font, path);

            for (int i = 0; i < pages.Count; ++i)
            {
                var texture = pages[i].texture;
                texture.name = $"{font.name} Baked Page {i + 1}";
                AssetDatabase.AddObjectToAsset(texture, font);
                SetTextureReadable(texture, false);
            }

            font.SetBakedPages(characters, allGlyphs, pages.ToArray());
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Removes the baked pages from the asset; the authored characters stay.</summary>
        public static void Clear(NowFont font)
        {
            if (font == null)
                return;

            string path = AssetDatabase.GetAssetPath(font);
            font.ClearDynamicCache();

            if (!string.IsNullOrEmpty(path))
                RemoveBakedTextures(font, path);

            font.SetBakedPages(font.bakedCharacters, false, null);
            EditorUtility.SetDirty(font);

            if (!string.IsNullOrEmpty(path))
                AssetDatabase.SaveAssets();
        }

        static void RemoveBakedTextures(NowFont font, string path)
        {
            var pages = font.GetBakedPages();

            if (pages == null)
                return;

            for (int i = 0; i < pages.Length; ++i)
            {
                var texture = pages[i].texture;

                if (texture == null || AssetDatabase.GetAssetPath(texture) != path)
                    continue;

                AssetDatabase.RemoveObjectFromAsset(texture);
                Object.DestroyImmediate(texture, true);
            }
        }

        /// <summary>
        /// The editor keeps texture data for serialization either way; clearing the flag
        /// only tells players to drop the CPU copy after upload, halving the page's memory.
        /// </summary>
        static void SetTextureReadable(Texture2D texture, bool readable)
        {
            var serialized = new SerializedObject(texture);
            var property = serialized.FindProperty("m_IsReadable");

            if (property == null)
                return;

            property.boolValue = readable;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Bakes <paramref name="characters"/> into in-memory pages without touching the
        /// asset database. Every page is rasterized at the font's current glyph size and
        /// base pixel range through the runtime compiler session; pages start at the
        /// smallest power-of-two side that holds the set and spill into further pages
        /// only past the font's configured page size, like runtime pages do.
        /// </summary>
        internal static bool TryBakePages(
            NowFont font,
            string characters,
            out List<NowFont.BakedPage> pages,
            out string error)
        {
            return TryBakeCodepoints(font, CollectCodepoints(characters), characters, out pages, out error);
        }

        /// <summary>Bakes every codepoint the font's cmap maps, in codepoint order.</summary>
        internal static bool TryBakeAllPages(
            NowFont font,
            out List<NowFont.BakedPage> pages,
            out string error)
        {
            return TryBakeCodepoints(font, CollectAllCodepoints(font), null, out pages, out error);
        }

        static List<int> CollectAllCodepoints(NowFont font)
        {
            var codepoints = new List<int>();

            if (font == null || !font.HasEmbeddedSource)
                return codepoints;

            var glyphs = NowFontGlyphCatalog.Load(font).glyphs;
            var seen = new HashSet<int>();

            for (int i = 0; i < glyphs.Length; ++i)
            {
                int codepoint = glyphs[i].codepoint;

                if (codepoint > 0 && codepoint != '\n' && codepoint != '\r' && codepoint != '\t' && seen.Add(codepoint))
                    codepoints.Add(codepoint);
            }

            return codepoints;
        }

        static bool TryBakeCodepoints(
            NowFont font,
            List<int> codepoints,
            string shapedText,
            out List<NowFont.BakedPage> pages,
            out string error)
        {
            pages = null;

            if (font == null)
            {
                error = "No font to bake.";
                return false;
            }

            if (!font.TryGetSourceBytes(out var fontData))
            {
                error = $"{font.name} has no embedded source bytes; compile it from a font file first.";
                return false;
            }

            if (NowUI.NowFontCompiler.IsColorFont(fontData))
            {
                error = $"{font.name} is a color font; color glyphs bake on demand only.";
                return false;
            }

            if (codepoints == null || codepoints.Count == 0)
            {
                error = "No characters to bake.";
                return false;
            }

            int atlasSize = font.GetBaseDynamicGlyphSize();
            int pixelRange = font.GetBaseDynamicPixelRange();
            int requiredSide = atlasSize + pixelRange + NowFont.DYNAMIC_GLYPH_PADDING * 2;
            int maxSide = font.GetDynamicPageSize(requiredSide);
            int side = MIN_PAGE_SIDE;

            while (side < requiredSide)
                side *= 2;

            if (side > maxSide)
            {
                error = $"A {atlasSize} px glyph with a {pixelRange} px range needs a {side} px page, above the font's {maxSide} px page size.";
                return false;
            }

            bool packedSdf16 = font.ResolvePackedManagedSdf16();
            var shapedIndices = CollectShapedGlyphIndices(font, shapedText, codepoints);
            var result = new List<NowFont.BakedPage>();

            while (true)
            {
                if (TryBakeFromSide(
                    fontData,
                    codepoints,
                    shapedIndices,
                    atlasSize,
                    pixelRange,
                    side,
                    maxSide,
                    packedSdf16,
                    result,
                    out bool grow,
                    out error))
                {
                    break;
                }

                DestroyPages(result);

                if (!grow)
                    return false;

                side *= 2;
            }

            pages = result;
            error = null;
            return true;
        }

        static List<int> CollectCodepoints(string characters)
        {
            var codepoints = new List<int>();

            if (string.IsNullOrEmpty(characters))
                return codepoints;

            var seen = new HashSet<int>();

            for (int i = 0; i < characters.Length; ++i)
            {
                int codepoint = NowFont.ReadCodepoint(characters, ref i);

                if (codepoint == '\n' || codepoint == '\r')
                    continue;

                if (codepoint == '\t')
                    codepoint = ' ';

                if (codepoint > 0 && seen.Add(codepoint))
                    codepoints.Add(codepoint);
            }

            return codepoints;
        }

        /// <summary>
        /// Glyph indices the shaper produces for the authored text: each codepoint on its
        /// own, plus every whitespace-free run so ligatures and contextual forms that only
        /// appear between neighbours are included. Empty when shaping is unavailable.
        /// </summary>
        static List<int> CollectShapedGlyphIndices(NowFont font, string characters, List<int> codepoints)
        {
            var indices = new List<int>();
            var seen = new HashSet<int>();

            for (int i = 0; i < codepoints.Count; ++i)
                AddShapedIndices(font, char.ConvertFromUtf32(codepoints[i]), indices, seen);

            if (string.IsNullOrEmpty(characters))
                return indices;

            int runStart = -1;

            for (int i = 0; i <= characters.Length; ++i)
            {
                bool breaks = i == characters.Length || char.IsWhiteSpace(characters[i]);

                if (!breaks)
                {
                    if (runStart < 0)
                        runStart = i;

                    continue;
                }

                if (runStart >= 0 && i - runStart > 1)
                    AddShapedIndices(font, characters.Substring(runStart, i - runStart), indices, seen);

                runStart = -1;
            }

            return indices;
        }

        static void AddShapedIndices(NowFont font, string text, List<int> indices, HashSet<int> seen)
        {
            if (!font.TryGetShapedRun(text, out var run))
                return;

            for (int i = 0; i < run.Length; ++i)
            {
                int glyphIndex = (int)run[i].glyphIndex;

                if (glyphIndex > 0 && seen.Add(glyphIndex))
                    indices.Add(glyphIndex);
            }
        }

        /// <summary>
        /// Grow-then-spill: while the first page is below the maximum side, a full atlas
        /// asks the caller to restart at twice the side; at the maximum, a full atlas seals
        /// the page and continues on a fresh one, matching what the runtime does.
        /// </summary>
        static bool TryBakeFromSide(
            byte[] fontData,
            List<int> codepoints,
            List<int> shapedIndices,
            int atlasSize,
            int pixelRange,
            int side,
            int maxSide,
            bool packedSdf16,
            List<NowFont.BakedPage> pages,
            out bool grow,
            out string error)
        {
            grow = false;
            NowUI.NowFontCompiler.DynamicSession session = null;

            try
            {
                if (!TryCreateSession(fontData, atlasSize, pixelRange, side, packedSdf16, out session, out error))
                    return false;

                var records = new List<NowFontAtlasInfo.Glyph>(codepoints.Count * 2);
                var coveredIndices = new HashSet<int>();
                var chunk = new int[SESSION_ADD_CHUNK];
                int offset = 0;
                int chunkLimit = SESSION_ADD_CHUNK;

                while (offset < codepoints.Count)
                {
                    int count = Mathf.Min(chunkLimit, codepoints.Count - offset);
                    codepoints.CopyTo(offset, chunk, 0, count);
                    var status = session.TryAddGlyphs(chunk, count, records, out string addError);

                    if (status == NowUI.NowFontCompiler.DynamicSession.AddResult.Ok)
                    {
                        offset += count;
                        chunkLimit = SESSION_ADD_CHUNK;
                        continue;
                    }

                    if (status != NowUI.NowFontCompiler.DynamicSession.AddResult.AtlasFull)
                    {
                        error = addError ?? "The font compiler session failed to add glyphs.";
                        return false;
                    }

                    if (count > 1)
                    {
                        chunkLimit = Mathf.Max(1, count / 2);
                        continue;
                    }

                    if (!TrySpill(fontData, atlasSize, pixelRange, side, maxSide, packedSdf16, pages, records, coveredIndices, ref session, out grow, out error))
                        return false;
                }

                if (session.supportsGlyphIndexBaking && shapedIndices.Count > 0)
                {
                    var pending = new List<int>(shapedIndices.Count);

                    for (int i = 0; i < shapedIndices.Count; ++i)
                    {
                        if (!coveredIndices.Contains(shapedIndices[i]) && !IsIndexCovered(records, session, shapedIndices[i]))
                            pending.Add(shapedIndices[i]);
                    }

                    offset = 0;
                    chunkLimit = SESSION_ADD_CHUNK;

                    while (offset < pending.Count)
                    {
                        int count = Mathf.Min(chunkLimit, pending.Count - offset);
                        pending.CopyTo(offset, chunk, 0, count);
                        int before = records.Count;
                        var status = session.TryAddGlyphsByIndex(chunk, count, records, out string addError);

                        if (status == NowUI.NowFontCompiler.DynamicSession.AddResult.Ok)
                        {
                            for (int i = before; i < records.Count; ++i)
                            {
                                var record = records[i];
                                record.unicode = NowFont.EncodeGlyphIndexKey(record.unicode);
                                records[i] = record;
                            }

                            offset += count;
                            chunkLimit = SESSION_ADD_CHUNK;
                            continue;
                        }

                        if (status != NowUI.NowFontCompiler.DynamicSession.AddResult.AtlasFull)
                        {
                            error = addError ?? "The font compiler session failed to add shaped glyphs.";
                            return false;
                        }

                        if (count > 1)
                        {
                            chunkLimit = Mathf.Max(1, count / 2);
                            continue;
                        }

                        if (!TrySpill(fontData, atlasSize, pixelRange, side, maxSide, packedSdf16, pages, records, coveredIndices, ref session, out grow, out error))
                            return false;
                    }
                }

                if (records.Count == 0 && pages.Count == 0)
                {
                    error = "None of the characters exist in the font.";
                    return false;
                }

                if (records.Count > 0)
                {
                    if (!TrySealPage(session, records, atlasSize, pixelRange, coveredIndices, out var page, out error))
                        return false;

                    pages.Add(page);
                }

                error = null;
                return true;
            }
            finally
            {
                session?.Dispose();
            }
        }

        static bool TryCreateSession(
            byte[] fontData,
            int atlasSize,
            int pixelRange,
            int side,
            bool packedSdf16,
            out NowUI.NowFontCompiler.DynamicSession session,
            out string error)
        {
            try
            {
                if (NowUI.NowFontCompiler.DynamicSession.TryCreate(fontData, atlasSize, pixelRange, side, packedSdf16, out session, out error))
                    return true;
            }
            catch (DllNotFoundException)
            {
                error = "The font needs the native font compiler plugin, which is not available on this platform.";
            }
            catch (EntryPointNotFoundException)
            {
                error = "The font needs the native font compiler plugin, which is outdated on this platform.";
            }
            catch (BadImageFormatException)
            {
                error = "The font needs the native font compiler plugin, which has the wrong architecture on this platform.";
            }

            session = null;
            return false;
        }

        /// <summary>A single glyph no longer fits: grow the page while it may, otherwise
        /// seal it and continue on a fresh session of the same side.</summary>
        static bool TrySpill(
            byte[] fontData,
            int atlasSize,
            int pixelRange,
            int side,
            int maxSide,
            bool packedSdf16,
            List<NowFont.BakedPage> pages,
            List<NowFontAtlasInfo.Glyph> records,
            HashSet<int> coveredIndices,
            ref NowUI.NowFontCompiler.DynamicSession session,
            out bool grow,
            out string error)
        {
            grow = false;

            if (side < maxSide)
            {
                grow = true;
                error = null;
                return false;
            }

            if (records.Count == 0)
            {
                error = $"A glyph does not fit an empty {side} px page.";
                return false;
            }

            if (!TrySealPage(session, records, atlasSize, pixelRange, coveredIndices, out var page, out error))
                return false;

            pages.Add(page);
            records.Clear();
            session.Dispose();
            session = null;
            return TryCreateSession(fontData, atlasSize, pixelRange, side, packedSdf16, out session, out error);
        }

        static bool IsIndexCovered(List<NowFontAtlasInfo.Glyph> records, NowUI.NowFontCompiler.DynamicSession session, int glyphIndex)
        {
            for (int i = 0; i < records.Count; ++i)
            {
                int key = records[i].unicode;

                if (key < 0)
                {
                    if (NowFont.EncodeGlyphIndexKey(glyphIndex) == key)
                        return true;
                }
                else if (session.TryGetGlyphIndex(key, out int recordIndex) && recordIndex == glyphIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Copies the session atlas into a page texture and registers every codepoint
        /// record under its glyph-index key as well, so shaped and per-codepoint draws
        /// resolve the same rasterized cell instead of baking the glyph twice.
        /// </summary>
        static bool TrySealPage(
            NowUI.NowFontCompiler.DynamicSession session,
            List<NowFontAtlasInfo.Glyph> records,
            int atlasSize,
            int pixelRange,
            HashSet<int> coveredIndices,
            out NowFont.BakedPage page,
            out string error)
        {
            page = default;

            if (session.supportsGlyphIndexBaking)
            {
                int codepointRecords = records.Count;

                for (int i = 0; i < codepointRecords; ++i)
                {
                    var record = records[i];

                    if (record.unicode < 0)
                    {
                        coveredIndices.Add(-1 - record.unicode);
                        continue;
                    }

                    if (!session.TryGetGlyphIndex(record.unicode, out int glyphIndex) || !coveredIndices.Add(glyphIndex))
                        continue;

                    record.unicode = NowFont.EncodeGlyphIndexKey(glyphIndex);
                    records.Add(record);
                }
            }

            byte[] buffer = null;

            if (!session.TryCopyAtlas(ref buffer, out error))
                return false;

            int side = session.AtlasSide;
            var texture = new Texture2D(side, side, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(buffer);
            texture.Apply(false, false);

            page = new NowFont.BakedPage
            {
                texture = texture,
                atlasSize = atlasSize,
                pixelRange = pixelRange,
                distanceRange = Mathf.RoundToInt(session.DistanceRange),
                size = Mathf.RoundToInt(session.Size),
                packedSdf16 = session.usesPackedSdf16,
                metrics = session.Metrics,
                glyphs = records.ToArray()
            };

            error = null;
            return true;
        }

        static void DestroyPages(List<NowFont.BakedPage> pages)
        {
            for (int i = 0; i < pages.Count; ++i)
            {
                if (pages[i].texture != null)
                    Object.DestroyImmediate(pages[i].texture);
            }

            pages.Clear();
        }
    }
}
