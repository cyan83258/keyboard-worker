using System;
using System.Text.Json;
using System.Windows;

namespace KeyboardWorker;

public static class BubbleSpotTests
{
    public static int Run()
    {
        int count = 0; void Check(bool result, string name) { if (!result) throw new Exception(name); count++; }
        var pet = new Rect(500, 400, 240, 280); var screen = new Rect(0, 0, 1920, 1040);
        var above = BubbleSpot.From(new Rect(420, 250, 300, 100), pet);
        Check(above == new BubbleSpot(-80, -50, true), "A bubble dragged above the pet is pinned by its bottom edge");
        Check(above.Place(new Size(300, 100), pet.TopLeft, screen) == new Point(420, 250), "A dragged bubble returns to the same place");
        Check(above.Place(new Size(300, 180), pet.TopLeft, screen) == new Point(420, 170), "Longer text above the pet grows upward");
        var below = BubbleSpot.From(new Rect(560, 700, 300, 100), pet);
        Check(!below.FromBottom && below.Place(new Size(300, 180), pet.TopLeft, screen) == new Point(560, 700), "Longer text below the pet grows downward");
        Check(above.Place(new Size(300, 100), new Point(900, 600), screen) == new Point(820, 450), "The bubble follows its pet");
        Check(above.Place(new Size(300, 100), new Point(20, 30), screen) == new Point(0, 0) && below.Place(new Size(300, 100), new Point(1800, 900), screen) == new Point(1620, 940), "The bubble stays on the screen");
        var saved = new Preferences { BubbleSpot = above };
        var loaded = JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(saved))!;
        Check(loaded.BubbleSpot == above, "Dragged bubble place survives saving");
        Check(JsonSerializer.Deserialize<Preferences>("{}")!.BubbleSpot == null, "Bubbles are placed automatically by default");
        saved.CompanionBubbleSpot = new BubbleSpot(double.NaN, 0, false);
        saved.Normalize();
        Check(saved.BubbleSpot == above && saved.CompanionBubbleSpot == null, "A broken saved place falls back to automatic");
        return count;
    }
}
