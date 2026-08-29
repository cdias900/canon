using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Headless player builds, so a run can be verified from the command line instead of by
    /// opening the editor. Invoked with -executeMethod SheepGate.EditorTools.BuildScript.BuildMac.
    /// </summary>
    public static class BuildScript
    {
        static string[] ScenePaths()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
        }

        static void Run(BuildTarget target, BuildTargetGroup group, string outputPath)
        {
            ProjectSetup.Apply();

            string[] scenes = ScenePaths();
            if (scenes.Length == 0)
            {
                throw new Exception("No scenes are enabled in the build settings.");
            }

            Debug.Log("[Build] " + target + " -> " + outputPath + " with " + scenes.Length + " scene(s).");

            string directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = target,
                targetGroup = group,
                options = BuildOptions.Development
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            Debug.Log("[Build] Result " + summary.result + ", " + summary.totalErrors + " error(s).");

            if (summary.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                {
                    foreach (var message in step.messages)
                    {
                        if (message.type == LogType.Error || message.type == LogType.Exception)
                        {
                            Debug.LogError("[Build] " + message.content);
                        }
                    }
                }

                throw new Exception("Build failed: " + summary.result);
            }
        }

        public static void BuildMac()
        {
            Run(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "Builds/mac/SheepGate.app");
        }

        /// <summary>
        /// Produces an Xcode project rather than an installable app: that is what a Unity iOS build
        /// is. Turning it into something that runs on a device needs Xcode and a signing team, which
        /// is a decision for whoever owns the Apple account, not something to bake in here.
        /// </summary>
        public static void BuildIOS()
        {
            Run(BuildTarget.iOS, BuildTargetGroup.iOS, "Builds/ios");
        }

        /// <summary>
        /// The same Xcode export against the simulator SDK, into its own directory so the device
        /// export is left alone. This is the variant that can actually be run from the command
        /// line: a simulator build needs no signing team.
        ///
        /// The architecture has to be set too. It defaults to x86_64, which exports the x86_64
        /// baselib and then fails to link against an arm64 simulator slice on an Apple Silicon
        /// machine — the error names missing baselib symbols and says nothing about architecture.
        /// Both settings are restored afterwards, so what stays on disk keeps describing the
        /// device build.
        /// </summary>
        public static void BuildIOSSimulator()
        {
            iOSSdkVersion previousSdk = PlayerSettings.iOS.sdkVersion;
            AppleMobileArchitectureSimulator previousArchitecture = PlayerSettings.iOS.simulatorSdkArchitecture;

            PlayerSettings.iOS.sdkVersion = iOSSdkVersion.SimulatorSDK;
            PlayerSettings.iOS.simulatorSdkArchitecture = AppleMobileArchitectureSimulator.ARM64;

            try
            {
                Run(BuildTarget.iOS, BuildTargetGroup.iOS, "Builds/ios-sim");
            }
            finally
            {
                PlayerSettings.iOS.sdkVersion = previousSdk;
                PlayerSettings.iOS.simulatorSdkArchitecture = previousArchitecture;
            }
        }

        public static void BuildAndroid()
        {
            Run(BuildTarget.Android, BuildTargetGroup.Android, "Builds/android/SheepGate.apk");
        }
    }
}
