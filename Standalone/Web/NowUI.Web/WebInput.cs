// The managed half of the input bridge: drain the browser's event queue once per frame and replay it into the
// seams NowUI already has.
//
// There are four of those seams and none of them needed a change under Assets/ (M2-InputContract.md section 0):
//
//   NowInput.defaultProvider   -> NowUIToolkitInputProvider, fed pointer, wheel and navigation keys. Not optional:
//                                 Now.StartUI reaches the provider through this static and through nothing else,
//                                 and left at its default every frame samples NowScreenInputProvider, which reads
//                                 Unity input that is compiled out - so the UI stays inert with no error at all.
//   NowTextInput.source        -> WebTextInputSource. In the standalone build the default source's whole body is
//                                 inside #if NOWUI_INPUT_SYSTEM / #if ENABLE_LEGACY_INPUT_MANAGER and compiles down
//                                 to `return false`, so text entry is ABSENT rather than degraded until this is set.
//   NowTextInput.isMacPlatform -> from the user agent. Auto-detection tests Application.platform for OSX*, and a
//                                 browser reports WebGLPlayer, so it is false in Safari on a Mac too - every Cmd
//                                 shortcut would silently do nothing there.
//   INowHostServices.clipboard -> WebClipboard (see WebHostServices.clipboard).
//
// The one rule that makes the whole thing work is where the drain happens: after NowRuntime.BeginFrame, before
// Now.StartUI. NowUIToolkitInputProvider keys its buffer lifecycle off Time.frameCount - the first read of a new
// frame folds every buffered event into one snapshot and immediately clears the one-shot state - so anything fed
// after that first read is invisible until the next frame, and anything read before BeginFrame stamps the buffer
// with the old frame number and throws the new frame's events away. See Program.cs' Frame().
//
// Design: Docs/Standalone/M2-InputContract.md.
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;
using NowUI.Engine;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>
    /// Browser input wired into NowUI's provider, text and clipboard seams.
    /// </summary>
    public sealed partial class WebInput
    {
        // Must match the constants at the top of wwwroot/nowui-input.js. Five doubles per record: kind and four
        // payload slots, all optional. A flat number array rather than a JSON object graph because the browser host
        // publishes trimmed, and a reflection-based serializer is exactly what trimming breaks.
        private const int k_RecordStride = 5;

        private const int k_KindPointerMove = 1;
        private const int k_KindPointerDown = 2;
        private const int k_KindPointerUp = 3;
        private const int k_KindPointerCancel = 4;
        private const int k_KindPointerEnter = 5;
        private const int k_KindPointerLeave = 6;
        private const int k_KindScroll = 7;
        private const int k_KindKeyDown = 8;
        private const int k_KindKeyUp = 9;
        private const int k_KindTextState = 10;

        private const int k_HeldBackspace = 1 << 0;
        private const int k_HeldDelete = 1 << 1;
        private const int k_HeldLeft = 1 << 2;
        private const int k_HeldRight = 1 << 3;
        private const int k_HeldUp = 1 << 4;
        private const int k_HeldDown = 1 << 5;
        private const int k_HeldEnter = 1 << 6;
        private const int k_HeldTab = 1 << 7;

        private const int k_PressedHome = 1 << 0;
        private const int k_PressedEnd = 1 << 1;
        private const int k_PressedEnter = 1 << 2;
        private const int k_PressedEscape = 1 << 3;
        private const int k_PressedTab = 1 << 4;
        private const int k_PressedRename = 1 << 5;
        private const int k_PressedCopy = 1 << 6;
        private const int k_PressedPaste = 1 << 7;
        private const int k_PressedCut = 1 << 8;
        private const int k_PressedSelectAll = 1 << 9;
        private const int k_PressedUndo = 1 << 10;
        private const int k_PressedRedo = 1 << 11;
        private const int k_PressedDuplicate = 1 << 12;
        private const int k_PressedComment = 1 << 13;
        private const int k_PressedGoToLine = 1 << 14;

        private const int k_ModShift = 1 << 0;
        private const int k_ModCommand = 1 << 1;
        private const int k_ModOption = 1 << 2;

        /// <summary>Where <see cref="CreateAsync"/> looks for <c>nowui-input.js</c>.</summary>
        /// <remarks>
        /// One level up for the same reason <see cref="WebGL2Backend.DefaultModulePath"/> is: a relative specifier
        /// given to <c>JSHost.ImportAsync</c> resolves against the runtime's own script under <c>_framework/</c>,
        /// so <c>"./nowui-input.js"</c> asks for a file that is not there.
        /// </remarks>
        public const string DefaultModulePath = "../nowui-input.js";

        private readonly NowUIToolkitInputProvider m_Provider = new NowUIToolkitInputProvider();
        private readonly WebTextInputSource m_TextSource = new WebTextInputSource();
        private readonly WebClipboard m_Clipboard = new WebClipboard();

        private float m_UiScale = 1f;
        private bool m_IsMacPlatform;

        private WebInput()
        {
        }

        /// <summary>The provider the frame loop feeds and <c>Now.StartUI</c> samples.</summary>
        public NowUIToolkitInputProvider provider
        {
            get { return m_Provider; }
        }

        /// <summary>The browser clipboard, for <see cref="WebHostServices.clipboard"/>.</summary>
        public INowClipboard clipboard
        {
            get { return m_Clipboard; }
        }

        /// <summary>True when the user agent looks like macOS or iOS, so Command drives word and line editing.</summary>
        public bool isMacPlatform
        {
            get { return m_IsMacPlatform; }
        }

        /// <summary>
        /// Imports the JavaScript module and attaches its listeners to the canvas.
        /// </summary>
        /// <remarks>
        /// Asynchronous for the same reason the backend's factory is: a <c>[JSImport]</c> cannot be called before
        /// its module has loaded, and there is no synchronous way to load one.
        /// </remarks>
        public static async Task<WebInput> CreateAsync(
            string canvasSelector,
            float uiScale,
            string modulePath = DefaultModulePath)
        {
            if (string.IsNullOrEmpty(canvasSelector))
                throw new ArgumentException("A CSS selector for the canvas is required.", nameof(canvasSelector));

            var input = new WebInput();
            await JSHost.ImportAsync(Interop.ModuleName, modulePath).ConfigureAwait(false);

            input.m_UiScale = uiScale > 0f ? uiScale : 1f;

            string report = Interop.Init(canvasSelector, input.m_UiScale) ?? string.Empty;
            string[] parts = report.Split('|');
            input.m_IsMacPlatform = parts.Length > 0 && parts[0] == "1";

            return input;
        }

        /// <summary>
        /// Points NowUI's four input statics at this bridge. Called once, before the first frame.
        /// </summary>
        public void Install()
        {
            NowInput.defaultProvider = m_Provider;
            NowTextInput.source = m_TextSource;
            NowTextInput.isMacPlatform = m_IsMacPlatform;

            // Arrows, Tab, Enter and Space drive the UI; WASD does not. The default is NowNavigationKeys.All, and in
            // a browser app - where any screen may hold a text field - W/A/S/D moving focus is a collision waiting
            // to happen. NowTextField does register a NowFocusNavigationLock.Directional, but locks are effective
            // only on the next frame swap and several other controls read NowInput.current.navigation directly.
            // The doc comment on navigationKeys anticipates exactly this case (section 4.4).
            //
            // The JS side never sends the letters at all, so this is belt and braces rather than the only defence -
            // but it is the line a host would flip to get gamepad-style navigation back, so it belongs here.
            NowInput.navigationKeys =
                NowNavigationKeys.Arrows |
                NowNavigationKeys.TabFocus |
                NowNavigationKeys.EnterSubmit |
                NowNavigationKeys.SpaceSubmit;
        }

        /// <summary>
        /// Replays everything the page buffered since the last call into the provider and the text source.
        /// </summary>
        /// <remarks>
        /// MUST run after <c>NowRuntime.BeginFrame</c> and before <c>Now.StartUI</c>. Feeding the provider is not
        /// the same as reading it - nothing here calls <c>TryGetSnapshot</c> - so the ordering rule this satisfies
        /// is the narrow one: every event of this frame is in the buffer before the frame's single snapshot is
        /// built, and none arrives after it has been built and cleared.
        /// <para><c>Invalidate()</c> is deliberately never called. Its doc comment says it is for hosts where
        /// frameCount is static; here frameCount advances every frame, and calling it mid-frame would rebuild the
        /// snapshot from an already-drained buffer and hand the rest of the frame an empty one.</para>
        /// </remarks>
        public void Drain(float uiScale)
        {
            if (uiScale > 0f && uiScale != m_UiScale)
            {
                m_UiScale = uiScale;
                Interop.SetUiScale(uiScale);
            }

            // Cleared every frame whether or not a text field reads it. NowTextInput.current calls TryGetFrame at
            // most once per frameCount and only when something asks for text, so edges left staged would otherwise
            // pile up while no field is drawn and then all fire at once the moment one appears.
            m_TextSource.BeginFrame();

            double[] events = Interop.Drain();

            if (events != null)
            {
                for (int i = 0; i + k_RecordStride <= events.Length; i += k_RecordStride)
                    Apply(events, i);
            }

            // Separate call only because a string cannot ride inside an array of numbers. Same frame, same point.
            m_TextSource.SetCharacters(Interop.DrainCharacters());
        }

        private void Apply(double[] events, int offset)
        {
            int kind = (int)events[offset];
            double a = events[offset + 1];
            double b = events[offset + 2];
            double c = events[offset + 3];
            double d = events[offset + 4];

            switch (kind)
            {
                case k_KindPointerMove:
                    m_Provider.SetPointerPosition(new Vector2((float)a, (float)b), (int)c);
                    break;

                case k_KindPointerDown:
                    m_Provider.SetPointerDown(new Vector2((float)a, (float)b), (int)c, (int)d);
                    break;

                case k_KindPointerUp:
                    m_Provider.SetPointerUp(new Vector2((float)a, (float)b), (int)c, (int)d);
                    break;

                case k_KindPointerCancel:
                    // Known limitation, recorded rather than worked around: CancelPointer moves the held buttons
                    // into the released mask, so Interact sees `released` and a drag in progress reports dragEnded
                    // rather than dragCancelled. NowInputSnapshot HAS a pointerCaptureCancelled field for this and
                    // Interact honours it, but BuildSnapshot hard-codes it false and the snapshot comes back by
                    // value, so no host can set it. Frozen tree; section 2.6 and finding 1.
                    m_Provider.CancelPointer();
                    break;

                case k_KindPointerEnter:
                    m_Provider.SetPointerPosition(new Vector2((float)a, (float)b));
                    break;

                case k_KindPointerLeave:
                    m_Provider.SetPointerPosition(new Vector2((float)a, (float)b), (int)c);
                    if ((int)c == 0)
                        m_Provider.ClearPointer();
                    break;

                case k_KindScroll:
                    // Already in UI Toolkit units on the JS side; AddScrollDelta does the /3 and the y negation.
                    m_Provider.AddScrollDelta(new Vector2((float)a, (float)b));
                    break;

                case k_KindKeyDown:
                    m_Provider.KeyDown((KeyCode)(int)a, b != 0d);
                    break;

                case k_KindKeyUp:
                    m_Provider.KeyUp((KeyCode)(int)a);
                    break;

                case k_KindTextState:
                    m_TextSource.SetState((int)a, (int)b, (int)c);
                    break;
            }
        }

        /// <summary>
        /// <see cref="INowTextInputSource"/> over the browser keyboard, staged once per frame by
        /// <see cref="WebInput.Drain"/>.
        /// </summary>
        /// <remarks>
        /// The held/pressed split is load-bearing and is the thing most likely to be got wrong. The Held fields are
        /// LEVEL - true for as long as the key is physically down - because NowUI generates its own key repeat from
        /// them (NowControlState.Repeat: one pulse immediately, then 20 Hz after 0.4 s). Report an edge there and a
        /// held Backspace deletes one character; forward the browser's auto-repeat on top of NowUI's and it deletes
        /// at roughly twice the intended rate.
        /// <para>Composition is not implemented: <c>composition</c> stays null, and NowTextInput's
        /// <c>setImeEnabled</c> / <c>setCompositionCursor</c> hooks are left alone (both compile down to nothing on
        /// the standalone build). Latin, Cyrillic and Greek typing, every editing key, every shortcut and selection
        /// all work; Japanese, Chinese and Korean input does not, because that needs a real focusable editable
        /// element driven by compositionstart/update/end. It is additive - nothing here changes to add it.</para>
        /// </remarks>
        private sealed class WebTextInputSource : INowTextInputSource, INowTextInputBuffer
        {
            private NowTextInputFrame m_Frame;

            /// <summary>Drops last frame's edges and characters. Called at the top of every drain.</summary>
            internal void BeginFrame()
            {
                m_Frame = default;
            }

            internal void SetCharacters(string characters)
            {
                m_Frame.characters = string.IsNullOrEmpty(characters) ? null : characters;
                ApplyChordCharacterGuard();
            }

            internal void SetState(int held, int pressed, int modifiers)
            {
                m_Frame.backspaceHeld = (held & k_HeldBackspace) != 0;
                m_Frame.deleteHeld = (held & k_HeldDelete) != 0;
                m_Frame.leftHeld = (held & k_HeldLeft) != 0;
                m_Frame.rightHeld = (held & k_HeldRight) != 0;
                m_Frame.upHeld = (held & k_HeldUp) != 0;
                m_Frame.downHeld = (held & k_HeldDown) != 0;
                m_Frame.enterHeld = (held & k_HeldEnter) != 0;
                m_Frame.tabHeld = (held & k_HeldTab) != 0;

                m_Frame.homePressed = (pressed & k_PressedHome) != 0;
                m_Frame.endPressed = (pressed & k_PressedEnd) != 0;
                m_Frame.enterPressed = (pressed & k_PressedEnter) != 0;
                m_Frame.escapePressed = (pressed & k_PressedEscape) != 0;
                m_Frame.tabPressed = (pressed & k_PressedTab) != 0;
                m_Frame.renamePressed = (pressed & k_PressedRename) != 0;
                m_Frame.copyPressed = (pressed & k_PressedCopy) != 0;
                m_Frame.pastePressed = (pressed & k_PressedPaste) != 0;
                m_Frame.cutPressed = (pressed & k_PressedCut) != 0;
                m_Frame.selectAllPressed = (pressed & k_PressedSelectAll) != 0;
                m_Frame.undoPressed = (pressed & k_PressedUndo) != 0;
                m_Frame.redoPressed = (pressed & k_PressedRedo) != 0;
                m_Frame.duplicatePressed = (pressed & k_PressedDuplicate) != 0;
                m_Frame.commentPressed = (pressed & k_PressedComment) != 0;
                m_Frame.goToLinePressed = (pressed & k_PressedGoToLine) != 0;

                m_Frame.shift = (modifiers & k_ModShift) != 0;
                m_Frame.command = (modifiers & k_ModCommand) != 0;
                m_Frame.option = (modifiers & k_ModOption) != 0;

                ApplyChordCharacterGuard();
            }

            /// <summary>
            /// Mirrors the default source: under <c>command &amp;&amp; !option</c> the whole frame's characters are
            /// dropped, so Ctrl+V does not insert a "v" beside the paste. The `!option` half is not decoration -
            /// AltGr arrives as Ctrl+Alt on Windows, and guarding on command alone makes every AltGr character
            /// disappear on European layouts.
            /// </summary>
            private void ApplyChordCharacterGuard()
            {
                if (m_Frame.command && !m_Frame.option)
                    m_Frame.characters = null;
            }

            /// <inheritdoc />
            public bool TryGetFrame(out NowTextInputFrame frame)
            {
                // A consuming read by contract, and it is consumed by BeginFrame rather than here: NowTextInput
                // calls this at most once per Time.frameCount, but only when a control actually asks for text, and
                // clearing on the frame boundary is what keeps an unread frame from leaking into the next one.
                frame = m_Frame;
                return true;
            }

            /// <inheritdoc />
            public void DiscardPendingText()
            {
                m_Frame.characters = null;
                Interop.DiscardPendingText();
            }
        }

        /// <summary>
        /// <see cref="INowClipboard"/> over the browser clipboard.
        /// </summary>
        /// <remarks>
        /// The interface is synchronous and the browser's clipboard read is not, so no implementation can be
        /// faithful (section 6.2). Writing matches exactly - <c>navigator.clipboard.writeText</c> returns a Promise
        /// the host does not await, and <c>SetText</c> returns void. Reading is served from a cache the page fills
        /// from the DOM <c>paste</c> event, which fires inside the browser's own Ctrl+V handling with the text
        /// already available, before the next animation frame. What that cannot cover is a paste driven from a
        /// NowUI context-menu item on a page the user has never pasted into.
        /// </remarks>
        private sealed class WebClipboard : INowClipboard
        {
            /// <inheritdoc />
            public string GetText()
            {
                return Interop.ClipboardRead() ?? string.Empty;
            }

            /// <inheritdoc />
            public void SetText(string text)
            {
                Interop.ClipboardWrite(text ?? string.Empty);
            }
        }

        /// <summary>The JavaScript side of the bridge, in one place so the boundary crossings can be counted.</summary>
        /// <remarks>Two per frame - <c>drain</c> and <c>drainCharacters</c> - plus one per clipboard operation.</remarks>
        internal static partial class Interop
        {
            public const string ModuleName = "nowui-input";

            [JSImport("init", ModuleName)]
            public static partial string Init(string canvasSelector, double uiScale);

            [JSImport("setUiScale", ModuleName)]
            public static partial void SetUiScale(double uiScale);

            /// <summary>The packed event batch, or null when the frame buffered nothing.</summary>
            [JSImport("drain", ModuleName)]
            [return: JSMarshalAs<JSType.Array<JSType.Number>>]
            public static partial double[] Drain();

            [JSImport("drainCharacters", ModuleName)]
            public static partial string DrainCharacters();

            [JSImport("discardPendingText", ModuleName)]
            public static partial void DiscardPendingText();

            [JSImport("clipboardRead", ModuleName)]
            public static partial string ClipboardRead();

            [JSImport("clipboardWrite", ModuleName)]
            public static partial void ClipboardWrite(string text);
        }
    }
}
