# FidelityReviveFix Hook Audit

Validation date: 2026-05-31

Game assembly checked: `D:\SteamLibrary\steamapps\common\REPO\REPO_Data\Managed\Assembly-CSharp.dll`

Version audited: `0.1.2`

## Harmony Targets

### `PlayerDeathHead.Update()`

- Patch: Postfix.
- Purpose: host/singleplayer instant revive detection and safe revive queueing.
- Boundary: only runs the revive decision when `SemiFunc.IsMasterClientOrSingleplayer()` returns true and `Enable Instant Extraction Revive` is enabled.
- Failure downgrade: if extraction detection or vanilla revive no-ops, the mod keeps retrying on a short throttle instead of locking the death head permanently. `SafeInstant` mode queues one coroutine per death head and waits until end-of-frame plus one fixed update before calling vanilla revive.

### `PlayerDeathHead.Revive()`

- Patch: no Harmony patch; called through the vanilla method.
- Required vanilla state: `triggered == true`, `inExtractionPoint == true`, and `playerAvatar != null`.
- Compatibility note: before calling it, FidelityReviveFix writes `PlayerDeathHead.inExtractionPoint = true` and `RoomVolumeCheck.inExtractionPoint = true` when its RoomVolumeCheck or fallback extraction detection says the death head is inside the extraction point.

### `PlayerAvatar.ReviveRPC(bool, Photon.Pun.PhotonMessageInfo)`

- Patch: Postfix.
- Purpose: start the local REPOFidelity revive-protection window after the vanilla revive RPC is accepted.
- Boundary: client-side only, local player only. It does not add a custom network protocol and does not attempt to remotely repair unmodded clients.
- Failure downgrade: if REPOFidelity is absent and protection mode is `Auto`, no local protection work runs. In `0.1.2`, the same local protection can also run before a queued revive, after a failed revive attempt, and after the next frame even if the Postfix path did not run.

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
- `PlayerAvatar.deadSet`
- `PlayerAvatar.isDisabled`
- `PlayerAvatar.playerTransform`
- `PhysGrabObject.centerPoint`
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
