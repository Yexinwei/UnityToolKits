using System;
using Limbo.CurveMotion.Serialization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if CURVEMOTION_UGUI
using UnityEngine.UI;
#endif

namespace Limbo.CurveMotion.Editor
{
    /// <summary>
    /// 测试专用场景：天空背景、3D 地台上的爱心、左上角的宝石与钱包资源栏、底部奖励卡上的 UI 金币，
    /// 配四个示例 Motion Asset，并在编辑器窗口中打开。竖屏（9:16 ~ 1:2）下所有对象都完整可见。
    /// 其中两个是 UI/3D 混合示例（爱心飞进钱包、金币落到爱心上）。每个示例资产都记好了场景、预览目标和落点对象，双击资产即可预览。
    /// 场景与示例资产生成到工程的 Assets/CurveMotionTest/（git 包是只读的，包内的场景无法保存修改）。
    /// 示例动效来自包内同目录的 JSON，美术素材在 Art/；素材缺失时退回纯色方块，不影响测试。
    /// </summary>
    internal static class TestSceneBuilder
    {
        const string Folder = "Assets/CurveMotionTest";
        const string ScenePath = Folder + "/CurveMotionTest.unity";
        const string DemoFolder = "Packages/" + CurveMotionInfo.PackageName + "/Editor/TestScene";
        const string ArtFolder = DemoFolder + "/Art";

        const string CoinDemo = "demo-coin-fly";
        const string HopDemo = "demo-heart-hop";
        const string HeartToWalletDemo = "demo-heart-to-wallet";
        const string CoinToHeartDemo = "demo-coin-to-heart";
        const string CoinName = "UI Target (Coin)";
        const string HeartName = "3D Target (Heart)";
        const string WalletName = "Wallet";

        /// <summary>场景内容变化时加 1；旧版本生成的场景会提示重建。记录在场景 .meta 的 userData 里。</summary>
        const string SceneVersion = "curvemotion-test-scene:5";

        [MenuItem("Tools/Curve Motion/Open Test Scene", priority = 20)]
        public static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Build(resetAssets: false);
                return;
            }
            // 布局变了，示例动效的路径跟着布局走，所以一起恢复默认。
            if (!IsCurrentVersion() && EditorUtility.DisplayDialog("测试场景已更新",
                    "现有测试场景是旧版本包生成的（布局和素材已更新）。是否重建？场景和四个示例动效会恢复为默认内容。", "重建", "先用旧的"))
            {
                Build(resetAssets: true);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var coinAsset = LoadDemo(CoinDemo, false);
            var hopAsset = LoadDemo(HopDemo, false);
            LoadDemo(HeartToWalletDemo, false);
            LoadDemo(CoinToHeartDemo, false);
            RegisterBindings(overwrite: false);
            FocusDemo(coinAsset, hopAsset);
        }

        static bool IsCurrentVersion()
        {
            var importer = AssetImporter.GetAtPath(ScenePath);
            return importer != null && importer.userData == SceneVersion;
        }

        static void MarkCurrentVersion()
        {
            var importer = AssetImporter.GetAtPath(ScenePath);
            if (importer == null || importer.userData == SceneVersion) return;
            importer.userData = SceneVersion;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(ScenePath); // 只写 .meta，不重新导入正在打开的场景
        }

        [MenuItem("Tools/Curve Motion/Rebuild Test Scene", priority = 21)]
        static void RebuildTestScene()
        {
            if (!EditorUtility.DisplayDialog("重建测试场景",
                    $"将重新生成 {ScenePath}，并把示例 Motion Asset 恢复为默认内容。对它们的修改会丢失。", "重建", "取消"))
                return;
            Build(resetAssets: true);
        }

        static void Build(bool resetAssets)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder();
            var coinAsset = LoadDemo(CoinDemo, resetAssets);
            var hopAsset = LoadDemo(HopDemo, resetAssets);
            LoadDemo(HeartToWalletDemo, resetAssets);
            LoadDemo(CoinToHeartDemo, resetAssets);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = SetupCamera();
            CreateBackground(camera);
            CreateGround();
            CreateHeart();
            CreateUi();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            MarkCurrentVersion();
            RegisterBindings(overwrite: true); // 新场景里的对象 id 都变了

            FocusDemo(coinAsset, hopAsset);
        }

        /// <summary>
        /// 给示例资产记下预览目标和落点对象，双击资产就能直接预览。场景必须已保存（对象才有稳定 id）。
        /// overwrite 为 false 时只补记缺的，不覆盖用户自己换过的对象。
        /// </summary>
        static void RegisterBindings(bool overwrite)
        {
            var coin = FindTransform(CoinName);
            var heart = FindTransform(HeartName);
            var wallet = FindTransform(WalletName);
            bool changed = RegisterBinding(CoinDemo, coin, wallet, overwrite);
            changed |= RegisterBinding(HopDemo, heart, null, overwrite);
            changed |= RegisterBinding(HeartToWalletDemo, heart, wallet, overwrite);
            changed |= RegisterBinding(CoinToHeartDemo, coin, heart, overwrite);
            if (changed) AssetDatabase.SaveAssets();
        }

        /// <summary>记进资产的“上次编辑”（场景 + 预览目标 + 落点对象）。</summary>
        static bool RegisterBinding(string demo, Transform mover, Transform end, bool overwrite)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MotionAsset>($"{Folder}/{demo}.asset");
            if (asset == null || mover == null) return false;
            var ctx = asset.editContext;
            if (!overwrite && ctx != null && EditContextStore.Resolve(ctx.mover) != null
                && (end == null || EditContextStore.Resolve(ctx.endTarget) != null))
                return false;
            return EditContextStore.Save(asset, EditContextStore.Slots.All, mover, end, null);
        }

        static Transform FindTransform(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.transform : null;
        }

        /// <summary>优先打开 UI 金币示例；工程没有 UGUI 时打开 3D 爱心示例。</summary>
        static void FocusDemo(MotionAsset coinAsset, MotionAsset hopAsset)
        {
            var coin = GameObject.Find(CoinName);
            var heart = GameObject.Find(HeartName);
            bool useCoin = coin != null && coinAsset != null;
            var target = useCoin ? coin.transform : heart != null ? heart.transform : null;
            var asset = useCoin ? coinAsset : hopAsset;

            var window = CurveMotionWindow.Open();
            window.SetAsset(asset);
            window.SetTarget(target);

            if (target == null) return;
            Selection.activeTransform = target;
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.in2DMode = true;
                view.FrameSelected();
            }
        }

        static MotionAsset LoadDemo(string demo, bool reset)
        {
            string assetPath = $"{Folder}/{demo}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<MotionAsset>(assetPath);
            if (existing != null && !reset) return existing;

            var json = AssetDatabase.LoadAssetAtPath<TextAsset>($"{DemoFolder}/{demo}.json");
            if (json == null)
            {
                Debug.LogError($"[Curve Motion] 找不到示例 {DemoFolder}/{demo}.json");
                return existing;
            }

            MotionData data;
            try
            {
                data = MotionJson.FromJson(json.text);
            }
            catch (FormatException e)
            {
                Debug.LogError($"[Curve Motion] 示例 {demo}.json 解析失败：{e.Message}");
                return existing;
            }

            if (existing != null)
            {
                existing.data = data;
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssets();
                return existing;
            }

            EnsureFolder();
            var asset = ScriptableObject.CreateInstance<MotionAsset>();
            asset.data = data;
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return asset;
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "CurveMotionTest");
        }

        /// <summary>相机拉远一点，竖屏最窄（1:2）时爱心和它的弹跳范围（x -3 ~ 3）也完整可见。</summary>
        static Camera SetupCamera()
        {
            var camera = Camera.main;
            if (camera == null) return null;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 0.3f, -12.5f), Quaternion.identity);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.44f, 0.76f, 0.97f, 1f);
            camera.orthographicSize = 6.5f; // 2D 工程的默认相机是正交的，6.5 下竖屏里爱心和地台完整可见
            return camera;
        }

        /// <summary>
        /// 天空背景放在 Screen Space - Camera 的 Canvas 上，平面在 3D 物体后面，所以爱心和地台画在它前面；
        /// 金币、资源栏所在的 Overlay Canvas 仍然盖在最上层。
        /// </summary>
        static void CreateBackground(Camera camera)
        {
#if CURVEMOTION_UGUI
            if (camera == null) return;
            var canvasGo = new GameObject("Background Canvas", typeof(RectTransform), typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100f;
            canvas.sortingOrder = -100;
            var sky = CreateImage(canvasGo.transform, "Sky", Vector2.zero, Vector2.zero, LoadSprite("bg_sky.png"));
            var rect = sky.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            sky.raycastTarget = false;
            // 背景很大，Scene 视图里点空白处别选中它。
            SceneVisibilityManager.instance.DisablePicking(canvasGo, true);
#endif
        }

        /// <summary>绿色地台，顶面在 y = -1，与示例动效的落地高度对齐。</summary>
        static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -1.15f, 0f);
            ground.transform.localScale = new Vector3(6.6f, 0.3f, 2f);
            var renderer = ground.GetComponent<MeshRenderer>();
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{ArtFolder}/Ground_Mat.mat");
            if (material != null) renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>爱心模型约 1 × 0.9 × 0.42，轴心在中心；放在地面上方，落点与示例动效的 y = 0 对齐。</summary>
        static void CreateHeart()
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{ArtFolder}/Heart3D_Mesh.asset");
            var material = AssetDatabase.LoadAssetAtPath<Material>($"{ArtFolder}/Heart3D_Mat.mat");
            GameObject heart;
            if (mesh != null && material != null)
            {
                heart = new GameObject(HeartName, typeof(MeshFilter), typeof(MeshRenderer));
                heart.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = heart.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                heart.transform.position = new Vector3(-2.5f, -1f + mesh.bounds.extents.y, 0f);
            }
            else
            {
                Debug.LogWarning($"[Curve Motion] 找不到 {ArtFolder} 下的爱心素材，改用方块。");
                heart = GameObject.CreatePrimitive(PrimitiveType.Cube);
                heart.name = HeartName;
                heart.transform.position = new Vector3(-2.5f, -0.5f, 0f);
            }
        }

        static void CreateUi()
        {
#if CURVEMOTION_UGUI
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            // 左上角资源栏：宝石、钱包，图标同尺寸，贴着屏幕左上角，不随屏幕比例跑位。
            var topLeft = new Vector2(0f, 1f);
            CreateCounter(canvasGo.transform, "Gem", topLeft, new Vector2(90f, -100f), LoadSprite("ic_gem.png"), "36");
            CreateCounter(canvasGo.transform, WalletName, topLeft, new Vector2(90f, -215f), LoadSprite("ic_wallet.png"), "1,250");

            // 底部奖励卡：金币从这里弹起。
            var bottom = new Vector2(0.5f, 0f);
            var card = CreateImage(canvasGo.transform, "Reward Card", bottom, new Vector2(0f, 330f), LoadSprite("ui_panel.png"), new Vector2(300f, 280f));
            card.type = Image.Type.Sliced;
            var amount = CreateLabel(card.transform, "Amount", new Vector2(0f, -85f), new Vector2(200f, 60f), "+50", 44);
            amount.color = new Color(0.75f, 0.4f, 0.16f, 1f);

            var coinSprite = LoadSprite("ic_coin.png");
            var coin = CreateImage(canvasGo.transform, CoinName, bottom, new Vector2(0f, 370f), coinSprite, new Vector2(140f, 140f));
            if (coinSprite == null) coin.color = new Color(1f, 0.82f, 0.2f, 1f);
            coin.preserveAspect = true;
#endif
        }

#if CURVEMOTION_UGUI
        static Sprite LoadSprite(string file)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{file}");
            if (sprite == null) Debug.LogWarning($"[Curve Motion] 找不到 {ArtFolder}/{file}，改用纯色块。");
            return sprite;
        }

        /// <summary>anchor 是锚点（0~1，左下为 0）；anchoredPosition 相对锚点。</summary>
        static Image CreateImage(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Sprite sprite, Vector2 size = default)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.color = sprite != null ? Color.white : new Color(0.55f, 0.35f, 0.2f, 1f);
            image.raycastTarget = false;
            return image;
        }

        static Text CreateLabel(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, string text, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var label = go.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        /// <summary>资源栏：胶囊底 + 左侧图标 + 数字。图标统一 110×110（保持比例），名字为 iconName，是动效的落点对象。</summary>
        static void CreateCounter(Transform parent, string iconName, Vector2 anchor, Vector2 iconPosition, Sprite icon, string amount)
        {
            var bar = CreateImage(parent, iconName + " Bar", anchor, iconPosition + new Vector2(118f, -2f), LoadSprite("ui_bar.png"), new Vector2(210f, 72f));
            bar.type = Image.Type.Sliced;
            var label = CreateLabel(bar.transform, "Amount", new Vector2(22f, 3f), new Vector2(150f, 60f), amount, 38);
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.45f);

            var image = CreateImage(parent, iconName, anchor, iconPosition, icon, new Vector2(110f, 110f));
            image.preserveAspect = true;
        }
#endif
    }
}
