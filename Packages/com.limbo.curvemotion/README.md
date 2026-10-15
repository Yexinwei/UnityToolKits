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

菜单 `Tools/Curve Motion/Editor`。

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
}
```

`typeId`（这里的 `myProp`）会写进 JSON，发布后不要改。

## 运行包内测试

在项目 `Packages/manifest.json` 中加入：

```json
"testables": ["com.limbo.curvemotion"]
```

然后在 Test Runner 的 EditMode 页签运行。
