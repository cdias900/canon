using System.Runtime.InteropServices;
using SheepGate.UI;
using UnityEngine;

namespace SheepGate.Core
{
    /// <summary>
    /// Reduced motion, remembered, and seeded from the operating system.
    ///
    /// <b>Why this exists.</b> <see cref="DesignTokens.Motion.ReduceMotion"/> was declared and
    /// honoured — parallax, pulse and shake all check it, and fades and bars deliberately keep
    /// running under it — but nothing in the project ever set it. An accessibility switch that is
    /// written and never thrown is the same as not having one.
    ///
    /// <b>Two sources, in this order.</b> The operating system's own setting decides the default,
    /// so a player who already told their phone to reduce motion is not asked twice. Once they
    /// touch the switch in settings, their choice wins for good, on the same PlayerPrefs shelf the
    /// two sound switches use and for the same reason: it has to survive a reinstall of the run,
    /// not of the app.
    ///
    /// The distinction between "never chosen" and "chosen false" is the whole point of
    /// <see cref="HasChoice"/>, and it is why this cannot be one bool with a default.
    /// </summary>
    public static class AccessibilityPreferences
    {
        /// <summary>Where the player's own answer lives, beside the audio keys.</summary>
        public const string ReduceMotionPrefKey = "sheepgate.a11y.reduce_motion";

#if UNITY_IOS && !UNITY_EDITOR
        /// <summary>
        /// UIAccessibilityIsReduceMotionEnabled, through the shim in Plugins/iOS. Declared here
        /// rather than in a wrapper class so the one platform branch in this file is the whole
        /// platform story.
        /// </summary>
        [DllImport("__Internal")]
        static extern bool SheepGateIsReduceMotionEnabled();
#endif

        /// <summary>True once the player has answered for themselves.</summary>
        public static bool HasChoice
        {
            get { return PlayerPrefs.HasKey(ReduceMotionPrefKey); }
        }

        /// <summary>
        /// What the operating system says. False everywhere the question cannot be asked, which is
        /// the honest answer: the desktop player has no such setting to read.
        /// </summary>
        public static bool SystemPrefersReducedMotion
        {
            get
            {
#if UNITY_IOS && !UNITY_EDITOR
                return SheepGateIsReduceMotionEnabled();
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// The setting in force. Reading it falls back to the system; writing it records a choice
        /// that no longer falls back to anything.
        /// </summary>
        public static bool ReduceMotion
        {
            get
            {
                if (!HasChoice)
                {
                    return SystemPrefersReducedMotion;
                }

                return PlayerPrefs.GetInt(ReduceMotionPrefKey, 0) != 0;
            }
            set
            {
                PlayerPrefs.SetInt(ReduceMotionPrefKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Apply();
            }
        }

        /// <summary>
        /// Pushes the setting into the design tokens, which is where every animation reads it.
        ///
        /// Called at boot and again on every write. It is a copy rather than a redirect because
        /// <c>DesignTokens</c> is the UI layer's own vocabulary and must not learn about
        /// PlayerPrefs to answer a question asked once per animation.
        /// </summary>
        public static void Apply()
        {
            DesignTokens.Motion.ReduceMotion = ReduceMotion;
        }
    }
}
