using UnityEngine;
using WashedAshore.Level;

namespace WashedAshore.Fish
{
    /// <summary>
    /// Structure lookup over f-level's anchors (ruling fish-robertson-island-anchor): the anchor whose reach covers a
    /// water point (BellsBendStructureAnchors.TryNearest: water side, nearest wins), mapped to the brief's structure type
    /// through FishTuning.structures[].levelKinds, with the anchor's area (bank length x reach). The asset's union bounds
    /// reject most of the river with one test, and TryNearest rejects per anchor by its bounds, since the plan asks once
    /// per 2 m sample.
    /// </summary>
    public sealed class FishAnchorQuery
    {
        readonly BellsBendStructureAnchors anchors;
        readonly int[] typeOf;

        public FishAnchorQuery(BellsBendStructureAnchors anchors, FishTuning tuning)
        {
            this.anchors = anchors;
            typeOf = new int[anchors.anchors.Length];
            for (int a = 0; a < typeOf.Length; a++) typeOf[a] = tuning.StructureForLevelKind(anchors.anchors[a].kind.ToString());
        }

        /// <summary>Anchors whose kind the tuning doesn't map (they're ignored), for a load-time warning.</summary>
        public int Unmapped
        {
            get
            {
                int n = 0;
                foreach (int ty in typeOf) if (ty < 0) n++;
                return n;
            }
        }

        public int StructureAt(float x, float z, out float anchorArea)
        {
            anchorArea = 0f;
            var p = new Vector2(x, z);
            if (!anchors.bounds.Contains(p) || !anchors.TryNearest(p, out int hit, out _)) return -1;
            var an = anchors.anchors[hit];
            anchorArea = an.length * an.reach;
            return typeOf[hit];
        }
    }
}
