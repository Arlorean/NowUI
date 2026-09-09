/*
 * HarfBuzz stubs for the browser host.
 *
 * nowui-msdf.bc is built for Unity's WebGL player, where its 18 undefined HarfBuzz symbols resolve against
 * Unity's TextRenderingModule at player link time (Native/build-msdf-webgl.sh leaves them undefined on purpose).
 * This host has no HarfBuzz and needs none: every one of those symbols is reachable only from NowShaperState's
 * destructor and nowui_shaper_create / nowui_shaper_shape_utf16 / nowui_shaper_destroy, all of them inside
 * `#ifndef NOWUI_MSDF_NO_SHAPING` in Assets/NowUI/Plugins/Native/nowui-msdf/nowui_msdf.cpp. None of the eight
 * entry points NowFontCompiler imports reaches them; the three shaper exports survive --gc-sections only because
 * NOWUI_MSDF_EXPORT marks them __attribute__((used, visibility("default"))), which is a link-visibility artifact
 * rather than a call path.
 *
 * So these exist to give wasm-ld something to bind, not to provide shaping. Reaching one would mean the host had
 * started calling a shaping entry point it does not import - a bug - so they abort loudly rather than return a
 * plausible-looking value and corrupt a glyph run silently.
 *
 * The signatures need only agree in wasm ABI shape (pointer/i32), since wasm-ld type-checks calls, but they are
 * written against the real HarfBuzz prototypes so a later reader can see what was being stubbed.
 */
#include <stdlib.h>
#include <stdio.h>

/*
 * _Noreturn matters here beyond documentation: without it the compiler cannot see that the non-void stubs never
 * fall off the end, and every one of them raises -Wreturn-type. Ten warnings on a clean build is ten warnings
 * nobody reads.
 */
static _Noreturn void nowui_hb_unavailable(const char *name) {
    fprintf(stderr, "nowui-msdf: %s called, but HarfBuzz is not linked into this host.\n", name);
    abort();
}

#define NOWUI_HB_STUB(ret, name, args) \
    ret name args { nowui_hb_unavailable(#name); }

NOWUI_HB_STUB(void *, hb_blob_create, (const char *a, unsigned b, int c, void *d, void *e))
NOWUI_HB_STUB(void, hb_blob_destroy, (void *a))
NOWUI_HB_STUB(void *, hb_face_create, (void *a, unsigned b))
NOWUI_HB_STUB(void, hb_face_destroy, (void *a))
NOWUI_HB_STUB(unsigned, hb_face_get_glyph_count, (void *a))
NOWUI_HB_STUB(unsigned, hb_face_get_upem, (void *a))
NOWUI_HB_STUB(void *, hb_font_create, (void *a))
NOWUI_HB_STUB(void, hb_font_destroy, (void *a))
NOWUI_HB_STUB(void, hb_font_set_scale, (void *a, int b, int c))
NOWUI_HB_STUB(void *, hb_buffer_create, (void))
NOWUI_HB_STUB(void, hb_buffer_destroy, (void *a))
NOWUI_HB_STUB(void, hb_buffer_reset, (void *a))
NOWUI_HB_STUB(void, hb_buffer_add_utf16, (void *a, const unsigned short *b, int c, unsigned d, int e))
NOWUI_HB_STUB(void, hb_buffer_guess_segment_properties, (void *a))
NOWUI_HB_STUB(unsigned, hb_buffer_get_length, (void *a))
NOWUI_HB_STUB(void *, hb_buffer_get_glyph_infos, (void *a, unsigned *b))
NOWUI_HB_STUB(void *, hb_buffer_get_glyph_positions, (void *a, unsigned *b))
NOWUI_HB_STUB(void, hb_shape, (void *a, void *b, const void *c, unsigned d))
