// Mirrors UnityEngine.Sprite for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md §3.5 ("Sprite.cs" - a plain record of texture/textureRect/border).
// Inventory: Docs/Standalone/UnityDependencyInventory.md §A, row `UnityEngine.Sprite` - only NowRectangle.SetSprite
// (NowRectangle.cs:348-367) and NowSdf's image shapes read one, and between them they touch `texture`,
// `textureRect` and `border` and nothing else.
//
// Two Unity conventions are easy to get backwards and are spelled out here:
//   * the `pivot` ARGUMENT to Create is normalised (0..1 across the rect); the `pivot` PROPERTY is in pixels. They
//     are different quantities with the same name, and Unity really does convert between them.
//   * `textureRect` is the region actually occupied in the texture. For a sprite that is not atlas-packed - which is
//     every sprite the standalone build can produce, since there is no sprite packer - that is exactly `rect`.

namespace UnityEngine
{
    /// <summary>
    /// A named rectangle of a texture, with nine-slice borders. There is no mesh here: NowUI draws sprites through its
    /// own quad builders and only ever asks a sprite where its texels are.
    /// </summary>
    public sealed class Sprite : Object
    {
        private Texture2D m_Texture;
        private Rect m_Rect;
        private Vector2 m_NormalizedPivot;
        private float m_PixelsPerUnit;
        private Vector4 m_Border;

        // Constructed only through Create, as in Unity: `new Sprite()` is not part of the public API there either.
        private Sprite()
        {
        }

        /// <summary>The texture the sprite's texels live in.</summary>
        public Texture2D texture
        {
            get { return m_Texture; }
        }

        /// <summary>The sprite's rectangle in texture pixel space, with the origin at the bottom-left of the texture.</summary>
        public Rect rect
        {
            get { return m_Rect; }
        }

        /// <summary>
        /// The region actually occupied in <see cref="texture"/>. Equal to <see cref="rect"/> here, because nothing
        /// in the standalone build packs sprites into an atlas.
        /// </summary>
        public Rect textureRect
        {
            get { return m_Rect; }
        }

        /// <summary>Nine-slice borders in pixels, as (left, bottom, right, top).</summary>
        public Vector4 border
        {
            get { return m_Border; }
        }

        /// <summary>The pivot in PIXELS relative to <see cref="rect"/>, which is what Unity's property reports.</summary>
        public Vector2 pivot
        {
            get { return new Vector2(m_NormalizedPivot.x * m_Rect.width, m_NormalizedPivot.y * m_Rect.height); }
        }

        /// <summary>Texture pixels per world unit.</summary>
        public float pixelsPerUnit
        {
            get { return m_PixelsPerUnit; }
        }

        /// <summary>
        /// Builds a sprite over part of a texture. <paramref name="pivot"/> is normalised;
        /// <paramref name="extrude"/> and <paramref name="meshType"/> are accepted for signature fidelity and have
        /// no effect, because the shim generates no sprite mesh.
        /// </summary>
        public static Sprite Create(
            Texture2D texture,
            Rect rect,
            Vector2 pivot,
            float pixelsPerUnit = 100F,
            uint extrude = 0,
            SpriteMeshType meshType = SpriteMeshType.Tight,
            Vector4 border = default)
        {
            var sprite = new Sprite
            {
                m_Texture = texture,
                m_Rect = rect,
                m_NormalizedPivot = pivot,
                // Unity refuses a non-positive pixelsPerUnit and falls back to 100; matching that keeps a divide by
                // it from producing an infinity in a layout calculation.
                m_PixelsPerUnit = pixelsPerUnit > 0F ? pixelsPerUnit : 100F,
                m_Border = border,
            };

            // Unity names a runtime-created sprite after its texture, which is what a frame debugger shows. The
            // fake-null `==` rather than ReferenceEquals: `name` throws on a destroyed object (VT §13), and being
            // handed an already-destroyed texture must not turn Create into a throw site.
            sprite.name = texture == null ? "" : texture.name;
            return sprite;
        }
    }
}
