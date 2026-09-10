using System;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.Tests.Scenes
{
    /// <summary>Copies from the currently bound target, then proves CopyTexture restored that binding.</summary>
    public sealed class CopyScene : INowScene, IDisposable
    {
        readonly RenderTexture source = new(32, 32, 0, RenderTextureFormat.ARGB32);
        readonly RenderTexture destination = new(32, 32, 0, RenderTextureFormat.ARGB32);

        public CopyScene()
        {
            source.Create();
            destination.Create();
        }

        public void Draw(NowRect view)
        {
            Graphics.SetRenderTarget(source);
            GL.Clear(false, true, Color.red);
            Graphics.CopyTexture(source, destination);
            // CopyTexture leaves source bound. This must change source, while destination remains red.
            GL.Clear(false, true, Color.blue);
            Graphics.Blit(destination, (RenderTexture)null);
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(destination);
        }
    }
}
