using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KeyboardWorker;

// What a character page draws its talk on. The page decides what is said; a surface decides how it looks:
// StageView (standing picture and dialogue box) or MessengerView (text messages). A new layout is one more class like these.
public interface IStageSurface
{
    FrameworkElement Element { get; }
    // True when the surface shows the whole thread itself and scrolls; false when it shows one line at a time.
    bool Scrolls { get; }
    bool LowPower { set; }
    string EmptyNote { set; }
    void SetFont(FontFamily family, double size);
    void Show(string background, string standing);
    // Decoded pictures are dropped (they were changed in the page settings).
    void Forget();
    // The talk so far with the character that was just opened.
    void Load(string name, IReadOnlyList<ChatTurn> thread);
    // The user's message went out and the answer is awaited.
    void Asked(string name, string message);
    void Streaming(string name, string partial);
    // A complete line: the answer that just came, or an older one being looked at again.
    void Present(string name, string text, bool typed);
    // No answer came: what Asked put on screen is taken back.
    void Failed();
}

static class StageSurfaces
{
    public static IStageSurface For(string layout) => layout == StageMode.Messenger ? new MessengerView() : new StageView();
}
