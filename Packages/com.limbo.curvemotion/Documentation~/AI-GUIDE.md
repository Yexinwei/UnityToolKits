# Curve Motion · AI 接入规则

给 AI 编码助手（Claude Code、Cursor 等）看的规则。用户会给你一段动效 JSON（由 Curve Motion 编辑器导出），让你把它放进 Unity 工程、接到指定的地方。照下面做。

> 本文件旁边（Unity 工程根目录的 `docs/`）如果有 `CurveMotion-AI-Deploy.md`，说明还没部署或刚升级：先按它做完部署，再继续用户的任务。

## 0. 原则

- **C# 动效代码不要自己写。** 把 JSON 存成 `<名字>.motion.json` 放进 `Assets/`，Unity 刷新时 Curve Motion 包会自动在同目录生成代码，结果和编辑器预览完全一致。你负责：放 JSON、写调用代码、填事件方法。
- **生成的 `<类名>.cs` 不要手改**，每次 JSON 变化都会被覆盖。要改动效就改 JSON（见第 6 节）。
- `<类名>.Events.cs` 是给人填的，可以改方法体，**不要改方法签名、不要删方法**。
- 用户要求移植到别的引擎/平台、或不用 DOTween 时，才按 JSON 里 `conventions` 的说明自己实现插值。

## 1. 流程

1. **放 JSON**：存到 `Assets/<用户指定的目录>/<名字>.motion.json`。没指定目录时，放在调用它的脚本旁边。`<名字>` 用小写短横线，例如 `coin-fly.motion.json`。
2. **定类名和命名空间**（可选）：在 JSON 根对象里加
   ```json
   "export": { "namespace": "Game.Fx", "className": "CoinFlyMotion" }
   ```
   - 不写 `className`：类名由文件名生成，`coin-fly.motion.json` → `CoinFlyMotion`（按 `-`、`_`、空格分词，首字母大写，末尾加 `Motion`）。
   - 不写 `namespace`：在全局命名空间。项目代码有统一命名空间时，写成和调用方一致的。
3. **等生成**：用户切回 Unity（或 Unity 自动刷新）后，同目录出现：
   - `<类名>.cs`：动效代码，`public static (partial) class <类名>`。
   - `<类名>.Events.cs`：动效有事件时才有，第一次生成后不会被覆盖。
   你不能触发 Unity 刷新时，告诉用户“切回 Unity 等编译完”。生成失败时 Console 有 `[Curve Motion]` 开头的错误，常见原因：JSON 格式错、工程没装 DOTween、已有同名类。
4. **接入**：在用户说的地方调用 `<类名>.Play(...)`，见第 3、4 节。
5. **填事件**：见第 5 节。
6. **检查**：Unity Console 没有编译错误；生成的 `<类名>.cs` 文件头里如果有“导出警告”，读一下是否影响用户要的效果。

## 2. 前提

- 工程装了 DOTween（免费版即可）。没装时不会生成代码。
- UI 动效里有透明度或颜色变化时用到 `UnityEngine.UI`（UGUI）。
- JSON 放在带 `.asmdef` 的目录时，这个 asmdef 要能引用 DOTween（DOTween 默认在 `Assets/Plugins/Demigiant`，没有 asmdef，属于 Assembly-CSharp；放进有 asmdef 的目录会编译不过，除非用户给 DOTween 建过 asmdef 并引用了它）。拿不准就放在没有 asmdef 的目录。

## 3. 生成的 API

看 JSON 的 `target` 和 `endTarget.bound` 决定签名：

```csharp
// target = "RectTransform"（UI，位置用 anchoredPosition，单位像素）
public static Sequence Play(RectTransform target);
// target = "Transform"（2D/3D 物体，位置用 localPosition）
public static Sequence Play(Transform target);

// endTarget.bound = true 时多两个参数：
public static Sequence Play(RectTransform target, Transform endTarget = null, Camera worldCamera = null);
//   endTarget：落点对象，路径终点会对准它（UI 和 3D 互相飞也可以），为空时按设计时的终点。
//   worldCamera：UI 与 3D 之间换算屏幕位置用的相机，为空时用 Camera.main。只有一边是 UI、一边是 3D 时才需要。

// 有事件（events 非空）时最后多一个：
//   Action<string> onEvent = null   事件触发时调用，参数是 JSON 里事件的 name。

public const float Duration;   // 单次播放时长（秒），不含循环
```

行为：

- **起点 = 调用 `Play` 时对象的当前位置**；缩放、旋转、透明度、颜色也以调用时的值为初始值。所以先把对象摆到起点（实例化、设 `anchoredPosition` / `position`），再调用 `Play`。
- 路径里的位置都是相对起点的偏移，和对象在哪无关。
- 返回的 `Sequence` 已经开始播放。`SetTarget(target)`、`SetLink(target.gameObject)` 已设好：对象销毁时自动停止，可以用 `target.DOKill()` 停止。
- 结束回调用返回值：`.OnComplete(...)`，协程里 `yield return seq.WaitForCompletion();`。
- 循环：`loop.count = -1` 时无限循环，`OnComplete` 不会触发；`loop.scope = "LastSegment"` 且无限循环时，返回的 Sequence 只含循环之前的部分、播完不自动销毁，Kill 它时循环部分一起停。
- 不要把返回的 Sequence 再 `Append/Insert` 进别的 Sequence（它已经在播放）。要错开播放多个，用 `DOVirtual.DelayedCall(delay, () => XMotion.Play(obj))`。
- 透明度轨道按 JSON 里轨道的 `settings.source` 找组件：`Auto` 时 UI 先找 `CanvasGroup` 再找 `Graphic`，场景物体找 `SpriteRenderer`。对象上要有对应组件，否则透明度不变。

## 4. 接入示例

UI 金币飞到钱包（`endTarget.bound = true`，有事件 `OnCoinArrive`）：

```csharp
using DG.Tweening;
using UnityEngine;

public class RewardCoinFly : MonoBehaviour
{
    [SerializeField] RectTransform coinPrefab;
    [SerializeField] RectTransform walletIcon;

    public void Fly(RectTransform from, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var coin = Instantiate(coinPrefab, coinPrefab.parent);
            coin.position = from.position;                      // 先摆到起点
            DOVirtual.DelayedCall(i * 0.06f, () =>
                CoinFlyMotion.Play(coin, walletIcon)            // 落点对准钱包
                    .OnComplete(() => Destroy(coin.gameObject)));
        }
    }
}
```

3D 物体原地弹跳（不绑定落点，无事件）：

```csharp
HeartHopMotion.Play(heart.transform);
```

要在调用处处理事件（依赖当前场景对象时）：

```csharp
CoinFlyMotion.Play(coin, walletIcon, onEvent: name =>
{
    if (name == "OnCoinArrive") wallet.AddCoins(1);
});
```

接入时：

- 用户说“接在 X 的 Y 时机”时，找到 X 对应的脚本和方法，在那里调用；需要的对象引用加成 `[SerializeField]` 字段，并告诉用户去 Inspector 里拖。
- 不要改动效参数来“凑效果”，参数不对就改 JSON 或告诉用户回编辑器调。

## 5. 事件

- JSON 里每个事件（`events[]`）对应 `<类名>.Events.cs` 里的一个方法 `static void OnXxx(Transform target)`：事件名按非字母数字分词、首字母大写，前面加 `On`（已经是 `OnXxx` 的不再加）。例：`coin land` → `OnCoinLand`，`OnCoinArrive` → `OnCoinArrive`。
- 方法注释里有触发时间和“特效说明”（`message`，特效师写给开发的话）。按特效说明实现：播特效、音效、震屏等。
- 方法里默认有一行橙色的 `Debug.LogWarning(...)`（“动效事件未接入”），**实现后删掉这一行**。
- 方法是静态的，只拿得到 `target`。和调用处无关的效果（在 target 位置播特效、播音效）写在这里；依赖调用处对象的逻辑（给哪个钱包加数）用 `Play` 的 `onEvent` 参数在调用处写，同时把 OnXxx 里的警告换成一句注释说明在调用处处理。
- 事件改名或删除后，旧方法会留在 Events.cs 里（不报错，只是没人调用），生成时 Console 会提示。把里面的代码挪到新方法后删掉旧方法。

## 6. 修改动效（改 JSON）

用户让你改动效时，改 JSON 再保存，代码会自动重新生成。只改下面这些**输入字段**；其余字段（`time`、`start`、`end`、`length`、`duration`（根节点）、`loop.start`、`endTarget.authoredEnd`、`summary`、`conventions`、轨道的 `mode` / `components`、`descSource = "auto"` 的 `desc`）是计算结果，读取时忽略，不用改也不用保持一致。

单位：UI 位置是像素，场景物体是 Unity 单位；时间是秒；角度是度。

| 位置 | 字段 | 说明 |
|---|---|---|
| 根 | `schemaVersion` | 保持原值（当前 1），必填 |
| 根 | `name` / `description` | 动效名、整段说明 |
| 根 | `target` | `RectTransform`（UI）或 `Transform` |
| 根 | `export` | 见第 1 节，可选 |
| `loop` | `count` | 播放次数，1 = 不循环，-1 = 无限 |
| `loop` | `type` | `Restart` / `Yoyo`（隔次倒放） |
| `loop` | `scope` | `Whole` 整段循环；`LastSegment` 前面播一次后只循环最后一段路径 |
| `endTarget` | `bound` / `kind` / `hint` | 是否运行时对准落点；落点类型 `RectTransform` / `Transform`；落点说明 |
| `path.points[]` | `id` | 点的唯一 id，片段和事件靠它锚定。新增点要起新 id，改 id 要同步改引用它的 `anchor` |
| `path.points[]` | `position` | `[x, y, z]`。第一个点是起点，其余点按相对它的偏移计算（保持第一个点 `[0, 0, 0]` 最清楚）；UI 的 z 用 0 |
| `path.points[]` | `type` | `Auto` 自动平滑（忽略切线）/ `Corner` 尖角 / `Smooth` 只用 `outTangent`，入切线自动取反 / `Free` 两个切线分别用 |
| `path.points[]` | `inTangent` / `outTangent` | 贝塞尔控制柄，相对该点的偏移，只对 `Smooth` / `Free` 生效 |
| `path.segments[i]` | — | 第 i 段 = 第 i 个点到第 i+1 个点；数量 = 点数 − 1，按顺序对应，`from` / `to` 只是参考 |
| `path.segments[i]` | `timing` | `Duration` 用 `duration`；`Speed` 用 `speed`（每秒走多少），时长 = 段长 / 速度 |
| `path.segments[i]` | `duration` / `speed` | 见上 |
| `path.segments[i]` | `ease` | 见下方缓动 |
| `path.segments[i]` | `desc` | 一句话描述；手写时把 `descSource` 设为 `"manual"`，否则删掉 `desc` 和 `descSource` 让它自动生成 |
| `tracks[]` | `type` | `scale` / `rotation` / `alpha` / `color` |
| `tracks[]` | `name` / `enabled` | 显示名；是否生效 |
| `tracks[]` | `settings` | 轨道设置。`alpha` 的 `source`：`Auto` / `CanvasGroup` / `Graphic` / `SpriteRenderer` |
| `tracks[].clips[]` | `anchor` / `offset` | 开始时间 = 锚点（路径点 id）的到达时间 + offset（可为负） |
| `tracks[].clips[]` | `duration` / `ease` | 时长；缓动。0 时长 = 瞬间变化 |
| `tracks[].clips[]` | `value` | scale：`[x, y, z]` 初始缩放的倍数；rotation：`[x, y, z]` 在初始角度上加的度数；alpha：`[a]` 绝对值 0~1；color：`[r, g, b]` 绝对值 0~1（透明度用 alpha 轨道） |
| `tracks[].clips[]` | `desc` | 同路径段 |
| `events[]` | `name` | 事件名，决定方法名（见第 5 节） |
| `events[]` | `message` | 特效说明 |
| `events[]` | `anchor` / `offset` | 同片段 |

规则：

- 同一条轨道的片段可以重叠，开始得晚的覆盖开始得早的（早的在晚的开始处截断）。每个片段从“当时的值”补间到目标值。
- 缓动 `ease`：`{ "type": "OutQuad" }`，名字和 DOTween 的 `Ease` 一致：`Linear`，`In/Out/InOut` + `Sine`、`Quad`、`Cubic`、`Quart`、`Quint`、`Expo`、`Circ`、`Elastic`、`Back`、`Bounce`。自定义曲线：`{ "type": "Custom", "keys": [[time, value, inTangent, outTangent], ...] }`，time 和 value 在 0~1。
- 常见改法：整体变快 → 各段 `duration` 乘系数（`Speed` 段改 `speed`），片段 `duration`、`offset` 同比例缩放；弹得更高 → 改中间点的 `position` y；落地回弹 → 给最后一段 `OutBack` / `OutBounce`，或加一个 scale 片段；加淡出 → 加 `alpha` 轨道，片段锚到最后一个点、`offset` 为负。
- 改完后在回复里简要说明改了什么，提醒用户可以在 Curve Motion 编辑器里“导入 JSON…”到一个 Motion Asset 上预览。

## 7. 不要做的事

- 不要手改 `<类名>.cs`，不要从 JSON 手写 DOTween 插值代替它。
- 不要在工程里另外保存一份同名类（例如编辑器“导出 C#…”出来的同名 .cs 放在别的目录），会重复定义；生成器发现同名脚本时会拒绝生成。
- 不要删 Events.cs 里的方法或改签名；不要给生成的类再写一个同名 partial 文件。
- 不要改 `schemaVersion`。
