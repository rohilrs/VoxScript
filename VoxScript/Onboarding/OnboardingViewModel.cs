using CommunityToolkit.Mvvm.ComponentModel;
using VoxScript.Core.Settings;

namespace VoxScript.Onboarding;

public enum OnboardingStep
{
    Welcome = 0,
    MicPick = 1,
    ModelPick = 2,
    Hotkeys = 3,
    TryIt = 4,
    Final = 5,
}

public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly AppSettings _settings;

    private readonly HashSet<OnboardingStep> _gatedSteps = new()
    {
        OnboardingStep.MicPick,
        OnboardingStep.ModelPick,
        OnboardingStep.TryIt,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    [NotifyPropertyChangedFor(nameof(ProgressValue))]
    [NotifyPropertyChangedFor(nameof(StepLabel))]
    public partial OnboardingStep CurrentStep { get; private set; }

    public bool CanGoBack => CurrentStep != OnboardingStep.Welcome;

    public bool CanGoNext => !_gatedSteps.Contains(CurrentStep);

    public double ProgressValue => ((int)CurrentStep + 1) / 6.0;

    public string StepLabel => $"Step {(int)CurrentStep + 1} of 6";

    public event Action? WizardCompleted;

    public OnboardingViewModel(AppSettings settings)
    {
        _settings = settings;
        CurrentStep = OnboardingStep.Welcome;
    }

    public void UnlockMicStep() => SetStepGated(OnboardingStep.MicPick, false);
    public void UnlockModelStep() => SetStepGated(OnboardingStep.ModelPick, false);
    public void UnlockTryItStep() => SetStepGated(OnboardingStep.TryIt, false);

    public void SetStepGated(OnboardingStep step, bool gated)
    {
        if (gated) _gatedSteps.Add(step);
        else _gatedSteps.Remove(step);
        OnPropertyChanged(nameof(CanGoNext));
    }

    public void GoNext()
    {
        if (!CanGoNext) return;
        if (CurrentStep == OnboardingStep.Final) return;
        CurrentStep = (OnboardingStep)((int)CurrentStep + 1);
    }

    public void GoBack()
    {
        if (!CanGoBack) return;
        CurrentStep = (OnboardingStep)((int)CurrentStep - 1);
    }

    public void Finish()
    {
        _settings.OnboardingCompleted = true;
        WizardCompleted?.Invoke();
    }
}
