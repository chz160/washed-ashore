#ifndef WASHED_ASHORE_WATER_SURFACE_INCLUDED
#define WASHED_ASHORE_WATER_SURFACE_INCLUDED

// Shared water surface motion (spec W9): the HLSL mirror of w-engineer's C# WaterMotion.Offset.
// Change one, change the other; w-qa compares them at seeded (x, z, t) samples.
//   h(x, z) = sum_i A_i * sin(k_i * (d_i.x * x + d_i.z * z) - phi_i)
//   k_i = 2 pi / L_i, d_i = (sin dir, cos dir) (compass degrees, 0 = +Z)
//   phi_i = (k_i * c_i * t - phase_i) mod 2 pi, computed by C# in double and pushed each frame.
// Surface Y = _WaterLevelY + h. The shader never reads _Time or any clock; WaterBody sets every global here.
// Unused waves are all zero and add nothing.

float4 _WaterWave0, _WaterWave1, _WaterWave2, _WaterWave3;                                 // (A, k, d.x, d.z)
float4 _WaterWaveSpeedPhase0, _WaterWaveSpeedPhase1, _WaterWaveSpeedPhase2, _WaterWaveSpeedPhase3; // (c, phi, 0, 0)
float _WaterLevelY;
float _WaterFlowPhase; // two-phase flow cycle in [0, 1), wrapped in double by WaterBody; visual only, never part of h

void WaterAddWave(float4 wave, float phi, float2 xz, inout float h, inout float2 slope)
{
    float s, c;
    sincos(wave.y * (wave.z * xz.x + wave.w * xz.y) - phi, s, c);
    h += wave.x * s;
    slope += (wave.x * wave.y * c) * wave.zw; // (dh/dx, dh/dz)
}

// Height offset above _WaterLevelY at world (x, z), and its gradient for analytic normals.
float WaterOffset(float2 xz, out float2 slope)
{
    float h = 0.0;
    slope = float2(0.0, 0.0);
    WaterAddWave(_WaterWave0, _WaterWaveSpeedPhase0.y, xz, h, slope);
    WaterAddWave(_WaterWave1, _WaterWaveSpeedPhase1.y, xz, h, slope);
    WaterAddWave(_WaterWave2, _WaterWaveSpeedPhase2.y, xz, h, slope);
    WaterAddWave(_WaterWave3, _WaterWaveSpeedPhase3.y, xz, h, slope);
    return h;
}

#endif
