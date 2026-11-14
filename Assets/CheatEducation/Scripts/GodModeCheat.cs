using UnityEngine;
using Blocks.Gameplay.Core;

namespace CheatEducation
{
    // レッスン04: 無敵（体力を毎フレーム最大に戻す）。
    //
    // 原理（本物）: 体力は CoreStatsHandler の NetworkList<RuntimeStat>（オーナー書込）。
    //   毎フレーム最大値へ ModifyStat で戻せば、被弾しても実質死なない。
    //   ＝ Cheat Engine で体力アドレスを最大値に freeze するのと同じ。
    //
    // ★ このゲームは体力をオーナー（自分）が書ける素朴実装なので通る。
    //   サーバーが体力の正本を持てば無効化できる（レッスン05 の対策）。
    [DisallowMultipleComponent]
    public class GodModeCheat : MonoBehaviour, ICheat
    {
        bool _enabled;
        float _lastHp;

        public string DisplayName => "04 無敵（体力フリーズ）";
        public bool CheatEnabled { get => _enabled; set => _enabled = value; }

        void Update()
        {
            if (!_enabled) return;
            var stats = CheatUtil.LocalStats();
            if (stats == null) return;

            float cur = stats.GetCurrentValue(StatKeys.Health);
            float max = stats.GetMaxValue(StatKeys.Health);
            _lastHp = cur;
            if (max > 0f && cur < max)
            {
                // ModifyStat はオーナーのみ実行される（内部で IsOwner チェック）。自分は所有者なので通る。
                stats.ModifyStat(StatKeys.Health, max - cur, 0, ModificationSource.Direct);
            }
        }

        void OnDisable() => _enabled = false;

        public void DrawCheatOptions() { }
        public string StatusLine() => _enabled ? $"体力を最大に固定中（現在 {_lastHp:F0}）" : null;
    }
}
