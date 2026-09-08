# M3 Surface Scout — the facts a JS-mirror designer needs

**Scope.** This document establishes facts from the source. It designs nothing and recommends nothing except where a
fact directly forecloses an option. Every claim carries a `file:line`. Paths are relative to
`D:/wkspaces/unity/Now-UI/`.

**Method.** Read of `Assets/NowUI/Runtime`, `Assets/NowUI/Extensions`, `Assets/NowUI/Analyzers~`,
`Assets/NowUI/Documentation~`, plus the generated public-API dump at
`artifacts/local/api/after-NowUI.Runtime.api.txt` (2113 public/`protected` methods across 349 public types) used only
for aggregate counts.

**Terminology used below.** *Entry point* = a `public static` factory a caller invokes to start something.
*Builder* = the `[NowBuilder]` struct it returns. *Consumer* = the `Draw()`/`Begin()`/`Reserve()` that makes the
builder do work. *Scope* = an `IDisposable` struct that pushes ambient state.

---

## 0. Headline numbers

| Fact | Count | Where |
|---|---|---|
| `[NowBuilder]` structs (shipping library) | **55** | 58 textual hits; minus 1 in an analyzer doc-comment, 1 in `Example/NowDocsExample.cs:4144` (`MyRating`, a docs sample), 1 private nested (`NowNodeGraph.cs:3527` `CanvasState`) |
| `[NowConsumer]` methods | **38** | 39 textual hits minus 1 analyzer doc-comment |
| …of which the analyzer actually acts on | **29** | see §1.3 |
| `[NowScope]` structs | **22** | 23 hits minus 1 analyzer doc-comment; **20 public**, 2 `internal` |
| `IDisposable` structs in Runtime+Extensions | **29** | 7 carry no `[NowScope]`; **2 of those are public** (§3.5) |
| `public static` factories returning a `[NowBuilder]` | **184** | overload count, not distinct control count |
| `public static` factories returning a `[NowScope]` | **60** | overload count |
| Public fluent methods on builders (`Set*`/`With*`/`Add*`/…) | **830** | `SetId` ×78, `SetWidth` ×38, `SetColor` ×35, … |
| `[CallerFilePath]` declarations | **188** | 188 entry points capture a call site |
| Builders exposing `SetId(NowId)` **and** `SetId(NowResolvedId)` | **39 each** | the other 16 are id-free draw primitives (§2.6) |
| Public methods with `ref` / `out` / `in` parameters | **89 / 103 / 118** | over the whole public Runtime surface |

---

## 1. The builder surface

### 1.1 What the three attributes are

All three are ordinary, non-conditional, `public sealed` attribute classes in namespace `NowUI`, compiled into
`NowUI.Runtime` and therefore present in metadata and reflectable at runtime in the standalone build.

```csharp
// Assets/NowUI/Runtime/Controls/NowBuilderAttribute.cs:11-14
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NowBuilderAttribute : Attribute { }

// Assets/NowUI/Runtime/Controls/NowConsumerAttribute.cs:13-16
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class NowConsumerAttribute : Attribute { }

// Assets/NowUI/Runtime/Controls/NowScopeAttribute.cs:11-14
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class NowScopeAttribute : Attribute { }
```

Their declared meanings, verbatim from the doc comments:

* `[NowBuilder]` — "Marks a struct as an inert NowUI builder: constructing it has no effect until the chain is consumed
  by `Draw()`/`Begin()`." (`NowBuilderAttribute.cs:5-9`)
* `[NowConsumer]` — "Marks a method that consumes a NowUI builder — it performs the actual work (drawing, reserving
  layout space) and returns the value only for further chaining." (`NowConsumerAttribute.cs:5-11`)
* `[NowScope]` — "Marks a disposable struct as a using-only NowUI scope: it restores state exclusively in Dispose and
  has no public end-call alternative, so discarding one as a bare statement is always a bug."
  (`NowScopeAttribute.cs:5-10`)

Note the last clause: `[NowScope]` is a *promise that no `End…()` alternative exists*. §3.4 confirms it holds.

### 1.2 What the analyzer does with them

The analyzer source is `Assets/NowUI/Analyzers~/NowUI.Analyzers/NowBuilderDiscardAnalyzer.cs`; the built binary that
Unity consumes is `Assets/NowUI/Runtime/Analyzers/NowUI.Analyzers.dll` (that folder contains **only** the DLL and its
`.meta` — there is no source under `Runtime/Analyzers`).

It registers a single action on `SyntaxKind.ExpressionStatement` (`:44`) and:

1. returns for assignments, `_ = …`, and `++`/`--` (`:53-58`);
2. returns unless the expression's type is a **struct** (`:60-63`);
3. if the statement is an invocation whose symbol carries `NowConsumerAttribute`, returns (`:67-75`);
4. otherwise reports **NOWUI001** for `NowBuilderAttribute` (`:81-85`) or **NOWUI002** for `NowScopeAttribute`
   (`:87-91`).

Both diagnostics are `DiagnosticSeverity.Warning` (`:24`, `:33`) — in Unity and in the standalone build alike
(`Docs/Standalone/StandaloneCoreDesign.md:75`).

Messages: NOWUI001 `"'{0}' renders nothing until it is consumed — did you forget .Draw() (or .Begin())?"` (`:22`);
NOWUI002 `"'{0}' is a scope and is never disposed here — wrap it in a using statement"` (`:31`).

**What this means for a generator.** The three marks are *lint metadata about C# statement shape*. They encode exactly
three semantic facts a JS mirror does care about — "inert until consumed", "this call is the consumption point",
"this must be disposed" — and nothing else. They do not encode: which entry point produces the builder, what a
consumer returns, whether a builder has identity, or which arguments matter. `[NowConsumer]` in particular does **not**
mean "this is the read-back point"; §1.3 shows a quarter of them are decorative.

### 1.3 `[NowConsumer]` is load-bearing on 29 of 38 methods

Step 3 of the analyzer only matters when the *returned* type would otherwise trip step 4 — i.e. when the return type
is itself `[NowBuilder]` or `[NowScope]`. Classifying all 38:

**Load-bearing (29)** — return a `[NowBuilder]` or `[NowScope]`:
`NowSdf.cs:3109,3115` (`NowSdfBuilder`), `NowSdf.cs:3128,3148` (`NowMaskScope`), `NowLottie.cs:126`,
`NowGlass.cs:162`, `NowGradient.cs:358`, `NowLayout.cs:781` (`NowLabel Reserve()`), `NowLayout.cs:790,801`
(`NowText`), `NowLayout.cs:990` (`NowLottieBuilder Reserve()`), `NowLayout.cs:1000,1011` (`NowLottie`),
`NowLayoutContainers.cs:200` (`NowLayoutScope Begin()`), `NowLine.cs:195`, `NowModelPreview.cs:237`,
`NowRectangle.cs:429`, `NowRipple.cs:81`, `NowShape.cs:83,148,259`, and the eight `NowText.cs:606,618,625,632,639,646,698,705`.

**Decorative (9)** — the return type is `void` or a plain result struct carrying neither mark, so the analyzer would
have returned at step 2 or fallen through step 4 harmlessly: `NowDocking.cs:2027` (`void`),
`NowMarkdown.cs:209,220` (`NowMarkdownResult`), `NowMarkup.cs:25,32,39,46` (`NowMarkupResult`),
`NowNodeGraph.cs:3913` (`NowNodeGraphResult`), `NowRichText.cs:294` (`NowRichTextResult`).
Verified: none of `NowMarkupResult`, `NowMarkdownResult`, `NowRichTextResult`, `NowNodeGraphResult`,
`NowSplitViewResult`, `NowCodeEditorResult`, `NowTextFieldResult` carries `[NowBuilder]`.

Consequence: **a generator cannot treat "has `[NowConsumer]`" as "this is a terminal call"**, and cannot treat
"lacks `[NowConsumer]`" as "this is not one". The single most common terminal call in the library —
`public bool Draw()` on `NowButton` (`NowControlBuilders.cs:132`) — carries no attribute at all, because a `bool`
is not a struct the analyzer would flag.

### 1.4 Where the marks are

`[NowBuilder]` structs, by file (55 shipping):

| File | Builders |
|---|---|
| `Runtime/Controls/NowControlBuilders.cs` | `NowButton:11`, `NowSelectableRow:155`, `NowCheckbox:358`, `NowRadio:495`, `NowSlider:625` |
| `Runtime/Controls/NowValueControls.cs` | `NowVectorField:10`, `NowEnumDropdown<TEnum>:181`, `NowEnumFlags<TEnum>:241`, `NowColorPicker:344`, `NowGradientField:1240`, `NowAnimationCurveField:2648` |
| `Runtime/Controls/*` (one each) | `NowBadge:10`, `NowChip:75`, `NowComboBox:19`, `NowDatePicker:18`, `NowDropdown:18`, `NowFilePicker:42`, `NowFoldout:20`, `NowInspector:37`, `NowKeyBindingField:19`, `NowMaskField:19`, `NowProgressBar:14`, `NowRichText:74`, `NowScrollView:19`, `NowSplitter:21`, `NowSplitView:22`, `NowSwitch:11`, `NowTabBar:12`, `NowTabView:144`, `NowTextArea:32`, `NowTextField:377`, `NowTimePicker:17`, `NowTreeView:57` |
| `Runtime/*` (drawing) | `NowLabel` (`NowLayout.cs:424`), `NowLottieBuilder` (`NowLayout.cs:816`), `NowLayoutContainer` (`NowLayoutContainers.cs:19`), `NowLine:25`, `NowModel` (`NowModelPreview.cs:63`), `NowRectangle:75`, `NowRipple:7`, `NowCircle` (`NowShape.cs:9`), `NowTriangle:91`, `NowPolygon:156`, `NowText:142`, `NowGlass:9`, `NowGradient:51`, `NowLottie` (`Lottie/NowLottie.cs:11`), `NowModifierBuilder<TDeformer>` (`NowEffects.cs:642`), `NowSnapshotBuilder` (`NowEffects.cs:826`) |
| Extensions | `NowCodeEditor:50`, `NowDockSpaceBuilder:1947`, `NowMarkdownBuilder:119`, `NowMarkupBuilder:6`, `NowNodeGraphCanvas:3525`, `NowSdfBuilder:2536` |

`[NowScope]` structs (22; the two `internal` ones flagged):

`NowControlScope` (`NowControlBuilders.cs:313`), `ControlIdScope` (`NowControls.cs:1140`),
`NowKeyedItemScope` (`NowControls.cs:1161`), `NowPopupFitScope` (`NowOverlay.cs:152`),
`NowOverlayHostScope` (`NowOverlay.cs:2018`, **internal**), `NowScrollScope` (`NowScrollView.cs:310`),
`NowSplitPaneScope` (`NowSplitView.cs:235`), `NowTabViewScope` (`NowTabs.cs:225`),
`NowTreeViewScope` (`NowTreeView.cs:176`), `NowInputContextScope` (`NowInput.cs:1602`, **internal**),
`NowInputScope` (`NowInput.cs:1629`), `NowUIScreenScope` (`Now.cs:4367`), `NowMaskScope` (`Now.cs:4392`),
`NowFontScope` (`Now.cs:4416`), `NowTransformScope` (`Now.cs:4440`), `NowDrawScope` (`NowDrawList.cs:473`),
`NowModifierScope<TDeformer>` (`NowEffects.cs:742`), `NowSnapshotScope` (`NowEffects.cs:860`),
`NowGUIScope` (`NowGUI.cs:640`), `NowLayoutScope` (`NowLayout.cs:373`),
`NowLabelStyleScope` (`NowLayout.cs:3274`), `ThemeScope` (`NowTheme.cs:193`).

### 1.5 **The marked set is NOT a complete description of the JS-callable API**

This is the most consequential finding in §1. The attributes are on *return types*, never on the entry points. A
reflection pass that enumerates `[NowBuilder]`/`[NowConsumer]`/`[NowScope]` finds the nouns and misses the verbs.

Concretely, none of the following carries any NowUI attribute:

**(a) Every factory.** `Now` and `NowLayout` are plain `public static partial class` declarations spread over eight
files (`NowControlFactories.cs:17,193`, `NowValueControls.cs`, `NowFilePicker.cs`, `NowFoldout.cs`,
`NowInspector.cs`, `NowKeyBindingField.cs`, `NowMaskField.cs`, `NowRichText.cs`, plus `Now` partials in `Now.cs`,
`NowEffects.cs`, `NowGlass.cs`, `NowGradient.cs`, `NowLine.cs`, `NowMaskShader.cs`, `NowModelPreview.cs`,
`NowRipple.cs`, `NowShape.cs`, `NowTextPreprocessor.cs`). 184 public static methods return a builder; 60 return a
scope. Every one is unmarked. `NowControlFactories.cs` in its entirety (358 lines, 55 factories) is attribute-free.

**(b) Whole static subsystems with no builder at all.** These are ordinary `public static class`es reached by
direct calls, and a JS app cannot work without several of them:

`NowControls` (`NowControls.cs:26`) — id scopes, `Interact`, `GetControlId`, `SiteId`.
`NowInput` (`NowInput.cs:118`) — pointer/keyboard queries, `Interact`, `NowInputScope`.
`NowFocus` (`NowFocus.cs:~317`) — focus registration/queries.
`NowControlState` (`NowControlState.cs:33`) — the persistent per-control state store.
`NowContextMenu` (`NowContextMenu.cs`) — a pure `Begin`/`Item`/`End` API with **no builder and no scope type**
(§3.6). `NowOverlay`, `NowContextAction`, `NowTooltip`, `NowTheme`, `NowScreen`, `NowClipboard`, `NowTextInput`,
`NowTextEdit`, `NowTextSelection`, `NowTextMetrics`, `NowScrollbar`, `NowViews` (dialogs), `NowGUI`/`NowGUILayout`,
`NowEffects`, `NowDeformers`, `NowTextAnimations`, `NowTextWrap`, `NowProfiler`, `NowKeyInput`, `NowPointerArbiter`,
`NowGlassSettings`, `NowMarkup`, `NowMarkdown`, `NowSdf`, `NowDock`, `NowCode`, `NowNodes`, `NowNodeIds`,
`NowMarkupBindings`.

**(c) The frame boundary.** `Now.StartUI()` and its three overloads (`Now.cs:1376,1389,1419,1432`) return
`NowUIScreenScope`, which *is* `[NowScope]` — but `StartUI` itself is unmarked, and `NowRuntime.BeginFrame`/
`EndFrame`/`ResetAll` (`Standalone/NowUI.Engine/NowRuntime.cs:151,191,208`) live in a different assembly
(`NowUI.Engine`) and carry no NowUI attribute by construction.

**(d) Result types.** All seven result structs enumerated in §1.3 are unmarked. They are what a JS caller actually
reads.

**(e) Two public disposable scopes carry no `[NowScope]`** — see §3.5.

**(f) The value-type vocabulary.** `NowRect`, `NowId`, `NowResolvedId`, `NowLayoutOptions`, `NowFocusNavigation`,
`NowInteraction`, `NowInteractionRegion`, `NowTreeNodeKey`, `NowCornerRadius`, and ~40 enums are unmarked and
must be mirrored for any of the marked API to be callable.

So: the attributes are a *useful cross-check* for a generator (they tell you which returned struct must not be
dropped on the floor, and which must be closed), but the **generation source has to be the public-API surface
itself** — which is exactly what `Tools/Standalone/ApiDump/Program.cs` already produces and what
`StandaloneCoreDesign.md:78` designates as the governance artifact. `Docs/Standalone/StandaloneCoreDesign.md:1581-1591`
(§4.8) says M3 needs "the `[NowBuilder]`/`[NowConsumer]`/`[NowScope]` attributes as the reflection source for
generating the JS surface"; taken literally that is insufficient, and a designer should say so explicitly rather than
discover it while writing the generator.

---

## 2. Identity

### 2.1 The two-phase model

`Assets/NowUI/Documentation~/Identity.md:3-8`:

> `NowId` is an authored, local key: a non-empty string, any integer (including zero), or `default` to use the caller
> site. `NowResolvedId` is the opaque runtime identity after NowUI has included the active owner, identity scopes,
> subsystem domain, and full path ancestry.

`NowId` (`Runtime/NowId.cs:20-113`) is a three-state discriminated value: `NoneKind`/`StringKind`/`IntKind`
(`:26-28`). An empty string **throws** (`:52-53` `"Control id strings cannot be empty."`); a `null` string silently
becomes `None` (`:44-50`); integer `0` is a *valid* id (`Identity.md:23-27`). Implicit conversions from `string` and
`int` exist (`:67-75`).

`NowResolvedId` (`Runtime/NowResolvedId.cs:11-115`) wraps a single `ulong` (`:13`). Its constructor is `internal`
(`:15`) and rejects `0` (`:17-18`). There is **no public integer conversion and no public raw-value constructor**
(`Identity.md:10-13`) — so a JS-side integer can never be smuggled in as a resolved id. Its only public mutators are
`Child(NowId)` / `Child(string)` / `Child(int)` (`:34-52`), which derive under `NowIdDomain.ExtensionPath`.

### 2.2 How a control id is actually derived

The single funnel is `NowControls.GetControlId`:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:498-504
internal static NowResolvedId GetControlId(NowId id, int fallbackIdentity)
{
    if (!id.hasValue)
        return GetControlId(fallbackIdentity);

    return CurrentIdentityParent().Derive(NowIdDomain.Control, id);
}
```

`CurrentIdentityParent()` (`:110-113`) is the innermost entry of a `List<NowResolvedId> _idStack` (`:28`), or, when
the stack is empty, `CurrentOwnerRoot()` (`:77-102`) — a per-`INowInputProvider` root allocated lazily through a
`ConditionalWeakTable` (`:46-47`, `:87-89`) with a process-local nonce counter (`:59-70`), plus one
`_defaultOwnerRoot` for the no-provider case (`:93-96`).

The hash is `NowIdHash` (`Runtime/NowIdHash.cs`), a SplitMix64-style avalanche (`:348-359`) over
`(parent, domain, segmentKind, payload)` (`:261-278`). Domains are an explicit enum, "part of the runtime identity
format" (`:5-23`): `OwnerRoot=1, Scope=2, Control=3, Layout=4, State=5, Overlay=6, FocusHost=7, Effect=8,
Occurrence=9, ExtensionPath=10, Legacy=11`. Derivation is ordered and non-cancelling — `a.Child(b) != b.Child(a)`
(`Identity.md:50-52`).

### 2.3 The role of `[CallerFilePath]` / `[CallerLineNumber]`

They are captured **only at the factory**, immediately interned, and never stored as strings on the builder:

```csharp
// Assets/NowUI/Runtime/Controls/NowControlFactories.cs:196-199
public static NowButton Button(string label = "", [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
{
    return new NowButton(label, NowControls.SiteToken(file, line));
}
```

`NowButton` stores `readonly int _site` (`NowControlBuilders.cs:14`) and resolves with
`NowResolvedId ResolveControlId() => _id.Resolve(_site);` (`:30`).

`SiteToken` (`NowControls.cs:425-455`) interns `(file, line)` into a dense positive `int` through two dictionaries —
a reference-keyed front cache exploiting the fact that "`[CallerFilePath]` hands every call site the same interned
literal" (`:373-376`, comparer at `:377-393`, 4096-entry cap at `:395`, `:434`, `:450`) and an ordinal fallback
(`:370-371`). The record stores the *hashed* path plus the line (`:357-368`). "Equivalent path strings share a token
even when they are not the same string instance. Tokens are process-local and must not be persisted." (`:418`).

The public form is:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:420-423
public static NowCallSiteId SiteId(string file, int line)
{
    return new NowCallSiteId(SiteToken(file, line));
}
```

`NowCallSiteId` (`Runtime/Controls/NowControlIdentity.cs:8-49`) is a deliberately distinct opaque type — token `0`
is reserved (`:14-17`), the accessor is `internal` (`:25-35`), there is no public integer conversion.
`Identity.md:66-71`: "store it only as the fallback beside a `NowControlIdentity` … Do not persist it or derive
children from it."

**Occurrence salting.** When identity falls back to the call site, and only then, the id is salted by draw-order
occurrence:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:526-529
internal static NowResolvedId GetControlId(int identity)
{
    return Salt(ResolveCallSiteStable(identity, NowIdDomain.Control));
}

// :563-580
static NowResolvedId Salt(NowResolvedId id)
{
    var occurrences = NowInput.isPassive ? _passiveOccurrences : _labelOccurrences;

    if (occurrences.TryGetValue(id, out int occurrence))
    {
        occurrences[id] = occurrence + 1;
        return NowIdHash.DeriveOccurrence(id, occurrence);
    }

    occurrences[id] = 1;
    return id;
}
```

The **first** occurrence keeps the unsalted id (`NowIdHash.DeriveOccurrence` throws for `occurrence <= 0`,
`NowIdHash.cs:213-216`). Occurrence counters are global-per-frame per surface, reset by
`ResetControlIdOccurrences()` (`:583-589`) when an input surface begins.

`Identity.md:62-64`: "Loops over one call site receive occurrence salting, so this fallback follows draw position.
It is appropriate for fixed one-off controls, not data that can reorder."

### 2.4 **Yes — explicit identity is already fully supported, and this is the load-bearing fact for the whole milestone**

There are four independent, documented, first-class ways to supply identity without any caller-info participation.

**(1) `SetId` on the builder.** Present on 39 builders, both overloads, verbatim shape:

```csharp
// Assets/NowUI/Runtime/Controls/NowControlBuilders.cs:64-68
/// <summary>Explicit control id, decoupling identity from the rendered label.</summary>
public NowButton SetId(NowId id) { _id = id; return this; }

/// <summary>Uses an identity that has already been fully resolved.</summary>
public NowButton SetId(NowResolvedId id) { _id = id; return this; }
```

Both write into a `NowControlIdentity _id` field, a carrier that preserves authored-vs-resolved intent until the
consumer resolves once:

```csharp
// Assets/NowUI/Runtime/Controls/NowControlIdentity.cs:92-104
public NowResolvedId Resolve(NowCallSiteId fallbackIdentity)
{
    return _isResolved ? _resolved : NowControls.GetControlId(_authored, fallbackIdentity);
}

internal NowResolvedId Resolve(int fallbackIdentity)
{
    return _isResolved ? _resolved : NowControls.GetControlId(_authored, fallbackIdentity);
}
```

Its `default` value means "use the captured call-site fallback" (`Identity.md:106-109`). Implicit conversions from
both `NowId` and `NowResolvedId` exist (`:106-114`).

**(2) `NowId` on the factory.** Nine factories take an id directly instead of relying on the site — always with
*both* a `NowId` and a `NowResolvedId` overload:

```csharp
// Assets/NowUI/Runtime/Controls/NowControlFactories.cs:232-240
public static NowTextField TextField(NowId id = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
{
    return new NowTextField(id, NowControls.SiteToken(file, line));
}

public static NowTextField TextField(NowResolvedId id, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)
{
    return new NowTextField(id, NowControls.SiteToken(file, line));
}
```

Same pairing for `TextArea` (`:243-251`), `Dropdown` (`:254-268`), `ScrollView` (`:271-279`), `Splitter`
(`:288-296`), and the `Now.*` rect forms at `:56-120`; plus `EnumDropdown<TEnum>`/`EnumFlags<TEnum>`
(`NowValueControls.cs:4183,4189,4298,4304`).

**(3) Id scopes.** Five public forms, `NowControls.cs:173-271`:

```csharp
public static ControlIdScope ControlScope(NowId id = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)   // :173
public static ControlIdScope ControlScope(NowResolvedId id)                                                                          // :184
public static ControlIdScope IdScope(string name)                                                                                    // :197
public static ControlIdScope IdScope(NowId id)                                                                                       // :211
public static ControlIdScope IdScope(NowResolvedId id)                                                                               // :221
public static ControlIdScope IdScope(int id)                                                                                         // :230
public static NowKeyedItemScope KeyedItem(NowId key, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)              // :241
public static NowKeyedItemScope KeyedItemIn(NowId listId, NowId key)                                                                 // :260
```

Semantics that matter:
* `IdScope(string)` rejects null/empty (`:199-200`); `IdScope(NowId)` with a default id is a **no-op that returns
  `default`** and pushes nothing (`:213-214`) — a silent identity-flattening trap.
* `IdScope(NowResolvedId)` / `ControlScope(NowResolvedId)` route to `RestoreIdScope` (`:131-138`), which **replaces**
  rather than nests: "this does not combine it with the current scope: the captured value already contains its
  complete host and nested-scope ancestry" (`:126-130`).
* `KeyedItem(key)` uses its *call site* as the list namespace (`:249-252`) — so it is **not** usable from a bridge
  without a synthetic site. `KeyedItemIn(listId, key)` is the call-site-free equivalent (`:268-270`) and both reject
  an empty key (`:246-247`, `:262-266`).
* `Identity.md:101-102`: "Use `IdScope` for a general reusable panel; prefer `KeyedItem`/`KeyedItemIn` when the scope
  represents collection data."

**(4) Direct resolution.**

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:464-470
public static NowResolvedId GetControlId(string id)                  // throws on null/empty

// :476-482
public static NowResolvedId GetControlId(NowId id = default, [CallerFilePath] string file = "", [CallerLineNumber] int line = 0)

// :491-496
public static NowResolvedId GetControlId(NowId id, NowCallSiteId fallbackIdentity)
```

plus `NowResolvedId.Child(...)` for private sub-controls (`Identity.md:138`), and `NowControls.SiteId(file, line)`
which lets a caller *mint its own call-site token from arbitrary strings and integers* (`:420-423`) — nothing
validates that `file` is a real path.

### 2.5 Collision behaviour of explicit ids — stated bluntly by the source

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:457-462 (doc comment on GetControlId(string))
/// Explicit ids are stable — never occurrence-salted — so the same name resolves to the same
/// control from anywhere under the same scope; two controls sharing one explicit id in one frame
/// share state, which is the caller's bug, not something to silently disambiguate.
```

The only guard is an editor/dev-build warning:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:606-625
public static bool warnOnDuplicateControlIds = true;

[System.Diagnostics.Conditional("UNITY_EDITOR"), System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
static void CheckDuplicateControlId(NowResolvedId id)
{
    if (!warnOnDuplicateControlIds || NowInput.isPassive)
        return;

    if (_interactedIds.Add(id) || !_duplicateWarnedIds.Add(id))
        return;

    Debug.LogWarning($"NowUI: two controls resolved to the same id ({id}) in one pass — they share focus, state and interaction. …");
}
```

It fires from `NowControls.Interact` (`:895`), once per id, only in `UNITY_EDITOR || DEVELOPMENT_BUILD`
(`:591-595`, `:608`). **In a release wasm build the check is compiled out entirely.** Per
`StandaloneCoreDesign.md:65` (H.11), the standalone Debug configuration defines `DEVELOPMENT_BUILD`, so the warning
exists in Debug and vanishes in Release.

Also note: the check is a `HashSet<NowResolvedId>` of ids that reached `Interact`. Non-interactive draws never
register, so two `Label`s sharing an id are never reported.

Collision *between* independent hosts cannot happen by id text alone: every input provider and retained host owns a
distinct root (`Identity.md:42-47`), and every subsystem uses its own domain, so "a control path, layout cache, state
slot, effect, focus host, and overlay cannot alias by producing the same authored integer" (`Identity.md:48-52`).

### 2.6 Which builders have no identity at all

16 of the 55 builders expose no `SetId`, because they resolve nothing: `NowBadge`, `NowCircle`, `NowGlass`,
`NowGradient`, `NowLabel`, `NowLine`, `NowLottie`, `NowLottieBuilder`, `NowMarkupBuilder`, `NowModel`, `NowPolygon`,
`NowRectangle`, `NowRipple`, `NowSdfBuilder`, `NowText`, `NowTriangle`. Verified by grep: `NowRipple.cs`,
`Lottie/NowLottie.cs` and `NowGlass.cs` contain **zero** references to `SiteToken`, `CallerFilePath`,
`GetControlId`, `ResolveCallSite` or `NowControlState.Get` — they are stateless draws.

(`NowMarkupBuilder` is the exception in kind: it has no `SetId` because identity is carried by the markup document's
`id=` attributes — §6.)

### 2.7 Two additional identity facts a bridge designer will need

**Layout groups already have a call-site-free structural identity mode.** `NowLayout.BeginGroup` resolves in four
tiers:

```csharp
// Assets/NowUI/Runtime/NowLayout.cs:2481-2498
NowResolvedId groupId;

if (identity.isResolved)                  groupId = identity.resolved;
else if (identity.authored.hasValue)      groupId = parent.id.Derive(NowIdDomain.Layout, identity.authored);
else if (site != 0)                       groupId = ResolveGroupSiteOccurrence(_depth - 1, parent.id, site);
else                                      groupId = NowIdHash.DeriveOccurrence(parent.id, parent.childIndex + 1);

parent.childIndex++;
```

The last branch is **positional identity under the parent group** — precedent that the codebase already accepts a
structural fallback where a call site is unavailable. It is reachable only through the private
`BeginGroup(bool, NowId, in NowLayoutOptions)` (`:2450-2453`) today, but the mechanism exists and is tested by
everything that renders through it.

Note also that layout occurrence counters are **per parent depth** (`_groupSiteOccurrences[parentDepth]`,
`:3171-3174`, `:3180-3206`), i.e. nested and reset per group — unlike control occurrence counters, which are flat
per frame (`NowControls.cs:307-311`). Two different disambiguation policies coexist.

**Measure passes replay identity deterministically.** When a host runs an exact two-pass layout
(`NowLayout.RunMeasured`, `NowLayout.cs:1481-1503`), the same UI executes twice per frame. Occurrence counting
switches to a second table seeded from the real pass's offsets (`NowControls.cs:563-580`, `:628-671`) so
"occurrence N during measurement resolves to occurrence N again when the real replay rewinds to that same base"
(`:565-568`). Any recorded command buffer a JS bridge replays must therefore be **replayable more than once per
frame, identically**, with no side effects of its own.

---

## 3. Scopes

### 3.1 The mechanism: a token, a guard, an ambient stack

Every scope is a value type holding an `int _token` (or an equivalent) issued by a shared
`NowScopeGuard` (`Runtime/NowScopeGuard.cs:11-142`). `Enter()` pushes a monotonically increasing non-zero token
(`:30-42`); `Exit(token)` pops only if that token is the top (`:105-112` via `IsCurrent` `:44-65`).

Out-of-order disposal is a **thrown exception**, not a silent fixup:

```csharp
// Assets/NowUI/Runtime/NowScopeGuard.cs:54-61
for (int i = last - 1; i >= 0; --i)
{
    if (_tokens[i] == token)
    {
        throw new InvalidOperationException(
            $"{_name} scopes must be disposed in reverse order. Dispose the inner scope first.");
    }
}
```

A disposed copy is harmless: the token no longer matches, `IsCurrent` returns `false`, and the exit is a no-op
(`:63-64`, and the struct fields self-zero — `Now.cs:4376-4383`, `NowLayout.cs:403-410`).

The canonical scope shape is four lines:

```csharp
// Assets/NowUI/Runtime/Now.cs:4401-4408 (NowMaskScope)
public void Dispose()
{
    if (_token == 0)
        return;

    Now.PopMask(_token);
    _token = 0;
}
```

with the matching pop guarded by the token *and* by the ambient stack being non-empty:

```csharp
// Assets/NowUI/Runtime/Now.cs:288-291
internal static void PopMask(int token)
{
    if (_maskScopes.Exit(token) && _maskStack.Count > 0)
        _maskStack.RemoveAt(_maskStack.Count - 1);
}
```

Guards in use: `Now.Font` (`Now.cs:70`), `Now.Mask` (`:181`), `Now.Transform` (`:430`), `Now.StartUI` (`:1258`),
`NowControls.IdScope` (`NowControls.cs:30`), `NowLayout` (`NowLayout.cs:1203`), `NowScrollView.Begin`
(`NowScrollView.cs:312`), plus guards in `NowTheme`, `NowGUI`, `NowInput`, `NowFrame`, `NowDrawList`.

### 3.2 The specific scopes named in the brief

**`NowUIScreenScope`** (`Now.cs:4367-4384`) — returned by all four `Now.StartUI` overloads
(`:1376,1389,1419,1432`). Dispose calls `Now.FinishUIScreenFrame(token)` (`:1966`), which "finalizes overlays,
screen rendering, and input capture even when the frame exits early" (`:4362-4364`). Finishing while a nested scope
is open throws (`:1976-1978`).

**`NowLayoutScope`** (`NowLayout.cs:373-411`) — returned by `Area`, `HorizontalScope`, `VerticalScope`
(overloads at `:1271-1374`, `:1837-1909`, `:1966-2038`) and by `NowLayoutContainer.Begin()`
(`NowLayoutContainers.cs:200`). It carries a public `readonly NowRect rect` plus `x/y/width/height` (`:382`,
`:395-401`) — **the reserved rect is readable from the scope handle**. Dispose dispatches on a `Kind` enum
(`:375-380`, `:403-410`) into `EndScope` (`:3208-3227`), which is a no-op if the token is not current (`:3210-3211`).

**`NowMaskScope`** (`Now.cs:4392-4409`) — from `Now.Mask(NowRect)` (`:207`), `Now.Mask(NowMaskShape)` (`:247`),
`Now.Mask(NowMaskTexture)` (`:275`), and `NowSdfBuilder.BeginMask()` (`NowSdf.cs:3128,3148`).

**`NowTransformScope`** (`Now.cs:4440-4457`) — from `Now.Transform(...)` (`:459`, `:499`).

Also relevant: `NowFontScope` (`Now.cs:4416-4433`), `ThemeScope` (`NowTheme.cs:193`, via `NowControls.Theme`
`NowControls.cs:155-158`), `NowControlScope` (`NowControlBuilders.cs:313-351`, returned by `Button().Begin()` etc.),
`NowScrollScope`, `NowTabViewScope`, `NowSplitPaneScope`, `NowTreeViewScope`.

### 3.3 What must happen on dispose

Composite scopes dispose their children in reverse construction order and are idempotent via a `bool _disposed`
flag rather than a token:

```csharp
// Assets/NowUI/Runtime/Controls/NowControlBuilders.cs:341-350
public void Dispose()
{
    if (_disposed)
        return;

    _disposed = true;
    _row.Dispose();
    _area.Dispose();
    _mask.Dispose();
}
```

`NowControlScope` bundles a mask + layout area + horizontal row (constructed at `NowControlBuilders.cs:125-129`).
`NowTabViewScope` bundles area + mask (`NowTabs.cs:249-256`). `NowScrollScope` bundles a `NowLayoutScope`,
`NowMaskScope` and `NowFocusScrollRegionScope` (`NowScrollView.cs:341-343`).

### 3.4 What happens if a scope is not disposed, and what the diagnostic does

**Nothing self-heals within the frame.** A leaked scope leaves its ambient state pushed for the rest of the frame —
that is exactly what NOWUI002's description says: "Discarding one as a statement leaks the pushed state for the rest
of the frame." (`NowBuilderDiscardAnalyzer.cs:35`). The recovery is at the **next** `Now.StartUI`:

```csharp
// Assets/NowUI/Runtime/Now.cs:1303-1316
if (_screenFrameActive)
{
    _screenFrameScopes.Clear();
    _screenFrameActive = false;
    NowOverlay.DiscardAbandonedFrame();
    DiscardActiveMeshCaptures();
    NowDrawList.DiscardAbandonedScopes();
    if (!_warnedLeakedScreenScope)
    {
        _warnedLeakedScreenScope = true;
        Debug.LogError("NowUI: a NowUIScreenScope from a previous frame was never disposed; the abandoned frame was discarded. Wrap Now.StartUI in a using statement.");
    }
}
```

`BeginScreenFrame` (`Now.cs:1271-1357`) is the whole self-heal. In order it:

1. **Throws** if `StartUI` is nested within the same frame (`:1273-1277`).
2. **Throws** if any retained-host / `NowGUI` / `NowInput` / `NowLayout` / theme / id scope is active *this frame*
   (`:1279-1287`) — the `hasActiveScopesThisFrame` predicates each compare a stored `Time.frameCount` against the
   current one (e.g. `NowControls.cs:148-149`, `NowLayout.cs:1216-1217`), which is precisely how a *leaked* scope
   (started last frame) is distinguished from a *live nested* one.
3. **Throws** if a `NowDrawList` scope is active (`:1289-1293`).
4. Discards abandoned guards for frame/GUI/input/layout (`:1298-1301`).
5. Logs the screen-scope leak error once and discards the frame (`:1303-1316`).
6. Resets a leaked draw-suppression depth with a second one-shot error (`:1325-1334`).
7. Resets theme and id-scope stacks (`:1340-1341`) and hard-clears font, mask, transform stacks and guards
   (`:1346-1354`).

The id-scope leak has its own message:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:286-305
internal static void ResetIdScopesForFrame()
{
    if (_idStack.Count == 0) { _warnedLeakedIdScope = false; return; }

    _idStack.Clear();
    _idScopes.Clear();
    _idScopeStartedAt = int.MinValue;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    if (!_warnedLeakedIdScope)
    {
        _warnedLeakedIdScope = true;
        Debug.LogWarning("NowUI: a NowControls.IdScope from the previous frame was never disposed; the id scope stack was reset. Wrap the scope in a using statement.");
    }
#endif
}
```

Layout has a third, independent path — `OnFrameBoundary` logs `"NowLayout: unbalanced Begin/End calls detected from
a previous frame. Stack cleared."` and zeroes `_depth` (`NowLayout.cs:3229-3245`).

**Three properties of these diagnostics that constrain a bridge:**

* The `NowLayout` message is an unconditional `Debug.LogError` (`:3241`), and the screen/suppress messages are
  unconditional `Debug.LogError` (`Now.cs:1314`, `:1332`). The id-scope message is `#if UNITY_EDITOR ||
  DEVELOPMENT_BUILD` (`NowControls.cs:298-304`) and the duplicate-id warning likewise (`NowControls.cs:591-625`).
  So leak reporting is partly gated by build configuration and partly not.
* Each is **warn-once**, latched by a `bool` that only clears on a clean frame (`Now.cs:1319`, `:1337`,
  `NowControls.cs:290`). A bridge that leaks every frame gets exactly one message, ever.
* **Cases 1-3 are `throw`, not recover.** A JS bridge that leaves a layout, theme, input or id scope open at the end
  of a frame does not get a warning next frame — it gets an `InvalidOperationException` out of `Now.StartUI`,
  killing the frame.

### 3.5 Two public disposable scopes carry no `[NowScope]`

Of 29 `IDisposable` structs in Runtime+Extensions, 7 lack the mark. Five are `internal` or private
(`NowFocusHostRegistrationScope` `NowFocus.cs:266`, `NowFocusScrollRegionScope` `NowFocus.cs:288`,
`NowVectorFieldScope` `NowValueControls.cs:4666`, `NowFrameScope` `NowFrame.cs:202`, `NowGlassQualityScope`
`NowGlassSettings.cs:329`). **Two are public:**

* `Now.TextContextScope` (`Runtime/NowTextPreprocessor.cs:70-77`), returned by `Now.TextContext(string)` (`:64-68`).
  It pushes onto a static `_textContexts` list and pops on dispose — a textbook ambient scope. Leaking it
  permanently shifts text preprocessing, and **nothing in `BeginScreenFrame` resets `_textContexts`**.
* `NowNodeGraphEvaluator<T>.BatchScope` (`Extensions/NodeGraph/NowNodeGraphEvaluator.cs:192-205`), returned by
  `BeginBatch()`; disposing calls `EndBatch()` (`:180-189`). It is *also generic on the evaluator's type parameter*.

A generator driven by `[NowScope]` will miss both.

### 3.6 What a bridge must guarantee — stated as facts, not advice

JavaScript has no `using`. The facts that determine what a bridge must do:

1. **Disposal order is enforced by exception**, not by best-effort unwinding (`NowScopeGuard.cs:54-61`, `:92-99`).
   Any scope model exposed to JS must produce a strictly LIFO close order on the wasm side, regardless of what JS
   did.
2. **Leaving a scope open across `StartUI` throws** for layout/theme/id/input/GUI/drawlist scopes
   (`Now.cs:1279-1293`), and discards the frame for `NowUIScreenScope` (`:1303-1316`). There is no "leak and carry
   on" mode for the throwing set.
3. **Some scopes are not merely ambient state — they are also results.** `NowControlScope` carries `interaction`,
   `rect`, `clicked`, `focused` as public readonly fields (`NowControlBuilders.cs:315-322`); `NowTabViewScope`
   carries `selected`, `changed`, `pageRect` (`NowTabs.cs:226-233`); `NowLayoutScope` carries `rect`
   (`NowLayout.cs:382`); `NowSplitViewResult` carries `firstRect`, `secondRect`, `dragging`
   (`NowSplitView.cs:178-197`). "Interaction runs immediately, so the result is readable inside the scope"
   (`NowControlBuilders.cs:84-85`). A scope open/close pair in a command buffer therefore has *a value flowing out of
   the open call*, in the middle of the frame.
4. **`Now.StartUI` cannot be nested, ever** (`Now.cs:1273-1277`) — one UI frame per `BeginFrame`.
5. **There is a precedent for an imperative Begin/End API in NowUI itself.** `NowContextMenu` is entirely
   `Begin(NowResolvedId)` / `Item(...)` / `BeginSubmenu(...)` / `EndSubmenu()` / `End()` with **no `IDisposable`
   type at all** (`NowContextMenu.cs:304`, `:390`, `:504`, `:560`, `:614`). It is the one place in the library whose
   shape a JS caller could mirror one-to-one.
6. **Scope structs are value types with mutable token fields.** Copying one and disposing the copy is defined
   behaviour (a no-op), which means a bridge can hold a scope in a wasm-side table without special care
   (`NowScopeGuard.cs:6-9`).

---

## 4. Results a caller reads

### 4.1 The four return conventions

`NowControls.cs:18-21` states the convention: "action controls return `bool` from `Draw()` — true on click or
submit-while-focused; value controls take `Draw(ref value)`, mutate the caller-owned ref and return true when it
changed this frame."

| Convention | Shape | Examples |
|---|---|---|
| Action | `bool Draw()` | `NowButton:132`, `NowSelectableRow:238`, `NowRadio:596`, `NowFoldout:69`, `NowChip:143` |
| Action + extra out | `bool Draw(out bool removed)` | `NowChip` (`NowBadge.cs:153`) |
| Value | `bool Draw(ref T value)` | `NowCheckbox(ref bool):462`, `NowSlider(ref float):680`, `NowSlider(ref int):722`, `NowSwitch(ref bool):66`, `NowDropdown(ref int):88`, `NowTabBar(ref int):63`, `NowComboBox(ref int):134` / `(ref string):232`, `NowSplitter(ref float, float, float):73`, `NowDatePicker(ref DateTime):175`, `NowTimePicker(ref DateTime):121` / `(ref TimeSpan):132`, `NowKeyBindingField(ref Key):77`, `NowMaskField(ref int):107` / `(ref LayerMask):116`, `NowFilePicker(ref string):304`, `NowTextArea(ref string):131`, `NowEnumDropdown(ref TEnum):223`, `NowEnumFlags(ref TEnum):288`, `NowVectorField(ref Vector2/3/4/Vector2Int/Vector3Int/Rect/RectInt):100-165`, `NowColorPicker(ref Color):501` / `(ref Vector4):490`, `NowGradientField(ref Gradient):1410`, `NowAnimationCurveField(ref AnimationCurve):2839`, `NowInspector.Draw<T>(ref T):98` |
| Result struct | `TResult Draw(...)` | `NowTextFieldResult` ×6 (`NowTextField.cs:638-685`), `NowRichTextResult` (`NowRichText.cs:294`), `NowSplitViewResult Begin(ref float ratio)` (`NowSplitView.cs:81`), `NowCodeEditorResult Draw(ref string)` (`NowCodeEditor.cs:350`), `NowMarkupResult`, `NowMarkdownResult`, `NowNodeGraphResult` |
| Scope-as-result | `TScope Begin()` | `NowControlScope` (`NowControlBuilders.cs:99`), `NowTabViewScope` (`NowTabs.cs:~218`), `NowScrollScope`, `NowTreeViewScope` |
| Pure draw | `void Draw()` | `NowBadge.cs:55`, `NowProgressBar.cs:71`, `NowDocking.cs:2027` |

### 4.2 The full readable set

**`NowInteraction`** (`Runtime/Input/NowInput.cs:13-116`) is the richest per-control result. Public readonly fields:
`id` (`NowResolvedId`), `rect` (`UnityEngine.Rect`), `button` (`NowPointerButton`), `hasPointer`, `pointerPosition`,
`pointerDelta`, `dragDelta` (`Vector2`), and the booleans `hovered`, `pressed`, `held`, `released`, `clicked`,
`active`, `dragging`, `dragStarted`, `dragEnded`, `cancelled`, `dragCancelled` (`:15-51`). It also exposes
`GetId(string|int)` → `NowResolvedId` (`:94-103`) and `ref T State<T>(string|int)` (`:106-115`).

**`out bool focused` and `out bool submitted`** accompany every `NowControls.Interact` overload
(`NowControls.cs:701-893`). `submitted` = "Enter/Return or the touch keyboard's Done action submitted the focused
field this frame … remains false during passive layout measurement" (`NowTextField.cs:333-337`).

**Rects.** Exposed by `NowControlScope.rect`, `NowLayoutScope.rect`, `NowTextFieldResult.rect`,
`NowRichTextResult.rect`, `NowTabViewScope.pageRect`, `NowSplitViewResult.firstRect/secondRect`,
`NowInteraction.rect`. **Not** exposed by any plain `bool Draw()`: `NowButton.Draw()` computes the rect at
`NowControlBuilders.cs:139` and discards it. A JS caller that needs the geometry of a simple button must call
`Begin()` instead of `Draw()`.

**Text field contents.** NowUI stores **no** caller text. `Draw(ref string text)` mutates the caller's string
(`NowTextField.cs:685`, `NowTextArea.cs:131`). Editing state (caret, selection, undo) lives in
`NowControlState` keyed by resolved id (`NowTextEditState` appears 16 times in the public API dump). This means
"what is in the text field" is *whatever the caller last passed in* — there is no getter.

### 4.3 What is available in the same frame

Same frame, unconditionally, because `Interact` runs against the current input snapshot before the control draws:

```csharp
// Assets/NowUI/Runtime/Controls/NowControls.cs:891-900
internal static NowInteraction Interact(NowResolvedId id, in NowInteractionRegion region, …)
{
    CheckDuplicateControlId(id);
    var interaction = NowInput.Interact(id, in region);
    NowFocus.Register(id, region.bounds, navigation, navigationLock, consumesCancel);

    if (interaction.pressed)
        NowFocus.Focus(id);
```

and every control calls it before drawing (`NowButton.Draw` `:140`, then `renderer.DrawButton` `:143`). So:
`hovered`, `pressed`, `held`, `released`, `clicked`, `dragging`, `dragStarted`, `dragEnded`, `focused`,
`submitted`, the `changed` bool of every value control, and the resolved rect are **all same-frame**.

### 4.4 What is inherently one frame late — in C#, before any bridge latency is added

| Value | Latency and citation |
|---|---|
| Focus navigation targets | "navigation resolves spatially against the previous frame's registry (immediate mode has no widget tree)" — `NowFocus.cs:307-310`. Explicit `Focus`/`Register` changes are "effective on the next frame swap" — `NowFocus.cs:1079`, `:1089`, `:1151` |
| Cross-surface pointer arbitration | "The winner is resolved from the previous frame's claims (one frame late …)" — `NowPointerArbiter.cs:11-17`; "Per-key union of last frame's content rects" `:55`; `:113`, `:136` |
| Overlay / popup pointer blocking | "…frame's claim, one frame late like overlay pointer blocks" — `NowInput.cs:681`, `:818` |
| Context-menu occlusion test | "last frame's rect for the occlusion test before this menu places itself, one frame late like overlay pointer blocks" — `NowContextMenu.cs:160-161` |
| **Context-menu item clicks** | "Adds an item; true when it was clicked (**the frame after the click**)" — `NowContextMenu.cs:346`. And delivery is owner-scoped: "A clicked item is delivered only during that owner's next declaration pass on the same provider; unrelated owners cannot receive or discard it, and an omitted item expires." — `Identity.md:216-222` |
| **Dropdown / ComboBox selection** | "Selection from the popup applies on the next frame's Draw (deferred draws run after Draw returns)." — `NowDropdown.cs:14-15`; identically `NowComboBox.cs:15`, `:528` |
| Auto sizes, stretch shares, flexible space in a one-pass host | "Deferred sizes (auto group extents, stretch shares, flexible space) resolve from the previous frame in a one-pass host." — `NowLayout.cs:1263-1266`. Mechanically: `BeginGroup` reads `autoSize` from `_cache[groupId]` (`:2503-2507`) |
| `Button().Begin()` content-derived size | Reads `NowLayout.TryGetCachedAreaContentSize(areaKey, out cached)` before measuring — `NowControlBuilders.cs:112-113`. Doc: "An exact layout host sizes the button from content measured in the same rebuild; a one-pass host uses the previous measurement." `:86-88` |
| Markdown live-embed height | "Reserves space for a live embed at last frame's measured height (one …" — `NowMarkdownDocument.cs:980` |
| `NowContentRect.End(measuredHeight)` convergence | Writes into `NowControlState` and requests a repaint; converges over frames — `NowLayout.cs:353-362` |

The escape hatch for the layout-latency rows is `NowLayout.RunMeasured` (`NowLayout.cs:1481-1503`), which runs the UI
**twice** — "first a measure pass with draws suppressed and input passive, then the real pass using this frame's
measurements — so flexible space, stretching and auto-sized groups are exact every frame, including the first and
while animating." Its callback is an `Action` (`:1495`), i.e. a delegate (§5.3).

### 4.5 What a previous-frame result table can and cannot serve

Directly implied by the above:

* **Can serve:** everything in §4.3 — the entire interaction bundle, `changed`, `focused`, `submitted`, and every
  rect. These are computed synchronously inside the replay, so a table populated during frame *N*'s replay is
  readable by JS during frame *N+1*'s recording. Total observed latency = 1 frame.
* **Compounds to 2 frames:** context-menu item clicks and dropdown/combobox selection, which are already one frame
  late in C#. A JS `menu.item("Rename")` reads true two frames after the physical click.
* **Cannot be served at all by a per-control table keyed on identity:** `NowRichTextResult.layout` is a
  `NowRichTextLayout` **reference type** (`NowRichText.cs:30`), and `NowRichTextResult.pointerHit` /
  `TryHit(Vector2, out NowRichTextHit)` (`:39-48`) require calling *into* that live object.
  `NowMarkupResult` borrows the document's event buffer and **throws** when read after the document redraws
  (`NowMarkupState.cs:61-69`). `NowSplitViewResult.BeginFirst()` (`NowSplitView.cs:200`) is a method on the result,
  not data.
* **Is not a result at all:** the text a text field holds. There is nothing to put in the table; the caller owns it.
  A JS mirror must own the string on the JS side and pass it in every frame, or own it on the wasm side and expose a
  getter that is not part of the builder API.
* **A control that is not drawn produces no entry.** State is evicted after 10 s untouched
  (`NowControlState.cs:35` `EVICT_AFTER_SECONDS = 10f`, sweep every 1 s at `:37`), and occurrence tables are cleared
  per surface (`NowControls.cs:583-589`). A result table has to have the same lifetime discipline or it will serve
  stale `clicked` values for controls that stopped being drawn.

---

## 5. Values that cross the boundary

### 5.1 Aggregate parameter-type histogram

Over all 2113 public methods in `NowUI.Runtime` (from `artifacts/local/api/after-NowUI.Runtime.api.txt`), by
occurrence:

```
641 System.Single          281 NowUI.NowRect          91 NowUI.NowThemeAsset     25 UnityEngine.Rect
397 System.Int32           223 System.Boolean         81 UnityEngine.Color       24 System.ReadOnlySpan<char>
389 System.String          153 NowUI.NowId            55 NowUI.NowLayoutOptions  23 System.Object
                           141 NowUI.NowResolvedId    40 NowUI.NowLayoutAlign    21 UnityEngine.Vector4[]
135 UnityEngine.Vector2    113 UnityEngine.Vector4    35 NowUI.NowFocusNavigation 21 System.Action
                                                      29 NowUI.NowFontStyle      21 NowUI.NowFontAsset
                                                      28 NowUI.NowTextStyle      20 NowUI.NowPointerButton
```

Then a long tail: `NowInputSurface` (14), `NowTextEditState` (16), `IReadOnlyList<string>` (15),
`UnityEngine.Material` (18), `UnityEngine.Camera` (16), `CommandBuffer` (13), `UnityEngine.Gradient` (10),
`NowCallSiteId` (10), `NowCornerRadius` (9), `NowInteractionRegion` (7), `NowTreeNodeKey` (7), `NowDrawList` (7),
`System.DateTime` (6), `System.Byte[]` (6), `T` (8), `TState` (6), `System.Action<TState>` (6).

Parameter modifiers: **118 `in`, 103 `out`, 89 `ref`** across the public surface.

### 5.2 What crosses cleanly

Blittable and trivially encodable: `float`, `int`, `bool`, `NowRect`, `Vector2/3/4`, `Rect`, `Color`, `Color32`,
`NowCornerRadius`, and ~40 enums (`NowLayoutAlign`, `NowTextStyle`, `NowRectangleStyle`, `NowFontStyle`,
`NowColorToken`, `NowRadiusToken`, `NowPointerButton`, `NowSplitAxis`, `NowMarkupEventKind`, …). Strings are the
third-most-common parameter (389) and encode fine, at a cost.

`NowId` is *almost* free: three states, string or int (`NowId.cs:26-28`). `NowResolvedId` is a single `ulong`
(`NowResolvedId.cs:13`) but has **no public constructor from a raw value** (`:15`), so a JS-side handle to one must
be an opaque index into a wasm-side table, never the value itself.

`NowLayoutOptions` is a flags-plus-floats struct (55 uses); `NowFocusNavigation` (35 uses) likewise.

### 5.3 What cannot cross a binary command buffer

**(a) Delegates — 21 `System.Action` parameters plus typed forms.** Full inventory of public delegate-taking
members:

| Member | Signature |
|---|---|
| `NowOverlay.Defer` / `DeferScreen` | `(NowRect blockRect, Action draw)` — `NowOverlay.cs:805`, `:858`; plus `DrawCallback` forms at `:910`, `:929`, `:968`, `:987`, `:1027`, `:1044` |
| `NowLayout.RunMeasured` | `(NowRect, Action ui, …)` — `NowLayout.cs:1493`, `:1505`, `:1517`, `:1527`; plus `RunMeasured<TState>` with `Action<TState>` |
| `NowDock.Window` | `(string title, Action draw, …)` and `(string title, Action<NowRect> draw, …)` — `NowDocking.cs:187`, `:198` |
| `NowNodeGraphCanvas.SetNodeContent` | `(Action<NowNode, NowRect> draw)` — `NowNodeGraph.cs:3889` |
| `NowNodeGraphCanvas.drawCustomItems` | `Func<NowNodeGraph, NowNodeGraphHistory, bool>` — `NowNodeGraph.cs:3248`, `:3249` |
| `NowViews.MessageBox` / `Confirm` | `(…, Action onClosed = null)` and `<TOwner>(…, Action<TOwner>)` — `NowDialogs.cs:286`, `:291`, `:309` |
| `NowInspector.SetDrawer<T>` | `(NowInspectorDrawer<T> drawer)` where `delegate bool NowInspectorDrawer<T>(ref T value)` — `NowInspector.cs:17`, `:182` |
| `NowRenderer.Warmup` / `NowDrawList.Warmup` | `(…, Action draw)` — `NowRenderer.cs:37,43,58,68,78`; `NowDrawList.cs:73,96,105` |
| `NowMarkupStyles.ParseDeclarations` | `(string, Action<string,string> add)` — `NowMarkupStyles.cs:108` |
| Settable static hooks | `NowClipboard.setText`/`getText` (`NowClipboard.cs:14,16`), `NowTextInput.setImeEnabled`/`setCompositionCursor` (`NowTextInput.cs:379,387`), `NowLottieAsset.remoteUrlPolicy` (`:58`), `NowMarkdownImages.remoteUrlPolicy` (`:87`), `Now.SetTextPreprocessor` (`NowTextPreprocessor.cs:87`) |
| Events | `NowGraphic.rebuildNowUI`, `NowPipelineGraphic.rebuildNowUI`, `NowVisualElement.rebuildNowUI`, `NowWorldGraphic.rebuildNowUI`, `NowBootstrap.onPreUpdate/onUpdate/onPostUpdate` |

**How common:** `Action` appears in 21 public parameter positions out of ~2113 methods — roughly 1%. It is
concentrated in exactly four capabilities: **deferred overlays** (popups, tooltips, context surfaces),
**exact two-pass layout**, **docking windows**, and **node-graph custom content**. Everything in the core control
library — every builder in §1.4, every factory in `NowControlFactories.cs` — is delegate-free.

**(b) Generic methods.** No `public` generic method appears in the fluent builder chain; they cluster in state and
inspection:

`NowControlState.Get<T>(NowResolvedId)` / `Get<T>(NowResolvedId, string)` / `Warmup<T>(…)` — all
`where T : struct`, 8 overloads (`NowControlState.cs:77-252`). `Get<T>` returns `ref T` — an interior reference,
which cannot cross a boundary at all.
`NowInteraction.State<T>(string|int)` → `ref T` (`NowInput.cs:106-115`).
`NowInspector.Draw<T>(ref T value)` (`NowInspector.cs:98`), `SetDrawer<T>` / `RemoveDrawer<T>` (`:182`, `:187`).
`NowViews.MessageBox<TOwner>` / `Confirm<TOwner>` (`NowDialogs.cs:291`, `:309`).
`NowLayout.RunMeasured<TState>` with `Action<TState>`.

**(c) Generic builder types — two, both closed over an enum:**
`NowEnumDropdown<TEnum>` and `NowEnumFlags<TEnum>`, both `where TEnum : struct, Enum`
(`NowValueControls.cs:181`, `:241`), reached through `EnumDropdown<TEnum>()` / `EnumFlags<TEnum>()`
(`:4183,4189,4298,4304`), consumed as `Draw(ref TEnum value)` (`:223`, `:288`). Plus
`NowModifierBuilder<TDeformer>` / `NowModifierScope<TDeformer>` (`NowEffects.cs:642`, `:742`) and
`NowNodeGraphEvaluator<T>.BatchScope`.

**(d) `ref` / `out` on the hot path.** This is the largest structural obstacle, larger than delegates. **Every value
control** takes `ref` (§4.1 — ~30 distinct `Draw(ref T)` methods), and **every `Interact` overload** takes
`out bool focused, out bool submitted` (`NowControls.cs:701-893`). A command buffer has no notion of an aliased
caller variable: for each `ref` argument the encoding must carry the *value in* and the *value out* as two separate
things, and something must decide who owns the value between frames.

**(e) Managed reference types in signatures.** `NowThemeAsset` (91), `NowFontAsset` (21), `UnityEngine.Material`
(18), `UnityEngine.Gradient` (10), `AnimationCurve`, `NowLottieAsset`, `NowMarkupState`, `NowTreeViewState`,
`NowNodeGraph`, `NowDrawList` (7), `IReadOnlyList<string>` (15), `NowRichTextLayout`, `Camera`, `CommandBuffer`
(13), `RenderTargetIdentifier` (11), `NowInputSurface` (14). All must be handle-indirected.

**(f) `ReadOnlySpan<char>`** — 24 occurrences, notably the eight `NowText.Draw` overloads
(`NowText.cs:618-705`) and the numeric formatting overloads. A span cannot be stored in a buffer; the string
overloads exist alongside every one of them.

**(g) `System.Object`** — 23 occurrences, chiefly `NowInspector.Draw(object target)` (`NowInspector.cs:113`),
which reflects over an arbitrary CLR object. No JS analogue exists.

---

## 6. Prior art: the Markup extension

`Assets/NowUI/Extensions/Markup` — 4020 LOC across 9 files. `Documentation~/Markup.md` (250 lines) is the user-facing
spec. It is the only existing NowUI surface driven by a non-C# caller.

### 6.1 Shape

Three entry points, all on `public static class NowMarkup` (`NowMarkup.cs:56`):

```csharp
public static NowMarkupBuilder Document(string markup)   // :88  — a markup string you already have
public static NowMarkupDocument Parse(string markup)     // :93  — explicit parse
public static NowMarkupFile File(string path)            // :102 — hot-reloadable disk file
```

`NowMarkupBuilder` (`:5-50`) is the thinnest builder in the library: two fields (`_markup`, `_state`), one setter
(`SetState`), four `[NowConsumer] Draw` overloads (`:25,32,39,46`) that all forward to
`NowMarkup.GetCached(_markup).Draw(...)`. `GetCached` is a 4-slot reference-identity front cache over a 64-entry
string-keyed dictionary with half-eviction (`:118-157`).

### 6.2 How it handles identity — the single most transferable finding

```csharp
// Assets/NowUI/Extensions/Markup/NowMarkupDocument.cs:480-484
cache.hasExplicitId = HasId(node);
string id = FirstAttribute(node, "id", "name");
cache.nodeId = !string.IsNullOrEmpty(id)
    ? id
    : "markup:" + node.name + ":" + node.sourceIndex.ToString(CultureInfo.InvariantCulture);
```

That is the whole scheme: **an author-supplied `id` attribute, or a synthesized `"markup:<tag>:<sourceIndex>"`
where `sourceIndex` is the node's position in the parse tree.** The synthesized form is the declarative analogue of
`[CallerFilePath]`/`[CallerLineNumber]` — a *stable location in the authored artifact*. `NowMarkupNodeCache` is
attached to the node (`:461-466`) and computed once per parse, so ids are computed at parse time, not draw time.

Derived ids are string concatenation under the same namespace: `nodeId + ".index"`, `nodeId + ".prev"`,
`nodeId + ".next"` (`:505-507`), `nodeId + ".bar"` (`:894`).

Every resolved id then enters the C# API as an explicit `NowId`, at 20 call sites:

```csharp
NowLayout.VerticalScope(new NowId(cache.nodeId), options)              // :756, :987, :1068
NowLayout.ScrollView(new NowId(cache.nodeId)).SetOptions(...).Begin()  // :768
NowLayout.Button("Prev").SetId(new NowId(cache.galleryPrevId))         // :803
NowLayout.Foldout(cache.summaryLabel, new NowId(cache.nodeId))         // :1036
NowLayout.TabBar(labels).SetId(new NowId(cache.tabsBarId))             // :1072
NowLayout.TextField(new NowId(id))                                     // :1315
NowLayout.TextArea(new NowId(id))                                      // :1338
NowLayout.Dropdown(new NowId(id), options)                             // :1364
```

**Markup never uses `SetId(NowResolvedId)`, never opens an `IdScope`, and never uses `KeyedItem`.** Grepped and
confirmed: zero occurrences of `IdScope`, `ControlScope` or `KeyedItem` anywhere in `Extensions/Markup`. Ids resolve
against whatever ambient scope the *caller* left in place.

**The limitation this creates, stated plainly:** drawing the same `NowMarkupDocument` twice in one frame makes every
element in the second draw collide with the first — same authored string, same ambient scope, therefore the same
`NowResolvedId`, therefore shared focus, state and interaction (`NowControls.cs:457-462`). Nothing in the extension
prevents it; the only signal is the dev-build duplicate-id warning. Two *different* documents that both use
`id="save"` collide identically. The caller must wrap in `NowControls.IdScope` — and no documentation in
`Markup.md` says so.

One partial mitigation exists for containers only: `RenderList` uses the id-carrying scope **only when the node has
an explicit id** (`:986-988`) and the anonymous positional scope otherwise. So structural containers fall back to
NowUI's own positional layout identity (§2.7), while controls do not.

`Markup.md:240` names the residual hazard as an authoring rule rather than a mechanism: "No reliance on implicit IDs
for controls that appear in lists or can move."

### 6.3 How it handles state

`NowMarkupState` (`NowMarkupState.cs:158-383`) is a **caller-owned bag of four typed dictionaries**:
`Dictionary<string,bool>`, `<string,int>`, `<string,float>`, `<string,string>` (`:160-163`), keyed by trimmed
strings (`NormalizeKey` `:379-382`, empty keys are silently ignored on write `:212-213`).

The typed getters **cross-read**: `GetBool` falls back to the int, float, then string stores with parsing
(`:181-206`); `GetInt` falls back to float (rounding), bool, then parsed string (`:225-248`); `GetFloat` and
`GetString` similarly (`:283-338`). So the store is loosely typed by design.

The binding rule (`Markup.md:149-155`): `state` / `bind` / `value-key` names a key; `group` overrides for radios;
**"If no state key is provided, interactive controls use their `id`"** — implemented at
`NowMarkupDocument.cs:494-495`:

```csharp
cache.hasStateKey = !string.IsNullOrEmpty(key);
cache.controlKey = string.IsNullOrEmpty(key) ? cache.nodeId : key;
```

Note that *identity* and *state key* are deliberately separate namespaces that default to the same string. The
document holds an `_ownedState` used when the caller passes none (`:178`, `:192`).

There is also `StepInt(key, delta, count, wrap)` (`:260-281`) and `Toggle(key, fallback)` (`:218-223`) — the
primitives the `on-click` action language needs.

### 6.4 How it handles events

Events are a per-draw buffer, not per-control return values:

```csharp
// Assets/NowUI/Extensions/Markup/NowMarkupDocument.cs:175-180
public NowMarkupResult Draw(NowMarkupState state = null)
{
    BeginFrame();
    RenderChildren(_root, state ?? _ownedState);
    return new NowMarkupResult(this, _events, _eventsVersion);
}

// :266-270
void BeginFrame()
{
    _events.Clear();
    ++_eventsVersion;
}
```

`NowMarkupEvent` is `(kind, id, name, value)` with all four fields non-null strings/enum
(`NowMarkupState.cs:14-35`); `NowMarkupEventKind` is `Click | Change | Action` (`:7-12`). Events are recorded
synchronously during the draw (e.g. `Record(new NowMarkupEvent(NowMarkupEventKind.Change, cache.nodeId, key, …))`
at `NowMarkupDocument.cs:1042`, `:1081`).

`NowMarkupResult` is a **borrowed, versioned, single-consumer view**:

```csharp
// Assets/NowUI/Extensions/Markup/NowMarkupState.cs:57-70
public IReadOnlyList<NowMarkupEvent> events
{
    get
    {
        if (_document != null && !_document.IsResultCurrent(_version))
        {
            throw new InvalidOperationException(
                "This NowMarkupResult was invalidated by a later draw of the same document; " +
                "read results before the document draws again.");
        }
        return _events;
    }
}
```

Queries are linear scans: `Clicked(id)` matches id only (`:77-81`); `Changed(idOrKey)` matches **either** the
element id or the state key (`:88-111`) — "so `Changed(\"volume\")` works whether or not the slider bound to the
`volume` key also carries that id" (`:83-87`); `Action(name)` matches emitted action names (`:113-133`).

Typo protection is a manifest-backed dev-build warning, not a type system:

```csharp
// Assets/NowUI/Extensions/Markup/NowMarkupDocument.cs:208-239
internal void ValidateQuery(NowMarkupEventKind kind, string name)
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    …
    bool known = kind == NowMarkupEventKind.Action
        ? _manifest.DeclaresAction(name)
        : _manifest.DeclaresId(name) || _manifest.DeclaresKey(name);
    …
    Debug.LogWarning($"NowMarkup: queried click \"{name}\", but the document declares no matching id or state key. Declared ids: … ; state keys: … .");
#endif
}
```

Every parsed document builds a `NowMarkupManifest` of declared ids, keys and action names
(`NowMarkupDocument.cs:162`, `NowMarkupManifest.cs`), gated by `NowMarkup.validateQueries` (`NowMarkup.cs:64`),
warn-once per `kind + ":" + name` (`:221-224`). `Markup.md:202-208` describes this as the answer to "silent typos".
The *other* half of the answer is codegen: `NowMarkupBindings.GenerateSource(document, "MainMarkup")` emits a
constants class of `Ids` / `Keys` / `Actions` (`Markup.md:209-230`, `NowMarkupManifest.cs`,
`Editor/NowMarkupBindingsGenerator.cs`), so "rename an id, regenerate, and every stale C# reference becomes a
compile error instead of a dead lookup."

### 6.5 What the JS surface can copy, and where it must differ

**Directly reusable, because they solve the same problem:**

* **Explicit id first, positional id as fallback.** `id` attribute, else `"markup:<tag>:<sourceIndex>"`
  (`:480-484`). A JS bridge has the same two options and no third.
* **A separate state-key namespace defaulting to the id** (`:494-495`).
* **A manifest of declared ids/keys/actions plus a warn-once query validator** (`:208-239`) — the only existing
  mitigation in the codebase for "a string lookup that silently returns false forever", which is precisely the
  failure mode a JS API multiplies.
* **A versioned, borrowed result buffer that throws when read stale** (`NowMarkupState.cs:61-68`). A JS
  previous-frame result table has the identical staleness hazard and no existing guard.
* **Event records as `(kind, id, name, value)` string tuples** (`NowMarkupState.cs:14-35`) — already a
  boundary-crossable encoding.
* **`Changed` matching either id or state key** (`:88-111`) — an ergonomic that survives translation.
* **Codegen from the artifact** (`Markup.md:209-230`) — the pattern for turning stringly-typed lookups into
  checkable names.

**Must differ, and why:**

1. **Markup ids are computed once, at parse time, from a tree that exists before the frame.** JS emits calls
   *during* the frame; there is no tree and no `sourceIndex`. Whatever plays that role must either be supplied by
   the JS author or synthesized by the bridge from something it can observe at call time. Markup does not solve this
   problem — it *inherits a solution from having a document.*
2. **Markup's collision behaviour is unsafe at the document level** (§6.2), and it is unsafe in exactly the way the
   brief warns about: two elements sharing an id do not fail, they share state. Copying markup's model without
   adding scoping copies the bug. Note the asymmetry the extension already shows: its *containers* fall back to
   NowUI's positional layout identity (`:986-988`), its *controls* do not.
3. **Markup has no scopes to leak.** Every `using` in the renderer is a C# `using` inside `RenderNode`
   (`:756`, `:768`, `:788`, `:990`, `:1068`), balanced by the recursive walk. Structural balance is guaranteed by
   the tree. An imperative JS caller has no such guarantee, so §3's disposal-order-throws and
   leak-across-`StartUI`-throws facts apply to a JS bridge and do **not** apply to markup. Markup is not prior art
   for the scope problem.
4. **Markup is a closed vocabulary; the builder API is open.** Markup covers ~35 tags and ~25 style properties
   (`Markup.md:80-195`) and explicitly excludes most of the library — `Markup.md:232-239` lists what it will not
   emit. The JS mirror's premise is the *whole* builder surface (55 builders, 184 factories, 830 fluent methods), so
   markup's one-string-per-attribute encoding does not scale to it.
5. **Markup binds values through a key-value store; the C# API binds through `ref`.** `NowMarkupState`
   (`:158-383`) is the existing answer to "immediate-mode controls need caller-owned values but the caller is not
   C#". It is the closest thing in the repo to a solution for §5.3(d), and it is a *store*, not an encoding of
   `ref`.
6. **Markup's result is read once, right after the draw, in the same call stack.** A JS caller reads results
   *before* the frame that produces them, from a table. Markup's `_eventsVersion` guard (`:197-200`) catches
   "read after redraw"; the JS case needs the mirror-image guard, "read before first draw".

---

## 7. Facts most likely to break a design if overlooked

Collected here so they are not buried. Each is cited above.

1. The three attributes mark **return types, not entry points**; `NowControlFactories.cs` — the file containing every
   control factory — has zero attributes (§1.5). `[NowConsumer]` is decorative on 9 of 38 methods (§1.3).
2. Explicit identity is **fully supported today** through four independent mechanisms, all with `NowResolvedId`
   overloads (§2.4). No new identity API is required to make the surface callable without a call site.
3. Explicit ids are **never salted**, and a duplicate is a silent state-share whose only guard is compiled out of
   Release builds (§2.5).
4. `KeyedItem(key)` needs a call site; `KeyedItemIn(listId, key)` does not (`NowControls.cs:241`, `:260`).
5. `IdScope(NowId)` with a default id **silently pushes nothing** (`NowControls.cs:213-214`).
6. `IdScope(NowResolvedId)` **replaces** the ambient path rather than nesting under it (`NowControls.cs:126-138`).
7. Out-of-order scope disposal **throws** (`NowScopeGuard.cs:54-61`).
8. A layout/theme/id/input/GUI/drawlist scope left open across a frame boundary makes the next `Now.StartUI`
   **throw**, not warn (`Now.cs:1279-1293`).
9. `Now.StartUI` **cannot nest** (`Now.cs:1273-1277`).
10. `Begin()` scopes carry interaction results as fields — a value flows *out of the opening call*
    (`NowControlBuilders.cs:315-322`).
11. `bool Draw()` does **not** expose the resolved rect; only `Begin()` and the result structs do (§4.2).
12. Text-field contents are caller-owned via `ref string`; NowUI has no getter (§4.2).
13. Context-menu clicks and dropdown selections are **already one frame late in C#** and become two through a
    previous-frame table (§4.4, §4.5).
14. A measure pass replays the same UI twice per frame with identity rewound; a recorded buffer must be replayable
    idempotently (§2.7, `NowControls.cs:563-580`, `:628-671`).
15. Delegates are ~1% of the parameter surface but gate four whole capabilities: deferred overlays, exact layout,
    docking, node-graph content (§5.3a).
16. `ref`/`out` — not delegates — is the pervasive obstacle: every value control and every `Interact` overload
    (§5.3d).
17. `NowControlState.Get<T>` returns `ref T` and is generic over `struct` — unrepresentable across a boundary
    (`NowControlState.cs:77`).
18. Two public disposable scopes carry no `[NowScope]`, one of them generic (§3.5).
19. `NowResolvedId` has no public value constructor; JS-side handles must be table indices (`NowResolvedId.cs:15`).
20. Control state evicts after 10 s untouched (`NowControlState.cs:35`); a result table must match that lifetime.
21. Markup solves identity by *having a document*, and its collision behaviour across two draws of one document is
    unsafe (§6.2). It is prior art for naming, state and events — not for scopes.

---

## Appendix A — files read

`Assets/NowUI/Runtime/Controls/`: `NowBuilderAttribute.cs`, `NowConsumerAttribute.cs`, `NowScopeAttribute.cs`,
`NowControls.cs`, `NowControlBuilders.cs`, `NowControlFactories.cs`, `NowControlIdentity.cs`, `NowControlState.cs`,
`NowContextMenu.cs`, `NowDropdown.cs`, `NowComboBox.cs`, `NowTextField.cs`, `NowTabs.cs`, `NowScrollView.cs`,
`NowSplitView.cs`, `NowBadge.cs`, `NowFocus.cs`, `NowOverlay.cs`, `NowValueControls.cs`, `NowRichText.cs`,
`NowInspector.cs`, `NowDialogs.cs`, `NowClipboard.cs`, `NowTextInput.cs`.
`Assets/NowUI/Runtime/`: `NowId.cs`, `NowResolvedId.cs`, `NowIdHash.cs`, `NowScopeGuard.cs`, `Now.cs`,
`NowLayout.cs`, `NowLayoutContainers.cs`, `NowTextPreprocessor.cs`, `NowEffects.cs`, `NowRipple.cs`, `NowGlass.cs`,
`NowGradient.cs`, `NowShape.cs`, `NowRectangle.cs`, `NowLine.cs`, `NowDrawList.cs`, `NowTheme.cs`, `NowGUI.cs`,
`NowFrame.cs`, `NowGlassSettings.cs`, `Input/NowInput.cs`, `Input/NowPointerArbiter.cs`, `Lottie/NowLottie.cs`.
`Assets/NowUI/Extensions/`: `Markup/*` (all 9), `Markdown/NowMarkdown.cs`, `NodeGraph/NowNodeGraph.cs`,
`NodeGraph/NowNodeGraphEvaluator.cs`, `Sdf/NowSdf.cs`, `Docking/NowDocking.cs`, `CodeEditor/NowCodeEditor.cs`.
`Assets/NowUI/Analyzers~/NowUI.Analyzers/NowBuilderDiscardAnalyzer.cs`.
`Assets/NowUI/Documentation~/Identity.md`, `Markup.md`.
`Standalone/NowUI.Engine/NowRuntime.cs`; `Tools/Standalone/ApiDump/Program.cs`;
`Docs/Standalone/StandaloneCoreDesign.md` §1.2, §4.8, §4.9;
`artifacts/local/api/after-NowUI.Runtime.api.txt`.

## Appendix B — commands whose output produced the aggregate counts

```
grep -rn "\[NowBuilder\]"  --include=*.cs Assets/NowUI | wc -l      -> 58  (55 shipping, §0)
grep -rn "\[NowConsumer\]" --include=*.cs Assets/NowUI | wc -l      -> 39  (38 real)
grep -rn "\[NowScope\]"    --include=*.cs Assets/NowUI | wc -l      -> 23  (22 real, 20 public)
grep -rn "CallerFilePath"  --include=*.cs Runtime Extensions | wc -l -> 188
grep -rn "SetId(NowId id)" --include=*.cs Runtime Extensions | wc -l -> 39
grep -c "^  method " artifacts/local/api/after-NowUI.Runtime.api.txt -> 2113
grep -cE "^(class|struct|interface|enum|delegate)" …api.txt         -> 349
```
