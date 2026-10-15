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

想直接试用：菜单 `Tools/Curve Motion/Open Test Scene`，会在 `Assets/CurveMotionTest/` 生成测试场景（UI 金币、3D 方块）和两个示例动效，并自动打开编辑器。`Rebuild Test Scene` 把它们恢复为默认内容。

1. 点“新建”创建 Motion Asset（或拖入已有的）。
2. 把场景里的对象拖到“预览目标”（或选中对象后点“用选中”）。路径从它的当前位置开始绘制。
3. Scene 视图里编辑路径：点击选点，拖动坐标手柄移动；Smooth/Free 点有方形切线手柄；按住 Shift 在路径上点击插入点；Delete 删除选中点。
4. 窗口里改点类型、每段的计时方式（时长或速度）、缓动和描述。
5. 点“播放”在目标上预览，拖动“时间”滑条逐帧查看。点“停止”、保存场景或进入 Play 模式时，对象自动还原。
6. “复制 JSON”可直接粘贴给 AI；“导出/导入 JSON”读写文件。

## 代码里使用

```csharp
using Limbo.CurveMotion;
using Limbo.CurveMotion.Serialization;

var data = MotionJson.FromJson(jsonText);
var eval = new MotionEvaluator(data);
Vector3 offset = eval.EvaluatePosition(eval.ToLocalTime(t));   // 相对起点的位置偏移
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
