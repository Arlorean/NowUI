using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Text;
using System.Threading.Tasks;
using NowUI.Engine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace NowUI.Browser
{
    /// <summary>DOM input transport for the existing NowUI pointer, keyboard, text and clipboard contracts.</summary>
    public sealed partial class BrowserInput : INowInputProvider, INowTextInputSource, INowTextInputBuffer,
        INowKeyInputSource, INowClipboard, IDisposable
    {
        readonly NowUIToolkitInputProvider pointer = new();
        readonly HashSet<Key> held = new(), pressed = new(), released = new();
        readonly Pad[] pads = new Pad[16], previousPads = new Pad[16];
        readonly bool mac;
        NowInputSnapshot snapshot;
        NowTextInputFrame text;
        NowKeyInputFrame keyFrame;
        INowInputProvider previousProvider;
        INowTextInputSource previousText;
        INowKeyInputSource previousKeys;
        Action<bool> previousIme;
        Action<Vector2> previousCursor;
        bool previousMac, attached, installed, disposed, focused = true, imeEnabled;
        bool cancelled, sourceChanged, compositionActive, compositionActivity;
        int modifiers, lastFrame = -1, currentPad = -1;
        string localClipboard = "";
        float scale = 1;

        struct Pad { public bool connected; public Vector2 stick; public int buttons; }

        internal BrowserInput(bool isMac = false) { mac = isMac; }
        public INowClipboard Clipboard => this;
        public INowInputProvider Provider => this;

        /// <summary>Imports the internal event transport and attaches it to one canvas.</summary>
        public static async Task<BrowserInput> CreateAsync(string canvasSelector, float uiScale = 1,
            string modulePath = "./nowui-input.js")
        {
            if (string.IsNullOrWhiteSpace(canvasSelector)) throw new ArgumentException("A canvas selector is required.", nameof(canvasSelector));
            if (!(uiScale > 0) || !float.IsFinite(uiScale)) throw new ArgumentOutOfRangeException(nameof(uiScale));
            await JSHost.ImportAsync(Interop.ModuleName, modulePath);
            // Numeric key identities come from the shared enum, not a second JavaScript public API.
            var map = new StringBuilder();
            foreach (string name in Enum.GetNames<Key>()) map.Append(name).Append(':').Append((int)Enum.Parse<Key>(name)).Append('|');
            bool isMac = Interop.Init(canvasSelector, uiScale, map.ToString());
            return new BrowserInput(isMac) { attached = true, scale = uiScale };
        }

        public void Install()
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (installed) return;
            previousProvider = NowInput.defaultProvider; previousText = NowTextInput.source; previousKeys = NowKeyInput.source;
            previousIme = NowTextInput.setImeEnabled; previousCursor = NowTextInput.setCompositionCursor; previousMac = NowTextInput.isMacPlatform;
            NowInput.defaultProvider = this; NowTextInput.source = this; NowKeyInput.source = this;
            NowTextInput.isMacPlatform = mac; NowTextInput.setImeEnabled = SetIme; NowTextInput.setCompositionCursor = SetCursor;
            NowTextInput.Invalidate(); NowKeyInput.Invalidate();
            installed = true;
        }

        /// <summary>Call once after NowRuntime.BeginFrame and before Now.StartUI. Pointer positions are UI units.</summary>
        public void Drain(float uiScale = 1)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!(uiScale > 0) || !float.IsFinite(uiScale)) throw new ArgumentOutOfRangeException(nameof(uiScale));
            if (lastFrame == Time.frameCount) return;
            if (scale != uiScale) { scale = uiScale; if (attached) Interop.SetUiScale(uiScale); }
            if (!attached) { DrainPacket(ReadOnlySpan<double>.Empty, null, null); return; }
            var events = Interop.Drain();
            DrainPacket(events, Interop.DrainCharacters(), Interop.Composition());
        }

        // Tests replay the exact transport records through this same control-facing adapter.
        internal void DrainPacket(ReadOnlySpan<double> events, string characters = null, string composition = null)
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            pressed.Clear(); released.Clear(); text = default; keyFrame = default;
            cancelled = sourceChanged = compositionActivity = false;
            int oldPad = currentPad;
            for (int i = 0; i + 5 <= events.Length; i += 5)
            {
                int kind = (int)events[i];
                float a = (float)events[i + 1], b = (float)events[i + 2];
                int c = (int)events[i + 3], d = (int)events[i + 4];
                switch (kind)
                {
                    case 1: pointer.SetPointerPosition(new Vector2(a, b), c); break;
                    case 2: pointer.SetPointerDown(new Vector2(a, b), c, d); break;
                    case 3: pointer.SetPointerUp(new Vector2(a, b), c, d); break;
                    case 4: pointer.CancelPointer(); cancelled = true; break;
                    case 6: pointer.ClearPointer(); break;
                    case 7: pointer.AddScrollDelta(new Vector2(a * 3, -b * 3)); break;
                    case 8: KeyDown((Key)(int)a, (int)b, c != 0); break;
                    case 9: KeyUp((Key)(int)a, (int)b); break;
                    case 10:
                        focused = a != 0;
                        if (!focused)
                        {
                            held.Clear(); pressed.Clear(); released.Clear(); modifiers = 0;
                            pointer.Reset(); cancelled = true; compositionActive = false;
                            text = default; keyFrame = default;
                        }
                        break;
                    case 15: pointer.Reset(); sourceChanged = true; break;
                    case 16: compositionActive = a != 0; compositionActivity = true; break;
                    case 17: text.pastePressed = true; break;
                    case 18: text.copyPressed = true; break;
                    case 19: text.cutPressed = true; break;
                    case 20:
                        int index = (int)a;
                        if ((uint)index < pads.Length)
                        {
                            var value = new Pad { connected = true, stick = new Vector2(b, (float)events[i + 3]), buttons = d };
                            var old = pads[index];
                            if (currentPad < 0 || ((value.buttons != 0 || value.stick.sqrMagnitude > .015625f) &&
                                (value.buttons != old.buttons || value.stick != old.stick))) currentPad = index;
                            pads[index] = value;
                        }
                        break;
                    case 21: if ((uint)(int)a < pads.Length) pads[(int)a] = default; break;
                    // Soft keyboards can edit without hardware key events.
                    case 22: if ((int)a == 1) pressed.Add(Key.Backspace); else if ((int)a == 2) pressed.Add(Key.Delete);
                        else if ((int)a == 3) { pressed.Add(Key.Enter); text.enterPressed = true; } break;
                }
            }
            if (currentPad >= 0 && !pads[currentPad].connected) currentPad = -1;
            for (int i = 0; currentPad < 0 && i < pads.Length; i++) if (pads[i].connected) currentPad = i;
            var navigationKeys = NowInput.navigationKeys;
            bool arrows = (navigationKeys & NowNavigationKeys.Arrows) != 0, wasd = (navigationKeys & NowNavigationKeys.Wasd) != 0;
            Vector2 navigation = new(
                (arrows && held.Contains(Key.RightArrow) || wasd && held.Contains(Key.D) ? 1 : 0) - (arrows && held.Contains(Key.LeftArrow) || wasd && held.Contains(Key.A) ? 1 : 0),
                (arrows && held.Contains(Key.UpArrow) || wasd && held.Contains(Key.W) ? 1 : 0) - (arrows && held.Contains(Key.DownArrow) || wasd && held.Contains(Key.S) ? 1 : 0));
            bool submitDown = false, submitPressed = false, submitReleased = false, cancelDown = false, cancelPressed = false, cancelReleased = false;
            if ((navigationKeys & NowNavigationKeys.EnterSubmit) != 0)
            { Merge(Key.Enter, ref submitDown, ref submitPressed, ref submitReleased); Merge(Key.NumpadEnter, ref submitDown, ref submitPressed, ref submitReleased); }
            if ((navigationKeys & NowNavigationKeys.SpaceSubmit) != 0) Merge(Key.Space, ref submitDown, ref submitPressed, ref submitReleased);
            Merge(Key.Escape, ref cancelDown, ref cancelPressed, ref cancelReleased);
            if (currentPad >= 0)
            {
                var pad = pads[currentPad]; var previous = previousPads[currentPad];
                float length = pad.stick.magnitude;
                if (float.IsFinite(length) && length > .125f) navigation += pad.stick * (Math.Clamp((length - .125f) / .8f, 0, 1) / length);
                navigation += new Vector2(Bit(pad.buttons, 15) - Bit(pad.buttons, 14), Bit(pad.buttons, 12) - Bit(pad.buttons, 13));
                MergePad(pad.buttons, previous.buttons, 0, ref submitDown, ref submitPressed, ref submitReleased);
                MergePad(pad.buttons, previous.buttons, 9, ref submitDown, ref submitPressed, ref submitReleased);
                MergePad(pad.buttons, previous.buttons, 1, ref cancelDown, ref cancelPressed, ref cancelReleased);
                MergePad(pad.buttons, previous.buttons, 8, ref cancelDown, ref cancelPressed, ref cancelReleased);
            }
            else if (oldPad >= 0)
            {
                int previous = previousPads[oldPad].buttons;
                submitReleased |= (previous & 513) != 0; cancelReleased |= (previous & 258) != 0;
            }
            Array.Copy(pads, previousPads, pads.Length);
            text.characters = focused && (!Command || (modifiers & 4) != 0 || compositionActivity) ? characters : null;
            text.composition = focused ? composition : null;
            text.backspaceHeld = Down(Key.Backspace); text.deleteHeld = Down(Key.Delete);
            text.leftHeld = Down(Key.LeftArrow); text.rightHeld = Down(Key.RightArrow); text.upHeld = Down(Key.UpArrow); text.downHeld = Down(Key.DownArrow);
            text.enterHeld = Down(Key.Enter) || Down(Key.NumpadEnter); text.tabHeld = Down(Key.Tab);
            text.shift = (modifiers & 1) != 0; text.command = Command; text.option = (modifiers & 4) != 0;
            bool tab = (navigationKeys & NowNavigationKeys.TabFocus) != 0 && pressed.Contains(Key.Tab);
            bool imeOwnsKeys = compositionActive || compositionActivity || !string.IsNullOrEmpty(composition);
            if (imeOwnsKeys)
            {
                keyFrame = default;
                text = new NowTextInputFrame { characters = text.characters, composition = text.composition,
                    shift = text.shift, command = text.command, option = text.option };
                foreach (var key in EditingKeys) { held.Remove(key); pressed.Remove(key); }
            }
            bool keysEnabled = focused && !imeOwnsKeys;
            pointer.TryGetSnapshot(new NowInputSurface(Vector2.one), out var p);
            snapshot = new NowInputSnapshot(p.hasPointer, p.pointerPosition, sourceChanged ? p.pointerPosition : p.previousPointerPosition,
                sourceChanged ? Vector2.zero : p.pointerDelta, p.pointerButtonsDown, p.pointerButtonsPressed, p.pointerButtonsReleased,
                p.scrollDelta, keysEnabled ? Vector2.ClampMagnitude(navigation, 1) : Vector2.zero,
                keysEnabled && tab && text.shift, keysEnabled && tab && !text.shift,
                keysEnabled && submitDown, keysEnabled && submitPressed, keysEnabled && submitReleased,
                keysEnabled && cancelDown, keysEnabled && cancelPressed, keysEnabled && cancelReleased, p.frame, p.time);
            snapshot.pointerCaptureCancelled = cancelled;
        }

        static readonly Key[] EditingKeys = { Key.Backspace, Key.Delete, Key.LeftArrow, Key.RightArrow, Key.UpArrow, Key.DownArrow,
            Key.Home, Key.End, Key.Enter, Key.NumpadEnter, Key.Tab, Key.Space, Key.Escape };
        bool Down(Key key) => held.Contains(key) || pressed.Contains(key);
        bool Command => (modifiers & (mac ? 8 : 2)) != 0;
        static int Bit(int mask, int bit) => (mask >> bit) & 1;
        void Merge(Key key, ref bool down, ref bool edge, ref bool up) { down |= held.Contains(key); edge |= pressed.Contains(key); up |= released.Contains(key); }
        static void MergePad(int current, int previous, int bit, ref bool down, ref bool edge, ref bool up)
        { bool now = Bit(current, bit) != 0, before = Bit(previous, bit) != 0; down |= now; edge |= now && !before; up |= before && !now; }

        void KeyDown(Key key, int mods, bool repeat)
        {
            modifiers = mods;
            if (!focused || key == Key.None) return;
            bool first = held.Add(key);
            if (!first || repeat || compositionActive || compositionActivity) return;
            pressed.Add(key);
            if (keyFrame.pressedKey == Key.None || key < keyFrame.pressedKey) keyFrame.pressedKey = key;
            switch (key)
            {
                case Key.Home: text.homePressed = true; break; case Key.End: text.endPressed = true; break;
                case Key.Enter: case Key.NumpadEnter: text.enterPressed = true; break;
                case Key.Escape: text.escapePressed = true; break; case Key.Tab: text.tabPressed = true; break;
                case Key.F2: text.renamePressed = true; break;
            }
            if (!Command || (mods & 4) != 0) return;
            switch (key)
            {
                case Key.C: text.copyPressed = true; break; case Key.X: text.cutPressed = true; break;
                // Paste is emitted by the DOM paste event, when synchronous clipboard contents are available.
                case Key.A: text.selectAllPressed = true; break;
                case Key.Z: if ((mods & 1) != 0) text.redoPressed = true; else text.undoPressed = true; break;
                case Key.Y: text.redoPressed = true; break; case Key.D: text.duplicatePressed = true; break;
                case Key.Slash: text.commentPressed = true; break; case Key.G: text.goToLinePressed = true; break;
            }
        }
        void KeyUp(Key key, int mods) { modifiers = mods; if (held.Remove(key)) released.Add(key); }
        void SetIme(bool value) { imeEnabled = value; if (attached) Interop.SetImeEnabled(value); }
        void SetCursor(Vector2 value)
        {
            value = Now.TransformScreenPoint(value);
            if (attached && float.IsFinite(value.x) && float.IsFinite(value.y)) Interop.SetCompositionCursor(value.x, value.y);
        }
        public void DiscardPendingText() { text.characters = text.composition = null; if (attached) Interop.DiscardPendingText(); }
        public string GetText() => attached ? Interop.GetClipboard() ?? "" : localClipboard;
        public void SetText(string value) { localClipboard = value ?? ""; if (attached) Interop.SetClipboard(localClipboard); }
        public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot value) { value = snapshot; return true; }
        public bool TryGetFrame(out NowTextInputFrame value) { value = text; return true; }
        bool INowKeyInputSource.TryGetFrame(out NowKeyInputFrame value) { value = keyFrame; return true; }

        public void Dispose()
        {
            if (disposed) return;
            if (attached) Interop.Detach();
            if (installed)
            {
                if (ReferenceEquals(NowInput.defaultProvider, this)) NowInput.defaultProvider = previousProvider;
                if (ReferenceEquals(NowTextInput.source, this)) { NowTextInput.source = previousText; NowTextInput.isMacPlatform = previousMac; NowTextInput.Invalidate(); }
                if (ReferenceEquals(NowKeyInput.source, this)) { NowKeyInput.source = previousKeys; NowKeyInput.Invalidate(); }
                if (NowTextInput.setImeEnabled == SetIme) NowTextInput.setImeEnabled = previousIme;
                if (NowTextInput.setCompositionCursor == SetCursor) NowTextInput.setCompositionCursor = previousCursor;
            }
            pointer.Reset(); disposed = true;
        }

        internal static partial class Interop
        {
            internal const string ModuleName = "nowui-input";
            [JSImport("init", ModuleName)] internal static partial bool Init(string selector, double scale, string keys);
            [JSImport("setUiScale", ModuleName)] internal static partial void SetUiScale(double value);
            [JSImport("drain", ModuleName)] [return: JSMarshalAs<JSType.Array<JSType.Number>>] internal static partial double[] Drain();
            [JSImport("drainCharacters", ModuleName)] internal static partial string DrainCharacters();
            [JSImport("composition", ModuleName)] internal static partial string Composition();
            [JSImport("setImeEnabled", ModuleName)] internal static partial void SetImeEnabled(bool value);
            [JSImport("setCompositionCursor", ModuleName)] internal static partial void SetCompositionCursor(double x, double y);
            [JSImport("discardPendingText", ModuleName)] internal static partial void DiscardPendingText();
            [JSImport("getClipboard", ModuleName)] internal static partial string GetClipboard();
            [JSImport("setClipboard", ModuleName)] internal static partial void SetClipboard(string value);
            [JSImport("detach", ModuleName)] internal static partial void Detach();
        }
    }
}
