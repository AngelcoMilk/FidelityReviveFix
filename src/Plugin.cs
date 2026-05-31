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
        public const string PluginVersion = "0.1.1";

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

        internal void StartProtectionRoutine(PlayerAvatar player)
        {
            StartCoroutine(ClientReviveProtection.Run(player));
        }
    }

    internal enum ClientProtectionMode
    {
        Auto,
        Always,
        Off
    }

    internal static class ModConfig
    {
        internal static ConfigEntry<bool> EnableInstantExtractionRevive;
        internal static ConfigEntry<ClientProtectionMode> REPOFidelityClientProtection;
        internal static ConfigEntry<float> PostReviveProtectionWindow;
        internal static ConfigEntry<bool> DebugLogging;

        internal static void Bind(ConfigFile config)
        {
            EnableInstantExtractionRevive = config.Bind(
                "Instant Revive",
                "Enable Instant Extraction Revive",
                true,
                "Host/singleplayer only. When a triggered death head enters an extraction point, immediately calls the vanilla revive.");

            REPOFidelityClientProtection = config.Bind(
                "REPOFidelity Compatibility",
                "REPOFidelity Client Protection",
                ClientProtectionMode.Auto,
                "Auto enables local post-revive protection only when Vippy.REPOFidelity is loaded. Always runs it after every revive. Off disables the client-side protection.");

            PostReviveProtectionWindow = config.Bind(
                "REPOFidelity Compatibility",
                "Post Revive Protection Window",
                0.75f,
                new ConfigDescription(
                    "Seconds to keep refreshing local camera/spectate state after the vanilla revive RPC. This happens after revive and does not delay the revive trigger.",
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
    }

    internal static class InstantReviveController
    {
        private const float RetryIntervalSeconds = 0.12f;
        private const float DebugLogIntervalSeconds = 0.75f;

        private sealed class ReviveState
        {
            internal bool ReviveCompleted;
            internal bool WasTriggered;
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

        private static readonly FieldInfo PlayerAvatarDeadSetField =
            typeof(PlayerAvatar).GetField("deadSet", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerAvatarIsDisabledField =
            typeof(PlayerAvatar).GetField("isDisabled", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoomVolumeCheckInExtractionPointField =
            typeof(RoomVolumeCheck).GetField("inExtractionPoint", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo RoomVolumeCheckMaskField =
            typeof(RoomVolumeCheck).GetField("Mask", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
                state.LastAttemptTime = 0f;
                return;
            }

            if (!state.WasTriggered)
            {
                state.ReviveCompleted = false;
                state.WasTriggered = true;
                state.LastAttemptTime = 0f;
            }

            if (state.ReviveCompleted)
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
            if (TryRevive(head, state, roomCheckInside, headInside, fallbackInside))
            {
                state.ReviveCompleted = true;
            }
        }

        private static bool TryRevive(PlayerDeathHead head, ReviveState state, bool roomCheckInside, bool headInside, bool fallbackInside)
        {
            try
            {
                SetBool(PlayerDeathHeadInExtractionPointField, head, true, "PlayerDeathHead.inExtractionPoint");
                head.Revive();
                bool succeeded = HasReviveClearlySucceeded(head);
                DebugLogState(state, "Instant extraction revive attempt. roomCheck=" + roomCheckInside + ", headFieldBefore=" + headInside + ", fallback=" + fallbackInside + ", headFieldAfter=" + GetBool(PlayerDeathHeadInExtractionPointField, head, false, "PlayerDeathHead.inExtractionPoint") + ", triggeredAfter=" + IsTriggered(head) + ", playerDeadSet=" + GetPlayerBool(head, PlayerAvatarDeadSetField, "PlayerAvatar.deadSet") + ", playerIsDisabled=" + GetPlayerBool(head, PlayerAvatarIsDisabledField, "PlayerAvatar.isDisabled") + ", successObserved=" + succeeded + ".");
                return succeeded;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Instant extraction revive failed: " + ex.Message);
                return false;
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

            fallbackInside = IsIndependentlyInsideExtractionPoint(head, check);
            return headInside || fallbackInside;
        }

        private static bool HasReviveClearlySucceeded(PlayerDeathHead head)
        {
            if (head == null)
            {
                return true;
            }

            if (!IsTriggered(head))
            {
                return true;
            }

            PlayerAvatar player = head.playerAvatar;
            if (player == null)
            {
                return false;
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

                return AnyExtractionRoomVolume(count);
            }

            int fallbackCount = Physics.OverlapSphereNonAlloc(
                head.transform.position,
                0.35f,
                OverlapBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            return AnyExtractionRoomVolume(fallbackCount);
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
                Plugin.Log.LogWarning(name + " write failed: " + ex.Message);
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
            if (!ShouldRunFor(player) || Plugin.Instance == null)
            {
                return;
            }

            Plugin.Instance.StartProtectionRoutine(player);
        }

        internal static IEnumerator Run(PlayerAvatar player)
        {
            float endTime = Time.time + ModConfig.SafeProtectionWindow();
            DebugLog("Starting local post-revive REPOFidelity protection.");

            while (Time.time <= endTime)
            {
                ApplyLocalFixes(player);
                yield return null;
            }

            ApplyLocalFixes(player);
            DebugLog("Finished local post-revive REPOFidelity protection.");
        }

        private static bool ShouldRunFor(PlayerAvatar player)
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

        private static bool IsREPOFidelityLoaded()
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

            SpectateCamera spectate = SpectateCamera.instance;
            if (spectate != null)
            {
                TryStopDeathSpectate(spectate);
                if (player != null)
                {
                    TryUpdateSpectatePlayer(spectate, player);
                }
            }

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

        private static void TryStopDeathSpectate(SpectateCamera spectate)
        {
            bool isDeathState;
            try
            {
                isDeathState = spectate.CheckState(SpectateCamera.State.Death);
            }
            catch
            {
                isDeathState = false;
            }

            if (isDeathState)
            {
                SafeCall(spectate.StopSpectate, "SpectateCamera.StopSpectate");
            }

            EnsureObject("SpectateCamera.MainCamera", GetField<Camera>(SpectateCameraMainCameraField, spectate));
            EnsureObject("SpectateCamera.ParentObject", GetField<Transform>(SpectateCameraParentObjectField, spectate));
            EnsureObject("SpectateCamera.PreviousParent", GetField<Transform>(SpectateCameraPreviousParentField, spectate));
            EnsureObject("SpectateCamera.normalTransformPivot", spectate.normalTransformPivot);
        }

        private static void TryUpdateSpectatePlayer(SpectateCamera spectate, PlayerAvatar player)
        {
            try
            {
                spectate.UpdatePlayer(player);
            }
            catch (Exception ex)
            {
                DebugLog("SpectateCamera.UpdatePlayer failed: " + ex.Message);
            }
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

            return field.GetValue(instance) as T;
        }

        private static void EnsureObject(string name, UnityEngine.Object obj)
        {
            if (obj == null)
            {
                DebugLog(name + " is not ready during post-revive protection.");
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
