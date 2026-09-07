# Unity gradient, curve and utility semantics spec (for clean-room shim)

**Scope and provenance.** This document extends `UnityValueTypeSemantics.md` with the `UnityEngine` types NowUI's runtime uses for gradients, animation curves and a few utilities: `Gradient`, `GradientColorKey`, `GradientAlphaKey`, `GradientMode`, `AnimationCurve`, `Keyframe`, `WrapMode`, `WeightedMode`, `ColorUtility`, `LayerMask`, `ExpressionEvaluator`, `TouchScreenKeyboard` and `TouchScreenKeyboardType`. It was derived from (a) reading the C# in `Unity-Technologies/UnityCsReference` (branch `6000.4`, cross-checked against `master` as of 2026-09-07: `Runtime/Export/Math/Gradient.bindings.cs`, `Runtime/Export/Animation/AnimationCurve.bindings.cs`, `Runtime/Export/Math/ColorUtility.cs` + `ColorUtility.bindings.cs`, `Runtime/Export/Scripting/LayerMask.bindings.cs`, `Runtime/Export/ExpressionEvaluator.cs`, `Runtime/Export/TouchScreenKeyboard/*.cs`), (b) the Unity Scripting API docs, and (c) an empirical probe run against **Unity 6000.4.0f1 (Windows x64 editor, Mono)** through the project's EditMode harness for every member whose body lives in native code (`Gradient.Evaluate`, key storage, `AnimationCurve.Evaluate`, `AddKey`, `MoveKey`, `SmoothTangents`, wrap modes, equality, `ColorUtility.TryParseHtmlString`, `LayerMask` lookups, `TouchScreenKeyboard` in the editor). Values marked **[verified]** come from that probe and are given with IEEE-754 single bit patterns where exactness matters. Formulas marked **[bit-exact]** were reverse-engineered by brute-forcing candidate float expressions against the probe output and reproduce every captured sample bit for bit.

**Licensing note.** UnityCsReference is published under the Unity Reference-Only License; it may be read to understand behaviour but must not be copied. This spec therefore describes semantics, algorithms and formulas so that an implementer can write original code. No Unity native (C++) source was consulted, and public mirrors of leaked engine source were deliberately not opened; native behaviour is described from documentation plus black-box measurement.

**Notation.** As in the value-type spec: `f32(expr)` = evaluate in single precision; "bits `0x…`" is the IEEE-754 single pattern; all arithmetic is `float` unless stated. `k` denotes a gradient key time in 16-bit fixed point (see §3.1). `∞` means `float.PositiveInfinity`.

---

## 0. How NowUI uses these types (what the shim must get right first)

| Type / member | Used by | What matters |
|---|---|---|
| `Gradient` as `Dictionary` key | `NowGradient.cs` (`NowGradientRampCache._gradientRamps`) | NowUI supplies its own `IEqualityComparer` (reference equality + `RuntimeHelpers.GetHashCode`), so Unity's `Equals`/`GetHashCode` are never consulted for the cache. They still matter for user code and `NowInspector` change detection (§3.7). |
| `Gradient.mode` (get/set), `Evaluate(t)` for `t = i / 255f`, `i ∈ [0,255]` | `NowGradient.cs` (`BakeGradientRow`), `NowValueControls.cs` (`FillGradientTexture`, 1024 samples of `Evaluate(Mathf.Clamp01(t))`) | The result is immediately converted `Color → Color32` (saturate, `×255`, round). Blend/Fixed ramps must therefore match Unity to better than 1/510 per channel; PerceptualBlend to within one byte step. `mode == GradientMode.Fixed` selects point filtering. |
| `new Gradient()`, `SetKeys(colorKeys, alphaKeys)`, `colorKeys`/`alphaKeys` getters | `NowValueControls.cs` (`NowGradientField`), `NowInspector.cs` | Getters must return **fresh arrays** (NowUI sorts the returned arrays in place and caches them). NowUI never limits key counts to 8 (see §10.4 hazard). A static `PreviewGradient` instance is re-keyed every bake. |
| `GradientColorKey(Color, float)`, `.color`, `.time`; `GradientAlphaKey(float alpha, float time)`, `.alpha`, `.time` | `NowValueControls.cs` | Plain structs. |
| `GradientMode.{Blend, Fixed, PerceptualBlend}` | `NowGradient.cs`, `NowValueControls.cs` | Enum values 0/1/2. |
| `AnimationCurve()` ctor, `AnimationCurve.Linear(0,0,1,1)`, `keys` (get/set), `length`, `preWrapMode`/`postWrapMode` (get/set), `Evaluate(t)` | `NowValueControls.cs` (`NowAnimationCurveField`, static `PreviewCurve`), `NowInspector.cs` | NowUI edits `Keyframe[]` itself (insert/delete/sort/smooth) and only round-trips through `keys`; it draws Hermite segments itself and samples `Evaluate` only for wrapped ranges, weighted segments and partially visible segments. `AddKey/RemoveKey/MoveKey/SmoothTangents/this[]` are **not** used. |
| `Keyframe(time, value)`, `.time/.value/.inTangent/.outTangent/.inWeight/.outWeight/.weightedMode`; `WeightedMode` bit tests; `WrapMode`; `float.PositiveInfinity` tangents for steps | `NowValueControls.cs` | Field semantics, `Both = In|Out`. |
| `ColorUtility.TryParseHtmlString` | `NowValueControls.cs` (`"#" + hex`), `NowRichTextParser.cs` (lower-cased names/hex after its own hex fast path), `Extensions/Markup/NowMarkupStyles.cs` (prefixes `#` when missing) | Accepted formats, failure output. |
| `ColorUtility.ToHtmlStringRGB/RGBA` | `NowValueControls.cs` (`FormatColor`) | Upper-case hex, rounding. |
| `LayerMask.LayerToName(int)`, `LayerMask.value`, implicit `int → LayerMask` | `NowMaskField.cs`, `NowInspector.cs` (`typeof(LayerMask)`, unbox) | Host-provided layer table. `NameToLayer`/`GetMask` are not used. |
| `ExpressionEvaluator.Evaluate(string, out double)` | `NowNumericExpression.cs` (behind its own strict grammar check, inside `try/catch`, with a managed fallback) | Grammar, culture, precision quirks. |
| `TouchScreenKeyboard.isSupported`, `Open(text, TouchScreenKeyboardType.Default)`, `Open(text, Default, autocorrection:false, multiline:true)`, `.status`, `Status.Visible/Done`, `.text` (get), `.active` (get/set) | `NowTextField.cs`, `NowTextArea.cs` | All calls are guarded by `isSupported`; a shim that reports `false` never has to construct one. |

`NowInspector.cs` also uses `typeof(Gradient)`, `typeof(AnimationCurve)` and `typeof(LayerMask)` for value-kind dispatch, and `NowMaskField` calls `LayerMask.LayerToName` for layers 0..31 once, skipping empty names.

**Layout sizes [verified].** `Marshal.SizeOf`: `Keyframe` = 32, `GradientColorKey` = 20, `GradientAlphaKey` = 8, `LayerMask` = 4.

---

## 1. GradientColorKey / GradientAlphaKey

Both are plain `public struct`s (no interfaces, no operators, no `Equals`/`GetHashCode` overrides; the default `ValueType` implementations apply).

```
public struct GradientColorKey { public Color color; public float time;
    public GradientColorKey(Color col, float time); public GradientColorKey(in Color col, float time); }
public struct GradientAlphaKey { public float alpha; public float time;
    public GradientAlphaKey(float alpha, float time); }
```

Field declaration order is `color, time` and `alpha, time` (sizes 20 and 8 bytes, sequential). Times are nominally in `[0,1]`; nothing is validated in the struct itself — clamping and quantisation happen when the key is handed to a `Gradient` (§3.1). The `alpha` component of `GradientColorKey.color` is carried through the getter unchanged but is **ignored** by `Evaluate` [verified].

---

## 2. GradientMode

`public enum GradientMode { Blend = 0, Fixed = 1, PerceptualBlend = 2 }`. `Gradient.mode` is stored in **8 bits**: setting `(GradientMode)(-1)` reads back as `255`, setting `7` reads back as `7` [verified]. Evaluation with an undefined value is not verified (a default gradient evaluated white under `mode = 7`, which is uninformative); the shim should treat `1` as Fixed, `2` as PerceptualBlend and everything else as Blend and document that choice.

---

## 3. Gradient

`public class Gradient : IEquatable<Gradient>` (not sealed; sequential layout wrapper around a native pointer plus a finalizer — the shim needs neither the pointer nor the finalizer). Members:

```
public Gradient();
public Color Evaluate(float time);
public GradientColorKey[] colorKeys { get; set; }     public GradientAlphaKey[] alphaKeys { get; set; }
public int colorKeyCount { get; }                      public int alphaKeyCount { get; }
public void GetColorKeys(Span<GradientColorKey> keys); public void GetAlphaKeys(Span<GradientAlphaKey> keys);   // throw ArgumentException("Destination array must be large enough to store the keys", "keys") when too small
public void SetColorKeys(ReadOnlySpan<GradientColorKey> keys); public void SetAlphaKeys(ReadOnlySpan<GradientAlphaKey> keys);
public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys);   // = SetKeys(colorKeys.AsSpan(), alphaKeys.AsSpan()); a null array becomes an empty span
public void SetKeys(ReadOnlySpan<GradientColorKey> colorKeys, ReadOnlySpan<GradientAlphaKey> alphaKeys);
public GradientMode mode { get; set; }                 public ColorSpace colorSpace { get; set; }
internal Color constantColor { get; set; }              // particle-system only; the shim may omit it
public override bool Equals(object o); public bool Equals(Gradient other); public override int GetHashCode();
```

### 3.1 Storage model [verified]

- **Key times are quantised to 16-bit fixed point on write.** The stored time is `k = (ushort)(clamp01(time) * 65535 + 0.5)` (round half up; the half-up direction is pinned by a key at `0.3`, which must round `19660.5` up to `19661` for a Fixed-mode probe to hold). Reading back returns `k / 65535f`. Consequences seen in the probe: `0.5 → 0.5000076 [0x3F000080]` (k = 32768), `0.25 → 0.2500038 [0x3E800080]`, `0.75 → 0.7499962 [0x3F3FFFC0]`, `1/9 → 0.1111162 [0x3DE390E4]`, `0.2 → 0.2` exactly (k = 13107), `0.6 → 0.6` exactly (k = 39321). Out-of-range times are clamped: `-0.5 → 0`, `1.5 → 1`, `-1 → 0`, `2 → 1`.
- **Colours and alphas are stored as `float`s, not bytes**: a colour key `(0.123, 0.456, 0.789)` evaluates back exactly at its own time, and HDR/negative components (`2`, `-1`) survive unchanged.
- **Keys are kept sorted by `k`, ascending, with a stable sort**: keys given as `{red@1, blue@0, green@0.5}` read back as `{blue, green, red}`; two keys at the same time keep their given relative order (`{red@0.5, blue@0.5}` and `{blue@0.5, red@0.5}` read back in the order set).
- **Each key array is validated independently and silently:** `SetKeys` / the property setters apply an array only when `1 ≤ count ≤ 8`. An array with 0 keys, `null`, 9 keys or 10 keys is **ignored** (no exception, previous keys kept, the other array is still applied if valid). Docs say "maximum of 8", but not that larger arrays are dropped wholesale.
- **A single key is expanded to two:** setting one colour key `red@0.3` stores `{red@0, red@1}`; one alpha key `0.5@0.7` stores `{0.5@0, 0.5@1}` — the key's own time is discarded. So a valid gradient always has 2..8 colour keys and 2..8 alpha keys; `colorKeyCount`/`alphaKeyCount` report the stored (post-expansion) counts.
- The getters allocate a **fresh array on every access** (`ReferenceEquals(g.colorKeys, g.colorKeys)` is false) — NowUI relies on this.
- `mode` is 8-bit (§2). `colorSpace` is a full `int`: default `ColorSpace.Uninitialized (-1)`; `7` and `-1` read back unchanged.

### 3.2 Constructor defaults [verified]

`new Gradient()` has `mode = Blend`, `colorSpace = Uninitialized (-1)`, colour keys `{white@0, white@1}` (`(1,1,1,1)` at k = 0 and k = 65535), alpha keys `{1@0, 1@1}`. `Evaluate` of a default gradient is `(1,1,1,1)` everywhere.

### 3.3 Evaluate — common search [bit-exact]

Let `tq = t * 65535f` (single precision; `t` itself is **not** quantised). If `t` is NaN the result is `(0,0,0,0)` [verified: `Evaluate(NaN) = (0,0,0,0)` even for a red→blue gradient]. Otherwise, for a key list `key[0..n-1]` (n ≥ 2, sorted by integer time `key[i].k`):

1. `tq = clamp(tq, key[0].k, key[n-1].k)` (as floats; the key times are exact integers).
2. `i` = the largest index with `key[i].k <= tq`, then `i = min(i, n-2)`. The segment is `(key[i], key[i+1])`.
3. `denom = (float)(key[i+1].k - key[i].k)` (integer subtraction, then converted). `u = denom > 0 ? (tq - key[i].k) / denom : 0`.

This is done separately for the colour keys and the alpha keys. Consequences (all verified): `t` before the first key returns the first key, after the last key returns the last key (`t = -1, 2, ±∞, 1.0000001, -1e-7` all clamp); with duplicate times `X` (`…, red@X, blue@X, next`) a `t` just below `X` interpolates toward **red** (the first duplicate) and a `t` just above `X` interpolates from **blue** (the last duplicate); when **all** keys share one time the result is the first key everywhere (the `0/0` guard), e.g. `{red@0.5, blue@0.5}` evaluates red at `t = 0, 0.5, 1`, and `{red@0, blue@0}` evaluates red at `t = 1`.

### 3.4 Blend (mode 0) [bit-exact, 34/34 samples]

Per channel `r, g, b` of the colour segment and for the alpha segment:

```
result = a + (b - a) * u          // all single precision; a = key[i].color.x, b = key[i+1].color.x
```

Exact single-precision forms that do **not** match the probe: computing `u` from the `k / 65535f` floats (`(t - q0) / (q1 - q0)`), multiplying by a reciprocal, `a * (1 - u) + b * u`, or double intermediates — the fixed-point `u` above is the only form that reproduced all 34 captured bit patterns. Nothing is clamped: `(2,0,0)@0 → (0,0,-1)@1` gives `(1, 0, -0.5)` at `t = 0.5`, alpha `2 → -1` gives `0.5`. Sample values: keys `(0.2,0.4,0.6)@0`, `(0.9,0.1,0.3)@1`, `t = 0.1` → `(0.27 [0x3E8A3D71], 0.37 [0x3EBD70A4], 0.57000005 [0x3F11EB86])`; keys `c0@0.13`, `c1@0.61`, `t = 0.2` → `u = 0.14582273 [0x3E15528E]` (from `k0 = 8520, k1 = 39976`). `colorSpace` has no effect on Blend [verified: black→white at 0.5 is `0.5` under Uninitialized, Gamma and Linear].

### 3.5 Fixed (mode 1) [verified]

Same search; the result of a segment is `tq <= key[i].k ? key[i] : key[i+1]` — i.e. the first key whose time is `≥ tq`, the first key before the range and the last key after it. Applies to colour **and** alpha keys (alpha steps too: alpha keys `0@0.25, 1@0.75` give `0` at `t = 0.25` and `1` at `t = 0.26`). With duplicates `red@X, blue@X` a `t` at or below `X` yields red, above `X` the next key. Because of quantisation, `t = 0.5` (`tq = 32767.5`) is *below* a key set at `0.5` (k = 32768) and still returns that key.

### 3.6 PerceptualBlend (mode 2) [approximately verified]

Same search and `u`; alpha is still the linear lerp of §3.4 (`0.3 → 0.8` gives `0.55` at `t = 0.5`). Colour is mixed in **Oklab** (Björn Ottosson's space, 2021 constants — the 2020 constants fit worse):

1. **Input transfer** (only when `colorSpace != ColorSpace.Linear`, i.e. for the default `Uninitialized` and for `Gamma`): each RGB component is converted sRGB → linear with the standard piecewise curve (`c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055)^2.4`); components **above 1 use `c^2.2`** instead (this reproduces `(2,0,0)→black` at `0.5` = `200/255` where the exact curve would give 206/255). Negative inputs are unverified. When `colorSpace == Linear` the key colours are used as linear values directly and step 4 is skipped.
2. Linear RGB → LMS via the `M1` matrix (`0.4122214708 0.5363325363 0.0514459929 / 0.2119034982 0.6806995451 0.1073969566 / 0.0883024619 0.2817188376 0.6299787005`), cube root each, then → `Lab` via `M2` (`0.2104542553 0.7936177850 -0.0040720468 / 1.9779984951 -2.4285922050 0.4505937099 / 0.0259040371 0.7827717662 -0.8086757660`).
3. `Lab = Lab0 + (Lab1 - Lab0) * u` per component (equivalently mixing the cube-rooted LMS, since the two are related linearly); then `Lab → LMS'` (`l' = L + 0.3963377774a + 0.2158037573b`, `m' = L - 0.1055613458a - 0.0638541728b`, `s' = L - 0.0894841775a - 1.2914855480b`), cube, and LMS → linear RGB (`4.0767416621 -3.3077115913 0.2307590544 / -1.2684380046 2.6097574011 -0.3413193965 / -0.0041960863 -0.7034186147 1.7076147010`).
4. **Output transfer** (non-Linear colour space only): linear → sRGB with the standard curve (`c <= 0.0031308 ? 12.92c : 1.055·c^(1/2.4) - 0.055`), components above 1 use `c^(1/2.2)` (reproduces `white→blue` at 0.5, blue channel `1.0372324`), and **in-range results are quantised to `n/255`** (`n` an integer; every in-gamut probe result was exactly `n/255`, e.g. `0.549019635 = 140/255`), while out-of-range results are returned unquantised.

Verified samples (Gamma/Uninitialized, `t = 0.5` unless stated): red→blue `(140, 83, 162)/255`, at 0.25 `(198, 73, 109)`, at 0.75 `(81, 71, 210)`; black→white 0.25/0.5/0.75 → `34/99/174`; red→green `(208, 168, 0)`; `(0.2,0.4,0.6)→(0.9,0.1,0.3)` at 0.3 `(120, 97, 133)` and at 0.5 `(154, 89, 119)`; yellow→blue `(112, 162, 195)`; white→blue `(116, 163, 264.49/255 = 1.0372324)`; grey→grey and red→red are identity (so no gamut-mapping occurs). Reference double-precision Oklab with the constants above reproduces every one of these after rounding to a byte **except** the red channel of white→blue (reference `115.47` vs Unity `116`), and the `colorSpace = Linear` sample red→blue at 0.5 is `(0.263734281, 0.08657174, 0.362824231)` where the reference gives `(0.263676, 0.0865717, 0.3628243)` — Unity's red channel is consistently ~2–5e-4 (relative) higher than the reference while G and B agree to 7 digits. The cause (a differing constant in Unity's first LMS→RGB row, or an approximation) could not be identified; the shim should implement the reference maths and accept a rare ±1 byte difference in R on baked ramps. Use the listed samples as regression fixtures.

### 3.7 Equality and hashing [verified]

- `Equals(Gradient other)`: `false` for null; `true` for the same reference; otherwise a **content comparison** — colour keys (colour and quantised time), alpha keys, `mode` and `colorSpace` must all match. Verified: two independently built gradients with the same keys are equal; differing `mode`, differing `colorSpace`, differing alpha, or a different key set are not; keys supplied in a different order (which sort the same) are equal. `Equals(object)` forwards when the object is a `Gradient`.
- `GetHashCode()` is the hash of the **native pointer**: equal gradients have different hash codes (`893770144` vs `893771072` for two equal instances; two default gradients differ too). It is stable per instance. Consequently `Dictionary<Gradient, …>` with the default comparer finds an entry only by the same instance (verified: `ContainsKey(equalCopy)` is false). NowUI avoids this by supplying a reference comparer; the shim may implement `GetHashCode` as `RuntimeHelpers.GetHashCode(this)` to reproduce the identity-hash behaviour (recommended), and must not make it content-based if it wants to stay observably identical.
- No `==`/`!=` operators: `g1 == g2` is reference equality.

### 3.8 Not in the shim's scope

`GetColorKeys/GetAlphaKeys(Span)`, `SetColorKeys/SetAlphaKeys(ReadOnlySpan)`: trivial once the storage model exists; keep the argument-check exception text above. The finalizer, `m_Ptr`, `BindingsMarshaller` and `constantColor` are omitted.

---

## 4. Keyframe, WeightedMode, WrapMode

### 4.1 WeightedMode

`public enum WeightedMode { None = 0, In = 1, Out = 2, Both = In | Out }` (not `[Flags]`-attributed, but used as bits). NowUI ors/ands the values directly.

### 4.2 Keyframe [managed, verified]

`public struct Keyframe` with private fields in this declaration order: `float m_Time, m_Value, m_InTangent, m_OutTangent; int m_TangentMode; int m_WeightedMode; float m_InWeight, m_OutWeight;` (32 bytes). Public properties `time, value, inTangent, outTangent, inWeight, outWeight` (float get/set), `weightedMode` (`WeightedMode`, stored as the raw int: values `7` and `-1` round-trip), and the `[Obsolete]` `tangentMode` (int get/set; editor-only meaning, always round-trips unchanged through curves — verified `5` and `-3` survive `keys`, the ctor and `AddKey(Keyframe)`; internal alias `tangentModeInternal`).

Constructors:
- `Keyframe(time, value)`: tangents 0, weights 0, `weightedMode = None`, `tangentMode = 0`.
- `Keyframe(time, value, inTangent, outTangent)`: weights 0, `None`.
- `Keyframe(time, value, inTangent, outTangent, inWeight, outWeight)`: sets the weights and **`weightedMode = Both`**.
- `default(Keyframe)`: all zero.

No `Equals`/`GetHashCode`/operators.

### 4.3 WrapMode

`public enum WrapMode { Once = 1, Loop = 2, PingPong = 4, Default = 0, ClampForever = 8, Clamp = 1 }` (`Clamp` and `Once` are the same value). Curve-specific behaviour is in §5.7.

---

## 5. AnimationCurve

`public class AnimationCurve : IEquatable<AnimationCurve>` (not sealed; native-pointer wrapper with finalizer — omit both). Members:

```
public AnimationCurve(); public AnimationCurve(params Keyframe[] keys);
public float Evaluate(float time);
public Keyframe[] keys { get; set; }      public int length { get; }      public Keyframe this[int index] { get; }
public int AddKey(float time, float value); public int AddKey(Keyframe key);
public int MoveKey(int index, Keyframe key); public void RemoveKey(int index); public void ClearKeys();
public void GetKeys(Span<Keyframe> keys);   // ArgumentException("Destination array must be large enough to store the keys", "keys") when too small
public void SetKeys(ReadOnlySpan<Keyframe> keys);
public void SmoothTangents(int index, float weight);
public WrapMode preWrapMode { get; set; }   public WrapMode postWrapMode { get; set; }
public static AnimationCurve Constant(float timeStart, float timeEnd, float value);
public static AnimationCurve Linear(float timeStart, float valueStart, float timeEnd, float valueEnd);
public static AnimationCurve EaseInOut(float timeStart, float valueStart, float timeEnd, float valueEnd);
public void CopyFrom(AnimationCurve other);
public override bool Equals(object o); public bool Equals(AnimationCurve other); public override int GetHashCode();
```

### 5.1 Storage and key ordering [verified]

- Keys are stored verbatim (times and values are **not** quantised; weights and tangents are not normalised; `tangentMode` and out-of-range `weightedMode` ints survive).
- The `keys` setter, `SetKeys`, and the `params` constructor **stable-sort by time**; duplicate times are kept in the given order (`{(0,0),(0.5,1),(0.5,2),(1,0)}` keeps `1` before `2`; the reversed pair keeps `2` before `1`). `keys = null` and `new AnimationCurve((Keyframe[])null)` produce an empty curve. `ClearKeys()` empties it.
- `length` is the key count. `this[index]` throws `IndexOutOfRangeException` with message `"GetKey"` when out of range (also on an empty curve, index -1, etc.).
- The `keys` getter of an **empty** curve returns the same empty array instance on every call [verified]; for non-empty curves Unity marshals a new array (not verified, but NowUI sorts the returned array in place, so the shim **must** return a copy).
- Defaults: `new AnimationCurve()` has `length 0`, `preWrapMode = postWrapMode = ClampForever (8)`.

### 5.2 AddKey [verified]

- `AddKey(Keyframe key)`: if a key with the same `time` already exists, returns `-1` and changes nothing (the docs' "replaces" wording is wrong for 6000.4). Otherwise inserts the key **verbatim** (no tangent or weight changes, neighbours untouched) at its sorted position and returns that index. Whether the "same time" test uses an epsilon is unverified (exact equality was tested).
- `AddKey(float time, float value)` ("add key with smooth tangents"): same duplicate rule (`-1`, no change). Otherwise creates `Keyframe(time, value)` with `inWeight = outWeight = 1/3`, `weightedMode = None`, inserts it at index `i`, then applies `SmoothTangents(j, 0)` (§5.5) to `j = i-1, i, i+1` where those exist, and returns `i`. Verified: inserting `(0.5, 5)` into `Linear(0,0,1,1)` yields key0 `in = out = 10, outWeight = 1/3` (inWeight left at 0), key1 `in = out = 1, weights 1/3`, key2 `in = out = -8, inWeight = 1/3` (outWeight left 0). On an empty curve the new key has tangents 0 and both weights 1/3, index 0.

### 5.3 MoveKey / RemoveKey [verified]

- `MoveKey(index, key)`: `index` out of range → `IndexOutOfRangeException("MoveKey")`. If **another** key (not the one at `index`) has `key.time`, the key at `index` is **removed** and `-1` is returned — the curve loses a key (verified: 3 keys → 2). Otherwise the key at `index` is replaced by `key` (verbatim, including tangents/weights), the array is re-sorted and the new index is returned (`0` when the time is unchanged, `1` when moving `0 → 0.75` past a key at `0.5`).
- `RemoveKey(index)`: out of range → `IndexOutOfRangeException("RemoveKey")`; otherwise removes the key, neighbours untouched.

### 5.4 Evaluate — overall algorithm

Given `n = length`, keys `K[0..n-1]` sorted by time:

1. `n == 0` → `0f` for any `t`, including NaN [verified].
2. `n == 1` → `K[0].value` for any `t`, any wrap mode, including NaN [verified].
3. Otherwise apply the wrap mode (§5.7) to obtain `t'` (may be NaN/∞ for non-finite input).
4. Select the segment `(L, R) = (K[i], K[i+1])` with `i` = the largest index such that `K[i].time <= t'`, clamped to `[0, n-2]`. With duplicate times this picks the **last** duplicate as `L` when `t'` equals the duplicated time, so `Evaluate(X)` returns the last key at `X` (verified: `{(0.5,1),(0.5,2)}` → `2` at `0.5`; `{(0,0),(0.5,1),(0.5,2),(1,0)}` → `0.998816` at `0.49`, `2` at `0.5`, `1.997632` at `0.51`). A `t'` outside `[K[0].time, K[n-1].time)` only reaches this step for the wrapping modes (which fold it into the range); clamping modes take the constant-edge path described in §5.7 instead.
5. **Step:** if `L.outTangent` or `R.inTangent` is infinite: `+∞` on either side → return `L.value`; `−∞` (with no `+∞`) → return `R.value` [verified individually: `out = +∞` holds the left value up to but excluding `R.time`; `in = +∞` likewise; `out = −∞` or `in = −∞` return the right value at the midpoint; both `+∞` → left]. The mixed case `(+∞, −∞)` and the precedence between them are unverified. Only the segment's own two tangents matter (`L.inTangent`/`R.outTangent` infinite do not make a step). A NaN tangent falls through to the polynomial and yields NaN. Huge finite tangents are **not** steps (`out = 1e30` → `1.2499998e29 [0x6FC9F2C8]` at 0.5; `out = float.MaxValue` → `−∞`, both reproduced by §5.6).
6. **Weighted:** if `(L.weightedMode & Out) != 0` or `(R.weightedMode & In) != 0` (bit test on the raw int; `7` and `-1` count as weighted) → §5.8.
7. Otherwise → cubic Hermite, §5.6.

### 5.5 SmoothTangents(index, weight) [verified]

`index` out of range → `IndexOutOfRangeException("SmoothTangents")`. With one key nothing changes (tangents and weights untouched). Otherwise let `sL = (K[i].value - K[i-1].value) / (K[i].time - K[i-1].time)` if `i > 0` and `sR = (K[i+1].value - K[i].value) / (K[i+1].time - K[i].time)` if `i < n-1`; an end key uses its single available slope for both `sL` and `sR`. Then

```
tangent = 0.5f * (1 + weight) * sL + 0.5f * (1 - weight) * sR      // weight is NOT clamped
K[i].inTangent = K[i].outTangent = tangent
if (i > 0)     K[i].inWeight  = 1/3
if (i < n-1)   K[i].outWeight = 1/3
```

`weightedMode` and `tangentMode` are unchanged. Verified on keys `(0,0),(1,3),(3,0)` (`sL = 3, sR = -1.5`): weight `-1 → -1.5`, `0 → 0.75`, `0.25 → 1.3125`, `0.5 → 1.875`, `1 → 3`, `2 → 5.25`; on `(0,0),(1,1),(5,0)`: `0 → 0.375` (plain average, not chord- or length-weighted), `0.5 → 0.6875`, `1 → 1`, `-1 → -0.25`; end keys → `3` and `-1.5` regardless of weight. Duplicate-time neighbours (division by zero) are unverified.

### 5.6 Cubic Hermite segment [bit-exact, 22/22 samples]

For `t'` in segment `(L, R)` without step/weights:

```
dx = R.time - L.time;  if (dx < 1e-4f) dx = 1e-4f       // minimum segment length; pinned by a 1e-7-long segment (1e-5 and 1e-6 do not reproduce it)
len = 1f / dx;   d = R.value - L.value;   m1 = L.outTangent;   m2 = R.inTangent
a = (m1 + m2 - 2f * d * len) * (len * len)
b = (3f * d - (2f * m1 + m2) * dx) / dx / dx
c = m1
x = t' - L.time                                            // NOT clamped by the 1e-4 rule
value = x * (x * (x * a + b) + c) + L.value                // ((a*x + b)*x + c)*x + L.value gives the same bits on all samples
```

Every intermediate is a single-precision `float`, and the exact grouping above matters (e.g. `(m1 + m2 - 2*d*len) * len * len` or `(3*d*len - 2*m1 - m2) * len` differ in the last bit on some inputs). Verified samples: `EaseInOut(0,0,1,1)` at `0.3 → 0.21600002 [0x3E5D2F1C]`, at `0.25 → 0.15625`; keys `(1,2,out 0.5)`,`(3,-1,in -2)`: `1.1 → 2.032875 [0x40021AA0]`, `1.7 → 1.621125 [0x3FCF8106]`, `2.2 → 0.7279999 [0x3F3A5E34]`, `2.9 → -0.7953751 [0xBF4B9DB4]`; keys `(0.13,0.37,out 1.9)`,`(0.61,-0.42,in 0.25)`: `0.2 → 0.4193537 [0x3ED6B587]`, `0.37 → 0.07400003 [0x3D978D54]`, `0.55 → -0.385070324 [0xBEC527F0]`; keys `(-3,10,out -4)`,`(7,-20,in 3)`: `-1 → 0.8000002 [0x3F4CCCD0]`, `2 → -13.75`, `6 → -21.9499969 [0xC1AF9998]`; keys `(0,0)`,`(1e-7,1)` at `5e-8 → 7.4975003e-7 [0x3549426E]` (the 1e-4 clamp); keys at `1e6` and `1e6+1` at `1e6+0.25 → 0.15625`. Evaluating exactly at `t' = R.time` goes through the next segment (`x = 0`) and returns `R.value` exactly; just below a key the polynomial can overshoot (`0.99999994 → 1.00000012` on a curve through `(1,1)`).

`Linear(…)` curves therefore evaluate exactly linearly only when the segment's arithmetic happens to be exact (`0.25 → 0.25`, `0.3 → 0.3` for `Linear(0,0,1,1)`).

### 5.7 Wrap modes [verified]

Setters normalise the stored value: `Loop (2)` and `PingPong (4)` are stored as such, `Default (0)` is stored as `0`, and **every other value** (`Once/Clamp = 1`, `ClampForever = 8`, `3`, `16`, `-1`) is stored as `ClampForever (8)` and reads back as `8`. Pre and post are independent.

Given `t0 = K[0].time`, `t1 = K[n-1].time`, `range = t1 - t0`, and `mode = t < t0 ? preWrapMode : postWrapMode` (for `t0 <= t < t1` no wrapping is applied; `t >= t1` uses `postWrapMode`, so under Loop `Evaluate(t1)` returns the value at `t0`):

- **ClampForever (8)** (and everything normalised to it): outside the key range the curve behaves as a constant polynomial anchored at the edge key — `x = t - K[0].time` (pre) or `x = t - K[n-1].time` (post) evaluated with coefficients `(a, b, c) = (0, 0, 0)` and the edge value, i.e. `x * (x * (x * 0 + 0) + 0) + edgeValue`. For finite `t` this is the edge value exactly (`Linear(0,0,1,1)`: `-1 → 0`, `2 → 1`, `1 → 1`, `float.MaxValue → 1`, `-float.MaxValue → 0`), but for `t = ±∞` it is **NaN** (`0 · ∞`) [verified]; NaN `t` yields NaN. (A plain clamp of `t` would return the edge value for `±∞`; the shim must reproduce the NaN if it wants parity.)
- **Loop (2)** and **Default (0)** — Default evaluates exactly like Loop [verified at 15 sample times, `Linear(0,0,1,1)` with Default: `-0.25 → 0.75`, `1 → 0`, `1.25 → 0.25`, `3 → 0`]: `x = t - t0; x = x mod range` (result in `[0, range)`, i.e. `fmod` corrected into the positive range), `t' = t0 + x`. Verified: range `[2,4]`: `-0.5 → 0.75, 0 → 0, 4 → 0, 4.5 → 0.25, 8.5 → 0.25`; range `[0.5,1.5]` values `1..3`: `2 → 2, 2.5 → 1, 0 → 2, 0.25 → 2.5, -0.5 → 1`; single precision: `7.1 → 0.0999999046 [0x3DCCCCC0]`, `1000000.25 → 0.25`. `t = +∞` → NaN, `float.MaxValue` with range 1 → `0`.
- **PingPong (4)**: `x = t - t0; x = x mod (2·range)` (into `[0, 2·range)`); `if (x > range) x = 2·range - x`; `t' = t0 + x`. Verified: range `[0,1]`: `-2.25 → 0.25, -1 → 1, -0.25 → 0.25, 1.25 → 0.75, 2 → 0, 3 → 1, 3.5 → 0.5`; range `[2,4]`: `0 → 1, 0.5 → 0.75, 4 → 1, 5.5 → 0.25, 8 → 1, 8.5 → 0.75`; `7.1 → 0.9000001 [0x3F666668]` (= `2 - f32(7.1 - 6)`), `-∞` with pre = PingPong → `1`.

### 5.8 Weighted segments (cubic Bézier) [verified to ~1e-6]

```
dt = R.time - L.time
ow = (L.weightedMode & Out) != 0 ? L.outWeight : 1f/3     // the unweighted side always contributes 1/3
iw = (R.weightedMode & In)  != 0 ? R.inWeight  : 1f/3
P0 = (L.time, L.value)
P1 = (L.time + dt*ow, L.value + dt*ow*L.outTangent)
P2 = (R.time - dt*iw, R.value - dt*iw*R.inTangent)
P3 = (R.time, R.value)
find u ∈ [0,1] with Bx(u) = t' (cubic Bézier in time), return By(u)
```

With both weights exactly `1/3` this equals the Hermite of §5.6 mathematically, but Unity's root solve is a separate numeric path (`Linear(0,0,1,1)` with `Both`/`1/3` weights gives `0.6999999` at `0.7`; weights `0` with `Both` give `0.4999999` at `0.5`), so bit-exactness is not achievable and not required. Verified samples (`(0,0,tangents 0)`→`(1,1,tangents 0)` unless stated): `outWeight 0.9 (Out) / inWeight 0.1 (In)`: `0.1 → 0.004332156, 0.25 → 0.0295022521, 0.5 → 0.140823036, 0.75 → 0.409674466, 0.9 → 0.7522198`; same weights with tangents `1/1`: `0.25 → 0.250000983, 0.5 → 0.499999672`; `Both` weights `0.5`: `0.1 → 0.0146218846, 0.25 → 0.105892561, 0.75 → 0.894107461`; only left `Out 0.9`: `0.25 → 0.03131967, 0.5 → 0.1658127, 0.75 → 0.606860757`; only right `In 0.9`: `0.25 → 0.393139154, 0.5 → 0.83418715`; keys `(1,2,out 0.5, Both 0.2)`,`(3,-1,in -2, Both 0.6)`: `1.5 → 1.76174855, 2 → 0.957175, 2.5 → -0.00364238, 2.9 → -0.8000241`; tangents `5/-5` with `Both 0.5`: `0.25 → 1.31470263, 0.5 → 2.375, 0.75 → 2.10291743`. Weights outside `[0,1]` make `Bx` non-monotonic; Unity then returns one particular root (with `Both` weights `2`: `0.25 → 0.0064249346`, `0.5 → 0.964758039` — the largest of three roots, `0.75 → 0.9935751`; weights `-0.5`: `0.25 → 0.3313348`). Root choice in that regime is unspecified; NowUI clamps its weights to `[0.01, 1]`. A weighted segment with an infinite tangent is a step (§5.4 step check comes first).

### 5.9 Static helpers [managed]

- `Linear(timeStart, valueStart, timeEnd, valueEnd)`: if `timeStart == timeEnd` → a one-key curve `Keyframe(timeStart, valueStart)`. Else `tangent = (valueEnd - valueStart) / (timeEnd - timeStart)` and keys `{Keyframe(timeStart, valueStart, 0, tangent), Keyframe(timeEnd, valueEnd, tangent, 0)}` passed through the sorting constructor. Note that with `timeEnd < timeStart` the keys are re-sorted but the tangents stay with their keys, so the segment gets `out = 0`, `in = 0`: `Linear(1,0,0,1)` is an ease curve, not a line [verified].
- `EaseInOut(…)`: same degenerate rule; otherwise two keys with all tangents 0 (smoothstep: `0.25 → 0.15625`, `0.75 → 0.84375`).
- `Constant(timeStart, timeEnd, value)` = `Linear(timeStart, value, timeEnd, value)`.
- All produce weights 0, `weightedMode None`, `tangentMode 0`, wrap modes `ClampForever`.

### 5.10 Equality, hashing, CopyFrom [verified]

- `Equals(AnimationCurve)`: null → false; same reference → true; otherwise content: every key's `time, value, inTangent, outTangent, inWeight, outWeight, weightedMode` **and** both wrap modes must match; **`tangentMode` is ignored** (two `Linear` curves differing only in `tangentMode` are equal). `Equals(object)` requires the exact same runtime type.
- `GetHashCode()`: content-based (equal curves hash equal; `Dictionary` lookup by an equal copy succeeds), **ignores the wrap modes** (a curve differing only in `postWrapMode` hashes the same) but **includes** `tangentMode` (so `Equals` and `GetHashCode` are inconsistent in that one respect) and the weights. An empty curve hashes to `0`. The exact formula is native and unspecified; the shim may choose any content hash over the key fields that yields `0` for an empty curve.
- `CopyFrom(other)`: copies keys and both wrap modes; the copy `Equals` the source.
- No `==`/`!=` operators.

---

## 6. ColorUtility

`public partial class ColorUtility` (a class with static members only; not static, not sealed).

### 6.1 TryParseHtmlString(string htmlString, out Color color) [native in 6000.4; verified]

Behaviour (identical to the managed span overload that `master` adds for TextCore, which the shim may mirror):

1. Null or empty → `false`. The input is trimmed of leading/trailing whitespace (spaces, tabs and newlines verified).
2. If it starts with `#`: the rest must be 3, 4, 6 or 8 hexadecimal digits (`0-9a-fA-F`, ASCII only — full-width digits fail). 3/4 digits are expanded by doubling each digit (`#f00 → #ff0000`, `#f008 → #ff000088`, `#FF00 → (255,255,0,0)`). 6 digits → alpha `FF`; 8 digits → the last pair is alpha. Any other length (`#`, `#f`, `#ff`, `#FF000`, `#FF00000`, `#FF0000FFF`), a second `#`, or non-hex characters → `false`. No `0x` prefix, no `rgb()` syntax.
3. Otherwise the whole (trimmed) string is compared **case-insensitively** against this 23-entry table (`RED`, `Red`, `DarkBlue`, `DARKBLUE` all succeed):

   `red (ff0000) cyan (00ffff) blue (0000ff) darkblue (00008b) lightblue (add8e6) purple (800080) yellow (ffff00) lime (00ff00) fuchsia (ff00ff) white (ffffff) silver (c0c0c0) grey (808080) black (000000) orange (ffa500) brown (a52a2a) maroon (800000) green (008000) olive (808000) navy (000080) teal (008080) aqua (00ffff) magenta (ff00ff) transparent (00000000)`

   `gray`, `pink`, `clear`, `light blue` fail. Hex without `#` (`ff0000`, `f00`) fails.
4. The parsed `Color32` is converted to `Color` (`byte / 255f` per channel: `0x80 → 0.5019608 [0x3F008081]`).
5. **On failure `color` is `Color.white` `(1,1,1,1)`**, not `default`.

### 6.2 ToHtmlStringRGB / ToHtmlStringRGBA [managed]

Each channel is converted with `(byte)Mathf.Clamp(Mathf.RoundToInt(c * 255), 0, 255)` (`RoundToInt` = `(int)Math.Round(float)`, banker's rounding: `0.5 → 127.5 → 128 = "80"`, `0.49999 → "7F"`, `0.0019 → 0.4845 → "00"`, `0.00196 → 0.4998 → "00"`, `0.002 → 0.51 → "01"`), formatted with `string.Format("{0:X2}{1:X2}{2:X2}", …)` (upper-case, no `#`), the RGBA variant appending the alpha byte. Verified: `(1, 0.5, 0.25, 0.75) → "FF8040"` / `"FF8040BF"`; `(0.1,0.2,0.3,0.4) → "1A334C66"`. Non-finite inputs: NaN → `"00"`; on Mono `+∞` also gives `"00"` (`(int)` of an out-of-range double is `int.MinValue`, clamped to 0), `-∞ → "00"`, `2 → "FF"`, `-1 → "00"`. On .NET 9 the conversion saturates (`(int)+∞ = int.MaxValue → "FF"`); the shim should special-case non-finite values to `0` if it wants Mono parity (see §10.2). (The `Color → Color32` cast is different: it saturates first, so `+∞ → 255`, NaN → 0.)

---

## 7. LayerMask

`public struct LayerMask` (in 6000.4 **without** `[Serializable]` on the C# side and **without** a `ToString` override — `ToString()` returns `"UnityEngine.LayerMask"` [verified]; `master` later adds both). Single private `int m_Mask` (4 bytes).

- `public int value { get; set; }`; `implicit operator int(LayerMask)`; `implicit operator LayerMask(int)`. `default(LayerMask).value == 0`. No `Equals`/`GetHashCode` overrides (default `ValueType` behaviour: equal masks are `Equals`, hash derived from the field — the shim should override both on `m_Mask` for determinism; the Mono value-type hash is not specified).
- `static string LayerToName(int layer)`: the name of the layer, or `""` for an unnamed or out-of-range layer (`-1, 3, 6..31, 32, 33, 100, int.MinValue` all return `""`; no exception) [verified].
- `static int NameToLayer(string layerName)`: ordinal, **case-sensitive, untrimmed** match against the 32 layer names (`"default"`, `" Default"`, `"ui"` → `-1`); unknown → `-1`. Quirk: `""` and `null` return **3**, the first layer with an empty name [verified] — the shim's host table should reproduce this only if it keeps empty names for unused slots.
- `static int GetMask(params string[] layerNames)`: `null` array → `ArgumentNullException("layerNames")`; otherwise ORs `1 << NameToLayer(name)` for every name that resolves (`!= -1`), so `GetMask("Default","UI") == 33`, `GetMask("nope") == 0`, `GetMask() == 0`, and — because of the quirk — `GetMask(new string[]{null}) == 8`.
- Default project layer table [verified]: `0 Default, 1 TransparentFX, 2 Ignore Raycast, 3 "", 4 Water, 5 UI, 6 "", 7 "", 8..31 ""` (user layers). The shim exposes the table through a host service; NowUI only needs `LayerToName` for 0..31.

---

## 8. ExpressionEvaluator

`public class ExpressionEvaluator` in **`UnityEngine`** (runtime assembly `UnityEngine.CoreModule`, file `Runtime/Export/ExpressionEvaluator.cs`, `[MovedFrom(true, "UnityEditor", "UnityEditor")]` — it used to be editor-only, hence the `MovedFrom`). Fully managed; the shim reimplements it. Public surface: `static bool Evaluate<T>(string expression, out T value)`. (An internal `Expression` class, `Evaluate<T>(string, out T, out Expression delayed)`, `SetRandomState(uint)` and per-index evaluation with `L(a,b)`/`R(a,b)` exist for editor multi-object editing; NowUI does not use them and they are not needed.)

### 8.1 Algorithm (behavioural description)

1. `value = default`. A `null` expression throws `NullReferenceException` [verified].
2. **Whole-string fast path (`TryParse<T>`)**: replace every `,` with `.`; lower-case (invariant); if the string has ≥ 2 characters and the second-to-last is a digit, trim any trailing `f`, `d`, `l` characters (so `3f`, `2d`, `5l`, `1e3f`, `10l` parse; `1f5`, `12ff`, `sqrt(16)f` do not). An **empty** string returns `true` with `default` (`""` → `true, 0`; `"   "` → `false`). Then: for `float`/`double`, the literal `pi` (any case after lower-casing) is `Math.PI` (as `float` for `T = float`), otherwise `float.TryParse`/`double.TryParse` with `NumberStyles.Float` and the **invariant** culture (`1e3`, `1E3`, `.5`, `3.`, `1e-2` accepted; `Infinity`, `NaN`, `inf`, `∞`, full-width digits, `0x10`, `1_000` rejected); for `int`/`long`/`ulong` the integer `TryParse` with `NumberStyles.Integer` (no exponent, no decimals). Success returns immediately.
3. Otherwise the string is **tokenised**: trimmed; if the last character is an operator token it is dropped (`"1 +" → 1`, `"5*" → 5`, `"2^" → 2`, `"3--" → 3`); a leading `+=`, `-=`, `*=`, `/=` turns the rest into `x+(…)` etc. (which then fails as a variable-bearing expression). Characters are split at `(`, `)`, `,` and operator/function tokens; spaces separate tokens; everything else accumulates into a token. Recognised operators/functions with precedence (higher binds tighter) and arity: `+`,`-` (2, binary, left), `*`,`/`,`%` (3, left), `^` (5, **right**-associative), unary minus `_` (5, left, arity 1), `sqrt`,`sin`,`cos`,`tan`,`floor`,`ceil`,`round` (4, arity 1), `R` and `L` (4, arity 2, "delayed"; presence makes the expression fail in the single-value API). Function names are **case-sensitive lower-case** (`SQRT(4)`, `Sqrt(4)`, `r(1,2)` fail). A `-` that is the first token, or follows any operator/function/`(`/`,` (but not `)`), becomes unary; the unary conversion is not applied to the **last** token position (so `2^-2` fails while `2*-3 = -6` and `4/-2 = -2` work — see §8.2).
4. Tokens are converted to RPN with the shunting-yard rules (pop while the new operator is left-associative with precedence ≤ top, or right-associative with precedence < top; `)` pops to `(` and then pops a delayed function; `,` pops to `(`; unmatched `)` is ignored; unmatched `(` is popped at the end).
5. RPN evaluation in **`double`**: numbers are pushed as strings; each operator pops its arity of operands and parses them with the whole-string rule of step 2 (so `pi` works as an operand: `2*pi`, `sin(pi)`), computes, and **pushes the result formatted with `double.ToString(CultureInfo.InvariantCulture)`** — on Mono that is 15 significant digits (`1/3 → 0.333333333333333`, `2*pi → 6.28318530717959`, `9007199254740993+0 → 9007199254740990`), and a result that formats as `Infinity`/`NaN` fails to re-parse, so `1/0`, `0/0`, `-1/0` return `false`. Operations: `_ → -a`, `+ - * /`, `% → a % b` (C# remainder: `-7%3 = -1`), `^ → Math.Pow`, `sqrt → a <= 0 ? 0 : Math.Sqrt(a)` (`sqrt(-4) = 0`), `floor/ceil → Math.Floor/Ceiling`, `round → Math.Round` (banker's: `round(2.5) = 2`, `round(3.5) = 4`, `round(-2.5) = -2`), `sin/cos/tan → Math.*` (radians). Any operator with too few operands, or a final stack that does not contain exactly one parsable value, returns `false` (`2(3)`, `5 5`, `1 2`, `3 4 +`, `()`, `sqrt()`, `round(2.5,1)`, `1+()`, `%5`, `*5`, `--3`, `++3`, `2**3`, `2^^3`, `cos`, `2pi`, `1,5+1`, `1,000+1` all fail).
6. **Conversion to `T`**: the `double` result is cast with a plain C# cast: `float` (`1e39 → +∞` accepted as `true`), `int` and `long` (truncation toward zero: `1.7 → 1`, `-1.7 → -1`, `7/2 → 3`, `0.9999 → 0`, `pi → 3`), `ulong` (negative results clamp to `0` first: `-5 → 0`). Out-of-range `int`/`long` casts are unchecked: on Mono x64 they produce `int.MinValue`/`long.MinValue` (`3000000000 → -2147483648`, `2^31 → -2147483648`, `2147483648 → -2147483648`, `1e10 → -2147483648`, `9223372036854775808 → long.MinValue`, `2^63 → long.MinValue`, `1e19 → long.MinValue`); .NET 9 saturates instead (§10.2). Any other `T` (`short`, `decimal`, `string`, …) returns `false` with `default`.

### 8.2 Verified result table (T = double unless noted)

`1+2*3 → 7`; `(1+2)*3 → 9`; `2^3^2 → 512`; `-2^2 → -4`; `2^-2 → false`; `2^3^-1 → false`; `2*-3 → -6`; `2*+3 → false`; `+3 → 3`; `1--3 → 4`; `1 - -2 → 3`; `2 -3 → -1`; `- 3 → -3`; `-(-3) → 3`; `(-3)^2 → 9`; `10%3 → 1`; `-7%3 → -1`; `8%3%2 → 0`; `2*3%4 → 2`; `1/2/2 → 0.25`; `1-2-3 → -4`; `2^0.5^2 → 1.18920711500272`; `2 ^ 0.5 → 1.4142135623731`; `7/2 → 3.5`; `sqrt(16) → 4`; `sqrt 16 → 4` (parentheses optional for unary functions); `sqrt(2)^2 → 2`; `floor(-2.7) → -3`; `ceil(2.1) → 3`; `tan(1) → 1.5574077246549`; `sin(pi) → 1.22464679914735E-16`; `pi → 3.1415926535897931` (fast path, full precision); `PI`, `Pi` → same; `2*pi`, `pi*2 → 6.28318530717959`; `e → false`; `1.5e2+1 → 151`; `1,5 → 1.5`; `1,000 → 1`; `3,5f → 3.5`; `0.5f*2 → 1`; `5f+1 → 6`; `2f^2 → 4`; `1e400 → false` (Mono; overflow parse failure); `123456789012345678901234567890 → 1.2345678901234568E+29`; `9007199254740993 → 9007199254740992` (fast path, `double`); `1+2) → 3`; `(1+2 → false`; `((2)) → 2`; `\t1+1\n → 2`; `1 + 1  → 2`; `1 +2 → 3`; `x`, `-x`, `x+1`, `v`, `f`, `#`, `R(1,2)`, `L(0,1)`, `+=5` → `false`; `true`, `nan`, `NaN`, `Infinity`, `inf`, `abc`, `12abc`, `1e`, `1ee3`, `1.2.3`, `1..2` → `false`. Float: `1/3 → 0.333333343 [0x3EAAAAAB]`, `3.4028236e38 → +∞ (true)`, `1e38*10 → +∞ (true)`, `"" → true 0`. Int: `2^31-1 → 2147483647`, `round(2.5) → 2`, `1,5 → false` (the comma-to-dot rewrite only helps when the rewritten literal parses as the target type; `int.TryParse("1.5")` fails, and the tokeniser then splits `1,5` into `1 , 5`, which leaves two operands on the stack), `1.7 → 1`, `5f/5l/5d → 5`. Long: `9007199254740993 → 9007199254740993` (integer fast path is exact), `2^62 → 4611686018427389952` (through `double`), `5.9 → 5`. Current-culture independence: under `de-DE`, `2.5*2 → 5`, `2,5*2 → false`, `1,5+1.5 → false` (comma is a token separator, not a decimal mark, once the fast path fails).

---

## 9. TouchScreenKeyboard and TouchScreenKeyboardType

`public enum TouchScreenKeyboardType { Default = 0, ASCIICapable = 1, NumbersAndPunctuation = 2, URL = 3, NumberPad = 4, PhonePad = 5, NamePhonePad = 6, EmailAddress = 7, [Obsolete] NintendoNetworkAccount = 8, Social = 9, Search = 10, DecimalPad = 11, OneTimeCode = 12 }`.

`public partial class TouchScreenKeyboard` (not sealed; finalizer destroys the native object). Nested `enum Status { Visible = 0, Done = 1, Canceled = 2, LostFocus = 3 }`, `enum InputFieldAppearance { Customizable = 0, AlwaysVisible = 1, AlwaysHidden = 2 }`, `enum InPlaceEditingBehavior { Auto = 0, AlwaysAllowed = 1, AlwaysDisallowed = 2 }` (6000.4 already has `InPlaceEditingBehavior`/`inPlaceEditingBehavior`).

Members and semantics:
- `static bool isSupported`: `true` only when `Application.platform` is iOS, tvOS, Android, Switch/Switch2, PS4/PS5, WebGL, Xbox (GameCore), VisionOS or WSA; `false` elsewhere, including every editor [verified false in the Windows editor]. For the browser build the host decides (WebGL players report `true`; M1 should report `false`).
- `static TouchScreenKeyboard Open(string text, TouchScreenKeyboardType keyboardType = Default, bool autocorrection = true, bool multiline = false, bool secure = false, bool alert = false, string textPlaceholder = "", int characterLimit = 0)` — implemented as eight explicit overloads (no optional parameters): `(text)`, `(text, type)`, `(…, autocorrection)`, `(…, multiline)`, `(…, secure)`, `(…, alert)`, `(…, textPlaceholder)`, `(…, characterLimit)`; all forward to the public constructor `TouchScreenKeyboard(text, keyboardType, autocorrection, multiline, secure, alert, textPlaceholder, characterLimit)`. `Open` always returns a non-null object, even where unsupported [verified: in the editor the object exists but every instance member except `selection` throws `NullReferenceException` because the native handle is null].
- Instance: `string text { get; set; }`, `bool active { get; set; }` (true while the keyboard is visible or sliding in; setting `false` closes it), `Status status { get; }`, `int characterLimit { get; set; }`, `bool canGetSelection/canSetSelection { get; }`, `RangeInt selection { get; set; }` (setter: if `text` is null/empty sets `(0,0)`; else throws `ArgumentOutOfRangeException(nameof(selection), "Selection is out of range.")` when `start < 0 || length < 0 || start + length > text.Length`), `TouchScreenKeyboardType type { get; }`, `int targetDisplay` (always `0`, setter no-op), obsolete `done`/`wasCanceled`.
- Static: `bool hideInput { get; set; }`, `InputFieldAppearance inputFieldAppearance { get; }`, `Rect area { get; }` (`(0,0,0,0)` when unsupported), `bool visible { get; }`, `InPlaceEditingBehavior inPlaceEditingBehavior { get; set; } = Auto`, `bool isInPlaceEditingAllowed { get; }` (Auto → platform query; verified `false` in the editor).
- Nested `Android` class with two `[Obsolete(…, true)]` properties that throw `NotSupportedException` — omit.

NowUI's contract: it opens a keyboard only when `isSupported`, mirrors `text` into the field while `status == Visible`, treats `status == Done` as submit (single-line) or final text (multi-line), and on any non-Visible status clears focus and sets `active = false` before dropping the reference. The shim's host interface therefore needs: `isSupported`, open(text, type, autocorrection, multiline) → handle, `text` get/set, `status`, `active` get/set.

---

## 10. Notes: unverified items, platform divergences, hazards

### 10.1 Could not verify / not fully pinned

- `Gradient`: behaviour of undefined `mode` values (3..255); Fixed-mode selection when `tq` equals a duplicated key time exactly; whether the `0/0` guard in §3.3 is `u = 0` or a division by `max(denom, 1)` (indistinguishable); PerceptualBlend for negative components and the exact source of the R-channel deviation (§3.6); whether a 9-key colour array combined with an 8-key alpha array applies the alpha array (each array appears to be validated independently: a 2-key colour array with an empty alpha array applied the colours only).
- `AnimationCurve`: the mixed `(+∞, −∞)` step case; the epsilon (if any) used by `AddKey`/`MoveKey` for "a key already exists at this time"; `SmoothTangents` with duplicate-time neighbours; the exact `GetHashCode` formula; root selection for non-monotonic weighted segments; whether `keys` on a non-empty curve returns a fresh array (assumed yes; the shim must copy).
- `LayerMask.GetHashCode` (Mono `ValueType` default, e.g. `LayerMask(0) → 837990888`, `(1) → 837990889`, `(-1) → -837990889` — value-XOR pattern, not worth reproducing).
- `TouchScreenKeyboard` instance behaviour on a supporting platform (status transitions, `text` updates) — host-defined.

### 10.2 Mono (Unity) vs .NET 9 divergences the shim must decide on

- **`double → int/long` casts of out-of-range values**: Mono returns `int.MinValue`/`long.MinValue`; .NET 9 saturates. Affects `ExpressionEvaluator.Evaluate<int/long>` (`3000000000`, `2^31`, `1e10`) and the non-finite branches of `ColorUtility.ToHtmlString*` and `Mathf.RoundToInt`. NowUI's `NowNumericExpression` only uses the `double` overload and rejects non-finite results, and `TryEvaluateLong` never calls Unity, so either choice is safe for NowUI; to keep Unity parity, convert through a helper that returns `int.MinValue` for NaN/out-of-range.
- **`double.ToString()` in the RPN evaluator**: Mono formats 15 significant digits; .NET Core 3.0+ formats the shortest round-trip string (`1/3` would become `0.3333333333333333`, `9007199254740993+0 → 9007199254740992`). Use `ToString("G15", InvariantCulture)` for parity, or accept the (more accurate) .NET behaviour and document it.
- **`double.TryParse` of overflowing literals** (`1e400`): Mono → `false`; .NET Core 3.0+ → `true` with `±Infinity`.
- `float.TryParse("1e39")` → `+∞`/`true` on both.

### 10.3 Places where the docs and the behaviour differ

- `AnimationCurve.AddKey(Keyframe)` docs imply replacement of an existing key at the same time; 6000.4 returns `-1` and leaves the curve unchanged.
- `MoveKey` with a time collision **removes** the moved key (docs only mention the `-1` return).
- `WrapMode.Default` on a curve evaluates as `Loop`; `Once`/`Clamp` are silently stored as `ClampForever`.
- `Gradient` key times are 16-bit fixed point; arrays with more than 8 (or fewer than 1) keys are silently ignored; a 1-key array becomes two keys at 0 and 1.
- `Gradient.GetHashCode` is identity-based while `Equals` is value-based.
- `ColorUtility.TryParseHtmlString` returns **white** on failure, trims, and matches names case-insensitively (docs only list lower-case names).
- `LayerMask.NameToLayer("")`/`(null)` returns the first unnamed layer instead of `-1`.
- `ExpressionEvaluator` accepts `pi`, `f/d/l` suffixes, comma decimals in a bare literal, a dangling trailing operator, and unmatched `)`; rejects `2^-2`; truncates intermediates to 15 significant digits (Mono).
- `AnimationCurve.Linear` with `timeEnd < timeStart` is not linear.

### 10.4 NowUI hazards spotted while reading the call sites (not shim issues)

- `NowValueControls` gradient editor never limits colour/alpha keys to 8; a ninth key makes `Gradient.SetKeys` a silent no-op in Unity (and in a faithful shim), so the applied gradient stops following the editor. `Normalize` only guarantees `≥ 1` key.
- `NowGradientRampCache` and `NowValueControls` both convert `Evaluate` results with the `Color → Color32` cast (saturating); HDR ramps are clipped there, not in `Gradient`.
- `NowAnimationCurveField` writes `float.PositiveInfinity` tangents for steps and clamps weights to `[0.01, 1]`, so it stays inside the verified regimes of §5.4/§5.8.
- `NowRichTextParser` lower-cases names before `TryParseHtmlString`; harmless (Unity is case-insensitive) but unnecessary. `NowMarkupStyles` prefixes `#` when absent, so bare hex is accepted there but not in Unity's own API.

**Verification environment.** The empirical values above come from a single throw-away NUnit EditMode test (`NowGradientCurveProbeTests`, since deleted along with its `.meta`) executed once through `Tools/NowUI-Harness.ps1 -Mode EditMode` on Unity 6000.4.0f1 (Windows 11 x64, Mono scripting backend), plus an offline .NET 9 console program that brute-forced candidate single-precision formulas against the captured bit patterns and a double-precision Oklab reference. Other Unity versions/backends (IL2CPP, ARM, Burst FTZ) may differ in last-ulp results of the native paths.
