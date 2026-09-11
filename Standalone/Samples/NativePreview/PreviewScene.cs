using NowUI.Hosting;

namespace NowUI.Samples.NativePreview
{
    /// <summary>The native host adapter; the drawing itself has no host dependency.</summary>
    public sealed class PreviewScene : INowScene
    {
        public void Draw(NowRect view) => PreviewContent.Draw(view);
    }
}
