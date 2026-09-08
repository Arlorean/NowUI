# M2 scouting: what is actually true before designing the browser build

Measured on 2026-09-07, immediately after M1 completed. Every number here came from running something, not from reading code.

## 1. The core runs in a browser under WebAssembly. Verified, not assumed.

A throwaway `wasmbrowser` project referencing `Standalone/NowUI.Runtime` and `Standalone/NowUI.Engine` compiled without a
single change to either, was served over HTTP, and printed this to the browser console:

```
NowUI ran under wasm: 2 backend ops
BeginFrame(1)
EndFrame()
```

That is NowUI initialising its runtime, opening a UI scope and closing a frame, inside the browser. Two operations rather
than more because no resource provider was registered, so the material failed to load and nothing drew. Which is itself the
first finding below.

No source changes were needed. The unsafe code, the struct layouts, the `DllImport` declarations behind their disabled
guards, all of it compiled for `browser-wasm` as-is.

## 2. Payload: 943 KB over the wire

| Configuration | Uncompressed | Brotli, what a browser downloads |
|---|---|---|
| Release publish, default settings | 6977 KB | 2017 KB |
| Release publish, `InvariantGlobalization` + `PublishTrimmed` | 3017 KB | **943 KB** |

The difference is almost entirely ICU globalization data. Under a megabyte compressed changes what this can be: not merely
an application framework, but something plausible to drop into a page. Note that `InvariantGlobalization` is right for a
browser host and wrong for the test project, which needs real cultures. It belongs per-project, not in
`Directory.Build.props`.

This supersedes the earlier guess of "several megabytes, fine for an app, awkward for a widget".

## 3. What a real frame actually asks the backend to do

The recording backend was pointed at a genuine NowUI scene, the README quick-start panel: a rounded translucent rectangle
plus text. The complete backend traffic for that frame:

```
BeginFrame(1)
UpdateSampler(tex0)
UploadTexture2D(tex0, pixels=4194304, dirty=(0, 0, 1024, 1024), mips=false)
SetViewProjection(view=identity, projection=ortho)
DrawMesh(mesh0, subMesh=0, material=mat0, pass=0, properties=none, model=identity)
SetViewProjection(view=identity, projection=ortho)
DrawMesh(mesh1, subMesh=0, material=mat1, pass=0, properties=none, model=identity)
EndFrame()
```

Eight operations. Three distinct capabilities: upload a texture, set an orthographic view-projection, draw a mesh. The
1024x1024 upload is the font atlas, baked at runtime by the managed MSDF baker with the native plugin compiled out, which
means text works on the managed path.

The consequence for planning: implementing `UploadTexture2D`, `UpdateSampler`, `SetViewProjection` and `DrawMesh`, plus two
shader programs, is enough to put a real NowUI scene on a canvas. The other sixteen backend methods serve glass, effects,
SDF and render-to-texture, and can follow.

## 4. The backend contract is 20 methods

`INowRenderBackend` is 298 lines and about twenty methods: frame begin and end, texture upload, sampler update, texture
release, render-texture create, loss-check and release, mesh and material release, shader resolve, set render target,
viewport, view-projection, clear, draw mesh, draw procedural, blit, copy texture. Two implementations already exist, a null
one and a recording one, so a third has a shape to follow and something to diff against.

## 5. Shaders to port: about 1800 lines of HLSL

23 shader programs were exported, but the UGUI variants are not used by the standalone build and the SDF ones are optional
for a first cut. The core set and its shared includes:

| Shader | Lines | Passes |
|---|---|---|
| UIRectangle | 163 | 1 |
| TxtRenderer | 173 | 1 |
| UIGradient | 223 | 1 |
| UIGlass | 267 | 1 |
| UIGlassBlur | 256 | 4 |
| UIRipple | 117 | 1 |
| UIBezier | 138 | 1 |
| UIColorPicker | 128 | 1 |
| NowUIMask.cginc | 236 | shared include |
| NowUIColorSpace.cginc | 31 | shared include |
| NowUITextGradient.cginc | 73 | shared include |

The glass blur's four passes are the awkward one. Rectangle and text alone unlock the first visible result.

## 6. The first thing a browser host needs is a resource provider

Both probes, desktop and browser, failed the same way before anything could draw: `Resources/NowUI/UIMaterial` and
`Resources/NowUI/NotoSans` could not load. The standalone test project already solves this with a fixture-backed
`INowResourceProvider`, and the Editor exporter already emits the fonts, materials and shader metadata as data. The browser
host needs the same provider reading the same fixture format over `fetch`.

## 7. Colour space

The Unity project runs in Gamma (`ProjectSettings.asset` has `m_ActiveColorSpace: 0`), and the shim defaults to Gamma to
match. The WebGL2 backend must not silently pick an sRGB framebuffer and double-convert.

---

# Slice 1 result and what it exposed (2026-09-07)

The vertical slice renders. `Standalone/Web/NowUI.Web` puts the README quick-start scene on a WebGL2 canvas: a wasm host,
a fetch-backed resource provider over the exported fixtures, GLSL ports of the rectangle and text shaders, and a
`requestAnimationFrame` loop. Side-by-side evidence against Unity's own render of the same snippet is in
`artifacts/local/m2-side-by-side.png`. Geometry, corner radius, translucency, font, weight and text position all match.

## Capturing the canvas: two traps, both worth knowing before writing golden comparison

1. **`canvas.toDataURL` and `drawImage` return an empty image** unless the WebGL context was created with
   `preserveDrawingBuffer: true`. The failure is silent and looks exactly like "nothing rendered". `nowui-gl.js` now
   enables it behind `?capture=1`, off by default because retaining the buffer costs a copy per frame.
2. **Even with that flag, `drawImage(webglCanvas, ...)` into a 2D canvas still came back black**, while `gl.readPixels`
   on the same buffer returned the correct pixels. Read the framebuffer directly; do not go through a 2D canvas.

## Headless capture: works sometimes, and is NOT yet trustworthy (see below)

```
chrome --headless=new --use-angle=swiftshader --enable-unsafe-swiftshader \
       --virtual-time-budget=25000 --window-size=1280,720 \
       --screenshot=out.png "http://localhost:PORT/?capture=1"
```

`--virtual-time-budget` is what makes it wait for the wasm runtime to start and draw; without it the screenshot is blank.
SwiftShader is needed because the default headless GPU path produced an empty canvas. Even with both, the capture is
intermittent: identical invocations return a correct image or a fully black one. Do not build a gate on it until that
is fixed.

## There was no rendering bug. The "squish" was the capture.

An earlier version of this document claimed the projection and the viewport were built from different heights,
because a headless capture measured the panel at (19,112) sized 258x70 instead of (20,20) sized 260x80. That
conclusion was wrong, and the way it was wrong is worth recording.

Measured directly from the live framebuffer with `gl.readPixels`, the browser renders the panel at exactly
**(20, 20), 260 x 80** - the geometry the code asks for, matching Unity. The distortion existed only in headless
Chrome's screenshot, whose canvas comes out 1264x625 for a 1280x720 window.

Two process errors produced the false diagnosis, both worth avoiding next time:

1. **A fix was written before the hypothesis was tested.** Setting the viewport in `beginFrame` was plausible and
   is arguably correct anyway, but it was applied on the strength of an unverified theory.
2. **The A/B test that seemed to confirm it was noise.** Headless capture here is flaky: the same command returns
   a correct image sometimes and a fully black one other times, with no change in between. One black run after the
   change and one good run before it looked like causation. Re-running the "control" produced black too, which is
   what exposed the flakiness. The change was reverted, because the evidence for it never existed.

The lesson for the golden-comparison work: headless capture is not yet trustworthy enough to be the oracle. Fix
its determinism first, or measure through `gl.readPixels` in a live page, which is exact and repeatable.

## Getting a captured image out of the page

Reading pixels is easy; getting the bytes to disk is the awkward part. Transcribing base64 out of a JavaScript
tool result corrupted it silently at 4.6 KB. The reliable route is to let the page download it:

```js
const a = document.createElement('a');
a.href = 'data:image/png;base64,' + base64; a.download = 'shot.png';
document.body.appendChild(a); a.click(); a.remove();
```

The file lands in the browser's download directory and can be picked up from there.

---

# Comparing at matched density (2026-09-07)

Unity's reference images are rendered at 2x. Comparing them against a 1x browser capture scaled up measures the
upscaler, not the renderer, so the browser now renders at 2x too and the two are differenced directly.

## The change

Rendering at 2x is two settings that must move together, because NowUI measures in drawing-buffer pixels:

- the canvas drawing buffer is the CSS box times the device pixel ratio, and
- `Now.StartUI` receives that same ratio as the UI scale.

Doubling only the buffer renders the same layout at half the on-screen size. `main.js` now derives the ratio from
`window.devicePixelRatio`, overridable with `?dpr=N` so a capture can pin it, and the host reports that effective
value rather than the raw one so the C# side scales by the number the buffer was actually sized with.

At `?dpr=2` the canvas is 2560x1440, the viewport matches, and the panel lands at (40, 40) sized 520x160: exactly
twice the 1x geometry, and exactly the framing of Unity's 600x240 reference.

## Result: they agree

Differencing `Docs/media/readme/quick-start-score.png` against the browser's 600x240 capture of the same region:

| Region | Median | 99th percentile | Max |
|---|---|---|---|
| Whole image | 2 | 40 | 252 |
| Panel interior | 1 | 127 | 252 |
| Background above the panel | 7 | 15 | 15 |
| Text band | 1 | 162 | 252 |

A median of 2/255 across the image is agreement. The two tails are both explained:

- **Background, up to 15/255.** Unity's reference has a faint gradient backdrop; the web scene clears to a flat
  colour. A difference in the scene, not in the renderer.
- **Text, up to 252/255 on individual pixels.** This is glyph-edge antialiasing, not misplacement. Measuring the
  white-pixel bounding box of the text in each: Unity spans x 75..414, y 94..140; the browser spans x 75..415,
  y 94..140. Same left edge, same top, same height, one pixel wider at 2x. The worst-differing pixel in the whole
  image sits at x=415, which is precisely that one-pixel overhang. A single pixel of difference at 2x is half a
  pixel at 1x, which is what independent rasterisers of the same distance field are expected to produce.

Worth remembering when reading those maxima: on a hard black-to-white glyph edge, a half-pixel disagreement
produces a 255-wide channel difference. Peak difference is the wrong statistic for text; the bounding box is the
right one.

---

# The browser cache will lie to you (2026-09-07)

Verifying slice 2 cost far more time than it should have, to a failure mode worth naming.

The page reported `draw: the uniform block is 176 floats; 180 were expected` and stopped its frame loop. Both sides of
that contract say 180 on disk, in one file each, with the build newer than both sources. Clearing the Cache API
storage did not help, and neither did a clean `rm -rf bin obj` and rebuild. Instrumentation added to the C# and to the
JavaScript never appeared in the console, while fetching the same JavaScript from the page returned the instrumented
text: the page was serving one file and executing another.

Running the identical URL in headless Chrome with a throwaway `--user-data-dir` printed:

```
[probe] m_Uniforms.Length=180 UniformSlots.Count=180 bytes=720
```

no error, and the app ran. **The code was correct the whole time.** The browser pane was executing a cached build of
the .NET application that survived Cache API deletion, a rebuild, and repeated reloads with new query strings. Note
that the .NET loader caches per URL: `caches.keys()` listed eleven `dotnet-resources-/?...` buckets, one per query
string tried, so each "cache-busting" URL created a fresh bucket instead of bypassing the stale one.

**Rule for verifying this app: a fresh profile, or it does not count.**

```
chrome --headless=new --use-angle=swiftshader --enable-unsafe-swiftshader \
       --user-data-dir=<throwaway> --virtual-time-budget=35000 \
       --window-size=1280,760 --screenshot=out.png "http://localhost:PORT/?capture=1"
```

This also resolves the earlier note about headless being unreliable: the intermittent blank captures were the same
cause. With a fresh profile it has been consistent.

The general lesson is the one that keeps recurring in this milestone: when a measurement contradicts the source,
suspect the measurement apparatus before rewriting the code. Two of the three "bugs" investigated in M2 so far have
been artefacts of how the result was captured rather than defects in what was captured.
