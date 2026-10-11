// Fish seen through the Bells Bend water (fish research §1.2; greenlight/fish N1, N5; ruling/fish-roster-look L1-L3).
// Drawn after the water (Transparent+10), ZWrite On (f-td addendum 1) and blended over it. Alpha mirrors
// WashedAshore.Fish.FishMurk.Visibility term for term: path = depth / max(V.y, minCosView), Tavg = mean exp(-ext * path),
// F = Schlick(F0, V.y) on the flat normal, alpha = (1 - F) * Tavg, 1 above water (a jump); change both together.
// The water's values come in as _FishWater* globals copied from BellsBendWater.mat at run time (no literal copies).
// Colour (L3): luminance-only lighting and a scalar murk term, so every fish keeps its swatch hue and only darkens and
// takes on the water's olive with depth (the per-channel version pushed dark fins toward mauve under the blue sky ambient).
// Daylight reaching the fish is attenuated over the water above it. No reflection, specular, emissive or rim.
// Two vertex sources, chosen by _UseVat: the baked Swim VAT (instanced runtime: model-root positions, rest row 0, frames
// 1..N, per-instance phase and stroke amplitude) or the mesh as given (census prefabs, skinned). No _Time, no inline
// samplers, no depth or opaque texture.
Shader "WashedAshore/FishUnderwater"
{
    Properties
    {
        _BackColor ("Back", Color) = (0.36, 0.39, 0.25, 1)
        _BellyColor ("Belly", Color) = (0.79, 0.77, 0.63, 1)
        _FinColor ("Fins", Color) = (0.42, 0.42, 0.28, 1)
        _Mottle ("Mottle strength", Range(0, 1)) = 0
        _Silhouette ("Draw as shadow (V2): one flat dark tone, no detail", Range(0, 1)) = 0
        _Flash ("Flank flash 0..1 (lerp to the dull belly swatch)", Range(0, 1)) = 0
        _Phase ("Swim phase 0..1", Range(0, 1)) = 0
        _Amplitude ("Stroke amplitude 0..1", Range(0, 1)) = 1
        _Fade ("Visible-cap fade 0..1 (FishVisibleCap; 1 = not capped)", Range(0, 1)) = 1
        [Header(Per model, set by the bake)]
        _UseVat ("Vertices from the VAT (1) or the mesh (0)", Float) = 0
        _IsFin ("Mesh path only: this material is the fins (1) or the body (0)", Float) = 0
        [NoScaleOffset] _VatPos ("VAT positions (rgb) + packed normal (a), or quantised positions", 2D) = "black" {}
        [NoScaleOffset] _VatNrm ("VAT normals (RGBA32 fallback only)", 2D) = "black" {}
        _VatInfo ("VAT: vertices, frames, quantised (0/1), unused", Vector) = (1, 1, 0, 0)
        _VatBoxMin ("Quantisation box min (model space)", Vector) = (0, 0, 0, 0)
        _VatBoxSize ("Quantisation box size (model space)", Vector) = (1, 1, 1, 0)
        _BodyFrame ("Body centre y, body length, split above centre (fraction of length), split half width (fraction)", Vector) = (0, 1, 0, 0.03)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent+10" "IgnoreProjector" = "True" }

        Pass
        {
            Name "FishForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            // ZWrite On (f-td gate/fish-technique-addendum-1): own fins and body sort; surface signs draw after the fish. A fish
            // fading in through the visible-body cap (_Fade < 1, at most 1 s) still writes depth, so it can briefly hide a fish
            // behind it on screen; accepted by f-td (cap review) over the fin/body self-sorting errors ZWrite Off would bring back.
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex FishVertex
            #pragma fragment FishFragment
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/World/BellsBend/Water/WaterSurface.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _UseVat, _IsFin;
                float4 _VatInfo, _VatBoxMin, _VatBoxSize, _BodyFrame;
            CBUFFER_END

            // Per fish: instanced arrays under RenderMeshInstanced, plain material values otherwise.
            UNITY_INSTANCING_BUFFER_START(FishProps)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BackColor)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BellyColor)
                UNITY_DEFINE_INSTANCED_PROP(half4, _FinColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _Mottle)
                UNITY_DEFINE_INSTANCED_PROP(float, _Silhouette)
                UNITY_DEFINE_INSTANCED_PROP(float, _Flash)
                UNITY_DEFINE_INSTANCED_PROP(float, _Phase)
                UNITY_DEFINE_INSTANCED_PROP(float, _Amplitude)
                UNITY_DEFINE_INSTANCED_PROP(float, _Fade)
            UNITY_INSTANCING_BUFFER_END(FishProps)

            TEXTURE2D(_VatPos); SAMPLER(sampler_VatPos);
            TEXTURE2D(_VatNrm); SAMPLER(sampler_VatNrm);

            // The water's own look values, pushed from BellsBendWater.mat (one source).
            float4 _FishWaterExtinction; // rgb 1/m
            float _FishWaterMinCos, _FishWaterF0;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;   // r = fin region (VAT mesh from the bake: 1 fins, 0 body)
                float2 uv1 : TEXCOORD1; // x = VAT vertex index (bake)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 extra : TEXCOORD3; // x = fin region, y = clip z for fog
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float3 OctDecode(float2 e)
            {
                e = e * 2.0 - 1.0;
                float3 n = float3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
                if (n.z < 0.0) n.xy = (1.0 - abs(n.yx)) * float2(n.x >= 0.0 ? 1.0 : -1.0, n.y >= 0.0 ? 1.0 : -1.0);
                return normalize(n);
            }

            // One VAT texel: model-space position and normal of vertex v in row r (0 = rest, 1..N = Swim frames).
            void VatRow(float v, float r, out float3 p, out float3 n)
            {
                float2 uv = float2((v + 0.5) / _VatInfo.x, (r + 0.5) / (_VatInfo.y + 1.0));
                float4 t = SAMPLE_TEXTURE2D_LOD(_VatPos, sampler_VatPos, uv, 0);
                if (_VatInfo.z > 0.5)
                {
                    p = _VatBoxMin.xyz + t.rgb * _VatBoxSize.xyz;
                    n = OctDecode(SAMPLE_TEXTURE2D_LOD(_VatNrm, sampler_VatNrm, uv, 0).rg);
                }
                else
                {
                    p = t.rgb;
                    float packed = round(t.a); // two 5-bit octahedral components: hi * 32 + lo
                    n = OctDecode(float2(floor(packed / 32.0), fmod(packed, 32.0)) / 31.0);
                }
            }

            Varyings FishVertex(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 pos = v.positionOS.xyz, nrm = v.normalOS;
                if (_UseVat > 0.5)
                {
                    float frames = _VatInfo.y;
                    float f = frac(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Phase)) * frames;
                    float f0 = floor(f), f1 = fmod(f0 + 1.0, frames), w = f - f0;
                    float3 p0, n0, p1, n1, pr, nr;
                    VatRow(v.uv1.x, f0 + 1.0, p0, n0);
                    VatRow(v.uv1.x, f1 + 1.0, p1, n1);
                    VatRow(v.uv1.x, 0.0, pr, nr);
                    float amp = saturate(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Amplitude));
                    pos = pr + amp * (lerp(p0, p1, w) - pr);
                    nrm = normalize(nr + amp * (normalize(lerp(n0, n1, w)) - nr));
                }
                VertexPositionInputs p = GetVertexPositionInputs(pos);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(nrm);
                o.positionOS = pos;
                o.extra = float2(_UseVat > 0.5 ? v.color.r : _IsFin, p.positionCS.z);
                return o;
            }

            // Cheap value noise for mottle (no texture, no sampler).
            float Hash(float3 p) { return frac(sin(dot(p, float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            float ValueNoise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(lerp(Hash(i), Hash(i + float3(1, 0, 0)), f.x), lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x), lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            half4 FishFragment(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 back = UNITY_ACCESS_INSTANCED_PROP(FishProps, _BackColor).rgb;
                half3 bellyC = UNITY_ACCESS_INSTANCED_PROP(FishProps, _BellyColor).rgb;
                half3 finC = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FinColor).rgb;
                float mottleK = UNITY_ACCESS_INSTANCED_PROP(FishProps, _Mottle);
                float silhouette = saturate(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Silhouette));
                float flash = saturate(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Flash));

                float3 p = i.positionWS;
                float2 slope;
                float surface = _WaterLevelY + WaterOffset(p.xz, slope);
                float depth = max(surface - p.y, 0.0);

                float3 toCam = GetCameraPositionWS() - p;
                float3 V = toCam / max(length(toCam), 1e-4);
                float under = step(1e-4, depth);
                float pathLen = depth / max(V.y, _FishWaterMinCos);
                float3 T = exp(-_FishWaterExtinction.rgb * pathLen);
                float Tavg = dot(T, float3(1.0, 1.0, 1.0) / 3.0);
                float F = under * (_FishWaterF0 + (1.0 - _FishWaterF0) * pow(1.0 - saturate(V.y), 5.0));
                float Tdown = dot(exp(-_FishWaterExtinction.rgb * depth), float3(1.0, 1.0, 1.0) / 3.0);

                // Albedo: back / belly about the body centre in model space (fish don't roll), mottle on the back, flat fins.
                float rel = (i.positionOS.y - _BodyFrame.x) / max(_BodyFrame.y, 1e-4);
                float belly = 1.0 - smoothstep(_BodyFrame.z - _BodyFrame.w, _BodyFrame.z + _BodyFrame.w, rel);
                float mottle = (ValueNoise(i.positionOS / max(_BodyFrame.y, 1e-4) * 6.0) - 0.5) * 2.0 * mottleK * (1.0 - belly);
                half3 body = lerp(back, bellyC, belly) * (1.0 + 0.5 * mottle);
                half3 albedo = lerp(body, finC, i.extra.x);
                // V2 (L1, f-director tiers): one flat dark tone; the flash still lifts it toward the dull belly (seam contract).
                albedo = lerp(albedo, back * 0.45, silhouette);
                albedo = saturate(lerp(albedo, bellyC, flash));

                Light mainLight = GetMainLight();
                float3 N = normalize(i.normalWS);
                const half3 lumaW = half3(0.2126, 0.7152, 0.0722);
                half lightL = dot(SampleSH(N) + mainLight.color * saturate(dot(N, mainLight.direction)), lumaW);
                half3 lit = albedo * lightL * lerp(1.0, Tdown, under);

                // Through the water the frame becomes water + (1 - F) Tavg (lit - body) with the scalar Tavg (hue kept): blending
                // the lit fish at alpha (1 - F) Tavg over the water pixel (which is ~ the water's body colour) gives exactly that.
                half3 seen = lit;
                float alpha = lerp(1.0, (1.0 - F) * Tavg, under) * saturate(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Fade));

                seen = MixFog(seen, ComputeFogFactor(i.extra.y));
                return half4(seen, alpha);
            }
            ENDHLSL
        }
    }
}
