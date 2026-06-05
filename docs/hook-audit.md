# HellRevival Hook Audit

Validation date: 2026-06-05

Game assembly checked: `D:\SteamLibrary\steamapps\common\REPO\REPO_Data\Managed\Assembly-CSharp.dll`

Version audited: `0.1.6`

## Harmony Targets

### `PlayerDeathHead.Update()`

- Patch: Postfix.
- Purpose: host/singleplayer extraction-point revive detection and adaptive revive timing.
- Boundary: only runs the revive decision when `SemiFunc.IsMasterClientOrSingleplayer()` returns true and `Enable Automatic Revive` is enabled.
- Instant guarantee: the internal auto policy resolves to instant revive for targets that report no REPOFidelity risk. Unknown clients use the host-local policy, so they also revive instantly when the host has no local REPOFidelity risk.
- Failure downgrade: if extraction detection or vanilla revive no-ops, the mod keeps retrying on a short throttle instead of locking the death head permanently.

### Photon player properties

- Patch: no Harmony patch; written from plugin load/update and level-change housekeeping.
- Purpose: publish local HellRevival capability, plugin version, and local `Vippy.REPOFidelity` detection through `PhotonNetwork.LocalPlayer.SetCustomProperties`.
- Boundary: capability metadata only. The mod still uses vanilla `PlayerAvatar.ReviveRPC` for revive sync and does not add a custom revive RPC.
- Failure downgrade: if Photon local player or custom properties are unavailable, publish is skipped and the host falls back to the internal host-local policy.

### `PlayerDeathHead.Revive()`

- Patch: no Harmony patch; called through the vanilla method.
- Required vanilla state: `triggered == true`, `inExtractionPoint == true`, and `playerAvatar != null`.
- Compatibility note: before calling it, HellRevival only writes `PlayerDeathHead.inExtractionPoint = true` and `RoomVolumeCheck.inExtractionPoint = true` after vanilla extraction state or the explicitly enabled guarded fallback says the death head is inside the extraction point.

### `PlayerAvatar.ReviveRPC(bool, Photon.Pun.PhotonMessageInfo)`

- Patch: Prefix + Postfix.
- Purpose: Prefix logs read-only dependency readiness before vanilla revive runs. Postfix applies the configured revive-health top-up on the host/singleplayer side, then starts the local non-destructive REPOFidelity protection window when appropriate.
- Health boundary: extraction revives are topped up with `PlayerHealth.HealOther(extra, true)`, using the game's vanilla health sync. The mod does not write `PlayerHealth.health` directly.
- Compatibility boundary: local protection is local-player only. It does not add a custom network protocol and does not attempt to remotely repair unmodded clients.
- Safety note: HellRevival must not call `SpectateCamera.StopSpectate()` from its own protection path. Vanilla `PlayerAvatar.ReviveRPC` calls it during the local revive path, and calling it again can leave `SpectateCamera.instance` unavailable for the next death.

### `RunManager.ChangeLevel(bool, bool, RunManager.ChangeLevelType)`

- Patch: Prefix.
- Purpose: clear cached revive state and client protection state between levels.
- Boundary: local housekeeping only.
- Failure downgrade: if called unexpectedly, state is only reset and no gameplay action is triggered.

## Method Checks

Build validation verifies these methods before compiling:

- `PlayerDeathHead.Update`
- `PlayerDeathHead.Revive`
- `PlayerAvatar.ReviveRPC`
- `PlayerHealth.HealOther`
- `PhotonNetwork.LocalPlayer`
- `PhotonNetwork.PlayerList`
- `Photon.Realtime.Player.SetCustomProperties`
- `SpectateCamera.CheckState`
- `SpectateCamera.StopSpectate`
- `RunManager.ChangeLevel`

## Field Checks

Build validation verifies these fields before compiling:

- `PlayerDeathHead.triggered`
- `PlayerDeathHead.roomVolumeCheck`
- `PlayerDeathHead.inExtractionPoint`
- `PlayerDeathHead.physGrabObject`
- `PlayerAvatar.playerDeathHead`
- `PlayerAvatar.deadSet`
- `PlayerAvatar.isDisabled`
- `PlayerAvatar.playerTransform`
- `PlayerAvatar.playerAvatarVisuals`
- `PlayerAvatar.playerDeathEffects`
- `PlayerAvatar.playerReviveEffects`
- `PlayerAvatar.playerAvatarCollision`
- `PlayerAvatar.RoomVolumeCheck`
- `PlayerHealth.health`
- `PlayerHealth.maxHealth`
- `PlayerHealth.photonView`
- `PhysGrabObject.centerPoint`
- `CameraAim.Instance`
- `CameraPosition.instance`
- `RoomVolumeCheck.inExtractionPoint`
- `RoomVolumeCheck.Mask`
- `RoomVolumeCheck.CheckPosition`
- `RoomVolumeCheck.currentSize`
- `RoomVolume.Extraction`
- `SpectateCamera.MainCamera`
- `SpectateCamera.ParentObject`
- `SpectateCamera.PreviousParent`
- `SpectateCamera.normalTransformPivot`
- `AudioManager.AudioListener`
- `Photon.Realtime.Player.CustomProperties`
