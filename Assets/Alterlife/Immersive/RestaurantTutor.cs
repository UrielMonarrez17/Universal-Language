using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Alterlife.Immersive
{
    internal static class RestaurantTutor
    {
        [System.Serializable] class Caption { public string text; public string english; }
        [System.Serializable] class Catalog { public Caption[] lines; }
        static Catalog catalog;
        public static string Translation(string prompt)
        {
            if (catalog == null) catalog = JsonUtility.FromJson<Catalog>(Resources.Load<TextAsset>("ParisWaiter/lines").text);
            foreach (var line in catalog.lines)
                if (line.text == prompt && !string.IsNullOrEmpty(line.english)) return line.english;
            return "Listen again to the French prompt, then try one of the examples.";
        }

        public static bool IsEnglishQuestion(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            return Regex.IsMatch(RestaurantEvaluation.Normalize(text), @"\b(what|how do|how to|could you|can you|help me|please help|i don t understand|i need|i would like|i want|hello|translate|definition|in english|what does|say that again)\b") || text.Trim().ToLowerInvariant() == "help";
        }

        static readonly Dictionary<string,string> Vocabulary = new Dictionary<string,string> {
            {"croque monsieur|sandwich","A croque-monsieur is a hot ham and cheese sandwich."},
            {"gazeuse|sparkling|sparkle","Eau gazeuse means sparkling water."},
            {"plate|still water","Eau plate means still water."},
            {"bonjour|hello","Bonjour means hello or good morning."},
            {"menu","Le menu is the list of dishes and drinks."},
            {"merci|thank you","Merci means thank you."},
            {"commander|order","Commander means to place an order."},
            {"salade|salad","Une salade means a salad."},
            {"cafe|coffee","Un café means a coffee."},
            {"boisson|drink","Une boisson is a drink."},
            {"conseillez|recommend","Vous me conseillez means you recommend to me."},
            {"eau|water","De l’eau means water."},
            {"addition|bill|check","L’addition is the bill."},
            {"fromage|cheese","Le fromage means cheese."},
            {"jambon|ham","Le jambon means ham."}
        };
        public static string Definition(string words)
        {
            var query = RestaurantEvaluation.Normalize(words);
            if (!Regex.IsMatch(query, @"\b(mean|means|meaning|define|definition|what is|how do|how to|translate|signifie|veut dire|comment dit)\b")) return null;
            foreach (var item in Vocabulary)
                if (Regex.IsMatch(query, @"\b(?:" + item.Key + @")\b")) return item.Value;
            return null;
        }
    }
}
