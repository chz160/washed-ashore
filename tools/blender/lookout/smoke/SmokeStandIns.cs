// Smoke project only. Stand-ins for the three classes TowerBuilder.cs calls that belong to other owners, so the real
// TowerBuilder.cs compiles and runs unchanged in a throwaway project with none of the game's assemblies:
//   LookoutManifest, LookoutGameplaySetup  (gameplay-engineer's, in Assets/Editor/Lookout/ of the game)
//   LookoutTowerArt                        (technical-artist's)
// They do the least that lets the builder run: read the manifest text, add nothing, make plain materials and one LOD.
// Nothing here is evidence about the real classes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WashedAshore.Lookout.Editor
{
    public class LookoutManifest
    {
        public const string AssetPath = "Assets/World/Lookout/MC39_Lookout.manifest.json";
        [NonSerialized] public string Json;
        public static LookoutManifest Load() => new LookoutManifest { Json = File.ReadAllText(AssetPath) };
    }

    public static class LookoutGameplaySetup
    {
        public static readonly string[] OptionalNullFields = new string[0];
        public static void Apply(GameObject towerRoot, LookoutManifest manifest) { }
    }

    public static class LookoutTowerArt
    {
        [Serializable] class Obj { public string[] materials; }
        [Serializable] class Names { public Obj[] objects; }

        public static IReadOnlyDictionary<string, Material> EnsureMaterials()
        {
            var names = JsonUtility.FromJson<Names>(File.ReadAllText(LookoutManifest.AssetPath)).objects
                .Where(o => o.materials != null).SelectMany(o => o.materials).Distinct().OrderBy(n => n, StringComparer.Ordinal);
            var result = new Dictionary<string, Material>();
            foreach (var name in names)
            {
                string path = $"Assets/World/Lookout/{name}.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material)
                {
                    material = new Material(Shader.Find("Standard")) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                result[name] = material;
            }
            return result;
        }

        public static void ApplyLods(GameObject root)
        {
            var group = root.GetComponent<LODGroup>();
            if (!group) group = root.AddComponent<LODGroup>();
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal).Cast<Renderer>().ToArray();
            group.SetLODs(new[] { new LOD(0.01f, renderers) });
        }
    }
}
