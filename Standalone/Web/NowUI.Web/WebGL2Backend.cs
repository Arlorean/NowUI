// The WebGL2 render backend for the browser host: NowUI.Engine's INowRenderBackend implemented over a real
// WebGL2 context. C# holds every decision; wwwroot/nowui-gl.js holds nothing but GL calls.
//
// Scope is Milestone 2 slice 1 (Docs/Standalone/M2-ShaderPort.md): the README quick-start scene — a rounded
// translucent panel plus the text "Score: 1200" — through two shader programs, `NowUI/UI Rectangle` and
// `NowUI/Text Renderer`. Gradient and ripple were added after it.
//
// Every method of INowRenderBackend is now implemented, including render targets, blits, procedural draws and
// texture copies. What remains unported is SHADER PROGRAMS, not machinery: a draw, blit or procedural pass
// through a program that is not in `IsPortedShader` throws with the program's name, because a silent no-op in a
// render backend is indistinguishable from a bug in the UI code above it. The one structural limit left is the
// uniform bridge: `UniformSlots` is a fixed slot map covering exactly what the ported NowUI programs declare, so
// porting a program with uniforms of its own means adding slots there and in nowui-gl.js's `U`.
//
// Boundary discipline: NowUI hands whole vertex and index buffers over, so this file packs a whole mesh into one
// contiguous payload and crosses into JavaScript once for it, and once more for the draw. Steady state for the
// quick-start frame is six crossings: BeginFrame, two mesh uploads, two draws, EndFrame.
using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using NowUI;
using NowUI.Engine;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Web
{
    /// <summary>
    /// An <see cref="INowRenderBackend"/> that draws through WebGL2 from inside a browser.
    /// </summary>
    /// <remarks>
    /// <para>Construct with <see cref="CreateAsync"/>: the JavaScript module has to be imported before any
    /// <c>[JSImport]</c> on it can be called, and that import is asynchronous.</para>
    /// <para>The eight invariants <see cref="INowRenderBackend"/> documents are relied on rather than
    /// re-checked, with one exception: this backend keeps no cached uniform state keyed on
    /// <c>Material.version</c>, because that field is <c>internal</c> to <c>NowUI.Engine</c> and this assembly
    /// is not a friend. See the class remarks on <see cref="EnsureMeshUploaded"/> for what that costs.</para>
    /// </remarks>
    public sealed partial class WebGL2Backend : INowRenderBackend
    {
        // ------------------------------------------------------------------------------------- shader programs

        const string RectangleShader = "NowUI/UI Rectangle";
        const string TextShader = "NowUI/Text Renderer";
        const string GradientShader = "NowUI/UI Gradient";
        const string RippleShader = "NowUI/UI Ripple";
        const string GlassShader = "NowUI/UI Glass";
        const string ColorPickerShader = "NowUI/Color Picker";
        const string BezierShader = "NowUI/UI Bezier";
        const string GlassBlurShader = "Hidden/NowUI/GlassBlur";
        const string SdfShader = "NowUI/SDF Scene";
        const string SdfImageFieldShader = "Hidden/NowUI/SDF Image Field";

        /// <summary>True for a shader name this backend has a compiled GLSL program for.</summary>
        /// <remarks>
        /// <para>This is the whole of the porting frontier. Everything absent from it — every UGUI variant,
        /// which the standalone build never uses — makes DrawMesh throw with the name in the message, because a
        /// render backend that silently skips a draw looks exactly like a bug in the UI code above it.</para>
        /// <para><c>Hidden/NowUI/GlassBlur</c> is here for <see cref="Blit"/>, not for DrawMesh: it is never a
        /// material on a mesh. Only its PASS 0 is ported — nowui-gl.js declares a one-entry <c>passes</c>
        /// array, so asking for pass 1, 2 or 3 fails there with the pass count in the message. Pass 1 is the
        /// texture-array blur, expressible on WebGL2 but unreachable (XR eye targets only); passes 2 and 3 read
        /// <c>Texture2DMS</c>, which GLSL ES 3.00 has no type for at all.</para>
        /// <para><c>Hidden/NowUI/SDF Image Field</c> is the other blit-only program, and unlike GlassBlur ALL
        /// FIVE of its passes are ported — Seed, Flood, Resolve, Stamp, Dilate, in the SubShader's order,
        /// because <c>NowSdfImageFields</c> addresses them by literal index. It is the one program in the port
        /// whose correctness depends on a float or half-float colour attachment; see the block above
        /// <c>GLSL_SDF_IMAGE_COMMON</c> in nowui-gl.js for what is probed and what happens when the extension
        /// is missing.</para>
        /// </remarks>
        /// <summary>One-shot latch for the <c>_NowGlassUseBackdrop</c> report; see the glass branch of the
        /// uniform bridge.</summary>
        private bool m_ReportedGlassBackdropFlag;

        /// <summary>
        /// Frames still to be traced, call by call, into the console. Set from the host with <c>?trace=N</c>.
        /// </summary>
        /// <remarks>
        /// Added for one specific failure that produces no exception: the render-to-texture area draws an empty
        /// canvas with a clean console, which is what a render target left bound looks like from outside. A trace
        /// of BeginFrame / SetRenderTarget / SetViewport / DrawMesh / Blit / EndFrame in order is the only way to
        /// see whether the target came back to the back buffer before the frame ended. Off unless asked for; the
        /// cost when off is one integer compare per call.
        /// </remarks>
        internal static int traceFrames;

        private const string NewLine = "\n";

        private static readonly System.Text.StringBuilder s_Trace = new System.Text.StringBuilder(4096);

        private static void Trace(string line)
        {
            if (traceFrames <= 0)
                return;

            s_Trace.Append(line).Append(NewLine);
        }

        /// <summary>
        /// The ported set, as prose, for a refusal message.
        /// </summary>
        /// <remarks>
        /// Built from the same constants <see cref="IsPortedShader"/> tests rather than typed out again. The
        /// hand-written version drifted: it still named four programs after four more had been ported, so the
        /// exception told a reader that Glass, Glass Blur, Color Picker and Bezier were unavailable when they were
        /// not. An error message that lists a capability set has to be generated from that set.
        /// </remarks>
        const string PortedShaderList =
            "'" + RectangleShader + "', '" + TextShader + "', '" + GradientShader + "', '" + RippleShader +
            "', '" + GlassShader + "', '" + GlassBlurShader + "', '" + ColorPickerShader + "', '" +
            BezierShader + "', '" + SdfShader + "' and '" + SdfImageFieldShader + "'";

        static bool IsPortedShader(string name)
        {
            return name == RectangleShader
                || name == TextShader
                || name == GradientShader
                || name == RippleShader
                || name == GlassShader
                || name == ColorPickerShader
                || name == BezierShader
                || name == GlassBlurShader
                || name == SdfShader
                || name == SdfImageFieldShader;
        }

        // ------------------------------------------------------------------------------------- uniform block
        //
        // One flat float block per draw, whose slot map is duplicated as `U` in nowui-gl.js. A fixed layout beats
        // a generic name-to-location walk here: it is one boundary crossing, one allocation-free scratch array,
        // and a mismatch is a compile-time-visible constant rather than a silent missing uniform.

        internal static class UniformSlots
        {
            public const int Mvp = 0;                     // mat4, column major
            public const int MainTexST = 16;              // vec4
            public const int PremultipliedTexture = 20;   // float
            public const int TextSdfEncoding = 21;        // float
            public const int MaskCount = 22;              // float
            public const int TextureMaskCount = 23;       // float
            public const int MaskRects = 24;              // vec4[8]
            public const int MaskData = 56;               // vec4[8]
            public const int MaskParams = 88;             // vec4[8]
            public const int MaskTransforms = 120;        // vec4[8]
            public const int TextureMaskRects = 152;      // vec4[2]
            public const int TextureMaskParams = 160;     // vec4[2]
            public const int TextureMaskTransforms = 168; // vec4[2]
            public const int GradientRampTexelSize = 176; // vec4, NowUI/UI Gradient only

            // Appended by the core-shader port. Nothing below 180 moved, so a drift between this table and
            // nowui-gl.js's `U` surfaces as that file's block-length check rather than as a wrong uniform.
            public const int ColorPickerMode = 180;         // float, NowUI/Color Picker only
            public const int GlassUseBackdrop = 181;        // float, NowUI/UI Glass
            public const int GlassUseStereoBackdrop = 182;  // float, NowUI/UI Glass
            public const int GlassMaterialMode = 183;       // float, NowUI/UI Glass
            public const int BackdropUvTransform = 184;     // vec4,  NowUI/UI Glass
            public const int BlurTexelSize = 188;           // vec4,  Hidden/NowUI/GlassBlur
            public const int BlurSourceScaleOffset = 192;   // vec4,  Hidden/NowUI/GlassBlur
            public const int BlurDirection = 196;           // vec2,  Hidden/NowUI/GlassBlur

            // Appended by the SDF image-field port. Nothing below 198 moved.
            //
            // Every one of these is bridged rather than left at GL's zero, because not one of them has a usable
            // zero: a zero <c>_FieldTexels.xy</c> makes the shader's FieldTexelCenter return 0.5 for every
            // fragment (one texel of field, smeared over the target), a zero <c>_FieldParams.w</c> clamps the
            // alpha threshold to 0.0001 so every texel reads as inside, and a zero <c>_Step</c> makes every
            // jump-flood neighbour the texel itself so the flood never propagates. All three produce a
            // plausible image rather than a failure.
            public const int SourceUv = 198;                // vec4,  Hidden/NowUI/SDF Image Field
            public const int FieldParams = 202;             // vec4,  Hidden/NowUI/SDF Image Field
            public const int FieldTexels = 206;             // vec4,  Hidden/NowUI/SDF Image Field
            public const int StampRect = 210;               // vec4,  Hidden/NowUI/SDF Image Field
            public const int Step = 214;                    // float, Hidden/NowUI/SDF Image Field
            public const int Count = 215;
        }

        /// <summary>
        /// The second flat block, for <c>NowUI/SDF Scene</c> alone. Mirrored as <c>S</c> in nowui-gl.js.
        /// </summary>
        /// <remarks>
        /// <para><b>Why it is separate from <see cref="UniformSlots"/>.</b> The nine SDF arrays are 480 vec4.
        /// Appending them would make the shared per-draw block ~2180 floats, so every rectangle, glyph run and
        /// ripple in the app would marshal 8.7 KB across the wasm boundary to fill uniforms its program does not
        /// declare. This block is written and shipped only when the SDF program is the one drawing.</para>
        /// <para><b>The capacities are structural.</b> <c>NowSdf.MaxShapes</c> is 64 and <c>NowSdf.MaxLayers</c>
        /// is 16 (NowSdf.cs:2343-2344); the scratch arrays <c>NowSdfCache</c> uploads from are exactly that long
        /// (NowSdf.cs:3326-3334) and <c>SetVectorArray</c> ships all of every one of them whatever the scene
        /// filled. Sizing either from a count uniform is the mistake NowUIMask.cginc's [8]/[2] already
        /// established the rule against.</para>
        /// </remarks>
        internal static class SdfUniformSlots
        {
            public const int Data0 = 0;                  // vec4[64]
            public const int Data1 = 256;                // vec4[64]
            public const int Data2 = 512;                // vec4[64]
            public const int ShapeMeta = 768;            // vec4[64]
            public const int Colors = 1024;              // vec4[64]
            public const int Uvs = 1280;                 // vec4[64]
            public const int ImageUvs = 1536;            // vec4[64]
            public const int LayerData0 = 1792;          // vec4[16]
            public const int LayerData1 = 1856;          // vec4[16]
            public const int ImageAtlasSize = 1920;      // vec4
            public const int Outline = 1924;             // vec4
            public const int OutlineColor = 1928;        // vec4
            public const int Glow = 1932;                // vec4
            public const int GlowColor = 1936;           // vec4
            public const int Shadow = 1940;              // vec4
            public const int ShadowColor = 1944;         // vec4
            public const int InnerShadow = 1948;         // vec4
            public const int InnerShadowColor = 1952;    // vec4
            public const int Emboss = 1956;              // vec4
            public const int Contour = 1960;             // vec4
            public const int ContourColor = 1964;        // vec4
            public const int ContourMask = 1968;         // vec4
            public const int Warp = 1972;                // vec4
            public const int ShapeCount = 1976;          // float
            public const int LayerCount = 1977;          // float
            public const int Feather = 1978;             // float
            public const int TextEffectLimit = 1979;     // float
            public const int MaskOutput = 1980;          // float
            public const int CanvasLayout = 1981;        // float
            public const int Time = 1982;                // float
            public const int Count = 1983;
        }

        /// <summary>Shape-array capacity, fixed by <c>NowSdf.MaxShapes</c>. Never sized from a count uniform.</summary>
        const int SdfShapeCapacity = 64;

        /// <summary>Layer-array capacity, fixed by <c>NowSdf.MaxLayers</c>.</summary>
        const int SdfLayerCapacity = 16;

        /// <summary>
        /// The only SDF material ABI this port implements.
        /// </summary>
        /// <remarks>
        /// NowSdf.shader includes <c>NowSdfShaderV2.cginc</c> and nothing else, and
        /// <c>NowSdf.MaterialAbiVersion</c> is 2. But <c>NowSdf.MinimumMaterialAbiVersion</c> is 1, so
        /// <c>NowSdfBuilder.SetMaterial</c> ACCEPTS a project-authored template that declares
        /// <c>_NowSdfAbiVersion = 1</c> (NowSdf.cs:3506-3527) as long as the scene uses only legacy primitives.
        /// Such a template names a different shader with a V1 implementation behind it. Drawing it through this
        /// V2 program would reinterpret its uniform arrays under the wrong contract, so the ABI is checked
        /// rather than assumed — which is what "honour <c>_NowSdfAbiVersion</c>" has to mean on the port side,
        /// the shader itself never reading it.
        /// </remarks>
        const float SdfPortedAbiVersion = 2f;

        /// <summary>Analytic mask capacity, fixed by <c>NowUIMask.cginc</c>. Never sized from the count uniform.</summary>
        const int AnalyticMaskCapacity = 8;

        /// <summary>Texture mask capacity, fixed by <c>NowUIMask.cginc</c>.</summary>
        const int TextureMaskCapacity = 2;

        // ------------------------------------------------------------------------------------- property ids
        //
        // `Shader.IDToName` is internal, so the id -> name direction is unavailable. It is not needed: interning
        // the eighteen names these two shaders declare gives the name -> id direction, and PropertyToID is
        // idempotent against the same process-wide table NowUI's own static initialisers used.

        static readonly int IdMainTex = Shader.PropertyToID("_MainTex");
        static readonly int IdPremultipliedTexture = Shader.PropertyToID("_NowPremultipliedTexture");
        static readonly int IdTextSdfEncoding = Shader.PropertyToID("_NowUITextSdfEncoding");
        static readonly int IdGradientRampTexelSize = Shader.PropertyToID("_NowGradientRampTexelSize");

        // The 256x256 ramp atlas the TEXT shader's gradient branch samples. Unlike every other sampler in the
        // port it is never on a material and never in a property block: NowGradientRampCache publishes it with
        // Shader.SetGlobalTexture on the first ramp ALLOCATION (NowGradient.cs:580), so TextureId finds it only
        // on its third and last lookup, NowRuntime.globals. NowUI/UI Gradient reads the same atlas through its
        // own _MainTex; the two paths are independent and both have to work.
        static readonly int IdGradientRampTexture = Shader.PropertyToID("_NowGradientRampTexture");

        // The core-shader port's own. `_Mode` comes from the picker material's bag; the four `_NowBlur*` come
        // from NowRuntime.globals, because NowGlassRenderer writes them with SetGlobalTexture/SetGlobalVector
        // (NowGlassRenderer.cs:860-891) rather than onto the blur material. ResolveFloat/ResolveVector already
        // fall through to the globals, so these ids are the whole of the work.
        static readonly int IdColorPickerMode = Shader.PropertyToID("_Mode");
        static readonly int IdGlassUseBackdrop = Shader.PropertyToID("_NowGlassUseBackdrop");
        static readonly int IdGlassUseStereoBackdrop = Shader.PropertyToID("_NowGlassUseStereoBackdrop");
        static readonly int IdGlassMaterialMode = Shader.PropertyToID("_NowMaterialGlassMode");
        static readonly int IdBackdropUvTransform = Shader.PropertyToID("_NowBackdropUVTransform");
        static readonly int IdBlurTexelSize = Shader.PropertyToID("_NowBlurTexelSize");
        static readonly int IdBlurSourceScaleOffset = Shader.PropertyToID("_NowBlurSourceScaleOffset");
        static readonly int IdBlurDirection = Shader.PropertyToID("_NowBlurDirection");
        static readonly int IdMaskCount = Shader.PropertyToID("_NowUIMaskCount");
        static readonly int IdMaskRects = Shader.PropertyToID("_NowUIMaskRects");
        static readonly int IdMaskData = Shader.PropertyToID("_NowUIMaskData");
        static readonly int IdMaskParams = Shader.PropertyToID("_NowUIMaskParams");
        static readonly int IdMaskTransforms = Shader.PropertyToID("_NowUIMaskTransforms");
        static readonly int IdTextureMaskCount = Shader.PropertyToID("_NowUITextureMaskCount");
        static readonly int IdTextureMask0 = Shader.PropertyToID("_NowUITextureMask0");
        static readonly int IdTextureMask1 = Shader.PropertyToID("_NowUITextureMask1");
        static readonly int IdTextureMaskRects = Shader.PropertyToID("_NowUITextureMaskRects");
        static readonly int IdTextureMaskParams = Shader.PropertyToID("_NowUITextureMaskParams");
        static readonly int IdTextureMaskTransforms = Shader.PropertyToID("_NowUITextureMaskTransforms");

        // NowUI/SDF Scene. Every one of these is written by NowSdfCache.Upload (NowSdf.cs:4873-4905) onto the
        // per-cache material instance, so the material bag is the source for all of them and none arrives as a
        // shader global. `_SdfTextEffectLimit` and `_SdfImageAtlasSize` are NOT in NowSdf.shader's Properties
        // block — Upload sets them anyway, which the shim's bag allows and Unity's does too.
        static readonly int IdSdfAbiVersion = Shader.PropertyToID("_NowSdfAbiVersion");
        static readonly int IdSdfCanvasLayout = Shader.PropertyToID("_NowCanvasLayout");
        static readonly int IdSdfShapeCount = Shader.PropertyToID("_SdfShapeCount");
        static readonly int IdSdfLayerCount = Shader.PropertyToID("_SdfLayerCount");
        static readonly int IdSdfFeather = Shader.PropertyToID("_SdfFeather");
        static readonly int IdSdfTextEffectLimit = Shader.PropertyToID("_SdfTextEffectLimit");
        static readonly int IdSdfMaskOutput = Shader.PropertyToID("_SdfMaskOutput");
        static readonly int IdSdfImageField = Shader.PropertyToID("_SdfImageField");
        static readonly int IdSdfImageColor = Shader.PropertyToID("_SdfImageColor");
        static readonly int IdSdfImageAtlasSize = Shader.PropertyToID("_SdfImageAtlasSize");
        static readonly int IdSdfData0 = Shader.PropertyToID("_SdfData0");
        static readonly int IdSdfData1 = Shader.PropertyToID("_SdfData1");
        static readonly int IdSdfData2 = Shader.PropertyToID("_SdfData2");
        static readonly int IdSdfShapeMeta = Shader.PropertyToID("_SdfShapeMeta");
        static readonly int IdSdfColors = Shader.PropertyToID("_SdfColors");
        static readonly int IdSdfUvs = Shader.PropertyToID("_SdfUvs");
        static readonly int IdSdfImageUvs = Shader.PropertyToID("_SdfImageUvs");
        static readonly int IdSdfLayerData0 = Shader.PropertyToID("_SdfLayerData0");
        static readonly int IdSdfLayerData1 = Shader.PropertyToID("_SdfLayerData1");
        static readonly int IdSdfOutline = Shader.PropertyToID("_SdfOutline");
        static readonly int IdSdfOutlineColor = Shader.PropertyToID("_SdfOutlineColor");
        static readonly int IdSdfGlow = Shader.PropertyToID("_SdfGlow");
        static readonly int IdSdfGlowColor = Shader.PropertyToID("_SdfGlowColor");
        static readonly int IdSdfShadow = Shader.PropertyToID("_SdfShadow");
        static readonly int IdSdfShadowColor = Shader.PropertyToID("_SdfShadowColor");
        static readonly int IdSdfInnerShadow = Shader.PropertyToID("_SdfInnerShadow");
        static readonly int IdSdfInnerShadowColor = Shader.PropertyToID("_SdfInnerShadowColor");
        static readonly int IdSdfEmboss = Shader.PropertyToID("_SdfEmboss");
        static readonly int IdSdfContour = Shader.PropertyToID("_SdfContour");
        static readonly int IdSdfContourColor = Shader.PropertyToID("_SdfContourColor");
        static readonly int IdSdfContourMask = Shader.PropertyToID("_SdfContourMask");
        static readonly int IdSdfWarp = Shader.PropertyToID("_SdfWarp");

        // Hidden/NowUI/SDF Image Field. Written by NowSdfImageFields.Bake (NowSdfImageField.cs:432-443) and
        // .Stamp (:350-357) onto the one material that class owns; nothing arrives through a property block or
        // a global, but the shared resolution order is used anyway so there is one rule rather than two.
        static readonly int IdSourceTex = Shader.PropertyToID("_SourceTex");
        static readonly int IdSourceUv = Shader.PropertyToID("_SourceUv");
        static readonly int IdFieldParams = Shader.PropertyToID("_FieldParams");
        static readonly int IdFieldTexels = Shader.PropertyToID("_FieldTexels");
        static readonly int IdStampRect = Shader.PropertyToID("_StampRect");
        static readonly int IdStep = Shader.PropertyToID("_Step");

        // ------------------------------------------------------------------------------------- state

        NowRenderCaps m_Caps;
        bool m_Initialized;
        bool m_InFrame;

        Matrix4x4 m_View = Matrix4x4.identity;
        Matrix4x4 m_Projection = Matrix4x4.identity;
        bool m_ViewProjectionSet;

        readonly HashSet<string> m_ResolvedShaders = new HashSet<string>();
        readonly HashSet<string> m_WarnedOnce = new HashSet<string>();

        // Scratch, allocated once. The steady-state frame must not allocate (design §1.2), and the mesh read-back
        // getters below are List-shaped, so the lists are members rather than locals.
        readonly List<Vector3> m_Positions = new List<Vector3>();
        readonly List<Vector2> m_Uv0 = new List<Vector2>();
        readonly List<Vector4>[] m_UvN = new List<Vector4>[8];
        readonly List<int> m_Indices = new List<int>();
        readonly List<Vector4> m_VectorArray = new List<Vector4>();
        readonly float[] m_Uniforms = new float[UniformSlots.Count];

        // Allocated once and only ever written on an SDF draw. 1983 floats is 7.9 KB, which is why it is not
        // part of m_Uniforms; see SdfUniformSlots.
        readonly float[] m_SdfUniforms = new float[SdfUniformSlots.Count];
        readonly int[] m_TextureInfo = new int[11];
        readonly int[] m_SamplerInfo = new int[5];
        readonly int[] m_MeshHeader = new int[11];

        // [indexCount, _MainTex, _NowUITextureMask0, _NowUITextureMask1, pass, _SdfImageField, _SdfImageColor,
        //  _NowGradientRampTexture].
        // Slots 5 and 6 are 0 for every program but NowUI/SDF Scene and slot 7 for every program but
        // NowUI/Text Renderer; nowui-gl.js reads each only for the program that declares the sampler.
        readonly int[] m_DrawInfo = new int[8];
        readonly int[] m_TargetInfo = new int[13];
        readonly int[] m_BlitInfo = new int[9];

        /// <summary>Blits issued per pass of <c>Hidden/NowUI/SDF Image Field</c>, indexed by pass.</summary>
        /// <remarks>
        /// Static rather than per-instance because there is one backend per page and the reader
        /// (<c>?area=sdf-image</c>) has no handle on it. Indexed by the SubShader's pass order: 0 Seed,
        /// 1 Flood, 2 Resolve, 3 Stamp, 4 Dilate. A page that draws image nodes and reports all zeros here is
        /// reporting that the bake never ran, whatever its cells look like.
        /// </remarks>
        static readonly int[] s_SdfImageFieldPassBlits = new int[5];

        /// <summary>
        /// The per-pass blit tally, as "seed/flood/resolve/stamp/dilate".
        /// </summary>
        internal static string SdfImageFieldPassReport()
        {
            return s_SdfImageFieldPassBlits[0] + "/" + s_SdfImageFieldPassBlits[1] + "/" +
                   s_SdfImageFieldPassBlits[2] + "/" + s_SdfImageFieldPassBlits[3] + "/" +
                   s_SdfImageFieldPassBlits[4];
        }
        readonly int[] m_ProceduralInfo = new int[7];
        byte[] m_Payload = new byte[64 * 1024];

        /// <summary>Bytes each of the nine streams contributes per vertex, indexed by attribute location.</summary>
        static readonly int[] StreamElementSize = { 12, 8, 16, 16, 16, 16, 16, 16, 16 };

        WebGL2Backend()
        {
            for (int i = 0; i < m_UvN.Length; ++i)
                m_UvN[i] = new List<Vector4>();
        }

        /// <summary>
        /// Where <see cref="CreateAsync"/> looks for <c>nowui-gl.js</c> by default.
        /// </summary>
        /// <remarks>
        /// Measured, not assumed: <c>JSHost.ImportAsync</c> resolves a relative specifier against the runtime's own
        /// script, which is served from <c>_framework/</c> — so <c>"./nowui-gl.js"</c> asks for
        /// <c>_framework/nowui-gl.js</c> and 404s. One level up is the application base, which is where a static
        /// web asset under <c>wwwroot</c> actually lands. A host that puts the module elsewhere passes its own path.
        /// </remarks>
        public const string DefaultModulePath = "../nowui-gl.js";

        /// <summary>
        /// Imports the JavaScript module, creates the WebGL2 context on the canvas <paramref name="canvasSelector"/>
        /// matches, and reports what it can do.
        /// </summary>
        /// <remarks>
        /// Asynchronous because <c>JSHost.ImportAsync</c> is: a <c>[JSImport]</c> cannot be called before its
        /// module has loaded, and there is no synchronous way to load one.
        /// </remarks>
        public static async Task<WebGL2Backend> CreateAsync(string canvasSelector, string modulePath = DefaultModulePath)
        {
            if (string.IsNullOrEmpty(canvasSelector))
                throw new ArgumentException("A CSS selector for the canvas is required.", nameof(canvasSelector));

            var backend = new WebGL2Backend();
            await JSHost.ImportAsync(Interop.ModuleName, modulePath).ConfigureAwait(false);

            string report = Interop.Init(canvasSelector);
            backend.m_Caps = ParseCaps(report);
            backend.m_Initialized = true;
            return backend;
        }

        /// <summary>Splits the pipe-delimited capability line the JavaScript side returns.</summary>
        /// <remarks>
        /// Pipe-delimited rather than JSON so nothing here needs a serializer: the browser host publishes trimmed,
        /// and reflection-based JSON is exactly what trimming breaks.
        /// </remarks>
        static NowRenderCaps ParseCaps(string report)
        {
            string[] parts = (report ?? string.Empty).Split('|');

            int maxTextureSize = parts.Length > 0 && int.TryParse(parts[0], out int t) ? t : 2048;
            bool floatRenderable = parts.Length > 2 && parts[2] == "1";
            string renderer = parts.Length > 3 ? parts[3] : "WebGL2";
            bool halfRenderable = parts.Length > 4 ? parts[4] == "1" : floatRenderable;

            return new NowRenderCaps(
                maxTextureSize: maxTextureSize,
                // ONE, not GL_MAX_SAMPLES. WebGL2 can multisample a RENDERBUFFER but has no multisampled
                // TEXTURE, and every NowUI render target exists to be sampled afterwards, so a multisampled one
                // cannot be honoured. Reporting the driver's MAX_SAMPLES here would make
                // SystemInfo.GetRenderTextureSupportedMSAASampleCount promise antialiasing that
                // CreateRenderTexture then silently flattens. The back buffer is created with
                // `antialias: false` as well, so 1 is the whole truth for this backend.
                maxMsaaSamples: 1,
                // WebGL2 has no sampleable multisampled texture — only multisampled renderbuffers. Reporting
                // false is what keeps NowGlassRenderer off its resolve path rather than into a driver error.
                supportsMultisampledTextures: false,
                supportsTextureArrays: true,
                supportsInstancing: true,
                supportsR8: true,
                // Half/float colour is sampleable in core WebGL2 but only *renderable* with an extension, and
                // NowRenderCaps' flags gate render targets as well as textures. Half and full float are asked
                // separately because EXT_color_buffer_half_float exists without EXT_color_buffer_float — which
                // is exactly the case NowSdfImageField's RHalf-then-RFloat preference walks into.
                supportsRHalf: halfRenderable,
                supportsRFloat: floatRenderable,
                supportsRGHalf: halfRenderable,
                supportsRGFloat: floatRenderable,
                supportsARGBHalf: halfRenderable,
                supportsARGBFloat: floatRenderable,
                supportsARGB32: true,
                // Depth-only render targets (RenderTextureFormat.Depth / Shadowmap) are NOT allocatable by this
                // backend: CreateRenderTexture has no internal format for them. Nothing in NowUI asks for one —
                // the depth NowModelPreview wants is depthBufferBits on a colour target, which IS honoured — so
                // this reports the depth *attachment* support that is real rather than a format that is not.
                supportsDepth: true,
                renderTargetsAreBottomUp: true,
                // Gamma, matching the Unity project. Nothing in this backend converts, and nothing must.
                colorSpace: ColorSpace.Gamma,
                deviceName: renderer,
                deviceType: GraphicsDeviceType.OpenGLES3,
                graphicsMemorySizeMb: 0);
        }

        /// <inheritdoc />
        public NowRenderCaps caps
        {
            get
            {
                RequireInitialized();
                return m_Caps;
            }
        }

        // ------------------------------------------------------------------------------------- frame boundaries

        /// <inheritdoc />
        public void BeginFrame(int frameCount)
        {
            RequireInitialized();

            if (m_InFrame)
                throw new InvalidOperationException("WebGL2Backend.BeginFrame was called twice without an EndFrame.");

            m_InFrame = true;
            m_ViewProjectionSet = false;
            Trace("BeginFrame(" + frameCount + ")");
            Interop.BeginFrame(frameCount);
        }

        /// <inheritdoc />
        public void EndFrame()
        {
            RequireInitialized();

            if (!m_InFrame)
                throw new InvalidOperationException("WebGL2Backend.EndFrame was called without a BeginFrame.");

            m_InFrame = false;
            Trace("EndFrame()");
            Interop.EndFrame();

            if (traceFrames > 0)
            {
                traceFrames--;
                Console.WriteLine("[NowUI][trace]" + NewLine + s_Trace.ToString());
                s_Trace.Clear();
            }
        }

        // ------------------------------------------------------------------------------------- textures

        /// <inheritdoc />
        public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips)
        {
            RequireInitialized();

            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            // RGBA32 is what NowUI's font atlas, mask pages and ramps are built as. Anything else would need a
            // format table this slice has no way to test, so it says so rather than guessing an internal format.
            if (texture.format != TextureFormat.RGBA32)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.UploadTexture2D: texture '{texture.name}' is {texture.format}. Slice 1 uploads " +
                    "RGBA32 only; other texture formats belong to a later slice.");
            }

            int width = texture.width;
            int height = texture.height;

            if (pixels.Length < width * height * 4)
            {
                throw new ArgumentException(
                    $"WebGL2Backend.UploadTexture2D: texture '{texture.name}' is {width}x{height} RGBA32, which needs " +
                    $"{width * height * 4} bytes, but {pixels.Length} arrived.", nameof(pixels));
            }

            // The dirty rect is in bottom-up texel space, row 0 = bottom, and so is a GL texture. Nothing flips.
            m_TextureInfo[0] = width;
            m_TextureInfo[1] = height;
            m_TextureInfo[2] = dirtyRect.x;
            m_TextureInfo[3] = dirtyRect.y;
            m_TextureInfo[4] = dirtyRect.width;
            m_TextureInfo[5] = dirtyRect.height;
            m_TextureInfo[6] = (int)texture.filterMode;
            m_TextureInfo[7] = (int)texture.wrapModeU;
            m_TextureInfo[8] = (int)texture.wrapModeV;
            m_TextureInfo[9] = texture.mipmapCount;
            m_TextureInfo[10] = generateMips ? 1 : 0;

            // `Texture2D.isLinear` is true for every atlas NowUI bakes, and under Gamma it must change nothing:
            // Unity performs no sRGB sampling conversion in a Gamma project. When a linear slice arrives,
            // isLinear == false is the signal to reach for SRGB8_ALPHA8 — not before.
            Interop.UploadTexture(
                texture.GetInstanceID(),
                m_TextureInfo,
                // ReadOnlySpan is not a marshallable JSImport parameter; Span is. This re-wraps the same memory
                // without copying it, and the view is only alive for the duration of the call.
                MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference(pixels), pixels.Length));
        }

        /// <inheritdoc />
        public void UpdateSampler(Texture texture)
        {
            RequireInitialized();

            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            m_SamplerInfo[0] = (int)texture.filterMode;
            m_SamplerInfo[1] = (int)texture.wrapModeU;
            m_SamplerInfo[2] = (int)texture.wrapModeV;

            // SLOT 3 IS RESERVED AND DELIBERATELY ZERO. It used to carry texture.anisoLevel, which the page then
            // ignored - applySampler in nowui-gl.js has never read it and no EXT_texture_filter_anisotropic is
            // requested anywhere. Sending it implied a capability that did not exist.
            //
            // It is not wired up instead of being removed because anisotropic filtering cannot help this
            // renderer. Aniso corrects minification that differs between the two screen axes; NowUI draws
            // axis-aligned quads under an orthographic projection, and world-space graphics are excluded from
            // the browser build entirely, so the two derivatives are equal and the anisotropy ratio is 1 - the
            // case the extension is specified to do nothing in. Nothing in Assets/NowUI sets anisoLevel above
            // its default of 1 either, so the value being dropped was always the no-op one.
            //
            // The SLOT stays rather than the array shrinking, because index 4 is mipCount and a shipped
            // nowui-gl.js reads it there. Renumbering would break any page served from an older bundle. It is
            // assigned rather than left alone because this array is reused across calls, and a stale aniso value
            // from a previous texture would otherwise sit in it.
            m_SamplerInfo[3] = 0;

            m_SamplerInfo[4] = texture.mipmapCount;

            Interop.UpdateSampler(texture.GetInstanceID(), m_SamplerInfo);
        }

        /// <inheritdoc />
        public void ReleaseTexture(Texture texture)
        {
            if (!m_Initialized || ReferenceEquals(texture, null))
                return;

            Interop.ReleaseTexture(texture.GetInstanceID());
        }

        // ------------------------------------------------------------------------------------- meshes, materials

        /// <inheritdoc />
        public void ReleaseMesh(Mesh mesh)
        {
            if (!m_Initialized || ReferenceEquals(mesh, null))
                return;

            Interop.ReleaseMesh(mesh.GetInstanceID());
        }

        /// <inheritdoc />
        /// <remarks>
        /// Nothing is cached per material — every uniform is pushed on every draw — so there is nothing to drop.
        /// This is a genuine no-op rather than an unimplemented one; see the class remarks.
        /// </remarks>
        public void ReleaseMaterial(Material material)
        {
        }

        /// <inheritdoc />
        public bool ResolveShader(Shader shader)
        {
            RequireInitialized();

            if (ReferenceEquals(shader, null))
                return false;

            string name = shader.name;

            if (m_ResolvedShaders.Contains(name))
                return true;

            // Compiling and linking here rather than lazily at the first draw is deliberate: a GLSL error should
            // surface while the host is still loading, not in the middle of a frame.
            if (Interop.ResolveShader(name) == 0)
                return false;

            m_ResolvedShaders.Add(name);
            return true;
        }

        // ------------------------------------------------------------------------------------- state

        /// <inheritdoc />
        /// <remarks>
        /// <para>The framebuffer origin does not need compensating for. GL's origin is bottom-left and NowUI's UI
        /// space is top-left, but the cancellation that makes the back buffer come out the right way up is a
        /// property of the projection, not of the back buffer: <c>Matrix4x4.Ortho(0, W, -H, 0, -1, 100)</c> puts
        /// UI top at <c>clip.y = +1</c>, GL puts <c>clip.y = +1</c> at the highest row of the viewport, and NowUI
        /// samples every texture bottom-up — so a render target's highest row is both "UI top" and "t = 1", which
        /// is what a NowUI shader reads it back as. The derivation is in <c>Docs/Standalone/M2-ShaderPort.md</c>
        /// §8.1–§8.4; the conclusion is that nothing in this backend flips anything, and a render-to-texture
        /// result that comes out inverted means a different projection or a partial viewport, not a missing
        /// flip.</para>
        /// </remarks>
        public void SetRenderTarget(in NowRenderTarget target)
        {
            RequireInitialized();

            // Reference identity, never ==: the fake-null operator reports true for a *destroyed* render texture,
            // and a destroyed target is a bug to surface rather than the back buffer.
            if (target.isBackBuffer)
            {
                Trace("SetRenderTarget(backbuffer)");
                Interop.SetRenderTarget(BackBufferTarget, 0, 0);
                return;
            }

            if (target.face != CubemapFace.Unknown)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.SetRenderTarget: cubemap face {target.face}. Only 2D targets are allocatable " +
                    "by this backend, so a cube face cannot be reached.");
            }

            // AllDepthSlices (-1) on a 2D target means "the only slice"; anything above 0 is an array target,
            // which CreateRenderTexture refuses, so the bind would be binding something that does not exist.
            int slice = target.depthSlice == NowRenderTarget.AllDepthSlices ? 0 : target.depthSlice;

            if (slice != 0)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.SetRenderTarget: depth slice {slice}. Array render targets are not ported; " +
                    "the only NowUI code that asks for one is NowGlassRenderer's single-pass-instanced stereo " +
                    "path, which no browser host reaches.");
            }

            Trace("SetRenderTarget(rt " + target.texture.GetInstanceID() + " " +
                  target.texture.width + "x" + target.texture.height + " mip " + target.mipLevel + ")");
            Interop.SetRenderTarget(target.texture.GetInstanceID(), target.mipLevel, slice);
        }

        /// <summary>The target id that means the host's default framebuffer, mirrored as <c>id === 0</c> in the module.</summary>
        /// <remarks>Safe for the same reason <see cref="NoTexture"/> is: every real instance id is negative.</remarks>
        const int BackBufferTarget = 0;

        /// <inheritdoc />
        public void SetViewport(in Rect pixelRect)
        {
            RequireInitialized();

            Trace("SetViewport(" + (int)pixelRect.x + "," + (int)pixelRect.y + " " +
                  (int)pixelRect.width + "x" + (int)pixelRect.height + ")");
            Interop.SetViewport(
                (int)System.Math.Round(pixelRect.x),
                (int)System.Math.Round(pixelRect.y),
                (int)System.Math.Round(pixelRect.width),
                (int)System.Math.Round(pixelRect.height));
        }

        /// <inheritdoc />
        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection)
        {
            RequireInitialized();

            m_View = view;
            m_Projection = projection;
            m_ViewProjectionSet = true;

            // No crossing here. The matrices only matter as the MVP a draw uploads, so they are folded into the
            // draw's uniform block instead of costing a call of their own.
        }

        /// <inheritdoc />
        public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth)
        {
            RequireInitialized();
            Trace("  ClearRenderTarget");
            // No depth attachment is ever allocated (neither shader writes or tests depth), so `clearDepth` has
            // nothing to clear. The JavaScript side is told anyway so the intent is visible in a GL trace.
            Interop.ClearTarget(clearColor, color.r, color.g, color.b, color.a);
        }

        // ------------------------------------------------------------------------------------- draws

        /// <inheritdoc />
        public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass,
                             MaterialPropertyBlock properties)
        {
            RequireInitialized();

            if (ReferenceEquals(mesh, null))
                throw new ArgumentNullException(nameof(mesh));
            if (ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(material));
            if (!m_ViewProjectionSet)
                throw new InvalidOperationException("WebGL2Backend.DrawMesh: no SetViewProjection preceded this draw.");

            Shader shader = material.shader;

            if (ReferenceEquals(shader, null))
                throw new InvalidOperationException($"WebGL2Backend.DrawMesh: material '{material.name}' has no shader.");

            string shaderName = shader.name;

            if (!IsPortedShader(shaderName))
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.DrawMesh: shader '{shaderName}' is not ported. This backend implements " +
                    PortedShaderList + "; every other program belongs to a later slice.");
            }

            Trace("  DrawMesh(" + (string.IsNullOrEmpty(mesh.name) ? "(unnamed)" : mesh.name) + ", '" +
                  shaderName + "', pass " + pass + ")");

            // Unity's -1 means "every pass". Every program ported so far is single-pass, so -1 and 0 name the
            // same draw; the module rejects an index past the program's declared pass count, which is where a
            // genuine multi-pass mismatch surfaces with the program's real pass count in the message.
            pass = NormalizePass(pass);

            if (!m_ResolvedShaders.Contains(shaderName) && !ResolveShader(shader))
                throw new InvalidOperationException($"WebGL2Backend.DrawMesh: '{shaderName}' failed to resolve.");

            int indexCount = EnsureMeshUploaded(mesh, subMesh);

            if (indexCount == 0)
                return;

            BuildUniformBlock(material, properties, model, shaderName);

            m_DrawInfo[0] = indexCount;
            m_DrawInfo[1] = TextureId(material, properties, IdMainTex);

            // For UIGradient, _MainTex IS the ramp atlas. The backend's fallback for an unbound _MainTex is a
            // 1x1 opaque WHITE texture, which makes every ramp sample (1,1,1,1) — a flat white gradient that
            // looks like a rendering choice rather than a missing resource. Say so instead.
            if (shaderName == GradientShader && m_DrawInfo[1] == NoTexture && m_WarnedOnce.Add("gradient-ramp"))
            {
                Debug.LogError(
                    "WebGL2Backend: a NowUI/UI Gradient draw has no _MainTex, so the ramp atlas is missing and " +
                    "every gradient will render flat white. NowGradientMaterials assigns the atlas as the " +
                    "material's mainTexture; check that NowGradientRampCache published one. This message " +
                    "appears once.");
            }
            m_DrawInfo[2] = TextureId(material, properties, IdTextureMask0);
            m_DrawInfo[3] = TextureId(material, properties, IdTextureMask1);
            m_DrawInfo[4] = pass;
            m_DrawInfo[5] = NoTexture;
            m_DrawInfo[6] = NoTexture;
            m_DrawInfo[7] = NoTexture;

            if (shaderName == TextShader)
            {
                m_DrawInfo[7] = TextureId(material, properties, IdGradientRampTexture);

                // The scan is skipped entirely once the atlas exists, which is from the first gradient of any
                // kind onward, so the common case costs one dictionary lookup and no vertex walk.
                if (m_DrawInfo[7] == NoTexture)
                    WarnOnMissingTextGradientRamp(mesh);
            }

            if (shaderName == SdfShader)
            {
                m_DrawInfo[5] = TextureId(material, properties, IdSdfImageField);
                m_DrawInfo[6] = TextureId(material, properties, IdSdfImageColor);

                // Immediately before the draw and never otherwise: nowui-gl.js consumes the pending flag this
                // sets, so a draw that skipped it fails with the reason rather than rendering an empty scene.
                BuildSdfUniformBlock(material, properties);
                Interop.SetSdfUniforms(MemoryMarshal.AsBytes(new Span<float>(m_SdfUniforms)));
            }

            Interop.Draw(
                mesh.GetInstanceID(),
                shaderName,
                m_DrawInfo,
                MemoryMarshal.AsBytes(new Span<float>(m_Uniforms)));
        }

        // ------------------------------------------------------------------------------------- mesh upload

        /// <summary>
        /// Packs one sub-mesh's nine vertex streams and index list into a single payload and ships it across in
        /// one call. Returns the index count, or 0 when there is nothing to draw.
        /// </summary>
        /// <remarks>
        /// <para>The nine-stream path is the only one this build can take: the interleaved path is gated on
        /// <c>NowLottieNative.packRenderAvailable</c>, a hard <c>false</c> under <c>NOWUI_VG_DISABLE_NATIVE</c>,
        /// which <c>NowUI.Runtime.csproj</c> defines. The public read-back getters de-interleave transparently,
        /// so this code keeps working if that ever changes.</para>
        /// <para><b>Every draw re-uploads.</b> <c>Mesh.version</c> is <c>internal</c> and this assembly is not a
        /// friend of <c>NowUI.Engine</c>, so the documented "cache by (instance id, version)" skip is not
        /// reachable from here. NowUI clears and refills its meshes every frame anyway, so the skip would rarely
        /// fire; the cost that remains is the read-back copy into the lists below, which is measurable and is
        /// reported rather than assumed away.</para>
        /// </remarks>
        int EnsureMeshUploaded(Mesh mesh, int subMesh)
        {
            if (subMesh < 0 || subMesh >= mesh.subMeshCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(subMesh), $"WebGL2Backend.DrawMesh: sub-mesh {subMesh} of a mesh with {mesh.subMeshCount}.");
            }

            SubMeshDescriptor descriptor = mesh.GetSubMesh(subMesh);

            if (descriptor.topology != MeshTopology.Triangles)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.DrawMesh: sub-mesh topology {descriptor.topology}. NowUI's render meshes are " +
                    "triangle lists; other topologies belong to a later slice.");
            }

            int vertexCount = mesh.vertexCount;

            // GetTriangles resolves baseVertex for us, so the indices returned address the whole vertex buffer.
            mesh.GetTriangles(m_Indices, subMesh);

            if (vertexCount == 0 || m_Indices.Count == 0)
                return 0;

            mesh.GetVertices(m_Positions);
            mesh.GetUVs(0, m_Uv0);

            for (int channel = 1; channel <= 7; ++channel)
                mesh.GetUVs(channel, m_UvN[channel]);

            RequireStream(mesh, "POSITION", m_Positions.Count, vertexCount);
            RequireStream(mesh, "TEXCOORD0", m_Uv0.Count, vertexCount);

            for (int channel = 1; channel <= 7; ++channel)
                RequireStream(mesh, "TEXCOORD" + channel, m_UvN[channel].Count, vertexCount);

            int vertexBytes = 0;
            for (int a = 0; a < StreamElementSize.Length; ++a)
                vertexBytes += StreamElementSize[a] * vertexCount;

            int indexBytes = m_Indices.Count * 4;
            EnsurePayload(vertexBytes + indexBytes);

            var payload = new Span<byte>(m_Payload);
            int cursor = 0;

            m_MeshHeader[0] = vertexBytes;
            m_MeshHeader[1] = m_Indices.Count;

            m_MeshHeader[2] = cursor;
            for (int v = 0; v < vertexCount; ++v)
            {
                Vector3 p = m_Positions[v];
                WriteFloat(payload, ref cursor, p.x);
                WriteFloat(payload, ref cursor, p.y);
                WriteFloat(payload, ref cursor, p.z);
            }

            m_MeshHeader[3] = cursor;
            for (int v = 0; v < vertexCount; ++v)
            {
                Vector2 uv = m_Uv0[v];
                WriteFloat(payload, ref cursor, uv.x);
                WriteFloat(payload, ref cursor, uv.y);
            }

            for (int channel = 1; channel <= 7; ++channel)
            {
                m_MeshHeader[3 + channel] = cursor;
                List<Vector4> stream = m_UvN[channel];

                for (int v = 0; v < vertexCount; ++v)
                {
                    Vector4 value = stream[v];
                    WriteFloat(payload, ref cursor, value.x);
                    WriteFloat(payload, ref cursor, value.y);
                    WriteFloat(payload, ref cursor, value.z);
                    WriteFloat(payload, ref cursor, value.w);
                }
            }

            // UNSIGNED_INT indices unconditionally. The shim's index format is not readable from here (the
            // public getter widens to int), and a 32-bit index is always correct where a 16-bit one would be;
            // the cost is two bytes per index on meshes that would have fitted in UInt16.
            for (int i = 0; i < m_Indices.Count; ++i)
            {
                int index = m_Indices[i];

                if ((uint)index >= (uint)vertexCount)
                {
                    throw new InvalidOperationException(
                        $"WebGL2Backend.DrawMesh: index {index} of sub-mesh {subMesh} addresses vertex {index} of " +
                        $"{vertexCount}. The mesh's index buffer and vertex streams disagree.");
                }

                WriteInt(payload, ref cursor, index);
            }

            Interop.UploadMesh(mesh.GetInstanceID(), vertexCount, m_MeshHeader, payload.Slice(0, cursor));
            return m_Indices.Count;
        }

        static void RequireStream(Mesh mesh, string semantic, int actual, int expected)
        {
            if (actual == expected)
                return;

            throw new InvalidOperationException(
                $"WebGL2Backend.DrawMesh: mesh '{mesh.name}' has {actual} {semantic} elements for {expected} " +
                "vertices. NowUI's render layout writes all nine streams at full length.");
        }

        /// <summary>
        /// Reports a text vertex that asks for a gradient while <c>_NowGradientRampTexture</c> is unset.
        /// </summary>
        /// <remarks>
        /// <para>The gradient branch is ported, so the failure this guards against is no longer "the maths is
        /// missing" but "the ramp atlas never reached the backend". The two produce the SAME picture: an unbound
        /// sampler falls back to 1x1 opaque white, every ramp texel comes back (1,1,1,1), and
        /// <c>fillColor *= ramp</c> leaves the glyph at its flat colour — plausible flat text, the exact class
        /// of bug M2-ShaderPort.md section 4.5 wrote its condition to prevent. This is the same guard aimed at
        /// what can still go wrong.</para>
        /// <para>Called only when the ramp id came back <see cref="NoTexture"/>, so the vertex walk stops
        /// happening the moment NowGradientRampCache allocates its first row — which any gradient anywhere in
        /// the frame does. <c>m_UvN[5]</c> still holds this mesh's TEXCOORD5 from
        /// <see cref="EnsureMeshUploaded"/>, which ran a few lines earlier.</para>
        /// </remarks>
        void WarnOnMissingTextGradientRamp(Mesh mesh)
        {
            if (m_WarnedOnce.Contains("text-gradient-ramp"))
                return;

            List<Vector4> extras = m_UvN[5];

            for (int v = 0; v < extras.Count; ++v)
            {
                if (extras[v].w == 0f)
                    continue;

                m_WarnedOnce.Add("text-gradient-ramp");
                Debug.LogError(
                    $"WebGL2Backend: mesh '{mesh.name}' carries a text vertex with extras.w = {extras[v].w}, which " +
                    "asks NowUITextGradientSample for row " + Mathf.Floor(extras[v].w) + " of the gradient ramp " +
                    "atlas — but _NowGradientRampTexture is not set, on the material, in the property block or " +
                    "in NowRuntime.globals. The sampler falls back to 1x1 white, so this text renders with its " +
                    "flat colour. NowGradientRampCache publishes the atlas with Shader.SetGlobalTexture " +
                    "(NowGradient.cs:580); check that the global survived the trip through the shim. This " +
                    "message appears once.");
                return;
            }
        }

        void EnsurePayload(int bytes)
        {
            if (m_Payload.Length >= bytes)
                return;

            int size = m_Payload.Length;
            while (size < bytes)
                size *= 2;

            m_Payload = new byte[size];
        }

        static void WriteFloat(Span<byte> destination, ref int cursor, float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(cursor, 4), value);
            cursor += 4;
        }

        static void WriteInt(Span<byte> destination, ref int cursor, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(cursor, 4), value);
            cursor += 4;
        }

        // ------------------------------------------------------------------------------------- uniforms

        /// <summary>
        /// Resolves every uniform the two programs declare into the flat block, in the order §7.3 gives:
        /// property block, then material, then globals, then a hard fallback.
        /// </summary>
        void BuildUniformBlock(Material material, MaterialPropertyBlock properties, in Matrix4x4 model,
                               string shaderName)
        {
            Array.Clear(m_Uniforms, 0, m_Uniforms.Length);

            // MVP = projection * view * model, multiplied here in the same float arithmetic Unity would use, so
            // the vertex shader stays one line and one uniform. UnityObjectToClipPos nests as VP * (M * v); the
            // difference is float rounding far below a pixel.
            Matrix4x4 mvp = m_Projection * m_View * model;
            WriteMatrix(m_Uniforms, UniformSlots.Mvp, mvp);

            // `_MainTex_ST` is never written by NowUI. mainTextureScale/Offset are the public getters that
            // already answer (1,1) and (0,0) for an unset property, which is the identity a shader assumes.
            Vector2 scale = material.mainTextureScale;
            Vector2 offset = material.mainTextureOffset;
            m_Uniforms[UniformSlots.MainTexST + 0] = scale.x;
            m_Uniforms[UniformSlots.MainTexST + 1] = scale.y;
            m_Uniforms[UniformSlots.MainTexST + 2] = offset.x;
            m_Uniforms[UniformSlots.MainTexST + 3] = offset.y;

            if (shaderName == RectangleShader)
            {
                m_Uniforms[UniformSlots.PremultipliedTexture] =
                    ResolveFloat(material, properties, IdPremultipliedTexture);
            }
            else if (shaderName == GradientShader)
            {
                // (1/width, 1/height, width, height) of the shared ramp atlas, written by
                // NowGradientMaterials.TryGet alongside the atlas itself. GL's all-zero default is NOT a safe
                // fallback: the shader computes the atlas row as `(row + 0.5) * texelSize.y`, so a zero .y makes
                // EVERY gradient sample row 0 -- a smooth, plausible sweep of the wrong ramp, which is the
                // failure mode hardest to notice. (The fixed-step branch also collapses to column 0.) The
                // Properties-block default, a 256x256 atlas, is used instead, and the substitution is logged.
                Vector4 texelSize = ResolveVector(material, properties, IdGradientRampTexelSize);

                if (texelSize.z < 1f || texelSize.w < 1f || texelSize.x <= 0f || texelSize.y <= 0f)
                {
                    if (m_WarnedOnce.Add("gradient-texel-size"))
                    {
                        Debug.LogWarning(
                            $"WebGL2Backend: _NowGradientRampTexelSize resolved to {texelSize}, which cannot " +
                            "describe a ramp atlas. Falling back to the shader's 256x256 default. Expect this " +
                            "when NowGradientMaterials could not publish the atlas. This message appears once.");
                    }

                    texelSize = DefaultRampTexelSize;
                }

                m_Uniforms[UniformSlots.GradientRampTexelSize + 0] = texelSize.x;
                m_Uniforms[UniformSlots.GradientRampTexelSize + 1] = texelSize.y;
                m_Uniforms[UniformSlots.GradientRampTexelSize + 2] = texelSize.z;
                m_Uniforms[UniformSlots.GradientRampTexelSize + 3] = texelSize.w;
            }
            else if (shaderName == RippleShader || shaderName == BezierShader)
            {
                // Nothing of their own. Neither declares a sampler, a _MainTex or a scalar uniform; every
                // uniform they read comes from the shared mask block below. UIBezier carries its four control
                // points, its stroke widths and its UI-space position in the VERTEX STREAMS, not in uniforms.
            }
            else if (shaderName == ColorPickerShader)
            {
                // 0 SaturationValue, 1 Hue, 2 Alpha, set once per material at creation
                // (NowValueControls.cs:953). Note what makes this one dangerous to get wrong: unlike every
                // other uniform here, ZERO IS A VALID VALUE — it is the SaturationValue mode. So a picker
                // whose _Mode never arrives does not fail, it renders all three strips as saturation/value
                // squares: three plausible gradients, one of them correct. There is no sentinel to check and
                // nothing to warn about, which is exactly why it is called out here.
                m_Uniforms[UniformSlots.ColorPickerMode] = ResolveFloat(material, properties, IdColorPickerMode);
            }
            else if (shaderName == GlassShader)
            {
                // All three flags are 0 on every path the browser host can reach, and that is a fact about the
                // BUILD rather than about this backend:
                //   * _NowGlassUseBackdrop — Now.StartUI drives the immediate path, and Now.cs:1155 calls
                //     NowGlassRenderer.DisableBackdropGlobal() for every NowMeshKind.Glass mesh on it, recording
                //     a NowGlassFallbackReason.LegacyImmediatePath. So glass in the browser is a translucent
                //     tinted rounded rect with an outline — which is what Unity's immediate path draws too.
                //   * _NowGlassUseStereoBackdrop — set only for a Tex2DArray capture target, i.e. an XR eye
                //     texture, which this host never allocates.
                //   * _NowMaterialGlassMode — its only writer is NowWorldGraphic.cs, which is on
                //     NowUI.Runtime.csproj's exclude list.
                // The last two feed the shader's unported-branch marker: a non-zero value paints the panel
                // magenta rather than silently selecting a half of the shader that was never ported.
                m_Uniforms[UniformSlots.GlassUseBackdrop] =
                    ResolveFloat(material, properties, IdGlassUseBackdrop);

                // MEASURED once per session, because the paragraph above is a claim about frozen code and this is
                // the only place the value itself can be seen. The comment cites Now.cs:1155 (the plain immediate
                // path). It is not the site that applies when a backdrop was actually produced: with
                // NowGlassRenderer.CanDrawSelfReplay true, Now.cs:2072 takes DrawLegacySelfReplayGlass instead,
                // which blurs into a render texture and calls EnableBackdropGlobal(blurred, ...) at Now.cs:2166 —
                // and then Now.cs:2192, the first statement of DrawLegacyReplayBatch, calls DisableBackdropGlobal()
                // for every NowMeshKind.Glass batch, cancelling it before the pane is drawn. The blur runs, is paid
                // for, and is discarded. This log makes the resulting 0 an observation rather than an inference.
                if (!m_ReportedGlassBackdropFlag)
                {
                    m_ReportedGlassBackdropFlag = true;
                    Console.WriteLine(
                        "[NowUI] WebGL2Backend: first 'NowUI/UI Glass' draw resolved _NowGlassUseBackdrop = " +
                        m_Uniforms[UniformSlots.GlassUseBackdrop].ToString("0.###") +
                        ". Zero selects the shader's no-backdrop branch, so the pane is a translucent tinted " +
                        "rounded rect and the backdrop behind it is NOT blurred, whatever the blur pipeline did.");
                }
                m_Uniforms[UniformSlots.GlassUseStereoBackdrop] =
                    ResolveFloat(material, properties, IdGlassUseStereoBackdrop);
                m_Uniforms[UniformSlots.GlassMaterialMode] =
                    ResolveFloat(material, properties, IdGlassMaterialMode);

                // (scaleX, scaleY, offsetX, offsetY) applied to the screen UV before sampling the backdrop.
                // ZERO IS NOT A SAFE FALLBACK: it collapses every backdrop sample onto texel (0,0), which reads
                // as a deliberate flat tint rather than as a missing uniform. (1,1,0,0) is both the Properties
                // default (UIGlass.shader:15) and what NowGlassRenderer.DisableBackdropGlobal writes (:632).
                Vector4 backdropUv = ResolveVector(material, properties, IdBackdropUvTransform);

                if (backdropUv == Vector4.zero)
                    backdropUv = IdentityUvTransform;

                m_Uniforms[UniformSlots.BackdropUvTransform + 0] = backdropUv.x;
                m_Uniforms[UniformSlots.BackdropUvTransform + 1] = backdropUv.y;
                m_Uniforms[UniformSlots.BackdropUvTransform + 2] = backdropUv.z;
                m_Uniforms[UniformSlots.BackdropUvTransform + 3] = backdropUv.w;
            }
            else if (shaderName == GlassBlurShader)
            {
                // Reached through Blit, never DrawMesh. All four uniforms are SHADER GLOBALS —
                // NowGlassRenderer.BlitBlur writes them with commandBuffer.SetGlobalVector / SetGlobalTexture
                // (:860-891), so they live in NowRuntime.globals and not in the blur material's bag.
                //
                // NONE of the three vectors has a safe zero default, and all three fail QUIETLY:
                //   * a zero _NowBlurTexelSize or _NowBlurDirection puts all nine taps on one texel. The weights
                //     still sum to one, so the pass becomes a pixel-perfect COPY — glass then renders over a
                //     SHARP backdrop and reads as "the blur radius did not take".
                //   * a zero _NowBlurSourceScaleOffset collapses the whole image onto the texel at .zw.
                // Only the scale/offset has a meaningful identity to fall back to; the other two are genuinely
                // caller-supplied, so a zero there is reported rather than papered over.
                Vector4 texelSize = ResolveVector(material, properties, IdBlurTexelSize);
                Vector4 sourceScaleOffset = ResolveVector(material, properties, IdBlurSourceScaleOffset);
                Vector4 direction = ResolveVector(material, properties, IdBlurDirection);

                if (sourceScaleOffset == Vector4.zero)
                    sourceScaleOffset = IdentityUvTransform;

                if ((texelSize.x <= 0f || texelSize.y <= 0f) && m_WarnedOnce.Add("blur-texel-size"))
                {
                    Debug.LogWarning(
                        $"WebGL2Backend: _NowBlurTexelSize resolved to {texelSize}, which cannot describe a " +
                        "blur source. Every tap will land on the same texel, so the pass will be a " +
                        "pixel-perfect copy and the glass behind it will look sharp rather than blurred. " +
                        "Expect this if the blur was invoked without NowGlassRenderer setting its globals. " +
                        "This message appears once.");
                }

                m_Uniforms[UniformSlots.BlurTexelSize + 0] = texelSize.x;
                m_Uniforms[UniformSlots.BlurTexelSize + 1] = texelSize.y;
                m_Uniforms[UniformSlots.BlurTexelSize + 2] = texelSize.z;
                m_Uniforms[UniformSlots.BlurTexelSize + 3] = texelSize.w;

                m_Uniforms[UniformSlots.BlurSourceScaleOffset + 0] = sourceScaleOffset.x;
                m_Uniforms[UniformSlots.BlurSourceScaleOffset + 1] = sourceScaleOffset.y;
                m_Uniforms[UniformSlots.BlurSourceScaleOffset + 2] = sourceScaleOffset.z;
                m_Uniforms[UniformSlots.BlurSourceScaleOffset + 3] = sourceScaleOffset.w;

                // float2 in the HLSL, so only two floats are pushed. The slot is four wide for alignment.
                m_Uniforms[UniformSlots.BlurDirection + 0] = direction.x;
                m_Uniforms[UniformSlots.BlurDirection + 1] = direction.y;
            }
            else if (shaderName == SdfImageFieldShader)
            {
                // Reached through Blit only, five passes of it, and every uniform comes from the bake
                // material's own bag. All five are pushed for every pass rather than per-pass: the passes share
                // one CGINCLUDE block, a pass that does not read one has its location optimised away, and
                // per-pass selection here would be a second place for the pass indices to be wrong.
                //
                // NO SANITISING, NO FALLBACKS. Unlike the glass and gradient branches above, there is no
                // identity to substitute for a missing value here -- these are the bake's geometry, and a
                // guessed field size or threshold would produce a smooth, wrong distance field that the SDF
                // scene would then render as a plausible silhouette. If the driver ever sends zeros, the
                // symptom is meant to be visible.
                WriteVector(m_Uniforms, UniformSlots.SourceUv, ResolveVector(material, properties, IdSourceUv));
                WriteVector(m_Uniforms, UniformSlots.FieldParams,
                            ResolveVector(material, properties, IdFieldParams));
                WriteVector(m_Uniforms, UniformSlots.FieldTexels,
                            ResolveVector(material, properties, IdFieldTexels));
                WriteVector(m_Uniforms, UniformSlots.StampRect,
                            ResolveVector(material, properties, IdStampRect));
                m_Uniforms[UniformSlots.Step] = ResolveFloat(material, properties, IdStep);
            }
            else if (shaderName == SdfShader)
            {
                // Nothing of its own in the SHARED block. Every uniform this program declares beyond the MVP
                // lives in m_SdfUniforms, which DrawMesh ships separately; see BuildSdfUniformBlock. The branch
                // exists so the SDF program does not fall into the text branch below and resolve — and then
                // warn about — a _NowUITextSdfEncoding it does not declare.
                //
                // _MainTex_ST is written above for every program. This one does not declare it (its vertex
                // stage passes v.uv through untransformed, NowSdfShaderV2.cginc:1503), so the slot is ignored.
            }
            else
            {
                // Which value is CORRECT here depends on which baker produced the page, so this cannot be a
                // one-sided check any more:
                //
                //   1 - the managed baker's packed SDF16 pages (NowFont writes it before the first text draw).
                //   0 - an ordinary MTSDF atlas, decoded by median-RGB. NowFontCompiler.CreateFont writes it for
                //       every page the NATIVE compiler produces, and since this host prefers the native msdf
                //       plugin (see Program.cs), 0 is the ordinary case rather than a symptom.
                //
                // So the warning now fires only when the managed baker is the one in use, which is the only
                // situation in which 0 still means "text will render blurry and subtly wrong". Left unconditional
                // it fired on every native run and said the opposite of the truth.
                float encoding = ResolveFloat(material, properties, IdTextSdfEncoding);
                m_Uniforms[UniformSlots.TextSdfEncoding] = encoding;

                if (encoding <= 0.5f && NowFontCompiler.forceManagedCompiler && m_WarnedOnce.Add("sdf-encoding"))
                {
                    Debug.LogWarning(
                        "WebGL2Backend: _NowUITextSdfEncoding resolved to 0 while the managed baker is forced, so " +
                        "the text shader will take the median-RGB branch on a page that packs SDF16; text will " +
                        "render blurry and subtly wrong until that is understood. This message appears once.");
                }
            }

            float maskCount = ResolveFloat(material, properties, IdMaskCount);
            float textureMaskCount = ResolveFloat(material, properties, IdTextureMaskCount);
            m_Uniforms[UniformSlots.MaskCount] = maskCount;
            m_Uniforms[UniformSlots.TextureMaskCount] = textureMaskCount;

            // The vector arrays are only ever set when the count is above zero (NowMaskShader.Apply skips them
            // otherwise), so "absent" is normal and means "leave the uniform at its all-zero default" — which the
            // shader's early-out then never reads.
            if (maskCount >= 0.5f)
            {
                CopyVectorArray(material, properties, IdMaskRects, UniformSlots.MaskRects, AnalyticMaskCapacity);
                CopyVectorArray(material, properties, IdMaskData, UniformSlots.MaskData, AnalyticMaskCapacity);
                CopyVectorArray(material, properties, IdMaskParams, UniformSlots.MaskParams, AnalyticMaskCapacity);
                CopyVectorArray(material, properties, IdMaskTransforms, UniformSlots.MaskTransforms,
                                AnalyticMaskCapacity);
            }

            if (textureMaskCount >= 0.5f)
            {
                CopyVectorArray(material, properties, IdTextureMaskRects, UniformSlots.TextureMaskRects,
                                TextureMaskCapacity);
                CopyVectorArray(material, properties, IdTextureMaskParams, UniformSlots.TextureMaskParams,
                                TextureMaskCapacity);
                CopyVectorArray(material, properties, IdTextureMaskTransforms, UniformSlots.TextureMaskTransforms,
                                TextureMaskCapacity);
            }
        }

        /// <summary>
        /// Resolves everything <c>NowUI/SDF Scene</c> declares into <see cref="m_SdfUniforms"/>.
        /// </summary>
        /// <remarks>
        /// <para>Written only for an SDF draw, and shipped by its own interop call, because the nine arrays are
        /// 480 vec4 — see <see cref="SdfUniformSlots"/>.</para>
        /// <para>Everything here comes from the material bag, written by <c>NowSdfCache.Upload</c>
        /// (NowSdf.cs:4873-4905) onto a material instance that cache owns exclusively. Nothing arrives as a
        /// shader global and nothing arrives through a <c>MaterialPropertyBlock</c> — but both are consulted
        /// anyway, through the same <c>ResolveFloat</c>/<c>ResolveVector</c> order every other program uses, so
        /// the resolution rule stays one rule.</para>
        /// </remarks>
        void BuildSdfUniformBlock(Material material, MaterialPropertyBlock properties)
        {
            Array.Clear(m_SdfUniforms, 0, m_SdfUniforms.Length);

            // HONOURING _NowSdfAbiVersion. The shader never reads it; the C# builder does, to decide whether a
            // material can carry the scene at all (NowSdf.cs:3506-3527, :4532-4551). This port implements the
            // V2 contract only, so a material declaring anything else has its arrays laid out under a contract
            // this GLSL does not implement. Refuse it by name rather than draw a plausible wrong scene.
            float abi = ResolveFloat(material, properties, IdSdfAbiVersion);

            if (abi != SdfPortedAbiVersion)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend: SDF material '{material.name}' declares _NowSdfAbiVersion = {abi}. This " +
                    $"backend ports NowSdfShaderV{(int)SdfPortedAbiVersion}.cginc only. A material on a " +
                    "different ABI packs its uniform arrays under a different contract, so drawing it through " +
                    "this program would render a plausible wrong scene rather than fail.");
            }

            CopyVectorArray(material, properties, IdSdfData0, m_SdfUniforms, SdfUniformSlots.Data0,
                            SdfShapeCapacity, "_SdfData0");
            CopyVectorArray(material, properties, IdSdfData1, m_SdfUniforms, SdfUniformSlots.Data1,
                            SdfShapeCapacity, "_SdfData1");
            CopyVectorArray(material, properties, IdSdfData2, m_SdfUniforms, SdfUniformSlots.Data2,
                            SdfShapeCapacity, "_SdfData2");
            CopyVectorArray(material, properties, IdSdfShapeMeta, m_SdfUniforms, SdfUniformSlots.ShapeMeta,
                            SdfShapeCapacity, "_SdfShapeMeta");
            CopyVectorArray(material, properties, IdSdfColors, m_SdfUniforms, SdfUniformSlots.Colors,
                            SdfShapeCapacity, "_SdfColors");
            CopyVectorArray(material, properties, IdSdfUvs, m_SdfUniforms, SdfUniformSlots.Uvs,
                            SdfShapeCapacity, "_SdfUvs");
            CopyVectorArray(material, properties, IdSdfImageUvs, m_SdfUniforms, SdfUniformSlots.ImageUvs,
                            SdfShapeCapacity, "_SdfImageUvs");
            CopyVectorArray(material, properties, IdSdfLayerData0, m_SdfUniforms, SdfUniformSlots.LayerData0,
                            SdfLayerCapacity, "_SdfLayerData0");
            CopyVectorArray(material, properties, IdSdfLayerData1, m_SdfUniforms, SdfUniformSlots.LayerData1,
                            SdfLayerCapacity, "_SdfLayerData1");

            // (fieldWidth, fieldHeight, colorWidth, colorHeight) in texels. Upload writes Vector4.one when the
            // scene has no image atlas (NowSdf.cs:4883), and the shader divides by max(atlasSize, 1.0), so a
            // zero is harmless here — but it is also never what the caller meant, so the identity is restored.
            Vector4 atlasSize = ResolveVector(material, properties, IdSdfImageAtlasSize);
            WriteVector(m_SdfUniforms, SdfUniformSlots.ImageAtlasSize,
                        atlasSize == Vector4.zero ? Vector4.one : atlasSize);

            WriteVector(m_SdfUniforms, SdfUniformSlots.Outline, ResolveVector(material, properties, IdSdfOutline));
            WriteVector(m_SdfUniforms, SdfUniformSlots.OutlineColor,
                        ResolveVector(material, properties, IdSdfOutlineColor));
            WriteVector(m_SdfUniforms, SdfUniformSlots.Glow, ResolveVector(material, properties, IdSdfGlow));
            WriteVector(m_SdfUniforms, SdfUniformSlots.GlowColor,
                        ResolveVector(material, properties, IdSdfGlowColor));
            WriteVector(m_SdfUniforms, SdfUniformSlots.Shadow, ResolveVector(material, properties, IdSdfShadow));
            WriteVector(m_SdfUniforms, SdfUniformSlots.ShadowColor,
                        ResolveVector(material, properties, IdSdfShadowColor));
            WriteVector(m_SdfUniforms, SdfUniformSlots.InnerShadow,
                        ResolveVector(material, properties, IdSdfInnerShadow));
            WriteVector(m_SdfUniforms, SdfUniformSlots.InnerShadowColor,
                        ResolveVector(material, properties, IdSdfInnerShadowColor));
            WriteVector(m_SdfUniforms, SdfUniformSlots.Emboss, ResolveVector(material, properties, IdSdfEmboss));
            WriteVector(m_SdfUniforms, SdfUniformSlots.Contour, ResolveVector(material, properties, IdSdfContour));
            WriteVector(m_SdfUniforms, SdfUniformSlots.ContourColor,
                        ResolveVector(material, properties, IdSdfContourColor));
            WriteVector(m_SdfUniforms, SdfUniformSlots.ContourMask,
                        ResolveVector(material, properties, IdSdfContourMask));
            WriteVector(m_SdfUniforms, SdfUniformSlots.Warp, ResolveVector(material, properties, IdSdfWarp));

            m_SdfUniforms[SdfUniformSlots.ShapeCount] = ResolveFloat(material, properties, IdSdfShapeCount);
            m_SdfUniforms[SdfUniformSlots.LayerCount] = ResolveFloat(material, properties, IdSdfLayerCount);
            m_SdfUniforms[SdfUniformSlots.Feather] = ResolveFloat(material, properties, IdSdfFeather);
            m_SdfUniforms[SdfUniformSlots.MaskOutput] = ResolveFloat(material, properties, IdSdfMaskOutput);
            m_SdfUniforms[SdfUniformSlots.CanvasLayout] = ResolveFloat(material, properties, IdSdfCanvasLayout);

            // ZERO IS WRONG HERE, not merely unset. The shader reads
            // `hasFiniteTextEffectLimit = _SdfTextEffectLimit < 100000.0`, so a zero claims every exterior
            // effect must have faded out by distance 0 — erasing outline, glow, shadow and contour from a scene
            // that has no glyphs at all. Upload always writes it (NowSdf.cs:4876, starting from 100000 and
            // lowered only by Glyph and Image nodes), so a zero means the material never had Upload run, which
            // is worth saying rather than papering over silently.
            float textEffectLimit = ResolveFloat(material, properties, IdSdfTextEffectLimit);

            if (textEffectLimit <= 0f)
            {
                if (m_WarnedOnce.Add("sdf-text-effect-limit"))
                {
                    Debug.LogWarning(
                        $"WebGL2Backend: _SdfTextEffectLimit resolved to {textEffectLimit} on SDF material " +
                        $"'{material.name}'. NowSdfCache.Upload always writes it, so this material was never " +
                        "uploaded. Falling back to 100000 (the analytic-only value) so outline, glow, shadow " +
                        "and contours are not silently erased. This message appears once.");
                }

                textEffectLimit = 100000f;
            }

            m_SdfUniforms[SdfUniformSlots.TextEffectLimit] = textEffectLimit;

            // Unity's _Time.y, the only built-in this program reads, and only from the domain warp — whose
            // guard is `_SdfWarp.x > 0`, false for the material default (0, 1, 0, 0). Fed from the same
            // unscaled clock Unity's _Time.y carries.
            m_SdfUniforms[SdfUniformSlots.Time] = Time.unscaledTime;

            // Image nodes evaluate here, but the atlases they read are built by "Hidden/NowUI/SDF Image Field",
            // which is NOT ported. Both samplers therefore fall back to 1x1 opaque black, whose r = 0 makes
            // every interior distance 0 and renders the node as a filled rectangle. Said once, so an Image node
            // in a capture cannot be read as a rendering result.
            if (m_DrawInfo[5] == NoTexture && m_DrawInfo[6] == NoTexture &&
                m_SdfUniforms[SdfUniformSlots.ShapeCount] > 0f &&
                HasImageNode() && m_WarnedOnce.Add("sdf-image-node"))
            {
                Debug.LogWarning(
                    "WebGL2Backend: this SDF scene contains an Image or Sprite node, but no image atlas is " +
                    "bound because 'Hidden/NowUI/SDF Image Field' — the jump-flood program that builds one — " +
                    "is not ported. _SdfImageField falls back to 1x1 black, so the node renders as a filled " +
                    "rectangle rather than as its silhouette. This message appears once.");
            }
        }

        /// <summary>
        /// Whether the shape block just written contains an Image node (<c>NowSdfShapeType.Image</c> == 10).
        /// </summary>
        /// <remarks>
        /// Type is <c>_SdfData0[i].x</c>, and the shader's own test for it is the half-open
        /// <c>type &gt; 9.5 &amp;&amp; type &lt; 10.5</c> band; the same test is used here rather than an
        /// equality, so the two cannot disagree about a value that is not exactly 10.
        /// </remarks>
        bool HasImageNode()
        {
            int count = Math.Min((int)m_SdfUniforms[SdfUniformSlots.ShapeCount], SdfShapeCapacity);

            for (int i = 0; i < count; ++i)
            {
                float type = m_SdfUniforms[SdfUniformSlots.Data0 + i * 4];

                if (type > 9.5f && type < 10.5f)
                    return true;
            }

            return false;
        }

        static void WriteVector(float[] destination, int slot, in Vector4 value)
        {
            destination[slot + 0] = value.x;
            destination[slot + 1] = value.y;
            destination[slot + 2] = value.z;
            destination[slot + 3] = value.w;
        }

        static void WriteMatrix(float[] destination, int slot, in Matrix4x4 m)
        {
            // Column major, which is both the shim's storage order and what uniformMatrix4fv wants with
            // transpose = false. Written out component by component rather than reinterpreted, so the ordering is
            // stated rather than inherited from a struct layout.
            destination[slot + 0] = m.m00; destination[slot + 1] = m.m10;
            destination[slot + 2] = m.m20; destination[slot + 3] = m.m30;
            destination[slot + 4] = m.m01; destination[slot + 5] = m.m11;
            destination[slot + 6] = m.m21; destination[slot + 7] = m.m31;
            destination[slot + 8] = m.m02; destination[slot + 9] = m.m12;
            destination[slot + 10] = m.m22; destination[slot + 11] = m.m32;
            destination[slot + 12] = m.m03; destination[slot + 13] = m.m13;
            destination[slot + 14] = m.m23; destination[slot + 15] = m.m33;
        }

        /// <summary>Property block, then material, then globals, then zero.</summary>
        /// <remarks>
        /// <para>The bag getters are used rather than <c>HasProperty</c> on purpose: <c>HasProperty</c> is true
        /// for a property the *shader declares* even when nobody assigned it, which is what NowUI's own feature
        /// detection depends on and is the wrong question here.</para>
        /// <para>The consequence is that a material which explicitly stores <c>0f</c> is indistinguishable from
        /// one that stores nothing, so both consult the globals. That is harmless for the four floats these two
        /// programs read — nothing in NowUI publishes any of them as a shader global — and the alternative needs
        /// <c>NowMaterialBag</c>, which is <c>internal</c>.</para>
        /// </remarks>
        static float ResolveFloat(Material material, MaterialPropertyBlock properties, int id)
        {
            if (properties != null && properties.HasProperty(id))
                return properties.GetFloat(id);

            float value = material.GetFloat(id);

            if (value != 0f)
                return value;

            return NowRuntime.globals.GetFloat(id);
        }

        /// <summary>
        /// The ramp-atlas texel size the <c>NowUI/UI Gradient</c> Properties block declares, and the value
        /// <c>GradientMaterial.mat</c> stores: a 256x256 atlas.
        /// </summary>
        static readonly Vector4 DefaultRampTexelSize = new Vector4(1f / 256f, 1f / 256f, 256f, 256f);

        /// <summary>
        /// The (scale, offset) identity, and the fallback for every UV-transform uniform whose all-zero GL
        /// default would collapse a sample onto one texel rather than merely look unset.
        /// </summary>
        static readonly Vector4 IdentityUvTransform = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>Property block, then material, then globals, then zero. The vector twin of ResolveFloat.</summary>
        /// <remarks>
        /// The same caveat applies: a material that explicitly stores <c>Vector4.zero</c> is indistinguishable
        /// from one that stores nothing, so both fall through to the globals. For the one vector these programs
        /// read that is harmless — an all-zero ramp texel size is not a value any caller means, and the caller
        /// checks for it.
        /// </remarks>
        static Vector4 ResolveVector(Material material, MaterialPropertyBlock properties, int id)
        {
            if (properties != null && properties.HasProperty(id))
                return properties.GetVector(id);

            Vector4 value = material.GetVector(id);

            if (value != Vector4.zero)
                return value;

            return NowRuntime.globals.GetVector(id);
        }

        /// <summary>
        /// The <c>info</c> value that means "no texture is bound; use the backend's fallback".
        /// </summary>
        /// <remarks>
        /// Zero is safe as the sentinel because the shim never issues it: <c>Object</c> starts its counter at -1
        /// and decrements, so every instance id is strictly negative (<c>Object.cs</c>, "Unity hands
        /// runtime-created objects negative, decreasing instance ids"). Mirrored as the <c>id === 0</c> test in
        /// <c>nowui-gl.js</c>.
        /// </remarks>
        const int NoTexture = 0;

        /// <summary>Resolves a texture to its instance id, or <see cref="NoTexture"/> for the fallback.</summary>
        static int TextureId(Material material, MaterialPropertyBlock properties, int id)
        {
            Texture texture = null;

            if (properties != null && properties.HasProperty(id))
                texture = properties.GetTexture(id);

            if (ReferenceEquals(texture, null))
                texture = material.GetTexture(id);

            // Globals are the only source for a shader-global texture; `_NowGradientRampTexture` arrives that
            // way. It is not sampled in this slice, but the lookup order is the same for every sampler.
            if (ReferenceEquals(texture, null))
                texture = NowRuntime.globals.GetTexture(id);

            return ReferenceEquals(texture, null) ? NoTexture : texture.GetInstanceID();
        }

        /// <summary>
        /// Copies a <c>Vector4[]</c> bag entry into the flat block, rejecting any length but the declared
        /// capacity.
        /// </summary>
        /// <remarks>
        /// <c>NowMaskShader</c> always passes the whole scratch array, so what reaches a backend is always
        /// exactly 8 (or 2) entries. A different length means an assumption broke upstream, and uploading it
        /// truncated or padded would produce a plausible wrong mask instead of an error.
        /// </remarks>
        void CopyVectorArray(Material material, MaterialPropertyBlock properties, int id, int slot, int capacity)
        {
            CopyVectorArray(material, properties, id, m_Uniforms, slot, capacity, "a mask vector array");
        }

        /// <summary>
        /// The general form: <paramref name="destination"/> and a name for the refusal message.
        /// </summary>
        /// <remarks>
        /// The SDF program needs it because its nine arrays live in a different block and because "a mask
        /// vector array" is the wrong thing to say about <c>_SdfData0</c>. The rule it enforces is identical
        /// and applies for the identical reason: <c>NowSdfCache.Upload</c>, like <c>NowMaskShader.Apply</c>,
        /// passes the WHOLE fixed-capacity scratch array to <c>SetVectorArray</c> regardless of how many
        /// entries the scene filled, so any other length means an assumption broke upstream.
        /// </remarks>
        void CopyVectorArray(Material material, MaterialPropertyBlock properties, int id, float[] destination,
                             int slot, int capacity, string name)
        {
            m_VectorArray.Clear();

            if (properties != null && properties.HasProperty(id))
                properties.GetVectorArray(id, m_VectorArray);

            if (m_VectorArray.Count == 0)
                material.GetVectorArray(id, m_VectorArray);

            if (m_VectorArray.Count == 0)
                return;

            if (m_VectorArray.Count != capacity)
            {
                throw new InvalidOperationException(
                    $"WebGL2Backend: {name} arrived with {m_VectorArray.Count} entries; the shader " +
                    $"declares exactly {capacity}. Uploading it truncated or padded would render a plausible " +
                    "wrong result, so it is refused.");
            }

            for (int i = 0; i < capacity; ++i)
            {
                Vector4 v = m_VectorArray[i];
                destination[slot + i * 4 + 0] = v.x;
                destination[slot + i * 4 + 1] = v.y;
                destination[slot + i * 4 + 2] = v.z;
                destination[slot + i * 4 + 3] = v.w;
            }
        }

        // ------------------------------------------------------------------------- render targets
        //
        // Everything past the default framebuffer. The invariants slice 1 established still hold: the viewport
        // follows the target (the shim's Bind issues SetViewport after every SetRenderTarget, and Blit sets the
        // destination's own viewport because no shim-side SetViewport follows it), and the view-projection is
        // set before any draw.

        /// <inheritdoc />
        /// <remarks>
        /// <para>Honours the descriptor's format, mip chain and depth bits, plus the sampler state that lives on
        /// the <see cref="Texture"/> rather than in the request (<c>filterMode</c>, <c>wrapModeU/V</c>). Returns
        /// <c>false</c> — never throws — for a request WebGL2 cannot serve, because that is what
        /// <c>RenderTexture.Create()</c> turns into its own <c>false</c> and what NowSdf's fallbacks read.</para>
        /// <para>Three requests are refused: a non-2D dimension (WebGL2 has array targets, but the only NowUI
        /// code that asks for one is <c>NowGlassRenderer</c>'s single-pass-instanced stereo path, unreachable in
        /// a browser); a float format on a context without the matching <c>EXT_color_buffer_*</c>; and
        /// <c>enableRandomWrite</c>, which needs compute that WebGL2 does not have.</para>
        /// <para>MSAA is flattened to one sample and warned about rather than refused: WebGL2 has no sampleable
        /// multisampled texture, and <see cref="caps"/> reports <c>maxMsaaSamples = 1</c> so nothing should be
        /// asking.</para>
        /// </remarks>
        public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request)
        {
            RequireInitialized();

            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            if (request.enableRandomWrite)
            {
                Debug.LogError(
                    $"WebGL2Backend.CreateRenderTexture: target '{texture.name}' asks for enableRandomWrite. " +
                    "WebGL2 has no compute and no image load/store, so there is no UAV to bind it as.");
                return false;
            }

            if (request.bindMS)
            {
                Debug.LogError(
                    $"WebGL2Backend.CreateRenderTexture: target '{texture.name}' asks for bindTextureMS. " +
                    "WebGL2 has no sampleable multisampled texture; caps.supportsMultisampledTextures is false " +
                    "for exactly this reason.");
                return false;
            }

            m_TargetInfo[0] = request.width;
            m_TargetInfo[1] = request.height;
            m_TargetInfo[2] = request.depthBits;
            m_TargetInfo[3] = request.volumeDepth;
            m_TargetInfo[4] = request.mipCount;
            m_TargetInfo[5] = request.msaaSamples;
            m_TargetInfo[6] = (int)request.format;
            m_TargetInfo[7] = (int)request.dimension;
            // Sampler state is not in the request — it lives on the Texture base, and NowSdfImageField assigns
            // it right after every acquire from the temporary pool. Read it here so a freshly created target is
            // already sampling the way its handle says it does.
            m_TargetInfo[8] = (int)texture.filterMode;
            m_TargetInfo[9] = (int)texture.wrapModeU;
            m_TargetInfo[10] = (int)texture.wrapModeV;
            m_TargetInfo[11] = request.useMipMap ? 1 : 0;
            m_TargetInfo[12] = request.autoGenerateMips ? 1 : 0;

            // `readWrite` is deliberately not sent. The request always carries a RESOLVED value, and under Gamma
            // — which this build is, and which caps reports — Unity performs no sRGB conversion at all, so an
            // SRGB8_ALPHA8 attachment would linearise on read behind shaders that do not expect it
            // (M2-ShaderPort.md §6.3). When a linear slice lands, this is the field that selects the format.
            return Interop.CreateRenderTexture(texture.GetInstanceID(), m_TargetInfo) != 0;
        }

        /// <inheritdoc />
        /// <remarks>
        /// True when the GPU object is gone: the target was released, or the WebGL context died and took every
        /// object with it. <c>RenderTexture.IsCreated()</c> clears its own created flag on a true, which is the
        /// signal NowSdf's device-loss recovery (<c>NowSdf.cs:4731-4744</c>) waits for.
        /// </remarks>
        public bool IsRenderTextureLost(RenderTexture texture)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            // Deliberately not RequireInitialized: a target queried before or after the backend is usable has no
            // GPU object, and "lost" is the honest answer rather than an exception on a validity check.
            if (!m_Initialized)
                return true;

            return Interop.IsRenderTextureLost(texture.GetInstanceID()) != 0;
        }

        /// <inheritdoc />
        public void ReleaseRenderTexture(RenderTexture texture)
        {
            // Reachable on teardown and on a handle that was never created, so it must not throw. Idempotent on
            // the JavaScript side too.
            if (!m_Initialized || ReferenceEquals(texture, null))
                return;

            Interop.ReleaseRenderTexture(texture.GetInstanceID());
        }

        // ------------------------------------------------------------------------- blit and procedural draws

        /// <summary>
        /// The projection that maps the blit quad's unit square onto the whole target, built with the shim's own
        /// <c>Matrix4x4</c> so the arithmetic is Unity's.
        /// </summary>
        /// <remarks>
        /// <c>Ortho(0, 1, 0, 1, -1, 100)</c> sends uv <c>(0,0)</c> to NDC <c>(-1,-1)</c> and uv <c>(1,1)</c> to
        /// <c>(1,1)</c>: the source's first texel row lands on the destination's first texel row, which is
        /// Unity's mapping on OpenGL and the reason <c>UIGlassBlur</c>'s <c>#if UNITY_UV_STARTS_AT_TOP</c> flip
        /// is compiled out on this platform and must stay out.
        /// </remarks>
        static readonly Matrix4x4 BlitProjection = Matrix4x4.Ortho(0f, 1f, 0f, 1f, -1f, 100f);

        /// <inheritdoc />
        /// <remarks>
        /// <para>Serves all four <c>Graphics.Blit</c> overloads the shim declares, including the scale/offset
        /// form: scale and offset are written into the <c>_MainTex_ST</c> slot and applied by the blit vertex
        /// stage exactly as <c>TRANSFORM_TEX</c> does.</para>
        /// <para>Leaves the destination bound (invariant 7) <i>and</i> sets the viewport to the whole of it. The
        /// viewport is this method's job because the shim issues no <c>SetViewport</c> after a blit —
        /// <c>NowImmediate.AdoptBlitDestination</c> deliberately binds nothing — so invariant 5 does not cover
        /// it and Unity's own blit leaves the full destination viewport behind.</para>
        /// <para>Blending is off for the duration. Both shaders that reach a material blit are opaque
        /// (<c>Hidden/NowUI/GlassBlur</c> declares no <c>Blend</c>, <c>NowSdfImageField.shader</c> says
        /// <c>Blend Off</c>), and a composited copy would accumulate whatever a pooled target held before.</para>
        /// </remarks>
        public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                         in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice)
        {
            RequireInitialized();
            Trace("  Blit");

            // Matches NullRenderBackend: a material-only blit is Unity's "fill from uniforms alone" and is
            // legal; a blit with neither a source nor a material has nothing to read.
            if (ReferenceEquals(source, null) && ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(source), "A blit needs either a source texture or a material.");

            if (sourceDepthSlice != 0 || destinationDepthSlice != 0)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.Blit: depth slices ({sourceDepthSlice} -> {destinationDepthSlice}). Array " +
                    "render targets are not ported.");
            }

            if (!destination.isBackBuffer && destination.face != CubemapFace.Unknown)
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.Blit: cubemap face {destination.face}. Only 2D targets are allocatable.");
            }

            string shaderName = string.Empty;
            pass = NormalizePass(pass);

            if (!ReferenceEquals(material, null))
            {
                Shader shader = material.shader;

                if (ReferenceEquals(shader, null))
                    throw new InvalidOperationException($"WebGL2Backend.Blit: material '{material.name}' has no shader.");

                shaderName = shader.name;

                if (!IsPortedShader(shaderName))
                {
                    throw new NotSupportedException(
                        $"WebGL2Backend.Blit: shader '{shaderName}' is not ported. The blit machinery itself is " +
                        "live — target binding, the full-screen quad, the pass index, scale/offset — but the " +
                        "program is not. Note for whoever ports it: this backend's uniform bridge is the fixed " +
                        "slot map in UniformSlots, so a blit program's own uniforms need slots there and in " +
                        "nowui-gl.js's U before the pass can be fed. Hidden/NowUI/GlassBlur pass 0 is done and " +
                        "is the worked example: _NowBlurSourceTex, _NowBlurTexelSize, " +
                        "_NowBlurSourceScaleOffset and _NowBlurDirection, all resolved from " +
                        "NowRuntime.globals.");
                }

                if (!m_ResolvedShaders.Contains(shaderName) && !ResolveShader(shader))
                    throw new InvalidOperationException($"WebGL2Backend.Blit: '{shaderName}' failed to resolve.");

                // The material's own bag first, then the blit's own two slots on top of it: a blit's MVP is the
                // unit-quad ortho rather than anything SetViewProjection supplied, and its _MainTex_ST is the
                // scale/offset the caller passed rather than the material's texture transform.
                BuildUniformBlock(material, null, Matrix4x4.identity, shaderName);
            }
            else
            {
                Array.Clear(m_Uniforms, 0, m_Uniforms.Length);
            }

            WriteMatrix(m_Uniforms, UniformSlots.Mvp, BlitProjection);
            m_Uniforms[UniformSlots.MainTexST + 0] = scale.x;
            m_Uniforms[UniformSlots.MainTexST + 1] = scale.y;
            m_Uniforms[UniformSlots.MainTexST + 2] = offset.x;
            m_Uniforms[UniformSlots.MainTexST + 3] = offset.y;

            m_BlitInfo[0] = ReferenceEquals(source, null) ? NoTexture : source.GetInstanceID();
            m_BlitInfo[1] = destination.isBackBuffer ? BackBufferTarget : destination.texture.GetInstanceID();
            m_BlitInfo[2] = destination.mipLevel;
            m_BlitInfo[3] = destination.width;
            m_BlitInfo[4] = destination.height;
            m_BlitInfo[5] = ReferenceEquals(material, null) ? NoTexture : TextureId(material, null, IdTextureMask0);
            m_BlitInfo[6] = ReferenceEquals(material, null) ? NoTexture : TextureId(material, null, IdTextureMask1);
            m_BlitInfo[7] = pass;

            // Per-pass blit tally for Hidden/NowUI/SDF Image Field. It exists because the SDF image field is
            // the one ported program whose output NOTHING on screen shows directly: the page draws a
            // silhouette, and a silhouette is equally consistent with "five passes ran" and with "the bake
            // returned false and something else drew that". These five counters are the oracle a screenshot
            // cannot be, and ?area=sdf-image publishes them. Cheap enough to leave on: one compare and one
            // increment per blit, on a path that runs a few dozen times per bake and not at all otherwise.
            if (shaderName == SdfImageFieldShader && pass >= 0 && pass < s_SdfImageFieldPassBlits.Length)
                ++s_SdfImageFieldPassBlits[pass];

            // Hidden/NowUI/SDF Image Field's _SourceTex, the sprite being baked. It is a MATERIAL property, not
            // the blit source: `source` above is the previous ping-pong target on the flood passes and the
            // sprite on the seed pass, and the shader reads the two through different samplers. Resolved for
            // every material blit rather than only for this shader, because TextureId already answers
            // NoTexture for a material that does not declare it and the backend's fallback there is the 1x1
            // white this shader's Properties block also names.
            m_BlitInfo[8] = ReferenceEquals(material, null) ? NoTexture : TextureId(material, null, IdSourceTex);

            Interop.Blit(shaderName, m_BlitInfo, MemoryMarshal.AsBytes(new Span<float>(m_Uniforms)));
        }

        /// <inheritdoc />
        /// <remarks>
        /// A vertex-less draw: no vertex attributes are enabled, so the vertex stage has nothing but
        /// <c>gl_VertexID</c> — which is what the glass blur's full-screen passes are written against
        /// (<c>float2 positionUV = float2((vertexID &lt;&lt; 1) &amp; 2, vertexID &amp; 2)</c> over three
        /// vertices produces the standard oversized triangle). GLSL ES 3.00 spells <c>SV_VertexID</c>
        /// <c>gl_VertexID</c>, so a ported pass needs no other change.
        /// </remarks>
        public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology,
                                   int vertexCount, int instanceCount, MaterialPropertyBlock properties)
        {
            RequireInitialized();
            Trace("  DrawProcedural");

            if (ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(material));
            if (vertexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            if (instanceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(instanceCount));
            if (!m_ViewProjectionSet)
                throw new InvalidOperationException("WebGL2Backend.DrawProcedural: no SetViewProjection preceded this draw.");

            Shader shader = material.shader;

            if (ReferenceEquals(shader, null))
                throw new InvalidOperationException($"WebGL2Backend.DrawProcedural: material '{material.name}' has no shader.");

            string shaderName = shader.name;

            if (!IsPortedShader(shaderName))
            {
                throw new NotSupportedException(
                    $"WebGL2Backend.DrawProcedural: shader '{shaderName}' is not ported. The procedural draw " +
                    "path itself is live — vertex-less draw over gl_VertexID, pass selection, instancing — but " +
                    "the program is not, and its uniforms need slots in UniformSlots (see the note on Blit).");
            }

            pass = NormalizePass(pass);

            if (!m_ResolvedShaders.Contains(shaderName) && !ResolveShader(shader))
                throw new InvalidOperationException($"WebGL2Backend.DrawProcedural: '{shaderName}' failed to resolve.");

            if (vertexCount == 0 || instanceCount == 0)
                return;

            BuildUniformBlock(material, properties, model, shaderName);

            m_ProceduralInfo[0] = (int)topology;
            m_ProceduralInfo[1] = vertexCount;
            m_ProceduralInfo[2] = instanceCount;
            m_ProceduralInfo[3] = TextureId(material, properties, IdMainTex);
            m_ProceduralInfo[4] = TextureId(material, properties, IdTextureMask0);
            m_ProceduralInfo[5] = TextureId(material, properties, IdTextureMask1);
            m_ProceduralInfo[6] = pass;

            Interop.DrawProcedural(shaderName, m_ProceduralInfo, MemoryMarshal.AsBytes(new Span<float>(m_Uniforms)));
        }

        /// <inheritdoc />
        /// <remarks>
        /// Implemented as a framebuffer read: the source is attached to a scratch framebuffer and
        /// <c>copyTexSubImage2D</c> pulls it into the destination. WebGL2 has no <c>copyImageSubData</c>, so that
        /// is the only shader-less copy available, and it requires the source to be colour-renderable — which
        /// every format this backend allocates is. Sizes must match; Unity's <c>CopyTexture</c> requires that
        /// too. Nothing in NowUI calls it today, so this path is written from the contract rather than from a
        /// caller, and it is the one method here with no in-tree exercise.
        /// </remarks>
        public void CopyTexture(Texture source, Texture destination)
        {
            RequireInitialized();

            if (ReferenceEquals(source, null))
                throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(destination, null))
                throw new ArgumentNullException(nameof(destination));

            Interop.CopyTexture(source.GetInstanceID(), destination.GetInstanceID());
        }

        // ------------------------------------------------------------------------------------- helpers

        /// <summary>Collapses Unity's "pass -1 means every pass" to the single pass every ported program has.</summary>
        /// <remarks>
        /// Only correct while every ported program is single-pass, which is asserted rather than assumed: the
        /// module rejects a pass index past a program's declared count, so the day a multi-pass program lands,
        /// a -1 that should have meant "all four" fails loudly on pass 0 rather than drawing one quarter of the
        /// effect.
        /// </remarks>
        static int NormalizePass(int pass)
        {
            return pass < 0 ? 0 : pass;
        }

        void RequireInitialized()
        {
            if (!m_Initialized)
            {
                throw new InvalidOperationException(
                    "WebGL2Backend was used before CreateAsync completed. The JavaScript module has to be imported " +
                    "and the WebGL2 context created before any backend call.");
            }
        }

        /// <summary>
        /// The JavaScript side of the backend. One partial class so the interop surface is readable in one place,
        /// and so the boundary crossings can be counted by reading it.
        /// </summary>
        /// <remarks>
        /// Every argument is a primitive or a <see cref="Span{T}"/>. A span marshals as a memory view over WASM
        /// memory that is valid only for the duration of the call, which is what lets a whole mesh cross in one
        /// call without a managed-to-JS array copy on this side.
        /// </remarks>
        internal static partial class Interop
        {
            public const string ModuleName = "nowui-gl";

            [JSImport("init", ModuleName)]
            public static partial string Init(string canvasSelector);

            [JSImport("resolveShader", ModuleName)]
            public static partial int ResolveShader(string name);

            [JSImport("beginFrame", ModuleName)]
            public static partial void BeginFrame(int frameCount);

            [JSImport("endFrame", ModuleName)]
            public static partial void EndFrame();

            [JSImport("setViewport", ModuleName)]
            public static partial void SetViewport(int x, int y, int width, int height);

            [JSImport("clearTarget", ModuleName)]
            public static partial void ClearTarget(bool clearColor, float r, float g, float b, float a);

            [JSImport("uploadTexture", ModuleName)]
            public static partial void UploadTexture(
                int id,
                [JSMarshalAs<JSType.MemoryView>] Span<int> info,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);

            [JSImport("updateSampler", ModuleName)]
            public static partial void UpdateSampler(int id, [JSMarshalAs<JSType.MemoryView>] Span<int> info);

            [JSImport("releaseTexture", ModuleName)]
            public static partial void ReleaseTexture(int id);

            [JSImport("uploadMesh", ModuleName)]
            public static partial void UploadMesh(
                int id,
                int vertexCount,
                [JSMarshalAs<JSType.MemoryView>] Span<int> header,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> payload);

            [JSImport("releaseMesh", ModuleName)]
            public static partial void ReleaseMesh(int id);

            [JSImport("draw", ModuleName)]
            public static partial void Draw(
                int meshId,
                string shaderName,
                [JSMarshalAs<JSType.MemoryView>] Span<int> info,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> uniforms);

            /// <summary>
            /// Hands nowui-gl.js the SDF scene block for the draw that follows it.
            /// </summary>
            /// <remarks>
            /// A separate call rather than a second argument to <see cref="Draw"/> so that every non-SDF draw —
            /// which is nearly all of them — pays nothing for 7.9 KB of uniforms its program does not declare.
            /// The module treats the block as pending and consumes it in the next draw, so a missing call is an
            /// error naming the program rather than an empty scene.
            /// </remarks>
            [JSImport("setSdfUniforms", ModuleName)]
            public static partial void SetSdfUniforms([JSMarshalAs<JSType.MemoryView>] Span<byte> uniforms);

            [JSImport("createRenderTexture", ModuleName)]
            public static partial int CreateRenderTexture(int id, [JSMarshalAs<JSType.MemoryView>] Span<int> info);

            [JSImport("isRenderTextureLost", ModuleName)]
            public static partial int IsRenderTextureLost(int id);

            [JSImport("releaseRenderTexture", ModuleName)]
            public static partial void ReleaseRenderTexture(int id);

            [JSImport("setRenderTarget", ModuleName)]
            public static partial void SetRenderTarget(int id, int mipLevel, int depthSlice);

            [JSImport("blit", ModuleName)]
            public static partial void Blit(
                string shaderName,
                [JSMarshalAs<JSType.MemoryView>] Span<int> info,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> uniforms);

            [JSImport("drawProcedural", ModuleName)]
            public static partial void DrawProcedural(
                string shaderName,
                [JSMarshalAs<JSType.MemoryView>] Span<int> info,
                [JSMarshalAs<JSType.MemoryView>] Span<byte> uniforms);

            [JSImport("copyTexture", ModuleName)]
            public static partial void CopyTexture(int sourceId, int destinationId);

            [JSImport("getError", ModuleName)]
            public static partial int GetError();
        }
    }
}
