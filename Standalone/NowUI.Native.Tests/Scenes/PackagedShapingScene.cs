using System;
using System.IO;
using System.Reflection;
using NowUI.Hosting;
using UnityEngine;

namespace NowUI.Native.TestScenes
{
    /// <summary>Run with an installed tool to prove its own Runtime can find the portable native payload.</summary>
    public sealed class PackagedShapingScene : INowScene
    {
        public void Draw(NowRect view)
        {
            var family = Resources.Load<NowFontAsset>("NowUI/NotoSans");
            if (family == null || !family.TryResolveFont(NowFontStyle.Regular, out var font))
                throw new InvalidOperationException("The installed tool did not load its font resources.");
            // Test-only reflection inspects the actual HarfBuzz substitution in
            // the installed Runtime assembly, rather than a test-local DLL.
            var shape = typeof(NowFont).GetMethod("TryGetShapedRun", BindingFlags.Instance | BindingFlags.NonPublic);
            object[] arguments = { "ffi", null };
            if (shape == null || !(bool)shape.Invoke(font, arguments) || ((Array)arguments[1]).Length != 1)
                throw new InvalidOperationException("The installed tool did not apply the native ffi ligature.");
            var resolver = typeof(NowFont).Assembly.GetType("NowUI.Internal.NativePluginResolver", true);
            string path = (string)resolver.GetMethod("LoadedPath", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "nowui-msdf" });
            if (string.IsNullOrEmpty(path) || !path.Contains(Path.DirectorySeparatorChar + "runtimes" + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("The installed tool bypassed its portable RID native payload.");
            Console.WriteLine("Native ffi ligature: 1 glyph; plugin: " + path);
            Now.Rectangle(view).SetColor(Color.black).Draw();
            Now.Text(new NowRect(view.x + 16, view.y + 16, view.width - 32, view.height - 32), family)
                .SetFontSize(32).SetColor(Color.white).Draw("office ffi AV e\u0301");
        }
    }
}
