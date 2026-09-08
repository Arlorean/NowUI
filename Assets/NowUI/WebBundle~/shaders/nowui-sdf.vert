#version 300 es
// ===========================================================================
// nowui-sdf.vert
//
// GLSL ES 3.00 port of `vert` from
// Assets/NowUI/Extensions/Sdf/NowSdfShaderV2.cginc:1494-1519, the vertex half
// of shader "NowUI/SDF Scene" (Assets/NowUI/Extensions/Sdf/NowSdf.shader).
//
// WHICH cginc. NowSdf.shader:80 includes NowSdfShaderV2.cginc and nothing
// else, `NowSdf.MaterialAbiVersion` is 2 (NowSdf.cs:2350) and the exported
// SdfMaterial fixture declares `_NowSdfAbiVersion = 2`. NowSdfShaderV1.cginc
// is dead for every material this host can build; it survives only for
// project-authored templates that declare ABI 1, which this port does not
// implement and the backend refuses out loud rather than mis-rendering.
//
// COMPOSITION: this file includes nowui-colorspace.glsl -- but see the note
// below on why it is NOT called. nowui-glsl-include.js describes the splice.
//
// THE VERTEX LAYOUT IS THE SHARED NINE-STREAM ONE. NowSdf never builds a mesh
// of its own: NowSdf.cs:3898 calls Now.DrawSdf, which fills one NowRectVertex
// and calls NowMesh.AddRect (Now.cs:2508-2557), the same routine every other
// NowUI primitive goes through. So the attribute table below is the table in
// M2-ShaderPort.md section 1.1, and one VAO layout still serves every program.
// What differs is only what three of the channels MEAN here:
//
//   aColor   (TEXCOORD3)  the scene TINT, ApplyColorMultiplier(color)
//   aExtras  (TEXCOORD5)  the SCENE MAPPING (authoredW, authoredH, dirX, dirY)
//   aRadius  (TEXCOORD2)  unused on this path (canvas layout only, see below)
//
// `appdata` in the cginc also declares `float4 canvasColor : COLOR`, `normal`
// and `tangent`. None of the three exists in NowUI's render layout
// (NowMesh.RenderVertexLayout, nine Float32 attributes, no COLOR, no NORMAL,
// no TANGENT); they are there for the UGUI graphic path, which the standalone
// build excludes. See the canvas-layout block below for how that is handled
// rather than ignored.
// ===========================================================================

precision highp float;
precision highp int;

// M2-ShaderPort.md section 1.4. Locations are declared, not left to the
// linker, so the one VAO the backend builds serves every ported program.
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec2 aUv;
layout(location = 2) in vec4 aRect;
layout(location = 3) in vec4 aRadius;
layout(location = 4) in vec4 aColor;
layout(location = 5) in vec4 aOutline;
layout(location = 6) in vec4 aExtras;
layout(location = 7) in vec4 aMask;
layout(location = 8) in vec4 aRawUV;

//#include "nowui-colorspace.glsl"

// UnityObjectToClipPos(v.vertex). The backend multiplies projection * view *
// model on the CPU, so this is one uniform and one line (M2-ShaderPort.md
// section 6).
uniform mat4 nowui_MatrixMVP;

// NowSdfShaderV2.cginc:53. The immediate path and the UGUI canvas path pack
// the same payload into different vertex channels, and this float selects
// between them. NowSdfCache.Upload writes 0f UNCONDITIONALLY
// (NowSdf.cs:4877), and the only writer of 1 is NowSdfGraphic, which is a
// UGUI MonoBehaviour excluded from NowUI.Runtime.csproj. It is therefore a
// hard constant 0 in this build -- and it is still bridged and still tested,
// because a silently-ignored layout switch renders a plausible wrong scene.
uniform float _NowCanvasLayout;

// v2f, minus two members. See the two omissions documented under main().
out highp vec2 vRawUV;
out highp vec4 vRect;
out highp vec4 vMask;
out highp vec4 vTint;
out highp vec4 vSceneMapping;

// The COLOR attribute the canvas path reads does not exist in NowUI's render
// vertex layout, so there is nothing to bind it to. Rather than substitute
// white -- which would make a canvas-layout draw render a plausible, wrongly
// tinted scene -- the unreachable branch selects magenta, so that if
// _NowCanvasLayout ever becomes 1 the result is unmistakably a port gap and
// not a colour bug. This is the same choice, for the same reason, that
// nowui-glass.frag makes for its two unported branches, and it is
// unconditional rather than behind a #define for the reason recorded in
// nowui-glsl-include.js point 4: a marker inside #ifdef makes the uniform it
// guards write-only, the compiler eliminates it, and the assertion vanishes in
// exactly the build it was meant to guard.
const vec4 NOWUI_SDF_CANVAS_COLOR_MARKER = vec4(1.0, 0.0, 1.0, 1.0);

void main()
{
    // NowSdfShaderV2.cginc:1499.
    gl_Position = nowui_MatrixMVP * vec4(aPosition, 1.0);

    // :1501-1508. lerp with a step()-derived 0/1 selector, kept verbatim
    // rather than turned into an `if`, so the two layouts stay visibly the
    // same expression the HLSL pairs them in.
    float isCanvas = step(0.5, _NowCanvasLayout);

    // v.data7.xy is TEXCOORD7 (rawUV); v.uv.xy is TEXCOORD0.
    vRawUV = mix(aRawUV.xy, aUv.xy, isCanvas);

    vRect = aRect;

    // v.data6 is TEXCOORD6 (the legacy clip rect); v.data2 is TEXCOORD2.
    vMask = mix(aMask, aRadius, isCanvas);

    // v.data3 is TEXCOORD3, the tint. NOTE: NowUIColorToWorkingSpace is NOT
    // applied here, and that is the cginc's own choice, not an omission --
    // NowSdfShaderV2.cginc:1506 passes v.data3 through untouched while
    // UIRectangle.shader:105 converts its equivalent. Under Gamma the
    // conversion is the identity so the two agree today; writing the call in
    // "for consistency" would make them disagree under linear, silently. The
    // colour-space include is spliced in above so the difference is visible as
    // an unused function rather than as an absent one.
    vTint = mix(aColor, NOWUI_SDF_CANVAS_COLOR_MARKER, isCanvas);

    // :1511. Immediate meshes carry the SDF scene mapping in UV5; canvas
    // meshes repack it into UV3 because UGUI exposes fewer channels.
    // Now.DrawSdf fills it with (authoredWidth, authoredHeight, signX, signY)
    // (Now.cs:2551-2556).
    vSceneMapping = mix(aExtras, aColor, isCanvas);

    // TWO OMISSIONS, both deliberate.
    //
    // 1. `o.uiMask` (:1513-1518) and the `pixelSize` arithmetic that feeds it.
    //    It is read ONLY inside `#ifdef UNITY_UI_CLIP_RECT`, and NowSdf.shader
    //    declares that keyword with `#pragma multi_compile_local _
    //    UNITY_UI_CLIP_RECT` -- the `_` variant, with the keyword OFF, is the
    //    one every material this host builds selects (the fixture material's
    //    `keywords` array is empty, and the only code that would enable it is
    //    UGUI's Mask component). Porting it would also require _ScreenParams
    //    and UNITY_MATRIX_P, neither of which the backend supplies, to compute
    //    a value nothing reads. If UGUI clipping is ever needed, this is the
    //    line to restore, together with the fragment's clip block.
    //
    // 2. The instancing and stereo macros (UNITY_SETUP_INSTANCE_ID,
    //    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO). No-ops here, exactly as
    //    M2-ShaderPort.md section 2.0 says of the other programs.
}
