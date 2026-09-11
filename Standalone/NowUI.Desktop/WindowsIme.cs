using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NowUI.Desktop
{
    /// <summary>IMM composition for this GLFW window, on its owning thread.</summary>
    internal sealed class WindowsIme : IDisposable
    {
        const uint WmImeStartComposition = 0x010D, WmImeEndComposition = 0x010E,
            WmImeComposition = 0x010F, WmImeSetContext = 0x0281, WmNcDestroy = 0x0082;
        const uint GcsCompStr = 0x0008, GcsResultStr = 0x0800;
        const uint CfsPoint = 0x0002, CfsExclude = 0x0080;
        readonly DesktopInput input;
        readonly SubclassProc callback;
        readonly uint threadId;
        IntPtr handle, context, originalContext;
        bool ownsContext, enabled = true;
        Exception pendingError;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate IntPtr SubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

        [StructLayout(LayoutKind.Sequential)]
        struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct Rect { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct CompositionForm { public uint style; public Point point; public Rect area; }
        [StructLayout(LayoutKind.Sequential)]
        struct CandidateForm { public uint index, style; public Point point; public Rect area; }

        public unsafe WindowsIme(NativeWindow window, DesktopInput input)
        {
            this.input = input;
            handle = GLFW.GetWin32Window(window.WindowPtr);
            if (handle == IntPtr.Zero) throw new InvalidOperationException("GLFW did not provide a Windows handle for IME input.");
            threadId = GetCurrentThreadId();
            if (GetWindowThreadProcessId(handle, out _) != threadId)
                throw new InvalidOperationException("DesktopInput must be created on the window's owning thread.");
            callback = WindowMessage;
            originalContext = ImmGetContext(handle);
            if (originalContext != IntPtr.Zero) ImmReleaseContext(handle, originalContext);
            // The thread's default context can be shared by several windows.
            // Use a private context so cancel/focus transitions stay local.
            context = ImmCreateContext();
            ownsContext = context != IntPtr.Zero;
            if (!ownsContext) throw new InvalidOperationException("Windows could not create an IME input context.");
            if (originalContext != IntPtr.Zero)
            {
                ImmSetOpenStatus(context, ImmGetOpenStatus(originalContext));
                if (ImmGetConversionStatus(originalContext, out uint conversion, out uint sentence))
                    ImmSetConversionStatus(context, conversion, sentence);
            }
            if (!SetWindowSubclass(handle, callback, UIntPtr.Zero, UIntPtr.Zero))
            {
                if (ownsContext) ImmDestroyContext(context);
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot attach IME input to the preview window.");
            }
            // A focused text editor explicitly enables its input context.
            SetEnabled(false);
        }

        public bool IsAttached => handle != IntPtr.Zero;
        internal IntPtr WindowHandle => handle;

        public void SetEnabled(bool value)
        {
            CheckThread();
            if (handle == IntPtr.Zero || enabled == value) return;
            if (!value) Cancel();
            enabled = value;
            ImmAssociateContext(handle, value ? context : IntPtr.Zero);
        }

        public void Cancel()
        {
            CheckThread();
            if (handle == IntPtr.Zero) return;
            if (enabled) ImmNotifyIME(context, 0x0015, 0x0004, 0); // NI_COMPOSITIONSTR / CPS_CANCEL.
        }

        public void SetCursor(int x, int y)
        {
            CheckThread();
            if (handle == IntPtr.Zero || !enabled) return;
            var point = new Point { x = x, y = y };
            var composition = new CompositionForm { style = CfsPoint, point = point };
            ImmSetCompositionWindow(context, ref composition);
            var candidate = new CandidateForm
            {
                style = CfsExclude, point = point,
                area = new Rect { left = x, top = y - 1, right = x + 1, bottom = y + 1 }
            };
            ImmSetCandidateWindow(context, ref candidate);
        }

        public void ThrowPendingError()
        {
            if (pendingError == null) return;
            var error = pendingError;
            pendingError = null;
            throw new InvalidOperationException("Windows IME message handling failed.", error);
        }

        IntPtr WindowMessage(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            // Exceptions must never cross an unmanaged window-procedure boundary.
            try
            {
                switch (message)
                {
                    case WmImeSetContext:
                        // NowUI draws pre-edit text inline; Windows keeps ownership of candidate UI.
                        lParam = new IntPtr(lParam.ToInt64() & ~0x80000000L);
                        break;
                    case WmImeStartComposition when enabled:
                        input.CompositionStart();
                        return IntPtr.Zero;
                    case WmImeComposition when enabled:
                        ReadComposition(window, unchecked((uint)lParam.ToInt64()));
                        // Processing GCS_RESULTSTR here replaces DefWindowProc's WM_IME_CHAR path;
                        // forwarding the message would insert the committed text a second time.
                        return IntPtr.Zero;
                    case WmImeEndComposition when enabled:
                        input.CompositionEnd();
                        return IntPtr.Zero;
                    case WmNcDestroy:
                        RemoveWindowSubclass(window, callback, UIntPtr.Zero);
                        handle = IntPtr.Zero;
                        break;
                }
            }
            catch (Exception error) { pendingError ??= error; }
            return DefSubclassProc(window, message, wParam, lParam);
        }

        void ReadComposition(IntPtr window, uint flags)
        {
            IntPtr current = ImmGetContext(window);
            if (current == IntPtr.Zero) return;
            try
            {
                if ((flags & GcsResultStr) != 0) input.CompositionCommit(ReadString(current, GcsResultStr));
                if ((flags & GcsCompStr) != 0) input.CompositionUpdate(ReadString(current, GcsCompStr));
                else if ((flags & GcsResultStr) != 0 || (flags & 0x1FFF) == 0) input.CompositionEnd();
            }
            finally { ImmReleaseContext(window, current); }
        }

        static string ReadString(IntPtr current, uint kind)
        {
            int length = ImmGetCompositionStringW(current, kind, null, 0);
            if (length <= 0) return string.Empty;
            // Native lengths are bytes, including for UTF-16; reject impossible/oversized packets.
            if (length > 1024 * 1024 || (length & 1) != 0)
                throw new InvalidOperationException("Windows returned an invalid IME composition length.");
            var bytes = new byte[length];
            int written = ImmGetCompositionStringW(current, kind, bytes, (uint)bytes.Length);
            if (written < 0) return string.Empty;
            if (written > bytes.Length || (written & 1) != 0)
                throw new InvalidOperationException("Windows returned invalid IME composition data.");
            return Encoding.Unicode.GetString(bytes, 0, written);
        }

        void CheckThread()
        {
            if (GetCurrentThreadId() != threadId)
                throw new InvalidOperationException("Windows IME input must be used and disposed on its window thread.");
        }

        public void Dispose()
        {
            CheckThread();
            if (handle != IntPtr.Zero)
            {
                Cancel();
                ImmAssociateContext(handle, originalContext);
                RemoveWindowSubclass(handle, callback, UIntPtr.Zero);
                handle = IntPtr.Zero;
            }
            if (ownsContext) { ImmDestroyContext(context); ownsContext = false; }
            context = IntPtr.Zero;
            GC.KeepAlive(callback);
        }

        [DllImport("comctl32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")]
        static extern IntPtr DefSubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr window);
        [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll")] static extern IntPtr ImmCreateContext();
        [DllImport("imm32.dll")] static extern bool ImmDestroyContext(IntPtr context);
        [DllImport("imm32.dll")] static extern bool ImmGetOpenStatus(IntPtr context);
        [DllImport("imm32.dll")] static extern bool ImmSetOpenStatus(IntPtr context, [MarshalAs(UnmanagedType.Bool)] bool open);
        [DllImport("imm32.dll")] static extern bool ImmGetConversionStatus(IntPtr context, out uint conversion, out uint sentence);
        [DllImport("imm32.dll")] static extern bool ImmSetConversionStatus(IntPtr context, uint conversion, uint sentence);
        [DllImport("imm32.dll")] static extern IntPtr ImmAssociateContext(IntPtr window, IntPtr context);
        [DllImport("imm32.dll")] static extern bool ImmNotifyIME(IntPtr context, uint action, uint index, uint value);
        [DllImport("imm32.dll")] static extern int ImmGetCompositionStringW(IntPtr context, uint index, byte[] buffer, uint length);
        [DllImport("imm32.dll")] static extern bool ImmSetCompositionWindow(IntPtr context, ref CompositionForm form);
        [DllImport("imm32.dll")] static extern bool ImmSetCandidateWindow(IntPtr context, ref CandidateForm form);
    }
}
