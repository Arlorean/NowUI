// Mirrors UnityEngine.Resources.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.7 member list; §4.4 INowResourceProvider).
// Behaviour spec: Docs/Standalone/UnityDependencyInventory.md (§A.2 "UnityEngine.Resources", §G.3 resource paths).
using System;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// Asset lookup by Unity resource path (<c>"NowUI/UIMaterial"</c>, <c>"NowUI/NotoSans"</c>, …), served by the
    /// host's <c>INowResourceProvider</c>. A provider must return the <b>same instance</b> for the same path: core
    /// types cache materials and fonts by reference and compare them with <c>==</c>.
    /// </summary>
    public static class Resources
    {
        /// <summary>
        /// Loads the asset at <paramref name="path"/> if it is a <typeparamref name="T"/>, else null.
        /// </summary>
        /// <remarks>
        /// The type test is done here rather than trusted to the provider, because Unity's <c>Load&lt;T&gt;</c> returns
        /// null (it does not throw) for an asset of the wrong type, and core code relies on that to probe a path.
        /// </remarks>
        public static T Load<T>(string path) where T : Object
        {
            return NowRuntime.host.resources.Load(path, typeof(T)) as T;
        }

        /// <summary>Loads the asset at <paramref name="path"/> whatever its type.</summary>
        public static Object Load(string path)
        {
            return NowRuntime.host.resources.Load(path, typeof(Object));
        }

        /// <summary>Loads the asset at <paramref name="path"/>, asking the provider for a specific type.</summary>
        public static Object Load(string path, Type systemTypeInstance)
        {
            return NowRuntime.host.resources.Load(path, systemTypeInstance);
        }

        /// <summary>
        /// Every <typeparamref name="T"/> at <paramref name="path"/>.
        /// </summary>
        /// <remarks>
        /// Unity returns all sub-assets at the path; the host contract (§4.4) exposes only a single-object
        /// <c>Load</c>, so this reports the one asset there is, or an empty array. Widening it would mean widening
        /// <c>INowResourceProvider</c>, which is another unit's file and is not required by anything NowUI calls
        /// (§A.2 lists only <c>Load&lt;T&gt;</c>).
        /// </remarks>
        public static T[] LoadAll<T>(string path) where T : Object
        {
            T single = NowRuntime.host.resources.Load(path, typeof(T)) as T;

            // `is null` rather than `== null`: on a type parameter C# ignores Object's fake-null operator and does a
            // reference comparison anyway, so writing the reference comparison is the honest spelling of what happens.
            if (single is null)
                return Array.Empty<T>();

            return new[] { single };
        }

        /// <summary>
        /// Unloads a single asset.
        /// </summary>
        /// <remarks>
        /// A no-op. In the standalone build the backend's copy of an asset is released by <c>Object.Destroy</c>, and
        /// a resource provider owns its instances for the process's lifetime (it must keep returning the same one).
        /// Freeing it here would hand the next caller a destroyed object.
        /// </remarks>
        public static void UnloadAsset(Object assetToUnload)
        {
        }

        /// <summary>
        /// Unloads assets nothing references.
        /// </summary>
        /// <remarks>
        /// A no-op, and deliberately not a <c>GC.Collect()</c>: the shim owns no asset graph to sweep, and a forced
        /// collection on a path core code calls between frames would be a frame-time spike the Unity build does not
        /// have.
        /// </remarks>
        public static void UnloadUnusedAssets()
        {
        }
    }
}
