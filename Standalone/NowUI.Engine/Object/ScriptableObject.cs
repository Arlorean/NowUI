// Mirrors UnityEngine.ScriptableObject for the NowUI standalone build.
// Design: Docs/Standalone/StandaloneCoreDesign.md (§3.4 member list, §6.5 message dispatch).
using System;
using System.Collections.Generic;
using System.Reflection;
using NowUI.Engine;

namespace UnityEngine
{
    /// <summary>
    /// A serialisable engine object with no scene presence. NowUI uses it for <c>NowThemeAsset</c>, <c>NowFont</c>,
    /// <c>NowLottieAsset</c> and friends, and depends on Unity's message order: <c>CreateInstance</c> runs
    /// <c>Awake</c> then <c>OnEnable</c>, and destruction runs <c>OnDisable</c> then <c>OnDestroy</c> (design §6.5).
    /// </summary>
    public class ScriptableObject : Object
    {
        // One cache entry per concrete subclass. Reflection over the type hierarchy happens once; every later
        // CreateInstance/Destroy of the same type just walks four small arrays.
        private static readonly Dictionary<Type, Messages> s_Messages = new Dictionary<Type, Messages>();

        private static readonly Type[] s_NoParameters = new Type[0];
        private static readonly object[] s_NoArguments = new object[0];

        protected ScriptableObject()
        {
        }

        /// <summary>
        /// Creates an instance of <paramref name="type"/> and dispatches <c>Awake</c> then <c>OnEnable</c>.
        /// </summary>
        /// <remarks>
        /// Uses <see cref="Activator"/> rather than a registration table on purpose (design §3.4): subclasses such as
        /// the test assembly's <c>TrackingFont</c> and <c>RecordingRenderer</c> are declared outside NowUI.Runtime, so
        /// any registry would have to be populated by the very code that is being tested.
        /// </remarks>
        public static ScriptableObject CreateInstance(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));
            if (!typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException(
                    "CreateInstance requires a ScriptableObject-derived type, got " + type.FullName + ".", nameof(type));
            if (type.IsAbstract)
                throw new ArgumentException("Cannot create an instance of the abstract type " + type.FullName + ".", nameof(type));

            // nonPublic: true so a subclass with only a private or protected parameterless constructor works, which is
            // the shape Unity asset classes normally have.
            ScriptableObject instance = (ScriptableObject)Activator.CreateInstance(type, nonPublic: true);

            // Unity names a CreateInstance'd object after its type; assets renamed on import keep the file name
            // instead, which the standalone build has no equivalent for.
            instance.name = type.Name;

            Messages messages = GetMessages(type);
            Dispatch(instance, messages.awake);
            Dispatch(instance, messages.onEnable);
            return instance;
        }

        /// <summary>Generic form of <see cref="CreateInstance(Type)"/>.</summary>
        public static T CreateInstance<T>() where T : ScriptableObject
        {
            return (T)CreateInstance(typeof(T));
        }

        /// <summary>
        /// Runs <c>OnDisable</c> then <c>OnDestroy</c>. Called by <c>Object</c>'s destroy pipeline while the object is
        /// still alive, so a handler that reads <c>name</c> behaves as it does in Unity (design §6.3, §6.5).
        /// </summary>
        internal void DispatchDestroyMessages()
        {
            Messages messages = GetMessages(GetType());
            Dispatch(this, messages.onDisable);
            Dispatch(this, messages.onDestroy);
        }

        /// <summary>Drops the reflection cache. Called by <c>NowRuntime.ResetAll</c>; purely a memory concern.</summary>
        internal static void ClearMessageCache()
        {
            lock (s_Messages)
            {
                s_Messages.Clear();
            }
        }

        private static void Dispatch(ScriptableObject target, MethodInfo[] methods)
        {
            for (int i = 0; i < methods.Length; i++)
            {
                try
                {
                    methods[i].Invoke(target, s_NoArguments);
                }
                catch (TargetInvocationException e)
                {
                    // Unity logs a throwing lifecycle message and carries on rather than failing the caller; a
                    // NowThemeAsset whose OnEnable throws must still be a usable object afterwards.
                    NowRuntime.LogMessageException(methods[i], target, e.InnerException ?? e);
                }
            }
        }

        private static Messages GetMessages(Type type)
        {
            lock (s_Messages)
            {
                Messages cached;
                if (s_Messages.TryGetValue(type, out cached))
                    return cached;

                cached = new Messages(
                    Collect(type, "Awake"),
                    Collect(type, "OnEnable"),
                    Collect(type, "OnDisable"),
                    Collect(type, "OnDestroy"));
                s_Messages.Add(type, cached);
                return cached;
            }
        }

        private static MethodInfo[] Collect(Type type, string name)
        {
            // Walk to the base first, so a base class's OnEnable runs before the subclass's, which is the order Unity's
            // message dispatch produces.
            List<Type> chain = null;
            for (Type t = type; t != null && t != typeof(ScriptableObject) && t != typeof(Object); t = t.BaseType)
            {
                if (chain == null)
                    chain = new List<Type>();
                chain.Add(t);
            }

            if (chain == null)
                return Array.Empty<MethodInfo>();

            List<MethodInfo> found = null;
            List<MethodInfo> baseDefinitions = null;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                MethodInfo m = chain[i].GetMethod(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
                    null,
                    s_NoParameters,
                    null);

                if (m == null || m.ReturnType != typeof(void) || m.IsAbstract || m.IsGenericMethodDefinition)
                    continue;

                // A virtual message declared on the base and overridden in the subclass produces two DeclaredOnly
                // MethodInfos, but invoking either one dispatches virtually to the override. Keeping only the first
                // (base-most) entry per base definition is what stops the override from running twice.
                MethodInfo baseDefinition = m.GetBaseDefinition();
                if (baseDefinitions != null && baseDefinitions.Contains(baseDefinition))
                    continue;

                if (found == null)
                {
                    found = new List<MethodInfo>(2);
                    baseDefinitions = new List<MethodInfo>(2);
                }

                found.Add(m);
                baseDefinitions.Add(baseDefinition);
            }

            return found == null ? Array.Empty<MethodInfo>() : found.ToArray();
        }

        private sealed class Messages
        {
            public readonly MethodInfo[] awake;
            public readonly MethodInfo[] onEnable;
            public readonly MethodInfo[] onDisable;
            public readonly MethodInfo[] onDestroy;

            public Messages(MethodInfo[] awake, MethodInfo[] onEnable, MethodInfo[] onDisable, MethodInfo[] onDestroy)
            {
                this.awake = awake;
                this.onEnable = onEnable;
                this.onDisable = onDisable;
                this.onDestroy = onDestroy;
            }
        }
    }
}
