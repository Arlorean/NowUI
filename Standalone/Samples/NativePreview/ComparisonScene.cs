using System;
using NowUI.Hosting;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Fixed state for comparing the same UI between the native and Unity renderers.</summary>
    public sealed class ComparisonScene : INowScene, IDisposable
    {
        readonly InteractiveContent content = new InteractiveContent { Animate = false };

        public void Draw(NowRect view) => content.Draw(view);

        public void Dispose() => content.Dispose();
    }
}
