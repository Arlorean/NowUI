using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>
    /// A portable 960 by 640 diagnostic, drawn at one UI unit per output pixel.
    /// Only the submitted primitive/text origin changes between columns; font
    /// outlines and advance widths retain their original fractional positions.
    /// </summary>
    public static class PixelAlignmentContent
    {
        static readonly Color Background = Rgb(18, 21, 26);
        static readonly Color Panel = Rgb(25, 29, 35);
        static readonly Color White = Rgb(242, 242, 237);
        static readonly Color Muted = Rgb(150, 158, 169);
        static readonly Color Accent = Rgb(201, 238, 140);
        static readonly string[] Titles = { "Integer origin", "Half pixel X", "Half pixel Y", "Half pixel X + Y" };
        static readonly string[] Offsets = { "offset (0, 0)", "offset (0.5, 0)", "offset (0, 0.5)", "offset (0.5, 0.5)" };

        public static void Draw(NowRect view)
        {
            Box(view, Background);
            float x = Mathf.Round(view.x) + 24;
            float y = Mathf.Round(view.y) + 20;
            Text("Pixel alignment", new NowRect(x, y, 500, 42), 27, White);
            Text("Same font and shader. Only the origin moves. Inspect at 100% zoom and a 1:1 UI scale.",
                new NowRect(x, y + 45, view.width - 48, 28), 12, Muted);

            float step = Mathf.Floor((view.width - 48) / 4);
            for (int column = 0; column < 4; column++)
            {
                float panelX = x + column * step;
                float contentX = panelX + 14;
                float top = y + 89;
                float dx = (column & 1) != 0 ? .5f : 0;
                float dy = (column & 2) != 0 ? .5f : 0;
                Box(new NowRect(panelX, top, step - 8, 349), Panel);
                Text(Titles[column], new NowRect(contentX, top + 12, step - 28, 24), 14, White);
                Text(Offsets[column], new NowRect(contentX, top + 40, step - 28, 22), 11, Accent);

                for (int row = 0; row < 3; row++)
                {
                    int size = 12 + row * 2;
                    float rowY = top + 78 + row * 55;
                    Text(size + " px / line-box origin", new NowRect(contentX, rowY, step - 28, 20), 10, Muted);
                    Text("Hh 11 minimum", new NowRect(contentX + dx, rowY + 20 + dy, step - 28, 30), size, White);
                }

                Text("1 px filled rectangles", new NowRect(contentX, top + 249, step - 28, 20), 10, Muted);
                // Integer boundaries enclose complete pixel centers at 1:1.
                // A half-pixel shift splits edge coverage over adjacent pixels.
                Box(new NowRect(contentX + dx, top + 277 + dy, step - 40, 1), White);
                for (int bar = 0; bar < 7; bar++)
                    Box(new NowRect(contentX + bar * 7 + dx, top + 295 + dy, 1, 30), White);
            }

            float comparisonY = y + 458;
            Text("Sample label positioning", new NowRect(x, comparisonY, 500, 26), 16, White);
            Text("Both labels use the same measured centering; the second rounds the resulting line-box origin.",
                new NowRect(x, comparisonY + 29, view.width - 48, 23), 11, Muted);
            float half = Mathf.Floor((view.width - 64) * .5f);
            DrawCenteredComparison(new NowRect(x, comparisonY + 62, half, 46), false);
            DrawCenteredComparison(new NowRect(x + half + 16, comparisonY + 62, half, 46), true);
            Text("Origin alignment does not pixel-fit individual glyph contours or fractional character advances.",
                new NowRect(x, comparisonY + 116, view.width - 48, 23), 11, Muted);
        }

        static void DrawCenteredComparison(NowRect rect, bool snapOrigin)
        {
            Box(rect, Panel);
            string value = "Fresh perspectives / minimum 11";
            var label = new NowRect(rect.x + 12, rect.y + 8, rect.width - 24, 30);
            var bounds = Now.font.MeasureTextBounds(value, 12);
            var origin = label.Offset(-bounds.x, (label.height - bounds.w) * .5f - bounds.y);
            if (snapOrigin)
                origin = new NowRect(Mathf.Round(origin.x), Mathf.Round(origin.y), origin.width, origin.height);
            Text(value, origin, 12, White);
            Text(snapOrigin ? "rounded" : "centered", new NowRect(rect.xMax - 80, rect.y - 17, 80, 18), 10, Accent);
        }

        static void Text(string value, NowRect rect, float size, Color color) =>
            Now.Text(rect).SetFontSize(size).SetColor(color).Draw(value);

        static void Box(NowRect rect, Color color) => Now.Rectangle(rect).SetColor(color).Draw();
        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1);
    }
}
