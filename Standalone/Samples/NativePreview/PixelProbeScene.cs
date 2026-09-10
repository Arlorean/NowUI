using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>A diagnostic scene for PNG orientation, channels, blending, masks, and text.</summary>
    public sealed class PixelProbeScene : INowScene
    {
        public void Draw(NowRect view)
        {
            float halfWidth = view.width * .5f;
            float halfHeight = view.height * .5f;
            Now.Rectangle(new NowRect(view.x, view.y, halfWidth, halfHeight)).SetColor(Color.red).Draw();
            Now.Rectangle(new NowRect(view.x + halfWidth, view.y, halfWidth, halfHeight)).SetColor(Color.green).Draw();
            Now.Rectangle(new NowRect(view.x, view.y + halfHeight, halfWidth, halfHeight)).SetColor(Color.blue).Draw();
            Now.Rectangle(new NowRect(view.x + halfWidth, view.y + halfHeight, halfWidth, halfHeight))
                .SetColor(new Color(1, 1, 0, 1)).Draw();

            float marker = Mathf.Min(8, Mathf.Min(view.width, view.height) * .05f);
            Now.Rectangle(new NowRect(view.x, view.y, marker, marker)).SetColor(Color.white).Draw();
            Now.Rectangle(new NowRect(view.xMax - marker, view.y, marker, marker)).SetColor(Color.black).Draw();
            Now.Rectangle(new NowRect(view.x, view.yMax - marker, marker, marker))
                .SetColor(new Color(1, 0, 1, 1)).Draw();
            Now.Rectangle(new NowRect(view.xMax - marker, view.yMax - marker, marker, marker))
                .SetColor(new Color(0, 1, 1, 1)).Draw();

            Now.Rectangle(new NowRect(view.x + view.width * .1f, view.y + view.height * .1f,
                    view.width * .15f, view.height * .15f))
                .SetColor(new Color(1, 1, 1, .5f)).Draw();

            var clip = new NowRect(view.x + view.width * .7f, view.y + view.height * .7f,
                view.width * .15f, view.height * .15f);
            using (Now.Mask(clip))
                Now.Rectangle(clip.Outset(view.width * .1f)).SetColor(Color.white).Draw();

            Now.Text(new NowRect(view.x + 16, view.y + view.height * .55f, view.width * .4f, view.height * .25f))
                .SetFontSize(Mathf.Min(24, view.height * .08f)).SetColor(Color.white).Draw("C# / NowUI");
        }
    }
}
