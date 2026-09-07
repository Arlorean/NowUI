# Standalone Test Plan (M1)

Scope: which files under `Assets/NowUITests` the `Standalone/Tests` project (AssemblyName `Tests`, NUnit, `dotnet test`, null render backend) compiles and runs in M1, what each file needs from the shim (`NowUI.Engine`), the null backend and the host clock, how Unity's NUnit usage maps to the NUnit NuGet, and the risks. Every candidate file listed in inventory §G.4 was read in full (40 test files + `Simple.cs` + `Support/NowInputReplay.cs` + `Support/NowPopupTestDriver.cs`, plus `BenchmarkSupport/NowBenchmarkAllocations.cs` which four of them depend on). Counts below are executed test *cases* (each `[TestCase]` row counts once), not attributes.

Companion documents: `UnityDependencyInventory.md` (shim inventory A, dispositions B, seams C, hazards D, open questions H) and `UnityValueTypeSemantics.md`.

---

## 0. Verdict at a glance

| Tier | Files | Cases | What it needs |
|---|---|---|---|
| A. Ready as-is (pure logic; null backend only incidental) | 20 | 342 | Shim value types, `ScriptableObject.CreateInstance`, `Time`, `Resources` provider returning the NotoSans fixture (only because `Now.defaultFont`/`NowLayout.labelStyle` getters load it) |
| B. Ready with the font fixture + capture-mode null backend | 16 | 441 | Everything in A plus: `NowUI/NotoSans` font family export + loader, the 9 non-UGUI material templates as shim `Material`s, shim `Mesh` channel storage with `GetUVs`/`GetNormals` read-back, shim `Texture2D` pixel storage, Tests-side `LogAssert` and `Unity.PerformanceTesting`/`Recorder` stubs (for `NowBenchmarkAllocations`) |
| C. Optional in M1 (benchmark, CPU only) | 1 | 18 | Tier B stubs; excluded from the gate by category filter |
| D. Deferred (host-only fixtures) | 4 | 50 | IMGUI `Event`/`GUI`/`NowGUI`/`NowIMGUIInputProvider` (2 files), Input System `Key`/`NowKeyInput`/`NowKeyBindingField` (1), `MonoBehaviour`/`GameObject`/`NowOverlay.Host(Component)` (1) |

**Recommended M1 gate: Tiers A + B = 36 test files, 783 cases, of which 9 are expected `Assert.Ignore` skips (6 in `NowTextShapingTests`, 3 in `NowTextStylingTests`: HarfBuzz shaping is compiled out per D6).** Tier C adds 18 cases when the `Unity.PerformanceTesting` stub is in place (run with `--filter "Category!=NowUI.Overview"` for the gate, without the filter for a CPU sanity run). The 50 deferred cases stay Unity-only in M1.

The single biggest prerequisite is not the null backend but **the bundled font**: 31 of the 36 gate files reach `Now.defaultFont` (directly, through `NowTheme.themeAsset.Text(...)`, `NowLayout.labelStyle`, or by drawing any labelled control), which calls `Now.LoadRequiredResource<NowFontAsset>("NowUI/NotoSans")` and logs `Debug.LogError` once when the provider returns null. With Unity-parity log handling (an unexpected error log fails the test) that error fails the first test that touches text. M1 therefore ships an exported NotoSans family as test data (§3.2).

---

## 1. Method and conventions

- "Draws through scopes" means the test enters `NowInput.Begin(provider, size)` and/or `NowDrawList.Begin(size)` and calls builder `Draw()` methods. No candidate file uses `Now.StartUI` (the screen path, H.2) — every draw goes through the **capture** path (`NowDrawList` → `NowMesh.UploadMesh` → shim `Mesh` setters). The null backend therefore never has to accept `Graphics.DrawMeshNow`, `GL.*`, `RenderTexture.GetTemporary` or `CommandBuffer` for this subset; it must accept `Mesh`/`Material`/`Texture2D` object creation and data uploads without a device.
- "Frame helpers" used by tests, all existing today: `NowFocus.ForceNewFrame()` (internal), `NowOverlay.ForceNewFrame()` (internal), `NowPointerArbiter.ForceNewFrame()` (public), `NowOverlay.Flush()` (internal), `NowOverlay.EndFrameTransaction(bool)` (internal), `NowTextInput.Invalidate()`, `NowKeyInput.Invalidate()`, and the per-subsystem `Reset()` statics called from `[SetUp]`/`[TearDown]`. All of them exist because EditMode `Time.frameCount` is static for the duration of a synchronous test; the standalone `Time` must reproduce that (§4).
- Fully-qualified API names are given once per file below; `NUnit` classic asserts (`Assert.AreEqual/IsTrue/Throws/...`) are not itemised unless they matter for the NUnit mapping (§5).

---

## 2. Per-file reports

### 2.1 Summary table

Legend for the "Unity API" column: **VT** = shim value types only (Vector2/3/4, Color, Rect, RectInt, Vector2Int, Quaternion, Bounds, Mathf); **SO** = `ScriptableObject.CreateInstance<T>` + `Object.DestroyImmediate`; **RES** = `Resources.Load<NowFontAsset>("NowUI/NotoSans")`; **T** = `Time.frameCount`/`realtimeSinceStartup`; **MESH** = reads `NowDrawList.mesh` (`GetUVs`, `GetNormals`, `vertexCount`, `subMeshCount`); **TEX** = creates `Texture2D`; **LOG** = `UnityEngine.TestTools.LogAssert`; **PERF** = `Unity.PerformanceTesting`; **ALLOC** = `NowBenchmarkAllocations` (BenchmarkSupport → PERF + `UnityEngine.Profiling.Recorder`); **DBG** = `Debug.Log`; **IMGUI** = `Event`, `EventType`, `KeyCode`, `GUI.changed`, `GUIUtility.hotControl`; **IS** = `UnityEngine.InputSystem.Key/Keyboard`; **GO** = `GameObject`/`MonoBehaviour`/`Component`.

| # | File | Cases | Unity API | B.4 host-only refs | Draws through scopes | Verdict |
|---|---|---|---|---|---|---|
| 1 | NowRectTests | 14 | VT (Vector2, Vector4, Rect ↔ NowRect casts) | – | no | **A** ready |
| 2 | NowResolvedIdTests | 15 | none (`using NowUI` only; reflection over NowUI types) | – | no | **A** ready |
| 3 | NowControlStateResolvedIdTests | 3 | none | – | no | **A** ready |
| 4 | NowFocusResolvedIdTests | 4 | VT (Vector2) | – | no | **A** ready |
| 5 | NowOverlayResolvedIdTests | 4 | VT | – | `NowInput.Begin` only (no draw list) | **A** ready |
| 6 | NowIdentityIntegrationTests | 6 | VT | – | `NowInput.Begin` only | **A** ready |
| 7 | NowTextWrapTests | 7 | RES, MESH (`GetUVs(6)`), VT | – | `NowDrawList.Begin` + `NowTextWrap.Draw` | **B** font fixture + Mesh read-back |
| 8 | NowTextAreaTests | 24 | RES, T (`Time.frameCount`), VT | – | `NowInput.Begin` + `NowDrawList.Begin` + `Now.TextArea` | **B** font fixture |
| 9 | NowTextEditTests | 15 | none (`NowTextInput.isMacPlatform` static) | – | no | **A** ready |
| 10 | NowTextSelectionTests | 11 | RES, VT | – | both scopes + `NowTextSelection.Draw` | **B** font fixture |
| 11 | NowTextPreprocessorTests | 10 | RES, LOG (`LogAssert.Expect(LogType.Exception, "InvalidOperationException: boom")`) | – | `NowDrawList.Begin` + text draw | **B** font fixture + LogAssert shim |
| 12 | NowMarkupTests | 13 | RES, LOG (`Expect(LogType.Warning, Regex)`, `NoUnexpectedReceived()`), ALLOC, `System.IO` temp file | – | both scopes + `NowMarkupDocument.Draw` | **B** font + LogAssert + PERF/Recorder stubs |
| 13 | NowCodeEditorTests | 43 | VT; reflection into `NowControlState.Store`1.entries`, `NowCodeEditor._caches`, `NowContextMenu._menus`; `NowTheme.themeAsset.Text(...)` → font | – | both scopes + `NowCode.Editor`, `Now.ScrollView` | **B** font fixture |
| 14 | NowNumericExpressionTests | 61 | none | – | no | **A** ready (see risk R9: `ExpressionEvaluator` fallback parity) |
| 15 | NowNodeGraphIndexedEvaluationTests | 13 | VT (`Vector2.zero`), ALLOC | – | no | **A** ready (+ PERF/Recorder stubs for ALLOC) |
| 16 | NowGraphEvaluationPerformanceTests | 18 | PERF (`[Performance]`, `Measure.Method(...).SampleGroup(...).WarmupCount(5).MeasurementCount(64).IterationsPerMeasurement(1).Run()`, `Measure.Custom`, `SampleGroup(name, SampleUnit, bool)`, `SampleUnit.Millisecond/Undefined`), ALLOC, `[Category("NowUI.Overview")]` | – | no | **C** with PERF stub; not in gate |
| 17 | NowDockingTests | 11 | VT | – | both scopes + `NowDock.Space(...).Draw()`, `NowOverlay.Flush` | **B** font (tab labels) |
| 18 | NowFilePickerTests | 38 | VT (`Vector2Int`), `System.IO` temp dir | – | no | **A** ready |
| 19 | NowFilePickerUserFoldersTests | 12 | `System.IO`, `Environment.SpecialFolder` | – | no | **A** ready |
| 20 | NowFontResolutionTests | 12 | SO (`CreateInstance<NowFont/NowFontFamily/TrackingFont>` where `TrackingFont : NowFont` is declared in the test assembly), `Object.DestroyImmediate` + fake-null after destroy, reflection on `NowFontAsset._fallbacks`, `NowFontFamily._regular/_bold/_italic/_boldItalic` | – | no | **A** ready (needs exact fake-null: `DestroyedRootReturnsDefaults...` calls methods on a destroyed asset and expects `TryResolveFont` false, metrics 1f) |
| 21 | NowFontStackTests | 8 | SO (`CreateInstance<NowFont>`), `Now.defaultFont` get/set (getter loads RES) | – | no | **A** ready (font fixture avoids the one-shot LogError) |
| 22 | NowMaskShapeTests | 13 | VT | – | `Now.Mask`/`Now.Transform` scopes only | **A** ready |
| 23 | NowCornerRadiusTests | 3 | VT (Vector4 equality) | – | no | **A** ready |
| 24 | NowViewStackTests | 12 | VT, `Thread.Sleep(60)` vs `NowTime.realtimeSinceStartup` | – | both scopes + `NowViewStack.Draw` | **B** font (view chrome) + live clock |
| 25 | NowInteractionRegionTests | 5 | VT (`(Rect)Row` cast equality) | – | `NowInput.Begin` only | **A** ready |
| 26 | NowInteractionRepaintTests | 18 | VT | – | no | **A** ready |
| 27 | NowIMGUIKeyInputRoutingTests | 11 | IMGUI, IS, `NowIMGUIInputProvider`, `NowKeyInput`; whole file `#if NOWUI_INPUT_SYSTEM` | NowIMGUIInputProvider, NowKeyInput, NowKeyBindingField | both scopes | **D** defer |
| 28 | NowIMGUITextInputRoutingTests | 21 | IMGUI (`Event.current`, `GUI.changed`, `GUIUtility.hotControl`, `EventType`, `KeyCode`, `Color.clear`), `NowGUI.AutoForEvent/DisposeAll/CacheEntry/GetEntry` (reflection), `NowIMGUIInputProvider` | NowGUI, NowIMGUIInputProvider (+ `NowKeyInput` under `#if NOWUI_INPUT_SYSTEM`) | both scopes | **D** defer |
| 29 | NowContextInputRoutingTests | 3 | VT (`Rect` in `NowInputSurface(size, screenRect)`) | – | both scopes, `NowOverlay.ForceNewFrame/DeferScreen/Flush` | **A** ready |
| 30 | NowKeyBindingFieldTests | 11 | IS (`Key.*`, `Keyboard.current`), SO (`NowThemeAsset`, `RecordingRenderer : NowControlRenderer`), T; whole file `#if NOWUI_INPUT_SYSTEM` | NowKeyInput, NowKeyBindingField | both scopes | **D** defer (compiles to nothing without the define) |
| 31 | NowNewControlsTests | 39 | SO (`NowThemeAsset`, `RecordingRenderer : NowControlRenderer`), VT (`Mathf.Repeat/Max`), reflection on `NowThemeAsset._controlRenderer`, `System.Globalization.CultureInfo.CurrentCulture` | – | both scopes; `Now.Switch/Chip/Badge/ProgressBar/TextField/FloatField/TabBar/SplitView/ComboBox/DatePicker/TimePicker`, `NowLayout.TreeView`, `NowTooltip.For`, `NowLayout.BeginMeasurePass/EndMeasurePass` | **B** font fixture |
| 32 | NowInspectorTests (4 fixtures: NowInspectorTests 14, NowFoldoutTests 1, NowMaskFieldTests 2, NowWideNumericFieldTests 7) | 24 | VT (`Quaternion.Euler`, `Bounds`, `Rect`, `RectInt`, `LayerMask` implicit int, `Color.red`, `Vector3.one`), attributes `[SerializeField] [HideInInspector] [Header] [Space] [UnityEngine.Range(0f,10f)] [TextArea(2,3)]` + `[NonSerialized]`, `LayerMask.LayerToName` (runtime, via `NowMaskField`) | – | both scopes; `NowLayout.Inspector`, `Now.Foldout/MaskField/TextField/VectorField/Vector2Field`, `NowOverlay.Flush/ForceNewFrame` | **B** font + attribute classes + layer-name table |
| 33 | NowDialogTests | 3 | VT | – | both scopes + `NowViewStack.Draw` of `NowViews.MessageBox/Confirm` | **B** font |
| 34 | NowFocusHostRegistryTests | 6 | VT | – | no | **A** ready |
| 35 | NowContextMenuOwnerLifetimeTests | 7 | GO (`new GameObject(name)`, `AddComponent<T>()`, `MonoBehaviour` subclass declared in the test assembly, `Component` implicit bool, `Object.DestroyImmediate(GameObject)`), `NowOverlay.Host(Component)`, `NowOverlay.ReleaseRegistrationOwner(object)`, `NowOverlay.EndFrameTransaction`, Support `NowInputReplay` | none by file, but `NowOverlay.Host(Component)` is the B.2 host-identity seam (H.4) | both scopes | **D** defer; runs unchanged only if the shim ships a minimal `GameObject/Component/MonoBehaviour` and NowOverlay keeps a `Component` overload |
| 36 | NowLayoutTests | 83 | SO (`NowThemeAsset`, `NowLottieAsset`), VT (`Color.red/blue/black`, Vector4 as rect), `NowLottieCache.SetAsset/Reset`, `Now.uiScale/SetUIScale`, `NowFrame.Begin/DrawContent/MeasureContent` (internal), `NowControls.AllocateOwnerScope/RestoreIdScope` (internal), `#pragma warning disable NOWUI002` | – | no draw list (`NowInput.Begin` in 2 tests); `NowLayout.labelStyle = new NowText(default, null)` pins a font-less style on purpose | **A** ready (the `DefaultLabelStyleUsesActiveThemeTextColor` test reads the theme's label style → font resolution → needs the fixture or a non-erroring provider) |
| 37 | NowNodeGraphTests | 117 | VT (`Mathf.Pow/RoundToInt/Max`, `Vector2.Scale`, `Color.*`), ALLOC, `Now.currentTransform`, `Now.TransformScreenRect` | – | both scopes; `NowNodes.Canvas(...).Draw()`, `NowOverlay.ForceNewFrame/Flush/Defer`, `Now.Transform`, `Now.Button/Rectangle` inside node content | **B** font (titles/port labels/search) + Bezier material + PERF/Recorder stubs |
| 38 | NowTextStylingTests | 32 | RES, MESH (`GetUVs(1,2,3,5,6)`, `GetNormals`, `vertexCount`, `subMeshCount`), `ColorSpace.Linear/Gamma`, `Mathf.GammaToLinearSpace/Abs/Min`, `Color(r,g,b,a)`, `Vector4`, ALLOC, `NowGradientRampCache.Reset` (→ `Texture2D(w,h,RGBA32,false,true)`, `SetPixels32`, `Apply`), `new NowDrawList(NowMeshLayout.Canvas, name)` | – | `NowDrawList.Begin` + `Now.Text/RichText`, `NowLayout.Label/RichText`, `NowTextWrap.Draw`, `Now.Transform` | **B** font + Mesh read-back + Texture2D storage; 3 cases `Assert.Ignore` without shaping |
| 39 | NowTextShapingTests | 8 | RES, DBG (`Debug.Log`), `Material` (out param), `Color.white`, `Now.textShaping` flag | – | `NowDrawList.Begin` + `Now.Text` | **B**; 6 cases `Assert.Ignore` (shaper unavailable), 2 run |
| 40 | NowControlsTests | 84 (+1 under `#if NOWUI_UGUI`, excluded) | SO (`NowThemeAsset`), T (`Time.realtimeSinceStartup` ×3), `Thread.Sleep(20)` ×5, VT, reflection on `NowControls.InteractionRepaintState` / `NowControlState.Store`1`, `NowFocus.ProcessImmediateTabNavigationPass/immediateRegistrationCount` (internal) | – | both scopes; `Now.Button/SelectableRow/Checkbox/Radio/Slider/ScrollView`, `NowLayout.Button/TreeView/Label`, `NowOverlay.DeferScreen` | **B** font + live clock |
| 41 | Simple | 9 | SO (`CreateInstance<NowFont>`), TEX (`new Texture2D(100, 200)`, `.width/.height` via `NormalizeGlyphAtlasBounds`), `Object.DestroyImmediate(Texture2D)`, `NowUI.Internal.StaticList`, reflection on `NowFontAsset._fallbacks` | – | no | **A** ready (needs the 2-arg `Texture2D` ctor) |
| S1 | Support/NowInputReplay | 0 | VT | – | provides `INowInputProvider` + `INowTextInputSource` | compile-only; include (used by #35 and by out-of-scope `NowPopupUXTests`) |
| S2 | Support/NowPopupTestDriver | 0 | VT | – | `NowOverlay.ForceNewFrame` (internal), `NowInput.Begin`, `NowDrawList` | compile-only; include (harmless, keeps the Unity file set identical) |
| S3 | BenchmarkSupport/NowBenchmarkAllocations | 0 | PERF (`Measure.Custom`, `SampleGroup`, `SampleUnit.Byte/Undefined`), `UnityEngine.Profiling.Recorder` (`Get`, `isValid`, `enabled`, `FilterToCurrentThread`, `sampleBlockCount`, `CollectFromAllThreads`), `[assembly: InternalsVisibleTo("Tests")]` | – | – | compile into `Tests` directly (drop the assembly attribute or keep it; harmless) |

Totals: Tier A = files 1–6, 9, 14, 15, 18–23, 25, 26, 29, 34, 36, 41 → 20 files, 342 cases. Tier B = files 7, 8, 10–13, 17, 24, 31–33, 37–40 → 16 files, 441 cases. Tier C = file 16 → 18 cases. Tier D = files 27, 28, 30, 35 → 50 cases.

### 2.2 File notes (what is not obvious from the table)

**NowRectTests.** Uses `Rect unity = rect; Assert.AreEqual(new Rect(1,2,3,4), unity)` → shim `Rect.Equals(object)` must be component-exact (Unity's is). `Assert.AreEqual(new Vector4(1,2,3,4), v)` → `Vector4.Equals(object)` exact.

**NowResolvedIdTests.** Golden hex vectors (`026883B751DD4027`, …) depend only on `NowIdHash.HashString` (NowUI-owned, deterministic; verified it does not use `string.GetHashCode`). 250k-id collision corpus is CPU-bound (~100 ms). Reflection asserts `[Obsolete(IsError=true)]` members on `NowControlState/NowFocus/NowControls/NowTooltip` and typed `SetId` overloads on `NowModifierBuilder<NowWaveDeformer>`, `NowSnapshotBuilder`, `NowVectorField`, `Now.Vector*Field(NowRect, NowResolvedId, string, int)` and `NowLayout.Vector*Field(NowResolvedId, string, int)` — any M1 refactor must keep those signatures byte-identical (they are public API anyway).

**NowFocusResolvedIdTests / NowFocusHostRegistryTests.** Call `NowFocus.EnterUGUINavigation/RouteUGUINavigation/BeginHostRegistration/Register/UnregisterHost/IsFocusedInHost`. These live in `NowFocus.cs` (B.2 core-with-guards); the UGUI names are just names — no `UnityEngine.UI` types in the signatures used here. `NowFocus.respectEventSystem` routes into `NowEventSystemFocusBridge`, which is already stubbed under `!NOWUI_UGUI`.

**NowOverlayResolvedIdTests / NowContextInputRoutingTests.** Use `NowOverlay.DeferScreen/Flush/ForceNewFrame/HasNestedOverlay/IsPointerInsideOverlayTree/BlockAllSurfaces/activeFocusLayerSourceId/currentFocusLayerId` and `NowInputSurface(size, Rect screenRect)`; `NowInput.currentProvider/current/surface`. All core; `NowOverlay.Flush` and `ForceNewFrame` are `internal` (InternalsVisibleTo).

**NowTextWrapTests.** `GeneratedWrapMaskIncludesLargeOutline` reads `_drawList.mesh.GetUVs(6, masks)` and expects `masks[0] == (Vector4)rect.Outset(104f)` — exact `Vector4` equality. `ResolveTextCarriesAmbientFontAndNoMask` expects `NowTheme.themeAsset.ResolveText().font == _font` (the family loaded from Resources) — the provider must return the **same instance** on repeated loads (`Now.defaultFont` is cached in a static, but `TearDown` sets it to null so it reloads each test).

**NowTextAreaTests.** `ImeEnablesOnFocusGainAndDisablesAfterAnIdleFrame` uses `NowTextInput.MaintainCapture(Time.frameCount + 1 / + 2)` — relative to whatever the shim reports; any constant works. `NowTextInput.setImeEnabled` delegate is set by the test. `CopyAndCutKeepNewlines` swaps `NowClipboard.setText/getText` delegates (the shim default delegate on `GUIUtility.systemCopyBuffer` is never invoked by tests, but the static initializer must not throw).

**NowTextEditTests.** `ModifierMappingFollowsThePlatformConvention` toggles `NowTextInput.isMacPlatform`, whose static initializer reads `Application.platform` → shim `Application.platform` + `RuntimePlatform` enum must exist and be OS-mapped (Windows→`WindowsPlayer`/`WindowsEditor`, macOS→`OSXPlayer`, Linux→`LinuxPlayer`); `NowFilePickerUserFolders.Platform(RuntimePlatform)` maps only those six values, so the shim must not invent a new enum member for desktop.

**NowTextPreprocessorTests.** `LogAssert.Expect(LogType.Exception, "InvalidOperationException: boom")` — Unity formats `Debug.LogException(e)` as `"{e.GetType().Name}: {e.Message}"`; the shim `Debug.LogException` must produce exactly that string for the LogAssert shim to match.

**NowMarkupTests.** `FileSourceReloadsChangedMarkup` writes a temp `.nowui` file, `NowMarkupFile` uses `FileSystemWatcher` + `File.ReadAllText` (fine on .NET 9 desktop; H.12 browser IO is not exercised). `UnknownResultQueriesWarnOnce` expects `Debug.LogWarning` containing `queried click "sve"` once, then `LogAssert.NoUnexpectedReceived()`. `CachedDocumentDrawIsAllocationFreeAfterWarmup` (ALLOC, see risk R6).

**NowCodeEditorTests.** `EditorTextPoint` measures with `NowTheme.themeAsset.Text(default, NowTextStyle.Body)` at 14 px — real advance widths from the fixture (self-consistent with the editor's own measurement, so any deterministic font works, but a missing glyph table would collapse every x to the gutter). Reflection: `NowCodeEditor.EditorState` (nested, non-public), `NowCodeEditor._caches`, `NowContextMenu._menus`, entry field names `label`, `shortcut`, `enabled`, `deliveryId` — do not rename in M1. `NowCodeEditor.ResetCaches/cacheCapacity/ReleaseCache` must be public (the CodeEditor asmdef grants no `InternalsVisibleTo("Tests")`, and it compiles in Unity today).

**NowNumericExpressionTests.** Pure, but the runtime's `TryEvaluate` first calls `UnityEngine.ExpressionEvaluator.Evaluate` and only on `false`/exception uses the bounded `DoubleParser` (B.2 row: "`ExpressionEvaluator.Evaluate` block (38-60)"). The shim `ExpressionEvaluator.Evaluate` should return `false` (documented as the "engine versions differ" path), which means all 61 golden results come from the fallback parser in standalone while some come from Unity's evaluator in the editor. Verify parity on first run (risk R9).

**NowNodeGraphIndexedEvaluationTests.** `FailedIndexBuildAndHandlerExceptionDoNotPoisonFollowingScopes` expects `NullReferenceException` from `BeginIndexedBatch` when `graph.links == null` — .NET 9 throws the same. ALLOC test (R6).

**NowGraphEvaluationPerformanceTests.** CPU only; 512-node chains with `maximumDepth` raised. With a stub `Measure.Method(...).Run()` that executes warmup+measurement iterations (5 + 64) it runs in well under a second per case. It reports `Counter(...)` via `Measure.Custom` — stub discards. Keep `[Category("NowUI.Overview")]` so the gate can exclude it.

**NowDockingTests.** Hard-coded pointer positions (tab at x=100, splitter at `sceneRect.xMax + 8`) depend on theme control styles, not on font metrics, except tab widths (`dock-tab` hit rects come from measured labels "Scene"/"Inspector"/"Console"). The fixture's NotoSans advances reproduce Unity's layout exactly because the glyph table is exported verbatim.

**NowFilePickerTests / NowFilePickerUserFoldersTests.** Only `NowFilePickerUtility` pure members and `NowFilePickerUserFolders.ResolveCandidates/BuildCandidates/PathComparer/CanonicalPath/IndexOfPath/PathsEqual/XdgUserDirsPath/VideosId` — all listed as "core keeps" in the B.3 `NowFilePicker.cs` split. `BuildSavePath/BuildOpenPath` use `Path.GetFullPath`/`File.Exists` — the split row moves these behind `INowFileSystem`; for M1 the default desktop `INowFileSystem` must be the real `System.IO` so these 6 cases keep passing. Paths are `Path.GetTempPath()`-relative; no absolute or `Assets/`-relative paths anywhere in the subset.

**NowFontResolutionTests.** Relies on exact `UnityEngine.Object` fake-null after `DestroyImmediate`: `DestroyedOwnFontAndFallbackAreSkipped` expects a destroyed `NowFont` in `_fallbacks` to be skipped (`asset != null` false) yet its `requests` list (a plain C# field) still readable; `DestroyedRootReturnsDefaultsWithoutVirtualSelection` calls `TryResolveFont` on the destroyed root and expects `false`, `GetLineHeight == 1f`, and that the virtual `TryGetOwnFont` is *not* invoked. `CreateInstance<TrackingFont>()` instantiates a test-assembly subclass — the shim's `CreateInstance<T>` must be `Activator`-based (no registry).

**NowFontStackTests.** `Now.Font(null)` must throw `ArgumentNullException`; `Now.font` compares by reference (`Assert.AreSame`).

**NowMaskShapeTests.** `Now.CaptureMaskShaderState()` identity/`count`/`Equals` — computed on the CPU (`NowMaskShader` snapshot); no `Material`/`SetVectorArray` call happens in these tests, but `NowMaskShader` has `Shader.PropertyToID` static initializers (hazard D.1 #4) → `Shader.PropertyToID` must work with no backend (inventory already requires this).

**NowViewStackTests.** `ExitTransitionKeepsViewUntilCompletion` pushes with a 0.03 s fade, sleeps 60 ms, expects the entry gone → `NowTime.realtimeSinceStartup` (`Time.realtimeSinceStartupAsDouble`) must be a **live** monotonic clock read at call time, not latched per frame (D5 says monotonic; this pins down "not frame-latched"). `PopupPushedByPressIgnoresThatInputPass...` sets `snapshot.frame/inputPass` explicitly.

**NowInteractionRegionTests.** `Assert.AreEqual((Rect)Row, interaction.rect)` — `NowInteraction.rect` is a `UnityEngine.Rect` (public API) → shim `Rect.Equals` exact.

**NowNewControlsTests.** Every hit-test position is derived from `NowTheme.themeAsset.controlStyles` and `theme.controlRenderer.MeasureTab/ChipRemoveRect/CalculateClockDialMetrics` (label widths → font advances). Date picker tests use `CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek` in both the test and `NowDatePicker` → consistent under any culture; do **not** set `InvariantGlobalization` in the Tests csproj unless the runtime does the same (H.17). `ProgressBarIndeterminate...` uses `Mathf.Repeat` (shim must match Unity: `t - Floor(t/len)*len` clamped). `Now.ProgressBar(...).SetTime(time)`, `NowTooltip.For` with snapshot time — caller-timed, no clock dependency. `TreeView` tests draw inside `NowLayout.Area(new Vector4(...))`.

**NowInspectorTests.** `RichTarget` declares `public LayerMask layers = 1;` and drawing it goes through `NowLayout.LayerMaskField()` → `NowMaskField` → `LayerMask.LayerToName(layer)` for 32 layers (cached on first draw). The shim must return non-throwing names (empty string for undefined layers is exactly what Unity returns for unnamed layers; a host-settable `string[32]`, default all empty, is enough — `NowMaskField` skips empty names). `RichTarget.rotation = Quaternion.Euler(0, 90, 0)` and `QuaternionRowsAreStableAcrossFrames` asserts `before == target.rotation` after two draws → `Quaternion.Euler` + `eulerAngles` round trip in the shim must be Unity-exact (spec §Quaternion), or `NowInspector.DrawQuaternion`'s `SameQuaternion` tolerance hides it — it compares the cached euler state, so exactness of `Euler` alone suffices. `MembersFollowUnitySerializationRules` needs `SerializeField`, `HideInInspector`, `HeaderAttribute(string)`, `SpaceAttribute(float)`, `RangeAttribute(float,float)`, `TextAreaAttribute(int,int)` as real attribute classes in `UnityEngine` (inventory C.14). `Bounds(Vector3, Vector3)` with `center/extents` (`NowInspector.DrawBounds` writes `new Bounds(center, extents * 2f)`).

**NowDialogTests.** `NowViews.MessageBox/Confirm` draw buttons + labels through the theme → font.

**NowContextMenuOwnerLifetimeTests.** Deferred. Beyond `GameObject`/`MonoBehaviour`, the interesting seam is `NowOverlay.Host(Component)`/`ReleaseRegistrationOwner(object)`/`EndFrameTransaction(completed:false)` — the H.4 design for overlay host identity decides whether this file can move to Tier B in M2 (an `object owner` overload plus a shim `Component` with `AddComponent<T>` and fake-null would let it run unchanged).

**NowLayoutTests.** The only file with `#pragma warning disable NOWUI002` (explicit `End*` primitives). Do **not** reference `NowUI.Analyzers.dll` from the Tests csproj (Unity's Tests asmdef is outside `Assets/NowUI`, so the analyzer never ran on tests there either; `_ = NowLayout.Label(...)` discards are the NOWUI001 opt-out and would otherwise warn). `LottieUsesNativeSizeAndDerivesAspect` calls `NowLottieAsset.SetSource(json)` → `NowLottieComposition.Parse` (pure). `NowLottieCache.SetAsset/Reset` — the B.3 split keeps these in core. `FrameScopeRestoresScaleAndReportsTrackedRepaint` uses `NowFrame.Begin(2f, trackRepaint: true)` — `NowFrame` has `ProfilerMarker` fields (B.2) → shim `Unity.Profiling.ProfilerMarker` with `Auto()`.

**NowNodeGraphTests.** 117 cases, the largest file. Draws through `NowNodes.Canvas(...).Draw()`: node chrome via `Now.Rectangle`, titles via text, links via `NowLine` (→ `Resources.Load<Material>("NowUI/BezierMaterial")` / `Shader.Find("NowUI/UI Bezier")`), search popup via `NowOverlay`, context menu via `NowContextMenu`. Asserts `_drawList.hasGeometry` true/false (`CanvasCullsBuiltInNodesOutsideTheViewport` expects **false** with background+grid off → the null backend must not inject any geometry of its own) and `mesh.vertexCount` deltas. `ContentRowsStayInGraphSpaceWithCanvasZoom` reads `Now.currentTransform.scale`. `RerouteNodesArePillsAndForwardValues` etc. use `NowNodeGraphEvaluator<float>`. `NowNodeGraphClipboard.shared.Clear()` in `SetUp`. `NowNodeGraph.Texture(Texture, ...)` exists but no test passes a texture (the `Texture(14)` grep hits are `TextureNode` constants). ALLOC test `CanvasDrawIsAllocationFreeAfterWarmup` (R6).

**NowTextStylingTests.** The most demanding Mesh read-back user: `GetUVs(5)` extras layout (16 entries for "AB" with outline, 8 for "A"), `GetUVs(1)` glyph rect, `GetUVs(3)` colour alpha, `GetUVs(2)`/`GetUVs(6)`, `GetNormals` + `GetUVs(3)` for the `NowMeshLayout.Canvas` list, `subMeshCount == 1`, `batchCount`, `batches[0].kind == NowMeshKind.Text`. Because `NowMesh.UploadMesh` takes the native `TryAppendNativeRenderVertices` path only when nowui-vg is present, the standalone build always uses the `SetVertices` + eight `SetUVs(channel, Vector4[], start, length, flags)` path — the shim `Mesh` must store each channel verbatim and hand it back through `GetUVs(int, List<Vector4>)` / `GetNormals(List<Vector3>)` (inventory row 96 lists only the setters — add the getters). `NowMesh.TextCanvasColorToWorkingSpace(color, ColorSpace.Linear)` → shim `ColorSpace` enum + `Mathf.GammaToLinearSpace` (Unity's piecewise sRGB formula, spec §Mathf). `NowGradientRampCache` builds a `Texture2D(256?, n, TextureFormat.RGBA32, false, linear: true)` and calls `SetPixels32(0, row, w, 1, Color32[])` + `Apply(false, false)` → shim `Texture2D` needs managed pixel storage for that overload (the ramp `w` component in `GetUVs(5).w > 0` assertions only needs the encoded row index, not GPU sampling). `GradientTextDrawsRemainTextAndBatchTogether` expects one batch for two gradient text draws → both draws must resolve to the same font `Material` instance (the fixture's Regular face material) — the provider must not clone materials per draw.

**NowTextShapingTests.** With HarfBuzz compiled out, `NowFont.TryGetShapedRun` returns false → 6 `Assert.Ignore`. The two live cases: `SpanMeasureMatchesCodepointStringMeasure` (sets `Now.textShaping = false`) and `ShapedDrawProducesGeometry` (draws "Affinity AV fi", "AB\nCD\tE", "AB"; U+E321 is absent from the atlas and, with no `_fontBytes`/dynamic bake, simply draws nothing for that glyph — geometry still comes from A/B).

**NowControlsTests.** 84 cases. Clock-dependent: `TransitionDoesNotAdvanceDuringPassiveMeasurePass`, `TransitionAdvancesOnFirstActiveFrameAfterIdle`, `PressAnimationAdvancesAndRequestsRepaintWhileActive` (all `Thread.Sleep(20)` then expect `NowControlState.Transition/PressAnimation` to have advanced via `Time.realtimeSinceStartup`), `ScheduledCaretBlinkRequestsOnlyItsNextPhaseBoundary` (`Is.InRange(0f, 0.51f)` on `nextRepaintAt - Time.realtimeSinceStartup`), `RepaintTrackerWaitsForScheduledDeadline`. `ImmediateTabNavigationDoesNotWaitForUnityFrameCount` explicitly relies on `Time.frameCount` **not** advancing between two `NowInput.Begin` scopes in the same test. `EventSystemSelectionSuspendsNowFocus` is under `#if NOWUI_UGUI` → excluded (define absent). `DefaultThemeIsAvailable` uses `Assert.AreEqual(NowTheme.themeAsset, NowTheme.themeAsset)` → `UnityEngine.Object.Equals` reference/instance-id semantics. `ThemeScopesRestorePreviousTheme` destroys themes at the end — the default theme must never be the destroyed one (it is a hidden `HideAndDontSave` default; fine).

**Simple.cs.** `new Texture2D(100, 200)` (2-arg ctor, defaults RGBA32/mipChain=true in Unity) and `font.atlas.width/height` used by `NormalizeGlyphAtlasBounds`; `MeasureTextBounds` memo asserts exact `Vector4` equality across calls (`bounds * 2f` for double size).

---

## 3. Recommended M1 include list

### 3.1 `Standalone/Tests/Tests.csproj` `<Compile Include>` set

```
../../Assets/NowUITests/Support/NowInputReplay.cs
../../Assets/NowUITests/Support/NowPopupTestDriver.cs
../../Assets/NowUITests/BenchmarkSupport/NowBenchmarkAllocations.cs
../../Assets/NowUITests/Simple.cs
../../Assets/NowUITests/NowRectTests.cs
../../Assets/NowUITests/NowResolvedIdTests.cs
../../Assets/NowUITests/NowControlStateResolvedIdTests.cs
../../Assets/NowUITests/NowFocusResolvedIdTests.cs
../../Assets/NowUITests/NowOverlayResolvedIdTests.cs
../../Assets/NowUITests/NowIdentityIntegrationTests.cs
../../Assets/NowUITests/NowTextEditTests.cs
../../Assets/NowUITests/NowNumericExpressionTests.cs
../../Assets/NowUITests/NowNodeGraphIndexedEvaluationTests.cs
../../Assets/NowUITests/NowFilePickerTests.cs
../../Assets/NowUITests/NowFilePickerUserFoldersTests.cs
../../Assets/NowUITests/NowFontResolutionTests.cs
../../Assets/NowUITests/NowFontStackTests.cs
../../Assets/NowUITests/NowMaskShapeTests.cs
../../Assets/NowUITests/NowCornerRadiusTests.cs
../../Assets/NowUITests/NowInteractionRegionTests.cs
../../Assets/NowUITests/NowInteractionRepaintTests.cs
../../Assets/NowUITests/NowContextInputRoutingTests.cs
../../Assets/NowUITests/NowFocusHostRegistryTests.cs
../../Assets/NowUITests/NowLayoutTests.cs
../../Assets/NowUITests/NowTextWrapTests.cs
../../Assets/NowUITests/NowTextAreaTests.cs
../../Assets/NowUITests/NowTextSelectionTests.cs
../../Assets/NowUITests/NowTextPreprocessorTests.cs
../../Assets/NowUITests/NowMarkupTests.cs
../../Assets/NowUITests/NowCodeEditorTests.cs
../../Assets/NowUITests/NowDockingTests.cs
../../Assets/NowUITests/NowViewStackTests.cs
../../Assets/NowUITests/NowNewControlsTests.cs
../../Assets/NowUITests/NowInspectorTests.cs
../../Assets/NowUITests/NowDialogTests.cs
../../Assets/NowUITests/NowNodeGraphTests.cs
../../Assets/NowUITests/NowTextStylingTests.cs
../../Assets/NowUITests/NowTextShapingTests.cs
../../Assets/NowUITests/NowControlsTests.cs
../../Assets/NowUITests/NowGraphEvaluationPerformanceTests.cs   (Tier C; gate filter "Category!=NowUI.Overview")
```

Plus Tests-local sources (new, under `Standalone/Tests/Shims/`): `LogAssert.cs` (namespace `UnityEngine.TestTools`), `PerformanceTesting.cs` (namespace `Unity.PerformanceTesting`), `Recorder.cs` (namespace `UnityEngine.Profiling` — or put it in `NowUI.Engine` if the runtime ever needs it; today only BenchmarkSupport does), `TestHost.cs` (`[SetUpFixture]` that installs the null backend, the resource provider with the font fixture, the log policy, and an assembly-level `ITestAction` that calls `NowRuntime.BeginFrame()` before each test), `AssemblyInfo.cs` (`[assembly: NonParallelizable]`, `[assembly: LevelOfParallelism(1)]`).

Explicitly **excluded** in M1: `NowIMGUIKeyInputRoutingTests.cs`, `NowIMGUITextInputRoutingTests.cs`, `NowKeyBindingFieldTests.cs`, `NowContextMenuOwnerLifetimeTests.cs`, and everything the inventory already lists as graphics-device/host-bound (renderer, SDF, mask texture, gradient, glass, screen, world graphic, model preview, Lottie asset/Burst, theme asset, markdown, editor GUI host, UI Toolkit, visual harness, performance suites other than the graph one, pointer arbiter, popup UX, value controls, text field editing, composition cursor, input, input replay, all PlayMode).

References: `NowUI.Engine`, `NowUI.Runtime`, `NowUI.Extensions.Markup`, `NowUI.Extensions.CodeEditor`, `NowUI.Extensions.Docking`, `NowUI.Extensions.NodeGraph` (Markdown/MarkdownMarkup/Sdf are not needed by the subset but referencing them is harmless and keeps the D2 assembly set compiling as a whole). Packages: `NUnit 3.14.0`, `NUnit3TestAdapter 4.6.0`, `Microsoft.NET.Test.Sdk 17.x`. `AllowUnsafeBlocks` false (matches the asmdef). No analyzer reference. Define `NOWUI_STANDALONE` (and `DEVELOPMENT_BUILD` in Debug so the guarded dev warnings match what EditMode sees under `UNITY_EDITOR`; several tests — `NowMarkupTests.UnknownResultQueriesWarnOnce`, leaked-scope warnings — only log under those defines; run the gate in Debug).

### 3.2 Expected counts

| | Files | Cases | Expected skips |
|---|---|---|---|
| Gate (A + B) | 36 | 783 | 9 (`Assert.Ignore`: 6 NowTextShapingTests, 3 NowTextStylingTests) |
| + Tier C (perf) | 37 | 801 | 9 |
| Deferred (D) | 4 | 50 | – |
| Unity-guarded, dead in standalone | – | 1 (`NowControlsTests.EventSystemSelectionSuspendsNowFocus`, `#if NOWUI_UGUI`) | – |

The 4 allocation-zero cases (`NowMarkupTests.CachedDocumentDrawIsAllocationFreeAfterWarmup`, `NowNodeGraphIndexedEvaluationTests.IndexedScopeConstructionAndEvaluationAllocateNothingAfterWarmup`, `NowTextStylingTests.AnimatedGradientTextIsAllocationFreeAfterWarmup`, `NowNodeGraphTests.CanvasDrawIsAllocationFreeAfterWarmup`) become **strict** under .NET (see R6); they are in the gate because the null backend is expected to be allocation-free in steady state, but they are the first candidates for a `[Category("Allocation")]` opt-out if the shim needs another iteration.

### 3.3 The font fixture (prerequisite for 31 of 36 gate files)

- **Source**: `Assets/NowUI/Assets/Resources/NowUI/NotoSans.asset` (a `NowFontFamily`: `_regular/_bold/_italic/_boldItalic` → `Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-{Regular,Bold,Italic,BoldItalic}.ttf.asset` (`NowFont`, ~630 KB each serialized incl. atlas + `_fontBytes`), `_fallbacks` → NotoSansJP/KR/SC/Arabic/OpenMoji/MaterialDesign families).
- **Export (Unity editor, one-off, checked in)**: a `NowUI.Editor` menu/`-executeMethod` `NowStandaloneFontExporter.Export` writing `Standalone/Tests/Fixtures/NowUI/NotoSans.family.json` + per face `NotoSans-Regular.font.json` = `{ name, atlasWidth, atlasHeight, isColor, atlasInfo: NowFontAtlasInfo (atlas/metrics/glyphs verbatim, pixel-space bounds exactly as serialized), materialTemplate: "NowUI/TxtMaterial" | "NowUI/TxtMaterialRGBA" }`. Atlas pixels and `_fontBytes` are **not** needed by the subset (no test samples the atlas; no test needs a glyph outside the pre-baked table except U+E321 which is intentionally missing) — omit them in M1 to keep the fixture ~1–2 MB of JSON; the format reserves `atlasPng`/`fontBytesBase64` fields for M2 (managed baker) and M3 (real rendering). Fallback families can be omitted in M1 (`_fallbacks` = empty) — no subset test needs CJK/emoji glyphs; note that omitting them changes nothing observable for these tests because fallback traversal only happens on glyph misses.
- **Load (Tests host / `NowUI.Engine` resource provider)**: `ScriptableObject.CreateInstance<NowFont>()`, set `name`, `atlas = new Texture2D(w, h, RGBA32, false)` (metadata only), `atlasInfo`, `material = new Material(Resources.Load<Material>(template)) { mainTexture = atlas }`; then `CreateInstance<NowFontFamily>()` and populate `_regular/_bold/_italic/_boldItalic/_fallbacks`. `NowFontFamily` has no setters today (H.7) — M1 can use reflection from the provider (exactly what `NowFontResolutionTests` already does) or add `internal` setters guarded by `#if NOWUI_STANDALONE` (behaviour-neutral in Unity). Register under `"NowUI/NotoSans"` and return the **same instance** on every `Resources.Load` (tests compare by reference and `Now.defaultFont` is reloaded after `TearDown` sets it to null).
- Alternative considered and rejected for the gate: a synthetic monospace `NowFont` built in the Tests host. It would pass every self-consistent measurement but would not exercise the real glyph table shape (sparse ranges, plane bounds, outline padding) that `NowTextStylingTests` and `Simple.cs` probe, and M2/M3 need the export path anyway.

---

## 4. What the null backend, `Time`, `Screen` and the resource provider must provide

### 4.1 Null render backend (capture path only)

No test in the subset touches a device. Requirements are object-model requirements on the shim:

- `Mesh`: `..ctor()`, `name`, `hideFlags`, `MarkDynamic()`, `Clear(bool)`, `indexFormat` set, `subMeshCount` get/set, `bounds` set, `SetVertices(Vector3[], int, int, MeshUpdateFlags)`, `SetUVs(int channel, Vector4[], int start, int length, MeshUpdateFlags)` ×8 channels, `SetNormals(...)` and `SetUVs(3, ...)` for the Canvas layout, `SetIndexBufferParams(int, IndexFormat)`, `SetIndexBufferData<T>(T[], int, int, int, MeshUpdateFlags)` for `ushort[]` and `int[]`, `SetSubMesh(int, SubMeshDescriptor, MeshUpdateFlags)`, `SetVertexBufferParams/SetVertexBufferData` (compile only; native path never taken), **plus read-back** `vertexCount`, `GetUVs(int, List<Vector4>)`, `GetNormals(List<Vector3>)` returning exactly what was set (copy semantics like Unity: clear the list, then append `length` items). Storage must be reused across `Clear` → `Set*` cycles without per-frame allocation (R6).
- `Material`: `..ctor(Material)` clone, `..ctor(Shader)`, `name`, `hideFlags`, `shader`, `mainTexture` get/set (same instance back), `HasProperty/SetFloat/GetFloat/SetVector/SetVectorArray/SetTexture/SetColor/SetInt`, identity via `UnityEngine.Object` instance ids (batch keys compare materials by reference). Destroy → fake-null.
- `Texture2D`: `..ctor(int,int)`, `..ctor(int,int,TextureFormat,bool)`, `..ctor(int,int,TextureFormat,bool,bool)`, `width/height`, `SetPixels32(int x,int y,int w,int h,Color32[])`, `SetPixels32(Color32[])`, `Apply(bool,bool)`, `GetPixels32` (NowGradientRampCache only writes), `filterMode/wrapMode` setters, `name/hideFlags`. Managed byte storage is enough.
- `Shader`: `PropertyToID(string)` usable from static initializers (stable ids, no backend), `Find(string)` returning a shim `Shader` for `NowUI/UI Bezier`, `NowUI/Color Picker`, `NowUI/UI Ripple` (never null for the 9 core names).
- `Resources.Load<Material>` must return non-null shim materials for `NowUI/UIMaterial`, `TxtMaterial`, `TxtMaterialRGBA`, `GradientMaterial`, `GlassMaterial`, `GlassBlurMaterial`, `RippleMaterial`, `BezierMaterial` (the subset draws rectangles, text, gradient text, node-graph Bezier links and control ripples; glass is never drawn but `NowGlass` statics may load lazily). A null return produces `Debug.LogError` via `LoadRequiredResource` and — under the parity log policy — a failed test.
- `Object`: `DestroyImmediate`, `Destroy` (Tests run with `Application.isPlaying = false`, so `NowDrawList.Dispose` takes the `DestroyImmediate` branch, as in EditMode), fake-null `==`/`!=`/implicit bool, instance-id `GetHashCode`.
- `ScriptableObject.CreateInstance<T>()` for any `T : ScriptableObject` including subclasses declared in the `Tests` assembly (`TrackingFont : NowFont`, `RecordingRenderer : NowControlRenderer` ×2) → `Activator.CreateInstance<T>()` + `OnEnable` dispatch, per D4.
- `ProfilerMarker` (`Unity.Profiling`) with `Auto()`/`Begin()`/`End()` no-ops (NowFrame, NowOverlay, NowFont, Now).
- Nothing else: no `Graphics`, `GL`, `RenderTexture`, `CommandBuffer`, `Camera` calls are reachable from the subset.

### 4.2 `Time`

- `Time.frameCount` must be **constant for the whole duration of a test**. Tests never expect it to advance; they force frame swaps with `NowFocus.ForceNewFrame` / `NowOverlay.ForceNewFrame` / `NowPointerArbiter.ForceNewFrame` and reset state in `[SetUp]`. `NowControlsTests.ImmediateTabNavigationDoesNotWaitForUnityFrameCount` and every `NowInput.Begin` scope (`_scopeStartedAt == Time.frameCount`) depend on it.
- Recommended: advance `frameCount` exactly once **before each test** (assembly-level `ITestAction.BeforeTest` → `NowRuntime.BeginFrame()`), so frame-keyed registries (`_registryFrame`) roll over between tests the way they do between editor updates in Unity, and stale state from a test that forgot a `Reset()` cannot alias into the next one. Starting value 1 (Unity's `frameCount` is ≥ 1 in the editor); tests only use it relatively.
- `Time.realtimeSinceStartup` (float) and `realtimeSinceStartupAsDouble` must be a **live** `Stopwatch`-backed monotonic clock (read at call time). Thread-sleeping tests (`NowViewStackTests` ×1, `NowControlsTests` ×5) fail if the clock is frame-latched. Resolution ≥ 1 ms.
- `Time.deltaTime`/`Time.time`: not read by the subset (`NowRichTextParser` Lottie tag only); return 0/elapsed.

### 4.3 `Screen` / `Application`

- `Screen.width/height`: not read by any subset test path (all surfaces are explicit `NowInput.Begin(provider, size)`); defaults 1920×1080 are fine. `Screen.dpi`: `NowInput` computes touch slop `max(4, 4*dpi/160)`; any value ≤ 160 (or 0 = unknown) yields Unity-editor behaviour (4 px) — default 96. `Screen.safeArea = (0,0,w,h)` bottom-left per D5.
- `Application.isPlaying`: host-settable; the Tests host sets **false** to mirror EditMode (`NowDrawList` destroy branch, `NowValueControls`/`NowFilePicker` edit-mode gates, `Now.cs` 1103). `Application.platform`: OS-mapped `RuntimePlatform` (see NowTextEditTests note). `Application.persistentDataPath`: any writable temp dir (not read by the subset). `Application.quitting`: event that never fires.

### 4.4 Log policy (Unity parity)

Unity's test runner fails a test when an `Error`/`Exception`/`Assert` log is received that was not registered through `LogAssert.Expect`, and `LogAssert.NoUnexpectedReceived()` additionally fails on any unexpected log at all. The Tests host must reproduce this: the shim `Debug` exposes a log sink (`Debug.logHandler`-style); the Tests `ITestAction` clears the per-test log scope before each test and, after it, fails the test if unconsumed error-level entries remain. Three subset cases depend on `LogAssert` directly (NowTextPreprocessorTests ×1, NowMarkupTests ×2); the implicit rule is what makes a missing font/material visible as a test failure instead of silently-blank geometry.

---

## 5. NUnit mapping (Unity custom NUnit 3.5 → NuGet)

- **Pin `NUnit 3.14.0`** (last 3.x). NUnit 4 moves `Assert.AreEqual/IsTrue/IsFalse/IsNull/NotNull/AreSame/Greater/Less/GreaterOrEqual/LessOrEqual/IsEmpty/Fail`, `CollectionAssert`, `StringAssert` to `NUnit.Framework.Legacy.ClassicAssert` — every subset file would stop compiling. With 3.14 the whole subset compiles unchanged.
- Used and identical in 3.14: `[Test]`, `[TestCase]` with `string/double/long/bool/int/enum/null` arguments (`[TestCase(null)]` on a `string` parameter is fine), `[SetUp]/[TearDown]/[OneTimeSetUp]`, `[Category]`, `Assert.That(x, Is.EqualTo(v).Within(d))`, `Is.True/False/Zero/Not.EqualTo/InRange`, `Assert.AreEqual(float,float,float delta)` and the `double` overload, `Assert.AreEqual(object,object)` (→ `Equals`, so shim struct `Equals(object)` must be Unity-exact: Vector2/3/4, Color, Rect, RectInt, Vector2Int, Quaternion, NowNodeLink), `Assert.Throws<T>(TestDelegate)` returning the exception (`error.ParamName` asserted in NowResolvedIdTests), `Assert.DoesNotThrow`, `Assert.Ignore(string)` (→ `IgnoreException`, reported as **Skipped** by `dotnet test`; the CI gate must count skips as non-failures, 9 expected), `Assert.IsEmpty(IEnumerable)`, `CollectionAssert.AreEqual`, `StringAssert.Contains`.
- Not used by the subset: `[UnityTest]`/`IEnumerator` tests, `[UnityPlatform]`, `[RequiresPlayMode]`, `[TestCaseSource]`, `[Values]/[Range]/[Combinatorial]` (the `Values(`/`Range(`/`Order(`/`Explicit`/`Retry(` grep hits are NowUI method names, not attributes), `Assume`, `TestContext`, `[Timeout]`, `[Parallelizable]`.
- Unity-only pieces that need Tests-local shims: `UnityEngine.TestTools.LogAssert` (`Expect(LogType, string)`, `Expect(LogType, Regex)`, `NoUnexpectedReceived()`, plus `ignoreFailingMessages` for completeness), `Unity.PerformanceTesting` (`PerformanceAttribute : NUnitAttribute` (a plain attribute is enough; Unity's derives from `NUnit.Framework.CategoryAttribute("Performance")` — mirror that so `--filter Category!=Performance` also works), `Measure.Method(Action) → MethodMeasurement { SampleGroup(SampleGroup), WarmupCount(int), MeasurementCount(int), IterationsPerMeasurement(int), Run() }`, `Measure.Custom(SampleGroup, double)`, `SampleGroup(string name, SampleUnit unit, bool increaseIsBetter = false)`, `enum SampleUnit { Nanosecond, Microsecond, Millisecond, Second, Byte, Kilobyte, Megabyte, Gigabyte, Undefined }`), `UnityEngine.Profiling.Recorder` (`static Recorder Get(string)` → instance with `isValid = false`; `enabled` get/set, `FilterToCurrentThread()`, `CollectFromAllThreads()`, `sampleBlockCount`).
- Execution model: Unity runs EditMode tests sequentially on the main thread; NUnit3TestAdapter runs a single assembly sequentially unless `[Parallelizable]` is present — still add `[assembly: NonParallelizable]` because every NowUI subsystem is a static singleton. Order: Unity runs fixtures alphabetically; NUnit does the same by default. Nothing in the subset is order-dependent by design (all reset in `SetUp`), but `Now.defaultFont` and `NowLayout.labelStyle` are global and restored in `TearDown` by the files that touch them.
- Test discovery: fixtures are plain classes without `[TestFixture]` (NUnit 3 discovers by `[Test]`) — works in 3.14. `NowInspectorTests.cs` holds four fixtures in one file; NUnit handles that.

---

## 6. Extra shim members the tests need beyond the runtime inventory

Only items **not already** implied by inventory section A:

1. `UnityEngine.Texture2D(int width, int height)` 2-arg ctor (Simple.cs). Inventory row 91 lists only the 4/5-arg ctors.
2. `UnityEngine.Mesh.GetUVs(int channel, List<Vector4>)` and `GetNormals(List<Vector3>)` read-back (NowTextWrapTests, NowTextStylingTests); `vertexCount`/`subMeshCount` are already listed.
3. `UnityEngine.ColorSpace` enum (`Linear`, `Gamma`) as a public type usable from tests (`NowMesh.TextCanvasColorToWorkingSpace(Color, ColorSpace)` is called directly). Inventory H.14 discusses `QualitySettings.activeColorSpace` but not the enum's public use.
4. `UnityEngine.RangeAttribute(float, float)` reachable as `[UnityEngine.Range(...)]`, `HeaderAttribute(string)`, `SpaceAttribute(float)`, `TextAreaAttribute(int, int)`, `HideInInspector`, `SerializeField` — declared **on test-assembly fields**, so they must be real, public attribute classes with those ctors (C.14 says "shim must define attribute classes"; this pins the ctor shapes).
5. `LayerMask.LayerToName(int)` host table: default `string[32]` all empty (Unity returns `""` for unnamed layers), host-settable. Inventory row 35 says "host-provided" — the test host provides the default.
6. `Debug.LogException(Exception)` message format `"{TypeName}: {Message}"` and a pluggable sink for `LogAssert`.
7. `Application.isPlaying` **settable** by the host (Tests set `false`); inventory C.6 recommends `isPlaying = true` for runtime hosts.
8. `Application.platform` mapped to the six desktop `RuntimePlatform` members `NowFilePickerUserFolders.Platform` understands.
9. `Time.realtimeSinceStartup` live (not frame-latched) — D5 says monotonic; this adds "read at call time".
10. `ScriptableObject.CreateInstance<T>()` for subclasses defined outside `NowUI.Runtime` (Activator-based; D4 already implies it but the tests make it mandatory).
11. Tests-only namespaces: `UnityEngine.TestTools.LogAssert`, `Unity.PerformanceTesting.*`, `UnityEngine.Profiling.Recorder` (§5).
12. Font family population: `NowFontFamily._regular/_bold/_italic/_boldItalic/_fallbacks` and `NowFontAsset._fallbacks` settable from the resource provider (reflection or `#if NOWUI_STANDALONE internal` setters).
13. `ExpressionEvaluator.Evaluate(string, out double)` returning `false` (see R9) — inventory B.2 lists the block, this fixes the required behaviour.
14. `UnityEngine.Component` / `GameObject` / `MonoBehaviour` are **not** needed for the gate (only by the deferred `NowContextMenuOwnerLifetimeTests`).

Nothing in the subset needs `Event`, `GUI`, `GUIUtility.hotControl`, `Keyboard`, `Key`, `Camera`, `RectTransform`, `RenderTexture`, `CommandBuffer`, `Graphics`, `GL`, `QualitySettings`, `SystemInfo`, `TouchScreenKeyboard`, `UnityWebRequest`, `JsonUtility`, `PlayerPrefs`, or any `UnityEditor` type.

---

## 7. Risks

- **R1 Font fixture is the critical path.** 31/36 gate files touch `Now.defaultFont` (directly or via theme text/labels). Without it, `LoadRequiredResource` logs an error (one-shot) and every text draw is silent: `hasGeometry` asserts fail, all pointer positions derived from label widths collapse, and the parity log policy fails the first test. Mitigation: ship the export in M1 (§3.3); until it lands, run only the 12 files that never resolve a font (1–6, 9, 14, 18, 19, 25, 26, 29, 34 → 165 cases) as the smoke gate.
- **R2 Materials/shaders as required resources.** `NowNodeGraphTests` (Bezier), gradient text (`GradientMaterial` is not used by text — text gradients ride the text material — but `NowGradient` rect gradients might be drawn by control renderers), ripples on buttons (`RippleMaterial`), and glass lazies must all return non-null shim materials or the log policy fails them. Mitigation: provider registers all 9 core templates + `Shader.Find` names from G.3 with placeholder shim shaders.
- **R3 InternalsVisibleTo / reflection on private names.** The Tests assembly must be named exactly `Tests` (no strong name) to keep `InternalsVisibleTo("Tests")` from `NowUI.Runtime`, `Markdown`, `Markup`, `Sdf` (internal APIs used: `NowFocus.ForceNewFrame/ProcessImmediateTabNavigationPass/immediateRegistrationCount`, `NowOverlay.ForceNewFrame/Flush/DeferScreen(…)`, `NowFrame.DrawContent/MeasureContent/Begin`, `NowControls.AllocateOwnerScope/RestoreIdScope/ResolveCallSiteStable/ResetControlIdOccurrences`, `NowInteractionRepaintTracker`, `NowLayout.BeginMeasurePass/EndMeasurePass`, `NowGradientRampCache`, `NowMesh.TextCanvasColorToWorkingSpace`, `NowTextUnitCursor`, `NowInspectorGui`, `NowUI.Internal.StaticList`, `NowFilePickerUserFolders.*`). Reflection on private names that M1 refactors must not touch: `NowInput._snapshot`, `NowControlState.Store`1` + `entries` + entry `value`, `NowTextArea.AreaState.scrollY`, `NowCodeEditor.EditorState.scrollY` + `_caches`, `NowContextMenu._menus` + entry `entries/label/shortcut/enabled/deliveryId`, `NowControls.InteractionRepaintState`, `NowFontAsset._fallbacks`, `NowFontFamily._regular/_bold/_italic/_boldItalic`, `NowThemeAsset._controlRenderer`. Splitting a class into core/host partials keeps these names; moving them to another type would break the tests.
- **R4 Frame semantics.** If the shim advanced `frameCount` per `NowInput.Begin` or per draw list scope, `ImmediateTabNavigationDoesNotWaitForUnityFrameCount`, the IME capture test, and every `_scopeStartedAt == Time.frameCount` guard would misbehave. Keep it per-host-frame (D5) and per-test in the Tests host.
- **R5 Wall-clock tests.** Six cases sleep 20–60 ms and compare against `realtimeSinceStartup`; they are inherently timing-sensitive (same in Unity) and fail on a frame-latched clock. Under heavy CI load `ScheduledCaretBlink...` (`Is.InRange(0f, 0.51f)`) can flake exactly as it can in Unity.
- **R6 Allocation-zero tests turn strict.** `NowBenchmarkAllocations` probes `GC.GetAllocatedBytesForCurrentThread`; on .NET 9 it is exact, so the four `AssertZero` cases measure every byte the shim allocates per frame (Unity Mono may report bytes too, so this is parity, but the shim is new code). Any `new` in `Mesh.Set*`, `Material` property setters, `Texture2D.Apply`, `Resources.Load`, or `Debug` logging on the steady-state draw path fails them. Mitigation: design the shim with pre-sized buffers and no boxing; keep `[Category("Allocation")]` as an escape hatch.
- **R7 Log policy strictness.** Emulating Unity's "unexpected error log fails the test" is what makes missing resources visible, but it also means any `Debug.LogError` the shim itself emits (e.g. an unsupported `Material.SetTexture` warning) fails tests. Keep the shim silent on the capture path; log only at `Warning` level for unimplemented device features.
- **R8 Deferred host classes and `NowInput.Reset()`.** Almost every `[SetUp]` calls `NowInput.Reset()`, which today calls `NowIMGUIInputProvider.instance.ResetState()` and `NowGUI.ResetInputProviders()` (B.2 row for `NowInput.cs`). The H.4 guard/interface for those sites must keep `Reset()` public and side-effect-equivalent, or every Tier A/B file fails at `SetUp`.
- **R9 `NowNumericExpression` parity.** In Unity the 61 golden values come from `UnityEngine.ExpressionEvaluator` when it accepts the input and from the bounded fallback otherwise; standalone uses the fallback for all of them. The fallback was written to match (right-associative `^`, unary minus below `^`, scientific notation), but the first standalone run must confirm all 61; a divergence would mean a shim `ExpressionEvaluator` port rather than a test edit.
- **R10 Shaping-dependent skips.** 9 cases `Assert.Ignore` because HarfBuzz is compiled out (D6). CI must treat `Skipped` as pass; when M2 adds a managed shaper these become live and may need new golden expectations.
- **R11 Platform-dependent paths.** `NowFilePickerUserFoldersTests` and `NowFilePickerTests` use `Path.GetTempPath()`, `Path.GetPathRoot`, `Environment.SpecialFolder` and `"bad\0name.json"` — valid on Windows and Linux; on Linux `PathComparer(Windows)` is still exercised via the explicit enum, so results are OS-independent. `NowTextInput.isMacPlatform` default differs on macOS runners (the test that cares sets it explicitly).
- **R12 Culture.** `NowNewControlsTests` calendar geometry follows `CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek` in both test and control; consistent, but do not enable `InvariantGlobalization` in Tests only (H.17 should decide for runtime and tests together).
- **R13 Dev-only diagnostics.** Warnings asserted by `NowMarkupTests.UnknownResultQueriesWarnOnce` and the leaked-scope warnings are compiled under `UNITY_EDITOR || DEVELOPMENT_BUILD`; the standalone Debug configuration must define `DEVELOPMENT_BUILD` (F.2/H.11) or that test fails with "expected log not received".
- **R14 `Assert.AreEqual` on shim structs.** Vector/Color/Rect/Quaternion `Equals(object)` must be exact-component (not the approximate `==`) and `GetHashCode` consistent, as specified in `UnityValueTypeSemantics.md`; several tests use `Assert.AreEqual(structA, structB)` where Unity's `==` would have tolerated ULP noise but `Equals` does not — the spec already separates the two.
- **R15 Analyzer.** Referencing `NowUI.Analyzers.dll` from Tests would raise NOWUI001/NOWUI002 warnings (and errors under warnings-as-errors) on the deliberate discards in `NowLayoutTests`; do not reference it from Tests (Unity's Tests asmdef never saw it).
- **R16 Editor-only fixtures.** `NowIMGUI*Tests` depend on `Event.current`/`GUIUtility.hotControl` (IMGUIModule) and `NowGUI`, `NowKeyBindingFieldTests` on Input System `Key`; `NowContextMenuOwnerLifetimeTests` on `GameObject`/`MonoBehaviour`. They stay Unity-only; the last one is the cheapest to bring over in M2 (needs `NowOverlay.Host(object)` + a tiny `Component` shim).

---

## 8. What is deferred and why (H.12/H.13/H.16 answers for tests)

- **H.16 chosen subset**: §3.1 (36 files / 783 cases gate, +18 optional). The null backend for tests is the capture-only object model of §4.1; no immediate-mode device emulation is required for M1 tests, which keeps H.2 open without blocking the gate.
- **H.12 browser IO**: the subset uses real `System.IO` (`NowMarkupFile` watcher, `NowFilePickerUtility.BuildSavePath/BuildOpenPath`, temp files). M1 keeps the desktop `System.IO` implementation behind whatever `INowFileSystem` seam the split introduces; nothing browser-specific is tested.
- **H.13 NativeArray**: not reached by any subset test (`NowFontCompiler.DynamicSession.TryCopyAtlas`, `Texture2D.GetRawTextureData<T>` are only used by the font pipeline, which the fixture bypasses by omitting `_fontBytes`). Compile-only shim is sufficient.
- **Deferred files** (50 cases): `NowIMGUIKeyInputRoutingTests` (11), `NowIMGUITextInputRoutingTests` (21), `NowKeyBindingFieldTests` (11), `NowContextMenuOwnerLifetimeTests` (7). The first three are host-input adapters by definition and belong with the Unity host assembly's own test run; the fourth is a candidate for M2 once overlay host identity (H.4) is settled.
