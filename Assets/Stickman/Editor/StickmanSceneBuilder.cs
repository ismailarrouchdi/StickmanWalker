using System.Collections.Generic;
using System.IO;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Stickman.EditorTools
{
    // Tools > Stickman menu: builds the stickman, the training areas and the scenes.
    public static class StickmanSceneBuilder
    {
        const string Root = "Assets/Stickman";
        const string ConfigPath = Root + "/Config/StickmanConfig.asset";
        const string MaterialsFolder = Root + "/Materials";
        const string PrefabPath = Root + "/Prefabs/TrainingArea.prefab";
        internal const string TrainingScenePath = Root + "/Scenes/StickmanTraining.unity";
        const string DemoScenePath = Root + "/Scenes/StickmanDemo.unity";
        const string DefaultModelPath = Root + "/Models/StickmanWalker_v06.onnx";
        const string GroundTexturePath = MaterialsFolder + "/GroundStripes.png";
        static readonly string[] Subfolders = { "Config", "Materials", "Prefabs", "Scenes", "Models" };

        // Must match the behavior name in the YAML files.
        public const string BehaviorName = "StickmanWalker";

        class Materials
        {
            public Material body, head, left, right, ground, eye;
        }

        [MenuItem("Tools/Stickman/Build Training Scene", priority = 1)]
        public static void BuildTrainingScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene(false, null);
            EditorUtility.DisplayDialog("Stickman", "Training scene built.\n\n" +
                "• Play alone: keyboard control (heuristic).\n• Training: start mlagents-learn, then press Play.", "OK");
        }

        [MenuItem("Tools/Stickman/Build Demo Scene (selected or default .onnx model)", priority = 2)]
        public static void BuildDemoScene()
        {
            Object model = GetSelectedModel();
            if (model == null) model = AssetDatabase.LoadMainAssetAtPath(DefaultModelPath);
            if (model == null &&
                !EditorUtility.DisplayDialog("Stickman",
                    $"No .onnx file is selected in the Project window and {DefaultModelPath} was not found.\n" +
                    "The demo scene will be built without a model (the stickman will be keyboard-controlled).",
                    "Continue", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BuildScene(true, model);
            EditorUtility.DisplayDialog("Stickman",
                model != null ? $"Demo scene built with model '{model.name}'.\n\nPress Play to watch the stickman."
                              : "Demo scene built without a model.", "OK");
        }

        // Used the first time the project is opened.
        internal static void BuildDefaultScenes()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildScene(false, null);
            Object model = AssetDatabase.LoadMainAssetAtPath(DefaultModelPath);
            if (model != null) BuildScene(true, model);
        }

        [MenuItem("Tools/Stickman/Assign Selected .onnx Model to Agents in Open Scene", priority = 20)]
        public static void AssignSelectedModel()
        {
            Object model = GetSelectedModel();
            if (model == null)
            {
                EditorUtility.DisplayDialog("Stickman",
                    "First select the .onnx file (imported in the project) in the Project window.", "OK");
                return;
            }

            int count = 0;
            var agents = Object.FindObjectsByType<BehaviorParameters>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var bp in agents)
            {
                if (bp.BehaviorName == BehaviorName && AssignModel(bp, model)) count++;
            }

            if (count > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("Stickman",
                count > 0
                    ? $"Model '{model.name}' assigned to {count} agent(s) in Inference Only mode.\n" +
                      "Press Play to watch it walk."
                    : "No agent could receive the model. " +
                      "Drag the .onnx into the 'Model' field of the Behavior Parameters component.",
                "OK");
        }

        [MenuItem("Tools/Stickman/Select Configuration", priority = 40)]
        public static void SelectConfiguration()
        {
            var config = LoadOrCreateConfig();
            Selection.activeObject = config;
            EditorGUIUtility.PingObject(config);
        }

        // Training scene: all areas. Demo scene: one area, with the model if given.
        static void BuildScene(bool demo, Object model)
        {
            var config = LoadOrCreateConfig();
            var materials = CreateMaterials(config);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            GameObject prefab = BuildAreaPrefab(config, materials);

            int areaCount = demo ? 1 : config.areaCount;
            for (int i = 0; i < areaCount; i++)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.name = $"TrainingArea_{i:00}";
                instance.transform.position = new Vector3(0f, i * config.areaSpacing, 0f);
                var area = instance.GetComponent<StickmanArea>();
                area.index = i;
                PrefabUtility.RecordPrefabInstancePropertyModifications(area);
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);

                if (model != null)
                    AssignModel(instance.GetComponentInChildren<BehaviorParameters>(), model);
            }

            SetUpCamera();
            SetUpLight();

            string path = demo ? DemoScenePath : TrainingScenePath;
            EditorSceneManager.SaveScene(scene, path);
            AddToBuildSettings(path);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Stickman] Scene built: {path} ({areaCount} area(s)" +
                      (model != null ? $", model '{model.name}')." : ")."));
        }

        static GameObject BuildAreaPrefab(StickmanConfig c, Materials m)
        {
            var root = new GameObject("TrainingArea");
            var area = root.AddComponent<StickmanArea>();

            float start = -c.groundBehindStart;
            float end = start + c.groundLength;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(0.5f * (start + end), -0.5f, 0f);
            ground.transform.localScale = new Vector3(end - start, 1f, 1.2f);
            ground.GetComponent<Renderer>().sharedMaterial = m.ground;
            ground.AddComponent<StickmanGround>();
            area.ground = ground.GetComponent<Collider>();
            area.groundStart = start;
            area.groundEnd = end;

            BuildStickman(root.transform, c, m);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // Standing stickman facing +X, right side towards the camera.
        static void BuildStickman(Transform parent, StickmanConfig c, Materials m)
        {
            var root = new GameObject("Stickman");
            root.transform.SetParent(parent, false);

            float kneeY = c.ankleHeight + c.shinLength;
            float hipY = kneeY + c.thighLength;
            float shoulderY = hipY + c.torsoLength;
            float headY = shoulderY + c.neckLength + c.headRadius;

            var torso = CreatePart(root, "Torso", BodyPartType.Torso, new Vector3(0f, hipY + 0.5f * c.torsoLength, 0f),
                c, FatalContactZone.Whole, c.torsoLength);
            AddCapsule(torso, c.torsoRadius, c.torsoLength, m.body);

            var head = CreatePart(root, "Head", BodyPartType.Head, new Vector3(0f, headY, 0f),
                c, FatalContactZone.Whole, 2f * c.headRadius);
            head.AddComponent<SphereCollider>().radius = c.headRadius;
            AddVisual(head, PrimitiveType.Sphere, Vector3.zero, Vector3.one * (2f * c.headRadius), m.head);
            AddVisual(head, PrimitiveType.Sphere, new Vector3(0.55f, 0.2f, -0.75f) * c.headRadius,
                Vector3.one * (0.35f * c.headRadius), m.eye);
            AddJoint(head, torso, new Vector3(0f, -c.headRadius, 0f), JointType.Neck, false, c);

            for (int side = 0; side < 2; side++)
            {
                bool isLeft = side == 0;
                string suffix = isLeft ? "Left" : "Right";

                // Left limbs further from the camera, right limbs closer (visual only).
                float legZ = isLeft ? c.legDepthOffset : -c.legDepthOffset;
                float armZ = isLeft ? c.armDepthOffset : -c.armDepthOffset;
                Material material = isLeft ? m.left : m.right;

                var arm = CreatePart(root, "Arm" + suffix, isLeft ? BodyPartType.LeftArm : BodyPartType.RightArm,
                    new Vector3(0f, shoulderY - 0.5f * c.armLength, armZ), c, FatalContactZone.Whole, c.armLength);
                AddCapsule(arm, c.limbRadius, c.armLength, material);
                AddJoint(arm, torso, new Vector3(0f, 0.5f * c.armLength, 0f), JointType.Shoulder, isLeft, c);

                var thigh = CreatePart(root, "Thigh" + suffix, isLeft ? BodyPartType.LeftThigh : BodyPartType.RightThigh,
                    new Vector3(0f, hipY - 0.5f * c.thighLength, legZ), c, FatalContactZone.Whole, c.thighLength);
                AddCapsule(thigh, c.limbRadius, c.thighLength, material);
                AddJoint(thigh, torso, new Vector3(0f, 0.5f * c.thighLength, 0f), JointType.Hip, isLeft, c);

                var shin = CreatePart(root, "Shin" + suffix, isLeft ? BodyPartType.LeftShin : BodyPartType.RightShin,
                    new Vector3(0f, kneeY - 0.5f * c.shinLength, legZ), c, FatalContactZone.UpperHalf, c.shinLength);
                AddCapsule(shin, c.limbRadius, c.shinLength, material);
                AddJoint(shin, thigh, new Vector3(0f, 0.5f * c.shinLength, 0f), JointType.Knee, isLeft, c);

                float centerX = c.footLength * (0.5f - c.ankleFromHeel);
                var footSize = new Vector3(c.footLength, c.footHeight, c.footWidth);
                var foot = CreatePart(root, "Foot" + suffix, isLeft ? BodyPartType.LeftFoot : BodyPartType.RightFoot,
                    new Vector3(centerX, 0.5f * c.footHeight, legZ), c, FatalContactZone.None, c.footLength);
                foot.AddComponent<BoxCollider>().size = footSize;
                AddVisual(foot, PrimitiveType.Cube, Vector3.zero, footSize, material);
                AddJoint(foot, shin, new Vector3(-centerX, c.ankleHeight - 0.5f * c.footHeight, 0f), JointType.Ankle, isLeft, c);
            }

            // ML-Agents components
            root.AddComponent<StickmanAgent>().config = c;

            var bp = root.GetComponent<BehaviorParameters>();
            if (bp == null) bp = root.AddComponent<BehaviorParameters>();
            bp.BehaviorName = BehaviorName;
            bp.BehaviorType = BehaviorType.Default;
            bp.BrainParameters.VectorObservationSize = StickmanAgent.ObservationSize;
            bp.BrainParameters.NumStackedVectorObservations = 1;
            bp.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(StickmanAgent.ActionCount);

            root.AddComponent<DecisionRequester>();
        }

        static GameObject CreatePart(GameObject root, string name, BodyPartType type, Vector3 position, StickmanConfig c,
            FatalContactZone fatalZone, float length)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = c.GetMass(type);
            rb.constraints = StickmanBodyPart.PlanarConstraints;

            var part = go.AddComponent<StickmanBodyPart>();
            part.part = type;
            part.fatalZone = fatalZone;
            part.length = length;
            return go;
        }

        static void AddCapsule(GameObject go, float radius, float length, Material material)
        {
            float height = Mathf.Max(length, 2f * radius);
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;
            capsule.radius = radius;
            capsule.height = height;
            AddVisual(go, PrimitiveType.Capsule, Vector3.zero, new Vector3(2f * radius, 0.5f * height, 2f * radius), material);
        }

        // Primitive without collider, for rendering only.
        static void AddVisual(GameObject parent, PrimitiveType type, Vector3 position, Vector3 scale, Material material)
        {
            var visual = GameObject.CreatePrimitive(type);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(parent.transform, false);
            visual.transform.localPosition = position;
            visual.transform.localScale = scale;
            visual.GetComponent<Renderer>().sharedMaterial = material;
        }

        // Joint free only around Z, with limits and a motor.
        static void AddJoint(GameObject child, GameObject parent, Vector3 localAnchor, JointType type, bool isLeft,
            StickmanConfig c)
        {
            var parentBody = parent.GetComponent<Rigidbody>();
            var joint = child.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentBody;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = localAnchor;
            joint.connectedAnchor = parent.transform.InverseTransformPoint(child.transform.TransformPoint(localAnchor));
            joint.axis = Vector3.forward;
            joint.secondaryAxis = Vector3.up;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;
            joint.enableCollision = false;
            joint.enablePreprocessing = false;

            var stickmanJoint = child.AddComponent<StickmanJoint>();
            stickmanJoint.type = type;
            stickmanJoint.isLeft = isLeft;
            stickmanJoint.joint = joint;
            stickmanJoint.body = child.GetComponent<Rigidbody>();
            stickmanJoint.parentBody = parentBody;
            stickmanJoint.Configure(c.GetJointSettings(type), c.invertJointSignConvention);
        }

        static void SetUpCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.transform.SetPositionAndRotation(new Vector3(0.5f, 1.0f, -4.5f), Quaternion.identity);
            cam.fieldOfView = 50f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.74f, 0.86f);
            if (cam.GetComponent<StickmanFollowCamera>() == null)
                cam.gameObject.AddComponent<StickmanFollowCamera>();
        }

        static void SetUpLight()
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
                light.shadows = LightShadows.Soft;
            }
        }

        static void CreateFolders()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Stickman");
            foreach (var folder in Subfolders)
                if (!AssetDatabase.IsValidFolder($"{Root}/{folder}")) AssetDatabase.CreateFolder(Root, folder);
        }

        static StickmanConfig LoadOrCreateConfig()
        {
            CreateFolders();
            var config = AssetDatabase.LoadAssetAtPath<StickmanConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<StickmanConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[Stickman] Configuration created: {ConfigPath}");
            }
            return config;
        }

        static Materials CreateMaterials(StickmanConfig c)
        {
            var m = new Materials
            {
                body = CreateMaterial("Stickman_Body", new Color(0.92f, 0.92f, 0.92f)),
                head = CreateMaterial("Stickman_Head", new Color(0.98f, 0.95f, 0.88f)),
                left = CreateMaterial("Stickman_Left", new Color(0.20f, 0.45f, 0.85f)),
                right = CreateMaterial("Stickman_Right", new Color(0.95f, 0.50f, 0.15f)),
                eye = CreateMaterial("Stickman_Eye", new Color(0.08f, 0.08f, 0.08f)),
                ground = CreateMaterial("Stickman_Ground", Color.white)
            };

            // 1 m stripes to judge step length.
            var texture = CreateGroundTexture();
            if (texture != null)
            {
                m.ground.mainTexture = texture;
                m.ground.mainTextureScale = new Vector2(c.groundLength / 2f, 1f);
                EditorUtility.SetDirty(m.ground);
            }
            return m;
        }

        static Material CreateMaterial(string name, Color color)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(FindLitShader());
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        // Lit shader of the active render pipeline.
        static Shader FindLitShader()
        {
            string[] names = GraphicsSettings.currentRenderPipeline != null
                ? new[] { "Universal Render Pipeline/Lit", "HDRP/Lit", "Universal Render Pipeline/Simple Lit", "Standard" }
                : new[] { "Standard" };
            foreach (var name in names)
            {
                var shader = Shader.Find(name);
                if (shader != null) return shader;
            }
            return Shader.Find("Unlit/Color");
        }

        static Texture2D CreateGroundTexture()
        {
            if (!File.Exists(GroundTexturePath))
            {
                var texture = new Texture2D(2, 1, TextureFormat.RGBA32, false);
                texture.SetPixel(0, 0, new Color(0.80f, 0.80f, 0.76f));
                texture.SetPixel(1, 0, new Color(0.55f, 0.55f, 0.52f));
                texture.Apply();
                File.WriteAllBytes(GroundTexturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(GroundTexturePath, ImportAssetOptions.ForceSynchronousImport);
            }

            if (AssetImporter.GetAtPath(GroundTexturePath) is TextureImporter importer)
            {
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexturePath);
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static Object GetSelectedModel()
        {
            var selected = Selection.activeObject;
            if (selected == null) return null;
            string extension = Path.GetExtension(AssetDatabase.GetAssetPath(selected)).ToLowerInvariant();
            return extension == ".onnx" || extension == ".sentis" ? selected : null;
        }

        // Uses the serialized field, which works whatever the model asset type.
        static bool AssignModel(BehaviorParameters bp, Object model)
        {
            if (bp == null) return false;
            var serialized = new SerializedObject(bp);
            var property = serialized.FindProperty("m_Model");
            if (property == null)
            {
                Debug.LogWarning("[Stickman] Field 'm_Model' not found: drag the .onnx into 'Model' manually.", bp);
                return false;
            }
            property.objectReferenceValue = model;
            serialized.ApplyModifiedProperties();
            if (property.objectReferenceValue == null)
            {
                Debug.LogWarning("[Stickman] The selected file is not a model accepted by ML-Agents.", bp);
                return false;
            }
            bp.BehaviorType = BehaviorType.InferenceOnly;
            EditorUtility.SetDirty(bp);
            if (PrefabUtility.IsPartOfPrefabInstance(bp))
                PrefabUtility.RecordPrefabInstancePropertyModifications(bp);
            return true;
        }
    }
}
