// The resource provider the browser host installs: the exported NowUI fixtures, fetched over HTTP instead of read
// from disk.
//
// WHY THIS FILE EXISTS AT ALL, given that Standalone/Tests/Support/NowStandaloneTestResources.cs already parses this
// exact fixture format against these exact shim types. Two reasons, in order of how much they cost:
//
//   1. `UnityEngine.Shader`'s only constructor is `internal Shader(string, NowShaderInfo)` (Graphics/Shader.cs:39),
//      and NowUI.Engine's friend list is `NowUI.Engine.Tests` and `Tests` - not `NowUI.Web`. The test provider says
//      `new Shader(name, info)`; this one cannot, and reaches the same constructor by reflection (NewShader below).
//      That is a shim gap, not a design choice, and it is reported as one. The fix is one line in
//      NowUI.Engine.csproj: <InternalsVisibleTo Include="NowUI.Web" />. Same finding as
//      Docs/Standalone/M2-ShaderPort.md section 9 item 6.
//   2. Every read in the test provider is File.ReadAllBytes off a directory that a browser does not have. The bytes
//      here arrive over fetch, before construction, into m_Files - which also means Load() can stay synchronous, as
//      INowResourceProvider requires it to be.
//
// EVERYTHING ELSE IS THE TEST PROVIDER'S LOGIC, deliberately kept line-for-line recognisable: the same required-path
// and required-shader lists, the same "declare every property or throw" shader parse, the same seed-then-overwrite
// material build, the same refusal of a texture-bearing template, the same lazy font-family build, the same
// dynamic-only face check, the same byte-count check, the same reflection into the four family slots, the same
// refusal of fallbacks the fixtures do not carry, and the same same-instance-per-path contract. Where a comment there
// explains a decision, that decision is unchanged here; read that file for the reasoning.
//
// Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2 (the fixture format), Docs/Standalone/M2-Scouting.md
// section 6 (the resource provider is the first thing a browser host needs).
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using NowUI;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>
    /// Serves NowUI's built-in resources to the browser host, from the fixture tree the build copied into
    /// <c>wwwroot/Fixtures</c>. Construct it with <see cref="CreateAsync"/>: the fetches must all have landed before
    /// the first <see cref="Load"/>, because <see cref="INowResourceProvider"/> is synchronous.
    /// </summary>
    public sealed class WebResourceProvider : INowResourceProvider
    {
        /// <summary>The resource path <c>Now.defaultFont</c> loads.</summary>
        public const string fontFamilyPath = "NowUI/NotoSans";

        /// <summary>
        /// The material templates the core reaches through <c>Resources.Load</c> outside the UGUI paths. Every one
        /// must resolve or a draw that needs it logs an error through <c>Now.LoadRequiredResource</c>; checked at
        /// construction so the failure names the fixture rather than surfacing three frames later as a null material.
        /// </summary>
        private static readonly string[] k_RequiredMaterialPaths =
        {
            "NowUI/UIMaterial",         // Now._defaultMaterial - the rounded panel
            "NowUI/TxtMaterial",        // text, and every dynamic font page - "Score: 1200"
            "NowUI/TxtMaterialRGBA",    // colour (bitmap) faces
            "NowUI/GradientMaterial",   // NowGradient
            "NowUI/GlassMaterial",      // NowGlass
            "NowUI/GlassBlurMaterial",  // NowGlass blur pass
            "NowUI/RippleMaterial",     // NowRipple
            "NowUI/BezierMaterial",     // NowLine, node-graph links
            "NowUI/SdfMaterial",        // NowSdf
        };

        /// <summary>Shader names the core reaches through <c>Shader.Find</c>; a null there is a silently wrong branch.</summary>
        private static readonly string[] k_RequiredShaderNames =
        {
            "NowUI/UI Bezier",              // NowLine
            "NowUI/UI Ripple",              // NowRipple
            "NowUI/Color Picker",           // NowValueControls
            "NowUI/SDF Scene",              // NowSdf
            "Hidden/NowUI/SDF Image Field", // NowSdfImageField
        };

        // NowFontFamily's four slots and NowFontAsset._fallbacks are [SerializeField] privates with no setters
        // (design H.7). Reflection is how a provider outside NowUI.Runtime fills them - the same access
        // NowStandaloneTestResources and NowFontResolutionTests already use.
        private static readonly FieldInfo k_RegularField = FamilyField("_regular");
        private static readonly FieldInfo k_BoldField = FamilyField("_bold");
        private static readonly FieldInfo k_ItalicField = FamilyField("_italic");
        private static readonly FieldInfo k_BoldItalicField = FamilyField("_boldItalic");
        private static readonly FieldInfo k_FallbacksField = FallbacksField();

        // The internal Shader constructor, resolved once. See the header: NowUI.Web is not a friend assembly.
        private static readonly ConstructorInfo k_ShaderCtor = ShaderConstructor();

        private readonly string m_FixtureRoot;

        /// <summary>Fixture-relative path (forward slashes) to the bytes fetched for it.</summary>
        private readonly Dictionary<string, byte[]> m_Files;

        private readonly Dictionary<string, UnityEngine.Object> m_Objects =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        private readonly Dictionary<string, Shader> m_Shaders =
            new Dictionary<string, Shader>(StringComparer.Ordinal);

        private WebResourceProvider(string fixtureRoot, Dictionary<string, byte[]> files)
        {
            m_FixtureRoot = fixtureRoot;
            m_Files = files;

            LoadShaders();
            LoadMaterials();
            AliasCanvasMaterials();
            RequireResources();
        }

        /// <summary>The base URL the fixtures were fetched from. For diagnostics.</summary>
        public string fixtureRoot
        {
            get { return m_FixtureRoot; }
        }

        /// <summary>Bytes fetched, summed. Reported once at start-up so the page's cost is visible, not guessed.</summary>
        public int fetchedByteCount
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<string, byte[]> entry in m_Files)
                    total += entry.Value.Length;
                return total;
            }
        }

        /// <summary>Number of files fetched.</summary>
        public int fetchedFileCount
        {
            get { return m_Files.Count; }
        }

        // -----------------------------------------------------------------------------------------------------------
        // Fetching
        // -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Fetches the fixture tree under <paramref name="fixtureRoot"/> (an absolute URL, e.g.
        /// <c>https://host/Fixtures</c>) and builds the provider from it.
        /// </summary>
        /// <remarks>
        /// Three waves, because the tree describes itself and nothing is hard-coded beyond the two table files and the
        /// family entry point: the tables and the family manifest; then the four face files the family names; then the
        /// TTF each face names. Within a wave the requests go out together, so the browser opens them in parallel.
        /// </remarks>
        public static async Task<WebResourceProvider> CreateAsync(HttpClient http, string fixtureRoot)
        {
            if (http == null)
                throw new ArgumentNullException(nameof(http));
            if (string.IsNullOrEmpty(fixtureRoot))
                throw new ArgumentException("A fixture root URL is required.", nameof(fixtureRoot));

            string root = fixtureRoot.TrimEnd('/');
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

            await FetchAll(http, root, files, new[]
            {
                "shaders.json",
                "materials.json",
                "NowUI/NotoSans.family.json",
            }).ConfigureAwait(false);

            // The family names its four faces; the faces name their TTFs. Reading the manifest rather than assuming
            // the file names keeps this provider honest against a re-export that renames a face.
            List<string> faceFiles = new List<string>(4);
            using (JsonDocument family = Parse(files["NowUI/NotoSans.family.json"]))
            {
                JsonElement faces = family.RootElement.GetProperty("faces");
                AddFace(faceFiles, faces, "regular");
                AddFace(faceFiles, faces, "bold");
                AddFace(faceFiles, faces, "italic");
                AddFace(faceFiles, faces, "boldItalic");
            }

            await FetchAll(http, root, files, faceFiles).ConfigureAwait(false);

            List<string> byteFiles = new List<string>(faceFiles.Count);

            // Baked atlas pages, fetched in the SAME wave as the TTFs rather than a fourth one: they are four more
            // parallel requests, not four more round trips. They are OPTIONAL - see the optional set below.
            List<string> pageFiles = new List<string>(faceFiles.Count);

            for (int i = 0; i < faceFiles.Count; i++)
            {
                using (JsonDocument face = Parse(files[faceFiles[i]]))
                {
                    JsonElement bytes = face.RootElement.GetProperty("fontBytesFile");
                    if (bytes.ValueKind == JsonValueKind.Null)
                    {
                        throw new InvalidOperationException(
                            "Font fixture '" + faceFiles[i] + "' has no source bytes, so nothing can bake from it.");
                    }

                    byteFiles.Add("NowUI/" + bytes.GetString());
                    AddBakedPageFiles(pageFiles, face.RootElement);
                }
            }

            // The TTF is load-bearing - nothing renders without it, so a missing one throws. A baked page is an
            // optimisation over a path that still works: losing one costs a slower first frame, and refusing to boot
            // over it would trade a slow page for a dead one. So pages fetch under the optional set and a miss is a
            // warning at build time (BuildFace), not an exception here.
            List<string> wave = new List<string>(byteFiles.Count + pageFiles.Count);
            wave.AddRange(byteFiles);
            wave.AddRange(pageFiles);

            await FetchAll(http, root, files, wave, new HashSet<string>(pageFiles, StringComparer.Ordinal))
                .ConfigureAwait(false);

            return new WebResourceProvider(root, files);
        }

        /// <summary>
        /// Appends the <c>bakedPages[].file</c> entries a face declares, if any. A face with no <c>bakedPages</c>
        /// member is the normal older shape and adds nothing.
        /// </summary>
        private static void AddBakedPageFiles(List<string> into, JsonElement face)
        {
            JsonElement pages;
            if (!face.TryGetProperty("bakedPages", out pages) || pages.ValueKind != JsonValueKind.Array)
                return;

            foreach (JsonElement page in pages.EnumerateArray())
            {
                JsonElement file;
                if (!page.TryGetProperty("file", out file) || file.ValueKind != JsonValueKind.String)
                    continue;

                string path = "NowUI/" + file.GetString();
                if (!into.Contains(path))
                    into.Add(path);
            }
        }

        private static void AddFace(List<string> into, JsonElement faces, string slot)
        {
            JsonElement fileName;
            if (!faces.TryGetProperty(slot, out fileName) || fileName.ValueKind == JsonValueKind.Null)
                return;

            string path = "NowUI/" + fileName.GetString();
            if (!into.Contains(path))
                into.Add(path);
        }

        private static Task FetchAll(
            HttpClient http,
            string root,
            Dictionary<string, byte[]> into,
            IReadOnlyList<string> paths)
        {
            return FetchAll(http, root, into, paths, null);
        }

        /// <summary>
        /// Fetches <paramref name="paths"/> in parallel. A path in <paramref name="optional"/> that does not come back
        /// is simply absent from <paramref name="into"/> afterwards; any other failure throws.
        /// </summary>
        private static async Task FetchAll(
            HttpClient http,
            string root,
            Dictionary<string, byte[]> into,
            IReadOnlyList<string> paths,
            HashSet<string> optional)
        {
            List<string> pending = new List<string>(paths.Count);
            List<Task<byte[]>> tasks = new List<Task<byte[]>>(paths.Count);

            for (int i = 0; i < paths.Count; i++)
            {
                if (into.ContainsKey(paths[i]))
                    continue;

                pending.Add(paths[i]);
                tasks.Add(optional != null && optional.Contains(paths[i])
                    ? FetchOptional(http, root + "/" + paths[i])
                    : Fetch(http, root + "/" + paths[i]));
            }

            byte[][] payloads = await Task.WhenAll(tasks).ConfigureAwait(false);

            for (int i = 0; i < pending.Count; i++)
            {
                // A null payload is an optional fetch that failed. Left OUT of the dictionary rather than stored as
                // null, so every existing reader keeps its "was never fetched" diagnosis instead of a
                // NullReferenceException three calls later.
                if (payloads[i] != null)
                    into[pending[i]] = payloads[i];
            }
        }

        private static async Task<byte[]> Fetch(HttpClient http, string url)
        {
            // GetByteArrayAsync goes through the browser's fetch(); a 404 becomes an HttpRequestException naming the
            // URL, which is the diagnosis the M2 scouting report says both probes needed first.
            try
            {
                return await http.GetByteArrayAsync(url).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException(
                    "Could not fetch the NowUI fixture '" + url + "'. The fixtures are copied into wwwroot/Fixtures " +
                    "by the CopyNowUIFixtures target in NowUI.Web.csproj; they originate from " +
                    "Assets/NowUI/Editor/NowStandaloneAssetExport.cs.", e);
            }
        }

        /// <summary>Like <see cref="Fetch"/>, but returns null with a warning instead of throwing.</summary>
        private static async Task<byte[]> FetchOptional(HttpClient http, string url)
        {
            try
            {
                return await http.GetByteArrayAsync(url).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    "the optional fixture '" + url + "' did not fetch (" + e.Message + "). Whatever needed " +
                    "it falls back to building it at runtime.");
                return null;
            }
        }

        // -----------------------------------------------------------------------------------------------------------
        // INowResourceProvider
        // -----------------------------------------------------------------------------------------------------------

        /// <inheritdoc />
        public UnityEngine.Object Load(string path, Type type)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            UnityEngine.Object result;
            bool known = m_Objects.TryGetValue(path, out result);

            // `result == null` is UnityEngine.Object's fake-null, which is also true for an instance somebody
            // destroyed. Unity would reload such an asset on the next Resources.Load; the font family is the one asset
            // here that a frame can plausibly reach, so it is rebuilt on demand and the materials are not.
            if (!known || result == null)
            {
                if (!string.Equals(path, fontFamilyPath, StringComparison.Ordinal))
                    return known ? result : null;

                result = BuildFontFamily();
                m_Objects[path] = result;
            }

            // Unity's Resources.Load<T> returns null when the asset is not of the requested type.
            if (type != null && !type.IsInstanceOfType(result))
                return null;

            return result;
        }

        /// <inheritdoc />
        public Shader FindShader(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            Shader shader;
            return m_Shaders.TryGetValue(name, out shader) ? shader : null;
        }

        /// <summary>Looks up a template material by resource path; null when the fixtures do not carry one.</summary>
        public Material GetMaterial(string path)
        {
            return Load(path, typeof(Material)) as Material;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Shaders and materials
        // -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// One shim <see cref="Shader"/> per exported program, carrying its declared uniforms and their defaults. The
        /// declarations are the point: <c>Material.HasProperty</c> answers from them for a property nobody assigned,
        /// which is what Unity does and what NowFont, Now and NowSdf gate real work on. In particular
        /// <c>_NowUITextSdfEncoding</c> on TxtMaterial - see M2-ShaderPort.md section 4.4.
        /// </summary>
        private void LoadShaders()
        {
            using (JsonDocument document = ReadJson("shaders.json"))
            {
                foreach (JsonElement entry in document.RootElement.GetProperty("shaders").EnumerateArray())
                {
                    string name = entry.GetProperty("name").GetString();
                    NowShaderInfo info = new NowShaderInfo(name, entry.GetProperty("passCount").GetInt32());

                    foreach (JsonElement property in entry.GetProperty("properties").EnumerateArray())
                    {
                        string propertyName = property.GetProperty("name").GetString();
                        string propertyType = property.GetProperty("type").GetString();

                        switch (propertyType)
                        {
                            case "Float":
                            case "Range":
                                info.DeclareFloat(propertyName, ReadFloat(property, "defaultFloat"));
                                break;
                            case "Color":
                            case "Vector":
                                info.DeclareVector(propertyName, ReadVector4(property, "defaultVector"));
                                break;
                            case "Texture":
                                info.DeclareTexture(propertyName);
                                break;
                            default:
                                // A dropped declaration is a HasProperty that answers false where Unity answers true.
                                throw new NotSupportedException(
                                    "Shader '" + name + "' declares property '" + propertyName +
                                    "' of unhandled type '" + propertyType + "'.");
                        }
                    }

                    info.keywords = ReadStringArray(entry, "keywords");
                    m_Shaders[name] = NewShader(name, info);
                }
            }
        }

        /// <summary>
        /// One shim <see cref="Material"/> per exported template. <c>new Material(shader)</c> seeds the shader's
        /// declared defaults exactly as Unity does, and the values the .mat stores are written over them.
        /// </summary>
        private void LoadMaterials()
        {
            using (JsonDocument document = ReadJson("materials.json"))
            {
                foreach (JsonElement entry in document.RootElement.GetProperty("materials").EnumerateArray())
                {
                    string resourcePath = entry.GetProperty("resourcePath").GetString();
                    string shaderName = entry.GetProperty("shader").GetString();

                    Shader shader;
                    if (!m_Shaders.TryGetValue(shaderName, out shader))
                    {
                        throw new InvalidOperationException(
                            "Material '" + resourcePath + "' uses shader '" + shaderName +
                            "', which shaders.json does not declare. The two fixtures are out of step; re-export.");
                    }

                    Material material = new Material(shader);
                    material.name = entry.GetProperty("name").GetString();
                    material.hideFlags = HideFlags.HideAndDontSave;
                    material.renderQueue = entry.GetProperty("renderQueue").GetInt32();

                    foreach (JsonElement value in entry.GetProperty("floats").EnumerateArray())
                        material.SetFloat(value.GetProperty("name").GetString(), ReadFloat(value, "value"));

                    foreach (JsonElement value in entry.GetProperty("vectors").EnumerateArray())
                        material.SetVector(value.GetProperty("name").GetString(), ReadVector4(value, "value"));

                    foreach (JsonElement value in entry.GetProperty("colors").EnumerateArray())
                    {
                        Vector4 rgba = ReadVector4(value, "value");
                        material.SetColor(
                            value.GetProperty("name").GetString(),
                            new Color(rgba.x, rgba.y, rgba.z, rgba.w));
                    }

                    // Every sampler in every exported template is unassigned, and an unassigned sampler reads as null
                    // here exactly as in Unity. A template that ever ships with a texture bound would need that
                    // texture exported too, so it is refused rather than quietly dropped.
                    foreach (JsonElement value in entry.GetProperty("textures").EnumerateArray())
                    {
                        if (value.GetProperty("texture").ValueKind != JsonValueKind.Null)
                        {
                            throw new NotSupportedException(
                                "Material '" + resourcePath + "' binds a texture to '" +
                                value.GetProperty("name").GetString() +
                                "'. The fixtures carry no texture payloads; see NowStandaloneAssetExport.");
                        }
                    }

                    string[] keywords = ReadStringArray(entry, "keywords");
                    for (int i = 0; i < keywords.Length; i++)
                        material.EnableKeyword(keywords[i]);

                    m_Objects[resourcePath] = material;
                }
            }
        }

        /// <summary>
        /// Mints the <c>*UGUI</c> material templates that the exporter deliberately drops but the frozen runtime
        /// still insists on, by cloning their non-UGUI twin.
        /// </summary>
        /// <remarks>
        /// <para>MEASURED, not assumed. <c>NowStandaloneAssetExport.cs:196-198</c> skips every resource path ending
        /// in "UGUI" because "NOWUI_UGUI is compiled out of the standalone build". That is true of the UGUI *hosts*
        /// and false of two call sites in code that IS compiled in:</para>
        /// <list type="bullet">
        /// <item><description><c>NowGradient.cs:663</c> loads <c>NowUI/GradientMaterialUGUI</c>, and
        /// <c>NowGradientMaterials.TryGet</c> returns <c>material != null &amp;&amp; canvasMaterial != null</c>
        /// (<c>NowGradient.cs:695</c>). With the template missing, TryGet is false, <c>NowGradient.Draw</c> returns at
        /// <c>NowGradient.cs:967</c>, and EVERY gradient in the browser draws nothing at all - silently, apart from
        /// one resource warning at start-up. That is what <c>?area=gradients</c> captured before this alias existed:
        /// seventeen labelled cells and not one swatch.</description></item>
        /// <item><description><c>NowGlass.cs:334</c> loads <c>NowUI/GlassMaterialUGUI</c>. That one is not a gate -
        /// it is passed to <c>UseGlassMaterial</c> as the canvas twin of a batch key - so glass drew anyway, but it
        /// logged the same start-up warning and left a null in a batch record.</description></item>
        /// </list>
        /// <para>Cloning is sound HERE and only here: the canvas material is used exclusively by the UGUI hosts,
        /// which cannot exist in this build (there is no <c>Canvas</c>, no <c>CanvasRenderer</c>), so nothing ever
        /// renders through the clone. It exists to satisfy a null check. The clone keeps the twin's shader and its
        /// whole property bag, and is given the UGUI name so a diagnostic that prints material names still tells the
        /// truth about which template it came from.</para>
        /// <para>This is a HOST-SIDE workaround for a defect in the export contract, not a fix. The real repair is
        /// one of: export the UGUI templates too (they are three lines in the exporter's skip test), or relax
        /// <c>NowGradientMaterials.TryGet</c> to require only the non-UGUI material. Both are outside this slice -
        /// the first touches the frozen Unity tree, the second touches frozen runtime code.</para>
        /// </remarks>
        private void AliasCanvasMaterials()
        {
            AliasCanvasMaterial("NowUI/GradientMaterial", "NowUI/GradientMaterialUGUI");
            AliasCanvasMaterial("NowUI/GlassMaterial", "NowUI/GlassMaterialUGUI");
            AliasCanvasMaterial("NowUI/UIMaterial", "NowUI/UIMaterialUGUI");
            AliasCanvasMaterial("NowUI/TxtMaterial", "NowUI/TxtMaterialUGUI");
            AliasCanvasMaterial("NowUI/TxtMaterialRGBA", "NowUI/TxtMaterialRGBAUGUI");
            AliasCanvasMaterial("NowUI/RippleMaterial", "NowUI/RippleMaterialUGUI");
        }

        private void AliasCanvasMaterial(string sourcePath, string canvasPath)
        {
            if (m_Objects.ContainsKey(canvasPath))
                return;

            UnityEngine.Object source;
            if (!m_Objects.TryGetValue(sourcePath, out source))
                return;

            Material template = source as Material;
            if (ReferenceEquals(template, null))
                return;

            Material clone = new Material(template);
            clone.name = template.name + "UGUI";
            clone.hideFlags = HideFlags.HideAndDontSave;
            m_Objects[canvasPath] = clone;
        }

        private void RequireResources()
        {
            for (int i = 0; i < k_RequiredMaterialPaths.Length; i++)
            {
                if (!m_Objects.ContainsKey(k_RequiredMaterialPaths[i]))
                {
                    throw new InvalidOperationException(
                        "materials.json (" + m_FixtureRoot + ") is missing the required template '" +
                        k_RequiredMaterialPaths[i] + "'. Re-run NowStandaloneAssetExport.Export.");
                }
            }

            for (int i = 0; i < k_RequiredShaderNames.Length; i++)
            {
                if (!m_Shaders.ContainsKey(k_RequiredShaderNames[i]))
                {
                    throw new InvalidOperationException(
                        "shaders.json (" + m_FixtureRoot + ") is missing the required program '" +
                        k_RequiredShaderNames[i] + "'. Re-run NowStandaloneAssetExport.Export.");
                }
            }
        }

        // -----------------------------------------------------------------------------------------------------------
        // The font family
        // -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the NotoSans family: four faces from their fetched TTFs, bound into a <see cref="NowFontFamily"/>.
        /// Built on first use rather than at construction, so it is built after NowRuntime.Initialize has installed
        /// the host and the backend - the baker allocates a texture, and a texture needs a live backend.
        /// </summary>
        private NowFontFamily BuildFontFamily()
        {
            using (JsonDocument document = ReadJson("NowUI/NotoSans.family.json"))
            {
                JsonElement root = document.RootElement;
                JsonElement faces = root.GetProperty("faces");

                NowFontFamily family = ScriptableObject.CreateInstance<NowFontFamily>();
                family.name = root.GetProperty("name").GetString();
                family.hideFlags = HideFlags.HideAndDontSave;

                k_RegularField.SetValue(family, BuildFace(faces, "regular"));
                k_BoldField.SetValue(family, BuildFace(faces, "bold"));
                k_ItalicField.SetValue(family, BuildFace(faces, "italic"));
                k_BoldItalicField.SetValue(family, BuildFace(faces, "boldItalic"));

                // The exporter omits the CJK/Arabic/emoji/icon fallback families. An empty array, not null, so the
                // family reports the shape a Unity asset with no fallbacks configured would.
                SetFallbacks(family, RequireNoFallbacks(root, "NotoSans.family.json"));

                return family;
            }
        }

        private NowFont BuildFace(JsonElement faces, string slot)
        {
            JsonElement fileNameElement;
            if (!faces.TryGetProperty(slot, out fileNameElement) || fileNameElement.ValueKind == JsonValueKind.Null)
                return null;

            string fileName = fileNameElement.GetString();

            using (JsonDocument document = ReadJson("NowUI/" + fileName))
            {
                JsonElement face = document.RootElement;

                // Refused, not guessed: no NowUI font asset in this project ships a prebaked atlas, so there is no
                // exercised code here to build one with.
                if (!string.Equals(face.GetProperty("kind").GetString(), "dynamic", StringComparison.Ordinal) ||
                    face.GetProperty("atlasWidth").GetInt32() != 0 ||
                    face.GetProperty("atlasInfo").GetProperty("glyphs").GetArrayLength() != 0)
                {
                    throw new NotSupportedException(
                        "Font fixture '" + fileName + "' carries a prebaked atlas. This provider only builds the " +
                        "dynamic faces NowUI actually ships; see NowStandaloneTestResources.BuildFace.");
                }

                JsonElement bytesElement = face.GetProperty("fontBytesFile");
                if (bytesElement.ValueKind == JsonValueKind.Null)
                {
                    throw new InvalidOperationException(
                        "Font fixture '" + fileName + "' has no source bytes, so nothing can bake from it.");
                }

                byte[] fontBytes = ReadBytes("NowUI/" + bytesElement.GetString());
                int expectedCount = face.GetProperty("fontByteCount").GetInt32();

                if (fontBytes.Length != expectedCount)
                {
                    throw new InvalidOperationException(
                        "Font fixture '" + fileName + "' declares " + expectedCount + " source bytes but the fetch " +
                        "returned " + fontBytes.Length + ". The fixtures are inconsistent, or the server rewrote the " +
                        "body; re-export, and check that no transform is applied to .ttf.");
                }

                // The same call the runtime makes for a .ttf.asset (NowFontCompiler.cs:731). The material template is
                // deliberately left null: on a Unity asset _dynamicMaterialTemplate is non-serialized and null there
                // too, so the font loads "NowUI/TxtMaterial" through this very provider when it bakes its first page.
                NowFont font;
                string error;
                if (!NowFontCompiler.TryCompile(
                        fontBytes,
                        face.GetProperty("dynamicAtlasSize").GetInt32(),
                        face.GetProperty("dynamicPixelRange").GetInt32(),
                        out font,
                        out error))
                {
                    throw new InvalidOperationException(
                        "NowFontCompiler rejected the '" + fileName + "' fixture: " + error);
                }

                font.name = face.GetProperty("name").GetString();
                font.hideFlags = HideFlags.HideAndDontSave;

                // TryCompile seeds the two page limits with the runtime defaults; the fixture carries what the asset
                // was actually authored with, which is what the Unity run used.
                font.dynamicPageSize = face.GetProperty("dynamicPageSize").GetInt32();
                font.dynamicMaxAtlasSize = face.GetProperty("dynamicMaxAtlasSize").GetInt32();
                font.dynamicMaxAtlasBytes = face.GetProperty("dynamicMaxAtlasBytes").GetInt32();

                SetFallbacks(font, RequireNoFallbacks(face, fileName));
                InstallBakedPages(font, face, fileName);

                return font;
            }
        }

        /// <summary>
        /// Warms the face's dynamic cache with the atlas pages the exporter baked, so the first frame that draws text
        /// does not have to rasterize printable ASCII.
        ///
        /// WHY THIS IS WORTH CODE. Measured on the shipped bundle in a real browser, the first frame cost ~2.0 s
        /// against ~11 ms for every frame after it, and 85-91% of that was MSDF rasterisation in interpreted
        /// WebAssembly. Unity never sees this because it bakes through HarfBuzz in milliseconds.
        ///
        /// WHAT A BAKED PAGE IS. Not a prebaked atlas - a WARMED DYNAMIC page. NowFont.SetBakedPages hands these to
        /// EnsureBakedPagesLoaded, which turns each into exactly the DynamicAtlasPage the runtime would have
        /// rasterized and registers its glyph keys. After that the lookup path cannot tell the two apart, and a
        /// codepoint outside the bake (the gallery's "Äéîõü ßçø Γαμμα Да" line, say) still bakes on demand onto a
        /// dynamic page beside the baked one.
        ///
        /// EVERY FAILURE LANDS ON TODAY'S BEHAVIOUR. A missing member, a missing file, a short body, a PNG the decoder
        /// refuses, dimensions that disagree with the declaration - each skips that page with one named warning and
        /// leaves the face to rasterize as it does now. Deliberately unlike the fontByteCount check above, which
        /// throws: the TTF is load-bearing and a page is an optimisation. And a page whose atlasSize or pixelRange no
        /// longer match the font needs no check here at all - NowFont.IsBakedPageCurrent leaves it dormant by itself.
        /// </summary>
        private void InstallBakedPages(NowFont font, JsonElement face, string fileName)
        {
            JsonElement declared;
            if (!face.TryGetProperty("bakedPages", out declared) ||
                declared.ValueKind != JsonValueKind.Array ||
                declared.GetArrayLength() == 0)
            {
                return;
            }

            List<NowFont.BakedPage> pages = new List<NowFont.BakedPage>(declared.GetArrayLength());

            foreach (JsonElement page in declared.EnumerateArray())
            {
                NowFont.BakedPage built;
                if (TryBuildBakedPage(page, fileName, out built))
                    pages.Add(built);
            }

            if (pages.Count == 0)
                return;

            string characters = face.TryGetProperty("bakedCharacters", out JsonElement chars) &&
                                chars.ValueKind == JsonValueKind.String
                ? chars.GetString()
                : null;

            font.SetBakedPages(characters, false, pages.ToArray());
        }

        private bool TryBuildBakedPage(JsonElement page, string fileName, out NowFont.BakedPage built)
        {
            built = default;

            string file = page.GetProperty("file").GetString();
            string path = "NowUI/" + file;

            byte[] encoded;
            if (!m_Files.TryGetValue(path, out encoded) || encoded == null)
            {
                Debug.LogWarning(
                    "'" + fileName + "' declares the baked page '" + file + "' but it did not fetch. That " +
                    "face rasterizes its glyphs on demand instead, which is slower and looks the same.");
                return false;
            }

            // Declared for the same reason fontByteCount is: a server or a transform that rewrites the body must
            // produce a named warning rather than a corrupted atlas.
            int declaredBytes = page.GetProperty("pageByteCount").GetInt32();
            if (encoded.Length != declaredBytes)
            {
                Debug.LogWarning(
                    "the baked page '" + file + "' declares " + declaredBytes + " bytes but " +
                    encoded.Length + " arrived; skipping it and rasterizing on demand. Re-export, and check that no " +
                    "transform is applied to .bin.");
                return false;
            }

            int width, height;
            byte[] rgba;
            string error;
            if (!NowWebPng.TryDecode(encoded, out width, out height, out rgba, out error))
            {
                Debug.LogWarning(
                    "the baked page '" + file + "' did not decode (" + error + "); skipping it and " +
                    "rasterizing on demand.");
                return false;
            }

            int declaredWidth = page.GetProperty("width").GetInt32();
            int declaredHeight = page.GetProperty("height").GetInt32();
            if (width != declaredWidth || height != declaredHeight)
            {
                Debug.LogWarning(
                    "the baked page '" + file + "' decoded to " + width + "x" + height + " but declares " +
                    declaredWidth + "x" + declaredHeight + "; skipping it and rasterizing on demand.");
                return false;
            }

            string encoding = page.GetProperty("pageEncoding").GetString();
            if (string.Equals(encoding, "grey8-alpha8", StringComparison.Ordinal))
            {
                // The file holds the two planes of the packed 16-bit distance: the HIGH byte in grey, the LOW byte in
                // alpha. NowWebPng expands colour type 4 to r = g = b = grey, a = alpha, so what arrives is
                // (hi, hi, hi, lo) and the page wants (hi, hi, lo, hi) - R/G/A high, B low, per
                // NowFont.CreateDynamicPageFont. One swap per pixel; measured under a millisecond for 1 Mpx.
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    byte low = rgba[i + 3];
                    rgba[i + 3] = rgba[i];
                    rgba[i + 2] = low;
                }
            }
            else if (!string.Equals(encoding, "rgba8", StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    "the baked page '" + file + "' declares the unknown encoding '" + encoding + "'; " +
                    "skipping it and rasterizing on demand.");
                return false;
            }

            // linear: true, matching NowFontBaker.TrySealPage. An sRGB page would be read through the transfer
            // function and every glyph would come out at the wrong weight.
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, false);

            built = new NowFont.BakedPage
            {
                texture = texture,
                atlasSize = page.GetProperty("atlasSize").GetInt32(),
                pixelRange = page.GetProperty("pixelRange").GetInt32(),
                distanceRange = page.GetProperty("distanceRange").GetInt32(),
                size = page.GetProperty("size").GetInt32(),
                packedSdf16 = page.GetProperty("packedSdf16").GetBoolean(),
                metrics = ReadMetrics(page.GetProperty("metrics")),
                glyphs = ReadGlyphs(page.GetProperty("glyphs"))
            };

            return true;
        }

        private static NowFontAtlasInfo.Metrics ReadMetrics(JsonElement element)
        {
            return new NowFontAtlasInfo.Metrics
            {
                emSize = ReadFloat(element, "emSize"),
                lineHeight = ReadFloat(element, "lineHeight"),
                ascender = ReadFloat(element, "ascender"),
                descender = ReadFloat(element, "descender"),
                underlineY = ReadFloat(element, "underlineY"),
                underlineThickness = ReadFloat(element, "underlineThickness")
            };
        }

        /// <summary>
        /// Glyph records as exported: <c>atlasBounds</c> in PIXELS. NowFont.BuildGlyphCache divides by the atlas size
        /// when it builds its lookup, so normalizing here would divide twice and every glyph would sample a sliver of
        /// the atlas corner.
        /// </summary>
        private static NowFontAtlasInfo.Glyph[] ReadGlyphs(JsonElement element)
        {
            NowFontAtlasInfo.Glyph[] glyphs = new NowFontAtlasInfo.Glyph[element.GetArrayLength()];
            int index = 0;

            foreach (JsonElement glyph in element.EnumerateArray())
            {
                glyphs[index++] = new NowFontAtlasInfo.Glyph
                {
                    unicode = glyph.GetProperty("unicode").GetInt32(),
                    advance = ReadFloat(glyph, "advance"),
                    planeBounds = ReadBounds(glyph.GetProperty("planeBounds")),
                    atlasBounds = ReadBounds(glyph.GetProperty("atlasBounds"))
                };
            }

            return glyphs;
        }

        private static NowFontAtlasInfo.Bounds ReadBounds(JsonElement element)
        {
            return new NowFontAtlasInfo.Bounds
            {
                left = ReadFloat(element, "left"),
                bottom = ReadFloat(element, "bottom"),
                right = ReadFloat(element, "right"),
                top = ReadFloat(element, "top")
            };
        }

        private static NowFontAsset[] RequireNoFallbacks(JsonElement element, string fixtureName)
        {
            if (element.GetProperty("fallbacks").GetArrayLength() != 0)
            {
                throw new NotSupportedException(
                    "Font fixture '" + fixtureName + "' names fallback assets, which the fixtures do not carry. " +
                    "See NowStandaloneAssetExport (\"omittedFallbacks\").");
            }

            return Array.Empty<NowFontAsset>();
        }

        private static void SetFallbacks(NowFontAsset asset, NowFontAsset[] fallbacks)
        {
            k_FallbacksField.SetValue(asset, fallbacks);
        }

        // -----------------------------------------------------------------------------------------------------------
        // Reflection into the shim and the core
        // -----------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Mints a shim <see cref="Shader"/>. THE SHIM GAP: the constructor is internal and NowUI.Web is not in
        /// NowUI.Engine's friend list, so this is reflection where the test provider writes
        /// <c>new Shader(name, info)</c>. The <c>DynamicDependency</c> keeps the trimmer from removing a constructor
        /// that nothing calls statically - without it a trimmed publish fails here at start-up, not at build time.
        /// </summary>
        [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicConstructors, typeof(Shader))]
        private static Shader NewShader(string name, NowShaderInfo info)
        {
            return (Shader)k_ShaderCtor.Invoke(new object[] { name, info });
        }

        private static ConstructorInfo ShaderConstructor()
        {
            ConstructorInfo ctor = typeof(Shader).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), typeof(NowShaderInfo) },
                null);

            if (ctor == null)
            {
                throw new MissingMethodException(
                    "UnityEngine.Shader has no (string, NowShaderInfo) constructor. A browser host cannot mint a " +
                    "Shader without it; the supported fix is <InternalsVisibleTo Include=\"NowUI.Web\" /> in " +
                    "NowUI.Engine.csproj, which removes the need for this reflection entirely.");
            }

            return ctor;
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(NowFontFamily))]
        private static FieldInfo FamilyField(string name)
        {
            return RequireField(typeof(NowFontFamily), name);
        }

        [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicFields, typeof(NowFontAsset))]
        private static FieldInfo FallbacksField()
        {
            return RequireField(typeof(NowFontAsset), "_fallbacks");
        }

        private static FieldInfo RequireField(
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicFields)] Type type,
            string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    type.Name + " has no field '" + name + "'. The standalone providers fill the font slots by " +
                    "reflection (design H.7); renaming one breaks this and NowFontResolutionTests alike.");
            }

            return field;
        }

        // -----------------------------------------------------------------------------------------------------------
        // Bytes and JSON
        // -----------------------------------------------------------------------------------------------------------

        private byte[] ReadBytes(string path)
        {
            byte[] bytes;
            if (!m_Files.TryGetValue(path, out bytes))
            {
                throw new InvalidOperationException(
                    "The fixture '" + path + "' was never fetched. WebResourceProvider.CreateAsync fetches the two " +
                    "table files, the family manifest, the faces it names and the TTFs those name; nothing else is " +
                    "available synchronously.");
            }

            return bytes;
        }

        private JsonDocument ReadJson(string path)
        {
            return Parse(ReadBytes(path));
        }

        private static JsonDocument Parse(byte[] bytes)
        {
            return JsonDocument.Parse(bytes);
        }

        private static float ReadFloat(JsonElement element, string name)
        {
            return element.GetProperty(name).GetSingle();
        }

        private static Vector4 ReadVector4(JsonElement element, string name)
        {
            JsonElement array = element.GetProperty(name);

            return new Vector4(
                array[0].GetSingle(),
                array[1].GetSingle(),
                array[2].GetSingle(),
                array[3].GetSingle());
        }

        private static string[] ReadStringArray(JsonElement element, string name)
        {
            JsonElement array = element.GetProperty(name);
            int count = array.GetArrayLength();

            if (count == 0)
                return Array.Empty<string>();

            string[] values = new string[count];
            for (int i = 0; i < count; i++)
                values[i] = array[i].GetString();

            return values;
        }

        /// <summary>Invariant formatting for the start-up diagnostics; a browser locale must not reshape a number.</summary>
        internal static string Format(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
