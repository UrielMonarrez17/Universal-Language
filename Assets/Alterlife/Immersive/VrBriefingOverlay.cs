using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    public enum VrBriefingMode
    {
        StartupControls,
        ParisTransition
    }

    /// <summary>
    /// Minimalist Black-and-White VR briefing & async transition overlay used for:
    /// 1) Meta Quest Controls guide before starting in ImmersiveHub.
    /// 2) Fox Companion Help guide shown while Paris.unity preloads in the background.
    /// Places the board at a rock-solid world position in front of the player (never moving or recentering),
    /// disables world visuals & non-floor ray-blocking colliders, pauses locomotion/gravity safely,
    /// and reveals OK in transition mode only after Paris finishes loading.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class VrBriefingOverlay : MonoBehaviour
    {
        public VrBriefingMode mode = VrBriefingMode.StartupControls;
        public GameObject overlayRoot;
        public Transform domeTransform;
        public Transform boardTransform;
        public TMP_Text statusLabel;
        public TMP_Text okButtonLabel;
        public PhysicalControl okButton;

        [Header("Placement & Safety")]
        public float boardDistance = 1.50f;
        public float boardHeightOffset = 0.08f;
        [SerializeField] private float okSafetyDelaySeconds = 0.45f;

        public static bool HasActiveOverlay { get; private set; }
        static bool startupControlsDismissed;

        readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        readonly HashSet<Renderer> hiddenRendererSet = new HashSet<Renderer>();
        readonly List<Collider> hiddenWorldColliders = new List<Collider>();
        readonly HashSet<Collider> hiddenWorldColliderSet = new HashSet<Collider>();
        readonly List<XRBaseInteractable> pausedInteractables = new List<XRBaseInteractable>();
        readonly HashSet<XRBaseInteractable> pausedInteractableSet = new HashSet<XRBaseInteractable>();
        readonly List<Behaviour> pausedLocomotionBehaviours = new List<Behaviour>();
        CharacterController pausedCharacterController;

        bool isOpen;
        bool isTransitioning;
        bool scenePreloaded;
        bool okReady;
        bool boardPoseLocked;
        float okUnlockTime;
        AsyncOperation pendingLoad;
        Camera viewerCamera;
        Coroutine okUnlockRoutine;
        Coroutine trackingSettleRoutine;

        public bool IsOpen => isOpen;

        void Awake()
        {
            if (overlayRoot == null)
                overlayRoot = gameObject;

            viewerCamera = Camera.main;

            if (mode == VrBriefingMode.StartupControls && !startupControlsDismissed && !Application.isBatchMode)
            {
                OpenOverlay(
                    "<b>META QUEST CONTROLS</b>  ·  Select OK below to begin",
                    "OK · START",
                    lockPoseImmediately: false);
            }
            else
            {
                SetOverlayActive(false);
            }
        }

        void Start()
        {
            if (!isOpen)
                return;

            // Second sweep catches any world visuals enabled during Start() (e.g. FranceSelection at order 100).
            HideWorldVisualsAndPauseLocomotion();
            ClearScreenTransitionVeil();
        }

        void OnDestroy()
        {
            if (isOpen)
                HasActiveOverlay = false;
            RestoreHiddenWorld();
        }

        void LateUpdate()
        {
            if (!isOpen)
                return;

            if (viewerCamera == null)
                viewerCamera = Camera.main;
            if (viewerCamera == null)
                return;

            // Keep the matte charcoal surround sphere centered on the head; the board itself stays fixed in world space.
            if (domeTransform != null)
                domeTransform.position = viewerCamera.transform.position;
        }

        public void BeginSceneTransition(string scenePath)
        {
            if (isTransitioning || string.IsNullOrWhiteSpace(scenePath))
                return;

            if (Application.isBatchMode || !Application.CanStreamedLevelBeLoaded(scenePath))
            {
                SceneManager.LoadSceneAsync(scenePath);
                return;
            }

            isTransitioning = true;
            scenePreloaded = false;
            okReady = false;

            OpenOverlay(
                "<b>LOADING PARIS...</b>  ·  Preparing Le Café de Paris",
                "OK · ENTER PARIS",
                lockPoseImmediately: true);

            StartCoroutine(PreloadAndWaitRoutine(scenePath));
        }

        public void ConfirmOk()
        {
            if (!isOpen || !okReady)
                return;

            if (mode == VrBriefingMode.StartupControls)
            {
                startupControlsDismissed = true;
                SetOverlayActive(false);
                return;
            }

            if (scenePreloaded && pendingLoad != null)
            {
                okReady = false;
                if (okButtonLabel != null) okButtonLabel.text = "ENTERING PARIS...";
                Debug.Log("[VrBriefing] OK · ENTER PARIS pressed — activating Paris scene.");
                pendingLoad.allowSceneActivation = true;
            }
        }

        IEnumerator PreloadAndWaitRoutine(string scenePath)
        {
            yield return null;
            pendingLoad = SceneManager.LoadSceneAsync(scenePath);
            if (pendingLoad == null)
            {
                isTransitioning = false;
                yield break;
            }

            pendingLoad.allowSceneActivation = false;

            while (pendingLoad.progress < 0.9f)
            {
                int pct = Mathf.Clamp(Mathf.RoundToInt((pendingLoad.progress / 0.9f) * 100f), 1, 99);
                if (statusLabel != null)
                    statusLabel.text = $"<b>LOADING PARIS · {pct}%</b>  ·  Read the Fox guide while Paris loads";
                yield return null;
            }

            scenePreloaded = true;

            while (Time.unscaledTime < okUnlockTime)
                yield return null;

            float triggerDeadline = Time.unscaledTime + 1.0f;
            while (IsAnyTriggerHeld() && Time.unscaledTime < triggerDeadline)
                yield return null;

            if (statusLabel != null)
                statusLabel.text = "<b>PARIS READY · 100%</b>  ·  Select OK below to enter Le Café de Paris";
            if (okButtonLabel != null)
                okButtonLabel.text = "OK · ENTER PARIS";
            if (okButton != null)
                okButton.gameObject.SetActive(true);

            okReady = true;
            Debug.Log("[VrBriefing] Paris preloaded (100%) — OK · ENTER PARIS button enabled.");
        }

        void OpenOverlay(string initialStatus, string initialButtonText, bool lockPoseImmediately)
        {
            okUnlockTime = Time.unscaledTime + okSafetyDelaySeconds;
            okReady = false;
            boardPoseLocked = false;

            if (okButton != null)
                okButton.gameObject.SetActive(mode == VrBriefingMode.StartupControls);

            SetOverlayActive(true);

            if (statusLabel != null)
                statusLabel.text = initialStatus ?? "";
            if (okButtonLabel != null)
                okButtonLabel.text = initialButtonText ?? "OK";

            ClearScreenTransitionVeil();
            PlaceBoardAtInitialViewPose(useTrackingCamera: lockPoseImmediately);

            if (lockPoseImmediately)
            {
                boardPoseLocked = true;
            }
            else
            {
                if (trackingSettleRoutine != null) StopCoroutine(trackingSettleRoutine);
                trackingSettleRoutine = StartCoroutine(LockStartupBoardOnceTrackingSettles());

                if (okUnlockRoutine != null) StopCoroutine(okUnlockRoutine);
                okUnlockRoutine = StartCoroutine(UnlockStartupOkRoutine());
            }
        }

        IEnumerator LockStartupBoardOnceTrackingSettles()
        {
            float deadline = Time.unscaledTime + 0.5f;
            while (!boardPoseLocked && Time.unscaledTime < deadline)
            {
                if (viewerCamera == null) viewerCamera = Camera.main;
                if (viewerCamera != null && viewerCamera.transform.localPosition.y > 0.25f)
                {
                    PlaceBoardAtInitialViewPose(useTrackingCamera: true);
                    boardPoseLocked = true;
                    yield break;
                }
                yield return null;
            }

            if (!boardPoseLocked)
            {
                PlaceBoardAtInitialViewPose(useTrackingCamera: true);
                boardPoseLocked = true;
            }
        }

        IEnumerator UnlockStartupOkRoutine()
        {
            while (Time.unscaledTime < okUnlockTime)
                yield return null;

            float triggerDeadline = Time.unscaledTime + 1.0f;
            while (IsAnyTriggerHeld() && Time.unscaledTime < triggerDeadline)
                yield return null;

            okReady = true;
        }

        static bool IsAnyTriggerHeld()
        {
            return IsNodeTriggerHeld(XRNode.LeftHand) || IsNodeTriggerHeld(XRNode.RightHand);
        }

        static bool IsNodeTriggerHeld(XRNode node)
        {
            InputDevice dev = InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) return false;
            bool btn = dev.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) && pressed;
            bool analog = dev.TryGetFeatureValue(CommonUsages.trigger, out float val) && val > 0.25f;
            return btn || analog;
        }

        void SetOverlayActive(bool active)
        {
            isOpen = active;
            HasActiveOverlay = active;

            if (active)
                HideWorldVisualsAndPauseLocomotion();
            else
                RestoreHiddenWorld();

            if (overlayRoot != null && overlayRoot != gameObject)
                overlayRoot.SetActive(active);
            else
            {
                if (domeTransform != null) domeTransform.gameObject.SetActive(active);
                if (boardTransform != null) boardTransform.gameObject.SetActive(active);
            }
        }

        void HideWorldVisualsAndPauseLocomotion()
        {
            var scene = gameObject.scene;
            if (!scene.IsValid())
                return;

            Transform rigRoot = ResolveXrRigRoot();
            Transform selfRoot = transform;

            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                Transform rt = root.transform;
                if (rt == selfRoot || (rigRoot != null && rt == rigRoot))
                    continue;

                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || !r.enabled) continue;
                    if (r.transform.IsChildOf(selfRoot) || (rigRoot != null && r.transform.IsChildOf(rigRoot)))
                        continue;
                    r.enabled = false;
                    if (hiddenRendererSet.Add(r))
                        hiddenRenderers.Add(r);
                }

                // Disable non-floor world colliders so invisible desks/books/plaques cannot block rays to OK.
                foreach (var c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c == null || !c.enabled || IsFloorCollider(c)) continue;
                    if (c.transform.IsChildOf(selfRoot) || (rigRoot != null && c.transform.IsChildOf(rigRoot)))
                        continue;
                    c.enabled = false;
                    if (hiddenWorldColliderSet.Add(c))
                        hiddenWorldColliders.Add(c);
                }

                foreach (var inter in root.GetComponentsInChildren<XRBaseInteractable>(true))
                {
                    if (inter == null || !inter.enabled) continue;
                    if (inter.transform.IsChildOf(selfRoot) || (rigRoot != null && inter.transform.IsChildOf(rigRoot)))
                        continue;
                    inter.enabled = false;
                    if (pausedInteractableSet.Add(inter))
                        pausedInteractables.Add(inter);
                }
            }

            if (rigRoot != null)
            {
                if (pausedCharacterController == null)
                {
                    var cc = rigRoot.GetComponent<CharacterController>();
                    if (cc != null && cc.enabled)
                    {
                        cc.enabled = false;
                        pausedCharacterController = cc;
                    }
                }

                if (pausedLocomotionBehaviours.Count == 0)
                {
                    Transform locomotion = FindDescendantByName(rigRoot, "Locomotion");
                    if (locomotion != null)
                    {
                        foreach (var b in locomotion.GetComponentsInChildren<Behaviour>(true))
                        {
                            if (b != null && b.enabled)
                            {
                                b.enabled = false;
                                pausedLocomotionBehaviours.Add(b);
                            }
                        }
                    }
                }
            }
        }

        static bool IsFloorCollider(Collider c)
        {
            string n = c.gameObject.name;
            return n.IndexOf("floor", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("ground", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   n.IndexOf("terrace", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void RestoreHiddenWorld()
        {
            for (int i = 0; i < hiddenRenderers.Count; i++)
                if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = true;
            hiddenRenderers.Clear();
            hiddenRendererSet.Clear();

            for (int i = 0; i < hiddenWorldColliders.Count; i++)
                if (hiddenWorldColliders[i] != null) hiddenWorldColliders[i].enabled = true;
            hiddenWorldColliders.Clear();
            hiddenWorldColliderSet.Clear();

            for (int i = 0; i < pausedInteractables.Count; i++)
                if (pausedInteractables[i] != null) pausedInteractables[i].enabled = true;
            pausedInteractables.Clear();
            pausedInteractableSet.Clear();

            for (int i = 0; i < pausedLocomotionBehaviours.Count; i++)
                if (pausedLocomotionBehaviours[i] != null) pausedLocomotionBehaviours[i].enabled = true;
            pausedLocomotionBehaviours.Clear();

            if (pausedCharacterController != null)
            {
                pausedCharacterController.enabled = true;
                pausedCharacterController = null;
            }
        }

        void ClearScreenTransitionVeil()
        {
            if (viewerCamera == null) viewerCamera = Camera.main;
            var fade = viewerCamera != null ? viewerCamera.GetComponent<HeadCollisionFade>() : null;
            if (fade != null)
                fade.TransitionOpacity = 0f;
        }

        void PlaceBoardAtInitialViewPose(bool useTrackingCamera)
        {
            if (viewerCamera == null)
                viewerCamera = Camera.main;

            Transform rigRoot = ResolveXrRigRoot();
            Vector3 anchorPos;
            Vector3 flatFwd;

            if (useTrackingCamera && viewerCamera != null)
            {
                anchorPos = viewerCamera.transform.position;
                if (anchorPos.y < 0.35f && rigRoot != null)
                    anchorPos.y = rigRoot.position.y + 1.38f;
                flatFwd = Vector3.ProjectOnPlane(viewerCamera.transform.forward, Vector3.up);
            }
            else if (rigRoot != null)
            {
                anchorPos = rigRoot.position + Vector3.up * 1.38f;
                flatFwd = Vector3.ProjectOnPlane(rigRoot.forward, Vector3.up);
            }
            else
            {
                anchorPos = new Vector3(0f, 1.38f, 0f);
                flatFwd = Vector3.forward;
            }

            if (flatFwd.sqrMagnitude < 0.001f)
                flatFwd = rigRoot != null ? Vector3.ProjectOnPlane(rigRoot.forward, Vector3.up) : Vector3.forward;
            if (flatFwd.sqrMagnitude < 0.001f)
                flatFwd = Vector3.forward;
            flatFwd.Normalize();

            if (domeTransform != null)
                domeTransform.position = anchorPos;

            if (boardTransform != null)
            {
                boardTransform.position = anchorPos + flatFwd * boardDistance + Vector3.up * boardHeightOffset;
                boardTransform.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
            }
        }

        Transform ResolveXrRigRoot()
        {
            var rig = FindAnyObjectByType<QuestMultimodalRig>(FindObjectsInactive.Include);
            if (rig != null) return rig.transform.root;
            if (viewerCamera == null) viewerCamera = Camera.main;
            return viewerCamera != null ? viewerCamera.transform.root : null;
        }

        static Transform FindDescendantByName(Transform root, string targetName)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                if (tr != null && tr.name == targetName) return tr;
            return null;
        }
    }
}
