#include "nowui_browser.h"

extern "C" void nowui_vg_blit_mesh_browser(Browser_nowui_vg_blit_mesh* a)
{
    nowui_vg_blit_mesh(a->srcPositions, a->srcColors, a->vertexCount, a->srcIndices, a->indexCount, a->positionScale, a->offsetX, a->offsetY, a->tint4, a->mask4, a->rect4, a->dstVerts, a->dstUvs, a->dstRawUv, a->dstRect, a->dstRadius, a->dstColor, a->dstOutline, a->dstExtra, a->dstMask, a->dstVertexBase, a->dstIndices, a->dstIndexBase, a->indexOffset);
}

extern "C" void nowui_vg_blit_text_run_browser(Browser_nowui_vg_blit_text_run* a)
{
    nowui_vg_blit_text_run(a->glyphs, a->start, a->end, a->x, a->y, a->fontSize, a->baseline, a->mask4, a->color4, a->outline4, a->outline, a->pixelRange, a->dstVerts, a->dstUvs, a->dstRawUv, a->dstRect, a->dstRadius, a->dstColor, a->dstOutline, a->dstExtra, a->dstMask, a->dstVertexBase, a->dstIndices, a->dstIndexBase, a->outPenX, a->outCounts, a->outBounds);
}

extern "C" void nowui_vg_pack_canvas_browser(Browser_nowui_vg_pack_canvas* a)
{
    nowui_vg_pack_canvas(a->srcVerts, a->srcUvs, a->srcRadius, a->srcRawUv, a->srcColors, a->srcRect, a->srcMask, a->srcExtra, a->srcOutline, a->vertexCount, a->isText, a->offsetX, a->offsetY, a->dst, a->dstVertexBase);
}

extern "C" void nowui_vg_pack_render_browser(Browser_nowui_vg_pack_render* a)
{
    nowui_vg_pack_render(a->srcVerts, a->srcUvs, a->srcRadius, a->srcRawUv, a->srcColors, a->srcRect, a->srcMask, a->srcExtra, a->srcOutline, a->vertexCount, a->offsetX, a->offsetY, a->dst, a->dstVertexBase);
}

extern "C" int nowui_vg_stroke_browser(Browser_nowui_vg_stroke* a)
{
    return nowui_vg_stroke(a->contours, a->contourFloatCount, a->contourCount, a->clipContours, a->clipFloatCount, a->clipContourCount, a->clipInvert, a->paint, a->paintFloatCount, a->width, a->cap, a->join, a->hasTrim, a->trimStart, a->trimEnd, a->trimOffset, a->trimIndividual);
}

extern "C" int nowui_compile_font_from_memory_with_codepoints_browser(Browser_nowui_compile_font_from_memory_with_codepoints* a)
{
    return nowui_compile_font_from_memory_with_codepoints(a->fontData, a->fontDataLength, a->size, a->pixelRange, reinterpret_cast<const unsigned int*>(a->codepoints), a->codepointCount, a->atlasRgba, a->atlasRgbaLength, a->glyphs, a->glyphCapacity, a->info, reinterpret_cast<char*>(a->errorBuffer), a->errorBufferLength);
}

// All fields occupy one wasm32 slot; fail maintenance builds on ABI drift.
#include <stddef.h>
#if defined(__wasm32__)
static_assert(offsetof(Browser_nowui_vg_blit_mesh, srcPositions) == 0, "Browser_nowui_vg_blit_mesh.srcPositions");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, srcColors) == 4, "Browser_nowui_vg_blit_mesh.srcColors");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, vertexCount) == 8, "Browser_nowui_vg_blit_mesh.vertexCount");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, srcIndices) == 12, "Browser_nowui_vg_blit_mesh.srcIndices");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, indexCount) == 16, "Browser_nowui_vg_blit_mesh.indexCount");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, positionScale) == 20, "Browser_nowui_vg_blit_mesh.positionScale");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, offsetX) == 24, "Browser_nowui_vg_blit_mesh.offsetX");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, offsetY) == 28, "Browser_nowui_vg_blit_mesh.offsetY");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, tint4) == 32, "Browser_nowui_vg_blit_mesh.tint4");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, mask4) == 36, "Browser_nowui_vg_blit_mesh.mask4");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, rect4) == 40, "Browser_nowui_vg_blit_mesh.rect4");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstVerts) == 44, "Browser_nowui_vg_blit_mesh.dstVerts");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstUvs) == 48, "Browser_nowui_vg_blit_mesh.dstUvs");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstRawUv) == 52, "Browser_nowui_vg_blit_mesh.dstRawUv");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstRect) == 56, "Browser_nowui_vg_blit_mesh.dstRect");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstRadius) == 60, "Browser_nowui_vg_blit_mesh.dstRadius");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstColor) == 64, "Browser_nowui_vg_blit_mesh.dstColor");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstOutline) == 68, "Browser_nowui_vg_blit_mesh.dstOutline");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstExtra) == 72, "Browser_nowui_vg_blit_mesh.dstExtra");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstMask) == 76, "Browser_nowui_vg_blit_mesh.dstMask");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstVertexBase) == 80, "Browser_nowui_vg_blit_mesh.dstVertexBase");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstIndices) == 84, "Browser_nowui_vg_blit_mesh.dstIndices");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, dstIndexBase) == 88, "Browser_nowui_vg_blit_mesh.dstIndexBase");
static_assert(offsetof(Browser_nowui_vg_blit_mesh, indexOffset) == 92, "Browser_nowui_vg_blit_mesh.indexOffset");
static_assert(sizeof(Browser_nowui_vg_blit_mesh) == 96, "Browser_nowui_vg_blit_mesh size");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, glyphs) == 0, "Browser_nowui_vg_blit_text_run.glyphs");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, start) == 4, "Browser_nowui_vg_blit_text_run.start");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, end) == 8, "Browser_nowui_vg_blit_text_run.end");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, x) == 12, "Browser_nowui_vg_blit_text_run.x");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, y) == 16, "Browser_nowui_vg_blit_text_run.y");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, fontSize) == 20, "Browser_nowui_vg_blit_text_run.fontSize");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, baseline) == 24, "Browser_nowui_vg_blit_text_run.baseline");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, mask4) == 28, "Browser_nowui_vg_blit_text_run.mask4");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, color4) == 32, "Browser_nowui_vg_blit_text_run.color4");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, outline4) == 36, "Browser_nowui_vg_blit_text_run.outline4");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, outline) == 40, "Browser_nowui_vg_blit_text_run.outline");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, pixelRange) == 44, "Browser_nowui_vg_blit_text_run.pixelRange");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstVerts) == 48, "Browser_nowui_vg_blit_text_run.dstVerts");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstUvs) == 52, "Browser_nowui_vg_blit_text_run.dstUvs");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstRawUv) == 56, "Browser_nowui_vg_blit_text_run.dstRawUv");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstRect) == 60, "Browser_nowui_vg_blit_text_run.dstRect");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstRadius) == 64, "Browser_nowui_vg_blit_text_run.dstRadius");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstColor) == 68, "Browser_nowui_vg_blit_text_run.dstColor");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstOutline) == 72, "Browser_nowui_vg_blit_text_run.dstOutline");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstExtra) == 76, "Browser_nowui_vg_blit_text_run.dstExtra");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstMask) == 80, "Browser_nowui_vg_blit_text_run.dstMask");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstVertexBase) == 84, "Browser_nowui_vg_blit_text_run.dstVertexBase");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstIndices) == 88, "Browser_nowui_vg_blit_text_run.dstIndices");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, dstIndexBase) == 92, "Browser_nowui_vg_blit_text_run.dstIndexBase");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, outPenX) == 96, "Browser_nowui_vg_blit_text_run.outPenX");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, outCounts) == 100, "Browser_nowui_vg_blit_text_run.outCounts");
static_assert(offsetof(Browser_nowui_vg_blit_text_run, outBounds) == 104, "Browser_nowui_vg_blit_text_run.outBounds");
static_assert(sizeof(Browser_nowui_vg_blit_text_run) == 108, "Browser_nowui_vg_blit_text_run size");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcVerts) == 0, "Browser_nowui_vg_pack_canvas.srcVerts");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcUvs) == 4, "Browser_nowui_vg_pack_canvas.srcUvs");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcRadius) == 8, "Browser_nowui_vg_pack_canvas.srcRadius");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcRawUv) == 12, "Browser_nowui_vg_pack_canvas.srcRawUv");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcColors) == 16, "Browser_nowui_vg_pack_canvas.srcColors");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcRect) == 20, "Browser_nowui_vg_pack_canvas.srcRect");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcMask) == 24, "Browser_nowui_vg_pack_canvas.srcMask");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcExtra) == 28, "Browser_nowui_vg_pack_canvas.srcExtra");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, srcOutline) == 32, "Browser_nowui_vg_pack_canvas.srcOutline");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, vertexCount) == 36, "Browser_nowui_vg_pack_canvas.vertexCount");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, isText) == 40, "Browser_nowui_vg_pack_canvas.isText");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, offsetX) == 44, "Browser_nowui_vg_pack_canvas.offsetX");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, offsetY) == 48, "Browser_nowui_vg_pack_canvas.offsetY");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, dst) == 52, "Browser_nowui_vg_pack_canvas.dst");
static_assert(offsetof(Browser_nowui_vg_pack_canvas, dstVertexBase) == 56, "Browser_nowui_vg_pack_canvas.dstVertexBase");
static_assert(sizeof(Browser_nowui_vg_pack_canvas) == 60, "Browser_nowui_vg_pack_canvas size");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcVerts) == 0, "Browser_nowui_vg_pack_render.srcVerts");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcUvs) == 4, "Browser_nowui_vg_pack_render.srcUvs");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcRadius) == 8, "Browser_nowui_vg_pack_render.srcRadius");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcRawUv) == 12, "Browser_nowui_vg_pack_render.srcRawUv");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcColors) == 16, "Browser_nowui_vg_pack_render.srcColors");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcRect) == 20, "Browser_nowui_vg_pack_render.srcRect");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcMask) == 24, "Browser_nowui_vg_pack_render.srcMask");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcExtra) == 28, "Browser_nowui_vg_pack_render.srcExtra");
static_assert(offsetof(Browser_nowui_vg_pack_render, srcOutline) == 32, "Browser_nowui_vg_pack_render.srcOutline");
static_assert(offsetof(Browser_nowui_vg_pack_render, vertexCount) == 36, "Browser_nowui_vg_pack_render.vertexCount");
static_assert(offsetof(Browser_nowui_vg_pack_render, offsetX) == 40, "Browser_nowui_vg_pack_render.offsetX");
static_assert(offsetof(Browser_nowui_vg_pack_render, offsetY) == 44, "Browser_nowui_vg_pack_render.offsetY");
static_assert(offsetof(Browser_nowui_vg_pack_render, dst) == 48, "Browser_nowui_vg_pack_render.dst");
static_assert(offsetof(Browser_nowui_vg_pack_render, dstVertexBase) == 52, "Browser_nowui_vg_pack_render.dstVertexBase");
static_assert(sizeof(Browser_nowui_vg_pack_render) == 56, "Browser_nowui_vg_pack_render size");
static_assert(offsetof(Browser_nowui_vg_stroke, contours) == 0, "Browser_nowui_vg_stroke.contours");
static_assert(offsetof(Browser_nowui_vg_stroke, contourFloatCount) == 4, "Browser_nowui_vg_stroke.contourFloatCount");
static_assert(offsetof(Browser_nowui_vg_stroke, contourCount) == 8, "Browser_nowui_vg_stroke.contourCount");
static_assert(offsetof(Browser_nowui_vg_stroke, clipContours) == 12, "Browser_nowui_vg_stroke.clipContours");
static_assert(offsetof(Browser_nowui_vg_stroke, clipFloatCount) == 16, "Browser_nowui_vg_stroke.clipFloatCount");
static_assert(offsetof(Browser_nowui_vg_stroke, clipContourCount) == 20, "Browser_nowui_vg_stroke.clipContourCount");
static_assert(offsetof(Browser_nowui_vg_stroke, clipInvert) == 24, "Browser_nowui_vg_stroke.clipInvert");
static_assert(offsetof(Browser_nowui_vg_stroke, paint) == 28, "Browser_nowui_vg_stroke.paint");
static_assert(offsetof(Browser_nowui_vg_stroke, paintFloatCount) == 32, "Browser_nowui_vg_stroke.paintFloatCount");
static_assert(offsetof(Browser_nowui_vg_stroke, width) == 36, "Browser_nowui_vg_stroke.width");
static_assert(offsetof(Browser_nowui_vg_stroke, cap) == 40, "Browser_nowui_vg_stroke.cap");
static_assert(offsetof(Browser_nowui_vg_stroke, join) == 44, "Browser_nowui_vg_stroke.join");
static_assert(offsetof(Browser_nowui_vg_stroke, hasTrim) == 48, "Browser_nowui_vg_stroke.hasTrim");
static_assert(offsetof(Browser_nowui_vg_stroke, trimStart) == 52, "Browser_nowui_vg_stroke.trimStart");
static_assert(offsetof(Browser_nowui_vg_stroke, trimEnd) == 56, "Browser_nowui_vg_stroke.trimEnd");
static_assert(offsetof(Browser_nowui_vg_stroke, trimOffset) == 60, "Browser_nowui_vg_stroke.trimOffset");
static_assert(offsetof(Browser_nowui_vg_stroke, trimIndividual) == 64, "Browser_nowui_vg_stroke.trimIndividual");
static_assert(sizeof(Browser_nowui_vg_stroke) == 68, "Browser_nowui_vg_stroke size");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, fontData) == 0, "Browser_nowui_compile_font_from_memory_with_codepoints.fontData");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, fontDataLength) == 4, "Browser_nowui_compile_font_from_memory_with_codepoints.fontDataLength");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, size) == 8, "Browser_nowui_compile_font_from_memory_with_codepoints.size");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, pixelRange) == 12, "Browser_nowui_compile_font_from_memory_with_codepoints.pixelRange");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, codepoints) == 16, "Browser_nowui_compile_font_from_memory_with_codepoints.codepoints");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, codepointCount) == 20, "Browser_nowui_compile_font_from_memory_with_codepoints.codepointCount");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, atlasRgba) == 24, "Browser_nowui_compile_font_from_memory_with_codepoints.atlasRgba");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, atlasRgbaLength) == 28, "Browser_nowui_compile_font_from_memory_with_codepoints.atlasRgbaLength");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, glyphs) == 32, "Browser_nowui_compile_font_from_memory_with_codepoints.glyphs");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, glyphCapacity) == 36, "Browser_nowui_compile_font_from_memory_with_codepoints.glyphCapacity");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, info) == 40, "Browser_nowui_compile_font_from_memory_with_codepoints.info");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, errorBuffer) == 44, "Browser_nowui_compile_font_from_memory_with_codepoints.errorBuffer");
static_assert(offsetof(Browser_nowui_compile_font_from_memory_with_codepoints, errorBufferLength) == 48, "Browser_nowui_compile_font_from_memory_with_codepoints.errorBufferLength");
static_assert(sizeof(Browser_nowui_compile_font_from_memory_with_codepoints) == 52, "Browser_nowui_compile_font_from_memory_with_codepoints size");
#endif
