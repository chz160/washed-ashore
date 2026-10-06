// Technical-artist R2 budget knobs for the Bells Bend tiles (Windows, 1080p, >= 60 FPS walking park -> ridge).
// BellsBendGround.ApplyRenderSettings writes them to every tile; the FPS pass tunes only here.
public static class BellsBendPerf
{
    public const float PixelError = 5f;            // heightmap LOD tolerance (px)
    public const float BasemapDistance = 250f;     // beyond this the splat is drawn from the baked basemap
    public const float TreeDistance = 450f;        // trees culled beyond (fog hides the edge)
    public const float TreeBillboardDistance = 80f; // only billboard-capable trees use it; LODGroups do their own
    public const int MaxFullLodTrees = 50;
    public const float DetailDistance = 70f;
    public const float DetailDensity = 1f;
}
