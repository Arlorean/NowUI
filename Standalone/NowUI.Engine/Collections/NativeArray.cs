// Mirrors Unity.Collections.NativeArray<T>, NativeList<T>, Allocator, NativeArrayOptions and the job-safety
// attributes (ReadOnly, WriteOnly, NativeDisableParallelForRestriction, DeallocateOnJobCompletion).
//
// Governed by Docs/Standalone/StandaloneCoreDesign.md section 3.9 (shim specification) and section 12.6 (float
// policy); the member list NowUI actually calls is Docs/Standalone/UnityDependencyInventory.md section A rows
// "Unity.Collections.NativeArray<T>" and "Unity.Collections.* (package)".
//
// WHY a managed backing store: the standalone build has no native allocator. Owned buffers come from the Pinned
// Object Heap (GC.AllocateUninitializedArray(pinned: true)) so the GC never moves them and
// NativeArrayUnsafeUtility.GetUnsafePtr can hand out a raw pointer that stays valid for the array's lifetime with
// no GCHandle and no pin/unpin cost. Views (Texture2D.GetRawTextureData<T>) alias a host-owned byte[] instead of
// owning one, which is why the storage is (buffer, byteOffset, length) rather than a pointer.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Unity.Collections
{
    /// <summary>
    /// Mirrors <c>Unity.Collections.Allocator</c>. The numeric values match Unity so code that serialises or
    /// compares them keeps working; the standalone shim treats every allocator the same because the backing store
    /// is always managed memory.
    /// </summary>
    public enum Allocator
    {
        Invalid = 0,
        None = 1,
        Temp = 2,
        TempJob = 3,
        Persistent = 4,
        AudioKernel = 5,
    }

    /// <summary>Mirrors <c>Unity.Collections.NativeArrayOptions</c>.</summary>
    public enum NativeArrayOptions
    {
        UninitializedMemory = 0,
        ClearMemory = 1,
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.ReadOnlyAttribute</c>. Purely declarative here: the shim runs every job
    /// sequentially on the calling thread, so there is no safety system to inform.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class ReadOnlyAttribute : Attribute
    {
    }

    /// <summary>Mirrors <c>Unity.Collections.WriteOnlyAttribute</c>. Declarative only.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class WriteOnlyAttribute : Attribute
    {
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.NativeDisableParallelForRestrictionAttribute</c>. Declarative only.
    /// WHY it lives in <c>Unity.Collections</c> and not <c>Unity.Collections.LowLevel.Unsafe</c>:
    /// NowManagedFontBaker.cs applies <c>[NativeDisableParallelForRestriction]</c> with only
    /// <c>using Unity.Collections;</c> in scope, so this is the namespace the sources resolve it from.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class NativeDisableParallelForRestrictionAttribute : Attribute
    {
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.DeallocateOnJobCompletionAttribute</c>. Declarative only — the shim never
    /// disposes a job field, because jobs complete before <c>Schedule</c> returns and the caller still owns the
    /// buffers.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class DeallocateOnJobCompletionAttribute : Attribute
    {
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.NativeArray&lt;T&gt;</c> (the CoreModule type, not the package one).
    /// </summary>
    /// <remarks>
    /// Storage is <c>byte[] buffer</c> + <c>byteOffset</c> + <c>length</c> exactly as design section 3.9
    /// specifies. Sub-arrays, reinterpretations and host views share the buffer and differ only in the offset and
    /// length, which is what makes <c>Texture2D.GetRawTextureData&lt;T&gt;()</c> an aliasing view rather than a
    /// copy.
    /// </remarks>
    public struct NativeArray<T> : IDisposable, IEnumerable<T>, IEquatable<NativeArray<T>> where T : struct
    {
        // Per-instantiation constant; the JIT folds it into a literal.
        internal static readonly int ElementSize = Unsafe.SizeOf<T>();

        internal byte[] buffer;
        internal int byteOffset;
        internal int length;

        /// <summary>
        /// Allocates <paramref name="length"/> elements. <see cref="NativeArrayOptions.ClearMemory"/> zeroes the
        /// buffer, matching Unity's default.
        /// </summary>
        public NativeArray(int length, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            CheckElementType();

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent.", nameof(allocator));

            // A zero-length NativeArray is still "created" in Unity and still yields a usable pointer, so never
            // leave the buffer null: IsCreated is defined as buffer != null.
            int bytes = checked(length * ElementSize);
            this.buffer = options == NativeArrayOptions.ClearMemory
                ? GC.AllocateArray<byte>(bytes == 0 ? 1 : bytes, pinned: true)
                : GC.AllocateUninitializedArray<byte>(bytes == 0 ? 1 : bytes, pinned: true);
            this.byteOffset = 0;
            this.length = length;
        }

        /// <summary>Allocates a copy of <paramref name="array"/>.</summary>
        public NativeArray(T[] array, Allocator allocator)
            : this(array != null ? array.Length : throw new ArgumentNullException(nameof(array)), allocator, NativeArrayOptions.UninitializedMemory)
        {
            CopyFrom(array);
        }

        /// <summary>Allocates a copy of <paramref name="array"/>.</summary>
        public NativeArray(NativeArray<T> array, Allocator allocator)
            : this(array.IsCreated ? array.length : throw new ArgumentException("The source array has not been created.", nameof(array)), allocator, NativeArrayOptions.UninitializedMemory)
        {
            CopyFrom(array);
        }

        NativeArray(byte[] backing, int byteOffset, int length)
        {
            this.buffer = backing;
            this.byteOffset = byteOffset;
            this.length = length;
        }

        /// <summary>
        /// Builds a view over a host-owned buffer without copying — used by <c>Texture2D.GetRawTextureData&lt;T&gt;()</c>
        /// and by <see cref="NativeList{T}.AsArray"/>. The caller keeps ownership: <see cref="Dispose"/> on the view
        /// does not touch <paramref name="backing"/>.
        /// </summary>
        /// <remarks>
        /// <paramref name="backing"/> must be a Pinned Object Heap array if the caller intends to take a pointer
        /// through <c>NativeArrayUnsafeUtility.GetUnsafePtr</c>, and <paramref name="byteOffset"/> must keep the
        /// natural alignment of <typeparamref name="T"/>.
        /// </remarks>
        internal static NativeArray<T> CreateView(byte[] backing, int byteOffset, int length)
        {
            CheckElementType();

            if (backing == null)
                throw new ArgumentNullException(nameof(backing));

            if (byteOffset < 0 || length < 0 || (long)byteOffset + (long)length * ElementSize > backing.Length)
                throw new ArgumentOutOfRangeException(nameof(length), "The view does not fit inside the backing buffer.");

            return new NativeArray<T>(backing, byteOffset, length);
        }

        /// <summary>Element count.</summary>
        public int Length => length;

        /// <summary>True while the array has a backing buffer, i.e. between construction and <see cref="Dispose"/>.</summary>
        public bool IsCreated => buffer != null;

        public T this[int index]
        {
            get
            {
                CheckIndex(index);
                return ElementRef(index);
            }
            set
            {
                CheckIndex(index);
                ElementRef(index) = value;
            }
        }

        /// <summary>
        /// Releases the array. WHY this never throws for <see cref="Allocator.None"/> the way Unity does: the
        /// backing store is managed, so "freeing" is only dropping the reference and letting the GC reclaim it.
        /// Making a view's Dispose fatal would turn a harmless call into a crash in the compile-driven bring-up.
        /// </summary>
        public void Dispose()
        {
            buffer = null;
            byteOffset = 0;
            length = 0;
        }

        /// <summary>A span over the elements. Writes through it are visible through the indexer and the pointer.</summary>
        public Span<T> AsSpan()
        {
            CheckCreated();
            return MemoryMarshal.CreateSpan(ref FirstElementRef(), length);
        }

        /// <summary>A read-only span over the elements.</summary>
        public ReadOnlySpan<T> AsReadOnlySpan()
        {
            CheckCreated();
            return MemoryMarshal.CreateReadOnlySpan(ref FirstElementRef(), length);
        }

        /// <summary>Copies the contents into a fresh managed array.</summary>
        public T[] ToArray()
        {
            CheckCreated();

            if (length == 0)
                return Array.Empty<T>();

            var result = new T[length];
            AsReadOnlySpan().CopyTo(result);
            return result;
        }

        public void CopyFrom(T[] array)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Copy(array, 0, this, 0, array.Length);
        }

        public void CopyFrom(NativeArray<T> array) => Copy(array, 0, this, 0, array.Length);

        public void CopyTo(T[] array)
        {
            if (array == null)
                throw new ArgumentNullException(nameof(array));

            Copy(this, 0, array, 0, length);
        }

        public void CopyTo(NativeArray<T> array) => Copy(this, 0, array, 0, length);

        /// <summary>
        /// Reinterprets the buffer as <typeparamref name="U"/>. <paramref name="expectedTypeSize"/> is Unity's
        /// guard against a silently changed <typeparamref name="T"/>: it must equal <c>sizeof(T)</c>.
        /// </summary>
        public NativeArray<U> Reinterpret<U>(int expectedTypeSize) where U : struct
        {
            CheckCreated();

            if (expectedTypeSize != ElementSize)
                throw new InvalidOperationException($"Type {typeof(T)} was expected to be {expectedTypeSize} bytes but is {ElementSize} bytes.");

            long totalBytes = (long)length * ElementSize;
            int destinationSize = NativeArray<U>.ElementSize;

            if (totalBytes % destinationSize != 0)
                throw new InvalidOperationException($"Types {typeof(T)} ({ElementSize} bytes) and {typeof(U)} ({destinationSize} bytes) do not line up: {totalBytes} bytes is not a whole number of {typeof(U)}.");

            return NativeArray<U>.CreateView(buffer, byteOffset, (int)(totalBytes / destinationSize));
        }

        /// <summary>An aliasing view of <paramref name="length"/> elements starting at <paramref name="start"/>.</summary>
        public NativeArray<T> GetSubArray(int start, int length)
        {
            CheckCreated();

            if (start < 0 || length < 0 || start + length > this.length)
                throw new ArgumentOutOfRangeException(nameof(length), "The sub-array does not fit inside the array.");

            return new NativeArray<T>(buffer, byteOffset + start * ElementSize, length);
        }

        // ---- the static Copy overload matrix (design section 3.9) --------------------------------------------

        public static void Copy(NativeArray<T> src, NativeArray<T> dst)
        {
            CheckSameLength(src.Length, dst.Length);
            Copy(src, 0, dst, 0, src.Length);
        }

        public static void Copy(NativeArray<T> src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);

        public static void Copy(NativeArray<T> src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            src.CheckCreated();
            dst.CheckCreated();
            CheckCopyRange(src.Length, srcIndex, dst.Length, dstIndex, length);

            if (length == 0)
                return;

            // Slice both sides so an overlapping copy inside one shared buffer still behaves like memmove.
            src.AsReadOnlySpan().Slice(srcIndex, length).CopyTo(dst.AsSpan().Slice(dstIndex, length));
        }

        public static void Copy(T[] src, NativeArray<T> dst)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));

            CheckSameLength(src.Length, dst.Length);
            Copy(src, 0, dst, 0, src.Length);
        }

        public static void Copy(T[] src, NativeArray<T> dst, int length) => Copy(src, 0, dst, 0, length);

        public static void Copy(T[] src, int srcIndex, NativeArray<T> dst, int dstIndex, int length)
        {
            if (src == null)
                throw new ArgumentNullException(nameof(src));

            dst.CheckCreated();
            CheckCopyRange(src.Length, srcIndex, dst.Length, dstIndex, length);

            if (length == 0)
                return;

            new ReadOnlySpan<T>(src, srcIndex, length).CopyTo(dst.AsSpan().Slice(dstIndex, length));
        }

        public static void Copy(NativeArray<T> src, T[] dst)
        {
            if (dst == null)
                throw new ArgumentNullException(nameof(dst));

            CheckSameLength(src.Length, dst.Length);
            Copy(src, 0, dst, 0, src.Length);
        }

        public static void Copy(NativeArray<T> src, T[] dst, int length) => Copy(src, 0, dst, 0, length);

        public static void Copy(NativeArray<T> src, int srcIndex, T[] dst, int dstIndex, int length)
        {
            if (dst == null)
                throw new ArgumentNullException(nameof(dst));

            src.CheckCreated();
            CheckCopyRange(src.Length, srcIndex, dst.Length, dstIndex, length);

            if (length == 0)
                return;

            src.AsReadOnlySpan().Slice(srcIndex, length).CopyTo(new Span<T>(dst, dstIndex, length));
        }

        // ---- equality ----------------------------------------------------------------------------------------

        /// <summary>
        /// Two arrays are equal when they alias the same memory, not when their contents match — the same
        /// identity comparison Unity performs on the pointer and length.
        /// </summary>
        public bool Equals(NativeArray<T> other) =>
            ReferenceEquals(buffer, other.buffer) && byteOffset == other.byteOffset && length == other.length;

        public override bool Equals(object obj) => obj is NativeArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            int hash = buffer == null ? 0 : RuntimeHelpers.GetHashCode(buffer);
            return HashCode.Combine(hash, byteOffset, length);
        }

        public static bool operator ==(NativeArray<T> left, NativeArray<T> right) => left.Equals(right);

        public static bool operator !=(NativeArray<T> left, NativeArray<T> right) => !left.Equals(right);

        // ---- enumeration -------------------------------------------------------------------------------------

        /// <summary>Struct enumerator: <c>foreach</c> over a <see cref="NativeArray{T}"/> allocates nothing.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        /// <summary>Mirrors <c>NativeArray&lt;T&gt;.Enumerator</c>.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            NativeArray<T> array;
            int index;

            internal Enumerator(NativeArray<T> array)
            {
                this.array = array;
                index = -1;
            }

            public T Current => array[index];

            object IEnumerator.Current => Current;

            public bool MoveNext() => ++index < array.length;

            public void Reset() => index = -1;

            public void Dispose()
            {
            }
        }

        // ---- internals ---------------------------------------------------------------------------------------

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref T FirstElementRef() =>
            ref Unsafe.As<byte, T>(ref Unsafe.AddByteOffset(ref MemoryMarshal.GetArrayDataReference(buffer), (nint)byteOffset));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        ref T ElementRef(int index) => ref Unsafe.Add(ref FirstElementRef(), index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void CheckIndex(int index)
        {
            if (buffer == null)
                ThrowNotCreated();

            if ((uint)index >= (uint)length)
                ThrowIndexOutOfRange(index, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void CheckCreated()
        {
            if (buffer == null)
                ThrowNotCreated();
        }

        static void CheckElementType()
        {
            // Unity rejects managed element types outright; so must the shim, because a byte[] reinterpreted as a
            // reference-carrying struct would hand the GC bogus object pointers.
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                throw new InvalidOperationException($"{typeof(T)} used in NativeArray<{typeof(T)}> must be unmanaged (contain no managed types).");
        }

        static void CheckSameLength(int srcLength, int dstLength)
        {
            if (srcLength != dstLength)
                throw new ArgumentException($"The source ({srcLength}) and destination ({dstLength}) lengths must match.");
        }

        static void CheckCopyRange(int srcLength, int srcIndex, int dstLength, int dstIndex, int length)
        {
            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

            if (srcIndex < 0 || srcIndex + length > srcLength)
                throw new ArgumentOutOfRangeException(nameof(srcIndex), $"Reading {length} elements from index {srcIndex} overruns the source of length {srcLength}.");

            if (dstIndex < 0 || dstIndex + length > dstLength)
                throw new ArgumentOutOfRangeException(nameof(dstIndex), $"Writing {length} elements at index {dstIndex} overruns the destination of length {dstLength}.");
        }

        static void ThrowNotCreated() =>
            throw new ObjectDisposedException("NativeArray", "The NativeArray has been disposed or was never created.");

        static void ThrowIndexOutOfRange(int index, int length) =>
            throw new IndexOutOfRangeException($"Index {index} is out of range of '{length}' Length.");
    }

    /// <summary>
    /// Mirrors <c>Unity.Collections.NativeList&lt;T&gt;</c> (the com.unity.collections package type).
    /// </summary>
    /// <remarks>
    /// WHY the state lives behind a class reference: Unity's <c>NativeList</c> is a struct wrapping a pointer to
    /// shared state, so passing one by value to a helper that calls <c>Add</c> and grows the buffer is visible to
    /// the caller. NowLottieBurstTessellator relies on exactly that (its jobs pass lists by value into static
    /// tessellation helpers). A struct holding the <c>byte[]</c> directly would silently lose every append that
    /// triggered a reallocation. The reference is allocated once per list, never on the append path.
    /// </remarks>
    public struct NativeList<T> : IDisposable, IEnumerable<T> where T : unmanaged
    {
        internal sealed class Storage
        {
            public byte[] buffer;
            public int length;
            public int capacity;
            public Allocator allocator;
        }

        internal Storage storage;

        public NativeList(Allocator allocator) : this(1, allocator)
        {
        }

        public NativeList(int initialCapacity, Allocator allocator)
        {
            if (initialCapacity < 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Capacity must be >= 0.");

            if (allocator <= Allocator.None)
                throw new ArgumentException("Allocator must be Temp, TempJob or Persistent.", nameof(allocator));

            storage = new Storage
            {
                capacity = initialCapacity,
                length = 0,
                allocator = allocator,
                buffer = Allocate(initialCapacity),
            };
        }

        public bool IsCreated => storage != null && storage.buffer != null;

        /// <summary>
        /// Element count. Setting it is Unity's shorthand for <c>Resize</c>: growing leaves the new elements
        /// uninitialised in Unity, but the shim's grow path always hands back zeroed memory, which is a superset
        /// of the guarantee and never observable as a difference.
        /// </summary>
        public int Length
        {
            get => storage == null ? 0 : storage.length;
            set => ResizeUninitialized(value);
        }

        public int Capacity
        {
            get => storage == null ? 0 : storage.capacity;
            set
            {
                CheckCreated();

                if (value < storage.length)
                    throw new ArgumentOutOfRangeException(nameof(value), "Capacity must be >= Length.");

                SetCapacity(value);
            }
        }

        public T this[int index]
        {
            get
            {
                CheckIndex(index);
                return AsArray()[index];
            }
            set
            {
                CheckIndex(index);

                // A local, because the indexer setter cannot run on the temporary AsArray() returns.
                NativeArray<T> view = AsArray();
                view[index] = value;
            }
        }

        /// <summary>Appends one element, growing the buffer geometrically when it is full.</summary>
        public void Add(in T value)
        {
            CheckCreated();

            if (storage.length == storage.capacity)
                SetCapacity(storage.capacity == 0 ? 1 : storage.capacity * 2);

            AsFullSpan()[storage.length] = value;
            ++storage.length;
        }

        /// <summary>Appends without growing; throws if the list is full, exactly as Unity does.</summary>
        public void AddNoResize(T value)
        {
            CheckCreated();

            if (storage.length == storage.capacity)
                throw new InvalidOperationException($"The NativeList is full ({storage.capacity} elements) and AddNoResize cannot grow it.");

            AsFullSpan()[storage.length] = value;
            ++storage.length;
        }

        /// <summary>Removes one element, shifting the tail down and preserving order.</summary>
        public void RemoveAt(int index)
        {
            CheckIndex(index);

            Span<T> span = AsFullSpan();
            int tail = storage.length - index - 1;

            if (tail > 0)
                span.Slice(index + 1, tail).CopyTo(span.Slice(index, tail));

            --storage.length;
        }

        /// <summary>Removes one element by moving the last element into its slot; order is not preserved.</summary>
        public void RemoveAtSwapBack(int index)
        {
            CheckIndex(index);

            Span<T> span = AsFullSpan();
            span[index] = span[storage.length - 1];
            --storage.length;
        }

        /// <summary>Drops every element but keeps the capacity, so reused scratch lists never reallocate.</summary>
        public void Clear()
        {
            CheckCreated();
            storage.length = 0;
        }

        /// <summary>Sets the length, growing the capacity if needed. New elements have unspecified contents.</summary>
        public void ResizeUninitialized(int length)
        {
            CheckCreated();

            if (length < 0)
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be >= 0.");

            if (length > storage.capacity)
                SetCapacity(Math.Max(length, storage.capacity == 0 ? 1 : storage.capacity * 2));

            storage.length = length;
        }

        /// <summary>Sets the length, zeroing the new tail when <paramref name="options"/> asks for it.</summary>
        public void Resize(int length, NativeArrayOptions options)
        {
            int previous = Length;
            ResizeUninitialized(length);

            if (options == NativeArrayOptions.ClearMemory && length > previous)
                AsFullSpan().Slice(previous, length - previous).Clear();
        }

        /// <summary>
        /// An aliasing <see cref="NativeArray{T}"/> over the first <see cref="Length"/> elements. As in Unity the
        /// view is invalidated by anything that reallocates the list (Add past capacity, Capacity, Resize).
        /// </summary>
        public NativeArray<T> AsArray()
        {
            CheckCreated();
            return NativeArray<T>.CreateView(storage.buffer, 0, storage.length);
        }

        public Span<T> AsSpan() => AsArray().AsSpan();

        public T[] ToArray() => AsArray().ToArray();

        public void Dispose()
        {
            if (storage != null)
            {
                storage.buffer = null;
                storage.length = 0;
                storage.capacity = 0;
            }

            storage = null;
        }

        public NativeArray<T>.Enumerator GetEnumerator() => AsArray().GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => AsArray().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => AsArray().GetEnumerator();

        static byte[] Allocate(int capacity)
        {
            int bytes = checked(capacity * NativeArray<T>.ElementSize);
            return GC.AllocateArray<byte>(bytes == 0 ? 1 : bytes, pinned: true);
        }

        void SetCapacity(int capacity)
        {
            byte[] grown = Allocate(capacity);
            int copyBytes = Math.Min(storage.length, capacity) * NativeArray<T>.ElementSize;

            if (copyBytes > 0)
                Buffer.BlockCopy(storage.buffer, 0, grown, 0, copyBytes);

            storage.buffer = grown;
            storage.capacity = capacity;

            if (storage.length > capacity)
                storage.length = capacity;
        }

        Span<T> AsFullSpan() => NativeArray<T>.CreateView(storage.buffer, 0, storage.capacity).AsSpan();

        void CheckCreated()
        {
            if (storage == null || storage.buffer == null)
                throw new ObjectDisposedException("NativeList", "The NativeList has been disposed or was never created.");
        }

        void CheckIndex(int index)
        {
            CheckCreated();

            if ((uint)index >= (uint)storage.length)
                throw new IndexOutOfRangeException($"Index {index} is out of range of '{storage.length}' Length.");
        }
    }
}
