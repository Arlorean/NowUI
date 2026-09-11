# Direct Unity project assets

The native CLI reads supported assets directly from the Unity project. No Editor,
export, generated asset copy, or changed serialization setting is required.
`render`, `animate` and `preview` find the enclosing project from the scene's `.csproj`, then
from the working directory. A scene outside the project can specify
`--unity-project C:/path/to/project`.

## Loading and drawing

Existing Unity `Resources` paths work for supported assets:

```csharp
var logo = Resources.Load<Texture2D>("UI/Logo");
var theme = Resources.Load<NowThemeAsset>("UI/DarkTheme");
var font = Resources.Load<NowFontAsset>("NowUI/NotoSans");

Now.Rectangle(rect).SetTexture(logo).Draw();
```

The native provider additionally accepts project paths, so files need not be moved
into a `Resources` directory. Select a named sprite with `#`:

```csharp
var logo = Resources.Load<Texture2D>("Assets/Brand/logo.png");
var panel = Resources.Load<Sprite>("Assets/UI/sprites.png#panel");
var theme = Resources.Load<NowThemeAsset>("Assets/UI/DarkTheme.asset");
var font = Resources.Load<NowFontAsset>("Assets/Fonts/Body.ttf.asset");

Now.Rectangle(rect).SetSprite(panel, sliced: true).Draw();
```

Project paths and `#` selectors are native loader extensions; Unity's own
`Resources.Load` retains its normal Resources-folder rules. Shared drawing code
can receive the resolved objects from either host without changing the draw calls.

The provider also exposes `NowProjectAssets.LoadAsset<T>(path)` and
`LoadAsset(path, type, localFileId)` to custom .NET hosts. The CLI installs it
automatically. It owns loaded objects and releases them when the preview/capture
ends; scenes should not destroy these shared assets.

## Supported files

| Asset | Direct loading |
| --- | --- |
| PNG, JPEG, TGA, BMP | Decode source pixels, preserve alpha and orientation, read common texture import settings. |
| Single and multiple sprites | Read `.meta` rectangles, named slices, pivots, borders, pixels-per-unit and local file identifiers. |
| TTF / OTF files | Compile the original font bytes through `NowFontCompiler`. |
| NowUI font `.asset` | Read serialized settings and embedded source bytes, including the binary font files shipped in this project; read supported embedded baked atlas textures. |
| NowUI font families | Resolve face slots and the full fallback graph through `.meta` GUIDs. |
| NowUI themes | Read serialized tokens, presets and controls, including counterpart themes and built-in control renderer references. |
| Lottie JSON / `.lottie` | Read plain animation JSON and dotLottie archives using the same parser and limits as Unity's `NowLottieImporter`. |
| Serialized `NowLottieAsset` | Read the known NowUI asset type from Unity YAML or supported binary serialization, and parse its saved JSON. |

Known `.asset` types are read from Unity YAML or binary serialization with embedded
type trees. Script GUIDs select a fixed list of supported NowUI types; project
scripts are not executed. GUID plus local file ID identifies referenced subassets.
Cycles such as light/dark theme counterparts retain object identity.

Assets are indexed under `Assets`, embedded/local packages, and available registry
package cache entries matching `packages-lock.json`. Ambiguous Resources names,
duplicate GUIDs, unresolved references and unsupported asset types produce
diagnostics naming the files. Stock NowUI material references use the native
renderer's matching templates; arbitrary Unity shaders are not imported.

Embedded baked atlases support Alpha8, R8, RGB24, RGBA32, ARGB32 and BGRA32 pixel
payloads. Compressed or externally streamed atlas payloads, and binary files with
stripped type trees, report an unsupported-format error.

## Import and refresh behavior

The image loader reads filter/wrap settings, mipmap enablement, alpha source,
color-space metadata, NPOT scaling, maximum size and Standalone platform size
overrides. Resizing uses bilinear sampling and reports when that may differ from
Unity's importer. Unity texture compression, alpha color dilation and custom
importers are not reproduced. The CLI defaults to Gamma; `--color-space linear`
uses the native linear rendering path with sRGB decoding for color textures and
display conversion on output. Linear data textures retain their numeric values.

Loaded assets are cached for one scene instance. `preview` watches saved source
and asset changes by default, then reloads the scene with a fresh asset provider.
A successful reload restarts scene state; a failed compilation keeps the current
preview running. Use `--no-watch` to keep a fixed instance. `render` and `animate`
read the saved assets when launched. The project files and their `.meta` files
are never rewritten by the native loader.

The optional [browser target](../../Assets/NowUI/Documentation~/BrowserDeployment.md)
automatically selects supported assets and their known GUID dependencies from
the compiled C# scene, then preloads original files into .NET's virtual filesystem.
It uses these same parsers. `--all-assets` includes the bounded supported asset
set for paths that are fully computed at runtime.

## Lottie and remote resources

Use your existing project files directly. A `.lottie` file may contain plain JSON
or a dotLottie ZIP archive; the same selection and parser rules apply as in Unity.
Raw `.json` files can also be loaded as `NowLottieAsset` by the native provider.

```csharp
var animation = Resources.Load<NowLottieAsset>("Assets/UI/loading.lottie");
Now.Lottie(rect, animation).SetTime(Time.time).Draw();
```

Resources aliases and GUID references preserve shared object identity. The
known importer main object (`animation`, local file ID `7868935432812623880`)
is supported; other scripted importers are rejected explicitly. Loaded assets
belong to the provider. `.lottie` changes trigger normal preview reload.

The native host also supplies streaming HTTP/HTTPS transport for the existing
Lottie URL cache and Markdown image cache. Application URL policies, per-hop
timeouts, redirect counts and cumulative byte limits remain owned by NowUI's
existing loaders. The transport never follows redirects automatically. Closing
or reloading a preview cancels outstanding transfers. Native remote images use
the same image-decoding library as project textures, including PNG, JPEG, TGA
and BMP; PNG encoding remains available.

Interactive previews load asynchronously. `render` and `animate` wait for remote
resources during warmup without advancing playback time, and settle active
transfers encountered while capturing. `--load-timeout` bounds each settling
wait (30 seconds by default, configurable from 1 to 600); a timeout fails without
publishing an incomplete capture. Local asset loading requires no network.

The [Lottie guide](../../Assets/NowUI/Documentation~/Lottie.md) describes format
support, source limits, URL policy and vector frame caching. Image layers and
unsupported Lottie effects retain the same shared-renderer limitations as Unity.

Render the repository's four original animated emoji assets:

```powershell
./Tools/NowUI-Native.ps1 animate Standalone/Samples/NativePreview/NativePreview.csproj --scene LottiePreviewScene --width 960 --height 380 --duration 2 --fps 20 --output artifacts/local/lottie-frames
```

## Example from this workspace

`AssetPreviewScene` uses the existing Google logo under `Assets/Mockups/Google`,
the project's `DefaultDark` theme and its linked light counterpart, and the binary
JetBrains Mono font asset. This scene requires that logo file to exist.

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene AssetPreviewScene --width 960 --height 610 --time 0.5 --output artifacts/local/project-assets/native-assets.png
```

Replace `render` and its output/time options with `preview` to interact with the
theme switch and button. Tests cover source image pixels, sprite metadata and
native GPU output, the actual repository themes and binary fonts, reference
identity, project discovery, malformed inputs and ownership.

Initial direct-asset validation on Windows, 2026-09-10: all 83 native tests passed with graphics
enabled, along with 10 existing shared-resource tests. The Release CLI built
without warnings. A Unity 6000.4 import probe independently verified sprite
rectangle, border, pivot and pixels-per-unit scaling after maximum-size reduction.

Lottie and remote-resource validation on Windows, 2026-09-10: 14 asset/HTTP tests
and six native graphics tests passed. These cover actual project animations,
archive and serialized loading, redirect policy and byte limits, cancellation,
deterministic downloads and replay, animation frames, and atomic capture timeout.
