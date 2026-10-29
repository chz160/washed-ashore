using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// The tower must never store a lowered capsule as the normal one (td ruling, TowerWalkZones.cs line 61 saves
    /// PlayerController.StandingHeight). Enter the footprint crouched, leave, and the capsule is 1.80 and the stance Stand.
    /// </summary>
    public class StanceTowerTests : StanceFixture
    {
        [UnityTest, Timeout(300000)]
        public IEnumerator EnteringTheTowerCrouched_NeverLeaksTheCrouchCapsule()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            var zones = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude).FirstOrDefault(m => m.GetType().Name == "TowerWalkZones");
            Assert.IsNotNull(zones, "no TowerWalkZones in World: run WalkableTowerBuilder");
            var t = zones.GetType();
            Vector3 min = (Vector3)t.GetField("footprintMin").GetValue(zones), max = (Vector3)t.GetField("footprintMax").GetValue(zones);
            Vector3 inside = zones.transform.TransformPoint(new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f));
            Assert.IsTrue(TerrainQuery.TryGroundHeight(inside, out float ground), "no terrain under the tower footprint");
            inside.y = ground + 0.5f;
            Vector3 outside = inside + new Vector3(40f, 0f, 0f);
            Assert.IsTrue(TerrainQuery.TryGroundHeight(outside, out float outGround), "no terrain outside the tower");
            outside.y = outGround + 0.5f;

            Place(outside, 0f);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(1.80f, body.height, Eps, "premise: full capsule outside the tower");
            yield return Go(StanceKey.Crouch);
            Assert.AreEqual(Stance.Crouch, player.Stance);

            Place(inside, 0f);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(0.25f, body.radius, 0.005f, "premise: the point is inside the tower footprint");
            Assert.AreEqual(Stance.Stand, player.Stance, "still lowered inside the tower");
            Assert.AreEqual(1.60f, body.height, Eps, "tower capsule");
            yield return Go(StanceKey.Crouch, 1f);
            Assert.AreEqual(Stance.Stand, player.Stance, "crouched inside the tower footprint");

            Place(outside, 0f);
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(0.40f, body.radius, 0.005f, "radius not restored on leaving");
            Assert.AreEqual(1.80f, body.height, Eps, "the crouch capsule leaked out of the tower");
            Assert.AreEqual(1.80f, player.StandingHeight, Eps);
            Assert.AreEqual(Stance.Stand, player.Stance);
            yield return Go(StanceKey.Crouch);
            Assert.AreEqual(Stance.Crouch, player.Stance, "stances did not come back outside the tower");
        }
    }
}
