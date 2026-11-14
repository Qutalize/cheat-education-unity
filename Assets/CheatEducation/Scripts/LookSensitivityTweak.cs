using UnityEngine;
using Blocks.Gameplay.Core;

namespace CheatEducation
{
    // ① 視点感度の調整（教材の使い勝手向上・チートではない）。
    //
    // このゲームの視点回転は CoreCameraController.SetLookInput で
    //   角度 += lookInput * m_LookSensitivity
    // と計算される。既定 m_LookSensitivity=1.0 はスティック(-1..1)には低すぎるので、
    //   公開 API SetLookSensitivity() で毎フレーム上書きして感度を上げる。
    //
    // ★ 感度はマウスとスティックで共通。スティック主体なら 2〜3 が快適。
    //   マウスで速すぎると感じたら Inspector の sensitivity を下げる。
    [DisallowMultipleComponent]
    public class LookSensitivityTweak : MonoBehaviour
    {
        [Tooltip("視点感度（元は1.0。スティックなら3〜4が快適）")]
        [Range(0.3f, 10f)] public float sensitivity = 3.5f;

        CoreCameraController _cam;

        void Update()
        {
            if (_cam == null)
            {
                var p = CheatUtil.LocalPlayer();
                if (p != null) _cam = p.GetComponentInChildren<CoreCameraController>(true);
            }
            // カメラモード切替で内部感度が上書きされることがあるので毎フレーム再適用
            if (_cam != null) _cam.SetLookSensitivity(sensitivity);
        }
    }
}
