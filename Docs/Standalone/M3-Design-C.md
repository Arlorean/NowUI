# M3 Design C — NowTree: a described tree over the immediate core

**Designer C. Angle: declarative.** This document argues that the JS surface should *not* mirror the immediate-mode
builder API, designs the alternative in full, and names what it costs.

Facts are cited to `Docs/Standalone/M3-SurfaceScout.md` (written for this milestone) as **[S §n]**, and to source as
`file:line`. Paths are relative to `D:/wkspaces/unity/Now-UI/`. Nothing under `Assets/NowUI` changes.

---

## 0. The argument in one page

`StandaloneCoreDesign.md` §4.8 proposes a JS mirror of the builder API: JS records builder calls, wasm replays them,
JS reads results from a previous-frame table. That design has one hard problem and one soft one.

The hard problem is identity. NowUI derives control identity from `[CallerFilePath]`/`[CallerLineNumber]` captured at
the factory (`NowControlFactories.cs:196-199`, **[S §2.3]**), interned to an `int` site token, and — when no explicit
id was given — salted by draw-order occurrence (`NowControls.cs:526-529`, `:563-580`). JavaScript can supply none of
that. A call-stream mirror must therefore invent identity *at call time*, from nothing but the order of the calls,
which is precisely the input that changes when a branch flips. **[S §7.3]**: explicit ids are never salted, a
duplicate is a silent state-share, and the only guard is `#if UNITY_EDITOR || DEVELOPMENT_BUILD`
(`NowControls.cs:606-625`) — compiled out of a Release wasm build.

The soft problem is that every value the author reads is one frame old, and there is no way to hide that from someone
writing `if (button()) count++`.

A tree fixes both, and it fixes them for the same reason the Markup extension already works. **[S §6.5.1]** states it
exactly: *"Markup does not solve this problem — it inherits a solution from having a document."* A document has a
structure that exists before the frame, so an element's position in it is a stable name
(`NowMarkupDocument.cs:480-484`: an author `id`, else `"markup:<tag>:<sourceIndex>"`). That is the declarative
analogue of a call site, and it is strictly better than one, because it nests.

So: **give JavaScript a document.** Not a markup string — a tree of plain objects, built fresh every frame by
ordinary JS code, submitted as one binary blob, and walked by a generated C# replayer that opens and closes real
scopes with real `using` statements.

What that buys, concretely:

| Problem | Call-stream mirror | Tree |
|---|---|---|
| Identity without a call site | invented from call order | the node's key path — computed before the frame, nestable, author-overridable |
| Duplicate ids | silent state share, no Release guard | detected in JS at build time, before anything crosses |
| Reordering a list | breaks silently | keys follow the data; and an *unkeyed* reorder is detectable and reported |
| `using` / scope pairing | JS must pair `begin`/`end` and cannot be trusted to | there are no begin/end calls; nesting is object nesting |
| JS throws mid-frame | half a frame is already in the buffer | nothing was submitted; last good tree redraws |
| Idempotent replay (**[S §7.14]**, required by `NowLayout.RunMeasured`) | a stream that mutates `ref` values double-applies | a tree is pure data; replay it as many times as you like |
| Delegate-taking APIs (`NowOverlay.Defer`, `NowDock.Window`) | unreachable — the callback must run inside a C# call | the callback's content is a subtree; the replayer walks it from inside the closure |
| `if (button()) count++` | works, one frame stale | **does not exist.** `onClick` + rebuild, one frame of latency |

The last row is the price and it is the only one. Everything else in the table is a win. Section 7 lists the rest of
the losses without decoration.

The transport stays a per-frame binary buffer — §4.8 was right about that — but it carries **nodes, not calls**, it
has **no begin/end opcodes**, and it is **resubmitted byte-identical on idle frames**. There is no result table on
the read path at all (§5).

The design's name in this document is **NowTree**. It lives at `Standalone/Web/NowUI.Web/Bridge/` (C#) and
`Standalone/Web/NowUI.Web/wwwroot/nowui/` (JS), with the generator at `Tools/Standalone/JsSurfaceGen/`.

---

## 1. The API, shown not described

A task list: a text field, a dropdown, a slider per row, a keyed list that grows and shrinks, and buttons that do
something. This is the entire application — there is no other file.

```js
// app.js
import { mount, h, ref } from './nowui/nowui.js';

const draftField = ref();

const state = {
  draft: '',
  priority: 1,
  tasks: [
    { id: 't1', title: 'Port the glass shader',   priority: 3 },
    { id: 't2', title: 'Write the surface scout', priority: 0 },
  ],
};

let nextId = 3;
const PRIORITIES = ['Low', 'Normal', 'High', 'Urgent'];

function addTask() {
  const title = state.draft.trim();
  if (title === '') return;

  state.tasks.unshift({ id: 't' + nextId++, title, priority: state.priority });
  state.draft = '';

  // The field owns its text (see section 5). This is how you write to it.
  draftField.setValue('');
  draftField.focus();
}

const removeTask = (id) => { state.tasks = state.tasks.filter(t => t.id !== id); };

function taskRow(task) {
  return h('row', { key: task.id, gap: 8, alignItems: 'center' }, [
    h('label',  { text: task.title, textStyle: 'body', stretchWidth: true }),
    h('slider', { min: 0, max: 3, width: 120, value: task.priority,
                  onChange: v => { task.priority = Math.round(v); } }),
    h('badge',  { text: PRIORITIES[task.priority], width: 68 }),
    h('button', { text: '\u00d7', width: 28, style: 'ghost',
                  onClick: () => removeTask(task.id) }),
  ]);
}

function app() {
  return h('column', { padding: 16, gap: 12, stretchWidth: true, stretchHeight: true }, [

    h('label', { text: 'Tasks', textStyle: 'title' }),

    h('row', { key: 'entry', gap: 8, alignItems: 'center' }, [
      h('textfield', { key: 'draft', ref: draftField,
                       placeholder: 'What needs doing?', stretchWidth: true,
                       onChange: v => { state.draft = v; },
                       onSubmit: addTask }),
      h('dropdown',  { key: 'priority', options: PRIORITIES, width: 110,
                       value: state.priority,
                       onChange: i => { state.priority = i; } }),
      h('button',    { key: 'add', text: 'Add', onClick: addTask }),
    ]),

    h('scroll', { key: 'list', stretchWidth: true, stretchHeight: true, gap: 6 },
      state.tasks.length === 0
        ? [h('label', { key: 'empty', text: 'Nothing to do.', textStyle: 'muted' })]
        : state.tasks.map(taskRow)),

    h('label', { key: 'count', text: `${state.tasks.length} open`, textStyle: 'caption' }),
  ]);
}

mount('#nowui-canvas', app);
```

That is 60 lines and it is a complete, running NowUI application: real SDF text, real theming, real focus navigation,
real text editing with IME and undo, drawn by WebGL2.

### 1.1 What is going on, line by line

**`h(type, props, children)`** builds a plain object `{ t, k, p, c }`. It allocates nothing else, touches no wasm, and
runs no NowUI code. It is a description. `h` is the only construction function in the API; the ~120 node types are a
string table, not 120 imported symbols. That is deliberate: an AI writing this file needs one function signature and
a name list, and the name list is in a generated `.d.ts` (§6).

**Nothing returns a result.** `h('button', ...)` returns a node, not a click. The click arrives at `onClick`. There
is no `if (button())` in this API and there never will be — see §7.1.

**Values are owned by the control unless you say otherwise.** `textfield` here has no `value` prop, so the wasm-side
value store owns the string (§5.2). Typing echoes at zero latency because NowUI edits its own copy inside the frame
and draws it; `onChange` tells JS about it afterwards. The `slider` and `dropdown` *do* pass `value`, so they are
controlled: the prop overwrites the store each frame, and if a handler fails to write the new value back, the control
snaps back. This is React's controlled/uncontrolled split, with the same rules and the same reasons, and §5.2 says
exactly when each one is wrong.

**Keys.** `key: task.id` on the row is the whole of the list-identity story. Every other node here is unkeyed and
gets an automatic key from its type and its ordinal among same-typed siblings (`label#0`, `slider#0`, …), which is
safe because those sibling sets have a fixed shape. §2 says precisely when that stops being safe and what warns you.

**The empty-list branch is keyed on both sides** (`key: 'empty'` vs `key: task.id`). That is the pattern §2.4
prescribes for conditionals, and the one place in this file where an author has to think.

**`ref`.** A `ref` is the escape hatch for the two things a description cannot express: writing into a control the
control owns (`setValue`, `focus`, `scrollTo`) and reading back geometry (`draftField.current.rect`). §5.3 defines it
and states that `current` describes the frame that was last *drawn*, which is one frame behind the tree you are
building.

**`mount`** installs a `requestAnimationFrame` loop and returns a handle. There is no `render()` call to remember and
no dirty flag to set by hand in the common case: the bridge marks itself dirty whenever it dispatches an event, which
covers every user interaction. `ui.invalidate()` exists for state that changes outside a handler (a `fetch`
completing, a `setInterval`); `ui.animate(true)` rebuilds unconditionally every frame for a UI driven by a clock.

### 1.2 The same file as JSX

`h` has the React element signature, so a build step is optional rather than required:

```jsx
/** @jsx h */
<row key="entry" gap={8} alignItems="center">
  <textfield key="draft" ref={draftField} placeholder="What needs doing?" stretchWidth
             onChange={v => state.draft = v} onSubmit={addTask} />
  <dropdown key="priority" options={PRIORITIES} width={110}
            value={state.priority} onChange={i => state.priority = i} />
  <button key="add" text="Add" onClick={addTask} />
</row>
```

One difference from JSX convention, and it matters enough to be the loudest line in the documentation:

> **A falsy child holds its slot.** `{cond && <button/>}` yields `false`, and NowTree keeps that `false` as an
> ordinal placeholder that draws nothing, instead of dropping it. Its siblings therefore keep their automatic keys
> when the condition flips.

React drops falsy children and pays for it with the well-known "index as key" state-loss bug. NowTree cannot afford
that bug, because the failure mode here is not a re-mounted component — it is two controls silently sharing focus and
edit state (`NowControls.cs:457-462`). §2.4 covers this in full.

### 1.3 Containers with content that reads its own interaction

Some NowUI controls are containers whose *opening* produces a result — `NowButton.Begin()` returns a
`NowControlScope` carrying `clicked`, `focused`, `rect` and `interaction` as public readonly fields
(`NowControlBuilders.cs:315-322`, **[S §7.10]**). In NowTree that is not a special case: a `button` node with
children is replayed through `Begin()` instead of `Draw()`, and the scope's `clicked` becomes the same `onClick`
event the childless form produces.

```js
h('button', { key: 'save', onClick: save }, [
  h('lottie', { asset: 'spinner', height: 18 }),
  h('label',  { text: saving ? 'Saving…' : 'Save' }),
])
```

The author does not learn that `Draw()` and `Begin()` are different methods. That is the point of a description.

---

## 2. Identity

### 2.1 The rule

> **A node's identity is the path of keys from the mount root to that node. Each key is unique among its siblings.**

Nothing else participates. Not the node's type, not its props, not its position in the buffer, not the order in which
the replayer reached it.

A key is:

1. the node's explicit `key` prop, if present — any non-empty string not containing `#`; or
2. `"<type>#<ordinal>"`, where the ordinal counts *preceding siblings of the same type*.

Rule 2 is the declarative analogue of `[CallerLineNumber]` and it is the same trick Markup plays with `sourceIndex`
(`NowMarkupDocument.cs:480-484`), improved in one way: Markup's namespace is flat (`"markup:<tag>:<index>"` over the
whole document), so two id-less buttons in different panels can collide. NowTree's is a path, so siblings only have
to be unique among siblings.

### 2.2 How the path reaches NowUI

Not by concatenating a string. The tree is mirrored into NowUI's own identity tree, one `ControlIdScope` per
container node:

```csharp
// Generated — Bridge/NowTreeReplay.Generated.cs, the Column case
case NodeType.Column:
{
    NowId key = f.Key(node);                       // interned string, zero allocation (§4.3)
    using (NowControls.IdScope(key))               // identity nesting  — NowControls.cs:211
    using (NowLayout.Column().SetId(key)
                    .Gap(f.F32(node, P.Gap, 0f))
                    .Padding(f.V4(node, P.Padding))
                    .AlignChildren(f.Enum<NowLayoutAlign>(node, P.AlignItems))
                    .Begin())                      // layout nesting    — NowLayoutContainers.cs:190
        ReplayChildren(f, node);
    break;
}
```

Two scopes, because they are two different things. `ControlIdScope` pushes onto `NowControls._idStack`, which is what
`CurrentIdentityParent()` reads (`NowControls.cs:110-113`) and therefore what every control id underneath derives
from. A layout group id does **not** push identity — layout lives in `NowIdDomain.Layout` and control ids in
`NowIdDomain.Control` (**[S §2.2]**), and the two cannot alias by construction (`Identity.md:48-52`). Passing the
same key to both means the layout cache is as stable as the identity, which matters for the deferred sizes in
**[S §4.4]**.

Leaf controls take the key directly:

```csharp
case NodeType.Slider:
{
    NowResolvedId id = NowControls.GetControlId(f.Key(node));   // NowControls.cs:476, public
    ref float v = ref f.values.Float(id, f.F32(node, P.DefaultValue, 0f));
    if (f.Has(node, P.Value)) v = f.F32(node, P.Value, v);      // controlled

    var s = NowLayout.Slider(f.F32(node, P.Min, 0f), f.F32(node, P.Max, 1f)).SetId(id);
    ApplySliderProps(ref s, f, node);
    if (s.Draw(ref v))
        f.events.Number(node, EventKind.Change, v);
    break;
}
```

Resolving once through the public `NowControls.GetControlId(NowId)` and passing the result to `SetId(NowResolvedId)`
does two jobs with one hash: it keys the value store (§5.2) and it identifies the control. It must produce the same
`NowResolvedId` as `SetId(NowId)` would have — both funnel through
`NowControls.GetControlId(NowId, fallback) → CurrentIdentityParent().Derive(NowIdDomain.Control, id)`
(`NowControls.cs:498-504`) with the fallback unused because the id has a value. **That equality is asserted by a test
in `Standalone/Tests`, not assumed** (§8, slice S2).

Four consequences worth stating:

* **Occurrence salting never runs.** Salting only applies when identity falls back to the call site
  (`NowControls.cs:526-529`). Every NowTree control has an explicit id, so the `_labelOccurrences` /
  `_passiveOccurrences` tables stay empty for bridge-drawn controls, and the flat-per-frame counter semantics
  (**[S §2.7]**) are irrelevant to this design. Good: those are exactly the semantics that break under reordering.
* **`IdScope(NowId)` is never called with a default.** **[S §7.5]**: a default id makes `IdScope` a silent no-op
  that pushes nothing (`NowControls.cs:213-214`), flattening identity. The encoder guarantees every node carries a
  non-empty key, and the decoder rejects a buffer where one does not.
* **`IdScope(NowResolvedId)` is never used.** It *replaces* the ambient path rather than nesting under it
  (`NowControls.cs:126-138`, **[S §7.6]**). NowTree only ever nests.
* **Two mounts on one page do not collide.** `mount()` takes a name (defaulting to the canvas selector) and the
  replayer opens `NowControls.IdScope(mountName)` as the outermost scope inside `Now.StartUI`.

`KeyedItemIn(listId, key)` (`NowControls.cs:260`) was considered and rejected: it is a two-level namespace where
NowTree already has a general n-level one, and its list-membership semantics buy nothing the `IdScope` chain does not
already give. Noted rather than dismissed — if focus restoration across a list edit turns out to behave differently
under `KeyedItem`, that is a bug report against this decision, and slice S3's focus test is where it would show up.

### 2.3 What happens when a list reorders

Nothing. `key: task.id` means the identity of a row follows its datum, so `tasks.reverse()` moves every row's
rendered position and moves none of its state: a text field in row `t7` keeps its caret, its selection and its undo
stack, because the path `list/t7/notes` is unchanged.

This is the case a call-stream mirror cannot get right without the author manually supplying the same key at every
call, and the case Markup explicitly cannot get right — `Markup.md:240` reduces it to an authoring rule: *"No
reliance on implicit IDs for controls that appear in lists or can move."* NowTree turns that rule into something the
bridge can check (§2.5).

### 2.4 What happens when a conditional adds or removes a control

Three cases, three behaviours.

**(a) A falsy child.** `[a, cond && b, c]` — when `cond` is false the middle child is `false`, and NowTree keeps it as
a placeholder occupying one ordinal slot of type `<none>`. `a` and `c` keep their automatic keys either way, because
ordinals count *preceding siblings of the same type* and a placeholder has no type. Nothing is lost.

The placeholder costs 16 bytes in the buffer and one `switch` case that does nothing.

**(b) A branch that swaps one subtree for another.** The empty-list branch in §1:

```js
state.tasks.length === 0
  ? [h('label', { key: 'empty', ... })]
  : state.tasks.map(taskRow)
```

Both branches produce keyed children, so switching between them is a clean identity change: `list/empty` appears,
`list/t1`… disappear. NowUI evicts the vanished controls' state after ten seconds untouched
(`NowControlState.cs:35`), which is what you want.

Written *without* keys — `[h('label', {text: 'Nothing to do.'})]` against `tasks.map(row => h('row', ...))` — it also
works, because the two branches produce different *types* (`label#0` vs `row#0`, `row#1`, …) and different types never
share a key. The dangerous shape is a branch that produces the **same type in a different count**, and §2.5 is what
catches it.

**(c) A filtered array.** `items.filter(visible).map(...)` with no keys is the failure. Filtering shifts every
subsequent ordinal, so `row#3` becomes `row#2` and inherits the scroll position, focus and edit state of a different
row. This is exactly the silent corruption the brief warns about, and NowTree's answer is not "don't do that" — it is
§2.5.

### 2.5 How the author is warned

Three checks, in order of cost.

**Check 1 — duplicate sibling keys. JS, every build, throws.**

Building a container's child list already walks the children to assign ordinals. The same walk keeps a `Set` of
assigned keys; a second occurrence of a key throws immediately:

```
NowUI: two children of "list" both have key "t3".
  Duplicate ids share focus, edit state and interaction — NowUI will not disambiguate them.
  at app.js:41  (taskRow)
```

This runs **before anything crosses the boundary**, so it is a JS stack trace pointing at the author's own line, not
a C# warning about a hash. It closes the hole **[S §2.5]** describes: NowUI's own duplicate-id check lives behind
`#if UNITY_EDITOR || DEVELOPMENT_BUILD` and is compiled out of a Release wasm build, and even in Debug it only fires
for ids that reach `NowControls.Interact`, so two colliding `Label`s are never reported at all. NowTree's check has
no build-configuration gate and does not care whether the node is interactive.

Cost: one `Set` per container per frame — in practice one `Set` per *depth*, cleared and reused, so a 300-node tree
costs 300 `Set.add` calls, roughly 20 µs. It is on in Release. There is a `mount(..., { validate: false })` and the
documentation says what you gave up.

**Check 2 — unstable automatic keys. JS, on change, warns once per path.**

The bridge keeps, per container path, the previous frame's key vector. When it changes, it classifies the change:

* pure append or truncate at the end — fine, no report;
* every changed slot carries an explicit key — fine, the author is in control;
* **any changed slot is automatic** — report once for that path:

```
NowUI: the children of "root/list" changed shape and slot 2 ("row#2") has an automatic key.
  It is now drawing different data than last frame, and it kept the previous control's state
  (focus, caret, scroll position, hover animation).
  Give these children explicit keys: h('row', { key: item.id }, ...).
  Previous: [row#0, row#1, row#2, row#3]
  Now:      [row#0, row#1, row#2]
```

This is the single strongest reason to prefer this design. The failure the brief names — *"it makes two controls
share state, or makes a control forget its state when a branch changes, which looks like a flaky UI rather than a bug
in the bridge"* — becomes a named path, a slot index, a before/after vector, and a one-line fix. There is no
equivalent check available to a call-stream mirror, because a call stream has no containers and no sibling vectors to
compare.

Cost: one string array per container, compared elementwise, retained across frames; ~40 bytes per container. On in
Release, warn-once per path, capped at 64 distinct reports per session so a pathological UI cannot flood the console.

**Check 3 — collision at the NowUI level. wasm, dev builds, warn-once.**

NowUI's own `CheckDuplicateControlId` (`NowControls.cs:606-625`) still runs in the standalone Debug configuration
(`DEVELOPMENT_BUILD` is defined there — `StandaloneCoreDesign.md:65`). It is kept as a backstop for bridge bugs, not
as an author-facing diagnostic: if it ever fires while checks 1 and 2 are silent, the bridge is wrong, and the message
is prefixed accordingly. Its `NowResolvedId` is mapped back to a node path through a wasm-side
`Dictionary<NowResolvedId, int>` the replayer fills, so the report names `root/list/t3/notes`, not a hash.

### 2.6 Where identity still breaks, honestly

* **An author who reuses a key for genuinely different data** — `key: index` over a reordering array — gets no
  warning from check 1 (the keys are unique) or check 2 (they are explicit). Identity follows the key, the key
  follows the position, and state follows the position. This is unfixable by the bridge; it is the React `key={i}`
  mistake and it has the same cure (documentation, and a lint rule if the project ever grows one).
* **A key that changes every frame** (`key: Math.random()`, `key: Date.now()`) re-mounts the control every frame:
  focus is lost each frame, hover transitions restart, text edit state is discarded, and `NowControlState` fills with
  entries that live ten seconds each (`NowControlState.cs:35`). Check 2 catches this on the first frame it happens,
  because every slot changed while the keys are explicit — that case reports as *"every child key changed; keys that
  change every frame re-mount the control"*.
* **A control drawn inside a subtree that runs twice** (`exact: true`, §7.6 / `NowLayout.RunMeasured`) is drawn twice
  with the same id in one frame. NowUI handles this correctly — the measure pass is passive and the occurrence tables
  rewind (`NowControls.cs:563-580`, `:628-671`) — but the *bridge* must not emit two events for one interaction. It
  does not: event collection is guarded by `!NowInput.isPassive`, the same condition `submitted` already uses
  (`NowTextField.cs:333-337`).
* **`NowResolvedId` handles never reach JS.** Its constructor is `internal` and it has no public value constructor
  (`NowResolvedId.cs:15`, **[S §7.19]**), so there is nothing to leak. JS names things with strings; the wasm side
  owns every resolved id. This is a constraint the design happily accepts.

---

## 3. Scopes without `using`

### 3.1 There are no begin/end calls

This section is short because the design deletes the problem rather than solving it.

JavaScript never opens or closes a scope. A container is a node with children; the child array's brackets *are* the
begin/end pair, and JavaScript balances brackets in the parser, before any of this code runs. The wasm-side replayer
walks the tree recursively with ordinary C# `using` statements (§2.2), so every scope NowUI hands out is disposed by
the CLR in strict LIFO order on the normal path and on the exception path alike.

This satisfies **[S §3.6]**'s six facts without any bridge machinery:

1. *Out-of-order disposal throws* (`NowScopeGuard.cs:54-61`) — recursion cannot produce out-of-order disposal.
2. *A scope left open across `Now.StartUI` throws* for layout/theme/id/input/GUI/drawlist scopes
   (`Now.cs:1279-1293`) — the recursion returns to depth zero before `StartUI`'s `using` closes, or it throws and the
   `using`s unwind (§3.4).
3. *`Begin()` scopes carry results out of the opening call* — read at the point the scope opens, before recursing
   (§1.3).
4. *`Now.StartUI` cannot nest* (`Now.cs:1273-1277`) — the replayer is called once per frame, from one place.
5. *`NowContextMenu` is a bare `Begin`/`Item`/`End` API with no disposable type* (`NowContextMenu.cs:304`, `:390`,
   `:504`) — replayed with `try`/`finally` instead of `using`, which the generator emits for the handful of types
   that have no `IDisposable`.
6. *Scope structs are copyable value types whose disposed copies are no-ops* — irrelevant here; nothing is copied.

It also covers the two public disposables that carry no `[NowScope]` and would be missed by an attribute-driven
generator (**[S §3.5]**): `Now.TextContextScope` (`NowTextPreprocessor.cs:70-77`, whose leak is *not* reset by
`BeginScreenFrame`) and `NowNodeGraphEvaluator<T>.BatchScope`. Neither is discovered by reflection over `[NowScope]`;
both are in the hand-written policy file (§6) as node types whose replay is a `using`, and the generator's
completeness check (§6.4) is what stops a third one appearing unnoticed.

### 3.2 What "getting it wrong" can even mean

The author cannot mispair a scope. The three failure modes that remain:

* **A malformed tree** — a `children` array on a node type that takes none, a child of `dropdown`, `width` on a root
  container that has an explicit rect (which throws `InvalidOperationException` from
  `NowLayoutContainer.RequireNestedPlacement`). All three are *prop legality*, checked in JS against the generated
  manifest at build time, with the node path in the message. The manifest records, per node type, whether it accepts
  children and which props are legal only in nested placement.
* **A cyclic structure** — `const a = h('row', {}, []); a.c.push(a);`. The encoder walks depth-first with a depth cap
  (default 128, configurable) and a visited set in dev mode; exceeding either throws in JS with the path so far.
* **A node object mutated after being handed to `h`** — undefined behaviour, documented as such. The encoder reads
  each node exactly once.

### 3.3 When JS throws mid-frame

This is the case the brief asks about and it is where the design is strongest.

The build function runs to completion **in JavaScript, producing an object graph, before one byte is encoded**. So a
throw inside `app()` — a `TypeError` on `undefined.title`, a bad `map`, anything — means the encoder was never
called, the buffer was never submitted, and the wasm side has not been touched.

```js
function tick() {
  dispatch(pendingEvents);                      // handlers may throw too — caught, see below
  if (dirty) {
    let next = null;
    try { next = build(); encode(next, buffer); }
    catch (e) { reportOnce(e); }                // `buffer` keeps the last good tree, byte for byte
    if (next) tree = next;
    dirty = false;
  }
  pendingEvents = decodeEvents(nowui.frame(bufferLength));
  requestAnimationFrame(tick);
}
```

The consequences, stated as guarantees:

* **NowUI never sees a partial frame.** No scope was opened, no control was drawn, no identity was pushed. There is
  no state to recover, so none of `BeginScreenFrame`'s seven recovery steps (`Now.cs:1271-1357`) is ever exercised by
  a JS error. Those recovery paths remain what they are: a diagnostic for a bug in the *bridge*.
* **The UI freezes on the last good frame rather than disappearing.** The previous buffer is still in memory and is
  resubmitted byte-identical, so animations, hover states and text editing continue to work against the last
  successfully built tree. That is a much better failure than a blank canvas, and it is only available because the
  transport is a whole tree rather than a stream of calls.
* **A handler that throws does not stop the other handlers.** Dispatch wraps each handler in `try`/`catch`, reports
  once per handler identity, and continues — losing a whole frame's input because one row's delete button threw is
  worse than losing that one action.
* **The error is surfaced, not swallowed.** `reportOnce` logs with the node path and, if `mount({ errorBanner: true
  })` (the default in a dev build), pushes an error node into the tree so the failure is visible on the canvas rather
  than only in a console the end user will never open. The banner is itself a NowTree subtree, drawn by the same
  replayer.

### 3.4 When the *replay* throws

A different problem, wasm-side. A NowUI control can throw: `NowId` rejects an empty string (`NowId.cs:52-53`),
`IdScope(string)` rejects null/empty (`NowControls.cs:199-200`), a container rejects an illegal option, an extension
hits an unported shader.

The replayer has one `try`/`catch` for the whole walk, inside `Now.StartUI`'s `using`, plus a `_currentNode` field
updated per node:

```csharp
NowRuntime.BeginFrame();
using (Now.StartUI(scale))
using (NowControls.IdScope(_mountName))
{
    try { Replay(f, 0); }
    catch (Exception e) { f.Poison(_currentNode, e); }
}
NowRuntime.EndFrame();
```

`using` unwinds every open scope correctly — that is what `using` is for, and it is why the scopes are held in
`using` rather than in a bridge-managed stack. The frame finishes clean, `StartUI` disposes normally, and the next
frame does not hit any of `Now.cs:1279-1293`'s throws.

`Poison` records the node's key path in a `HashSet<string>`, reports it to JS as an `error` event, and the replayer
skips that path (and its subtree) on subsequent frames. The cost is one bad frame — the nodes after the failing one
are not drawn that frame, so the UI flashes once — then a stable UI with a hole in it and a message naming the hole.
Skipping is cleared by `ui.reset()` and by hot reload.

Per-node `try`/`catch` was considered and rejected: it costs an exception filter per node for a case that should never
happen, and it would let a broken node silently do nothing forever.

---

## 4. The command stream

### 4.1 Shape

One buffer per frame, written by JS into memory the managed side owns, submitted with one call. It carries a
**pre-order node array with explicit child counts** — no begin/end opcodes, no matching to get wrong, and a shape the
decoder can validate in a single pass before it opens a single scope.

```
Frame buffer (little-endian, all sections 4-byte aligned)

  Header               16 B
    u32  magic         0x544F574E  'NOWT'
    u16  version
    u16  flags         bit0 stringsAppended · bit1 exactLayout · bit2 validated
    u32  nodeCount
    u32  totalBytes

  Node table           nodeCount x 16 B, pre-order
    u16  type          index into the generated NodeType table (0 = placeholder)
    u16  childCount
    u32  key           string id, or 0x8000_0000 | (typeOrdinal << 8) for an automatic key
    u32  propOffset    byte offset into the prop blob
    u16  propCount
    u16  flags         bit0 hasHandlers · bit1 controlled · bit2 reported (has a ref)

  Prop blob            sum(propCount) x 8 B
    u16  prop          index into the generated PropId table
    u8   kind          F32 | I32 | Bool | Str | StrInline | Enum | Vec2 | Vec4 | Color | Rect | StrList
    u8   _
    u32  payload       the value itself for F32/I32/Bool/Str/Enum; otherwise an arena offset

  Arena                Vec2/Vec4/Color/Rect values, inline UTF-8 strings, string-list tables

  String appendix      only when flags.stringsAppended; (u32 id, u32 byteLength, utf8...) records
```

A node is 16 bytes. A prop is 8. The §1 application is 14 nodes with an empty list and 14 + 4·n with n tasks; at 20
tasks that is 94 nodes and ~500 props: **1.5 KB + 4 KB = 5.5 KB per frame**, 330 KB/s at 60 Hz.

A large real UI — a docked editor with a 300-node tree and 6 props per node — is 4.8 KB + 14 KB ≈ **19 KB per frame**,
1.1 MB/s. That is a memcpy of 19 KB per frame, which on any browser that can run WebGL2 is tens of microseconds. The
buffer is not the frame budget; the replay is.

### 4.2 There is no diff, on purpose

NowTree serializes the whole tree every frame and reconciles nothing. Four reasons, in order of weight:

1. **The renderer is immediate.** Every control is re-executed every frame regardless of whether its description
   changed. A diff would shrink the transport and shrink nothing else, and the transport is already ~1% of the frame.
2. **Identity comes from keys, not from diff matching.** In React the diff *is* the identity mechanism, so the diff
   has to be right. Here identity is a pure function of the tree, so there is no reconciliation to get wrong and no
   class of "the diff matched the wrong node" bugs.
3. **Diffing costs JS CPU.** Comparing 300 nodes and 1800 props in JS is plausibly *more* work than memcpying 19 KB.
4. **Idempotence.** **[S §7.14]**: a measure pass replays the same UI twice per frame with identity rewound, so the
   buffer must be replayable more than once, identically, with no side effects. A whole-tree buffer of pure data has
   that property by construction. A diff-based protocol whose meaning depends on prior state does not.

What replaces the diff is cheaper and covers the same cost:

> **On a frame where nothing is dirty, JS does not rebuild and does not re-encode. It calls `frame(previousLength)`
> and the wasm side replays the bytes already in the buffer.**

An idle frame therefore costs zero JS allocation, zero encoding and zero copying — one managed call. Since a NowUI
frame must run anyway (hover transitions, caret blink, `NowControlState.Transition`, deferred layout convergence),
this is exactly the right shape. Dirty is set by: dispatching any event, `ui.invalidate()`, a `ref` mutation, and
unconditionally while `ui.animate(true)`.

### 4.3 String interning

Strings are the third-most-common parameter type in the API (389 occurrences, **[S §5.1]**) and the only unbounded
one. The rule:

> **A string is interned on its second sighting. Its first sighting is encoded inline.**

JS keeps `Map<string, {id, seen}>`. On the first sighting `seen = 1` and the string goes into the arena as
`StrInline`. On the second it is assigned the next id, appended to the string appendix for that frame, and from then
on costs 4 bytes.

That single rule handles both populations correctly:

* **Static text** — labels, placeholders, option lists, style names, keys — is interned on frame 2 and costs 4 bytes
  per frame thereafter, forever. The first frame of an application is the only one that pays for its text.
* **Volatile text** — `` `${tasks.length} open` ``, a clock, a frame-time readout — is never seen twice, so it is
  never interned and never pollutes the table. No heuristic, no `volatile: true` prop, no eviction pass.

Wasm side: `string[] _strings` indexed by id, appended in the same order. Because ids are stable and the array holds
the *same string instance* every frame, `new NowId(key)` allocates nothing and `NowIdHash` hashes the same reference
every time — which is precisely the reference-identity fast path `SiteToken` already exploits for
`[CallerFilePath]` literals (`NowControls.cs:373-376`). The bridge inherits that optimisation for free.

Ceiling: ids are u31; the practical cap is a configurable 65 536 (`mount({ maxStrings })`). Overflow is a hard error
naming the ten most recently interned strings, because a UI that mints 65 536 *repeated* strings is deriving identity
from data and that is a bug worth stopping. Ids are never reused within a session; `ui.reset()` and hot reload clear
both sides together.

### 4.4 Buffer growth

JS owns an `ArrayBuffer` and a `DataView` over it, 64 KB initially. On overflow it doubles, copies, and calls
`nowui.ensureCapacity(bytes)` so the managed `byte[]` matches; the managed side re-hands its `ArraySegment<byte>` view
to JS, and JS drops the old one. Growth happens O(log n) times per session and never in steady state. Shrinking never
happens automatically; `ui.compact()` exists and is documented as something you will not need.

### 4.5 Boundary crossings per frame

Counting *managed transitions* — a JS→wasm call that runs C# — not memcpys:

| Step | Mechanism | Managed transitions |
|---|---|---|
| Write the frame into managed memory | `view.set(bytes, 0)` on a `MemoryView` | 0 (JS-glue memcpy) |
| Submit and run the frame | `[JSExport] int Frame(int byteLength)` | **1** |
| Read events back | `resultView.slice(0, n)`, `n` = `Frame`'s return | 0 (JS-glue memcpy), skipped when `n == 0` |
| Drain DOM input | existing `drain()` + `drainCharacters()` (`WebInput.cs:403,407`) | 2 |

**One managed transition for the UI; three per frame in total**, unchanged from today's input bridge plus one. An
idle frame is one transition and zero bytes moved.

The mechanism: at start-up the managed side allocates `byte[] _in` and `byte[] _out` and hands JS a
`JSType.MemoryView` over an `ArraySegment<byte>` of each. An `ArraySegment` marshalled as a memory view is pinned and
its JS-side handle stays valid until disposed, so the handles are obtained once and reused for the session. JS writes
with `view.set(...)` and reads with `view.slice(0, n)`.

**Named risk.** This depends on `[JSMarshalAs<JSType.MemoryView>]` over `ArraySegment<byte>` remaining valid across
calls in .NET 9's wasm interop, and on `set`/`slice` being available on the JS-side proxy. Slice S1's first
acceptance test is a five-line spike that writes 1 MB through the view, calls a no-op export, and reads it back —
before any of the rest is built. If the view turns out to be per-call only, the fallback is
`[JSMarshalAs<JSType.Array<JSType.Number>>] double[]`, which is what `WebInput.Drain` already uses
(`WebInput.cs:403-405`): one managed array allocation and one element-wise conversion per frame — measurably worse
(a 19 KB frame becomes ~2 400 doubles) but not fatal, and still one transition.

### 4.6 Events out

`Frame` returns the byte length written into `_out`.

```
  Event                16 B
    u32  node          index into the node table JS just submitted
    u16  kind          Click | Change | Submit | Focus | Blur | DragStart | DragEnd | Error | Report
    u16  valueKind     None | F64 | Bool | I32 | Str | Rect
    u64  payload       the f64 value, or (arenaOffset, byteLength) for a string
```

`node` is an index into *the array JS submitted on this call*, and `Frame` is synchronous, so JS still holds that
exact array. Dispatch is `tree.flat[e.node].p.onClick(...)` — a direct array index, no path lookup, no map.

The out-buffer also carries the per-node **reports** (§5.3) and the string arena for changed text-field contents.
Typical frame: 0 events. Interaction frame: 1-3. A drag frame: 1 per moving control.

### 4.7 Where a call-stream mirror would be cheaper, and where it would not

Honesty: a call-stream mirror sending only *changed* setters could be smaller on a static frame. It could not be
smaller on an interactive one, it could not be idempotent, and it would need begin/end opcodes with a matching
discipline. NowTree trades ~19 KB of memcpy for the deletion of an entire class of correctness problems. At 60 Hz on
hardware that runs WebGL2, that is not a close call.

---

## 5. The result table — and why there is not one on the read path

### 5.1 Events replace synchronous reads

**[S §4.5]** describes a previous-frame result table and its hazards: one frame of latency, two frames for
context-menu clicks and dropdown selections (already one frame late in C# — `NowContextMenu.cs:346`,
`NowDropdown.cs:14-15`), entries that go stale for controls that stopped being drawn, and a lifetime that must match
`NowControlState`'s ten-second eviction (`NowControlState.cs:35`).

NowTree does not have that table because it does not have that read. The author never asks "did this get clicked";
they say what to do when it is. The frame protocol:

```
  rAF(N)
    1. JS   dispatch the events frame N-1 produced   -> handlers mutate JS state, set dirty
    2. JS   if dirty: build() and encode()
    3. JS   nowui.frame(len)                          -> ONE managed call
    4. wasm BeginFrame; StartUI; replay; collect events; EndFrame
    5. JS   keep the returned events for rAF(N+1)
```

The user's click is drained by frame N-1's input pass, reported by frame N-1's replay, handled at the top of frame N,
and drawn by frame N.

> **One frame. Always. 16.7 ms at 60 Hz.**

That is the number, it is the same number a call-stream mirror's result table pays, and it is the only latency this
design adds anywhere. What is different is that the author is never invited to read a stale value and reason about
it — the staleness is absorbed into "your handler ran between two frames", which is where every JS author already
expects their code to run.

Compounding is the same as **[S §4.5]** states and is not made worse: a dropdown selection is one frame late in C#,
so its `onChange` lands two frames after the physical click. This is documented per node type in the generated
`.d.ts`, on the member itself, so it is visible at the point of use:

```ts
/** Fires when the selection changes. NowUI applies popup selection on the next frame's draw,
 *  so this lands 2 frames after the click (NowDropdown.cs:14-15). */
onChange?: (index: number) => void;
```

### 5.2 Values: who owns them

**[S §5.3d]** names `ref`/`out` as the pervasive obstacle: ~30 `Draw(ref T)` value controls, every `Interact`
overload with `out bool focused, out bool submitted`. A buffer has no aliased caller variable, so something must own
the value between frames.

**The bridge owns it.** A wasm-side value store keyed by `NowResolvedId`, modelled on `NowMarkupState`
(`NowMarkupState.cs:158-383`) — which **[S §6.5.5]** identifies as the closest thing in the repository to an answer —
but typed rather than loosely cross-parsing:

```csharp
sealed class NowTreeValues
{
    readonly Dictionary<NowResolvedId, int> _slots;   // id -> index
    float[] _f;  int[] _i;  bool[] _b;  string[] _s;  byte[] _kind;
    int[] _touchedFrame;

    public ref float Float(NowResolvedId id, float seed) { ... }   // ref into _f[slot]
}
```

`ref` into an array element is a real `ref float`, so `slider.Draw(ref store.Float(id, seed))` compiles and the
control mutates the store directly. A `Dictionary` cannot hand out a `ref` to its value; an index into a parallel
array can. That is the whole trick.

Eviction mirrors NowUI's: a slot untouched for ten seconds is freed, matching
`NowControlState.EVICT_AFTER_SECONDS` (`NowControlState.cs:35`) so the two stores cannot disagree about whether a
control exists. **[S §4.5]**'s warning — *"a result table has to have the same lifetime discipline or it will serve
stale `clicked` values"* — applies to the value store and is met by construction.

**Controlled vs uncontrolled**, and when each is wrong:

* **Uncontrolled** (no `value` prop): the store owns the value; `defaultValue` seeds it the first frame the node
  appears; `onChange` reports changes. **This is the default and the recommendation for text.** Typing echoes with
  *zero* latency, because NowUI edits the store's string inside the frame and draws it in the same frame; only JS's
  copy is one frame behind. Programmatic writes go through `ref.setValue(v)`, which enqueues a command applied at the
  top of the next replay, before the control draws.
* **Controlled** (a `value` prop): the prop overwrites the store every frame before `Draw`. Correct for low-rate
  discrete values — dropdown index, checkbox, radio group, tab index — where the round trip is invisible.

  **Controlled text fields drop characters and must not be used.** The failure is exact. You type "abc"; frame N's
  `Draw` sets the store to "abc" and fires `onChange("abc")`; JS's handler runs at the top of frame N+1 and sets
  `state.text = "abc"`; frame N+1 writes the prop "abc" into the store *before* `Draw`, and the "d" typed during N+1's
  input drain is applied on top — fine. The case that is not fine is any frame where the handler did **not** run or
  ran late — a dropped frame, an early return, an async setState — where the prop resets the field to a value older
  than what was typed. At 60 Hz with a synchronous handler it works; at 20 Hz on a loaded page it visibly eats
  characters. The `.d.ts` for `textfield.value` says so, and `mount({ strict: true })` throws on it.

`out bool focused` / `out bool submitted` are not values the author owns; they are results, and they become the
`onFocus`/`onBlur`/`onSubmit` events and the `focused` field of a report.

### 5.3 `ref`: the one place a value is read, and what it means

Some things genuinely must be read rather than handled: a rect (to position a DOM overlay, an HTML `<video>`, a
tooltip anchor), whether a control is focused, a scroll offset. For those, `ref` — opt-in, per node, costing nothing
on nodes that do not use it.

```js
const chart = ref();
...
h('panel', { key: 'chart', ref: chart, stretchWidth: true, height: 240 })
...
// anywhere, any time:
const r = chart.current;         // { frame, rect: {x,y,w,h}, hovered, focused, ... } | null
if (r) positionOverlay(r.rect);
```

The rules, each a decision:

* **`current` describes the frame that was last drawn**, which is one behind the tree you are currently building. It
  carries `frame` (the `NowRuntime.frameCount` that produced it) so `r.frame === ui.frame - 1` is a freshness test
  the author can write. This matches `ref.current` in React (also post-render), which is why the name was chosen.
* **`current` is `null` before the node's first draw**, so reading `.rect` off it throws a `TypeError` at the
  author's own line. In `strict` mode `current` is a throwing proxy with a better message: *"ref for \"root/chart\"
  has not been drawn yet; refs describe the previous frame."* This is the mirror image of Markup's `_eventsVersion`
  guard (`NowMarkupState.cs:57-70`) — **[S §6.5.6]** asks for exactly this, observing that markup guards "read after
  redraw" while the JS case needs "read before first draw".
* **`current` becomes `null` one frame after the node stops being drawn**, matching **[S §4.5]**: *"a control that
  is not drawn produces no entry."* It does not go stale and silently keep answering.
* **It never throws merely for being one frame old.** That is the normal, documented case; throwing would make the
  feature unusable.
* **Refs are opt-in.** A node without `ref` sets no `reported` flag and produces no out-buffer record. A 300-node
  tree with two refs writes two records.

`ref` also carries the imperative escapes, which are commands queued into the *next* replay rather than reads:
`setValue(v)`, `focus()`, `blur()`, `scrollTo(x, y)`, `selectAll()`. They are applied at the top of the replay before
the node draws, so their effect is visible in the very next frame.

### 5.4 What a report can and cannot carry

**Can** — everything **[S §4.3]** lists as same-frame, because it is computed synchronously inside the replay:
`rect`, `hovered`, `pressed`, `held`, `released`, `clicked`, `dragging`, `dragStarted`, `dragEnded`,
`pointerPosition`, `dragDelta`, `focused`, `submitted`, `changed`, and the control's current value.

**Cannot** — named here rather than faked:

* `NowRichTextResult.layout` is a `NowRichTextLayout` reference type, and `TryHit(Vector2, out NowRichTextHit)`
  requires calling *into* a live object (`NowRichText.cs:30`, `:39-48`). A report can carry the hit under the pointer
  *this frame*, which is what a click handler needs; it cannot answer an arbitrary hit query from JS.
* `NowMarkupResult` borrows the document's event buffer and throws when read after the document redraws
  (`NowMarkupState.cs:61-69`). NowTree copies markup events into its own event stream during the replay rather than
  handing out the borrowed view.
* `NowSplitViewResult.BeginFirst()` is a *method* on the result, not data (`NowSplitView.cs:200`). It is expressed as
  tree structure — a `splitview` node with exactly two children — not as a readable result.

---

## 6. How the surface is produced and kept honest

### 6.1 Generated, from a checked-in policy, with a build-breaking consistency gate

Three artifacts, one generator, one source of truth for each half.

```
Assets/NowUI (frozen)
   |
   |  reflection over NowUI.Runtime + the 7 extension assemblies
   v
Tools/Standalone/JsSurfaceGen  <---- Bridge/surface-policy.json   (checked in, hand-written)
   |
   +--> Bridge/nowui-surface.json          the manifest        (checked in, reviewed in diffs)
   +--> wwwroot/nowui/nowui.generated.js   node & prop tables, validators
   +--> wwwroot/nowui/nowui.d.ts           types, per-node docs lifted from the C# XML docs
   +--> Bridge/NowTreeReplay.Generated.cs  the decode-and-call switch
```

Hand-written, and only these: `nowui.js` (the ~400-line runtime — `h`, `mount`, the encoder, key assignment, the two
validators, dispatch, `ref`), `NowTreeDecoder.cs`, `NowTreeValues.cs`, `NowTreeEvents.cs`, and `surface-policy.json`.

### 6.2 Why the attributes cannot be the source

`StandaloneCoreDesign.md` §4.8 names *"the `[NowBuilder]`/`[NowConsumer]`/`[NowScope]` attributes as the reflection
source for generating the JS surface."* **[S §1.5]** shows that is insufficient, and this design says so rather than
discovering it in the generator:

* The attributes mark **return types, never entry points**. `NowControlFactories.cs` — 358 lines containing every
  control factory in the library — carries zero attributes. 184 public statics return a builder and 60 return a
  scope; not one is marked.
* `[NowConsumer]` is decorative on 9 of 38 methods (**[S §1.3]**), and the single most common terminal call in the
  library — `NowButton.Draw()` (`NowControlBuilders.cs:132`) — carries no attribute at all, because a `bool` is not a
  struct the analyzer would flag.
* Whole subsystems a JS app cannot work without have no builder: `NowContextMenu` is a bare `Begin`/`Item`/`End` API
  with no disposable type (**[S §1.5b]**), and `NowControls`, `NowInput`, `NowFocus`, `NowTheme`, `NowOverlay` and
  `NowViews` are plain static classes.
* Two public disposable scopes carry no `[NowScope]` (**[S §3.5]**), one of them generic.

So the generation source is the **public API surface**, which `Tools/Standalone/ApiDump/Program.cs` already produces
and `StandaloneCoreDesign.md:78` already designates as the governance artifact.

The attributes keep a job, and it is a better one: **a completeness ratchet.** The generator asserts that

* every node type in the policy names a type carrying `[NowBuilder]`, or appears on a named exception list with a
  reason string (`NowContextMenu`: "no builder type — imperative Begin/End API");
* every `[NowBuilder]` type in the assemblies is either covered by the policy or explicitly excluded with a reason;
* every `[NowScope]` type reachable from a covered node is replayed inside a `using`.

Adding a new control to the frozen tree therefore fails the generator with *"NowSparkline carries [NowBuilder] and is
neither covered nor excluded"* rather than quietly not existing in JS.

### 6.3 What the policy file contains

One entry per node type, ~120 entries, hand-written because §6.2's facts mean it cannot be derived. Abridged:

```jsonc
{
  "button": {
    "type":     "NowUI.NowButton",
    "factory":  "NowUI.NowLayout.Button(string)",
    "consumer": { "leaf": "Draw()", "container": "Begin()" },
    "identity": "SetId",
    "events":   { "click": "return-value|scope.clicked", "focus": "scope.focused" },
    "props": {
      "text":         { "arg": 0 },
      "width":        { "setter": "SetWidth(float)" },
      "stretchWidth": { "setter": "SetStretchWidth(float)", "default": 1.0 },
      "style":        { "setter": "SetStyle(NowUI.NowRectangleStyle)",  "enum": "NowRectangleStyle" },
      "textStyle":    { "setter": "SetTextStyle(NowUI.NowTextStyle)",   "enum": "NowTextStyle" },
      "alignItems":   { "setter": "SetAlignItems(NowUI.NowLayoutAlign)","enum": "NowLayoutAlign" }
    },
    "propOrder": ["options", "*"],
    "children":  "optional"
  },

  "column": {
    "type":     "NowUI.NowLayoutContainer",
    "factory":  "NowUI.NowLayout.Column()",
    "consumer": { "container": "Begin()" },
    "identity": "SetId",
    "idScope":  true,
    "props": {
      "gap":          { "setter": "Gap(float)",       "alias": "spacing" },
      "padding":      { "setter": "Padding(Vector4)", "coerce": "sides" },
      "alignItems":   { "setter": "AlignChildren(NowUI.NowLayoutAlign)", "enum": "NowLayoutAlign" },
      "width":        { "setter": "Width(float)",     "nestedOnly": true },
      "stretchWidth": { "setter": "FillWidth()",      "nestedOnly": true }
    },
    "children": "required"
  }
}
```

Five fields do real work:

* **`propOrder`** — props are order-independent *except* where two setters write the same field. `SetOptions`
  replaces the whole `NowLayoutOptions` struct while `SetWidth` writes one field of it, so `{width: 120, options:
  {...}}` must be deterministic. `propOrder` fixes a priority (whole-struct setters first, `*` for the rest); the
  encoder sorts by it, so JS object key order never matters. Verified applicable: every setter on `NowButton`,
  `NowTextField` and `NowLayoutContainer` is a pure field write returning `this`.
* **`nestedOnly`** — `NowLayoutContainer.Width/Height/Min*/Max*/Fill*/Grow/AlignSelf` call `RequireNestedPlacement`
  and **throw** on a root container that was given an explicit rect. The JS validator rejects those props on a root
  node with a message naming the alternative, so the throw is unreachable.
* **`alias`** — exactly one renaming mechanism, declared here and nowhere else, because a rename invented in the JS
  runtime is drift by definition. Both names are emitted in the `.d.ts`. There are three: `gap`→`Gap/spacing`,
  `stretchWidth`→`FillWidth`, `alignItems`→`AlignChildren`.
* **`coerce`** — `padding: 8` / `[h, v]` / `[l, t, r, b]` → `Vector4`, and `color: '#3b82f6'` / `[r,g,b,a]` →
  `Color`. Named coercions, listed in the manifest, implemented once.
* **`identity`** — whether the type takes `SetId`. The 16 identity-free builders (`NowLabel`, `NowRectangle`,
  `NowCircle`, `NowGlass`, … — **[S §2.6]**) declare `"identity": null` and the generator asserts they have no `SetId`
  overload. Their nodes still get keys (for `ref`, for reports, for the sibling checks) but the key is not handed to
  NowUI, because there is nothing to hand it to.

### 6.4 What stops it drifting

Five gates, in CI, all failing the build:

1. **The generator is a consistency test.** It resolves every `factory`, `consumer`, `setter` and `enum` in the
   policy against the actual assemblies by full signature. A renamed setter, a changed parameter type, a removed
   overload, a new required parameter → *"surface-policy.json: button.props.style names SetStyle(NowRectangleStyle),
   which no longer exists on NowUI.NowButton"*. This is the primary gate and it covers the whole `Assets/NowUI`
   surface the bridge touches.
2. **The manifest is checked in and regenerated in CI.** `JsSurfaceGen --check` regenerates and diffs; a difference
   fails. A change in the frozen tree that alters the surface shows up as a reviewable diff in `nowui-surface.json`,
   not as a silent behaviour change.
3. **The completeness ratchet** (§6.2): every `[NowBuilder]` is covered or excluded-with-a-reason.
4. **The public-API delta gate already exists** (`StandaloneCoreDesign.md:78`, §1.2) and is unchanged. NowTree adds
   nothing to it and depends on it: if the delta gate passes, signature drift is already impossible without review;
   if it is bypassed, gate 1 catches what the bridge cares about.
5. **A replay-equivalence test per node type.** For each node type, `Standalone/Tests` draws the JS-shaped tree
   through the replayer and draws the equivalent hand-written C# through the same frame, both against
   `NowRecordingRenderBackend` (`StandaloneCoreDesign.md` §4.6), and **diffs the op logs**. That backend exists
   precisely for this kind of check and the core design already calls the diff *"the cheapest possible correctness
   check"*. If a NowUI change alters what a control draws, both sides change together and the test still passes; if
   the bridge stops calling a control the way C# does, the logs diverge and it fails. Headless, no browser, no GPU.

### 6.5 The coverage report

`JsSurfaceGen --report` emits a table in the shape of `M2-FeatureMatrix.md`: every public builder, factory and static
subsystem, and for each one *covered / excluded (reason) / uncovered*. Checked in, and the honest answer to "what of
NowUI can JavaScript actually reach". A design that claims a surface without publishing that number is claiming
something it has not measured.

---

## 7. What it cannot do

Listed plainly. Each is a decision, not an oversight.

1. **`if (button()) count++` does not exist, and no amount of work will make it.** Anything that reads a control's
   result and branches on it *within the same frame* must become `onEvent` + state + rebuild, at one frame of
   latency. This is the design's central trade and it cannot be bought back.

2. **Immediate mode's directness is genuinely gone.** You cannot compute a value, draw it, read the interaction it
   produced, and use that to decide what to draw next — all in one pass. A chart that emits a segment per data point
   and thickens the hovered one needs the hover from the previous frame (available via a report), so the highlight is
   one frame behind the pointer. In C# it is not. That is a real loss and it is felt most in exactly the
   custom-drawing code immediate mode is best at.

3. **`onChange` for a dropdown, combo box or context-menu item is two frames after the physical click**, not one.
   One frame is NowUI's (`NowDropdown.cs:14-15`, `NowComboBox.cs:15`, `NowContextMenu.cs:346` — *"true when it was
   clicked (the frame after the click)"*) and one is the bridge's. Documented on each member in the `.d.ts`.

4. **Controlled text fields drop characters** under frame-rate pressure (§5.2). The design's answer is to make
   uncontrolled the default and to throw in `strict` mode, not to fix it — it is not fixable without a synchronous
   round trip.

5. **There is no `disabled`.** NowUI's builders have no enabled/disabled concept — `NowButton` exposes
   `SetOptions/SetWidth/SetHeight/SetStretchWidth/SetId/SetNavigation/SetStyle/SetTextStyle/SetAlignItems` and
   nothing else. The bridge does not invent one, because an invented `disabled` would either be a lie (drawn normally
   but ignoring clicks, with no visual affordance) or an unrequested theme decision. Authors write the branch
   themselves, or use `style: 'ghost'` and a no-op handler. If NowUI grows a disabled state, the policy file gains a
   prop that day.

6. **Delegate-taking APIs: four of six work, two do not.** **[S §5.3a]** counts 21 `Action` parameters gating four
   capabilities.
   * **Work**, because a subtree is data the replayer can walk from inside a closure: `NowOverlay.Defer` /
     `DeferScreen` (`NowOverlay.cs:805`, `:858`) → an `overlay` node; `NowDock.Window(string, Action)`
     (`NowDocking.cs:187`) → a `dockwindow` node; `NowNodeGraphCanvas.SetNodeContent(Action<NowNode, NowRect>)`
     (`NowNodeGraph.cs:3889`) → a `nodecontent` child; `NowLayout.RunMeasured` (`NowLayout.cs:1493`) → `exact: true`
     on a container, the subtree replayed twice, which is safe precisely because the buffer is pure data (§4.2).
   * **Do not work**: `NowInspector.SetDrawer<T>(NowInspectorDrawer<T>)` — a generic delegate returning `bool` and
     taking `ref T`; and `NowNodeGraphCanvas.drawCustomItems`, a `Func<..., bool>` whose return value steers C#
     control flow mid-call. A JS function cannot be called synchronously from inside a wasm frame and return a value
     into it. Excluded permanently.

7. **`NowInspector.Draw(object target)`** reflects over an arbitrary CLR object (`NowInspector.cs:113`,
   **[S §5.3g]**). There is no JS analogue. Excluded permanently, as is `NowInspector.Draw<T>(ref T)`.

8. **`NowControlState.Get<T>` is not exposed** — it returns `ref T` and is generic over `struct`
   (`NowControlState.cs:77`, **[S §7.17]**), unrepresentable across the boundary in both directions. JS state lives
   in JS; per-control state NowUI owns stays owned by NowUI.

9. **Generic controls lose their generics.** `NowEnumDropdown<TEnum>` / `NowEnumFlags<TEnum>` cannot be closed over a
   JS type. They are replaced by `dropdown` / `flags` nodes taking an explicit `options: string[]`, which is what a
   JS author would write anyway — but the enum's names, ordering and flag values must be supplied by JS and can drift
   from the C# enum with nothing checking it.

10. **Managed reference types are handles, not values.** `NowThemeAsset` (91 parameter occurrences), `NowFontAsset`,
    `Material`, `Gradient`, `AnimationCurve`, `NowLottieAsset`, `NowTreeViewState`, `NowNodeGraph`, `NowDrawList`,
    `Camera`, `CommandBuffer` (**[S §5.3e]**). JS names them with strings resolved through the resource provider; JS
    cannot construct or mutate one. A custom theme is authored in C# or loaded as an asset — not written as a JS
    object. That is a real limitation for the "an AI writes an app" story, and the honest mitigation is a small,
    explicitly listed set of themeable tokens exposed as a `theme` node, not the whole `NowThemeAsset`.

11. **`ReadOnlySpan<char>` overloads are unreachable** (24 occurrences, notably the eight `NowText.Draw` overloads,
    `NowText.cs:618-705`). The string overloads exist beside every one of them, so nothing is lost functionally; the
    zero-allocation text path is.

12. **No virtualized lists in v1.** A 10 000-row tree is 160 KB of nodes per frame — fine — but 10 000 `IdScope`
    pushes and hash derivations per frame, real CPU inside the replay, plus 10 000 controls NowUI actually draws. The
    intended answer is a `scroll` node with `virtual: { count, rowHeight }` that reports its visible range through a
    `ref` so JS emits only the visible rows. Designed for, not built in the first pass. Until it exists the practical
    ceiling is a few thousand nodes.

13. **`Now.TextContextScope` leaks are not recovered.** `Now.TextContext(string)` (`NowTextPreprocessor.cs:64-77`)
    pushes onto a static list, and **[S §3.5]** records that nothing in `BeginScreenFrame` resets `_textContexts`.
    NowTree always disposes it (it is a `using` in the replay), so it cannot leak from the bridge — but if it ever
    did, the process is permanently wrong and nothing detects it.

14. **Accessibility is nil.** The canvas is opaque to screen readers and nothing in this design changes that. For a
    milestone framed as "an AI writes an app and serves it to someone", that is worth stating out loud even though it
    is out of scope: a person using a screen reader cannot use the output of this design at all.

15. **Hot reload loses control-owned values.** `NowRuntime.ResetAll` plus clearing the value store and the string
    table is the reload, and it means text a user had typed into an uncontrolled field is gone. Recoverable only by
    JS having mirrored it through `onChange` and re-seeding with `defaultValue`, which the documentation recommends
    and nothing enforces.

16. **The buffer is a copy, not shared memory.** Without `SharedArrayBuffer` (which needs cross-origin isolation
    headers the host may not control), every frame's bytes are copied once. Measured cost is small (§4.1), but it is
    not zero and it does not go away.

17. **One tree, one canvas, one `Now.StartUI` per frame** (`Now.cs:1273-1277`, **[S §7.9]**). Multiple mounts on one
    page share a frame and draw in mount order into the same canvas; they are not independent surfaces.

---

## 8. A work breakdown to a first working application

Done means **the §1 task list running in a browser**: keyed, with a list that grows and shrinks, typing that echoes,
and a slider that drags — plus the tests that keep it honest. Nine slices; each ends with something runnable.

**S0 — Skeleton and policy (small).**
`Bridge/surface-policy.json` for twelve node types: `column`, `row`, `panel`, `label`, `button`, `textfield`,
`slider`, `checkbox`, `dropdown`, `badge`, `scroll`, `spacer`. `nowui-surface.json` hand-written to match, so
everything downstream can be built before the generator exists.
*Done when:* the manifest exists and a human has read it against `NowControlFactories.cs`.

**S1 — Transport spike, then the codec.**
First the five-line `MemoryView` spike from §4.5 — write 1 MB from JS, call a no-op export, read it back. **If it
fails, the fallback lands here, not in slice 6.** Then the encoder (JS) and `NowTreeDecoder` (C#): header, node table,
prop blob, arena, string appendix, growth, validation.
*Done when:* a round-trip test in `Standalone/Tests` encodes a fixture tree, decodes it, and compares node for node;
and a malformed buffer (bad child-count sum, truncated arena, key 0) is rejected before any NowUI call.

**S2 — Replayer and identity.**
The recursive walk, `IdScope` + layout scope per container, `NowTreeValues` with `ref`-into-array slots, the twelve
node cases hand-written (they become generated in S5, but writing them by hand first is what tells you whether the
policy schema is right).
*Done when:* (a) `NowControls.GetControlId(new NowId(k))` equals the id `SetId(new NowId(k))` resolves to, asserted
for nested scopes — the load-bearing assumption of §2.2; (b) the op-log diff of §6.4 gate 5 passes for all twelve
types against hand-written C#; (c) a tree replayed twice in one frame produces identical op logs (idempotence,
**[S §7.14]**).

**S3 — Events, reports, refs.**
Event collection guarded by `!NowInput.isPassive`, the out-buffer, `ref` reports, the imperative command queue
(`setValue`/`focus`/`scrollTo`).
*Done when:* a headless test drives a synthetic pointer through `INowInputProvider`, replays two frames, and asserts
the click event arrives with the right node index; and a focus test asserts that reordering a keyed list does not
move focus.

**S4 — The JS runtime.**
`h`, `mount`, key assignment, the duplicate-sibling check, the reorder detector, the falsy-child placeholder,
dispatch, the rAF loop, error containment (§3.3), `ui.invalidate`/`animate`/`reset`.
*Done when:* the reorder detector fires on `items.filter().map()` with no keys and stays silent with keys, proven by
a Node-hosted unit test that needs no wasm at all — the JS half is testable on its own and should be.

**S5 — The generator.**
`Tools/Standalone/JsSurfaceGen`: reflection, policy resolution, the four outputs, `--check`, `--report`, and the five
gates of §6.4 wired into CI.
*Done when:* the twelve hand-written replay cases from S2 are deleted and regenerated byte-comparably, and
deliberately renaming a setter in a scratch copy of the assembly fails the generator with a useful message.

**S6 — The application.**
The §1 file, `index.html`, served by the existing web host. `Program.cs` gains a `?app=` selector alongside `?area=`
(the `FeatureGallery.cs` pattern), so the JS app is one more selectable area and the existing gallery is untouched.
*Done when:* it runs; tasks add and delete; the field clears and refocuses; the slider drags; and reordering the array
moves neither focus nor a caret.

**S7 — Surface expansion.**
The rest of the control library, driven by the coverage report: switch, radio, tab bar / tab view, splitter / split
view, combo box, date and time pickers, colour picker, tree view, progress bar, chip, foldout, plus the drawing
primitives (rect, circle, line, polygon, gradient, glass, text) and the extensions with a JS-expressible shape
(markdown, markup, code editor).
*Done when:* the coverage report's *covered* count stops rising and every remaining row has a written reason.

**S8 — Delegate nodes.**
`overlay`, `dockwindow`, `nodecontent`, `exact` (§7.6). These need the replayer to be re-entrant from inside a closure
and are deliberately last, because they are the only place the recursion is not straight-line.

**S9 — Virtualization** (§7.12) and the `theme` node (§7.10).

Ordering rationale: S1's spike is first because it is the only unknown that can invalidate the transport; S2's
identity assertion is second because it is the only unknown that can invalidate the design. Everything after those
two is construction.

---

## Appendix A — alternatives considered and rejected

**A JS mirror of the builder API (the §4.8 proposal).** Rejected for the reasons in §0. It is not unworkable — it is
workable, and the identity mechanism it needs is the same key path this design uses, just supplied by hand at every
call instead of derived from structure. The tree gets the same answer for free and gets the scope problem and the
error-containment problem thrown in.

**Markup strings from JS.** `NowMarkup.Document(string)` already exists and already works. Rejected because
**[S §6.5.4]** is decisive: markup is a closed vocabulary of ~35 tags and ~25 style properties that explicitly
excludes most of the library (`Markup.md:232-239`), while the milestone's premise is the whole surface. Also
**[S §6.2]**: drawing one markup document twice in a frame makes every element collide, and nothing warns.

**A retained scene graph on the wasm side, mutated by JS commands** (`createNode`/`setProp`/`appendChild` — a DOM).
Rejected: it reintroduces exactly the identity and lifetime problems the tree deletes (now with node-handle lifetimes
to manage across the boundary), multiplies boundary crossings by the mutation count, and adds a second source of
truth — the wasm-side graph — that can disagree with JS's.

**Diffing on the JS side and sending patches.** Rejected in §4.2. Worth revisiting only if a real application is
measured spending more than 1% of its frame in `encode`, and the fix is more likely to be virtualization than
diffing.

**A synchronous "settle" pass** — replay, dispatch handlers, and replay again in the same rAF so state changes are
visible in the same frame. This removes §7.1's one-frame latency entirely and is genuinely tempting. Rejected for v1
because the second pass would run against the same input snapshot, which means running it with `NowInput.isPassive`
so interactions are not double-reported; `NowLayout.RunMeasured` establishes that a passive pass is legal
(`NowLayout.cs:1481-1503`), but it runs one *before* the real pass, not after, and a passive pass that also *draws*
is not an existing pattern. It also doubles GPU work on every input frame. If §7.1 turns out to matter, this is the
first thing to try, and slice S2's idempotence test is the prerequisite that makes it safe to try.

---

## Appendix B — the assumptions this design will live or die by

1. `[JSMarshalAs<JSType.MemoryView>]` over `ArraySegment<byte>` gives JS a handle valid across calls, with
   `set`/`slice`. **Tested first, in S1.** Fallback named in §4.5.
2. `NowControls.GetControlId(new NowId(k))` under scope *S* equals what `SetId(new NowId(k))` resolves to under *S*.
   **Asserted in S2.** Both should funnel through `NowControls.cs:498-504`; "should" is not "does".
3. A `ControlIdScope` per container node is cheap enough at 300 containers per frame. Unmeasured. If it is not, the
   fallback is to open an id scope only for containers whose subtree contains an identity-bearing control, which the
   encoder can flag at build time.
4. Event collection guarded by `!NowInput.isPassive` is sufficient to keep a measure pass from double-reporting.
   Follows from `NowTextField.cs:333-337`'s treatment of `submitted`; **tested in S3.**
5. NowUI's per-control state (`NowControlState`, keyed by resolved id) and the bridge's value store, both evicting at
   ten seconds, stay in agreement. **Tested by leaving a control undrawn for eleven seconds and redrawing it**; the
   expected result is that both forget together and the control reappears seeded from `defaultValue`.
