using UnityEngine;

namespace Stickman
{
    // Tracks the ground contacts of one body segment.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public class StickmanBodyPart : MonoBehaviour
    {
        // 2D motion: Z position and X/Y rotations are frozen.
        public const RigidbodyConstraints PlanarConstraints =
            RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;

        [Tooltip("Which body segment this is.")]
        public BodyPartType part;

        [Tooltip("Part of the segment whose contact with the ground ends the episode.")]
        public FatalContactZone fatalZone = FatalContactZone.None;

        [Tooltip("Segment length (m), filled in by the scene builder.")]
        public float length;

        Rigidbody body;
        double lastGroundContactTime = double.NegativeInfinity;

        public Rigidbody Body
        {
            get
            {
                if (body == null) body = GetComponent<Rigidbody>();
                return body;
            }
        }

        // True if the segment touched the ground during the last physics step.
        public bool IsGrounded => Time.fixedTimeAsDouble - lastGroundContactTime <= Time.fixedDeltaTime * 1.5;

        public bool HasFatalContact { get; private set; }

        public void ResetContacts()
        {
            lastGroundContactTime = double.NegativeInfinity;
            HasFatalContact = false;
        }

        void OnCollisionEnter(Collision collision) => HandleCollision(collision);

        void OnCollisionStay(Collision collision) => HandleCollision(collision);

        void HandleCollision(Collision collision)
        {
            if (collision.collider == null || !collision.collider.TryGetComponent<StickmanGround>(out _))
                return;

            lastGroundContactTime = Time.fixedTimeAsDouble;

            if (fatalZone == FatalContactZone.None || HasFatalContact)
                return;

            if (fatalZone == FatalContactZone.Whole)
            {
                HasFatalContact = true;
                return;
            }

            // UpperHalf: only contacts on the upper half of the capsule (the knee).
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (transform.InverseTransformPoint(collision.GetContact(i).point).y > 0f)
                {
                    HasFatalContact = true;
                    return;
                }
            }
        }
    }
}
