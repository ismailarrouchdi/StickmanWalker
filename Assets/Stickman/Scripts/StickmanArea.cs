using UnityEngine;

namespace Stickman
{
    // Training area root: a ground and a stickman. The ground top is at this height.
    [DisallowMultipleComponent]
    public class StickmanArea : MonoBehaviour
    {
        [Tooltip("Area number (used by the camera to order the agents).")]
        public int index;

        [Tooltip("Ground collider of this area.")]
        public Collider ground;

        [Tooltip("Local X where the ground starts (m).")]
        public float groundStart = -10f;

        [Tooltip("Local X where the ground ends (m).")]
        public float groundEnd = 140f;

        public float GroundHeight => transform.position.y;
    }
}
