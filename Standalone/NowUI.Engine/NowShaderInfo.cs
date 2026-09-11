// Not a Unity type: the shim's description of a shader program, supplied by the host's INowResourceProvider and hung
// off UnityEngine.Shader.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.10 (the type table), §3.5 (`Shader.cs` / `Material.HasProperty`).
//
// WHY this exists at all: under Unity, `Material.HasProperty` answers from the compiled shader's declared uniform list,
// so it is true for a property the material has never been assigned. NowUI depends on that — `Now.cs:1020`
// (`_NowPremultipliedTexture`), `NowFont.cs:1478` (`_NowUITextOutlineOnlyPass`) and `NowSdf.cs:3508`
// (`_NowUITextSdfEncoding`, `_NowSdfAbiVersion`) all gate a `SetFloat` on `HasProperty` and would silently take the
// wrong branch if the shim answered only from the property bag. There is no shader compiler on this side of the
// build, so the declaration list has to be data the host hands us.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace NowUI.Engine
{
    /// <summary>
    /// The declared surface of one shader program: its name, how many passes it has, the uniforms it declares (with
    /// their defaults) and the keywords it understands. Mutable by design — a resource provider builds one of these
    /// while parsing its asset bundle and then hands it to a <see cref="UnityEngine.Shader"/>, after which nothing
    /// writes to it.
    /// </summary>
    public sealed class NowShaderInfo
    {
        /// <summary>The Unity shader name, e.g. <c>"NowUI/UI Rectangle"</c> or <c>"Hidden/NowUI/SDF Image Field"</c>.</summary>
        public string name;

        /// <summary>Passes the program declares. Backs <c>Material.passCount</c>; 1 for every NowUI surface shader.</summary>
        public int passCount;

        /// <summary>Declared float/int uniforms and the value a fresh <c>Material</c> starts with.</summary>
        public readonly Dictionary<int, float> floatDefaults;

        /// <summary>Declared vector/color uniforms and the value a fresh <c>Material</c> starts with.</summary>
        public readonly Dictionary<int, Vector4> vectorDefaults;

        /// <summary>Declared texture samplers. No defaults: an unassigned sampler reads as null, as it does in Unity.</summary>
        public readonly HashSet<int> textureSlots;

        /// <summary>Keywords the program declares. Informational; <c>Material</c> does not validate against it.</summary>
        public string[] keywords;

        public NowShaderInfo()
            : this(null, 1)
        {
        }

        public NowShaderInfo(string name)
            : this(name, 1)
        {
        }

        public NowShaderInfo(string name, int passCount)
        {
            this.name = name ?? "";
            // Unity reports at least one pass for any program that resolved at all; a zero here would make
            // `SetPass(0)` look out of range for every material built from this info.
            this.passCount = passCount < 1 ? 1 : passCount;
            floatDefaults = new Dictionary<int, float>();
            vectorDefaults = new Dictionary<int, Vector4>();
            textureSlots = new HashSet<int>();
            keywords = Array.Empty<string>();
        }

        /// <summary>Declares a float uniform with its default value. Returns this, so a provider can chain.</summary>
        public NowShaderInfo DeclareFloat(string propertyName, float defaultValue)
        {
            return DeclareFloat(Shader.PropertyToID(propertyName), defaultValue);
        }

        public NowShaderInfo DeclareFloat(int nameID, float defaultValue)
        {
            floatDefaults[nameID] = defaultValue;
            return this;
        }

        /// <summary>Declares a vector or color uniform with its default value.</summary>
        public NowShaderInfo DeclareVector(string propertyName, Vector4 defaultValue)
        {
            return DeclareVector(Shader.PropertyToID(propertyName), defaultValue);
        }

        public NowShaderInfo DeclareVector(int nameID, Vector4 defaultValue)
        {
            vectorDefaults[nameID] = defaultValue;
            return this;
        }

        /// <summary>Declares a texture sampler. Unassigned samplers have no default, exactly as in Unity.</summary>
        public NowShaderInfo DeclareTexture(string propertyName)
        {
            return DeclareTexture(Shader.PropertyToID(propertyName));
        }

        public NowShaderInfo DeclareTexture(int nameID)
        {
            textureSlots.Add(nameID);
            return this;
        }

        /// <summary>
        /// Whether the program declares <paramref name="nameID"/> as any kind of uniform. This is the half of
        /// <c>Material.HasProperty</c> that answers for a property nobody has assigned yet.
        /// </summary>
        public bool DeclaresProperty(int nameID)
        {
            return floatDefaults.ContainsKey(nameID)
                || vectorDefaults.ContainsKey(nameID)
                || textureSlots.Contains(nameID);
        }

        /// <summary>String overload of <see cref="DeclaresProperty(int)"/>, interning the name first.</summary>
        public bool DeclaresProperty(string propertyName)
        {
            return DeclaresProperty(Shader.PropertyToID(propertyName));
        }
    }
}
