using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KeyboardWorker;

// One step of an answer on a character page: what the character does or thinks (written in brackets by the model) and what it says.
public sealed record StageBeat(string Narration, string Line);

// Takes an answer apart for the stage: stage directions out of the brackets, and long speech into pages the reader clicks through.
public static class StageScript
{
    static bool Opens(char c) => c is '(' or '（';
    static bool Closes(char c) => c is ')' or '）';
    // "(고개를 든다) 왔군. (작게 웃으며) 앉아라." -> [(고개를 든다, 왔군.), (작게 웃으며, 앉아라.)]
    // A short bracket glued to a word, like "오늘(월요일)", is part of the sentence and stays where it is.
    public static List<StageBeat> Parse(string text)
    {
        var beats = new List<StageBeat>(); var speech = new StringBuilder(); var aside = new StringBuilder(); string pending = "";
        void Speak()
        {
            string line = Regex.Replace(speech.ToString(), @"[ \t]+", " ").Trim(); speech.Clear(); if (line.Length == 0) return;
            beats.Add(new(pending, line)); pending = "";
        }
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!Opens(c)) { speech.Append(c); continue; }
            int depth = 1, end = i + 1; while (end < text.Length && depth > 0) { if (Opens(text[end])) depth++; else if (Closes(text[end])) depth--; end++; }
            string inside = text[(i + 1)..(depth == 0 ? end - 1 : end)].Trim();
            bool glued = i > 0 && !char.IsWhiteSpace(text[i - 1]) && !".!?…~\"'”’」』,".Contains(text[i - 1]) && inside.Length <= 15 && depth == 0;
            if (glued) { speech.Append(text, i, end - i); i = end - 1; continue; }
            Speak(); if (inside.Length > 0) pending = pending.Length == 0 ? inside : pending + " · " + inside;
            i = end - 1;
        }
        Speak();
        // A direction after the last line belongs to that line; one with no speech at all stands alone.
        if (pending.Length > 0) { if (beats.Count > 0 && beats[^1].Narration.Length == 0) beats[^1] = beats[^1] with { Narration = pending }; else beats.Add(new(pending, "")); }
        return beats;
    }
    // The speech alone, for places that show no directions.
    public static string Speech(string text) => string.Join("\n", Parse(text).Where(b => b.Line.Length > 0).Select(b => b.Line));

    static readonly Regex sentenceEnd = new(@"(?<=[.!?…。！？~][""'”’」』]*)\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
    // Splits speech into pages of about `budget` characters (two lines of the dialogue box), breaking between sentences.
    // Each page keeps the direction of its beat. A short answer is one page.
    public static List<StageBeat> Pages(IReadOnlyList<StageBeat> beats, int budget)
    {
        budget = Math.Max(20, budget); var pages = new List<StageBeat>();
        foreach (var beat in beats)
        {
            if (beat.Line.Length == 0) { pages.Add(new(beat.Narration, "……")); continue; }
            var page = new StringBuilder(); int before = pages.Count;
            void Turn() { if (page.Length > 0) { pages.Add(new(beat.Narration, page.ToString().Trim())); page.Clear(); } }
            foreach (string paragraph in beat.Line.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                foreach (string sentence in sentenceEnd.Split(paragraph).Where(s => s.Length > 0))
                {
                    string rest = sentence;
                    // A sentence longer than a page is cut at a space near the end of the page.
                    while (rest.Length > budget)
                    {
                        Turn(); int cut = rest.LastIndexOf(' ', budget - 1, budget / 2); if (cut <= 0) cut = budget;
                        pages.Add(new(beat.Narration, rest[..cut].Trim())); rest = rest[cut..].TrimStart();
                    }
                    if (page.Length > 0 && page.Length + 1 + rest.Length > budget) Turn();
                    if (rest.Length > 0) { if (page.Length > 0 && page[^1] != '\n') page.Append(' '); page.Append(rest); }
                }
                // A new paragraph starts on a new line of the same page when it fits, otherwise on the next page.
                if (page.Length > 0) page.Append('\n');
            }
            Turn(); if (pages.Count == before) pages.Add(new(beat.Narration, "……"));
        }
        return pages;
    }
}
