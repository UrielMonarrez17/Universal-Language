using System;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;

namespace Alterlife.Immersive.Editor
{
    /// <summary>Upgrades ImmersiveHub, BookSelection, and Paris scenes for Meta Quest multimodal hands + controllers.</summary>
    public static class QuestSceneRigUpdater
    {
        const string HandsRigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Hands Interaction Demo/Prefabs/XR Origin Hands (XR Rig).prefab";
        const string SageGreenMaterialPath = "Assets/Alterlife/Immersive/Generated/Sage green.mat";
        const string IvoryPaperMaterialPath = "Assets/Alterlife/Immersive/Generated/Ivory paper.mat";
        const string FontAssetPath = "Assets/VRTemplateAssets/Fonts/Inter/Inter-Regular SDF.asset";

        static readonly string[] TargetScenes =
        {
            "Assets/Scenes/ImmersiveHub.unity",
            "Assets/Scenes/BookSelection.unity",
            "Assets/Scenes/Paris.unity",
        };

        static readonly string[] ComfortScenes =
        {
            "Assets/Scenes/ImmersiveHub.unity",
            "Assets/Scenes/BookSelection.unity",
        };

        [MenuItem("Alterlife/Quest/Upgrade Scenes for Quest Multimodal")]
        public static void UpgradeAllScenes()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            var handsRigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HandsRigPrefabPath);
            if (handsRigPrefab == null)
            {
                Debug.LogError("Missing hands rig prefab at " + HandsRigPrefabPath);
                return;
            }

            foreach (var scenePath in TargetScenes)
            {
                if (!System.IO.File.Exists(scenePath))
                {
                    Debug.LogWarning("Skipping missing scene: " + scenePath);
                    continue;
                }

                UpgradeScene(scenePath, handsRigPrefab);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("ALTERLIFE_QUEST_SCENES_UPGRADED");
        }

        static void UpgradeScene(string scenePath, GameObject handsRigPrefab)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var xrOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                Debug.LogWarning("No active XROrigin found in scene: " + scenePath);
                return;
            }

            var cameraOffset = xrOrigin.CameraFloorOffsetObject != null
                ? xrOrigin.CameraFloorOffsetObject.transform
                : xrOrigin.transform.Find("Camera Offset");
            if (cameraOffset == null)
            {
                Debug.LogWarning("No Camera Offset found under XROrigin in scene: " + scenePath);
                return;
            }

            var modalityManager = xrOrigin.GetComponent<XRInputModalityManager>()
                ?? xrOrigin.GetComponentInChildren<XRInputModalityManager>(true);
            var interactionManager = UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>();

            EnsureHandsOnRig(cameraOffset, modalityManager, interactionManager, handsRigPrefab);

            var multimodalRig = xrOrigin.GetComponent<QuestMultimodalRig>()
                ?? xrOrigin.gameObject.AddComponent<QuestMultimodalRig>();
            WireMultimodalRig(multimodalRig, xrOrigin, modalityManager, interactionManager);

            var hubRendering = UnityEngine.Object.FindAnyObjectByType<HubRendering>(FindObjectsInactive.Include);
            var perfHost = hubRendering != null ? hubRendering.gameObject : xrOrigin.gameObject;
            if (perfHost.GetComponent<QuestPerformanceOptimizer>() == null)
            {
                var optimizer = perfHost.AddComponent<QuestPerformanceOptimizer>();
                MarkDirty(optimizer);
            }

            if (ComfortScenes.Contains(scenePath))
            {
                UpgradeComfortStation(xrOrigin, interactionManager);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void EnsureHandsOnRig(
            Transform cameraOffset,
            XRInputModalityManager modalityManager,
            XRInteractionManager interactionManager,
            GameObject handsRigPrefab)
        {
            var leftHand = cameraOffset.Find("Left Hand");
            var rightHand = cameraOffset.Find("Right Hand");

            bool needsExtraction = leftHand == null || rightHand == null
                || (modalityManager != null && (modalityManager.leftHand == null || modalityManager.rightHand == null));

            if (needsExtraction && (leftHand == null || rightHand == null))
            {
                var tempRig = (GameObject)PrefabUtility.InstantiatePrefab(handsRigPrefab);
                try
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(tempRig))
                    {
                        PrefabUtility.UnpackPrefabInstance(tempRig, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                    }

                    var tempOffset = tempRig.transform.Find("Camera Offset");
                    if (tempOffset != null)
                    {
                        if (leftHand == null)
                        {
                            var sourceLeft = tempOffset.Find("Left Hand");
                            if (sourceLeft != null)
                            {
                                sourceLeft.SetParent(cameraOffset, false);
                                leftHand = sourceLeft;
                            }
                        }

                        if (rightHand == null)
                        {
                            var sourceRight = tempOffset.Find("Right Hand");
                            if (sourceRight != null)
                            {
                                sourceRight.SetParent(cameraOffset, false);
                                rightHand = sourceRight;
                            }
                        }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(tempRig);
                }
            }

            if (modalityManager != null)
            {
                if (leftHand != null)
                    modalityManager.leftHand = leftHand.gameObject;
                if (rightHand != null)
                    modalityManager.rightHand = rightHand.gameObject;
                MarkDirty(modalityManager);
            }

            if (leftHand != null)
                ConfigureHandRoot(leftHand, interactionManager);
            if (rightHand != null)
                ConfigureHandRoot(rightHand, interactionManager);
        }

        static void ConfigureHandRoot(Transform handRoot, XRInteractionManager interactionManager)
        {
            handRoot.gameObject.SetActive(false);
            MarkDirty(handRoot.gameObject);

            foreach (var child in handRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child == null || child == handRoot)
                    continue;

                if (child.name.Contains("Quest Visual", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(true);
                    MarkDirty(child.gameObject);
                }
                else if (child.name.Contains("Android XR Visual", StringComparison.OrdinalIgnoreCase))
                {
                    child.gameObject.SetActive(false);
                    MarkDirty(child.gameObject);
                }
            }

            RemoveHandTeleportInteractors(handRoot);

            foreach (var caster in handRoot.GetComponentsInChildren<CurveInteractionCaster>(true))
            {
                caster.hitDetectionType = CurveInteractionCaster.HitDetectionType.Raycast;
                MarkDirty(caster);
            }

            foreach (var poke in handRoot.GetComponentsInChildren<XRPokeInteractor>(true))
            {
                poke.requirePokeFilter = true;
                MarkDirty(poke);
            }

            if (interactionManager != null)
            {
                foreach (var interactor in handRoot.GetComponentsInChildren<XRBaseInteractor>(true))
                {
                    interactor.interactionManager = interactionManager;
                    MarkDirty(interactor);
                }

                foreach (var group in handRoot.GetComponentsInChildren<XRInteractionGroup>(true))
                {
                    group.interactionManager = interactionManager;
                    MarkDirty(group);
                }
            }
        }

        static void RemoveHandTeleportInteractors(Transform handRoot)
        {
            var teleportObjects = handRoot.GetComponentsInChildren<Transform>(true)
                .Where(t => t != null && t != handRoot &&
                    (t.name.Contains("Teleport Interactor", StringComparison.OrdinalIgnoreCase)
                     || t.name.Contains("TeleportInteractor", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            foreach (var teleportTransform in teleportObjects)
            {
                if (teleportTransform == null)
                    continue;

                if (PrefabUtility.IsPartOfPrefabInstance(teleportTransform.gameObject)
                    && !PrefabUtility.IsOutermostPrefabInstanceRoot(teleportTransform.gameObject))
                {
                    teleportTransform.gameObject.SetActive(false);
                    MarkDirty(teleportTransform.gameObject);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(teleportTransform.gameObject);
                }
            }

            foreach (var component in handRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;

                var typeName = component.GetType().Name;
                if (typeName.Contains("TeleportInteractor", StringComparison.OrdinalIgnoreCase))
                {
                    component.enabled = false;
                    component.gameObject.SetActive(false);
                    MarkDirty(component);
                    MarkDirty(component.gameObject);
                }
            }
        }

        static void WireMultimodalRig(
            QuestMultimodalRig multimodalRig,
            XROrigin xrOrigin,
            XRInputModalityManager modalityManager,
            XRInteractionManager interactionManager)
        {
            var serialized = new SerializedObject(multimodalRig);
            TryAssignObjectReference(serialized, xrOrigin, "xrOrigin", "m_XROrigin", "origin", "m_Origin");
            TryAssignObjectReference(serialized, modalityManager, "modalityManager", "m_ModalityManager", "inputModalityManager", "m_InputModalityManager");
            TryAssignObjectReference(serialized, interactionManager, "interactionManager", "m_InteractionManager");
            if (modalityManager != null)
            {
                TryAssignObjectReference(serialized, modalityManager.leftHand, "leftHand", "m_LeftHand");
                TryAssignObjectReference(serialized, modalityManager.rightHand, "rightHand", "m_RightHand");
                TryAssignObjectReference(serialized, modalityManager.leftController, "leftController", "m_LeftController");
                TryAssignObjectReference(serialized, modalityManager.rightController, "rightController", "m_RightController");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            MarkDirty(multimodalRig);
        }

        static void TryAssignObjectReference(SerializedObject serialized, UnityEngine.Object value, params string[] propertyNames)
        {
            if (value == null)
                return;

            foreach (var propertyName in propertyNames)
            {
                var prop = serialized.FindProperty(propertyName);
                if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference)
                {
                    prop.objectReferenceValue = value;
                }
            }
        }

        static void UpgradeComfortStation(XROrigin xrOrigin, XRInteractionManager interactionManager)
        {
            var comfort = UnityEngine.Object.FindAnyObjectByType<ComfortOptions>(FindObjectsInactive.Include);
            if (comfort == null)
            {
                Debug.LogWarning("ComfortOptions not found in scene: " + SceneManager.GetActiveScene().path);
                return;
            }

            comfort.xrOrigin = xrOrigin;

            var existingButton = comfort.transform.Find("Posture comfort toggle");
            GameObject buttonObject;
            TMP_Text postureLabel;

            if (existingButton == null)
            {
                buttonObject = CreatePostureToggleButton(comfort, interactionManager, out postureLabel);
            }
            else
            {
                buttonObject = existingButton.gameObject;
                buttonObject.transform.localPosition = new Vector3(0f, -0.76f, 0f);
                buttonObject.transform.localRotation = Quaternion.identity;
                buttonObject.transform.localScale = new Vector3(1.15f, 0.18f, 0.08f);

                postureLabel = buttonObject.GetComponentInChildren<TMP_Text>(true);
                if (postureLabel == null)
                {
                    postureLabel = CreateButtonLabel(buttonObject.transform, "Posture comfort toggle label", "POSTURE: STANDING / SEATED", new Vector3(1.15f, 0.18f, 0.08f));
                }

                var interactable = buttonObject.GetComponent<XRSimpleInteractable>();
                if (interactable != null && interactionManager != null)
                {
                    interactable.interactionManager = interactionManager;
                    MarkDirty(interactable);
                }
            }

            comfort.postureLabel = postureLabel;
            MarkDirty(buttonObject);
            MarkDirty(comfort);
        }

        static GameObject CreatePostureToggleButton(
            ComfortOptions comfort,
            XRInteractionManager interactionManager,
            out TMP_Text label)
        {
            var movementButton = comfort.transform.Find("Movement comfort toggle");
            Material sageGreen = AssetDatabase.LoadAssetAtPath<Material>(SageGreenMaterialPath);
            if (sageGreen == null && movementButton != null)
            {
                var movementRenderer = movementButton.GetComponent<Renderer>();
                if (movementRenderer != null)
                    sageGreen = movementRenderer.sharedMaterial;
            }

            var size = new Vector3(1.15f, 0.18f, 0.08f);
            var buttonGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            buttonGo.name = "Posture comfort toggle";
            buttonGo.transform.SetParent(comfort.transform, false);
            buttonGo.transform.localPosition = new Vector3(0f, -0.76f, 0f);
            buttonGo.transform.localRotation = Quaternion.identity;
            buttonGo.transform.localScale = size;

            var renderer = buttonGo.GetComponent<Renderer>();
            if (sageGreen != null)
                renderer.sharedMaterial = sageGreen;

            var control = buttonGo.AddComponent<PhysicalControl>();
            control.feedbackRenderer = renderer;

            var interactable = buttonGo.GetComponent<XRSimpleInteractable>() ?? buttonGo.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            var collider = buttonGo.GetComponent<Collider>();
            if (collider != null)
                interactable.colliders.Add(collider);
            if (interactionManager != null)
                interactable.interactionManager = interactionManager;

            UnityEventTools.AddPersistentListener(control.pressed, comfort.TogglePosture);

            label = CreateButtonLabel(buttonGo.transform, "Posture comfort toggle label", "POSTURE: STANDING / SEATED", size);
            return buttonGo;
        }

        static TMP_Text CreateButtonLabel(Transform buttonTransform, string name, string caption, Vector3 buttonSize)
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            var ivoryPaper = AssetDatabase.LoadAssetAtPath<Material>(IvoryPaperMaterialPath);
            var textColor = ivoryPaper != null ? ivoryPaper.color : (Color)new Color32(242, 220, 177, 255);

            var labelGo = new GameObject(name, typeof(TextMeshPro));
            labelGo.transform.SetParent(buttonTransform.parent, false);
            labelGo.transform.localPosition = buttonTransform.localPosition + Vector3.back * (buttonSize.z / 2f + 0.002f);
            labelGo.transform.localRotation = Quaternion.identity;

            var text = labelGo.GetComponent<TextMeshPro>();
            if (font != null)
                text.font = font;
            text.text = caption;
            text.fontSize = 0.45f;
            text.enableAutoSizing = true;
            text.fontSizeMin = 0.22f;
            text.fontSizeMax = 0.45f;
            text.color = textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.rectTransform.sizeDelta = new Vector2(buttonSize.x * 0.93f, buttonSize.y);

            labelGo.transform.SetParent(buttonTransform, true);
            return text;
        }

        static void MarkDirty(UnityEngine.Object obj)
        {
            if (obj == null)
                return;

            EditorUtility.SetDirty(obj);
            if (PrefabUtility.IsPartOfPrefabInstance(obj))
                PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
        }
    }
}
