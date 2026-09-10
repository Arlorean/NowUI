# Native C# previews

Use this workflow for NowUI mockups, interactive demonstrations and animation
apps. It runs real C# with the same `Now` / `NowLayout` drawing API used in Unity.
The native host reads supported project assets directly; no asset export is needed.

For an explicitly requested website or browser deployment, use the optional
[browser target](BrowserDeployment.md). It runs the same C# scene; native preview
remains the default workflow.

## Start an app

From the Unity project directory, use the package launcher:

```powershell
pwsh -File <package-root>/Native~/nowui.ps1 init NowUI/apps/Demo
pwsh -File <package-root>/Native~/nowui.ps1 preview NowUI/apps/Demo/Preview.csproj
```

In the NowUI source checkout, `Tools/NowUI-Native.ps1` accepts the same arguments.
The launcher uses the bundled CLI in a project-local tool directory. The .NET 9
SDK and an OpenGL 3.3 desktop session are required; the launcher uses PowerShell 7.
Unity need not be open. Generated projects include the standalone extensions,
including SDF, Docking, Markup, Markdown, Code Editor and Node Graph.

`init` writes a normal C# project and a scene. Edit the generated `Scene.cs` and
save: the preview watches source and project assets by default. A successful build
restarts the scene with the new code and assets. Compiler errors keep the last
working preview running and print diagnostics. Use `--no-watch` when a fixed app
instance is preferable. Reload resets application state.

The host owns frame timing, input and `Now.StartUI`. Scene code implements
`INowScene.Draw(NowRect view)` and uses the regular NowUI builders. Keep reusable
drawing methods separate from the native adapter so Unity hosts can call them too.

## Use the project's assets

Existing `Resources` names resolve normally. The native loader also accepts project
paths and a named sprite after `#`, without moving files into a Resources folder:

```csharp
var logo = Resources.Load<Texture2D>("Assets/Brand/logo.png");
var panel = Resources.Load<Sprite>("Assets/UI/panels.png#rounded");
var theme = Resources.Load<NowThemeAsset>("Assets/UI/DarkTheme.asset");
var font = Resources.Load<NowFontAsset>("Assets/Fonts/Body.ttf.asset");

Now.Rectangle(rect).SetTexture(logo).Draw();
Now.Rectangle(panelRect).SetSprite(panel, sliced: true).Draw();
```

PNG/JPEG/TGA/BMP, sprite `.meta` settings, TTF/OTF, and known NowUI theme/font
assets are supported, including binary font files and common embedded baked atlases.
Lottie JSON, `.lottie` archives and known `NowLottieAsset` files also load directly;
draw them with the regular `Now.Lottie` API. Existing Lottie and Markdown URL APIs
use the host's HTTP transport with the same shared download policies.
Asset references and font fallbacks resolve through GUIDs and local file IDs.
The host owns these cached objects; scene code should not destroy them.

Project paths and `#` selectors are standalone-provider extensions to `Resources.Load`. Shared
Unity drawing code can receive the loaded objects through its usual fields or
constructor. For a scene located outside its Unity project, pass
`--unity-project <project-directory>`; local projects are discovered automatically.

## Stills and animations

An animation can simply be an app: draw from `Time.time`, `Time.deltaTime` or the
regular NowUI animation APIs, and use `preview` to interact with it.

Capture a still at a reproducible time:

```powershell
pwsh -File <package-root>/Native~/nowui.ps1 render NowUI/apps/Demo/Preview.csproj --time 0.5 --output NowUI/captures/demo.png
```

Capture a deterministic sequence:

```powershell
pwsh -File <package-root>/Native~/nowui.ps1 animate NowUI/apps/Demo/Preview.csproj --duration 2 --fps 30 --output NowUI/captures/demo-frames
```

The frame directory is published only after capture succeeds and must not already
exist. It includes timing metadata. Use `--input <replay.json>` to replay pointer,
keyboard, text and scrolling events in `render` or `animate`. The source checkout's
`Tools/Encode-NowUINativeAnimation.ps1 -Frames <directory> -Output demo.webp`
helper can turn the sequence into an opaque animated WebP for sharing (Python 3
with Pillow required only for encoding).

Choose capture size with `--width` and `--height`, select a scene with `--scene`,
and select `--color-space gamma|linear` when comparing with a Unity project.
Remote assets settle at the capture's fixed animation time; `--load-timeout`
sets the loading limit in seconds (default 30). Interactive previews load them
asynchronously.
Run `--help` for the installed CLI's exact options.

## Validation boundaries

Stock text, shapes, images, masks, glass blur and SDF effects run in the native
renderer, including sprite distance fields and Gamma/Linear color. Native font
libraries are included for Windows x64, Linux x64 and macOS x64/arm64; actual
execution has been verified on Windows. HarfBuzz shaping is enabled.

The shared key-binding field, five mouse buttons, wheel, clipboard, Unicode
editing, configurable WASD/arrow/Tab navigation and mapped gamepad navigation
are available. Key bindings use the existing `UnityEngine.InputSystem.Key` values,
including F1–F24, keypad and modifiers. Gamepad South/Start submits and East/Back
cancels. Native key names follow the layout where available; media-key capture
is Windows-only, and reserved OEM3–OEM5 keys have no GLFW mapping.

| Native input bridge | Windows | macOS / Linux |
| --- | --- | --- |
| Mouse/keyboard/clipboard/committed Unicode and gamepad navigation | Implemented | Portable GLFW path; not runtime-validated here |
| IME composition and candidate placement | Implemented | Not implemented |
| Primary touch and capture cancellation | Implemented | Native touch bridge not implemented |
| On-screen keyboard requests | Windows 10+ per-window InputPane | Not implemented |

Touch preserves the first contact's drag ownership and clears hover after
release. Additional contacts do not become pinch/rotation gestures. Touching an
editable control requests the Windows keyboard; leaving text capture or losing
window focus hides it. Windows may decline the request. Keyboard text follows
the normal Unicode/IME path rather than a separate mobile text session.

Tests drive real NowUI controls with synthetic device events and exercise
IME/media/cancellation messages and InputPane availability on a hidden Windows
window. Physical touch/gamepad delivery, on-screen typing and human IME candidate
selection have not been validated. These bridges therefore do not yet provide
complete input parity across every desktop operating system.

The native host supports ordinary 2D NowUI drawing; it does not execute Unity
components, prefabs, cameras or arbitrary Unity shaders. Source texture loading
does not reproduce Unity's texture compression or custom importers. Unsupported
formats fail with the asset path rather than silently substituting placeholders.
Validate Unity-specific behavior in the requested Unity host as well.

Native vector tessellation is enabled. The source checkout's
`Docs/Standalone/NativePerformance.md` records frame timings, allocations and
the Unity comparison; native jobs do not generally reproduce Unity Burst's
execution model. Detailed contracts and tests also live in `Docs/Standalone`.
