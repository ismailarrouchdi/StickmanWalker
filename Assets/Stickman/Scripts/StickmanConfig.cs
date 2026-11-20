using UnityEngine;

namespace Stickman
{
    // All tunable parameters. The defaults are the settings of the v06 model.
    [CreateAssetMenu(fileName = "StickmanConfig", menuName = "Stickman/Configuration")]
    public class StickmanConfig : ScriptableObject
    {
        [Header("Body dimensions in metres (rebuild the scene after changing)")]
        [Tooltip("Foot length, heel to toe.")]
        public float footLength = 0.26f;
        public float footHeight = 0.06f;
        [Tooltip("Foot width (Z depth). Only affects visuals and contact stability.")]
        public float footWidth = 0.09f;
        [Tooltip("Ankle position along the foot, from the heel (0 = heel, 0.5 = middle).")]
        [Range(0.05f, 0.5f)] public float ankleFromHeel = 0.25f;
        [Tooltip("Ankle height above the ground.")]
        public float ankleHeight = 0.08f;
        public float shinLength = 0.43f;
        public float thighLength = 0.43f;
        [Tooltip("Hip-to-shoulder distance.")]
        public float torsoLength = 0.52f;
        public float neckLength = 0.06f;
        public float headRadius = 0.11f;
        [Tooltip("Arm length (shoulder to fingertips), as a single segment.")]
        public float armLength = 0.68f;
        [Tooltip("Capsule radius of the arms, thighs and shins.")]
        public float limbRadius = 0.045f;
        public float torsoRadius = 0.11f;
        [Tooltip("Visual depth (Z) offset between the left and right legs. Both legs share the same physical plane.")]
        public float legDepthOffset = 0.09f;
        [Tooltip("Visual depth (Z) offset between the left and right arms.")]
        public float armDepthOffset = 0.17f;

        [Header("Body masses in kg (≈ 70 kg in total, human proportions)")]
        public float headMass = 5f;
        [Tooltip("Torso and pelvis (≈ 50 % of human body mass).")]
        public float torsoMass = 34f;
        public float armMass = 4f;
        public float thighMass = 7f;
        public float shinMass = 3.5f;
        public float footMass = 1.1f;

        [Header("Joints (degrees; + = forward / counter-clockwise)")]
        [Tooltip("Hip: extension -35°, flexion up to 100°. Human peak torque ≈ 200-250 N·m.")]
        public JointSettings hip = new JointSettings(-35f, 100f, 0f, 600f, 50f, 250f);
        [Tooltip("Knee: bends backward only (negative values). +2° = very slight hyperextension.")]
        public JointSettings knee = new JointSettings(-135f, 2f, -5f, 500f, 30f, 200f);
        [Tooltip("Ankle: plantar flexion -45°, dorsiflexion +30°.")]
        public JointSettings ankle = new JointSettings(-45f, 30f, 0f, 300f, 15f, 120f);
        [Tooltip("Shoulder: arm back -80°, forward up to 60° (a walking arm rarely exceeds 45°).")]
        public JointSettings shoulder = new JointSettings(-80f, 60f, 0f, 150f, 12f, 60f);
        [Tooltip("Neck: passive joint (not driven by the agent), a spring that keeps the head aligned.")]
        public JointSettings neck = new JointSettings(-25f, 25f, 0f, 200f, 15f, 50f);
        [Tooltip("Tick ONLY if the knee bends forward in keyboard mode (different ConfigurableJoint sign convention). " +
                 "The agent detects this case automatically and reports it in the Console.")]
        public bool invertJointSignConvention;

        [Header("Physics")]
        [Tooltip("Physics timestep (s). 0.01 = 100 Hz, more stable than 50 Hz for an articulated body.")]
        [Range(0.002f, 0.04f)] public float fixedTimestep = 0.01f;
        [Min(1)] public int solverIterations = 12;
        [Min(1)] public int solverVelocityIterations = 4;
        [Tooltip("Maximum angular velocity of a segment (rad/s).")]
        public float maxAngularVelocity = 40f;
        public float linearDamping = 0.05f;
        public float angularDamping = 0.05f;
        [Tooltip("Foot friction (averaged with the ground's). Higher = less slipping.")]
        public float footFriction = 1.0f;
        [Tooltip("Friction of the other segments.")]
        public float bodyFriction = 0.5f;
        public float groundFriction = 0.8f;

        [Header("Agent")]
        [Tooltip("One decision every N physics steps (4 at 100 Hz = 25 decisions/s). The action is repeated in between.")]
        [Min(1)] public int decisionPeriod = 4;
        [Tooltip("Maximum episode length during training, in physics steps (3000 at 100 Hz = 30 s). " +
                 "Outside training (inference, keyboard) episodes are not time-limited.")]
        [Min(100)] public int maxEpisodeSteps = 3000;
        [Tooltip("Low-pass filter applied to the target angles at every physics step " +
                 "(1 = no smoothing, 0.3 ≈ 30 ms time constant). Makes movements less jerky.")]
        [Range(0.05f, 1f)] public float actionResponsiveness = 0.3f;
        [Tooltip("Target speed (m/s) when the trainer does not provide one (inference, keyboard). Human walking ≈ 1.3 m/s.")]
        public float defaultTargetSpeed = 1.3f;
        [Tooltip("Balance assistance when the trainer does not provide one. Keep 0 to judge the real gait.")]
        [Range(0f, 1f)] public float defaultBalanceAssist;
        [Tooltip("Duration (s) of the start ramp: the requested speed rises from 0 to the target, like a human starting " +
                 "to walk. 0 = target speed immediately.")]
        [Min(0f)] public float speedRampDuration = 3f;
        [Tooltip("Random tilt of the whole body at the start of each episode (± degrees), for robustness.")]
        public float initialTiltNoise = 3f;
        [Tooltip("Spawn height above the ground (m).")]
        public float spawnHeight = 0.01f;

        [Header("Balance assistance (curriculum 'training wheels', 0 in the last lesson)")]
        [Tooltip("Restoring torque applied to the torso per radian of tilt, multiplied by balance_assist (0..1).")]
        public float assistStiffness = 1000f;
        [Tooltip("Matching damping (N·m per rad/s).")]
        public float assistDamping = 100f;

        // Rewards are per second unless noted.
        [Header("Rewards: speed (per second)")]
        [Tooltip("Constant bonus while the agent is standing.")]
        public float aliveWeight = 0.05f;
        [Tooltip("Speed tracking: weight × exp(-((v - vTarget) / tolerance)²). Main term.")]
        public float speedWeight = 0.40f;
        [Tooltip("Speed error (m/s) tolerated by the Gaussian.")]
        [Min(0.01f)] public float speedTolerance = 0.30f;
        [Tooltip("Linear progress: weight × clamp(v / vTarget, -1, 1). Unlike the Gaussian, it rewards the first " +
                 "centimetres of motion (a way out of standing still), penalises walking backwards, and pays nothing " +
                 "above the target speed.")]
        public float progressWeight = 0.30f;
        [Tooltip("Time constant (s) of the speed used by every speed-related reward. ≈ one stride: rocking back and " +
                 "forth in place yields no average speed, hence no speed reward.")]
        [Min(0.05f)] public float speedSmoothingTime = 1.0f;
        [Tooltip("If ticked, the speed and progress terms are multiplied by the posture term: moving forward while " +
                 "leaning pays almost nothing.")]
        public bool speedRequiresPosture = true;

        [Header("Rewards: style (per second)")]
        [Tooltip("Upright torso: weight × exp(-(tilt / tolerance)²).")]
        public float postureWeight = 0.20f;
        [Tooltip("Tolerated torso tilt (degrees).")]
        [Min(0.1f)] public float tiltTolerance = 12f;
        [Tooltip("Head height: penalised only when the head drops below this fraction of its standing height.")]
        public float headHeightWeight = 0.25f;
        [Range(0.5f, 1f)] public float headHeightTargetFraction = 0.97f;
        [Tooltip("Tolerated height deficit (m).")]
        [Min(0.005f)] public float headHeightTolerance = 0.05f;
        [Tooltip("Stable head: weight × exp(-(head angular speed / tolerance)²).")]
        public float headStabilityWeight = 0.05f;
        [Tooltip("rad/s")]
        [Min(0.01f)] public float headAngularSpeedTolerance = 1.5f;
        [Tooltip("Arms swinging opposite to the legs: ideal shoulder angle = -gain × (hip difference) / 2.")]
        public float armSwingWeight = 0.15f;
        public float armSwingGain = 0.8f;
        [Tooltip("Tolerated difference (degrees) between an arm and its ideal opposite position.")]
        [Min(0.1f)] public float armSwingTolerance = 20f;
        [Tooltip("Straight stance leg: rewards a nearly straight knee on each foot touching the ground (no crouched gait).")]
        public float stanceKneeWeight = 0.10f;
        [Tooltip("Stance knee flexion allowed without penalty (degrees). Human: ≈ 5 to 20°.")]
        public float stanceKneeFlexionAllowance = 15f;
        [Tooltip("Extra flexion (degrees) tolerated by the Gaussian beyond the allowance.")]
        [Min(0.1f)] public float stanceKneeTolerance = 15f;
        [Tooltip("Share of the style rewards still paid when the agent stands still while a speed is requested. " +
                 "1 = style always pays in full (standing still becomes very profitable); 0.3 = style only pays in " +
                 "full at the target speed.")]
        [Range(0f, 1f)] public float styleShareWhenIdle = 0.3f;

        [Header("Rewards: steps (per event)")]
        [Tooltip("Bonus per valid step: the landing foot ends up ahead of the other one by enough, the other foot is on " +
                 "the ground, and the previous valid step was made by the other foot (strict alternation). Scaled by " +
                 "step length and cadence regularity.")]
        public float stepWeight = 0.20f;
        [Tooltip("Penalty when the same foot lands twice in a row (hopping, limping).")]
        public float sameFootPenalty = 0.10f;
        [Tooltip("Penalty when a foot lands without passing the other foot (shuffle / step-to gait).")]
        public float nonCrossingStepPenalty = 0.10f;
        [Tooltip("Absolute minimum distance (m) the landing foot must be ahead of the other foot.")]
        public float minStepLength = 0.15f;
        [Tooltip("Target step length = base + perSpeed × target speed (≈ 0.38 m at 0.5 m/s, ≈ 0.72 m at 1.3 m/s).")]
        public float targetStepLengthBase = 0.17f;
        public float targetStepLengthPerSpeed = 0.42f;
        [Tooltip("A step only counts if it reaches this fraction of the target step length.")]
        [Range(0f, 1f)] public float minStepLengthFraction = 0.75f;
        [Tooltip("Minimum time (s) a foot must spend in the air for its landing to count (filters micro-contacts).")]
        public float minSwingTime = 0.12f;
        [Tooltip("Tolerated difference (s) between two successive step durations (cadence regularity).")]
        [Min(0.01f)] public float cadenceTolerance = 0.15f;

        [Header("Penalties (per second, except jerk: per decision)")]
        [Tooltip("Energy: mean over the 8 motors of (torque / max torque)².")]
        public float energyWeight = 0.15f;
        [Tooltip("Jerk: mean of (action - previous action)² at each new decision.")]
        public float jerkWeight = 0.01f;
        [Tooltip("Slip: horizontal speed (m/s) of the supporting point of each foot on the ground.")]
        public float slipWeight = 0.30f;
        [Tooltip("Flight phase: both feet in the air at the same time (hopping, running).")]
        public float flightWeight = 0.50f;
        [Tooltip("Height (m) of the bottom of a foot above which it is considered too high (kicks, goose steps).")]
        public float maxFootHeight = 0.25f;
        [Tooltip("Penalty per second and per metre above maxFootHeight.")]
        public float highFootWeight = 5f;

        [Header("Episode termination")]
        [Tooltip("One-off penalty when falling.")]
        public float fallPenalty = 1.0f;
        [Tooltip("Fall when the torso tilts more than this angle (degrees).")]
        public float maxTilt = 60f;
        [Tooltip("Fall when the head drops below this fraction of its standing height.")]
        [Range(0.2f, 0.9f)] public float minHeadHeightFraction = 0.55f;
        [Tooltip("Stagnation check window (s). 0 = disabled.")]
        [Min(0f)] public float stagnationWindow = 5f;
        [Tooltip("The episode ends when, over one window, the centre of mass moves forward less than this fraction of " +
                 "(target speed × window). 0.25 at 1.3 m/s = 1.6 m in 5 s.")]
        [Range(0f, 1f)] public float stagnationMinProgress = 0.25f;

        [Header("Training areas (rebuild the scene after changing)")]
        [Range(1, 64)] public int areaCount = 16;
        [Tooltip("Areas are stacked vertically (m) so that the side camera only sees one of them.")]
        public float areaSpacing = 12f;
        [Tooltip("Total ground length (m).")]
        [Min(20f)] public float groundLength = 150f;
        [Tooltip("Ground length behind the starting point (m).")]
        [Min(0f)] public float groundBehindStart = 10f;

        public JointSettings GetJointSettings(JointType type)
        {
            switch (type)
            {
                case JointType.Neck: return neck;
                case JointType.Shoulder: return shoulder;
                case JointType.Hip: return hip;
                case JointType.Knee: return knee;
                default: return ankle;
            }
        }

        public float GetMass(BodyPartType part)
        {
            switch (part)
            {
                case BodyPartType.Torso: return torsoMass;
                case BodyPartType.Head: return headMass;
                case BodyPartType.LeftArm:
                case BodyPartType.RightArm: return armMass;
                case BodyPartType.LeftThigh:
                case BodyPartType.RightThigh: return thighMass;
                case BodyPartType.LeftShin:
                case BodyPartType.RightShin: return shinMass;
                default: return footMass;
            }
        }

        // Called by each agent at start-up.
        public void ApplyGlobalPhysics()
        {
            Time.fixedDeltaTime = fixedTimestep;
            Physics.defaultSolverIterations = solverIterations;
            Physics.defaultSolverVelocityIterations = solverVelocityIterations;
        }

        public static PhysicsMaterial CreatePhysicsMaterial(string name, float friction)
        {
            return new PhysicsMaterial(name)
            {
                dynamicFriction = friction,
                staticFriction = friction * 1.1f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }
    }
}
