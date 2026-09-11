using System;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.Tests.Scenes;

public sealed class RemoteLottieScene : INowScene
{
    readonly string url = Environment.GetEnvironmentVariable("NOWUI_TEST_LOTTIE_URL") ?? throw new InvalidOperationException("Missing test Lottie URL.");
    public void Draw(NowRect view)
    {
        Now.Rectangle(view).SetColor(Color.black).Draw();
        var asset = NowLottieCache.GetAsset(url);
        if (asset != null) Now.Lottie(view.Inset(10), asset).SetTime(Time.time).Draw();
    }
}

public sealed class OnPressRemoteLottieScene : INowScene
{
    readonly RemoteLottieScene remote = new();
    bool pressed;
    public void Draw(NowRect view)
    {
        if (NowInput.defaultProvider.TryGetSnapshot(new NowInputSurface(new Vector2(view.width, view.height)), out var input))
            pressed |= input.primaryPressed;
        if (pressed) remote.Draw(view);
        else Now.Rectangle(view).SetColor(Color.black).Draw();
    }
}

public sealed class FrameCountScene : INowScene
{
    public void Draw(NowRect view) => Now.Rectangle(view).SetColor(new Color32((byte)Time.frameCount, 0, 0, 255)).Draw();
}
