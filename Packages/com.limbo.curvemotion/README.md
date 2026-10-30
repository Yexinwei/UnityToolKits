# Curve Motion Editor

可视化曲线运动编辑器。在 Scene 视图里编辑路径，在时间轴窗口里叠加属性变化，导出：

- 结构化 JSON（附自然语言描述，供 AI 解析）
- DOTween C# 代码（由 JSON 生成）

本包不依赖 DOTween，生成的代码放进装了 DOTween 的项目即可使用。

## 安装

Package Manager → Add package from git URL：

```
https://github.com/Yexinwei/UnityToolKits.git?path=Packages/com.limbo.curvemotion
```

要求 Unity 2022.3 及以上。

## 打开编辑器

菜单 `Tools/Curve Motion/Editor`，或在 Project 窗口双击 Motion Asset。

想直接试用：菜单 `Tools/Curve Motion/Open Test Scene`，会在 `Assets/CurveMotionTest/` 生成测试场景（奖励面板上的 UI 金币、宝石与钱包资源栏、3D 爱心）和四个示例动效，并自动打开编辑器。其中 `demo-heart-to-wallet`（3D 爱心飞进 UI 钱包）和 `demo-coin-to-heart`（UI 金币落到 3D 爱心上）是 UI/3D 混合示例，双击资产即可预览。`Rebuild Test Scene` 把它们恢复为默认内容。

1. 点“新建”创建 Motion Asset（或拖入已有的）。
2. 把场景里的对象拖到“预览目标”（或选中对象后点“用选中”），再打开工具栏的“预览”。预览开着时 Scene 视图才显示路径（从目标的当前位置开始），动效按播放头作用在目标上；关掉预览隐藏路径、对象还原。拖播放头、点“播放”或打开“编辑路径”会自动打开预览。预览目标不在当前打开的场景/预制体里（例如进了别的预制体）时路径自动隐藏、对象还原，回去后接着预览。
3. Scene 视图里编辑路径：点击选点，拖动坐标手柄移动；Smooth/Free 点有方形切线手柄；按住 Shift 在路径上点击插入点；Delete 删除选中点。打开“网格吸附”后，点和切线相对起点按网格取整。
4. 窗口中间是时间轴：
   - 标尺：点击或拖动定位播放头，目标对象同步显示该时刻的状态。
   - 路径行：每段一块，拖动段之间的竖线改前一段的时长（速度模式的段会反算速度），后面的点、片段、事件跟着移动。
   - 事件行：双击空白处加事件，拖动改时间。
   - 属性轨道：“+ 添加属性轨道”选择缩放、旋转、透明度、颜色等；双击空白处加片段，拖动片段移动，拖两端改时长。同一轨道后面的片段会覆盖前面的，被覆盖的部分显示为半透明。选中轨道可改它的设置，例如透明度的“作用组件”：自动（有 CanvasGroup 用 CanvasGroup，否则 Graphic，再否则 SpriteRenderer）、CanvasGroup（连同子节点一起淡）、Graphic（只改这一张 Image / Text）、SpriteRenderer。预览和导出代码都按这个设置走。
   - 拖动时吸附到路径点、事件、其他片段边界和播放头，按住 Ctrl/⌘ 临时关闭。右键有插入、复制、删除等操作。
5. 时间轴下方分两块：
   - **单段编辑**（黄色）：只改选中的那一项。路径点与路径段（点类型、计时方式、缓动、描述），片段（锚点、偏移、时长、缓动、目标值、描述），事件（名称、特效说明、锚点、偏移）。
   - **全局编辑**（蓝色）：作用于整个动效。名称、描述、循环、终点绑定。循环范围可选“整段循环”或“尾段自循环”（前面只播一次，之后只循环最后一段路径，例如飞到位后原地浮动），时间轴上会标出循环的那一截。
   - 再往下是**提示与警告**（按错误 / 警告 / 提示分类列出）、**导出**（JSON、DOTween 代码、Play 模式试播三组）和**说明**（按模块折叠的详细用法）。
   - 整个窗口一起上下滚动，各分区按内容实际高度排开，不互相挤压。
6. 快捷键（窗口有焦点时）：空格播放/暂停，Delete 删除选中项，Ctrl/⌘+D 复制一份，Ctrl/⌘+C / V 复制粘贴片段或事件（粘贴到播放头处）。
7. 关掉“预览”或进入 Play 模式时对象还原；保存场景/预制体前也会先还原再保存，保存完接着预览。“停止”只是停止播放、回到 0 秒。
8. “复制 JSON”可直接粘贴给 AI；“导出/导入 JSON”读写文件。描述框留空时自动生成描述（框下方小字），JSON 和导出代码的注释都用它。
9. “复制 DOTween 代码”/“导出 C#…”生成可直接放进工程的代码，见下方“导出 DOTween 代码”。

### UI 与 3D 混合（飞到另一个对象上）

把终点要对准的对象拖到“落点对象”，动效就变成“终点绑定”：路径终点自动对准它，对象移动时实时跟随。

- 预览目标和落点对象可以一个是 UI、一个是 3D 物体，例如 3D 物体飞到 UI 图标上、UI 图片飞到 3D 模型上。两者之间按屏幕位置换算，3D 物体保持起点离相机的距离；换算用的相机在“3D 相机”里指定，留空用 Main Camera。
- UI 和 3D 在 Scene 视图里不在一处，实际效果请在 Game 视图里看（工具栏“Game 视图”按钮），预览时实时刷新。
- 终点跟随时不能手动拖终点；要手动改，先清空落点对象，或在“动效设置”里关掉“终点绑定”。
- 预览目标、落点对象和相机记在资产里，见下方“上次编辑的场景”。
- 运行时落点由代码传入，路径不需要重画：起点不动，整条路径按走过的弧长比例修正到实际终点（见下方代码示例与 JSON 里的 `conventions.endTarget`）。

### 上次编辑的场景

资产会记住上次在哪个场景或预制体里编辑，以及预览目标、落点对象和相机（存在资产文件里，跟着版本库走，不进 JSON）。

- 双击或拖入资产时，那个场景/预制体没打开就自动回去：场景直接打开（当前场景有未保存修改会先问），预制体进入预制体模式，然后填回这几个对象。
- 窗口“对象”区的“上次编辑”一行显示记录的位置，“打开”手动回去，“自动”关掉自动回去（本机设置）。
- 对象按 GlobalObjectId 找，找不到再按层级路径找（预制体模式里只按路径），所以场景对象改名、挪层级也能找回；找不到时“提示与警告”会说明。
- 未保存的新场景里的对象没法记，先保存场景。

## 导出 DOTween 代码

窗口“导出”区：

- **复制 DOTween 代码**：生成的 C# 复制到剪贴板。
- **导出 C#…**：保存成 `.cs`，类名跟文件名一致（默认由动效名生成，如 `demo-coin-fly` → `DemoCoinFlyMotion`）；存到 Assets 下会自动导入。
- **代码命名空间**：留空放在全局命名空间，整个工程共用这个设置。
- **Play 模式试播**（导出区的“试播导出代码”）：工程装了 DOTween、代码导出到 Assets 并编译完后，进入 Play 模式点它，会在预览目标（和落点对象）上真正跑一遍导出的代码，方便和编辑器预览对比；再点一次从头重播。

生成的是一个静态类：

```csharp
// 以对象当前位置为起点、当前缩放/旋转/透明度/颜色为初始值播放，返回已开始播放的 Sequence
var seq = DemoCoinFlyMotion.Play(coinRectTransform);

// 终点绑定的动效多两个参数：落点对象（可以是 UI 或 3D 物体）和 UI/3D 换算用的相机（默认 Camera.main）
DemoCoinFlyMotion.Play(coinRectTransform, walletIcon, Camera.main);

// 有事件的动效：事件要做的事写在导出时一起生成的 DemoCoinFlyMotion.Events.cs 里（见下）

// 停止：target.DOKill() 或 seq.Kill()；对象销毁时自动停止
```

- **事件**：每个事件名对应一个方法 `static void OnXxx(Transform target)`（如事件 `coin land` → `OnCoinLand`），导出时生成在旁边的 `类名.Events.cs` 里，开发只需填方法体（播特效、音效、加分……）。方法注释写着触发时间和特效师填的“特效说明”；没接入前方法里是一条橙色加粗的 `Debug.LogWarning`（带特效说明，点日志能定位到对象），接入后删掉即可。这个文件只在第一次导出时生成，之后重新导出只追加新事件的方法，不覆盖已写的代码；事件改名或删除留下的旧方法不会报错，导出时会提示。导出类是 `partial` 的，事件文件要和它在同一个程序集、同一个命名空间。“复制 DOTween 代码”时事件方法直接接在代码后面。`Play` 仍有可选的 `onEvent` 参数（参数是事件名），需要在外部额外监听时用。
- 只依赖 UnityEngine 和 DOTween（UI 的透明度、颜色另需 UGUI）。DOTween 放在 Assets 里（默认的 Plugins/Demigiant）时直接可用；导出到带 asmdef 的目录时，asmdef 要引用 DOTween。
- 播放结果与编辑器预览一致：路径每段一个 `DOVirtual.Float`，位置用与预览相同的贝塞尔 + 弧长算法；属性片段用 `DOScale` / `DOTween.To` 插入 Sequence，被同轨道后一个片段覆盖的部分截掉；循环、尾段自循环、事件、终点绑定都照预览的规则生成。
- 无限的尾段自循环由两个 Sequence 组成，返回的是前半段（播完不自动销毁），Kill 它时循环部分一起停。
- 终点绑定时路径走完后落点对象再移动，代码不再跟随。
- 自定义属性轨道要导出代码，需要再写一个代码生成器（见下方“新增属性轨道”），没有的会跳过并在提示与警告里说明。

## 代码里使用

```csharp
using Limbo.CurveMotion;
using Limbo.CurveMotion.Serialization;

var data = MotionJson.FromJson(jsonText);
var eval = new MotionEvaluator(data);
Vector3 offset = eval.EvaluatePosition(eval.ToLocalTime(t));   // 相对起点的位置偏移

// 终点绑定（data.endTarget.bound）：actualEnd 是落点对象换算到本对象父节点空间、相对起点的偏移
Vector3 corrected = eval.EvaluatePosition(eval.ToLocalTime(t), actualEnd - eval.AuthoredEndOffset);
```

JSON 格式示例：`Documentation~/sample-motion.json`。

## 新增属性轨道

```csharp
[System.Serializable, MotionTrack("myProp", "My Prop")]
public class MyPropTrack : MotionTrack
{
    public override int Dimension => 1;
    public override ValueMode Mode => ValueMode.Absolute;
    public override Vector4 DefaultValue => Vector4.zero;

    // 可选：实现这两个方法后编辑器预览就能驱动该属性。
    public override bool TryReadValue(GameObject target, out Vector4 value) { ... }
    public override void WriteValue(GameObject target, Vector4 value) { ... }
}
```

`typeId`（这里的 `myProp`）会写进 JSON，发布后不要改。

轨道需要可调的选项时，加一个带 `[TrackSetting]` 的字段（枚举、bool、int、float、string），选中轨道时窗口里会显示，JSON 写在轨道的 `settings` 里，代码生成器里按 `track` 的字段值生成不同代码：

```csharp
[TrackSetting("作用组件", "鼠标悬停时的说明")]
public MySource source = MySource.Auto;
```

要导出 DOTween 代码，在 Editor 程序集里加一个代码生成器，说明怎么读写这个属性；片段时间、截断、循环由导出器统一处理：

```csharp
using Limbo.CurveMotion.Editor.Export;

[TrackCodeGen(typeof(MyPropTrack))]
public class MyPropCodeGen : TrackCodeGen
{
    public override TrackBinding Bind(DotweenCodeContext ctx, MotionTrack track)
    {
        string comp = ctx.Declare("myComp", ctx.Target + ".GetComponent<MyComp>()");
        return new TrackBinding
        {
            Read = comp + ".value",                       // 当前值
            Write = v => comp + ".value = " + v,          // 写回
            Initial = () => ctx.Declare("myProp0", comp + ".value"),   // 初始值（按倍率、增量算目标时用）
        };
    }
}
```

## 运行包内测试

在项目 `Packages/manifest.json` 中加入：

```json
"testables": ["com.limbo.curvemotion"]
```

然后在 Test Runner 的 EditMode 页签运行。
