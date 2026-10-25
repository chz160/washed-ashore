using System.Collections.Generic;
using UnityEngine;

namespace WashedAshore.World
{
    /// <summary>
    /// A component that moves transforms during play (a hinged leaf, a turning sight). Colliders under a moved transform must either
    /// belong to <see cref="SwitchedOffWhileMoving"/> (the owner turns them off for the whole move) or sit on a kinematic Rigidbody;
    /// a static collider must never be moved (docs/physics-guidelines.md, rules 9 and 10; #45). An EditMode scan checks this.
    /// </summary>
    public interface IMovingTransforms
    {
        /// <summary>The transforms this component moves while the game runs.</summary>
        IEnumerable<Transform> MovedTransforms { get; }

        /// <summary>The colliders under them that the owner switches off while they move.</summary>
        IEnumerable<Collider> SwitchedOffWhileMoving { get; }
    }
}
