# Browser code size

This is the second size pass, following [Deliverable Sizes](DeliverableSizes.md).
The later [reflection experiment](BrowserReflectionSize.md) records direct scene
construction, selected-scene trimming and the generated-registry approaches
that were measured and discarded.
Measurements use the same Motion Room scene and unchanged asset bytes, .NET SDK
9.0.101 / browser workload 9.0.19, and a fresh `en-US` Chromium context with
Brotli. KB and MB are decimal. The CLI remains a native authoring tool by default.

## NowUI attribution and result

The earlier 2.9 MB code/runtime figure combined NowUI, .NET managed libraries,
asset parsers and the linked native runtime. NowUI's own managed assemblies were
only 0.55 MB of the 4.56 MB initial download. They are now **0.319 MB**, a
**41.9% reduction**. Keeping fewer NowUI methods also removes some otherwise
unneeded .NET methods.

| Brotli response bodies | Before | After |
| --- | ---: | ---: |
| NowUI.Runtime | 333,143 B | 162,046 B |
| NowUI.Engine | 97,426 B | 45,255 B |
| NowUI.Browser, including shader resources | 86,870 B | 85,192 B |
| NowUI.Hosting | 30,513 B | 25,037 B |
| Generated NowUI.WebApp | 1,930 B | 1,858 B |
| **NowUI managed subtotal** | **549,882 B** | **319,388 B** |
| .NET/BCL managed libraries | 940,716 B | 785,175 B |
| **Entire initial Motion Room download** | **4,561,264 B** | **4,173,091 B** |

The full initial download fell **8.5%** in this pass. Browser-estimated transfer
including headers fell from 4,583,164 to 4,193,791 bytes. This is a byte comparison
without network throttling, not an internet startup-time benchmark. The complete
site is 14,893,543 original bytes / 4,661,902 Brotli-selected bytes. Stored originals
plus both encodings total 25,371,302 bytes, excluding `nowui-size.json` itself.

Fresh apps created with the installed CLI outside the checkout also improved:

| Fresh scaffold, Brotli | Previous size pass | This pass |
| --- | ---: | ---: |
| NowUI managed assemblies | 550,007 B | 274,100 B |
| Normal initial download | 4,104,540 B | 3,670,707 B |
| AOT initial download | 6,652,019 B | 5,553,753 B |

The smaller scaffold removes **50.2%** of its previous NowUI managed download.
Normal and AOT pages both retained identical initial pixels and passed stateful
button input and resize. These are size/behavior checks, not comparative AOT
frame-performance measurements.

## Implementation

The publisher generates narrow roots for known asset serialization and lifecycle
reflection rather than preserving every member in all NowUI assemblies. Ordinary
static scene calls keep their required implementations. Scene and unknown plugin
assemblies remain rooted; consumer reflection, inspector and serializer use
restore conservative whole-library preservation. The precise boundary is in
[Browser Target](BrowserTarget.md#trimming-and-reflection).

The browser asset-alias map now uses the existing `JsonDocument` reader instead
of reflection-based generic deserialization. This fixed string map did not need
serializer metadata. Case-sensitive keys, escaped paths and last-duplicate-value
behavior are retained; invalid shapes fail explicitly.

`nowui-size.json` now separates NowUI, .NET and third-party managed assemblies,
application code, and the combined native runtime/plugins. Category totals make
the attribution visible without having to sum individual files.

## Remaining floor

Fonts still contribute **1.353 MB**, and the combined native runtime contributes
**1.340 MB** to the initial download. That native file includes Mono/.NET, ICU,
HarfBuzz, FreeType/MSDF and vector rendering; it is not all NowUI code.

A function-body audit of the preceding native file found 1,846,826 uncompressed
bytes attributable to ICU plus Mono/interop, 1,111,515 to HarfBuzz plus the font
plugin, and 39,083 to NowUI vector rendering plus its browser adapter. Its data
section and remaining platform functions are separate. Raw function sizes cannot
be divided proportionally to assign compressed bytes because compression shares
data across functions. The detailed audit is in
`artifacts/local/browser-code-size/attribution.json`.

Further NowUI code removal has diminishing returns against those font and native
runtime costs. This pass preserves font faces, glyphs, culture coverage, Lottie
and supported asset formats.

## Validation

The installed CLI published Motion Room successfully. Static validation checked
all encoded sidecars round-trip to their originals. Chromium passed startup,
rendering, text editing, theme changes, Lottie selection, key binding and resize.
Its fixed-time PNG is byte-identical to the prior browser baseline
(`2497f50c63f15e7afbe77b2391219fd70c1c6eadc91f8f5d141dbbfe180ad3f4`).

Focused tests cover descriptor generation, private/nested asset fields, enum names,
default constructors, lifecycle hooks, whitelist coverage, dynamic-reflection
fallback, inspector and serializer calls, and alias map parsing. Source changes
are confined to browser publishing/hosting and test access; shared Unity draw
and input implementations are unchanged.

The installed site, cold network report, images, probe comparison and package
checks are under `artifacts/local/browser-code-size`. The prepared browser kit
and native CLI bundle were rebuilt together.
