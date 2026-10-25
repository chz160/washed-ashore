using System;
using System.Collections;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WashedAshore.World;

namespace WashedAshore.Tests.Lookout.PlayMode
{
    /// <summary>
    /// #45 K5: BoundaryGate does not become openable in play. Asking it to change its pose in play must move nothing (its leaves are
    /// static colliders) and log one error. AFTER only: on main the leaves move, so this is red by design there.
    /// </summary>
    public class LookoutMoversGate
    {
        [UnityTest]
        public IEnumerator BoundaryGate_AskedToOpenInPlay_MovesNothingAndSaysSo()
        {
            SceneManager.LoadScene("World", LoadSceneMode.Single);
            yield return null;
            yield return null;
            var type = Type.GetType("WashedAshore.Level.BoundaryGate, WashedAshore.Level");
            Assert.IsNotNull(type, "BoundaryGate type not found");
            var gate = (Component)UnityEngine.Object.FindFirstObjectByType(type);
            Assert.IsNotNull(gate, "no BoundaryGate in World");
            T Get<T>(string n) => (T)type.GetField(n).GetValue(gate);
            var left = Get<Transform>("leftLeaf"); var right = Get<Transform>("rightLeaf");
            var before = (left.localRotation, right.localRotation);
            var savedConfig = Get<MapConfig>("config");
            var asked = ScriptableObject.CreateInstance<MapConfig>();
            asked.gateOpen = savedConfig == null || !savedConfig.gateOpen; // the opposite of the saved state
            type.GetField("config").SetValue(gate, asked);
            LogAssert.Expect(LogType.Error, new Regex("BoundaryGate: the gate cannot open or close in play"));
            type.GetMethod("Apply").Invoke(gate, null);
            yield return null;
            Debug.Log($"LookoutMovers K5: gate leaves moved? left {Quaternion.Angle(before.Item1, left.localRotation):F4} deg, right {Quaternion.Angle(before.Item2, right.localRotation):F4} deg");
            Assert.Less(Quaternion.Angle(before.Item1, left.localRotation), 0.01f, "left leaf moved in play");
            Assert.Less(Quaternion.Angle(before.Item2, right.localRotation), 0.01f, "right leaf moved in play");
            type.GetField("config").SetValue(gate, savedConfig);
            UnityEngine.Object.Destroy(asked);
        }
    }
}
