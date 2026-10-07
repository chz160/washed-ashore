using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WashedAshore.Level;

namespace WashedAshore.Tests.EditMode.Level
{
    /// <summary>
    /// Horizon haze (ruling/water-look-scum-horizon fix 2, w-td sky ruling). Reads the built World scene; build first
    /// with BellsBendSky.Build().
    /// </summary>
    public class SkyBuildTests
    {
        [OneTimeSetUp]
        public void OpenWorld()
        {
            if (SceneManager.GetActiveScene().path != BellsBendLevel.ScenePath)
                EditorSceneManager.OpenScene(BellsBendLevel.ScenePath, OpenSceneMode.Single);
        }

        [Test]
        public void Sky_ExactlyOneRuntimeEnvironmentInit()
        {
            var inits = Object.FindObjectsByType<BellsBendSkyInit>(FindObjectsInactive.Include);
            Assert.AreEqual(1, inits.Length, "exactly one BellsBendSkyInit in World");
            Assert.AreEqual(BellsBendSky.RootName, inits[0].gameObject.name);
            Assert.IsNull(inits[0].transform.parent, "on a root object");
        }

        [Test]
        public void Sky_ProjectMaterialOnBuiltInProceduralShader()
        {
            var sky = RenderSettings.skybox;
            Assert.IsNotNull(sky);
            Assert.AreEqual(BellsBendSky.MaterialPath, AssetDatabase.GetAssetPath(sky));
            Assert.AreEqual("Skybox/Procedural", sky.shader.name);
            Assert.AreSame(AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat").shader, sky.shader, "built-in shader, not a copy");
        }

        [Test]
        public void Sky_FogIsLinearAndOffTheOldBlue()
        {
            Assert.IsTrue(RenderSettings.fog);
            Assert.AreEqual(FogMode.Linear, RenderSettings.fogMode);
            Assert.AreEqual(BellsBendSky.FogStart, RenderSettings.fogStartDistance, 1e-3f, "near water 0-120 m stays clear");
            Assert.AreEqual(BellsBendSky.FogEnd, RenderSettings.fogEndDistance, 1e-3f);
            var c = RenderSettings.fogColor;
            Assert.Greater(Vector3.Distance(new Vector3(c.r, c.g, c.b), new Vector3(0.72f, 0.80f, 0.88f)), 0.02f, "fog colour moved off the old blue");
        }
    }
}
