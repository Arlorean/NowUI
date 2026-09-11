# Optional browser target

The browser target deploys an ordinary C# `INowScene` through a WebGL2 host. The
native CLI remains the default for scene authoring, interactive previews, reload,
image capture and animation recording. Consumer commands and asset selection are
documented in [Browser Deployment](../../Assets/NowUI/Documentation~/BrowserDeployment.md).

## Assembly and deployment boundary

[BrowserRunner](../../Standalone/NowUI.Cli/BrowserRunner.cs) builds the scene's
normal .NET 9 project, locates its `INowScene`, and collects its portable managed
dependencies. It then generates a small `Microsoft.NET.Sdk` executable targeting
`net9.0-browser` with `RuntimeIdentifier=browser-wasm`. This executable references
the already compiled scene DLL and the same `NowUI.Engine`, `NowUI.Runtime` and
`NowUI.Hosting` assemblies used by native previews. There is no second scene API,
JavaScript authoring layer or browser-specific rebuild of the shared runtime.

`NowUI.Browser` is a class library. Its entry template starts the browser host,
loads and registers the configured assemblies, initializes NowUI, and constructs
the selected scene. JavaScript owns browser events, the animation frame callback
and WebGL interop. The C# frame entry point owns ordinary runtime frame setup,
`Now.StartUI`, `INowScene.Draw`, submission and disposal.

The generated executable preloads supported project assets into a virtual
filesystem. The host reads them through `NowFileResources` and `NowProjectAssets`,
including the ordinary Unity asset parsers. JavaScript does not reinterpret
themes, animations or UI declarations. Browser HTTP, filesystem and input
boundaries are host capabilities; they do not change the scene interface.

The SDK's application-bundle pipeline is significant: the generated project uses
the normal SDK plus `browser-wasm`, with `WasmFilesToIncludeInFileSystem`,
`WasmMainJSPath`, `WasmMainHTMLPath` and `WasmAppDir`. Changing SDKs is not merely a
project-file cleanup; verify that the resulting bundle still contains the virtual
files and expected `_framework/dotnet.js` entry point.

## Prepared browser kit

[Build-NowUIBrowserKit.ps1](../../Tools/Build-NowUIBrowserKit.ps1) builds the browser
class library and stages `Assets/NowUI/Native~/browser`. The native CLI packages
this directory as `BrowserKit` content without referencing the browser project.
Ordinary native builds therefore do not require the WebAssembly workload. An
explicit web publish requires the .NET 9 SDK and its `wasm-tools` workload.

The kit contains the browser DLL, embedded stock shaders, JavaScript/HTML files,
entry template, native MSBuild targets, licenses, provenance and a manifest of
file hashes. Its four prepared native objects are:

| Object | Purpose |
| --- | --- |
| `nowui-msdf.o` | Existing font compiler plugin, including FreeType and MSDF implementations |
| `nowui-vg.o` | Existing native Lottie tessellation and geometry-copy implementation |
| `harfbuzz.o` | Complete HarfBuzz implementation required by the font plugin's shaping exports |
| `nowui-browser.o` | Compact native-call ABI forwarding to the unchanged font and vector functions |

[Browser.Native.targets](../../Standalone/NowUI.Browser/Browser.Native.targets)
stages these objects into the generated executable's intermediate directory.
`NowUIBrowserNativeRoot` and the four individual source properties are overridable.
Installed users receive prepared objects and do not need the upstream native
source trees. The Unity `.bc` inputs are relocatable WebAssembly objects and are
staged as `.o` so Emscripten links them as objects.

Maintainer commands, from the repository root:

```powershell
./Tools/Standalone/Build-BrowserHarfBuzz.ps1
./Tools/Standalone/Build-BrowserBridge.ps1 -SmokeTest
./Tools/Build-NowUIBrowserKit.ps1
./Tools/Build-NowUINativeBundle.ps1
```

The first command requires the upstream HarfBuzz checkout under `Native/harfbuzz`
or an explicit source path. The two native-object builders use the installed .NET
Emscripten 3.1.56 toolchain on Windows. Regenerating those objects is separate from
assembling a kit with existing prepared objects. Rebuild the kit after changing
browser source, shaders, interop files, entry templates, notices or native objects.

## Compact native-call ABI

The .NET 9 WebAssembly interpreter supports at most 12 integer/pointer arguments
and 12 floating-point arguments in its native-call trampoline. Exceeding the
integer limit terminates the runtime in `build_args_from_sig`; it is not a
catchable missing-plugin error. The initial Motion Room browser validation
encountered this failure during the first draw.

The shared runtime now selects a browser wrapper at runtime with
`OperatingSystem.IsBrowser()`. Each wrapper pins the original buffers, puts its
arguments in a sequential stack struct, and passes one pointer to
`nowui-browser.o`. C++ forwards those values to the original plugin function.
Desktop and Unity continue using the original native ABI. Native tessellation,
native geometry copying and HarfBuzz remain enabled; the fix does not force a
managed rendering fallback or change the default TrueType glyph rasterizer.

| Function | Integer/pointer arguments in original ABI |
| --- | ---: |
| `nowui_vg_blit_mesh` | 21 |
| `nowui_vg_blit_text_run` | 21 |
| `nowui_vg_pack_canvas` | 13 |
| `nowui_vg_pack_render` | 12 |
| `nowui_vg_stroke` | 13 |
| `nowui_compile_font_from_memory_with_codepoints` | 13 |

Render packing is wrapped alongside canvas packing even though it fits the
current limit. The other font, shaping and vector entry points fit the limit.
Keep new native APIs within that limit or add an explicit compact bridge.

The bridge includes wasm32 compile-time assertions for every struct field offset
and total size. [BrowserNativeAbiTests](../../Standalone/NowUI.Native.Tests/BrowserNativeAbiTests.cs)
checks the managed sequential layouts and single-pointer declarations. The
`-SmokeTest` command links the actual shipped WebAssembly plugin objects and runs
them under Node's WebAssembly engine. It compares all six wrappers with direct
calls, requiring exact geometry streams, text metrics, packed vertices, trimmed
stroke tessellation, font-atlas pixels and glyph metrics.

The focused managed validation command is:

```powershell
dotnet test Standalone/NowUI.Native.Tests/NowUI.Native.Tests.csproj -c Debug --filter 'FullyQualifiedName~BrowserNativeAbiTests|FullyQualifiedName~NativePluginResolverTests|FullyQualifiedName~NativeTextShapingTests'
```

On 2026-09-10, all 16 focused managed tests and all six actual-wasm comparisons
passed. Motion Room subsequently rendered its four real Lottie assets, theme,
text and controls in the browser. These checks exercise the deployment and ABI
paths; they are not exhaustive WebGL coverage of every stock effect.

## Shaders and rendering limits

The browser library embeds the stock shader files directly from
`Standalone/NowUI.Desktop/Shaders`. C# expands their shared includes and registers
the sources with WebGL before creating programs. Shader fixes therefore have one
source location; do not add duplicate shader strings to `nowui-gl.js`.

The implemented stock paths include text, rectangles, gradients, ripples, glass
and its blur passes, color picking, Bezier geometry, SDF scenes and image-field
baking. Shared implementation is broader than the browser scenarios validated so
far. A native GPU test passing does not establish browser/driver parity.

The browser backend currently accepts Gamma color space and explicitly rejects
Linear. It reports no texture arrays or instancing and uses one sample per pixel.
Texture2D uploads currently accept RGBA32. Float/half render-target support is
separate and depends on the WebGL capability probe; do not infer general float
Texture2D upload support from it. Arbitrary Unity shaders, Unity components and
Unity render-pipeline integrations remain outside this host.

## Trimming and reflection

The generated executable enables trimming. The publisher generates a direct
factory for the selected scene, with an assembly alias to disambiguate identical
type names across dependencies. Required-member or obsolete-constructor cases
use a typed constructor accessor; types that C# cannot reference retain the
original reflective construction. The scene contract remains unchanged.

`BrowserSceneTrimming` removes the selected scene assembly's whole-assembly root
only for simple static applications. It retains the previous root for dynamic
discovery, inspectors, serializers, custom asset subclasses/reset hooks, enums,
interop and additional dependencies outside the audited host. Other application
and plugin assemblies remain rooted. This lets an eligible project containing
several scenes publish only the code reachable from the selected scene.

The four known NowUI
host assemblies can trim unused methods. `BrowserReflectionRoots` reads the exact
referenced Engine/Runtime DLL metadata and emits an
[ILLink descriptor](https://github.com/dotnet/runtime/blob/main/docs/tools/illink/data-formats.md#descriptor-format)
for their reflection contracts. It does not load the assemblies or execute their
initializers while building the descriptor.

Built-in font fixtures use five typed field accessors for font-family slots and
fallbacks. These declare the exact private field dependencies using .NET's
[UnsafeAccessor](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.unsafeaccessorattribute?view=net-9.0).
The generic Unity asset reader and Unity-style lifecycle/reset dispatch still
use their existing narrow reflection contracts. Replacing the latter with
generated registries was measured and rejected: it added maintenance for
negligible download savings. The experiment is recorded in
[Browser Reflection Size](BrowserReflectionSize.md).

The descriptor keeps every core runtime reset hook, known ScriptableObject asset
constructors and inherited lifecycle methods, and the recursive serialized field
graph, including font-family slots, fallback arrays, font atlas structs, theme
data, Engine colors/vectors and enum names. Currently it names 70 types, 46 methods
and 328 fields. Reset hooks are unconditional to retain the previous lifecycle
behavior. A regression compares the descriptor with the actual Hosting asset
whitelist so adding an asset kind requires its schema to remain covered.

`BrowserTrimming` permits unused code removal from `AssetsTools.NET`, `YamlDotNet`
and `StbImageSharp` only when their use is exclusive to the audited hosting code.
The host uses statically reachable binary asset/image readers and YAML
representation nodes. If an application or extension references one of those
libraries, its whole-assembly root is restored because consumers may use its
reflection-based APIs. All other dependencies remain rooted by default.
Consumer calls to dynamic type discovery, assembly loading, or reflective
invocation conservatively restore the infrastructure and NowUI roots as well. This
covers configured type names that leave no direct assembly reference in the
consumer DLL. Known shipped host discovery code is exempt from that fallback;
direct library references outside the asset hosting code still retain roots.
Core roots also return for consumer `NowInspector` calls, member inspection,
component type descriptors and supported reflection-based serializer API calls
(JSON, XML/data contracts or YAML). The scan is conservative, including member
references in unused consumer methods. The browser's own fixed alias map now uses
`JsonDocument`, avoiding serializer metadata for that internal string dictionary.

Browser JS exports have preservation emitted by the .NET interop generator's
module initializer. Embedded stock shader resources remain present and rendering
continues using the shared shader sources. JSON reflection remains enabled for
consumer code. The framework is still trimmed; these rules do not make arbitrary
reflection into framework types or every external library safe. See
[Browser Code Size](BrowserCodeSize.md) for measured effects and validation.

Before narrowing these infrastructure roots, an expanded linker audit on
2026-09-10 produced 54 warnings: 15 from NowUI's scene
construction, initialization discovery, lifecycle dispatch, asset fields and
constructors, inspector and alias JSON; 39 from YamlDotNet reflection,
TypeConverter and F# serialization helpers. Rooting YamlDotNet retains public
serializer paths beyond the representation-model parsing used by NowUI assets.
Those retained paths can generate warnings even when the current scene does not
call them.

Keep linker warnings visible. The whole-assembly roots address the known
application-member preservation requirement; they do not justify a global
`NoWarn`, `SuppressTrimAnalysisWarnings`, or an assertion that every consumer
library supports trimming. Scenes that introduce dynamic framework reflection
or other libraries need their own preservation and runtime validation. Narrow
known schemas can use `JsonDocument` or generated JSON metadata when appropriate.

To expand per-assembly warnings while diagnosing a generated project, set
`TrimmerSingleWarn=false`. Preserve the concrete generated project and full build
log. Use a separate project/output for an audit: SDK build targets can recreate
`WasmAppDir` even when the requested target sounds limited to managed linking.
Do not run competing builds against a live preview bundle.

## Performance interpretation

The default browser target runs shared C# through the WebAssembly interpreter
and its runtime optimizations. `--aot` requests ahead-of-time compilation with a
longer publish step. The font/vector objects are already native WebAssembly in
either mode. AOT performance, payload size and startup must be measured on the
actual scene; the flag alone is not a performance result.

The browser backend copies compatible raw interleaved or separate vertex streams
into a reused upload payload and passes the buffers through synchronous memory
views. It avoids per-float decoding and rebuilding the common mesh layout.
WebGL calls, C#/JavaScript transitions, shader compilation, font baking, asset
preload and browser scheduling still have costs. The browser can render the same
scene while having materially different startup and frame time from native.

`window.__nowuiDiagnostics` records the first rendered-frame startup timestamp
and up to 600 synchronous frame-call timings after 120 warmup calls. These
timings include the C# frame and synchronous submission work; they exclude RAF
waiting, presentation, GPU completion and compositor work. Trusted input
gestures can trigger extra frame calls, so leave the scene idle except for its
animation when comparing runs. The startup timestamp is navigation-relative and
includes download/preload/initialization; it is not a standalone CPU measurement.

For comparisons, hold viewport, `?dpr=1`, asset bytes, scene state, browser and
device constant, separate cold/cache-warm startup, and report median and p95 with
the runtime mode. Use `?time=<seconds>&dpr=1` for untimed deterministic pixel
validation, not frame-performance measurement: it renders the preceding fixed
steps synchronously and captures the canvas. Do not include screenshot encoding
or readback in frame timing. No browser GPU timing or managed-allocation parity
claim follows from the JavaScript frame samples.

[NativePerformance](NativePerformance.md) contains the reproducible native and
Unity results. Its numbers do not describe this browser target. Keep browser
results separate and do not label WebAssembly execution as equivalent to
Unity/Burst or to the native CLI.

### Observed Motion Room run, 2026-09-10

A quiet headless Chromium run of the non-AOT Motion Room application collected
600 warmed frame-call samples: median **5.5 ms**, p95 **7.1 ms**. The first-frame
navigation-relative startup timestamp was **944 ms**. Raw diagnostics are in
`artifacts/local/browser-ready/playground-smoke/browser.json`.

This run used software browser graphics and preserved the drawing buffer for
capture. It is a useful functional/performance observation of this configuration,
not a hardware WebGL benchmark, measured native FPS, total frame latency, or a
cold-download startup guarantee. The CPU sample window did not wait for GPU
completion. Browser graphics mode, caching and capture-buffer preservation can
change these results; repeat measurements with the intended deployment settings.

An untimed browser/native comparison used the same Motion Room scene and assets
at 1120 × 780, DPR 1, and animation time 0.5 seconds. Its mean absolute RGBA
difference was **0.02837 levels out of 255**. Of 873,600 pixels, 21,211 differed
in any channel; 6,577 differed by more than two levels in at least one channel,
and 22 by more than 16. The images visually matched in their text, Lottie assets
and controls, but were not byte-identical across hosts.

The paired images are
`artifacts/local/browser-ready/playground-smoke/browser-reference.png` and
`artifacts/local/browser-ready/native-reference.png`. These are scene-specific
validation artifacts, not proof of universal renderer parity.

The final installed CLI also published a fresh scaffold with `--aot` outside the
checkout. All 41 managed assemblies compiled ahead of time; Chromium startup,
two persistent button clicks, resize and error checks passed. Its output was
31.72 MB before compression and 10.16 MB with Brotli, versus 15.62 MB and 5.94 MB
for that scaffold without AOT. These are scaffold payload comparisons, not
Motion Room timing comparisons. Those historical figures counted only
compression-eligible files, not the complete site or actual browser requests.
Current complete totals and cold downloads are in [Deliverable Sizes](DeliverableSizes.md).
The build log and captures are under
`artifacts/local/browser-ci-aot-publish.log` and
`artifacts/local/browser-ci-smoke-aot/`. The optional GitHub workflow was added
and syntax checked; the equivalent checks ran locally, not on GitHub Actions.
