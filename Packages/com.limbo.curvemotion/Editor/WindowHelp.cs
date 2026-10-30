using UnityEditor;
using UnityEngine;

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 窗口最下面的“说明”：按模块分条，每条一个折叠项，展开状态记在 EditorPrefs。
    /// 各模块标题后面不再放说明文字，详细用法都写在这里。
    /// </summary>
    internal static class WindowHelp
    {
        const string PrefKey = "Limbo.CurveMotion.Help.";

        static readonly (string title, string body)[] s_Topics =
        {
            ("快速上手",
                "1. 选一个 Motion Asset，或点“新建”（默认三个点的路径）。也可以在 Project 窗口双击资产打开。\n" +
                "2. 指定预览目标（场景里要动的对象），打开“预览”，Scene 视图里出现路径。\n" +
                "3. 打开“编辑路径”在 Scene 视图拖点和切线；在时间轴调每段时长，加属性轨道、片段和事件。\n" +
                "4. 播放检查效果，没问题后在“导出”里复制 JSON 给 AI，或导出 DOTween 代码放进工程。\n" +
                "想先看示例：菜单 Tools/Curve Motion/Open Test Scene，双击示例动效即可预览。"),

            ("对象",
                "Motion Asset：动效数据存在这个资产里。\n" +
                "预览目标：动效在谁身上预览。路径以它的当前位置为起点，所有位置都是相对起点的偏移；UI 用 anchoredPosition，场景物体用 localPosition。关掉预览对象自动还原。\n" +
                "落点对象（可选）：指定后路径终点自动对准它，可以 3D 物体飞到 UI 上，也可以 UI 飞到 3D 物体上。开着“终点绑定”时终点跟随落点，不能手动改。不指定时终点就是最后一个路径点，可以随便调。\n" +
                "3D 相机：UI 与 3D 之间换算屏幕位置用，留空自动用 Main Camera。\n" +
                "上次编辑：资产记着上次在哪个场景或预制体里编辑，以及预览目标、落点对象和相机。打开资产时自动回到那里并填回对象，不想要可以关掉“自动”。"),

            ("Scene 视图编辑路径",
                "打开“编辑路径”后：点击路径点选中，拖坐标手柄移动；Smooth / Free 点有方形的切线手柄。\n" +
                "按住 Shift 在路径上点击插入点，Delete 删除选中的点。打开“网格吸附”后，点和切线相对起点按网格取整。\n" +
                "起点就是预览目标的位置，不能拖；终点跟随落点对象时也不能拖。"),

            ("时间轴",
                "编辑路径：Scene 视图显示可拖的路径点和切线手柄。\n" +
                "预览：打开后 Scene 视图显示路径，动效按播放头作用在预览目标上；关掉隐藏路径、对象还原。拖播放头或点播放会自动打开。\n" +
                "播放 / 暂停 / 停止：停止是回到 0 秒，不关预览。循环预览只影响预览，和动效本身的循环设置无关。Game 视图：看 UI 和 3D 叠在一起的实际效果。\n" +
                "时间轴吸附：拖动时吸到路径点、事件、其他片段和播放头，按住 Ctrl/⌘ 临时关闭。网格吸附：Scene 视图里拖点时按网格取整。\n" +
                "路径行：每段一个块。点一段，选中的是这段的起点和这一段（时长、缓动）；最后一段里还能直接改终点。拖段与段的分界改时长，右键插入或删除路径点。\n" +
                "事件行：双击空白处或右键加事件，拖动改触发时间。\n" +
                "属性轨道：点“+ 添加属性轨道”；在轨道上双击空白处或右键加片段，拖动移动，拖两端改时长。同一属性的片段重叠时，后面的覆盖前面的。\n" +
                "快捷键：空格播放/暂停，Delete 删除，Ctrl/⌘+D 复制一份，Ctrl/⌘+C / V 复制粘贴。"),

            ("单段编辑",
                "只改当前选中的那一项。\n" +
                "路径段：起点位置、点类型（Corner 尖角 / Smooth 平滑 / Free 两侧切线独立 / Auto 自动平滑）和切线、按时长或按速度计时、缓动、描述。起点固定在对象的初始位置，要改起点直接移动对象。\n" +
                "片段：锚点（跟随哪个路径点的到达时间）和偏移、时长、缓动、目标值、描述。缩放是倍率，旋转是增量，透明度和颜色是绝对值。\n" +
                "轨道：名称、启用，以及轨道自己的设置（例如透明度轨道的作用组件）。\n" +
                "事件：名称（决定导出的方法名）、特效说明、锚点和偏移。"),

            ("全局编辑",
                "作用于整个动效。\n" +
                "名称：也决定导出代码的默认类名，例如 coin-fly → CoinFlyMotion。\n" +
                "描述：整段动效写给 AI 看的说明。\n" +
                "循环：次数（-1 无限）、Restart / Yoyo；范围可选整段循环，或前面播一次后只循环最后一段。\n" +
                "终点绑定：运行时终点对准代码传入的落点对象，当前终点只是设计参考；可写落点说明，给人和 AI 看。"),

            ("提示与警告",
                "汇总当前动效的问题，按错误、警告、提示分级。例如预览目标类型和动效不符、落点换算失败、某条轨道没有代码生成器导不出代码、同一属性有多条轨道等。"),

            ("JSON",
                "JSON 是动效的完整数据，也是给 AI 读的格式：每个路径段、片段、事件都带一句描述（desc），根节点有整段动效的概括（summary），AI 不用读代码就能看懂动效。\n" +
                "复制 JSON：复制到剪贴板，可以直接贴给 AI，让它解释或修改动效。\n" +
                "导出 JSON…：保存成 .json 文件，用于备份、跨工程搬运或交给别人。\n" +
                "导入 JSON…：用一个 .json 文件替换当前资产里的动效数据，可以撤销。常用来把 AI 改过的 JSON 贴回来。自动生成的描述不读回；上次编辑的场景和对象不受影响；格式不对或版本比包新时会报错，不改任何东西。想得到一份新动效，先“新建”再导入。"),

            ("DOTween 代码",
                "生成一个静态类，可以直接放进工程（工程里要装 DOTween）。Play(target) 以对象当前位置为起点、当前缩放/旋转/透明度/颜色为初始值播放，返回 Sequence，效果和编辑器预览一致；停止用 target.DOKill() 或 Kill 返回值。终点绑定的动效多两个参数：落点对象和换算用的相机。\n" +
                "代码命名空间：导出的类放进这个命名空间，留空为全局，整个工程共用。\n" +
                "复制 DOTween 代码：代码和事件处理方法一起复制，一份文本就能编译，适合贴进现有脚本。\n" +
                "导出 C#…：保存成 .cs 文件，类名跟文件名一致；存到 Assets 下会自动导入。有事件时旁边还会生成“类名.Events.cs”，见“事件接入”。"),

            ("事件接入",
                "每个事件名对应一个方法 OnXxx(Transform target)，例如事件 coin land → OnCoinLand。方法写在导出时生成的“类名.Events.cs”里，开发只需要填方法体：播特效、音效、震屏、加分等。\n" +
                "这个文件只在第一次导出时生成，之后重新导出只在末尾追加新事件的方法，不会覆盖已经写好的代码。事件改名或删除后留下的旧方法不会报错，导出时会提示。\n" +
                "特效说明：特效师写给开发的话。导出后写进方法注释；开发接入前，每次触发都会在 Console 打一条橙色警告带上这段话，点日志能定位到对象。接入后删掉那行警告即可。"),

            ("Play 模式试播",
                "用导出到 Assets 的类，在预览目标上真正跑一遍 DOTween，和编辑器预览对比，确认导出代码没问题。\n" +
                "条件：工程装了 DOTween，已经“导出 C#…”到 Assets 下并编译完，处于 Play 模式，并且指定了预览目标。\n" +
                "再点一次会先停掉上一次、把对象还原再从头播。"),
        };

        public static void Draw()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                // 改了展开状态后结束本次事件，否则后面的控件数和 Layout 时对不上。
                if (GUILayout.Button("全部展开", EditorStyles.miniButtonLeft, GUILayout.Width(64))) { SetAll(true); GUIUtility.ExitGUI(); }
                if (GUILayout.Button("全部收起", EditorStyles.miniButtonRight, GUILayout.Width(64))) { SetAll(false); GUIUtility.ExitGUI(); }
            }
            for (int i = 0; i < s_Topics.Length; i++)
            {
                bool open = EditorPrefs.GetBool(PrefKey + i, false);
                bool next = EditorGUILayout.Foldout(open, s_Topics[i].title, true);
                if (next != open) EditorPrefs.SetBool(PrefKey + i, next);
                // 按改之前的状态画，本次事件的控件与 Layout 一致；下一次重绘再展开或收起。
                if (!open) continue;
                EditorGUI.indentLevel++;
                foreach (var line in s_Topics[i].body.Split('\n'))
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
                EditorGUI.indentLevel--;
                GUILayout.Space(4f);
            }
        }

        static void SetAll(bool open)
        {
            for (int i = 0; i < s_Topics.Length; i++) EditorPrefs.SetBool(PrefKey + i, open);
        }
    }
}
