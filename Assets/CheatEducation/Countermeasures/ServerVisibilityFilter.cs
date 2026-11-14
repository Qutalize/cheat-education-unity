using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace CheatEducation.Countermeasures
{
    // レッスン01 の対策: サーバーが「そのクライアントから実際に見える敵」だけを送る。
    //
    // これで ESP / ウォールハックの根を断つ:
    //   見えない敵の座標がクライアントに届かない ＝ 描くものが無い。
    //
    // 実在の手本:
    //   ioquake3 の PVS + sv_wallhack.c（視線判定）／ BlackMirror（ACM CCS 2020）／
    //   米国特許 US 11,771,997 B2（サーバー側レイトレで可視性フィルタ）。
    //
    // 使い方:
    //   Assets/Shooter/ のプレイヤープレハブに追加する（各プレイヤーに 1 個）。
    //   wallMask に「壁・地形」のレイヤーだけを設定（プレイヤー・弾は含めない）。
    //
    // ★注意（NGO の仕様）:
    //   NetworkObject.CheckObjectVisibility は「スポーン時／新規接続時」しか呼ばれない。
    //   プレイ中に変わる可視性は、毎tick 自分で NetworkShow/NetworkHide する必要がある。
    //   このスクリプトは両方やる。
    [RequireComponent(typeof(NetworkObject))]
    public class ServerVisibilityFilter : NetworkBehaviour
    {
        [Header("可視判定")]
        [Tooltip("この距離を超えたら問答無用で不可視")]
        public float maxDistance = 120f;
        [Tooltip("壁・地形のレイヤー（プレイヤー/弾は含めない）")]
        public LayerMask wallMask = 0;
        [Tooltip("観測者・対象の目の高さ")]
        public float eyeHeight = 1.6f;
        [Tooltip("複数の点で遮蔽判定する（0=中心のみ, 1=上下, 2=上下左右）")]
        [Range(0, 2)] public int extraSamplePairs = 1;

        [Header("チラつき対策")]
        [Tooltip("見えなくなってから実際に隠すまでの猶予（秒）")]
        public float graceSeconds = 0.4f;
        [Tooltip("何秒ごとに再評価するか（tick より粗くして負荷を下げる）")]
        public float evalInterval = 0.1f;

        // clientId -> 最後に視線が通った時刻
        readonly Dictionary<ulong, float> _lastSeen = new();
        readonly HashSet<ulong> _initiallyRejected = new();
        readonly HashSet<ulong> _pendingReconcile = new();
        float _timer;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }

            NetworkManager.OnClientConnectedCallback += HandleSynchronizedClient;
            NetworkManager.OnClientDisconnectCallback += HandleDisconnectedClient;
            // スポーン時／新規接続時のフィルタ。所有者だけは常に見える。
            NetworkObject.CheckObjectVisibility = (clientId) =>
            {
                if (clientId == OwnerClientId) return true;
                bool visible = EvaluateVisible(clientId);
                if (!visible && NetworkManager.NetworkConfig.NetworkTopology == NetworkTopologyTypes.DistributedAuthority)
                    _initiallyRejected.Add(clientId);
                return visible;
            };
        }

        void HandleSynchronizedClient(ulong clientId)
        {
            if (IsServer && _initiallyRejected.Remove(clientId)) _pendingReconcile.Add(clientId);
        }

        void HandleDisconnectedClient(ulong clientId)
        {
            _initiallyRejected.Remove(clientId);
            _pendingReconcile.Remove(clientId);
            _lastSeen.Remove(clientId);
        }
        void Update()
        {
            if (!IsServer) return;
            _timer += Time.deltaTime;
            if (_timer < evalInterval) return;
            _timer = 0f;

            var nm = NetworkManager.Singleton;
            if (nm == null) return;

            foreach (var clientId in nm.ConnectedClientsIds)
            {
                if (clientId == OwnerClientId) continue; // 自分自身は隠さない
                // 初期同期で観測者だけ登録され、クライアントに生成されなかった場合を整合する。
                if (_pendingReconcile.Remove(clientId) && NetworkObject.IsNetworkVisibleTo(clientId))
                {
                    NetworkObject.NetworkHide(clientId);
                    continue;
                }

                bool visibleNow = EvaluateVisible(clientId);
                if (visibleNow) _lastSeen[clientId] = Time.time;

                bool keep = visibleNow ||
                            (_lastSeen.TryGetValue(clientId, out float t) && Time.time - t < graceSeconds);

                bool currentlyShown = NetworkObject.IsNetworkVisibleTo(clientId);
                if (keep && !currentlyShown) NetworkObject.NetworkShow(clientId);
                else if (!keep && currentlyShown) NetworkObject.NetworkHide(clientId);
            }
        }

        // clientId のプレイヤーから、この対象へ視線が通るか。
        bool EvaluateVisible(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            var observer = nm.SpawnManager.GetPlayerNetworkObject(clientId);
            if (observer == null) return false;

            Vector3 eye = observer.transform.position + Vector3.up * eyeHeight;
            Vector3 baseTarget = transform.position + Vector3.up * eyeHeight;

            if (Vector3.Distance(eye, baseTarget) > maxDistance) return false;

            // 中心 + 追加サンプル。1 点でも通れば「見えている」。
            if (LineClear(eye, baseTarget)) return true;
            for (int i = 1; i <= extraSamplePairs; i++)
            {
                float o = 0.35f * i;
                if (LineClear(eye, baseTarget + Vector3.up * o)) return true;
                if (LineClear(eye, baseTarget - Vector3.up * o)) return true;
                if (i == 2)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, (baseTarget - eye)).normalized * o;
                    if (LineClear(eye, baseTarget + right)) return true;
                    if (LineClear(eye, baseTarget - right)) return true;
                }
            }
            return false;
        }

        bool LineClear(Vector3 from, Vector3 to)
            => !Physics.Linecast(from, to, wallMask, QueryTriggerInteraction.Ignore);

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleSynchronizedClient;
                NetworkManager.OnClientDisconnectCallback -= HandleDisconnectedClient;
                if (NetworkObject != null) NetworkObject.CheckObjectVisibility = null;
            }
        }
    }
}




