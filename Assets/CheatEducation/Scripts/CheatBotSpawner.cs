using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CheatEducation
{
    // シーンに置く。Host（＝サーバー）が始まったら簡易ボットを生成する。
    // オンライン対戦・Unity Gaming Services は一切使わない。
    //
    // 使い方: メニュー「Tools > Cheat Education > レッスン01 をセットアップ」で自動配置される。
    [DisallowMultipleComponent]
    public class CheatBotSpawner : MonoBehaviour
    {
        [Tooltip("Resources から読むボットのプレハブ名")]
        public string botResourceName = "CheatBot";

        [Range(1, 8)] public int botCount = 3;
        [Tooltip("ボットを撒く半径")]
        public float spawnAreaRadius = 10f;
        [Tooltip("サーバー開始からボット生成までの待ち（自分のプレイヤーが出るのを待つ）")]
        public float spawnDelaySeconds = 1.5f;

        GameObject _botPrefab;
        readonly List<NetworkObject> _spawned = new List<NetworkObject>();
        bool _hooked;

        void Start()
        {
            _botPrefab = Resources.Load<GameObject>(botResourceName);
            if (_botPrefab == null)
            {
                Debug.LogError($"[CheatBotSpawner] Resources/{botResourceName}.prefab が見つかりません。" +
                               "メニュー Tools > Cheat Education > レッスン01 をセットアップ を実行してください。");
                return;
            }

            var nm = NetworkManager.Singleton;
            if (nm == null) { Debug.LogError("[CheatBotSpawner] NetworkManager がありません。"); return; }

            // ★ プレハブの登録は Editor のセットアップメニューが DefaultNetworkPrefabs へ行う。
            //   ここで AddNetworkPrefab すると「リスト由来」と「実行時追加」で二重登録になり
            //   "duplicate GlobalObjectIdHash" エラーで StartHost が失敗する。ので何もしない。

            nm.OnServerStarted += HandleServerStarted;
            nm.OnServerStopped += HandleServerStopped;
            _hooked = true;

            if (nm.IsServer && nm.IsListening) HandleServerStarted();
        }

        void OnDestroy()
        {
            if (!_hooked) return;
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            nm.OnServerStarted -= HandleServerStarted;
            nm.OnServerStopped -= HandleServerStopped;
        }

        void HandleServerStarted()
        {
            if (_spawned.Count > 0) return;
            StartCoroutine(SpawnAfterDelay());
        }

        IEnumerator SpawnAfterDelay()
        {
            yield return new WaitForSeconds(spawnDelaySeconds);

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || _botPrefab == null) yield break;

            // 中心 = 自分のプレイヤーの位置（まだ居なければワールド原点）
            Vector3 center = Vector3.zero;
            var hostPlayer = nm.SpawnManager != null ? nm.SpawnManager.GetLocalPlayerObject() : null;
            if (hostPlayer != null) center = hostPlayer.transform.position;

            for (int i = 0; i < botCount; i++)
            {
                try
                {
                    var r = Random.insideUnitCircle.normalized * Random.Range(3f, spawnAreaRadius);
                    Vector3 pos = center + new Vector3(r.x, 0f, r.y);

                    // 真下に床を探して着地（ボット・プレイヤーのコライダーは無視）
                    var hits = Physics.RaycastAll(pos + Vector3.up * 30f, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);
                    System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    foreach (var h in hits)
                    {
                        if (h.collider.GetComponentInParent<CheatBot>() != null) continue;
                        if (h.collider.GetComponentInParent<NetworkObject>() is { IsPlayerObject: true }) continue;
                        pos = h.point + Vector3.up * 0.05f;
                        break;
                    }

                    var go = Instantiate(_botPrefab, pos, Quaternion.identity);
                    go.name = $"CheatBot_{i}";
                    // 各ボットに「陣地の中心と半径」を渡す（この中でだけ徘徊する）
                    var bot = go.GetComponent<CheatBot>();
                    if (bot != null) bot.ConfigureServer(center, spawnAreaRadius + 3f, bot.moveSpeed);
                    go.GetComponent<NetworkObject>().Spawn(true);
                    _spawned.Add(go.GetComponent<NetworkObject>());
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[CheatBotSpawner] ボット {i} の生成に失敗: {e.Message}");
                }
            }

            Debug.Log($"[CheatBotSpawner] ボットを {_spawned.Count} 体生成（center={center}）。" +
                      " 見当たらない場合はプレイヤーの周囲を見回してください。");
        }

        void HandleServerStopped(bool _)
        {
            StopAllCoroutines();
            foreach (var no in _spawned)
                if (no != null && no.IsSpawned) no.Despawn();
            _spawned.Clear();
        }
    }
}
