using Unity.Netcode;
using UnityEngine;
using Blocks.Gameplay.Core;   // IHittable / HitInfo / CoreStatsHandler / StatKeys / ModificationSource / CorePlayerState

namespace CheatEducation
{
    // レッスン用の練習相手ボット（サーバー権威）。オンライン対戦は使わない。
    //
    // このボットは:
    //   ③ 撃たれるとダメージを受ける … IHittable を実装。プレイヤーの弾(HitscanShooting)が
    //      hittable.OnHit を呼ぶので、ゲーム本来のヒット経路でちゃんと減る。
    //   ② プレイヤーを攻撃する        … 視界内かつ射線が通るとき、プレイヤーの IHittable.OnHit を
    //      呼んで本来のダメージ処理でプレイヤー体力を削る（サーバー権威なので host で確実）。
    //   ④ 陣地から出ない              … arenaCenter からの距離を arenaRadius 内にクランプ。
    //   ⑤ 固まらない/周回しない        … 各ボットが自分の巡回中心(_home)を持ち、範囲内をランダム徘徊。
    [RequireComponent(typeof(NetworkObject))]
    public class CheatBot : NetworkBehaviour, IHittable
    {
        [Header("体力")]
        public NetworkVariable<float> Health =
            new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        public float MaxHealth = 100f;

        [Header("移動・徘徊")]
        public float moveSpeed = 2.6f;
        public float wanderRadius = 5f;
        public float turnSpeed = 8f;

        [Header("戦闘（弱め設定）")]
        public float detectRange = 15f;    // これ以内でプレイヤーを発見
        public float attackRange = 12f;    // これ以内で射撃
        public float stopRange = 7f;       // これ以内なら近づかない
        public float fireInterval = 2.2f;  // 射撃間隔（秒・長いほど弱い）
        public float damagePerShot = 3f;   // 1 発の威力（小さいほど弱い）
        public float graceSeconds = 6f;    // スポーン/リスポーン後この秒数は撃たない（準備時間）
        public LayerMask sightMask = ~0;   // 射線判定に使うレイヤー

        // 全ボット共通の「攻撃を許可するか」。CheatConsole が校正中や P トグルで false にする。
        public static bool AttacksEnabled = true;

        float _spawnTime;

        // サーバーがボット→プレイヤーのダメージを送るときの attackerId。
        // ★ホストのプレイヤーは clientId=0。ShooterHitProcessor は attackerId==自分 のヒットを無視するので、
        //   0 以外の番号にしないと host プレイヤーに当たらない。
        const ulong kBotAttackerId = 88888801;

        // サーバー専用の徘徊/戦闘状態（NetworkVariable ではない＝サーバーだけが使う）
        Vector3 _home, _target, _arenaCenter;
        float _arenaRadius = 18f;
        float _fireTimer;
        float _respawnTimer = -1f;
        bool _configured;

        // spawner から生成直後に呼ぶ（サーバーのみ）
        public void ConfigureServer(Vector3 arenaCenter, float arenaRadius, float speed)
        {
            _arenaCenter = arenaCenter;
            _arenaRadius = Mathf.Max(3f, arenaRadius);
            moveSpeed = speed;
            _configured = true;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) { enabled = false; return; }
            _home = transform.position;
            if (!_configured) _arenaCenter = _home;
            Health.Value = MaxHealth;
            _spawnTime = Time.time;
            PickWanderTarget();
        }

        void Update()
        {
            if (!IsServer) return;

            // 死亡→一定時間後にリスポーン（撃破を何度も試せるように）
            if (_respawnTimer > 0f)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f) Respawn();
                return;
            }

            var player = FindNearestPlayer(out float dist);
            bool engaging = player != null && dist <= detectRange && HasLineOfSight(player);

            if (engaging)
            {
                // プレイヤーの方を向く
                FaceTowards(player.transform.position);
                // 近すぎなければ接近
                if (dist > stopRange)
                    MoveTowards(player.transform.position);
                // 射撃（攻撃許可中＆スポーン猶予を過ぎている時だけ）
                bool mayFire = AttacksEnabled && (Time.time - _spawnTime) >= graceSeconds;
                _fireTimer -= Time.deltaTime;
                if (mayFire && dist <= attackRange && _fireTimer <= 0f)
                {
                    _fireTimer = fireInterval;
                    ShootAt(player);
                }
            }
            else
            {
                // 徘徊
                Vector3 flat = new Vector3(_target.x, transform.position.y, _target.z);
                if ((flat - transform.position).sqrMagnitude < 0.6f * 0.6f) PickWanderTarget();
                MoveTowards(_target);
                FaceTowards(_target);
            }

            ClampToArena();
            SnapToGround();
        }

        // ---- 移動系 ----
        void MoveTowards(Vector3 worldPos)
        {
            Vector3 to = new Vector3(worldPos.x, transform.position.y, worldPos.z) - transform.position;
            if (to.sqrMagnitude < 0.0001f) return;
            transform.position += to.normalized * moveSpeed * Time.deltaTime;
        }

        void FaceTowards(Vector3 worldPos)
        {
            Vector3 dir = new Vector3(worldPos.x, transform.position.y, worldPos.z) - transform.position;
            if (dir.sqrMagnitude < 0.0001f) return;
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), turnSpeed * Time.deltaTime);
        }

        void PickWanderTarget()
        {
            var r = Random.insideUnitCircle * wanderRadius;
            _target = _home + new Vector3(r.x, 0f, r.y);
        }

        void ClampToArena()
        {
            Vector3 flat = transform.position - _arenaCenter;
            flat.y = 0f;
            if (flat.magnitude > _arenaRadius)
            {
                Vector3 clamped = _arenaCenter + flat.normalized * _arenaRadius;
                transform.position = new Vector3(clamped.x, transform.position.y, clamped.z);
                PickWanderTarget();  // 壁際で張り付かないよう目標を取り直す
            }
        }

        void SnapToGround()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 3f, Vector3.down, out var hit, 8f, sightMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<CheatBot>() == null &&
                    hit.collider.GetComponentInParent<NetworkObject>()?.IsPlayerObject != true)
                    transform.position = new Vector3(transform.position.x, hit.point.y + 0.05f, transform.position.z);
            }
        }

        // ---- 戦闘系 ----
        NetworkObject FindNearestPlayer(out float dist)
        {
            dist = float.MaxValue;
            NetworkObject best = null;
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.SpawnManager == null) return null;
            foreach (var no in nm.SpawnManager.SpawnedObjectsList)
            {
                if (no == null || !no.IsSpawned || !no.IsPlayerObject) continue;
                float d = Vector3.Distance(transform.position, no.transform.position);
                if (d < dist) { dist = d; best = no; }
            }
            return best;
        }

        Vector3 Eye => transform.position + Vector3.up * 1.4f;

        bool HasLineOfSight(NetworkObject player)
        {
            Vector3 tp = player.transform.position + Vector3.up * 1.2f;
            if (Physics.Linecast(Eye, tp, out var hit, sightMask, QueryTriggerInteraction.Ignore))
                return hit.transform.IsChildOf(player.transform) || hit.transform == player.transform;
            return true; // 障害物に当たらなければ見えている
        }

        void ShootAt(NetworkObject player)
        {
            // プレイヤーの IHittable にゲーム本来のヒットを送る＝正規のダメージ処理で体力が減る
            var hittable = player.GetComponentInChildren<IHittable>();
            if (hittable == null) return;
            Vector3 tp = player.transform.position + Vector3.up * 1.2f;
            var info = new HitInfo
            {
                amount = damagePerShot,
                hitPoint = tp,
                hitNormal = (Eye - tp).normalized,
                attackerId = kBotAttackerId,   // 0 以外（host プレイヤーに当てるため）
                impactForce = (tp - Eye).normalized * 20f
            };
            hittable.OnHit(info);   // サーバー=Authority なので即 HandleHit される
        }

        // ---- 被ダメージ（IHittable）: プレイヤーの弾がこれを呼ぶ ----
        public void OnHit(HitInfo info)
        {
            if (HasAuthority) HandleHitServer(info);
            else SubmitHitRpc(info);
        }

        [Rpc(SendTo.Authority)]
        public void SubmitHitRpc(HitInfo info, RpcParams rpcParams = default) => HandleHitServer(info);

        void HandleHitServer(HitInfo info)
        {
            if (!IsServer || _respawnTimer > 0f) return;
            Health.Value = Mathf.Max(0f, Health.Value - info.amount);
            if (Health.Value <= 0f)
            {
                _respawnTimer = 3f;   // 3 秒後に復活（繰り返し撃破を試せる）
            }
        }

        void Respawn()
        {
            _respawnTimer = -1f;
            var r = Random.insideUnitCircle * (_arenaRadius * 0.7f);
            transform.position = _arenaCenter + new Vector3(r.x, 1f, r.y);
            _home = transform.position;
            Health.Value = MaxHealth;
            PickWanderTarget();
        }
    }
}
