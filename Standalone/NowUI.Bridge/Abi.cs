// W2 - the ABI, managed half. Docs/Standalone/M3-Spec.md sections 5.2 and 5.3.
//
// The C# twin of wwwroot/nowui/abi.js. The two files hold the same signature list, derive opcodes from it with the
// same hash, and fold the resulting table into the same 32-bit surface hash. That hash rides in header slot 1 of
// every frame and the validator refuses a frame whose hash does not match (gate G3, section 7.4), so the two halves
// cannot drift into decoded garbage - they drift into a refused frame naming both hashes, on the first frame.
//
// The signature strings are the NowUI members each op replays into, not the JavaScript names. That is deliberate:
// the JavaScript name is a surface decision, and the thing whose drift the hash has to catch is the C# member.
// W8's generator emits both files from Standalone/Surface/surface.json; until then they are edited in pairs, and
// the surface hash is what makes a forgotten pair a refused frame rather than a wrong control.

using System;
using System.Collections.Generic;
using System.Text;

namespace NowUI.Bridge
{
    /// <summary>The argument kinds of section 5.3, with the slot width each occupies.</summary>
    public enum ArgKind
    {
        /// <summary>One slot, read through the Int32 view.</summary>
        I32,

        /// <summary>One slot, read through the Float32 view.</summary>
        F32,

        /// <summary>One slot, 0 or 1.</summary>
        Bool,

        /// <summary>One slot: the C# enum's integer value.</summary>
        Enum,

        /// <summary>One slot: >= 0 an intern handle, &lt; 0 the bitwise complement of a volatile-table index.</summary>
        Str,

        /// <summary>One slot, always >= 0. A key is always interned, never volatile (section 5.4).</summary>
        Key,

        /// <summary>One slot: >= 0 an intern handle, &lt;= -1 an anonymous ordinal (section 3.3).</summary>
        Seg,

        /// <summary>One slot: the recorder's dense trie integer (section 3.4), echoed and never computed here.</summary>
        Rid,

        /// <summary>One slot, RGBA8 packed.</summary>
        Color,

        /// <summary>Two slots of f32.</summary>
        Vec2,

        /// <summary>Four slots of f32.</summary>
        Vec4,

        /// <summary>Two slots, low word first. A 64-bit value that does not survive an f32.</summary>
        I64,

        /// <summary>
        /// Variable width: one count slot, then that many <see cref="Str"/> slots. The number carried in
        /// <see cref="OpSpec.Slots"/> is the MINIMUM - the op's size with an empty list.
        /// </summary>
        Strlist,

        /// <summary>
        /// Variable width: one bitmask slot, then the named fields in ascending bit order (section 2.7). The
        /// number carried in <see cref="OpSpec.Slots"/> is the op's size with an empty options object.
        /// </summary>
        Opts,
    }

    /// <summary>One op: what it is called, which NowUI member it replays into, and its wire shape.</summary>
    public sealed class OpSpec
    {
        internal OpSpec(string name, string signature, int salt, ArgKind[] args, bool opensScope = false)
        {
            Name = name;
            Signature = signature;
            Salt = salt;
            Args = args;
            OpensScope = opensScope;

            int slots = 0;
            bool variable = false;
            for (int i = 0; i < args.Length; ++i)
            {
                slots += Abi.SlotsFor(args[i]);
                if (args[i] == ArgKind.Strlist || args[i] == ArgKind.Opts) variable = true;
            }
            Slots = slots;
            Variable = variable;

            Opcode = Abi.OpcodeFor(signature, salt);
        }

        /// <summary>The bridge's name for the op. Diagnostics only; nothing on the wire carries it.</summary>
        public string Name { get; }

        /// <summary>Declaring type, member name and parameter type list - what section 5.2 hashes.</summary>
        public string Signature { get; }

        /// <summary>The checked-in collision resolver. Zero for every entry today.</summary>
        public int Salt { get; }

        public ArgKind[] Args { get; }

        /// <summary>
        /// Total argument slots - the exact width for a fixed op, and the MINIMUM for a variable one. Carried on
        /// the wire too, so an unknown opcode can be skipped.
        /// </summary>
        public int Slots { get; }

        /// <summary>
        /// Whether this op carries a variable-width argument (<see cref="ArgKind.Strlist"/> or
        /// <see cref="ArgKind.Opts"/>). The validator compares <c>argSlots</c> for equality against
        /// <see cref="Slots"/> for a fixed op and for a lower bound on a variable one - section 5.5 rule 4 is a
        /// walk that must land on <c>opEnd</c>, and the per-op width is what makes that walk exact.
        /// </summary>
        public bool Variable { get; }

        public int Opcode { get; }

        /// <summary>
        /// Whether this op opens a scope that a later OP_SCOPE_CLOSE closes. Section 5.5 rule 5 balances on it.
        /// </summary>
        /// <remarks>
        /// Deliberately NOT part of the surface hash. The hash exists to catch a JavaScript bundle and a wasm
        /// module that disagree about what an opcode MEANS on the wire, and the JavaScript side never consults
        /// this flag - it emits its own OP_SCOPE_CLOSE from a finally block (section 4.1), so scope structure is
        /// carried explicitly in the stream rather than inferred from the table on either side.
        /// </remarks>
        public bool OpensScope { get; }
    }

    /// <summary>The wire format: header layout, opcodes, and the surface hash both halves must agree on.</summary>
    public static class Abi
    {
        /// <summary>
        /// Header magic, slot 0, section 5.2's literal used verbatim.
        /// </summary>
        /// <remarks>
        /// A note for anyone reading a hex dump: the spec annotates 0x424F574E as 'NOWB'. Little-endian, that value
        /// is the bytes 4E 57 4F 42 - "NWOB". The number is what both halves compare and it is used exactly as
        /// written; only the spec's mnemonic is wrong.
        /// </remarks>
        public const int Magic = 0x424F574E;

        // Fixed header slots, before the intern and volatile tables (section 5.2).
        public const int HdrMagic = 0;
        public const int HdrSurfaceHash = 1;
        public const int HdrFrameFlags = 2;
        public const int HdrInternCount = 3;
        public const int HdrVolatileCount = 4;
        public const int HdrTextBytes = 5;
        public const int HdrOpStart = 6;
        public const int HdrOpEnd = 7;

        /// <summary>The number of fixed header slots.</summary>
        public const int HeaderSlots = 8;

        // frameFlags bits, header slot 2.
        public const int FlagFaulted = 1 << 0;
        public const int FlagExactLayout = 1 << 1;
        public const int FlagHasNewStrings = 1 << 2;

        /// <summary>The only negative value <c>record</c> returns (section 5.1).</summary>
        public const int NeedMore = -1;

        /// <summary>Section 5.1's hard cap: 4 M slots is 16 MB of ops.</summary>
        public const int MaxSlots = 4 * 1024 * 1024;

        // Structural opcodes. 0-15 are reserved and FIXED (section 5.2); only these five are defined, and 5-15 are
        // held so a later structural op never has to displace a content-hashed one.
        public const int OpInvalid = 0;
        public const int OpScopeClose = 1;
        public const int OpCallbackBegin = 2;
        public const int OpCallbackEnd = 3;
        public const int OpNop = 4;

        /// <summary>The first opcode a content hash may take.</summary>
        public const int FirstHashedOp = 16;

        // ---------------------------------------------------------------------------------------- options
        //
        // Section 2.7's options object on the wire, and the twin of abi.js's OPT_* block. One bitmask slot names
        // which fields follow; the fields follow in ASCENDING BIT ORDER, which is the only ordering rule either
        // half needs and the reason neither carries a field index. A flag-only option occupies a bit and no slot.

        public const int OptWidth = 1 << 0;        // f32
        public const int OptHeight = 1 << 1;       // f32
        public const int OptMinWidth = 1 << 2;     // f32
        public const int OptMaxWidth = 1 << 3;     // f32
        public const int OptMinHeight = 1 << 4;    // f32
        public const int OptMaxHeight = 1 << 5;    // f32
        public const int OptGrow = 1 << 6;         // f32
        public const int OptGap = 1 << 7;          // f32
        public const int OptPadding = 1 << 8;      // 4 x f32, left top right bottom
        public const int OptAlign = 1 << 9;        // NowLayoutAlign
        public const int OptJustify = 1 << 10;     // NowLayoutJustify
        public const int OptStyle = 1 << 11;       // NowRectangleStyle
        public const int OptTextStyle = 1 << 12;   // NowTextStyle
        public const int OptStep = 1 << 13;        // f32
        public const int OptRect = 1 << 14;        // flag only
        public const int OptDisabled = 1 << 15;    // flag only

        /// <summary>The highest option bit this build knows.</summary>
        public const int OptKnownMask = (1 << 16) - 1;

        /// <summary>Payload slots per option bit, in ascending bit order. The two zeroes are the flag-only ones.</summary>
        public static readonly int[] OptSlots = { 1, 1, 1, 1, 1, 1, 1, 1, 4, 1, 1, 1, 1, 1, 0, 0 };

        internal static int SlotsFor(ArgKind kind)
        {
            switch (kind)
            {
                case ArgKind.Vec2: return 2;
                case ArgKind.Vec4: return 4;
                case ArgKind.I64: return 2;

                // The MINIMUM width of the two variable kinds: one count slot, one bitmask slot. The recorder
                // writes the real total into the op header's high 16 bits and the validator walks that, so a
                // variable-width op is skippable by a decoder that has never heard of it.
                case ArgKind.Strlist: return 1;
                case ArgKind.Opts: return 1;

                default: return 1;
            }
        }

        /// <summary>
        /// FNV-1a over the UTF-16 code units of <paramref name="text"/>. The JavaScript half hashes
        /// <c>charCodeAt(i)</c>, which is the same sequence, so the two produce the same value for every string -
        /// including the non-ASCII ones a member name will never contain but a future signature might.
        /// </summary>
        public static uint Fnv1a32(string text)
        {
            unchecked
            {
                uint h = 0x811C9DC5u;
                for (int i = 0; i < text.Length; ++i)
                {
                    h ^= text[i];
                    h *= 0x01000193u;
                }
                return h;
            }
        }

        /// <summary>
        /// Section 5.2: the low 16 bits of a hash of the signature, lifted above the reserved structural range.
        /// Content-hashed, not ordinal, so adding a function renumbers nothing and a JavaScript bundle and a wasm
        /// build from different commits still agree about every function both of them have.
        /// </summary>
        public static int OpcodeFor(string signature, int salt = 0)
        {
            uint h = Fnv1a32(salt == 0 ? signature : signature + "#" + salt.ToString());
            return FirstHashedOp + (int)((h & 0xFFFFu) % (0x10000u - FirstHashedOp));
        }

        // ---------------------------------------------------------------------------------------- the table

        private static readonly OpSpec[] s_Specs = BuildSpecs();

        private static readonly Dictionary<int, OpSpec> s_ByOpcode = BuildByOpcode(s_Specs);

        private static OpSpec[] BuildSpecs()
        {
            return new[]
            {
                // --- W2 ---------------------------------------------------------------------------------------
                new OpSpec("TEXT", "NowUI.NowLayout.Label(System.String)", 0,
                    new[] { ArgKind.Str }),

                // --- W3 ---------------------------------------------------------------------------------------
                new OpSpec("COLUMN", "NowUI.NowLayout.Column(NowUI.NowId)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg }, opensScope: true),
                new OpSpec("ROW", "NowUI.NowLayout.Row(NowUI.NowId)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg }, opensScope: true),
                new OpSpec("LIST_ITEM", "NowUI.NowControls.KeyedItemIn(NowUI.NowId,NowUI.NowId)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Seg }, opensScope: true),
                new OpSpec("BUTTON", "NowUI.NowLayout.Button(System.String,NowUI.NowId)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Str }),

                // --- W4 ---------------------------------------------------------------------------------------
                // The sixth op, and the one W5 is written against. It is a VALUE control: it carries the caller's
                // string out and brings the post-draw string back in the result table (section 6.4), which is the
                // only op in this build that exercises the whole of section 6 rather than a single flag bit.
                //
                // The signature hashed here is the CONSUMER, Draw(ref string), not the factory. Every other entry
                // names its factory because the factory is what would drift; here the factory is
                // TextField(NowId) - a plain id passthrough - and the member whose shape the bridge actually
                // depends on is the ref-string overload. If Draw(ref string) became Draw(in string) the factory
                // signature would not move an inch and the hash would not notice, so the hash names the thing
                // that matters. Gate G1 catches it as a compile error either way; the hash is the second line.
                new OpSpec("TEXT_FIELD", "NowUI.NowTextField.Draw(System.String&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Str, ArgKind.Str }),

                // --- W6 ---------------------------------------------------------------------------------------
                //
                // THE MODIFIER OP. Section 2.7's options object arrives as its own op, immediately before the op
                // it modifies, rather than as a trailing argument on every op. abi.js carries the long form of
                // why; the short form is that widening the six ops above would rewrite W2 to W5's acceptance
                // evidence - run.mjs asserts "TEXT carries one slot" - to add an argument that is empty in every
                // frame those four units recorded. W8's generator emits the trailing-argument form from
                // surface.json and regenerates those expectations with it.
                new OpSpec("OPTS", "NowUI.Bridge.PendingOptions(System.Int32)", 0,
                    new[] { ArgKind.Opts }),

                // Scopes (section 2.2).
                new OpSpec("CARD", "NowUI.NowLayout.Column(NowUI.NowId)+NowUI.Now.Rectangle(NowUI.NowRect)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg }, opensScope: true),
                new OpSpec("IDSCOPE", "NowUI.NowControls.IdScope(System.Int32)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg }, opensScope: true),
                new OpSpec("SCROLL", "NowUI.NowLayout.ScrollView(NowUI.NowId)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg }, opensScope: true),
                new OpSpec("FOLDOUT", "NowUI.NowFoldout.Draw(System.Boolean&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Bool, ArgKind.Str }, opensScope: true),

                // Drawings (section 2.3).
                new OpSpec("SPACE", "NowUI.NowLayout.Space(System.Single)", 0,
                    new[] { ArgKind.F32 }),
                new OpSpec("FLEX_SPACE", "NowUI.NowLayout.FlexibleSpace(System.Single)", 0,
                    new[] { ArgKind.F32 }),
                new OpSpec("RULE", "NowUI.NowLayout.Row()+NowUI.Now.Rectangle(NowUI.NowRect)", 0,
                    new ArgKind[0]),
                new OpSpec("BADGE", "NowUI.NowLayout.Badge(System.String)", 0,
                    new[] { ArgKind.Str }),

                // Actions (section 2.4).
                new OpSpec("SELECTABLE", "NowUI.NowSelectableRow.Draw()", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Bool, ArgKind.Str }),
                new OpSpec("CHIP", "NowUI.NowChip.Draw(System.Boolean&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Str, ArgKind.Bool, ArgKind.Bool }),

                // Values (section 2.5).
                new OpSpec("TEXT_AREA", "NowUI.NowTextArea.Draw(System.String&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Str, ArgKind.Str }),
                new OpSpec("NUMBER_FIELD", "NowUI.NowTextField.Draw(System.Single&,System.String)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.F32, ArgKind.Str }),
                new OpSpec("CHECKBOX", "NowUI.NowCheckbox.Draw(System.Boolean&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Bool, ArgKind.Str }),
                new OpSpec("SWITCH", "NowUI.NowSwitch.Draw(System.Boolean&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Bool, ArgKind.Str }),
                new OpSpec("RADIO_ITEM", "NowUI.NowRadio.Draw()", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Bool, ArgKind.Str }),
                new OpSpec("SLIDER", "NowUI.NowSlider.Draw(System.Single&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.F32, ArgKind.F32, ArgKind.F32 }),
                new OpSpec("INT_SLIDER", "NowUI.NowSlider.Draw(System.Int32&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I32, ArgKind.F32, ArgKind.F32 }),
                new OpSpec("DROPDOWN", "NowUI.NowDropdown.Draw(System.Int32&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I32, ArgKind.Strlist }),
                new OpSpec("COMBO", "NowUI.NowComboBox.Draw(System.Int32&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I32, ArgKind.Strlist }),
                new OpSpec("COLOR_FIELD", "NowUI.NowColorPicker.Draw(UnityEngine.Color&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.Color }),
                new OpSpec("DATE_PICKER", "NowUI.NowDatePicker.Draw(System.DateTime&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I64 }),
                new OpSpec("TIME_PICKER", "NowUI.NowTimePicker.Draw(System.TimeSpan&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I32 }),
                new OpSpec("TABS", "NowUI.NowTabBar.Draw(System.Int32&)", 0,
                    new[] { ArgKind.Rid, ArgKind.Seg, ArgKind.I32, ArgKind.Strlist }),

                // Feedback (section 2.6).
                new OpSpec("PROGRESS", "NowUI.NowProgressBar.Draw()", 0,
                    new[] { ArgKind.F32 }),
            };
        }

        private static Dictionary<int, OpSpec> BuildByOpcode(OpSpec[] specs)
        {
            var map = new Dictionary<int, OpSpec>(specs.Length);

            foreach (OpSpec spec in specs)
            {
                OpSpec clash;
                if (map.TryGetValue(spec.Opcode, out clash))
                {
                    // Detected at type initialisation, exactly as section 5.2 requires, rather than discovered as
                    // decoded garbage on a live frame.
                    throw new InvalidOperationException(
                        "NowUI ABI: opcode collision at " + spec.Opcode + " between \"" + clash.Signature +
                        "\" and \"" + spec.Signature + "\". Resolve it by adding a salt to one of the two entries " +
                        "in Abi.cs and the identical salt in abi.js.");
                }

                map.Add(spec.Opcode, spec);
            }

            return map;
        }

        /// <summary>Every op, in declaration order.</summary>
        public static IReadOnlyList<OpSpec> Specs => s_Specs;

        /// <summary>The op an opcode names, or null for one this build does not know.</summary>
        public static OpSpec Find(int opcode)
        {
            OpSpec spec;
            return s_ByOpcode.TryGetValue(opcode, out spec) ? spec : null;
        }

        /// <summary>The op with this name. Throws rather than returning null: a name is a compile-time constant.</summary>
        public static OpSpec Op(string name)
        {
            foreach (OpSpec spec in s_Specs)
            {
                if (spec.Name == name) return spec;
            }

            throw new ArgumentOutOfRangeException(nameof(name), "No op named '" + name + "'.");
        }

        /// <summary>
        /// The manifest hash of gate G3: one line per op, sorted by name so the order the entries happen to be
        /// written in cannot change it, folded with FNV-1a. abi.js builds the same string from the same list.
        /// </summary>
        public static int SurfaceHash { get; } = ComputeSurfaceHash();

        private static int ComputeSurfaceHash()
        {
            var names = new List<string>(s_Specs.Length);
            foreach (OpSpec spec in s_Specs) names.Add(spec.Name);
            names.Sort(StringComparer.Ordinal);

            var text = new StringBuilder();
            for (int i = 0; i < names.Count; ++i)
            {
                OpSpec spec = Op(names[i]);
                if (i > 0) text.Append('\n');
                text.Append(spec.Name).Append('=').Append(spec.Opcode).Append(':').Append(spec.Signature).Append(':');

                for (int a = 0; a < spec.Args.Length; ++a)
                {
                    if (a > 0) text.Append(',');
                    text.Append(KindName(spec.Args[a]));
                }
            }

            return unchecked((int)Fnv1a32(text.ToString()));
        }

        /// <summary>The lower-case kind names abi.js uses, so both halves hash the same characters.</summary>
        internal static string KindName(ArgKind kind)
        {
            switch (kind)
            {
                case ArgKind.I32: return "i32";
                case ArgKind.F32: return "f32";
                case ArgKind.Bool: return "bool";
                case ArgKind.Enum: return "enum";
                case ArgKind.Str: return "str";
                case ArgKind.Key: return "key";
                case ArgKind.Seg: return "seg";
                case ArgKind.Rid: return "rid";
                case ArgKind.Color: return "color";
                case ArgKind.Vec2: return "vec2";
                case ArgKind.Vec4: return "vec4";
                case ArgKind.I64: return "i64";
                case ArgKind.Strlist: return "strlist";
                case ArgKind.Opts: return "opts";
                default: return kind.ToString().ToLowerInvariant();
            }
        }
    }
}
