// The one and only temporary render-texture pool. Not a UnityEngine type: it is the shim-side implementation behind
// RenderTexture.GetTemporary / ReleaseTemporary and CommandBuffer.GetTemporaryRT.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 ("Temporary pool" - the key, the "TempBuffer" name and the
// 8-frame trim), §3.10 (this type is an internal static class in the NowUI.Engine namespace), §4.1 guarantee 4
// (a backend never sees a nameID and has no GetTemporary of its own), §6.2 (ResetAll empties it).
//
// Why the key is what it is:
//   * filterMode and wrapMode are deliberately NOT in it. NowSdfImageField assigns both immediately AFTER the
//     acquire (NowSdfImageField.cs:448-451), so including them would guarantee a miss on the first acquire of every
//     configuration and quietly double the pool.
//   * sRGB stands in for RenderTextureReadWrite. `Default` is not a resource property, it is "whatever the project's
//     colour space implies", and the descriptor resolves it at construction - so two acquires that name the read/write
//     mode differently but resolve the same really are asking for the same resource and must share a pool slot.
//
// Why the trim exists at all: NowSdfImageField.Bake takes two float/half temporaries per bake at the field's exact
// pixel size (NowSdfImageField.cs:396-428). A UI that scrolls through differently sized fields would otherwise grow
// the pool without bound, one dead entry per size, for the life of the process.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// Reuses render targets across frames and drops the ones that stop being asked for. Single-threaded, like every
    /// other part of the shim's render path (design §4.1 guarantee 8).
    /// </summary>
    internal static class NowTemporaryRenderTexturePool
    {
        /// <summary>Frames a free entry may sit unused before it is released (design §3.5).</summary>
        internal const int TrimAfterFrames = 8;

        /// <summary>The name Unity gives a temporary target, and the one a frame debugger will show.</summary>
        internal const string TemporaryName = "TempBuffer";

        private static readonly Dictionary<Key, List<Entry>> s_Free = new Dictionary<Key, List<Entry>>();

        // Which key each outstanding target was issued under. Keyed by instance id rather than by the target's own
        // descriptor because a caller may legally change the descriptor while holding it, and the entry still has to
        // go back to the shelf it came from.
        private static readonly Dictionary<int, Key> s_Outstanding = new Dictionary<int, Key>();

        private static bool s_Hooked;

        /// <summary>Free entries currently parked in the pool. For tests and diagnostics.</summary>
        internal static int freeCount
        {
            get
            {
                int total = 0;

                foreach (KeyValuePair<Key, List<Entry>> shelf in s_Free)
                    total += shelf.Value.Count;

                return total;
            }
        }

        /// <summary>Targets handed out and not yet released. For tests and diagnostics.</summary>
        internal static int outstandingCount
        {
            get { return s_Outstanding.Count; }
        }

        /// <summary>Distinct keys the pool holds free entries for. For tests: this is what "fragmentation" means.</summary>
        internal static int shelfCount
        {
            get
            {
                int total = 0;

                foreach (KeyValuePair<Key, List<Entry>> shelf in s_Free)
                {
                    if (shelf.Value.Count > 0)
                        ++total;
                }

                return total;
            }
        }

        /// <summary>
        /// Hands out a created target matching <paramref name="desc"/>, reusing a free one when there is a match.
        /// </summary>
        internal static RenderTexture Get(RenderTextureDescriptor desc)
        {
            EnsureHooked();

            Normalize(ref desc);

            var key = new Key(desc);
            List<Entry> shelf;

            if (s_Free.TryGetValue(key, out shelf) && shelf.Count > 0)
            {
                // Last-in-first-out: the most recently released target is the one most likely still resident.
                int last = shelf.Count - 1;
                RenderTexture reused = shelf[last].texture;
                shelf.RemoveAt(last);

                // A pooled entry can have been lost to a context reset while it sat on the shelf; IsCreated() clears
                // the flag in that case, so Create() really does rebuild rather than returning a dead handle.
                if (!reused.IsCreated())
                    reused.Create();

                ResetSamplerState(reused);
                s_Outstanding[reused.GetInstanceID()] = key;
                return reused;
            }

            var created = new RenderTexture(desc);
            created.name = TemporaryName;
            created.Create();

            s_Outstanding[created.GetInstanceID()] = key;
            return created;
        }

        /// <summary>Puts a target back on the shelf it was issued from.</summary>
        internal static void Release(RenderTexture temp)
        {
            if (ReferenceEquals(temp, null))
                throw new ArgumentNullException(nameof(temp));

            EnsureHooked();

            int id = temp.GetInstanceID();
            Key key;

            if (!s_Outstanding.TryGetValue(id, out key))
                throw new ArgumentException(
                    "ReleaseTemporary: this RenderTexture was not obtained from GetTemporary, or has already been released.",
                    nameof(temp));

            s_Outstanding.Remove(id);

            // A destroyed target must not go back on the shelf: it would be handed out as a live one and fake-null
            // for whoever received it.
            if (temp == null)
                return;

            List<Entry> shelf;

            if (!s_Free.TryGetValue(key, out shelf))
            {
                shelf = new List<Entry>(4);
                s_Free.Add(key, shelf);
            }

            shelf.Add(new Entry(temp, NowRuntime.frameCount));
        }

        /// <summary>
        /// Releases every free entry that has sat unused for <see cref="TrimAfterFrames"/> frames. Called from
        /// <c>NowRuntime.EndFrame</c>; outstanding targets are never touched.
        /// </summary>
        internal static void Trim()
        {
            int now = NowRuntime.frameCount;

            foreach (KeyValuePair<Key, List<Entry>> shelf in s_Free)
            {
                List<Entry> entries = shelf.Value;

                // Backwards, so a removal cannot skip the next element.
                for (int i = entries.Count - 1; i >= 0; --i)
                {
                    if (now - entries[i].frameReleased < TrimAfterFrames)
                        continue;

                    RenderTexture texture = entries[i].texture;
                    entries.RemoveAt(i);

                    // Release, not Destroy: the GPU resource goes away now, and the handle - which nobody holds any
                    // more - is left to the GC. Destroying it would only add a queue entry with no observer.
                    if (texture != null)
                        texture.Release();
                }
            }
        }

        /// <summary>Empties the pool, releasing every free entry. Called from <c>NowRuntime.ResetAll</c> (design §6.2).</summary>
        internal static void Reset()
        {
            foreach (KeyValuePair<Key, List<Entry>> shelf in s_Free)
            {
                List<Entry> entries = shelf.Value;

                for (int i = 0; i < entries.Count; ++i)
                {
                    RenderTexture texture = entries[i].texture;

                    if (texture != null)
                        texture.Release();
                }

                entries.Clear();
            }

            s_Free.Clear();
            s_Outstanding.Clear();
        }

        /// <summary>
        /// Fills in the values a descriptor is allowed to leave at zero, so that two descriptors describing the same
        /// resource produce the same key. <c>default(RenderTextureDescriptor)</c> is legal input in Unity.
        /// </summary>
        private static void Normalize(ref RenderTextureDescriptor desc)
        {
            if (desc.volumeDepth < 1)
                desc.volumeDepth = 1;

            if (desc.msaaSamples < 1)
                desc.msaaSamples = 1;

            if (desc.dimension == TextureDimension.None || desc.dimension == TextureDimension.Unknown)
                desc.dimension = TextureDimension.Tex2D;
        }

        /// <summary>
        /// Hands a reused target back with Unity's fresh-temporary sampler state, so a caller that does not set
        /// filterMode gets the same texture whether the pool hit or missed.
        /// </summary>
        private static void ResetSamplerState(RenderTexture texture)
        {
            texture.filterMode = FilterMode.Bilinear;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.anisoLevel = 1;
        }

        private static void EnsureHooked()
        {
            if (s_Hooked)
                return;

            s_Hooked = true;

            // Subscribing lazily keeps the static-initialiser graph shallow (hazard D.1 #4): a core type that only
            // ever touches Texture2D never brings the pool - or its frame hooks - into existence.
            NowRuntime.onEngineEndFrame += Trim;
            NowRuntime.onEngineReset += Reset;
        }

        /// <summary>What makes two temporary targets interchangeable (design §3.5).</summary>
        private readonly struct Key : IEquatable<Key>
        {
            private readonly int m_Width;
            private readonly int m_Height;
            private readonly int m_DepthBufferBits;
            private readonly int m_MsaaSamples;
            private readonly int m_VolumeDepth;
            private readonly RenderTextureFormat m_Format;
            private readonly TextureDimension m_Dimension;
            private readonly bool m_SRgb;
            private readonly bool m_UseMipMap;

            internal Key(in RenderTextureDescriptor desc)
            {
                m_Width = desc.width;
                m_Height = desc.height;
                m_DepthBufferBits = desc.depthBufferBits;
                m_MsaaSamples = desc.msaaSamples;
                m_VolumeDepth = desc.volumeDepth;
                m_Format = desc.colorFormat;
                m_Dimension = desc.dimension;
                m_SRgb = desc.sRGB;
                m_UseMipMap = desc.useMipMap;
            }

            public bool Equals(Key other)
            {
                return m_Width == other.m_Width
                       && m_Height == other.m_Height
                       && m_DepthBufferBits == other.m_DepthBufferBits
                       && m_MsaaSamples == other.m_MsaaSamples
                       && m_VolumeDepth == other.m_VolumeDepth
                       && m_Format == other.m_Format
                       && m_Dimension == other.m_Dimension
                       && m_SRgb == other.m_SRgb
                       && m_UseMipMap == other.m_UseMipMap;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                // HashCode.Combine over the nine components, in two steps because it takes at most eight arguments.
                int head = HashCode.Combine(m_Width, m_Height, m_DepthBufferBits, m_MsaaSamples, m_VolumeDepth,
                    (int)m_Format, (int)m_Dimension, m_SRgb);

                return HashCode.Combine(head, m_UseMipMap);
            }
        }

        /// <summary>A free target plus the frame it became free, which is all the trim needs.</summary>
        private readonly struct Entry
        {
            internal readonly RenderTexture texture;
            internal readonly int frameReleased;

            internal Entry(RenderTexture texture, int frameReleased)
            {
                this.texture = texture;
                this.frameReleased = frameReleased;
            }
        }
    }
}
