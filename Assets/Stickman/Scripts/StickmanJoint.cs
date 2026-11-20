using UnityEngine;

namespace Stickman
{
    // Drives one joint towards a target angle with its motor.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ConfigurableJoint))]
    public class StickmanJoint : MonoBehaviour
    {
        public JointType type;
        public bool isLeft;
        public ConfigurableJoint joint;
        [Tooltip("Rigidbody of the child segment (the one carrying this component).")]
        public Rigidbody body;
        [Tooltip("Rigidbody of the parent segment.")]
        public Rigidbody parentBody;

        JointSettings settings = new JointSettings();

        // Unity's joint angle is the opposite of the project's convention.
        float sign = -1f;

        public JointSettings Settings => settings;

        public float TargetAngle { get; private set; }

        public void Configure(JointSettings jointSettings, bool invertSignConvention)
        {
            settings = jointSettings;
            sign = invertSignConvention ? 1f : -1f;

            float a = sign * settings.minAngle;
            float b = sign * settings.maxAngle;
            joint.lowAngularXLimit = new SoftJointLimit { limit = Mathf.Clamp(Mathf.Min(a, b), -177f, 177f) };
            joint.highAngularXLimit = new SoftJointLimit { limit = Mathf.Clamp(Mathf.Max(a, b), -177f, 177f) };
            joint.angularXDrive = new JointDrive
            {
                positionSpring = settings.stiffness,
                positionDamper = settings.damping,
                maximumForce = settings.maxTorque
            };
            SetTarget(settings.neutralAngle);
        }

        public void SetTarget(float angle)
        {
            TargetAngle = Mathf.Clamp(angle, settings.minAngle, settings.maxAngle);
            joint.targetRotation = Quaternion.Euler(sign * TargetAngle, 0f, 0f);
        }

        // Action 0 = neutral angle, +1 = max angle, -1 = min angle.
        public float ActionToAngle(float action)
        {
            action = Mathf.Clamp(action, -1f, 1f);
            float neutral = Mathf.Clamp(settings.neutralAngle, settings.minAngle, settings.maxAngle);
            return action >= 0f
                ? neutral + action * (settings.maxAngle - neutral)
                : neutral + action * (neutral - settings.minAngle);
        }

        public float Angle
        {
            get
            {
                Vector3 parentUp = parentBody.rotation * Vector3.up;
                Vector3 childUp = body.rotation * Vector3.up;
                float cross = parentUp.x * childUp.y - parentUp.y * childUp.x;
                float dot = parentUp.x * childUp.x + parentUp.y * childUp.y;
                return Mathf.Atan2(cross, dot) * Mathf.Rad2Deg;
            }
        }

        public float AngularVelocity => body.angularVelocity.z - parentBody.angularVelocity.z;

        public float NormalizedAngle
        {
            get
            {
                float range = Mathf.Max(1f, settings.maxAngle - settings.minAngle);
                return Mathf.Clamp(2f * (Angle - settings.minAngle) / range - 1f, -1.5f, 1.5f);
            }
        }

        // Estimated torque / max torque, used by the energy penalty.
        public float NormalizedEffort
        {
            get
            {
                if (settings.maxTorque <= 0f) return 0f;
                float torque = settings.stiffness * (TargetAngle - Angle) * Mathf.Deg2Rad - settings.damping * AngularVelocity;
                return Mathf.Clamp(torque / settings.maxTorque, -1f, 1f);
            }
        }
    }
}
