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
using NowUI.Markdown;
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

        /// <summary>
        /// W9. The SECOND mask word, present only when <see cref="Abi.OptMore"/> is set in the first - which
        /// nothing sets today. Read and carried so that a nowui.js from a later build does not desynchronise this
        /// decode; the fields its bits name sit past every field this build knows, at the end of the op, where
        /// the header's argSlots already accounts for them.
        /// </summary>
        public int mask2;

        public float width, height, minWidth, maxWidth, minHeight, maxHeight, grow, gap, step;
        public Vector4 padding;
        public int align, justify, style, textStyle;

        // --- W9: styling -------------------------------------------------------------------------------------

        /// <summary>The fill paint: <c>[tag, value]</c>. See <see cref="BridgePaint"/>.</summary>
        public int colorTag, colorValue;

        /// <summary>The outline paint: <c>[tag, value]</c>.</summary>
        public int strokeColorTag, strokeColorValue;

        public float stroke, blur, fontSize, angle;

        /// <summary>Corner radii in HUMAN order - topLeft, topRight, bottomRight, bottomLeft.</summary>
        /// <remarks>
        /// Deliberately not the renderer-packed order. <c>NowMaskShape.RoundedRect(NowRect, Vector4)</c> takes
        /// top-right, bottom-right, top-left, bottom-left (NowMaskShape.cs:105-111) and would silently rotate a
        /// three-different-corners radius, so this value goes through <c>NowCornerRadius</c> or the four-float
        /// setters everywhere it is used, never through a raw Vector4.
        /// </remarks>
        public Vector4 radius;

        /// <summary>Dash length, gap, offset.</summary>
        public Vector3 dash;

        public int cap, segments, spread;

        /// <summary>
        /// Whether a shape is filled. NOT a flag-only option, which is the whole point of spending a payload slot
        /// on a boolean: <c>fill: false</c> has to be sayable, and a flag-only bit can only say <c>true</c>.
        /// </summary>
        public bool fill;

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

            // W9. The continuation word, if any, comes BEFORE the payload - so a decoder that does not know the
            // second word's bits still knows where the first word's fields start. Nothing sets OptMore today; the
            // branch exists so that the day something does, this build skips it rather than mis-reading the whole
            // options object. `Has` is a `!= 0` test, so the sign of bit 31 is not a problem on either side.
            if (o.Has(Abi.OptMore))
            {
                o.mask2 = recorder.Slot(slot);
                ++slot;
            }

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

            // Bits 14 and 15 are flag-only and carry nothing. Bits 16-27 are W9's, still in ascending bit order.

            if (o.Has(Abi.OptColor))
            {
                o.colorTag = recorder.Slot(slot);
                o.colorValue = recorder.Slot(slot + 1);
                slot += 2;
            }

            if (o.Has(Abi.OptStroke)) o.stroke = recorder.SlotF32(slot++);

            if (o.Has(Abi.OptStrokeColor))
            {
                o.strokeColorTag = recorder.Slot(slot);
                o.strokeColorValue = recorder.Slot(slot + 1);
                slot += 2;
            }

            if (o.Has(Abi.OptRadius))
            {
                o.radius = new Vector4(
                    recorder.SlotF32(slot), recorder.SlotF32(slot + 1),
                    recorder.SlotF32(slot + 2), recorder.SlotF32(slot + 3));
                slot += 4;
            }

            if (o.Has(Abi.OptBlur)) o.blur = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptFontSize)) o.fontSize = recorder.SlotF32(slot++);
            if (o.Has(Abi.OptCap)) o.cap = recorder.Slot(slot++);

            if (o.Has(Abi.OptDash))
            {
                o.dash = new Vector3(
                    recorder.SlotF32(slot), recorder.SlotF32(slot + 1), recorder.SlotF32(slot + 2));
                slot += 3;
            }

            if (o.Has(Abi.OptSegments)) o.segments = recorder.Slot(slot++);
            if (o.Has(Abi.OptFill)) o.fill = recorder.Slot(slot++) != 0;
            if (o.Has(Abi.OptSpread)) o.spread = recorder.Slot(slot++);
            if (o.Has(Abi.OptAngle)) o.angle = recorder.SlotF32(slot++);

            return o;
        }

        /// <summary>
        /// The fill colour: the author's <c>color</c> when there is one, otherwise the theme's
        /// <paramref name="fallback"/> token.
        /// </summary>
        /// <remarks>
        /// The fallback is a TOKEN rather than a literal on purpose. A shape with no colour drawn in
        /// <c>Vector4.one</c> - which is what every shape builder's constructor defaults to - is white, and white
        /// on the light theme's white ground is the same invisible-and-correct failure W2 hit with its first
        /// label. Falling back to <c>NowColorToken.Text</c> means an uncoloured shape is visible in both themes.
        /// </remarks>
        public Color FillColor(NowThemeAsset theme, NowColorToken fallback)
        {
            return Has(Abi.OptColor)
                ? BridgePaint.Resolve(theme, colorTag, colorValue, fallback)
                : theme.GetColor(fallback);
        }

        /// <summary>
        /// The outline colour: the author's <c>strokeColor</c> when there is one, and otherwise the FILL colour.
        /// </summary>
        /// <remarks>
        /// FALLING BACK TO THE FILL COLOUR IS A BUG FIX, found in the browser and worth writing down because
        /// nothing about it is visible from the C# side. Every shape builder's constructor leaves
        /// <c>outlineColor</c> at <c>default</c> - which is transparent black, not "unset" (NowShape.cs:20-30) -
        /// and <c>SetColor</c> does not touch it. So <c>{ color: 'danger', fill: false, stroke: 6 }</c>, which
        /// reads as "a danger-coloured ring", produced a ring drawn in transparent black: no error, no warning,
        /// nothing on screen. It was the one shape missing from W9's first browser capture.
        /// <para>Defaulting the outline to the fill colour makes an outline the author asked for visible, and an
        /// explicit <c>strokeColor</c> still wins. A filled shape whose outline matches its fill is invisible in
        /// the harmless direction.</para>
        /// </remarks>
        public Color StrokeColor(NowThemeAsset theme, NowColorToken fallback)
        {
            return Has(Abi.OptStrokeColor)
                ? BridgePaint.Resolve(theme, strokeColorTag, strokeColorValue, fallback)
                : FillColor(theme, fallback);
        }

        /// <summary>Whether an outline colour has to be set at all: the author named a stroke, or a shape's fill off.</summary>
        public bool NeedsStrokeColor => Has(Abi.OptStrokeColor) || Has(Abi.OptStroke) || (Has(Abi.OptFill) && !fill);

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

    /// <summary>
    /// Section 5.3's <c>paint</c> kind: two slots, a tag and a value.
    /// </summary>
    /// <remarks>
    /// Tag 0's packing is byte-identical to the <c>color</c> kind's - it goes through the very same
    /// <see cref="BridgeReplay.UnpackColor"/> the COLOR_FIELD op uses - so <c>ui.colorField</c>'s value composes
    /// into <c>{ color: ... }</c> with no conversion at either end. That is one pair of functions rather than two
    /// that merely happen to agree today.
    /// </remarks>
    public static class BridgePaint
    {
        public const int TagLiteral = 0;
        public const int TagToken = 1;

        /// <summary>
        /// The colour a paint names. An unknown tag falls back rather than throwing: a paint arrives from a
        /// nowui.js the surface hash has already agreed with, so an unknown tag means the two halves agree on the
        /// manifest and disagree on this value - a bridge bug, and one a blank frame would hide.
        /// </summary>
        public static Color Resolve(NowThemeAsset theme, int tag, int value, NowColorToken fallback)
        {
            switch (tag)
            {
                case TagLiteral:
                    return BridgeReplay.UnpackColor(value);

                case TagToken:
                    return value >= 0 && value <= (int)NowColorToken.Scrim
                        ? theme.GetColor((NowColorToken)value)
                        : theme.GetColor(fallback);

                default:
                    return theme.GetColor(fallback);
            }
        }
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

        /// <summary>
        /// W9's POLYGON points, decoded. One reusable list for the same reason <see cref="m_Strings"/> is one:
        /// <c>Now.Polygon</c> reads it inside the call and keeps no reference, and a 500-point chart must not cost
        /// an allocation per pass - and there are two passes under exactLayout.
        /// </summary>
        private readonly List<Vector2> m_Points = new List<Vector2>(64);

        /// <summary>
        /// W9. The top-left of the innermost enclosing CANVAS, in screen space; (0,0) when there is none.
        /// </summary>
        /// <remarks>
        /// <para>Every drawing coordinate on the wire is CANVAS-LOCAL and this is what makes it absolute. Putting
        /// the translation here rather than in JavaScript is the whole point of the design: the ORIGIN is exact on
        /// every frame, because it comes from the layout pass that is running right now, so a drawing is never in
        /// the wrong place. Only the canvas's SIZE can be one frame stale, and only when the author sized it by
        /// growing rather than by declaring - a resized chart is briefly drawn at the old size, never in the wrong
        /// corner.</para>
        /// <para>It is reset in <see cref="BeginControlsPass"/> beside <c>m_Pending</c>, which is what keeps the
        /// two exactLayout passes identical (section 5.6).</para>
        /// </remarks>
        private Vector2 m_Origin;

        /// <summary>Reset at the top of every decode pass, beside the scope depth. See the file header.</summary>
        /// <summary>Whether anything has put ink on the surface yet this frame. See OpenTheme.</summary>
        private bool m_Painted;

        private void BeginControlsPass()
        {
            m_Painted = false;
            m_Pending = default;
            m_RuleOrdinal = 0;
            m_Origin = default;
        }

        // ---------------------------------------------------------------------------------------- geometry

        /// <summary>One f32 slot.</summary>
        private float Flt(int slot) => m_Recorder.SlotF32(slot);

        /// <summary>Section 5.3's <c>vec2</c>, translated out of canvas-local space into screen space.</summary>
        private Vector2 PointAt(int slot)
        {
            return new Vector2(m_Origin.x + Flt(slot), m_Origin.y + Flt(slot + 1));
        }

        /// <summary>
        /// Section 5.3's <c>rect</c>: x, y, width, height. The POSITION is translated; the SIZE is not, because a
        /// size is not a place.
        /// </summary>
        private NowRect RectAt(int slot)
        {
            return new NowRect(m_Origin.x + Flt(slot), m_Origin.y + Flt(slot + 1), Flt(slot + 2), Flt(slot + 3));
        }

        /// <summary>Section 5.3's <c>vec4</c>. Never translated: it is always parameters, never a position.</summary>
        private Vector4 Vec4At(int slot)
        {
            return new Vector4(Flt(slot), Flt(slot + 1), Flt(slot + 2), Flt(slot + 3));
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

                // ---- W9: drawing scopes --------------------------------------------------------------------
                case "CANVAS":
                    OpenCanvas(m_Recorder.Slot(args), m_Recorder.Slot(args + 1));
                    return true;

                case "MASK":
                    OpenMask(args);
                    return true;

                case "SPLIT":
                    OpenSplit(
                        m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                        m_Recorder.SlotF32(args + 2), m_Recorder.Slot(args + 3));
                    return true;

                case "PANE":
                    OpenPane(m_Recorder.Slot(args));
                    return true;

                case "THEME":
                    OpenTheme(m_Recorder.Slot(args), m_Recorder.Slot(args + 1), m_Recorder.Slot(args + 2));
                    return true;

                // ---- W9: drawings --------------------------------------------------------------------------
                case "RECT":
                    DrawRect(args);
                    return true;

                case "MARKDOWN":
                    DrawMarkdown(m_Recorder.Slot(args), m_Recorder.Slot(args + 1),
                                 m_Recorder.Text(m_Recorder.Slot(args + 2)), Flt(args + 3));
                    return true;

                case "IMAGE":
                    DrawImage(args);
                    return true;

                case "LOTTIE":
                    DrawLottie(args);
                    return true;

                case "CIRCLE":
                    DrawCircle(args);
                    return true;

                case "LINE":
                    DrawLine(args, cubic: false);
                    return true;

                case "BEZIER":
                    DrawLine(args, cubic: true);
                    return true;

                case "TRIANGLE":
                    DrawTriangle(args);
                    return true;

                case "POLYGON":
                    DrawPolygon(args);
                    return true;

                case "GRADIENT":
                    DrawGradient(args);
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
            m_Painted = true;
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
            m_Painted = true;
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
            m_Painted = true;
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

        /// <summary>
        /// <c>ui.markdown</c>: a rendered document, laid out where it sits.
        /// </summary>
        /// <remarks>
        /// <para>The no-rect <c>Draw()</c> is the one that matters: it measures through NowLayout.ContentRect,
        /// so the document is an ordinary child of whatever contains it and <c>ui.scroll</c> both sizes and
        /// clips it without being told anything. Drawing into a rect instead would mean reporting a height to
        /// JavaScript and being handed it back a frame later.</para>
        /// <para>THE TEXT DOES NOT CROSS EVERY FRAME, which is worth stating because the op's shape suggests
        /// it does. The recorder's <c>str</c> position is volatile the first time it sees a value and interns it
        /// the second, and an interned handle is permanent for the session - so a document that stays the same
        /// costs one UTF-8 encode on the frame it appears, one intern on the next, and NOTHING after that.
        /// Measured on the docs viewer: 582 slots and <b>0 text bytes</b> per frame at frame 3060.</para>
        /// <para>The other half of that rule is what makes it safe rather than merely fast. A document that
        /// CHANGES every frame never gets a second sighting, so it stays volatile and is re-encoded each time -
        /// the correct cost, and no permanent entry is leaked. Neither case needs an author to choose.</para>
        /// <para>A font size of 0 means "whatever the style says", which is how an author omits the option
        /// without the surface having to encode absence separately.</para>
        /// </remarks>
        private void DrawMarkdown(int rid, int segment, string source, float fontSize)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            NowMarkdownBuilder document = NowMarkdown.Document(source)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout());

            if (fontSize > 0f) document = document.SetFontSize(fontSize);

            NowMarkdownResult result = document.Draw();

            // The link is the control's value and the flag is what makes it a click rather than a standing
            // report: hoveredLink would otherwise be indistinguishable from a link the reader actually chose.
            bool clicked = !string.IsNullOrEmpty(result.clickedLink);
            m_Results.WriteString(rid, clicked ? BridgeFlags.Clicked : BridgeFlags.None,
                                  clicked ? result.clickedLink : string.Empty);
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

            Color color = UnpackColor(packed);
            bool changed = NowLayout.ColorPicker()
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Draw(ref color);

            int result = Pack(color);

            m_Results.WriteI32(rid, changed ? BridgeFlags.Changed : BridgeFlags.None, result);
        }

        /// <summary>Section 5.3's <c>color</c> kind: RGBA8 packed, red in the low byte.</summary>
        /// <remarks>Internal rather than private because W9's <see cref="BridgePaint"/> resolves a literal paint
        /// through this exact function - see its remarks for why sharing it is the point.</remarks>
        internal static Color UnpackColor(int packed)
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
            m_Painted = true;
            BridgeOptions o = TakeOptions();

            NowLayout.ProgressBar(value01)
                .SetOptions(o.ToLayout())
                .Draw();
        }

        // ------------------------------------------------------------------------------- W9: drawing scopes

        /// <summary>
        /// <c>ui.canvas</c>: one layout box, a coordinate origin, and a mask.
        /// </summary>
        /// <remarks>
        /// <para>Three things happen here and each earns its line. The COLUMN reserves the box - a canvas is a
        /// real container, so it may also hold controls, which flow normally while drawings paint at absolute
        /// coordinates; a chart with a legend row is one canvas. The ORIGIN makes every coordinate inside it
        /// local. The MASK means a drawing cannot escape its box, which is also what makes a canvas nest
        /// correctly inside a scroll view.</para>
        /// <para>The measured rect goes into the result table so the author can size next frame's drawing to it.
        /// It is one frame old, which is section 6.2's R6 - "everything a control returns is one frame old" -
        /// rather than a new exception: a canvas that declared numeric width and height has no lag at all, and one
        /// that grew is stale in its SIZE only, never in its position.</para>
        /// <para>The rid rather than the segment goes to SetId, for the reason OpenContainer records at
        /// length.</para>
        /// </remarks>
        private void OpenCanvas(int rid, int segment)
        {
            BridgeOptions o = TakeOptions();
            ref BridgeScopeFrame frame = ref Push(rid);

            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            NowLayoutScope scope = NowLayout.Column().SetId(new NowId(rid)).Options(o.ToLayout(container: true)).Begin();

            frame.layout = scope;
            frame.kinds |= BridgeScopeFrame.HasLayout;

            frame.savedOrigin = m_Origin;
            frame.kinds |= BridgeScopeFrame.HasOrigin;
            m_Origin = new Vector2(scope.rect.x, scope.rect.y);

            frame.mask = Now.Mask(scope.rect);
            frame.kinds |= BridgeScopeFrame.HasMask;

            // A presence-only record plus the rect. WriteEvent is exactly the "no value, flags only" write the
            // design asked for under the name WriteNone - Results.cs has had it since W3 (BridgeValueKind.None),
            // so no new method is needed. The rect follows immediately, which is the only order AppendRect
            // supports.
            m_Results.WriteEvent(rid, BridgeFlags.None);
            m_Results.AppendRect(scope.rect);
        }

        /// <summary>
        /// <c>ui.mask</c>: an analytic mask over a SUBSET of the enclosing canvas. A canvas already masks to its
        /// own box, so this is for the cases that box cannot express - a circular avatar clip, a capsule, a
        /// rounded panel.
        /// </summary>
        /// <remarks>
        /// It opens an identity scope like every other scope in this surface, which has a consequence worth
        /// stating in the docs rather than leaving to be discovered: wrapping EXISTING controls in a mask changes
        /// their canonical path, so their keyed state - a scroll offset, a caret - resets once. That is the same
        /// cost as wrapping them in a <c>ui.column</c> and is the price of uniform scope identity.
        /// </remarks>
        private void OpenMask(int args)
        {
            TakeOptions();

            int rid = m_Recorder.Slot(args);
            int segment = m_Recorder.Slot(args + 1);
            int kind = m_Recorder.Slot(args + 2);
            Vector4 extra = Vec4At(args + 7);
            float feather = Flt(args + 11);

            NowMaskShape shape;

            switch (kind)
            {
                case 0:
                    shape = NowMaskShape.Rectangle(RectAt(args + 3));
                    break;

                case 1:
                    // Through NowCornerRadius, NOT the raw Vector4 overload: that one takes the renderer's packed
                    // order (top-right, bottom-right, top-left, bottom-left, NowMaskShape.cs:105-111) and would
                    // silently rotate a mask whose four corners differ.
                    shape = NowMaskShape.RoundedRect(
                        RectAt(args + 3), new NowCornerRadius(extra.x, extra.y, extra.z, extra.w));
                    break;

                case 2:
                    shape = NowMaskShape.Ellipse(RectAt(args + 3));
                    break;

                case 3:
                    // The rect slot carries cx, cy, r for this kind: a circle has no width and height to send.
                    shape = NowMaskShape.Circle(PointAt(args + 3), Flt(args + 5));
                    break;

                case 4:
                    // Two POINTS, so both are translated. RectAt would translate the first and not the second,
                    // which is exactly the bug this branch exists to not have.
                    shape = NowMaskShape.Capsule(PointAt(args + 3), PointAt(args + 5), extra.x);
                    break;

                default:
                    Report("NowUI bridge: MASK carried shape kind " + kind + ", which this build does not know. " +
                           "The surface hash matched, so abi.js and Abi.cs agree on the manifest and disagree on " +
                           "this enum. Falling back to a rectangular mask.");
                    shape = NowMaskShape.Rectangle(RectAt(args + 3));
                    break;
            }

            if (feather > 0f) shape = shape.SetFeather(feather);

            ref BridgeScopeFrame frame = ref Push(rid);

            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            frame.mask = Now.Mask(shape);
            frame.kinds |= BridgeScopeFrame.HasMask;
        }

        /// <summary>
        /// <c>ui.split</c>: two resizable panes and a draggable divider. It needs no second kind of scope bracket
        /// because brackets NEST - a split is SPLIT{ PANE(0){..} PANE(1){..} }, three ops and one structural rule
        /// that already existed.
        /// </summary>
        /// <remarks>
        /// The control draws FIRST and the identity scope opens after it, which is <see cref="OpenFoldout"/>'s
        /// shape: the split's own id resolves under the enclosing scope, and the panes' contents resolve under the
        /// split's. The ratio comes back through the result table so a drag reaches the author's state.
        /// </remarks>
        private void OpenSplit(int rid, int segment, float ratio, int axis)
        {
            BridgeOptions o = TakeOptions();
            ++decodedControls;
            Identify(rid, segment);

            float value = ratio;
            NowSplitViewResult result = NowLayout.SplitView((NowSplitAxis)axis)
                .SetId(new NowId(segment))
                .SetOptions(o.ToLayout())
                .Begin(ref value);

            m_Results.WriteF32(rid, result.dragging ? BridgeFlags.Changed : BridgeFlags.None, value);

            ref BridgeScopeFrame frame = ref Push(rid);
            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;
            frame.split = result;
            frame.kinds |= BridgeScopeFrame.HasSplit;
        }

        /// <summary>
        /// One pane of the enclosing split. <c>NowSplitPaneScope</c> already bundles a mask and a layout area
        /// (NowSplitView.cs:199-215), so a pane clips and lays out for free.
        /// </summary>
        /// <remarks>
        /// No identity scope of its own, and that is a decision rather than an omission: <c>NowLayout.Area</c> is
        /// opened with the split's own per-pane resolved key, so the two panes are already distinct identity
        /// parents derived from the split's id. Adding a bridge-invented segment on top would be structure the C#
        /// API does not have.
        /// </remarks>
        private void OpenPane(int index)
        {
            TakeOptions();

            if (m_Depth == 0 || (m_Scopes[m_Depth - 1].kinds & BridgeScopeFrame.HasSplit) == 0)
            {
                // The validator balances brackets but does not type them, so this is reachable from a malformed
                // stream. A frame is still pushed: the matching OP_SCOPE_CLOSE is coming either way, and popping
                // a scope that was never pushed is the worse failure.
                Report("NowUI bridge: PANE appeared outside a SPLIT. The pane was skipped and its body will draw " +
                       "in the enclosing container.");
                Push(m_Depth > 0 ? m_Scopes[m_Depth - 1].rid : 0);
                return;
            }

            NowSplitViewResult split = m_Scopes[m_Depth - 1].split;
            int parentRid = m_Scopes[m_Depth - 1].rid;

            ref BridgeScopeFrame frame = ref Push(parentRid);
            frame.pane = index == 0 ? split.BeginFirst() : split.BeginSecond();
            frame.kinds |= BridgeScopeFrame.HasPane;
        }

        /// <summary>
        /// <c>ui.theme</c>, in the only form a browser host can serve: light or dark.
        /// </summary>
        /// <remarks>
        /// The general form - a theme by asset NAME - does need a host resource manifest, which is the same
        /// missing piece that blocks textures. Light and dark do not: <see cref="ThemeAssets"/> builds them the
        /// way NowTheme builds its own defaults, so the "manifest" is a two-entry table.
        /// </remarks>
        private void OpenTheme(int rid, int segment, int mode)
        {
            TakeOptions();

            NowThemeAsset asset = ThemeAssets(dark: mode == 1);

            ref BridgeScopeFrame frame = ref Push(rid);

            frame.id = BridgeIdentity.Scope(segment);
            frame.kinds |= BridgeScopeFrame.HasIdScope;

            frame.theme = NowControls.Theme(asset);
            frame.kinds |= BridgeScopeFrame.HasTheme;

            // The ground a host should clear to, recorded HERE because this is the only moment the author's theme
            // is knowable: the scope closes before RunFrame returns, and a host that samples the theme after the
            // frame - or before it, which is when a clear actually runs - sees the default instead.
            //
            // ONLY a theme that owns the whole page may set the ground, and "owns the whole page" means nothing
            // had been painted yet when it opened. The first version of this took the outermost theme scope
            // instead, which is wrong for the common shape of theming ONE PANE: draw.js draws a full column of
            // controls and then wraps only its right-hand canvas in ui.theme('dark'). Taking that scope's
            // background cleared the page dark while the left pane was still drawing the default theme's dark
            // text, and the controls went invisible - the same invisible-but-correct failure this whole surface
            // keeps trying to design out, reintroduced by the fix for it.
            if (!frameGround.HasValue && !m_Painted && decodedControls == 0)
                frameGround = asset.GetColor(NowColorToken.Background);
        }

        private static NowThemeAsset s_Light;
        private static NowThemeAsset s_Dark;

        /// <summary>
        /// The bridge's own light and dark assets, built once and cached for the session.
        /// </summary>
        /// <remarks>
        /// Built here rather than borrowed from <c>NowTheme</c>, and the difference matters. NowTheme's cached
        /// defaults are reached by TOGGLING the global <c>NowTheme.preferDark</c> and reading <c>themeAsset</c>
        /// back - and <c>themeAsset</c> resolves against the theme STACK, so inside another theme scope that
        /// returns the enclosing asset's counterpart rather than the default. Constructing the two assets the same
        /// way NowTheme constructs its own (NowTheme.cs:79-96) makes <c>ui.theme('dark')</c> mean the dark theme
        /// wherever it appears, with no global state poked on the way.
        /// </remarks>
        private static NowThemeAsset ThemeAssets(bool dark)
        {
            if (dark)
            {
                if (s_Dark == null)
                {
                    s_Dark = ScriptableObject.CreateInstance<NowThemeAsset>();
                    s_Dark.name = "NowUI Bridge Dark Theme";
                    s_Dark.hideFlags = HideFlags.HideAndDontSave;
                    s_Dark.ResetToDefaults(dark: true);
                }

                return s_Dark;
            }

            if (s_Light == null)
            {
                s_Light = ScriptableObject.CreateInstance<NowThemeAsset>();
                s_Light.name = "NowUI Bridge Light Theme";
                s_Light.hideFlags = HideFlags.HideAndDontSave;
                s_Light.ResetToDefaults(dark: false);
            }

            return s_Light;
        }

        // ------------------------------------------------------------------------------------ W9: drawings

        /// <summary>
        /// <c>ui.rect</c>. The one drawing that has a semantic style, because it is the one NowUI shape with a
        /// <c>SetStyle</c>.
        /// </summary>
        /// <remarks>
        /// STYLE FIRST, THEN EXPLICIT. <c>SetStyle</c> sets colour, radius and outline together, so semantic-then-
        /// explicit is the only layering in which <c>{ style: 'accent', radius: 0 }</c> means what it reads as. And
        /// a rect with NEITHER a colour nor a style takes the Surface style rather than a raw white: this is the
        /// same white-on-white failure the RULE op's comment already records, and the same fix.
        /// </remarks>
        private void DrawRect(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRectangle rect = Now.Rectangle(RectAt(args));

            bool styled = o.Has(Abi.OptStyle) || o.disabled || !o.Has(Abi.OptColor);
            if (styled) rect = rect.SetStyle(theme, o.RectStyle(NowRectangleStyle.Surface));

            if (o.Has(Abi.OptColor)) rect = rect.SetColor(o.FillColor(theme, NowColorToken.Text));

            // The four-float overload, in human order. SetRadius(Vector4) is the renderer's packed order.
            if (o.Has(Abi.OptRadius)) rect = rect.SetRadius(o.radius.x, o.radius.y, o.radius.z, o.radius.w);

            if (o.Has(Abi.OptStroke)) rect = rect.SetOutline(o.stroke);

            // An explicit strokeColor always wins. Without one, the outline is filled in from the fill colour
            // ONLY when no style supplied one - SetStyle sets colour, radius and outline together, and
            // { style: 'accent', stroke: 3 } must keep the accent style's own outline.
            if (o.Has(Abi.OptStrokeColor) || (o.Has(Abi.OptStroke) && !styled))
                rect = rect.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Border));
            if (o.Has(Abi.OptBlur)) rect = rect.SetBlur(o.blur);

            rect.Draw();
        }

        /// <summary>
        /// <c>ui.image</c>: a rectangle with a picture in it, fetched by URL and drawn as soon as it arrives.
        /// </summary>
        /// <remarks>
        /// <para>THE DEFAULT COLOUR IS WHITE, and that is the difference between this and <see cref="DrawRect"/>
        /// rather than an oversight. A rect with no colour takes a theme STYLE, which tints whatever it draws; a
        /// photograph tinted by the surface colour is a photograph the author did not ask for. So an image starts
        /// at white - the identity tint - and honours <c>color</c> only when one is named, which is then a real
        /// tint and not an accident.</para>
        /// <para>NOTHING BLOCKS. The first frame that names a URL starts a download and draws the placeholder;
        /// later frames find the texture in the cache and draw it. That works because the browser host runs frames
        /// continuously and <c>NowMarkdownImages.Tick</c> is on <c>NowRuntime.onFrame</c>, so the download makes
        /// progress between frames without this decode waiting for anything.</para>
        /// <para>A FAILED IMAGE STILL DRAWS ITS BOX. Leaving nothing would collapse a layout around a hole and
        /// leave the author guessing whether the URL was wrong or the op did nothing; a muted box keeps the
        /// composition and says the slot was reserved. The reason it failed is on the console, once per URL,
        /// through the cache's own reporting rather than once per frame from here.</para>
        /// </remarks>
        private void DrawImage(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect where = RectAt(args);
            string url = m_Recorder.Text(m_Recorder.Slot(args + 4));

            Texture2D texture;
            NowMarkdownImageState state = NowMarkdownImages.GetState(url, out texture);

            NowRectangle rect = Now.Rectangle(where);

            if (state == NowMarkdownImageState.Loaded && texture != null)
            {
                rect = rect.SetTexture(texture)
                    .SetColor(o.Has(Abi.OptColor) ? o.FillColor(theme, NowColorToken.Text) : Color.white);

                switch ((BridgeImageFit)m_Recorder.Slot(args + 5))
                {
                    case BridgeImageFit.Contain:
                        // One flag, because NowUI already does this: Now.cs:2279 shrinks the quad to the
                        // source's aspect and centres it. Nothing to compute here.
                        rect = rect.SetPreserveAspect(true);
                        break;

                    case BridgeImageFit.Cover:
                        rect = rect.SetUV(CoverUV(texture, where));
                        break;
                }
            }
            else
            {
                // The placeholder. Muted rather than the author's tint: a solid block of their accent colour
                // reads as a drawn shape, where the muted surface reads as something not there yet.
                rect = rect.SetStyle(theme, NowRectangleStyle.Muted);
            }

            if (o.Has(Abi.OptRadius)) rect = rect.SetRadius(o.radius.x, o.radius.y, o.radius.z, o.radius.w);
            if (o.Has(Abi.OptStroke)) rect = rect.SetOutline(o.stroke);
            if (o.Has(Abi.OptStrokeColor)) rect = rect.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Border));

            rect.Draw();
        }

        /// <summary>
        /// The texture sub-rectangle that makes a picture COVER a box: centred, cropped on whichever axis has
        /// spare, and never distorted.
        /// </summary>
        /// <remarks>
        /// <para>This is the one fit mode NowUI does not already implement, and the reason is that it cannot be
        /// a flag: cropping needs the SOURCE's pixel dimensions, which are only known once the texture has
        /// actually arrived. <c>preserveAspect</c> can be set before then because the renderer resolves it at
        /// draw time; a uvRect cannot.</para>
        /// <para>Worked example, because contain and cover are opposites and easy to swap: a 200x100 source in a
        /// 100x100 box has sourceAspect 2 and boxAspect 1. boxAspect &lt; sourceAspect, so the box is the
        /// narrower shape and the crop is horizontal: keep 1/2 of the width, offset by 1/4, giving a 100x100
        /// region of the source that fills the box exactly. Contain would instead have drawn 100x50 and left
        /// two empty bands.</para>
        /// <para>The result is always centred. An anchor option would be the natural next thing to want - "crop
        /// to the top" is what a portrait usually needs - and is deliberately not invented here.</para>
        /// </remarks>
        private static Vector4 CoverUV(Texture2D texture, NowRect box)
        {
            // A degenerate box or texture has no aspect to preserve; the full range is the honest answer and it
            // makes cover behave exactly like stretch rather than dividing by zero.
            if (texture.width <= 0 || texture.height <= 0 || box.width <= 0f || box.height <= 0f)
                return new Vector4(0f, 0f, 1f, 1f);

            float sourceAspect = (float)texture.width / texture.height;
            float boxAspect = box.width / box.height;

            if (boxAspect > sourceAspect)
            {
                // The box is the wider shape, so the full width is used and the height is cropped.
                float keep = sourceAspect / boxAspect;
                return new Vector4(0f, (1f - keep) * 0.5f, 1f, keep);
            }

            float keepWidth = boxAspect / sourceAspect;
            return new Vector4((1f - keepWidth) * 0.5f, 0f, keepWidth, 1f);
        }

        /// <summary>
        /// <c>ui.lottie</c>: a vector animation from a URL, drawn at the playback position the caller names.
        /// </summary>
        /// <remarks>
        /// <para>The shape is deliberately the same as <see cref="DrawImage"/> - ask the cache, draw what is
        /// there, draw a placeholder when it is not - so an author who has used one already knows this one. The
        /// only real difference is the time argument, and the reason it is an argument is in the op's own entry
        /// in Abi.cs.</para>
        /// <para>A Lottie that has NOT arrived draws nothing rather than a muted box. That is the opposite of
        /// the image decision and it is deliberate: an image is usually content in a layout, where a hole is a
        /// broken page, while a Lottie is usually an accent over one - a spinner, a tick, a flourish - where a
        /// grey rectangle appearing for a moment before the animation is worse than nothing appearing at all.
        /// The failure is still reported by the cache, once, with the URL in it.</para>
        /// </remarks>
        private void DrawLottie(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect where = RectAt(args);
            string url = m_Recorder.Text(m_Recorder.Slot(args + 4));
            float time = Flt(args + 5);

            NowLottieAsset asset;
            string error;
            NowLottieCacheState state = NowLottieCache.GetState(url, out asset, out error);

            if (state != NowLottieCacheState.Loaded || asset == null) return;

            var lottie = new NowLottie(where, asset).SetTime(time);

            if (o.Has(Abi.OptColor)) lottie = lottie.SetColor(o.FillColor(theme, NowColorToken.Text));

            lottie.Draw();
        }

        /// <summary>
        /// <c>ui.circle</c>, on <c>Now.Ellipse(center, radius)</c> so that one op serves both a circle and an
        /// ellipse - the author broadcasts a scalar radius, or names two.
        /// </summary>
        /// <remarks>
        /// The RADIUS is not translated by the origin. It is a size, and a size is not a place; only the centre
        /// moves with the canvas.
        /// </remarks>
        private void DrawCircle(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowCircle circle = Now.Ellipse(PointAt(args), new Vector2(Flt(args + 2), Flt(args + 3)))
                .SetColor(o.FillColor(theme, NowColorToken.Text));

            if (o.Has(Abi.OptSegments)) circle = circle.SetSegments(o.segments);
            if (o.Has(Abi.OptStroke)) circle = circle.SetOutline(o.stroke);
            if (o.NeedsStrokeColor) circle = circle.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Text));

            // AFTER SetColor, and this is not stylistic: SetColor sets fill = true as a side effect
            // (NowShape.cs:59), so an explicit `fill: false` applied before it would be silently undone.
            if (o.Has(Abi.OptFill)) circle = circle.SetFill(o.fill);

            circle.Draw();
        }

        /// <summary><c>ui.line</c> and <c>ui.bezier</c>: the same NowLine, straight or cubic.</summary>
        private void DrawLine(int args, bool cubic)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowLine line = cubic
                ? Now.Bezier(PointAt(args), PointAt(args + 2), PointAt(args + 4), PointAt(args + 6))
                : Now.Line(PointAt(args), PointAt(args + 2));

            line = line.SetColor(o.FillColor(theme, NowColorToken.Text));

            if (o.Has(Abi.OptStroke)) line = line.SetWidth(o.stroke);
            if (o.Has(Abi.OptCap)) line = line.SetCap((NowLineCap)o.cap);
            if (o.Has(Abi.OptDash)) line = line.SetDash(o.dash.x, o.dash.y, o.dash.z);

            line.Draw();
        }

        private void DrawTriangle(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowTriangle triangle = Now.Triangle(PointAt(args), PointAt(args + 2), PointAt(args + 4))
                .SetColor(o.FillColor(theme, NowColorToken.Text));

            if (o.Has(Abi.OptStroke)) triangle = triangle.SetOutline(o.stroke);
            if (o.NeedsStrokeColor) triangle = triangle.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Text));
            if (o.Has(Abi.OptFill)) triangle = triangle.SetFill(o.fill);

            triangle.Draw();
        }

        /// <summary>Section 5.3's <c>vec2list</c>: one count slot, then 2 * count f32.</summary>
        private void DrawPolygon(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            int count = m_Recorder.Slot(args);
            m_Points.Clear();
            for (int i = 0; i < count; ++i) m_Points.Add(PointAt(args + 1 + i * 2));

            // Fewer than three points is not a polygon. Silently drawing nothing is right here rather than a
            // diagnostic: a chart whose data is still loading legitimately has none, and NowPolygon's own path
            // would do the same thing without saying so either.
            if (m_Points.Count < 3) return;

            NowPolygon polygon = Now.Polygon(m_Points, 0, m_Points.Count)
                .SetColor(o.FillColor(theme, NowColorToken.Text));

            if (o.Has(Abi.OptStroke)) polygon = polygon.SetOutline(o.stroke);
            if (o.NeedsStrokeColor) polygon = polygon.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Text));
            if (o.Has(Abi.OptFill)) polygon = polygon.SetFill(o.fill);

            polygon.Draw();
        }

        /// <summary>
        /// <c>ui.gradient</c>. The two ramp ends are <c>paint</c> arguments rather than options, because a
        /// gradient with one colour is not a gradient - they are required, not decoration.
        /// </summary>
        private void DrawGradient(int args)
        {
            m_Painted = true;
            BridgeOptions o = TakeOptions();
            NowThemeAsset theme = NowTheme.themeAsset;

            NowRect rect = RectAt(args);
            Color from = BridgePaint.Resolve(
                theme, m_Recorder.Slot(args + 4), m_Recorder.Slot(args + 5), NowColorToken.Surface);
            Color to = BridgePaint.Resolve(
                theme, m_Recorder.Slot(args + 6), m_Recorder.Slot(args + 7), NowColorToken.Accent);

            NowGradient gradient = Now.Gradient(rect, from, to);

            switch ((NowGradientKind)m_Recorder.Slot(args + 8))
            {
                case NowGradientKind.Radial:
                    gradient = gradient.SetRadial();
                    break;

                case NowGradientKind.Conic:
                    gradient = gradient.SetConic();
                    break;

                default:
                    // SetLinear(angle) is CSS's convention - 0 points up, 90 right, clockwise - and NowGradient
                    // says so (NowGradient.cs:271-273), so `angle` needs no conversion on either side.
                    gradient = o.Has(Abi.OptAngle) ? gradient.SetLinear(o.angle) : gradient.SetLinear();
                    break;
            }

            if (o.Has(Abi.OptSpread)) gradient = gradient.SetSpread((NowGradientSpread)o.spread);
            if (o.Has(Abi.OptRadius)) gradient = gradient.SetRadius(o.radius.x, o.radius.y, o.radius.z, o.radius.w);
            if (o.Has(Abi.OptBlur)) gradient = gradient.SetBlur(o.blur);
            if (o.Has(Abi.OptStroke)) gradient = gradient.SetOutline(o.stroke);
            if (o.NeedsStrokeColor) gradient = gradient.SetOutlineColor(o.StrokeColor(theme, NowColorToken.Border));

            gradient.Draw();
        }
    }
}
