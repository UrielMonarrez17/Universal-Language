using System.Text;
using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Base personality and acting script for an AI-driven NPC powered by Google AI Studio (Gemini).
    /// Attach a specialized subclass per character to define voice, demeanor, roleplay rules, and behavior.
    /// </summary>
    public class NPCPersonality : MonoBehaviour
    {
        [Header("Identity & Voice")]
        public string npcName = "NPC";
        public string roleTitle = "Local resident";
        [Tooltip("Gemini prebuilt voice name (e.g. Puck, Charon, Kore, Fenrir, Aoede).")]
        public string geminiVoiceName = "Puck";
        public string targetLanguage = "French (fr-FR)";
        public string learnerLevel = "A1/A2 beginner";

        [Header("Personality & Acting")]
        [TextArea(3, 8)]
        public string personalityTraits =
            "Warm, attentive, patient, expressive, naturally encouraging without breaking character.";

        [TextArea(3, 8)]
        public string actingBehavior =
            "Speak naturally in short, clear French sentences suited to an adult beginner. " +
            "Never judge or grade harshly; gently model natural phrasing in your reply.";

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

        public virtual string BuildSystemPrompt(
            string currentStage,
            string dish,
            string drink,
            string waterType,
            bool menuInspected,
            FoxHelpOption[] activeFoxOptions)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {npcName}, {roleTitle}.");
            sb.AppendLine($"Language to speak: {targetLanguage}. Learner level: {learnerLevel}.");
            sb.AppendLine($"Personality: {personalityTraits}");
            sb.AppendLine($"Acting behavior: {actingBehavior}");
            if (!string.IsNullOrWhiteSpace(worldKnowledge))
                sb.AppendLine($"Setting & details: {worldKnowledge}");
            sb.AppendLine($"Current scenario stage: {currentStage}. Menu inspected: {menuInspected}. Current dish: '{dish}', drink: '{drink}', waterType: '{waterType}'.");
            if (activeFoxOptions != null && activeFoxOptions.Length > 0)
            {
                sb.AppendLine("Active Fox Companion rescue cards currently shown to the player:");
                for (int i = 0; i < activeFoxOptions.Length; i++)
                    sb.AppendLine($"  [{i}] \"{activeFoxOptions[i].french}\" ({activeFoxOptions[i].englishDescription})");
            }
            return sb.ToString();
        }
    }
}
