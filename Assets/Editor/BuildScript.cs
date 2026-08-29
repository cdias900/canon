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

        public static void BuildAndroid()
        {
            Run(BuildTarget.Android, BuildTargetGroup.Android, "Builds/android/SheepGate.apk");
        }
    }
}
