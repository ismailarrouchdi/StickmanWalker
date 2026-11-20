using System;
using UnityEngine;

namespace Stickman
{
    // Side camera following one agent, with a HUD. Tab/N: next, B: previous, H: hide HUD.
    [RequireComponent(typeof(Camera))]
    public class StickmanFollowCamera : MonoBehaviour
    {
        [Tooltip("Index (in area order) of the followed agent.")]
        public int followedAgent;
        [Tooltip("Distance between the camera and the walking plane (m).")]
        public float distance = 4.5f;
        [Tooltip("Camera height above the ground (m).")]
        public float height = 1.0f;
        [Tooltip("Horizontal offset: > 0 shows more space in front of the stickman.")]
        public float lead = 0.5f;
        [Tooltip("Follow responsiveness (higher = tighter).")]
        public float smoothing = 5f;
        public bool showHud = true;

        StickmanAgent[] agents = Array.Empty<StickmanAgent>();
        GUIStyle hudStyle;

        void Start()
        {
            RefreshAgents();
            UpdatePosition(true);
        }

        void RefreshAgents()
        {
            agents = FindObjectsByType<StickmanAgent>(FindObjectsSortMode.None);
            Array.Sort(agents, (a, b) => a.AreaIndex.CompareTo(b.AreaIndex));
            if (agents.Length > 0) followedAgent = Mathf.Clamp(followedAgent, 0, agents.Length - 1);
        }

        void Update()
        {
            if (agents.Length == 0) return;
            if (StickmanInput.WasPressedThisFrame(KeyCode.Tab) || StickmanInput.WasPressedThisFrame(KeyCode.N))
                followedAgent = (followedAgent + 1) % agents.Length;
            if (StickmanInput.WasPressedThisFrame(KeyCode.B))
                followedAgent = (followedAgent - 1 + agents.Length) % agents.Length;
            if (StickmanInput.WasPressedThisFrame(KeyCode.H))
                showHud = !showHud;
        }

        void LateUpdate()
        {
            UpdatePosition(false);
        }

        void UpdatePosition(bool snap)
        {
            if (agents.Length == 0) return;
            var agent = agents[followedAgent];
            if (agent == null)
            {
                RefreshAgents();
                return;
            }

            Vector3 torso = agent.TorsoPosition;
            Vector3 target = new Vector3(torso.x + lead, agent.GroundHeight + height, torso.z - distance);

            // Snap after a teleport or an agent change.
            if (snap || (transform.position - target).sqrMagnitude > 9f)
                transform.position = target;
            else
                transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            transform.rotation = Quaternion.identity;
        }

        void OnGUI()
        {
            if (!showHud || agents.Length == 0) return;
            var agent = agents[followedAgent];
            if (agent == null) return;

            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14 };
                hudStyle.normal.textColor = Color.white;
            }

            GUI.Box(new Rect(10, 10, 470, 168), GUIContent.none);
            GUILayout.BeginArea(new Rect(20, 14, 455, 164));
            GUILayout.Label($"Followed agent: {followedAgent + 1}/{agents.Length}   " +
                            "(Tab/N: next, B: previous, H: hide)", hudStyle);
            GUILayout.Label($"Mean speed: {agent.SmoothedSpeed:0.00} m/s   (target {agent.CurrentTargetSpeed:0.00})   " +
                            $"instant: {agent.InstantSpeed:0.00}", hudStyle);
            GUILayout.Label($"Torso tilt: {agent.Tilt:+0;-0;0}°", hudStyle);
            string left = agent.IsLeftFootGrounded ? "●" : "○";
            string right = agent.IsRightFootGrounded ? "●" : "○";
            GUILayout.Label($"Feet on ground: L {left}   R {right}     " +
                            $"Valid steps: {agent.ValidSteps} (L {agent.LeftValidSteps} / R {agent.RightValidSteps})", hudStyle);
            GUILayout.Label($"Episode reward: {agent.GetCumulativeReward():0.00}   Steps: {agent.StepCount}", hudStyle);
            GUILayout.Label($"Balance assist: {agent.BalanceAssist:0.00}", hudStyle);
            GUILayout.EndArea();
        }
    }
}
