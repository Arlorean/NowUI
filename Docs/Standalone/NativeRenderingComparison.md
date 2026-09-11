# Native and Unity rendering comparison

These measurements record the pixel-snapping investigation on 2026-09-10,
before native HarfBuzz shaping and Linear color-space support were enabled.
The counts below describe that validation run. Current capabilities and commands
are in [Native CLI](NativeCLI.md); rerunning the string-text comparisons with
shaping enabled may change glyph geometry.

The comparison renders the exact same sample C# source through Unity's renderer
and `NowUI.Desktop`. Both use Gamma color space, one UI unit per output pixel,
RGBA8 output without MSAA, and the same NotoSans resources. The interactive
sample's artwork is paused; idle warmup frames let control transitions settle.
This compares captured pixels, not the desktop compositor or a resized image
viewer.

Run from the repository root with Unity closed:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene ComparisonScene --width 1100 --height 760 --time 0.5 --output artifacts/local/native-comparison/native.png
./Tools/Standalone/Capture-NativeReference.ps1
./Tools/Standalone/Compare-Renders.ps1 -Reference artifacts/local/native-comparison/unity.png -Candidate artifacts/local/native-comparison/native.png -DiffPath artifacts/local/native-comparison/diff.png -Json artifacts/local/native-comparison/diff.json
```

`Capture-NativeReference.ps1` stages the existing sample source and an Editor
capture helper in a unique temporary Assets folder, runs graphics-enabled Unity,
and cleans up only that folder and its metadata. It preserves project settings.
Pass `-Unity` to choose the Unity executable. The PNG's neighboring JSON records
the actual Unity version, device, graphics API, color space, and capture settings.

For a direct pixel-alignment comparison:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene PixelAlignmentScene --width 960 --height 640 --output artifacts/local/native-comparison/alignment-native.png
./Tools/Standalone/Capture-NativeReference.ps1 -Scene PixelAlignment -Width 960 -Height 640 -Output artifacts/local/native-comparison/alignment-unity.png
```

The diagnostic repeats 12, 14, and 16 pixel text and one-pixel filled rectangles
at integer, half-pixel X, half-pixel Y, and half-pixel X/Y origins. It also compares
the sample's measured text centering against rounding the resulting text origin.
Inspect at 100% or use integer nearest-neighbor enlargement. Rounded text origins
still preserve the font's fractional glyph bounds, ascender, and advances; they
do not guarantee that all glyph edges align to pixels.

## Original comparison on Windows, 2026-09-10

Unity 6000.4.0f1 used Direct3D11; the native renderer used OpenGL 3.3. Both ran on
the same RTX 4080 SUPER. The 1100 × 760 paused sample passed the existing render
comparison tolerances:

- Mean absolute RGB channel difference: 0.447 out of 255.
- 99th percentile RGB channel difference: 1 out of 255.
- 0.862% of pixels differed by more than 8 on any channel.
- The selected 10 pixel control-label crop was byte-for-byte identical.
- Selected circle and rounded-rectangle crops differed by at most 1 per RGB channel.
- Some text edges differed: the 12 pixel subtitle crop had a mean RGB difference
  of 1.261 and a maximum of 53. Whole-image similarity does not imply identical text.

The text and rectangle shaders use the same antialiasing formulas in both hosts.
The native presentation copies the full-resolution framebuffer with nearest
filtering. A local surface probe reported matching 1100 × 760 client, framebuffer,
and output dimensions with a monitor scale of 1. These checks found no additional
whole-image smoothing in the native renderer. They do not establish behavior on
other displays or drivers.

The pixel-alignment diagnostic exposed a shared thin-rectangle defect. At row
418, seven integer-aligned vertical rectangles each cover one fully opaque pixel.
Moving their X origin by 0.5 makes them alternate between two opaque pixels and
no visible pixels in both Unity and native output. Further code inspection found
the cause before shading: `Now.DrawRectangle` rounds both edges independently
with `Mathf.RoundToInt`, whose half-way ties round to even. The interval
`[10.5, 11.5]` becomes `[10, 12]`, while `[11.5, 12.5]` becomes `[12, 12]` and
is discarded. The diagnostic uses untransformed rectangles, so it takes this
snapping path. This experiment therefore establishes a CPU snapping defect;
the initial suspicion of a signed-distance derivative problem was not supported.

The original snap also used logical coordinates rather than physical pixel
coordinates. Both issues are addressed by the changes below.
The complete diagnostic image has larger text differences than the interactive
sample: 2.063% of pixels exceed the 8-channel-value threshold, slightly above the
comparison tool's default 2% allowance. The thin-bar samples described above are
pixel-for-pixel identical despite that broader text variation.

Fractional text positioning also changes glyph coverage. The sample's centered
labels produce fractional text origins, but simply rounding those origins does
not pixel-fit the font's internal geometry.

## Physical pixel snapping fix

Untransformed rectangle and SDF edges now snap after applying the UI scale, using
a consistent half-tie rule toward positive infinity. A bounded floating-point
tolerance stabilizes half-pixel ties at fractional scales. Nine-slice cells share
the same snapped boundaries without rounding their widths a second time.
Transformed geometry and the explicit unsnapped SDF path retain fractional
positions.

Fresh Unity and native captures show all seven half-pixel-placed bars at exactly
one opaque pixel wide. Preserve the original captures when reproducing the fix:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/NativePreview/NativePreview.csproj --scene PixelAlignmentScene --width 960 --height 640 --output artifacts/local/native-comparison/alignment-native-fixed.png
./Tools/Standalone/Capture-NativeReference.ps1 -Scene PixelAlignment -Width 960 -Height 640 -Output artifacts/local/native-comparison/alignment-unity-fixed.png
```

The focused Unity suite passed all 68 tests, including positive and negative
coordinates, scales 1/1.25/1.5/2, shared edges, compressed nine-slices, transforms,
text shaping, and animation. The standalone non-performance suite passed 820
tests with 18 expected skips. All 30 native tests passed with graphics enabled,
including a pixel-level regression for the half-pixel-placed bars.

## Optional baseline snapping and bitmap comparison

`NowText.SetBaselineSnap()` opts static text into physical-pixel baseline snapping.
It leaves horizontal advances, shaped glyph offsets, glyph contours and text
measurement unchanged. Each line snaps independently. Transforms and configured
glyph animations bypass snapping; leave it disabled for manually moving text.
The default remains off. See [Text Styling](../../Assets/NowUI/Documentation~/TextStyling.md).

The [small-text experiment](../../Standalone/Samples/TextRenderingComparison/README.md)
renders three columns through the actual native host: current SDF, SDF with
baseline snapping, and Skia grayscale bitmap text. All use the same bundled
NotoSans font, NowUI glyph advances, colors and 12/14/16 pixel sizes. SkiaSharp is
a dependency of this diagnostic sample only.

Bitmap text looks heavier and sharper in these captures; baseline snapping has a
subtler effect. An isolated Skia `Hinting.Normal` versus `Hinting.None` control
changed only 28/16/13 alpha pixels at the respective sizes. The larger visual
difference therefore cannot reasonably be attributed chiefly to hinting. This
is a rasterization experiment, not a production font backend. Default SDF
coverage and shared shader formulas remain unchanged.
