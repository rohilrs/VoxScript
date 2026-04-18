using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoxScript.Onboarding.Controls;

public sealed partial class LevelMeter : UserControl
{
    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(LevelMeter),
            new PropertyMetadata(0.0, (d, e) => ((LevelMeter)d).UpdateFill()));

    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    public LevelMeter()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateFill();
    }

    private void UpdateFill()
    {
        var clamped = Math.Clamp(Level, 0.0, 1.0);
        FillRect.Width = clamped * ActualWidth;
    }
}
