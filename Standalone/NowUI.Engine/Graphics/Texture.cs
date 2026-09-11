// Mirrors UnityEngine.Texture for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`Texture.cs` member list, the backendId/version handle
// contract), §4.1 (INowRenderBackend.UpdateSampler), §1.2 (allocation rule).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Texture`; hazard D.1 #11
// (`updateCount` is a staleness signal NowSdf and NowSdfImageField branch on).
//
// Three behaviours here look odd and are deliberate:
//   * `updateCount` is a plain wrapping counter with NO meaning other than "different from last time". NowSdf
//     (NowSdf.cs:3939) and NowSdfImageField (NowSdfImageField.cs:159/483) cache the value and compare, so overflow is
//     harmless but a *missed* increment silently serves a stale SDF forever.
//   * the sampler setters notify the backend only when the value actually CHANGES. NowSdfImageField assigns
//     `filterMode`/`wrapMode` on every acquire (NowSdfImageField.cs:448-451) - onto a pooled render texture that
//     already has them - so an unconditional notify would be one redundant backend call per bake, per frame.
//   * nothing here throws once the object is destroyed. Unity's own `Texture.width` does throw, but the shim's
//     `Object` only guards `name`/`hideFlags` (design §3.4), and every core call site fake-null tests first
//     (hazard D.1 #1). Diverging here alone would trade one faithful throw for a lot of surprise.
using NowUI.Engine;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Base of every texture resource. Under Unity this wraps a native texture; here it is a handle carrying the
    /// sampler state plus the two numbers a backend caches on: <see cref="Object.GetInstanceID"/> for identity and
    /// <see cref="version"/> for "the CPU data moved" (design §4.1 guarantee 1).
    /// </summary>
    public abstract class Texture : Object
    {
        // Process-wide, because Unity's Texture.currentTextureMemory is process-wide. A long internally so that a
        // host with more than 2 GB of textures cannot make the accounting go negative; the property saturates.
        private static long s_TextureMemory;

        private int m_Width;
        private int m_Height;
        private int m_MipmapCount = 1;
        private FilterMode m_FilterMode = FilterMode.Bilinear;
        private TextureWrapMode m_WrapModeU = TextureWrapMode.Repeat;
        private TextureWrapMode m_WrapModeV = TextureWrapMode.Repeat;
        private TextureWrapMode m_WrapModeW = TextureWrapMode.Repeat;
        private int m_AnisoLevel = 1;
        private uint m_UpdateCount;
        private TextureDimension m_Dimension = TextureDimension.Tex2D;

        // What this texture currently contributes to currentTextureMemory. Kept per instance so a Reinitialize or a
        // Release adjusts the total by a delta rather than by a recomputation the subclass could get wrong.
        private long m_MemoryBytes;

        /// <summary>The backend's handle for this texture; 0 means "not created yet" (design §3.5).</summary>
        internal int backendId;

        /// <summary>Bumped whenever the CPU-side contents change, so a backend re-uploads only on a mismatch.</summary>
        internal uint version;

        /// <summary>Texel width. Virtual because <c>RenderTexture</c> re-creates itself when it changes.</summary>
        public virtual int width
        {
            get { return m_Width; }
            set { m_Width = value; }
        }

        /// <summary>Texel height.</summary>
        public virtual int height
        {
            get { return m_Height; }
            set { m_Height = value; }
        }

        /// <summary>Number of mip levels, 1 when there is no chain.</summary>
        public int mipmapCount
        {
            get { return m_MipmapCount; }
            protected set { m_MipmapCount = value; }
        }

        /// <summary>
        /// Whether the CPU store may still be read. Sealed by <c>Texture2D.Apply(_, makeNoLongerReadable: true)</c>;
        /// always false for a render texture.
        /// </summary>
        public bool isReadable { get; protected set; }

        /// <summary>Filtering mode. Default <see cref="FilterMode.Bilinear"/>, as in Unity.</summary>
        public FilterMode filterMode
        {
            get { return m_FilterMode; }
            set
            {
                if (m_FilterMode == value)
                    return;

                m_FilterMode = value;
                NotifySampler();
            }
        }

        /// <summary>
        /// Addressing mode. Unity's <c>wrapMode</c> getter reports the U axis and its setter writes all three, which is
        /// why the per-axis properties exist at all.
        /// </summary>
        public TextureWrapMode wrapMode
        {
            get { return m_WrapModeU; }
            set
            {
                if (m_WrapModeU == value && m_WrapModeV == value && m_WrapModeW == value)
                    return;

                m_WrapModeU = value;
                m_WrapModeV = value;
                m_WrapModeW = value;
                NotifySampler();
            }
        }

        /// <summary>Addressing mode along U only.</summary>
        public TextureWrapMode wrapModeU
        {
            get { return m_WrapModeU; }
            set
            {
                if (m_WrapModeU == value)
                    return;

                m_WrapModeU = value;
                NotifySampler();
            }
        }

        /// <summary>Addressing mode along V only.</summary>
        public TextureWrapMode wrapModeV
        {
            get { return m_WrapModeV; }
            set
            {
                if (m_WrapModeV == value)
                    return;

                m_WrapModeV = value;
                NotifySampler();
            }
        }

        /// <summary>Addressing mode along W only (3D and cube textures).</summary>
        public TextureWrapMode wrapModeW
        {
            get { return m_WrapModeW; }
            set
            {
                if (m_WrapModeW == value)
                    return;

                m_WrapModeW = value;
                NotifySampler();
            }
        }

        /// <summary>
        /// Anisotropic filtering level. Unity clamps assignments into [0, 16] rather than throwing, so the shim does
        /// too - a host that writes 64 gets 16, not an exception in the middle of a draw.
        /// </summary>
        public int anisoLevel
        {
            get { return m_AnisoLevel; }
            set
            {
                int clamped = value < 0 ? 0 : (value > 16 ? 16 : value);

                if (m_AnisoLevel == clamped)
                    return;

                m_AnisoLevel = clamped;
                NotifySampler();
            }
        }

        /// <summary>
        /// Incremented on every upload and on every bind as a draw target. NowUI treats a change as "my cached copy of
        /// this texture is stale" (hazard D.1 #11); the absolute value means nothing and is allowed to wrap.
        /// </summary>
        public uint updateCount
        {
            get { return m_UpdateCount; }
        }

        /// <summary>
        /// The shape of the resource. Virtual with a setter because <c>RenderTexture.dimension</c> is assignable and an
        /// override cannot add an accessor the base does not declare - Unity's own <c>Texture</c> carries both for the
        /// same reason, even though the docs show it read-only. Design §3.5 lists it get-only on <c>Texture</c> and
        /// get/set on <c>RenderTexture</c>; the setter here is what makes that pair legal C#.
        /// </summary>
        public virtual TextureDimension dimension
        {
            get { return m_Dimension; }
            set { m_Dimension = value; }
        }

        /// <summary>
        /// Bytes of texture memory currently accounted for, saturating at <see cref="int.MaxValue"/> as Unity's
        /// 32-bit counter does.
        /// </summary>
        public static int currentTextureMemory
        {
            get { return s_TextureMemory > int.MaxValue ? int.MaxValue : (int)s_TextureMemory; }
        }

        /// <summary>
        /// Marks the contents changed. Called by <c>Texture2D.Apply</c> and by <c>NowImmediate</c> when a render
        /// texture is bound as a draw target.
        /// </summary>
        internal void IncrementUpdateCount()
        {
            // Unchecked: the counter is a change signal, and NowUI only ever compares it for inequality.
            unchecked
            {
                ++m_UpdateCount;
            }
        }

        /// <summary>Sets the width and height without going through the virtual setters a subclass may override.</summary>
        internal void SetSizeDirect(int width, int height)
        {
            m_Width = width;
            m_Height = height;
        }

        /// <summary>Sets <see cref="isReadable"/> from engine code outside a subclass body.</summary>
        internal void SetReadable(bool value)
        {
            isReadable = value;
        }

        /// <summary>Sets <see cref="mipmapCount"/> from engine code outside a subclass body.</summary>
        internal void SetMipmapCount(int value)
        {
            m_MipmapCount = value < 1 ? 1 : value;
        }

        /// <summary>
        /// Re-points this texture's contribution to <see cref="currentTextureMemory"/> at <paramref name="bytes"/>.
        /// Idempotent, so a subclass may call it on every resize without tracking the previous value itself.
        /// </summary>
        internal void SetMemoryFootprint(long bytes)
        {
            if (bytes < 0)
                bytes = 0;

            s_TextureMemory += bytes - m_MemoryBytes;
            m_MemoryBytes = bytes;
        }

        /// <summary>Releases this texture's share of the memory counter. Subclasses must call it from their override.</summary>
        internal override void OnDestroyResources()
        {
            SetMemoryFootprint(0);
        }

        /// <summary>
        /// Tells the backend the sampler state moved. Separate from <see cref="version"/>: sampler state is not
        /// texel data, so a filter-mode flip must not make a backend re-upload the whole texture.
        /// </summary>
        private void NotifySampler()
        {
            INowRenderBackend backend = NowRuntime.backend;

            if (backend != null)
                backend.UpdateSampler(this);
        }
    }
}
