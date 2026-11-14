using Unity.Netcode;
using UnityEngine;

namespace CheatEducation.Countermeasures
{
    // レッスン04 の対策: サーバーが移動量を検証し、異常なら引き戻す（rubber-banding）。
    //
    // 前提: NetworkTransform を Server Authority にしておくのが本筋（レッスン04 対策1）。
    //   このスクリプトはその補助、または「まだ Owner Authority のまま」の状態での
    //   検知＆是正を体験するためのもの。
    //
    // 使い方: Assets/Shooter/ のプレイヤープレハブに追加。maxSpeed をゲームの
    //   正規最大速度（ダッシュ・ジャンプ込み）に少し余裕を持たせて設定。
    [RequireComponent(typeof(NetworkObject))]
    public class ServerMovementValidator : NetworkBehaviour
    {
        [Tooltip("正規の最大移動速度(m/s)。ダッシュ・落下も含めた上限")]
        public float maxSpeed = 9f;
        [Tooltip("1 tick でこの倍率までは許容（ネットワークゆらぎ・段差のため）")]
        public float tolerance = 1.6f;
        [Tooltip("この距離を超える 1 tick の移動は即テレポート判定")]
        public float teleportThreshold = 6f;
        [Tooltip("true=異常を検知したら正しい位置へ戻す / false=ログのみ")]
        public bool correct = true;

        Vector3 _lastPos;
        double _lastTime;
        bool _init;
        int _violations;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            _lastPos = transform.position;
            _lastTime = NetworkManager.ServerTime.Time;
            _init = true;
        }

        void FixedUpdate()
        {
            if (!IsServer || !_init) return;

            double now = NetworkManager.ServerTime.Time;
            float dt = Mathf.Max(0.0001f, (float)(now - _lastTime));
            Vector3 cur = transform.position;
            float moved = Vector3.Distance(_lastPos, cur);

            float allowed = maxSpeed * dt * tolerance;

            if (moved > teleportThreshold)
            {
                _violations++;
                Debug.LogWarning($"[対策] {OwnerClientId}: テレポート検知 {moved:F1}m / {dt*1000:F0}ms （通算 {_violations}）");
                if (correct) transform.position = _lastPos;
                else _lastPos = cur;
            }
            else if (moved > allowed)
            {
                _violations++;
                Debug.LogWarning($"[対策] {OwnerClientId}: 速度超過 {moved/dt:F1}m/s > 上限 {maxSpeed*tolerance:F1}m/s（通算 {_violations}）");
                if (correct)
                {
                    // 許容ぶんだけ進めて、残りは削る（rubber-band）
                    transform.position = _lastPos + (cur - _lastPos).normalized * allowed;
                    _lastPos = transform.position;
                }
                else _lastPos = cur;
            }
            else
            {
                _lastPos = cur;
            }

            _lastTime = now;
        }
    }
}
