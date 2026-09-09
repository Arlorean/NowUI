using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using NowUI.Editor.Web;

/// <summary>
/// The Web Preview's capture receiver, checked as the pure functions it was written as.
/// </summary>
/// <remarks>
/// No socket is opened here, on purpose and for the same reason <c>NowWebPreviewServer.Init</c> refuses to bind in
/// batch mode: binding a port inside a 1,900-test EditMode run is how a suite acquires an intermittent failure. The
/// interesting decisions - what a capture may be called, what it may contain, how big it may be, and that it lands
/// inside the folder the Editor chose - are all reachable without one.
/// </remarks>
public class NowWebPreviewCaptureTests
{
    private string m_Root;

    [SetUp]
    public void SetUp()
    {
        m_Root = Path.Combine(Path.GetTempPath(), "NowUICaptureTests-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(m_Root)) Directory.Delete(m_Root, true); }
        catch (IOException) { }
    }

    // -------------------------------------------------------------------------------------------------- names

    [Test]
    public void SafeNameAcceptsLettersDigitsDashAndUnderscore()
    {
        Assert.IsTrue(NowWebPreviewCapture.IsSafeName("login"));
        Assert.IsTrue(NowWebPreviewCapture.IsSafeName("login-flow_2"));
        Assert.IsTrue(NowWebPreviewCapture.IsSafeName("A"));
    }

    /// <summary>
    /// Every spelling of "leave the folder" the alphabet has to exclude, plus the ones that are not traversal at
    /// all but would still put a file somewhere nobody asked for.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("..")]
    [TestCase("../escape")]
    [TestCase("..\\escape")]
    [TestCase("sub/dir")]
    [TestCase("sub\\dir")]
    [TestCase("C:")]
    [TestCase("C:/absolute")]
    [TestCase("/absolute")]
    [TestCase("\\\\server\\share")]
    [TestCase("file.png")]
    [TestCase("stream:hidden")]
    [TestCase("with space")]
    [TestCase("tab\there")]
    [TestCase("-")]
    [TestCase("--")]
    [TestCase("_")]
    public void SafeNameRefusesAnythingThatIsNotOneBareSegment(string name)
    {
        Assert.IsFalse(NowWebPreviewCapture.IsSafeName(name), "'" + name + "' should not be an acceptable name");
    }

    [Test]
    public void SafeNameRefusesAnOverlongName()
    {
        Assert.IsTrue(NowWebPreviewCapture.IsSafeName(new string('a', 64)));
        Assert.IsFalse(NowWebPreviewCapture.IsSafeName(new string('a', 65)));
    }

    // --------------------------------------------------------------------------------------------- extensions

    [TestCase("image/png", "png")]
    [TestCase("image/webp", "webp")]
    [TestCase("image/gif", "gif")]
    [TestCase("video/webm", "webm")]
    [TestCase("video/mp4", "mp4")]
    [TestCase("text/plain", "txt")]
    [TestCase("IMAGE/PNG", "png")]
    [TestCase("image/webp; charset=binary", "webp")]
    [TestCase("video/webm;codecs=vp9", "webm")]
    public void ExtensionComesFromTheContentTypeThroughAClosedList(string contentType, string expected)
    {
        Assert.AreEqual(expected, NowWebPreviewCapture.ExtensionFor(contentType));
    }

    /// <summary>
    /// The client never names the file, so the only way to write an executable or a Unity asset would be for this
    /// map to grow one. It cannot: an unknown type has no extension and the write is refused.
    /// </summary>
    [TestCase(null)]
    [TestCase("")]
    [TestCase("application/octet-stream")]
    [TestCase("text/html")]
    [TestCase("application/x-msdownload")]
    [TestCase("text/javascript")]
    [TestCase("image/svg+xml")]
    public void ExtensionIsRefusedForEverythingElse(string contentType)
    {
        Assert.IsNull(NowWebPreviewCapture.ExtensionFor(contentType));
    }

    // -------------------------------------------------------------------------------------------------- write

    [Test]
    public void WriteLandsInTheComputedFolderUnderTheServerChosenExtension()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("not really a png, but bytes are bytes");

        string written = NowWebPreviewCapture.Write(m_Root, "login", "image/png", bytes, out string error);

        Assert.IsNull(error);
        Assert.IsNotNull(written);
        Assert.AreEqual(Path.Combine(m_Root, "login.png"), written);
        Assert.IsTrue(File.Exists(written));
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(written));
        Assert.IsTrue(NowWebPreviewCapture.IsInside(m_Root, written));
    }

    [Test]
    public void WriteCreatesTheFolderItWasGiven()
    {
        Assert.IsFalse(Directory.Exists(m_Root));
        NowWebPreviewCapture.Write(m_Root, "first", "image/webp", new byte[] { 1, 2, 3 }, out string error);
        Assert.IsNull(error);
        Assert.IsTrue(Directory.Exists(m_Root));
    }

    [Test]
    public void WriteRefusesAnUnsafeNameAndTouchesNothing()
    {
        string written = NowWebPreviewCapture.Write(m_Root, "../escape", "image/png", new byte[] { 1 }, out string error);

        Assert.IsNull(written);
        Assert.IsNotNull(error);
        Assert.IsFalse(Directory.Exists(m_Root), "a refused capture must not even create the folder");
    }

    [Test]
    public void WriteRefusesAContentTypeItDoesNotUnderstand()
    {
        string written = NowWebPreviewCapture.Write(m_Root, "payload", "application/x-msdownload",
            new byte[] { 1 }, out string error);

        Assert.IsNull(written);
        StringAssert.Contains("application/x-msdownload", error);
    }

    [Test]
    public void WriteRefusesAnEmptyCapture()
    {
        Assert.IsNull(NowWebPreviewCapture.Write(m_Root, "empty", "image/png", new byte[0], out string error));
        Assert.IsNotNull(error);

        Assert.IsNull(NowWebPreviewCapture.Write(m_Root, "empty", "image/png", null, out error));
        Assert.IsNotNull(error);
    }

    [Test]
    public void WriteRefusesAnythingOverTheCap()
    {
        // One byte over, rather than a realistic size: the cap is the assertion, not the allocation.
        var oversized = new byte[NowWebPreviewCapture.MaxBytes + 1];

        string written = NowWebPreviewCapture.Write(m_Root, "huge", "image/webp", oversized, out string error);

        Assert.IsNull(written);
        StringAssert.Contains(NowWebPreviewCapture.MaxBytes.ToString(), error);
    }

    [Test]
    public void WriteReplacesAnEarlierCaptureOfTheSameName()
    {
        NowWebPreviewCapture.Write(m_Root, "shot", "image/png", new byte[] { 1, 1, 1 }, out string _);
        string second = NowWebPreviewCapture.Write(m_Root, "shot", "image/png", new byte[] { 2, 2 }, out string error);

        Assert.IsNull(error);
        CollectionAssert.AreEqual(new byte[] { 2, 2 }, File.ReadAllBytes(second));
    }

    /// <summary>
    /// The report that lands INSTEAD of an image when the browser tab was not drawing. It shares the name and the
    /// folder so that whoever went looking for the picture finds the reason in its place.
    /// </summary>
    [Test]
    public void ANotDrawingReportIsWrittenAsTextBesideWhereTheImageWouldHaveGone()
    {
        string written = NowWebPreviewCapture.Write(m_Root, "login", "text/plain",
            Encoding.UTF8.GetBytes("the tab was not drawing"), out string error);

        Assert.IsNull(error);
        Assert.AreEqual(Path.Combine(m_Root, "login.txt"), written);
    }

    // ---------------------------------------------------------------------------------------------- boundary

    [Test]
    public void IsInsideAcceptsTheRootAndItsChildrenAndRefusesASibling()
    {
        string root = Path.Combine(m_Root, "captures");

        Assert.IsTrue(NowWebPreviewCapture.IsInside(root, root));
        Assert.IsTrue(NowWebPreviewCapture.IsInside(root, Path.Combine(root, "a.png")));
        Assert.IsFalse(NowWebPreviewCapture.IsInside(root, Path.Combine(m_Root, "captures-elsewhere")));
        Assert.IsFalse(NowWebPreviewCapture.IsInside(root, Path.Combine(root, "..", "escaped.png")));
    }

    // ------------------------------------------------------------------------------------------------- token

    [Test]
    public void TheTokenIsLongRandomHexAndStableWithinASession()
    {
        string token = NowWebPreviewCapture.Token;

        Assert.IsNotNull(token);
        Assert.AreEqual(48, token.Length, "24 random bytes, hex-encoded");
        StringAssert.IsMatch("^[0-9a-f]+$", token);
        Assert.AreEqual(token, NowWebPreviewCapture.Token, "the same page must be able to read it twice");
    }

    [Test]
    public void PreparingTheServerRollsANewToken()
    {
        string before = NowWebPreviewCapture.Token;
        NowWebPreviewCapture.Prepare();
        Assert.AreNotEqual(before, NowWebPreviewCapture.Token);
    }

    [Test]
    public void TheCapturesFolderSitsBesideTheUsersAppsAndOutsideTheAssetsTree()
    {
        string captures = NowWebPreviewCapture.CapturesRoot;

        Assert.AreEqual(NowWebPreviewPaths.UserRoot, Path.GetDirectoryName(captures));
        Assert.AreEqual(NowWebPreviewCapture.FolderName, Path.GetFileName(captures));
        Assert.IsFalse(NowWebPreviewCapture.IsInside(UnityEngine.Application.dataPath, captures),
            "captures under Assets/ would be an AssetDatabase import per file");
    }

    // ------------------------------------------------------------------- /assets/, the project-file route
    //
    // ui.image and ui.lottie name a URL, and the point of the route is that the URL can be a file already in the
    // project rather than something baked into the WebAssembly bundle. What is worth testing is the ALLOWLIST:
    // the route widens what a loopback socket will hand out, and the only thing keeping that widening honest is
    // the set of extensions it agrees to serve.

    [TestCase("art/logo.png")]
    [TestCase("Art/Sub Folder/photo.JPG")]
    [TestCase("x.jpeg")]
    [TestCase("x.gif")]
    [TestCase("x.webp")]
    [TestCase("x.bmp")]
    [TestCase("x.svg")]
    [TestCase("x.ico")]
    [TestCase("anim/tick.json")]
    [TestCase("anim/tick.lottie")]
    [TestCase("fonts/Inter.ttf")]
    [TestCase("fonts/Inter.woff2")]
    public void AssetsServesPicturesAnimationsAndFonts(string relative)
    {
        Assert.IsTrue(NowWebPreviewServer.IsServableAsset(relative),
            relative + " is something an image or a Lottie can be, so refusing it makes the route useless " +
            "for the case it exists for");
    }

    [TestCase("Scripts/PlayerController.cs")]
    [TestCase("art/logo.png.meta")]
    [TestCase("Scenes/Main.unity")]
    [TestCase("Settings/URP.asset")]
    [TestCase("Editor/Build.dll")]
    [TestCase("secrets.txt")]
    [TestCase("build.exe")]
    [TestCase("run.ps1")]
    [TestCase("noextension")]
    [TestCase("")]
    public void AssetsRefusesEverythingThatIsNotMedia(string relative)
    {
        Assert.IsFalse(NowWebPreviewServer.IsServableAsset(relative),
            relative + " is not a picture, an animation or a font, and serving a project's source tree over a " +
            "socket to earn an image is a trade nobody asked for");
    }

    /// <summary>
    /// The allowlist reads the LAST extension, so a source file cannot be dressed up as a picture by putting an
    /// image extension somewhere earlier in the name.
    /// </summary>
    [Test]
    public void AnImageExtensionInTheMiddleOfANameDoesNotServeTheFile()
    {
        Assert.IsFalse(NowWebPreviewServer.IsServableAsset("logo.png.cs"));
        Assert.IsFalse(NowWebPreviewServer.IsServableAsset("photo.jpg.meta"));
        Assert.IsTrue(NowWebPreviewServer.IsServableAsset("logo.cs.png"),
            "the last extension is the file's type, and a file genuinely named that way is still a picture");
    }
}
