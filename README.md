# HellRevival

## 将死亡后的头放入撤离点自动复活

HellRevival 是一个撤离点复活 mod。主机安装后，玩家死亡后的头进入撤离点时，会通过游戏原版复活流程自动复活，并支持多人模式。

默认情况下：

- 没有 REPOFidelity 风险的玩家：进入撤离点后即时复活。
- 检测到 [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) 风险的玩家：短暂等待稳定状态后自动复活，用于兼容复活类 mod 冲突问题。
- 复活后的血量默认为 `20`，可在配置中修改。

多人游戏中，主机负责实际复活，并通过游戏原版 `ReviveRPC` 同步给其他玩家。HellRevival 不新增自定义复活 RPC。

### 多人安装建议

- 普通多人使用，且玩家没有安装 REPOFidelity 时，通常只需要主机安装 HellRevival。
- 如果非主机玩家自己安装了 REPOFidelity，建议也安装 HellRevival，用于向主机上报风险状态，并在本机收到复活同步后执行本地兼容保护。
- 如果装了 REPOFidelity 的客户端没有安装本 mod，主机仍可复活该玩家，但无法远程完全处理该客户端本机的相机或死亡视角状态。

### 主要配置

- `Enable Automatic Revive`：启用撤离点自动复活，默认开启。
- `Revive Health`：复活后的血量，默认 `20`。设为 `1` 时等同原版撤离点复活血量。

---

## Automatically Revive by Placing a Dead Player's Head in the Extraction Point

HellRevival is an extraction-point revive mod. When the host has it installed, a dead player's head entering the extraction point automatically revives that player through the game's vanilla revive flow, including in multiplayer.

By default:

- Players without REPOFidelity risk revive instantly after entering the extraction point.
- Players with detected [REPOFidelity](https://thunderstore.io/c/repo/p/Vippy/REPOFidelity/) risk wait briefly for a stable state before reviving, for compatibility with revive-mod conflict issues.
- Revive health defaults to `20` and can be changed in the config.

In multiplayer, the host performs the actual revive and syncs it to other players through the vanilla `ReviveRPC`. HellRevival does not add a custom revive RPC.

### Multiplayer Install Guidance

- For normal multiplayer use, when players do not use REPOFidelity, the host installing HellRevival is usually enough.
- If a non-host player has REPOFidelity installed, that player should also install HellRevival so they can report their risk state to the host and run local compatibility protection after receiving the revive sync.
- If a REPOFidelity client does not install this mod, the host can still revive that player, but cannot fully handle that client's local camera or death-view state remotely.

### Main Config

- `Enable Automatic Revive`: enables extraction-point automatic revive. Default: on.
- `Revive Health`: health after revive. Default: `20`. Set it to `1` to match the vanilla extraction-point revive health.
