// Tests for U2 -- UnityEngine serialization/inspector/component attributes (Engine/Attributes/Attributes.cs).
//
// Governed by StandaloneCoreDesign.md section 3.3. The acceptance check in design section 8 is "a reflection test
// reads tooltip/header/min/max/minLines back", so the bulk of this file decorates a sample type and reads the
// values off it -- which is also exactly what NowInspector.cs 1207-1264 does at runtime.
//
// The declaration-shape tests at the bottom are the oracle for the U2 deviations listed in Attributes.cs's header:
// their expected values were read out of UnityEngine.CoreModule.dll's metadata for Unity 6000.4.0f1 (AttributeUsage
// blobs, TypeAttributes flags and constructor IL), not taken from design section 3.3's code block, which
// transcribes several of them inaccurately.
//
// `UnityEngine.RangeAttribute` and `UnityEngine.PropertyAttribute` are written fully qualified throughout: NUnit
// declares types of both names, so the unqualified spellings are ambiguous in a test file.

using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace NowUI.Engine.Tests.Attributes
{
    [TestFixture]
    public class AttributesTests
    {
        // -----------------------------------------------------------------------------------------------------
        // The sample under reflection. Declared in the *test* assembly on purpose: TP section 6.4 has
        // NowInspectorTests declaring these attributes on test-assembly fields, so that is the shape that has to
        // work. Fields are written by nobody and read by reflection only, hence the pragma.
        // -----------------------------------------------------------------------------------------------------
#pragma warning disable CS0649, CS0169
        [CreateAssetMenu(menuName = "NowUI/Sample Asset", fileName = "SampleAsset", order = 42)]
        [PreferBinarySerialization]
        [AddComponentMenu("NowUI/Sample", 17)]
        [ExecuteAlways]
        [DisallowMultipleComponent]
        [DefaultExecutionOrder(-250)]
        [RequireComponent(typeof(SampleDependencyA), typeof(SampleDependencyB))]
        sealed class Sample
        {
            [SerializeField] string _serialized;

            [SerializeField, HideInInspector] string _hidden;

            [SerializeReference] object _reference;

            [Tooltip("A tooltip.")] public float tooltipped;

            [Header("Section header")] public float headered;

            [Space(12.5f)] public float spaced;

            [Space] public float spacedDefault;

            [UnityEngine.Range(-2f, 7.25f)] public float ranged;

            [Min(3f)] public float minned;

            [TextArea(2, 6)] public string area;

            [TextArea] public string areaDefault;

            [Multiline(4)] public string multi;

            [Multiline] public string multiDefault;

            [Header("Ordered", order = 3)] public float ordered;

            public float plain;

            [NonSerialized] public float notSerialized;

            // Unity's PropertyAttribute is valid on properties as well as fields, so this has to compile.
            [UnityEngine.Range(0f, 1f)] public float rangedProperty { get; set; }

            [ContextMenu("Do The Thing")]
            void DoTheThing()
            {
            }

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            static void ResetStatics()
            {
            }

            [RuntimeInitializeOnLoadMethod]
            static void ResetStaticsDefaultLoadType()
            {
            }
        }

        sealed class SampleDependencyA
        {
        }

        sealed class SampleDependencyB
        {
        }

        // Unity's TooltipAttribute is valid on AttributeTargets.All, which is how component classes get one.
        [Tooltip("A class-level tooltip.")]
        sealed class TooltippedClass
        {
        }
#pragma warning restore CS0649, CS0169

        static FieldInfo Field(string name)
        {
            var f = typeof(Sample).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(f, Is.Not.Null, $"sample field '{name}' is missing");
            return f;
        }

        static MethodInfo Method(string name)
        {
            var m = typeof(Sample).GetMethod(name,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"sample method '{name}' is missing");
            return m;
        }

        // -----------------------------------------------------------------------------------------------------
        // The acceptance check: reflect the values back off the decorated sample.
        // -----------------------------------------------------------------------------------------------------

        [Test]
        public void TooltipRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.tooltipped)).GetCustomAttribute<TooltipAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.tooltip, Is.EqualTo("A tooltip."));
        }

        [Test]
        public void TooltipIsLegalOnATypeAsWellAsAField()
        {
            var attr = typeof(TooltippedClass).GetCustomAttribute<TooltipAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.tooltip, Is.EqualTo("A class-level tooltip."));
        }

        [Test]
        public void HeaderRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.headered)).GetCustomAttribute<HeaderAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.header, Is.EqualTo("Section header"));
        }

        [Test]
        public void SpaceRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.spaced)).GetCustomAttribute<SpaceAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.height, Is.EqualTo(12.5f));
        }

        [Test]
        public void SpaceParameterlessIsEightUnitsTall()
        {
            // U2 deviation 1: design section 3.3 leaves the default unstated; Unity's ctor is `height = 8f`, and
            // NowInspector consumes `space?.height ?? 0f` as the literal gap, so getting this wrong would silently
            // drop every bare [Space].
            var attr = Field(nameof(Sample.spacedDefault)).GetCustomAttribute<SpaceAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.height, Is.EqualTo(8f));
        }

        [Test]
        public void RangeRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.ranged)).GetCustomAttribute<UnityEngine.RangeAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.min, Is.EqualTo(-2f));
            Assert.That(attr.max, Is.EqualTo(7.25f));
        }

        [Test]
        public void RangeIsLegalOnAProperty()
        {
            var p = typeof(Sample).GetProperty(nameof(Sample.rangedProperty));
            Assert.That(p, Is.Not.Null);
            var attr = p.GetCustomAttribute<UnityEngine.RangeAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.max, Is.EqualTo(1f));
        }

        [Test]
        public void MinRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.minned)).GetCustomAttribute<MinAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.min, Is.EqualTo(3f));
        }

        [Test]
        public void TextAreaRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.area)).GetCustomAttribute<TextAreaAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.minLines, Is.EqualTo(2));
            Assert.That(attr.maxLines, Is.EqualTo(6));
        }

        [Test]
        public void TextAreaParameterlessIsThreeByThree()
        {
            var attr = Field(nameof(Sample.areaDefault)).GetCustomAttribute<TextAreaAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.minLines, Is.EqualTo(3));
            Assert.That(attr.maxLines, Is.EqualTo(3));
        }

        [Test]
        public void MultilineRoundTripsThroughReflection()
        {
            var attr = Field(nameof(Sample.multi)).GetCustomAttribute<MultilineAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.lines, Is.EqualTo(4));
        }

        [Test]
        public void MultilineParameterlessIsThreeLines()
        {
            var attr = Field(nameof(Sample.multiDefault)).GetCustomAttribute<MultilineAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.lines, Is.EqualTo(3));
        }

        [Test]
        public void PropertyAttributeOrderIsSettableByNamedArgument()
        {
            var attr = Field(nameof(Sample.ordered)).GetCustomAttribute<HeaderAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.order, Is.EqualTo(3));
        }

        [Test]
        public void UndecoratedFieldYieldsNoPropertyAttributes()
        {
            var f = Field(nameof(Sample.plain));
            Assert.That(f.GetCustomAttribute<TooltipAttribute>(inherit: false), Is.Null);
            Assert.That(f.GetCustomAttribute<HeaderAttribute>(inherit: false), Is.Null);
            Assert.That(f.GetCustomAttribute<UnityEngine.RangeAttribute>(inherit: false), Is.Null);
            Assert.That(f.GetCustomAttribute<MinAttribute>(inherit: false), Is.Null);
        }

        [Test]
        public void ApplyToCollectionFollowsWhichBaseConstructorEachAttributeCalls()
        {
            // Verified against Unity's ctor IL: Header and Space call base(true); Tooltip, Range, Min, TextArea
            // and Multiline call the parameterless base, which is base(false).
            Assert.That(new HeaderAttribute("h").applyToCollection, Is.True);
            Assert.That(new SpaceAttribute().applyToCollection, Is.True);
            Assert.That(new SpaceAttribute(4f).applyToCollection, Is.True);
            Assert.That(new TooltipAttribute("t").applyToCollection, Is.False);
            Assert.That(new UnityEngine.RangeAttribute(0f, 1f).applyToCollection, Is.False);
            Assert.That(new MinAttribute(0f).applyToCollection, Is.False);
            Assert.That(new TextAreaAttribute().applyToCollection, Is.False);
            Assert.That(new MultilineAttribute().applyToCollection, Is.False);
        }

        // -----------------------------------------------------------------------------------------------------
        // The serialization markers, exercised the way NowInspector.cs 1207-1211 exercises them.
        // -----------------------------------------------------------------------------------------------------

        [Test]
        public void SerializeFieldIsDefinedOnPrivateSerializedField()
        {
            Assert.That(Field("_serialized").IsDefined(typeof(SerializeField), inherit: false), Is.True);
            Assert.That(Field(nameof(Sample.plain)).IsDefined(typeof(SerializeField), inherit: false), Is.False);
        }

        [Test]
        public void HideInInspectorIsDefinedOnHiddenField()
        {
            Assert.That(Field("_hidden").IsDefined(typeof(HideInInspector), inherit: false), Is.True);
            Assert.That(Field("_serialized").IsDefined(typeof(HideInInspector), inherit: false), Is.False);
        }

        [Test]
        public void SerializeReferenceIsDefinedOnManagedReferenceField()
        {
            Assert.That(Field("_reference").IsDefined(typeof(SerializeReference), inherit: false), Is.True);
        }

        [Test]
        public void NowInspectorFieldFilterSelectsTheSameFieldsAsUnity()
        {
            // Reproduces NowInspector.ShouldSerialize (NowInspector.cs 1207-1212): public-or-[SerializeField],
            // minus [NonSerialized], minus [HideInInspector].
            bool Serialized(FieldInfo f) =>
                (f.IsPublic || f.IsDefined(typeof(SerializeField), inherit: false)) &&
                !f.IsDefined(typeof(NonSerializedAttribute), inherit: false) &&
                !f.IsDefined(typeof(HideInInspector), inherit: false);

            var names = typeof(Sample)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(Serialized)
                .Select(f => f.Name)
                .ToArray();

            Assert.That(names, Does.Contain("_serialized"));
            Assert.That(names, Does.Contain(nameof(Sample.plain)));
            Assert.That(names, Does.Contain(nameof(Sample.tooltipped)));
            Assert.That(names, Does.Not.Contain("_hidden"));                    // [HideInInspector]
            Assert.That(names, Does.Not.Contain("_reference"));                 // private, no [SerializeField]
            Assert.That(names, Does.Not.Contain(nameof(Sample.notSerialized))); // [NonSerialized]
        }

        // -----------------------------------------------------------------------------------------------------
        // Asset attributes.
        // -----------------------------------------------------------------------------------------------------

        [Test]
        public void CreateAssetMenuRoundTripsNamedArguments()
        {
            var attr = typeof(Sample).GetCustomAttribute<CreateAssetMenuAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.menuName, Is.EqualTo("NowUI/Sample Asset"));
            Assert.That(attr.fileName, Is.EqualTo("SampleAsset"));
            Assert.That(attr.order, Is.EqualTo(42));
        }

        [Test]
        public void CreateAssetMenuDefaultsAreNullAndZero()
        {
            var attr = new CreateAssetMenuAttribute();
            Assert.That(attr.menuName, Is.Null);
            Assert.That(attr.fileName, Is.Null);
            Assert.That(attr.order, Is.EqualTo(0));
        }

        [Test]
        public void PreferBinarySerializationIsDefinedOnTheSample()
        {
            // U2 deviation 2: the type name has no `Attribute` suffix -- typeof() would not compile otherwise.
            Assert.That(typeof(PreferBinarySerialization).Name, Is.EqualTo("PreferBinarySerialization"));
            Assert.That(typeof(Sample).IsDefined(typeof(PreferBinarySerialization), inherit: false), Is.True);
        }

        // -----------------------------------------------------------------------------------------------------
        // RuntimeInitializeOnLoadMethod -- the one attribute NowRuntime.ResetAll actually scans for (design H.15).
        // -----------------------------------------------------------------------------------------------------

        [Test]
        public void RuntimeInitializeOnLoadMethodCarriesTheLoadType()
        {
            var attr = Method("ResetStatics").GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.loadType, Is.EqualTo(RuntimeInitializeLoadType.SubsystemRegistration));
        }

        [Test]
        public void RuntimeInitializeOnLoadMethodDefaultsToAfterSceneLoad()
        {
            var attr = Method("ResetStaticsDefaultLoadType")
                .GetCustomAttribute<RuntimeInitializeOnLoadMethodAttribute>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.loadType, Is.EqualTo(RuntimeInitializeLoadType.AfterSceneLoad));
        }

        [Test]
        public void RuntimeInitializeOnLoadMethodScanFindsBothSampleMethods()
        {
            // The shape of NowRuntime.ResetAll's reflection pass.
            var found = typeof(Sample)
                .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute), inherit: false))
                .Select(m => m.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();

            Assert.That(found, Is.EqualTo(new[] { "ResetStatics", "ResetStaticsDefaultLoadType" }));
        }

        [Test]
        public void RuntimeInitializeLoadTypeSetterIsNotPublic()
        {
            var p = typeof(RuntimeInitializeOnLoadMethodAttribute).GetProperty("loadType");
            Assert.That(p, Is.Not.Null);
            Assert.That(p.GetGetMethod(nonPublic: false), Is.Not.Null, "loadType must have a public getter");
            Assert.That(p.GetSetMethod(nonPublic: false), Is.Null, "loadType's setter is private in Unity");
        }

        // -----------------------------------------------------------------------------------------------------
        // ISerializationCallbackReceiver.
        // -----------------------------------------------------------------------------------------------------

        sealed class Receiver : ISerializationCallbackReceiver
        {
            public int before;
            public int after;
            public void OnBeforeSerialize() => before++;
            public void OnAfterDeserialize() => after++;
        }

        [Test]
        public void SerializationCallbackReceiverIsImplementableAndDispatches()
        {
            var r = new Receiver();
            ISerializationCallbackReceiver iface = r;
            iface.OnBeforeSerialize();
            iface.OnAfterDeserialize();
            iface.OnAfterDeserialize();
            Assert.That(r.before, Is.EqualTo(1));
            Assert.That(r.after, Is.EqualTo(2));
        }

        [Test]
        public void SerializationCallbackReceiverDeclaresExactlyTwoVoidMethods()
        {
            var t = typeof(ISerializationCallbackReceiver);
            Assert.That(t.IsInterface, Is.True);
            var names = t.GetMethods().Select(m => m.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            Assert.That(names, Is.EqualTo(new[] { "OnAfterDeserialize", "OnBeforeSerialize" }));
            foreach (var m in t.GetMethods())
            {
                Assert.That(m.ReturnType, Is.EqualTo(typeof(void)));
                Assert.That(m.GetParameters(), Is.Empty);
            }
        }

        // -----------------------------------------------------------------------------------------------------
        // Component attributes.
        // -----------------------------------------------------------------------------------------------------

        [Test]
        public void AddComponentMenuRoundTripsMenuAndOrder()
        {
            var attr = typeof(Sample).GetCustomAttribute<AddComponentMenu>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.componentMenu, Is.EqualTo("NowUI/Sample"));
            Assert.That(attr.componentOrder, Is.EqualTo(17));
        }

        [Test]
        public void AddComponentMenuSingleArgumentOrderIsZero()
        {
            var attr = new AddComponentMenu("NowUI/Other");
            Assert.That(attr.componentMenu, Is.EqualTo("NowUI/Other"));
            Assert.That(attr.componentOrder, Is.EqualTo(0));
        }

        [Test]
        public void AddComponentMenuExposesNoSetters()
        {
            foreach (var name in new[] { "componentMenu", "componentOrder" })
            {
                var p = typeof(AddComponentMenu).GetProperty(name);
                Assert.That(p, Is.Not.Null, name);
                Assert.That(p.GetSetMethod(nonPublic: false), Is.Null, name + " is get-only in Unity");
            }
        }

        [Test]
        public void ExecuteAlwaysAndDisallowMultipleAreDefinedOnTheSample()
        {
            Assert.That(typeof(Sample).IsDefined(typeof(ExecuteAlways), inherit: false), Is.True);
            Assert.That(typeof(Sample).IsDefined(typeof(DisallowMultipleComponent), inherit: false), Is.True);
            Assert.That(typeof(Sample).IsDefined(typeof(ExecuteInEditMode), inherit: false), Is.False);
        }

        [Test]
        public void DefaultExecutionOrderRoundTripsItsOrder()
        {
            var attr = typeof(Sample).GetCustomAttribute<DefaultExecutionOrder>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.order, Is.EqualTo(-250));
            Assert.That(typeof(DefaultExecutionOrder).GetProperty("order").GetSetMethod(nonPublic: false), Is.Null);
        }

        [Test]
        public void RequireComponentExposesTheThreeTypeSlots()
        {
            var attr = typeof(Sample).GetCustomAttribute<RequireComponent>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.m_Type0, Is.EqualTo(typeof(SampleDependencyA)));
            Assert.That(attr.m_Type1, Is.EqualTo(typeof(SampleDependencyB)));
            Assert.That(attr.m_Type2, Is.Null);
        }

        [Test]
        public void RequireComponentSingleAndTripleFormsFillTheRightSlots()
        {
            var one = new RequireComponent(typeof(SampleDependencyA));
            Assert.That(one.m_Type0, Is.EqualTo(typeof(SampleDependencyA)));
            Assert.That(one.m_Type1, Is.Null);
            Assert.That(one.m_Type2, Is.Null);

            var three = new RequireComponent(typeof(SampleDependencyA), typeof(SampleDependencyB), typeof(Sample));
            Assert.That(three.m_Type0, Is.EqualTo(typeof(SampleDependencyA)));
            Assert.That(three.m_Type1, Is.EqualTo(typeof(SampleDependencyB)));
            Assert.That(three.m_Type2, Is.EqualTo(typeof(Sample)));
        }

        [Test]
        public void ContextMenuRoundTripsItemValidateAndPriority()
        {
            var attr = Method("DoTheThing").GetCustomAttribute<ContextMenu>(inherit: false);
            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.menuItem, Is.EqualTo("Do The Thing"));
            Assert.That(attr.validate, Is.False);
            Assert.That(attr.priority, Is.EqualTo(1000000));   // Unity's default, read from its ctor IL.

            var validator = new ContextMenu("Do The Thing", true);
            Assert.That(validator.validate, Is.True);
            Assert.That(validator.priority, Is.EqualTo(1000000));

            var prioritised = new ContextMenu("Do The Thing", true, 5);
            Assert.That(prioritised.priority, Is.EqualTo(5));
        }

        // -----------------------------------------------------------------------------------------------------
        // Declaration shape. Expected values here were read out of Unity 6000.4.0f1's UnityEngine.CoreModule.dll
        // metadata; they are the oracle for the U2 deviations from design section 3.3.
        // -----------------------------------------------------------------------------------------------------

        static AttributeUsageAttribute DeclaredUsage(Type t) =>
            t.GetCustomAttribute<AttributeUsageAttribute>(inherit: false);

        static void AssertUsage(Type t, AttributeTargets validOn, bool inherited, bool allowMultiple)
        {
            var u = DeclaredUsage(t);
            Assert.That(u, Is.Not.Null, $"{t.Name} declares no AttributeUsage");
            Assert.That(u.ValidOn, Is.EqualTo(validOn), t.Name + ".ValidOn");
            Assert.That(u.Inherited, Is.EqualTo(inherited), t.Name + ".Inherited");
            Assert.That(u.AllowMultiple, Is.EqualTo(allowMultiple), t.Name + ".AllowMultiple");
        }

        [Test]
        public void SerializationMarkersMatchUnitysDeclarations()
        {
            AssertUsage(typeof(SerializeField), AttributeTargets.Field, inherited: true, allowMultiple: false);
            AssertUsage(typeof(SerializeReference), AttributeTargets.Field, inherited: true, allowMultiple: false);

            // U2 deviation 3: Unity declares NO AttributeUsage on HideInInspector, so it is legal anywhere.
            Assert.That(DeclaredUsage(typeof(HideInInspector)), Is.Null);

            foreach (var t in new[] { typeof(SerializeField), typeof(SerializeReference), typeof(HideInInspector) })
            {
                Assert.That(t.IsSealed, Is.True, t.Name);
                Assert.That(t.BaseType, Is.EqualTo(typeof(Attribute)), t.Name);
            }
        }

        [Test]
        public void PropertyAttributeMatchesUnitysDeclaration()
        {
            var t = typeof(UnityEngine.PropertyAttribute);
            Assert.That(t.IsAbstract, Is.True);
            Assert.That(t.BaseType, Is.EqualTo(typeof(Attribute)));

            // U2 deviation 4: Field|Property and AllowMultiple = false, not the design's Field|Method|Class|Struct
            // with AllowMultiple = true.
            AssertUsage(t, AttributeTargets.Field | AttributeTargets.Property, inherited: true, allowMultiple: false);

            var order = t.GetProperty("order");
            Assert.That(order, Is.Not.Null);
            Assert.That(order.PropertyType, Is.EqualTo(typeof(int)));
            Assert.That(order.GetSetMethod(nonPublic: false), Is.Not.Null, "order is publicly settable in Unity");

            var apply = t.GetProperty("applyToCollection");
            Assert.That(apply, Is.Not.Null);
            Assert.That(apply.PropertyType, Is.EqualTo(typeof(bool)));
            Assert.That(apply.GetSetMethod(nonPublic: false), Is.Null, "applyToCollection is get-only in Unity");

            // Both constructors are protected, so a derived attribute can pick either.
            var ctors = t.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(ctors.Length, Is.EqualTo(2));
            Assert.That(ctors.All(c => c.IsFamily), Is.True, "PropertyAttribute's ctors are protected in Unity");
        }

        [Test]
        public void InspectorAttributesMatchUnitysPerTypeAttributeUsage()
        {
            // U2 deviation 5: each of these declares its own usage in Unity, and they are not all the same.
            const AttributeTargets fieldOrProperty = AttributeTargets.Field | AttributeTargets.Property;

            AssertUsage(typeof(TooltipAttribute), AttributeTargets.All, inherited: true, allowMultiple: false);
            AssertUsage(typeof(HeaderAttribute), fieldOrProperty, inherited: true, allowMultiple: true);
            AssertUsage(typeof(SpaceAttribute), AttributeTargets.Field, inherited: true, allowMultiple: true);
            AssertUsage(typeof(UnityEngine.RangeAttribute), fieldOrProperty, inherited: true, allowMultiple: false);
            AssertUsage(typeof(MinAttribute), fieldOrProperty, inherited: true, allowMultiple: false);
            AssertUsage(typeof(TextAreaAttribute), fieldOrProperty, inherited: true, allowMultiple: false);
            AssertUsage(typeof(MultilineAttribute), fieldOrProperty, inherited: true, allowMultiple: false);
        }

        [TestCase(typeof(TooltipAttribute))]
        [TestCase(typeof(HeaderAttribute))]
        [TestCase(typeof(SpaceAttribute))]
        [TestCase(typeof(UnityEngine.RangeAttribute))]
        [TestCase(typeof(MinAttribute))]
        [TestCase(typeof(TextAreaAttribute))]
        [TestCase(typeof(MultilineAttribute))]
        public void InspectorAttributesDeriveFromPropertyAttribute(Type t)
        {
            Assert.That(t.BaseType, Is.EqualTo(typeof(UnityEngine.PropertyAttribute)));
        }

        [Test]
        public void SealednessMatchesUnity()
        {
            // U2 deviation 6: design section 3.3 marks everything sealed; Unity does not seal these five.
            foreach (var t in new[]
                     {
                         typeof(TooltipAttribute), typeof(HeaderAttribute), typeof(SpaceAttribute),
                         typeof(DefaultExecutionOrder), typeof(RuntimeInitializeOnLoadMethodAttribute)
                     })
                Assert.That(t.IsSealed, Is.False, t.Name + " is not sealed in Unity");

            foreach (var t in new[]
                     {
                         typeof(UnityEngine.RangeAttribute), typeof(MinAttribute), typeof(TextAreaAttribute),
                         typeof(MultilineAttribute), typeof(CreateAssetMenuAttribute),
                         typeof(PreferBinarySerialization), typeof(AddComponentMenu), typeof(ExecuteAlways),
                         typeof(ExecuteInEditMode), typeof(RequireComponent),
                         typeof(DisallowMultipleComponent), typeof(ContextMenu)
                     })
                Assert.That(t.IsSealed, Is.True, t.Name + " is sealed in Unity");
        }

        [Test]
        public void AssetAndLifecycleAttributesMatchUnitysDeclarations()
        {
            // U2 deviation 7: Unity adds Inherited = false to CreateAssetMenu.
            AssertUsage(typeof(CreateAssetMenuAttribute), AttributeTargets.Class, inherited: false, allowMultiple: false);
            AssertUsage(typeof(PreferBinarySerialization), AttributeTargets.Class, inherited: true, allowMultiple: false);
            AssertUsage(typeof(RuntimeInitializeOnLoadMethodAttribute), AttributeTargets.Method,
                inherited: true, allowMultiple: false);
        }

        [Test]
        public void ComponentAttributesMatchUnitysDeclarations()
        {
            AssertUsage(typeof(ContextMenu), AttributeTargets.Method, inherited: true, allowMultiple: true);
            AssertUsage(typeof(RequireComponent), AttributeTargets.Class, inherited: true, allowMultiple: true);
            AssertUsage(typeof(DisallowMultipleComponent), AttributeTargets.Class, inherited: false, allowMultiple: false);
            AssertUsage(typeof(DefaultExecutionOrder), AttributeTargets.Class, inherited: true, allowMultiple: false);

            // Unity declares no AttributeUsage on these three.
            Assert.That(DeclaredUsage(typeof(AddComponentMenu)), Is.Null);
            Assert.That(DeclaredUsage(typeof(ExecuteAlways)), Is.Null);
            Assert.That(DeclaredUsage(typeof(ExecuteInEditMode)), Is.Null);
        }

        [Test]
        public void EveryTypeIsPublicAndLivesInTheUnityEngineNamespace()
        {
            Type[] all =
            {
                typeof(SerializeField), typeof(SerializeReference), typeof(HideInInspector),
                typeof(UnityEngine.PropertyAttribute), typeof(TooltipAttribute), typeof(HeaderAttribute),
                typeof(SpaceAttribute), typeof(UnityEngine.RangeAttribute), typeof(MinAttribute),
                typeof(TextAreaAttribute), typeof(MultilineAttribute), typeof(CreateAssetMenuAttribute),
                typeof(PreferBinarySerialization), typeof(RuntimeInitializeOnLoadMethodAttribute),
                typeof(ISerializationCallbackReceiver), typeof(AddComponentMenu), typeof(ExecuteAlways),
                typeof(ExecuteInEditMode), typeof(RequireComponent), typeof(DisallowMultipleComponent),
                typeof(DefaultExecutionOrder), typeof(ContextMenu)
            };

            foreach (var t in all)
            {
                Assert.That(t.Namespace, Is.EqualTo("UnityEngine"), t.Name);
                Assert.That(t.IsPublic, Is.True, t.Name);
            }
        }

        [Test]
        public void PayloadsArePublicReadonlyFieldsAndMarkersCarryNoState()
        {
            // The shim's attributes must stay no-ops: no hidden engine handles, nothing that could keep an
            // allocation alive. Field shape is the cheapest guard against someone adding state later.
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var t in new[] { typeof(SerializeField), typeof(SerializeReference), typeof(HideInInspector),
                                      typeof(PreferBinarySerialization), typeof(ExecuteAlways),
                                      typeof(ExecuteInEditMode), typeof(DisallowMultipleComponent) })
                Assert.That(t.GetFields(any), Is.Empty, t.Name + " must carry no state");

            void ReadonlyPayload(Type t, params string[] names)
            {
                var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Public);
                Assert.That(fields.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal),
                    Is.EqualTo(names.OrderBy(n => n, StringComparer.Ordinal)), t.Name);
                foreach (var f in fields)
                    Assert.That(f.IsInitOnly, Is.True, t.Name + "." + f.Name + " is readonly in Unity");
            }

            ReadonlyPayload(typeof(TooltipAttribute), "tooltip");
            ReadonlyPayload(typeof(HeaderAttribute), "header");
            ReadonlyPayload(typeof(SpaceAttribute), "height");
            ReadonlyPayload(typeof(UnityEngine.RangeAttribute), "min", "max");
            ReadonlyPayload(typeof(MinAttribute), "min");
            ReadonlyPayload(typeof(TextAreaAttribute), "minLines", "maxLines");
            ReadonlyPayload(typeof(MultilineAttribute), "lines");
            ReadonlyPayload(typeof(ContextMenu), "menuItem", "validate", "priority");

            // RequireComponent is the exception: Unity's three slots are mutable public fields.
            var slots = typeof(RequireComponent).GetFields(BindingFlags.Instance | BindingFlags.Public);
            Assert.That(slots.Select(f => f.Name), Is.EqualTo(new[] { "m_Type0", "m_Type1", "m_Type2" }));
            Assert.That(slots.All(f => !f.IsInitOnly), Is.True, "RequireComponent's slots are mutable in Unity");
        }
    }
}
