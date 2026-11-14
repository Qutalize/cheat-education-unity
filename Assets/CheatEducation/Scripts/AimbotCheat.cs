using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

namespace CheatEducation
{
    // レッスン02: エイムボット（サイレント＝視点を動かさず弾を敵へ吸わせる本物の方式）。
    //
    // このゲームの発射の仕組み（実コードで確認）:
    //   WeaponController.HandleFirePressed / ModularWeapon.Update は、弾を撃つ方向を
    //     fireDirection = (AimController.PreciseAimTargetPosition - muzzle).normalized
    //   で計算する。PreciseAimTargetPosition は AimController がカメラのレイキャストから求めて
    //   NetworkVariable(m_NetworkedPreciseAimPosition) に入れている「照準の当たり先」。
    //
    // 原理（本物）: その照準の当たり先を、毎フレーム「最寄りの敵の胸」に書き換える。
    //   すると発射方向が muzzle→敵になり、HitscanShooting のレイが敵に当たる。
    //   ★視点（カメラ）は一切動かさないので「サイレントエイムボット」。実際のメモリ書換系
    //     エイムボットと同じ発想（照準ターゲット座標を上書き）。ノースプレッド併用で必中。
    //
    // ★ NetworkVariable への書込はホスト（Start Host）で確実。オーナーのみ有効。
    //
    // 使い方: 通常どおり L2 で構え、R2/左クリックで撃つ。撃つ瞬間に照準が敵へ吸着する。
    [DisallowMultipleComponent]
    public class AimbotCheat : MonoBehaviour, ICheat
    {
        [Range(5f, 300f)] public float range = 150f;
        [Range(5f, 180f)] public float fovCone = 90f;      // カメラ前方からこの角度内の敵のみ
        public bool onlyWhileFiring = true;                 // 撃っている間だけ吸着（自然に見せる）
        public bool requireLineOfSight = false;             // true=壁越しの敵は狙わない

        bool _enabled;
        string _status = "";

        public string DisplayName => "02 エイムボット（サイレント）";
        public bool CheatEnabled { get => _enabled; set => _enabled = value; }

        static bool FireHeld()
        {
            var m = Mouse.current; if (m != null && m.leftButton.isPressed) return true;
            var g = Gamepad.current; if (g != null && g.rightTrigger.ReadValue() > 0.3f) return true;
            return false;
        }

        void LateUpdate()
        {
            if (!_enabled) { return; }
            _status = "";

            var reg = NetPlayerRegistry.Instance;
            var cam = Camera.main;
            var aim = CheatUtil.LocalAimController();
            if (reg == null || cam == null || aim == null) { _status = "準備待ち"; return; }

            var aimVar = CheatUtil.GetPreciseAimVar(aim);
            if (aimVar == null) { _status = "照準変数を取得できない"; return; }

            if (onlyWhileFiring && !FireHeld()) { _status = "待機（撃つと吸着）"; return; }

            // 最寄り＆最も正面の敵を選ぶ
            Vector3 eye = cam.transform.position;
            Vector3 fwd = cam.transform.forward;
            NetPlayerRegistry.Target best = null;
            float bestScore = float.MaxValue;
            foreach (var t in reg.Targets)
            {
                if (t == null || !t.Alive) continue;
                Vector3 to = t.AimPoint - eye;
                float d = to.magnitude;
                if (d > range || d < 0.01f) continue;
                float ang = Vector3.Angle(fwd, to);
                if (ang > fovCone) continue;
                if (requireLineOfSight &&
                    Physics.Linecast(eye, t.AimPoint, out var hit, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (t.netObj == null || !hit.transform.IsChildOf(t.netObj.transform)) continue;
                }
                float score = ang + d * 0.1f;   // 正面かつ近いほど良い
                if (score < bestScore) { bestScore = score; best = t; }
            }

            if (best == null) { _status = "対象なし"; return; }

            try
            {
                aimVar.Value = best.AimPoint;   // ★照準の当たり先を敵の胸へ上書き＝サイレントエイム
                _status = $"吸着中 → {best.Name}";
            }
            catch { _status = "書込権限なし（ホストで実行）"; }
        }

        void OnDisable() => _enabled = false;

        public void DrawCheatOptions()
        {
            var s = new GUIStyle(GUI.skin.label){fontSize=11};
            onlyWhileFiring = GUILayout.Toggle(onlyWhileFiring, " 撃っている間だけ吸着");
            requireLineOfSight = GUILayout.Toggle(requireLineOfSight, " 見えている敵のみ（OFF=壁越しも）");
            GUILayout.Label($"射程 {range:F0}m / コーン {fovCone:F0}°", s);
        }
        public string StatusLine() => _enabled ? _status : null;
    }
}
