// Far card of the lookout tower (tower-td-note section 6, item 5; director note N15: a thin pale lattice with a dark cab,
// no beacon, no oversized impostor). One quad, MC39_Impostor, standing on the tower's axis: the vertex stage turns it
// about that axis to face the camera (yaw only), so it never shows edge-on and never tilts. Alpha blended with the
// coverage baked into the atlas' far-card region; fades in with distance as the geometry LODs fade out. It is the one
// thing in the scene exempt from the 120-520 m fog: it carries its own haze toward unity_FogColor, capped below 1, so
// the tower still reads at 1 km without glowing. One pass, no depth or opaque texture (keeps the GL Core fallback simple), ZWrite off, drawn
// just after the water (Transparent+5). Nothing emissive: the lit term is the sky's ambient plus the main light.
Shader "WashedAshore/LookoutFarCard"
{
    Properties
    {
        _BaseMap ("Atlas (far-card region; alpha = pre-filtered coverage)", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Coverage ("Coverage scale", Range(0, 1.5)) = 1
        _FadeRange ("Fade in from, fully in at (m from the camera)", Vector) = (350, 520, 0, 0)
        _HazeRange ("Haze starts at, reaches its cap at (m from the camera)", Vector) = (400, 1600, 0, 0)
        _HazeMax ("Haze cap (below 1: the card never becomes pure fog)", Range(0, 0.95)) = 0.72
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+5" "IgnoreProjector" = "True" "DisableBatching" = "True" }

        Pass
        {
            Name "LookoutFarCard"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CardVertex
            #pragma fragment CardFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _Tint;
                float4 _FadeRange, _HazeRange;
                float _Coverage, _HazeMax;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float dist : TEXCOORD1; half3 light : TEXCOORD2; };

            Varyings CardVertex(Attributes v)
            {
                Varyings o;
                // the card's own frame: X across, Y up the tower's axis; scale taken from the object's matrix
                float3 originWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 upWS = TransformObjectToWorldDir(float3(0, 1, 0), false);
                float3 acrossWS = TransformObjectToWorldDir(float3(1, 0, 0), false);
                float upLen = length(upWS);
                float3 up = upWS / max(upLen, 1e-6);
                // yaw only: face the camera about the axis; straight overhead the model's own across direction is kept
                float3 toCam = _WorldSpaceCameraPos - originWS;
                toCam -= up * dot(toCam, up);
                float3 right = cross(up, toCam);
                float rl = length(right);
                right = rl > 1e-4 ? right / rl : normalize(acrossWS);
                float3 positionWS = originWS + right * (v.positionOS.x * length(acrossWS)) + up * (v.positionOS.y * upLen);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.dist = distance(_WorldSpaceCameraPos, originWS);
                // one light value for the whole card (normal = the axis), so it matches the LOD it replaces
                Light mainLight = GetMainLight();
                o.light = SampleSH(up) + mainLight.color * saturate(dot(up, mainLight.direction));
                return o;
            }

            half4 CardFragment(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _Tint;
                half3 col = tex.rgb * i.light;
                float haze = saturate((i.dist - _HazeRange.x) / max(_HazeRange.y - _HazeRange.x, 1e-3)) * _HazeMax;
                col = lerp(col, unity_FogColor.rgb, haze);
                float fade = saturate((i.dist - _FadeRange.x) / max(_FadeRange.y - _FadeRange.x, 1e-3));
                return half4(col, saturate(tex.a * _Coverage) * fade);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
