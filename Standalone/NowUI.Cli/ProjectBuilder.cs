using System.Diagnostics;
using System.Text.Json;

namespace NowUI.Cli;

internal static class ProjectBuilder
{
    internal static string Build(RenderOptions options, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(options.Project)) throw new FileNotFoundException("C# project not found.", options.Project);
        string directory = Path.GetDirectoryName(options.Project)!;
        // MSBuild resolves custom output paths; never guess bin/Release/<framework>/Name.dll.
        string[] properties = ["msbuild", options.Project, "-nologo", $"-property:Configuration={options.Configuration}",
            $"-property:NowUIHome={AppContext.BaseDirectory}",
            "-getProperty:TargetPath,TargetFramework,TargetFrameworks"];
        using var document = JsonDocument.Parse(RunDotnet(directory, properties, echo: false, cancellationToken));
        var data = document.RootElement.GetProperty("Properties");
        string? framework = data.GetProperty("TargetFramework").GetString();
        if (framework != "net9.0")
            throw new InvalidOperationException($"The native scene project must target net9.0 (reported '{framework}'). " +
                "Use a small standalone project that compiles your portable drawing code.");
        string? target = data.GetProperty("TargetPath").GetString();
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("MSBuild did not return TargetPath.");
        target = Path.GetFullPath(target, directory);
        if (!options.NoBuild)
        {
            Console.Error.WriteLine($"Building {options.Project}");
            RunDotnet(directory, ["build", options.Project, "--configuration", options.Configuration,
                $"-property:NowUIHome={AppContext.BaseDirectory}",
                "--nologo", "--verbosity", "minimal"], echo: true, cancellationToken);
        }
        if (!File.Exists(target))
            throw new FileNotFoundException("The scene assembly is missing. Run without --no-build to build it.", target);
        return target;
    }

    internal static string RunDotnet(string directory, IEnumerable<string> arguments, bool echo, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet. Install the .NET 9 SDK.");
        var stdout = ReadOutput(process.StandardOutput, echo);
        var stderr = ReadOutput(process.StandardError, true);
        try { process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw;
        }
        string output = stdout.GetAwaiter().GetResult();
        string errors = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"dotnet exited with code {process.ExitCode}." +
                (echo ? " See the build diagnostics above." : Environment.NewLine + output));
        return output;
    }

    static async Task<string> ReadOutput(StreamReader reader, bool echo)
    {
        var result = new System.Text.StringBuilder();
        while (await reader.ReadLineAsync() is { } line)
        {
            result.AppendLine(line);
            if (echo) Console.Error.WriteLine(line);
        }
        return result.ToString();
    }
}
