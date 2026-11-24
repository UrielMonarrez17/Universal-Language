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
    /// Base personality and prompt builder for an AI-driven NPC powered by Google AI Studio (Gemini).
    /// </summary>
    public class NPCPersonality : MonoBehaviour
    {
        [Header("Identity & Voice")]
        public string npcName = "NPC";
        public string roleTitle = "Local resident";
        public string geminiVoiceName = "Puck";
        public string foxVoiceName = "Cherry";
        public string targetLanguage = "French (fr-FR)";
        public string learnerLevel = "A2/B1 intermediate (Level 3/5)";

        [Header("Personality & Acting")]
        [TextArea(3, 8)] public string personalityTraits = "Warm, attentive, patient, expressive, naturally encouraging.";
        [TextArea(3, 8)] public string actingBehavior = "Speak naturally in clear Parisian French suited to an intermediate learner.";
        [TextArea(3, 8)] public string worldKnowledge = "";

        public virtual string BuildDynamicCafeSystemPrompt(RestaurantPromptContext context)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"You are {npcName}, {roleTitle}. Language: {targetLanguage}. Level: {learnerLevel}.");
            sb.AppendLine($"Personality: {personalityTraits}");
            sb.AppendLine($"Acting: {actingBehavior}");
            if (!string.IsNullOrWhiteSpace(worldKnowledge))
                sb.AppendLine($"Context:\n{worldKnowledge}");
            return sb.ToString();
        }
    }
}
