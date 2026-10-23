using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Wildlife;
using WashedAshore.World;
using static WashedAshore.Tests.Wildlife.WildlifeTestKit;

namespace WashedAshore.Tests.Wildlife
{
    /// <summary>#43 (td ruling adr/physics-layers-43, section 1): spawned wildlife, bodies and children, are on the Wildlife layer; the vendor prefabs are untouched.</summary>
    public class WildlifeLayerTests
    {
        [SetUp]
        public void SetUp() => PinFrameStep();

        [TearDown]
        public void TearDown()
        {
            WildlifeRandom.OverrideSeed(null);
            Time.timeScale = 1f;
            UnpinFrameStep();
        }

        [UnityTest]
        public IEnumerator SpawnedWildlife_AreOnTheWildlifeLayer([Values(101, 202)] int seed)
        {
            yield return LoadWorld(seed); // 101 = the baked animals (Awake layers them), 202 = re-planned in Awake (Spawn layers them)
            var agents = WildlifeAgent.All.ToList();
            Assert.IsNotEmpty(agents, "no wildlife spawned");
            int layer = WorldLayers.Wildlife;
            foreach (var a in agents)
            {
                var off = a.GetComponentsInChildren<Transform>(true).Where(t => t.gameObject.layer != layer).Select(t => t.name).ToList();
                Assert.IsEmpty(off, $"{a.name}: objects off the Wildlife layer: {string.Join(", ", off.Take(5))}");
            }
            Debug.Log($"WildlifeLayer: seed {seed}, {agents.Count} animals on {LayerMask.LayerToName(layer)}");
        }
    }
}
