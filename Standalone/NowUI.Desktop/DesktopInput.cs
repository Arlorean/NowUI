using System;
using System.Collections.Generic;
using System.Text;
using NowUI.Engine;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Desktop
{
    /// <summary>Native mouse, keyboard, Unicode text, and Windows IME input for one desktop window.</summary>
    /// <remarks>
    /// Process native events before BeginFrame, then call Drain once after BeginFrame and before drawing.
    /// Coordinates use the same UI scale as Now.StartUI. Windows supports inline IME composition and native candidates.
    /// A windowless instance accepts the same event helpers for deterministic tests.
    /// </remarks>
    public sealed partial class DesktopInput : INowInputProvider, INowTextInputSource, INowTextInputBuffer, INowKeyInputSource, INowClipboard, IDisposable
    {
        readonly NativeWindow window;
        readonly NowUIToolkitInputProvider pointer = new();
        readonly Queue<PointerEvent> pointerEvents = new();
        readonly HashSet<Keys> held = new();
        readonly HashSet<Keys> pressed = new();
        readonly StringBuilder characters = new();
        readonly bool mac = OperatingSystem.IsMacOS();
        readonly WindowsIme ime;
        NowTextInputFrame pendingText, textFrame;
        NowInputSnapshot snapshot;
        KeyModifiers modifiers;
        Vector2 clientPosition;
        int buttons, clientWidth = 1, clientHeight = 1, framebufferWidth = 1, framebufferHeight = 1;
        bool focused = true, inside, pendingCancellation, installed, disposed;
        int lastDrainFrame = -1;
        INowInputProvider previousProvider;
        INowTextInputSource previousTextSource;
        INowKeyInputSource previousKeySource;
        bool previousMac;
        string localClipboard = string.Empty;
        string composition;
        bool composing, compositionActivity, imeEnabled;
        float lastUiScale = 1;
        Action<bool> previousImeEnabled;
        Action<Vector2> previousCompositionCursor;
        internal Vector2 CompositionCursorClient { get; private set; }
        internal bool IsComposing => composing;

        readonly struct PointerEvent
        {
            public readonly int kind, button, buttons;
            public readonly Vector2 position;
            public PointerEvent(int kind, Vector2 position, int button, int buttons)
            { this.kind = kind; this.position = position; this.button = button; this.buttons = buttons; }
        }

        public INowInputProvider Provider => this;
        public INowClipboard Clipboard => this;
        /// <summary>Whether this window has the native Windows composition/candidate bridge attached.</summary>
        public bool SupportsImeComposition => ime?.IsAttached == true;
        internal IntPtr NativeImeWindowHandle => ime?.WindowHandle ?? IntPtr.Zero;

        public DesktopInput(NativeWindow window = null)
        {
            this.window = window;
            if (window == null) return;
            if (OperatingSystem.IsWindows())
            {
                ime = new WindowsIme(window, this);
                try { windowsPointer = new WindowsPointerInput(window, this); touchKeyboard = new WindowsTouchKeyboard(ime.WindowHandle); }
                catch { windowsPointer?.Dispose(); ime.Dispose(); throw; }
            }
            focused = window.IsFocused;
            UpdateDimensions();
            clientPosition = new Vector2(window.MousePosition.X, window.MousePosition.Y);
            inside = IsInside(clientPosition);
            window.MouseMove += OnMouseMove;
            window.MouseDown += OnMouseDown;
            window.MouseUp += OnMouseUp;
            window.MouseWheel += OnMouseWheel;
            window.MouseEnter += OnMouseEnter;
            window.MouseLeave += PointerLeave;
            window.FocusedChanged += OnFocusedChanged;
            window.KeyDown += OnKeyDown;
            window.KeyUp += OnKeyUp;
            window.TextInput += OnTextInput;
        }

        public void Install()
        {
            ThrowIfDisposed();
            if (installed) return;
            previousProvider = NowInput.defaultProvider;
            previousTextSource = NowTextInput.source;
            previousKeySource = NowKeyInput.source;
            previousMac = NowTextInput.isMacPlatform;
            previousImeEnabled = NowTextInput.setImeEnabled;
            previousCompositionCursor = NowTextInput.setCompositionCursor;
            NowInput.defaultProvider = this;
            NowTextInput.source = this;
            NowTextInput.isMacPlatform = mac;
            NowTextInput.setImeEnabled = SetImeEnabled;
            NowTextInput.setCompositionCursor = SetCompositionCursor;
            NowTextInput.Invalidate();
            NowKeyInput.source = this;
            NowKeyInput.Invalidate();
            previousKeyNames = NowKeyNames.nativeDisplayName;
            RefreshKeyNames();
            NowKeyNames.nativeDisplayName = KeyName;
            installed = true;
        }

        /// <summary>Publishes this frame's buffered events; repeated calls within one frame are harmless.</summary>
        public void Drain(float uiScale = 1f)
        {
            ThrowIfDisposed();
            if (!(uiScale > 0) || !float.IsFinite(uiScale)) throw new ArgumentOutOfRangeException(nameof(uiScale));
            if (lastDrainFrame == Time.frameCount) return;
            lastDrainFrame = Time.frameCount;
            lastUiScale = uiScale;
            ime?.ThrowPendingError();
            windowsPointer?.ThrowPendingError();
            PrepareTouchFrame();
            if (window != null)
            {
                UpdateDimensions();
                if (focused != window.IsFocused) SetFocused(window.IsFocused);
                if (focused && !touchMode && (inside || buttons != 0))
                    PointerMove(window.MousePosition.X, window.MousePosition.Y);
            }
            float sx = framebufferWidth / (float)clientWidth / uiScale;
            float sy = framebufferHeight / (float)clientHeight / uiScale;
            while (pointerEvents.TryDequeue(out var e))
            {
                Vector2 position = new(e.position.x * sx, e.position.y * sy);
                switch (e.kind)
                {
                    case 0: pointer.SetPointerPosition(position, e.buttons); break;
                    case 1: pointer.SetPointerDown(position, e.button, e.buttons); break;
                    case 2: pointer.SetPointerUp(position, e.button, e.buttons); break;
                    case 3: pointer.ClearPointer(); break;
                    case 5: pointer.CancelPointer(); break;
                    case 6: pointer.Reset(); pointerSourceChanged = true; break;
                    case 4: pointer.AddScrollDelta(new Vector2(e.position.x * 3, -e.position.y * 3)); break;
                }
            }
            pointer.TryGetSnapshot(new NowInputSurface(new Vector2(framebufferWidth / uiScale, framebufferHeight / uiScale)), out snapshot);
            PublishDeviceInput();
            bool imeOwnsKeys = composing || compositionActivity;
            if (imeOwnsKeys)
            {
                snapshot = new NowInputSnapshot(snapshot.hasPointer, snapshot.pointerPosition,
                    snapshot.previousPointerPosition, snapshot.pointerDelta, snapshot.pointerButtonsDown,
                    snapshot.pointerButtonsPressed, snapshot.pointerButtonsReleased, snapshot.scrollDelta,
                    Vector2.zero, false, false, false, false, false, false, false, false, snapshot.frame, snapshot.time);
            }
            snapshot.pointerCaptureCancelled = pendingCancellation;
            pendingCancellation = false;

            textFrame = pendingText;
            textFrame.characters = characters.Length == 0 ? null : characters.ToString();
            textFrame.backspaceHeld = Down(Keys.Backspace);
            textFrame.deleteHeld = Down(Keys.Delete);
            textFrame.leftHeld = Down(Keys.Left);
            textFrame.rightHeld = Down(Keys.Right);
            textFrame.upHeld = Down(Keys.Up);
            textFrame.downHeld = Down(Keys.Down);
            textFrame.enterHeld = Down(Keys.Enter) || Down(Keys.KeyPadEnter);
            textFrame.tabHeld = Down(Keys.Tab);
            textFrame.shift = (modifiers & KeyModifiers.Shift) != 0;
            textFrame.command = Command(modifiers);
            textFrame.option = (modifiers & KeyModifiers.Alt) != 0;
            textFrame.composition = composition;
            if (imeOwnsKeys)
            {
                // A commit may clear pre-edit text in the same event pump as Enter/Escape.
                // The IME still owns those keys for that frame, including control navigation.
                textFrame = new NowTextInputFrame { characters = textFrame.characters,
                    composition = composition, shift = textFrame.shift, command = textFrame.command, option = textFrame.option };
                ReleaseEditingKeys();
            }
            keyFrame.pressedKey = imeOwnsKeys ? Key.None : pendingKey;
            pendingKey = Key.None;
            released.Clear();
            FinishTouchFrame();
            compositionActivity = false;
            pendingText = default;
            characters.Clear();
            pressed.Clear();
        }

        bool Down(Keys key) => held.Contains(key) || pressed.Contains(key);
        bool Command(KeyModifiers value) => (value & (mac ? KeyModifiers.Super : KeyModifiers.Control)) != 0;
        bool IsInside(Vector2 position) => position.x >= 0 && position.y >= 0 && position.x < clientWidth && position.y < clientHeight;

        /// <summary>Sets client/framebuffer geometry for a windowless host. Native windows update it automatically.</summary>
        public void SetViewportSize(int clientWidth, int clientHeight, int framebufferWidth, int framebufferHeight)
        {
            ThrowIfDisposed();
            if (clientWidth <= 0 || clientHeight <= 0 || framebufferWidth <= 0 || framebufferHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(clientWidth), "Viewport dimensions must be positive.");
            this.clientWidth = clientWidth; this.clientHeight = clientHeight;
            this.framebufferWidth = framebufferWidth; this.framebufferHeight = framebufferHeight;
        }

        void UpdateDimensions() => SetViewportSize(Math.Max(1, window.ClientSize.X), Math.Max(1, window.ClientSize.Y),
            Math.Max(1, window.FramebufferSize.X), Math.Max(1, window.FramebufferSize.Y));

        public void PointerMove(float clientX, float clientY)
        {
            ThrowIfDisposed();
            if (!AcceptMouse()) return;
            clientPosition = new Vector2(clientX, clientY);
            inside = IsInside(clientPosition);
            if (focused && !touchMode && (inside || buttons != 0)) QueuePointer(0);
            else if (!inside && buttons == 0) QueuePointer(3);
        }

        /// <summary>Buttons use 0 = left, 1 = right, 2 = middle, 3 = back, 4 = forward.</summary>
        public void PointerDown(float clientX, float clientY, int button)
        {
            ThrowIfDisposed();
            if (!focused || (uint)button > 4 || !AcceptMouse()) return;
            clientPosition = new Vector2(clientX, clientY);
            inside = IsInside(clientPosition);
            buttons |= 1 << button;
            QueuePointer(1, button);
        }

        public void PointerUp(float clientX, float clientY, int button)
        {
            ThrowIfDisposed();
            if ((uint)button > 4 || (buttons & (1 << button)) == 0) return;
            clientPosition = new Vector2(clientX, clientY);
            inside = IsInside(clientPosition);
            buttons &= ~(1 << button);
            QueuePointer(2, button);
            mouseReleasedPending = buttons == 0;
            if (!inside && buttons == 0) QueuePointer(3);
        }

        public void PointerLeave()
        {
            ThrowIfDisposed();
            if (touchMode) return;
            inside = false;
            if (buttons == 0) QueuePointer(3);
            // GLFW retains native mouse capture during a button drag; off-window moves and releases still arrive.
        }

        public void Scroll(float horizontalNotches, float verticalNotches)
        {
            ThrowIfDisposed();
            if (focused && AcceptMouse()) pointerEvents.Enqueue(new PointerEvent(4, new Vector2(horizontalNotches, verticalNotches), 0, buttons));
        }

        void QueuePointer(int kind, int button = 0) => pointerEvents.Enqueue(new PointerEvent(kind, clientPosition, button, buttons));

        public void SetFocused(bool value)
        {
            ThrowIfDisposed();
            if (focused == value) return;
            focused = value;
            ime?.SetEnabled(value && imeEnabled);
            if (value) return;
            ResetDevices();
            touchKeyboard?.Hide();
            pendingCancellation = true;
            buttons = 0;
            inside = false;
            held.Clear(); pressed.Clear(); characters.Clear();
            modifiers = default;
            pendingText = default;
            composition = null;
            composing = compositionActivity = false;
            pointerEvents.Clear();
            pointer.Reset();
        }

        public void KeyDown(Keys key, KeyModifiers mods = default, bool isRepeat = false)
        {
            ThrowIfDisposed();
            if (!focused) return;
            bool first = held.Add(key);
            modifiers = mods | HeldModifiers();
            if (isRepeat || !first) return;
            pressed.Add(key);
            if (composing || compositionActivity) return;
            RawKeyDown(DesktopKeys.Map(key));
            switch (key)
            {
                case Keys.Home: pendingText.homePressed = true; break;
                case Keys.End: pendingText.endPressed = true; break;
                case Keys.Enter: case Keys.KeyPadEnter: pendingText.enterPressed = true; break;
                case Keys.Escape: pendingText.escapePressed = true; break;
                case Keys.Tab: pendingText.tabPressed = true; break;
                case Keys.F2: pendingText.renamePressed = true; break;
            }
            if (!Command(modifiers) || (modifiers & KeyModifiers.Alt) != 0) return;
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            switch (key)
            {
                case Keys.C: pendingText.copyPressed = true; break;
                case Keys.V: pendingText.pastePressed = true; break;
                case Keys.X: pendingText.cutPressed = true; break;
                case Keys.A: pendingText.selectAllPressed = true; break;
                case Keys.Z: if (shift) pendingText.redoPressed = true; else pendingText.undoPressed = true; break;
                case Keys.Y: pendingText.redoPressed = true; break;
                case Keys.D: pendingText.duplicatePressed = true; break;
                case Keys.Slash: pendingText.commentPressed = true; break;
                case Keys.G: pendingText.goToLinePressed = true; break;
            }
        }

        public void KeyUp(Keys key, KeyModifiers mods = default)
        {
            ThrowIfDisposed();
            if (!focused) return;
            if (!held.Remove(key)) return;
            modifiers = mods | HeldModifiers();
            released.Add(key);
            // Key-up modifier packets may still include the key being released.
            if (key is Keys.LeftShift or Keys.RightShift && !held.Contains(Keys.LeftShift) && !held.Contains(Keys.RightShift)) modifiers &= ~KeyModifiers.Shift;
            if (key is Keys.LeftControl or Keys.RightControl && !held.Contains(Keys.LeftControl) && !held.Contains(Keys.RightControl)) modifiers &= ~KeyModifiers.Control;
            if (key is Keys.LeftAlt or Keys.RightAlt && !held.Contains(Keys.LeftAlt) && !held.Contains(Keys.RightAlt)) modifiers &= ~KeyModifiers.Alt;
            if (key is Keys.LeftSuper or Keys.RightSuper && !held.Contains(Keys.LeftSuper) && !held.Contains(Keys.RightSuper)) modifiers &= ~KeyModifiers.Super;

        }

        public void TextInput(string text)
        {
            ThrowIfDisposed();
            if (!focused || string.IsNullOrEmpty(text) || (Command(modifiers) && (modifiers & KeyModifiers.Alt) == 0)) return;
            foreach (char value in text) if (!char.IsControl(value)) characters.Append(value);
        }

        /// <summary>Starts a native pre-edit session. Windowless hosts can use these composition helpers too.</summary>
        public void CompositionStart()
        {
            ThrowIfDisposed();
            if (!focused || !imeEnabled) return;
            composing = compositionActivity = true;
            composition = null;
            ReleaseEditingKeys();
        }

        /// <summary>Replaces the uncommitted text without changing the editor's stored value.</summary>
        public void CompositionUpdate(string value)
        {
            ThrowIfDisposed();
            if (!focused || !imeEnabled) return;
            composing = compositionActivity = true;
            composition = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Commits Unicode text once; subsequent pre-edit updates may continue the session.</summary>
        public void CompositionCommit(string value)
        {
            ThrowIfDisposed();
            if (!focused || !imeEnabled) return;
            compositionActivity = true;
            composing = false;
            composition = null;
            // Composition results are text, even when Ctrl/Alt helped choose a candidate.
            if (!string.IsNullOrEmpty(value))
                foreach (char character in value) if (!char.IsControl(character)) characters.Append(character);
        }

        /// <summary>Ends or cancels pre-edit text; committed characters already queued are preserved.</summary>
        public void CompositionEnd()
        {
            ThrowIfDisposed();
            if (composing || composition != null) compositionActivity = true;
            composing = false;
            composition = null;
        }

        void SetImeEnabled(bool value)
        {
            ThrowIfDisposed();
            imeEnabled = value;
            ime?.SetEnabled(value && focused);
            if (value && focused && lastInputWasTouch) touchKeyboard?.Show();
            else if (!value) touchKeyboard?.Hide();
            if (!value) CompositionEnd();
        }

        void SetCompositionCursor(Vector2 position)
        {
            ThrowIfDisposed();
            position = Now.TransformScreenPoint(position);
            position = new Vector2(position.x * lastUiScale * clientWidth / framebufferWidth,
                position.y * lastUiScale * clientHeight / framebufferHeight);
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y)) return;
            CompositionCursorClient = position;
            ime?.SetCursor((int)MathF.Round(position.x), (int)MathF.Round(position.y));
        }

        void ReleaseEditingKeys()
        {
            foreach (var key in EditingKeys)
            {
                held.Remove(key);
                pressed.Remove(key);
            }
        }

        static readonly Keys[] EditingKeys = { Keys.Backspace, Keys.Delete, Keys.Left, Keys.Right, Keys.Up, Keys.Down,
            Keys.Home, Keys.End, Keys.Enter, Keys.KeyPadEnter, Keys.Escape, Keys.Tab, Keys.Space };

        public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot value) { value = snapshot; return true; }
        public bool TryGetFrame(out NowTextInputFrame value) { value = textFrame; return true; }
        public void DiscardPendingText()
        {
            characters.Clear(); pendingText.characters = null; textFrame.characters = null;
            if (composing) ime?.Cancel();
            CompositionEnd();
            textFrame.composition = null;
        }
        public string GetText() { ThrowIfDisposed(); return window == null ? localClipboard : window.ClipboardString; }
        public void SetText(string value) { ThrowIfDisposed(); if (window == null) localClipboard = value ?? string.Empty; else window.ClipboardString = value ?? string.Empty; }

        void OnMouseMove(MouseMoveEventArgs e) => PointerMove(e.X, e.Y);
        void OnMouseDown(MouseButtonEventArgs e) => PointerDown(window.MousePosition.X, window.MousePosition.Y, (int)e.Button);
        void OnMouseUp(MouseButtonEventArgs e) => PointerUp(window.MousePosition.X, window.MousePosition.Y, (int)e.Button);
        void OnMouseWheel(MouseWheelEventArgs e) => Scroll(e.OffsetX, e.OffsetY);
        void OnMouseEnter() => PointerMove(window.MousePosition.X, window.MousePosition.Y);
        void OnFocusedChanged(FocusedChangedEventArgs e) => SetFocused(e.IsFocused);
        void OnKeyDown(KeyboardKeyEventArgs e)
        {
            if (!e.IsRepeat) RefreshKeyName(e.Key);
            KeyDown(e.Key, e.Modifiers, e.IsRepeat);
        }
        void OnKeyUp(KeyboardKeyEventArgs e) => KeyUp(e.Key, e.Modifiers);
        void OnTextInput(TextInputEventArgs e) => TextInput(e.AsString);
        void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);

        public void Dispose()
        {
            if (disposed) return;
            touchKeyboard?.Dispose();
            windowsPointer?.Dispose();
            ime?.Dispose();
            if (window != null)
            {
                window.MouseMove -= OnMouseMove;
                window.MouseDown -= OnMouseDown;
                window.MouseUp -= OnMouseUp;
                window.MouseWheel -= OnMouseWheel;
                window.MouseEnter -= OnMouseEnter;
                window.MouseLeave -= PointerLeave;
                window.FocusedChanged -= OnFocusedChanged;
                window.KeyDown -= OnKeyDown;
                window.KeyUp -= OnKeyUp;
                window.TextInput -= OnTextInput;
            }
            if (installed)
            {
                if (ReferenceEquals(NowInput.defaultProvider, this)) NowInput.defaultProvider = previousProvider;
                if (ReferenceEquals(NowTextInput.source, this))
                {
                    NowTextInput.source = previousTextSource;
                    if (NowTextInput.isMacPlatform == mac) NowTextInput.isMacPlatform = previousMac;
                    NowTextInput.Invalidate();
                }
                if (ReferenceEquals(NowKeyInput.source, this)) { NowKeyInput.source = previousKeySource; NowKeyInput.Invalidate(); }
                if (NowKeyNames.nativeDisplayName == KeyName) NowKeyNames.nativeDisplayName = previousKeyNames;
                if (NowTextInput.setImeEnabled == SetImeEnabled) NowTextInput.setImeEnabled = previousImeEnabled;
                if (NowTextInput.setCompositionCursor == SetCompositionCursor) NowTextInput.setCompositionCursor = previousCompositionCursor;
            }
            pointer.Reset();
            pointerEvents.Clear(); held.Clear(); pressed.Clear(); characters.Clear();
            disposed = true;
        }
    }
}
