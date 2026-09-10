using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>
    /// Portable NowUI drawing code. A Unity host can compile this file and call
    /// Draw from its DrawNowUI callback, just as the native scene adapter does.
    /// The host owns the frame, font resources, rendering, and input.
    /// </summary>
    public static class PreviewContent
    {
        static readonly Color Background = Rgb(18, 21, 26);
        static readonly Color Panel = Rgb(25, 29, 35);
        static readonly Color Border = Rgb(47, 52, 60);
        static readonly Color White = Rgb(242, 242, 237);
        static readonly Color Muted = Rgb(142, 150, 159);
        static readonly Color Lime = Rgb(201, 238, 140);
        static readonly Color Violet = Rgb(194, 183, 240);
        static readonly Color Peach = Rgb(245, 186, 144);

        public static void Draw(NowRect view)
        {
            Now.Rectangle(view).SetColor(Background).Draw();
            var canvas = new Canvas(view);

            canvas.Box(20, 20, 920, 600, Panel, 18);
            canvas.Box(211, 20, 1, 600, Border);
            canvas.Box(212, 88, 728, 1, Border);

            canvas.Box(42, 42, 27, 27, Lime, 8);
            canvas.Box(49, 49, 5, 13, Background, 1);
            canvas.Box(57, 49, 5, 8, Background, 1);
            canvas.Text("fieldnotes", 80, 40, 120, 29, 19, White, bold: true);
            canvas.Text("YOUR WORKSPACE", 42, 107, 148, 20, 10, Muted);
            canvas.Box(34, 139, 164, 40, Rgb(44, 50, 43), 9);
            canvas.Box(48, 153, 12, 12, Lime, 3);
            canvas.Text("Overview", 74, 146, 116, 25, 13, Lime);
            canvas.Text("Collections", 74, 196, 116, 25, 13, Muted);
            canvas.Box(48, 204, 12, 9, Muted, 2);
            canvas.Box(50, 201, 6, 4, Muted, 1);
            canvas.Text("Activity", 74, 246, 116, 25, 13, Muted);
            canvas.Box(48, 255, 3, 8, Muted, 1);
            canvas.Box(53, 250, 3, 13, Muted, 1);
            canvas.Box(58, 253, 3, 10, Muted, 1);

            canvas.Box(34, 451, 164, 104, Rgb(32, 37, 43), 12);
            canvas.Text("A little space to grow", 48, 465, 137, 22, 11, White);
            canvas.Text("7 of 12 boards in use", 48, 489, 137, 20, 10, Muted);
            canvas.Box(48, 525, 136, 5, Border, 2);
            canvas.Box(48, 525, 80, 5, Lime, 2);
            canvas.Box(42, 574, 28, 28, Violet, 14);
            canvas.Text("A", 42, 574, 28, 28, 12, Background, centered: true);
            canvas.Text("Alex's studio", 81, 574, 114, 25, 12, White);

            canvas.Text("Studio / Overview", 244, 42, 340, 27, 12, Muted);
            canvas.Box(795, 40, 119, 30, Rgb(40, 49, 39), 15);
            canvas.Box(809, 52, 6, 6, Lime, 3);
            canvas.Text("All changes saved", 823, 43, 83, 23, 9, Lime);

            canvas.Text("Room to create.", 244, 116, 580, 49, 34, White, bold: true);
            canvas.Text("Gather your ideas. Make something worth keeping.", 244, 170, 615, 26, 13, Muted);

            DrawCollection(canvas, 244, Lime, "01", "Fresh perspectives", "12 ideas  /  Updated today", 0);
            DrawCollection(canvas, 473, Violet, "02", "Quiet moments", "8 ideas  /  Updated yesterday", 1);
            DrawCollection(canvas, 702, Peach, "03", "Things in motion", "16 ideas  /  Updated Tuesday", 2);

            canvas.Text("Recent boards", 244, 421, 400, 31, 18, White, bold: true);
            canvas.Text("VIEW ALL", 831, 426, 85, 23, 10, Muted);
            canvas.Box(244, 466, 670, 1, Border);
            DrawBoard(canvas, 479, "Brand exploration", "Design system", "Today", Lime);
            canvas.Box(244, 529, 670, 1, Border);
            DrawBoard(canvas, 542, "A slower kind of Sunday", "Inspiration", "Yesterday", Violet);
        }

        static void DrawCollection(Canvas canvas, float x, Color color, string number,
            string title, string subtitle, int motif)
        {
            canvas.Box(x, 222, 212, 174, Rgb(33, 38, 44), 12);
            // The motif deliberately extends beyond the hard mask. This exercises
            // the same Now.Mask scope used by scroll areas and custom controls.
            using (Now.Mask(canvas.Rect(x + 12, 234, 188, 89)))
            {
                canvas.Box(x + 12, 234, 188, 89, color, 6);
                Color ink = new Color(Background.r, Background.g, Background.b, .15f);
                if (motif == 0)
                {
                    canvas.Box(x + 133, 212, 78, 141, ink, 38);
                    canvas.Box(x + 81, 255, 78, 141, ink, 38);
                    canvas.Box(x + 29, 297, 78, 141, ink, 38);
                }
                else if (motif == 1)
                {
                    canvas.Box(x + 100, 242, 71, 71, ink, 36);
                    canvas.Box(x + 128, 215, 71, 71, ink, 36);
                    canvas.Box(x + 153, 188, 71, 71, ink, 36);
                }
                else
                {
                    for (int i = 0; i < 7; i++)
                        canvas.Box(x + 89 + i * 19, 222 + i * 8, 11, 123, ink, 5);
                }
            }
            canvas.Text(number, x + 24, 246, 60, 35, 22, Background);
            canvas.Text(title, x + 14, 333, 183, 26, 13, White, bold: true);
            canvas.Text(subtitle, x + 14, 364, 183, 20, 9, Muted);
        }

        static void DrawBoard(Canvas canvas, float y, string title, string category, string date, Color color)
        {
            canvas.Box(244, y, 36, 36, color, 7);
            canvas.Box(252, y + 8, 9, 20, new Color(0, 0, 0, .2f), 2);
            canvas.Box(264, y + 8, 8, 8, new Color(0, 0, 0, .2f), 2);
            canvas.Box(264, y + 19, 8, 9, new Color(0, 0, 0, .2f), 2);
            canvas.Text(title, 294, y + 3, 303, 29, 12, White);
            canvas.Text(category, 609, y + 3, 161, 29, 11, Muted);
            canvas.Text(date, 801, y + 3, 110, 29, 11, Muted);
        }

        static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f, 1);

        readonly struct Canvas
        {
            readonly float scale;
            readonly float left;
            readonly float top;

            public Canvas(NowRect view)
            {
                scale = Mathf.Min(view.width / 960f, view.height / 640f);
                left = view.x + (view.width - 960 * scale) * .5f;
                top = view.y + (view.height - 640 * scale) * .5f;
            }

            public NowRect Rect(float x, float y, float width, float height) =>
                new NowRect(left + x * scale, top + y * scale, width * scale, height * scale);

            public void Box(float x, float y, float width, float height, Color color, float radius = 0) =>
                Now.Rectangle(Rect(x, y, width, height)).SetColor(color).SetRadius(radius * scale).Draw();

            public void Text(string text, float x, float y, float width, float height, float size,
                Color color, bool bold = false, bool centered = false)
            {
                var rect = Rect(x, y, width, height);
                float fontSize = size * scale;
                var bounds = Now.font.MeasureTextBounds(text, fontSize);
                float offsetX = centered ? (rect.width - bounds.z) * .5f - bounds.x : -bounds.x;
                float offsetY = (rect.height - bounds.w) * .5f - bounds.y;
                Now.Text(rect.Offset(offsetX, offsetY))
                    .SetFontSize(fontSize).SetColor(color).SetBold(bold)
                    .SetMask(rect.Outset(2 * scale)).Draw(text);
            }
        }
    }
}
