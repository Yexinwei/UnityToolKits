# Changelog

## [0.5.0] - 2026-10-15
- UI 与 3D 混合预览：窗口新增“落点对象”，路径终点自动对准它，可以是 UI 也可以是 3D 物体（3D 物体飞到 UI 上、UI 图片飞到 3D 模型上）。UI 与 3D 之间经屏幕坐标换算，3D 对象保持起点离相机的距离；跨 UI/3D 时可指定换算用的相机，默认 Main Camera。
- 终点跟随：落点对象或预览目标移动、Game 视图尺寸变化时，终点实时对准；跟随时终点不能手动拖，Scene 视图在终点画出落点标记。
- 预览时实时刷新 Game 视图，工具栏新增“Game 视图”按钮。
- 预览目标、落点对象和相机按资产记在本机，重新打开资产时自动填回。
- 数据：MotionData 新增 endTarget（bound、kind、hint），JSON 输出 endTarget 与 authoredEnd，并说明运行时的终点修正方法；MotionEvaluator 新增 GetPathProgress、EvaluatePosition(time, endCorrection)、AuthoredEndOffset；MotionEditing 新增 SetEndOffset。
- 测试场景新增两个混合示例：demo-heart-to-wallet（3D 爱心飞进 UI 钱包）、demo-coin-to-heart（UI 金币落到 3D 爱心上），双击即可预览。
- 循环新增“尾段自循环”：前面只播一次，之后只循环最后一段路径（例如飞到位后原地浮动）；时间轴标出循环的那一截。数据：LoopSettings.scope（Whole / LastSegment），MotionEvaluator.LoopStart / LoopLength，JSON 的 loop.scope 与 loop.start。
- 窗口分区：对象、时间轴、单段编辑（只改选中项）、全局编辑（整个动效）、提示与警告、导出，各自带色条标题；单段编辑的最后一段可一键设为自循环。
- 提示与警告单独成区，固定在窗口下方，按错误 / 警告 / 提示分类显示。
- 测试场景素材：新增钱包、宝石、星星图标和奖励面板、资源栏九宫格底图（tools/art/gen_ui_art.py 生成），替换原来的纯色钱包；旧测试场景会提示重建。

## [0.4.0] - 2026-10-15
- 时间轴：标尺与播放头（点击/拖动定位）、路径段条（拖分界改段时长，速度模式反算速度）、事件行、每条属性轨道一行。
- 属性轨道：添加、启用/停用、改名、排序、删除；片段双击添加、拖动移动、拖两端改时长，被覆盖部分半透明显示；移动后自动重新锚定到它前面最近的路径点。
- 检查器：片段（锚点、偏移、时长、缓动、目标值、描述）、事件、轨道、动效设置（名称、描述、播放次数、Restart/Yoyo）。
- 吸附：时间轴吸附到路径点、事件、片段边界和播放头（Ctrl/⌘ 临时关闭）；Scene 视图路径点与切线可按网格吸附。
- 快捷键：空格播放、Delete 删除、Ctrl/⌘+D 复制一份、Ctrl/⌘+C/V 复制粘贴片段和事件。
- Scene 视图显示事件触发位置，选中片段时高亮它对应的那段路径。
- MotionEditing 新增 AnchorAt、SetClipStart、SetEventTime、SetSegmentDuration、InsertPointAtTime 等；新增 TimeSnapping；MotionEvaluator 新增 TryGetClipRange。

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
