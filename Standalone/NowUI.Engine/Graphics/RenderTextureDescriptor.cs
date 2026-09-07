// Mirrors UnityEngine.RenderTextureDescriptor for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 (`RenderTextureDescriptor.cs` field and constructor list).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.RenderTextureDescriptor` - NowRenderer
// and the glass backdrops read width/height/msaaSamples/bindMS/dimension/volumeDepth/vrUsage/useMipMap/
// autoGenerateMips/enableRandomWrite, and pass the struct by `in`.
//
// Two notes on fidelity:
//   * `shadowSamplingMode` is NOT declared. Design §3.5 lists it, but design §3.2 explicitly omits the
//     `UnityEngine.Rendering.ShadowSamplingMode` enum it would need, and no NowUI file reads it. The §3.2 omission
//     wins; the conflict is reported with unit U7 rather than resolved by inventing an enum another unit owns.
//   * `default(RenderTextureDescriptor)` has msaaSamples 0, volumeDepth 0, mipCount 0 and dimension None, not the
//     "(1)/(1)/(-1)/(Tex2D)" the design notes in brackets. Those brackets are the CONSTRUCTOR defaults, and Unity
//     behaves the same way: its descriptor is a plain struct whose sensible values come from a constructor, which is
//     why SystemInfo.GetRenderTextureSupportedMSAASampleCount has to floor msaaSamples at 1.
using System;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Everything needed to create a <see cref="RenderTexture"/>. Held by value inside every render texture, so a
    /// pooled target can be re-created after a context loss from the descriptor alone.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderTextureDescriptor
    {
        /// <summary>Texel width.</summary>
        public int width { get; set; }

        /// <summary>Texel height.</summary>
        public int height { get; set; }

        /// <summary>MSAA sample count; 1 means no multisampling.</summary>
        public int msaaSamples { get; set; }

        /// <summary>Slices for an array or 3D target; 1 for a plain 2D target.</summary>
        public int volumeDepth { get; set; }

        /// <summary>Mip levels, or -1 for the full chain.</summary>
        public int mipCount { get; set; }

        /// <summary>Depth/stencil bits: 0, 16 or 24.</summary>
        public int depthBufferBits { get; set; }

        /// <summary>Colour format of the target.</summary>
        public RenderTextureFormat colorFormat { get; set; }

        /// <summary>Whether the target is sRGB-encoded. Derived from the read/write mode at construction.</summary>
        public bool sRGB { get; set; }

        /// <summary>Shape of the target.</summary>
        public TextureDimension dimension { get; set; }

        /// <summary>Stereo layout. NowUI only ever asks for <see cref="VRTextureUsage.None"/>.</summary>
        public VRTextureUsage vrUsage { get; set; }

        /// <summary>Whether the target carries a mip chain.</summary>
        public bool useMipMap { get; set; }

        /// <summary>Whether mips are regenerated after every render into the target.</summary>
        public bool autoGenerateMips { get; set; }

        /// <summary>Whether a multisampled target is bound for sampling without a resolve.</summary>
        public bool bindMS { get; set; }

        /// <summary>Whether the target is usable as a compute UAV.</summary>
        public bool enableRandomWrite { get; set; }

        /// <summary>A default-format colour target with no depth buffer.</summary>
        public RenderTextureDescriptor(int width, int height)
            : this(width, height, RenderTextureFormat.Default, 0, -1)
        {
        }

        /// <summary>A colour target with no depth buffer.</summary>
        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat)
            : this(width, height, colorFormat, 0, -1)
        {
        }

        /// <summary>A colour target with a depth buffer.</summary>
        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits)
            : this(width, height, colorFormat, depthBufferBits, -1)
        {
        }

        /// <summary>The general constructor; every other one funnels through it so the defaults live in one place.</summary>
        public RenderTextureDescriptor(int width, int height, RenderTextureFormat colorFormat, int depthBufferBits, int mipCount)
        {
            this.width = width;
            this.height = height;
            msaaSamples = 1;
            volumeDepth = 1;
            this.mipCount = mipCount;
            this.depthBufferBits = depthBufferBits;
            this.colorFormat = colorFormat;

            // Unity derives sRGB from the graphics format, which for a descriptor built this way follows the project's
            // colour space. The shim reports the runtime's colour space for the same reason: it is the single value
            // SystemInfo, QualitySettings and the backend caps all agree on (design §4.1).
            sRGB = NowUI.Engine.NowRuntime.colorSpace == ColorSpace.Linear;

            dimension = TextureDimension.Tex2D;
            vrUsage = VRTextureUsage.None;
            useMipMap = false;
            autoGenerateMips = true;
            bindMS = false;
            enableRandomWrite = false;
        }
    }
}
