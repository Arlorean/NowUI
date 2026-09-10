namespace NowUI.Cli;

internal sealed record BrowserOptions(string Project, string? Output, string? Scene, string? UnityProject,
    string Configuration, bool NoBuild, bool Preview, bool Aot, bool AllAssets, string Title, int Port, bool NoOpen,
    bool NativeSymbols = false)
{
    internal const string Help = """

        Optional browser target (same C# INowScene):
          nowui publish <project.csproj> --target web --output <new-directory> [options]
          nowui preview <project.csproj> --target web [options]
          nowui serve <site-directory> [--port <number>] [--no-open]

          --scene, --unity-project, --configuration, --no-build retain their usual meaning.
          --title <text>             Browser page title (default NowUI)
          --aot                      Compile C# ahead of time; longer build, larger download; benchmark speed
          --all-assets               Include all supported assets for fully computed asset paths
          --native-symbols           Include native function names for browser diagnostics
          --port <number>            Preview port, 0..65535 (default 0: choose a free port)
          --no-open                  Preview: print URL without opening a browser

        Browser publishing requires the .NET 9 SDK and its wasm-tools workload.
        Output is a static site; serve over HTTP(S). Native preview remains the default.
        """;

    internal static bool IsRequested(string[] args) => args.Length > 0 &&
        (args[0] == "publish" || args.Contains("--target", StringComparer.Ordinal));

    internal static BrowserOptions Parse(string[] args)
    {
        if (args.Length < 2 || args[0] is not ("publish" or "preview") || args[1].StartsWith('-'))
            throw new ArgumentException("Expected: nowui publish|preview <project.csproj> --target web.");
        string project = Path.GetFullPath(args[1]);
        if (!Path.GetExtension(project).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The input must be a C# .csproj file.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 2; i < args.Length; i++)
        {
            string key = args[i];
            if (key is "--no-build" or "--aot" or "--all-assets" or "--no-open" or "--native-symbols")
            {
                if (!flags.Add(key)) throw new ArgumentException($"{key} was supplied twice.");
                continue;
            }
            if (key is not ("--target" or "--output" or "--scene" or "--unity-project" or "--configuration" or "--title" or "--port"))
                throw new ArgumentException($"Unknown browser option '{key}'. Use --help for options.");
            if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for {key}.");
            if (!values.TryAdd(key, args[i])) throw new ArgumentException($"{key} was supplied twice.");
        }
        if (values.GetValueOrDefault("--target") != "web")
            throw new ArgumentException("The optional browser target must be explicitly selected with --target web.");
        bool preview = args[0] == "preview";
        string? output = values.GetValueOrDefault("--output");
        if (preview && output != null) throw new ArgumentException("Use publish for a persistent --output directory.");
        if (!preview && string.IsNullOrWhiteSpace(output)) throw new ArgumentException("publish requires --output <new-directory>.");
        if (output != null)
        {
            output = Path.GetFullPath(output);
            if (Path.GetPathRoot(output) == output || Directory.Exists(output) || File.Exists(output))
                throw new ArgumentException("Browser --output must name a new directory; existing output is never replaced.");
        }
        string configuration = values.GetValueOrDefault("--configuration", "Release");
        if (configuration is not ("Debug" or "Release")) throw new ArgumentException("--configuration must be Debug or Release.");
        if (!int.TryParse(values.GetValueOrDefault("--port", "0"), out int port) || port < 0 || port > 65535)
            throw new ArgumentException("--port must be 0..65535.");
        if (!preview && (values.ContainsKey("--port") || flags.Contains("--no-open")))
            throw new ArgumentException("--port and --no-open are preview options.");
        string? unity = values.GetValueOrDefault("--unity-project");
        if (unity != null) unity = Path.GetFullPath(unity);
        return new(project, output, values.GetValueOrDefault("--scene"), unity, configuration,
            flags.Contains("--no-build"), preview, flags.Contains("--aot"), flags.Contains("--all-assets"),
            values.GetValueOrDefault("--title", "NowUI"), port, flags.Contains("--no-open"), flags.Contains("--native-symbols"));
    }
}
