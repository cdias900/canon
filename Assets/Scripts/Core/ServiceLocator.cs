using System;
using System.Collections.Generic;

namespace SheepGate.Core
{
    /// <summary>
    /// Minimal type-keyed registry for the few long-lived objects that must be reachable from
    /// any scene without inspector wiring. Static storage survives scene loads, which is exactly
    /// what the POC needs: BootSequence registers, every later scene resolves.
    /// </summary>
    public static class ServiceLocator
    {
        static readonly Dictionary<Type, object> Services = new Dictionary<Type, object>();

        /// <summary>Registers a service, replacing any previous instance of the same type.</summary>
        public static void Register<T>(T service) where T : class
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service), "Cannot register a null service for " + typeof(T).FullName + ".");
            }

            Services[typeof(T)] = service;
        }

        /// <summary>Resolves a registered service. Throws when the type was never registered.</summary>
        public static T Get<T>() where T : class
        {
            if (Services.TryGetValue(typeof(T), out var found) && found is T typed)
            {
                return typed;
            }

            throw new InvalidOperationException("No service registered for type " + typeof(T).FullName + ".");
        }

        /// <summary>Resolves a registered service without throwing.</summary>
        public static bool TryGet<T>(out T service) where T : class
        {
            if (Services.TryGetValue(typeof(T), out var found) && found is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        /// <summary>Drops every registration. Called at the start of a boot so a restart is clean.</summary>
        public static void Clear()
        {
            Services.Clear();
        }
    }
}
