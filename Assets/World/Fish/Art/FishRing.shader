// Surface signs over the Bells Bend water (fish research §2.5; ruling/fish-roster-look L4): flat quads drawn after the water
// and the fish (Transparent+20). Visual only, never part of f(x, z, t) (water TD note §2.5). Far legibility
// (ruling/fish-sign-legibility-distance): with distance a sign keeps a minimum on-screen band thickness, breaks into faint
// arcs and darkens/flattens the water inside it (a disturbance of the surface pattern), with contrast rising to _FarMaxAlpha,
// still capped at the scum colour; no glint, no crisp line. Up close (< _FarRange.x) the L4 look is unchanged. No _Time: the caller sets each
// sign's age as _Radius (leading ring, fraction of the quad half size) and _Strength (fade), per instance.
// L4: rings are soft (wide gaussian bands, never a one-pixel line), slightly irregular (radius wobbles with angle by a
// per-sign seed) and fade as they expand. Modes: 0 = concentric rings (dimple, rise, swirl, roll, jump); 1 = V wake
// behind the heading (+v), Kelvin half-angle 19.5 deg; 2 = nervous water / busting: scattered small rings.
// A crest catches the overcast sky (lighter, capped at the scum colour), the trough reads darker. No specular, emissive or rim.
Shader "WashedAshore/FishRing"
{
    Properties
    {
        _Radius ("Leading ring radius (fraction of quad half size)", Range(0, 1)) = 0.5
        _Strength ("Strength 0..1 (fades with age)", Range(0, 1)) = 1
        _Seed ("Irregularity seed", Float) = 0
        _Mode ("0 rings, 1 wake, 2 nervous water", Float) = 0
        _RingCount ("Rings (1-3)", Range(1, 3)) = 2
        _RingWidth ("Ring band width (fraction)", Range(0.02, 0.3)) = 0.13
        _RingGap ("Gap between trailing rings (fraction)", Range(0, 0.5)) = 0.26
        _LightColor ("Lit edge (sRGB, capped at the scum colour)", Color) = (0.659, 0.624, 0.518, 1)
        _DarkColor ("Trough", Color) = (0.20, 0.20, 0.13, 1)
        _MaxAlpha ("Max alpha", Range(0, 1)) = 0.24
        [Header(Distance legibility, ruling fish sign legibility distance)]
        _FarRange ("Near look up to, full far look at (m from the camera)", Vector) = (15, 60, 0, 0)
        _FarMaxAlpha ("Max alpha when far (still capped at the scum colour)", Range(0, 1)) = 0.42
        _MinBandPixels ("Minimum on-screen band thickness (px)", Range(0, 4)) = 1.6
        _PatchStrength ("Far disturbance patch: flattened, darker water inside the spreading sign", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+20" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FishRing"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex RingVertex
            #pragma fragment RingFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _RingWidth, _RingGap, _MaxAlpha, _FarMaxAlpha, _MinBandPixels, _PatchStrength;
                float4 _FarRange;
                half4 _LightColor, _DarkColor;
            CBUFFER_END

            UNITY_INSTANCING_BUFFER_START(RingProps)
                UNITY_DEFINE_INSTANCED_PROP(float, _Radius)
                UNITY_DEFINE_INSTANCED_PROP(float, _Strength)
                UNITY_DEFINE_INSTANCED_PROP(float, _Seed)
                UNITY_DEFINE_INSTANCED_PROP(float, _Mode)
                UNITY_DEFINE_INSTANCED_PROP(float, _RingCount)
            UNITY_INSTANCING_BUFFER_END(RingProps)

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float clipZ : TEXCOORD1; float3 positionWS : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings RingVertex(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv * 2.0 - 1.0;
                o.clipZ = p.positionCS.z;
                o.positionWS = p.positionWS;
                return o;
            }

            float Hash1(float x) { return frac(sin(x * 127.1) * 43758.5453); }

            // Lit crest and dark trough of a soft band at signed distance d from a ring (positive = outside).
            void Band(float d, float w, float k, inout float lit, inout float dark)
            {
                lit += k * exp(-d * d / (w * w));
                float dt = d + 1.3 * w;
                dark += k * exp(-dt * dt / (w * w));
            }

            half4 RingFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float radius = UNITY_ACCESS_INSTANCED_PROP(RingProps, _Radius);
                float strength = UNITY_ACCESS_INSTANCED_PROP(RingProps, _Strength);
                float seed = UNITY_ACCESS_INSTANCED_PROP(RingProps, _Seed);
                float mode = UNITY_ACCESS_INSTANCED_PROP(RingProps, _Mode);
                int count = (int)UNITY_ACCESS_INSTANCED_PROP(RingProps, _RingCount);
                float2 uv = i.uv;
                float r = length(uv);
                float ang = atan2(uv.y, uv.x);
                // Irregular edge: three low harmonics with seeded phases, about +-6% of the radius.
                float wob = 0.035 * sin(3.0 * ang + 6.28 * Hash1(seed)) + 0.025 * sin(5.0 * ang + 6.28 * Hash1(seed + 1.7))
                          + 0.015 * sin(2.0 * ang + 6.28 * Hash1(seed + 3.1));
                // Far = 0 near (the L4 look, unchanged) to 1 at _FarRange.y metres.
                float far = smoothstep(_FarRange.x, _FarRange.y, length(GetCameraPositionWS() - i.positionWS));
                float w = _RingWidth * (0.8 + 0.6 * radius); // bands widen as they spread
                // Minimum on-screen thickness: at grazing angles a band would flatten under a pixel and vanish.
                w = max(w, _MinBandPixels * fwidth(r) * far);
                // Far: the rings break into faint arcs (seeded), never a crisp continuous line.
                float arcs = lerp(1.0, smoothstep(-0.2, 0.6, sin(4.0 * ang + 6.28 * Hash1(seed + 5.3)) + 0.4 * sin(7.0 * ang + 6.28 * Hash1(seed + 8.1))), far);
                float lit = 0.0, dark = 0.0;

                if (mode < 0.5)
                {
                    for (int k = 0; k < 3; k++)
                    {
                        if (k >= count) break;
                        float rk = radius - k * _RingGap;
                        if (rk <= 0.02) break;
                        Band(r - rk * (1.0 + wob), w, (1.0 - 0.3 * k) * arcs, lit, dark);
                    }
                }
                else if (mode < 1.5)
                {
                    // V wake: apex at the front (+v), arms trailing back at the Kelvin half-angle; fades toward the back.
                    float along = 0.9 - uv.y;                    // distance behind the apex
                    float arm = abs(uv.x) - along * 0.354;       // tan(19.5 deg)
                    float fadeBack = saturate(1.0 - along / 1.8) * step(0.0, along);
                    Band(arm * (1.0 + wob), w * 0.8, fadeBack, lit, dark);
                }
                else
                {
                    // Nervous water / busting: small rings in a 4x4 jittered grid, each at its own age.
                    float2 g = (uv * 0.5 + 0.5) * 4.0;
                    float2 c = floor(g);
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            float2 cell = c + float2(dx, dy);
                            float h = Hash1(dot(cell, float2(17.0, 31.0)) + seed);
                            float2 centre = cell + 0.5 + (float2(Hash1(h * 91.0), Hash1(h * 53.0)) - 0.5) * 0.6;
                            float age = frac(radius * 1.7 + h);
                            float rr = length(g - centre) - 0.15 - 0.45 * age;
                            float k2 = (1.0 - age) * step(0.35, h);
                            lit += k2 * exp(-rr * rr / 0.012);
                            dark += k2 * exp(-(rr + 0.12) * (rr + 0.12) / 0.012);
                        }
                }

                float edge = 1.0 - smoothstep(0.8, 1.0, r);
                // Far: the sign reads as a small spreading patch where the ripple/reflection is broken: the water inside the
                // leading ring is flattened toward the darker body tone, soft-edged and irregular (that's how a far rise is spotted).
                float inside = (1.0 - smoothstep(radius * 0.75, radius * (1.0 + wob), r)) * step(mode, 0.5);
                dark += inside * _PatchStrength * far * (0.7 + 0.3 * Hash1(floor(ang * 3.0) + seed));
                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0.0, 1.0, 0.0)) + mainLight.color * saturate(mainLight.direction.y);
                half3 crest = min(_LightColor.rgb * light, _LightColor.rgb);
                float wl = saturate(lit), wd = saturate(dark) * (1.0 - wl);
                half3 col = (crest * wl + _DarkColor.rgb * wd) / max(wl + wd, 1e-4);
                float a = saturate(wl + 0.6 * wd) * lerp(_MaxAlpha, _FarMaxAlpha, far) * saturate(strength) * edge;
                col = MixFog(col, ComputeFogFactor(i.clipZ));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
