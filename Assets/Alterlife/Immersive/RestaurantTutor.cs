using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Alterlife.Immersive
{
    [Serializable]
    public sealed class FoxLevel1Hint
    {
        public string waiterEnglishTranslation;
        public string[] keywords;
        public string situationalTip;
    }

    /// <summary>
    /// Pedagogical state & formatting coordinator for the Fox Tutor in Le Café de Paris.
    /// Keeps live Gemini-generated English translations, Level 1 subtle hints, Level 2 dynamic cards,
    /// and direct English tutor answers synchronized without relying on static phrase lookup tables.
    /// </summary>
    public sealed class RestaurantTutor
    {
        public string LastWaiterFrench { get; private set; } = "";
        public string LastWaiterEnglish { get; private set; } = "";
        public FoxLevel1Hint CurrentLevel1Hint { get; private set; } = new FoxLevel1Hint();
        public FoxHelpOption[] CurrentLevel2Options { get; private set; } = Array.Empty<FoxHelpOption>();
        public string LastTutorReplyEnglish { get; private set; } = "";

        public void Reset(RestaurantLesson lesson)
        {
            LastWaiterFrench = lesson != null ? lesson.EffectiveOpeningFrench : "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte.";
            LastWaiterEnglish = lesson != null ? lesson.EffectiveOpeningEnglish : "Hello! Welcome to Le Café de Paris. Have a seat, here is the menu.";
            LastTutorReplyEnglish = "";

            string[] openingKeywords = lesson != null && lesson.openingHintKeywords != null && lesson.openingHintKeywords.Length > 0
                ? lesson.openingHintKeywords
                : new[] { "Bonjour — Hello", "Merci beaucoup — Thank you", "La carte — The menu" };

            CurrentLevel1Hint = new FoxLevel1Hint
            {
                waiterEnglishTranslation = LastWaiterEnglish,
                keywords = openingKeywords,
                situationalTip = "Greet Lucas politely in French, pick up the menu on the table, or ask for a recommendation."
            };

            CurrentLevel2Options = new[]
            {
                new FoxHelpOption
                {
                    french = "Bonjour monsieur, merci beaucoup pour la carte !",
                    englishDescription = "Warm polite greeting thanking Lucas for the menu"
                },
                new FoxHelpOption
                {
                    french = "Bonjour ! Qu'est-ce que vous me conseillez aujourd'hui ?",
                    englishDescription = "Greet Lucas and ask what he recommends right away"
                },
                new FoxHelpOption
                {
                    french = "Bonjour, merci. Je vais regarder le menu une minute.",
                    englishDescription = "Greet politely and say you'll look at the menu for a moment"
                }
            };
        }

        public void UpdateFromWaiterTurn(
            string waiterFrench,
            string waiterEnglish,
            string[] level1Keywords,
            string level1TipEnglish,
            FoxHelpOption[] nextLevel2Options)
        {
            if (!string.IsNullOrWhiteSpace(waiterFrench))
                LastWaiterFrench = waiterFrench.Trim();

            if (!string.IsNullOrWhiteSpace(waiterEnglish))
                LastWaiterEnglish = waiterEnglish.Trim();

            if (CurrentLevel1Hint == null)
                CurrentLevel1Hint = new FoxLevel1Hint();

            CurrentLevel1Hint.waiterEnglishTranslation = LastWaiterEnglish;

            if (level1Keywords != null && level1Keywords.Length > 0)
                CurrentLevel1Hint.keywords = level1Keywords;

            if (!string.IsNullOrWhiteSpace(level1TipEnglish))
                CurrentLevel1Hint.situationalTip = level1TipEnglish.Trim();

            if (nextLevel2Options != null && nextLevel2Options.Length > 0)
                CurrentLevel2Options = nextLevel2Options;
        }

        public void UpdateFromFoxTutorTurn(
            string tutorReplyEnglish,
            string[] level1Keywords,
            string level1TipEnglish,
            FoxHelpOption[] nextLevel2Options)
        {
            if (!string.IsNullOrWhiteSpace(tutorReplyEnglish))
                LastTutorReplyEnglish = tutorReplyEnglish.Trim();

            if (CurrentLevel1Hint == null)
                CurrentLevel1Hint = new FoxLevel1Hint();

            CurrentLevel1Hint.waiterEnglishTranslation = LastWaiterEnglish;

            if (level1Keywords != null && level1Keywords.Length > 0)
                CurrentLevel1Hint.keywords = level1Keywords;

            if (!string.IsNullOrWhiteSpace(level1TipEnglish))
                CurrentLevel1Hint.situationalTip = level1TipEnglish.Trim();

            if (nextLevel2Options != null && nextLevel2Options.Length > 0)
                CurrentLevel2Options = nextLevel2Options;
        }

        public string FormatLevel1Panel()
        {
            var sb = new StringBuilder();
            sb.AppendLine("<color=#7DD3FC><b>FOX HINT · LEVEL 1</b></color>");

            if (!string.IsNullOrWhiteSpace(LastWaiterEnglish))
                sb.AppendLine($"<color=#CBD5E1>Lucas said:</color> \"{LastWaiterEnglish}\"");

            if (CurrentLevel1Hint != null && CurrentLevel1Hint.keywords != null && CurrentLevel1Hint.keywords.Length > 0)
            {
                sb.Append("<color=#FDE68A>Key words:</color> ");
                for (int i = 0; i < CurrentLevel1Hint.keywords.Length; i++)
                {
                    if (i > 0) sb.Append("  ·  ");
                    sb.Append(CurrentLevel1Hint.keywords[i]);
                }
                sb.AppendLine();
            }

            if (CurrentLevel1Hint != null && !string.IsNullOrWhiteSpace(CurrentLevel1Hint.situationalTip))
                sb.AppendLine($"<size=85%><color=#E2E8F0>{CurrentLevel1Hint.situationalTip}</color></size>");

            sb.Append("<size=75%><color=#94A3B8>Press Y again for 3 example phrases (Level 2)</color></size>");
            return sb.ToString().TrimEnd();
        }

        public string FormatTutorAnswerPanel(string tutorReplyEnglish)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<color=#7DD3FC><b>FOX TUTOR</b></color>");
            if (!string.IsNullOrWhiteSpace(tutorReplyEnglish))
                sb.AppendLine(tutorReplyEnglish.Trim());

            if (CurrentLevel1Hint != null && CurrentLevel1Hint.keywords != null && CurrentLevel1Hint.keywords.Length > 0)
            {
                sb.Append("<size=82%><color=#FDE68A>Useful French:</color> ");
                for (int i = 0; i < CurrentLevel1Hint.keywords.Length; i++)
                {
                    if (i > 0) sb.Append("  ·  ");
                    sb.Append(CurrentLevel1Hint.keywords[i]);
                }
                sb.AppendLine("</size>");
            }

            sb.Append("<size=72%><color=#94A3B8>Reply to Lucas in French · Press Y for phrase cards</color></size>");
            return sb.ToString().TrimEnd();
        }

        public string FormatLevel2Header()
        {
            string translation = string.IsNullOrWhiteSpace(LastWaiterEnglish)
                ? "Listen to Lucas and try speaking naturally."
                : $"Lucas said: \"{LastWaiterEnglish}\"";
            return $"{translation}\n<size=78%><color=#CBD5E1>Tap a card to hear pronunciation, then speak aloud · Press Y to close</color></size>";
        }

        // Legacy-compatible helper retained in case external utilities query it
        public static bool IsEnglishQuestion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string normalized = RestaurantEvaluation.Normalize(text);
            return Regex.IsMatch(
                normalized,
                @"\b(what|how do|how to|could you|can you|help me|please help|i don t understand|translate|definition|in english|what does|what did he|say that again)\b")
                || normalized == "help";
        }
    }
}
