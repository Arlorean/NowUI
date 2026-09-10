using NowUI.Hosting;

namespace NowUI.Samples.NativePreview
{
    /// <summary>Native adapter for the portable pixel-alignment diagnostic.</summary>
    public sealed class PixelAlignmentScene : INowScene
    {
        public void Draw(NowRect view) => PixelAlignmentContent.Draw(view);
    }
}
