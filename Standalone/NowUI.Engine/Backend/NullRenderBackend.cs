// The default render backend: accepts every valid call, draws nothing, and counts. It is what NowRuntime installs
// before a host registers, so the shim is never in a "no backend" state and no shim file needs a null check.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.6 defines its caps, its counters, its validation and the
// opt-in draw ring; §4.1 is the contract it implements; §1.2 is the allocation rule the ring exists to respect).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// One entry of <see cref="NullRenderBackend"/>'s optional draw ring: enough to assert what was drawn, and small
    /// enough that 256 of them can be pre-allocated once.
    /// </summary>
    [Serializable]
    public struct NowDrawRecord
    {
        /// <summary>Instance id of the mesh, or 0 for a procedural draw.</summary>
        public int meshId;

        /// <summary>Sub-mesh index, or the vertex count for a procedural draw.</summary>
        public int subMesh;

        /// <summary>Instance id of the material.</summary>
        public int materialId;

        /// <summary>Shader pass.</summary>
        public int pass;

        /// <summary>Instance id of the bound render texture, or 0 for the back buffer.</summary>
        public int targetId;

        /// <summary>The model matrix the draw was issued with.</summary>
        public Matrix4x4 model;
    }

    /// <summary>
    /// A backend that validates and counts but never touches a GPU. Three jobs: it is the default so
    /// <c>NowRuntime.backend</c> is never null (design §4.5); it lets the whole shim and the whole core run headless
    /// in tests; and it catches NowUI-side misuse that a real backend would only turn into a driver error.
    /// </summary>
    /// <remarks>
    /// <para>It <b>never throws on valid input</b>, but it <b>does</b> validate the way a real backend would — a null
    /// mesh or material is <see cref="ArgumentNullException"/>, a sub-mesh out of range is
    /// <see cref="ArgumentOutOfRangeException"/> — because a silent no-op backend would let a NowUI bug reach M2
    /// undetected (design §4.6).</para>
    /// <para><b>It allocates nothing per draw.</b> The counters are fields, the created-target set only grows when a
    /// new render texture is created, and the draw ring is off (<see cref="recordDraws"/> defaults to false) and is
    /// allocated once, on the transition to true, not per record. That is what lets §7.5's steady-state frame case
    /// assert 0 bytes with <c>GC.GetAllocatedBytesForCurrentThread</c>.</para>
    /// </remarks>
    public sealed class NullRenderBackend : INowRenderBackend
    {
        /// <summary>Entries the draw ring holds before it wraps (design §4.6).</summary>
        public const int DrawRingCapacity = 256;

        // Created render targets, by instance id. This is what keeps NowSdf's and NowEffects' targets valid: a
        // RenderTexture that was Create()d reports IsCreated() true, and one that was released or never created
        // reports "lost", exactly as a real backend after a context loss.
        private readonly HashSet<int> m_CreatedRenderTextures = new HashSet<int>();

        private NowDrawRecord[] m_DrawRing;
        private int m_DrawRingCount;
        private int m_DrawRingNext;
        private bool m_RecordDraws;

        // ------------------------------------------------------------------------------------------------ counters

        /// <summary>Frames opened.</summary>
        public int frameBegins { get; private set; }

        /// <summary>Frames closed.</summary>
        public int frameEnds { get; private set; }

        /// <summary>The frame number the last <see cref="BeginFrame"/> was given.</summary>
        public int lastFrameCount { get; private set; }

        /// <summary>Calls to <see cref="UploadTexture2D"/>.</summary>
        public int textureUploads { get; private set; }

        /// <summary>Bytes the last upload was handed (the size of the full CPU store, not of the dirty rect).</summary>
        public int lastUploadByteCount { get; private set; }

        /// <summary>The dirty rect of the last upload, in bottom-up texel space.</summary>
        public RectInt lastDirtyRect { get; private set; }

        /// <summary>Calls to <see cref="UpdateSampler"/>.</summary>
        public int samplerUpdates { get; private set; }

        /// <summary>Calls to <see cref="ReleaseTexture"/>.</summary>
        public int textureReleases { get; private set; }

        /// <summary>Calls to <see cref="CreateRenderTexture"/>.</summary>
        public int renderTextureCreates { get; private set; }

        /// <summary>Calls to <see cref="ReleaseRenderTexture"/>.</summary>
        public int renderTextureReleases { get; private set; }

        /// <summary>Render textures currently holding a GPU object.</summary>
        public int liveRenderTextures
        {
            get { return m_CreatedRenderTextures.Count; }
        }

        /// <summary>Calls to <see cref="ReleaseMesh"/>.</summary>
        public int meshReleases { get; private set; }

        /// <summary>Calls to <see cref="ReleaseMaterial"/>.</summary>
        public int materialReleases { get; private set; }

        /// <summary>Calls to <see cref="ResolveShader"/>.</summary>
        public int shaderResolves { get; private set; }

        /// <summary>Calls to <see cref="SetRenderTarget"/>.</summary>
        public int renderTargetSets { get; private set; }

        /// <summary>Calls to <see cref="SetViewport"/>.</summary>
        public int viewportSets { get; private set; }

        /// <summary>Calls to <see cref="SetViewProjection"/>.</summary>
        public int viewProjectionSets { get; private set; }

        /// <summary>Calls to <see cref="ClearRenderTarget"/>.</summary>
        public int clears { get; private set; }

        /// <summary>Calls to <see cref="DrawMesh"/>.</summary>
        public int meshDraws { get; private set; }

        /// <summary>Calls to <see cref="DrawProcedural"/>.</summary>
        public int proceduralDraws { get; private set; }

        /// <summary>Calls to <see cref="Blit"/>.</summary>
        public int blits { get; private set; }

        /// <summary>Calls to <see cref="CopyTexture"/>.</summary>
        public int textureCopies { get; private set; }

        /// <summary>The target bound by the last <see cref="SetRenderTarget"/>.</summary>
        public NowRenderTarget lastRenderTarget { get; private set; }

        /// <summary>The rect passed to the last <see cref="SetViewport"/>.</summary>
        public Rect lastViewport { get; private set; }

        /// <summary>The view matrix of the last <see cref="SetViewProjection"/>.</summary>
        public Matrix4x4 lastView { get; private set; }

        /// <summary>The projection matrix of the last <see cref="SetViewProjection"/>.</summary>
        public Matrix4x4 lastProjection { get; private set; }

        /// <summary>The colour of the last <see cref="ClearRenderTarget"/>.</summary>
        public Color lastClearColor { get; private set; }

        /// <summary>The depth value of the last <see cref="ClearRenderTarget"/>.</summary>
        public float lastClearDepth { get; private set; }

        // ----------------------------------------------------------------------------------------------- draw ring

        /// <summary>
        /// Whether draws are recorded into the ring. <b>False by default</b>: recording is a test affordance, and the
        /// steady-state allocation gate runs with it off (design §4.6).
        /// </summary>
        /// <remarks>
        /// The ring is allocated on the first transition to true and then reused, so turning it on costs one
        /// allocation ever rather than one per draw.
        /// </remarks>
        public bool recordDraws
        {
            get { return m_RecordDraws; }
            set
            {
                if (value && m_DrawRing == null)
                    m_DrawRing = new NowDrawRecord[DrawRingCapacity];

                m_RecordDraws = value;
            }
        }

        /// <summary>
        /// Draws currently held in the ring: at most <see cref="DrawRingCapacity"/>, and 0 while
        /// <see cref="recordDraws"/> has never been on.
        /// </summary>
        public int recordedDrawCount
        {
            get { return m_DrawRingCount; }
        }

        /// <summary>
        /// One recorded draw, oldest first. Index 0 is the oldest draw the ring still holds, which after a wrap is
        /// not the first draw ever issued.
        /// </summary>
        public NowDrawRecord GetDrawRecord(int index)
        {
            if (index < 0 || index >= m_DrawRingCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            // m_DrawRingNext is where the *next* record goes; once the ring is full that slot also holds the oldest.
            int start = m_DrawRingCount == DrawRingCapacity ? m_DrawRingNext : 0;
            return m_DrawRing[(start + index) % DrawRingCapacity];
        }

        // ---------------------------------------------------------------------------------------------------- caps

        /// <summary>
        /// A permissive device: 16384 texels, no MSAA, texture arrays and instancing, every format, bottom-up
        /// targets, and whatever colour space the runtime is configured for (design §4.6).
        /// </summary>
        /// <remarks>
        /// <c>colorSpace</c> is read from <c>NowRuntime.colorSpace</c> on every access rather than latched in a
        /// constructor: §4.1 requires <c>SystemInfo</c> and <c>QualitySettings</c> to agree with the backend, and a
        /// test that sets the runtime's colour space after installing the backend must not be able to break that
        /// agreement. Building the struct per access costs no allocation — it is a struct with one interned string.
        /// </remarks>
        public NowRenderCaps caps
        {
            get
            {
                return NowRenderCaps.CreateAllFormats(
                    maxTextureSize: 16384,
                    maxMsaaSamples: 1,
                    supportsMultisampledTextures: false,
                    supportsTextureArrays: true,
                    supportsInstancing: true,
                    renderTargetsAreBottomUp: true,
                    colorSpace: NowRuntime.colorSpace,
                    deviceName: "Null",
                    deviceType: GraphicsDeviceType.Null,
                    graphicsMemorySizeMb: 0);
            }
        }

        // ---------------------------------------------------------------------------------------- frame boundaries

        /// <inheritdoc/>
        public void BeginFrame(int frameCount)
        {
            frameBegins++;
            lastFrameCount = frameCount;
        }

        /// <inheritdoc/>
        public void EndFrame()
        {
            frameEnds++;
        }

        // --------------------------------------------------------------------------------------- resource lifetime

        /// <inheritdoc/>
        public void UploadTexture2D(Texture2D texture, ReadOnlySpan<byte> pixels, RectInt dirtyRect, bool generateMips)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            textureUploads++;
            lastUploadByteCount = pixels.Length;
            lastDirtyRect = dirtyRect;
        }

        /// <inheritdoc/>
        public void UpdateSampler(Texture texture)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            samplerUpdates++;
        }

        /// <inheritdoc/>
        public void ReleaseTexture(Texture texture)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            textureReleases++;
        }

        /// <inheritdoc/>
        public bool CreateRenderTexture(RenderTexture texture, in NowRenderTextureRequest request)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            renderTextureCreates++;
            m_CreatedRenderTextures.Add(texture.GetInstanceID());
            return true;
        }

        /// <inheritdoc/>
        public bool IsRenderTextureLost(RenderTexture texture)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            // "Lost" for anything this backend has no GPU object for, which covers both "never created" and
            // "released". RenderTexture.IsCreated() ands this with its own created flag, so the answer for a
            // never-created target is unobservable through the shim and honest here.
            return !m_CreatedRenderTextures.Contains(texture.GetInstanceID());
        }

        /// <inheritdoc/>
        public void ReleaseRenderTexture(RenderTexture texture)
        {
            if (ReferenceEquals(texture, null))
                throw new ArgumentNullException(nameof(texture));

            renderTextureReleases++;
            m_CreatedRenderTextures.Remove(texture.GetInstanceID());
        }

        /// <inheritdoc/>
        public void ReleaseMesh(Mesh mesh)
        {
            if (ReferenceEquals(mesh, null))
                throw new ArgumentNullException(nameof(mesh));

            meshReleases++;
        }

        /// <inheritdoc/>
        public void ReleaseMaterial(Material material)
        {
            if (ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(material));

            materialReleases++;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// True for every shader, because the only shaders that exist standalone are the ones the host's resource
        /// provider minted; there is no asset database that could hand out a program this backend has not seen
        /// (design §4.6).
        /// </remarks>
        public bool ResolveShader(Shader shader)
        {
            if (ReferenceEquals(shader, null))
                throw new ArgumentNullException(nameof(shader));

            shaderResolves++;
            return true;
        }

        // -------------------------------------------------------------------------------------------------- state

        /// <inheritdoc/>
        public void SetRenderTarget(in NowRenderTarget target)
        {
            renderTargetSets++;
            lastRenderTarget = target;
        }

        /// <inheritdoc/>
        public void SetViewport(in Rect pixelRect)
        {
            viewportSets++;
            lastViewport = pixelRect;
        }

        /// <inheritdoc/>
        public void SetViewProjection(in Matrix4x4 view, in Matrix4x4 projection)
        {
            viewProjectionSets++;
            lastView = view;
            lastProjection = projection;
        }

        /// <inheritdoc/>
        public void ClearRenderTarget(bool clearDepth, bool clearColor, in Color color, float depth)
        {
            clears++;
            lastClearColor = color;
            lastClearDepth = depth;
        }

        // -------------------------------------------------------------------------------------------------- draws

        /// <inheritdoc/>
        public void DrawMesh(Mesh mesh, int subMesh, in Matrix4x4 model, Material material, int pass, MaterialPropertyBlock properties)
        {
            if (ReferenceEquals(mesh, null))
                throw new ArgumentNullException(nameof(mesh));
            if (ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(material));
            if (subMesh < 0 || subMesh >= mesh.subMeshCount)
                throw new ArgumentOutOfRangeException(nameof(subMesh));

            meshDraws++;
            RecordDraw(mesh.GetInstanceID(), subMesh, material.GetInstanceID(), pass, in model);
        }

        /// <inheritdoc/>
        public void DrawProcedural(in Matrix4x4 model, Material material, int pass, MeshTopology topology, int vertexCount,
                                   int instanceCount, MaterialPropertyBlock properties)
        {
            if (ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(material));
            if (vertexCount < 0)
                throw new ArgumentOutOfRangeException(nameof(vertexCount));
            if (instanceCount < 0)
                throw new ArgumentOutOfRangeException(nameof(instanceCount));

            proceduralDraws++;
            // meshId 0 marks "procedural"; the vertex count takes the sub-mesh slot, which is the only number that
            // identifies what was drawn.
            RecordDraw(0, vertexCount, material.GetInstanceID(), pass, in model);
        }

        /// <inheritdoc/>
        /// <remarks>
        /// A null <paramref name="source"/> is allowed when a <paramref name="material"/> is given: that is Unity's
        /// material-only blit, which NowUI's effect passes use to fill a target from uniforms alone. A blit with
        /// neither a source nor a material has nothing to read, so it is rejected.
        /// </remarks>
        public void Blit(Texture source, in NowRenderTarget destination, Material material, int pass,
                         in Vector2 scale, in Vector2 offset, int sourceDepthSlice, int destinationDepthSlice)
        {
            if (ReferenceEquals(source, null) && ReferenceEquals(material, null))
                throw new ArgumentNullException(nameof(source), "A blit needs either a source texture or a material.");

            blits++;

            // Invariant 7: the destination stays bound afterwards, so a caller reading lastRenderTarget sees what a
            // real backend would have left bound. renderTargetSets is deliberately NOT bumped - it counts calls the
            // shim made to SetRenderTarget, which is what the "SetRenderTarget is followed by SetViewport" invariant
            // is asserted against, and a blit's implicit bind is not one of them.
            lastRenderTarget = destination;
        }

        /// <inheritdoc/>
        public void CopyTexture(Texture source, Texture destination)
        {
            if (ReferenceEquals(source, null))
                throw new ArgumentNullException(nameof(source));
            if (ReferenceEquals(destination, null))
                throw new ArgumentNullException(nameof(destination));

            textureCopies++;
        }

        // ------------------------------------------------------------------------------------------------- resets

        /// <summary>
        /// Clears every counter and the draw ring, and forgets which render textures were created.
        /// </summary>
        /// <remarks>
        /// Forgetting the created set is deliberate: <c>NowRuntime.ResetAll</c> is the standalone domain reload, and
        /// after it every GPU object is gone, so a render texture that survived the reset must report itself lost and
        /// re-create. Keeps <c>recordDraws</c> and the ring's storage, so a fixture can reset between cases without
        /// re-enabling recording or re-allocating.
        /// </remarks>
        public void Reset()
        {
            frameBegins = 0;
            frameEnds = 0;
            lastFrameCount = 0;
            textureUploads = 0;
            lastUploadByteCount = 0;
            lastDirtyRect = default;
            samplerUpdates = 0;
            textureReleases = 0;
            renderTextureCreates = 0;
            renderTextureReleases = 0;
            meshReleases = 0;
            materialReleases = 0;
            shaderResolves = 0;
            renderTargetSets = 0;
            viewportSets = 0;
            viewProjectionSets = 0;
            clears = 0;
            meshDraws = 0;
            proceduralDraws = 0;
            blits = 0;
            textureCopies = 0;
            lastRenderTarget = default;
            lastViewport = default;
            lastView = default;
            lastProjection = default;
            lastClearColor = default;
            lastClearDepth = 0f;

            m_CreatedRenderTextures.Clear();
            m_DrawRingCount = 0;
            m_DrawRingNext = 0;
        }

        private void RecordDraw(int meshId, int subMesh, int materialId, int pass, in Matrix4x4 model)
        {
            if (!m_RecordDraws)
                return;

            NowDrawRecord[] ring = m_DrawRing;
            int slot = m_DrawRingNext;

            // Written field by field through the array element rather than through a local struct: a local would be
            // copied twice, and this is the only work the ring does on the draw path.
            ring[slot].meshId = meshId;
            ring[slot].subMesh = subMesh;
            ring[slot].materialId = materialId;
            ring[slot].pass = pass;
            ring[slot].targetId = ReferenceEquals(lastRenderTarget.texture, null)
                ? 0
                : lastRenderTarget.texture.GetInstanceID();
            ring[slot].model = model;

            m_DrawRingNext = slot + 1 == DrawRingCapacity ? 0 : slot + 1;
            if (m_DrawRingCount < DrawRingCapacity)
                m_DrawRingCount++;
        }
    }
}
