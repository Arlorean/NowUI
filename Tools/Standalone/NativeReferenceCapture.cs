// Staged into a unique Editor-only Assets assembly by Capture-NativeReference.ps1.
using System;
using System.IO;
using NowUI.Samples.NativePreview;
using UnityEditor;
using UnityEngine;

namespace NowUI
{
    public static class NativeReferenceCapture
    {
        [Serializable]
        sealed class Metadata
        {
            public string unityVersion, graphicsDevice, graphicsApi, colorSpace, fontAsset, fontTexture;
            public string source;
            public int width, height, msaa = 1, warmupFrames = 32;
            public float uiScale = 1;
            public bool animation = false, textShaping;
        }

        sealed class IdleInput : INowInputProvider
        {
            public int frame;
            public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot snapshot)
            {
                snapshot = default;
                snapshot.frame = frame;
                snapshot.inputPass = frame;
                return true;
            }
        }

        public static void Capture()
        {
            string output = Argument("-nowuiCaptureOutput");
            int width = int.Parse(Argument("-nowuiCaptureWidth"));
            int height = int.Parse(Argument("-nowuiCaptureHeight"));
            bool pixelAlignment = Argument("-nowuiCaptureScene") == "PixelAlignment";
            if (QualitySettings.activeColorSpace != ColorSpace.Gamma)
                throw new InvalidOperationException("The reference requires Gamma color space; no project settings were changed.");
            var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "NowUI native comparison", antiAliasing = 1, filterMode = FilterMode.Point,
                useMipMap = false, autoGenerateMips = false
            };
            var previousTarget = RenderTexture.active;
            var previousInput = NowInput.defaultProvider;
            Texture2D readback = null;
            var input = new IdleInput();
            try
            {
                target.Create();
                NowInput.defaultProvider = input;
                using (var content = pixelAlignment ? null : new InteractiveContent { Animate = false })
                using (var renderer = new NowRenderer())
                {
                    // Controls use Unity's real clock. Keep the scene paused and
                    // advance enough idle frames for their initial fades to settle.
                    for (int frame = 1; frame <= 32; frame++)
                    {
                        input.frame = frame;
                        using (NowInput.Begin(input, new NowInputSurface(new Vector2(width, height))))
                        using (renderer.Begin(target))
                        {
                            var view = new NowRect(0, 0, width, height);
                            if (pixelAlignment) PixelAlignmentContent.Draw(view);
                            else content.Draw(view);
                        }
                        renderer.Render(target, true, Color.clear);
                        if (frame < 32) System.Threading.Thread.Sleep(17);
                    }
                    RenderTexture.active = target;
                    readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
                    readback.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                    readback.Apply(false, false);
                    File.WriteAllBytes(output, readback.EncodeToPNG());
                    var metadata = new Metadata
                    {
                        unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName,
                        graphicsApi = SystemInfo.graphicsDeviceType.ToString(), colorSpace = QualitySettings.activeColorSpace.ToString(),
                        fontAsset = AssetDatabase.GetAssetPath(Now.defaultFont), textShaping = Now.textShaping,
                        source = "Standalone/Samples/NativePreview/" + (pixelAlignment ? "PixelAlignmentContent.cs" : "InteractiveContent.cs"),
                        width = width, height = height, uiScale = Now.uiScale
                    };
                    if (Now.defaultFont.TryResolveFont(NowFontStyle.Regular, out var regular))
                        metadata.fontTexture = AssetDatabase.GetAssetPath(regular.atlas);
                    File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonUtility.ToJson(metadata, true));
                    Debug.Log("NowUI native reference written: " + output + "\n" + JsonUtility.ToJson(metadata, true));
                }
            }
            finally
            {
                NowInput.defaultProvider = previousInput;
                RenderTexture.active = previousTarget;
                if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static string Argument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == name) return arguments[i + 1];
            throw new ArgumentException("Missing " + name);
        }
    }
}
