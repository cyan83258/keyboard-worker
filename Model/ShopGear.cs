using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyboardWorker;

// Accessories that come with the program as pictures and are bought in the shop with coins: the PNG / JPG files in the
// "accessories" folder next to the program (Assets\Accessories in the source).
//   * A file alone is enough. Its name says what it is: "head_고양이 귀_300.png" = worn on the head, called 고양이 귀, 300 coins.
//     The first part may be head / face / neck / body / effect and the last part a price; both may be left out (head, 300).
//   * "accessories.json" in the same folder may say more about a file - above all where the picture sits on the character:
//     [ { "file": "head_고양이 귀_300.png", "name": "고양이 귀", "slot": "head", "price": 300, "level": 1, "description": "쫑긋한 귀",
//         "width": 42, "x": 0, "y": -6, "rotation": 0, "behind": false } ]
//     width is a share of the character's width (5-200); x / y move the centre from the slot's anchor, in percent.
// They are drawn like the user's own gear, but cannot be edited or deleted from inside the program.
public static class ShopGear
{
    public const int StandardPrice = 300, Max = 200;
    public sealed class Entry
    {
        [JsonPropertyName("file")] public string File { get; set; } = "";
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("slot")] public string? Slot { get; set; }
        [JsonPropertyName("price")] public int? Price { get; set; }
        [JsonPropertyName("level")] public int? Level { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("width")] public double? Width { get; set; }
        [JsonPropertyName("x")] public double? X { get; set; }
        [JsonPropertyName("y")] public double? Y { get; set; }
        [JsonPropertyName("rotation")] public double? Rotation { get; set; }
        [JsonPropertyName("behind")] public bool? Behind { get; set; }
    }
    // How each is drawn, and how each is sold; the same order, the same ids ("shop-…").
    public static IReadOnlyList<CustomGear> Items { get; private set; } = Array.Empty<CustomGear>();
    public static IReadOnlyList<ShopItem> Catalog { get; private set; } = Array.Empty<ShopItem>();
    static ShopGear() => Load(Path.Combine(AppContext.BaseDirectory, "accessories"));
    public static bool Has(string id) => id.StartsWith("shop-", StringComparison.Ordinal);
    // "head_고양이 귀_300" -> (head, 고양이 귀, 300)
    public static (string Slot, string Name, int Price) FromName(string file)
    {
        var parts = Path.GetFileNameWithoutExtension(file).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); string slot = "head"; int price = StandardPrice;
        if (parts.Count > 1 && CustomGear.Slots.Contains(parts[0].ToLowerInvariant())) { slot = parts[0].ToLowerInvariant(); parts.RemoveAt(0); }
        if (parts.Count > 1 && int.TryParse(parts[^1], out int coins) && coins >= 0) { price = coins; parts.RemoveAt(parts.Count - 1); }
        string name = string.Join(" ", parts).Trim(); return (slot, name.Length == 0 ? "장식" : name, price);
    }
    // Reads the folder again (at start, and by the tests with a folder of their own).
    public static void Load(string folder)
    {
        var gear = new List<CustomGear>(); var sold = new List<ShopItem>();
        try
        {
            if (Directory.Exists(folder))
            {
                var notes = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase); string list = Path.Combine(folder, "accessories.json");
                if (File.Exists(list) && new FileInfo(list).Length < 1024 * 1024)
                    try { foreach (var entry in JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(list), new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) ?? new()) if (entry?.File is { Length: > 0 }) notes[Path.GetFileName(entry.File)] = entry; }
                    catch (JsonException) { }   // a broken list only loses the fine placement; the files are still sold by their names
                foreach (string file in Directory.EnumerateFiles(folder).Where(f => Path.GetExtension(f).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(Max))
                {
                    var (slot, name, price) = FromName(file); notes.TryGetValue(Path.GetFileName(file), out var note);
                    if (note?.Slot is { } chosen && CustomGear.Slots.Contains(chosen.ToLowerInvariant())) slot = chosen.ToLowerInvariant();
                    string id = "shop-" + Path.GetFileNameWithoutExtension(file).ToLowerInvariant(); if (gear.Any(g => g.Id == id)) continue;
                    var item = new CustomGear { Id = id, Name = SafeFiles.OneLine(note?.Name, 40) is { Length: > 0 } given ? given : name, Slot = slot, ImagePath = file, WidthPercent = note?.Width ?? 35, OffsetX = note?.X ?? 0, OffsetY = note?.Y ?? 0, Rotation = note?.Rotation ?? 0, Behind = note?.Behind ?? false }.Normalized();
                    gear.Add(item); sold.Add(new ShopItem(id, item.Name, slot, Math.Clamp(note?.Price ?? price, 0, 1000000), Math.Clamp(note?.Level ?? 1, 1, 100), SafeFiles.OneLine(note?.Description, 80) is { Length: > 0 } told ? told : SlotName(slot) + " 장식"));
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        Items = gear; Catalog = sold;
    }
    public static string SlotName(string slot) => slot switch { "face" => "얼굴", "neck" => "목", "body" => "몸", "effect" => "오라", _ => "머리" };
    // The lines of accessories.json for a piece of gear the user placed by hand in the program: copied from the gear window, pasted into the file.
    public static string Describe(CustomGear gear, string file) =>
        JsonSerializer.Serialize(new Entry { File = file, Name = gear.Name, Slot = gear.Slot, Price = StandardPrice, Level = 1, Description = "", Width = Math.Round(gear.WidthPercent, 2), X = Math.Round(gear.OffsetX, 2), Y = Math.Round(gear.OffsetY, 2), Rotation = Math.Round(gear.Rotation, 2), Behind = gear.Behind },
            new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}
