using NowUI.Browser;
using NUnit.Framework;

namespace NowUI.Browser.Tests;

public sealed class BrowserResourceTests
{
    [Test]
    public void AliasReaderRetainsCaseSensitivePathsEscapingAndLastDuplicateValue()
    {
        var aliases = BrowserResources.ReadAliases("""
            {"Assets/A.asset":"old.asset", "Assets/a.asset":"lower.asset",
             "Assets/A.asset":"Packages/example/\u00e9.asset"}
            """);
        Assert.That(aliases.Count, Is.EqualTo(2));
        Assert.That(aliases["Assets/A.asset"], Is.EqualTo("Packages/example/é.asset"));
        Assert.That(aliases["Assets/a.asset"], Is.EqualTo("lower.asset"));
    }

    [TestCase("null")]
    [TestCase("[]")]
    [TestCase("{\"Assets/A\":null}")]
    [TestCase("{\"Assets/A\":1}")]
    public void AliasReaderRejectsUnexpectedShapes(string json) =>
        Assert.Throws<InvalidDataException>(() => BrowserResources.ReadAliases(json));
}
