// Mirrors Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility and Unity.Collections.LowLevel.Unsafe.UnsafeUtility.
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9; the members NowUI actually calls are listed in
// Docs/Standalone/UnityDependencyInventory.md section A ("GetUnsafePtr", "MemCpy", "As<TFrom,TTo>").
//
// Everything here is implemented over System.Runtime.CompilerServices.Unsafe, System.Buffer and
// System.Runtime.InteropServices.NativeMemory — no P/Invoke, no allocator of our own.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// WHY the alias: this file's own namespace ends in "Unsafe", so the bare name `Unsafe` binds to
// Unity.Collections.LowLevel.Unsafe (a member namespace of the enclosing Unity.Collections.LowLevel) rather than to
// System.Runtime.CompilerServices.Unsafe. The alias sidesteps that shadowing.
using SysUnsafe = System.Runtime.CompilerServices.Unsafe;

namespace Unity.Collections.LowLevel.Unsafe
{
    /// <summary>
    /// Mirrors <c>Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility</c>.
    /// </summary>
    public static unsafe class NativeArrayUnsafeUtility
    {
        /// <summary>
        /// The address of element 0. WHY this is safe without pinning: owned <see cref="NativeArray{T}"/> buffers
        /// are allocated on the Pinned Object Heap, so the GC never relocates them and the pointer stays valid for
        /// as long as the array lives. Views must be handed a POH buffer by their owner for the same reason.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* GetUnsafePtr<T>(NativeArray<T> nativeArray) where T : struct
        {
            nativeArray.CheckCreated();
            return SysUnsafe.AsPointer(ref nativeArray.FirstElementRef());
        }

        /// <summary>
        /// The same address as <see cref="GetUnsafePtr{T}"/>. The distinction only exists in Unity so the job
        /// safety system can record the intended access; the shim has no safety system.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* GetUnsafeReadOnlyPtr<T>(NativeArray<T> nativeArray) where T : struct
        {
            nativeArray.CheckCreated();
            return SysUnsafe.AsPointer(ref nativeArray.FirstElementRef());
        }

        /// <summary>The same address again, skipping the (nonexistent) safety handle checks.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* GetUnsafeBufferPointerWithoutChecks<T>(NativeArray<T> nativeArray) where T : struct
        {
            nativeArray.CheckCreated();
            return SysUnsafe.AsPointer(ref nativeArray.FirstElementRef());
        }

        /// <summary>
        /// Not supported by the standalone shim.
        /// </summary>
        /// <remarks>
        /// WHY: design section 3.9 fixes <see cref="NativeArray{T}"/>'s storage at
        /// <c>(byte[] buffer, int byteOffset, int length)</c>. A managed array cannot alias arbitrary native
        /// memory, and adding a raw-pointer field would put a branch on every indexer access for a member no
        /// NowUI source calls (verified across Assets/NowUI). Copying the data instead would look like it worked
        /// while silently dropping writes, which is worse than failing loudly.
        /// </remarks>
        public static NativeArray<T> ConvertExistingDataToNativeArray<T>(void* dataPointer, int length, Allocator allocator) where T : struct =>
            throw new NotSupportedException(
                "NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray is not supported by NowUI.Engine: the shim's NativeArray<T> is backed by a managed byte[] and cannot alias native memory. Copy into a new NativeArray<T> instead.");

        /// <summary>
        /// No-op. Unity attaches an <c>AtomicSafetyHandle</c> here; the shim has none, but the method exists so
        /// host code written against Unity still compiles.
        /// </summary>
        public static void SetAtomicSafetyHandle<T>(ref NativeArray<T> nativeArray, object safety) where T : struct
        {
        }
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.LowLevel.Unsafe.UnsafeUtility</c>.
    /// </summary>
    public static unsafe class UnsafeUtility
    {
        /// <summary>Non-overlapping byte copy (memcpy semantics).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCpy(void* destination, void* source, long size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            if (size == 0)
                return;

            NativeMemory.Copy(source, destination, (nuint)size);
        }

        /// <summary>Byte copy that tolerates overlapping regions (memmove semantics).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemMove(void* destination, void* source, long size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            if (size == 0)
                return;

            // Buffer.MemoryCopy is documented to handle overlapping source and destination.
            Buffer.MemoryCopy(source, destination, size, size);
        }

        /// <summary>Writes <paramref name="size"/> zero bytes at <paramref name="destination"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemClear(void* destination, long size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            if (size == 0)
                return;

            NativeMemory.Clear(destination, (nuint)size);
        }

        /// <summary>Writes <paramref name="size"/> copies of <paramref name="value"/> at <paramref name="destination"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemSet(void* destination, byte value, long size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            if (size == 0)
                return;

            NativeMemory.Fill(destination, (nuint)size, value);
        }

        /// <summary>Compares two byte ranges; 0 when equal, matching Unity's memcmp-style result.</summary>
        public static int MemCmp(void* left, void* right, long size)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            var a = new ReadOnlySpan<byte>(left, (int)size);
            var b = new ReadOnlySpan<byte>(right, (int)size);
            return a.SequenceCompareTo(b);
        }

        /// <summary>The size in bytes of one <typeparamref name="T"/>, as laid out in an array.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>() where T : struct => SysUnsafe.SizeOf<T>();

        /// <summary>
        /// The alignment <typeparamref name="T"/> requires, derived from the padding the runtime inserts in
        /// <c>struct { byte pad; T value; }</c> — the standard trick, since C# has no <c>alignof</c>.
        /// </summary>
        public static int AlignOf<T>() where T : struct => SysUnsafe.SizeOf<AlignOfHelper<T>>() - SysUnsafe.SizeOf<T>();

        /// <summary>
        /// Reinterprets a reference. NowValueControls.cs uses this to read an enum's underlying integer without
        /// boxing; it is a pure type-system operation and compiles away.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref TTo As<TFrom, TTo>(ref TFrom from)
            where TFrom : struct
            where TTo : struct
            => ref SysUnsafe.As<TFrom, TTo>(ref from);

        /// <summary>Reinterprets a raw address as a managed reference.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ref T AsRef<T>(void* pointer) where T : struct => ref SysUnsafe.AsRef<T>(pointer);

        /// <summary>
        /// The address of <paramref name="output"/>. The caller is responsible for keeping the storage alive and
        /// unmoved for as long as the pointer is used, exactly as in Unity.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* AddressOf<T>(ref T output) where T : struct => SysUnsafe.AsPointer(ref output);

        /// <summary>Reads one <typeparamref name="T"/> from a possibly unaligned address.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ReadArrayElement<T>(void* source, int index) where T : struct =>
            SysUnsafe.ReadUnaligned<T>((byte*)source + (long)index * SysUnsafe.SizeOf<T>());

        /// <summary>Writes one <typeparamref name="T"/> to a possibly unaligned address.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteArrayElement<T>(void* destination, int index, T value) where T : struct =>
            SysUnsafe.WriteUnaligned((byte*)destination + (long)index * SysUnsafe.SizeOf<T>(), value);

        /// <summary>
        /// Aligned native allocation. The shim keeps <see cref="NativeArray{T}"/> on managed memory, so this
        /// exists only for host code that asks for raw scratch; the <paramref name="allocator"/> is recorded by
        /// the caller, not by us.
        /// </summary>
        public static void* Malloc(long size, int alignment, Allocator allocator)
        {
            if (size < 0)
                throw new ArgumentOutOfRangeException(nameof(size), "Size must be >= 0.");

            if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
                throw new ArgumentException("Alignment must be a positive power of two.", nameof(alignment));

            return NativeMemory.AlignedAlloc((nuint)size, (nuint)alignment);
        }

        /// <summary>Frees a block returned by <see cref="Malloc"/>.</summary>
        public static void Free(void* memory, Allocator allocator)
        {
            if (memory != null)
                NativeMemory.AlignedFree(memory);
        }

        /// <summary>True when <typeparamref name="T"/> can be memcpy'd, i.e. contains no managed references.</summary>
        public static bool IsBlittable<T>() where T : struct => !RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        [StructLayout(LayoutKind.Sequential)]
        struct AlignOfHelper<T> where T : struct
        {
#pragma warning disable CS0169 // the field exists only so the runtime inserts T's alignment padding after it
            byte pad;
            T value;
#pragma warning restore CS0169
        }
    }
}
