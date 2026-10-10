using Festival.Simulation;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>One set as the paper sees it: the record, with the act, genre and stage named.</summary>
internal sealed record ReviewSet(PerformanceRecord Record, string Act, string Genre, string Stage);

/// <summary>A genre's standing with its fans before and after the festival.</summary>
internal sealed record SceneMove(string Genre, int Before, int After);

/// <summary>Everything the review is written from: the result, the sets and what happened around the field.</summary>
internal sealed record ReviewFacts(FestivalResult Result, int Tier, int Sold, bool Rain, int Showers, bool Downpour, bool Muddy,
    IReadOnlyList<ReviewSet> Sets, IReadOnlyDictionary<ulong, string> Names,
    bool WaspSting, bool LooFumes, bool Drink, bool Distress,
    int CowsEscaped, int ChewedCables, int StuckInLoo, int BrokenTaps, int LavSuckerCalls, bool GeneratorScare,
    bool EarlierTragedy, (int Before, int After)? Reputation, IReadOnlyList<SceneMove> Scenes)
{
    /// <summary>What lifted and what dragged the crowd's mood most, in words; empty when the day wasn't watched (a loaded save).</summary>
    public string[] Lifts { get; init; } = [];
    public string[] Grumbles { get; init; } = [];
}

/// <summary>The review as printed: headline, standfirst, body, verdict and scene buzz.</summary>
internal sealed record Review(string Issue, string Headline, string Standfirst, string Byline, string[] Paragraphs, string Verdict,
    (string Line, string Who)? PullQuote, (string Words, string Numbers, bool Up)[] Buzz, string? Standing);

/// <summary>
/// The Wittering Echo's review of the day, written from the festival's facts: never a disaster that didn't happen, never
/// praise for one that did. Headlines come from a table per outcome, picked by a hash of the result so a reload reads
/// the same.
/// </summary>
internal static class FestivalReview
{
    public const string Masthead = "The Wittering Echo";
    public const string Motto = "Lower Wittering’s paper of record (and records)";
    public const string Reporter = "Clara Nett, music correspondent";
    public const string Photographer = "Kay Dence";
    public const string FestivalName = "Lower Wittering Festival";

    /// <summary>Best to worst, for picking the set of the day and the low point.</summary>
    public static int Rank(string reaction) => reaction switch
    {
        GigRules.Enthusiastic => 0, GigRules.Warm => 1, GigRules.Polite => 2, GigRules.Smattering => 3,
        GigRules.EmptyField => 4, GigRules.CutShort => 5, _ => 6,
    };

    /// <summary>The sets in order of how well they went: reaction, then fewest hiccups, then the bigger crowd.</summary>
    public static IEnumerable<PerformanceRecord> BestFirst(IEnumerable<PerformanceRecord> records) => records
        .OrderBy(record => Rank(record.Reaction)).ThenBy(record => record.Hiccups.Length).ThenByDescending(record => record.PeakCrowd)
        .ThenBy(record => record.ScheduledStartTick);

    public static Review Write(ReviewFacts f)
    {
        var r = f.Result;
        var seed = unchecked((ulong)r.SatisfactionTotal * 2_654_435_761UL ^ (ulong)r.Tick * 40_503UL ^ (ulong)(f.Tier * 97 + r.Attempt * 13));
        string Pick(params string[] options) => options[(int)(seed % (ulong)options.Length)];

        var played = f.Sets.Where(set => set.Record.StartedTick >= 0).ToArray();
        var best = played.Length == 0 ? null : f.Sets.First(set => set.Record == BestFirst(played.Select(s => s.Record)).First());
        var lows = Lows(f.Sets, best).ToArray();
        var powerCuts = f.Sets.Count(set => set.Record.Hiccups.Contains(GigRules.PowerCut));
        var stars = r.Stars;
        var good = stars >= 4;
        var bad = stars <= 2;

        // The headline goes to the day's biggest story, good or bad.
        string headline;
        if (stars is null) headline = Pick("The Sound of Silence", "Nobody Came to the Party", "Empty Field, Silent Night");
        else if (bad && powerCuts > 0) headline = Pick("Unplugged — and Not in a Good Way", "Lights Out at Lower Wittering", "Power Ballad, Minus the Power");
        else if (bad && played.Length == 0 && f.Sets.Count > 0) headline = Pick("No Show, No Glory", "The Band Played On? Not Quite");
        else if (bad && r.Fights >= 3) headline = Pick("Bad Vibrations as Tempers Flare", "Festival Hits a Bum Note as Fists Fly");
        else if (bad && r.MedicalCollapses >= 2 && !f.Rain) headline = Pick("Some Like It Hot — Not These Punters", "Heatwave Takes Its Toll on the Field");
        else if (bad && f.CowsEscaped > 0) headline = Pick("Udder Chaos in Lower Wittering", "Herd It Through the Grapevine: Cows Crash Festival");
        else if (bad && f.Rain) headline = f.Muddy ? Pick("Stuck in the Mud", "Wittering Washout") : Pick("Rain Stops Play", "Here Comes the Rain Again", "Wittering Washout");
        else if (bad && f.StuckInLoo + (f.LooFumes ? 1 : 0) > 0) headline = Pick("Loo-sing Battle in Lower Wittering", "Something in the Air at Lower Wittering");
        else if (bad && lows.Any(low => low.Set.Record.Reaction == GigRules.NoShow)) headline = Pick("No Show, No Glory", "Waiting for the Band");
        else if (bad) headline = stars == 1 ? Pick("Festival Hits a Bum Note", "Face the Music: Festival Flops", "Feedback Loud and Clear")
            : Pick("Festival Falls Flat", "Out of Tune in Lower Wittering", "An Off-Key Day in the Field");
        else if (good && f.Rain) headline = f.Muddy ? Pick("Mud, Sweat and Beers", "Singing in the Rain") : Pick("Singing in the Rain", "Rain Can’t Dampen Wittering Spirits");
        else if (good && powerCuts > 0) headline = Pick("Power Cut Can’t Pull the Plug on Wittering", "Unplugged, Undaunted");
        else if (good && f.CowsEscaped > 0) headline = Pick("Moo-sic to Our Ears", "Herd It Through the Grapevine: Wittering Rocks");
        else if (good && best?.Record.Reaction == GigRules.Enthusiastic)
            headline = Pick($"Wittering Goes Wild for {best.Act}", $"Hats Off to {best.Act}", $"Field Day for {best.Act}", $"All Hail {best.Act}");
        else if (stars == 5) headline = Pick("Wittering Rocks!", "Encore! Encore!", "A Field Day for Music Fans");
        else if (good) headline = Pick("Festival Hits the Right Notes", "A Sound Day Out in Wittering", "Wittering Finds Its Groove");
        else if (f.Rain) headline = Pick("Grey Skies, Mixed Reviews", "Sunshine and Showers — Mostly Showers");
        else if (f.CowsEscaped > 0) headline = Pick("Cows and Chords: A Mixed Day", "Moo-ving Parts: A Festival of Two Halves");
        else headline = Pick("A Bit of a Mixed Tape", "Festival Finds Its Rhythm — Eventually", "Middle of the Road, Middle of a Field");

        var edition = Ordinal(f.Tier);
        var standfirst = stars is null
            ? $"The tickets were sold and the bands were booked, but nobody came through the gate of the {edition} {FestivalName}."
            : $"{Capitalise(Count(r.GuestCount, "punter"))}, {Count(played.Length, "set")} and {Weather(f)}: our reviewer gives the {edition} {FestivalName} {Words(stars.Value)} out of five.";

        var paragraphs = new List<string> { Day(f, played.Length), Music(f, best, lows) };
        if (Field(f) is { } field) paragraphs.Add(field);
        var verdict = stars switch
        {
            5 => "A near-perfect day out. If the organisers can do this again, book early.",
            4 => "A cracking day with a few rough edges. We’ll be back.",
            3 => "Pleasant enough, but the organisers have work to do before next time.",
            2 => f.Rain ? "A disappointing day, and not only because of the rain. Punters went home grumbling." : "A disappointing day. Punters went home grumbling.",
            1 => "A day most punters will want to forget.",
            _ => "None to give. You can’t review a party nobody came to.",
        };
        if (stars <= 3 && f.Grumbles.Length > 0) verdict += $" Punters grumbled most about {List(f.Grumbles)}.";
        else if (stars >= 4 && f.Lifts.Length > 0) verdict += $" What sent them home happy: {List(f.Lifts)}.";

        // The pull quote: a happy punter on a good day, an unhappy one on a bad day.
        var quoteSet = (bad ? lows.Select(low => low.Set).Where(set => set.Record.Quotes.Length > 0) : Array.Empty<ReviewSet>())
            .Concat(best is not null && best.Record.Quotes.Length > 0 ? [best] : []).FirstOrDefault();
        var pull = quoteSet is { } q && QuoteOf(q, f.Names, 0) is { } said ? said : ((string, string)?)null;

        string[] ups = ["{0} fans are already talking about it", "Word has reached the {0} crowd", "{0} fans are buzzing", "The {0} scene is taking notice"];
        string[] downs = ["{0} fans were unimpressed", "The {0} crowd is giving it a wide berth", "{0} fans won’t be rushing back"];
        var buzz = f.Scenes.Where(move => move.After != move.Before).OrderByDescending(move => Math.Abs(move.After - move.Before)).Select((move, index) =>
        {
            var change = move.After - move.Before;
            var phrase = change > 0 ? ups[index % ups.Length] : downs[index % downs.Length];
            var words = Capitalise(string.Format(phrase, phrase.StartsWith("{0}") ? move.Genre : move.Genre.ToLowerInvariant()));
            return (words, $"{(change > 0 ? "+" : "−")}{Math.Abs(change)}", change > 0);
        }).ToArray();
        var standing = f.Reputation is { } rep && rep.After != rep.Before
            ? $"Festival reputation {(rep.After > rep.Before ? "up" : "down")}, {rep.Before} → {rep.After}" : null;

        var issue = $"No. {4_800 + f.Tier * 7 + r.Attempt:#,0} · Monday · 60p";
        return new(issue, headline, standfirst, $"By {Reporter}", paragraphs.ToArray(), verdict, pull, buzz, standing);
    }

    private static string Day(ReviewFacts f, int played)
    {
        var r = f.Result;
        var text = f.EarlierTragedy ? "The organisers reopened their gates after the tragedy that ended their previous weekend. " : "";
        text += $"The {Ordinal(f.Tier)} {FestivalName} {(f.Tier == 1 ? "opened" : "returned to the farm")} " +
            (f.Rain ? $"under grey skies, with {Count(f.Showers, "heavy shower")}{(f.Downpour ? " and one proper downpour" : "")} across the day. "
                : "under a blazing sun, in the middle of a heatwave. ");
        if (f.Tier >= GameSession.PondStageFromTier && f.Sets.Any(set => set.Record.StageId == FestivalStages.PondId))
            text += "New this year was a second stage down by the farm pond. ";
        text += r.GuestCount == 0 ? $"All {f.Sold} tickets were sold, yet nobody turned up to use one." :
            r.GuestCount >= f.Sold ? $"All {f.Sold} ticket-holders made the trip" + (played > 0 ? $", and {Count(played, "act")} {(played == 1 ? "was" : "were")} there to play for them." : ".") :
            $"{r.GuestCount} of the {f.Sold} ticket-holders made it through the gate.";
        return text;
    }

    private static string Music(ReviewFacts f, ReviewSet? best, IReadOnlyList<(ReviewSet Set, string Words)> lows)
    {
        if (f.Sets.Count == 0) return "Not a note was played all day.";
        if (f.Result.GuestCount == 0)
            return best is null ? "Not one act made it onto the stage either." : $"{Capitalise(Count(f.Sets.Count(set => set.Record.StartedTick >= 0), "act"))} played on regardless, to nobody but the stewards.";
        if (best is null) return "Not one act made it onto the stage all day. " + string.Join(" ", lows.Select(low => low.Words));
        var b = best.Record;
        var opening = Rank(b.Reaction) <= 1 ? "The pick of the day was" : "If there was a highlight, it was";
        var text = $"{opening} {best.Act}, whose {best.Genre.ToLowerInvariant()} set on the {best.Stage} drew {Count(b.PeakCrowd, "punter")} at its height" + b.Reaction switch
        {
            GigRules.Enthusiastic => " and ended with the field roaring for more.",
            GigRules.Warm => " and went down warmly.",
            GigRules.Polite => ", though the applause was more polite than passionate.",
            GigRules.Smattering => ", though only a smattering stayed to the end.",
            GigRules.EmptyField => ", which is to say they played to a near-empty field.",
            _ => ", before it was cut short.",
        };
        // The pull quote takes the first line on a good day; the body takes another.
        if ((QuoteOf(best, f.Names, 1) ?? (f.Result.Stars <= 2 ? QuoteOf(best, f.Names, 0) : null)) is { } said) text += $" As {said.Who} put it: “{said.Line}”";
        if (lows.Count > 0) text += " " + string.Join(" ", lows.Select(low => low.Words));
        return text;
    }

    /// <summary>The day's low points among the sets, worst first, at most two, never the best set's own triumph.</summary>
    private static IEnumerable<(ReviewSet Set, string Words)> Lows(IReadOnlyList<ReviewSet> sets, ReviewSet? best)
    {
        var found = new List<(int Weight, ReviewSet Set, string Words)>();
        foreach (var set in sets)
        {
            var r = set.Record;
            // The best set's own reaction is already told; only its hiccups count against it.
            var reaction = set == best ? GigRules.Polite : r.Reaction;
            string? words = reaction switch
            {
                GigRules.NoShow => $"{set.Act} never made it onto the {set.Stage}, leaving {Count(r.PeakCrowd, "punter")} waiting.",
                GigRules.CutShort when r.Hiccups.Contains(GigRules.PowerCut) => $"The power went during {set.Act}’s set and it never recovered.",
                GigRules.CutShort => $"{set.Act}’s set was cut short.",
                _ when r.Hiccups.Contains(GigRules.PowerCut) => $"The power cut out during {set.Act}’s set{(r.Hiccups.Contains(GigRules.Boos) ? ", and the crowd booed the silence" : "")}.",
                _ when r.Hiccups.Contains(GigRules.Collapse) => $"A member of {set.Act} collapsed during their set.",
                GigRules.EmptyField => $"{set.Act} played to a near-empty field.",
                _ when r.Hiccups.Contains(GigRules.Stopped) => $"{set.Act} stopped mid-set{(r.Hiccups.Contains(GigRules.Boos) ? " to boos" : "")}.",
                GigRules.Smattering => $"Only a smattering turned out for {set.Act}.",
                _ when r.Hiccups.Contains(GigRules.PoorSoundHiccup) => $"The sound for {set.Act} was muddy at best.",
                _ when r.Hiccups.Contains(GigRules.Late) => $"{set.Act} were late on stage.",
                _ => null,
            };
            if (words is null) continue;
            found.Add((Rank(r.Reaction) * 10 + r.Hiccups.Length, set, words));
        }
        return found.OrderByDescending(item => item.Weight).ThenBy(item => item.Set.Record.ScheduledStartTick).Take(2).Select(item => (item.Set, item.Words));
    }

    private static string? Field(ReviewFacts f)
    {
        var r = f.Result;
        var parts = new List<string>();
        if (r.MedicalCollapses > 0)
        {
            var causes = new List<string>();
            if (f.Distress && !f.Rain) causes.Add("the heat");
            if (f.WaspSting) causes.Add("a wasp sting");
            if (f.Drink) causes.Add("one too many");
            if (f.LooFumes) causes.Add("fumes from a jammed loo");
            parts.Add($"First aiders treated {Count(r.MedicalCollapses.Value, "punter")} who collapsed" + (causes.Count > 0 ? $"; among the causes, {List(causes)}." : "."));
        }
        else if (r.MedicalCollapses == 0 && !f.Rain && r.GuestCount > 0) parts.Add("Despite the heat, nobody needed carrying to first aid.");
        if (r.Fights > 0) parts.Add($"Stewards broke up {Count(r.Fights.Value, "scuffle")}.");
        if (f.CowsEscaped > 0)
            parts.Add($"{Capitalise(Count(f.CowsEscaped, "cow"))} from the neighbouring field got in and wandered among the crowd" +
                (f.ChewedCables > 0 ? $", one of them chewing through a power cable." : "."));
        if (f.StuckInLoo > 0) parts.Add($"{Capitalise(Count(f.StuckInLoo, "punter"))} got stuck in the portable loos.");
        if (f.LavSuckerCalls > 0) parts.Add($"Dav’s Lav-Sucker was called in {Times(f.LavSuckerCalls)} to empty the toilets.");
        if (f.BrokenTaps > 0) parts.Add($"{Capitalise(Count(f.BrokenTaps, "water tap"))} broke.");
        if (f.GeneratorScare) parts.Add("The farm generator gave the organisers a scare.");
        if (f.Rain && f.Muddy) parts.Add("By the evening, much of the field had been churned to mud.");
        if (r.BeersFinished > 0) parts.Add($"Punters got through {Count(r.BeersFinished.Value, "pint")} at the bar.");
        var trouble = r.MedicalCollapses > 0 || r.Fights > 0 || f.CowsEscaped > 0 || f.StuckInLoo > 0 || f.BrokenTaps > 0 || f.GeneratorScare;
        if (!trouble && r.Fights == 0 && r.GuestCount > 0) parts.Insert(0, "Stewards reported a trouble-free day.");
        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    private static (string Line, string Who)? QuoteOf(ReviewSet set, IReadOnlyDictionary<ulong, string> names, int index)
    {
        if (index >= set.Record.Quotes.Length) return null;
        var quote = set.Record.Quotes[index];
        var line = GigQuotes.Line(set.Record.Reaction, quote);
        if (line.Length == 0) return null;
        var name = names.TryGetValue(quote.GuestId, out var who) ? who : "one punter";
        var genre = set.Genre.ToLowerInvariant();
        return (line, quote.Fan ? $"{name}, {("aeiou".Contains(genre[0]) ? "an" : "a")} {genre} fan" : name);
    }

    private static string Weather(ReviewFacts f) => f.Rain ? (f.Downpour ? "a downpour" : "the rain") : "a heatwave";
    private static string Ordinal(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", 4 => "fourth", 5 => "fifth", _ => $"{n}th" };
    private static string Words(int stars) => stars switch { 1 => "one star", 2 => "two stars", 3 => "three stars", 4 => "four stars", _ => "five stars" };
    private static string Times(int n) => n switch { 1 => "once", 2 => "twice", _ => $"{n} times" };
    private static string Count(int n, string noun) => (n switch
    {
        0 => "no", 1 => "one", 2 => "two", 3 => "three", 4 => "four", 5 => "five", 6 => "six", 7 => "seven", 8 => "eight", 9 => "nine", 10 => "ten",
        _ => n.ToString(),
    }) + " " + (n == 1 ? noun : noun + "s");
    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    private static string List(IReadOnlyList<string> items) => items.Count == 1 ? items[0] : string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
}
