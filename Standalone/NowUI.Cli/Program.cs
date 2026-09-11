using System.Reflection;

namespace NowUI.Cli;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args is ["--help"] or ["-h"] || args.Length == 2 && args[1] == "--help")
        {
            Console.WriteLine(RenderOptions.Help + Environment.NewLine + BrowserOptions.Help);
            return 0;
        }
        try
        {
            if (args[0] == "init")
            {
                if (args.Length != 2) throw new ArgumentException("Expected: nowui init <directory>.");
                SceneScaffold.Create(args[1]);
                return 0;
            }
            if (args[0] == "serve")
            {
                BrowserRunner.Serve(args);
                return 0;
            }
            if (BrowserOptions.IsRequested(args))
            {
                BrowserRunner.Run(BrowserOptions.Parse(args));
                return 0;
            }
            var options = RenderOptions.Parse(args);
            string path = ProjectBuilder.Build(options);
            using var loaded = LoadedScene.Create(path, options.Scene);
            if (options.Preview) PreviewRunner.Run(options, loaded);
            else CaptureRunner.Run(options, loaded);
            return 0;
        }
        catch (Exception exception)
        {
            while (exception is TargetInvocationException { InnerException: not null }) exception = exception.InnerException;
            Console.Error.WriteLine("NowUI: " + exception.Message);
            return exception is ArgumentException ? 2 : 1;
        }
        finally { LoadedScene.Cleanup(); }
    }

    internal static void WriteOutput(string output, byte[] png)
    {
        string directory = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".nowui-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temporary, png);
            File.Move(temporary, output, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
