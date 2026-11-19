using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;

namespace Alterlife.Immersive
{
    public enum RestaurantStage { Welcome, ReadMenu, Recommendation, Meal, Drink, Water, Confirmation, Complete, Practice }

    /// <summary>
    /// Coordinates the non-linear Intermediate (Level 3/5) Paris café exam powered exclusively by Gemini AI:
    /// - 8 communicative milestones evaluated organically in any natural conversational order.
    /// - On-demand Fox Companion scaffolding (no silence auto-timer): Press Y -> Level 1 hint -> Level 2 cards -> Close,
    ///   plus live spoken English tutor answers via Fox's own spatial voice.
    /// - Autonomy passing threshold (>= 5/8 milestones without Level 2 rescue cards) + personalized Gemini final report.
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
        [Tooltip("Floating panel displaying what the player just spoke (in French or to the Fox tutor).")]
        public TMP_Text playerTranscriptText;
        public GameObject practiceControl, helpCards;
        public TMP_Text answerA, answerB, answerC;
        public RestaurantVoiceBridge voice;

        public RestaurantStage Stage { get; private set; }
        public string OrderedFood { get; private set; } = "";
        public string OrderedDrink { get; private set; } = "";
        public string OrderedDessert { get; private set; } = "";
        public string Dish => OrderedFood;
        public string Drink => OrderedDrink;
        public string WaterType { get; private set; } = "";
        public bool MenuInspected { get; private set; }
        public bool ConfirmationChallengeIssued { get; private set; }
        public bool ConfirmationChallengeResolved { get; private set; }
        public bool OrderServed { get; private set; }
        public int TotalObjectives => requiredMilestoneIds.Count;
        public int CompletedObjectives => completedMilestones.Count;
        public int AssistedObjectives => level2RescuedMilestones.Count;
        public int AutonomousObjectives => Mathf.Max(0, completedMilestones.Count - level2RescuedMilestones.Count);
        public int Level1HintCount { get; private set; }
        public int MinAutonomousToPass => lesson != null
            ? Mathf.Clamp(lesson.minAutonomousObjectives, 1, Mathf.Max(1, TotalObjectives))
            : Mathf.Min(5, Mathf.Max(1, TotalObjectives));
        public bool PassedExam => TotalObjectives > 0 && CompletedObjectives >= TotalObjectives && AutonomousObjectives >= MinAutonomousToPass;
        public int Attempts { get; private set; }
        public string CurrentPrompt { get; private set; } = "";
        public string LastPlayerTranscript { get; private set; } = "";
        public string FinalCoachingSummary { get; private set; } = "";
        public RestaurantTutor Tutor => tutor;
        public event Action<string> PromptChanged;

        readonly RestaurantTutor tutor = new RestaurantTutor();
        readonly List<string> requiredMilestoneIds = new List<string>();
        readonly HashSet<string> completedMilestones = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> level1HintedMilestones = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> level2RescuedMilestones = new HashSet<string>(StringComparer.Ordinal);

        bool ready;
        bool usedLevel1ThisTurn;
        bool usedLevel2ThisTurn;

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

        void RebuildRequiredMilestones()
        {
            requiredMilestoneIds.Clear();
            if (lesson != null)
            {
                var lines = lesson.GetUniqueRubricLines();
                for (int i = 0; i < lines.Count; i++)
                    requiredMilestoneIds.Add(lines[i].milestoneId);
            }

            if (requiredMilestoneIds.Count == 0 && experience != null && experience.milestoneIds != null)
            {
                for (int i = 0; i < experience.milestoneIds.Length; i++)
                    if (!string.IsNullOrWhiteSpace(experience.milestoneIds[i]))
                        requiredMilestoneIds.Add(experience.milestoneIds[i]);
            }

            if (requiredMilestoneIds.Count == 0)
            {
                foreach (var line in RestaurantLesson.CreateIntermediateParisRubric())
                    requiredMilestoneIds.Add(line.milestoneId);
            }
        }

        public bool IsMilestoneCompleted(string milestoneId) =>
            !string.IsNullOrWhiteSpace(milestoneId) && completedMilestones.Contains(milestoneId);

        public void Begin()
        {
            RebuildRequiredMilestones();
            if (voice) voice.ResetConversation();

            Stage = RestaurantStage.Welcome;
            OrderedFood = OrderedDrink = OrderedDessert = WaterType = "";
            MenuInspected = false;
            ConfirmationChallengeIssued = false;
            ConfirmationChallengeResolved = false;
            OrderServed = false;
            Attempts = 0;
            Level1HintCount = 0;
            LastPlayerTranscript = "";
            FinalCoachingSummary = "";
            usedLevel1ThisTurn = false;
            usedLevel2ThisTurn = false;
            completedMilestones.Clear();
            level1HintedMilestones.Clear();
            level2RescuedMilestones.Clear();
            ready = true;

            tutor.Reset(lesson);

            if (practiceControl) practiceControl.SetActive(false);
            if (helpCards) helpCards.SetActive(false);
            if (companion) companion.ResetToObserving();
            if (resultsText)
            {
                resultsText.text = "";
                if (resultsText.transform.parent != null && resultsText.transform.parent.name == "Evaluation summary")
                    resultsText.transform.parent.gameObject.SetActive(false);
            }

            string openingFrench = lesson != null
                ? lesson.EffectiveOpeningFrench
                : "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte.";

            voice?.SeedWaiterOpening(openingFrench);
            SetWaiterPrompt(openingFrench, speak: true);
            SetPlayerTranscriptPlaceholder();
            RefreshJournalHeader();

            if (voice != null && !voice.IsAiReady)
            {
                Feedback("Online Gemini API key required. Place your key in Assets/Resources/GeminiLocalKey.txt and connect to Wi-Fi.");
            }
            else
            {
                Feedback("Speak naturally in French with Lucas. Pick up the menu anytime, or press Y for on-demand Fox help.");
            }
        }

        public void InspectMenu()
        {
            if (MenuInspected) return;
            MenuInspected = true;
            if (Stage < RestaurantStage.Complete)
                Feedback("Menu inspected. Speak naturally in French whenever you're ready to chat or order.");
        }

        public RestaurantPromptContext BuildPromptContext()
        {
            bool cardsOpen = companion != null ? companion.IsShowingHelp : (helpCards != null && helpCards.activeSelf);
            return new RestaurantPromptContext
            {
                lesson = lesson,
                completedMilestones = completedMilestones,
                menuInspected = MenuInspected,
                orderedFood = OrderedFood,
                orderedDrink = OrderedDrink,
                orderedDessert = OrderedDessert,
                confirmationChallengeIssued = ConfirmationChallengeIssued,
                confirmationChallengeResolved = ConfirmationChallengeResolved,
                orderServed = OrderServed,
                isPracticeMode = Stage == RestaurantStage.Practice,
                foxLevel2Visible = cardsOpen,
                activeFoxOptions = GetCurrentContextualOptions(),
                autonomousObjectives = AutonomousObjectives,
                level2AssistedObjectives = AssistedObjectives,
                totalTurns = Attempts
            };
        }

        public void NotifyPlayerSpeaking()
        {
            if (playerTranscriptText != null && Stage != RestaurantStage.Complete)
                playerTranscriptText.text = "<size=72%><color=#7DD3FC>LISTENING (FR)...</color></size>\n<color=#CBD5E1>...</color>";
        }

        public void NotifyPlayerThinking()
        {
            if (playerTranscriptText != null && Stage != RestaurantStage.Complete)
                playerTranscriptText.text = "<size=72%><color=#FDE68A>PROCESSING...</color></size>\n<color=#CBD5E1>...</color>";
        }

        void SetPlayerTranscriptPlaceholder()
        {
            if (playerTranscriptText != null)
                playerTranscriptText.text = "<size=72%><color=#94A3B8>YOU SAID (FR)</color></size>\n<color=#CBD5E1>Speak naturally in French...</color>";
        }

        void UpdatePlayerTranscriptDisplay(string transcript, bool askedFox)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return;
            LastPlayerTranscript = transcript.Trim();
            if (playerTranscriptText == null) return;

            string header = askedFox
                ? "<size=72%><color=#7DD3FC>YOU ASKED FOX</color></size>"
                : "<size=72%><color=#7DD3FC>YOU SAID (FR)</color></size>";
            string formatted = askedFox ? $"\"{LastPlayerTranscript}\"" : $"« {LastPlayerTranscript} »";
            playerTranscriptText.text = $"{header}\n{formatted}";
        }

        void SetWaiterPrompt(string frenchText, bool speak)
        {
            if (string.IsNullOrWhiteSpace(frenchText)) return;
            CurrentPrompt = frenchText.Trim();
            if (speechText) speechText.text = CurrentPrompt;
            PromptChanged?.Invoke(CurrentPrompt);
            if (speak && voice != null)
                voice.SpeakAsWaiter(CurrentPrompt);
        }

        void RefreshJournalHeader()
        {
            if (!objectiveText) return;

            if (Stage == RestaurantStage.Practice)
            {
                objectiveText.text = "FREE CONVERSATION\nKeep chatting naturally with Lucas in French.";
                return;
            }

            if (Stage == RestaurantStage.Complete)
            {
                objectiveText.text = PassedExam
                    ? $"EXAM PASSED ({CompletedObjectives}/{TotalObjectives})\nAutonomous: {AutonomousObjectives}/{TotalObjectives}"
                    : $"RETRY TO PASS ({CompletedObjectives}/{TotalObjectives})\nAutonomous: {AutonomousObjectives}/{TotalObjectives} (need {MinAutonomousToPass})";
                return;
            }

            objectiveText.text = $"CAFÉ EXAM · {CompletedObjectives}/{TotalObjectives} GOALS\nNatural French conversation (Level 3/5)";
        }

        /// <summary>
        /// Entry point for text-submitted utterances (forwards exclusively to Gemini AI).
        /// </summary>
        public void SubmitTranscript(string transcript)
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            if (string.IsNullOrWhiteSpace(transcript))
            {
                Feedback("I could not hear clear speech. Please try speaking again.");
                return;
            }

            if (voice == null || !voice.IsAiReady)
            {
                Feedback("Google AI Studio (Gemini) connection is required. Add Assets/Resources/GeminiLocalKey.txt and connect to Wi-Fi.");
                return;
            }

            voice.SubmitTextToGemini(transcript);
        }

        /// <summary>
        /// Applies a structured Gemini turn — either a Fox Tutor English coaching reply or Lucas Moreau's Parisian French turn.
        /// </summary>
        public void ApplyGeminiTurn(RestaurantVoiceBridge.GeminiWaiterTurn turn)
        {
            if (turn == null || !ready) return;

            bool isFoxTutor = string.Equals(turn.speakerMode, "fox_tutor", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(turn.userTranscript))
                UpdatePlayerTranscriptDisplay(turn.userTranscript, askedFox: isFoxTutor);

            // 1. Fox Tutor Turn (Player asked a question in English or requested help)
            if (isFoxTutor)
            {
                usedLevel1ThisTurn = true;
                Level1HintCount++;

                string replyEn = !string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish)
                    ? turn.foxTutorReplyEnglish.Trim()
                    : "Try replying to Lucas in French — press Y if you'd like example phrases.";

                tutor.UpdateFromFoxTutorTurn(replyEn, turn.level1Keywords, turn.level1TipEnglish, turn.nextFoxOptions);
                string formattedPanel = tutor.FormatTutorAnswerPanel(replyEn);

                if (companion != null)
                    companion.ShowHintPanel(formattedPanel);
                else
                    Feedback(replyEn);

                if (helpCards != null) helpCards.SetActive(false);
                Feedback("Fox Tutor: " + replyEn);
                voice?.SpeakAsFox(replyEn, isFrenchModelPhrase: false);
                return;
            }

            // 2. Waiter Turn (Natural French conversation with Lucas Moreau)
            Attempts++;

            // Practice mode after exam completion
            if (Stage == RestaurantStage.Practice)
            {
                tutor.UpdateFromWaiterTurn(turn.npcReplyFrench, turn.npcReplyEnglish, turn.level1Keywords, turn.level1TipEnglish, turn.nextFoxOptions);
                if (companion != null && companion.IsShowingAnyHelp) companion.ResetToObserving();
                if (helpCards != null) helpCards.SetActive(false);
                SetWaiterPrompt(turn.npcReplyFrench, speak: true);
                if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                    Feedback(turn.feedbackEnglish);
                return;
            }

            // Track open-ended order details
            if (!string.IsNullOrWhiteSpace(turn.detectedFood))
                OrderedFood = turn.detectedFood.Trim();
            if (!string.IsNullOrWhiteSpace(turn.detectedDrink))
            {
                OrderedDrink = turn.detectedDrink.Trim();
                string normDrink = Normalize(OrderedDrink);
                if (normDrink.Contains("gazeuse")) WaterType = "gazeuse";
                else if (normDrink.Contains("plate")) WaterType = "plate";
            }
            if (!string.IsNullOrWhiteSpace(turn.detectedDessert))
                OrderedDessert = turn.detectedDessert.Trim();

            if (turn.issuedConfirmationChallenge)
                ConfirmationChallengeIssued = true;
            if (turn.resolvedConfirmationChallenge)
                ConfirmationChallengeResolved = true;
            if (turn.servedOrder)
                OrderServed = true;

            bool level2UsedThisTurn = usedLevel2ThisTurn || turn.matchedFoxOptionIndex >= 0;

            // Evaluate all newly completed milestones (non-linear & multi-milestone capable)
            int newlyAwarded = 0;
            if (turn.newlyCompletedMilestones != null)
            {
                for (int i = 0; i < turn.newlyCompletedMilestones.Length; i++)
                {
                    string id = turn.newlyCompletedMilestones[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    if (!requiredMilestoneIds.Contains(id) || completedMilestones.Contains(id)) continue;

                    // Enforce physical 3D menu inspection gate for read-menu
                    var line = lesson != null ? lesson.GetLineByMilestone(id) : null;
                    bool needsMenu = (line != null && line.requiresMenuInspection) || id == "read-menu";
                    if (needsMenu && !MenuInspected)
                        continue;

                    // Enforce dynamic order confirmation challenge gate when active
                    bool challengeEnabled = lesson == null || lesson.ConfirmationChallengeEnabled;
                    if (id == "confirm-order" && challengeEnabled && ConfirmationChallengeIssued && !ConfirmationChallengeResolved)
                        continue;

                    completedMilestones.Add(id);
                    if (usedLevel1ThisTurn) level1HintedMilestones.Add(id);
                    if (level2UsedThisTurn) level2RescuedMilestones.Add(id);
                    newlyAwarded++;
                }
            }

            // Close Fox panels cleanly once the player responds to Lucas
            if (helpCards != null) helpCards.SetActive(false);
            if (companion != null && companion.IsShowingAnyHelp)
                companion.ResetToObserving();

            if (newlyAwarded > 0)
            {
                usedLevel1ThisTurn = false;
                usedLevel2ThisTurn = false;
            }

            // Keep Tutor synced with Lucas's live French line, English translation, Level 1 hints & Level 2 cards
            tutor.UpdateFromWaiterTurn(
                turn.npcReplyFrench,
                turn.npcReplyEnglish,
                turn.level1Keywords,
                turn.level1TipEnglish,
                turn.nextFoxOptions);

            // Sync high-level stage indicator
            if (CompletedObjectives >= TotalObjectives)
            {
                CompleteExam(turn);
                return;
            }

            Stage = OrderServed ? RestaurantStage.Confirmation :
                (!string.IsNullOrEmpty(OrderedFood) && !string.IsNullOrEmpty(OrderedDrink) ? RestaurantStage.Confirmation : RestaurantStage.Meal);

            RefreshJournalHeader();

            if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                Feedback(turn.feedbackEnglish);
            else if (!MenuInspected && CompletedObjectives > 0)
                Feedback("Tip: Grab the physical menu on the table to inspect dishes and unlock the menu goal.");

            if (newlyAwarded > 0 && companion != null && !companion.IsShowingAnyHelp)
                companion.Celebrate();

            SetWaiterPrompt(turn.npcReplyFrench, speak: true);
        }

        void CompleteExam(RestaurantVoiceBridge.GeminiWaiterTurn finalTurn)
        {
            Stage = RestaurantStage.Complete;
            bool passed = PassedExam;

            if (session != null && experience != null)
            {
                session.RecordExamResult(
                    experience,
                    completedMilestones,
                    AutonomousObjectives,
                    AssistedObjectives,
                    passed);
            }

            // Preserve Lucas's authentic closing French line rather than overwriting with a hardcoded prompt
            string closingFrench = !string.IsNullOrWhiteSpace(finalTurn.npcReplyFrench)
                ? finalTurn.npcReplyFrench.Trim()
                : "Merci beaucoup pour votre visite au Café de Paris ! Bonne journée et à bientôt !";
            SetWaiterPrompt(closingFrench, speak: true);

            FinalCoachingSummary = !string.IsNullOrWhiteSpace(finalTurn.finalSummaryEnglish)
                ? finalTurn.finalSummaryEnglish.Trim()
                : (passed
                    ? "Great work navigating Le Café de Paris naturally in French! Keep practicing polite expressions and listening for subtle order details."
                    : $"You completed the full café conversation, using Level-2 rescue cards on {AssistedObjectives} of {TotalObjectives} goals. Try another run aiming for at least {MinAutonomousToPass} autonomous goals to pass.");

            RefreshJournalHeader();

            if (resultsText != null)
            {
                if (resultsText.transform.parent != null && resultsText.transform.parent.name == "Evaluation summary")
                    resultsText.transform.parent.gameObject.SetActive(true);

                string badge = passed
                    ? "<color=#86EFAC><b>EXAM PASSED · LEVEL 3/5</b></color>"
                    : "<color=#FCD34D><b>NOT PASSED YET · TRY AGAIN</b></color>";

                var orderParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(OrderedFood)) orderParts.Add(OrderedFood);
                if (!string.IsNullOrWhiteSpace(OrderedDrink)) orderParts.Add(OrderedDrink);
                if (!string.IsNullOrWhiteSpace(OrderedDessert)) orderParts.Add(OrderedDessert);
                string orderSummary = orderParts.Count > 0 ? string.Join(" + ", orderParts) : "Café visit";

                resultsText.text =
                    $"{badge}\n" +
                    $"Goals: {CompletedObjectives}/{TotalObjectives}  ·  Autonomous: {AutonomousObjectives}/{TotalObjectives} (min {MinAutonomousToPass})\n" +
                    $"Order: {orderSummary}\n\n" +
                    $"<size=82%>{FinalCoachingSummary}</size>";
            }

            if (practiceControl != null)
            {
                practiceControl.SetActive(true);
                var practiceLabel = practiceControl.GetComponentInChildren<TMP_Text>();
                if (practiceLabel != null)
                    practiceLabel.text = passed ? "KEEP PRACTICING" : "RETRY EXAM";
            }

            Feedback(passed
                ? "Exam passed! Review your AI coaching report, keep practicing with Lucas, or return to the book."
                : $"Conversation finished, but you need at least {MinAutonomousToPass}/{TotalObjectives} autonomous goals (without Level-2 cards) to pass. Select RETRY EXAM to try again.");

            if (passed && companion != null)
                companion.Celebrate();
        }

        public FoxHelpOption[] GetCurrentContextualOptions()
        {
            if (tutor.CurrentLevel2Options != null && tutor.CurrentLevel2Options.Length > 0)
                return tutor.CurrentLevel2Options;

            return new[]
            {
                new FoxHelpOption { french = "Bonjour monsieur, merci beaucoup !", englishDescription = "Polite greeting and thank you" },
                new FoxHelpOption { french = "Qu'est-ce que vous me conseillez ?", englishDescription = "Ask Lucas for a recommendation" },
                new FoxHelpOption { french = "Pouvez-vous répéter un peu plus lentement, s'il vous plaît ?", englishDescription = "Politely ask Lucas to repeat more slowly" }
            };
        }

        /// <summary>
        /// On-demand progressive Fox help cycle (triggered by Left Controller Y button or Wrist Menu):
        /// Observing -> Level 1 (English translation + key French words, non-penalizing)
        /// -> Level 2 (3 dynamic Gemini French/English phrase cards)
        /// -> Observing (close help).
        /// </summary>
        public void ToggleFoxHelp()
        {
            if (!ready || Stage == RestaurantStage.Complete)
                return;

            if (companion == null)
            {
                if (helpCards != null && helpCards.activeSelf)
                    helpCards.SetActive(false);
                else
                    ShowFoxLevel2Cards();
                return;
            }

            if (companion.IsShowingLevel1)
            {
                ShowFoxLevel2Cards();
            }
            else if (companion.IsShowingHelp)
            {
                HideFoxHelp();
            }
            else
            {
                ShowFoxLevel1Hint();
            }
        }

        public void ShowFoxLevel1Hint()
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            usedLevel1ThisTurn = true;
            Level1HintCount++;

            string panelText = tutor.FormatLevel1Panel();
            if (companion != null)
                companion.ShowHintPanel(panelText);

            if (helpCards != null) helpCards.SetActive(false);
            Feedback($"Fox Level 1 Hint · Lucas said: \"{tutor.LastWaiterEnglish}\" (Press Y again for 3 example phrases)");
        }

        public void ShowFoxLevel2Cards()
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            usedLevel2ThisTurn = true;

            FoxHelpOption[] options = GetCurrentContextualOptions();
            if (companion != null)
            {
                companion.ShowRescueOptions(options, idx => SelectFoxOptionByIndex(idx));
                companion.SetHelpCaption(tutor.FormatLevel2Header());
            }
            else if (helpCards != null)
            {
                helpCards.SetActive(true);
            }

            if (answerA && options.Length > 0) answerA.text = FormatOption(options[0]);
            if (answerB && options.Length > 1) answerB.text = FormatOption(options[1]);
            if (answerC && options.Length > 2) answerC.text = FormatOption(options[2]);

            Feedback("Fox Level 2 · Tap any card to hear pronunciation, then say your own French reply aloud.");
        }

        public void HideFoxHelp()
        {
            if (companion != null) companion.ResetToObserving();
            if (helpCards != null) helpCards.SetActive(false);
        }

        // Backwards-compatible entry point (opens Level 1 hint first when invoked externally)
        public void ShowHelp() => ShowFoxLevel1Hint();

        static string FormatOption(FoxHelpOption opt) =>
            $"<b><color=#F8FAFC>{opt.french}</color></b>\n<size=68%><color=#94A3B8>{opt.englishDescription}</color></size>";

        void SelectFoxOptionByIndex(int idx)
        {
            var options = GetCurrentContextualOptions();
            if (idx < 0 || idx >= options.Length) return;

            usedLevel2ThisTurn = true;
            string phrase = options[idx].french;
            voice?.SpeakAsFox(phrase, isFrenchModelPhrase: true);

            string caption = $"Listen & repeat aloud: \"{phrase}\"\n<size=78%><color=#CBD5E1>{options[idx].englishDescription}</color></size>";
            if (companion != null) companion.SetHelpCaption(caption);
            Feedback($"Fox pronunciation: \"{phrase}\" — now say it (or your own variation) aloud to Lucas.");
        }

        public void UseAnswerA() => SelectFoxOptionByIndex(0);
        public void UseAnswerB() => SelectFoxOptionByIndex(1);
        public void UseAnswerC() => SelectFoxOptionByIndex(2);

        public void Repeat()
        {
            if (Stage >= RestaurantStage.Complete || string.IsNullOrWhiteSpace(CurrentPrompt)) return;
            voice?.SpeakAsWaiter(CurrentPrompt);
            Feedback("Listening to Lucas again...");
        }

        public void BeginPractice()
        {
            if (Stage != RestaurantStage.Complete) return;

            if (!PassedExam)
            {
                Begin();
                return;
            }

            Stage = RestaurantStage.Practice;
            if (practiceControl) practiceControl.SetActive(false);
            RefreshJournalHeader();

            string opener = "Vous visitez Paris pour la première fois ? Qu'est-ce qui vous plaît le plus ici ?";
            tutor.UpdateFromWaiterTurn(
                opener,
                "Is this your first time visiting Paris? What do you like most here?",
                new[] { "Première fois — First time", "J'adore — I love", "La nourriture — The food" },
                "Chat freely with Lucas about your trip to Paris!",
                new[]
                {
                    new FoxHelpOption { french = "Oui, c'est ma première fois à Paris et j'adore l'architecture !", englishDescription = "Say it's your first visit and you love the architecture" },
                    new FoxHelpOption { french = "Je suis déjà venu, j'aime beaucoup les cafés parisiens.", englishDescription = "Say you've visited before and love Parisian cafés" },
                    new FoxHelpOption { french = "J'adore la cuisine française et cette vue sur la tour Eiffel !", englishDescription = "Praise French food and the view of the Eiffel Tower" }
                });

            SetWaiterPrompt(opener, speak: true);
            Feedback("Free conversation mode! Chat naturally with Lucas in French.");
        }

        public void ReceivePracticeReply(string reply)
        {
            if (Stage == RestaurantStage.Practice)
                SetWaiterPrompt(reply, speak: true);
        }

        public void Feedback(string message)
        {
            if (feedbackText) feedbackText.text = message;
        }

        public void ReturnToBook() => ExperienceNavigator.ReturnToHub();

        public static string Normalize(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var result = new StringBuilder();
            foreach (char c in input.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                result.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return Regex.Replace(result.ToString(), @"\s+", " ").Trim();
        }
    }
}
