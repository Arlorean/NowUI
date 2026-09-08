# M2 feature matrix: what of NowUI works in a browser

Measured 2026-09-07, **re-measured in part 2026-09-08**. Every **Actual** cell below was produced by loading the
feature gallery in a **fresh headless Chrome profile**, capturing it, reading its console, and — for everything
interactive — dispatching real pointer, wheel, keyboard and clipboard events at it over the Chrome DevTools
Protocol and reading the page's own instrumentation back. Screenshots for every row are in
`artifacts/local/features/`.

The **Expected** column was written by the inventory unit before any capture. It is left exactly as it was, because
a refuted prediction is the most valuable thing in this document. **Eight of its predictions were wrong**, and
§0.3 lists them.

**The 2026-09-08 pass.** Three of §0.2's four "cannot" rows are gone. Two — the Sdf extension and remote loading —
were ported by parallel units and then verified by a third, which is where every row marked *(verified
2026-09-08)* comes from. The third, **gradient-filled text**, was ported later the same day against the flat
capture that pass produced; §3.1 is its account and §3's two gradient rows carry the measurement.
That pass did three things beyond re-capturing: it drove the morph and the warp over a live clock rather than
photographing them once (§7.3), it added `?area=sdf-compose` because a line-by-line read of `README.md`'s SDF
paragraphs against the two existing SDF pages found five advertised behaviours neither page touched (§7.3), and
it measured the remote byte cap from **outside** the page, with resource timing, which **refuted a claim the
remote page was making about itself** (§8.1). Both corrections are recorded in place rather than quietly fixed.

---

## 0. Summary

### 0.1 What someone can build with NowUI in a browser today

**A complete desktop-class application UI.** Not a demo of one — the real thing. Measured, not inferred:

- **Every drawing primitive works.** Rectangles with per-corner radii, outlines, hairline outlines, blur, padding,
  textures and UV sub-rects; circles, ellipses, triangles, convex and concave polygons at any tessellation; lines
  and polylines with widths, three cap styles, dashes with phase, arrow heads and gradient strokes; bezier curves.
- **Text works, at parity.** Four faces, any size, outlines, tabs, newlines, Latin/Greek/Cyrillic, measurement,
  wrapping, the five built-in glyph animations, **and gradient fills — linear at any angle, radial, conic and
  Unity `Gradient` ramps, including under an animation**. Slice 1 matched Unity's own render of the same scene to
  a median of 2/255. The gradient fills are new in the 2026-09-08 second pass; before it, every gradient line
  drew flat. §3.
- **Gradients work** — linear at any angle, radial, conic, all three spread modes, repetitions, Unity `Gradient`
  ramps, and gradient shape styling. This is new in this slice: they drew *nothing at all* before §0.4's fix.
- **Masks work**, all of them: hard rect clip, rounded rect with per-corner radii, circle, ellipse, capsule,
  feathering in screen pixels, nesting, and texture masks.
- **The whole control library works, and is driveable.** Buttons, text fields, sliders, switches, checkboxes,
  scroll views, dropdowns, combo boxes, tab bars, foldouts, radios, badges, chips, numeric and vector fields,
  text areas, tree views, date and time pickers, **and the colour picker, gradient field and curve field** —
  including their popups, which this slice was the first to open.
- **Input works, exactly to contract.** Click, hover, press, drag with the density-scaled threshold, right and
  middle button (correctly remapped from DOM's opposite convention), wheel in canonical notches, Tab focus
  navigation, typing, Backspace, Ctrl+A, and a **full clipboard round trip** through the browser's async
  clipboard API.
- **Six of the seven extensions do something useful**: Markup, CodeEditor, Docking and NodeGraph fully —
  including custom renderer subclassing, node dragging and live bezier links — plus Markdown (everything but
  remote images) and Markdown.Markup (renders, but its embedded controls are inert). Sdf is the seventh.
  **Superseded 2026-09-08: all seven do, Sdf included, and Markdown's remote images load.** §7.2, §7.3.
- **The SDF shape system works** *(verified 2026-09-08)* — nine analytic primitives, six boolean operators,
  composition past two operands, glyph nodes, GPU-baked image and sprite fields, per-node and scoped rotation, a
  clock-driven domain warp, all six distance effects, real distance-field morphs, and the scene used as a mask
  over ordinary NowUI content. §7.3.
- **Remote loading works** *(verified 2026-09-08)* — `INowFetchProvider` over `fetch()` + `ReadableStream` and
  `INowImageDecoder` over a browser pre-decode with a managed PNG decoder under it. Same-origin **and**
  cross-origin: a 26494-byte PNG off `raw.githubusercontent.com` arrives and draws. §8.1.
- **Render to texture works**, after this slice's fix: `SetRenderToTexture`, `NowEffects.Snapshot`, and drawing a
  captured `RenderTexture` back through `SetTexture`.

### 0.2 What they cannot

**One thing, down from four.** The table below is the 2026-09-08 state; the three rows struck through are kept
rather than deleted, because "this was a blocker and is not any more" is the fact a reader of an older copy of
this document most needs.

| Cannot | Why, precisely |
|---|---|
| **Frosted glass** | The pane draws — tinted, rounded, outlined — but the **backdrop behind it is never blurred**, even though the blur pipeline runs in full (10 passes, 160392 px, no fallback recorded). Cause is in frozen runtime code, §5.2. Not re-measured on 2026-09-08; nothing changed in `Assets/NowUI`, so it stands. |
| ~~**Gradient-filled text**~~ | **RESOLVED 2026-09-08 (second pass).** `NowUITextGradient.cginc` is ported. All five gradient lines of `?area=textfx` now render their gradients, and so does the animated one; the flat control stays flat. The reserved sampler slot is now unit 3, bound from `NowRuntime.globals`. §3. |
| ~~**Anything remote**~~ | **RESOLVED 2026-09-08.** `WebFetchProvider` and `WebImageDecoder` are installed and driven; same-origin and cross-origin GETs both arrive and draw, and Markdown's remote image row reaches `Loaded`. §8.1. |
| ~~**The Sdf extension**~~ | **RESOLVED 2026-09-08.** Both programs are ported: `IsPortedShader` now lists ten, `NowUI/SDF Scene` and `Hidden/NowUI/SDF Image Field` included. §7.2, §7.3. |

**One thing inside the Sdf extension still cannot work, and it is worth naming separately** because the README
advertises it: `NowSdfBuilder.SetMaterial(customMaterial)` — the "custom final-shading" behind the README's x-ray
lens and its aurora/topographic/paper-cutout gallery. A custom final shader is a user-authored Unity `Material`
whose `Shader` this host would have to compile, and there is no shader compiler here: `WebGL2Backend.DrawMesh`
throws on any program outside the ten hand-ported GLSL ones. Every SDF *scene* built on the stock material works;
a scene that replaces the material does not. §7.3.

Plus the genuinely Unity-only surface — UGUI, world-space, UI Toolkit, URP/HDRP, IMGUI, the model preview, the
Input System key bindings and the native plugins — enumerated in §9.

### 0.3 Predictions the measurement refuted

| The matrix said | It is actually | Why the prediction was wrong |
|---|---|---|
| `Now.Bezier` **fails** | **WORKS** | `NowUI/UI Bezier` was ported by a parallel unit between the prediction and the capture. |
| `ColorPicker` **fails** | **WORKS**, popup and all | Same: `NowUI/Color Picker` is ported. The `fields` area was still excluding it "so it cannot kill the area". |
| `AnimationCurveField` / `CurveField` **fails** | **WORKS** | Same, via `NowUI/UI Bezier`. |
| `SetRenderToTexture`, `NowEffects.Snapshot` **not ported** ("CreateRenderTexture throws") | **WORK**, after a host fix | Every render-target backend method is implemented. The real blocker was a 0×0 viewport, §5.3 — a different bug with an identical symptom. |
| `Now.Gradient` and all gradient rows **work** | **were BROKEN**, now fixed | A UGUI-only material gates the non-UGUI path, §5.1. Nothing drew. |
| `Now.Glass` **fails** on the shader | **PARTIAL** — draws, does not blur | Both glass programs are ported and the render target allocates. The failure is further downstream than any prediction reached. |
| NodeGraph **partial** ("stops the frame on its first wire") | **WORKS** entirely | Bezier again. |
| The seven extensions **not wired** | all seven **are referenced** | `NowUI.Web.csproj` links them now. |

Two of those eight ("expected to fail, actually works") were stale by the time the capture ran, which is the
ordinary cost of parallel units. The other six were wrong on the merits, and three of them — gradients, glass,
render-to-texture — were wrong in the direction that matters most: a feature reported as fine that is not, or a
cause named confidently that is not the cause.

**Three more were refuted on 2026-09-08**, and the last of them is the only one in this document where a *page's
own instrumentation* was the thing that was wrong.

| The claim | It is actually | How it was caught |
|---|---|---|
| §0.2: **Sdf cannot draw**, **nothing remote works** | Both **WORK** | Ported by parallel units between that capture and this one. Stale rather than wrong, and recorded because a reader of the older table would act on it. |
| `?area=remote`'s own footer: "**cap-declared is the half that does save it here**, refusing on Content-Length before a byte is read" | **It saves nothing here.** Both cap probes refuse correctly and deliver 0 bytes to the sink — and `performance.getEntriesByType('resource')` reports `transferSize` **1297005 for both** 1.3 MB requests. Over loopback the whole body lands in ~10 ms, before script sees the response headers, so neither abort has anything left to save. | Measured from **outside** the page. The page could not see this: it reads its own sink counters, and a sink that received 0 bytes looks identical whether or not the wire carried 1.3 MB. Worse, the first attempt at the measurement was itself confounded — both cap probes fetched the *same* URL, so the second was a memory-cache hit reporting `transferSize` 0, which looks exactly like a saved transfer. The probes now carry a distinct query string each. §8.1. |
| `?area=sdf-compose`'s first capture: a warped rounded box whose edge was a band of **per-pixel dither** — read as a possible defect in the ported noise | **The port is correct**; the request was wrong. `warpScenePos` computes `scenePos / scale` (`NowSdfShaderV2.cginc:1421`), so `scale` is a **wavelength, not a frequency**, and the 0.06 asked for was ~17 cycles per scene unit — aliasing, faithfully rendered. | Opening the include before believing the picture. The GLSL in `nowui-gl.js:2691-2721` is a line-for-line match of the HLSL, hash included. §7.3. |

The third row is the same lesson `M2-Scouting.md` closes on, arriving from a new direction: **suspect the
measurement before the code.** Three of the four "bugs" chased across M2 have now been artefacts of how a result
was captured — a headless screenshot, a browser cache, and now an HTTP cache plus a misread parameter.

### 0.4 What the 2026-09-07 slice changed

Three edits, all in `Standalone/Web/NowUI.Web`, all of which turned a measured failure into a measured pass. None
of them touched `Assets/NowUI` or `Standalone/NowUI.Engine`.

1. **`WebResourceProvider.AliasCanvasMaterials`** — mints the `*UGUI` material templates the exporter deliberately
   drops. Fixes gradients entirely (§5.1).
2. **`Program.Frame` binds the back buffer** (`RenderTexture.active = null`) at the top of every frame. Fixes
   render-to-texture and `NowEffects.Snapshot` (§5.3).
3. **A new `?area=pickers` and `?area=rendertexture`**, plus `?trace=N` and two one-shot diagnostics in
   `WebGL2Backend`, because three features were being reported as blocked by blockers that had already been
   removed, and two failures produced no error at all.

### 0.5 The defects found in frozen code

All are in code the measuring slice is not allowed to edit. Each is stated with the file and line, and each is
supported by a measurement rather than by reading.

1. **`Assets/NowUI/Runtime/Now.cs:2192` throws away the glass backdrop it just computed.** §5.2.
2. **`Assets/NowUI/Editor/NowStandaloneAssetExport.cs:196-198` excludes UGUI materials that non-UGUI code
   requires.** §5.1.
3. **`Standalone/NowUI.Engine/Graphics/NowImmediate.cs:105` will bind a zero-sized viewport without complaint.**
   §5.3.
4. **`Assets/NowUI/Runtime/Controls/NowDropdown.cs:95-105` drained a cross-frame one-shot with no
   passive/measure guard** — found 2026-09-08, **FIXED in the package the same day** (see the closing paragraph of
   this item; this is the one defect in this list that is no longer open). The popup hands its choice to the next
   frame through
   `NowControlState.Get<int>(id, "pending")` and `Draw` consumes that slot unconditionally, including while
   `NowLayout.isMeasurePass` / `NowInput.isPassive` are set. Under `NowLayout.RunMeasured` — two passes — the
   MEASURE pass therefore eats the selection, and the C# gallery survives only because its `ref s_Resolution`
   persists between the two passes; even there the real pass returns `changed == false`, so a C# caller under
   `RunMeasured` gets the value and loses the event. A caller whose state is re-seeded from outside on every
   pass, which is what the JavaScript bridge is by design (§5.6 of `M3-Spec.md`), loses the value as well. Same
   shape at `NowComboBox.cs:142` and `:242`, `NowValueControls.cs:506-519` (the colour picker) and
   `NowDatePicker.cs:181-190`. `NowTimePicker` was recorded here as NOT affected, wrongly (see the eleventh site below) — it derives its value from persistent `TimeParts`
   each pass rather than from a one-shot — which is what makes this a shape rather than a guess.
   **Measured four ways**: headless, in a two-pass frame with a synthetic pointer, bridge shape loses the value
   and gallery shape keeps it; and in the browser, same build and same driven click, `?app=app` and `?app=popups`
   commit nothing before the host-side carry and commit correctly after
   (`artifacts/local/popup-fix/app-dpr1-UNFIXED-*` against `app-dpr1-*`).

   **THE FIX (2026-09-08), in the package.** Guarding only the *clear* is not enough, and the headless repro
   proves it: it repairs the bridge shape but leaves the gallery shape returning `changed == false`, because the
   measure pass has already written the caller's persisting variable and the live pass then compares the new
   value against itself. The guard therefore covers the whole commit — `if (!NowInput.isPassive && pending …)` —
   so a passive pass observes without mutating, which is the rule `NowControlState.AdvanceTransition`
   (`:313`) and `RepeatByStateKey` (`:486`) already followed. It costs one frame of measurement against the
   previous label, self-correcting on the next frame, and that is strictly cheaper than a destroyed choice.
   Applied at **eleven** sites. The first report of this said eight and then listed nine, which was simply a
   miscount; the tenth is `NowFilePicker` and the eleventh `NowTimePicker`, both found later by a second sweep and
   described below. Four were found by
   sweeping for the shape rather than reported:
   `NowDropdown.cs`, `NowComboBox.cs` ×3 (the int overload, the string overload, and `pendingCustomValue`, the
   free-text commit), `NowMaskField.cs` (whose clear sat *outside* the `if`, so a passive pass ate it
   unconditionally), `NowValueControls.cs` ×3 (colour, gradient, curve) and `NowDatePicker.cs`.
   **A tenth site, and why the first sweep could not see it.** `NowFilePicker.ApplyPending`
   (`NowFilePicker.cs:386`, called unconditionally from `:313`) drains `state.hasPendingPath` with no guard. The
   sweep that found the other nine grepped `NowControlState.Get<...>("pending")`, and the picker keeps its
   one-shot in a **private `Dictionary<NowResolvedId, PopupState>`** (`:166`) instead, so a name-and-container
   sweep was structurally incapable of finding it. Measured with the latch set exactly as `Commit()`
   (`:3238-3240`) sets it, then ordinary frames: without the guard, one-pass commits and two-pass reports
   `changed == False`; with it, both commit. Not reachable from JavaScript, since the picker is outside the
   JavaScript surface.

   **An eleventh site, and the reason a shape-based sweep still missed it.** `NowTimePicker` carries no one-shot
   at all. It keeps a persistent `TimeParts` mirror and, on every pass, writes the caller's value whenever the
   mirror and the value differ (`NowTimePicker.cs:147-161`). That is the same defect with the latch inverted: the
   measure pass performs the write, and the live pass then compares the value against itself and reports nothing.
   Earlier notes in this document asserted the opposite, that the picker "derives its value from persistent
   TimeParts each pass rather than from a one-shot - which is what makes this a shape rather than a guess." The
   reasoning was right and the conclusion was wrong; persistence is what makes it vulnerable, not what protects
   it. Measured through the real control, opening the popup with a click and moving the hour on the real keyboard
   path: without the guard the hour reaches 08:30 and the live pass reports `changed == false`; with it, both
   pass shapes report the change. `NowPopupUXTests.MeasuredFrameReportsTheTimePickerChangeItApplied` fails
   without it.

   The bridge was never affected here, which is why the browser looked fine: `Replay.Controls.cs` re-seeds the
   value from the op stream on every pass, so the mirror differs from the value on both passes and both report
   the change. Only a C# caller whose variable persists loses the event.

   **The trigger set is wider than "someone called RunMeasured".** Four shipped host components hard-code the
   two-pass path for the whole user callback, every frame: `NowLayoutGraphic.cs:17`,
   `NowPipelineLayoutGraphic.cs:9`, `NowWorldLayoutGraphic.cs:9` and `NowVisualElement.cs:707`, the auto-sizing
   UI Toolkit host. So an author who never writes `RunMeasured` still gets the two-pass frame by default, which
   is what makes this class worth sweeping for rather than patching where reported.

   **The first version of this fix shipped a leak, recorded here rather than quietly corrected.** Moving
   `pending = 0` inside the guard put it behind the range test as well, so a latch whose index no longer fit a
   shrunken option list was neither applied nor cleared. It then committed silently on the first later frame
   whose list was long enough again, with `changed == true` and no user input. Measured against a genuine
   pre-fix tree: pre-fix `selected=0 changed=False`, leaking version `selected=2 changed=True`. The drain is now
   unconditional on a live pass and sits outside the range test, and
   `NowPopupUXTests.AnOutOfRangeLatchIsDiscardedRatherThanCommittedLater` fails without that.
   **Verified**: Unity EditMode and PlayMode identical to the baseline case-for-case
   (`Tools/Standalone/Compare-TestResults.ps1`, `artifacts/local/popup-fix-check`); the standalone gate at
   801 passed / 9 skipped; all six rows of the headless repro committing; and the three browser fixtures
   (`?area=fields`, `?app=app`, `?app=popups`) committing with the host-side carry **deleted**.
   **Guarded** by `NowPopupUXTests.MeasuredFramePublishesTheDropdownChoiceItCommitted`, which fails without the
   fix on "the live pass must report the change, not just leave the value behind" — the Unity-visible half of the
   defect — and by the `?app=popups` page for the JavaScript path.

### 0.6 What the 2026-09-08 verification pass changed

Three edits, all in `Standalone/Web/NowUI.Web`, none of them touching `Assets/NowUI` or
`Standalone/NowUI.Engine`. Two of the three exist because a measurement caught a page over-claiming about itself.

1. **`GalleryAreas.SdfCompose.cs` — a new `?area=sdf-compose`.** Composition past two operands, `RotateNext`,
   `PushRotation`/`PopRotation`, `SetWarp`, a `Sprite` node, and `BeginMask()` clipping ordinary NowUI content.
   Every row carries a control beside it, because each of these can fail by quietly doing nothing and "quietly
   did nothing" and "worked" are the same picture with nothing to compare against. §7.3.
2. **`GalleryAreas.Remote.cs` — a distinct URL per byte-cap probe.** Both fetched the same URL, so the second was
   a memory-cache hit and its `transferSize` of 0 read as a saved transfer. §8.1.
3. **`GalleryAreas.Remote.cs` — the cap footer rewritten to what was measured.** It claimed `cap-declared` saved
   the transfer. It does not, here. §0.3, §8.1.

No new defect in frozen code was found by that pass. §0.5's first three still stand as written; its fourth was
added later the same day by the popup-commit unit, which was the first to drive a dropdown popup rather than
photograph it closed.

---

## 1. How to reproduce any row

```bash
# 1. Build and serve. Port 5103 is the launch profile's HTTP URL. If it is already taken - a parallel unit's
#    server will take it - pick your own rather than capturing against someone else's build:
#      ASPNETCORE_URLS=http://localhost:5191 dotnet run -c Release --no-launch-profile
cd Standalone/Web/NowUI.Web
dotnet build -c Release
dotnet run   -c Release --launch-profile NowUI.Web

# 2. Capture one area, in a FRESH profile, with retries for the blank-capture flake.
Tools/Standalone/WebCapture/capture-area.sh <area> artifacts/local/features 5103

# 3. Drive one area with real input and read its instrumentation back.
node Tools/Standalone/WebCapture/drive.mjs <script.json>
```

`capture-area.sh` is the fresh-profile invocation from `M2-Scouting.md` with a retry around it:

```bash
rm -rf <profile>
chrome --headless=new --use-angle=swiftshader --enable-unsafe-swiftshader \
       --user-data-dir=<profile> --virtual-time-budget=35000 --window-size=1180,760 \
       --enable-logging=stderr --screenshot=area-<name>.png \
       "http://localhost:5103/?capture=1&area=<name>"
```

**A fresh profile every time, or the result does not count.** The .NET loader caches per URL and survives cache
clearing, rebuilds and query-string busting (`M2-Scouting.md`, "The browser cache will lie to you").

### 1.1 Three things learned about the instrument itself

- **The blank-capture flake is real and frequent.** Roughly one run in three returned a 4290-byte (or zero-byte)
  PNG with a clean console. `capture-area.sh` retries any capture under 12 KB; without that, several areas in this
  sweep would have been recorded as failures.
- **`--screenshot` and `Page.captureScreenshot` return different frames.** The former photographs the *window*
  (1180×760, with the canvas letterboxed inside it); the latter photographs the *viewport* (1164×609). A
  coordinate read off one is not usable in the other, and mixing them cost one whole drive run in which every
  event landed ~150 px below its target and every counter read zero. Ask the page instead: `?debug=1` exposes
  `window.nowui.debugRects()`, which reports each control's rect in CSS pixels.
- **Typing has to be key events.** `Input.insertText` types nothing, because `nowui-input.js` reads characters from
  `e.key` on `keydown` and insertText never synthesises one. Likewise `Input.dispatchMouseEvent` needs the button
  *mask* to match the button (left 1, right 2, middle 4) or a right-press produces nothing. Both are documented at
  the top of `drive.mjs`.

### 1.2 Query parameters

| Parameter | Effect |
|---|---|
| `?area=NAME` | Which feature area to draw. Default `controls`. An unknown name lists every name that exists. |
| `?capture=1` | Keeps the drawing buffer and exposes `window.nowuiCapture`. |
| `?debug=1` | Exposes `window.nowui.debugState/debugRects/step`. |
| `?animate=1` | Hands animated areas the wall clock; off by default so two captures of one area match. |
| `?dpr=N` | Pins the device pixel ratio. |
| `?trace=N` | **New.** Prints the backend's call sequence for N frames: BeginFrame, SetRenderTarget, SetViewport, DrawMesh, Blit, ClearRenderTarget, EndFrame. Added for §5.3, which raises nothing. |
| `?rtcheck=1` | Runs `RenderTargetSelfTest`'s in-page render-target contract suite and prints its results. |
| `?sdftext=1` | Adds the glyph-node cell to `?area=sdf`. Off by default because an unported program takes the whole frame down rather than one cell, and glyph nodes are the one path there whose field is a texture. |
| `?xorigin=1` | Adds the one probe on `?area=remote` that leaves the machine. |
| `?size=WxH`, `?chrome=0`, `?rt=1`, `?sdfprobe=1` | As before. |

### 1.3 Status vocabulary

| Status | Means |
|---|---|
| **WORKS** | Exercised and behaves. Where a row is only rendered and not driven, the note says so. |
| **PARTIAL** | Works with a specific stated limitation. |
| **BROKEN** | Should work, does not. The note carries the error and the diagnosis. |
| **NOT PORTED** | Needs work this slice did not do. The note says what that work is. |
| **UNITY ONLY** | Cannot apply to a browser. The note says why. |

---

## 2. Drawing primitives

Evidence: `area-rectangles.png`, `area-shapes.png`, `area-lines.png`, `area-bezier.png`. All four consoles clean.

| Feature | Expected | Actual | Evidence |
|---|---|---|---|
| Fill colour | works | **WORKS** | Every cell of `area-rectangles.png`. |
| Uniform corner radius | works | **WORKS** | radius 0 / 16 cells. |
| Per-corner radius | works | **WORKS** | The 28/4/28/4 cell rounds its top corners and squares its bottom ones — the packing is `(TR, BR, TL, BL)` and the y sense is right. Confirmed again on the mask side at 44/4/44/4, where a 180° y flip would have been invisible on a symmetric shape. |
| Per-side padding | works | **WORKS** | "padding 16/8" cell. |
| Outline width and colour | works | **WORKS** | "outline 2". |
| Hairline outline (< 1 AA width) | works | **WORKS** | "hairline outline 0.5" is visible, so the `max(outline, delta)` clamp survived the port. |
| Blur (soft edge) | works | **WORKS** | blur 2 / 8 / 14 are three distinct softnesses; the quad grows and `rawUV` leaves `[0,1]` without artefacts. |
| Translucency over drawn content | works | **WORKS** | Three alphas (0.25/0.50/0.75) over an opaque amber ground read as three distinct densities. Premultiplied blend is right. |
| `SetTexture` | works | **WORKS** | Checkerboard cell, and "texture x colour". |
| `SetUV` sub-rect | works | **WORKS** | The quarter crop shows a 4×4 checker where the default shows 8×8, and the radius does **not** follow the crop — the shape SDF stayed in full-quad space. |
| `SetPreserveAspect` | works | **WORKS** | Stretched-wide textured cell. |
| `SetSprite`, sliced sprites | untested | **NOT PORTED (untested)** | Nothing in the host constructs a `Sprite`. `Sprite` exists in the shim; no area covers it. Adding one is a gallery job, not a renderer job. |
| `SetMaterial` (custom material) | fails | **NOT PORTED** | `WebGL2Backend.DrawMesh` resolves programs by shader *name* against a set of eight. Any other name throws with the name in the message. Not exercised — no area supplies a custom material. |
| `Now.Circle`, `Now.Ellipse` | works | **WORKS** | `area-shapes.png`, filled, outline-only, and fill+outline. |
| `SetSegments` | works | **WORKS** | "8 segments" reads as an octagon; the parameter reaches the tessellator. |
| `Now.Triangle` | works | **WORKS** | Filled and outlined. |
| `Now.Polygon`, concave and convex | works | **WORKS** | Five-point and nine-point stars (concave) and a convex 9-gon. |
| Shape fill / outline / both | works | **WORKS** | |
| `Now.Line`, `Now.DrawPolyline` | works | **WORKS** | `area-lines.png`: widths 1/4/7/10, a zigzag polyline, a 49-point sampled sine. |
| Caps: Butt / Round / Square | works | **WORKS** | Three visibly different end treatments. |
| Dashes, dash offset | works | **WORKS** | "dash 14 / gap 8" and the same dash at phase offset 7 are out of step with each other, so the offset is honoured. |
| Arrow heads | works | **WORKS** | End and Both, sized 18×14. |
| Gradient stroke (`SetGradient`) | works | **WORKS** | Vertex colours along the stroke. |
| `Now.Bezier` | **fails** | **WORKS — prediction refuted** | `area-bezier.png` draws a solid cubic and a dashed one with an arrow head. `NowUI/UI Bezier` is in `WebGL2Backend.IsPortedShader`. The area's own on-screen text still predicts failure; the picture contradicts it. |

> The inventory's footnote correction — that `Now.Circle`/`Triangle`/`Polygon` need no shape shader, only the
> ported rectangle program — is confirmed. The Sdf extension's shape algebra is a different feature (§7.2).

---

## 3. Text

Evidence: `area-text.png`, `area-textfx.png` (and `area-textfx-before-gradientport.png`, the flat capture the
gradient rows below used to describe).

| Feature | Expected | Actual | Evidence |
|---|---|---|---|
| Glyph rendering at any size | works | **WORKS** | 11 / 14 / 18 / 24 / 34 / 48 px, all crisp. |
| Packed SDF16 decoding | works | **WORKS** | Implied by the above: the median-RGB branch renders visibly blurry text and this is not that. `_NowUITextSdfEncoding` resolves to 1 as `M2-ShaderPort.md` §4.4 predicted. |
| Bold / Italic / BoldItalic faces | works | **WORKS** | Four visually distinct faces. Not a fallback to Regular. |
| Text outline (`SetOutlinePixels`) | works | **WORKS** | "Outlined, 2px" over an amber ground. Screen-pixel width. |
| Tabs and newlines | works | **WORKS** | Both inside one `Draw(string)`. |
| Non-ASCII | works | **WORKS** | `Äéîõü ßçø Γαμμα Да — "quoted" …` all render. Greek, Cyrillic, em dash, curly quotes, ellipsis. |
| Emoji / colour-font glyphs | not ported | **NOT PORTED** | Colour faces need the native CFF/MTSDF path, compiled out by `NOWUI_VG_DISABLE_NATIVE`, and no colour font is exported. Not exercised. |
| `MeasureText` / `MeasureTextBounds` | works | **WORKS** | The drawn box fits the string exactly. |
| Wrapping (`NowTextWrap`) | works | **WORKS** | A paragraph wrapped to 476 px, measured 475×95. |
| Runtime font compilation (`NowFontCompiler`) | works | **WORKS** | It is how the atlas in every capture here was produced: managed TrueType parse plus managed SDF rasteriser, 1024×1024 RGBA. |
| **Text gradients — linear / radial / conic** | **uncertain** | **WORKS — ported 2026-09-08, was NOT PORTED** | `area-textfx.png` draws a flat control and five gradient variants of the same string. The control stays flat; the five carry their gradients, and each carries the **right** one. Measured off the PNG rather than eyeballed, because "some colour variation" is not the claim: `linear 0deg` is constant across x and runs magenta→cyan down the glyph (CSS 0° is *up*, `NowText.cs:442`); `linear 90deg` is constant down y and runs cyan→magenta across; `radial` is cyan at the bounds centre and magenta at both ends; `conic` sits in a narrow wedge because its centre is the middle of the full-width text rect while the string occupies the left half — geometrically right, and the one row whose picture is least obvious. The backend's old once-per-session report is gone with the branch it guarded. §3.1. |
| Text gradient from a Unity ramp | uncertain | **WORKS — ported 2026-09-08** | Same branch, plus the one resolution path nothing else on the page exercises. `Gradient ramp keys` runs green→amber→red down the glyph, four colour keys off a `UnityEngine.Gradient`. `_NowGradientRampTexture` is a shader **global** (`Shader.SetGlobalTexture`, `NowGradient.cs:580`), so it reaches the draw only through `NowRuntime.globals` — a third lookup `TextureId` already did and nothing had yet used. |
| Glyph animations (Typewriter, FadeIn, FadeUp, ScaleIn, Wave) | works | **WORKS** | At the frozen 0.45 s clock, Typewriter shows "Typewr" mid-type, FadeIn/FadeUp/ScaleIn show per-glyph stagger, Wave is displaced off its zero crossing. All five mid-flight. |
| Glyph animation + gradient together | — | **WORKS — was PARTIAL** | Both at once: the "Gradient + animation" line is displaced and faded per glyph *and* carries its ramp. The gradient is in absolute UI space, not per-glyph, so it does not travel with a glyph the animation moves — which is the same behaviour Unity gives (`NowGradient.cs:763-767`). |
| Rich text tags (`NowRichText`) | works | **NOT PORTED (untested)** | No area covers it. Expected to work — it is a parser over the same two primitives — but this slice did not test it, and "expected" is not a result. |
| Rich text hit testing / selection / copy | works | **NOT PORTED (untested)** | Same. The bridges it needs (pointer, clipboard) are both proven (§6), and markdown's own text selection works (§7.1), so the risk is low; it is still untested. |
| Lottie tag inside rich text | partial | **NOT PORTED (untested)** | No `.json` animation is exported into `wwwroot/Fixtures`. |
| Text preprocessor hook | works | **NOT PORTED (untested)** | Pure managed callback; no area. |
| HarfBuzz shaping (`Now.textShaping`) | not ported | **NOT PORTED** | Native plugin, compiled out. The per-codepoint path is what runs, so ligatures and complex scripts are unshaped. |

### 3.1 The gradient branch, and what porting it actually took

`M2-ShaderPort.md` §4.5 let slice 1 omit the text shader's gradient branch **on condition that a text vertex with
`extras.w != 0` be reported**. The condition was met, the report fired, and this is the fix it asked for. Four
things are worth carrying forward.

**It is one file, and it is generated.** The maths lives in `Standalone/Web/NowUI.Web/wwwroot/shaders/nowui-text-gradient.glsl`,
a line-for-line port of `NowUITextGradient.cginc`, and its copy inside `nowui-gl.js` is produced by
`tools/embed-shader.py` rather than typed twice — the tool the SDF port introduced for exactly this. The rest of
the text program is still hand-maintained in both places, which is a known gap: the slice-1 pair names the outline
varying `vOutlineColor` in the annotated files and `vOutline` in `nowui-gl.js`, and generating either would mean
renaming a varying across four files. The reason to care is not tidiness. A one-off comparison of the two sources
during this port caught a **backtick inside a JS template literal** that would have made `nowui-gl.js` fail to
parse; hand-maintained shader text is exactly where that class of mistake lives.

**§4.5 named three HLSL→GLSL traps; there are four.** `frac`→`fract`, `fmod`→a written-out `hlslFmod` (never
`mod`), `atan2(y,x)`→`atan(y,x)` **with the same argument order** — and the fourth, which §4.5 did not name:
`sampler2D` defaults to **lowp** in a GLSL ES fragment stage, so `_NowGradientRampTexture` has to be declared
`highp` or the ramp is quantised on the way in.

**The ramp arrives as a shader global, and that is now confirmed rather than assumed.** §4 proved the atlas
reaches `NowUI/UI Gradient` — but through that material's `_MainTex`, which is a different path. The text shader
reads the same 256×256 atlas from `_NowGradientRampTexture`, which `NowGradientRampCache.EnsureTexture` publishes
with `Shader.SetGlobalTexture` on the first ramp **allocation** (`NowGradient.cs:580`). It is on no material and
in no property block, so `WebGL2Backend.TextureId` finds it only on its third and last lookup,
`NowRuntime.globals`. It is bound to texture unit 3 — the slot `nowui-gl.js` had reserved for it since slice 1.

**The guard was re-aimed, not deleted.** An unbound sampler falls back to 1×1 opaque white, every ramp texel comes
back `(1,1,1,1)`, and `fillColor *= ramp` leaves the glyph at its flat colour — *the same picture the unported
branch produced*. So `WebGL2Backend.WarnOnMissingTextGradientRamp` now reports a text vertex that asks for a
gradient while the global is unset, naming the atlas row it wanted. It scans only while the ramp id comes back
empty, so the walk stops happening the moment any gradient anywhere in the frame allocates a row.

**What this does not cover.** Only what `?area=textfx` draws was measured: `Clamp` spread, one repetition, and the
ellipse radial. The shader ports `Repeat` and `Mirror` spread, repetition counts, the circle radial and
`GradientMode.Fixed` banding faithfully — they are three lines each — but no capture exercises them, and "ported"
is not "measured". Adding a gradient row to `?area=textfx` for those five is one area edit away.

---

## 4. Gradients, glass, ripple, effects

Evidence: `area-gradients.png` (and `area-gradients-before-uguialias.png`), `area-glass.png`, `area-ripple.png`,
`area-effects.png`, `area-rendertexture.png`.

| Feature | Expected | Actual | Evidence |
|---|---|---|---|
| `Now.Gradient` linear, by angle | works | **WORKS — after this slice's fix; was BROKEN** | See §5.1. Before: seventeen labelled cells and not one swatch. After: 0°, 45°, 90°, 135° all correct. |
| Radial (ellipse and circle), off-centre | works | **WORKS** (after the fix) | Including centre (0.25, 0.30). |
| Conic, with start angle | works | **WORKS** (after the fix) | 0° and 120° differ correctly. |
| Spread: Clamp / Repeat / Mirror, repetitions | works | **WORKS** (after the fix) | At ×3 the three modes are visibly distinct — Repeat shows hard seams, Mirror shows reflected bands, Clamp shows one ramp with flat ends. |
| Unity `Gradient` ramp keys | works | **WORKS** (after the fix) | Four colour keys, blue→green→amber→red, linear and radial. **This is the proof that the `_NowGradientRampTexture` shader-*global* resolution path works**, which nothing else in the gallery exercises. |
| Gradient shape styling (radius, outline, blur, tint) | works | **WORKS** (after the fix) | Per-corner radius + outline, blur 10, tint alpha 0.4. |
| `Now.Ripple` | works | **WORKS** | `area-ripple.png`: phase 0.20/0.45/0.70/0.95 from a centre origin expand and fade; from a bottom-left origin on a pill radius they expand and fill. |
| **`Now.Glass`** | **fails** | **PARTIAL — draws, does not blur** | See §5.2. The pane renders with its tint, corner radius and outline; the striped backdrop behind it stays **perfectly sharp** (zoom of `area-glass.png`: hard stripe edges inside the pane). |
| Glass blur quality levels | fails | **BROKEN** | The blur *runs*: NowGlass's own diagnostics report `panes=1 entries=1 fallbacks=0 copiedPixels=160392 blurredPixels=160392 blurPasses=10`, `host=LegacySelfReplay reason=None quality=Balanced radius=18 src=978x164 blurred=978x164 downsample=1 iterations=5 passes=10`. Ten passes of `NowUI/UI Glass Blur` into a real render texture, no fallback. And the result is discarded — §5.2. |
| `NowEffects.Modifier` mesh capture | works | **WORKS** | `area-effects.png`: Wave and Genie against an undeformed control. |
| `SetSubdivision` | works | **WORKS** | Subdivision 6 bends smoothly; subdivision 1 is visibly faceted. The parameter reaches the mesh. |
| `SetSubdivideText` | works | **WORKS** | "Deformed text" bends per glyph rather than moving rigidly. |
| `NowDeformers.Wave`, `NowDeformers.Genie` | works | **WORKS** | |
| **`SetRenderToTexture`** | **not ported** | **WORKS — after this slice's fix** | See §5.3. `area-rendertexture.png` draws the same wave twice, once through a `RenderTexture` and once in place, and they agree in shape. One difference worth recording: the render-to-texture copy is **clipped to its capture rect**, so deformation that leaves the source rectangle is cut off, where the in-place version overflows freely. Whether that matches Unity is untested. |
| **`NowEffects.Snapshot`** | not ported | **WORKS — after this slice's fix** | Target allocated at 523×96; the captured texture drawn back through `SetTexture` shows the card. The source position stays empty, which is what a snapshot means — it diverts the drawing rather than duplicating it. |

---

## 5. The three defects, in full

These are the substance of this slice. Each is a measurement, then a cause, then what was done about it.

### 5.1 Gradients drew nothing, because a UGUI-only material gates the non-UGUI path

**Measured.** `?area=gradients` rendered every label and every caption and **not one gradient**
(`area-gradients-before-uguialias.png`). One console line, at start-up:

```
[NowUI] NowUI: required bundled resource 'Resources/NowUI/GradientMaterialUGUI' failed to load;
whatever depends on it will not render.
```

**Cause.** `NowGradientMaterials.TryGet` (`Assets/NowUI/Runtime/NowGradient.cs:642-696`) loads two templates and
ends:

```csharp
return material != null && canvasMaterial != null;
```

`canvasMaterial` is `NowUI/GradientMaterialUGUI`. `NowGradient.Draw` returns early at `NowGradient.cs:967` when
`TryGet` is false. And `Assets/NowUI/Editor/NowStandaloneAssetExport.cs:196-198` skips every resource path ending
in `UGUI`, with the comment *"The UGUI variants are excluded by design: NOWUI_UGUI is compiled out of the
standalone build"*. That is true of the UGUI **hosts** and false of `NowGradient.cs`, which is compiled in and
demands the UGUI material's mere existence. The result is that the entire gradient feature is disabled outside
Unity, silently, by an exporter rule that reads as safe.

`NowGlass.cs:334` loads `NowUI/GlassMaterialUGUI` the same way. That one is not a gate — it is passed on as a
batch key's canvas twin — so glass drew anyway, with the same warning and a null in a batch record.

**Done.** `WebResourceProvider.AliasCanvasMaterials` clones each non-UGUI template under its `*UGUI` path. Sound
only because the canvas material is used exclusively by UGUI hosts, which cannot exist here — nothing ever renders
through the clone; it exists to satisfy a null check. Gradients then work completely (`area-gradients.png`).

**Not fixed.** This is a host-side workaround. The repair is either exporting the UGUI templates (three lines in
the exporter's skip test) or relaxing `TryGet` to require only the non-UGUI material. The first touches the frozen
Unity tree; the second touches frozen runtime code. **Reported, not done.**

### 5.2 Glass blurs the backdrop, then throws it away — in frozen runtime code

**Measured, twice, independently.**

1. NowGlass's own diagnostics, rendered under the pane in `area-glass.png`:
   `panes=1 entries=1 fallbacks=0 copiedPixels=160392 blurredPixels=160392 blurPasses=10`, and per pane
   `host=LegacySelfReplay reason=None quality=Balanced radius=18 src=978x164 blurred=978x164 downsample=1
   iterations=5 passes=10`. The blur ran, in full, into a real render texture, with **no fallback recorded**.
2. The backend, at the first `NowUI/UI Glass` draw:
   `first 'NowUI/UI Glass' draw resolved _NowGlassUseBackdrop = 0`.

Zero selects the shader's no-backdrop branch. So the pane is a translucent tinted rounded rect, and the ten blur
passes are paid for and discarded.

**Cause**, in `Assets/NowUI/Runtime/Now.cs`:

- `:2072` — when `NowGlassRenderer.CanDrawSelfReplay(batch)`, `FlushLegacyGlassReplay` calls
  `DrawLegacySelfReplayGlass`.
- `:2166` — that method blurs into a temporary target and calls
  `NowGlassRenderer.EnableBackdropGlobal(blurred, capture.backdropUvTransform)`.
- `:2167` — it then calls `DrawLegacyReplayBatch(batchIndex, drawMatrix, false)` to draw the pane.
- `:2192` — and `DrawLegacyReplayBatch`'s **first statement**, for every `NowMeshKind.Glass` batch, is
  `NowGlassRenderer.DisableBackdropGlobal()`.

The enable at `:2166` is cancelled by the disable at `:2192` before the pane is drawn. Two lines apart.

**Correcting the backend's own comment.** `WebGL2Backend.cs` attributes the zero to `Now.cs:1155`, the *plain*
immediate path, which records a `LegacyImmediatePath` fallback. That is not the path that ran here: the
diagnostics say `host=LegacySelfReplay` and `reason=None`. The conclusion (the flag is zero) is right; the
mechanism named is not. The comment is now corrected in place, next to the measurement.

**Not fixed.** `Assets/NowUI` is frozen. This is not a browser defect — a Unity host driving the same immediate
`Now.StartUI` path would take the same two lines — so it belongs upstream, not here.

### 5.3 Render-to-texture blanked the whole canvas, silently, via a 0×0 viewport

**Measured.** `?area=rendertexture` rendered **nothing at all** — clear colour only, no panels, no text — with a
**completely clean console**: no exception, no GL error, no warning. The frame loop did not even latch off.

`?trace=2` (added for this) printed the frame:

```
BeginFrame(1)
  ClearRenderTarget
SetRenderTarget(rt -57 1116x122 mip 0)
SetViewport(0,0 1116x122)
  ClearRenderTarget
  DrawMesh(Now Draw List Mesh, 'NowUI/UI Rectangle', pass 0)
SetRenderTarget(backbuffer)
SetViewport(0,0 0x0)              <-- here
...
  DrawMesh((unnamed), 'NowUI/UI Rectangle', pass 0)   x12, all into a zero-sized viewport
EndFrame()
```

The render target itself is flawless: created, bound, cleared, drawn into, unbound. What is wrong is the
**viewport restored with the back buffer** — `0×0`, after which every remaining draw in the frame rasterises
nothing.

**Cause.** `NowImmediate.activeTarget` (`Standalone/NowUI.Engine/Graphics/NowImmediate.cs:41`) starts as
`default(NowRenderTarget)`: texture null, **width and height zero**. This host never populates it, because
`nowui-gl.js` sets the viewport itself inside `beginFrame` and no shim call ever binds the back buffer. The first
code that binds a render texture captures that default as "the target to restore" — `Graphics.ExecuteCommandBuffer`
does exactly this at `Graphics.cs:41` — and `NowImmediate.Bind` (`:105`) then restores it faithfully:

```csharp
Rect viewport = new Rect(0f, 0f, target.width, target.height);   // 0 x 0
backend.SetRenderTarget(in target);
backend.SetViewport(in viewport);
```

**Done, in the host.** `Program.Frame` now assigns `RenderTexture.active = null` immediately after
`NowRuntime.BeginFrame()`. That routes through `NowImmediate.SetActive` → `ResolveTarget(null)` → `BackBuffer()`,
which reads `INowHostServices.screen` and therefore carries the real canvas size — so the target later restored is
the right one. Cost: one `SetRenderTarget` and one `SetViewport` per frame. `area-rendertexture.png` then draws.

**The latent shim defect is reported, not fixed.** `NowImmediate.Bind` will happily bind a back buffer with no
size. A back buffer with no size is not a thing that exists, and clamping it to `NowRuntime.host.screen` there
would make the whole class of bug impossible for every host, not just this one. That is a change under
`Standalone/NowUI.Engine`, and this host could express the same guarantee without one — so it was not made. It is
worth making.

**The general lesson**, and it is the same one `M2-Scouting.md` keeps recording: a render backend that throws is a
gift. Both failures in this slice that cost real time — this one and the glass one — produced *no diagnostic at
all*, and both needed an instrument built before they could be seen. `?trace=N` is that instrument and is worth
keeping.

---

## 6. Masks, input and the control library

### 6.1 Masks — every row driven by the same content, so the mask is the only variable

Evidence: `area-masks.png`. All twelve cells clip identical diagonal stripes.

| Feature | Expected | Actual | Evidence |
|---|---|---|---|
| `Now.Mask(NowRect)` hard clip | works | **WORKS** | Square-cornered, no AA, by design. |
| Analytic rectangle mask | works | **WORKS** | |
| Rounded-rect mask, uniform and per-corner | works | **WORKS** | radius 26, and 44/4/44/4 — the asymmetric cell rounds its **top** corners, which is the correct `(TR,BR,TL,BL)` reading in UI-space y-down. A 180° y flip here would be invisible on a symmetric shape and was the specific trap `M2-ShaderPort.md` §5.2 warned about. |
| Circle / ellipse mask | works | **WORKS** | |
| Capsule mask | works | **WORKS** | |
| Feather (screen pixels) | works | **WORKS** | 0 / 2 / 6 / 14 px produce four monotonically softer edges, and feather 0 still has 1 px of AA. |
| Nested masks (intersection) | works | **WORKS** | Two circles intersect to a lens. |
| Texture masks (`Alpha` / `Red`) | works | **WORKS** | Alpha-channel cell fades the stripes across a gradient. |
| Inverted texture mask | works | **NOT PORTED (untested)** | The area draws the non-inverted case only. |
| SDF masks | not ported | **NOT PORTED** | Needs the Sdf extension's programs, §7.2. |
| Mask + custom material opt-in | fails | **NOT PORTED (untested)** | Custom materials are refused by name; no area supplies one. |
| Pointer interaction following the mask interior | works | **WORKS** | Proven indirectly and convincingly: `drive-controls-E-checkbox.png` unchecks a checkbox inside a **scrolled and clipped** scroll view, and the hit test lands on the right row. |

### 6.2 Input — driven, with the page's own counters read back

Evidence: `drive-controls-*.png`, and `window.nowui.debugState()` before and after each gesture.

| Gesture | Expected | Actual | Counter |
|---|---|---|---|
| Button click | works | **WORKS** | `clicks=0` → `1` → `2`; `adds` and `tasks` follow. |
| Hover and press states | works | **WORKS** | `drive-controls-1-hover-add.png` / `-2-press-add.png` show the two distinct visuals. |
| **Secondary (right) button** | works | **WORKS, and correctly remapped** | `secondary=1`, `middle=0` after a DOM right-press. DOM's `button` is 0/1/2 = left/middle/right and NowUI's index is 0/1/2 = primary/secondary/middle, so the two swap on the way in. With the remap missing the counters would **cross over** rather than go quiet; they did not. |
| Middle button | works | **WORKS** | `middle=1` after a DOM middle-press. |
| Drag with threshold | works | **WORKS** | `dragstarts=1`, `dragging=1` mid-drag, `dragends=1`, `dragcancels=0`. |
| Slider drag | works | **WORKS** | 50 → 89.2276. |
| **Wheel, in canonical units** | works | **WORKS, exactly to contract** | One wheel-down reads `wheely = -1`; two wheel-ups take it to `+1`. `M2-InputContract.md` requires precisely "one notch of wheel-down must read -1". |
| Scroll view wheel | works | **WORKS** | `drive-controls-D-scrolled.png`: the list scrolls, the scrollbar thumb moves. |
| Typing | works | **WORKS** | `a=` → `a=typed over CDP`. |
| Backspace | works | **WORKS** | `typed over CDP` → `typed over CD`. |
| Tab focus navigation | works | **WORKS** | One Tab from the task field lands on the **Add button** (spaces then activate it), two Tabs land on the Notes field and typing fills `b`. That is the correct visual order — field, button, field — not a bug. |
| **Clipboard round trip** | works | **WORKS** | `navigator.clipboard.writeText('from the system clipboard')`, then Ctrl+A, Ctrl+V in the Notes field: `b=from the system clipboard`. Select-all and paste both through the browser's async clipboard API. |
| Checkbox in a clipped, scrolled region | works | **WORKS** | Done count 4/12 → 3/12. |
| Switch | works | **WORKS** | `showdone=1` → `0`. |
| IME composition | uncertain | **NOT PORTED (untestable here)** | Headless Chrome cannot produce a composition. Needs a person at a real IME. |
| Gamepad navigation | works | **NOT PORTED** | No Gamepad API bridge exists. |
| Pointer cancel mid-drag | — | **NOT PORTED (untested)** | The probe exists (`dragcancels`) and read 0; nothing dispatched a `pointercancel`. `M2-InputContract.md` predicts it will arrive as `dragEnded` rather than `dragCancelled`. Still a prediction. |

### 6.3 The control library

Evidence: `area-controls.png`, `area-fields.png`, `area-pickers.png`, and the drive shots.

| Control | Expected | Actual | Evidence |
|---|---|---|---|
| `Button` | works | **WORKS (driven)** | §6.2. |
| `Label`, `SelectableRow` | works | **WORKS** | Demo. |
| `Checkbox` | works | **WORKS (driven)** | Inside a clipped, scrolled region. |
| `Radio` | works | **WORKS (rendered)** | `area-fields.png`, three-option group with the middle selected. |
| `Switch` | works | **WORKS (driven)** | |
| `Slider` (float, int, stepped) | works | **WORKS (driven)** | Pointer capture and the density-scaled threshold. |
| `ProgressBar`, determinate and indeterminate | works | **WORKS (rendered)** | Caller-passed time, so a still capture is reproducible. |
| `Badge`, `Chip` | works | **WORKS (rendered)** | |
| `Foldout` | works | **WORKS (rendered)** | Open, with a nested checkbox inside. |
| `TabBar` | works | **WORKS (rendered)** | Four tabs, first selected. |
| `TabView` | works | **NOT PORTED (untested)** | No area. |
| `SplitView` / `Splitter` | works | **WORKS (driven)** | Through Docking: `drive-docking-4-splitter-after.png` moves the Hierarchy/Scene boundary from x≈292 to x≈422. Note the drag needs a **settled hover frame before the press** — the input bridge folds DOM events into one snapshot per frame, and a press dispatched in the same frame as the move that positions it can miss. A first attempt without that pause did nothing. |
| `ScrollView`, `Scrollbar` | works | **WORKS (driven)** | Wheel over a viewport with more content than fits. |
| `TreeView` | works | **WORKS (rendered)** | `area-fields.png`, two collapsed roots. Expansion not driven. |
| `Dropdown` (closed field) | works | **WORKS (rendered)** | |
| `Dropdown` popup | works | **WORKS (driven)** — measured 2026-09-08 | Opened, hovered and picked with real pointer events at `?dpr=1` and `?dpr=2`: `artifacts/local/popup-fix/gallery-dpr1-*`. In `?area=fields` the resolution goes 1920 x 1080 → 3840 x 2160, a click outside closes it with the value intact, and Escape cancels. **The hover highlight is real but faint** — the hovered row goes (23,26,40) → (28,31,46) on this theme, `Accent` at `hoverStateOpacity`, so "hovering highlights nothing" is an eyeballing artefact. Through JavaScript the same popup was **broken until 2026-09-08**; see §0.5. |
| `ComboBox` | works | **WORKS (driven)** — popup measured 2026-09-08 | `ui.combo`'s popup filters and commits: `artifacts/local/popup-fix/popups-dpr1-*` takes Amsterdam → Copenhagen. Same §0.5 defect and fix as the dropdown. |
| `MaskField`, `EnumDropdown`, `EnumFlags` | works | **WORKS (rendered)** | MaskField shows "Music, Voice". |
| `TextField` | works | **WORKS (driven)** | Typing, caret, focus, Tab, Backspace, paste. |
| `TextArea` | works | **WORKS (rendered)** | Multi-line, wrapped. |
| `FloatField`, `IntField`, vector and rect fields | works | **WORKS (rendered)** | `Vector3Field` with scrubbable labels; numeric fields with ranges. |
| Label scrubbing, spinners | works | **NOT PORTED (untested)** | No drive. |
| **`ColorPicker`** | **fails** | **WORKS (driven) — prediction refuted** | `drive-pickers-1-colorpicker-popup.png`: the popup opens with a full saturation/value square, hue strip, alpha strip with checkerboard, hex field, copy/paste buttons and four channel sliders. `drive-pickers-2-sv-dragged.png`: dragging inside the SV square takes the value from `#5C6BF2D9` to `#000A66D9`, and the sliders, the hex field and the closed field's swatch all follow. `NowUI/Color Picker` is ported. |
| **`GradientField`** | partial | **WORKS (driven)** | `drive-pickers-3-gradient-popup.png`: closed field shows a texture-backed ramp with an alpha checkerboard; the popup carries alpha stops, colour stops, a Location field and an embedded colour picker. |
| **`AnimationCurveField` / `CurveField`** | **fails** | **WORKS (driven) — prediction refuted** | `area-pickers.png` draws the four-key curve in the closed field; `drive-pickers-4-curve-popup.png` opens the editor with grid, keyframes, tangent handles, Time/Value fields and Smooth/Linear/Step/Flat buttons. Draws through `Now.Bezier`. |
| `DatePicker`, `TimePicker` | works | **WORKS (driven)** — popups measured 2026-09-08 | `artifacts/local/popup-fix/popups-dpr1-open-date.png` opens the September 2026 calendar and `-open-time.png` the clock face; clicking a day and an hour commits both (2026-09-21 → 2026-09-24, 07:30 → 10:30). The calendar was subject to §0.5; the clock was **not** — it commits from persistent state each pass rather than from a one-shot, which is the control that shows the discriminator is real rather than a guess. |
| File and directory fields | partial | **PARTIAL** | Unchanged from the prediction: the control draws, but it browses wasm's in-memory filesystem. A browser file picker is a different feature. Not exercised. |
| `Inspector` | works | **NOT PORTED (untested)** | No area. |
| `ContextMenu`, `Tooltip`, `Dialogs`, `ViewStack`, `Overlay` | works | **NOT PORTED (untested)** | No area. The overlay machinery underneath them is proven by the three picker popups. |
| `Lottie` | works | **NOT PORTED (untested)** | No `.json` animation exported. |
| Remote Lottie / remote images | **not ported** | **NOT PORTED** | §8.1. |
| `NowClipboard` | works | **WORKS (driven)** | §6.2. |
| `LayerMaskField`, `KeyBindingField` | Unity-only | **UNITY ONLY** | §9. |

---

## 7. Layout, themes, identity, and the extensions

### 7.1 Layout and themes

Evidence: `area-layout.png`, `area-theme.png`.

| Feature | Expected | Actual | Evidence |
|---|---|---|---|
| Explicit rect slicing | works | **WORKS** | |
| `NowLayout.Row` / `Column`, gaps, padding | works | **WORKS** | Every reserved rect is outlined, so a layout error is visible rather than merely wrong. |
| Cross-axis alignment (Start/Center/End) | works | **WORKS** | Three rows, three alignments, visibly different. |
| Main-axis stretch and weights | works | **WORKS** | Fixed/stretch/fixed, and two stretches sharing 1:1 — **on frame one**. The hazard the inventory named (plain `Column` resolving stretch from the previous frame, so a one-shot capture photographs a zero-width frame) is real and `RunMeasured` is the fix; the area uses it and the capture is correct. |
| `Spacer`, `Space`, nesting, `ReserveRect` | works | **WORKS** | |
| Scroll view content measurement | works | **WORKS** | Bar appears per axis from the group's measured extent. |
| Themes: all 27 `NowColorToken`s | works | **WORKS** | Light and dark palettes side by side, each swatch with its hex; alpha tokens (Scrim, FocusRing) show over a checkerboard. |
| Spacing / radius / shadow / preset sets | works | **NOT PORTED (untested)** | Data, not rendering; no area shows them. |
| Theme generation, light/dark counterparts | works | **WORKS** | `ResetToDefaults(bool)` mints both built-in palettes in one frame. |
| Control renderer hooks | works | **NOT PORTED (untested)** | |
| Identity (`NowId`, keyed items) | works | **WORKS** | The demo's list keys rows by task id; adding tasks above a checked row does not move its state. |
| Editor dark preset and comparison harness | Unity-only | **UNITY ONLY** | |

### 7.2 The seven extensions

**All seven are referenced by `NowUI.Web.csproj`.** The inventory's "not wired" is superseded. As of
2026-09-08 all seven also **draw**; the one that still does not take input is Markdown.Markup's embedded
controls.

| Extension | Expected | Actual | Evidence |
|---|---|---|---|
| **Markdown** | works, minus remote images | **PARTIAL 2026-09-07 → WORKS 2026-09-08** | `area-markdown.png`: headings, bold, italic, bold-italic, strikethrough, inline code, nested unordered lists, ordered lists, task checkboxes (checked and unchecked), a syntax-highlighted code fence, a horizontal rule, and links. Driven: hovering the link reports `hovered link: https://github.com/BlenMiner/NowUI` and clicking it reports `clicked link: ... (1)`. Text selection drags work. An **injected** texture renders. **Remote images did not**, and now do *(verified 2026-09-08)*: `area-markdown.png` shows the remote row at `Remote state: Loaded` with the fetched image drawn beside the injected one. That upgrades this extension from PARTIAL to **WORKS**; the row keeps its old text because the reason it was partial is the thing §8.1 fixed. §8.1. |
| **Markup** | works | **WORKS (driven)** | `drive-markup-1-driven.png`: dragging the Volume slider takes `volume` 0.65 → 0.25; the Night mode switch takes `night` True → False and fires an event (`last = change night`); "Toggle details" takes `details` True → False, and the **visibility expression** removes the name field and the Save/Reset row from the layout. State binding, events and expressions all live. `NowMarkup.File(path)` is unused — it needs a filesystem this host does not have. |
| **Markdown.Markup** | works | **PARTIAL — and sharper than "does not commit"** | `drive-mdmarkup-1-checkbox-and-slider.png`: with a `NowMarkupEmbeds` set the fence renders as live controls (slider, checkbox, button) and the document reflows around them correctly; without one it degrades to a highlighted code block, as designed. But the embedded controls **receive no pointer input at all**. Clicking the checkbox leaves it checked; dragging the slider does not move it and instead **selects text in the markdown paragraph below** — visible as a highlight in that screenshot. So the hit test never reaches the embed; the document's own text-selection layer takes the pointer. The same markup outside a document (the `markup` area, above) takes every gesture, which isolates the fault to the embedding, not the controls. |
| **CodeEditor** | works | **WORKS (driven)** | `drive-codeeditor-1-typed.png`: click-to-focus, End, typing, Enter with auto-indent, and live re-highlighting (`int` as a keyword, `7` as a number, the moment they are typed). `-2-undo.png`: two Ctrl+Z restore the buffer. `-3-json-fixed.png`: deleting the planted trailing comma clears the validator's "Line 13: Trailing commas are not valid JSON" and the current-line highlight tracks the caret. |
| **Docking** | works | **WORKS (driven)** | Four panels in a seeded split tree. `drive-docking-4-splitter-after.png`: the splitter drags. `-6-tab-close.png`: closing the Console tab removes the pane and the Scene expands into it. `-2-slider.png`: the switch and slider **inside** a docked pane take pointer input (Exposure 0.60 → 1.64). |
| **NodeGraph** | **partial** ("stops the frame on its first wire") | **WORKS (driven) — prediction refuted** | `area-nodegraph.png`: four nodes, three bezier links, ports, grid, and the evaluator's answer (`Evaluate(graph,"output") = 11 — constant(6) x1.5 +2`). `drive-nodegraph-1-dragged.png`: dragging the Add node moves it, selects it, and **both links re-route to follow**. `area-nodegraph-custom.png`: a subclass overriding one virtual, `DrawLink`, produces a visibly heavier link — a 5237-pixel difference confined to the link paths (diff bbox 108,12–819,270), so the subclassing hook demonstrably takes effect. |
| **Sdf** | **fails** | **WORKS — prediction refuted, with two named limits** *(verified 2026-09-08)* | Both programs are ported; `IsPortedShader` lists ten. Three captures, each in its own fresh profile, no console errors on any: `area-sdf.png` (nine primitives, six operators, morph, six effects), `area-sdf-image.png` (the GPU-baked image field), `area-sdf-compose.png` (composition depth, rotation, warp, sprites, scene-as-mask), plus `area-sdf-sdftext.png` for glyph nodes. The open question the older row flagged is answered by measurement rather than left open: `SystemInfo.SupportsRenderTextureFormat` is now backed by an `EXT_color_buffer_float` / `EXT_color_buffer_half_float` probe, and this device granted **RHalf** for the field and **ARGBFloat** for the flood. The two limits are `SetMaterial(custom)` (§0.2, §7.3) and the fact that all of this was measured on SwiftShader only. Detail and evidence: **§7.3**. |

### 7.3 The SDF shape system, row by row against what the README advertises

Measured 2026-09-08, in fresh profiles, with the retry rule. The specification here is `README.md` — not the
extension's own API surface — because the README is what the showcase promises, and it is the document a reader
will hold this against.

Four pages carry the evidence. `?area=sdf` and `?area=sdf-image` were written by the two porting units.
`?area=sdf-compose` was added by this verification pass, for a reason worth stating: between them the first two
pages draw six boolean operators over **exactly two operands**, never rotate a node, say themselves that the warp
is off, never call `Sprite`, and never call `BeginMask`. Five README claims with no capture behind them.

| README claim | Actual | Evidence |
|---|---|---|
| "composable SDF circles, boxes, rounded and chamfered boxes, triangles, ellipses, capsules, round-capped lines, arcs, and pies" | **WORKS** | `area-sdf.png`, nine cells. The arc leaves its gap and the pie its wedge, which is what separates them from a ring and a disc. |
| "union/subtract/intersect operations, smooth blends" | **WORKS** | `area-sdf.png`, six cells over one shared pair. Smooth variants show the fillet and the colour blending *across* it. |
| "composable" — **past two operands** | **WORKS** | `area-sdf-compose.png`, top row: the same tree truncated at 1, 2, 3 and 4 operands. Each cell is the previous one plus one operator, the `Subtract` removes area and the `Union` adds the capsule. A fifth cell puts outline and glow on the four-operand result: both trace the composed silhouette, fillet and notch included, so **the effects read the composed field and not the last primitive**. |
| "explicit next-operand and scoped rotation for primitives" | **WORKS** | `area-sdf-compose.png`, middle row. `RotateNext(30)` turns one node and leaves the box beside it axis-aligned — scope respected, and the unrotated control cell shows what "did nothing" would look like. `PushRotation(25)` turns both nodes of a cross rigidly. |
| "scene-level outlines, shadows, glow, embossing, contours" | **WORKS** | `area-sdf.png`, six cells; `area-sdf-image.png` repeats outline, glow, shadow and emboss over a *baked* field. The contour cell is the strongest single piece of evidence on any of these pages: on the star the level sets round off the concave notches and the punched hole grows its own ring, which is what an offset curve of a real distance field does and what no raster trick produces. |
| "and warp" | **WORKS, and reads the clock** | `area-sdf-compose.png`. Driven rather than photographed: with `?animate=1`, sampling the warped silhouette's row extents at 0.9 s intervals gives three different shapes (one scanline present in the first sample and absent in the next two), so `_Time.y` is arriving and not pinned at zero. See §0.3 for the parameter that made the first capture of this cell look broken. |
| "real distance-field morphs — not crossfades" | **WORKS, and this was driven, not inferred** | A still image cannot separate the two, so the morph cell was sampled twelve times over a live clock and its silhouette measured directly out of the framebuffer. Bounding box runs continuously from **128×18** (the bar, mean colour `[93,218,156]`) to **64×64** (the circle, `[246,113,61]`) through 117×26, 99×34, 82×40, 74×48, 68×56. A crossfade holds the **union** box — 128×64 — constant at every t and varies only opacity. Covered pixel count also drops *below both endpoints* mid-morph (2118 against 2288 and 3254), which no blend of two rasters does. Strip: `sdf-morph-sequence.png`. |
| "texture fills"; images and sprites | **WORKS** | `area-sdf-image.png`: source art, silhouette, `UseTexture`, contours, threshold 0.85, and the five bake passes tallied on the console — seed/flood/resolve/stamp/dilate = 5/45/5/18/5. `area-sdf-compose.png` adds a `Sprite` node, which goes through the same `AddImage` path and renders the star identically. |
| "text ... for primitives" (glyph nodes) | **WORKS** | `area-sdf-sdftext.png`, behind `?sdftext=1`: SDF text with an outline, sampling the font atlas through `_MainTex`. Kept behind a flag because it is the one path here whose field is a texture. |
| "the scene used as a mask" (README's metamorphosis gloss and shading) | **WORKS** | `area-sdf-compose.png`, bottom row, with both controls it needs: the content unmasked, the mask shape drawn normally, and the content inside `BeginMask()`. The gradient's ramp survives the clip — which is what makes it "ordinary content was masked" rather than "an SDF was drawn". |
| "two custom final-shading materials" (the x-ray lens; the aurora / topographic / paper-cutout gallery) | **CANNOT** | Structural, not missing work. `SetMaterial` takes a user-authored `Material`, whose `Shader` would have to be compiled; this host has no shader compiler and `DrawMesh` throws on any program outside the ten ported ones. Deliberately **not** on `?area=sdf-compose`, because an unported program throws out of the frame flush and would erase the evidence for every other cell on the page. |

Two limits on all of the above, stated rather than left to be discovered:

- **SwiftShader only.** Every capture ran under `--use-angle=swiftshader`. The float/half render-target formats
  the image bake needs are granted by a real GPU too, but that has not been measured here.
- **`?area=sdf-compose` is a still capture apart from the warp and morph drives.** Nothing on it is interactive,
  so "works" there means "renders correctly", not "responds".

---

## 8. Host capabilities that are missing rather than broken

### 8.1 Networking and image decoding

Two of these three moved *(verified 2026-09-08)*. Evidence: `area-remote.png`, `area-remote-xorigin.png`,
`area-markdown.png`, plus resource timing read out of the page over CDP.

| Capability | Actual | Evidence |
|---|---|---|
| `INowFetchProvider` (`NowRuntime.host.fetch`) | **WORKS** — `WebFetchProvider`, one `fetch()` and one `ReadableStream` reader per request | Five probes, each a separate row on `?area=remote` with its own answer: a 22936-byte PNG and a 6961-byte JPEG both `OK 200` with status, headers and `Content-Length`; a 404 reported as a **transport success carrying a failure status**; and both halves of the byte cap refusing. `?xorigin=1` adds the only probe that leaves the machine and it succeeds: `cross-origin=OK 200/200/26494/1` off `raw.githubusercontent.com`, decoded and drawn. |
| `INowImageDecoder` (`host.imageDecoder`) | **WORKS** — `WebImageDecoder`, both paths | The synchronous-`TryDecode`-versus-async-browser-codec problem is solved by pre-decoding during the fetch, and the page proves both paths separately rather than as one "an image appeared": the PNG is answered by the **managed** decoder (counter `managed=2`, no network needed) and the JPEG only by the **browser pre-decode cache** (`cached=1`) — and a JPEG is the honest test, because there is no managed JPEG decoder that could be answering instead. `EncodeToPNG` → `LoadImage` round-trips 576/576 pixels exactly, alpha included. |
| `INowTouchKeyboard` | **NOT PORTED** — `null`, so `TouchScreenKeyboard.isSupported` is false | Unchanged. Correct for a desktop browser, wrong for a phone. Needs the same hidden editable element IME composition needs. |

Everything that was blocked by the first two is unblocked: **remote markdown images load** (`area-markdown.png`,
`Remote state: Loaded`) and web fonts and remote Lottie now have a transport under them. `NowRemoteContent` is
the exception and it is blocked by something else — its whole downloader is inside `#if !NOWUI_STANDALONE`, so
the engine-free build has no fetcher at that layer regardless of the host.

#### The byte cap, and what the page could not see about itself

The abort rule is the reason the streaming contract exists — `INowFetchSink.OnData` returns a `bool` so an
over-large response can be stopped mid-flight — so it was measured twice, once from inside the page and once from
outside it. **The two answers differ, and the outside one is the true one.**

*From inside*, both halves refuse exactly as designed. Against a 1.3 MB PNG behind a 64 KB cap:

```
cap-stream    REFUSED at the cap   0 bytes in 1 chunk    cap tripped in OnData after 0 accepted bytes;
                                                         the reader was cancelled
cap-declared  REFUSED at the cap   0 bytes in 0 chunks   cap tripped on the declared Content-Length before a
                                                         byte was read; AbortError: BodyStreamBuffer was aborted
```

Not one byte reaches the sink on either path, and the second one refuses before the body is read at all. That is
the contract, and it holds.

*From outside*, `performance.getEntriesByType('resource')` reports `transferSize` **1297005 for both** requests —
the entire 1.3 MB crossed the loopback in each case, in about 10 ms, before script ever saw the response headers.
**Neither half saved a wire byte here.** The page had been claiming that `cap-declared` was the half that did, and
that claim is now removed from the page and recorded in §0.3 instead of being quietly deleted.

Two things about that measurement are worth keeping, because both nearly produced a wrong answer:

1. **The first attempt was confounded by the HTTP cache.** Both cap probes fetched the *same* URL, so the second
   was served from Chrome's memory cache and reported `transferSize` 0 — which looks exactly like a saved
   transfer. The probes now carry a distinct query string each (`?probe=cap-stream`, `?probe=cap-declared`), and
   only then do the two rows mean what they say.
2. **This is a limit of the transport under test, not a demonstrated defect.** Over loopback there is nothing
   left for an abort to save. Whether the cap saves bytes on a slow or metered link is **unmeasured**, and the
   page now says so. What is measured, and is what the sink contract actually promises, is that the refusal
   reaches the transport, stops every byte before the sink, and cancels the stream.

### 8.2 The backend surface, as it actually stands

The brief's gap list — "render textures, `SetRenderTarget` for an FBO, `Blit`, `DrawProcedural`, `CopyTexture` all
throw" — is **out of date**. All are implemented. `?rtcheck=1` runs `RenderTargetSelfTest`'s in-page contract suite,
and this slice ran it: **21 passed, 0 failed**.

```
PASS create_argb32                        PASS procedural_draw_runs
PASS release_reports_lost                 PASS procedural_instanced_runs
PASS recreate_after_release               PASS copy_texture
PASS bind_clear_restore                   PASS temporary_pool_round_trip
PASS blit_copy_leaves_destination_bound   PASS mip_chain_target
PASS blit_scale_offset                    PASS depth_bits_target
PASS blit_to_back_buffer                  PASS float_target_matches_caps
PASS blit_material_unported_throws        PASS array_target_refused
PASS blit_pass_out_of_range_throws        PASS random_write_refused
PASS zero_sized_target_refused            PASS null_arguments_throw
PASS caps_msaa_is_one
```

The three console errors that accompany that run are the suite's own negative cases firing, each refused by name: a
`pass 1` request against a single-pass program, a `TextureDimension 5` (array) target, and `enableRandomWrite`
("WebGL2 has no compute and no image load/store, so there is no UAV to bind it as").

Note what the suite does **not** cover, and what §5.3 therefore had to find the hard way: it binds and restores a
target within its own scope, so `bind_clear_restore` passes while the frame-level restore that NowEffects performs
still hands back a 0×0 viewport. A contract suite that passes is not a frame that draws.

What still throws, by name and with the name in the message:

| Call | Refuses |
|---|---|
| `DrawMesh` / `Blit` / `DrawProcedural` | Any shader outside the **ten** ported programs. The two Sdf programs joined the list on 2026-09-08; what is outside it now is only a shader NowUI does not ship — which is why `NowSdfBuilder.SetMaterial(custom)` cannot work (§0.2, §7.3). |
| `SetRenderTarget` | Cubemap faces; array depth slices > 0 (the single-pass-instanced stereo path, which no browser host reaches). |
| `UploadTexture2D` | Formats and mip configurations outside what the ported programs need. |

Ported programs, verbatim from the refusal message: `NowUI/UI Rectangle`, `NowUI/Text Renderer`,
`NowUI/UI Gradient`, `NowUI/UI Ripple`, `NowUI/UI Glass`, `Hidden/NowUI/GlassBlur`, `NowUI/Color Picker`,
`NowUI/UI Bezier`, and — since 2026-09-08 — `NowUI/SDF Scene` and `Hidden/NowUI/SDF Image Field`. Ten.

That message was itself wrong until this slice: it was hand-written and still named only the first four, so
`?area=sdf&sdfprobe=1` reported that Glass, Glass Blur, Color Picker and Bezier were unavailable when all four were
ported. It is now generated from the same constants `IsPortedShader` tests. A small thing, but it is the message a
future reader will trust over any document, and the whole point of this exercise is that a stale claim about what
works is worse than no claim.

### 8.3 Incidental

- Every page load produces one `404` — `/favicon.ico`. Cosmetic; named so nobody chases it.
- The payload the gallery loads is ~22 MB **untrimmed** in the `dotnet run` development server, which is not the
  published figure. `M2-Scouting.md` §2's 943 KB brotli is a `PublishTrimmed` measurement and still stands as the
  number that matters; this document did not re-measure it.

---

## 9. Inherently Unity-only

Unchanged from the inventory, and none of it was contradicted. `NowUI.Runtime.csproj`'s exclude list is the
authoritative version.

| Feature | Reason |
|---|---|
| `NowGraphic`, `NowLayoutGraphic` (UGUI hosts) | Render into a `CanvasRenderer` inside a Unity `Canvas`. No Canvas, no `RectTransform`, no UGUI. |
| `NowWorldGraphic`, `NowWorldLayoutGraphic`, `NowWorldGlassBackdrop` | Put NowUI on a world-space `MeshRenderer` and ray-map a `Camera` onto it. No scene, no camera. |
| `NowVisualElement` / UI Toolkit host | Wraps NowUI as a `VisualElement` inside Unity's own retained UI. |
| `NowUGUINavigationProxy` | Bridges Unity's `EventSystem` selection into NowUI focus. No `EventSystem`. |
| `NowPipelineGraphic`, URP and HDRP integration | `ScriptableRenderContext`, render passes and custom passes are SRP types. WebGL2 here is a raw context. |
| `NowModelPreview` | Renders a `GameObject` hierarchy with a `Camera` into a `RenderTexture`. |
| `NowGUI` / `NowEditorGUI` / `NowEditorGUILayout` | IMGUI. `NowGUI.cs` *is* in the standalone compile, but nothing can call it: there is no IMGUI event pump. |
| `CommandBuffer` **targets** | The draw-list container underneath is portable and is used — §5.2's glass blur runs through one. The Unity-specific *targets* are not. |
| Editor font compilation (`Assets > NowUI > Compile Font`) | An editor menu item over `AssetDatabase`. The **runtime** compiler is portable and is what the browser uses. |
| `KeyBindingField`, `NowKeyInput`, `NowKeyNames` | Public API is `UnityEngine.InputSystem.Key`. |
| `LayerMaskField` | Edits against `ProjectSettings`' named layers. There is no project. |
| Native plugins: `nowui-msdf`, HarfBuzz, the Lottie native packer, the Burst rasteriser and tessellator | `DllImport`. `NOWUI_VG_DISABLE_NATIVE` compiles every one out; the managed fallbacks run. Unity-only **in this build** rather than in principle — a wasm build of these is conceivable. |
| XR / stereo single-pass glass | Stereo eye indices and instancing macros. |
| Mobile safe areas, on-screen keyboard | `Screen.safeArea` and `TouchScreenKeyboard`. A browser has its own equivalents; those are a different feature, not this one ported. |

---

## 10. What a next slice should do, in order

Items 1, 3 and 6 were done on 2026-09-08 and are struck through rather than deleted, so the list still reads as a
record of what was decided and when.

1. ~~**Port the text gradient branch** (`NowUITextGradient.cginc`).~~ **DONE 2026-09-08.** §3.1. It leaves one
   smaller item behind it: `?area=textfx` exercises `Clamp` spread, one repetition and the ellipse radial only, so
   `Repeat`/`Mirror` spread, repetitions, the circle radial and `GradientMode.Fixed` banding are ported but
   unmeasured.
2. **Fix the glass backdrop upstream** (`Now.cs:2192`). The blur already runs and is already paid for; one line
   stands between it and a working frosted pane. This is a Unity-side change, not a browser one.
3. ~~**Wire `INowFetchProvider`**, then decide what to do about the synchronous `INowImageDecoder`.~~ **DONE
   2026-09-08.** The synchronous-decoder problem was solved by pre-decoding during the fetch. §8.1.
4. **Fix the export contract for UGUI materials** so §5.1's host alias can be deleted.
5. **Make `NowImmediate.Bind` refuse a zero-sized back buffer**, so §5.3's class of bug cannot recur for any host.
6. ~~**Port the two Sdf programs.**~~ **DONE 2026-09-08.** §7.2, §7.3.
7. **Make the embedded controls in Markdown.Markup take input.** With Sdf and remote loading resolved this is the
   only "renders but does not respond" row left in §7.2, and it is isolated: the same markup outside a document
   takes every gesture.
8. **Add areas for what is honestly untested**: rich text, `TabView`, dialogs, context menus, tooltips, Lottie,
   and the dropdown popup. (Sprites came off this list on 2026-09-08 — §7.3.) Each of those rows says
   "untested", and each is one area away from saying something true instead.
9. **Re-run the SDF pages on a real GPU.** Everything in §7.3 was measured under SwiftShader, including the
   float/half render-target formats the image bake depends on.
10. **Measure the byte cap on a link slow enough for the abort to matter.** §8.1 proves the refusal reaches the
    transport and proves nothing about wire bytes, because over loopback there is nothing left to save.
