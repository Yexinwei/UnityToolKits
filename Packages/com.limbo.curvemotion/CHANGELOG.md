# Changelog

## [0.6.2] - 2026-10-30
- 时间轴选中最后一段时，单段编辑里多一行“终点位置”，可以直接改整条路径的终点（终点跟随落点对象时只读）；下方“选中终点”按钮切到终点本身，改点类型和切线。之前时间轴上点段只能选中段的起点，终点只能去 Scene 视图里点。

## [0.6.1] - 2026-10-24
- 事件接入简化：导出类改为 `partial`，每个事件名对应一个 `static void OnXxx(Transform target)`，生成在导出文件旁边的 `类名.Events.cs` 里，开发只填方法体，不用再在 `onEvent` 回调里按事件名分支。事件文件只在第一次导出时整份生成，之后重新导出只追加新事件的方法，不覆盖已写代码；改名或删除事件留下的旧方法、命名空间对不上时导出会提示。“复制 DOTween 代码”时事件方法接在代码后面。`onEvent` 参数保留为可选的额外通知。
- 事件日志加重：未接入的事件每次触发打一条橙色加粗的 `Debug.LogWarning`（【动效事件未接入】类名 · 事件名（时间）+ 特效说明 + 该在哪个方法里实现），点日志能定位到播放的对象。
- 窗口事件面板：“日志内容”改为醒目的多行“特效说明（写给开发）”，并显示将生成的方法名。

## [0.6.0] - 2026-10-24
- 导出 DOTween 代码：窗口“导出”区新增“复制 DOTween 代码”和“导出 C#…”。生成一个静态类，`Play(target)` 以对象当前状态为起点和初始值播放，返回 Sequence。路径每段一个 DOVirtual.Float，位置与预览同一算法（贝塞尔 + 弧长匀速 + 回弹缓动外推）；属性片段用 DOScale / DOTween.To 插入 Sequence，被覆盖的部分截断、缓动按原时长计算；事件用 InsertCallback，默认打印日志，可传回调；整段循环用 SetLoops，尾段自循环用子 Sequence（无限循环时前半段播完再开始）；终点绑定的动效 `Play(target, endTarget, worldCamera)` 每帧把终点对准落点对象，UI 与 3D 之间经屏幕坐标换算。瞬间变化的片段不用 0 时长补间，倒放和循环时能正确还原。
- 导出代码只依赖 UnityEngine 和 DOTween（UI 的透明度、颜色另需 UGUI），C# 9，无编译警告。可设命名空间；导出到 Assets 下自动导入，类名跟文件名一致。工程没装 DOTween 时会提醒。
- Play 模式试播：装了 DOTween 的工程里，导出到 Assets 后进入 Play 模式，点“Play 模式试播导出代码”在预览目标上真正跑一遍 DOTween，和编辑器预览对比；再点一次从头重播。
- 自动描述：每个路径段、片段、事件自动生成一句中文描述（方向、距离、弧度、时长、速度、缓动含义、目标值），没手填描述时用它。窗口描述框下方显示自动描述；JSON 写出 `desc` + `descSource`（manual / auto，auto 读回时忽略），根节点新增整个动效的 `summary`；导出代码的注释也用这些描述。
- 新增属性轨道的代码导出扩展点：写一个带 `[TrackCodeGen(typeof(MyTrack))]` 的 `TrackCodeGen` 子类即可，没有生成器的轨道导出时跳过并在提示与警告里说明。
- 提示与警告新增导出相关的检查（没有代码生成器的轨道、同类型多条轨道）。
- 轨道设置：透明度轨道新增“作用组件”（自动 / CanvasGroup / Graphic / SpriteRenderer），对象上同时有 CanvasGroup 和 Image 时可以只淡出这张图，场景物体也能用 CanvasGroup；预览、JSON（轨道的 `settings`）、导出代码、自动描述都按它走。自定义轨道加带 `[TrackSetting]` 的字段即可有自己的设置。
- 预览开关与 Scene 视图路径联动：打开“预览”才在 Scene 视图显示路径，关掉隐藏路径、对象还原；拖播放头、播放、打开“编辑路径”自动打开预览。预览目标不在当前打开的场景/预制体里时自动隐藏路径并还原对象，回去后接着预览。保存场景/预制体、脚本重新编译不再关掉预览（只临时还原对象）。“停止”改为停止播放并回到 0 秒，不关预览。
- 验证：tools/verify 新增回放测试，把导出代码用 Roslyn 编译后在模拟的 DOTween 上逐帧播放，和预览求值器对照位置与各属性（覆盖示例动效、各种循环、终点绑定、截断、瞬间片段）。

## [0.5.0] - 2026-10-15
- UI 与 3D 混合预览：窗口新增“落点对象”，路径终点自动对准它，可以是 UI 也可以是 3D 物体（3D 物体飞到 UI 上、UI 图片飞到 3D 模型上）。UI 与 3D 之间经屏幕坐标换算，3D 对象保持起点离相机的距离；跨 UI/3D 时可指定换算用的相机，默认 Main Camera。
- 终点跟随：落点对象或预览目标移动、Game 视图尺寸变化时，终点实时对准；跟随时终点不能手动拖，Scene 视图在终点画出落点标记。
- 预览时实时刷新 Game 视图，工具栏新增“Game 视图”按钮。
- 资产记住上次编辑的地方：上次在哪个场景或预制体里编辑，以及预览目标、落点对象和相机（MotionAsset.editContext，按场景/预制体 GUID + 层级路径 + GlobalObjectId 记录，不进 JSON）。打开或拖入资产时自动回到那个场景/预制体模式并填回对象（有未保存修改先问，可在窗口“上次编辑”一行关掉“自动”）；对象找不到时在提示与警告里说明。
- 数据：MotionData 新增 endTarget（bound、kind、hint），JSON 输出 endTarget 与 authoredEnd，并说明运行时的终点修正方法；MotionEvaluator 新增 GetPathProgress、EvaluatePosition(time, endCorrection)、AuthoredEndOffset；MotionEditing 新增 SetEndOffset。
- 测试场景新增两个混合示例：demo-heart-to-wallet（3D 爱心飞进 UI 钱包）、demo-coin-to-heart（UI 金币落到 3D 爱心上），双击即可预览。
- 循环新增“尾段自循环”：前面只播一次，之后只循环最后一段路径（例如飞到位后原地浮动）；时间轴标出循环的那一截。数据：LoopSettings.scope（Whole / LastSegment），MotionEvaluator.LoopStart / LoopLength，JSON 的 loop.scope 与 loop.start。
- 窗口分区：对象、时间轴、单段编辑（只改选中项）、全局编辑（整个动效）、提示与警告、导出，各自带色条标题；单段编辑的最后一段可一键设为自循环。
- 提示与警告单独成区，固定在窗口下方，按错误 / 警告 / 提示分类显示。
- 测试场景重新布局：天空背景、绿色地台上的 3D 爱心（竖屏 9:16 ~ 1:2 都完整可见）、左上角宝石与钱包资源栏（图标同尺寸）、底部奖励卡上的金币；金币飞钱包示例也改成终点绑定钱包。素材由 tools/art/gen_ui_art.py 生成。Main Camera 的 Size（正交相机）默认 6.5。旧测试场景会提示重建，重建时示例动效一并恢复默认。

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
