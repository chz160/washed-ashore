using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WashedAshore.Character.Stance;
using WashedAshore.World;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// #43 (td ruling adr/physics-layers-43, section 1): the Player's controller and every object under the Player root,
    /// the shadow and view bodies and anything built later included, are on the Player layer, set by script (the scene
    /// keeps its Default values), and the controller is still the only collider on it.
    /// </summary>
    public class PlayerLayerTests : StanceFixture
    {
        void AssertAllOnPlayerLayer(string when)
        {
            int layer = WorldLayers.Player;
            var off = player.GetComponentsInChildren<Transform>(true)
                .Where(t => t.gameObject.layer != layer)
                .Select(t => $"{t.name}@{LayerMask.LayerToName(t.gameObject.layer)}").ToList();
            Assert.IsEmpty(off, $"{when}: objects under the Player root off the Player layer: {string.Join(", ", off.Take(10))}");
            Assert.AreEqual(layer, player.gameObject.layer, $"{when}: the Player root");
            Assert.AreEqual(layer, body.gameObject.layer, $"{when}: the CharacterController");
        }

        [UnityTest]
        public IEnumerator AfterLoad_ControllerAndEveryChild_AreOnPlayerLayer()
        {
            yield return LoadWorld();
            AssertAllOnPlayerLayer("after load");
            var colliders = player.GetComponentsInChildren<Collider>(true);
            Assert.AreEqual(1, colliders.Length, "the controller is the only collider under the Player root");
            Assert.AreSame(body, colliders[0]);
            Debug.Log($"PlayerLayer: {player.GetComponentsInChildren<Transform>(true).Length} objects under the Player root, all on {LayerMask.LayerToName(WorldLayers.Player)}");
        }

        [UnityTest]
        public IEnumerator AfterBodyRebuild_EveryChildIsStillOnPlayerLayer()
        {
            yield return LoadWorld();
            var view = player.GetComponent<PlayerCharacterView>();
            for (int i = 0; i < 2; i++)
            {
                view.RebuildWith(view.Appearance);
                yield return null;
                yield return null;
                AssertAllOnPlayerLayer($"after rebuild {i + 1}");
            }
            Assert.IsNotNull(view.ViewCopy, "a view body exists after the rebuild");
        }

        [UnityTest]
        public IEnumerator Player_SpawnsOnPlayerLayerAndNothingElseIsThereButTheBodies()
        {
            yield return LoadWorld();
            int layer = WorldLayers.Player;
            var strays = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .Where(t => t.gameObject.layer == layer && !t.IsChildOf(player.transform)).Select(t => t.name).ToList();
            Assert.IsEmpty(strays, "objects on the Player layer outside the Player root: " + string.Join(", ", strays.Take(10)));
        }
    }
}
