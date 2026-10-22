using NUnit.Framework;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.PlayMode.Character
{
    /// <summary>
    /// player-stances.md §5.2: one case per cell of the stance matrix, on the pure rule (no scene). S-A5.
    /// Columns: Ctrl/East, C/D-Down, X/D-Left, Z/D-Right, Move, Jump, Sprint held (with move). "*" cells need headroom, which the
    /// controller checks; here the rule only names the target. "-" is no change.
    /// </summary>
    public class StanceMatrixTests
    {
        const Stance S = Stance.Stand, C = Stance.Crouch, Q = Stance.Squat, T = Stance.Sit, Z = Stance.Sleep;

        // from, previous (squat only), key, moving, jump, sprint, still, expected (null = no change)
        static readonly object[] Cases =
        {
            // Stand
            new object[] { S, S, StanceKey.Crouch, false, false, false, true, C },
            new object[] { S, S, StanceKey.Squat, false, false, false, true, Q },
            new object[] { S, S, StanceKey.Sit, false, false, false, true, T },
            new object[] { S, S, StanceKey.Sleep, false, false, false, true, Z },
            new object[] { S, S, StanceKey.Sit, true, false, false, false, null },       // sit while moving is ignored
            new object[] { S, S, StanceKey.Sleep, true, false, false, false, null },
            new object[] { S, S, StanceKey.None, true, false, false, false, null },
            new object[] { S, S, StanceKey.None, false, true, false, true, null },         // jump is the controller's
            new object[] { S, S, StanceKey.None, true, false, true, false, null },
            // Crouch
            new object[] { C, S, StanceKey.Crouch, false, false, false, true, S },
            new object[] { C, S, StanceKey.Squat, false, false, false, true, Q },
            new object[] { C, S, StanceKey.Sit, false, false, false, true, T },
            new object[] { C, S, StanceKey.Sleep, false, false, false, true, Z },
            new object[] { C, S, StanceKey.Sit, true, false, false, false, null },
            new object[] { C, S, StanceKey.None, true, false, false, false, null },       // crouch-walk
            new object[] { C, S, StanceKey.None, false, true, false, true, S },            // jump from crouch stands, no jump
            new object[] { C, S, StanceKey.None, true, false, true, false, S },            // sprint stands, then sprints
            // Squat entered from Stand
            new object[] { Q, S, StanceKey.Crouch, false, false, false, true, C },
            new object[] { Q, S, StanceKey.Squat, false, false, false, true, S },          // previous stance
            new object[] { Q, S, StanceKey.Sit, false, false, false, true, T },
            new object[] { Q, S, StanceKey.Sleep, false, false, false, true, Z },
            new object[] { Q, S, StanceKey.None, true, false, false, false, S },           // move: previous stance
            new object[] { Q, S, StanceKey.None, false, true, false, true, S },
            new object[] { Q, S, StanceKey.None, true, false, true, false, S },
            // Squat entered from Crouch
            new object[] { Q, C, StanceKey.Squat, false, false, false, true, C },
            new object[] { Q, C, StanceKey.None, true, false, false, false, C },
            new object[] { Q, C, StanceKey.None, false, true, false, true, S },             // jump: Stand
            // Sit
            new object[] { T, S, StanceKey.Crouch, false, false, false, true, C },
            new object[] { T, S, StanceKey.Squat, false, false, false, true, Q },
            new object[] { T, S, StanceKey.Sit, false, false, false, true, S },
            new object[] { T, S, StanceKey.Sleep, false, false, false, true, Z },
            new object[] { T, S, StanceKey.None, true, false, false, false, S },
            new object[] { T, S, StanceKey.None, false, true, false, true, S },
            new object[] { T, S, StanceKey.None, false, false, true, true, null },          // sprint alone is ignored
            // Sleep
            new object[] { Z, S, StanceKey.Crouch, false, false, false, true, S },
            new object[] { Z, S, StanceKey.Squat, false, false, false, true, S },
            new object[] { Z, S, StanceKey.Sit, false, false, false, true, S },
            new object[] { Z, S, StanceKey.Sleep, false, false, false, true, S },
            new object[] { Z, S, StanceKey.None, true, false, false, false, S },
            new object[] { Z, S, StanceKey.None, false, true, false, true, S },
            new object[] { Z, S, StanceKey.None, false, false, true, true, null },          // sprint alone is ignored
            new object[] { Z, S, StanceKey.None, false, false, false, true, null },         // looking and interacting do not wake
        };

        [TestCaseSource(nameof(Cases))]
        public void Cell(Stance from, Stance previous, StanceKey key, bool moving, bool jump, bool sprint, bool still, object expected)
        {
            var d = StanceRules.Resolve(from, previous, key, moving, jump, sprint, still);
            if (expected == null) Assert.IsFalse(d.Change, $"{from} + {key} moving={moving} jump={jump} sprint={sprint}: expected no change, got {d.Target}");
            else
            {
                Assert.IsTrue(d.Change, $"{from} + {key} moving={moving} jump={jump} sprint={sprint}: expected {expected}, got no change");
                Assert.AreEqual((Stance)expected, d.Target);
            }
        }

        /// <summary>Binding audit (H6): the stance table's paths are free of every other runtime binding except the two listed shares.</summary>
        [Test]
        public void StanceBindings_DoNotShareControlsWithOtherActions()
        {
            // Everything bound in code outside StanceBindings (PlayerController.cs, Interactor.cs, Firefinder.cs).
            var others = new System.Collections.Generic.Dictionary<string, string>
            {
                { "<Keyboard>/w", "Move" }, { "<Keyboard>/a", "Move" }, { "<Keyboard>/s", "Move" }, { "<Keyboard>/d", "Move" },
                { "<Gamepad>/leftStick", "Move" }, { "<Mouse>/delta", "Look" }, { "<Gamepad>/rightStick", "Look" },
                { "<Keyboard>/leftShift", "Sprint" }, { "<Gamepad>/leftStickPress", "Sprint" },
                { "<Keyboard>/space", "Jump" }, { "<Gamepad>/buttonSouth", "Jump+Interact" }, { "<Keyboard>/e", "Interact" },
                { "<Keyboard>/q", "FirefinderTurnLeft" }, { "<Keyboard>/escape", "FirefinderLeave" },
                { "<Gamepad>/buttonEast", "FirefinderLeave" }, { "<Keyboard>/f9", "DebugCamera (dev builds)" },
            };
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var e in StanceBindings.All)
            {
                foreach (string path in new[] { e.Keyboard, e.Gamepad })
                {
                    Assert.IsTrue(seen.Add(path), $"{path} is bound twice in the stance table");
                    if (!others.TryGetValue(path, out string owner)) continue;
                    // The one allowed share: East = Crouch + Firefinder leave (the focus suspends control, and the resume frame is skipped).
                    Assert.IsTrue(path == "<Gamepad>/buttonEast" && e.Key == StanceKey.Crouch,
                        $"{e.Name} shares {path} with {owner}");
                }
            }
            Assert.IsFalse(seen.Contains("<Gamepad>/buttonSouth"), "South stays Jump + Interact only");
            Assert.IsTrue(seen.Contains("<Keyboard>/leftCtrl") && seen.Contains("<Keyboard>/c"), "LeftCtrl and C are the crouch and squat keys");
        }
    }
}
