using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Real project Lottie assets, using the same vector parser and drawing API as Unity.</summary>
    public sealed class LottiePreviewScene : INowScene
    {
        static readonly string[] Files = { "1f600", "1f602", "2764", "u1f63b" };
        static readonly string[] Labels = { "Grinning face", "Tears of joy", "Heart", "Heart eyes" };
        readonly NowLottieAsset[] animations = new NowLottieAsset[4];
        readonly NowThemeAsset theme = Resources.Load<NowThemeAsset>("Assets/NowUI/Assets/Themes/DefaultDark.asset");
        bool playing = true;
        float seconds;

        public void Draw(NowRect view)
        {
            using var themeScope = NowTheme.Scope(theme);
            Now.Rectangle(view).SetColor(new Color(.055f, .065f, .085f)).Draw();
            Now.Text(new NowRect(28, 22, view.width - 220, 42)).SetFontSize(28).SetColor(Color.white).Draw("Your Lottie assets, live in C#");
            Now.Switch(new NowRect(view.width - 180, 26, 150, 30), "Animate").SetId("playing").Draw(ref playing);
            Now.Text(new NowRect(28, 72, view.width - 56, 26)).SetFontSize(13).SetColor(new Color(.65f, .7f, .8f))
                .Draw("Original Unity project files • vector geometry • no export or copied assets");
            if (playing) seconds += Mathf.Max(0, Time.deltaTime);
            float width = (view.width - 80) / 4f;
            for (int i = 0; i < animations.Length; i++)
            {
                if (animations[i] == null)
                    animations[i] = Resources.Load<NowLottieAsset>("Assets/NowUI/Assets/AnimatedEmoji/" + Files[i] + ".lottie");
                float x = 28 + i * (width + 8);
                Now.Rectangle(new NowRect(x, 122, width, view.height - 148)).SetRadius(16).SetColor(new Color(.09f, .105f, .135f)).Draw();
                Now.Lottie(new NowRect(x + 16, 134, width - 32, view.height - 206), animations[i])
                    .SetTime(seconds).SetPlaybackFrameRate(30).Draw();
                Now.Text(new NowRect(x + 16, view.height - 65, width - 32, 24)).SetFontSize(14).SetColor(Color.white).Draw(Labels[i]);
            }
        }
    }
}
