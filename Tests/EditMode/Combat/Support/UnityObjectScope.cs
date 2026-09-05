using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Tracks Unity objects a test creates and destroys them all at cleanup, replacing the
    /// per-file created-object lists and try/finally + DestroyImmediate scaffolding. Use as a
    /// fixture field with a [TearDown] that calls <see cref="Dispose"/> (idempotent, reusable
    /// across tests), or in a using block for a single test.
    /// </summary>
    public sealed class UnityObjectScope : IDisposable
    {
        private readonly List<Object> tracked = new List<Object>();

        /// <summary>Registers an object for cleanup and returns it, so creation sites stay one-liners.</summary>
        public T Track<T>(T unityObject) where T : Object
        {
            if (unityObject != null)
            {
                tracked.Add(unityObject);
            }

            return unityObject;
        }

        public GameObject NewGameObject(string name)
        {
            return Track(new GameObject(name));
        }

        public void Dispose()
        {
            // Destroy in reverse creation order so children/dependents go before their sources.
            for (var index = tracked.Count - 1; index >= 0; index--)
            {
                if (tracked[index] != null)
                {
                    Object.DestroyImmediate(tracked[index]);
                }
            }

            tracked.Clear();
        }
    }
}
