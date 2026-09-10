# Small text rendering experiment

This is a diagnostic scene, not a new NowUI font backend. It compares the current
NowUI SDF renderer, optional baseline snapping, and grayscale bitmap text generated
by SkiaSharp, composited through the actual NowUI native renderer.

From the repository root:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/TextRenderingComparison/TextRenderingComparison.csproj --scene TextRenderingComparisonScene --width 960 --height 640 --output artifacts/local/native-comparison/text-hinting.png
```

Open the resulting 960 by 640 PNG at 100% zoom: one image pixel represents one
rendered device pixel. The scene is intended for this size and scale; it does not
rerasterize its cached bitmap text for preview-window DPI changes.

All samples use the bundled `NotoSans-Regular.ttf` at 12, 14 and 16 pixels, with
identical foreground and background colors. Each size has an integer line-box Y
and a second line shifted by an additional half pixel. The line-box origin is not
the glyph baseline: the font's ascender establishes the baseline. The snapped SDF
and bitmap columns round that baseline, leaving horizontal positions fractional.

Skia renders the same characters individually at **NowUI's own fractional glyph
advances** to isolate rasterization from text spacing. This diagnostic explicitly
uses the span overload for unshaped codepoint runs, even though the native host
now supports HarfBuzz shaping. It therefore does not compare
complex shaping, ligatures, fallback fonts, RTL, or native platform text layout.

The sample alone depends on
[SkiaSharp 4.152.0](https://www.nuget.org/packages/SkiaSharp/4.152.0), the latest
stable release when this experiment was written. Its
[SKFont options](https://learn.microsoft.com/en-us/dotnet/api/skiasharp.skfont)
are set explicitly: `Hinting.Normal`, `Edging.Antialias`, `Subpixel = true`,
`LinearMetrics = true`, `BaselineSnap = true`, `EmbeddedBitmaps = false`.
These request hinting while retaining fractional placement and grayscale coverage;
they do not request LCD subpixel color antialiasing or synthesize a bold face.

The platform's Skia font backend determines how those hinting requests are applied.
The sample also rasterizes a `Hinting.None` control and logs its alpha
pixel difference count against `Normal`. A tiny difference here means a visible
SDF-versus-bitmap change cannot reasonably be attributed chiefly to hinting.

Render that isolated control at 640 by 360 pixels:

```powershell
./Tools/NowUI-Native.ps1 render Standalone/Samples/TextRenderingComparison/TextRenderingComparison.csproj --scene SkiaHintingControlScene --width 640 --height 360 --output artifacts/local/native-comparison/text-hinting-control.png
```

Bitmap glyphs are rendered once into transparent RGBA coverage textures, copied to
Unity-style bottom-up storage with straight white RGB, then tinted by NowUI using
the same authored colors as the SDF column. Point filtering at integer texture
coordinates avoids an extra bitmap resampling blur. Textures are disposed with
the scene; no native library or dependency is added to NowUI's runtime or hosts.

On the tested Windows/NVIDIA machine, grayscale bitmap text looked visibly heavier
and sharper at these sizes; baseline snapping made a subtler difference to SDF.
`Normal` versus `None` changed only 28, 16 and 13 alpha pixels respectively at 12,
14 and 16 pixels. This shows a useful rasterization alternative, but does not
establish that switching hinting on alone produces the improved sharpness. Font
backend behavior, coverage generation, contrast and weight all remain relevant.

A production bitmap text mode would also need DPI-aware glyph caching, shaping,
fallbacks, scrolling/transform behavior, clipping, color and outline policy, and
performance/memory validation. This experiment deliberately adds none of those.
