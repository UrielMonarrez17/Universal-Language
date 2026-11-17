using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Alterlife.Immersive
{
    public enum RestaurantStage { Welcome, ReadMenu, Recommendation, Meal, Drink, Water, Confirmation, Complete, Practice }

    /// <summary>Deterministic communicative objectives. Speech providers supply transcripts, never completion decisions.</summary>
    public sealed class RestaurantEvaluation : MonoBehaviour
    {
        public RestaurantLesson lesson;
        public ExperienceDefinition experience;
        public LearningSession session;
        public CommunicativeEncounter encounter;
        public HummingbirdCompanion companion;
        public Transform menu;
        public TMP_Text speechText, feedbackText, objectiveText, resultsText;
        public GameObject practiceControl, helpCards;
        public TMP_Text answerA, answerB;
        public RestaurantVoiceBridge voice;
        public RestaurantStage Stage { get; private set; }
        public string Dish { get; private set; }
        public string Drink { get; private set; }
        public string WaterType { get; private set; }
        public bool MenuInspected { get; private set; }
        public int CompletedObjectives { get; private set; }
        public int AssistedObjectives { get; private set; }
        public int Attempts { get; private set; }
        public string CurrentPrompt { get; private set; }
        public event Action<string> PromptChanged;
        bool assisted, ready;

        void Start() { Begin(); }
        public void Begin()
        {
            if (voice) voice.ResetConversation();
            Stage = RestaurantStage.Welcome; Dish = Drink = WaterType = "";
            MenuInspected = false; CompletedObjectives = AssistedObjectives = Attempts = 0;
            assisted = false; ready = true;
            if (practiceControl) practiceControl.SetActive(false);
            if (resultsText) resultsText.text = "";
            Present();
        }
        public void InspectMenu() { MenuInspected = true; if (Stage == RestaurantStage.ReadMenu) Feedback("Read the dishes, then tell the waiter you are ready."); }
        RestaurantLine Line => lesson.lines[Mathf.Min((int)Stage, lesson.lines.Length - 1)];
        public string Example
        {
            get
            {
                if (Stage == RestaurantStage.Confirmation && Challenge) return "Non, de l'eau " + WaterType + ", s'il vous plaît.";
                return Line.example;
            }
        }
        bool Challenge => lesson.challengeWaterConfirmation && Drink == "eau" && Stage == RestaurantStage.Confirmation;
        void Present()
        {
            if (helpCards) helpCards.SetActive(false);
            if (companion) companion.ResetToObserving();
            if (Stage == RestaurantStage.Complete)
            {
                SetPrompt("Très bien, votre commande arrive bientôt. Merci !");
                objectiveText.text = "ORDER COMPLETE";
                resultsText.text = "Conversation completed\n" + CompletedObjectives + "/6 objectives\n" + AssistedObjectives + " completed with assistance\n\n" + Dish + " + " + (Drink == "eau" ? "eau " + WaterType : Drink);
                practiceControl.SetActive(true);
                Feedback("You completed the conversation. Keep practicing or return to the book.");
                if (companion) companion.Celebrate();
                return;
            }
            if (Stage == RestaurantStage.Confirmation)
            {
                string spokenDrink = Drink == "eau" ? "une eau " + (Challenge ? (WaterType == "plate" ? "gazeuse" : "plate") : WaterType) : "un café";
                SetPrompt("Alors, " + (Dish == "salade" ? "une salade" : "un croque-monsieur") + " et " + spokenDrink + ". C'est bien ça ?");
            }
            else SetPrompt(Line.prompt);
            objectiveText.text = "YOUR GOAL\n" + Line.hint;
            Feedback("Speak in French. Different clear answers are welcome.");
        }
        void SetPrompt(string text)
        {
            CurrentPrompt = text;
            if (speechText) speechText.text = text;
            PromptChanged?.Invoke(text);
            if (voice) voice.Speak(text, Stage < RestaurantStage.Confirmation ? Line.voice : null);
        }
        public void SubmitTranscript(string transcript)
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            if (string.IsNullOrWhiteSpace(transcript)) { Feedback("I could not hear you. Try speaking again."); return; }
            if (Stage == RestaurantStage.Practice)
            {
                if (voice) voice.Practice(transcript);
                return;
            }
            string text = Normalize(transcript);
            if (Has(text, "repeter|repetez|lentement") || text.Contains("moins vite")) { Repeat(); return; }
            if (text.Contains("ne sais pas") || text.Contains("ne comprends pas")) { ShowHelp(); return; }
            Attempts++;
            bool accepted = false;
            RestaurantStage next = Stage + 1;
            switch (Stage)
            {
                case RestaurantStage.Welcome:
                    accepted = Has(text, "bonjour|bonsoir|salut") && Has(text, "merci"); break;
                case RestaurantStage.ReadMenu:
                    accepted = MenuInspected && (Has(text, "pret|prete|commander") || text.Contains("mon choix"));
                    if (!MenuInspected) { Feedback("Pick up or select the menu before ordering."); return; }
                    break;
                case RestaurantStage.Recommendation:
                    accepted = Has(text, "conseillez|recommandez|conseil|recommandation") || text.Contains("plat du jour"); break;
                case RestaurantStage.Meal:
                    bool salad = Has(text, "salade"), croque = text.Contains("croque monsieur") || Has(text, "croque");
                    accepted = salad != croque && !Has(text, "pas|sans|non");
                    if (accepted) Dish = salad ? "salade" : "croque-monsieur";
                    break;
                case RestaurantStage.Drink:
                    bool water = Has(text, "eau"), coffee = Has(text, "cafe");
                    accepted = water != coffee && !Has(text, "pas|sans|non");
                    if (accepted)
                    {
                        Drink = water ? "eau" : "café";
                        if (water && Has(text, "plate|gazeuse"))
                        {
                            if (Has(text, "plate") && Has(text, "gazeuse")) { accepted = false; break; }
                            WaterType = Has(text, "plate") ? "plate" : "gazeuse";
                            next = RestaurantStage.Confirmation;
                        }
                        else if (coffee) next = RestaurantStage.Confirmation;
                    }
                    break;
                case RestaurantStage.Water:
                    accepted = Has(text, "plate") != Has(text, "gazeuse") && !Has(text, "pas|non");
                    if (accepted) WaterType = Has(text, "plate") ? "plate" : "gazeuse";
                    break;
                case RestaurantStage.Confirmation:
                    accepted = Challenge ? Has(text, "non") && Has(text, WaterType) && !Has(text, WaterType == "plate" ? "gazeuse" : "plate")
                        : (Has(text, "oui|exactement") || text.Contains("c est ca")) && !Has(text, "non|pas");
                    break;
            }
            if (!accepted)
            {
                Feedback(Stage == RestaurantStage.Confirmation && Challenge
                    ? "Listen carefully: the waiter repeated a different water type. Correct the order."
                    : "Try again: " + Line.hint + " You can ask for a hint.");
                return;
            }
            // Asking water preference is part of the drink objective, not an extra completion.
            if (!(Stage == RestaurantStage.Drink && next == RestaurantStage.Water))
            {
                CompletedObjectives++;
                if (assisted) AssistedObjectives++;
                if (session && experience) session.RecordMilestone(experience, Line.milestoneId, assisted);
                assisted = false;
            }
            Stage = next;
            Present();
        }
        public void ShowHelp()
        {
            if (Stage >= RestaurantStage.Complete) return;
            assisted = true;
            helpCards.SetActive(true);
            answerA.text = Example;
            answerB.text = Line.alternative;
            Feedback("Assisted practice: say a phrase or select a response card. Assistance is recorded.");
            if (companion && menu) companion.ShowHintLevel1(menu, Stage == RestaurantStage.ReadMenu ? "le menu" : "s'il vous plaît");
        }
        public void UseAnswerA() { assisted = true; SubmitTranscript(Example); }
        public void UseAnswerB() { assisted = true; SubmitTranscript(Line.alternative); }
        public void Repeat()
        {
            if (Stage >= RestaurantStage.Complete) return;
            assisted = true;
            SetPrompt(CurrentPrompt);
            Feedback("Listen again. Asking for clarification is useful communication.");
        }
        public void BeginPractice()
        {
            if (Stage != RestaurantStage.Complete) return;
            if (!voice || !voice.IsConfigured) { Feedback("Conversation practice is unavailable offline. You can replay the guided conversation."); return; }
            Stage = RestaurantStage.Practice;
            objectiveText.text = "FREE CONVERSATION\nTalk about your visit to Paris. Your evaluation is already complete.";
            SetPrompt("Vous visitez Paris pour la première fois ?");
        }
        public void ReceivePracticeReply(string reply) { if (Stage == RestaurantStage.Practice) SetPrompt(reply); }
        public void Feedback(string message) { if (feedbackText) feedbackText.text = message; }
        public void ReturnToBook() => ExperienceNavigator.ReturnToHub();
        public static string Normalize(string input)
        {
            var result = new StringBuilder();
            foreach (char c in input.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                result.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
        }
        static bool Has(string value, string alternatives) => Regex.IsMatch(value, @"\b(?:" + alternatives + @")\b");
    }
}
