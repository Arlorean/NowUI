// Mirrors UnityEngine.RenderTexture for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`RenderTexture.cs` member list and the temporary-pool rules),
// §4.1 (INowRenderBackend.CreateRenderTexture / IsRenderTextureLost / ReleaseRenderTexture, NowRenderTextureRequest),
// §4.2 (NowImmediate owns the active target).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.RenderTexture`; hazard D.1 #11
// (IsCreated is NowSdf's device-loss signal, NowSdf.cs:4731-4744).
//
// Behaviours that look odd and are deliberate:
//   * The descriptor is the single source of truth. `width`, `depth`, `format`, `antiAliasing` and friends are views
//     onto it, so a target that has to be re-created after a WebGL context loss can be rebuilt from the descriptor
//     alone with nothing else to keep in sync.
//   * A setter releases the target only when the value actually CHANGES. NowSdfImageField assigns `antiAliasing`,
//     `useMipMap` and `autoGenerateMips` on every acquire (NowSdfImageField.cs:446-451), and those acquires come
//     from the temporary pool, whose key already contains all three - so on the steady-state path every one of those
//     assignments is a no-op and the pooled target survives.
//   * `IsCreated()` clears the created flag when the backend reports the target lost. Without that, the very next
//     `Create()` would see `created == true` and hand NowSdf's device-loss recovery path (NowSdf.cs:4731-4744) a
//     target that does not exist on the GPU.
using System;
using NowUI.Engine;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// A GPU render target. Unlike <see cref="Texture2D"/> it has no CPU store: everything about it lives in its
    /// <see cref="descriptor"/> until <see cref="Create"/> asks the backend for the real resource.
    /// </summary>
    public sealed class RenderTexture : Texture
    {
        private RenderTextureDescriptor m_Descriptor;
        private bool m_Created;

        /// <summary>A default-format colour target with <paramref name="depth"/> depth bits.</summary>
        public RenderTexture(int width, int height, int depth)
            : this(width, height, depth, RenderTextureFormat.Default, RenderTextureReadWrite.Default)
        {
        }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format)
            : this(width, height, depth, format, RenderTextureReadWrite.Default)
        {
        }

        public RenderTexture(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            var descriptor = new RenderTextureDescriptor(width, height, format, depth)
            {
                sRGB = ResolveSrgb(readWrite),
            };

            Adopt(descriptor);
        }

        /// <summary>Builds a target from a descriptor. Nothing is allocated on the GPU until <see cref="Create"/>.</summary>
        public RenderTexture(RenderTextureDescriptor desc)
        {
            Adopt(desc);
        }

        /// <summary>
        /// Copies another target's descriptor and sampler state. Unity copies the settings, not the contents, and
        /// does not create the copy.
        /// </summary>
        public RenderTexture(RenderTexture textureToCopy)
        {
            if (ReferenceEquals(textureToCopy, null))
                throw new ArgumentNullException(nameof(textureToCopy));

            Adopt(textureToCopy.m_Descriptor);

            filterMode = textureToCopy.filterMode;
            wrapModeU = textureToCopy.wrapModeU;
            wrapModeV = textureToCopy.wrapModeV;
            wrapModeW = textureToCopy.wrapModeW;
            anisoLevel = textureToCopy.anisoLevel;
        }

        /// <summary>Texel width. Changing it releases the target so the next <see cref="Create"/> rebuilds it.</summary>
        public override int width
        {
            get { return m_Descriptor.width; }
            set
            {
                if (m_Descriptor.width == value)
                    return;

                m_Descriptor.width = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Texel height. Changing it releases the target so the next <see cref="Create"/> rebuilds it.</summary>
        public override int height
        {
            get { return m_Descriptor.height; }
            set
            {
                if (m_Descriptor.height == value)
                    return;

                m_Descriptor.height = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Depth/stencil bits: 0, 16 or 24.</summary>
        public int depth
        {
            get { return m_Descriptor.depthBufferBits; }
            set
            {
                if (m_Descriptor.depthBufferBits == value)
                    return;

                m_Descriptor.depthBufferBits = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Colour format.</summary>
        public RenderTextureFormat format
        {
            get { return m_Descriptor.colorFormat; }
            set
            {
                if (m_Descriptor.colorFormat == value)
                    return;

                m_Descriptor.colorFormat = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>
        /// The full creation record. Assigning one replaces every setting at once, which is how a host restores a
        /// target after a context loss.
        /// </summary>
        public RenderTextureDescriptor descriptor
        {
            get { return m_Descriptor; }
            set
            {
                m_Descriptor = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Shape of the target. Assignable here, unlike on a plain <see cref="Texture"/>.</summary>
        public override TextureDimension dimension
        {
            get { return m_Descriptor.dimension; }
            set
            {
                if (m_Descriptor.dimension == value)
                    return;

                m_Descriptor.dimension = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Slice count for an array or 3D target.</summary>
        public int volumeDepth
        {
            get { return m_Descriptor.volumeDepth; }
            set
            {
                if (m_Descriptor.volumeDepth == value)
                    return;

                m_Descriptor.volumeDepth = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Stereo layout. NowUI only ever asks for <see cref="VRTextureUsage.None"/>.</summary>
        public VRTextureUsage vrUsage
        {
            get { return m_Descriptor.vrUsage; }
            set
            {
                if (m_Descriptor.vrUsage == value)
                    return;

                m_Descriptor.vrUsage = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>MSAA sample count; 1 means no multisampling.</summary>
        public int antiAliasing
        {
            get { return m_Descriptor.msaaSamples; }
            set
            {
                if (m_Descriptor.msaaSamples == value)
                    return;

                m_Descriptor.msaaSamples = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Whether a multisampled target is sampled without a resolve.</summary>
        public bool bindTextureMS
        {
            get { return m_Descriptor.bindMS; }
            set
            {
                if (m_Descriptor.bindMS == value)
                    return;

                m_Descriptor.bindMS = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Whether the target carries a mip chain.</summary>
        public bool useMipMap
        {
            get { return m_Descriptor.useMipMap; }
            set
            {
                if (m_Descriptor.useMipMap == value)
                    return;

                m_Descriptor.useMipMap = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Whether mips are regenerated after every render into the target.</summary>
        public bool autoGenerateMips
        {
            get { return m_Descriptor.autoGenerateMips; }
            set
            {
                if (m_Descriptor.autoGenerateMips == value)
                    return;

                m_Descriptor.autoGenerateMips = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>Whether the target is usable as a compute UAV.</summary>
        public bool enableRandomWrite
        {
            get { return m_Descriptor.enableRandomWrite; }
            set
            {
                if (m_Descriptor.enableRandomWrite == value)
                    return;

                m_Descriptor.enableRandomWrite = value;
                OnDescriptorChanged();
            }
        }

        /// <summary>
        /// The target currently bound for drawing, or null for the host's back buffer. Routed through
        /// <c>NowImmediate</c> because binding a target is backend state, not a field: the setter has to reach the
        /// backend and set the viewport with it (design §4.2, backend guarantee 5).
        /// </summary>
        public static RenderTexture active
        {
            get { return NowImmediate.activeTarget.texture; }
            set { NowImmediate.SetActive(value); }
        }

        /// <summary>
        /// Asks the backend for the GPU resource. Idempotent: an already-created target returns true without
        /// touching the backend, which is what makes it safe on NowSdf's per-frame validity check.
        /// </summary>
        public bool Create()
        {
            if (m_Created)
                return true;

            if (m_Descriptor.width <= 0 || m_Descriptor.height <= 0)
                return false;

            INowRenderBackend backend = NowRuntime.backend;

            if (backend == null)
                return false;

            NowRenderTextureRequest request = BuildRequest();

            if (!backend.CreateRenderTexture(this, in request))
                return false;

            m_Created = true;
            SetMemoryFootprint(EstimateBytes());
            return true;
        }

        /// <summary>
        /// Whether the GPU resource exists right now. A backend that reports the target lost (WebGL context loss)
        /// also clears the created flag here, so the caller's recovery <see cref="Create"/> really does rebuild.
        /// </summary>
        public bool IsCreated()
        {
            if (!m_Created)
                return false;

            INowRenderBackend backend = NowRuntime.backend;

            if (backend != null && backend.IsRenderTextureLost(this))
            {
                m_Created = false;
                SetMemoryFootprint(0);
                return false;
            }

            return true;
        }

        /// <summary>Frees the GPU resource. The descriptor is kept, so <see cref="Create"/> can rebuild it.</summary>
        public void Release()
        {
            if (!m_Created)
                return;

            m_Created = false;
            SetMemoryFootprint(0);

            INowRenderBackend backend = NowRuntime.backend;

            if (backend != null)
                backend.ReleaseRenderTexture(this);
        }

        /// <summary>
        /// Declares the current contents expendable. A no-op on the shim side: it is a bandwidth hint, and a backend
        /// that wants it can implement the equivalent invalidate in its own <c>SetRenderTarget</c>.
        /// </summary>
        public void DiscardContents()
        {
        }

        /// <summary>Declares part of the current contents expendable. A no-op, as <see cref="DiscardContents()"/> is.</summary>
        public void DiscardContents(bool discardColor, bool discardDepth)
        {
        }

        /// <summary>Tells the driver the contents will be restored. A no-op on the shim side.</summary>
        public void MarkRestoreExpected()
        {
        }

        /// <summary>A pooled target. Filter and wrap mode are NOT part of the pool key - see the pool's own header.</summary>
        public static RenderTexture GetTemporary(int width, int height)
        {
            return GetTemporary(width, height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);
        }

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer)
        {
            return GetTemporary(width, height, depthBuffer, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1);
        }

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format)
        {
            return GetTemporary(width, height, depthBuffer, format, RenderTextureReadWrite.Default, 1);
        }

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            return GetTemporary(width, height, depthBuffer, format, readWrite, 1);
        }

        public static RenderTexture GetTemporary(int width, int height, int depthBuffer, RenderTextureFormat format, RenderTextureReadWrite readWrite, int antiAliasing)
        {
            var descriptor = new RenderTextureDescriptor(width, height, format, depthBuffer)
            {
                sRGB = ResolveSrgb(readWrite),
                msaaSamples = antiAliasing < 1 ? 1 : antiAliasing,
            };

            return NowTemporaryRenderTexturePool.Get(descriptor);
        }

        /// <summary>A pooled target matching <paramref name="desc"/>.</summary>
        public static RenderTexture GetTemporary(RenderTextureDescriptor desc)
        {
            return NowTemporaryRenderTexturePool.Get(desc);
        }

        /// <summary>Returns a pooled target. Keeping it created is the point: the next matching Get reuses it.</summary>
        public static void ReleaseTemporary(RenderTexture temp)
        {
            NowTemporaryRenderTexturePool.Release(temp);
        }

        /// <summary>The creation record a backend needs. Built fresh per call; a struct, so this allocates nothing.</summary>
        internal NowRenderTextureRequest BuildRequest()
        {
            return new NowRenderTextureRequest(
                m_Descriptor.width,
                m_Descriptor.height,
                m_Descriptor.depthBufferBits,
                m_Descriptor.volumeDepth < 1 ? 1 : m_Descriptor.volumeDepth,
                m_Descriptor.mipCount,
                m_Descriptor.msaaSamples < 1 ? 1 : m_Descriptor.msaaSamples,
                m_Descriptor.colorFormat,
                // The descriptor stores the RESOLVED colour handling, so a request never carries `Default`: a backend
                // must never have to re-derive the project's colour space to know what it is being asked for.
                m_Descriptor.sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear,
                m_Descriptor.dimension,
                m_Descriptor.vrUsage,
                m_Descriptor.bindMS,
                m_Descriptor.useMipMap,
                m_Descriptor.autoGenerateMips,
                m_Descriptor.enableRandomWrite);
        }

        /// <summary>Releases the GPU resource when the handle is destroyed (design §3.5).</summary>
        internal override void OnDestroyResources()
        {
            Release();
            base.OnDestroyResources();
        }

        /// <summary>
        /// Resolves a <see cref="RenderTextureReadWrite"/> against the runtime's colour space, which is what Unity's
        /// `Default` means: in linear rendering a default target is sRGB, in gamma rendering it is not.
        /// </summary>
        internal static bool ResolveSrgb(RenderTextureReadWrite readWrite)
        {
            switch (readWrite)
            {
                case RenderTextureReadWrite.sRGB:
                    return true;

                case RenderTextureReadWrite.Linear:
                    return false;

                default:
                    return NowRuntime.colorSpace == ColorSpace.Linear;
            }
        }

        /// <summary>Approximate bytes for the memory counter; colour only, mips and MSAA included.</summary>
        internal long EstimateBytes()
        {
            long slices = m_Descriptor.volumeDepth < 1 ? 1 : m_Descriptor.volumeDepth;
            long samples = m_Descriptor.msaaSamples < 1 ? 1 : m_Descriptor.msaaSamples;
            long bytes = (long)m_Descriptor.width * m_Descriptor.height * BytesPerPixel(m_Descriptor.colorFormat) * slices * samples;

            // A full chain adds a third again, which is the classic sum of the 1/4 series.
            if (m_Descriptor.useMipMap)
                bytes += bytes / 3;

            return bytes;
        }

        /// <summary>Bytes per texel of a render-target format; 4 for anything the shim has no better number for.</summary>
        internal static int BytesPerPixel(RenderTextureFormat format)
        {
            switch (format)
            {
                case RenderTextureFormat.R8:
                    return 1;

                case RenderTextureFormat.RHalf:
                case RenderTextureFormat.R16:
                case RenderTextureFormat.RGB565:
                case RenderTextureFormat.ARGB4444:
                case RenderTextureFormat.ARGB1555:
                case RenderTextureFormat.RG16:
                    return 2;

                case RenderTextureFormat.RGHalf:
                case RenderTextureFormat.RFloat:
                case RenderTextureFormat.RInt:
                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.RG32:
                    return 4;

                case RenderTextureFormat.RGFloat:
                case RenderTextureFormat.RGInt:
                case RenderTextureFormat.ARGB64:
                case RenderTextureFormat.RGBAUShort:
                    return 8;

                case RenderTextureFormat.ARGBFloat:
                case RenderTextureFormat.ARGBInt:
                    return 16;

                default:
                    return 4;
            }
        }

        private void Adopt(RenderTextureDescriptor desc)
        {
            m_Descriptor = desc;

            if (m_Descriptor.volumeDepth < 1)
                m_Descriptor.volumeDepth = 1;

            if (m_Descriptor.msaaSamples < 1)
                m_Descriptor.msaaSamples = 1;

            if (m_Descriptor.dimension == TextureDimension.None || m_Descriptor.dimension == TextureDimension.Unknown)
                m_Descriptor.dimension = TextureDimension.Tex2D;

            SyncBaseState();
        }

        private void OnDescriptorChanged()
        {
            SyncBaseState();

            // "Setters re-create on next Create()" (design §3.5). Releasing now rather than deferring keeps
            // IsCreated() honest: a target whose size no longer matches the GPU resource is not created.
            if (m_Created)
                Release();
        }

        private void SyncBaseState()
        {
            SetSizeDirect(m_Descriptor.width, m_Descriptor.height);
            SetReadable(false);
            SetMipmapCount(m_Descriptor.useMipMap
                ? Texture2D.FullMipChainLength(m_Descriptor.width, m_Descriptor.height)
                : 1);
        }
    }
}
