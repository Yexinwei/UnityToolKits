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

## 运行包内测试

在项目 `Packages/manifest.json` 中加入：

```json
"testables": ["com.limbo.curvemotion"]
```

然后在 Test Runner 的 EditMode 页签运行。
