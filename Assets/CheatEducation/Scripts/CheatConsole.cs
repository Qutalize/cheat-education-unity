using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CheatEducation
{
    // チート体験教材のハブ。
    //
    // ★ マウスでパネルを触ると発砲するため、操作は「ホットキー」に統一。パネルは状態表示だけ。
    //
    //   F1 パネル / F2 ESP / F3 ウォールハック / F4 エイムボット / F5 ノーリコイル /
    //   F6 ダメージ / F7 無限弾 / F8 スピード / F9 無敵 / F10 入力診断 /
    //   F11 パッド校正画面 / F12 校正ステップ確定
    //   コントローラー(ブリッジ): Create パネル / L1 ESP / R1 ウォールハック
    [DisallowMultipleComponent]
    public class CheatConsole : MonoBehaviour
    {
        public bool visibleOnStart = true;

        bool _visible, _showInputDiag;
        bool _enemyAttacksOn = true;   // P で切替
        GamepadBridge _bridge;
        LookSensitivityTweak _look;
        GUIStyle _title, _row, _hint;

        class Entry { public ICheat cheat; public Key key; public string label; }
        readonly List<Entry> _entries = new List<Entry>();

        WallhackCheat _wallhack; EspOverlayCheat _esp;

        void Awake()
        {
            _visible = visibleOnStart;
            Ensure<NetPlayerRegistry>();
            _bridge = Ensure<GamepadBridge>();
            _look = Ensure<LookSensitivityTweak>();   // ① 視点感度アップ（既定オン）

            _esp      = Ensure<EspOverlayCheat>();
            _wallhack = Ensure<WallhackCheat>();
            var aimbot = Ensure<AimbotCheat>();
            var norec  = Ensure<NoRecoilCheat>();
            var dmg    = Ensure<DamageCheat>();
            var ammo   = Ensure<InfiniteAmmoCheat>();
            var speed  = Ensure<SpeedHackCheat>();
            var god    = Ensure<GodModeCheat>();

            Add(_esp,     Key.F2, "F2 / L1");
            Add(_wallhack,Key.F3, "F3 / R1");
            Add(aimbot,   Key.F4, "F4");
            Add(norec,    Key.F5, "F5");
            Add(dmg,      Key.F6, "F6");
            Add(ammo,     Key.F7, "F7");
            Add(speed,    Key.F8, "F8");
            Add(god,      Key.F9, "F9");

            try { InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus; } catch { }
        }

        T Ensure<T>() where T : Component
        {
            var c = GetComponent<T>();
            if (c == null) c = gameObject.AddComponent<T>();
            return c;
        }
        void Add(ICheat c, Key k, string label) { if (c != null) _entries.Add(new Entry { cheat = c, key = k, label = label }); }

        void Update()
        {
            var kb = Keyboard.current;
            var gp = Gamepad.current;

            if ((kb != null && kb.f1Key.wasPressedThisFrame) || (gp != null && gp.selectButton.wasPressedThisFrame))
                _visible = !_visible;
            if (kb != null && kb.f10Key.wasPressedThisFrame) _showInputDiag = !_showInputDiag;
            if (kb != null && kb.f11Key.wasPressedThisFrame && _bridge != null) _bridge.calibrate = !_bridge.calibrate;
            if (kb != null && kb.pKey.wasPressedThisFrame) _enemyAttacksOn = !_enemyAttacksOn;

            // 視点感度の微調整: [ で下げる / ] で上げる（キーボード）
            if (_look != null && kb != null)
            {
                float d = 0f;
                if (kb.leftBracketKey.wasPressedThisFrame)  d -= 0.5f;
                if (kb.rightBracketKey.wasPressedThisFrame) d += 0.5f;
                if (d != 0f) _look.sensitivity = Mathf.Clamp(_look.sensitivity + d, 0.5f, 10f);
            }

            // 校正中は敵の攻撃を止める（起動直後の調整中に撃たれないように）。P でも手動 ON/OFF。
            bool calibrating = _bridge != null && _bridge.calibrate;
            CheatBot.AttacksEnabled = _enemyAttacksOn && !calibrating;

            // キーボードの各トグル
            if (kb != null)
                foreach (var e in _entries)
                    if (kb[e.key].wasPressedThisFrame) e.cheat.CheatEnabled = !e.cheat.CheatEnabled;

            // コントローラー: L1 ESP / R1 ウォールハック
            if (gp != null)
            {
                if (gp.leftShoulder.wasPressedThisFrame && _esp != null) _esp.CheatEnabled = !_esp.CheatEnabled;
                if (gp.rightShoulder.wasPressedThisFrame && _wallhack != null) _wallhack.CheatEnabled = !_wallhack.CheatEnabled;
            }
        }

        void OnGUI()
        {
            if (!_visible) return;
            _title ??= new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            _row   ??= new GUIStyle(GUI.skin.label) { fontSize = 12 };
            _hint  ??= new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.82f, 0.82f, 0.82f) } };

            bool calib = _bridge != null && _bridge.calibrate;
            float h = 96f + _entries.Count * 30f + (_showInputDiag ? 150f : 0f) + (calib ? 330f : 0f);
            var box = new Rect(12, 12, 460, h);
            GUI.color = new Color(0f, 0f, 0f, 0.78f); GUI.DrawTexture(box, Texture2D.whiteTexture); GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(box.x + 10, box.y + 8, box.width - 20, box.height - 16));
            GUILayout.Label("チート体験パネル", _title);

            foreach (var e in _entries) DrawRow(e);

            GUILayout.Space(4);
            var reg = NetPlayerRegistry.Instance;
            int others = reg != null ? reg.Targets.Count : 0;
            GUILayout.Label(others > 0 ? $"検出中の敵: {others} 体（ホスト={CheatUtil.IsHost}）"
                                       : "敵 未検出（Start Host → ボット待ち）", _row);
            var gp = Gamepad.current;
            GUILayout.Label($"入力 KB{(Keyboard.current!=null?"○":"×")} / パッド{(gp!=null?"○":"×")}  " +
                            (_bridge!=null?$"ブリッジ:{_bridge.Status}":""), _hint);
            GUILayout.Label("撃つには L2 で構えながら R2（このゲームの仕様）。F10 診断 / F11 校正", _hint);
            bool calibrating = _bridge != null && _bridge.calibrate;
            GUILayout.Label($"敵の攻撃: {(CheatBot.AttacksEnabled ? "ON" : "OFF")}  (P で切替" +
                            (calibrating ? " / 校正中は自動停止" : "") + ")", _hint);
            if (_look != null)
                GUILayout.Label($"視点感度: {_look.sensitivity:F1}  ([ で下げ / ] で上げ)", _hint);

            if (calib && _bridge != null) _bridge.DrawCalibration();
            if (_showInputDiag) DrawInputDiag();
            GUILayout.EndArea();
        }

        void DrawRow(Entry e)
        {
            bool on = e.cheat.CheatEnabled;
            GUILayout.BeginHorizontal();
            GUI.color = on ? new Color(0.4f,1f,0.4f) : new Color(1f,0.65f,0.65f);
            GUILayout.Label(on ? "[ON ]" : "[OFF]", _row, GUILayout.Width(42));
            GUI.color = Color.white;
            GUILayout.Label($"{e.cheat.DisplayName}  ({e.label})", _row);
            GUILayout.EndHorizontal();
            if (on) { var s = e.cheat.StatusLine(); if (!string.IsNullOrEmpty(s)) GUILayout.Label("    " + s, _hint); }
        }

        void DrawInputDiag()
        {
            GUILayout.Space(4);
            GUILayout.Label("── 入力診断（F10 で閉じる）──", _row);
            GUILayout.Label($"Gamepad.all={Gamepad.all.Count} / updateMode={InputSystem.settings.updateMode}", _hint);
            var gp = Gamepad.current;
            if (gp != null)
            {
                GUILayout.Label($"current=\"{gp.displayName}\" LS={gp.leftStick.ReadValue()} RS={gp.rightStick.ReadValue()} R2={gp.rightTrigger.ReadValue():F2}", _hint);
            }
        }
    }
}
