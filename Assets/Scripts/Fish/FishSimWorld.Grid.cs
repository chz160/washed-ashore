using UnityEngine;

namespace WashedAshore.Fish
{
    public sealed partial class FishSimWorld
    {
        // Broad phase for Clear()'s body-overlap guard (f-td slot-B condition 1): live fish counting-sorted into hashed
        // buckets of a uniform grid once per tick, so a pose test looks at the 3 x 3 neighbouring cells, not every fish.
        // The cell is at least the largest pair of body lengths plus a tick's travel, so no overlapping pair is missed.
        // Hash collisions only add candidates. Flat pre-sized arrays: no allocation.
        const int Buckets = 1024;
        int[] bucketStart, bucketFish;
        float gridCell;

        void InitGrid()
        {
            float longest = 0.1f, fastest = 0f;
            foreach (var sp in t.species) { longest = Mathf.Max(longest, sp.lengthTailMax); fastest = Mathf.Max(fastest, sp.burstSpeed); }
            gridCell = 2f * longest + 2f * fastest * Step + 0.1f;
            bucketStart = new int[Buckets + 1];
            bucketFish = new int[fish.Length];
        }

        static int BucketOf(int cx, int cz) => (int)((uint)(cx * 73856093 ^ cz * 19349663) % Buckets);

        int CellX(float v) => Mathf.FloorToInt(v / gridCell);

        /// <summary>Counting sort of live fish into buckets by their position now.</summary>
        void BuildGrid()
        {
            System.Array.Clear(bucketStart, 0, bucketStart.Length);
            for (int f = 0; f < fish.Length; f++)
                if (fish[f].live) bucketStart[BucketOf(CellX(fish[f].pos.x), CellX(fish[f].pos.z)) + 1]++;
            for (int b = 0; b < Buckets; b++) bucketStart[b + 1] += bucketStart[b];
            // Fill using bucketStart as cursors, then shift back.
            for (int f = 0; f < fish.Length; f++)
                if (fish[f].live) bucketFish[bucketStart[BucketOf(CellX(fish[f].pos.x), CellX(fish[f].pos.z))]++] = f;
            for (int b = Buckets; b > 0; b--) bucketStart[b] = bucketStart[b - 1];
            bucketStart[0] = 0;
        }

        long lastOther = -1;

        /// <summary>Diagnostics for tests: the longest-stuck live fish, the fish whose body blocked it last, and the pair's poses.</summary>
        public string DebugLongestStuck()
        {
            int w = -1;
            for (int f = 0; f < fish.Length; f++) if (fish[f].live && (w < 0 || fish[f].stuck > fish[w].stuck)) w = f;
            if (w < 0) return "none";
            return $"stuck {fish[w].stuck:F2}s block {fish[w].block} mode {fish[w].mode} species {fish[w].species} other {fish[w].blockOther} :: " +
                   (fish[w].blockOther >= 0 ? DebugPair(fish[w].fishId, fish[w].blockOther) : "");
        }

        /// <summary>Diagnostics for tests: the two fishes' sim poses, and whether their unpadded XZ boxes overlap now and at prev poses.</summary>
        public string DebugPair(long idA, long idB)
        {
            int a = -1, b = -1;
            for (int f = 0; f < fish.Length; f++) { if (!fish[f].live) continue; if (fish[f].fishId == idA) a = f; if (fish[f].fishId == idB) b = f; }
            if (a < 0 || b < 0) return "pair not live";
            var A = Box(fish[a].variant, fish[a].scale, fish[a].pos, fish[a].yaw);
            var B = Box(fish[b].variant, fish[b].scale, fish[b].pos, fish[b].yaw);
            var Ap = Box(fish[a].variant, fish[a].scale, fish[a].prevPos, fish[a].prevYaw);
            var Bp = Box(fish[b].variant, fish[b].scale, fish[b].prevPos, fish[b].prevYaw);
            return $"sim A {fish[a].pos:F3} yaw {fish[a].yaw:F1} prev {fish[a].prevPos:F3}/{fish[a].prevYaw:F1} spd {fish[a].speed:F2} stuck {fish[a].stuck:F2} mode {fish[a].mode} | " +
                   $"B {fish[b].pos:F3} yaw {fish[b].yaw:F1} prev {fish[b].prevPos:F3}/{fish[b].prevYaw:F1} spd {fish[b].speed:F2} stuck {fish[b].stuck:F2} mode {fish[b].mode} | " +
                   $"overlapXZ now {A.OverlapsXZ(B)} prevA-B {Ap.OverlapsXZ(B)} A-prevB {A.OverlapsXZ(Bp)} prev-prev {Ap.OverlapsXZ(Bp)} cellA {CellX(fish[a].pos.x)},{CellX(fish[a].pos.z)} cellB {CellX(fish[b].pos.x)},{CellX(fish[b].pos.z)} grid {gridCell:F2}";
        }

        /// <summary>
        /// True if any live fish other than <paramref name="self"/> near <paramref name="centre"/> overlaps <paramref name="mine"/>.
        /// Relief (f-td C2 review 1): against a fish that is still (no step this tick, so render draws it in one place), a
        /// candidate that moves the centres apart only needs its own unpadded clearance from that fish's unpadded body:
        /// the speed padding can't hold two still neighbours apart-but-frozen.
        /// </summary>
        bool AnyOverlap(int self, in FishBox mine, Vector3 centre, float yaw, float length)
        {
            int cx = CellX(centre.x), cz = CellX(centre.z);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int b = BucketOf(cx + dx, cz + dz);
                    for (int k = bucketStart[b]; k < bucketStart[b + 1]; k++)
                    {
                        int o = bucketFish[k];
                        if (o == self || !fish[o].live) continue;
                        // Horizontal quick reject: the guard is on XZ footprints, so a fish at another depth still counts.
                        float reach = 0.5f * (length + fish[o].length) + 2f * Step * (fish[o].speed + 0.5f);
                        if (FlatSq(fish[o].pos, centre) > reach * reach * 1.5f && FlatSq(fish[o].prevPos, centre) > reach * reach * 1.5f) continue;
                        // Render interpolates both fish between their tick poses, so the other's box is padded by its own step
                        // this tick (both of its poses), and the caller pads its own by its step: the swept bodies stay apart.
                        float pad = fish[o].speed * Step;
                        lastOther = fish[o].fishId;
                        bool hit = mine.OverlapsXZ(Box(fish[o].variant, fish[o].scale, fish[o].pos, fish[o].yaw, pad));
                        if (!hit && fish[o].prevPos != fish[o].pos)
                            hit = mine.OverlapsXZ(Box(fish[o].variant, fish[o].scale, fish[o].prevPos, fish[o].prevYaw, pad));
                        if (!hit) continue;
                        bool still = fish[o].prevPos == fish[o].pos && Mathf.Approximately(fish[o].prevYaw, fish[o].yaw);
                        bool apart = FlatSq(fish[o].pos, centre) > FlatSq(fish[o].pos, fish[self].pos) + ReliefMin;
                        if (still && apart)
                        {
                            var tight = Box(fish[self].variant, fish[self].scale, centre, yaw, BoxClearance * length);
                            if (!tight.OverlapsXZ(Box(fish[o].variant, fish[o].scale, fish[o].pos, fish[o].yaw))) continue;
                        }
                        return true;
                    }
                }
            return false;
        }
    }
}
