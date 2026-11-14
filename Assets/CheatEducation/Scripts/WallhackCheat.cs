using System.Collections.Generic;
using UnityEngine;

namespace CheatEducation
{
    // レッスン01: ウォールハック（X-Ray シルエット）。
    //
    // 原理: 各敵プレイヤーのメッシュ（このテンプレートでは Armature_Mesh という SkinnedMeshRenderer）を
    //       複製し、マテリアルを "CheatEducation/XRay"（ZTest Always）に差し替えて毎フレーム同じ姿勢で
    //       重ね描く。壁の深度を無視するので、壁の向こうの敵の形が透けて見える。
    //
    //       ＝「敵ドローの深度ステートだけ ALWAYS にすり替える」現実のウォールハックと見た目は同じ。
    //
    // 対策（レッスン01 後半 / ServerVisibilityFilter）: サーバーが見えない敵を NetworkHide すると、
    //       NetPlayerRegistry の一覧から消える＝複製元が無くなり、シルエットも出なくなる。
    [DisallowMultipleComponent]
    public class WallhackCheat : MonoBehaviour, ICheat
    {
        public Material xrayMaterial;
        public Color color = new Color(1f, 0.2f, 0.2f, 0.65f);
        [Range(0f, 500f)] public float maxDistance = 250f;

        bool _enabled;

        class Ghost
        {
            public Transform root;
            public List<(Renderer src, MeshFilter dstFilter, MeshRenderer dstRenderer, bool skinned)> parts = new();
        }
        readonly Dictionary<ulong, Ghost> _ghosts = new();

        public string DisplayName => "01 ウォールハック（壁越しシルエット）";

        public bool CheatEnabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; if (!_enabled) ClearAll(); }
        }

        string _matSource = "未生成";

        void Awake()
        {
            if (xrayMaterial != null) { _matSource = "手動割当"; return; }

            // 1) 自作 URP シェーダー
            var sh = Shader.Find("CheatEducation/XRay");
            if (sh != null && sh.isSupported)
            {
                xrayMaterial = new Material(sh) { hideFlags = HideFlags.DontSave };
                xrayMaterial.SetColor("_Color", color);
                _matSource = "CheatEducation/XRay";
                return;
            }

            // 2) 予備: URP Unlit を透明＋ZTest Always に設定（壁越し描画）
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                var m = new Material(unlit) { hideFlags = HideFlags.DontSave };
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);   // Transparent
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty("_ZTest")) m.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                xrayMaterial = m;
                _matSource = "URP/Unlit(予備・_ZTest=Always)";
                return;
            }

            Debug.LogError("[WallhackCheat] X-Ray 用のシェーダーを用意できませんでした。" +
                           "Assets/CheatEducation/Shaders/CheatEducation_XRay.shader を確認してください。");
        }

        void OnDisable() => ClearAll();

        void LateUpdate()
        {
            if (!_enabled || xrayMaterial == null) return;
            var reg = NetPlayerRegistry.Instance;
            var cam = Camera.main;
            if (reg == null || cam == null) return;

            var live = new HashSet<ulong>();

            foreach (var t in reg.Targets)
            {
                if (t.netObj == null || !t.Alive) continue;
                ulong id = t.netObj.NetworkObjectId;
                bool near = Vector3.Distance(cam.transform.position, t.Position) <= maxDistance;

                if (!near)
                {
                    if (_ghosts.ContainsKey(id)) Remove(id);
                    continue;
                }
                live.Add(id);

                if (!_ghosts.TryGetValue(id, out var g) || g.root == null)
                {
                    g = Build(t);
                    _ghosts[id] = g;
                }
                Sync(g);
            }

            // 範囲外・消滅した敵のゴーストを掃除
            var stale = new List<ulong>();
            foreach (var k in _ghosts.Keys) if (!live.Contains(k)) stale.Add(k);
            foreach (var k in stale) Remove(k);
        }

        Ghost Build(NetPlayerRegistry.Target t)
        {
            var g = new Ghost();
            var rootGo = new GameObject($"[xray] {t.Name}") { hideFlags = HideFlags.DontSave };
            g.root = rootGo.transform;

            if (t.renderers == null) return g;
            foreach (var src in t.renderers)
            {
                if (src == null) continue;
                if (src is ParticleSystemRenderer || src is TrailRenderer || src is LineRenderer) continue;

                Mesh mesh = null;
                bool skinned = false;
                if (src is SkinnedMeshRenderer smr && smr.sharedMesh != null) { mesh = new Mesh { name = "xray_bake" }; skinned = true; }
                else if (src is MeshRenderer && src.TryGetComponent<MeshFilter>(out var srcMf) && srcMf.sharedMesh != null) mesh = srcMf.sharedMesh;
                if (mesh == null) continue;

                var partGo = new GameObject("part") { hideFlags = HideFlags.DontSave };
                partGo.transform.SetParent(g.root, false);
                var mf = partGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = partGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = xrayMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.parts.Add((src, mf, mr, skinned));
            }
            return g;
        }

        void Sync(Ghost g)
        {
            for (int i = g.parts.Count - 1; i >= 0; i--)
            {
                var (src, mf, mr, skinned) = g.parts[i];
                if (src == null || mf == null) { g.parts.RemoveAt(i); continue; }

                if (skinned && src is SkinnedMeshRenderer smr)
                {
                    smr.BakeMesh(mf.sharedMesh);   // 現在のポーズを焼き込む（ローカル空間）
                    mr.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                    mr.transform.localScale = Vector3.one;
                }
                else
                {
                    mr.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation);
                    mr.transform.localScale = src.transform.lossyScale;
                }
            }
        }

        void Remove(ulong id)
        {
            if (_ghosts.TryGetValue(id, out var g) && g.root != null) Destroy(g.root.gameObject);
            _ghosts.Remove(id);
        }

        void ClearAll()
        {
            foreach (var g in _ghosts.Values) if (g.root != null) Destroy(g.root.gameObject);
            _ghosts.Clear();
        }

        public void DrawCheatOptions()
        {
            GUILayout.Label($"表示距離 {maxDistance:F0}m");
            maxDistance = GUILayout.HorizontalSlider(maxDistance, 20f, 500f);
        }

        public string StatusLine()
        {
            if (!_enabled) return null;
            var reg = NetPlayerRegistry.Instance;
            int targets = reg != null ? reg.Targets.Count : 0;
            if (xrayMaterial == null) return "エラー: X-Ray マテリアル未生成（Console 参照）";
            if (Camera.main == null) return "エラー: Main Camera が見つからない（Tag を MainCamera に）";
            if (targets == 0) return "敵が 0（Start Host 済み？ ボットが出た？）";
            return $"マテリアル[{_matSource}]  シルエット {_ghosts.Count}/{targets} 体";
        }
    }
}
