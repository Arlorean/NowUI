using NowUI.Hosting;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Samples.NativePreview
{
    /// <summary>An interactive demo using original project assets and ordinary NowUI controls.</summary>
    public sealed class PlaygroundScene : INowScene
    {
        static readonly string[] Names = { "Grinning face", "Tears of joy", "Heart", "Heart eyes" };
        static readonly string[] Files = { "1f600", "1f602", "2764", "u1f63b" };
        static readonly string[] CardIds = { "choose-grinning", "choose-joy", "choose-heart", "choose-cat" };
        readonly NowLottieAsset[] animations = new NowLottieAsset[4];
        readonly NowThemeAsset dark = Resources.Load<NowThemeAsset>("Assets/NowUI/Assets/Themes/DefaultDark.asset");
        string caption = "A little room to play.";
        bool playing = true, light;
        int selected;
        float seconds, speed = 1, size = 1;
        Key capturedKey = Key.P;

        Color background, panel, ink, muted, line, accent;

        public PlaygroundScene()
        {
            for (int i = 0; i < animations.Length; i++)
                animations[i] = Resources.Load<NowLottieAsset>("Assets/NowUI/Assets/AnimatedEmoji/" + Files[i] + ".lottie");
        }

        public void Draw(NowRect view)
        {
            var theme = light && dark.counterpart != null ? dark.counterpart : dark;
            using var themeScope = NowTheme.Scope(theme);
            background = light ? Rgb(240, 239, 245) : Rgb(15, 17, 23);
            panel = light ? Color.white : Rgb(24, 27, 35);
            ink = light ? Rgb(30, 30, 45) : Rgb(243, 241, 250);
            muted = light ? Rgb(99, 98, 119) : Rgb(157, 161, 182);
            line = light ? Rgb(222, 220, 232) : Rgb(43, 47, 60);
            accent = light ? Rgb(112, 83, 218) : Rgb(185, 163, 255);
            Box(view, background);
            if (view.width < 850 || view.height < 650)
            {
                Label("Give the playground a little more room.", new NowRect(24, 24, view.width - 48, 40), 21, ink);
                Label("Resize this window to at least 850 × 650.", new NowRect(24, 74, view.width - 48, 32), 14, muted);
                return;
            }

            Label("motion room", new NowRect(28, 22, 240, 40), 28, ink);
            Label("Your assets. Your controls.", new NowRect(270, 28, 260, 30), 14, muted);
            Now.Switch(new NowRect(view.width - 338, 29, 135, 30), "Light theme").SetId("theme").Draw(ref light);
            if (Now.Button(new NowRect(view.width - 174, 22, 146, 42), playing ? "Pause" : "Play")
                .SetId("play").Draw()) playing = !playing;
            Box(new NowRect(28, 82, view.width - 56, 1), line);

            float bodyHeight = view.height - 154;
            var settings = new NowRect(28, 104, 264, bodyHeight);
            Box(settings, panel, 16);
            Label("Make it yours", new NowRect(48, 124, 224, 30), 18, ink);
            Label("CAPTION", new NowRect(48, 173, 224, 20), 11, muted);
            Now.TextField(new NowRect(48, 199, 224, 38), "caption").SetPlaceholder("Write something…").Draw(ref caption);
            Label("ANIMATION", new NowRect(48, 259, 224, 20), 11, muted);
            if (Now.Dropdown(new NowRect(48, 285, 224, 38), "animation", Names).Draw(ref selected)) seconds = 0;

            Label("PLAYBACK SPEED", new NowRect(48, 346, 164, 20), 11, muted);
            Now.Text(new NowRect(220, 343, 52, 24)).SetFontSize(14).SetColor(accent).Draw(speed, "0.00'×'");
            Now.Slider(new NowRect(48, 374, 224, 28), .25f, 2).SetId("speed").SetStep(.05f).Draw(ref speed);
            Label("SIZE", new NowRect(48, 429, 164, 20), 11, muted);
            Now.Text(new NowRect(220, 426, 52, 24)).SetFontSize(14).SetColor(accent).Draw(size, "0.00'×'");
            Now.Slider(new NowRect(48, 457, 224, 28), .5f, 1.25f).SetId("size").SetStep(.05f).Draw(ref size);

            if (view.height >= 735)
            {
                Label("TRY KEY CAPTURE", new NowRect(48, 513, 224, 20), 11, muted);
                Now.KeyBindingField(new NowRect(48, 541, 224, 38), "captured-key").Draw(ref capturedKey);
                Label("Click the field, then press a key.\nTab moves between controls.", new NowRect(48, 592, 224, 50), 12, muted);
            }

            if (playing) seconds += Mathf.Max(0, Time.deltaTime) * speed;
            float duration = Mathf.Max(.001f, animations[selected].duration);
            seconds = playing ? Mathf.Repeat(seconds, duration) : Mathf.Clamp(seconds, 0, duration);
            float stageX = 312, stageWidth = view.width - stageX - 28;
            float galleryY = view.height - 230;
            var stage = new NowRect(stageX, 104, stageWidth, galleryY - 120);
            Box(stage, panel, 16);
            Label(Names[selected], new NowRect(stage.x + 24, stage.y + 17, stage.width - 160, 22), 12, accent);
            Label(caption, new NowRect(stage.x + 24, stage.y + 48, stage.width - 48, 40), 25, ink);
            float diameter = Mathf.Min(stage.width - 96, stage.height - 153) * size;
            var art = new NowRect(stage.center.x - diameter * .5f, stage.y + 87 + (stage.height - 153 - diameter) * .5f, diameter, diameter);
            using (Now.Mask(new NowRect(stage.x + 16, stage.y + 87, stage.width - 32, stage.height - 145)))
                Now.Lottie(art, animations[selected]).SetTime(seconds).SetLoop(false).SetPlaybackFrameRate(60).Draw();

            float position = seconds / duration;
            if (Now.Slider(new NowRect(stage.x + 24, stage.yMax - 49, stage.width - 166, 28), 0, 1)
                .SetId("timeline").Draw(ref position))
            {
                seconds = position * duration;
                playing = false;
            }
            if (Now.Button(new NowRect(stage.xMax - 122, stage.yMax - 52, 98, 34), "Restart")
                .SetId("restart").Draw()) { seconds = 0; playing = true; }

            float cardWidth = (stageWidth - 36) / 4;
            for (int i = 0; i < animations.Length; i++)
            {
                var card = new NowRect(stageX + i * (cardWidth + 12), galleryY, cardWidth, 180);
                Box(card, selected == i ? accent : panel, 14);
                Box(card.Inset(1), panel, 13);
                float thumb = Mathf.Min(88, cardWidth - 32);
                var thumbRect = new NowRect(card.center.x - thumb * .5f, card.y + 16, thumb, thumb);
                Now.Lottie(thumbRect, animations[i]).SetTime(seconds).SetPlaybackFrameRate(30).Draw();
                if (Now.Button(new NowRect(card.x + 10, card.yMax - 54, cardWidth - 20, 38), Names[i])
                    .SetId(CardIds[i]).Draw()) { selected = i; seconds = 0; }
            }
            Label("Drag the timeline to freeze a moment. Choose another animation below.",
                new NowRect(28, view.height - 32, view.width - 56, 22), 12, muted);
        }

        static Color Rgb(byte r, byte g, byte b) => new Color32(r, g, b, 255);
        static void Box(NowRect rect, Color color, float radius = 0) => Now.Rectangle(rect).SetColor(color).SetRadius(radius).Draw();
        static void Label(string text, NowRect rect, float size, Color color) => Now.Text(rect).SetFontSize(size).SetColor(color).Draw(text);
    }
}
