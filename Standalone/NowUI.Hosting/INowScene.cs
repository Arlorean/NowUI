namespace NowUI.Hosting
{
    /// <summary>A C# scene drawn by a host inside an active NowUI frame.</summary>
    /// <remarks>
    /// Store application state in the implementing object and draw using Now or NowLayout.
    /// The host owns frame timing, input and rendering. Implement IDisposable separately when the scene owns resources.
    /// </remarks>
    public interface INowScene
    {
        void Draw(NowRect view);
    }
}
