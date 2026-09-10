using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.Tests.Scenes;

public sealed class ReplayProbeScene : INowScene
{
    private int clicks;
    private string text = "";
    private bool scrolled;
    public void Draw(NowRect view)
    {
        Now.Rectangle(view).SetColor(Color.black).Draw();
        if (Now.Button(new NowRect(10, 10, 180, 40), "Click").SetId("button").Draw()) clicks++;
        Now.TextField(new NowRect(10, 65, 180, 40), "text").Draw(ref text);
        if (NowInput.defaultProvider.TryGetSnapshot(new NowInputSurface(new Vector2(view.width, view.height)), out var snapshot)
            && snapshot.scrollDelta.y != 0) scrolled = true;
        Now.Rectangle(new NowRect(10, 125, 40, 40)).SetColor(clicks == 1 ? Color.green : Color.red).Draw();
        Now.Rectangle(new NowRect(70, 125, 40, 40)).SetColor(text == "NowUI" ? Color.green : Color.red).Draw();
        Now.Rectangle(new NowRect(130, 125, 40, 40)).SetColor(scrolled ? Color.green : Color.red).Draw();
    }
}
