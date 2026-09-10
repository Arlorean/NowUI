#version 300 es
// ===========================================================================
// nowui-rectangle.vert
//
// GLSL ES 3.00 port of the `vert` function of
// Assets/NowUI/Assets/Shaders/UIRectangle.shader (shader "NowUI/UI Rectangle",
// 163 lines, single pass, render queue 3000). HLSL line references below are
// into that file.
//
// COMPOSITION: GLSL has no #include. The line
//     //#include "nowui-colorspace.glsl"
// is a marker the loader in nowui-glsl-include.js replaces with that file's
// text before calling gl.shaderSource. The marker must be a whole line whose
// only content is `//#include "<name>"` (leading whitespace allowed). To a
// plain GLSL compiler it is just a comment, so an editor or linter that reads
// this file unresolved sees valid-but-incomplete source rather than garbage.
//
// PRECISION: highp everywhere, deliberately. Unity's fixed4/half4 would map to
// lowp/mediump, and that is wrong here -- see nowui-rectangle.frag's header.
// The vertex language already defaults float and int to highp, but say it.
// ===========================================================================

precision highp float;
precision highp int;

//#include "nowui-colorspace.glsl"

// ---------------------------------------------------------------------------
// Vertex attributes. Locations are FIXED here (rather than left to the linker
// or to bindAttribLocation) so that one VAO layout serves both programs.
// They match M2-ShaderPort.md section 1.4 and the nine NowMeshData streams:
//
//   loc  name        stream (VertexAttribute)  floats  UIRectangle appdata
//   ---  ----------  -----------------------   ------  -------------------
//    0   aPosition   Position   (0)               3    vertex   : POSITION
//    1   aUv         TexCoord0  (4)               2    uv       : TEXCOORD0
//    2   aRect       TexCoord1  (5)               4    rect     : TEXCOORD1
//    3   aRadius     TexCoord2  (6)               4    radius   : TEXCOORD2
//    4   aColor      TexCoord3  (7)               4    color    : TEXCOORD3
//    5   aOutline    TexCoord4  (8)               4    outlineColor : TEXCOORD4
//    6   aExtras     TexCoord5  (9)               4    extras   : TEXCOORD5
//    7   aMask       TexCoord6  (10)              4    mask     : TEXCOORD6
//    8   aRawUV      TexCoord7  (11)              4    rawUV    : TEXCOORD7
//
// UIRectangle.shader:41-53 (struct appdata). The instancing macro on :52 is
// dropped: multi_compile_instancing is Unity variant bookkeeping and no NowUI
// call site in scope enables it (M2-ShaderPort.md section 2.0).
//
// aPosition is vec3 because the Position stream's elementSize is 12. z is
// always 0 (NowMesh.AddRect never sets it).
// ---------------------------------------------------------------------------
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

// ---------------------------------------------------------------------------
// Uniforms.
//
// nowui_MatrixMVP replaces UnityObjectToClipPos, which expands to
//   mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, v)).
// The backend computes projection * view * model on the C# side with the
// shim's Matrix4x4 and uploads one mat4 with
//   gl.uniformMatrix4fv(loc, /*transpose*/ false, floats)
// -- NO transpose and NO reordering: the shim declares its sixteen floats in
// column-major memory order, which is exactly what GLSL's mat4 wants, and
// Matrix4x4.Ortho already emits the OpenGL clip convention with the Y negation
// baked in. There is no flip anywhere in this pipeline
// (M2-ShaderPort.md sections 8.2 and 8.4).
//
// _MainTex_ST is TRANSFORM_TEX's (scaleX, scaleY, offsetX, offsetY). NowUI
// never writes it; the backend must fall back to (1, 1, 0, 0).
// ---------------------------------------------------------------------------
uniform highp mat4 nowui_MatrixMVP;
uniform highp vec4 _MainTex_ST;

// ---------------------------------------------------------------------------
// Varyings. Names and order match UIRectangle.shader:55-67 (struct v2f) with
// the TEXCOORDn semantics replaced by names; nowui-rectangle.frag declares the
// identical set as `in`.
// ---------------------------------------------------------------------------
out highp vec2 vUv;
out highp vec4 vRect;
out highp vec4 vRadius;
out highp vec4 vColor;
out highp vec4 vOutlineColor;
out highp vec4 vExtras;
out highp vec4 vMask;
out highp vec4 vRawUV;

void main()
{
    // UIRectangle.shader:87 -- UnityObjectToClipPos(v.vertex).
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // UIRectangle.shader:88 -- TRANSFORM_TEX(v.uv, _MainTex), which is
    // v.uv * _MainTex_ST.xy + _MainTex_ST.zw. Unity-convention (bottom-up) UVs;
    // do not flip them and do not flip the texture upload.
    vUv = aUv * _MainTex_ST.xy + _MainTex_ST.zw;

    // UIRectangle.shader:89-90 -- straight pass-through.
    vRect = aRect;
    vRadius = aRadius;

    // UIRectangle.shader:91-92 -- the ONLY transformation the vertex stage
    // applies to the data. Under Gamma this is the identity; the call is kept
    // so the linear branch remains one #define away. Alpha is untouched.
    vColor = NowUIColorToWorkingSpace(aColor);
    vOutlineColor = NowUIColorToWorkingSpace(aOutline);

    // UIRectangle.shader:93-95 -- pass-through.
    vExtras = aExtras;
    vMask = aMask;
    vRawUV = aRawUV;
}
