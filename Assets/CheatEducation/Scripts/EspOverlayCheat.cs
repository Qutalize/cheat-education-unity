using UnityEngine;

namespace CheatEducation
{
    // レッスン01: ESP（Extra Sensory Perception）オーバーレイ。
    //
    // 原理: NetPlayerRegistry が集めた敵の座標を Camera.main.WorldToScreenPoint で画面に投影し、
    //       枠・名前・距離・体力バーを描く。壁も深度も一切関係ない。座標さえあれば描ける。
    //
    // 対策（レッスン01 後半）: サーバーが見えない敵を送らなければ、ここに描くものが無くなる。
    [DisallowMultipleComponent]
    public class EspOverlayCheat : MonoBehaviour, ICheat
    {
        public Color boxColor = new Color(1f, 0.25f, 0.25f, 1f);
        public bool showName = true;
        public bool showDistance = true;
        public bool showHealthBar = true;
        [Range(0f, 500f)] public float maxDistance = 300f;

        bool _enabled;
        Texture2D _px;
        GUIStyle _label;

        public string DisplayName => "01 ESP（位置・体力の透視）";
        public bool CheatEnabled { get => _enabled; set => _enabled = value; }

        void Awake()
        {
            _px = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }

        void OnGUI()
        {
            if (!_enabled) return;
            var reg = NetPlayerRegistry.Instance;
            var cam = Camera.main;
            if (reg == null || cam == null) return;

            if (_label == null)
                _label = new GUIStyle(GUI.skin.label)
                { fontSize = 11, alignment = TextAnchor.LowerCenter, fontStyle = FontStyle.Bold };

            foreach (var t in reg.Targets)
            {
                if (!t.Alive) continue;
                float dist = Vector3.Distance(cam.transform.position, t.Position);
                if (dist > maxDistance) continue;

                // ワールド AABB の 8 頂点を投影して画面上の矩形を得る
                var b = t.WorldBounds();
                Vector3 mn = b.min, mx = b.max;
                Vector2 sMin = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 sMax = new Vector2(float.MinValue, float.MinValue);
                bool anyFront = false;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? mn.x : mx.x,
                                        (i & 2) == 0 ? mn.y : mx.y,
                                        (i & 4) == 0 ? mn.z : mx.z);
                    var sp = cam.WorldToScreenPoint(c);
                    if (sp.z <= 0f) continue;
                    anyFront = true;
                    sMin = Vector2.Min(sMin, new Vector2(sp.x, sp.y));
                    sMax = Vector2.Max(sMax, new Vector2(sp.x, sp.y));
                }
                if (!anyFront) continue;

                // GUI 座標は上下が反転している
                var rect = new Rect(sMin.x, Screen.height - sMax.y,
                                    Mathf.Max(6f, sMax.x - sMin.x), Mathf.Max(6f, sMax.y - sMin.y));

                DrawOutline(rect, boxColor, 1.5f);

                float textY = rect.y - 16f;
                if (showName || showDistance)
                {
                    string s = "";
                    if (showName) s += t.Name;
                    if (showDistance) s += (s.Length > 0 ? "  " : "") + $"{dist:F0}m";
                    var oc = GUI.color; GUI.color = boxColor;
                    GUI.Label(new Rect(rect.x - 60f, textY, rect.width + 120f, 15f), s, _label);
                    GUI.color = oc;
                    textY -= 6f;
                }

                if (showHealthBar && t.TryHealth(out float hp, out float max))
                {
                    float frac = Mathf.Clamp01(hp / max);
                    var barBg = new Rect(rect.x, rect.y - 5f, rect.width, 3f);
                    Fill(barBg, new Color(0f, 0f, 0f, 0.6f));
                    Fill(new Rect(barBg.x, barBg.y, barBg.width * frac, barBg.height),
                         Color.Lerp(Color.red, Color.green, frac));
                }
            }
        }

        void DrawOutline(Rect r, Color c, float w)
        {
            var oc = GUI.color; GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, w), _px);
            GUI.DrawTexture(new Rect(r.x, r.yMax - w, r.width, w), _px);
            GUI.DrawTexture(new Rect(r.x, r.y, w, r.height), _px);
            GUI.DrawTexture(new Rect(r.xMax - w, r.y, w, r.height), _px);
            GUI.color = oc;
        }

        void Fill(Rect r, Color c)
        {
            var oc = GUI.color; GUI.color = c;
            GUI.DrawTexture(r, _px);
            GUI.color = oc;
        }

        public void DrawCheatOptions()
        {
            showName = GUILayout.Toggle(showName, " 名前");
            showDistance = GUILayout.Toggle(showDistance, " 距離");
            showHealthBar = GUILayout.Toggle(showHealthBar, " 体力バー");
        }

        public string StatusLine()
        {
            var reg = NetPlayerRegistry.Instance;
            return _enabled && reg != null ? $"透視中の敵: {reg.Targets.Count} 体" : null;
        }
    }
}
