using System.Runtime.InteropServices;
using NowUI.Internal;
using NUnit.Framework;

namespace NowUI.Native.Tests;

public sealed class NativePluginResolverTests
{
    [TestCase("windows", Architecture.X64, "win-x64")]
    [TestCase("linux", Architecture.X64, "linux-x64")]
    [TestCase("macos", Architecture.X64, "osx-x64")]
    [TestCase("macos", Architecture.Arm64, "osx-arm64")]
    [TestCase("windows", Architecture.Arm64, null)]
    [TestCase("linux", Architecture.Arm64, null)]
    public void MatchesProcessArchitectureRatherThanOperatingSystemArchitecture(string os, Architecture architecture, string? expected)
        => Assert.That(NativePluginResolver.SelectRid(os, architecture), Is.EqualTo(expected));

    [Test]
    public void SearchesApplicationThenRuntimeAssemblyFolderWithoutDependingOnWorkingDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "nowui-native-resolver-" + Guid.NewGuid().ToString("N"));
        string application = Path.Combine(root, "application");
        string assembly = Path.Combine(root, "assemblies");
        string relative = Path.Combine("runtimes", "win-x64", "native", "nowui-msdf.dll");
        string fromAssembly = Path.Combine(assembly, relative);
        string fromApplication = Path.Combine(application, relative);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fromAssembly)!);
            File.WriteAllBytes(fromAssembly, [0]);
            Assert.That(NativePluginResolver.FindLibrary(application, assembly, "win-x64", "nowui-msdf.dll"), Is.EqualTo(fromAssembly));
            Directory.CreateDirectory(Path.GetDirectoryName(fromApplication)!);
            File.WriteAllBytes(fromApplication, [0]);
            Assert.That(NativePluginResolver.FindLibrary(application, assembly, "win-x64", "nowui-msdf.dll"), Is.EqualTo(fromApplication));
            Assert.That(NativePluginResolver.FindLibrary(application, assembly, "linux-x64", "libnowui-msdf.so"), Is.Null);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
