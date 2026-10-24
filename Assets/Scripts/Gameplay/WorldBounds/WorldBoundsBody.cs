using UnityEngine;

namespace WashedAshore.Gameplay
{
    /// <summary>
    /// Put this on any Rigidbody that can leave the map (loot, crates, boats, thrown items) so the north-boundary
    /// clamp keeps it inside. It registers the body in OnEnable and removes it in OnDisable, so the clamp never
    /// searches the scene (#44). A Rigidbody without it is not clamped. Adding it to a bare GameObject adds the
    /// Rigidbody too.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class WorldBoundsBody : MonoBehaviour
    {
        Rigidbody body;

        void OnEnable()
        {
            body = GetComponent<Rigidbody>();
            WorldBoundsClamp.Register(body);
        }

        void OnDisable() => WorldBoundsClamp.Unregister(body);
    }
}
