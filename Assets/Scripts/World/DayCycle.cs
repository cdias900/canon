using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using SheepGate.Core;

namespace SheepGate.World
{
    /// <summary>
    /// Day into night into the next morning.
    ///
    /// <see cref="NightAmount"/> runs from 0 (day) to 1 (night) and is applied two ways at once: to
    /// a global URP Light2D when the project has one, and to a full screen tint overlay that always
    /// works. The Light2D path is late-bound so this file compiles and runs with or without URP.
    ///
    /// The night resolves on the split the player chose: with a watch posted the exposed segment
    /// survives, without one it loses the work in progress. Completed stages never regress.
    /// </summary>
    public class DayCycle : MonoBehaviour
    {
        public const int FinalDay = 3;
        public const float DuskSeconds = 1.1f;
        public const float NightHoldSeconds = 0.9f;
        public const float DawnSeconds = 1.1f;
        public const float MaxTintAlpha = 0.72f;

        private const string LightTypeName = "UnityEngine.Rendering.Universal.Light2D";
        private const float LightSearchInterval = 2f;
        private const int DefaultWorkers = 4;
        private const int DefaultWatchers = 2;

        private static readonly string[] PanelOpenMethods = { "Open", "Show", "Compose", "Present" };
        private static readonly string[] ReportOpenMethods = { "Show", "Open", "Compose", "Present" };

        /// <summary>Raised with the new day number once the night has fully resolved.</summary>
        public event Action<int> MorningStarted;

        /// <summary>Raised by <see cref="RequestEndDay"/> so the end-of-day panel can open.</summary>
        public event Action EndDayRequested;

        /// <summary>Raised every time <see cref="NightAmount"/> changes.</summary>
        public event Action<float> NightAmountChanged;

        public float NightAmount { get; private set; }

        /// <summary>True while the night coroutine is running; end-of-day requests are ignored then.</summary>
        public bool IsResolving { get; private set; }

        public bool LastNightHadWatch { get; private set; }

        /// <summary>Segment damaged by the last night, or null when the night passed without loss.</summary>
        public string LastNightDamagedSegment { get; private set; }

        public int LastWorkers { get; private set; }
        public int LastWatchers { get; private set; }

        private Image _tint;
        private Type _lightType;
        private UnityEngine.Object _light;
        private PropertyInfo _lightIntensity;
        private float _nextLightSearch;
        private float _appliedNightAmount = -1f;

        private void Awake()
        {
            CreateTintOverlay();
            _lightType = TypeBridge.Find(LightTypeName);
            if (_lightType != null)
            {
                _lightIntensity = TypeBridge.FindProperty(_lightType, "intensity");
            }

            ApplyNightAmount(0f, true);
        }

        private void CreateTintOverlay()
        {
            GameObject canvasObject = new GameObject("NightOverlay");
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Below every other overlay canvas: the world darkens, the HUD stays readable.
            canvas.sortingOrder = -100;

            GameObject tintObject = new GameObject("Tint");
            tintObject.transform.SetParent(canvasObject.transform, false);
            _tint = tintObject.AddComponent<Image>();
            _tint.raycastTarget = false;
            _tint.color = new Color(0.05f, 0.07f, 0.16f, 0f);

            RectTransform rect = _tint.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Asks for the end-of-day split. The UI layer either subscribes to
        /// <see cref="EndDayRequested"/> or exposes an opener on SheepGate.UI.EndDayPanel.
        /// </summary>
        public void RequestEndDay()
        {
            if (IsResolving)
            {
                return;
            }

            Action handler = EndDayRequested;
            if (handler != null)
            {
                try
                {
                    handler();
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] An EndDayRequested listener threw: " + exception.Message);
                    return;
                }
            }

            Type panelType = TypeBridge.Find("SheepGate.UI.EndDayPanel");
            if (panelType != null)
            {
                if (TypeBridge.InvokeStatic(panelType, PanelOpenMethods, new object[] { this }))
                {
                    return;
                }

                if (TypeBridge.InvokeStatic(panelType, PanelOpenMethods, new object[0]))
                {
                    return;
                }

                if (typeof(Component).IsAssignableFrom(panelType))
                {
                    UnityEngine.Object instance = FindFirstObjectByType(panelType);
                    if (instance != null && (TypeBridge.Invoke(instance, PanelOpenMethods, new object[] { this })
                                             || TypeBridge.Invoke(instance, PanelOpenMethods, new object[0])))
                    {
                        return;
                    }
                }
            }

            Debug.LogWarning("[World] Nothing answered RequestEndDay; resolving the night with a default split of "
                             + DefaultWorkers + " on the work and " + DefaultWatchers + " on the watch.");
            EndDay(DefaultWorkers, DefaultWatchers);
        }

        /// <summary>Resolves the night on the chosen split and advances to the next morning.</summary>
        public void EndDay(int workers, int watchers)
        {
            if (IsResolving)
            {
                Debug.LogWarning("[World] EndDay ignored: the night is already resolving.");
                return;
            }

            if (!isActiveAndEnabled)
            {
                Debug.LogWarning("[World] EndDay called on an inactive DayCycle; resolving without the fade.");
                ResolveImmediately(workers, watchers);
                return;
            }

            StartCoroutine(ResolveNight(workers, watchers));
        }

        private void ResolveImmediately(int workers, int watchers)
        {
            IsResolving = true;
            ApplyNightAmount(1f, false);
            ResolveNightOutcome(workers, watchers);
            AdvanceToMorning();
            ApplyNightAmount(0f, false);
            IsResolving = false;
            RaiseMorning();
        }

        private IEnumerator ResolveNight(int workers, int watchers)
        {
            IsResolving = true;

            yield return Fade(0f, 1f, DuskSeconds);

            ResolveNightOutcome(workers, watchers);

            yield return new WaitForSeconds(NightHoldSeconds);

            AdvanceToMorning();

            yield return Fade(1f, 0f, DawnSeconds);

            IsResolving = false;
            RaiseMorning();
        }

        private void ResolveNightOutcome(int workers, int watchers)
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            LastWorkers = Mathf.Max(0, workers);
            LastWatchers = Mathf.Max(0, watchers);
            state.workAssigned = LastWorkers;
            state.watchAssigned = LastWatchers;

            int day = state.day;
            LastNightHadWatch = LastWatchers > 0;
            LastNightDamagedSegment = null;

            if (LastNightHadWatch)
            {
                if (day <= 1)
                {
                    state.SetFlag(WorldRuntime.FlagWatchPostedD1);
                }
                else if (day == 2)
                {
                    state.SetFlag(WorldRuntime.FlagWatchPostedD2);
                    // Believing the resident who saw the riders on the road.
                    WorldRuntime.AwardOnce("prophet_watch_d2_awarded", WorldRuntime.VocationProphet, 3);
                }
            }
            else
            {
                WallSystem wall = FindWallSystem();
                if (wall != null)
                {
                    string exposed = wall.PrimaryExposedSegmentId;
                    if (!string.IsNullOrEmpty(exposed))
                    {
                        wall.DamageSegment(exposed);
                        LastNightDamagedSegment = exposed;
                    }
                }
                else
                {
                    Debug.LogWarning("[World] No WallSystem found; the unwatched night resolved without damage.");
                }
            }

            ScoreSteward(state, day);
        }

        /// <summary>
        /// The steward scores for finishing both day one and day two with the work capacity fully
        /// spent, and for finishing both of them with no rubble left lying around. Each is a single
        /// action worth three points, checked once, when the second night resolves.
        /// </summary>
        private static void ScoreSteward(GameState state, int day)
        {
            if (day > 2)
            {
                return;
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null && resources.Capacity <= 0)
            {
                state.counters["steward_capacity_qualified_d" + day] = 1;
            }

            if (state.rubble <= 0)
            {
                state.counters["steward_rubble_qualified_d" + day] = 1;
            }

            if (day < 2)
            {
                return;
            }

            if (state.Counter("steward_capacity_qualified_d1") > 0 && state.Counter("steward_capacity_qualified_d2") > 0)
            {
                WorldRuntime.AwardOnce("steward_capacity_awarded", WorldRuntime.VocationSteward, 3);
            }

            if (state.Counter("steward_rubble_qualified_d1") > 0 && state.Counter("steward_rubble_qualified_d2") > 0)
            {
                WorldRuntime.AwardOnce("steward_rubble_awarded", WorldRuntime.VocationSteward, 3);
            }
        }

        private void AdvanceToMorning()
        {
            GameState state = WorldRuntime.State;
            if (state == null)
            {
                return;
            }

            if (state.day < FinalDay)
            {
                state.day = state.day + 1;
            }
            else
            {
                Debug.LogWarning("[World] EndDay resolved on the final day; the day counter stays at " + FinalDay + ".");
            }

            ResourceSystem resources = ResourceSystem.Find();
            if (resources != null)
            {
                resources.ResetDailyCapacity();
            }

            WorldRuntime.SaveNow();
        }

        private void RaiseMorning()
        {
            GameState state = WorldRuntime.State;
            int day = state != null ? state.day : 1;

            Action<int> handler = MorningStarted;
            bool userInterfaceListening = false;

            if (handler != null)
            {
                userInterfaceListening = HasUserInterfaceListener(handler);
                try
                {
                    handler(day);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] A MorningStarted listener threw: " + exception.Message);
                }
            }

            if (userInterfaceListening)
            {
                return;
            }

            // World objects such as the rubble piles always listen, so the presence of subscribers
            // proves nothing about the UI. Only reach for the panel when no UI type is listening.
            Type reportType = TypeBridge.Find("SheepGate.UI.MorningReportUI");
            if (reportType != null)
            {
                if (TypeBridge.InvokeStatic(reportType, ReportOpenMethods, new object[] { day })
                    || TypeBridge.InvokeStatic(reportType, ReportOpenMethods, new object[0]))
                {
                    return;
                }
            }

            Debug.LogWarning("[World] No UI listened to MorningStarted; day " + day + " begins without a morning report.");
        }

        private static bool HasUserInterfaceListener(Delegate handler)
        {
            Delegate[] listeners = handler.GetInvocationList();
            for (int i = 0; i < listeners.Length; i++)
            {
                object target = listeners[i].Target;
                Type type = target != null ? target.GetType() : listeners[i].Method.DeclaringType;
                if (type == null || string.IsNullOrEmpty(type.Namespace))
                {
                    continue;
                }

                if (type.Namespace == "SheepGate.UI" || type.Namespace.StartsWith("SheepGate.UI."))
                {
                    return true;
                }
            }

            return false;
        }

        private WallSystem FindWallSystem()
        {
            WallSystem wall = null;
            try
            {
                ServiceLocator.TryGet(out wall);
            }
            catch (Exception)
            {
                wall = null;
            }

            if (wall == null)
            {
                wall = FindFirstObjectByType<WallSystem>();
            }

            return wall;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            if (seconds <= 0f)
            {
                ApplyNightAmount(to, false);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);
                ApplyNightAmount(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t)), false);
                yield return null;
            }

            ApplyNightAmount(to, false);
        }

        /// <summary>Sets the night level directly, for scripted moments such as the day three assault.</summary>
        public void SetNightAmount(float amount)
        {
            ApplyNightAmount(Mathf.Clamp01(amount), false);
        }

        private void ApplyNightAmount(float amount, bool force)
        {
            NightAmount = Mathf.Clamp01(amount);
            if (!force && Mathf.Approximately(_appliedNightAmount, NightAmount))
            {
                return;
            }

            _appliedNightAmount = NightAmount;

            if (_tint != null)
            {
                Color color = _tint.color;
                color.a = NightAmount * MaxTintAlpha;
                _tint.color = color;
            }

            ApplyToLight();

            Action<float> handler = NightAmountChanged;
            if (handler != null)
            {
                try
                {
                    handler(NightAmount);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[World] A NightAmountChanged listener threw: " + exception.Message);
                }
            }
        }

        private void ApplyToLight()
        {
            if (_lightType == null || _lightIntensity == null)
            {
                return;
            }

            if (_light == null && Time.unscaledTime >= _nextLightSearch)
            {
                _nextLightSearch = Time.unscaledTime + LightSearchInterval;
                try
                {
                    _light = FindFirstObjectByType(_lightType);
                }
                catch (Exception)
                {
                    _light = null;
                }
            }

            if (_light == null)
            {
                return;
            }

            try
            {
                float intensity = Mathf.Lerp(1f, 0.25f, NightAmount);
                _lightIntensity.SetValue(_light, intensity, null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[World] Driving the 2D light failed; falling back to the tint overlay only: " + exception.Message);
                _lightIntensity = null;
            }
        }
    }
}
