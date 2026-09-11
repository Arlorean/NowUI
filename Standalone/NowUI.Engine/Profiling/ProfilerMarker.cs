// Mirrors Unity.Profiling.ProfilerMarker, ProfilerMarker.AutoScope and ProfilerCategory.
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9. The members NowUI calls are listed in
// Docs/Standalone/UnityDependencyInventory.md section A row "Unity.Profiling.ProfilerMarker": the (string)
// constructor, Auto() returning AutoScope, and Begin()/End().
//
// Two shape requirements come straight from the NowUI sources and are not negotiable:
//   * ProfilerMarker must be PUBLIC, because NowProfiler.cs declares `public static readonly ProfilerMarker`
//     fields (NowProfiler.cs:14-59) and a public field cannot have a less accessible type.
//   * AutoScope must be a PUBLIC READONLY STRUCT, because NowEffects.cs stores one in a public struct field
//     (NowEffects.cs:756, :867), assigns `default` to it (:254, :307) and passes it by value (:771, :880).
//     A class would allocate per scope and a `default` class field would null-ref on Dispose.
//
// UnityEngine.Profiling.Recorder is deliberately NOT shipped here: design section 3.9 places it in
// Standalone/Tests/Shims/Recorder.cs because only NowBenchmarkAllocations needs it.

using System;
using NowUI.Engine;

namespace Unity.Profiling
{
    /// <summary>
    /// Mirrors <c>Unity.Profiling.ProfilerCategory</c>. Categories are inert labels in the shim; they exist so
    /// the two-argument <see cref="ProfilerMarker"/> constructor compiles.
    /// </summary>
    public readonly struct ProfilerCategory : IEquatable<ProfilerCategory>
    {
        readonly string name;

        ProfilerCategory(string name) => this.name = name;

        /// <summary>The category name, or <c>"Scripts"</c> for a default-constructed value, as in Unity.</summary>
        public string Name => name ?? "Scripts";

        public static ProfilerCategory Scripts => new ProfilerCategory("Scripts");

        public static ProfilerCategory Render => new ProfilerCategory("Render");

        public static ProfilerCategory Gui => new ProfilerCategory("GUI");

        public static ProfilerCategory Internal => new ProfilerCategory("Internal");

        public bool Equals(ProfilerCategory other) => string.Equals(Name, other.Name, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ProfilerCategory other && Equals(other);

        public override int GetHashCode() => Name.GetHashCode();

        public override string ToString() => Name;

        public static bool operator ==(ProfilerCategory left, ProfilerCategory right) => left.Equals(right);

        public static bool operator !=(ProfilerCategory left, ProfilerCategory right) => !left.Equals(right);
    }

    /// <summary>
    /// Mirrors <c>Unity.Profiling.ProfilerMarker</c>.
    /// </summary>
    /// <remarks>
    /// The whole type is one string reference. Constructing a marker allocates nothing beyond the name the caller
    /// already had, <see cref="Auto"/> returns a struct by value, and <see cref="Begin"/>/<see cref="End"/> are a
    /// static read plus a null test while no sink is installed — so the markers scattered through NowUI's hot paths
    /// cost nothing in the standalone build, which is what design section 1.2's no-steady-state-allocation rule
    /// requires. Static readonly marker fields are also safe to initialise before any backend exists
    /// (hazard D.1 #4), because nothing here touches the render context.
    /// </remarks>
    public readonly struct ProfilerMarker : IEquatable<ProfilerMarker>
    {
        readonly string name;

        /// <summary>Declares a marker named <paramref name="name"/>.</summary>
        public ProfilerMarker(string name) => this.name = name;

        /// <summary>
        /// Declares a marker in a category. The category is accepted and dropped: the shim has one flat marker
        /// namespace.
        /// </summary>
        public ProfilerMarker(ProfilerCategory category, string name) => this.name = name;

        /// <summary>The marker's name, or null for a default-constructed marker.</summary>
        public string Name => name;

        /// <summary>
        /// Opens a sample, routed to <c>NowRuntime.profilerSink</c> per design section 3.9.
        /// </summary>
        /// <remarks>
        /// The sink is null by default, so the whole call is a static field read and a null test — free enough for
        /// the hot paths NowUI marks up, and it allocates nothing either way (the name string already exists and
        /// the sink is an interface reference, so there is no boxing).
        /// </remarks>
        public void Begin() => NowRuntime.profilerSink?.Begin(name);

        /// <summary>Closes a sample. See <see cref="Begin"/>.</summary>
        public void End() => NowRuntime.profilerSink?.End(name);

        /// <summary>
        /// Opens a sample and returns a scope that closes it on <see cref="AutoScope.Dispose"/>.
        /// </summary>
        public AutoScope Auto()
        {
            Begin();
            return new AutoScope(name);
        }

        public bool Equals(ProfilerMarker other) => string.Equals(name, other.name, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ProfilerMarker other && Equals(other);

        public override int GetHashCode() => name == null ? 0 : name.GetHashCode();

        public override string ToString() => name ?? string.Empty;

        public static bool operator ==(ProfilerMarker left, ProfilerMarker right) => left.Equals(right);

        public static bool operator !=(ProfilerMarker left, ProfilerMarker right) => !left.Equals(right);

        /// <summary>
        /// Mirrors <c>Unity.Profiling.ProfilerMarker.AutoScope</c>: the <c>using</c> scope returned by
        /// <see cref="Auto"/>.
        /// </summary>
        /// <remarks>
        /// A <c>default</c> AutoScope carries a null name and its <see cref="Dispose"/> is a no-op, which is what
        /// makes NowEffects.cs's `AutoScope captureProfile = default;` pattern safe.
        /// </remarks>
        public readonly struct AutoScope : IDisposable
        {
            readonly string name;

            internal AutoScope(string name) => this.name = name;

            /// <summary>Closes the sample opened by <see cref="Auto"/>.</summary>
            public void Dispose() => NowRuntime.profilerSink?.End(name);
        }
    }
}
