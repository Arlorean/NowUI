#version 300 es
// ===========================================================================
// nowui-glassblur.vert
//
// GLSL ES 3.00 port of the vertex stage of PASS 0 of
// Assets/NowUI/Assets/Shaders/UIGlassBlur.shader (shader
// "Hidden/NowUI/GlassBlur", 256 lines, FOUR passes). HLSL line references
// below are into that file.
//
// COMPOSITION: this file needs no include -- no colour space (the blur moves
// image data, it does not author colours) and no mask (the blur is a
// full-screen pass, not a UI element).
//
// ---------------------------------------------------------------------------
// PASS 0 IS THE ONLY PASS PORTED. The other three, and why.
// ---------------------------------------------------------------------------
//   PASS 1 (:48-113) the TEXTURE-ARRAY blur. NOT PORTED, because it is
//     unreachable rather than because it is impossible: GLSL ES 3.00 does have
//     sampler2DArray and WebGL2 does have TEXTURE_2D_ARRAY, so this one COULD
//     be written. It is selected by NowGlassRenderer.BlitBlur (:869-887) only
//     when `layout.isArray`, which comes from
//     NowGlassTextureLayout.FromDescriptor -- i.e. only when the capture
//     target's RenderTextureDescriptor says TextureDimension.Tex2DArray. That
//     is an XR eye texture. The browser host allocates no array targets and
//     has no XR path, so nothing can select it. It would also need the backend
//     to grow array render targets and per-slice framebuffer attachment, which
//     nothing else wants.
//
//   PASS 2 (:117-187) the MSAA texture-array resolve, and
//   PASS 3 (:192-254) the MSAA 2D resolve. NOT PORTABLE TO WebGL2 AT ALL.
//     Both read a multisampled texture directly -- `Texture2DMSArray<float4>`
//     and `Texture2DMS<float4>`, with .GetDimensions() and .Load(pixel, sample)
//     per sample index, behind `#pragma target 4.5` / `#pragma require msaatex`.
//     WebGL2 (GLSL ES 3.00) has NO sampler2DMS: multisampled sampler types and
//     texelFetch on them arrive in GLSL ES 3.10, which WebGL2 does not expose,
//     and no WebGL extension adds them. A WebGL2 multisampled RENDERBUFFER can
//     only be resolved with blitFramebuffer, never sampled, and it cannot be a
//     texture at all. This is a hard platform limit, not a porting decision.
//     It is also moot twice over: NowGlassRenderer.CopySource (:905-912)
//     reaches those passes only when `layout.sourceRequiresExplicitResolve`,
//     which needs both msaaSamples > 1 and bindMS -- an XR eye target again.
//
// So the reachable blur is exactly what BlitBlur's non-array arm does
// (NowGlassRenderer.cs:889-891): SetGlobalTexture(_NowBlurSourceTex, source)
// then Blit(source, destination, blurMaterial, 0). One pass, single-sampled
// 2D, applied separably by the C# in a horizontal/vertical pair per iteration.
//
// nowui-gl.js declares this program with a `passes` array of length ONE. Asking
// selectPass for 1, 2 or 3 therefore fails loudly with the pass count in the
// message, which is the behaviour wanted: an unreachable pass that silently
// drew pass 0 instead would be a blur of the wrong image.
//
// ---------------------------------------------------------------------------
// WHY THIS FILE EXISTS AT ALL, GIVEN vert_img -- AND WHY IT IS NOT THE
// gl_VertexID TRIANGLE THE FIRST DRAFT USED.
// ---------------------------------------------------------------------------
// Pass 0's HLSL declares `#pragma vertex vert_img` (:15) -- UnityCG's stock
// image vertex shader, which reads appdata_img (POSITION + TEXCOORD0) from the
// quad Blit supplies and does
//     o.pos = UnityObjectToClipPos(v.vertex);
//     o.uv  = MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord);
// There is no stock shader here, so it is written out.
//
// It is written to the BLIT GEOMETRY CONTRACT that nowui-gl.js's blit() path
// defines (see the comment above GLSL_VERTEX_BLIT in that file), NOT to the
// attributeless gl_VertexID triangle that UIGlassBlur's own passes 1-3 use:
//     attribute 0  aPosition  vec3 in the unit square, z = 0
//     attribute 1  aUv        vec2 in [0,1], uv (0,0) at NDC (-1,-1)
//     nowui_MatrixMVP         Ortho(0, 1, 0, 1, -1, 100)
//     _MainTex_ST             the blit's own (scale, offset)
//     attributes 2..8         NOT enabled
// The contract is the more faithful of the two, because it is what Unity's
// Blit actually does: a quad, an ortho, and a texcoord. Routing the blit's
// scale/offset through _MainTex_ST rather than baking it into the quad's UVs
// is the one difference from Unity, and it is exact -- UNITY_MATRIX_TEXTURE0
// is identity under Blit, and NowGlassRenderer only ever calls the four-argument
// Blit(source, destination, material, 0), whose scale/offset is the identity
// (1, 1, 0, 0). So `_MainTex_ST` contributes nothing here today and the whole
// source transform is _NowBlurSourceScaleOffset, applied in the fragment stage
// exactly where HLSL :27 applies it.
//
// UV ORIENTATION -- the one place a flip could creep in. Pass 1's vertex
// shader (:80-84) writes
//     #if UNITY_UV_STARTS_AT_TOP
//         uv = float2(positionUV.x, 1.0 - positionUV.y);
//     #else
//         uv = positionUV;
//     #endif
// UNITY_UV_STARTS_AT_TOP is defined for the D3D-style platforms whose render
// textures have a top-left origin. WebGL2 is OpenGL: the default drawing
// buffer and an FBO colour attachment share one bottom-left origin, so the
// #else arm is ours and the blit quad's uv is used as-is with no flip. That
// also matches Unity's own Blit on GL, which is what pass 0 is invoked
// through. Do not flip here, do not flip the render-texture upload, and do not
// set UNPACK_FLIP_Y_WEBGL (M2-ShaderPort.md section 8.4).
//
// PRECISION: highp. The blur's tap offsets are fractional texel multiples up
// to 7.38 texels out; at mediump those offsets quantise and the kernel stops
// being the bilinear-optimised Gaussian it is tuned to be.
// ===========================================================================

precision highp float;
precision highp int;

// ---------------------------------------------------------------------------
// The blit quad. Only locations 0 and 1 are enabled by nowui-gl.js's blitQuad
// VAO, so this program must declare only these two -- an attribute at 2..8
// would read the generic constant (0,0,0,1) rather than anything meaningful.
// ---------------------------------------------------------------------------
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;

// ---------------------------------------------------------------------------
// Uniforms, both supplied by the blit path rather than by the blur's own
// material: nowui_MatrixMVP is the unit-quad ortho and _MainTex_ST the blit's
// scale/offset. The blur's four real uniforms are all in the fragment stage.
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;
uniform highp vec4 _MainTex_ST;

// The single varying: the source UV, BEFORE _NowBlurSourceScaleOffset is
// applied. The fragment stage applies that, matching HLSL :27.
out highp vec2 vUv;

void main()
{
    // UnityObjectToClipPos(v.vertex), with the blit's unit-quad ortho.
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // MultiplyUV(UNITY_MATRIX_TEXTURE0, v.texcoord) with the blit's own
    // scale/offset folded in -- identity for every call NowGlassRenderer makes.
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;
}
