# Curve Motion Editor

可视化曲线运动编辑器。在 Scene 视图里编辑路径，在时间轴窗口里叠加属性变化，导出：

- 结构化 JSON（附自然语言描述，供 AI 解析）
- DOTween C# 代码（由 JSON 生成）

本包不依赖 DOTween，生成的代码粘贴到装了 DOTween 的项目即可使用。

## 安装

Package Manager → Add package from git URL：

```
https://github.com/Yexinwei/UnityToolKits.git?path=Packages/com.limbo.curvemotion
```

要求 Unity 2022.3 及以上。

## 打开编辑器

菜单 `Tools/Curve Motion/Editor`，或在 Project 窗口双击 Motion Asset。

想直接试用：菜单 `Tools/Curve Motion/Open Test Scene`，会在 `Assets/CurveMotionTest/` 生成测试场景（UI 金币、UI 钱包、3D 爱心）和四个示例动效，并自动打开编辑器。其中 `demo-heart-to-wallet`（3D 爱心飞进 UI 钱包）和 `demo-coin-to-heart`（UI 金币落到 3D 爱心上）是 UI/3D 混合示例，双击资产即可预览。`Rebuild Test Scene` 把它们恢复为默认内容。

1. 点“新建”创建 Motion Asset（或拖入已有的）。
2. 把场景里的对象拖到“预览目标”（或选中对象后点“用选中”）。路径从它的当前位置开始绘制。
3. Scene 视图里编辑路径：点击选点，拖动坐标手柄移动；Smooth/Free 点有方形切线手柄；按住 Shift 在路径上点击插入点；Delete 删除选中点。打开“网格吸附”后，点和切线相对起点按网格取整。
4. 窗口中间是时间轴：
   - 标尺：点击或拖动定位播放头，目标对象同步显示该时刻的状态。
   - 路径行：每段一块，拖动段之间的竖线改前一段的时长（速度模式的段会反算速度），后面的点、片段、事件跟着移动。
   - 事件行：双击空白处加事件，拖动改时间。
   - 属性轨道：“+ 添加属性轨道”选择缩放、旋转、透明度、颜色等；双击空白处加片段，拖动片段移动，拖两端改时长。同一轨道后面的片段会覆盖前面的，被覆盖的部分显示为半透明。
   - 拖动时吸附到路径点、事件、其他片段边界和播放头，按住 Ctrl/⌘ 临时关闭。右键有插入、复制、删除等操作。
5. 时间轴下方显示选中项的参数：路径点与路径段（点类型、计时方式、缓动、描述），片段（锚点、偏移、时长、缓动、目标值、描述），事件（名称、日志内容、锚点、偏移），以及动效设置（名称、描述、播放次数与循环方式）。
6. 快捷键（窗口有焦点时）：空格播放/暂停，Delete 删除选中项，Ctrl/⌘+D 复制一份，Ctrl/⌘+C / V 复制粘贴片段或事件（粘贴到播放头处）。
7. 点“停止”、保存场景或进入 Play 模式时，对象自动还原。
8. “复制 JSON”可直接粘贴给 AI；“导出/导入 JSON”读写文件。

### UI 与 3D 混合（飞到另一个对象上）

把终点要对准的对象拖到“落点对象”，动效就变成“终点绑定”：路径终点自动对准它，对象移动时实时跟随。

- 预览目标和落点对象可以一个是 UI、一个是 3D 物体，例如 3D 物体飞到 UI 图标上、UI 图片飞到 3D 模型上。两者之间按屏幕位置换算，3D 物体保持起点离相机的距离；换算用的相机在“3D 相机”里指定，留空用 Main Camera。
- UI 和 3D 在 Scene 视图里不在一处，实际效果请在 Game 视图里看（工具栏“Game 视图”按钮），预览时实时刷新。
- 终点跟随时不能手动拖终点；要手动改，先清空落点对象，或在“动效设置”里关掉“终点绑定”。
- 预览目标、落点对象和相机按资产记在本机，下次打开资产自动填回。
- 运行时落点由代码传入，路径不需要重画：起点不动，整条路径按走过的弧长比例修正到实际终点（见下方代码示例与 JSON 里的 `conventions.endTarget`）。

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

## 运行包内测试

在项目 `Packages/manifest.json` 中加入：

```json
"testables": ["com.limbo.curvemotion"]
```

然后在 Test Runner 的 EditMode 页签运行。
