#version 300 es
// ===========================================================================
// nowui-sdf-image.vert
//
// GLSL ES 3.00 port of UnityCG.cginc's `vert_img`, the vertex stage all five
// passes of Assets/NowUI/Extensions/Sdf/NowSdfImageField.shader declare
// (`#pragma vertex vert_img`, :262, :272, :282, :292, :302).
//
// One vertex stage, five fragment stages. The .shader file says so directly:
// every Pass block names the same vertex entry point and differs only in its
// fragment entry point, so nowui-gl.js gives all five passes this source.
//
// IT DOES NOT APPLY _MainTex_ST, AND THAT IS THE POINT WORTH READING TWICE.
// UnityCG.cginc's vert_img is
//
//     v2f_img vert_img(appdata_img v) {
//         v2f_img o;
//         o.pos = UnityObjectToClipPos(v.vertex);
//         o.uv  = v.texcoord;            // NOT TRANSFORM_TEX
//         return o;
//     }
//
// -- the texture coordinate passes through untransformed. nowui-gl.js's own
// GLSL_VERTEX_BLIT *does* apply _MainTex_ST, because the blit contract carries
// the caller's scale/offset there. The two therefore differ, and this file is
// deliberately the faithful one.
//
// Nothing observable turns on the difference today, and that is a fact rather
// than a hope: NowSdfImageFields reaches this program only through
// Graphics.Blit(source, dest, mat, pass) (NowSdfImageField.cs:362, :456, :462,
// :470, :473, :474), and the shim's four-argument overload passes
// Vector2.one / Vector2.zero (Graphics.cs:91-94), i.e. _MainTex_ST = (1,1,0,0),
// the identity. If a caller ever blits this material with a scale, Unity would
// IGNORE it (vert_img has no TRANSFORM_TEX) and so does this file; a port that
// had copied GLSL_VERTEX_BLIT would silently have honoured it instead, which
// is a wrong result rather than a missing one.
//
// Concretely: _MainTex_ST is not declared below, so its uniform location comes
// back null and the backend's uniform bridge pushes nothing for it. The
// omission is structural, not a matter of remembering.
//
// PRECISION. Unity's v2f_img.uv is half2; this is highp. See the note in
// nowui-sdf-image.glsl on why that is the faithful choice for the desktop
// profile these passes were measured against, not a liberty.
// ===========================================================================

precision highp float;
precision highp int;

// The blit geometry contract (nowui-gl.js, "THE BLIT GEOMETRY CONTRACT"):
// attribute 0 is a vec3 in the unit square with z = 0, attribute 1 the
// matching vec2 in [0,1] with uv (0,0) at NDC (-1,-1). appdata_img declares
// exactly these two members and nothing else.
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;

// UnityObjectToClipPos(v.vertex). For a blit the backend uploads
// Ortho(0, 1, 0, 1, -1, 100) here, built with the shim's own Matrix4x4.
uniform mat4 nowui_MatrixMVP;

out highp vec2 vUv;

void main()
{
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);
    vUv = aUv;
}
