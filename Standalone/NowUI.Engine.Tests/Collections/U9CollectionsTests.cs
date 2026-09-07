// Unit U9 semantics suite: Unity.Collections (NativeArray, NativeList), Unity.Collections.LowLevel.Unsafe,
// Unity.Jobs, Unity.Burst, Unity.Mathematics and Unity.Profiling.
//
// Acceptance checks from Docs/Standalone/StandaloneCoreDesign.md section 8 row U9:
//   * the Copy overload matrix
//   * a sequential IJobParallelFor runs Execute(i) for all i
//   * GetUnsafePtr writes are visible through the indexer
// plus the surface and float-policy requirements in section 3.9 and section 12.6.

using System;
using NUnit.Framework;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using NowUI.Engine;

namespace NowUI.Engine.Tests
{
    [TestFixture]
    public class U9CollectionsTests
    {
        // ---- NativeArray: construction, lifetime, indexing -----------------------------------------------------

        [Test]
        public void NativeArray_ClearMemory_IsTheDefaultAndZeroes()
        {
            var array = new NativeArray<int>(4, Allocator.Temp);

            try
            {
                Assert.That(array.Length, Is.EqualTo(4));
                Assert.That(array.IsCreated, Is.True);

                for (int i = 0; i < array.Length; ++i)
                    Assert.That(array[i], Is.EqualTo(0), $"element {i}");
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void NativeArray_UninitializedMemory_IsCreatedAndAddressable()
        {
            var array = new NativeArray<byte>(8, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            try
            {
                Assert.That(array.IsCreated, Is.True);
                Assert.That(array.Length, Is.EqualTo(8));

                for (int i = 0; i < array.Length; ++i)
                    array[i] = (byte)(i * 3);

                for (int i = 0; i < array.Length; ++i)
                    Assert.That(array[i], Is.EqualTo((byte)(i * 3)));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void NativeArray_ZeroLength_IsStillCreated()
        {
            // Unity treats a zero-length allocation as a real (created) array, and GetUnsafePtr still returns a
            // usable address. The shim must not represent it as a null buffer.
            var array = new NativeArray<float>(0, Allocator.Temp);

            try
            {
                Assert.That(array.IsCreated, Is.True);
                Assert.That(array.Length, Is.EqualTo(0));
                Assert.That(array.AsSpan().Length, Is.EqualTo(0));
                Assert.That(array.ToArray(), Is.Empty);
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void NativeArray_Dispose_ClearsIsCreated()
        {
            var array = new NativeArray<int>(2, Allocator.Temp);
            array.Dispose();

            Assert.That(array.IsCreated, Is.False);
            Assert.That(array.Length, Is.EqualTo(0));
        }

        [Test]
        public void NativeArray_IndexerOutOfRange_Throws()
        {
            var array = new NativeArray<int>(2, Allocator.Temp);

            try
            {
                Assert.Throws<IndexOutOfRangeException>(() => { int _ = array[2]; });
                Assert.Throws<IndexOutOfRangeException>(() => { int _ = array[-1]; });
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void NativeArray_UseAfterDispose_Throws()
        {
            var array = new NativeArray<int>(2, Allocator.Temp);
            array.Dispose();

            Assert.Throws<ObjectDisposedException>(() => { int _ = array[0]; });
        }

        [Test]
        public void NativeArray_InvalidAllocatorAndNegativeLength_Throw()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new NativeArray<int>(-1, Allocator.Temp).Dispose());
            Assert.Throws<ArgumentException>(() => new NativeArray<int>(1, Allocator.None).Dispose());
            Assert.Throws<ArgumentException>(() => new NativeArray<int>(1, Allocator.Invalid).Dispose());
        }

        [Test]
        public void NativeArray_ManagedElementType_IsRejected()
        {
            // A byte[] reinterpreted as a reference-carrying struct would hand the GC bogus pointers, so this must
            // fail the way Unity's "must be unmanaged" check does.
            Assert.Throws<InvalidOperationException>(() => new NativeArray<ManagedPayload>(1, Allocator.Temp).Dispose());
        }

        struct ManagedPayload
        {
#pragma warning disable CS0649 // never assigned: only its layout matters to the test
            public string text;
#pragma warning restore CS0649
        }

        [Test]
        public void NativeArray_FromManagedArray_Copies()
        {
            int[] source = { 5, 6, 7 };
            var array = new NativeArray<int>(source, Allocator.Temp);

            try
            {
                Assert.That(array.ToArray(), Is.EqualTo(source));

                // A copy, not an alias.
                array[0] = 99;
                Assert.That(source[0], Is.EqualTo(5));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void NativeArray_FromNativeArray_Copies()
        {
            var source = new NativeArray<int>(new[] { 1, 2, 3 }, Allocator.Temp);
            var array = new NativeArray<int>(source, Allocator.Temp);

            try
            {
                Assert.That(array.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));

                array[1] = 42;
                Assert.That(source[1], Is.EqualTo(2));
            }
            finally
            {
                source.Dispose();
                array.Dispose();
            }
        }

        // ---- NativeArray: the Copy overload matrix (U9 acceptance check) ---------------------------------------

        [Test]
        public void Copy_NativeToNative_WholeArray()
        {
            var src = new NativeArray<int>(new[] { 1, 2, 3, 4 }, Allocator.Temp);
            var dst = new NativeArray<int>(4, Allocator.Temp);

            try
            {
                NativeArray<int>.Copy(src, dst);
                Assert.That(dst.ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            }
            finally
            {
                src.Dispose();
                dst.Dispose();
            }
        }

        [Test]
        public void Copy_NativeToNative_LengthPrefix()
        {
            var src = new NativeArray<int>(new[] { 1, 2, 3, 4 }, Allocator.Temp);
            var dst = new NativeArray<int>(4, Allocator.Temp);

            try
            {
                NativeArray<int>.Copy(src, dst, 2);
                Assert.That(dst.ToArray(), Is.EqualTo(new[] { 1, 2, 0, 0 }));
            }
            finally
            {
                src.Dispose();
                dst.Dispose();
            }
        }

        [Test]
        public void Copy_NativeToNative_Indexed()
        {
            var src = new NativeArray<int>(new[] { 1, 2, 3, 4 }, Allocator.Temp);
            var dst = new NativeArray<int>(5, Allocator.Temp);

            try
            {
                NativeArray<int>.Copy(src, 1, dst, 2, 3);
                Assert.That(dst.ToArray(), Is.EqualTo(new[] { 0, 0, 2, 3, 4 }));
            }
            finally
            {
                src.Dispose();
                dst.Dispose();
            }
        }

        [Test]
        public void Copy_ManagedToNative_AllThreeShapes()
        {
            int[] source = { 9, 8, 7, 6 };

            var whole = new NativeArray<int>(4, Allocator.Temp);
            var prefix = new NativeArray<int>(4, Allocator.Temp);
            var indexed = new NativeArray<int>(5, Allocator.Temp);

            try
            {
                NativeArray<int>.Copy(source, whole);
                Assert.That(whole.ToArray(), Is.EqualTo(source));

                NativeArray<int>.Copy(source, prefix, 2);
                Assert.That(prefix.ToArray(), Is.EqualTo(new[] { 9, 8, 0, 0 }));

                // This is the overload NowLottieBurstTessellator.cs:75 and :1460 call.
                NativeArray<int>.Copy(source, 1, indexed, 2, 3);
                Assert.That(indexed.ToArray(), Is.EqualTo(new[] { 0, 0, 8, 7, 6 }));
            }
            finally
            {
                whole.Dispose();
                prefix.Dispose();
                indexed.Dispose();
            }
        }

        [Test]
        public void Copy_NativeToManaged_AllThreeShapes()
        {
            var src = new NativeArray<int>(new[] { 4, 5, 6, 7 }, Allocator.Temp);

            try
            {
                var whole = new int[4];
                NativeArray<int>.Copy(src, whole);
                Assert.That(whole, Is.EqualTo(new[] { 4, 5, 6, 7 }));

                var prefix = new int[4];
                NativeArray<int>.Copy(src, prefix, 2);
                Assert.That(prefix, Is.EqualTo(new[] { 4, 5, 0, 0 }));

                // This is the overload NowManagedFontSession.cs:379 calls.
                var indexed = new int[5];
                NativeArray<int>.Copy(src, 1, indexed, 2, 3);
                Assert.That(indexed, Is.EqualTo(new[] { 0, 0, 5, 6, 7 }));
            }
            finally
            {
                src.Dispose();
            }
        }

        [Test]
        public void Copy_MismatchedWholeArrayLengths_Throw()
        {
            var four = new NativeArray<int>(4, Allocator.Temp);
            var three = new NativeArray<int>(3, Allocator.Temp);

            try
            {
                Assert.Throws<ArgumentException>(() => NativeArray<int>.Copy(four, three));
                Assert.Throws<ArgumentException>(() => NativeArray<int>.Copy(new int[3], four));
                Assert.Throws<ArgumentException>(() => NativeArray<int>.Copy(four, new int[3]));
            }
            finally
            {
                four.Dispose();
                three.Dispose();
            }
        }

        [Test]
        public void Copy_OutOfRange_Throws()
        {
            var src = new NativeArray<int>(4, Allocator.Temp);
            var dst = new NativeArray<int>(4, Allocator.Temp);

            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => NativeArray<int>.Copy(src, 2, dst, 0, 3));
                Assert.Throws<ArgumentOutOfRangeException>(() => NativeArray<int>.Copy(src, 0, dst, 2, 3));
                Assert.Throws<ArgumentOutOfRangeException>(() => NativeArray<int>.Copy(src, 0, dst, 0, -1));
            }
            finally
            {
                src.Dispose();
                dst.Dispose();
            }
        }

        [Test]
        public void Copy_ZeroLength_IsANoOp()
        {
            var src = new NativeArray<int>(new[] { 1, 2 }, Allocator.Temp);
            var dst = new NativeArray<int>(2, Allocator.Temp);

            try
            {
                NativeArray<int>.Copy(src, 0, dst, 0, 0);
                Assert.That(dst.ToArray(), Is.EqualTo(new[] { 0, 0 }));
            }
            finally
            {
                src.Dispose();
                dst.Dispose();
            }
        }

        [Test]
        public void CopyFrom_And_CopyTo_RoundTrip()
        {
            var array = new NativeArray<int>(3, Allocator.Temp);
            var other = new NativeArray<int>(3, Allocator.Temp);

            try
            {
                array.CopyFrom(new[] { 3, 1, 4 });
                var managed = new int[3];
                array.CopyTo(managed);
                Assert.That(managed, Is.EqualTo(new[] { 3, 1, 4 }));

                other.CopyFrom(array);
                Assert.That(other.ToArray(), Is.EqualTo(new[] { 3, 1, 4 }));

                var back = new NativeArray<int>(3, Allocator.Temp);
                other.CopyTo(back);
                Assert.That(back.ToArray(), Is.EqualTo(new[] { 3, 1, 4 }));
                back.Dispose();
            }
            finally
            {
                array.Dispose();
                other.Dispose();
            }
        }

        // ---- NativeArray: views, sub-arrays, reinterpretation, enumeration -------------------------------------

        [Test]
        public void GetSubArray_AliasesTheParentBuffer()
        {
            var array = new NativeArray<int>(new[] { 0, 1, 2, 3, 4 }, Allocator.Temp);

            try
            {
                NativeArray<int> sub = array.GetSubArray(1, 3);

                Assert.That(sub.Length, Is.EqualTo(3));
                Assert.That(sub.ToArray(), Is.EqualTo(new[] { 1, 2, 3 }));

                sub[0] = 42;
                Assert.That(array[1], Is.EqualTo(42), "the sub-array must alias, not copy");
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void GetSubArray_OutOfRange_Throws()
        {
            var array = new NativeArray<int>(4, Allocator.Temp);

            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => array.GetSubArray(3, 2));
                Assert.Throws<ArgumentOutOfRangeException>(() => array.GetSubArray(-1, 1));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void Reinterpret_ViewsTheSameBytes()
        {
            var bytes = new NativeArray<byte>(8, Allocator.Temp);

            try
            {
                NativeArray<int> ints = bytes.Reinterpret<int>(1);

                Assert.That(ints.Length, Is.EqualTo(2));

                ints[0] = 0x01020304;
                Assert.That(bytes[0], Is.EqualTo(BitConverter.IsLittleEndian ? (byte)0x04 : (byte)0x01));
            }
            finally
            {
                bytes.Dispose();
            }
        }

        [Test]
        public void Reinterpret_WrongExpectedSizeOrRagged_Throws()
        {
            var bytes = new NativeArray<byte>(6, Allocator.Temp);

            try
            {
                Assert.Throws<InvalidOperationException>(() => bytes.Reinterpret<int>(4));
                Assert.Throws<InvalidOperationException>(() => bytes.Reinterpret<int>(1));
            }
            finally
            {
                bytes.Dispose();
            }
        }

        [Test]
        public void AsSpan_WritesThrough()
        {
            var array = new NativeArray<int>(3, Allocator.Temp);

            try
            {
                Span<int> span = array.AsSpan();
                span[1] = 77;

                Assert.That(array[1], Is.EqualTo(77));
                Assert.That(array.AsReadOnlySpan()[1], Is.EqualTo(77));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void Enumerator_VisitsEveryElementInOrder()
        {
            var array = new NativeArray<int>(new[] { 10, 20, 30 }, Allocator.Temp);

            try
            {
                int index = 0;

                foreach (int value in array)
                {
                    Assert.That(value, Is.EqualTo(array[index]));
                    ++index;
                }

                Assert.That(index, Is.EqualTo(3));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void Equality_IsAliasIdentityNotContent()
        {
            var a = new NativeArray<int>(new[] { 1, 2 }, Allocator.Temp);
            var b = new NativeArray<int>(new[] { 1, 2 }, Allocator.Temp);

            try
            {
                NativeArray<int> alias = a;

                Assert.That(a.Equals(alias), Is.True);
                Assert.That(a == alias, Is.True);

                // Same contents, different buffers: Unity compares the pointer, so these are NOT equal.
                Assert.That(a.Equals(b), Is.False);
                Assert.That(a != b, Is.True);

                Assert.That(a.GetSubArray(0, 2).Equals(a), Is.True, "a full-length sub-array aliases the same memory");
            }
            finally
            {
                a.Dispose();
                b.Dispose();
            }
        }

        // ---- UnsafeUtility / NativeArrayUnsafeUtility (U9 acceptance check) ------------------------------------

        [Test]
        public unsafe void GetUnsafePtr_WritesAreVisibleThroughTheIndexer()
        {
            var array = new NativeArray<int>(4, Allocator.Persistent);

            try
            {
                var pointer = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(array);

                for (int i = 0; i < array.Length; ++i)
                    pointer[i] = i * 11;

                for (int i = 0; i < array.Length; ++i)
                    Assert.That(array[i], Is.EqualTo(i * 11), $"element {i} written through the pointer");

                // And the other direction: an indexer write is visible through the pointer.
                array[2] = -5;
                Assert.That(pointer[2], Is.EqualTo(-5));

                Assert.That((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(array), Is.EqualTo((IntPtr)pointer));
                Assert.That((IntPtr)NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(array), Is.EqualTo((IntPtr)pointer));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public unsafe void GetUnsafePtr_OnASubArray_PointsAtTheOffsetElement()
        {
            var array = new NativeArray<int>(new[] { 0, 1, 2, 3 }, Allocator.Persistent);

            try
            {
                NativeArray<int> sub = array.GetSubArray(2, 2);
                var pointer = (int*)NativeArrayUnsafeUtility.GetUnsafePtr(sub);

                Assert.That(pointer[0], Is.EqualTo(2));

                pointer[1] = 33;
                Assert.That(array[3], Is.EqualTo(33));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public unsafe void MemCpy_MovesBytesBetweenNativeArrays()
        {
            // This is the NowManagedFontSession.TryCopyAtlas / NowFontCompiler.DynamicSession.TryCopyAtlas shape:
            // a managed byte[] fixed and memcpy'd into a NativeArray's raw pointer.
            var destination = new NativeArray<byte>(4, Allocator.Persistent);
            byte[] source = { 1, 2, 3, 4 };

            try
            {
                fixed (byte* sourcePointer = source)
                {
                    UnsafeUtility.MemCpy(NativeArrayUnsafeUtility.GetUnsafePtr(destination), sourcePointer, source.Length);
                }

                Assert.That(destination.ToArray(), Is.EqualTo(source));
            }
            finally
            {
                destination.Dispose();
            }
        }

        [Test]
        public unsafe void MemMove_HandlesOverlap()
        {
            var array = new NativeArray<byte>(new byte[] { 1, 2, 3, 4, 5 }, Allocator.Persistent);

            try
            {
                var b = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(array);
                UnsafeUtility.MemMove(b + 1, b, 4);

                Assert.That(array.ToArray(), Is.EqualTo(new byte[] { 1, 1, 2, 3, 4 }));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public unsafe void MemClear_And_MemSet_FillTheRange()
        {
            var array = new NativeArray<byte>(4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);

            try
            {
                var b = (byte*)NativeArrayUnsafeUtility.GetUnsafePtr(array);

                UnsafeUtility.MemSet(b, 0xAB, 4);
                Assert.That(array.ToArray(), Is.EqualTo(new byte[] { 0xAB, 0xAB, 0xAB, 0xAB }));

                UnsafeUtility.MemClear(b + 1, 2);
                Assert.That(array.ToArray(), Is.EqualTo(new byte[] { 0xAB, 0, 0, 0xAB }));

                // Zero-size operations must not touch anything and must not fault.
                UnsafeUtility.MemClear(b, 0);
                UnsafeUtility.MemSet(b, 0xFF, 0);
                UnsafeUtility.MemCpy(b, b, 0);
                Assert.That(array.ToArray(), Is.EqualTo(new byte[] { 0xAB, 0, 0, 0xAB }));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public void SizeOf_And_AlignOf_MatchTheLayout()
        {
            Assert.That(UnsafeUtility.SizeOf<byte>(), Is.EqualTo(1));
            Assert.That(UnsafeUtility.SizeOf<int>(), Is.EqualTo(4));
            Assert.That(UnsafeUtility.SizeOf<float4>(), Is.EqualTo(16));
            Assert.That(UnsafeUtility.SizeOf<Vector2>(), Is.EqualTo(8));

            Assert.That(UnsafeUtility.AlignOf<byte>(), Is.EqualTo(1));
            Assert.That(UnsafeUtility.AlignOf<int>(), Is.EqualTo(4));
            Assert.That(UnsafeUtility.AlignOf<float4>(), Is.EqualTo(4), "float4 is four floats, so it aligns like a float");
        }

        [Test]
        public void As_ReinterpretsAnEnumWithoutBoxing()
        {
            // The NowValueControls.cs:4769-4821 pattern: read an enum's underlying integer, and build an enum from
            // an integer, without boxing.
            SampleEnum value = SampleEnum.Second;
            Assert.That(UnsafeUtility.As<SampleEnum, int>(ref value), Is.EqualTo(2));

            int narrowed = 3;
            Assert.That(UnsafeUtility.As<int, SampleEnum>(ref narrowed), Is.EqualTo(SampleEnum.Third));

            // The reference is live: writing through it changes the original.
            UnsafeUtility.As<SampleEnum, int>(ref value) = 3;
            Assert.That(value, Is.EqualTo(SampleEnum.Third));
        }

        enum SampleEnum
        {
            First = 1,
            Second = 2,
            Third = 3,
        }

        [Test]
        public unsafe void ReadAndWriteArrayElement_RoundTrip()
        {
            var array = new NativeArray<float>(3, Allocator.Persistent);

            try
            {
                void* pointer = NativeArrayUnsafeUtility.GetUnsafePtr(array);

                UnsafeUtility.WriteArrayElement(pointer, 2, 1.5f);
                Assert.That(UnsafeUtility.ReadArrayElement<float>(pointer, 2), Is.EqualTo(1.5f));
                Assert.That(array[2], Is.EqualTo(1.5f));
            }
            finally
            {
                array.Dispose();
            }
        }

        [Test]
        public unsafe void MallocAndFree_RoundTrip()
        {
            void* block = UnsafeUtility.Malloc(64, 16, Allocator.Persistent);

            Assert.That((IntPtr)block, Is.Not.EqualTo(IntPtr.Zero));
            Assert.That((long)block % 16, Is.EqualTo(0), "Malloc must honour the requested alignment");

            UnsafeUtility.MemClear(block, 64);
            UnsafeUtility.Free(block, Allocator.Persistent);
        }

        [Test]
        public unsafe void ConvertExistingDataToNativeArray_IsExplicitlyUnsupported()
        {
            // Documented limitation: the shim's NativeArray is byte[]-backed and cannot alias native memory.
            // Failing loudly is deliberate — a silent copy would drop the caller's writes.
            int storage = 0;
            void* pointer = &storage;

            Assert.Throws<NotSupportedException>(() => NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<int>(pointer, 1, Allocator.None));
        }

        // ---- NativeList ---------------------------------------------------------------------------------------

        [Test]
        public void NativeList_AddGrowsAndIndexes()
        {
            var list = new NativeList<int>(2, Allocator.Temp);

            try
            {
                Assert.That(list.IsCreated, Is.True);
                Assert.That(list.Length, Is.EqualTo(0));

                for (int i = 0; i < 10; ++i)
                    list.Add(i);

                Assert.That(list.Length, Is.EqualTo(10));

                for (int i = 0; i < 10; ++i)
                    Assert.That(list[i], Is.EqualTo(i));

                list[3] = 99;
                Assert.That(list[3], Is.EqualTo(99));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_GrowthIsVisibleThroughACopyOfTheStruct()
        {
            // Unity's NativeList is a struct wrapping shared state, so a by-value copy handed to a helper that
            // appends past the capacity still updates the caller's view. NowLottieBurstTessellator depends on this
            // (its jobs pass lists by value into static tessellation helpers).
            var list = new NativeList<int>(1, Allocator.Temp);

            try
            {
                AppendThroughACopy(list, 5);

                Assert.That(list.Length, Is.EqualTo(5));
                Assert.That(list.ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            }
            finally
            {
                list.Dispose();
            }
        }

        static void AppendThroughACopy(NativeList<int> list, int count)
        {
            for (int i = 0; i < count; ++i)
                list.Add(i);
        }

        [Test]
        public void NativeList_RemoveAt_PreservesOrder()
        {
            var list = new NativeList<int>(4, Allocator.Temp);

            try
            {
                list.Add(0);
                list.Add(1);
                list.Add(2);
                list.Add(3);

                list.RemoveAt(1);

                Assert.That(list.Length, Is.EqualTo(3));
                Assert.That(list.ToArray(), Is.EqualTo(new[] { 0, 2, 3 }));

                list.RemoveAt(list.Length - 1);
                Assert.That(list.ToArray(), Is.EqualTo(new[] { 0, 2 }));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_ClearKeepsCapacity()
        {
            var list = new NativeList<int>(8, Allocator.Temp);

            try
            {
                list.Add(1);
                list.Add(2);
                int capacity = list.Capacity;

                list.Clear();

                Assert.That(list.Length, Is.EqualTo(0));
                Assert.That(list.Capacity, Is.EqualTo(capacity), "Clear must not release the buffer: scratch lists are reused every frame");
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_ResizeUninitializedAndLengthSetterAgree()
        {
            var list = new NativeList<int>(2, Allocator.Temp);

            try
            {
                list.ResizeUninitialized(5);
                Assert.That(list.Length, Is.EqualTo(5));
                Assert.That(list.Capacity, Is.GreaterThanOrEqualTo(5));

                list.Length = 2;
                Assert.That(list.Length, Is.EqualTo(2));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_ResizeClearMemory_ZeroesTheNewTail()
        {
            var list = new NativeList<int>(2, Allocator.Temp);

            try
            {
                list.Add(7);
                list.Resize(4, NativeArrayOptions.ClearMemory);

                Assert.That(list.ToArray(), Is.EqualTo(new[] { 7, 0, 0, 0 }));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_AsArray_AliasesTheList()
        {
            var list = new NativeList<int>(4, Allocator.Temp);

            try
            {
                list.Add(1);
                list.Add(2);

                NativeArray<int> view = list.AsArray();
                Assert.That(view.Length, Is.EqualTo(2), "AsArray must expose Length elements, not Capacity");

                view[0] = 42;
                Assert.That(list[0], Is.EqualTo(42));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_AddNoResize_ThrowsWhenFull()
        {
            var list = new NativeList<int>(1, Allocator.Temp);

            try
            {
                list.AddNoResize(1);
                Assert.Throws<InvalidOperationException>(() => list.AddNoResize(2));
            }
            finally
            {
                list.Dispose();
            }
        }

        [Test]
        public void NativeList_UseAfterDispose_Throws()
        {
            var list = new NativeList<int>(2, Allocator.Temp);
            list.Dispose();

            Assert.That(list.IsCreated, Is.False);
            Assert.Throws<ObjectDisposedException>(() => list.Add(1));
        }

        [Test]
        public void NativeList_RemoveAtSwapBack_MovesTheTail()
        {
            var list = new NativeList<int>(4, Allocator.Temp);

            try
            {
                list.Add(0);
                list.Add(1);
                list.Add(2);
                list.Add(3);

                list.RemoveAtSwapBack(0);

                Assert.That(list.ToArray(), Is.EqualTo(new[] { 3, 1, 2 }));
            }
            finally
            {
                list.Dispose();
            }
        }

        // ---- Jobs (U9 acceptance check) ------------------------------------------------------------------------

        [BurstCompile]
        struct FillJob : IJob
        {
            public NativeArray<int> output;
            public int value;

            public void Execute()
            {
                for (int i = 0; i < output.Length; ++i)
                    output[i] = value;
            }
        }

        [BurstCompile]
        struct SquareJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<int> input;

            [NativeDisableParallelForRestriction] public NativeArray<int> output;

            public void Execute(int index) => output[index] = input[index] * input[index];
        }

        [Test]
        public void IJob_Run_ExecutesSynchronously()
        {
            var output = new NativeArray<int>(4, Allocator.TempJob);

            try
            {
                new FillJob { output = output, value = 7 }.Run();

                Assert.That(output.ToArray(), Is.EqualTo(new[] { 7, 7, 7, 7 }));
            }
            finally
            {
                output.Dispose();
            }
        }

        [Test]
        public void IJob_Schedule_HasAlreadyRunWhenItReturns()
        {
            var output = new NativeArray<int>(3, Allocator.TempJob);

            try
            {
                JobHandle handle = new FillJob { output = output, value = 2 }.Schedule();

                // The result is visible BEFORE Complete(), because Schedule ran the job inline.
                Assert.That(output.ToArray(), Is.EqualTo(new[] { 2, 2, 2 }));
                Assert.That(handle.IsCompleted, Is.True);

                handle.Complete();
                Assert.That(output.ToArray(), Is.EqualTo(new[] { 2, 2, 2 }));
            }
            finally
            {
                output.Dispose();
            }
        }

        [Test]
        public void IJobParallelFor_Schedule_RunsEveryIndexExactlyOnceInOrder()
        {
            // The U9 acceptance check, and the shape of NowManagedFontSession.cs:374
            // (`NowSdfBakeJob.Schedule(cellCount, 1).Complete()`).
            const int count = 64;

            var input = new NativeArray<int>(count, Allocator.TempJob);
            var output = new NativeArray<int>(count, Allocator.TempJob);

            try
            {
                for (int i = 0; i < count; ++i)
                    input[i] = i;

                new SquareJob { input = input, output = output }.Schedule(count, 1).Complete();

                for (int i = 0; i < count; ++i)
                    Assert.That(output[i], Is.EqualTo(i * i), $"index {i} must have been visited");
            }
            finally
            {
                input.Dispose();
                output.Dispose();
            }
        }

        [BurstCompile]
        struct RecordingJob : IJobParallelFor
        {
            public NativeArray<int> visitOrder;
            public NativeArray<int> cursor;

            public void Execute(int index)
            {
                int slot = cursor[0];
                visitOrder[slot] = index;
                cursor[0] = slot + 1;
            }
        }

        [Test]
        public void IJobParallelFor_VisitsIndicesInAscendingOrder()
        {
            const int count = 8;

            var visitOrder = new NativeArray<int>(count, Allocator.TempJob);
            var cursor = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                new RecordingJob { visitOrder = visitOrder, cursor = cursor }.Schedule(count, 4).Complete();

                Assert.That(cursor[0], Is.EqualTo(count), "Execute must be called exactly once per index");
                Assert.That(visitOrder.ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }),
                    "design section 3.9 fixes execution as sequential on the calling thread, so order is ascending");
            }
            finally
            {
                visitOrder.Dispose();
                cursor.Dispose();
            }
        }

        [Test]
        public void IJobParallelFor_Run_AlsoCoversEveryIndex()
        {
            const int count = 5;

            var input = new NativeArray<int>(count, Allocator.TempJob);
            var output = new NativeArray<int>(count, Allocator.TempJob);

            try
            {
                for (int i = 0; i < count; ++i)
                    input[i] = i + 1;

                new SquareJob { input = input, output = output }.Run(count);

                Assert.That(output.ToArray(), Is.EqualTo(new[] { 1, 4, 9, 16, 25 }));
            }
            finally
            {
                input.Dispose();
                output.Dispose();
            }
        }

        [Test]
        public void IJobParallelFor_ZeroLength_RunsNothing()
        {
            var input = new NativeArray<int>(0, Allocator.TempJob);
            var output = new NativeArray<int>(0, Allocator.TempJob);

            try
            {
                Assert.DoesNotThrow(() => new SquareJob { input = input, output = output }.Schedule(0, 1).Complete());
            }
            finally
            {
                input.Dispose();
                output.Dispose();
            }
        }

        [Test]
        public void IJobParallelFor_NegativeLength_Throws()
        {
            var input = new NativeArray<int>(1, Allocator.TempJob);
            var output = new NativeArray<int>(1, Allocator.TempJob);

            try
            {
                var job = new SquareJob { input = input, output = output };
                Assert.Throws<ArgumentOutOfRangeException>(() => job.Schedule(-1, 1));
            }
            finally
            {
                input.Dispose();
                output.Dispose();
            }
        }

        [Test]
        public void JobHandle_CompletionHelpersAreNoOps()
        {
            JobHandle a = default;
            JobHandle b = default;

            Assert.That(a.IsCompleted, Is.True);
            Assert.DoesNotThrow(() => a.Complete());
            Assert.DoesNotThrow(() => JobHandle.CompleteAll(ref a, ref b));
            Assert.DoesNotThrow(() => JobHandle.ScheduleBatchedJobs());
            Assert.That(JobHandle.CombineDependencies(a, b).IsCompleted, Is.True);
            Assert.That(a == b, Is.True);
            Assert.That(a != b, Is.False);
        }

        // ---- Burst --------------------------------------------------------------------------------------------

        [Test]
        public void BurstCompileAttribute_IsDeclarativeAndReadable()
        {
            var attribute = (BurstCompileAttribute[])typeof(FillJob).GetCustomAttributes(typeof(BurstCompileAttribute), false);

            Assert.That(attribute.Length, Is.EqualTo(1), "[BurstCompile] must be visible on a job struct");

            var configured = new BurstCompileAttribute(FloatPrecision.Low, FloatMode.Fast) { CompileSynchronously = true };

            Assert.That(configured.FloatPrecision, Is.EqualTo(FloatPrecision.Low));
            Assert.That(configured.FloatMode, Is.EqualTo(FloatMode.Fast));
            Assert.That(configured.CompileSynchronously, Is.True);
            Assert.That(configured.DisableSafetyChecks, Is.False);
        }

        [Test]
        public void BurstEnums_MatchUnityNumericValues()
        {
            Assert.That((int)FloatMode.Default, Is.EqualTo(0));
            Assert.That((int)FloatMode.Strict, Is.EqualTo(1));
            Assert.That((int)FloatMode.Deterministic, Is.EqualTo(2));
            Assert.That((int)FloatMode.Fast, Is.EqualTo(3));

            Assert.That((int)FloatPrecision.Standard, Is.EqualTo(0));
            Assert.That((int)FloatPrecision.High, Is.EqualTo(1));
            Assert.That((int)FloatPrecision.Medium, Is.EqualTo(2));
            Assert.That((int)FloatPrecision.Low, Is.EqualTo(3));
        }

        [Test]
        public void CollectionEnums_MatchUnityNumericValues()
        {
            Assert.That((int)Allocator.Invalid, Is.EqualTo(0));
            Assert.That((int)Allocator.None, Is.EqualTo(1));
            Assert.That((int)Allocator.Temp, Is.EqualTo(2));
            Assert.That((int)Allocator.TempJob, Is.EqualTo(3));
            Assert.That((int)Allocator.Persistent, Is.EqualTo(4));
            Assert.That((int)Allocator.AudioKernel, Is.EqualTo(5));

            Assert.That((int)NativeArrayOptions.UninitializedMemory, Is.EqualTo(0));
            Assert.That((int)NativeArrayOptions.ClearMemory, Is.EqualTo(1));
        }

        // ---- Mathematics --------------------------------------------------------------------------------------

        [Test]
        public void Float2_ConstructorsAndAccessors()
        {
            var v = new float2(1f, 2f);

            Assert.That(v.x, Is.EqualTo(1f));
            Assert.That(v.y, Is.EqualTo(2f));

            var splat = new float2(3f);
            Assert.That(splat.x, Is.EqualTo(3f));
            Assert.That(splat.y, Is.EqualTo(3f));
        }

        [Test]
        public void Float2_Operators()
        {
            var a = new float2(3f, 4f);
            var b = new float2(1f, 2f);

            Assert.That((a + b).Equals(new float2(4f, 6f)), Is.True);
            Assert.That((a - b).Equals(new float2(2f, 2f)), Is.True);
            Assert.That((a * b).Equals(new float2(3f, 8f)), Is.True);
            Assert.That((a * 2f).Equals(new float2(6f, 8f)), Is.True);
            Assert.That((2f * a).Equals(new float2(6f, 8f)), Is.True);
            Assert.That((a / 2f).Equals(new float2(1.5f, 2f)), Is.True);
            Assert.That((-a).Equals(new float2(-3f, -4f)), Is.True);
        }

        [Test]
        public void Float2_ConvertsToAndFromVector2()
        {
            float2 fromVector = new Vector2(1.5f, -2.5f);
            Assert.That(fromVector.x, Is.EqualTo(1.5f));
            Assert.That(fromVector.y, Is.EqualTo(-2.5f));

            var toVector = (Vector2)new float2(4f, 5f);
            Assert.That(toVector.x, Is.EqualTo(4f));
            Assert.That(toVector.y, Is.EqualTo(5f));
        }

        [Test]
        public void Float4_ConstructorsAndAccessors()
        {
            var v = new float4(1f, 2f, 3f, 4f);

            Assert.That(v.x, Is.EqualTo(1f));
            Assert.That(v.y, Is.EqualTo(2f));
            Assert.That(v.z, Is.EqualTo(3f));
            Assert.That(v.w, Is.EqualTo(4f));

            Assert.That(new float4(2f).Equals(new float4(2f, 2f, 2f, 2f)), Is.True);
            Assert.That(new float4(new float3(1f, 2f, 3f), 4f).Equals(v), Is.True);
            Assert.That(new float4(new float2(1f, 2f), 3f, 4f).Equals(v), Is.True);
            Assert.That(new float4(new float2(1f, 2f), new float2(3f, 4f)).Equals(v), Is.True);

            Assert.That(v.xy.Equals(new float2(1f, 2f)), Is.True);
            Assert.That(v.zw.Equals(new float2(3f, 4f)), Is.True);
        }

        [Test]
        public void Float4_ConvertsToAndFromVector4()
        {
            // This is the NowManagedFontSession.cs:342 shape: a Vector4 segment fed into a float4 buffer.
            float4 segment = new Vector4(1f, 2f, 3f, 4f);

            Assert.That(segment.Equals(new float4(1f, 2f, 3f, 4f)), Is.True);
            Assert.That((Vector4)segment, Is.EqualTo(new Vector4(1f, 2f, 3f, 4f)));
        }

        [Test]
        public void Float4_Operators()
        {
            var a = new float4(1f, 2f, 3f, 4f);
            var b = new float4(4f, 3f, 2f, 1f);

            Assert.That((a + b).Equals(new float4(5f, 5f, 5f, 5f)), Is.True);
            Assert.That((a - b).Equals(new float4(-3f, -1f, 1f, 3f)), Is.True);
            Assert.That((a * 2f).Equals(new float4(2f, 4f, 6f, 8f)), Is.True);
            Assert.That((-a).Equals(new float4(-1f, -2f, -3f, -4f)), Is.True);
        }

        [Test]
        public void Math_Dot()
        {
            Assert.That(math.dot(new float2(1f, 2f), new float2(3f, 4f)), Is.EqualTo(11f));
            Assert.That(math.dot(new float4(1f, 2f, 3f, 4f), new float4(4f, 3f, 2f, 1f)), Is.EqualTo(20f));
        }

        [Test]
        public void Math_MinMax_IgnoreANaNSecondOperand()
        {
            // Unity.Mathematics defines min as `float.IsNaN(y) || x < y ? x : y`, so NaN never propagates the way
            // MathF.Min propagates it. NowSdfBakeJob's min(minDistSq, ...) accumulation depends on this.
            Assert.That(math.min(1f, 2f), Is.EqualTo(1f));
            Assert.That(math.min(2f, 1f), Is.EqualTo(1f));
            Assert.That(math.min(1f, float.NaN), Is.EqualTo(1f));
            Assert.That(math.min(float.NaN, 1f), Is.EqualTo(1f));

            Assert.That(math.max(1f, 2f), Is.EqualTo(2f));
            Assert.That(math.max(2f, 1f), Is.EqualTo(2f));
            Assert.That(math.max(1f, float.NaN), Is.EqualTo(1f));
            Assert.That(math.max(float.NaN, 1f), Is.EqualTo(1f));

            Assert.That(math.min(new float2(1f, 4f), new float2(3f, 2f)).Equals(new float2(1f, 2f)), Is.True);
            Assert.That(math.max(new float2(1f, 4f), new float2(3f, 2f)).Equals(new float2(3f, 4f)), Is.True);
        }

        [Test]
        public void Math_Saturate_ClampsAndTurnsNaNIntoOne()
        {
            Assert.That(math.saturate(-0.5f), Is.EqualTo(0f));
            Assert.That(math.saturate(0.25f), Is.EqualTo(0.25f));
            Assert.That(math.saturate(1.5f), Is.EqualTo(1f));

            // The Unity-faithful oddity: clamp(NaN, 0, 1) == max(0, min(1, NaN)) == max(0, 1) == 1.
            Assert.That(math.saturate(float.NaN), Is.EqualTo(1f));

            Assert.That(math.saturate(new float4(-1f, 0.5f, 2f, 1f)).Equals(new float4(0f, 0.5f, 1f, 1f)), Is.True);
        }

        [Test]
        public void Math_Clamp_UsesUnityArgumentOrder()
        {
            Assert.That(math.clamp(5f, 0f, 1f), Is.EqualTo(1f));
            Assert.That(math.clamp(-5f, 0f, 1f), Is.EqualTo(0f));
            Assert.That(math.clamp(3, 0, 10), Is.EqualTo(3));
        }

        [Test]
        public void Math_Sqrt_And_LengthFamily()
        {
            Assert.That(math.sqrt(9f), Is.EqualTo(3f));
            Assert.That(math.sqrt(0f), Is.EqualTo(0f));
            Assert.That(float.IsNaN(math.sqrt(-1f)), Is.True);

            var v = new float2(3f, 4f);
            Assert.That(math.lengthsq(v), Is.EqualTo(25f));
            Assert.That(math.length(v), Is.EqualTo(5f));

            float2 normalized = math.normalize(v);
            Assert.That(normalized.x, Is.EqualTo(0.6f).Within(1e-6f));
            Assert.That(normalized.y, Is.EqualTo(0.8f).Within(1e-6f));
        }

        [Test]
        public void Math_AbsFloorCeilRoundSignRcp()
        {
            Assert.That(math.abs(-2.5f), Is.EqualTo(2.5f));
            Assert.That(math.abs(2.5f), Is.EqualTo(2.5f));
            Assert.That(math.abs(-0f), Is.EqualTo(0f));
            Assert.That(math.asuint(math.abs(-0f)), Is.EqualTo(0u), "the sign bit must be cleared, not just the value");

            Assert.That(math.floor(1.7f), Is.EqualTo(1f));
            Assert.That(math.floor(-1.2f), Is.EqualTo(-2f));
            Assert.That(math.ceil(1.2f), Is.EqualTo(2f));
            Assert.That(math.ceil(-1.7f), Is.EqualTo(-1f));

            // Ties to even, matching System.Math.Round, which is what Unity.Mathematics forwards to.
            Assert.That(math.round(0.5f), Is.EqualTo(0f));
            Assert.That(math.round(1.5f), Is.EqualTo(2f));
            Assert.That(math.round(2.5f), Is.EqualTo(2f));
            Assert.That(math.round(-0.5f), Is.EqualTo(0f));

            Assert.That(math.sign(5f), Is.EqualTo(1f));
            Assert.That(math.sign(-5f), Is.EqualTo(-1f));
            Assert.That(math.sign(0f), Is.EqualTo(0f));
            Assert.That(math.sign(float.NaN), Is.EqualTo(0f), "neither comparison holds for NaN");

            Assert.That(math.rcp(4f), Is.EqualTo(0.25f));
        }

        [Test]
        public void Math_LerpAndSelect()
        {
            Assert.That(math.lerp(2f, 6f, 0.25f), Is.EqualTo(3f));
            Assert.That(math.lerp(2f, 6f, 0f), Is.EqualTo(2f));
            Assert.That(math.lerp(2f, 6f, 1f), Is.EqualTo(6f));

            // Unity's argument order: the false value comes first.
            Assert.That(math.select(1f, 2f, true), Is.EqualTo(2f));
            Assert.That(math.select(1f, 2f, false), Is.EqualTo(1f));
            Assert.That(math.select(1, 2, true), Is.EqualTo(2));
        }

        [Test]
        public void Math_AsUintAsFloatRoundTrip()
        {
            Assert.That(math.asuint(1f), Is.EqualTo(0x3F800000u));
            Assert.That(math.asfloat(0x3F800000u), Is.EqualTo(1f));
            Assert.That(math.asfloat(math.asuint(-12.5f)), Is.EqualTo(-12.5f));
        }

        [Test]
        public void Math_SdfBakeInnerLoop_ReproducesTheSpecExpression()
        {
            // NowManagedFontBaker.NowSdfBakeJob's per-segment distance step, written out with the same operation
            // order. This is the expression whose bit-exactness the standalone SDF bake depends on.
            var p = new float2(2.5f, 1.5f);
            var a = new float2(0f, 0f);
            var b = new float2(4f, 0f);

            float2 e = b - a;
            float2 w = p - a;

            float lengthSq = math.dot(e, e);
            float t = lengthSq > 1e-12f ? math.saturate(math.dot(w, e) / lengthSq) : 0f;
            float2 d = w - e * t;
            float minDistSq = math.min(float.MaxValue, math.dot(d, d));

            Assert.That(lengthSq, Is.EqualTo(16f));
            Assert.That(t, Is.EqualTo(0.625f));
            Assert.That(d.x, Is.EqualTo(0f));
            Assert.That(d.y, Is.EqualTo(1.5f));
            Assert.That(math.sqrt(minDistSq), Is.EqualTo(1.5f));
        }

        // ---- Profiling ----------------------------------------------------------------------------------------

        [Test]
        public void ProfilerMarker_NameAndScopesAreInert()
        {
            var marker = new ProfilerMarker("Now.Test.Marker");

            Assert.That(marker.Name, Is.EqualTo("Now.Test.Marker"));

            Assert.DoesNotThrow(() =>
            {
                marker.Begin();
                marker.End();
            });

            using (marker.Auto())
            {
            }
        }

        [Test]
        public void ProfilerMarker_CategoryConstructorKeepsTheName()
        {
            var marker = new ProfilerMarker(ProfilerCategory.Render, "Now.Test.Render");

            Assert.That(marker.Name, Is.EqualTo("Now.Test.Render"));
            Assert.That(ProfilerCategory.Scripts.Name, Is.EqualTo("Scripts"));
            Assert.That(ProfilerCategory.Render.Name, Is.EqualTo("Render"));
            Assert.That(ProfilerCategory.Gui.Name, Is.EqualTo("GUI"));
            Assert.That(ProfilerCategory.Internal.Name, Is.EqualTo("Internal"));
        }

        [Test]
        public void ProfilerMarker_DefaultAutoScopeDisposesSafely()
        {
            // The NowEffects.cs:254 / :307 pattern: a default AutoScope field that may never be assigned before
            // it is disposed.
            ProfilerMarker.AutoScope scope = default;

            Assert.DoesNotThrow(() => scope.Dispose());

            // Even with a sink installed, disposing a default scope must not fault; it reports a null name.
            INowProfilerSink previous = NowRuntime.profilerSink;

            try
            {
                NowRuntime.profilerSink = new RecordingProfilerSink();
                ProfilerMarker.AutoScope another = default;
                Assert.DoesNotThrow(() => another.Dispose());
            }
            finally
            {
                NowRuntime.profilerSink = previous;
            }
        }

        [Test]
        public void ProfilerMarker_AutoScopeIsAReadonlyStruct()
        {
            // NowEffects stores an AutoScope in a public struct field, so the type must stay a public readonly
            // struct: a class would allocate per scope and null-ref when default.
            Type scope = typeof(ProfilerMarker.AutoScope);

            Assert.That(scope.IsValueType, Is.True);
            Assert.That(scope.IsPublic || scope.IsNestedPublic, Is.True);
            Assert.That(typeof(IDisposable).IsAssignableFrom(scope), Is.True);
        }

        sealed class RecordingProfilerSink : INowProfilerSink
        {
            public readonly System.Collections.Generic.List<string> events = new System.Collections.Generic.List<string>();

            public void Begin(string name) => events.Add("begin:" + name);

            public void End(string name) => events.Add("end:" + name);
        }

        [Test]
        public void ProfilerMarker_RoutesToNowRuntimeProfilerSink()
        {
            // Design section 3.9: markers route to NowRuntime.profilerSink, which is null by default.
            INowProfilerSink previous = NowRuntime.profilerSink;
            var sink = new RecordingProfilerSink();

            try
            {
                NowRuntime.profilerSink = sink;

                var marker = new ProfilerMarker("Now.Test.Sink");
                marker.Begin();
                marker.End();

                using (marker.Auto())
                {
                }

                Assert.That(sink.events, Is.EqualTo(new[]
                {
                    "begin:Now.Test.Sink",
                    "end:Now.Test.Sink",
                    "begin:Now.Test.Sink",
                    "end:Now.Test.Sink",
                }));
            }
            finally
            {
                NowRuntime.profilerSink = previous;
            }
        }

        [Test]
        public void ProfilerMarker_WithNoSinkInstalled_IsSilent()
        {
            INowProfilerSink previous = NowRuntime.profilerSink;

            try
            {
                NowRuntime.profilerSink = null;

                var marker = new ProfilerMarker("Now.Test.Silent");

                Assert.DoesNotThrow(() =>
                {
                    marker.Begin();
                    marker.End();

                    using (marker.Auto())
                    {
                    }
                });
            }
            finally
            {
                NowRuntime.profilerSink = previous;
            }
        }

        [Test]
        public void ProfilerMarker_IsPublicSoNowProfilerCanExposeStaticFields()
        {
            // NowProfiler.cs:14-59 declares `public static readonly ProfilerMarker` fields.
            Assert.That(typeof(ProfilerMarker).IsPublic, Is.True);
            Assert.That(typeof(ProfilerMarker).IsValueType, Is.True);
        }

        // ---- Allocation behaviour (design section 1.2) ---------------------------------------------------------

        [Test]
        public void SteadyStateOperations_DoNotAllocate()
        {
            var array = new NativeArray<int>(64, Allocator.Persistent);
            INowProfilerSink previousSink = NowRuntime.profilerSink;
            NowRuntime.profilerSink = null;

            try
            {
                // Warm up so first-call JIT and statics are not counted.
                Touch(array);

                long before = GC.GetAllocatedBytesForCurrentThread();
                Touch(array);
                long after = GC.GetAllocatedBytesForCurrentThread();

                Assert.That(after - before, Is.EqualTo(0),
                    "indexing, spans, sub-arrays, markers and math must not allocate on steady-state paths");
            }
            finally
            {
                NowRuntime.profilerSink = previousSink;
                array.Dispose();
            }
        }

        static readonly ProfilerMarker TouchMarker = new ProfilerMarker("Now.Test.Touch");

        static void Touch(NativeArray<int> array)
        {
            using (TouchMarker.Auto())
            {
                Span<int> span = array.AsSpan();

                for (int i = 0; i < array.Length; ++i)
                    array[i] = i;

                span[0] = span[1];

                NativeArray<int> sub = array.GetSubArray(4, 8);
                sub[0] = sub[1];

                var v = new float2(array[0], array[1]);
                array[2] = (int)math.saturate(math.dot(v, v));
            }
        }
    }
}
