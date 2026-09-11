// The one immediate-mode render contract for the engine-free build: everything the shim can ask a GPU to do, and
// nothing else. Not a UnityEngine type — this is the seam UnityEngine.Graphics, UnityEngine.GL and
// UnityEngine.Rendering.CommandBuffer all funnel into.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.1 declares every type in this file and the eight invariants;
// §4.2/§4.3 are the shim-side callers; §4.7 is how the WebGL2 backend plugs in).
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// A resolved draw target. A null <see cref="texture"/> means the host's default framebuffer.
    /// </summary>
    /// <remarks>
    /// "Resolved" is the point: the shim owns the temporary-render-texture pool and the
    /// <c>RenderTargetIdentifier</c> vocabulary (name ids, builtin types, "the current target"), so a backend is
    /// handed a real <see cref="RenderTexture"/> or the back buffer and never has to look anything up (design §4.1
    /// invariant 4, §4.2 <c>NowImmediate.Resolve</c>).
    /// <para>
    /// Test for the back buffer with <c>ReferenceEquals(target.texture, null)</c>, never with <c>== null</c>: the
    /// UnityEngine fake-null operator reports true for a <i>destroyed</i> render texture, and a destroyed target is a
    /// bug to surface, not the back buffer.
    /// </para>
    /// </remarks>
    [Serializable]
    public readonly struct NowRenderTarget
    {
        /// <summary>The target texture, or null for the host's default framebuffer.</summary>
        public readonly RenderTexture texture;

        /// <summary>Mip level to render into. 0 in everything NowUI does.</summary>
        public readonly int mipLevel;

        /// <summary>Cubemap face, or <c>CubemapFace.Unknown</c> for a plain 2D target.</summary>
        public readonly CubemapFace face;

        /// <summary>Array slice, or <see cref="AllDepthSlices"/> (-1) for all slices.</summary>
        public readonly int depthSlice;

        /// <summary>Target width in pixels.</summary>
        public readonly int width;

        /// <summary>Target height in pixels.</summary>
        public readonly int height;

        /// <summary>The "every slice" <see cref="depthSlice"/> value, matching <c>RenderTargetIdentifier.AllDepthSlices</c>.</summary>
        public const int AllDepthSlices = -1;

        /// <summary>Builds a fully specified target.</summary>
        public NowRenderTarget(RenderTexture texture, int mipLevel, CubemapFace face, int depthSlice, int width, int height)
        {
            this.texture = texture;
            this.mipLevel = mipLevel;
            this.face = face;
            this.depthSlice = depthSlice;
            this.width = width;
            this.height = height;
        }

        /// <summary>The host's default framebuffer at the size <c>INowHostServices.screen</c> reports.</summary>
        public static NowRenderTarget BackBuffer(int width, int height)
        {
            return new NowRenderTarget(null, 0, CubemapFace.Unknown, AllDepthSlices, width, height);
        }

        /// <summary>
        /// True when this is the host's default framebuffer. Uses reference identity deliberately — see the type
        /// remarks.
        /// </summary>
        public bool isBackBuffer
        {
            get { return ReferenceEquals(texture, null); }
        }
    }

    /// <summary>
    /// Everything a backend needs to create a render texture, including the layout hints a WebGL2 backend may have to
    /// flatten (array slices, MSAA). A backend that flattens must report it through <see cref="NowRenderCaps"/>.
    /// </summary>
    /// <remarks>
    /// This is a flattened <c>RenderTextureDescriptor</c> rather than the descriptor itself, so that the backend
    /// contract does not depend on a UnityEngine type whose members Unity may reshape, and so a backend author reads
    /// exactly the fields that affect allocation (design §4.1, §4.7).
    /// </remarks>
    [Serializable]
    public readonly struct NowRenderTextureRequest
    {
        /// <summary>Width in pixels.</summary>
        public readonly int width;

        /// <summary>Height in pixels.</summary>
        public readonly int height;

        /// <summary>Depth/stencil bits: 0, 16, 24 or 32.</summary>
        public readonly int depthBits;

        /// <summary>Slice count for an array or 3D target; 1 for a plain 2D target.</summary>
        public readonly int volumeDepth;

        /// <summary>Mip levels to allocate; -1 means "the full chain".</summary>
        public readonly int mipCount;

        /// <summary>MSAA sample count; 1 means no multisampling.</summary>
        public readonly int msaaSamples;

        /// <summary>Colour format.</summary>
        public readonly RenderTextureFormat format;

        /// <summary>Whether sampling and writing convert sRGB.</summary>
        public readonly RenderTextureReadWrite readWrite;

        /// <summary>2D, 2D array, cube or 3D.</summary>
        public readonly TextureDimension dimension;

        /// <summary>Stereo layout. NowUI only ever asks for <c>VRTextureUsage.None</c>.</summary>
        public readonly VRTextureUsage vrUsage;

        /// <summary>Whether the multisampled surface itself must be sampleable.</summary>
        public readonly bool bindMS;

        /// <summary>Whether a mip chain is allocated.</summary>
        public readonly bool useMipMap;

        /// <summary>Whether mips regenerate after every render into the target.</summary>
        public readonly bool autoGenerateMips;

        /// <summary>Whether the target is bound as a UAV. Always false on WebGL2, which has no compute.</summary>
        public readonly bool enableRandomWrite;

        /// <summary>Builds a fully specified request.</summary>
        public NowRenderTextureRequest(
            int width,
            int height,
            int depthBits,
            int volumeDepth,
            int mipCount,
            int msaaSamples,
            RenderTextureFormat format,
            RenderTextureReadWrite readWrite,
            TextureDimension dimension,
            VRTextureUsage vrUsage,
            bool bindMS,
            bool useMipMap,
            bool autoGenerateMips,
            bool enableRandomWrite)
        {
            this.width = width;
            this.height = height;
            this.depthBits = depthBits;
            this.volumeDepth = volumeDepth;
            this.mipCount = mipCount;
            this.msaaSamples = msaaSamples;
            this.format = format;
            this.readWrite = readWrite;
            this.dimension = dimension;
            this.vrUsage = vrUsage;
            this.bindMS = bindMS;
            this.useMipMap = useMipMap;
            this.autoGenerateMips = autoGenerateMips;
            this.enableRandomWrite = enableRandomWrite;
        }

        /// <summary>A plain 2D colour target: no MSAA, no mips, no array slices.</summary>
        public NowRenderTextureRequest(int width, int height, int depthBits, RenderTextureFormat format, RenderTextureReadWrite readWrite)
            : this(width, height, depthBits, 1, 1, 1, format, readWrite, TextureDimension.Tex2D, VRTextureUsage.None,
                   false, false, false, false)
        {
        }
    }

    /// <summary>
    /// Encoded image container. Lives here because §4.1 declares it alongside the backend contract, but it is used by
    /// <c>INowImageDecoder</c> on the host services: image codecs and GL are different capabilities, and a headless
    /// host wants one without the other (design §4.4, §3.5 <c>ImageConversion</c>).
    /// </summary>
    public enum NowImageFormat
    {
        /// <summary>PNG. <c>Texture2D.EncodeToPNG</c>.</summary>
        Png = 0,

        /// <summary>JPEG. <c>Texture2D.EncodeToJPG</c>.</summary>
        Jpg = 1,
    }

    /// <summary>
    /// The GPU adapter. Thin by construction: the shim owns <b>all</b> CPU-side state — render targets, viewport,
    /// matrices, shader globals, the temporary-RT pool, material bags, mesh streams — so a backend holds no policy
    /// and makes no decisions the shim could have made for it (design §4, opening rule).
    /// </summary>
    /// <remarks>
    /// <para><b>The eight invariants the shim guarantees.</b> A backend may rely on all of them; the shim's own tests
    /// assert them (design §4.1, §7.5 "Immediate path"):</para>
    /// <list type="number">
    /// <item><description><c>Mesh.data.version</c>, <c>Texture.version</c> and <c>Material.version</c> change whenever
    /// CPU data changed, so a backend caches by <c>Object.GetInstanceID()</c> and re-uploads only on a version
    /// mismatch. <c>NowShaderGlobals.version</c> does the same for the globals bag.</description></item>
    /// <item><description><see cref="Material"/> bags are fully resolved (property id → value) and
    /// <c>shader.info</c> names the program and its declared properties.</description></item>
    /// <item><description><c>NowRuntime.globals</c> holds the current global uniforms at draw time.</description></item>
    /// <item><description>Temporary render textures are ordinary <see cref="RenderTexture"/>s — a backend
    /// <b>never</b> sees a <c>nameID</c>, and there is exactly one temporary pool, on the shim side.</description></item>
    /// <item><description><see cref="SetRenderTarget"/> is always followed by <see cref="SetViewport"/>.</description></item>
    /// <item><description>Every draw is preceded by at least one <see cref="SetViewProjection"/>.</description></item>
    /// <item><description><see cref="Blit"/> leaves the destination bound (Unity's convention).</description></item>
    /// <item><description>All calls arrive on one thread, in order, between <see cref="BeginFrame"/> and
    /// <see cref="EndFrame"/>.</description></item>
    /// </list>
    /// <para>Handles are passed directly rather than snapshotted into a per-draw struct or copied through a
    /// dictionary: this is the hot path of a renderer whose entire point is 60 fps in a browser, and a per-draw copy
    /// would be the allocation the design forbids (§1.2).</para>
    /// <para>Image decode and encode are deliberately <b>not</b> here; they are <c>INowImageDecoder</c> on the host
    /// services (§4.4).</para>
    /// </remarks>
    public interface INowRenderBackend
    {
        /// <summary>What this backend can do. Constant for the life of the backend.</summary>
        NowRenderCaps caps { get; }

        // ------------------------------------------------------------------ frame boundaries (called by NowRuntime)

        /// <summary>Opens a frame. <paramref name="frameCount"/> is <c>Time.frameCount</c> for this frame.</summary>
        void BeginFrame(int frameCount);

        /// <summary>Closes the frame opened by <see cref="BeginFrame"/>. Present the framebuffer here if the host does not.</summary>
        void EndFrame();

        // ------------------------------------------- resource lifetime; all idempotent; identity is GetInstanceID()

        /// <summary>
        /// Uploads a texture's pixels. <paramref name="pixels"/> is the <i>full</i> CPU store, and
        /// <paramref name="dirtyRect"/> is the sub-rect that changed, in bottom-up texel space (row 0 = bottom, as
        /// Unity stores raw texture data).
        /// </summary>
        void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips);

        /// <summary>The texture's <c>filterMode</c>, <c>wrapMode</c> or <c>anisoLevel</c> changed.</summary>
        void UpdateSampler(Texture texture);

        /// <summary>Drops the GPU object for a texture. Idempotent.</summary>
        void ReleaseTexture(Texture texture);

        /// <summary>Allocates the GPU object for a render texture. Returns false when the request cannot be honoured.</summary>
        bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request);

        /// <summary>True once the GPU object is gone underneath us — after a WebGL context loss. Backs <c>RenderTexture.IsCreated</c>.</summary>
        bool IsRenderTextureLost(RenderTexture texture);

        /// <summary>Drops the GPU object for a render texture. Idempotent.</summary>
        void ReleaseRenderTexture(RenderTexture texture);

        /// <summary>Drops the GPU buffers for a mesh. Idempotent.</summary>
        void ReleaseMesh(Mesh mesh);

        /// <summary>Drops any cached uniform state for a material. Idempotent.</summary>
        void ReleaseMaterial(Material material);

        /// <summary>
        /// Looks a shader program up by <c>shader.name</c>. False means the program is unknown, which is what makes
        /// <c>Shader.Find</c> return null.
        /// </summary>
        bool ResolveShader(Shader shader);

        // ------------------------------- state (shared by CommandBuffer replay and the immediate GL/Graphics path)

        /// <summary>Binds a target. Always followed by <see cref="SetViewport"/> (invariant 5).</summary>
        void SetRenderTarget(in NowRenderTarget target);

        /// <summary>Sets the viewport in pixels of the bound target.</summary>
        void SetViewport(in Rect pixelRect);

        /// <summary>Sets the camera matrices. At least one of these precedes every draw (invariant 6).</summary>
        void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection);

        /// <summary>Clears the bound target.</summary>
        void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth);

        // ---- draws (mesh, material and block are read through the handles; globals through NowRuntime.globals) ----

        /// <summary>Draws one sub-mesh. <paramref name="properties"/> may be null.</summary>
        void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass, MaterialPropertyBlock properties);

        /// <summary>Draws vertices generated by the vertex shader. <paramref name="properties"/> may be null.</summary>
        void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology, int vertexCount,
                            int instanceCount, MaterialPropertyBlock properties);

        /// <summary>
        /// Copies <paramref name="source"/> into <paramref name="destination"/>, through
        /// <paramref name="material"/> when one is given and as a plain copy when it is null. Leaves the destination
        /// bound (invariant 7).
        /// </summary>
        void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                  in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice);

        /// <summary>GPU-side texture copy with no shader involved.</summary>
        void CopyTexture(Texture source, Texture destination);
    }
}
