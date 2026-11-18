using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Alterlife.Immersive
{
    public sealed class ExperienceNavigator : MonoBehaviour
    {
        public LearningSession session;
        public GameObject preparation;
        public TMP_Text preparationText;
        public TMP_Text selectionText;
        public GameObject confirmControl;
        public Renderer selectionSeal;
        public ExperienceDefinition Selected { get; private set; }
        public event System.Action SelectionChanged;
        public bool IsPreparing => preparation.activeSelf;
        public bool CanSelect(ExperienceDefinition definition) => definition && session &&
            !string.IsNullOrWhiteSpace(definition.scenePath) && Application.CanStreamedLevelBeLoaded(definition.scenePath) &&
            session.GetProgress(definition).unlocked;
        public void Select(ExperienceDefinition definition)
        {
            if (!CanSelect(definition))
            {
                ClearPreview();
                selectionText.text = (definition ? definition.displayName : "Experience") + "\nCOMING SOON\nNot included in this beta.";
                return;
            }
            if (!session.SelectExperience(definition)) return;
            Selected = definition;
            selectionText.text = "SELECTED · " + definition.displayName + "\nDifficulty: " + definition.difficulty + "/5\n" + definition.description;
            selectionSeal.enabled = true;
            confirmControl.SetActive(true);
            SelectionChanged?.Invoke();
        }
        public void ClearPreview()
        {
            Selected = null;
            selectionSeal.enabled = false;
            confirmControl.SetActive(false);
            selectionText.text = "Choose a miniature to select an experience.";
            SelectionChanged?.Invoke();
        }
        public void Confirm()
        {
            if (!CanSelect(Selected) || !session.FranceSelected) return;
            session.Save();
            if (!string.IsNullOrWhiteSpace(Selected.scenePath) && Application.CanStreamedLevelBeLoaded(Selected.scenePath))
            {
                SceneManager.LoadSceneAsync(Selected.scenePath);
                return;
            }
            preparationText.text = Selected.displayName + "\n\nCOMING SOON\n" + Selected.description +
                "\n\nDifficulty: " + Selected.difficulty + "/5\nYour selection is saved.\nThis experience is being prepared.";
            preparation.SetActive(true);
            confirmControl.SetActive(false);
        }
        public void ReturnToBook() { preparation.SetActive(false); if (Selected) confirmControl.SetActive(true); }
        public static void ReturnToHub()
        {
            var journey = FindAnyObjectByType<SceneJourney>();
            if (journey) journey.OpenBook();
            else SceneManager.LoadSceneAsync(SceneJourney.BookScene);
        }
    }
}
