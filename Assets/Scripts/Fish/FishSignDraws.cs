using System.Reflection;
using UnityEngine;

namespace WashedAshore.Fish
{
    /// <summary>
    /// Was a surface sign actually drawn? (f-td / f-qa after the null FishSurfaceFx.set: counted must mean drawn.) Reads
    /// f-artist's FishSurfaceFx by type name (the rendering assembly references this one, not the reverse): the enabled
    /// component (its static Active when present) and its DrawnLastFrame. Every live sign within its event radius is
    /// drawn (no per-sign culling), so a sign raised this frame was drawn if the component is active and drew at least one.
    /// Read after FishSurfaceFx's LateUpdate (execution order 1001). For tests and the probe only; allocates on first use.
    /// </summary>
    public static class FishSignDraws
    {
        const string FxType = "WashedAshore.Fish.Rendering.FishSurfaceFx";
        static System.Type type;
        static PropertyInfo activeProp, drawnProp;
        static Behaviour cached;

        /// <summary>The enabled FishSurfaceFx and the signs it drew in its last LateUpdate; false (0) if there is none.</summary>
        public static bool Active(out int drawnLastFrame)
        {
            drawnLastFrame = 0;
            var fx = Find();
            if (!fx || !fx.isActiveAndEnabled || drawnProp == null) return false;
            drawnLastFrame = (int)drawnProp.GetValue(fx);
            return true;
        }

        static Behaviour Find()
        {
            if (activeProp != null) return activeProp.GetValue(null) as Behaviour;
            if (cached) return cached;
            foreach (var mb in Object.FindObjectsByType<MonoBehaviour>())
            {
                if (mb.GetType().FullName != FxType) continue;
                type = mb.GetType();
                activeProp = type.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                drawnProp = type.GetProperty("DrawnLastFrame", BindingFlags.Public | BindingFlags.Instance);
                cached = mb;
                return activeProp != null ? activeProp.GetValue(null) as Behaviour : mb;
            }
            return null;
        }

        /// <summary>Tests: forget the cached component (a new scene load).</summary>
        public static void Reset()
        {
            cached = null;
            activeProp = drawnProp = null;
            type = null;
        }
    }
}
