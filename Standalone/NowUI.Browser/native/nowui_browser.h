// Compact native-call ABI for the .NET browser interpreter (maximum 12 integer arguments).
#pragma once
#include "nowui_vg.h"
#include "nowui_msdf.h"
extern "C" {
struct Browser_nowui_vg_blit_mesh {
    float* srcPositions;
    float* srcColors;
    int vertexCount;
    int* srcIndices;
    int indexCount;
    float positionScale;
    float offsetX;
    float offsetY;
    float* tint4;
    float* mask4;
    float* rect4;
    float* dstVerts;
    float* dstUvs;
    float* dstRawUv;
    float* dstRect;
    float* dstRadius;
    float* dstColor;
    float* dstOutline;
    float* dstExtra;
    float* dstMask;
    int dstVertexBase;
    int* dstIndices;
    int dstIndexBase;
    int indexOffset;
};
NOWUI_VG_EXPORT void nowui_vg_blit_mesh_browser(Browser_nowui_vg_blit_mesh* a);

struct Browser_nowui_vg_blit_text_run {
    float* glyphs;
    int start;
    int end;
    float x;
    float y;
    float fontSize;
    float baseline;
    float* mask4;
    float* color4;
    float* outline4;
    float outline;
    float pixelRange;
    float* dstVerts;
    float* dstUvs;
    float* dstRawUv;
    float* dstRect;
    float* dstRadius;
    float* dstColor;
    float* dstOutline;
    float* dstExtra;
    float* dstMask;
    int dstVertexBase;
    int* dstIndices;
    int dstIndexBase;
    float* outPenX;
    int* outCounts;
    float* outBounds;
};
NOWUI_VG_EXPORT void nowui_vg_blit_text_run_browser(Browser_nowui_vg_blit_text_run* a);

struct Browser_nowui_vg_pack_canvas {
    float* srcVerts;
    float* srcUvs;
    float* srcRadius;
    float* srcRawUv;
    float* srcColors;
    float* srcRect;
    float* srcMask;
    float* srcExtra;
    float* srcOutline;
    int vertexCount;
    int isText;
    float offsetX;
    float offsetY;
    float* dst;
    int dstVertexBase;
};
NOWUI_VG_EXPORT void nowui_vg_pack_canvas_browser(Browser_nowui_vg_pack_canvas* a);

struct Browser_nowui_vg_pack_render {
    float* srcVerts;
    float* srcUvs;
    float* srcRadius;
    float* srcRawUv;
    float* srcColors;
    float* srcRect;
    float* srcMask;
    float* srcExtra;
    float* srcOutline;
    int vertexCount;
    float offsetX;
    float offsetY;
    float* dst;
    int dstVertexBase;
};
NOWUI_VG_EXPORT void nowui_vg_pack_render_browser(Browser_nowui_vg_pack_render* a);

struct Browser_nowui_vg_stroke {
    float* contours;
    int contourFloatCount;
    int contourCount;
    float* clipContours;
    int clipFloatCount;
    int clipContourCount;
    int clipInvert;
    float* paint;
    int paintFloatCount;
    float width;
    int cap;
    int join;
    int hasTrim;
    float trimStart;
    float trimEnd;
    float trimOffset;
    int trimIndividual;
};
NOWUI_VG_EXPORT int nowui_vg_stroke_browser(Browser_nowui_vg_stroke* a);

struct Browser_nowui_compile_font_from_memory_with_codepoints {
    unsigned char* fontData;
    int fontDataLength;
    int size;
    int pixelRange;
    int* codepoints;
    int codepointCount;
    unsigned char* atlasRgba;
    int atlasRgbaLength;
    NowMsdfGlyph* glyphs;
    int glyphCapacity;
    NowMsdfAtlasInfo* info;
    unsigned char* errorBuffer;
    int errorBufferLength;
};
NOWUI_VG_EXPORT int nowui_compile_font_from_memory_with_codepoints_browser(Browser_nowui_compile_font_from_memory_with_codepoints* a);

}
