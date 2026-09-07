# Unity value-type semantics spec (for clean-room shim)

**Scope and provenance.** This document specifies the observable behaviour of the `UnityEngine` value types and `Mathf` that NowUI's engine-free build must reproduce in its "UnityEngine-compatible shim". It was derived from (a) reading the C# in the `Unity-Technologies/UnityCsReference` repository (`Runtime/Export/Math/*.cs`, `Runtime/Export/Geometry/{Rect,RectInt,Bounds}.cs`, `Runtime/Export/Math/Math.bindings.cs`, `Runtime/Export/Scripting/UnityEngineObject.bindings.cs`, `Runtime/Export/Graphics/GraphicsEnums.cs`, master branch as of 2026-09-02), (b) the Unity Scripting API docs, and (c) an empirical probe run against **Unity 6000.4.0f1 (Windows x64 editor, Mono)** for members whose bodies live in native code. Values marked **[verified]** come from that probe and are given with IEEE-754 bit patterns in brackets where exactness matters.

**Licensing note.** UnityCsReference is published under the Unity Reference-Only License; it may be read to understand behaviour but must not be copied. This spec therefore describes semantics and formulas so that an implementer can write original code. No Unity native (C++) source was consulted; the mirrors of leaked engine source that a web search surfaces were deliberately not opened. Native behaviour is described from documentation plus black-box measurement.

**Notation.** `f32(expr)` means evaluate in single precision. "bits `0x…`" is the IEEE-754 single bit pattern. "kEps" refers to the per-type epsilon constant. All arithmetic in these structs is `float` unless stated; C# evaluates `float` expressions in single precision on all Unity backends (no extended precision), so the shim should keep every intermediate in `float` and preserve operation order where a formula is given.

---

## 0. Cross-cutting conventions

These apply to every struct below unless a section says otherwise.

| Convention | Detail |
|---|---|
| Struct kind | All are `[Serializable] public struct`, sequential layout, fields are public `float`/`int` (except Rect, RectInt, Bounds, Vector2Int, Vector3Int, Color32 which keep private fields behind properties). `Mathf` is a `public partial struct` (a value type, not a static class), with only static members. |
| Interfaces | Every struct implements `IEquatable<T>` and `IFormattable`. |
| `in` overloads | Unity ships a by-value and an `in`-parameter twin of most static methods (`Lerp(Vector2,Vector2,float)` and `Lerp(in Vector2,in Vector2,float)`, `Equals(T)` and `Equals(in T)`, etc.). They are behaviourally identical. C# overload resolution prefers the by-value form unless the caller writes `in x`, so the shim needs the `in` twins only for source compatibility with callers that use `in`; include them if NowUI does. |
| `readonly` members | Getters and pure instance methods are `readonly`; mutators (`Set`, `Normalize()`, `Scale(v)`, setters) are not. |
| `ToString()` family | `ToString()` → `ToString(null, null)`; `ToString(string format)` → `ToString(format, null)`; `ToString(string format, IFormatProvider p)`: if `format` is null/empty it is replaced by the type's default format (table below); if `p` is null it is replaced by `CultureInfo.InvariantCulture.NumberFormat`; each component is formatted with `component.ToString(format, p)` and spliced into a fixed template. Integer types (Color32, Vector2Int, Vector3Int, RectInt) have **no** default format (null passes through to `int.ToString(null, p)`, i.e. `"G"`). |
| Default numeric format | Vector2/3/4: `"F2"`. Rect: `"F2"`. Bounds: `"F2"` (passed on to Vector3). Color: `"F3"`. Quaternion: `"F5"`. Matrix4x4: `"F5"`. |
| `GetHashCode` | Built from component hash codes with shifts/xors (exact formulas per type). `float.GetHashCode()` on Mono/.NET returns the raw IEEE bits (with ±0 and all NaNs normalised in .NET Core; the probe confirms raw-bit behaviour, e.g. `new Vector2(1,2).GetHashCode() == 1065353216 == 0x3F800000`). |
| `==` tolerance vs `Equals` | The float vector types use a squared-distance tolerance in `operator==` but **exact component equality** in `Equals`. Two different exactness styles exist: Vector2/3/4, Bounds, Matrix4x4 use C# `==` on floats (NaN ≠ NaN); Color, Rect, Quaternion use `float.Equals` (NaN.Equals(NaN) is **true**). [verified: `new Vector2(NaN,0).Equals(itself)` = false; `new Color(NaN,0,0,0).Equals(itself)` = true; same for Rect and Quaternion.] |
| `!=` | Always the logical negation of the `==` expression (so `!=` returns true when NaN is involved, `==` returns false). |
| Statics like `zero`, `one`, `up` | Exposed as static **properties** returning a private `static readonly` instance (Vector2/3/4, Vector2Int/3Int, Rect, RectInt, Matrix4x4, Quaternion) or constructing a new value each call (Color presets). Behaviourally indistinguishable; either is fine. |
| Indexer errors | `IndexOutOfRangeException` with the exact messages listed per type. |
| `Time.deltaTime` | The `SmoothDamp` overloads that omit `deltaTime` read `UnityEngine.Time.deltaTime`; the shim must provide a `Time.deltaTime` source (any value; NowUI decides). |
| Obsolete members | Listed per type for completeness; the shim may omit them unless NowUI uses them. |

---

## 1. Vector2

`public partial struct Vector2 : IEquatable<Vector2>, IFormattable`

**Constants.** `public const float kEpsilon = 0.00001F;` `public const float kEpsilonNormalSqrt = 1e-15f;`

**Fields.** `public float x, y;` (declared in that order).

**Constructor.** `Vector2(float x, float y)`.

**Indexer** `float this[int index]`: 0→x, 1→y; any other index throws `IndexOutOfRangeException("Invalid Vector2 index!")` (get and set).

**Instance members**
- `Set(float newX, float newY)` – assigns both.
- `Scale(Vector2 scale)` – `x *= scale.x; y *= scale.y`.
- `Normalize()` – `mag = magnitude; if (mag > kEpsilon) { x /= mag; y /= mag; } else { x = 0; y = 0; }`.
- `normalized` (get) – returns `Normalize(this)` (static form below).
- `magnitude` (get) – `f32(Math.Sqrt(x*x + y*y))` (double sqrt of the float sum, cast to float).
- `sqrMagnitude` (get) – `x*x + y*y`.
- `SqrMagnitude()` – same as `sqrMagnitude` (undocumented legacy).
- `GetHashCode()` – `x.GetHashCode() ^ (y.GetHashCode() << 2)`.
- `Equals(object other)` – `other is Vector2 v && Equals(v)`.
- `Equals(Vector2 other)` – `x == other.x && y == other.y` (exact, NaN-unequal).
- `ToString(...)` – template `"({0}, {1})"`, default format `"F2"`. [verified: `new Vector3(1,2,3).ToString()` = `"(1.00, 2.00, 3.00)"`.]

**Static members**
- `zero (0,0)`, `one (1,1)`, `up (0,1)`, `down (0,-1)`, `left (-1,0)`, `right (1,0)`, `positiveInfinity (+∞,+∞)`, `negativeInfinity (−∞,−∞)`.
- `Lerp(a, b, t)` – `t = Clamp01(t)`; result component = `a + (b - a) * t` per component (note the form `a + (b-a)*t`, not `a*(1-t)+b*t`).
- `LerpUnclamped(a, b, t)` – same without clamping.
- `MoveTowards(current, target, maxDistanceDelta)` – `to = target - current` (componentwise floats); `sqDist = to.x² + to.y²`; **if `sqDist == 0` or (`maxDistanceDelta >= 0` and `sqDist <= maxDistanceDelta²`) return `target`**; else `dist = f32(Math.Sqrt(sqDist))` and result = `current + to / dist * maxDistanceDelta` per component (evaluate as `current.x + to.x / dist * maxDistanceDelta`). A negative `maxDistanceDelta` moves away from the target. [verified: `MoveTowards(0,(10,0),-3)` = `(-3,0)`.]
- `Scale(a, b)` – componentwise product.
- `Normalize(value)` – `mag = value.magnitude; mag > kEpsilon ? value/mag (componentwise) : zero`.
- `Reflect(inDirection, inNormal)` – `factor = -2 * Dot(inNormal, inDirection)`; result = `factor * inNormal + inDirection` per component (`factor * n.x + d.x`). Normal is **not** normalised.
- `Perpendicular(inDirection)` – `(-inDirection.y, inDirection.x)` (90° counter-clockwise).
- `Dot(lhs, rhs)` – `lhs.x*rhs.x + lhs.y*rhs.y`.
- `Angle(from, to)` – `denominator = from.sqrMagnitude * to.sqrMagnitude` (float); **if `denominator < kEpsilonNormalSqrt * kEpsilonNormalSqrt` (i.e. < 1e-30) return 0**; `denominator = f32(Math.Sqrt(denominator))`; `dot = Clamp(Dot(from,to) / denominator, -1, 1)`; return `f32(Math.Acos(dot)) * Rad2Deg`. (Note: Vector2 compares the *product of squared magnitudes* against 1e-30; Vector3 takes the square root first and compares against 1e-15 — mathematically equal, different underflow behaviour.)
- `SignedAngle(from, to)` – `Angle(from,to) * Sign(from.x*to.y - from.y*to.x)`; `Mathf.Sign(0) == 1`, so collinear vectors give `+angle`.
- `Distance(a, b)` – `f32(Math.Sqrt(dx*dx + dy*dy))` with `dx = a.x-b.x` etc.
- `ClampMagnitude(vector, maxLength)` – `sqr = vector.sqrMagnitude; if (sqr > maxLength*maxLength) { mag = f32(Math.Sqrt(sqr)); nx = vector.x/mag; ny = vector.y/mag; return (nx*maxLength, ny*maxLength); } return vector;` The normalised components must be materialised as `float` locals before multiplying (Unity comments that this forces single precision). A negative `maxLength` flips the vector. [verified: `ClampMagnitude((3,4),-1)` = `(-0.6,-0.8)`.]
- `SqrMagnitude(a)` – `a.sqrMagnitude`.
- `Min(lhs, rhs)` / `Max(lhs, rhs)` – componentwise `Mathf.Min`/`Mathf.Max` (see Mathf for NaN ordering).
- `SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime)` – full algorithm (all floats):
  1. `smoothTime = Max(0.0001, smoothTime)`; `omega = 2 / smoothTime`; `x = omega * deltaTime`; `exp = 1 / (1 + x + 0.48*x*x + 0.235*x*x*x)`.
  2. `change = current - target` (per component).
  3. `maxChange = maxSpeed * smoothTime`; `maxChangeSq = maxChange*maxChange`; `sqDist = change.x² + change.y²`; if `sqDist > maxChangeSq`: `mag = f32(Math.Sqrt(sqDist))`; `change = change / mag * maxChange` (evaluate `change.x / mag * maxChange`).
  4. `target' = current - change`.
  5. `temp = (currentVelocity + omega * change) * deltaTime` (per component).
  6. `currentVelocity = (currentVelocity - omega * temp) * exp`.
  7. `output = target' + (change + temp) * exp`.
  8. Overshoot guard: `origMinusCurrent = originalTarget - current`; `outMinusOrig = output - originalTarget`; **if `Dot(origMinusCurrent, outMinusOrig) > 0`** then `output = originalTarget` and `currentVelocity = (output - originalTarget) / deltaTime` (which is zero, or NaN when deltaTime == 0 — there is **no** deltaTime guard in the vector versions, unlike `Mathf.SmoothDamp`).
  Overloads: `(…, smoothTime, maxSpeed)` uses `Time.deltaTime`; `(…, smoothTime)` uses `maxSpeed = Mathf.Infinity` and `Time.deltaTime`. [verified: `SmoothDamp(0,(10,0), ref 0, 0.3, ∞, 0.016)` = `(0.05165842, 0)`, velocity `(6.392509, 0)`; overshoot case `current 0, target (1,0), vel (100,0), 0.3, ∞, 0.5` → `(1,0)`, vel `(0,0)`.]

**Operators**
- `+`, `-` (binary), unary `-`, `*` (Vector2, Vector2) **componentwise**, `/` (Vector2, Vector2) **componentwise**, `*` (Vector2, float), `*` (float, Vector2), `/` (Vector2, float). (Vector3/Vector4 do **not** have vector×vector or vector÷vector operators.)
- `==`: `d = lhs - rhs` per component; `sqrmag = dx*dx + dy*dy`; return `sqrmag < kEpsilon * kEpsilon` (the product is a compile-time float constant ≈ 1e-10). Returns false if any NaN. [verified: `zero == (1e-6,0)` true; `zero == (1e-5,0)` false.]
- `!=`: `!(sqrmag < kEpsilon*kEpsilon)`.

**Conversions (declared on Vector2)**
- `implicit Vector2(Vector3 v)` → `(v.x, v.y)` (drops z).
- `implicit Vector3(Vector2 v)` → `(v.x, v.y, 0)`.

---

## 2. Vector3

`public partial struct Vector3 : IEquatable<Vector3>, IFormattable`

**Constants.** `kEpsilon = 0.00001F`, `kEpsilonNormalSqrt = 1e-15F`.

**Fields.** `public float x, y, z;`

**Constructors.** `Vector3(float x, float y, float z)`; `Vector3(float x, float y)` sets `z = 0`.

**Indexer.** 0/1/2 → x/y/z; else `IndexOutOfRangeException("Invalid Vector3 index!")`.

**Instance members** – as Vector2 with a z term: `Set(x,y,z)`, `Scale(Vector3)`, `Normalize()` (threshold `mag > kEpsilon`, else all zero), `normalized`, `magnitude = f32(Math.Sqrt(x*x+y*y+z*z))`, `sqrMagnitude`, `GetHashCode() = x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2)` [verified `(1,2,3)` → 797966336], `Equals(object)`, `Equals(Vector3)` (exact `==` per component), `ToString` template `"({0}, {1}, {2})"` default `"F2"`.

**Static members**
- `zero, one, up (0,1,0), down (0,-1,0), left (-1,0,0), right (1,0,0), forward (0,0,1), back (0,0,-1), positiveInfinity, negativeInfinity`.
- `Lerp`, `LerpUnclamped`, `MoveTowards`, `Scale`, `Normalize`, `Reflect`, `Dot`, `Distance`, `ClampMagnitude`, `Magnitude(v)`, `SqrMagnitude(v)`, `Min`, `Max`, `SmoothDamp` (three overloads) – identical algorithms to Vector2 extended to three components (SmoothDamp uses the 3-component dot product in the overshoot test; no deltaTime guard).
- `Cross(lhs, rhs)` – `(lhs.y*rhs.z - lhs.z*rhs.y, lhs.z*rhs.x - lhs.x*rhs.z, lhs.x*rhs.y - lhs.y*rhs.x)`.
- `Project(vector, onNormal)` – `sqrMag = Dot(onNormal, onNormal)`; **if `sqrMag < Mathf.Epsilon` return `zero`**; else `k = Dot(vector, onNormal) / sqrMag`; return `onNormal * k` per component (`onNormal.x * k`).
- `ProjectOnPlane(vector, planeNormal)` – same guard but returns `vector` unchanged when `sqrMag < Mathf.Epsilon`; else `vector - planeNormal * k` per component.
- `Angle(from, to)` – `denominator = f32(Math.Sqrt(from.sqrMagnitude * to.sqrMagnitude))`; **if `denominator < kEpsilonNormalSqrt` (1e-15) return 0**; `dot = Clamp(Dot(from,to)/denominator, -1, 1)`; return `f32(Math.Acos(dot)) * Rad2Deg`.
- `SignedAngle(from, to, axis)` – `Angle(from,to) * Sign(Dot(axis, Cross(from,to)))`, with the cross product expanded inline as floats; `Sign(0) = 1`.
- `Slerp(a, b, t)`, `SlerpUnclamped(a, b, t)` – **native**. `Slerp` clamps t to [0,1]. Behaviour [verified]: interpolates *direction* along the great circle and *magnitude* linearly: `Slerp(right*2, up*4, 0.5)` = `(2.1213203, 2.1213203, 0)` (magnitude 3); if either input has (near-)zero magnitude the result is the plain `Lerp` (`Slerp(zero, up, 0.5)` = `(0,0.5,0)`); if the directions are (near-)identical the result is `Lerp` (`Slerp(right, right*3, 0.5)` = `(2,0,0)`); if the directions are (near-)opposite, Unity rotates `a` about an arbitrary perpendicular axis by `π·t`: `Slerp(right, left, 0.5)` = `(≈0, 0, -1)` and `Slerp(up, down, 0.5)` = `(0, ≈0, -1)`. The perpendicular axis is consistent with the rule "if `|dir.z| > 1/√2` use `normalize(0, -dir.z, dir.y)` else use `normalize(-dir.y, dir.x, 0)`" (for `right` that is `+y`, giving `-z` at the midpoint; for `up` it is `-x`, also giving `-z`). `SlerpUnclamped(right, up, 1.5)` = `(-0.70710677, 0.70710677, 0)`, `(…, -0.5)` = `(0.70710677, -0.70710677, 0)` (extrapolates along the circle). Thresholds ("near") are not verified; assume `1e-5`-class comparisons on the normalised dot product.
- `RotateTowards(current, target, maxRadiansDelta, maxMagnitudeDelta)` – native; rotates the direction by at most `maxRadiansDelta` radians toward `target` and moves the magnitude by at most `maxMagnitudeDelta`. [verified: `RotateTowards(right, up, 0.5, 0)` = `(0.87758255, 0.47942555, 0)` = `(cos 0.5, sin 0.5, 0)`; `(right, up*3, 0.5, 1)` = `(1.7551651, 0.9588511, 0)` (magnitude 2); `(right, left, 0.5, 0)` = `(0.87758255, 0, -0.47942555)` (antiparallel fallback rotates toward `-z`, same axis rule as Slerp).]
- `OrthoNormalize(ref normal, ref tangent)` / `(ref normal, ref tangent, ref binormal)` – native Gram–Schmidt: normalises `normal`, makes `tangent` orthogonal to it and unit length, then `binormal` orthogonal to both. [verified: `((2,0,0),(1,1,0))` → `(1,0,0),(0,1,0)`; three-vector form with `(1,1,1)` → `(0,0,1)`.]

**Operators.** `+`, binary `-`, unary `-`, `*` (Vector3,float), `*` (float,Vector3), `/` (Vector3,float). **No** componentwise `*`/`/` operators. `==`/`!=` as Vector2 with three terms (`sqrmag < kEpsilon*kEpsilon`).

**Conversions.** Declared elsewhere: Vector2↔Vector3 on Vector2; Vector3↔Vector4 on Vector4; Vector3Int→Vector3 on Vector3Int.

**Obsolete.** `fwd` (= forward), `AngleBetween(from,to)` (radians: `f32(Math.Acos(Clamp(Dot(from.normalized, to.normalized), -1, 1)))`), `Exclude(excludeThis, fromThat)` = `ProjectOnPlane(fromThat, excludeThis)`.

---

## 3. Vector4

`public partial struct Vector4 : IEquatable<Vector4>, IFormattable`

**Constant.** `kEpsilon = 0.00001F` (no `kEpsilonNormalSqrt`).

**Fields.** `public float x, y, z, w;`

**Constructors.** `(x,y,z,w)`; `(x,y,z)` → `w = 0`; `(x,y)` → `z = w = 0`.

**Indexer.** 0..3 → x,y,z,w; else `IndexOutOfRangeException("Invalid Vector4 index!")`.

**Instance members.** `Set(x,y,z,w)`, `Scale(Vector4)`, `Normalize()` (`mag = Magnitude(this)`; `> kEpsilon` divide else zero all four), `normalized`, `magnitude = f32(Math.Sqrt(Dot(this,this)))`, `sqrMagnitude = Dot(this,this)`, `SqrMagnitude()`, `GetHashCode() = x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1)` [verified `(1,2,3,4)` → 265289728], `Equals(object)`, `Equals(Vector4)` (exact), `ToString` template `"({0}, {1}, {2}, {3})"` default `"F2"`.

**Static members.** `zero, one, positiveInfinity, negativeInfinity` (no up/down/etc.); `Lerp` (clamped), `LerpUnclamped`, `MoveTowards` (4-component version of the Vector2 algorithm), `Scale`, `Normalize(a)`, `Dot`, `Project(a, b)` = `b * (Dot(a,b) / Dot(b,b))` (**no zero guard** – NaN for zero `b`), `Distance(a,b)` = `Magnitude(a - b)`, `Magnitude(a)` = `f32(Math.Sqrt(Dot(a,a)))`, `SqrMagnitude(a)`, `Min`, `Max`. There is no Angle, Cross, Reflect, ClampMagnitude or SmoothDamp on Vector4.

**Operators.** `+`, binary `-`, unary `-`, `*`(v,f), `*`(f,v), `/`(v,f); `==`: squared 4-component difference `< kEpsilon*kEpsilon`; `!=` negation.

**Conversions (declared on Vector4).**
- `implicit Vector4(Vector3 v)` → `(v.x, v.y, v.z, 0)`.
- `implicit Vector3(Vector4 v)` → `(v.x, v.y, v.z)`.
- `implicit Vector4(Vector2 v)` → `(v.x, v.y, 0, 0)`.
- `implicit Vector2(Vector4 v)` → `(v.x, v.y)`.
- Vector4↔Color conversions are declared on `Color` (section 4).

---

## 4. Color

`public struct Color : IEquatable<Color>, IFormattable` (not partial).

**Fields.** `public float r, g, b, a;`

**Constructors.** `Color(r,g,b,a)`; `Color(r,g,b)` sets `a = 1`.

**Indexer.** 0..3 → r,g,b,a; else `IndexOutOfRangeException("Invalid Color index(" + index + ")!")` (the index value is embedded, e.g. `"Invalid Color index(4)!"`).

**Instance members**
- `ToString` template `"RGBA({0}, {1}, {2}, {3})"`, default format `"F3"`. [verified: `Color.red.ToString()` = `"RGBA(1.000, 0.000, 0.000, 1.000)"`.]
- `GetHashCode() = r.GetHashCode() ^ (g.GetHashCode() << 2) ^ (b.GetHashCode() >> 2) ^ (a.GetHashCode() >> 1)` (same shape as Vector4; `new Color(1,2,3,4)` hashes identically to `new Vector4(1,2,3,4)`).
- `Equals(object)`; `Equals(Color other)` = `r.Equals(other.r) && g.Equals(other.g) && b.Equals(other.b) && a.Equals(other.a)` (**`float.Equals`**: NaN equals NaN).
- `grayscale` (get) = `0.299f*r + 0.587f*g + 0.114f*b`.
- `linear` (get) = `(GammaToLinearSpace(r), GammaToLinearSpace(g), GammaToLinearSpace(b), a)` – alpha untouched.
- `gamma` (get) = `(LinearToGammaSpace(r), LinearToGammaSpace(g), LinearToGammaSpace(b), a)`.
- `maxColorComponent` (get) = `Mathf.Max(Mathf.Max(r, g), b)` (alpha excluded; negative values allowed). [verified `(0.2,0.5,-3)` → 0.5.]
- **internal** `RGBMultiplied(float m)` = `(r*m, g*m, b*m, a)`; **internal** `RGBMultiplied(Color m)` = `(r*m.r, g*m.g, b*m.b, a)`; **internal** `AlphaMultiplied(float m)` = `(r, g, b, a*m)`. (Internal in Unity; UI Toolkit consumes `RGBMultiplied(float)` via `VisibleToOtherModules`. The shim may expose them as internal or public.)

**Static members**
- `Lerp(a, b, t)` – `t = Clamp01(t)`; each channel `a + (b - a) * t` (alpha included).
- `LerpUnclamped(a, b, t)` – same, unclamped.
- `RGBToHSV(Color rgb, out float H, out float S, out float V)` – algorithm:
  1. Choose the dominant channel: if `b > g && b > r` → helper(offset 4, dominant b, one = r, two = g); else if `g > r` → helper(2, g, one = b, two = r); else → helper(0, r, one = g, two = b).
  2. Helper: `V = dominant`. If `V != 0`: `small = (one > two) ? two : one`; `diff = V - small`; if `diff != 0` { `S = diff / V`; `H = offset + (one - two) / diff` } else { `S = 0`; `H = offset + (one - two)` }; `H /= 6`; if `H < 0` `H += 1`. Else (`V == 0`): `S = 0; H = 0`.
  Notes: V is not clamped (HDR input gives V > 1; [verified `(2,0.5,0.5)` → H 0, S 0.75, V 2]); greys give H = S = 0; H is in [0,1). [verified `RGBToHSV(Color.yellow)` = `(0.15338646, 0.9843137, 1)`.]
- `HSVToRGB(H, S, V)` = `HSVToRGB(H, S, V, true)`.
- `HSVToRGB(H, S, V, bool hdr)` – start from `white` (so alpha = 1). If `S == 0` → rgb = `(V,V,V)`. Else if `V == 0` → rgb = `(0,0,0)`. Else: rgb = 0; `h6 = H * 6`; `sector = FloorToInt(h6)`; `t = h6 - (float)sector`; `p = V*(1 - S)`; `q = V*(1 - S*t)`; `u = V*(1 - S*(1 - t))`; then by sector: 0 → `(V,u,p)`; 1 → `(q,V,p)`; 2 → `(p,V,u)`; 3 → `(p,q,V)`; 4 → `(u,p,V)`; 5 → `(V,p,q)`; 6 → `(V,u,p)`; −1 → `(V,p,q)`; **any other sector leaves rgb = (0,0,0)**. If `!hdr`, each of r,g,b is `Clamp01`'d. [verified: `HSVToRGB(1,1,1)` = red; `HSVToRGB(-0.1,1,1)` = `(1,0,0.6)`; `HSVToRGB(1.2,1,2,true)` = `(0,0,0,1)` because sector 7 has no case; `HSVToRGB(0,0,0.5)` = `(0.5,0.5,0.5)`.]

**Operators**
- `+`, `-`, `*` (Color,Color) – componentwise on all four channels including alpha.
- `*` (Color, Vector4) – `(a.r*b.x, a.g*b.y, a.b*b.z, a.a*b.w)`.
- `*` (Color,float), `*` (float,Color), `/` (Color,float) – all four channels.
- `==` – `d = lhs - rhs` per channel; `sqrmag = dr²+dg²+db²+da²`; `sqrmag < Vector4.kEpsilon * Vector4.kEpsilon`. [verified `red == (1,1e-6,0,1)` true; `(1,1e-5,0,1)` false.] `!=` negation.

**Conversions (declared on Color).** `implicit Vector4(Color c)` → `(r,g,b,a)`; `implicit Color(Vector4 v)` → `(x,y,z,w)`. (Color32 conversions are on Color32.)

**Preset colours (the ones NowUI is likely to use; all alpha = 1 except `clear`).** `red (1,0,0)`, `green (0,1,0)`, `blue (0,0,1)`, `white (1,1,1)`, `black (0,0,0)`, `yellow (1, 235f/255f, 4f/255f)` = `(1, 0.92156863 [0x3F6BEBEC], 0.015686275 [0x3C808081])` — **not** `(1, 0.92, 0.016)` as the docs print; `cyan (0,1,1)`, `magenta (1,0,1)`, `gray` = `grey` = `gray5` = `(0.5,0.5,0.5)`, `clear (0,0,0,0)`. Unity 6 also exposes `gray1..gray9` (`0.1..0.9`), `yellowNice` (= yellow), and ~140 named CSS/WPF-style presets (`aliceBlue`, `cornflowerBlue`, `softRed`, …) each defined as `new(rf, gf, bf, 1f)` with the 7-significant-digit float literals of `byte/255`; implement any NowUI references by the same rule (`byte/255f` rounded to float). An `internal static Dictionary<Color,string> defaultColorNames` exists for the colour picker; not needed.

---

## 5. Color32

`public partial struct Color32 : IEquatable<Color32>, IFormattable`, **explicit layout, 4 bytes**: `[FieldOffset(0)] private int rgba;` overlapped with `[FieldOffset(0)] byte r; [1] byte g; [2] byte b; [3] byte a`. On little-endian targets `rgba == r | g<<8 | b<<16 | a<<24`.

- Constructor `Color32(byte r, byte g, byte b, byte a)` – sets `rgba = 0` then the four bytes.
- Indexer 0..3 → r,g,b,a; else `IndexOutOfRangeException("Invalid Color32 index(" + index + ")!")`.
- **`implicit Color32(Color c)`** – each channel = `(byte)Mathf.Round(Mathf.Clamp01(c.ch) * 255f)`. `Mathf.Round` is banker's rounding (`Math.Round` default, to-even), so exact `.5` products round to the even byte: [verified] `127.5 → 128`, `128.5 → 128`, `0.5 → 0`, `254.5 → 254`; `1.5` alpha → 255; negative → 0; **NaN → 0** (Clamp01(NaN) = NaN, Round(NaN) = NaN, `(byte)NaN` yields 0 on the tested x64 Mono runtime; C# leaves float→byte of NaN unspecified, so the shim should special-case NaN → 0).
- **`implicit Color(Color32 c)`** – each channel = `c.ch / 255f`. [verified `(128,255,1,0)` → `(0.5019608, 1, 0.003921569, 0)`.]
- `Lerp(a, b, t)` – `t = Clamp01(t)`; each channel `(byte)(a.ch + (b.ch - a.ch) * t)` — the arithmetic is in float (byte promoted to int then float), and the cast **truncates toward zero**: [verified] `Lerp(0,255,0.5)` → 127; `Lerp(0,255,0.999)` → 254.
- `LerpUnclamped(a, b, t)` – same without clamping; out-of-range results wrap through the unchecked float→byte cast: [verified] `LerpUnclamped(100, 0, 1.5)` → `-50f` → byte 206.
- `GetHashCode()` = `rgba.GetHashCode()` = the packed int [verified `(1,2,3,4)` → 67305985 = 0x04030201].
- `Equals(object)`; `Equals(Color32)` = `rgba == other.rgba`. **No `==`/`!=` operators** are defined on Color32.
- `ToString` template `"RGBA({0}, {1}, {2}, {3})"`, no default format (bytes print as integers): `"RGBA(1, 2, 3, 4)"`.

---

## 6. Rect

`public partial struct Rect : IEquatable<Rect>, IFormattable`. Private fields in order `m_XMin, m_YMin, m_Width, m_Height` (floats). Width/height may be negative; nothing normalises them.

**Constructors.** `Rect(float x, float y, float width, float height)`; `Rect(Vector2 position, Vector2 size)`; `Rect(Rect source)` (copy).

**Static.** `zero` = `(0,0,0,0)`; `MinMaxRect(xmin, ymin, xmax, ymax)` = `(xmin, ymin, xmax - xmin, ymax - ymin)`; `NormalizedToPoint(rect, n)` = `(Mathf.Lerp(xMin, xMin+width, n.x), Mathf.Lerp(yMin, yMin+height, n.y))` (clamped lerp); `PointToNormalized(rect, p)` = `(Mathf.InverseLerp(xMin, xMin+width, p.x), Mathf.InverseLerp(yMin, yMin+height, p.y))` (clamped; 0 when width or height is 0).

**Properties (exact setter semantics)**
- `x` get/set `m_XMin` (position moves, width unchanged). `y` likewise.
- `width`, `height` get/set the raw fields.
- `position` get `(m_XMin, m_YMin)`; set assigns both (size unchanged).
- `size` get `(m_Width, m_Height)`; set assigns both.
- `center` get `(m_XMin + m_Width*0.5f, m_YMin + m_Height*0.5f)`; set `m_XMin = value.x - m_Width*0.5f; m_YMin = value.y - m_Height*0.5f`.
- `min` get `(m_XMin, m_YMin)`; set → `xMin = value.x; yMin = value.y` (goes through the `xMin`/`yMin` setters, so **width/height change**).
- `max` get `(m_XMin + m_Width, m_YMin + m_Height)`; set → `xMax = value.x; yMax = value.y`.
- `xMin` get `m_XMin`; set: `oldXMax = m_XMin + m_Width; m_XMin = value; m_Width = oldXMax - m_XMin` (keeps the right edge fixed).
- `yMin` same pattern with height.
- `xMax` get `m_XMin + m_Width`; set `m_Width = value - m_XMin`.
- `yMax` get `m_YMin + m_Height`; set `m_Height = value - m_YMin`.
- Obsolete `left/right/top/bottom` = xMin/xMax/yMin/yMax getters.

**Methods**
- `Set(x, y, width, height)`.
- `Contains(Vector2 point)` and `Contains(Vector3 point)` (uses x,y only): `point.x >= m_XMin && point.x < m_XMin + m_Width && point.y >= m_YMin && point.y < m_YMin + m_Height` — **min inclusive, max exclusive**; for a negative-size rect this is always false.
- `Contains(Vector3 point, bool allowInverse)`: if `!allowInverse` → the above. Else with `xmax = m_XMin + m_Width`, `ymax = m_YMin + m_Height`: `xAxis = (m_Width < 0 && point.x <= m_XMin && point.x > xmax) || (m_Width >= 0 && point.x >= m_XMin && point.x < xmax)`; same for y; return `xAxis && yAxis` (an inverted axis is max-inclusive/min-exclusive).
- `Overlaps(Rect other)`: `other.xMax > xMin && other.xMin < xMax && other.yMax > yMin && other.yMin < yMax` (all computed as `m_XMin + m_Width` etc.; strict, so touching edges do not overlap).
- `Overlaps(Rect other, bool allowInverse)`: if `allowInverse` compares `Max(rhsXMin, rhsXMax) > Min(lhsXMin, lhsXMax) && Min(rhsX...) < Max(lhsX...)` and same for y (each rect's edges normalised with `Mathf.Min/Max` first); else the plain `Overlaps`.

**Equality.** `==`: **exact** `m_XMin == && m_YMin == && m_Width == && m_Height ==` (no tolerance; false with NaN). `!=` negation. `Equals(Rect)`: the four `float.Equals` (NaN-equal). `GetHashCode() = m_XMin.GetHashCode() ^ (m_Width.GetHashCode() << 2) ^ (m_YMin.GetHashCode() >> 2) ^ (m_Height.GetHashCode() >> 1)` (note the x/width/y/height order) [verified `(1,2,3,4)` → 247463936].

**ToString.** Template `"(x:{0}, y:{1}, width:{2}, height:{3})"`, default `"F2"`: `"(x:1.00, y:2.00, width:3.00, height:4.00)"`.

---

## 7. RectInt

`public struct RectInt : IEquatable<RectInt>, IFormattable`; private ints `m_XMin, m_YMin, m_Width, m_Height`.

- Constructors `(int xMin, int yMin, int width, int height)`, `(Vector2Int position, Vector2Int size)`. `zero`.
- `x`, `y`, `width`, `height` – raw get/set.
- `position` `(m_XMin, m_YMin)` get/set; `size` `(m_Width, m_Height)` get/set.
- `center` (get only) = `new Vector2(m_XMin + m_Width*0.5f, m_YMin + m_Height*0.5f)` (raw, not normalised).
- **`xMin` get = `Mathf.Min(m_XMin, m_XMin + m_Width)`**, `xMax` get = `Mathf.Max(m_XMin, m_XMin + m_Width)`; `yMin`/`yMax` likewise — the getters **normalise inverted rects**. Setters: `xMin`: `old = xMax; m_XMin = value; m_Width = old - m_XMin` (uses the *normalised* old max); `xMax`: `m_Width = value - m_XMin`; y analogous.
- `min` get `new Vector2Int(xMin, yMin)` (normalised), set → `xMin = v.x; yMin = v.y`; `max` likewise with xMax/yMax.
- `SetMinMax(Vector2Int min, Vector2Int max)` – `m_XMin = min.x; m_YMin = min.y; m_Width = max.x - min.x; m_Height = max.y - min.y`.
- `ClampToBounds(RectInt bounds)` – with `bounds.xMin/xMax/yMin/yMax` (normalised): `m_XMin = Math.Max(Math.Min(xmax, m_XMin), xmin); m_YMin = …; m_Width = Math.Min(xmax - m_XMin, m_Width); m_Height = Math.Min(ymax - m_YMin, m_Height)`.
- `Contains(Vector2Int p)` – `p.x >= xMin && p.y >= yMin && p.x < xMax && p.y < yMax` using the normalised getters (so inverted rects still contain points).
- `Overlaps(RectInt other)` – `other.xMin < xMax && other.xMax > xMin && other.yMin < yMax && other.yMax > yMin` (normalised getters, strict).
- `==`/`!=` – exact on the four raw fields; `Equals(RectInt)` same; `GetHashCode()`: `xh ^ (yh << 4) ^ (yh >> 28) ^ (wh >> 4) ^ (wh << 28) ^ (hh >> 4) ^ (hh << 28)` where `xh = m_XMin.GetHashCode()` etc. (int hash = the int).
- `ToString` template `"(x:{0}, y:{1}, width:{2}, height:{3})"`, no default format: `"(x:1, y:2, width:3, height:4)"`.
- `allPositionsWithin` – returns `PositionEnumerator(min, max)`, a struct `IEnumerator<Vector2Int>` with `GetEnumerator()` returning itself. Iteration order: x fastest from `min.x` to `max.x - 1`, then y from `min.y` to `max.y - 1`; empty if `min.x >= max.x` or `min.y >= max.y`. Internal state: `_current = min` with `_current.x--`; `MoveNext`: if `_current.y >= _max.y` return false; `_current.x++`; if `_current.x >= _max.x` { `_current.x = _min.x`; if `_current.x >= _max.x` return false; `_current.y++`; if `_current.y >= _max.y` return false }; return true. `Reset` restores the initial state; `Dispose` no-op.

---

## 8. Matrix4x4 (and FrustumPlanes)

`public partial struct Matrix4x4 : IEquatable<Matrix4x4>, IFormattable`

**Storage.** Sixteen public floats named `mRC` (R = row, C = column), **declared in column-major memory order**: `m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33`. `mRC` is the element in row R, column C of the mathematical matrix; a column is a basis vector/translation (column 3 = position `m03, m13, m23`). The shim must declare the fields in this order so that `StructLayout`/`unsafe` reinterpretation and serialisation match.

**Constructor.** `Matrix4x4(Vector4 column0, column1, column2, column3)` – `m00 = c0.x, m10 = c0.y, m20 = c0.z, m30 = c0.w; m01 = c1.x …` (each Vector4 is a column).

**Indexers.** `this[int index]` (0..15) maps to the fields in memory order: 0→m00, 1→m10, 2→m20, 3→m30, 4→m01, 5→m11, 6→m21, 7→m31, 8→m02, 9→m12, 10→m22, 11→m32, 12→m03, 13→m13, 14→m23, 15→m33; else `IndexOutOfRangeException("Invalid matrix index!")`. `this[int row, int column]` = `this[row + column * 4]`. [verified: `m[0,3] == m[12] == m03`.]

**Accessors.** `GetColumn(i)` → `(m0i, m1i, m2i, m3i)`; invalid i → `IndexOutOfRangeException("Invalid column index!")`. `GetRow(i)` → `(mi0, mi1, mi2, mi3)`; invalid → `"Invalid row index!"`. `SetColumn(i, v)` writes `this[0,i]..this[3,i]`; `SetRow(i, v)` writes `this[i,0]..this[i,3]` (both go through the indexer, so bad indices throw the `"Invalid matrix index!"` message). `GetPosition()` → `(m03, m13, m23)`.

**Statics.** `zero` (all 0), `identity`.

**Equality.** `==`: `lhs.GetColumn(i) == rhs.GetColumn(i)` for i = 0..3, i.e. **each column compared with the Vector4 tolerance** (squared column difference `< 1e-10`), false with NaN. `!=` = `!(lhs == rhs)`. `Equals(Matrix4x4)`: each column's `Vector4.Equals` (exact). `GetHashCode() = c0.GetHashCode() ^ (c1.GetHashCode() << 2) ^ (c2.GetHashCode() >> 2) ^ (c3.GetHashCode() >> 1)` using `Vector4.GetHashCode` of the columns.

**Operators.**
- `Matrix4x4 * Matrix4x4` – standard product; element `[r,c] = Σ_k lhs[r,k] * rhs[k,c]` evaluated as `lhs.mr0*rhs.m0c + lhs.mr1*rhs.m1c + lhs.mr2*rhs.m2c + lhs.mr3*rhs.m3c` (left-to-right float sums).
- `Matrix4x4 * Vector4` – column vector: `x = m00*v.x + m01*v.y + m02*v.z + m03*v.w`, etc.

**Transform helpers.**
- `MultiplyPoint(Vector3 p)` – `res = (m00*p.x + m01*p.y + m02*p.z + m03, m10*…+m13, m20*…+m23)`; `w = m30*p.x + m31*p.y + m32*p.z + m33`; `w = 1/w`; `res *= w` per component (division by zero → ±∞/NaN, no guard).
- `MultiplyPoint3x4(p)` – same without the divide.
- `MultiplyVector(v)` – upper 3×3 only, no translation.
- `TransformPlane(Plane p)` – `it = this.inverse`; `(a,b,c,d) = it^T · (n.x, n.y, n.z, p.distance)` i.e. `a = it.m00*x + it.m10*y + it.m20*z + it.m30*w`, `b = it.m01*x + it.m11*y + …`, etc.; returns `new Plane(normal (a,b,c), d)`.

**Factories (managed).**
- `Scale(Vector3 s)` – diag `(s.x, s.y, s.z, 1)`.
- `Translate(Vector3 t)` – identity with `m03 = t.x, m13 = t.y, m23 = t.z`.
- `Rotate(Quaternion q)` – assumes a unit quaternion (no normalisation): `x2 = q.x*2, y2 = q.y*2, z2 = q.z*2; xx = q.x*x2, yy = q.y*y2, zz = q.z*z2, xy = q.x*y2, xz = q.x*z2, yz = q.y*z2, wx = q.w*x2, wy = q.w*y2, wz = q.w*z2`; then `m00 = 1 - (yy+zz), m10 = xy + wz, m20 = xz - wy; m01 = xy - wz, m11 = 1 - (xx+zz), m21 = yz + wx; m02 = xz + wy, m12 = yz - wx, m22 = 1 - (xx+yy)`; last row/column `(0,0,0,1)`.
- `Determinant(m)` – static alias of `m.determinant`.
- `Inverse(m)`, `Transpose(m)` – static aliases of the properties.
- `Frustum(FrustumPlanes fp)` – calls the 6-float `Frustum`.

**Native members — behaviour [verified on 6000.4.0f1]**
- `TRS(pos, q, s)` and `SetTRS(pos, q, s)` – result is **bit-identical** to `Translate(pos) * Rotate(q) * Scale(s)` (equivalently: take `Rotate(q)`, multiply column 0 by `s.x`, column 1 by `s.y`, column 2 by `s.z`, set column 3 to `(pos, 1)`) for unit quaternions in tests. `Rotate(q)` equals `TRS(zero, q, one)` bit-for-bit. A non-unit quaternion is **not** normalised (same algebraic formula; last-ulp rounding may differ from the managed `Rotate`).
- `inverse` / `Inverse(m)` – general 4×4 inverse. **A singular matrix (determinant 0) returns `Matrix4x4.zero`** (docs and probe agree: `zero.inverse`, `Scale(1,0,1).inverse` → zero). The result is not bit-reproducible by cofactor formulas: `Perspective(60,1.5,0.3,1000).inverse` has `m22 = -5.9568897e-08` instead of 0, which indicates Gaussian elimination with partial pivoting (Mesa `gluInvertMatrix` lineage) in single precision. For TRS matrices the result differs from `Inverse3DAffine` by ≈1.8e-7. Implement elimination with partial pivoting in `float`; expect ≤ a few ulp of divergence.
- `Inverse3DAffine(input, ref result)` – affine (3×4) inverse; returns `false` and writes `zero` when singular; `true` otherwise.
- `transpose` / `Transpose(m)` – exact transpose.
- `determinant` – **bit-identical** in tests to a cofactor expansion computed in **double** precision and cast to float: with `mRC` promoted to double, `v0 = m20*m31 - m21*m30, v1 = m20*m32 - m22*m30, v2 = m20*m33 - m23*m30, v3 = m21*m32 - m22*m31, v4 = m21*m33 - m23*m31, v5 = m22*m33 - m23*m32; t00 = +(v5*m11 - v4*m12 + v3*m13); t10 = -(v5*m10 - v2*m12 + v1*m13); t20 = +(v4*m10 - v2*m11 + v0*m13); t30 = -(v3*m10 - v1*m11 + v0*m12); det = (float)(t00*m00 + t10*m01 + t20*m02 + t30*m03)`. [verified: `Scale(2,3,4)` → 24; `TRS(…,(2,3,4))` → 24 exactly; `Perspective(60,1.5,0.3,1000)` → -1.20036006 bit-exact.]
- `isIdentity` – **approximate**: every one of the 16 elements must be within about 1e-5 of the identity element (`|m − I| < 1e-5`; 9e-6 deviations pass, 1.1e-5 fail; the bottom row *is* checked: `m30 = 1e-4` → false, `m33 = 1+1e-4` → false); NaN → false; `zero` → false. Whether exactly 1e-5 passes was not tested; implement `Mathf.Abs(diff) < 1e-5f`.
- `ValidTRS()` – **only checks that the last row is exactly `(0,0,0,1)`**. Shear, non-orthogonal or zero-scale matrices return true (`Scale(0,0,0)` true, `m01 = 0.5` true); `m30 = 1e-6` false, `m33 = 1+1e-6` false; `Perspective` false; `Ortho` true.
- `lossyScale` – `(sign · |column0|, |column1|, |column2|)` where `|·|` is the 3-component magnitude and `sign = -1` if the upper-3×3 determinant is negative, else `+1`. The sign is always applied to **x only**: [verified] TRS with scale `(-2,3,4)`, `(2,-3,4)`, `(2,3,-4)` and `(-2,-3,-4)` all return `(-2,3,4)`; `(-2,-3,4)` returns `(2,3,4)`; a sheared identity (`m01 = 0.5`) returns `(1, 1.118034, 1)`; `Perspective(60,1.5,…)` returns `(-1.1547005, 1.7320508, 1.0006001)`.
- `rotation` – for proper matrices (det > 0) it is the rotation of the column-normalised upper 3×3 (`TRS(p,q,(2,3,4)).rotation` ≈ `q` within 3e-8; `Rotate(Euler(120,200,300)).rotation` ≈ the source quaternion within 1 ulp). For TRS-built matrices with negative scale the result is `q` composed with a 180° turn so that the remaining scale matches `lossyScale`'s "sign on x" convention: scale `(2,-3,4)` → `q * AngleAxis(180, z)`, `(2,3,-4)` → `q * AngleAxis(180, y)`, `(-2,-3,-4)` → `q * AngleAxis(180, x)`, `(-2,3,4)` → `q`. However the pure mirror `Scale(-1,1,1).rotation` returned `(-4.2e-7, 1, 0, 0)` (180° about y, not identity), so the improper-matrix path is data-dependent and could not be reduced to one rule. `zero.rotation` returns a garbage non-identity quaternion (`(-0.159, -0.384, -0.384, 0.824)`), `Perspective(...).rotation` = identity. Recommendation: implement the proper case (normalise columns, standard trace/largest-diagonal matrix→quaternion, normalise result) and treat det ≤ 0 as unspecified.
- `Ortho(left, right, bottom, top, zNear, zFar)` – OpenGL convention (clip z ∈ [−1, 1], includes the z-flip): identity with `m00 = 2/(r-l)`, `m03 = -(r+l)/(r-l)`, `m11 = 2/(t-b)`, `m13 = -(t+b)/(t-b)`, `m22 = -2/(f-n)`, `m23 = -(f+n)/(f-n)`, all in float with `dx = r-l`, `dy = t-b`, `dz = f-n` computed once. [verified bit-exact for `Ortho(0,800,600,0,-1,1)` = rows `(0.0025,0,0,-1) / (0,-0.0033333334,0,1) / (0,0,-1,-0) / (0,0,0,1)`, `Ortho(0,800,0,600,-100,100)`, and an arbitrary-value case; `Ortho(-1,1,-1,1,0.3,1000)` differed from the formula by 1 ulp in the z row (`m22 = -0.0020006 [0xBB031C80]`, `m23 = -1.0006001 [0xBF8013AA]`), i.e. native evaluation order/contraction can differ by an ulp.]
- `Perspective(fov, aspect, zNear, zFar)` – `rad = (fov/2) * Deg2Rad`; `cot = cos(rad)/sin(rad)`; `dz = zNear - zFar`; `m00 = cot/aspect`, `m11 = cot`, `m22 = (zFar + zNear)/dz`, `m23 = 2*zNear*zFar/dz`, `m32 = -1`, all else 0 (`m33 = 0`). [verified: `Perspective(90,1,0.1,100)` bit-exact = rows `(1,0,0,0)/(0,1,0,0)/(0,0,-1.002002,-0.2002002)/(0,0,-1,0)`; other inputs within 1–2 ulp because native `sinf/cosf` differ from `Math.Sin/Cos`.]
- `Frustum(l, r, b, t, n, f)` – `m00 = 2n/(r-l)`, `m02 = (r+l)/(r-l)`, `m11 = 2n/(t-b)`, `m12 = (t+b)/(t-b)`, `m22 = -(f+n)/(f-n)`, `m23 = -(2fn)/(f-n)`, `m32 = -1`, rest 0. [verified within 1 ulp; `Frustum(-1,1,-1,1,0.3,1000)` rows `(0.3,0,0,0)/(0,0.3,0,0)/(0,0,-1.0006001,-0.60018003)/(0,0,-1,0)`.]
- `decomposeProjection` – returns `FrustumPlanes { left, right, bottom, top, zNear, zFar }` recovered from a projection matrix (`Perspective(60,1.5,0.3,1000)` → `l = -0.25980765, r = 0.25980765, b = -0.1732051, t = 0.1732051, n = 0.3, f = 1000.1341`).
- `LookAt(from, to, up)` – equals `TRS(from, Quaternion.LookRotation(to - from, up), Vector3.one)` within 6e-8 (not bit-exact). Degenerate cases: `from == to` → identity rotation with translation `from`; **`LookAt(zero, up, up)` (forward parallel to up) returns the identity matrix**, whereas `LookRotation(up, up)` returns a −90° x-rotation — so LookAt has its own fallback.
- `internal CompareApproximately(a, b, threshold)` – native per-element approximate comparison; not needed by a shim unless NowUI's internals reference it.

**ToString.** Template `"{0}\t{1}\t{2}\t{3}\n{4}\t{5}\t{6}\t{7}\n{8}\t{9}\t{10}\t{11}\n{12}\t{13}\t{14}\t{15}\n"` filled **row-major** (`m00, m01, m02, m03, m10, …`), default format `"F5"`, trailing newline included. [verified `identity.ToString()` = `"1.00000\t0.00000\t0.00000\t0.00000\n0.00000\t1.00000\t…\n"`.]

**FrustumPlanes.** `[Serializable] struct FrustumPlanes { public float left, right, bottom, top, zNear, zFar; }`.

---

## 9. Quaternion (UI-relevant subset plus everything public)

`public partial struct Quaternion : IEquatable<Quaternion>, IFormattable`

**Constant.** `public const float kEpsilon = 0.000001F;` (1e-6, ten times smaller than the vector epsilon).

**Fields.** `public float x, y, z, w;` Constructor `(x,y,z,w)`. `Set(x,y,z,w)`. Indexer 0..3 → x,y,z,w; else `IndexOutOfRangeException("Invalid Quaternion index!")`.

**Static.** `identity = (0,0,0,1)`.

**Operators.**
- `q * q` (Hamilton product, lhs applied after rhs): `x = lw*rx + lx*rw + ly*rz - lz*ry; y = lw*ry + ly*rw + lz*rx - lx*rz; z = lw*rz + lz*rw + lx*ry - ly*rx; w = lw*rw - lx*rx - ly*ry - lz*rz`.
- `q * Vector3 p` (rotate point, assumes unit q): `x2 = q.x*2, y2 = q.y*2, z2 = q.z*2; xx = q.x*x2, yy = q.y*y2, zz = q.z*z2, xy = q.x*y2, xz = q.x*z2, yz = q.y*z2, wx = q.w*x2, wy = q.w*y2, wz = q.w*z2`; `res.x = (1 - (yy+zz))*p.x + (xy - wz)*p.y + (xz + wy)*p.z; res.y = (xy + wz)*p.x + (1 - (xx+zz))*p.y + (yz - wx)*p.z; res.z = (xz - wy)*p.x + (yz + wx)*p.y + (1 - (xx+yy))*p.z`. [verified `Euler(0,90,0) * forward` = `(0.99999994, 0, 5.96e-8)`.]
- `==`: `Dot(lhs, rhs) > 1.0f - kEpsilon` (so `q == -q` is **false**, and comparisons are only meaningful for unit quaternions); `!=`: `!(Dot > 1 - kEpsilon)`.

**Instance/static managed members.**
- `Dot(a,b)` – 4-component dot.
- `Angle(a, b)` – `dot = Min(Abs(Dot(a,b)), 1)`; return `(dot > 1 - kEpsilon) ? 0 : Acos(dot) * 2 * Rad2Deg`. [verified `Angle(q, -q)` = 0, `Angle(identity, Euler(0,180,0))` = 180.]
- `RotateTowards(from, to, maxDegreesDelta)` – `angle = Angle(from,to); if (angle == 0) return to; return SlerpUnclamped(from, to, Min(1, maxDegreesDelta / angle))`.
- `Normalize(q)` – `mag = Mathf.Sqrt(Dot(q,q)); if (mag < Mathf.Epsilon) return identity; return q / mag` per component. Because `Dot` is a float, tiny quaternions underflow: `(1e-30,0,0,0)` → identity; `(1e-20,0,0,0)` → `(1.0000026,0,0,0)` (denormal precision). `Normalize()` instance and `normalized` call it. NaN propagates.
- `Equals(object)`; `Equals(Quaternion)` uses `float.Equals` on all four (NaN-equal). `GetHashCode()` as Vector4 (`x ^ (y<<2) ^ (z>>2) ^ (w>>1)` of component hashes).
- `ToString` template `"({0}, {1}, {2}, {3})"`, default `"F5"`.
- `eulerAngles` get = `MakePositive(ToEulerRad(this) * Rad2Deg)`; set = `this = FromEulerRad(value * Deg2Rad)` (Vector3 × float).
- `Euler(float x, float y, float z)` = `FromEulerRad((x*Deg2Rad, y*Deg2Rad, z*Deg2Rad))`; `Euler(Vector3 e)` = `FromEulerRad(e * Deg2Rad)` (bit-identical to the 3-float form [verified]).
- `ToAngleAxis(out float angle, out Vector3 axis)` – native axis/angle in radians, then `angle *= Rad2Deg`. [verified: identity → angle 0, axis `(1,0,0)`; `(0,0,0,-1)` → angle 360, axis `(1,0,0)`; `Euler(0,90,0)` → 89.99999°, axis `(0, 1.0000001, 0)` (axis not exactly unit).]
- `SetLookRotation(view[, up])` = `this = LookRotation(view, up)`; `SetFromToRotation(from, to)` = `this = FromToRotation(from, to)`.
- `MakePositive` (private): with `negativeFlip = -0.0001f * Rad2Deg` (≈ −0.0057296°) and `positiveFlip = 360 + negativeFlip` (≈ 359.9943): for each of x, y, z: if `v < negativeFlip` add 360; else if `v > positiveFlip` subtract 360. Consequently tiny negative angles are **not** wrapped: [verified] `Euler(0,-0.001,0).eulerAngles.y` = `-0.001`; `Euler(0,-0.01,0)` → `359.99`; `Euler(0,359.999,0)` → `-0.00100085`.
- Obsolete radian-based API: `EulerRotation`, `SetEulerRotation`, `ToEuler`, `EulerAngles`, `ToAxisAngle`, `SetEulerAngles`, `ToEulerAngles`, `SetAxisAngle`, `AxisAngle` — thin wrappers over the native radian functions (`AxisAngle(axis, rad)` = `AngleAxis(Rad2Deg * rad, axis)`).

**Native members — behaviour [verified]**
- `FromEulerRad` / `Euler(x,y,z)` (degrees) – **ZXY order**: with half-angles in radians `qX = (sin(x/2), 0, 0, cos(x/2))`, `qY = (0, sin(y/2), 0, cos(y/2))`, `qZ = (0, 0, sin(z/2), cos(z/2))`, the result is **`(qY * qX) * qZ`** (equivalently `AngleAxis(y, up) * AngleAxis(x, right) * AngleAxis(z, forward)`; the docs' "rotates z degrees around z, then x around x, then y around y"). Probe: the half-angle product matches Unity bit-for-bit in most cases and within 1 ulp otherwise (native `sinf/cosf` vs `Math.Sin/Cos`). Reference values: `Euler(10,20,30)` = `(0.12767944, 0.14487813, 0.23929833, 0.9515485)`; `Euler(90,0,0)` = `(0.70710677, 0, 0, 0.70710677)`; `Euler(360,0,0)` = `(-8.742278e-08, 0, 0, -1)`; `Euler(150,35,45)` = `(0.88087976, -0.2806315, -0.17388792, 0.33920458)`.
- `ToEulerRad` / `eulerAngles` – inverse of the above in ZXY order, normalised to [0, 360) by `MakePositive`; scale-invariant (non-unit `(1,2,3,4)` gives the same result as its normalised form; `(0,0,0,0)` and `(0,0,0,2)` → `(0,0,0)`). Round trips: `Euler(10,20,30)` → `(9.999999, 20.000002, 30.000002)`; docs example `Euler(150,35,45)` → `(30.000002, 215, 225)`; `Euler(-10,-20,-30)` → `(350, 340, 330)`. Gimbal lock (x = ±90°): the singular branch puts all remaining rotation into y and sets z = 0: `Euler(90,45,0)` → `(90,45,0)`, `Euler(-90,45,0)` → `(270,45,0)`, `Euler(270,10,20)` → `(270, 29.999998, 0)`; the branch triggers when `|sin x|` rounds to 1 in float (`Euler(89.99,45,0)` → `(90, 45.000004, 0)`). Standard formula for a unit quaternion with the ZXY convention: build the rotation matrix, `x = asin(clamp(-m12, -1, 1))`; if not singular `y = atan2(m02, m22)`, `z = atan2(m10, m11)`; if singular (`|m12| ≈ 1`) `y = atan2(-m20, m00)`, `z = 0`. Exact singular threshold unverified.
- `AngleAxis(float degrees, Vector3 axis)` – axis is normalised internally; **if `axis.magnitude <= 1e-6` (or NaN) the result is `identity`** (`1e-6` → identity, `2e-6` → rotation); otherwise `half = degrees * Deg2Rad * 0.5; s = sin(half) / |axis|; result = (s*axis.x, s*axis.y, s*axis.z, cos(half))`. Bit-exact against this formula except for large angles (−450° differed by 1–2 ulp due to trig argument reduction). `AngleAxis(180, up)` = `(0, 1, 0, -4.371139e-08)`.
- `Inverse(q)` – **the conjugate `(-x, -y, -z, w)` with no normalisation**, for any input (`Inverse((1,2,3,4))` = `(-1,-2,-3,4)`; zero → `(-0,-0,-0,0)`).
- `Lerp(a, b, t)` = `LerpUnclamped(a, b, Clamp01(t))`; `LerpUnclamped(a, b, t)`: if `Dot(a,b) < 0` interpolate toward `-b` (`a + t*(-b - a)` per component) else `a + t*(b - a)`; then **normalise** (`q / sqrt(Dot(q,q))`, no epsilon guard). Non-unit inputs therefore come out unit (`Lerp((0,0,0,2), …, 0)` = identity). Probe matched this bit-for-bit except ≤1-ulp differences attributable to the native normalisation (e.g. `w = 0.5` vs `0.49999997`).
- `Slerp(a, b, t)` = `SlerpUnclamped(a, b, Clamp01(t))`; `SlerpUnclamped(a, b, t)`: `dot = Dot(a,b)`; if `dot < 0` { `dot = -dot; b = -b` }; **if `dot < 0.95`**: `angle = acos(dot); invSin = 1/sin(angle); sa = sin(angle*t); sb = sin(angle*(1-t)); result = (a*sb + b*sa) * invSin` per component (no final normalisation); **else** return `Lerp(a, b, t)` (normalised lerp). Evidence: for a pair with dot 0.9976 Slerp and Lerp outputs are bit-identical; for dot 0.5 they differ and match the sine formula within 1 ulp; `q` vs `-q` returns `a` for every t; a 180° pair (dot ≈ −4e-8) negates `b` first and then slerps the "short" way (`Slerp(identity, Euler(0,180,0), 0.3)` = `(0, -0.4539905, 0, 0.8910065)`). Extrapolation: `SlerpUnclamped(identity, Euler(0,120,0), 1.5)` = `(0, 1, 0, -3.4e-08)`.
- `LookRotation(forward, up = Vector3.up)` – builds the rotation whose z axis is `normalize(forward)` and whose y axis is `up` re-orthogonalised (`right = normalize(cross(up, forward))`, `up' = cross(forward, right)`). [verified: `(forward, up)` → identity; `(right, up)` → `(0, 0.70710677, 0, 0.70710677)`; `(-forward, up)` → `(0,1,0,0)`; `((1,2,3), up)` → `(-0.27465674, 0.15385647, 0.04457066, 0.94810617)`; `((1,2,3), (0,1,1))` → `(-0.19896805, 0.24396692, 0.38962165, 0.865498)`.] Degenerate: **zero forward → identity** (Unity also logs the error "Look rotation viewing vector is zero"); forward parallel to up (`(up, up)`) → `(-0.70710677, 0, 0, 0.70710677)` (a −90° x rotation; Unity picks a fallback basis rather than failing).
- `FromToRotation(from, to)` – shortest rotation taking direction `from` to direction `to`; inputs are normalised (`(right*2, up*3)` = `(right, up)` = `(0,0,0.70710677,0.70710677)`); antiparallel inputs pick a perpendicular axis: `(right, left)` → `(0,1,0,0)`, `(up, down)` → `(1,0,-0,0)`; zero input → identity. `((1,2,3),(3,2,1))` → `(-0.15430337, 0.30860674, -0.15430337, 0.9258201)`.

---

## 10. Bounds

`public partial struct Bounds : IEquatable<Bounds>, IFormattable`; private `Vector3 m_Center, m_Extents` (in that order).

- Constructor `Bounds(Vector3 center, Vector3 size)` – `m_Center = center; m_Extents = size * 0.5f` per component (`size.x * 0.5F`).
- `center` get/set raw. `extents` get/set raw. `size` get `extents * 2` (`m_Extents.x * 2.0F`), set `extents = value * 0.5F` per component.
- `min` get `center - extents`, set → `SetMinMax(value, max)`; `max` get `center + extents`, set → `SetMinMax(min, value)`.
- `SetMinMax(min, max)` – `extents = (max - min) * 0.5F` per component; `center = min + extents` per component (note: center is derived from the new extents, in that order).
- `Encapsulate(Vector3 p)` – `SetMinMax(Vector3.Min(min, p), Vector3.Max(max, p))`. [verified: unit box + `(3,0,0)` → center `(1.25,0,0)`, extents `(1.75,0.5,0.5)`.]
- `Encapsulate(Bounds b)` – `Encapsulate(b.min); Encapsulate(b.max)`.
- `Expand(float amount)` – `amount *= 0.5f; extents += amount` on each axis (can go negative). `Expand(Vector3 amount)` – `extents.i += amount.i * 0.5f`.
- `Intersects(Bounds b)` – `min.x <= b.max.x && max.x >= b.min.x && …` for y and z (inclusive; computed from min/max even when extents are negative — [verified] a negative-x-extent box still "intersects" the unit box).
- `Contains(Vector3 p)` – **native**, inclusive on min and max faces/edges/corners; **any negative extent → always false**; NaN coordinate → true (comparisons implemented as "reject if `p < min` or `p > max`"). [verified: `(0.5,0,0)` true, `(0.5000001,0,0)` false, negative-extent box contains nothing, `(NaN,0,0)` true.]
- `SqrDistance(Vector3 p)` – native; squared distance from p to the box (0 inside): `(2,0,0)` → 2.25, `(2,2,2)` → 6.75.
- `ClosestPoint(Vector3 p)` – native; per component `max(min_i, min(max_i, p_i))` (for negative extents this yields `min` because min > max: `(0.25,0,0)` in the negative-x box → `(0.5,0,0)`).
- `IntersectRay(Ray r)` / `IntersectRay(Ray r, out float distance)` – native slab test; `distance` is the ray parameter of the entry point (the `Ray` struct normalises its direction, so it is a world distance), **may be negative when the origin is inside** (`(-0.5)` from the centre heading +x), 0 when there is no hit; grazing an edge counts as a hit.
- `==`: `lhs.m_Center == rhs.m_Center && lhs.m_Extents == rhs.m_Extents` using **Vector3's tolerant `==`** [verified 1e-6 difference → equal]; `!=` negation. `Equals(Bounds)` uses `Vector3.Equals` (exact). `GetHashCode() = center.GetHashCode() ^ (extents.GetHashCode() << 2)`.
- `ToString` template `"Center: {0}, Extents: {1}"` where each part is `Vector3.ToString(format, provider)` with default `"F2"`: `"Center: (1.00, 1.00, 1.00), Extents: (1.00, 1.00, 1.00)"`.

---

## 11. Mathf (and UnityEngineInternal.MathfInternal)

`public partial struct Mathf` — all members static.

**`UnityEngineInternal.MathfInternal`** (public partial struct): `public static volatile float FloatMinNormal = 1.17549435E-38f;` `public static volatile float FloatMinDenormal = Single.Epsilon;` `public static bool IsFlushToZeroEnabled = (FloatMinDenormal == 0);` — evaluated once at type initialisation through a volatile load, so on a flush-to-zero CPU mode the denormal reads as 0.

**Constants**
- `PI = (float)Math.PI` = 3.1415927 `[0x40490FDB]`.
- `Infinity = float.PositiveInfinity`, `NegativeInfinity = float.NegativeInfinity`.
- `Deg2Rad = PI * 2F / 360F` (constant-folded in float) = 0.017453292 `[0x3C8EFA35]`.
- `Rad2Deg = 1F / Deg2Rad` = 57.29578 `[0x42652EE1]`.
- `Epsilon` (static readonly, **not** const) = `IsFlushToZeroEnabled ? FloatMinNormal : FloatMinDenormal`. **In practice (desktop editor/players, verified on Windows x64): `1.401298E-45` `[0x00000001]`, i.e. `float.Epsilon`;** `1.17549435E-38` only on FTZ platforms. The shim should replicate the runtime check or hard-code `float.Epsilon` for desktop.
- `internal const int kMaxDecimals = 15`.
- `private static readonly float ApproxEpsilon = Epsilon * 8` (= 1.12e-44 on desktop).

**Trig/exp — all are `(float)` casts of the `System.Math` double functions:** `Sin, Cos, Tan, Asin, Acos, Atan, Atan2(y, x), Sqrt, Pow(f, p), Exp, Log(f, p), Log(f), Log10`. `Abs(float)` = `Math.Abs(float)`; `Abs(int)` = `Math.Abs(int)` (throws `OverflowException` for `int.MinValue`, as .NET does).

**Min/Max**
- `Min(float a, float b)` = `a < b ? a : b`; `Max(float a, float b)` = `a > b ? a : b`. NaN ordering follows from the expression: `Min(NaN, 1) = 1`, `Min(1, NaN) = NaN`, `Max(NaN, 1) = 1`, `Max(1, NaN) = NaN` [verified].
- `Min(int,int)`, `Max(int,int)` same form.
- `Min(params float[])`, `Max(params float[])`, and int versions: **empty array returns 0**; otherwise scan with `<` / `>` starting at element 0.

**Rounding**
- `Ceil(f) = (float)Math.Ceiling(f)`, `Floor(f) = (float)Math.Floor(f)`, `Round(f) = (float)Math.Round(f)`; `CeilToInt = (int)Math.Ceiling(f)`, `FloorToInt = (int)Math.Floor(f)`, `RoundToInt = (int)Math.Round(f)`.
- **`Math.Round` uses `MidpointRounding.ToEven` (banker's rounding)** [verified]: `Round(0.5) = 0`, `Round(1.5) = 2`, `Round(2.5) = 2`, `Round(-0.5) = -0` (negative zero, bits `0x80000000`), `Round(-1.5) = -2`, `Round(-2.5) = -2`; `RoundToInt` likewise (`RoundToInt(0.5) = 0`). The `(int)` casts are unchecked (NaN/∞ → `int.MinValue` on x64 .NET; unspecified in C#).
- `Sign(f) = f >= 0 ? 1 : -1` → `Sign(0) = 1`, `Sign(-0) = 1`, `Sign(NaN) = -1` [verified].

**Clamp/lerp**
- `Clamp(float value, min, max)` = `value < min ? min : (value > max ? max : value)`; `Clamp(int, …)` same; `Clamp01(v)` = `v < 0 ? 0 : (v > 1 ? 1 : v)`. NaN passes through unchanged [verified].
- `Lerp(a, b, t) = a + (b - a) * Clamp01(t)`; `LerpUnclamped(a, b, t) = a + (b - a) * t`.
- `LerpAngle(a, b, t)` – `delta = Repeat(b - a, 360); if (delta > 180) delta -= 360; return a + delta * Clamp01(t)` — result is **not** re-wrapped: `LerpAngle(350, 10, 0.5) = 360` [verified].
- `InverseLerp(a, b, value)` = `a != b ? Clamp01((value - a) / (b - a)): 0.0f` — returns **0 when `a == b`** [verified `InverseLerp(1,1,5) = 0`].
- `SmoothStep(from, to, t)` – `t = Clamp01(t); t = -2*t*t*t + 3*t*t; return to*t + from*(1 - t)`. [verified `SmoothStep(0,1,0.25) = 0.15625`.]
- `MoveTowards(current, target, maxDelta)` – `diff = target - current; if (Abs(diff) <= maxDelta) return target; return current + Sign(diff) * maxDelta`. Negative `maxDelta` moves away [verified `MoveTowards(0,10,-3) = -3`].
- `MoveTowardsAngle(current, target, maxDelta)` – `d = DeltaAngle(current, target); if (-maxDelta < d && d < maxDelta) return target; target = current + d; return MoveTowards(current, target, maxDelta)`.
- `Gamma(value, absmax, gamma)` – `negative = value < 0; absval = Abs(value); if (absval > absmax) return negative ? -absval : absval; result = Pow(absval / absmax, gamma) * absmax; return negative ? -result : result`. [verified `Gamma(0.5,1,2.2) = 0.21763763`, `Gamma(-2,1,2.2) = -2`.]
- `Approximately(a, b)` = `Abs(b - a) < Max(0.000001f * Max(Abs(a), Abs(b)), ApproxEpsilon)` with `ApproxEpsilon = Epsilon * 8`. [verified: `(1, 1.000001)` true, `(1, 1.00001)` false, `(0, 1e-44)` true, `(0, 1e-43)` false.]
- `Repeat(t, length)` = `Clamp(t - Floor(t / length) * length, 0, length)`; `Repeat(x, 0)` = NaN [verified].
- `PingPong(t, length)` – `t = Repeat(t, length * 2); return length - Abs(t - length)`. [verified `PingPong(3.5,1) = 0.5`.]
- `DeltaAngle(current, target)` – `delta = Repeat(target - current, 360); if (delta > 180) delta -= 360; return delta`. [verified `DeltaAngle(350,10) = 20`.]

**SmoothDamp (float)** – `SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime)`:
1. `smoothTime = Max(0.0001, smoothTime); omega = 2/smoothTime; x = omega*deltaTime; exp = 1/(1 + x + 0.48*x*x + 0.235*x*x*x)`.
2. `change = current - target; originalTo = target; maxChange = maxSpeed * smoothTime; change = Clamp(change, -maxChange, maxChange); target = current - change`.
3. `temp = (currentVelocity + omega*change) * deltaTime; originalVelocity = currentVelocity; currentVelocity = (currentVelocity - omega*temp) * exp; output = target + (change + temp) * exp`.
4. Overshoot: `if ((originalTo - current > 0) == (output > originalTo)) { output = originalTo; currentVelocity = deltaTime != 0 ? (output - originalTo) / deltaTime : originalVelocity; }` — the scalar version **has** a `deltaTime != 0` guard and uses a sign comparison rather than a dot product.
Overloads without `deltaTime` use `Time.deltaTime`; without `maxSpeed` use `Infinity`. [verified: `SmoothDamp(0,10, ref 0, 0.3, ∞, 0.016)` = `0.05165842`, vel `6.392509`; with `maxSpeed = 5` → `0.007748774`, vel `0.95887625`; overshoot `(0, 1, ref 100, 0.3, ∞, 0.5)` → `1`, vel `0`.]
`SmoothDampAngle(...)` – `target = current + DeltaAngle(current, target); return SmoothDamp(...)` (same overload set).

**Power-of-two**
- `NextPowerOfTwo(int v)` – `v -= 1; v |= v>>16; v |= v>>8; v |= v>>4; v |= v>>2; v |= v>>1; return v + 1`. [verified `0→0, 1→1, 5→8, 1024→1024, -3→0`.]
- `ClosestPowerOfTwo(int v)` – `next = NextPowerOfTwo(v); prev = next >> 1; return (v - prev < next - v) ? prev : next`. [verified `0→0, 3→4, 5→4, 6→8, 7→8, 12→16`.]
- `IsPowerOfTwo(int v)` = `(v & (v - 1)) == 0` → `IsPowerOfTwo(0) = true`, negative values false except patterns like `int.MinValue` [verified `0 true, 1 true, 2 true, 3 false, -2 false`].

**Internal helpers (Unity marks these `internal`; expose as internal with `InternalsVisibleTo`, or public, as NowUI needs)**
- `LineIntersection(p1, p2, p3, p4, ref Vector2 result)` – infinite lines p1→p2 and p3→p4: `bx = p2.x-p1.x; by = p2.y-p1.y; dx = p4.x-p3.x; dy = p4.y-p3.y; perp = bx*dy - by*dx; if (perp == 0) return false; cx = p3.x-p1.x; cy = p3.y-p1.y; t = (cx*dy - cy*dx) / perp; result = (p1.x + t*bx, p1.y + t*by); return true`.
- `LineSegmentIntersection(p1, p2, p3, p4, ref result)` – same, then `if (t < 0 || t > 1) return false; u = (cx*by - cy*bx) / perp; if (u < 0 || u > 1) return false;` before writing the result.
- `RandomToLong(System.Random r)` – 8 random bytes → `(long)(UInt64 & Int64.MaxValue)`.
- `ClampToFloat(double)` (±∞ preserved, otherwise clamped to `float.MinValue/MaxValue` then cast), `ClampToInt(long)`, `ClampToUInt(long)`, `ClampToShort(long)`, `ClampToUShort(long)` – saturating conversions.
- `RoundToMultipleOf(value, roundingValue)` – `roundingValue == 0 ? value : Round(value / roundingValue) * roundingValue`.
- `GetClosestPowerOfTen(positiveNumber)` – `<= 0 ? 1 : Pow(10, RoundToInt(Log10(n)))`.
- `GetNumberOfDecimalsForMinimumDifference(float)` = `Clamp(-FloorToInt(Log10(Abs(d))), 0, 15)`; double version = `(int)Max(0, -Floor(Log10(Abs(d))))`.
- `RoundBasedOnMinimumDifference(value, minDifference)` (float and double) – `minDifference == 0 ? DiscardLeastSignificantDecimal(value) : Math.Round(value, decimals, MidpointRounding.AwayFromZero)`.
- `DiscardLeastSignificantDecimal(float v)` – `decimals = Clamp((int)(5 - Log10(Abs(v))), 0, 15); return (float)Math.Round(v, decimals, AwayFromZero)`; double version uses `Max(0, …)` and returns 0 on `ArgumentOutOfRangeException`.

**Native members [verified]**
- `GammaToLinearSpace(float v)` – piecewise sRGB decode with **no clamping**: `v <= 0.04045f` → `v / 12.92f` (negatives and −∞ go through this branch: `G2L(-0.5) = -0.03869969`); `v < 1` → `pow((v + 0.055f) / 1.055f, 2.4f)`; `v >= 1` → `pow(v, 2.2f)` (`G2L(1) = 1`, `G2L(1.5) = 2.4400616`, `G2L(2) = 4.594794`); NaN → NaN; +∞ → +∞. The native `pow` is single precision and not bit-identical to either `(float)Math.Pow` or `MathF.Pow`: differences of 1–3 ulp were observed (e.g. `G2L(0.99)`: native `0.9774019 [0x3F7A3703]`, `MathF.Pow` `0x3F7A3705`, `Math.Pow` `0x3F7A3706`; exact matches at 0.001, 0.04045, 0.1, 0.5, 0.7, 1.0001, 1.5, 2, 4, 100). A shim cannot guarantee bit-equality here; use `MathF.Pow` (closest) and treat ≤3-ulp differences as expected.
- `LinearToGammaSpace(float v)` – `v <= 0` → `0` (so negatives, −0 and −∞ all give `+0`); `v <= 0.0031308f` → `12.92f * v`; `v < 1` → `1.055f * pow(v, 0.4166667f) - 0.055f`; `v >= 1` → `pow(v, 0.45454545f)` (`L2G(1) = 1`, `L2G(1.5) = 1.2023792`, `L2G(2) = 1.370351`); NaN → NaN; +∞ → +∞. Observed ≤1 ulp differences vs `MathF.Pow` (e.g. `L2G(0.5)`: native `0.7353569 [0x3F3C405A]`, candidates `0x3F3C405B`). Round trips are not exact (`G2L(L2G(0.1)) = 0.09999997`).
- `FloatToHalf(float)` → `ushort`, IEEE half with **round-to-nearest, ties away from zero** [verified: `1 + 2^-11` → `0x3C01`; `2049` → `0x6801` (2050); `1024.5` → `0x6401` (1025); `2^-25` → `0x0001`]; values ≥ 65520 → `0x7C00` (+∞); subnormals produced below 2^-14 (`6.1e-5` → `0x03FF`); `1e-8` → `0x0000`; signed zero preserved; NaN keeps sign and top mantissa bit (`0xFFC00000` → `0xFF00`).
- `HalfToFloat(ushort)` – standard decode: `0x7C00` → +∞, `0xFC00` → −∞, `0x7E00` → NaN (`0x7FC00000`), `0x0001` → 5.9604645e-8, `0x03FF` → 6.097555e-5, `0x0400` → 6.1035156e-5.
- `CorrelatedColorTemperatureToRGB(float kelvin)` – returns a `Color` with `a = 1`; **the input is clamped to [1000, 40000]** (500 K and 1000 K give identical output, as do 40000 and 50000). With `k = clamp(kelvin)/1000`, `k2 = k*k`, the following rational fits reproduce the probe to float rounding: red: `k < 6.57 ? 1 : clamp01((1.35651 + 0.216422*k + 0.000633715*k2) / (-3.24223 + 0.918711*k))`; green for `k >= 6.57`: `clamp01((1370.38 + 734.616*k + 0.689955*k2) / (-4625.69 + 1699.87*k))`; blue: `k > 6.57 ? 1 : clamp01((348.963 - 523.53*k + 183.62*k2) / (2848.82 - 214.52*k + 78.8614*k2))`. The green branch for `k < 6.57` is also a rational function but its coefficients could not be confirmed; sample values: 1000 K `(1, 0.041611645, 0.00333669)`, 1500 K `(1, 0.14693816, 0)`, 2700 K `(1, 0.3989094, 0.09633335)`, 4000 K `(1, 0.6348557, 0.3667193)`, 5000 K `(1, 0.77996707, 0.619521)`, 6500 K `(1, 0.9433625, 0.98279023)`, 8000 K `(0.7616496, 0.81257623, 1)`, 10000 K `(0.60288876, 0.7100564, 1)`, 20000 K `(0.39244252, 0.5562728, 1)`, 40000 K `(0.32911316, 0.50275207, 1)`. Output is linear-light RGB normalised so the largest channel is 1.
- `PerlinNoise(float x, float y)` and `PerlinNoise1D(float x)` – native "normalised" 2-D Perlin noise (nominally 0..1 but not strictly clamped); the reference implementation's lattice hashing is needed for equality. `PerlinNoise1D(x)` appears to equal `PerlinNoise(x, 0)` [verified for x = 1.3: both `0.7115522`]. Samples for a compatibility test: `(0,0) → 0.46527308`, `(0.5,0.5) → 0.2966959`, `(1.3,2.7) → 0.57015276`, `(10.25,3.75) → 0.559137`, `(-1.5,2.25) → 0.70068854`; `PerlinNoise1D(0.5) → 0.46527308`.

---

## 12. Vector2Int / Vector3Int

**Vector2Int** (`IEquatable<Vector2Int>, IFormattable`; private `m_X, m_Y` behind `x`, `y` properties).
- Constructor `(int x, int y)`; `Set(x, y)`; indexer 0/1 else `IndexOutOfRangeException(string.Format("Invalid Vector2Int index addressed: {0}!", index))`.
- `magnitude = (float)Math.Sqrt(x*x + y*y)` (int arithmetic inside, may overflow); `sqrMagnitude` (int).
- `Distance(a, b)` – differences converted to float then `(float)Math.Sqrt`.
- `Min`, `Max` (via `Math.Min/Max`), `Scale(a,b)`, instance `Scale`, `Clamp(min, max)` (per component `Mathf.Clamp`).
- `FloorToInt(Vector2 v)`, `CeilToInt(Vector2 v)`, `RoundToInt(Vector2 v)` – apply `Mathf.FloorToInt/CeilToInt/RoundToInt` per component (banker's rounding applies).
- Operators: unary `-`, `+`, `-`, `*` (Vector2Int,Vector2Int) componentwise, `*` (int, v), `*` (v, int), `/` (v, int) (integer division, truncating); `==`/`!=` exact; `Equals` exact; `GetHashCode() = (x * 73856093) ^ (y * 83492791)` (unchecked int) [verified `(1,2)` → 227871539].
- Conversions: `implicit Vector2(Vector2Int)`; `explicit Vector3Int(Vector2Int)` → `(x, y, 0)`.
- `ToString` template `"({0}, {1})"`, no default format.
- Statics: `zero, one, up (0,1), down (0,-1), left (-1,0), right (1,0)`.

**Vector3Int** – same pattern with z: constructors `(x,y)` (z = 0) and `(x,y,z)`; indexer message `"Invalid Vector3Int index addressed: {0}!"`; `GetHashCode()`: `yh = y.GetHashCode(); zh = z.GetHashCode(); return x.GetHashCode() ^ (yh << 4) ^ (yh >> 28) ^ (zh >> 4) ^ (zh << 28)` [verified `(1,2,3)` → 805306401]; operators `+`, `-`, `*` componentwise, unary `-`, `*` int both sides, `/` int; `==`/`!=`/`Equals` exact; `implicit Vector3(Vector3Int)`; `explicit Vector2Int(Vector3Int)` drops z; `FloorToInt/CeilToInt/RoundToInt(Vector3)`; `Distance`, `Min`, `Max`, `Scale`, `Clamp`; statics `zero, one, up, down, left, right, forward (0,0,1), back (0,0,-1)`; `ToString` `"({0}, {1}, {2})"`.

---

## 13. UnityEngine.Object semantics (for the shim's base class)

Unity's `Object` is a managed wrapper holding a native pointer (`m_CachedPtr`) and an instance id. The shim models "native object gone" with an `isDestroyed` flag. The observable rules [all verified on 6000.4.0f1]:

| Expression | Alive | Destroyed (managed reference still non-null) | Real `null` reference |
|---|---|---|---|
| `obj == null` / `null == obj` | false | **true** | true |
| `obj != null` | true | false | false |
| `(object)obj == null`, `ReferenceEquals(obj, null)` | false | **false** | true |
| `(bool)obj` (implicit) | true | **false** | false |
| `obj.Equals(null)` | false | **true** | n/a (NRE) |
| `obj.Equals(other)` (other alive, different) | false | false | — |
| `obj == obj`, `obj.Equals(obj)` | true | **true** (ids equal) | — |
| `destroyedA == destroyedB` (distinct objects) | — | **false** | — |
| `obj?.name` | name | **throws** `MissingReferenceException` (the `?.` operator sees a non-null reference) | null |
| `obj ?? fallback` | obj | **returns the destroyed obj** | fallback |
| `obj is GameObject` | true | true | false |
| `obj.GetHashCode()` | instance id | **unchanged** (same as when alive) | — |
| `obj.GetInstanceID()` | id (negative for runtime-created instances in the editor, e.g. −1468; positive for assets) | unchanged | — |
| `obj.ToString()` | `"<name> (<FullTypeName>)"`, e.g. `"ProbeGO (UnityEngine.GameObject)"`; empty name gives `" (UnityEngine.Texture2D)"` | **`"null"`** | — |
| `obj.name` (get/set), `obj.hideFlags` (get/set) | works | **throws** `UnityEngine.MissingReferenceException` with message `"The object of type 'UnityEngine.GameObject' has been destroyed but you are still trying to access it.\nYour script should either check if it is null or you should not destroy the object."` | — |

Implementation contract for the shim base class:
- Override **`operator ==(Object x, Object y)`** and **`operator !=`** with the semantics of Unity's `CompareBaseObjects`: `lhsNull = ReferenceEquals(x, null); rhsNull = ReferenceEquals(y, null); if (both null) return true; if (rhsNull) return !IsAlive(x); if (lhsNull) return !IsAlive(y); return x.instanceId == y.instanceId`. Note that two *distinct* destroyed objects compare **unequal** (ids differ) even though each equals `null`.
- **`public static implicit operator bool(Object exists)`** = `!(exists == null)`; annotate with `[NotNullWhen(true)]`/`[MaybeNullWhen(false)]` if the shim uses nullable annotations.
- Override **`Equals(object other)`**: if `other` is not null and not an `Object` return false; otherwise `CompareBaseObjects(this, other as Object)` (so `Equals(null)` is true for a destroyed object and `Equals("str")` is false).
- Override **`GetHashCode()`** to return the instance id (stable across destruction).
- `IsAlive` in Unity: true if the native pointer is non-null; otherwise, for non-`MonoBehaviour`/`ScriptableObject` types it additionally asks the native side whether an object with that id exists (asset resurrection in the editor). A shim can simply return `!isDestroyed`.
- `name` get/set and `hideFlags` get/set must throw `MissingReferenceException` (define the exception type in the shim) when destroyed; `ToString()` must return `"null"` when destroyed. `name` of a new `Texture2D` is `""`; a `Material` created from a shader takes the shader's name.
- `GetInstanceID()` returns `int`. (In the current UnityCsReference master it is `[Obsolete(error: true)]` in favour of `GetEntityId()`/`EntityId`, but in 6000.4 it is still callable; provide both if NowUI compiles against either.) Runtime-created ids are negative in the editor; a shim can hand out a decreasing negative counter.
- `Destroy(obj)` / `Destroy(obj, float t)` – schedule destruction at end of frame (after `t` seconds); `DestroyImmediate(obj)` / `DestroyImmediate(obj, bool allowDestroyingAssets)` – destroy now. Both are `static` and accept `null`/destroyed objects silently. In an engine-free shim both can mark `isDestroyed` immediately (or `Destroy` can defer to a frame-end queue if NowUI relies on same-frame access). Also present: `DontDestroyOnLoad(Object)`, `Instantiate` family, `FindObjectsByType`, etc. — not required for value-type parity.
- `HideFlags` enum (`[Flags]`): `None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8, DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52 (4|16|32), HideAndDontSave = 61 (1|4|8|16|32)`.

---

## 14. Enums

Numeric values matter only where NowUI serialises or casts them; the members are listed with Unity's exact values.

- **`ColorSpace`**: `Uninitialized = -1, Gamma = 0, Linear = 1`.
- **`FilterMode`**: `Point = 0, Bilinear = 1, Trilinear = 2`.
- **`TextureWrapMode`**: `Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3`.
- **`TextureFormat`**: `Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, RGB565 = 7, R16 = 9, DXT1 = 10, DXT5 = 12, RGBA4444 = 13, BGRA32 = 14, RHalf = 15, RGHalf = 16, RGBAHalf = 17, RFloat = 18, RGFloat = 19, RGBAFloat = 20, YUY2 = 21, RGB9e5Float = 22, BC6H = 24, BC7 = 25, BC4 = 26, BC5 = 27, DXT1Crunched = 28, DXT5Crunched = 29, PVRTC_RGB2 = 30, PVRTC_RGBA2 = 31, PVRTC_RGB4 = 32, PVRTC_RGBA4 = 33, ETC_RGB4 = 34, EAC_R = 41, EAC_R_SIGNED = 42, EAC_RG = 43, EAC_RG_SIGNED = 44, ETC2_RGB = 45, ETC2_RGBA1 = 46, ETC2_RGBA8 = 47, ASTC_4x4 = 48, ASTC_5x5 = 49, ASTC_6x6 = 50, ASTC_8x8 = 51, ASTC_10x10 = 52, ASTC_12x12 = 53, RG16 = 62, R8 = 63, ETC_RGB4Crunched = 64, ETC2_RGBA8Crunched = 65, ASTC_HDR_4x4 = 66 … ASTC_HDR_12x12 = 71, RG32 = 72, RGB48 = 73, RGBA64 = 74, R8_SIGNED = 75, RG16_SIGNED = 76, RGB24_SIGNED = 77, RGBA32_SIGNED = 78, R16_SIGNED = 79, RG32_SIGNED = 80, RGB48_SIGNED = 81, RGBA64_SIGNED = 82`; obsolete negatives `ETC_RGB4_3DS = -60, ETC_RGBA8_3DS = -61, ASTC_RGB_4x4 = -48 … ASTC_RGB_12x12 = -53, ASTC_RGBA_4x4 = -54 … ASTC_RGBA_12x12 = -59`.
- **`RenderTextureFormat`**: `ARGB32 = 0, Depth = 1, ARGBHalf = 2, Shadowmap = 3, RGB565 = 4, ARGB4444 = 5, ARGB1555 = 6, Default = 7, ARGB2101010 = 8, DefaultHDR = 9, ARGB64 = 10, ARGBFloat = 11, RGFloat = 12, RGHalf = 13, RFloat = 14, RHalf = 15, R8 = 16, ARGBInt = 17, RGInt = 18, RInt = 19, BGRA32 = 20, RGB111110Float = 22, RG32 = 23, RGBAUShort = 24, RG16 = 25, BGRA10101010_XR = 26, BGR101010_XR = 27, R16 = 28`.
- Related, in case NowUI references them: `RenderTextureReadWrite { Default = 0, Linear = 1, sRGB = 2 }`, `CubemapFace { Unknown = -1, PositiveX = 0 … NegativeZ = 5 }`, `TextureDimension` (in `UnityEngine.Rendering`), `GraphicsFormat` (large; look up on demand).

---

## 15. Notes: unverified items and doc-vs-source discrepancies

**Verification environment.** Empirical values come from a throw-away NUnit EditMode test executed once through the project's batch-mode harness on Unity 6000.4.0f1 (Windows 11 x64, Mono scripting backend); the test file and artifacts were deleted afterwards. Other Unity versions/backends (IL2CPP, ARM, Burst FTZ) may differ in the native `pow`/trig last-ulp results and in `Mathf.Epsilon`.

**Could not verify / not fully pinned**
1. `Matrix4x4.rotation` for improper matrices (det ≤ 0): data-dependent; `Scale(-1,1,1).rotation` is a 180° y-rotation, inconsistent with the "sign on x" rule that the TRS cases and `lossyScale` follow. Treat as unspecified.
2. `Matrix4x4.inverse` bit pattern (elimination order/pivoting) and native `pow`/`sin`/`cos` rounding (1–3 ulp deviations observed) — exact reproduction is not achievable from the outside; golden tests should tolerate these.
3. `Quaternion.eulerAngles` singular-branch threshold and the exact formula variant; `Vector3.Slerp`/`RotateTowards` thresholds for "nearly parallel/antiparallel" and the perpendicular-axis rule (reconstructed from samples only).
4. `CorrelatedColorTemperatureToRGB` green fit for k < 6.57 (coefficients unknown; samples given). `PerlinNoise` requires the reference lattice/hash; only samples are given.
5. `Matrix4x4.isIdentity` at exactly 1e-5 deviation (strict `<` assumed). `Bounds.Contains` NaN behaviour beyond the x component.
6. `LookRotation` with a zero `up` vector (only tested with `forward = +z`, which is identity regardless).
7. `Mathf.Epsilon` on FTZ platforms — behaviour follows from the source but was only measured on desktop (`float.Epsilon`).

**Places where the docs and the source/behaviour differ**
1. `Color.yellow` docs print `RGBA(1, 0.92, 0.016, 1)`; the source is `(1, 235/255, 4/255, 1)` = `(1, 0.92156863, 0.015686275, 1)`.
2. `Matrix4x4.ValidTRS` docs say "checks if this matrix is a valid transform matrix"; it only checks that the bottom row is exactly `(0,0,0,1)` (shear and zero scale pass).
3. `Matrix4x4.isIdentity` docs describe exact 1/0 content; the implementation is tolerant (≈1e-5).
4. `Matrix4x4.LookAt` docs state equivalence with `TRS(from, LookRotation(to-from, up), one)`; true to ~6e-8 except when forward is parallel to up, where `LookAt` returns the identity rotation but `LookRotation` does not.
5. `Mathf.CorrelatedColorTemperatureToRGB` docs say the temperature "must fall between 1000 and 40000"; the implementation clamps silently.
6. `Quaternion.AngleAxis` docs say the axis magnitude is ignored (true) but not that a zero/NaN/≤1e-6-magnitude axis yields identity.
7. `Quaternion.Inverse` docs do not mention it; it is a plain conjugate without normalisation.
8. `Bounds.Contains` docs: boundary points inside and negative extents → false are both confirmed; NaN → true is undocumented.
9. `Color32` conversion docs do not state the rounding rule; the source uses `Round(Clamp01(x)*255)` with banker's rounding (`127.5 → 128`, `254.5 → 254`, `0.5 → 0`).
10. `Mathf.LerpAngle`/`MoveTowardsAngle` results are not re-wrapped into [0,360) (`LerpAngle(350,10,0.5) = 360`).
11. `Mathf.Round(-0.5f)` returns negative zero.
12. `Color.HSVToRGB` with `H*6` outside `[-1, 7)` returns black (alpha 1) rather than wrapping hue.
13. `Color32.Lerp` truncates (`Lerp(0,255,0.5) = 127`), and `LerpUnclamped` wraps through the unchecked byte cast (`-50 → 206`).
14. `Mathf.LineIntersection`/`LineSegmentIntersection` are `internal` in Unity, not public.
15. `Object.GetInstanceID` is obsolete-as-error in current UnityCsReference master (replaced by `GetEntityId()`), but still present and functional in 6000.4.
16. `Vector2.Angle` tests `sqrMag(a)*sqrMag(b) < 1e-30` before the square root, whereas `Vector3.Angle` tests `sqrt(...) < 1e-15` — same mathematics, different underflow behaviour for tiny vectors.
17. The vector `SmoothDamp` overshoot branch divides by `deltaTime` unguarded (NaN when `deltaTime == 0`); the scalar `Mathf.SmoothDamp` keeps the original velocity in that case.

**Deliberately not used.** A public git mirror of leaked Unity 4.x native source (`Runtime/Math/Quaternion.cpp` etc.) exists online; it was not opened, so every native statement above rests on the Scripting API docs plus the black-box probe.