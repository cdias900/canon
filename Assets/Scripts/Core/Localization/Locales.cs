using System;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Which languages exist, which one is running, and how that choice is remembered.
    ///
    /// This type is deliberately inert: it knows the identity of a locale and nothing about the
    /// content keyed by it. Reloading the string table, the content files and the scripture index
    /// is <see cref="BootSequence.ApplyLocale"/>'s job, so that a locale can be resolved during
    /// boot before any of those systems exist.
    ///
    /// The choice is stored in PlayerPrefs rather than in the save. A player who deletes their run
    /// is not asking to be spoken to in another language, and the language has to be resolvable
    /// before a GameState exists at all.
    /// </summary>
    public static class Locales
    {
        /// <summary>
        /// The locale content is authored in. Every other locale is a translation of this one, and
        /// the content validator treats this one as the structural authority.
        /// </summary>
        public const string Source = "pt-BR";

        public const string English = "en";

        /// <summary>Every locale that ships. Adding one here is the first step of adding a language.</summary>
        public static readonly string[] Supported = { Source, English };

        const string PrefKey = "sheepgate.locale";
        const string CommandLineFlag = "-locale";

        static string _active;

        /// <summary>
        /// Stops the chosen language being written to PlayerPrefs.
        ///
        /// PlayerPrefs is not covered by -data-path, so an automated run that taps the toggle
        /// would change the language the person at this machine gets on their next launch. The
        /// e2e runner sets this before anything can switch.
        /// </summary>
        public static bool SuppressPersistence { get; set; }

        /// <summary>
        /// The locale in force. Resolved on first access from the command line, then the stored
        /// preference, then the system language. Never null and never unsupported.
        /// </summary>
        public static string Active
        {
            get
            {
                if (string.IsNullOrEmpty(_active))
                {
                    _active = Resolve();
                }

                return _active;
            }
        }

        /// <summary>True when the id is one this build ships. Comparison is case insensitive.</summary>
        public static bool IsSupported(string locale)
        {
            return Canonical(locale) != null;
        }

        /// <summary>
        /// Returns the supported id matching this string, or null. Accepts a bare language tag, so
        /// "pt", "PT-br" and "pt-BR" all resolve to "pt-BR".
        /// </summary>
        public static string Canonical(string locale)
        {
            if (string.IsNullOrEmpty(locale))
            {
                return null;
            }

            string trimmed = locale.Trim();

            for (int i = 0; i < Supported.Length; i++)
            {
                if (string.Equals(Supported[i], trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return Supported[i];
                }
            }

            // Bare language tag: "pt" matches "pt-BR", "en-GB" matches "en".
            string language = trimmed.Split('-')[0];
            for (int i = 0; i < Supported.Length; i++)
            {
                string supportedLanguage = Supported[i].Split('-')[0];
                if (string.Equals(supportedLanguage, language, StringComparison.OrdinalIgnoreCase))
                {
                    return Supported[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Command line, then stored preference, then system language, then the source locale.
        /// The command line wins so an end-to-end run can pin a language without touching the
        /// preferences of whoever is sitting at the machine.
        /// </summary>
        public static string Resolve()
        {
            string fromCommandLine = Canonical(ReadCommandLineLocale());
            if (fromCommandLine != null)
            {
                return fromCommandLine;
            }

            string fromPrefs = Canonical(PlayerPrefs.GetString(PrefKey, string.Empty));
            if (fromPrefs != null)
            {
                return fromPrefs;
            }

            return Application.systemLanguage == SystemLanguage.Portuguese ? Source : English;
        }

        /// <summary>
        /// Records the locale in force. Persisting is optional so a headless run can pin a language
        /// without writing to the preferences of the machine it happens to be on.
        /// </summary>
        public static void SetActive(string locale, bool persist = true)
        {
            string canonical = Canonical(locale);
            if (canonical == null)
            {
                Debug.LogError("[Locales] Unsupported locale '" + locale + "'; staying on " + Active + ".");
                return;
            }

            _active = canonical;

            if (persist && !SuppressPersistence)
            {
                PlayerPrefs.SetString(PrefKey, canonical);
                PlayerPrefs.Save();
            }
        }

        /// <summary>The locale after this one, for a toggle that cycles through what is installed.</summary>
        public static string Next(string locale)
        {
            string canonical = Canonical(locale) ?? Source;
            for (int i = 0; i < Supported.Length; i++)
            {
                if (Supported[i] == canonical)
                {
                    return Supported[(i + 1) % Supported.Length];
                }
            }

            return Source;
        }

        /// <summary>Two-letter label for the toggle: pt-BR renders as PT, en as EN.</summary>
        public static string ShortLabel(string locale)
        {
            string canonical = Canonical(locale) ?? Source;
            return canonical.Split('-')[0].ToUpperInvariant();
        }

        /// <summary>
        /// Resources folder holding this locale's player-facing content, without a trailing slash.
        /// Every string a player can read is loaded from under here.
        /// </summary>
        public static string ResourceFolder(string locale)
        {
            return "Data/locales/" + (Canonical(locale) ?? Source);
        }

        static string ReadCommandLineLocale()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception exception)
            {
                // Some platforms refuse the process arguments. That is not a reason to fail boot.
                Debug.LogWarning("[Locales] Could not read the command line: " + exception.Message);
                return null;
            }

            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.IsNullOrEmpty(arg))
                {
                    continue;
                }

                // -locale en
                if (string.Equals(arg, CommandLineFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                // -locale=en and --locale=en
                int equals = arg.IndexOf('=');
                if (equals > 0)
                {
                    string name = arg.Substring(0, equals).TrimStart('-');
                    if (string.Equals(name, "locale", StringComparison.OrdinalIgnoreCase))
                    {
                        return arg.Substring(equals + 1);
                    }
                }
            }

            return null;
        }
    }
}
