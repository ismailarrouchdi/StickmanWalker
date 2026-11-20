using System;
using UnityEngine;

namespace Stickman
{
    public enum BodyPartType
    {
        Torso,
        Head,
        LeftArm,
        RightArm,
        LeftThigh,
        RightThigh,
        LeftShin,
        RightShin,
        LeftFoot,
        RightFoot
    }

    // All joints rotate around the Z axis.
    public enum JointType
    {
        Neck,
        Shoulder,
        Hip,
        Knee,
        Ankle
    }

    // Part of a segment that ends the episode when it touches the ground.
    public enum FatalContactZone
    {
        None,

        Whole,

        UpperHalf
    }

    // Angles in degrees. Positive = counter-clockwise seen from the camera (forward).
    // The knee bends with negative angles.
    [Serializable]
    public class JointSettings
    {
        [Tooltip("Minimum allowed angle (degrees, geometric convention).")]
        public float minAngle = -45f;

        [Tooltip("Maximum allowed angle (degrees, geometric convention).")]
        public float maxAngle = 45f;

        [Tooltip("Target angle when the action is 0 (standing pose).")]
        public float neutralAngle;

        [Tooltip("Motor stiffness (N·m per radian of error).")]
        public float stiffness = 300f;

        [Tooltip("Motor damping (N·m per rad/s).")]
        public float damping = 20f;

        [Tooltip("Maximum torque the motor can produce (N·m).")]
        public float maxTorque = 100f;

        public JointSettings()
        {
        }

        public JointSettings(float minAngle, float maxAngle, float neutralAngle, float stiffness, float damping, float maxTorque)
        {
            this.minAngle = minAngle;
            this.maxAngle = maxAngle;
            this.neutralAngle = neutralAngle;
            this.stiffness = stiffness;
            this.damping = damping;
            this.maxTorque = maxTorque;
        }
    }
}
