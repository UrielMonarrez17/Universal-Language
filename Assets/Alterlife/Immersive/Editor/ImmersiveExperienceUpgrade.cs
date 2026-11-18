using System;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

namespace Alterlife.Immersive.Editor
{
    /// <summary>
    /// Upgrades ImmersiveHub, BookSelection, and Paris with minimalist floating UI,
    /// Left Wrist Menu, controller-priority rig checks, Paris Fox 3-card help, and Gemini Waiter personality.
    /// </summary>
    public static class ImmersiveExperienceUpgrade
    {
        const string GeneratedDir = "Assets/Alterlife/Immersive/Generated";
        const string MinimalistPanelMatPath = GeneratedDir + "/Minimalist Floating Panel.mat";
        const string MinimalistBackdropMatPath = GeneratedDir + "/Minimalist Floating Backdrop.mat";
        const string FontAssetPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";

        static readonly Color PanelBaseColor = new Color(0.06f, 0.09f, 0.14f, 0.78f);
        static readonly Color BackdropBaseColor = new Color(0.05f, 0.07f, 0.11f, 0.68f);
        static readonly Color HoverHighlightColor = new Color(0.24f, 0.62f, 0.88f, 0.92f);
        static readonly Color CrispTextColor = new Color(0.97f, 0.98f, 0.99f, 1f);

        static readonly string[] AllScenes =
        {
            "Assets/Scenes/ImmersiveHub.unity",
            "Assets/Scenes/BookSelection.unity",
            "Assets/Scenes/Paris.unity",
        };

        [MenuItem("Alterlife/Immersive/Apply Full UX & AI Upgrade")]
        public static void ApplyAll()
        {
            if (SceneManager.GetActiveScene().isDirty)
                EditorSceneManager.SaveOpenScenes();

            Directory.CreateDirectory(GeneratedDir);
            Material panelMat = GetOrCreateTransparentPanelMaterial(MinimalistPanelMatPath, "Minimalist Floating Panel", PanelBaseColor);
            Material backdropMat = GetOrCreateTransparentPanelMaterial(MinimalistBackdropMatPath, "Minimalist Floating Backdrop", BackdropBaseColor);
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);

            string previousScenePath = SceneManager.GetActiveScene().path;

            foreach (string scenePath in AllScenes)
            {
                if (!File.Exists(scenePath))
                {
                    Debug.LogWarning("Skipping missing scene: " + scenePath);
                    continue;
                }

                UpgradeScene(scenePath, panelMat, backdropMat, font);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (!string.IsNullOrEmpty(previousScenePath) && File.Exists(previousScenePath))
                EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);

            Debug.Log("ALTERLIFE_FULL_UX_AI_UPGRADE_APPLIED");
        }

        [MenuItem("Alterlife/Immersive/Validate All Upgraded Scenes")]
        public static void ValidateAllUpgradedScenes()
        {
            QuestBuildPipeline.ValidateQuestReadiness(throwOnError: false);
        }

        static Material GetOrCreateTransparentPanelMaterial(string path, string matName, Color color)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.name = matName;
                AssetDatabase.CreateAsset(mat, path);
            }

            ConfigureTransparentLitMaterial(mat, color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureTransparentLitMaterial(Material mat, Color color)
        {
            if (mat == null) return;
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetFloat("_Surface", 1f); // Transparent
            mat.SetFloat("_Blend", 0f);   // Alpha
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Smoothness", 0.25f);
            mat.SetFloat("_Metallic", 0.0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_EMISSION");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.color = color;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", Color.black);
        }

        static void UpgradeScene(string scenePath, Material panelMat, Material backdropMat, TMP_FontAsset font)
        {
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            bool isHub = scenePath.EndsWith("ImmersiveHub.unity", StringComparison.OrdinalIgnoreCase);
            bool isBook = scenePath.EndsWith("BookSelection.unity", StringComparison.OrdinalIgnoreCase);
            bool isParis = scenePath.EndsWith("Paris.unity", StringComparison.OrdinalIgnoreCase);

            XROrigin xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>(FindObjectsInactive.Include);
            XRInteractionManager interactionManager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>(FindObjectsInactive.Include);

            if (xrOrigin == null)
            {
                Debug.LogWarning("No XROrigin in " + scenePath);
                return;
            }

            if (isHub || isBook)
                CleanupComfortAndWorldButtons(isHub, isBook, backdropMat);

            if (isParis)
                UpgradeParisScene(xrOrigin, interactionManager, panelMat, backdropMat, font);

            // Restyle remaining PhysicalControl buttons across the scene to minimalist floating dark translucent panels
            RestyleAllPhysicalButtons(panelMat, interactionManager);

            // Ensure ComfortOptions exists (including in Paris for Wrist Menu movement/posture)
            ComfortOptions comfort = EnsureComfortOptions(xrOrigin);

            // Build or rebuild the Left Wrist Menu cleanly inside XROrigin's Camera Floor Offset
            BuildOrUpdateWristMenu(scenePath, xrOrigin, interactionManager, comfort, panelMat, backdropMat, font);

            // Ensure QuestMultimodalRig & hand visuals are cleanly wired
            EnsureMultimodalRig(xrOrigin, interactionManager);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void CleanupComfortAndWorldButtons(bool isHub, bool isBook, Material backdropMat)
        {
            // Keep ComfortOptions component alive, remove the bulky green buttons & wall plaque
            var comfort = UnityEngine.Object.FindAnyObjectByType<ComfortOptions>(FindObjectsInactive.Include);
            if (comfort != null)
            {
                comfort.label = null;
                comfort.postureLabel = null;
                DestroyChildIfFound(comfort.transform, "Movement comfort toggle");
                DestroyChildIfFound(comfort.transform, "Posture comfort toggle");
                DestroyChildIfFound(comfort.transform, "Comfort instructions");
                EditorUtility.SetDirty(comfort);
            }

            DestroyNamed("Comfort instructions");
            DestroyNamed("Comfort instructions physical plaque");

            // Restyle bulky wooden/brass plaques in Observatory & Reading Room into clean floating backdrops
            string[] plaqueNames =
            {
                "Globe instructions physical plaque",
                "Controls physical plaque",
                "Selected chapter summary physical plaque",
                "Book instructions physical plaque"
            };
            foreach (string plaqueName in plaqueNames)
            {
                GameObject plaque = GameObject.Find(plaqueName);
                if (plaque == null) continue;
                RestylePlaqueToMinimalist(plaque, backdropMat);
            }

            // Update instruction labels so they reflect Trigger & Left Wrist Menu
            GameObject controlsGo = GameObject.Find("Controls");
            if (controlsGo != null)
            {
                TMP_Text controlsTmp = controlsGo.GetComponent<TMP_Text>();
                if (controlsTmp != null)
                {
                    controlsTmp.text = "CONTROLS\nTrigger or Pinch: Select\nGrip or Trigger: Rotate globe\nLook at Left Wrist (or X): Menu";
                    controlsTmp.color = CrispTextColor;
                    EditorUtility.SetDirty(controlsTmp);
                }
            }

            GameObject globeInstGo = GameObject.Find("Globe instructions");
            if (globeInstGo != null)
            {
                TMP_Text globeTmp = globeInstGo.GetComponent<TMP_Text>();
                if (globeTmp != null)
                {
                    globeTmp.text = "EXPLORE THE GLOBE\nHold Trigger or Grip on Earth to spin.\nHover & Trigger France (or flag) to enter.\nOther destinations: Coming Soon";
                    globeTmp.color = CrispTextColor;
                    EditorUtility.SetDirty(globeTmp);
                }
            }

            if (isBook)
            {
                // Move "Return to world selection" from the room into the Wrist Menu
                GameObject oldReturn = GameObject.Find("Return to world selection");
                if (oldReturn != null && oldReturn.GetComponentInParent<WristMenuController>() == null)
                    UnityEngine.Object.DestroyImmediate(oldReturn);
            }
        }

        static void RestylePlaqueToMinimalist(GameObject plaque, Material backdropMat)
        {
            if (plaque == null) return;
            DestroyChildIfFound(plaque.transform, "Brass plaque frame");
            DestroyChildIfFound(plaque.transform, "Lectern stem");
            DestroyChildIfFound(plaque.transform, "Lectern foot");

            Transform slate = plaque.transform.Find("Engraved slate");
            if (slate != null)
            {
                Renderer r = slate.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = backdropMat;
                Collider col = slate.GetComponent<Collider>();
                if (col != null) UnityEngine.Object.DestroyImmediate(col);
                Vector3 s = slate.localScale;
                slate.localScale = new Vector3(s.x, s.y, 0.008f);
                EditorUtility.SetDirty(slate);
            }
        }

        static void UpgradeParisScene(
            XROrigin xrOrigin,
            XRInteractionManager interactionManager,
            Material panelMat,
            Material backdropMat,
            TMP_FontAsset font)
        {
            GameObject rootObj = GameObject.Find("Paris Restaurant Prototype");
            Transform root = rootObj != null ? rootObj.transform : null;
            if (root == null) return;

            var eval = UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>(FindObjectsInactive.Include);
            var voice = UnityEngine.Object.FindAnyObjectByType<RestaurantVoiceBridge>(FindObjectsInactive.Include);

            // 1. Remove clunky table & room green buttons (moved to Wrist Menu / automatic VAD + Fox Y)
            string[] toRemove =
            {
                "Microphone control",
                "Help control",
                "Repeat control",
                "Restart restaurant",
                "Return to experience book",
                "Journal stand"
            };
            foreach (string name in toRemove)
                DestroyChildIfFound(root, name);

            // 2. Restyle Conversation journal & Waiter dialogue to minimalist floating panels
            Transform journal = root.Find("Conversation journal");
            if (journal != null)
            {
                Transform frame = journal.Find("Journal wood frame");
                if (frame != null)
                {
                    Renderer r = frame.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = backdropMat;
                    Collider col = frame.GetComponent<Collider>();
                    if (col != null) UnityEngine.Object.DestroyImmediate(col);
                    frame.localScale = new Vector3(1.12f, 0.94f, 0.01f);
                    frame.localPosition = new Vector3(0f, 0f, 0.02f);
                }
                foreach (var tmp in journal.GetComponentsInChildren<TMP_Text>(true))
                    tmp.color = CrispTextColor;
            }

            Transform dialogue = root.Find("Waiter dialogue");
            if (dialogue != null)
            {
                Transform backing = dialogue.Find("Dialogue backing");
                if (backing != null)
                {
                    Renderer r = backing.GetComponent<Renderer>();
                    if (r != null) r.sharedMaterial = backdropMat;
                    backing.localScale = new Vector3(1.42f, 0.44f, 0.008f);
                    backing.localPosition = new Vector3(0f, 0f, 0.015f);
                }
                foreach (var tmp in dialogue.GetComponentsInChildren<TMP_Text>(true))
                    tmp.color = CrispTextColor;
            }

            Transform menuInstr = root.Find("Menu instruction");
            if (menuInstr != null)
            {
                var tmp = menuInstr.GetComponent<TMP_Text>();
                if (tmp != null)
                {
                    tmp.text = "Grab menu with Trigger/Grip to inspect.\nSpeak French naturally • Press Y for Fox help.";
                    tmp.color = new Color(0.16f, 0.14f, 0.12f, 1f);
                }
            }

            // 3. Create sleek minimalist Mic Status Pill on the table
            Transform oldPill = root.Find("Mic status indicator");
            if (oldPill != null) UnityEngine.Object.DestroyImmediate(oldPill.gameObject);

            GameObject micPill = GameObject.CreatePrimitive(PrimitiveType.Cube);
            micPill.name = "Mic status indicator";
            micPill.transform.SetParent(root, false);
            micPill.transform.localPosition = new Vector3(0f, 0.885f, -0.42f);
            micPill.transform.localRotation = Quaternion.Euler(42f, 0f, 0f);
            micPill.transform.localScale = new Vector3(0.38f, 0.075f, 0.008f);
            UnityEngine.Object.DestroyImmediate(micPill.GetComponent<Collider>());
            micPill.GetComponent<Renderer>().sharedMaterial = backdropMat;

            TMP_Text micLabel = CreateText("Mic status label", micPill.transform, "● LISTENING (FR)",
                new Vector3(0f, 0f, -0.65f), new Vector2(0.35f, 0.065f), 0.022f, CrispTextColor, font);
            micLabel.transform.localScale = new Vector3(1f / 0.38f, 1f / 0.075f, 1f / 0.008f);
            micLabel.transform.localPosition = new Vector3(0f, 0f, -0.75f);

            if (voice != null)
            {
                voice.microphoneLabel = micLabel;
                voice.autoListenVad = true;
                EditorUtility.SetDirty(voice);
            }

            // 4. Deactivate green-sphere hummingbird & configure floating_fox as active companion
            GameObject oldBird = GameObject.Find("Restaurant hummingbird helper");
            if (oldBird != null)
            {
                oldBird.SetActive(false);
                EditorUtility.SetDirty(oldBird);
            }

            GameObject foxGo = GameObject.Find("floating_fox");
            HummingbirdCompanion foxCompanion = null;
            if (foxGo != null)
            {
                foxCompanion = GetOrAdd<HummingbirdCompanion>(foxGo);
                AudioSource foxAudio = GetOrAdd<AudioSource>(foxGo);
                foxAudio.spatialBlend = 1f;
                foxAudio.playOnAwake = false;

                // Ensure vocab hint label exists & is cleanly scaled above the fox
                GameObject vocabHint = GameObject.Find("French vocabulary hint");
                if (vocabHint != null)
                {
                    vocabHint.transform.SetParent(foxGo.transform, false);
                    float invScale = foxGo.transform.localScale.x > 0.001f ? 1f / foxGo.transform.localScale.x : 19.5f;
                    vocabHint.transform.localScale = Vector3.one * invScale;
                    vocabHint.transform.localPosition = new Vector3(0f, 3.2f, 0f);
                    var hintTmp = vocabHint.GetComponent<TMP_Text>();
                    if (hintTmp != null) hintTmp.color = CrispTextColor;
                }

                // Rebuild "Assisted response cards" as a clean world-scale container with 3 bilingual cards
                Transform oldCards = root.Find("Assisted response cards");
                if (oldCards != null) UnityEngine.Object.DestroyImmediate(oldCards.gameObject);

                GameObject cardsRoot = new GameObject("Assisted response cards");
                cardsRoot.transform.SetParent(root, false);
                cardsRoot.transform.localPosition = new Vector3(0f, 1.35f, -0.45f);

                // Subtle header label above the 3 Fox options
                CreateText("Fox help header", cardsRoot.transform,
                    "FOX HELPER · Say a French phrase aloud or select with Trigger / Pinch",
                    new Vector3(0f, 0.34f, 0f), new Vector2(0.95f, 0.08f), 0.022f,
                    new Color(0.58f, 0.78f, 0.96f, 1f), font);

                PhysicalControl cardA = CreateMinimalistButton("Response A", cardsRoot.transform,
                    new Vector3(0f, 0.19f, 0f), new Vector3(0.94f, 0.15f, 0.008f), "",
                    eval != null ? (UnityAction)eval.UseAnswerA : null, panelMat, font, interactionManager, 0.024f);
                PhysicalControl cardB = CreateMinimalistButton("Response B", cardsRoot.transform,
                    new Vector3(0f, 0.01f, 0f), new Vector3(0.94f, 0.15f, 0.008f), "",
                    eval != null ? (UnityAction)eval.UseAnswerB : null, panelMat, font, interactionManager, 0.024f);
                PhysicalControl cardC = CreateMinimalistButton("Response C", cardsRoot.transform,
                    new Vector3(0f, -0.17f, 0f), new Vector3(0.94f, 0.15f, 0.008f), "",
                    eval != null ? (UnityAction)eval.UseAnswerC : null, panelMat, font, interactionManager, 0.024f);

                // Wire cards onto HummingbirdCompanion (on floating_fox)
                SerializedObject foxSo = new SerializedObject(foxCompanion);
                if (xrOrigin.Camera != null)
                    foxSo.FindProperty("headCamera").objectReferenceValue = xrOrigin.Camera.transform;
                foxSo.FindProperty("audioSource").objectReferenceValue = foxAudio;
                foxSo.FindProperty("dialogueCardsRoot").objectReferenceValue = cardsRoot;
                foxSo.FindProperty("cardOptionAControl").objectReferenceValue = cardA;
                foxSo.FindProperty("cardOptionBControl").objectReferenceValue = cardB;
                foxSo.FindProperty("cardOptionCControl").objectReferenceValue = cardC;
                foxSo.FindProperty("cardOptionAText").objectReferenceValue = cardA.GetComponentInChildren<TMP_Text>(true);
                foxSo.FindProperty("cardOptionBText").objectReferenceValue = cardB.GetComponentInChildren<TMP_Text>(true);
                foxSo.FindProperty("cardOptionCText").objectReferenceValue = cardC.GetComponentInChildren<TMP_Text>(true);
                foxSo.FindProperty("enableLeftYButtonSummon").boolValue = true;
                if (vocabHint != null)
                {
                    foxSo.FindProperty("vocabLabelRoot").objectReferenceValue = vocabHint;
                    foxSo.FindProperty("vocabLabelText").objectReferenceValue = vocabHint.GetComponent<TMP_Text>();
                }
                foxSo.ApplyModifiedPropertiesWithoutUndo();

                cardsRoot.SetActive(false);

                if (eval != null)
                {
                    eval.companion = foxCompanion;
                    eval.helpCards = cardsRoot;
                    eval.answerA = cardA.GetComponentInChildren<TMP_Text>(true);
                    eval.answerB = cardB.GetComponentInChildren<TMP_Text>(true);
                    eval.answerC = cardC.GetComponentInChildren<TMP_Text>(true);
                    EditorUtility.SetDirty(eval);
                }

                var encounter = UnityEngine.Object.FindAnyObjectByType<CommunicativeEncounter>(FindObjectsInactive.Include);
                if (encounter != null)
                {
                    SerializedObject encSo = new SerializedObject(encounter);
                    encSo.FindProperty("companion").objectReferenceValue = foxCompanion;
                    encSo.ApplyModifiedPropertiesWithoutUndo();
                }

                EditorUtility.SetDirty(foxGo);
            }

            // 5. Attach ParisWaiterPersonality to "Paris waiter"
            Transform waiter = root.Find("Paris waiter");
            if (waiter != null)
            {
                ParisWaiterPersonality personality = GetOrAdd<ParisWaiterPersonality>(waiter.gameObject);
                personality.headOrBodyTransform = waiter;
                EditorUtility.SetDirty(personality);

                if (eval != null)
                {
                    eval.waiterPersonality = personality;
                    EditorUtility.SetDirty(eval);
                }
                if (voice != null)
                {
                    voice.npcPersonality = personality;
                    EditorUtility.SetDirty(voice);
                }
            }
        }

        static void RestyleAllPhysicalButtons(Material panelMat, XRInteractionManager interactionManager)
        {
            foreach (var control in UnityEngine.Object.FindObjectsByType<PhysicalControl>(FindObjectsInactive.Include))
            {
                if (control == null) continue;

                var interactable = control.GetComponent<XRSimpleInteractable>();
                if (interactable != null && interactionManager != null && interactable.interactionManager == null)
                    interactable.interactionManager = interactionManager;

                if (control.feedbackRenderer == null)
                    continue;

                control.feedbackRenderer.sharedMaterial = panelMat;
                control.applyMinimalistTintOnAwake = true;
                control.normalColor = PanelBaseColor;
                control.hoverColor = HoverHighlightColor;

                // Slim down chunky vertical buttons while preserving thin page-mounted buttons
                Vector3 scale = control.transform.localScale;
                bool isFlatOnPage = scale.y <= 0.045f && scale.z >= 0.08f;
                if (!isFlatOnPage && scale.z > 0.018f)
                {
                    float oldZ = scale.z;
                    scale.z = 0.012f;
                    control.transform.localScale = scale;

                    foreach (var tmp in control.GetComponentsInChildren<TMP_Text>(true))
                    {
                        tmp.color = CrispTextColor;
                        if (tmp.transform.parent == control.transform)
                        {
                            // Keep text positioned cleanly in front of the thinned Z surface
                            Vector3 lp = tmp.transform.localPosition;
                            float zSign = lp.z <= 0f ? -1f : 1f;
                            tmp.transform.localPosition = new Vector3(lp.x, lp.y, zSign * 0.65f);
                            // Compensate TMP scale against the new thin button Z
                            Vector3 ls = tmp.transform.localScale;
                            tmp.transform.localScale = new Vector3(ls.x, ls.y, ls.z * (oldZ / 0.012f));
                        }
                    }
                }
                else
                {
                    foreach (var tmp in control.GetComponentsInChildren<TMP_Text>(true))
                        tmp.color = CrispTextColor;
                }

                EditorUtility.SetDirty(control.gameObject);
                EditorUtility.SetDirty(control);
            }
        }

        static ComfortOptions EnsureComfortOptions(XROrigin xrOrigin)
        {
            ComfortOptions comfort = UnityEngine.Object.FindAnyObjectByType<ComfortOptions>(FindObjectsInactive.Include);
            if (comfort == null)
            {
                var go = new GameObject("Comfort station");
                comfort = go.AddComponent<ComfortOptions>();
            }

            comfort.xrOrigin = xrOrigin;
            if (comfort.leftController == null)
            {
                comfort.leftController = xrOrigin.GetComponentsInChildren<ControllerInputActionManager>(true)
                    .FirstOrDefault(c => c != null && c.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            EditorUtility.SetDirty(comfort);
            return comfort;
        }

        static void BuildOrUpdateWristMenu(
            string scenePath,
            XROrigin xrOrigin,
            XRInteractionManager interactionManager,
            ComfortOptions comfort,
            Material panelMat,
            Material backdropMat,
            TMP_FontAsset font)
        {
            Transform parent = xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : xrOrigin.transform;

            Transform oldWrist = parent.Find("Wrist Menu");
            if (oldWrist != null)
                UnityEngine.Object.DestroyImmediate(oldWrist.gameObject);

            bool isHub = scenePath.EndsWith("ImmersiveHub.unity", StringComparison.OrdinalIgnoreCase);
            bool isBook = scenePath.EndsWith("BookSelection.unity", StringComparison.OrdinalIgnoreCase);
            bool isParis = scenePath.EndsWith("Paris.unity", StringComparison.OrdinalIgnoreCase);

            GameObject wristRoot = new GameObject("Wrist Menu");
            wristRoot.transform.SetParent(parent, false);
            wristRoot.transform.localPosition = new Vector3(-0.25f, 1.1f, 0.35f);

            WristMenuController wrist = wristRoot.AddComponent<WristMenuController>();

            GameObject panelRoot = new GameObject("Wrist Menu Panel");
            panelRoot.transform.SetParent(wristRoot.transform, false);

            float panelHeight = isParis ? 0.24f : (isBook ? 0.16f : 0.115f);
            GameObject backdrop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            backdrop.name = "Wrist backdrop";
            backdrop.transform.SetParent(panelRoot.transform, false);
            backdrop.transform.localScale = new Vector3(0.20f, panelHeight, 0.005f);
            backdrop.GetComponent<Renderer>().sharedMaterial = backdropMat;
            UnityEngine.Object.DestroyImmediate(backdrop.GetComponent<Collider>());

            CreateText("Wrist header", panelRoot.transform, "MENU",
                new Vector3(0f, panelHeight * 0.5f - 0.018f, -0.004f), new Vector2(0.18f, 0.025f),
                0.013f, new Color(0.58f, 0.78f, 0.96f, 1f), font);

            float y = panelHeight * 0.5f - 0.048f;
            const float step = 0.043f;

            PhysicalControl moveBtn = CreateMinimalistButton("Wrist movement toggle", panelRoot.transform,
                new Vector3(0f, y, -0.004f), new Vector3(0.175f, 0.034f, 0.007f), "MOVE: TELEPORT",
                wrist.ToggleMovementMode, panelMat, font, interactionManager, 0.013f);
            y -= step;

            PhysicalControl postureBtn = CreateMinimalistButton("Wrist posture toggle", panelRoot.transform,
                new Vector3(0f, y, -0.004f), new Vector3(0.175f, 0.034f, 0.007f), "POSTURE: STANDING",
                wrist.ToggleSeatedStandingMode, panelMat, font, interactionManager, 0.013f);
            y -= step;

            string returnName = isBook ? "Return to world selection" : (isParis ? "Return to experience book" : "Wrist back button");
            string returnCaption = isBook ? "BACK TO GLOBE" : (isParis ? "BACK TO BOOK" : "BACK");
            PhysicalControl backBtn = CreateMinimalistButton(returnName, panelRoot.transform,
                new Vector3(0f, y, -0.004f), new Vector3(0.175f, 0.034f, 0.007f), returnCaption,
                wrist.ReturnToPreviousScene, panelMat, font, interactionManager, 0.013f);
            if (isHub)
                backBtn.gameObject.SetActive(false);
            else
                y -= step;

            PhysicalControl foxBtn = null;
            PhysicalControl restartBtn = null;
            if (isParis)
            {
                foxBtn = CreateMinimalistButton("Wrist fox help", panelRoot.transform,
                    new Vector3(0f, y, -0.004f), new Vector3(0.175f, 0.034f, 0.007f), "FOX HELP (Y)",
                    wrist.SummonFoxHelp, panelMat, font, interactionManager, 0.013f);
                y -= step;

                restartBtn = CreateMinimalistButton("Wrist restart conversation", panelRoot.transform,
                    new Vector3(0f, y, -0.004f), new Vector3(0.175f, 0.034f, 0.007f), "RESTART",
                    wrist.RestartConversation, panelMat, font, interactionManager, 0.013f);
            }

            var multimodalRig = GetOrAdd<QuestMultimodalRig>(xrOrigin.gameObject);
            var journey = UnityEngine.Object.FindAnyObjectByType<SceneJourney>(FindObjectsInactive.Include);
            var eval = UnityEngine.Object.FindAnyObjectByType<RestaurantEvaluation>(FindObjectsInactive.Include);
            var companion = UnityEngine.Object.FindAnyObjectByType<HummingbirdCompanion>(FindObjectsInactive.Include);

            SerializedObject so = new SerializedObject(wrist);
            if (xrOrigin.Camera != null)
                so.FindProperty("headCamera").objectReferenceValue = xrOrigin.Camera.transform;
            so.FindProperty("multimodalRig").objectReferenceValue = multimodalRig;
            so.FindProperty("menuCanvas").objectReferenceValue = panelRoot;
            so.FindProperty("comfortOptions").objectReferenceValue = comfort;
            so.FindProperty("sceneJourney").objectReferenceValue = journey;
            so.FindProperty("restaurantEvaluation").objectReferenceValue = eval;
            so.FindProperty("companion").objectReferenceValue = companion;
            so.FindProperty("movementStatusText").objectReferenceValue = moveBtn.GetComponentInChildren<TMP_Text>(true);
            so.FindProperty("seatedStandingStatusText").objectReferenceValue = postureBtn.GetComponentInChildren<TMP_Text>(true);
            so.FindProperty("returnButtonObject").objectReferenceValue = backBtn.gameObject;
            so.FindProperty("returnButtonText").objectReferenceValue = backBtn.GetComponentInChildren<TMP_Text>(true);
            if (foxBtn != null) so.FindProperty("foxHelpButtonObject").objectReferenceValue = foxBtn.gameObject;
            if (restartBtn != null) so.FindProperty("restartButtonObject").objectReferenceValue = restartBtn.gameObject;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(wristRoot);
        }

        static void EnsureMultimodalRig(XROrigin xrOrigin, XRInteractionManager interactionManager)
        {
            var modality = xrOrigin.GetComponent<XRInputModalityManager>();
            if (modality == null) modality = xrOrigin.GetComponentInChildren<XRInputModalityManager>(true);
            var multimodal = GetOrAdd<QuestMultimodalRig>(xrOrigin.gameObject);

            SerializedObject so = new SerializedObject(multimodal);
            so.FindProperty("xrOrigin").objectReferenceValue = xrOrigin;
            so.FindProperty("modalityManager").objectReferenceValue = modality;
            so.FindProperty("interactionManager").objectReferenceValue = interactionManager;
            if (modality != null)
            {
                so.FindProperty("leftHand").objectReferenceValue = modality.leftHand;
                so.FindProperty("rightHand").objectReferenceValue = modality.rightHand;
                so.FindProperty("leftController").objectReferenceValue = modality.leftController;
                so.FindProperty("rightController").objectReferenceValue = modality.rightController;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(multimodal);
        }

        static PhysicalControl CreateMinimalistButton(
            string name,
            Transform parent,
            Vector3 localPos,
            Vector3 scale,
            string caption,
            UnityAction callback,
            Material panelMat,
            TMP_FontAsset font,
            XRInteractionManager interactionManager,
            float textHeight)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            Renderer renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = panelMat;

            PhysicalControl control = go.AddComponent<PhysicalControl>();
            control.feedbackRenderer = renderer;
            control.applyMinimalistTintOnAwake = true;
            control.normalColor = PanelBaseColor;
            control.hoverColor = HoverHighlightColor;

            XRSimpleInteractable interactable = go.GetComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            Collider col = go.GetComponent<Collider>();
            if (col != null) interactable.colliders.Add(col);
            if (interactionManager != null) interactable.interactionManager = interactionManager;

            if (callback != null)
                UnityEventTools.AddPersistentListener(control.pressed, callback);

            TMP_Text label = CreateText(name + " label", parent, caption,
                localPos + Vector3.back * (scale.z * 0.5f + 0.002f),
                new Vector2(scale.x * 0.92f, scale.y * 0.88f),
                textHeight, CrispTextColor, font);
            label.transform.SetParent(go.transform, true);
            return control;
        }

        static TMP_Text CreateText(
            string name,
            Transform parent,
            string content,
            Vector3 localPos,
            Vector2 sizeDelta,
            float letterHeight,
            Color color,
            TMP_FontAsset font)
        {
            GameObject go = new GameObject(name, typeof(TextMeshPro));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            TextMeshPro text = go.GetComponent<TextMeshPro>();
            if (font != null) text.font = font;
            text.text = content;
            text.fontSize = letterHeight * 10f;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = sizeDelta;
            return text;
        }

        static void DestroyChildIfFound(Transform parent, string name)
        {
            if (parent == null) return;
            Transform child = parent.Find(name);
            if (child != null)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        static void DestroyNamed(string name)
        {
            GameObject go = GameObject.Find(name);
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            T existing = go.GetComponent<T>();
            return existing != null ? existing : go.AddComponent<T>();
        }
    }
}
