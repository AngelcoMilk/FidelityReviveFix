# FidelityReviveFix

## 中文说明

将玩家死亡后的头放入撤离点即可瞬间复活。

FidelityReviveFix 可以作为一个普通的即时复活 mod 使用。它同时为 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) 做了专门的兼容补丁，重点用于修复 REPOFidelity 与其他复活类 mod 同时使用时可能出现的冲突 bug，减少复活后卡入虚空、掉入虚空或卡在死亡视角的问题。

### 多人安装建议

- 正常多人使用时，通常只需要主机安装 FidelityReviveFix。
- 主机负责即时撤离点复活，游戏会通过原版复活 RPC 同步给其他玩家。
- 如果非主机玩家自己安装了 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/)，也建议安装 FidelityReviveFix，以保证本地兼容保护生效。
- 未安装本 mod 的玩家，无法被远程完全修复自己本机的 REPOFidelity 相机状态。

## English

Put a dead player's head into the extraction point to revive them instantly.

FidelityReviveFix can be used as a normal instant revive mod. It also includes a dedicated compatibility patch for [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/). Its main purpose is to fix conflict bugs that can happen when REPOFidelity is used together with other revive mods, reducing cases where players get stuck, fall into the void, or remain in the death camera after being revived.

### Multiplayer Install Guidance

- In normal multiplayer use, the host installing FidelityReviveFix is usually enough.
- The host performs the instant extraction revive and the game syncs it through the vanilla revive RPC.
- If a non-host player has [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) installed, that player should also install FidelityReviveFix to get the local compatibility protection.
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
