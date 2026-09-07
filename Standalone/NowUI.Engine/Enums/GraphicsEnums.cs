// Mirrors the UnityEngine graphics enums (FilterMode, TextureWrapMode, TextureFormat, RenderTextureFormat,
// RenderTextureReadWrite, CubemapFace) for the NowUI standalone build; spec: Docs/Standalone/UnityValueTypeSemantics.md (§14).
// Numeric values are Unity's exact values: NowUI casts and serialises some of them, so the numbers are part of the contract.

namespace UnityEngine
{
    /// <summary>Texture filtering mode (spec §14).</summary>
    public enum FilterMode
    {
        Point = 0,
        Bilinear = 1,
        Trilinear = 2,
    }

    /// <summary>Texture addressing mode outside [0, 1] (spec §14).</summary>
    public enum TextureWrapMode
    {
        Repeat = 0,
        Clamp = 1,
        Mirror = 2,
        MirrorOnce = 3,
    }

    /// <summary>Pixel format of a <c>Texture2D</c> (spec §14). Obsolete negative members are included with their values.</summary>
    public enum TextureFormat
    {
        Alpha8 = 1,
        ARGB4444 = 2,
        RGB24 = 3,
        RGBA32 = 4,
        ARGB32 = 5,
        RGB565 = 7,
        R16 = 9,
        DXT1 = 10,
        DXT5 = 12,
        RGBA4444 = 13,
        BGRA32 = 14,
        RHalf = 15,
        RGHalf = 16,
        RGBAHalf = 17,
        RFloat = 18,
        RGFloat = 19,
        RGBAFloat = 20,
        YUY2 = 21,
        RGB9e5Float = 22,
        BC6H = 24,
        BC7 = 25,
        BC4 = 26,
        BC5 = 27,
        DXT1Crunched = 28,
        DXT5Crunched = 29,
        PVRTC_RGB2 = 30,
        PVRTC_RGBA2 = 31,
        PVRTC_RGB4 = 32,
        PVRTC_RGBA4 = 33,
        ETC_RGB4 = 34,
        EAC_R = 41,
        EAC_R_SIGNED = 42,
        EAC_RG = 43,
        EAC_RG_SIGNED = 44,
        ETC2_RGB = 45,
        ETC2_RGBA1 = 46,
        ETC2_RGBA8 = 47,
        ASTC_4x4 = 48,
        ASTC_5x5 = 49,
        ASTC_6x6 = 50,
        ASTC_8x8 = 51,
        ASTC_10x10 = 52,
        ASTC_12x12 = 53,
        RG16 = 62,
        R8 = 63,
        ETC_RGB4Crunched = 64,
        ETC2_RGBA8Crunched = 65,
        ASTC_HDR_4x4 = 66,
        ASTC_HDR_5x5 = 67,
        ASTC_HDR_6x6 = 68,
        ASTC_HDR_8x8 = 69,
        ASTC_HDR_10x10 = 70,
        ASTC_HDR_12x12 = 71,
        RG32 = 72,
        RGB48 = 73,
        RGBA64 = 74,
        R8_SIGNED = 75,
        RG16_SIGNED = 76,
        RGB24_SIGNED = 77,
        RGBA32_SIGNED = 78,
        R16_SIGNED = 79,
        RG32_SIGNED = 80,
        RGB48_SIGNED = 81,
        RGBA64_SIGNED = 82,

        // Obsolete in Unity; kept because the numeric values are part of serialised data.
        ETC_RGB4_3DS = -60,
        ETC_RGBA8_3DS = -61,
        ASTC_RGB_4x4 = -48,
        ASTC_RGB_5x5 = -49,
        ASTC_RGB_6x6 = -50,
        ASTC_RGB_8x8 = -51,
        ASTC_RGB_10x10 = -52,
        ASTC_RGB_12x12 = -53,
        ASTC_RGBA_4x4 = -54,
        ASTC_RGBA_5x5 = -55,
        ASTC_RGBA_6x6 = -56,
        ASTC_RGBA_8x8 = -57,
        ASTC_RGBA_10x10 = -58,
        ASTC_RGBA_12x12 = -59,
    }

    /// <summary>Pixel format of a <c>RenderTexture</c> (spec §14). Note the gap at 21.</summary>
    public enum RenderTextureFormat
    {
        ARGB32 = 0,
        Depth = 1,
        ARGBHalf = 2,
        Shadowmap = 3,
        RGB565 = 4,
        ARGB4444 = 5,
        ARGB1555 = 6,
        Default = 7,
        ARGB2101010 = 8,
        DefaultHDR = 9,
        ARGB64 = 10,
        ARGBFloat = 11,
        RGFloat = 12,
        RGHalf = 13,
        RFloat = 14,
        RHalf = 15,
        R8 = 16,
        ARGBInt = 17,
        RGInt = 18,
        RInt = 19,
        BGRA32 = 20,
        RGB111110Float = 22,
        RG32 = 23,
        RGBAUShort = 24,
        RG16 = 25,
        BGRA10101010_XR = 26,
        BGR101010_XR = 27,
        R16 = 28,
    }

    /// <summary>Colour space conversion applied when reading from / writing to a <c>RenderTexture</c> (spec §14).</summary>
    public enum RenderTextureReadWrite
    {
        Default = 0,
        Linear = 1,
        sRGB = 2,
    }

    /// <summary>Face of a cubemap render target (spec §14).</summary>
    public enum CubemapFace
    {
        Unknown = -1,
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5,
    }
}
