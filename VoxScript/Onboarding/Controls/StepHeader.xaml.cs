using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoxScript.Onboarding.Controls;

public sealed partial class StepHeader : UserControl
{
    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(StepHeader),
            new PropertyMetadata(0.0, (d, e) =>
                ((StepHeader)d).BarControl.Value = (double)e.NewValue));

    public static readonly DependencyProperty StepTextProperty =
        DependencyProperty.Register(nameof(StepText), typeof(string), typeof(StepHeader),
            new PropertyMetadata(string.Empty, (d, e) =>
                ((StepHeader)d).StepLabelText.Text = (string)(e.NewValue ?? string.Empty)));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string StepText
    {
        get => (string)GetValue(StepTextProperty);
        set => SetValue(StepTextProperty, value);
    }

    public StepHeader() => InitializeComponent();
}
