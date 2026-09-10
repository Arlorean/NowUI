# Deliverable sizes

This page records the first compression/package pass. The subsequent
[NowUI code trimming pass](BrowserCodeSize.md) reduced Motion Room's cold browser
download further, from 4.56 MB to 4.17 MB.

Measured on Windows, 2026-09-10, with .NET SDK 9.0.101 / browser workload 9.0.19.
MB below means 1,000,000 bytes. Native C# preview remains the default workflow;
the website is the optional `--target web` deployment of the same scene.
The CLI is a framework-dependent .NET tool; its package size excludes the
separately installed .NET runtime/SDK.

## Before and after the first size pass

The website comparison uses the same Motion Room C# scene, 19 project asset files,
11 built-in resource files and 3,978,150 asset bytes. Asset contents, font faces,
glyphs and globalization coverage were retained.

| Deliverable or measurement | Before | After |
| --- | ---: | ---: |
| Universal CLI NuGet download | 13.51 MB | 11.79 MB |
| CLI unpacked payload | 32.38 MB | 29.00 MB |
| CLI installed version, including NuGet archive copies | 59.54 MB | 52.73 MB |
| Motion Room cold browser response bodies, Brotli | 6.48 MB | 4.56 MB |
| Motion Room original site files | 17.56 MB | 16.12 MB |
| Motion Room complete Brotli representation | 7.16 MB | 5.05 MB |
| Motion Room stored originals + Brotli + gzip | 30.74 MB | 27.45 MB |

The full-site rows include unrequested locale variants and notices. The new
`nowui-size.json` excludes itself from its totals; it lists each original file
and encoded sizes, using the original when no smaller variant exists. Stored
bytes include both encodings, which a single browser does not download together.

Cold response-body totals are **6,481,712 -> 4,561,264 bytes**, a **29.6%** reduction.
These came from fresh Chromium contexts at `en-US`, through 30 rendered frames,
served locally with Brotli, including the HTML navigation. Browser-estimated
transfer bytes including headers were **6,506,912 -> 4,583,164**. The browser
loaded the EFIGS ICU partition rather than all three partitions. This is a byte
measurement without throttling, not a WAN startup-time benchmark.

## Implemented reductions

- The CLI package omits five flat native libraries that duplicated the selected
  build platform's `runtimes/<rid>/native` copies. All four supported native RID
  sets remain intact. Source builds retain their app-local copies. This saves
  about **1.72 MB compressed / 3.40 MB unpacked**. The package builder checks that
  RID libraries exist and duplicate flat copies do not.
- Browser builds allow unused-code trimming of the hosting implementation's
  AssetsTools.NET, YamlDotNet and StbImageSharp paths. NowUI and application
  assemblies remain rooted for initialization and Unity asset reflection. Direct
  consumer library references retain their full roots; dynamic type discovery
  or reflective invocation in consumer code restores all three roots. This removed about
  **0.71 MB original / 0.28 MB at the previous Brotli settings** in an isolated
  comparison. See [Browser Target](BrowserTarget.md#trimming-and-reflection) for
  the preservation boundary.
- Native browser symbol maps are now opt-in with `--native-symbols`. The previous
  build downloaded a **712,685-byte** uncompressed function-name map at startup.
  The optional map now receives compression too. This trades native diagnostic
  names for size; scene rendering and ordinary C# behavior are unchanged.
- Static publish uses Brotli quality 11, preview uses quality 6, and both use
  gzip SmallestSize. Each file also tries the former fast encoding and keeps it
  when smaller. Compression alone saved **1.15 MB** of the old complete Brotli
  representation. On that unchanged site the publish compression pass took about
  **33 seconds**, versus roughly **1.6 seconds** for the new preview profile.
  These are build costs; native preview has no browser compression step.

The isolated savings overlap slightly when combined; use the measured final
totals instead of adding estimates.

## Remaining opportunities

| Area | Current cost / opportunity | Tradeoff |
| --- | --- | --- |
| Fonts | Motion Room includes **1.35 MB Brotli** of built-in and project font data. Four built-in faces alone are 2.55 MB original. | Lazy face loading could reduce initial downloads while retaining styles. Subsetting needs an explicit language/glyph contract. |
| Duplicate embedded font | The project's serialized regular font contains the same **629 KB TTF blob** as the built-in regular font. | Whole-file hashes differ because the Unity asset has settings. A shared lossless asset archive or structured blob deduplication needs loader work. |
| Native platform packages | The three non-Windows platform sets account for about **4.68 MB** of a Windows user's universal ZIP download. | Per-platform packages could defer this cost, with more release and installation coordination. The universal package remains useful for portability. |
| Managed/native runtime | Complete Motion Room managed code is **1.58 MB Brotli** and native WASM **1.34 MB**. | Further rooting/feature audits need real scene coverage. Do not trim reflection-dependent scene or Unity asset members blindly. |
| Browser kit inside CLI | About **1.01 MB ZIP**. | Splitting it would add another installation path for a relatively small saving. |
| Globalization | All ICU variants occupy **0.62 MB Brotli** in the site; only the selected partition loads. | Removing partitions mainly reduces deployed storage. Invariant mode changes culture behavior and must be an explicit product choice. |

AOT is a performance choice and generally increases this application's download;
it is not the size-minimizing default. Asset selection already follows known
scene strings and serialized dependencies; `--all-assets` intentionally costs
more for fully computed paths.

Fresh `nowui init` scenes were independently published and tested in both modes:

| Fresh scaffold | Cold Brotli before | Cold Brotli after | Complete Brotli site after |
| --- | ---: | ---: | ---: |
| Default interpreter | 5.99 MB | 4.10 MB | 4.59 MB |
| `--aot` | 9.49 MB | 6.65 MB | 7.14 MB |

These are the same scaffold scene, not the larger Motion Room scene. Both modes
retained identical initial pixels and passed two stateful pointer clicks and
resize. The AOT row does not establish a frame-performance advantage for a
particular application.

## Validation and reproduction

The combined Motion Room build passed startup, canvas rendering, Unity theme
switching, Lottie gallery selection, text editing, F8 capture and resize. Its
fixed-time browser PNG was byte-identical to the previous browser output. The
newly installed native CLI also produced a PNG byte-identical to its native
reference. Native unit/integration tests passed with graphics-gated tests
separate; package installation and native rendering were exercised directly.

```powershell
./Tools/Build-NowUINativeBundle.ps1
./Assets/NowUI/Native~/nowui.ps1 publish Standalone/Samples/NativePreview/NativePreview.csproj --target web --scene NowUI.Samples.NativePreview.PlaygroundScene --unity-project . --output artifacts/local/new-size-check
./Assets/NowUI/Native~/nowui.ps1 serve artifacts/local/new-size-check --port 8080 --no-open
# In a separate terminal:
node Tools/Standalone/BrowserSmoke/validate-site.mjs artifacts/local/new-size-check
node Tools/Standalone/BrowserSmoke/smoke.mjs http://127.0.0.1:8080/ artifacts/local/new-size-smoke --playground
```

Local evidence lives under `artifacts/local/browser-size`: `baseline-smoke`,
`motion-room-final`, `final-smoke`, `trimming/file-sizes.json`, `compression`,
`scaffold/comparison.json`, `package-audit.json` and `final-package-verification.json`.
The final CLI archive SHA256 is
`5a51d84ffcdaf5916db091f05fa400b09e68fb0247bffb64c1954cd8e0ff371f`.
These generated artifacts
are not required in consumer packages.
