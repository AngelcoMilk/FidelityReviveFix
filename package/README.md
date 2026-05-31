# FidelityReviveFix

## 将玩家死亡后的头放入撤离点即可自动复活

FidelityReviveFix 可以作为一个普通的撤离点复活 mod 使用。主机安装后，玩家死亡后的头进入撤离点时，会通过游戏原版复活流程自动复活，并支持多人模式。

默认情况下，本 mod 会根据本机是否安装 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) 自动选择复活时序：

- 未安装 REPOFidelity：进入撤离点后即时复活，接近普通即时复活 mod 的效果。
- 已安装 REPOFidelity：进入撤离点后短暂等待稳定状态再自动复活，用于修复 REPOFidelity 与其他复活类 mod 同时使用时可能出现的冲突 bug，例如第二次复活后掉入虚空、卡入虚空或卡在死亡视角。

### 多人安装建议

- 正常多人使用时，通常只需要主机安装 FidelityReviveFix。
- 主机负责撤离点复活，游戏会通过原版复活 RPC 同步给其他玩家。
- 如果非主机玩家自己安装了 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/)，也建议安装 FidelityReviveFix，以保证本地兼容保护生效。
- 未安装本 mod 的玩家，无法被远程完全修复自己本机的 REPOFidelity 相机或死亡视角状态。

### 主要配置

- `Enable Instant Extraction Revive`：启用撤离点自动复活。
- `Revive Timing Policy`：`Auto` / `Instant` / `StableDelayed`。默认 `Auto`，会在检测到 REPOFidelity 时使用稳定延迟复活，否则使用即时复活。
- `Stable Delayed Revive Delay`：稳定延迟复活的等待时间，默认 `0.75` 秒。
- `REPOFidelity Client Protection`：`Auto` / `Always` / `Off`。默认 `Auto`，只在检测到 REPOFidelity 时启用本地兼容保护。
- `Debug Logging`：输出复活时序和兼容保护诊断日志。

---

## Automatically Revive Players by Placing Their Head in the Extraction Point

FidelityReviveFix can be used as a regular extraction-point revive mod. When the host has it installed, a dead player's head entering the extraction point automatically revives that player through the game's vanilla revive flow, including in multiplayer.

By default, this mod chooses its revive timing based on whether [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) is installed locally:

- Without REPOFidelity: revive happens immediately when the head enters the extraction point, similar to a normal instant revive mod.
- With REPOFidelity: revive waits briefly for stable vanilla camera and spectate state before reviving. This is intended to fix conflict bugs between REPOFidelity and revive mods, such as falling into the void on the second revive, getting stuck in the void, or staying in the death camera.

### Multiplayer Install Guidance

- In normal multiplayer use, the host installing FidelityReviveFix is usually enough.
- The host performs the extraction-point revive, and the game syncs it to other players through the vanilla revive RPC.
- If a non-host player has [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) installed, that player should also install FidelityReviveFix so the local compatibility protection can take effect.
- A player who does not install this mod cannot have their own local REPOFidelity camera or death-view state fully repaired remotely.

### Main Config

- `Enable Instant Extraction Revive`: enables automatic extraction-point revive.
- `Revive Timing Policy`: `Auto` / `Instant` / `StableDelayed`. The default is `Auto`, which uses stable delayed revive when REPOFidelity is detected and instant revive otherwise.
- `Stable Delayed Revive Delay`: wait time for stable delayed revive, default `0.75` seconds.
- `REPOFidelity Client Protection`: `Auto` / `Always` / `Off`. The default is `Auto`, which enables local compatibility protection only when REPOFidelity is detected.
- `Debug Logging`: writes revive timing and compatibility diagnostics to the log.
