// Mirrors the UnityEngine.Rendering enums for the NowUI standalone build: TextureDimension, IndexFormat,
// MeshUpdateFlags, VertexAttribute, VertexAttributeFormat, BuiltinRenderTextureType, GraphicsDeviceType,
// CompareFunction, StencilOp, ColorWriteMask, ShadowCastingMode, LightProbeUsage and ReflectionProbeUsage.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/Rendering.cs") and inventory §A.4;
// values verified against UnityEngine.CoreModule.dll from Unity 6000.4.0f1.
//
// These are the enums the mesh/command-buffer path serialises into backend calls, so the numbers are the contract
// between NowUI's core and any INowRenderBackend (design §4.1), not just a compile-time convenience.

using System;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// The shape of a texture resource (design §3.2, inventory §A.4 — NowWorldGraphic and the glass backdrops read
    /// <see cref="Tex2D"/> and <see cref="Tex2DArray"/>).
    /// </summary>
    /// <remarks>
    /// <c>Unknown</c> is -1 and <c>None</c> is 0, so <c>default(TextureDimension)</c> is <c>None</c> ("no texture"),
    /// not <c>Unknown</c> ("not yet determined") — the two mean different things.
    /// </remarks>
    public enum TextureDimension
    {
        Unknown = -1,
        None = 0,
        Any = 1,
        Tex2D = 2,
        Tex3D = 3,
        Cube = 4,
        Tex2DArray = 5,
        CubeArray = 6,
    }

    /// <summary>Index buffer element width (design §3.2, inventory §A.4 — Now/NowMesh use both).</summary>
    public enum IndexFormat
    {
        UInt16 = 0,
        UInt32 = 1,
    }

    /// <summary>
    /// Work a <c>Mesh.Set*</c> call is allowed to skip (design §3.2, inventory §A.4).
    /// </summary>
    /// <remarks>
    /// Every member is an opt-<em>out</em>, so <c>Default = 0</c> means "do all the validation and bookkeeping" and
    /// NowMesh passes <c>DontValidateIndices | DontNotifyMeshUsers | DontRecalculateBounds</c> (13) on its hot
    /// upload path to skip it. Unity 6000.4 also declares <c>DontValidateLodRanges = 16</c>, omitted here per the
    /// design §3.2 list and the §3 omission rule.
    /// </remarks>
    [Flags]
    public enum MeshUpdateFlags
    {
        Default = 0,
        DontValidateIndices = 1,
        DontResetBoneBounds = 2,
        DontNotifyMeshUsers = 4,
        DontRecalculateBounds = 8,
    }

    /// <summary>
    /// A vertex stream channel (design §3.2, inventory §A.4 — NowMesh's CanvasVertexLayout / RenderVertexLayout).
    /// </summary>
    /// <remarks>
    /// <c>TexCoord0</c>..<c>TexCoord7</c> are contiguous at 4..11, which is what lets a UV channel index be added
    /// straight onto <c>TexCoord0</c>.
    /// </remarks>
    public enum VertexAttribute
    {
        Position = 0,
        Normal = 1,
        Tangent = 2,
        Color = 3,
        TexCoord0 = 4,
        TexCoord1 = 5,
        TexCoord2 = 6,
        TexCoord3 = 7,
        TexCoord4 = 8,
        TexCoord5 = 9,
        TexCoord6 = 10,
        TexCoord7 = 11,
        BlendWeight = 12,
        BlendIndices = 13,
    }

    /// <summary>Element type of a vertex attribute (design §3.2 — NowMesh declares Float32 only).</summary>
    public enum VertexAttributeFormat
    {
        Float32 = 0,
        Float16 = 1,
        UNorm8 = 2,
        SNorm8 = 3,
        UNorm16 = 4,
        SNorm16 = 5,
        UInt8 = 6,
        SInt8 = 7,
        UInt16 = 8,
        SInt16 = 9,
        UInt32 = 10,
        SInt32 = 11,
    }

    /// <summary>
    /// A render target the engine owns, usable as a <c>RenderTargetIdentifier</c> (design §3.2, inventory §A.4 —
    /// NowRenderer / NowUniversalRendererFeature / NowWorldGlassBackdrop use <see cref="CameraTarget"/>).
    /// </summary>
    /// <remarks>
    /// Design §3.2 pins this to the six-member subset below; Unity declares many more (negative sentinels and the
    /// deferred G-buffer slots), omitted per the §3 omission rule so a later reference is a compile error.
    /// </remarks>
    public enum BuiltinRenderTextureType
    {
        None = 0,
        CurrentActive = 1,
        CameraTarget = 2,
        Depth = 3,
        DepthNormals = 4,
        ResolvedDepth = 5,
    }

    /// <summary>
    /// The graphics API in use, as reported by <c>SystemInfo.graphicsDeviceType</c> (design §3.2).
    /// </summary>
    /// <remarks>
    /// Design §3.2 pins this to the six APIs the standalone build can plausibly report; the rest of Unity's list
    /// (retired consoles and desktop APIs) is omitted per the §3 omission rule. Note that a WebGL2 host has no
    /// dedicated member here — Unity has no <c>OpenGLES3</c> alternative for the browser, and reports
    /// <see cref="OpenGLES3"/> for WebGL 2.0, which is what the M2 backend will do.
    /// </remarks>
    public enum GraphicsDeviceType
    {
        Direct3D11 = 2,
        Null = 4,
        OpenGLES3 = 11,
        Metal = 16,
        Direct3D12 = 18,
        Vulkan = 21,
    }

    /// <summary>
    /// Depth/stencil comparison (inventory §A.4 — NowWorldGraphic and NowGraphic use LessEqual, Always, Equal).
    /// </summary>
    /// <remarks><c>Disabled = 0</c> is not a comparison; it turns the test off entirely.</remarks>
    public enum CompareFunction
    {
        Disabled = 0,
        Never = 1,
        Less = 2,
        Equal = 3,
        LessEqual = 4,
        Greater = 5,
        NotEqual = 6,
        GreaterEqual = 7,
        Always = 8,
    }

    /// <summary>Stencil buffer write operation (inventory §A.4 — NowGraphic uses <see cref="Keep"/>).</summary>
    public enum StencilOp
    {
        Keep = 0,
        Zero = 1,
        Replace = 2,
        IncrementSaturate = 3,
        DecrementSaturate = 4,
        Invert = 5,
        IncrementWrap = 6,
        DecrementWrap = 7,
    }

    /// <summary>
    /// Which colour channels a draw writes (inventory §A.4 — NowGraphic uses <see cref="All"/>).
    /// </summary>
    /// <remarks>
    /// The bit order is <em>reversed</em> from the channel order you would expect: <c>Alpha</c> is bit 0 and
    /// <c>Red</c> is bit 3. That is Unity's, and it is why <c>All</c> is 15 rather than any RGBA-ordered constant.
    /// </remarks>
    [Flags]
    public enum ColorWriteMask
    {
        Alpha = 1,
        Blue = 2,
        Green = 4,
        Red = 8,
        All = 15,
    }

    /// <summary>Whether a renderer casts shadows (inventory §A.4 — host RenderParams in NowModelPreview).</summary>
    public enum ShadowCastingMode
    {
        Off = 0,
        On = 1,
        TwoSided = 2,
        ShadowsOnly = 3,
    }

    /// <summary>
    /// How a renderer samples light probes (inventory §A.4 — NowModelPreview sets <see cref="Off"/>).
    /// </summary>
    /// <remarks><c>3</c> is unused: <c>CustomProvided</c> is 4, not 3.</remarks>
    public enum LightProbeUsage
    {
        Off = 0,
        BlendProbes = 1,
        UseProxyVolume = 2,
        CustomProvided = 4,
    }

    /// <summary>How a renderer samples reflection probes (inventory §A.4 — NowModelPreview sets <see cref="Off"/>).</summary>
    public enum ReflectionProbeUsage
    {
        Off = 0,
        BlendProbes = 1,
        BlendProbesAndSkybox = 2,
        Simple = 3,
    }
}
