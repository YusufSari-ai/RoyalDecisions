using System;
using System.Collections.Generic;
using RoyalDecisions.Data;
using UnityEngine;

namespace RoyalDecisions.Editor
{
    /// <summary>
    /// Builds the placeholder card and ending set in memory, with no asset I/O.
    /// </summary>
    /// <remarks>
    /// Deliberately separate from the generator: keeping content construction free of
    /// <c>AssetDatabase</c> lets tests build the whole set, validate it, and play a full run
    /// against it without writing a single file.
    ///
    /// Every card exists to prove one capability of the Phase 2 rule engine — see the table in the
    /// Phase 3 plan. All of it is disposable test data; replacing it must never require a code
    /// change (CLAUDE.md §4).
    /// </remarks>
    public static class PlaceholderContentLibrary
    {
        /// <summary>Marks generated text so placeholder content is obvious on screen and in the Inspector.</summary>
        public const string PlaceholderTag = "[PLACEHOLDER]";

        public const string OpeningCardId = "card_01_coronation";

        public const string FlagTaxesRaised = "taxes_raised";
        public const string FlagArmyFavoured = "army_favoured";

        private const int LowStatThreshold = 25;

        /// <summary>The twenty placeholder cards, pre-sorted by ordinal ID.</summary>
        public static List<CardDefinition> CreateCards()
        {
            List<CardDefinition> cards = new List<CardDefinition>(20)
            {
                Card("card_01_coronation", "The Chamberlain",
                    "The crown is ready, and so is the court. Shall we proceed with the rite?",
                    Choice("Take the crown", authority: 10, people: 5),
                    Choice("Delay the rite", authority: -5, security: 5),
                    oncePerRun: true),

                Card("card_02_border_report", "The Marshal",
                    "Riders report movement beyond the eastern passes. Nothing confirmed yet.",
                    Choice("Reinforce the passes", security: 10, wealth: -10),
                    Choice("Spare the treasury", security: -10, wealth: 10)),

                Card("card_03_harvest", "The Reeve",
                    "The harvest is counted. The barns are fuller than last year, barely.",
                    Choice("Collect the tithe", people: -10, wealth: 10),
                    Choice("Forgive the tithe", people: 10, wealth: -10),
                    weight: 3),

                Card("card_04_tax_reform", "The Treasurer",
                    "The ledgers do not balance. A new levy would close the gap within a season.",
                    Choice("Raise the levy", people: -10, wealth: 15, add: Flags(FlagTaxesRaised)),
                    Choice("Leave the rates alone", wealth: -5)),

                Card("card_05_tax_backlash", "A Guild Master",
                    "The new levy has emptied our workshops. The guilds ask you to reconsider.",
                    Choice("Send the guard", authority: 5, people: -10, security: 5),
                    Choice("Repeal the levy", people: 10, wealth: -10, remove: Flags(FlagTaxesRaised)),
                    conditions: Conditions(required: Flags(FlagTaxesRaised))),

                Card("card_06_amnesty", "The Magistrate",
                    "The cells are overfull. An amnesty would empty them by week's end.",
                    Choice("Grant the amnesty", authority: -5, people: 10,
                        remove: Flags(FlagTaxesRaised)),
                    Choice("Refuse outright", authority: 5, people: -5)),

                Card("card_07_general_visit", "The General",
                    "The standing companies have not been paid in two seasons, your Majesty.",
                    Choice("Pay them first", authority: 5, security: 10,
                        add: Flags(FlagArmyFavoured)),
                    Choice("Let them wait", authority: -5, security: -5),
                    weight: 2),

                Card("card_08_peace_envoy", "A Foreign Envoy",
                    "My court proposes an accord. Neither of us profits from another winter of this.",
                    Choice("Sign the accord", people: 10, security: -5),
                    Choice("Send them home", people: -5, security: 5),
                    conditions: Conditions(forbidden: Flags(FlagArmyFavoured))),

                Card("card_09_bread_riots", "A City Warden",
                    "The bread queues turned into something else this morning. We need orders.",
                    Choice("Open the granaries", people: 15, wealth: -15),
                    Choice("Clear the square", authority: 10, people: -5, security: 5),
                    conditions: Conditions(ranges: Ranges(
                        new StatRange(StatType.People, StatBounds.Min, LowStatThreshold)))),

                Card("card_10_empty_vault", "The Treasurer",
                    "The vault echoes. I can show you, if you would rather see it than hear it.",
                    Choice("Melt the regalia", authority: -10, wealth: 15),
                    Choice("Borrow from abroad", security: -10, wealth: 10),
                    conditions: Conditions(ranges: Ranges(
                        new StatRange(StatType.Wealth, StatBounds.Min, LowStatThreshold)))),

                Card("card_11_spy_master", "The Spymaster",
                    "My people hear a great deal. Keeping them listening costs a great deal.",
                    Choice("Fund the network", security: 10, wealth: -10),
                    Choice("Cut the budget", security: -10, wealth: 10),
                    cooldown: 3),

                Card("card_12_festival", "The Master of Revels",
                    "A festival would lift the city's mood. It would also empty a wing of the vault.",
                    Choice("Hold the festival", people: 10, wealth: -10),
                    Choice("Cancel it", people: -10, wealth: 5),
                    cooldown: 5),

                Card("card_13_royal_wedding", "The Chamberlain",
                    "Two proposals of marriage have arrived. Both expect an answer this month.",
                    Choice("Marry for alliance", authority: 5, people: -5, security: 10),
                    Choice("Marry for love", authority: -10, people: 15),
                    oncePerRun: true),

                Card("card_14_plague", "The Court Physician",
                    "Three districts show the same fever. We have perhaps a week to decide.",
                    Choice("Quarantine the districts", people: -10, security: 10, wealth: -5),
                    Choice("Keep the roads open", people: -5, security: -10, wealth: 10),
                    oncePerRun: true),

                Card("card_15_inquisitor", "The Inquisitor",
                    "I ask only for permission to open an inquiry. The findings will follow.",
                    Choice("Permit the inquiry", authority: 5, people: -10),
                    Choice("Refuse the inquiry", authority: -5, people: 5),
                    forcedNext: "card_16_inquisitor_verdict"),

                Card("card_16_inquisitor_verdict", "The Inquisitor",
                    "The inquiry is closed and the verdict written. It awaits only your seal.",
                    Choice("Uphold the verdict", authority: 10, people: -10),
                    Choice("Overturn it", authority: -10, people: 10)),

                Card("card_17_ambassador", "The Ambassador",
                    "I carry a treaty in one hand and my leave-taking in the other. Choose.",
                    Choice("Offer the treaty", security: 5, wealth: -5,
                        forcedNext: "card_18_ambassador_accord"),
                    Choice("Dismiss the embassy", authority: 5, security: -5),
                    forcedNext: "card_19_ambassador_refusal"),

                Card("card_18_ambassador_accord", "The Ambassador",
                    "The accord is signed. My court will want to know what it bought them.",
                    Choice("Honour it fully", people: 10, security: 5, wealth: -10),
                    Choice("Honour the letter only", authority: 5, people: -5)),

                Card("card_19_ambassador_refusal", "The Marshal",
                    "The embassy has gone. Their escort rode east, not home.",
                    Choice("Muster the companies", security: 10, wealth: -10),
                    Choice("Wait and watch", security: -5, authority: -5)),

                Card("card_20_wandering_scholar", "A Wandering Scholar",
                    "I ask for a room, a lamp, and a year. In return, a history of your reign.",
                    Choice("Endow the work", authority: 5, wealth: -5),
                    Choice("Send him on", authority: -5, wealth: 5),
                    weight: 5)
            };

            // Sorted here so the generated catalogue has a stable asset diff between runs.
            cards.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            return cards;
        }

        /// <summary>One ending per statistic per boundary: eight in total.</summary>
        public static List<EndingDefinition> CreateEndings()
        {
            return new List<EndingDefinition>(8)
            {
                Ending(StatType.Authority, StatBoundary.Min, "The Crown Ignored",
                    "Your orders stopped leaving the palace. In time, so did you."),
                Ending(StatType.Authority, StatBoundary.Max, "The Unquestioned Throne",
                    "No one contradicted you again. No one told you anything again, either."),
                Ending(StatType.People, StatBoundary.Min, "The Gates Opened Inward",
                    "The crowd did not stop at the courtyard, and no one asked it to."),
                Ending(StatType.People, StatBoundary.Max, "Carried Through the Streets",
                    "They loved you past all governing, and governed themselves instead."),
                Ending(StatType.Security, StatBoundary.Min, "The Undefended Season",
                    "The walls held. The garrison did not."),
                Ending(StatType.Security, StatBoundary.Max, "The Watchful Realm",
                    "Every road was guarded, every guard was watched, and nothing moved at all."),
                Ending(StatType.Wealth, StatBoundary.Min, "The Echoing Vault",
                    "The last of the plate was weighed, sold, and not replaced."),
                Ending(StatType.Wealth, StatBoundary.Max, "The Gilded Account",
                    "The treasury outgrew the kingdom it was meant to serve.")
            };
        }

        public static string EndingId(StatType stat, StatBoundary boundary)
        {
            // Invariant casing: a culture-sensitive lowercase would reshape these IDs under a
            // Turkish locale.
            return string.Format(
                "ending_{0}_{1}",
                stat.ToString().ToLowerInvariant(),
                boundary.ToString().ToLowerInvariant());
        }

        // --- Construction helpers ------------------------------------------------

        private static CardDefinition Card(
            string id,
            string speaker,
            string bodyText,
            ChoiceDefinition left,
            ChoiceDefinition right,
            CardConditions conditions = null,
            int weight = CardDefinition.DefaultSelectionWeight,
            bool oncePerRun = false,
            int cooldown = 0,
            string forcedNext = "")
        {
            CardDefinition card = ScriptableObject.CreateInstance<CardDefinition>();
            card.name = id;
            card.SetAuthoringData(
                id,
                PlaceholderTag + " " + speaker,
                bodyText,
                left,
                right,
                conditions,
                weight,
                oncePerRun,
                cooldown,
                forcedNext);

            return card;
        }

        private static ChoiceDefinition Choice(
            string previewText,
            int authority = 0,
            int people = 0,
            int security = 0,
            int wealth = 0,
            string[] add = null,
            string[] remove = null,
            string forcedNext = "")
        {
            return new ChoiceDefinition(
                previewText,
                new StatDeltas(authority, people, security, wealth),
                add,
                remove,
                forcedNext);
        }

        private static EndingDefinition Ending(
            StatType stat,
            StatBoundary boundary,
            string title,
            string bodyText)
        {
            string id = EndingId(stat, boundary);

            EndingDefinition ending = ScriptableObject.CreateInstance<EndingDefinition>();
            ending.name = id;
            ending.SetAuthoringData(id, PlaceholderTag + " " + title, bodyText, stat, boundary);

            return ending;
        }

        private static CardConditions Conditions(
            string[] required = null,
            string[] forbidden = null,
            StatRange[] ranges = null)
        {
            return new CardConditions(required, forbidden, ranges);
        }

        private static string[] Flags(params string[] flags)
        {
            return flags;
        }

        private static StatRange[] Ranges(params StatRange[] ranges)
        {
            return ranges;
        }
    }
}
