# Browser deployment of C# scenes

The optional web target runs the same .NET 9 `INowScene` and ordinary
`Now` / `NowLayout` calls used by the native CLI. Keep scene code in C#.
Native preview remains the default for authoring, live reload, screenshots
and animation recording.

## Publish or preview

Use the package launcher from the Unity project directory:

```powershell
pwsh -File "<package-root>/Native~/nowui.ps1" publish NowUI/apps/Demo/Preview.csproj --target web --output NowUI/sites/Demo
pwsh -File "<package-root>/Native~/nowui.ps1" preview NowUI/apps/Demo/Preview.csproj --target web
pwsh -File "<package-root>/Native~/nowui.ps1" serve NowUI/sites/Demo
```

In a source checkout, `Tools/NowUI-Native.ps1` accepts the same arguments.
The scene remains a normal .NET 9 project; the CLI creates the browser host
and builds its WebAssembly application. The .NET 9 SDK and its `wasm-tools`
workload are required for web builds. Native commands do not require the
WebAssembly workload.

`publish` writes a new output directory only after the build succeeds. Deploy
that directory to a static HTTP/HTTPS host. `preview --target web` builds the
same output, serves it locally and opens the browser. It chooses a free port
by default (`--port 0`); select a port with `--port 8080` or suppress opening
the browser with `--no-open`. Browser preview currently has no source or
asset hot reload: run the command again after editing.

`serve <site-directory>` opens an already published site without rebuilding.
It accepts the same `--port` and `--no-open` options.
Keep the server process running while the user uses the page. Open the emitted
URL and verify startup and the requested interactions before reporting a working
browser preview. Publishing creates local files; deploying them to a public host
is a separate action within the user's requested scope.

## Download size and compilation

The generated browser host is always a trimmed Release build. `--configuration`
selects the scene assembly's build configuration. Start without `--aot` when
download size matters. AOT can improve execution speed for some workloads, but
adds build time and can substantially increase the download; benchmark the
actual scene before choosing it.

For scale, the .NET 9 starter has measured about 3.7 MB of Brotli-compressed
initial response bodies, and about 5.5 MB with AOT. These are example measurements
with the bundled fonts, not fixed sizes or a size guarantee for other scenes.

The output includes smaller Brotli/GZip variants of compressible runtime and
asset files. The local preview server negotiates these automatically. A deployed
static host can serve the variants with their matching `Content-Encoding`;
the original files remain available for hosts that do not use precompression.

Publish uses Brotli quality 11; browser preview uses quality 6 to shorten
iteration. Both also generate gzip, keeping the smaller result if the previous
fast encoding wins for an individual file. Native symbol maps are omitted by
default; use `--native-symbols` when diagnosing native browser stack traces.
Font faces, glyphs, globalization data and supported asset formats are retained.
The publisher also trims unused NowUI methods while retaining the metadata needed
to load supported assets and run lifecycle hooks. Consumer reflection, inspectors
and serializer use trigger conservative preservation automatically; no scene API
changes or manual asset-export step are needed.
For simple static applications, publishing a selected scene also strips unused
code from the scene assembly, including other scenes in the same project.
Applications with dynamic behavior or additional dependencies retain conservative
preservation. `nowui-build.json` records whether scene-assembly trimming applied.

Every build writes `nowui-size.json`, listing original files from largest to
smallest, with their Brotli and gzip sizes. The totals include uncompressed files,
all locale variants and notices; stored bytes also include both encoding copies.
The report excludes itself. A cold browser load requests only a subset of this
site, so measure network transfers separately when budgeting initial downloads.

| Option | Use |
| --- | --- |
| `--scene <type>` | Select an `INowScene` when the assembly contains several. |
| `--unity-project <directory>` | Find project assets when the scene is outside its Unity project. |
| `--configuration <name>` | Select Debug or Release for the scene assembly; the browser host remains Release. |
| `--no-build` | Reuse the existing scene assembly; the browser app still needs a build. |
| `--title <text>` | Set the page title. |
| `--aot` | Request ahead-of-time C# compilation; benchmark execution speed against the longer build and larger download. |
| `--all-assets` | Include all supported project assets when their paths are fully computed. |
| `--native-symbols` | Include the optional native function-name map for diagnostics. |

`render` and `animate` retain their native capture behavior. Their deterministic
clock, PNG sequence and input replay contracts are unchanged.

## Assets without an export step

The build reads string literals from the compiled scene and helper assemblies. It includes
matching project paths and Resources aliases, conservatively includes folder
prefixes used to construct filenames, and follows known serialized GUID
dependencies such as theme counterparts and font fallback graphs. For example:

```csharp
var theme = Resources.Load<NowThemeAsset>("Assets/UI/DarkTheme.asset");
var animation = Resources.Load<NowLottieAsset>("Assets/UI/Emoji/" + name + ".lottie");
```

The theme's supported references and the emoji folder are included
automatically. Files keep their original bytes and `.meta` data. The browser
preloads them into a virtual filesystem, then uses the same `NowFileResources`
and `NowProjectAssets` parsers as the native host. Resolved package paths become
`Packages/<package-name>/...`; the project lock file and local machine paths
are not needed in the deployed application.

All staged local assets load before the application starts, including all four
built-in Noto Sans styles (regular, bold, italic and bold italic) and selected
project fonts. The current host does not fetch local fonts on demand or use
CSS/system fonts: NowUI reads font bytes and renders its own glyphs. Remote
Lottie and Markdown downloads described below are a separate asynchronous path.

Supported source images, sprites, fonts, themes and Lottie formats follow the
[native asset contract](NativePreview.md#use-the-projects-assets). The build
does not include project scripts, ordinary configuration JSON, scenes,
prefabs or arbitrary Unity assets. Indirect missing, duplicate or unsupported
GUID references fail with the referencing asset and GUID.

Literal folder prefixes cover common computed filenames. If code constructs
the entire resource path at runtime, use `--all-assets`. Both modes keep the
same type allowlist and limits: 4,096 files, 256 MiB total and 64 MiB per file.
The output asset report lists included virtual paths, byte sizes and hashes.
Loaded objects belong to the host and are released with it.

## Browser boundaries

Rendering uses a WebGL2 canvas in Gamma color space. It does not create
semantic DOM controls, screen-reader structure or indexable page content;
it is suitable for the rendered application surface, not a replacement for
ordinary document content and its accessibility or SEO behavior.

Lottie and Markdown URLs use streaming browser HTTP transport with the shared
NowUI limits and cancellation behavior. Cross-origin requests require the
remote server to permit CORS. Redirects remain manual: browsers hide redirect
locations, so those requests fail explicitly instead of bypassing the
application's URL policy. Prefer a direct accessible asset URL or serve it
from the same origin.

Runtime filesystem writes, including `/tmp`, are ephemeral and do not provide
persistent browser storage. The host does not execute Unity components,
cameras, prefabs or arbitrary Unity shaders. Browser performance and device
input remain host-dependent; validate the deployed scene in its target browser.

The previous JavaScript authoring bridge, separate browser demo API and Editor
web-preview tools remain retired. This target adds deployment for the shared
C# scene contract.
