// Mirrors UnityEngine.ExpressionEvaluator.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 - the body is specified there verbatim; §12.5 acceptance risk).
// Behaviour spec: Docs/Standalone/GradientCurveSemantics.md §8 (the full algorithm, if it ever has to be ported).
namespace UnityEngine
{
    /// <summary>
    /// Unity's arithmetic-expression parser. A class with static members, not a static class, exactly as in Unity.
    /// </summary>
    /// <remarks>
    /// Deliberately always fails (design §3.7). <c>NowNumericExpression</c> is the only caller and treats
    /// <c>false</c> as "use my own parser", which is a complete bounded evaluator its 61 tests already exercise -
    /// so the shim's answer routes every expression through one implementation instead of two.
    /// <para>
    /// Design §12.5 records the acceptance risk this carries: some of those 61 golden values were captured from
    /// Unity's evaluator, and if the managed fallback disagrees on any of them, the fix is to port the evaluator here
    /// (GC §8 specifies it completely, ~300 LOC) and <b>not</b> to edit a test. U27 is where that is measured.
    /// </para>
    /// </remarks>
    public class ExpressionEvaluator
    {
        /// <summary>
        /// Always returns false with <paramref name="value"/> left at its default.
        /// </summary>
        public static bool Evaluate<T>(string expression, out T value)
        {
            value = default;
            return false;
        }
    }
}
