// Mirrors UnityEngine.LayerMask for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.1; behaviour: Docs/Standalone/GradientCurveSemantics.md §7.
// The 32-entry layer-name table lives on the host (INowHostServices.layerNames), because in Unity it is project
// data, not engine data; DefaultHostServices ships Unity's default project table.
using System;
using System.Runtime.InteropServices;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A bitmask over the 32 layers, plus the static name/index lookups that read the host's layer table.
    /// <para>
    /// Deliberately <b>not</b> <c>[Serializable]</c> and with <b>no</b> <c>ToString</c> override: that is what
    /// 6000.4 ships (GC §7), so <c>((LayerMask)5).ToString()</c> is "UnityEngine.LayerMask", not "5". Unity also
    /// leaves <c>Equals</c>/<c>GetHashCode</c> to <c>ValueType</c>; both are overridden here over the single field
    /// so hashing is deterministic instead of depending on the runtime's value-type hash.
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct LayerMask
    {
        private int m_Mask;

        public int value
        {
            readonly get => m_Mask;
            set => m_Mask = value;
        }

        public static implicit operator int(LayerMask mask)
        {
            return mask.m_Mask;
        }

        public static implicit operator LayerMask(int intVal)
        {
            LayerMask mask = default;
            mask.m_Mask = intVal;
            return mask;
        }

        /// <summary>The host's layer table, never null; a short or missing table simply yields empty names.</summary>
        private static string[] Table()
        {
            INowHostServices host = NowRuntime.host;
            string[] names = host?.layerNames;
            return names ?? Array.Empty<string>();
        }

        /// <summary>
        /// The name of a layer, or "" for an unnamed or out-of-range layer. Never throws: -1, 32, 100 and
        /// int.MinValue all return "" (GC §7), because callers enumerate layer indices blindly.
        /// </summary>
        public static string LayerToName(int layer)
        {
            if (layer < 0 || layer >= 32)
                return string.Empty;

            string[] names = Table();
            if (layer >= names.Length)
                return string.Empty;

            return names[layer] ?? string.Empty;
        }

        /// <summary>
        /// The index of a layer by name: ordinal, case-sensitive and untrimmed, so "default", " Default" and "ui"
        /// are all unknown and return -1.
        /// <para>
        /// Unity's quirk, reproduced here: "" and null match the first <i>empty-named</i> layer, which is 3 with the
        /// default project table. It falls out of comparing against the table as-is rather than being special-cased,
        /// so a host whose table has no empty slot gets -1 instead.
        /// </para>
        /// </summary>
        public static int NameToLayer(string layerName)
        {
            // A null name compares as the empty string, which is how the native lookup behaves.
            string wanted = layerName ?? string.Empty;

            string[] names = Table();
            int count = names.Length < 32 ? names.Length : 32;
            for (int i = 0; i < count; i++)
            {
                string candidate = names[i] ?? string.Empty;
                if (string.Equals(candidate, wanted, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// ORs 1 &lt;&lt; NameToLayer(name) for every name that resolves, skipping the ones that do not. So
        /// GetMask("Default", "UI") is 33, GetMask("nope") is 0, GetMask() is 0, and — through the ""/null quirk —
        /// GetMask(new string[] { null }) is 8. A null array throws ArgumentNullException("layerNames").
        /// </summary>
        public static int GetMask(params string[] layerNames)
        {
            if (layerNames == null)
                throw new ArgumentNullException(nameof(layerNames));

            int mask = 0;
            for (int i = 0; i < layerNames.Length; i++)
            {
                int layer = NameToLayer(layerNames[i]);
                if (layer != -1)
                    mask |= 1 << layer;
            }

            return mask;
        }

        public override readonly bool Equals(object other)
        {
            return other is LayerMask mask && mask.m_Mask == m_Mask;
        }

        public override readonly int GetHashCode()
        {
            return m_Mask.GetHashCode();
        }
    }
}
