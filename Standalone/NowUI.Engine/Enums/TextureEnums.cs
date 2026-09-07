// Mirrors the UnityEngine texture / mesh-shape enums for the NowUI standalone build.
// Governed by StandaloneCoreDesign.md §3.2 ("Enums/TextureEnums.cs"), UnityValueTypeSemantics.md §14 and
// inventory §A.4; values verified against UnityEngine.CoreModule.dll from Unity 6000.4.0f1.
//
// FILE-SPLIT NOTE (U1): design §3.2 assigns FilterMode, TextureWrapMode, TextureFormat, RenderTextureFormat,
// RenderTextureReadWrite and CubemapFace to this file too, but they already exist in the sibling
// Enums/GraphicsEnums.cs — a file that is not in the design's §3.12 file map and that U1 does not own, so it is
// left untouched rather than duplicated (duplicating them would be a CS0101 build break for every other unit).
// Their values there match Unity, so the assembly's contract is correct; only the file boundary differs from the
// design. If GraphicsEnums.cs is ever removed, those six enums belong back here.

namespace UnityEngine
{
    /// <summary>How a render texture is laid out for stereo (VR) rendering (design §3.2, inventory §A.4).</summary>
    /// <remarks>NowWorldGlassBackdrop / NowGlassBackdropSurface only ever pass <see cref="None"/>.</remarks>
    public enum VRTextureUsage
    {
        None = 0,
        OneEye = 1,
        TwoEyes = 2,
        DeviceSpecific = 3,
    }

    /// <summary>
    /// How a mesh's index buffer is interpreted (design §3.2, inventory §A.4 — NowGlassRenderer uses
    /// <see cref="Triangles"/> only).
    /// </summary>
    /// <remarks>
    /// <c>1</c> is missing: it was the removed <c>Quads</c>-strip topology. The gap is Unity's, not an omission.
    /// </remarks>
    public enum MeshTopology
    {
        Triangles = 0,
        Quads = 2,
        Lines = 3,
        LineStrip = 4,
        Points = 5,
    }

    /// <summary>How a sprite's mesh is generated from its texture (design §3.2).</summary>
    public enum SpriteMeshType
    {
        FullRect = 0,
        Tight = 1,
    }
}
