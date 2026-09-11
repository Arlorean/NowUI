// Mirrors UnityEngine.Rendering.RenderTargetIdentifier for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.5 ("Engine/Graphics/Rendering/") and inventory §A line 97
// (CommandBuffer.SetRenderTarget / Blit / SetGlobalTexture take one).

using System;
using System.Runtime.InteropServices;

namespace UnityEngine.Rendering
{
    /// <summary>
    /// A render target named in one of three ways: an engine-owned builtin, a shader property id standing for a
    /// temporary RT, or a concrete <see cref="Texture"/> handle (design §3.5).
    /// </summary>
    /// <remarks>
    /// <c>default(RenderTargetIdentifier)</c> is <see cref="Kind.None"/>, which means "whatever is currently bound",
    /// not "no target". NowUI relies on that: <c>NowRenderer</c> passes a default identifier when it wants the caller's
    /// active target left alone.
    /// </remarks>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderTargetIdentifier : IEquatable<RenderTargetIdentifier>
    {
        /// <summary>Which of the union's members is meaningful.</summary>
        internal enum Kind
        {
            /// <summary>The currently bound target; this is what <c>default</c> means.</summary>
            None = 0,
            BuiltinType = 1,
            NameID = 2,
            Texture = 3,
        }

        internal Kind kind;
        internal BuiltinRenderTextureType builtin;
        internal int nameID;
        internal Texture texture;
        internal int mipLevel;
        internal CubemapFace face;
        internal int depthSlice;

        /// <summary>Bind every slice of an array/3D target (Unity's sentinel, -1).</summary>
        public const int AllDepthSlices = -1;

        /// <summary>Names an engine-owned target such as <see cref="BuiltinRenderTextureType.CameraTarget"/>.</summary>
        public RenderTargetIdentifier(BuiltinRenderTextureType type)
        {
            kind = Kind.BuiltinType;
            builtin = type;
            nameID = 0;
            texture = null;
            mipLevel = 0;
            face = CubemapFace.Unknown;
            depthSlice = 0;
        }

        /// <summary>Names a temporary RT by the shader property id it was allocated under.</summary>
        public RenderTargetIdentifier(int nameID)
        {
            kind = Kind.NameID;
            builtin = BuiltinRenderTextureType.None;
            this.nameID = nameID;
            texture = null;
            mipLevel = 0;
            face = CubemapFace.Unknown;
            depthSlice = 0;
        }

        /// <summary>Names a concrete texture handle.</summary>
        public RenderTargetIdentifier(Texture tex)
        {
            // A null texture is not the same as "no target": Unity keeps the Texture kind so a later comparison against
            // another null-textured identifier still says equal. The shim copies that.
            kind = Kind.Texture;
            builtin = BuiltinRenderTextureType.None;
            nameID = 0;
            texture = tex;
            mipLevel = 0;
            face = CubemapFace.Unknown;
            depthSlice = 0;
        }

        /// <summary>Names one mip/face/slice of a render texture.</summary>
        public RenderTargetIdentifier(RenderTexture rt, int mipLevel, CubemapFace cubeFace = CubemapFace.Unknown, int depthSlice = 0)
        {
            kind = Kind.Texture;
            builtin = BuiltinRenderTextureType.None;
            nameID = 0;
            texture = rt;
            this.mipLevel = mipLevel;
            face = cubeFace;
            this.depthSlice = depthSlice;
        }

        /// <summary>Re-targets an existing identifier at a different mip/face/slice, keeping what it names.</summary>
        public RenderTargetIdentifier(RenderTargetIdentifier renderTargetIdentifier, int mipLevel, CubemapFace cubeFace = CubemapFace.Unknown, int depthSlice = 0)
        {
            kind = renderTargetIdentifier.kind;
            builtin = renderTargetIdentifier.builtin;
            nameID = renderTargetIdentifier.nameID;
            texture = renderTargetIdentifier.texture;
            this.mipLevel = mipLevel;
            face = cubeFace;
            this.depthSlice = depthSlice;
        }

        /// <summary>Names a builtin target by mip/face/slice.</summary>
        public RenderTargetIdentifier(BuiltinRenderTextureType type, int mipLevel, CubemapFace cubeFace = CubemapFace.Unknown, int depthSlice = 0)
        {
            kind = Kind.BuiltinType;
            builtin = type;
            nameID = 0;
            texture = null;
            this.mipLevel = mipLevel;
            face = cubeFace;
            this.depthSlice = depthSlice;
        }

        /// <summary>Names a temporary RT by property id, at a specific mip/face/slice.</summary>
        public RenderTargetIdentifier(int nameID, int mipLevel, CubemapFace cubeFace = CubemapFace.Unknown, int depthSlice = 0)
        {
            kind = Kind.NameID;
            builtin = BuiltinRenderTextureType.None;
            this.nameID = nameID;
            texture = null;
            this.mipLevel = mipLevel;
            face = cubeFace;
            this.depthSlice = depthSlice;
        }

        public static implicit operator RenderTargetIdentifier(BuiltinRenderTextureType type) => new RenderTargetIdentifier(type);

        public static implicit operator RenderTargetIdentifier(int nameID) => new RenderTargetIdentifier(nameID);

        // RenderTexture reaches this through its Texture base, exactly as under Unity, so there is deliberately no
        // separate RenderTexture operator: adding one would be API Unity does not have.
        public static implicit operator RenderTargetIdentifier(Texture tex) => new RenderTargetIdentifier(tex);

        public readonly bool Equals(RenderTargetIdentifier other) =>
            kind == other.kind &&
            builtin == other.builtin &&
            nameID == other.nameID &&
            // ReferenceEquals, not ==: the Object operator treats a destroyed handle as null, and two *different*
            // destroyed targets must not compare equal just because both read as null (hazard D.1 #1).
            ReferenceEquals(texture, other.texture) &&
            mipLevel == other.mipLevel &&
            face == other.face &&
            depthSlice == other.depthSlice;

        public override readonly bool Equals(object obj) => obj is RenderTargetIdentifier other && Equals(other);

        public override readonly int GetHashCode()
        {
            int textureHash = texture is null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(texture);
            return HashCode.Combine((int)kind, (int)builtin, nameID, textureHash, mipLevel, (int)face, depthSlice);
        }

        public static bool operator ==(RenderTargetIdentifier lhs, RenderTargetIdentifier rhs) => lhs.Equals(rhs);

        public static bool operator !=(RenderTargetIdentifier lhs, RenderTargetIdentifier rhs) => !lhs.Equals(rhs);

        public override readonly string ToString()
        {
            switch (kind)
            {
                case Kind.BuiltinType: return $"Builtin {builtin} mip={mipLevel} face={face} slice={depthSlice}";
                case Kind.NameID: return $"NameID {nameID} mip={mipLevel} face={face} slice={depthSlice}";
                case Kind.Texture: return $"Texture {(texture is null ? "null" : texture.name)} mip={mipLevel} face={face} slice={depthSlice}";
                default: return "None";
            }
        }
    }
}
