using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Puts the accent back on the name a player reads.
    ///
    /// PlayerSettings productName has to survive Xcode, which derives the bundle name through
    /// $(TARGET_NAME:c99extidentifier) and drops anything that cannot appear in a C identifier.
    /// The accented name came out of it as "Cnon.app", silently, on a build that reported success.
    /// productName is therefore ASCII, and the real name is written back here.
    ///
    /// The name itself is read from the authoring locale rather than spelled in this file, because
    /// a name on a home screen is a string a player reads and those live in locales. It is the same
    /// string the title card shows.
    ///
    /// The two platforms read different keys, so each gets the one it shows. iOS labels the icon
    /// from CFBundleDisplayName. macOS does not write that key at all and shows CFBundleName.
    /// CFBundleExecutable is left alone on both: it names a file on disk, and tools/e2e.sh and
    /// tools/ios-sim.sh resolve the binary by looking for it rather than by spelling it.
    /// </summary>
    public static class BundleDisplayName
    {
        private const string AuthoringLocalePath = "Resources/Data/locales/pt-BR/ui.json";
        private const string WordmarkKey = "title.wordmark";

        [PostProcessBuild]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            string key = DisplayNameKey(target);
            string plistPath = PlistPath(target, pathToBuiltProject);
            if (key == null || plistPath == null)
            {
                return;
            }

            if (!File.Exists(plistPath))
            {
                Debug.LogWarning("[Build] No Info.plist at " + plistPath + "; " + key + " keeps the ASCII product name.");
                return;
            }

            string displayName = Wordmark();
            if (string.IsNullOrEmpty(displayName))
            {
                Debug.LogWarning("[Build] " + WordmarkKey + " is missing from " + AuthoringLocalePath +
                    "; " + key + " keeps the ASCII product name.");
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            plist.root.SetString(key, displayName);
            plist.WriteToFile(plistPath);

            Debug.Log("[Build] " + key + " -> " + displayName);
        }

        private static string Wordmark()
        {
            string path = Path.Combine(Application.dataPath, AuthoringLocalePath);
            if (!File.Exists(path))
            {
                return null;
            }

            var strings = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
            if (strings == null || !strings.ContainsKey(WordmarkKey))
            {
                return null;
            }

            return strings[WordmarkKey];
        }

        private static string PlistPath(BuildTarget target, string pathToBuiltProject)
        {
            if (target == BuildTarget.iOS)
            {
                return Path.Combine(pathToBuiltProject, "Info.plist");
            }

            if (target == BuildTarget.StandaloneOSX)
            {
                return Path.Combine(pathToBuiltProject, "Contents", "Info.plist");
            }

            return null;
        }

        private static string DisplayNameKey(BuildTarget target)
        {
            if (target == BuildTarget.iOS)
            {
                return "CFBundleDisplayName";
            }

            if (target == BuildTarget.StandaloneOSX)
            {
                return "CFBundleName";
            }

            return null;
        }
    }
}
