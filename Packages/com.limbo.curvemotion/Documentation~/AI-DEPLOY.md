# Curve Motion · AI 规则部署

给 AI 编码助手看的一次性任务。用户在 Unity 里点了 `Tools/Curve Motion/Deploy AI Integration`，工程根目录（`Assets` 的上一级）多了两个文件：

- `CurveMotion-AI-Guide.md`：动效接入规则，长期保留。
- `CurveMotion-AI-Deploy.md`：就是本文件，部署完删掉。

你的任务：把规则接进这个项目的 AI 指令文件，让以后的会话一开始就知道有这份规则。按下面做，做完向用户汇报。

## 1. 找到要改的指令文件

1. 确定两个目录：**Unity 工程根目录**（本文件所在目录）和 **git 仓库根目录**（`git rev-parse --show-toplevel`；不是 git 仓库时就等于 Unity 工程根目录）。两者可能不同，例如仓库根下有一层 Unity 工程目录。
2. 在 git 仓库根目录和 Unity 工程根目录里找这些文件（存在哪个改哪个，都要改）：
   - `CLAUDE.md`（Claude Code）
   - `AGENTS.md`（Codex 等）
   - `GEMINI.md`（Gemini CLI）
   - `.cursorrules`、`.cursor/rules/*.mdc`（Cursor）
   - `.github/copilot-instructions.md`（GitHub Copilot）
   - `.windsurfrules`（Windsurf）
3. 一个都没有时，给你自己所属的工具在 **git 仓库根目录** 新建一个：Claude Code 建 `CLAUDE.md`；Cursor 建 `.cursor/rules/curve-motion.mdc`（见第 3 步的格式）；其他工具建 `AGENTS.md`。

## 2. 写入的内容

在每个要改的文件**末尾**追加下面这段（`<规则路径>` 换成从该指令文件所在目录到 `CurveMotion-AI-Guide.md` 的相对路径，例如同目录就是 `CurveMotion-AI-Guide.md`，Unity 工程在子目录 `Game/` 时是 `Game/CurveMotion-AI-Guide.md`）：

```markdown
<!-- curve-motion:begin -->
## Curve Motion 动效

涉及 Curve Motion 动效（`*.motion.json`、`*Motion.cs`、`*Motion.Events.cs`，或用户给出 Curve Motion 编辑器导出的 JSON、要求接入/修改动效）时，先完整读 `<规则路径>`，按里面的规则做。要点：
- 动效代码由包根据 `Assets/` 下的 `<名字>.motion.json` 自动生成，不要手写或手改生成的 `<类名>.cs`；要改动效就改 JSON。
- 你负责放 JSON、在用户指定的地方调用 `<类名>.Play(...)`、在 `<类名>.Events.cs` 里填事件方法。
<!-- curve-motion:end -->
```

- 文件里已经有 `<!-- curve-motion:begin -->` 到 `<!-- curve-motion:end -->` 这段时，**整段替换**，不要重复追加。
- 不改文件里的其他内容。

## 3. Cursor 的 .mdc 格式

新建 `.cursor/rules/curve-motion.mdc` 时内容为：

```markdown
---
description: Curve Motion 动效接入规则（*.motion.json、生成的 Motion 类、事件文件）
globs: ["**/*.motion.json", "**/*Motion.cs", "**/*Motion.Events.cs"]
alwaysApply: false
---
```

后面接第 2 步的整段内容。已有其他 `.mdc` 时，不往它们里面加，单独建这个文件。

## 4. 收尾

1. 确认 `CurveMotion-AI-Guide.md` 没有被 `.gitignore` 忽略（团队成员和其他 AI 会话要能读到）。被忽略的话告诉用户，不要自行改 `.gitignore`。
2. 删除本文件 `CurveMotion-AI-Deploy.md`。
3. 向用户汇报：改了或新建了哪些文件（路径）、写入的规则路径，并提醒把这些改动和 `CurveMotion-AI-Guide.md` 一起提交到版本库。
4. 不要提交、不要推送，除非用户要求。

以后 Curve Motion 包升级后，用户会再点一次部署菜单：`CurveMotion-AI-Guide.md` 会被更新，本文件会重新出现，照样执行即可（已有的段落整段替换）。
