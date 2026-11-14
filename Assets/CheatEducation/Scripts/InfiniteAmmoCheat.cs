using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Shooter;

namespace CheatEducation
{
    // レッスン03: 無限弾（リロード不要）。
    //
    // 原理（本物）: 弾数は AmmoHandler の private NetworkVariable<int> m_CurrentClipAmmo に入っている。
    //   これを毎フレーム clipSize に戻せば、撃っても減らない＝無限弾。
    //   ＝ Cheat Engine で弾数アドレスを最大値に freeze するのと同じ。
    //
    //   NetworkVariable を書き換えるのは「メモリ上の同期変数を直接いじる」現実の手口に対応。
    //   このゲームはオーナー（＝自分）が弾数を書ける設計なのでそのまま通る（ホスト時は確実）。
    [DisallowMultipleComponent]
    public class InfiniteAmmoCheat : MonoBehaviour, ICheat
    {
        bool _enabled;
        int _lastCount;

        public string DisplayName => "03 無限弾";
        public bool CheatEnabled { get => _enabled; set => _enabled = value; }

        void Update()
        {
            if (!_enabled) return;
            _lastCount = 0;
            foreach (var a in CheatUtil.OwnedAmmoHandlers())
            {
                var v = CheatUtil.GetClipAmmoVar(a);
                int clip = CheatUtil.GetClipSize(a);
                if (v == null || clip <= 0) continue;
                try
                {
                    // 減っていたら満タンに戻す（毎フレームの freeze）
                    if (v.Value < clip) v.Value = clip;
                    _lastCount++;
                }
                catch { /* 書込権限が無い場合（非ホストのクライアントなど）は無視 */ }
            }
        }

        void OnDisable() => _enabled = false;

        public void DrawCheatOptions() { }
        public string StatusLine() => _enabled ? (_lastCount > 0 ? $"弾数を固定中（{_lastCount} 武器）" : "武器なし/権限なし") : null;
    }
}
