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
    /// Coordinates the Intermediate (Level 3/5) Paris café exam powered by Gemini AI:
    /// - 8 communicative milestones evaluated organically in any natural conversational order.
    /// - On-demand Fox Companion scaffolding (Press Y -> Level 1 hint -> Level 2 cards -> Close),
    ///   plus live spoken English tutor answers via Alibaba Cloud TTS.
    /// - Rich per-turn coaching feedback and a closable Final Results Modal (completion status,
    ///   0-100 grade, strengths, improvement tips, and coach summary).
    /// </summary>
    public sealed class RestaurantEvaluation : MonoBehaviour
    {
        public RestaurantLesson lesson;
        public ExperienceDefinition experience;
        public LearningSession session;
        public HummingbirdCompanion companion;
        public NPCPersonality waiterPersonality;
        public Transform menu;
        public TMP_Text speechText, feedbackText, objectiveText, resultsText;
        [Tooltip("Floating panel displaying what the player just spoke (in French or to the Fox tutor).")]
        public TMP_Text playerTranscriptText;
        public GameObject practiceControl, helpCards;
        public TMP_Text answerA, answerB, answerC;
        public RestaurantVoiceBridge voice;
        public WaiterServiceChoreographer waiterChoreographer;

        [Header("Exam Completion Results Modal")]
        public GameObject resultsModalRoot;
        public TMP_Text resultsModalText;
        public Renderer resultsModalBorder;

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
        public int FinalScore { get; private set; }
        public bool IsResultsModalOpen => resultsModalRoot != null && resultsModalRoot.activeSelf;
        public int Attempts { get; private set; }
        public string CurrentPrompt { get; private set; } = "";
        public string LastPlayerTranscript { get; private set; } = "";
        public string FinalCoachingSummary { get; private set; } = "";
        public string[] FinalStrengths { get; private set; } = Array.Empty<string>();
        public string[] FinalImprovements { get; private set; } = Array.Empty<string>();
        public RestaurantTutor Tutor => tutor;
        public event Action<string> PromptChanged;
        public event Action WaiterGreeted;
        public event Action WaiterOrderServed;
        public event Action WaiterGesturePrompted;

        static readonly Color PassedBorderColor = new Color(0.14f, 0.78f, 0.56f, 1f);
        static readonly Color RetryBorderColor = new Color(0.95f, 0.72f, 0.22f, 1f);

        readonly RestaurantTutor tutor = new RestaurantTutor();
        readonly List<string> requiredMilestoneIds = new List<string>();
        readonly HashSet<string> completedMilestones = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> level1HintedMilestones = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> level2RescuedMilestones = new HashSet<string>(StringComparer.Ordinal);

        readonly List<GameObject> hiddenWhileModalOpen = new List<GameObject>();
        MaterialPropertyBlock borderPropBlock;
        bool ready;
        bool usedLevel1ThisTurn;
        bool usedLevel2ThisTurn;
        float lastCardTapTime = -10f;
        int lastCardTapIdx = -1;

        void Awake()
        {
            if (companion != null)
                companion.HelpRequestedByButton += ToggleFoxHelp;
            if (waiterChoreographer == null)
                waiterChoreographer = FindAnyObjectByType<WaiterServiceChoreographer>();
        }

        void OnDestroy()
        {
            if (companion != null)
                companion.HelpRequestedByButton -= ToggleFoxHelp;
        }

        void Start() => Begin();

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
            FinalScore = 0;
            Attempts = 0;
            Level1HintCount = 0;
            LastPlayerTranscript = "";
            FinalCoachingSummary = "";
            FinalStrengths = Array.Empty<string>();
            FinalImprovements = Array.Empty<string>();
            usedLevel1ThisTurn = false;
            usedLevel2ThisTurn = false;
            completedMilestones.Clear();
            level1HintedMilestones.Clear();
            level2RescuedMilestones.Clear();
            ready = true;

            tutor.Reset(lesson);
            if (waiterChoreographer == null)
                waiterChoreographer = FindAnyObjectByType<WaiterServiceChoreographer>();
            waiterChoreographer?.ResetToTable();

            if (practiceControl) practiceControl.SetActive(false);
            if (helpCards) helpCards.SetActive(false);
            RestorePanelsHiddenByModal();
            if (resultsModalRoot) resultsModalRoot.SetActive(false);
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
            WaiterGreeted?.Invoke();
            SetPlayerTranscriptPlaceholder();
            RefreshJournalHeader();

            if (voice != null && !voice.IsAiReady)
                Feedback("<color=#FCA5A5><b>OFFLINE</b></color> · Add Assets/Resources/GeminiLocalKey.txt and connect to Wi-Fi.");
            else
                Feedback("<color=#FDE68A><b>READY</b></color> · Greet Lucas in French, grab the menu anytime, or press Y for Fox help.");
        }

        public void InspectMenu()
        {
            if (MenuInspected) return;
            MenuInspected = true;
            if (Stage < RestaurantStage.Complete)
                Feedback("<color=#6EE7B7><b>MENU INSPECTED</b></color> · Ask Lucas about a dish or place your order in French.");
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
                isExploringCity = voice != null && voice.IsExploringCity,
                lastWaiterFrench = tutor.LastWaiterFrench,
                lastWaiterEnglish = tutor.LastWaiterEnglish,
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
                playerTranscriptText.text = "<size=74%><color=#7DD3FC><b>LISTENING (FR)...</b></color></size>\n<color=#F1F5F9>...</color>";
        }

        public void NotifyPlayerThinking()
        {
            if (playerTranscriptText != null && Stage != RestaurantStage.Complete)
                playerTranscriptText.text = "<size=74%><color=#FDE68A><b>PROCESSING...</b></color></size>\n<color=#F1F5F9>...</color>";
        }

        public void NotifyNoiseFiltered()
        {
            if (playerTranscriptText != null && Stage != RestaurantStage.Complete)
            {
                string prev = string.IsNullOrWhiteSpace(LastPlayerTranscript)
                    ? "Speak naturally in French..."
                    : $"« {LastPlayerTranscript} »";
                playerTranscriptText.text = $"<size=74%><color=#CBD5E1><b>YOU SAID (FR)</b></color></size>\n<color=#F8FAFC>{prev}</color>";
            }
        }

        public void NotifyMicrophoneMuted(bool muted)
        {
            if (Stage == RestaurantStage.Complete) return;
            if (muted)
            {
                if (playerTranscriptText != null)
                    playerTranscriptText.text = "<size=74%><color=#FCA5A5><b>MICROPHONE MUTED</b></color></size>\n<color=#F1F5F9>Press B on right controller to unmute</color>";
                Feedback("<color=#FCA5A5><b>MIC OFF</b></color> · Press B on the right controller whenever you're ready to speak.");
            }
            else
            {
                SetPlayerTranscriptPlaceholder();
                Feedback("<color=#6EE7B7><b>MIC ON</b></color> · Speak naturally in French with Lucas, or press Y for Fox help.");
            }
        }

        public void NotifyExploringCity(bool exploring)
        {
            if (exploring)
            {
                if (helpCards != null) helpCards.SetActive(false);
                if (playerTranscriptText != null)
                    playerTranscriptText.text = "<size=74%><color=#7DD3FC><b>EXPLORING PARIS · FOX GUIDE</b></color></size>\n<color=#F1F5F9>Ask Fox anything in English, or walk back to the café</color>";
                Feedback("<color=#7DD3FC><b>FOX PARIS GUIDE</b></color> · Lucas is waiting at your table. Ask Fox questions in English anytime!");
            }
            else
            {
                SetPlayerTranscriptPlaceholder();
                Feedback("<color=#6EE7B7><b>BACK AT THE CAFÉ</b></color> · Speak naturally in French with Lucas, or ask Fox for help in English.");
            }
        }

        void SetPlayerTranscriptPlaceholder()
        {
            if (playerTranscriptText != null)
                playerTranscriptText.text = "<size=74%><color=#CBD5E1><b>YOU SAID (FR)</b></color></size>\n<color=#F1F5F9>Speak naturally in French...</color>";
        }

        void UpdatePlayerTranscriptDisplay(string transcript, bool askedFox)
        {
            if (string.IsNullOrWhiteSpace(transcript)) return;
            LastPlayerTranscript = transcript.Trim();
            if (playerTranscriptText == null) return;

            string header = askedFox
                ? "<size=74%><color=#7DD3FC><b>YOU ASKED FOX</b></color></size>"
                : "<size=74%><color=#7DD3FC><b>YOU SAID (FR)</b></color></size>";
            string formatted = askedFox ? $"\"{LastPlayerTranscript}\"" : $"« {LastPlayerTranscript} »";
            playerTranscriptText.text = $"{header}\n<color=#F8FAFC>{formatted}</color>";
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
                objectiveText.text = "<color=#6EE7B7><b>FREE CONVERSATION</b></color>\nKeep chatting naturally with Lucas in French.";
                return;
            }

            if (Stage == RestaurantStage.Complete)
            {
                objectiveText.text = PassedExam
                    ? $"<color=#6EE7B7><b>PASSED · {FinalScore}/100</b></color>\nGoals: {CompletedObjectives}/{TotalObjectives} · Autonomous: {AutonomousObjectives}/{TotalObjectives}"
                    : $"<color=#FCD34D><b>RETRY TO PASS · {FinalScore}/100</b></color>\nAutonomous: {AutonomousObjectives}/{TotalObjectives} (need {MinAutonomousToPass})";
                return;
            }

            objectiveText.text = $"<b>CAFÉ EXAM · {CompletedObjectives}/{TotalObjectives} GOALS</b>\nNatural French conversation (Level 3/5)";
        }

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

        public void ApplyGeminiTurn(RestaurantVoiceBridge.GeminiWaiterTurn turn)
        {
            if (turn == null || !ready) return;

            bool isFoxTutor = string.Equals(turn.speakerMode, "fox_tutor", StringComparison.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(turn.userTranscript))
                UpdatePlayerTranscriptDisplay(turn.userTranscript, askedFox: isFoxTutor);

            // 1. Fox Tutor Turn (English question, help request, or Paris exploration)
            if (isFoxTutor)
            {
                bool exploring = voice != null && voice.IsExploringCity;
                if (!exploring)
                {
                    usedLevel1ThisTurn = true;
                    Level1HintCount++;
                }

                string replyEn = !string.IsNullOrWhiteSpace(turn.foxTutorReplyEnglish)
                    ? turn.foxTutorReplyEnglish.Trim()
                    : tutor.BuildFallbackEnglishTutorReply(turn.userTranscript);

                tutor.UpdateFromFoxTutorTurn(replyEn, turn.level1Keywords, turn.level1TipEnglish, turn.nextFoxOptions);
                string formattedPanel = exploring
                    ? tutor.FormatCityGuidePanel(replyEn)
                    : tutor.FormatTutorAnswerPanel(replyEn);

                if (companion != null) companion.ShowHintPanel(formattedPanel);
                if (helpCards != null) helpCards.SetActive(false);

                string badge = exploring ? "<color=#7DD3FC><b>FOX GUIDE</b></color>" : "<color=#7DD3FC><b>FOX TUTOR</b></color>";
                Feedback($"{badge} · {replyEn}");
                voice?.SpeakAsFox(replyEn, isFrenchModelPhrase: false, speed: 1.0f);
                return;
            }

            // 2. Waiter Turn (Natural French conversation with Lucas Moreau)
            Attempts++;

            if (Stage == RestaurantStage.Practice)
            {
                if (!string.IsNullOrWhiteSpace(turn.detectedFood)) OrderedFood = turn.detectedFood.Trim();
                if (!string.IsNullOrWhiteSpace(turn.detectedDrink)) OrderedDrink = turn.detectedDrink.Trim();
                if (!string.IsNullOrWhiteSpace(turn.detectedDessert)) OrderedDessert = turn.detectedDessert.Trim();
                DispatchPhysicalWaiterService(turn, turn.servedOrder, turn.serveDessert || !string.IsNullOrWhiteSpace(turn.detectedDessert), turn.bringBill);

                tutor.UpdateFromWaiterTurn(turn.npcReplyFrench, turn.npcReplyEnglish, turn.level1Keywords, turn.level1TipEnglish, turn.nextFoxOptions);
                if (companion != null && companion.IsShowingAnyHelp) companion.ResetToObserving();
                if (helpCards != null) helpCards.SetActive(false);
                SetWaiterPrompt(turn.npcReplyFrench, speak: true);
                if (!string.IsNullOrWhiteSpace(turn.feedbackEnglish))
                    Feedback($"<color=#FDE68A><b>COACH TIP</b></color> · {turn.feedbackEnglish.Trim()}");
                return;
            }

            string prevDessert = OrderedDessert;
            bool wasOrderServed = OrderServed;
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

            if (turn.issuedConfirmationChallenge) ConfirmationChallengeIssued = true;
            if (turn.resolvedConfirmationChallenge) ConfirmationChallengeResolved = true;
            if (turn.servedOrder) OrderServed = true;

            bool newlyServedMain = !wasOrderServed && OrderServed;
            bool newlyOrderedDessert = turn.serveDessert ||
                (!string.IsNullOrWhiteSpace(OrderedDessert) &&
                 !string.Equals(OrderedDessert, prevDessert, StringComparison.OrdinalIgnoreCase) &&
                 OrderServed && !newlyServedMain);
            bool level2UsedThisTurn = usedLevel2ThisTurn || turn.matchedFoxOptionIndex >= 0;

            int newlyAwarded = 0;
            if (turn.newlyCompletedMilestones != null)
            {
                for (int i = 0; i < turn.newlyCompletedMilestones.Length; i++)
                {
                    string id = turn.newlyCompletedMilestones[i]?.Trim();
                    if (string.IsNullOrWhiteSpace(id) || !requiredMilestoneIds.Contains(id) || completedMilestones.Contains(id))
                        continue;

                    var line = lesson != null ? lesson.GetLineByMilestone(id) : null;
                    bool needsMenu = (line != null && line.requiresMenuInspection) || id == "read-menu";
                    if (needsMenu && !MenuInspected)
                        continue;

                    bool challengeEnabled = lesson == null || lesson.ConfirmationChallengeEnabled;
                    if (id == "confirm-order" && challengeEnabled && ConfirmationChallengeIssued && !ConfirmationChallengeResolved)
                        continue;

                    completedMilestones.Add(id);
                    if (usedLevel1ThisTurn) level1HintedMilestones.Add(id);
                    if (level2UsedThisTurn) level2RescuedMilestones.Add(id);
                    newlyAwarded++;
                }
            }

            if (helpCards != null) helpCards.SetActive(false);
            if (companion != null && companion.IsShowingAnyHelp)
                companion.ResetToObserving();

            if (newlyAwarded > 0)
            {
                usedLevel1ThisTurn = false;
                usedLevel2ThisTurn = false;
            }

            tutor.UpdateFromWaiterTurn(
                turn.npcReplyFrench,
                turn.npcReplyEnglish,
                turn.level1Keywords,
                turn.level1TipEnglish,
                turn.nextFoxOptions);

            bool billMilestoneAwarded = turn.newlyCompletedMilestones != null &&
                Array.Exists(turn.newlyCompletedMilestones, m => string.Equals(m?.Trim(), "ask-bill-farewell", StringComparison.Ordinal));
            DispatchPhysicalWaiterService(turn, newlyServedMain, newlyOrderedDessert, billMilestoneAwarded);

            if (CompletedObjectives >= TotalObjectives)
            {
                CompleteExam(turn);
                return;
            }

            Stage = OrderServed ? RestaurantStage.Confirmation :
                (!string.IsNullOrEmpty(OrderedFood) && !string.IsNullOrEmpty(OrderedDrink) ? RestaurantStage.Confirmation : RestaurantStage.Meal);

            RefreshJournalHeader();
            ShowRichTurnFeedback(turn.feedbackEnglish, newlyAwarded);

            if (newlyAwarded > 0 && companion != null && !companion.IsShowingAnyHelp)
                companion.Celebrate();

            SetWaiterPrompt(turn.npcReplyFrench, speak: true);
            EmitWaiterAnimationEvents(turn);
        }

        void ShowRichTurnFeedback(string coachFeedback, int newlyAwarded)
        {
            string body = !string.IsNullOrWhiteSpace(coachFeedback)
                ? coachFeedback.Trim()
                : (!MenuInspected && CompletedObjectives > 0
                    ? "Grab the physical 3D menu on the table to inspect dishes and unlock the menu goal."
                    : "Great effort! Keep responding to Lucas in polite French (use 'vous' and 's'il vous plaît').");

            string prefix = newlyAwarded > 0
                ? $"<color=#6EE7B7><b>+{newlyAwarded} GOAL ({CompletedObjectives}/{TotalObjectives})</b></color>"
                : "<color=#FDE68A><b>COACH TIP</b></color>";

            Feedback($"{prefix} · <color=#F8FAFC>{body}</color>");
        }

        void DispatchPhysicalWaiterService(
            RestaurantVoiceBridge.GeminiWaiterTurn turn,
            bool newlyServedMain,
            bool newlyOrderedDessert,
            bool completedBillMilestone)
        {
            if (turn == null) return;
            if (waiterChoreographer == null)
                waiterChoreographer = FindAnyObjectByType<WaiterServiceChoreographer>();
            if (waiterChoreographer == null)
                return;

            string userNorm = Normalize(turn.userTranscript);
            string npcNorm = Normalize(turn.npcReplyFrench);

            bool wantsBill = turn.bringBill || completedBillMilestone ||
                             userNorm.Contains("addition") || userNorm.Contains("payer") ||
                             npcNorm.Contains("voici l addition") || npcNorm.Contains("apporte l addition");

            bool wantsClear = turn.clearTable ||
                              userNorm.Contains("debarrasser") || userNorm.Contains("fini") ||
                              userNorm.Contains("rien d autre") || userNorm.Contains("plus rien") ||
                              userNorm.Contains("c est tout") || npcNorm.Contains("debarrasse");

            if (newlyServedMain)
                waiterChoreographer.RequestServeOrder(OrderedFood, OrderedDrink, OrderedDessert);
            else if (newlyOrderedDessert && !string.IsNullOrWhiteSpace(OrderedDessert))
                waiterChoreographer.RequestServeDessert(OrderedDessert);

            if (wantsBill)
                waiterChoreographer.RequestBringBill(OrderedFood, OrderedDrink, OrderedDessert);
            else if (wantsClear)
                waiterChoreographer.RequestClearTable(thenBringBill: false, food: OrderedFood, drink: OrderedDrink, dessert: OrderedDessert);
        }

        void EmitWaiterAnimationEvents(RestaurantVoiceBridge.GeminiWaiterTurn turn)
        {
            if (turn == null) return;
            if (turn.servedOrder)
                WaiterOrderServed?.Invoke();
            else if (turn.issuedConfirmationChallenge || HasWaiterGestureKeyword(turn.npcReplyFrench))
                WaiterGesturePrompted?.Invoke();
        }

        static bool HasWaiterGestureKeyword(string replyFrench)
        {
            if (string.IsNullOrWhiteSpace(replyFrench)) return false;
            string lower = replyFrench.ToLowerInvariant();
            return lower.Contains("carte") || lower.Contains("menu") || lower.Contains("voici")
                || lower.Contains("conseille") || lower.Contains("recommande") || lower.Contains("spécialité");
        }

        int ComputeFinalScore(RestaurantVoiceBridge.GeminiWaiterTurn finalTurn, bool passed)
        {
            float autonomousRatio = (float)AutonomousObjectives / Mathf.Max(1, TotalObjectives);
            int autonomyScore = Mathf.RoundToInt(40f + autonomousRatio * 60f);

            int blended = finalTurn != null && finalTurn.finalScore > 0
                ? Mathf.RoundToInt(Mathf.Clamp(finalTurn.finalScore, 1, 100) * 0.5f + autonomyScore * 0.5f)
                : autonomyScore;

            return passed ? Mathf.Clamp(blended, 70, 100) : Mathf.Clamp(blended, 30, 69);
        }

        static string GetGradeLabel(int score, bool passed)
        {
            if (!passed) return "RETRY RECOMMENDED";
            if (score >= 95) return "A+ · EXCELLENT";
            if (score >= 88) return "A · GREAT WORK";
            if (score >= 80) return "B+ · VERY GOOD";
            return "B · SOLID PASS";
        }

        string[] ResolveStrengths(RestaurantVoiceBridge.GeminiWaiterTurn finalTurn)
        {
            var list = FilterNonEmpty(finalTurn?.finalStrengths, 3);
            if (list.Count > 0) return list.ToArray();

            var fallback = new List<string>();
            fallback.Add($"Completed all {TotalObjectives} real-world café goals in French with Lucas.");
            if (AutonomousObjectives >= MinAutonomousToPass)
                fallback.Add($"Handled {AutonomousObjectives}/{TotalObjectives} goals independently without Level-2 phrase cards.");
            if (ConfirmationChallengeResolved)
                fallback.Add("Caught and politely corrected Lucas's order mix-up in French.");
            else if (MenuInspected)
                fallback.Add("Inspected the French café menu and ordered naturally.");
            return fallback.ToArray();
        }

        string[] ResolveImprovements(RestaurantVoiceBridge.GeminiWaiterTurn finalTurn, bool passed)
        {
            var list = FilterNonEmpty(finalTurn?.finalImprovements, 3);
            if (list.Count > 0) return list.ToArray();

            var fallback = new List<string>();
            if (!passed)
                fallback.Add($"Aim for at least {MinAutonomousToPass}/{TotalObjectives} autonomous goals (rely less on Level-2 rescue cards).");
            if (AssistedObjectives > 0)
                fallback.Add("Practice saying full polite requests ('Je voudrais..., s'il vous plaît') from memory.");
            fallback.Add("Try adding natural follow-up questions about ingredients or desserts in your next conversation.");
            return fallback.ToArray();
        }

        static List<string> FilterNonEmpty(string[] items, int max)
        {
            var result = new List<string>();
            if (items == null) return result;
            for (int i = 0; i < items.Length && result.Count < max; i++)
            {
                if (!string.IsNullOrWhiteSpace(items[i]))
                    result.Add(items[i].Trim());
            }
            return result;
        }

        string BuildOrderSummary()
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(OrderedFood)) parts.Add(OrderedFood);
            if (!string.IsNullOrWhiteSpace(OrderedDrink)) parts.Add(OrderedDrink);
            if (!string.IsNullOrWhiteSpace(OrderedDessert)) parts.Add(OrderedDessert);
            return parts.Count > 0 ? string.Join(" + ", parts) : "Café visit";
        }

        void CompleteExam(RestaurantVoiceBridge.GeminiWaiterTurn finalTurn)
        {
            Stage = RestaurantStage.Complete;
            bool passed = PassedExam;
            FinalScore = ComputeFinalScore(finalTurn, passed);
            FinalStrengths = ResolveStrengths(finalTurn);
            FinalImprovements = ResolveImprovements(finalTurn, passed);

            if (session != null && experience != null)
            {
                session.RecordExamResult(
                    experience,
                    completedMilestones,
                    AutonomousObjectives,
                    AssistedObjectives,
                    passed);
            }

            string closingFrench = !string.IsNullOrWhiteSpace(finalTurn.npcReplyFrench)
                ? finalTurn.npcReplyFrench.Trim()
                : "Merci beaucoup pour votre visite au Café de Paris ! Bonne journée et à bientôt !";
            SetWaiterPrompt(closingFrench, speak: true);
            EmitWaiterAnimationEvents(finalTurn);

            FinalCoachingSummary = !string.IsNullOrWhiteSpace(finalTurn.finalSummaryEnglish)
                ? finalTurn.finalSummaryEnglish.Trim()
                : (passed
                    ? "Great work navigating Le Café de Paris naturally in French! Keep practicing polite expressions and listening for order details."
                    : $"You finished the café conversation, using Level-2 rescue cards on {AssistedObjectives} of {TotalObjectives} goals. Try another run aiming for at least {MinAutonomousToPass} autonomous goals to pass.");

            string orderSummary = BuildOrderSummary();
            string gradeLabel = GetGradeLabel(FinalScore, passed);

            RefreshJournalHeader();

            if (resultsText != null)
            {
                if (resultsText.transform.parent != null && resultsText.transform.parent.name == "Evaluation summary")
                    resultsText.transform.parent.gameObject.SetActive(true);

                string badge = passed
                    ? "<color=#6EE7B7><b>COMPLETED · PASSED</b></color>"
                    : "<color=#FCD34D><b>COMPLETED · RETRY TO PASS</b></color>";

                resultsText.text =
                    $"{badge}  <color=#FDE68A><b>{FinalScore}/100</b></color>\n" +
                    $"<color=#E2E8F0>Goals: {CompletedObjectives}/{TotalObjectives} · Autonomous: {AutonomousObjectives}/{TotalObjectives}\n" +
                    $"Order: {orderSummary}</color>\n\n" +
                    $"<size=84%><color=#F8FAFC>{FinalCoachingSummary}</color></size>";
            }

            if (practiceControl != null)
            {
                practiceControl.SetActive(true);
                var practiceLabel = practiceControl.GetComponentInChildren<TMP_Text>();
                if (practiceLabel != null)
                    practiceLabel.text = passed ? "KEEP PRACTICING" : "RETRY EXAM";
            }

            OpenResultsModal(passed, gradeLabel, orderSummary);

            Feedback(passed
                ? $"<color=#6EE7B7><b>EXPERIENCE COMPLETED · {FinalScore}/100 ({gradeLabel})</b></color> · Review your report and select CLOSE to keep chatting with Lucas."
                : $"<color=#FCD34D><b>COMPLETED · {FinalScore}/100</b></color> · Review your feedback, select CLOSE, then try RETRY EXAM.");

            if (passed && companion != null)
                companion.Celebrate();
        }

        void OpenResultsModal(bool passed, string gradeLabel, string orderSummary)
        {
            if (resultsModalRoot == null)
                return;

            var sb = new StringBuilder(640);
            sb.AppendLine(passed
                ? "<size=122%><color=#6EE7B7><b>EXPERIENCE COMPLETED · PASSED</b></color></size>"
                : "<size=122%><color=#FCD34D><b>EXPERIENCE COMPLETED · RETRY TO PASS</b></color></size>");
            sb.AppendLine($"<size=110%><color=#FDE68A><b>GRADE: {FinalScore} / 100   ({gradeLabel})</b></color></size>");
            sb.AppendLine($"<size=88%><color=#E2E8F0>Goals: <b>{CompletedObjectives}/{TotalObjectives}</b>  ·  Autonomous: <b>{AutonomousObjectives}/{TotalObjectives}</b> (need {MinAutonomousToPass})  ·  Order: <b>{orderSummary}</b></color></size>");
            sb.AppendLine();

            sb.AppendLine("<size=90%><color=#86EFAC><b>WHAT YOU DID WELL</b></color></size>");
            for (int i = 0; i < FinalStrengths.Length; i++)
                sb.AppendLine($"<size=84%><color=#F8FAFC>• {FinalStrengths[i]}</color></size>");
            sb.AppendLine();

            sb.AppendLine("<size=90%><color=#7DD3FC><b>TIPS TO IMPROVE</b></color></size>");
            for (int i = 0; i < FinalImprovements.Length; i++)
                sb.AppendLine($"<size=84%><color=#F8FAFC>• {FinalImprovements[i]}</color></size>");
            sb.AppendLine();

            sb.AppendLine("<size=90%><color=#FDE68A><b>COACH SUMMARY</b></color></size>");
            sb.AppendLine($"<size=84%><color=#F1F5F9>{FinalCoachingSummary}</color></size>");

            if (resultsModalText != null)
                resultsModalText.text = sb.ToString().TrimEnd();

            if (resultsModalBorder != null)
            {
                if (borderPropBlock == null) borderPropBlock = new MaterialPropertyBlock();
                Color borderCol = passed ? PassedBorderColor : RetryBorderColor;
                resultsModalBorder.GetPropertyBlock(borderPropBlock);
                borderPropBlock.SetColor("_BaseColor", borderCol);
                borderPropBlock.SetColor("_Color", borderCol);
                resultsModalBorder.SetPropertyBlock(borderPropBlock);
            }

            HideOverlappingPanelsForModal();

            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up);
                if (flatFwd.sqrMagnitude < 0.001f) flatFwd = Vector3.forward;
                flatFwd.Normalize();
                resultsModalRoot.transform.position = cam.transform.position + flatFwd * 0.88f + Vector3.up * -0.02f;
                resultsModalRoot.transform.rotation = Quaternion.LookRotation(flatFwd, Vector3.up);
            }

            resultsModalRoot.SetActive(true);
        }

        void HideOverlappingPanelsForModal()
        {
            RestorePanelsHiddenByModal();
            TryHidePanelForModal(GameObject.Find("Menu instruction"));
            if (speechText != null && speechText.transform.parent != null)
                TryHidePanelForModal(speechText.transform.parent.gameObject);
            if (feedbackText != null && feedbackText.transform.parent != null)
                TryHidePanelForModal(feedbackText.transform.parent.gameObject);
            if (playerTranscriptText != null && playerTranscriptText.transform.parent != null)
                TryHidePanelForModal(playerTranscriptText.transform.parent.gameObject);
        }

        void TryHidePanelForModal(GameObject panel)
        {
            if (panel == null || !panel.activeSelf || panel == resultsModalRoot || panel == gameObject)
                return;
            if (resultsModalRoot != null && resultsModalRoot.transform.IsChildOf(panel.transform))
                return;
            panel.SetActive(false);
            hiddenWhileModalOpen.Add(panel);
        }

        void RestorePanelsHiddenByModal()
        {
            for (int i = 0; i < hiddenWhileModalOpen.Count; i++)
            {
                if (hiddenWhileModalOpen[i] != null)
                    hiddenWhileModalOpen[i].SetActive(true);
            }
            hiddenWhileModalOpen.Clear();
        }

        /// <summary>
        /// Invoked by the CLOSE button on the Exam Completion Results Modal.
        /// If passed, smoothly enters Free Conversation mode with Lucas; otherwise leaves Retry controls accessible.
        /// </summary>
        public void CloseResultsModal()
        {
            if (resultsModalRoot != null)
                resultsModalRoot.SetActive(false);
            RestorePanelsHiddenByModal();

            if (Stage == RestaurantStage.Complete && PassedExam)
                BeginPractice();
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

        public void ToggleFoxHelp()
        {
            if (!ready || Stage == RestaurantStage.Complete) return;

            if (companion == null)
            {
                if (helpCards != null && helpCards.activeSelf) helpCards.SetActive(false);
                else ShowFoxLevel2Cards();
                return;
            }

            if (companion.IsShowingLevel1) ShowFoxLevel2Cards();
            else if (companion.IsShowingHelp) HideFoxHelp();
            else ShowFoxLevel1Hint();
        }

        public void ShowFoxLevel1Hint()
        {
            if (!ready || Stage == RestaurantStage.Complete) return;
            usedLevel1ThisTurn = true;
            Level1HintCount++;

            string panelText = tutor.FormatLevel1Panel();
            if (companion != null) companion.ShowHintPanel(panelText);
            if (helpCards != null) helpCards.SetActive(false);

            Feedback($"<color=#7DD3FC><b>FOX LEVEL 1</b></color> · Lucas said: \"{tutor.LastWaiterEnglish}\" (Press Y again for 3 phrases)");

            string spokenLevel1 = tutor.BuildLevel1SpokenText();
            if (!string.IsNullOrWhiteSpace(spokenLevel1))
                voice?.SpeakAsFox(spokenLevel1, isFrenchModelPhrase: false, speed: 1.0f);
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

            Feedback("<color=#FDE68A><b>FOX LEVEL 2</b></color> · Tap any card to hear pronunciation, then say your own French reply aloud.");
        }

        public void HideFoxHelp()
        {
            if (companion != null) companion.ResetToObserving();
            if (helpCards != null) helpCards.SetActive(false);
        }

        static string FormatOption(FoxHelpOption opt) =>
            $"<b><color=#F8FAFC>{opt.french}</color></b>\n<size=70%><color=#E2E8F0>{opt.englishDescription}</color></size>";

        void SelectFoxOptionByIndex(int idx)
        {
            var options = GetCurrentContextualOptions();
            if (idx < 0 || idx >= options.Length) return;

            if (idx == lastCardTapIdx && Time.unscaledTime - lastCardTapTime < 0.15f)
                return;
            lastCardTapIdx = idx;
            lastCardTapTime = Time.unscaledTime;

            usedLevel2ThisTurn = true;
            string phrase = options[idx].french;
            voice?.SpeakAsFox(phrase, isFrenchModelPhrase: true, speed: 0.85f);

            string caption = $"<color=#F8FAFC>Listen & repeat aloud (0.85x): \"{phrase}\"</color>\n<size=78%><color=#E2E8F0>{options[idx].englishDescription}</color></size>";
            if (companion != null) companion.SetHelpCaption(caption);
            Feedback($"<color=#7DD3FC><b>PRONUNCIATION</b></color> · \"{phrase}\" — now say it (or your own variation) aloud to Lucas.");
        }

        public void UseAnswerA() => SelectFoxOptionByIndex(0);
        public void UseAnswerB() => SelectFoxOptionByIndex(1);
        public void UseAnswerC() => SelectFoxOptionByIndex(2);

        public void BeginPractice()
        {
            if (Stage != RestaurantStage.Complete) return;

            if (resultsModalRoot) resultsModalRoot.SetActive(false);

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
            Feedback("<color=#6EE7B7><b>FREE CONVERSATION</b></color> · Chat naturally with Lucas in French, or use Wrist Menu to return to the Book.");
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
