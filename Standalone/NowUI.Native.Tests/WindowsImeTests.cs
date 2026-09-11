using System.Runtime.InteropServices;
using NowUI.Desktop;
using NowUI.Engine;
using NUnit.Framework;
using OpenTK.Windowing.Desktop;
using UnityEngine;

namespace NowUI.Native.Tests;

[NonParallelizable, Category("NativeGraphics")]
public sealed class WindowsImeTests
{
    [Test, Apartment(ApartmentState.STA)]
    public void OwnWindowContextAndNativeCompositionMessagesFollowInputLifecycle()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("IMM is a Windows-specific native input bridge.");
        if (Environment.GetEnvironmentVariable("NOWUI_TEST_NATIVE_GRAPHICS") != "1")
            Assert.Ignore("Set NOWUI_TEST_NATIVE_GRAPHICS=1 to use a native graphics window.");
        // NUnit runs on a worker, not the process entry thread. Windows permits
        // this provided creation, pumping and disposal stay on this one thread.
        // Only this nonparallel Windows test relaxes OpenTK's portable guard.
        bool previousMainThreadCheck = GLFWProvider.CheckForMainThread;
        GLFWProvider.CheckForMainThread = false;
        try { RunWindowLifecycle(); }
        finally { GLFWProvider.CheckForMainThread = previousMainThreadCheck; }
    }

    static void RunWindowLifecycle()
    {
        NowInput.Reset();
        NowTextInput.Reset();
        using var backend = new DesktopRenderBackend(320, 180);
        NowRuntime.Initialize(new DefaultHostServices(), backend);
        try
        {
            using var input = new DesktopInput(backend.Window);
            input.Install();
            input.SetFocused(true);
            Assert.That(input.SupportsImeComposition, Is.True);
            var handle = input.NativeImeWindowHandle;
            SendMessageW(handle, 0x7, UIntPtr.Zero, IntPtr.Zero); // Drive the hidden window's native focus callback.
            Assert.That(CurrentContext(handle), Is.EqualTo(IntPtr.Zero), "IME starts disabled until an editor asks for it.");
            NowTextInput.setImeEnabled(true);
            Assert.That(CurrentContext(handle), Is.Not.EqualTo(IntPtr.Zero));
            NowTextInput.setCompositionCursor(new Vector2(40, 70));
            SendMessageW(handle, 0x010D, UIntPtr.Zero, IntPtr.Zero); // WM_IME_STARTCOMPOSITION on this test window only.
            Assert.That(input.IsComposing, Is.True, "The real window subclass receives native composition messages.");
            input.KeyDown(OpenTK.Windowing.GraphicsLibraryFramework.Keys.Enter);
            NowRuntime.BeginFrame();
            try
            {
                input.Drain();
                input.TryGetFrame(out var frame);
                Assert.That(frame.enterPressed || frame.enterHeld, Is.False);
            }
            finally { NowRuntime.EndFrame(); }
            SendMessageW(handle, 0x010E, UIntPtr.Zero, IntPtr.Zero);
            Assert.That(input.IsComposing, Is.False);
            NowTextInput.setImeEnabled(false);
            Assert.That(CurrentContext(handle), Is.EqualTo(IntPtr.Zero));
            Assert.That(input.SupportsTouch, Is.True);
            Assert.That(input.SupportsOnScreenKeyboard, Is.True, "Windows 10+ exposes the per-window InputPane interface.");
            NowRuntime.BeginFrame();
            try { input.Drain(); }
            finally { NowRuntime.EndFrame(); }
            input.SetFocused(true);
            SendMessageW(handle, 0x319, UIntPtr.Zero, new IntPtr(14 << 16)); // WM_APPCOMMAND / MEDIA_PLAY_PAUSE.
            NowRuntime.BeginFrame();
            try
            {
                input.Drain();
                Assert.That(NowKeyInput.current.pressedKey, Is.EqualTo(UnityEngine.InputSystem.Key.MediaPlayPause));
            }
            finally { NowRuntime.EndFrame(); }
            input.TouchDown(42, 30, 30);
            Assert.That(Interaction().pressed, Is.True);
            input.TouchMove(42, 80, 30);
            Assert.That(Interaction().dragging, Is.True);
            // A synthetic contact has no OS POINTER_INFO; Windows reserves WM_POINTER* for actual contacts.
            // Exercise the real window's cancellation path with WM_CANCELMODE, which the host also handles.
            SendMessageW(handle, 0x1F, UIntPtr.Zero, IntPtr.Zero);
            var cancelled = Interaction();
            input.TryGetSnapshot(new NowInputSurface(new Vector2(320, 180)), out var cancellationFrame);
            Assert.That(cancellationFrame.pointerCaptureCancelled, Is.True, "Native pointer capture loss reached the adapter.");
            Assert.That(cancelled.dragCancelled, Is.True);
            Assert.That(cancelled.clicked || cancelled.dragEnded, Is.False);
            input.Dispose();
            // Closing the window after detach must not call an abandoned managed callback.
            backend.Close();
            backend.ProcessEvents();

            NowInteraction Interaction()
            {
                NowRuntime.BeginFrame();
                try
                {
                    input.Drain();
                    using (NowInput.Begin(input, new NowInputSurface(new Vector2(320, 180))))
                        return NowInput.Interact(NowControls.GetControlId("native-touch"), new Rect(0, 0, 200, 100));
                }
                finally { NowRuntime.EndFrame(); }
            }
        }
        finally { NowRuntime.Shutdown(); }
    }

    static IntPtr CurrentContext(IntPtr window)
    {
        IntPtr value = ImmGetContext(window);
        if (value != IntPtr.Zero) ImmReleaseContext(window, value);
        return value;
    }
    [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
    [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
}
