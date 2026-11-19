using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Alterlife.Immersive
{
    public enum RestaurantStage { Welcome, ReadMenu, Recommendation, Meal, Drink, Water, Confirmation, Complete, Practice }

    /// <summary>
    /// Coordinates Paris restaurant communicative objectives, Gemini AI conversational replies,
    /// deterministic validation fallback, and Fox companion 3-option bilingual rescue (voice or Trigger/Pinch).
    /// </summary>
    public sealed class RestaurantEvaluation : MonoBehaviour
    {
        public RestaurantLesson lesson;
        public ExperienceDefinition experience;
        public LearningSession session;
        public CommunicativeEncounter encounter;
        public HummingbirdCompanion companion;
        public NPCPersonality waiterPersonality;
        public Transform menu;
        public TMP_Text speechText, feedbackText, objectiveText, resultsText;
        public GameObject practiceControl, helpCards;
        public TMP_Text answerA, answerB, answerC;
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
        FoxHelpOption[] dynamicFoxOptions;

        void Awake()
        {
            if (companion != null)
                companion.HelpRequestedByButton += ToggleFoxHelp;
        }

        void OnDestroy()
        {
            if (companion != null)
                companion.HelpRequestedByButton -= ToggleFoxHelp;
        }

        void Start() { Begin(); }

        public void Begin()
        {
            if (voice) voice.ResetConversation();
            helpWait = 0;
            Stage = RestaurantStage.Welcome;
            Dish = Drink = WaterType = "";
            MenuInspected = false;
            CompletedObjectives = AssistedObjectives = Attempts = 0;
            assisted = false;
            ready = true;
            dynamicFoxOptions = null;

            if (practiceControl) practiceControl.SetActive(false);
            if (resultsText) resultsText.text = "";
            Present();
        }

        public void InspectMenu()
        {
            MenuInspected = true;
            if (voice) voice.NotifyUserActivity();
            if (Stage == RestaurantStage.ReadMenu)
                Feedback("Read the dishes, then tell the waiter you are ready.");
        }

        RestaurantLine Line => lesson.lines[Mathf.Min((int)Stage, lesson.lines.Length - 1)];

        public string Example
        {
            get
            {
                if (Stage == RestaurantStage.Confirmation && Challenge)
                    return "Non, de l'eau " + WaterType + ", s'il vous plaît.";
                return Line.example;
            }
        }

        bool Challenge => lesson != null && lesson.challengeWaterConfirmation && Drink == "eau" && Stage == RestaurantStage.Confirmation;

        void Present()
        {
            helpWait = 0;
            if (helpCards) helpCards.SetActive(false);
            if (companion) companion.ResetToObserving();
            if (voice) voice.NotifyUserActivity();

            if (Stage == RestaurantStage.Complete)
            {
                SetPrompt("Très bien, votre commande arrive bientôt. Merci !");
                if (objectiveText) objectiveText.text = "ORDER COMPLETE";
                if (resultsText)
                {
                    resultsText.text = "Conversation completed\n" + CompletedObjectives + "/6 objectives\n" +
                        AssistedObjectives + " completed with assistance\n\n" + Dish + " + " + (Drink == "eau" ? "eau " + WaterType : Drink);
                }
                if (practiceControl)
                {
                    practiceControl.SetActive(true);
                    var practiceLabel = practiceControl.GetComponentInChildren<TMP_Text>();
                    if (practiceLabel) practiceLabel.text = voice && voice.IsAiReady ? "KEEP PRACTICING" : "PRACTICE AGAIN";
                }
                Feedback("You completed the conversation. Keep practicing or use your wrist menu to return.");
                if (companion) companion.Celebrate();
                return;
            }

            if (Stage == RestaurantStage.Confirmation)
            {
                string spokenDrink = Drink == "eau"
                    ? "une eau " + (Challenge ? (WaterType == "plate" ? "gazeuse" : "plate") : WaterType)
                    : "un café";
                SetPrompt("Alors, " + (Dish == "salade" ? "une salade" : "un croque-monsieur") + " et " + spokenDrink + ". C'est bien ça ?");
            }
            else
            {
                SetPrompt(Line.prompt);
            }

            if (objectiveText) objectiveText.text = "YOUR GOAL\n" + Line.hint;
            Feedback("Speak naturally in French. Press Y anytime for Fox help.");
        }

        string lastWaiterLine, translatedLine, translatedText;
        string WaiterSubtitle => lastWaiterLine == translatedLine && !string.IsNullOrEmpty(translatedText)
            ? translatedText : RestaurantTutor.Translation(lastWaiterLine);

        void SayAside(string text)
        {
            lastWaiterLine = text;
            if (speechText) speechText.text = text;
            voice?.Speak(text);
        }

        void SetPrompt(string text)
        {
            lastWaiterLine = CurrentPrompt = text;
            if (speechText) speechText.text = text;
            PromptChanged?.Invoke(text);
            if (voice) voice.Speak(text, Stage < RestaurantStage.Confirmation && lesson != null && text == Line.prompt ? Line.voice : null);
        }

        public void SubmitTranscript(string transcript)
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Feedback("I could not hear you. Try speaking again.");
                return;
            }

            if (voice) voice.NotifyUserActivity();

            if (HandleTutorQuestion(transcript)) return;

            if (Stage == RestaurantStage.Practice)
            {
                if (voice) voice.Practice(transcript);
                return;
            }

            string text = Normalize(transcript);
            if (Has(text, "repeter|repetez|lentement") || text.Contains("moins vite")) { Repeat(); return; }
            if (text.Contains("ne sais pas") || text.Contains("ne comprends pas")) { ShowHelp(); return; }

            if (text.Contains("combien") || Has(text, "prix|coute"))
            {
                SayAside("La salade coûte dix euros, le croque-monsieur douze euros et le café trois euros.");
                return;
            }
            if (Has(text, "contient|ingredients") || text.Contains("il y a quoi"))
            {
                SayAside("Le croque-monsieur contient du jambon et du fromage.");
                return;
            }
            Attempts++;
            bool accepted = false;
            RestaurantStage next = Stage + 1;

            switch (Stage)
            {
                case RestaurantStage.Welcome:
                    accepted = Has(text, "bonjour|bonsoir|salut") && Has(text, "merci");
                    break;
                case RestaurantStage.ReadMenu:
                    accepted = MenuInspected && (Has(text, "pret|prete|commander") || text.Contains("mon choix"));
                    if (!MenuInspected) { Feedback("Pick up or select the menu before ordering."); return; }
                    break;
                case RestaurantStage.Recommendation:
                    accepted = Has(text, "conseillez|recommandez|conseil|recommandation") || text.Contains("plat du jour");
                    break;
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
                        else if (coffee)
                        {
                            next = RestaurantStage.Confirmation;
                        }
                    }
                    break;
                case RestaurantStage.Water:
                    accepted = Has(text, "plate") != Has(text, "gazeuse") && !Has(text, "pas|non");
                    if (accepted) WaterType = Has(text, "plate") ? "plate" : "gazeuse";
                    break;
                case RestaurantStage.Confirmation:
                    accepted = Challenge
                        ? Has(text, "non") && Has(text, WaterType) && !Has(text, WaterType == "plate" ? "gazeuse" : "plate")
                        : (Has(text, "oui|exactement") || text.Contains("c est ca")) && !Has(text, "non|pas");
                    break;
            }

            if (!accepted)
            {
                SayAside("Pardon, pouvez-vous reformuler ?");
                Feedback(Stage == RestaurantStage.Confirmation && Challenge
                    ? "Listen carefully: the waiter repeated a different water type. Correct the order."
                    : "Try again: " + Line.hint + " (Press Y for Fox help).");
                return;
            }

            RecordStageAdvance(next);
            dynamicFoxOptions = null;
            Stage = next;
            Present();
        }

        void RecordStageAdvance(RestaurantStage next)
        {
            if (!(Stage == RestaurantStage.Drink && next == RestaurantStage.Water))
            {
                CompletedObjectives++;
                if (assisted) AssistedObjectives++;
                if (session && experience && lesson != null)
                    session.RecordMilestone(experience, Line.milestoneId, assisted);
                assisted = false;
            }
        }

        public void ApplyGeminiTurn(RestaurantVoiceBridge.GeminiWaiterTurn turn, bool usedCardSelection)
        {
            if (turn == null || !ready || HandleTutorQuestion(turn.userTranscript)) return;

            translatedLine = turn.npcReplyFrench; translatedText = turn.npcReplyEnglish;
            if (voice) voice.NotifyUserActivity();

            // If Gemini recognized that the user read one of the active Fox options aloud, handle celebration & assistance.
            if (turn.matchedFoxOptionIndex >= 0 && companion != null && companion.IsShowingHelp)
            {
                assisted = true;
                companion.ResetToObserving();
                companion.Celebrate();
            }
            else if (usedCardSelection)
            {
                assisted = true;
            }

            if (!string.IsNullOrWhiteSpace(turn.detectedDish))
                Dish = turn.detectedDish == "salade" ? "salade" : "croque-monsieur";

            if (!string.IsNullOrWhiteSpace(turn.detectedDrink))
                Drink = turn.detectedDrink == "eau" ? "eau" : "café";

            if (!string.IsNullOrWhiteSpace(turn.detectedWaterType))
                WaterType = turn.detectedWaterType == "gazeuse" ? "gazeuse" : "plate";

            if (turn.nextFoxOptions != null && turn.nextFoxOptions.Length > 0)
                dynamicFoxOptions = turn.nextFoxOptions;

            if (Stage == RestaurantStage.Practice)
            {
                SetPrompt(turn.npcReplyFrench);
                if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                    Feedback(turn.feedbackEnglish);
                return;
            }

            if (turn.advanceObjective)
            {
                RestaurantStage next = Stage + 1;
                if (Stage == RestaurantStage.Drink)
                {
                    if (Drink == "café") next = RestaurantStage.Confirmation;
                    else if (Drink == "eau" && !string.IsNullOrEmpty(WaterType)) next = RestaurantStage.Confirmation;
                }

                if (Stage == RestaurantStage.ReadMenu && !MenuInspected)
                {
                    Feedback("Pick up or select the menu on the table before ordering.");
                    SetPrompt(turn.npcReplyFrench);
                    return;
                }

                RecordStageAdvance(next);
                Stage = next;

                if (Stage == RestaurantStage.Complete)
                {
                    Present();
                    return;
                }
            }

            if (helpCards) helpCards.SetActive(false);
            if (companion && companion.IsShowingHelp) companion.ResetToObserving();

            if (objectiveText && lesson != null)
                objectiveText.text = "YOUR GOAL\n" + Line.hint;

            if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                Feedback(turn.feedbackEnglish);

            // Use Lucas's authentic Gemini response as his spoken line & dialogue bubble.
            SetPrompt(turn.npcReplyFrench);
        }

        public FoxHelpOption[] GetCurrentContextualOptions()
        {
            if (dynamicFoxOptions != null && dynamicFoxOptions.Length >= 3)
                return dynamicFoxOptions;

            switch (Stage)
            {
                case RestaurantStage.Welcome:
                    return new[]
                    {
                        new FoxHelpOption { french = "Bonjour, merci beaucoup !", englishDescription = "Polite greeting & thank you (recommended)" },
                        new FoxHelpOption { french = "Bonjour et merci !", englishDescription = "Short warm greeting & thanks" },
                        new FoxHelpOption { french = "Salut, merci pour le menu !", englishDescription = "Friendly greeting thanking for the menu" }
                    };
                case RestaurantStage.ReadMenu:
                    return new[]
                    {
                        new FoxHelpOption { french = "Je suis prêt à commander.", englishDescription = "Tell the waiter you're ready to order (m.)" },
                        new FoxHelpOption { french = "Je suis prête à commander.", englishDescription = "Tell the waiter you're ready to order (f.)" },
                        new FoxHelpOption { french = "J'ai fait mon choix, merci.", englishDescription = "Say you have chosen your meal" }
                    };
                case RestaurantStage.Recommendation:
                    return new[]
                    {
                        new FoxHelpOption { french = "Qu'est-ce que vous me conseillez ?", englishDescription = "Ask what the waiter advises for you" },
                        new FoxHelpOption { french = "Qu'est-ce que vous recommandez ?", englishDescription = "Ask what he recommends on the menu" },
                        new FoxHelpOption { french = "Quel est le plat du jour ?", englishDescription = "Ask what today's special dish is" }
                    };
                case RestaurantStage.Meal:
                    return new[]
                    {
                        new FoxHelpOption { french = "Je voudrais un croque-monsieur, s'il vous plaît.", englishDescription = "Order the toasted ham & cheese sandwich (12 €)" },
                        new FoxHelpOption { french = "Une salade, s'il vous plaît.", englishDescription = "Order the fresh tomato & cheese salad (10 €)" },
                        new FoxHelpOption { french = "Je vais prendre un croque-monsieur.", englishDescription = "Polite alternative: 'I will take a croque-monsieur'" }
                    };
                case RestaurantStage.Drink:
                    return new[]
                    {
                        new FoxHelpOption { french = "De l'eau, s'il vous plaît.", englishDescription = "Order water (waiter will ask still or sparkling)" },
                        new FoxHelpOption { french = "Un café, s'il vous plaît.", englishDescription = "Order a classic French espresso/coffee (3 €)" },
                        new FoxHelpOption { french = "De l'eau plate, s'il vous plaît.", englishDescription = "Order still water directly (skips extra question)" }
                    };
                case RestaurantStage.Water:
                    return new[]
                    {
                        new FoxHelpOption { french = "Plate, s'il vous plaît.", englishDescription = "Choose still (non-sparkling) water" },
                        new FoxHelpOption { french = "Gazeuse, s'il vous plaît.", englishDescription = "Choose sparkling (carbonated) water" },
                        new FoxHelpOption { french = "De l'eau plate, merci.", englishDescription = "Polite full phrase for still water" }
                    };
                case RestaurantStage.Confirmation:
                    if (Challenge)
                    {
                        string expected = string.IsNullOrEmpty(WaterType) ? "plate" : WaterType;
                        string wrong = expected == "plate" ? "gazeuse" : "plate";
                        return new[]
                        {
                            new FoxHelpOption { french = $"Non, de l'eau {expected}, s'il vous plaît.", englishDescription = $"Politely correct the waiter: you asked for {expected} water" },
                            new FoxHelpOption { french = $"Pardon, je préfère de l'eau {expected}.", englishDescription = $"Gentle correction: 'Excuse me, I prefer {expected} water'" },
                            new FoxHelpOption { french = $"Pas {wrong}, de l'eau {expected} !", englishDescription = $"Direct clarification: 'Not {wrong}, {expected} water!'" }
                        };
                    }
                    return new[]
                    {
                        new FoxHelpOption { french = "Oui, c'est ça. Merci !", englishDescription = "Confirm that your order is right" },
                        new FoxHelpOption { french = "Oui, exactement, merci.", englishDescription = "Enthusiastic confirmation: 'Yes, exactly'" },
                        new FoxHelpOption { french = "Pouvez-vous répéter, s'il vous plaît ?", englishDescription = "Ask the waiter to repeat the order slowly" }
                    };
                default:
                    return new[]
                    {
                        new FoxHelpOption { french = "Oui, c'est ma première fois à Paris !", englishDescription = "Say it's your first time visiting Paris" },
                        new FoxHelpOption { french = "J'aime beaucoup ce quartier.", englishDescription = "Say you really like this neighborhood" },
                        new FoxHelpOption { french = "L'addition, s'il vous plaît.", englishDescription = "Politely ask for the check" }
                    };
            }
        }

        float helpWait;
        void Update()
        {
            if (!ready || Stage >= RestaurantStage.Complete || (companion && companion.IsShowingHelp) ||
                (voice && voice.speaker && voice.speaker.isPlaying)) return;
            helpWait += Time.unscaledDeltaTime;
            if (helpWait >= (voice ? voice.HelpDelay : 8f)) OnProlongedUserPause();
        }

        bool HandleTutorQuestion(string words)
        {
            if (string.IsNullOrWhiteSpace(words) || !RestaurantTutor.IsEnglishQuestion(words) &&
                !System.Text.RegularExpressions.Regex.IsMatch(Normalize(words), @"(veut dire|signifie|aidez|comment dit|besoin d aide|comprends pas)")) return false;
            ShowHelp();
            var meaning = RestaurantTutor.Definition(words);
            TutorCaption("Waiter: " + WaiterSubtitle +
                (meaning == null ? "\nTap a phrase to hear it." : "\n" + meaning));
            return true;
        }

        void TutorCaption(string message)
        {
            Feedback(message);
            if (companion) companion.SetHelpCaption(message);
        }

        public void OnProlongedUserPause()
        {
            if (!ready || Stage >= RestaurantStage.Complete)
                return;

            helpWait = 0;
            if (companion != null && !companion.IsShowingHelp)
                ShowHelp();
        }

        public void ToggleFoxHelp()
        {
            helpWait = 0;
            if (companion != null && companion.IsShowingHelp)
            {
                companion.ResetToObserving();
                if (helpCards) helpCards.SetActive(false);
                if (voice) voice.NotifyUserActivity();
                return;
            }
            ShowHelp();
        }

        public void ShowHelp()
        {
            if (Stage >= RestaurantStage.Complete) return;
            assisted = true;

            FoxHelpOption[] options = GetCurrentContextualOptions();

            if (companion != null)
            {
                companion.ShowRescueOptions(options, idx => SelectFoxOptionByIndex(idx, spokenAloud: false));
            }
            else if (helpCards != null)
            {
                helpCards.SetActive(true);
            }

            if (answerA && options.Length > 0) answerA.text = FormatOption(options[0]);
            if (answerB && options.Length > 1) answerB.text = FormatOption(options[1]);
            if (answerC && options.Length > 2) answerC.text = FormatOption(options[2]);

            TutorCaption("Waiter: " + WaiterSubtitle + "\nTap a phrase to hear it. Then try it yourself.");
        }

        static string FormatOption(FoxHelpOption opt) =>
            $"<b><color=#F8FAFC>{opt.french}</color></b>\n<size=68%><color=#94A3B8>{opt.englishDescription}</color></size>";

        void SelectFoxOptionByIndex(int idx, bool spokenAloud)
        {
            var options = GetCurrentContextualOptions();
            if (idx < 0 || idx >= options.Length) return;
            assisted = true;
            string phrase = options[idx].french;
            voice?.Speak(phrase);
            TutorCaption("Try saying: " + phrase + "\n" + options[idx].englishDescription);
        }

        public void UseAnswerA() => SelectFoxOptionByIndex(0, false);
        public void UseAnswerB() => SelectFoxOptionByIndex(1, false);
        public void UseAnswerC() => SelectFoxOptionByIndex(2, false);

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
            if (!voice || !voice.IsAiReady)
            {
                Begin();
                Feedback("Guided practice restarted. Press Y for Fox help cards.");
                return;
            }

            Stage = RestaurantStage.Practice;
            if (objectiveText) objectiveText.text = "FREE CONVERSATION\nTalk about your visit to Paris. Your evaluation is already complete.";
            SetPrompt("Vous visitez Paris pour la première fois ?");
        }

        public void ReceivePracticeReply(string reply)
        {
            if (Stage == RestaurantStage.Practice)
                SetPrompt(reply);
        }

        public void Feedback(string message)
        {
            if (feedbackText) feedbackText.text = message;
        }

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
