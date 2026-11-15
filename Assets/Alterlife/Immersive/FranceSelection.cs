using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Alterlife.Immersive
{
    public sealed class FranceSelection : MonoBehaviour
    {
        public LearningSession session;
        public XRSimpleInteractable country;
        public Renderer highlight;
        public GameObject label;
        public GameObject confirmControl;
        public TMP_Text status;
        public StudyDoor door;
        public bool Pending { get; private set; }
        bool hovered;
        void Start()
        {
            country.firstHoverEntered.AddListener(Enter);
            country.lastHoverExited.AddListener(Exit);
            country.selectEntered.AddListener(Select);
            session.Changed += Refresh;
            Refresh();
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
        public void TargetFrance() { Pending = true; Refresh(); }
        public void Confirm()
        {
            if (!Pending) return;
            session.SelectFrance();
            Pending = false;
            Refresh();
        }
        void Refresh()
        {
            highlight.enabled = hovered || Pending || session.FranceSelected;
            label.SetActive(hovered || Pending || session.FranceSelected);
            confirmControl.SetActive(Pending && !session.FranceSelected);
            status.text = "France\nLanguage: French\nLanguage Progress: " + Mathf.RoundToInt(session.LanguageProgress * 100) + "%" +
                (session.FranceSelected ? "\nStudy unlocked — welcome in" : "\nSelect France, then confirm");
            if (session.FranceSelected) door.Unlock();
        }
    }
}
