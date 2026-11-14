using System.Collections.Generic;
using UnityEngine;
using Blocks.Gameplay.Shooter;

namespace CheatEducation
{
    // レッスン03: 武器ダメージ強化。
    //
    // 原理（本物）: 1 発のダメージは WeaponData.weaponDamage。この float を倍化すると、
    //   HitscanShooting が hittable.OnHit に渡す context.damage が増える＝相手が一撃で倒れる。
    //   ＝メモリ上の damage 値を書き換える古典的チートそのもの。
    //
    // ★ このゲームはホスト権威でダメージを処理する設計ではなく、撃った側（オーナー）が
    //   命中判定してダメージを送る素朴な実装。だからクライアント側の weaponDamage 改変が
    //   そのまま通る＝「サーバー権威なら防げる」ことの実例（レッスン03/05 の対策で解説）。
    [DisallowMultipleComponent]
    public class DamageCheat : MonoBehaviour, ICheat
    {
        [Range(1f, 50f)] public float multiplier = 10f;

        bool _enabled;
        readonly Dictionary<WeaponData, float> _saved = new();

        public string DisplayName => "03 ダメージ強化";
        public bool CheatEnabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; if (_enabled) Apply(); else Restore(); } }

        void Apply()
        {
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d == null || _saved.ContainsKey(d)) continue;
                _saved[d] = d.weaponDamage;
                d.weaponDamage = _saved[d] * multiplier;
            }
        }

        void Restore()
        {
            foreach (var kv in _saved) if (kv.Key != null) kv.Key.weaponDamage = kv.Value;
            _saved.Clear();
        }

        void Update()
        {
            if (!_enabled) return;
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d == null || _saved.ContainsKey(d)) continue;
                _saved[d] = d.weaponDamage;
                d.weaponDamage = _saved[d] * multiplier;
            }
        }

        void OnDisable() { if (_enabled) { Restore(); _enabled = false; } }

        public void DrawCheatOptions()
        {
            GUILayout.Label($"倍率 x{multiplier:F0}", new GUIStyle(GUI.skin.label){fontSize=11});
        }
        public string StatusLine() => _enabled ? $"x{multiplier:F0} 適用中（{_saved.Count} 武器）" : null;
    }
}
