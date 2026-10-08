using NUnit.Framework;
using UnityEngine;
using WashedAshore.Fish;
using WashedAshore.Fish.Rendering;

namespace WashedAshore.Tests.EditMode.FishRendering
{
    /// <summary>FishVisibleCap to f-td's review: no eviction, admit below 3 by priority (fishId tie-break), 1 s fade-in,
    /// 3 s cooldown, airborne exempt, no allocation; and the cap keeps drawn inside FishView.CouldSee.</summary>
    public class FishVisibleCapTests
    {
        const float Dt = 1f / 60f;

        [Test]
        public void AntiChurn_SixStaticFish_30s_EachAdmittedOnce_NeverRemoved_ShownAtMost3()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 11, 12, 13, 14, 15, 16 };
            float[] pri = { 1, 6, 3, 5, 2, 4 }, alpha = new float[6];
            bool[] exempt = new bool[6];
            int maxShown = 0;
            for (int f = 0; f < 30 * 60; f++) maxShown = Mathf.Max(maxShown, cap.Apply(6, ids, pri, exempt, alpha, Dt, 3));
            Assert.LessOrEqual(maxShown, 3);
            Assert.AreEqual(3, cap.Admissions, "each shown fish admitted once");
            Assert.AreEqual(0, cap.Removals, "no eviction while they stay drawable");
            Assert.AreEqual(1f, alpha[1], 1e-4); Assert.AreEqual(1f, alpha[3], 1e-4); Assert.AreEqual(1f, alpha[5], 1e-4); // priorities 6, 5, 4
            Assert.AreEqual(0f, alpha[0], 1e-4);
        }

        [Test]
        public void NoPopIn_AdmittedFishFadeInOverOneSecond_OnePerFrame()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 1, 2, 3 };
            float[] pri = { 3, 2, 1 }, alpha = new float[3];
            bool[] exempt = new bool[3];
            cap.Apply(3, ids, pri, exempt, alpha, Dt, 3);
            Assert.AreEqual(Dt / FishVisibleCap.FadeSeconds, alpha[0], 1e-5, "first frame: one step of the fade");
            Assert.AreEqual(0f, alpha[1], 1e-5, "one admission per frame");
            for (int f = 0; f < 70; f++) cap.Apply(3, ids, pri, exempt, alpha, Dt, 3);
            Assert.AreEqual(1f, alpha[0], 1e-4);
        }

        [Test]
        public void NoBlink_SixStillFish_EachChangesStateAtMostOnce()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 21, 22, 23, 24, 25, 26 };
            float[] alpha = new float[6];
            bool[] exempt = new bool[6];
            var changes = new System.Collections.Generic.Dictionary<long, int>();
            cap.Changed += (id, admitted) => changes[id] = (changes.TryGetValue(id, out int c) ? c : 0) + 1;
            var rng = new System.Random(7);
            for (int f = 0; f < 30 * 60; f++)
            {
                // Priorities wobble as if the player were walking: they must not cause blinking.
                var pri = new float[6];
                for (int i = 0; i < 6; i++) pri[i] = 1f + (float)rng.NextDouble();
                Assert.LessOrEqual(cap.Apply(6, ids, pri, exempt, alpha, Dt, 3), 3);
            }
            foreach (var kv in changes) Assert.LessOrEqual(kv.Value, 1, $"fish {kv.Key} changed state {kv.Value} times");
        }

        [Test]
        public void TieBreak_ByStableFishId()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 9, 4, 7 };
            float[] pri = { 1, 1, 1 }, alpha = new float[3];
            cap.Apply(3, ids, pri, new bool[3], alpha, Dt, 1);
            Assert.Greater(alpha[1], 0f, "equal priority: lowest fishId (4) admitted");
            Assert.AreEqual(0f, alpha[0]); Assert.AreEqual(0f, alpha[2]);
        }

        [Test]
        public void Cooldown_AFishThatLeft_WaitsThreeSeconds()
        {
            var cap = new FishVisibleCap();
            long[] one = { 5 };
            float[] alpha = new float[1];
            for (int f = 0; f < 60; f++) cap.Apply(1, one, new float[] { 1 }, new bool[1], alpha, Dt, 3);
            cap.Apply(0, one, new float[1], new bool[1], alpha, Dt, 3);                // it leaves (rule says no)
            Assert.AreEqual(1, cap.Removals);
            for (int f = 0; f < 120; f++) cap.Apply(1, one, new float[] { 1 }, new bool[1], alpha, Dt, 3); // 2 s later
            Assert.AreEqual(0f, alpha[0], "still cooling down");
            for (int f = 0; f < 80; f++) cap.Apply(1, one, new float[] { 1 }, new bool[1], alpha, Dt, 3);
            Assert.Greater(alpha[0], 0f, "readmitted after 3 s");
        }

        [Test]
        public void AirborneJumps_AreExempt_AndUncounted()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 1, 2, 3, 4, 5 };
            float[] alpha = new float[5];
            bool[] exempt = { false, false, false, false, true };
            int shown = 0;
            for (int f = 0; f < 300; f++) shown = cap.Apply(5, ids, new float[] { 5, 4, 3, 2, 1 }, exempt, alpha, Dt, 3);
            Assert.AreEqual(1f, alpha[4], 1e-4);
            Assert.AreEqual(3, shown);
        }

        [Test]
        public void ZeroAllocation_WithTheCapActive()
        {
            var cap = new FishVisibleCap();
            long[] ids = { 1, 2, 3, 4, 5, 6, 7, 8 };
            float[] pri = { 8, 7, 6, 5, 4, 3, 2, 1 }, alpha = new float[8];
            bool[] exempt = new bool[8];
            for (int f = 0; f < 300; f++) cap.Apply(8, ids, pri, exempt, alpha, Dt, 3); // warm up (tables sized)
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int f = 0; f < 1000; f++) cap.Apply(f % 50 == 0 ? 6 : 8, ids, pri, exempt, alpha, Dt, 3); // includes leave/readmit churn
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before, "bytes allocated by 1000 capped frames");
        }

        [Test]
        public void SubsetWithCap_RandomScenesOf5To8Candidates()
        {
            var murk = new FishMurk(new Vector3(2.2f, 2.4f, 3.2f), 0.15f, 0.02f);
            var tuning = ScriptableObject.CreateInstance<FishTuning>();
            tuning.bodyDrawDistance = 25f; tuning.jumpBodyDrawRadius = 40f;
            var rng = new System.Random(4242);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int checkedDrawn = 0;
            for (int scene = 0; scene < 300; scene++)
            {
                var eye = new Vector3(0f, R(1.5f, 4f), 0f);
                var planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(60f, 16f / 9f, 0.1f, 3000f) *
                    Matrix4x4.TRS(eye, Quaternion.LookRotation(new Vector3(0f, -0.3f, 1f)), new Vector3(1f, 1f, -1f)).inverse);
                var view = new FishView(eye, planes, tuning, murk);
                int n = rng.Next(5, 9);
                var ids = new long[n]; var pri = new float[n]; var alpha = new float[n]; var exempt = new bool[n];
                var top = new float[n]; var centre = new Vector3[n]; var len = new float[n];
                int cand = 0;
                for (int i = 0; i < n; i++)
                {
                    len[cand] = R(0.15f, 0.6f);
                    top[cand] = -R(0.02f, 0.5f);
                    centre[cand] = new Vector3(R(-6f, 6f), top[cand] - 0.1f, R(2f, 12f));
                    var mode = FishDrawRule.Decide(murk, planes, eye, FishTier.V1, false, 0f, top[cand] - 0.2f, top[cand], centre[cand], 0.5f * len[cand],
                                                   0.6f, tuning.BodyDrawRadius(FishMode.Cruise, -top[cand]), out float vis, out _);
                    if (mode == FishDrawMode.None) continue;
                    ids[cand] = scene * 100 + i; pri[cand] = vis * len[cand] / centre[cand].magnitude;
                    cand++;
                }
                var cap = new FishVisibleCap();
                for (int f = 0; f < 90; f++) cap.Apply(cand, ids, pri, exempt, alpha, Dt, 3);
                int shown = 0;
                for (int i = 0; i < cand; i++)
                {
                    if (alpha[i] <= 0f) continue;
                    shown++; checkedDrawn++;
                    Assert.IsTrue(view.CouldSee(top[i], centre[i], 0f, len[i], FishMode.Cruise), "a capped, drawn fish must be CouldSee");
                }
                Assert.LessOrEqual(shown, 3);
            }
            Object.DestroyImmediate(tuning);
            Assert.Greater(checkedDrawn, 100);
        }
    }
}
