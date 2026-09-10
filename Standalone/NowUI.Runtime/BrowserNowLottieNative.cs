using System;
using System.Runtime.InteropServices;

#if !NOWUI_VG_DISABLE_NATIVE
namespace NowUI.Internal
{
    // Keep the shared DLL usable on native and browser hosts. Browser calls pass one
    // pointer; the C++ bridge forwards to the unchanged native implementation.
    public static partial class NowLottieNative
    {
        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Browser_nowui_vg_blit_mesh
        {
            public float* srcPositions;
            public float* srcColors;
            public int vertexCount;
            public int* srcIndices;
            public int indexCount;
            public float positionScale;
            public float offsetX;
            public float offsetY;
            public float* tint4;
            public float* mask4;
            public float* rect4;
            public float* dstVerts;
            public float* dstUvs;
            public float* dstRawUv;
            public float* dstRect;
            public float* dstRadius;
            public float* dstColor;
            public float* dstOutline;
            public float* dstExtra;
            public float* dstMask;
            public int dstVertexBase;
            public int* dstIndices;
            public int dstIndexBase;
            public int indexOffset;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void nowui_vg_blit_mesh_browser(Browser_nowui_vg_blit_mesh* args);

        static unsafe void nowui_vg_blit_mesh(float* srcPositions, float* srcColors, int vertexCount, int* srcIndices, int indexCount, float positionScale, float offsetX, float offsetY, float* tint4, float* mask4, float* rect4, float* dstVerts, float* dstUvs, float* dstRawUv, float* dstRect, float* dstRadius, float* dstColor, float* dstOutline, float* dstExtra, float* dstMask, int dstVertexBase, int* dstIndices, int dstIndexBase, int indexOffset)
        {
            if (!OperatingSystem.IsBrowser())
            {
                nowui_vg_blit_mesh_native(srcPositions, srcColors, vertexCount, srcIndices, indexCount, positionScale, offsetX, offsetY, tint4, mask4, rect4, dstVerts, dstUvs, dstRawUv, dstRect, dstRadius, dstColor, dstOutline, dstExtra, dstMask, dstVertexBase, dstIndices, dstIndexBase, indexOffset);
                return;
            }
            {
                var args = new Browser_nowui_vg_blit_mesh
                {
                    srcPositions = srcPositions,
                    srcColors = srcColors,
                    vertexCount = vertexCount,
                    srcIndices = srcIndices,
                    indexCount = indexCount,
                    positionScale = positionScale,
                    offsetX = offsetX,
                    offsetY = offsetY,
                    tint4 = tint4,
                    mask4 = mask4,
                    rect4 = rect4,
                    dstVerts = dstVerts,
                    dstUvs = dstUvs,
                    dstRawUv = dstRawUv,
                    dstRect = dstRect,
                    dstRadius = dstRadius,
                    dstColor = dstColor,
                    dstOutline = dstOutline,
                    dstExtra = dstExtra,
                    dstMask = dstMask,
                    dstVertexBase = dstVertexBase,
                    dstIndices = dstIndices,
                    dstIndexBase = dstIndexBase,
                    indexOffset = indexOffset,
                };
                nowui_vg_blit_mesh_browser(&args);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Browser_nowui_vg_blit_text_run
        {
            public float* glyphs;
            public int start;
            public int end;
            public float x;
            public float y;
            public float fontSize;
            public float baseline;
            public float* mask4;
            public float* color4;
            public float* outline4;
            public float outline;
            public float pixelRange;
            public float* dstVerts;
            public float* dstUvs;
            public float* dstRawUv;
            public float* dstRect;
            public float* dstRadius;
            public float* dstColor;
            public float* dstOutline;
            public float* dstExtra;
            public float* dstMask;
            public int dstVertexBase;
            public int* dstIndices;
            public int dstIndexBase;
            public float* outPenX;
            public int* outCounts;
            public float* outBounds;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void nowui_vg_blit_text_run_browser(Browser_nowui_vg_blit_text_run* args);

        static unsafe void nowui_vg_blit_text_run(float* glyphs, int start, int end, float x, float y, float fontSize, float baseline, float* mask4, float* color4, float* outline4, float outline, float pixelRange, float* dstVerts, float* dstUvs, float* dstRawUv, float* dstRect, float* dstRadius, float* dstColor, float* dstOutline, float* dstExtra, float* dstMask, int dstVertexBase, int* dstIndices, int dstIndexBase, float* outPenX, int* outCounts, float* outBounds)
        {
            if (!OperatingSystem.IsBrowser())
            {
                nowui_vg_blit_text_run_native(glyphs, start, end, x, y, fontSize, baseline, mask4, color4, outline4, outline, pixelRange, dstVerts, dstUvs, dstRawUv, dstRect, dstRadius, dstColor, dstOutline, dstExtra, dstMask, dstVertexBase, dstIndices, dstIndexBase, outPenX, outCounts, outBounds);
                return;
            }
            {
                var args = new Browser_nowui_vg_blit_text_run
                {
                    glyphs = glyphs,
                    start = start,
                    end = end,
                    x = x,
                    y = y,
                    fontSize = fontSize,
                    baseline = baseline,
                    mask4 = mask4,
                    color4 = color4,
                    outline4 = outline4,
                    outline = outline,
                    pixelRange = pixelRange,
                    dstVerts = dstVerts,
                    dstUvs = dstUvs,
                    dstRawUv = dstRawUv,
                    dstRect = dstRect,
                    dstRadius = dstRadius,
                    dstColor = dstColor,
                    dstOutline = dstOutline,
                    dstExtra = dstExtra,
                    dstMask = dstMask,
                    dstVertexBase = dstVertexBase,
                    dstIndices = dstIndices,
                    dstIndexBase = dstIndexBase,
                    outPenX = outPenX,
                    outCounts = outCounts,
                    outBounds = outBounds,
                };
                nowui_vg_blit_text_run_browser(&args);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Browser_nowui_vg_pack_canvas
        {
            public float* srcVerts;
            public float* srcUvs;
            public float* srcRadius;
            public float* srcRawUv;
            public float* srcColors;
            public float* srcRect;
            public float* srcMask;
            public float* srcExtra;
            public float* srcOutline;
            public int vertexCount;
            public int isText;
            public float offsetX;
            public float offsetY;
            public float* dst;
            public int dstVertexBase;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void nowui_vg_pack_canvas_browser(Browser_nowui_vg_pack_canvas* args);

        static unsafe void nowui_vg_pack_canvas(float* srcVerts, float* srcUvs, float* srcRadius, float* srcRawUv, float* srcColors, float* srcRect, float* srcMask, float* srcExtra, float* srcOutline, int vertexCount, int isText, float offsetX, float offsetY, float* dst, int dstVertexBase)
        {
            if (!OperatingSystem.IsBrowser())
            {
                nowui_vg_pack_canvas_native(srcVerts, srcUvs, srcRadius, srcRawUv, srcColors, srcRect, srcMask, srcExtra, srcOutline, vertexCount, isText, offsetX, offsetY, dst, dstVertexBase);
                return;
            }
            {
                var args = new Browser_nowui_vg_pack_canvas
                {
                    srcVerts = srcVerts,
                    srcUvs = srcUvs,
                    srcRadius = srcRadius,
                    srcRawUv = srcRawUv,
                    srcColors = srcColors,
                    srcRect = srcRect,
                    srcMask = srcMask,
                    srcExtra = srcExtra,
                    srcOutline = srcOutline,
                    vertexCount = vertexCount,
                    isText = isText,
                    offsetX = offsetX,
                    offsetY = offsetY,
                    dst = dst,
                    dstVertexBase = dstVertexBase,
                };
                nowui_vg_pack_canvas_browser(&args);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Browser_nowui_vg_pack_render
        {
            public float* srcVerts;
            public float* srcUvs;
            public float* srcRadius;
            public float* srcRawUv;
            public float* srcColors;
            public float* srcRect;
            public float* srcMask;
            public float* srcExtra;
            public float* srcOutline;
            public int vertexCount;
            public float offsetX;
            public float offsetY;
            public float* dst;
            public int dstVertexBase;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe void nowui_vg_pack_render_browser(Browser_nowui_vg_pack_render* args);

        static unsafe void nowui_vg_pack_render(float* srcVerts, float* srcUvs, float* srcRadius, float* srcRawUv, float* srcColors, float* srcRect, float* srcMask, float* srcExtra, float* srcOutline, int vertexCount, float offsetX, float offsetY, float* dst, int dstVertexBase)
        {
            if (!OperatingSystem.IsBrowser())
            {
                nowui_vg_pack_render_native(srcVerts, srcUvs, srcRadius, srcRawUv, srcColors, srcRect, srcMask, srcExtra, srcOutline, vertexCount, offsetX, offsetY, dst, dstVertexBase);
                return;
            }
            {
                var args = new Browser_nowui_vg_pack_render
                {
                    srcVerts = srcVerts,
                    srcUvs = srcUvs,
                    srcRadius = srcRadius,
                    srcRawUv = srcRawUv,
                    srcColors = srcColors,
                    srcRect = srcRect,
                    srcMask = srcMask,
                    srcExtra = srcExtra,
                    srcOutline = srcOutline,
                    vertexCount = vertexCount,
                    offsetX = offsetX,
                    offsetY = offsetY,
                    dst = dst,
                    dstVertexBase = dstVertexBase,
                };
                nowui_vg_pack_render_browser(&args);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal unsafe struct Browser_nowui_vg_stroke
        {
            public float* contours;
            public int contourFloatCount;
            public int contourCount;
            public float* clipContours;
            public int clipFloatCount;
            public int clipContourCount;
            public int clipInvert;
            public float* paint;
            public int paintFloatCount;
            public float width;
            public int cap;
            public int join;
            public int hasTrim;
            public float trimStart;
            public float trimEnd;
            public float trimOffset;
            public int trimIndividual;
        }

        [DllImport("nowui-browser", CallingConvention = CallingConvention.Cdecl)]
        static extern unsafe int nowui_vg_stroke_browser(Browser_nowui_vg_stroke* args);

        static unsafe int nowui_vg_stroke(float[] contours, int contourFloatCount, int contourCount, float[] clipContours, int clipFloatCount, int clipContourCount, int clipInvert, float[] paint, int paintFloatCount, float width, int cap, int join, int hasTrim, float trimStart, float trimEnd, float trimOffset, int trimIndividual)
        {
            if (!OperatingSystem.IsBrowser())
            {
                return nowui_vg_stroke_native(contours, contourFloatCount, contourCount, clipContours, clipFloatCount, clipContourCount, clipInvert, paint, paintFloatCount, width, cap, join, hasTrim, trimStart, trimEnd, trimOffset, trimIndividual);
            }
            fixed (float* contoursPointer = contours)
            fixed (float* clipContoursPointer = clipContours)
            fixed (float* paintPointer = paint)
            {
                var args = new Browser_nowui_vg_stroke
                {
                    contours = contoursPointer,
                    contourFloatCount = contourFloatCount,
                    contourCount = contourCount,
                    clipContours = clipContoursPointer,
                    clipFloatCount = clipFloatCount,
                    clipContourCount = clipContourCount,
                    clipInvert = clipInvert,
                    paint = paintPointer,
                    paintFloatCount = paintFloatCount,
                    width = width,
                    cap = cap,
                    join = join,
                    hasTrim = hasTrim,
                    trimStart = trimStart,
                    trimEnd = trimEnd,
                    trimOffset = trimOffset,
                    trimIndividual = trimIndividual,
                };
                return nowui_vg_stroke_browser(&args);
            }
        }

    }
}
#endif
