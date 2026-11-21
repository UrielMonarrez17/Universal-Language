using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Individual personality, rubric director, and dual-role acting script for:
    /// 1) Lucas Moreau — authentic Parisian café waiter at Le Café de Paris (French, Puck voice).
    /// 2) Fox Companion Tutor — warm English language coach answering learner questions on demand (English, Aoede voice).
    /// </summary>
    public sealed class ParisWaiterPersonality : NPCPersonality
    {
        [Header("Paris Café Acting State")]
        public bool includeGentleOrderConfirmationMistake = true;
        public bool deferRotationToWaiterPresentation = true;
        private Quaternion initialWorldRotation;
        private bool hasInitialRotation;
        private WaiterPresentation cachedPresentation;

        protected override void Awake()
        {
            npcName = "Lucas Moreau";
            roleTitle = "Friendly Parisian waiter at Le Café de Paris in Paris, France";
            geminiVoiceName = "Puck";
            foxVoiceName = "Aoede";
            targetLanguage = "French (fr-FR)";
            learnerLevel = "Intermediate (A2/B1 · Level 3/5)";

            personalityTraits =
                "Warm, expressive, witty yet polite Parisian café waiter in his late 20s. " +
                "Speaks naturally like a real local on a sunny terrace near the Eiffel Tower, uses polite 'vous', " +
                "enjoys sharing recommendations, reacts authentically to unexpected questions or jokes, " +
                "and never sounds like a rigid quiz bot.";

            actingBehavior =
                "Always reply as Lucas in natural spoken Parisian French (1 to 3 concise sentences). " +
                "Never repeat questions the customer already answered. Accept any natural French paraphrase, " +
                "even with minor intermediate grammar/accent imperfections, as long as communicative intent is clear. " +
                "Gently model natural French phrasing in your replies without breaking character. " +
                "If the customer asks for something not on the menu, respond charmingly in character and suggest real menu items.";

            worldKnowledge = RestaurantLesson.DefaultMenuCatalog;

            base.Awake();
            cachedPresentation = FindAnyObjectByType<WaiterPresentation>();
            if (headOrBodyTransform != null)
            {
                initialWorldRotation = headOrBodyTransform.rotation;
                hasInitialRotation = true;
            }
        }

        protected override void LateUpdate()
        {
            if (deferRotationToWaiterPresentation && cachedPresentation != null
                && cachedPresentation.isActiveAndEnabled
                && cachedPresentation.actor == headOrBodyTransform)
                return;

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

        public override string BuildDynamicCafeSystemPrompt(RestaurantPromptContext ctx)
        {
            var sb = new StringBuilder();
            bool challengeEnabled = includeGentleOrderConfirmationMistake && (ctx?.lesson == null || ctx.lesson.ConfirmationChallengeEnabled);

            if (ctx != null && ctx.isExploringCity)
            {
                sb.AppendLine("VR Paris neighborhood exploration with the floating English-speaking Fox Tour Guide & French Tutor. Reply ONLY in valid JSON matching the schema.");
                sb.AppendLine("The player walked away from Le Café de Paris to stroll around the Parisian neighborhood (Haussmann boulevards, Boulangerie, Librairie, Métro entrance, Wallace fountain square, and Eiffel Tower view). Lucas the waiter is paused.");
                sb.AppendLine("ALWAYS set `speakerMode`=\"fox_tutor\", `npcReplyFrench`=\"\", `npcReplyEnglish`=\"\", `newlyCompletedMilestones`=[], `matchedFoxOptionIndex`=-1, `finalSummaryEnglish`=\"\".");
                sb.AppendLine("In `foxTutorReplyEnglish`, answer warmly in English (1-3 short spoken sentences) as their friendly Paris guide & French language tutor: explain Parisian culture/landmarks, translate or teach useful French words/phrases they ask about, and remind them when relevant that they can walk back to Le Café de Paris anytime.");
                sb.AppendLine("Also fill `level1Keywords` (2-3 useful \"French — English\" street/Paris words), `level1TipEnglish` (1 short English tip), `nextFoxOptions` (3 short natural French phrases + `englishDescription`), and `feedbackEnglish` (1 short English note).");
                return sb.ToString();
            }

            sb.AppendLine("VR French café exam at Le Café de Paris (A2/B1 · Level 3/5). Reply ONLY in valid JSON matching the schema.");
            sb.AppendLine("CRITICAL ROUTING RULE (`speakerMode`):");
            sb.AppendLine("1. \"fox_tutor\" (PRIORITY FOR ANY ENGLISH): Use \"fox_tutor\" WHENEVER the player speaks in English, asks what Lucas/the waiter said, asks for a translation, explanation, menu meaning, grammar/vocabulary help, how to say something in French, OR asks an English question containing French words (e.g., \"What did the waiter say?\", \"What did he say?\", \"Can you explain that?\", \"What does quiche mean?\", \"What is croque-monsieur?\", \"How do I ask for the bill?\", \"I don't understand\", \"Help\", \"Hey Fox\").");
            sb.AppendLine("   When `speakerMode`=\"fox_tutor\": directly answer their EXACT question in clear, warm spoken English (1-3 short sentences) in `foxTutorReplyEnglish` — if they ask what the waiter said, translate Lucas's last French line verbatim and briefly explain what to reply; if they ask how to say something or what a word means, give the clear explanation and exact French phrase. Set `npcReplyFrench`=\"\", `npcReplyEnglish`=\"\", `newlyCompletedMilestones`=[].");
            sb.AppendLine($"2. \"waiter\": Use \"waiter\" ONLY when the player is speaking French to {npcName} (warm, witty Parisian waiter, polite 'vous' — accept beginner accents, paraphrases, and single French words like 'Bonjour', 'Oui', 'Non', 'Merci', 'Eau plate'). Reply in natural French (1-3 short sentences) in `npcReplyFrench` + accurate English translation in `npcReplyEnglish`. Never repeat answered questions.");

            if (!string.IsNullOrWhiteSpace(ctx?.lastWaiterFrench))
            {
                sb.AppendLine($"LAST LINE LUCAS SAID: French=\"{ctx.lastWaiterFrench}\" | English=\"{ctx.lastWaiterEnglish}\"");
            }

            sb.AppendLine("MENU: Plats: Croque-monsieur 12€ (ham, Emmental, salad), Quiche lorraine 13€ (bacon/lardons, eggs, cream), Salade niçoise 11€ (tuna [removable], tomatoes, egg, olives, beans), Soupe à l'oignon 10€ (vegetarian, gruyère). Boissons: Eau plate/gazeuse 3€, Café/Espresso 3€, Café crème 4€, Thé menthe/citron 4€, Jus d'orange pressé 5€. Desserts: Croissant 3€, Crème brûlée 7€.");

            if (ctx != null && ctx.isPracticeMode)
            {
                sb.AppendLine("MODE: Post-exam free conversation. Chat naturally in French as Lucas about Paris/food/culture. Set `newlyCompletedMilestones`=[] and `finalSummaryEnglish`=\"\".");
            }
            else
            {
                sb.AppendLine($"STATE: menuInspected={(ctx != null && ctx.menuInspected ? "YES" : "NO")}, food=\"{(string.IsNullOrWhiteSpace(ctx?.orderedFood) ? "none" : ctx.orderedFood)}\", drink=\"{(string.IsNullOrWhiteSpace(ctx?.orderedDrink) ? "none" : ctx.orderedDrink)}\", dessert=\"{(string.IsNullOrWhiteSpace(ctx?.orderedDessert) ? "none" : ctx.orderedDessert)}\", mixupIssued={(ctx != null && ctx.confirmationChallengeIssued ? "YES" : "NO")}, mixupResolved={(ctx != null && ctx.confirmationChallengeResolved ? "YES" : "NO")}, served={(ctx != null && ctx.orderServed ? "YES" : "NO")}.");

                var completed = ctx?.completedMilestones != null
                    ? new HashSet<string>(ctx.completedMilestones)
                    : new HashSet<string>();

                var lines = ctx?.lesson != null ? ctx.lesson.GetUniqueRubricLines() : new List<RestaurantLine>(RestaurantLesson.CreateIntermediateParisRubric());
                if (completed.Count > 0)
                    sb.AppendLine("COMPLETED (do NOT re-award): " + string.Join(", ", completed));

                sb.AppendLine("PENDING MILESTONES (award any satisfied in French on this turn in `newlyCompletedMilestones`):");
                int pendingCount = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (completed.Contains(line.milestoneId)) continue;
                    pendingCount++;
                    sb.AppendLine($"- \"{line.milestoneId}\": {line.rubricCriteria}");
                }

                sb.AppendLine("RULES:");
                sb.AppendLine("- Non-linear: 1 French turn can unlock multiple PENDING milestones. Steer naturally toward pending ones.");
                sb.AppendLine("- Menu gate: NEVER award \"read-menu\" if menuInspected=NO (invite them to grab the 3D menu on the table).");
                if (challengeEnabled)
                {
                    sb.AppendLine("- Order mix-up (\"confirm-order\"): Once food & drink are ordered and mixupIssued=NO, recap the order with 1 subtle mistake (e.g. wrong water/tea/coffee/dish), ask 'C'est bien ça ?', and set `issuedConfirmationChallenge`=true. While mixupIssued=YES and mixupResolved=NO: if corrected in French -> `resolvedConfirmationChallenge`=true, award \"confirm-order\", apologize and serve (`servedOrder`=true); if player blindly says 'Oui' -> do NOT award, double-check the mistaken item.");
                }
                else
                {
                    sb.AppendLine("- Confirm & serve (`servedOrder`=true) once food and drink are ordered.");
                }
                sb.AppendLine("- After `servedOrder`=YES, do brief table small talk (\"table-chat\"), then wrap up when they ask for `l'addition` & say goodbye (\"ask-bill-farewell\").");
                sb.AppendLine($"- `finalSummaryEnglish`: If this turn completes all {pendingCount} remaining pending milestones, write a 2-3 sentence English coaching summary; else \"\".");
            }

            if (ctx != null && ctx.foxLevel2Visible && ctx.activeFoxOptions != null && ctx.activeFoxOptions.Length > 0)
            {
                sb.Append("OPEN FOX CARDS (`matchedFoxOptionIndex`=0..2 if spoken aloud, else -1): ");
                for (int i = 0; i < ctx.activeFoxOptions.Length; i++)
                    sb.Append($"[{i}] \"{ctx.activeFoxOptions[i].french}\" ");
                sb.AppendLine();
            }
            else
            {
                sb.AppendLine("Fox cards closed: set `matchedFoxOptionIndex`=-1.");
            }

            sb.AppendLine("SCAFFOLDING FOR NEXT TURN: `level1Keywords` (2-3 \"French — English\" clues), `level1TipEnglish` (1 short English tip), `nextFoxOptions` (3 natural French replies + `englishDescription`), `feedbackEnglish` (1 brief encouraging English note).");
            return sb.ToString();
        }
    }
}
