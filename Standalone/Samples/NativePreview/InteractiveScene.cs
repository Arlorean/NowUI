using System;
using NowUI.Hosting;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Native adapter for the portable, interactive Fieldnotes example.</summary>
    public sealed class InteractiveScene : INowScene, IDisposable
    {
        public InteractiveContent Content { get; } = new InteractiveContent();

        public void Draw(NowRect view) => Content.Draw(view);

        public void Dispose() => Content.Dispose();
    }
}
