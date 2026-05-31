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
        public const string PluginVersion = "0.1.0";

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
        private sealed class ReviveState
        {
            internal bool ReviveIssued;
            internal bool WasTriggered;
        }

        private static readonly Dictionary<int, ReviveState> States = new Dictionary<int, ReviveState>();
        private static readonly Collider[] OverlapBuffer = new Collider[32];

        private static readonly FieldInfo PlayerDeathHeadTriggeredField =
            typeof(PlayerDeathHead).GetField("triggered", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly FieldInfo PlayerDeathHeadRoomVolumeCheckField =
            typeof(PlayerDeathHead).GetField("roomVolumeCheck", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
                state.ReviveIssued = false;
                state.WasTriggered = false;
                return;
            }

            if (!state.WasTriggered)
            {
                state.ReviveIssued = false;
                state.WasTriggered = true;
            }

            if (state.ReviveIssued || !IsInsideExtractionPoint(head))
            {
                return;
            }

            if (TryRevive(head))
            {
                state.ReviveIssued = true;
            }
        }

        private static bool TryRevive(PlayerDeathHead head)
        {
            try
            {
                DebugLog("Instant extraction revive triggered.");
                head.Revive();
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Instant extraction revive failed: " + ex.Message);
                return false;
            }
        }

        private static bool IsTriggered(PlayerDeathHead head)
        {
            if (head == null || PlayerDeathHeadTriggeredField == null)
            {
                return false;
            }

            object value = PlayerDeathHeadTriggeredField.GetValue(head);
            return value is bool && (bool)value;
        }

        private static bool IsInsideExtractionPoint(PlayerDeathHead head)
        {
            RoomVolumeCheck check = PlayerDeathHeadRoomVolumeCheckField == null
                ? null
                : PlayerDeathHeadRoomVolumeCheckField.GetValue(head) as RoomVolumeCheck;

            if (check != null && RoomVolumeCheckInExtractionPointField != null)
            {
                object value = RoomVolumeCheckInExtractionPointField.GetValue(check);
                if (value is bool && (bool)value)
                {
                    return true;
                }
            }

            return IsIndependentlyInsideExtractionPoint(head, check);
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
