// Mirrors the identity-only UnityEngine scene types (GameObject, Component, Behaviour, Transform, RectTransform,
// Camera) and UnityEngine.SceneManagement.Scene for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.4).
//
// These types exist so the untouched NowUI sources compile; they are never instantiated. Every constructor is
// internal and nothing in NowUI.Engine calls one, so standalone code cannot produce an instance and every
// host-identity branch in the core provably takes its null path. MonoBehaviour and Coroutine are deliberately absent:
// their only core users move to host halves (design §5.2), so a compile error naming them is the intended signal.
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>Scene node identity. Never instantiated in the standalone build (design §3.4).</summary>
    public class GameObject : Object
    {
        internal GameObject()
        {
        }

        public bool activeInHierarchy => false;

        public bool activeSelf => false;

        public Transform transform => null;

        public string tag => "";

        public int layer => 0;

        public UnityEngine.SceneManagement.Scene scene => default;
    }

    /// <summary>Base of everything attached to a <see cref="GameObject"/>. Never instantiated (design §3.4).</summary>
    public class Component : Object
    {
        internal Component()
        {
        }

        public GameObject gameObject => null;

        public Transform transform => null;
    }

    /// <summary>A component with an enabled flag. Never instantiated (design §3.4).</summary>
    public class Behaviour : Component
    {
        internal Behaviour()
        {
        }

        // A real auto-property, not a constant: core code both reads and writes Behaviour.enabled, and a get-only
        // member would be a compile error at those sites rather than the inert no-op this file is for.
        public bool enabled { get; set; }

        public bool isActiveAndEnabled => false;
    }

    /// <summary>Identity transform. Every matrix is the identity and every vector its zero/one default (design §3.4).</summary>
    public class Transform : Component
    {
        internal Transform()
        {
        }

        public Matrix4x4 localToWorldMatrix => Matrix4x4.identity;

        public Matrix4x4 worldToLocalMatrix => Matrix4x4.identity;

        public Vector3 position => Vector3.zero;

        public Quaternion rotation => Quaternion.identity;

        public Vector3 lossyScale => Vector3.one;
    }

    /// <summary>uGUI's rect transform. Never instantiated (design §3.4).</summary>
    public sealed class RectTransform : Transform
    {
        internal RectTransform()
        {
        }

        public Rect rect => default;

        public Vector2 pivot => default;

        public Vector2 sizeDelta => default;
    }

    /// <summary>
    /// Camera identity. <c>current</c> and <c>main</c> are always null, which is what makes NowUI's screen-space paths
    /// the only reachable ones in the standalone build (design §3.4).
    /// </summary>
    public sealed class Camera : Behaviour
    {
        internal Camera()
        {
        }

        public static Camera current => null;

        public static Camera main => null;

        public Rect pixelRect => default;

        public int pixelWidth => 0;

        public int pixelHeight => 0;

        public RenderTexture targetTexture => null;

        public Matrix4x4 worldToCameraMatrix => Matrix4x4.identity;

        public Matrix4x4 projectionMatrix => Matrix4x4.identity;
    }
}

namespace UnityEngine.SceneManagement
{
    /// <summary>
    /// Scene handle. Always invalid: the standalone build has no scene manager, so <see cref="IsValid"/> is the branch
    /// every core scene test takes (design §3.4).
    /// </summary>
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Scene
    {
        public bool IsValid()
        {
            return false;
        }

        public string name => "";

        public int buildIndex => -1;
    }
}
