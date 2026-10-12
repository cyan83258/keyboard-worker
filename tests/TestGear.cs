using System.IO;

namespace KeyboardWorker;

// The accessories the tests buy and wear: a folder of (empty) picture files, read the way the program reads its own.
public static class TestGear
{
    public const string Bow = "shop-bow_20", Pin = "shop-pin_15", Crown = "shop-crown_120", Glasses = "shop-face_glasses_35";
    public static void Use(string root)
    {
        string folder = Path.Combine(root, "test-accessories"); Directory.CreateDirectory(folder);
        foreach (string file in new[] { "bow_20.png", "pin_15.png", "crown_120.png", "face_glasses_35.png" }) File.WriteAllBytes(Path.Combine(folder, file), new byte[] { 1 });
        File.WriteAllText(Path.Combine(folder, "accessories.json"), "[ { \"file\": \"crown_120.png\", \"level\": 3 }, { \"file\": \"face_glasses_35.png\", \"level\": 2 } ]");
        ShopGear.Load(folder);
    }
}