using System;
using System.Collections.Generic;

namespace KeyboardWorker;

public sealed record SpokenLine(DateTime At, string Speaker, string Text);

// Lines shown in speech bubbles during this run (newest last), for the "지난 대사" window. Never written to disk.
public sealed class LineHistory
{
    public const int Limit = 50;
    readonly List<SpokenLine> lines = new();
    public IReadOnlyList<SpokenLine> Items => lines;
    public void Remember(string speaker, string text, DateTime at)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // A reply that is still streaming grows in place instead of filling the list with its beginnings.
        if (lines.Count > 0 && lines[^1].Speaker == speaker && (text.StartsWith(lines[^1].Text, StringComparison.Ordinal) || lines[^1].Text.StartsWith(text, StringComparison.Ordinal)))
        {
            if (text.Length >= lines[^1].Text.Length) lines[^1] = lines[^1] with { Text = text };
            return;
        }
        lines.Add(new(at, speaker, text)); if (lines.Count > Limit) lines.RemoveAt(0);
    }
}

// With two pets out, who says the next nudge or random line. The one who did not speak last is the more likely one,
// so they take turns without a fixed pattern.
public sealed class SpeakerTurns
{
    readonly Func<int, int> roll; bool secondSpokeLast;
    public SpeakerTurns(Func<int, int>? roll = null) { this.roll = roll ?? Random.Shared.Next; }
    // True when the second pet speaks next.
    public bool NextIsSecond() { secondSpokeLast = roll(100) < (secondSpokeLast ? 30 : 70); return secondSpokeLast; }
}
