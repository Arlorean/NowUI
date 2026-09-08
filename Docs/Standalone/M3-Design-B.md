# M3 Design B — the curated JavaScript surface

**Angle.** A hand-designed JavaScript API, deliberately smaller than NowUI's C# surface, shaped for the way UI is
actually written — and specifically for the way an AI writes it: a whole file in one pass, with no compiler to argue
with and no second attempt. It covers the common 90% properly and hands the rest to a mechanically generated second
tier.

**Factual base.** `Docs/Standalone/M3-SurfaceScout.md`. Every claim about the C# side below is cited to it or to
`file:line` directly. Paths are relative to `D:/wkspaces/unity/Now-UI/`.

**The one sentence.** *There are no builders, no `.Draw()`, no `using`, no ids and no refs in JavaScript; there are
58 functions, every interactive one of which takes a key as its first argument, and scopes are callbacks.*

---

## 1. The API, shown

This is a complete NowUI application. It is the whole file. Nothing else is imported, generated, or configured.

```js
// app.js
import { start, ui } from './nowui.js';

const ROLES = ['Engineering', 'Compilers', 'Research', 'Design'];

const state = {
  name:     '',
  email:    '',
  priority: 3,
  team: [
    { id: 'ada',   name: 'Ada Lovelace', role: 'Engineering', active: true  },
    { id: 'grace', name: 'Grace Hopper', role: 'Compilers',   active: true  },
    { id: 'karen', name: 'Karen Jones',  role: 'Research',    active: false },
  ],
  selected: 'ada',
  status:   null,
};

function addPerson() {
  const name = state.name.trim();
  if (!name) { state.status = 'A name is required.'; return; }
  const id = name.toLowerCase().replace(/[^a-z0-9]+/g, '-');
  state.team.push({ id, name, role: ROLES[0], active: true });
  state.selected = id;
  state.name = state.email = '';
  state.status = `Added ${name}.`;
}

start(() => {
  ui.column({ padding: 24, gap: 16, fill: true }, () => {

    ui.heading('Team roster');

    // ---- the form -------------------------------------------------------
    ui.card({ padding: 16, gap: 12 }, () => {

      ui.row({ gap: 12 }, () => {
        state.name  = ui.textField('name',  state.name,  { label: 'Name',  placeholder: 'Full name',     grow: 1 });
        state.email = ui.textField('email', state.email, { label: 'Email', placeholder: 'name@team.dev', grow: 1 });
      });

      ui.row({ gap: 12, align: 'center' }, () => {
        ui.text('Priority', { width: 64 });
        state.priority = ui.slider('priority', state.priority, 1, 5, { step: 1, grow: 1 });
        ui.text(String(state.priority), { width: 24, align: 'end' });
      });

      ui.row({ gap: 8, justify: 'end' }, () => {
        if (ui.button('Clear', { style: 'ghost' })) {
          state.name = state.email = '';
          state.status = null;
        }
        if (ui.button('Add', { style: 'accent', disabled: state.name.trim() === '' })) {
          addPerson();
        }
      });
    });

    // ---- the list -------------------------------------------------------
    ui.scroll('roster', { grow: 1, gap: 2 }, () => {

      ui.list('team', state.team, p => p.id, (p) => {
        ui.row({ gap: 8, align: 'center' }, () => {
          if (ui.selectable('row', p.id === state.selected, { label: p.name, grow: 1 }))
            state.selected = p.id;

          p.role   = ui.dropdown('role', p.role, ROLES, { width: 150 });
          p.active = ui.switch('active', p.active);

          if (ui.button('Remove', { style: 'danger' })) {
            state.team = state.team.filter(x => x.id !== p.id);
            state.status = `Removed ${p.name}.`;
          }
        });
      });

      ui.when(state.team.length === 0, () => {
        ui.text('Nobody on the team yet.', { style: 'muted' });
      });
    });

    ui.when(state.status !== null, () => ui.note(state.status));
  });
});
```

`index.html` boots the wasm module, which calls `start`'s callback once per animation frame and draws. That page
already exists in this repository's shape (`Standalone/Web/NowUI.Web/wwwroot/index.html`, `main.js`).

### 1.1 The rules the example is teaching

There are five, and they are the whole design.

**R1 — Every interactive control takes a key as its first argument. Nothing else does.**
`ui.button('Add', …)`, `ui.textField('name', …)`, `ui.slider('priority', …)`, `ui.switch('active', …)`.
Drawings — `ui.text`, `ui.image`, `ui.rule`, `ui.space` — hold no state and take content first, no key. That single
distinction is the entire identity model (§2), and it is the one thing an author has to internalise.

**R2 — Argument order is always `key, value, data, options`.**
Key, then the current value if the control has one, then whatever the control itself needs (min/max, the option
list), then one options object. No overloads, ever. `ui.slider('priority', v, 1, 5, {step:1})`,
`ui.dropdown('role', p.role, ROLES, {width:150})`, `ui.textField('name', v, {placeholder:'…'})`. There is no
signature in this API where two arguments of the same type are adjacent and swappable, except the deliberate
`min, max` pair. Compare the C# form, where `SetId`, `SetWidth`, `SetStyle`, `SetTextStyle`, `SetAlignItems` and
`SetNavigation` are six chained calls whose order does not matter but whose *presence* does, and where forgetting
`.Draw()` renders nothing and produces only a compiler warning (NOWUI001, `NowBuilderDiscardAnalyzer.cs:22`). In
JavaScript there is no builder object to forget to consume: calling `ui.button(...)` *is* the draw. The library's
most common authoring mistake becomes unrepresentable.

**R3 — Values go in and come back out. There are no refs and no read-backs.**
`state.priority = ui.slider('priority', state.priority, 1, 5)`. The C# API's pervasive `Draw(ref T value)` — about
thirty distinct methods (M3-SurfaceScout §4.1) and the single largest structural obstacle to any bridge (§5.3d) — is
collapsed into ordinary assignment. The durable value lives in a plain JavaScript object, which is where an author
already keeps it and where they can serialise it, diff it, or send it over a socket. NowUI keeps only the ephemeral
part (caret, selection, scroll offset), which is all it kept anyway.

**R4 — Scopes are callbacks. There is no begin, no end, and nothing to dispose.**
`ui.column(opts, body)`, `ui.row`, `ui.card`, `ui.scroll(key, opts, body)`, `ui.list`, `ui.when`. Balance is a
property of the JavaScript call stack, not of the author's discipline (§3). The C# facts that make this
non-negotiable — out-of-order disposal throws (`NowScopeGuard.cs:54-61`), and a scope left open across a frame
boundary throws out of the *next* `Now.StartUI` (`Now.cs:1279-1293`) — are unreachable from this shape.

**R5 — Boolean-returning controls are events, read with `if`.**
`if (ui.button('Add')) addPerson();` — true on exactly the frame it is clicked, once. This is the C# convention
verbatim (`NowControls.cs:18-21`) and it survives translation because the bridge latches and consumes one-shot flags
(§5.3).

### 1.2 What is deliberately absent from the example

No ids. No `NowId`, no `NowResolvedId`, no `IdScope`, no `KeyedItem` — all four of the C# identity mechanisms
(M3-SurfaceScout §2.4) are used by the bridge and none is visible. No `using`. No `.Draw()`, `.Begin()`,
`.SetOptions()`, `.SetWidth()`. No `NowRect` and no layout arithmetic. No frame function beyond `start`. No type
imports and no build step: `app.js` is served as-is, and `nowui.js` is a static file beside it.

### 1.3 The complete surface, listed

58 functions. This is the whole of tier 1.

| Group | Functions |
|---|---|
| **Frame** (3) | `start(draw)`, `ui.frame` *(read-only: `width`, `height`, `dt`, `time`)*, `ui.theme(name, body)` |
| **Scopes** (11) | `column`, `row`, `card`, `panel`, `scroll`\*, `group`\*, `list`\*, `when`, `tabs`\*, `foldout`\*, `split`\* |
| **Drawings** (9) | `text`, `heading`, `subheading`, `caption`, `rule`, `space`, `image`, `rect`, `circle` |
| **Actions** (5) | `button`\*, `selectable`\*, `chip`\*, `link`\*, `iconButton`\* |
| **Values** (17) | `textField`\*, `textArea`\*, `numberField`\*, `checkbox`\*, `switch`\*, `radioGroup`\*, `slider`\*, `intSlider`\*, `dropdown`\*, `combo`\*, `colorField`\*, `datePicker`\*, `timePicker`\*, `tree`\*, `codeEditor`\*, `splitRatio`\*, `filePicker`\* |
| **Feedback** (7) | `progress`, `badge`, `note`, `tooltip`, `contextMenu`\*, `confirm`\*, `markdown` |
| **Shapes** (6) | `shape.circle`, `shape.box`, `shape.union`, `shape.subtract`, `shape.smooth`, `shape.draw` |

`*` = takes a key (R1). That is 33 of 58.

Everything else in the C# library — 55 builders, 184 factories, 830 fluent setters (M3-SurfaceScout §0) — is
reachable only through tier 2 (§6.4), or not at all (§7).

---

## 2. Identity

### 2.1 The problem, restated exactly

`[CallerFilePath]`/`[CallerLineNumber]` are captured at 188 entry points and interned into an `int` site token
(`NowControls.cs:425-455`), which becomes the control's identity when no explicit id is given
(`NowControls.cs:498-504`). JavaScript can supply neither. So identity must come from somewhere else, and the failure
mode is silent: two controls sharing an id share focus, caret, drag and scroll — "which is the caller's bug, not
something to silently disambiguate" (`NowControls.cs:457-462`) — and the only guard, `CheckDuplicateControlId`, is
`[Conditional("UNITY_EDITOR")] [Conditional("DEVELOPMENT_BUILD")]` (`NowControls.cs:606-625`), i.e. compiled out of a
Release wasm build entirely.

### 2.2 The answer: an authored path, and JavaScript owns the values

Two decisions, and neither works without the other.

**(a) Identity is a path of segments, and every interactive control contributes an authored segment.**

The path of the `Remove` button in the example, written out:

```
  root / #0 / "roster" / "team" / "grace" / #0 / "Remove"
         │       │         │        │       │       │
         │       │         │        │       │       └── the control's key
         │       │         │        │       └────────── the ui.row inside the item (anonymous, ordinal 0)
         │       │         │        └────────────────── the list item's key, from keyOf
         │       │         └─────────────────────────── the ui.list key
         │       └───────────────────────────────────── the ui.scroll key
         └───────────────────────────────────────────── the outer ui.column (anonymous, ordinal 0)
```

Each segment enters the C# API through a mechanism that already exists and is documented as first-class
(M3-SurfaceScout §2.4). Nothing new is required of `Assets/NowUI`:

| Bridge construct | C# call | Why that one |
|---|---|---|
| a keyed scope (`ui.group('x')`, `ui.scroll('roster')`) | `NowControls.IdScope(string)` — `NowControls.cs:197` | Derives under `NowIdDomain.Scope` against the innermost parent; nests correctly. Rejects null/empty, which the bridge guarantees. |
| an anonymous scope (`ui.column`, `ui.row`) | `NowControls.IdScope(int ordinal)` — `NowControls.cs:230` | Integer 0 is a valid id (`Identity.md:23-27`); no allocation; same domain. |
| one item of a list | `NowControls.KeyedItemIn(listId, key)` — `NowControls.cs:260` | The *only* call-site-free keyed-collection primitive in the library. `KeyedItem(key)` needs a call site and is therefore unusable here (M3-SurfaceScout fact 4). |
| a control | `SetId(NowId)` — 39 builders, `NowControlBuilders.cs:64-68` | Explicit authored id, resolved once by the consumer against the ambient scope stack. |

The bridge never constructs a `NowResolvedId` and never calls `IdScope(NowResolvedId)` — that overload *replaces* the
ambient path instead of nesting under it (`NowControls.cs:126-138`), which is exactly the wrong semantics here. It
never calls `IdScope(NowId)` either, because a default `NowId` there silently pushes nothing
(`NowControls.cs:213-214`), and a bridge that could ever produce a default would flatten a whole subtree's identity
with no diagnostic. Only the `string` and `int` overloads, both of which either throw or are total.

The consequence is that the domain discipline the library was built around survives intact: control ids stay in
`NowIdDomain.Control`, layout groups in `Layout`, scopes in `Scope`, and "a control path, layout cache, state slot,
effect, focus host, and overlay cannot alias" (`Identity.md:48-52`) remains true of a JavaScript-authored app.

**(b) The durable value lives in JavaScript, so a wrong identity costs a caret, never data.**

This is what makes (a) safe enough to ship. In C#, `NowControlState` holds text-edit state, scroll offsets, tree
expansion and drag state keyed by resolved id, and losing an id loses all of them. But the *value* — the string in
the field, the number under the knob — is caller-owned in C# too (`NowTextField.cs:685`; M3-SurfaceScout §4.2: "NowUI
stores **no** caller text"), and here the caller is a JavaScript object. So the exact blast radius of an identity
change is:

| Lost when identity changes | Not lost |
|---|---|
| text caret position, selection, undo history | the text itself |
| scroll offset | the list contents and the selection |
| tree expansion state | which node is selected |
| split ratio, selected tab, foldout open/closed | everything in `state` |
| focus, and an in-progress drag | — |

And it is recoverable: `NowControlState` evicts after ten seconds untouched (`NowControlState.cs:35`,
`EVICT_AFTER_SECONDS = 10f`), so a control that disappears and comes back inside ten seconds finds its caret and
scroll exactly where it left them. Longer than ten seconds and it does not. That is a real, observable behaviour of
this design, stated here rather than discovered later.

### 2.3 The rule the author follows

> **Anything you would be annoyed to lose the scroll position of takes a key you chose. Everything else is
> positional, and the bridge handles it.**

That is why exactly six scopes require a key — `scroll`, `tree`, `tabs`, `foldout`, `split`, `codeEditor` — and
`column`/`row`/`card`/`panel` do not. Those six are the ones whose only home is `NowControlState` (Appendix B). The
`.d.ts` makes the key a required parameter on each, so an author who omits it gets an editor error and a thrown
`TypeError` at runtime, not a silently shared scroll offset.

### 2.4 What happens in each of the four cases the brief names

**A conditional adds or removes a control.**
Nothing shifts. `ui.button('Add')` is identified by the string `"Add"` and the enclosing path, never by position, so
wrapping it in an `if` and unwrapping it later changes nothing about its siblings. This is the largest behavioural
difference from the C# call-site model, where inserting a line above a control changes `[CallerLineNumber]` for
every control below it in the file. C# absorbs that because a recompile is a recompile; a bridge cannot.

**A conditional adds or removes an anonymous *scope*.**
This one does shift, and it is the design's one honest positional hazard. Anonymous scopes are numbered by ordinal
within their parent, so:

```js
if (showToolbar) ui.row({}, () => { /* … */ });   // ordinal #0 when present
ui.column({}, () => ui.scroll('main', …));        // #1 with the toolbar, #0 without it
```

flips the `main` scroll view between two different resolved ids as `showToolbar` toggles, losing its offset each
time. Two mitigations, in order of preference:

1. **`ui.when(cond, body)` exists for exactly this.** It always occupies its ordinal and only runs `body` when
   `cond` is true, so toggling shifts nothing. The example uses it twice. This is why `ui.when` is in a 58-function
   API instead of "just write an `if`": an `if` around a *control* is free, an `if` around a *scope* is not, and
   rather than ask the author to remember which, the API provides one form that is always right.
2. Give the scope a key: `ui.group('main', …)`. Keyed scopes are immune to ordinal shifts.

The bridge reports this rather than leaving it silent. In debug mode (`?nowui=debug`, using the query-parameter
plumbing already in `WebHostServices.cs:68`) the recorder keeps the previous frame's child ordinals per scope path
and logs, once per path:

```
NowUI: the anonymous scope at root/#0 changed ordinal (#1 -> #0); 4 controls beneath it
       lost their caret/scroll state. Give it a key — ui.group('name', …) — or wrap the
       conditional sibling in ui.when().
```

**A list reorders.**
`ui.list(key, items, keyOf, render)` makes `keyOf` a **required** parameter. There is no overload without it and no
default of `(_, i) => i`. Passing `p => p.id` means state follows the item through insertion, removal and
reordering — precisely what `KeyedItemIn` is documented to provide ("The required domain key survives insertion,
removal and reordering", `NowControls.cs:255-258`). Passing `(_, i) => i` means state follows the *position*, which
is sometimes what you want (a fixed eight-slot inventory grid) and is a decision the author is now forced to write
down. Requiring `keyOf` costs eight characters and removes the most common source of state-follows-the-wrong-row
bugs in every immediate-mode UI.

A `keyOf` returning a duplicate, `undefined`, or a non-string/non-integer throws immediately with the offending
index and value. (`KeyedItemIn` would itself throw on an empty key, `NowControls.cs:262-266`, but by then the
message has lost the JavaScript context that makes it actionable.)

**The same control drawn in a loop.**
Without `ui.list`, a bare `for` loop emitting `ui.button('Remove')` produces the same path twice — silent state
sharing in C#. Here it throws on the first frame:

```
NowUI: duplicate control key.
  path:  root/#0/roster/#1/Remove
  first: draw call #217
  again: draw call #241
Two controls with the same path share focus, caret and drag state. Use
ui.list(key, items, keyOf, render) for repeated data, or give each one a distinct key.
```

This is the second thing the curated tier adds that the C# API does not have, and the more important one: **the
duplicate check runs in every build, not only under `UNITY_EDITOR || DEVELOPMENT_BUILD`.** It is a `Set<number>` of
rids (§4.4) cleared each frame — one hash insert per control, no allocation — and it turns M3-SurfaceScout's fact 3
from a silent corruption into a first-frame stack trace.

### 2.5 What breaks, stated plainly

1. **A key that changes every frame re-registers the control every frame.** ``ui.button(`Retry (${n})`)`` gets a new
   identity on each `n`, so it can never hold focus and its press-tracking resets mid-press. The fix is
   ``ui.button('retry', { label: `Retry (${n})` })``, and this is exactly why `label` is an *option* rather than a
   positional argument: the ergonomic default (key doubles as label) is safe only for static labels, so the override
   has to be cheap. Debug mode reports keys that appear for a single frame and never again, which catches this on
   frame two.
2. **A 64-bit hash collision between two different paths presents as a duplicate-key error.** The detector compares
   rids, not path strings — keeping path strings per control would allocate per control per frame. At 64 bits and a
   few thousand controls the probability is order 1e-13 per frame. When it happens the author sees a spurious
   duplicate error naming two visibly different paths: annoying, loud, and never a silent state share. That is the
   correct direction to fail in.
3. **Two `ui.list` calls with the same key under the same parent collide**, because the list key is a path segment
   like any other. It is caught by the duplicate detector on the item scopes rather than on the list, so the message
   names an item id. Acceptable; noted.
4. **Identity is not stable across a key rename.** Renaming `'roster'` to `'people'` discards that scroll view's
   offset. This is correct — it is a different scroll view now — but it means hot reload (§8, W4) resets ephemeral
   state for anything whose key changed, and only for those.

---

## 3. Scopes without `using`

### 3.1 The shape

Every scope in tier 1 is a function taking a body callback. There is no `ui.beginColumn`, no scope handle, nothing
an author can hold, store, or forget.

```js
ui.column({ gap: 8 }, () => {
  ui.row({ justify: 'end' }, () => {
    ui.button('OK');
  });
});
```

The recorder is four lines, and the same four lines for all eleven scopes:

```js
function scope(op, args, body) {
  emitOpen(op, args);              // pushes the identity segment + the opcode
  try { body(); }
  finally { emitClose(op); }       // always: normal return, throw, or return-through
}
```

Because the `finally` runs on every path out of `body`, the emitted stream is **structurally balanced by
construction and strictly LIFO**. That is not a best effort; it is a property of the language. The C# replay
therefore never has to reorder, repair, or defend against a crossed pair — which matters, because out-of-order
disposal in NowUI is a thrown `InvalidOperationException`, not a fixup (`NowScopeGuard.cs:54-61`).

### 3.2 Enforcement, in three places

**In JavaScript, at record time.** The recorder keeps a depth counter and a small array of currently open opcodes
(max depth 32; deeper throws, because a UI thirty-three scopes deep is runaway recursion and saying so is more
useful than exhausting the buffer). `emitClose` asserts the opcode it closes matches the top of that array.

**In JavaScript, at the boundary.** A `ui.*` call outside an active recording throws:

```
NowUI: ui.button() was called outside the frame callback. Every ui.* call must happen
synchronously inside the function you passed to start(). Timers, promises and event
handlers cannot draw — change `state` from them instead, and the next frame will show it.
```

`start(draw)` also rejects a `draw` that returns a thenable, with the same explanation. An `await` inside a draw
function would emit half a frame's ops, hand control back to the browser, and then emit the rest into a buffer the
wasm side has already consumed. It is the mistake a JavaScript author is most likely to make, and it earns a
dedicated message.

**In C#, at replay.** The decoder maintains its own stack of the real `IDisposable` structs and disposes them in
reverse on close. It also validates: an unmatched close, or a buffer ending with a non-empty stack, is a
`NowBridgeProtocolException` naming the opcode and slot offset. That check exists not because the JS recorder can
produce such a buffer — it cannot — but because the same buffer is the tier-2 and test surface, and a decoder that
trusts its input is a decoder that corrupts a frame silently.

### 3.3 When JavaScript throws mid-frame

The author's `body` throws. In order:

1. Every enclosing `scope()`'s `finally` emits its close op, innermost first. The buffer is **balanced and valid**,
   just short.
2. The exception reaches `start`'s own try/catch around the draw call.
3. The bridge marks the frame *faulted*, records the error, and **returns the partial buffer anyway.**
4. The replay draws it, then draws an error banner over the top: the message, the first three stack frames, and the
   path of the last scope open when it threw.
5. The error is logged once per distinct `message + top frame` — not once per frame — through `BrowserInterop.Log`
   (`WebHostServices.cs:58`), the same latch the existing host already uses for a failed frame
   (`Program.cs:330-337`).
6. After 120 consecutive faulted frames (two seconds) the loop stops drawing the app and shows only the banner. The
   first error is the useful one, and a page burning its frame budget on a stack trace sixty times a second helps
   nobody.

**Why replay the partial buffer rather than the last good one.** Freezing on the last complete frame makes the app
look alive and correct while being neither, and hides *where* it stopped. Drawing what actually got emitted shows
the author the exact failure point as a visual boundary: everything above the banner rendered, everything below it
did not. The cost is real and is stated — controls after the throw point are not drawn, so a fault persisting past
ten seconds costs them their caret and scroll offsets to `NowControlState`'s eviction (`NowControlState.cs:35`).
That is an acceptable price for a diagnostic that points at the line.

### 3.4 Why there is no begin/end escape hatch in tier 1

`NowContextMenu` proves an imperative `Begin`/`Item`/`End` API is expressible — it is the one place in NowUI with no
`IDisposable` at all (`NowContextMenu.cs:304,390,504,560,614`), and M3-SurfaceScout §3.6(5) names it as the shape a
JS caller could mirror one-to-one. This design declines. A `beginColumn`/`endColumn` pair in JavaScript is an
invitation for an early `return`, a `continue`, or a thrown error to leave a scope open, and the consequence is not a
missing box: it is an `InvalidOperationException` out of the *next* frame's `Now.StartUI` (`Now.cs:1279-1293`) — the
app dies one frame later, at a stack trace with no visible relationship to the bug.
`ui.contextMenu(key, items, opts)` takes an array of item descriptors and does the begin/end inside the bridge.

Tier 2 (§6.4) does expose paired operations, because it is generated and cannot do otherwise. It wraps them in the
same callback shape, and its documentation says in one line that this is why tier 1 exists.

---

## 4. The command stream

### 4.1 The frame, in order

```
requestAnimationFrame
  └─ WebApp.Frame()                          [JSExport — exists today, Program.cs:250]
       host.Poll(); NowRuntime.BeginFrame(); input.Drain();           (unchanged)
       using (Now.StartUI(dpr))
         NowLayout.RunMeasured(screen, s_ReplayDelegate)              ← the bridge
       NowRuntime.EndFrame();
```

and inside that, the bridge does exactly two things:

```
 1. Record.  ONE call out to JavaScript. JS runs the author's draw function, reading last
             frame's results from a memory view and writing this frame's ops into another.
             Returns the slot count used.
 2. Replay.  Decode the ops, call the real static NowUI API, fill the result table.
```

**Record before replay, in the same frame.** M3-SurfaceScout's summary of `StandaloneCoreDesign.md` §4.8 has the
buffer recorded in frame N and replayed at the *next* `BeginFrame`. Recording first and replaying immediately is
strictly better and costs nothing: the pixels of frame N then reflect the JavaScript state of frame N, so anything
JS changes for a reason other than a control — a fetch completing, an animation, a websocket message — appears the
same frame. Read latency is one frame either way (§5.2). There is no reason to also take a frame of render lag.

**Replay inside `NowLayout.RunMeasured`** (`NowLayout.cs:1493`). This costs one extra decode of the buffer per frame
and buys exact layout every frame: flexible space, stretch shares and auto-sized groups resolve from *this* frame's
measurements instead of last frame's, removing four of the rows in M3-SurfaceScout §4.4's latency table outright. It
is legal because the buffer is idempotent (§4.6) and because explicit ids are never occurrence-salted, so the measure
pass and the real pass resolve every control to the same `NowResolvedId` by construction — the exact property fact
14 demands. `RunMeasured` takes an `Action`; the bridge passes a cached static delegate field, not a lambda, per its
own doc comment (`NowLayout.cs:1489-1491`).

### 4.2 Transport: two memory views and one call

The mechanism is already proven in this repository, in the opposite direction. `WebGL2Backend` passes
`[JSMarshalAs<JSType.MemoryView>] Span<int>` and `Span<byte>` across `[JSImport]` boundaries so "a whole mesh can
cross in one call without a managed-to-JS array copy" (`WebGL2Backend.cs:1941-1945`). A `MemoryView` is a JavaScript
`TypedArray` over WASM linear memory, valid for the duration of the call — and the duration of *this* call is the
whole of the author's draw function.

```csharp
// Standalone/NowUI.Bridge/BridgeInterop.cs
[JSImport("record", "nowui-app")]
internal static partial int Record(
    [JSMarshalAs<JSType.MemoryView>] Span<int>  ops,          // JS writes commands here
    [JSMarshalAs<JSType.MemoryView>] Span<byte> opsText,      // JS writes UTF-8 string bytes here
    [JSMarshalAs<JSType.MemoryView>] Span<int>  results,      // last frame's result table
    [JSMarshalAs<JSType.MemoryView>] Span<byte> resultsText,
    int resultCount);
```

It returns the number of `int` slots written, or a negative value meaning "I need at least `-n` slots" (§4.5).

The JavaScript side:

```js
export function record(ops, opsText, results, resultsText, resultCount) {
  R.load(results, resultsText, resultCount);   // n Map inserts, n = live controls
  W.reset(ops, opsText);
  try { drawFn(); }
  catch (e) { W.fault(e); }
  finally { W.closeAll(); }
  return W.used;
}
```

Strings never cross as strings. `TextEncoder.encodeInto(str, opsText.subarray(cursor))` writes UTF-8 directly into
WASM memory and returns the byte count; the op carries `(byteOffset, byteLength)`. On the C# side that is
`Encoding.UTF8.GetString(span.Slice(off, len))` — one allocation per distinct volatile string per frame, and zero for
interned ones (§4.4).

**Boundary crossings per frame: 2.** `Frame()` in (which already exists and is not the bridge's cost) and `Record`
out. That number is constant: it does not grow with the number of controls, the depth of the UI, or the number of
results read. For scale, the input bridge that ships today costs two crossings per frame on its own
(`WebInput.cs:390-391`).

**If `MemoryView` on an import turns out not to survive a re-entrant call** — the one transport assumption worth
verifying in W1 before anything is built on it — the fallback is already proven in this repo: `WebInput.Drain`
returns a packed `double[]` via `[return: JSMarshalAs<JSType.Array<JSType.Number>>]` (`WebInput.cs:401-403`). That
costs one array copy each way and takes the frame to four crossings. The design does not otherwise change.

### 4.3 Op encoding

One `Int32Array` and one `Float32Array` over the same `ArrayBuffer`, so a slot is read as whichever the opcode's
signature says. Header per op:

```
  slot[0]       = opcode | (argc << 16)
  slot[1..argc] = arguments
```

Sixteen bits of opcode (tier 1 uses 1..58; tier 2 starts at 1024) and sixteen bits of arity. The arity is redundant
with the generated signature table and is carried anyway: a decoder meeting an opcode it does not know can skip
`argc` slots and continue, rather than desynchronise and produce garbage. Four bytes per op to make a version
mismatch between `nowui.js` and the wasm module a warning instead of a crash is the right trade.

Argument kinds, all one slot except where noted:

| Kind | Encoding |
|---|---|
| `i32` | as-is |
| `f32` | written through the float view |
| `bool` | 0 / 1 |
| `str` | `sid >= 0` → interned-table index; `sid < 0` → `-(sid+1)` indexes this frame's volatile string list, whose `(offset,length)` pairs live in the ops tail |
| `rid` | 2 slots — the 64-bit path hash (§4.4) |
| `color` | 1 slot, RGBA8 packed |
| `rect` | 4 `f32` slots |
| `enum` | `i32`, values generated from the C# enum so `'accent'` → `NowRectangleStyle.Accent = 3` (`NowThemeStyles.cs:4-14`) |
| `opts` | 1 slot bitmask of which optional fields follow, then those fields in declared order |

The `opts` bitmask is what keeps an options object cheap. `ui.button('Add', { style: 'accent' })` emits
`[OP_BUTTON|5<<16, ridHi, ridLo, sid('Add'), 0x08, 3]` — six slots, 24 bytes. The whole example application above
emits, at rest, **214 slots (856 bytes)** for 23 controls and 9 scopes.

A realistic larger app: 200 controls averaging seven slots plus 60 scopes averaging six is 1,760 slots — 7 KB per
frame, written by JavaScript directly into WASM memory with no copy, decoded by an allocation-free `switch`. The
existing WebGL2 backend already moves two orders of magnitude more than that per frame in vertex data.

### 4.4 Strings and rids

**Interned strings.** JavaScript keeps `Map<string, number>`; the wasm side keeps `List<string>` at the same
indices. A string is interned on first use and its index is permanent for the session. New interns this frame are
appended to the ops tail as `(offset, length)` pairs into `opsText`, so the wasm side can add them in one pass before
decoding begins. Keys, labels, style names, placeholders and option lists all intern on frame 1 and cost one slot
each thereafter. The table caps at 4,096 live entries; on overflow the recorder stops interning and treats
everything as volatile, logging once — a UI with 4,096 distinct static strings is not a UI, it is data, and it
should be flowing through the volatile path anyway.

**Volatile strings.** Anything the author interpolates — ``` `Added ${name}.` ```, the contents of a text field, a
formatted number — is never interned. It is written into `opsText` for this frame only and referenced by a negative
sid. This is the difference between a text field costing one UTF-8 encode per keystroke and permanently leaking one
intern-table entry per keystroke. The recorder decides by argument position, from the generated signature table, not
by guessing.

**Rids.** The 64-bit path hash, computed incrementally in JavaScript as the path is built. Each scope push derives a
child hash from its parent's; each control derives one more from its key.

The wasm side **never computes a rid** — it only echoes them back in the result table — so the two sides cannot
disagree about the hash function. (A conformance test asserts they agree anyway, because tier 2 and the golden
buffers exercise the C# implementation.)

Note the deliberate split: **rids are the bridge's key for the result table; `NowResolvedId`s are NowUI's key for
state.** They are different values in different spaces and neither is derivable from the other. That is not a
redundancy to eliminate — `NowResolvedId` has no public value constructor (`NowResolvedId.cs:15`), so a JS-side
handle to one could only ever be a table index, and a table index is worse than a hash in every way that matters
here.

### 4.5 Buffer growth

Both buffers start at 64 KB of ops (16,384 slots) and 64 KB of text — enough for roughly 2,000 controls, which is
already an unreasonable UI. The recorder bounds-checks each write against the view's length. On overflow it does not
grow mid-frame (it cannot: the view is WASM memory it does not own). It unwinds cleanly, discards the frame's ops,
and returns `-needed`. The C# side sees the negative return, reallocates to the next power of two, and calls
`Record` again — same frame, one extra crossing, at most once per size class for the life of the app.

Hard cap at 4 M slots (16 MB):

```
NowUI: the draw function emitted more than 4,194,304 command slots. This is almost always
an unbounded loop or a recursive component. The last 8 scope keys were: …
```

### 4.6 Idempotence

The buffer is decoded twice per frame (the `RunMeasured` measure pass and the real pass), so decoding must have no
side effects of its own. Concretely the decoder:

* allocates nothing that outlives the call except the strings it interns — and those are added *before* decoding,
  once;
* advances no cursor that is not a local;
* writes to the result table **only when `NowLayout.isMeasurePass` is false**, so a passive pass — during which
  `NowInput.isPassive` already makes every interaction result inert — cannot overwrite real results with zeroes;
* consumes no one-shot state.

The decode is a `while (p < end) switch (ops[p] & 0xFFFF)`. Each case reads its arguments, calls the C# API, and
advances `p`. At roughly the input decoder's measured per-record cost, 1,760 slots at ~7 slots per op is ~250 ops
decoded twice — tens of microseconds against a 16 ms budget.

---

## 5. The result table

### 5.1 What is in it

One record per interactive control drawn in the last replay, keyed by rid:

```
  [ ridHi, ridLo, flags, kind, v0, v1, v2, v3 ]        (variable: 4..8 slots)
```

`flags` is a bitfield: `clicked, changed, submitted, focused, hovered, pressed, held, released, dragging,
dragStarted, dragEnded, hasRect`. Every one of these is computed synchronously inside the replay — `Interact` runs
against the current input snapshot before the control draws (`NowControls.cs:891-900`) — so they are *that replay's*
truth, not an approximation of it.

`kind` selects what `v0..v3` hold: nothing, a float, an int, a UTF-8 string span, or a rect. The value slots carry
the post-draw value of a value control, which is what makes R3 work.

Controls not drawn produce no record, and their previous record is dropped. This matches `NowControlState`'s own
discipline (M3-SurfaceScout §4.5: "A control that is not drawn produces no entry") and is what stops the table
serving a `clicked` for a button that stopped existing three seconds ago.

Cost: the 23 controls in the example produce 23 records, ~140 slots, 560 bytes. They ride back in the same `Record`
call the ops go out on, so they cost no crossing at all.

### 5.2 What "one frame old" actually means here, and where it shows

The record JavaScript reads during frame N was written by the replay of frame N−1:

```
frame N:    input drained → JS draws (reads N−1's results) → replay (writes N's results)
frame N+1:  input drained → JS draws (reads N's results)   → replay
```

A click landing in frame N's input is seen by frame N's replay, is in frame N's result table, and is read by
JavaScript at the start of frame N+1 — whose replay draws the consequence. **A click becomes visible one frame after
it happens.** At 60 Hz that is 16 ms, below the threshold where a pointer interaction reads as laggy, and it is the
same one frame that `NowFocus` navigation, pointer arbitration and overlay blocking already cost inside C#
(M3-SurfaceScout §4.4).

Two places where it is genuinely visible, both named in the docs rather than papered over:

**(a) A value derived in JavaScript lags its control by one frame.** In the example, `ui.text(String(state.priority))`
sits beside the slider. Drag the knob: the knob moves immediately, because C# computes the new value inside the same
replay and draws it — but the number beside it is one frame behind, because `state.priority` only receives the new
value at the start of the next frame. At 60 Hz this is invisible in practice, and it is the one thing about this
design a careful author will eventually notice.

**(b) Anything already one frame late in C# becomes two.** Context-menu item clicks ("true when it was clicked, the
frame after the click", `NowContextMenu.cs:346`) and dropdown/combo-box selection ("Selection from the popup applies
on the next frame's Draw", `NowDropdown.cs:14-15`) are already deferred inside the library. Through this bridge they
are two frames — 33 ms. Unnoticeable for a menu selection; recorded here because it is a fact of the design and not
of the implementation.

**And a thing that is impossible, so it is not offered.** There is no "settle" mode that re-runs the draw function
after the replay to resolve values in the same frame. It cannot exist: settling means drawing the same subtree twice
inside one `Now.StartUI`, and explicit ids are never occurrence-salted (`NowControls.cs:457-462`), so the second draw
would collide with the first on every control and share focus, caret and drag state with itself. The measure pass
inside `RunMeasured` is the one legal double-draw, and it is legal precisely because it is passive — which is also
why it cannot produce the values a settle pass would need. One frame of model lag is a property of this
architecture, not a missing feature.

### 5.3 How the author is kept from being surprised

**One-shot reads are latched and consumed.** `clicked`, `submitted`, `dragStarted`, `dragEnded` are events, not
states. The loader marks them unread; the first `ui.button(...)` matching the rid returns `true` and clears the bit.
A control drawn twice — which throws anyway (§2.4) — cannot double-fire. A control not drawn this frame never fires,
and its record is dropped at the end of the frame, so an event can never be delivered late.

**Values are answered from the caller's own input when nothing changed.** This is the mechanism that makes the
latency invisible for the common case:

```js
ui.slider = (key, value, min, max, opts) => {
  const rid = pathHash(key);
  emit(OP_SLIDER, rid, value, min, max, opts);
  const r = results.get(rid);
  return (r !== undefined && r.changed) ? r.value : value;   // ← the whole trick
};
```

If the control did not change, the author gets back exactly the number they passed in — bit-identical, no drift, no
snapping. If it did change, they get the post-draw value from the last replay. The author never observes a stale
value; they observe either their own value or a fresher one. Those three lines are the entirety of the `ref`
problem's solution.

**A read that could not have been served says so.** Reading a result for a rid with no record — the first frame, or
the frame after a key changed — returns the identity default: `false` for events, the passed-in value for values. In
debug mode the loader additionally counts missed reads and logs once per rid *after* that control has been drawn for
three consecutive frames without ever producing a record, which is the signature of a control the replay is silently
not reaching (a malformed options object, an opcode the wasm side skipped as unknown). This is the mirror image of
`NowMarkup`'s query validator (`NowMarkupDocument.cs:208-239`) — the only existing mitigation in the codebase for a
string lookup that returns false forever — and it exists for the same reason.

**`ui.frame` is honest about which frame it describes.** `ui.frame.width/height/dt/time` come from the host poll at
the top of the *current* frame (`Program.cs:258`), not from the result table, so they are not one frame old. Mixing
current and previous-frame data under one accessor would be the worst available ergonomic, so the split is explicit:
anything under `ui.frame` is current, and every return value from a `ui.*` control is previous-replay.

---

## 6. How the surface is produced, and kept honest

### 6.1 Both, with a clear seam

Tier 1 is **hand-written as a specification and generated from it**. `Standalone/Surface/surface.json` is the single
source of truth; everything below is generated and none of it is edited:

```
  Standalone/Surface/surface.json                          (hand-written, ~1400 lines, reviewed)
        │
        ├─→ Standalone/NowUI.Bridge/Generated/Replay.g.cs      the decode switch
        ├─→ Standalone/NowUI.Bridge/Generated/Ops.g.cs         opcode + signature tables
        ├─→ Standalone/Web/NowUI.Web/wwwroot/nowui.js          the recorder and the 58 functions
        ├─→ Standalone/Web/NowUI.Web/wwwroot/nowui.d.ts        types, for editors and for the AI
        ├─→ Docs/Standalone/M3-Surface.md                      the reference the author reads
        └─→ Docs/Standalone/M3-SurfaceCoverage.md              what is NOT covered  ← the important one
```

A spec entry is a recipe, not prose:

```jsonc
{
  "js": "slider", "op": 21, "key": "required",
  "params": [ { "name": "value", "kind": "f32" },
              { "name": "min",   "kind": "f32" },
              { "name": "max",   "kind": "f32" } ],
  "opts":   [ { "name": "step",  "kind": "f32", "setter": "SetStep" },
              { "name": "width", "kind": "f32", "setter": "SetWidth" },
              { "name": "grow",  "kind": "f32", "setter": "SetStretchWidth" } ],
  "csharp": { "factory":  "NowLayout.Slider(min, max)",
              "id":       "SetId",
              "consumer": "Draw(ref value)" },
  "result": { "changed": "return", "value": "f32", "flags": "interaction" }
}
```

### 6.2 What stops it drifting: a compile error, not a test failure

`Replay.g.cs` emits **fully explicit C#** — the named method, the named setter, declared argument types, no
`dynamic`, no reflection, no `MethodInfo`:

```csharp
case Op.Slider: {
    float value = F(p + 3), min = F(p + 4), max = F(p + 5);
    var b = NowLayout.Slider(min, max).SetId(Id(p + 1));
    if ((mask & 0x1) != 0) b = b.SetStep(F(o + 0));
    if ((mask & 0x2) != 0) b = b.SetWidth(F(o + 1));
    if ((mask & 0x4) != 0) b = b.SetStretchWidth(F(o + 2));
    bool changed = b.Draw(ref value);
    WriteResult(p + 1, changed, value);
    break;
}
```

If `SetStep` is renamed, if `Draw(ref float)` becomes `Draw(in float)`, if `Slider(float, float)` gains a required
parameter — **the standalone solution does not compile**. Not a red test, not a runtime `MissingMethodException` in
a browser three weeks later: a build break in the same PR that made the change. That is the whole answer to "a
curated surface must be maintained against a moving C# API". The maintenance is neither optional nor deferrable,
because the bridge is a compiled consumer of the API like any other.

That is complemented, not replaced, by three gates:

1. **The existing public-API delta gate** (`StandaloneCoreDesign.md:78`) catches removals and Unity/standalone
   divergence before the bridge ever sees them.
2. **The coverage report is checked in and CI diffs it.** `SurfaceGen` reads `artifacts/local/api/*.api.txt` and
   emits, per public type, every factory and every fluent setter that no spec entry references. Adding a control to
   `Assets/NowUI` makes `M3-SurfaceCoverage.md` dirty; CI regenerates and fails on a non-empty `git diff`. The
   author of the C# change must then either add a spec entry or check in the coverage diff — which is a reviewed,
   visible decision to leave something out, exactly the shape the api-delta gate already established for divergence.
   **Omission becomes a reviewed act rather than an accumulating silence.** That is the only real defence a curated
   surface has against rot, and it is cheap.
3. **A golden-buffer replay test** in a sibling `NowUI.Bridge.Tests` that decodes one hand-built buffer exercising
   all 58 ops inside a real `Now.StartUI` frame and asserts: no throw, a balanced scope stack at the end, every op
   reached, and a stable hash of the emitted draw list. Plus a JavaScript-side test asserting every exported function
   in `nowui.d.ts` has a matching opcode in `Ops.g.cs` and vice versa — the check that catches a spec entry
   generated into one artifact but not the other.

### 6.3 What the gates cannot catch, said plainly

**Semantics.** If a future change makes `NowSlider.SetStep` clamp differently, or makes `NowTextField.Draw` return
`changed` on focus loss rather than on edit, the bridge still compiles, the coverage report is still clean, the
golden buffer still replays — and the JavaScript API now behaves differently from before, with nothing to notice.
Everything tier 1 promises beyond the raw call — value-in/value-out, latched one-shot events, synthesized
`disabled`, mandatory keys, the duplicate detector — is implemented *in the bridge*, so each is a place where the JS
surface can diverge from the C# surface without any C#-side test being able to fail.

The mitigation is behavioural tests on the bridge itself: a headless replay harness that drives synthetic input and
asserts JavaScript-visible outcomes ("after a click on the rect at (612,288), the next frame's result table has
`clicked` on rid X and only on rid X"). That harness is a real line item in §8, not a footnote. **This is the largest
ongoing cost of choosing a curated surface, and it does not go away.**

### 6.4 The escape hatch: tier 2

Tier 1 will not cover something. It is 58 functions against 184 factories and 830 setters, and the gap is by design.
So `SurfaceGen` also emits **tier 2** — mechanically, from the api dump, with no hand-written spec:

```js
ui.x.NowLayout.Chip('tag', { label: 'urgent', style: 'accentSoft' });
ui.x.NowLayout.MaskField('layers', { in: state.layers });   // → result.out.layers
```

The inclusion rule is mechanical and total: a public static factory returning a `[NowBuilder]`, plus its
`Set*`/`With*` setters and its consumers, is included **iff** every parameter is a primitive, string, enum,
`NowRect`, `Vector2/3/4`, `Color`, `NowId`, or a `ref`/`out` of one of those, and every consumer returns `void`,
`bool`, or a result struct whose fields are all of those kinds. By the histogram in M3-SurfaceScout §5.1 that admits
most of the long tail. It excludes — mechanically, with a generated reason string per exclusion printed into the
coverage report — everything carrying a delegate, a generic parameter, `System.Object`, `ReadOnlySpan<char>`, a
managed handle type, or a result holding a live reference.

Tier 2's ergonomics are deliberately worse: no key defaulting, no synthesized `disabled`, no value-in/value-out (a
`ref` argument becomes `{ in: x }` and `result.out.x`), opcodes above 1024, and every call site is a name that can be
misspelled. It exists so the answer to "I need the thing you left out" is "here, and it is ugly" rather than "write
C#". The `.d.ts` still types it, so an editor still autocompletes it.

**And below tier 2 there is C#.** `ui.host(key, name, props, body)` invokes a handler registered on the wasm side by
name. Anything a delegate gates — deferred overlays, `NowDock.Window`, node-graph node content, `NowInspector`
drawers (M3-SurfaceScout §5.3a) — is reachable only this way, and reaching it means somebody writes twenty lines of
C# and rebuilds the wasm module. **An AI that can only write JavaScript cannot do that.** That is the honest boundary
of this design, and §7 lists what falls outside it.

---

## 7. What it cannot do

Stated as decisions, not as gaps to be discovered.

**Structural, from the C# API:**

1. **No user-authored deferred overlays.** `NowOverlay.Defer(NowRect, Action)` and its seven `DrawCallback` forms
   (`NowOverlay.cs:805-1044`) take a delegate. Built-in popups — dropdown, combo box, date and time pickers, context
   menu, tooltips — work fine, because they defer *internally*. A custom floating panel does not.
2. **No docking.** `NowDock.Window(string, Action, …)` (`NowDocking.cs:187,198`) is delegate-driven end to end. The
   Docking extension links and works in the browser; it is not reachable from JavaScript.
3. **No node-graph custom content.** `SetNodeContent(Action<NowNode, NowRect>)` (`NowNodeGraph.cs:3889`) and
   `drawCustomItems` (`:3248`). A node graph with default node rendering is reachable through tier 2; a node graph
   with content *inside* the nodes is not.
4. **No `NowInspector`.** `Draw<T>(ref T)` is generic over an arbitrary struct and `Draw(object)` reflects over a CLR
   object (`NowInspector.cs:98,113`). Neither has a JavaScript analogue.
5. **No `NowControlState.Get<T>`.** It returns `ref T` where `T : struct` (`NowControlState.cs:77`) — an interior
   reference to a generic value type, which cannot cross any boundary. A JavaScript author cannot attach custom
   per-control state to a NowUI id; they keep it in their own object, keyed however they like.
6. **No custom SDF material.** `NowSdfBuilder.SetMaterial` already cannot work in this host — the browser backend
   throws on any program outside the ten hand-ported ones (`M2-FeatureMatrix.md:78-82`). Unchanged here, restated so
   nobody rediscovers it.
7. **No model preview.** `Now.Model`/`NowModelPreview` is excluded from the standalone build entirely
   (`StandaloneCoreDesign.md:1675`) and recorded in the public-API delta.
8. **No `ReadOnlySpan<char>` overloads.** The 24 span overloads (`NowText.cs:618-705` and the numeric formatters) are
   unreachable; the string overload beside each is used instead. Cost: one managed string allocation per distinct
   volatile string per frame.
9. **No live-object results.** `NowRichTextResult.layout` is a reference type and `TryHit` is a method on it
   (`NowRichText.cs:30,39-48`); `NowMarkupResult` borrows an event buffer that throws when read after a redraw
   (`NowMarkupState.cs:61-69`). Rich-text hit testing and markup event buffers are not in the result table and
   cannot be.
10. **No native disabled state, because NowUI has none.** There is no `SetEnabled`, no `BeginDisabled`, no disabled
    scope anywhere in `Assets/NowUI/Runtime` (grepped; the whole of `NowButton`'s fluent surface is `SetOptions`,
    `SetWidth`, `SetHeight`, `SetStretchWidth`, `SetId`, `SetNavigation`, `SetStyle`, `SetTextStyle`,
    `SetAlignItems`). `{ disabled: true }` is **synthesized by the bridge**: it forces `NowRectangleStyle.Ghost`,
    mutes the text, and discards the interaction result. The consequences are real and are documented at the option —
    a disabled control is still hoverable, still focusable, still in the keyboard navigation order, and still shows a
    focus ring. If that matters, do not draw it.

**Architectural, from this design:**

11. **JavaScript's model of a value is one frame behind the pixels**, always, and there is no settle mode (§5.2).
12. **The same subtree cannot be drawn twice in one frame.** Ever. Two calls with the same path throw.
13. **`ui.*` is synchronous-only.** No `await` in a draw function, no drawing from a timer, promise, or event
    handler. Change `state` and let the next frame draw it.
14. **A control's rect is not in the result table unless asked for.** `bool Draw()` discards the rect
    (`NowControlBuilders.cs:139`; M3-SurfaceScout fact 11); `{ rect: true }` switches that control to the `Begin()`
    path, which is a heavier call, so it is opt-in per control rather than always on.
15. **Ephemeral state has a ten-second memory.** A control not drawn for more than ten seconds loses its caret,
    selection, undo history and scroll offset (`NowControlState.cs:35`). Values are unaffected — they are in
    JavaScript.
16. **Anonymous scopes are positional.** Toggling a sibling scope with a bare `if` shifts ordinals and discards
    ephemeral state below it. `ui.when` and keyed scopes are the two ways out, and debug mode reports it (§2.4).
17. **No custom themes or fonts beyond names.** `NowThemeAsset` (91 parameter occurrences) and `NowFontAsset` (21)
    are managed reference types; `ui.theme('dark', body)` selects from what the host's resource manifest already
    provides. Authoring a theme from JavaScript is not offered.
18. **No IMGUI.** `NowGUI`/`NowGUILayout` are compiled and reachable from C#; they are not mirrored.
19. **Tier 1 omits, by count, most of the library.** 58 functions cover roughly 30 of 55 builders. Not in tier 1:
    node graph, docking, inspector, model, Lottie, gradient field, animation-curve field, key-binding field, mask
    field, vector field, and the `NowDrawList`/`NowEffects`/`NowSnapshot` families. Some are in tier 2; the rest are
    in the coverage report with a machine-generated reason.
20. **The C# escape hatch is not available to a JavaScript-only author.** `ui.host` requires somebody to write and
    compile C#. This is the price of Angle B, and it is charged in exactly the places listed as items 1-4.

---

## 8. Work breakdown to a first working application

"First working application" means: `app.js` from §1 renders, is clickable, and its state round-trips.

| # | Slice | Contents | Done when |
|---|---|---|---|
| **W1** | **Transport** | `Standalone/NowUI.Bridge` project, referencing `NowUI.Runtime` only. `BridgeInterop.Record`, the four memory views, the `TextEncoder.encodeInto` string path, buffer growth and the `-needed` retry. Verify the re-entrant `MemoryView` assumption of §4.2 before anything is built on it; fall back to the proven `double[]` path if it does not hold. `nowui.js` recorder core: `emit`, `scope`, path hashing, intern table. One hard-coded `OP_TEXT`, no controls. | A wasm frame calls JavaScript, JavaScript writes six slots, and the word "hello" appears on the canvas. The entire transport risk is retired first. |
| **W2** | **Identity and scopes** | `IdScope(string)` / `IdScope(int)` / `KeyedItemIn` push and pop in the decoder, mirrored by the recorder's path stack. The rid hash. The duplicate-key `Set` and its message. The `finally` scope wrapper, depth limit, outside-the-frame guard. `ui.column`, `ui.row`, `ui.group`, `ui.list`, `ui.when`. | Two buttons with the same key in different columns coexist; two in the same column throw with the full path in the message. |
| **W3** | **The result table** | Record layout, `WriteResult` from the decoder, measure-pass suppression, the JavaScript `Map` loader, one-shot latching, the value-in/value-out rule, the not-drawn drop. `ui.button`, `ui.textField`, `ui.slider`. | `if (ui.button('Add')) …` fires exactly once per click, and `state.name = ui.textField('name', state.name)` round-trips a typed character. **The example's core loop runs here.** |
| **W4** | **Faults and reload** | JS throw → balanced partial buffer → error banner → warn-once latch → 120-frame stop. `NowRuntime.ResetAll` wired to a hot-reload path so editing `app.js` re-runs without a page load. The `?nowui=debug` diagnostics: ordinal-shift report, missing-result report, one-frame-key report. | Deleting a `)` in `app.js` shows a banner naming the line instead of a blank canvas, and fixing it recovers without a reload. |
| **W5** | **The generator and its gates** | `Tools/Standalone/SurfaceGen`. `surface.json` filled out to all 58 entries. All six generated artifacts. CI: regenerate + `git diff --exit-code`. Golden-buffer replay test. The `.d.ts` ↔ `Ops.g.cs` symmetry test. **The behavioural harness of §6.3** — synthetic input in, JavaScript-visible outcomes asserted. | Renaming a setter in `Assets/NowUI` on a scratch branch breaks the standalone build; adding a factory makes the coverage file dirty. |
| **W6** | **Tier 2 and the long tail** | Mechanical generation from the api dump with §6.4's inclusion rule, a generated exclusion reason per omitted member, the `ui.x.*` namespace, the `ui.host` handler registry. | A control in no spec entry is callable as `ui.x.NowLayout.<Name>(...)`, and every exclusion has a reason in the report. |
| **W7** | **The author's documentation** | The generated reference plus one hand-written page: the five rules of §1.1, the identity rule of §2.3, the one-frame rule of §5.2, and three complete example apps. Written for an AI reading it as context — no prose without a code block beside it, every function shown at a call site, and §7's limitations phrased as rules rather than caveats. | An AI given only that page and no other context writes §1's application, correctly, on the first attempt. **That is the milestone's actual acceptance test.** |

W1-W3 produce the first working application. W4-W5 are what make it safe to hand to somebody. W6-W7 are what make
it a product.

**Sequencing note.** The generator arrives in W5, *after* three slices of hand-written code, deliberately. Writing
the recorder and decoder by hand for six opcodes is what establishes the shape the generator should emit; generating
first would fix the wrong shape into a template before anything had run. The hand-written six are regenerated and
deleted in W5.

---

## Appendix A — a frame, end to end

The `Remove` button in the example, from click to effect.

```
frame 41  input.Drain()     pointer-up at (612, 288) enters the provider
          Record → JS       state.team has 3 rows; JS emits ops for all of them.
                            Results from frame 40 carry no clicked flag on any rid.
          Replay            RunMeasured measure pass: passive, no results written.
                            RunMeasured real pass:
                              IdScope("roster") → KeyedItemIn("team","grace") → IdScope(0)
                              NowLayout.Button("Remove").SetId(new NowId("Remove"))
                                .SetStyle(NowRectangleStyle.Danger).Draw()  →  true
                              WriteResult(rid = 0x8f3a…, flags |= clicked)
          EndFrame          the row is still on screen, and looks pressed

frame 42  Record → JS       results.get(0x8f3a…).clicked === true, consumed on read
                            ui.button('Remove') returns true
                            state.team = state.team.filter(...)      → 2 rows
                            state.status = 'Removed Grace Hopper.'
                            JS emits ops for 2 rows plus the note
          Replay            two rows drawn; the note appears
```

Elapsed: one frame, 16 ms. Slots crossed: 214 out, ~140 back. Boundary crossings: 2.

## Appendix B — the six keyed scopes, and why exactly those

| Scope | State it owns in `NowControlState` | Consequence of losing it |
|---|---|---|
| `ui.scroll` | scroll offset | the list jumps to the top |
| `ui.tree` | expansion set | every node collapses |
| `ui.tabs` | selected tab | the user is thrown back to tab 1 |
| `ui.foldout` | open / closed | a section the user opened closes |
| `ui.split` | pane ratio | the panes snap back to 50/50 |
| `ui.codeEditor` | caret, selection, undo, scroll | the cursor jumps to the start of the file |

Every other scope in tier 1 — `column`, `row`, `card`, `panel`, `list`, `when`, `group` — owns nothing in
`NowControlState`, so its identity matters only as a path prefix for the controls beneath it, and a positional
prefix is sufficient. `group` takes an optional key precisely so an author can promote a positional prefix to a
stable one when §2.4's ordinal-shift case bites.

## Appendix C — files this design creates

```
  Standalone/NowUI.Bridge/                      new project, references NowUI.Runtime
    BridgeInterop.cs                            the JSImport and the buffers
    Recorder.cs                                 scope stack, string interning, result table
    Generated/Replay.g.cs                       the decode switch            (generated, W5)
    Generated/Ops.g.cs                          opcode + signature tables    (generated, W5)
  Standalone/NowUI.Bridge.Tests/                golden buffers, behavioural harness
  Standalone/Surface/surface.json               the hand-written spec
  Tools/Standalone/SurfaceGen/                  the generator
  Standalone/Web/NowUI.Web/
    BridgeHost.cs                               wires the bridge into Program.Frame()
    wwwroot/nowui.js                            the 58 functions             (generated, W5)
    wwwroot/nowui.d.ts                          types                        (generated, W5)
    wwwroot/app.js                              the example application
  Docs/Standalone/M3-Surface.md                 the reference                (generated, W5)
  Docs/Standalone/M3-SurfaceCoverage.md         what is not covered          (generated, W5)
```

`Assets/NowUI`, `Assets/NowUITests` and `Assets/NowUIHarness` are not touched. `Standalone/NowUI.Engine` is not
touched either: the bridge needs `NowRuntime.BeginFrame`/`EndFrame`/`ResetAll`, and all three are already public
(`NowRuntime.cs:151,191,208`).
