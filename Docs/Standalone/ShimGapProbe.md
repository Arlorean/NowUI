# Shim gap probe — measured, 2026-09-07

An empirical check of how much of `Assets/NowUI/Runtime` the standalone shim must still cover. It exists because the
dependency inventory was produced by reading code, and a compiler is a better witness than a reader.

## Method

A throw-away SDK-style project (net9.0, `LangVersion latest`, `AllowUnsafeBlocks`, `DefineConstants=NOWUI_STANDALONE`,
`EnableDefaultCompileItems=false`) compiles every file under `Assets/NowUI/Runtime/**/*.cs` together with a copy of the
shim sources under test. Nothing is excluded, so the counts include the host-only files that the real standalone build
will drop; treat them as an upper bound. Errors are counted per diagnostic, so one missing type appears once per use.

Only the first resolution pass is visible: member-level errors (a missing method on a type that does resolve) appear
only after the type exists, so these totals shrink faster than the real remaining work does.

## Results

| Shim contents | `error CS` count |
|---|---|
| Nothing (bare `dotnet build` of Runtime) | 3342 |
| Value types only (Vector/Color/Rect/Matrix4x4/Quaternion/Bounds/Mathf/Plane + ColorSpace) | 1587 |

## What is still missing, ranked by diagnostic count

Grouped by the shim unit that would resolve them. The count is the sum of the individual type counts in that group.

| Group | Count | Types |
|---|---|---|
| Serialization and lifecycle attributes | ~570 | `SerializeField` 226 (+226 as `SerializeFieldAttribute`), `RuntimeInitializeOnLoadMethod` 38 (+38 attribute form, +38 `RuntimeInitializeLoadType`), `HideInInspector` 14 (+14), `AddComponentMenu` 4, plus Tooltip/Header/Space/Range/Min/CreateAssetMenu/PreferBinarySerialization in the tail |
| Graphics resource handles | ~400 | `Material` 158, `Camera` 71, `RenderTargetIdentifier` 59, `CommandBuffer` 54, `RenderTexture` 38, `Texture` 36, `Mesh` 24, `Texture2D` 17, `RenderTextureDescriptor` 10, `UnityEngine.Rendering` namespace members 11, `MeshUpdateFlags` 4 |
| Gradient and curve layer | ~130 | `Keyframe` 46, `GradientAlphaKey` 24, `GradientColorKey` 20, `UnityEngine.Gradient` 19, `GradientMode` 13, `WrapMode` 12, `WeightedMode` 8, `AnimationCurve` 6, `Gradient` 7 |
| Collections and jobs stand-ins | ~106 | `NativeList<>` 40, `NativeArray<>` 38, `Unity` namespace root 20, `ReadOnly` 8 (+attribute form) |
| Scene types | ~70 | `Component` 23, `GameObject` 18, `Transform` 8, `Scene` 8, `Light` 8, `RectTransform` 7, `MonoBehaviour` 6 |
| Object model | ~9 | `UnityEngine.Object` 5, `ScriptableObject` 4 |
| Misc services | ~50 | `ProfilerMarker` 16, `RenderPipelineAsset` 10, `Event` 10, `LayerMask` 9, `KeyCode` 5, `EventType` 4, `GUILayoutOption` 4, `UnityWebRequest` 5, `UnityEngine.Networking` 4, `UnityEngine.SceneManagement` 1 |

## Implications for ordering

1. **Attributes first.** They are no-op classes with matching public members and clear about a third of the remaining
   diagnostics for very little code. `RuntimeInitializeLoadType` must be an enum, not just an attribute.
2. **Graphics handles are the real work** and the only group whose design matters for M2: `Material` as a property bag,
   `Mesh` retaining its managed streams, `Texture2D` with a CPU store, `RenderTexture` with a temp pool, `CommandBuffer`
   as a recorded op list.
3. **Camera at 71 is misleading.** Most uses are in host-only files that the standalone build excludes; the count will
   drop sharply once the exclude list is applied, so do not size a `Camera` shim from this number.
4. **The gradient and curve layer is well specified** by `GradientCurveSemantics.md` and is self-contained, so it can be
   built in parallel with the graphics handles.
5. Re-run this probe after each shim unit lands. Once the count stops falling faster than the units land, the remaining
   errors are member-level and the compile-error-driven loop over the core files begins.

---

# Re-run 2026-09-07 — the full shim fleet (U1–U13) has landed

Probe project: `scratchpad/probe3/` (unchanged method — net9.0, `LangVersion latest`, `AllowUnsafeBlocks`,
`EnableDefaultCompileItems=false`, `DefineConstants=NOWUI_STANDALONE`, compiling every file under
`Assets/NowUI/Runtime/**/*.cs` plus a copy of `Standalone/NowUI.Engine` minus `obj`/`bin`). Nothing under `Assets/` was
modified.

## Headline

| Shim contents | `error CS` count |
|---|---|
| Nothing (bare `dotnet build` of Runtime) | 3342 |
| Value types only | 1587 |
| **Full shim fleet U1–U13** | **49** |

49 diagnostics, 17 distinct types, confined to **9 files** — and all 9 are files the design already plans to remove or
split. Nothing in the standalone compile set is missing a type.

## What is still missing, ranked

| # | Type | Count | Only referenced from | Design disposition |
|---|---|---|---|---|
| 1 | `RenderPipelineAsset` | 10 | `NowModelPreview.cs` | §5.2 **exclude** |
| 2 | `Light` | 8 | `NowModelPreview.cs` | §5.2 exclude |
| 3 | `MonoBehaviour` | 6 | `NowWorldGraphic` 2, `NowLottieCache` 1, `NowPipelineGraphic` 1, `NowBootstrap` 1, `NowModelPreview` 1 | §5.3 exclude / §5.2 split |
| 4 | `UnityWebRequest` | 5 | `NowLottieAsset` 3, `NowLottieCache` 1, `NowFilePicker` 1 | §5.2 **split** (moves to `.Unity.cs`) |
| 5 | `UnityEngine.Networking` (namespace) | 4 | `NowLottieAsset`, `NowLottieCache`, `NowFilePicker`, `NowRemoteContent` | §5.2 split / **guard** |
| 6 | `RenderingPath` | 2 | `NowWorldGlassBackdrop.cs` | §5.3 exclude |
| 7 | `Renderer` | 2 | `NowModelPreview.cs` | §5.2 exclude |
| 8 | `MeshRenderer` | 2 | `NowWorldGraphic.cs` | §5.3 exclude |
| 9 | `MeshFilter` | 2 | `NowWorldGraphic.cs` | §5.3 exclude |
| 10 | `UnityWebRequestAsyncOperation` | 1 | `NowFilePicker.cs` | §5.2 split |
| 11 | `SkinnedMeshRenderer` | 1 | `NowModelPreview.cs` | §5.2 exclude |
| 12 | `ScriptableRenderContext` | 1 | `NowWorldGlassBackdrop.cs` | §5.3 exclude |
| 13 | `RenderPipeline` | 1 | `NowModelPreview.cs` | §5.2 exclude |
| 14 | `RenderParams` | 1 | `NowModelPreview.cs` | §5.2 exclude (see U11's objection) |
| 15 | `LoadSceneMode` | 1 | `NowModelPreview.cs` | §5.2 exclude |
| 16 | `DownloadHandlerScript` | 1 | `NowRemoteContent.cs` | §5.2 guard |
| 17 | `Coroutine` | 1 | `NowLottieCache.cs` | §5.2 split |

The list is shorter than 25 because only 17 distinct types remain.

## Correction to the method — the earlier numbers were declaration-phase only

The original write-up guessed that "member-level errors appear only after the type exists". That is not a tendency, it is
a hard gate, and it is worth stating exactly because it changes how all three headline numbers should be read:

> **Roslyn reports *no* method-body diagnostics at all while even one declaration-phase error is outstanding.**

Verified with a two-line project: a class with an unresolved field type and a class whose body contains both `int x =
"str"` and a call to an undeclared name reports **only** the field-type error. So 3342, 1587 and 49 all count unresolved
types in *declarations* (fields, parameters, return types, base types, `using`s). Every missing member, wrong overload and
bad conversion in a method body was invisible in all three measurements. The counts were never an upper bound on the
remaining work in the way the original text implied — they were a different quantity.

## The number that actually matters: 0

To see past that gate, a second probe (`scratchpad/probe3x/`) applies the design's own file plan:

* the 20 §5.3 + 1 §5.2 host-only **excludes** are removed from the compile;
* the three §3.11 stand-ins (`NowWorldGraphic`, `NowRectTransformProjection`, `NowLottieBurstTessellator`) are supplied
  verbatim from the §3.11 table — probe-locally, since their real home `Assets/NowUI/Runtime/Standalone/` is gated;
* the four §5.2 **split/guard** files (`NowLottieAsset`, `NowLottieCache`, `NowFilePicker`, `NowRemoteContent`) are
  transformed in a scratch copy per the design's stated regions, so their core halves stay in the compile.

Result: **0 errors, 115 core Runtime files, full method-body binding.** `Now.cs` (4458 lines), `NowMesh.cs`,
`NowFont.cs`, `NowFontCompiler.cs`, `NowGUI.cs`, `NowIMGUIInputProvider.cs`, `NowManagedFontSession/Baker`,
`NowFilePicker.cs` (3780 lines after the split) and every control file bind clean against the shim — not just their
declarations. The §5.4 "compile-fix watch list" is empty: no missing member, no wrong overload, no bad conversion.

The M1 compile gate is met on the shim side. What remains is entirely inside `Assets/`, and entirely in the gated file
plan: perform the six planned regions of §5.2, write the three §3.11 stand-ins, and apply the §5.3 exclude list to the
real standalone csproj.

## One correction the file plan needs

Emulating the splits turned up call sites §5.2 does not enumerate, in exactly one file:

**`NowFilePicker.cs`** — the §5.2 row moves `ThumbnailEntry.request/operation` (89-90), `StartThumbnailRequest`
(2184-2255) and `CompleteThumbnailRequest` (2378-2436) to the `.Unity.cs` half, and names no extractions. But two further
methods touch `entry.request` / `entry.operation` and stay in core, so the split as written does not compile:

* `TrimThumbnailCache` — `oldest.request?.Abort(); oldest.request?.Dispose();`
* `CancelThumbnailRequests` — `thumbnail.request?.Abort(); …Dispose(); thumbnail.request = null; thumbnail.operation = null;`

Both need the same treatment §5.2 already prescribes for `NowLottieCache`: one extracted
`AbortThumbnailRequest(ThumbnailEntry)` that each half implements. Note this is a gap in the `NowFilePicker` row only —
`NowLottieCache`'s row is complete, since its "lines 136-151 and 344-345" already covers both the `Reset()` and the
`RemoveEntry` abort sites.

## Ordering note

Item 5 of the original implications list ("re-run after each shim unit lands; once the count stops falling faster than
the units land, the remaining errors are member-level") never triggered, because the declaration gate means member-level
errors cannot appear while any type is unresolved. The equivalent signal is the probe3x number, and it is already 0.
