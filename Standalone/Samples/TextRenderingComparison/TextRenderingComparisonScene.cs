using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using NowUI.Hosting;
using SkiaSharp;
using UnityEngine;

namespace NowUI.Samples.TextRenderingComparison
{
    /// <summary>A 960 x 640, one-device-pixel-per-UI-unit text rasterization experiment.</summary>
    public sealed class TextRenderingComparisonScene : INowScene, IDisposable
    {
        const string Sample = "Hh 11 minimum / fresh ideas";
        const int BitmapWidth = 286;
        const int BitmapHeight = 32;
        const int BitmapBaseline = 24;
        static readonly string[] Titles = { "NowUI SDF", "SDF + baseline snap", "Skia grayscale bitmap" };
        static readonly string[] Subtitles = { "Fractional baseline", "Same glyph spacing", "Hinting.Normal requested" };
        static readonly Color Dark = Rgb(25, 29, 35);
        static readonly Color Light = Rgb(245, 244, 239);
        readonly Dictionary<int, Texture2D> textures = new Dictionary<int, Texture2D>();
        SKTypeface typeface;
        bool initialized;

        public void Draw(NowRect view)
        {
            if (!initialized) Initialize();
            Box(view, Rgb(18, 21, 26));
            Text("Small text: alignment and rasterization", 24, 15, 27, Light);
            Text("Same Noto Sans Regular file, sizes, colors and fractional character advances. Inspect the PNG at 100%.",
                24, 58, 12, Rgb(164, 174, 184));

            for (int column = 0; column < 3; column++)
            {
                float x = 24 + column * 308;
                Text(Titles[column], x + 12, 90, 16, Light);
                Text(Subtitles[column], x + 12, 116, 11, Rgb(192, 214, 160));
                DrawPanel(column, x, 150, Dark, Light);
                DrawPanel(column, x, 369, Light, Dark);
            }

            Text("Each size: top sample uses an integer line-box Y; lower sample adds 0.5px to that Y.",
                24, 594, 11, Rgb(164, 174, 184));
            Text("Bitmap text uses an integer baseline, grayscale coverage and the exact NowUI glyph advances. No LCD color AA.",
                24, 612, 11, Rgb(164, 174, 184));
        }

        internal void DrawHintingControl(NowRect view)
        {
            if (!initialized) Initialize();
            Box(view, Rgb(18, 21, 26));
            Text("Skia: isolate the hinting setting", 16, 12, 23, Light);
            Text("Identical bitmap path, pixel baseline and fractional advances. Only Hinting changes.",
                16, 52, 11, Rgb(164, 174, 184));
            Text("Hinting.Normal", 28, 83, 15, Light);
            Text("Hinting.None", 336, 83, 15, Light);
            DrawPanel(2, 16, 113, Dark, Light);
            DrawPanel(3, 324, 113, Dark, Light);
            Text("On this Windows machine the two settings produce almost identical coverage.",
                16, 330, 11, Rgb(164, 174, 184));
        }

        void DrawPanel(int column, float x, float top, Color background, Color foreground)
        {
            Box(new NowRect(x, top, 296, 207), background);
            for (int row = 0; row < 3; row++)
            {
                int size = 12 + row * 2;
                float rowTop = top + 8 + row * 66;
                Text(size + " px", x + 12, rowTop, 10, foreground * new Color(1, 1, 1, .65f));
                DrawSample(column, x + 12, rowTop + 16, size, foreground);
                DrawSample(column, x + 12, rowTop + 36.5f, size, foreground);
            }
        }

        void DrawSample(int column, float x, float lineTop, int size, Color color)
        {
            float baseline = lineTop + Now.font.GetAscender() * size;
            if (column >= 2)
            {
                float y = MathF.Floor(baseline + .5f) - BitmapBaseline;
                Now.Rectangle(new NowRect(x, y, BitmapWidth, BitmapHeight))
                    .SetTexture(textures[column == 3 ? size + 100 : size]).SetColor(color).Draw();
            }
            else
            {
                Now.Text(new NowRect(x, lineTop, BitmapWidth, BitmapHeight))
                    .SetFontSize(size).SetBaselineSnap(column == 1).SetColor(color).Draw(Sample.AsSpan());
            }
        }

        void Initialize()
        {
            string fontPath = Path.Combine(NowFileResources.bundledRoot, "NowUI", "NotoSans-Regular.ttf");
            typeface = SKTypeface.FromFile(fontPath) ?? throw new InvalidOperationException("Could not open comparison font: " + fontPath);
            Console.WriteLine("Text comparison: NotoSans-Regular.ttf SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fontPath))));
            Console.WriteLine("SkiaSharp=" + typeof(SKFont).Assembly.GetName().Version +
                "; Edging=Antialias; Hinting=Normal; Subpixel=true; LinearMetrics=true; BaselineSnap=true; EmbeddedBitmaps=false");
            for (int size = 12; size <= 16; size += 2)
            {
                byte[] hinted = Rasterize(size, SKFontHinting.Normal);
                byte[] unhinted = Rasterize(size, SKFontHinting.None);
                int differentPixels = 0;
                int coveragePixels = 0;
                for (int i = 3; i < hinted.Length; i += 4)
                {
                    if (hinted[i] != unhinted[i]) differentPixels++;
                    if (hinted[i] != 0) coveragePixels++;
                }
                if (coveragePixels == 0) throw new InvalidOperationException("Skia produced an empty text bitmap.");
                Console.WriteLine(size + "px: Normal-vs-None alpha differences=" + differentPixels +
                    "; nonempty pixels=" + coveragePixels + "; Normal SHA256=" + Convert.ToHexString(SHA256.HashData(hinted)));
                textures.Add(size, CreateTexture(hinted, "Normal " + size + "px"));
                textures.Add(size + 100, CreateTexture(unhinted, "None " + size + "px"));
            }
            initialized = true;
        }

        static Texture2D CreateTexture(byte[] rgba, string name)
        {
            var texture = new Texture2D(BitmapWidth, BitmapHeight, TextureFormat.RGBA32, false, false)
            {
                name = "Hinting comparison " + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.LoadRawTextureData(rgba);
            texture.Apply(false, false);
            return texture;
        }

        byte[] Rasterize(int size, SKFontHinting hinting)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(BitmapWidth, BitmapHeight, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var canvas = new SKCanvas(bitmap);
            using var font = new SKFont(typeface, size)
            {
                Edging = SKFontEdging.Antialias,
                Hinting = hinting,
                Subpixel = true,
                LinearMetrics = true,
                BaselineSnap = true,
                EmbeddedBitmaps = false
            };
            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            canvas.Clear(SKColors.Transparent);
            float penX = 0;
            // The standalone NowUI path currently uses unshaped codepoint runs.
            // Reuse its advances explicitly, avoiding spacing/shaping differences
            // between the two rasterizers and preserving every fractional advance.
            foreach (char c in Sample)
            {
                if (!Now.font.TryResolveGlyph(c, size, NowFontStyle.Regular, out _, out var glyph, out _))
                    throw new InvalidOperationException("NowUI could not resolve comparison glyph " + c);
                canvas.DrawText(c.ToString(), penX, BitmapBaseline, SKTextAlign.Left, font, paint);
                penX += glyph.advance * size;
            }
            canvas.Flush();
            var rgba = new byte[BitmapWidth * BitmapHeight * 4];
            for (int y = 0; y < BitmapHeight; y++)
            for (int x = 0; x < BitmapWidth; x++)
            {
                // Texture2D rows are bottom-up. Keep straight white RGB + coverage
                // alpha so the NowUI material applies the same sRGB foreground tint.
                int offset = ((BitmapHeight - 1 - y) * BitmapWidth + x) * 4;
                rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = 255;
                rgba[offset + 3] = bitmap.GetPixel(x, y).Alpha;
            }
            return rgba;
        }

        public void Dispose()
        {
            foreach (Texture2D texture in textures.Values) UnityEngine.Object.Destroy(texture);
            textures.Clear();
            typeface?.Dispose();
            typeface = null;
        }

        static void Text(string value, float x, float y, float size, Color color) =>
            Now.Text(new NowRect(x, y, 910, 42)).SetFontSize(size).SetColor(color).Draw(value);
        static void Box(NowRect rect, Color color) => Now.Rectangle(rect).SetColor(color).Draw();
        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1);
    }

    /// <summary>A 640 x 360 control: same Skia path with Normal versus None hinting.</summary>
    public sealed class SkiaHintingControlScene : INowScene, IDisposable
    {
        readonly TextRenderingComparisonScene comparison = new TextRenderingComparisonScene();
        public void Draw(NowRect view) => comparison.DrawHintingControl(view);
        public void Dispose() => comparison.Dispose();
    }
}
