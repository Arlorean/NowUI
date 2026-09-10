using System;
using System.Runtime.InteropServices;

#if NOWUI_STANDALONE_MSDF_NATIVE
namespace NowUI
{
    // Keep the shared DLL usable on native and browser hosts. Browser calls pass one
    // pointer; the C++ bridge forwards to the unchanged native implementation.
    public static partial class NowFontCompiler
    {
        [StructLayout(LayoutKind.Sequential)]
        unsafe struct Browser_nowui_compile_font_from_memory_with_codepoints
        {
            public byte* fontData;
            public int fontDataLength;
            public int size;
            public int pixelRange;
            public int* codepoints;
            public int codepointCount;
            public byte* atlasRgba;
            public int atlasRgbaLength;
            public NativeGlyph* glyphs;
            public int glyphCapacity;
            public NativeAtlasInfo* info;
            public byte* errorBuffer;
            public int errorBufferLength;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe int nowui_compile_font_from_memory_with_codepoints_browser(Browser_nowui_compile_font_from_memory_with_codepoints* args);

        static unsafe int nowui_compile_font_from_memory_with_codepoints(byte[] fontData, int fontDataLength, int size, int pixelRange, int[] codepoints, int codepointCount, byte[] atlasRgba, int atlasRgbaLength, NativeGlyph[] glyphs, int glyphCapacity, ref NativeAtlasInfo info, byte[] errorBuffer, int errorBufferLength)
        {
            if (!OperatingSystem.IsBrowser())
            {
                return nowui_compile_font_from_memory_with_codepoints_native(fontData, fontDataLength, size, pixelRange, codepoints, codepointCount, atlasRgba, atlasRgbaLength, glyphs, glyphCapacity, ref info, errorBuffer, errorBufferLength);
            }
            fixed (byte* fontDataPointer = fontData)
            fixed (int* codepointsPointer = codepoints)
            fixed (byte* atlasRgbaPointer = atlasRgba)
            fixed (NativeGlyph* glyphsPointer = glyphs)
            fixed (NativeAtlasInfo* infoPointer = &info)
            fixed (byte* errorBufferPointer = errorBuffer)
            {
                var args = new Browser_nowui_compile_font_from_memory_with_codepoints
                {
                    fontData = fontDataPointer,
                    fontDataLength = fontDataLength,
                    size = size,
                    pixelRange = pixelRange,
                    codepoints = codepointsPointer,
                    codepointCount = codepointCount,
                    atlasRgba = atlasRgbaPointer,
                    atlasRgbaLength = atlasRgbaLength,
                    glyphs = glyphsPointer,
                    glyphCapacity = glyphCapacity,
                    info = infoPointer,
                    errorBuffer = errorBufferPointer,
                    errorBufferLength = errorBufferLength,
                };
                return nowui_compile_font_from_memory_with_codepoints_browser(&args);
            }
        }

    }
}
#endif
