using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;
using WashedAshore.Tests.Support;

namespace WashedAshore.Tests.PlayMode
{
    /// <summary>
    /// Bush slowdown, PlayMode (#21, docs/design/bush-slowdown.md section 9: B1.1, B1.2, B1.7, B2.2 to B2.4, B3). The World scene supplies the player;
    /// a flat scratch terrain with 2 m detail cells, placed clear of the real tiles, supplies the cover. The sim is driven tick by tick with
    /// Simulate (AutoTick off), so the speed is distance over ticks, not over frame time.
    /// </summary>
    public class BushSlowdownTests
    {
        const float Dt = PlayerController.TickDt;
        const int Cells = 160, Patch = 15, Centre = 80;   // 320 m terrain, 2 m cells (Unity rounds the detail resolution to a multiple of the 16 cell patch), a 15 x 15 patch (30 m) around cell 80
        const float Size = 320f, GroundY = 100f;
        static readonly string[] Layers = { "Broomsedge", "Briar", "ShrubThicket", "KudzuMound", "KudzuLeaf", "Fern", "CrackGrass", "BankGrass" };

        PlayerController player;
        Terrain scratch;
        Vector3 origin;   // scratch terrain corner

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            SceneManager.LoadScene(TestSpawn.WorldScene, LoadSceneMode.Single);
            yield return null;
            yield return null;
            player = TestSpawn.Player();
            // Clear of every real tile, south-west of them, and south of the north line.
            float minX = float.MaxValue, minZ = float.MaxValue;
            foreach (var t in Terrain.activeTerrains)
            {
                minX = Mathf.Min(minX, t.transform.position.x);
                minZ = Mathf.Min(minZ, t.transform.position.z);
            }
            origin = new Vector3(minX - Size - 500f, GroundY, minZ - Size - 500f);
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(Size, 10f, Size) };
            data.SetDetailResolution(Cells, 16);
            Assert.AreEqual(Cells, data.detailResolution, "the scratch terrain has 2 m detail cells");
            var protos = new DetailPrototype[Layers.Length];
            for (int i = 0; i < protos.Length; i++) protos[i] = new DetailPrototype { prototypeTexture = Texture2D.whiteTexture, usePrototypeMesh = false };
            data.detailPrototypes = protos;
            var go = Terrain.CreateTerrainGameObject(data);
            go.transform.position = origin;
            scratch = go.GetComponent<Terrain>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (player != null) player.AutoTick = true;
            if (scratch != null) { var d = scratch.terrainData; Object.Destroy(scratch.gameObject); Object.Destroy(d); }
        }

        void FillPatch(int layer, int count)
        {
            var cells = new int[Patch, Patch];
            for (int y = 0; y < Patch; y++) for (int x = 0; x < Patch; x++) cells[y, x] = count;
            scratch.terrainData.SetDetailLayer(Centre - Patch / 2, Centre - Patch / 2, layer, cells);
            player.InvalidateBushCover();
        }

        /// <summary>World position of the patch centre plus an offset in x, metres.</summary>
        Vector3 At(float dx) => new Vector3(origin.x + Centre * 2f + dx, GroundY + 1f, origin.z + Centre * 2f);

        static PlayerCommand Cmd(bool sprint, StanceKey key = StanceKey.None) => new PlayerCommand
        {
            Move = Vector2.up, Yaw = 90f, Buttons = sprint ? PlayerButtons.Sprint : PlayerButtons.None, Stance = key,
        };

        IEnumerator Start(float dx)
        {
            player.AutoTick = false;
            yield return TestSpawn.At(At(dx), 90f);
            var idle = new PlayerCommand { Yaw = 90f };
            for (int i = 0; i < 30; i++) player.Simulate(idle);
            Assert.IsTrue(player.IsGrounded, "the player stands on the scratch terrain");
            Assert.IsTrue(player.Capsule.enabled);
        }

        /// <summary>Runs <paramref name="ticks"/> ticks and returns the horizontal distance over the last <paramref name="measure"/> of them.</summary>
        float Run(int ticks, int measure, bool sprint, StanceKey key = StanceKey.None, List<float> steps = null)
        {
            Vector3 from = default;
            for (int i = 0; i < ticks; i++)
            {
                if (i == ticks - measure) from = player.transform.position;
                Vector3 before = player.transform.position;
                player.Simulate(Cmd(sprint, i == 0 ? key : StanceKey.None));
                steps?.Add(Vector2.Distance(new Vector2(before.x, before.z), new Vector2(player.transform.position.x, player.transform.position.z)));
            }
            Vector3 to = player.transform.position;
            return Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.x, to.z));
        }

        [UnityTest]
        public IEnumerator B1_1_WalkSpeedInAFullPatchOfEachLayer()
        {
            var table = new List<string>();
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                yield return Start(0f);
                                int full = layer == 4 ? 2 : 1;
                FillPatch(layer, full);
                float expected = 5f * ExpectedMultiplier(layer, false);
                float speed = Run(90, 60, false) / (60 * Dt);
                table.Add($"{Layers[layer]} walk {speed:F3} (want {expected:F2})");
                Assert.AreEqual(expected, speed, 0.05f, $"walk in {Layers[layer]}");
                FillPatchClear(layer);
            }
            Debug.Log("BushSlowdownTests B1.1 speed table (walk): " + string.Join("; ", table));
        }

        [UnityTest]
        public IEnumerator B1_2_SprintSpeedInAFullPatchOfEachLayer()
        {
            var table = new List<string>();
            for (int layer = 0; layer < Layers.Length; layer++)
            {
                yield return Start(0f);
                                FillPatch(layer, layer == 4 ? 2 : 1);
                float expected = 8f * ExpectedMultiplier(layer, true);
                float speed = Run(90, 60, true) / (60 * Dt);
                table.Add($"{Layers[layer]} sprint {speed:F3} (want {expected:F2})");
                Assert.AreEqual(expected, speed, 0.05f, $"sprint in {Layers[layer]}");
                FillPatchClear(layer);
            }
            Debug.Log("BushSlowdownTests B1.2 speed table (sprint): " + string.Join("; ", table));
        }

        float ExpectedMultiplier(int layer, bool sprint)
        {
            var l = player.BushTuning.layers[layer];
            if (!l.slows) return 1f;
            return sprint ? l.sprintMultiplier : l.walkMultiplier;
        }

        [UnityTest]
        public IEnumerator B1_7_CrouchInAFullThicketAndMoundKeepsTheCrouchSpeed()
        {
            foreach (int layer in new[] { 2, 3 })
            {
                yield return Start(0f);
                FillPatch(layer, 1);
                // Crouch takes a moment; walk until the stance has settled, then measure the last second.
                float speed = Run(150, 60, false, StanceKey.Crouch) / (60 * Dt);
                Assert.AreEqual(Stance.Crouch, player.Stance, "crouched");
                Assert.AreEqual(1.75f, speed, 0.05f, $"crouch speed in {Layers[layer]}");
                FillPatchClear(layer);
            }
        }

        /// <summary>The target magnitude the code before the bush system produces for full forward input: the same expression as MoveTarget (Water.cs), bit for bit.</summary>
        float UnmodifiedTarget(float speed) => ((player.transform.forward * 1f + player.transform.right * 0f) * speed).magnitude;

        void FillPatchClear(int layer)
        {
            scratch.terrainData.SetDetailLayer(Centre - Patch / 2, Centre - Patch / 2, layer, new int[Patch, Patch]);
            player.InvalidateBushCover();
        }

        [UnityTest]
        public IEnumerator B2_LeavingTheBushRestoresWalk5AndSprint8()
        {
            foreach (bool sprint in new[] { false, true })
            {
                yield return Start(11f);   // inside the last cell column of the 30 m patch (the last cell centre is 15 m east of the start line, so cover is 0 from +17 m)
                FillPatch(3, 1);
                int guard = 0;
                while (player.transform.position.x < At(17.5f).x && guard++ < 600) player.Simulate(Cmd(sprint));
                Assert.Less(guard, 600, "the player left the patch");
                float speed = Run(120, 60, sprint) / (60 * Dt);
                float target = player.LastMoveTargetMagnitude;
                float want = sprint ? 8f : 5f;
                Assert.AreEqual(want, speed, 0.01f, $"measured {(sprint ? "sprint" : "walk")} speed after the patch");
                Assert.AreEqual(UnmodifiedTarget(want), target, "the move target is bit-equal to the unmodified code's");
                Assert.IsTrue(player.LastBushMultiplier == 1f, "the multiplier is exactly 1 outside cover");
                FillPatchClear(3);
            }
        }

        [UnityTest]
        public IEnumerator B2_4_NonSlowingLayersLeaveWalk5AndSprint8()
        {
            foreach (int layer in new[] { 0, 5, 6, 7 })
                foreach (bool sprint in new[] { false, true })
                {
                    yield return Start(0f);
                    FillPatch(layer, 1);
                    float speed = Run(90, 60, sprint) / (60 * Dt);
                    float want = sprint ? 8f : 5f;
                    Assert.AreEqual(want, speed, 0.01f, $"{Layers[layer]} {(sprint ? "sprint" : "walk")}");
                    Assert.AreEqual(UnmodifiedTarget(want), player.LastMoveTargetMagnitude, $"{Layers[layer]} target is bit-equal to the unmodified code's");
                    FillPatchClear(layer);
                }
        }

        [UnityTest]
        public IEnumerator B3_1_NoSnagStartingAtRestInTheThicket()
        {
            foreach (int layer in new[] { 2, 3 })   // ShrubThicket and KudzuMound (spec B3.1)
            {
                yield return Start(0f);
                FillPatch(layer, 1);
                var steps = new List<float>();
                Run(90, 60, false, StanceKey.None, steps);
                float expected = 5f * player.BushTuning.layers[layer].walkMultiplier / 60f;
                for (int i = 30; i < steps.Count; i++)
                {
                    Assert.Greater(steps[i], 0f, $"{Layers[layer]} tick {i} moved");
                    Assert.AreEqual(expected, steps[i], expected * 0.05f, $"{Layers[layer]} tick {i} step within 5% of {expected:F4}");
                }
                FillPatchClear(layer);
            }
        }

        [UnityTest]
        public IEnumerator B3_2_WalkingThroughAndOutOfAThicketNeverStopsForATick()
        {
            foreach (bool sprint in new[] { false, true })
            {
                yield return Start(-18f);   // outside the patch (edge at -15 m)
                FillPatch(2, 1);
                var steps = new List<float>();
                int guard = 0;
                while (player.transform.position.x < At(20f).x && guard++ < 900)
                {
                    Vector3 before = player.transform.position;
                    player.Simulate(Cmd(sprint));
                    steps.Add(Vector2.Distance(new Vector2(before.x, before.z), new Vector2(player.transform.position.x, player.transform.position.z)));
                }
                Assert.Less(guard, 900, "walked through");
                for (int i = 0; i < steps.Count; i++) Assert.Greater(steps[i], 0f, $"{(sprint ? "sprint" : "walk")} tick {i} did not move");
                float top = sprint ? 8f : 5f;
                foreach (float s in steps) Assert.LessOrEqual(s, top * Dt * 1.05f, "no position correction (a step above the sim speed)");
                FillPatchClear(2);
            }
        }
    }
}
