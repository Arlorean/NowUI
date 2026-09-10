# Native rendering measurements

The native host now submits existing mesh buffers directly and uses the bundled native vector renderer by default. On the measured machine, this reduced warmed main-thread frame cost substantially for the controls and image SDF workloads, and approximately halved the four-animation workload. The original sample drawing code and output pixels were preserved.

These are representative host measurements, not a claim that CoreCLR/OpenGL has the same execution model or total CPU cost as Unity/Burst.

## Reproduce

Run from the repository root with other graphics applications, tests and builds idle:

```powershell
./Tools/Standalone/Measure-NativeRendering.ps1 -OutputDirectory artifacts/local/native-performance/native -Runs 3
./Tools/Standalone/Measure-NativeRendering.ps1 -OutputDirectory artifacts/local/native-performance/managed -ManagedVg -Runs 3
./Tools/Standalone/Measure-UnityRendering.ps1 -Output artifacts/local/native-performance/unity/report.json
```

The native wrapper publishes a frozen Release runtime before measuring fresh processes. Each run writes its raw samples, summaries, separately measured process startup, and untimed verification PNGs. `-SkipBuild` reuses an existing output directory's `runtime` snapshot. `-Warmup` and `-Samples` default to 240 and 600.

`-ManagedVg` disables native tessellation and bulk geometry/text copy through the existing profiling switches. Both variants otherwise use the same C# API, shader paths, viewport and asset bytes. For a build with the bindings compiled out entirely, use `-p:NowUIUseNativeVg=false`; normal builds include them. Native availability and the selected tessellation path are recorded in each report.

The Unity wrapper requires an idle Gamma project. It stages one uniquely named temporary test assembly containing the unchanged benchmark and UI sources, runs a graphics-enabled PlayMode test, and removes only that staging folder. It does not change project settings. The Unity editor path can be passed with `-Unity`.

## Workloads and timing scope

The shared source is [BenchmarkContent.cs](../../Standalone/Benchmarks/NativeRendering/BenchmarkContent.cs). It draws at 1100 × 760, UI scale 1, Gamma, with one sample per pixel and no VSync.

| Workload | Content | Final-frame geometry |
|---|---|---:|
| Controls/text | The actual [InteractiveContent](../../Standalone/Samples/NativePreview/InteractiveContent.cs) Fieldnotes sample with controls and shaped text; its optional animation is paused | 1,624 vertices, 30 batches |
| Image SDF | Eight animated image-to-circle morphs with outline and shadow, using a cached GPU-generated image distance field | 36 vertices, 9 batches |
| Animated Lottie | Four bundled emoji animations (`1f600`, `1f602`, `2764`, `u1f63b`), each 320 × 320 | 24,168 vertices, 1 batch with native VG |

Each scenario constructs its assets outside timing, records its first frame separately, replays 240 warmup frames, and measures 600 frames. Animation time is explicitly `frame / 60`, so both hosts render the same logical sequence even when their actual frame rates differ. Warmup is a fixed frame count, not a minimum wall-clock interval; process scheduling, JIT tiering and GPU clock changes can still affect the results. The reported ranges retain the observed variation between runs.

The native CPU window includes frame setup, clear, `Now.StartUI`, shared C# drawing, scope disposal and runtime frame completion. Separate draw and submission timestamps identify where that main-thread time is spent. Unity measures the corresponding clear, screen UI scope, drawing and immediate submission on its main thread.

Neither CPU window includes presentation, VSync, readback, PNG encoding, file I/O, or a wait for GPU completion. Unity render-thread completion and other player-loop work are also excluded. Native submits frames continuously; Unity yields through the player loop between submissions. This changes queue pressure and prevents interpreting these numbers as a comparison of total engine CPU or end-to-end latency.

Native OpenGL elapsed queries are issued around each measured frame and retrieved only after the complete measured block. Their intervals can include GPU idle while the CPU supplies commands; on this machine those intervals followed CPU submission timing closely. They are retained in the JSON for diagnosis and are **not** presented as pure GPU shader execution time. The Unity runner does not report GPU timing.

Managed allocation phase probes run separately after the timed block. Geometry counts are an additional untimed replay through the shared `NowRenderer` capture API. Pixel readback and all validation captures also occur outside the measurements.

## Results, 2026-09-10

Machine: AMD Ryzen 9 7900X, NVIDIA RTX 4080 SUPER, Windows build 26200. Native: .NET 9.0.0 Release, OpenGL 3.3, NVIDIA driver 595.79. Unity: 6000.4.0f1 Editor/Mono, Direct3D 11, native tessellation enabled. No concurrent test suite or other benchmark was launched during measurement.

The baseline was published before changing the desktop backend, with native VG disabled. Two baseline processes and three final native processes were measured. Each cell below is the range of per-run medians or p95 values, in milliseconds.

| Workload | Original median | Final median | Original p95 | Final p95 |
|---|---:|---:|---:|---:|
| Controls/text | 3.75–4.60 | 0.71–1.03 | 5.51–7.34 | 1.10–1.72 |
| Image SDF | 1.60–2.57 | 0.15–0.22 | 2.92–7.62 | 0.26–0.46 |
| Animated Lottie | 6.45–7.37 | 3.10–3.20 | 8.40–10.17 | 4.09–4.28 |

The changes were measured in stages:

1. Upload compatible interleaved vertex bytes directly, retain and reuse VAO layouts, and make the OpenGL context current only when needed. This brought controls to 0.72–0.93 ms and SDF to 0.14–0.21 ms; Lottie remained 5.94–6.18 ms.
2. Upload separate vertex streams directly instead of rebuilding an interleaved staging buffer. With native VG still disabled, Lottie reached 3.72–4.01 ms.
3. Enable the existing bundled native VG implementation. The final repeated Lottie runs reached 3.10–3.20 ms. At the captured frame it emitted 24,168 vertices versus 41,158 through the managed fallback, with identical rendered pixels.

The second final native run attributed its median time as follows. Phase medians need not sum exactly to the whole-frame median.

| Workload | Draw | Submit | Complete measured main-thread window |
|---|---:|---:|---:|
| Controls/text | 0.142 ms | 0.555 ms | 0.710 ms |
| Image SDF | 0.027 ms | 0.149 ms | 0.182 ms |
| Animated Lottie | 1.359 ms | 1.783 ms | 3.162 ms |

## Allocation attribution

| Workload | Native managed bytes/frame | Draw | Setup/submission |
|---|---:|---:|---:|
| Controls/text | 2,704 | 2,704 | 0 |
| Image SDF | 0 | 0 | 0 |
| Animated Lottie | 0 | 0 | 0 |

The controls sample formats its labels, counters and row strings during each `Draw`. Its allocation was retained unchanged for the comparison. The measurement supports zero managed allocation in the warmed native setup/submission paths of these workloads; it does not claim the entire sample allocates nothing, or measure unmanaged driver memory.

Unity Mono exposed `GC.GetAllocatedBytesForCurrentThread` but failed a real-allocation calibration probe. The final Unity run therefore uses the existing validated `NowBenchmarkAllocations` fallback, which counts allocation calls. Controls produced 55 calls/frame; SDF and Lottie produced zero. Those calls are not converted to estimated bytes. The first exploratory Unity report contains uncalibrated zero byte values and must not be used as allocation evidence.

## Unity comparison

Both staged PlayMode runs passed their rendering assertions. The final run uses the calibrated allocation instrumentation:

| Workload | Unity main-thread median | Unity p95 | Native final median range |
|---|---:|---:|---:|
| Controls/text | 0.385 ms | 0.632 ms | 0.71–1.03 ms |
| Image SDF | 0.155 ms | 0.285 ms | 0.15–0.22 ms |
| Animated Lottie | 2.799 ms | 3.630 ms | 3.10–3.20 ms |

Unity remains faster on the measured controls and Lottie submission windows. Its render-thread work is outside this table, and its driver/runtime/queue behavior differs. Both hosts use native VG in this comparison, so it does not measure a managed-versus-Burst advantage.

Final-frame geometry counts match between hosts. Controls and Lottie captures differ at 45 and 3 pixels respectively by more than two 8-bit channel levels out of 836,000 pixels. Image SDF is not pixel-identical across the APIs: average absolute RGB differences are 0.25/0.57/0.33 channel levels, with 91,091 pixels exceeding two levels in at least one RGBA channel. This host comparison does not assert universal Unity/native rendering parity.

Within the native host, the original and optimized backends produced byte-identical verification PNGs for all three workloads. Native VG enabled versus disabled also produced byte-identical PNGs at animation frames 0, 37, 121, 317, 599 and 839, plus the controls and SDF captures. These cover the measured assets and phases, not every possible animation.

## Startup and local evidence

The three final native processes took **657–747 ms from process start to first UI submission**, including host startup, resource loading, scene setup, first draw and initial shader/resource work. Main-entry-to-host-initialization was 443–520 ms; the first controls draw/submission was 125–138 ms. These are fresh processes with normal OS caches, not disk-cold launches. Build/publish time and GPU completion are excluded. The benchmark constructor prepares resources for all three scenarios, so this is not a minimal application startup target. Unity import/editor startup is not compared with that native process metric.

Local evidence is under `artifacts/local/native-performance/`:

- `baseline-runtime`, `baseline-run1`, `baseline-run2`: frozen original runtime, raw measurements and captures.
- `backend-candidate-run1` through `run3`: interleaved/context optimization.
- `streams-managed-run1`, `streams-managed-run2`, `final-managed-run1`: direct stream upload with native VG disabled.
- `final-native/runtime`, `final-native/run1` through `run3`: final frozen runtime, raw samples, allocation probes, startup reports and animation phase captures.
- `unity-run2`: final calibrated Unity report, successful test XML, log and verification PNGs.
- `unity-pixel-comparison.json`: cross-host pixel difference statistics.

The frozen directories are ignored local artifacts. The benchmark source, scripts and this report are maintained in the repository; they introduce no runtime or package dependency into the shipped CLI.
