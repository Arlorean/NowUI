using System;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Uses existing Unity project files directly. The host owns these cached assets.</summary>
    public sealed class AssetPreviewScene : INowScene
    {
        readonly Texture2D logo = Require<Texture2D>("Assets/Mockups/Google/google-logo.png");
        readonly NowThemeAsset dark = Require<NowThemeAsset>("Assets/NowUI/Assets/Themes/DefaultDark.asset");
        readonly NowFontAsset mono = Require<NowFontAsset>("Assets/NowUI/Assets/Fonts/JetBrainsMono/JetBrainsMono-Regular.ttf.asset");
        bool light;
        int count;

        public void Draw(NowRect view)
        {
            var theme = light && dark.counterpart != null ? dark.counterpart : dark;
            using (NowTheme.Scope(theme))
            {
                Now.Rectangle(view).SetColor(theme.GetColor(NowColorToken.Background)).Draw();
                theme.Text(new NowRect(32, 25, view.width - 64, 50), NowTextStyle.Title).Draw("Your assets, in the native preview");
                theme.Text(new NowRect(32, 79, view.width - 64, 30), NowTextStyle.Muted).Draw("Loaded directly from this Unity project.");
                var left = new NowRect(32, 140, 360, 360);
                var right = new NowRect(416, 140, view.width - 448, 360);
                theme.Rectangle(left, NowRectangleStyle.Surface).Draw();
                theme.Rectangle(right, NowRectangleStyle.Surface).Draw();
                theme.Text(new NowRect(56, 160, 310, 30), NowTextStyle.Heading).Draw("Project image");
                Now.Rectangle(new NowRect(56, 216, 312, 106)).SetTexture(logo).Draw();
                theme.Text(new NowRect(56, 353, 310, 65), NowTextStyle.Caption).Draw("Assets/Mockups/Google/\ngoogle-logo.png");
                theme.Text(new NowRect(440, 160, right.width - 48, 32), NowTextStyle.Heading).Draw("Project theme + font");
                Now.Text(new NowRect(440, 220, right.width - 48, 72)).SetFont(mono).SetFontSize(20)
                    .SetColor(theme.GetColor(NowColorToken.Text)).Draw("JetBrains Mono\nAa Bb 0123456789");
                theme.Text(new NowRect(440, 316, right.width - 48, 34), NowTextStyle.Caption).Draw("Embedded font bytes from the .ttf.asset");
                if (Now.Button(new NowRect(440, 378, 180, 42), light ? "Use dark theme" : "Use light theme").Draw()) light = !light;
                if (Now.Button(new NowRect(640, 378, Math.Max(100, right.width - 248), 42), "Click " + count).Draw()) count++;
                theme.Text(new NowRect(32, 535, view.width - 64, 35), NowTextStyle.Muted)
                    .Draw("The light/dark link is resolved from the theme's existing asset reference.");
            }
        }

        static T Require<T>(string path) where T : UnityEngine.Object => Resources.Load<T>(path)
            ?? throw new InvalidOperationException("Missing project asset: " + path);
    }
}
