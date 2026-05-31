# FidelityReviveFix

Instant extraction-point revive for R.E.P.O., with optional client-side compatibility protection for [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/).

## Overview

FidelityReviveFix can be used as a normal instant revive mod. When the host installs it, placing a dead player's head into an extraction point immediately triggers the game's vanilla revive flow.

For players who also run REPOFidelity, the mod includes a small post-revive local protection pass that refreshes spectate, camera, post-processing, and audio-listener state. This is intended to reduce revive timing issues such as getting stuck or falling into the void after revive.

## Multiplayer

- No REPOFidelity: usually only the host needs FidelityReviveFix.
- Host installs FidelityReviveFix: the host performs the instant revive and the game syncs it through vanilla revive RPCs.
- Non-host player has REPOFidelity: that player should also install FidelityReviveFix to get the local compatibility protection.
- A player who does not install this mod cannot have their own local REPOFidelity camera state fully repaired remotely.

## Build

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

To create the Thunderstore zip on the desktop:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -PackageToDesktop
```

The Thunderstore package files live in `package/`, and build output is written to `dist/`.
