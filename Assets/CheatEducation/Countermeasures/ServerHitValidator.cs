using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CheatEducation.Countermeasures
{
    // レッスン02/03 の対策の雛形: サーバー側でヒットと発射レートを検証する。
    //
    // ★これは「そのまま動く」スクリプトではない。テンプレートの発砲フローに合わせて
    //   接続する必要がある（発砲は Shooter レイヤーのどのスクリプトのどの RPC か、を
    //   レッスン02 の調査手順で特定する）。下の TODO を埋める。
    //
    // 考え方:
    //   クライアントは「撃った」という事実と視線方向だけを送る。
    //   サーバーが (1) 発射レート (2) 発射位置の妥当性 (3) 実際に当たるか (4) ダメージ量
    //   をすべて自前で決める。クライアントの「当たった」「9999 ダメージ」を信じない。
    [RequireComponent(typeof(NetworkObject))]
    public class ServerHitValidator : NetworkBehaviour
    {
        [Header("武器（サーバーが持つ正本）")]
        [Tooltip("ダメージ・射程・連射間隔の正本。クライアントの値は使わない")]
        public float serverDamage = 20f;
        public float serverRange = 100f;
        public float serverFireInterval = 0.12f;
        public LayerMask hitMask = ~0;

        [Header("妥当性の許容")]
        [Tooltip("報告された発射位置と、サーバーが知る発射者の位置の許容ズレ(m)")]
        public float originTolerance = 1.5f;
        [Tooltip("報告された方向と、直近に報告された視線方向の許容角(度)。エイムボットの急な首振りを弾く")]
        public float directionToleranceDeg = 35f;

        // ---- ラグ補償用: 各プレイヤーの過去位置リングバッファ ----
        struct Snap { public double time; public Vector3 pos; }
        static readonly Dictionary<ulong, Queue<Snap>> s_history = new();
        const double kHistorySeconds = 1.0;

        // ---- 発射レート監視 ----
        readonly Dictionary<ulong, double> _lastShotTime = new();
        // ---- 統計（事後検知） ----
        readonly Dictionary<ulong, (int shots, int hits, double reactSum)> _stats = new();

        void Update()
        {
            if (!IsServer) return;
            // このプレイヤーの位置履歴を毎フレーム記録（ラグ補償の材料）。
            // 各プレイヤーの Validator が自分ぶんを記録する。
            double now = NetworkManager.ServerTime.Time;
            if (!s_history.TryGetValue(OwnerClientId, out var q))
                s_history[OwnerClientId] = q = new Queue<Snap>();
            q.Enqueue(new Snap { time = now, pos = transform.position });
            while (q.Count > 0 && now - q.Peek().time > kHistorySeconds) q.Dequeue();
        }

        // ================================================================
        // TODO: テンプレートの「発砲 RPC」からこのメソッドを呼ぶ。
        //   例) [Rpc(SendTo.Server)] void FireRpc(Vector3 origin, Vector3 dir, RpcParams p=default)
        //       => GetComponent<ServerHitValidator>().ServerTryFire(p.Receive.SenderClientId, origin, dir);
        // ================================================================
        public void ServerTryFire(ulong shooterClientId, Vector3 reportedOrigin, Vector3 reportedDir)
        {
            if (!IsServer) return;
            double now = NetworkManager.ServerTime.Time;

            // (1) 発射レート
            if (_lastShotTime.TryGetValue(shooterClientId, out double last) &&
                now - last < serverFireInterval * 0.9)   // 0.9 = ネットワークゆらぎの許容
            {
                LogSuspicious(shooterClientId, "fire-rate（連射が速すぎる）");
                return;
            }
            _lastShotTime[shooterClientId] = now;

            // (2) 発射位置の妥当性
            var shooter = NetworkManager.SpawnManager.GetPlayerNetworkObject(shooterClientId);
            if (shooter == null) return;
            if (Vector3.Distance(reportedOrigin, shooter.transform.position) > originTolerance + 2f)
            {
                LogSuspicious(shooterClientId, "origin（発射位置が本体から離れている）");
                return;
            }

            // (3) 方向の妥当性（前回報告との角度差）
            //   ※ テンプレートが「毎フレーム視線方向を同期」しているならそれと比較する。
            //   ここでは簡易に shooter の forward と比較。
            if (Vector3.Angle(shooter.transform.forward, reportedDir) > directionToleranceDeg + 60f)
            {
                LogSuspicious(shooterClientId, "direction（体の向きと弾道が大きく食い違う）");
                // return; // ← 厳しくするならここで弾く
            }

            // (4) サーバー自身がレイキャストして命中判定（ラグ補償つき）
            double shotTime = now - EstimateRtt(shooterClientId) * 0.5;
            NetworkObject victim = ServerRaycastWithRewind(reportedOrigin, reportedDir.normalized, shotTime,
                                                          out float reactionTime);

            var st = _stats.GetValueOrDefault(shooterClientId);
            st.shots++;
            if (victim != null && victim.OwnerClientId != shooterClientId)
            {
                st.hits++;
                st.reactSum += reactionTime;
                ApplyDamageServer(victim, serverDamage);
            }
            _stats[shooterClientId] = st;

            // 統計の簡易フラグ
            if (st.shots >= 20)
            {
                float hitRate = (float)st.hits / st.shots;
                double avgReact = st.hits > 0 ? st.reactSum / st.hits : 999;
                if (hitRate > 0.92f || avgReact < 0.06)
                    LogSuspicious(shooterClientId, $"統計（命中率 {hitRate:P0} / 平均反応 {avgReact*1000:F0}ms）");
            }
        }

        NetworkObject ServerRaycastWithRewind(Vector3 origin, Vector3 dir, double atTime, out float reactionTime)
        {
            reactionTime = 0f;
            // ラグ補償: 本来は atTime 時点の各プレイヤーの位置へコライダーを巻き戻してから Raycast する。
            //   ここでは簡略化して現在位置で Raycast し、履歴からの補正はコメントに留める。
            if (Physics.Raycast(origin, dir, out var hit, serverRange, hitMask, QueryTriggerInteraction.Ignore))
            {
                var no = hit.collider.GetComponentInParent<NetworkObject>();
                if (no != null && no.IsPlayerObject) return no;
            }
            return null;
        }

        void ApplyDamageServer(NetworkObject victim, float dmg)
        {
            // TODO: テンプレートの体力コンポーネント（CoreStatsHandler 等）の
            //   「サーバー専用ダメージ適用」メソッドを呼ぶ。体力は NetworkVariable(server write) にしておく。
            // 例) victim.GetComponent<CoreStatsHandler>().ApplyDamageServer(dmg);
            Debug.Log($"[ServerHitValidator] {victim.name} に {dmg} ダメージ（サーバー計算）");
        }

        double EstimateRtt(ulong clientId)
        {
            var t = NetworkManager.Singleton.NetworkConfig.NetworkTransport;
            return t != null ? t.GetCurrentRtt(clientId) / 1000.0 : 0.1;
        }

        void LogSuspicious(ulong clientId, string reason)
            => Debug.LogWarning($"[対策] client {clientId}: {reason}");
    }
}
