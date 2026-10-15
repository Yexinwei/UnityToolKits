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
    /// 测试专用场景：一个 UI 金币、一个 3D 方块，各配一个示例 Motion Asset，并在编辑器窗口中打开。
    /// 场景与示例资产生成到工程的 Assets/CurveMotionTest/（git 包是只读的，包内的场景无法保存修改）。
    /// 示例动效来自包内同目录的 JSON。
    /// </summary>
    internal static class TestSceneBuilder
    {
        const string Folder = "Assets/CurveMotionTest";
        const string ScenePath = Folder + "/CurveMotionTest.unity";
        const string DemoFolder = "Packages/" + CurveMotionInfo.PackageName + "/Editor/TestScene";

        const string CoinDemo = "demo-coin-fly";
        const string HopDemo = "demo-cube-hop";
        const string CoinName = "UI Target (Coin)";
        const string CubeName = "3D Target (Cube)";

        [MenuItem("Tools/Curve Motion/Open Test Scene", priority = 20)]
        public static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                Build(resetAssets: false);
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            FocusDemo(LoadDemo(CoinDemo, false), LoadDemo(HopDemo, false));
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

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            CreateGround();
            CreateCube();
            CreateUi();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            FocusDemo(coinAsset, hopAsset);
        }

        /// <summary>优先打开 UI 金币示例；工程没有 UGUI 时打开 3D 方块示例。</summary>
        static void FocusDemo(MotionAsset coinAsset, MotionAsset hopAsset)
        {
            var coin = GameObject.Find(CoinName);
            var cube = GameObject.Find(CubeName);
            bool useCoin = coin != null && coinAsset != null;
            var target = useCoin ? coin.transform : cube != null ? cube.transform : null;
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

        static void CreateCube()
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = CubeName;
            cube.transform.position = new Vector3(-2.5f, -0.5f, 0f);
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
            CreateImage(canvasGo.transform, "Wallet", coinStart + new Vector2(420f, 640f), new Vector2(180f, 90f), new Color(0.55f, 0.35f, 0.2f, 1f));
            CreateImage(canvasGo.transform, CoinName, coinStart, new Vector2(120f, 120f), new Color(1f, 0.82f, 0.2f, 1f));
#endif
        }

#if CURVEMOTION_UGUI
        static void CreateImage(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            go.GetComponent<Image>().color = color;
        }
#endif
    }
}
