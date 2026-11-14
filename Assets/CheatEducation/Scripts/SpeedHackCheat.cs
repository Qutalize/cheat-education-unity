using UnityEngine;
using Blocks.Gameplay.Core;

namespace CheatEducation
{
    // レッスン04: スピードハック ＋ ハイジャンプ。
    //
    // 原理（本物）: 移動は CoreMovement の public フィールド moveSpeed / sprintSpeed が決める。
    //   ジャンプ高さは jumpHeight。これらを倍化すると本当に速く動け・高く跳べる。
    //   ＝ Cheat Engine で移動速度の float を書き換える古典的チート。
    //
    // ★ このゲームの NetworkTransform は AuthorityMode=Owner（＝クライアント権威）。
    //   だから自分が計算した速い移動をサーバーがそのまま信じる＝他プレイヤー画面でも速く見える。
    //   「サーバー権威 + 移動量検証」なら弾ける（レッスン04/05 の対策）。
    [DisallowMultipleComponent]
    public class SpeedHackCheat : MonoBehaviour, ICheat
    {
        [Range(1f, 8f)] public float moveMultiplier = 2.5f;
        [Range(1f, 5f)] public float jumpMultiplier = 2f;

        bool _enabled;
        CoreMovement _mv;
        float _moveOrig, _sprintOrig, _jumpOrig;
        bool _hooked;

        public string DisplayName => "04 スピードハック＋ハイジャンプ";
        public bool CheatEnabled { get => _enabled; set { if (_enabled == value) return; _enabled = value; if (_enabled) Apply(); else Restore(); } }

        void Apply()
        {
            _mv = CheatUtil.LocalMovement();
            if (_mv == null) return;
            _moveOrig = _mv.moveSpeed;
            _sprintOrig = _mv.sprintSpeed;
            _jumpOrig = _mv.jumpHeight;
            _mv.moveSpeed = _moveOrig * moveMultiplier;
            _mv.sprintSpeed = _sprintOrig * moveMultiplier;
            _mv.jumpHeight = _jumpOrig * jumpMultiplier;
            _hooked = true;
        }

        void Restore()
        {
            if (_hooked && _mv != null)
            {
                _mv.moveSpeed = _moveOrig;
                _mv.sprintSpeed = _sprintOrig;
                _mv.jumpHeight = _jumpOrig;
            }
            _hooked = false; _mv = null;
        }

        void Update()
        {
            if (!_enabled) return;
            // プレイヤーがまだ居なかった等で未フックなら遅延フック
            if (!_hooked) { Apply(); return; }
            // 値がゲーム側で戻された場合に備えて再適用
            if (_mv != null)
            {
                _mv.moveSpeed = _moveOrig * moveMultiplier;
                _mv.sprintSpeed = _sprintOrig * moveMultiplier;
                _mv.jumpHeight = _jumpOrig * jumpMultiplier;
            }
        }

        void OnDisable() { if (_enabled) { Restore(); _enabled = false; } }

        public void DrawCheatOptions()
        {
            var s = new GUIStyle(GUI.skin.label){fontSize=11};
            GUILayout.Label($"移動 x{moveMultiplier:F1} / ジャンプ x{jumpMultiplier:F1}", s);
        }
        public string StatusLine() => _enabled ? (_hooked ? $"素の速度 {_moveOrig:F1} → {_moveOrig*moveMultiplier:F1}" : "プレイヤー待ち") : null;
    }
}
