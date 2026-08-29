using System;
using System.IO;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Where this run keeps its save and its telemetry.
    ///
    /// Normally that is <see cref="Application.persistentDataPath"/>. It can be redirected with
    /// -data-path on the command line, and the reason is not configurability: an automated run
    /// that writes through the real SaveSystem will overwrite a real playtest. That has already
    /// happened once in this project. A test run points somewhere disposable and cannot reach the
    /// directory a person's run lives in.
    /// </summary>
    public static class AppPaths
    {
        const string CommandLineFlag = "-data-path";

        static string _root;

        /// <summary>Directory holding save.json and telemetry.jsonl. Created if it does not exist.</summary>
        public static string DataRoot
        {
            get
            {
                if (string.IsNullOrEmpty(_root))
                {
                    _root = Resolve();
                }

                return _root;
            }
        }

        /// <summary>True when this run was redirected away from the normal location.</summary>
        public static bool IsRedirected { get; private set; }

        static string Resolve()
        {
            string requested = ReadCommandLinePath();
            if (string.IsNullOrEmpty(requested))
            {
                return Application.persistentDataPath;
            }

            try
            {
                Directory.CreateDirectory(requested);
                IsRedirected = true;
                return requested;
            }
            catch (Exception exception)
            {
                // Falling back to the normal path would silently put a test run's writes on top of
                // a real save, which is the exact outcome the flag exists to prevent. Say so loudly.
                Debug.LogError(
                    "[AppPaths] Could not create the requested data path '" + requested + "': " +
                    exception.Message + ". Falling back to " + Application.persistentDataPath + ".");
                return Application.persistentDataPath;
            }
        }

        static string ReadCommandLinePath()
        {
            string[] args;
            try
            {
                args = Environment.GetCommandLineArgs();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[AppPaths] Could not read the command line: " + exception.Message);
                return null;
            }

            if (args == null)
            {
                return null;
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], CommandLineFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return null;
        }
    }
}
