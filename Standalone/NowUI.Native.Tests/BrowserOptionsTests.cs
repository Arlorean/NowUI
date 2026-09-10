using NowUI.Cli;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public class BrowserOptionsTests
{
    [Test]
    public void NativePreviewRemainsDefault()
    {
        Assert.That(BrowserOptions.IsRequested(["preview", "Scene.csproj"]), Is.False);
        Assert.That(RenderOptions.Parse(["preview", "Scene.csproj"]).Preview, Is.True);
    }

    [Test]
    public void BrowserIsExplicitAndPreservesSceneOptions()
    {
        var options = BrowserOptions.Parse(["preview", "Scene.csproj", "--target", "web", "--scene", "Demo",
            "--aot", "--all-assets", "--no-open", "--port", "12345"]);
        Assert.That(options.Preview && options.Aot && options.AllAssets && options.NoOpen, Is.True);
        Assert.That(options.Scene, Is.EqualTo("Demo"));
        Assert.That(options.Port, Is.EqualTo(12345));
    }

    [TestCase("--target", "native")]
    [TestCase("--port", "65536")]
    [TestCase("--color-space", "linear")]
    [TestCase("--configuration", "Surprise")]
    public void InvalidBrowserOptionsFail(string option, string value) => Assert.Throws<ArgumentException>(() =>
        BrowserOptions.Parse(["preview", "Scene.csproj", option, value]));

    [Test]
    public void PublishCannotReplaceAnExistingDirectory()
    {
        Assert.Throws<ArgumentException>(() => BrowserOptions.Parse(["publish", "Scene.csproj", "--target", "web",
            "--output", TestContext.CurrentContext.WorkDirectory]));
        Assert.Throws<ArgumentException>(() => BrowserOptions.Parse(["publish", "Scene.csproj", "--output", "new-site"]));
    }
}
