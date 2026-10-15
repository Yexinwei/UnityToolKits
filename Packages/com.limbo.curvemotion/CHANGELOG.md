# Changelog

## [0.2.0] - 2026-10-15
- 数据模型：MotionData / MotionAsset，路径点（Corner、Smooth、Free、Auto）、分段计时（时长或速度）、属性轨道、事件点、循环（Restart、Yoyo）。
- 属性轨道扩展点：继承 MotionTrack 并加 [MotionTrack("id", "名称")] 即可新增属性；内置 scale、rotation、alpha、color。
- 求值器 MotionEvaluator：路径按弧长匀速、片段锚定路径点、同轨道后者覆盖前者、循环时间换算。
- 缓动：与 DOTween 默认参数一致的全部内置 Ease，外加自定义 AnimationCurve。
- JSON 读写（零依赖），附计算字段与语义说明，供 AI 解析；示例见 Documentation~/sample-motion.json。

## [0.1.0] - 2026-10-15
- 包骨架：Runtime / Editor / Tests 程序集，空编辑器窗口。
