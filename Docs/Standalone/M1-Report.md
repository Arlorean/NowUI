# M1 — engine-free NowUI core: status report

Historical milestone report. The engine-free core remains the native host's
foundation; the M2/M3 standalone browser host and JavaScript API described below
were retired. The current optional browser target uses shared C# scenes;
current usage is in [Native CLI](NativeCLI.md).

Everything below was measured on 2026-09-07, not estimated. Commands and counts
record that checkout and may differ from the current build.

## Result: all four exit criteria met

## What M1 set out to prove

That the same C# sources Unity compiles can also compile and run with plain `dotnet`, against a UnityEngine-compatible shim,
**without changing NowUI's behaviour in Unity at all**. That is the foundation for the browser build: a WebGL2 backend behind the
shim (M2) and a JavaScript mirror of the builder API over a per-frame command buffer (M3).

## Exit criteria

| # | Criterion | Status | Evidence |
|---|---|---|---|
| D8(a) | The standalone solution builds | **Met** | `dotnet build Standalone/NowUI.Standalone.sln` gives 0 errors and 0 warnings across the shim, the runtime, all seven extensions and both test projects. |
| D8(b) | NowUI's own tests pass standalone | **Met** | 801 passed, 9 skipped, 0 failed, from NowUI's own Unity-authored tests compiled unmodified by reference. The shim's own suite passes a further 1661. |
| D8(c) | Unity results unchanged | **Met** | EditMode 1860 cases and PlayMode 163 cases compare identical to the pre-change baseline, case by case, via `Tools/Standalone/Compare-TestResults.ps1`. |
| D8(d) | The Unity diff is reviewable and the public API is unchanged | **Met** | The public API dump is byte-identical at 4296 lines. The diff under `Assets` is 6 modified files, 46 inserted lines against 647 deleted, where the deletions are verbatim moves into new host-only files. |

## What the standalone build contains

| Project | Contents | Result |
|---|---|---|
| `NowUI.Engine` | The shim: value types, object model, host services, graphics resource handles, gradient and curve, collections stand-ins, the render backend contract, the immediate and recorded draw paths | 84 files, ~24,700 lines, 1661 tests passing |
| `NowUI.Runtime` | `Assets/NowUI/Runtime` compiled engine-free, host-only files excluded by an explicit list | 0 errors, 0 warnings |
| `Extensions/*` | Markdown, Markup, Markdown.Markup, CodeEditor, Docking, NodeGraph, Sdf | 0 errors each |

The NowUI Roslyn analyzer loads and fires under the .NET 9 SDK: a deliberately discarded builder produces
`warning NOWUI001: 'NowRectangle' renders nothing until it is consumed`, exactly as in Unity.

## The change inside `Assets`

Six existing files changed, none of them by more than a guard or a keyword:

| File | Change |
|---|---|
| `NowFontCompiler.cs` | 2 lines: the file-local `#define NOWUI_MSDF_NATIVE` becomes conditional |
| `NowRemoteContent.cs` | 4 lines: two `#if` regions around the download handler and its `using` |
| `NowLottieAsset.cs` | `partial`, plus its network members moved out verbatim |
| `NowLottieCache.cs` | `partial`, plus extracted load and abort seams |
| `NowMarkdownImages.cs` | `partial`, plus an extracted `FinishDownload` shared by both transports |
| `NowFilePicker.cs` | `partial`, plus an extracted `AbortThumbnailRequest` |

Eleven new files were added, every one wrapped entirely in `#if NOWUI_STANDALONE` or `#if !NOWUI_STANDALONE`, so Unity's compiled
output is unchanged. The `.Unity.cs` halves hold the moved host code; the `.Standalone.cs` halves reimplement it over the shim's
interfaces; three stand-ins under `Runtime/Standalone/` satisfy the nine call sites that reference host-only types.

One detail worth preserving: in `NowFilePicker.cs` a balanced `#if`/`#endif` pair replaces the two lines that moved out, so every
subsequent `[CallerLineNumber]` keeps its former value. NowUI derives control identity from caller line numbers, so a shifted
line is not a cosmetic difference there.

## How to reproduce

```bash
dotnet build Standalone/NowUI.Standalone.sln
dotnet test Standalone/NowUI.Engine.Tests/NowUI.Engine.Tests.csproj
pwsh -File Tools/NowUI-Harness.ps1 -Mode EditMode -ArtifactsPath artifacts/local/check
pwsh -File Tools/Standalone/Compare-TestResults.ps1 -Baseline artifacts/local/standalone-baseline-current/EditMode/NowUI-EditMode-results.xml -Current artifacts/local/check/EditMode/NowUI-EditMode-results.xml
```

The comparison script exits non-zero on any difference and compares test cases as sets rather than counts, so a regression
cannot hide behind an offsetting fix elsewhere.

## About the 9 skips

They are the text-shaping tests, and they skip themselves. M1 compiles the native plugins out, so the HarfBuzz shaper reports
itself unavailable and those fixtures call `Assert.Ignore` on their own initiative. Nothing was excluded to reach a green number:
the tests were compiled unmodified and decided for themselves. Linking the shaper is an M2 item, at which point they run.

## Known state, honestly

- The one pre-existing EditMode failure has since been fixed, separately from this work.
  `NowHarnessAnimationTests` hard-coded the roster of six README showcases while the harness declares eight, so adding a README
  loop broke it. The test now asserts the invariant that matters, that every showcase shares one capture format, and derives
  everything else, so the roster can grow freely. Verified by mutation: changing one scenario's frame rate makes it fail and
  names that scenario. EditMode is now 1858 passed, 0 failed, 2 skipped.
- Because that renamed one test, the regression oracle moved. Compare against
  `artifacts/local/standalone-baseline-current` from now on. `standalone-baseline-main` is retained as the pre-change record
  that proved criterion D8(c), and differs from current only by that rename.
- The shim's `NativeArray`, jobs and Burst stand-ins are sequential and synchronous. That is correct for a single-threaded
  WebAssembly target and is not a temporary shortcut, but it does mean the Burst tessellator's own test cannot run standalone.
- Native plugins are compiled out in M1, so text shaping and glyph compilation take their managed fallbacks.

## What M1 deliberately did not do

No WebGL2 backend, no JavaScript mirror, no wasm packaging. The shim's backend interface exists and a null backend implements it,
which is what makes those the next milestone rather than a rewrite.
