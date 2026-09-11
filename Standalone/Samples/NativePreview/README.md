# Native C# preview sample

## Motion room: Lottie and input playground

```powershell
./Tools/NowUI-Native.ps1 preview Standalone/Samples/NativePreview/NativePreview.csproj --scene PlaygroundScene --width 1120 --height 780
```

Choose one of four original project Lottie assets, edit the caption, change speed
and size, pause/restart, or drag the timeline to inspect a frame. The gallery
buttons and dropdown select the same animation. The key-binding field captures
a key for demonstration; it does not install a global shortcut. The light-theme
switch follows the existing DefaultDark asset's linked theme. Tab and gamepad
navigation use the shared NowUI controls.

Keep the window at least 850 × 650; key capture appears at heights of 735 or more.
Assets are owned by the host, and edits remain in memory for the preview session.
For a reproducible interaction capture at 1120 × 780:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene PlaygroundScene --width 1120 --height 780 --time 3 --input Standalone/Samples/NativePreview/playground-input.json --output artifacts/local/native-playground/interacted.png
```

## Interactive desktop preview

Open the interactive sample in a resizable native window:

```powershell
dotnet run --project Standalone/NowUI.Cli -- preview Standalone/Samples/NativePreview/NativePreview.csproj --scene NowUI.Samples.NativePreview.InteractiveScene --width 1100 --height 760
```

The window contains ordinary NowUI controls with caller-owned C# state:

- Edit the board title; the cover and selected shelf item update as you type.
- Choose a palette from the dropdown to recolor the cover.
- Drag the shape-size slider to resize the artwork.
- Toggle motion to pause and resume the animation in place.
- Click **Add an idea** to create and select a new board; the session count increases.
- Scroll the idea shelf and select another board to load its title.

The layout fits windows from 800 × 600 upward. Edits are kept in memory for the
life of the preview. `InteractiveContent.cs` owns both the drawing and state;
`InteractiveScene.cs` is only the native host adapter. The content can be reused
inside another NowUI host and disposed when that host closes. Only the shelf
uses a locally measured layout region; the rest uses resolved rectangles.

To capture this same interactive UI as a still, use the existing render command:

```powershell
dotnet run --project Standalone/NowUI.Cli -- render Standalone/Samples/NativePreview/NativePreview.csproj --scene NowUI.Samples.NativePreview.InteractiveScene --width 1100 --height 760 --time 0.5 --output interactive.png
```

The half-second clock lets the standard controls' opening transitions settle.

Saved C# and asset edits reload the open preview automatically. A successful
reload restarts the scene state; compiler errors leave the current preview open.
Pass `--no-watch` to disable reload.

## Record an animation and interactions

The same `InteractiveScene` supplies both the live animation and deterministic
frame capture. This three-second example adds a board, edits its title and
scrolls the shelf using the checked-in replay:

```powershell
./Tools/NowUI-Native.ps1 animate Standalone/Samples/NativePreview/NativePreview.csproj --scene InteractiveScene --width 1100 --height 760 --duration 3 --fps 30 --input Standalone/Samples/NativePreview/demo-input.json --output artifacts/local/native-demo-frames
```

The output must be a new directory. It contains `frame-000000.png` onward and
`animation.json` with the dimensions, frame rate and filename pattern. The replay
coordinates are authored for 1100 × 760. See the [native CLI guide](../../../Docs/Standalone/NativeCLI.md)
for replay events and animated WebP encoding.

## Compare rendering and pixel alignment

`ComparisonScene` freezes the interactive sample's artwork for matching Unity
and native captures. `PixelAlignmentScene` compares integer and half-pixel
origins for small text and one-pixel rectangles at 960 × 640:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene PixelAlignmentScene --width 960 --height 640 --output artifacts/local/native-comparison/alignment-native.png
```

The diagnostic also compares the sample's measured text centering with a rounded
text origin. Rounding that origin does not pixel-fit individual glyph contours
or character advances. See [rendering comparison](../../../Docs/Standalone/NativeRenderingComparison.md)
for matching Unity captures and measured results.

## Static workspace mockup

This ordinary C# class library uses the public `Now` API. `PreviewScene` draws a
960 × 640 workspace mockup and fits it into other output sizes without changing
its aspect ratio. It uses rounded rectangles, alpha blending, text, and hard
clipping; it has no external images or interaction requirements.

Render from the repository root with the native CLI:

```powershell
dotnet run --project Standalone/NowUI.Cli -- render Standalone/Samples/NativePreview/NativePreview.csproj --scene NowUI.Samples.NativePreview.PreviewScene --width 960 --height 640 --output preview.png
```

`PreviewContent.cs` contains the portable drawing code. The native adapter only
implements `INowScene` and delegates to `PreviewContent.Draw(view)`. To reuse the
drawing in Unity, compile `PreviewContent.cs` with the NowUI package and call
`PreviewContent.Draw(view)` from an existing NowUI host's `DrawNowUI` callback.
The host owns the frame lifecycle; the drawing does not call `Now.StartUI`.
The optional browser target runs this same C# drawing code:

```powershell
./Tools/NowUI-Native.ps1 preview Standalone/Samples/NativePreview/NativePreview.csproj --scene LottiePreviewScene --target web
./Tools/NowUI-Native.ps1 publish Standalone/Samples/NativePreview/NativePreview.csproj --scene LottiePreviewScene --target web --output artifacts/local/lottie-site
```

See [Browser Deployment](../../../Assets/NowUI/Documentation~/BrowserDeployment.md)
for the optional workload, automatic asset inclusion and host limits. The old
JavaScript authoring bridge remains retired.

## Project assets

`AssetPreviewScene` demonstrates [direct Unity project assets](../../../Docs/Standalone/NativeAssets.md)
using the existing local Google logo, DefaultDark theme and JetBrains Mono font.
It requires `Assets/Mockups/Google/google-logo.png` in this workspace; the other
scenes remain independent of that optional image. Render at 960 x 610, or use
`preview` to switch between the theme's linked light/dark assets.

## Pixel readback probe

Use `--scene NowUI.Samples.NativePreview.PixelProbeScene` for a diagnostic PNG.
At 320 × 240 or larger, sample well inside shape edges:

| Position as a fraction of image size | Expected RGB | Purpose |
| --- | --- | --- |
| (0.35, 0.35) | (255, 0, 0) | Top-left quadrant |
| (0.65, 0.35) | (0, 255, 0) | Top-right quadrant |
| (0.35, 0.90) | (0, 0, 255) | Bottom-left quadrant |
| (0.65, 0.90) | (255, 255, 0) | Bottom-right quadrant |
| (0.15, 0.15) | approximately (255, 128, 128) | Half-opacity white over red |
| (0.75, 0.75) | (255, 255, 255) | Inside hard mask |
| (0.65, 0.75) | (255, 255, 0) | Outside hard mask |

Each corner also has an 8 × 8 marker: white at top left, black at top right,
magenta at bottom left, and cyan at bottom right. The blue quadrant includes
white `C# / NowUI` text. The output is opaque throughout.
