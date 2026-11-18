using TMPro;
using UnityEngine;

namespace Alterlife.Immersive
{
    public sealed class BookChapter : MonoBehaviour
    {
        public ExperienceDefinition definition;
        public ExperienceNavigator navigator;
        public TMP_Text progressText;
        public GameObject selectionStamp;
        public TMP_Text selectionControlLabel;
        void OnEnable()
        {
            if (navigator) navigator.SelectionChanged += Refresh;
            Refresh();
        }
        void OnDisable() { if (navigator) navigator.SelectionChanged -= Refresh; }
        void Refresh()
        {
            if (definition && navigator && navigator.session && progressText)
                progressText.text = "Progress: " + Mathf.RoundToInt(navigator.session.GetProgress(definition).progress * 100) + "%";
            if (selectionStamp) selectionStamp.SetActive(navigator && navigator.Selected == definition);
            if (selectionControlLabel)
            {
                selectionControlLabel.enabled = true;
                selectionControlLabel.text = !navigator || !navigator.CanSelect(definition) ? "COMING SOON" : navigator.Selected == definition ? "SELECTED" : "SELECT EXPERIENCE";
            }
        }
        public void Select() => navigator.Select(definition);
    }
}
