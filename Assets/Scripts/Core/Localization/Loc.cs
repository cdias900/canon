using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// The player-facing string table for the locale in force.
    ///
    /// No C# source file may hold a sentence a player can read. Every one of them lives in
    /// Resources/Data/locales/&lt;locale&gt;/ui.json and is reached through <see cref="T(string)"/>.
    /// That is what makes adding a language a content change rather than a code change, and it is
    /// checkable: tools/validate-content.mjs fails the build on a literal at a UI call site.
    ///
    /// A missing key renders a visible marker and logs an error. It never falls back to another
    /// language: a silent fallback that looks like real content is exactly the failure that hides
    /// a missing translation until a player finds it.
    /// </summary>
    public static class Loc
    {
        const string TableFileName = "/ui";

        static readonly Dictionary<string, string> Table = new Dictionary<string, string>(StringComparer.Ordinal);

        static string _loadedLocale;

        /// <summary>Suffix appended to a plural key when the count is exactly one.</summary>
        public const string PluralOne = ".one";

        /// <summary>Suffix appended to a plural key for every other count, zero included.</summary>
        public const string PluralOther = ".other";

        /// <summary>The locale whose table is in memory. Empty before the first load.</summary>
        public static string LoadedLocale
        {
            get { return _loadedLocale ?? string.Empty; }
        }

        /// <summary>Number of strings in memory. Used by the acceptance harness.</summary>
        public static int Count
        {
            get
            {
                EnsureLoaded();
                return Table.Count;
            }
        }

        /// <summary>
        /// Reads the table for a locale. Safe to call repeatedly; a second call for the same locale
        /// is a no-op unless <paramref name="force"/> is set.
        /// </summary>
        public static void Load(string locale, bool force = false)
        {
            string canonical = Locales.Canonical(locale) ?? Locales.Source;
            if (!force && _loadedLocale == canonical)
            {
                return;
            }

            _loadedLocale = canonical;
            Table.Clear();

            string path = Locales.ResourceFolder(canonical) + TableFileName;
            TextAsset asset = Resources.Load<TextAsset>(path);
            if (asset == null)
            {
                Debug.LogError("[Loc] Missing string table Resources/" + path + ".json. Every string will render as its key.");
                return;
            }

            Dictionary<string, string> parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<Dictionary<string, string>>(asset.text);
            }
            catch (Exception exception)
            {
                Debug.LogError("[Loc] Could not parse Resources/" + path + ".json: " + exception.Message);
                return;
            }

            if (parsed == null)
            {
                Debug.LogError("[Loc] Resources/" + path + ".json did not contain an object.");
                return;
            }

            foreach (KeyValuePair<string, string> pair in parsed)
            {
                if (string.IsNullOrEmpty(pair.Key) || pair.Value == null)
                {
                    continue;
                }

                Table[pair.Key] = pair.Value;
            }
        }

        /// <summary>Rereads the table for whichever locale is active.</summary>
        public static void Reload()
        {
            Load(Locales.Active, true);
        }

        /// <summary>True when this key exists in the loaded table.</summary>
        public static bool Has(string key)
        {
            EnsureLoaded();
            return !string.IsNullOrEmpty(key) && Table.ContainsKey(key);
        }

        /// <summary>
        /// The string for this key. On a miss, a visible marker so the gap shows up on screen
        /// instead of as an empty label, plus an error in the log.
        /// </summary>
        public static string T(string key)
        {
            EnsureLoaded();

            if (string.IsNullOrEmpty(key))
            {
                return Missing(string.Empty);
            }

            string value;
            if (Table.TryGetValue(key, out value) && value != null)
            {
                return value;
            }

            Debug.LogError("[Loc] Missing key '" + key + "' in locale " + LoadedLocale + ".");
            return Missing(key);
        }

        /// <summary>
        /// The string for this key with positional arguments substituted, as in "Dia {0}".
        /// A malformed format string degrades to the raw template rather than throwing mid-frame.
        /// </summary>
        public static string T(string key, params object[] args)
        {
            string template = T(key);
            if (args == null || args.Length == 0)
            {
                return template;
            }

            try
            {
                return string.Format(template, args);
            }
            catch (FormatException exception)
            {
                Debug.LogError("[Loc] Key '" + key + "' has a bad format string in locale " + LoadedLocale +
                               ": " + exception.Message);
                return template;
            }
        }

        /// <summary>
        /// Picks between "&lt;key&gt;.one" and "&lt;key&gt;.other" on the count, then formats. The count
        /// is passed as {0} and any further arguments follow it.
        ///
        /// One and other is enough for the languages that ship. A language with a richer plural
        /// system needs this method to grow a rule per locale, not a new call site.
        /// </summary>
        public static string Plural(string key, int count, params object[] args)
        {
            string suffixed = (key ?? string.Empty) + (count == 1 ? PluralOne : PluralOther);

            object[] formatArgs;
            if (args == null || args.Length == 0)
            {
                formatArgs = new object[] { count };
            }
            else
            {
                formatArgs = new object[args.Length + 1];
                formatArgs[0] = count;
                Array.Copy(args, 0, formatArgs, 1, args.Length);
            }

            return T(suffixed, formatArgs);
        }

        static void EnsureLoaded()
        {
            if (string.IsNullOrEmpty(_loadedLocale))
            {
                Load(Locales.Active);
            }
        }

        // Angle quotes rather than plain brackets so a marker on screen cannot be mistaken for
        // authored punctuation, and so it survives a grep for the key that produced it.
        static string Missing(string key)
        {
            return "⟨" + key + "⟩";
        }
    }
}
