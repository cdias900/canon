using System;
using System.Reflection;
using UnityEngine;

namespace SheepGate.Dialogue
{
    /// <summary>
    /// Late-bound hand-off from a dialogue bubble to the in-app chapter reader.
    ///
    /// Why late bound: the reader is owned by another module and the dialogue module must keep
    /// compiling whether that module exposes a static entry point, a singleton, or a plain scene
    /// component. A missed lookup degrades to one logged warning on a secondary button; a wrong
    /// compile-time reference would take the whole assembly down with it.
    ///
    /// Integration: assign <see cref="Opener"/> once during boot and no lookup ever runs.
    /// </summary>
    public static class ChapterReaderBridge
    {
        /// <summary>
        /// Explicit hook, checked before anything else. Set this from the boot sequence to wire the
        /// reader directly, e.g. <c>ChapterReaderBridge.Opener = MyReader.Open;</c>
        /// </summary>
        public static Action<string> Opener;

        private const string ReaderTypeName = "ChapterReaderUI";

        private static readonly string[] PreferredTypeNames =
        {
            "SheepGate.UI.ChapterReaderUI",
            "SheepGate.Scripture.ChapterReaderUI"
        };

        private static readonly string[] InstanceAccessorNames = { "Instance", "Current" };

        private static bool lookupDone;
        private static bool warned;
        private static Type readerType;
        private static MethodInfo staticOpen;
        private static MethodInfo instanceOpen;
        private static MethodInfo staticCreate;

        /// <summary>
        /// Opens the reader on the given chapter reference. Returns true only when the reader was
        /// actually reached — callers use that to decide whether to report the open.
        /// </summary>
        public static bool Open(string chapterRef)
        {
            if (string.IsNullOrEmpty(chapterRef))
            {
                return false;
            }

            if (Opener != null)
            {
                Opener(chapterRef);
                return true;
            }

            if (!lookupDone)
            {
                lookupDone = true;
                Lookup();
            }

            object[] args = { chapterRef };

            try
            {
                if (staticOpen != null)
                {
                    staticOpen.Invoke(null, args);
                    return true;
                }

                if (instanceOpen != null)
                {
                    object target = FindTarget();
                    if (target != null)
                    {
                        instanceOpen.Invoke(target, args);
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Dialogue] Chapter reader threw while opening " + chapterRef + ": " + e);
                return false;
            }

            if (!warned)
            {
                warned = true;
                Debug.LogWarning(
                    "[Dialogue] No chapter reader was found for " + chapterRef +
                    ". Assign SheepGate.Dialogue.ChapterReaderBridge.Opener during boot to wire one.");
            }

            return false;
        }

        /// <summary>Test/boot seam: forces the next Open call to look the reader up again.</summary>
        public static void ResetLookup()
        {
            lookupDone = false;
            warned = false;
            readerType = null;
            staticOpen = null;
            instanceOpen = null;
            staticCreate = null;
        }

        private static void Lookup()
        {
            readerType = FindReaderType();
            if (readerType == null)
            {
                return;
            }

            Type[] oneString = { typeof(string) };

            staticOpen = readerType.GetMethod(
                "Open", BindingFlags.Public | BindingFlags.Static, null, oneString, null);

            instanceOpen = readerType.GetMethod(
                "Open", BindingFlags.Public | BindingFlags.Instance, null, oneString, null);

            staticCreate = readerType.GetMethod(
                "Create", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

            if (staticCreate != null && !readerType.IsAssignableFrom(staticCreate.ReturnType))
            {
                staticCreate = null;
            }
        }

        private static Type FindReaderType()
        {
            Assembly own = typeof(ChapterReaderBridge).Assembly;
            for (int i = 0; i < PreferredTypeNames.Length; i++)
            {
                Type t = own.GetType(PreferredTypeNames[i], false);
                if (t != null)
                {
                    return t;
                }
            }

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch (Exception)
            {
                assemblies = new[] { own };
            }

            for (int n = 0; n < PreferredTypeNames.Length; n++)
            {
                for (int a = 0; a < assemblies.Length; a++)
                {
                    Type t = SafeGetType(assemblies[a], PreferredTypeNames[n]);
                    if (t != null)
                    {
                        return t;
                    }
                }
            }

            Type ownFallback = ScanForTypeName(own);
            if (ownFallback != null)
            {
                return ownFallback;
            }

            for (int a = 0; a < assemblies.Length; a++)
            {
                Type t = ScanForTypeName(assemblies[a]);
                if (t != null)
                {
                    return t;
                }
            }

            return null;
        }

        private static Type SafeGetType(Assembly assembly, string fullName)
        {
            if (assembly == null)
            {
                return null;
            }

            try
            {
                return assembly.GetType(fullName, false);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Type ScanForTypeName(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
            {
                return null;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (Exception)
            {
                return null;
            }

            for (int i = 0; i < types.Length; i++)
            {
                if (types[i] != null && types[i].Name == ReaderTypeName)
                {
                    return types[i];
                }
            }

            return null;
        }

        private static object FindTarget()
        {
            if (readerType == null)
            {
                return null;
            }

            for (int i = 0; i < InstanceAccessorNames.Length; i++)
            {
                string name = InstanceAccessorNames[i];

                PropertyInfo property = readerType.GetProperty(
                    name, BindingFlags.Public | BindingFlags.Static);
                if (property != null && property.CanRead && readerType.IsAssignableFrom(property.PropertyType))
                {
                    object value = Alive(property.GetValue(null, null));
                    if (value != null)
                    {
                        return value;
                    }
                }

                FieldInfo field = readerType.GetField(
                    name, BindingFlags.Public | BindingFlags.Static);
                if (field != null && readerType.IsAssignableFrom(field.FieldType))
                {
                    object value = Alive(field.GetValue(null));
                    if (value != null)
                    {
                        return value;
                    }
                }
            }

            if (typeof(Component).IsAssignableFrom(readerType))
            {
                object found = Alive(UnityEngine.Object.FindFirstObjectByType(readerType));
                if (found != null)
                {
                    return found;
                }
            }

            if (staticCreate != null)
            {
                return Alive(staticCreate.Invoke(null, null));
            }

            return null;
        }

        /// <summary>Unity objects compare equal to null once destroyed; plain objects do not.</summary>
        private static object Alive(object candidate)
        {
            UnityEngine.Object unityObject = candidate as UnityEngine.Object;
            if (unityObject != null)
            {
                return unityObject;
            }

            if (candidate is UnityEngine.Object)
            {
                return null;
            }

            return candidate;
        }
    }
}
