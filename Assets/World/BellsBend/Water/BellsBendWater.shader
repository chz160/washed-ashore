// Bells Bend river water (spec W5/W9/W10; research D5 recipe; w-td rulings 1-3).
// Silty olive, opaque within ~1.5 m: Beer-Lambert over the baked bed depth (WaterMaps G), so neither
// platform needs the camera depth or opaque texture. Foam and shallows come from the LevelMaps shore
// distance copied into WaterMaps R (north of the line: from bed depth, recorded exception).
// Ripple normals are the analytic gradient of the shared f(x, z, t) in WaterSurface.hlsl, plus two
// flow-advected micro-normal layers (visual only). Sky/probe reflection with Schlick Fresnel, dulled to
// overcast. No refraction, no planar camera, no _Time (the flow phase comes from WaterBody), no inline samplers (WebGL2).
// Premultiplied output: rgb = light the surface adds, a = how much of the scene behind shows through.
Shader "WashedAshore/BellsBendWater"
{
    Properties
    {
        [Header(Absorption)]
        _Extinction ("Extinction RGB (1/m)", Vector) = (2.2, 2.4, 3.2, 0)
        _ScatterColor ("Body colour", Color) = (0.290, 0.290, 0.180, 1)
        _EdgeFade ("Edge fade depth (m)", Range(0.02, 0.5)) = 0.15
        _MinCosView ("Min view cosine for path length", Range(0.05, 1)) = 0.15

        [Header(Reflection)]
        _F0 ("Fresnel F0", Range(0, 0.1)) = 0.02
        _ReflDesat ("Reflection desaturation", Range(0, 1)) = 0.8
        _OvercastColor ("Overcast colour", Color) = (0.541, 0.565, 0.588, 1)
        _ReflTint ("Lerp to overcast", Range(0, 1)) = 0.6
        _ReflIntensity ("Reflection intensity", Range(0, 1.5)) = 0.65
        _Roughness ("Perceptual roughness", Range(0, 1)) = 0.2
        _SunSpec ("Sun specular", Range(0, 1)) = 0.15
        _SpecPower ("Sun specular power", Range(8, 512)) = 96

        [Header(Ripples and flow)]
        _WaveNormalScale ("f(x,z,t) normal scale", Range(0, 8)) = 1.5
        [NoScaleOffset] _RippleTex ("Ripple (RG slope, B foam noise, A drift noise)", 2D) = "grey" {}
        _RippleTiling ("Tiling m (layer 1, layer 2)", Vector) = (4.1, 2.3, 0, 0)
        _RippleStrength ("Micro-ripple strength", Range(0, 1)) = 0.22
        _FlowStep ("Drift per flow cycle (m); speed = step / WaterMotion flowCyclePeriod", Range(0, 2)) = 0.5
        _FlowSign ("Downstream sign (+1 clockwise round the bend)", Float) = 1
        _FlowFallback ("Flow where the shore field is flat (x, z)", Vector) = (0, -1, 0, 0)
        _FlowFarFade ("Current fades out between these distances from the bank (m)", Vector) = (60, 120, 0, 0)
        _NormalFade ("Normal fade start, end (m)", Vector) = (30, 150, 0, 0)

        [Header(Foam)]
        _FoamColor ("Scum colour", Color) = (0.604, 0.573, 0.478, 1)
        _FoamMax ("Scum never lighter than", Color) = (0.659, 0.624, 0.518, 1)
        _FoamTiling ("Scum noise tiles (m): two incommensurate layers", Vector) = (13, 16, 0, 0)
        _FoamStretch ("Scum stretch along the bank", Range(1, 8)) = 4
        _ScumBand ("Scum band: in at sd, full at sd, full to sd, out at sd", Vector) = (-3.5, -2.8, -1.4, -0.9)
        _ScumCoverage ("Scum coverage of the band", Range(0, 0.5)) = 0.33
        _DriftLine ("Drift line: centre sd, half width (m), coverage", Vector) = (-5.0, 0.7, 0.25, 0)
        _FoamSoftness ("Patch edge softness (noise units)", Range(0.01, 0.2)) = 0.11
        _FoamOpacity ("Scum opacity", Range(0, 1)) = 0.38
        _FoamFade ("Foam fade start, end (m)", Vector) = (60, 160, 0, 0)
        _SdJitter ("Shore distance jitter (m)", Range(0, 3)) = 0.6
        _DepthScum ("North-of-line scum: full below, zero at depth (m)", Vector) = (0.3, 0.8, 0, 0)

        [Header(Horizon haze)]
        _WaterHaze ("Haze: distance gate from, to (m); full below, none above view sine", Vector) = (120, 160, 0.004, 0.03)

        [Header(Underside)]
        _MurkColor ("Underside murk", Color) = (0.227, 0.227, 0.141, 1)

        [Header(Set by BellsBendWaterMaps.Bake)]
        [NoScaleOffset] _WaterMaps ("WaterMaps (R shore distance, G bed depth)", 2D) = "black" {}
        _WaterMapsST ("World xz to WaterMaps uv (scale xy, offset zw)", Vector) = (0, 0, 0, 0)
        _NorthLineZ ("North line Z", Float) = 100000
        _NeckX ("Neck centre X (between the north-line bank crossings)", Float) = 0
        _NorthBlend ("Half width of the blend across the north line (m)", Float) = 30

        [Toggle(_WATER_DEBUG_HEIGHT)] _DebugHeight ("Debug: rgb = f(x,z,t) / 0.1 m + 0.5, unlit, alpha 0", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend One SrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma shader_feature_local_fragment _WATER_DEBUG_HEIGHT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WaterSurface.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Extinction;
                half4 _ScatterColor;
                float _EdgeFade, _MinCosView;
                half _F0, _ReflDesat, _ReflTint, _ReflIntensity, _Roughness, _SunSpec, _SpecPower;
                half4 _OvercastColor;
                float _WaveNormalScale;
                float4 _RippleTiling;
                float _RippleStrength, _FlowStep, _FlowSign;
                float4 _FlowFallback, _FlowFarFade, _NormalFade;
                half4 _FoamColor, _FoamMax;
                float4 _FoamTiling;
                float _FoamStretch;
                float4 _ScumBand;
                float _ScumCoverage;
                float4 _DriftLine;
                float _FoamSoftness, _FoamOpacity;
                float4 _FoamFade;
                float _SdJitter;
                float4 _DepthScum;
                float4 _WaterHaze;
                half4 _MurkColor;
                float4 _WaterMapsST;
                float _NorthLineZ, _NeckX, _NorthBlend;
                float _DebugHeight;
            CBUFFER_END

            TEXTURE2D(_WaterMaps); SAMPLER(sampler_WaterMaps);
            TEXTURE2D(_RippleTex); SAMPLER(sampler_RippleTex);

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float clipZ : TEXCOORD1;
            };

            Varyings WaterVertex(Attributes v)
            {
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                Varyings o;
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.clipZ = p.positionCS.z; // fog per pixel: the quads are 1024 m, too big for per-vertex fog
                return o;
            }

            float ShoreDistance(float2 uv) { return SAMPLE_TEXTURE2D(_WaterMaps, sampler_WaterMaps, uv).r * 255.0 - 128.0; }

            // Two-phase flow sample (Catlike "Texture Distortion", Valve 2010): returns the blended texel.
            half4 FlowSample(float2 xz, float2 drift0, float2 drift1, float w0, float tiling, float2 jump)
            {
                half4 a = SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex, (xz - drift0) / tiling);
                half4 b = SAMPLE_TEXTURE2D(_RippleTex, sampler_RippleTex, (xz - drift1) / tiling + jump);
                return a * w0 + b * (1.0 - w0);
            }

            half4 WaterFragment(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // Underside: only seen if the camera ever clips the surface (ruling r2: it shouldn't). Flat murk.
                if (!IS_FRONT_VFACE(face, true, false)) return half4(_MurkColor.rgb, 0.0);

                float3 p = i.positionWS;
                float2 slope;
                float h = WaterOffset(p.xz, slope);
            #if defined(_WATER_DEBUG_HEIGHT)
                // W9 readback: f only (no WaterLevelY), h = (rgb - 0.5) * 0.1 m, range +-5 cm. Unlit, no fog; alpha 0
                // so the premultiplied blend writes exactly this value.
                return float4(saturate(h / 0.1 + 0.5).xxx, 0.0);
            #endif

                // Baked data: shore distance (m, + inland) and bed depth (m).
                float2 uv = p.xz * _WaterMapsST.xy + _WaterMapsST.zw;
                half2 maps = SAMPLE_TEXTURE2D(_WaterMaps, sampler_WaterMaps, uv).rg;
                float sd = maps.r * 255.0 - 128.0;
                float depth = maps.g * 4.0;
                // 0 = shore-distance driven, 1 = north channels / far water (flow from the channel rule, scum from depth).
                // Blended, not switched, so neither flow nor foam shows a seam at the fence line.
                float northW = smoothstep(_NorthLineZ - _NorthBlend, _NorthLineZ + _NorthBlend, p.z);
                float altW = max(northW, smoothstep(90.0, 100.0, abs(sd)));

                // Flow along the bank tangent (2 m forward differences of sd), slower near the bank and fading out
                // in open water. North of the line sd doesn't describe the channels: the east channel runs south into
                // the bend and the west channel north out of it (Cumberland flows clockwise round Bells Bend; OSM way 40806685).
                // Gradient from +-4 m differences (smoother direction on the 1 m-quantised field). Where the forward and
                // backward halves disagree the field bends (bank kinks): flow and scum stretch fade out there, otherwise the
                // advected normals and the squeezed scum coordinate curl into concentric rings.
                float2 du = float2(4.0 * _WaterMapsST.x, 0.0), dv = float2(0.0, 4.0 * _WaterMapsST.y);
                float2 gradF = float2(ShoreDistance(uv + du) - sd, ShoreDistance(uv + dv) - sd) * 0.25;
                float2 gradB = float2(sd - ShoreDistance(uv - du), sd - ShoreDistance(uv - dv)) * 0.25;
                float2 grad = 0.5 * (gradF + gradB);
                float gradLen = length(grad);
                float agree = smoothstep(0.5, 0.95, dot(gradF, gradB) / max(length(gradF) * length(gradB), 1e-4));
                float2 tangent = float2(-grad.y, grad.x) * (_FlowSign / max(gradLen, 1e-4));
                float2 v = lerp(_FlowFallback.xy, tangent, saturate(gradLen * 2.0));
                float2 dir = v * rsqrt(max(dot(v, v), 1e-6));
                float flowStep = _FlowStep * lerp(0.3, 1.0, saturate(-sd / 10.0)) * (1.0 - smoothstep(_FlowFarFade.x, _FlowFarFade.y, -sd)) * saturate(length(v)) * agree;
                float2 channelDir = float2(0.0, p.x < _NeckX ? _FlowSign : -_FlowSign);
                flowStep = lerp(flowStep, _FlowStep, northW);
                // Opposing directions meet at zero drift instead of snapping (and never normalise a zero vector).
                v = lerp(dir, channelDir, northW);
                float vLen = length(v);
                dir = v * rsqrt(max(dot(v, v), 1e-6));
                flowStep *= saturate(vLen);
                float phase = _WaterFlowPhase;
                float phase1 = frac(phase + 0.5);
                float w0 = 1.0 - abs(1.0 - 2.0 * phase);
                float2 drift0 = dir * (flowStep * phase), drift1 = dir * (flowStep * phase1);
                half4 r1 = FlowSample(p.xz, drift0, drift1, w0, _RippleTiling.x, float2(0.5, 0.5));
                half4 r2 = FlowSample(p.xz, drift0, drift1, w0, _RippleTiling.y, float2(0.31, 0.67));

                // Normal: analytic f gradient + micro ripples, both fading with distance.
                float3 toCam = GetCameraPositionWS() - p;
                float dist = length(toCam);
                float3 V = toCam / max(dist, 1e-4);
                float fade = 1.0 - smoothstep(_NormalFade.x, _NormalFade.y, dist);
                float2 micro = ((r1.rg - 0.5) + (r2.rg - 0.5)) * (2.0 * _RippleStrength);
                float2 s = (slope * _WaveNormalScale + micro) * fade;
                float3 N = normalize(float3(-s.x, 1.0, -s.y));

                // Beer-Lambert over the view path through the baked depth.
                float pathLen = depth / max(V.y, _MinCosView);
                float3 T = exp(-_Extinction.rgb * pathLen);
                float Tavg = dot(T, float3(1.0, 1.0, 1.0) / 3.0);

                Light mainLight = GetMainLight();
                half3 light = SampleSH(half3(0.0, 1.0, 0.0)) + mainLight.color * saturate(mainLight.direction.y);
                half3 body = _ScatterColor.rgb * light * (1.0 - T);

                // Dull overcast reflection with Schlick Fresnel.
                half NdotV = saturate(dot(N, V));
                half F = _F0 + (1.0 - _F0) * pow(1.0 - NdotV, 5.0);
                // Sky only: URP sets _GlossyEnvironmentCubeMap per camera on Forward and Forward+ alike (no local probes placed).
                half3 env = DecodeHDREnvironment(SAMPLE_TEXTURECUBE_LOD(_GlossyEnvironmentCubeMap, sampler_GlossyEnvironmentCubeMap,
                    reflect(-V, N), PerceptualRoughnessToMipmapLevel(_Roughness)), _GlossyEnvironmentCubeMap_HDR);
                env = lerp(env, dot(env, half3(0.2126, 0.7152, 0.0722)).xxx, _ReflDesat);
                env = lerp(env, _OvercastColor.rgb, _ReflTint) * _ReflIntensity;
                half3 H = normalize(mainLight.direction + V);
                half3 spec = _SunSpec * mainLight.color * pow(saturate(dot(N, H)), _SpecPower);

                half3 color = (1.0 - F) * body + F * env + spec;
                half trans = (1.0 - F) * Tavg;

                // Foam (ruling water-look-scum-horizon): filled soft patches of thresholded low-frequency noise, no lines.
                // The noise is squeezed across the bank (offset along the shore-distance gradient by sd * (stretch - 1)), so
                // patches run along the bank and flow (moment axis ratio about 4.7:1); the stretch fades out on distance-field ridges (bluff toes, neck)
                // and north of the line. Two layers on 13 m and 16 m tiles (37 degrees apart): no repeat within 10 m. Both drift with the flow.
                float2 across = grad / max(gradLen, 1e-4);
                // ...and at bank kinks (agree, above).
                float stretch = lerp(1.0, _FoamStretch, smoothstep(0.5, 0.8, gradLen) * agree * (1.0 - altW));
                float2 foamXZ = p.xz + across * (sd * (stretch - 1.0));
                half4 fa = FlowSample(foamXZ, drift0, drift1, w0, _FoamTiling.x, float2(0.0, 0.0));
                // Second layer rotated 37 degrees against the first, so neither tile's repeat lines up with the other.
                const float2x2 rot = float2x2(0.7986, -0.6018, 0.6018, 0.7986);
                half4 fb = FlowSample(mul(rot, foamXZ), mul(rot, drift0), mul(rot, drift1), w0, _FoamTiling.y, float2(0.0, 0.0));
                float scumNoise = 0.5 * (fa.b + fb.a), driftNoise = 0.5 * (fa.a + fb.b);
                float sdJ = sd + (fb.b - 0.5) * 2.0 * _SdJitter;
                // Broken band 1-3 m out from the bank (south of the line); north of the line, from depth (recorded exception).
                float sdBand = smoothstep(_ScumBand.x, _ScumBand.y, sdJ) * (1.0 - smoothstep(_ScumBand.z, _ScumBand.w, sdJ));
                float band = lerp(sdBand, 1.0 - smoothstep(_DepthScum.x, _DepthScum.y, depth), altW);
                float driftBand = (1.0 - altW) * (1.0 - smoothstep(0.0, _DriftLine.y, abs(sdJ - _DriftLine.x)));
                // The mean of two uniform noises is triangular: P(n > t) = 2 (1 - t)^2, so t = 1 - sqrt(c / 2) keeps coverage c.
                float scumT = 1.0 - sqrt(_ScumCoverage * 0.5), driftT = 1.0 - sqrt(_DriftLine.z * 0.5);
                float scum = band * smoothstep(scumT - _FoamSoftness, scumT + _FoamSoftness, scumNoise);
                float drift = driftBand * smoothstep(driftT - _FoamSoftness, driftT + _FoamSoftness, driftNoise);
                float foamMask = saturate(scum + drift) * (1.0 - smoothstep(_FoamFade.x, _FoamFade.y, dist));
                float foam = foamMask * _FoamOpacity;
                color -= spec * foamMask; // no specular on the scum
                color = lerp(color, min(_FoamColor.rgb * light, _FoamMax.rgb), foam);
                trans *= 1.0 - foam;

                // Soft contact with the bank.
                float edge = saturate(depth / _EdgeFade);
                color *= edge;
                trans = lerp(1.0, trans, edge);

                // Scene fog over what the water itself adds (the scene behind is already fogged), then the water's own
                // haze (ruling water-look-scum-horizon 2): past a 120-160 m distance gate it ramps with the view elevation (sine
                // 0.03 -> 0.004, about 1.7 -> 0.2 degrees), which is close to linear in screen rows, so far water fades into the
                // fog colour over a band instead of in the last few pixels at the fog end. Near water (< 120 m) is untouched, and
                // from the bluff tops the ramp only starts beyond ~900 m, so their view of the river keeps its olive.
                color = MixFogColor(color, unity_FogColor.rgb * (1.0 - trans), ComputeFogFactor(i.clipZ));
                float haze = smoothstep(_WaterHaze.x, _WaterHaze.y, dist) * (1.0 - smoothstep(_WaterHaze.z, _WaterHaze.w, V.y));
                trans *= 1.0 - haze;
                color = lerp(color, unity_FogColor.rgb * (1.0 - trans), haze);
                return half4(color, trans);
            }
            ENDHLSL
        }
    }
}
