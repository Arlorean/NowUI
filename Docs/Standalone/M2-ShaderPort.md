# M2 shader port specification: UIRectangle and TxtRenderer on WebGL2

Written 2026-09-07 for Milestone 2, slice 1: the README quick-start scene (a rounded translucent panel plus the text
`Score: 1200`) drawn by real NowUI code through WebGL2. This is the document the backend and host units build from.
Nothing here was guessed; every claim cites the file it came from.

Scope discipline. Only two shader programs are in scope, `NowUI/UI Rectangle` and `NowUI/Text Renderer`, one pass each.
Glass, gradients (the rectangle kind), ripple, bezier, colour picker and SDF are out. Where the two in-scope shaders
contain machinery that the quick-start scene provably never reaches (§3.6, §4.5), this document says so, states the
guard condition that makes stubbing it safe, and says what to assert so a stub cannot rot into a silent wrong result.

Sources read in full: `Assets/NowUI/Assets/Shaders/UIRectangle.shader`, `TxtRenderer.shader`, `NowUIMask.cginc`,
`NowUIColorSpace.cginc`, `NowUITextGradient.cginc`; `Standalone/NowUI.Engine/Graphics/Mesh.cs`, `Material.cs`,
`NowMeshData.cs`, `NowMaterialBag.cs`, `NowShaderGlobals.cs`, `NowShaderInfo.cs`, `Backend/INowRenderBackend.cs`;
`Assets/NowUI/Runtime/NowMesh.cs`, `Now.cs`, `NowRenderer.cs`, `NowMaskShader.cs`, `NowFont.cs`, `NowGradient.cs`;
`Standalone/Tests/Fixtures/shaders.json` and `materials.json`.

---

## 0. The five things most likely to be got wrong

Read these first; the rest of the document is the detail behind them.

1. **The interleaved vertex path never runs in this build.** It is gated on `NowLottieNative.packRenderAvailable`,
   which is a hard `false` under `NOWUI_VG_DISABLE_NATIVE` — which `NowUI.Runtime.csproj` defines. Implement the
   nine-stream path (§1).
2. **Nothing needs flipping.** The projection NowUI emits already carries the Y negation, and GL clip space is the
   convention that matrix was written for. Upload it verbatim, `gl.viewport(0,0,w,h)`, no flip anywhere (§6).
3. **Gamma means "do nothing", but you have to actively do nothing.** No `SRGB8_ALPHA8` internal formats, no
   `EXT_sRGB` framebuffer, no `UNPACK_COLORSPACE_CONVERSION_WEBGL`, and `NowUIColorToWorkingSpace` compiles to the
   identity function (§5).
4. **Both shaders output premultiplied alpha and blend `ONE, ONE_MINUS_SRC_ALPHA`.** Get the canvas context
   (`alpha:false`) and the blend func right or the panel's translucency will be wrong in a way that looks almost right
   (§2.3).
5. **The text atlas is packed SDF16 in this build, not median-RGB MSDF.** `_NowUITextSdfEncoding` resolves to `1`
   because `TxtMaterial` declares the property (§4.2, §4.4). Taking the median branch renders text, but blurry and
   subtly wrong — the failure mode that looks like "close enough".

---

## 1. Vertex layout

### 1.1 What NowUI emits

`NowMesh.RenderVertexLayout` (`Assets/NowUI/Runtime/NowMesh.cs:203-214`) declares nine Float32 attributes:

| # | Unity semantic | HLSL semantic | Components | Carries |
|---|---|---|---|---|
| 0 | `Position` | `POSITION` | 3 | Quad corner in NowUI mesh space (see §1.3). `z` is always 0. |
| 1 | `TexCoord0` | `TEXCOORD0` | 2 | Texture UV, Unity convention (bottom-up). Atlas sub-rect for sprites/glyphs. |
| 2 | `TexCoord1` | `TEXCOORD1` | 4 | `rect` — the shape rectangle `(x, y, width, height)` in mesh space. |
| 3 | `TexCoord2` | `TEXCOORD2` | 4 | `radius` — corner radii packed `(TR, BR, TL, BL)`. For text: gradient payload `xyz`. |
| 4 | `TexCoord3` | `TEXCOORD3` | 4 | `color` — RGBA fill, display/sRGB encoded, **not** premultiplied. |
| 5 | `TexCoord4` | `TEXCOORD4` | 4 | `outlineColor` — RGBA outline, display/sRGB encoded, not premultiplied. |
| 6 | `TexCoord5` | `TEXCOORD5` | 4 | `extras` — per-shader scalars (§3.1, §4.1). |
| 7 | `TexCoord6` | `TEXCOORD6` | 4 | `mask` — the legacy clip rect `(x, y, w, h)` in **UI** coordinates (y down, positive). |
| 8 | `TexCoord7` | `TEXCOORD7` | 4 | `rawUV` — the un-atlased quad coordinate. `xy` used, `zw` unused. |

Nine attributes. WebGL2 guarantees `MAX_VERTEX_ATTRIBS >= 16`, so there is headroom; no packing tricks are needed and
none should be invented.

The declared `RenderVertexLayout` above is the **interleaved** description, mirrored by
`NowUI.Engine`'s `VertexAttributeDescriptor` table. It is what you would get if the interleaved path ran. It does not.

### 1.2 Which path actually runs: the nine streams, not the interleaved buffer

Both upload sites choose the same way:

- `Now.cs:1784` — `bool useNativeRenderPacking = layout == NowMeshLayout.Render && NowLottieNative.packRenderAvailable;`
- `NowMesh.cs:1616-1618` — `TryAppendNativeRenderVertices` returns `false` immediately `if (!NowLottieNative.packRenderAvailable)`.

`NowLottieNative.packRenderAvailable` has two definitions (`Assets/NowUI/Runtime/Lottie/NowLottieNative.cs:42` and
`:219`), separated by `#if NOWUI_VG_DISABLE_NATIVE`. `Standalone/NowUI.Runtime/NowUI.Runtime.csproj` puts
`NOWUI_VG_DISABLE_NATIVE` in `DefineConstants`. So in the standalone build `packRenderAvailable` is the constant
`false`, `useNativeRenderPacking` is always false, and both sites take the stream branch:

```
SetVertices(_verts,   0, n, ...)   →  streams[VertexAttribute.Position]   elementSize 12  (Vector3)
SetUVs(0, _uvs,       0, n, ...)   →  streams[VertexAttribute.TexCoord0]  elementSize  8  (Vector2)
SetUVs(1, _rect,      0, n, ...)   →  streams[VertexAttribute.TexCoord1]  elementSize 16  (Vector4)
SetUVs(2, _radius,    0, n, ...)   →  streams[VertexAttribute.TexCoord2]  elementSize 16
SetUVs(3, _color,     0, n, ...)   →  streams[VertexAttribute.TexCoord3]  elementSize 16
SetUVs(4, _outline,   0, n, ...)   →  streams[VertexAttribute.TexCoord4]  elementSize 16
SetUVs(5, _extra,     0, n, ...)   →  streams[VertexAttribute.TexCoord5]  elementSize 16
SetUVs(6, _mask,      0, n, ...)   →  streams[VertexAttribute.TexCoord6]  elementSize 16
SetUVs(7, _rawuv,     0, n, ...)   →  streams[VertexAttribute.TexCoord7]  elementSize 16
```

(`Now.cs:1898-1906`; `NowMesh.cs:1983-1991`.)

The `NowMeshData.streams` array is indexed by the `VertexAttribute` enum value
(`Standalone/NowUI.Engine/Enums/Rendering.cs:67-83`): `Position = 0`, `TexCoord0 = 4` … `TexCoord7 = 11`. Each
`NowMeshStream` is `{ byte[] bytes; int elementSize; int count; }`, tightly packed, no padding, no stride other than
`elementSize` (`Mesh.cs` `WriteStream`, `TryLocateChannel`). `bytes.Length` is capacity, `count` is what is live —
never read past `count * elementSize`.

**Decision: implement the nine-stream path.** Reasons, in order:

1. It is the only path this build can take. Implementing the interleaved path first would be implementing dead code
   and would leave the live path untested.
2. It maps cleanly onto WebGL2 with either of two shapes (§1.4), both of which are one `bufferData` per stream or one
   packed `bufferData` total.
3. `data.interleaved` is nevertheless a field the backend can read. **Read it and fail loudly** (a console error naming
   the mesh) if it is ever `true`, rather than silently reading `vertexBytes` as if it were a stream. If a future build
   links the native plugin, that assert is the thing that tells you rather than a garbled frame.

Index data is separate and independent of this choice: `SetIndexBufferParams` + `SetIndexBufferData`, `UInt16` below
65536 vertices and `UInt32` above (`Now.cs:1909`, `NowMesh.cs:1993`). The quick-start scene is far below the threshold,
so expect `UNSIGNED_SHORT`; handle both, the branch is two lines.

### 1.3 Coordinate conventions inside the vertex data

This is the part that produces upside-down or corner-swapped output when got wrong. Derived from
`NowMesh.AddRect` (`NowMesh.cs:417-580`) and `NowRectVertex.IsOutsideMask` (`NowMesh.cs:172-179`).

`NowRectVertex.position` is `(x, y, w, h)` and goes into `TEXCOORD1` unchanged for all four vertices. The four corners
emitted from it are, in order:

```
A = (x,     y    )   rawUV (0,0)
B = (x,     y + h)   rawUV (0,1)
C = (x + w, y + h)   rawUV (1,1)
D = (x + w, y    )   rawUV (1,0)
```

with triangles `A B C` and `A C D` (`NowMesh.cs:568-576`).

Mesh-space `y` relates to UI-space `y` by **negation**: `IsOutsideMask` compares `-rect.y` against the UI-space mask,
and both fragment shaders recover UI coordinates as `uiPosition = float2(pos.x, -pos.y)`. Concretely, a rect at UI
top-left `(uiX, uiY)` with size `(w, h)` is emitted with `rect.xy = (uiX, -(uiY + h))`, `rect.zw = (w, h)` — i.e.
**`rect.xy` is the UI bottom-left corner, expressed in negated-y mesh space, and `rect.zw` is always positive.**

Consequences the implementer must hold on to:

- `rawUV.y == 0` is the **UI bottom** edge of the quad; `rawUV.y == 1` is the UI top.
- Therefore in the rectangle SDF, `position = (rawUV.xy - 0.5) * size` has `+y` pointing **up the screen**, which is
  why `sdRoundedBox`'s `(p.y > 0) ? r.x : r.y` picks the *top* radius from `radius.x` — matching the documented
  `(TR, BR, TL, BL)` packing in `NowUIMask.cginc:12-13`.
- `TEXCOORD0` (`uv`) is `uvwh.xy + rawUV * uvwh.zw` (`NowMesh.cs:546-549`), i.e. ordinary Unity bottom-up texture UVs.
  Because Unity's raw texture data is also bottom-up and `INowRenderBackend.UploadTexture2D` documents its `dirtyRect`
  as "bottom-up texel space (row 0 = bottom)", uploading with `UNPACK_FLIP_Y_WEBGL = false` (the default) makes UVs
  line up with no correction. Do not flip.
- When geometry padding is in play (`geometryPadding > 0`, used to give blur/outline room) the quad grows outward and
  `rawUV` goes *outside* `[0,1]`: `leftRaw = -padding/w`, `rightRaw = 1 + padding/w`, etc. (`NowMesh.cs:530-537`). The
  SDF handles this correctly by construction. Do **not** clamp or saturate `rawUV` anywhere.

### 1.4 Mapping the streams onto WebGL2

Two shapes work; pick one and be consistent.

**(a) Nine `ARRAY_BUFFER`s, one per stream.** Straightest translation. Each stream's `bytes[0 .. count*elementSize)`
goes to its own buffer with `bufferData(ARRAY_BUFFER, view, DYNAMIC_DRAW)`, and each attribute gets
`vertexAttribPointer(loc, size, FLOAT, false, 0, 0)` with `size` = `elementSize / 4` (3, 2, 4, 4, 4, 4, 4, 4, 4). Nine
buffer objects per mesh, one VAO per mesh.

**(b) One interleaved buffer the backend packs itself.** Walk the nine streams into a single `Float32Array` of stride
`3 + 2 + 4·7 = 33` floats (132 bytes — the same stride `NowRenderVertex` would have had) and use one `bufferData` plus
nine `vertexAttribPointer` calls with the computed offsets. Fewer GL objects, one extra CPU pass.

For slice 1, (a) is recommended: it has no packing arithmetic to get wrong, and the quick-start scene is two meshes of
a handful of quads. Revisit under measurement, not on principle.

Attribute locations: bind them explicitly with `bindAttribLocation` before linking (or `layout(location=N)` in the
GLSL ES 3.00 source) so the mapping is fixed rather than driver-assigned. Suggested, matching the table in §1.1:

```
0 aPosition   vec3
1 aUv         vec2
2 aRect       vec4
3 aRadius     vec4
4 aColor      vec4
5 aOutline    vec4
6 aExtras     vec4
7 aMask       vec4
8 aRawUV      vec4
```

Use the *same* locations for both programs so one VAO layout serves both.

### 1.5 Reading the mesh from outside `NowUI.Engine`

`Mesh.data` (`Mesh.cs:40`), `Mesh.backendId` (`:43`) and `Mesh.version` (`:48`) are `internal`, and
`NowUI.Engine.csproj` grants `InternalsVisibleTo` only to `NowUI.Engine.Tests` and `Tests`. A backend living in
`Standalone/Web/NowUI.Web` therefore cannot touch them.

It does not have to. The public read-back API covers everything slice 1 needs:

```csharp
mesh.vertexCount, mesh.subMeshCount, mesh.GetSubMesh(i)
mesh.GetVertices(List<Vector3>)
mesh.GetUVs(channel, List<Vector4>)   // and the Vector2 overload for channel 0
mesh.GetTriangles(List<int>, subMesh)
```

`TryLocateChannel` de-interleaves transparently, so these getters work in both storage shapes — which also means the
backend written against them keeps working if the interleaved path ever comes back. The cost is a copy into managed
lists per upload. **This is acceptable for slice 1 and should be measured, not assumed away.**

The one thing the public API does not give you is `Mesh.version`, so a backend built this way cannot cheaply skip a
re-upload of an unchanged mesh. NowUI clears and refills its meshes every frame anyway, so for slice 1 the skip would
almost never fire. If profiling later says otherwise, the minimal shim change is adding
`<InternalsVisibleTo Include="NowUI.Web" />` to `NowUI.Engine.csproj` — that is a shim edit, so per the workflow rules
it belongs to the backend unit and must be reported as a finding, not made quietly.

---

## 2. Per-shader contract

### 2.0 Shared preamble

Both programs are `#pragma target 3.0`, single pass, and both `#include "UnityCG.cginc"`, `"NowUIColorSpace.cginc"`
and `"NowUIMask.cginc"` (TxtRenderer additionally includes `"NowUITextGradient.cginc"`).

Everything they use from `UnityCG.cginc` is:

- `UnityObjectToClipPos(v.vertex)` = `mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, v))`. With model and view both
  identity in slice 1 (the recorded frame shows `model=identity`, `view=identity`), this is `projection * vertex`.
- `TRANSFORM_TEX(v.uv, _MainTex)` = `v.uv * _MainTex_ST.xy + _MainTex_ST.zw`.
- The instancing / stereo macros (`UNITY_VERTEX_INPUT_INSTANCE_ID`, `UNITY_SETUP_INSTANCE_ID`,
  `UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX`, `UNITY_VERTEX_OUTPUT_STEREO`).
  All no-ops here. **Drop them; do not attempt an instancing path.** The `INSTANCING_ON` keyword listed in
  `shaders.json` for both shaders is Unity's variant bookkeeping and is not enabled by any NowUI call site in scope.

`fixed4` / `half4` / `half3` are Unity's legacy precision aliases. In GLSL ES 3.00 they would map to `lowp`/`mediump`.
**Do not.** Declare `precision highp float;` and use `highp` throughout. Reasons: the rounded-box SDF differences
(`ddx(dist)`, `ddy(dist)`) lose their meaning at `mediump`, and the text shader's packed-SDF16 reconstruction
(`msd.r * 256.0 + msd.b`) is exactly the arithmetic `mediump` destroys.

### 2.1 Render state — identical for both

Declared in the `SubShader` block of each `.shader` file:

```
Cull Off
Lighting Off        // fixed-function; no meaning here
ZWrite Off
ZTest [_ZTest]      // _ZTest defaults to 8 = CompareFunction.Always
Blend One OneMinusSrcAlpha
Tags { Queue=Transparent RenderType=Transparent IgnoreProjector=True }
```

`_ZTest`'s default is `8` in both `shaders.json` entries **and** in `UIMaterial.mat` / `TxtMaterial.mat`
(`materials.json`). `8` is `UnityEngine.Rendering.CompareFunction.Always`. Nothing in the standalone build writes it
— the only setter is `NowWorldGraphic.cs:69`, which is excluded from `NowUI.Runtime.csproj`.

WebGL2 state for a draw with either program:

```js
gl.disable(gl.DEPTH_TEST);          // ZTest Always + ZWrite Off ⇒ depth is irrelevant
gl.depthMask(false);
gl.disable(gl.CULL_FACE);           // Cull Off
gl.disable(gl.STENCIL_TEST);
gl.disable(gl.SCISSOR_TEST);
gl.disable(gl.DITHER);              // ON by default in GL; Unity does not dither UI. Turn it off.
gl.colorMask(true, true, true, true);
gl.enable(gl.BLEND);
gl.blendEquation(gl.FUNC_ADD);
gl.blendFunc(gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
```

`Cull Off` matters more than it looks: the projection negates Y (§6), which reverses the apparent winding of NowUI's
triangles. With culling disabled that is a non-issue; with culling enabled you would get an empty screen and no error.

`_ZTest` is **render state, not a GLSL uniform.** Read it from the material's property bag and translate it if you ever
support anything but `Always`; never bind it to a uniform location.

### 2.2 Depth buffer

Neither shader writes or tests depth. Request the context with `depth: false` and never allocate a depth attachment.
The projection still produces a well-defined `gl_Position.z` (§6.3) — it is simply ignored.

### 2.3 Canvas context and blending

Both fragment shaders emit **premultiplied** colour (`UIRectangle` builds `col.rgb` already multiplied by coverage;
`TxtRenderer` ends with the explicit `color.rgb *= color.a`). Combined with `Blend One OneMinusSrcAlpha` that is the
standard premultiplied source-over composite.

Recommended context:

```js
canvas.getContext('webgl2', {
  alpha: false,               // opaque canvas; browser compositing stays out of it
  depth: false,
  stencil: false,
  antialias: false,           // NowUI does its own analytic AA; MSAA adds nothing and costs
  premultipliedAlpha: false,  // moot while alpha:false, but say what you mean
  preserveDrawingBuffer: false,
  powerPreference: 'high-performance',
})
```

With `alpha: false` the drawing buffer's alpha is forced to 1 at composite time, so the premultiplied output composites
against the cleared background inside GL and the browser never re-interprets it. If a later slice needs a transparent
page background, switch to `alpha: true` **and** `premultipliedAlpha: true` together — those two must move as a pair.

Clear once per frame with an opaque colour before the first draw. `ClearRenderTarget(clearColor: true, ...)` is on the
backend interface; slice 1's host may also just clear at `BeginFrame`.

---

## 3. `NowUI/UI Rectangle`

`Assets/NowUI/Assets/Shaders/UIRectangle.shader`, 163 lines, one pass, render queue 3000.

### 3.1 Vertex inputs consumed

All nine. `POSITION`, `TEXCOORD0..TEXCOORD7`, exactly as §1.1. `extras` (`TEXCOORD5`) is read as:

| Component | Name | Meaning |
|---|---|---|
| `.x` | `blur` | Extra softness added to the outer edge of the AA band, in local units. `0` for a crisp edge. |
| `.y` | `outline` | Outline width in local units. `0` disables the outline entirely (an exact `== 0` test). |
| `.z` | — | Unused by this shader. |
| `.w` | — | Unused by this shader. |

`rawUV.zw` is unused. `radius` is `(TR, BR, TL, BL)`.

### 3.2 Uniforms

| GLSL name | Type | Source | Meaning / fallback |
|---|---|---|---|
| `_MainTex` | `sampler2D` | material `_MainTex` | Fill texture. Unset ⇒ bind 1×1 opaque white. |
| `_MainTex_ST` | `vec4` | material `_MainTex_ST` | `(scaleX, scaleY, offsetX, offsetY)`. **Never written by NowUI** — always fall back to `(1,1,0,0)`. |
| `_NowPremultipliedTexture` | `float` | material `_NowPremultipliedTexture` | `> 0.5` ⇒ `_MainTex` already carries premultiplied RGB. Default `0`. Written by `Now.SetPremultipliedTexture` from the cached textured-material path (`Now.cs:852`, `Now.UseTextureMaterial`). |
| `_NowUIMaskCount` | `float` | material | Analytic mask count, 0..8. Default `0`. |
| `_NowUIMaskRects[8]` | `vec4[8]` | material vector array | §5. Absent when count is 0. |
| `_NowUIMaskData[8]` | `vec4[8]` | material vector array | §5. |
| `_NowUIMaskParams[8]` | `vec4[8]` | material vector array | §5. |
| `_NowUIMaskTransforms[8]` | `vec4[8]` | material vector array | §5. |
| `_NowUITextureMaskCount` | `float` | material | Texture mask count, 0..2. Default `0`. |
| `_NowUITextureMask0` | `sampler2D` | material | Default `Texture2D.blackTexture` (1×1 opaque black). |
| `_NowUITextureMask1` | `sampler2D` | material | Same. |
| `_NowUITextureMaskRects[2]` | `vec4[2]` | material vector array | §5. |
| `_NowUITextureMaskParams[2]` | `vec4[2]` | material vector array | §5. |
| `_NowUITextureMaskTransforms[2]` | `vec4[2]` | material vector array | §5. |
| — | `mat4` | backend | The MVP (§6). Name it yourself, e.g. `nowui_MatrixMVP`. |

Two traps:

- **`_Color` is declared and never used.** `UIRectangle.shader:71` declares `float4 _Color;` inside the CGPROGRAM. It
  appears nowhere in `vert` or `frag`, and it is not in the `Properties` block, so it is not in `shaders.json` either.
  Do not port it.
- **`_ZTest` is in `Properties` but is state, not a uniform.** See §2.1.

For the quick-start scene, `_NowUIMaskCount` and `_NowUITextureMaskCount` are both `0` (the `UIMaterial.mat` fixture
values, and nothing in the scene pushes a mask), the recorded frame shows `properties=none`, and the analytic and
texture mask vector arrays are consequently **never set** — `NowMaskShader.Apply` only calls `SetVectorArray` when
`count > 0` (`NowMaskShader.cs:346-353`). The backend must therefore treat "vector array absent from the bag" as
normal and leave those uniforms at their GL default of all-zero. The shader's early-out
(`NowUIMaskCoverage` returns `1.0` when both counts are below `0.5`) means the zeros are never read.

### 3.3 Vertex stage

```
clipPos      = MVP * vec4(aPosition, 1.0)
uv           = aUv * _MainTex_ST.xy + _MainTex_ST.zw
rect         = aRect                       (flat pass-through)
radius       = aRadius
color        = NowUIColorToWorkingSpace(aColor)        // §4: identity under Gamma
outlineColor = NowUIColorToWorkingSpace(aOutline)
extras       = aExtras
mask         = aMask
rawUV        = aRawUV
```

`NowUIColorToWorkingSpace` converts only `.rgb`; alpha passes through untouched
(`NowUIColorSpace.cginc:26-29`).

### 3.4 Fragment maths

Written out so the GLSL can be produced without reopening the HLSL. `i.*` are the interpolated varyings.

```
// 1. UI-space position of this fragment, for masking.
size       = rect.zw
pos        = rect.xy + rawUV.xy * rect.zw          // NOTE: HLSL writes `i.rawUV * rect.zw`,
                                                   // a float4*float2 which HLSL truncates to .xy.
                                                   // In GLSL you MUST write rawUV.xy explicitly.
uiPosition = vec2(pos.x, -pos.y)

// 2. Hard legacy clip. Discards outside the axis-aligned mask rect (UI coords, y down).
d = min( min(uiPosition.x - mask.x, mask.x + mask.z - uiPosition.x),
         min(uiPosition.y - mask.y, mask.y + mask.w - uiPosition.y) )
if (d < 0.0) discard;

// 3. Read the per-vertex scalars.
blur    = extras.x
outline = extras.y

// 4. Texture sample, in the (possibly atlased) UV space.
textureSample = texture(_MainTex, uv)

// 5. Shape SDF, in FULL-QUAD space — deliberately not the atlas UV space,
//    so sprites and custom UVs keep the same corners.
position = (rawUV.xy - 0.5) * size
halfSize = size * 0.5
dist     = sdRoundedBox(position, halfSize, radius)

// 6. Screen-derivative AA. Half-pixel band centred on the true edge, so alpha
//    crosses 0.5 exactly at dist == 0 and shapes neither grow nor halo.
delta = max(length(vec2(dFdx(dist), dFdy(dist))), 0.0001)
aa    = 0.5 * delta
graphicAlpha = 1.0 - smoothstep(-aa, aa + max(blur, 0.0), dist)

// 7. Outline. Never thinner than one AA width, or a hairline outline would sit
//    entirely inside the edge fade and wash out. The inner transition is centred
//    on -outlineWidth so the ring renders at its requested thickness.
outlineWidth = max(outline, delta)
outlineAlpha = (outline == 0.0) ? 0.0
             : smoothstep(-outlineWidth - aa, -outlineWidth + aa, dist)

// 8. Premultiplied composite of outline over fill.
outlineCoverage = outlineColor.a * outlineAlpha * graphicAlpha
fillCoverage    = textureSample.a * color.a * graphicAlpha
fillColor = (_NowPremultipliedTexture > 0.5)
          ? textureSample.rgb * color.rgb * color.a * graphicAlpha
          : textureSample.rgb * color.rgb * fillCoverage

col.rgb = outlineColor.rgb * outlineCoverage + fillColor * (1.0 - outlineCoverage)
col.a   = outlineCoverage  + fillCoverage    * (1.0 - outlineCoverage)

// 9. Analytic + texture mask coverage (§5). Multiplies ALL FOUR channels —
//    HLSL `col *= x` on a float4 by a float scales rgb and a alike, which is
//    correct for premultiplied output.
col *= NowUIMaskCoverage(uiPosition)

// 10. Cheap reject of fully transparent fragments.
if (col.a - 0.001 < 0.0) discard;

fragColor = col
```

and the SDF itself (`UIRectangle.shader:74-80`):

```
float sdRoundedBox(vec2 p, vec2 b, vec4 r) {
    r.xy = (p.x > 0.0) ? r.xy : r.zw;   // pick the right-hand pair (TR,BR) or the left (TL,BL)
    r.x  = (p.y > 0.0) ? r.x  : r.y;    // then top or bottom within that pair
    vec2 q = abs(p) - b + r.x;
    return min(max(q.x, q.y), 0.0) + length(max(q, vec2(0.0))) - r.x;
}
```

This is legal GLSL ES 3.00 verbatim: the ternary's condition is a scalar `bool`, its branches may be vectors, and
writing to a swizzle of a value parameter is allowed.

### 3.5 HLSL→GLSL notes specific to this shader

- `ddx`/`ddy` → `dFdx`/`dFdy`. Both are core in GLSL ES 3.00 (no `OES_standard_derivatives` extension needed — that is
  the ES 1.00 / WebGL1 story). `fwidth` is likewise core.
- `clip(x)` → `if (x < 0.0) discard;`. Note HLSL's `clip` discards on **strictly negative**, so `x == 0` survives.
- `saturate(x)` → `clamp(x, 0.0, 1.0)`.
- `lerp` → `mix`, `frac` → `fract`, `atan2(y,x)` → `atan(y,x)`, `tex2D(s, uv)` → `texture(s, uv)`.
- `smoothstep(a, b, x)` is identical in both languages, including that `a >= b` is undefined. Every call site here has
  `a < b` by construction (`aa > 0`, `blur >= 0`), so no guard is needed — but do not "helpfully" reorder the arguments.
- The `float4 * float2` truncation in step 1 is the single place HLSL's implicit-truncation rule changes the meaning of
  a line. GLSL will refuse to compile it, which is the good outcome; write `rawUV.xy`.
- `fixed4 frag(...) : SV_Target` → `out vec4 fragColor;` with `#version 300 es`.
- No `ddx_fine`/`ddx_coarse`, no `VPOS`/`SV_Position` reads in the fragment stage, no `[unroll]` outside the mask
  include. Nothing else here has an HLSL-only dependency.

### 3.6 What slice 1 can and cannot stub

Nothing in this shader. All of it runs for the quick-start panel: rounded corners (radius non-zero), translucency
(`color.a < 1`), a white 1×1 `_MainTex`, `blur == 0`, `outline == 0`, masks at count 0. Port it whole; there is no
saving to be had.

---

## 4. `NowUI/Text Renderer`

`Assets/NowUI/Assets/Shaders/TxtRenderer.shader`, 173 lines, one pass, render queue 3000.

### 4.1 Vertex inputs consumed

The same nine attributes, with different meanings for three of them:

| Attribute | Meaning in TxtRenderer |
|---|---|
| `rect` (`TEXCOORD1`) | Glyph quad `(x, y, w, h)` in mesh space. Used only to reconstruct `uiPosition`; **`size` is not used**, there is no shape SDF here. |
| `radius` (`TEXCOORD2`) | **Not corner radii.** `radius.xyz` is the gradient payload's first three components (§4.5). Unused when `extras.w == 0`. |
| `extras` (`TEXCOORD5`) | `.x` = outline width (local units, signed); `.y` = distance-field range in local units, **negative signals the outline-only pass**; `.z` = gradient payload `.w`; `.w` = encoded gradient ramp (`0` ⇒ no gradient). |

`color` and `outlineColor` carry fill and outline RGBA as before. `mask` is the legacy clip rect. `uv` is the glyph's
sub-rect in the font atlas. `rawUV` is the quad coordinate.

### 4.2 Uniforms

| GLSL name | Type | Source | Meaning / fallback |
|---|---|---|---|
| `_MainTex` | `sampler2D` | material `_MainTex` | The font atlas. |
| `_MainTex_ST` | `vec4` | material | `(1,1,0,0)` fallback; never written. |
| `_NowUITextSdfEncoding` | `float` | material | `> 0.5` ⇒ packed SDF16 in R+B. Fixture default `0`, **runtime value `1`** (§4.4). |
| `_NowGradientRampTexture` | `sampler2D` | **shader global** | 256×256 ramp atlas. Set via `Shader.SetGlobalTexture` (`NowGradient.cs:580`), so it lands in `NowRuntime.globals.textures`, not in any material bag. |
| `_NowUIMaskCount` … `_NowUITextureMaskTransforms` | | material | Identical to §3.2. |
| — | `mat4` | backend | MVP. |

`_NowUITextOutlineOnlyPass` is in `Properties` and in `shaders.json` (default `1`) but **the shader never reads it**.
It is a CPU-side capability flag: `NowFont.supportsOutlineOnlyPass` (`NowFont.cs:1469-1482`) checks
`material.HasProperty(...) && material.GetFloat(...) > 0.5f` to decide whether to emit a second, outline-only draw.
Bind nothing for it.

Likewise `_ZTest` — state, not a uniform (§2.1).

### 4.3 Vertex stage

Identical to §3.3, including `NowUIColorToWorkingSpace` on both colours. The only difference is that `radius` is being
used as a data channel rather than as radii; the vertex shader does not care.

### 4.4 Fragment maths

```
pos        = rect.xy + rawUV.xy * rect.zw        // again: .xy explicitly
uiPosition = vec2(pos.x, -pos.y)

// Legacy hard clip, identical to §3.4 step 2.
if (legacyRectDistance(uiPosition, mask) < 0.0) discard;

outline = extras.x
msd     = texture(_MainTex, uv)

// Convert the distance-field range from local units to screen pixels, so canvas
// scale and transform scale keep text crisp.
gradX         = vec2(dFdx(pos.x), dFdy(pos.x))
gradY         = vec2(dFdx(pos.y), dFdy(pos.y))
unitsPerPixel = max(0.5 * (length(gradX) + length(gradY)), 1e-5)

outlineOnly   = extras.y < 0.0
screenPxRange = max(abs(extras.y) / unitsPerPixel, 1.0)

packedSdf16 = _NowUITextSdfEncoding > 0.5
sd = packedSdf16 ? (msd.r * 256.0 + msd.b) / 257.0
                 : median(msd.r, msd.g, msd.b)

screenPxDistance = screenPxRange * (sd - 0.5)

// MTSDF alpha is the TRUE signed distance and stays stable far from corners,
// where median-RGB is optimised for the fill edge instead.
outlineSd = (outline == 0.0 || packedSdf16) ? sd : msd.a
screenPxDistanceOutline = screenPxRange * (outlineSd - 0.5) + outline / unitsPerPixel

// A large RGBA8 field can span more than one screen pixel per stored distance
// code; widen the ramp to at least one code step. Packed pages keep a 1px ramp.
distanceCodeCount = packedSdf16 ? 65535.0 : 255.0
aaWidth   = max(1.0, screenPxRange / distanceCodeCount)
opacity   = clamp(screenPxDistance        / aaWidth + 0.5, 0.0, 1.0)
outlineOp = clamp(screenPxDistanceOutline / aaWidth + 0.5, 0.0, 1.0)

fillColor = color
if (extras.w > 0.0) {                                   // gradient path, §4.5
    payload   = vec4(radius.xyz, extras.z)
    fillColor *= NowUITextGradientSample(uiPosition, payload, extras.w)
}

vec4 outColor;
if (outlineOnly) {
    // Coverage which, after the fill pass composites source-over, keeps the
    // opaque union equal to outlineOp without painting the face.
    remainingFill = max(1.0 - opacity, 1e-5)
    ringCoverage  = clamp((outlineOp - opacity) / remainingFill, 0.0, 1.0)
    outColor    = outlineColor
    outColor.a *= ringCoverage
} else {
    outColor = (outline == 0.0)
             ? fillColor
             : mix(outlineColor, fillColor, (outline < 0.0) ? outlineOp : opacity)
    outColor.a *= max(opacity, outlineOp)
}

outColor.a   *= NowUIMaskCoverage(uiPosition)
outColor.rgb *= outColor.a            // premultiply, explicitly, at the end
fragColor = outColor
```

with

```
float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}
```

Note the asymmetry with §3.4: TxtRenderer applies `NowUIMaskCoverage` to **alpha only** and then premultiplies, while
UIRectangle multiplies an already-premultiplied `col` by it. Both are correct for their own shader; do not "unify" them.

Also note TxtRenderer has **no** `clip(col.a - 0.001)`. It returns fully transparent fragments and relies on the blend.
Do not add the discard.

**`_NowUITextSdfEncoding` is `1` in this build.** `NowFont.cs:3555-3558` computes
`usePackedManagedSdf16 = encodingMaterial != null && encodingMaterial.HasProperty(SDF_ENCODING_PROPERTY)`, and
`TxtMaterial` declares `_NowUITextSdfEncoding` (both `shaders.json` and `materials.json` confirm it), so the managed
baker packs SDF16 and `NowFont.cs:3647-3648` sets the uniform to `1`. The `materials.json` default of `0` is the
*asset's authored* value; the runtime overwrites it before the first text draw. Implement both branches — the
`packedSdf16` branch is what will execute, and if the observed value is ever `0` that is a finding worth chasing, not
something to paper over.

Precision follow-on: `(msd.r * 256.0 + msd.b) / 257.0` reconstructs a 16-bit distance from two 8-bit channels of an
`RGBA8` texture read through `LINEAR` filtering. It requires `highp` and a genuinely `RGBA8`-normalised sample. Do not
upload the atlas as anything else, do not enable mipmaps on it, and do not let the sampler fall back to `NEAREST`.

The font atlas, from `NowFont.cs:2846-2851` and `:3626-3631`:

```
new Texture2D(side, side, TextureFormat.RGBA32, mipChain:false, linear:true)
{ filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }
```

which matches the recorded `UploadTexture2D(tex0, pixels=4194304, dirty=(0,0,1024,1024), mips=false)` exactly
(1024·1024·4 = 4194304). GL mapping: `RGBA8` / `RGBA` / `UNSIGNED_BYTE`, `TEXTURE_MIN_FILTER = LINEAR`,
`TEXTURE_MAG_FILTER = LINEAR`, `TEXTURE_WRAP_S/T = CLAMP_TO_EDGE`, `TEXTURE_BASE_LEVEL = 0`,
`TEXTURE_MAX_LEVEL = 0`. That last pair is not optional: with `MIN_FILTER = LINEAR` and no mip chain the texture is
complete, but setting `MAX_LEVEL` explicitly is what keeps it complete if someone later flips `MIN_FILTER`.

### 4.5 What slice 1 may stub, and the guard that makes it safe

The gradient path — `extras.w > 0.0` and everything in `NowUITextGradient.cginc` — is not reached by the quick-start
scene. `Score: 1200` is drawn with a flat colour, so every text vertex has `extras.w == 0`, `_NowGradientRampTexture`
is never bound (`NowGradientRampCache` only publishes it on first ramp allocation, `NowGradient.cs:579-580`), and the
branch is dead.

Slice 1 may therefore omit the gradient branch entirely, **on these conditions**:

1. The `extras.w > 0.0` test stays in the GLSL, with the true branch replaced by a visible marker rather than silence
   — the cleanest being to leave `fillColor` untouched *and* have the backend log once when it sees a text vertex with
   `extras.w != 0`. A silently-ignored gradient renders plausible-looking flat text, which is the exact class of bug
   this document exists to prevent.
2. `_NowGradientRampTexture` is still *declared* in the GLSL only if the branch is kept. If the branch is dropped,
   drop the sampler too — an unbound `sampler2D` that is never sampled is legal, but leaving declared-and-unbound
   samplers around invites a driver-dependent warning and a future mystery.

If the gradient path is ported after all, the maths is in `NowUITextGradient.cginc` and needs three HLSL→GLSL
substitutions beyond the usual: `frac`→`fract`, `atan2(y,x)`→`atan(y,x)`, and — the one that bites —
**`fmod` is not `mod`**. HLSL's `fmod` truncates toward zero and keeps the sign of the dividend; GLSL's `mod` floors.
`NowUITextGradientSample` calls `fmod(flags, 4.0)`, `fmod(floor(flags/4.0), 4.0)`, `fmod(floor(flags/32.0), 2.0)` and
`fmod(floor(flags/16.0), 2.0)`, all on non-negative values, where the two agree — but write a helper named `hlslFmod`
rather than reaching for `mod` and hoping, because the next shader that gets ported will have a negative operand.

> **Ported 2026-09-08.** `wwwroot/shaders/nowui-text-gradient.glsl`, generated into `nowui-gl.js` by
> `tools/embed-shader.py`. The three traps above were all real; a **fourth** was not anticipated here and is worth
> adding to the list — `sampler2D` defaults to **lowp** in a GLSL ES fragment stage, so `_NowGradientRampTexture`
> must be declared `highp` or the ramp quantises on the way in. Condition 1's report is not deleted but re-aimed:
> an unbound ramp sampler falls back to 1×1 white and `fillColor *= (1,1,1,1)` reproduces the *exact* flat picture
> the unported branch gave, so `WebGL2Backend` now reports a gradient vertex arriving while the shader global is
> unset. `M2-FeatureMatrix.md` §3.1 has the measurement and names what is ported but still unmeasured.

---

## 5. The mask include

`Assets/NowUI/Assets/Shaders/NowUIMask.cginc`, 236 lines, shared verbatim by both shaders. Two independent mask
systems plus one legacy hard clip.

### 5.1 The legacy rect clip — always active

```
NowUILegacyRectDistance(p, rect) = min( min(p.x - rect.x, rect.x + rect.z - p.x),
                                        min(p.y - rect.y, rect.y + rect.w - p.y) )
NowUIClipLegacyRect(p, rect)     = clip(that)
```

`rect` here is the per-vertex `mask` attribute (`TEXCOORD6`), in **UI coordinates, y down, positive**, and `p` is
`uiPosition`. This is a hard discard with no anti-aliasing, deliberately, to preserve the existing `NowRect` mask
contract exactly (`NowUIMask.cginc:34-36`). Both shaders call it before doing any other work. It is not optional and
not conditional on any uniform. In the quick-start scene it is the screen rect.

### 5.2 The analytic masks — uniform arrays of exactly 8

```
#define NOW_UI_ANALYTIC_MASK_CAPACITY 8

float  _NowUIMaskCount;
float4 _NowUIMaskRects[8];       // local-space (x, y, width, height)
float4 _NowUIMaskData[8];        // rounded-rect radii (TR,BR,TL,BL)  OR  capsule endpoints (sx,sy,ex,ey)
float4 _NowUIMaskParams[8];      // (kind, extra feather pixels, capsule radius, unused)
float4 _NowUIMaskTransforms[8];  // (screen origin.x, screen origin.y, signed scale.x, signed scale.y)
```

Kinds: `0` rectangle, `1` rounded rectangle, `2` ellipse, `3` capsule.

**How the values arrive, and why the array length matters.** `NowMaskShader` (`NowMaskShader.cs:302-308`) holds
`static readonly Vector4[]` scratch arrays sized at `NowMaskShaderState.Capacity` (8) and
`NowMaskShaderState.TextureCapacity` (2), and `Apply` / `GetPropertyBlock` pass **the whole array**, never a slice:

```csharp
material.SetVectorArray(_rectsId, _rects);   // _rects.Length == 8, regardless of state.count
```

`Material.SetVectorArray` copies into a bag-owned array "of exactly `values.Length` entries"
(`Material.cs` / `NowMaterialBag.SetVectorArray`), so what reaches the backend is always a `Vector4[8]` (or `[2]`).
It matches the fixed HLSL capacity by construction. The backend must therefore:

- Declare the GLSL arrays at the same fixed sizes, `[8]` and `[2]`. Never size them from `_NowUIMaskCount`.
- Upload with a single `gl.uniform4fv(loc, flat32)` where `flat32.length == 32` (or `8`), and `loc` comes from
  `getUniformLocation(prog, "_NowUIMaskRects[0]")` — the `[0]` suffix is how you get the array's base location.
- **Reject a length mismatch loudly.** A bag entry of any length other than the declared capacity means an assumption
  broke upstream; uploading it truncated or padded would produce a plausible wrong mask.
- Treat *absent* as "count is 0". `Apply` skips the `SetVectorArray` calls entirely when `count == 0`
  (`NowMaskShader.cs:346-353`), which is the quick-start case. Leave the uniform at GL's zero default.

**Precedence.** `NowMaskShader` writes either into the material (`Apply`) or into a shared
`MaterialPropertyBlock` (`GetPropertyBlock`), and the block is handed to `DrawMesh` as the `properties` argument. So
the resolution order in §7 is not academic here — it is the mechanism by which two batches with different masks share
one material.

**Fragment maths.** Per mask index, in order, taking the minimum coverage:

```
localPosition = NowUIMaskLocalPosition(uiPosition, transforms[i])
              = (uiPosition - transforms[i].xy) / safeScale
   where safeScale preserves the SIGN of transforms[i].zw and clamps the magnitude away from zero:
       s.x < 0 ? min(s.x, -1e-5) : max(s.x, 1e-5)     (same for y)
   The sign must survive: a mirrored scope has to select the mirrored rounded-rect corner.

signedDistance = NowUIAnalyticMaskDistance(localPosition, rects[i], data[i], params[i])
   kind < 0.5  → rect:         halfSize = max(abs(rect.zw)*0.5, 1e-5)
                               centre   = rect.xy + rect.zw*0.5
                               q        = abs(p - centre) - halfSize
                               d        = length(max(q,0)) + min(max(q.x,q.y),0)
   kind < 1.5  → rounded rect: local  = p - (rect.xy + rect.zw*0.5)
                               radius = local.x < 0 ? (local.y < 0 ? data.z : data.w)
                                                    : (local.y < 0 ? data.x : data.y)
                               radius = clamp(radius, 0, min(halfSize.x, halfSize.y))
                               q      = abs(local) - halfSize + radius
                               d      = length(max(q,0)) + min(max(q.x,q.y),0) - radius
   kind < 2.5  → ellipse:      d = (length(local / halfSize) - 1) * min(halfSize.x, halfSize.y)
                               (exact zero contour and sign; magnitude is approximate away from
                                the edge, which the derivative normalisation below absorbs)
   else        → capsule:      from = data.xy, to = data.zw
                               seg  = to - from
                               t    = clamp(dot(p - from, seg) / max(dot(seg,seg), 1e-5), 0, 1)
                               d    = length(p - (from + seg*t)) - max(params.z, 0)

shapeCoverage = NowUIAnalyticMaskEdgeCoverage(signedDistance, params[i].y)
   distancePerPixel = max(fwidth(signedDistance), 1e-5)
   transitionPixels = 1.0 + max(featherPixels, 0.0)     // zero feather still gets 1px of AA
   halfBand         = 0.5 * transitionPixels * distancePerPixel
   coverage         = 1.0 - smoothstep(-halfBand, halfBand, signedDistance)

coverage = min(coverage, shapeCoverage)
```

Note the rounded-rect corner selection here is the **opposite y sense** from `sdRoundedBox` in §3.4: this one works in
UI coordinates (y down), so `local.y < 0` is the *top* half and picks `data.z`/`data.x` = TL/TR. `sdRoundedBox` works
in the y-up SDF space and picks the top from `p.y > 0`. Both end up selecting the top-right radius from component
`.x`. Get this backwards and rounded masks come out rotated 180° in y — a bug that survives casual inspection on a
symmetric shape.

**Loop shape.** The HLSL is `[unroll]` over the full capacity with an early `break`:

```
int maskCount = (int)clamp(floor(_NowUIMaskCount + 0.5), 0.0, 8.0);
[unroll] for (int i = 0; i < 8; ++i) { if (i >= maskCount) break; ... }
```

Keep exactly that shape in GLSL — constant bound, `break` on the dynamic count. GLSL ES 3.00 permits fully dynamic
loops, but the constant bound costs nothing and keeps the ported code a line-for-line match against the source.

### 5.3 The texture masks — capacity 2, explicit samplers

```
#define NOW_UI_TEXTURE_MASK_CAPACITY 2

float     _NowUITextureMaskCount;
sampler2D _NowUITextureMask0;
sampler2D _NowUITextureMask1;
float4    _NowUITextureMaskRects[2];       // authored local-space (x, y, w, h)
float4    _NowUITextureMaskParams[2];      // (channel, inverted, valid texture, unused); channel 0 = alpha, 1 = red
float4    _NowUITextureMaskTransforms[2];  // (origin.x, origin.y, signed scale.x, signed scale.y)
```

The two samplers are **explicitly unrolled, not dynamically indexed** — the HLSL says this is for SM3 compatibility
(`NowUIMask.cginc:26`), and it happens to be exactly what GLSL ES 3.00 also requires (sampler arrays may only be
indexed by constant expressions). Keep the unrolled form.

```
NowUITextureMaskSampleCoverage(p, rect, params, transform, tex):
    if (params.z < 0.5 || rect.z <= 0.0 || rect.w <= 0.0) return 0.0;   // validity BEFORE inversion,
                                                                        // so an empty source stays empty
    local      = NowUIMaskLocalPosition(p, transform)
    normalized = (local - rect.xy) / max(rect.zw, 1e-5)
    inside     = step(0, n.x) * step(n.x, 1) * step(0, n.y) * step(n.y, 1)
    uv         = clamp(vec2(n.x, 1.0 - n.y), 0.0, 1.0)   // NowUI is y-down; texture UVs are y-up
    s          = texture(tex, uv)
    c          = params.x < 0.5 ? s.a : s.r
    c          = params.y > 0.5 ? 1.0 - c : c
    return clamp(c, 0.0, 1.0) * inside
```

with `NowUITextureMaskCoverage` returning `1.0` when the count is `<= 0`, sampling slot 0, and `min`-ing in slot 1 when
count `> 1`.

Both mask samplers must always be bound to *something* — `NowMaskShader.Apply` binds `Texture2D.blackTexture` (1×1
opaque black) when unused (`NowMaskShader.cs:369-375`), and the backend should do the same rather than leaving unit
bindings stale from a previous draw.

### 5.4 The early-out

```
float NowUIMaskCoverage(float2 position) {
    if (_NowUIMaskCount < 0.5 && _NowUITextureMaskCount < 0.5) return 1.0;
    return min(NowUIAnalyticMaskCoverage(position), NowUITextureMaskCoverage(position));
}
```

This is the branch that makes the all-zero uniform arrays of the quick-start frame harmless. Keep it first.

### 5.5 Uniform budget

8 masks × 4 `vec4` = 32 `vec4`, plus 2 texture masks × 3 `vec4` = 6, plus scalars: about 40 `vec4` of fragment uniform
space, plus 4 samplers (`_MainTex`, ramp, two mask textures). WebGL2's floor for `MAX_FRAGMENT_UNIFORM_VECTORS` is 224
and for `MAX_TEXTURE_IMAGE_UNITS` is 16, so there is ample room. No uniform buffer object is needed; plain uniforms are
fine and simpler.

---

## 6. Colour space

### 6.1 What `NowUIColorSpace.cginc` does

31 lines. One function, two overloads:

```hlsl
inline float3 NowUIColorToWorkingSpace(float3 color)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return color;                       // ← the branch that applies to us
#else
    half3 value = color;
    half3 low   = 0.0849710h * value - 0.000163029h;
    half3 high  = value * (value * (value * 0.265885h + 0.736584h) - 0.00980184h) + 0.00319697h;
    return (value < (half3)0.0725490h) ? low : high;
#endif
}
inline float4 NowUIColorToWorkingSpace(float4 c) { return float4(NowUIColorToWorkingSpace(c.rgb), c.a); }
```

The intent, per the file's own comment: NowUI's public `Color` values and theme palettes are authored as display/sRGB
UI colours, matching Unity's colour picker and CSS palettes. Shader maths must happen in the *project's working* colour
space. The `#else` branch is `UnityUI.cginc`'s piecewise sRGB→linear approximation (chosen over `UnityCG`'s cubic
because the cubic loses display-code values in the dark UI range).

Alpha is never converted, in either branch.

### 6.2 Which branch we are in

Gamma. Three independent confirmations:

- `ProjectSettings.asset` has `m_ActiveColorSpace: 0` (M2-Scouting §7).
- `NowRuntime.colorSpace` defaults to `ColorSpace.Gamma` (`NowRuntime.cs:52`), and `QualitySettings.activeColorSpace`
  returns it (`Services/QualitySettings.cs:22`).
- `Standalone/Tests/Fixtures/materials.json` records `"colorSpace": "Gamma"`.

Unity defines `UNITY_COLORSPACE_GAMMA` when the project colour space is Gamma, so **`NowUIColorToWorkingSpace` is the
identity function** in the build we are matching.

### 6.3 What the WebGL2 backend must do

Precisely: nothing, in five specific places where WebGL would otherwise convert on its own.

| Concern | Required setting | Why |
|---|---|---|
| GLSL implementation | `vec4 NowUIColorToWorkingSpace(vec4 c) { return c; }` | Gamma branch. Keep the function (not a manual inline) so the linear branch is one `#define` away for a later slice. |
| Framebuffer | Default `webgl2` drawing buffer. Do **not** request or enable any sRGB framebuffer; WebGL has no `GL_FRAMEBUFFER_SRGB` and you should not go looking for `EXT_sRGB` equivalents. | The default drawing buffer's bytes are handed to the compositor as already-sRGB-encoded, which is exactly the Gamma pipeline's contract. |
| Canvas colour space | Leave `drawingBufferColorSpace` / `unpackColorSpace` at their default `'srgb'`. Do not set `'display-p3'`. | Any other value re-maps the values you wrote. |
| Texture internal format | `RGBA8` (`gl.RGBA8`), format `RGBA`, type `UNSIGNED_BYTE`. **Never `SRGB8_ALPHA8`.** | An sRGB internal format makes the sampler linearise on read, which the shader does not expect and cannot undo. |
| Texture unpack | `UNPACK_FLIP_Y_WEBGL = false`, `UNPACK_PREMULTIPLY_ALPHA_WEBGL = false`, `UNPACK_COLORSPACE_CONVERSION_WEBGL = NONE`, `UNPACK_ALIGNMENT = 1`. | All are defaults except `COLORSPACE_CONVERSION` (default is `BROWSER_DEFAULT_WEBGL`) and `ALIGNMENT` (default 4). Set all four explicitly at context creation and never change them. |

`Texture2D.isLinear` (`Texture2D.cs:110-117`) is `true` for every mask, ramp and SDF atlas NowUI builds, including the
font atlas (`new Texture2D(side, side, TextureFormat.RGBA32, false, true)`). Under **Gamma** that flag has no effect —
Unity performs no sRGB sampling conversion at all in a Gamma project, so `isLinear` and its opposite upload
identically. The backend should read the flag and, for now, assert that it changes nothing; when a linear slice
arrives, `isLinear == false` will be the signal to use `SRGB8_ALPHA8`.

Where conversion *would* belong, if a later slice goes linear: in the shader, inside `NowUIColorToWorkingSpace`
(vertex colours) and in the texture internal format (sampled textures) — never in the framebuffer, because NowUI's
premultiplied blend is defined in the working space and an sRGB-converting blend unit would compose the wrong values.

`UNPACK_ALIGNMENT = 1` deserves its own line: the font atlas is RGBA8 at 1024 wide, so 4-byte alignment happens to
work, but partial `texSubImage2D` uploads of a narrow dirty rect, and any future single-channel atlas, will silently
skew without it.

---

## 7. The uniform bridge

### 7.1 What the shim hands the backend

A `Material` is a `Shader` plus a `NowMaterialBag` (`Material.cs`, `NowMaterialBag.cs`). The bag is seven typed
dictionaries keyed by `int` property id, plus a keyword set and a `version` counter:

```csharp
Dictionary<int, float>      floats;
Dictionary<int, int>        ints;
Dictionary<int, Vector4>    vectors;
Dictionary<int, Vector4[]>  vectorArrays;
Dictionary<int, float[]>    floatArrays;
Dictionary<int, Matrix4x4>  matrices;
Dictionary<int, Texture>    textures;
HashSet<string>             keywords;
uint                        version;
```

`MaterialPropertyBlock` uses the same `NowMaterialBag` type, so one code path reads either.
`NowRuntime.globals` is a `NowShaderGlobals` with the same shape and its own `int version`.

Ids come from `Shader.PropertyToID(string)`, a process-wide intern table that hands out `1, 2, 3, …` in first-seen
order and is never reset (`Shader.cs:63-79`, and `:96-104` on why). **Ids are not stable across processes** — they
depend on which static initialiser ran first — so a backend must never hard-code a numeric id or persist one.

### 7.2 Getting from an id to a GLSL uniform name

`Shader.IDToName(int)` exists and does exactly this (`Shader.cs:85-94`), but it is `internal` and `NowUI.Web` is not a
friend assembly (§1.5). The backend does not need it:

**Intern the names you care about, once, at backend construction, and build your own `int → string` map.**

```csharp
static readonly (string name, int id)[] Known = new[] {
    "_MainTex", "_MainTex_ST", "_NowPremultipliedTexture",
    "_NowUITextSdfEncoding", "_NowUITextOutlineOnlyPass", "_ZTest",
    "_NowUIMaskCount", "_NowUIMaskRects", "_NowUIMaskData",
    "_NowUIMaskParams", "_NowUIMaskTransforms",
    "_NowUITextureMaskCount", "_NowUITextureMask0", "_NowUITextureMask1",
    "_NowUITextureMaskRects", "_NowUITextureMaskParams", "_NowUITextureMaskTransforms",
    "_NowGradientRampTexture",
}.Select(n => (n, Shader.PropertyToID(n))).ToArray();
```

`PropertyToID` is idempotent and returns the same id NowUI's own static initialisers got, because the table is one
shared dictionary. Eighteen names cover both shaders completely. Anything not on the list is a property these two
shaders do not declare and can be skipped without loss.

Reading values uses the public API, which needs no internals:

```csharp
material.GetFloat(id)          material.GetVector(id)        material.GetTexture(id)
material.GetVectorArray(id)    material.HasProperty(id)
block.GetFloat(id)             block.GetVector(id)  …        // same surface on MaterialPropertyBlock
NowRuntime.globals.GetFloat(id) / GetVector(id) / GetTexture(id)
```

Caveat worth knowing: `Material.GetVectorArray(int)` allocates a fresh copy every call
(`Material.cs`, "A fresh array of exactly the stored length"). Use the
`GetVectorArray(int, List<Vector4>)` overload with a reusable list, or accept the allocation while masks are unused in
slice 1 and revisit when they are not.

### 7.3 Resolution order

For each uniform a program declares, in this order, first hit wins:

1. The per-draw `MaterialPropertyBlock`, when `properties != null` and `HasProperty(id)`.
2. The `Material`'s bag.
3. `NowRuntime.globals` — this is the only source for `_NowGradientRampTexture` (`Shader.SetGlobalTexture`,
   `NowGradient.cs:580`).
4. The shader's declared default from `NowShaderInfo` — already seeded into a fresh `Material`'s bag by
   `SeedFromShaderDefaults`, so in practice step 2 covers this.
5. A hard backend fallback, listed per uniform in §3.2 and §4.2 (white 1×1 for `_MainTex`, black 1×1 for the mask
   samplers, `(1,1,0,0)` for `_MainTex_ST`, `0` for every other float).

Beware `Material.HasProperty`: it is `true` for a property the *shader declares* even if nobody assigned it
(`Material.cs:158-166`), which is deliberate and is what NowUI's `HasProperty`-gated feature detection depends on. Use
the bag's `TryGetValue`-shaped getters (`GetFloat` returns `0f` for a missing key, `GetTexture` returns `null`) to
decide whether a value is really present, not `HasProperty`.

### 7.4 Type mapping

| Bag entry | GLSL | WebGL2 call |
|---|---|---|
| `float` | `uniform float` | `gl.uniform1f(loc, v)` |
| `int` (via `SetInteger`) | `uniform int` | `gl.uniform1i(loc, v)`. Neither shader in scope declares one. |
| `Vector4` / `Color` | `uniform vec4` | `gl.uniform4f(loc, x, y, z, w)`. Colours and vectors share one dictionary, as in Unity. |
| `Vector4[]` | `uniform vec4 u[N];` | `gl.uniform4fv(locOf("u[0]"), flatFloat32)` — one call for the whole array; `flatFloat32.length == 4*N`. |
| `float[]` | `uniform float u[N];` | `gl.uniform1fv`. Not used by these two shaders. |
| `Matrix4x4` | `uniform mat4` | `gl.uniformMatrix4fv(loc, false, floats)`; see §8.2 on why `transpose` is `false`. |
| `Texture` | `uniform sampler2D` | Assign a fixed texture unit per sampler at link time, `gl.uniform1i(loc, unit)` once, then `activeTexture` + `bindTexture` per draw. |

Sampler units for these two programs — fix them and never shuffle:

```
0  _MainTex
1  _NowUITextureMask0
2  _NowUITextureMask1
3  _NowGradientRampTexture   (TxtRenderer only, and only if §4.5's gradient branch is ported)
```

### 7.5 Caching

`Material.version` (`Material.cs:38`) and `NowMaterialBag.version` are `internal`, and `NowShaderGlobals.version` is
`public`. `INowRenderBackend`'s documented guarantee 1 is that a backend may key a resolved-uniform cache on
`(GetInstanceID(), version)` and skip the re-push when it has not moved. From `NowUI.Web` only the globals half of
that is reachable. For slice 1 — two draws per frame, a couple of dozen uniforms — **push every uniform every draw**
and do not build the cache. If it ever matters, the `InternalsVisibleTo` note in §1.5 applies here too.

`GetInstanceID()` is public on `Object` and is the documented identity for backend-side resource maps.

---

## 8. Projection

This is the section that decides whether the first frame is upside down. Be literal about it.

### 8.1 The exact matrix NowUI emits

Two call sites, identical:

- `Now.cs:1115` — `_projectionMatrix = Matrix4x4.Ortho(0, screenMask.width, -screenMask.height, 0, -1, 100);`
- `NowRenderer.cs:657-660` — `GetProjectionMatrix(Vector2 size) => Matrix4x4.Ortho(0, size.x, -size.y, 0, -1, 100);`

So `left = 0`, `right = W`, `bottom = -H`, `top = 0`, `zNear = -1`, `zFar = 100`.

`Matrix4x4.Ortho` in the shim (`Standalone/NowUI.Engine/Math/Matrix4x4.cs:726-740`, documented as bit-exact against
Unity for two reference inputs) is:

```
dx = right - left;  dy = top - bottom;  dz = zFar - zNear;
m00 = 2/dx;   m03 = -(right + left)/dx;
m11 = 2/dy;   m13 = -(top + bottom)/dy;
m22 = -2/dz;  m23 = -(zFar + zNear)/dz;
                                        // everything else from the identity
```

Substituting:

```
dx = W          dy = 0 - (-H) = H          dz = 100 - (-1) = 101

m00 = 2/W       m01 = 0         m02 = 0            m03 = -1
m10 = 0         m11 = 2/H       m12 = 0            m13 = +1
m20 = 0         m21 = 0         m22 = -2/101       m23 = -99/101
m30 = 0         m31 = 0         m32 = 0            m33 = 1
```

(`m13 = -(top + bottom)/dy = -(0 + (-H))/H = +1`; `m23 = -(100 + (-1))/101 = -99/101 ≈ -0.98019803`.)

So, for a vertex `v` in NowUI mesh space:

```
clip.x = 2·v.x / W - 1
clip.y = 2·v.y / H + 1
clip.z = -2·v.z / 101 - 99/101
clip.w = 1
```

### 8.2 Memory layout and `uniformMatrix4fv`

The shim's `Matrix4x4` declares its sixteen floats **in column-major memory order**
(`Math/Matrix4x4.cs:29-46`: `m00, m10, m20, m30, m01, m11, …`), explicitly so that unsafe reinterpretation matches
Unity. GLSL's `mat4` and `uniformMatrix4fv` with `transpose = false` want exactly column-major. Therefore:

```js
gl.uniformMatrix4fv(loc, false, floats)   // floats = the struct's 16 floats in declaration order
```

with **no transpose and no reordering**. For `W = 800, H = 600` that array is literally

```
[ 0.0025, 0, 0, 0,
  0, 0.00333333, 0, 0,
  0, 0, -0.01980198, 0,
  -1, 1, -0.98019803, 1 ]
```

and the GLSL is `gl_Position = uMVP * vec4(aPosition, 1.0);`.

HLSL's `mul(M, v)` with Unity's column-vector convention is the same operation as GLSL's `M * v`. Unity's HLSL is
row-major in *source syntax* (`m03` is the translation, row 0 column 3) and column-major in *storage*; the shim
preserves both. No majorness fix-up is needed anywhere. If you find yourself transposing, something else is wrong.

### 8.3 Which matrix to upload

`UnityObjectToClipPos(v)` expands to `mul(UNITY_MATRIX_VP, mul(unity_ObjectToWorld, v))`. `SetViewProjection` supplies
view and projection; `DrawMesh` supplies the model matrix. The recorded quick-start frame has `view = identity` and
`model = identity`, so the product is just the projection — but do not hard-code that.

Compute `MVP = projection * view * model` on the C# side using the shim's `Matrix4x4` operator (which is Unity's own
arithmetic) and upload one `mat4`. Reasons: one uniform instead of two or three; the multiplication happens in the
same float arithmetic Unity would use; and the vertex shader stays a single line.

The associativity differs from Unity's `VP * (M * v)` by float rounding only, at a magnitude far below a pixel. If a
later slice ever needs exactness, split the uniform into `uMatrixVP` and `uMatrixM` and reproduce the nesting.

### 8.4 Where the Y flip lives: nowhere, because it is already done

Walk it through, because this is the claim most worth checking rather than trusting.

NowUI's mesh-space `y` is the negation of UI-space `y` (§1.3). A rect at UI top-left `(0, 0)` sizing `(W, H)` produces
mesh corners spanning `y ∈ [-H, 0]`.

- UI top edge (`uiY = 0`) → mesh `y = 0` → `clip.y = 2·0/H + 1 = +1`.
- UI bottom edge (`uiY = H`) → mesh `y = -H` → `clip.y = 2·(-H)/H + 1 = -1`.

GLSL / OpenGL clip space puts `y = +1` at the **top** of the viewport (`gl_FragCoord.y` increases upward from a
bottom-left origin, and the viewport transform maps NDC `+1` to the highest `y`). The browser then presents the
drawing buffer with the framebuffer's top at the top of the page.

So UI top → clip `+1` → framebuffer top → page top. Correct, with **no flip in the matrix and no flip at the
framebuffer**. Concretely:

- Do **not** negate `m11` or `m13`.
- Do **not** set `UNPACK_FLIP_Y_WEBGL`.
- Do **not** flip the viewport, scissor, or the canvas via CSS `transform: scaleY(-1)`.
- Do **not** reverse the index winding to "fix" the apparent flip. `Cull Off` (§2.1) already makes winding irrelevant,
  and reversing it would hide a genuine bug next time.

`gl.viewport(0, 0, drawingBufferWidth, drawingBufferHeight)`, and that is the whole of it.

The reason this works cleanly, and Unity needs `GetGPUProjectionMatrix` on D3D but we do not: `Matrix4x4.Ortho` emits
the **OpenGL** convention — the shim's own comment calls it that (`Math/Matrix4x4.cs:742`, and the `decomposeProjection`
inverse at `:786` restates the formulas). WebGL2 *is* that convention. We are the platform the matrix was written for.

### 8.5 Depth, for completeness

`clip.z = -2·v.z/101 - 99/101`, and every NowUI vertex has `v.z = 0` (`NowMesh.AddRect` leaves `Vector3.z` at its
default), so `clip.z = -0.98019803` for every fragment. GL's default NDC depth range is `[-1, +1]`, so that value is in
range and would map to window depth `≈ 0.0099` under the default `depthRange(0, 1)`.

It never matters: `ZWrite Off` and `ZTest Always` (§2.1) mean depth is neither written nor tested, and §2.2 says to
allocate no depth buffer at all. Do not call `gl.clearDepth`, do not call `gl.depthRange`. This paragraph exists so
that nobody "fixes" the `-0.98` on the way past, and so that the D3D-vs-GL `[0,1]`-vs-`[-1,1]` difference — the reason
Unity has `GetGPUProjectionMatrix` in the first place — is recorded as understood rather than overlooked.

### 8.6 Device pixel ratio

The ortho maps `[0, W] × [0, H]` NowUI units onto the full NDC cube, and the viewport maps NDC onto the whole drawing
buffer. So `W`/`H` are whatever logical size the host reports through `Screen.width`/`Screen.height`, and the drawing
buffer may be larger.

If the host sets `canvas.width = cssWidth * devicePixelRatio` while reporting `Screen.width = cssWidth`, everything
still works and text gets *sharper*, because `dFdx`/`dFdy` are evaluated per physical pixel: `unitsPerPixel` in §4.4
shrinks by `dpr`, `screenPxRange` grows, and the AA band lands on one physical pixel rather than one CSS pixel. That is
the correct behaviour and is worth having.

For slice 1, though, pin `devicePixelRatio = 1` (`canvas.width = cssWidth`). One fewer variable while the first frame
is being made to appear; turn it on afterwards and confirm the panel edge and the glyph stems both sharpen rather than
shift.

---

## 9. Summary of findings the implementation units should carry forward

1. **The interleaved vertex path is dead in this build** (`NOWUI_VG_DISABLE_NATIVE` ⇒ `packRenderAvailable == false`).
   Implement nine streams; assert on `data.interleaved`. §1.2.
2. **`_NowUITextSdfEncoding` is `1` at runtime**, not the `0` the material fixture records. The packed-SDF16 branch is
   the live one. §4.4.
3. **`_Color` in UIRectangle and `_NowUITextOutlineOnlyPass` in TxtRenderer are not shader uniforms** — one is declared
   and unused, the other is a CPU-side capability flag. Do not bind either. §3.2, §4.2.
4. **No flip anywhere.** The Y negation is already in the vertex data and the matrix, and GL clip space is the
   convention `Matrix4x4.Ortho` targets. §8.4.
5. **Mask vector arrays always arrive at full capacity (8 and 2) or not at all.** Declare the GLSL arrays at fixed
   size, upload whole, reject a length mismatch, treat absent as count-zero. §5.2.
6. **`Shader.IDToName`, `Material.bag`, `Mesh.data` and `Mesh.version` are `internal` and `NowUI.Web` is not a friend
   assembly.** No shim edit is required for slice 1 — intern the eighteen names yourself and read through the public
   getters — but the zero-copy path would need
   `<InternalsVisibleTo Include="NowUI.Web" />` in `NowUI.Engine.csproj`, which is a shim change and therefore a
   reportable finding rather than a quiet edit. §1.5, §7.2, §7.5.
7. **The colour-space work is all negative work**: five settings that must be left alone or explicitly turned off. The
   likely failure is `SRGB8_ALPHA8` sneaking in via a "correct" texture upload helper. §6.3.
