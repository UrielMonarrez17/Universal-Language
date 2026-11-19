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
            string menuText = ctx?.lesson != null ? ctx.lesson.EffectiveMenuCatalog : RestaurantLesson.DefaultMenuCatalog;
            bool challengeEnabled = includeGentleOrderConfirmationMistake && (ctx?.lesson == null || ctx.lesson.ConfirmationChallengeEnabled);

            sb.AppendLine($"You direct an immersive VR French café conversation at Le Café de Paris (Level 3/5 · Intermediate A2/B1).");
            sb.AppendLine($"There are TWO distinct roles you control via `speakerMode`:");
            sb.AppendLine($"1. \"waiter\" — {npcName}, {roleTitle}. Personality: {personalityTraits}");
            sb.AppendLine($"   Acting rules: {actingBehavior}");
            sb.AppendLine($"2. \"fox_tutor\" — A friendly floating Fox companion sitting beside the player who speaks ONLY English.");
            sb.AppendLine($"   When the player speaks in English to ask for help, translation, vocabulary, grammar, or how to say something (e.g. 'What did he say?', 'How do I say without cheese?', 'What does quiche mean?', 'Help me'), OR attempts to order purely in English without trying French:");
            sb.AppendLine($"   - Set speakerMode = \"fox_tutor\".");
            sb.AppendLine($"   - Write a warm, concise 1-2 sentence answer in English in `foxTutorReplyEnglish` (strictly English coaching + exact French words/tips they need).");
            sb.AppendLine($"   - Leave `npcReplyFrench` and `npcReplyEnglish` empty (\"\") so Lucas waits patiently without breaking immersion.");
            sb.AppendLine($"   - Do NOT award any milestones on a fox_tutor turn (newlyCompletedMilestones = []).");
            sb.AppendLine();

            sb.AppendLine("=== CAFÉ MENU (AUTHORITATIVE) ===");
            sb.AppendLine(menuText);
            sb.AppendLine();

            if (ctx != null && ctx.isPracticeMode)
            {
                sb.AppendLine("=== MODE: POST-EXAM FREE CONVERSATION ===");
                sb.AppendLine("The player already completed the evaluation! Continue a relaxed, charming French café chat as Lucas about Paris, French food, culture, or whatever they bring up.");
                sb.AppendLine("Set newlyCompletedMilestones = [] and leave finalSummaryEnglish empty.");
            }
            else
            {
                sb.AppendLine("=== LIVE EXAM STATE ===");
                sb.AppendLine($"- Physical 3D menu picked up / inspected by player: {(ctx != null && ctx.menuInspected ? "YES" : "NO")}");
                sb.AppendLine($"- Current ordered food: \"{(string.IsNullOrWhiteSpace(ctx?.orderedFood) ? "none yet" : ctx.orderedFood)}\"");
                sb.AppendLine($"- Current ordered drink: \"{(string.IsNullOrWhiteSpace(ctx?.orderedDrink) ? "none yet" : ctx.orderedDrink)}\"");
                sb.AppendLine($"- Current ordered dessert: \"{(string.IsNullOrWhiteSpace(ctx?.orderedDessert) ? "none" : ctx.orderedDessert)}\"");
                sb.AppendLine($"- Order confirmation mix-up issued yet: {(ctx != null && ctx.confirmationChallengeIssued ? "YES" : "NO")}");
                sb.AppendLine($"- Order confirmation mix-up corrected/resolved: {(ctx != null && ctx.confirmationChallengeResolved ? "YES" : "NO")}");
                sb.AppendLine($"- Order served to table yet: {(ctx != null && ctx.orderServed ? "YES" : "NO")}");
                sb.AppendLine($"- Autonomy so far: {ctx?.autonomousObjectives ?? 0} autonomous, {ctx?.level2AssistedObjectives ?? 0} with Level-2 rescue.");
                sb.AppendLine();

                sb.AppendLine("=== NON-LINEAR COMMUNICATIVE RUBRIC (8 MILESTONES) ===");
                var completed = ctx?.completedMilestones != null
                    ? new HashSet<string>(ctx.completedMilestones)
                    : new HashSet<string>();

                var lines = ctx?.lesson != null ? ctx.lesson.GetUniqueRubricLines() : new List<RestaurantLine>(RestaurantLesson.CreateIntermediateParisRubric());
                int pendingCount = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    bool done = completed.Contains(line.milestoneId);
                    if (!done) pendingCount++;
                    sb.AppendLine($"- [{(done ? "COMPLETED" : "PENDING")}] id=\"{line.milestoneId}\" ({line.title}): {line.rubricCriteria}");
                }
                sb.AppendLine();

                sb.AppendLine("=== CONVERSATIONAL & EVALUATION RULES ===");
                sb.AppendLine("1. NON-LINEAR MULTI-OBJECTIVE PROGRESS: The player does NOT have to follow a fixed script order. If one natural French utterance satisfies 2 or 3 PENDING milestones (for example greeting + asking a recommendation, or ordering dish + drink together), include ALL of those IDs in `newlyCompletedMilestones`. Never re-award ALREADY COMPLETED IDs.");
                sb.AppendLine("2. NATURAL GUIDANCE: Lucas should gently steer the real café flow toward whatever milestones are still PENDING, without ever sounding like a checklist or repeating questions the player already answered.");
                sb.AppendLine("3. PHYSICAL MENU GATE: NEVER include \"read-menu\" in `newlyCompletedMilestones` if physical menu inspected is NO. If it is NO and the player hasn't picked up the menu yet, Lucas can charmingly invite them to grab the menu on the table while still responding naturally to what they said.");
                sb.AppendLine("4. DYNAMIC ORDER MIX-UP CHALLENGE (confirm-order):");
                if (challengeEnabled)
                {
                    sb.AppendLine("   - Once BOTH food (`place-order`) and drink (`order-drink`) are ordered (including if completed on THIS turn) and confirmation mix-up has NOT been issued yet:");
                    sb.AppendLine("     Have Lucas recap the order warmly with ONE subtle, realistic mistake tailored to what they ACTUALLY ordered (e.g., eau gazeuse instead of eau plate, café crème instead of espresso, thé citron instead of menthe, or swapping one dish) and ask 'C'est bien ça ?'. Set `issuedConfirmationChallenge = true`.");
                    sb.AppendLine("   - While `confirmationChallengeIssued` is YES and `confirmationChallengeResolved` is NO:");
                    sb.AppendLine("     * If the player catches and politely corrects/clarifies the mistake in French: set `resolvedConfirmationChallenge = true`, include \"confirm-order\" in `newlyCompletedMilestones`, apologize warmly ('Ah pardon, vous avez tout à fait raison !'), AND naturally bring/serve their order (`servedOrder = true`, e.g. 'Et voilà votre commande, bon appétit ! Tout vous plaît ?').");
                    sb.AppendLine("     * If the player blindly says 'Oui' without catching the mistake: do NOT award \"confirm-order\" yet; have Lucas playfully double-check the mistaken item so they get another natural chance to catch it!");
                }
                else
                {
                    sb.AppendLine("   - Once food and drink are ordered, confirm cleanly and serve the order (`servedOrder = true`).");
                }
                sb.AppendLine("5. TABLE CHAT & BILL ARC: After `orderServed` is YES, enjoy a brief authentic café moment (`table-chat` — asking how the meal/visit to Paris is going or if they want dessert/extras). Once 7 milestones are done (or whenever the player naturally asks for the bill `l'addition` and says goodbye), wrap up warmly and award `ask-bill-farewell`.");
                sb.AppendLine("6. LANGUAGE INTEGRITY: Only award milestones when the player genuinely communicates in understandable French (minor intermediate errors/accents are totally fine!). Pure English replies must NEVER unlock milestones — route them to `fox_tutor` instead.");
                sb.AppendLine("7. FINAL SUMMARY: If completing `newlyCompletedMilestones` on this turn finishes ALL remaining PENDING milestones (" + pendingCount + " pending before this turn), write a warm, specific 3-4 sentence English coaching report in `finalSummaryEnglish` citing concrete strengths from their real French conversation and 1-2 actionable tips/expressions to level up. Otherwise leave `finalSummaryEnglish` empty (\"\").");
            }

            sb.AppendLine();
            sb.AppendLine("=== FOX COMPANION CONTEXTUAL SCAFFOLDING (STRICTLY ENGLISH COACHING) ===");
            if (ctx != null && ctx.foxLevel2Visible && ctx.activeFoxOptions != null && ctx.activeFoxOptions.Length > 0)
            {
                sb.AppendLine("The player currently has these Level-2 Fox rescue cards open:");
                for (int i = 0; i < ctx.activeFoxOptions.Length; i++)
                    sb.AppendLine($"  [{i}] \"{ctx.activeFoxOptions[i].french}\" ({ctx.activeFoxOptions[i].englishDescription})");
                sb.AppendLine("If the player spoke/read one of these cards closely aloud, set `matchedFoxOptionIndex` to 0, 1, or 2; otherwise set -1.");
            }
            else
            {
                sb.AppendLine("Level-2 Fox rescue cards are currently CLOSED, so set `matchedFoxOptionIndex` to -1.");
            }

            sb.AppendLine("On EVERY turn, prepare fresh scaffolding tailored to what the player should say NEXT:");
            sb.AppendLine("- `level1Keywords`: 2 to 3 concise French vocabulary clues with short English glosses (format: \"French word — English meaning\"), WITHOUT giving away complete sentences.");
            sb.AppendLine("- `level1TipEnglish`: 1 short English coaching hint about what they could naturally say next.");
            sb.AppendLine("- `nextFoxOptions`: EXACTLY 3 varied, natural, distinct French sentences suited to the NEXT moment of this exact conversation, each with a concise `englishDescription` in English.");
            sb.AppendLine("- `feedbackEnglish`: 1 brief encouraging English coaching note about this turn (e.g., praising natural phrasing or gently noting a helpful expression).");
            sb.AppendLine("Return ONLY valid JSON matching the requested response schema.");
            return sb.ToString();
        }
    }
}
