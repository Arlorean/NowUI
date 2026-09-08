# M3 Design A — the faithful generated mirror

**Thesis, in one sentence.** Generate the whole JavaScript surface mechanically from `NowUI.Runtime` metadata so
that every name is the C# name, record a frame of calls into a shared word buffer, replay that buffer against the
real static API inside one `Now.StartUI`, and replace `[CallerFilePath]`/`[CallerLineNumber]` with **the position
of the call in the recorded stream** — a positional key under an explicit scope path, which is the same thing the
Markup extension already does with `"markup:<tag>:<sourceIndex>"` (`M3-SurfaceScout.md` §6.2).

**Scope.** This document designs the JS surface, its transport, its identity model and its generator. It does not
design the packaging story (npm layout, CDN), the debugger, or hot reload beyond what `NowRuntime.ResetAll` already
gives. Paths are relative to `D:/wkspaces/unity/Now-UI/`. Every number in this document is either **measured**
(with the command in Appendix A) or explicitly labelled **estimated**.

**Facts this design is built on**, all from `Docs/Standalone/M3-SurfaceScout.md`:

* §2.4 — explicit identity is already first-class through four independent mechanisms. **No new identity API is
  needed in the frozen tree.** This is the single fact that makes the milestone tractable.
* §1.5 — the three attributes mark return types, not entry points, so the generator's source must be the public API
  itself, not the attributes. The attributes become *cross-checks*.
* §3.6.1/§3.6.2 — scope disposal order is enforced by exception, and a scope left open across `StartUI` throws.
* §4.5 — a previous-frame result table can serve the whole interaction bundle; it cannot serve
  `NowRichTextResult`, `NowMarkupResult`, or the text a field holds.
* §5.3d — `ref`/`out`, not delegates, is the pervasive obstacle.
* §2.7 — the layout system *already* falls back to positional identity under the parent group when no call site is
  available. The precedent for this design's identity model is inside NowUI.

---

## 0. The shape of the answer, before the details

Six decisions define this design. Everything else follows.

| # | Decision | Because |
|---|---|---|
| **D1** | The surface is **generated from assembly metadata**, 1:1 with C# names, camelCased. 2,890 JS functions over 3,187 opcodes (measured). | The brief's angle, and the only way the surface cannot drift. |
| **D2** | Identity is the **positional path**: a per-scope, per-kind ordinal under an explicit id-scope stack the bridge pushes for every scope it opens. `.setId()` and `keyed()` override it. | JS has no call site. The positional path is the JS analogue of a call site, and NowUI already accepts one (§2.7). |
| **D3** | `ref`/`out` parameters become **bindings**: a wasm-side slot the replay passes by reference, fed from and written back to a JavaScript object the app owns. | Solves §5.3d and the "who owns the text field's string" question at once. |
| **D4** | Scopes are **callbacks**, and the buffer is **validated for balance before it is replayed**. NowUI never sees an unbalanced frame. | §3.6 says imbalance throws. Record-then-replay lets the bridge guarantee balance instead of hoping for it. |
| **D5** | The transport is **one growable word buffer** shared with wasm through `[JSMarshalAs<JSType.MemoryView>]`, the same mechanism `WebGL2Backend` already uses (`WebGL2Backend.cs:1971-2031`). **One boundary crossing per frame** in steady state. | Anything per-call is a per-control crossing, and a frame has hundreds of calls. |
| **D6** | Delegate parameters become **recorded sub-spans** of the same buffer, replayed as a C# `Action` that re-enters the decoder. | Recovers deferred overlays, exact two-pass layout, docking windows and node-graph content (§5.3a) without a JS re-entry. |

---

## 1. The API, shown

### 1.1 A complete application

A task list: a text field, a slider, a scrolling list of rows that can be reordered and deleted, and a button that
adds one. This is the whole file; nothing is elided.

```js
// app.js
import { NowLayout, NowLayoutAlign } from './nowui/nowui.js';
import { mount, bind, keyed } from './nowui/runtime.js';

// ---- application state, owned entirely by JavaScript -------------------------------
const draft = { title: '', priority: 0.5 };

let tasks = [
  { key: 'a', title: 'Port the SDF shader', done: true,  weight: 0.9 },
  { key: 'b', title: 'Write the JS mirror', done: false, weight: 0.6 },
];
let nextKey = 3;

// ---- the frame --------------------------------------------------------------------
mount('#nowui-canvas', () => {
  NowLayout.verticalScope({ spacing: 8, padding: 16 }, () => {

    NowLayout.label('Tasks', 20).draw();

    // --- add form --------------------------------------------------------
    NowLayout.horizontalScope({ spacing: 8, alignItems: NowLayoutAlign.center }, () => {
      const field = NowLayout.textField()
        .setPlaceholder('New task...')
        .setStretchWidth(1)
        .draw(bind(draft, 'title'));

      const add = NowLayout.button('Add').setWidth(72).draw();

      if (add || field.submitted) {
        const title = draft.title.trim();
        if (title) {
          tasks.push({ key: 'k' + nextKey++, title, done: false, weight: draft.priority });
          draft.title = '';
        }
      }
    });

    NowLayout.horizontalScope({ spacing: 8, alignItems: NowLayoutAlign.center }, () => {
      NowLayout.label('Priority').setWidth(64).draw();
      NowLayout.slider(0, 1).setStep(0.05).setStretchWidth(1).draw(bind(draft, 'priority'));
      NowLayout.label(draft.priority.toFixed(2)).setWidth(40).draw();
    });

    // --- the list --------------------------------------------------------
    NowLayout.scrollView().setHeight(220).setStretchWidth(1).begin(() => {
      NowLayout.verticalScope({ spacing: 4 }, () => {

        for (const task of tasks) {
          keyed('tasks', task.key, () => {
            NowLayout.horizontalScope({ spacing: 8, alignItems: NowLayoutAlign.center }, () => {

              NowLayout.checkbox().setWidth(20).draw(bind(task, 'done'));

              NowLayout.label(task.title)
                .setStretchWidth(1)
                .setColor(task.done ? [0.5, 0.5, 0.5, 1] : [0.9, 0.9, 0.9, 1])
                .draw();

              NowLayout.slider(0, 1).setWidth(90).draw(bind(task, 'weight'));

              if (NowLayout.button('X').setId('remove').setWidth(24).draw()) {
                tasks = tasks.filter(t => t !== task);
              }

              if (NowLayout.button('^').setId('up').setWidth(24).draw()) {
                const i = tasks.indexOf(task);
                if (i > 0) [tasks[i - 1], tasks[i]] = [tasks[i], tasks[i - 1]];
              }
            });
          });
        }

        if (tasks.length === 0) {
          NowLayout.label('Nothing to do.', 13).draw();
        }
      });
    });

    NowLayout.space(8);

    NowLayout.horizontalScope({ spacing: 8 }, () => {
      const done = tasks.filter(t => t.done).length;
      NowLayout.label(`${done} of ${tasks.length} done`, 12).setStretchWidth(1).draw();

      if (NowLayout.button('Clear done').draw()) {
        tasks = tasks.filter(t => !t.done);
      }
    });
  });
});
```

That is the design. What follows justifies each construct in it.

### 1.2 Every construct, mapped to its C#

| JavaScript | C# it replays | Rule |
|---|---|---|
| `NowLayout.button('Add')` | `NowLayout.Button("Add", file, line)` | Static class and method names are identical; the method is camelCased. Caller-info parameters are never present in JS. |
| `.setWidth(72)` | `.SetWidth(72f)` | Every one of the 707 public builder methods (measured) is present, camelCased. |
| `.draw()` → `boolean` | `bool Draw()` | A C# primitive return becomes the JS primitive. `if (…draw())` reads identically. |
| `.draw(bind(draft,'title'))` → `NowTextFieldResult` | `NowTextFieldResult Draw(ref string)` | `ref T` becomes a binding (§1.3.1). A C# result struct becomes a frozen JS object with the same field names. |
| `NowLayout.verticalScope(opts, fn)` | `using (NowLayout.VerticalScope(options)) { … }` | Any method whose return type carries `[NowScope]` gains a trailing callback (§3). |
| `NowLayout.scrollView().begin(fn)` | `using (…ScrollView().Begin()) { … }` | Same rule; `Begin()` is a consumer that returns a scope, so it takes the callback. |
| `{ spacing: 8, padding: 16 }` | `NowLayoutOptions` built by `SetSpacing`/`SetPadding` | `NowLayoutOptions` has no public constructor — only `Set*` methods over a private `Field` bitmask (`NowLayout.cs:30-117`). An object literal is encoded as a field mask plus values and rebuilt by calling those setters (§4.4). |
| `NowLayoutAlign.center` | `NowLayoutAlign.Center` | All 54 enums (measured, including extensions) become frozen JS objects with the C# member names camelCased; the PascalCase spelling is kept as an alias. |
| `[0.5,0.5,0.5,1]` | `UnityEngine.Color` | Small blittable structs accept an array *or* an object literal (`{r,g,b,a}`). |
| `.setId('remove')` | `.SetId(new NowId("remove"))` | Present on the 39 builders that have it, absent on the 16 that do not (scout §2.6). The generator does not invent it. |
| `keyed('tasks', task.key, fn)` | `using (NowControls.KeyedItemIn("tasks", key)) { … }` | The call-site-free keyed-item scope (`NowControls.cs:260`). Hand-written sugar over the generated `NowControls.keyedItemIn`. |
| `bind(obj, 'prop')` | there is no C# equivalent | One of five hand-written primitives (§1.3). |
| `mount(sel, fn)` | `NowRuntime.BeginFrame(); using (Now.StartUI(dpr)) fn(); NowRuntime.EndFrame();` | The frame boundary is owned by the bridge and is not exposed. `Now.StartUI` cannot nest (`Now.cs:1273-1277`), so it must not be callable. |

### 1.3 The five things that are **not** generated

Everything else in the surface is mechanical. These five are hand-written, in `nowui/runtime.js` (~600 lines,
estimated), and are the whole of the design's invention.

**1.3.1 `bind(object, property)` — the answer to `ref`.**

291 `ref`/`out` parameter positions exist on the public surface (measured). A command buffer cannot carry an
aliased caller variable, and JavaScript has no `ref`. A binding is a wasm-side slot with a declared primitive type:

```js
const b = bind(draft, 'title');   // -> Binding { slot: 7, type: 'str' }
```

Per frame, for each binding that a recorded call touches:

1. at record time the bridge emits `OP_SET_BINDING slot value` (2–3 words) carrying `object[property]`;
2. at replay the decoder calls `Draw(ref store[slot])`, so NowUI edits the slot in place, exactly as it edits a C#
   local;
3. after the replay, the bridge writes every touched slot back into `object[property]`.

The consequences, stated plainly:

* **JavaScript owns the value.** There is one source of truth — the JS object — and NowUI holds a per-frame copy.
  Assigning `draft.title = ''` in JS takes effect on the next replay, which is what the example's Add button relies
  on.
* **Reading is a plain property read**: `draft.priority` is `draft.priority`. It reflects the *previous* replay,
  which is the same one-frame rule as everything else (§5), but the author never reaches through a bridge object
  to get it.
* `bind` memoises on a `WeakMap` keyed by the object, so calling it every frame in a loop allocates nothing after
  the first frame, and a binding for a task that is deleted is collected with the task. Slots are recycled after
  600 untouched frames (~10 s at 60 Hz), matching `NowControlState.EVICT_AFTER_SECONDS = 10f`
  (`NowControlState.cs:35`).
* Types are fixed at creation from the current value (`number`→`f32` unless the chosen overload wants `i32`,
  `string`→`str`, `boolean`→`bool`). A binding whose JS value changes type throws at record time rather than
  silently truncating.
* **`out` parameters** (103 positions) become extra fields on the returned result object rather than bindings —
  `NowChip.Draw(out bool removed)` becomes `{ clicked, removed }`. The generator knows the difference from
  `ParameterInfo.IsOut`.

**1.3.2 `keyed(listId, key, fn)`** — `NowControls.KeyedItemIn`, the only identity scope in the library that needs
no call site (`NowControls.cs:260-270`). Both arguments are required and non-empty; the C# throws otherwise, and
the JS wrapper throws the same message at record time so the stack points at the author's loop.

**1.3.3 `mount(selector, draw)`** — owns `requestAnimationFrame`, the buffer, `NowRuntime.BeginFrame`,
`Now.StartUI`, the replay, `NowRuntime.EndFrame`, and the root id scope. It also owns error latching: the existing
host already latches a throwing frame (`Program.cs:333-340`), and `mount` does the same for a throwing *recording*
(§3.4).

**1.3.4 `reset()`** — `NowRuntime.ResetAll()` plus a full clear of the binding store, result slots, string intern
table and scope shape memory. This is hot reload: re-import the module, call `reset()`, keep the canvas and the GL
context.

**1.3.5 The result object protocol** — the small class behind every returned struct: a slot index, a frame stamp,
and lazily-computed getters (§5).

### 1.4 What the example would look like in C#, for comparison

```csharp
using (NowLayout.VerticalScope(spacing: 8, padding: 16))
{
    NowLayout.Label("Tasks", 20).Draw();

    using (NowLayout.HorizontalScope(spacing: 8, alignItems: NowLayoutAlign.Center))
    {
        var field = NowLayout.TextField().SetPlaceholder("New task...").SetStretchWidth(1f).Draw(ref draftTitle);
        bool add = NowLayout.Button("Add").SetWidth(72f).Draw();
        …
    }
    …
}
```

Line for line, with `using` becoming a callback, `ref x` becoming `bind(o,'x')`, and `PascalCase` becoming
`camelCase`. That correspondence *is* the design goal, and it is what makes the mechanical generator worth its
costs.

---

## 2. Identity

### 2.1 What replaces the call site

C# derives control identity from the pair `(interned file path, line number)`, captured at the factory and interned
into a dense int (`NowControls.cs:420-455`), and salts repeats by draw-order occurrence
(`NowControls.cs:563-580`). The essential property is not "file and line"; it is **a stable location in the
authored artifact**. JavaScript's authored artifact, from the bridge's point of view, is the recorded command
stream. So:

> **The identity of an unkeyed control is its position in the recorded stream: the ordinal of that control among
> controls of the same kind within the innermost open scope.**

Written as a segment: `"btn#0"`, `"btn#1"`, `"txt#0"`. The segment is a `NowId` string. It is *not* hashed in JS
and it is *not* a global path — ancestry comes from NowUI itself, because the bridge pushes a real id scope for
every scope the author opens.

### 2.2 The mechanism, in the exact public API it uses

Every JS scope opens **two** wasm-side things and closes them inner-first:

```csharp
// on beginScope(segment, layoutOptions)
var layout = NowLayout.VerticalScope(new NowId(segment), options);   // layout identity, NowIdDomain.Layout
var ids    = NowControls.IdScope(new NowId(segment));                // control identity, NowIdDomain.Scope
```

and every control resolves under the ambient id scope:

```csharp
// on a control op
NowLayout.Button(label).SetId(new NowId(segment)).Draw();
```

Why two: layout identity is derived from the *layout* parent (`NowLayout.cs:2481-2498`), and control identity is
derived from `NowControls.CurrentIdentityParent()`, the id-scope stack (`NowControls.cs:110-113`). They are
independent stacks. Pushing only the layout scope would leave every control in the frame in one flat namespace, and
two `"btn#0"`s in two different rows would collide — a silent state-share (`NowControls.cs:457-462`). Pushing both
makes the control namespace mirror the visual nesting, which is what an author assumes.

Root: `mount` opens one `NowControls.IdScope("nowui.js")` for the whole frame, so a JS UI can never collide with a
C# UI drawn on the same surface in the same frame.

Two traps in the scout's list are avoided by construction:

* §7.5 — `IdScope(NowId)` with a *default* id silently pushes nothing (`NowControls.cs:213-214`). The bridge always
  has a segment, and the JS wrapper for `NowControls.idScope` rejects an empty argument at record time.
* §7.6 — `IdScope(NowResolvedId)` *replaces* the ambient path rather than nesting (`NowControls.cs:126-138`). The
  bridge never uses that overload for scope nesting. It remains reachable from the generated surface (it is public
  API) with a `@replacesAmbientPath` doc tag.

### 2.3 Why the ordinal is per-kind and per-scope, not global

Three candidate schemes, and why this one:

| Scheme | Insert a control above | Change a control's kind | Cost |
|---|---|---|---|
| Let the bridge's own `[CallerFilePath]` fire | **every control in the frame shifts** — one call site, flat occurrence salting (`NowControls.cs:563-580`) | same | zero |
| Ordinal within scope | later siblings in that scope shift | later siblings shift | one counter per scope |
| **Per-kind ordinal within scope** | only later siblings *of the same kind* shift | nothing shifts | one small counter map per scope |

The first row is the trap a naive bridge falls into, and it is worth naming loudly: **letting the factories'
caller-info defaults fire from bridge code is not "no identity", it is the worst possible identity** — all 200
controls in a frame share one site token and are distinguished only by flat, frame-global draw order, so a
conditional anywhere renumbers everything after it.

### 2.4 What breaks, exactly

**A conditional adds or removes a control.** Inside one scope:

```js
if (showAdvanced) NowLayout.button('Advanced').draw();   // btn#0 when shown
NowLayout.button('Save').draw();                          // btn#0 when hidden, btn#1 when shown
```

Toggling `showAdvanced` makes Save inherit Advanced's identity, or lose its own. For a `Button` the visible damage
is small (a hover/press latch and a focus ring). For a `TextField` it is a caret, a selection and an undo stack
landing in the wrong control; for a `ScrollView` it is a jumped scroll position. **Fix: `.setId('save')`.** The
rule to teach is one line: *if a control can appear conditionally, give it or its siblings an id.*

**A list reorders.** Without `keyed()`, row 0's identity stays with position 0, so editing state follows the
position and not the datum. With `keyed('tasks', task.key, …)` identity follows the key, which is the documented
purpose of the API (`Identity.md:101-102`). The example uses it.

**The same control in a loop.** Stable, and strictly better than C#: NowUI's own fallback salts occurrences
*flat per frame* (`NowControls.cs:307-311`), so in C# a loop's controls renumber if anything earlier in the frame
changes count. The bridge's counters are per scope, so a loop is only disturbed by changes inside its own scope.

**Two controls with the same explicit id at the same level.** `.setId('row')` twice in one scope is a genuine
collision: two controls share state, and in Release the duplicate-id warning is compiled out
(`NowControls.cs:606-625`, `#if UNITY_EDITOR || DEVELOPMENT_BUILD`). The bridge does not rely on that warning
(§2.5a).

### 2.5 How the author is warned

Three checks, all in the bridge, all on by default, all costing a few instructions per control.

**(a) Duplicate segment, same scope — hard error.** The bridge already maintains a per-scope counter map; a segment
supplied by `.setId()` is inserted into the same map. A second insertion of the same segment throws at *record*
time, with the JS stack pointing at the offending call. This is the check NowUI itself only performs in development
builds, done unconditionally and one frame earlier. It costs one map probe per keyed control.

**(b) Shape drift — one-shot warning.** Each scope accumulates a 32-bit FNV hash over the sequence of kinds it
emitted. At `endScope` the hash is compared with the same scope path's hash from the previous frame. A difference,
in a scope containing at least one *stateful* control, produces one console warning per scope path:

```
NowUI: the shape of scope "nowui.js/vert#0/scroll#0/vert#0" changed between frames.
  was: [chk, lbl, sld, btn, btn]
  now: [chk, lbl, sld, btn]
  Controls after the change may have swapped identity and state.
  Give the affected controls .setId(...), or wrap repeated items in keyed(listId, key, ...).
```

"Stateful" is not a judgement call: the generator marks a builder stateful iff it exposes `SetId` — 39 of the 55
builders do, and the other 16 are the id-free draw primitives (scout §2.6). Cost: one multiply-xor per emitted
control, one map lookup per scope, per frame.

**(c) Reorder — heuristic warning.** Shape drift does **not** detect a reorder: the kind sequence is identical. The
bridge additionally hashes the label string handle at each position. If the *multiset* of labels in a scope is
unchanged but the *sequence* differs, it warns once:

```
NowUI: scope "…/vert#0" contains the same labels in a different order.
  Unkeyed controls take identity from position, so editing state stayed with the position.
  Wrap the items in keyed(listId, key, ...).
```

State this heuristic's limit honestly: **it is blind when the items carry no label, or when labels repeat.** A list
of unlabelled sliders that reorders is not detectable by any means available to the bridge, and the only defence is
`keyed()`. That is a real hole and it is why `keyed()` appears in the example rather than in an appendix.

**What none of these catch.** Identity that is *correct but wrong* — the author gives two logically different
controls the same explicit id in two code paths that are never both live in one frame. The bridge sees one id per
frame and has nothing to compare.

### 2.6 Cost of identity

Per control: one segment string (interned once, then a small integer), one counter increment, one hash update, one
`SetId` opcode word. Per scope: two extra wasm-side pushes and pops, one map lookup. On the wasm side the segment
becomes a `NowId` string and `NowIdHash` hashes it once per frame per control — the same work C# does for any
`SetId("save")` call. No allocation after warm-up: segments come from a per-scope array of pre-interned `"kind#n"`
strings for n < 64, built on demand beyond that.

**One hazard to record.** `NowControls.SiteToken`'s ordinal table `_callSiteTokens` and its `_callSites` record map
grow **without eviction** (`NowControls.cs:369-378`, `:440-450`); only the 4096-entry reference front cache is
cleared. Any bridge that minted a synthetic call site per *item* — rather than per *program location* — would leak
an entry per item forever. This design mints no call sites at all, and that is one reason it uses `SetId` and id
scopes rather than `NowControls.SiteId(file, line)`.

---

## 3. Scopes without `using`

### 3.1 The primary form is a callback

```js
NowLayout.verticalScope({ spacing: 8 }, () => { … });
NowLayout.scrollView().setHeight(220).begin(() => { … });
```

Mechanical rule, derived from metadata: **a method whose return type carries `[NowScope]` gains a trailing
`(scope) => void` parameter.** The generator finds those types from the attribute — this is one of the two places
where the attributes are load-bearing for generation — and it covers 20 public `[NowScope]` structs plus the 59
static factories that return one (measured).

Scopes that carry results give the callback an argument, because scout §3.6.3 is right that a value flows out of
the opening call:

```js
NowLayout.button('Menu').begin(s => {
  // s.clicked, s.focused, s.rect, s.interaction — NowControlScope's public fields
  NowLayout.label('…').draw();
});
```

The two public disposable structs that carry **no** `[NowScope]` (`Now.TextContextScope`,
`NowNodeGraphEvaluator<T>.BatchScope`, scout §3.5) are added by an explicit checked-in list in the generator, not
discovered — and that list is asserted against `IDisposable` at generation time, so a third one appearing in the
frozen tree fails the build rather than silently generating a scope with no end.

### 3.2 The explicit form, and why it exists

```js
const s = NowLayout.verticalScope.begin({ spacing: 8 });
…
s.end();
```

Needed when the body is spread over functions the author does not want to restructure. The bridge tracks a
record-time depth stack; `s.end()` on a scope that is not on top throws immediately, naming both scopes. This is
the record-time equivalent of `NowScopeGuard`'s "scopes must be disposed in reverse order"
(`NowScopeGuard.cs:54-61`), moved one layer out so the author's stack trace points at the mistake.

### 3.3 Enforcement: the buffer is validated before it is replayed

This is the structural advantage of record-then-replay and it deserves to be stated as a guarantee:

> **NowUI never sees an unbalanced frame from this bridge.** The recorded buffer is a complete artifact before any
> of it executes, so the bridge validates balance, then replays.

Concretely, `mount`'s frame is:

```
1. reset the write cursor, push the root scope segment
2. call the author's draw()            <- may throw
3. close every scope still open in the RECORD (append the missing end opcodes)
4. validate: depth returns to 0, every builder was consumed, every opcode's argWords is consistent
5. if valid: BeginFrame; StartUI; replay; EndFrame
6. write results; write bindings back
```

Consequences of steps 3–4:

* **Scope leaks across `StartUI` cannot happen.** Scout §7.8 — a layout/theme/id/input/GUI/drawlist scope left open
  makes the next `Now.StartUI` **throw** (`Now.cs:1279-1293`). Step 3 makes that unreachable.
* **Out-of-order disposal cannot happen.** The replay closes scopes in exact LIFO order because it is walking a
  balanced stream, so `NowScopeGuard`'s exception path is unreachable from the bridge.
* **The `NOWUI001` analyzer diagnostic gets a runtime twin.** Step 4 catches a builder that was started and never
  consumed and reports it once per kind per session:
  `NowUI: a NowButton was created but never .draw()n — it renders nothing.` That is the analyzer's message
  (`NowBuilderDiscardAnalyzer.cs:22`) delivered where a JS author will see it.

### 3.4 When JavaScript throws mid-frame

The recording is truncated where the throw happened; step 3 closes the open scopes; step 5 replays the partial
frame. The author sees the UI that had been built up to the failure, which localises the bug on screen, and the
console gets the error once — latched per error identity, because an exception in a draw loop repeats sixty times a
second and only the first is useful (the same reasoning as `Program.cs:333-340`).

Two alternatives considered and rejected:

* **Replay the previous frame's buffer.** The UI keeps working and hides the bug. Rejected: a UI that lies about
  its own state is worse than one that visibly stops halfway.
* **Skip the frame entirely.** The canvas keeps whatever the last frame drew — the same lie with an extra step, and
  it drops that frame's input handling.

Bindings are *not* written back on a throwing frame: the replay ran against a truncated stream and the slots are
half-updated. Stated as a rule: **a frame that throws does not commit its bindings.**

### 3.5 What is deliberately not exposed

`Now.StartUI` and its `NowUIScreenScope` are absent from the generated surface, because they cannot nest
(`Now.cs:1273-1277`) and `mount` owns them. Likewise `NowRuntime.BeginFrame`/`EndFrame`. The generator's exclusion
list names them with this reason, so the absence is a decision on record rather than an oversight (§6.5).

---

## 4. The command stream

### 4.1 Transport

One growable `byte[]` on the managed side, handed to JavaScript as a `Span<byte>` through
`[JSMarshalAs<JSType.MemoryView>]` — the exact mechanism `WebGL2Backend` already uses for its uniform and pixel
payloads (`WebGL2Backend.cs:1971-2031`) and `WebFetchProvider` for chunk reads (`WebFetchProvider.cs:584-588`). JS
holds three views over the same buffer:

```js
let CMD_I32, CMD_F32, CMD_U8;   // Int32Array, Float32Array, Uint8Array
```

There is no copy in either direction, and there is no `SharedArrayBuffer` requirement: the page is single-threaded,
so JS writes, then calls, then wasm reads.

### 4.2 Word format

Every command starts on a 4-byte word boundary.

```
word 0   header:  opcode:16 | argWords:8 | flags:8
word 1.. arguments, one word each unless noted
```

`argWords` is redundant for the decoder (each opcode knows its arity) and present for the validator and the
disassembler: a corrupt or version-skewed stream is detected at step 4 of §3.3 rather than crashing the decoder.
16 bits of opcode against 3,187 needed (measured) leaves room for a decade of API growth.

Argument encoding:

| C# parameter | Words | Encoding |
|---|---|---|
| `float` | 1 | IEEE-754 bits via `CMD_F32` |
| `int`, enum, `bool` | 1 | `CMD_I32` (`bool` as 0/1) |
| `string` | 1 or 2 | intern handle; top bit set ⇒ low 31 bits are a byte offset into the string arena and the next word is the UTF-8 length |
| `NowRect`, `Vector4`, `Color`, `NowCornerRadius` | 4 | inline floats |
| `Vector2` | 2 | inline floats |
| `NowId` | 2 | kind word (`none`/`str`/`int`) + payload (string handle or int) |
| `NowResolvedId` | 1 | index into a wasm-side handle table — `NowResolvedId` has no public value constructor (`NowResolvedId.cs:15`), so it can only ever be a handle |
| `NowLayoutOptions` | 1 + n | field bitmask word, then one word per set field (§4.4) |
| `ref T` / binding | 1 | binding slot index |
| managed reference type (`NowThemeAsset`, `NowFontAsset`, `NowDrawList`, …) | 1 | handle-table index |
| `Action` / `Action<T>` | 2 | sub-span (start word, end word) into this same buffer (§4.5) |

Rejected alternative: LEB128 / tagged variable-width encoding. It would shrink the buffer by roughly a third, but
the buffer is transient — it never leaves the process, is never transmitted, never persisted — so bytes are cheap
and decode branches are not. A fixed word stride lets the decoder compute every argument's address from the header
with no per-argument tag test.

### 4.3 String interning

JS keeps `Map<string, handle>`. On a miss it appends the UTF-8 bytes to the arena, records the handle, and emits
the arena form. The decoder materialises a `string` the first time it sees a handle and caches it in a `string[]`.
So:

* a **literal** label (`'Add'`, `'Priority'`) costs one word per use, forever, after the first frame;
* a **dynamic** string (`` `${done} of ${tasks.length} done` ``) is a fresh JS string each frame, misses the map,
  is written to the arena, and the decoder allocates one .NET string per frame for it. At 100 dynamic labels per
  frame that is 100 gen-0 allocations per frame — small, but it is the one per-frame allocation this design has,
  and it is named rather than hidden;
* the intern map is capped (32 k entries) and cleared wholesale on overflow, matching the pattern
  `NowControls.SiteToken` uses for its reference cache (`NowControls.cs:395`, `:434`). A cleared map costs one
  frame of re-interning.

### 4.4 `NowLayoutOptions`, concretely

`NowLayoutOptions` is a private-field struct with an internal `Field : ushort` bitmask and only `Set*` builders
(`NowLayout.cs:30-117`), so it cannot be memcpy'd from a JS object literal. The encoding is a mask plus values, and
the decoder replays the setters:

```
OP_LAYOUT_OPTIONS  mask:i32  [width][height][spacing][padding.x..w][alignItems]…
```

Fourteen settable fields (`width height minWidth maxWidth minHeight maxHeight stretchWidth stretchHeight spacing padding align alignItems grow justify`, `NowLayout.cs:81-108`); a typical `{ spacing: 8, padding: 16 }` is 1 + 1 + 1 + 4 = 7 words including the header.
The decoder is a fixed `if ((mask & W) != 0) o = o.SetWidth(F32[p++]);` chain — 14 predictable branches, no
allocation, and it produces exactly the struct a C# author would have produced.

### 4.5 Delegates as sub-spans

21 public `System.Action` parameter positions plus the typed forms (scout §5.3a) gate four capabilities the mirror
would otherwise lose: deferred overlays, `NowLayout.RunMeasured`, `NowDock.Window`, and node-graph node content.

At record time, `NowLayout.runMeasured(rect, () => { … })` writes a placeholder, invokes the JS callback so its
commands land contiguously in the buffer, then backpatches the placeholder with `(startWord, endWord)`. At replay
the decoder builds a real `Action` closing over that span and hands it to the C# method; invoking it re-enters the
decoder on that range. For `Action<NowRect>` and `Action<NowNode, NowRect>` the arguments are written into a small
callback-argument slot region that the sub-span reads with `OP_READ_CB_ARG`.

**The restriction this creates, stated now rather than discovered later:** the callback body is recorded *once,
before it runs*, so **JavaScript inside a callback cannot branch on the callback's own argument.** In

```js
NowDock.window('Inspector', rect => {
  if (rect.width > 300) twoColumns(); else oneColumn();   // <- cannot work
});
```

`rect` does not exist at record time. The bridge gives the JS callback the *previous frame's* value of the
argument, which is right often enough to be dangerous, so the generated wrapper marks such parameters and the
runtime warns once per call site when a callback's recorded shape differs from the previous frame's — reusing the
§2.5(b) machinery. Authors who need a true same-frame branch must restructure to read the rect from the result
table one frame late, which is the same rule as everything else in §5.

`RunMeasured` needs one extra guarantee. It runs the UI **twice per frame**, with identity rewound
(`NowControls.cs:563-580`, `:628-671`), so the replayed span must be idempotent. It is not, by default: a
`Draw(ref slot)` clamps and writes the slot during the measure pass. The bridge therefore **snapshots the binding
store before a measure pass and restores it after**, and suppresses result-slot writes during the measure pass.
This is scout fact #14 turned into two lines of the replay loop.

### 4.6 Buffer growth

Start at 64 KB of commands plus a 16 KB arena (estimated to cover a 500-control frame; see §4.7). When a write
would overflow, JS calls `Grow(bytes)`, the managed side reallocates and returns fresh `MemoryView` spans, and JS
rebuilds its three views. Growth is a boundary crossing, so the policy is doubling with no shrinking within a
session: after the first few frames a UI's peak is reached and growth stops permanently. A UI whose command count
varies wildly frame to frame pays growth once at its high-water mark, not repeatedly.

### 4.7 Boundary crossings and cost per frame

**Steady state: one crossing per frame.**

```
requestAnimationFrame
  └─ JS: draw()      -> writes N words into the shared buffer, zero crossings
  └─ JS: Submit(N)   -> ONE JSExport call
        └─ wasm: BeginFrame / StartUI / decode+replay / EndFrame / write results
  └─ JS: reads results and bindings straight out of shared memory, zero crossings
```

Plus `Grow` on the rare growth frame, and zero for string interning, because the arena lives in the same buffer.

Word cost of the example application's frame, counted by hand from the §4.2 encoding table (not from a running
build):

| Line | Words |
|---|---|
| `verticalScope({spacing:8,padding:16})` begin + id scope | 1 + 7 + 3 = 11 |
| `label('Tasks',20).draw()` | 3 + 2 + 1 = 6 |
| `horizontalScope(...)` begin + id scope | 11 |
| `textField().setPlaceholder(..).setStretchWidth(1).draw(bind)` | 3 + 2 + 2 + 3 + 2 (set binding) = 12 |
| `button('Add').setWidth(72).draw()` | 3 + 2 + 3 = 8 |
| one task row (`keyed` + hscope + chk + lbl + sld + 2 btn) | ~64 |
| two scope ends × 3 levels | 6 |

A 20-row list is roughly **1,500 words = 6 KB per frame**, written with `CMD_I32[p++] = …` — on the order of a few
microseconds of JS. A 500-control UI is ~35 KB and stays inside the initial 64 KB buffer.

**The alternative, for scale.** Without the buffer, each builder call is a `[JSExport]` crossing: the example's
frame is ~120 calls, a dense 500-control UI is ~2,000. At an **estimated** 0.2–1 µs per .NET-wasm interop crossing
— this number is not measured in this repository, and measuring it is task W1 in §8 — that is 0.4–2 ms per frame
of pure crossing overhead before any string marshalling, against a 16.7 ms budget. The buffer removes essentially
all of it. If W1 measures crossings at the fast end of that range, the buffer is still the right call for a
different reason: it is what makes §3.3's balance guarantee possible at all.

### 4.8 The decoder

One generated `switch (opcode)` over 3,187 cases. The measured IL cost of a dispatch case of this shape is **~29
bytes per case** (Appendix A.4), so 3,187 simple cases ≈ **93 KB of IL**; cases that decode structs or
`NowLayoutOptions` are 2–3× that, so budget **150–250 KB of extra IL, estimated**. Its real payload cost is not the
IL — it is trimming (§6.7).

Builders live on a replay-side stack, not in a handle table: a factory opcode pushes, a setter opcode mutates the
top, a consumer opcode pops. The JS wrapper enforces the same discipline at record time, so a chain that is
interrupted throws in JavaScript with the author's stack rather than misbehaving in wasm (§7.12). One wrinkle the
generator must handle: some consumers *return another builder* — `NowLabel.Draw()` returns `NowText`
(`NowLayout.cs:801-806`) — so a consumer's return is pushed but exempted from the "never consumed" check of §3.3.

---

## 5. The result table

### 5.1 Layout

A second shared buffer of fixed 16-word (64-byte) slots.

| Word | Contents |
|---|---|
| 0 | `frameStamp` — the frame number of the replay that wrote this slot |
| 1 | flags: `hovered pressed held released clicked active dragging dragStarted dragEnded cancelled dragCancelled focused submitted changed hasPointer present` |
| 2–5 | `rect.x/y/width/height` |
| 6–7 | `pointerPosition` |
| 8–9 | `pointerDelta` |
| 10–11 | `dragDelta` |
| 12 | `button` (`NowPointerButton`) |
| 13–14 | the resolved id, low/high — so JS can pass it back as a `NowResolvedId` handle |
| 15 | result-kind tag + reserved |

That is every field of `NowInteraction` (`NowInput.cs:15-51`), plus the `focused`/`submitted` outs that accompany
every `NowControls.Interact` overload (`NowControls.cs:701-893`), plus `changed`. 500 controls cost 32 KB.

Slots are allocated in JavaScript, keyed by the identity path the bridge is already computing, so a control keeps
its slot across frames for free. Slots unused for 600 frames are recycled — the same discipline as
`NowControlState`'s 10-second eviction (`NowControlState.cs:35`), which scout §4.5 says a result table must match
or it will serve stale `clicked` values for controls that stopped being drawn.

Result structs that are not per-control (`NowTextFieldResult`, `NowSplitViewResult`, `NowTabViewScope`) reuse the
same slot, with the kind tag in word 15 selecting which JS getters are exposed.

### 5.2 Reading is free; the value is one frame old

```js
if (NowLayout.button('Add').draw()) { … }
```

`draw()` returns `(flags & CLICKED) !== 0 && frameStamp === frame - 1`. No crossing, no allocation — the generated
wrapper for a `bool Draw()` returns a primitive, so this line is byte-for-byte the C# line's meaning, one frame
behind.

**The contract, stated once and put at the top of the generated `.d.ts`:**

> Every value NowUI returns to JavaScript describes the **previous** frame.

Three consequences, and how each is handled:

1. **Reaction latency is one frame (~16 ms).** The click is processed on the next frame. In an immediate-mode loop
   that redraws unconditionally this is invisible: the user's click at frame N is handled at N+1 and drawn at N+1.
   That is the whole of the cost for buttons, checkboxes, sliders, drags and focus.
2. **A value you set this frame reads back correctly**, because values live in JS objects, not in the result table
   (§1.3.1). `draft.title = ''` is visible to the very next line. This is why bindings are objects the app owns
   rather than reads through the bridge.
3. **Two members are already one frame late in C# and become two here.** Context-menu item clicks
   ("the frame after the click", `NowContextMenu.cs:346`) and dropdown/combobox selection
   ("applies on the next frame's Draw", `NowDropdown.cs:14-15`, `NowComboBox.cs:15`). The generator tags these from
   a checked-in list into the `.d.ts` as `@latency 2 frames`, and the runtime does nothing else about them —
   there is nothing it can do.

### 5.3 Keeping the author from being surprised

**Read-before-first-draw.** A control drawn for the first time has no slot from last frame. Its results read
`false` and its rect reads zeros, and the *first* such read for a given identity emits one warning:

```
NowUI: read a result for "…/vert#0/btn#1" before it had ever been drawn.
  Results describe the previous frame; the first frame of a control always reads false.
```

This is the mirror image of `NowMarkupResult`'s staleness guard, which throws when a result is read *after* the
document redraws (`NowMarkupState.cs:57-70`). Scout §6.5 asks for exactly this inversion.

**Stale-slot policy.** Booleans from a slot whose `frameStamp` is older than `frame - 1` read hard `false` — a
`clicked` must never fire twice. `rect` is served from the last frame the control was drawn, with `rectAge`
available, because geometry decays gracefully and a caller positioning a popup relative to a control that blinked
out for one frame is better served by a stale rect than by zeros.

**Discarded results.** JavaScript has no implicit `bool` conversion for objects, so

```js
if (field.draw(bind(draft,'title'))) { … }     // ALWAYS TRUE — NowTextFieldResult is an object
```

is a real hazard that C#'s `implicit operator bool` (`NowTextField.cs:356-359`) hides. It cannot be fixed:
`if (obj)` consults neither `valueOf` nor `Symbol.toPrimitive`. The mitigations are (a) the `.d.ts` types the
return as `NowTextFieldResult`, so TypeScript and every editor's inference flags it, and (b) in development the
runtime tracks whether each returned result object had *any* property read before the next frame and warns once if
not: `a NowTextFieldResult was returned and never read — did you mean .changed?`. That catches the exact mistake
at the cost of one boolean per result object per frame. **This is the single largest faithfulness gap in the
design and it is unfixable at the language level.**

### 5.4 What the result table cannot serve

Taken from scout §4.5 and not argued with:

* `NowRichTextResult.layout` is a `NowRichTextLayout` **object** and `TryHit` is a call into it
  (`NowRichText.cs:30,39-48`). Exposed as a wasm-side handle with explicit method calls, not as slot data — and
  those calls are recorded commands, so their answers are also one frame late.
* `NowMarkupResult` borrows the document's event buffer and throws when read after the document redraws
  (`NowMarkupState.cs:61-69`). The bridge copies the event list into the arena at the end of the replay instead of
  handing back a borrowed view.
* `NowSplitViewResult.BeginFirst()` is a method on the result, not data — it becomes a callback scope like any
  other `Begin`.
* **The text a field holds is not a result at all.** NowUI stores no caller text (scout §4.2); the caller owns it.
  In this design the caller is a JS object and `bind` is the plumbing. This is the cleanest correspondence in the
  whole design, and it exists because the C# API was already written that way.

---

## 6. How the surface is produced and kept honest

### 6.1 Generated, from metadata — not from the attributes, not from the dump

The generator is a console tool, `Tools/Standalone/JsGen`, structured like `Tools/Standalone/ApiDump` and using the
same loading strategy: `MetadataLoadContext` over the built `NowUI.Runtime.dll` plus the six extension assemblies,
with `NowUI.Engine.dll` on the resolver path so `UnityEngine` type names resolve. Metadata only, no execution.

Two source choices worth defending:

* **Not the `[NowBuilder]`/`[NowConsumer]`/`[NowScope]` attributes.** `StandaloneCoreDesign.md` §4.8 names them as
  "the reflection source for generating the JS surface". Taken literally that is insufficient, and scout §1.5 shows
  why: the attributes sit on return types, so a pass over them finds the 47 builder structs and misses all 157
  factories that produce them, all 38 static subsystems, and every result struct. `NowControlFactories.cs` — the
  file containing every control factory — carries no attribute at all. The attributes are used, but as
  **cross-checks** (§6.4).
* **Not `after-NowUI.Runtime.api.txt`.** The dump sorts every line ordinally across the whole assembly
  (`ApiDump/Program.cs:155`), which discards the declaring type: `method NowUI.NowButton Button(...)` and
  `method NowUI.NowButton SetWidth(...)` sit adjacent with nothing saying that one is a factory on `NowLayout` and
  the other an instance method on `NowButton`. The dump is a **gate**, not a source, and this design keeps it in
  that role (§6.5).

### 6.2 What it emits

| Artifact | Contents |
|---|---|
| `nowui/nowui.js` | the generated surface: 2,890 functions, 3,187 opcodes (**measured**, Appendix A.2) |
| `nowui/nowui.d.ts` | full type declarations, with the C# XML doc comments carried over |
| `nowui/core.js` | a curated re-export of ~120 members (§6.6) |
| `NowJsDispatch.g.cs` | the decoder `switch`, compiled into a new `NowUI.JsBridge` project |
| `nowui.surface.json` | the opcode manifest: `{opcode, declaringType, member, signature, hash}` — **checked in** |
| `nowui.unsupported.json` | every public member the generator refused, with the reason — **checked in** |

Naming rules, in full, so they are mechanical rather than tasteful:

1. Type names are unchanged (`NowLayout`, `NowButton`, `NowLayoutAlign`).
2. Method and property names are camelCased on the first character only (`SetStretchWidth` → `setStretchWidth`,
   `Draw` → `draw`). No re-spelling, no synonyms, no "nicer" names.
3. Enum members are camelCased (`NowLayoutAlign.Center` → `.center`); the PascalCase name is kept as an alias so
   code copied from C# examples still runs.
4. Caller-info parameters (`file`, `line`) are removed from every signature.
5. `ref`/`in` parameters become bindings; `out` parameters become extra result fields.
6. A trailing run of optional parameters becomes one optional options object; required parameters stay positional.
   `VerticalScope(spacing: 8, padding: 16)` is already how the C# reads, so `{spacing: 8, padding: 16}` is the
   faithful transcription, not a departure.
7. A method returning a `[NowScope]` type gains a trailing callback.
8. Overloads collapse onto one JS function that dispatches on arity and runtime kind (§6.3).

### 6.3 Overloads, generics, delegates — what actually happens

**Overloads.** 387 overload groups exist across Runtime and the six extensions (**measured**). Dispatching on
(arity range, JS runtime kind of each positional argument), where a binding carries its exact element type and a
struct literal is identified by its shape, leaves **43 groups genuinely ambiguous, covering 175 of 2,429 methods —
7%** (**measured**, Appendix A.3). The worst are `NowInput.Interact` (22 overloads), `NowControls.Interact` (14),
`NowLayout.Area` (12), `NowText.Draw` (8).

One caveat on that count, because it is the kind of thing that is discovered later if it is not stated now: the
measurement distinguishes structs by *type*, and JavaScript cannot always do that at runtime. `SetColor(Color)`
and `SetColor(Vector4)` are counted as distinguishable, but a JS `[0.5, 0.5, 0.5, 1]` matches both. In every such
pair in this library the two overloads are alternate spellings of the same RGBA quadruple and the choice is
immaterial (`NowLabel.SetColor`, `SetGradient`, `SetOutlineColor`), so the generated dispatcher picks the first in
the documented priority order and the `.d.ts` types the parameter as the union. Where a future overload pair made
the choice material, gate 2 of §6.5 would surface it as a manifest diff rather than as a runtime surprise.

For those 43 the generator emits the dispatcher *and* explicit disambiguated aliases named from the differing
parameter: `NowLayout.area(rect)` stays, and `NowLayout.area$id(id, rect)` exists for the case the dispatcher
cannot decide. Ambiguity is resolved by a documented priority (most specific parameter kinds first, then fewest
parameters), and the chosen order is written into `nowui.surface.json` so a change to it is a reviewed diff. The
ambiguous-group count is asserted in the generator's tests: if a new overload pushes a group from resolvable to
ambiguous, the build fails until an alias is added.

**Generic types.** `NowEnumDropdown<TEnum>` and `NowEnumFlags<TEnum>` are `where TEnum : struct, Enum`
(`NowValueControls.cs:181,241`). The generator instantiates them over every enum in the loaded assemblies — a
closed, mechanical set — so `NowLayout.enumDropdown(NowLayoutAlign, binding)` works. **A JavaScript-defined enum
is not a CLR enum and cannot be used**; the author uses `dropdown(labels)` with an int binding, and the `.d.ts`
says so on the member.

**Generic methods.** 30 exist (**measured**). `NowControlState.Get<T>` returns `ref T` over a caller-defined
struct — unrepresentable, excluded. `NowInspector.Draw<T>(ref T)` and `Draw(object)` reflect over a CLR object
graph — excluded. `NowViews.MessageBox<TOwner>` is instantiated over `object`. `NowLayout.RunMeasured<TState>` is
instantiated over `int` and the state is carried as a callback-argument slot.

**Delegates.** §4.5. All 21 `Action` positions and the typed forms are reachable, with the record-time branching
restriction named there. The settable static hooks (`NowClipboard.setText`, `NowTextInput.setImeEnabled`,
`Now.SetTextPreprocessor`) are **excluded**: they are host wiring, already owned by `WebHostServices` and
`WebInput`, and letting an app replace them would let a JS app break the page's clipboard and IME contracts.

### 6.4 The attributes as cross-checks

Three generator assertions, each of which fails the build:

1. Every type carrying `[NowBuilder]` must produce a JS class with at least one method the generator classified as
   terminal. A builder with no reachable terminal is a generation bug — and, because `[NowConsumer]` is decorative
   on 9 of 38 methods and absent from the single most common terminal (`bool NowButton.Draw()`, scout §1.3), the
   terminal classifier is *return-type-and-name* based, not attribute based. The attribute is the check on the
   classifier, not the classifier.
2. Every type carrying `[NowScope]` must appear in the callback-taking set, and every member of the
   callback-taking set must either carry `[NowScope]` on its return type or appear in the two-entry
   `KNOWN_UNMARKED_SCOPES` list (scout §3.5). A third unmarked public `IDisposable` struct appearing in the frozen
   tree fails the build.
3. Every method carrying `[NowConsumer]` must be classified terminal. This one catches the classifier drifting the
   other way.

### 6.5 What stops it drifting

Four gates, in order of how early they fire:

1. **The existing public-API delta gate.** `Tools/Standalone/Dump-PublicApi.ps1` already fails the build if the
   standalone surface diverges from Unity's without `Docs/Standalone/StandaloneApiDelta.md` being updated
   (`StandaloneCoreDesign.md:78`). The JS surface is generated from that same assembly, so it cannot drift from a
   surface that cannot itself drift silently.
2. **The checked-in opcode manifest.** CI regenerates and diffs `nowui.surface.json`. A changed manifest is a
   reviewed diff, and the review shows exactly which JS names appeared, vanished or changed shape.
3. **Opcodes are content-hashed, not ordinal.** An opcode is the low 16 bits of a hash of
   `declaringType + memberName + parameter type list`. Adding a method does not renumber existing opcodes, so a JS
   bundle and a wasm build produced from different commits still agree about every member both of them have.
   Collisions are detected at generation time and resolved by a checked-in salt.
4. **A surface handshake at boot.** The generated JS embeds the manifest's hash; the first `Submit` carries it; a
   mismatch refuses the frame with a message naming both hashes rather than replaying garbage. This is what makes
   gate 3 safe rather than merely convenient.

And the honest inverse: **a member excluded from generation is checked in too** (`nowui.unsupported.json`), each
with a machine-readable reason (`ref-generic`, `interior-reference`, `clr-object`, `host-hook`, `frame-boundary`,
`span`, `unity-render`, `mono-behaviour`, `editor-only`). The exclusion list is reviewed like any other artifact,
so "the generator quietly skipped it" is not a state this design can reach.

### 6.6 Discovery — the honest weak point

2,890 functions is not a surface a person or a model browses. Four answers, in decreasing strength:

1. **The 1:1 rule is itself the strongest discovery aid.** Anyone — or any model — that knows `NowLayout.Button` in
   C# knows `NowLayout.button` in JS, and the corpus for the C# API is the repository's own `Documentation~` and
   `Example/` trees. This is the argument for the faithful approach and it should be the argument that carries it.
2. **`nowui.d.ts` with the C# XML docs carried over.** Editor completion turns 2,890 into "the twelve methods on
   the builder I already have in my hand". The fluent shape helps here more than it usually does: after
   `NowLayout.button('x').` there are 12 members, not 2,890.
3. **`nowui/core.js`, a curated ~120-member re-export.** `import { NowLayout } from 'nowui/core'` gets the control
   library, the layout scopes and the enums they need, and nothing else; `nowui/full` gets everything. Faithful
   does not have to mean flat, and the default import being small is a design decision, not a compromise of the
   mirror — every excluded member is still present under `full`, under the same name.
4. **A generated `SURFACE.md` grouped by task** (draw a control / lay something out / query input / theme / text /
   extensions), and a runtime `NowUI.find('slider')` that greps the manifest and prints signatures. Both are
   generated from the same manifest, so neither can go stale.

None of this makes 2,890 functions small. The honest statement is: **this design trades discoverability for
fidelity and zero drift, and buys back most of the loss with a curated default import and a `.d.ts`.** A design
that instead hand-picked a small surface would win discovery and lose the guarantee that the JS API *is* the C#
API.

### 6.7 Payload — measured

| Item | Size |
|---|---|
| Generated `nowui.js`, raw | **184,181 bytes** |
| …gzip -9 | **29,340 bytes** |
| …brotli -q 11 | **23,376 bytes** |
| `nowui.d.ts` | not shipped to the browser |
| Decoder IL, 3,187 cases | ~93 KB simple; **150–250 KB estimated** with struct decoding |
| NowUI assemblies today, trimmed, brotli | **399,931 bytes** |
| NowUI assemblies with everything reachable, brotli | **579,958 bytes** |
| **Trim loss from a full-surface dispatcher** | **+180,027 bytes brotli** |
| Whole `_framework` payload today, brotli | **1,644,603 bytes** |

So the faithful mirror costs roughly **+23 KB of JavaScript and +180 KB of wasm, brotli — about +14% on the
current 1.6 MB payload** — and the JavaScript is the cheap part. The expensive part is that **a dispatcher naming
every public member defeats the trimmer**: `NowUI.Runtime.dll` goes from 590,848 to 938,496 bytes unlinked
(213,844 → 323,894 brotli), because `PublishTrimmed` currently removes 37% of it.

This is the design's largest and least avoidable cost, and it is inherent to "every builder is reachable". Two
mitigations, neither free:

* **Feature-sliced dispatchers.** Generate one dispatch partial per area (controls, layout, drawing, text, sdf,
  nodegraph, docking, markdown) and let an application's build select the ones it imports. This restores trimming
  for the unused areas at the cost of a build step — which contradicts "an AI writes an app and serves it" for the
  no-build case, so it must be opt-in and not the default.
* **Accept it.** 1.6 MB → 1.85 MB brotli for a UI library that draws everything in the feature matrix is a
  defensible number, and the default should be the one that works with no build step.

The recommendation is: **default to the whole surface and eat the 180 KB; ship the sliced generator as an
optimisation for anyone who publishes.**

---

## 7. What it cannot do

Stated plainly. Each is a decision made now rather than a bug found later.

1. **Every returned value is one frame old.** There is no mode in which it is not. Reaction latency is ~16 ms;
   context-menu clicks and dropdown selections are two frames because they are already one in C# (§5.2).
2. **A JavaScript callback cannot branch on its own argument** (`Action<NowRect>`, `Action<NowNode,NowRect>`),
   because its body is recorded before it is invoked (§4.5).
3. **A reorder of unlabelled items without `keyed()` is undetectable.** The shape hash sees no change and the label
   heuristic has nothing to compare. State follows position, silently (§2.5c).
4. **Two controls given the same explicit id collide.** The bridge throws for a duplicate within one scope, but it
   cannot see a collision the author creates across two code paths that are never both live in the same frame's
   map. In Release, NowUI's own duplicate warning is compiled out (`NowControls.cs:606-625`).
5. **`if (result)` on a result struct is always true**, where the C# `implicit operator bool` would have read
   `.changed`. Unfixable in JavaScript; mitigated by `.d.ts` types and a never-read warning (§5.3).
6. **`NowControlState.Get<T>` and every other `ref T` return is absent.** An interior reference into managed memory
   cannot cross a boundary at all (`NowControlState.cs:77`).
7. **`NowInspector` is absent.** `Draw<T>(ref T)` and `Draw(object)` reflect over a CLR object graph; a JS object
   is not one. An inspector for JS data would be a different feature with a different design.
8. **JavaScript-defined enums cannot drive `enumDropdown`/`enumFlags`.** Only the ~54 CLR enums in the loaded
   assemblies are instantiated (§6.3).
9. **Host wiring is not exposed**: `NowClipboard.setText`, `NowTextInput.setImeEnabled`, `Now.SetTextPreprocessor`,
   `NowLottieAsset.remoteUrlPolicy`, `NowMarkdownImages.remoteUrlPolicy`. The host owns them; a JS app that could
   replace them could break the page.
10. **Unity reference types in signatures are opaque handles.** `NowThemeAsset` (78 parameter positions),
    `NowFontAsset` (21), `Material`, `Gradient`, `AnimationCurve`, `Camera`, `CommandBuffer`,
    `RenderTargetIdentifier`, `NowInputSurface`. JS can pass a handle it was given and call the members the mirror
    generated on those types, but it cannot construct one from a literal unless the type has a public constructor
    the generator could mirror.
11. **`ReadOnlySpan<char>` overloads are absent** (24 positions). The `string` overload beside each is generated
    instead, which costs an allocation per dynamic string per frame (§4.3).
12. **A builder chain cannot be interrupted.** `const b = NowLayout.button('x'); other(); b.draw();` throws at
    record time, because builders live on a replay stack rather than in a handle table (§4.8). Chains must be
    written as chains — which is how the C# API is written anyway.
13. **`Now.StartUI` is not callable**; the bridge owns the frame (§3.5). Neither are `NowRuntime.BeginFrame` /
    `EndFrame`.
14. **A full-surface dispatcher defeats trimming**: +180 KB brotli, measured (§6.7).
15. **The surface is 2,890 functions.** No amount of curation changes that number; it changes only which of them
    are in the default import (§6.6).
16. **A frame that throws does not commit its bindings**, so a JS exception mid-frame silently discards that
    frame's edits (§3.4). Deliberate — a half-replayed stream's slots are not trustworthy — but an author whose
    code throws every frame will see edits never stick, with only a console error to explain it.
17. **No same-frame measurement.** `NowText.Measure` and `NowLabel.Measure` return a `Vector2` that JS can only
    read from the result table, i.e. one frame late. A JS layout that branches on measured text size therefore
    converges over two frames rather than one. `RunMeasured` (§4.5) does not fix this for JS, because the branch
    would still be in a recorded callback.

---

## 8. Work breakdown, to a first working application

The target for "done" is: `app.js` from §1.1, served from `Standalone/Web/NowUI.Web/wwwroot`, drawing through the
existing WebGL2 backend, with typing, dragging, scrolling and reordering all working, and identity surviving an
add, a delete and a reorder.

New code lives under `Standalone/Web/NowUI.JsBridge` (a new sibling project referenced by `NowUI.Web`) and
`Tools/Standalone/JsGen`. Nothing under `Assets/NowUI` is touched.

| # | Task | Deliverable | Exit criterion |
|---|---|---|---|
| **W1** | Measure the two unmeasured numbers | a micro-benchmark page: cost of one `[JSExport]` crossing, and of a `MemoryView` round trip | a number replacing "estimated 0.2–1 µs" in §4.7; if crossings prove cheap, §4.7's second argument still stands |
| **W2** | Transport | `NowJsBuffer` (managed) + the buffer half of `runtime.js`: growable `byte[]`, three views, `Submit`, `Grow`, string arena and intern table | a hand-written buffer containing one `Button` opcode replays and draws a button |
| **W3** | Identity core | scope stack, per-kind counters, the `IdScope` + layout-scope double push, `SetId` emission, root scope | two buttons in two different rows do not share state; the duplicate-segment check throws |
| **W4** | Hand-written runtime | `mount`, `bind`, `keyed`, result objects, the validate-then-replay frame (§3.3) | a hand-written JS frame with a slider and a text field edits real values; a deliberately unbalanced frame is refused, not replayed |
| **W5** | Generator, first cut | `Tools/Standalone/JsGen` emitting all six artifacts for the **47 builders + 157 factories + the enums** only | `app.js` from §1.1 runs unmodified; the manifest is checked in |
| **W6** | Diagnostics | shape-drift hash, reorder heuristic, read-before-draw, unconsumed builder, never-read result | each of the five fires on a deliberately broken app and stays silent on `app.js` |
| **W7** | Generator, full surface | the remaining 38 static subsystems, result structs, value types, extensions; the three attribute cross-checks; overload aliases for the 43 ambiguous groups | 3,187 opcodes generated; cross-checks pass; the ambiguous-group count is asserted |
| **W8** | Delegates and measured layout | sub-span recording, callback-argument slots, binding snapshot/restore around `RunMeasured` | a deferred overlay and a `RunMeasured` block both work; a measure pass leaves bindings unchanged |
| **W9** | Drift gates in CI | regenerate-and-diff, surface-hash handshake, opcode content hashing | a deliberate signature change on a scratch branch fails the build with a readable diff |
| **W10** | Payload | measure the real trim loss with the full dispatcher linked; build the sliced-dispatcher option | the §6.7 table replaced with post-implementation measurements |

W1–W5 is the critical path to the §1.1 application running. W6 is not optional in spirit — the brief's central
warning is that identity failures look like flakiness rather than bugs, and W6 is the whole of the answer to that —
but it can land after the first running app.

---

## Appendix A — measurements, and how they were taken

All commands run on this machine, 2026-09-08, against `Standalone/Web/NowUI.Web/bin/Release/net9.0` (the current
Release build of the browser host) using a `MetadataLoadContext` reflection tool written for this document.

**A.1 Surface size.**

```
NowUI.Runtime.dll     publicTypes=315 structs=175 enums=40 enumMembers=219 staticClasses=38
                      methods=1921 (static=672 instance=1249) props=405 fields=758 ctors=127
                      genericMethods=30 refOutParams=291 delegateParams=39
                      builderStructs=47 builderMethods=707 scopeStructs=20
                      factoriesReturningBuilder=157 factoriesReturningScope=59
                      overloadGroups=305 methodsInOverloadGroups=831
extensions (6)        methods=508 publicTypes=99 enums=14 builderStructs=6
TOTAL                 methods=2429 publicTypes=414 enums=54 builders=53
```

`NowUI.Runtime`'s standalone build has 315 public types where the Unity dump has 349, because the standalone
excludes the `NowModelPreview` family — that gap is the checked-in API delta, not a discrepancy in this count.
Likewise 47 builders here against the scout's 55 in the Unity tree.

**A.2 Generated surface size.** A mock generator emitting one JS wrapper per public method, one dispatcher per
overload group, one getter per property, and one frozen object per enum, over Runtime plus the six extensions:

```
functions=2890  opcodes=3187  bytes=184181
gzip -9        29340
brotli -q 11   23376
```

**A.3 Overload ambiguity.** Dispatch on (required-arity range overlap, JS runtime kind per positional argument),
with `ref T` treated as a typed binding and structs identified by shape:

```
overloadGroups=387  ambiguousGroups=43  methodsInAmbiguousGroups=175
worst: NowInput.Interact(22) NowControls.Interact(14) NowLayout.Area(12) NowText.Draw(8) Now.Polygon(6)
```

Without typed bindings and shaped structs the same measurement gives `ambiguousGroups=111,
methodsInAmbiguousGroups=363` — which is the cost of *not* making a binding carry its type.

**A.4 Dispatcher IL cost.** A synthetic assembly with N fluent setters, built twice — with and without a `switch`
dispatching to them — at N=100 and N=800, Release, `DebugType=none`:

```
N=100  no-switch  8704   with-switch 11776
N=800  no-switch 43008   with-switch 66560
dispatcher-only slope = ((66560-43008) - (11776-8704)) / 700 = 29.3 bytes per case
```

**A.5 Trim loss.** Brotli of each NowUI assembly, linked (`obj/Release/net9.0/linked/`) versus unlinked
(`bin/Release/net9.0/`):

```
NowUI.Runtime               213844 -> 323894   (+110050)
NowUI.Engine                 50411 ->  96707   (+46296)
NowUI.Extensions.NodeGraph   27579 ->  36176   (+8597)
NowUI.Extensions.Sdf         23827 ->  28544   (+4717)
NowUI.Extensions.Markup      19462 ->  23123   (+3661)
NowUI.Extensions.CodeEditor  28939 ->  32521   (+3582)
NowUI.Extensions.Markdown    24256 ->  26194   (+1938)
NowUI.Extensions.Docking     11613 ->  12799   (+1186)
TOTAL                       399931 -> 579958   (+180027)
```

The unlinked column is an **upper bound** on the dispatcher's trim cost: it is the whole assembly, including
internals no public entry point reaches. It is close to the real figure because public members call internals.

**A.6 Current payload.** Brotli of every `.wasm`/`.js`/`.json`/`.dat` under `publish/wwwroot/_framework`:
**1,644,603 bytes**.

**Not measured, and named as such:** the cost of one `[JSExport]` boundary crossing on this host (task W1); the
real IL size of the generated dispatcher (task W10); the per-frame word counts in §4.7, which are counted by hand
from the encoding table rather than from a running build.

---

## Appendix B — the exclusion list, by category

Every public member the generator will refuse, and why. Checked in as `nowui.unsupported.json` and reviewed on
every regeneration.

| Reason code | Members | Why |
|---|---|---|
| `interior-reference` | `NowControlState.Get<T>` (8 overloads), `NowInteraction.State<T>` (2) | return `ref T`; an interior managed reference cannot cross a boundary |
| `clr-object` | `NowInspector.Draw(object)`, `NowInspector.Draw<T>(ref T)`, `SetDrawer<T>`, `RemoveDrawer<T>` | reflect over a CLR object graph |
| `host-hook` | `NowClipboard.setText/getText`, `NowTextInput.setImeEnabled/setCompositionCursor`, `Now.SetTextPreprocessor`, `NowLottieAsset.remoteUrlPolicy`, `NowMarkdownImages.remoteUrlPolicy` | host wiring owned by `WebHostServices`/`WebInput` |
| `frame-boundary` | `Now.StartUI` (4 overloads), `NowRuntime.BeginFrame/EndFrame` | `mount` owns the frame; `StartUI` cannot nest (`Now.cs:1273-1277`) |
| `span` | the 24 `ReadOnlySpan<char>` positions | a span cannot be stored in a buffer; the `string` overload beside each is generated |
| `unity-render` | `NowRenderer`/`NowDrawList` members taking `CommandBuffer`, `Camera`, `RenderTargetIdentifier`, `Material` with no JS-constructible form | no JS analogue; reachable only as handles the host supplies |
| `mono-behaviour` | `NowBootstrap`, `NowGraphic`, `NowVisualElement` and their `rebuildNowUI` events | scene objects; absent from the standalone browser host anyway |
| `editor-only` | `NowUnityEditorControlRenderer` (32 methods) | an editor renderer; no browser role |

Roughly 90 members are excluded. Every one is named in the file, so an author who cannot find something learns
*why* rather than concluding the mirror is incomplete by accident.
