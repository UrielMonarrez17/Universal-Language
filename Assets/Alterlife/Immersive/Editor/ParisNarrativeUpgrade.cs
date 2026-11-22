using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Upgrades Le Café de Paris assets and scene (Paris.unity) for the non-linear 8-milestone
    /// Gemini AI conversation, expanded physical 3D menu, Fox Level 1 / Tutor panel, dual spatial voices,
    /// and the new Player Transcript panel while preserving all existing Paris UI.
    /// </summary>
    public static class ParisNarrativeUpgrade
    {
        const string ParisScenePath = "Assets/Scenes/Paris.unity";
        const string RestaurantDefPath = "Assets/Alterlife/Immersive/Data/restaurant.asset";
        const string RestaurantLessonPath = "Assets/Alterlife/Immersive/Paris/RestaurantLesson.asset";
        const string BackdropMatPath = "Assets/Alterlife/Immersive/Generated/Minimalist Floating Backdrop.mat";
        const string FontAssetPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";

        static readonly Color CrispTextColor = new Color(0.97f, 0.98f, 0.99f, 1f);

        [MenuItem("Alterlife/Restaurant/Apply Gemini Narrative & Tutor Upgrade")]
        public static void Apply()
        {
            if (!Application.isBatchMode && SceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            UpdateDataAssets();
            ImmersiveExperienceUpgrade.ApplyAll();
            UpdateParisScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("ALTERLIFE_PARIS_NARRATIVE_UPGRADE_APPLIED");
        }

        static void UpdateDataAssets()
        {
            var rubricLines = RestaurantLesson.CreateIntermediateParisRubric();
            string[] milestoneIds = rubricLines.Select(l => l.milestoneId).ToArray();

            var def = AssetDatabase.LoadAssetAtPath<ExperienceDefinition>(RestaurantDefPath);
            if (def != null)
            {
                def.milestoneIds = milestoneIds;
                def.description = "Have a natural French café conversation, catch a subtle order mix-up, and ask for the bill.";
                def.difficulty = 3;
                def.scenePath = ParisScenePath;
                EditorUtility.SetDirty(def);
            }

            var lesson = AssetDatabase.LoadAssetAtPath<RestaurantLesson>(RestaurantLessonPath);
            if (lesson == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RestaurantLessonPath) ?? "Assets/Alterlife/Immersive/Paris");
                lesson = ScriptableObject.CreateInstance<RestaurantLesson>();
                AssetDatabase.CreateAsset(lesson, RestaurantLessonPath);
            }

            lesson.scenarioName = "Le Café de Paris";
            lesson.learnerLevel = "Intermediate (A2/B1 · Level 3/5)";
            lesson.minAutonomousObjectives = 5;
            lesson.openingWaiterLineFrench = "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte.";
            lesson.openingWaiterLineEnglish = "Hello! Welcome to Le Café de Paris. Have a seat, here is the menu.";
            lesson.openingHintKeywords = new[]
            {
                "Bonjour — Hello",
                "Merci beaucoup — Thank you very much",
                "La carte — The menu"
            };
            lesson.menuCatalog = RestaurantLesson.DefaultMenuCatalog;
            lesson.lines = rubricLines;
            lesson.challengeOrderConfirmation = true;
            lesson.challengeWaterConfirmation = true;
            EditorUtility.SetDirty(lesson);
        }

        static void UpdateParisScene()
        {
            string previousScenePath = SceneManager.GetActiveScene().path;
            Scene scene = EditorSceneManager.OpenScene(ParisScenePath, OpenSceneMode.Single);

            GameObject rootObj = GameObject.Find("Paris Restaurant Prototype");
            if (rootObj == null)
            {
                Debug.LogError("ParisNarrativeUpgrade: 'Paris Restaurant Prototype' root not found in Paris.unity.");
                return;
            }
            Transform root = rootObj.transform;

            Material backdropMat = AssetDatabase.LoadAssetAtPath<Material>(BackdropMatPath);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            var lesson = AssetDatabase.LoadAssetAtPath<RestaurantLesson>(RestaurantLessonPath);
            var def = AssetDatabase.LoadAssetAtPath<ExperienceDefinition>(RestaurantDefPath);

            var eval = Object.FindAnyObjectByType<RestaurantEvaluation>(FindObjectsInactive.Include);
            var voice = Object.FindAnyObjectByType<RestaurantVoiceBridge>(FindObjectsInactive.Include);
            var personality = Object.FindAnyObjectByType<ParisWaiterPersonality>(FindObjectsInactive.Include);

            // 1. Update Physical 3D Café Menu on the table with expanded authentic Parisian items
            Transform menuRoot = root.Find("Physical French menu");
            if (menuRoot != null)
            {
                Transform titleTf = menuRoot.Find("Menu title");
                if (titleTf != null && titleTf.TryGetComponent<TMP_Text>(out var titleTmp))
                {
                    titleTf.localPosition = new Vector3(0f, 0.012f, 0.188f);
                    titleTmp.text = "LE CAFÉ DE PARIS · LA CARTE";
                    titleTmp.fontSize = 0.21f;
                    titleTmp.rectTransform.sizeDelta = new Vector2(0.33f, 0.055f);
                    EditorUtility.SetDirty(titleTmp);
                }

                Transform dishesTf = menuRoot.Find("French menu dishes");
                if (dishesTf != null && dishesTf.TryGetComponent<TMP_Text>(out var dishesTmp))
                {
                    dishesTf.localPosition = new Vector3(0f, 0.012f, -0.025f);
                    dishesTmp.text = RestaurantLesson.PhysicalMenuDisplayText;
                    dishesTmp.fontSize = 0.162f;
                    dishesTmp.alignment = TextAlignmentOptions.TopLeft;
                    dishesTmp.textWrappingMode = TextWrappingModes.Normal;
                    dishesTmp.overflowMode = TextOverflowModes.Overflow;
                    dishesTmp.rectTransform.sizeDelta = new Vector2(0.32f, 0.37f);
                    EditorUtility.SetDirty(dishesTmp);
                }

                EditorUtility.SetDirty(menuRoot.gameObject);
            }

            // 2. Create/Update Player Transcript Panel on the left side of the table (preserving all existing UI)
            Transform existingPlayerPanel = root.Find("Player transcript panel");
            if (existingPlayerPanel != null)
                Object.DestroyImmediate(existingPlayerPanel.gameObject);

            GameObject playerPanel = new GameObject("Player transcript panel");
            playerPanel.transform.SetParent(root, false);
            playerPanel.transform.localPosition = new Vector3(-0.78f, 1.12f, 0.08f);
            playerPanel.transform.localRotation = Quaternion.Euler(0f, -22f, 0f);

            GameObject playerBacking = GameObject.CreatePrimitive(PrimitiveType.Cube);
            playerBacking.name = "Player transcript backing";
            playerBacking.transform.SetParent(playerPanel.transform, false);
            playerBacking.transform.localPosition = new Vector3(0f, 0f, 0.014f);
            playerBacking.transform.localScale = new Vector3(0.74f, 0.30f, 0.008f);
            Object.DestroyImmediate(playerBacking.GetComponent<Collider>());
            if (backdropMat != null)
                playerBacking.GetComponent<Renderer>().sharedMaterial = backdropMat;

            TMP_Text playerText = CreateText(
                "Player transcript text",
                playerPanel.transform,
                "<size=72%><color=#94A3B8>YOU SAID (FR)</color></size>\n<color=#CBD5E1>Speak naturally in French...</color>",
                Vector3.zero,
                new Vector2(0.68f, 0.25f),
                0.24f,
                CrispTextColor,
                font);
            EditorUtility.SetDirty(playerPanel);

            // 3. Create/Update Floating Fox Level 1 / Tutor Hint Panel (for on-demand Level 1 hints & English tutor replies)
            Transform existingHintPanel = root.Find("Fox hint panel");
            if (existingHintPanel != null)
                Object.DestroyImmediate(existingHintPanel.gameObject);

            GameObject hintPanel = new GameObject("Fox hint panel");
            hintPanel.transform.SetParent(root, false);
            hintPanel.transform.localPosition = new Vector3(0f, 1.35f, -0.45f);

            GameObject hintBacking = GameObject.CreatePrimitive(PrimitiveType.Cube);
            hintBacking.name = "Fox hint backing";
            hintBacking.transform.SetParent(hintPanel.transform, false);
            hintBacking.transform.localPosition = new Vector3(0f, 0f, 0.012f);
            hintBacking.transform.localScale = new Vector3(0.88f, 0.36f, 0.008f);
            Object.DestroyImmediate(hintBacking.GetComponent<Collider>());
            if (backdropMat != null)
                hintBacking.GetComponent<Renderer>().sharedMaterial = backdropMat;

            TMP_Text hintText = CreateText(
                "Fox hint text",
                hintPanel.transform,
                "<color=#7DD3FC><b>FOX HINT · LEVEL 1</b></color>",
                Vector3.zero,
                new Vector2(0.82f, 0.31f),
                0.22f,
                CrispTextColor,
                font);
            hintPanel.SetActive(false);
            EditorUtility.SetDirty(hintPanel);

            // 4. Wire Fox Companion (floating_fox) Level 1 Panel & Spatial AudioSource
            GameObject foxGo = GameObject.Find("floating_fox");
            HummingbirdCompanion foxCompanion = foxGo != null ? foxGo.GetComponent<HummingbirdCompanion>() : null;
            AudioSource foxAudio = null;
            if (foxGo != null)
            {
                foxAudio = foxGo.GetComponent<AudioSource>();
                if (foxAudio == null) foxAudio = foxGo.AddComponent<AudioSource>();
                foxAudio.spatialBlend = 1f;
                foxAudio.minDistance = 0.5f;
                foxAudio.maxDistance = 6f;
                foxAudio.playOnAwake = false;
                EditorUtility.SetDirty(foxAudio);

                if (foxCompanion != null)
                {
                    SerializedObject foxSo = new SerializedObject(foxCompanion);
                    foxSo.FindProperty("audioSource").objectReferenceValue = foxAudio;
                    foxSo.FindProperty("hintPanelRoot").objectReferenceValue = hintPanel;
                    foxSo.FindProperty("hintPanelText").objectReferenceValue = hintText;
                    foxSo.FindProperty("shoulderOffset").vector3Value = new Vector3(0.48f, -0.38f, 0.72f);
                    foxSo.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(foxCompanion);
                }
            }

            // 4b. Create/Update compact Bottom-Right HUD Mic Status indicator inside XR Camera's field of view
            var xrOrigin = Object.FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include);
            TMP_Text hudMicLabel = null;
            Renderer hudMicBackingRenderer = null;
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                Transform camTf = xrOrigin.Camera.transform;
                Transform existingHud = camTf.Find("HUD Mic Status");
                if (existingHud != null)
                    Object.DestroyImmediate(existingHud.gameObject);

                GameObject hudRoot = new GameObject("HUD Mic Status");
                hudRoot.transform.SetParent(camTf, false);
                hudRoot.transform.localPosition = new Vector3(0.23f, -0.165f, 0.56f);
                hudRoot.transform.localRotation = Quaternion.Euler(6f, 12f, 0f);

                GameObject hudBacking = GameObject.CreatePrimitive(PrimitiveType.Cube);
                hudBacking.name = "HUD Mic Backing";
                hudBacking.transform.SetParent(hudRoot.transform, false);
                hudBacking.transform.localPosition = new Vector3(0f, 0f, 0.004f);
                hudBacking.transform.localScale = new Vector3(0.145f, 0.036f, 0.003f);
                Object.DestroyImmediate(hudBacking.GetComponent<Collider>());
                hudMicBackingRenderer = hudBacking.GetComponent<Renderer>();
                if (backdropMat != null)
                    hudMicBackingRenderer.sharedMaterial = backdropMat;

                hudMicLabel = CreateText(
                    "HUD Mic Label",
                    hudRoot.transform,
                    "<color=#6EE7B7><b>● MIC ON</b></color> <size=78%><color=#CBD5E1>· Press B</color></size>",
                    Vector3.zero,
                    new Vector2(0.138f, 0.032f),
                    0.115f,
                    CrispTextColor,
                    font);
                EditorUtility.SetDirty(hudRoot);
            }

            // 5. Wire RestaurantEvaluation, RestaurantVoiceBridge, ParisWaiterPersonality
            if (personality != null)
            {
                personality.geminiVoiceName = "Puck";
                personality.foxVoiceName = AlibabaTtsClient.DefaultVoice;
                personality.learnerLevel = "Intermediate (A2/B1 · Level 3/5)";
                personality.includeGentleOrderConfirmationMistake = true;
                personality.worldKnowledge = RestaurantLesson.DefaultMenuCatalog;
                EditorUtility.SetDirty(personality);
            }

            if (voice != null)
            {
                voice.foxSpeaker = foxAudio;
                voice.npcPersonality = personality;
                voice.autoListenVad = true;
                voice.hudMicStatusLabel = hudMicLabel;
                voice.hudMicStatusBacking = hudMicBackingRenderer;
                SerializedObject voiceSo = new SerializedObject(voice);
                voiceSo.FindProperty("geminiChatModel").stringValue = "gemini-3.5-flash-lite";
                voiceSo.FindProperty("geminiTtsModel").stringValue = "gemini-3.8-flash-lite-tts";
                voiceSo.FindProperty("alibabaPrimaryModel").stringValue = AlibabaTtsClient.DefaultPrimaryModel;
                voiceSo.FindProperty("alibabaFoxVoice").stringValue = AlibabaTtsClient.DefaultVoice;
                voiceSo.FindProperty("hudMicStatusLabel").objectReferenceValue = hudMicLabel;
                voiceSo.FindProperty("hudMicStatusBacking").objectReferenceValue = hudMicBackingRenderer;
                voiceSo.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(voice);
            }

            if (eval != null)
            {
                eval.lesson = lesson;
                eval.experience = def;
                eval.playerTranscriptText = playerText;
                if (foxCompanion != null) eval.companion = foxCompanion;
                if (personality != null) eval.waiterPersonality = personality;
                EditorUtility.SetDirty(eval);
            }

            // 6. Keep Menu instruction concise so it fits cleanly inside Engraved slate
            Transform menuInstr = root.Find("Menu instruction");
            if (menuInstr != null && menuInstr.TryGetComponent<TMP_Text>(out var instrTmp))
            {
                instrTmp.text = "Grab menu with Trigger/Grip.\nSpeak French naturally • Press Y for Fox help";
                EditorUtility.SetDirty(instrTmp);
            }

            // Update Waiter dialogue default placeholder cleanly
            Transform waiterDialogue = root.Find("Waiter dialogue");
            if (waiterDialogue != null)
            {
                Transform frenchTmpTf = waiterDialogue.Find("French dialogue");
                if (frenchTmpTf != null && frenchTmpTf.TryGetComponent<TMP_Text>(out var frenchTmp))
                {
                    frenchTmp.text = "Bonjour ! Bienvenue au Café de Paris.";
                    EditorUtility.SetDirty(frenchTmp);
                }
            }

            // Update Evaluation summary with crisp light text + translucent dark backing (hidden until exam finishes)
            Transform evalSummary = root.Find("Evaluation summary");
            if (evalSummary != null)
            {
                evalSummary.localPosition = new Vector3(1.45f, 1.55f, 0.85f);
                evalSummary.localRotation = Quaternion.Euler(0f, 28f, 0f);
                Transform oldBacking = evalSummary.Find("Summary backing");
                if (oldBacking != null) Object.DestroyImmediate(oldBacking.gameObject);

                GameObject summaryBacking = GameObject.CreatePrimitive(PrimitiveType.Cube);
                summaryBacking.name = "Summary backing";
                summaryBacking.transform.SetParent(evalSummary, false);
                summaryBacking.transform.localPosition = new Vector3(0f, 0f, 0.02f);
                summaryBacking.transform.localScale = new Vector3(1.52f, 0.98f, 0.008f);
                Object.DestroyImmediate(summaryBacking.GetComponent<Collider>());
                if (backdropMat != null)
                    summaryBacking.GetComponent<Renderer>().sharedMaterial = backdropMat;

                Transform resultsTf = evalSummary.Find("Results");
                if (resultsTf != null && resultsTf.TryGetComponent<TMP_Text>(out var resultsTmp))
                {
                    resultsTmp.color = CrispTextColor;
                    resultsTmp.fontSize = 0.34f;
                    resultsTmp.rectTransform.sizeDelta = new Vector2(1.42f, 0.90f);
                    EditorUtility.SetDirty(resultsTmp);
                }

                EditorUtility.SetDirty(evalSummary.gameObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            if (!string.IsNullOrEmpty(previousScenePath) && File.Exists(previousScenePath) && previousScenePath != ParisScenePath)
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
        }

        static TMP_Text CreateText(
            string name,
            Transform parent,
            string content,
            Vector3 localPos,
            Vector2 sizeDelta,
            float fontSize,
            Color color,
            TMP_FontAsset font)
        {
            GameObject go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            TextMeshPro text = go.GetComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = sizeDelta;
            return text;
        }
    }
}
