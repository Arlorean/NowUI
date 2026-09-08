// W6 - the tier-1 surface, managed half. Docs/Standalone/M3-Spec.md sections 2 and 5.3.
//
// Replay.cs holds W2-W5's six ops and the frame. This file holds everything section 2 adds on top of them: the
// options object of section 2.7, the scopes of section 2.2 that W3 did not need, the drawings of section 2.3, the
// two remaining actions of section 2.4 and the fourteen values of section 2.5.
//
// It is one `partial` half of BridgeReplay rather than a second class, for two reasons that are not stylistic:
// InvariantI1.targets scans `Replay*.cs` and requires every NowUI container and control chain in it to carry
// exactly one SetId (gate G10), and the scope stack, the duplicate backstop and the result table are all instance
// state the new ops need. A second class would have to be handed all three and would fall outside the gate.
//
// THE PENDING-OPTIONS FIELD, and why it does not break section 5.6.
//
// Section 2.7's options object arrives as its own op (OPTS) immediately before the op it modifies, and is held in
// one field until that op consumes it. That is a cursor, and section 5.6 allows exactly one kind: "advances no
// cursor that is not a local". This one is not a local, so it earns its place by satisfying the property the rule
// exists to protect rather than the letter of it:
//
//   * it is reset at the top of every decode pass, beside m_Depth, so the second pass under exactLayout starts
//     from the same state as the first;
//   * it is cleared by the op that reads it, so a pass leaves it empty;
//   * nothing outside one op-pair ever reads it.
//
// Both passes therefore decode identically, which is the property section 5.6 is actually asking for. The test in
// Standalone/NowUI.Bridge.Tests asserts it as a comparison of the two passes rather than as a claim here.

using System;
using System.Collections.Generic;
using NowUI;
using UnityEngine;

namespace NowUI.Bridge
{
    /// <summary>
    /// Section 2.7's options object, decoded. A bitmask names which fields the OPTS op carried; the fields follow
    /// it in ascending bit order, which is the only ordering rule the two halves share (see abi.js's OPT_* block).
    /// </summary>
    /// <remarks>
    /// A struct with a mask rather than a bag of nullables: it is constructed once per control that has options
    /// and destroyed at the end of the same op, and section 5.6 asks the decode to allocate nothing that outlives
    /// the call.
    /// </remarks>
    public struct BridgeOptions
    {
        public int mask;

        public float width, height, minWidth, maxWidth, minHeight, maxHeight, grow, gap, step;
        public Vector4 padding;
        public int align, justify, style, textStyle;

        public bool Has(int bit) => (mask & bit) != 0;

        /// <summary>Section 2.7's <c>disabled</c> composite. NowUI has no disabled concept; see <see cref="Disable"/>.</summary>
        public bool disabled => Has(Abi.OptDisabled);

        /// <summary>Section 6.1's <c>rect</c> opt-in.</summary>
        public bool wantsRect => Has(Abi.OptRect);

        /// <summary>
        /// Reads one OPTS op's arguments. <paramref name="args"/> is the slot the bitmask sits at.
        /// </summary>
        public static BridgeOptions Decode(BridgeRecorder recorder, int args)
        {
            var o = default(BridgeOptions);
            o.mask = recorder.Slot(args);

            int slot = args + 1;

            // Ascending bit order, and the payload width per bit comes from Abi.OptSlots so the walk cannot
            // disagree with the recorder about where the next field starts. An unknown high bit (a nowui.js
            // newer than this wasm build, which the surface hash should already have refused) is skipped by the
            // op header's argSlots rather than mis-read here.
            if (o.Has(Abi.OptWidth)) o.width = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptHeight)) o.height = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptMinWidth)) o.minWidth = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptMaxWidth)) o.maxWidth = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptMinHeight)) o.minHeight = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptMaxHeight)) o.maxHeight = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptGrow)) o.grow = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptGap)) o.gap = recorder.SlotF32(slot++);

            if (o.Has(Abi.OptPadding))
            {
                o.padding = new Vector4(
                    recorder.SlotF32(slot), recorder.SlotF32(slot + 1),
                    recorder.SlotF32(slot + 2), recorder.SlotF32(slot + 3));
                slot += 4;
            }

            if (o.Has(Abi.OptAlign)) o.align = recorder.Slot(slot++);
            if (o.Has(Abi.OptJustify)) o.justify = recorder.Slot(slot++);
            if (o.Has(Abi.OptStyle)) o.style = recorder.Slot(slot++);
            if (o.Has(Abi.OptTextStyle)) o.textStyle = recorder.Slot(slot++);
            if (o.Has(Abi.OptStep)) o.step = recorder.SlotF32(slot++);

            return o;
        }

        /// <summary>
        /// The sizing half, as one <see cref="NowLayoutOptions"/>. Every tier-1 builder takes one through
        /// <c>SetOptions</c> (a container through <c>Options</c>), so the whole of section 2.7's sizing table is
        /// one code path rather than a per-control switch.
        /// </summary>
        /// <remarks>
        /// <para><c>grow</c> IS THE ONE OPTION THAT MEANS TWO DIFFERENT CALLS, and section 2.7 says so:
        /// "SetStretchWidth(weight) on controls, FillWidth()/Grow(w) on containers". That reads like an
        /// inconsistency worth removing - <c>SetGrow</c> "follows the parent direction, so the same declaration
        /// works when a container moves between a row and a column" (NowLayout.cs:225-228), and it is available
        /// on every builder through <c>SetOptions</c>, so one call ought to serve both.</para>
        /// <para>It does not, and the source is what says so. Every CONTROL reserves its rect through
        /// <c>NowControls.ReserveRect</c>, which fills in a content-derived width when the options carry neither
        /// Width nor StretchWidth (NowControls.cs:1002-1003) - and <c>Grow</c> is neither. The next thing that
        /// runs is <c>NowLayout.Allocate</c>, which throws "A growing element cannot also have a fixed size on
        /// its parent's main axis" (NowLayout.cs:2811-2815) on exactly that pair. So <c>grow</c> on a control is
        /// not a worse spelling of stretch, it is an exception - measured in the browser, on section 1's first
        /// text field, which is `{ placeholder: 'Full name', grow: 1 }`.</para>
        /// <para>A CONTAINER does not go through <c>NowControls.ReserveRect</c>, so nothing fills in a width and
        /// <c>SetGrow</c> works there. Section 2.7's split mapping is therefore correct as written, and this is
        /// the parameter that carries it.</para>
        /// </remarks>
        /// <param name="container">
        /// True for <c>ui.column</c>, <c>ui.row</c>, <c>ui.card</c> and <c>ui.rule</c>'s inner row; false for
        /// every control, <c>ui.scroll</c> included.
        /// </param>
        public NowLayoutOptions ToLayout(bool container = false)
        {
            var o = default(NowLayoutOptions);
            if (mask == 0) return o;

            if (Has(Abi.OptWidth)) o = o.SetWidth(width);
            if (Has(Abi.OptHeight)) o = o.SetHeight(height);
            if (Has(Abi.OptMinWidth)) o = o.SetMinWidth(minWidth);
            if (Has(Abi.OptMaxWidth)) o = o.SetMaxWidth(maxWidth);
            if (Has(Abi.OptMinHeight)) o = o.SetMinHeight(minHeight);
            if (Has(Abi.OptMaxHeight)) o = o.SetMaxHeight(maxHeight);
            if (Has(Abi.OptGrow)) o = container ? o.SetGrow(grow) : o.SetStretchWidth(grow);
            if (Has(Abi.OptGap)) o = o.SetSpacing(gap);
            if (Has(Abi.OptPadding)) o = o.SetPadding(padding);
            if (Has(Abi.OptAlign)) o = o.SetAlignItems((NowLayoutAlign)align);
            if (Has(Abi.OptJustify)) o = o.SetJustify((NowLayoutJustify)justify);

            return o;
        }

        /// <summary>The rectangle style, or <paramref name="fallback"/> when the author named none.</summary>
        public NowRectangleStyle RectStyle(NowRectangleStyle fallback)
        {
            if (disabled) return NowRectangleStyle.Ghost;
            return Has(Abi.OptStyle) ? (NowRectangleStyle)style : fallback;
        }

        /// <summary>The text style, or <paramref name="fallback"/> when the author named none.</summary>
        public NowTextStyle TextStyle(NowTextStyle fallback)
        {
            if (disabled) return NowTextStyle.Muted;
            return Has(Abi.OptTextStyle) ? (NowTextStyle)textStyle : fallback;
        }

        /// <summary>
        /// Section 2.7's <c>disabled</c>, in the one place it can be honoured: the caller discards the
        /// interaction result. NowUI has no disabled concept - a disabled control is still hoverable, still
        /// focusable, still in the keyboard order and still shows a focus ring - and the option says so.
        /// </summary>
        public bool Disable(bool interaction) => disabled ? false : interaction;
    }

    public sealed partial class BridgeReplay
    {
        /// <summary>Section 2.7's options object, waiting for the op it modifies. See the file header.</summary>
        private BridgeOptions m_Pending;

        /// <summary>
        /// The option lists of <c>ui.dropdown</c>, <c>ui.combo</c> and <c>ui.tabs</c>, decoded. One reusable list
        /// rather than an allocation per control per pass: the consumer reads it inside <c>Draw</c> and keeps no
        /// reference, and section 5.6 asks the decode to allocate nothing that outlives the call.
        /// </summary>
        private readonly List<string> m_Strings = new List<string>(16);

        /// <summary>Reset at the top of every decode pass, beside the scope depth. See the file header.</summary>
        private void BeginControlsPass()
        {
            m_Pending = default;
            m_RuleOrdinal = 0;
        }

        /// <summary>Consumes the pending options, leaving the field empty for the next op.</summary>
        private BridgeOptions TakeOptions()
        {
            BridgeOptions o = m_Pending;
            m_Pending = default;
            return o;
        }

        /// <summary>
        /// W6's half of the decode switch. Returns false for an op this file does not own, which is how
        /// <see cref="Dispatch"/> tells "no case" from "handled".
        /// </summary>
        private bool DispatchControl(string name, int args)
        {
            switch (name)
            {
                case "OPTS":
                    m_Pending = BridgeOptions.Decode(m_Recorder, args);
                    return true;

                // ---- scopes (section 2.2) ------------------------------------------------------------------
                case "CARD":
                    OpenCard(m_Recorder.Slot(args), m_Recorder.Slot(args + 1));
                    return true;

                case "IDSCOPE":
                    OpenIdScope(m_Recorder.Slot(args), m_Recorder.Slot(args + 1));
                    return true;

                case "SCROLL":
                    OpenScroll(m_Recorder.Slot(args), m_Recorder.Slot(args + 1));
                    return true;

                case "FOLDOUT":
                    OpenFoldout(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2) != 0, m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                // ---- drawings (section 2.3) ----------------------------------------------------------------
                case "SPACE":
                    NowLayout.Space(m_Recorder.SlotF32(args));
                    TakeOptions();
                    return true;

                case "FLEX_SPACE":
                    NowLayout.FlexibleSpace(m_Recorder.SlotF32(args));
                    TakeOptions();
                    return true;

                case "RULE":
                    DrawRule();
                    return true;

                case "BADGE":
                    DrawBadge(m_Recorder.Text(m_Recorder.Slot(args)));
                    return true;

                // ---- actions (section 2.4) -----------------------------------------------------------------
                case "SELECTABLE":
                    DrawSelectable(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2) != 0, m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "CHIP":
                    DrawChip(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Text(m_Recorder.Slot(args + 2)),
                        m_Recorder.Slot(args + 3) != 0, m_Recorder.Slot(args + 4) != 0);
                    return true;

                // ---- values (section 2.5) ------------------------------------------------------------------
                case "TEXT_AREA":
                    DrawTextArea(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Text(m_Recorder.Slot(args + 2)), m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "NUMBER_FIELD":
                    DrawNumberField(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.SlotF32(args + 2), m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "CHECKBOX":
                    DrawCheckbox(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2) != 0, m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "SWITCH":
                    DrawSwitch(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2) != 0, m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "RADIO_ITEM":
                    DrawRadio(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2) != 0, m_Recorder.Text(m_Recorder.Slot(args + 3)));
                    return true;

                case "SLIDER":
                    DrawSlider(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.SlotF32(args + 2), m_Recorder.SlotF32(args + 3), m_Recorder.SlotF32(args + 4));
                    return true;

                case "INT_SLIDER":
                    DrawIntSlider(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.Slot(args + 2), m_Recorder.SlotF32(args + 3), m_Recorder.SlotF32(args + 4));
                    return true;

                case "DROPDOWN":
                    DrawDropdown(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2),
                        ReadStrings(args + 3));
                    return true;

                case "COMBO":
                    DrawCombo(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2),
                        ReadStrings(args + 3));
                    return true;

                case "COLOR_FIELD":
                    DrawColorField(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2));
                    return true;

                case "DATE_PICKER":
                    DrawDatePicker(m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        ReadI64(args + 2));
                    return true;

                case "TIME_PICKER":
                    DrawTimePicker(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2));
                    return true;

                case "TABS":
                    DrawTabs(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2),
                        ReadStrings(args + 3));
                    return true;

                // ---- feedback (section 2.6) ----------------------------------------------------------------
                case "PROGRESS":
                    DrawProgress(m_Recorder.SlotF32(args));
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>Section 5.3's <c>strlist</c>: one count slot, then that many <c>str</c> slots.</summary>
        private List<string> ReadStrings(int slot)
        {
            int count = m_Recorder.Slot(slot);
            m_Strings.Clear();
            for (int i = 0; i < count; ++i) m_Strings.Add(m_Recorder.Text(m_Recorder.Slot(slot + 1 + i)));
            return m_Strings;
        }

        /// <summary>Section 5.3's <c>i64</c>: two slots, low word first.</summary>
        private long ReadI64(int slot)
        {
            uint low = unchecked((uint)m_Recorder.Slot(slot));
            int high = m_Recorder.Slot(slot + 1);
            return unchecked(((long)high << 32) | low);
        }

        // ------------------------------------------------------------------------------------------ scopes

        /// <summary>
        /// Section 2.2's <c>ui.card</c> composite: a column, then a themed rectangle over the group's reserved
        /// rect, drawn BEFORE the children so the background is behind them.
        /// </summary>
        /// <remarks>
        /// <c>NowLayoutScope</c> exposes the group's rect at <c>Begin()</c> (NowLayout.cs:382), which is what
        /// makes the composite expressible at all - a card is one call in the surface and two in NowUI, and the
        /// second one needs the first one's geometry.
        /// </remarks>
        private void OpenCard(int rid, int segment)
        {
            BridgeOptions o = TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);

            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            // The rid rather than the segment, for the reason OpenContainer records at length: a nested container
            // derives its group id under the enclosing LAYOUT group, and the rid is the one value that is 1:1
            // with a path.
            NowLayoutScope scope = NowLayout.Column().SetId(new NowId(rid)).Options(o.ToLayout(container: true)).Begin();

            Now.Rectangle(scope.rect)
                .SetStyle(NowTheme.themeAsset, o.RectStyle(NowRectangleStyle.Elevated))
                .Draw();

            frame.layout = scope;
            frame.kinds |= BridgeScopeFrame.HasLayout;
        }

        /// <summary>
        /// Section 2.2's <c>ui.when</c>: an identity scope and no box, so the body's children flow in the parent.
        /// The recorder emits it on every frame whether or not the condition held, which is what keeps the
        /// anonymous ordinals of every following sibling stable (section 3.5).
        /// </summary>
        private void OpenIdScope(int rid, int segment)
        {
            TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);
            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;
        }

        private void OpenScroll(int rid, int segment)
        {
            BridgeOptions o = TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);

            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            // A scroll view keeps STATE - the offset - so its identity is the thing this whole design is about.
            // The rid is used here for the same reason a container uses it: it is 1:1 with the canonical path,
            // and it is the value check 1 proves unique within a frame.
            frame.scroll = NowLayout.ScrollView().SetId(new NowId(rid)).SetOptions(o.ToLayout()).Begin();
            frame.kinds |= BridgeScopeFrame.HasScroll;
        }

        /// <summary>
        /// Section 2.2's <c>ui.foldout</c>. It is a control AND a scope: the header draws and returns the open
        /// state, and the body ops that follow are inside the scope either way - the recorder emits them only
        /// when it is open, so a closed foldout costs one op.
        /// </summary>
        private void OpenFoldout(int rid, int segment, bool open, string label)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool expanded = open;
            bool clicked = NowLayout.Foldout(label)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref expanded);

            m_Results.WriteBool(rid, clicked ? BridgeFlags.Changed : BridgeFlags.None, expanded);

            ref BridgeScopeFrame frame = ref Push(rid);
            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;
        }

        // ---------------------------------------------------------------------------------------- drawings

        /// <summary>
        /// Section 2.3's <c>ui.text</c>, and with it <c>ui.heading</c>, <c>ui.subheading</c> and
        /// <c>ui.caption</c> - the three aliases, which are this op with <c>textStyle</c> pre-set (section 2.3).
        /// </summary>
        /// <remarks>
        /// The two branches are not cosmetic. With no <c>textStyle</c> the label takes the theme's own Text
        /// colour rather than <c>NowLayout.labelStyle</c>'s default, which W2 established the hard way: the first
        /// browser capture of this op put dark text on the host's dark ground, correct and unreadable. With a
        /// <c>textStyle</c> the theme's resolved preset carries its own colour, and overriding it would make
        /// <c>ui.caption</c> and <c>ui.text</c> the same colour - which is most of what a caption is.
        /// </remarks>
        private void DrawLabel(string content)
        {
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            if (o.Has(Abi.OptTextStyle) || o.disabled)
            {
                NowLayout.Label(theme.ResolveText(o.TextStyle(NowTextStyle.Body)), content)
                    .SetOptions(o.ToLayout())
                    .Draw();
                return;
            }

            NowLayout.Label(content)
                .SetOptions(o.ToLayout())
                .SetColor(theme.GetColor(NowColorToken.Text))
                .Draw();
        }

        /// <summary>
        /// Section 2.3's <c>ui.rule</c> composite: a one-pixel row filled with the theme's Outline style.
        /// </summary>
        private void DrawRule()
        {
            BridgeOptions o = TakeOptions();
            NowLayoutOptions layout = o.ToLayout(container: true);

            if (!o.Has(Abi.OptHeight)) layout = layout.SetHeight(1f);
            if (!o.Has(Abi.OptWidth)) layout = layout.SetStretchWidth();

            // A rule is a drawing and holds no state, so the container it opens takes the enclosing rid - the
            // scope frame it would otherwise need does not exist. It is opened and closed in one statement pair
            // so nothing can leave it open.
            using (NowLayoutScope scope = NowLayout.Row().SetId(new NowId(RuleId())).Options(layout).Begin())
            {
                Now.Rectangle(scope.rect)
                    .SetStyle(NowTheme.themeAsset, o.RectStyle(NowRectangleStyle.Outline))
                    .Draw();
            }
        }

        /// <summary>
        /// A rule has no key and therefore no rid of its own. It gets a per-frame counter salted into a value no
        /// recorder rid can take, so two rules under one layout parent cannot derive one group id.
        /// </summary>
        private int RuleId()
        {
            return int.MinValue + 1 + (m_RuleOrdinal++ & 0xFFFF);
        }

        private int m_RuleOrdinal;

        private void DrawBadge(string label)
        {
            BridgeOptions o = TakeOptions();

            NowLayout.Badge(label)
                .SetOptions(o.ToLayout())
                .SetStyle(o.RectStyle(NowRectangleStyle.Muted))
                .SetTextStyle(o.TextStyle(NowTextStyle.Caption))
                .Draw();
        }

        // ----------------------------------------------------------------------------------------- actions

        private void DrawSelectable(int rid, int segment, bool selected, string label)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool clicked = NowLayout.SelectableRow(label)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetSelected(selected)
                .SetTextStyle(o.TextStyle(NowTextStyle.Body))
                .Draw();

            m_Results.WriteEvent(rid, o.Disable(clicked) ? BridgeFlags.Clicked : BridgeFlags.None);
        }

        private void DrawChip(int rid, int segment, string label, bool selected, bool removable)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool removed;
            bool clicked = NowLayout.Chip(label)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetSelected(selected)
                .SetRemovable(removable)
                .SetTextStyle(o.TextStyle(NowTextStyle.Body))
                .Draw(out removed);

            BridgeFlags flags = BridgeFlags.None;
            if (o.Disable(clicked)) flags |= BridgeFlags.Clicked;

            // `removed` reaches the author through opts.onRemove, never as a returned object (section 2.4), so it
            // rides as its own one-shot flag rather than as a second return value.
            if (o.Disable(removed)) flags |= BridgeFlags.Cancelled;

            m_Results.WriteEvent(rid, flags);
        }

        // ------------------------------------------------------------------------------------------ values

        private void DrawTextArea(int rid, int segment, string value, string placeholder)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            string text = value;
            bool changed = NowLayout.TextArea()
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetPlaceholder(placeholder)
                .Draw(ref text);

            m_Results.WriteString(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, text);
        }

        private void DrawNumberField(int rid, int segment, float value, string format)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            float number = value;
            NowTextFieldResult result = format.Length == 0
                ? NowLayout.TextField().SetId(new NowId(segment)).SetOptions(o.ToLayout()).Draw(ref number)
                : NowLayout.TextField().SetId(new NowId(segment)).SetOptions(o.ToLayout()).Draw(ref number, format);

            // Section 2.5's `step` is snapped here rather than in JavaScript, so the number the author is
            // handed is the same one the field is showing. min/max are not applied: they are a range, and
            // NowTextField has no range - clamping a half-typed number as the user types it deletes digits.
            if (o.Has(Abi.OptStep) && o.step > 0f) number = Mathf.Round(number / o.step) * o.step;

            BridgeFlags flags = BridgeFlags.None;
            if (result.changed) flags |= BridgeFlags.Changed;
            if (result.submitted) flags |= BridgeFlags.Submitted;

            m_Results.WriteF32(rid, flags, number);
            if (o.wantsRect) m_Results.AppendRect(result.rect);
        }

        private void DrawCheckbox(int rid, int segment, bool value, string label)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool state = value;
            bool changed = NowLayout.Checkbox(label)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref state);

            if (o.disabled) state = value;
            m_Results.WriteBool(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, state);
        }

        private void DrawSwitch(int rid, int segment, bool value, string label)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool state = value;
            bool changed = NowLayout.Switch(label)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetTextStyle(o.TextStyle(NowTextStyle.Body))
                .Draw(ref state);

            if (o.disabled) state = value;
            m_Results.WriteBool(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, state);
        }

        /// <summary>
        /// One option of section 2.5's <c>ui.radio</c> composite. The JavaScript side opens an identity scope for
        /// the group and emits one of these per option, keyed by the option string; the clicked flag is what tells
        /// it which one was chosen.
        /// </summary>
        private void DrawRadio(int rid, int segment, bool isOn, string label)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            bool clicked = NowLayout.Radio(label, isOn)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw();

            m_Results.WriteEvent(rid, o.Disable(clicked) ? BridgeFlags.Clicked : BridgeFlags.None);
        }

        private void DrawSlider(int rid, int segment, float value, float min, float max)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            float number = value;
            bool changed = NowLayout.Slider(min, max)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetStep(o.Has(Abi.OptStep) ? o.step : 0f)
                .Draw(ref number);

            if (o.disabled) number = value;
            m_Results.WriteF32(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, number);
        }

        private void DrawIntSlider(int rid, int segment, int value, float min, float max)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            int number = value;
            bool changed = NowLayout.Slider(min, max)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .SetStep(o.Has(Abi.OptStep) ? o.step : 1f)
                .Draw(ref number);

            if (o.disabled) number = value;
            m_Results.WriteI32(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, number);
        }

        /// <summary>
        /// Section 2.5's <c>ui.dropdown</c>. The INDEX never surfaces: JavaScript sends the current option's
        /// index and reads the selected index back, and maps both ends to the option string itself.
        /// </summary>
        private void DrawDropdown(int rid, int segment, int selected, List<string> options)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            int index = selected;
            bool changed = NowLayout.Dropdown(options)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref index);

            if (o.disabled) index = selected;
            m_Results.WriteI32(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, index);
        }

        private void DrawCombo(int rid, int segment, int selected, List<string> options)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            int index = selected;
            bool changed = NowLayout.ComboBox(options)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref index);

            if (o.disabled) index = selected;
            m_Results.WriteI32(rid, o.Disable(changed) ? BridgeFlags.Changed : BridgeFlags.None, index);
        }

        private void DrawColorField(int rid, int segment, int packed)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            Color color = Unpack(packed);
            bool changed = NowLayout.ColorPicker()
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref color);

            int result = Pack(color);

            m_Results.WriteI32(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, result);
        }

        /// <summary>Section 5.3's <c>color</c> kind: RGBA8 packed, red in the low byte.</summary>
        private static Color Unpack(int packed)
        {
            uint v = unchecked((uint)packed);
            return new Color(
                (v & 0xFF) / 255f,
                ((v >> 8) & 0xFF) / 255f,
                ((v >> 16) & 0xFF) / 255f,
                ((v >> 24) & 0xFF) / 255f);
        }

        private static int Pack(Color color)
        {
            uint r = (uint)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            uint g = (uint)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            uint b = (uint)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            uint a = (uint)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255);
            return unchecked((int)(r | (g << 8) | (b << 16) | (a << 24)));
        }

        private static readonly DateTime s_Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private void DrawDatePicker(int rid, int segment, long epochMs)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            DateTime value = s_Epoch.AddMilliseconds(epochMs);
            bool changed = NowLayout.DatePicker()
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref value);

            // SpecifyKind, not ToUniversalTime, and it is a bug fix rather than a tidy-up. JavaScript sends UTC
            // midnight of the chosen day and reads the same back (section 2.5's date kind), and the value handed
            // IN is DateTimeKind.Utc because s_Epoch is. But the value handed BACK by a calendar click is not:
            // NowDatePicker rebuilds it as `new DateTime(pending.ticks).Date + value.TimeOfDay`
            // (NowDatePicker.cs:186), whose Kind is Unspecified - and ToUniversalTime treats Unspecified as LOCAL
            // and shifts it by the host's offset. Measured at UTC+2: picking the 24th published the 23rd, because
            // 24th 00:00 "local" is 23rd 22:00 UTC. SpecifyKind reads the picker's answer as the calendar day it
            // is, which is the same convention the value arrived under, so the round-trip is symmetric.
            long ms = (long)(DateTime.SpecifyKind(value, DateTimeKind.Utc) - s_Epoch).TotalMilliseconds;

            m_Results.WriteI64(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, ms);
        }

        private void DrawTimePicker(int rid, int segment, int secondsSinceMidnight)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            TimeSpan value = TimeSpan.FromSeconds(secondsSinceMidnight);
            bool changed = NowLayout.TimePicker()
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref value);

            int seconds = (int)value.TotalSeconds;

            m_Results.WriteI32(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, seconds);
        }

        private void DrawTabs(int rid, int segment, int selected, List<string> labels)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            int index = selected;
            bool changed = NowLayout.TabBar(labels)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref index);

            m_Results.WriteI32(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, index);
        }

        // ---------------------------------------------------------------------------------------- feedback

        private void DrawProgress(float value01)
        {
            BridgeOptions o = TakeOptions();

            NowLayout.ProgressBar(value01)
                .SetOptions(o.ToLayout())
                .Draw();
        }
    }
}
