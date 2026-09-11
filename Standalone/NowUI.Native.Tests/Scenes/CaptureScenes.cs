using System;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.Tests.Scenes
{
    public sealed class TransparentScene : INowScene
    {
        public void Draw(NowRect view)
        {
            Now.Rectangle(new NowRect(0, 0, view.width / 2, view.height / 2))
                .SetColor(new Color(1, 0, 0, .5f)).Draw();
        }
    }

    public sealed class ClockScene : INowScene
    {
        public void Draw(NowRect view)
        {
            Now.Rectangle(view).SetColor(new Color(Time.time, 0, 0, 1)).Draw();
        }
    }

    public sealed class ErrorScene : INowScene
    {
        public void Draw(NowRect view) { Debug.LogError("Deliberate capture error"); }
    }

    public sealed class ThrowScene : INowScene
    {
        public void Draw(NowRect view) { throw new InvalidOperationException("Deliberate draw failure"); }
    }

    public sealed class DisposeScene : INowScene, IDisposable
    {
        public void Draw(NowRect view) { Now.Rectangle(view).SetColor(Color.red).Draw(); }
        public void Dispose() { throw new InvalidOperationException("Deliberate disposal failure"); }
    }

    public sealed class DisposeErrorScene : INowScene, IDisposable
    {
        public void Draw(NowRect view) { Now.Rectangle(view).SetColor(Color.red).Draw(); }
        public void Dispose() { Debug.LogError("Deliberate disposal error"); }
    }
}
