using System.Collections.Generic;
using UnityEngine;
using Blocks.Gameplay.Shooter;

namespace CheatEducation
{
    // レッスン03: ノーリコイル ＋ ノースプレッド（完全命中）。
    //
    // 原理（本物）: 武器の挙動は WeaponData という ScriptableObject の
    //   recoil / minSpreadAngle / maxSpreadAngle / spreadIncreasePerShot が決めている。
    //   その値をゼロに書き換えると、反動も弾のばらつきも消える。
    //   ＝ Cheat Engine で「反動」「拡散」の float アドレスを 0 に固定するのと同じこと。
    //   HitscanShooting.ApplySpread は spreadAngle<=0 のとき素の方向をそのまま撃つ（＝ど真ん中）。
    //
    // 実データ: ModularWeapon が参照する WeaponData の public フィールドを直接書き換える。
    //   ON にした瞬間の値を保存し、OFF で必ず元に戻す（SO はアセットなので復元必須）。
    [DisallowMultipleComponent]
    public class NoRecoilCheat : MonoBehaviour, ICheat
    {
        bool _enabled;

        struct Saved { public Vector3 recoil; public float minSpread, maxSpread, incPerShot; }
        readonly Dictionary<WeaponData, Saved> _saved = new();

        public string DisplayName => "03 ノーリコイル＋ノースプレッド";
        public bool CheatEnabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; if (_enabled) Apply(); else Restore(); } }

        void Apply()
        {
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d == null || _saved.ContainsKey(d)) continue;
                _saved[d] = new Saved { recoil = d.recoil, minSpread = d.minSpreadAngle, maxSpread = d.maxSpreadAngle, incPerShot = d.spreadIncreasePerShot };
                d.recoil = Vector3.zero;
                d.minSpreadAngle = 0f;
                d.maxSpreadAngle = 0f;
                d.spreadIncreasePerShot = 0f;
            }
            // ★ ModularWeapon は Awake 時に SpreadHandler へ spread 値をコピーしている。
            //   撃つ前に反映させるため、武器を再初期化して SpreadHandler を作り直す。
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d != null) w.Initialize(d);
            }
        }

        void Restore()
        {
            foreach (var kv in _saved)
            {
                var d = kv.Key; if (d == null) continue;
                d.recoil = kv.Value.recoil;
                d.minSpreadAngle = kv.Value.minSpread;
                d.maxSpreadAngle = kv.Value.maxSpread;
                d.spreadIncreasePerShot = kv.Value.incPerShot;
            }
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d != null) w.Initialize(d);
            }
            _saved.Clear();
        }

        // ON 中に武器を持ち替えたら新しい WeaponData にも適用する
        void Update()
        {
            if (!_enabled) return;
            foreach (var w in CheatUtil.OwnedWeapons())
            {
                var d = CheatUtil.GetWeaponData(w);
                if (d == null || _saved.ContainsKey(d)) continue;
                _saved[d] = new Saved { recoil = d.recoil, minSpread = d.minSpreadAngle, maxSpread = d.maxSpreadAngle, incPerShot = d.spreadIncreasePerShot };
                d.recoil = Vector3.zero; d.minSpreadAngle = 0f; d.maxSpreadAngle = 0f; d.spreadIncreasePerShot = 0f;
                w.Initialize(d);
            }
        }

        void OnDisable() { if (_enabled) { Restore(); _enabled = false; } }

        public void DrawCheatOptions() { }
        public string StatusLine() => _enabled ? $"改変中の武器データ: {_saved.Count}" : null;
    }
}
