using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Builds the playable Apartment Pilot scene ("La Vie à la Maison") with
    /// HummingbirdCompanion, CommunicativeEncounter, WristMenuController, and LearningSession.
    /// </summary>
    public static class ApartmentPilotBuilder
    {
        public const string ScenePath = "Assets/Scenes/ApartmentPilot.unity";
        private const string DataRoot = "Assets/Alterlife/Immersive/Data";
        private const string OfficePrefabs = "Assets/nappin/OfficeEssentialsPack/Prefabs/";

        [MenuItem("Alterlife/Immersive/Build Apartment Pilot Scene")]
        public static void BuildScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset");

            // 1. Materials (Palette consistent with Design System)
            var warmPlaster = CreateMat("Pilot_WarmPlaster", new Color32(243, 232, 210, 255)); // #F3E8D2
            var darkWood = CreateMat("Pilot_DarkWood", new Color32(84, 49, 31, 255));
            var hudDark = CreateMat("Pilot_HudDark", new Color32(11, 17, 32, 230));        // #0B1120
            var cyanGlow = CreateMat("Pilot_CyanGlow", new Color32(79, 209, 197, 255));    // #4FD1C5
            var goldAccent = CreateMat("Pilot_GoldAccent", new Color32(234, 179, 8, 255)); // #EAB308
            var parchment = CreateMat("Pilot_Parchment", new Color32(243, 232, 210, 255)); // #F3E8D2

            // 2. Lighting
            var sunGo = new GameObject("Directional Light");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.94f, 0.84f);
            sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            RenderSettings.ambientLight = new Color(0.45f, 0.42f, 0.38f);

            // 3. XR Origin & Interaction Manager
            new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab");
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "Apartment XR Origin";
            rig.transform.position = new Vector3(0f, 0.02f, -1.6f);

            var origin = rig.GetComponent<Unity.XR.CoreUtils.XROrigin>();
            if (origin != null)
            {
                origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor;
            }

            foreach (var caster in rig.GetComponentsInChildren<CurveInteractionCaster>(true))
            {
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
            }

            var cam = rig.GetComponentInChildren<Camera>(true);

            // 4. Learning Session & Catalog
            var systems = new GameObject("Learning Systems");
            var session = systems.AddComponent<LearningSession>();
            var aptAsset = AssetDatabase.LoadAssetAtPath<ExperienceDefinition>($"{DataRoot}/apartment.asset");
            var restAsset = AssetDatabase.LoadAssetAtPath<ExperienceDefinition>($"{DataRoot}/restaurant.asset");
            var custAsset = AssetDatabase.LoadAssetAtPath<ExperienceDefinition>($"{DataRoot}/customs.asset");
            session.catalog = new[] { aptAsset, restAsset, custAsset }.Where(x => x != null).ToArray();

            // Link apartment.asset to this scene path
            if (aptAsset != null)
            {
                aptAsset.scenePath = ScenePath;
                EditorUtility.SetDirty(aptAsset);
            }

            // 5. Parisian Dining Environment
            var envRoot = new GameObject("Parisian Apartment Environment");
            var floor = CreateShape(PrimitiveType.Cube, "Floor", envRoot.transform, new Vector3(0, -0.05f, 0), new Vector3(6f, 0.1f, 6f), darkWood, true);
            var teleportArea = floor.AddComponent<TeleportationArea>();

            CreateShape(PrimitiveType.Cube, "BackWall", envRoot.transform, new Vector3(0, 1.5f, 2.5f), new Vector3(6f, 3f, 0.15f), warmPlaster, true);
            CreateShape(PrimitiveType.Cube, "LeftWall", envRoot.transform, new Vector3(-3f, 1.5f, 0f), new Vector3(0.15f, 3f, 6f), warmPlaster, true);
            CreateShape(PrimitiveType.Cube, "RightWall", envRoot.transform, new Vector3(3f, 1.5f, 0f), new Vector3(0.15f, 3f, 6f), warmPlaster, true);

            // Dining Table & Mug Prop
            Transform propTargetTransform = null;
            var tablePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OfficePrefabs + "ConferenceTable.prefab");
            if (tablePrefab != null)
            {
                var table = (GameObject)PrefabUtility.InstantiatePrefab(tablePrefab, envRoot.transform);
                table.transform.position = new Vector3(0f, 0f, 0.2f);
            }
            else
            {
                CreateShape(PrimitiveType.Cube, "Breakfast Table", envRoot.transform, new Vector3(0f, 0.4f, 0.2f), new Vector3(1.6f, 0.8f, 0.9f), darkWood, true);
            }

            var mugPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OfficePrefabs + "Mug.prefab");
            if (mugPrefab != null)
            {
                var mug = (GameObject)PrefabUtility.InstantiatePrefab(mugPrefab, envRoot.transform);
                mug.name = "KeyProp_CoffeeMug";
                mug.transform.position = new Vector3(0.2f, 0.78f, 0.15f);
                propTargetTransform = mug.transform;
            }
            else
            {
                var mug = CreateShape(PrimitiveType.Cylinder, "KeyProp_CoffeeMug", envRoot.transform, new Vector3(0.2f, 0.85f, 0.15f), new Vector3(0.1f, 0.1f, 0.1f), goldAccent, true);
                propTargetTransform = mug.transform;
            }

            // 6. NPC (Madame Dubois Placeholder + Speech Bubble)
            var npcGo = new GameObject("NPC_MadameDubois");
            npcGo.transform.position = new Vector3(0f, 0f, 1.1f);
            CreateShape(PrimitiveType.Capsule, "Body", npcGo.transform, new Vector3(0f, 0.85f, 0f), new Vector3(0.45f, 0.85f, 0.45f), warmPlaster, true);
            CreateShape(PrimitiveType.Sphere, "Head", npcGo.transform, new Vector3(0f, 1.55f, 0f), Vector3.one * 0.32f, parchment, false);

            var bubbleGo = new GameObject("NPC_SpeechBubble");
            bubbleGo.transform.SetParent(npcGo.transform);
            bubbleGo.transform.localPosition = new Vector3(0f, 2.05f, 0f);
            var bubbleBg = CreateShape(PrimitiveType.Cube, "BubbleBg", bubbleGo.transform, Vector3.zero, new Vector3(1.4f, 0.42f, 0.04f), parchment, false);
            var bubbleTmp = CreateWorldText("BubbleText", bubbleGo.transform, new Vector3(0, 0, -0.025f), "Bonjour ! Que veux-tu pour le petit déjeuner ?", 1.8f, new Color32(45, 36, 30, 255), font);

            // 7. Hummingbird Companion Rig
            var petGo = new GameObject("HummingbirdCompanion");
            petGo.transform.position = new Vector3(0.35f, 1.35f, -1.1f);
            CreateShape(PrimitiveType.Sphere, "Colibri_Body", petGo.transform, Vector3.zero, new Vector3(0.12f, 0.1f, 0.16f), cyanGlow, false);
            var petLight = petGo.AddComponent<Light>();
            petLight.type = LightType.Point;
            petLight.range = 1.6f;
            petLight.color = new Color(0.31f, 0.82f, 0.77f);
            var petAudio = petGo.AddComponent<AudioSource>();
            petAudio.spatialBlend = 1f;

            // Level 1 Vocab Label
            var labelRoot = new GameObject("VocabLabelRoot");
            labelRoot.transform.SetParent(petGo.transform);
            labelRoot.transform.localPosition = new Vector3(0f, 0.18f, 0f);
            CreateShape(PrimitiveType.Cube, "LabelBg", labelRoot.transform, Vector3.zero, new Vector3(0.45f, 0.14f, 0.02f), hudDark, false);
            var labelTmp = CreateWorldText("LabelText", labelRoot.transform, new Vector3(0, 0, -0.015f), "la tasse de café", 1.2f, new Color32(79, 209, 197, 255), font);

            // Level 2 Dialogue Cards
            var cardsRoot = new GameObject("DialogueCardsRoot");
            cardsRoot.transform.SetParent(petGo.transform);
            cardsRoot.transform.localPosition = new Vector3(0f, -0.25f, 0f);

            var (cardAControl, cardAText) = CreateXRCard("CardOptionA", cardsRoot.transform, new Vector3(-0.35f, 0f, 0f), "1. Un café, s'il vous plaît", parchment, goldAccent, font);
            var (cardBControl, cardBText) = CreateXRCard("CardOptionB", cardsRoot.transform, new Vector3(0.35f, 0f, 0f), "2. Non, merci", parchment, goldAccent, font);

            var companion = petGo.AddComponent<HummingbirdCompanion>();
            var petSo = new SerializedObject(companion);
            petSo.FindProperty("headCamera").objectReferenceValue = cam != null ? cam.transform : null;
            petSo.FindProperty("auraLight").objectReferenceValue = petLight;
            petSo.FindProperty("vocabLabelRoot").objectReferenceValue = labelRoot;
            petSo.FindProperty("vocabLabelText").objectReferenceValue = labelTmp;
            petSo.FindProperty("dialogueCardsRoot").objectReferenceValue = cardsRoot;
            petSo.FindProperty("cardOptionAText").objectReferenceValue = cardAText;
            petSo.FindProperty("cardOptionBText").objectReferenceValue = cardBText;
            petSo.FindProperty("cardOptionAControl").objectReferenceValue = cardAControl;
            petSo.FindProperty("cardOptionBControl").objectReferenceValue = cardBControl;
            petSo.ApplyModifiedPropertiesWithoutUndo();

            // 8. Communicative Encounter Orchestrator
            var encounter = systems.AddComponent<CommunicativeEncounter>();
            var encSo = new SerializedObject(encounter);
            encSo.FindProperty("experience").objectReferenceValue = aptAsset;
            encSo.FindProperty("session").objectReferenceValue = session;
            encSo.FindProperty("companion").objectReferenceValue = companion;
            encSo.FindProperty("npcTransform").objectReferenceValue = npcGo.transform;
            encSo.FindProperty("npcSpeechBubble").objectReferenceValue = bubbleTmp;
            encSo.FindProperty("keyInteractiveProp").objectReferenceValue = propTargetTransform;
            encSo.ApplyModifiedPropertiesWithoutUndo();

            // 9. Diegetic Wrist Menu on Left Controller
            var leftHand = rig.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Left") && t.name.Contains("Controller"));
            var wristAttach = leftHand != null ? leftHand : rig.transform;
            var wristGo = new GameObject("WristMenu");
            wristGo.transform.SetParent(wristAttach);
            wristGo.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            wristGo.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);

            var wristPanel = new GameObject("WristMenuCanvas");
            wristPanel.transform.SetParent(wristGo.transform);
            wristPanel.transform.localPosition = Vector3.zero;
            wristPanel.transform.localScale = Vector3.one * 0.35f;

            var wristCtrl = wristGo.AddComponent<WristMenuController>();
            var wristSo = new SerializedObject(wristCtrl);
            wristSo.FindProperty("headCamera").objectReferenceValue = cam != null ? cam.transform : null;
            wristSo.FindProperty("menuCanvas").objectReferenceValue = wristPanel;
            wristSo.FindProperty("directionalSunLight").objectReferenceValue = sun;
            wristSo.FindProperty("xrOriginOffsetTransform").objectReferenceValue = rig.transform;
            wristSo.FindProperty("companion").objectReferenceValue = companion;
            wristSo.ApplyModifiedPropertiesWithoutUndo();

            var (dayNightBtn, _) = CreateXRCard("Btn_DayNight", wristPanel.transform, new Vector3(0f, 0.25f, 0f), "Jour / Nuit", hudDark, cyanGlow, font);
            UnityEventTools.AddPersistentListener(dayNightBtn.pressed, wristCtrl.ToggleDayNight);

            var (postureBtn, _) = CreateXRCard("Btn_Posture", wristPanel.transform, new Vector3(0f, 0.08f, 0f), "Assis / Debout", hudDark, cyanGlow, font);
            UnityEventTools.AddPersistentListener(postureBtn.pressed, wristCtrl.ToggleSeatedStandingMode);

            var (petBtn, _) = CreateXRCard("Btn_SummonPet", wristPanel.transform, new Vector3(0f, -0.09f, 0f), "Appeler Colibri", hudDark, cyanGlow, font);
            UnityEventTools.AddPersistentListener(petBtn.pressed, wristCtrl.SummonCompanion);

            var (hubBtn, _) = CreateXRCard("Btn_ReturnHub", wristPanel.transform, new Vector3(0f, -0.26f, 0f), "Retour au Hub", goldAccent, goldAccent, font);
            UnityEventTools.AddPersistentListener(hubBtn.pressed, wristCtrl.ReturnToHub);

            // Save Scene & Register in Build Settings
            EditorSceneManager.SaveScene(scene, ScenePath);
            var existingScenes = EditorBuildSettings.scenes.ToList();
            if (!existingScenes.Any(s => s.path == ScenePath))
            {
                existingScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = existingScenes.ToArray();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_APARTMENT_PILOT_CREATED " + ScenePath);
        }

        private static (PhysicalControl, TextMeshPro) CreateXRCard(string name, Transform parent, Vector3 localPos, string text, Material bgMat, Material hoverMat, TMP_FontAsset font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            var box = CreateShape(PrimitiveType.Cube, "Bg", go.transform, Vector3.zero, new Vector3(0.55f, 0.14f, 0.02f), bgMat, true);
            box.AddComponent<XRSimpleInteractable>();
            var control = box.AddComponent<PhysicalControl>();
            control.feedbackRenderer = box.GetComponent<Renderer>();
            control.hoverColor = hoverMat.color;

            var tmp = CreateWorldText("Text", go.transform, new Vector3(0, 0, -0.015f), text, 1.1f, new Color32(45, 36, 30, 255), font);
            if (bgMat.color.r < 0.2f) tmp.color = Color.white;
            return (control, tmp);
        }

        private static TextMeshPro CreateWorldText(string name, Transform parent, Vector3 localPos, string content, float fontSize, Color32 color, TMP_FontAsset font)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.text = content;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(1.3f, 0.35f);
            return tmp;
        }

        private static GameObject CreateShape(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider)
            {
                var col = go.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
            }
            return go;
        }

        private static Material CreateMat(string name, Color32 color)
        {
            Directory.CreateDirectory("Assets/Alterlife/Immersive/Generated");
            string path = $"Assets/Alterlife/Immersive/Generated/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
