using System;
using System.Runtime.InteropServices;

namespace NowUI.Desktop
{
    /// <summary>Windows 10+ per-window InputPane. Text arrives through normal key/Unicode/IME events.</summary>
    internal sealed unsafe class WindowsTouchKeyboard : IDisposable
    {
        IntPtr pane;
        readonly bool initialized;
        readonly int thread = Environment.CurrentManagedThreadId;
        bool requested, disposed;
        internal bool IsAvailable => pane != IntPtr.Zero;

        internal WindowsTouchKeyboard(IntPtr hwnd)
        {
            int hr = RoInitialize(0); // The window thread may already have a COM apartment.
            initialized = hr >= 0;
            if (hr < 0 && hr != unchecked((int)0x80010106)) return; // RPC_E_CHANGED_MODE is an existing apartment.
            IntPtr name = IntPtr.Zero, factory = IntPtr.Zero;
            try
            {
                const string runtimeClass = "Windows.UI.ViewManagement.InputPane";
                if (WindowsCreateString(runtimeClass, runtimeClass.Length, out name) < 0) return;
                var interopId = new Guid("75CF2C57-9195-4931-8332-F0B409E916AF");
                if (RoGetActivationFactory(name, in interopId, out factory) < 0) return;
                var paneId = new Guid("8A6B3F26-7090-4793-944C-C3F2CDE26276");
                var getForWindow = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)(*(IntPtr**)factory)[6];
                IntPtr result = IntPtr.Zero;
                if (getForWindow(factory, hwnd, &paneId, &result) >= 0) pane = result;
            }
            finally
            {
                if (factory != IntPtr.Zero) Marshal.Release(factory);
                if (name != IntPtr.Zero) WindowsDeleteString(name);
            }
        }

        internal bool Show()
        {
            CheckThread();
            if (pane == IntPtr.Zero) return false;
            byte accepted = 0;
            var show = (delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)(*(IntPtr**)pane)[6];
            requested = show(pane, &accepted) >= 0 && accepted != 0;
            return requested;
        }

        internal void Hide()
        {
            CheckThread();
            if (pane == IntPtr.Zero || !requested) return;
            byte accepted = 0;
            var hide = (delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)(*(IntPtr**)pane)[7];
            hide(pane, &accepted);
            requested = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            CheckThread();
            Hide();
            if (pane != IntPtr.Zero) { Marshal.Release(pane); pane = IntPtr.Zero; }
            if (initialized) RoUninitialize();
            disposed = true;
        }

        void CheckThread()
        {
            if (thread != Environment.CurrentManagedThreadId) throw new InvalidOperationException("The touch keyboard belongs to its window thread.");
        }

        [DllImport("combase.dll")] static extern int RoInitialize(uint type);
        [DllImport("combase.dll")] static extern void RoUninitialize();
        [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string value, int length, out IntPtr result);
        [DllImport("combase.dll")] static extern int WindowsDeleteString(IntPtr value);
        [DllImport("combase.dll")] static extern int RoGetActivationFactory(IntPtr name, in Guid iid, out IntPtr factory);
    }
}
