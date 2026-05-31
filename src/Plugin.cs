using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FidelityReviveFix
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "AngelcoMilk.FidelityReviveFix";
        public const string PluginName = "FidelityReviveFix";
        public const string PluginVersion = "0.1.4";

        internal static Plugin Instance;
        internal static ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ModConfig.Bind(Config);
            InstantReviveController.Reset();
            ClientReviveProtection.Reset();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            Logger.LogInfo(PluginName + " " + PluginVersion + " loaded.");
        }

        private void OnDestroy()
        {
            InstantReviveController.Reset();
            ClientReviveProtection.Reset();

            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }

        internal Coroutine StartReviveRoutine(IEnumerator routine)
        {
            return StartCoroutine(routine);
        }

        internal Coroutine StartProtectionRoutine(PlayerAvatar player)
        {
            return StartCoroutine(ClientReviveProtection.Run(player));
        }

        internal Coroutine StartProtectionRoutine(PlayerAvatar player, bool hasExpectedPosition, Vector3 expectedPosition, string reason)
        {
            return StartCoroutine(ClientReviveProtection.Run(player, hasExpectedPosition, expectedPosition, reason));
        }
    }

    internal enum ClientProtectionMode
    {
        Auto,
        Always,
        Off
    }

    internal enum ReviveTimingPolicy
    {
        Auto,
        Instant,
        StableDelayed
    }

    internal static class ModConfig
    {
        internal static ConfigEntry<bool> EnableInstantExtractionRevive;
        internal static ConfigEntry<ReviveTimingPolicy> ReviveTimingPolicyEntry;
        internal static ConfigEntry<float> StableDelayedReviveDelay;
        internal static ConfigEntry<bool> EnableFallbackExtractionDetection;
        internal static ConfigEntry<ClientProtectionMode> REPOFidelityClientProtection;
        internal static ConfigEntry<float> PostReviveProtectionWindow;
        internal static ConfigEntry<bool> DebugLogging;

        internal static void Bind(ConfigFile config)
        {
            EnableInstantExtractionRevive = config.Bind(
                "Instant Revive",
                "Enable Instant Extraction Revive",
                true,
                "Host/singleplayer only. When a triggered death head enters an extraction point, immediately triggers the vanilla revive.");

            ReviveTimingPolicyEntry = config.Bind(
                "Instant Revive",
                "Revive Timing Policy",
                ReviveTimingPolicy.Auto,
                "Auto uses StableDelayed when Vippy.REPOFidelity is loaded and Instant otherwise. Instant revives during PlayerDeathHead.Update. StableDelayed waits briefly for vanilla camera/spectate state before reviving.");

            StableDelayedReviveDelay = config.Bind(
                "Instant Revive",
                "Stable Delayed Revive Delay",
                0.75f,
                new ConfigDescription(
                    "Seconds to wait before automatic revive when StableDelayed timing is active.",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            EnableFallbackExtractionDetection = config.Bind(
                "Instant Revive",
                "Enable Fallback Extraction Detection",
                false,
                "Diagnostic compatibility fallback. Disabled by default so revive follows vanilla extraction point state. When enabled, an extra extraction-volume scan is allowed only near known extraction points.");

            REPOFidelityClientProtection = config.Bind(
                "REPOFidelity Compatibility",
                "REPOFidelity Client Protection",
                ClientProtectionMode.Auto,
                "Auto enables local protection only when Vippy.REPOFidelity is loaded. Always runs it after every revive. Off disables the client-side compatibility protection.");

            PostReviveProtectionWindow = config.Bind(
                "REPOFidelity Compatibility",
                "Post Revive Protection Window",
                0.75f,
                new ConfigDescription(
                    "Seconds to keep refreshing non-destructive local camera/audio/post-processing state after vanilla ReviveRPC returns.",
                    new AcceptableValueRange<float>(0.1f, 3.0f)));

            DebugLogging = config.Bind(
                "Diagnostics",
                "Debug Logging",
                false,
                "Write instant revive and REPOFidelity client-protection details to the BepInEx log.");
        }

        internal static float SafeProtectionWindow()
        {
            return PostReviveProtectionWindow == null
                ? 0.75f
                : Mathf.Clamp(PostReviveProtectionWindow.Value, 0.1f, 3.0f);
        }

        internal static float SafeStableDelayedReviveDelay()
        {
            return StableDelayedReviveDelay == null
                ? 0.75f
                : Mathf.Clamp(StableDelayedReviveDelay.Value, 0.1f, 3.0f);
        }

        internal static ReviveTimingPolicy SafeReviveTimingPolicy()
        {
            return ReviveTimingPolicyEntry == null ? ReviveTimingPolicy.Auto : ReviveTimingPolicyEntry.Value;
        }

        internal static ReviveTimingPolicy EffectiveReviveTimingPolicy()
        {
            ReviveTimingPolicy policy = SafeReviveTimingPolicy();
            if (policy != ReviveTimingPolicy.Auto)
            {
                return policy;
            }

            return ClientReviveProtection.IsREPOFidelityLoaded()
                ? ReviveTimingPolicy.StableDelayed
                : ReviveTimingPolicy.Instant;
        }
    }

    internal static class InstantReviveController
    {
        private const float RetryIntervalSeconds = 0.12f;
        private const float DebugLogIntervalSeconds = 0.75f;

        private sealed class ReviveState
        {
            internal bool ReviveCompleted;
            internal bool WasTriggered;
            internal bool ReviveQueued;
            internal float LastAttemptTime;
            internal float NextDebugLogTime;
        }

        private static readonly Dictionary<int, ReviveState> States = new Dictionary<int, ReviveState>();
        private static readonly Collider[] OverlapBuffer = new Collider[32];

        private static readonly FieldInfo PlayerDeathHeadTriggeredField =
            typeof(PlayerDeathHead).GetField("triggered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDeathHeadRoomVolumeCheckField =
            typeof(PlayerDeathHead).GetField("roomVolumeCheck", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDeathHeadInExtractionPointField =
            typeof(PlayerDeathHead).GetField("inExtractionPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDeathHeadPhysGrabObjectField =
            typeof(PlayerDeathHead).GetField("physGrabObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarDeadSetField =
            typeof(PlayerAvatar).GetField("deadSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarIsDisabledField =
            typeof(PlayerAvatar).GetField("isDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoomVolumeCheckInExtractionPointField =
            typeof(RoomVolumeCheck).GetField("inExtractionPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoomVolumeCheckMaskField =
            typeof(RoomVolumeCheck).GetField("Mask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoundDirectorExtractionPointCurrentField =
            typeof(RoundDirector).GetField("extractionPointCurrent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoundDirectorExtractionPointListField =
            typeof(RoundDirector).GetField("extractionPointList", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void Reset()
        {
            States.Clear();
        }

        internal static void ProcessHead(PlayerDeathHead head)
        {
            if (!IsHostOrSingleplayer() ||
                ModConfig.EnableInstantExtractionRevive == null ||
                !ModConfig.EnableInstantExtractionRevive.Value ||
                head == null)
            {
                return;
            }

            int id = head.GetInstanceID();
            ReviveState state;
            if (!States.TryGetValue(id, out state))
            {
                state = new ReviveState();
                States[id] = state;
            }

            bool triggered = IsTriggered(head);
            if (!triggered)
            {
                state.ReviveCompleted = false;
                state.WasTriggered = false;
                state.ReviveQueued = false;
                state.LastAttemptTime = 0f;
                return;
            }

            if (!state.WasTriggered)
            {
                state.ReviveCompleted = false;
                state.WasTriggered = true;
                state.ReviveQueued = false;
                state.LastAttemptTime = 0f;
            }

            if (state.ReviveCompleted || state.ReviveQueued)
            {
                return;
            }

            bool roomCheckInside;
            bool headInside;
            bool fallbackInside;
            bool inside = IsInsideExtractionPoint(head, out roomCheckInside, out headInside, out fallbackInside);

            if (!inside)
            {
                DebugLogState(state, "Revive scan: host=True, triggered=True, roomCheck=" + roomCheckInside + ", headField=" + headInside + ", fallback=" + fallbackInside + ", inside=False.");
                return;
            }

            if (state.LastAttemptTime > 0f && Time.time - state.LastAttemptTime < RetryIntervalSeconds)
            {
                return;
            }

            if (state.LastAttemptTime <= 0f)
            {
                state.NextDebugLogTime = 0f;
            }

            state.LastAttemptTime = Time.time;

            ReviveTimingPolicy policy = ModConfig.EffectiveReviveTimingPolicy();

            if (policy == ReviveTimingPolicy.Instant || Plugin.Instance == null)
            {
                ExecuteReviveAttempt(head, state, roomCheckInside, headInside, fallbackInside, "instant revive", false);
                return;
            }

            state.ReviveQueued = true;
            DebugLogState(state, "queued stable delayed revive. policy=" + policy + ", repoFidelityLoaded=" + ClientReviveProtection.IsREPOFidelityLoaded() + ", delay=" + ModConfig.SafeStableDelayedReviveDelay().ToString("F2") + ", roomCheck=" + roomCheckInside + ", headField=" + headInside + ", fallback=" + fallbackInside + ".");
            try
            {
                Plugin.Instance.StartReviveRoutine(RunStableDelayedRevive(head, state));
            }
            catch (Exception ex)
            {
                state.ReviveQueued = false;
                LogException("queue stable delayed revive", ex);
            }
        }

        private static IEnumerator RunStableDelayedRevive(PlayerDeathHead head, ReviveState state)
        {
            float reviveAllowedAt = Time.time + ModConfig.SafeStableDelayedReviveDelay();

            while (true)
            {
                if (head == null ||
                    ModConfig.EnableInstantExtractionRevive == null ||
                    !ModConfig.EnableInstantExtractionRevive.Value ||
                    !IsHostOrSingleplayer() ||
                    !IsTriggered(head) ||
                    head.playerAvatar == null)
                {
                    if (state != null)
                    {
                        state.ReviveQueued = false;
                    }

                    DebugLog("stable delayed revive aborted before attempt; death head/player state changed.");
                    yield break;
                }

                bool roomCheckInside;
                bool headInside;
                bool fallbackInside;
                bool inside = IsInsideExtractionPoint(head, out roomCheckInside, out headInside, out fallbackInside);
                if (!inside)
                {
                    if (state != null)
                    {
                        state.ReviveQueued = false;
                    }

                    DebugLogState(state, "stable delayed revive aborted; head left extraction point. roomCheck=" + roomCheckInside + ", headField=" + headInside + ", fallback=" + fallbackInside + ".");
                    yield break;
                }

                if (Time.time < reviveAllowedAt)
                {
                    DebugLogState(state, "stable delayed revive waiting for delay. remaining=" + Mathf.Max(0f, reviveAllowedAt - Time.time).ToString("F2") + ", roomCheck=" + roomCheckInside + ", headField=" + headInside + ".");
                    yield return null;
                    continue;
                }

                if (!ReviveDiagnostics.AreCriticalDependenciesReadyNoLog(head.playerAvatar))
                {
                    DebugLogState(state, "stable delayed revive waiting; vanilla ReviveRPC dependencies are not ready. " + ReviveDiagnostics.BuildReadinessReport(head.playerAvatar, false));
                    yield return null;
                    continue;
                }

                if (state != null)
                {
                    state.ReviveQueued = false;
                }

                ExecuteReviveAttempt(head, state, roomCheckInside, headInside, fallbackInside, "stable delayed revive", false);
                yield break;
            }
        }

        private static void ExecuteReviveAttempt(PlayerDeathHead head, ReviveState state, bool roomCheckInside, bool headInside, bool fallbackInside, string stage, bool requirePreflight)
        {
            if (head == null || head.playerAvatar == null)
            {
                return;
            }

            PlayerAvatar player = head.playerAvatar;
            Vector3 expectedPosition;
            bool hasExpectedPosition = TryGetExpectedRevivePosition(head, out expectedPosition);

            if (requirePreflight && !ReviveDiagnostics.AreCriticalDependenciesReady(player, stage + " preflight"))
            {
                DebugLogState(state, stage + ": revive delayed; vanilla ReviveRPC dependencies are not ready.");
                return;
            }

            SetExtractionState(head, true);

            bool hadException;
            bool successObserved;
            bool completed = TryRevive(head, state, roomCheckInside, headInside, fallbackInside, stage, hasExpectedPosition, expectedPosition, out hadException, out successObserved);
            if (completed && !hadException && state != null)
            {
                state.ReviveCompleted = true;
            }

            if (hadException)
            {
                DebugLogState(state, stage + ": skipped post revive protection because vanilla ReviveRPC threw.");
            }
        }

        private static bool TryRevive(PlayerDeathHead head, ReviveState state, bool roomCheckInside, bool headInside, bool fallbackInside, string stage, bool hasExpectedPosition, Vector3 expectedPosition, out bool hadException, out bool successObserved)
        {
            hadException = false;
            successObserved = false;

            try
            {
                DebugLogState(state, stage + ": revive attempt. effectivePolicy=" + ModConfig.EffectiveReviveTimingPolicy() + ", roomCheck=" + roomCheckInside + ", headFieldBefore=" + headInside + ", fallback=" + fallbackInside + ", expectedPosition=" + FormatExpectedPosition(hasExpectedPosition, expectedPosition) + ", repoFidelityProtection=" + ClientReviveProtection.WouldRunFor(head.playerAvatar) + ".");
                head.Revive();
                successObserved = HasReviveClearlySucceeded(head);
                bool multiplayer = IsMultiplayer();
                DebugLogState(state, stage + ": revive returned. multiplayer=" + multiplayer + ", headFieldAfter=" + GetBool(PlayerDeathHeadInExtractionPointField, head, false, "PlayerDeathHead.inExtractionPoint") + ", triggeredAfter=" + IsTriggered(head) + ", playerDeadSet=" + GetPlayerBool(head, PlayerAvatarDeadSetField, "PlayerAvatar.deadSet") + ", playerIsDisabled=" + GetPlayerBool(head, PlayerAvatarIsDisabledField, "PlayerAvatar.isDisabled") + ", successObserved=" + successObserved + ".");
                return successObserved || multiplayer;
            }
            catch (Exception ex)
            {
                hadException = true;
                LogException(stage + " revive attempt", ex);
                successObserved = HasReviveClearlySucceeded(head);
                DebugLogState(state, stage + ": revive threw. triggeredAfter=" + IsTriggered(head) + ", playerDeadSet=" + GetPlayerBool(head, PlayerAvatarDeadSetField, "PlayerAvatar.deadSet") + ", playerIsDisabled=" + GetPlayerBool(head, PlayerAvatarIsDisabledField, "PlayerAvatar.isDisabled") + ", successObservedAfterException=" + successObserved + ".");
                return successObserved;
            }
        }

        private static bool IsTriggered(PlayerDeathHead head)
        {
            return GetBool(PlayerDeathHeadTriggeredField, head, false, "PlayerDeathHead.triggered");
        }

        private static bool IsInsideExtractionPoint(PlayerDeathHead head, out bool roomCheckInside, out bool headInside, out bool fallbackInside)
        {
            roomCheckInside = false;
            headInside = GetBool(PlayerDeathHeadInExtractionPointField, head, false, "PlayerDeathHead.inExtractionPoint");
            fallbackInside = false;

            RoomVolumeCheck check = GetRoomVolumeCheck(head);

            if (check != null && RoomVolumeCheckInExtractionPointField != null)
            {
                roomCheckInside = GetBool(RoomVolumeCheckInExtractionPointField, check, false, "RoomVolumeCheck.inExtractionPoint");
                if (roomCheckInside)
                {
                    return true;
                }
            }

            if (IsFallbackExtractionDetectionEnabled())
            {
                fallbackInside = IsIndependentlyInsideExtractionPoint(head, check);
            }

            return headInside || fallbackInside;
        }

        private static bool HasReviveClearlySucceeded(PlayerDeathHead head)
        {
            if (head == null)
            {
                return true;
            }

            PlayerAvatar player = head.playerAvatar;
            if (player == null)
            {
                return !IsTriggered(head);
            }

            bool deadSet = GetBool(PlayerAvatarDeadSetField, player, true, "PlayerAvatar.deadSet");
            bool isDisabled = GetBool(PlayerAvatarIsDisabledField, player, true, "PlayerAvatar.isDisabled");
            return !deadSet && !isDisabled;
        }

        private static RoomVolumeCheck GetRoomVolumeCheck(PlayerDeathHead head)
        {
            if (PlayerDeathHeadRoomVolumeCheckField == null || head == null)
            {
                return null;
            }

            try
            {
                return PlayerDeathHeadRoomVolumeCheckField.GetValue(head) as RoomVolumeCheck;
            }
            catch (Exception ex)
            {
                DebugLog("PlayerDeathHead.roomVolumeCheck read failed: " + ex.Message);
                return null;
            }
        }

        private static bool IsIndependentlyInsideExtractionPoint(PlayerDeathHead head, RoomVolumeCheck check)
        {
            if (check != null)
            {
                Vector3 size = check.currentSize;
                if (size == Vector3.zero)
                {
                    size = check.transform.localScale;
                }

                LayerMask mask = GetRoomVolumeMask(check);
                Vector3 position = check.transform.position + check.transform.rotation * check.CheckPosition;
                int count = Physics.OverlapBoxNonAlloc(
                    position,
                    size * 0.5f,
                    OverlapBuffer,
                    check.transform.rotation,
                    mask,
                    QueryTriggerInteraction.Collide);

                return AnyExtractionRoomVolume(count) && IsNearKnownExtractionPoint(head.transform.position);
            }

            int fallbackCount = Physics.OverlapSphereNonAlloc(
                head.transform.position,
                0.35f,
                OverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            return AnyExtractionRoomVolume(fallbackCount) && IsNearKnownExtractionPoint(head.transform.position);
        }

        private static bool IsFallbackExtractionDetectionEnabled()
        {
            return ModConfig.EnableFallbackExtractionDetection != null &&
                ModConfig.EnableFallbackExtractionDetection.Value;
        }

        private static bool IsNearKnownExtractionPoint(Vector3 position)
        {
            const float maxDistance = 12f;

            try
            {
                if (RoundDirector.instance == null)
                {
                    return false;
                }

                ExtractionPoint current = GetField<ExtractionPoint>(RoundDirectorExtractionPointCurrentField, RoundDirector.instance);
                if (current != null && Vector3.Distance(position, current.transform.position) <= maxDistance)
                {
                    return true;
                }

                List<GameObject> extractionPoints = GetField<List<GameObject>>(RoundDirectorExtractionPointListField, RoundDirector.instance);
                if (extractionPoints == null)
                {
                    return false;
                }

                for (int i = 0; i < extractionPoints.Count; i++)
                {
                    GameObject extractionPoint = extractionPoints[i];
                    if (extractionPoint != null && Vector3.Distance(position, extractionPoint.transform.position) <= maxDistance)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog("Known extraction point fallback check failed: " + ex.Message);
            }

            return false;
        }

        private static LayerMask GetRoomVolumeMask(RoomVolumeCheck check)
        {
            if (check != null && RoomVolumeCheckMaskField != null)
            {
                object value = RoomVolumeCheckMaskField.GetValue(check);
                if (value is LayerMask)
                {
                    return (LayerMask)value;
                }
            }

            return ~0;
        }

        private static bool AnyExtractionRoomVolume(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Collider collider = OverlapBuffer[i];
                if (collider == null)
                {
                    continue;
                }

                RoomVolume room = collider.GetComponent<RoomVolume>();
                if (room == null)
                {
                    room = collider.GetComponentInParent<RoomVolume>();
                }

                if (room != null && room.Extraction)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetExtractionState(PlayerDeathHead head, bool value)
        {
            SetBool(PlayerDeathHeadInExtractionPointField, head, value, "PlayerDeathHead.inExtractionPoint");

            RoomVolumeCheck check = GetRoomVolumeCheck(head);
            if (check != null)
            {
                SetBool(RoomVolumeCheckInExtractionPointField, check, value, "RoomVolumeCheck.inExtractionPoint");
            }
        }

        private static bool TryGetExpectedRevivePosition(PlayerDeathHead head, out Vector3 position)
        {
            position = Vector3.zero;
            if (head == null)
            {
                return false;
            }

            try
            {
                PhysGrabObject physGrabObject = GetPhysGrabObject(head);
                if (physGrabObject != null)
                {
                    position = physGrabObject.centerPoint - Vector3.up * 0.25f;
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog("Expected revive position from death head PhysGrabObject failed: " + ex.Message);
            }

            position = head.transform.position;
            return true;
        }

        private static PhysGrabObject GetPhysGrabObject(PlayerDeathHead head)
        {
            if (PlayerDeathHeadPhysGrabObjectField == null || head == null)
            {
                return null;
            }

            try
            {
                return PlayerDeathHeadPhysGrabObjectField.GetValue(head) as PhysGrabObject;
            }
            catch (Exception ex)
            {
                DebugLog("PlayerDeathHead.physGrabObject read failed: " + ex.Message);
                return null;
            }
        }

        private static bool IsHostOrSingleplayer()
        {
            try
            {
                return SemiFunc.IsMasterClientOrSingleplayer();
            }
            catch
            {
                return true;
            }
        }

        private static bool IsMultiplayer()
        {
            try
            {
                return GameManager.Multiplayer();
            }
            catch
            {
                return false;
            }
        }

        private static bool GetBool(FieldInfo field, object instance, bool defaultValue, string name)
        {
            if (field == null || instance == null)
            {
                return defaultValue;
            }

            try
            {
                object value = field.GetValue(instance);
                if (value is bool)
                {
                    return (bool)value;
                }
            }
            catch (Exception ex)
            {
                DebugLog(name + " read failed: " + ex.Message);
            }

            return defaultValue;
        }

        internal static T GetField<T>(FieldInfo field, object instance) where T : class
        {
            if (field == null || instance == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(instance) as T;
            }
            catch (Exception ex)
            {
                DebugLog("Field read failed: " + ex.Message);
                return null;
            }
        }

        private static string GetPlayerBool(PlayerDeathHead head, FieldInfo field, string name)
        {
            PlayerAvatar player = head == null ? null : head.playerAvatar;
            if (field == null || player == null)
            {
                return "unknown";
            }

            try
            {
                object value = field.GetValue(player);
                if (value is bool)
                {
                    return ((bool)value).ToString();
                }
            }
            catch (Exception ex)
            {
                DebugLog(name + " read failed: " + ex.Message);
            }

            return "unknown";
        }

        private static void SetBool(FieldInfo field, object instance, bool value, string name)
        {
            if (field == null || instance == null)
            {
                DebugLog(name + " is not available; vanilla Revive may no-op if the game field is still false.");
                return;
            }

            try
            {
                field.SetValue(instance, value);
            }
            catch (Exception ex)
            {
                LogException(name + " write", ex);
            }
        }

        private static void DebugLogState(ReviveState state, string message)
        {
            if (ModConfig.DebugLogging == null || !ModConfig.DebugLogging.Value || state == null)
            {
                return;
            }

            if (Time.time < state.NextDebugLogTime)
            {
                return;
            }

            state.NextDebugLogTime = Time.time + DebugLogIntervalSeconds;
            Plugin.Log.LogInfo(message);
        }

        private static void DebugLog(string message)
        {
            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
            {
                Plugin.Log.LogInfo(message);
            }
        }

        private static string FormatExpectedPosition(bool hasExpectedPosition, Vector3 expectedPosition)
        {
            if (!hasExpectedPosition)
            {
                return "unknown";
            }

            return "(" + expectedPosition.x.ToString("F2") + ", " + expectedPosition.y.ToString("F2") + ", " + expectedPosition.z.ToString("F2") + ")";
        }

        internal static void LogException(string stage, Exception ex)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            Exception report = ex;
            if (ex is TargetInvocationException && ex.InnerException != null)
            {
                report = ex.InnerException;
            }

            Plugin.Log.LogWarning(stage + " failed: " + report.GetType().Name + ": " + report.Message);
            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
            {
                if (report.StackTrace != null)
                {
                    Plugin.Log.LogWarning(stage + " stack: " + report.StackTrace);
                }

                if (!object.ReferenceEquals(report, ex))
                {
                    Plugin.Log.LogWarning(stage + " wrapper: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }
    }

    internal static class ClientReviveProtection
    {
        private const string REPOFidelityGuid = "Vippy.REPOFidelity";
        private static readonly FieldInfo SpectateCameraMainCameraField =
            typeof(SpectateCamera).GetField("MainCamera", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo SpectateCameraParentObjectField =
            typeof(SpectateCamera).GetField("ParentObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo SpectateCameraPreviousParentField =
            typeof(SpectateCamera).GetField("PreviousParent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo AudioManagerAudioListenerField =
            typeof(AudioManager).GetField("AudioListener", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarIsLocalField =
            typeof(PlayerAvatar).GetField("isLocal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static void Reset()
        {
        }

        internal static void OnReviveRpc(PlayerAvatar player)
        {
            if (!WouldRunFor(player) || Plugin.Instance == null)
            {
                return;
            }

            Plugin.Instance.StartProtectionRoutine(player, false, Vector3.zero, "post revive rpc protection");
        }

        internal static IEnumerator Run(PlayerAvatar player)
        {
            return Run(player, false, Vector3.zero, "post revive protection");
        }

        internal static IEnumerator Run(PlayerAvatar player, bool hasExpectedPosition, Vector3 expectedPosition, string reason)
        {
            if (!WouldRunFor(player))
            {
                yield break;
            }

            float endTime = Time.time + ModConfig.SafeProtectionWindow();
            DebugLog("Starting local " + reason + ".");
            yield return null;

            while (Time.time <= endTime)
            {
                ApplyLocalFixes(player);
                yield return null;
            }

            ApplyLocalFixes(player);
            DebugLog("Finished local " + reason + ".");
        }

        internal static bool WouldRunFor(PlayerAvatar player)
        {
            if (ModConfig.REPOFidelityClientProtection == null ||
                ModConfig.REPOFidelityClientProtection.Value == ClientProtectionMode.Off ||
                player == null ||
                !IsLocalPlayer(player))
            {
                return false;
            }

            if (ModConfig.REPOFidelityClientProtection.Value == ClientProtectionMode.Always)
            {
                return true;
            }

            return IsREPOFidelityLoaded();
        }

        private static bool IsLocalPlayer(PlayerAvatar player)
        {
            try
            {
                if (PlayerAvatarIsLocalField != null)
                {
                    object value = PlayerAvatarIsLocalField.GetValue(player);
                    if (value is bool)
                    {
                        return (bool)value;
                    }
                }
            }
            catch
            {
            }

            return player == PlayerAvatar.instance;
        }

        internal static bool IsREPOFidelityLoaded()
        {
            try
            {
                return Chainloader.PluginInfos.ContainsKey(REPOFidelityGuid);
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyLocalFixes(PlayerAvatar player)
        {
            if (player == null)
            {
                player = PlayerAvatar.instance;
            }

            InspectSpectateCamera(SpectateCamera.instance);

            if (player != null && player.localCamera != null)
            {
                SafeCall(player.localCamera.Teleported, "PlayerLocalCamera.Teleported");
            }

            EnsureObject("CameraZoom.Instance", CameraZoom.Instance);
            EnsureObject("PostProcessing.Instance", PostProcessing.Instance);
            EnsureObject("AudioManager.instance", AudioManager.instance);

            if (AudioManager.instance != null)
            {
                EnsureObject("AudioManager.AudioListener", GetField<AudioListenerFollow>(AudioManagerAudioListenerField, AudioManager.instance));
            }
        }

        private static void InspectSpectateCamera(SpectateCamera spectate)
        {
            if (spectate == null)
            {
                DebugLog("SpectateCamera.instance is not ready during local revive protection.");
                return;
            }

            EnsureObject("SpectateCamera.MainCamera", GetField<Camera>(SpectateCameraMainCameraField, spectate));
            EnsureObject("SpectateCamera.ParentObject", GetField<Transform>(SpectateCameraParentObjectField, spectate));
            EnsureObject("SpectateCamera.PreviousParent", GetField<Transform>(SpectateCameraPreviousParentField, spectate));
            EnsureObject("SpectateCamera.normalTransformPivot", spectate.normalTransformPivot);
        }

        private static void SafeCall(Action action, string name)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                DebugLog(name + " failed: " + ex.Message);
            }
        }

        private static T GetField<T>(FieldInfo field, object instance) where T : class
        {
            if (field == null || instance == null)
            {
                return null;
            }

            try
            {
                return field.GetValue(instance) as T;
            }
            catch (Exception ex)
            {
                DebugLog("Field read failed: " + ex.Message);
                return null;
            }
        }

        private static void EnsureObject(string name, UnityEngine.Object obj)
        {
            if (obj == null)
            {
                DebugLog(name + " is not ready during local revive protection.");
            }
        }

        private static void DebugLog(string message)
        {
            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
            {
                Plugin.Log.LogInfo(message);
            }
        }
    }

    internal static class ReviveDiagnostics
    {
        private static readonly FieldInfo PlayerAvatarPlayerDeathHeadField =
            typeof(PlayerAvatar).GetField("playerDeathHead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarPlayerAvatarCollisionField =
            typeof(PlayerAvatar).GetField("playerAvatarCollision", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarIsLocalField =
            typeof(PlayerAvatar).GetField("isLocal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDeathHeadPhysGrabObjectField =
            typeof(PlayerDeathHead).GetField("physGrabObject", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static bool AreCriticalDependenciesReady(PlayerAvatar player, string reason)
        {
            PlayerDeathHead head = GetDeathHead(player);
            bool ready = player != null &&
                head != null &&
                GetPhysGrabObject(head) != null &&
                player.playerHealth != null &&
                player.playerAvatarVisuals != null &&
                player.playerDeathEffects != null &&
                player.playerReviveEffects != null &&
                GetAvatarCollision(player) != null &&
                player.RoomVolumeCheck != null;

            if (player != null && IsLocalPlayer(player))
            {
                ready = ready &&
                    player.playerTransform != null &&
                    player.playerTransform.parent != null &&
                    CameraAim.Instance != null &&
                    CameraPosition.instance != null &&
                    GameDirector.instance != null &&
                    SpectateCamera.instance != null &&
                    PlayerController.instance != null &&
                    CameraGlitch.Instance != null;
            }

            if (ModConfig.DebugLogging != null && ModConfig.DebugLogging.Value)
            {
                Plugin.Log.LogInfo(reason + ": " + BuildReadinessReport(player, ready));
            }

            return ready;
        }

        internal static void LogReviveRpcPrefix(PlayerAvatar player)
        {
            if (ModConfig.DebugLogging == null || !ModConfig.DebugLogging.Value)
            {
                return;
            }

            Plugin.Log.LogInfo("ReviveRPC prefix: " + BuildReadinessReport(player, AreCriticalDependenciesReadyNoLog(player)));
        }

        internal static bool AreCriticalDependenciesReadyNoLog(PlayerAvatar player)
        {
            if (player == null ||
                player.playerHealth == null ||
                player.playerAvatarVisuals == null ||
                player.playerDeathEffects == null ||
                player.playerReviveEffects == null ||
                player.RoomVolumeCheck == null)
            {
                return false;
            }

            PlayerDeathHead head = GetDeathHead(player);
            if (head == null || GetPhysGrabObject(head) == null || GetAvatarCollision(player) == null)
            {
                return false;
            }

            if (!IsLocalPlayer(player))
            {
                return true;
            }

            return player.playerTransform != null &&
                player.playerTransform.parent != null &&
                CameraAim.Instance != null &&
                CameraPosition.instance != null &&
                GameDirector.instance != null &&
                SpectateCamera.instance != null &&
                PlayerController.instance != null &&
                CameraGlitch.Instance != null;
        }

        internal static string BuildReadinessReport(PlayerAvatar player, bool ready)
        {
            if (player == null)
            {
                return "ready=False, playerAvatar=null.";
            }

            PlayerDeathHead head = GetDeathHead(player);
            bool local = IsLocalPlayer(player);
            string playerTransformParent = player.playerTransform == null
                ? "unknown"
                : FormatReady(player.playerTransform.parent != null);

            return "ready=" + ready +
                ", isLocal=" + local +
                ", playerDeathHead=" + FormatReady(head != null) +
                ", physGrabObject=" + FormatReady(head != null && GetPhysGrabObject(head) != null) +
                ", playerHealth=" + FormatReady(player.playerHealth != null) +
                ", playerAvatarVisuals=" + FormatReady(player.playerAvatarVisuals != null) +
                ", playerDeathEffects=" + FormatReady(player.playerDeathEffects != null) +
                ", playerReviveEffects=" + FormatReady(player.playerReviveEffects != null) +
                ", playerAvatarCollision=" + FormatReady(GetAvatarCollision(player) != null) +
                ", playerTransform=" + FormatReady(player.playerTransform != null) +
                ", playerTransform.parent=" + playerTransformParent +
                ", CameraAim.Instance=" + FormatReady(CameraAim.Instance != null) +
                ", CameraPosition.instance=" + FormatReady(CameraPosition.instance != null) +
                ", GameDirector.instance=" + FormatReady(GameDirector.instance != null) +
                ", SpectateCamera.instance=" + FormatReady(SpectateCamera.instance != null) +
                ", PlayerController.instance=" + FormatReady(PlayerController.instance != null) +
                ", CameraGlitch.Instance=" + FormatReady(CameraGlitch.Instance != null) +
                ", RoomVolumeCheck=" + FormatReady(player.RoomVolumeCheck != null) +
                ".";
        }

        private static bool IsLocalPlayer(PlayerAvatar player)
        {
            if (player == null)
            {
                return false;
            }

            try
            {
                if (PlayerAvatarIsLocalField != null)
                {
                    object value = PlayerAvatarIsLocalField.GetValue(player);
                    if (value is bool)
                    {
                        return (bool)value;
                    }
                }
            }
            catch
            {
            }

            return player == PlayerAvatar.instance;
        }

        private static PlayerDeathHead GetDeathHead(PlayerAvatar player)
        {
            return InstantReviveController.GetField<PlayerDeathHead>(PlayerAvatarPlayerDeathHeadField, player);
        }

        private static PhysGrabObject GetPhysGrabObject(PlayerDeathHead head)
        {
            return InstantReviveController.GetField<PhysGrabObject>(PlayerDeathHeadPhysGrabObjectField, head);
        }

        private static PlayerAvatarCollision GetAvatarCollision(PlayerAvatar player)
        {
            return InstantReviveController.GetField<PlayerAvatarCollision>(PlayerAvatarPlayerAvatarCollisionField, player);
        }

        private static string FormatReady(bool ready)
        {
            return ready ? "ok" : "null";
        }
    }

    [HarmonyPatch(typeof(PlayerDeathHead), "Update")]
    internal static class PlayerDeathHeadUpdatePatch
    {
        private static void Postfix(PlayerDeathHead __instance)
        {
            InstantReviveController.ProcessHead(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerAvatar), "ReviveRPC")]
    internal static class PlayerAvatarReviveRpcPatch
    {
        private static void Prefix(PlayerAvatar __instance)
        {
            ReviveDiagnostics.LogReviveRpcPrefix(__instance);
        }

        private static void Postfix(PlayerAvatar __instance)
        {
            ClientReviveProtection.OnReviveRpc(__instance);
        }
    }

    [HarmonyPatch(typeof(RunManager), "ChangeLevel")]
    internal static class RunManagerChangeLevelPatch
    {
        private static void Prefix()
        {
            InstantReviveController.Reset();
            ClientReviveProtection.Reset();
        }
    }
}
