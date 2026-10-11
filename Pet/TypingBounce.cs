using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KeyboardWorker;

public sealed class TypingBounce : IDisposable
{
    readonly FrameworkElement target;
    readonly RotateTransform tilt = new();
    readonly TranslateTransform offset = new();
    long version;
    public bool Enabled { get; private set; } = true;
    public bool Active { get; private set; }
    public bool LowPower { get; set; }
    public double VerticalOffset => offset.Y;
    public double Angle => tilt.Angle;
    public TypingBounce(FrameworkElement target)
    {
        this.target = target; var transforms = new TransformGroup(); transforms.Children.Add(tilt); transforms.Children.Add(offset);
        target.RenderTransformOrigin = new Point(.5, .85); target.RenderTransform = transforms;
    }
    public void Configure(bool enabled) { Enabled = enabled; if (!enabled) Reset(); }
    static DoubleAnimationUsingKeyFrames Frames(double start, params (int Time, double Value)[] points)
    {
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(240), FillBehavior = FillBehavior.HoldEnd };
        animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(start, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        foreach (var point in points) animation.KeyFrames.Add(new SplineDoubleKeyFrame(point.Value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(point.Time)), new KeySpline(.25, .6, .35, 1)));
        return animation;
    }
    public void Tap(bool left, int strength)
    {
        if (!Enabled) return;
        double gain = Math.Clamp(strength, 1, 10) / 5.0, direction = left ? -1 : 1; long current = ++version; Active = true;
        var jump = Frames(Math.Clamp(offset.Y, -12, 3), (28, .8 * gain), (75, -4.5 * gain), (155, .7 * gain), (240, 0));
        Timeline.SetDesiredFrameRate(jump, LowPower ? 15 : 30);
        jump.Completed += (_, _) => { if (current == version) Reset(); };
        tilt.BeginAnimation(RotateTransform.AngleProperty, Frames(tilt.Angle, (35, direction * .75 * gain), (85, direction * 1.1 * gain), (155, -direction * .3 * gain), (240, 0)), HandoffBehavior.SnapshotAndReplace);
        offset.BeginAnimation(TranslateTransform.YProperty, jump, HandoffBehavior.SnapshotAndReplace);
    }
    public void Reset()
    {
        version++; Active = false;
        tilt.BeginAnimation(RotateTransform.AngleProperty, null); offset.BeginAnimation(TranslateTransform.YProperty, null);
        tilt.Angle = 0; offset.Y = 0;
    }
    public void Dispose() { Reset(); target.RenderTransform = Transform.Identity; }
}
