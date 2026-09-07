// The resource provider the test host installs.
//
// What it must do, and why (test plan R1/R2): NowUI loads a handful of resources through Resources.Load and
// Shader.Find, and Now.LoadRequiredResource logs a Debug.LogError when one comes back null. Under the Unity-parity log
// policy that error FAILS the first test that touches the resource, so a provider that serves nothing would not fail
// quietly - it would fail loudly and for the wrong reason.
//
// Everything it serves comes from Standalone/Tests/Fixtures, exported from the Unity project by
// Assets/NowUI/Editor/NowStandaloneAssetExport.cs (work unit U24):
//
//   Fixtures/shaders.json          every NowUI shader program: pass count, declared properties with their defaults,
//                                  keywords. This is what makes Material.HasProperty answer for a property nobody has
//                                  assigned - the half of it Unity answers from the compiled uniform list, which
//                                  NowFont, Now and NowSdf all gate real work on.
//   Fixtures/materials.json        the material templates under a Resources folder, with the shader each uses and the
//                                  float/vector/colour/keyword values the .mat actually stores.
//   Fixtures/NowUI/*.font.json     the four NotoSans faces, plus the family that binds them.
//   Fixtures/NowUI/*.ttf           the source bytes those faces bake from.
//
// THE FONTS ARE DYNAMIC, and that decides the shape of the code below. Design H.7 and test plan section 3.3 expected
// each face to carry a PREBAKED atlas (atlas/metrics/glyphs verbatim) with the TTF omitted as dead weight. No NowUI
// font asset in this project is built that way: every one carries an empty atlasInfo and its source TTF in _fontBytes,
// and NowFontCompiler bakes glyphs on demand. So a face here is built exactly the way the runtime builds one from a
// .ttf.asset - NowFontCompiler.TryCompile over the exported bytes - and the metrics the tests measure come out of the
// same baker Unity runs, from the same input. A fixture that ever does carry a prebaked atlas is refused loudly
// (BuildFace) rather than silently mis-built.
//
// Two contracts this provider must not break:
//   * The SAME instance is returned for the same path on every call. Core code caches materials and fonts by
//     reference, and NowTextWrapTests compares the loaded font family by reference (Assert.AreSame semantics).
//   * Materials are NOT cloned per draw. NowTextStylingTests.GradientTextDrawsRemainTextAndBatchTogether expects two
//     gradient text draws to land in ONE batch, and batch keys compare materials by instance.
//
// New file of ours; nothing under Assets/NowUITests is touched.
// Design: Docs/Standalone/StandaloneCoreDesign.md section 7.2 ("Support/NowStandaloneTestResources.cs").
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using NowUI;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Standalone.Tests
{
    /// <summary>Serves NowUI's built-in resources to the engine-free test run, from the exported fixtures.</summary>
    public sealed class NowStandaloneTestResources : INowResourceProvider
    {
        /// <summary>The resource path <c>Now.defaultFont</c> loads. Public so a self-test can name it once.</summary>
        public const string fontFamilyPath = "NowUI/NotoSans";

        /// <summary>
        /// The material templates the core reaches through <c>Resources.Load</c> outside the UGUI paths, which are
        /// compiled out of the standalone build. Every one of these must resolve or a draw that needs it logs an error
        /// and fails its test; the constructor checks the list rather than letting that surface later, somewhere else.
        /// </summary>
        private static readonly string[] k_RequiredMaterialPaths =
        {
            "NowUI/UIMaterial",         // Now._defaultMaterial
            "NowUI/TxtMaterial",        // text, and every dynamic font page
            "NowUI/TxtMaterialRGBA",    // colour (bitmap) faces
            "NowUI/GradientMaterial",   // NowGradient
            "NowUI/GlassMaterial",      // NowGlass
            "NowUI/GlassBlurMaterial",  // NowGlass blur pass
            "NowUI/RippleMaterial",     // NowRipple
            "NowUI/BezierMaterial",     // NowLine, node-graph links
            "NowUI/SdfMaterial",        // NowSdf
        };

        /// <summary>
        /// Shader names the core and the extensions reach through <c>Shader.Find</c>. Checked for the same reason as
        /// the materials above: a null here is a silently wrong branch, not an exception.
        /// </summary>
        private static readonly string[] k_RequiredShaderNames =
        {
            "NowUI/UI Bezier",              // NowLine
            "NowUI/UI Ripple",              // NowRipple
            "NowUI/Color Picker",           // NowValueControls
            "NowUI/SDF Scene",              // NowSdf
            "Hidden/NowUI/SDF Image Field", // NowSdfImageField
        };

        // NowFontFamily has no setters (design H.7): the slots are [SerializeField] privates that Unity fills in from
        // the asset. Reflection is how the standalone provider fills them, which is what NowFontResolutionTests
        // already does to build its own families - so this uses no access the oracle does not use itself.
        private static readonly FieldInfo k_RegularField = FamilyField("_regular");
        private static readonly FieldInfo k_BoldField = FamilyField("_bold");
        private static readonly FieldInfo k_ItalicField = FamilyField("_italic");
        private static readonly FieldInfo k_BoldItalicField = FamilyField("_boldItalic");
        private static readonly FieldInfo k_FallbacksField = FallbacksField();

        private readonly string m_FixtureRoot;

        private readonly Dictionary<string, UnityEngine.Object> m_Objects =
            new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);

        private readonly Dictionary<string, Shader> m_Shaders =
            new Dictionary<string, Shader>(StringComparer.Ordinal);

        /// <summary>Serves the fixtures next to the test assembly.</summary>
        public NowStandaloneTestResources()
            : this(ResolveFixtureRoot())
        {
        }

        /// <summary>Serves the fixtures under <paramref name="fixtureRoot"/>.</summary>
        public NowStandaloneTestResources(string fixtureRoot)
        {
            if (string.IsNullOrEmpty(fixtureRoot))
                throw new ArgumentException("A fixture root is required.", nameof(fixtureRoot));

            m_FixtureRoot = fixtureRoot;

            LoadShaders();
            LoadMaterials();
            RequireResources();
        }

        /// <summary>The directory the fixtures were read from. For diagnostics and for a self-test.</summary>
        public string fixtureRoot
        {
            get { return m_FixtureRoot; }
        }

        /// <summary>The material template paths a test can expect to resolve, in fixture order.</summary>
        public static IReadOnlyList<string> requiredMaterialPaths
        {
            get { return k_RequiredMaterialPaths; }
        }

        /// <summary>The shader names a test can expect <see cref="FindShader"/> to answer, in fixture order.</summary>
        public static IReadOnlyList<string> requiredShaderNames
        {
            get { return k_RequiredShaderNames; }
        }

        /// <inheritdoc />
        public UnityEngine.Object Load(string path, Type type)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            UnityEngine.Object result;
            bool known = m_Objects.TryGetValue(path, out result);

            // `result == null` is UnityEngine.Object's fake-null: it is also true for an instance a test destroyed.
            // Unity would reload such an asset from disk on the next Resources.Load, and the font family is the one
            // asset here a test can plausibly reach (Now.defaultFont is a global), so it is rebuilt on demand. The
            // materials are not: nothing in the subset destroys a template, and returning the destroyed instance makes
            // that (impossible) case fail visibly instead of being papered over.
            if (!known || result == null)
            {
                if (!string.Equals(path, fontFamilyPath, StringComparison.Ordinal))
                    return known ? result : null;

                result = BuildFontFamily();
                m_Objects[path] = result;
            }

            // Unity's Resources.Load<T> returns null when the asset is not of the requested type; matching that keeps
            // a mis-typed load looking the same in both runs.
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

        /// <summary>Registers an object under a resource path, replacing anything already there.</summary>
        public void Register(string path, UnityEngine.Object asset)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A resource path is required.", nameof(path));

            m_Objects[path] = asset;
        }

        /// <summary>Looks up a template material by resource path; null when the fixtures do not carry one.</summary>
        public Material GetMaterial(string path)
        {
            return Load(path, typeof(Material)) as Material;
        }

        // -------------------------------------------------------------------------------------------------------
        // Fixture location
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// <c>Fixtures/</c> beside the test assembly (the csproj copies the tree there), falling back to the one in
        /// the source tree so a run from an unusual working directory still finds it. Throws rather than degrading:
        /// a provider with no fixtures serves nulls, and a null here is a wrong answer everywhere else.
        /// </summary>
        private static string ResolveFixtureRoot()
        {
            string beside = Path.Combine(AppContext.BaseDirectory, "Fixtures");
            if (Directory.Exists(beside))
                return beside;

            for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Standalone", "Tests", "Fixtures");
                if (Directory.Exists(candidate))
                    return candidate;
            }

            throw new DirectoryNotFoundException(
                "The standalone test fixtures are missing. Expected them at '" + beside +
                "'. They are produced by Assets/NowUI/Editor/NowStandaloneAssetExport.cs; see " +
                "Docs/Standalone/StandaloneCoreDesign.md section 7.2.");
        }

        private string FixturePath(string part)
        {
            return Path.Combine(m_FixtureRoot, part);
        }

        private string FixturePath(string folder, string part)
        {
            return Path.Combine(m_FixtureRoot, folder, part);
        }

        private static JsonDocument ReadJson(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Missing standalone test fixture '" + path + "'. Re-run NowStandaloneAssetExport.Export.", path);
            }

            return JsonDocument.Parse(File.ReadAllBytes(path));
        }

        // -------------------------------------------------------------------------------------------------------
        // Shaders and materials
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds one shim <see cref="Shader"/> per exported program, carrying its declared uniforms and their
        /// defaults. The declarations are the point: <c>Material.HasProperty</c> answers from them for a property
        /// nobody has assigned, which is what Unity does and what NowUI gates optional features on.
        /// </summary>
        private void LoadShaders()
        {
            using (JsonDocument document = ReadJson(FixturePath("shaders.json")))
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
                                // An unknown property type would silently drop a declaration, and a dropped
                                // declaration is a HasProperty that answers false where Unity answers true.
                                throw new NotSupportedException(
                                    "Shader '" + name + "' declares property '" + propertyName +
                                    "' of unhandled type '" + propertyType + "'.");
                        }
                    }

                    info.keywords = ReadStringArray(entry, "keywords");
                    m_Shaders[name] = new Shader(name, info);
                }
            }
        }

        /// <summary>
        /// Builds one shim <see cref="Material"/> per exported template. <c>new Material(shader)</c> seeds the
        /// shader's declared defaults, exactly as Unity does, and the values the .mat stores are written over them.
        /// </summary>
        private void LoadMaterials()
        {
            using (JsonDocument document = ReadJson(FixturePath("materials.json")))
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

                    // Texture slots are deliberately left alone: every sampler in every exported template is
                    // unassigned, and an unassigned sampler reads as null here exactly as it does in Unity. A
                    // template that ever ships with a texture bound would need that texture exported too, so it is
                    // refused rather than quietly dropped.
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

        /// <summary>Fails construction when a resource the core requires is not in the fixtures.</summary>
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

        // -------------------------------------------------------------------------------------------------------
        // The font family
        // -------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the NotoSans family: four faces from their exported TTFs, bound into a
        /// <see cref="NowFontFamily"/>. Built on first use rather than in the constructor, so it is built after
        /// NowRuntime.Initialize has installed the host and the backend, and so a run that never touches text never
        /// reads 2.5 MB of font.
        /// </summary>
        private NowFontFamily BuildFontFamily()
        {
            using (JsonDocument document = ReadJson(FixturePath("NowUI", "NotoSans.family.json")))
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

                // The exporter omits the CJK/Arabic/emoji/icon fallback families (they are 5-10 MB each and no gate
                // test misses a glyph, so traversal never reaches them). An empty array, not null, so the family
                // reports the same shape a Unity asset with no fallbacks configured would.
                SetFallbacks(family, RequireNoFallbacks(root, "NotoSans.family.json"));

                return family;
            }
        }

        /// <summary>Builds one face from the file named in the family's <c>faces</c> object.</summary>
        private NowFont BuildFace(JsonElement faces, string slot)
        {
            JsonElement fileNameElement;
            if (!faces.TryGetProperty(slot, out fileNameElement) || fileNameElement.ValueKind == JsonValueKind.Null)
                return null;

            string fileName = fileNameElement.GetString();

            using (JsonDocument document = ReadJson(FixturePath("NowUI", fileName)))
            {
                JsonElement face = document.RootElement;

                // Refused, not guessed. Design H.7 and test plan 3.3 describe a prebaked face (atlas pixels plus a
                // glyph table); no asset in this project is one, so there is no exercised code here to build one
                // with. A fixture that ever carries one changes what the tests measure, and that has to be a
                // deliberate change to this method rather than a silent half-build.
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

                byte[] fontBytes = File.ReadAllBytes(FixturePath("NowUI", bytesElement.GetString()));
                int expectedCount = face.GetProperty("fontByteCount").GetInt32();

                if (fontBytes.Length != expectedCount)
                {
                    throw new InvalidOperationException(
                        "Font fixture '" + fileName + "' declares " + expectedCount + " source bytes but the file " +
                        "holds " + fontBytes.Length + ". The fixtures are inconsistent; re-export.");
                }

                // The same call the runtime makes for a .ttf.asset (NowFontCompiler.cs:731), with the same argument
                // shape. The material template is deliberately left null: on a Unity asset _dynamicMaterialTemplate
                // is a non-serialized field, so it is null there too, and the font loads "NowUI/TxtMaterial" through
                // this very provider when it bakes its first page (NowFont.cs:3618). Passing one here would take a
                // branch the editor run does not take.
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
                // was actually authored with, which is what the editor run used.
                font.dynamicPageSize = face.GetProperty("dynamicPageSize").GetInt32();
                font.dynamicMaxAtlasSize = face.GetProperty("dynamicMaxAtlasSize").GetInt32();
                font.dynamicMaxAtlasBytes = face.GetProperty("dynamicMaxAtlasBytes").GetInt32();

                SetFallbacks(font, RequireNoFallbacks(face, fileName));

                return font;
            }
        }

        /// <summary>
        /// Reads a fixture's <c>fallbacks</c> list, which the exporter writes empty. A populated one would name
        /// families whose TTFs are not in the fixtures, so it is refused rather than half-resolved.
        /// </summary>
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

        private static FieldInfo FamilyField(string name)
        {
            return RequireField(typeof(NowFontFamily), name);
        }

        private static FieldInfo FallbacksField()
        {
            return RequireField(typeof(NowFontAsset), "_fallbacks");
        }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);

            if (field == null)
            {
                throw new MissingFieldException(
                    type.Name + " has no field '" + name + "'. The standalone provider fills the font slots by " +
                    "reflection (design H.7); renaming one breaks this and NowFontResolutionTests alike.");
            }

            return field;
        }

        // -------------------------------------------------------------------------------------------------------
        // JSON helpers
        // -------------------------------------------------------------------------------------------------------

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
    }
}
