using System.Xml.Linq;
using NowUI.Engine;
using NowUI.Hosting;

namespace NowUI.Cli;

internal static class SceneScaffold
{
    internal static string Create(string destination)
    {
        destination = Path.GetFullPath(destination);
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            throw new ArgumentException("init requires a new or empty directory; existing files are never replaced.");
        Directory.CreateDirectory(destination);
        string project = Path.Combine(destination, "Preview.csproj");
        var items = new XElement("ItemGroup");
        foreach (var assembly in new[] { typeof(INowScene).Assembly, typeof(Now).Assembly, typeof(NowRuntime).Assembly })
            items.Add(new XElement("Reference", new XAttribute("Include", assembly.GetName().Name!),
                new XElement("HintPath", "$(NowUIHome)/" + Path.GetFileName(assembly.Location)), new XElement("Private", "false")));
        foreach (string extension in Directory.EnumerateFiles(AppContext.BaseDirectory, "NowUI.Extensions.*.dll").OrderBy(p => p, StringComparer.Ordinal))
            items.Add(new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(extension)),
                new XElement("HintPath", "$(NowUIHome)/" + Path.GetFileName(extension)), new XElement("Private", "true")));
        var document = new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("TargetFramework", "net9.0"),
                new XElement("EnableDynamicLoading", "true"), new XElement("ImplicitUsings", "enable"),
                new XElement("Nullable", "enable"), new XElement("NowUIHome", new XAttribute("Condition", "'$(NowUIHome)' == ''"),
                    Path.GetDirectoryName(typeof(INowScene).Assembly.Location))), items));
        document.Save(project);
        File.WriteAllText(Path.Combine(destination, "Scene.cs"), """
            using NowUI;
            using NowUI.Hosting;
            using UnityEngine;

            public sealed class PreviewScene : INowScene
            {
                private int clicks;

                public void Draw(NowRect view)
                {
                    Now.Rectangle(view).SetColor(new Color(.055f, .065f, .08f)).Draw();
                    Now.Text(new NowRect(32, 32, view.width - 64, 48))
                        .SetFontSize(28).SetColor(Color.white).Draw("Your NowUI preview");
                    if (Now.Button(new NowRect(32, 112, 220, 44), $"Clicked {clicks} times").Draw()) clicks++;
                }
            }
            """);
        File.WriteAllText(Path.Combine(destination, ".gitignore"), "bin/\nobj/\n");
        Console.WriteLine(project);
        Console.WriteLine($"Ready: nowui preview \"{project}\"");
        return project;
    }
}
