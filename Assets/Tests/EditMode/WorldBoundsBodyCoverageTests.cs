using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using WashedAshore.Gameplay;

namespace WashedAshore.Tests.EditMode
{
    /// <summary>
    /// #44: the world-bounds clamp only moves bodies that carry a WorldBoundsBody, so none may be authored without one.
    /// Reads every scene and prefab under Assets/ (ThirdParty and Tests excluded) as text and fails on a Rigidbody whose
    /// GameObject has no WorldBoundsBody. Runtime bodies are covered by WorldBoundsClamp.Start, which logs an error.
    /// </summary>
    public class WorldBoundsBodyCoverageTests
    {
        const string ScriptPath = "Assets/Scripts/Gameplay/WorldBounds/WorldBoundsBody.cs";
        static readonly Regex Doc = new Regex(@"(?m)^--- !u!(\d+) &(-?\d+)([^\r\n]*)\r?\n");
        static readonly Regex GameObjectRef = new Regex(@"m_GameObject: \{fileID: (-?\d+)\}");
        static readonly Regex ScriptRef = new Regex(@"m_Script: \{fileID: \d+, guid: ([0-9a-f]{32})");

        static IEnumerable<string> AuthoredFiles()
        {
            foreach (var path in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                string p = path.Replace('\\', '/');
                if (!(p.EndsWith(".prefab") || p.EndsWith(".unity"))) continue;
                if (p.StartsWith("Assets/ThirdParty/") || p.StartsWith("Assets/Tests/")) continue;
                yield return p;
            }
        }

        /// <summary>The GameObject ids with a Rigidbody and no WorldBoundsBody in one scene or prefab file.</summary>
        static List<string> Uncovered(string text, string bodyGuid)
        {
            var parts = Doc.Split(text);
            var withBody = new HashSet<string>();
            var rigidbodies = new List<string>();
            for (int i = 1; i + 3 < parts.Length; i += 4)
            {
                string classId = parts[i], header = parts[i + 2], body = parts[i + 3];
                if (header.Contains("stripped")) continue;
                var go = GameObjectRef.Match(body);
                if (!go.Success) continue;
                if (classId == "54") rigidbodies.Add(go.Groups[1].Value);
                else if (classId == "114" && ScriptRef.Match(body) is Match s && s.Success && s.Groups[1].Value == bodyGuid) withBody.Add(go.Groups[1].Value);
            }
            return rigidbodies.FindAll(id => !withBody.Contains(id));
        }

        [Test]
        public void NoSceneOrPrefab_HasARigidbodyWithoutWorldBoundsBody()
        {
            string guid = AssetDatabase.AssetPathToGUID(ScriptPath);
            Assert.IsNotEmpty(guid, "WorldBoundsBody script not found");
            var bad = new List<string>();
            int files = 0;
            foreach (var p in AuthoredFiles())
            {
                files++;
                foreach (var id in Uncovered(File.ReadAllText(p), guid)) bad.Add($"{p} (GameObject {id})");
            }
            Assert.Greater(files, 0, "no scenes or prefabs scanned");
            Assert.IsEmpty(bad, "Rigidbody without WorldBoundsBody (the clamp would never move it):\n" + string.Join("\n", bad));
        }

        /// <summary>Force the failure: the same scan flags a Rigidbody document that has no registrar next to it.</summary>
        [Test]
        public void Scan_FlagsARigidbodyWithoutTheRegistrar()
        {
            const string guid = "5b6f0c1d7a2e4c3b9d8e1f2a3b4c5d6e";
            string body = "--- !u!54 &7\nRigidbody:\n  m_GameObject: {fileID: 11}\n";
            string registrar = "--- !u!114 &8\nMonoBehaviour:\n  m_GameObject: {fileID: 11}\n  m_Script: {fileID: 11500000, guid: " + guid + ", type: 3}\n";
            Assert.AreEqual(new[] { "11" }, Uncovered("%YAML 1.1\n" + body, guid).ToArray());
            Assert.IsEmpty(Uncovered("%YAML 1.1\n" + body + registrar, guid));
        }
    }
}
