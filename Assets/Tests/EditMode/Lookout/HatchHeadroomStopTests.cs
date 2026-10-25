using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;
using WashedAshore.World;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #40 td part 4 (B): the headroom stop on the built tower prefab. A BoxCollider node HATCH_HeadroomStop, a child of the tower root, on
    /// WorldBounds, centre zero, saved disabled (Awake's Settle turns it on while Closed), height LookoutGameplaySetup.StopDepth() for the same inputs (derived, at most 0.25 m), top at the
    /// cab floor's underside (opening_min.y - cab.floor_thickness), within 1 mm.
    /// </summary>
    public class HatchHeadroomStopTests
    {
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";
        const float Tolerance = 0.001f;

        [Test]
        public void BuiltTower_HasTheHeadroomStop_AtTheRightHeight_AndTopAtTheFloorUnderside()
        {
            var root = PrefabUtility.LoadPrefabContents(TowerPrefab);
            try
            {
                var hatch = root.GetComponentInChildren<TrapDoor>(true);
                Assert.IsNotNull(hatch, "the built tower has no TrapDoor");
                var node = root.transform.Find("HATCH_HeadroomStop");
                Assert.IsNotNull(node, "HATCH_HeadroomStop is not a child of the tower root");
                var box = node.GetComponent<BoxCollider>();
                Assert.IsNotNull(box, "HATCH_HeadroomStop has no BoxCollider");
                Assert.AreEqual(WorldLayers.WorldBounds, node.gameObject.layer, "the stop is on WorldBounds");
                Assert.AreEqual(Vector3.zero, box.center, "the stop's BoxCollider centre is zero");
                Assert.IsFalse(box.enabled, "the prefab saves the stop disabled (Settle turns it on while Closed)");
                Assert.AreSame(box, hatch.HeadroomStop, "TrapDoor holds the stop");
                // One rule for the build and the check: LookoutGameplaySetup.StopDepth with the same inputs (td parts 15, 16; director amend4).
                var m = LookoutManifest.Load();
                var walk = root.GetComponent<TowerWalkZones>();
                Assert.IsNotNull(walk, "the tower has no TowerWalkZones (capsule height)");
                float want = LookoutGameplaySetup.StopDepth(hatch.Tuning, m.flights[m.flights.Length - 1], hatch.OpeningLength, m.cab.floor_thickness,
                    walk.towerHeight, LookoutGameplaySetup.StandingEyeHeight());
                Assert.LessOrEqual(want, LookoutGameplaySetup.StopDepthCeiling, "the derived depth is never above the director's 0.25 m cap");
                float height = box.size.y * Mathf.Abs(node.lossyScale.y);
                Assert.AreEqual(want, height, Tolerance, "the stop's height is StopDepth() for the same inputs");
                float underside = hatch.OpeningBounds.min.y - hatch.FloorThickness; // opening_min.y - cab.floor_thickness
                float top = node.position.y + height * 0.5f;
                Assert.AreEqual(underside, top, Tolerance, "the stop's top is the cab floor's underside");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
