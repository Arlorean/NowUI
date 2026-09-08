// The scene the browser host draws: a small task-list application built out of NowUI's own controls, plus the
// fixed-rect probes the input contract's test plan drives.
//
// Why an application and not a diagram. Slice 1 proved a NowUI frame can be *rendered* in a browser by drawing the
// README quick-start panel and differencing it against Unity's render of the same snippet (M2-Scouting.md,
// "Comparing at matched density"). A static panel cannot prove the other half. Hover, press, drag, wheel, focus and
// text entry are only observable in a thing that responds, so this file replaces that panel with the smallest scene
// in which every one of those is visible at a glance:
//
//   type a task name   -> text entry, caret, focus, Tab from one field to the next
//   drag Priority      -> pointer capture, and the drag threshold that scales with density
//   press Add          -> hover tint, press tint, and a click that changes the list
//   scroll the list    -> wheel over a viewport with more content than fits
//   tick a row         -> a click inside a clipped, scrolled region, which is the harder hit-test
//   flip Show finished -> a control whose state changes what the rest of the scene draws
//
// Everything here is a real NowUI control (Assets/NowUI/Runtime/Controls/) placed by real NowUI layout: Button,
// TextField, Slider, Switch, Checkbox, ProgressBar, ScrollView and Label, inside NowLayout Row/Column containers with
// ReserveRect where a rect has to be published. Nothing is a hand-drawn rectangle pretending to be a widget: the
// point of the exercise is that the control library works in a browser, and a lookalike would prove the opposite.
//
// Shader budget. WebGL2Backend implements four programs - UI Rectangle, Text Renderer, UI Gradient, UI Ripple - and
// throws by name on any other (WebGL2Backend.DrawMesh). Every control used here draws inside that set. Controls that
// would leave it, and which are therefore NOT in this scene, are listed under "What this scene deliberately omits"
// at the bottom of this file.
//
// Coordinates. NowUI layout is top-left origin, and so is the pointer space the provider expects: the bottom-left
// convention belongs to Unity's raw mouse API and is flipped away inside NowScreenInputProvider, which a browser
// host does not use (M2-InputContract.md, trap 1). One NowUI unit is one CSS pixel at every device pixel ratio,
// because main.js sizes the drawing buffer to css * dpr and Program.cs passes that same dpr to Now.StartUI. So a
// rect below can be handed to a DOM event dispatcher unchanged - which is what DebugRects() exists to do.
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NowUI;
using UnityEngine;

namespace NowUI.Web
{
    /// <summary>The page's scene: a task list, and the input contract's probe rects.</summary>
    internal static class DemoScene
    {
        /// <summary>One row of the list. A record of what the user did, which is the only state an IM UI keeps.</summary>
        private struct Task
        {
            /// <summary>Stable across insertion, so a row's checkbox keeps its control state when the list shifts.</summary>
            public int id;
            public string name;
            public int priority;
            public bool done;
        }

        // ------------------------------------------------------------------------------------- application state
        //
        // An immediate-mode UI owns no widgets, so this is the whole application: what the user typed, dragged and
        // pressed. It is also the input tests' oracle - "the click counter went from 3 to 4" is a fact, where "these
        // pixels got lighter" is an inference (DebugState below).

        private static readonly List<Task> s_Tasks = new List<Task>
        {
            new Task { id = 1, name = "Port the rectangle shader", priority = 90, done = true },
            new Task { id = 2, name = "Port the text shader", priority = 85, done = true },
            new Task { id = 3, name = "Fetch fixtures over HTTP", priority = 80, done = true },
            new Task { id = 4, name = "Match Unity at 2x", priority = 75, done = true },
            new Task { id = 5, name = "Feed pointer events", priority = 70 },
            new Task { id = 6, name = "Feed the wheel", priority = 60 },
            new Task { id = 7, name = "Feed the keyboard", priority = 55 },
            new Task { id = 8, name = "Clipboard round trip", priority = 40 },
            new Task { id = 9, name = "Port the glass blur", priority = 30 },
            new Task { id = 10, name = "Port the SDF programs", priority = 25 },
            new Task { id = 11, name = "Render to texture", priority = 20 },
            new Task { id = 12, name = "Shrink the payload", priority = 10 },
        };

        private static int s_NextTaskId = 13;

        private static string s_TaskName = "";
        private static string s_Notes = "";
        private static float s_Priority = 50f;
        private static bool s_ShowDone = true;
        private static int s_ClickCount;
        private static int s_AddCount;

        // Probe state. These belong to the input contract's test plan rather than to the application; see the probes
        // section of DrawScene for what each one is for.
        private static int s_SecondaryCount;
        private static int s_MiddleCount;
        private static bool s_Dragging;
        private static int s_DragStartCount;
        private static int s_DragEndCount;
        private static int s_DragCancelCount;
        private static Vector2 s_WheelAccum;

        // ------------------------------------------------------------------------------------------ known rects
        //
        // Recorded as each control is laid out, and published by DebugRects(). A driver that needs to dispatch a
        // pointerdown "on the Add button" reads the rect from the page instead of hard-coding a number that this
        // file is free to change - which is the difference between a test that survives a layout edit and one that
        // silently starts clicking the background.

        private static NowRect s_RectAddButton;
        private static NowRect s_RectPrioritySlider;
        private static NowRect s_RectTaskField;
        private static NowRect s_RectNotesField;
        private static NowRect s_RectScrollView;
        private static NowRect s_RectButtonProbe;
        private static NowRect s_RectDragProbe;
        private static NowRect s_RectWheelProbe;

        private static bool s_ThemeSelected;

        /// <summary>Draws one frame of the scene into <paramref name="view"/>, the whole UI surface in UI units.</summary>
        internal static void Draw(NowRect view)
        {
            SelectTheme();

            NowThemeAsset theme = NowTheme.themeAsset;

            // The scene owns its own ground. Program.cs still clears to the flat colour slice 1 chose to match the
            // Unity reference render's backdrop; this covers it, so the card, the muted captions and the theme's own
            // background are one consistent surface rather than two competing ones.
            Now.Rectangle(view)
                .SetColor(theme.GetColor(NowColorToken.Background))
                .Draw();

            NowRect card = new NowRect(view.x + 24, view.y + 24, 660, 468);
            NowRect probes = new NowRect(view.x + 708, view.y + 24, 400, 224);

            DrawCard(card, theme);
            DrawCard(probes, theme);

            // Two passes per card, not one.
            //
            // NowLayout is a one-pass layout by default: a main-axis stretch (a text field that should take the
            // width the label and the button beside it leave over) is resolved from the PREVIOUS frame's
            // measurement of that group, cached by group id. Measured here rather than assumed: with the plain
            // NowLayout.Column(card) form, every stretched rect in this scene reported width 0 on frame one and
            // stayed 0 - the fields and the slider were invisible, and DebugRects showed the Add button sitting
            // at the field's x. RunMeasured is the public exact form: a measure pass with drawing suppressed and
            // input passive, then the real pass using this frame's own numbers, so stretch is right on the first
            // frame and while anything animates.
            //
            // It costs a second traversal of the scene per card per frame. That is the correct trade for a host
            // that already redraws continuously (M2-InputContract.md finding 2: on-demand redraw is not
            // expressible from a browser host, because NowFrame's repaint-tracking entry points are internal).
            //
            // Safe with the mutations below - Add appends a task, the wheel probe accumulates - because input is
            // inert during the measure pass, so nothing that reacts to a click or a wheel notch can fire twice.
            NowLayout.RunMeasured(card, s_DrawApplication, spacing: 10f, padding: 18f);
            NowLayout.RunMeasured(probes, s_DrawProbes, spacing: 8f, padding: 16f);
        }

        // Cached so the two RunMeasured calls above do not allocate a delegate every frame.
        private static readonly System.Action s_DrawApplication = DrawApplication;
        private static readonly System.Action s_DrawProbes = DrawProbes;

        /// <summary>The panel a card's controls sit on, drawn before its layout so the controls land over it.</summary>
        private static void DrawCard(NowRect rect, NowThemeAsset theme)
        {
            Now.Rectangle(rect)
                .SetColor(theme.GetColor(NowColorToken.Surface))
                .SetRadius(12)
                .SetOutline(1, theme.GetColor(NowColorToken.Border))
                .Draw();
        }

        /// <summary>
        /// Chooses the dark built-in theme, once.
        /// </summary>
        /// <remarks>
        /// A preference, not a workaround: the default theme is light, the page's ground is dark, and a light
        /// theme's muted body text over a dark ground is the unreadable grey the first slice-2 screenshot showed.
        /// Setting it here rather than in Program.cs keeps the scene's appearance inside the scene. It also
        /// exercises NowTheme.preferDark itself, which resolves through the same ScriptableObject.CreateInstance
        /// path the shim reimplements, so a failure would be a shim failure and worth knowing about.
        /// </remarks>
        private static void SelectTheme()
        {
            if (s_ThemeSelected)
                return;

            s_ThemeSelected = true;
            NowTheme.preferDark = true;
        }

        /// <summary>The task list: a text field, a slider, a button, a switch and a scrolling list of rows.</summary>
        private static void DrawApplication()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color text = theme.GetColor(NowColorToken.Text);
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(8).Begin())
            {
                NowLayout.Label("Task list", 20, text).SetBold().Draw();
                NowLayout.Spacer();
                NowLayout.Label("NowUI / WebAssembly / WebGL2", 12, muted).Draw();
            }

            NowLayout.Label(
                    "Every control below is a NowUI control, placed by NowLayout. Type a name, set a priority, press Add.",
                    12,
                    muted)
                .Draw();

            NowLayout.Space(4);

            // Text entry. Two fields rather than one, because Tab needs somewhere to go: focus navigation is
            // only observable when there is a second focusable control to receive it (test plan 7.5).
            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10).Begin())
            {
                NowLayout.Label("New task", 13, muted).SetWidth(66).Draw();

                s_RectTaskField = NowLayout.ReserveRect(height: 30, stretchWidth: true);

                Now.TextField(s_RectTaskField)
                    .SetPlaceholder("what needs doing?")
                    .Draw(ref s_TaskName);

                s_RectAddButton = NowLayout.ReserveRect(width: 92, height: 30);

                if (Now.Button(s_RectAddButton, "Add").Draw())
                {
                    s_ClickCount++;
                    AddTask();
                }
            }

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10).Begin())
            {
                NowLayout.Label("Notes", 13, muted).SetWidth(66).Draw();

                s_RectNotesField = NowLayout.ReserveRect(height: 30, stretchWidth: true);

                Now.TextField(s_RectNotesField)
                    .SetPlaceholder("Tab moves focus here")
                    .Draw(ref s_Notes);
            }

            // The value control. The slider tracks the pointer from the press, and the progress bar underneath
            // is the same number drawn a second way, so a drag is visible even when the handle is under the
            // cursor.
            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10).Begin())
            {
                NowLayout.Label("Priority", 13, muted).SetWidth(66).Draw();

                s_RectPrioritySlider = NowLayout.ReserveRect(height: 24, stretchWidth: true);

                Now.Slider(s_RectPrioritySlider, 0f, 100f).Draw(ref s_Priority);

                NowLayout.Label(
                        Mathf.RoundToInt(s_Priority).ToString(CultureInfo.InvariantCulture),
                        13,
                        text)
                    .SetWidth(28)
                    .Draw();
            }

            // A read-only control, bound to the list rather than to the slider. Bound to the slider it would be
            // a second, thinner copy of the track directly under the real one, which reads as a broken slider;
            // bound to the list it is a number the application actually has and the slider does not.
            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(10).Begin())
            {
                int done = CountDone();

                NowLayout.Label("Done", 13, muted).SetWidth(66).Draw();
                NowLayout.ProgressBar(s_Tasks.Count == 0 ? 0f : done / (float)s_Tasks.Count)
                    .SetStretchWidth()
                    .SetHeight(8)
                    .Draw();

                NowLayout.Label(
                        done.ToString(CultureInfo.InvariantCulture) + "/" +
                        s_Tasks.Count.ToString(CultureInfo.InvariantCulture),
                        12,
                        muted)
                    .SetWidth(40)
                    .Draw();
            }

            NowLayout.Space(2);

            using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(14).Begin())
            {
                NowLayout.Switch("Show finished").SetWidth(150).Draw(ref s_ShowDone);
                NowLayout.Spacer();
                NowLayout.Label("added " + s_AddCount + " / clicks " + s_ClickCount, 12, muted).Draw();
            }

            NowLayout.Label("Tasks - the list is taller than its box, so scroll it", 12, muted).Draw();

            // The scroll region. Reserved as a rect rather than stretched, so the viewport a wheel test targets
            // is a number this file can publish (DebugRects) rather than a consequence of whatever space the
            // rows above happened to leave.
            s_RectScrollView = NowLayout.ReserveRect(height: 176, stretchWidth: true);

            Now.Rectangle(s_RectScrollView)
                .SetColor(theme.GetColor(NowColorToken.SurfaceMuted))
                .SetRadius(8)
                .Draw();

            using (Now.ScrollView(s_RectScrollView.Inset(6)).Begin())
            {
                int shown = 0;

                for (int i = 0; i < s_Tasks.Count; ++i)
                {
                    Task task = s_Tasks[i];

                    if (task.done && !s_ShowDone)
                        continue;

                    shown++;

                    // Keyed by the row's identity rather than by its position in the loop, so a checkbox keeps
                    // its control state when Add inserts a task above it and every row shifts down by one.
                    using (NowControls.KeyedItem(task.id))
                    using (NowLayout.Row().FillWidth().AlignChildren(NowLayoutAlign.Center).Gap(8).Begin())
                    {
                        // A second clickable control, inside a clipped and scrollable region: hit-testing here
                        // has to survive the scroll offset and the viewport mask, which the button above does
                        // not exercise.
                        bool done = task.done;

                        if (NowLayout.Checkbox().SetWidth(20).Draw(ref done))
                        {
                            task.done = done;
                            s_Tasks[i] = task;
                        }

                        NowLayout.Label(task.name, 13, task.done ? muted : text).Draw();
                        NowLayout.Spacer();
                        NowLayout.Label(task.priority.ToString(CultureInfo.InvariantCulture), 12, muted).Draw();
                    }
                }

                if (shown == 0)
                    NowLayout.Label("nothing to show", 13, muted).Draw();
            }
        }

        /// <summary>
        /// The three fixed-rect probes the input contract's test plan drives, in their own card so they read as
        /// instrumentation rather than as part of the application.
        /// </summary>
        /// <remarks>
        /// Each one exists because the corresponding assertion cannot be made against a normal control:
        /// the application's controls all respond to the primary button, track the pointer with no threshold, and
        /// convert wheel notches into their own units before anything is observable.
        /// </remarks>
        private static void DrawProbes()
        {
            NowThemeAsset theme = NowTheme.themeAsset;
            Color text = theme.GetColor(NowColorToken.Text);
            Color muted = theme.GetColor(NowColorToken.TextMuted);

            NowLayout.Label("Input probes", 14, text).SetBold().Draw();
            NowLayout.Label("Instrumentation for M2-InputContract.md section 7.", 11, muted).Draw();

            // The button-index probe. DOM PointerEvent.button is 0 left, 1 middle, 2 right; NowUI's index is
            // 0 Primary, 1 Secondary, 2 Middle, so 1 and 2 have to be swapped on the way in. Two counters on
            // one rect, one bound to each of the two buttons that move, is the only way to see that swap: with
            // the remap missing, a DOM right-press increments middle and a DOM middle-press increments
            // secondary - the counters cross over rather than going quiet, which is a failure that cannot be
            // mistaken for anything else.
            s_RectButtonProbe = NowLayout.ReserveRect(height: 34, stretchWidth: true);

            Now.Rectangle(s_RectButtonProbe)
                .SetColor(new Color(0.35f, 0.2f, 0.5f, 1f))
                .SetRadius(6)
                .Draw();

            if (NowInput.Interact(s_RectButtonProbe, NowPointerButton.Secondary).clicked)
                s_SecondaryCount++;

            if (NowInput.Interact(s_RectButtonProbe, NowPointerButton.Middle).clicked)
                s_MiddleCount++;

            Now.Text(s_RectButtonProbe.Inset(8))
                .SetFontSize(12)
                .SetColor(Color.white)
                .Draw("right " + s_SecondaryCount + "   middle " + s_MiddleCount);

            // The drag probe. NowSlider tracks the pointer from the press with no threshold, so it cannot show
            // NowInput.dragThreshold at work; a bare Interact can. The threshold is 4 * max(1, Screen.dpi / 160)
            // in UI units and WebHostServices reports dpi = 96 * devicePixelRatio, so it is 4.0 at dpr 1 and 4.8
            // at dpr 2 - the one observable in the whole test plan that is SUPPOSED to differ between the two
            // density runs, and therefore the one that proves density is threaded through rather than merely
            // cancelling out.
            s_RectDragProbe = NowLayout.ReserveRect(height: 34, stretchWidth: true);

            Now.Rectangle(s_RectDragProbe)
                .SetColor(new Color(0.2f, 0.4f, 0.35f, 1f))
                .SetRadius(6)
                .Draw();

            var drag = NowInput.Interact(s_RectDragProbe);
            s_Dragging = drag.dragging;

            if (drag.dragStarted)
                s_DragStartCount++;

            // Recorded rather than asserted. NowUIToolkitInputProvider.CancelPointer moves the held buttons into
            // the released mask and cannot set NowInputSnapshot.pointerCaptureCancelled, so a DOM pointercancel
            // mid-drag is expected to arrive as dragEnded - a commit - rather than dragCancelled. These two
            // counters make that visible instead of leaving it as a claim about code nobody ran.
            if (drag.dragEnded)
                s_DragEndCount++;

            if (drag.dragCancelled)
                s_DragCancelCount++;

            Now.Text(s_RectDragProbe.Inset(8))
                .SetFontSize(12)
                .SetColor(Color.white)
                .Draw("drag " + (s_Dragging ? "1" : "0") + "   started " + s_DragStartCount +
                    "   ended " + s_DragEndCount + "   cancelled " + s_DragCancelCount);

            // The wheel probe. The task list is the realistic consumer, but a scroll view converts notches into
            // pixels through NowTheme.controlStyles.scrollWheelStep and clamps to its own content, so what it
            // shows is its scrolling rather than the delta that reached it. ConsumeScrollDelta returns the
            // snapshot value itself, in the canonical unit - notches, +y up - which is exactly the quantity this
            // bridge is responsible for. One notch of wheel-down must read -1 here, and the two deltaModes must
            // agree.
            s_RectWheelProbe = NowLayout.ReserveRect(height: 34, stretchWidth: true);

            Now.Rectangle(s_RectWheelProbe)
                .SetColor(new Color(0.18f, 0.22f, 0.3f, 1f))
                .SetRadius(6)
                .Draw();

            s_WheelAccum += NowInput.ConsumeScrollDelta(s_RectWheelProbe);

            Now.Text(s_RectWheelProbe.Inset(8))
                .SetFontSize(12)
                .SetColor(Color.white)
                .Draw("wheel " + Format(s_WheelAccum.x) + ", " + Format(s_WheelAccum.y));
        }

        /// <summary>How many rows are finished, for the progress bar.</summary>
        private static int CountDone()
        {
            int done = 0;

            for (int i = 0; i < s_Tasks.Count; ++i)
            {
                if (s_Tasks[i].done)
                    done++;
            }

            return done;
        }

        /// <summary>Appends the typed name at the current priority and clears the field, the way a form does.</summary>
        private static void AddTask()
        {
            string name = string.IsNullOrWhiteSpace(s_TaskName) ? "Untitled task" : s_TaskName.Trim();

            s_Tasks.Insert(0, new Task { id = s_NextTaskId++, name = name, priority = Mathf.RoundToInt(s_Priority) });
            s_TaskName = "";
            s_AddCount++;
        }

        /// <summary>
        /// The scene's state as one line, for driving the input tests from the page.
        /// </summary>
        /// <remarks>
        /// An oracle, not a feature. M2-Scouting.md establishes that headless screenshot capture here is not yet
        /// trustworthy and that <c>gl.readPixels</c> on a live page is; this is the third option, and for the things
        /// input changes it is the exact one - "the click counter went from 3 to 4" is a fact, where "these pixels
        /// got lighter" is an inference. Pixels remain the right oracle for hover and focus rings, which have no
        /// managed state to report.
        /// <para>The key set is append-only: a driver that greps for <c>clicks=</c> keeps working when a later slice
        /// adds a field.</para>
        /// </remarks>
        internal static string DebugState()
        {
            return "clicks=" + s_ClickCount +
                ";secondary=" + s_SecondaryCount +
                ";middle=" + s_MiddleCount +
                ";dragging=" + (s_Dragging ? 1 : 0) +
                ";dragstarts=" + s_DragStartCount +
                ";dragends=" + s_DragEndCount +
                ";dragcancels=" + s_DragCancelCount +
                ";wheelx=" + Format(s_WheelAccum.x) +
                ";wheely=" + Format(s_WheelAccum.y) +
                ";slider=" + Format(s_Priority) +
                ";a=" + s_TaskName +
                ";b=" + s_Notes +
                ";adds=" + s_AddCount +
                ";tasks=" + s_Tasks.Count +
                ";showdone=" + (s_ShowDone ? 1 : 0);
        }

        /// <summary>
        /// Where each driveable control was laid out this frame, as
        /// <c>name=x,y,width,height</c> pairs separated by <c>;</c>.
        /// </summary>
        /// <remarks>
        /// In CSS pixels, directly usable as DOM client coordinates relative to the canvas, at any device pixel
        /// ratio: main.js sizes the drawing buffer to <c>css * dpr</c> and Program.cs passes that same ratio to
        /// <c>Now.StartUI</c>, so one NowUI unit is one CSS pixel by construction. Top-left origin, which is both
        /// NowUI's layout space and the pointer space the provider expects - a browser host flips nothing
        /// (M2-InputContract.md, trap 1).
        /// <para>Published so an input test can ask the page where a control is instead of hard-coding a rect this
        /// file is free to move.</para>
        /// </remarks>
        internal static string DebugRects()
        {
            StringBuilder builder = new StringBuilder(320);

            Append(builder, "addButton", s_RectAddButton);
            Append(builder, "prioritySlider", s_RectPrioritySlider);
            Append(builder, "taskField", s_RectTaskField);
            Append(builder, "notesField", s_RectNotesField);
            Append(builder, "scrollView", s_RectScrollView);
            Append(builder, "buttonProbe", s_RectButtonProbe);
            Append(builder, "dragProbe", s_RectDragProbe);
            Append(builder, "wheelProbe", s_RectWheelProbe);

            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string name, NowRect rect)
        {
            if (builder.Length > 0)
                builder.Append(';');

            builder.Append(name).Append('=')
                .Append(Format(rect.x)).Append(',')
                .Append(Format(rect.y)).Append(',')
                .Append(Format(rect.width)).Append(',')
                .Append(Format(rect.height));
        }

        /// <summary>Invariant formatting, because <c>InvariantGlobalization</c> is on and a comma decimal separator would corrupt the pairs above.</summary>
        private static string Format(float value)
        {
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}

// What this scene deliberately omits, and why
// -------------------------------------------
// Every control here draws through one of the four ported programs. These are the ones that would not, listed so
// the omission reads as a coordination note to the shader unit rather than as an oversight:
//
//   NowUI/UI Glass, NowUI/UI Glass Blur   - any control given a glass surface, and the blur behind a modal scrim.
//                                           Four passes for the blur; WebGL2Backend.DrawMesh rejects pass > 0.
//   NowUI/UI Bezier                       - Now.Bezier, and through it the animation-curve field
//                                           (NowValueControls.cs:3630). Nothing else in the control set uses it.
//   NowUI/UI Color Picker                 - the colour field's saturation/value square.
//   the SDF programs                      - Now.Shape and the shape-algebra drawing paths.
//
// Nothing in the list above was avoided because it "might" need a program: the backend throws by name on an
// unported shader, so the cost of being wrong is a loud start-of-frame exception, not a silent blank. The list is
// what a scene twice this size would want next.
