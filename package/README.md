# FidelityReviveFix v0.1.0 中文说明

将玩家死亡后的头放入撤离点即可瞬间复活。

FidelityReviveFix 可以作为一个普通的即时复活 mod 使用。它同时为 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) 做了特殊兼容补丁，用于尽量避免复活后卡入虚空、掉入虚空或卡在死亡视角的问题。

作者 / Author: **AngelcoMilk**  
Thunderstore: https://thunderstore.io/c/repo/p/AngelcoMilk/FidelityReviveFix/  
GitHub / Source: https://github.com/AngelcoMilk/FidelityReviveFix

## 功能

- 将玩家死亡后的头放入撤离点进行复活。
- 进入撤离点后瞬间复活。
- 支持多人模式。
- 即使没有安装 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/)，也可以作为普通复活 mod 使用。
- 兼容 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/)，用于减少复活后的虚空/相机状态问题。

## 多人安装建议

- 正常多人使用时，通常只需要主机安装 FidelityReviveFix。
- 主机负责即时撤离点复活，游戏会通过原版复活 RPC 同步给其他玩家。
- 如果非主机玩家自己安装了 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/)，也建议安装 FidelityReviveFix，以保证本地兼容保护生效。
- 未安装本 mod 的玩家，无法被远程完全修复自己本机的 REPOFidelity 相机状态。

不建议在主机上同时安装多个即时撤离点复活 mod，例如 InstantRevive 或 Hura Instant Revive，避免多个 mod 同时调用复活。

## 配置

- `Enable Instant Extraction Revive = true`
- `REPOFidelity Client Protection = Auto`
- `Post Revive Protection Window = 0.75`
- `Debug Logging = false`

`REPOFidelity Client Protection` 模式：

- `Auto`：仅在检测到 `Vippy.REPOFidelity` 时启用本地保护。
- `Always`：每次本地复活后都运行保护。
- `Off`：只作为普通即时撤离点复活 mod 使用。

REPOFidelity 保护会在收到原版复活 RPC 后执行，不会延迟主机的瞬间复活触发。

---

# FidelityReviveFix v0.1.0

Put a dead player's head into the extraction point to revive them instantly.

FidelityReviveFix can be used as a normal instant revive mod. It also includes a special compatibility patch for [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) to help prevent players from getting stuck or falling into the void after revive.

Author: **AngelcoMilk**  
Thunderstore: https://thunderstore.io/c/repo/p/AngelcoMilk/FidelityReviveFix/  
GitHub / Source: https://github.com/AngelcoMilk/FidelityReviveFix

## Features

- Revive a dead player by placing their head in the extraction point.
- Instant revive when the head enters the extraction point.
- Multiplayer supported.
- Works as a regular instant revive mod even when [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) is not installed.
- Includes a compatibility patch for [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) to reduce revive-related void/camera issues.

## Multiplayer Install Guidance

- In normal multiplayer use, the host installing FidelityReviveFix is usually enough.
- The host performs the instant extraction revive and the game syncs it through the vanilla revive RPC.
- If a non-host player has [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) installed, that player should also install FidelityReviveFix to get the local compatibility protection.
- A player who does not install this mod cannot have their own local REPOFidelity camera state fully repaired remotely.

Avoid running multiple instant extraction revive mods on the host at the same time, such as InstantRevive or Hura Instant Revive, unless you intentionally want multiple mods calling revive.

## Config

- `Enable Instant Extraction Revive = true`
- `REPOFidelity Client Protection = Auto`
- `Post Revive Protection Window = 0.75`
- `Debug Logging = false`

`REPOFidelity Client Protection` modes:

- `Auto`: run local protection only when `Vippy.REPOFidelity` is loaded.
- `Always`: run local protection after every local revive.
- `Off`: behave as a normal instant extraction revive mod only.

The REPOFidelity protection happens after the vanilla revive RPC is received. It does not delay the host's instant revive trigger.
