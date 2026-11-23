using System.Collections.Generic;
using System.Text;

namespace Alterlife.Immersive
{
    /// <summary>
    /// Personality, rubric director, and rich pedagogical feedback prompt builder for:
    /// 1) Lucas Moreau — Parisian café waiter (French, Gemini Puck voice).
    /// 2) Fox Companion Tutor — warm English language coach (Alibaba TTS, Cherry voice).
    /// </summary>
    public sealed class ParisWaiterPersonality : NPCPersonality
    {
        public bool includeGentleOrderConfirmationMistake = true;

        void Awake()
        {
            npcName = "Lucas Moreau";
            roleTitle = "Friendly Parisian waiter at Le Café de Paris in Paris, France";
            geminiVoiceName = "Puck";
            foxVoiceName = "Cherry";
            targetLanguage = "French (fr-FR)";
            learnerLevel = "Intermediate (A2/B1 · Level 3/5)";
            worldKnowledge = RestaurantLesson.DefaultMenuCatalog;
        }

        public override string BuildDynamicCafeSystemPrompt(RestaurantPromptContext ctx)
        {
            var sb = new StringBuilder();
            bool challengeEnabled = includeGentleOrderConfirmationMistake && (ctx?.lesson == null || ctx.lesson.ConfirmationChallengeEnabled);

            if (ctx != null && ctx.isExploringCity)
            {
                sb.AppendLine("VR Paris neighborhood exploration with the floating English-speaking Fox Tour Guide & French Tutor. Reply ONLY in valid JSON matching the schema.");
                sb.AppendLine("The player walked away from Le Café de Paris to stroll around the Parisian neighborhood. Lucas the waiter is paused.");
                sb.AppendLine("ALWAYS set `speakerMode`=\"fox_tutor\", `npcReplyFrench`=\"\", `npcReplyEnglish`=\"\", `newlyCompletedMilestones`=[], `matchedFoxOptionIndex`=-1, `finalScore`=0, `finalStrengths`=[], `finalImprovements`=[], `finalSummaryEnglish`=\"\".");
                sb.AppendLine("In `foxTutorReplyEnglish`, answer warmly in English (1-3 short spoken sentences) as their Paris guide & French tutor.");
                sb.AppendLine("Fill `level1Keywords` (2-3 \"French — English\" words), `level1TipEnglish`, `nextFoxOptions` (3 short French phrases), and `feedbackEnglish` (1-2 rich encouraging English sentences with a concrete French learning tip).");
                return sb.ToString();
            }

            sb.AppendLine("VR French café exam at Le Café de Paris (A2/B1 · Level 3/5). Reply ONLY in valid JSON matching the schema.");
            sb.AppendLine("CRITICAL ROUTING RULE (`speakerMode`):");
            sb.AppendLine("1. \"fox_tutor\" (PRIORITY FOR ANY ENGLISH): Use \"fox_tutor\" WHENEVER the player speaks in English, asks what Lucas said, asks for a translation, explanation, menu meaning, grammar/vocabulary help, or how to say something in French.");
            sb.AppendLine("   When `speakerMode`=\"fox_tutor\": answer their exact question in clear, warm spoken English (1-3 sentences) in `foxTutorReplyEnglish`. Set `npcReplyFrench`=\"\", `npcReplyEnglish`=\"\", `newlyCompletedMilestones`=[].");
            sb.AppendLine($"2. \"waiter\": Use \"waiter\" when the player speaks French to {npcName} (warm, witty Parisian waiter, polite 'vous' — accept beginner accents, paraphrases, and short French replies). Reply in natural French (1-3 sentences) in `npcReplyFrench` + accurate English translation in `npcReplyEnglish`.");

            if (!string.IsNullOrWhiteSpace(ctx?.lastWaiterFrench))
                sb.AppendLine($"LAST LINE LUCAS SAID: French=\"{ctx.lastWaiterFrench}\" | English=\"{ctx.lastWaiterEnglish}\"");

            sb.AppendLine("MENU: Plats: Croque-monsieur 12€ (ham, Emmental, salad), Quiche lorraine 13€ (bacon/lardons, eggs, cream), Salade niçoise 11€ (tuna [removable], tomatoes, egg, olives, beans), Soupe à l'oignon 10€ (vegetarian, gruyère). Boissons: Eau plate/gazeuse 3€, Café/Espresso 3€, Café crème 4€, Thé menthe/citron 4€, Jus d'orange pressé 5€. Desserts: Croissant 3€, Crème brûlée 7€.");

            if (ctx != null && ctx.isPracticeMode)
            {
                sb.AppendLine("MODE: Post-exam free conversation. Chat naturally in French as Lucas. Support ordering food/drink/dessert (`servedOrder`/`serveDessert`), clearing plates (`clearTable`), and bringing the bill (`bringBill`). Set `newlyCompletedMilestones`=[], `finalScore`=0, `finalStrengths`=[], `finalImprovements`=[], `finalSummaryEnglish`=\"\".");
            }
            else
            {
                sb.AppendLine($"STATE: menuInspected={(ctx != null && ctx.menuInspected ? "YES" : "NO")}, food=\"{(string.IsNullOrWhiteSpace(ctx?.orderedFood) ? "none" : ctx.orderedFood)}\", drink=\"{(string.IsNullOrWhiteSpace(ctx?.orderedDrink) ? "none" : ctx.orderedDrink)}\", dessert=\"{(string.IsNullOrWhiteSpace(ctx?.orderedDessert) ? "none" : ctx.orderedDessert)}\", mixupIssued={(ctx != null && ctx.confirmationChallengeIssued ? "YES" : "NO")}, mixupResolved={(ctx != null && ctx.confirmationChallengeResolved ? "YES" : "NO")}, served={(ctx != null && ctx.orderServed ? "YES" : "NO")}.");

                var completed = ctx?.completedMilestones != null ? new HashSet<string>(ctx.completedMilestones) : new HashSet<string>();
                var lines = ctx?.lesson != null ? ctx.lesson.GetUniqueRubricLines() : new List<RestaurantLine>(RestaurantLesson.CreateIntermediateParisRubric());
                if (completed.Count > 0)
                    sb.AppendLine("COMPLETED (do NOT re-award): " + string.Join(", ", completed));

                sb.AppendLine("PENDING MILESTONES (award any satisfied in French on this turn in `newlyCompletedMilestones`):");
                int pendingCount = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (completed.Contains(lines[i].milestoneId)) continue;
                    pendingCount++;
                    sb.AppendLine($"- \"{lines[i].milestoneId}\": {lines[i].rubricCriteria}");
                }

                sb.AppendLine("RULES:");
                sb.AppendLine("- Non-linear: 1 French turn can unlock multiple PENDING milestones. Steer naturally toward pending ones.");
                sb.AppendLine("- Menu gate: NEVER award \"read-menu\" if menuInspected=NO (invite them to grab the 3D menu on the table).");
                if (challengeEnabled)
                    sb.AppendLine("- Order mix-up (\"confirm-order\"): Once food & drink are ordered and mixupIssued=NO, recap the order with 1 subtle mistake, ask 'C'est bien ça ?', and set `issuedConfirmationChallenge`=true. When corrected in French -> `resolvedConfirmationChallenge`=true, award \"confirm-order\", apologize and serve (`servedOrder`=true).");
                else
                    sb.AppendLine("- Confirm & serve (`servedOrder`=true) once food and drink are ordered.");
                sb.AppendLine("- After `servedOrder`=YES, do brief table small talk (\"table-chat\"), then wrap up when they ask for `l'addition` & say goodbye (\"ask-bill-farewell\").");
                sb.AppendLine("- PHYSICAL SERVICE FLAGS: `servedOrder`=true when bringing main dish & drink; `serveDessert`=true when bringing a dessert; `clearTable`=true when player finished or asks to clear; `bringBill`=true when player asks for `l'addition`/to pay.");
                sb.AppendLine($"- FINAL EVALUATION: If this turn completes all {pendingCount} remaining milestones, fill: `finalScore` (integer 1-100 reflecting French fluency, politeness & accuracy), `finalStrengths` (2-3 concrete strengths from their French), `finalImprovements` (2-3 specific, actionable French grammar/vocab/politeness tips), and `finalSummaryEnglish` (a rich 2-3 sentence coaching summary). Otherwise set `finalScore`=0, `finalStrengths`=[], `finalImprovements`=[], `finalSummaryEnglish`=\"\".");
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

            sb.AppendLine("RICH COACHING & SCAFFOLDING EVERY TURN:");
            sb.AppendLine("- `feedbackEnglish`: Give high-quality, specific coaching in 1-2 concise sentences: praise what they communicated well in French (quote a good phrase if possible) AND include 1 concrete tip on grammar, politeness, vocabulary, or their natural next step.");
            sb.AppendLine("- Also fill `level1Keywords` (2-3 \"French — English\" clues), `level1TipEnglish` (1 clear English tip), and `nextFoxOptions` (3 natural French replies + `englishDescription`).");
            return sb.ToString();
        }
    }
}
