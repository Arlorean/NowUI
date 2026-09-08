# M3 Specification — the JavaScript surface for NowUI

**Status.** Implementable specification. This is the document an implementer builds from; the three designs
(`M3-Design-A.md`, `M3-Design-B.md`, `M3-Design-C.md`) and the fact base (`M3-SurfaceScout.md`) remain as the record of
how it was reached, and this document supersedes all three where they disagree with it.

**What this delivers.** A person or a model writing only JavaScript can write a complete NowUI application — real SDF
text, real theming, real focus navigation, real text editing with IME and undo, drawn by WebGL2 — serve it as two
static files, and have it work. Until now every one of the 88 measured working features
(`Docs/Standalone/M2-FeatureMatrix.md`) was reachable only from C#.

**Frozen.** `Assets/NowUI`, `Assets/NowUITests`, `Assets/NowUIHarness` are read-only. `Standalone/NowUI.Engine` is not
touched: the bridge needs `NowRuntime.BeginFrame` / `EndFrame` / `ResetAll`, and all three are already public
(`Standalone/NowUI.Engine/NowRuntime.cs:151`, `:191`, `:208`). All new code lives under `Standalone/NowUI.Bridge`,
`Standalone/Web/NowUI.Web` and `Tools/Standalone/SurfaceGen`.

**Citations.** `file:line` references were re-verified against the working tree on 2026-09-08 while writing this
document. Where a source design or a judge asserted something the source does not say, Appendix A records the
correction and the evidence.

**The one sentence.** *There are no builders, no `.Draw()`, no `using`, no ids and no refs in JavaScript; there are 41
functions, 23 of which take a key as their first argument; scopes are callbacks; and every value a control returns is
one frame old.*

---

## 0. What was chosen, and what that costs

The base is **Design B — the curated ergonomic surface**. B and C tied on judged total (90 each across two lenses);
B carries the single highest scorecard (47) and wins the lens this milestone is framed by — an AI writing a whole file
in one pass, with no compiler to argue with. A reader who weights faithfulness and failure containment above
authorability would reasonably have chosen C, and §10 records exactly what that reader gives up and what is recovered
by graft.

Grafted in, each verified against source before adoption:

| From | What | Where it lands |
|---|---|---|
| C | the sibling key-vector reorder detector, **on by default**, with C's report format | §3.6 check 3 |
| C | gate 5 — the per-function op-log diff against `NowRecordingRenderBackend` | §7.4 G5 |
| C | delegate-gated capabilities as recorded subtrees (overlay first) | §5.8, §9 W11 |
| C | the falsy-slot discipline for conditional scopes | §3.5 |
| C | the load-bearing identity equality asserted by a test, not assumed | §7.4 G9 |
| C | the honest accessibility and virtualization entries in the limits list | §8.15, §8.20 |
| A | content-hashed opcodes and a boot-time surface handshake | §5.2, §7.4 G3 |
| A | `nowui.unsupported.json` with machine-readable reason codes | §7.4 G4 |
| A | generation from assembly metadata, not from the api dump | §7.1 |
| A | the three attribute cross-checks | §7.4 G6 |
| A | the never-read-result warning, scoped to tier 2 | §7.5 |

Grafts considered and **rejected**, with reasons, are in §10. The largest is C's idle-frame resubmit, which does not
apply to an immediate-mode API.

Errors proved by the judges, and one class none of them caught, are corrected here and itemised in Appendix A. The
most consequential:

* **Transport.** All three designs claim JavaScript writes into WASM memory with no copy. The repository's own JS says
  otherwise: *"A .NET MemoryView is valid only for the duration of the call. `slice()` copies it out"*
  (`Standalone/Web/NowUI.Web/wwwroot/nowui-gl.js:3668-3673`). §5.1 is redesigned around one copy in each direction —
  which is cheaper to implement, removes the whole cross-call-lifetime risk class, and costs ~8 KB of memcpy per frame.
* **Identity plumbing.** Design B's decoder does `SetId(Id(p + 1))` over a 64-bit hash. That call cannot be written:
  `NowId` admits only a string or a 32-bit int (`Assets/NowUI/Runtime/NowId.cs:42`, `:60`) and `NowResolvedId` has no
  public value constructor (`Assets/NowUI/Runtime/NowResolvedId.cs:15`). §3.3 replaces it.
* **Values.** Design B's recorder emits the caller's value *before* reading the previous frame's result, which reverts
  a keystroke. Traced to `NowTextEdit.Clamp(ref state, text)` — the caller's string is authoritative. §6.4 replaces
  the rule.
* **`RunMeasured` runs the whole UI twice**, not "one extra decode" (`Assets/NowUI/Runtime/NowLayout.cs:1578-1590`,
  verified). §5.7 states the real cost and pins the ordering that keeps the author's draw function running once.

---

## 1. The API, shown

This is a complete NowUI application. It is the whole file. Nothing else is imported, generated or configured.

```js
// app.js
import { start, ui } from './nowui/nowui.js';

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
  ui.column({ padding: 24, gap: 16, grow: 1 }, () => {

    ui.heading('Team roster');

    // ---- the form -------------------------------------------------------
    ui.card({ padding: 16, gap: 12 }, () => {

      ui.row({ gap: 12 }, () => {
        state.name  = ui.textField('name',  state.name,  { placeholder: 'Full name',     grow: 1,
                                                           onSubmit: addPerson });
        state.email = ui.textField('email', state.email, { placeholder: 'name@team.dev', grow: 1 });
      });

      ui.row({ gap: 12, align: 'center' }, () => {
        ui.text('Priority', { width: 64 });
        state.priority = ui.slider('priority', state.priority, 1, 5, { step: 1, grow: 1 });
        ui.text(String(state.priority), { width: 24 });
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

    ui.when(state.status !== null, () => ui.caption(state.status));
  });
});
```

`index.html` boots the wasm module, which calls `start`'s callback once per animation frame and draws. That page
already exists in this repository's shape (`Standalone/Web/NowUI.Web/wwwroot/index.html`, `main.js`).

**This example is the API's contract with its author.** It is the acceptance test of W12: an AI given only the
reference page and no other context must write it, correctly, on the first attempt.

### 1.1 The six rules the example teaches

**R1 — Every interactive control takes a key as its first argument. Nothing else does.**
`ui.button('Add', …)`, `ui.textField('name', …)`, `ui.slider('priority', …)`, `ui.switch('active', …)`. Drawings —
`ui.text`, `ui.heading`, `ui.space`, `ui.badge` — hold no state and take content first, no key. That single
distinction is the entire identity model (§3), and it is the one thing an author has to internalise.

**R2 — Argument order is always `key, value, data, options`, and a scope's body comes last. There are no overloads,
ever.**
`ui.slider('priority', v, 1, 5, {step:1})`, `ui.dropdown('role', p.role, ROLES, {width:150})`,
`ui.textField('name', v, {placeholder:'…'})`. For a scope the body (or, for `ui.split`, both bodies) comes last and
the options object sits immediately before it: `ui.scroll('roster', {gap:2}, body)`,
`ui.foldout('adv', open, {label:'Advanced'}, body)`. There is no signature in this API where two arguments of the same
type are adjacent and swappable, except the deliberate `min, max` pair and `ui.split`'s two bodies.

Compare the C# form, where forgetting `.Draw()` renders nothing and produces only a compiler warning
(NOWUI001, `Assets/NowUI/Analyzers~/NowUI.Analyzers/NowBuilderDiscardAnalyzer.cs:20-26`). In JavaScript there is no
builder object to forget to consume: calling `ui.button(...)` **is** the draw. The library's most common authoring
mistake is unrepresentable.

**R3 — Values go in and come back out. There are no refs and no read-backs.**
`state.priority = ui.slider('priority', state.priority, 1, 5)`. The C# API's pervasive `Draw(ref T value)` — the
single largest structural obstacle to any bridge (M3-SurfaceScout §5.3d) — collapses into ordinary assignment. The
durable value lives in a plain JavaScript object, which is where an author already keeps it, and where they can
serialise it, diff it or send it over a socket. NowUI keeps only the ephemeral part (caret, selection, scroll offset),
which is all it kept anyway: `NowTextField.Draw(ref string text)` stores no caller text
(`Assets/NowUI/Runtime/Controls/NowTextField.cs:685`).

**R4 — Scopes are callbacks. There is no begin, no end, and nothing to dispose.**
`ui.column(opts, body)`, `ui.row`, `ui.card`, `ui.scroll(key, opts, body)`, `ui.list`, `ui.when`. Balance is a property
of the JavaScript call stack, not of the author's discipline (§4). The two C# facts that make this non-negotiable —
out-of-order disposal throws (`Assets/NowUI/Runtime/NowScopeGuard.cs:54-61`) and a scope left open across a frame
boundary makes the *next* `Now.StartUI` throw (`Assets/NowUI/Runtime/Now.cs:1279-1286`) — are unreachable from this
shape.

**R5 — Boolean-returning controls are events, read with `if`.**
`if (ui.button('Add')) addPerson();` — true on exactly the frame it is clicked, once. This is the C# convention
verbatim, and it survives translation because the bridge latches and consumes one-shot flags (§6.3).

No tier-1 function returns a **result object** — a bag of interaction flags of the kind C# would have
implicit-bool-converted (`NowTextField.cs:356`). That removes by construction the trap that `if (obj)` is always true
in JavaScript. One tier-1 function returns a non-primitive at all: `ui.colorField` returns `[r,g,b,a]`. It is a value,
not an event, and `if (ui.colorField(...))` is as meaningless as `if (someArray)` anywhere else in JavaScript.

**R6 — Everything a control tells you describes the previous frame; everything under `ui.frame` is current.**
One frame, 16 ms at 60 Hz (§6.2). The split is explicit because mixing the two under one accessor would be the worst
available ergonomic.

### 1.2 What is deliberately absent from the example

No ids. No `NowId`, no `NowResolvedId`, no `IdScope`, no `KeyedItem` — all four C# identity mechanisms
(M3-SurfaceScout §2.4) are used by the bridge and none is visible. No `using`. No `.Draw()`, `.Begin()`,
`.SetOptions()`, `.SetWidth()`. No `NowRect` and no layout arithmetic. No frame function beyond `start`. No type
imports and no build step: `app.js` is served as-is and `nowui.js` is a static file beside it.

---

## 2. The complete JavaScript surface

**50 functions.** 24 take a key. This is the whole of tier 1. Everything else in the C# library — 55 builders, 184
factories, 830 fluent setters (M3-SurfaceScout §0) — is reachable through tier 2 (§7.5), or not at all (§8).

**Four** markers appear in the tables and mean something precise:

* **`*`** the first argument is a mandatory key (R1).
* **`△` alias** — the function is the same op as another with one option pre-set. The expansion is declared in
  `surface.json` and generated; the bridge invents no behaviour. There are exactly three: `heading`, `subheading`,
  `caption`.
* **`◇` composite** — the function expands to more than one C# call, or to behaviour NowUI does not have. The
  expansion is declared in `surface.json`, and the generator refuses to build a composite that has no hand-written C#
  twin in the gate-5 corpus (§7.4 G5). There are exactly six: `card`, `when`, `list`, `radio`, `rule`, and the
  `disabled` option. Six is the cap; a seventh requires a decision recorded in this document.
* **`✗` not in this release** — the function EXISTS on the `ui` object and THROWS a named `NowUIAuthorError`
  when called. §2.0 lists all of them with the reason. Do not write code against a `✗` row.

### 2.0 Not in this release

Three functions in the tables below are absent. They are marked `✗` at their row and listed here, before the
signatures, so a reader meets the absences before the shapes that would make them look available.

| JS | Why | Where it becomes possible |
|---|---|---|
| `ui.reset` ✗ | Hot reload is a COORDINATED clear: `NowRuntime`, the intern table on **both** sides of the boundary, the path trie and the result table have to go together. Clearing one without the others is how a string handle comes to mean two different strings | W7. `start()`'s returned handle also exposes `reset()` and it throws for the same reason — the handle member is the same absence, not a second route around it |
| `ui.overlay` ✗ | It is the sole consumer of the recorded subtrees of §5.8. `OP_CALLBACK_BEGIN` and `OP_CALLBACK_END` are reserved opcodes 2 and 3 and nothing emits them | W11 |
| `ui.contextMenu` ✗ | Said to need a `NowResolvedId` rather than a `NowId`, through a begin/item/end triple the command stream does not carry. **That blocker is UNVERIFIED and is recorded as unverified rather than laundered into this document as fact**: `Replay.Identity.cs`'s `DuplicateIdBackstop` already maps a `NowResolvedId` back to a rid, so resolved ids are demonstrably obtainable on that side. `NowContextMenu.cs` has not been read since. Expect this to be implementable | after someone reads `NowContextMenu.cs` |

**This table is generated from one array, and that is the point.** `nowui.js` exports
`NOT_IMPLEMENTED = ['reset', 'overlay', 'contextMenu']`, and its `notInThisRelease` helper asserts at **module
load** that the name it is given appears in that array. So the runtime cannot throw for a function this document
says works, and cannot silently start working for one it says is absent. That assertion exists because the
opposite happened: five functions threw while §2 tabled all five with no marker, and code written from this
document threw on its first call.

Two functions that were on that list are now real, and neither stated blocker survived reading the source:

* **`ui.theme`** — the claim was "the host has no resource manifest". `NowTheme` already builds **and caches**
  both a light and a dark `NowThemeAsset` (`NowTheme.cs:79-96`), and `NowControls.Theme(asset)` pushes a real
  scope (`NowControls.cs:155`). A two-entry table is the whole manifest a browser host needs. The *general*
  named-asset form is still absent, and that absence is real — it is the same missing piece that blocks textures.
* **`ui.split`** — the claim was "the command stream has one scope bracket". Brackets **nest**, so a split is
  `SPLIT{ PANE(0){…} PANE(1){…} }`: three ops and a structural rule that already existed.

### 2.1 Frame and lifecycle — 5

| JS | Signature | Replays as |
|---|---|---|
| `start` | `start(draw, options?) → { stop(), reset() }` | owns `requestAnimationFrame` through the existing `WebApp.Frame()` export (`Standalone/Web/NowUI.Web/Program.cs:250`); `options` is `{ exactLayout = true, onFault = 'partial' \| 'lastGood', maxStrings = 65536 }` |
| `ui.frame` | read-only `{ width, height, dpr, dt, time, count }` | the host poll at the top of the current frame (`Program.cs:258`); **not** one frame old |
| `ui.theme` | `ui.theme(name, body)` | `NowControls.Theme(asset)` (`NowControls.cs:155`). `name` is **`'light'` or `'dark'`** and nothing else: the bridge builds both assets the way `NowTheme` builds its own defaults (`NowTheme.cs:79-96`), so no resource manifest is involved. A theme by ASSET NAME needs one and is absent (§8). Any other name throws, naming the two |
| `ui.reset` ✗ | `ui.reset()` | **Not in this release** (§2.0). `NowRuntime.ResetAll()` plus a coordinated clear of the string table, the path trie and the result table — this is hot reload, W7 |
| `ui.debugPath` | `ui.debugPath() → string` | recorder-only; returns the canonical path (§3.2) of the position the recorder is at. Costs one string build, on demand |

### 2.2 Scopes — 11

`opts` on every scope accepts `key` to promote an anonymous scope to a keyed one (§3.5).

| JS | Signature | Replays as |
|---|---|---|
| `ui.column` | `(opts?, body)` | `IdScope(seg)` + `NowLayout.Column().SetId(seg)…Begin()` (`NowLayoutContainers.cs:251`, `:53`, `:200`) |
| `ui.row` | `(opts?, body)` | `IdScope(seg)` + `NowLayout.Row().SetId(seg)…Begin()` (`:285`) |
| `ui.card` ◇ | `(opts?, body)` | `Column().SetId(seg).Padding(v4).Gap(f).Begin()`, then `Now.Rectangle(scope.rect).SetStyle(style).Draw()` (`Now.cs:4335`) before the children, so the background is behind them. `NowLayoutScope` exposes the group's reserved `rect` at `Begin()` (`NowLayout.cs:382`), which is what makes the composite expressible at all. `style` defaults to `NowRectangleStyle.Elevated` (`NowThemeStyles.cs:10`) |
| `ui.when` ◇ | `(cond, body)` | recorder construct. Always consumes one anonymous ordinal; when `cond` is true it opens `NowControls.IdScope(seg)` only — no layout group — so the body's children flow in the parent |
| `ui.list` * ◇ | `(key, items, keyOf, render)` | `IdScope(listSeg)`, then per item `NowControls.KeyedItemIn(listSeg, itemSeg)` (`NowControls.cs:260`). `keyOf` is **required**; there is no overload without it and no index default. No options object in the first release |
| `ui.scroll` * | `(key, opts?, body)` | `IdScope(seg)` + `NowLayout.ScrollView().SetId(seg)…Begin()` (`Controls/NowScrollView.cs:74`, `:123`) |
| `ui.foldout` * | `(key, open, opts?, body) → boolean` | `NowLayout.Foldout(label, id)` (`Controls/NowFoldout.cs:143`); `opts.label` defaults to the key; returns the open state and runs `body` when open |
| `ui.split` * | `(key, ratio, opts?, first, second) → number` | `NowSplitView.Begin(ref float)` under `NowLayout.SplitView(axis).SetId(seg)` (`NowControlFactories.cs:329`); returns the new ratio, reconciled per §6.4, so a drag reaches the author. The only function with two bodies, and it needs no new kind of scope bracket: brackets NEST, so the wire is `SPLIT{ PANE(0){…} PANE(1){…} }`. `opts.axis` is `'horizontal'` (default) or `'vertical'`. Identity: one keyed node for the split, one anonymous ordinal per pane |
| `ui.overlay` * ✗ | `(key, rect, body)` | **Not in this release** (§2.0). `NowOverlay.Defer(rect, action)` over a recorded subtree (`Controls/NowOverlay.cs:805`; §5.8). The overlay's id ancestry would be correct for free: `Defer` captures the id scope and restores it when the deferred draw runs (`:848`, `:1857`) |
| `ui.canvas` * | `(key, opts, body) → { width, height, stale }` | `NowLayout.Column().SetId(seg)…Begin()` + `Now.Mask(scope.rect)`. **The drawing scope**: it reserves one layout box, defines the coordinate origin every drawing inside it is relative to, and clips to that box so a drawing cannot escape it (which is also what makes a canvas nest correctly inside a scroll view). See §2.3b for the coordinate model. THROWS when given none of `height`, `minHeight` or `grow` — a canvas has no children to size it, so without one it collapses to zero and draws an invisible nothing |
| `ui.mask` | `(shape, body)` | `Now.Mask(NowMaskShape)` — an analytic clip over a SUBSET of the enclosing canvas. `shape` is exactly one of `{ rect }`, `{ rect, radius }`, `{ ellipse }`, `{ circle, radius }`, `{ capsule, radius }`, plus an optional `{ feather }`. Anonymous, like `ui.when`. **It opens an identity scope**, so wrapping EXISTING controls in one changes their canonical path and their keyed state — scroll offset, caret, foldout — resets once. That is the same cost as wrapping them in a `ui.column` |

### 2.3 Drawings — 8, none keyed

| JS | Signature | Replays as |
|---|---|---|
| `ui.text` | `(content, opts?)` | `NowLayout.Label(content)…Draw()` |
| `ui.heading` △ | `(content, opts?)` | `ui.text` with `textStyle: NowTextStyle.Heading = 5` (`NowThemeStyles.cs:24`) |
| `ui.subheading` △ | `(content, opts?)` | `textStyle: Subheading = 6` |
| `ui.caption` △ | `(content, opts?)` | `textStyle: Caption = 9` |
| `ui.space` | `(pixels)` | `NowLayout.Space(pixels)` (`NowLayout.cs:2090`) |
| `ui.flexSpace` | `(weight?)` | `NowLayout.FlexibleSpace(weight)` (`:2104`) |
| `ui.rule` ◇ | `(opts?)` | a `NowLayout.Row().FillWidth().Height(1).Begin()` whose rect is filled with `Now.Rectangle(rect).SetStyle(Outline).Draw()` |
| `ui.badge` | `(content, opts?)` | `NowLayout.Badge(content)…Draw()` (`NowControlFactories.cs:305`) |

### 2.3b Shapes — 7, none keyed, none returns anything

A drawing has no state and no interaction, so it has no identity either — the same reasoning §2.3 applies to
`ui.text`.

**THE COORDINATE MODEL, once.** A coordinate is **canvas-local**: relative to the top-left of the innermost
enclosing `ui.canvas`, so `(0, 0)` is that canvas's own corner. Where there is no enclosing canvas it is screen
space, the space `ui.frame.width/height` is in. The **decoder** adds the origin; JavaScript emits raw numbers.
That is what makes a drawing land in the right place even on the frame a window is resized, when JavaScript's
idea of the canvas *size* is one frame old: the origin is always exact, only the size briefly is not.

Why a canvas exists at all: `Now.Rectangle` is the only NowUI draw that takes a `NowRect` a layout scope can
supply. `Now.Ellipse`, `Now.Line`, `Now.Bezier`, `Now.Triangle` and `Now.Polygon` take raw `Vector2` and cannot
be laid out — there is no such thing as laying out a bezier. So layout answers *where is the drawing area* once,
and coordinates answer *where in it*.

A `point` is `[x, y]`; a `box` is `[x, y, width, height]`. All numbers, all finite (§2.8).

| JS | Signature | Replays as |
|---|---|---|
| `ui.rect` | `(box, opts?)` | `Now.Rectangle(NowRect)`. The one shape with a semantic `style`, because `NowRectangle` is the one NowUI shape with a `SetStyle`. Layering: **style first, explicit second**, so `{ style: 'accent', radius: 0 }` means what it reads as. With neither `color` nor `style` it takes `NowRectangleStyle.Surface` rather than raw white |
| `ui.circle` | `(center, radius, opts?)` | `Now.Ellipse(Vector2, Vector2)`. `radius` is one number broadcast to both axes, or `[rx, ry]`. The radius is a SIZE and is not translated by the origin — only the centre moves |
| `ui.line` | `(from, to, opts?)` | `Now.Line(Vector2, Vector2)` |
| `ui.bezier` | `(from, control1, control2, to, opts?)` | `Now.Bezier(…)` — the same `NowLine`, so the same `stroke`, `cap` and `dash` |
| `ui.triangle` | `(a, b, c, opts?)` | `Now.Triangle(…)` |
| `ui.polygon` | `(points, opts?)` | `Now.Polygon(List<Vector2>, int, int)`. `points` is `[[x,y], …]` **or** a flat `[x,y,x,y,…]`, discriminated by the first element — both, because the nested form is what a person writes and the flat form is what a 500-point chart sends without allocating 500 arrays a frame. Fewer than three points draws nothing, silently: a chart whose data has not loaded legitimately has none |
| `ui.gradient` | `(box, from, to, opts?)` | `Now.Gradient(NowRect, Color, Color)`. The two ramp ends are ARGUMENTS, not options: a gradient with one colour is not a gradient. `opts.kind` is `'linear'` (default), `'radial'` or `'conic'`; `opts.angle` follows CSS's convention — 0 up, 90 right, clockwise — so a value copied out of a CSS gradient needs no conversion |

**Colour**, for all of them, is one option discriminated by shape:

| Written | Meaning |
|---|---|
| `{ color: 'accent' }` | one of the 27 `NowColorToken` names — **prefer this**, it follows light and dark where hex does not |
| `{ color: '#3B82F6' }` | a literal; `'#rgb'`, `'#rrggbb'` and `'#rrggbbaa'` all parse |
| `{ color: [0.2, 0.5, 1] }` | RGBA floats 0..1 — **the same array `ui.colorField` returns**, so `{ color: ui.colorField('c', c) }` composes with no conversion anywhere |

The tokens are `background surface surfaceMuted text textMuted border accent accentText surfaceElevated
surfaceHover surfacePressed accentHover accentPressed accentMuted borderStrong focusRing success successText
successMuted warning warningText warningMuted danger dangerText dangerMuted shadow scrim`.

A shape with **no** colour is drawn in the theme's text colour, not in white: white on the light theme's white
ground is invisible-and-correct, which this library has already shipped once.

**`style` is refused by name on every shape but `ui.rect`**, with the pointer to `ui.rect` in the message.
`NowCircle`, `NowLine`, `NowTriangle` and `NowPolygon` have no `SetStyle` — there is nothing to map it to — and
the precedent is `align: 'stretch'` (§2.7): reject, rather than silently draw nothing.

Every drawing option is refused on the functions whose decoder does not read it. The allowed sets are:

| Function | Options |
|---|---|
| `ui.rect` | `color stroke strokeColor radius blur style disabled` |
| `ui.circle` | `color stroke strokeColor fill segments` |
| `ui.line`, `ui.bezier` | `color stroke cap dash` |
| `ui.triangle`, `ui.polygon` | `color stroke strokeColor fill` |
| `ui.gradient` | `kind angle spread radius blur stroke strokeColor` |
| `ui.canvas` | the layout half of §2.7 only: `key width height minWidth maxWidth minHeight maxHeight grow gap padding align justify` |
| `ui.split` | the same, plus `axis` |

### 2.4 Actions — 3, all keyed

Every one returns `boolean`, true on exactly the frame the event fired, once.

| JS | Signature | Replays as |
|---|---|---|
| `ui.button` * | `(key, opts?) → boolean` | `NowLayout.Button(label).SetId(seg)…Draw()` (`Controls/NowControlBuilders.cs:65`, `:132`). `label` defaults to the key; `opts.label` overrides it (§3.7 hazard 1) |
| `ui.selectable` * | `(key, selected, opts?) → boolean` | `NowLayout.SelectableRow(...).SetId(seg)…Draw()` (`NowControlBuilders.cs:213`, `:238`) |
| `ui.chip` * | `(key, label, opts?) → boolean` | `NowLayout.Chip(label).SetId(seg)…Draw(out bool removed)`; `removed` is delivered through `opts.onRemove`, never as a returned object |

### 2.5 Values — 14, all keyed

Every one returns the value, following the reconciliation rule of §6.4.

| JS | Signature | Replays as |
|---|---|---|
| `ui.textField` * | `(key, value, opts?) → string` | `TextField().SetId(seg)…Draw(ref string)` (`NowTextField.cs:685`). `opts.onSubmit` fires from `result.submitted` |
| `ui.textArea` * | `(key, value, opts?) → string` | `NowLayout.TextArea()…Draw(ref string)` |
| `ui.numberField` * | `(key, value, opts?) → number` | `TextField().SetId(seg)…Draw(ref float)`; `opts.min/max/step/format` |
| `ui.checkbox` * | `(key, value, opts?) → boolean` | `NowLayout.Checkbox().SetId(seg)…Draw(ref bool)` (`NowControlBuilders.cs:406`) |
| `ui.switch` * | `(key, value, opts?) → boolean` | `NowLayout.Switch().SetId(seg)…Draw(ref bool)` |
| `ui.radio` * ◇ | `(key, value, options, opts?) → string` | one `NowLayout.Radio()` per option under `IdScope(seg)`, each keyed by its option string; returns the selected option |
| `ui.slider` * | `(key, value, min, max, opts?) → number` | `NowLayout.Slider(min, max).SetId(seg)…Draw(ref float)` (`NowControlBuilders.cs:672`) |
| `ui.intSlider` * | `(key, value, min, max, opts?) → number` | the same builder with `Draw(ref int)` |
| `ui.dropdown` * | `(key, value, options, opts?) → string` | `NowLayout.Dropdown(options).SetId(seg)…Draw(ref int)`; `value` is the current option and the return is the selected option — the index never surfaces. **Two frames late** (§6.2) |
| `ui.combo` * | `(key, value, options, opts?) → string` | `NowLayout.ComboBox(options).SetId(seg)…`. **Two frames late** |
| `ui.colorField` * | `(key, value, opts?) → [r,g,b,a]` | `NowLayout.ColorPicker().SetId(seg)…Draw(ref Color)` (`Controls/NowValueControls.cs:344`) |
| `ui.datePicker` * | `(key, value, opts?) → number` | `NowLayout.DatePicker()…` (`NowControlFactories.cs:347`); value is a Unix epoch in milliseconds |
| `ui.timePicker` * | `(key, value, opts?) → number` | `NowLayout.TimePicker()…` (`:353`); value is seconds since midnight |
| `ui.tabs` * | `(key, selected, labels, opts?) → number` | `NowLayout.TabBar(labels).SetId(seg)…` (`:317`) |

### 2.6 Feedback — 2

| JS | Signature | Replays as |
|---|---|---|
| `ui.progress` | `(value01, opts?)` | `NowLayout.ProgressBar(value01)…Draw()` (`NowControlFactories.cs:299`) |
| `ui.contextMenu` * ✗ | `(key, items, opts?) → number` | **Not in this release** (§2.0), and its blocker is recorded there as UNVERIFIED. `NowContextMenu.Begin(NowResolvedId)` / `Item(label, NowId id, …)` / `End()` (`Controls/NowContextMenu.cs:304`, `:390`, `:614`), begin/end kept inside the bridge. Returns the index of the item clicked, or `-1`. **Two frames late** |

`ui.contextMenu` is the one place a `NowResolvedId` is required rather than a `NowId`: `NowContextMenu.Begin` takes
one, `Begin(int)` is `[Obsolete(…, true)]` (`:337`), and the type has no public value constructor. The bridge obtains
it from the public `NowControls.GetControlId(NowId)` (`NowControls.cs:476`) under the ambient scope. **That makes gate
G9 load-bearing for a first-release function, not merely prudent** — if `GetControlId(new NowId(h))` does not resolve
to the same identity `SetId(new NowId(h))` would, the context menu's identity silently diverges from every other
control's. Note also that every *positional* entry overload is obsolete-as-error
(`PositionalEntryObsoleteMessage`, `:44`) — *"Positional context-menu entries were removed. Supply a stable authored
id"* — so the item's interned key handle is not an ergonomic choice here, it is the only compiling call.

### 2.7 The options object

One options object per call, last. Keys are stable across every function that accepts them; a function accepts a key
only when the underlying C# builder has the matching setter, and the generator refuses an options key with no setter.

| Option | Type | Setter |
|---|---|---|
| `key` | string | promotes an anonymous scope to a keyed one (§3.5). Accepted by `ui.column`, `ui.row` and `ui.card` only — every other scope takes its key positionally because it is mandatory |
| `label` | string | the rendered label, when it must differ from the key |
| `width`, `height` | number | `SetWidth` / `SetHeight` |
| `minWidth`, `maxWidth`, `minHeight`, `maxHeight` | number | the matching `NowLayoutOptions` setters |
| `grow` | number | `SetStretchWidth(weight)` on controls, `FillWidth()`/`Grow(w)` on containers |
| `gap` | number | `Gap(f)` (containers) |
| `padding` | number \| [h,v] \| [l,t,r,b] | `Padding(Vector4)`; the coercion is named in the manifest |
| `align` | `'start'\|'center'\|'end'\|'stretch'` | `AlignChildren(NowLayoutAlign)` / `SetAlignItems` |
| `justify` | `'start'\|'center'\|'end'\|'between'` | `Justify(NowLayoutJustify)` |
| `style` | enum name | `SetStyle(NowRectangleStyle)` — `surface muted outline accent elevated accentSoft danger ghost` (`NowThemeStyles.cs:6-13`) |
| `textStyle` | enum name | `SetTextStyle(NowTextStyle)` — `title body muted button display heading subheading bodyStrong label caption` (`:19-28`) |
| `step` | number | `SetStep` |
| `placeholder` | string | `SetPlaceholder` |
| `rect` | boolean | opt in to a rect in the result table; switches the control to its `Begin()` path (§6.1) |
| `disabled` ◇ | boolean | a declared composite: forces `NowRectangleStyle.Ghost`, applies the muted text style, and discards the interaction result. NowUI has no disabled concept — `NowButton`'s entire fluent surface is `SetOptions/SetWidth/SetHeight/SetStretchWidth/SetId/SetNavigation/SetStyle/SetTextStyle/SetAlignItems` (`NowControlBuilders.cs:52-81`) — so the limits are real and documented at the option: a disabled control is still hoverable, still focusable, still in the keyboard navigation order, and still shows a focus ring. If that matters, do not draw it |
| `onSubmit`, `onRemove` | function | invoked synchronously at the point of the call when the previous frame's result carries the flag. Handlers run inside the draw function, so a handler's state change is visible to every control drawn after it — the same ordering `if (ui.button(...))` already has |

`{ disabled }` is the fifth and last composite. The count is capped deliberately (§7.3).

#### The styling options — bits 16-27 of the same mask

Added for the shapes of §2.3b, in place in the option mask's free high bits, so no existing field moved a byte.
Each is accepted only by the functions whose decoder reads it; the allowed sets are tabled at the end of §2.3b.

| Option | Type | Setter |
|---|---|---|
| `color` | token name \| `'#rrggbb'` \| `[r,g,b,a]` | `SetColor` on every shape; the fill of a rect. See §2.3b |
| `stroke` | number | `SetOutline(w)` on a shape, `SetWidth(w)` on a line |
| `strokeColor` | the same three forms as `color` | `SetOutlineColor`. **Defaults to the fill colour** when a stroke was asked for and no style supplied an outline — every shape builder leaves `outlineColor` at `default`, which is *transparent black, not unset*, so `{ color: 'danger', fill: false, stroke: 6 }` drew a ring nobody could see until this fallback existed |
| `radius` | number \| `[tl, tr, br, bl]` | `SetRadius(f,f,f,f)` in **human** corner order. `ui.rect` and `ui.gradient` |
| `blur` | number | `SetBlur` |
| `cap` | `'butt'\|'round'\|'square'` | `SetCap(NowLineCap)` — 1-based in the C# enum, and the table says so rather than assuming |
| `dash` | `[length, gap]` \| `[length, gap, offset]` | `SetDash(l, g, o)` |
| `segments` | number | `SetSegments` — circle tessellation |
| `fill` | boolean | `SetFill`. A payload slot rather than a flag bit, deliberately: a flag can only say *true*, and `fill: false` — an unfilled ring, an outlined polygon — is the thing an author most wants to say. Applied AFTER `color`, because `SetColor` sets `fill = true` as a side effect (`NowShape.cs:59`) |
| `spread` | `'clamp'\|'repeat'\|'mirror'` | `SetSpread(NowGradientSpread)` |
| `angle` | number | `SetLinear(angle)` / the conic start angle. CSS convention |
| `axis` | `'horizontal'\|'vertical'` | `ui.split` only; the op's own argument rather than an option bit |
| `kind` | `'linear'\|'radial'\|'conic'` | `ui.gradient` only; likewise the op's own argument |

`fontSize` is **reserved on the wire and applied by nothing.** It holds bit 21 so the bits either side keep their
numbers, but NowUI's label path takes a resolved `NowTextStyle` rather than a size, so there is nothing to set.
Asking for it is an error naming `textStyle` as the answer — not a value that silently vanishes.

### 2.8 Arguments — one rule, everywhere

| What arrives | What happens |
|---|---|
| **absent** — `undefined` or `null` | the documented zero: `false`, `0`, `''`. **Legal**, and it has to stay legal: `ui.checkbox('a', state.notYetSet)` on frame one is normal and correct |
| **the wrong type** | `NowUIAuthorError` naming the function, the parameter and what actually arrived |
| **`NaN` or `±Infinity`** | `NowUIAuthorError` |

This is uniform as of W10 and was not before, which is worth stating because the change is **breaking**: code
that passed `'50'` to a slider used to draw a silent zero and now throws. What it replaced was three rules and an
accident — `value === true` turned `'yes'` into `false`; `typeof value === 'number' ? value : 0` turned `'50'`
into `0`; and both sat two lines from a helper that threw a well-written named error for exactly that mistake.

`NaN` is the case the rule exists for. `typeof NaN` is `'number'`, so it passed every guard, reached the wire as
an f32 and poisoned a layout with no message anywhere — a coordinate that is not a number is not a coordinate,
and refusing it here costs one comparison where diagnosing it there costs an afternoon.

A related invariant that is invisible from the outside but is what makes the rule safe: **every argument is
validated before its op header is written.** `W.op(record, n)` writes a header PROMISING `n` argument slots, so a
throw between the header and the nth slot would leave a stream one op short of its own claim — a corrupt frame
that the managed validator rejects wholesale, with the author's name nowhere in the message. Instead a bad
argument throws with nothing emitted, the recorder's `finally` closes the open scopes, and §4.4's partial frame
still validates.



---

## 3. Identity

### 3.1 The problem, restated exactly

`[CallerFilePath]` / `[CallerLineNumber]` are captured at 188 entry points and interned into an `int` site token
(`NowControls.cs:425`), which becomes the control's identity when no explicit id is given (`NowControls.cs:498-504`).
JavaScript can supply neither.

The failure mode is silent. Two controls sharing an id share focus, caret, drag and scroll — *"which is the caller's
bug, not something to silently disambiguate"* (`NowControls.cs:457-462`) — and the only guard,
`CheckDuplicateControlId`, is `[Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]`
(`NowControls.cs:606-608`), i.e. compiled out of a Release wasm build entirely; even in a Debug build it returns early
under `NowInput.isPassive` and only fires for ids that reach `NowControls.Interact` (`:895`), so two colliding labels
are never reported at all.

### 3.2 The answer

> **Identity is a path. Every interactive control contributes an authored segment to it; only anonymous scopes
> contribute a positional one; and the durable value lives in JavaScript, so a wrong identity costs a caret, never
> data.**

The canonical rendering of a path, used verbatim in every diagnostic this bridge emits — the `Remove` button of §1:

```
  /#0/roster/team[grace]/#0/Remove
   │    │        │       │    └── the control's key
   │    │        │       └─────── the ui.row inside the list item (anonymous, ordinal 0)
   │    │        └─────────────── the list key and the item key from keyOf
   │    └──────────────────────── the ui.scroll key
   └───────────────────────────── the outer ui.column (anonymous, ordinal 0)
```

Two decisions are packed into that sentence, and neither works without the other.

**(a) Every interactive control contributes an authored segment**, so no control is ever positional. Wrapping
`ui.button('Add')` in an `if` and unwrapping it later changes nothing about its siblings. This is the largest
behavioural difference from the C# call-site model, where inserting a line above a control changes
`[CallerLineNumber]` for every control below it in the file. C# absorbs that because a recompile is a recompile; a
bridge cannot.

**(b) The durable value lives in JavaScript**, so a wrong identity costs a caret, never data. **(b) is what makes (a)
safe enough to ship.** The value — the string in the field, the number under the knob — is caller-owned in C# too
(`NowTextField.cs:685`), and here the caller is a JavaScript object. So the exact blast radius of an identity change
is:

| Lost when identity changes | Not lost |
|---|---|
| text caret position, selection, undo history | the text itself |
| scroll offset | the list contents and the selection |
| foldout open/closed, selected tab, split ratio | everything in `state` |
| focus, hover transition, an in-progress drag | — |

And it is recoverable: `NowControlState` evicts after ten seconds untouched
(`Controls/NowControlState.cs:35`, `EVICT_AFTER_SECONDS = 10f`), so a control that disappears and comes back inside
ten seconds finds its caret and scroll exactly where it left them. Longer than ten seconds and it does not. That is a
real, observable behaviour of this design, stated here rather than discovered later.

### 3.3 How a segment reaches NowUI — the plumbing, exactly

Every string that can be a segment is **interned** into a session-stable integer handle (§5.4). The segment handed to
NowUI is that handle, as an **integer `NowId`**, not a string and not a hash.

| Bridge construct | Segment value | C# call |
|---|---|---|
| keyed scope (`ui.scroll('roster')`, `opts.key`) | `internHandle` ≥ 0 | `NowControls.IdScope(int)` — `NowControls.cs:230` |
| anonymous scope (`ui.column`, `ui.row`, `ui.when`) | `-(ordinal + 1)` ≤ −1 | `NowControls.IdScope(int)` |
| one item of a list | `(listHandle, itemHandle)` | `NowControls.KeyedItemIn(listId, key)` — `NowControls.cs:260` |
| a control | `internHandle` ≥ 0 | `builder.SetId(new NowId(handle))` — the `NowId(int)` ctor, `NowId.cs:60` |
| a layout container | the same value as its id scope | `container.SetId(new NowId(seg))` — `NowLayoutContainers.cs:53` |

Five properties this buys, each of which was a defect in one of the three designs:

1. **It is writable.** `NowId` admits a string or a 32-bit int and nothing else (`NowId.cs:42`, `:60`);
   `NowResolvedId`'s only constructor is `internal` (`NowResolvedId.cs:15`). An integer handle is the only
   call-site-free segment the public API accepts that is also allocation-free.
2. **It is total.** `IdScope(int)` cannot throw and cannot no-op. The two traps M3-SurfaceScout names are avoided by
   construction: `IdScope(NowId)` silently pushes nothing on a default id (`NowControls.cs:213-214`) and
   `IdScope(NowResolvedId)` *replaces* the ambient path rather than nesting under it (`:126-138`, via
   `RestoreIdScope`). Neither overload is ever used by the bridge.
3. **It costs no hashing of string content.** `NowIdHash.Derive` hashes a string's characters every time
   (`NowIdHash.cs:105-113`); there is no reference-identity fast path for authored strings. An `AuthoredInt` segment
   skips that entirely.
4. **The two integer namespaces cannot collide.** Intern handles are ≥ 0, anonymous ordinals are ≤ −1.
5. **Domains keep the rest apart.** A control keyed `'x'` and a scope keyed `'x'` under the same parent derive in
   `NowIdDomain.Control` and `NowIdDomain.Scope` respectively, and *"a control path, layout cache, state slot, effect,
   focus host, and overlay cannot alias"* (`Identity.md:48-52`) stays true of a JavaScript-authored app. The layout
   container's id lands in `NowIdDomain.Layout`, which is why passing it the same segment is safe and makes the layout
   cache exactly as stable as the identity.

**The root.** The replay opens `NowControls.IdScope("nowui")` — the one string segment in the design, hashed once per
frame — as the outermost scope inside `Now.StartUI`, so a JavaScript UI can never collide with a C# UI drawn on the
same surface in the same frame. Everything below it is an integer segment.

**Invariant I1, enforced by the generator.** Every container and every control the replay opens *must* be given an
explicit id. `NowLayout.Column()` captures `[CallerFilePath]`/`[CallerLineNumber]` at the bridge's own call site
(`NowLayoutContainers.cs:251-256`), so a container the bridge forgets to `SetId` falls into
`ResolveGroupSiteOccurrence` with one site token shared by every container in the application — flat per-depth
occurrence numbering keyed on a single token (M3-SurfaceScout §2.7, `NowLayout.cs:2481-2498`). That is the worst
available identity, it is silent, and no design named it. `Replay.g.cs` is generated, so the generator asserts that
every emitted factory chain contains exactly one `SetId`, and the assertion fails the build.

**Consequence: occurrence salting never runs for bridge-drawn controls.** Salting applies only when identity falls
back to the call site (`NowControls.cs:526-529` → `Salt`, `:563-580`). Every bridge control has an explicit id, so
`_labelOccurrences` / `_passiveOccurrences` stay empty for them, and the flat-per-frame counter semantics — the ones
that break under reordering — are irrelevant to this design.

### 3.4 The rid, and why it is not a hash

The bridge needs its own key for the result table. It is **not** a `NowResolvedId` (there is nothing public to
construct one from) and it is **not** a hash.

> **The rid is a dense session-local integer assigned by a path trie in JavaScript.**

The recorder already maintains a path stack. Each stack frame is a trie node with three lazily created maps —
`scopes`, `controls`, `items` — keyed by segment value. Pushing a scope is one `Map.get` on the current node; emitting
a control is one `Map.get` on the current node. Nodes persist across frames, so after the first frame every probe is a
hit and nothing allocates. A node's `rid` is a dense integer assigned when the node is created.

This is strictly better than the hash both B and A reached for:

* **Exact.** No collision, at any width, ever. Design B's `Set<number>` of 64-bit rids cannot hold 64 bits — a
  JavaScript number carries 53 — so B's stated collision probability was optimistic by three orders of magnitude, and
  the fix is not better arithmetic, it is not needing arithmetic.
* **Cheaper to read.** The result table is a plain array indexed by rid, with a per-frame generation stamp instead of a
  clear pass. `results[rid]` is an array index, not a map probe.
* **Free diagnostics.** Every node keeps a parent pointer and its segment, so any report can render the canonical path
  of §3.2 on demand without building path strings per control per frame.

The wasm side **never computes a rid** — it only echoes the one the op carried. rids are the bridge's key for the
result table; `NowResolvedId`s are NowUI's key for state; they are different values in different spaces and neither is
derivable from the other. Trie nodes untouched for 600 frames (~10 s at 60 Hz) are dropped, matching
`NowControlState.EVICT_AFTER_SECONDS`, and their rids are recycled through a free list.

### 3.5 The one positional hazard, and the two escapes

Controls are never positional. Anonymous scopes are, and this is the design's one honest positional hazard.

```js
if (showToolbar) ui.row({}, () => { /* … */ });   // anonymous ordinal #0 when present
ui.column({}, () => ui.scroll('main', …));        // #1 with the toolbar, #0 without it
```

Toggling `showToolbar` flips the `main` scroll view between two resolved ids, losing its offset each time. Two
escapes, in order of preference:

1. **`ui.when(cond, body)` exists for exactly this.** It always consumes its ordinal and only runs `body` when `cond`
   is true, so toggling shifts nothing. This is the falsy-slot discipline Design C got right and React gets wrong,
   inverted for the right reason: React drops a falsy child and pays with a re-mount; here the cost of a shift is two
   controls silently sharing edit state, which is worse, so the slot is held. It is why `ui.when` exists in a
   41-function API instead of "just write an `if`" — an `if` around a *control* is free, an `if` around a *scope* is
   not, and rather than ask the author to remember which, the API provides one form that is always right.
2. **Give the scope a key:** `ui.column({ key: 'main' }, …)`. Keyed scopes are immune to ordinal shifts.

The anonymous ordinal counts **only anonymous scopes** within the same parent, so inserting or removing a *keyed*
sibling never shifts anything.

### 3.6 How the author is warned — three checks, all on in every build

**Check 1 — duplicate path. JavaScript, record time, throws.**

Two controls resolving to the same path is a hard error on the frame it happens. The trie makes it exact: the recorder
keeps a per-frame `Uint32Array` stamp indexed by rid; a second sighting of a rid in one frame throws.

```
NowUI: duplicate control key.
  path:  /#0/roster/#1/Remove
  first: draw call #217
  again: draw call #241
Two controls with the same path share focus, caret and drag state. Use
ui.list(key, items, keyOf, render) for repeated data, or give each one a distinct key.
```

**This runs in every build**, which is the single most important thing the curated tier adds that C# does not have. It
turns `NowControls.cs:606-608`'s development-only, interaction-only warning into a first-frame stack trace pointing at
the author's own line, before a byte crosses the boundary.

**Check 2 — key-vector reorder. JavaScript, on change, reports once per path.**

Grafted from Design C, promoted from `?nowui=debug` to on-by-default. Each trie node keeps the previous frame's vector
of child segments. When it changes, the recorder classifies the change: a pure append or truncate at the end is fine
and silent; a change in which every altered slot carries an explicit key is fine and silent (the author is in
control); a change in which any altered slot is an **anonymous ordinal** reports once for that path:

```
NowUI: the children of "/#0/roster" changed shape and slot 2 ("#2") has an automatic key.
  It is now drawing different content than last frame, and it kept the previous scope's
  state (scroll position, foldout state, focus, hover animation).
  Give it a key — ui.column({ key: 'name' }, …) — or wrap the conditional sibling in
  ui.when(cond, body).
  Previous: [#0, #1, #2, #3]
  Now:      [#0, #1, #2]
```

This is the diagnostic that names the brief's exact failure — *"a control forgot its state when a branch changed"* —
as a path, a slot index, a before/after vector and a one-line fix. It costs one string array per trie node, compared
elementwise, ~40 bytes per node, capped at 64 distinct reports per session.

**Check 3 — `keyOf` validation. JavaScript, record time, throws.**

`keyOf` returning a duplicate, `undefined`, or a non-string throws immediately with the offending index and value.
(`KeyedItemIn` would itself throw on an empty key, `NowControls.cs:262-266`, but by then the message has lost the
JavaScript context that makes it actionable.)

**Backstop.** NowUI's own `CheckDuplicateControlId` still runs in the standalone Debug configuration. It is kept as a
backstop for *bridge* bugs, not as an author-facing diagnostic: if it fires while checks 1-3 are silent, the bridge is
wrong, and the message says so. The replayer keeps a `Dictionary<NowResolvedId, int>` of rid, so the report names a
path rather than a hash.

### 3.7 What still breaks, stated plainly

1. **A key that changes every frame re-registers the control every frame.** ``ui.button(`Retry (${n})`)`` gets a new
   identity on each `n`, so it can never hold focus and its press tracking resets mid-press. The fix is
   ``ui.button('retry', { label: `Retry (${n})` })``, and this is exactly why `label` is an *option* rather than a
   positional argument: the ergonomic default (key doubles as label) is safe only for static labels, so the override
   has to be cheap. Check 2 reports it on the second frame — every child key changed while the keys are explicit.
2. **`keyOf: (_, i) => i` over a reordering array is unfixable by the bridge.** Identity follows the key, the key
   follows the position, and state follows the position. This is React's `key={i}` mistake with the same cure
   (documentation, and a lint rule if the project ever grows one). Requiring `keyOf` at least forces the author to
   write the decision down.
3. **A collision the author creates across two code paths never both live in one frame** is invisible. The bridge sees
   one path per frame and has nothing to compare.
4. **Renaming a key discards that control's ephemeral state.** Correct — it is a different control now — but it means
   hot reload resets caret and scroll for anything whose key changed, and only for those.
5. **Two `ui.list` calls with the same key under the same parent collide.** Caught by check 1 on the item scopes
   rather than on the list, so the message names an item. Acceptable; noted.

---

## 4. Scopes without `using`

### 4.1 The shape

Every scope in tier 1 is a function taking a body callback. There is no `ui.beginColumn`, no scope handle, nothing an
author can hold, store or forget. The recorder is four lines, and the same four lines for all nine:

```js
function scope(op, seg, args, body) {
  emitOpen(op, seg, args);            // pushes the trie node + the opcode
  try { body(); }
  finally { emitClose(); }            // always: normal return, throw, or return-through
}
```

Because the `finally` runs on every path out of `body`, the emitted stream is **structurally balanced by construction
and strictly LIFO**. That is not a best effort; it is a property of the language. The C# replay therefore never has to
reorder, repair or defend against a crossed pair — which matters, because out-of-order disposal in NowUI is a thrown
`InvalidOperationException`, not a fixup (`NowScopeGuard.cs:54-61`).

### 4.2 Enforcement, in three places

**In JavaScript, at record time.** The recorder keeps a depth counter and an array of open opcodes; max depth 32,
deeper throws, because a UI thirty-three scopes deep is runaway recursion and saying so is more useful than exhausting
the buffer. `emitClose` asserts the opcode it closes matches the top of that array.

**In JavaScript, at the boundary.** A `ui.*` call outside an active recording throws:

```
NowUI: ui.button() was called outside the frame callback. Every ui.* call must happen
synchronously inside the function you passed to start(). Timers, promises and event
handlers cannot draw — change `state` from them instead, and the next frame will show it.
```

`start(draw)` also rejects a `draw` that returns a thenable, with the same explanation. An `await` inside a draw
function would emit half a frame's ops, hand control back to the browser, and then emit the rest into a buffer the
wasm side has already consumed. It is the mistake a JavaScript author is most likely to make, and it earns a dedicated
message.

**In C#, at replay.** The decoder maintains its own stack of the real `IDisposable` structs and disposes them in
reverse on close. It also validates: an unmatched close, or a buffer ending with a non-empty stack, is a
`NowBridgeProtocolException` naming the opcode and slot offset. That check exists not because the JS recorder can
produce such a buffer — it cannot — but because the same buffer is the tier-2 and test surface, and a decoder that
trusts its input is a decoder that corrupts a frame silently.

### 4.3 Why there is no begin/end escape hatch in tier 1

`NowContextMenu` proves an imperative `Begin`/`Item`/`End` API is expressible — it is the one place in NowUI with no
`IDisposable` at all (`NowContextMenu.cs:304`, `:348`, `:614`). This specification declines. A
`beginColumn`/`endColumn` pair in JavaScript is an invitation for an early `return`, a `continue` or a thrown error to
leave a scope open, and the consequence is not a missing box: it is an `InvalidOperationException` out of the *next*
frame's `Now.StartUI` (`Now.cs:1279-1286`) — the app dies one frame later, at a stack trace with no visible
relationship to the bug. `ui.contextMenu(key, items, opts)` takes an array of item descriptors and does the begin/end
inside the bridge.

Tier 2 does expose paired operations, because it is generated and cannot do otherwise. It wraps them in the same
callback shape, and its documentation says in one line that this is why tier 1 exists.

### 4.4 When JavaScript throws mid-frame

**The author's draw function runs with no NowUI scope open at all.** `Record` is called before `Now.StartUI` (§5.7),
so at the moment a JavaScript exception is thrown, NowUI has not been touched: no scope was opened, no control was
drawn, no identity was pushed, and none of `BeginScreenFrame`'s recovery paths (`Now.cs:1266-1357`) is ever exercised
by an author error. Those paths remain what they are — a diagnostic for a bug in the bridge. This is Design C's
strongest single property, and it is recovered here by ordering alone rather than by changing the API's shape.

What happens next is then a free choice, and the default is the one that localises the bug:

1. Every enclosing `scope()`'s `finally` emits its close op, innermost first. The buffer is **balanced and valid**,
   just short.
2. The exception reaches `record()`'s own try/catch. The frame is marked faulted and the partial buffer is returned.
3. The replay draws it, then draws an error banner over the top: the message, the first three stack frames, and the
   path of the last scope open when it threw. The banner is drawn by hand-written C#, not by a JS-authored subtree,
   so it cannot itself fail.
4. The error is logged once per distinct `message + top frame` — not once per frame — through `BrowserInterop.Log`
   (`WebHostServices.cs:58`).
5. After 120 consecutive faulted frames (two seconds) the loop stops replaying the app buffer and shows only the
   banner.
6. `start(draw, { onFault: 'lastGood' })` replays the last non-faulted buffer instead of the partial one, which keeps
   a working UI on screen at the cost of hiding where it stopped. Both behaviours are one flag; `'partial'` is the
   default.

**Integration hazard, named because no design named it.** `WebApp.Frame()`'s own catch sets `s_Failed = true`
permanently and stops the page's frame loop (`Program.cs:330-337`). An author's JavaScript error must never reach it.
The bridge catches everything it can throw — in `record()`, in the decode walk, and around the banner — and only a
genuine bridge invariant violation is allowed to propagate.

### 4.5 When the replay throws

A NowUI control can throw: `NowId` rejects an empty string (`NowId.cs:52-53`), `IdScope(string)` rejects null/empty
(`NowControls.cs:199-200`), a container rejects an illegal option, an extension hits an unported shader
(`M2-FeatureMatrix.md:78-82`).

The replayer has one `try`/`catch` for the whole walk, **inside** `Now.StartUI`'s `using`, plus a `_currentOp` field
updated per op. `using` unwinds every open scope correctly — that is what `using` is for, and it is why the scopes are
held in `using` rather than in a bridge-managed stack. The frame finishes clean, `StartUI` disposes normally, and the
next frame does not hit `Now.cs:1279-1286`. The failing rid is added to a skip set, reported to JavaScript as a fault
record in the result table, and its subtree is skipped on subsequent frames; the cost is one bad frame, then a stable
UI with a hole in it and a message naming the hole. The skip set would be cleared by `ui.reset()`, which is
not in this release (§2.0) - so today a skipped subtree stays skipped for the life of the page.

Per-op `try`/`catch` was considered and rejected: it costs an exception filter per op for a case that should never
happen, and it would let a broken control silently do nothing forever.

---

## 5. The command stream — the ABI

This section is sufficient to implement both ends without reference to any other document.

### 5.1 Transport

**The correction that shapes everything else.** A `[JSMarshalAs<JSType.MemoryView>]` parameter does not give
JavaScript a typed array over WASM memory. It gives it a `MemoryView` proxy whose public API is `slice()`,
`set()`, `copyTo()`, `length` and `byteLength`. The repository states both halves of this itself:

> *"A span marshals as a memory view over WASM memory that is valid only for the duration of the call, which is what
> lets a whole mesh cross in one call without a managed-to-JS array copy on this side."*
> — `Standalone/Web/NowUI.Web/WebGL2Backend.cs:1941-1945`

> *"A .NET MemoryView is valid only for the duration of the call. `slice()` copies it out."*
> — `Standalone/Web/NowUI.Web/wwwroot/nowui-gl.js:3668-3673`

"No managed-to-JS array copy **on this side**" is the managed side. The JavaScript side copies. Every span in
`WebGL2Backend` is an argument to a `[JSImport]`, live only inside the call, and every JS consumer of one calls
`slice()`.

So the transport is:

* **JavaScript owns its buffers** — a plain `ArrayBuffer` with `Int32Array`, `Float32Array` and `Uint8Array` views over
  it. The recorder writes into those at full speed, with no proxy in the path, and grows them freely.
* **One call per frame carries them across.** Inside the call, JavaScript `set()`s its ops into the managed view and
  `slice()`s the results out.

```csharp
// Standalone/NowUI.Bridge/BridgeInterop.cs
[JSImport("record", "nowui-bridge")]
internal static partial int Record(
    [JSMarshalAs<JSType.MemoryView>] Span<int>  ops,          // JS set()s commands into this
    [JSMarshalAs<JSType.MemoryView>] Span<byte> opsText,      // JS set()s UTF-8 string bytes into this
    [JSMarshalAs<JSType.MemoryView>] Span<int>  results,      // last frame's result table
    [JSMarshalAs<JSType.MemoryView>] Span<byte> resultsText,
    int resultSlots,
    int frame);
```

```js
const NEED_MORE = -1;                       // the only negative return

export function record(ops, opsText, results, resultsText, resultSlots, frame) {
  if (!W.pending) {                         // a retry re-uses the bytes already recorded
    R.load(results.slice(0, resultSlots), resultsText, frame);
    W.reset(frame);
    try { drawFn(); }
    catch (e) { W.fault(e); }               // §4.4 — NowUI has not been touched
    finally { W.closeAll(); }               // §4.1 — the buffer is balanced on every path
    W.pending = true;
  }
  if (W.used > ops.length || W.textUsed > opsText.length) {
    ops.set(Int32Array.of(W.used, W.textUsed));   // the two sizes the managed side must grow to
    return NEED_MORE;                             // ops is always >= 2 slots, so this always fits
  }
  ops.set(W.i32.subarray(0, W.used));
  opsText.set(W.u8.subarray(0, W.textUsed));
  W.pending = false;
  return W.used;
}
```

Consequences, all improvements over what the three designs specified:

* **The cross-call lifetime question disappears.** Nothing is retained across calls. `Span<T>` is correct and
  `ArraySegment<T>` is unnecessary; neither design's assumption needs to hold.
* **Growth is trivial and never loses a frame.** JavaScript grows its own buffer whenever it likes. When the *managed*
  buffer is too small, `record` returns `NEED_MORE` after writing the two required sizes into the first two slots,
  **without discarding the recorded bytes**; the managed side reallocates both buffers and calls `Record` again, and
  the retry is two `set()`s — the author's draw function is not re-run, so no handler fires twice and no state is read
  twice. At most one growth per size class for the life of the application. A hard cap of 4 M slots (16 MB) fails with
  *"the draw function emitted more than 4,194,304 command slots; this is almost always an unbounded loop or a
  recursive component. The last 8 scope keys were: …"*
* **The cost is two memcpys.** ~7 KB out and ~600 bytes back for a realistic frame; the existing WebGL2 backend
  already moves two orders of magnitude more per frame in vertex data.

**Boundary crossings per frame: 1**, plus `Frame()` in and the two existing input drains
(`WebInput.cs:404-408`) — which the bridge does not add. That number is constant: it does not grow with the number of
controls, the depth of the UI, or the number of results read.

**Fallback, if W1 finds `MemoryView.set` unusable.** `[return: JSMarshalAs<JSType.Array<JSType.Number>>] double[]`,
proven in this repository by `WebInput.Drain` (`WebInput.cs:401-405`). It costs an element-wise conversion each way
and is measurably worse; the design does not otherwise change. W1 exists to retire this question before anything is
built on it.

### 5.2 Buffer layout

Little-endian throughout, all offsets in 4-byte slots unless stated. Slots are read through the `Int32Array` or the
`Float32Array` according to the argument kind in the generated signature table.

```
Ops buffer

  slot 0   magic          0x424F574E  'NOWB'
  slot 1   surfaceHash    low 32 bits of the manifest content hash (§7.4 G3)
  slot 2   frameFlags     bit0 faulted · bit1 exactLayout · bit2 hasNewStrings
  slot 3   internCount    number of (handle, offset, length) triples that follow
  slot 4   volatileCount  number of (offset, length) pairs that follow the triples
  slot 5   textBytes      bytes used in opsText
  slot 6   opStart        slot index at which the op stream begins
  slot 7   opEnd          slot index one past the last op slot
  slot 8 … internCount x 3 slots: (handle, byteOffset, byteLength)
     …     volatileCount x 2 slots: (byteOffset, byteLength)
  opStart … the op stream
```

Every op:

```
  slot 0        opcode:16 | argSlots:16
  slot 1..n     arguments, n = argSlots
```

`argSlots` is redundant with the generated signature table and is carried anyway: a decoder meeting an opcode it does
not know skips `argSlots` slots and continues rather than desynchronising. Four bytes per op to make a version mismatch
between `nowui.js` and the wasm module a warning instead of decoded garbage is the right trade.

**Opcodes are content hashes, not ordinals** (grafted from A). An opcode is the low 16 bits of a hash of
`declaringType + memberName + parameterTypeList`, so adding a function renumbers nothing and a JavaScript bundle and a
wasm build produced from different commits still agree about every function both of them have. Collisions are detected
at generation time and resolved by a checked-in salt. Opcodes 0-15 are reserved for structural ops and are fixed:

| Opcode | Name | Meaning |
|---|---|---|
| 0 | `OP_INVALID` | never emitted; a zeroed buffer fails validation immediately |
| 1 | `OP_SCOPE_CLOSE` | closes the innermost open scope; no arguments |
| 2 | `OP_CALLBACK_BEGIN` | opens a recorded subtree (§5.8); arg: subtree id |
| 3 | `OP_CALLBACK_END` | closes it |
| 4 | `OP_NOP` | one slot; used only by the growth retry path |

### 5.3 Argument kinds

| Kind | Slots | Encoding |
|---|---|---|
| `i32` | 1 | as-is, through the `Int32Array` |
| `f32` | 1 | through the `Float32Array` |
| `bool` | 1 | 0 / 1 |
| `enum` | 1 | the C# enum's integer value, generated from the enum so `'accent'` → `NowRectangleStyle.Accent = 3` |
| `str` | 1 | ≥ 0 → an intern handle; < 0 → `~value` indexes this frame's volatile table |
| `key` | 1 | always ≥ 0; a key is always interned, never volatile |
| `seg` | 1 | ≥ 0 an intern handle, ≤ −1 an anonymous ordinal (§3.3) |
| `rid` | 1 | the dense trie integer (§3.4) |
| `color` | 1 | RGBA8 packed |
| `vec2` | 2 | two `f32` |
| `rect` / `vec4` | 4 | four `f32` |
| `strlist` | 1 + n | count, then n `str` slots |
| `opts` | 1 + n | a bitmask slot naming which optional fields follow, then those fields in declared order |

The `opts` bitmask is what keeps an options object cheap. `ui.button('Add', { style: 'accent' })` emits
`[OP_BUTTON | 5<<16, rid, seg, sid('Add'), 0x08, 3]` — six slots, 24 bytes. The whole application of §1 emits, at rest,
**about 220 slots (880 bytes)** for 23 controls and 9 scopes. A realistic larger application — 200 controls averaging
seven slots plus 60 scopes averaging six — is 1,760 slots, 7 KB per frame.

### 5.4 Strings

**Interned.** JavaScript keeps `Map<string, handle>`; the wasm side keeps `List<string>` at the same indices. A string
is interned on first use and its handle is permanent for the session. Handles introduced this frame are declared in
the header's intern table, so the wasm side adds them in one pass before decoding begins. Keys, labels, style names,
placeholders and option lists all intern on frame 1 and cost one slot each thereafter.

**Volatile.** Anything the author interpolates — ``` `Added ${name}.` ```, the contents of a text field, a formatted
number — is never interned. It is written into `opsText` for this frame only and declared in the header's volatile
table. This is the difference between a text field costing one UTF-8 encode per keystroke and permanently leaking one
intern-table entry per keystroke. The recorder decides by **argument position**, from the generated signature table,
never by guessing: `key` positions always intern, `str` positions intern only when the same string instance or value
has been seen before.

**Keys always intern.** A key is by definition repeated across frames, and its handle is the identity segment (§3.3),
so it must be stable.

**The ceiling.** The intern table caps at `options.maxStrings`, default 65 536. On overflow a **key** throws — a UI
minting unbounded distinct keys is deriving identity from data, and stopping is correct — and a non-key string falls
back to the volatile path with one logged warning. There is no eviction in the first release (§8.14).

Encoding is `TextEncoder.encodeInto(str, W.u8.subarray(cursor))`, which writes UTF-8 directly into the recorder's own
buffer and returns the byte count. On the C# side that is `Encoding.UTF8.GetString(span.Slice(off, len))` — one
allocation per distinct volatile string per frame, and zero for interned ones.

### 5.5 Validation, before anything is replayed

The decoder validates the whole buffer before it opens a single scope:

1. magic and `surfaceHash` match the wasm build's manifest hash; a mismatch refuses the frame with a message naming
   both hashes (§7.4 G3);
2. `opStart`, `opEnd`, `textBytes`, `internCount`, `volatileCount` are within bounds and mutually consistent;
3. every intern handle is either already known or declared in this frame's table, and declarations are contiguous
   from `_strings.Count`;
4. walking `argSlots` from `opStart` lands exactly on `opEnd`;
5. scope opens and closes balance and nest.

A failure is a `NowBridgeProtocolException` naming the slot offset. NowUI is not touched.

### 5.6 Idempotence

Under `exactLayout` the buffer is decoded twice per frame, so decoding must have no side effects of its own. The
decoder:

* allocates nothing that outlives the call except the strings it interns — and those are added *before* decoding,
  once, in the preamble;
* advances no cursor that is not a local;
* writes to the result table **only when `!NowLayout.isMeasurePass && !NowInput.isPassive`**
  (`NowLayout.cs:1256`, `NowInput.cs:1317`), so a passive pass cannot overwrite real results with zeroes;
* consumes no one-shot state.

This is exactly the property M3-SurfaceScout §2.7 demands: *"Any recorded command buffer a JS bridge replays must be
replayable more than once per frame, identically, with no side effects of its own."* It holds by construction because
explicit ids are never occurrence-salted, so the measure pass and the real pass resolve every control to the same
`NowResolvedId`.

### 5.7 The frame, in order

```
requestAnimationFrame                                  [main.js, unchanged]
 └─ WebApp.Frame()                                     [JSExport, Program.cs:250]
      s_Host.Poll()                                    (unchanged)
      NowRuntime.BeginFrame()                          (unchanged)
      s_Input.Drain(dpr)                               (unchanged)
      ── bridge ────────────────────────────────────────────────────────────
      used = Bridge.Record(...)          ← ONE call out. JS runs the author's draw
      if (used == NEED_MORE) { grow to the two sizes in slots 0-1; used = Bridge.Record(...); }
      Bridge.Preamble(used)              // intern new strings, validate (§5.5)
      using (Now.StartUI(dpr))
        exactLayout ? NowLayout.RunMeasured(screen, s_Replay)   // replay runs TWICE
                    : using (NowLayout.Area(screen)) s_Replay() // replay runs ONCE
      ── /bridge ───────────────────────────────────────────────────────────
      NowRuntime.EndFrame()                            (unchanged)
```

Four things are pinned here, each because getting them wrong is silent:

**Record is outside `Now.StartUI` and outside `RunMeasured`.** The author's draw function runs **exactly once per
frame**. Design B's frame diagram placed Record inside the `RunMeasured` delegate; `RunMeasured` calls its delegate
twice (`NowLayout.cs:1578-1590`, verified), and its own documentation says *"The callback must not mutate state
unconditionally"* (`:1486-1488`), so an author's draw function running twice would read state twice, double every
handler, and double the JavaScript cost. `s_Replay` is a cached static `Action` field, per the same doc comment's
warning about closure allocation (`:1489-1491`).

**Record before replay, in the same frame.** `StandaloneCoreDesign.md` §4.8 records in frame N and replays at the next
`BeginFrame`. Recording first and replaying immediately is strictly better and costs nothing: the pixels of frame N
reflect the JavaScript state of frame N, so anything JavaScript changes for a reason other than a control — a fetch
completing, an animation, a websocket message — appears the same frame. Read latency is one frame either way.

**`exactLayout` defaults to true, and it costs a second full execution of the UI in wasm.** Not "one extra decode":
`RunMeasured` runs the whole callback twice, so every control is measured, laid out and state-looked-up twice. What it
buys is that flexible space, stretch shares and auto-sized groups resolve from *this* frame's measurements instead of
last frame's — *"Deferred sizes (auto group extents, stretch shares, flexible space) resolve from the previous frame in
a one-pass host"* (`NowLayout.cs:1260-1266`) — removing four rows of M3-SurfaceScout §4.4's latency table outright — including the first frame a
control appears and every frame while it animates, which is exactly when a one-pass settle reads as a flaky UI. It is
legal because the buffer is idempotent (§5.6). `start(draw, { exactLayout: false })` halves the wasm cost for a large
UI and is the first thing to try if a frame budget is tight.

**The root is a container area either way**, so the two paths differ only in the number of passes. Root layout areas
already have explicit bounds, and their options may configure only spacing, padding, child alignment and justification
(`NowLayout.cs:1476-1478`); the author's outermost `ui.column` is therefore a *nested* container, which is why `grow`
and `fill` are legal on it.

### 5.8 Delegate parameters as recorded subtrees

Grafted from C and A together. `NowOverlay.Defer(NowRect, Action)` takes a delegate; a JavaScript function cannot be
called synchronously from inside a wasm frame. The body is instead recorded as a contiguous sub-span of the same
buffer, bracketed by `OP_CALLBACK_BEGIN id` / `OP_CALLBACK_END`, and the replay builds a real C# `Action` closing over
that span; invoking it re-enters the decoder on that range.

Three verified facts make this work for overlays specifically, and they are the reason overlay is the one delegate
capability in the first release:

* `NowOverlay.Defer` captures the ambient id scope (`NowOverlay.cs:848`) and restores it when the deferred draw runs
  (`:1857`), so a recorded subtree's identity ancestry is correct with no work by the bridge;
* `Defer` returns immediately under `NowInput.isPassive` (`:836`), so an overlay body is **not** replayed by the
  measure pass — the sub-span is entered once per frame even under `exactLayout`;
* the sub-span lives in the same buffer, which is still alive when deferred draws run at the end of the frame.

**The restriction, stated now rather than discovered later.** The subtree is recorded before the callback runs, so
**JavaScript inside a callback cannot branch on the callback's own argument.** For `NowOverlay.Defer(NowRect, Action)`
there is no argument and the restriction is vacuous. It bites for `NowDockSpace.Window(string, Action<NowRect>)`
(`Assets/NowUI/Extensions/Docking/NowDocking.cs:198`) and
`NowNodeGraphCanvas.SetNodeContent(Action<NowNode, NowRect>)` (`Assets/NowUI/Extensions/NodeGraph/NowNodeGraph.cs:3889`),
neither of which is in the first release (§8.7, §8.8). Design C listed both as working without stating this;
Design A stated it correctly. It is stated here.

---

## 6. The result table

### 6.1 Layout

One record per interactive control drawn in the last **real** replay pass, written into the results buffer in draw
order:

```
  slot 0        rid
  slot 1        flags:24 | valueKind:8
  slot 2..      value slots, by valueKind
  then          4 slots of rect, iff flags.hasRect
```

| valueKind | Slots | Contents |
|---|---|---|
| `None` | 0 | — |
| `F32` | 1 | the post-draw value |
| `I32` | 1 | the post-draw value |
| `Bool` | 1 | 0/1 |
| `Str` | 2 | byteOffset, byteLength into `resultsText` |
| `Color` | 1 | RGBA8 |

`flags`: `clicked changed submitted focused hovered pressed held released dragging dragStarted dragEnded cancelled
hasRect present`. Every one is computed synchronously inside the replay — `Interact` runs against the current input
snapshot before the control draws (`NowControls.cs:891-900`) — so they are *that replay's* truth, not an approximation
of it.

Because `valueKind` is in the header, a reader advances through the table without consulting the signature table.
JavaScript stores each record into `results[rid]` with a generation stamp equal to the frame number, so a lookup is an
array index and a comparison, with no clearing pass and no allocation.

**Controls not drawn produce no record.** This matches `NowControlState`'s own discipline and is what stops the table
serving a `clicked` for a button that stopped existing three seconds ago.

**Rects are opt-in.** `bool Draw()` discards the rect (`NowControlBuilders.cs:132-140`); `{ rect: true }` switches
that control to its `Begin()` path, which reserves an area, opens a mask and a row, and is a materially heavier call
(`:99-130`). One further wrinkle the generator must handle: `NowButton.Begin()` warns in development builds when a
label was set (`:100-103`), so the `{ rect: true }` path builds the button without a label and draws the label as
content inside the scope.

Cost: the 23 controls of §1 produce 23 records, about 100 slots. They ride back in the same `Record` call the ops go
out on, so they cost no crossing at all.

### 6.2 What "one frame old" means, and where it shows

```
frame N:    input drained → JS draws (reads N−1's results) → replay (writes N's results)
frame N+1:  input drained → JS draws (reads N's results)   → replay
```

A click landing in frame N's input is seen by frame N's replay, is in frame N's result table, and is read by
JavaScript at the start of frame N+1 — whose replay draws the consequence. **A click becomes visible one frame after
it happens.** At 60 Hz that is 16 ms, below the threshold where a pointer interaction reads as laggy, and it is the
same one frame that focus navigation, pointer arbitration and overlay blocking already cost inside C#
(M3-SurfaceScout §4.4).

Two places where it is genuinely visible:

**(a) A value derived in JavaScript lags its control by one frame.** In §1, `ui.text(String(state.priority))` sits
beside the slider. Drag the knob: the knob moves immediately, because C# computes the new value inside the same replay
and draws it — but the number beside it is one frame behind. At 60 Hz this is invisible in practice, and it is the one
thing about this design a careful author will eventually notice.

**(b) Anything already one frame late in C# becomes two.** Context-menu item clicks (*"true when it was clicked (the
frame after the click)"*, `NowContextMenu.cs:346`) and dropdown/combo-box selection (*"Selection from the popup
applies on the next frame's Draw (deferred draws run after Draw returns)"*, `NowDropdown.cs:14-15`; *"Selection applies
on the next frame's Draw, matching dropdown behavior"*, `NowComboBox.cs:15`). Through this bridge they are two
frames — 33 ms. The `.d.ts` tags each such member with `@latency 2 frames`, generated from a checked-in list, so it is
visible at the point of use.

**And a thing that is impossible, so it is not offered.** There is no "settle" mode that re-runs the draw function
after the replay to resolve values in the same frame. It cannot exist: settling means drawing the same subtree twice
inside one `Now.StartUI`, and explicit ids are never occurrence-salted (`NowControls.cs:457-462`), so the second draw
would collide with the first on every control and share focus, caret and drag state with itself. The measure pass
inside `RunMeasured` is the one legal double-draw, and it is legal precisely because it is passive — which is also why
it cannot produce the values a settle pass would need. One frame of model lag is a property of this architecture, not
a missing feature.

### 6.3 One-shot events

`clicked`, `submitted`, `dragStarted`, `dragEnded`, `cancelled` are events, not states. The loader marks them unread;
the first `ui.*` call matching the rid returns `true` and clears the bit. A control drawn twice — which throws anyway
(§3.6 check 1) — cannot double-fire. A control not drawn this frame never fires, and its record is dropped at the end
of the frame, so an event can never be delivered late.

### 6.4 The value reconciliation rule

This is the rule Design B got wrong, and it is worth stating with the trace, because the wrong version is plausible
and its failure is a dropped keystroke.

Design B's recorder emitted the caller's value and *then* consulted the result:

```js
emit(OP_SLIDER, rid, value, min, max, opts);          // WRONG
const r = results.get(rid);
return (r && r.changed) ? r.value : value;
```

Trace a keystroke. Frame N−1's replay produced `"abc"` from `"ab"` and set `changed`. At frame N the author's
`state.name` is still `"ab"` — it only receives `"abc"` from this call's return. So the op carries `"ab"`, and frame
N's replay calls `Draw(ref text)` with `"ab"`. The caller's string is authoritative: the field's edit state is clamped
to it (`NowTextField.cs:1398`, `NowTextEdit.Clamp(ref state, text)`), and NowUI stores no caller text of its own. The
character is reverted.

Resolving before emitting fixes that, but on its own it introduces the opposite bug: a programmatic write
(`state.name = ''` in a Clear handler) would be overwritten by the last frame's result forever.

**The rule.** The recorder remembers what it *returned* for each rid last frame. If the incoming value differs, the
author wrote it and the author wins. If it is identical, the author is echoing, and the freshest known value wins.

```js
function value(rid, incoming) {
  const prev = W.lastSent[rid];
  let v = incoming;
  if (prev !== undefined && Object.is(incoming, prev)) {
    const r = R.get(rid);                 // last replay's post-draw value
    if (r !== undefined) v = r.value;
  }
  W.lastSent[rid] = v;
  return v;                               // emitted AND returned
}
```

Four properties, each of which the two-line version lacks:

* a keystroke is never reverted, because the emitted value is the resolved one;
* a programmatic write always lands, because "the author changed it" is distinguishable from "the author echoed it";
* the author never observes a stale value — they observe their own value or a fresher one;
* it is bit-identical when nothing changed: no drift, no snapping, no re-clamping.

The first frame has no `lastSent`, so the author's value seeds the control. Assigning the same value the bridge
returned is indistinguishable from echoing, and harmless.

**A read that could not have been served says so.** Reading a result for a rid with no record — the first frame, or the
frame after a key changed — returns the identity default: `false` for events, the passed-in value for values. Under
`?nowui=debug` (using the query-parameter plumbing already in `WebHostServices.cs:61-68`) the loader counts missed
reads and logs once per rid after that control has been drawn for three consecutive frames without ever producing a
record — the signature of a control the replay is silently not reaching. This is the mirror image of `NowMarkup`'s
query validator (`NowMarkupDocument.cs:208-239`), the only existing mitigation in the codebase for a string lookup
that returns false forever, and it exists for the same reason.

### 6.5 What the result table cannot serve

Taken from M3-SurfaceScout §4.5 and not argued with:

* `NowRichTextResult.layout` is a `NowRichTextLayout` reference type and `TryHit` is a call into it
  (`NowRichText.cs:30`, `:39-48`). Rich-text hit testing is not in the table and cannot be.
* `NowMarkupResult` borrows the document's event buffer and throws when read after the document redraws
  (`NowMarkupState.cs:61-69`). If markup is ever mirrored, the bridge copies the events into its own stream rather
  than handing back a borrowed view.
* `NowSplitViewResult.BeginFirst()` is a method on the result, not data (`NowSplitView.cs:200`). It is expressed as
  the two body callbacks of `ui.split`.
* The text a field holds is not a result at all; the caller owns it, and here the caller is a JavaScript object.

---

## 7. How the surface is produced, and kept from drifting

### 7.1 Source of truth

```
  Standalone/Surface/surface.json                        (hand-written, reviewed, ~900 lines)
        │       ↑
        │       └── resolved by full signature against NowUI.Runtime + the 7 extension assemblies,
        │           loaded with MetadataLoadContext (metadata only, no execution)
        │
        ├─→ Standalone/NowUI.Bridge/Generated/Replay.g.cs        the decode switch
        ├─→ Standalone/NowUI.Bridge/Generated/Ops.g.cs           opcode + signature tables
        ├─→ Standalone/NowUI.Bridge.Tests/Generated/GateFive.g.cs the JS-shaped half of the op-log diff
        ├─→ Standalone/Web/NowUI.Web/wwwroot/nowui/nowui.js      the recorder and the 50 functions
        ├─→ Standalone/Web/NowUI.Web/wwwroot/nowui/nowui.d.ts    types, with the C# XML docs carried over
        ├─→ Standalone/Surface/nowui.surface.json                the opcode manifest        (checked in)
        ├─→ Standalone/Surface/nowui.unsupported.json            exclusions with reasons    (checked in)
        ├─→ Docs/Standalone/M3-Surface.md                        the reference the author reads
        └─→ Docs/Standalone/M3-SurfaceCoverage.md                what is NOT covered  ← the important one
```

**Not the api dump.** `Tools/Standalone/ApiDump` sorts every line ordinally across the whole assembly
(`ApiDump/Program.cs:155-156`, verified) and its line format carries the *return* type, not the declaring type —
`method NowUI.NowButton SetWidth(System.Single)` sits adjacent to `method NowUI.NowCheckbox SetWidth(System.Single)`
with nothing saying which type each belongs to. Tier 2's `ui.x.<Type>.<Member>` namespace needs exactly that
information. The dump stays what it already is: the delta gate (G2).

**Not the attributes.** `[NowBuilder]`/`[NowConsumer]`/`[NowScope]` mark return types, never entry points.
`NowControlFactories.cs` — the file containing every control factory in the library — carries none. They become
cross-checks (G6), which is the one job M3-SurfaceScout §1.5 leaves them fit for.

### 7.2 A specification entry

```jsonc
{
  "js": "slider", "key": "required",
  "params": [ { "name": "value", "kind": "f32" },
              { "name": "min",   "kind": "f32" },
              { "name": "max",   "kind": "f32" } ],
  "opts":   [ { "name": "step",  "kind": "f32", "setter": "SetStep(System.Single)" },
              { "name": "width", "kind": "f32", "setter": "SetWidth(System.Single)" },
              { "name": "grow",  "kind": "f32", "setter": "SetStretchWidth(System.Single)" } ],
  "csharp": { "factory":  "NowUI.NowLayout.Slider(System.Single,System.Single)",
              "id":       "SetId(NowUI.NowId)",
              "consumer": "Draw(System.Single&)" },
  "result": { "valueKind": "f32", "flags": "interaction" },
  "gate5":  "SliderTwin"
}
```

Every `factory`, `setter`, `consumer` and `enum` is a **full signature** and is resolved against the assemblies. A
rename, a changed parameter type, a removed overload or a new required parameter fails the generator with a message
naming the member.

### 7.3 Aliases and composites are declared, never invented

The three aliases (`heading`, `subheading`, `caption`) and the six composites (`card`, `when`, `list`, `radio`,
`rule`, and the `disabled` option) are declared in `surface.json` with `"alias"` or `"composite"` and their expansion.
Two rules hold them down:

1. **A composite must name a `gate5` twin**, and the generator fails if the named twin does not exist in
   `Standalone/NowUI.Bridge.Tests`. The twin is hand-written C# that draws what the composite claims to draw.
2. **The composite budget is six and is recorded in this document.** A seventh requires an edit here saying why.

This is the direct answer to Design B's own §6.3, which conceded that everything the bridge adds beyond the raw call
can drift from C# with the build green, the coverage report clean and the golden buffer passing. Under this rule the
set of things that can drift is enumerable, capped, and each has a test that a C#-side change breaks.

### 7.4 The gates

| # | Gate | Catches |
|---|---|---|
| **G1** | **A compile error, not a test failure.** `Replay.g.cs` emits fully explicit C# — named methods, named setters, declared argument types, no `dynamic`, no reflection. If `SetStep` is renamed, if `Draw(ref float)` becomes `Draw(in float)`, if `Slider(float,float)` gains a required parameter, **the standalone solution does not compile**, in the same PR that made the change | signature drift |
| **G2** | The existing public-API delta gate (`Tools/Standalone/Dump-PublicApi.ps1`, `StandaloneCoreDesign.md:78`) | removals and Unity/standalone divergence, before the bridge sees them |
| **G3** | The manifest is checked in; CI regenerates and diffs. Opcodes are content-hashed, not ordinal. `nowui.js` embeds the manifest hash and the first `Record` of a session carries it; a mismatch refuses the frame naming both hashes | a renumbered opcode; a stale `nowui.js` against a newer wasm module |
| **G4** | The coverage report and `nowui.unsupported.json` are checked in and diffed. Every excluded member carries a machine-readable reason code: `interior-reference`, `clr-object`, `host-hook`, `frame-boundary`, `span`, `unity-render`, `managed-handle`, `generic`, `editor-only` | omission becoming an accumulating silence rather than a reviewed act; an author concluding the mirror is incomplete by accident |
| **G5** | **The op-log diff.** For every tier-1 function and every composite, `Standalone/NowUI.Bridge.Tests` drives the JS-shaped call through the bridge and the hand-written C# twin through the same frame, both against `NowRecordingRenderBackend` (`Standalone/NowUI.Engine/Backend/NowRecordingRenderBackend.cs:32`, verified to exist), and diffs the op logs. Headless, no browser, no GPU | the bridge behaving differently from C# with no signature changing — the one class G1 cannot reach |
| **G6** | Three attribute cross-checks: every `[NowBuilder]` type is covered or excluded with a reason; every `[NowScope]` type reachable from a covered function is replayed inside a `using`, or is one of the two known unmarked public disposables (`Now.TextContextScope`, `NowNodeGraphEvaluator<T>.BatchScope`); every `[NowConsumer]` method classifies as terminal | a new control appearing in the frozen tree and quietly not existing in JavaScript; a third unmarked public `IDisposable` appearing |
| **G7** | The behavioural harness: synthetic input in, JavaScript-visible outcomes asserted — *"after a click on the rect at (612,288), the next frame's result table has `clicked` on rid X and only on rid X"* | latching, the reconciliation rule, one-shot consumption, the not-drawn drop |
| **G8** | Every exported function in `nowui.d.ts` has a matching opcode in `Ops.g.cs`, and vice versa | a spec entry generated into one artifact but not the other |
| **G9** | **The load-bearing identity assertion.** `NowControls.GetControlId(new NowId(h))` (`NowControls.cs:476`) under scope *S* equals what `builder.SetId(new NowId(h))` resolves to under *S*, asserted for nested scopes and for list items. Both should funnel through `NowControls.cs:498-504` → `CurrentIdentityParent().Derive(NowIdDomain.Control, id)`; "should" is not "does". Not merely prudent: `ui.contextMenu` cannot be written without it (§2.6) | the assumption every rid, every diagnostic, every state lookup and the whole of `ui.contextMenu` rests on |
| **G10** | Invariant I1: every generated factory chain contains exactly one `SetId` | the flat-site-token identity trap of §3.3 |

### 7.5 Tier 2, and below it C#

Tier 1 will not cover something; 50 functions against 184 factories and 830 setters, and the gap is by design. So
`SurfaceGen` also emits **tier 2**, mechanically, from assembly metadata, with no hand-written spec:

```js
ui.x.NowLayout.Chip('tag', { label: 'urgent', style: 'accentSoft' });
ui.x.NowLayout.MaskField('layers', { in: state.layers });   // → result.out.layers
```

The inclusion rule is mechanical and total: a public static factory returning a `[NowBuilder]`, plus its `Set*`/`With*`
setters and its consumers, is included **iff** every parameter is a primitive, string, enum, `NowRect`,
`Vector2/3/4`, `Color`, `NowId`, or a `ref`/`out` of one of those, and every consumer returns `void`, `bool`, or a
result struct whose fields are all of those kinds. Everything else is excluded mechanically with a generated reason
string per exclusion (G4).

Tier 2's ergonomics are deliberately worse: no key defaulting, no reconciliation rule (a `ref` argument becomes
`{ in: x }` and `result.out.x`), and **it returns result objects**. That reintroduces the one trap tier 1 does not
have: `if (result)` is always true in JavaScript, because `if (obj)` consults neither `valueOf` nor
`Symbol.toPrimitive`, where C#'s `implicit operator bool` (`NowTextField.cs:356`) would have read `.changed`. It is
unfixable at the language level. Two mitigations, both scoped to tier 2: the `.d.ts` types the return so every editor
flags it, and the runtime tracks whether each returned result object had any property read before the next frame,
warning once if not — *"a NowTextFieldResult was returned and never read — did you mean .changed?"* — at the cost of
one boolean per result object per frame.

**And below tier 2 there is C#.** `ui.host(key, name, props, body)` invokes a handler registered on the wasm side by
name. Anything a delegate or a managed handle gates and that is not in §5.8's recorded-subtree set is reachable only
this way, and reaching it means somebody writes twenty lines of C# and rebuilds the wasm module. **An AI that can only
write JavaScript cannot do that.** That is the honest boundary of this design; §8 lists what falls outside it, and
§5.8 plus W11 is what shrinks it.

---

## 8. What the first release cannot do

Stated as decisions, not as gaps to be discovered.

1. **Every value a control returns describes the previous frame.** There is no settle mode and there cannot be
   (§6.2). Reaction latency is ~16 ms.
2. **Dropdown, combo-box and context-menu selection are two frames late**, because they are already one frame late in
   C#. 33 ms. Tagged per member in the `.d.ts`.
   *Measured 2026-09-08, and it cost a defect to learn:* being one frame late in C# means the choice is handed over
   through a one-shot slot in `NowControlState`, which `Draw` drained with no passive/measure guard
   (`NowDropdown.cs`, and the same shape in `NowComboBox`, `NowMaskField`, the colour, gradient and curve fields
   and `NowDatePicker`). Under item 23's two passes the MEASURE pass drained it and the choice never reached
   JavaScript at all — the popup opened, highlighted and closed with the value unchanged, which is
   indistinguishable from a click outside. **Fixed in the package the same day**: the commit itself is now
   guarded by `!NowInput.isPassive`, so a passive pass observes without mutating, which is the rule the rest of
   `NowControlState` already followed. Every popup-bearing control in §2.5 commits under both settings of
   `exactLayout`, verified with the interim bridge-side carry deleted. `ui.timePicker` was never affected: it
   recomputes from persistent state each pass. See §0.5 item 4 of `M2-FeatureMatrix.md` for the full account
   and the evidence.
3. **`if (result)` on a result object is always true.** Structurally absent from tier 1, because no tier-1 function
   returns an object. Present in tier 2, mitigated by the `.d.ts` and the never-read warning (§7.5).
4. **`ui.*` is synchronous-only.** No `await` in a draw function, no drawing from a timer, promise or event handler.
   Change `state` and let the next frame draw it.
5. **The same path cannot be drawn twice in one frame.** Ever. It throws.
6. **Anonymous scopes are positional.** `ui.when` and `opts.key` are the two escapes, and check 2 reports the shift
   (§3.5, §3.6).
7. **No docking.** `NowDockSpace.Window` is an instance method on a reference type (`NowDocking.cs:187`, `:198`), so
   it needs a managed-handle mechanism as well as a recorded subtree. The Docking extension links and works in the
   browser; it is not reachable from JavaScript in the first release.
8. **No node-graph custom content.** `SetNodeContent(Action<NowNode, NowRect>)` (`NowNodeGraph.cs:3889`) is reachable
   by §5.8's mechanism, subject to the no-branching restriction, and is not in the first release. A node graph with
   default node rendering is reachable through tier 2.
9. **No `NowInspector`.** `Draw<T>(ref T)` is generic over an arbitrary struct and `Draw(object)` reflects over a CLR
   object graph. Neither has a JavaScript analogue. Excluded permanently.
10. **No `NowControlState.Get<T>`.** It returns `ref T` where `T : struct` (`NowControlState.cs:77`) — an interior
    reference to a generic value type, which cannot cross any boundary in either direction. A JavaScript author cannot
    attach custom per-control state to a NowUI id; they keep it in their own object.
11. **No tree view, no file picker, no image control in the first release.** `NowLayout.TreeView` requires a
    `NowTreeViewState` reference type (`NowControlFactories.cs:335`), and `NowFilePicker` is a host-integration
    question in a browser. There is no image control in `NowUI.Runtime` at all; images are drawn through the
    texture-carrying draw primitives, which need the managed-handle mechanism.
12. **No custom themes or fonts, and no theme by asset name.** `NowThemeAsset` (91 parameter occurrences) and
    `NowFontAsset` (21) are managed reference types. `ui.theme('light' | 'dark', body)` works and is the whole of
    what is offered: the bridge builds those two assets itself, the way `NowTheme` builds its own defaults, so no
    resource manifest is involved. A theme by ASSET NAME does need one and is absent. Authoring a theme from
    JavaScript is not offered either, and that is a real limitation for the "an AI writes an app" story — an
    author can colour any individual shape with `{ color }` (§2.3b), but cannot change what `accent` means.
13. **No custom SDF material, no model preview, no IMGUI.** `NowSdfBuilder.SetMaterial` already cannot work in this
    host — the browser backend throws on any program outside the ten hand-ported ones (`M2-FeatureMatrix.md:78-82`).
    `Now.Model`/`NowModelPreview` is excluded from the standalone build entirely (`StandaloneCoreDesign.md:1675`) and
    recorded in the public-API delta. `NowGUI`/`NowGUILayout` are compiled and reachable from C#; they are not
    mirrored.
14. **The intern table does not evict.** 65 536 distinct keys and labels per session, then a key throws and a label
    degrades to volatile. A session that mints a new key per incoming message will hit it. Eviction is a follow-up
    unit, not a first-release feature.
15. **Ephemeral state has a ten-second memory.** A control not drawn for more than ten seconds loses its caret,
    selection, undo history and scroll offset (`NowControlState.cs:35`). Values are unaffected — they are in
    JavaScript.
16. **A rect is not in the result table unless asked for**, because `Draw()` discards it and `Begin()` is a heavier
    call (§6.1).
17. **No `ReadOnlySpan<char>` overloads.** The 24 span positions are unreachable; the string overload beside each is
    used instead, at one managed string allocation per distinct volatile string per frame.
18. **No live-object results.** Rich-text hit testing and markup event buffers are not in the result table and cannot
    be (§6.5).
19. **One mount, one canvas, one `Now.StartUI` per frame** (`Now.cs:1271-1277`).
20. **No virtualization.** A `ui.list` over ten thousand rows is ten thousand `KeyedItemIn` derivations and ten
    thousand controls NowUI actually draws. The intended answer is a `ui.scroll` with a declared row height that
    reports its visible range, so JavaScript emits only the visible rows. Designed for, not built. Until it exists the
    practical ceiling is a few thousand controls.
21. **Accessibility is nil.** The canvas is opaque to screen readers and nothing in this design changes that. For a
    milestone framed as "an AI writes an app and serves it to someone", that is worth stating out loud even though it
    is out of scope: a person using a screen reader cannot use the output of this design at all.
22. **Two memcpys per frame**, one each way, unavoidable through the public `MemoryView` API (§5.1). Small — about
    8 KB for a realistic frame — but not zero and not removable.
23. **`exactLayout` runs the whole UI twice per frame in wasm.** On by default because the alternative reads as a
    flaky UI on first appearance and while animating; `{ exactLayout: false }` is the switch.
    The second pass is not free of consequences, and item 2 records the one that bit: a control that hands a value
    to the next frame through latched state has that state drained by the measure pass, whose results are
    discarded. The decode is still side-effect free (§5.6) — the side effect is inside NowUI's own control state,
    not in the decode. The bridge carried such a value across the two passes until the package was fixed on
    2026-09-08; that carry has since been deleted, and item 2 above records what replaced it.
24. **The C# escape hatch is not available to a JavaScript-only author.** `ui.host` requires somebody to write and
    compile C#. This is the price of a curated surface, and it is charged in exactly the places listed as items 7-13.

### What the DRAWING surface cannot do

Added with §2.3b, and stated the same way: decisions, not gaps to be discovered.

25. **A canvas that GROWS is one frame stale in its size.** The origin is always exact — the decoder adds it — so
    nothing ever lands in the wrong place; but a responsive chart is drawn at the old size for one frame after a
    resize, and at 0 × 0 on its very first frame. Declaring `width` and `height` removes the lag entirely and
    `box.stale` says which you got. It cannot be removed in general without doing layout in JavaScript.
26. **No textures or images on a drawing.** `NowRectangle.SetTexture` exists and no `texture` option is exposed: a
    texture is a HANDLE, and handles need the host resource manifest that item 12 also lacks. When that lands, a
    texture is one option bit carrying a handle and nothing in §2.3b moves.
27. **No transforms, no rotation.** Available rather than absent — `Now` has a transform stack — but a transform is
    a SCOPE carrying a matrix, and it interacts with mask bounds and with hit testing. Deferred rather than
    specified badly; it is the first thing the option mask's reserved bits should buy.
28. **No hit testing on a drawing, and no text measurement read-back.** Both need a result channel keyed to
    something that is not a control rid, which is a §6 redesign rather than an addition. A drawing has no
    identity (§2.3b) and therefore nothing to key a result to. Put a `ui.button` over the shape instead.
29. **No SDF shapes, node graph, markdown or code editor from JavaScript.** Each is a sub-surface rather than an
    op: the node graph needs a graph model with per-node identity ancestry, markdown needs a document model plus
    link callbacks, the code editor needs a text buffer that would cross the boundary on every keystroke — which
    the volatile-string channel is not sized for — and SDF needs shape assets, i.e. item 26's manifest again.
    None is expressible as "one op with arguments", which is the unit this ABI is built from.
30. **No line gradients or arrowheads.** `NowLine.colorEnd` and `NowLine.arrows` are real NowUI features, omitted
    only to keep option bits in reserve. Reachable later with no wire break.
31. **`fontSize` is reserved and does nothing.** It holds a bit on the wire; NowUI's label path takes a resolved
    `NowTextStyle` rather than a size, so there is nothing to set. Asking for it throws, naming `textStyle`.
32. **Wrapping existing controls in a `ui.mask` resets their keyed state once.** A mask opens an identity scope
    like every other scope, so it changes the canonical path of everything inside it — scroll offset, caret and
    foldout state reset on the frame the mask is introduced. Same cost as wrapping them in a `ui.column`.

---

## 9. Work breakdown to a first working application

"First working application" means: `app.js` from §1 renders, is clickable, and its state round-trips. That is the end
of **W6**. W7-W9 are what make it safe to hand to somebody; W10-W12 are what make it a product.

New code lives under `Standalone/NowUI.Bridge` (a new project referencing `NowUI.Runtime` only),
`Standalone/NowUI.Bridge.Tests`, `Standalone/Surface`, `Tools/Standalone/SurfaceGen`, and
`Standalone/Web/NowUI.Web/wwwroot/nowui`. Nothing under `Assets/NowUI` is touched.

---

**W1 — Transport spike.** *No dependencies.*
Files: `Standalone/NowUI.Bridge/BridgeInterop.cs`, `Standalone/Web/NowUI.Web/wwwroot/nowui/spike.js`.
Prove `MemoryView.set()` exists and works on a `[JSImport]` `Span<int>` parameter, and measure the round trip: write
1 MB from JavaScript, `set()` it in, read it back with `slice()`, compare. If `set()` is unavailable, the `double[]`
fallback (`WebInput.cs:401-405`) lands here and §5.1's cost table is revised.
**Acceptance:** a number for the per-frame copy cost at 8 KB and at 1 MB, and a green byte-comparison round trip. The
entire transport risk is retired before anything is built on it.

**W2 — Buffers and the op stream.** *Depends on W1.*
Files: `Standalone/NowUI.Bridge/BridgeInterop.cs`, `Recorder.cs` (C# side: buffers, growth, preamble decode,
validation), `wwwroot/nowui/recorder.js` (JS side: the growable ArrayBuffer, `emit`, the string tables, the header).
One hard-coded `OP_TEXT` opcode, no controls, no identity.
**Acceptance:** a wasm frame calls JavaScript, JavaScript writes eight slots and a string, and the word "hello" appears
on the canvas. A deliberately truncated buffer is refused by §5.5's validator with a slot offset, and NowUI is never
touched.

**W3 — Identity.** *Depends on W2.*
Files: `wwwroot/nowui/trie.js` (path trie, rid allocation, segment interning, the anonymous-ordinal counters),
`Standalone/NowUI.Bridge/Replay.Identity.cs` (`IdScope(int)`, `KeyedItemIn`, `SetId(new NowId(h))`, the root scope),
`Standalone/NowUI.Bridge.Tests/IdentityTests.cs`.
Includes G9's assertion and G10's invariant, and check 1 and check 2 of §3.6.
**Acceptance:** (a) G9 passes for nested scopes and list items; (b) two buttons with the same key in different columns
coexist and two in the same column throw with the canonical path in the message; (c) check 2 fires on a conditional
anonymous sibling and stays silent when `ui.when` is used; (d) a container the replay opens without `SetId` fails the
build.

**W4 — Replay and the frame.** *Depends on W3.*
Files: `Standalone/NowUI.Bridge/Replay.cs` (the decode switch, hand-written for six opcodes), `BridgeHost.cs` in
`NowUI.Web` wiring §5.7's ordering into `Program.Frame()`, `s_Replay` as a cached static delegate.
**Acceptance:** the six opcodes draw inside `Now.StartUI`; `exactLayout` on and off both produce the same draw list
against `NowRecordingRenderBackend`; the replay is provably called twice under `RunMeasured` and once without it; a
JavaScript `throw` mid-draw leaves NowUI untouched and produces a balanced short buffer.

**W5 — The result table.** *Depends on W4.*
Files: `Standalone/NowUI.Bridge/Results.cs` (record layout, `WriteResult`, measure-pass suppression),
`wwwroot/nowui/results.js` (the loader, the generation stamp, one-shot latching, the reconciliation rule of §6.4).
**Acceptance:** `if (ui.button('Add'))` fires exactly once per click; `state.name = ui.textField('name', state.name)`
round-trips a typed character with no revert; a programmatic `state.name = ''` in a handler clears the field on the
next frame; a result written during a measure pass never reaches JavaScript.

**W6 — The 41 functions, hand-written.** *Depends on W5.*
Files: `wwwroot/nowui/nowui.js` (all 41, hand-written for now), `Standalone/NowUI.Bridge/Replay.Controls.cs`,
`wwwroot/app.js`, `Program.cs` gains an `?app=` selector alongside `?area=` following the `FeatureGallery.cs` pattern.
**Acceptance: `app.js` from §1 runs.** People add and remove; the roster scrolls and keeps its offset across an add;
the slider drags; the dropdown selects; reordering `state.team` moves neither a caret nor the scroll position. **This
is the first working application.**

**W7 — Faults, diagnostics, hot reload.** *Depends on W6.*
Files: `wwwroot/nowui/faults.js`, `Standalone/NowUI.Bridge/Banner.cs`, `Replay.Poison.cs`.
Includes §4.4's six steps, §4.5's skip set, the `s_Failed` isolation, `NowRuntime.ResetAll` wired to a hot-reload path,
and the `?nowui=debug` reads of §6.4.
**Acceptance:** deleting a `)` in `app.js` shows a banner naming the line instead of a blank canvas, and fixing it
recovers without a page reload; a control that throws in the replay leaves a hole and a message rather than killing the
frame loop; `Program.cs`'s `s_Failed` is never set by an author error.

**W8 — The generator.** *Depends on W6.*
Files: `Tools/Standalone/SurfaceGen/**`, `Standalone/Surface/surface.json` filled to all 41 entries plus the aliases
and composites, and the eight generated artifacts of §7.1.
Gates G1, G3, G4, G8 wired into CI.
**Acceptance:** the hand-written 41 from W6 are deleted and regenerated byte-comparably; renaming a setter in a scratch
copy of `NowUI.Runtime` fails the build with a message naming the member; adding a factory makes the coverage file
dirty; a `nowui.js` built from a different commit is refused at boot with both hashes in the message.

**W9 — The gates that catch behaviour.** *Depends on W8.*
Files: `Standalone/NowUI.Bridge.Tests/GateFive/**` (the hand-written C# twins), `Generated/GateFive.g.cs`,
`BehaviourHarness.cs`.
Gates G5, G6, G7, and the composite-twin requirement of §7.3.
**Acceptance:** the op-log diff passes for all 41 functions and all five composites; a deliberate one-character change
to a composite's expansion fails G5 with the diverging op named; a new `[NowBuilder]` in a scratch assembly fails G6.

**W10 — Tier 2 and the long tail.** *Depends on W8.*
Files: `Tools/Standalone/SurfaceGen/Tier2.cs`, `Standalone/Surface/nowui.unsupported.json`,
`Standalone/NowUI.Bridge/HostRegistry.cs`.
**Acceptance:** a control in no spec entry is callable as `ui.x.NowLayout.<Name>(...)`; every exclusion has a reason
code; the never-read-result warning fires on a discarded tier-2 result and never on tier 1.

**W11 — Delegate subtrees.** *Depends on W9.*
Files: `wwwroot/nowui/subtree.js`, `Standalone/NowUI.Bridge/Replay.Callbacks.cs`.
`OP_CALLBACK_BEGIN`/`END`, the sub-span `Action`, and `ui.overlay` as the first and only consumer in this unit.
**Acceptance:** a floating panel positioned by `ui.overlay` draws above everything, blocks pointer input inside its
rect, resolves its controls' identities under the correct ancestry (proved by a focus test), and is entered exactly
once per frame under `exactLayout`.

**W12 — The author's documentation, and the milestone's acceptance test.** *Depends on W8.*
Files: `Docs/Standalone/M3-Surface.md` (generated), plus one hand-written page: the six rules of §1.1, the identity
rule of §3.2, the one-frame rule of §6.2, the limits of §8 phrased as rules rather than caveats, and three complete
example applications. Written for a model reading it as context — no prose without a code block beside it, every
function shown at a call site.
**Acceptance: an AI given only that page and no other context writes §1's application, correctly, on the first
attempt.** That is the milestone's actual acceptance test.

**Sequencing note.** The generator arrives in W8, *after* two slices of hand-written surface, deliberately. Writing the
recorder and decoder by hand for a handful of opcodes is what establishes the shape the generator should emit;
generating first would fix the wrong shape into a template before anything had run. The hand-written functions are
regenerated and deleted in W8.

**Twelve work units. W1-W6 is the critical path to a running application.**

---

## 10. Decision log

Each entry: the contested choice, what was chosen, what was rejected and why.

**D1 — The base design. Chose B; rejected C as the base.**
B and C tied on judged total (90 each). B carries the single highest scorecard and wins the lens the milestone is
framed by. The deciding argument is not the score: it is that C deletes `if (button())` and says so — *"Anything that
reads a control's result and branches on it within the same frame must become `onEvent` + state + rebuild"* — and that
is the idiom of the library being mirrored. Deleting it makes the JavaScript API a different library that happens to
render through NowUI, which also makes the op-log diff (G5) a much weaker instrument, because there is no longer a
hand-written C# equivalent that means the same thing. *What C had that is not recovered:* a whole-tree submission is
inherently idempotent as data, whereas this design's idempotence is a property the decoder must maintain (§5.6); and
C's `h(type, props, children)` has the strongest raw prior in a JavaScript training corpus. *What is recovered:* the
reorder detector (§3.6), gate 5 (§7.4), the delegate subtrees (§5.8), the falsy-slot discipline (§3.5), and — through
§5.7's ordering alone — C's strongest property, that NowUI is never touched when the author's code throws (§4.4).

**D2 — The identity segment. Chose the interned handle as an integer `NowId`; rejected the key string and the 64-bit
hash.** A string segment costs a content hash per control per frame (`NowIdHash.cs:105-113`, no reference fast path
for authored strings). A 64-bit hash is not expressible as a `NowId` at all — the constructor takes a string or a
32-bit int (`NowId.cs:42`, `:60`) — which is the defect that made Design B's `SetId(Id(p+1))` unwritable. An integer
handle is total, allocation-free, and cannot collide across the two integer namespaces (§3.3).

**D3 — The rid. Chose a dense path-trie integer; rejected a hash of the path.** Design B's `Set<number>` of 64-bit
rids cannot hold 64 bits, because a JavaScript number carries 53. The judges' remedy was better arithmetic; the better
remedy is not needing arithmetic. A trie is exact at any depth, turns the result table into an array index, and gives
every diagnostic a real path for free (§3.4). Cost: one `Map.get` per segment per frame and one trie node per distinct
path ever seen, evicted at 600 frames.

**D4 — Transport. Chose JavaScript-owned buffers with one copy each way; rejected shared views held across calls.**
The repository says a `MemoryView` is valid only for the duration of the call and that `slice()` copies
(`WebGL2Backend.cs:1941-1945`, `nowui-gl.js:3668-3673`). All three designs claimed no copy; A additionally held views
across calls and returned fresh views from `Grow`, neither of which is supported. Owning the buffer in JavaScript is
simpler, makes growth free and never re-runs the author's draw function on a retry, and costs ~8 KB of memcpy per
frame against a backend that already moves two orders of magnitude more.

**D5 — Where Record runs. Chose outside `Now.StartUI` and outside `RunMeasured`.** Design B's diagram put it inside
the `RunMeasured` delegate; `RunMeasured` calls its delegate twice (`NowLayout.cs:1578-1590`), so the author's draw
would run twice, read state twice, and fire every handler twice. Putting Record first also buys C's error-containment
property: at the moment the author's code throws, NowUI has not been touched.

**D6 — The value rule. Chose reconciliation against the last returned value; rejected `changed ? result : incoming`.**
The rejected rule emits before it resolves and reverts a keystroke, traced through `NowTextEdit.Clamp(ref state, text)`
— the caller's string is authoritative. Resolving first alone breaks programmatic writes. Comparing against what the
bridge last returned distinguishes "the author wrote it" from "the author echoed it" and is correct in both directions
(§6.4).

**D7 — `disabled`. Chose to synthesize it, as a declared composite with a gate-5 twin; rejected both B's undeclared
synthesis and C's refusal.** NowUI has no disabled concept, so any `disabled` is invented behaviour — which is exactly
the class Design B admitted nothing could test. C's answer (refuse it) is honest and loses the single prop a model
writing React by habit is most likely to reach for. Declaring the expansion and requiring a hand-written C# twin makes
the invention testable, which is what makes it acceptable. Its real limits — still hoverable, still focusable, still
in the navigation order — are documented at the option, not in a footnote.

**D8 — Fault behaviour. Chose replaying the partial buffer; kept last-good as a flag.** Freezing on the last complete
frame makes the app look alive and correct while being neither, and hides where it stopped. Drawing what actually got
emitted shows the failure point as a visual boundary. Since the buffer is balanced by construction, "partial" never
means "unbalanced" — the guarantee `Now.StartUI` actually needs. C's argument for last-good is strong for a *persistent*
fault, so it is the behaviour after 120 consecutive faulted frames and is available as `{ onFault: 'lastGood' }`.

**D9 — `exactLayout` default. Chose on; rejected off.** It costs a second full execution of the UI in wasm, which is
real. Off, auto-sized groups, stretch shares and flexible space resolve from the previous frame
(`NowLayout.cs:1263-1266`), which produces a visible settle on the first frame a control appears and on every frame it
animates — indistinguishable, to an author, from the flakiness this milestone is trying to avoid. The switch exists
and is documented.

**D10 — C's idle-frame resubmit. Rejected, with reason.** C skips rebuilding and re-encoding on frames where nothing
is dirty, and gets that for free because a declarative tree has a dirty flag driven by event dispatch. An immediate
API has no such flag: the draw function reads arbitrary state and "nothing changed" is not knowable without running
it. The graft therefore does not apply. Its benefit here would also be small: recording the §1 application is ~220
slot writes, microseconds, and the replay must run anyway for hover transitions and caret blink. *One adoptable half*
is noted for later: on a frame whose ops buffer is byte-identical to the previous frame's, the measure pass could be
skipped while keeping exact layout, because identical layout inputs give identical measurements. It is not in the
first release because the precondition is not quite true — a font finishing an async load or an image decoding changes
content without changing the buffer — and getting that wrong is a stale-layout bug.

**D11 — Aliases and composites. Chose "declared, generated, capped at five, each with a hand-written C# twin";
rejected both free invention and total refusal.** This is the direct answer to Design B's §6.3, which conceded that
everything the bridge adds beyond the raw call can drift with every gate green.

**D12 — Generation source. Chose assembly metadata via `MetadataLoadContext`; rejected the api dump and the
attributes.** The dump sorts ordinally across the whole assembly and carries the return type rather than the declaring
type (`ApiDump/Program.cs:155-156`), which is precisely what tier 2's namespace needs. The attributes mark return
types, never entry points. Both keep a job: the dump is the delta gate (G2), the attributes are cross-checks (G6).

**D13 — Scopes. Chose callbacks only in tier 1; rejected a begin/end pair.** `NowContextMenu` proves an imperative
pair is expressible, but an unclosed scope in JavaScript does not produce a missing box — it produces an
`InvalidOperationException` out of the *next* frame's `Now.StartUI`, a stack trace with no visible relationship to the
bug.

**D14 — Result objects. Chose primitives only in tier 1; accepted objects in tier 2.** `if (obj)` is always true in
JavaScript and consults neither `valueOf` nor `Symbol.toPrimitive`. Making every tier-1 function return a primitive
removes the trap by construction; tier 2 cannot, so it gets the `.d.ts` type and the never-read warning.

**D15 — `ui.list`'s `keyOf`. Chose required, with no index default.** Eight characters, and it forecloses the most
common state-follows-the-wrong-row bug in every immediate-mode UI. `keyOf: (_, i) => i` is still writable and still
wrong for a reordering array — but now it is a decision the author wrote down.

**D16 — The reorder detector's default. Chose on in Release; rejected `?nowui=debug`.** The failure it names is silent
by definition. A diagnostic for a silent failure that is off by default is a diagnostic for people who already suspect
the bug. Cost is one string array per trie node and an elementwise compare, capped at 64 reports per session.

**D17 — Surface size. Chose 41 functions; rejected both 58 and 2,890.** B's 58 included controls that do not exist in
`NowUI.Runtime` as single builders (`link`, `iconButton`, `note`, `confirm`, `tooltip`) and would have been invented
composites, which is the drift D11 exists to bound. A's 2,890 is the faithful mirror and buys zero drift and total
reach at +180 KB of defeated trimming and a surface no model browses.

### Where the three designs disagreed most sharply

Four disagreements, and they are the part of this record worth understanding.

**1. Where identity comes from when there is no call site.** This is the real question the milestone poses and the
three answers are genuinely different. A invents it *at call time* from the order of the calls — a per-kind ordinal
within scope — which is the most faithful analogue of `[CallerLineNumber]` and which fails exactly when the call order
changes, which is exactly when a branch flips. C derives it from *structure* — the path of keys through a tree built
before the frame — which is the strongest of the three and is why the whole tree exists. B requires the *author* to
supply it, per interactive control, and makes that the one thing the author must internalise.

This specification takes B's answer and then takes C's diagnostic, and the reason is worth stating: B's answer makes
controls non-positional by construction, which removes the largest failure class outright; what remains positional is
only anonymous scopes; and C's key-vector check is the only diagnostic any of the three designs proposed that can name
that residue as a path, a slot and a fix rather than as a heuristic. A's shape-hash and label-multiset heuristics are
the weaker instrument, and A says so itself: a reorder of unlabelled items is undetectable.

**2. Whether the JavaScript API should be the C# API.** A says yes, and pays 2,890 functions, +180 KB of defeated
trimming, and a discovery story that depends on a model already knowing the C# library. B and C both say no, and pay
the maintenance of a hand-curated mapping that can drift semantically with every build green. That cost is real, B
admitted it, and the answer this document lands on — enumerate and cap the invented behaviour, require a hand-written
C# twin for each, and diff the op logs — is the merge of C's gate 5 with B's honesty about why it is needed.

**3. Whether `if (button())` survives.** A and B keep it; C deletes it and argues the deletion is the price of
everything else in its table. This is the sharpest single disagreement, because it is the difference between a
JavaScript surface *for* an immediate-mode library and a declarative library that renders through one. Keeping it
means every value the author reads is one frame old and there is no way to hide that; deleting it means the draw-read-
decide loop that immediate mode is best at is gone. Both designs are honest about their side. This document keeps it,
and §6.2 states the cost without decoration.

**4. What the transport actually is.** All three designs believed JavaScript could write into WASM linear memory
without a copy, and cited the same file. The disagreement between them was only about *lifetime* — whether the view
survives the call — and the judges spent their fire there. The repository answers a different and more basic question
in its own JavaScript: `slice()` copies. That neither the designers nor the judges caught it is the best available
argument for W1 existing at all, and for the rule that a spike precedes anything built on an unmeasured assumption.

---

## Appendix A — corrections, with evidence

Each claim was re-checked against the working tree before being accepted or rejected.

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 1 | A's transport holds `MemoryView` spans across calls | **Confirmed, and worse** | `WebGL2Backend.cs:1941-1945` — a span view "is valid only for the duration of the call"; every span there is a `[JSImport]` argument. `Grow` returning fresh views is not supported marshalling. §5.1 |
| 2 | **All three** designs claim no copy in either direction | **Confirmed — found here, not by a judge** | `nowui-gl.js:3668-3673`: *"A .NET MemoryView is valid only for the duration of the call. `slice()` copies it out."* The `MemoryView` proxy's API is `slice`/`set`/`copyTo`, not indexed access. §5.1, D4 |
| 3 | B's `SetId(Id(p+1))` over a 64-bit rid cannot be written | **Confirmed** | `NowId.cs:42`, `:60` — string or 32-bit int only. `NowResolvedId.cs:15` — the only constructor is `internal`. And under B's op layout a key that is not also a label never reached wasm. §3.3, D2 |
| 4 | B's value mechanism emits before it resolves, reverting a keystroke | **Confirmed** | `NowTextField.cs:1398` `NowTextEdit.Clamp(ref state, text)` — the caller's string is authoritative; `NowTextField.cs:685` `Draw(ref string text)` — NowUI stores no caller text. §6.4, D6 |
| 5 | B misstates the cost of `RunMeasured` as "one extra decode" | **Confirmed** | `NowLayout.cs:1578-1590` — `ui()` is invoked twice, once inside `BeginMeasurePass`/`EndMeasurePass` and once after. §5.7 |
| 6 | B's frame diagram puts Record inside `RunMeasured` | **Confirmed** | Same lines; plus `NowLayout.cs:1486-1488` — *"The callback must not mutate state unconditionally."* §5.7, D5 |
| 7 | B's `Set<number>` cannot hold 64 bits | **Confirmed** | A JavaScript number carries a 53-bit mantissa. Removed by construction rather than re-derived: §3.4, D3 |
| 8 | B renders the same path two different ways | **Confirmed, cosmetic** | One canonical rendering is fixed in §3.2 and used in every diagnostic in this document |
| 9 | C states delegate capabilities "work" without the no-branching restriction | **Confirmed** | The subtree is recorded before the callback runs. A states it correctly. §5.8 |
| 10 | C asserts an `ArraySegment` memory view survives across calls | **Partially rejected** | The repository line the judge cited is specifically about `Span<T>`, so C was not contradicted by it. C is nonetheless wrong about the copy — see #2 — and the question is moot here, because nothing is retained across calls. §5.1 |
| 11 | C's key-vector check misses a pure unkeyed reorder | **Confirmed** | Reversing `[row#0, row#1, row#2]` leaves the vector unchanged. In this design controls are never positional and array rendering goes only through `ui.list` with a required `keyOf`, so the residue is anonymous-scope shape change, which the check does detect. §3.6, D15 |
| 12 | C's flagship example passes a controlled `value` to a dragged slider | **Confirmed** | C §1's `taskRow` against C §5.2's own rule. The underlying lesson — a high-rate value round-tripped through a handler fights the pointer — is why §6.4 exists |
| 13 | A's overload-ambiguity denominator is wrong | **Confirmed as framing; the replacement number is not verified** | A's own Appendix A.1 reports `methodsInOverloadGroups=831` for Runtime alone against A.3's 387 groups including extensions; the two populations do not line up. Moot here — there is no full mirror |
| 14 | A's `nowui.js` payload and trim-loss figures reproduce | **Not re-verified** | Judged as reproducing on this machine by one judge. Moot for this specification, and not re-run for it; recorded so the claim is not silently inherited |
| 15 | A's reorder heuristic hashes label *handles*, which change every frame for interpolated labels | **Confirmed** | A §4.3 states a dynamic string "is a fresh JS string each frame, misses the map". Moot — no such heuristic here |

**Three further facts found while verifying, which no design states:**

* **N1 — the flat-site-token trap.** `NowLayout.Column()` captures caller info at the *bridge's* call site
  (`NowLayoutContainers.cs:251-256`). A container the bridge forgets to `SetId` falls into
  `ResolveGroupSiteOccurrence` with one token shared by every container in the application
  (`NowLayout.cs:2481-2498`). Made invariant I1 and gate G10 (§3.3, §7.4).
* **N2 — the host's permanent failure latch.** `Program.cs:330-337` sets `s_Failed = true` and stops the frame loop
  forever. An author's JavaScript error reaching it kills the page. §4.4.
* **N3 — `NowOverlay.Defer` is identity-correct and measure-pass-exempt for free.** It captures the id scope
  (`NowOverlay.cs:848`), restores it when the deferred draw runs (`:1857`), and returns immediately under
  `NowInput.isPassive` (`:836`). This is why overlay is the first delegate capability rather than the last. §5.8.
* **N4 — the context-menu API all three designs cited is obsolete-as-error.** Every positional `Item` and
  `BeginSubmenu` overload carries `[Obsolete(PositionalEntryObsoleteMessage, true)]`
  (`NowContextMenu.cs:44`, `:347`, `:354`, `:361`, `:368`, `:475`, `:482`), as does `Begin(int)` (`:337`). The live
  forms are `Begin(NowResolvedId)` (`:304`) and `Item(string label, NowId id, …)` (`:390`). `Begin` therefore needs a
  `NowResolvedId`, which has no public value constructor — which is what promotes gate G9 from prudence to a
  prerequisite. §2.6, §7.4.

---

## Appendix B — assumptions this design will live or die by

Each has the unit that retires it. None is left to be discovered.

| # | Assumption | Retired by |
|---|---|---|
| 1 | `MemoryView.set()` is available and fast on a `[JSImport]` `Span<int>` parameter | **W1**, before anything is built on it. Fallback: `double[]`, proven at `WebInput.cs:401-405` |
| 2 | `NowControls.GetControlId(new NowId(h))` under scope *S* equals what `SetId(new NowId(h))` resolves to under *S* | **W3 / G9**. Both should funnel through `NowControls.cs:498-504`; "should" is not "does". `ui.contextMenu` does not compile without it |
| 3 | Two `IdScope` pushes per container (identity and layout) are cheap enough at ~60 containers per frame | Unmeasured. If not, the fallback is to open an identity scope only for containers whose subtree contains an identity-bearing control, which the recorder can flag at record time |
| 4 | `!NowLayout.isMeasurePass && !NowInput.isPassive` is sufficient to keep the measure pass from writing results | **W5**. Follows from `NowLayout.cs:1256` and `NowInput.cs:1317` |
| 5 | `NowControlState`'s ten-second eviction and the bridge's 600-frame trie eviction stay in agreement | Tested by leaving a control undrawn for eleven seconds and redrawing it: both forget together, and the control reappears seeded from the author's value |
| 6 | `exactLayout` is affordable at a realistic UI size | Measured in W6 with the §1 application and again with a 300-control synthetic one. If not, the default flips and §8.23 is rewritten |
| 7 | 65 536 interned strings is enough for a session without eviction | Not measured. §8.14 states the limit; the follow-up unit is intern eviction with an explicit release list in the frame preamble |
