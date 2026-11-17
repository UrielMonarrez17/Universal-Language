using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Alterlife.Menu
{
    public enum FrenchScenario { None, Apartment, Cafe, Customs }

    /// <summary>Owns the demo selection. Gameplay can subscribe to ScenarioSelected.</summary>
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] GameObject globePanel;
        [SerializeField] GameObject difficultyPanel;
        [SerializeField] GlobeSelector globe;
        [SerializeField] MenuFeedback[] scenarios;
        [SerializeField] TMP_Text selectionLabel;
        [SerializeField] UnityEvent<int> scenarioSelected = new UnityEvent<int>();

        public FrenchScenario SelectedScenario { get; private set; }
        public string SelectedCountry { get; private set; }
        public bool IsChoosingDifficulty => difficultyPanel.activeSelf;
        public UnityEvent<int> ScenarioSelected => scenarioSelected;

        void Awake() => ShowWorld();

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                ShowWorld();
        }

        public void SelectFrance()
        {
            // A hidden country must never be selected through stale UI focus.
            if (!globePanel.activeSelf || !globe.FranceVisible) return;
            SelectedCountry = "France";
            SelectedScenario = FrenchScenario.None;
            globePanel.SetActive(false);
            difficultyPanel.SetActive(true);
            selectionLabel.text = string.Empty;
            foreach (var scenario in scenarios) scenario.SetSelected(false);
        }

        public void SelectScenario(int index)
        {
            if (!IsChoosingDifficulty || SelectedCountry != "France" || index < 0 || index >= scenarios.Length) return;
            SelectedScenario = (FrenchScenario)(index + 1);
            for (int i = 0; i < scenarios.Length; i++) scenarios[i].SetSelected(i == index);
            string[] names = { "LA VIE A LA MAISON", "LE CAFÉ DU QUARTIER", "LE CONTROLE DOUANIER" };
            selectionLabel.text = "France · " + names[index];
            scenarioSelected.Invoke(index);
        }

        public void ShowWorld()
        {
            SelectedCountry = null;
            SelectedScenario = FrenchScenario.None;
            difficultyPanel.SetActive(false);
            globePanel.SetActive(true);
            globe.ResetInteraction();
        }
    }
}
