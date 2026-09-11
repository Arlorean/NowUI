using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Typography probe using the project's actual Latin and Arabic font assets.</summary>
    public sealed class TextShapingScene : INowScene
    {
        public void Draw(NowRect view)
        {
            Now.Rectangle(view).SetColor(new Color(.07f, .08f, .10f, 1)).Draw();
            var latin = Resources.Load<NowFont>("Assets/NowUI/Assets/Fonts/NotoSans/NotoSans-Regular.ttf.asset");
            var arabic = Resources.Load<NowFont>("Assets/NowUI/Assets/Fonts/Noto_Sans_Arabic/NotoSansArabic-Regular.ttf.asset");
            if (latin == null || arabic == null)
                throw new System.InvalidOperationException("TextShapingScene requires the NowUI source project's Latin and Arabic fonts.");
            Now.Text(new NowRect(view.x + 24, view.y + 20, view.width - 48, 50), latin)
                .SetFontSize(24).SetColor(Color.white).Draw("Native C# typography");
            Now.Text(new NowRect(view.x + 24, view.y + 88, view.width - 48, 70), latin)
                .SetFontSize(40).SetColor(Color.white).Draw("office  ffi  AV  e\u0301");
            Now.Text(new NowRect(view.x + 24, view.y + 173, view.width - 48, 110), arabic)
                .SetFontSize(48).SetColor(new Color(.79f, .93f, .55f, 1)).Draw("\u0633\u0644\u0627\u0645");
        }
    }
}
