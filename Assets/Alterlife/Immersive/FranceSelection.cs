using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using System.Collections;
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
        public static bool ReturnToBookRequested;
        bool travelling;
        public bool Pending { get; private set; }
        bool hovered;
        Camera viewer;
        void Start()
        {
            viewer = Camera.main;
            country.firstHoverEntered.AddListener(Enter);
            country.lastHoverExited.AddListener(Exit);
            country.selectEntered.AddListener(Select);
            session.Changed += Refresh;
            Refresh();
            if (ReturnToBookRequested)
            {
                ReturnToBookRequested = false;
                if (session.FranceSelected) StartCoroutine(TravelToBook());
            }
        }
        void LateUpdate()
        {
            // Location remains attached to France, while the flag stays legible from either controller.
            if (!viewer) viewer = Camera.main;
            if (marker && viewer)
            {
                Vector3 awayFromPlayer = marker.position - viewer.transform.position;
                if (awayFromPlayer.sqrMagnitude > .0001f) marker.rotation = Quaternion.LookRotation(awayFromPlayer, Vector3.up);
            }
        }
        void OnDestroy()
        {
            if (session) session.Changed -= Refresh;
            if (!country) return;
            country.firstHoverEntered.RemoveListener(Enter);
            country.lastHoverExited.RemoveListener(Exit);
            country.selectEntered.RemoveListener(Select);
        }
        void Enter(HoverEnterEventArgs _) { hovered = true; Refresh(); }
        void Exit(HoverExitEventArgs _) { hovered = false; Refresh(); }
        void Select(SelectEnterEventArgs _) => TargetFrance();
        public void TargetFrance()
        {
            if (travelling) return;
            Pending = true;
            if (travelOnSelection) Confirm(); else Refresh();
        }
        public void Confirm()
        {
            if (!Pending) return;
            session.SelectFrance();
            Pending = false;
            Refresh();
            if (travelOnSelection && bookArrival && teleportation && !travelling) StartCoroutine(TravelToBook());
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
            teleportation.QueueTeleportRequest(new TeleportRequest {
                destinationPosition = bookArrival.position,
                destinationRotation = bookArrival.rotation,
                matchOrientation = MatchOrientation.TargetUpAndForward });
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
            highlight.enabled = hovered || Pending || session.FranceSelected;
            label.SetActive(hovered || Pending || session.FranceSelected);
            if (markerTooltip) markerTooltip.SetActive(hovered || Pending || session.FranceSelected);
            confirmControl.SetActive(Pending && !session.FranceSelected);
            status.text = "France\nLanguage: French\nLanguage Progress: " + Mathf.RoundToInt(session.LanguageProgress * 100) + "%" +
                (session.FranceSelected ? "\nStudy unlocked — welcome in" : travelOnSelection ? "\nSelect France to visit the book" : "\nSelect France, then confirm");
            if (session.FranceSelected) door.Unlock();
        }
    }
}
