#if UNITY_EDITOR
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CheatEducation.EditorTools
{
    // メニューからレッスン01 の配線を自動で行う。
    //   1. ボットのマテリアル／プレハブを生成（Resources/）
    //   2. ボットプレハブを DefaultNetworkPrefabs に登録
    //   3. 開いているシーンに CheatConsole と CheatBotSpawner を追加
    //
    // オンライン対戦（Unity Gaming Services / Sessions）は一切使わない。
    // 使うのは [BB] Shooter.unity（NetworkManager が 127.0.0.1 のローカル構成）だけ。
    public static class CheatEducationSetup
    {
        const string kDir = "Assets/CheatEducation/Resources";
        const string kBotPrefabPath = kDir + "/CheatBot.prefab";
        const string kBotMatPath = kDir + "/CheatBotMat.mat";
        const string kNetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/Cheat Education/レッスン01 をセットアップ")]
        public static void Setup()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.name.Contains("Shooter") || scene.name.Contains("MultiplayerSession"))
            {
                if (!EditorUtility.DisplayDialog("シーンを確認",
                    $"開いているシーンは「{scene.name}」です。\n\n" +
                    "レッスン01 は Assets/Shooter/TestScenes/[BB] Shooter.unity（ローカル構成）で行います。\n" +
                    "[BB] Shooter MultiplayerSession はオンライン対戦用なので使いません。\n\n" +
                    "このまま続けますか？", "続ける", "やめる"))
                    return;
            }

            var botPrefab = EnsureBotPrefab();
            if (botPrefab == null) { Debug.LogError("[CheatEducation] ボットプレハブの生成に失敗しました。"); return; }

            RegisterInNetworkPrefabs(botPrefab);
            EnsureSceneObject<CheatConsole>("CheatConsole");
            var spawnerGo = EnsureSceneObject<CheatBotSpawner>("CheatBotSpawner");
            if (spawnerGo != null && spawnerGo.transform.position == Vector3.zero)
                spawnerGo.transform.position = new Vector3(0f, 3f, 0f);

            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();

            Debug.Log("[CheatEducation] セットアップ完了。\n" +
                      "  1) このシーン（[BB] Shooter）を保存\n" +
                      "  2) Play を押す\n" +
                      "  3) 画面の『Start Host』をクリック（オンライン接続は不要）\n" +
                      "  4) F1 でチートパネル → ESP / ウォールハック を ON\n" +
                      "  ※ ボットが床から外れて出る場合は CheatBotSpawner を床の上へ移動");
        }

        static GameObject EnsureBotPrefab()
        {
            if (!AssetDatabase.IsValidFolder(kDir))
            {
                if (!AssetDatabase.IsValidFolder("Assets/CheatEducation"))
                    AssetDatabase.CreateFolder("Assets", "CheatEducation");
                AssetDatabase.CreateFolder("Assets/CheatEducation", "Resources");
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(kBotPrefabPath);
            if (existing != null) return existing;

            // マテリアル（URP Lit・赤）
            var mat = AssetDatabase.LoadAssetAtPath<Material>(kBotMatPath);
            if (mat == null)
            {
                var urpLit = Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(urpLit != null ? urpLit : Shader.Find("Standard"));
                mat.color = new Color(0.85f, 0.2f, 0.2f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", mat.color);
                AssetDatabase.CreateAsset(mat, kBotMatPath);
            }

            // 一時 GameObject を組んでプレハブ化
            var root = new GameObject("CheatBot");
            var col = root.AddComponent<CapsuleCollider>();
            col.height = 2f; col.radius = 0.4f; col.center = new Vector3(0f, 1f, 0f);

            root.AddComponent<NetworkObject>();
            var nt = root.AddComponent<NetworkTransform>();
            nt.Interpolate = true;

            root.AddComponent<CheatBot>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(0f, 1f, 0f);
            visual.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, kBotPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.ImportAsset(kBotPrefabPath, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(kBotPrefabPath);
            Debug.Log("[CheatEducation] CheatBot.prefab を作成しました。");
            return reloaded;
        }

        static void RegisterInNetworkPrefabs(GameObject botPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(kNetworkPrefabsPath);
            if (list == null)
            {
                Debug.LogWarning($"[CheatEducation] {kNetworkPrefabsPath} が見つかりません。" +
                                 "CheatBotSpawner が実行時に AddNetworkPrefab で登録します。");
                return;
            }

            var so = new SerializedObject(list);
            var arr = so.FindProperty("List");

            // まず既存の CheatBot エントリを全部除去（二重登録＝"duplicate GlobalObjectIdHash" 防止）
            int removed = 0;
            for (int i = arr.arraySize - 1; i >= 0; i--)
            {
                var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab");
                if (p != null && p.objectReferenceValue == botPrefab)
                {
                    arr.DeleteArrayElementAtIndex(i);
                    removed++;
                }
            }

            // ちょうど 1 個だけ追加
            arr.arraySize++;
            var elem = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            elem.FindPropertyRelative("Override").enumValueIndex = 0;
            elem.FindPropertyRelative("Prefab").objectReferenceValue = botPrefab;
            var srcHash = elem.FindPropertyRelative("SourceHashToOverride");
            if (srcHash != null) srcHash.uintValue = 0;
            var srcPrefab = elem.FindPropertyRelative("SourcePrefabToOverride");
            if (srcPrefab != null) srcPrefab.objectReferenceValue = null;
            var overriding = elem.FindPropertyRelative("OverridingTargetPrefab");
            if (overriding != null) overriding.objectReferenceValue = null;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssetIfDirty(list);
            Debug.Log($"[CheatEducation] DefaultNetworkPrefabs を整理（重複 {removed} 個除去）してボットを 1 個登録しました。");
        }

        static GameObject EnsureSceneObject<T>(string name) where T : Component
        {
            var existing = Object.FindFirstObjectByType<T>();
            if (existing != null) return existing.gameObject;

            var go = new GameObject(name);
            go.AddComponent<T>();
            Undo.RegisterCreatedObjectUndo(go, "Cheat Education setup");
            Debug.Log($"[CheatEducation] シーンに {name} を追加しました。");
            return go;
        }
    }
}
#endif
