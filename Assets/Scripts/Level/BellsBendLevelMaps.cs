using UnityEngine;

namespace WashedAshore.Level
{
    // Level-side lookups for Bells Bend, written by the level build (BellsBendLevel.BuildAll).
    // Zone grid (5 m, R8): zone id per designer brief rev 2, bit 0x80 = Z3 cliff overlay.
    // Road grid (2 m, RGB24): R = road id, G = road weight * 255, B = 128 + signed shore distance (m, clamped).
    public class BellsBendLevelMaps : ScriptableObject
    {
        public const string AssetPath = "Assets/World/BellsBend/LevelMaps.asset";

        public const byte ZoneOutside = 0, ZoneRoad = 1, ZoneRoadBuffer = 2, ZoneBank = 3, ZoneRidge = 4,
            ZoneHollows = 5, ZoneFormerFields = 6, ZoneClearing = 7, ZoneNorthVista = 8, CliffBit = 0x80;
        public const byte RoadNone = 0, RoadOldHickory = 1, RoadPecanValley = 2, RoadTidwellHollow = 3, RoadCleecesFerry = 4, RoadCleecesTrack = 5;

        public static readonly string[] ZoneNames = { "Outside", "Z0_Road", "Z1_RoadBuffer", "Z2_Bank", "Z4a_Ridge", "Z4b_Hollows", "Z5_FormerFields", "Z6_Clearing", "ZN_NorthVista" };
        public static readonly string[] RoadNames = { "", "Old Hickory Blvd", "Pecan Valley Rd", "Tidwell Hollow Rd", "Cleeces Ferry Rd", "Cleeces Ferry track" };

        public Vector2 originXZ;
        public float zoneCell = 5f, roadCell = 2f;
        public int zoneW, zoneH, roadW, roadH;
        public Texture2D zoneTex, roadTex;

        byte[] zones, roads;

        void Ensure()
        {
            if (zones == null && zoneTex) zones = zoneTex.GetRawTextureData();
            if (roads == null && roadTex) roads = roadTex.GetRawTextureData();
        }

        bool Index(Vector3 p, float cell, int w, int h, out int i)
        {
            int x = Mathf.FloorToInt((p.x - originXZ.x) / cell), z = Mathf.FloorToInt((p.z - originXZ.y) / cell);
            i = z * w + x;
            return x >= 0 && z >= 0 && x < w && z < h;
        }

        byte RawZone(Vector3 p) { Ensure(); return zones != null && Index(p, zoneCell, zoneW, zoneH, out int i) ? zones[i] : ZoneOutside; }
        byte RoadByte(Vector3 p, int channel, byte fallback)
        {
            Ensure();
            return roads != null && Index(p, roadCell, roadW, roadH, out int i) ? roads[i * 3 + channel] : fallback;
        }

        public byte Zone(Vector3 world) => (byte)(RawZone(world) & 0x7F);
        public bool IsCliff(Vector3 world) => (RawZone(world) & CliffBit) != 0;
        public byte Road(Vector3 world) => RoadByte(world, 0, RoadNone);
        public float RoadWeight(Vector3 world) => RoadByte(world, 1, 0) / 255f;
        public float ShoreDistance(Vector3 world) => RoadByte(world, 2, 0) - 128f;

        public void InvalidateCache() { zones = null; roads = null; }
    }
}
