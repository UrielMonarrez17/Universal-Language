using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Alterlife.Immersive
{
    public sealed class RestaurantPromptContext
    {
        public RestaurantLesson lesson;
        public IReadOnlyCollection<string> completedMilestones;
        public bool menuInspected;
        public string orderedFood;
        public string orderedDrink;
        public string orderedDessert;
        public bool confirmationChallengeIssued;
        public bool confirmationChallengeResolved;
        public bool orderServed;
        public bool isPracticeMode;
        public bool isExploringCity;
        public string lastWaiterFrench;
        public string lastWaiterEnglish;
        public bool foxLevel2Visible;
        public FoxHelpOption[] activeFoxOptions;
        public int autonomousObjectives;
        public int level2AssistedObjectives;
        public int totalTurns;
    }

    /// <summary>
    /// Base personality and acting script for an AI-driven NPC powered by Google AI Studio (Gemini).
    /// Attach a specialized subclass per character to define voice, demeanor, roleplay rules, and behavior.
    /// </summary>
    public class NPCPersonality : MonoBehaviour
    {
        [Header("Identity & Voice")]
        public string npcName = "NPC";
        public string roleTitle = "Local resident";
        [Tooltip("Gemini prebuilt voice name for this NPC (e.g. Puck, Charon, Kore, Fenrir, Aoede).")]
        public string geminiVoiceName = "Puck";
        [Tooltip("Distinct Gemini prebuilt voice name for the Fox companion tutor.")]
        public string foxVoiceName = "Aoede";
        public string targetLanguage = "French (fr-FR)";
        public string learnerLevel = "A2/B1 intermediate (Level 3/5)";

        [Header("Personality & Acting")]
        [TextArea(3, 8)]
        public string personalityTraits =
            "Warm, attentive, patient, expressive, naturally encouraging without breaking character.";

        [TextArea(3, 8)]
        public string actingBehavior =
            "Speak naturally in clear, authentic Parisian French suited to an intermediate learner. " +
            "Never sound scripted or robotic; gently model natural phrasing in your reply.";

        [TextArea(3, 8)]
        public string worldKnowledge = "";

        [Header("Physical Presence")]
        public Transform headOrBodyTransform;
        public float lookAtPlayerSpeed = 3.5f;

        private Transform playerCamera;

        protected virtual void Awake()
        {
            if (headOrBodyTransform == null)
                headOrBodyTransform = transform;
            if (Camera.main != null)
                playerCamera = Camera.main.transform;
        }

        protected virtual void LateUpdate()
        {
            if (headOrBodyTransform == null)
                return;

            if (playerCamera == null && Camera.main != null)
                playerCamera = Camera.main.transform;

            if (playerCamera == null)
                return;

            Vector3 toPlayer = playerCamera.position - headOrBodyTransform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f)
                return;

            Quaternion targetYaw = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
            headOrBodyTransform.rotation = Quaternion.Slerp(
                headOrBodyTransform.rotation,
                targetYaw,
                Time.deltaTime * lookAtPlayerSpeed);
        }

        public virtual string BuildDynamicCafeSystemPrompt(RestaurantPromptContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {npcName}, {roleTitle}.");
            sb.AppendLine($"Language to speak as NPC: {targetLanguage}. Learner level: {learnerLevel}.");
            sb.AppendLine($"Personality: {personalityTraits}");
            sb.AppendLine($"Acting behavior: {actingBehavior}");
            if (!string.IsNullOrWhiteSpace(worldKnowledge))
                sb.AppendLine($"Setting & details:\n{worldKnowledge}");
            return sb.ToString();
        }

        // Retained for compatibility with any external utilities
        public virtual string BuildSystemPrompt(
            string currentStage,
            string dish,
            string drink,
            string waterType,
            bool menuInspected,
            FoxHelpOption[] activeFoxOptions)
        {
            return BuildDynamicCafeSystemPrompt(new RestaurantPromptContext
            {
                orderedFood = dish,
                orderedDrink = string.IsNullOrWhiteSpace(waterType) ? drink : $"{drink} {waterType}".Trim(),
                menuInspected = menuInspected,
                activeFoxOptions = activeFoxOptions,
                foxLevel2Visible = activeFoxOptions != null && activeFoxOptions.Length > 0
            });
        }
    }
}
