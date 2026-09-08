# M2 input contract: what a browser host must feed NowUI

Written 2026-09-07, from the source, for the host that turns the rendering slice into an application. Every claim
below cites the file and line it came from. Where the code contradicts the obvious expectation — and it does, in
three places that will each cost an afternoon if guessed — the code wins and the disagreement is called out.

The three traps, up front, because they are the ones a host will get wrong:

1. **The pointer space is top-left, not bottom-left.** Unity's raw mouse API is bottom-left; the *provider contract*
   is not. The flip happens before the provider, in `NowScreenInputProvider`. A browser host does not flip anything.
2. **DOM `PointerEvent.button` 1 and 2 are the opposite of NowUI's.** DOM says 1=middle, 2=secondary. NowUI says
   1=Secondary, 2=Middle. The *bitmask* (`PointerEvent.buttons`) needs no remap; the single-button index does.
3. **The default text-input source is dead in the standalone build.** It is entirely inside
   `#if NOWUI_INPUT_SYSTEM` / `#if ENABLE_LEGACY_INPUT_MANAGER`, neither of which the standalone build defines, so
   it falls through to `return false`. Text entry does not "partly work" — it produces nothing at all until the host
   installs an `INowTextInputSource`.

---

## 0. What the host installs, once, at start-up

Four assignments, none of which need a change under `Assets/`:

```csharp
NowInput.defaultProvider = provider;          // NowUIToolkitInputProvider instance
NowTextInput.source      = textSource;        // host INowTextInputSource + INowTextInputBuffer
NowTextInput.isMacPlatform = navigatorIsMac;  // Application.platform is WebGLPlayer; auto-detect gets this wrong
NowClipboard.setText / getText                // or INowHostServices.clipboard, see §6
```

`NowInput.defaultProvider` is not optional and is not a convenience. `Now.StartUI` reaches the provider through it
and through nothing else: `Now.StartUI(uiScale)` calls `NowInput.BeginScreenFrame` (`Now.cs:1400`), which calls
`Update(_defaultProvider, surface)` (`NowInput.cs:180`). There is no `StartUI` overload that takes a provider. Leave
it at its default and every frame samples `NowScreenInputProvider`, which reads `NowMouseInput` — Unity input that
is compiled out — and the UI stays inert with no error.

Two host-service nulls in `WebHostServices` become live once input exists: `clipboard` (§6) and `touchKeyboard`
(out of scope; `TouchScreenKeyboard.isSupported` stays false, which is correct for a desktop browser).

---

## 1. The frame contract

### 1.1 Where input enters the frame

`Time.frameCount` in the standalone build is `NowRuntime.frameCount` (`Services/Time.cs:29`), incremented exactly
once, by `NowRuntime.BeginFrame()` (`NowRuntime.cs:153`). The provider keys its whole event-buffer lifecycle off that
number:

```csharp
public bool TryGetSnapshot(NowInputSurface surface, out NowInputSnapshot snapshot)
{
    if (_lastFrame != Time.frameCount)
    {
        _lastFrame = Time.frameCount;
        _snapshot = BuildSnapshot();
        ClearTransient(_snapshot);
    }
    ...
}
```
(`NowUIToolkitInputProvider.cs:57`)

So: the first read of a new frame folds every event buffered since the last read into one snapshot, and *immediately*
clears the one-shot state (`ClearTransient`, line 313) — pressed/released masks, scroll accumulation, focus-prev/next,
submit/cancel pressed edges, and the transient navigation vector. Every later read in the same frame gets the same
cached struct. The snapshot is built once and spent once.

That gives exactly one correct ordering:

```
  drain nothing / feed events freely   <- DOM handlers, between frames
  s_Host.Poll();                       <- canvas size for this frame
  NowRuntime.BeginFrame();             <- frameCount++  : MUST come before any NowInput call
  using (Now.StartUI(dpr))             <- first TryGetSnapshot: buffer -> snapshot, buffer cleared
      DrawScene(...);                  <- Interact() consumes the snapshot
  NowRuntime.EndFrame();
```

The rule that matters: **never touch `NowInput` before `NowRuntime.BeginFrame()`**. A `TryGetSnapshot` issued before
the increment stamps `_lastFrame` with the *old* frame number, and the whole new frame then reads a snapshot built
from stale state while the events that arrived since are silently discarded by the next `ClearTransient`.

Feeding the provider directly from DOM event handlers is correct and is the simplest thing that works: JavaScript is
single-threaded and DOM handlers cannot interleave with the `requestAnimationFrame` callback, so "between frames" is
automatic. A host-side event queue drained at the top of `Frame()` is equally correct, provided it drains before
`Now.StartUI`.

### 1.2 What collapses when several events share a frame

The buffer is a fold, not a queue. Positions overwrite (`SetPointerPosition`, line 118); button masks OR together
(lines 128-150); scroll accumulates (line 169). Two consequences, both measured from the code rather than assumed:

- **A press and its release in the same frame still produce a click.** In `Interact`, `pressed` is true from the
  pressed mask, `released` is true from the released mask, `held` is false, and `clicked = released && hovered &&
  !_activeDragged` is true (`NowInput.cs:1073-1146`). A 5 ms click between two frames is not lost.
- **A second press/release pair in the same frame is lost.** The masks are already set; ORing changes nothing. At
  60 Hz that is a 16 ms window and a real double-click is ~150 ms apart, so this is a note, not a defect. If a host
  ever needs exact fidelity it must defer: hold the second pair and feed it after the next `TryGetSnapshot`.

`pointerDelta` is computed frame-to-frame, not event-to-event: `ClearTransient` sets
`_previousPointerPosition = snapshot.pointerPosition` (line 315) so the next frame's delta spans the whole gap.
Intermediate `pointermove` positions inside one frame are therefore *not* lost from the delta, only from the path —
which is right for hover and drag and wrong only for a stroke recorder. Use `getCoalescedEvents()` and feed each one
only if a future feature needs the path; the delta will be identical either way.

### 1.3 `Invalidate()` and `Reset()`

`Invalidate()` (line 74) sets `_lastFrame = -1`, forcing the next read to rebuild. Its doc comment says what it is
for: "tests and custom hosts where frameCount is static". In the browser `frameCount` advances every frame, so
**the browser host must not call `Invalidate()`** — calling it mid-frame would rebuild the snapshot from an
already-cleared buffer and hand the second half of the frame an empty one.

`Reset()` (line 285) is a full wipe: it drops buffered events, clears held-key state, and calls
`NowOverlay.ReleaseRegistrationOwner(this)`. It is the right response to a host-level discontinuity (the page being
re-initialised), and the *wrong* response to a window blur — see §2.6 and §4.5, which need a targeted release of
held state, not a wipe of pointer position and overlay ownership.

### 1.4 What is resolved one frame late, and why the browser host must draw continuously

NowUI settles four things against the *previous* frame's record. This is deliberate and documented in each place:

| Mechanism | Where | What is one frame late |
|---|---|---|
| Pointer arbitration between surfaces | `NowPointerArbiter.cs:6-19` | "The winner is resolved from the previous frame's claims … so the result cannot depend on host update order" |
| Overlay pointer blocking | `NowOverlay.cs:1447` | `IsPointerBlocked` consults `_blocksPrevious` as well as `_blocksCurrent` |
| Focus navigation | `NowFocus.cs:310`, `1697-1747` | "navigation resolves spatially against the previous frame's registry"; the swap runs at the first `BeginFrameIfNeeded` of a new `frameCount` |
| Focus navigation locks | `NowFocus.cs:1079` | "Call every frame from the focused control's draw; effective on the next frame swap" |

Layered on top, controls request future frames for their own animation through `NowControlState.RequestRepaint()`
and `RequestRepaintAt(realtime)` (`NowControlState.cs:699, 708`), and key repeat is generated from held state on a
timer (`Repeat`, `NowControlState.cs:469`, 0.4 s delay then 0.05 s interval) rather than from events.

**Therefore: slice 2 draws continuously, one `Now.StartUI` per `requestAnimationFrame`, exactly as slice 1 does.**
That is not laziness, and the on-demand alternative is not merely harder — it is currently *inexpressible* from the
web host. The repaint signal lives on `NowFrame`: `NowFrame.Begin(uiScale, trackRepaint: true)` and
`NowFrameScope.EndRepaintTracking(out float nextRepaintAt)` (`NowFrame.cs:40, 233`) are both `internal`, `Now.StartUI`
has no `trackRepaint` overload (no occurrence of `trackRepaint` anywhere in `Now.cs`), and `NowUI.Web` is not in
`InternalsVisibleTo` (`Assets/NowUI/Runtime/AssemblyInfo.cs:3-11`). A retained UI Toolkit host gets this because it
lives inside the assembly; a browser host does not.

This is a finding, not a request: it is a gap in the *public* surface of the frozen `Assets/` tree, and it should be
recorded rather than worked around. A host that wanted on-demand drawing today would have to draw at least two frames
after every input event anyway (for the table above), poll nothing for animation, and would still be wrong for
hover-fade and caret blink. Continuous rAF is ~1 ms of managed work per frame for the quick-start scene and is the
honest answer for slice 2.

---

## 2. Pointer

### 2.1 The coordinate space: top-left, in UI units, and no flip anywhere

The brief warned that "pointer positions are in Unity's bottom-left-origin pixel space". That is true of Unity's
`Input.mousePosition` and it is **not** true of the provider contract. The flip lives one layer below:

```csharp
var nextPosition = new Vector2(mouseInput.screenPosition.x, Screen.height - mouseInput.screenPosition.y);
```
(`NowScreenInputProvider.cs:178`, immediately before `NowInput.TryScreenToSurface`)

and the function it feeds is declared as taking a top-left point:

```csharp
internal static bool TryScreenToSurface(Vector2 topLeftScreenPosition, NowInputSurface surface, out Vector2 position)
```
(`NowInput.cs:1341`)

Two independent confirmations: `NowInputSurface.FromCamera` builds its `screenRect` as
`Screen.height - pixelRect.yMax` (`NowInputSnapshot.cs:65`), converting a bottom-left camera rect into a top-left
screen rect; and the renderer's own UI space is top-left with the Y negation baked into the mesh and the matrix
(`M2-ShaderPort.md` §8.4: "UI top → clip +1 → framebuffer top → page top. Correct, with no flip in the matrix and
no flip at the [viewport]").

So the space the snapshot's `pointerPosition` must be in is: **origin at the top-left of the surface, +x right,
+y down, measured in UI units** — the same units the drawing code writes rects in. `Interact` hit-tests it directly
against `Now.TransformScreenRect(region.bounds)` (`NowInput.cs:1005, 1018`), and with no transform stack pushed
`TransformScreenRect` is the identity (`Now.cs:570`). A rect drawn at `(20, 20, 260, 80)` is hovered when the
pointer is between 20 and 280 in x and 20 and 100 in y, counted from the top-left. **A browser host performs no
vertical flip.** DOM coordinates are already in exactly this orientation.

### 2.2 Density: the conversion, and why `?dpr=2` is the test that matters

`NowUIToolkitInputProvider.TryGetSnapshot` takes a `NowInputSurface` and never reads it (`line 57`); `BuildSnapshot`
passes `_pointerPosition` straight through (`line 92`). The provider does **no** space conversion. Whatever the host
feeds is what `Interact` hit-tests. So the host owes the conversion, and the target space is defined by `Now.StartUI`:

```csharp
screenMask = new NowRect(0f, 0f, Screen.width / uiScale, Screen.height / uiScale);
NowInput.BeginScreenFrame(new NowInputSurface(
    new Vector2(screenMask.width, screenMask.height),
    new Rect(0f, 0f, Screen.width, Screen.height)));
```
(`Now.cs:1399-1402`)

`Screen.width/height` is the canvas drawing buffer (`WebHostServices.Poll` reports `canvas.width/height`), and the
host passes `devicePixelRatio` as `uiScale` (`Program.cs`, `Now.StartUI(s_Host.devicePixelRatio)`). So

```
surface size (UI units) = drawingBufferPixels / uiScale = CSS pixels        (when uiScale == dpr)
```

The conversion the host must apply, written so it survives a CSS-scaled canvas and a `?dpr=` override rather than
only the happy case:

```js
const r = canvas.getBoundingClientRect();
const x = (e.clientX - r.left) * (canvas.width  / r.width ) / uiScale;
const y = (e.clientY - r.top ) * (canvas.height / r.height) / uiScale;
```

When `canvas.width === r.width * dpr` and `uiScale === dpr` this reduces to `e.clientX - r.left`: the CSS-pixel
offset inside the canvas, unmodified. That is the useful mental model — *one NowUI unit is one CSS pixel* — but it is
a consequence of the host's own choice to pass `dpr` as `uiScale`, not a law. Write the general form; it costs two
divisions per event and it is the difference between working at `?dpr=2` and drifting by a factor of two.

Do not use `offsetX`/`offsetY`: they are relative to the *padding box* of whatever node is under the pointer, which
is the canvas here but silently is not the moment an overlay div is added. `clientX` minus the bounding rect is
unambiguous.

### 2.3 The button bitmask, and the index remap that is NOT a bitmask

Two different encodings, and the host must treat them differently.

**`pressedButtons` is a bitmask** and it matches DOM `PointerEvent.buttons` bit for bit:

| Bit | `ToButtonMask` (`NowUIToolkitInputProvider.cs:364-384`) | DOM `PointerEvent.buttons` |
|---|---|---|
| 1 | Primary | primary (left) |
| 2 | Secondary | secondary (right) |
| 4 | Middle | auxiliary (middle) |
| 8 | Back | fourth (back) |
| 16 | Forward | fifth (forward) |

Pass `e.buttons` through verbatim. No remap.

**`button` is an index and it does NOT match.** `TryGetButton` (`line 386-409`) maps `0→Primary, 1→Secondary,
2→Middle, 3→Back, 4→Forward`. That is UI Toolkit's ordering (`PointerEventBase.button`: 0 left, 1 right, 2 middle),
which is what the provider was written against. DOM's `PointerEvent.button` is `0 left, 1 middle, 2 right, 3 back,
4 forward`. **1 and 2 are swapped.** The host must remap:

```js
const NOWUI_BUTTON = [0, 2, 1, 3, 4];   // DOM index -> NowUI index
```

Get this wrong and right-click opens nothing while middle-click opens context menus — a symptom that looks like a
control bug and is not. Anything outside 0..4 is silently ignored by `TryGetButton`, which returns false and leaves
the masks untouched; that is a safe default for exotic devices.

### 2.4 Press, drag and release

All of it is decided in `NowInput.Interact` (`NowInput.cs:996-1172`) from the three masks plus position:

- **hover**: `hasPointer && screenRect.Contains(pointerPosition)` and not excluded, ambient-masked or overlay-blocked
  (line 1017). Requires only `SetPointerPosition`.
- **press**: the button's bit is in `pointerButtonsPressed`, the pointer is hovering, no other control is active, and
  nobody has claimed that press already (lines 1049-1098). Sets `_activeId` — NowUI's own capture.
- **held / drag**: while `pointerButtonsDown` still contains the bit, `Interact` measures
  `pointerPosition - _pressPosition` against `dragThreshold` and, once past it, latches `_dragId` so the control keeps
  dragging even when the pointer leaves its rect (lines 1125-1138). `dragThreshold` is
  `4 * max(1, Screen.dpi / 160)` (`NowInput.cs:254-276`), and `WebHostServices` reports
  `dpi = 96 * devicePixelRatio` (`WebHostServices.cs:268`). So in UI units: **4.0 at dpr 1, 4.8 at dpr 2, 7.2 at
  dpr 3.** Those are testable numbers (§7).
- **release / click**: the bit is in `pointerButtonsReleased`; `clicked = released && hovered && !_activeDragged`
  (line 1146). A press that dragged past the threshold never clicks, and a release outside the rect never clicks —
  both are the conventional behaviours and both fall out of this line.

Because the drag latch is `_activeId`-based and independent of hover, **the host must keep delivering moves and the
eventual release even when the pointer leaves the canvas.** That is what DOM pointer capture is for.

### 2.5 Which DOM events, and why pointer events rather than mouse events

Mirror what `NowVisualElement` does, event for event (`NowVisualElement.cs:586-637`):

| DOM event | Provider call | Notes |
|---|---|---|
| `pointerdown` | `setPointerCapture(e.pointerId)`, then `SetPointerDown(pos, remap(e.button), e.buttons)` | Also focus the canvas, so keys arrive |
| `pointermove` | `SetPointerPosition(pos, e.buttons)` | |
| `pointerup` | `SetPointerUp(pos, remap(e.button), e.buttons)`, then `releasePointerCapture` | |
| `pointercancel` | `CancelPointer()` | See §2.6 |
| `pointerenter` | `SetPointerPosition(pos)` | The one-arg overload keeps the current mask (line 113) |
| `pointerleave` | `SetPointerPosition(pos, e.buttons)`; if `e.buttons === 0` then `ClearPointer()` | Exactly `NowVisualElement.cs:629-635` |
| `contextmenu` | none — `preventDefault()` only | So a right-press can reach NowUI's own context menus |

`NowVisualElement` calls `PointerCaptureHelper.CapturePointer` on down and releases on up (lines 604, 613); the DOM
equivalent is `setPointerCapture`, and it is the reason to use pointer events rather than mouse events. The others:
one event family covers mouse, pen and touch, so a tablet or a touchscreen works without a second code path;
`e.buttons` is a first-class bitmask on every event, which `mousemove` also has but `mouseenter` handles worse; and
`pointerId` lets the host bind to a single active pointer and ignore the rest.

**Single pointer only.** The provider has exactly one `_pointerPosition` and one button mask. The host must pick one
pointer — the first that goes down, held in a `activePointerId` field — and drop events from any other `pointerId`
until it is released. Multi-touch is out of scope and pretending otherwise would produce a pointer that teleports
between fingers.

Page-level hygiene the input layer needs and slice 1 did not: `touch-action: none` on the canvas (otherwise a touch
drag scrolls the page instead of reaching the UI), `tabindex="0"` on the canvas (otherwise it cannot take keyboard
focus), `user-select: none`, and `preventDefault()` on `pointerdown` and `contextmenu`.

### 2.6 What `CancelPointer` is for, and the one thing it cannot say

`CancelPointer` (`line 152`) exists for the case where the host loses the pointer without a normal release: DOM
`pointercancel` (the browser took over for a scroll gesture, the touch was interrupted by a system UI), and it is
what `NowVisualElement` calls from `PointerCancelEvent`.

Its implementation is:

```csharp
_pointerButtonsReleased |= _pointerButtonsDown;
_pointerButtonsDown = NowPointerButtons.None;
_hasPointer = false;
```

Trace that through `Interact`. `hasPointer` false means `hovered` is false, so `clicked` is false — a cancel does not
fire a click, which is right. But `WasPointerReleased` is *true*, so `released` is true, so:

```csharp
if (released && _dragId == id) { dragEnded = true; ... }
```
(`NowInput.cs:1140`)

**A cancelled drag reports `dragEnded`, not `dragCancelled`.** A control that commits on `dragEnded` — a slider
writing its value, a window drop — commits on `pointercancel` instead of reverting.

The snapshot has the field for this. `NowInputSnapshot.pointerCaptureCancelled` is documented exactly for it:
"True when the host lost native pointer capture without receiving a normal button release. Active controls cancel
instead of clicking or committing a drag" (`NowInputSnapshot.cs:125-130`), and `Interact` honours it
(`cancelled` at line 1113, `dragCancelled` at 1116). But `NowUIToolkitInputProvider` never sets it: the constructor
hard-codes `pointerCaptureCancelled = false` (`NowInputSnapshot.cs:356`) and `BuildSnapshot` uses that constructor.
The provider returns the snapshot by value, so the host cannot set the field after the fact either.

**This is a genuine limitation of the existing seam and it lives in the frozen tree, so it is reported, not fixed.**
It is not a blocker: `pointercancel` is rare with a mouse, the click case is already correct, and the loss is confined
to drag-commit semantics on touch. Record it; if a slice needs true drag-cancel, the minimal change under `Assets/`
would be a `CancelPointer()` that latches a flag `BuildSnapshot` writes into the snapshot — one field, one line, and
the Unity behaviour of `NowVisualElement` would improve identically, which is a point in its favour.

`ClearPointer` (line 159) is the softer one: it only sets `_hasPointer = false`, leaving buttons alone. That is
"the pointer left the surface" — `pointerleave` with no buttons held — and it is why `NowVisualElement` guards it
with `if (evt.pressedButtons == 0)`: leaving mid-drag must *not* clear the pointer, or the drag strands.

---

## 3. Scroll

### 3.1 The unit

Two units, one conversion, in one method:

```csharp
/// Accumulates scroll from a UI Toolkit WheelEvent.delta (down-positive, roughly three units per wheel notch),
/// normalizing it to the canonical snapshot unit: notches with +y scrolling up.
public void AddScrollDelta(Vector2 delta)
{
    _scrollDelta += new Vector2(delta.x, -delta.y) / 3f;
}
```
(`NowUIToolkitInputProvider.cs:164-172`)

So the **snapshot** unit is notches, +y = up (`NowInputSnapshot.cs:94` says so), and the **method argument** unit is
UI Toolkit's: three units per notch, y down-positive, x passed through unchanged.

The sign convention is confirmed by the consumer rather than taken on trust — `NowScrollView`:

```csharp
scroll.x += wheel.x * step;
scroll.y -= wheel.y * step;
```
(`NowScrollView.cs:476-478`, `step` = `NowTheme.themeAsset.controlStyles.scrollWheelStep`)

`scroll` is a top-left content offset, so `+scrollDelta.y` (wheel up) *decreases* it — the content moves down, which
is what "scroll up" means. And `+scrollDelta.x` increases the horizontal offset, matching DOM `deltaX > 0` meaning
"scroll right". So: **x passes through with DOM's sign, y is negated.**

### 3.2 Normalising a DOM wheel event

`WheelEvent.deltaMode` says which unit `deltaX/deltaY` are in: `0 = DOM_DELTA_PIXEL`, `1 = DOM_DELTA_LINE`,
`2 = DOM_DELTA_PAGE`. There is no cross-browser guarantee of magnitude, so convert to notches with the conventional
constants and then into the method's unit:

```js
const PX_PER_NOTCH   = 100;                       // Chrome/Edge/Safari report ~100 px per detent
const LINES_PER_NOTCH = 3;                        // Firefox reports deltaMode 1, 3 lines per detent
function notches(e) {
  if (e.deltaMode === 1) return [e.deltaX / LINES_PER_NOTCH, e.deltaY / LINES_PER_NOTCH];
  if (e.deltaMode === 2) return [e.deltaX, e.deltaY];               // one page ~ one detent; see below
  return [e.deltaX / PX_PER_NOTCH, e.deltaY / PX_PER_NOTCH];
}
// AddScrollDelta wants UI Toolkit units: 3 per notch, y still down-positive.
const [nx, ny] = notches(e);
provider.AddScrollDelta(new Vector2(nx * 3, ny * 3));
```

Four things worth stating plainly:

- **Do not scale by `devicePixelRatio`.** The wheel unit is notches, not pixels; the pixel-to-scroll-distance
  conversion is `scrollWheelStep`, a theme value in UI units, applied inside the control. Scaling the wheel by dpr
  would double scroll speed on a retina display for no reason.
- **Fractional notches are correct and wanted.** A trackpad emits many small pixel deltas; `_scrollDelta` is a float
  and `scroll.y -= wheel.y * step` is linear, so smooth trackpad scrolling works with no special case. Do not round.
- **`DOM_DELTA_PAGE` is a guess.** No browser in normal configuration emits it; the mapping above is a placeholder
  that will not be exercised. Say so rather than pretending it is calibrated.
- **`preventDefault()` requires `{ passive: false }`** on the `wheel` listener, or the page scrolls behind the canvas.
  Preventing unconditionally is right for a full-window canvas app.

### 3.3 One consumer per frame

`NowInput.ConsumeScrollDelta(rect)` returns the delta and latches `_scrollConsumed` for the rest of the frame
(`NowInput.cs:628-651`), so the first scroll view under the pointer wins and the rest see zero. Nothing is required
of the host here — but it explains why accumulating several wheel events into one frame is the right shape: they
arrive as a single summed delta and one control spends it.

---

## 4. Keyboard

### 4.1 What the provider actually consumes

`KeyDown(KeyCode, bool shift)` (`NowUIToolkitInputProvider.cs:199-244`) is a closed switch. Ten key identities, and
everything else falls to `default: return false`:

| Key | Effect | Gate (`NowInput.navigationKeys`) |
|---|---|---|
| `LeftArrow` / `RightArrow` / `UpArrow` / `DownArrow` | navigation vector | `Arrows` |
| `A` / `D` / `W` / `S` | navigation vector | `Wasd` |
| `Return`, `KeypadEnter` | `submitDown` + `submitPressed` | `EnterSubmit` |
| `Space` | `submitDown` + `submitPressed` | `SpaceSubmit` |
| `Escape` | `cancelDown` + `cancelPressed` | **ungated** |
| `Tab` | `focusPreviousPressed` if `shift`, else `focusNextPressed` | `TabFocus` |

The return value is "NowUI took this key". `NowVisualElement` uses it to `StopPropagation`; the browser host should
use it to decide `preventDefault()` — which is what stops Tab from walking out of the canvas, Space from scrolling
the page, and arrows from scrolling the document. That mapping is exact and free.

The navigation vector is built in `UpdateKeyNavigation` (line 343) from four held booleans, so it is **level, not
edge**: `y` is `+1` for Up and `-1` for Down, i.e. **the navigation vector's +y is up**, opposite to the pointer's
+y-down. Both are correct; they are different quantities. Down/up pairs must be balanced or focus will keep moving.

`KeyUp` (line 246) is deliberately *not* gated by `navigationKeys`, and this asymmetry is worth knowing: `KeyUp`
clears WASD/arrow held state and sets `submitReleased`/`cancelReleased` regardless of the mask. It is harmless
(clearing a flag that was never set is a no-op, and `ClearTransient` lines 324-334 consume the released edges), but
it means a host cannot reason "the mask is off, so this key is inert in both directions".

### 4.2 Mapping DOM keys onto the shim's `KeyCode`

Use `KeyboardEvent.code`, not `.key`, for the physical keys, so a French AZERTY keyboard still navigates with the
key in the WASD position and a dead key does not produce a phantom arrow. Use `.key` only for text (§5). The shim's
values (`Standalone/NowUI.Engine/Enums/KeyCode.cs`):

| DOM `code` | `KeyCode` | Value |
|---|---|---|
| `ArrowUp` / `ArrowDown` / `ArrowLeft` / `ArrowRight` | `UpArrow` / `DownArrow` / `LeftArrow` / `RightArrow` | 273 / 274 / 275 / 276 |
| `Enter` | `Return` | 13 |
| `NumpadEnter` | `KeypadEnter` | 271 |
| `Space` | `Space` | 32 |
| `Escape` | `Escape` | 27 |
| `Tab` | `Tab` | 9 |
| `KeyA` / `KeyW` / `KeyS` / `KeyD` | `A` / `W` / `S` / `D` | 97 / 119 / 115 / 100 |

Note the letters are the **lowercase** ASCII codes (`A = 97`), not the uppercase ones. The shim's header explains the
enum's oddities; this is one of them, and a mapping table built from `'A'.charCodeAt(0)` would be off by 32 and
would silently never navigate.

Everything else (`Backspace`, `Delete`, `Home`, `End`, `F2`, letters for shortcuts) reaches NowUI through the *text*
seam in §5, not through `KeyDown`. Sending them to `KeyDown` is not wrong — it returns false and does nothing — but
it is not how they get consumed.

### 4.3 Modifiers

`KeyDown` carries exactly one modifier: `shift`, and only to choose Tab's direction. Everything else — Ctrl, Alt,
Meta, and Shift for selection — travels on `NowTextInputFrame` (§5). Take `shift` from `e.shiftKey` on the event
itself rather than from a tracked held-set, so a Shift pressed after focus was granted is still seen.

### 4.4 One recommendation: mask off WASD

`NowInput.navigationKeys` defaults to `All` (`NowInput.cs:150`), which includes `Wasd`. In a browser app — where
every screen may contain a text field — W/A/S/D driving focus navigation is a collision waiting to happen. NowUI
does defend it: `NowTextField` registers with `NowFocusNavigationLock.Directional` (`NowTextField.cs:1395`), which
suppresses arrow and WASD focus movement while the field is focused. But locks are "effective on the next frame
swap" (`NowFocus.cs:1079`), and other controls read `NowInput.current.navigation` directly (`NowComboBox.cs:194`,
`NowDropdown.cs:269`, `NowDatePicker.cs:578`).

The doc comment on `navigationKeys` anticipates this — "games that use WASD or Space for gameplay can mask those
out". A browser host should set:

```csharp
NowInput.navigationKeys = NowNavigationKeys.Arrows | NowNavigationKeys.TabFocus |
                          NowNavigationKeys.EnterSubmit | NowNavigationKeys.SpaceSubmit;
```

and leave `Wasd` off. This is a recommendation with a stated reason, not a finding; a host that wants gamepad-style
navigation can turn it back on.

### 4.5 Focus loss

The browser delivers no `keyup` when the window loses focus mid-press, so a held Ctrl or arrow latches forever. On
`blur` (window) and on the canvas losing focus, the host must call `KeyUp` for every key it believes is held and
clear its own held-set for the text frame. Do **not** call `provider.Reset()` for this: it also clears pointer
position and calls `NowOverlay.ReleaseRegistrationOwner`, which is a heavier discontinuity than a blur.

---

## 5. Text entry

### 5.1 The seam, and the fact that it is currently empty

```csharp
public interface INowTextInputSource
{
    bool TryGetFrame(out NowTextInputFrame frame);
}
public interface INowTextInputBuffer
{
    void DiscardPendingText();
}
```
(`NowTextInput.cs:86-94`)

`NowTextInput.source` defaults to `NowKeyboardTextInputSource.instance` (`line 120`). In the standalone build that
object's `TryGetFrame` is *entirely* inside `#if NOWUI_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM` and
`#if ENABLE_LEGACY_INPUT_MANAGER` (lines 546-666), neither of which `NowUI.Runtime.csproj` defines (it defines only
`NOWUI_STANDALONE` and `NOWUI_VG_DISABLE_NATIVE`). The whole body compiles away and the method is `return false;`
(line 668) — at which point `NowTextInput.current` assigns `_frame = default` (line 174).

So text entry is not degraded in the standalone build, it is **absent**, and it becomes present the moment the host
assigns `NowTextInput.source`. Implement `INowTextInputBuffer` on the same object: `NowTextInput.DiscardPending()`
(line 362) casts to it, and text fields call it on focus transitions so characters typed before the field existed do
not land in it.

### 5.2 Typed characters are not key presses

`NowTextInputFrame.characters` is "printable characters typed this frame (control characters stripped)" (line 12).
That is a different quantity from a key press, and the browser gives both: `KeyboardEvent.key` is a single printable
character (`"a"`, `"€"`, `"あ"`) exactly when the keystroke produces text, and a named string otherwise (`"Enter"`,
`"ArrowLeft"`, `"Shift"`, `"Dead"`). The rule that produces correct behaviour on every keyboard layout:

```js
if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) pendingCharacters += e.key;
```

`e.key.length === 1` is the test for "this keystroke made a character", and it handles AltGr, accents and every
non-US layout without a table. Everything with a longer `key` is a named key and belongs to the flags below. Prefer
`beforeinput`/`input` on a hidden field only if IME is being done properly (§5.4); for a first cut `keydown` is
enough and is what the reference source does.

The reason the Ctrl/Meta guard is there rather than being an optimisation: the default source does the same thing,
setting `frame.characters = null` whenever `command && !option` (`NowTextInput.cs:595`, and again at 658), precisely
so `Ctrl+V` does not insert a "v" alongside the paste. Match it, and note the exception the comment names: AltGr
arrives as Ctrl+Alt on Windows, so the guard is `command && !option`, never `command` alone — otherwise AltGr
characters vanish on European layouts.

### 5.3 Held versus pressed, and who generates key repeat

`NowTextInputFrame` mixes two kinds of field, and the difference is load-bearing:

| Kind | Fields | Host must report |
|---|---|---|
| **Held** (level) | `backspaceHeld`, `deleteHeld`, `leftHeld`, `rightHeld`, `upHeld`, `downHeld`, `enterHeld`, `tabHeld`, `shift`, `command`, `option` | true for as long as the key is physically down |
| **Pressed** (edge) | `homePressed`, `endPressed`, `enterPressed`, `escapePressed`, `tabPressed`, `renamePressed`, `copyPressed`, `pastePressed`, `cutPressed`, `selectAllPressed`, `undoPressed`, `redoPressed`, `duplicatePressed`, `commentPressed`, `goToLinePressed` | true for exactly one frame per physical press |

The held ones exist because **NowUI generates its own key repeat**:

```csharp
if (NowControlState.Repeat(id.Child(BackspaceRepeatSeed), frame.backspaceHeld))
```
(`NowTextField.cs:1525`, and the same shape for Delete, Left, Right)

`Repeat` (`NowControlState.cs:469, 480-517`) pulses immediately on the rising edge, then after a 0.4 s delay at
0.05 s intervals, timed from `NowInput.current.time`. Consequences for the host, both concrete:

- Track a held-set (`Set<string>` of `e.code`) from `keydown`/`keyup`, and report *that*. Reporting an edge as
  "held" gives one character per physical press and no repeat.
- **Ignore `e.repeat === true` keydowns for everything except characters.** The browser's auto-repeat and NowUI's
  would compound, deleting at roughly twice the intended rate. Characters are the exception: a held letter key
  *should* insert repeatedly, and the browser's repeated `keydown` is the right source for that, since `characters`
  is a per-frame string with no repeat machinery behind it.

The edge fields must be cleared when consumed. `NowTextInput.current` calls `TryGetFrame` at most once per
`Time.frameCount` (`line 166-177`), so **`TryGetFrame` is a consuming read**: build the frame, then clear the edge
flags and the pending character buffer inside it. That is the same contract the default source honours
(`_pending.Clear()` after `frame.characters` is set, line 555).

### 5.4 Modifiers, and the macOS problem

`command` means "Ctrl on Windows/Linux, Command on macOS" (`line 38`) and `option` means "Alt/Option" (line 41).
The derived properties matter for correct editing: `wordModifier` is Option on macOS and Ctrl elsewhere (line 45),
`lineModifier` is Command on macOS and nothing elsewhere (line 48). Both branch on
`NowTextInput.isMacPlatform`.

**Auto-detection is wrong on the web and the host must override it.** `isMacPlatform` is initialised from
`DetectMacPlatform()`, which tests `Application.platform` for `OSXEditor`/`OSXPlayer` (`lines 129-135`);
`WebHostServices.platform` returns `WebGLPlayer`, so it is *always false* — including in Safari on a Mac, where
Cmd+C would then do nothing while Ctrl+C did the copying. The field is a public mutable static; set it from the user
agent at start-up:

```csharp
NowTextInput.isMacPlatform = /* navigator.platform / userAgentData starts with "Mac" */;
```

and then report `frame.command = isMac ? e.metaKey : e.ctrlKey`, `frame.option = e.altKey`,
`frame.shift = e.shiftKey`.

The shortcut edges the frame carries are exactly the chords the default source produces under `command && !option`
(lines 582-596): C/V/X copy-paste-cut, A select-all, Z undo, Y or Shift+Z redo, D duplicate, `/` comment, G go-to-line,
plus F2 rename which is unmodified. Detect them from `e.code` (`KeyC`, `KeyV`, …) so they survive Dvorak and AZERTY.

Note that the browser will also fire its own behaviour for several of these; `preventDefault()` when the frame
consumes one, or Ctrl+A will select the whole page's DOM behind the canvas.

### 5.5 Composition (IME), and what a first cut may honestly omit

`frame.composition` is "uncommitted IME pre-edit text, or null when not composing. Editors render it inline at the
caret and suppress editing keys while it is non-empty; committed text still arrives through `characters`"
(`NowTextInput.cs:15-20`). Two other hooks exist for it: `NowTextInput.setImeEnabled` (`line 379`), which text fields
drive from `RequestTextCapture` on focus and which `MaintainCapture` turns off after a full frame with no request
(lines 340-352); and `setCompositionCursor` (line 387), which `NowTextField` calls every frame with the caret in
**surface coordinates** (`NowTextField.cs:1618`) so the candidate window can be placed next to it. On the standalone
build both defaults compile down to nothing, so both are free to replace.

Doing composition properly in a browser needs a real focusable editable element — a hidden `<input>` or a
`contenteditable` div positioned at the caret — driven by `compositionstart` / `compositionupdate` /
`compositionend` and `beforeinput`. That element then also becomes the natural source of `characters` (via `input`)
and the reason mobile soft keyboards appear at all.

**A first cut can honestly omit all of it**, and should say which "all" it means:

- Omit: `composition` stays null; `setImeEnabled` and `setCompositionCursor` stay no-ops; no hidden input element.
- Result: Latin, Cyrillic and Greek typing, all editing keys, all shortcuts, and selection work fully. Japanese,
  Chinese and Korean input does not — a candidate window either does not appear or appears at the corner of the
  screen with no inline pre-edit. Dead-key accents (`´` then `e` → `é`) mostly work through `e.key.length === 1`
  because the browser commits them for you, and on some layouts produce the base letter twice.
- What it costs later: composition is additive. Nothing in §5.1-§5.4 has to change to add it; a hidden input
  element becomes an *additional* source feeding `composition` and `characters`.

Mobile soft keyboards are the same omission from the other side: `INowHostServices.touchKeyboard` is null and
`TouchScreenKeyboard.isSupported` is false, which is the correct answer for a desktop browser and the wrong one for a
phone. Out of scope; note it in the report rather than half-doing it.

---

## 6. Clipboard

### 6.1 What NowUI needs

`NowClipboard` is 29 lines and is the single hook for every copy and paste in the library — "selection Ctrl+C, text
field copy/cut/paste, markdown copy buttons and context menus" (`NowClipboard.cs:5-11`). Five files call it:
`NowTextField`, `NowTextArea`, `NowTextSelection`, `NowRichText`, `NowValueControls`.

```csharp
public static Action<string> setText = static text => GUIUtility.systemCopyBuffer = text;
public static Func<string>   getText = static () => GUIUtility.systemCopyBuffer;
```

Two seams, either of which the host may use:

1. Replace the delegates directly. The class comment invites exactly this: "replace once for platforms with their
   own copy flow (WebGL, mobile toasts, tests)".
2. Implement `INowHostServices.clipboard`. The shim's `GUIUtility.systemCopyBuffer` forwards to
   `NowRuntime.host?.clipboard` and is null-safe in both directions (`Imgui.cs:399-422`) — a null clipboard reads
   `""` and swallows writes. `WebHostServices.clipboard` returns null today, with a comment that already names the
   problem (`WebHostServices.cs:158-165`).

Option 2 is the better one: it is the shim's designated seam, it needs no change under `Assets/`, and it keeps the
IMGUI path consistent. The call sites are `NowClipboard.Copy(text)` (skips empty strings) and
`NowClipboard.Paste()` (returns `""` rather than null), driven by `frame.copyPressed` / `cutPressed` /
`pastePressed` (`NowTextField.cs:1504-1520`).

### 6.2 The impedance mismatch, stated plainly

`INowClipboard.GetText()` returns a `string`, synchronously, with no way to express "not yet". The browser's
`navigator.clipboard.readText()` returns a Promise and is gated on a permission prompt and on transient user
activation. **The interface cannot be satisfied faithfully.** Writing is fine —
`navigator.clipboard.writeText(text)` returns a Promise the host can simply not await, and `SetText` returns void, so
a fire-and-forget write matches the contract exactly (with a `document.execCommand('copy')` fallback for older
Safari, from inside the user-gesture handler).

### 6.3 The paste-event cache, and one better idea

**The cache.** Listen for the DOM `paste` event on the document. It fires as part of the browser's own Ctrl+V/Cmd+V
handling, inside a user gesture, with the text already available synchronously on
`e.clipboardData.getData('text/plain')` — no permission prompt, no Promise. Store it:

```js
document.addEventListener('paste', e => {
  hostClipboardCache = e.clipboardData.getData('text/plain');
  e.preventDefault();
});
```

`GetText()` then returns the cached string. The sequencing works because the `paste` event fires *before* the next
`requestAnimationFrame`, so the cache is already warm when `frame.pastePressed` is consumed in the following frame.

Its honest limits: the cache is only as fresh as the last paste gesture into this page. A paste triggered from a
NowUI context-menu "Paste" item — a click, not Ctrl+V — never fires a DOM `paste` event, and would paste whatever
was last pasted, or nothing. And the first `GetText()` before any paste returns `""`.

**Better, and worth doing instead of only the cache.** Combine three things:

1. The `paste` event cache above, as the synchronous answer.
2. An opportunistic async refresh: on every `pointerdown` and on `focus`, fire
   `navigator.clipboard.readText()` and write the result into the cache when it resolves, ignoring rejection. This
   is inside a user gesture, so it is the moment the permission is most likely to be granted, and it keeps the cache
   fresh for menu-driven pastes that never produce a `paste` event.
3. Do not prompt on `GetText()` itself. It is called from inside a frame, the answer would arrive frames later, and
   a permission dialog raised from a render loop is a bad experience and races the user's own gesture.

Together these make Ctrl+V exact, make menu paste correct after the first pointer interaction, and never block a
frame. What remains impossible is a *first* menu paste on a page the user has never clicked, which cannot be fixed
at this layer and should be documented rather than engineered around.

There is a second, quieter reason to prefer the cache as the primary path: `getData` returns exactly what the user
copied, whereas `readText()` in Firefox is gated behind a paste-context prompt that appears as a small popup the user
must click — for a UI that draws its own everything, an unexplained browser popup mid-frame is a worse failure than a
stale string.

---

## 7. Test plan

Each row names one observable that is objectively true or false. "Reads back" means `gl.readPixels` on the live
framebuffer, which `M2-Scouting.md` establishes as the trustworthy oracle here — headless screenshot capture is
explicitly not (see its "Headless capture: works sometimes, and is NOT yet trustworthy"). Every pointer test runs
**twice, at `?dpr=1` and `?dpr=2`**; a density error is invisible at 1 by construction.

A scene sufficient for all of it: one button, one horizontal slider, one scroll view with more content than fits,
two text fields (so Tab has somewhere to go), at a known rect.

### 7.1 Hover

- **Drive:** dispatch `pointermove` at CSS `(button.x + 5, button.y + 5)` inside the canvas.
- **Observable:** the button's pixels change to its hover colour, read back at the button's centre; the pixel at
  `(button.x - 5, button.y - 5)` does not change. Then move to `(button.x - 5, button.y - 5)` and confirm the button
  reverts.
- **Proves:** coordinate space, origin, and density, in that order. A vertical flip shows as "hover works at the
  mirrored y"; a density error at `?dpr=2` shows as "hover requires half the coordinates". Both are unambiguous, and
  distinguishable from each other, which is the point of hovering an off-centre rect rather than a centred one.
- **Boundary case worth running:** hover exactly `(button.x, button.y)` and `(button.xMax - 0.5, button.yMax - 0.5)`.
  `Rect.Contains` is min-inclusive, max-exclusive; the edge behaviour should match Unity's.

### 7.2 Click

- **Drive:** `pointerdown` then `pointerup`, both at the button centre, same position, DOM `button: 0`,
  `buttons: 1` then `0`.
- **Observable:** the button's click handler fires exactly once (increment a counter drawn as text, read it back or
  assert on the managed side). Pressed-state colour visible between the two events, when a frame renders in between.
- **Also assert:** press at the centre, move 60 units away, release — counter does **not** increment (release outside
  the rect never clicks, `NowInput.cs:1146`).
- **Right-click:** `pointerdown` with DOM `button: 2, buttons: 2` opens the context menu; DOM `button: 1,
  buttons: 4` (middle) does not. This is the single test that catches the §2.3 index swap, and it fails loudly and
  specifically if the remap is missing.

### 7.3 Drag

- **Drive:** `pointerdown` on the slider handle; a sequence of `pointermove`s increasing x by 2 units per frame;
  `pointerup`.
- **Observable, threshold:** after a total movement of 3 units the slider value has not changed; after 5 units it
  has. At `?dpr=2` the crossing moves to between 4 and 5 units (`4 * max(1, 96*2/160) = 4.8`). Those two numbers
  differing between the two runs *is* the proof that density is threaded through correctly — it is the one place
  where the correct behaviour is not "identical at both densities".
- **Observable, capture:** with the button held, dispatch `pointermove` with coordinates outside the canvas
  (negative y). The slider must keep tracking, and the eventual `pointerup` outside must end the drag. This proves
  `setPointerCapture` is wired; without it the browser stops delivering moves and the slider strands mid-drag.
- **Known-limitation check, not a pass/fail gate:** dispatch `pointercancel` mid-drag and record what happens. Per
  §2.6 the expected observation today is that the drag *commits* (`dragEnded`) rather than reverting. Recording it
  as a known result is the honest outcome; asserting it reverts would be asserting a bug fix that does not exist.

### 7.4 Wheel scroll

- **Drive:** `wheel` with `deltaMode: 0, deltaY: 100` over the scroll view.
- **Observable:** content moves up by exactly `scrollWheelStep` UI units (one notch × the theme step) — measure by
  reading back the y of a horizontal rule drawn in the content. `deltaY: -100` moves it back by the same amount to
  the original position.
- **Sign gate:** `deltaY: +100` (wheel toward the user) must move content **up**, i.e. reveal content further down.
  If it goes the other way the `-delta.y` in `AddScrollDelta` has been double-applied.
- **deltaMode:** `deltaMode: 1, deltaY: 3` must produce the same displacement as `deltaMode: 0, deltaY: 100`. This is
  the Firefox path and it is easy to leave untested until a Firefox user reports scrolling at a thirtieth speed.
- **Ownership:** with the pointer *outside* the scroll view, the same wheel event must not move it.

### 7.5 Keyboard navigation

- **Drive:** click text field A to focus it, then `keydown` `Tab`.
- **Observable:** the focus ring is drawn on field B and not on A (read back the ring's pixels at both). Then
  `Shift+Tab` returns it to A.
- **Arrows:** with the button focused and no text field focused, `ArrowRight` moves focus to the next control in the
  layout. With a text field focused, `ArrowRight` moves the caret and does **not** move focus — this is the
  `NowFocusNavigationLock.Directional` path (`NowTextField.cs:1395`) and it confirms the lock's one-frame lateness
  is being satisfied by continuous drawing.
- **Submit:** with the button focused, `keydown Space` fires the click handler exactly once, and `keyup Space` fires
  it zero more times.
- **Cancel:** with a dropdown open, `keydown Escape` closes it.
- **preventDefault:** after `Tab` inside the canvas, `document.activeElement` is still the canvas. If the browser's
  own focus walked away, the return value of `KeyDown` is not being used to suppress the default.
- **Latency note the test must respect:** focus navigation resolves against the previous frame's registry
  (`NowFocus.cs:310`). Assert *after* at least two rendered frames, not on the frame the key was dispatched. A test
  that asserts on the same frame will fail against correct code.

### 7.6 Text entry

- **Drive:** click into field A, then `keydown` for `H`, `i`.
- **Observable:** the field reads "Hi" — read the text back from the managed side, and confirm visually that the
  glyphs render (the text path is already proven by slice 1, so a discrepancy here is input, not rendering).
- **Repeat:** hold `Backspace` (one `keydown`, then no further events, held-set true) for 1 second. Expect exactly
  one deletion immediately, then deletions at 20 Hz after 0.4 s — about 13 characters in 1.0 s
  (`1 + (1.0 - 0.4)/0.05 = 13`). Approximately, because it is sampled per frame at 60 Hz; the assertion should be
  "between 10 and 14", and "exactly 1" would prove held-state is not being reported while ">25" would prove the
  browser's auto-repeat is being forwarded on top of NowUI's.
- **Caret:** `ArrowLeft` moves the caret one character and does not move focus; `Home` jumps to the start;
  `Shift+ArrowRight` extends a selection (visible as the selection rect in the readback).
- **Copy/paste round trip:** select all (`Ctrl+A` / `Cmd+A` per platform), `Ctrl+C`, click into field B,
  `Ctrl+V`. Field B reads "Hi". This exercises §6's cache: the `paste` event fires as part of the browser's own
  Ctrl+V handling, so the string is present synchronously when the next frame consumes `pastePressed`.
- **The Ctrl-guard:** `Ctrl+V` must not also insert the character "v". This is the single test for the
  `command && !option` guard, and its failure mode ("Hiv") is instantly recognisable.
- **AltGr, if a European layout is available:** AltGr+E on a Spanish layout must insert `€`, not nothing. This is the
  half of the guard that the previous test cannot see.
- **macOS, if available:** with `isMacPlatform` set, `Cmd+C` copies and `Ctrl+C` does not; `Option+ArrowLeft` moves
  by word. Without the §5.4 override this fails on a Mac while passing everywhere else, which is exactly the kind of
  bug that ships.

### 7.7 Cross-cutting: the density sweep

Run 7.1 through 7.6 at `?dpr=1` and `?dpr=2`. Everything except the drag threshold (7.3) must produce **identical
observations in CSS-pixel terms** at both densities. The drag threshold must differ, 4.0 versus 4.8. Any other
difference between the two runs is a density bug, and stating it this way — "one number is allowed to change, and
here is which" — is what makes the sweep an oracle rather than a smoke test.

---

## 8. Findings, collected

Things the verification unit and the host author should know, separated from the specification proper.

1. **`pointerCaptureCancelled` is unreachable through this provider.** `NowInputSnapshot` has the field and
   `NowInput.Interact` honours it, but `NowUIToolkitInputProvider.BuildSnapshot` cannot set it, so a cancelled drag
   reports `dragEnded` and commits. Affects DOM `pointercancel` and equally affects Unity's UI Toolkit host. Frozen
   tree; reported, not fixed. §2.6.
2. **On-demand redraw is not expressible from a browser host.** `NowFrame.Begin(trackRepaint:)` and
   `EndRepaintTracking` are `internal`, `Now.StartUI` has no equivalent, and `NowUI.Web` is not in
   `InternalsVisibleTo`. Slice 2 must draw continuously. §1.4.
3. **`NowTextInput.isMacPlatform` auto-detects to false on the web, always**, because
   `Application.platform` is `WebGLPlayer`. Every Mac user's Cmd shortcuts break unless the host overrides the
   public field. §5.4.
4. **DOM `PointerEvent.button` 1 and 2 are swapped relative to NowUI's index**, while `PointerEvent.buttons` matches
   exactly. §2.3.
5. **The default text-input source is compiled to `return false`** in the standalone build. Text entry is absent, not
   degraded, until the host installs a source. §5.1.
6. **`Now.StartUI` reaches the provider only through `NowInput.defaultProvider`.** There is no overload that takes
   one. §0.
7. **`KeyUp` is not gated by `navigationKeys` while `KeyDown` is** — a documented asymmetry, harmless in effect,
   surprising to reason about. §4.1.
8. **`INowClipboard` is synchronous and the browser clipboard read is not.** No implementation can be faithful; the
   paste-event cache plus an opportunistic gesture-time refresh is the best available and its residual gap (a first
   menu-driven paste with no prior interaction) should be documented rather than engineered around. §6.
