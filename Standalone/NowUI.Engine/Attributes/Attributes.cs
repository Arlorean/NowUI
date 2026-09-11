// Mirrors the UnityEngine serialization, inspector and component attributes plus ISerializationCallbackReceiver.
//
// Governed by StandaloneCoreDesign.md section 3.3 ("Engine/Attributes/Attributes.cs"), with the per-member
// referencing lists in UnityDependencyInventory.md section A (rows SerializeField, HideInInspector,
// TooltipAttribute, HeaderAttribute, SpaceAttribute/RangeAttribute/TextAreaAttribute/MultilineAttribute,
// MinAttribute, CreateAssetMenu, PreferBinarySerialization, RuntimeInitializeOnLoadMethodAttribute,
// ISerializationCallbackReceiver, AddComponentMenu, ExecuteAlways).
//
// Every type here is a pure no-op marker. Nothing in the standalone build acts on them except:
//   * NowInspector.cs 1207-1264, which reflects the VALUES back off decorated fields -- SerializeField /
//     HideInInspector / HeaderAttribute.header / SpaceAttribute.height / RangeAttribute.min|max / MinAttribute.min /
//     TextAreaAttribute.minLines|maxLines / MultilineAttribute.lines; and
//   * NowRuntime.ResetAll, which scans the registered assemblies for RuntimeInitializeOnLoadMethodAttribute
//     (design H.15).
// The names below are therefore load-bearing and are spelled exactly as Unity spells them -- including the several
// that carry no `Attribute` suffix (SerializeField, HideInInspector, PreferBinarySerialization, ContextMenu,
// AddComponentMenu, ExecuteAlways, ExecuteInEditMode, RequireComponent, DisallowMultipleComponent,
// DefaultExecutionOrder). NowUI writes `typeof(SerializeField)` and `typeof(HideInInspector)` literally, so the
// suffix-less spelling is required rather than stylistic.
//
// ---------------------------------------------------------------------------------------------------------------
// DEVIATIONS FROM DESIGN SECTION 3.3 -- all reported with unit U2; each is a place where the design's code block
// transcribes Unity inaccurately. The declarations here follow Unity 6000.4.0f1 as verified by reading the
// AttributeUsage blobs, type flags and constructor IL out of UnityEngine.CoreModule.dll's metadata (behaviour
// verification only -- no Unity source was consulted or copied). Design section 3.3's own stated reason for these
// types is that NowUI reflects over them, so matching the real shape serves that intent. Each deviation WIDENS
// what compiles or corrects a value; none narrows anything.
//
//   1. SpaceAttribute()      design leaves height unspecified -> Unity's parameterless ctor sets height = 8f
//                            (IL: ldc.r4 0x41000000). NowInspector uses `space?.height ?? 0f` as the literal gap,
//                            so a 0 default would silently drop the [Space] gap in the standalone build.
//   2. PreferBinarySerialization   design spells it `PreferBinarySerializationAttribute`; Unity's type name has no
//                            `Attribute` suffix. Declaring both names would make `[PreferBinarySerialization]`
//                            an ambiguous attribute reference (CS1614), so only Unity's name can exist.
//   3. HideInInspector       design pins it to AttributeTargets.Field; Unity declares no AttributeUsage at all.
//   4. PropertyAttribute     design says Field|Method|Class|Struct, AllowMultiple = true; Unity says
//                            Field|Property, AllowMultiple = false. Note Unity's set includes Property, which the
//                            design's does not -- `[Range(0,1)] public float Foo { get; set; }` compiles in Unity.
//                            Unity also carries `applyToCollection` and a protected `(bool)` ctor, both omitted by
//                            the design; they are reproduced here because derived attributes select between the
//                            two base ctors and the choice is publicly observable.
//   5. Derived property attributes   design gives them no AttributeUsage (so they would inherit the base's); Unity
//                            declares one on each, and they differ from each other (Tooltip is valid on All;
//                            Space is Field-only and AllowMultiple; Header is AllowMultiple).
//   6. sealed                design marks all of these sealed; in Unity, TooltipAttribute, HeaderAttribute,
//                            SpaceAttribute, DefaultExecutionOrder and RuntimeInitializeOnLoadMethodAttribute are
//                            NOT sealed.
//   7. CreateAssetMenuAttribute   Unity adds Inherited = false, which the design omits.
//   8. ContextMenu, AddComponentMenu, ExecuteAlways, ExecuteInEditMode, RequireComponent,
//      DisallowMultipleComponent, DefaultExecutionOrder   design says "omitted (host-only files)"; the U2 work
//                            order assigns them. They are declared here: the addition is strictly additive (no
//                            standalone-compiled file references them, so it cannot change behaviour) while their
//                            absence would break a host compiling a MonoBehaviour-shaped file against the shim.
// ---------------------------------------------------------------------------------------------------------------

using System;

namespace UnityEngine
{
    // ---------------------------------------------------------------------------------------------------------
    // Serialization markers. No members at all: NowUI only ever asks IsDefined for these.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Marks a non-public field as serialized (and, for NowInspector, as inspectable).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute
    {
    }

    /// <summary>Marks a field for polymorphic ("managed reference") serialization.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeReference : Attribute
    {
    }

    /// <summary>
    /// Hides an otherwise-serialized field from the inspector. Deliberately carries no AttributeUsage: Unity
    /// declares none, so it is legal on any target (deviation 3 above).
    /// </summary>
    public sealed class HideInInspector : Attribute
    {
    }

    // ---------------------------------------------------------------------------------------------------------
    // Property attributes.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Base class for the inspector decoration attributes. Valid on fields and properties, not on methods or
    /// types, and single-use -- NowInspector calls the singular <c>GetCustomAttribute&lt;T&gt;</c>, which would
    /// throw <see cref="System.Reflection.AmbiguousMatchException"/> on a duplicate exactly as it does in Unity.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public abstract class PropertyAttribute : Attribute
    {
        /// <summary>Ordering hint between several decorations on one field. Unused by the standalone build.</summary>
        public int order { get; set; }

        /// <summary>
        /// Whether the decoration applies to the elements of a collection field rather than to the field itself.
        /// Read-only after construction, and chosen by which base constructor a derived attribute calls.
        /// </summary>
        public bool applyToCollection { get; }

        protected PropertyAttribute()
            : this(false)
        {
        }

        protected PropertyAttribute(bool applyToCollection)
        {
            this.applyToCollection = applyToCollection;
        }
    }

    /// <summary>
    /// Hover text. Read as <c>.tooltip</c>. Valid on <see cref="AttributeTargets.All"/> in Unity -- it decorates
    /// component classes and enum members as well as fields -- so the usage here is deliberately wide.
    /// </summary>
    [AttributeUsage(AttributeTargets.All, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute
    {
        // Unity exposes the payloads of these attributes as public readonly FIELDS, not properties. NowInspector
        // reads `.tooltip` / `.header` / `.height` / ... directly, and a public-API diff would flag a property, so
        // the field form is what has to be reproduced.
        public readonly string tooltip;

        public TooltipAttribute(string tooltip)
        {
            this.tooltip = tooltip;
        }
    }

    /// <summary>
    /// A bold section label drawn above a field. Read as <c>.header</c> (NowInspector.cs 1227-1228).
    /// AllowMultiple is true in Unity, so two headers on one field compile -- and then make NowInspector's
    /// singular GetCustomAttribute throw, which is Unity-faithful.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : PropertyAttribute
    {
        public readonly string header;

        public HeaderAttribute(string header)
            : base(true)
        {
            this.header = header;
        }
    }

    /// <summary>Vertical spacing above a field. Read as <c>.height</c> (NowInspector.cs 1230-1231).</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class SpaceAttribute : PropertyAttribute
    {
        public readonly float height;

        /// <summary>
        /// Unity's parameterless form is 8 units tall, NOT zero. NowInspector consumes `space?.height ?? 0f`
        /// directly as the gap, so a zero here would make bare <c>[Space]</c> render nothing (deviation 1 above).
        /// </summary>
        public SpaceAttribute()
            : base(true)
        {
            height = 8f;
        }

        public SpaceAttribute(float height)
            : base(true)
        {
            this.height = height;
        }
    }

    /// <summary>Clamps a numeric field to [min, max]. Read as <c>.min</c>/<c>.max</c> (NowInspector.cs 1233-1240).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class RangeAttribute : PropertyAttribute
    {
        public readonly float min;
        public readonly float max;

        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    /// <summary>Lower bound for a numeric field. Read as <c>.min</c> (NowInspector.cs 1242-1248).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class MinAttribute : PropertyAttribute
    {
        public readonly float min;

        public MinAttribute(float min)
        {
            this.min = min;
        }
    }

    /// <summary>
    /// Draws a string field as a text area that grows between two line counts. Read as
    /// <c>.minLines</c>/<c>.maxLines</c> (NowInspector.cs 1250-1256).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class TextAreaAttribute : PropertyAttribute
    {
        public readonly int minLines;
        public readonly int maxLines;

        /// <summary>Unity's parameterless form is three lines tall and does not grow.</summary>
        public TextAreaAttribute()
        {
            minLines = 3;
            maxLines = 3;
        }

        public TextAreaAttribute(int minLines, int maxLines)
        {
            this.minLines = minLines;
            this.maxLines = maxLines;
        }
    }

    /// <summary>
    /// Draws a string field as a fixed-height text area. Read as <c>.lines</c> (NowInspector.cs 1258-1264).
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public sealed class MultilineAttribute : PropertyAttribute
    {
        public readonly int lines;

        /// <summary>Unity's parameterless form is three lines tall.</summary>
        public MultilineAttribute()
        {
            lines = 3;
        }

        public MultilineAttribute(int lines)
        {
            this.lines = lines;
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // Asset attributes.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Editor-only "Create/..." menu entry for a ScriptableObject. Carried by NowMaterialControlRenderer,
    /// NowFontFamily and NowThemeAsset; only <c>menuName</c> and <c>fileName</c> are ever set (inventory A).
    /// These are settable properties rather than readonly fields because Unity's usage form is
    /// <c>[CreateAssetMenu(menuName = "...", fileName = "...")]</c> -- named-argument initialisation, which needs
    /// a parameterless constructor and public setters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    /// <summary>
    /// Asks the editor to serialize an asset in binary form (NowFont). Pure marker. Note the type name carries no
    /// `Attribute` suffix -- that is Unity's real name (deviation 2 above).
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class PreferBinarySerialization : Attribute
    {
    }

    // ---------------------------------------------------------------------------------------------------------
    // Lifecycle.
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Marks a static parameterless method to be invoked at startup. In the standalone build this is inert data:
    /// NowRuntime.ResetAll reflects over the registered assemblies looking for it (design H.15) instead of an
    /// engine calling it. 39 NowUI files carry it, always with RuntimeInitializeLoadType.SubsystemRegistration.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        /// <summary>
        /// Unity's parameterless form means <see cref="RuntimeInitializeLoadType.AfterSceneLoad"/>. That happens
        /// to be the enum's zero value, but it is assigned explicitly so the intent survives an enum reordering.
        /// </summary>
        public RuntimeInitializeOnLoadMethodAttribute()
        {
            loadType = RuntimeInitializeLoadType.AfterSceneLoad;
        }

        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType)
        {
            this.loadType = loadType;
        }

        public RuntimeInitializeLoadType loadType { get; private set; }
    }

    /// <summary>
    /// Two-way hook around serialization. NowThemeAsset implements it (inventory A). Nothing in the standalone
    /// build calls the members, but the interface must exist for that base-class list to compile.
    /// </summary>
    public interface ISerializationCallbackReceiver
    {
        void OnBeforeSerialize();
        void OnAfterDeserialize();
    }

    // ---------------------------------------------------------------------------------------------------------
    // Component attributes (deviation 8 above -- assigned by the U2 work order, omitted by design 3.3).
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Editor "Add Component" menu path. Carried by 8 host files (inventory A). Like Unity, it declares no
    /// AttributeUsage.
    /// </summary>
    public sealed class AddComponentMenu : Attribute
    {
        // Unity names the members componentMenu/componentOrder, not menuName/order. Keep the odd spelling, and
        // keep them get-only: Unity exposes no setters.
        public string componentMenu { get; }
        public int componentOrder { get; }

        public AddComponentMenu(string menuName)
        {
            componentMenu = menuName;
            componentOrder = 0;
        }

        public AddComponentMenu(string menuName, int order)
        {
            componentMenu = menuName;
            componentOrder = order;
        }
    }

    /// <summary>Runs the component's messages in edit mode as well as play mode. Carried by 4 host files.</summary>
    public sealed class ExecuteAlways : Attribute
    {
    }

    /// <summary>The legacy spelling of <see cref="ExecuteAlways"/>. A distinct type in Unity, so distinct here.</summary>
    public sealed class ExecuteInEditMode : Attribute
    {
    }

    /// <summary>
    /// Declares up to three components the decorated component depends on. Unity's public surface really is the
    /// three raw mutable fields m_Type0/m_Type1/m_Type2 -- "m_" naming and all -- so that is what is reproduced.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class RequireComponent : Attribute
    {
        public Type m_Type0;
        public Type m_Type1;
        public Type m_Type2;

        public RequireComponent(Type requiredComponent)
        {
            m_Type0 = requiredComponent;
        }

        public RequireComponent(Type requiredComponent, Type requiredComponent2)
        {
            m_Type0 = requiredComponent;
            m_Type1 = requiredComponent2;
        }

        public RequireComponent(Type requiredComponent, Type requiredComponent2, Type requiredComponent3)
        {
            m_Type0 = requiredComponent;
            m_Type1 = requiredComponent2;
            m_Type2 = requiredComponent3;
        }
    }

    /// <summary>Forbids adding a second instance of the component to one GameObject.</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute
    {
    }

    /// <summary>Script execution order for a component type; lower runs earlier. Not sealed, as in Unity.</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order)
        {
            this.order = order;
        }

        public int order { get; }
    }

    /// <summary>
    /// Adds a command to a component's inspector context menu. AllowMultiple is true because one method can back
    /// several entries, and a validate function is a second attribute on the same method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class ContextMenu : Attribute
    {
        public readonly string menuItem;
        public readonly bool validate;
        public readonly int priority;

        public ContextMenu(string itemName)
            : this(itemName, false)
        {
        }

        // 1000000 is Unity's own default priority (IL: ldc.i4 0x000F4240), not a round number picked here.
        public ContextMenu(string itemName, bool isValidateFunction)
            : this(itemName, isValidateFunction, 1000000)
        {
        }

        public ContextMenu(string itemName, bool isValidateFunction, int priority)
        {
            menuItem = itemName;
            validate = isValidateFunction;
            this.priority = priority;
        }
    }
}
