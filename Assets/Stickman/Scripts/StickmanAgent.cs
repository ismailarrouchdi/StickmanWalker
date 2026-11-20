using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace Stickman
{
    // Agent that learns to walk with PPO.
    public class StickmanAgent : Agent
    {
        public const int ActionCount = 8;
        public const int ObservationSize = 61;

        // Action order.
        public const int LeftHip = 0, LeftKnee = 1, LeftAnkle = 2, LeftShoulder = 3;
        public const int RightHip = 4, RightKnee = 5, RightAnkle = 6, RightShoulder = 7;

        const int Left = 0, Right = 1;
        static readonly int[] KneeIndices = { LeftKnee, RightKnee };

        // Below this target speed, the agent is asked to stand still.
        const float MinTargetSpeed = 0.05f;

        // No flight penalty while the body settles after spawning.
        const float FlightGracePeriod = 0.2f;

        [Tooltip("Shared configuration (body, joints, physics, rewards).")]
        public StickmanConfig config;

        // References
        StickmanArea area;
        StickmanBodyPart[] parts;
        Rigidbody[] bodies;
        Vector3[] restPositions;
        Quaternion[] restRotations;
        StickmanBodyPart torso, head, leftArm, rightArm;
        readonly StickmanBodyPart[] feet = new StickmanBodyPart[2];
        readonly Vector3[] footHalfSize = new Vector3[2];
        readonly StickmanJoint[] joints = new StickmanJoint[ActionCount];
        StickmanJoint neck;
        float totalMass, standingHeadHeight, standingTorsoHeight;
        bool isReady, isTraining;

        // Episode state
        readonly float[] previousActions = new float[ActionCount];
        readonly float[] filteredTargets = new float[ActionCount];
        float targetSpeed, currentTargetSpeed, balanceAssist, episodeTime, smoothedSpeed;
        readonly bool[] wasGrounded = new bool[2];
        readonly float[] liftOffTime = new float[2];
        int lastLandedFoot, lastValidStepFoot;
        float lastLandingTime, lastValidStepTime, previousStepDuration;
        float stagnationCheckX, stagnationCheckTime;

        // TensorBoard statistics
        readonly int[] validStepsPerFoot = new int[2];
        float speedSum, tiltSum, flightTime;
        int sampleCount, sameFootLandings, nonCrossingSteps;
        bool endedByStagnation;
        bool observationWarningLogged;

        // One-off check of the joint sign convention, shared by all agents.
        static float kneeAngleSum;
        static int kneeSampleCount;
        static bool signCheckDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            kneeAngleSum = 0f;
            kneeSampleCount = 0;
            signCheckDone = false;
        }

        // Read by the camera HUD.
        public float InstantSpeed { get; private set; }
        public float SmoothedSpeed => smoothedSpeed;
        public float Tilt { get; private set; }
        public float CurrentTargetSpeed => currentTargetSpeed;
        public float BalanceAssist => balanceAssist;
        public int ValidSteps => validStepsPerFoot[Left] + validStepsPerFoot[Right];
        public int LeftValidSteps => validStepsPerFoot[Left];
        public int RightValidSteps => validStepsPerFoot[Right];
        public int AreaIndex => area != null ? area.index : 0;
        public float GroundHeight => area != null ? area.GroundHeight : 0f;
        public Vector3 TorsoPosition => torso != null ? torso.Body.position : transform.position;
        public bool IsLeftFootGrounded => feet[Left] != null && feet[Left].IsGrounded;
        public bool IsRightFootGrounded => feet[Right] != null && feet[Right].IsGrounded;

        public override void Initialize()
        {
            if (config == null)
            {
                Debug.LogWarning("[Stickman] No StickmanConfig assigned to the agent: using default values.", this);
                config = ScriptableObject.CreateInstance<StickmanConfig>();
            }
            config.ApplyGlobalPhysics();

            // The time limit and the stagnation check only apply during training.
            isTraining = Academy.Instance.IsCommunicatorOn;
            MaxStep = isTraining ? config.maxEpisodeSteps : 0;
            var decisionRequester = GetComponent<DecisionRequester>();
            if (decisionRequester != null)
            {
                decisionRequester.DecisionPeriod = config.decisionPeriod;
                decisionRequester.TakeActionsBetweenDecisions = true;
            }

            area = GetComponentInParent<StickmanArea>();
            parts = GetComponentsInChildren<StickmanBodyPart>();
            SetUpBodyParts();
            SetUpJoints();

            isReady = area != null && area.ground != null && torso != null && head != null &&
                      leftArm != null && rightArm != null && feet[Left] != null && feet[Right] != null && neck != null;
            for (int i = 0; i < ActionCount; i++) isReady &= joints[i] != null;
            if (!isReady)
            {
                Debug.LogError("[Stickman] Incomplete stickman: rebuild the scene with " +
                               "'Tools > Stickman > Build Training Scene'.", this);
                return;
            }

            area.ground.sharedMaterial = StickmanConfig.CreatePhysicsMaterial("Stickman_Ground", config.groundFriction);

            // Body parts never collide with each other.
            var colliders = GetComponentsInChildren<Collider>();
            for (int a = 0; a < colliders.Length; a++)
                for (int b = a + 1; b < colliders.Length; b++)
                    Physics.IgnoreCollision(colliders[a], colliders[b], true);

            // Remember the standing pose for episode resets.
            Vector3 origin = area.transform.position;
            restPositions = new Vector3[parts.Length];
            restRotations = new Quaternion[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                restPositions[i] = bodies[i].transform.position - origin;
                restRotations[i] = bodies[i].transform.rotation;
            }

            for (int f = 0; f < 2; f++)
            {
                var box = feet[f].GetComponent<BoxCollider>();
                footHalfSize[f] = box != null
                    ? new Vector3(box.size.x * 0.5f, box.size.y * 0.5f, 0f)
                    : new Vector3(config.footLength * 0.5f, config.footHeight * 0.5f, 0f);
            }

            standingHeadHeight = Mathf.Max(0.1f, head.transform.position.y - origin.y);
            standingTorsoHeight = Mathf.Max(0.1f, torso.transform.position.y - origin.y);
        }

        // Applies the configuration to each body part and keeps references to them.
        void SetUpBodyParts()
        {
            bodies = new Rigidbody[parts.Length];
            var footMaterial = StickmanConfig.CreatePhysicsMaterial("Stickman_Feet", config.footFriction);
            var bodyMaterial = StickmanConfig.CreatePhysicsMaterial("Stickman_Body", config.bodyFriction);
            totalMass = 0f;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var rb = part.Body;
                bodies[i] = rb;
                rb.mass = config.GetMass(part.part);
                rb.linearDamping = config.linearDamping;
                rb.angularDamping = config.angularDamping;
                rb.maxAngularVelocity = config.maxAngularVelocity;
                rb.solverIterations = config.solverIterations;
                rb.solverVelocityIterations = config.solverVelocityIterations;
                rb.sleepThreshold = 0f;
                rb.constraints = StickmanBodyPart.PlanarConstraints;
                totalMass += rb.mass;

                bool isFoot = part.part == BodyPartType.LeftFoot || part.part == BodyPartType.RightFoot;
                var col = part.GetComponent<Collider>();
                if (col != null) col.sharedMaterial = isFoot ? footMaterial : bodyMaterial;

                switch (part.part)
                {
                    case BodyPartType.Torso: torso = part; break;
                    case BodyPartType.Head: head = part; break;
                    case BodyPartType.LeftArm: leftArm = part; break;
                    case BodyPartType.RightArm: rightArm = part; break;
                    case BodyPartType.LeftFoot: feet[Left] = part; break;
                    case BodyPartType.RightFoot: feet[Right] = part; break;
                }
            }
        }

        // Configures the joints and stores the driven ones in action order.
        void SetUpJoints()
        {
            foreach (var joint in GetComponentsInChildren<StickmanJoint>())
            {
                joint.Configure(config.GetJointSettings(joint.type), config.invertJointSignConvention);
                switch (joint.type)
                {
                    case JointType.Neck: neck = joint; break;
                    case JointType.Hip: joints[joint.isLeft ? LeftHip : RightHip] = joint; break;
                    case JointType.Knee: joints[joint.isLeft ? LeftKnee : RightKnee] = joint; break;
                    case JointType.Ankle: joints[joint.isLeft ? LeftAnkle : RightAnkle] = joint; break;
                    case JointType.Shoulder: joints[joint.isLeft ? LeftShoulder : RightShoulder] = joint; break;
                }
            }
        }

        public override void OnEpisodeBegin()
        {
            if (!isReady) return;

            // Statistics of the episode that just ended.
            RecordStatistics();

            // Curriculum values (defaults outside training).
            var parameters = Academy.Instance.EnvironmentParameters;
            targetSpeed = parameters.GetWithDefault("target_speed", config.defaultTargetSpeed);
            balanceAssist = Mathf.Clamp01(parameters.GetWithDefault("balance_assist", config.defaultBalanceAssist));
            currentTargetSpeed = config.speedRampDuration > 0f ? 0f : targetSpeed;

            ResetPose();

            for (int i = 0; i < ActionCount; i++)
            {
                previousActions[i] = 0f;
                filteredTargets[i] = joints[i].ActionToAngle(0f);
                joints[i].SetTarget(filteredTargets[i]);
            }
            neck.SetTarget(neck.Settings.neutralAngle);

            episodeTime = 0f;
            smoothedSpeed = 0f;
            for (int f = 0; f < 2; f++)
            {
                wasGrounded[f] = false;
                liftOffTime[f] = 0f;
                validStepsPerFoot[f] = 0;
            }
            lastLandedFoot = lastValidStepFoot = -1;
            lastLandingTime = lastValidStepTime = 0f;
            previousStepDuration = -1f;
            stagnationCheckX = area.transform.position.x;
            stagnationCheckTime = 0f;

            speedSum = tiltSum = flightTime = 0f;
            sampleCount = sameFootLandings = nonCrossingSteps = 0;
            endedByStagnation = false;
        }

        // Puts the body back in the standing pose, with a small random tilt.
        void ResetPose()
        {
            Vector3 origin = area.transform.position;
            float angle = Random.Range(-config.initialTiltNoise, config.initialTiltNoise);
            Quaternion tilt = Quaternion.Euler(0f, 0f, angle);
            Vector3 lift = Vector3.up * config.spawnHeight;

            for (int i = 0; i < parts.Length; i++)
            {
                Vector3 position = origin + tilt * restPositions[i] + lift;
                Quaternion rotation = tilt * restRotations[i];
                var rb = bodies[i];
                rb.transform.SetPositionAndRotation(position, rotation);
                rb.position = position;
                rb.rotation = rotation;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                parts[i].ResetContacts();
            }
        }

        // 61 observations. Trained models depend on their order and scale.
        public override void CollectObservations(VectorSensor sensor)
        {
            int count = 0;
            void Add(float value)
            {
                sensor.AddObservation(float.IsNaN(value) || float.IsInfinity(value) ? 0f : value);
                count++;
            }

            if (!isReady)
            {
                for (int i = 0; i < ObservationSize; i++) Add(0f);
                return;
            }

            float ground = GroundHeight;
            Rigidbody torsoBody = torso.Body;
            float tilt = ComputeTorsoTilt();
            Vector3 comVelocity = CenterOfMassVelocity();
            Vector3 com = CenterOfMass();

            // Torso
            Add((torsoBody.position.y - ground) / standingTorsoHeight);
            Add(Mathf.Sin(tilt * Mathf.Deg2Rad));
            Add(Mathf.Cos(tilt * Mathf.Deg2Rad));
            Add(torsoBody.angularVelocity.z / 5f);

            // Head
            Add((head.Body.position.y - ground) / standingHeadHeight);
            Add(neck.Angle / 45f);

            // Velocities and forward direction
            Add(comVelocity.x / 2f);
            Add(comVelocity.y / 2f);
            Add(torsoBody.linearVelocity.x / 2f);
            Add(torsoBody.linearVelocity.y / 2f);

            Vector3 forward = Quaternion.Inverse(torsoBody.rotation) * Vector3.right;
            Add(forward.x);
            Add(forward.y);

            // Target speed and speed error
            Add(currentTargetSpeed / 2f);
            Add((smoothedSpeed - currentTargetSpeed) / 2f);

            for (int i = 0; i < ActionCount; i++)
            {
                // Joints
                Add(joints[i].NormalizedAngle);
                Add(joints[i].AngularVelocity / 10f);
            }

            Add(neck.NormalizedAngle);
            Add(neck.AngularVelocity / 10f);

            for (int f = 0; f < 2; f++)
            {
                // Feet
                var foot = feet[f];
                Vector3 relative = foot.Body.position - torsoBody.position;
                Vector3 velocity = foot.Body.linearVelocity;
                Add(foot.IsGrounded ? 1f : 0f);
                Add(relative.x);
                Add(relative.y);
                Add(velocity.x / 3f);
                Add(velocity.y / 3f);
                Add(Mathf.Clamp((FootBottomHeight(f) - ground) * 5f, -1f, 3f));
            }

            // Hands
            Vector3 leftHand = HandPosition(leftArm) - torsoBody.position;
            Vector3 rightHand = HandPosition(rightArm) - torsoBody.position;
            Add(leftHand.x);
            Add(leftHand.y);
            Add(rightHand.x);
            Add(rightHand.y);

            // Gait phase, balance, previous actions, assist, center of mass height
            Add(lastLandedFoot == Left ? -1f : lastLandedFoot == Right ? 1f : 0f);
            Add(Mathf.Clamp01(episodeTime - lastLandingTime));

            float feetMidpoint = 0.5f * (feet[Left].Body.position.x + feet[Right].Body.position.x);
            Add(Mathf.Clamp((com.x - feetMidpoint) / 0.5f, -3f, 3f));

            for (int i = 0; i < ActionCount; i++) Add(previousActions[i]);

            Add(balanceAssist);

            Add((com.y - ground) / standingTorsoHeight);

            if (count != ObservationSize && !observationWarningLogged)
            {
                observationWarningLogged = true;
                Debug.LogError($"[Stickman] {count} observations produced instead of {ObservationSize}.", this);
            }
        }

        public override void OnActionReceived(ActionBuffers actionBuffers)
        {
            if (!isReady) return;
            float dt = Time.fixedDeltaTime;
            episodeTime += dt;

            // Start ramp: the target speed rises from 0.
            currentTargetSpeed = config.speedRampDuration > 0f
                ? targetSpeed * Mathf.Clamp01(episodeTime / config.speedRampDuration)
                : targetSpeed;

            // Actions -> smoothed joint targets
            var actions = actionBuffers.ContinuousActions;
            float jerk = 0f;
            for (int i = 0; i < ActionCount; i++)
            {
                float action = Mathf.Clamp(actions[i], -1f, 1f);
                float delta = action - previousActions[i];
                jerk += delta * delta;
                previousActions[i] = action;

                filteredTargets[i] += config.actionResponsiveness * (joints[i].ActionToAngle(action) - filteredTargets[i]);
                joints[i].SetTarget(filteredTargets[i]);
            }
            jerk /= ActionCount;
            neck.SetTarget(neck.Settings.neutralAngle);

            // Current state
            Vector3 comVelocity = CenterOfMassVelocity();
            InstantSpeed = comVelocity.x;

            // Speed smoothed over about one stride: rocking in place gives ~0.
            float alpha = Mathf.Clamp01(dt / Mathf.Max(dt, config.speedSmoothingTime));
            smoothedSpeed += alpha * (comVelocity.x - smoothedSpeed);
            Tilt = ComputeTorsoTilt();
            float headHeight = head.Body.position.y - GroundHeight;

            ApplyBalanceAssist();
            CheckJointSignConvention();

            // End of episode: fall, end of ground or stagnation
            if (HasFallen(headHeight))
            {
                AddReward(-config.fallPenalty);
                EndEpisode();
                return;
            }
            float localX = torso.Body.position.x - area.transform.position.x;
            if (localX > area.groundEnd - 2f || localX < area.groundStart + 1f)
            {
                EpisodeInterrupted();
                return;
            }
            if (IsStagnating())
            {
                endedByStagnation = true;
                EndEpisode();
                return;
            }

            // Steps
            DetectLandings();

            // Rewards (per second, multiplied by dt)
            bool speedRequested = targetSpeed > MinTargetSpeed;
            float speedDivisor = Mathf.Max(0.1f, currentTargetSpeed);

            float speedReward = Gaussian(smoothedSpeed - currentTargetSpeed, config.speedTolerance);

            float progress = speedRequested ? Mathf.Clamp(smoothedSpeed / speedDivisor, -1f, 1f) : 0f;

            float postureReward = Gaussian(Tilt, config.tiltTolerance);
            float postureFactor = config.speedRequiresPosture ? postureReward : 1f;

            float headDeficit = Mathf.Max(0f, config.headHeightTargetFraction * standingHeadHeight - headHeight);
            float headHeightReward = Gaussian(headDeficit, config.headHeightTolerance);

            float headStabilityReward = Gaussian(head.Body.angularVelocity.z, config.headAngularSpeedTolerance);

            // Arms swing opposite to the legs.
            float hipDifference = joints[LeftHip].Angle - joints[RightHip].Angle;
            float idealLeftShoulder = -config.armSwingGain * 0.5f * hipDifference;
            float armError = 0.5f * (Mathf.Abs(joints[LeftShoulder].Angle - idealLeftShoulder) +
                                     Mathf.Abs(joints[RightShoulder].Angle + idealLeftShoulder));
            float armSwingReward = Gaussian(armError, config.armSwingTolerance);

            // Stance leg nearly straight.
            float stanceKneeReward = 0f;
            int stanceCount = 0;
            for (int f = 0; f < 2; f++)
            {
                if (!feet[f].IsGrounded) continue;
                float flexion = -joints[f == Left ? LeftKnee : RightKnee].Angle;
                stanceKneeReward += Gaussian(Mathf.Max(0f, flexion - config.stanceKneeFlexionAllowance),
                    config.stanceKneeTolerance);
                stanceCount++;
            }
            stanceKneeReward = stanceCount > 0 ? stanceKneeReward / stanceCount : 1f;

            // Style only pays in full when walking at the target speed.
            float style = config.postureWeight * postureReward
                          + config.headHeightWeight * headHeightReward
                          + config.headStabilityWeight * headStabilityReward
                          + config.armSwingWeight * armSwingReward
                          + config.stanceKneeWeight * stanceKneeReward;
            float speedRatio = speedRequested ? Mathf.Clamp01(smoothedSpeed / speedDivisor) : 1f;
            float styleFactor = Mathf.Lerp(config.styleShareWhenIdle, 1f, speedRatio);

            float positive = config.aliveWeight
                             + config.speedWeight * speedReward * postureFactor
                             + config.progressWeight * progress * postureFactor
                             + style * styleFactor;

            // Penalties
            float effort = 0f;
            for (int i = 0; i < ActionCount; i++)
            {
                float e = joints[i].NormalizedEffort;
                effort += e * e;
            }
            effort /= ActionCount;

            float slip = 0f;
            for (int f = 0; f < 2; f++)
                if (feet[f].IsGrounded) slip += FootSlipSpeed(f);

            bool inFlight = !feet[Left].IsGrounded && !feet[Right].IsGrounded && episodeTime > FlightGracePeriod;

            float footExcessHeight = 0f;
            for (int f = 0; f < 2; f++)
                footExcessHeight += Mathf.Max(0f, FootBottomHeight(f) - GroundHeight - config.maxFootHeight);

            float negative = config.energyWeight * effort
                             + config.slipWeight * slip
                             + config.flightWeight * (inFlight ? 1f : 0f)
                             + config.highFootWeight * footExcessHeight;

            AddReward((positive - negative) * dt);

            // Jerk: once per decision (zero while an action is repeated).
            AddReward(-config.jerkWeight * jerk);

            // Statistics
            speedSum += comVelocity.x;
            tiltSum += Mathf.Abs(Tilt);
            if (inFlight) flightTime += dt;
            sampleCount++;
        }

        // A landing counts after at least minSwingTime in the air.
        void DetectLandings()
        {
            for (int f = 0; f < 2; f++)
            {
                bool grounded = feet[f].IsGrounded;
                if (!grounded && wasGrounded[f])
                    liftOffTime[f] = episodeTime;

                if (grounded && !wasGrounded[f] && episodeTime - liftOffTime[f] >= config.minSwingTime)
                    OnFootLanded(f);

                wasGrounded[f] = grounded;
            }
        }

        // Rewards alternating steps and penalizes hopping and shuffling.
        void OnFootLanded(int foot)
        {
            if (targetSpeed > MinTargetSpeed)
            {
                if (foot == lastLandedFoot)
                {
                    AddReward(-config.sameFootPenalty);
                    sameFootLandings++;
                }
                else
                {
                    int other = 1 - foot;
                    float advance = feet[foot].Body.position.x - feet[other].Body.position.x;
                    float targetLength = config.targetStepLengthBase + config.targetStepLengthPerSpeed * currentTargetSpeed;
                    float minLength = Mathf.Max(config.minStepLength, config.minStepLengthFraction * targetLength);
                    bool isValid = advance >= minLength && feet[other].IsGrounded;

                    // Strict alternation: the previous valid step must be by the other foot.
                    if (isValid && foot != lastValidStepFoot)
                    {
                        bool isFirstStep = lastValidStepFoot < 0;
                        float duration = episodeTime - lastValidStepTime;
                        float regularity = previousStepDuration > 0f
                            ? Gaussian(duration - previousStepDuration, config.cadenceTolerance)
                            : 1f;
                        float lengthFactor = Mathf.Clamp01(advance / Mathf.Max(0.05f, targetLength));
                        AddReward(config.stepWeight * lengthFactor * (0.5f + 0.5f * regularity));

                        // The first step is not a cadence reference.
                        previousStepDuration = isFirstStep ? -1f : duration;
                        lastValidStepTime = episodeTime;
                        lastValidStepFoot = foot;
                        validStepsPerFoot[foot]++;
                    }
                    else if (advance < config.minStepLength && episodeTime > config.speedRampDuration)
                    {
                        // The foot landed without passing the other one.
                        AddReward(-config.nonCrossingStepPenalty);
                        nonCrossingSteps++;
                    }
                }
            }
            lastLandedFoot = foot;
            lastLandingTime = episodeTime;
        }

        // True if the body did not move forward enough over the last window (training only).
        bool IsStagnating()
        {
            if (!isTraining || config.stagnationWindow <= 0f || targetSpeed <= MinTargetSpeed) return false;
            if (episodeTime < config.speedRampDuration) return false;
            if (episodeTime - stagnationCheckTime < config.stagnationWindow) return false;

            float x = CenterOfMass().x;
            float expectedAdvance = config.stagnationMinProgress * targetSpeed * config.stagnationWindow;
            bool stagnating = x - stagnationCheckX < expectedAdvance;
            stagnationCheckX = x;
            stagnationCheckTime = episodeTime;
            return stagnating;
        }

        bool HasFallen(float headHeight)
        {
            for (int i = 0; i < parts.Length; i++)
                if (parts[i].HasFatalContact) return true;
            return Mathf.Abs(Tilt) > config.maxTilt || headHeight < config.minHeadHeightFraction * standingHeadHeight;
        }

        // Curriculum help: torque that keeps the torso upright.
        void ApplyBalanceAssist()
        {
            if (balanceAssist <= 0f) return;
            float torsoAngle = -Tilt * Mathf.Deg2Rad;
            float torque = -balanceAssist * (config.assistStiffness * torsoAngle +
                                             config.assistDamping * torso.Body.angularVelocity.z);
            torso.Body.AddTorque(0f, 0f, torque, ForceMode.Force);
        }

        // Warns once if the knees bend the wrong way.
        void CheckJointSignConvention()
        {
            if (signCheckDone) return;
            foreach (int index in KneeIndices)
            {
                if (joints[index].TargetAngle < -40f)
                {
                    kneeAngleSum += joints[index].Angle;
                    kneeSampleCount++;
                }
            }
            if (kneeSampleCount < 3000) return;

            signCheckDone = true;
            float mean = kneeAngleSum / kneeSampleCount;
            if (mean > -10f)
                Debug.LogError($"[Stickman] The knees do not bend in the commanded direction (mean angle {mean:0}° for a " +
                               "target below -40°). The ConfigurableJoint sign convention seems to differ in this Unity " +
                               "version: toggle 'Invert Joint Sign Convention' in StickmanConfig, then check with the " +
                               "keyboard (W / S) that the knee bends backward.");
            else
                Debug.Log($"[Stickman] Joint sign convention checked: OK (mean knee angle {mean:0}°).");
        }

        // Keyboard control (QWERTY positions): top row = +1, middle row = -1.
        public override void Heuristic(in ActionBuffers actionsOut)
        {
            var actions = actionsOut.ContinuousActions;
            actions[LeftHip] = KeyAxis(KeyCode.Q, KeyCode.A);
            actions[LeftKnee] = KeyAxis(KeyCode.W, KeyCode.S);
            actions[LeftAnkle] = KeyAxis(KeyCode.E, KeyCode.D);
            actions[LeftShoulder] = KeyAxis(KeyCode.R, KeyCode.F);
            actions[RightHip] = KeyAxis(KeyCode.U, KeyCode.J);
            actions[RightKnee] = KeyAxis(KeyCode.I, KeyCode.K);
            actions[RightAnkle] = KeyAxis(KeyCode.O, KeyCode.L);
            actions[RightShoulder] = KeyAxis(KeyCode.P, KeyCode.Semicolon);
        }

        static float KeyAxis(KeyCode positive, KeyCode negative)
        {
            float value = 0f;
            if (StickmanInput.IsPressed(positive)) value += 1f;
            if (StickmanInput.IsPressed(negative)) value -= 1f;
            return value;
        }

        // TensorBoard statistics ("Stickman" section).
        void RecordStatistics()
        {
            if (sampleCount == 0) return;
            var stats = Academy.Instance.StatsRecorder;
            float duration = sampleCount * Time.fixedDeltaTime;
            int totalSteps = ValidSteps;

            stats.Add("Stickman/Mean speed (m per s)", speedSum / sampleCount);
            stats.Add("Stickman/Distance (m)", CenterOfMass().x - area.transform.position.x);
            stats.Add("Stickman/Valid steps per second", totalSteps / duration);
            if (totalSteps > 0)
                stats.Add("Stickman/Left-right step asymmetry (0 = perfect)",
                    Mathf.Abs(validStepsPerFoot[Left] - validStepsPerFoot[Right]) / (float)totalSteps);
            stats.Add("Stickman/Same-foot landings per second", sameFootLandings / duration);
            stats.Add("Stickman/Non-crossing steps per second", nonCrossingSteps / duration);
            stats.Add("Stickman/Flight time fraction", flightTime / duration);
            stats.Add("Stickman/Mean torso tilt (deg)", tiltSum / sampleCount);
            stats.Add("Stickman/Ended by stagnation (fraction of episodes)", endedByStagnation ? 1f : 0f);
            stats.Add("Stickman/Episode duration (s)", duration);
            stats.Add("Stickman/Target speed", targetSpeed);
            stats.Add("Stickman/Balance assist", balanceAssist);
        }

        static float Gaussian(float x, float tolerance)
        {
            float r = x / Mathf.Max(1e-4f, tolerance);
            return Mathf.Exp(-r * r);
        }

        // Degrees, positive = leaning forward.
        float ComputeTorsoTilt()
        {
            Vector3 up = torso.Body.rotation * Vector3.up;
            return Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg;
        }

        Vector3 CenterOfMass()
        {
            Vector3 sum = Vector3.zero;
            foreach (var rb in bodies) sum += rb.worldCenterOfMass * rb.mass;
            return sum / totalMass;
        }

        Vector3 CenterOfMassVelocity()
        {
            Vector3 sum = Vector3.zero;
            foreach (var rb in bodies) sum += rb.linearVelocity * rb.mass;
            return sum / totalMass;
        }

        static Vector3 HandPosition(StickmanBodyPart arm)
        {
            return arm.transform.TransformPoint(new Vector3(0f, -0.5f * arm.length, 0f));
        }

        void GetFootCorners(int foot, out Vector3 heel, out Vector3 toe)
        {
            Transform t = feet[foot].transform;
            Vector3 half = footHalfSize[foot];
            heel = t.TransformPoint(new Vector3(-half.x, -half.y, 0f));
            toe = t.TransformPoint(new Vector3(half.x, -half.y, 0f));
        }

        float FootBottomHeight(int foot)
        {
            GetFootCorners(foot, out Vector3 heel, out Vector3 toe);
            return Mathf.Min(heel.y, toe.y);
        }

        // Horizontal speed of the least moving foot corner.
        float FootSlipSpeed(int foot)
        {
            GetFootCorners(foot, out Vector3 heel, out Vector3 toe);
            var rb = feet[foot].Body;
            return Mathf.Min(Mathf.Abs(rb.GetPointVelocity(heel).x), Mathf.Abs(rb.GetPointVelocity(toe).x));
        }
    }
}
