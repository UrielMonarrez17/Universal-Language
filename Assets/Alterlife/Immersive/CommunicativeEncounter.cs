using System;
using UnityEngine;
using TMPro;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Coordinates the Krashen i+1 communicative loop in a scenario.
    /// Handles NPC dialogue prompts, silence timers, Meta Voice SDK listening,
    /// progressive Hummingbird scaffolding, and progress recording into LearningSession.
    /// </summary>
    public sealed class CommunicativeEncounter : MonoBehaviour
    {
        [Header("Multi-step restaurant override")]
        public RestaurantEvaluation restaurantEvaluation;
        [Header("Academic & Pedagogical Context")]
        [SerializeField] private ExperienceDefinition experience;
        [SerializeField] private LearningSession session;
        [SerializeField] private HummingbirdCompanion companion;
        [SerializeField] private string targetMilestoneId = "understand-household-request";
        [Tooltip("If true, completing this pilot encounter marks all milestones of the experience complete (0% -> 33% France).")]
        [SerializeField] private bool completeExperienceOnSuccess = true;

        [Header("NPC Presentation")]
        [SerializeField] private Transform npcTransform;
        [SerializeField] private TextMeshPro npcSpeechBubble;
        [SerializeField] private AudioSource npcAudioSource;
        [SerializeField] private AudioClip npcInitialPromptClip;
        [SerializeField] private AudioClip npcGentlePardonClip;
        [SerializeField] private AudioClip npcSuccessReactionClip;

        [Header("Scenario Target Object (Scaffold Level 1)")]
        [SerializeField] private Transform keyInteractiveProp; // e.g. Taza de café
        [SerializeField] private string vocabWordFrench = "la tasse de café";

        [Header("Dialogue Content (Scaffold Level 2)")]
        [SerializeField] private string promptFrench = "Bonjour ! Que veux-tu pour le petit déjeuner ?";
        [SerializeField] private string optionAPhrase = "Un café, s'il vous plaît";
        [SerializeField] private string optionBPhrase = "Non, merci";
        [SerializeField] private string expectedSpokenKeyword = "café";

        [Header("Timing (VR Low Affective Filter)")]
        [Tooltip("Seconds of silence before Level 1 Hint is triggered.")]
        [SerializeField] private float silenceThreshold = 5.0f;
        [Tooltip("Additional seconds or failed attempts before Level 2 Rescue is triggered.")]
        [SerializeField] private float rescueThreshold = 5.0f;

        // Runtime State
        private bool isEncounterActive;
        private bool encounterCompleted;
        private bool usedAssistance;
        private float encounterTimer;
        private int failedVoiceAttempts;
        private int currentScaffoldLevel; // 0 = listening, 1 = hint 1, 2 = rescue 2

        public event Action<bool> EncounterFinished;

        private void Start()
        {
            if (restaurantEvaluation != null) return;
            if (session == null) session = FindAnyObjectByType<LearningSession>();
            if (companion == null) companion = FindAnyObjectByType<HummingbirdCompanion>();

            BeginEncounter();
        }

        public void BeginEncounter()
        {
            if (restaurantEvaluation != null) { restaurantEvaluation.Begin(); return; }
            isEncounterActive = true;
            encounterCompleted = false;
            usedAssistance = false;
            encounterTimer = 0f;
            failedVoiceAttempts = 0;
            currentScaffoldLevel = 0;

            if (npcSpeechBubble != null)
            {
                npcSpeechBubble.gameObject.SetActive(true);
                npcSpeechBubble.text = promptFrench;
            }

            if (npcAudioSource != null && npcInitialPromptClip != null)
            {
                npcAudioSource.PlayOneShot(npcInitialPromptClip);
            }

            if (companion != null)
            {
                companion.ResetToObserving();
            }
        }

        private void Update()
        {
            if (!isEncounterActive || encounterCompleted) return;

            encounterTimer += Time.deltaTime;

            // Step 1: Trigger Level 1 Hint after silence
            if (currentScaffoldLevel == 0 && encounterTimer >= silenceThreshold)
            {
                TriggerHintLevel1();
            }
            // Step 2: Trigger Level 2 Rescue after continued silence
            else if (currentScaffoldLevel == 1 && encounterTimer >= (silenceThreshold + rescueThreshold))
            {
                TriggerRescueLevel2();
            }
        }

        private void TriggerHintLevel1()
        {
            currentScaffoldLevel = 1;
            usedAssistance = true;

            if (companion != null && keyInteractiveProp != null)
            {
                companion.ShowHintLevel1(keyInteractiveProp, vocabWordFrench);
            }
        }

        private void TriggerRescueLevel2()
        {
            currentScaffoldLevel = 2;
            usedAssistance = true;

            // NPC expresses gentle comprehension ("Pardon ?") with no punitive score
            if (npcSpeechBubble != null)
            {
                npcSpeechBubble.text = "Pardon ?";
            }

            if (npcAudioSource != null && npcGentlePardonClip != null)
            {
                npcAudioSource.PlayOneShot(npcGentlePardonClip);
            }

            if (companion != null)
            {
                companion.ShowRescueLevel2(
                    optionAPhrase,
                    OnOptionASelected,
                    optionBPhrase,
                    OnOptionBSelected
                );
            }
        }

        /// <summary>
        /// Hook for Meta Voice SDK / Wit.ai transcription callback.
        /// Call this method from AppVoiceExperience.VoiceEvents.OnFullTranscription(string text).
        /// </summary>
        public void OnVoiceTranscriptionReceived(string transcription)
        {
            if (restaurantEvaluation != null) { restaurantEvaluation.SubmitTranscript(transcription); return; }
            if (string.IsNullOrWhiteSpace(transcription)) return;
            if (!isEncounterActive || encounterCompleted) return;

            string cleaned = transcription.ToLowerInvariant();
            if (cleaned.Contains(expectedSpokenKeyword.ToLowerInvariant()) ||
                cleaned.Contains("cafe") ||
                cleaned.Contains("s'il vous plait") ||
                cleaned.Contains("plaît"))
            {
                CompleteEncounter(optionAPhrase);
            }
            else
            {
                failedVoiceAttempts++;
                if (failedVoiceAttempts >= 2 && currentScaffoldLevel < 2)
                {
                    TriggerRescueLevel2();
                }
            }
        }

        private void OnOptionASelected()
        {
            CompleteEncounter(optionAPhrase);
        }

        private void OnOptionBSelected()
        {
            CompleteEncounter(optionBPhrase);
        }

        private void CompleteEncounter(string chosenResponse)
        {
            if (encounterCompleted) return;
            encounterCompleted = true;
            isEncounterActive = false;

            // Positive feedback & Krashen reinforcement
            if (npcSpeechBubble != null)
            {
                npcSpeechBubble.text = "Très bien ! Voici votre café.";
            }

            if (npcAudioSource != null && npcSuccessReactionClip != null)
            {
                npcAudioSource.PlayOneShot(npcSuccessReactionClip);
            }

            // Record progress in LearningSession
            if (session != null && experience != null)
            {
                if (completeExperienceOnSuccess && experience.milestoneIds != null && experience.milestoneIds.Length > 0)
                {
                    bool loggedAssist = false;
                    foreach (string milestone in experience.milestoneIds)
                    {
                        session.RecordMilestone(experience, milestone, usedAssistance && !loggedAssist);
                        loggedAssist = true;
                    }
                }
                else
                {
                    session.RecordMilestone(experience, targetMilestoneId, usedAssistance);
                }
            }

            if (companion != null)
            {
                companion.Celebrate();
            }

            EncounterFinished?.Invoke(usedAssistance);
        }

        // ==========================================
        // Editor & Testing Simulation Methods
        // ==========================================
        [ContextMenu("Simulate Voice Success")]
        public void SimulateVoiceSuccess()
        {
            OnVoiceTranscriptionReceived("Un café s'il vous plaît");
        }

        [ContextMenu("Simulate Voice Error (Triggers Rescue)")]
        public void SimulateVoiceError()
        {
            OnVoiceTranscriptionReceived("Je ne sais pas");
        }

        [ContextMenu("Simulate Silence (>5s)")]
        public void SimulateSilence()
        {
            encounterTimer = silenceThreshold + 0.1f;
        }
    }
}
