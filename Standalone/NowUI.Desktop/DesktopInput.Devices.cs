using System;
using System.Collections.Generic;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Desktop
{
    public sealed partial class DesktopInput
    {
        readonly HashSet<Keys> released = new();
        NowKeyInputFrame keyFrame;
        Key pendingKey;
        Func<Key, string> previousKeyNames;
        readonly Dictionary<Key, string> keyNames = new(64);
        readonly WindowsPointerInput windowsPointer;
        readonly WindowsTouchKeyboard touchKeyboard;
        readonly NativeGamepad[] gamepads = new NativeGamepad[16];
        readonly NativeGamepad[] previousGamepads = new NativeGamepad[16];
        int currentGamepad = -1;
        readonly Dictionary<int, TouchContact> touches = new(16);
        int? trackedTouch;
        long touchSequence;
        bool touchMode, touchReleasedPending, touchReleasePublished, mouseReleasedPending, pointerSourceChanged, lastInputWasTouch;

        /// <summary>GLFW's mapped gamepads provide stick/D-pad navigation and the shared submit/cancel actions.</summary>
        public bool SupportsGamepadNavigation => window != null;
        /// <summary>Whether this window receives Windows primary-touch and cancellation messages.</summary>
        public bool SupportsTouch => windowsPointer?.IsAttached == true;
        /// <summary>Whether a per-window Windows InputPane is available. Windows decides whether a show request is accepted.</summary>
        public bool SupportsOnScreenKeyboard => touchKeyboard?.IsAvailable == true;

        bool INowKeyInputSource.TryGetFrame(out NowKeyInputFrame frame) { frame = keyFrame; return true; }

        string KeyName(Key key) => keyNames.TryGetValue(key, out var name) ? name : null;

        internal void RefreshKeyNames()
        {
            if (window == null) return;
            for (int key = 32; key <= 96; key++) RefreshKeyName((Keys)key);
            RefreshKeyName((Keys)161); RefreshKeyName((Keys)162);
            for (int key = (int)Keys.KeyPad0; key <= (int)Keys.KeyPadEqual; key++) RefreshKeyName((Keys)key);
        }

        void RefreshKeyName(Keys native)
        {
            if (window == null) return;
            Key key = DesktopKeys.Map(native);
            if (key == Key.None || native == Keys.Space || native == Keys.KeyPadEnter) return;
            if ((int)native > 162 && (native < Keys.KeyPad0 || native > Keys.KeyPadEqual)) return;
            if (GLFW.GetKeyScancode(native) < 0) return; // A named GLFW key may not exist on this OS (e.g. WORLD_1 on Windows).
            string name = GLFW.GetKeyName(native, 0);
            if (string.IsNullOrWhiteSpace(name)) keyNames.Remove(key);
            else keyNames[key] = name;
        }

        internal void RawKeyDown(Key key)
        {
            if (!focused || composing || compositionActivity || key == Key.None) return;
            if (pendingKey == Key.None || key < pendingKey) pendingKey = key;
        }

        internal struct NativeGamepad
        {
            public bool connected;
            public Vector2 stick, dpad;
            // Standard GLFW indices: South=0, East=1, Back=6, Start=7.
            public ushort buttons;
            public bool HasActivity => buttons != 0 || dpad != Vector2.zero || stick.sqrMagnitude > .125f * .125f;
        }

        // Same input seam as native polling, kept internal so consumers use NowUI's input contracts.
        internal void SetGamepadState(int index, Vector2 stick, Vector2 dpad, bool south = false,
            bool east = false, bool start = false, bool select = false, bool connected = true)
        {
            if ((uint)index >= gamepads.Length) throw new ArgumentOutOfRangeException(nameof(index));
            gamepads[index] = new NativeGamepad { connected = connected, stick = stick, dpad = dpad,
                buttons = (ushort)((south ? 1 : 0) | (east ? 2 : 0) | (select ? 64 : 0) | (start ? 128 : 0)) };
        }

        unsafe void PollGamepads()
        {
            if (window == null) return;
            for (int i = 0; i < gamepads.Length; i++)
            {
                NativeGamepad state = default;
                if (GLFW.GetGamepadState(i, out GamepadState native))
                {
                    state.connected = true;
                    state.stick = new Vector2(native.Axes[0], -native.Axes[1]);
                    state.dpad = new Vector2(native.Buttons[12] - native.Buttons[14], native.Buttons[11] - native.Buttons[13]);
                    for (int b = 0; b < 15; b++) if (native.Buttons[b] != 0) state.buttons |= (ushort)(1 << b);
                }
                gamepads[i] = state;
            }
        }

        void PublishDeviceInput()
        {
            int previousCurrent = currentGamepad;
            PollGamepads();
            if (currentGamepad >= 0 && !gamepads[currentGamepad].connected) currentGamepad = -1;
            for (int i = 0; i < gamepads.Length; i++)
            {
                var gamepad = gamepads[i];
                var previous = previousGamepads[i];
                if (gamepad.connected && (currentGamepad < 0 || gamepad.HasActivity &&
                    (!previous.connected || gamepad.buttons != previous.buttons || gamepad.stick != previous.stick || gamepad.dpad != previous.dpad)))
                    currentGamepad = i;
            }
            var keys = NowInput.navigationKeys;
            bool arrows = (keys & NowNavigationKeys.Arrows) != 0, wasd = (keys & NowNavigationKeys.Wasd) != 0;
            Vector2 navigation = new(
                (arrows && held.Contains(Keys.Right) || wasd && held.Contains(Keys.D) ? 1 : 0) -
                (arrows && held.Contains(Keys.Left) || wasd && held.Contains(Keys.A) ? 1 : 0),
                (arrows && held.Contains(Keys.Up) || wasd && held.Contains(Keys.W) ? 1 : 0) -
                (arrows && held.Contains(Keys.Down) || wasd && held.Contains(Keys.S) ? 1 : 0));
            bool submitDown = false, submitPressed = false, submitReleased = false;
            if ((keys & NowNavigationKeys.EnterSubmit) != 0)
            {
                MergeKey(Keys.Enter, ref submitDown, ref submitPressed, ref submitReleased);
                MergeKey(Keys.KeyPadEnter, ref submitDown, ref submitPressed, ref submitReleased);
            }
            if ((keys & NowNavigationKeys.SpaceSubmit) != 0) MergeKey(Keys.Space, ref submitDown, ref submitPressed, ref submitReleased);
            bool cancelDown = false, cancelPressed = false, cancelReleased = false;
            MergeKey(Keys.Escape, ref cancelDown, ref cancelPressed, ref cancelReleased);
            if (focused && currentGamepad < 0 && previousCurrent >= 0)
            {
                ushort previous = previousGamepads[previousCurrent].buttons;
                submitReleased |= (previous & 129) != 0;
                cancelReleased |= (previous & 66) != 0;
            }
            if (focused && currentGamepad >= 0)
            {
                var gamepad = gamepads[currentGamepad];
                var previous = previousGamepads[currentGamepad];
                navigation += Deadzone(gamepad.stick) + gamepad.dpad;
                MergeGamepad(gamepad.buttons, previous.buttons, 0, ref submitDown, ref submitPressed, ref submitReleased);
                MergeGamepad(gamepad.buttons, previous.buttons, 7, ref submitDown, ref submitPressed, ref submitReleased);
                MergeGamepad(gamepad.buttons, previous.buttons, 1, ref cancelDown, ref cancelPressed, ref cancelReleased);
                MergeGamepad(gamepad.buttons, previous.buttons, 6, ref cancelDown, ref cancelPressed, ref cancelReleased);
            }
            // Remember disconnected state too; reconnecting can never keep an old held action alive.
            Array.Copy(gamepads, previousGamepads, gamepads.Length);
            bool tab = (keys & NowNavigationKeys.TabFocus) != 0 && pressed.Contains(Keys.Tab);
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            Vector2 delta = pointerSourceChanged ? Vector2.zero : snapshot.pointerDelta;
            snapshot = new NowInputSnapshot(snapshot.hasPointer, snapshot.pointerPosition,
                pointerSourceChanged ? snapshot.pointerPosition : snapshot.previousPointerPosition, delta,
                snapshot.pointerButtonsDown, snapshot.pointerButtonsPressed, snapshot.pointerButtonsReleased, snapshot.scrollDelta,
                focused ? Vector2.ClampMagnitude(navigation, 1) : Vector2.zero, tab && shift, tab && !shift,
                submitDown, submitPressed, submitReleased, cancelDown, cancelPressed, cancelReleased, snapshot.frame, snapshot.time);
            pointerSourceChanged = false;
        }

        void MergeKey(Keys key, ref bool down, ref bool edge, ref bool up)
        { down |= held.Contains(key); edge |= pressed.Contains(key); up |= released.Contains(key); }

        KeyModifiers HeldModifiers()
        {
            KeyModifiers value = 0;
            if (held.Contains(Keys.LeftShift) || held.Contains(Keys.RightShift)) value |= KeyModifiers.Shift;
            if (held.Contains(Keys.LeftControl) || held.Contains(Keys.RightControl)) value |= KeyModifiers.Control;
            if (held.Contains(Keys.LeftAlt) || held.Contains(Keys.RightAlt)) value |= KeyModifiers.Alt;
            if (held.Contains(Keys.LeftSuper) || held.Contains(Keys.RightSuper)) value |= KeyModifiers.Super;
            return value;
        }

        static void MergeGamepad(ushort buttons, ushort previous, int bit, ref bool down, ref bool edge, ref bool up)
        {
            int mask = 1 << bit;
            bool now = (buttons & mask) != 0, before = (previous & mask) != 0;
            down |= now; edge |= now && !before; up |= before && !now;
        }

        // Unity Input System's default StickDeadzoneProcessor: radial deadzone .125..925.
        static Vector2 Deadzone(Vector2 value)
        {
            float magnitude = value.magnitude;
            if (!float.IsFinite(magnitude) || magnitude <= .125f) return Vector2.zero;
            return value * (Math.Clamp((magnitude - .125f) / .8f, 0, 1) / magnitude);
        }

        struct TouchContact { public Vector2 position; public long order; }

        /// <summary>Primary-touch host events use client coordinates and a stable contact identity. Extra contacts never steal an active drag.</summary>
        public void TouchDown(int id, float clientX, float clientY)
        {
            ThrowIfDisposed();
            if (!focused || touches.ContainsKey(id)) return;
            touches[id] = new TouchContact { position = new Vector2(clientX, clientY), order = ++touchSequence };
            if (!trackedTouch.HasValue && !touchReleasedPending && !touchReleasePublished && buttons == 0 && !mouseReleasedPending)
                StartTouch(id);
        }

        public void TouchMove(int id, float clientX, float clientY)
        {
            ThrowIfDisposed();
            if (!focused || !touches.TryGetValue(id, out var contact)) return;
            contact.position = new Vector2(clientX, clientY);
            touches[id] = contact;
            if (trackedTouch == id) pointerEvents.Enqueue(new PointerEvent(0, contact.position, 0, 1));
        }

        public void TouchUp(int id, float clientX, float clientY, bool cancelled = false)
        {
            ThrowIfDisposed();
            if (!touches.Remove(id) || trackedTouch != id) return;
            trackedTouch = null;
            touchReleasedPending = true;
            pointerEvents.Enqueue(new PointerEvent(cancelled ? 5 : 2, new Vector2(clientX, clientY), 0, 0));
            if (cancelled) pendingCancellation = true;
        }

        internal void CancelTouch(int id)
        {
            if (touches.TryGetValue(id, out var contact)) TouchUp(id, contact.position.x, contact.position.y, true);
        }

        internal void CancelMouseCapture()
        {
            // GLFW releases its capture after the ordinary last mouse-up; that is not cancellation.
            if (buttons == 0) return;
            buttons = 0;
            pendingCancellation = true;
            pointerEvents.Enqueue(new PointerEvent(5, default, 0, 0));
        }

        internal void CancelPointers()
        {
            if (buttons == 0 && !trackedTouch.HasValue) return;
            buttons = 0;
            touches.Clear(); trackedTouch = null;
            touchReleasedPending = true;
            pendingCancellation = true;
            pointerEvents.Enqueue(new PointerEvent(5, default, 0, 0));
        }

        void StartTouch(int id)
        {
            trackedTouch = id;
            touchMode = lastInputWasTouch = true;
            if (imeEnabled) touchKeyboard?.Show();
            pointerEvents.Enqueue(new PointerEvent(6, default, 0, 0));
            pointerEvents.Enqueue(new PointerEvent(1, touches[id].position, 0, 1));
        }

        void PrepareTouchFrame()
        {
            if (touchReleasePublished)
            {
                touchReleasePublished = false;
                pointerEvents.Enqueue(new PointerEvent(3, default, 0, 0));
            }
            if (!focused || trackedTouch.HasValue || touchReleasedPending || buttons != 0 || mouseReleasedPending || touches.Count == 0) return;
            long earliest = long.MaxValue;
            int first = 0;
            foreach (var contact in touches) if (contact.Value.order < earliest) { earliest = contact.Value.order; first = contact.Key; }
            StartTouch(first);
        }

        void FinishTouchFrame()
        {
            touchReleasePublished = touchReleasedPending;
            touchReleasedPending = false;
            mouseReleasedPending = false;
        }

        bool AcceptMouse()
        {
            if (buttons != 0) return true; // A touch contact cannot take an existing mouse drag's ownership.
            if (trackedTouch.HasValue || touchReleasedPending || touches.Count != 0) return false;
            if (touchMode)
            {
                touchReleasePublished = false;
                touchMode = false;
                pointerEvents.Enqueue(new PointerEvent(6, default, 0, 0));
            }
            lastInputWasTouch = false;
            return true;
        }

        void ResetDevices()
        {
            released.Clear(); pendingKey = Key.None; keyFrame = default;
            touches.Clear(); trackedTouch = null;
            touchMode = touchReleasedPending = touchReleasePublished = mouseReleasedPending = lastInputWasTouch = false;
            currentGamepad = -1;
            // Polling continues while unfocused, consuming edges without activating another window's controls.
        }
    }
}
