using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;   // CoreStatsHandler / CorePlayerState / StatKeys（テンプレート本体）

namespace CheatEducation
{
    // 「自分以外のプレイヤー」を集める。全チートスクリプトの共通データ源。
    //
    // 教材の要点（レッスン01）:
    //   Netcode for GameObjects は既定で、全プレイヤーの NetworkObject を全クライアントへ複製し、
    //   NetworkTransform で位置を毎tick同期する。壁の裏かどうかは見ていない。
    //   だから下のコードで「壁の向こうの敵の正確な座標・体力・名前」が普通に取れてしまう。
    //   → これが ESP / ウォールハックが成立する理由。
    [DisallowMultipleComponent]
    public class NetPlayerRegistry : MonoBehaviour
    {
        public static NetPlayerRegistry Instance { get; private set; }

        public readonly List<Target> Targets = new List<Target>();

        // NetworkObjectId -> Target（コンポーネント参照を毎フレーム取り直さないようキャッシュ）
        readonly Dictionary<ulong, Target> _cache = new Dictionary<ulong, Target>();

        public class Target
        {
            public NetworkObject netObj;
            public Transform tr;
            public CoreStatsHandler stats;   // プレイヤー用（テンプレート本体）
            public CorePlayerState state;    // プレイヤー用
            public CheatBot bot;             // ボット用（練習相手）
            public Renderer[] renderers;

            public bool IsBot => bot != null;

            public Vector3 Position => tr ? tr.position : Vector3.zero;

            public string Name
            {
                get
                {
                    if (bot != null) return netObj != null ? $"Bot {netObj.NetworkObjectId}" : "Bot";
                    if (state != null && !string.IsNullOrEmpty(state.PlayerName)) return state.PlayerName;
                    return netObj != null ? $"Client {netObj.OwnerClientId}" : "?";
                }
            }

            public bool Alive => bot != null || state == null || state.IsActive;

            // 体力: プレイヤーは CoreStatsHandler（NetworkList）、ボットは CheatBot.Health（NetworkVariable）。
            // どちらも全クライアントに同期されている＝ESP に体力を出せる。
            public bool TryHealth(out float current, out float max)
            {
                current = max = 0f;
                if (bot != null) { current = bot.Health.Value; max = bot.MaxHealth; return max > 0f; }
                if (stats == null) return false;
                current = stats.GetCurrentValue(StatKeys.Health);
                max = stats.GetMaxValue(StatKeys.Health);
                return max > 0f;
            }

            public Bounds WorldBounds()
            {
                if (renderers != null && renderers.Length > 0)
                {
                    bool has = false;
                    Bounds b = default;
                    foreach (var r in renderers)
                    {
                        if (r == null || !r.enabled) continue;
                        if (!has) { b = r.bounds; has = true; }
                        else b.Encapsulate(r.bounds);
                    }
                    if (has) return b;
                }
                // レンダラーが取れないときは人型サイズの箱で代用
                return new Bounds(Position + Vector3.up * 0.9f, new Vector3(0.8f, 1.8f, 0.8f));
            }

            public Vector3 AimPoint
            {
                get { var b = WorldBounds(); return new Vector3(b.center.x, Mathf.Lerp(b.center.y, b.max.y, 0.4f), b.center.z); }
            }
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void LateUpdate()
        {
            Targets.Clear();

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || nm.SpawnManager == null)
            {
                _cache.Clear();
                return;
            }

            var live = new HashSet<ulong>();

            // SpawnManager.SpawnedObjects = 全スポーン済み NetworkObject（このクライアントに複製されているもの）
            foreach (var no in nm.SpawnManager.SpawnedObjects.Values)
            {
                if (no == null || !no.IsSpawned) continue;

                var botComp = no.GetComponent<CheatBot>();

                // ボットでなく「自分のプレイヤー」なら除外
                if (botComp == null && (no.IsLocalPlayer || no.OwnerClientId == nm.LocalClientId)) continue;

                // 敵とみなすもの: ボット / NGO の PlayerObject / 名前に "Player" を含む NetworkObject
                bool isTarget = botComp != null || no.IsPlayerObject || no.name.Contains("Player");
                if (!isTarget) continue;

                live.Add(no.NetworkObjectId);

                if (!_cache.TryGetValue(no.NetworkObjectId, out var t) || t.tr == null)
                {
                    t = new Target
                    {
                        netObj = no,
                        tr = no.transform,
                        bot = botComp,
                        stats = botComp == null ? no.GetComponentInChildren<CoreStatsHandler>(true) : null,
                        state = botComp == null ? no.GetComponentInChildren<CorePlayerState>(true) : null,
                        renderers = no.GetComponentsInChildren<Renderer>(true),
                    };
                    _cache[no.NetworkObjectId] = t;
                }
                Targets.Add(t);
            }

            // 消えたプレイヤーをキャッシュから除去
            if (_cache.Count != live.Count)
            {
                var stale = new List<ulong>();
                foreach (var k in _cache.Keys) if (!live.Contains(k)) stale.Add(k);
                foreach (var k in stale) _cache.Remove(k);
            }
        }
    }
}
