# Changelog

## [0.3.0] - 2026-10-15
- 编辑器窗口：选择/新建 Motion Asset，指定场景里的预览目标，路径点列表与选中点、路径段的详细参数编辑。
- Scene 视图路径编辑：点击选点、拖动移动，Smooth/Free 点的切线手柄，Shift+点击路径插入点，Delete 删除点，全部支持 Undo。
- 预览：在目标对象上播放/暂停/拖动时间，包含路径与属性轨道；停止、保存场景、进入 Play 模式时自动还原对象。
- 复制、导出、导入 JSON。
- 测试场景：菜单 Tools/Curve Motion/Open Test Scene 一键生成 UI 金币与 3D 爱心两个目标，并附两个示例动效（金币飞向钱包、爱心弹跳）。金币与爱心素材在 Editor/TestScene/Art/，仅供测试场景使用；包升级后旧测试场景会提示重建。
- MotionEditing：插入/删除点时自动拆分或合并段时长，锚定在被删点上的片段与事件保持原绝对时间。
- 轨道新增 TryReadValue/WriteValue，自定义轨道重写即可支持预览。

## [0.2.0] - 2026-10-15
- 数据模型：MotionData / MotionAsset，路径点（Corner、Smooth、Free、Auto）、分段计时（时长或速度）、属性轨道、事件点、循环（Restart、Yoyo）。
- 属性轨道扩展点：继承 MotionTrack 并加 [MotionTrack("id", "名称")] 即可新增属性；内置 scale、rotation、alpha、color。
- 求值器 MotionEvaluator：路径按弧长匀速、片段锚定路径点、同轨道后者覆盖前者、循环时间换算。
- 缓动：与 DOTween 默认参数一致的全部内置 Ease，外加自定义 AnimationCurve。
- JSON 读写（零依赖），附计算字段与语义说明，供 AI 解析；示例见 Documentation~/sample-motion.json。

## [0.1.0] - 2026-10-15
- 包骨架：Runtime / Editor / Tests 程序集，空编辑器窗口。
