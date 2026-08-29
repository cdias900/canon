using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SheepGate.Player
{
    /// <summary>
    /// Name-based bridge to the world module's interactables.
    ///
    /// The architecture contract freezes the systems the player module may reference by type, and
    /// the interactable base class is not one of them. Binding to it at compile time would make a
    /// single signature change in another module break the whole assembly, so the lookup is done
    /// by method name instead: a miss costs one feature, never the build. Components implementing
    /// <see cref="IInteractable"/> skip reflection entirely.
    /// </summary>
    public static class InteractBridge
    {
        private static readonly string[] MethodNames = { "Interact", "OnInteract" };
        private static readonly Dictionary<Type, MethodInfo> MethodCache = new Dictionary<Type, MethodInfo>();

        /// <summary>True when the component exposes an interaction entry point.</summary>
        public static bool CanInteract(Component component)
        {
            if (component == null) return false;
            if (component is IInteractable) return true;
            return ResolveMethod(component.GetType()) != null;
        }

        /// <summary>Fires the interaction. Returns false when the component has no entry point.</summary>
        public static bool Invoke(Component component)
        {
            if (component == null) return false;

            var typed = component as IInteractable;
            if (typed != null)
            {
                typed.Interact();
                return true;
            }

            MethodInfo method = ResolveMethod(component.GetType());
            if (method == null) return false;

            try
            {
                method.Invoke(component, null);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("InteractBridge: interaction on " + component.GetType().Name +
                                 " threw: " + exception.Message);
                return false;
            }
        }

        private static MethodInfo ResolveMethod(Type type)
        {
            MethodInfo cached;
            if (MethodCache.TryGetValue(type, out cached)) return cached;

            MethodInfo resolved = null;
            for (int i = 0; i < MethodNames.Length && resolved == null; i++)
            {
                try
                {
                    resolved = type.GetMethod(
                        MethodNames[i],
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        Type.EmptyTypes,
                        null);
                }
                catch (Exception)
                {
                    resolved = null;
                }
            }

            MethodCache[type] = resolved;
            return resolved;
        }
    }
}
