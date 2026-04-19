using Microsoft.UI.Xaml.Controls;
using VoxScript.Core.Settings;

namespace VoxScript.Onboarding.Steps;

public sealed partial class FinalStepView : UserControl
{
    private readonly AppSettings _settings;

    public FinalStepView(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LaunchAtLoginToggle.IsOn = _settings.LaunchAtLogin;
    }

    // Called by OnboardingView when the Finish button is clicked, before
    // OnboardingViewModel.Finish() fires. Writes the toggle state to settings so
    // Finish() can read it and apply the registry entry.
    internal void CommitToggle()
    {
        _settings.LaunchAtLogin = LaunchAtLoginToggle.IsOn;
    }
}
