using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

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
        public Transform marker;
        public GameObject markerTooltip;
        public bool travelOnSelection = true;
        public SceneJourney sceneJourney;

        public bool Pending { get; private set; }

        bool travelling;
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
        }

        void CacheFlagRenderers()
        {
            flagRenderers.Clear();
            flagBaseColors.Clear();
            if (marker == null) return;

            markerBaseScale = marker.localScale;
            AddRenderersFrom(marker);

            Transform stem = marker.parent != null ? marker.parent.Find("France pin stem") : null;
            if (stem != null) AddRenderersFrom(stem);
        }

        void AddRenderersFrom(Transform root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.GetComponent<TMP_Text>() != null || flagRenderers.Contains(r))
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
            if (!PhysicalControl.IsControllerGripOnly(args.interactorObject))
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
            if (sceneJourney)
            {
                travelling = true;
                sceneJourney.OpenBook();
            }
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
                int pct = Mathf.RoundToInt(session.LanguageProgress * 100);
                status.text = $"France\nLanguage: French\nLanguage Progress: {pct}%" +
                    (selected ? "\nStudy unlocked — select France to open the book" : "\nTrigger or pinch France to visit the book");
            }
        }

        void UpdateFlagHighlight(bool activeHoverOrPending, bool selected)
        {
            if (marker != null)
                marker.localScale = markerBaseScale * (activeHoverOrPending ? 1.22f : (selected ? 1.1f : 1.0f));

            if (flagPropertyBlock == null) return;

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
                float strength = activeHoverOrPending ? 0.75f : 0.35f;
                Color emission = glow
                    ? new Color(Mathf.Max(tinted.r, 0.25f) * strength, Mathf.Max(tinted.g, 0.25f) * strength, Mathf.Max(tinted.b, 0.25f) * strength, 1f)
                    : Color.black;

                flagPropertyBlock.SetColor("_BaseColor", tinted);
                flagPropertyBlock.SetColor("_Color", tinted);
                flagPropertyBlock.SetColor("_EmissionColor", emission);
                r.SetPropertyBlock(flagPropertyBlock);
            }
        }
    }
}
