using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Alterlife.Immersive
{
    [DefaultExecutionOrder(100)]
    public sealed class FranceSelection : MonoBehaviour
    {
        public LearningSession session;
        public XRSimpleInteractable country;
        public Renderer highlight;
        public GameObject label;
        public GameObject confirmControl;
        public TMP_Text status;
        public StudyDoor door;
        public Transform marker;
        public GameObject markerTooltip;
        public Transform bookArrival;
        public TeleportationProvider teleportation;
        public bool travelOnSelection = true;
        public SceneJourney sceneJourney;
        public static bool ReturnToBookRequested;

        bool travelling;
        public bool Pending { get; private set; }
        bool hovered;
        Camera viewer;
        PhysicalPokeFilter pokeFilter;

        readonly List<Renderer> flagRenderers = new List<Renderer>();
        readonly List<Color> flagBaseColors = new List<Color>();
        MaterialPropertyBlock flagPropertyBlock;
        Vector3 markerBaseScale = Vector3.one;

        void Start()
        {
            viewer = Camera.main;
            flagPropertyBlock = new MaterialPropertyBlock();

            CacheFlagRenderers();

            if (country != null)
            {
                pokeFilter = new PhysicalPokeFilter(() => isActiveAndEnabled);
                country.selectFilters.Add(pokeFilter);
                country.interactionStrengthFilters.Add(pokeFilter);
                country.firstHoverEntered.AddListener(Enter);
                country.lastHoverExited.AddListener(Exit);
                country.selectEntered.AddListener(Select);
                country.activated.AddListener(Activate);
            }

            if (session != null)
                session.Changed += Refresh;

            Refresh();

            if (ReturnToBookRequested)
            {
                ReturnToBookRequested = false;
                if (session != null && session.FranceSelected)
                    StartCoroutine(TravelToBook());
            }
        }

        void CacheFlagRenderers()
        {
            flagRenderers.Clear();
            flagBaseColors.Clear();

            if (marker == null)
                return;

            markerBaseScale = marker.localScale;
            AddRenderersFrom(marker);

            if (marker.parent != null)
            {
                Transform stem = marker.parent.Find("France pin stem");
                if (stem != null)
                    AddRenderersFrom(stem);
            }
        }

        void AddRenderersFrom(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.GetComponent<TMP_Text>() != null)
                    continue;

                if (flagRenderers.Contains(r))
                    continue;

                Color baseColor = r.sharedMaterial != null && r.sharedMaterial.HasProperty("_BaseColor")
                    ? r.sharedMaterial.GetColor("_BaseColor")
                    : (r.sharedMaterial != null ? r.sharedMaterial.color : Color.white);

                if (Application.isPlaying && r.material != null)
                    r.material.EnableKeyword("_EMISSION");

                flagRenderers.Add(r);
                flagBaseColors.Add(baseColor);
            }
        }

        void LateUpdate()
        {
            // Location remains attached to France, while the flag stays legible from either controller/hand.
            if (!viewer) viewer = Camera.main;
            if (marker && viewer)
            {
                Vector3 awayFromPlayer = marker.position - viewer.transform.position;
                if (awayFromPlayer.sqrMagnitude > .0001f)
                    marker.rotation = Quaternion.LookRotation(awayFromPlayer, Vector3.up);
            }
        }

        void OnDestroy()
        {
            if (session) session.Changed -= Refresh;
            if (!country) return;
            if (pokeFilter != null)
            {
                country.selectFilters.Remove(pokeFilter);
                country.interactionStrengthFilters.Remove(pokeFilter);
            }
            country.firstHoverEntered.RemoveListener(Enter);
            country.lastHoverExited.RemoveListener(Exit);
            country.selectEntered.RemoveListener(Select);
            country.activated.RemoveListener(Activate);
        }

        void Enter(HoverEnterEventArgs _) { hovered = true; Refresh(); }
        void Exit(HoverExitEventArgs _) { hovered = false; Refresh(); }

        void Select(SelectEnterEventArgs args)
        {
            if (PhysicalControl.IsControllerGripOnly(args.interactorObject))
                return;

            TargetFrance();
        }

        void Activate(ActivateEventArgs _) => TargetFrance();

        public void TargetFrance()
        {
            if (travelling) return;
            Pending = true;
            if (travelOnSelection) Confirm(); else Refresh();
        }

        public void Confirm()
        {
            if (!Pending) return;
            if (session != null) session.SelectFrance();
            Pending = false;
            Refresh();
            if (sceneJourney) { travelling = true; sceneJourney.OpenBook(); }
            else if (travelOnSelection && bookArrival && teleportation && !travelling) StartCoroutine(TravelToBook());
        }

        IEnumerator TravelToBook()
        {
            if (!bookArrival || !teleportation || travelling) yield break;
            travelling = true;
            var fade = viewer ? viewer.GetComponent<HeadCollisionFade>() : null;
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime)
            {
                if (fade) fade.TransitionOpacity = t / .18f;
                yield return null;
            }
            if (fade) fade.TransitionOpacity = 1;
            teleportation.QueueTeleportRequest(new TeleportRequest
            {
                destinationPosition = bookArrival.position,
                destinationRotation = bookArrival.rotation,
                matchOrientation = MatchOrientation.TargetUpAndForward
            });
            yield return new WaitForSecondsRealtime(.15f);
            for (float t = 0; t < .25f; t += Time.unscaledDeltaTime)
            {
                if (fade) fade.TransitionOpacity = 1 - t / .25f;
                yield return null;
            }
            if (fade) fade.TransitionOpacity = 0;
            travelling = false;
        }

        void Refresh()
        {
            bool selected = session != null && session.FranceSelected;
            bool isHighlighted = hovered || Pending || selected;

            if (highlight) highlight.enabled = isHighlighted;
            if (label) label.SetActive(isHighlighted);
            if (markerTooltip) markerTooltip.SetActive(isHighlighted);
            if (confirmControl) confirmControl.SetActive(Pending && !selected);

            UpdateFlagHighlight(hovered || Pending, selected);

            if (status && session != null)
            {
                status.text = "France\nLanguage: French\nLanguage Progress: " + Mathf.RoundToInt(session.LanguageProgress * 100) + "%" +
                    (selected ? "\nStudy unlocked — welcome in" : travelOnSelection ? "\nTrigger or pinch France to visit the book" : "\nSelect France, then confirm");
            }

            if (selected && door) door.Unlock();
        }

        void UpdateFlagHighlight(bool activeHoverOrPending, bool selected)
        {
            if (marker != null)
            {
                float scaleFactor = activeHoverOrPending ? 1.22f : (selected ? 1.1f : 1.0f);
                marker.localScale = markerBaseScale * scaleFactor;
            }

            if (flagPropertyBlock == null)
                return;

            bool glow = activeHoverOrPending || selected;
            Color warmGlow = new Color(1f, 0.92f, 0.58f, 1f);

            for (int i = 0; i < flagRenderers.Count; i++)
            {
                Renderer r = flagRenderers[i];
                if (r == null) continue;

                Color baseColor = flagBaseColors[i];
                Color tinted = activeHoverOrPending
                    ? Color.Lerp(baseColor, warmGlow, 0.42f)
                    : (selected ? Color.Lerp(baseColor, warmGlow, 0.2f) : baseColor);
                Color emission = glow
                    ? new Color(
                        Mathf.Max(tinted.r, 0.25f) * (activeHoverOrPending ? 0.75f : 0.35f),
                        Mathf.Max(tinted.g, 0.25f) * (activeHoverOrPending ? 0.75f : 0.35f),
                        Mathf.Max(tinted.b, 0.25f) * (activeHoverOrPending ? 0.75f : 0.35f),
                        1f)
                    : Color.black;

                flagPropertyBlock.SetColor("_BaseColor", tinted);
                flagPropertyBlock.SetColor("_Color", tinted);
                flagPropertyBlock.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(flagPropertyBlock);
            }
        }
    }
}
