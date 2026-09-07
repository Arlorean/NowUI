// The render backend's capability report. Not a UnityEngine type: it is the single source SystemInfo and
// QualitySettings read from, so the shim never has to guess what the GPU underneath it can do.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§4.1 declares the struct and its member list; §3.7 shows
// SystemInfo/QualitySettings forwarding to it; §4.7 explains which flags the WebGL2 backend gates on).
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace NowUI.Engine
{
    /// <summary>
    /// What the installed <see cref="INowRenderBackend"/> can do. Immutable: a backend reports its capabilities once,
    /// and every shim query (<c>SystemInfo.SupportsRenderTextureFormat</c>,
    /// <c>SystemInfo.GetRenderTextureSupportedMSAASampleCount</c>, <c>QualitySettings.activeColorSpace</c>) answers
    /// from this struct rather than from a guess.
    /// </summary>
    /// <remarks>
    /// The flags are the ones NowUI actually branches on. <c>NowGlassRenderer</c> skips its resolve passes when
    /// <see cref="supportsMultisampledTextures"/> is false, and <c>NowSdf</c> falls back off the single-channel path
    /// when <see cref="supportsR8"/> is false, so getting these wrong changes which code path the core takes rather
    /// than merely producing a driver error (design §4.7).
    /// </remarks>
    [Serializable]
    public readonly struct NowRenderCaps
    {
        /// <summary>Largest texture edge in texels the backend will accept.</summary>
        public readonly int maxTextureSize;

        /// <summary>Highest MSAA sample count. <c>1</c> means no multisampling at all.</summary>
        public readonly int maxMsaaSamples;

        /// <summary>Whether a multisampled texture can be sampled directly (<c>bindTextureMS</c>).</summary>
        public readonly bool supportsMultisampledTextures;

        /// <summary>Whether <c>TextureDimension.Tex2DArray</c> targets work.</summary>
        public readonly bool supportsTextureArrays;

        /// <summary>Whether instanced draws work (<see cref="INowRenderBackend.DrawProcedural"/> with instanceCount &gt; 1).</summary>
        public readonly bool supportsInstancing;

        /// <summary>Single-channel 8-bit colour targets and textures.</summary>
        public readonly bool supportsR8;

        /// <summary>Single-channel 16-bit float colour targets and textures.</summary>
        public readonly bool supportsRHalf;

        /// <summary>Single-channel 32-bit float colour targets and textures.</summary>
        public readonly bool supportsRFloat;

        /// <summary>Two-channel 16-bit float colour targets and textures.</summary>
        public readonly bool supportsRGHalf;

        /// <summary>Two-channel 32-bit float colour targets and textures.</summary>
        public readonly bool supportsRGFloat;

        /// <summary>Four-channel 16-bit float colour targets and textures.</summary>
        public readonly bool supportsARGBHalf;

        /// <summary>Four-channel 32-bit float colour targets and textures.</summary>
        public readonly bool supportsARGBFloat;

        /// <summary>Ordinary four-channel 8-bit colour targets and textures. Effectively "can you render at all".</summary>
        public readonly bool supportsARGB32;

        /// <summary>Depth-only targets (<c>RenderTextureFormat.Depth</c>, <c>Shadowmap</c>).</summary>
        public readonly bool supportsDepth;

        /// <summary>
        /// Whether the API's render targets have texel row 0 at the bottom. Informational only.
        /// </summary>
        /// <remarks>
        /// The backend still owns the flip (design §4.1 and §4.7): NowUI's projection convention is fixed, so nothing
        /// on the shim side reads this. It exists so a host can report the convention and so a diff of two backends'
        /// caps shows the difference rather than hiding it.
        /// </remarks>
        public readonly bool renderTargetsAreBottomUp;

        /// <summary>
        /// What the framebuffer expects. <c>SystemInfo</c> and <c>QualitySettings</c> must agree with this value
        /// (design §4.1), which is why <c>NowRuntime.colorSpace</c> and this field are set together by a host.
        /// </summary>
        public readonly ColorSpace colorSpace;

        /// <summary>Human-readable device name; the WebGL <c>RENDERER</c> string for the M2 backend.</summary>
        public readonly string deviceName;

        /// <summary>Which graphics API the backend speaks. WebGL 2.0 reports <c>OpenGLES3</c>, as Unity does.</summary>
        public readonly GraphicsDeviceType deviceType;

        /// <summary>Approximate video memory in megabytes, or 0 when the backend cannot tell.</summary>
        public readonly int graphicsMemorySizeMb;

        /// <summary>Reports every field explicitly. Backends build this once, in their constructor.</summary>
        public NowRenderCaps(
            int maxTextureSize,
            int maxMsaaSamples,
            bool supportsMultisampledTextures,
            bool supportsTextureArrays,
            bool supportsInstancing,
            bool supportsR8,
            bool supportsRHalf,
            bool supportsRFloat,
            bool supportsRGHalf,
            bool supportsRGFloat,
            bool supportsARGBHalf,
            bool supportsARGBFloat,
            bool supportsARGB32,
            bool supportsDepth,
            bool renderTargetsAreBottomUp,
            ColorSpace colorSpace,
            string deviceName,
            GraphicsDeviceType deviceType,
            int graphicsMemorySizeMb)
        {
            this.maxTextureSize = maxTextureSize;
            this.maxMsaaSamples = maxMsaaSamples;
            this.supportsMultisampledTextures = supportsMultisampledTextures;
            this.supportsTextureArrays = supportsTextureArrays;
            this.supportsInstancing = supportsInstancing;
            this.supportsR8 = supportsR8;
            this.supportsRHalf = supportsRHalf;
            this.supportsRFloat = supportsRFloat;
            this.supportsRGHalf = supportsRGHalf;
            this.supportsRGFloat = supportsRGFloat;
            this.supportsARGBHalf = supportsARGBHalf;
            this.supportsARGBFloat = supportsARGBFloat;
            this.supportsARGB32 = supportsARGB32;
            this.supportsDepth = supportsDepth;
            this.renderTargetsAreBottomUp = renderTargetsAreBottomUp;
            this.colorSpace = colorSpace;
            // Never null: SystemInfo.graphicsDeviceName is a string property under Unity and core code concatenates it.
            this.deviceName = deviceName ?? string.Empty;
            this.deviceType = deviceType;
            this.graphicsMemorySizeMb = graphicsMemorySizeMb;
        }

        /// <summary>
        /// A capability report with every format flag set, for backends that accept everything (the
        /// <see cref="NullRenderBackend"/>, and any test double that should never make NowUI take a fallback path).
        /// </summary>
        public static NowRenderCaps CreateAllFormats(
            int maxTextureSize,
            int maxMsaaSamples,
            bool supportsMultisampledTextures,
            bool supportsTextureArrays,
            bool supportsInstancing,
            bool renderTargetsAreBottomUp,
            ColorSpace colorSpace,
            string deviceName,
            GraphicsDeviceType deviceType,
            int graphicsMemorySizeMb)
        {
            return new NowRenderCaps(
                maxTextureSize,
                maxMsaaSamples,
                supportsMultisampledTextures,
                supportsTextureArrays,
                supportsInstancing,
                true, true, true, true, true, true, true, true, true,
                renderTargetsAreBottomUp,
                colorSpace,
                deviceName,
                deviceType,
                graphicsMemorySizeMb);
        }

        /// <summary>
        /// Whether a render target of <paramref name="format"/> can be created. Backs
        /// <c>SystemInfo.SupportsRenderTextureFormat</c>.
        /// </summary>
        /// <remarks>
        /// Each format is answered by the capability class it belongs to, because the nine format flags are the whole
        /// vocabulary §4.1 gives a backend. Formats with no dedicated flag fall back to
        /// <see cref="supportsARGB32"/> — the "can this device render colour at all" flag — rather than to
        /// <c>false</c>, so that a permissive backend really does accept every member and a
        /// no-render-targets backend really does reject every member. Unity's own answer is per-driver and finer
        /// grained than this; see the objection recorded with unit U10.
        /// </remarks>
        public bool SupportsRenderTextureFormat(RenderTextureFormat format)
        {
            switch (format)
            {
                case RenderTextureFormat.Depth:
                case RenderTextureFormat.Shadowmap:
                    return supportsDepth;

                // Single channel, 8-bit unsigned.
                case RenderTextureFormat.R8:
                // Two channel, 8-bit unsigned per channel — the same storage class as R8.
                case RenderTextureFormat.RG16:
                    return supportsR8;

                // Single channel, 16-bit. R16 is unorm rather than float, but it is the same "half-width single
                // channel" capability every driver gates together.
                case RenderTextureFormat.RHalf:
                case RenderTextureFormat.R16:
                    return supportsRHalf;

                case RenderTextureFormat.RFloat:
                case RenderTextureFormat.RInt:
                    return supportsRFloat;

                // Two channel, 16-bit per channel.
                case RenderTextureFormat.RGHalf:
                case RenderTextureFormat.RG32:
                    return supportsRGHalf;

                case RenderTextureFormat.RGFloat:
                case RenderTextureFormat.RGInt:
                    return supportsRGFloat;

                case RenderTextureFormat.ARGBHalf:
                case RenderTextureFormat.DefaultHDR:
                    return supportsARGBHalf;

                case RenderTextureFormat.ARGBFloat:
                case RenderTextureFormat.RGB111110Float:
                    return supportsARGBFloat;

                // Every remaining member is a fixed-point colour target: ARGB32, Default, BGRA32, the packed 4444 /
                // 1555 / 565 / 2101010 forms, the 16-bit-per-channel integer forms, and the XR packings.
                default:
                    return supportsARGB32;
            }
        }

        /// <summary>
        /// Whether a CPU texture of <paramref name="format"/> can be uploaded. Backs
        /// <c>SystemInfo.SupportsTextureFormat</c>.
        /// </summary>
        /// <remarks>Same classification rule as <see cref="SupportsRenderTextureFormat"/>.</remarks>
        public bool SupportsTextureFormat(TextureFormat format)
        {
            switch (format)
            {
                // Single channel, 8 or 16 bit fixed point.
                case TextureFormat.Alpha8:
                case TextureFormat.R8:
                case TextureFormat.R8_SIGNED:
                case TextureFormat.R16:
                case TextureFormat.R16_SIGNED:
                    return supportsR8;

                case TextureFormat.RHalf:
                    return supportsRHalf;

                case TextureFormat.RFloat:
                    return supportsRFloat;

                // Two channel, in every width. Unity has no separate RG8 capability query, so they share a flag.
                case TextureFormat.RGHalf:
                case TextureFormat.RG16:
                case TextureFormat.RG16_SIGNED:
                case TextureFormat.RG32:
                case TextureFormat.RG32_SIGNED:
                    return supportsRGHalf;

                case TextureFormat.RGFloat:
                    return supportsRGFloat;

                case TextureFormat.RGBAHalf:
                    return supportsARGBHalf;

                case TextureFormat.RGBAFloat:
                    return supportsARGBFloat;

                // Everything else is a four-channel or three-channel fixed-point format, compressed or not.
                default:
                    return supportsARGB32;
            }
        }
    }
}
