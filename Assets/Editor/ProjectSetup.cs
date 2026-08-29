using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SheepGate.EditorTools
{
    /// <summary>
    /// Idempotent project configuration applied on every domain reload.
    ///
    /// The project is authored without inspector wiring: scenes are near-empty and every
    /// GameObject is built at runtime. The few settings that genuinely live in
    /// ProjectSettings (build scene list, portrait orientation, render pipeline) are set
    /// here through Unity's own APIs so no agent ever hand-writes a .asset file.
    ///
    /// Everything is written only when it differs from the current value, so opening the
    /// editor does not dirty the project.
    /// </summary>
    public static class ProjectSetup
    {
        private const string BootScenePath = "Assets/Scenes/Boot.unity";
        private const string CharacterCreationScenePath = "Assets/Scenes/CharacterCreation.unity";
        private const string GameScenePath = "Assets/Scenes/Game.unity";

        private const string SettingsFolder = "Assets/Settings";
        private const string RendererDataPath = SettingsFolder + "/SheepGateRenderer2D.asset";
        private const string PipelineAssetPath = SettingsFolder + "/SheepGateRenderPipeline.asset";
        private const string UniversalPackagePath = "Packages/com.unity.render-pipelines.universal";

        private const string ProductName = "Porta das Ovelhas";
        private const string CompanyName = "Create Hack";
        private const int DefaultScreenWidth = 1080;
        private const int DefaultScreenHeight = 1920;

        private const string SaveFileName = "save.json";
        private const string TelemetryFileName = "telemetry.jsonl";

        [InitializeOnLoadMethod]
        private static void Schedule()
        {
            // Deferred so the AssetDatabase is fully available. Apply is also public so a
            // batch-mode run can invoke it directly, where delayCall may never fire.
            EditorApplication.delayCall += Apply;
        }

        /// <summary>
        /// Applies every project setting this POC depends on. Safe to call repeatedly.
        /// </summary>
        public static void Apply()
        {
            ApplyBuildScenes();
            ApplyPlayerSettings();
            ApplyRenderPipeline();
            ApplyBundleIdentifiers();
        }

        // --- Build scenes ---------------------------------------------------------------

        private static void ApplyBuildScenes()
        {
            // Order matters: Boot must be index 0 because it is the scene the player starts in.
            string[] wanted = { BootScenePath, CharacterCreationScenePath, GameScenePath };

            var present = new List<string>(wanted.Length);
            for (int i = 0; i < wanted.Length; i++)
            {
                if (File.Exists(wanted[i]))
                {
                    present.Add(wanted[i]);
                }
            }

            if (present.Count == 0)
            {
                return;
            }

            EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
            if (Matches(current, present))
            {
                return;
            }

            var updated = new EditorBuildSettingsScene[present.Count];
            for (int i = 0; i < present.Count; i++)
            {
                updated[i] = new EditorBuildSettingsScene(present[i], true);
            }

            EditorBuildSettings.scenes = updated;
        }

        private static bool Matches(EditorBuildSettingsScene[] current, List<string> wanted)
        {
            if (current == null || current.Length != wanted.Count)
            {
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (!current[i].enabled || current[i].path != wanted[i])
                {
                    return false;
                }
            }

            return true;
        }

        // --- Player settings ------------------------------------------------------------

        private static void ApplyPlayerSettings()
        {
            if (PlayerSettings.productName != ProductName)
            {
                PlayerSettings.productName = ProductName;
            }

            if (PlayerSettings.companyName != CompanyName)
            {
                PlayerSettings.companyName = CompanyName;
            }

            if (PlayerSettings.defaultInterfaceOrientation != UIOrientation.Portrait)
            {
                PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            }

            if (!PlayerSettings.allowedAutorotateToPortrait)
            {
                PlayerSettings.allowedAutorotateToPortrait = true;
            }

            if (PlayerSettings.allowedAutorotateToPortraitUpsideDown)
            {
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            }

            if (PlayerSettings.allowedAutorotateToLandscapeLeft)
            {
                PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            }

            if (PlayerSettings.allowedAutorotateToLandscapeRight)
            {
                PlayerSettings.allowedAutorotateToLandscapeRight = false;
            }

            if (PlayerSettings.defaultIsNativeResolution)
            {
                PlayerSettings.defaultIsNativeResolution = false;
            }

            if (PlayerSettings.defaultScreenWidth != DefaultScreenWidth)
            {
                PlayerSettings.defaultScreenWidth = DefaultScreenWidth;
            }

            if (PlayerSettings.defaultScreenHeight != DefaultScreenHeight)
            {
                PlayerSettings.defaultScreenHeight = DefaultScreenHeight;
            }
        }

        // --- Render pipeline ------------------------------------------------------------

        /// <summary>
        /// Bundle identifiers. Unity leaves these empty, and a mobile build fails outright without
        /// one, so they are set here rather than left to whoever first opens Build Settings.
        /// Android is not a current build target but keeps its id: the value costs nothing and
        /// removing it would only mean rediscovering this the next time someone tries.
        /// </summary>
        private static void ApplyBundleIdentifiers()
        {
            const string packageName = "com.createhack.portadasovelhas";

            TrySetIdentifier(NamedBuildTarget.iOS, packageName);
            TrySetIdentifier(NamedBuildTarget.Android, packageName);

            try
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Setup] Could not set the Android minimum SDK: " + exception.Message);
            }
        }

        private static void TrySetIdentifier(NamedBuildTarget target, string packageName)
        {
            try
            {
                if (PlayerSettings.GetApplicationIdentifier(target) != packageName)
                {
                    PlayerSettings.SetApplicationIdentifier(target, packageName);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Setup] Could not set the " + target + " bundle id: " + exception.Message);
            }
        }

        private static void ApplyRenderPipeline()
        {
            // Guarded end to end: a broken pipeline setup must never stop the project from
            // opening, because every other agent depends on the editor being usable.
            try
            {
                UniversalRenderPipelineAsset pipeline =
                    GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

                if (pipeline == null)
                {
                    pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
                }

                if (pipeline == null)
                {
                    pipeline = CreatePipelineAsset();
                }

                if (pipeline == null)
                {
                    return;
                }

                if (!ReferenceEquals(GraphicsSettings.defaultRenderPipeline, pipeline))
                {
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                }

                AssignToAllQualityLevels(pipeline);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SheepGate] Render pipeline setup skipped: " + exception.Message);
            }
        }

        private static UniversalRenderPipelineAsset CreatePipelineAsset()
        {
            EnsureFolder(SettingsFolder);

            Renderer2DData rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererDataPath);
            if (rendererData == null)
            {
                // A 2D renderer is required: the day/night cycle uses Light2D.
                rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(rendererData, RendererDataPath);
                ResourceReloader.ReloadAllNullIn(rendererData, UniversalPackagePath);
                EditorUtility.SetDirty(rendererData);
            }

            UniversalRenderPipelineAsset pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            AssetDatabase.SaveAssets();

            return pipeline;
        }

        private static void AssignToAllQualityLevels(RenderPipelineAsset pipeline)
        {
            int originalLevel = QualitySettings.GetQualityLevel();
            try
            {
                int levelCount = QualitySettings.names.Length;
                for (int i = 0; i < levelCount; i++)
                {
                    QualitySettings.SetQualityLevel(i, false);
                    if (!ReferenceEquals(QualitySettings.renderPipeline, pipeline))
                    {
                        QualitySettings.renderPipeline = pipeline;
                    }
                }
            }
            finally
            {
                QualitySettings.SetQualityLevel(originalLevel, false);
            }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent))
            {
                return;
            }

            parent = parent.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // --- Menu helpers ---------------------------------------------------------------

        [MenuItem("SheepGate/Reset Save")]
        private static void ResetSave()
        {
            // Deliberately uses raw file paths instead of SheepGate.Core.SaveSystem: this
            // editor tool must keep working even while the runtime assembly is mid-rewrite.
            string root = Application.persistentDataPath;
            string[] fileNames = { SaveFileName, TelemetryFileName };

            var deleted = new List<string>(fileNames.Length);
            foreach (string fileName in fileNames)
            {
                string path = Path.Combine(root, fileName);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted.Add(fileName);
                }
            }

            if (deleted.Count == 0)
            {
                Debug.Log("[SheepGate] Nothing to reset in " + root);
            }
            else
            {
                Debug.Log("[SheepGate] Removed " + string.Join(", ", deleted.ToArray()) + " from " + root);
            }
        }
    }
}
