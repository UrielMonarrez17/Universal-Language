using System.Text;
using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Individual personality and acting script for Lucas Moreau, the friendly Parisian café waiter.
    /// Guides the learner naturally through ordering at a Parisian terrace while keeping immersion high.
    /// </summary>
    public sealed class ParisWaiterPersonality : NPCPersonality
    {
        [Header("Paris Café Acting State")]
        public bool includeGentleWaterConfirmationMistake = true;
        private Quaternion initialWorldRotation;
        private bool hasInitialRotation;

        protected override void Awake()
        {
            npcName = "Lucas Moreau";
            roleTitle = "Friendly waiter at Le Café de Paris in Paris, France";
            geminiVoiceName = "Puck";
            targetLanguage = "French (fr-FR)";
            learnerLevel = "A1/A2 beginner";

            personalityTraits =
                "Charming, upbeat, patient Parisian café server in his late 20s. " +
                "Takes pride in simple French bistro classics, smiles easily, uses polite French ('vous'), " +
                "never rushes tourists, and responds naturally like a real person on a sunny Paris terrace.";

            actingBehavior =
                "Always respond in natural French (1 to 2 short, clear sentences + 1 gentle question when guiding the order). " +
                "Stay completely in character as Lucas at Le Café de Paris. " +
                "If the player makes a grammar or pronunciation slip, understand their intent warmly and model the correct French naturally in your reply. " +
                "Follow the café flow naturally across 6 communicative goals: " +
                "1) greet-waiter (greet & thank), 2) read-menu (let player look at the menu and say they are ready), " +
                "3) ask-about-menu (offer recommendation when asked), 4) place-order (dish: croque-monsieur or salade), " +
                "5) respond-to-waiter (drink: eau plate/gazeuse or café), 6) confirm-order (repeat the order; if they ordered water and haven't confirmed yet, play along with a classic slight mix-up between eau plate and eau gazeuse so they politely correct you!).";

            worldKnowledge =
                "Today's menu at Le Café de Paris: " +
                "Croque-monsieur (ham and melted cheese toasted sandwich, 12 EUR), " +
                "Salade (fresh tomato and cheese salad, 10 EUR), " +
                "Eau plate or Eau gazeuse (still or sparkling water, 3 EUR), " +
                "Café (espresso or café crème, 3 EUR).";

            base.Awake();
            if (headOrBodyTransform != null)
            {
                initialWorldRotation = headOrBodyTransform.rotation;
                hasInitialRotation = true;
            }
        }

        protected override void LateUpdate()
        {
            // Keep the barista GLB's authored X(-90) upright tilt intact while rotating smoothly in world Yaw toward the player.
            if (headOrBodyTransform == null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            Vector3 toPlayer = cam.transform.position - headOrBodyTransform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude < 0.01f)
                return;

            if (!hasInitialRotation)
            {
                initialWorldRotation = headOrBodyTransform.rotation;
                hasInitialRotation = true;
            }

            float targetYaw = Quaternion.LookRotation(toPlayer.normalized, Vector3.up).eulerAngles.y;
            Vector3 initialEuler = initialWorldRotation.eulerAngles;
            Quaternion target = Quaternion.Euler(initialEuler.x, targetYaw, initialEuler.z);
            headOrBodyTransform.rotation = Quaternion.Slerp(
                headOrBodyTransform.rotation,
                target,
                Time.deltaTime * lookAtPlayerSpeed);
        }

        public override string BuildSystemPrompt(
            string currentStage,
            string dish,
            string drink,
            string waterType,
            bool menuInspected,
            FoxHelpOption[] activeFoxOptions)
        {
            var sb = new StringBuilder(base.BuildSystemPrompt(currentStage, dish, drink, waterType, menuInspected, activeFoxOptions));
            sb.AppendLine("Respond ONLY with valid JSON (no markdown fences) matching this exact schema:");
            sb.AppendLine("{");
            sb.AppendLine("  \"userTranscript\": \"exact words the user said in French/English (or empty if unclear audio)\",");
            sb.AppendLine("  \"npcReplyFrench\": \"Lucas's spoken French response (1-2 short natural sentences)\",");
            sb.AppendLine("  \"npcReplyEnglish\": \"Exact English subtitle for npcReplyFrench\",");
            sb.AppendLine("  \"feedbackEnglish\": \"brief encouraging coaching tip in English for the journal\",");
            sb.AppendLine("  \"advanceObjective\": true/false,");
            sb.AppendLine("  \"detectedDish\": \"salade\" | \"croque-monsieur\" | \"\",");
            sb.AppendLine("  \"detectedDrink\": \"eau\" | \"café\" | \"\",");
            sb.AppendLine("  \"detectedWaterType\": \"plate\" | \"gazeuse\" | \"\",");
            sb.AppendLine("  \"matchedFoxOptionIndex\": -1 or 0/1/2 if the player read/spoke one of the active Fox options aloud,");
            sb.AppendLine("  \"nextFoxOptions\": [");
            sb.AppendLine("    {\"french\": \"phrase 1\", \"englishDescription\": \"short meaning and when to use it in English\"},");
            sb.AppendLine("    {\"french\": \"phrase 2\", \"englishDescription\": \"short meaning and when to use it in English\"},");
            sb.AppendLine("    {\"french\": \"phrase 3\", \"englishDescription\": \"short meaning and when to use it in English\"}");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("Note: When currentStage is ReadMenu and menuInspected is false, kindly remind them to pick up/look at the menu on the table first and set advanceObjective=false.");
            sb.AppendLine("Always provide exactly 3 helpful, distinct nextFoxOptions appropriate for the NEXT step of the conversation.");
            return sb.ToString();
        }
    }
}
