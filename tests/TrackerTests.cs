using System;
using System.Collections.Generic;
using System.Linq;

namespace KeyboardWorker;

public static class TrackerTests
{
    public static int Run()
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        // Program in front
        var options = new AwarenessOptions { CommentMinutes = 20 }; var apps = new AppTracker(7);
        Check(!apps.InFront && apps.Current.Title == "" && !apps.RemarkDue(options, 999999), "Nothing is tracked before a program comes to the front");
        apps.Track("eldenring", @"D:\Games\ELDEN RING\eldenring.exe", options, 1000);
        Check(apps.InFront && apps.GameInFront && apps.Current.Kind == "game" && apps.Summary.Contains("게임"), "A program in front is recognised with its kind");
        Check(!apps.RemarkDue(options, 30999) && apps.RemarkDue(options, 31000), "A remark waits until the program has been in front for half a minute");
        Check(apps.Remarked(31000) == 7 && !apps.RemarkDue(options, 400000), "After a remark the same program is left alone");
        apps.Track("KeyboardWorker", "", options, 50000);
        Check(!apps.InFront && !apps.GameInFront && apps.Current.Process == "eldenring" && !apps.RemarkDue(options, 99999999), "This app's own windows do not replace the tracked program, and nothing is said while they are in front");
        apps.Track("Code", @"C:\Tools\Code.exe", options, 60000);
        Check(apps.Current.Kind == "work" && !apps.RemarkDue(options, 200000) && apps.RemarkDue(options, 331000), "Another program is remarked on only after the five-minute gap");
        Check(apps.Remarked(331000) == 8, "Ready-made lines rotate");
        apps.Track("eldenring", @"D:\Games\ELDEN RING\eldenring.exe", options, 700000);
        Check(!apps.RemarkDue(options, 1000000) && apps.RemarkDue(options, 31000 + 20 * 60000), "The same program is remarked on again after the chosen number of minutes");
        Check(!apps.RemarkDue(options with { Comments = false }, 99999999) && !apps.RemarkDue(options with { Apps = false }, 99999999), "Remarks follow the on/off settings");
        apps.Track("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", options, 5000000);
        Check(apps.Current.Kind == "other" && !apps.RemarkDue(options, 99999999), "Programs that are neither games nor work are never remarked on");
        apps.Track("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", options with { Games = "chrome" }, 5000001);
        Check(apps.GameInFront, "Changing the user's lists reclassifies the program in front");

        // Spoken lines
        var history = new LineHistory(); var at = new DateTime(2026, 10, 11, 9, 0, 0);
        history.Remember("모모", "안", at); history.Remember("모모", "안녕, 오늘", at); history.Remember("모모", "안녕, 오늘도 힘내!", at);
        Check(history.Items.Count == 1 && history.Items[0].Text == "안녕, 오늘도 힘내!", "A streaming reply is kept as one line");
        history.Remember("모모", "안녕", at); history.Remember("루미", "안녕, 오늘도 힘내!", at); history.Remember("모모", "  ", at);
        Check(history.Items.Count == 2 && history.Items[1].Speaker == "루미", "A shorter repeat is ignored, another speaker is a new line, blanks are skipped");
        for (int n = 0; n < 80; n++) history.Remember("모모", "대사 " + n + ".", at);
        Check(history.Items.Count == LineHistory.Limit && history.Items[^1].Text == "대사 79." && history.Items[0].Text == "대사 30.", "Only the newest lines are kept");

        // Turns between two pets
        var rolls = new Queue<int>(new[] { 69, 29, 30, 70, 0 }); var turns = new SpeakerTurns(_ => rolls.Dequeue());
        Check(turns.NextIsSecond() && turns.NextIsSecond() && !turns.NextIsSecond() && !turns.NextIsSecond() && turns.NextIsSecond(), "The pet that did not speak last is favoured 70 to 30");
        var real = new SpeakerTurns(); int second = Enumerable.Range(0, 2000).Count(_ => real.NextIsSecond());
        Check(second > 800 && second < 1200, "Over time both pets speak about equally often");
        return count;
    }
}
