# FidelityReviveFix Hook Audit

Validation date: 2026-05-31

Game assembly checked: `D:\SteamLibrary\steamapps\common\REPO\REPO_Data\Managed\Assembly-CSharp.dll`

Version audited: `0.1.4`

## Harmony Targets

### `PlayerDeathHead.Update()`

- Patch: Postfix.
- Purpose: host/singleplayer extraction-point revive detection and adaptive revive timing.
- Boundary: only runs the revive decision when `SemiFunc.IsMasterClientOrSingleplayer()` returns true and `Enable Instant Extraction Revive` is enabled.
- Failure downgrade: if extraction detection or vanilla revive no-ops, the mod keeps retrying on a short throttle instead of locking the death head permanently. `Revive Timing Policy = Auto` uses `Instant` when REPOFidelity is absent and `StableDelayed` when `Vippy.REPOFidelity` is loaded.
- Safety note: default revive detection now follows vanilla `RoomVolumeCheck.inExtractionPoint` or `PlayerDeathHead.inExtractionPoint`. The independent fallback scan is a diagnostic compatibility option and is disabled by default.

### `PlayerDeathHead.Revive()`

- Patch: no Harmony patch; called through the vanilla method.
- Required vanilla state: `triggered == true`, `inExtractionPoint == true`, and `playerAvatar != null`.
- Compatibility note: before calling it, FidelityReviveFix only writes `PlayerDeathHead.inExtractionPoint = true` and `RoomVolumeCheck.inExtractionPoint = true` after vanilla extraction state or the explicitly enabled guarded fallback says the death head is inside the extraction point.

### `PlayerAvatar.ReviveRPC(bool, Photon.Pun.PhotonMessageInfo)`

- Patch: Prefix + Postfix.
- Purpose: Prefix logs read-only dependency readiness before vanilla revive runs; Postfix starts the local non-destructive REPOFidelity revive-protection window after the vanilla revive RPC returns.
- Boundary: client-side only, local player only. It does not add a custom network protocol and does not attempt to remotely repair unmodded clients.
- Failure downgrade: if REPOFidelity is absent and protection mode is `Auto`, no local protection work runs. Local protection is not run before vanilla `ReviveRPC`, and failed revive attempts do not trigger position repair.
- Safety note: FidelityReviveFix must not call `SpectateCamera.StopSpectate()` from its own protection path. Vanilla `PlayerAvatar.ReviveRPC` calls it during the local revive path, and calling it again can leave `SpectateCamera.instance` unavailable for the next death.

### `RunManager.ChangeLevel(bool, bool, RunManager.ChangeLevelType)`

- Patch: Prefix.
- Purpose: clear cached revive state and client protection state between levels.
- Boundary: local housekeeping only.
- Failure downgrade: if called unexpectedly, state is only reset and no gameplay action is triggered.

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
