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
    /// 测试专用场景：一个 UI 金币、一个 3D 爱心、一个 UI 钱包，配四个示例 Motion Asset，并在编辑器窗口中打开。
    /// 其中两个是 UI/3D 混合示例（爱心飞进钱包、金币落到爱心上），预览目标和落点对象已经记好，双击资产即可预览。
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
        const string SceneVersion = "curvemotion-test-scene:2";

        [MenuItem("Tools/Curve Motion/Open Test Scene", priority = 20)]
        public static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Build(resetAssets: false);
                return;
            }
            if (!IsCurrentVersion() && EditorUtility.DisplayDialog("测试场景已更新",
                    "现有测试场景是旧版本包生成的（没有新的金币和爱心素材）。是否重建？已有的示例动效保留不动。", "重建", "先用旧的"))
            {
                Build(resetAssets: false);
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
            RegisterBinding(CoinDemo, coin, null, overwrite);
            RegisterBinding(HopDemo, heart, null, overwrite);
            RegisterBinding(HeartToWalletDemo, heart, wallet, overwrite);
            RegisterBinding(CoinToHeartDemo, coin, heart, overwrite);
        }

        static void RegisterBinding(string demo, Transform mover, Transform end, bool overwrite)
        {
            var asset = AssetDatabase.LoadAssetAtPath<MotionAsset>($"{Folder}/{demo}.asset");
            if (asset == null || mover == null) return;
            if (!overwrite && PreviewBindings.TryLoad(asset, out var savedMover, out var savedEnd, out _)
                && savedMover != null && (end == null || savedEnd != null))
                return;
            PreviewBindings.Save(asset, mover, end, null);
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

        static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -1.05f, 0f);
            ground.transform.localScale = new Vector3(12f, 0.1f, 2f);
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

            // 钱包放在金币示例路径的终点（起点 + (420, 640)），方便目测路径是否对齐。
            var coinStart = new Vector2(-250f, -450f);
            CreateImage(canvasGo.transform, WalletName, coinStart + new Vector2(420f, 640f), new Vector2(180f, 90f), new Color(0.55f, 0.35f, 0.2f, 1f));

            var coinSprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/ic_coin.png");
            if (coinSprite == null) Debug.LogWarning($"[Curve Motion] 找不到 {ArtFolder}/ic_coin.png，金币改用纯色块。");
            var coin = CreateImage(canvasGo.transform, CoinName, coinStart, new Vector2(120f, 120f),
                coinSprite != null ? Color.white : new Color(1f, 0.82f, 0.2f, 1f));
            coin.sprite = coinSprite;
            coin.preserveAspect = true;
#endif
        }

#if CURVEMOTION_UGUI
        static Image CreateImage(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }
#endif
    }
}
