using NUnit.Framework;
using NowUI;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class NowGlassImmediateRenderingTests
{
    [Test]
    public void StartUIKeepsBlurredBackdropEnabledForFinalPaneDraw()
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            Assert.Ignore("The immediate glass regression needs a graphics device.");

        const int width = 128, height = 96;
        var previous = RenderTexture.active;
        var previousMask = Now.screenMask;
        float previousScale = Now.uiScale;
        var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        Texture2D readback = null;
        try
        {
            Assert.IsTrue(target.Create());
            RenderTexture.active = target;
            GL.Clear(false, true, Color.clear);
            using (Now.StartUI(new NowRect(0, 0, width, height)))
            {
                for (int x = 0; x < width; x += 8)
                    Now.Rectangle(new NowRect(x, 0, 8, height)).SetColor(x % 16 == 0 ? Color.white : Color.black).Draw();
                Now.Glass(new NowRect(24, 16, 80, 64)).SetBlurRadius(12).SetBlurQuality(NowGlassBlurQuality.High)
                    .SetTint(Color.clear).SetVibrancy(1, 1).SetRadius(0).Draw();
                Now.Rectangle(new NowRect(56, 40, 16, 16)).SetColor(Color.red).Draw();
            }

            readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readback.Apply(false, false);
            Color gray = readback.GetPixel(48, height - 1 - 32);
            Assert.That(gray.r, Is.InRange(.25f, .85f), "A sharp white stripe means the final pane disabled its blurred backdrop.");
            Assert.That(gray.g, Is.EqualTo(gray.r).Within(.02f));
            Assert.That(gray.b, Is.EqualTo(gray.r).Within(.02f));
            Assert.That(readback.GetPixel(4, 48).r, Is.GreaterThan(.95f), "The exterior stripe stays sharp.");
            Assert.That(readback.GetPixel(12, 48).r, Is.LessThan(.05f));
            Color foreground = readback.GetPixel(64, 48);
            Assert.That(foreground.r, Is.GreaterThan(.95f));
            Assert.That(foreground.g, Is.LessThan(.05f), "Later foreground draws stay sharp.");
            Assert.That(Shader.GetGlobalFloat("_NowGlassUseBackdrop"), Is.Zero, "Glass must restore its global state after drawing.");
        }
        finally
        {
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            if (readback != null) Object.DestroyImmediate(readback);
            Now.screenMask = previousMask;
            Now.SetUIScale(previousScale);
        }
    }
}
