using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine.InputSystem;

namespace NowUI.Desktop
{
    /// <summary>Window-local touch, capture cancellation, and media keys. No process/global input hooks.</summary>
    internal sealed class WindowsPointerInput : IDisposable
    {
        readonly DesktopInput input;
        readonly SubclassProc callback;
        readonly uint threadId;
        IntPtr handle;
        Exception pendingError;
        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        delegate IntPtr SubclassProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
        [StructLayout(LayoutKind.Sequential)]
        struct Point { public int x, y; }
        [StructLayout(LayoutKind.Sequential)]
        struct PointerInfo
        {
            public uint type, id, frame, flags;
            public IntPtr device, target;
            public Point pixel, himetric, pixelRaw, himetricRaw;
            public uint time, history;
            public int inputData;
            public uint keyStates;
            public ulong performanceCount;
            public uint buttonChange;
        }

        internal unsafe WindowsPointerInput(NativeWindow window, DesktopInput input)
        {
            this.input = input;
            handle = GLFW.GetWin32Window(window.WindowPtr);
            threadId = GetCurrentThreadId();
            if (GetWindowThreadProcessId(handle, out _) != threadId)
                throw new InvalidOperationException("Touch input must be attached on the window's owning thread.");
            callback = WindowMessage;
            if (!SetWindowSubclass(handle, callback, UIntPtr.Zero, UIntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot attach touch input to the preview window.");
        }

        internal bool IsAttached => handle != IntPtr.Zero;

        IntPtr WindowMessage(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            try
            {
                if (message == 0x82) // WM_NCDESTROY
                {
                    RemoveWindowSubclass(hwnd, callback, UIntPtr.Zero);
                    handle = IntPtr.Zero;
                }
                else if (message == 0x1F) input.CancelPointers(); // WM_CANCELMODE
                else if (message == 0x51) // WM_INPUTLANGCHANGE: GLFW refreshes its layout table first.
                {
                    var result = DefSubclassProc(hwnd, message, wParam, lParam);
                    input.RefreshKeyNames();
                    return result;
                }
                else if (message == 0x215 && lParam != hwnd) input.CancelMouseCapture(); // WM_CAPTURECHANGED
                else if (message == 0x319) // WM_APPCOMMAND: GLFW has no named media-key constants.
                {
                    int command = (int)((lParam.ToInt64() >> 16) & 0xFFF);
                    Key key = command switch { 14 => Key.MediaPlayPause, 11 => Key.MediaForward, 12 => Key.MediaRewind, _ => Key.None };
                    if (key != Key.None) { input.RawKeyDown(key); return new IntPtr(1); }
                }
                else if (message >= 0x200 && message <= 0x20E &&
                    (unchecked((ulong)GetMessageExtraInfo().ToInt64()) & 0xFFFFFF80UL) == 0xFF515780UL)
                    return IntPtr.Zero; // Touch-promoted mouse messages must not duplicate the primary contact.
                else if (message is 0x245 or 0x246 or 0x247 or 0x249 or 0x24A or 0x24C)
                {
                    uint pointerId = (uint)(wParam.ToUInt64() & 0xFFFF);
                    if (message == 0x24C)
                    {
                        input.CancelTouch((int)pointerId);
                        return DefSubclassProc(hwnd, message, wParam, lParam);
                    }
                    if (GetPointerType(pointerId, out uint type) && type == 2) // PT_TOUCH
                    {
                        if (GetPointerInfo(pointerId, out var info))
                        {
                            var point = info.pixel;
                            if (ScreenToClient(hwnd, ref point))
                            {
                                if ((info.flags & 0x8000) != 0) input.CancelTouch((int)pointerId);
                                else if (message == 0x246)
                                {
                                    SetFocus(hwnd);
                                    input.TouchDown((int)pointerId, point.x, point.y);
                                }
                                else if (message == 0x247) input.TouchUp((int)pointerId, point.x, point.y);
                                else if (message == 0x245) input.TouchMove((int)pointerId, point.x, point.y);
                            }
                        }
                        // Consume the entire touch sequence; DefWindowProc otherwise promotes it to mouse/gestures.
                        return IntPtr.Zero;
                    }
                }
            }
            catch (Exception error) { pendingError ??= error; }
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        internal void ThrowPendingError()
        {
            if (pendingError == null) return;
            var error = pendingError; pendingError = null;
            throw new InvalidOperationException("Native touch input failed.", error);
        }

        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            if (GetCurrentThreadId() != threadId) throw new InvalidOperationException("Touch input must be detached on its owning thread.");
            RemoveWindowSubclass(handle, callback, UIntPtr.Zero);
            handle = IntPtr.Zero;
        }

        [DllImport("comctl32.dll", SetLastError = true)] static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id, UIntPtr data);
        [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc callback, UIntPtr id);
        [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetPointerType(uint id, out uint type);
        [DllImport("user32.dll")] static extern bool GetPointerInfo(uint id, out PointerInfo info);
        [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    }
}
