#ifndef NOWUI_COLOR_SPACE_INCLUDED
#define NOWUI_COLOR_SPACE_INCLUDED

// ===========================================================================
// nowui-colorspace.glsl
//
// GLSL ES 3.00 port of Assets/NowUI/Assets/Shaders/NowUIColorSpace.cginc
// (31 lines, read in full). Every function below cites the HLSL line it came
// from so a reviewer can diff the two side by side.
//
// This file is an INCLUDE FRAGMENT, not a standalone shader: it carries no
// "#version" line. It is spliced into a .vert/.frag by the loader described in
// nowui-glsl-include.js, which replaces a line of the form
//     //#include "nowui-colorspace.glsl"
// with this file's text. The #ifndef guard above is belt-and-braces: the
// loader already includes each file at most once per program.
//
// Used by the VERTEX stage of both programs (M2-ShaderPort.md sections 3.3 and
// 4.3). The fragment stages never call it -- both shaders convert their vertex
// colours once, in the vertex shader, and interpolate the result.
//
// COLOUR SPACE: the Unity project this port matches runs in Gamma
// (ProjectSettings m_ActiveColorSpace: 0; materials.json "colorSpace": "Gamma";
// NowRuntime.colorSpace defaults to ColorSpace.Gamma). Unity therefore defines
// UNITY_COLORSPACE_GAMMA and NowUIColorToWorkingSpace is THE IDENTITY.
// The function is kept rather than inlined away precisely so that a later
// linear slice is one #define distant -- prepend
//     #define NOWUI_COLORSPACE_LINEAR
// after the #version line (the loader's `defines` option does this) and the
// linear branch comes back. See M2-ShaderPort.md section 6.3.
// ===========================================================================

// NowUIColorSpace.cginc:8-24
highp vec3 NowUIColorToWorkingSpace(highp vec3 color)
{
#ifdef NOWUI_COLORSPACE_LINEAR
    // cginc:16-22 -- the #else branch, UnityUI.cginc's piecewise sRGB->linear
    // approximation. Chosen over UnityCG's cubic because the cubic loses
    // display-code values in the dark UI range (cginc:13-15).
    highp vec3 value = color;
    highp vec3 low  = 0.0849710 * value - 0.000163029;
    highp vec3 high = value * (value * (value * 0.265885 + 0.736584) - 0.00980184)
                    + 0.00319697;
    // HLSL `(value < split) ? low : high` is a per-component select on a
    // half3 condition. GLSL's ternary demands a SCALAR bool, so the
    // component-wise form is mix(x, y, bvec) -- which returns y where the
    // bool is true, hence the swapped argument order versus the HLSL.
    return mix(high, low, lessThan(value, vec3(0.0725490)));
#else
    // cginc:11 -- UNITY_COLORSPACE_GAMMA: the working space already is the
    // authoring space, so nothing happens. This is the live branch.
    return color;
#endif
}

// NowUIColorSpace.cginc:26-29. Alpha is never converted, in either branch.
highp vec4 NowUIColorToWorkingSpace(highp vec4 color)
{
    return vec4(NowUIColorToWorkingSpace(color.rgb), color.a);
}

#endif // NOWUI_COLOR_SPACE_INCLUDED
