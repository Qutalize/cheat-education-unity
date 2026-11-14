using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace CheatEducation
{
    // 旧 Input（DirectInput 経由。joy.cpl と同じ経路）で DualSense を読み、
    // 新 Input System の「仮想ゲームパッド」へ GamepadState として流し込むブリッジ。
    //
    // 背景: 一部の Windows / DualSense で新 Input System が入力レポートを受け取らない
    //   （F4 診断で lastUpdate 固定）。joy.cpl では動くのでこの経路で橋渡しする。
    //
    // 前提（この教材が設定済み）:
    //   - Active Input Handling = Both（★エディター再起動が必要）
    //   - InputManager.asset に CE_Axis0..9（joystick axis）
    //
    // 使い方: CheatConsole が自動で付ける。
    //   F8  … キャリブレーション画面の開閉
    //   F9  … キャリブレーションの各ステップを確定（画面の指示どおり倒して押す）
    [DisallowMultipleComponent]
    public class GamepadBridge : MonoBehaviour
    {
        [Header("軸マッピング（F8/F9 で自動設定。手動でも可）")]
        public int lxAxis = 0;  public bool lxInvert = false;
        public int lyAxis = 1;  public bool lyInvert = true;
        public int rxAxis = 2;  public bool rxInvert = false;
        public int ryAxis = 3;  public bool ryInvert = true;
        public int l2Axis = 4;  public bool l2Half = true;   // -1..1 を 0..1 に変換
        public int r2Axis = 5;  public bool r2Half = true;

        [Header("ボタン番号（旧 Input の JoystickButton。DualSense 既定）")]
        public int btnSquare = 0, btnCross = 1, btnCircle = 2, btnTriangle = 3;
        public int btnL1 = 4, btnR1 = 5, btnL2 = 6, btnR2 = 7;
        public int btnShare = 8, btnOptions = 9, btnL3 = 10, btnR3 = 11;

        [Range(0.05f, 0.4f)] public float deadzone = 0.18f;

        public bool calibrate;
        public bool Active { get; private set; }
        public string Status { get; private set; } = "未初期化";

        Gamepad _pad;
        readonly float[] _bias = new float[10];   // 各軸の中心（rest）値

        // --- キャリブレーション ---
        int _calStep = -1;   // -1 = 未開始
        static readonly string[] kCalPrompts =
        {
            "左スティックを → (右) いっぱいに倒して F12",
            "左スティックを ↑ (上/前) いっぱいに倒して F12",
            "右スティックを → (右) いっぱいに倒して F12",
            "右スティックを ↑ (上) いっぱいに倒して F12",
            "R2 を最後まで引いて F12",
            "L2 を最後まで引いて F12",
        };

        bool _prevCalibrate;

        void OnEnable()
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            if (_pad == null || !_pad.added)
                _pad = InputSystem.AddDevice<Gamepad>("CheatEdu Bridge Pad");
            LoadCal();
            _pendingRecenter = true;   // 起動直後（手を離している前提）に中心を取る
            Active = true;
            Status = "仮想パッド作成済み（旧 Input → 新 Input）";
#else
            Active = false;
            Status = "★ Active Input Handling を Both にしてエディター再起動が必要";
#endif
        }

        void OnDisable()
        {
            if (_pad != null && _pad.added) InputSystem.RemoveDevice(_pad);
            _pad = null;
            Active = false;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        static float RawAxis(int i)
        {
            if (i < 0 || i > 9) return 0f;
            try { return Input.GetAxisRaw("CE_Axis" + i); } catch { return 0f; }
        }

        float Ax(int i) => (i < 0 || i > 9) ? 0f : RawAxis(i) - _bias[i];

        static bool Btn(int i) => i >= 0 && i < 20 && Input.GetKey(KeyCode.JoystickButton0 + i);

        Vector2 DZ(Vector2 v)
        {
            float m = v.magnitude;
            if (m < deadzone) return Vector2.zero;
            float t = Mathf.Clamp01((m - deadzone) / (1f - deadzone));
            return v / m * t;
        }

        // トリガーを 0..1 に正規化。離した時の値 = 校正で取った中心(_bias)、引ききると +1 になる前提。
        //   これで「-1(離)..+1(引)」も「0(離)..1(引)」も自動で正しく 0..1 になる（half フラグ不要）。
        float Trig(int axis, bool _unused)
        {
            if (axis < 0 || axis > 9) return 0f;
            float released = _bias[axis];
            float v = (RawAxis(axis) - released) / Mathf.Max(0.001f, 1f - released);
            return Mathf.Clamp01(v);
        }

        bool _pendingRecenter;

        // 「今のスティック位置」を中心(ゼロ)として記録する。手を離した状態で呼ぶ。
        void Recenter()
        {
            for (int i = 0; i < 10; i++) _bias[i] = RawAxis(i);
            Status = "中心をリセットしました（手を離した状態で校正 F11→F12）";
        }

        void Update()
        {
            if (_pad == null || !_pad.added) return;

            // 校正画面を開いた瞬間（F11 押下の立ち上がり）＝手を離している前提で中心を取り直す。
            if (calibrate && !_prevCalibrate) _pendingRecenter = true;
            _prevCalibrate = calibrate;
            if (_pendingRecenter) { Recenter(); _pendingRecenter = false; }

            Vector2 ls = DZ(new Vector2(Ax(lxAxis) * (lxInvert ? -1f : 1f),
                                        Ax(lyAxis) * (lyInvert ? -1f : 1f)));
            Vector2 rs = DZ(new Vector2(Ax(rxAxis) * (rxInvert ? -1f : 1f),
                                        Ax(ryAxis) * (ryInvert ? -1f : 1f)));

            // ★「止めると視点も止まる/小刻み」対策: Value アクションは値が変化した時だけ再発火する
            //   ことがあるので、非ゼロの間は毎フレーム微小に揺らして常に「変化あり」にする。
            if (rs.sqrMagnitude > 0.0001f)
            {
                float j = (Time.frameCount & 1) == 0 ? 0.0007f : -0.0007f;
                rs += new Vector2(j, j);
            }

            float l2 = Trig(l2Axis, l2Half); if (l2 < 0.1f && Btn(btnL2)) l2 = 1f;
            float r2 = Trig(r2Axis, r2Half); if (r2 < 0.1f && Btn(btnR2)) r2 = 1f;

            var st = new GamepadState { leftStick = ls, rightStick = rs, leftTrigger = l2, rightTrigger = r2 };
            if (Btn(btnCross))    st = st.WithButton(GamepadButton.South);
            if (Btn(btnCircle))   st = st.WithButton(GamepadButton.East);
            if (Btn(btnSquare))   st = st.WithButton(GamepadButton.West);
            if (Btn(btnTriangle)) st = st.WithButton(GamepadButton.North);
            if (Btn(btnL1))       st = st.WithButton(GamepadButton.LeftShoulder);
            if (Btn(btnR1))       st = st.WithButton(GamepadButton.RightShoulder);
            if (Btn(btnL3))       st = st.WithButton(GamepadButton.LeftStick);
            if (Btn(btnR3))       st = st.WithButton(GamepadButton.RightStick);
            if (Btn(btnShare))    st = st.WithButton(GamepadButton.Select);
            if (Btn(btnOptions))  st = st.WithButton(GamepadButton.Start);
            // ★ LeftTrigger/RightTrigger はビット値 32/33 で WithButton の 32bit 範囲外＝例外になる。
            //   トリガーは上の st.leftTrigger/st.rightTrigger(float) で既に伝わり、テンプレートの
            //   発砲バインド(<Gamepad>/rightTrigger) は float を読んで自動的にボタン押下扱いするので、
            //   ここでボタン化する必要はない（WithButton で扱わない）。

            InputSystem.QueueStateEvent(_pad, st);

            // F12: 校正の開始 / 各ステップ確定
            if (calibrate && Input.GetKeyDown(KeyCode.F12))
            {
                if (_calStep < 0) { _calStep = 0; Status = "校正中…"; }
                else if (_calStep < kCalPrompts.Length) CommitCalStep();
            }
        }

        void CommitCalStep()
        {
            // いま最も大きく振れている軸を採用（bias を引いた実振れ量で判定）
            int best = 0; float bestAbs = 0f;
            for (int i = 0; i < 10; i++)
            {
                float v = RawAxis(i) - _bias[i];
                if (Mathf.Abs(v) > bestAbs) { bestAbs = Mathf.Abs(v); best = i; }
            }
            bool negative = (RawAxis(best) - _bias[best]) < 0f;

            // 各ステップは「+ になってほしい方向」に倒してもらう。倒した生値が負なら反転フラグを立てる。
            switch (_calStep)
            {
                case 0: lxAxis = best; lxInvert = negative; break;  // 右 = +x
                case 1: lyAxis = best; lyInvert = negative; break;  // 上/前 = +y
                case 2: rxAxis = best; rxInvert = negative; break;  // 右 = +x
                case 3: ryAxis = best; ryInvert = negative; break;  // 上 = +y
                case 4: r2Axis = best; r2Half = Mathf.Abs(RawAxis(best)) < 0.9f; break;
                case 5: l2Axis = best; l2Half = Mathf.Abs(RawAxis(best)) < 0.9f; break;
            }
            _calStep++;
            if (_calStep >= kCalPrompts.Length)
            {
                SaveCal();
                _calStep = -1;
                Status = "校正完了・保存しました";
                calibrate = false;
            }
        }

        const string kKey = "CheatEdu.GamepadCal.";
        void SaveCal()
        {
            PlayerPrefs.SetInt(kKey + "lx", lxAxis); PlayerPrefs.SetInt(kKey + "lxi", lxInvert ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "ly", lyAxis); PlayerPrefs.SetInt(kKey + "lyi", lyInvert ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "rx", rxAxis); PlayerPrefs.SetInt(kKey + "rxi", rxInvert ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "ry", ryAxis); PlayerPrefs.SetInt(kKey + "ryi", ryInvert ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "l2", l2Axis); PlayerPrefs.SetInt(kKey + "l2h", l2Half ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "r2", r2Axis); PlayerPrefs.SetInt(kKey + "r2h", r2Half ? 1 : 0);
            PlayerPrefs.SetInt(kKey + "done", 1);
            PlayerPrefs.Save();
        }
        void LoadCal()
        {
            if (PlayerPrefs.GetInt(kKey + "done", 0) != 1) return;
            lxAxis = PlayerPrefs.GetInt(kKey + "lx", lxAxis); lxInvert = PlayerPrefs.GetInt(kKey + "lxi", 0) == 1;
            lyAxis = PlayerPrefs.GetInt(kKey + "ly", lyAxis); lyInvert = PlayerPrefs.GetInt(kKey + "lyi", 1) == 1;
            rxAxis = PlayerPrefs.GetInt(kKey + "rx", rxAxis); rxInvert = PlayerPrefs.GetInt(kKey + "rxi", 0) == 1;
            ryAxis = PlayerPrefs.GetInt(kKey + "ry", ryAxis); ryInvert = PlayerPrefs.GetInt(kKey + "ryi", 1) == 1;
            l2Axis = PlayerPrefs.GetInt(kKey + "l2", l2Axis); l2Half = PlayerPrefs.GetInt(kKey + "l2h", 1) == 1;
            r2Axis = PlayerPrefs.GetInt(kKey + "r2", r2Axis); r2Half = PlayerPrefs.GetInt(kKey + "r2h", 1) == 1;
            Status = "校正データを読み込みました";
        }

        public void DrawCalibration()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUILayout.Label("── コントローラー校正（F11 で閉じる）──", st);

            GUILayout.Label(_calStep < 0
                ? "F12 で 6 ステップ校正を開始。全ての操作でまず必要。"
                : $"[{_calStep + 1}/{kCalPrompts.Length}] {kCalPrompts[_calStep]}", st);

            // 生の軸を大きく表示（どの Ax がどの操作で動くか一目で分かる＝報告にも使える）
            for (int i = 0; i < 10; i++)
            {
                float v = RawAxis(i);
                string bar = "";
                int n = Mathf.RoundToInt(Mathf.Clamp(v, -1f, 1f) * 8);
                for (int b = -8; b <= 8; b++) bar += (b == 0) ? "|" : ((b <= 0 && b >= n) || (b >= 0 && b <= n)) ? "#" : "-";
                GUILayout.Label($"Ax{i} {v: 0.00;-0.00} [{bar}]", st);
            }

            var held = new System.Text.StringBuilder();
            for (int i = 0; i < 20; i++) if (Btn(i)) held.Append(i).Append(' ');
            GUILayout.Label("押下ボタン番号: " + (held.Length == 0 ? "（なし）" : held.ToString()), st);
            GUILayout.Label($"現在割当 LS=({lxAxis}{(lxInvert ? "-" : "+")},{lyAxis}{(lyInvert ? "-" : "+")}) " +
                            $"RS=({rxAxis}{(rxInvert ? "-" : "+")},{ryAxis}{(ryInvert ? "-" : "+")}) " +
                            $"L2={l2Axis} R2={r2Axis}", st);
            GUILayout.Label("視点が上下逆なら Inspector の Ry Invert を、左右逆なら Rx Invert を切替。", st);
        }
#else
        void Update() { }
        public void DrawCalibration()
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = 11 };
            GUILayout.Label("Active Input Handling が Both になっていません。", st);
            GUILayout.Label("Project Settings > Player > Active Input Handling = Both → エディター再起動。", st);
        }
#endif
    }
}
