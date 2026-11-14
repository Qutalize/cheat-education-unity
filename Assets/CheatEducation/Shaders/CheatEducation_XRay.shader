// ウォールハック（X-Ray）の実体。
//
// ポイントは1行:  ZTest Always
//   通常のマテリアルは ZTest LEqual（＝手前に何か描かれていたら描かない）。
//   Always にすると「壁より奥でも必ず描く」＝壁を無視して見える。
//   本体プロジェクト(C++/DX12)の m_enemyDepthAlwaysPSO と同じ。
//
// URP 17 向けの最小 Unlit。LightMode タグを付けない＝URP は SRPDefaultUnlit として描画する。
Shader "CheatEducation/XRay"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.15, 0.15, 0.65)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always      // ← ウォールハックの核心
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                return _Color;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
