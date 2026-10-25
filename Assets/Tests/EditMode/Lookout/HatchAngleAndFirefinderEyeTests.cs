using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WashedAshore.Lookout;
using WashedAshore.Lookout.Editor;

namespace WashedAshore.Tests.Lookout.EditMode
{
    /// <summary>
    /// #40 director amend2 Q1 and Q2, td part 11: the hatch's open angle in LookoutTuning is the manifest's hatch.open_angle_deg (within 0.001
    /// degrees); the firefinder's eye at the sight is cab.floor_y plus the tuning eye height, with the tuned eye back, on the built tower.
    /// </summary>
    public class HatchAngleAndFirefinderEyeTests
    {
        const string TowerPrefab = "Assets/World/Lookout/LookoutTower.prefab";

        [Test]
        public void TuningOpenAngle_EqualsTheManifest_WithinOneThousandthOfADegree()
        {
            var m = LookoutManifest.Load();
            var t = AssetDatabase.LoadAssetAtPath<LookoutTuning>("Assets/Settings/LookoutTuning.asset");
            Assert.IsNotNull(t, "LookoutTuning.asset is missing");
            Assert.AreEqual(m.hatch.open_angle_deg, t.hatchOpenAngle, 0.001f, "LookoutTuning.hatchOpenAngle must equal the manifest's hatch.open_angle_deg");
        }

        [Test]
        public void FirefinderEye_IsTheCabFloorPlusTheTuningEyeHeight_AndTheTunedEyeBack()
        {
            var m = LookoutManifest.Load();
            var root = PrefabUtility.LoadPrefabContents(TowerPrefab);
            try
            {
                var ff = root.GetComponentInChildren<Firefinder>(true);
                Assert.IsNotNull(ff, "the built tower has no Firefinder");
                ff.ApplyBearing(0f);
                ff.SightPose(out Vector3 eye, out _);
                Vector3 axisFloor = ff.Alidade.position;
                float floorY = root.transform.TransformPoint(new Vector3(0f, m.cab.floor_y, 0f)).y;
                Assert.AreEqual(floorY + ff.Tuning.firefinderEyeHeight, eye.y, 0.001f, "the eye is cab.floor_y + firefinderEyeHeight");
                Vector2 back = new Vector2(eye.x - axisFloor.x, eye.z - axisFloor.z);
                Assert.AreEqual(ff.Tuning.firefinderEyeBack, back.magnitude, 0.001f, "the eye is firefinderEyeBack behind the pedestal axis");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
