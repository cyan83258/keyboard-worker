namespace KeyboardWorker;

public sealed record MotionSettings(int HandsMs, int BodyMs, bool Bounce, int Strength, bool TypingPriority, int ExpressionSeconds, int PatSeconds)
{
    public static MotionSettings Capture(DesktopOptions d) => new(d.TypingReactionMs, d.BodyReactionMs, d.TypingBounce, d.BounceStrength, d.TypingPriority, d.ExpressionSeconds, d.PatSeconds);
    public void Apply(DesktopOptions d)
    {
        d.TypingReactionMs = HandsMs;
        d.BounceReactionMs = BodyMs;
        d.TypingBounce = Bounce;
        d.BounceStrength = Strength;
        d.TypingPriority = TypingPriority;
        d.ExpressionSeconds = ExpressionSeconds;
        d.PatSeconds = PatSeconds;
        d.Normalize();
    }
}
