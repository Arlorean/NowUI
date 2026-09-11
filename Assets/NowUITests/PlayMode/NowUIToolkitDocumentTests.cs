#if NOWUI_UITOOLKIT
using System.Collections;
using NowUI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

/// <summary>
/// Runtime UIDocument reproductions of the "percent-sized host draws nothing"
/// report. Whether a 100% x 100% host draws depends entirely on whether the
/// UIDocument root has a definite height: Unity's default runtime theme makes
/// the root absolute and panel-filling, while a panel without that theme
/// leaves the root shrink-wrapping its children.
/// </summary>
public class NowUIToolkitDocumentTests
{
    GameObject _go;
    PanelSettings _settings;

    [TearDown]
    public void TearDown()
    {
        if (_go != null)
            Object.Destroy(_go);
        if (_settings != null)
            Object.Destroy(_settings);
    }

    UIDocument CreateDocument()
    {
        _settings = ScriptableObject.CreateInstance<PanelSettings>();
        _settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        _go = new GameObject("NowUI UIDocument probe");
        var document = _go.AddComponent<UIDocument>();
        document.panelSettings = _settings;
        return document;
    }

    static NowLayoutVisualElement CreatePercentHost(out System.Func<(int draws, int measures, NowRect drawn)> report)
    {
        var element = new NowLayoutVisualElement();
        element.style.width = Length.Percent(100f);
        element.style.height = Length.Percent(100f);
        int draws = 0;
        int measures = 0;
        NowRect drawn = default;
        element.rebuildNowUI += (_, rect) =>
        {
            if (NowLayout.isMeasurePass)
            {
                ++measures;
                return;
            }

            ++draws;
            drawn = rect;
            Now.Rectangle(rect).SetColor(Color.red).Draw();
        };
        report = () => (draws, measures, drawn);
        return element;
    }

    static IEnumerator Settle(VisualElement element, int frames)
    {
        for (int i = 0; i < frames; ++i)
        {
            yield return null;
            if (element.layout.width > 0f && element.layout.height > 0f)
                yield break;
        }
    }

    [UnityTest]
    public IEnumerator PlainVisualElementWithPercentHeightCollapsesUnderAnUnthemedDocumentRoot()
    {
        var document = CreateDocument();
        yield return null;

        var plain = new VisualElement();
        plain.style.width = Length.Percent(100f);
        plain.style.height = Length.Percent(100f);
        plain.style.backgroundColor = Color.red;
        document.rootVisualElement.Add(plain);

        yield return Settle(plain, 10);
        yield return null;

        Assert.AreEqual(Screen.width, plain.layout.width, 0.5f, "The column root still stretches children horizontally.");
        Assert.AreEqual(0f, plain.layout.height, 0.5f,
            "Without the runtime theme the UIDocument root has no height, so a plain element collapses exactly like the NowUI host does.");
    }

    [UnityTest]
    public IEnumerator PercentSizedHostCollapsesThroughMeasurePassesUnderAnUnthemedDocumentRoot()
    {
        var document = CreateDocument();
        yield return null;

        var element = CreatePercentHost(out var report);
        document.rootVisualElement.Add(element);

        yield return Settle(element, 10);
        yield return null;

        (int draws, int measures, NowRect _) = report();
        Assert.AreEqual(0f, element.layout.height, 0.5f, "100% of an indefinite root is auto, and explicit-rect content measures to zero.");
        Assert.AreEqual(0, draws, "A collapsed host never reaches a draw pass.");
        Assert.Greater(measures, 0, "The measure passes are the only callbacks the author sees, which is the reported symptom.");
    }

    [UnityTest]
    public IEnumerator PercentSizedHostFillsThePanelOnceTheDocumentRootGrows()
    {
        var document = CreateDocument();
        yield return null;
        document.rootVisualElement.style.flexGrow = 1f;

        var element = CreatePercentHost(out var report);
        document.rootVisualElement.Add(element);

        yield return Settle(element, 10);
        yield return null;

        (int draws, int _, NowRect drawn) = report();
        Assert.AreEqual(Screen.height, document.rootVisualElement.layout.height, 0.5f, "flex-grow: 1 on the document root fills the panel.");
        Assert.AreEqual(Screen.height, element.layout.height, 0.5f, "100% height then resolves against the root.");
        Assert.Greater(draws, 0, "The host reaches a real draw pass.");
        Assert.AreEqual(Screen.height, drawn.height, 0.5f, "The draw pass receives the full-height rect.");
    }

    [UnityTest]
    public IEnumerator PercentHeightUnderAutoHeightContainerCollapses()
    {
        var document = CreateDocument();
        yield return null;
        document.rootVisualElement.style.flexGrow = 1f;

        var container = new VisualElement();
        document.rootVisualElement.Add(container);

        var element = CreatePercentHost(out var report);
        container.Add(element);

        yield return Settle(element, 10);
        yield return null;

        (int draws, int measures, NowRect _) = report();
        Assert.AreEqual(0f, element.layout.height, 0.5f, "An auto-height container between the root and the host collapses it the same way.");
        Assert.AreEqual(0, draws);
        Assert.Greater(measures, 0);
    }

#if UNITY_EDITOR
    const string ThemeProbePath = "Assets/NowUITests/PlayMode/NowUIThemeProbe.tss";

    [UnityTest]
    public IEnumerator DefaultRuntimeThemeSizesTheDocumentRootSoPercentHostsFillThePanel()
    {
        System.IO.File.WriteAllText(ThemeProbePath, "@import url(\"unity-theme://default\");" + System.Environment.NewLine);
        UnityEditor.AssetDatabase.ImportAsset(ThemeProbePath, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        var theme = UnityEditor.AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemeProbePath);

        try
        {
            Assert.IsNotNull(theme, "The default runtime theme probe must import.");
            var document = CreateDocument();
            _settings.themeStyleSheet = theme;
            yield return null;

            var element = CreatePercentHost(out var report);
            document.rootVisualElement.Add(element);

            yield return Settle(element, 10);
            yield return null;

            VisualElement root = document.rootVisualElement;
            (int draws, int _, NowRect drawn) = report();
            Assert.AreEqual(Position.Absolute, root.resolvedStyle.position, "The theme's unity-ui-document__root rule makes the root absolute.");
            Assert.AreEqual(Screen.height, root.layout.height, 0.5f, "The themed root fills the panel.");
            Assert.AreEqual(Screen.height, element.layout.height, 0.5f, "A 100% host under a themed root fills it.");
            Assert.Greater(draws, 0, "The host reaches a real draw pass.");
            Assert.AreEqual(Screen.height, drawn.height, 0.5f, "The draw pass receives the full-height rect.");
        }
        finally
        {
            UnityEditor.AssetDatabase.DeleteAsset(ThemeProbePath);
        }
    }
#endif
}
#endif
