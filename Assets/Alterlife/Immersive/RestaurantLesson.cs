using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alterlife.Immersive
{
    [Serializable]
    public sealed class RestaurantLine
    {
        public string milestoneId;
        public string title;
        [TextArea(2, 5)] public string rubricCriteria;
        [TextArea] public string prompt;
        [TextArea] public string hint;
        public string example;
        public string alternative;
        public bool requiresMenuInspection;
        public string[] defaultKeywords;
        public AudioClip voice;
    }

    [CreateAssetMenu(menuName = "Alterlife/Restaurant lesson")]
    public sealed class RestaurantLesson : ScriptableObject
    {
        public string scenarioName = "Le Café de Paris";
        public string learnerLevel = "Intermediate (A2/B1 · Level 3/5)";
        [Min(1)] public int minAutonomousObjectives = 5;

        [TextArea(2, 4)]
        public string openingWaiterLineFrench =
            "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte.";

        [TextArea(2, 4)]
        public string openingWaiterLineEnglish =
            "Hello! Welcome to Le Café de Paris. Have a seat, here is the menu.";

        public string[] openingHintKeywords =
        {
            "Bonjour — Hello",
            "Merci beaucoup — Thank you very much",
            "La carte — The menu"
        };

        [TextArea(6, 16)]
        public string menuCatalog = DefaultMenuCatalog;

        public RestaurantLine[] lines;
        public bool challengeWaterConfirmation = true;
        public bool challengeOrderConfirmation = true;

        public const string DefaultMenuCatalog =
            "PLATS (Main Dishes):\n" +
            "- Croque-monsieur (12 €): toasted ham and melted Emmental cheese sandwich with a side salad.\n" +
            "- Quiche lorraine (13 €): savory pastry tart with smoked bacon (lardons), eggs, and cream.\n" +
            "- Salade niçoise (11 €): fresh salad with tuna, tomatoes, hard-boiled egg, olives, and green beans (can be made without tuna on request).\n" +
            "- Soupe à l'oignon (10 €): traditional French onion soup topped with melted gruyère cheese (vegetarian option).\n\n" +
            "BOISSONS (Drinks):\n" +
            "- Eau plate (3 €) / Eau gazeuse (3 €): still or sparkling water.\n" +
            "- Café / Espresso (3 €) or Café crème (4 €).\n" +
            "- Thé (4 €): mint (menthe) or lemon (citron) tea.\n" +
            "- Jus d'orange pressé (5 €): freshly squeezed orange juice.\n\n" +
            "DESSERTS:\n" +
            "- Croissant au beurre (3 €): warm butter croissant.\n" +
            "- Crème brûlée (7 €): classic vanilla custard with caramelized sugar crust.";

        public const string PhysicalMenuDisplayText =
            "<b>PLATS</b>\n" +
            "Croque-monsieur · 12 €  <i>(jambon, fromage)</i>\n" +
            "Quiche lorraine · 13 €  <i>(lardons, crème)</i>\n" +
            "Salade niçoise · 11 €  <i>(thon, tomates, olives)</i>\n" +
            "Soupe à l'oignon · 10 €  <i>(gratinée)</i>\n\n" +
            "<b>BOISSONS</b>\n" +
            "Eau plate / gazeuse · 3 €\n" +
            "Café · 3 €   |   Café crème · 4 €\n" +
            "Thé (menthe / citron) · 4 €\n" +
            "Jus d'orange pressé · 5 €\n\n" +
            "<b>DESSERTS</b>\n" +
            "Croissant · 3 €   |   Crème brûlée · 7 €";

        public bool ConfirmationChallengeEnabled => challengeOrderConfirmation || challengeWaterConfirmation;

        public string EffectiveMenuCatalog =>
            string.IsNullOrWhiteSpace(menuCatalog) ? DefaultMenuCatalog : menuCatalog;

        public string EffectiveOpeningFrench =>
            string.IsNullOrWhiteSpace(openingWaiterLineFrench)
                ? "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte."
                : openingWaiterLineFrench;

        public string EffectiveOpeningEnglish =>
            string.IsNullOrWhiteSpace(openingWaiterLineEnglish)
                ? "Hello! Welcome to Le Café de Paris. Have a seat, here is the menu."
                : openingWaiterLineEnglish;

        public RestaurantLine GetLineByMilestone(string milestoneId)
        {
            if (lines == null || string.IsNullOrWhiteSpace(milestoneId)) return null;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i] != null && string.Equals(lines[i].milestoneId, milestoneId, StringComparison.Ordinal))
                    return lines[i];
            }
            return null;
        }

        public List<RestaurantLine> GetUniqueRubricLines()
        {
            var result = new List<RestaurantLine>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (lines != null)
            {
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line == null || string.IsNullOrWhiteSpace(line.milestoneId)) continue;
                    if (seen.Add(line.milestoneId))
                        result.Add(line);
                }
            }
            return result;
        }

        public static RestaurantLine[] CreateIntermediateParisRubric()
        {
            return new[]
            {
                new RestaurantLine
                {
                    milestoneId = "greet-waiter",
                    title = "Polite Greeting & 'vous'",
                    rubricCriteria = "Greet Lucas naturally in French (e.g. Bonjour/Bonsoir) and show polite café etiquette using 'vous', 'merci', or 's'il vous plaît'.",
                    prompt = "Bonjour ! Bienvenue au Café de Paris. Installez-vous, voici la carte.",
                    hint = "Greet Lucas politely in French.",
                    example = "Bonjour monsieur, merci beaucoup !",
                    alternative = "Bonjour ! Merci pour la carte.",
                    defaultKeywords = new[] { "Bonjour — Hello", "Merci — Thank you", "Vous — Polite 'you'" }
                },
                new RestaurantLine
                {
                    milestoneId = "read-menu",
                    title = "Inspect the Café Menu",
                    rubricCriteria = "The player MUST physically pick up/inspect the menu on the table (menuInspected == true) and engage with it in French (e.g. saying they are ready, commenting on dishes, or referencing items from the menu).",
                    prompt = "Je vous laisse regarder la carte. Dites-moi quand vous êtes prêt.",
                    hint = "Pick up the physical menu on the table and let Lucas know when you're ready.",
                    example = "Merci, j'ai regardé la carte et je suis prêt à commander.",
                    alternative = "Tout a l'air délicieux ! Je suis prête.",
                    requiresMenuInspection = true,
                    defaultKeywords = new[] { "La carte — Menu", "Prêt(e) — Ready", "Commander — To order" }
                },
                new RestaurantLine
                {
                    milestoneId = "ask-about-menu",
                    title = "Ask About Recommendations or Details",
                    rubricCriteria = "Ask Lucas a genuine question in French about the menu: a recommendation, today's favorite, ingredients, dietary preferences (e.g. vegetarian/allergies), or prices.",
                    prompt = "Vous voulez un conseil sur nos spécialités ?",
                    hint = "Ask for a recommendation, ingredients, or prices in French.",
                    example = "Qu'est-ce que vous me conseillez aujourd'hui ?",
                    alternative = "Qu'est-ce qu'il y a dans la quiche lorraine ?",
                    defaultKeywords = new[] { "Vous conseillez — You recommend", "Ingrédients — Ingredients", "Combien coûte — How much is" }
                },
                new RestaurantLine
                {
                    milestoneId = "place-order",
                    title = "Order Food Naturally",
                    rubricCriteria = "Order at least one food item from the café menu in natural French (dish or pastry, optionally specifying preferences). Off-menu requests should be politely redirected until a real café menu item is ordered.",
                    prompt = "Qu'est-ce qui vous ferait plaisir comme plat ?",
                    hint = "Order a dish from the menu in French.",
                    example = "Je vais prendre une quiche lorraine, s'il vous plaît.",
                    alternative = "Je voudrais une soupe à l'oignon, s'il vous plaît.",
                    defaultKeywords = new[] { "Je voudrais — I would like", "Je vais prendre — I'll have", "S'il vous plaît — Please" }
                },
                new RestaurantLine
                {
                    milestoneId = "order-drink",
                    title = "Order a Beverage",
                    rubricCriteria = "Order a drink from the café menu in natural French (e.g. still/sparkling water, espresso/café crème, mint/lemon tea, fresh orange juice), clarifying preference if asked.",
                    prompt = "Et qu'est-ce que je vous sers comme boisson ?",
                    hint = "Order a beverage in French.",
                    example = "Une eau gazeuse, s'il vous plaît.",
                    alternative = "Je voudrais un café crème, s'il vous plaît.",
                    defaultKeywords = new[] { "Boisson — Drink", "Eau plate / gazeuse — Still / sparkling", "Café crème — Coffee with milk" }
                },
                new RestaurantLine
                {
                    milestoneId = "confirm-order",
                    title = "Catch & Correct Order Mix-Up",
                    rubricCriteria = "After Lucas summarizes the order with ONE subtle, natural mix-up tailored to what the player ordered, the player must notice it and politely correct or clarify their order in French. Blindly agreeing ('Oui') to the wrong recap must NOT complete this goal.",
                    prompt = "Alors, si j'ai bien noté... C'est bien ça ?",
                    hint = "Listen carefully to Lucas's recap and politely correct any mistake.",
                    example = "Pardon, j'avais demandé de l'eau plate, pas gazeuse.",
                    alternative = "Non, excusez-moi, j'ai choisi une salade niçoise.",
                    defaultKeywords = new[] { "Pardon — Excuse me", "J'avais demandé — I asked for", "En fait — Actually" }
                },
                new RestaurantLine
                {
                    milestoneId = "table-chat",
                    title = "Café Follow-Up & Small Talk",
                    rubricCriteria = "Once the order is served ('Voici votre commande, bon appétit !'), respond naturally in French to Lucas's friendly check-in (e.g. commenting on the food/terrace, answering a brief question about their visit to Paris, or requesting a dessert/napkin/extra).",
                    prompt = "Voici votre commande, bon appétit ! Tout vous plaît ? Vous aimez le quartier ?",
                    hint = "Chat briefly with Lucas in French after your order arrives.",
                    example = "Merci beaucoup, c'est délicieux ! J'adore ce quartier.",
                    alternative = "C'est très bon, merci ! Est-ce que je peux aussi avoir une crème brûlée ?",
                    defaultKeywords = new[] { "Délicieux — Delicious", "Quartier — Neighborhood", "Autre chose — Anything else" }
                },
                new RestaurantLine
                {
                    milestoneId = "ask-bill-farewell",
                    title = "Ask for the Bill & Farewell",
                    rubricCriteria = "Politely ask for the check ('l'addition') and close the visit with a courteous French farewell (e.g. Merci beaucoup, au revoir / bonne journée).",
                    prompt = "Est-ce que je peux vous apporter autre chose, ou l'addition ?",
                    hint = "Ask for the bill (l'addition) and say goodbye politely.",
                    example = "L'addition, s'il vous plaît. Merci beaucoup et bonne journée !",
                    alternative = "Je voudrais payer, s'il vous plaît. Merci, au revoir !",
                    defaultKeywords = new[] { "L'addition — The bill", "Payer — To pay", "Bonne journée / Au revoir — Have a nice day / Goodbye" }
                }
            };
        }
    }
}
