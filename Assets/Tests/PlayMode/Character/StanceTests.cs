using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>Stance numbers and rules of record: _bmad-output/design/player-stances.md. Acceptance S-A1 to S-A8 (H4, H6).</summary>
    public class StanceTests : StanceFixture
    {
        static readonly (StanceKey key, Stance stance, float eye, float cap)[] Settled =
        {
            (StanceKey.Crouch, Stance.Crouch, 1.20f, 1.35f),
            (StanceKey.Squat, Stance.Squat, 1.00f, 1.15f),
            (StanceKey.Sit, Stance.Sit, 0.80f, 0.95f),
            (StanceKey.Sleep, Stance.Sleep, 0.20f, 0.80f),
        };

        /// <summary>S-A1: settled pivot height and capsule height per §3, and back to 1.65 / 1.80.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator SettledHeights_MatchTheTable()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            Assert.AreEqual(1.80f, body.height, Eps);
            Assert.AreEqual(1.65f, Eye, Eps);
            foreach (var s in Settled)
            {
                yield return Go(s.key);
                Assert.AreEqual(s.stance, player.Stance, $"after {s.key}");
                Assert.AreEqual(s.eye, Eye, Eps, $"{s.stance} eye");
                Assert.AreEqual(s.cap, body.height, Eps, $"{s.stance} capsule");
                yield return Go(s.key);   // the same key again stands (sleep: wakes)
                Assert.AreEqual(Stance.Stand, player.Stance, $"{s.stance} toggled off");
                Assert.AreEqual(1.65f, Eye, Eps);
                Assert.AreEqual(1.80f, body.height, Eps);
            }
        }

        /// <summary>S-A3: every frame of every transition keeps eye <= capsule top - 0.15 (+0.005).</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator EveryTransitionFrame_KeepsTheEyeUnderTheCapsuleTop()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            float worst = float.MinValue;
            int frames = 0;
            var keys = new[] { StanceKey.Crouch, StanceKey.Squat, StanceKey.Sit, StanceKey.Sleep, StanceKey.Sleep, StanceKey.Crouch, StanceKey.Crouch };
            foreach (var key in keys)
            {
                player.PressStanceKey(key);
                float t = 0f;
                do
                {
                    yield return null;
                    t += Time.deltaTime;
                    worst = Mathf.Max(worst, Eye - (CapTop - 0.15f));
                    frames++;
                } while (player.StanceTransitioning && t < 4f);
            }
            Assert.Greater(frames, 30);
            Assert.LessOrEqual(worst, 0.005f, $"eye over capsule top - 0.15 by {worst:F4} m");
        }

        /// <summary>S-A4: no headroom keeps you crouched; walking out stands; another stance key cancels the held stand.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Headroom_HoldsTheStandRequest_AndAnotherKeyCancelsIt()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            yield return Go(StanceKey.Crouch);
            var ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.localScale = new Vector3(6f, 0.2f, 6f);
            ceiling.transform.position = player.transform.position + new Vector3(0f, 1.65f + 0.1f, 0f);
            yield return Frames(3);
            yield return Go(StanceKey.Crouch, 1f);   // stand under a 1.5 m ceiling
            Assert.AreEqual(Stance.Crouch, player.Stance, "stood up under a 1.5 m ceiling");
            Assert.AreEqual(1.35f, body.height, Eps);
            Object.Destroy(ceiling);
            yield return Frames(3);
            yield return Finish();
            Assert.AreEqual(Stance.Stand, player.Stance, "the held stand request did not complete once the ceiling was gone");

            // Cancel: hold the request, press squat, clear the ceiling, stay squatting.
            yield return Go(StanceKey.Crouch);
            ceiling = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ceiling.transform.localScale = new Vector3(6f, 0.2f, 6f);
            ceiling.transform.position = player.transform.position + new Vector3(0f, 1.65f + 0.1f, 0f);
            yield return Frames(3);
            yield return Go(StanceKey.Crouch, 1f);
            Assert.AreEqual(Stance.Crouch, player.Stance);
            yield return Go(StanceKey.Squat);
            Assert.AreEqual(Stance.Squat, player.Stance);
            Object.Destroy(ceiling);
            yield return Frames(10);
            yield return Finish();
            Assert.AreEqual(Stance.Squat, player.Stance, "a cancelled stand request stood the player up");
        }

        /// <summary>S-A2: crouch speed 1.75 +-0.05; walk 5 and sprint 8 unchanged; sprint from crouch stands and sprints.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Speeds_CrouchWalkSprint()
        {
            yield return LoadWorld();
            yield return OnFloor();
            float walk = 0f, crouch = 0f, sprint = 0f;
            Vector3 last;
            Press(Key.W);
            yield return new WaitForSeconds(1.0f);
            last = player.transform.position; yield return new WaitForSeconds(0.5f);
            walk = HSpeed(last, 0.5f);
            Release();
            yield return new WaitForSeconds(0.6f);

            yield return Go(StanceKey.Crouch);
            Press(Key.W);
            yield return new WaitForSeconds(1.0f);
            last = player.transform.position; yield return new WaitForSeconds(0.5f);
            crouch = HSpeed(last, 0.5f);
            Assert.AreEqual(Stance.Crouch, player.Stance);

            Press(Key.W, Key.LeftShift);
            yield return new WaitForSeconds(1.5f);
            last = player.transform.position; yield return new WaitForSeconds(0.5f);
            sprint = HSpeed(last, 0.5f);
            Release();
            Assert.AreEqual(Stance.Stand, player.Stance, "sprint from crouch did not stand");
            Assert.AreEqual(5f, walk, 0.1f, "walk");
            Assert.AreEqual(1.75f, crouch, 0.05f, "crouch");
            Assert.AreEqual(8f, sprint, 0.15f, "sprint");
        }

        /// <summary>Jump from crouch stands you up and does not jump; a jump from standing is 1.2 m.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator JumpFromCrouch_StandsWithoutJumping()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            yield return Go(StanceKey.Crouch);
            float y0 = player.transform.position.y;
            float peak = y0;
            Press(Key.Space);
            for (int i = 0; i < 40; i++) { yield return null; peak = Mathf.Max(peak, player.transform.position.y); }
            Release();
            Assert.AreEqual(Stance.Stand, player.Stance);
            Assert.Less(peak - y0, 0.05f, "jumped out of a crouch");
            yield return Finish();
            yield return Frames(5);
            y0 = player.transform.position.y; peak = y0;
            Press(Key.Space);
            for (float tj = 0f; tj < 1.2f; tj += Time.deltaTime) { yield return null; peak = Mathf.Max(peak, player.transform.position.y); }
            Release();
            string above = Physics.Raycast(player.transform.position + Vector3.up * 1.9f, Vector3.up, out RaycastHit up, 20f, WashedAshore.World.WorldLayers.PlayerCollisionMask, QueryTriggerInteraction.Ignore) ? $"{up.collider.name} at {up.distance:F2} m above the head" : "nothing within 20 m";
            Assert.AreEqual(1.2f, peak - y0, 0.08f, "standing jump height; overhead: " + above);
        }

        /// <summary>Airborne entry is blocked; a crouch held off an edge stays crouched.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Airborne_BlocksEntry()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            Press(Key.Space);
            yield return Frames(3);
            Release();
            Assert.IsFalse(player.IsGrounded);
            player.PressStanceKey(StanceKey.Crouch);
            yield return null;
            Assert.AreEqual(Stance.Stand, player.Stance, "crouched in mid-air");
            yield return new WaitForSeconds(1.2f);
        }

        /// <summary>A place that slims the capsule (the tower) ends any lowered stance at once and blocks new ones.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator SlimmedCapsule_EndsAndBlocksLoweredStances()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            yield return Go(StanceKey.Crouch);
            player.SetWalkCapsule(0.25f, 1.6f);
            yield return Frames(2);
            Assert.AreEqual(Stance.Stand, player.Stance);
            Assert.AreEqual(1.6f, body.height, Eps, "the tower capsule was not kept");
            foreach (var key in new[] { StanceKey.Crouch, StanceKey.Squat, StanceKey.Sit, StanceKey.Sleep })
            {
                yield return Go(key, 1f);
                Assert.AreEqual(Stance.Stand, player.Stance, $"{key} worked in the tower capsule");
            }
            player.SetWalkCapsule(0.40f, 1.80f);
            yield return Go(StanceKey.Crouch);
            Assert.AreEqual(Stance.Crouch, player.Stance, "stances did not come back with the full capsule");
            Assert.AreEqual(1.80f, player.StandingHeight, Eps);
        }

        /// <summary>Sit and sleep need standing still; sleep does not wake on look; any move wakes; no Canvas appears.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Sleep_RulesAndDim()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            int canvasesBefore = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).Length;
            Press(Key.W);
            yield return Frames(15);
            player.PressStanceKey(StanceKey.Sleep);
            yield return null;
            Assert.AreEqual(Stance.Stand, player.Stance, "slept while moving");
            Release();
            yield return new WaitForSeconds(0.6f);
            yield return Go(StanceKey.Sleep);
            Assert.AreEqual(Stance.Sleep, player.Stance);
            Assert.AreEqual(-60f, player.Pitch, 0.5f, "sleep pitch");
            float yaw = player.Yaw;
            Set(Mouse.current.delta, new Vector2(80f, -40f));
            yield return new WaitForSeconds(10f);
            Set(Mouse.current.delta, Vector2.zero);
            Assert.AreEqual(Stance.Sleep, player.Stance, "look-only input woke the sleeper");
            Assert.AreEqual(yaw, player.Yaw, 0.01f, "look turned a sleeper");
            Assert.Greater(player.SleepDim, 0.99f, "dim did not reach full");
            Assert.AreEqual(canvasesBefore, Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).Length, "a Canvas appeared (no HUD)");
            Press(Key.W);
            yield return Frames(2);
            Release();
            yield return Finish();
            Assert.AreEqual(Stance.Stand, player.Stance, "move did not wake");
            Assert.AreEqual(0f, player.SleepDim, 0.01f);
        }

        /// <summary>Sit refuses a slope over 25 degrees and a blocked box; sleep refuses over 15 degrees.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator SitAndSleep_RefuseBadGround()
        {
            PinTicks();   // #46 PR B: one tick per frame (td-46 adr/46-b-test-pin-pattern)
            yield return LoadWorld();
            yield return OnFloor();
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.transform.localScale = new Vector3(8f, 0.5f, 8f);
            ramp.transform.position = player.transform.position + Vector3.down * 0.05f;
            ramp.transform.rotation = Quaternion.Euler(0f, 0f, 30f);
            try
            {
                Place(player.transform.position + Vector3.up * 3f, 0f);
                yield return new WaitForSeconds(1.5f);
                yield return Go(StanceKey.Sit, 1f);
                Assert.AreEqual(Stance.Stand, player.Stance, "sat on a 30 degree slope");
                yield return Go(StanceKey.Sleep, 1f);
                Assert.AreEqual(Stance.Stand, player.Stance, "slept on a 30 degree slope");
            }
            finally { Object.Destroy(ramp); }
            yield return new WaitForSeconds(1.0f);
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.localScale = new Vector3(2f, 0.5f, 0.3f);
            wall.transform.position = player.transform.position + player.transform.forward * 0.7f + Vector3.up * 0.35f;
            try
            {
                yield return new WaitForSeconds(0.5f);
                yield return Go(StanceKey.Sit, 1f);
                Assert.AreEqual(Stance.Stand, player.Stance, "sat with a block in the sit box");
            }
            finally { Object.Destroy(wall); }
            yield return new WaitForSeconds(0.5f);
            yield return Go(StanceKey.Sit);
            Assert.AreEqual(Stance.Sit, player.Stance, "sit on flat clear ground was refused");
        }

        /// <summary>Respawn from any stance: Stand, 1.80 capsule, 1.65 eye, no dim.</summary>
        [UnityTest, Timeout(300000)]
        public IEnumerator Respawn_ResetsTheStance()
        {
            yield return LoadWorld();
            yield return OnFloor();
            yield return Go(StanceKey.Sleep);
            yield return new WaitForSeconds(2.5f);
            player.Respawn();
            yield return Frames(2);
            Assert.AreEqual(Stance.Stand, player.Stance);
            Assert.AreEqual(player.StandingHeight, body.height, Eps);
            Assert.AreEqual(1.65f, Eye, Eps);
            Assert.AreEqual(0f, player.SleepDim, 0.001f);
        }

        void Press(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        void Release() => InputSystem.QueueStateEvent(keyboard, new KeyboardState());

        float HSpeed(Vector3 from, float seconds)
        {
            Vector3 d = player.transform.position - from;
            d.y = 0f;
            return d.magnitude / seconds;
        }
    }
}
